using System;
using System.Collections.Generic;
using Complex = System.Numerics.Complex;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using TMPro;
using UnityEngine;

namespace Atlas.Blades.FieldFit.ScotomaRiver
{
    /// <summary>
    /// Master Plan §10.4.3 beat 5 (P3-B07, Extended): text read with one eye parts around the walker's own blind spot.
    /// World-space TextMeshPro on a plane perpendicular to the blind-spot direction (D9).
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class RiverText : MonoBehaviour
    {
        public const double TextDistanceM = 1.0, LineHeightDeg = 1.0;

        // Dickens, A Tale of Two Cities (1859), public domain.
        public static readonly string[] DefaultLines =
        {
            "It was the best of times, it was the",
            "worst of times, it was the age of",
            "wisdom, it was the age of foolishness,",
            "it was the epoch of belief, it was the",
            "epoch of incredulity, it was the",
            "season of Light, it was the season of",
            "Darkness, it was the spring of hope,",
            "it was the winter of despair, we had",
            "everything before us, we had nothing",
            "before us, we were all going direct to",
            "Heaven, we were all going direct the",
            "other way - in short, the period was so",
            "far like the present period, that some",
        };

        public IReadOnlyList<RiverGlyph> Glyphs => _glyphs;
        public int AnchorCharIndex { get; private set; } = -1;
        public double APlaneM { get; private set; }
        public double K { get; private set; }
        public double MinROverA { get; private set; }
        public double ScaleAbove { get; private set; }

        readonly List<RiverGlyph> _glyphs = new List<RiverGlyph>(512);
        TextMeshPro _tmp;
        double _radiusRad;
        bool _active, _inShow, _subscribed;

        TextMeshPro Tmp => _tmp != null ? _tmp : (_tmp = GetComponent<TextMeshPro>());

        public bool Show(in BlindSpotResult r, Transform litEye, IBlindSpotEvents events = null)
        {
            if (!r.Found) return false;
            var d = BlindSpotGrid.Direction(r.CentreAzRad, r.CentreElRad);
            transform.SetParent(litEye, false);
            transform.localPosition = (float)TextDistanceM * d;   // plane perpendicular to the blind-spot direction: the spot is a disc of radius D tan r (D9)
            transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
            transform.localScale = Vector3.one;
            _radiusRad = r.RadiusRad;

            var t = Tmp;
            Subscribe();
            _inShow = true;
            try
            {
                t.text = string.Join("\n", DefaultLines);
                t.fontSize = 10;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.richText = false;
                t.ForceMeshUpdate();
                _active = true;
                Apply();
            }
            finally { _inShow = false; }

            events?.Emit("river_start", new RiverStartData
            {
                eye = r.Eye == EyeSide.Left ? "left" : "right", a_deg = r.RadiusRad * 180.0 / Math.PI,
                centre_az_deg = r.CentreAzRad * 180.0 / Math.PI, centre_el_deg = r.CentreElRad * 180.0 / Math.PI,
                glyphs = _glyphs.Count, min_r_over_a = MinROverA, scale_above = ScaleAbove,
            });
            return true;
        }

        public void Hide()
        {
            _active = false;
            Unsubscribe();
            _glyphs.Clear();
            if (_tmp != null) { _tmp.text = ""; }
            transform.SetParent(null, false);
        }

        void Subscribe() { if (!_subscribed) { TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged); _subscribed = true; } }
        void Unsubscribe() { if (_subscribed) { TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged); _subscribed = false; } }
        void OnDisable() { Unsubscribe(); }
        void OnDestroy() { Unsubscribe(); }

        // Re-apply when TMP regenerates its mesh; never call ForceMeshUpdate here.
        void OnTextChanged(UnityEngine.Object obj)
        {
            if (_active && !_inShow && obj == _tmp) Apply();
        }

        void Apply()
        {
            var ti = Tmp.textInfo;
            int meshCount = ti.meshInfo.Length;
            var orig = new Vector3[meshCount][];
            for (int m = 0; m < meshCount; m++)
            {
                var src = ti.meshInfo[m].vertices;
                if (src == null) continue;
                orig[m] = new Vector3[src.Length];
                Array.Copy(src, orig[m], src.Length);
            }

            K = TextDistanceM * Math.Tan(LineHeightDeg * Math.PI / 180.0) / ti.lineInfo[0].lineHeight;
            APlaneM = TextDistanceM * Math.Tan(_radiusRad);

            int n = ti.characterCount;
            // Anchor: visible character nearest the midpoint of line lineCount/2 (ties: lower index).
            int line = ti.lineCount / 2;
            double first = double.NaN, last = double.NaN;
            for (int i = 0; i < n; i++)
            {
                var ci = ti.characterInfo[i];
                if (!ci.isVisible || ci.lineNumber != line) continue;
                double cx = (ci.origin + ci.xAdvance) * 0.5;
                if (double.IsNaN(first)) first = cx;
                last = cx;
            }
            double mid = (first + last) * 0.5;
            int anchor = -1; double best = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var ci = ti.characterInfo[i];
                if (!ci.isVisible || ci.lineNumber != line) continue;
                double dist = Math.Abs((ci.origin + ci.xAdvance) * 0.5 - mid);
                if (dist < best) { best = dist; anchor = i; }   // strict <: the lower index wins a tie
            }
            AnchorCharIndex = anchor;
            _glyphs.Clear();
            MinROverA = double.MaxValue; ScaleAbove = 0;
            if (anchor < 0) { MinROverA = 0; return; }

            var ca = ti.characterInfo[anchor];
            double ax = (ca.origin + ca.xAdvance) * 0.5, ay = (ca.ascender + ca.descender) * 0.5;
            float k = (float)K;
            for (int i = 0; i < n; i++)
            {
                var ci = ti.characterInfo[i];
                if (!ci.isVisible) continue;
                double cx = (ci.origin + ci.xAdvance) * 0.5, cy = (ci.ascender + ci.descender) * 0.5;
                var g = RiverLayout.Place(i, new Complex(K * (cx - ax), K * (cy - ay)), APlaneM);
                _glyphs.Add(g);
                double rOverA = g.Z.Magnitude / APlaneM;
                if (rOverA < MinROverA) MinROverA = rOverA;
                if (i == anchor) ScaleAbove = g.Scale;

                int mi = ci.materialReferenceIndex, vi = ci.vertexIndex;
                var vs = ti.meshInfo[mi].vertices;
                var o = orig[mi];
                var centre = new Vector3((float)cx, (float)cy, 0f);
                var pos = new Vector3((float)g.Z.Real, (float)g.Z.Imaginary, 0f);
                float s = (float)g.Scale * k;
                for (int q = 0; q < 4; q++) vs[vi + q] = pos + s * (o[vi + q] - centre);
            }
            Tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            for (int m = 0; m < meshCount; m++)
                if (ti.meshInfo[m].mesh != null) ti.meshInfo[m].mesh.RecalculateBounds();
        }
    }
}
