using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spectator
{
    /// <summary>
    /// The Spectator's furniture in the running Station (lane D contract, section 5.8): the thin adapter round
    /// <see cref="SpectatorAssetsCore"/>. It listens to the link and to the room content, ticks the core at
    /// step 8b and turns the core's mesh data into meshes for the room content. It never touches a renderer
    /// or a material: the room content assigns the pooled look material (F7). Nothing is uploaded.
    /// </summary>
    public sealed class SpectatorFurniture : IAssetRoom, IAfterFrameStep, IDisposable
    {
        const MeshUpdateFlags k_Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
        // A replaced built-in mesh is still drawn while the room content fades it out.
        const double RetireSeconds = 1.0;

        struct Retired { public Mesh Mesh; public double At; }

        readonly SpectatorAssetsCore _core;
        readonly SpectatorCore _spectator;
        readonly IRoomContent _content;
        readonly Dictionary<int, Mesh> _builtIn = new Dictionary<int, Mesh>();
        readonly List<Retired> _retired = new List<Retired>();
        readonly Action<RoomModel> _onBuilt;
        ushort[] _index16 = new ushort[16384];
        LinkClient _link;
        double _now;
        bool _built, _disposed;

        /// <param name="transport">HTTP to the Forge; the Spectator's one transport.</param>
        /// <param name="spectator">Names the Forge the Hub announced.</param>
        /// <param name="content">The room content whose objects take the meshes.</param>
        public SpectatorFurniture(IForgeTransport transport, SpectatorCore spectator, IRoomContent content)
        {
            if (spectator == null) throw new ArgumentNullException("spectator");
            if (content == null) throw new ArgumentNullException("content");
            _spectator = spectator;
            _content = content;
            // persistentDataPath and Resources are read here, once, on the main thread.
            var cache = new FileAssetCache(Path.Combine(Application.persistentDataPath, "assets"));
            FallbackSet fallback = FallbackSet.Load(ReadFallbackResource);
            if (fallback.LastError != null) Debug.LogWarning("Atlas: Spectator: " + fallback.LastError);
            _core = new SpectatorAssetsCore(transport, this, cache, new ThreadPoolAssetWorker(), fallback);
            _onBuilt = OnBuilt;
            content.Built += _onBuilt;
            if (content.IsBuilt && content.Room != null) OnBuilt(content.Room);
        }

        /// <summary>The state machine, for the panels and tests.</summary>
        public SpectatorAssetsCore Core { get { return _core; } }

        /// <summary>Follows the phone's asset events and the Hub's job news on this link.</summary>
        public void Attach(LinkClient link)
        {
            Detach();
            _link = link;
            if (link == null) return;
            link.EventReceived += _core.OnEvent;
            link.AssetReadyReceived += _core.OnAssetReady;
            link.JobStatusReceived += _core.OnJobStatus;
        }

        public void Detach()
        {
            LinkClient link = _link;
            _link = null;
            if (link == null) return;
            link.EventReceived -= _core.OnEvent;
            link.AssetReadyReceived -= _core.OnAssetReady;
            link.JobStatusReceived -= _core.OnJobStatus;
        }

        /// <summary>Step 8b: advances the downloads and places at most one generated mesh.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            if (_disposed) return;
            _now = frame.Time;
            // The room content was cleared (a room that no longer matches the phone's): nothing can be placed.
            if (_built && !_content.IsBuilt)
            {
                _built = false;
                ReleaseBuiltIn();
                _core.RoomCleared();
            }
            _core.ForgeUrl = _spectator.ForgeUrl;
            _core.Tick(frame.Time);
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                if (_now - _retired[i].At < RetireSeconds) continue;
                DestroyMesh(_retired[i].Mesh);
                _retired.RemoveAt(i);
            }
        }

        /// <summary>Leaves the link and the room content and destroys the built-in meshes still owned here.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
            _content.Built -= _onBuilt;
            _core.RoomCleared();
            ReleaseBuiltIn();
            for (int i = 0; i < _retired.Count; i++) DestroyMesh(_retired[i].Mesh);
            _retired.Clear();
        }

        void OnBuilt(RoomModel room)
        {
            // The room content has rebuilt its views, so nothing draws the built-in meshes of the room before.
            ReleaseBuiltIn();
            _built = room != null;
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
            return _content.SetObjectState(objectId, MeshState.Requested);
        }

        bool IAssetRoom.PlaceGenerated(int objectId, MeshData data, string sha256)
        {
            if (!_content.IsBuilt || data == null || data.TriangleCount == 0) return false;
            Mesh mesh = CreateMesh(data, "Forge Object " + objectId);
            // The room content owns a generated mesh from here on.
            if (!_content.SetGeneratedMesh(objectId, mesh, sha256)) { DestroyMesh(mesh); return false; }
            Retire(objectId);
            return true;
        }

        // The Spectator stays at the full level: it never downloads a light copy (SpectatorAssetsCore ignores the
        // light fields of AssetReady) and sets no furniture level, so there is nothing to place here.
        bool IAssetRoom.PlaceGeneratedLight(int objectId, MeshData data, string sha256) { return false; }

        bool IAssetRoom.PlaceBuiltIn(int objectId, MeshData data)
        {
            if (!_content.IsBuilt || data == null || data.TriangleCount == 0) return false;
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

        // The shared vertex format in one stream (lane A contract, section 10.2), 16-bit indices where they fit.
        Mesh CreateMesh(MeshData data, string name)
        {
            int vertexCount = data.VertexCount, indexCount = data.IndexCount;
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertexBufferParams(vertexCount, AtlasMesh.Layout);
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

        // "manifest" and "<category>.glb": the built-in set next to the telemetry assembly, as the phone reads it.
        static byte[] ReadFallbackResource(string name)
        {
            var asset = Resources.Load<TextAsset>(FallbackSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }
    }
}
