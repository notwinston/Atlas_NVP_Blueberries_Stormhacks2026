using System.Text;
using Atlas.Input;
using Atlas.Kernel;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    // P1-B09: what S2 shows of its controls and of the walk (Master Plan §10.1.3). The read-out is one TextMeshPro object
    // under the head-locked HUD root (the path of the telemetry HUD: both eyes draw it at its real depth); the three chips
    // are the gaze twins of the dial and of the mode switch (§0.5 rule 5), registered with the real input while S2 runs
    // (CurvedWalkTwins holds them and decides which are live).
    // The read-out's text is rebuilt only when the walk changed, into one cached builder, and handed over without a string.
    sealed class CurvedWalkHud
    {
        /// <summary>Distance of the read-out from the eyes, metres, and how far above the view axis it sits, degrees.</summary>
        public const float Distance = 1.0f, AboveDegrees = 9f;
        public const float LineHeight = 0.02f;
        // One point of a TextMeshPro world text is a tenth of a unit, and a line of the default font is about 1.15 times
        // its point size (as the telemetry HUD).
        const float PointsPerLineMetre = 10f / 1.15f;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);

        readonly CurvedWalkTwins twins;
        readonly StringBuilder text = new StringBuilder(96);
        readonly GameObject readoutObject;
        readonly TextMeshPro readout;
        readonly GameObject[] chipObjects = new GameObject[3];
        readonly TextMeshPro[] chipTexts = new TextMeshPro[3];
        int shownCorners = -1, shownQ = -1, shownTurn = int.MinValue, shownFlag = int.MinValue;
        bool shown, shownStarted, shownClosed;

        /// <summary>hud: the head-locked root (IStereoRig.Hud), or null. world: the space's root. input may be null.</summary>
        public CurvedWalkHud(Transform hud, Transform world, IAtlasInput input)
        {
            twins = new CurvedWalkTwins(input);
            // The default font asset of the project's TextMeshPro resources. A build without them has no text and runs on.
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font != null && hud != null)
            {
                readout = MakeText(hud, "CurvedWalkReadout", font, LineHeight, new Vector2(0.30f, LineHeight * 4f), out readoutObject);
                float a = AboveDegrees * Mathf.Deg2Rad;
                readout.rectTransform.localPosition = new Vector3(0f, Mathf.Sin(a) * Distance, Mathf.Cos(a) * Distance);
                readout.rectTransform.localRotation = Quaternion.Euler(-AboveDegrees, 0f, 0f);   // tilted to face the eyes
                readoutObject.SetActive(false);
            }
            for (int i = 0; i < 3; i++)
            {
                if (font != null && world != null)
                {
                    chipTexts[i] = MakeText(world, "CurvedWalkChip" + i, font, LineHeight * 2f,
                        new Vector2(CurvedWalkChips.Radius * 4f, CurvedWalkChips.Radius * 2f), out chipObjects[i]);
                }
            }
        }

        public CurvedWalkTwins Twins { get { return twins; } }
        public StringBuilder Text { get { return text; } }
        public TextMeshPro Readout { get { return readout; } }

        static TextMeshPro MakeText(Transform parent, string name, TMP_FontAsset font, float lineHeight, Vector2 size, out GameObject go)
        {
            go = new GameObject(name) { hideFlags = HideFlags.DontSave };
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
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return t;
        }

        /// <summary>
        /// MP §6.3 step 6. The chips follow the head's place and, lazily, its heading; their labels say what a dwell does
        /// from the dial snap q, and they show only while the walker stands still, as the dial and the mode switch act only
        /// then (§10.1.4). The read-out follows the walk.
        /// </summary>
        public void Upload(float3 head, quaternion headRotation, int q, LapWalk walk, bool still = true)
        {
            twins.Place(head, headRotation);
            for (int i = 0; i < CurvedWalkTwins.Count; i++)
            {
                Vector3 centre = (Vector3)twins.Centre(i);
                if (chipObjects[i] != null) chipObjects[i].transform.SetPositionAndRotation(centre, Quaternion.LookRotation(centre - (Vector3)head));
            }
            if (twins.Update(q, still) || q != shownQ)
            {
                shownQ = q;
                SetChip(CurvedWalkTwins.DialLeft, "q ", q - 1);
                SetChip(CurvedWalkTwins.DialRight, "q ", q + 1);
                if (chipTexts[CurvedWalkTwins.Mode] != null)
                {
                    chipObjects[CurvedWalkTwins.Mode].SetActive(twins.IsLive(CurvedWalkTwins.Mode));
                    chipTexts[CurvedWalkTwins.Mode].SetText("cells");
                }
            }

            if (readout == null) return;
            // The text changes at a corner and as the walker moves: rebuilt when a shown digit would change.
            bool armed = walk != null && walk.Armed;
            int corners = armed ? walk.Corners : -1;
            int flag = armed && walk.Started ? (int)math.round(walk.DistanceToFlagM * 100.0) : int.MinValue;
            int turn = armed && walk.Started ? (int)math.round(walk.WorldTurnDeg) : int.MinValue;
            bool started = armed && walk.Started, closed = armed && walk.Closed;
            if (armed == shown && corners == shownCorners && flag == shownFlag && turn == shownTurn && started == shownStarted && closed == shownClosed) return;
            shownCorners = corners; shownFlag = flag; shownTurn = turn; shownStarted = started; shownClosed = closed;
            bool show = WalkReadout.Build(text, walk);
            if (show != shown) { shown = show; readoutObject.SetActive(show); }
            if (show) readout.SetText(text);
        }

        void SetChip(int i, string prefix, int value)
        {
            if (chipTexts[i] == null) return;
            bool live = twins.IsLive(i);
            chipObjects[i].SetActive(live);
            if (live) chipTexts[i].SetText(prefix + "{0}", value);
        }

        public void Dispose()
        {
            twins.Dispose();
            for (int i = 0; i < 3; i++)
            {
                Kill(chipObjects[i]); chipObjects[i] = null; chipTexts[i] = null;
            }
            Kill(readoutObject);
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
