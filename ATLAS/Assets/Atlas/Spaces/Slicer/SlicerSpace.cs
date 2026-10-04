using Atlas.Input;
using Atlas.Kernel;
using Atlas.Kernel.FourD;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// Space S6, the Slicer (Master Plan sections 3.1 and 5; task P1-A12): a four-dimensional body floats in
    /// the walker's own room and answers to the three verbs. Slice: walking the lane on the floor, or the
    /// stick, moves the cut along w. Shadow: the body flattened along w, drawn as a ghost round the slice.
    /// Rotate: the sticks, or dwelling on a handle, turn the body in the planes that contain w.
    /// </summary>
    /// <remarks>
    /// The component is a thin adapter (README F2): <see cref="SlicerController"/> holds the state and the
    /// rules, <see cref="SlicerMeshKernel"/> makes the vertices, and the two Burst jobs run them into meshes
    /// with fixed native buffers. Nothing is cut while neither the cut nor the body moves.
    /// </remarks>
    public sealed class SlicerSpace : MonoBehaviour, ISpaceModule, ISpaceStateReader, IUploadStep, ISlicerReadings
    {
        /// <summary>Opacity of the shadow: dithered, so the slice shows through it.</summary>
        const float ShadowOpacity = 0.3f;
        const int GazeCount = 10;

        static readonly ProfilerMarker s_SliceMarker = new ProfilerMarker("Atlas.Slicer4D");
        static readonly ProfilerMarker s_UploadMarker = new ProfilerMarker("Atlas.Slicer4D.Upload");

        SlicerLayout m_Layout;
        SlicerController m_Controller;
        SlicerParams m_Params;
        SlicerBuffers m_Buffers;

        MaterialPool m_Pool;
        IAtlasInput m_Input;
        ISpaceHost m_Host;
        IEventLog m_Events;

        Material m_SliceMaterial, m_ShadowMaterial, m_FixtureMaterial;
        Mesh m_SliceMesh, m_ShadowMesh, m_FixtureMesh, m_MarkerMesh;
        MeshFilter m_SliceFilter, m_ShadowFilter;
        MeshRenderer m_SliceRenderer, m_ShadowRenderer;
        Transform m_BarMarker, m_LaneMarker;

        readonly GazeTargetId[] m_Gaze = new GazeTargetId[GazeCount];
        readonly int[] m_GazeButton = new int[GazeCount];      // the button each gaze target belongs to
        int m_GazeCount;

        JobHandle m_SliceHandle, m_ShadowHandle;
        bool m_SlicePending, m_ShadowPending;

        // What the meshes on screen were built from; a frame that changes none of it cuts nothing.
        bool m_SliceValid, m_ShadowValid;
        float m_SlicedOffset, m_SlicedTilt;
        float4x4 m_SlicedRotation, m_ShadowRotation;

        // The toybox (tasks P2-A01, P2-A02): the slice's name, the look-down shadow, the guided steps, the events and the panel.
        SlicerToyboxParams m_Toybox;
        readonly SlicerSection m_Section = new SlicerSection();
        readonly SlicerShapeStrip m_Strip = new SlicerShapeStrip();
        readonly LookDownShadow m_Look = new LookDownShadow();
        readonly SlicerTour m_Tour = new SlicerTour();
        SlicerEvents m_EventRules = new SlicerEvents(null);
        SlicerPanel m_Panel;
        SlicerButtons m_Buttons;
        SectionInfo m_SectionInfo;
        bool m_SectionValid;
        float m_NamedOffset, m_NamedTilt;
        float3 m_NamedTurns;
        int m_Active;

        SlicerView m_View;
        bool m_Entered;
        bool m_Mirrored;
        float m_MirrorTilt;

        public SpaceId Id { get { return SpaceId.Slicer; } }
        /// <summary>Home stays as it is; the body floats in it.</summary>
        public bool Registered { get { return true; } }

        /// <summary>The state and its rules, for tests and the equation card.</summary>
        public SlicerController Controller { get { return m_Controller; } }
        /// <summary>The body on screen.</summary>
        public SlicerObject Body { get { return m_Buffers != null ? m_Buffers.Body : SlicerObject.Tesseract; } }
        /// <summary>Triangles of the cross-section last uploaded.</summary>
        public int SliceTriangles { get; private set; }
        /// <summary>What the slice on screen is (task P2-A02).</summary>
        public SectionInfo Section { get { return m_SectionInfo; } }
        public float W { get { return m_Controller != null ? m_Controller.W : 0f; } }
        public float Xw { get { return m_Controller != null ? m_Controller.Xw : 0f; } }
        public float Yw { get { return m_Controller != null ? m_Controller.Yw : 0f; } }
        public float Zw { get { return m_Controller != null ? m_Controller.Zw : 0f; } }
        /// <summary>The guided steps (task P2-A01).</summary>
        public SlicerTour Tour { get { return m_Tour; } }
        /// <summary>True while the shadow is shown because the walker looks down.</summary>
        public bool PeekingShadow { get { return m_Look.Peeking; } }
        /// <summary>The view on screen: the shadow view, or a look-down peek, shows the shadow.</summary>
        public SlicerView ShownView { get { return m_View; } }
        /// <summary>The panel's text, or null when the build has no TextMeshPro resources.</summary>
        public string PanelText { get { return m_Panel != null && m_Panel.HasText ? m_Panel.CardText : null; } }
        /// <summary>The shapes along w on the info card.</summary>
        public SlicerShapeStrip Strip { get { return m_Strip; } }

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene, with the module.</summary>
        public static ISpaceModule Create()
        {
            var go = new GameObject(SpaceIds.SceneName(SpaceId.Slicer));
            return go.AddComponent<SlicerSpace>();
        }

        /// <summary>Declares the parameters of group "slicer".</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            SlicerParams.Declare(registry);
            SlicerToyboxParams.Declare(registry);
        }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            Release(true);

            m_Params = SlicerParams.Declare(registry);
            m_Toybox = SlicerToyboxParams.Declare(registry);
            m_Layout = SlicerLayout.FromRoom(room.FreeRect.Wf, room.FreeRect.Df, room.Standing);
            m_Controller = new SlicerController(in m_Layout);

            AtlasServices.TryGet(out m_Pool);
            AtlasServices.TryGet(out m_Input);
            AtlasServices.TryGet(out m_Host);
            AtlasServices.TryGet(out m_Events);
            m_EventRules = new SlicerEvents(m_Events);

            if (m_Pool != null)
            {
                m_SliceMaterial = m_Pool.Rent();
                MaterialPool.SetCull(m_SliceMaterial, CullMode.Off);
                m_ShadowMaterial = m_Pool.Rent();
                MaterialPool.SetCull(m_ShadowMaterial, CullMode.Off);
                MaterialPool.SetTint(m_ShadowMaterial, new float3(1f, 1f, 1f), ShadowOpacity);
                m_FixtureMaterial = m_Pool.Rent();
                MaterialPool.SetCull(m_FixtureMaterial, CullMode.Off);
            }

            m_SliceRenderer = CreateRenderer("Slice", m_SliceMaterial, out m_SliceFilter);
            m_ShadowRenderer = CreateRenderer("Shadow", m_ShadowMaterial, out m_ShadowFilter);
            BuildFixtures();
            m_Panel = new SlicerPanel(transform, in m_Layout, m_FixtureMaterial);
            m_Buttons = new SlicerButtons(transform, in m_Layout, m_FixtureMaterial);
            Bind(m_Params.ObjectValue);
        }

        public void Enter(in WalkerPose start)
        {
            // Registered: the module's root is the room frame itself (README F4).
            Transform root = transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            m_Params.Load(m_Controller);
            m_View = m_Params.ViewValue;
            m_SliceValid = false;
            m_ShadowValid = false;
            m_Mirrored = false;
            m_Look.Reset();
            m_Tour.Restart();
            m_EventRules.Reset();
            m_SectionValid = false;
            if (m_Panel != null) m_Panel.SetVisible(true);
            if (m_Buttons != null) m_Buttons.SetVisible(true);
            RegisterGaze();
            m_Entered = true;
        }

        public void Tick(in FrameContext frame)
        {
            if (!m_Entered) return;

            SlicerObject body;
            float tilt;
            if (m_Mirrored)
            {
                // The Spectator: the state came with ReadState; no input and no parameter is consulted.
                body = Body;
                tilt = m_MirrorTilt;
            }
            else
            {
                if (m_Input != null)
                {
                    if (m_Input.Pressed(AtlasAction.Primary))
                    {
                        m_Params.SetObject(SlicerObjects.Next(m_Params.ObjectValue));
                        if (m_Buttons != null) m_Buttons.Flash(SlicerButtons.Next);
                    }
                    if (m_Input.Pressed(AtlasAction.Secondary))
                    {
                        m_Params.SetView(m_Params.ViewValue == SlicerView.Slice ? SlicerView.Shadow : SlicerView.Slice);
                        if (m_Buttons != null) m_Buttons.Flash(SlicerButtons.Shadow);
                        if (!m_Look.Peeking) m_EventRules.Shadow(m_Params.ViewValue == SlicerView.Shadow, false);
                    }
                }

                m_Params.TakeOutsideChanges(m_Controller);
                SlicerInput input = default;
                input.Floor = frame.HeadPosition.xz;
                input.DeltaTime = frame.DeltaTime;
                if (m_Input != null)
                {
                    input.AxisW = m_Input.Axis(AtlasAxis.LeftY);
                    input.AxisXw = m_Input.Axis(AtlasAxis.LeftX);
                    input.AxisYw = m_Input.Axis(AtlasAxis.RightY);
                    input.AxisZw = m_Input.Axis(AtlasAxis.Twist);
                }
                m_Controller.Step(in input);
                m_Params.Store(m_Controller);

                body = m_Params.ObjectValue;
                m_View = m_Params.ViewValue;
                tilt = m_Params.TiltRadians;
                if (body != Body) ChangeBody(body);

                // Looking down while standing still shows the shadow for as long as the walker looks (task P2-A02).
                bool lookDown = m_Toybox == null || m_Toybox.LookDownEnabled;
                if (m_Look.Step(frame.HeadPosition, frame.HeadRotation, frame.DeltaTime, lookDown) && m_View == SlicerView.Slice)
                    m_EventRules.Shadow(m_Look.Peeking, true);
                if (m_Look.Peeking) m_View = SlicerView.Shadow;
                m_Active = ActiveLabels(in input);
            }

            if (m_Buffers == null) return;
            NameSection(body, tilt);
            if (!m_Mirrored)
            {
                if (m_Toybox == null || m_Toybox.TourShown)
                {
                    var step = new SlicerTourInput
                    {
                        Body = body, Section = m_SectionInfo.Shape, W = m_Controller.W,
                        Xw = m_Controller.Xw, Yw = m_Controller.Yw, Zw = m_Controller.Zw,
                        ShadowShown = m_View == SlicerView.Shadow, DeltaTime = frame.DeltaTime,
                    };
                    if (m_Tour.Step(in step)) m_EventRules.Task(m_Tour.Task);
                }
                m_EventRules.Step(frame.Time, m_Controller.W, m_Controller.Xw, m_Controller.Yw, m_Controller.Zw, in m_SectionInfo, body);
            }
            ScheduleJobs(body, tilt);
            MoveMarkers();
        }

        // The slice's name, from the body's own structure (task P2-A02). Recomputed only when the cut or the turns moved: a few
        // microseconds for the largest polytope, nothing for the smooth bodies, no allocation.
        void NameSection(SlicerObject body, float tilt)
        {
            SlicerSource source = m_Mirrored || m_Params == null ? SlicerSource.Live : m_Params.SourceValue;
            float offset = m_Controller.CutOffset(source);
            var turns = new float3(m_Controller.Xw, m_Controller.Yw, m_Controller.Zw);
            if (m_Section.Body == body) m_Strip.Update(m_Section, turns.x, turns.y, turns.z, tilt);
            if (m_SectionValid && offset == m_NamedOffset && tilt == m_NamedTilt && turns.Equals(m_NamedTurns) && m_Section.Body == body) return;
            m_SectionInfo = m_Section.Name(turns.x, turns.y, turns.z, offset, tilt);
            m_SectionValid = true;
            m_NamedOffset = offset;
            m_NamedTilt = tilt;
            m_NamedTurns = turns;
        }

        // One bit per panel label whose control acts this frame: a stick, or a dwell on its gaze handle (the same axes).
        static int ActiveLabels(in SlicerInput input)
        {
            int bits = 0;
            bits |= Bit(input.AxisW, 0, 1);
            bits |= Bit(input.AxisXw, 3, 2);
            bits |= Bit(input.AxisYw, 5, 4);
            bits |= Bit(input.AxisZw, 7, 6);
            return bits;
        }

        static int Bit(float axis, int plusLabel, int minusLabel)
        {
            float a = SlicerController.DeadZone(axis);
            return a > 0f ? 1 << plusLabel : a < 0f ? 1 << minusLabel : 0;
        }

        /// <summary>Step 6: takes this frame's jobs and uploads what they made. No allocation.</summary>
        public void Upload(in FrameContext frame)
        {
            if (m_Buffers == null) return;
            if (m_SlicePending)
            {
                s_UploadMarker.Begin();
                m_SliceHandle.Complete();
                m_SlicePending = false;
                int vertexCount = m_Buffers.SliceCounts[0];
                SliceTriangles = m_Buffers.SliceCounts[1];
                if (m_SliceMesh != null)
                    AtlasMesh.Upload(m_SliceMesh, m_Buffers.SliceVertices, vertexCount, m_Buffers.Indices, vertexCount,
                                     ToBounds(m_Buffers.SliceBounds[0], m_Buffers.SliceBounds[1]));
                s_UploadMarker.End();
            }
            if (m_ShadowPending)
            {
                m_ShadowHandle.Complete();
                m_ShadowPending = false;
                int vertexCount = m_Buffers.ShadowTriangleCount * 3;
                if (m_ShadowMesh != null)
                    AtlasMesh.Upload(m_ShadowMesh, m_Buffers.ShadowVertices, vertexCount, m_Buffers.Indices, vertexCount,
                                     ToBounds(m_Buffers.ShadowBounds[0], m_Buffers.ShadowBounds[1]));
            }
            if (m_ShadowRenderer != null)
            {
                bool show = m_View == SlicerView.Shadow && m_ShadowValid;
                if (m_ShadowRenderer.enabled != show) m_ShadowRenderer.enabled = show;
            }
            if (m_Panel != null && m_Controller != null)
            {
                var state = new SlicerReadoutState
                {
                    Body = Body, Section = m_SectionInfo, W = m_Controller.W,
                    Xw = m_Controller.Xw, Yw = m_Controller.Yw, Zw = m_Controller.Zw,
                    View = m_Mirrored || m_Params == null ? m_View : m_Params.ViewValue, Peeking = m_Look.Peeking,
                    Task = m_Tour.Task, ShowTour = !m_Mirrored && (m_Toybox == null || m_Toybox.TourShown), Strip = m_Strip,
                };
                m_Panel.Update(in state, frame.HeadPosition);
            }
            if (m_Buttons != null && m_Controller != null)
            {
                int focused = -1;
                float progress = 0f;
                if (m_Input != null && !m_Mirrored)
                {
                    GazeState gaze = m_Input.Gaze;
                    for (int i = 0; i < m_GazeCount; i++)
                    {
                        if (!m_Gaze[i].Equals(gaze.Focused)) continue;
                        focused = m_GazeButton[i];
                        progress = gaze.Progress;
                        break;
                    }
                }
                bool shadow = (m_Mirrored || m_Params == null ? m_View : m_Params.ViewValue) == SlicerView.Shadow;
                m_Buttons.Update(frame.HeadPosition, m_Mirrored ? 0 : m_Active, focused, progress,
                                 new float3(m_Controller.Xw, m_Controller.Yw, m_Controller.Zw), shadow, frame.DeltaTime);
            }
        }

        public void Exit()
        {
            m_Entered = false;
            Release(true);
        }

        public void WriteState(ref StateMessage state)
        {
            if (m_Controller == null) return;
            state.P3 = m_Controller.W;
            state.P4 = m_Controller.Xw;
            state.P5 = m_Controller.Yw;
            state.P6 = m_Controller.Zw;
            state.P7 = SlicerState.Pack(Body, m_View);
        }

        /// <summary>The Spectator's side: the body, the view, the cut and the turns of the phone's frame.</summary>
        public void ReadState(in StateMessage state)
        {
            if (m_Controller == null) return;
            m_Mirrored = true;
            m_MirrorTilt = m_Params != null ? m_Params.TiltRadians : 0f;
            m_Controller.Reset(state.P3, state.P4, state.P5, state.P6);
            SlicerState.Unpack(state.P7, out SlicerObject body, out m_View);
            if (body != Body) ChangeBody(body);
        }

        void OnDestroy()
        {
            // Exit has normally done all of this; a root destroyed without it must still free native memory.
            // Its children are going with it.
            Release(false);
        }

        // ---- the body -----------------------------------------------------------------------------------------

        // A change of body regenerates geometry (slicer.object is an on-release parameter): it allocates, off
        // the steady frame path, and the Spectator is told through the world version.
        void ChangeBody(SlicerObject body)
        {
            Bind(body);
            m_SectionValid = false;
            if (m_Host != null) m_Host.BumpWorldVersion();
            if (m_Events != null) m_Events.Log("slicer.object", "object", SlicerObjects.Name(body));
        }

        void Bind(SlicerObject body)
        {
            CompleteJobs();
            if (m_Buffers != null) m_Buffers.Dispose();
            DestroyAny(m_SliceMesh);
            DestroyAny(m_ShadowMesh);

            m_Buffers = new SlicerBuffers(body);
            m_Section.Bind(body);
            m_Strip.Invalidate();
            m_SectionValid = false;
            m_SliceMesh = AtlasMesh.CreateDynamic("Slicer Slice", m_Buffers.SliceCapacity, m_Buffers.SliceCapacity);
            m_ShadowMesh = AtlasMesh.CreateDynamic("Slicer Shadow", m_Buffers.ShadowCapacity, m_Buffers.ShadowCapacity);
            if (m_SliceFilter != null) m_SliceFilter.sharedMesh = m_SliceMesh;
            if (m_ShadowFilter != null) m_ShadowFilter.sharedMesh = m_ShadowMesh;
            if (m_ShadowRenderer != null) m_ShadowRenderer.enabled = false;
            m_SliceValid = false;
            m_ShadowValid = false;
            SliceTriangles = 0;
        }

        void ScheduleJobs(SlicerObject body, float tilt)
        {
            SlicerSource source = m_Mirrored || m_Params == null ? SlicerSource.Live : m_Params.SourceValue;
            float offset = m_Controller.CutOffset(source);
            m_Controller.GetTransform(body, out float4x4 rotation, out float4 translation);

            if (!m_SlicePending &&
                (!m_SliceValid || offset != m_SlicedOffset || tilt != m_SlicedTilt || !rotation.Equals(m_SlicedRotation)))
            {
                var job = new SlicerSliceJob
                {
                    Vertices = m_Buffers.Vertices,
                    Tetrahedra = m_Buffers.Tetrahedra,
                    TetCell = m_Buffers.TetCell,
                    CellUv = m_Buffers.CellUv,
                    ModelToSlice = rotation,
                    Translation = translation,
                    Normal = SlicerController.CutNormal(body, tilt),
                    Offset = offset,
                    Centre = m_Layout.Centre,
                    Scale = m_Layout.Radius,
                    Light = Atlas.Kernel.AtlasShading.LightDirection,
                    ScratchPositions = m_Buffers.ScratchPositions,
                    ScratchNormals = m_Buffers.ScratchNormals,
                    ScratchCells = m_Buffers.ScratchCells,
                    OutVertices = m_Buffers.SliceVertices,
                    OutBounds = m_Buffers.SliceBounds,
                    OutCounts = m_Buffers.SliceCounts,
                    Marker = s_SliceMarker,
                };
                m_SliceHandle = job.Schedule();
                m_SlicePending = true;
                m_SliceValid = true;
                m_SlicedOffset = offset;
                m_SlicedTilt = tilt;
                m_SlicedRotation = rotation;
            }

            // The shadow does not depend on the cut: it is rebuilt only when the body turns, and only while shown.
            if (m_View == SlicerView.Shadow && !m_ShadowPending &&
                (!m_ShadowValid || !rotation.Equals(m_ShadowRotation)))
            {
                var job = new SlicerShadowJob
                {
                    Vertices = m_Buffers.Vertices,
                    Triangles = m_Buffers.ShadowTriangles,
                    TriangleCount = m_Buffers.ShadowTriangleCount,
                    ModelToSlice = rotation,
                    Translation = translation,
                    Perspective = SlicerObjects.PerspectiveShadow(body),
                    Focal = Projector4D.DefaultFocal,
                    Centre = m_Layout.Centre,
                    Scale = m_Layout.Radius,
                    Light = Atlas.Kernel.AtlasShading.LightDirection,
                    OutVertices = m_Buffers.ShadowVertices,
                    OutBounds = m_Buffers.ShadowBounds,
                };
                // Both jobs read the body's vertices; they write different arrays and may run side by side.
                m_ShadowHandle = job.Schedule();
                m_ShadowPending = true;
                m_ShadowValid = true;
                m_ShadowRotation = rotation;
            }

            // Jobs scheduled here are completed in Upload, later in the same frame. Start them now.
            if (m_SlicePending || m_ShadowPending) JobHandle.ScheduleBatchedJobs();
        }

        void CompleteJobs()
        {
            if (m_SlicePending) m_SliceHandle.Complete();
            if (m_ShadowPending) m_ShadowHandle.Complete();
            m_SlicePending = false;
            m_ShadowPending = false;
        }

        // ---- still content ------------------------------------------------------------------------------------

        void BuildFixtures()
        {
            var builder = new AtlasMeshBuilder(512, 1024);
            SlicerFixtures.Build(in m_Layout, builder);
            m_FixtureMesh = AtlasMesh.Create(builder, "Slicer Fixtures");
            CreateRenderer("Fixtures", m_FixtureMaterial, out MeshFilter fixtures);
            fixtures.sharedMesh = m_FixtureMesh;

            builder.Clear();
            SlicerFixtures.BuildMarker(builder);
            m_MarkerMesh = AtlasMesh.Create(builder, "Slicer Marker");
            MeshRenderer bar = CreateRenderer("Cut On Bar", m_FixtureMaterial, out MeshFilter barFilter);
            barFilter.sharedMesh = m_MarkerMesh;
            m_BarMarker = bar.transform;
            m_LaneMarker = null;
            if (m_Layout.HasLane)
            {
                MeshRenderer lane = CreateRenderer("Cut On Lane", m_FixtureMaterial, out MeshFilter laneFilter);
                laneFilter.sharedMesh = m_MarkerMesh;
                m_LaneMarker = lane.transform;
                // Turned a quarter so that its long side lies across the lane.
                m_LaneMarker.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            MoveMarkers();
        }

        void MoveMarkers()
        {
            float w = m_Controller.W;
            if (m_BarMarker != null) m_BarMarker.localPosition = m_Layout.BarPoint(w);
            if (m_LaneMarker != null)
                m_LaneMarker.localPosition = new Vector3(m_Layout.LaneX, 2f * SlicerFixtures.LaneLift, m_Layout.LaneZ(w));
        }

        MeshRenderer CreateRenderer(string childName, Material material, out MeshFilter filter)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            filter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // Without a pooled material there is nothing to draw with (a test with no look installed).
            meshRenderer.enabled = material != null;
            return meshRenderer;
        }

        // ---- gaze twins (Master Plan section 0.5 rule 5) --------------------------------------------------------

        void RegisterGaze()
        {
            UnregisterGaze();
            if (m_Input == null) return;
            float radius = SlicerLayout.HandleRadius;

            // The cut: the two ends of the bar are the twin of the stick.
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftY, +1f, m_Layout.CutHandle(+1), radius), "slicer.w+", 0);
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftY, -1f, m_Layout.CutHandle(-1), radius), "slicer.w-", 1);
            // The turns: one pair of handles per plane.
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftX, +1f, m_Layout.TurnHandle(0, +1), radius), "slicer.xw+", 3);
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftX, -1f, m_Layout.TurnHandle(0, -1), radius), "slicer.xw-", 2);
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.RightY, +1f, m_Layout.TurnHandle(1, +1), radius), "slicer.yw+", 5);
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.RightY, -1f, m_Layout.TurnHandle(1, -1), radius), "slicer.yw-", 4);
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.Twist, +1f, m_Layout.TurnHandle(2, +1), radius), "slicer.zw+", 7);
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.Twist, -1f, m_Layout.TurnHandle(2, -1), radius), "slicer.zw-", 6);
            // Next body, and shadow on and off: the two chips beside the bar. The shadow is not put on
            // looking down, because a walker on the lane looks at the floor all the time.
            AddGaze(GazeTarget.World(AtlasAction.Primary, m_Layout.NextObjectChip, radius), "slicer.next", SlicerButtons.Next);
            AddGaze(GazeTarget.World(AtlasAction.Secondary, m_Layout.ViewChip, radius), "slicer.shadow", SlicerButtons.Shadow);
        }

        void AddGaze(GazeTarget target, string label, int button)
        {
            target.Label = label;
            GazeTargetId id = m_Input.RegisterGazeTarget(in target);
            if (!id.IsValid || m_GazeCount >= m_Gaze.Length) return;
            m_GazeButton[m_GazeCount] = button;
            m_Gaze[m_GazeCount++] = id;
        }

        void UnregisterGaze()
        {
            if (m_Input != null)
                for (int i = 0; i < m_GazeCount; i++) m_Input.UnregisterGazeTarget(m_Gaze[i]);
            m_GazeCount = 0;
        }

        // ---- teardown -----------------------------------------------------------------------------------------

        void Release(bool destroyChildren)
        {
            CompleteJobs();
            UnregisterGaze();
            if (m_Panel != null) m_Panel.Dispose();
            m_Panel = null;
            if (m_Buttons != null) m_Buttons.Dispose();
            m_Buttons = null;
            if (m_Buffers != null)
            {
                m_Buffers.Dispose();
                m_Buffers = null;
            }
            if (m_Pool != null)
            {
                if (m_SliceMaterial != null) m_Pool.Return(m_SliceMaterial);
                if (m_ShadowMaterial != null) m_Pool.Return(m_ShadowMaterial);
                if (m_FixtureMaterial != null) m_Pool.Return(m_FixtureMaterial);
            }
            m_SliceMaterial = null;
            m_ShadowMaterial = null;
            m_FixtureMaterial = null;

            DestroyAny(m_SliceMesh);
            DestroyAny(m_ShadowMesh);
            DestroyAny(m_FixtureMesh);
            DestroyAny(m_MarkerMesh);
            m_SliceMesh = null;
            m_ShadowMesh = null;
            m_FixtureMesh = null;
            m_MarkerMesh = null;

            // The children go with the root when SpaceLab destroys it; a second Configure builds new ones.
            if (destroyChildren)
                for (int i = transform.childCount - 1; i >= 0; i--) DestroyAny(transform.GetChild(i).gameObject);
            m_SliceFilter = null;
            m_ShadowFilter = null;
            m_SliceRenderer = null;
            m_ShadowRenderer = null;
            m_BarMarker = null;
            m_LaneMarker = null;
            m_SliceValid = false;
            m_ShadowValid = false;
        }

        static Bounds ToBounds(float3 min, float3 max)
        {
            return new Bounds(0.5f * (min + max), max - min);
        }

        static void DestroyAny(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
