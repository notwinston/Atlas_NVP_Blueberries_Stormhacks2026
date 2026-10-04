using System;
using Atlas.Kernel;
using Atlas.World;
using UnityEngine.Rendering;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Shows a <see cref="FlatTwin"/> on a square panel: one mesh, one material of the map's own flat-colour
    /// shader, and a few lines of legacy 3D text for the labels (Master Plan task P1-D07). The same class is the
    /// headset's mini-map under the head-locked HUD root and the Station's panels beside the mirror. Everything
    /// is made in code. <see cref="Apply"/> rewrites the mesh in place and allocates nothing once the buffers
    /// have reached their working size.
    /// </summary>
    public sealed class FlatTwinPanel : IDisposable
    {
        public const string ShaderResource = "AtlasFlatTwin";
        const int FontPixels = 48;
        const MeshUpdateFlags k_Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        // The AtlasVertex layout (lane A contract, section 10.2). Atlas.Telemetry cannot reference
        // Atlas.Rendering, so the layout is repeated here, as the asset client does.
        static readonly VertexAttributeDescriptor[] s_layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
        };

        readonly FlatTwin _twin = new FlatTwin();
        readonly MeshData _data = new MeshData(4096, 8192);
        readonly float _halfSize;
        readonly bool _background;
        readonly GameObject _object;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly TextMesh[] _labels = new TextMesh[FlatTwin.MaxLabels];
        readonly GameObject[] _labelObjects = new GameObject[FlatTwin.MaxLabels];
        readonly string[] _labelShown = new string[FlatTwin.MaxLabels];
        ushort[] _index16 = new ushort[8192];
        int _vertexCapacity, _indexCapacity;
        bool _visible = true;
        NetworkDisc _network;

        /// <param name="parent">The panel's parent; the panel takes its layer. Null builds no objects.</param>
        /// <param name="name">The object's name.</param>
        /// <param name="size">Side of the square, in the parent's units.</param>
        /// <param name="background">A dark square under the map, for a panel in front of a world.</param>
        /// <param name="textHeight">Height of one line of a label, in the parent's units.</param>
        public FlatTwinPanel(Transform parent, string name, float size, bool background, float textHeight)
        {
            _halfSize = 0.5f * size;
            _background = background;
            if (parent == null) return;

            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null)
            {
                Debug.LogWarning("Atlas: shader '" + ShaderResource + "' is not in Resources; the flat twin is not shown.");
                return;
            }
            _material = new Material(shader) { name = "Flat Twin", hideFlags = HideFlags.DontSave };

            _object = new GameObject(name);
            _object.layer = parent.gameObject.layer;
            _object.transform.SetParent(parent, false);

            _mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _object.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return;
            for (int i = 0; i < _labels.Length; i++)
            {
                var go = new GameObject("Label");
                go.layer = _object.layer;
                go.transform.SetParent(_object.transform, false);
                var text = go.AddComponent<TextMesh>();
                var textRenderer = go.GetComponent<MeshRenderer>();
                if (textRenderer == null) textRenderer = go.AddComponent<MeshRenderer>();
                textRenderer.sharedMaterial = font.material;
                textRenderer.shadowCastingMode = ShadowCastingMode.Off;
                textRenderer.receiveShadows = false;
                text.font = font;
                text.fontSize = FontPixels;
                // A line of legacy 3D text is characterSize * fontSize / 10 units tall.
                text.characterSize = textHeight * 10f / FontPixels;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.richText = false;
                text.color = new Color(0.92f, 0.95f, 0.92f, 1f);
                go.SetActive(false);
                _labels[i] = text;
                _labelObjects[i] = go;
            }
        }

        /// <summary>The list this panel shows. Fill it, then call <see cref="Apply"/>.</summary>
        public FlatTwin Twin { get { return _twin; } }

        /// <summary>The panel's object, to place it; null when nothing was built.</summary>
        public Transform Transform { get { return _object != null ? _object.transform : null; } }

        /// <summary>Half the side of the square, in the parent's units.</summary>
        public float HalfSize { get { return _halfSize; } }

        /// <summary>Triangles in the mesh as last written.</summary>
        public int TriangleCount { get { return _data.TriangleCount; } }

        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (value == _visible) return;
                _visible = value;
                if (_object != null) _object.SetActive(value);
            }
        }

        /// <summary>Left-aligned labels read as a column of text (the Station's status lines).</summary>
        public void SetLabelAnchor(TextAnchor anchor, TextAlignment alignment)
        {
            for (int i = 0; i < _labels.Length; i++)
            {
                if (_labels[i] == null) continue;
                _labels[i].anchor = anchor;
                _labels[i].alignment = alignment;
            }
        }

        /// <summary>Shows a network disc (NET-04) in this panel; null goes back to the list the caller fills.</summary>
        public void ShowNetwork(NetworkDisc disc) { _network = disc; }

        /// <summary>The network disc this panel shows, or null.</summary>
        public NetworkDisc Network { get { return _network; } }

        /// <summary>Writes the list into the mesh and the labels. With a network disc shown, the list is its drawing.</summary>
        public void Apply()
        {
            if (_network != null) _network.Draw(_twin);
            if (_mesh == null) return;
            FlatTwinMesher.Write(_twin, _data, _halfSize, _background);
            Upload();

            int labels = _twin.LabelCount;
            for (int i = 0; i < _labels.Length; i++)
            {
                if (_labels[i] == null) continue;
                string text = null;
                float x = 0f, z = 0f;
                if (i < labels) text = _twin.GetLabel(i, out x, out z);
                if (text == null)
                {
                    if (_labelShown[i] != null)
                    {
                        _labelShown[i] = null;
                        _labelObjects[i].SetActive(false);
                    }
                    continue;
                }
                Unity.Mathematics.float2 p = FlatTwinMesher.ToPanel(_twin, x, z, _halfSize);
                // Just in front of the mesh, towards the viewer.
                _labelObjects[i].transform.localPosition = new Vector3(p.x, p.y, -0.001f * _halfSize);
                if (!ReferenceEquals(_labelShown[i], text))
                {
                    if (_labelShown[i] == null) _labelObjects[i].SetActive(true);
                    _labelShown[i] = text;
                    _labels[i].text = text;
                }
            }
        }

        void Upload()
        {
            int vertexCount = _data.VertexCount, indexCount = _data.IndexCount;
            if (vertexCount > _vertexCapacity)
            {
                _vertexCapacity = Mathf.NextPowerOfTwo(Mathf.Max(vertexCount, 1024));
                _mesh.SetVertexBufferParams(_vertexCapacity, s_layout);
            }
            if (indexCount > _indexCapacity)
            {
                _indexCapacity = Mathf.NextPowerOfTwo(Mathf.Max(indexCount, 2048));
                _mesh.SetIndexBufferParams(_indexCapacity, IndexFormat.UInt16);
                if (_index16.Length < _indexCapacity) _index16 = new ushort[_indexCapacity];
            }
            int[] source = _data.Indices;
            for (int i = 0; i < indexCount; i++) _index16[i] = (ushort)source[i];
            if (vertexCount > 0) _mesh.SetVertexBufferData(_data.Vertices, 0, 0, vertexCount, 0, k_Flags);
            if (indexCount > 0) _mesh.SetIndexBufferData(_index16, 0, 0, indexCount, k_Flags);

            // The panel never leaves its square, so its bounds are fixed.
            var bounds = new Bounds(Vector3.zero, new Vector3(2.4f * _halfSize, 2.4f * _halfSize, 0.01f * _halfSize));
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
            {
                bounds = bounds, firstVertex = 0, vertexCount = vertexCount
            }, k_Flags);
            _mesh.bounds = bounds;
        }

        public void Dispose()
        {
            Kill(_object);
            Kill(_mesh);
            Kill(_material);
        }

        static void Kill(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
