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
    public sealed class SlicerSpace : MonoBehaviour, ISpaceModule, ISpaceStateReader, IUploadStep
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
        int m_GazeCount;

        JobHandle m_SliceHandle, m_ShadowHandle;
        bool m_SlicePending, m_ShadowPending;

        // What the meshes on screen were built from; a frame that changes none of it cuts nothing.
        bool m_SliceValid, m_ShadowValid;
        float m_SlicedOffset, m_SlicedTilt;
        float4x4 m_SlicedRotation, m_ShadowRotation;

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
        }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            Release(true);

            m_Params = SlicerParams.Declare(registry);
            m_Layout = SlicerLayout.FromRoom(room.FreeRect.Wf, room.FreeRect.Df, room.Standing);
            m_Controller = new SlicerController(in m_Layout);

            AtlasServices.TryGet(out m_Pool);
            AtlasServices.TryGet(out m_Input);
            AtlasServices.TryGet(out m_Host);
            AtlasServices.TryGet(out m_Events);

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
                    if (m_Input.Pressed(AtlasAction.Primary)) m_Params.SetObject(SlicerObjects.Next(m_Params.ObjectValue));
                    if (m_Input.Pressed(AtlasAction.Secondary))
                        m_Params.SetView(m_Params.ViewValue == SlicerView.Slice ? SlicerView.Shadow : SlicerView.Slice);
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
            }

            if (m_Buffers == null) return;
            ScheduleJobs(body, tilt);
            MoveMarkers();
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
                    Light = Atlas.Rendering.AtlasShading.LightDirection,
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
                    Light = Atlas.Rendering.AtlasShading.LightDirection,
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
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftY, +1f, m_Layout.CutHandle(+1), radius), "slicer.w+");
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftY, -1f, m_Layout.CutHandle(-1), radius), "slicer.w-");
            // The turns: one pair of handles per plane.
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftX, +1f, m_Layout.TurnHandle(0, +1), radius), "slicer.xw+");
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.LeftX, -1f, m_Layout.TurnHandle(0, -1), radius), "slicer.xw-");
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.RightY, +1f, m_Layout.TurnHandle(1, +1), radius), "slicer.yw+");
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.RightY, -1f, m_Layout.TurnHandle(1, -1), radius), "slicer.yw-");
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.Twist, +1f, m_Layout.TurnHandle(2, +1), radius), "slicer.zw+");
            AddGaze(GazeTarget.HoldAxis(AtlasAxis.Twist, -1f, m_Layout.TurnHandle(2, -1), radius), "slicer.zw-");
            // Next body, and shadow on and off: the two chips beside the bar. The shadow is not put on
            // looking down, because a walker on the lane looks at the floor all the time.
            AddGaze(GazeTarget.World(AtlasAction.Primary, m_Layout.NextObjectChip, radius), "slicer.next");
            AddGaze(GazeTarget.World(AtlasAction.Secondary, m_Layout.ViewChip, radius), "slicer.shadow");
        }

        void AddGaze(GazeTarget target, string label)
        {
            target.Label = label;
            GazeTargetId id = m_Input.RegisterGazeTarget(in target);
            if (id.IsValid && m_GazeCount < m_Gaze.Length) m_Gaze[m_GazeCount++] = id;
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
