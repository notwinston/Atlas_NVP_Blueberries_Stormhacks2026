using System;
using Atlas.Blades.Network.Core;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// Draws what <see cref="NetworkTwins"/> holds (section 4 V2, section 6.4): the caption chip (hop count and stretch of
    /// the last packet), the packet chip, the rule chip, and the gamepad cursor on the node the stick chose. Chips are a
    /// squircle in the look's shader (one pooled material, as the Playground's chips and the exhibit signs) with a line of
    /// legacy 3D text; the cursor is a flat puck of <see cref="AtlasMeshBuilder"/> under the cursor node. Built in code,
    /// hidden with the twins. A text is assigned only when it changed; nothing allocates per frame.
    /// </summary>
    public sealed class NetworkChipView : IDisposable
    {
        const int FontPixels = 64;
        const float CornerRadius = 0.035f;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);

        sealed class Chip
        {
            public GameObject Root;
            public Mesh Mesh;
            public TextMesh Text;
        }

        readonly NetworkTwins m_Twins;
        readonly NetworkFloorMesher m_Mesher;
        readonly MaterialPool m_Pool;
        readonly Material m_PanelMaterial, m_CursorMaterial;
        readonly Chip[] m_Chips = new Chip[3];
        readonly GameObject m_Cursor;
        readonly Mesh m_CursorMesh;
        int m_CaptionRevision = -1;
        RoutingRule m_ShownRule;
        bool m_RuleDrawn;
        float m_ShownScale = -1f;
        int m_ShownCursor = -1;
        float2 m_ShownCentre = new float2(float.NaN, float.NaN);
        bool m_Shown, m_Disposed;

        public NetworkChipView(NetworkTwins twins, NetworkFloorMesher mesher, MaterialPool pool)
        {
            if (twins == null) throw new ArgumentNullException(nameof(twins));
            if (mesher == null) throw new ArgumentNullException(nameof(mesher));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            m_Twins = twins; m_Mesher = mesher; m_Pool = pool;

            m_PanelMaterial = pool.Rent();
            MaterialPool.SetTint(m_PanelMaterial, new float3(1f, 1f, 1f), 0.85f);
            m_Chips[NetworkTwins.PacketChipIndex] = Build("Network Packet Chip", NetworkTwins.PacketChipIndex, NetworkTwins.ChipHalfSize);
            m_Chips[NetworkTwins.RuleChipIndex] = Build("Network Rule Chip", NetworkTwins.RuleChipIndex, NetworkTwins.ChipHalfSize);
            m_Chips[NetworkTwins.CaptionChipIndex] = Build("Network Caption Chip", NetworkTwins.CaptionChipIndex, NetworkTwins.CaptionHalfSize);
            m_Chips[NetworkTwins.PacketChipIndex].Text.text = "Send packet";

            m_CursorMaterial = pool.Rent();
            MaterialPool.SetTintIndex(m_CursorMaterial, AtlasPalette.HomeBeacon);
            var b = new AtlasMeshBuilder(64, 192);
            b.AddPost(new float3(0f, NetworkFloorMesher.FloorLift, 0f), NetworkTwins.NodeTargetRadius,
                NetworkFloorMesher.HighlightLift, AtlasPalette.Uv(AtlasPalette.HomeBeacon));
            m_CursorMesh = AtlasMesh.Create(b, "Network Cursor");
            m_Cursor = new GameObject("Network Cursor");
            m_Cursor.AddComponent<MeshFilter>().sharedMesh = m_CursorMesh;
            Configure(m_Cursor.AddComponent<MeshRenderer>(), m_CursorMaterial);
            SetShown(false);
        }

        public bool Shown { get { return m_Shown; } }
        public Transform CaptionChip { get { return m_Chips[NetworkTwins.CaptionChipIndex].Root.transform; } }
        public Transform PacketChip { get { return m_Chips[NetworkTwins.PacketChipIndex].Root.transform; } }
        public Transform RuleChip { get { return m_Chips[NetworkTwins.RuleChipIndex].Root.transform; } }
        public Transform CursorPuck { get { return m_Cursor.transform; } }
        public string CaptionText { get { return m_Chips[NetworkTwins.CaptionChipIndex].Text.text; } }
        public string RuleText { get { return m_Chips[NetworkTwins.RuleChipIndex].Text.text; } }

        Chip Build(string name, int index, float2 half)
        {
            var c = new Chip();
            c.Root = new GameObject(name);
            var builder = new AtlasMeshBuilder(40, 112);
            builder.AddSquircle(float3.zero, half.x, half.y, math.min(CornerRadius, half.y), AtlasPalette.Uv(AtlasPalette.FogBase));
            c.Mesh = AtlasMesh.Create(builder, name);
            c.Root.AddComponent<MeshFilter>().sharedMesh = c.Mesh;
            Configure(c.Root.AddComponent<MeshRenderer>(), m_PanelMaterial);
            c.Root.transform.localRotation = NetworkTwins.ChipRotation(index);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                var textObject = new GameObject("Text");
                textObject.transform.SetParent(c.Root.transform, false);
                textObject.transform.localPosition = new Vector3(0f, 0f, -0.001f);
                c.Text = textObject.AddComponent<TextMesh>();
                MeshRenderer r = textObject.GetComponent<MeshRenderer>();
                if (r == null) r = textObject.AddComponent<MeshRenderer>();
                Configure(r, font.material);
                c.Text.font = font;
                c.Text.fontSize = FontPixels;
                c.Text.characterSize = NetworkTwins.ChipLineHeight * 10f / FontPixels;
                c.Text.anchor = TextAnchor.MiddleCenter;
                c.Text.alignment = TextAlignment.Left;
                c.Text.richText = false;
                c.Text.color = s_text;
            }
            return c;
        }

        static void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        void SetShown(bool shown)
        {
            m_Shown = shown;
            m_Cursor.SetActive(shown);
            for (int i = 0; i < m_Chips.Length; i++) m_Chips[i].Root.SetActive(shown && (i != NetworkTwins.CaptionChipIndex || m_Chips[i].Text == null || m_Chips[i].Text.text.Length > 0));
        }

        /// <summary>Once per frame, after the twins' update.</summary>
        public void Update()
        {
            if (m_Disposed) return;
            if (m_Twins.Visible != m_Shown) { SetShown(m_Twins.Visible); m_CaptionRevision = -1; }
            if (!m_Shown) return;

            float scale = m_Twins.ScaleX;
            if (scale != m_ShownScale)
            {
                m_ShownScale = scale;
                for (int i = 0; i < m_Chips.Length; i++)
                {
                    float3 c = NetworkTwins.ChipCentre(i, scale);
                    m_Chips[i].Root.transform.localPosition = new Vector3(c.x, c.y, c.z);
                }
                m_ShownCursor = -1;
            }
            if (m_Twins.Caption.Revision != m_CaptionRevision)
            {
                m_CaptionRevision = m_Twins.Caption.Revision;
                Chip cap = m_Chips[NetworkTwins.CaptionChipIndex];
                if (cap.Text != null) cap.Text.text = m_Twins.Caption.Text;
                cap.Root.SetActive(m_Twins.Caption.Text.Length > 0);
            }
            if (!m_RuleDrawn || m_Twins.Rule != m_ShownRule)
            {
                m_ShownRule = m_Twins.Rule; m_RuleDrawn = true;
                Chip rule = m_Chips[NetworkTwins.RuleChipIndex];
                if (rule.Text != null) rule.Text.text = "Rule: " + NetworkEvents.RuleName(m_ShownRule);
            }
            // The cursor puck stands on the node; the node moves while the map glides.
            float3 p = m_Mesher.NodePosition(m_Twins.Cursor);
            if (m_Twins.Cursor != m_ShownCursor || p.x != m_ShownCentre.x || p.z != m_ShownCentre.y)
            {
                m_ShownCursor = m_Twins.Cursor;
                m_ShownCentre = new float2(p.x, p.z);
                m_Cursor.transform.localPosition = new Vector3(p.x * m_Twins.ScaleX, 0f, p.z * m_Twins.ScaleZ);
            }
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            for (int i = 0; i < m_Chips.Length; i++)
            {
                Kill(m_Chips[i].Root);
                Kill(m_Chips[i].Mesh);
            }
            Kill(m_Cursor);
            Kill(m_CursorMesh);
            if (m_Pool.Owns(m_PanelMaterial)) m_Pool.Return(m_PanelMaterial);
            if (m_Pool.Owns(m_CursorMaterial)) m_Pool.Return(m_CursorMaterial);
        }
    }
}
