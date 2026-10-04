using System.Text;
using Atlas.Rendering;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// What the Slicer shows in words (task P2-A01, the toybox's read-outs and labels): a panel above the body with the body's
    /// name, its slice, the cut on the w range, the three turns, the view and the tour's step; a label over each gaze handle,
    /// lit while its control acts; and a line on the floor at the start of the w lane. Text is TextMeshPro in the world, as
    /// Curved Walk's chips (both eyes draw it at its real depth); the plate behind the panel is the look's own box, drawn with
    /// the fixtures' material. The panel and the labels turn to face the walker about the vertical only. The panel's text is
    /// rebuilt only when a shown digit changes (<see cref="SlicerReadout.Key"/>), handed over without a string.
    /// </summary>
    sealed class SlicerPanel
    {
        /// <summary>Line height of the panel and of the labels, metres.</summary>
        public const float PanelLine = 0.032f, LabelLine = 0.026f;
        public const float PanelWidth = 0.74f, PanelHeight = 0.5f;
        /// <summary>Gap between the top of the body and the bottom of the panel, metres.</summary>
        public const float PanelGap = 0.12f;
        /// <summary>How long a chip's label stays lit after its action fires, seconds.</summary>
        public const float FlashSeconds = 0.35f;

        // One point of a TextMeshPro world text is a tenth of a unit, and a line of the default font is about 1.15 times its
        // point size (as the telemetry HUD).
        const float PointsPerLineMetre = 10f / 1.15f;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);
        static readonly Color s_dim = new Color(0.70f, 0.75f, 0.78f, 1f);
        static readonly Color s_lit = new Color(1.00f, 0.86f, 0.30f, 1f);

        /// <summary>The labels, in the order of <see cref="LabelAt"/>: cut up and down, then each turn's minus and plus, then
        /// the two chips.</summary>
        public static readonly string[] Labels =
        {
            "w +", "w -", "XW -", "XW +", "YW -", "YW +", "ZW -", "ZW +", "next body", "shadow",
        };
        public const int LabelCount = 10, NextLabel = 8, ShadowLabel = 9;

        readonly GameObject m_PanelObject;
        readonly TextMeshPro m_Panel;
        readonly Mesh m_PlateMesh;
        readonly GameObject[] m_LabelObjects = new GameObject[LabelCount];
        readonly TextMeshPro[] m_LabelTexts = new TextMeshPro[LabelCount];
        readonly bool[] m_LabelLit = new bool[LabelCount];
        readonly float[] m_Flash = new float[LabelCount];
        readonly GameObject m_LaneObject;
        readonly StringBuilder m_Text = new StringBuilder(512);
        long m_ShownKey = long.MinValue;

        public bool HasText { get { return m_Panel != null; } }
        public TextMeshPro Text { get { return m_Panel; } }
        public string LabelText(int i) { return m_LabelTexts[i] != null ? m_LabelTexts[i].text : null; }

        /// <summary>Where label <paramref name="i"/> of <see cref="Labels"/> hangs: just over its handle.</summary>
        public static float3 LabelAt(in SlicerLayout layout, int i)
        {
            float3 at;
            if (i == 0) at = layout.CutHandle(+1);
            else if (i == 1) at = layout.CutHandle(-1);
            else if (i < NextLabel) at = layout.TurnHandle((i - 2) / 2, (i - 2) % 2 == 0 ? -1 : +1);
            else if (i == NextLabel) at = layout.NextObjectChip;
            else at = layout.ViewChip;
            return at + new float3(0f, SlicerLayout.HandleBox * 0.5f + 0.035f, 0f);
        }

        /// <summary>Where the panel's centre is: above the body, facing the room.</summary>
        public static float3 PanelCentre(in SlicerLayout layout)
        {
            return layout.Centre + new float3(0f, layout.Radius + PanelGap + 0.5f * PanelHeight, 0f);
        }

        /// <param name="root">The space's root (the room frame). <param name="plate">Material for the plate, or null for none.</param>
        public SlicerPanel(Transform root, in SlicerLayout layout, Material plate)
        {
            // The default font asset of the project's TextMeshPro resources. A build without them has no text and runs on.
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null || root == null) return;

            m_PanelObject = new GameObject("Slicer Panel") { hideFlags = HideFlags.DontSave };
            m_PanelObject.transform.SetParent(root, false);
            m_PanelObject.transform.localPosition = PanelCentre(in layout);
            m_Panel = MakeText(m_PanelObject.transform, "Text", font, PanelLine, new Vector2(PanelWidth - 0.06f, PanelHeight - 0.05f), true);
            m_Panel.alignment = TextAlignmentOptions.TopLeft;
            m_Panel.rectTransform.localPosition = new Vector3(0f, 0f, -0.012f);    // in front of the plate

            if (plate != null)
            {
                var builder = new AtlasMeshBuilder(32, 64);
                builder.AddBox(new float3(0f, -0.5f * PanelHeight, 0f), new float3(PanelWidth, PanelHeight, 0.012f), 0f,
                               AtlasPalette.Uv(AtlasPalette.Void), true, 0, 10f);
                m_PlateMesh = AtlasMesh.Create(builder, "Slicer Panel Plate");
                var go = new GameObject("Plate") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(m_PanelObject.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m_PlateMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = plate;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            for (int i = 0; i < LabelCount; i++)
            {
                TextMeshPro t = MakeText(root, "Slicer Label " + Labels[i], font, LabelLine, new Vector2(0.3f, LabelLine * 1.5f), false);
                t.SetText(Labels[i]);
                t.color = s_dim;
                t.rectTransform.localPosition = LabelAt(in layout, i);
                m_LabelTexts[i] = t;
                m_LabelObjects[i] = t.gameObject;
            }

            if (layout.HasLane)
            {
                // Flat on the floor beside the lane's near end, read standing on the lane looking along it.
                TextMeshPro lane = MakeText(root, "Slicer Lane Caption", font, LabelLine * 1.3f, new Vector2(1.2f, LabelLine * 3f), false);
                lane.SetText("walk along this line: it moves the cut through w");
                lane.color = s_dim;
                lane.rectTransform.localPosition = new Vector3(layout.LaneX + 0.45f, 0.006f, -0.5f * layout.LaneLength + 0.15f);
                lane.rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                m_LaneObject = lane.gameObject;
            }
        }

        static TextMeshPro MakeText(Transform parent, string name, TMP_FontAsset font, float lineHeight, Vector2 size, bool wrap)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSize = lineHeight * PointsPerLineMetre;
            t.enableAutoSizing = false;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            t.color = s_text;
            t.rectTransform.sizeDelta = size;
            Renderer r = t.renderer;
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return t;
        }

        /// <summary>
        /// Once a frame (step 6): the panel's text when it changed, the turn of the panel and the labels towards the head, and
        /// the labels' light. <paramref name="active"/>: one bit per label whose control acts this frame.
        /// </summary>
        public void Update(in SlicerReadoutState state, float3 head, int active, float deltaTime)
        {
            if (m_Panel == null) return;
            long key = SlicerReadout.Key(in state);
            if (key != m_ShownKey)
            {
                m_ShownKey = key;
                SlicerReadout.Build(m_Text, in state);
                m_Panel.SetText(m_Text);
            }
            Face(m_PanelObject.transform, head);
            for (int i = 0; i < LabelCount; i++)
            {
                if (m_LabelTexts[i] == null) continue;
                m_Flash[i] = math.max(0f, m_Flash[i] - deltaTime);
                bool lit = (active & (1 << i)) != 0 || m_Flash[i] > 0f;
                if (lit != m_LabelLit[i])
                {
                    m_LabelLit[i] = lit;
                    m_LabelTexts[i].color = lit ? s_lit : s_dim;
                }
                Face(m_LabelObjects[i].transform, head);
            }
        }

        /// <summary>Lights a label for <see cref="FlashSeconds"/>: its action fired.</summary>
        public void Flash(int label)
        {
            if ((uint)label < LabelCount) m_Flash[label] = FlashSeconds;
        }

        public void SetVisible(bool visible)
        {
            if (m_PanelObject != null && m_PanelObject.activeSelf != visible) m_PanelObject.SetActive(visible);
            for (int i = 0; i < LabelCount; i++)
                if (m_LabelObjects[i] != null && m_LabelObjects[i].activeSelf != visible) m_LabelObjects[i].SetActive(visible);
            if (m_LaneObject != null && m_LaneObject.activeSelf != visible) m_LaneObject.SetActive(visible);
        }

        // Turns a text about the vertical so that its front faces the head (a TextMeshPro text reads from its -z side).
        // The head is in the room frame, which is the world while a space runs (README F4).
        static void Face(Transform t, float3 head)
        {
            Vector3 away = t.position - (Vector3)head;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>The objects are children of the space's root and go with it; the plate's mesh is freed here.</summary>
        public void Dispose()
        {
            if (m_PlateMesh == null) return;
            if (Application.isPlaying) Object.Destroy(m_PlateMesh); else Object.DestroyImmediate(m_PlateMesh);
        }
    }
}
