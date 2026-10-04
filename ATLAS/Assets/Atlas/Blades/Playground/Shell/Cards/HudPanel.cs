using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// A flat panel with legacy 3D text, head-locked under the HUD parent: the way the on-device menu and the HUD
    /// draw (lane D contract, section 5.7). One quad in the look's shader (one pooled material) and one text
    /// object. Anchored to the body, never warped (Master Plan section 10.1.4). Built in code (README F3).
    /// </summary>
    sealed class HudPanel
    {
        const int FontPixels = 64;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);
        // Neutral ramp cell used by the menu for its panel.
        const int PanelCell = 1;
        const float CornerRadius = 0.035f;

        readonly MaterialPool _pool;
        readonly Material _material;
        readonly GameObject _root;
        readonly Mesh _mesh;
        readonly TextMesh _text;
        readonly float _halfWidth;
        readonly float _halfHeight;
        float _alpha = -1f;
        float3 _tint = new float3(1f, 1f, 1f);

        public HudPanel(string name, Transform hud, MaterialPool pool, float3 centre, float halfWidth, float halfHeight,
                        float lineHeight, TextAnchor anchor, float textX, bool headLocked = true)
        {
            _pool = pool;
            _halfWidth = halfWidth;
            _halfHeight = halfHeight;
            _root = new GameObject(name);
            _root.layer = hud.gameObject.layer;
            _root.transform.SetParent(hud, false);
            _root.transform.localPosition = new Vector3(centre.x, centre.y, centre.z);
            _root.transform.localRotation = Quaternion.identity;

            if (pool != null)
            {
                _material = pool.Rent();
                // Head-locked: no fog, and the fade of a space switch does not touch it. A sign in the room takes both.
                MaterialPool.SetSurface(_material, 1f, headLocked ? 0f : 1f, 0f, headLocked ? 0f : 1f);
                var builder = new AtlasMeshBuilder(40, 112);
                // Squircle corners; a one-line chip comes out a capsule.
                builder.AddSquircle(float3.zero, halfWidth, halfHeight, math.min(CornerRadius, halfHeight),
                                    AtlasPalette.Uv(PanelCell));
                _mesh = AtlasMesh.Create(builder, name);
                _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
                MeshRenderer renderer = _root.AddComponent<MeshRenderer>();
                Configure(renderer, _material);
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                var textObject = new GameObject("Text");
                textObject.layer = _root.layer;
                textObject.transform.SetParent(_root.transform, false);
                textObject.transform.localPosition = new Vector3(textX, 0f, -0.001f);
                _text = textObject.AddComponent<TextMesh>();
                MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = textObject.AddComponent<MeshRenderer>();
                Configure(renderer, font.material);
                _text.font = font;
                _text.fontSize = FontPixels;
                // A line of legacy 3D text is characterSize * fontSize / 10 units tall.
                _text.characterSize = lineHeight * 10f / FontPixels;
                _text.anchor = anchor;
                _text.alignment = TextAlignment.Left;
                _text.richText = false;
                _text.color = s_text;
            }
            SetAlpha(0f);
        }

        public float HalfWidth { get { return _halfWidth; } }
        public float HalfHeight { get { return _halfHeight; } }
        public Transform Transform { get { return _root != null ? _root.transform : null; } }

        /// <summary>One string assignment: callers do it only when their text changed.</summary>
        public void SetText(string text)
        {
            if (_text != null) _text.text = text;
        }

        /// <summary>0 hides the panel; between 0 and 1 the background dithers and the text fades.</summary>
        public void SetAlpha(float alpha)
        {
            if (_root == null) return;
            alpha = Mathf.Clamp01(alpha);
            if (Mathf.Approximately(alpha, _alpha)) return;
            _alpha = alpha;
            bool on = alpha > 0.001f;
            if (_root.activeSelf != on) _root.SetActive(on);
            if (!on) return;
            if (_material != null) MaterialPool.SetTint(_material, _tint, alpha * 0.85f);
            if (_text != null)
            {
                Color c = s_text;
                c.a = alpha;
                _text.color = c;
            }
        }

        /// <summary>Colour of the panel (linear rgb), for a highlight.</summary>
        public void SetTint(float3 linearRgb)
        {
            _tint = linearRgb;
            if (_material != null && _alpha > 0.001f) MaterialPool.SetTint(_material, _tint, _alpha * 0.85f);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mesh != null) Object.Destroy(_mesh);
            if (_material != null && _pool != null && _pool.Owns(_material)) _pool.Return(_material);
        }

        static void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    /// <summary>The places of the chip and the card in the HUD frame (x right, y up, z forward, metres).</summary>
    public static class CardLayout
    {
        /// <summary>The caption chip: one line low and centred, 1.2 m ahead (about 14 degrees below the horizon).</summary>
        public static readonly float3 ChipCentre = new float3(0f, -0.30f, 1.2f);
        public const float ChipHalfWidth = 0.42f, ChipHalfHeight = 0.035f;
        public const float ChipLineHeight = 0.034f;

        /// <summary>The prompt chip of the tutorial (task P2-C09): one line above the caption chip.</summary>
        public static readonly float3 PromptCentre = new float3(0f, -0.23f, 1.2f);
        public const float PromptHalfWidth = 0.5f, PromptHalfHeight = 0.035f;
        public const float PromptLineHeight = 0.03f;

        /// <summary>The equation card: right of centre, 1.2 m ahead.</summary>
        public static readonly float3 CardCentre = new float3(0.46f, -0.08f, 1.2f);
        public const float CardHalfWidth = 0.22f, CardHalfHeight = 0.14f;
        public const float CardLineHeight = 0.028f;
    }
}
