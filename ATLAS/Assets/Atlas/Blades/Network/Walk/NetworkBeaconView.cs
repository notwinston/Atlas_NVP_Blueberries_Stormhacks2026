using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Network.Walk
{
    // V3 beacons: one arrow per neighbour on a ring round the visitor, each along the log-map bearing. Driven by the module
    // (no Update). The ring is rebuilt only when the node changes, into a preallocated AtlasMeshBuilder and a mesh made once
    // (AtlasMeshBuilder, MaterialPool only; the material is the one shared look). The world fade of a hop is applied by
    // NetworkWingModule.FadeSink (contract section 9 C6).
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class NetworkBeaconView : MonoBehaviour
    {
        public const int MaxArrows = 32;
        public const float RingMetres = 0.6f, ArrowLength = 0.3f, ArrowWidth = 0.06f, ArrowHeight = 0.02f;

        readonly AtlasMeshBuilder _builder = new AtlasMeshBuilder(MaxArrows * 24, MaxArrows * 36);
        Mesh _mesh;
        Material _material;
        MaterialPool _pool;

        public int ArrowCount { get; private set; }

        // Appends one arrow per bearing (radians clockwise from forward) round `centre`. Public so a test can build the ring
        // without a scene. Returns the count (capped at MaxArrows).
        public static int AppendRing(AtlasMeshBuilder builder, double[] bearings, int count, float3 centre)
        {
            int n = math.min(count, MaxArrows);
            for (int i = 0; i < n; i++)
            {
                float yaw = (float)bearings[i];
                math.sincos(yaw, out float s, out float c);
                var at = new float3(centre.x + RingMetres * s, centre.y + ArrowHeight, centre.z + RingMetres * c);
                builder.AddBox(at, new float3(ArrowWidth, ArrowHeight, ArrowLength), yaw, float2.zero);
            }
            return n;
        }

        // Rebuilds the ring (call when the node changes, never per frame).
        public void SetBearings(double[] bearings, int count, float3 headPosition)
        {
            _builder.Clear();
            ArrowCount = AppendRing(_builder, bearings, count, new float3(headPosition.x, 0f, headPosition.z));
            if (_mesh == null)
            {
                _mesh = AtlasMesh.Create(_builder, "NetworkBeacons");
                GetComponent<MeshFilter>().sharedMesh = _mesh;
                if (_pool == null) _pool = MaterialPool.CreateDefault(4);
                _material = _pool.Rent();
                GetComponent<MeshRenderer>().sharedMaterial = _material;
            }
            else AtlasMesh.Apply(_builder, _mesh);
        }

        public void SetVisible(bool on) { gameObject.SetActive(on); }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_pool != null) { if (_material != null) _pool.Return(_material); _pool.Dispose(); }
        }
    }
}
