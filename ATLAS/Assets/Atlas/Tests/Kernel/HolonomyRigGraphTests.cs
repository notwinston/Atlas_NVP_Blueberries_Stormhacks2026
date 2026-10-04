using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    // P1-B09 Rig_GraphCtor (brief D9): HolonomyRig(TileGraph) crosses through the graph's own edges, q = 5 and 6
    public class HolonomyRigGraphTests
    {
        const double Dt = 1.0 / 60.0;

        static IEnumerable<double2> Lap(int sides, double s, double2 start)
        {
            double2[] dirs = { new double2(1, 0), new double2(0, 1), new double2(-1, 0), new double2(0, -1) };
            double2 p = start;
            yield return p;
            for (int k = 0; k < sides; k++)
            {
                int n = (int)Math.Round(s / 0.7 / Dt);
                for (int i = 1; i <= n; i++) yield return p + dirs[k & 3] * (s * i / n);
                p += dirs[k & 3] * s;
            }
        }

        [Test] // brief §7: identical tile sequence and U to 1e-12 for q = 5; the TileStep form is the P1-B02 one
        public void Rig_GraphCtor_MatchesStepCtor_Q5()
        {
            var graph = new TileGraph(5);
            var graph2 = new TileGraph(5);
            double L = 1.884481;
            var oldRig = new HolonomyRig((TileId t, int e, out Iso2 g) => { g = graph2.StepMatrix(t, e); return graph2.Neighbour(t, e); });
            var newRig = new HolonomyRig(graph);
            var start = new double2(-1, -1);
            var pose = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(start / L, -1) };
            oldRig.Reset(pose, -1, L); newRig.Reset(pose, -1, L);
            long f = 0; int crossings = 0;
            foreach (var p in Lap(5, 2.0, start))
            {
                var h = new HeadSample(f, f * Dt, new double3(p.x, 1.6, p.y), true);
                var a = oldRig.Step(f, h); var b = newRig.Step(f, h);
                f++;
                Assert.AreEqual(a.Crossings, b.Crossings);
                Assert.AreEqual(oldRig.Pose.Tile, newRig.Pose.Tile);
                Assert.Less(math.cmax(math.abs(oldRig.Pose.Local.M.c0 - newRig.Pose.Local.M.c0)), 1e-12);
                Assert.Less(math.cmax(math.abs(oldRig.Pose.Local.M.c1 - newRig.Pose.Local.M.c1)), 1e-12);
                Assert.Less(math.cmax(math.abs(oldRig.Pose.Local.M.c2 - newRig.Pose.Local.M.c2)), 1e-12);
                crossings += b.Crossings;
            }
            Assert.Greater(crossings, 0);
            Assert.AreEqual(0, newRig.GuardExhausted);
        }

        [Test] // brief D9: q = 6 crosses at the {4,6} in-radius, so the walker is always inside after the step
        public void Rig_GraphCtor_Q6_AlwaysInsideAfterStep()
        {
            var graph = new TileGraph(6);
            double L = 1.518603;
            var rig = new HolonomyRig(graph);
            var start = new double2(-1, -1);
            rig.Reset(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(start / L, -1) }, -1, L);
            long f = 0; int crossings = 0;
            foreach (var p in Lap(6, 2.0, start))
            {
                var r = rig.Step(f, new HeadSample(f, f * Dt, new double3(p.x, 1.6, p.y), true));
                f++;
                crossings += r.Crossings;
                Assert.AreEqual(-1, graph.ExitEdge(rig.Pose.Local), "frame " + f);
            }
            Assert.Greater(crossings, 0);
            Assert.AreEqual(0, rig.GuardExhausted);
        }

        [Test]
        public void Ctor_RejectsNull() => Assert.Throws<ArgumentNullException>(() => new HolonomyRig((TileGraph)null));
    }
}
