using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Input
{
    /// <summary>
    /// The head-locked gaze reticle (lane C contract section 13.2): a dot 2 m ahead under the HUD parent, with
    /// a ring that fills clockwise as the dwell runs. Over a target nearer than 2 m it comes forward to that
    /// target's distance, scaled so that it looks the same size, or it would be seen double there
    /// (<see cref="ReticleDepth"/>). Built in code (README F3). The ring's progress is the number of indices
    /// drawn, so a frame costs at most one sub-mesh update and no allocation.
    /// </summary>
    public sealed class GazeReticle
    {
        public const string ShaderResource = "AtlasReticle";
        /// <summary>Where the reticle stands with nothing near in focus, and the distance its mesh is sized for.</summary>
        public const float DistanceMetres = ReticleDepth.FarMetres;

        private const int DotSegments = 16;
        private const int RingSegments = 48;
        private const float DotRadius = 0.007f;          // 0.2 degrees at 2 m
        private const float RingInner = 0.020f;
        private const float RingOuter = 0.027f;
        private const int DotIndexCount = DotSegments * 3;

        private readonly GameObject _object;
        private readonly MeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Material _ownMaterial;
        private readonly Bounds _bounds;
        private readonly int _vertexCount;
        private int _shownSegments = -1;
        private bool _visible = true;
        private float _metres = DistanceMetres;

        /// <summary>
        /// Creates the reticle under the HUD parent. It draws with its own unlit shader from Resources; the
        /// material given is used only when that shader cannot be loaded.
        /// </summary>
        public GazeReticle(Transform hud, Material fallbackMaterial)
        {
            _object = new GameObject("Gaze Reticle");
            _object.layer = hud.gameObject.layer;
            Transform t = _object.transform;
            t.SetParent(hud, false);
            t.localPosition = new Vector3(0f, 0f, DistanceMetres);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            _mesh = BuildMesh(out _vertexCount);
            _bounds = new Bounds(Vector3.zero, new Vector3(RingOuter * 2f, RingOuter * 2f, 0.001f));
            _mesh.bounds = _bounds;

            Material material = fallbackMaterial;
            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader != null)
            {
                _ownMaterial = new Material(shader) { name = "Gaze Reticle", hideFlags = HideFlags.DontSave };
                _ownMaterial.SetColor("_Color", Color.white);      // palette cell Reticle (204) is #FFFFFF
                material = _ownMaterial;
            }

            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (material == null) _renderer.enabled = false;

            Set(false, 0f);
        }

        /// <summary>
        /// Shows or hides the reticle, sets the ring (0 = dot only, 1 = full ring) and how far ahead it stands.
        /// At any distance it fills the same angle of the view.
        /// </summary>
        public void Set(bool visible, float progress, float metres = DistanceMetres)
        {
            if (_object == null) return;                 // destroyed with the scene
            if (visible != _visible)
            {
                _visible = visible;
                _object.SetActive(visible);
            }
            if (!visible) return;

            if (metres != _metres && metres > 0f)
            {
                _metres = metres;
                float scale = ReticleDepth.Scale(metres);
                Transform t = _object.transform;
                t.localPosition = new Vector3(0f, 0f, metres);
                t.localScale = new Vector3(scale, scale, 1f);
            }

            int segments = Mathf.Clamp(Mathf.CeilToInt(progress * RingSegments - 0.001f), 0, RingSegments);
            if (segments == _shownSegments) return;
            _shownSegments = segments;
            var sub = new SubMeshDescriptor(0, DotIndexCount + segments * 6, MeshTopology.Triangles)
            {
                bounds = _bounds,
                firstVertex = 0,
                vertexCount = _vertexCount
            };
            _mesh.SetSubMesh(0, sub, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        public void Dispose()
        {
            Kill(_object);
            Kill(_mesh);
            Kill(_ownMaterial);
        }

        // The same in play mode and in an edit-mode test, where Destroy is not allowed.
        private static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        // Dot first, then the ring's segments in order from 12 o'clock clockwise, so that drawing a prefix of
        // the index buffer draws the dot and the filled part of the ring. Triangles face the viewer (-Z).
        private static Mesh BuildMesh(out int vertexCount)
        {
            vertexCount = 1 + DotSegments + (RingSegments + 1) * 2;
            var vertices = new Vector3[vertexCount];
            var triangles = new int[DotIndexCount + RingSegments * 6];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < DotSegments; i++)
            {
                float a = i * (2f * Mathf.PI / DotSegments);
                vertices[1 + i] = new Vector3(Mathf.Sin(a) * DotRadius, Mathf.Cos(a) * DotRadius, 0f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + i;
                triangles[i * 3 + 2] = 1 + (i + 1) % DotSegments;
            }

            int ring = 1 + DotSegments;
            for (int i = 0; i <= RingSegments; i++)
            {
                float a = i * (2f * Mathf.PI / RingSegments);
                float x = Mathf.Sin(a);
                float y = Mathf.Cos(a);
                vertices[ring + i * 2] = new Vector3(x * RingInner, y * RingInner, 0f);
                vertices[ring + i * 2 + 1] = new Vector3(x * RingOuter, y * RingOuter, 0f);
            }
            for (int i = 0; i < RingSegments; i++)
            {
                int inner = ring + i * 2;
                int k = DotIndexCount + i * 6;
                triangles[k] = inner;
                triangles[k + 1] = inner + 1;
                triangles[k + 2] = inner + 3;
                triangles[k + 3] = inner;
                triangles[k + 4] = inner + 3;
                triangles[k + 5] = inner + 2;
            }

            var mesh = new Mesh { name = "Gaze Reticle", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            return mesh;
        }
    }
}
