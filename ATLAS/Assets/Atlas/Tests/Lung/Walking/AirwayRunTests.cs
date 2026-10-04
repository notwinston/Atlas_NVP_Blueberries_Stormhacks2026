using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // P2-B12 airway run test (MP §10.2.8 row P2-B12; G6 §4.5). PH-39: the fixture route has 6 forks; no test hard-codes 4 and none
    // claims the 4-fork acceptance (it stays open; the phone checklist is docs/laneB/TESTING.md G10-B12).
    public class AirwayRunTests
    {
        static readonly IForkTiles Tiles = new AirwayTreeBinding(Lung.Tests.TreeFixture.Tree);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static RoomModel Room(string name) =>
            RoomModel.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings", name + ".json")));

        static string EvidenceDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P2-B12"));

        static readonly string[] Rooms = { "room_3x3", "room_3x4_L", "room_4x5_table" };

        // Walks `route` on the centre strip in Mode 1 until `steps` chambers are entered; returns the oracle result.
        static (int straight, int corner, int strong, int entries) Walk(RoomModel room, string route, double speed, int steps)
        {
            var clear = new FakeClearance(room);
            var wing = new AirwayWing(room, Tiles, clear, new ScriptedForkChoice(route), new FakeChamberView());
            var oracle = new BumpWarnOracle(clear, new LapCorners(room.lapSquare.s));
            int entries = 0; wing.ChamberEntered = (a, t) => entries++;
            var w = new SimWalker(wing) { Speed = speed };
            wing.Enter();
            for (int f = 0; f < 400000 && entries < steps; f++) { w.Step(); oracle.Sample(w.HeadXZ); }
            return (oracle.StraightWarnStarts, oracle.CornerCues, oracle.StrongStarts, entries);
        }

        // T12.1, criterion: "centre strip 8 tile steps, no WARN on straight sides" (MP §10.2.8 P2-B12), Mode 1, 3 rooms, 0.7 and 1.0 m/s.
        [Test]
        public void Run_CentreStrip_8TileSteps_NoWarnOnStraightSides()
        {
            string target = Tiles.TargetAddress;
            int treeSteps = 2 * target.Length;
            Assert.GreaterOrEqual(treeSteps, 8, "tree.json route has at least 8 tile steps");
            var sb = new StringBuilder("{\n  \"criterion\": \"P2-B12 centre strip 8 tile steps, no WARN on straight sides\",\n");
            sb.Append("  \"target_address\": \"" + target + "\", \"route_forks\": " + target.Length + ", \"tile_steps\": " + treeSteps + ",\n");
            sb.Append("  \"note\": \"4-fork route acceptance blocked on PH-39\",\n  \"runs\": [\n");
            string[] hundred = SimRoutes.Hundred(); bool first = true;
            foreach (var name in Rooms)
                foreach (double speed in new[] { 0.7, 1.0 })
                {
                    var room = Room(name);
                    var a = Walk(room, target, speed, treeSteps);
                    Assert.AreEqual(treeSteps, a.entries, name + " " + speed + ": tree route completes");
                    Assert.AreEqual(0, a.straight, name + " " + speed + " tree route: straight-side WARN starts");
                    Assert.AreEqual(0, a.strong, name + " " + speed + " tree route: STRONG starts");
                    int sStraight = 0, sCorner = 0, sStrong = 0;
                    foreach (var r in hundred)
                    {
                        var b = Walk(room, r, speed, 8);
                        Assert.AreEqual(8, b.entries, name + " " + r + ": 8 tile steps");
                        sStraight += b.straight; sCorner += b.corner; sStrong += b.strong;
                    }
                    Assert.AreEqual(0, sStraight, name + " " + speed + " 100 routes: straight-side WARN starts");
                    Assert.AreEqual(0, sStrong, name + " " + speed + " 100 routes: STRONG starts");
                    if (!first) sb.Append(",\n"); first = false;
                    sb.Append("    {\"room\":\"" + name + "\",\"speed\":" + speed.ToString("0.0", Inv) +
                              ",\"tree_route\":{\"straight_warn\":" + a.straight + ",\"corner_cues\":" + a.corner + ",\"strong\":" + a.strong + "}" +
                              ",\"hundred_routes\":{\"straight_warn\":" + sStraight + ",\"corner_cues\":" + sCorner + ",\"strong\":" + sStrong + "}}");
                }
            sb.Append("\n  ]\n}\n");
            Directory.CreateDirectory(EvidenceDir);
            File.WriteAllText(Path.Combine(EvidenceDir, "sim_centre_strip.json"), sb.ToString());
        }

        sealed class Run
        {
            public AirwayWing Wing; public NavigationBridge Bridge; public SimWalker Walker; public AirwayRoute Route;
            public readonly List<EventMessage> Events = new List<EventMessage>(); public Action<EventMessage> Handler; public int Entries;
            public void Frame() { Walker.Step(); Bridge.Observe(Wing, Walker.Frame * SimWalker.Dt); }
            public void Dispose() { EventLog.Emitted -= Handler; }
        }

        static Run Start(Func<string, char> source)
        {
            var room = Room("room_3x3"); var tree = Lung.Tests.TreeFixture.Tree;
            var r = new Run { Route = AirwayRoute.Full(tree) };
            r.Bridge = new NavigationBridge(tree, Tiles, r.Route) { ChoiceSource = source };
            r.Wing = new AirwayWing(room, Tiles, new FakeClearance(room), r.Bridge, new FakeChamberView());
            r.Bridge.Attach(r.Wing);
            r.Wing.ChamberEntered = (a, t) => r.Entries++;
            r.Walker = new SimWalker(r.Wing);
            r.Handler = e => r.Events.Add(e); EventLog.Emitted += r.Handler;
            r.Wing.Enter();
            return r;
        }

        // T12.2, criterion: "Output: run report" (MP §10.2.8 P2-B12): RouteForks == TargetAddress.Length (PH-39), rescue-free, FMS.
        [Test]
        public void RunReport_FromForkEvents()
        {
            string target = Tiles.TargetAddress;
            var r = Start(a => target[a.Length]);
            try
            {
                for (int f = 0; f < 400000 && r.Entries < 2 * target.Length; f++) r.Frame();
                Assert.AreEqual(2 * target.Length, r.Entries);
                EventLog.Emit("lung", "fms", "{\"score\":2}");
                if (!r.Events.Exists(e => e.Ev == "compass_choice"))
                    EventLog.Emit("lung", "compass_choice", "{\"address\":\"\",\"choice\":\"L\",\"correct\":true}");   // injected (G6 §4.5)
                var rep = AirwayRunReport.FromEvents(r.Events, target);
                Assert.AreEqual(target.Length, rep.RouteForks);
                Assert.AreEqual(rep.RouteForks, rep.ForksWalked);
                Assert.AreEqual(rep.RouteForks, rep.ForksCorrect);
                Assert.AreEqual(0, rep.BeaconShownCount);
                Assert.IsTrue(rep.RescueFree);
                Assert.AreEqual(2f, rep.FmsMax);
                Assert.IsTrue(rep.CompassForkLogged);
                var lines = new List<string>(); foreach (var e in r.Events) lines.Add(AirwayRunReport.ToJsonLine(e));
                var rep2 = AirwayRunReport.FromJsonLines(lines, target);
                Assert.IsTrue(rep.SameAs(rep2), rep.ToJson() + " vs " + rep2.ToJson());
                Directory.CreateDirectory(EvidenceDir);
                File.WriteAllText(Path.Combine(EvidenceDir, "run_report_sim.json"), rep.ToJson() + "\n");
                // a wrong run is not rescue free; no events gives FmsMax -1
                Assert.AreEqual(-1f, AirwayRunReport.FromEvents(new List<EventMessage>(), target).FmsMax);
                Assert.IsFalse(AirwayRunReport.FromEvents(new List<EventMessage>(), target).RescueFree);
            }
            finally { r.Dispose(); }
        }

        // S12.1, criterion: "fork choice reaches the wing through the bridge; fork event carries beacon_shown from the beacon state".
        [Test]
        public void Bridge_ForkChoiceFromForkUi()
        {
            string target = Tiles.TargetAddress;
            var tree = Lung.Tests.TreeFixture.Tree;
            int k = 0; while (k < target.Length && target.Substring(0, k) == tree.CompassForkAddress) k++;
            string hold = null; bool holdOn = false;
            var r = Start(a => holdOn && a == hold ? '\0' : target[a.Length]);
            try
            {
                // k = first fork on the route that is not the Two Compasses fork (the Two Compasses arrival is not a rescue, D-G4-7)
                k = 0; while (k < target.Length && target.Substring(0, k) == tree.CompassForkAddress) k++;
                hold = target.Substring(0, k); holdOn = true;
                for (int f = 0; f < 400000 && !(r.Wing.Address == hold && r.Wing.Phase == ValvePhase.AwaitChoice); f++) r.Frame();
                Assert.AreEqual(hold, r.Wing.Address);
                for (int i = 0; i < 60 * 9 + 30; i++) r.Frame();   // stand 9.5 s without a choice
                Assert.AreEqual(hold, r.Wing.Address, "no choice, no letter");
                Assert.IsTrue(r.Bridge.BeaconShown, "rescue shown after 8 s hesitation");
                r.Bridge.Submit(target[hold.Length]); holdOn = false;
                for (int i = 0; i < 10; i++) r.Frame();
                Assert.AreEqual(hold + target[hold.Length], r.Wing.Address, "wing Address gains the submitted letter");
                var fork = r.Events.Find(e => e.Ev == "fork" && e.Data.Contains("\"address\":\"" + hold + "\""));
                Assert.IsNotNull(fork.Data, "fork event emitted");
                StringAssert.Contains("\"beacon_shown\":true", fork.Data);
            }
            finally { r.Dispose(); }
        }
    }
}
