using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Atlas.Blades.Lung.Tests;
using Atlas.Blades.Lung.Walking;
using Atlas.Kernel;
using Atlas.Safety;
using Atlas.Spaces.AlignedCells;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Random = System.Random;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // P2-B04 Valve walk, Mode 2 (MP §10.2.3 Mode 2; Build Graph row P2-B04). Each test names the criterion it checks.
    public class ValveWalkMode2Tests
    {
        // Static SDF = distance to the free rectangle boundary (inside positive) + dynamic points (blocked cell centres).
        sealed class Clear : IClearanceQuery
        {
            readonly double w, d; public readonly List<double2> Dyn = new List<double2>();
            public Clear(double w, double d) { this.w = w; this.d = d; }
            public float Clearance(float2 p)
            {
                double c = Math.Min(Math.Min(w / 2 - Math.Abs(p.x), d / 2 - Math.Abs(p.y)), 1e9);
                foreach (var q in Dyn) c = Math.Min(c, math.length(new double2(p.x, p.y) - q));
                return (float)c;
            }
        }

        // Test-only 100 routes of 4 letters (G6 brief 4: System.Random(2101); same list for B03, B04 and B05).
        static List<string> Hundred()
        {
            var r = new Random(2101); var l = new List<string>();
            for (int i = 0; i < 100; i++) { var c = new char[4]; for (int k = 0; k < 4; k++) c[k] = r.Next(2) == 0 ? 'L' : 'R'; l.Add(new string(c)); }
            return l;
        }

        // The 9 route tiles [fork(""), bend(r1), fork(r1), ..., fork(r1..r4)] (MP §10.2.3: each tree step is a tile step).
        static List<TileId> RouteTiles(AirwayTree tree, string route)
        {
            var t = new List<TileId> { tree.FrameOf("").Tile };
            for (int i = 1; i <= route.Length; i++)
            {
                string a = route.Substring(0, i);
                t.Add(tree.BendFrameOf(a).Tile); t.Add(tree.FrameOf(a).Tile);
            }
            return t;
        }

        static int EdgeTo(TileGraph g, TileId from, TileId to)
        {
            for (int k = 0; k < 4; k++) if (g.Neighbour(from, k).Equals(to)) return k;
            return -1;
        }

        struct Grid { public string Name; public double W, D; public int2[] Blocked; public int2 Start; }

        static Grid[] Grids()
        {
            var all3 = new List<int2>();
            for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) if (!((x == 1 || x == 2) && z == 1)) all3.Add(new int2(x, z));
            return new[]
            {
                new Grid { Name = "room_3x3", W = 2.4, D = 2.4, Blocked = new int2[0], Start = new int2(1, 1) },
                new Grid { Name = "room_3x3_b", W = 2.4, D = 2.5, Blocked = new int2[0], Start = new int2(0, 0) },
                new Grid { Name = "corner_demo_2x2", W = 1.5, D = 1.5, Blocked = new int2[0], Start = new int2(0, 1) },
                new Grid { Name = "1x2_in_3x3", W = 2.4, D = 2.4, Blocked = all3.ToArray(), Start = new int2(1, 1) },
                new Grid { Name = "corner_demo_1x2", W = 1.5, D = 1.5, Blocked = new[] { new int2(0, 0), new int2(1, 0) }, Start = new int2(0, 1) },
            };
        }

        sealed class Result { public int Routes, Completed, Valves; public double MinFreeCells; }

        // Runs one route in Mode 2. Returns null on success else a failure text.
        static string Run(AirwayTree tree, TileGraph graph, CellGrid cg, Clear clr, int2 start, string route, ref int valves)
        {
            var tiles = RouteTiles(tree, route);
            var w = new CellValveWalk(cg, graph, clr);
            double2 head = cg.CellCentre(start);
            Assert.IsTrue(w.Valve.Free(start), "start cell free");
            w.Enter(head, tiles[0]);
            var visited = new List<TileId> { tiles[0] };
            for (int i = 0; i < tiles.Count - 1; i++)
            {
                int exit = EdgeTo(graph, tiles[i], tiles[i + 1]);
                if (exit < 0) return "no exit edge " + i;
                if (!w.TryOpen(head, exit, out var t)) return "valve closed at chamber " + i;
                valves++;
                Assert.AreEqual(exit, w.Cells.Frame.TileEdge(t.Dir), "chosen tree edge faces the picked direction");
                Assert.IsTrue(w.Valve.Free(w.TargetCell), "target cell free");
                double2 goal = cg.CellCentre(w.TargetCell);
                int guard = 0;
                while (!w.InChamber(head))
                {
                    double2 dv = goal - head; double len = math.length(dv);
                    head += dv / len * Math.Min(0.015, len);
                    w.Walk(head);
                    if (!w.Valve.Free(w.Cells.Cell)) return "entered non-free cell " + w.Cells.Cell;
                    if (++guard > 2000) return "walker did not arrive";
                }
                if (!w.Cells.Frame.Tile.Equals(tiles[i + 1])) return "tile mismatch at " + (i + 1);
                visited.Add(w.Cells.Frame.Tile);
            }
            return visited.Count == 9 ? null : "tile count " + visited.Count;
        }

        // Acceptance (P2-B04): "Same 100 routes complete with >= 1x2 cells"; Output: 90 degree frame rotation per cell.
        [Test]
        public void Mode2_Same100RoutesComplete_WithAtLeast1x2Cells()
        {
            var tree = TreeFixture.Tree; var graph = new TileGraph(5);
            var routes = Hundred(); var sb = new StringBuilder("{\n  \"grids\": [\n");
            bool first = true;
            foreach (var g in Grids())
            {
                var cg = CellGrid.FromFreeRectangle(new FreeRectangle(g.W, g.D));
                var clr = new Clear(g.W, g.D);
                foreach (var b in g.Blocked) clr.Dyn.Add(cg.CellCentre(b));
                int free = 0;
                var v = new CellValve(cg, clr);
                for (int x = 0; x < cg.CountX; x++) for (int z = 0; z < cg.CountZ; z++) if (v.Free(new int2(x, z))) free++;
                Assert.GreaterOrEqual(free, 2, g.Name + ": at least 1x2 free cells");
                int valves = 0, done = 0;
                foreach (var r in routes)
                {
                    string fail = Run(tree, graph, cg, clr, g.Start, r, ref valves);
                    Assert.IsNull(fail, g.Name + " route " + r + ": " + fail);
                    done++;
                }
                Assert.AreEqual(100, done); Assert.AreEqual(800, valves, g.Name + " 8 valves per route");
                sb.Append(first ? "" : ",\n").Append(string.Format(CultureInfo.InvariantCulture,
                    "    {{\"name\":\"{0}\",\"cells\":\"{1}x{2}\",\"side_m\":{3:0.####},\"free_cells\":{4},\"routes\":{5},\"completed\":{6},\"valves\":{7}}}",
                    g.Name, cg.CountX, cg.CountZ, cg.Side, free, routes.Count, done, valves));
                first = false;
            }
            sb.Append("\n  ]\n}\n");
            WriteEvidence("mode2_sim.json", sb.ToString());
        }

        // S4.1: longest clear run chosen (budget room 3x4 grid at 0.75 m, from corner cell (0,0): +x = 2, +z = 3 -> +z); ties -> lowest dir.
        [Test]
        public void Mode2_LongestClearRunChosen()
        {
            var cg = CellGrid.FromFreeRectangle(new FreeRectangle(2.25, 3.0));
            Assert.AreEqual(3, cg.CountX); Assert.AreEqual(4, cg.CountZ);
            var v = new CellValve(cg, new Clear(2.25, 3.0));
            Assert.AreEqual(2, v.ClearRun(new int2(0, 0), 0));
            Assert.AreEqual(3, v.ClearRun(new int2(0, 0), 1));
            Assert.IsTrue(v.TryPick(new int2(0, 0), out int dir)); Assert.AreEqual(1, dir);
            var clr = new Clear(2.25, 3.0); clr.Dyn.Add(cg.CellCentre(new int2(0, 3)));   // +z run 2, +x run 2: tie
            var v2 = new CellValve(cg, clr);
            Assert.IsTrue(v2.TryPick(new int2(0, 0), out dir)); Assert.AreEqual(0, dir, "ties -> lowest direction");
        }

        // S4.2: no free neighbour -> valve stays closed (Waiting, Opaque, no reaim) for 300 frames.
        [Test]
        public void Mode2_SingleFreeCell_StaysClosed()
        {
            var cg = CellGrid.FromFreeRectangle(new FreeRectangle(2.4, 2.4));
            var clr = new Clear(2.4, 2.4);
            for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) if (x != 1 || z != 1) clr.Dyn.Add(cg.CellCentre(new int2(x, z)));
            var w = new CellValveWalk(cg, new TileGraph(5), clr);
            double2 head = cg.CellCentre(new int2(1, 1));
            var tile = TreeFixture.Tree.FrameOf("").Tile;
            w.Enter(head, tile);
            for (int f = 0; f < 300; f++)
            {
                Assert.IsFalse(w.TryOpen(head, 0, out _));
                Assert.AreEqual(0, w.Cells.Frame.Turn); Assert.AreEqual(tile, w.Cells.Frame.Tile);
            }
        }

        static void WriteEvidence(string name, string json) => Lung.Tests.LungTestEnv.WriteEvidence("P2-B04", name, json);
    }
}
