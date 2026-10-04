using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The asset client of the running app (Master Plan section 7.4, tasks P1-D10 and P1-D12): the thin adapter
    /// around <see cref="AssetClientCore"/>. It is the photo sink of the scan, listens to the room content and
    /// to the link, ticks the core at step 8b, and turns the core's mesh data into meshes for the room content.
    /// It never touches a renderer or a material: the room content assigns the pooled look material (F7).
    /// </summary>
    public sealed class AssetClient : IObjectPhotoSink, IAfterFrameStep, IAssetRoom
    {
        public const float LoadedRoomGraceSeconds = AssetClientCore.LoadedRoomGraceSeconds;

        // Position, Normal, Color (UNorm8 x 4), TexCoord0, TexCoord1 in one stream: the AtlasVertex layout
        // (lane A section 10.2). Atlas.Telemetry cannot reference Atlas.Rendering, so the layout is repeated here.
        static readonly VertexAttributeDescriptor[] s_layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
        };

        const MeshUpdateFlags k_Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
        // A replaced built-in mesh is still drawn while the room content fades it out.
        const double RetireSeconds = 1.0;

        struct Retired { public Mesh Mesh; public double At; }

        readonly AssetClientCore _core;
        readonly Dictionary<int, Mesh> _builtIn = new Dictionary<int, Mesh>();
        readonly List<Retired> _retired = new List<Retired>();
        readonly Action<RoomModel> _onBuilt;
        ushort[] _index16 = new ushort[16384];
        IRoomContent _content;
        double _now;

        public AssetClient(AssetClientCore core)
        {
            if (core == null) throw new ArgumentNullException("core");
            _core = core;
            _onBuilt = OnBuilt;
        }

        /// <summary>
        /// Creates the client with its core. The core shows its results through the client, so the two are made
        /// together.
        /// </summary>
        public static AssetClient Create(IForgeTransport transport, IAssetCache cache, IAssetWorker worker,
                                         FallbackSet fallback, IEventLog events)
        {
            var relay = new RoomRelay();
            var client = new AssetClient(new AssetClientCore(transport, relay, cache, worker, fallback, events));
            relay.Target = client;
            return client;
        }

        /// <summary>The state machine, for the menu, the HUD and tests.</summary>
        public AssetClientCore Core { get { return _core; } }

        /// <summary>The last GET /health answered 200.</summary>
        public bool ForgeReachable { get { return _core.ForgeReachable; } }
        /// <summary>Telemetry.assetsPlaced.</summary>
        public int PlacedCount { get { return _core.PlacedCount; } }
        /// <summary>Telemetry.assetsPending.</summary>
        public int PendingCount { get { return _core.PendingCount; } }
        public int BuiltInCount { get { return _core.BuiltInCount; } }
        public ObjectAssetState GetState(int objectId) { return _core.GetState(objectId); }

        /// <summary>Follows this room content: its Built event starts a room. Null detaches.</summary>
        public void Bind(IRoomContent content)
        {
            if (ReferenceEquals(content, _content)) return;
            if (_content != null) _content.Built -= _onBuilt;
            _content = content;
            if (content == null) return;
            content.Built += _onBuilt;
            if (content.IsBuilt && content.Room != null) OnBuilt(content.Room);
        }

        // IObjectPhotoSink (lane C section 11.4)
        public void BeginRoom(RoomModel room, string roomJson) { _core.BeginRoom(room, roomJson); }
        public void Submit(ObjectPhotoSet photos) { _core.Submit(photos); }
        public void ReportNoPhoto(int objectId) { _core.ReportNoPhoto(objectId); }
        public void EndRoom() { _core.EndRoom(); }

        // The link (section 2.10)
        public void OnJobStatus(JobStatusMessage status) { _core.OnJobStatus(status); }
        public void OnAssetReady(AssetReadyMessage ready) { _core.OnAssetReady(ready); }

        /// <summary>DELETE /rooms/{roomId} and removal of this room's cached GLBs (Master Plan section 13.5).</summary>
        public void ForgetRoom(string roomId) { _core.ForgetRoom(roomId); }

        /// <summary>Step 8b: advances the requests and places at most one generated mesh.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            _now = frame.Time;
            _core.Tick(frame.Time, frame.DeltaTime * 1000f);
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                if (_now - _retired[i].At < RetireSeconds) continue;
                DestroyMesh(_retired[i].Mesh);
                _retired.RemoveAt(i);
            }
        }

        /// <summary>Destroys the built-in meshes this client still owns. Call when the app shuts down.</summary>
        public void Dispose()
        {
            Bind(null);
            ReleaseBuiltIn();
            for (int i = 0; i < _retired.Count; i++) DestroyMesh(_retired[i].Mesh);
            _retired.Clear();
        }

        void OnBuilt(RoomModel room)
        {
            // The room content has rebuilt its views, so nothing draws the built-in meshes of the room before.
            ReleaseBuiltIn();
            _core.RoomBuilt(room);
        }

        void ReleaseBuiltIn()
        {
            foreach (KeyValuePair<int, Mesh> pair in _builtIn) DestroyMesh(pair.Value);
            _builtIn.Clear();
        }

        // IAssetRoom: what the core asks of the room content.

        bool IAssetRoom.SetRequested(int objectId)
        {
            return _content != null && _content.SetObjectState(objectId, MeshState.Requested);
        }

        bool IAssetRoom.PlaceGenerated(int objectId, MeshData data, string sha256)
        {
            if (_content == null || data == null || data.TriangleCount == 0) return false;
            Mesh mesh = CreateMesh(data, "Forge Object " + objectId);
            // The room content owns a generated mesh from here on.
            if (!_content.SetGeneratedMesh(objectId, mesh, sha256)) { DestroyMesh(mesh); return false; }
            Retire(objectId);
            return true;
        }

        bool IAssetRoom.PlaceBuiltIn(int objectId, MeshData data)
        {
            if (_content == null || data == null || data.TriangleCount == 0) return false;
            Mesh mesh = CreateMesh(data, "Built-in Object " + objectId);
            // The room content never destroys a built-in mesh: it stays ours until it is replaced.
            if (!_content.SetBuiltInMesh(objectId, mesh, Vector3.one)) { DestroyMesh(mesh); return false; }
            Retire(objectId);
            _builtIn[objectId] = mesh;
            return true;
        }

        void Retire(int objectId)
        {
            Mesh old;
            if (!_builtIn.TryGetValue(objectId, out old)) return;
            _builtIn.Remove(objectId);
            _retired.Add(new Retired { Mesh = old, At = _now });
        }

        Mesh CreateMesh(MeshData data, string name)
        {
            int vertexCount = data.VertexCount, indexCount = data.IndexCount;
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertexBufferParams(vertexCount, s_layout);
            mesh.SetVertexBufferData(data.Vertices, 0, 0, vertexCount, 0, k_Flags);
            if (vertexCount <= 65535)
            {
                if (_index16.Length < indexCount) _index16 = new ushort[Mathf.NextPowerOfTwo(indexCount)];
                int[] source = data.Indices;
                for (int i = 0; i < indexCount; i++) _index16[i] = (ushort)source[i];
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
                mesh.SetIndexBufferData(_index16, 0, 0, indexCount, k_Flags);
            }
            else
            {
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                mesh.SetIndexBufferData(data.Indices, 0, 0, indexCount, k_Flags);
            }
            Vector3 min = new Vector3(data.BoundsMin.x, data.BoundsMin.y, data.BoundsMin.z);
            Vector3 max = new Vector3(data.BoundsMax.x, data.BoundsMax.y, data.BoundsMax.z);
            var bounds = new Bounds((min + max) * 0.5f, max - min);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
            {
                bounds = bounds, firstVertex = 0, vertexCount = vertexCount
            }, k_Flags);
            mesh.bounds = bounds;
            return mesh;
        }

        static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }

        // The core is created before the client that shows its results.
        sealed class RoomRelay : IAssetRoom
        {
            public IAssetRoom Target;
            public bool SetRequested(int objectId) { return Target != null && Target.SetRequested(objectId); }
            public bool PlaceGenerated(int objectId, MeshData mesh, string sha256) { return Target != null && Target.PlaceGenerated(objectId, mesh, sha256); }
            public bool PlaceBuiltIn(int objectId, MeshData mesh) { return Target != null && Target.PlaceBuiltIn(objectId, mesh); }
        }
    }
}
