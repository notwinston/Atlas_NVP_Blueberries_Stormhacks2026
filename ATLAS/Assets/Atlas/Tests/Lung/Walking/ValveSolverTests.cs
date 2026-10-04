using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // P2-B03 ValveSolver tests (Master Plan §10.2.3 steps 1-5; §10.2.8 row P2-B03). Each test names the criterion it checks.
    public class ValveSolverTests
    {
        static readonly string[] Rooms = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table };
        static readonly IForkTiles Tiles = new AirwayTreeBinding(Lung.Tests.TreeFixture.Tree);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static RoomModel Room(string name) => RoomFixtures.LoadFromProject(name);

        static List<TileId> ExpectedTiles(string route, IForkTiles tiles, TileGraph g)
        {
            var list = new List<TileId> { tiles.Root };
            for (int i = 1; i <= route.Length; i++)
            {
                var a = tiles.ForkTile(route.Substring(0, i - 1)); var b = tiles.ForkTile(route.Substring(0, i));
                Assert.IsTrue(g.TryCommonNeighbour(a, b, out var m));
                list.Add(m); list.Add(b);
            }
            return list;
        }

        sealed class RouteRun
        {
            public bool Ok = true; public string Why = ""; public double MinWall = double.PositiveInfinity; public int Reaims; public long Frames;
        }

        // Runs one route in Mode 1 until the last fork chamber is entered; checks the P2-B03 acceptance conditions.
        static RouteRun RunRoute(RoomModel room, string route, double lateral = 0, double speed = 0.7, double wallBound = 0.40)
        {
            var run = new RouteRun();
            var g = new TileGraph(5);
            var view = new FakeChamberView();
            var wing = new AirwayWing(room, Tiles, new FakeClearance(room), new ScriptedForkChoice(route), view);
            var lap = new LapCorners(room.LapSide);
            var exp = ExpectedTiles(route, Tiles, g);
            var visited = new List<TileId>();
            var records = new List<(ShellState rec, ShellState view)>();
            bool regOk = true;
            wing.Reaimed += r =>
            {
                records.Add((r.Shell, view.CurrentShell[r.Tile]));
                double2 cur = lap.Corner(lap.Nearest(wing.CurrentChamberWorld));
                int nk = lap.Nearest(wing.NextChamberWorld);
                int diff = (nk - lap.Nearest(wing.CurrentChamberWorld) + 4) & 3;
                if (math.length(wing.NextChamberWorld - lap.Corner(nk)) > 1e-3 || (diff != 1 && diff != 3)) regOk = false;
            };
            var w = new SimWalker(wing) { Lateral = lateral, Speed = speed };
            wing.Enter();
            TileId last = default;
            for (int i = 0; i < 40000 && !(visited.Count == exp.Count); i++)
            {
                w.Step();
                run.MinWall = Math.Min(run.MinWall, WallDistance.Of(room, w.HeadXZ));
                if (wing.CurrentTile != last) { last = wing.CurrentTile; visited.Add(last); }
            }
            run.Frames = w.Frame; run.Reaims = records.Count;
            void Fail(string s) { if (run.Ok) { run.Ok = false; run.Why = route + ": " + s; } }
            if (run.MinWall < wallBound) Fail("wall distance " + run.MinWall.ToString("F4", Inv));
            if (visited.Count != exp.Count) Fail("visited " + visited.Count + " tiles");
            else for (int i = 0; i < exp.Count; i++) if (visited[i] != exp[i]) { Fail("tile " + i + " differs"); break; }
            if (wing.Address != route) Fail("address " + wing.Address);
            if (records.Count != 8) Fail("reaims " + records.Count);
            foreach (var r in records) if (r.rec != ShellState.Opaque || r.view != ShellState.Opaque) Fail("reaim while not opaque");
            if (!regOk && lateral == 0) Fail("registration > 1 mm or non-adjacent corner");
            foreach (var (tile, edge) in view.IrisCalls)
            {
                if (edge < 0) continue;
                int k = exp.IndexOf(tile);
                if (k < 0 || k + 1 >= exp.Count || edge != g.EdgeTo(tile, exp[k + 1])) { Fail("iris edge " + edge); break; }
            }
            return run;
        }

        // Acceptance P2-B03 (MP §10.2.8): "100 simulated 8-step routes in 3 RoomModels: head always >= 0.4 m from walls;
        // rotation applied only while opaque".
        [Test]
        public void Valve_100RoutesIn3Rooms_HeadAtLeast0p4mFromWalls_RotationOnlyWhileOpaque()
        {
            var routes = SimRoutes.Hundred();
            var json = new StringBuilder("{\n  \"task\": \"P2-B03\",\n  \"criterion\": \"100 routes x 3 rooms: head >= 0.4 m from walls; rotation only while opaque\",\n  \"rooms\": [\n");
            int pass = 0; var failures = new List<string>();
            for (int ri = 0; ri < Rooms.Length; ri++)
            {
                var room = Room(Rooms[ri]);
                double minWall = double.PositiveInfinity; long frames = 0; int reaims = 0, ok = 0;
                foreach (var route in routes)
                {
                    var r = RunRoute(room, route);
                    minWall = Math.Min(minWall, r.MinWall); frames += r.Frames; reaims += r.Reaims;
                    if (r.Ok) { ok++; pass++; } else if (failures.Count < 5) failures.Add(Rooms[ri] + " " + r.Why);
                }
                json.Append($"    {{\"room\": \"{Rooms[ri]}\", \"routes_ok\": {ok}, \"min_wall_distance_m\": {minWall.ToString("F4", Inv)}, \"reaims\": {reaims}, \"frames\": {frames}}}");
                json.Append(ri < Rooms.Length - 1 ? ",\n" : "\n");
            }
            json.Append("  ],\n  \"routes_passing\": " + pass + ",\n  \"routes_total\": 300\n}\n");
            Lung.Tests.LungTestEnv.WriteEvidence("P2-B03", "valve_sim.json", json.ToString());
            Assert.AreEqual(300, pass, "routes passing of 300 (100 per room): " + string.Join("; ", failures));
        }

        // S3.4 supporting (MP §10.2.3 strip 0.2 m wide): lateral wobble of 0.1 m keeps the head >= 0.40 m from walls.
        [Test]
        public void Valve_StripWobble_HeadAtLeast0p4m()
        {
            var room = Room(RoomFixtures.Sample3x3);
            int ok = 0; string why = "";
            foreach (var route in SimRoutes.Hundred())
            {
                var r = RunRoute(room, route, lateral: 0.1, wallBound: 0.40 - 1e-9);
                if (r.Ok) ok++; else if (why == "") why = r.Why;
            }
            Assert.AreEqual(100, ok, "wobble routes ok of 100: " + why);
        }

        struct Case { public AirwayWing Wing; public FakeChamberView View; public FakeClearance Clear; public SimWalker Walker; public List<ReaimRecord> Reaims; public int Corner; public int OtherCorner; }

        // Head at K_i, root chamber at K_i, walker on the glyph (G6 §4.1 T3.2/T3.3).
        static Case MakeCase(RoomModel room, int i, Action<FakeClearance, LapCorners> stamp, int otherCorner = -1)
        {
            var lap = new LapCorners(room.LapSide);
            var clear = new FakeClearance(room); stamp(clear, lap);
            var view = new FakeChamberView();
            var wing = new AirwayWing(room, Tiles, clear, new ScriptedForkChoice("LRLR"), view);
            var c = new Case { Wing = wing, View = view, Clear = clear, Reaims = new List<ReaimRecord>(), Corner = i, OtherCorner = otherCorner };
            wing.Reaimed += r => c.Reaims.Add(r);
            double2 k = lap.Corner(i);
            c.Walker = new SimWalker(wing) { Head = new float3((float)k.x, 1.6f, (float)k.y) };
            wing.Enter();
            return c;
        }

        static void Stamp(FakeClearance clear, LapCorners lap, int i, int j, System.Random r)
        {
            double2 a = lap.Corner(i), b = lap.Corner(j), d = math.normalize(b - a), n = new double2(-d.y, d.x);
            double t = Math.Max(0.2, 0.4 / lap.Side) + r.NextDouble() * (0.8 - Math.Max(0.2, 0.4 / lap.Side));   // DV-B03-2
            double off = (r.NextDouble() * 2 - 1) * 0.1;
            clear.AddDynamic(a + t * (b - a) + off * n);
        }

        // Acceptance P2-B03 (MP §10.2.8): "one side blocked in the dynamic layer -> the other chosen, 20 of 20".
        [Test]
        public void Valve_OneSideBlockedInDynamicLayer_OtherSideChosen_20of20()
        {
            var r = new System.Random(2103); int pass = 0; var why = new List<string>();
            for (int n = 0; n < 20; n++)
            {
                var room = Room(Rooms[n % 3]);
                int i = r.Next(4), blockedNext = r.Next(2);
                int blocked = blockedNext == 1 ? (i + 1) & 3 : (i + 3) & 3, other = blockedNext == 1 ? (i + 3) & 3 : (i + 1) & 3;
                var c = MakeCase(room, i, (cl, lap) => Stamp(cl, lap, i, blocked, r), other);
                for (int f = 0; f < 10 && c.Reaims.Count == 0; f++) c.Walker.Frame_(c.Walker.HeadXZ);
                var lapc = new LapCorners(room.LapSide);
                bool ok = c.Wing.Phase == ValvePhase.Walking && c.Reaims.Count == 1 && c.Reaims[0].Shell == ShellState.Opaque
                    && math.length(c.Wing.NextChamberWorld - lapc.Corner(other)) < 1e-3;
                if (ok) pass++; else why.Add($"case {n} room {Rooms[n % 3]} i={i} blocked={blocked} phase={c.Wing.Phase} reaims={c.Reaims.Count} next={c.Wing.NextChamberWorld} cn={ValveSolver.SideClearance(c.Clear, lapc.Corner(i), lapc.Corner((i + 1) & 3))} cp={ValveSolver.SideClearance(c.Clear, lapc.Corner(i), lapc.Corner((i + 3) & 3))}");
            }
            Assert.AreEqual(20, pass, "cases passing of 20: " + string.Join("; ", why));
        }

        // Acceptance P2-B03 (MP §10.2.8): "neither side clear to 0.30 m -> the valve stays closed, 20 of 20".
        [Test]
        public void Valve_NeitherSideClearTo0p30m_ValveStaysClosed_20of20()
        {
            var r = new System.Random(2103); int pass = 0; var why = new List<string>();
            for (int n = 0; n < 20; n++)
            {
                var room = Room(Rooms[n % 3]);
                int i = r.Next(4);
                var c = MakeCase(room, i, (cl, lap) => { Stamp(cl, lap, i, (i + 1) & 3, r); Stamp(cl, lap, i, (i + 3) & 3, r); });
                for (int f = 0; f < 300; f++) c.Walker.Frame_(c.Walker.HeadXZ);
                bool irisOnlyClosed = true; foreach (var (_, e) in c.View.IrisCalls) if (e != -1) irisOnlyClosed = false;
                bool ok = c.Wing.Phase == ValvePhase.Waiting && c.Wing.Shell == ShellState.Opaque && c.Reaims.Count == 0
                    && irisOnlyClosed && c.View.PromptCalls.Count > 0 && c.View.PromptCalls[c.View.PromptCalls.Count - 1].wait;
                if (ok) pass++; else why.Add($"case {n} room {Rooms[n % 3]} i={i}");
            }
            Assert.AreEqual(20, pass, "cases passing of 20: " + string.Join("; ", why));
        }

        // S3.1, MP §10.2.3 step 3 "ties alternate".
        [Test]
        public void Valve_TiesAlternate()
        {
            var s = new ValveSolver(new LapCorners(2.0), new ConstClearance(1.0f));
            var got = new List<int>();
            for (int n = 0; n < 6; n++) got.Add(s.Solve(0).TargetCorner);
            CollectionAssert.AreEqual(new[] { 1, 3, 1, 3, 1, 3 }, got);   // Next, Prev, Next, Prev, Next, Prev
        }

        // S3.2, MP §10.2.3 step 4 + DV-G6-1/DV-B03-4: no rotation until the head stands within 0.15 m of the centre.
        [Test]
        public void Valve_WaitsForGlyph()
        {
            var room = Room(RoomFixtures.Sample3x3);
            var wing = new AirwayWing(room, Tiles, new FakeClearance(room), new ScriptedForkChoice("LRLR"), new FakeChamberView());
            int reaims = 0; wing.Reaimed += _ => reaims++;
            var w = new SimWalker(wing) { Head = new float3(-1f + 0.20f, 1.6f, -1f) };   // 0.20 m from K0
            wing.Enter();
            for (int f = 0; f < 120; f++) w.Frame_(w.HeadXZ);
            Assert.AreEqual(0, reaims, "no reaim at 0.20 m");
            double d = 0.20;
            while (d > 0.141) { d -= 0.01; w.Frame_(new double2(-1 + d, -1)); Assert.AreEqual(0, reaims, "no reaim while moving or beyond 0.15 m, d=" + d); }
            Assert.LessOrEqual(wing.CurrentChamberDistance, ValveSolver.GlyphRadius);
            w.Frame_(w.HeadXZ);
            Assert.AreEqual(1, reaims, "reaim on the first still frame within 0.15 m");
        }

        // S3.3, MP §10.2.3 step 1: entering within 0.5 m closes the valve and makes the shell opaque.
        [Test]
        public void Valve_ChamberEntryAt0p5m()
        {
            var room = Room(RoomFixtures.Sample3x3);
            var view = new FakeChamberView();
            var wing = new AirwayWing(room, Tiles, new FakeClearance(room), new ScriptedForkChoice("LRLR"), view);
            var w = new SimWalker(wing) { Head = new float3(-1f, 1.6f, -1f) };
            wing.Enter();
            for (int f = 0; f < 10; f++) w.Step();
            Assert.AreEqual(ValvePhase.Walking, wing.Phase);
            var tile0 = wing.CurrentTile;
            double prev = wing.NextChamberDistance;
            for (int f = 0; f < 1000 && wing.Phase == ValvePhase.Walking; f++)
            {
                prev = wing.NextChamberDistance;
                w.Step();
                if (wing.Phase == ValvePhase.Walking) Assert.Greater(wing.NextChamberDistance, 0.5, "still walking inside 0.5 m");
            }
            Assert.AreNotEqual(ValvePhase.Walking, wing.Phase, "0.49 m from the next centre");
            Assert.Greater(prev, 0.5);
            Assert.LessOrEqual(wing.CurrentChamberDistance, 0.5 + 1e-9);
            Assert.AreEqual(ShellState.Opaque, wing.Shell);
            Assert.AreNotEqual(tile0, wing.CurrentTile);
            Assert.AreEqual(wing.CurrentTile, view.IrisCalls[view.IrisCalls.Count - 1].tile);
            Assert.AreEqual(-1, view.IrisCalls[view.IrisCalls.Count - 1].edge);
        }

        // S3.5, MP §10.2.3 step 3: the valve re-solves each frame and reopens when the way clears.
        [Test]
        public void Valve_ReopensWhenCleared()
        {
            var room = Room(RoomFixtures.Sample3x3);
            var c = MakeCase(room, 0, (cl, lap) => { Stamp(cl, lap, 0, 1, new System.Random(1)); Stamp(cl, lap, 0, 3, new System.Random(2)); });
            for (int f = 0; f < 30; f++) c.Walker.Frame_(c.Walker.HeadXZ);
            Assert.AreEqual(ValvePhase.Waiting, c.Wing.Phase);
            c.Clear.ClearDynamic();
            c.Walker.Frame_(c.Walker.HeadXZ);
            Assert.AreEqual(ValvePhase.Walking, c.Wing.Phase);
            Assert.AreEqual(1, c.Reaims.Count);
        }

        // S3.6, MP §10.2.3 "Walkable depth" 16: valves are forward only; generation 16 is a dead end (DV-G6-13).
        [Test]
        public void Valve_DeadEndAtGeneration16()
        {
            var room = Room(RoomFixtures.Sample3x3);
            string route = "LRLRLRLRLRLRLRLR";
            var tiles = new FakeForkTiles();
            var wing = new AirwayWing(room, tiles, new FakeClearance(room), new ScriptedForkChoice(route), new FakeChamberView());
            int reaims = 0; wing.Reaimed += _ => reaims++;
            var w = new SimWalker(wing) { Speed = 1.4 };
            wing.Enter();
            for (int i = 0; i < 200000 && wing.Phase != ValvePhase.DeadEnd; i++) w.Step();
            Assert.AreEqual(ValvePhase.DeadEnd, wing.Phase);
            Assert.AreEqual(route, wing.Address);
            int before = reaims;
            for (int f = 0; f < 300; f++) w.Step();
            Assert.AreEqual(ValvePhase.DeadEnd, wing.Phase);
            Assert.AreEqual(before, reaims, "no reaim at the dead end");
            Assert.AreEqual(ShellState.Opaque, wing.Shell);
        }

        // S3.7, MP §10.2.6 "fork {address, choice, correct, beacon_shown, t_s}"; MP §10.2.3 step 2.
        [Test]
        public void ForkEvent_Fields()
        {
            var room = Room(RoomFixtures.Sample3x3);
            string target = Tiles.TargetAddress; string route = target.Substring(0, 4);
            var good = RunForks(room, route, null);
            Assert.AreEqual(4, good.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual("lung", good[i].scene);
                Assert.AreEqual(route.Substring(0, i), good[i].d.address);
                Assert.AreEqual(route[i].ToString(), good[i].d.choice);
                Assert.IsTrue(good[i].d.correct, "route choice " + i);
                Assert.IsFalse(good[i].d.beacon_shown);
                Assert.GreaterOrEqual(good[i].d.t_s, 0f);
            }
            char wrong = route[0] == 'L' ? 'R' : 'L';
            var bad = RunForks(room, route, wrong.ToString());
            Assert.IsFalse(bad[0].d.correct, "deliberate wrong choice");
            Assert.AreEqual(wrong.ToString(), bad[0].d.choice);
        }

        static List<(string scene, ForkEventData d)> RunForks(RoomModel room, string route, string overrideFirst)
        {
            var list = new List<(string, ForkEventData)>();
            using (var capture = new Lung.Tests.EventCapture())
            {
                capture.Log.Logged += m => { if (m.Ev == "fork") list.Add((m.Scene, ForkEventData.FromJson(m.Data))); };
                var choice = new ScriptedForkChoice(route) { Override = overrideFirst };
                var wing = new AirwayWing(room, Tiles, new FakeClearance(room), choice, new FakeChamberView());
                var w = new SimWalker(wing); wing.Enter();
                int need = overrideFirst != null ? 1 : route.Length;
                for (int i = 0; i < 40000 && list.Count < need; i++) w.Step();
            }
            return list;
        }
    }
}
