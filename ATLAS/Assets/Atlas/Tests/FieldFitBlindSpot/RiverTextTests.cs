using System;
using System.Linq;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using Atlas.Blades.FieldFit.ScotomaRiver;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotTests
{
    public class RiverTextTests
    {
        GameObject _eye, _go; RiverText _rt;
        static BlindSpotResult Res(bool found = true) => new BlindSpotResult(EyeSide.Right, 1, found, false,
            15 * Math.PI / 180, -1.5 * Math.PI / 180, 3 * Math.PI / 180, 8.0, default, 6, 18, 0, 96, 100);

        [SetUp] public void SetUp() { _eye = new GameObject("eye"); _go = new GameObject("river"); _rt = _go.AddComponent<RiverText>(); }
        [TearDown] public void TearDown() { _rt.Hide(); UnityEngine.Object.DestroyImmediate(_go); UnityEngine.Object.DestroyImmediate(_eye); }

        [Test]
        public void RiverText_Placement()
        {
            // checks: text plane perpendicular to the blind-spot direction at 1.0 m (D9)
            var r = Res();
            Assert.IsTrue(_rt.Show(r, _eye.transform));
            Assert.AreEqual(1.0f, _go.transform.localPosition.magnitude, 1e-6);
            var d = BlindSpotGrid.Direction(r.CentreAzRad, r.CentreElRad);
            Assert.Less(Vector3.Angle(_go.transform.localPosition, d), 1e-3f);
            Assert.AreEqual(Math.Tan(3 * Math.PI / 180), _rt.APlaneM, 1e-9);
        }

        [Test]
        public void RiverText_NoGlyphInsideA()
        {
            // checks: B07 acceptance 1: no glyph centre inside radius a
            _rt.Show(Res(), _eye.transform);
            int visible = RiverText.DefaultLines.Sum(l => l.Count(c => c != ' '));
            Assert.AreEqual(visible, _rt.Glyphs.Count);
            foreach (var g in _rt.Glyphs) Assert.GreaterOrEqual(g.Z.Magnitude, _rt.APlaneM * (1 - 1e-9));
            Assert.GreaterOrEqual(_rt.MinROverA, 1 - 1e-9);
        }

        [Test]
        public void RiverText_ScaleAbove()
        {
            // checks: B07 acceptance 2: scale directly above = 0.5 +- 0.02
            _rt.Show(Res(), _eye.transform);
            Assert.AreEqual(0.5, _rt.ScaleAbove, 0.02);

            var twinGo = new GameObject("twin");
            try
            {
                var twin = twinGo.AddComponent<TextMeshPro>();
                twin.text = string.Join("\n", RiverText.DefaultLines);
                twin.fontSize = 10; twin.alignment = TextAlignmentOptions.Center;
                twin.textWrappingMode = TextWrappingModes.NoWrap; twin.richText = false;
                twin.ForceMeshUpdate();
                int vi = twin.textInfo.characterInfo[_rt.AnchorCharIndex].vertexIndex;
                var tv = twin.textInfo.meshInfo[0].vertices;
                float twinW = Mathf.Abs(tv[vi + 3].x - tv[vi].x);
                var rv = _go.GetComponent<TextMeshPro>().textInfo.meshInfo[0].vertices;
                float riverW = Mathf.Abs(rv[vi + 3].x - rv[vi].x);
                double ratio = riverW / (_rt.K * twinW);
                Assert.AreEqual(0.5, ratio, 0.02);
            }
            finally { UnityEngine.Object.DestroyImmediate(twinGo); }
        }

        [Test]
        public void RiverText_NotFound()
        {
            // checks: no river without a found blind spot
            Assert.IsFalse(_rt.Show(Res(false), _eye.transform));
        }
    }
}
