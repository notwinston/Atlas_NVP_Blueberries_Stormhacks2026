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
    /// <para>With an <see cref="OnPhoneObjectsCore"/> beside the Forge's core, an <see cref="AssetRoute"/> chooses per
    /// room which of the two serves it (standalone-phone plan section 6.5): the on-phone path when forge.backend is
    /// off, today's round trip otherwise. The client also decodes the crops for the on-phone path, on the main
    /// thread, and receives the scanned mesh as the scan's <see cref="IScanMeshSink"/>.</para>
    /// </summary>
    public sealed class AssetClient : IObjectPhotoSink, IScanMeshSink, IAfterFrameStep, IAssetRoom
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

        readonly AssetClientCore _core;
        readonly OnPhoneObjectsCore _onPhone;
        readonly AssetRoute _route;
        // The on-phone path's JPEG decoder, when this client made it: its texture goes with the client.
        UnityCropDecoder _decoder;
        readonly Dictionary<int, Mesh> _builtIn = new Dictionary<int, Mesh>();
        // A replaced first shape is still drawn until its swap has begun and the cross-fade has ended.
        readonly RetiredMeshes<Mesh> _retired = new RetiredMeshes<Mesh>();
        // Made once: a method group converted every frame would allocate.
        readonly Func<Mesh, bool> _isDrawn;
        readonly Action<Mesh> _destroy = DestroyMesh;
        readonly Action<RoomModel> _onBuilt;
        ushort[] _index16 = new ushort[16384];
        IRoomContent _content;

        public AssetClient(AssetClientCore core) : this(core, null) { }

        /// <param name="onPhone">The on-phone path, or null for the Forge's round trip alone.</param>
        public AssetClient(AssetClientCore core, OnPhoneObjectsCore onPhone)
        {
            if (core == null) throw new ArgumentNullException("core");
            _core = core;
            _onPhone = onPhone;
            _route = new AssetRoute(core, onPhone);
            _onBuilt = OnBuilt;
            _isDrawn = IsDrawn;
        }

        /// <summary>
        /// Creates the client with its cores. The cores show their results through the client, so they are made
        /// together.
        /// </summary>
        /// <param name="props">The prop library of the on-phone path; null leaves the Forge's round trip alone.</param>
        public static AssetClient Create(IForgeTransport transport, IAssetCache cache, IAssetWorker worker,
                                         FallbackSet fallback, IEventLog events, PropSet props = null)
        {
            var relay = new RoomRelay();
            var core = new AssetClientCore(transport, relay, cache, worker, fallback, events);
            UnityCropDecoder decoder = props != null ? new UnityCropDecoder() : null;
            OnPhoneObjectsCore onPhone = props != null ? new OnPhoneObjectsCore(relay, props, worker, fallback, events, decoder) : null;
            var client = new AssetClient(core, onPhone) { _decoder = decoder };
            relay.Target = client;
            return client;
        }

        /// <summary>The Forge's state machine, for the menu, the HUD and tests.</summary>
        public AssetClientCore Core { get { return _core; } }
        /// <summary>The on-phone path, or null.</summary>
        public OnPhoneObjectsCore OnPhone { get { return _onPhone; } }
        /// <summary>Which core serves the room in force.</summary>
        public AssetRoute Route { get { return _route; } }

        /// <summary>
        /// Raised once per scanned room served on the phone when every object has its final mesh and the room's
        /// objects say so: the app saves a kept room again (plan section 6.5).
        /// </summary>
        public event Action<RoomModel> ObjectsSettled
        {
            add { if (_onPhone != null) _onPhone.Settled += value; }
            remove { if (_onPhone != null) _onPhone.Settled -= value; }
        }

        /// <summary>The last GET /health answered 200.</summary>
        public bool ForgeReachable { get { return _core.ForgeReachable; } }
        /// <summary>Telemetry.assetsPlaced: of the core that serves the room.</summary>
        public int PlacedCount { get { return _route.PlacedCount; } }
        /// <summary>Telemetry.assetsPending: of the core that serves the room.</summary>
        public int PendingCount { get { return _route.PendingCount; } }
        public int BuiltInCount { get { return _route.BuiltInCount; } }
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

        // IObjectPhotoSink (lane C section 11.4) and IScanMeshSink (plan D8): to the core that serves the room.
        public void BeginRoom(RoomModel room, string roomJson) { _route.BeginRoom(room, roomJson); }
        public void Submit(ObjectPhotoSet photos) { _route.Submit(photos); }
        public void ReportNoPhoto(int objectId) { _route.ReportNoPhoto(objectId); }
        public void EndRoom() { _route.EndRoom(); }
        public void SetScanMesh(string roomId, ScanMesh mesh) { _route.SetScanMesh(roomId, mesh); }

        // The link (section 2.10)
        public void OnJobStatus(JobStatusMessage status) { _core.OnJobStatus(status); }
        public void OnAssetReady(AssetReadyMessage ready) { _core.OnAssetReady(ready); }

        /// <summary>DELETE /rooms/{roomId} and removal of this room's cached GLBs (Master Plan section 13.5).</summary>
        public void ForgetRoom(string roomId) { _route.ForgetRoom(roomId); }

        /// <summary>Step 8b: advances the requests and the on-phone builds, and places what is ready.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            _route.Tick(frame.Time, frame.DeltaTime * 1000f);
            _retired.Sweep(_isDrawn, _destroy);
        }

        /// <summary>Destroys the built-in meshes this client still owns. Call when the app shuts down.</summary>
        public void Dispose()
        {
            Bind(null);
            ReleaseBuiltIn();
            _retired.DestroyAll(_destroy);
            if (_decoder != null) _decoder.Dispose();
        }

        void OnBuilt(RoomModel room)
        {
            // The room content has rebuilt its views, so nothing draws the built-in meshes of the room before.
            ReleaseBuiltIn();
            _route.RoomBuilt(room);
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

        bool IAssetRoom.PlaceGeneratedLight(int objectId, MeshData data, string sha256)
        {
            if (_content == null || data == null || data.TriangleCount == 0) return false;
            Mesh mesh = CreateMesh(data, "Forge Object (light) " + objectId);
            // The room content owns a light mesh from here on, as it owns the full one.
            if (!_content.SetGeneratedLightMesh(objectId, mesh, sha256)) { DestroyMesh(mesh); return false; }
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

        bool IAssetRoom.PlaceOnPhone(int objectId, MeshData data, MeshState state, string prop, byte[] propCells)
        {
            if (_content == null || data == null || data.TriangleCount == 0) return false;
            if (state != MeshState.Matched && state != MeshState.ScanCut) return false;
            Mesh mesh = CreateMesh(data, (state == MeshState.Matched ? "Prop Object " : "Cut Object ") + objectId);
            // The room content owns an on-phone mesh from here on, as it owns a generated one.
            if (!_content.SetOnPhoneMesh(objectId, mesh, state)) { DestroyMesh(mesh); return false; }
            IRoomObjectView view;
            if (_content.TryGetObject(objectId, out view) && view.Data != null)
            {
                bool matched = state == MeshState.Matched;
                view.Data.Prop = matched ? prop ?? string.Empty : string.Empty;
                view.Data.PropCells = matched && propCells != null && propCells.Length > 0 ? (byte[])propCells.Clone() : null;
            }
            Retire(objectId);
            return true;
        }

        bool IsDrawn(Mesh mesh)
        {
            return _content != null && _content.IsMeshDrawn(mesh);
        }

        void Retire(int objectId)
        {
            Mesh old;
            if (!_builtIn.TryGetValue(objectId, out old)) return;
            _builtIn.Remove(objectId);
            _retired.Add(old);
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
            public bool PlaceGeneratedLight(int objectId, MeshData mesh, string sha256) { return Target != null && Target.PlaceGeneratedLight(objectId, mesh, sha256); }
            public bool PlaceBuiltIn(int objectId, MeshData mesh) { return Target != null && Target.PlaceBuiltIn(objectId, mesh); }
            public bool PlaceOnPhone(int objectId, MeshData mesh, MeshState state, string prop, byte[] propCells)
            {
                return Target != null && Target.PlaceOnPhone(objectId, mesh, state, prop, propCells);
            }
        }

        // The crops of the on-phone path as pictures: Unity's JPEG decoder, main thread, one texture reused.
        sealed class UnityCropDecoder : ICropDecoder
        {
            Texture2D _texture;

            public bool TryDecode(byte[] jpeg, out int width, out int height, out byte[] rgb)
            {
                width = height = 0;
                rgb = null;
                if (jpeg == null || jpeg.Length == 0) return false;
                if (_texture == null) _texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                // markNonReadable false: the pixels are read back below.
                if (!_texture.LoadImage(jpeg, false)) return false;
                width = _texture.width;
                height = _texture.height;
                Color32[] pixels = _texture.GetPixels32();
                rgb = new byte[width * height * 3];
                // Unity's row 0 is the bottom; the picture's row 0 is the top.
                for (int row = 0; row < height; row++)
                {
                    int from = (height - 1 - row) * width, to = row * width * 3;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 c = pixels[from + x];
                        rgb[to++] = c.r;
                        rgb[to++] = c.g;
                        rgb[to++] = c.b;
                    }
                }
                // The texture keeps the last crop's pixels until the next one; a small one is put back in its place.
                _texture.Reinitialize(2, 2);
                return true;
            }

            public void Dispose()
            {
                if (_texture == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_texture);
                else UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }
        }
    }

    /// <summary>
    /// Which core serves a room (standalone-phone plan section 6.5): the on-phone path when there is one and
    /// forge.backend is <c>off</c> as the room starts, the Forge's round trip otherwise. The choice is made when a
    /// room's first event arrives (its scanned mesh, its photos or its build) and holds for that room; the core that
    /// does not serve it hears nothing of it, so the two never place meshes on the same objects. Engine-free.
    /// </summary>
    public sealed class AssetRoute : IObjectPhotoSink, IScanMeshSink
    {
        readonly AssetClientCore _forge;
        readonly OnPhoneObjectsCore _onPhone;
        string _roomId;
        bool _servesOnPhone;

        public AssetRoute(AssetClientCore forge, OnPhoneObjectsCore onPhone)
        {
            if (forge == null) throw new ArgumentNullException("forge");
            _forge = forge;
            _onPhone = onPhone;
        }

        /// <summary>True when the on-phone path serves the room in force (or the next one, before any room).</summary>
        public bool ServesOnPhone { get { return _roomId != null ? _servesOnPhone : WouldServeOnPhone; } }
        /// <summary>What a room starting now would get.</summary>
        public bool WouldServeOnPhone { get { return _onPhone != null && _forge.Backend == AssetClientCore.BackendOff; } }

        public int PlacedCount { get { return ServesOnPhone ? _onPhone.PlacedCount : _forge.PlacedCount; } }
        public int PendingCount { get { return ServesOnPhone ? _onPhone.PendingCount : _forge.PendingCount; } }
        public int BuiltInCount { get { return ServesOnPhone ? _onPhone.BuiltInCount : _forge.BuiltInCount; } }

        // A new room id: the route is chosen for it, and the core that will not serve it lets go of what it followed.
        void Choose(string roomId)
        {
            roomId = roomId ?? string.Empty;
            if (roomId == _roomId) return;
            _roomId = roomId;
            _servesOnPhone = WouldServeOnPhone;
            if (_servesOnPhone) _forge.RoomCleared();
            else if (_onPhone != null) _onPhone.RoomCleared();
        }

        public void SetScanMesh(string roomId, ScanMesh mesh)
        {
            Choose(roomId);
            if (_servesOnPhone) _onPhone.SetScanMesh(roomId, mesh);
        }

        public void RoomBuilt(RoomModel room)
        {
            if (room == null)
            {
                _forge.RoomCleared();
                if (_onPhone != null) _onPhone.RoomCleared();
                _roomId = null;
                return;
            }
            Choose(room.Id);
            if (_servesOnPhone) _onPhone.RoomBuilt(room);
            else _forge.RoomBuilt(room);
        }

        public void BeginRoom(RoomModel room, string roomJson)
        {
            if (room == null) return;
            Choose(room.Id);
            if (_servesOnPhone) _onPhone.BeginRoom(room, roomJson);
            else _forge.BeginRoom(room, roomJson);
        }

        public void Submit(ObjectPhotoSet photos)
        {
            if (_servesOnPhone) _onPhone.Submit(photos);
            else _forge.Submit(photos);
        }

        public void ReportNoPhoto(int objectId)
        {
            if (_servesOnPhone) _onPhone.ReportNoPhoto(objectId);
            else _forge.ReportNoPhoto(objectId);
        }

        public void EndRoom()
        {
            if (_servesOnPhone) _onPhone.EndRoom();
            else _forge.EndRoom();
        }

        /// <summary>The Forge forgets the room (when it is used at all); the on-phone path drops it when it is the one in force.</summary>
        public void ForgetRoom(string roomId)
        {
            _forge.ForgetRoom(roomId);
            if (_onPhone != null && !string.IsNullOrEmpty(roomId) && roomId == _onPhone.RoomId)
            {
                _onPhone.RoomCleared();
                if (roomId == _roomId) _roomId = null;
            }
        }

        /// <summary>Both cores advance: the one that serves no room has nothing to do but finish a DELETE.</summary>
        public void Tick(double timeSeconds, float frameMs)
        {
            _forge.Tick(timeSeconds, frameMs);
            if (_onPhone != null) _onPhone.Tick(timeSeconds);
        }
    }
}
