using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Tests
{
    // P2-B08 Two Compasses tests (Master Plan §10.2.3 Aids, §10.2.6 events; Build Graph row P2-B08).
    // The route length and the compass fork always come from tree.json (PH-39: the fixture route has 6 forks, never hard-code 4).
    public class TwoCompassesTests
    {
        static AirwayTree Tree => TreeFixture.Tree;

        // The events body logs, through a real EventLog published as the session's IEventLog.
        static List<EventMessage> Capture(Action body)
        {
            using (var capture = new EventCapture())
            {
                body();
                return capture.Events;
            }
        }

        // C1: row P2-B08 acceptance: at the designed fork the Euclidean compass and the beacon point to different children.
        [Test]
        public void P2B08_Accept_CompassAndBeaconDisagreeAtDesignedFork()
        {
            var route = AirwayRoute.Full(Tree);
            string fork = Tree.CompassForkAddress, target = route.TargetAddress;
            Assert.IsTrue(route.HasTwoCompasses(Tree));
            Assert.IsTrue(AirwayAddress.IsProperPrefixOf(fork, target));
            char e = TwoCompasses.EuclideanChild(Tree, fork, target), b = TwoCompasses.BeaconChild(fork, target);
            Assert.AreNotEqual(e, b);
            Assert.AreEqual(target[fork.Length], b);

            var g = new TileGraph(5);
            var frame = Tree.FrameOf(AirwayTile.Fork(fork));
            var pose = new WalkerPose { Tile = frame.Tile, Local = new Iso2(double3x3.identity, -1) };
            double2[] dirs = new double2[2];
            int k = 0;
            foreach (char c in new[] { e, b })
            {
                int ce = frame.CanonicalEdge(c == 'L' ? RuleS.LeftEdge : RuleS.RightEdge);
                double2 d = AirwayBeacon.ArrowDirection(pose, ce);
                dirs[k++] = d;
                var moved = pose.Local * Iso2.Boost(0.6 * d, -1);
                int exit = g.ExitEdge(moved);
                Assert.AreEqual(g.Neighbour(frame.Tile, exit), Tree.FrameOf(AirwayTile.Bend(fork + c)).Tile, "probe lands in the child's bend tile");
            }
            double ang = Math.Acos(math.clamp(math.dot(dirs[0], dirs[1]), -1, 1)) * 180 / Math.PI;
            Assert.Greater(ang, 179.0);
        }

        // C2 (supporting; PH-35): over the route, exactly one fork has compass != prefix child, and it is compass_fork.
        [Test]
        public void P2B08_FixtureRoute_OneWrongCompassFork()
        {
            var route = AirwayRoute.Full(Tree);
            var wrong = new List<string>();
            for (int i = 0; i < route.Forks; i++)
            {
                string f = route.TargetAddress.Substring(0, i);
                if (TwoCompasses.EuclideanChild(Tree, f, route.TargetAddress) != TwoCompasses.BeaconChild(f, route.TargetAddress)) wrong.Add(f);
            }
            Assert.AreEqual(1, wrong.Count);
            Assert.AreEqual(Tree.CompassForkAddress, wrong[0]);
        }

        // C3 (P2-D07 4-fork): blocked on PH-39.
        [Test, Ignore("Blocked on PH-39: fixture route has 6 forks; P2-D07's 4-fork acceptance not claimed")]
        public void P2B08_FourForkRoute_P2D07()
        {
            Assert.AreEqual(4, AirwayRoute.Full(Tree).Forks);
        }

        // C4: MP §10.2.6 fork and compass_choice events.
        [Test]
        public void P2B08_Events_ForkAndCompassChoice()
        {
            var route = AirwayRoute.Full(Tree);
            var log = new ForkLog(Tree);
            var ev = Capture(() =>
            {
                log.Reset(route);
                log.Observe(AirwayTile.Fork(""), 0, false);
                log.Observe(AirwayTile.Bend("R"), 3.5, false);
            });
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual("lung", ev[0].Scene); Assert.AreEqual("lung", ev[1].Scene);
            Assert.AreEqual("fork", ev[0].Ev); Assert.AreEqual("compass_choice", ev[1].Ev);
            var f = ForkEventData.FromJson(ev[0].Data);
            Assert.AreEqual("", f.address); Assert.AreEqual("R", f.choice); Assert.IsTrue(f.correct); Assert.IsFalse(f.beacon_shown);
            Assert.AreEqual(3.5f, f.t_s, 1e-4f);
            var c = CompassChoiceEventData.FromJson(ev[1].Data);
            Assert.AreEqual("", c.address); Assert.AreEqual("R", c.choice); Assert.AreEqual("L", c.compass); Assert.AreEqual("R", c.beacon);
            Assert.IsTrue(c.correct);

            ev = Capture(() =>
            {
                log.Reset(route);
                log.Observe(AirwayTile.Fork("R"), 10, false);
                log.Observe(AirwayTile.Fork("R"), 18, true);
                log.Observe(AirwayTile.Bend("RL"), 20, false);
            });
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual("fork", ev[0].Ev);
            f = ForkEventData.FromJson(ev[0].Data);
            Assert.AreEqual("R", f.address); Assert.AreEqual("L", f.choice); Assert.IsFalse(f.correct); Assert.IsTrue(f.beacon_shown);
            Assert.AreEqual(10f, f.t_s, 1e-4f);
        }

        // C5: moves back toward the parent and bend-to-fork moves emit nothing.
        [Test]
        public void P2B08_Events_NoneOnBackMove()
        {
            var log = new ForkLog(Tree);
            var ev = Capture(() =>
            {
                log.Reset(AirwayRoute.Full(Tree));
                log.Observe(AirwayTile.Fork("R"), 0, false);
                log.Observe(AirwayTile.Bend("R"), 1, false);
                log.Observe(AirwayTile.Fork(""), 2, false);
            });
            Assert.AreEqual(0, ev.Count);
        }

#if !ATLAS_HEADLESS
        // C6 (supporting): the ghost compass arrow is active only at the designed fork.
        [Test, Category("Unity")]
        public void TwoCompassesView_OnlyAtDesignedFork()
        {
            var b = new AirwayBeacon(Tree);
            var go = new UnityEngine.GameObject("ghost");
            try
            {
                var v = go.AddComponent<TwoCompassesView>();
                var pose = new WalkerPose { Tile = TileId.Zero, Local = new Iso2(double3x3.identity, -1) };
                var frame = LungTestEnv.Frame(1, 0, 0.01f, new float3(0, 1.6f, 0), quaternion.identity);
                var route = AirwayRoute.Full(Tree);
                b.Tick(AirwayTile.Fork(""), 0);
                v.Apply(frame, pose, Tree, b, route); Assert.IsTrue(go.activeSelf);
                b.Tick(AirwayTile.Bend("R"), 1);
                v.Apply(frame, pose, Tree, b, route); Assert.IsFalse(go.activeSelf);
                b.Tick(AirwayTile.Fork("R"), 2);
                v.Apply(frame, pose, Tree, b, route); Assert.IsFalse(go.activeSelf);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
#endif
    }
}
