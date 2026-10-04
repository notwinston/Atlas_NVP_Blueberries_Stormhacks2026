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

        readonly List<PortalRoom> m_Rooms = new List<PortalRoom>(16);
        readonly List<PortalDoor> m_Doors = new List<PortalDoor>(16);
        // The frame's logic, engine-free (foundation F2): this class reads the cameras and applies its result.
        readonly PortalFrameCore m_Core = new PortalFrameCore();
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
        int m_SlotsInUse;

        /// <param name="maskParent">Parent of the mask objects, or null for a root object of their own.</param>
        public PortalRenderer(Transform maskParent = null)
        {
            m_MaskParent = maskParent;
            m_Core.RoomChanged += OnCoreRoomChanged;
        }

        /// <summary>The eye cameras, for their frusta. Null in the Spectator: a doorway then counts as seen
        /// whenever the head is in front of it.</summary>
        public IStereoRig Rig { get; set; }

        /// <summary>The engine-free frame logic this renderer applies: the same object a headless test steps.</summary>
        public PortalFrameCore Core { get { return m_Core; } }

        public int RoomCount { get { return m_Rooms.Count; } }
        public int DoorCount { get { return m_Doors.Count; } }
        public PortalRoom GetRoom(int room) { return m_Rooms[room]; }
        public PortalDoor GetDoor(int door) { return m_Doors[door]; }

        /// <summary>The room the walker is in. Changed by the renderer when the head crosses a doorway.
        /// Placed from outside, the next frame tests no crossing: the head did not walk there.</summary>
        public int CurrentRoom
        {
            get { return m_Core.CurrentRoom; }
            set { m_Core.CurrentRoom = value; }
        }

        /// <summary>0: doorways show the fog colour. 1: doorways show the next room (portal.depth).</summary>
        public int Depth
        {
            get { return m_Core.Depth; }
            set { m_Core.Depth = value; }
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
            m_Core.AddRoom(room.BoundsMin, room.BoundsMax);
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
            m_Core.AddDoor(fromRoom, toRoom, a, b, height);
            return door.Id;
        }

        public void SetDoorOpen(int door, bool open)
        {
            m_Doors[door].Open = open;
            m_Core.SetDoorOpen(door, open);
        }

        /// <summary>Removes every room and door and puts every root back on Default with the clip off.</summary>
        public void Clear()
        {
            for (int k = 0; k < m_Rooms.Count; k++)
            {
                PortalRoom room = m_Rooms[k];
                room.SetOn(true);
                room.SetLayer(PortalStencilTable.WorldLayer);
                room.SetPlane(PortalFrameCore.NoPlane);
                room.WasShown = false;
            }
            m_Rooms.Clear();
            for (int k = 0; k < m_Doors.Count; k++) DestroyMasks(m_Doors[k]);
            m_Doors.Clear();
            if (m_MaskRoot != null) DestroyObject(m_MaskRoot.gameObject);
            m_MaskRoot = null;
            m_Core.Clear();
            m_SlotsInUse = 0;
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

            IStereoRig rig = Rig;
            bool frusta = rig != null && rig.Mode == DisplayMode.Headset;
            if (frusta)
            {
                ReadPlanes(rig, Eye.Left, m_LeftPlanes);
                ReadPlanes(rig, Eye.Right, m_RightPlanes);
            }

            // The cameras are at the raw head pose, so the doorways are judged from it too.
            m_Core.Step(frame.RawHeadPosition, frame.LeftEyePosition, frame.RightEyePosition,
                        frusta ? m_LeftPlanes : null, frusta ? m_RightPlanes : null, m_MaskFeature != null);

            // Rooms that come on first, so no frame shows a gap; every setter only touches the engine on a change.
            for (int k = 0; k < m_Rooms.Count; k++)
            {
                PortalFrameCore.RoomFrame placed = m_Core.Room(k);
                PortalRoom room = m_Rooms[k];
                room.WasShown = placed.WasShown;
                if (placed.On)
                {
                    room.SetOn(true);
                    room.SetLayer(placed.Layer);
                    room.SetPlane(placed.Plane);
                }
                else
                {
                    room.SetOn(false);
                }
            }
            for (int k = 0; k < m_Doors.Count; k++)
            {
                PortalDoor door = m_Doors[k];
                PortalFrameCore.DoorFrame forward = m_Core.DoorMasks(k, 0), backward = m_Core.DoorMasks(k, 1);
                ApplyMasks(ref door.Forward, forward.Reference, forward.Box);
                ApplyMasks(ref door.Backward, backward.Reference, backward.Box);
            }

            m_SlotsInUse = m_Core.SlotsInUse;
            SetFeatures(m_SlotsInUse, m_Core.MasksShown, m_Core.RoomsShown);
        }

        void OnCoreRoomChanged(int from, int to)
        {
            Action<int, int> changed = RoomChanged;
            if (changed != null) changed(from, to);
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
