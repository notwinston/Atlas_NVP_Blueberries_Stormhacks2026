using Atlas.App.Menu;
using Atlas.Rendering;
using Atlas.World;
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
    /// <para>
    /// In a hands session the signs come within reach (documents/ATLAS_Hands_Design.md section 6.1): the same
    /// root then stands in the near anchor's frame, the boards are built again on their slots at the scale
    /// given, and everything is on the world layer, where the look's shader tests the boards against depth
    /// and the text is on <see cref="NearText"/>, so that a finger in front of a sign covers it.
    /// </para>
    /// </summary>
    sealed class SpaceSignsView : ISpaceSignView, System.IDisposable
    {
        const int FontPixels = 64;
        const float TextHeight = 0.04f;
        /// <summary>The text stands this far in front of its board, so it is not lost in it.</summary>
        const float TextLift = 0.002f;
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
        readonly MeshRenderer[] _textRenderers = new MeshRenderer[SpaceSignLayout.Count];
        readonly Font _font;
        readonly int _hudLayer;
        bool _near;
        float _scale = 1f;

        SpaceSignsView(Transform hud, MaterialPool pool)
        {
            _pool = pool;
            _material = pool.Rent();
            // Head-locked content: no fog, and the fade of a space switch does not touch it.
            MaterialPool.SetSurface(_material, 1f, 0f, 0f, 0f);

            _hudLayer = hud.gameObject.layer;
            _root = new GameObject("AtlasSpaceSigns");
            _root.layer = _hudLayer;
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
            _font = font;
            if (font != null)
            {
                for (int i = 0; i < _texts.Length; i++)
                {
                    _texts[i] = NewText(font, SpaceSignLayout.TextOf(i), i);
                    _textRenderers[i] = _texts[i].GetComponent<MeshRenderer>();
                }
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

        public void Place(float3 origin, quaternion frame)
        {
            if (_root != null) _root.transform.SetPositionAndRotation(origin, frame);
        }

        public void SetReach(bool near, float scale)
        {
            if (_root == null) return;
            float size = near ? scale : 1f;
            if (near == _near && size == _scale) return;
            _near = near;
            _scale = size;

            // A child does not take its parent's layer: the root and each text are set one by one.
            int layer = near ? PortalStencilTable.WorldLayer : _hudLayer;
            _root.layer = layer;
            Material text = near ? NearText.For(_font) : null;
            if (text == null && _font != null) text = _font.material;
            var whole = new Vector3(size, size, size);
            for (int i = 0; i < _texts.Length; i++)
            {
                if (_texts[i] == null) continue;
                Transform t = _texts[i].transform;
                t.gameObject.layer = layer;
                t.localPosition = TextPlace(i);
                t.localScale = whole;
                _textRenderers[i].sharedMaterial = text;
            }

            if (_mesh == null) return;
            Rebuild();
            AtlasMesh.Apply(_builder, _mesh);
        }

        public void Dispose()
        {
            AppObjects.Kill(_root);
            AppObjects.Kill(_mesh);
            if (_material != null && _pool.Owns(_material)) _pool.Return(_material);
        }

        // Centre of a sign in the root's frame: the heading frame at the head, with the signs a metre ahead; or
        // the near anchor's frame, with each sign on its slot in the anchor's plane.
        float3 Centre(int sign)
        {
            return _near ? ReachSlots.Local(SpaceSignLayout.SlotOf(sign), _scale) : SpaceSignLayout.Centre(sign);
        }

        // A hair in front of the board, towards the walker, so the text is not lost in it.
        Vector3 TextPlace(int sign)
        {
            float3 centre = Centre(sign);
            return new Vector3(centre.x, centre.y, centre.z - TextLift * _scale);
        }

        // A board faces the walker: the front of a quad is towards cross(v, u).
        void Rebuild()
        {
            _builder.Clear();
            float halfWidth = SpaceSignLayout.BoardHalfWidth * _scale, halfHeight = SpaceSignLayout.BoardHalfHeight * _scale;
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                float3 c = Centre(i);
                int cell = !_available[i] ? DimCell : i == SpaceSignLayout.Home ? AtlasPalette.HomeBeacon : ActiveCell;
                _builder.AddSquircle(c, halfWidth, halfHeight, BoardRadius * _scale, AtlasPalette.Uv(cell));
            }
        }

        TextMesh NewText(Font font, string text, int sign)
        {
            var go = new GameObject("Sign");
            go.layer = _root.layer;
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = TextPlace(sign);
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
