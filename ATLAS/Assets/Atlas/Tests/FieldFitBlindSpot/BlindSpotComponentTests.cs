using System;
using System.Collections.Generic;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotTests
{
    public class BlindSpotComponentTests
    {
        sealed class FakeRig : IBlindSpotRig
        {
            public Transform Left, Right; public List<string> Log = new List<string>();
            public Transform EyeTransform(EyeSide e) => e == EyeSide.Left ? Left : Right;
            public void SetEyeBlank(EyeSide e, bool b) => Log.Add(e + ":" + b);
        }
        sealed class FakeLook : IBlindSpotLook
        {
            public Material M = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            public Material SurfaceMaterial => M;
            public Vector2 BrightUv => new Vector2(0.9f, 0.9f);
            public Vector2 DarkUv => new Vector2(0.1f, 0.1f);
        }
        sealed class FakeTap : IBlindSpotTap { public event Action Tapped; public void Raise() => Tapped?.Invoke(); }
        sealed class Sink : IBlindSpotEvents
        {
            public List<(string ev, object data)> Items = new List<(string, object)>();
            public void Emit(string ev, object data) => Items.Add((ev, data));
        }

        GameObject _go, _l, _r; FakeRig _rig; FakeLook _look; FakeTap _tap; Sink _sink; BlindSpot _bs; double _now;

        [SetUp]
        public void SetUp()
        {
            _l = new GameObject("L"); _r = new GameObject("R");
            _rig = new FakeRig { Left = _l.transform, Right = _r.transform };
            _look = new FakeLook(); _tap = new FakeTap(); _sink = new Sink(); _now = 100;
            _go = new GameObject("bs");
            _bs = _go.AddComponent<BlindSpot>();
            _bs.Clock = () => _now;
            _bs.Bind(_rig, _look, _tap, _sink);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bs != null) _bs.End();
            UnityEngine.Object.DestroyImmediate(_go); UnityEngine.Object.DestroyImmediate(_l); UnityEngine.Object.DestroyImmediate(_r);
            UnityEngine.Object.DestroyImmediate(_look.M);
        }

        [Test]
        public void Comp_OneEyeLit()
        {
            // checks: beat 1 one eye lit
            Assert.IsTrue(_bs.Begin(3));
            CollectionAssert.AreEqual(new[] { "Left:True" }, _rig.Log);
        }

        [Test]
        public void Comp_StimulusLayout()
        {
            // checks: stimulus layout (Brief §3.5, D2, D6)
            _bs.Begin(3);
            var root = _r.transform.Find("BlindSpot.Stimulus");
            Assert.IsNotNull(root);
            Assert.AreEqual(0, ((Vector3)root.Find("BlindSpot.Backdrop").localPosition - new Vector3(0, 0, 1.2f)).magnitude, 1e-6);
            Assert.AreEqual(0, ((Vector3)root.Find("BlindSpot.Cross").localPosition - new Vector3(0, 0, 1.0f)).magnitude, 1e-6);
            var dot = root.Find("BlindSpot.Dot");
            Assert.IsFalse(dot.gameObject.activeSelf);
            _bs.Step(_now + 1.0 + 0.05);
            Assert.IsTrue(dot.gameObject.activeSelf);
            var p = _bs.Procedure.CurrentPoint;
            Assert.AreEqual(0, (dot.localPosition - BlindSpotGrid.Direction(p.AzRad, p.ElRad)).magnitude, 1e-5);
            var m = dot.GetComponent<MeshFilter>().sharedMesh;
            float maxR = 0; foreach (var v in m.vertices) maxR = Mathf.Max(maxR, v.magnitude);
            Assert.AreEqual(0.0037525f, maxR, 1e-6);
            Assert.AreSame(_look.M, dot.GetComponent<MeshRenderer>().sharedMaterial);
            _bs.Step(_now + 1.0 + 0.2 + 1e-3);
            Assert.IsFalse(dot.gameObject.activeSelf);
        }

        [Test]
        public void Comp_TapSeam()
        {
            // checks: tap seam (twins, rule 5): confirm raises Seen with RT
            _bs.Begin(3);
            _bs.Step(_now + 1.0);
            double on = _bs.Procedure.CurrentOnsetS;
            double start = _now;
            _now = start + on + 0.45;
            _tap.Raise();
            var rec = _bs.Procedure.Probes[0];
            Assert.IsTrue(rec.Seen); Assert.AreEqual(0.45, rec.ResponseS, 1e-9);
        }

        [Test]
        public void Comp_Events()
        {
            // checks: §10.4.7 measures logged; event order and schema (Brief §4)
            _bs.Begin(3);
            double start = _now;
            var target = BlindSpotGrid.PresentationOrder(EyeSide.Right, 3)[0];
            // drive to completion; observer misses only the first point
            int guard = 0;
            while (_bs.Procedure.Phase == BlindSpotPhase.Running && guard++ < 100000)
            {
                _now += 0.05; _bs.Step(_now);
                var p = _bs.Procedure;
                if (p.InTrial && !(p.CurrentPoint.TemporalDeg == target.TemporalDeg && p.CurrentPoint.ElDeg == target.ElDeg))
                { _now = start + p.CurrentOnsetS + 0.45; _tap.Raise(); }
            }
            Assert.IsTrue(_bs.HasResult);
            Assert.AreEqual("blindspot_start", _sink.Items[0].ev);
            Assert.AreEqual("blindspot_done", _sink.Items[_sink.Items.Count - 1].ev);
            int probes = 0, founds = 0;
            for (int i = 1; i < _sink.Items.Count - 1; i++)
            {
                if (_sink.Items[i].ev == "blindspot_probe") probes++;
                else if (_sink.Items[i].ev == "blindspot_found") founds++;
                else Assert.Fail("unexpected " + _sink.Items[i].ev);
            }
            Assert.AreEqual(_bs.LastResult.Trials, probes); Assert.AreEqual(1, founds);
            string all = "";
            foreach (var (ev, data) in _sink.Items)
            {
                string j = JsonUtility.ToJson(data);
                Assert.IsFalse(j.Contains("NaN") || j.Contains("Infinity"), j);
                all += j;
            }
            foreach (var f in new[] { "eye", "seed", "index", "kind", "az_deg", "el_deg", "onset_s", "seen", "rt_ms", "t_find_s", "found", "aborted",
                "centre_az_deg", "centre_el_deg", "radius_deg", "missed", "catch_trials", "catch_fp", "trials", "duration_s" })
                StringAssert.Contains("\"" + f + "\"", all);
            var done = (BlindSpotDoneData)_sink.Items[_sink.Items.Count - 1].data;
            var fd = (BlindSpotFoundData)_sink.Items.Find(x => x.ev == "blindspot_found").data;
            Assert.AreEqual(_bs.LastResult.TimeToFindS, done.t_find_s, 1e-12);
            Assert.AreEqual(_bs.LastResult.TimeToFindS, fd.t_find_s, 1e-12);
            Assert.AreEqual(target.TemporalDeg, fd.az_deg, 1e-9); Assert.AreEqual(target.ElDeg, fd.el_deg, 1e-9);
            Assert.AreEqual("right", done.eye);
        }

        [Test]
        public void Comp_EndRestores()
        {
            // checks: eye restored and stimulus removed on End, Abort and OnDisable
            _bs.Begin(3);
            _bs.Abort();
            var done = (BlindSpotDoneData)_sink.Items[_sink.Items.Count - 1].data;
            Assert.IsTrue(done.aborted);
            Assert.IsNotNull(_r.transform.Find("BlindSpot.Stimulus"));   // stays until End
            _bs.End();
            Assert.AreEqual("Left:False", _rig.Log[_rig.Log.Count - 1]);
            Assert.IsNull(_r.transform.Find("BlindSpot.Stimulus"));
            _bs.End();   // idempotent
            Assert.AreEqual(2, _rig.Log.Count);

            _rig.Log.Clear();
            _bs.Begin(4);
            _go.SetActive(false);   // OnDisable
            CollectionAssert.AreEqual(new[] { "Left:True", "Left:False" }, _rig.Log);
            Assert.IsNull(_r.transform.Find("BlindSpot.Stimulus"));
        }
    }
}
