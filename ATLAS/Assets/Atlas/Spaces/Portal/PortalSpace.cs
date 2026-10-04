using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Portal
{
    /// <summary>
    /// Impossible Rooms, space S5 (Master Plan section 3, tasks P1-A10 and P1-A11): flat rooms joined by
    /// stencil portals so that they overlap. A booth in a corner of the room contains a hall; a corridor
    /// makes four right turns and arrives in a different room. Every room is built in the room frame from
    /// the RoomModel's free rectangle, so the space is registered and every portal transform is the
    /// identity (D16). The real room, drawn by the room content, is room 0.
    /// </summary>
    public sealed class PortalSpace : MonoBehaviour, ISpaceModule, IUploadStep, ISpaceStateReader
    {
        readonly PortalSpaceCore m_Core = new PortalSpaceCore();
        readonly List<GameObject> m_Pieces = new List<GameObject>(8);
        readonly List<Mesh> m_Meshes = new List<Mesh>(8);
        readonly List<Material> m_Materials = new List<Material>(8);

        RoomModel m_Room;
        ParamRegistry m_Registry;
        IRoomContent m_Content;
        MaterialPool m_Pool;
        PortalRenderer m_Renderer;
        bool m_OwnsRenderer;
        IEventLog m_Events;
        IHands m_Hands;
        IStereoRig m_Rig;
        ISpaceHost m_Host;
        Action<int, int> m_OnRoomChanged;
        Action<int> m_OnObjectChanged;
        Action<RoomModel> m_OnContentBuilt;
        bool m_Subscribed;

        public SpaceId Id { get { return SpaceId.Portal; } }
        /// <summary>Registered, room by room: the real room's walls are where they really are.</summary>
        public bool Registered { get { return true; } }
        /// <summary>The logic behind this component.</summary>
        public PortalSpaceCore Core { get { return m_Core; } }
        /// <summary>The portal renderer the rooms are registered with. Null before Configure.</summary>
        public PortalRenderer Portals { get { return m_Renderer; } }

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene, with the module.</summary>
        public static ISpaceModule Create()
        {
            var go = new GameObject(SpaceIds.SceneName(SpaceId.Portal));
            return go.AddComponent<PortalSpace>();
        }

        /// <summary>Declares the parameters of group "portal".</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            PortalSpaceCore.DeclareParams(registry);
        }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            m_Room = room;
            m_Registry = registry;
            AtlasServices.TryGet(out m_Content);
            AtlasServices.TryGet(out m_Pool);
            AtlasServices.TryGet(out m_Events);
            AtlasServices.TryGet(out m_Host);
            AtlasServices.TryGet(out m_Hands);
            AtlasServices.TryGet(out m_Rig);

            // The shared renderer when the composition root made one; otherwise one of our own, driven from Upload.
            if (AtlasServices.TryGet(out PortalRenderer shared))
            {
                m_Renderer = shared;
                m_OwnsRenderer = false;
            }
            else
            {
                m_Renderer = new PortalRenderer(transform);
                m_OwnsRenderer = true;
            }
            if (m_Renderer.Rig == null && AtlasServices.TryGet(out IStereoRig rig)) m_Renderer.Rig = rig;

            m_OnRoomChanged = OnRoomChanged;
            m_OnObjectChanged = OnObjectChanged;
            m_OnContentBuilt = OnContentBuilt;

            m_Core.Configure(room, registry);
            Build();
        }

        public void Enter(in WalkerPose start)
        {
            // Registered: the module's root is the room frame itself (contract README F4).
            Transform root = transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            if (m_Content != null)
            {
                // The real room in full: its walls are the real walls.
                m_Content.SetWallMode(WallMode.Full, 0f);
                m_Content.FloorVisible = true;
                m_Content.ObjectsVisible = true;
                // What the room content just changed is read again before the first frame.
                m_Renderer.RefreshRoom(0);
            }
            Subscribe();
            m_Renderer.Depth = m_Core.Depth;
            m_Renderer.CurrentRoom = m_Core.Layout.StartRoom;
        }

        public void Tick(in FrameContext frame)
        {
            if (m_Core.RebuildPending)
            {
                // rooms.layout or rooms.count was released at a new value: new rooms, back in the real room.
                m_Core.Generate();
                Build();
                m_Renderer.CurrentRoom = m_Core.Layout.StartRoom;
                if (m_Host != null) m_Host.BumpWorldVersion();
            }
            int depth = m_Core.Depth;
            if (m_Renderer.Depth != depth) m_Renderer.Depth = depth;
            if (m_Hands != null && EyeInDoorwayMask(in frame)) m_Hands.HideThisFrame();
        }

        /// <summary>How close to a doorway's plane an eye must be for the doorway's box mask to cover it (metres).</summary>
        const float DoorMaskReach = 0.10f;

        // O1: true while either eye is within DoorMaskReach of a doorway's plane and inside its outline (plus the reach
        // sideways). The hands are hidden for those frames rather than missing in one eye only.
        bool EyeInDoorwayMask(in FrameContext frame)
        {
            PortalLayout layout = m_Core.Layout;
            if (layout == null) return false;
            float half = 0.5f * (m_Rig != null ? m_Rig.Ipd : 0.064f);
            float3 right = math.mul(frame.HeadRotation, new float3(1f, 0f, 0f));
            float3 head = frame.HeadPosition;
            for (int d = 0; d < layout.Doors.Count; d++)
            {
                LayoutDoor door = layout.Doors[d];
                float2 ab = door.B - door.A;
                float len = math.length(ab);
                if (len < 1e-4f) continue;
                float2 dir = ab / len, normal = new float2(-dir.y, dir.x);
                for (int e = -1; e <= 1; e += 2)
                {
                    float3 eye = head + right * (half * e);
                    if (eye.y > door.Height + DoorMaskReach) continue;
                    float2 rel = new float2(eye.x, eye.z) - door.A;
                    float along = math.dot(rel, dir);
                    if (along < -DoorMaskReach || along > len + DoorMaskReach) continue;
                    if (math.abs(math.dot(rel, normal)) < DoorMaskReach) return true;
                }
            }
            return false;
        }

        public void Upload(in FrameContext frame)
        {
            if (m_OwnsRenderer && m_Renderer != null) m_Renderer.Upload(in frame);
        }

        public void Exit()
        {
            Unsubscribe();
            if (m_Renderer != null)
            {
                // Every root back on Default with the clip off, before the rooms themselves go.
                if (m_OwnsRenderer) m_Renderer.Dispose();
                else m_Renderer.Clear();
            }
            ReleasePieces();
            m_Core.Exit(m_Registry);
            m_Renderer = null;
            m_Content = null;
            m_Pool = null;
            m_Events = null;
            m_Host = null;
            m_Hands = null;
            m_Rig = null;
            m_Registry = null;
            m_Room = null;
        }

        public void WriteState(ref StateMessage state)
        {
            m_Core.WriteState(ref state);
        }

        /// <summary>The Spectator shows the room the walker is in.</summary>
        public void ReadState(in StateMessage state)
        {
            if (m_Renderer == null) return;
            m_Core.SetCurrentRoom(state.Sheet);
            if (m_Renderer.CurrentRoom != m_Core.CurrentRoom) m_Renderer.CurrentRoom = m_Core.CurrentRoom;
        }

        void OnDestroy()
        {
            // Exit has done this already; it only covers a root destroyed without Exit.
            Unsubscribe();
            for (int k = 0; k < m_Meshes.Count; k++) DestroyObject(m_Meshes[k]);
            m_Meshes.Clear();
        }

        // ---- building -------------------------------------------------------------------------------------

        // Registers the layout's rooms and doorways with the portal renderer. Rooms keep the layout's ids.
        void Build()
        {
            PortalLayout layout = m_Core.Layout;
            m_Renderer.Clear();
            ReleasePieces();

            // Room 0: the real room. Without room content (a test scene) an empty root stands in for it.
            Transform realRoot = m_Content != null ? m_Content.Root : null;
            if (realRoot == null) realRoot = CreateHolder("Real Room").transform;
            m_Renderer.AddRoom(realRoot, RealRoomBounds(m_Room));

            var data = new AtlasMeshBuilder(1024, 4096);
            PortalRoomMeshes.BuildExterior(layout, data);
            if (!data.IsEmpty)
            {
                GameObject exterior = CreatePiece("Exterior", data);
                if (exterior != null) m_Renderer.AddRoomPart(0, exterior.transform);
            }

            for (int r = 1; r < layout.RoomCount; r++)
            {
                data.Clear();
                PortalRoomMeshes.BuildRoom(layout, r, data);
                GameObject piece = CreatePiece("Room " + r, data) ?? CreateHolder("Room " + r);
                var bounds = new Bounds((Vector3)data.BoundsCentre, (Vector3)data.BoundsSize);
                m_Renderer.AddRoom(piece.transform, bounds);
            }

            for (int d = 0; d < layout.Doors.Count; d++)
            {
                LayoutDoor door = layout.Doors[d];
                m_Renderer.AddDoor(door.From, door.To, door.A, door.B, door.Height);
            }
            m_Renderer.Depth = m_Core.Depth;
        }

        GameObject CreateHolder(string pieceName)
        {
            var go = new GameObject(pieceName);
            go.transform.SetParent(transform, false);
            m_Pieces.Add(go);
            return go;
        }

        // One mesh, one renderer, one pooled material per room. Null when there is no material pool.
        GameObject CreatePiece(string pieceName, AtlasMeshBuilder data)
        {
            if (m_Pool == null || data.IsEmpty) return null;
            GameObject go = CreateHolder(pieceName);
            Mesh mesh = AtlasMesh.Create(data, "Portal " + pieceName);
            m_Meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            // The look has no shadows, probes or motion vectors (contract README F7).
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Material material = m_Pool.Rent();
            MaterialPool.SetLines(material, 0.45f, 0f, float2.zero);
            m_Materials.Add(material);
            meshRenderer.sharedMaterial = material;
            return go;
        }

        void ReleasePieces()
        {
            for (int k = 0; k < m_Pieces.Count; k++) DestroyObject(m_Pieces[k]);
            m_Pieces.Clear();
            for (int k = 0; k < m_Meshes.Count; k++) DestroyObject(m_Meshes[k]);
            m_Meshes.Clear();
            if (m_Pool != null)
                for (int k = 0; k < m_Materials.Count; k++) m_Pool.Return(m_Materials[k]);
            m_Materials.Clear();
        }

        // The scanned room, wall to wall, with some slack for what stands outside the outline.
        static Bounds RealRoomBounds(RoomModel room)
        {
            float2 min = new float2(-0.5f * room.FreeRect.Wf, -0.5f * room.FreeRect.Df), max = -min;
            float2[] outline = room.Outline;
            for (int k = 0; k < outline.Length; k++)
            {
                min = math.min(min, outline[k]);
                max = math.max(max, outline[k]);
            }
            float height = room.MaxWallHeight;
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x - 0.5f, -0.1f, min.y - 0.5f), new Vector3(max.x + 0.5f, height + 0.5f, max.y + 0.5f));
            return bounds;
        }

        // ---- events ---------------------------------------------------------------------------------------

        void Subscribe()
        {
            if (m_Subscribed) return;
            m_Subscribed = true;
            m_Renderer.RoomChanged += m_OnRoomChanged;
            if (m_Content != null)
            {
                m_Content.ObjectChanged += m_OnObjectChanged;
                m_Content.Built += m_OnContentBuilt;
            }
        }

        void Unsubscribe()
        {
            if (!m_Subscribed) return;
            m_Subscribed = false;
            if (m_Renderer != null) m_Renderer.RoomChanged -= m_OnRoomChanged;
            if (m_Content != null)
            {
                m_Content.ObjectChanged -= m_OnObjectChanged;
                m_Content.Built -= m_OnContentBuilt;
            }
        }

        void OnRoomChanged(int from, int to)
        {
            bool lap = m_Core.OnRoomChanged(from, to);
            if (m_Events == null) return;
            m_Events.Log("portal.cross", "from", from, "to", to);
            if (lap) m_Events.Log("rooms.loop", "room", to, "laps", m_Core.LoopsWalked);
        }

        // A generated mesh replaced a placeholder: the real room has a new renderer or material.
        void OnObjectChanged(int objectId)
        {
            if (m_Renderer != null) m_Renderer.RefreshRoom(0);
        }

        void OnContentBuilt(RoomModel room)
        {
            if (m_Renderer != null) m_Renderer.RefreshRoom(0);
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
