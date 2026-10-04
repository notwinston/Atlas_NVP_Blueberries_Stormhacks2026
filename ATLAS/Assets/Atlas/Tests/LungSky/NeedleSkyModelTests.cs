using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public class NeedleSkyModelTests
    {
        const float Dt = 0.01f;
        SkyMaps _maps; FakeSkyEvents _ev; NeedleSkyModel _m;

        [SetUp] public void SetUp() { _maps = SkyMapsFactory.Fixture(); _ev = new FakeSkyEvents(); _m = new NeedleSkyModel(_maps, _ev); }

        void Run(double3 gaze, double seconds, float2 stick = default, bool confirm = false)
        {
            int n = (int)Math.Round(seconds / Dt);
            for (int k = 0; k < n; k++) _m.Step(new SkyInput { Gaze = gaze, Stick = stick, Confirm = confirm && k == 0, DeltaTime = Dt });
        }

        static double3 G(double az, double el) => SkyDirection.FromAzEl(az, el);

        [Test] // P2-B10 acceptance; G10 checklist
        public void B10_ClearanceShown_EqualsSkyJson_20Directions()
        {
            Assert.AreEqual(20, _maps.Meta.checks.Length);
            foreach (var c in _maps.Meta.checks)
            {
                var m = new NeedleSkyModel(_maps, _ev);
                m.Step(new SkyInput { Gaze = G(c.az_deg, c.el_deg), DeltaTime = Dt });
                Assert.LessOrEqual(Math.Abs(m.Readout.ClearanceShownMm - c.clear_mm), 0.1f, "az " + c.az_deg + " el " + c.el_deg);
                SkyDirection.Pixel(c.az_deg, c.el_deg, out int i, out int j);
                for (int k = 0; k < 10; k++) Assert.LessOrEqual(Math.Abs(_maps.ClearanceMm(k, i, j) - c.clear_mm_phase[k]), 0.1f);
            }
        }

        [Test] // P2-B10 acceptance, logged path (MP §10.2.6)
        public void B10_SkyPickEvent_ClearMatchesSkyJson_20Directions()
        {
            var checks = _maps.Meta.checks;
            for (int s = 0; s < checks.Length; s += 3)
            {
                _m.Reset(); _ev.Names.Clear(); _ev.Data.Clear();
                int n = Math.Min(3, checks.Length - s);
                for (int q = 0; q < n; q++)
                {
                    var c = checks[s + q];
                    _m.Step(new SkyInput { Gaze = G(0, 0), ConsolePick = true, ConsoleAzDeg = c.az_deg, ConsoleElDeg = c.el_deg, DeltaTime = Dt });
                }
                int got = _ev.Count("sky_pick");
                for (int q = 0; q < got; q++)
                {
                    var c = checks[s + q]; var p = _ev.Pick(q);
                    Assert.AreEqual(c.el_deg, p.el_deg, 1e-3);
                    if (Math.Abs(c.el_deg) < 89.99) Assert.AreEqual(c.az_deg, p.az_deg, 1e-3);
                    Assert.LessOrEqual(Math.Abs(p.clear_mm - c.clear_mm), 0.1f);
                    SkyDirection.Pixel(c.az_deg, c.el_deg, out int i, out int j);
                    Assert.AreEqual(_maps.IsRobust(i, j), p.robust);
                }
                Assert.AreEqual(n, got, "a check was rejected (separation) at batch " + s);
            }
        }

        [Test] // MP §10.2.2 "dwell 700 ms"; P2-B10 "dwell pick"
        public void B10_Dwell_PicksAt700ms_Once()
        {
            var d = G(20, 10);
            Run(d, 0.69); Assert.AreEqual(0, _ev.Count("sky_pick"));
            Run(d, 0.02); Assert.AreEqual(1, _ev.Count("sky_pick"));
            SkyDirection.ToAzEl(d, out double az, out double el);
            Assert.AreEqual(az, _ev.Pick(0).az_deg, 1e-4); Assert.AreEqual(el, _ev.Pick(0).el_deg, 1e-4);
            Run(d, 3.0); Assert.AreEqual(1, _ev.Count("sky_pick"));
            Run(G(22, 10), 1.0); Assert.AreEqual(1, _ev.Count("sky_pick"));      // 2 deg away: rejected (< 3 deg)
            Run(G(30, 10), 0.75); Assert.AreEqual(2, _ev.Count("sky_pick"));
        }

        [Test] // D5
        public void B10_Dwell_ResetsOutsideCone()
        {
            var d = G(20, 10);
            _m.Step(new SkyInput { Gaze = d, DeltaTime = Dt });
            for (int k = 0; k < 70; k++)
                _m.Step(new SkyInput { Gaze = G(20 + (k % 2 == 0 ? 1.0 : -1.0) / Math.Cos(10 * Math.PI / 180), 10), DeltaTime = Dt });
            Assert.AreEqual(1, _ev.Count("sky_pick"), "wobble within the cone picks by 0.71 s");

            _m.Reset(); _ev.Names.Clear(); _ev.Data.Clear();
            Run(d, 0.50);
            var away = G(20, 12);
            Run(away, 1.19 - 0.50); Assert.AreEqual(0, _ev.Count("sky_pick"));
            Run(away, 0.02); Assert.AreEqual(1, _ev.Count("sky_pick"));
        }

        [Test] // MP §10.2.2 "stick + A"; §0.5 rule 5
        public void B10_StickAndA_Twin()
        {
            Run(G(0, 0), 0.5, new float2(1, 0));
            SkyDirection.ToAzEl(_m.Cursor, out double az, out double el);
            Assert.AreEqual(45.0, az, 0.1); Assert.IsFalse(_m.CursorAttached); Assert.AreEqual(0, _ev.Count("sky_pick"));
            _m.Step(new SkyInput { Gaze = G(0, 0), Confirm = true, DeltaTime = Dt });
            Assert.AreEqual(1, _ev.Count("sky_pick"));
            Assert.AreEqual(45.0, _ev.Pick(0).az_deg, 0.1); Assert.AreEqual(0.0, _ev.Pick(0).el_deg, 0.1);
            Assert.IsTrue(_m.CursorAttached);

            _m.Reset();
            Run(G(0, 0), 1.0, new float2(0.15f, 0));
            SkyDirection.ToAzEl(_m.Cursor, out az, out el);
            Assert.AreEqual(0.0, az, 1e-9);                       // deadzone
            Run(G(0, 0), 2.0, new float2(0, 1));
            SkyDirection.ToAzEl(_m.Cursor, out az, out el);
            Assert.AreEqual(89.0, el, 1e-6);
        }

        [Test] // D7
        public void B10_MaxThreePicks_RejectsNearAndAntipode()
        {
            Step(0, 0); Step(1, 0); Step(180, 0);
            Assert.AreEqual(1, _m.PickCount);
            Step(90, 0); Step(0, 60); Step(180, 30);
            Assert.AreEqual(3, _m.PickCount); Assert.AreEqual(3, _ev.Count("sky_pick"));
        }

        void Step(double az, double el) => _m.Step(new SkyInput { Gaze = G(0, 0), ConsolePick = true, ConsoleAzDeg = az, ConsoleElDeg = el, DeltaTime = Dt });

        [Test] // D9
        public void B10_Completed_5sAfterThird()
        {
            Step(90, 0); Step(0, 60); Step(0, 0);
            Assert.IsFalse(_m.Completed);
            Run(G(0, 0), 4.99); Assert.IsFalse(_m.Completed);
            Run(G(0, 0), 0.02); Assert.IsTrue(_m.Completed);
        }

        [Test] // MP §10.2.5 "0 ms compute per frame"; G10 "no per-frame compute"
        public void B10_NoPerFrameCompute_ZeroAlloc()
        {
            // a gaze that never dwells: moves 0.5 deg per step (> cone is not needed; the anchor resets each step)
            for (int k = 0; k < 10; k++) _m.Step(new SkyInput { Gaze = G(k * 0.5, 0), DeltaTime = Dt });
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int k = 0; k < 1000; k++)
                _m.Step(new SkyInput { Gaze = G(10 * 0.5 + k * 0.5 % 300, 0), DeltaTime = Dt });
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, _ev.Count("sky_pick"));
            Assert.AreEqual(0, a1 - a0);
            Assert.LessOrEqual(sw.Elapsed.TotalMilliseconds / 1000.0, 0.05, "mean Step ms");
            foreach (var t in typeof(NeedleSkyModel).Assembly.GetTypes().Where(t => t.Namespace == "Atlas.Blades.Lung.Sky"))
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                bool Bad(Type x) => x.Name == "ComputeShader" || x.Name == "ComputeBuffer";
                foreach (var fi in t.GetFields(f)) Assert.IsFalse(Bad(fi.FieldType), t.Name + "." + fi.Name);
                foreach (var pi in t.GetProperties(f)) Assert.IsFalse(Bad(pi.PropertyType), t.Name + "." + pi.Name);
                foreach (var mi in t.GetMethods(f)) foreach (var p in mi.GetParameters()) Assert.IsFalse(Bad(p.ParameterType), t.Name + "." + mi.Name);
            }
        }
    }
}
