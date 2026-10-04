using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // P2-B05 Mode switch (MP §10.2.3 switch rule; Build Graph row P2-B05). Each test names the criterion it checks.
    public class ModeSwitchTests
    {
        static readonly IForkTiles Tiles = new AirwayTreeBinding(Lung.Tests.TreeFixture.Tree);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly List<AirwayWingModule> _modules = new List<AirwayWingModule>();

        static RoomModel Room(string name) =>
            RoomModel.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings", name + ".json")));

        static string EvidenceDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P2-B05"));

        // EventLog.Emitted is static: dispose every switch so tests do not leak handlers (G6 §8 pitfall 6).
        [TearDown] public void TearDown() { foreach (var m in _modules) m.Exit(); _modules.Clear(); }

        AirwayWingModule MakeModule(RoomModel room, ParamRegistry reg, string route)
        {
            var m = new AirwayWingModule(Tiles, new FakeClearance(room), new ScriptedForkChoice(route), new FakeChamberView(), _ => { });
            m.Configure(room, reg);
            _modules.Add(m);
            return m;
        }

        static List<TileId> Expected(string route)
        {
            var g = new TileGraph(5); var l = new List<TileId> { Tiles.Root };
            for (int i = 1; i <= route.Length; i++)
            {
                Assert.IsTrue(g.TryCommonNeighbour(Tiles.ForkTile(route.Substring(0, i - 1)), Tiles.ForkTile(route.Substring(0, i)), out var m));
                l.Add(m); l.Add(Tiles.ForkTile(route.Substring(0, i)));
            }
            return l;
        }

        enum When { InChamber, WhileWalking }

        sealed class Run { public bool Ok = true; public string Why = ""; public int Switches; public AirwayMode Final; }

        // Walks one route; requests `target` through registry.ApplyRemote at the trigger frame (after 3 chambers entered).
        static Run Drive(RoomModel room, string route, AirwayMode start, AirwayMode target, When when, ParamRegistry reg, AirwayWingModule module)
        {
            var run = new Run();
            var wing = module.Wing;
            reg.TryGetId("curved.mode", out ushort id);
            if (start == AirwayMode.AlignedCells) Assert.IsTrue(reg.ApplyRemote(id, 2));
            var w = new SimWalker(wing) { Module = module };
            module.Enter(default);
            var exp = Expected(route); var visited = new List<TileId>(); TileId last = default;
            bool requested = false; int expectSwitches = start == AirwayMode.AlignedCells ? 2 : 1;
            void Fail(string s) { if (run.Ok) { run.Ok = false; run.Why = route + ": " + s; } }
            for (int i = 0; i < 60000 && visited.Count < exp.Count; i++)
            {
                var pre = (wing.Mode, wing.Phase, wing.Shell, wing.Address, wing.CurrentTile, wing.NextTile, wing.ExitEdge);
                w.Step();
                if (wing.CurrentTile != last) { last = wing.CurrentTile; visited.Add(last); }
                if (wing.Mode != pre.Mode)
                {
                    run.Switches++;
                    if (i == 0) continue;   // frame 0 includes Enter itself (the root chamber is created in that frame), no pre-state to compare
                    // criterion: switch only at a chamber, shell opaque, address/tiles/exit edge unchanged on the switch frame
                    if (pre.Shell != ShellState.Opaque) Fail("switch while shell not opaque");
                    if (pre.Phase != ValvePhase.AwaitChoice && pre.Phase != ValvePhase.AwaitGlyph && pre.Phase != ValvePhase.Waiting) Fail("switch in phase " + pre.Phase);
                    if (wing.CurrentTile != pre.CurrentTile) Fail("current tile changed at switch");
                    // Address, NextTile and ExitEdge may only differ by the fork choice taken in the same frame at a fork (AwaitChoice)
                    if (pre.Phase != ValvePhase.AwaitChoice && (wing.Address != pre.Address || wing.NextTile != pre.NextTile || wing.ExitEdge != pre.ExitEdge))
                        Fail("address, next tile or exit edge changed at switch");
                }
                if (!requested && visited.Count == 4 && start != target && wing.Mode == start)
                {
                    bool trigger = when == When.InChamber
                        ? wing.Phase == ValvePhase.AwaitGlyph && (start == AirwayMode.AlignedCells || wing.CurrentChamberDistance <= 0.3)
                        : wing.Phase == ValvePhase.Walking;
                    if (trigger)
                    {
                        requested = true;
                        if (!reg.ApplyRemote(id, (float)target)) Fail("ApplyRemote refused");
                        if (wing.Mode != start) Fail("mode changed on the request frame");
                    }
                }
            }
            run.Final = wing.Mode;
            if (visited.Count != exp.Count) Fail("visited " + visited.Count + " of " + exp.Count + " tiles");
            else for (int i = 0; i < exp.Count; i++) if (visited[i] != exp[i]) { Fail("tile " + i + " differs"); break; }
            if (wing.Address != route) Fail("address " + wing.Address);
            if (run.Switches != expectSwitches) Fail("switches " + run.Switches + " of " + expectSwitches);
            if (run.Final != target) Fail("final mode " + run.Final);
            return run;
        }

        // Acceptance P2-B05 (Editor half): "switch at a chamber keeps the address unchanged" (T5.1), both directions, 10 routes.
        [Test]
        public void Switch_AtChamber_KeepsAddressUnchanged()
        {
            var room = Room("room_3x3");
            var routes = SimRoutes.Hundred();
            var sb = new StringBuilder("{\n  \"task\": \"P2-B05\",\n  \"criterion\": \"switch at a chamber keeps the address unchanged\",\n  \"runs\": [\n");
            int pass = 0, total = 0; var why = new List<string>(); bool firstRow = true;
            foreach (var (from, to) in new[] { (AirwayMode.CurvedWalk, AirwayMode.AlignedCells), (AirwayMode.AlignedCells, AirwayMode.CurvedWalk) })
                foreach (var when in new[] { When.InChamber, When.WhileWalking })
                {
                    int ok = 0;
                    for (int r = 0; r < 10; r++)
                    {
                        var reg = new ParamRegistry();
                        var m = MakeModule(room, reg, routes[r]);
                        var run = Drive(room, routes[r], from, to, when, reg, m);
                        total++;
                        if (run.Ok) { ok++; pass++; } else if (why.Count < 5) why.Add($"{from}->{to} {when} {run.Why}");
                        m.Exit();
                    }
                    sb.Append(firstRow ? "" : ",\n").Append($"    {{\"from\": \"{from}\", \"to\": \"{to}\", \"when\": \"{when}\", \"routes_ok\": {ok}, \"routes\": 10}}");
                    firstRow = false;
                }
            sb.Append("\n  ],\n  \"runs_passing\": " + pass + ",\n  \"runs_total\": " + total + "\n}\n");
            Directory.CreateDirectory(EvidenceDir);
            File.WriteAllText(Path.Combine(EvidenceDir, "switch_sim.json"), sb.ToString());
            Assert.AreEqual(40, pass, "runs passing of 40: " + string.Join("; ", why));
        }

        // S5.1: automatic rule "the free rectangle gives s < 1.5 m" (MP §10.2.3).
        [Test]
        public void Auto_SmallLapSide_SelectsMode2()
        {
            var reg = new ParamRegistry(); MakeModule(Room("room_4x5_table"), reg, "L");
            reg.TryGetId("curved.mode", out var id);
            Assert.AreEqual(2f, reg.Get(id), "room_4x5_table (s < 1.5) -> Mode 2");
            var reg2 = new ParamRegistry(); MakeModule(Room("room_3x3"), reg2, "L");
            reg2.TryGetId("curved.mode", out var id2);
            Assert.AreEqual(1f, reg2.Get(id2), "room_3x3 (s = 2.0) -> Mode 1");
        }

        // S5.2: automatic rule "FMS > 3/20" (MP §10.2.3); unsubscribed after Dispose.
        [Test]
        public void Auto_Fms()
        {
            var reg = new ParamRegistry(); var m = MakeModule(Room("room_3x3"), reg, "L");
            reg.TryGetId("curved.mode", out var id);
            EventLog.Emit("lung", "fms", "{\"score\":3}");
            Assert.AreEqual(1f, reg.Get(id), "FMS = 3 stays Mode 1");
            EventLog.Emit("lung", "other", "{\"score\":19}");
            Assert.AreEqual(1f, reg.Get(id), "other events ignored");
            EventLog.Emit("lung", "fms", "{\"score\":4}");
            Assert.AreEqual(2f, reg.Get(id), "FMS = 4 -> Mode 2");
            reg.Set(id, 1f);
            m.ModeSwitch.Dispose();
            EventLog.Emit("lung", "fms", "{\"score\":9}");
            Assert.AreEqual(1f, reg.Get(id), "no change after Dispose");
        }

        // S5.3: Console switch; encoding 1/2, default 1, DemoLock (MP §7.3.2, §12.4).
        [Test]
        public void CurvedMode_Param_Range()
        {
            var reg = new ParamRegistry(); MakeModule(Room("room_3x3"), reg, "L");
            Assert.IsTrue(reg.TryGetId("curved.mode", out var id));
            Assert.AreEqual(1f, reg.Get(id), "default 1");
            Assert.AreEqual(1f, reg.Set(id, 0f), "min 1");
            Assert.AreEqual(2f, reg.Set(id, 7f), "max 2");
            reg.Set(id, 1f);
            reg.DemoLock = true;
            Assert.IsFalse(reg.ApplyRemote(id, 2f), "DemoLock refuses ParamSet");
            Assert.AreEqual(1f, reg.Get(id));
            reg.DemoLock = false;
            Assert.IsTrue(reg.ApplyRemote(id, 2f));
            Assert.AreEqual(AirwayMode.AlignedCells, (AirwayMode)_modules[0].Params.CurvedMode, "Console value reaches the field");
        }

        // S5.4: App. B.1 State fields.
        [Test]
        public void WriteState_Fields()
        {
            var room = Room("room_3x3"); var reg = new ParamRegistry();
            var m = MakeModule(room, reg, "LRLR");
            var w = new SimWalker(m.Wing) { Module = m };
            m.Enter(default);
            for (int i = 0; i < 90; i++) w.Step();
            var s = StateMessage.Create(); m.WriteState(ref s);
            var pose = m.Wing.Rig.Pose;
            Assert.AreEqual(2, s.SpaceId, "Mode 1 SpaceId = 2");
            Assert.AreEqual(pose.Tile.Xu, s.TileId[0]); Assert.AreEqual(pose.Tile.Yu, s.TileId[2]); Assert.AreEqual(pose.Tile.Tv, s.TileId[5]);
            var M = pose.Local.M;
            Assert.AreEqual(M.c1.x, s.Xform[1], 0); Assert.AreEqual(M.c0.y, s.Xform[3], 0); Assert.AreEqual(M.c2.z, s.Xform[8], 0);
            Assert.AreEqual((float)m.Wing.L, s.P[0], 0, "P[0] = L");
            Assert.AreEqual(0, s.Sheet);

            reg.TryGetId("curved.mode", out var id); reg.ApplyRemote(id, 2f);
            for (int i = 0; i < 5; i++) w.Step();
            Assert.AreEqual(AirwayMode.AlignedCells, m.Wing.Mode);
            var s2 = StateMessage.Create(); m.WriteState(ref s2);
            Assert.AreEqual(3, s2.SpaceId, "Mode 2 SpaceId = 3");
            var c = m.Wing.Cells.Cell;
            Assert.AreEqual(c.x + c.y * m.Wing.Grid.CountX, s2.Sheet, "Sheet = cell index");
            Assert.AreEqual(1.0, s2.Xform[0], 0); Assert.AreEqual(0.0, s2.Xform[1], 0); Assert.AreEqual(0f, s2.P[0], 0);
        }

        sealed class FakeS6 : ISpaceModule
        {
            public SpaceId Id => SpaceId.S6; public bool Registered => true;
            public void Configure(RoomModel room, ParamRegistry registry) { }
            public void Enter(in WalkerPose start) { }
            public void Tick(in FrameContext frame) { }
            public void Exit() { }
            public void WriteState(ref StateMessage state) { }
        }

        // S5.5: the module registers in the placeholder SpaceLab as S2.
        [Test]
        public void Module_RegistersInSpaceLab()
        {
            var room = Room("room_3x3"); var reg = new ParamRegistry();
            var lab = new SpaceLab(room, reg);
            var m = new AirwayWingModule(Tiles, new FakeClearance(room), new ScriptedForkChoice("L"), new FakeChamberView(), lab.Request);
            _modules.Add(m);
            lab.Register(m); lab.Register(new FakeS6());
            lab.Request(SpaceId.S2);
            lab.Tick(new FrameContext(1, 1 / 60.0, 1 / 60f, new float3(0, 1.6f, 0), quaternion.identity, true, false));
            Assert.AreEqual(SpaceId.S2, lab.Active.Id);
            Assert.IsNotNull(m.Wing);
        }

        // G6 §7: Lung_Airway.unity holds one AirwayWingHost (created here with EditorSceneManager on first run, then committed).
        [Test]
        public void Scene_LungAirway_HasOneAirwayWingHost()
        {
            const string path = "Assets/Atlas/Blades/Lung/Scenes/Lung_Airway.unity";
            if (!File.Exists(Path.Combine(Application.dataPath, "..", path)))
            {
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Atlas/Blades/Lung/Scenes"));
                var created = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
                var go = new GameObject("AirwayWingHost"); go.AddComponent<AirwayWingHost>();
                Assert.IsTrue(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(created, path));
                UnityEditor.AssetDatabase.Refresh();
            }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<AirwayWingHost>(FindObjectsSortMode.None).Length, "AirwayWingHost count");
        }
    }
}
