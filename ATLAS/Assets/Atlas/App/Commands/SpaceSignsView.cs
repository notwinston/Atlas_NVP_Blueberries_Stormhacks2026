using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App
{
    /// <summary>
    /// What the SpaceLab signs draw (<see cref="SpaceSigns"/>), built in code under the HUD parent the way the
    /// menu's chip is (contract README F3): one mesh of four flat boards in the look's shader and four short
    /// texts. Two cells of the palette's neutral ramp mark a sign that can act and one that cannot; the home
    /// beacon takes the palette's beacon accent. One draw call for the boards, the mesh is rebuilt only when a
    /// sign's availability changes.
    /// </summary>
    sealed class SpaceSignsView : ISpaceSignView, System.IDisposable
    {
        const int FontPixels = 64;
        const float TextHeight = 0.04f;
        // Cells of the neutral ramp: a sign that can act, one that cannot.
        const int ActiveCell = 3, DimCell = 1;
        /// <summary>The boards' squircle corners.</summary>
        const float BoardRadius = 0.02f;

        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);

        readonly MaterialPool _pool;
        readonly Material _material;
        readonly AtlasMeshBuilder _builder = new AtlasMeshBuilder(160, 448);
        readonly GameObject _root;
        readonly Mesh _mesh;
        readonly bool[] _available = new bool[SpaceSignLayout.Count];
        readonly TextMesh[] _texts = new TextMesh[SpaceSignLayout.Count];

        SpaceSignsView(Transform hud, MaterialPool pool)
        {
            _pool = pool;
            _material = pool.Rent();
            // Head-locked content: no fog, and the fade of a space switch does not touch it.
            MaterialPool.SetSurface(_material, 1f, 0f, 0f, 0f);

            _root = new GameObject("AtlasSpaceSigns");
            _root.layer = hud.gameObject.layer;
            _root.transform.SetParent(hud, false);
            Rebuild();
            _mesh = AtlasMesh.Create(_builder, "Atlas Space Signs");
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                for (int i = 0; i < _texts.Length; i++)
                    _texts[i] = NewText(font, SpaceSignLayout.TextOf(i), SpaceSignLayout.Centre(i));
            }
            _root.SetActive(false);
        }

        /// <summary>Builds the objects under the HUD parent with one material from the pool. Hidden.</summary>
        public static SpaceSignsView Create(Transform hud, MaterialPool pool)
        {
            return new SpaceSignsView(hud, pool);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        public void SetAvailable(int sign, bool available)
        {
            if (_available[sign] == available) return;
            _available[sign] = available;
            if (_mesh == null) return;
            Rebuild();
            AtlasMesh.Apply(_builder, _mesh);
        }

        public void Place(float3 head, quaternion frame)
        {
            if (_root != null) _root.transform.SetPositionAndRotation(head, frame);
        }

        public void Dispose()
        {
            AppObjects.Kill(_root);
            AppObjects.Kill(_mesh);
            if (_material != null && _pool.Owns(_material)) _pool.Return(_material);
        }

        // A board faces the walker: the front of a quad is towards cross(v, u).
        void Rebuild()
        {
            _builder.Clear();
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                float3 c = SpaceSignLayout.Centre(i);
                int cell = !_available[i] ? DimCell : i == SpaceSignLayout.Home ? AtlasPalette.HomeBeacon : ActiveCell;
                _builder.AddSquircle(c, SpaceSignLayout.BoardHalfWidth, SpaceSignLayout.BoardHalfHeight,
                    BoardRadius, AtlasPalette.Uv(cell));
            }
        }

        TextMesh NewText(Font font, string text, float3 centre)
        {
            var go = new GameObject("Sign");
            go.layer = _root.layer;
            go.transform.SetParent(_root.transform, false);
            // A hair in front of the board, so the text is not lost in it.
            go.transform.localPosition = new Vector3(centre.x, centre.y, centre.z - 0.002f);
            var mesh = go.AddComponent<TextMesh>();
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            mesh.font = font;
            mesh.fontSize = FontPixels;
            mesh.characterSize = TextHeight * 10f / FontPixels;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.richText = false;
            mesh.color = s_text;
            mesh.text = text;
            return mesh;
        }
    }
}
