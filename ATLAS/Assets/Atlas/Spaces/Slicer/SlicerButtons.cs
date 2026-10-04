using Atlas.Rendering;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The Slicer's buttons at its gaze handles (task P2-A01): a framed tile with an arrow at each end of the w bar, a pair of
    /// tiles with turning arrows for each plane with the plane's name and angle between them, and two pills, "next body" and
    /// "shadow", with their words on them. A yellow ring lights round a button while the gaze is on it or its control acts,
    /// and a bar fills along its foot as the dwell runs, so the walker sees the press coming. Each button turns to face the
    /// walker about the vertical. Meshes are the look's own, drawn with the fixtures' material; text is TextMeshPro and is
    /// left out when the build has no font. Built once per room; a frame moves, scales and switches objects only.
    /// </summary>
    sealed class SlicerButtons
    {
        /// <summary>The buttons, in the order of <see cref="Centre"/>: cut up and down, then each turn's minus and plus, then
        /// the two chips.</summary>
        public const int Count = 10, Next = 8, Shadow = 9;
        public const float LabelLine = 0.034f, PillLine = 0.03f, RowLine = 0.04f;
        /// <summary>How long a button stays lit after its action fires, seconds.</summary>
        public const float FlashSeconds = 0.35f;
        /// <summary>The icons' size, metres.</summary>
        public const float IconSize = 0.056f;

        const float PointsPerLineMetre = 10f / 1.15f;
        const int RimColour = AtlasPalette.DoorFrame, RingColour = AtlasPalette.Beacon, IconColour = AtlasPalette.Reticle;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);
        static readonly Color s_dim = new Color(0.70f, 0.75f, 0.78f, 1f);
        static readonly Color s_lit = new Color(1.00f, 0.86f, 0.30f, 1f);
        static readonly string[] s_rowNames = { "XW", "YW", "ZW" };

        readonly Transform[] m_Roots = new Transform[Count];
        readonly GameObject[] m_Rings = new GameObject[Count];
        readonly Transform[] m_Bars = new Transform[Count];
        readonly bool[] m_RingOn = new bool[Count];
        readonly float[] m_Flash = new float[Count];
        readonly Transform[] m_Rows = new Transform[3];
        readonly TextMeshPro[] m_RowTexts = new TextMeshPro[3];
        readonly int[] m_RowDegrees = { int.MinValue, int.MinValue, int.MinValue };
        readonly TextMeshPro[] m_PillTexts = new TextMeshPro[2];
        readonly Mesh[] m_Meshes = new Mesh[8];
        int m_MeshCount;
        int m_ShadowShown = -1;

        /// <summary>Where button i sits: on its gaze handle.</summary>
        public static float3 Centre(in SlicerLayout layout, int i)
        {
            if (i == 0) return layout.CutHandle(+1);
            if (i == 1) return layout.CutHandle(-1);
            if (i < Next) return layout.TurnHandle((i - 2) / 2, (i - 2) % 2 == 0 ? -1 : +1);
            return i == Next ? layout.NextObjectChip : layout.ViewChip;
        }

        /// <summary>The words on a turn plane's row label, or on a pill; null for the rest.</summary>
        public string RowText(int plane) { return m_RowTexts[plane] != null ? m_RowTexts[plane].text : null; }
        public string PillText(int pill) { return m_PillTexts[pill] != null ? m_PillTexts[pill].text : null; }
        public bool RingShown(int i) { return m_RingOn[i]; }

        public SlicerButtons(Transform root, in SlicerLayout layout, Material material)
        {
            if (root == null || material == null) return;
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            float2 tile = SlicerButtonShapes.TileHalf, pill = SlicerButtonShapes.PillHalf;

            var b = new AtlasMeshBuilder(256, 512);
            Mesh up = Make(b, tile, SlicerIcon.Up, SlicerMeshKernel.DepthUv(SlicerObjects.CutRange), "Slicer Button Up");
            Mesh down = Make(b, tile, SlicerIcon.Down, SlicerMeshKernel.DepthUv(-SlicerObjects.CutRange), "Slicer Button Down");
            Mesh turnPlus = Make(b, tile, SlicerIcon.TurnPlus, AtlasPalette.Uv(IconColour), "Slicer Button Turn +");
            Mesh turnMinus = Make(b, tile, SlicerIcon.TurnMinus, AtlasPalette.Uv(IconColour), "Slicer Button Turn -");
            Mesh pillMesh = Make(b, pill, SlicerIcon.None, default, "Slicer Button Pill");
            b.Clear();
            SlicerButtonShapes.AddRing(b, tile, RingColour);
            Mesh tileRing = Keep(AtlasMesh.Create(b, "Slicer Button Ring"));
            b.Clear();
            SlicerButtonShapes.AddRing(b, pill, RingColour);
            Mesh pillRing = Keep(AtlasMesh.Create(b, "Slicer Pill Ring"));
            b.Clear();
            SlicerButtonShapes.AddUnitBar(b, RingColour);
            Mesh bar = Keep(AtlasMesh.Create(b, "Slicer Button Bar"));

            for (int i = 0; i < Count; i++)
            {
                bool isPill = i >= Next;
                Mesh face = i == 0 ? up : i == 1 ? down : isPill ? pillMesh : (i % 2 == 1 ? turnPlus : turnMinus);
                float2 half = isPill ? pill : tile;

                var go = new GameObject("Slicer Button " + i) { hideFlags = HideFlags.DontSave };
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = Centre(in layout, i);
                m_Roots[i] = go.transform;
                MakeRenderer(go.transform, "Tile", face, material);
                m_Rings[i] = MakeRenderer(go.transform, "Ring", isPill ? pillRing : tileRing, material).gameObject;
                m_Rings[i].SetActive(false);
                m_Bars[i] = MakeRenderer(go.transform, "Dwell", bar, material);
                m_Bars[i].localPosition = SlicerButtonShapes.BarOrigin(half);
                m_Bars[i].localScale = new Vector3(0f, SlicerButtonShapes.BarHeight, 1f);
                m_Bars[i].gameObject.SetActive(false);

                if (font == null) continue;
                if (i < 2)
                {
                    // The cut's two tiles: what they push, over the top one and under the bottom one.
                    TextMeshPro t = Text(go.transform, font, LabelLine, new Vector2(0.2f, LabelLine * 1.4f));
                    t.SetText(i == 0 ? "w +" : "w −");
                    t.color = s_dim;
                    float dy = half.y + SlicerButtonShapes.Ring + 0.6f * LabelLine;
                    t.rectTransform.localPosition = new Vector3(0f, i == 0 ? dy : -dy, SlicerButtonShapes.IconZ);
                }
                else if (isPill)
                {
                    TextMeshPro t = Text(go.transform, font, PillLine, new Vector2(2f * half.x, 2f * half.y));
                    t.rectTransform.localPosition = new Vector3(0f, 0f, SlicerButtonShapes.IconZ);
                    t.SetText(i == Next ? "next body  ›" : "shadow  off");
                    m_PillTexts[i - Next] = t;
                }
            }

            if (font == null) return;
            for (int plane = 0; plane < 3; plane++)
            {
                // Each plane's name and angle, between its two tiles.
                var go = new GameObject("Slicer Row " + s_rowNames[plane]) { hideFlags = HideFlags.DontSave };
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = 0.5f * (layout.TurnHandle(plane, -1) + layout.TurnHandle(plane, +1));
                m_Rows[plane] = go.transform;
                TextMeshPro t = Text(go.transform, font, RowLine, new Vector2(0.14f, 2.4f * RowLine));
                t.richText = true;
                m_RowTexts[plane] = t;
            }
        }

        Mesh Make(AtlasMeshBuilder b, float2 half, SlicerIcon icon, float2 iconUv, string name)
        {
            b.Clear();
            SlicerButtonShapes.AddTile(b, half, RimColour);
            if (icon != SlicerIcon.None) SlicerButtonShapes.AddIcon(b, icon, IconSize, iconUv);
            return Keep(AtlasMesh.Create(b, name));
        }

        Mesh Keep(Mesh mesh)
        {
            m_Meshes[m_MeshCount++] = mesh;
            return mesh;
        }

        static Transform MakeRenderer(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        static TextMeshPro Text(Transform parent, TMP_FontAsset font, float lineHeight, Vector2 size)
        {
            var go = new GameObject("Text") { hideFlags = HideFlags.DontSave };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSize = lineHeight * PointsPerLineMetre;
            t.enableAutoSizing = false;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
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
        /// Once a frame (step 6). <paramref name="active"/>: one bit per button whose control acts. <paramref name="focused"/>:
        /// the button under the gaze (-1 for none) and <paramref name="progress"/> its dwell, 0..1. The turns are in radians.
        /// </summary>
        public void Update(float3 head, int active, int focused, float progress, float3 turns, bool shadowShown, float deltaTime)
        {
            for (int i = 0; i < Count; i++)
            {
                if (m_Roots[i] == null) continue;
                m_Flash[i] = math.max(0f, m_Flash[i] - deltaTime);
                bool ring = i == focused || (active & (1 << i)) != 0 || m_Flash[i] > 0f;
                if (ring != m_RingOn[i])
                {
                    m_RingOn[i] = ring;
                    m_Rings[i].SetActive(ring);
                }
                float fill = i == focused ? math.saturate(progress) : 0f;
                bool barOn = fill > 0f;
                if (m_Bars[i].gameObject.activeSelf != barOn) m_Bars[i].gameObject.SetActive(barOn);
                if (barOn)
                {
                    float2 half = i >= Next ? SlicerButtonShapes.PillHalf : SlicerButtonShapes.TileHalf;
                    m_Bars[i].localScale = new Vector3(fill * SlicerButtonShapes.BarLength(half), SlicerButtonShapes.BarHeight, 1f);
                }
                Face(m_Roots[i], head);
            }

            for (int plane = 0; plane < 3; plane++)
            {
                if (m_RowTexts[plane] == null) continue;
                int degrees = (int)math.round(math.degrees(SlicerController.WrapAngle(turns[plane])));
                if (degrees != m_RowDegrees[plane])
                {
                    m_RowDegrees[plane] = degrees;
                    m_RowTexts[plane].SetText(s_rowFormats[plane], degrees);
                }
                Face(m_Rows[plane], head);
            }

            int shown = shadowShown ? 1 : 0;
            if (shown != m_ShadowShown && m_PillTexts[1] != null)
            {
                m_ShadowShown = shown;
                m_PillTexts[1].SetText(shadowShown ? "shadow  on" : "shadow  off");
                m_PillTexts[1].color = shadowShown ? s_lit : s_text;
            }
        }

        static readonly string[] s_rowFormats =
        {
            "XW\n<size=75%><color=#8C99A6>{0:0}°</color></size>",
            "YW\n<size=75%><color=#8C99A6>{0:0}°</color></size>",
            "ZW\n<size=75%><color=#8C99A6>{0:0}°</color></size>",
        };

        /// <summary>Lights a button for <see cref="FlashSeconds"/>: its action fired.</summary>
        public void Flash(int button)
        {
            if ((uint)button < Count) m_Flash[button] = FlashSeconds;
        }

        public void SetVisible(bool visible)
        {
            for (int i = 0; i < Count; i++)
                if (m_Roots[i] != null && m_Roots[i].gameObject.activeSelf != visible) m_Roots[i].gameObject.SetActive(visible);
            for (int plane = 0; plane < 3; plane++)
                if (m_Rows[plane] != null && m_Rows[plane].gameObject.activeSelf != visible) m_Rows[plane].gameObject.SetActive(visible);
        }

        // Turns a button about the vertical so that its front (-z) faces the head.
        static void Face(Transform t, float3 head)
        {
            Vector3 away = t.position - (Vector3)head;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>The objects are children of the space's root and go with it; the meshes are freed here.</summary>
        public void Dispose()
        {
            for (int i = 0; i < m_MeshCount; i++)
            {
                if (m_Meshes[i] == null) continue;
                if (Application.isPlaying) Object.Destroy(m_Meshes[i]); else Object.DestroyImmediate(m_Meshes[i]);
                m_Meshes[i] = null;
            }
            m_MeshCount = 0;
        }
    }
}
