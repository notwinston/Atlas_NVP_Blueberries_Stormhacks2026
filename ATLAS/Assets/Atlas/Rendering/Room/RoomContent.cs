using System;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering.Room
{
    /// <summary>
    /// The scanned room as drawn (Master Plan sections 4.5, 4.9 and 7.4): the shell and one view per object,
    /// built once from the RoomModel in the room frame, so every transform above the objects is the identity.
    /// Two draw calls for the shell and one per object, each on a pooled material instance. Main thread only.
    /// </summary>
    /// <remarks>
    /// Wall changes and mesh cross-fades advance in <see cref="Tick"/>, which the frame loop calls once per
    /// frame. While <see cref="Instant"/> is set (nothing ticks it) every change is applied at once.
    /// </remarks>
    public sealed class RoomContent : IRoomContent, IDisposable
    {
        /// <summary>Floor lines: a faint line every 0.5 m and a stronger one every metre (Master Plan section 8.1).</summary>
        public const float FloorMinorLine = 0.6f, FloorMajorLine = 1f;

        readonly Transform m_Root;
        readonly MaterialPool m_Pool;
        readonly AtlasMeshBuilder m_Data = new AtlasMeshBuilder(4096, 16384);

        readonly GameObject m_FloorObject, m_WallObject, m_ObjectsObject;
        readonly MeshFilter m_FloorFilter, m_WallFilter;
        readonly MeshRenderer m_FloorRenderer, m_WallRenderer;

        RoomModel m_Room;
        Mesh m_FloorMesh, m_WallMesh, m_KerbMesh;
        int m_FloorTriangles, m_WallTriangles, m_KerbTriangles;
        float m_Band;
        Material m_ShellMaterial;
        RoomWallSink m_Sink;
        float m_KerbHeight = RoomShellBuilder.DefaultKerbHeight;
        bool m_FloorVisible = true, m_ObjectsVisible = true;
        FurnitureLevel m_Level = FurnitureLevel.Full;

        RoomObjectView[] m_Views = new RoomObjectView[0];
        int m_ViewCount;
        // Views waiting to begin a mesh swap, oldest first.
        int[] m_Queue = new int[RoomModel.MaxObjects];
        int m_QueueHead, m_QueueCount;
        int m_SwapsThisFrame;
        int m_FadingCount;
        bool m_Disposed;

        /// <summary>Builds under root. Rents its material instances from the pool and returns them on Clear.</summary>
        public RoomContent(Transform root, MaterialPool pool)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            m_Root = root;
            m_Pool = pool;
            m_FloorObject = CreateRenderer("Floor", root, out m_FloorFilter, out m_FloorRenderer);
            m_WallObject = CreateRenderer("Walls", root, out m_WallFilter, out m_WallRenderer);
            m_ObjectsObject = new GameObject("Objects");
            m_ObjectsObject.transform.SetParent(root, false);
            m_Sink.Reset(KerbRatio(null));
        }

        public event Action<RoomModel> Built;
        public event Action<int> ObjectChanged;

        /// <summary>When set, wall changes and mesh swaps complete at once. For callers with no frame loop.</summary>
        public bool Instant { get; set; }

        public RoomModel Room { get { return m_Room; } }
        public bool IsBuilt { get { return m_Room != null; } }
        public Transform Root { get { return m_Root; } }
        public WallMode Walls { get { return m_Sink.Mode; } }
        /// <summary>Which wall mesh is drawn now; the walls while they sink or rise.</summary>
        public WallShown WallsShown { get { return m_Sink.Shown; } }
        /// <summary>Row height of the wall mesh chosen by the budget rule, metres.</summary>
        public float WallBand { get { return m_Band; } }
        public Renderer FloorRenderer { get { return m_FloorRenderer; } }
        public Renderer WallRenderer { get { return m_WallRenderer; } }
        public Material ShellMaterial { get { return m_ShellMaterial; } }
        public int ObjectCount { get { return m_ViewCount; } }

        // ---- Building ------------------------------------------------------------------------------------------

        public void Build(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            ThrowIfDisposed();
            Clear();
            m_Room = room;
            m_KerbHeight = RoomShellBuilder.DefaultKerbHeight;

            m_ShellMaterial = m_Pool.Rent();
            m_Data.Clear();
            RoomShellBuilder.Floor(room, m_Data);
            m_FloorTriangles = m_Data.TriangleCount;
            m_FloorMesh = AtlasMesh.Create(m_Data, "Room Floor");
            m_Data.Clear();
            RoomShellBuilder.Walls(room, m_FloorTriangles, m_Data, out m_Band);
            m_WallTriangles = m_Data.TriangleCount;
            m_WallMesh = AtlasMesh.Create(m_Data, "Room Walls");
            m_Data.Clear();
            RoomShellBuilder.Kerbs(room, m_KerbHeight, m_Data);
            m_KerbTriangles = m_Data.TriangleCount;
            m_KerbMesh = AtlasMesh.Create(m_Data, "Room Kerbs");

            m_FloorFilter.sharedMesh = m_FloorMesh;
            m_FloorRenderer.sharedMaterial = m_ShellMaterial;
            m_WallRenderer.sharedMaterial = m_ShellMaterial;

            RoomObject[] objects = room.Objects;
            if (m_Views.Length < objects.Length) m_Views = new RoomObjectView[objects.Length];
            if (m_Queue.Length < objects.Length) m_Queue = new int[objects.Length];
            for (int k = 0; k < objects.Length; k++) m_Views[k] = CreateView(objects[k]);
            m_ViewCount = objects.Length;

            ResetPresentation();
            Action<RoomModel> built = Built;
            if (built != null) built(room);
        }

        public void Clear()
        {
            if (m_Room == null) return;
            for (int k = 0; k < m_ViewCount; k++)
            {
                RoomObjectView view = m_Views[k];
                if (view.Fading) FinishFade(view);
                DropPending(view);
                if (view.OwnsMesh) DestroyObject(view.Mesh);
                DestroyObject(view.LightMesh);
                m_Pool.Return(view.MaterialInternal);
                DestroyObject(view.AnchorObject);
                m_Views[k] = null;
            }
            m_ViewCount = 0;
            m_QueueHead = 0;
            m_QueueCount = 0;
            m_FadingCount = 0;

            m_FloorFilter.sharedMesh = null;
            m_WallFilter.sharedMesh = null;
            m_FloorRenderer.sharedMaterial = null;
            m_WallRenderer.sharedMaterial = null;
            m_FloorRenderer.enabled = false;
            m_WallRenderer.enabled = false;
            DestroyObject(m_FloorMesh);
            DestroyObject(m_WallMesh);
            DestroyObject(m_KerbMesh);
            m_FloorMesh = null;
            m_WallMesh = null;
            m_KerbMesh = null;
            m_FloorTriangles = 0;
            m_WallTriangles = 0;
            m_KerbTriangles = 0;
            m_Pool.Return(m_ShellMaterial);
            m_ShellMaterial = null;
            m_Room = null;
        }

        /// <summary>Clears the room and destroys the objects this instance created under the root.</summary>
        public void Dispose()
        {
            if (m_Disposed) return;
            Clear();
            m_Disposed = true;
            DestroyObject(m_FloorObject);
            DestroyObject(m_WallObject);
            DestroyObject(m_ObjectsObject);
        }

        // ---- Presentation --------------------------------------------------------------------------------------

        public void SetWallMode(WallMode mode, float seconds = 0.5f)
        {
            m_Sink.Set(mode, Instant ? 0f : seconds, KerbRatio(m_Room));
            ApplyWalls();
        }

        public float KerbHeight
        {
            get { return m_KerbHeight; }
            set
            {
                float height = math.clamp(value, RoomShellBuilder.MinKerbHeight, RoomShellBuilder.MaxKerbHeight);
                if (height == m_KerbHeight) return;
                m_KerbHeight = height;
                if (m_Room == null) return;
                // The one case in which a shell mesh is rebuilt after Build: into the same Mesh object.
                m_Data.Clear();
                RoomShellBuilder.Kerbs(m_Room, height, m_Data);
                m_KerbTriangles = m_Data.TriangleCount;
                AtlasMesh.Apply(m_Data, m_KerbMesh);
                m_Sink.SetKerbRatio(KerbRatio(m_Room));
                ApplyWalls();
            }
        }

        public bool FloorVisible
        {
            get { return m_FloorVisible; }
            set
            {
                m_FloorVisible = value;
                m_FloorRenderer.enabled = value && m_Room != null;
            }
        }

        public bool ObjectsVisible
        {
            get { return m_ObjectsVisible; }
            set
            {
                m_ObjectsVisible = value;
                if (m_ObjectsObject.activeSelf != value) m_ObjectsObject.SetActive(value);
            }
        }

        public FurnitureLevel FurnitureLevel
        {
            get { return m_Level; }
            set
            {
                if (value == m_Level) return;
                m_Level = value;
                for (int k = 0; k < m_ViewCount; k++) ApplyDrawn(m_Views[k]);
            }
        }

        public void ResetPresentation()
        {
            FurnitureLevel = FurnitureLevel.Full;
            for (int k = 0; k < m_ViewCount; k++)
            {
                RoomObjectView view = m_Views[k];
                if (view.Fading) FinishFade(view);
                MaterialPool.ResetToDefaults(view.MaterialInternal);
            }
            KerbHeight = RoomShellBuilder.DefaultKerbHeight;
            m_Sink.Reset(KerbRatio(m_Room));
            ApplyWalls();
            FloorVisible = true;
            ObjectsVisible = true;
            if (m_ShellMaterial != null)
            {
                MaterialPool.ResetToDefaults(m_ShellMaterial);
                // The floor's line coordinate is in units of 0.5 m: minor lines every unit, major every two.
                MaterialPool.SetLines(m_ShellMaterial, FloorMinorLine, FloorMajorLine, new float2(2f, 2f));
            }
        }

        /// <summary>
        /// Once per frame, from the frame loop: advances the walls and the cross-fades and begins at most one
        /// queued mesh swap. No allocation.
        /// </summary>
        public void Tick(float dt)
        {
            m_SwapsThisFrame = 0;
            if (m_Room == null) return;
            if (m_Sink.Tick(dt)) ApplyWalls();
            if (m_FadingCount > 0)
            {
                for (int k = 0; k < m_ViewCount; k++)
                {
                    RoomObjectView view = m_Views[k];
                    if (!view.Fading) continue;
                    view.FadeTime += math.max(dt, 0f);
                    float t = math.saturate(view.FadeTime / view.FadeSeconds);
                    if (t >= 1f)
                    {
                        FinishFade(view);
                        continue;
                    }
                    SetOpacity(view.MaterialInternal, t);
                    SetOpacity(view.OutMaterial, 1f - t);
                    FollowWarp(view);
                }
            }
            BeginQueuedSwaps();
        }

        // ---- Accounting ----------------------------------------------------------------------------------------

        public int ShellTriangleCount
        {
            get
            {
                int walls = m_Sink.Shown == WallShown.Walls ? m_WallTriangles : m_Sink.Shown == WallShown.Kerbs ? m_KerbTriangles : 0;
                return (m_FloorVisible ? m_FloorTriangles : 0) + walls;
            }
        }

        /// <summary>Draw calls the room costs per eye as shown now: floor, walls or kerbs, one per object, and
        /// one more for each object whose old mesh is still fading out.</summary>
        public int DrawCallsPerEye
        {
            get
            {
                if (m_Room == null) return 0;
                int calls = (m_FloorVisible ? 1 : 0) + (m_Sink.Shown != WallShown.None && m_WallTriangles + m_KerbTriangles > 0 ? 1 : 0);
                if (m_ObjectsVisible) calls += m_ViewCount + m_FadingCount;
                return calls;
            }
        }

        /// <summary>Triangles the room costs per eye as shown now.</summary>
        public int TrianglesPerEye
        {
            get
            {
                if (m_Room == null) return 0;
                int triangles = ShellTriangleCount;
                if (!m_ObjectsVisible) return triangles;
                for (int k = 0; k < m_ViewCount; k++)
                {
                    RoomObjectView view = m_Views[k];
                    triangles += view.TriangleCount + (view.Fading ? view.OutTriangles : 0);
                }
                return triangles;
            }
        }

        /// <summary>The cost per eye and per frame as shown now.</summary>
        public RoomDrawCost Cost
        {
            get
            {
                int shell = m_Room == null ? 0 : ShellTriangleCount;
                int all = TrianglesPerEye;
                return new RoomDrawCost
                {
                    DrawCallsPerEye = DrawCallsPerEye, TrianglesPerEye = all, ShellTriangles = shell, ObjectTriangles = all - shell
                };
            }
        }

        // ---- Objects -------------------------------------------------------------------------------------------

        public IRoomObjectView GetObject(int index)
        {
            if (index < 0 || index >= m_ViewCount) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Views[index];
        }

        /// <summary>The view with its anchor and mesh transform, for a space that re-anchors objects.</summary>
        public RoomObjectView GetView(int index)
        {
            if (index < 0 || index >= m_ViewCount) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Views[index];
        }

        public bool TryGetObject(int objectId, out IRoomObjectView view)
        {
            int index = IndexOf(objectId);
            view = index >= 0 ? m_Views[index] : null;
            return index >= 0;
        }

        public bool SetObjectState(int objectId, MeshState state)
        {
            int index = IndexOf(objectId);
            if (index < 0) return false;
            if (state != MeshState.Placeholder && state != MeshState.Requested) return false;
            RoomObjectView view = m_Views[index];
            // Only the box changes between these two; a generated, built-in or stand-in mesh keeps its state.
            if (view.ShownInternal != MeshState.Placeholder && view.ShownInternal != MeshState.Requested) return false;
            if (view.HasPending) return false;
            view.ShownInternal = state;
            view.DataInternal.Mesh = state;
            RaiseObjectChanged(objectId);
            return true;
        }

        public bool SetGeneratedMesh(int objectId, Mesh mesh, string assetHash, float fadeSeconds = 0.3f)
        {
            int index = IndexOf(objectId);
            if (index < 0 || mesh == null) return false;
            Enqueue(index, mesh, true, MeshState.Ready, assetHash ?? "", Vector3.one, fadeSeconds);
            return true;
        }

        public bool SetGeneratedLightMesh(int objectId, Mesh mesh, string assetHashLight)
        {
            int index = IndexOf(objectId);
            if (index < 0 || mesh == null) return false;
            RoomObjectView view = m_Views[index];
            if (view.HasPending)
            {
                // The full copy is still waiting its turn: the light copy waits with it.
                if (view.PendingState != MeshState.Ready) return false;
                if (view.PendingLightMesh != null && view.PendingLightMesh != mesh) DestroyObject(view.PendingLightMesh);
                view.PendingLightMesh = mesh;
                view.PendingLightHash = assetHashLight ?? "";
                return true;
            }
            if (view.ShownInternal != MeshState.Ready) return false;
            Mesh old = view.LightMesh;
            view.LightMesh = mesh;
            view.LightTriangles = TriangleCount(mesh);
            view.DataInternal.AssetHashLight = assetHashLight ?? "";
            ApplyDrawn(view);
            if (old != null && old != mesh) DestroyObject(old);
            RaiseObjectChanged(objectId);
            return true;
        }

        public bool IsMeshDrawn(Mesh mesh)
        {
            if (mesh == null) return false;
            for (int k = 0; k < m_ViewCount; k++)
            {
                RoomObjectView view = m_Views[k];
                if (view.Mesh == mesh || view.LightMesh == mesh) return true;
                if (view.Fading && view.OutMesh == mesh) return true;
                if (view.HasPending && view.PendingMesh == mesh) return true;
            }
            return false;
        }

        public bool SetBuiltInMesh(int objectId, Mesh sharedMesh, Vector3 scale, float fadeSeconds = 0.3f)
        {
            int index = IndexOf(objectId);
            if (index < 0 || sharedMesh == null) return false;
            Enqueue(index, sharedMesh, false, MeshState.BuiltIn, "", scale, fadeSeconds);
            return true;
        }

        /// <summary>
        /// As SetGeneratedMesh, from mesh data in the shared vertex format and the object's local frame (origin
        /// at the centre of the base of its box, metres). The mesh made from it is owned here.
        /// </summary>
        public bool SetGeneratedMesh(int objectId, AtlasMeshBuilder data, string assetHash, float fadeSeconds = 0.3f)
        {
            if (data == null || data.TriangleCount == 0 || IndexOf(objectId) < 0) return false;
            return SetGeneratedMesh(objectId, AtlasMesh.Create(data, "Room Object " + objectId), assetHash, fadeSeconds);
        }

        // ---- Internals -----------------------------------------------------------------------------------------

        RoomObjectView CreateView(RoomObject obj)
        {
            var view = new RoomObjectView { DataInternal = obj };
            view.AnchorObject = new GameObject("Object " + obj.Id);
            Transform anchor = view.AnchorObject.transform;
            anchor.SetParent(m_ObjectsObject.transform, false);
            anchor.localPosition = new Vector3(obj.Base.x, obj.Base.y, obj.Base.z);
            anchor.localRotation = Quaternion.AngleAxis(math.degrees(obj.Yaw), Vector3.up);

            GameObject meshObject = CreateRenderer("Mesh", anchor, out view.Filter, out view.MeshRenderer);
            view.MeshTransformInternal = meshObject.transform;
            view.MaterialInternal = m_Pool.Rent();

            m_Data.Clear();
            RoomObjectMeshes.Initial(obj, m_Data);
            view.Mesh = AtlasMesh.Create(m_Data, "Room Object " + obj.Id);
            view.OwnsMesh = true;
            view.Triangles = m_Data.TriangleCount;
            // A room saved with a mesh on the way or in place shows the box until the asset client places it again.
            view.ShownInternal = obj.Mesh == MeshState.StandIn ? MeshState.StandIn
                : obj.Mesh == MeshState.Requested ? MeshState.Requested : MeshState.Placeholder;
            view.Filter.sharedMesh = view.Mesh;
            view.MeshRenderer.sharedMaterial = view.MaterialInternal;
            view.MeshRenderer.enabled = true;
            return view;
        }

        int IndexOf(int objectId)
        {
            for (int k = 0; k < m_ViewCount; k++)
                if (m_Views[k].DataInternal.Id == objectId) return k;
            return -1;
        }

        void Enqueue(int index, Mesh mesh, bool owned, MeshState state, string hash, Vector3 scale, float seconds)
        {
            RoomObjectView view = m_Views[index];
            bool queued = view.HasPending;
            if (queued) DropPending(view);           // a newer mesh for the same object replaces the waiting one
            view.HasPending = true;
            view.PendingMesh = mesh;
            view.PendingOwned = owned;
            view.PendingState = state;
            view.PendingHash = hash;
            view.PendingScale = scale;
            view.PendingSeconds = seconds;
            if (!queued)
            {
                m_Queue[(m_QueueHead + m_QueueCount) % m_Queue.Length] = index;
                m_QueueCount++;
            }
            BeginQueuedSwaps();
        }

        // At most one swap begins per frame, so the worst frame during furniture arrival stays short
        // (Master Plan section 7.4).
        void BeginQueuedSwaps()
        {
            while (m_QueueCount > 0 && (Instant || m_SwapsThisFrame == 0))
            {
                int index = m_Queue[m_QueueHead];
                m_QueueHead = (m_QueueHead + 1) % m_Queue.Length;
                m_QueueCount--;
                m_SwapsThisFrame++;
                BeginSwap(m_Views[index]);
            }
        }

        void BeginSwap(RoomObjectView view)
        {
            if (view.Fading) FinishFade(view);
            float seconds = Instant ? 0f : view.PendingSeconds;
            // What is drawn now fades out; the other copy, if there is one, goes at once.
            Mesh drawn = view.ShowingLight ? view.LightMesh : view.Mesh;
            bool drawnOwned = view.ShowingLight || view.OwnsMesh;
            int drawnTriangles = view.TriangleCount;
            if (seconds > 0f)
            {
                // The old mesh fades out by dither on a material of its own while the new one fades in.
                if (view.OutObject == null)
                {
                    view.OutObject = CreateRenderer("Outgoing", view.AnchorObject.transform, out view.OutFilter, out view.OutRenderer);
                }
                view.OutMesh = drawn;
                view.OutOwned = drawnOwned;
                view.OutTriangles = drawnTriangles;
                view.OutMaterial = m_Pool.Rent();
                view.OutMaterial.CopyPropertiesFromMaterial(view.MaterialInternal);
                view.OutObject.transform.localScale = view.MeshTransformInternal.localScale;
                view.OutFilter.sharedMesh = view.OutMesh;
                view.OutRenderer.sharedMaterial = view.OutMaterial;
                view.OutRenderer.enabled = true;
                view.Fading = true;
                view.FadeTime = 0f;
                view.FadeSeconds = seconds;
                m_FadingCount++;
                SetOpacity(view.MaterialInternal, 0f);
                SetOpacity(view.OutMaterial, 1f);
            }
            else if (drawnOwned)
            {
                DestroyObject(drawn);
            }
            if (drawn != view.Mesh && view.OwnsMesh) DestroyObject(view.Mesh);
            if (drawn != view.LightMesh) DestroyObject(view.LightMesh);

            view.Mesh = view.PendingMesh;
            view.OwnsMesh = view.PendingOwned;
            view.Triangles = TriangleCount(view.Mesh);
            view.LightMesh = view.PendingLightMesh;
            view.LightTriangles = view.LightMesh != null ? TriangleCount(view.LightMesh) : 0;
            view.DataInternal.AssetHashLight = view.LightMesh != null ? view.PendingLightHash : "";
            view.PendingLightMesh = null;
            view.PendingLightHash = null;
            ApplyDrawn(view);
            view.MeshTransformInternal.localScale = view.PendingScale;
            view.ShownInternal = view.PendingState;
            view.DataInternal.Mesh = view.PendingState;
            view.DataInternal.AssetHash = view.PendingState == MeshState.Ready ? view.PendingHash : "";
            view.HasPending = false;
            view.PendingMesh = null;
            RaiseObjectChanged(view.DataInternal.Id);
        }

        // Puts the mesh of the current level on the filter: the light copy at Light when the object has one,
        // the full copy otherwise. At once, no cross-fade, no allocation.
        void ApplyDrawn(RoomObjectView view)
        {
            bool light = RoomObjectMeshes.DrawsLight(m_Level, view.LightMesh != null);
            view.ShowingLight = light;
            Mesh mesh = light ? view.LightMesh : view.Mesh;
            if (view.Filter.sharedMesh != mesh) view.Filter.sharedMesh = mesh;
        }

        // A curved space writes the object's anchor to its material every frame, before this runs (the space's
        // Upload comes first in step 6): the mesh that is fading out stays on the object, under the same warp
        // and with the same bounds (Master Plan section 4.6).
        static void FollowWarp(RoomObjectView view)
        {
            float mode = view.MaterialInternal.GetFloat(AtlasShaderIDs.WarpMode);
            view.OutMaterial.SetFloat(AtlasShaderIDs.WarpMode, mode);
            if (mode < 0.5f) return;
            view.OutMaterial.SetMatrix(AtlasShaderIDs.Anchor, view.MaterialInternal.GetMatrix(AtlasShaderIDs.Anchor));
            view.OutRenderer.localBounds = view.MeshRenderer.localBounds;
        }

        void FinishFade(RoomObjectView view)
        {
            view.OutRenderer.ResetLocalBounds();
            view.Fading = false;
            m_FadingCount--;
            SetOpacity(view.MaterialInternal, 1f);
            view.OutRenderer.enabled = false;
            view.OutRenderer.sharedMaterial = null;
            view.OutFilter.sharedMesh = null;
            m_Pool.Return(view.OutMaterial);
            view.OutMaterial = null;
            if (view.OutOwned) DestroyObject(view.OutMesh);
            view.OutMesh = null;
            view.OutTriangles = 0;
        }

        static void DropPending(RoomObjectView view)
        {
            if (!view.HasPending) return;
            if (view.PendingOwned) DestroyObject(view.PendingMesh);
            DestroyObject(view.PendingLightMesh);
            view.PendingMesh = null;
            view.PendingLightMesh = null;
            view.PendingLightHash = null;
            view.HasPending = false;
        }

        void ApplyWalls()
        {
            if (m_Room == null)
            {
                m_WallRenderer.enabled = false;
                return;
            }
            WallShown shown = m_Sink.Shown;
            Mesh mesh = shown == WallShown.Kerbs ? m_KerbMesh : m_WallMesh;
            int triangles = shown == WallShown.Kerbs ? m_KerbTriangles : m_WallTriangles;
            bool visible = shown != WallShown.None && triangles > 0;
            if (visible)
            {
                if (m_WallFilter.sharedMesh != mesh) m_WallFilter.sharedMesh = mesh;
                // Sinking scales the walls in height only; the room frame's floor plane is untouched.
                m_WallObject.transform.localScale = new Vector3(1f, math.max(m_Sink.ScaleY, 1e-4f), 1f);
            }
            if (m_WallRenderer.enabled != visible) m_WallRenderer.enabled = visible;
        }

        float KerbRatio(RoomModel room)
        {
            float height = room != null ? room.MaxWallHeight : RoomModel.DefaultWallHeight;
            return height > 1e-3f ? math.saturate(m_KerbHeight / height) : 1f;
        }

        void RaiseObjectChanged(int objectId)
        {
            Action<int> changed = ObjectChanged;
            if (changed != null) changed(objectId);
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(RoomContent));
        }

        // Opacity is the alpha of the base tint; the shader dithers it. The tint's colour is left as a space set it.
        static void SetOpacity(Material material, float opacity)
        {
            Vector4 tint = material.GetVector(AtlasShaderIDs.BaseTint);
            tint.w = opacity;
            material.SetVector(AtlasShaderIDs.BaseTint, tint);
        }

        static int TriangleCount(Mesh mesh)
        {
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
            return (int)(indices / 3);
        }

        static GameObject CreateRenderer(string name, Transform parent, out MeshFilter filter, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            // The look has no shadows, probes or motion vectors (contract README F7).
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.enabled = false;
            return go;
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
