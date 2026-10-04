using System.Text;
using Atlas.Rendering;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// What the Slicer shows in words (task P2-A01, the toybox's read-outs and labels). An info card at head height beside the
    /// body, over the turn handles: the body's name, its slice as the headline in the slice's colour, the faces, a strip of
    /// the shapes met along w with a marker at the cut (<see cref="SlicerShapeStrip"/>), then the cut, the turns, the view
    /// and the tour's step; and a caption on the floor at the start of the w lane. The buttons are <see cref="SlicerButtons"/>. Text is TextMeshPro in the world, as Curved Walk's chips (both eyes draw it at its real depth);
    /// the plate and the strip are the look's own meshes, drawn with the fixtures' material. The card tips to face the
    /// walker's eyes. The card's text is rebuilt only when a shown digit or the
    /// strip changes (<see cref="SlicerReadout.Key"/>), handed over without a string.
    /// </summary>
    sealed class SlicerPanel
    {
        /// <summary>Base line height of the card and of the floor caption, metres.</summary>
        public const float CardLine = 0.034f, LaneLine = 0.06f;
        public const float CardWidth = 0.8f, CardHeight = 0.72f;
        /// <summary>Margin inside the card, height of its top block and of the strip, metres.</summary>
        public const float CardPad = 0.04f, HeaderHeight = 0.275f, StripHeight = 0.045f;
        /// <summary>Gap between the top row of turn buttons and the bottom of the card, metres.</summary>
        public const float CardGap = 0.07f;

        // One point of a TextMeshPro world text is a tenth of a unit, and a line of the default font is about 1.15 times its
        // point size (as the telemetry HUD).
        const float PointsPerLineMetre = 10f / 1.15f;
        const float TextDepth = -0.012f, StripDepth = -0.008f;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);
        static readonly Color s_dim = new Color(0.70f, 0.75f, 0.78f, 1f);

        readonly GameObject m_CardObject;
        readonly TextMeshPro m_Header, m_Footer;
        readonly Mesh m_PlateMesh, m_StripMesh, m_MarkerMesh;
        readonly Transform m_Marker;
        readonly AtlasMeshBuilder m_StripBuilder;
        readonly GameObject m_LaneObject;
        readonly StringBuilder m_Text = new StringBuilder(1024);
        long m_ShownKey = long.MinValue;
        int m_ShownStrip = int.MinValue;

        public bool HasText { get { return m_Header != null; } }
        /// <summary>The card's text, header then footer, rich-text tags included (allocates: for tests and logs).</summary>
        public string CardText { get { return m_Header != null ? m_Header.text + "\n" + m_Footer.text : null; } }

        /// <summary>Where the card's centre is: over the turn handles, beside the body, near head height.</summary>
        public static float3 CardCentre(in SlicerLayout layout)
        {
            float3 handles = 0.5f * (layout.TurnHandle(0, -1) + layout.TurnHandle(0, +1));
            float tileTop = SlicerButtonShapes.TileHalf.y + SlicerButtonShapes.Ring;
            return handles + new float3(0f, tileTop + CardGap + 0.5f * CardHeight, 0f);
        }

        /// <param name="root">The space's root (the room frame).</param>
        /// <param name="material">The fixtures' material, for the plate and the strip; null for text only.</param>
        public SlicerPanel(Transform root, in SlicerLayout layout, Material material)
        {
            // The default font asset of the project's TextMeshPro resources. A build without them has no text and runs on.
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null || root == null) return;

            m_CardObject = new GameObject("Slicer Card") { hideFlags = HideFlags.DontSave };
            m_CardObject.transform.SetParent(root, false);
            m_CardObject.transform.localPosition = CardCentre(in layout);
            Transform card = m_CardObject.transform;

            float inner = CardWidth - 2f * CardPad;
            float top = 0.5f * CardHeight - CardPad;
            m_Header = MakeText(card, "Header", font, CardLine, new Vector2(inner, HeaderHeight), false, true);
            m_Header.alignment = TextAlignmentOptions.TopLeft;
            m_Header.rectTransform.localPosition = new Vector3(0f, top - 0.5f * HeaderHeight, TextDepth);

            float stripY = top - HeaderHeight - 0.5f * StripHeight;
            float footerTop = stripY - 0.5f * StripHeight - 0.025f;
            float footerHeight = footerTop - (-0.5f * CardHeight + CardPad);
            m_Footer = MakeText(card, "Footer", font, CardLine, new Vector2(inner, footerHeight), true, true);
            m_Footer.alignment = TextAlignmentOptions.TopLeft;
            m_Footer.rectTransform.localPosition = new Vector3(0f, footerTop - 0.5f * footerHeight, TextDepth);

            if (material != null)
            {
                // The plate, with a lighter rim a little behind it.
                var builder = new AtlasMeshBuilder(64, 128);
                builder.AddBox(new float3(0f, -0.5f * CardHeight - 0.008f, 0.004f), new float3(CardWidth + 0.016f, CardHeight + 0.016f, 0.008f),
                               0f, AtlasPalette.Uv(AtlasPalette.Kerb), true, 0, 10f);
                builder.AddBox(new float3(0f, -0.5f * CardHeight, 0f), new float3(CardWidth, CardHeight, 0.012f), 0f,
                               AtlasPalette.Uv(AtlasPalette.Void), true, 0, 10f);
                m_PlateMesh = AtlasMesh.Create(builder, "Slicer Card Plate");
                MakeRenderer(card, "Plate", m_PlateMesh, material, Vector3.zero);

                // The strip is rebuilt when its shapes change; the marker only moves.
                m_StripBuilder = new AtlasMeshBuilder(4 * (SlicerShapeStrip.Samples + 1), 6 * (SlicerShapeStrip.Samples + 1));
                BuildStrip(null);
                m_StripMesh = AtlasMesh.Create(m_StripBuilder, "Slicer Card Strip");
                Transform strip = MakeRenderer(card, "Strip", m_StripMesh, material, new Vector3(0f, stripY, StripDepth));

                builder.Clear();
                builder.AddBox(new float3(0f, -0.5f * StripHeight - 0.014f, 0f), new float3(0.01f, StripHeight + 0.028f, 0.008f), 0f,
                               AtlasPalette.Uv(AtlasPalette.Reticle), true, 0, 10f);
                m_MarkerMesh = AtlasMesh.Create(builder, "Slicer Card Marker");
                m_Marker = MakeRenderer(strip, "Marker", m_MarkerMesh, material, new Vector3(0f, 0f, -0.004f));
            }

            if (layout.HasLane)
            {
                // Flat on the floor beside the lane's near end, read standing on the lane looking along it.
                TextMeshPro lane = MakeText(root, "Slicer Lane Caption", font, LaneLine, new Vector2(0.8f, LaneLine * 2.5f), false, false);
                lane.SetText("walk along this line:\nit moves the cut through w");
                lane.color = s_dim;
                lane.rectTransform.localPosition = new Vector3(layout.LaneX + 0.5f, 0.006f, -0.5f * layout.LaneLength + 0.15f);
                lane.rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                m_LaneObject = lane.gameObject;
            }
        }

        static TextMeshPro MakeText(Transform parent, string name, TMP_FontAsset font, float lineHeight, Vector2 size, bool wrap, bool rich)
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
            t.richText = rich;
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

        static Transform MakeRenderer(Transform parent, string name, Mesh mesh, Material material, Vector3 at)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        // The strip's quads, facing the walker (-z): a dark track, then one quad per run of samples with the same shape.
        void BuildStrip(SlicerShapeStrip strip)
        {
            float width = CardWidth - 2f * CardPad;
            float x0 = -0.5f * width;
            m_StripBuilder.Clear();
            m_StripBuilder.AddQuad(new float3(x0, -0.5f * StripHeight, 0f), new float3(width, 0f, 0f), new float3(0f, StripHeight, 0f),
                                   AtlasPalette.Uv(AtlasPalette.Kerb));
            if (strip == null) return;
            // Sample i sits at i / (Samples - 1) along the strip and owns half a step either side of it.
            float step = 1f / (SlicerShapeStrip.Samples - 1);
            float h = StripHeight - 0.01f;
            int run = 0;
            for (int i = 1; i <= SlicerShapeStrip.Samples; i++)
            {
                if (i < SlicerShapeStrip.Samples && strip[i] == strip[run]) continue;
                if (strip[run] != SectionShape.None)
                {
                    float a0 = math.max(0f, (run - 0.5f) * step), a1 = math.min(1f, (i - 0.5f) * step);
                    m_StripBuilder.AddQuad(new float3(x0 + a0 * width, -0.5f * h, -0.001f), new float3((a1 - a0) * width, 0f, 0f),
                                           new float3(0f, h, 0f), AtlasPalette.Uv(SlicerShapeStrip.Colour(strip[run])));
                }
                run = i;
            }
        }

        /// <summary>
        /// Once a frame (step 6): the card's text and strip when they changed, the marker at the cut, and the turn of the card
        /// towards the head.
        /// </summary>
        public void Update(in SlicerReadoutState state, float3 head)
        {
            if (m_Header == null) return;
            long key = SlicerReadout.Key(in state);
            int stripVersion = state.Strip != null ? state.Strip.Version : -1;
            if (key != m_ShownKey || stripVersion != m_ShownStrip)
            {
                m_ShownKey = key;
                SlicerReadout.BuildHeader(m_Text, in state);
                m_Header.SetText(m_Text);
                SlicerReadout.BuildFooter(m_Text, in state);
                m_Footer.SetText(m_Text);
            }
            if (stripVersion != m_ShownStrip)
            {
                m_ShownStrip = stripVersion;
                if (m_StripMesh != null)
                {
                    BuildStrip(state.Strip);
                    AtlasMesh.Apply(m_StripBuilder, m_StripMesh);
                }
            }
            if (m_Marker != null)
            {
                float width = CardWidth - 2f * CardPad;
                m_Marker.localPosition = new Vector3((SlicerShapeStrip.Along(state.W) - 0.5f) * width, 0f, -0.004f);
            }

            Face(m_CardObject.transform, head);
        }

        public void SetVisible(bool visible)
        {
            if (m_CardObject != null && m_CardObject.activeSelf != visible) m_CardObject.SetActive(visible);
            if (m_LaneObject != null && m_LaneObject.activeSelf != visible) m_LaneObject.SetActive(visible);
        }

        // Turns the card so that its front faces the head (a TextMeshPro text reads from its -z side), tipped as well as
        // turned. The head is in the room frame, which is the world while a space runs (README F4).
        static void Face(Transform t, float3 head)
        {
            Vector3 away = t.position - (Vector3)head;
            if (away.sqrMagnitude < 1e-6f) return;
            t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>The objects are children of the space's root and go with it; the meshes are freed here.</summary>
        public void Dispose()
        {
            Free(m_PlateMesh);
            Free(m_StripMesh);
            Free(m_MarkerMesh);
        }

        static void Free(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
    }
}
