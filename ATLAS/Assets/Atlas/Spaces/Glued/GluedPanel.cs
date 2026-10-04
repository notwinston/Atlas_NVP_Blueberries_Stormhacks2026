using System.Globalization;
using System.Text;
using Atlas.Rendering;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// What S1 shows in words, and its gluing sign (Master Plan section 10.1.3 and task P2-C14). An info card carried at
    /// the waist (<see cref="GluedCardAnchor"/>): the gluing as the headline with its rule, the lattice plan
    /// (<see cref="GluedPlan"/>, the flat twin) with the walker's dot, the live numbers of the S1 card beside it, and the
    /// step line. Under the card, the gluing sign: three buttons, T², Klein and T³, the gaze twin of the bumpers, and a
    /// "seen it" button that stands in for A while the wave step waits for it. A yellow ring lights round a button while
    /// the gaze is on it or its gluing was just chosen, and a bar fills along its foot as the dwell runs. Text is
    /// TextMeshPro in the world (left out when the build has no font); the plate, the buttons and the plan are the look's
    /// own meshes, drawn with the space's plain material. The card tips to face the walker's eyes. A block of text is
    /// rebuilt only when a shown digit changes, and the plan (with the walker's dot in it) only when the dot moves.
    /// Draw calls are kept to <see cref="GluedBudget.CardDrawCallsPerEye"/>: the dot is in the plan's mesh and the four
    /// button words are one text.
    /// </summary>
    sealed class GluedPanel
    {
        /// <summary>The buttons: one per gluing, in glued.mode order, then "seen it".</summary>
        public const int ButtonCount = 4, SeenIt = 3;
        /// <summary>Base line height of the card's text, metres.</summary>
        public const float CardLine = 0.024f;
        public const float CardWidth = 0.48f, CardHeight = 0.32f, CardPad = 0.02f;
        /// <summary>Height of the header block, side of the lattice plan, metres.</summary>
        public const float HeaderHeight = 0.065f, PlanSize = 0.15f;
        /// <summary>Half the size of a button, the gap between two, and the gap between the card and the row, metres.</summary>
        public static readonly float2 ButtonHalf = new float2(0.05f, 0.022f);
        public const float ButtonGap = 0.02f, RowGap = 0.014f;
        /// <summary>How long a button stays lit after its gluing is chosen, seconds.</summary>
        public const float FlashSeconds = 0.35f;
        /// <summary>The radius of a button's gaze target, metres.</summary>
        public const float GazeRadius = 0.04f;

        const float PointsPerLineMetre = 10f / 1.15f;
        const float TextDepth = -0.006f, PlanDepth = -0.004f;
        const float Rim = 0.004f, Ring = 0.006f, BarHeight = 0.005f, BarInset = 0.008f;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);
        static readonly string[] s_labels = { "T²", "Klein", "T³", "seen it" };
        /// <summary>The button words are one monospaced text, so each can be centred on its button by position alone.</summary>
        const float WordSpaceEm = 0.55f, WordLine = 0.95f * CardLine;
        /// <summary>The dot is redrawn once it has moved this far on the plan, metres, or turned this far, degrees.</summary>
        const float DotStep = 0.0005f, DotTurnDeg = 3f;

        readonly GameObject m_Root;
        readonly TextMeshPro m_Header, m_Numbers, m_Step;
        readonly TextMeshPro m_Words;
        readonly string[] m_WordAt = new string[ButtonCount];
        readonly Mesh m_PlateMesh, m_SeenMesh, m_RingMesh, m_BarMesh, m_PlanMesh;
        readonly GameObject m_Seen;
        readonly GameObject[] m_Rings = new GameObject[ButtonCount];
        readonly Transform[] m_Bars = new Transform[ButtonCount];
        readonly bool[] m_RingOn = new bool[ButtonCount];
        readonly float[] m_Flash = new float[ButtonCount];
        readonly AtlasMeshBuilder m_PlanBuilder;
        readonly StringBuilder m_Text = new StringBuilder(512);
        GluedCardAnchor m_Anchor;
        long m_HeaderKey = long.MinValue, m_NumbersKey = long.MinValue, m_StepKey = long.MinValue;
        int m_StepVersion;
        string m_ShownStep;
        GluedLayout m_PlanLayout = (GluedLayout)255;
        GluedCell m_PlanCell;
        int m_PlanCopies = -1;
        int2 m_PlanDot;
        int m_PlanTurn;
        int m_LitLayout = -1;
        bool m_SeenShown = true;
        int m_WordsKey = -1;

        public bool HasText { get { return m_Header != null; } }
        /// <summary>The card's text, header, numbers then step, rich-text tags included (allocates: for tests and logs).</summary>
        public string CardText
        {
            get { return m_Header != null ? m_Header.text + "\n" + m_Numbers.text + "\n" + m_Step.text : null; }
        }
        /// <summary>The words on the buttons, rich-text tags included, or null in a build with no font.</summary>
        public string ButtonWords { get { return m_Words != null ? m_Words.text : null; } }
        public bool RingShown(int button) { return m_RingOn[button]; }
        public bool SeenItShown { get { return m_SeenShown; } }

        /// <summary>Where button i sits on the card, in the card's own frame.</summary>
        public static float3 ButtonLocal(int i)
        {
            float pitch = 2f * ButtonHalf.x + ButtonGap;
            float x = (i - 0.5f * (ButtonCount - 1)) * pitch;
            float y = -0.5f * CardHeight - RowGap - ButtonHalf.y;
            return new float3(x, y, 0f);
        }

        /// <summary>Where button i is in the room frame, as the card was last placed.</summary>
        public float3 ButtonCentre(int i)
        {
            return m_Root != null ? (float3)m_Root.transform.TransformPoint(ButtonLocal(i)) : float3.zero;
        }

        /// <param name="parent">The space's content (the room frame).</param>
        /// <param name="material">The space's plain material, for the plate, the buttons and the plan; null builds nothing.</param>
        public GluedPanel(Transform parent, Material material)
        {
            if (parent == null || material == null) return;
            m_Root = new GameObject("Glued Card") { hideFlags = HideFlags.DontSave };
            m_Root.layer = parent.gameObject.layer;
            m_Root.transform.SetParent(parent, false);
            Transform card = m_Root.transform;

            // The plate with its rim, and the three gluing buttons: one mesh, since none of it ever moves on the card.
            var b = new AtlasMeshBuilder(256, 1024);
            b.AddSquircle(new float3(0f, 0f, 0.002f), 0.5f * CardWidth + Rim, 0.5f * CardHeight + Rim, 0.024f,
                          AtlasPalette.Uv(AtlasPalette.Kerb));
            b.AddSquircle(float3.zero, 0.5f * CardWidth, 0.5f * CardHeight, 0.02f, AtlasPalette.Uv(AtlasPalette.Void));
            for (int i = 0; i < SeenIt; i++) AddButton(b, ButtonLocal(i));
            m_PlateMesh = AtlasMesh.Create(b, "Glued Card Plate");
            MakeRenderer(card, "Plate", m_PlateMesh, material, Vector3.zero);

            // "seen it" is its own object: it is shown only while the wave step waits for it.
            b.Clear();
            AddButton(b, float3.zero);
            m_SeenMesh = AtlasMesh.Create(b, "Glued Card Seen It");
            m_Seen = MakeRenderer(card, "Seen It", m_SeenMesh, material, ButtonLocal(SeenIt)).gameObject;

            b.Clear();
            b.AddSquircle(new float3(0f, 0f, 0.003f), ButtonHalf.x + Ring, ButtonHalf.y + Ring, ButtonHalf.y + Ring,
                          AtlasPalette.Uv(AtlasPalette.Beacon));
            m_RingMesh = AtlasMesh.Create(b, "Glued Button Ring");
            b.Clear();
            // The dwell bar at full length, from x = 0 to 1 and one unit high: each button scales it.
            b.AddQuad(new float3(0f, -0.5f, 0f), new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), AtlasPalette.Uv(AtlasPalette.Beacon));
            m_BarMesh = AtlasMesh.Create(b, "Glued Button Bar");
            for (int i = 0; i < ButtonCount; i++)
            {
                Vector3 at = ButtonLocal(i);
                m_Rings[i] = MakeRenderer(card, "Ring " + i, m_RingMesh, material, at).gameObject;
                m_Rings[i].SetActive(false);
                m_Bars[i] = MakeRenderer(card, "Dwell " + i, m_BarMesh, material,
                                         at + new Vector3(-ButtonHalf.x + BarInset, -ButtonHalf.y + BarInset, -0.003f));
                m_Bars[i].localScale = new Vector3(0f, BarHeight, 1f);
                m_Bars[i].gameObject.SetActive(false);
            }

            // The plan, rebuilt when the walker's cell or the gluing changes, and the walker's dot on it.
            float top = 0.5f * CardHeight - CardPad;
            float middleTop = top - HeaderHeight - 0.01f;
            float middleHeight = PlanSize + 0.005f;
            var planCentre = new Vector3(-0.5f * CardWidth + CardPad + 0.5f * PlanSize, middleTop - 0.5f * middleHeight, PlanDepth);
            m_PlanBuilder = new AtlasMeshBuilder(4 * (GluedRules.MaxCells * 3 + 1), 6 * (GluedRules.MaxCells * 3 + 1));
            m_PlanMesh = AtlasMesh.Create(m_PlanBuilder, "Glued Card Plan");
            MakeRenderer(card, "Plan", m_PlanMesh, material, planCentre);

            // The default font asset of the project's TextMeshPro resources. A build without them has no text and runs on.
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null) return;
            float inner = CardWidth - 2f * CardPad;
            m_Header = MakeText(card, "Header", font, CardLine, new Vector2(inner, HeaderHeight), true);
            m_Header.rectTransform.localPosition = new Vector3(0f, top - 0.5f * HeaderHeight, TextDepth);

            float numbersLeft = -0.5f * CardWidth + CardPad + PlanSize + 0.02f;
            float numbersWidth = 0.5f * CardWidth - CardPad - numbersLeft;
            m_Numbers = MakeText(card, "Numbers", font, 0.9f * CardLine, new Vector2(numbersWidth, middleHeight), true);
            m_Numbers.rectTransform.localPosition = new Vector3(numbersLeft + 0.5f * numbersWidth, middleTop - 0.5f * middleHeight, TextDepth);

            float stepTop = middleTop - middleHeight - 0.008f;
            float stepHeight = stepTop - (-0.5f * CardHeight + CardPad);
            m_Step = MakeText(card, "Step", font, 0.85f * CardLine, new Vector2(inner, stepHeight), true);
            m_Step.textWrappingMode = TextWrappingModes.Normal;
            m_Step.rectTransform.localPosition = new Vector3(0f, stepTop - 0.5f * stepHeight, TextDepth);

            // The four words: one text across the row, each word placed by <pos> at its button's centre less half its
            // monospaced width. One em of a world text is a tenth of its point size, in metres.
            float rowWidth = ButtonCount * (2f * ButtonHalf.x + ButtonGap);
            float em = 0.1f * WordLine * PointsPerLineMetre;
            m_Words = MakeText(card, "Words", font, WordLine, new Vector2(rowWidth, 2f * ButtonHalf.y), true);
            m_Words.alignment = TextAlignmentOptions.Left;
            m_Words.rectTransform.localPosition = new Vector3(0f, ButtonLocal(0).y, TextDepth);
            for (int i = 0; i < ButtonCount; i++)
            {
                float left = (ButtonLocal(i).x + 0.5f * rowWidth) / em - 0.5f * WordSpaceEm * s_labels[i].Length;
                m_WordAt[i] = "<pos=" + left.ToString("0.000", CultureInfo.InvariantCulture) + "em>";
            }
        }

        // A button: a capsule with a lighter rim round a dark face, about `centre` on the card.
        static void AddButton(AtlasMeshBuilder b, float3 centre)
        {
            b.AddSquircle(centre, ButtonHalf.x, ButtonHalf.y, ButtonHalf.y, AtlasPalette.Uv(AtlasPalette.DoorFrame));
            b.AddSquircle(centre + new float3(0f, 0f, -0.001f), ButtonHalf.x - Rim, ButtonHalf.y - Rim, ButtonHalf.y - Rim,
                          AtlasPalette.Uv(AtlasPalette.Void));
        }

        static TextMeshPro MakeText(Transform parent, string name, TMP_FontAsset font, float lineHeight, Vector2 size, bool rich)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSize = lineHeight * PointsPerLineMetre;
            t.enableAutoSizing = false;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
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

        /// <summary>The next frame puts the card straight in front of the walker (on entering the space).</summary>
        public void ResetPlace() { m_Anchor.Reset(); }

        /// <summary>
        /// Once a frame: carries the card with the walker and tips it to face the eyes. Call it before the gaze targets are
        /// moved, so they follow the card in the same frame.
        /// </summary>
        public void Place(float3 head, float heading, float deltaTime)
        {
            if (m_Root == null) return;
            float3 centre = m_Anchor.Step(head, heading, deltaTime);
            Transform t = m_Root.transform;
            t.position = centre;
            // A TextMeshPro text reads from its -z side: the card's +z points away from the eyes. The head is in the room
            // frame, which is the world while a space runs.
            Vector3 away = (Vector3)(centre - head);
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>
        /// Once a frame, after <see cref="Place"/>: the text blocks and the plan when they changed, the dot, and the buttons.
        /// `focused` is the button under the gaze (-1 for none) and `progress` its dwell, 0..1.
        /// </summary>
        public void Update(in GluedReadoutState state, in GluedLattice lattice, float heading, int focused, float progress, float deltaTime)
        {
            if (m_Root == null) return;
            if (m_Header != null)
            {
                long header = GluedReadout.HeaderKey(in state);
                if (header != m_HeaderKey)
                {
                    m_HeaderKey = header;
                    GluedReadout.BuildHeader(m_Text, in state);
                    m_Header.SetText(m_Text);
                }
                long numbers = GluedReadout.NumbersKey(in state);
                if (numbers != m_NumbersKey)
                {
                    m_NumbersKey = numbers;
                    GluedReadout.BuildNumbers(m_Text, in state);
                    m_Numbers.SetText(m_Text);
                }
                // The step lines are constants: a new reference is a new line.
                if (!ReferenceEquals(state.Step, m_ShownStep))
                {
                    m_ShownStep = state.Step;
                    m_StepVersion++;
                }
                long step = GluedReadout.StepKey(in state, m_StepVersion);
                if (step != m_StepKey)
                {
                    m_StepKey = step;
                    GluedReadout.BuildStep(m_Text, in state);
                    m_Step.SetText(m_Text);
                }
            }
            // The gluing in force: its word in yellow, and a flash of its ring when it has just been chosen.
            int lit = (int)state.Layout;
            if (lit != m_LitLayout)
            {
                if (m_LitLayout >= 0) Flash(lit);
                m_LitLayout = lit;
            }
            bool seen = state.ShowTour && state.Task == GluedTask.Wave;
            int words = lit * 2 + (seen ? 1 : 0);
            if (m_Words != null && words != m_WordsKey)
            {
                m_WordsKey = words;
                BuildWords(lit, seen);
                m_Words.SetText(m_Text);
            }

            // The plan and the walker's dot in it, redrawn when the dot has moved or turned a visible step.
            float2 dot = GluedPlan.Dot(in lattice, state.Copies, PlanSize, in state.Card);
            var dotStep = (int2)math.round(dot / DotStep);
            int turn = (int)math.round(math.degrees(heading) / DotTurnDeg);
            if (state.Layout != m_PlanLayout || !state.Cell.Equals(m_PlanCell) || state.Copies != m_PlanCopies
                || !dotStep.Equals(m_PlanDot) || turn != m_PlanTurn)
            {
                m_PlanLayout = state.Layout;
                m_PlanCell = state.Cell;
                m_PlanCopies = state.Copies;
                m_PlanDot = dotStep;
                m_PlanTurn = turn;
                m_PlanBuilder.Clear();
                GluedPlan.Build(m_PlanBuilder, in lattice, state.Cell, state.Copies, PlanSize);
                GluedPlan.BuildDot(m_PlanBuilder, 0.016f, AtlasPalette.Beacon, dot, heading);
                AtlasMesh.Apply(m_PlanBuilder, m_PlanMesh);
            }

            if (seen != m_SeenShown)
            {
                m_SeenShown = seen;
                m_Seen.SetActive(seen);
            }
            for (int i = 0; i < ButtonCount; i++)
            {
                m_Flash[i] = math.max(0f, m_Flash[i] - deltaTime);
                bool shown = i != SeenIt || seen;
                bool ring = shown && (i == focused || m_Flash[i] > 0f);
                if (ring != m_RingOn[i])
                {
                    m_RingOn[i] = ring;
                    m_Rings[i].SetActive(ring);
                }
                float fill = shown && i == focused ? math.saturate(progress) : 0f;
                bool barOn = fill > 0f;
                if (m_Bars[i].gameObject.activeSelf != barOn) m_Bars[i].gameObject.SetActive(barOn);
                if (barOn) m_Bars[i].localScale = new Vector3(fill * 2f * (ButtonHalf.x - BarInset), BarHeight, 1f);
            }
        }

        // The words of the row, each at its button, the gluing in force in yellow; "seen it" only in the wave step.
        void BuildWords(int lit, bool seen)
        {
            m_Text.Length = 0;
            m_Text.Append("<mspace=").Append(WordSpaceEm.ToString("0.00", CultureInfo.InvariantCulture)).Append("em>");
            for (int i = 0; i < ButtonCount; i++)
            {
                if (i == SeenIt && !seen) continue;
                m_Text.Append(m_WordAt[i]);
                if (i == lit) m_Text.Append("<color=#FFDB4D>").Append(s_labels[i]).Append("</color>");
                else m_Text.Append(s_labels[i]);
            }
        }

        /// <summary>Lights a button for <see cref="FlashSeconds"/>: its action fired.</summary>
        public void Flash(int button)
        {
            if ((uint)button < ButtonCount) m_Flash[button] = FlashSeconds;
        }

        public void SetVisible(bool visible)
        {
            if (m_Root != null && m_Root.activeSelf != visible) m_Root.SetActive(visible);
        }

        /// <summary>The objects are children of the space's content and go with it; the meshes are freed here.</summary>
        public void Dispose()
        {
            Free(m_PlateMesh);
            Free(m_SeenMesh);
            Free(m_RingMesh);
            Free(m_BarMesh);
            Free(m_PlanMesh);
            if (m_Root != null)
            {
                if (Application.isPlaying) Object.Destroy(m_Root); else Object.DestroyImmediate(m_Root);
            }
        }

        static void Free(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
    }
}
