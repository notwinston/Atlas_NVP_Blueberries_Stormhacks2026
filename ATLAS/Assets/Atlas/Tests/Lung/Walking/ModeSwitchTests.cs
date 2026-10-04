using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // P2-B05 Mode switch (MP §10.2.3 switch rule; Build Graph row P2-B05). Each test names the criterion it checks.
    public class ModeSwitchTests
    {
        static readonly IForkTiles Tiles = new AirwayTreeBinding(Lung.Tests.TreeFixture.Tree);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly List<AirwayWingModule> _modules = new List<AirwayWingModule>();

        static RoomModel Room(string name) => RoomFixtures.LoadFromProject(name);

        // The module subscribes to the published EventLog: exit every module so no handler outlives its test (G6 §8 pitfall 6).
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

        // Walks one route; requests `target` as a remote ParamSet of curved.mode at the trigger frame (after 3 chambers entered).
        static Run Drive(RoomModel room, string route, AirwayMode start, AirwayMode target, When when, ParamRegistry reg, AirwayWingModule module)
        {
            var run = new Run();
            var wing = module.Wing;
            var mode = reg.Find(AirwayParams.CurvedMode);
            if (start == AirwayMode.AlignedCells) Assert.IsTrue(mode.Set(AirwayParams.IndexOf(AirwayMode.AlignedCells), ParamSource.Remote));
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
                        if (!mode.Set(AirwayParams.IndexOf(target), ParamSource.Remote)) Fail("ParamSet refused");
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
            var room = Room(RoomFixtures.Sample3x3);
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
            Lung.Tests.LungTestEnv.WriteEvidence("P2-B05", "switch_sim.json", sb.ToString());
            Assert.AreEqual(40, pass, "runs passing of 40: " + string.Join("; ", why));
        }

        // S5.1: automatic rule "the free rectangle gives s < 1.5 m" (MP §10.2.3).
        [Test]
        public void Auto_SmallLapSide_SelectsMode2()
        {
            var reg = new ParamRegistry(); MakeModule(Room(RoomFixtures.Sample4x5Table), reg, "L");
            Assert.AreEqual(AirwayMode.AlignedCells, AirwayParams.ModeOf(reg.Find(AirwayParams.CurvedMode)), "sample_4x5_table (s = 1.2 < 1.5) -> Mode 2");
            var reg2 = new ParamRegistry(); MakeModule(Room(RoomFixtures.Sample3x3), reg2, "L");
            Assert.AreEqual(AirwayMode.CurvedWalk, AirwayParams.ModeOf(reg2.Find(AirwayParams.CurvedMode)), "sample_3x3 (s = 2.0) -> Mode 1");
        }

        // S5.2: automatic rule "FMS > 3/20" (MP §10.2.3), read from the "fms" event of the published EventLog; unsubscribed after Dispose.
        [Test]
        public void Auto_Fms()
        {
            using (var capture = new Lung.Tests.EventCapture())
            {
                var reg = new ParamRegistry(); var m = MakeModule(Room(RoomFixtures.Sample3x3), reg, "L");
                var mode = reg.Find(AirwayParams.CurvedMode);
                capture.Log.LogJson("lung", "fms", "{\"score\":3}");
                Assert.AreEqual(0, mode.Int, "FMS = 3 stays Mode 1");
                capture.Log.LogJson("lung", "other", "{\"score\":19}");
                Assert.AreEqual(0, mode.Int, "other events ignored");
                capture.Log.LogJson("lung", "fms", "{\"score\":4}");
                Assert.AreEqual(1, mode.Int, "FMS = 4 -> Mode 2");
                mode.Set(0);
                m.ModeSwitch.Dispose();
                capture.Log.LogJson("lung", "fms", "{\"score\":9}");
                Assert.AreEqual(0, mode.Int, "no change after Dispose");
            }
        }

        // S5.3: Console switch; curved.mode is lane A's Enum declaration, index 0 = Mode 1 (default), 1 = Mode 2 (MP §7.3.2, §12.4).
        // The demo lock is lane D's (LinkClient and LinkPump drop the ParamSet and log param.ignored; Atlas.Link.Tests covers it), not the registry's.
        [Test]
        public void CurvedMode_Param_Range()
        {
            var reg = new ParamRegistry(); var m = MakeModule(Room(RoomFixtures.Sample3x3), reg, "L");
            var mode = reg.Find(AirwayParams.CurvedMode);
            Assert.IsNotNull(mode);
            Assert.AreEqual(ParamKind.Enum, mode.Kind);
            Assert.AreEqual(0, mode.Int, "default Mode 1");
            mode.Set(-1);
            Assert.AreEqual(0, mode.Int, "min Mode 1");
            mode.Set(7);
            Assert.AreEqual(1, mode.Int, "max Mode 2");
            mode.Set(0);
            Assert.IsTrue(mode.Set(1, ParamSource.Remote));
            Assert.AreEqual(AirwayMode.AlignedCells, m.ModeSwitch.Requested, "Console value reaches the switch");
        }

        // S5.3b: an existing declaration (lane A's AppParams) is the one that stays.
        [Test]
        public void CurvedMode_ExistingDeclarationKept()
        {
            var reg = new ParamRegistry();
            var declared = reg.DeclareEnum(AirwayParams.CurvedMode, "curved", new[] { "Mode 1 Curved Walk", "Mode 2 Aligned Cells" }, 0, null, true);
            MakeModule(Room(RoomFixtures.Sample3x3), reg, "L");
            Assert.AreSame(declared, reg.Find(AirwayParams.CurvedMode));
            Assert.AreEqual(1, reg.Count);
        }

        // S5.4: App. B.1 State fields.
        [Test]
        public void WriteState_Fields()
        {
            var room = Room(RoomFixtures.Sample3x3); var reg = new ParamRegistry();
            var m = MakeModule(room, reg, "LRLR");
            var w = new SimWalker(m.Wing) { Module = m };
            m.Enter(default);
            for (int i = 0; i < 90; i++) w.Step();
            var s = StateMessage.Identity; m.WriteState(ref s);
            var pose = m.Wing.Rig.Pose;
            Assert.AreEqual((byte)SpaceId.CurvedWalk, s.SpaceId, "Mode 1 SpaceId = S2");
            Assert.AreEqual(pose.Tile.Xu, s.GetTile(0)); Assert.AreEqual(pose.Tile.Yu, s.GetTile(2)); Assert.AreEqual(pose.Tile.Tv, s.GetTile(5));
            var M = pose.Local.M;   // State.xform is row-major
            Assert.AreEqual(M.c1.x, s.GetXform(1), 0); Assert.AreEqual(M.c0.y, s.GetXform(3), 0); Assert.AreEqual(M.c2.z, s.GetXform(8), 0);
            Assert.AreEqual((float)m.Wing.L, s.GetParam(StateMessage.ParamL), 0, "P0 = L");
            Assert.AreEqual(0, s.Sheet);

            reg.Find(AirwayParams.CurvedMode).Set(1, ParamSource.Remote);
            for (int i = 0; i < 5; i++) w.Step();
            Assert.AreEqual(AirwayMode.AlignedCells, m.Wing.Mode);
            var s2 = StateMessage.Identity; m.WriteState(ref s2);
            Assert.AreEqual((byte)SpaceId.AlignedCells, s2.SpaceId, "Mode 2 SpaceId = S3");
            var c = m.Wing.Cells.Cell;
            Assert.AreEqual(c.x + c.y * m.Wing.Grid.CountX, s2.Sheet, "Sheet = cell index");
            Assert.AreEqual(1.0, s2.GetXform(0), 0); Assert.AreEqual(0.0, s2.GetXform(1), 0); Assert.AreEqual(0f, s2.GetParam(StateMessage.ParamL), 0);
        }

        // S5.5: the module is S2's entry in the SpaceRegistry and runs the ISpaceModule lifecycle the real SpaceLab drives.
        [Test]
        public void Module_RegistersAsS2_AndRunsTheLifecycle()
        {
            var room = Room(RoomFixtures.Sample3x3); var reg = new ParamRegistry();
            var m = new AirwayWingModule(Tiles, new FakeClearance(room), new ScriptedForkChoice("L"), new FakeChamberView(), _ => { });
            _modules.Add(m);
            try
            {
                SpaceRegistry.Register(SpaceId.CurvedWalk, () => m);
                Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.CurvedWalk));
                ISpaceModule module = SpaceRegistry.Create(SpaceId.CurvedWalk);
                Assert.AreSame(m, module);
                Assert.AreEqual(SpaceId.CurvedWalk, module.Id);
                module.Configure(room, reg);
                Assert.IsFalse(module.Registered, "constant from Configure to Exit");
                module.Enter(WalkerPose.Start);
                for (int f = 1; f <= 3; f++) module.Tick(Lung.Tests.LungTestEnv.Frame(f, f / 60.0, 1 / 60f, new float3(0, 1.6f, 0), quaternion.identity));
                Assert.IsNotNull(m.Wing);
                var s = StateMessage.Identity;
                module.WriteState(ref s);
                Assert.AreEqual((byte)SpaceId.CurvedWalk, s.SpaceId);
                module.Exit();
                Assert.IsFalse(module.Registered);
            }
            finally { SpaceRegistry.Clear(); }
        }

#if !ATLAS_HEADLESS
        // G6 §7: Lung_Airway.unity holds one AirwayWingHost (created here with EditorSceneManager on first run, then committed).
        [Test, Category("Unity")]
        public void Scene_LungAirway_HasOneAirwayWingHost()
        {
            const string path = "Assets/Atlas/Blades/Lung/Scenes/Lung_Airway.unity";
            if (!System.IO.File.Exists(System.IO.Path.Combine(Lung.Tests.LungTestEnv.ProjectDir, path)))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Lung.Tests.LungTestEnv.ProjectDir, "Assets/Atlas/Blades/Lung/Scenes"));
                var created = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
                var go = new UnityEngine.GameObject("AirwayWingHost"); go.AddComponent<AirwayWingHost>();
                Assert.IsTrue(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(created, path));
                UnityEditor.AssetDatabase.Refresh();
            }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<AirwayWingHost>(UnityEngine.FindObjectsSortMode.None).Length, "AirwayWingHost count");
        }
#endif
    }
}
