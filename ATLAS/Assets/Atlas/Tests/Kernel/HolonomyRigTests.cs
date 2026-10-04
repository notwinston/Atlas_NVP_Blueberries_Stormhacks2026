using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Random = System.Random;

namespace Atlas.Kernel.Tests
{
    public class HolonomyRigTests
    {
        const int Seed = 20261003;
        const double Dt = 1.0 / 60.0;
        static readonly double Phi = HolonomyRig.Phi;
        static readonly double L = 2.0 / Math.Acosh(Phi); // loop-fit L for s = 2.0 m (A.4)

        // Test-side tile step: S_k = R^k T H (A.5); Acc lives in the test only (rule 7).
        sealed class Harness
        {
            public readonly HolonomyRig Rig;
            public Iso2 Acc = new Iso2(double3x3.identity, -1);
            public readonly List<(TileId, int)> Calls = new List<(TileId, int)>();
            int _n;
            public Harness() { Rig = new HolonomyRig(Step); }
            static Iso2 S(int k) => Iso2.Rot(k * Math.PI / 2) * Iso2.Boost(new double2(Math.Acosh(Phi), 0), -1) * Iso2.Rot(Math.PI);
            TileId Step(TileId t, int e, out Iso2 g)
            {
                g = S(e); Calls.Add((t, e)); Acc = Acc * g;
                return new TileId(++_n, 0, 0, 0, 0, 0);
            }
            public Iso2 Abs => Acc * Rig.Pose.Local;
            public void Start(double2 metres, double k, double l)
            {
                Rig.Reset(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(metres / (k == 0 ? 1 : l), k) }, k, l);
            }
        }

        static HeadSample Sample(long f, double3 p, bool ok = true) => new HeadSample(f, f * Dt, p, ok);
        static double3 P(double2 xz) => new double3(xz.x, 1.6, xz.y);
        static double Dist(Iso2 a, Iso2 b) => L * Geometry2.Distance(a.M.c2, b.M.c2, -1);
        static double Heading(Iso2 a) => Math.Atan2(a.M.c0.y, a.M.c0.x);

        static List<double2> LapPositions()
        {
            var dirs = new[] { new double2(1, 0), new double2(0, 1), new double2(-1, 0), new double2(0, -1), new double2(1, 0) };
            var list = new List<double2> { double2.zero };
            double2 p = double2.zero;
            foreach (var d in dirs)
            {
                for (int i = 0; i < 200; i++) { p += 0.01 * d; list.Add(p); }
                for (int i = 0; i < 30; i++) list.Add(p);
            }
            return list;
        }

        // Master Plan §9.5.6 P1-B02 "square lap closes after five sides to 1 mm"; A.4.
        [TestCase(-1.0, -1.0)] [TestCase(0.0, 0.0)]
        public void Rig_SquareLap_ClosesAfterFiveSides(double sx, double sz)
        {
            var h = new Harness();
            var start = new double2(sx, sz);
            h.Start(start, -1, L);
            Iso2 u0 = h.Rig.Pose.Local;
            var pos = LapPositions();
            Assert.AreEqual(1151, pos.Count);
            Iso2 atFour = u0;
            for (int f = 0; f < pos.Count; f++)
            {
                var r = h.Rig.Step(f, Sample(f, P(pos[f])));
                Assert.IsTrue(r.Stepped);
                if (f == 920) atFour = h.Abs;
            }
            double closure = Dist(h.Abs, u0);
            double lap = Dist(atFour, u0);
            Iso2 rel = u0.Inverse() * atFour;
            double rot = Heading(rel);
            double back = math.length(h.Acc.M.c2 - new double3(0, 0, 1));
            TestContext.WriteLine($"lap closure after five sides {closure:E3} m, lap displacement {lap:F9} m, rotation {rot * 180 / Math.PI:F6} deg, crossings {h.Calls.Count}, |Acc.o-o| {back:E3}");
            Assert.Less(closure, 1e-3);
            Assert.Less(Math.Abs(lap - 2.0), 1e-6);
            Assert.AreEqual(-Math.PI / 2, rot, 1e-9);
            Assert.Less(back, 1e-9);
            Assert.AreEqual(1151, h.Rig.StepCount);
            Assert.AreEqual(0, h.Rig.RejectedCalls);
            Assert.AreEqual(0, h.Rig.GuardExhausted);
        }

        // §9.5.6 P1-B02 "stand-still of 60 s moves the frame by < 5 mm"; R1 §F.4.
        [TestCase(0.0, 0.0)] [TestCase(-1.0, -1.0)]
        public void Rig_StandStill60s_MovesLessThan5mm(double sx, double sz)
        {
            var rnd = new Random(Seed);
            var h = new Harness();
            h.Start(new double2(sx, sz), -1, L);
            Iso2 u0 = h.Rig.Pose.Local;
            double max = 0, last = 0, head = 0;
            for (int f = 0; f <= 3600; f++)
            {
                double3 p = new double3(1 + (rnd.NextDouble() * 2 - 1) * 1e-3, 1.6 + (rnd.NextDouble() * 2 - 1) * 1e-3, 1 + (rnd.NextDouble() * 2 - 1) * 1e-3);
                h.Rig.Step(f, Sample(f, p));
                last = Dist(h.Abs, u0);
                max = Math.Max(max, last);
                head = Heading(u0.Inverse() * h.Abs);
            }
            TestContext.WriteLine($"stand-still 60 s: max {max * 1000:F3} mm, final {last * 1000:F3} mm, heading drift {head * 180 / Math.PI:E3} deg");
            Assert.Less(max, 5e-3);
            Assert.AreEqual(0, h.Calls.Count);
        }

        // §6.3 / §9.5.6 P1-B02 "exactly once in each of 600 consecutive frames (logged frame counter)".
        [Test]
        public void Rig_StepsExactlyOncePerFrame_600Frames()
        {
            var pos = LapPositions();
            Harness Run(int callsPerFrame, int staleFrame, List<long> log, List<double2> applied)
            {
                var h = new Harness();
                h.Start(new double2(-1, -1), -1, L);
                for (int f = 0; f < 600; f++)
                {
                    var s = Sample(staleFrame == f ? f - 1 : f, P(pos[staleFrame == f ? f - 1 : f]));
                    for (int c = 0; c < callsPerFrame; c++)
                    {
                        var r = h.Rig.Step(f, s);
                        if (r.Stepped) { log?.Add(r.Frame); applied?.Add(r.Applied); }
                    }
                }
                return h;
            }

            var logA = new List<long>();
            var a = Run(1, -1, logA, null);
            for (int i = 0; i < 600; i++) Assert.AreEqual(i, logA[i]);
            Assert.AreEqual(600, logA.Count);
            Assert.AreEqual(600, a.Rig.StepCount);
            Assert.AreEqual(0, a.Rig.RejectedCalls);
            Assert.AreEqual(0, a.Rig.MissedFrames);
            Assert.AreEqual(0, a.Rig.StalePoseFrames);
            TestContext.WriteLine($"frame counter: first {logA[0]}, last {logA[599]}, count {logA.Count}");

            var logB = new List<long>();
            var b = Run(2, -1, logB, null);
            Assert.AreEqual(600, logB.Count);
            Assert.AreEqual(600, b.Rig.RejectedCalls);
            Assert.IsTrue(a.Rig.Pose.Local.M.Equals(b.Rig.Pose.Local.M));
            Assert.AreEqual(a.Rig.Pose.Tile, b.Rig.Pose.Tile);

            var logC = new List<long>(); var app = new List<double2>();
            var c = Run(1, 300, logC, app);
            Assert.AreEqual(1, c.Rig.StalePoseFrames);
            Assert.AreEqual(600, c.Rig.StepCount);
            Assert.AreEqual(double2.zero, app[300]);
        }

        // §4.6 0.1 m/s, §0.5 rule 4, §10.1.4 4 s.
        [Test]
        public void Rig_CurvatureRamp_OnlyWhileStill()
        {
            var rnd = new Random(Seed);
            var h = new Harness();
            h.Start(double2.zero, 0, 0);
            h.Rig.SetCurvatureTarget(-1, L);
            double target = -1 / (L * L);
            var pos = new List<double2>(); var normal = new List<bool>(); var jump = new List<bool>();
            double2 p = double2.zero;
            void Add(int n, double2 step, bool ok = true, double jit = 0)
            {
                for (int i = 0; i < n; i++)
                {
                    p += step;
                    pos.Add(p + new double2((rnd.NextDouble() * 2 - 1) * jit, (rnd.NextDouble() * 2 - 1) * jit));
                    normal.Add(ok); jump.Add(false);
                }
            }
            Add(60, double2.zero);
            Add(120, new double2(0.01, 0));
            Add(60, double2.zero, false);
            Add(60, double2.zero);
            Add(60, new double2(0.002, 0));
            Add(60, new double2(0.05 * Dt, 0));
            Add(1, new double2(0.25, 0)); jump[jump.Count - 1] = true;
            Add(240, double2.zero, true, 2e-4);

            double tauTest = 0, prevK = h.Rig.Curvature; int reached = -1, expected = -1, fast = 0;
            for (int f = 0; f < pos.Count; f++)
            {
                double before = h.Rig.Curvature;
                var r = h.Rig.Step(f, Sample(f, new double3(pos[f].x, 1.6, pos[f].y), normal[f]));
                double after = h.Rig.Curvature;
                // True speed from the scripted positions, independent of the rig's own Speed (§9.5.6 "never while head speed > 0.1 m/s").
                double trueSpeed = f == 0 ? 0 : math.length(pos[f] - pos[f - 1]) * 60.0;
                if (trueSpeed > 0.1) { fast++; Assert.AreEqual(BitConverter.DoubleToInt64Bits(before), BitConverter.DoubleToInt64Bits(after), $"true speed {trueSpeed:F3} m/s, frame {f}"); }
                if (r.Speed > 0.1 || double.IsNaN(r.Speed) || !normal[f] || jump[f])
                    Assert.AreEqual(BitConverter.DoubleToInt64Bits(before), BitConverter.DoubleToInt64Bits(after), $"frame {f}");
                if (!r.Still) Assert.IsFalse(r.CurvatureChanged, $"frame {f}");
                Assert.LessOrEqual(after, prevK, $"frame {f}");
                prevK = after;
                if (r.Still && before != target) { tauTest += Dt; if (expected < 0 && tauTest >= 4 - 1e-9) expected = f; }
                if (reached < 0 && after == target) reached = f;
            }
            TestContext.WriteLine($"ramp reached target at frame {reached}, expected {expected}");
            Assert.GreaterOrEqual(fast, 120); // the 0.6 m/s walk and the 0.12 m/s segment were all checked
            Assert.AreEqual(481, reached); // 59 + 60 + 60 still frames before the jump, then 61 jitter frames
            Assert.GreaterOrEqual(reached, 0);
            Assert.LessOrEqual(Math.Abs(reached - expected), 1);
            Assert.AreEqual(target, h.Rig.Curvature);
            Assert.AreEqual(-1, h.Rig.Kappa);

            // Rescale: the log-chart position in metres and the heading survive the ramp (deviation Dv3).
            var g = new Harness();
            g.Rig.Reset(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(new double2(-1, -1), 0) }, 0, 0);
            g.Rig.SetCurvatureTarget(-1, L);
            double maxPos = 0, maxHead = 0;
            for (int f = 0; f < 330; f++)
            {
                g.Rig.Step(f, Sample(f, P(double2.zero)));
                double k = g.Rig.Curvature, kap = g.Rig.Kappa, sc = k == 0 ? 1 : 1 / Math.Sqrt(Math.Abs(k));
                var u = new Iso2(g.Rig.Pose.Local.M, kap);
                double2 t = Geometry2.Log(u.M.c2, kap) * sc;
                maxPos = Math.Max(maxPos, math.length(t - new double2(-1, -1)));
                maxHead = Math.Max(maxHead, Math.Abs(Heading(Iso2.Boost(Geometry2.Log(u.M.c2, kap), kap).Inverse() * u)));
            }
            TestContext.WriteLine($"rescale: max position error {maxPos:E3} m, max heading {maxHead:E3} rad");
            Assert.Less(maxPos, 1e-9);
            Assert.Less(maxHead, 1e-12);
            Assert.AreEqual(-1, g.Rig.Kappa);

            // Information: ramp time under +-1 mm jitter.
            var j = new Harness();
            j.Rig.Reset(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity }, 0, 0);
            j.Rig.SetCurvatureTarget(-1, L);
            int jf = 0;
            for (; jf < 1200 && j.Rig.Curvature != target; jf++)
                j.Rig.Step(jf, Sample(jf, new double3(1 + (rnd.NextDouble() * 2 - 1) * 1e-3, 1.6 + (rnd.NextDouble() * 2 - 1) * 1e-3, 1 + (rnd.NextDouble() * 2 - 1) * 1e-3)));
            TestContext.WriteLine($"ramp under +-1 mm jitter: {jf * Dt:F2} s");
            Assert.AreEqual(target, j.Rig.Curvature);
        }

        // §4.6; A.3 0.4859; kernel.md §5.3 edge numbering.
        [TestCase(1, 0, 0)] [TestCase(0, 1, 1)] [TestCase(-1, 0, 2)] [TestCase(0, -1, 3)]
        public void Rig_TileCrossing_EdgeIndex(double dx, double dz, int edge)
        {
            var h = new Harness();
            h.Start(double2.zero, -1, L);
            double r = Math.Asinh(HolonomyRig.SinhR), thr = (r + HolonomyRig.CrossingHysteresis) * L;
            int hit = -1;
            for (int f = 0; f < 200 && hit < 0; f++)
            {
                var res = h.Rig.Step(f, Sample(f, P(0.01 * f * new double2(dx, dz))));
                if (res.Crossings > 0) { hit = f; Assert.AreEqual(edge, res.LastEdge); }
            }
            TestContext.WriteLine($"edge {edge}: crossing at {hit * 0.01:F3} m, threshold {thr:F4} m, Klein half-width {HolonomyRig.KleinHalfWidth:F6}");
            Assert.AreEqual(0.4859, HolonomyRig.KleinHalfWidth, 5e-5);  // A.3
            Assert.AreEqual(0.5306, r, 5e-5);                            // A.3
            Assert.Greater(hit * 0.01, thr);
            Assert.LessOrEqual((hit - 1) * 0.01, thr);
            Assert.AreEqual((TileId.Zero, edge), h.Calls[0]);
        }

        // Frame-gap accounting (§4.6 stale-pose guard).
        [Test]
        public void Rig_MissedFrames_CountsGaps()
        {
            var h = new Harness();
            h.Start(double2.zero, -1, L);
            h.Rig.Step(0, Sample(0, P(double2.zero)));
            h.Rig.Step(1, Sample(1, P(double2.zero)));
            Assert.AreEqual(0, h.Rig.MissedFrames);
            h.Rig.Step(5, Sample(5, P(double2.zero)));
            Assert.AreEqual(3, h.Rig.MissedFrames);
        }

        [Test]
        public void Rig_TileCrossing_Hysteresis()
        {
            var h = new Harness();
            h.Start(double2.zero, -1, L);
            int f = 0; double x = 0;
            void Walk(double dx, int frames, Action<HolonomyRigResult> each = null)
            {
                int n = (int)Math.Round(Math.Abs(dx) / 0.01) ;
                for (int i = 0; i < n; i++) { x += Math.Sign(dx) * 0.01; var res = h.Rig.Step(f, Sample(f, P(new double2(x, 0)))); f++; each?.Invoke(res); }
            }
            h.Rig.Step(f++, Sample(0, P(double2.zero)));
            while (h.Calls.Count == 0) Walk(0.01, 1);
            Assert.AreEqual(1, h.Calls.Count);
            int c = 0;
            Walk(-0.03, 0, res => c += res.Crossings);
            Assert.AreEqual(0, c, "no flicker inside the 2*0.02 L band");
            Walk(-0.08, 0, res => { c += res.Crossings; if (res.Crossings > 0) Assert.AreEqual(0, res.LastEdge); });
            Assert.AreEqual(1, c);
            Assert.AreEqual(2, h.Calls.Count);
            Assert.Less(math.length(h.Acc.M.c2 - new double3(0, 0, 1)), 1e-9);
        }

        [Test]
        public void Rig_TileCrossing_DiagonalAndFlat()
        {
            var h = new Harness();
            h.Start(double2.zero, -1, L);
            double2 d = new double2(1, 1) / math.sqrt(2.0);
            for (int f = 0; f < 600; f++)
            {
                h.Rig.Step(f, Sample(f, P(0.01 * f * d)));
                for (int k = 0; k < 4; k++) Assert.LessOrEqual(HolonomyRig.Sigma(h.Rig.Pose.Local.M.c2, k), HolonomyRig.SinhHyst);
            }
            Assert.AreEqual(0, h.Rig.GuardExhausted);
            Assert.Greater(h.Calls.Count, 0);

            var flat = new Harness();
            flat.Start(double2.zero, 0, 0);
            for (int f = 0; f < 500; f++) flat.Rig.Step(f, Sample(f, P(new double2(0.01 * f, 0))));
            Assert.AreEqual(0, flat.Calls.Count);
            Assert.AreEqual(4.99, flat.Rig.Pose.Local.M.c2.x, 1e-9);
        }

        // §4.6 "if |d| > 0.2 m or tracking is not Normal: d = 0"; §6.3 step 3.
        [Test]
        public void Rig_PoseFilter_RejectsJumpsAndBadTracking()
        {
            var h = new Harness();
            h.Start(double2.zero, -1, L);
            h.Rig.Step(0, Sample(0, P(double2.zero)));
            h.Rig.Step(1, Sample(1, P(new double2(0.01, 0))));
            var m0 = h.Rig.Pose.Local.M;
            var r = h.Rig.Step(2, Sample(2, P(new double2(0.26, 0))));
            Assert.AreEqual(1, h.Rig.RejectedJumps);
            Assert.AreEqual(double2.zero, r.Applied);
            Assert.IsTrue(m0.Equals(h.Rig.Pose.Local.M), "U unchanged by a jump");
            r = h.Rig.Step(3, Sample(3, P(new double2(0.27, 0))));
            Assert.AreEqual(0.01, r.Applied.x, 1e-12);
            m0 = h.Rig.Pose.Local.M;
            r = h.Rig.Step(4, Sample(4, P(new double2(0.5, 0)), false));
            Assert.AreEqual(1, h.Rig.UntrackedFrames);
            Assert.IsTrue(m0.Equals(h.Rig.Pose.Local.M), "U unchanged without tracking");

            var e = new Harness();
            e.Start(double2.zero, -1, L);
            e.Rig.Step(0, Sample(0, P(double2.zero)));
            r = e.Rig.Step(1, Sample(1, P(new double2(0.2, 0))));
            Assert.AreEqual(0, e.Rig.RejectedJumps);
            Assert.AreEqual(0.2, r.Applied.x);
        }
    }
}
