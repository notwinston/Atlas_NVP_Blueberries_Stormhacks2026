using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App
{
    /// <summary>
    /// The built-in minimal space: the scanned room as it stands (the room content draws floor, walls and
    /// objects one to one) on a lit grid ground that runs out to the horizon. It takes the place of Home
    /// while no Home space is registered, so the app starts, runs and switches on every build. Registered to
    /// the room: its transform is the identity of the room frame (contract README F4).
    /// </summary>
    public sealed class GridSpace : MonoBehaviour, ISpaceModule
    {
        /// <summary>Half the side of the ground square, beyond the default fog end. Metres.</summary>
        public const float GroundHalfSize = 16f;
        /// <summary>The ground lies this far under the room's floor, so the two never fight for depth. Metres.</summary>
        public const float GroundDrop = 0.02f;
        /// <summary>Cell side of the ground mesh. Coarse: this space is flat and never warped. Metres.</summary>
        public const float GroundCell = 1f;

        MaterialPool _pool;
        Material _material;
        Mesh _mesh;
        GameObject _ground;
        IRoomContent _room;

        /// <summary>The factory given to SpaceRegistry: a new object named like the Home scene.</summary>
        public static ISpaceModule Create()
        {
            return new GameObject(SpaceIds.SceneName(SpaceId.Home)).AddComponent<GridSpace>();
        }

        public SpaceId Id { get { return SpaceId.Home; } }
        public bool Registered { get { return true; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            AtlasServices.TryGet(out _room);
            if (!AtlasServices.TryGet(out _pool)) return;     // no look in this build: the room content alone

            var data = new AtlasMeshBuilder(2048, 8192);
            float h = GroundHalfSize;
            data.AddFloorGrid(new float2(-h, -h), new float2(h, h), -GroundDrop, AtlasPalette.Uv(AtlasPalette.FloorAlt),
                AtlasMeshBuilder.GridMetres, 1f, 255, GroundCell);
            _mesh = AtlasMesh.Create(data, "Grid Ground");

            _material = _pool.Rent();
            // Line coordinates are in units of 0.5 m: a faint line every unit, a clear one every metre.
            MaterialPool.SetLines(_material, 0.35f, 0.8f, new float2(2f, 2f));

            _ground = new GameObject("Ground");
            _ground.transform.SetParent(transform, false);
            _ground.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var meshRenderer = _ground.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            _ground.SetActive(false);
        }

        public void Enter(in WalkerPose start)
        {
            if (_room != null && _room.IsBuilt) _room.SetWallMode(WallMode.Full, 0f);
            if (_ground != null) _ground.SetActive(true);
        }

        public void Tick(in FrameContext frame) { }

        public void Exit()
        {
            if (_ground != null) _ground.SetActive(false);
            if (_material != null && _pool != null)
            {
                var meshRenderer = _ground != null ? _ground.GetComponent<MeshRenderer>() : null;
                if (meshRenderer != null) meshRenderer.sharedMaterial = null;
                _pool.Return(_material);
                _material = null;
            }
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }
        }

        public void WriteState(ref StateMessage state) { }
    }
}
