using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Atlas.Rendering
{
    /// <summary>
    /// Stencil portals between rooms authored in the room frame (Master Plan D16, task P1-A10). The room
    /// the walker is in is drawn by the renderer's own pass on layer Default. Each visible doorway gets a
    /// stencil reference, largest first; its mask marks the doorway and resets the depth inside it, and the
    /// room behind it is moved to that reference's layer, where a Render Objects feature draws it with the
    /// stencil test and the surface shader clips it at the doorway's plane. No extra camera, no extra
    /// texture, no recursion: a room seen through a doorway shows no further room.
    /// </summary>
    public sealed class PortalRenderer : IUploadStep
    {
        public const int MaxSlots = PortalStencilTable.MaxSlots;
        /// <summary>The mask shader's file name under Resources.</summary>
        public const string MaskShaderResource = "AtlasPortalMask";

        const int MaxCandidates = 32;
        static readonly float4 k_NoPlane = new float4(0f, 0f, 0f, 1f);

        struct Candidate
        {
            public PortalDoor Door;
            public int Direction;
            public float Area;
            public bool Box;
            public int Reference;
        }

        readonly List<PortalRoom> m_Rooms = new List<PortalRoom>(16);
        readonly List<PortalDoor> m_Doors = new List<PortalDoor>(16);
        readonly PortalSlotAllocator m_Slots = new PortalSlotAllocator();
        readonly PortalSwapRule m_Swap = new PortalSwapRule(16);
        readonly Candidate[] m_Candidates = new Candidate[MaxCandidates];
        readonly Plane[] m_UnityPlanes = new Plane[6];
        readonly float4[] m_LeftPlanes = new float4[6];
        readonly float4[] m_RightPlanes = new float4[6];
        readonly AtlasMeshBuilder m_MeshData = new AtlasMeshBuilder(32, 64);

        // One material per stencil reference and kind of mask; index 0 is unused.
        readonly Material[] m_Mark = new Material[MaxSlots + 1];
        readonly Material[] m_BoxMark = new Material[MaxSlots + 1];
        readonly Material[] m_Reset = new Material[MaxSlots + 1];
        readonly ScriptableRendererFeature[] m_RoomFeatures = new ScriptableRendererFeature[MaxSlots + 1];
        ScriptableRendererFeature m_MaskFeature;
        bool m_FeaturesSearched;
        bool m_MaterialsBuilt;

        readonly Transform m_MaskParent;
        Transform m_MaskRoot;
        int m_CurrentRoom;
        int m_Depth = 1;
        int m_SlotsInUse;
        float3 m_PreviousHead;
        bool m_HasPreviousHead;

        /// <param name="maskParent">Parent of the mask objects, or null for a root object of their own.</param>
        public PortalRenderer(Transform maskParent = null)
        {
            m_MaskParent = maskParent;
        }

        /// <summary>The eye cameras, for their frusta. Null in the Spectator: a doorway then counts as seen
        /// whenever the head is in front of it.</summary>
        public IStereoRig Rig { get; set; }

        public int RoomCount { get { return m_Rooms.Count; } }
        public int DoorCount { get { return m_Doors.Count; } }
        public PortalRoom GetRoom(int room) { return m_Rooms[room]; }
        public PortalDoor GetDoor(int door) { return m_Doors[door]; }

        /// <summary>The room the walker is in. Changed by the renderer when the head crosses a doorway.</summary>
        public int CurrentRoom
        {
            get { return m_CurrentRoom; }
            set
            {
                // Placed from outside: the head did not walk there, so the next frame tests no crossing.
                m_HasPreviousHead = false;
                SetCurrent(value);
            }
        }

        /// <summary>0: doorways show the fog colour. 1: doorways show the next room (portal.depth).</summary>
        public int Depth
        {
            get { return m_Depth; }
            set { m_Depth = math.clamp(value, 0, 1); }
        }

        /// <summary>Stencil references in use this frame: 0 .. 15.</summary>
        public int SlotsInUse { get { return m_SlotsInUse; } }

        /// <summary>True when the renderer features of the project set-up were found.</summary>
        public bool FeaturesFound { get { return m_MaskFeature != null; } }

        /// <summary>Raised when CurrentRoom changes: (old, new).</summary>
        public event Action<int, int> RoomChanged;

        /// <summary>Registers a room: every renderer under root, with materials from the pool. Returns its id (0, 1, ...).</summary>
        public int AddRoom(Transform root, Bounds boundsWorld)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var room = new PortalRoom(m_Rooms.Count, root, boundsWorld);
            m_Rooms.Add(room);
            m_Swap.EnsureCapacity(m_Rooms.Count);
            return room.Id;
        }

        /// <summary>Adds a second root to a room: geometry of that room that lives under another parent.</summary>
        public void AddRoomPart(int room, Transform root)
        {
            m_Rooms[room].AddRoot(root);
        }

        /// <summary>Re-reads the renderers under a room's root after its content changed.</summary>
        public void RefreshRoom(int room)
        {
            if (room >= 0 && room < m_Rooms.Count) m_Rooms[room].Refresh();
        }

        /// <summary>
        /// A doorway between two rooms: a vertical rectangle from floor point a to floor point b, of the given height,
        /// seen from fromRoom with a on the left. Creates both directions. Returns the door id.
        /// </summary>
        public int AddDoor(int fromRoom, int toRoom, float2 a, float2 b, float height)
        {
            if (fromRoom < 0 || fromRoom >= m_Rooms.Count) throw new ArgumentOutOfRangeException(nameof(fromRoom));
            if (toRoom < 0 || toRoom >= m_Rooms.Count) throw new ArgumentOutOfRangeException(nameof(toRoom));
            var door = new PortalDoor(m_Doors.Count, fromRoom, toRoom, a, b, height);
            BuildMasks(door);
            m_Doors.Add(door);
            return door.Id;
        }

        public void SetDoorOpen(int door, bool open)
        {
            m_Doors[door].Open = open;
        }

        /// <summary>Removes every room and door and puts every root back on Default with the clip off.</summary>
        public void Clear()
        {
            for (int k = 0; k < m_Rooms.Count; k++)
            {
                PortalRoom room = m_Rooms[k];
                room.SetOn(true);
                room.SetLayer(PortalStencilTable.WorldLayer);
                room.SetPlane(k_NoPlane);
            }
            m_Rooms.Clear();
            for (int k = 0; k < m_Doors.Count; k++) DestroyMasks(m_Doors[k]);
            m_Doors.Clear();
            if (m_MaskRoot != null) DestroyObject(m_MaskRoot.gameObject);
            m_MaskRoot = null;
            m_Swap.Reset();
            m_Slots.Begin();
            m_SlotsInUse = 0;
            m_CurrentRoom = 0;
            m_HasPreviousHead = false;
            SetFeatures(0, false, false);
        }

        /// <summary>Clear, and the mask materials are destroyed. The renderer can be used again afterwards.</summary>
        public void Dispose()
        {
            Clear();
            for (int n = 1; n <= MaxSlots; n++)
            {
                DestroyObject(m_Mark[n]);
                DestroyObject(m_BoxMark[n]);
                DestroyObject(m_Reset[n]);
                m_Mark[n] = m_BoxMark[n] = m_Reset[n] = null;
            }
            m_MaterialsBuilt = false;
        }

        /// <summary>Step 6: visibility through doorways for both eyes, slots, layers, masks, clip planes, room change.</summary>
        public void Upload(in FrameContext frame)
        {
            if (m_Rooms.Count == 0) return;
            if (m_CurrentRoom < 0 || m_CurrentRoom >= m_Rooms.Count) m_CurrentRoom = 0;

            // The cameras are at the raw head pose, so the doorways are judged from it too.
            float3 head = frame.RawHeadPosition;
            if (m_HasPreviousHead) DetectCrossing(head);
            m_PreviousHead = head;
            m_HasPreviousHead = true;

            IStereoRig rig = Rig;
            bool frusta = rig != null && rig.Mode == DisplayMode.Headset;
            if (frusta)
            {
                ReadPlanes(rig, Eye.Left, m_LeftPlanes);
                ReadPlanes(rig, Eye.Right, m_RightPlanes);
            }

            // Without the renderer features nothing would test the stencil, and rooms that overlap would all
            // be drawn at once: the walker's room is then shown alone, and walking through a doorway still works.
            bool features = m_MaskFeature != null;
            int candidates = features ? FindDoorways(in frame, head, frusta) : 0;
            bool rooms = features && m_Depth > 0;
            AssignSlots(candidates);
            PlaceRooms(candidates, head, frusta, rooms);
            ShowMasks(candidates);

            m_SlotsInUse = m_Slots.Count;
            SetFeatures(m_Slots.Count, candidates > 0, rooms);
        }

        // ---- the frame ------------------------------------------------------------------------------------

        // The current room changes when the head, not an eye, goes through a doorway (Master Plan P1-A10).
        void DetectCrossing(float3 head)
        {
            for (int k = 0; k < m_Doors.Count; k++)
            {
                PortalDoor door = m_Doors[k];
                if (!door.Open) continue;
                for (int direction = 0; direction < 2; direction++)
                {
                    if (door.ViewerRoom(direction) != m_CurrentRoom) continue;
                    float2 left = direction == 0 ? door.A : door.B, right = direction == 0 ? door.B : door.A;
                    if (!PortalMath.Crossed(door.ViewerPlane(direction), left, right, door.Height, m_PreviousHead, head)) continue;
                    SetCurrent(door.TargetRoom(direction));
                    return;
                }
            }
        }

        void SetCurrent(int room)
        {
            int old = m_CurrentRoom;
            if (room == old) return;
            m_CurrentRoom = room;
            Action<int, int> changed = RoomChanged;
            if (changed != null) changed(old, room);
        }

        // Doorways of the walker's room that an eye can see, largest first. Returns their number.
        int FindDoorways(in FrameContext frame, float3 head, bool frusta)
        {
            int count = 0;
            float3 leftEye = frame.LeftEyePosition, rightEye = frame.RightEyePosition;
            for (int k = 0; k < m_Doors.Count && count < MaxCandidates; k++)
            {
                PortalDoor door = m_Doors[k];
                if (!door.Open) continue;
                for (int direction = 0; direction < 2 && count < MaxCandidates; direction++)
                {
                    if (door.ViewerRoom(direction) != m_CurrentRoom) continue;
                    float4 plane = door.ViewerPlane(direction);
                    float side = PortalMath.Side(plane, head);
                    if (side < -PortalMath.BehindTolerance) continue;

                    // The portal box: an eye this close cannot rely on a flat mask, the near plane cuts it.
                    bool box = Straddles(door, plane, leftEye) || Straddles(door, plane, rightEye);
                    float area;
                    if (box) area = PortalMath.FullViewArea;
                    else if (frusta)
                        area = math.max(PortalMath.DoorViewArea(door.A, door.B, door.Height, leftEye, m_LeftPlanes),
                                        PortalMath.DoorViewArea(door.A, door.B, door.Height, rightEye, m_RightPlanes));
                    else
                        area = side > 0f ? door.Width * door.Height / math.max(side * side, 0.01f) : 0f;
                    if (!(area > 1e-6f)) continue;

                    // Insertion keeps the list sorted by area, largest first.
                    int at = count;
                    while (at > 0 && m_Candidates[at - 1].Area < area)
                    {
                        m_Candidates[at] = m_Candidates[at - 1];
                        at--;
                    }
                    m_Candidates[at] = new Candidate { Door = door, Direction = direction, Area = area, Box = box };
                    count++;
                }
            }
            return count;
        }

        static bool Straddles(PortalDoor door, float4 plane, float3 eye)
        {
            return math.abs(PortalMath.Side(plane, eye)) < PortalMath.BoxDistance
                   && PortalMath.InsideDoor(door.A, door.B, door.Height, eye, PortalMath.BoxDistance);
        }

        void AssignSlots(int candidates)
        {
            m_Slots.Begin();
            for (int k = 0; k < candidates; k++)
                m_Candidates[k].Reference = m_Slots.Assign(m_Candidates[k].Door.TargetRoom(m_Candidates[k].Direction));
        }

        // showRooms is false at depth 0: doorways are still masked, and show the background's fog colour.
        void PlaceRooms(int candidates, float3 head, bool frusta, bool showRooms)
        {
            PortalRoom current = m_Rooms[m_CurrentRoom];
            current.SetOn(true);
            current.SetLayer(PortalStencilTable.WorldLayer);
            current.SetPlane(k_NoPlane);
            current.WasShown = true;
            m_Swap.Reset(m_CurrentRoom);

            // Rooms behind doorways: the largest doorway into a room gives its clip plane.
            if (showRooms)
            {
                for (int k = candidates - 1; k >= 0; k--)
                {
                    ref Candidate candidate = ref m_Candidates[k];
                    if (candidate.Reference == 0) continue;
                    PortalRoom room = m_Rooms[candidate.Door.TargetRoom(candidate.Direction)];
                    if (room == current) continue;
                    room.SetOn(true);
                    room.SetLayer(PortalStencilTable.Layer(candidate.Reference));
                    // Normal into the room behind the doorway: what lies in front of the doorway is not drawn.
                    room.SetPlane(PortalMath.Flip(candidate.Door.ViewerPlane(candidate.Direction)));
                    room.WasShown = true;
                    m_Swap.Reset(room.Id);
                }
            }

            // Rooms no doorway shows.
            for (int k = 0; k < m_Rooms.Count; k++)
            {
                PortalRoom room = m_Rooms[k];
                if (k == m_CurrentRoom) continue;
                if (showRooms && m_Slots.ReferenceOf(k) != 0) continue;
                if (!room.IsOn) continue;

                bool keep = false;
                int reference = 0;
                if (showRooms && room.WasShown)
                {
                    bool leftSees = frusta && PortalMath.BoundsInFrustum(m_LeftPlanes, room.BoundsMin, room.BoundsMax);
                    bool rightSees = frusta && PortalMath.BoundsInFrustum(m_RightPlanes, room.BoundsMin, room.BoundsMax);
                    bool inOverlap = PortalMath.InOverlap(head, room.BoundsMin, room.BoundsMax, current.BoundsMin, current.BoundsMax);
                    if (!m_Swap.Step(k, leftSees, rightSees, inOverlap))
                    {
                        // Kept on a layer of its own: no mask writes that reference, so nothing of it shows.
                        reference = m_Slots.Assign(k);
                        keep = reference != 0;
                    }
                }
                if (keep)
                {
                    room.SetLayer(PortalStencilTable.Layer(reference));
                }
                else
                {
                    room.SetOn(false);
                    room.WasShown = false;
                    m_Swap.Reset(k);
                }
            }
        }

        void ShowMasks(int candidates)
        {
            // Hide everything, then show this frame's doorways; a mask only touches the engine when it changes.
            for (int k = 0; k < m_Doors.Count; k++)
            {
                PortalDoor door = m_Doors[k];
                int forward = 0, backward = 0;
                bool forwardBox = false, backwardBox = false;
                for (int c = 0; c < candidates; c++)
                {
                    if (m_Candidates[c].Door != door) continue;
                    if (m_Candidates[c].Direction == 0) { forward = m_Candidates[c].Reference; forwardBox = m_Candidates[c].Box; }
                    else { backward = m_Candidates[c].Reference; backwardBox = m_Candidates[c].Box; }
                }
                ApplyMasks(ref door.Forward, forward, forwardBox);
                ApplyMasks(ref door.Backward, backward, backwardBox);
            }
        }

        void ApplyMasks(ref PortalDoor.Masks masks, int reference, bool box)
        {
            if (masks.QuadMark == null) return;
            if (reference == masks.Reference && box == masks.Box) return;
            bool shown = reference != 0;
            if (shown && reference != masks.Reference)
            {
                masks.QuadMark.sharedMaterial = m_Mark[reference];
                masks.QuadReset.sharedMaterial = m_Reset[reference];
                masks.BoxMark.sharedMaterial = m_BoxMark[reference];
                masks.BoxReset.sharedMaterial = m_Reset[reference];
            }
            masks.QuadMark.enabled = shown;
            masks.QuadReset.enabled = shown;
            masks.BoxMark.enabled = shown && box;
            masks.BoxReset.enabled = shown && box;
            masks.Reference = reference;
            masks.Box = box;
        }

        void ReadPlanes(IStereoRig rig, Eye eye, float4[] planes)
        {
            rig.GetEyeFrustumPlanes(eye, m_UnityPlanes);
            for (int k = 0; k < 6; k++)
            {
                Vector3 normal = m_UnityPlanes[k].normal;
                planes[k] = new float4(normal.x, normal.y, normal.z, m_UnityPlanes[k].distance);
            }
        }

        // ---- renderer features ----------------------------------------------------------------------------

        // A room feature is active only while its reference is in use, so a space without portals pays nothing.
        void SetFeatures(int references, bool masks, bool rooms)
        {
            if (!m_FeaturesSearched) return;
            SetActive(m_MaskFeature, masks);
            for (int n = 1; n <= MaxSlots; n++) SetActive(m_RoomFeatures[n], rooms && n <= references);
        }

        static void SetActive(ScriptableRendererFeature feature, bool active)
        {
            if (feature != null && feature.isActive != active) feature.SetActive(active);
        }

        void FindFeatures()
        {
            if (m_FeaturesSearched) return;
            m_FeaturesSearched = true;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
            {
                ReadOnlySpan<ScriptableRendererData> renderers = asset.rendererDataList;
                for (int r = 0; r < renderers.Length; r++)
                {
                    ScriptableRendererData data = renderers[r];
                    if (data == null) continue;
                    List<ScriptableRendererFeature> features = data.rendererFeatures;
                    for (int f = 0; f < features.Count; f++)
                    {
                        ScriptableRendererFeature feature = features[f];
                        if (feature == null) continue;
                        string name = feature.name;
                        if (name == PortalStencilTable.MaskFeature) { if (m_MaskFeature == null) m_MaskFeature = feature; continue; }
                        for (int n = 1; n <= MaxSlots; n++)
                            if (m_RoomFeatures[n] == null && name == PortalStencilTable.FeatureName(n)) m_RoomFeatures[n] = feature;
                    }
                }
            }
            if (m_MaskFeature == null)
                Debug.LogWarning("portal.features: the renderer has no 'Atlas Portal Masks' feature; run Atlas > Apply Project Settings. Until then a doorway shows nothing of the room behind it.");
        }

        // ---- masks ----------------------------------------------------------------------------------------

        void BuildMaterials()
        {
            if (m_MaterialsBuilt) return;
            m_MaterialsBuilt = true;
            Shader shader = Resources.Load<Shader>(MaskShaderResource);
            if (shader == null)
            {
                Debug.LogError("portal.shader: '" + MaskShaderResource + "' is not in Resources.");
                return;
            }
            for (int n = 1; n <= MaxSlots; n++)
            {
                // Mark: the doorway's reference where the doorway is visible, at the doorway's depth.
                m_Mark[n] = MaskMaterial(shader, "AtlasPortalMask mark", n, CompareFunction.Always, StencilOp.Replace,
                    CompareFunction.LessEqual, true, false, PortalStencilTable.MarkQueue);
                // The box encloses the eye, so nothing of the walker's room can stand in front of it.
                m_BoxMark[n] = MaskMaterial(shader, "AtlasPortalMask box", n, CompareFunction.Always, StencilOp.Replace,
                    CompareFunction.Always, false, false, PortalStencilTable.MarkQueue);
                // Reset: depth back to far wherever the reference was written, after every mark.
                m_Reset[n] = MaskMaterial(shader, "AtlasPortalMask reset", n, CompareFunction.Equal, StencilOp.Keep,
                    CompareFunction.Always, true, true, PortalStencilTable.ResetQueue);
            }
        }

        static Material MaskMaterial(Shader shader, string name, int reference, CompareFunction stencil, StencilOp pass,
                                     CompareFunction depthTest, bool depthWrite, bool toFarPlane, int queue)
        {
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave, enableInstancing = false };
            material.SetFloat(AtlasShaderIDs.StencilRef, reference);
            material.SetFloat(AtlasShaderIDs.StencilComp, (float)stencil);
            material.SetFloat("_StencilPass", (float)pass);
            material.SetFloat("_MaskZTest", (float)depthTest);
            material.SetFloat("_MaskZWrite", depthWrite ? 1f : 0f);
            material.SetVector("_MaskParams", new Vector4(toFarPlane ? 1f : 0f, 0f, 0f, 0f));
            material.renderQueue = queue;
            return material;
        }

        void BuildMasks(PortalDoor door)
        {
            FindFeatures();
            BuildMaterials();
            if (m_Mark[1] == null) return;      // no shader: the doorway stays unmarked
            if (m_MaskRoot == null)
            {
                var root = new GameObject(PortalStencilTable.MaskFeature) { layer = PortalStencilTable.MaskLayer };
                m_MaskRoot = root.transform;
                if (m_MaskParent != null) m_MaskRoot.SetParent(m_MaskParent, false);
            }

            var holder = new GameObject("Door " + door.Id) { layer = PortalStencilTable.MaskLayer };
            holder.transform.SetParent(m_MaskRoot, false);
            door.Holder = holder;

            float3 a = new float3(door.A.x, 0f, door.A.y), b = new float3(door.B.x, 0f, door.B.y);
            float3 across = b - a, up = new float3(0f, door.Height, 0f);
            float2 uv = AtlasPalette.Uv(AtlasPalette.Void);

            m_MeshData.Clear();
            m_MeshData.AddQuad(a, across, up, uv);
            door.QuadMesh = AtlasMesh.Create(m_MeshData, "Portal Door");
            // Forward is seen from the side the normal points to, so its box lies on the other side.
            door.ForwardBoxMesh = BoxMesh(a, across, up, -door.Plane.xyz * PortalMath.BoxDepth, uv);
            door.BackwardBoxMesh = BoxMesh(a, across, up, door.Plane.xyz * PortalMath.BoxDepth, uv);

            door.Forward = CreateMasks(holder.transform, door.QuadMesh, door.ForwardBoxMesh);
            door.Backward = CreateMasks(holder.transform, door.QuadMesh, door.BackwardBoxMesh);
        }

        // The doorway pushed back into a box with no front: back, two sides, top and bottom.
        Mesh BoxMesh(float3 a, float3 across, float3 up, float3 back, float2 uv)
        {
            m_MeshData.Clear();
            m_MeshData.AddQuad(a + back, across, up, uv);
            m_MeshData.AddQuad(a, back, up, uv);
            m_MeshData.AddQuad(a + across, back, up, uv);
            m_MeshData.AddQuad(a + up, across, back, uv);
            m_MeshData.AddQuad(a, across, back, uv);
            return AtlasMesh.Create(m_MeshData, "Portal Box");
        }

        static PortalDoor.Masks CreateMasks(Transform parent, Mesh quad, Mesh box)
        {
            return new PortalDoor.Masks
            {
                QuadMark = CreateMask("Mark", parent, quad),
                QuadReset = CreateMask("Reset", parent, quad),
                BoxMark = CreateMask("Box Mark", parent, box),
                BoxReset = CreateMask("Box Reset", parent, box),
            };
        }

        static MeshRenderer CreateMask(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name) { layer = PortalStencilTable.MaskLayer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.enabled = false;
            return renderer;
        }

        static void DestroyMasks(PortalDoor door)
        {
            DestroyObject(door.Holder);
            DestroyObject(door.QuadMesh);
            DestroyObject(door.ForwardBoxMesh);
            DestroyObject(door.BackwardBoxMesh);
            door.Holder = null;
            door.QuadMesh = door.ForwardBoxMesh = door.BackwardBoxMesh = null;
            door.Forward = default;
            door.Backward = default;
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
