using System.IO;
using System.Text;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using UnityEngine;
#endif

namespace Atlas.Spaces.AlignedCells.Tests
{
    // S3 duck maze design (2026-10-04) §3, §4, §5: the walls as kerbs, the dark tiles off the maze, the duck in every view of
    // its tile, and the read-out.
    public class DuckMazeViewTests
    {
        static CellGrid Grid4() => CellGrid.FromAligned(new AlignedGrid { N = 4, M = 4, G = 0.6 }, true);

        static readonly float3[] s_Room = { new float3(1, 0, 0), new float3(0, 0, 1), new float3(-1, 0, 0), new float3(0, 0, -1) };
        static readonly int[] s_DuckCells = { AtlasPalette.DuckYellow, AtlasPalette.DuckOrange, AtlasPalette.DuckWhite, AtlasPalette.DuckBlack };

        // The instance's rotation, Quaternion.Euler(0, -90 * turn, 0), in Unity.Mathematics (same sense: +y turns +z towards +x).
        static quaternion Turn(int turn) => quaternion.RotateY(math.radians(-90f * turn));

        // TileMeshGenerator, flat cell (kappa 0): the kerb of mesh edge e stands on RotE((h, u), e), placed at (x, z) = Klein
        // (x, y). So before rotation mesh edge 0 is the +x side, 1 the +z side, 2 the -x side, 3 the -z side: mesh edge e faces
        // local room direction e. The rotation by -90 deg * turn takes local direction j to room direction (j + turn) & 3, which
        // is the CellFrame rule for tile edge j: the mesh edge of tile edge k is k.
        [Test]
        public void InstanceKerbMask_FacesTheRightRoomSide()
        {
            // The rotation's sense, with explicit vectors: turn 1 takes +x to +z and +z to -x.
            float3 x1 = math.mul(Turn(1), s_Room[0]), z1 = math.mul(Turn(1), s_Room[1]);
            Assert.That(math.distance(x1, s_Room[1]), Is.LessThan(1e-6f), "+x -> +z, got " + x1);
            Assert.That(math.distance(z1, s_Room[2]), Is.LessThan(1e-6f), "+z -> -x, got " + z1);

            var gen = new TileMeshGenerator(TilePalette.Default);
            for (int mask = 0; mask < 16; mask++)
                for (int turn = 0; turn < 4; turn++)
                {
                    int meshMask = AlignedCellsView.InstanceKerbMask(mask, turn);
                    Assert.That(meshMask, Is.InRange(0, 15));
                    TileMeshLayout lay = gen.Build(TileMeshSpec.FlatCell(0.6, meshMask, 0));
                    int faced = 0;
                    for (int e = 0; e < 4; e++)
                    {
                        UnityEngine.RangeInt v = lay.KerbVertices(e);
                        if (v.length == 0) continue;
                        float3 sum = float3.zero;
                        for (int i = v.start; i < v.start + v.length; i++) sum += gen.Vertices[i].Position;
                        float3 mid = math.mul(Turn(turn), sum / v.length);
                        int d = 0;
                        for (int k = 1; k < 4; k++) if (math.dot(mid, s_Room[k]) > math.dot(mid, s_Room[d])) d = k;
                        Assert.That(math.dot(mid, s_Room[d]), Is.GreaterThan(0.2f), "a kerb stands on a cell side");
                        faced |= 1 << d;
                    }
                    int expected = 0;
                    for (int k = 0; k < 4; k++) if ((mask >> k & 1) != 0) expected |= 1 << ((k + turn) & 3);
                    Assert.That(faced, Is.EqualTo(expected), "tile wall mask " + mask + ", turn " + turn);
                }
        }

        [Test]
        public void DuckMesh_FallsBackWhenTheKitIsMissing()
        {
            foreach (KitSet kit in new[] { null, KitSet.Load(_ => null) })
            {
                AtlasMeshBuilder duck = DuckMesh.Build(kit);
                Assert.That(duck.TriangleCount, Is.InRange(1, 1000));
                for (int v = 0; v < duck.VertexCount; v++)
                {
                    bool onDuck = false;
                    foreach (int c in s_DuckCells) onDuck |= math.all(math.abs(AtlasPalette.Uv(c) - duck.Vertices[v].Uv0) < 1e-5f);
                    Assert.That(onDuck, Is.True, "vertex " + v + " is on a duck cell");
                }
                Assert.That(duck.BoundsMin.y, Is.EqualTo(0f).Within(1e-4f), "stands on the floor");
                Assert.That(duck.BoundsMax.y - duck.BoundsMin.y, Is.InRange(0.25f, 0.35f), "about 0.3 m tall");
            }
            var into = new AtlasMeshBuilder();
            Assert.That(DuckMesh.Build(KitSet.Load(_ => null), into, out string error), Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(DuckMesh.Build(null, into, out error), Is.False);
            Assert.That(error, Is.Null, "no kit asked for: nothing to report");
        }

        [Test]
        public void DuckMesh_UsesTheKitPiece()
        {
            string dir = Path.Combine(Fixtures.ProjectDir, "Assets", "Atlas", "Spaces", "Glued", "Resources", KitSet.ResourceFolder);
            KitSet kit = KitSet.Load(name =>
            {
                string path = Path.Combine(dir, name == KitSet.ManifestName ? "manifest.json" : name + ".bytes");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            });
            var into = new AtlasMeshBuilder();
            Assert.That(DuckMesh.Build(kit, into, out string error), Is.True, error);
            Assert.That(into.TriangleCount, Is.InRange(1001, 2500), "the modelled duck, not the procedural one");
        }

        [Test]
        public void DuckMaze_AnimationCurves()
        {
            Assert.That(AlignedCellsView.DuckSpin(0, false, 0), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(AlignedCellsView.DuckSpin(2, false, 0), Is.EqualTo(80f).Within(1e-3f));
            Assert.That(AlignedCellsView.DuckSpin(10, false, 0), Is.EqualTo(40f).Within(1e-3f), "wrapped to 0..360");
            // After the find: 240 deg/s for 2 s, continuous at both ends, then 40 deg/s again.
            Assert.That(AlignedCellsView.DuckSpin(5, true, 5), Is.EqualTo(200f).Within(1e-3f));
            Assert.That(AlignedCellsView.DuckSpin(6, true, 5), Is.EqualTo((240f + 200f) % 360f).Within(1e-3f));
            Assert.That(AlignedCellsView.DuckSpin(8, true, 5), Is.EqualTo((320f + 400f) % 360f).Within(1e-3f));
            Assert.That(AlignedCellsView.DuckSpin(1e6, false, 0), Is.InRange(0f, 360f), "a long run stays exact enough");

            Assert.That(AlignedCellsView.DuckBob(0), Is.EqualTo(0.03f).Within(1e-6f));
            Assert.That(AlignedCellsView.DuckBob(0.4), Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(AlignedCellsView.DuckBob(1.2), Is.EqualTo(0.01f).Within(1e-6f));

            Assert.That(AlignedCellsView.DuckScale(3, false, 0), Is.EqualTo(1f));
            Assert.That(AlignedCellsView.DuckScale(5, true, 5), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AlignedCellsView.DuckScale(5.15, true, 5), Is.GreaterThan(1.2f), "ease-out: past half way at half time");
            Assert.That(AlignedCellsView.DuckScale(5.3, true, 5), Is.EqualTo(1.4f).Within(1e-6f));
            Assert.That(AlignedCellsView.DuckScale(9, true, 5), Is.EqualTo(1.4f).Within(1e-6f));
        }

        [Test]
        public void DuckMaze_HudText()
        {
            var sb = new StringBuilder();
            MazeHud.Format(sb, 65.9, 7, 3, false);
            Assert.That(sb.ToString(), Is.EqualTo("Find the duck  1:05  walls 3"));
            MazeHud.Format(sb, 0, 0, 0, false);
            Assert.That(sb.ToString(), Is.EqualTo("Find the duck  0:00  walls 0"));
            MazeHud.Format(sb, 47.2, 12, 2, true);
            Assert.That(sb.ToString(), Is.EqualTo("Found the duck!  12 steps, 2 walls crossed, 0:47"));
            MazeHud.Format(sb, 725, 105, 10, true);
            Assert.That(sb.ToString(), Is.EqualTo("Found the duck!  105 steps, 10 walls crossed, 12:05"));
            MazeHud.Format(sb, -3, 0, 0, false);
            Assert.That(sb.ToString(), Is.EqualTo("Find the duck  0:00  walls 0"));
        }

        [Test]
        public void DuckMaze_HudWithoutARootRunsOn()
        {
            var hud = new MazeHud(null);
            hud.Show(12.5, 3, 1, false);
            hud.Show(13.5, 4, 1, true);
            Assert.That(hud.Visible, Is.True);
            Assert.That(hud.Text.ToString(), Is.EqualTo("Found the duck!  4 steps, 1 walls crossed, 0:13"));
            hud.Hide();
            Assert.That(hud.Visible, Is.False);
            hud.Dispose();
            hud.Dispose();
            // After Dispose, Show and Hide do nothing.
            hud.Show(20, 9, 2, false);
            Assert.That(hud.Visible, Is.False);
            Assert.That(hud.Text.ToString(), Is.EqualTo("Found the duck!  4 steps, 1 walls crossed, 0:13"));
            hud.Hide();
        }

#if !ATLAS_HEADLESS
        // A maze and a walker place from which the duck's tile is seen in as many instances as can be found (round a post
        // there can be several), with the instances.
        static DuckMaze MazeWithDuckInView(CellGrid grid, TileGraph graph, CellInstanceList list, out int duckViews)
        {
            DuckMaze best = null; int2 bestCell = default; CellFrame bestFrame = default; int bestViews = 0;
            for (int seed = 1; seed <= 40 && bestViews < 2; seed++)
            {
                DuckMaze maze = DuckMaze.Generate(grid, graph, new int2(seed % 4, (seed / 4) % 4), seed);
                for (int i = 0; i < maze.TileCount && bestViews < 2; i++)
                {
                    maze.TryPlace(maze.Tile(i), out int2 cell, out CellFrame frame);
                    SightLines.Build(grid, graph, cell, frame, list);
                    int views = 0;
                    for (int j = 0; j < list.Count; j++) if (list[j].Frame.Tile.Equals(maze.DuckTile)) views++;
                    if (views > bestViews) { best = maze; bestCell = cell; bestFrame = frame; bestViews = views; }
                }
            }
            SightLines.Build(grid, graph, bestCell, bestFrame, list);
            duckViews = bestViews;
            return best;
        }

        static void AssertGates(in CellInstance inst, Material mat)
        {
            var gateIds = new[] { AtlasShaderIDs.Gate0, AtlasShaderIDs.Gate1, AtlasShaderIDs.Gate2, AtlasShaderIDs.Gate3 };
            for (int row = 0; row < 4; row++)
            {
                double4 g = row < inst.GateCount ? inst.Gate(row) : double4.zero;
                Vector4 v = mat.GetVector(gateIds[row]);
                Assert.AreEqual(g.x, v.x, 1e-5); Assert.AreEqual(g.y, v.y, 1e-5); Assert.AreEqual(g.z, v.z, 1e-5);
            }
        }

        [Test, Category("Unity")]
        public void Kerbs_AreBuiltForEveryMask()
        {
            // The instance rotation of the view agrees with the one the kerb rule was derived for.
            for (int turn = 0; turn < 4; turn++)
            {
                Vector3 a = Quaternion.Euler(0, -90f * turn, 0) * Vector3.right;
                float3 b = math.mul(Turn(turn), s_Room[0]);
                Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-5f));
            }
            var root = new GameObject("DuckMazeKerbs");
            MaterialPool pool = MaterialPool.CreateDefault(64);
            var view = new AlignedCellsView(root.transform, pool, Grid4());
            try
            {
                Assert.AreEqual(16, AlignedCellsView.CellMeshCount);
                int t0 = (int)view.CellMesh(0).GetIndexCount(0) / 3;
                Assert.AreEqual(view.CellTriangles, t0);
                for (int m = 1; m < 16; m++)
                {
                    Assert.IsNotNull(view.CellMesh(m));
                    Assert.Greater((int)view.CellMesh(m).GetIndexCount(0) / 3, t0, "mask " + m);
                }
                Assert.Greater((int)view.CellMesh(15).GetIndexCount(0) / 3, (int)view.CellMesh(1).GetIndexCount(0) / 3);
            }
            finally
            {
                view.Dispose(); pool.Dispose(); Object.DestroyImmediate(root);
            }
        }

        [Test, Category("Unity")]
        public void Duck_DrawnOnlyWhereItsTileIs()
        {
            var root = new GameObject("DuckMazeDucks");
            root.transform.position = new Vector3(0.3f, 0f, -0.2f);
            MaterialPool pool = MaterialPool.CreateDefault(128);
            CellGrid grid = Grid4();
            var graph = new TileGraph(5);
            var list = new CellInstanceList();
            var view = new AlignedCellsView(root.transform, pool, grid);
            try
            {
                DuckMaze maze = MazeWithDuckInView(grid, graph, list, out int k);
                Assert.That(k, Is.GreaterThanOrEqualTo(1));
                TestContext.WriteLine("duck seen in " + k + " instances of " + list.Count);
                view.Apply(list, maze);
                Assert.AreEqual(k, view.DuckViewCount);
                int d = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    CellInstance inst = list[i];
                    // Every cell takes the mesh of its tile's walls.
                    Assert.AreSame(view.CellMesh(AlignedCellsView.InstanceKerbMask(maze.WallMask(inst.Frame.Tile), inst.Frame.Turn)), view.InstanceMesh(i));
                    if (!inst.Frame.Tile.Equals(maze.DuckTile)) continue;
                    Transform duck = view.DuckTransform(d);
                    Assert.IsTrue(duck.gameObject.activeSelf);
                    double2 c = grid.CellCentre(inst.Cell);
                    Assert.AreEqual(c.x, duck.localPosition.x, 1e-5); Assert.AreEqual(c.y, duck.localPosition.z, 1e-5);
                    Assert.IsTrue(pool.Owns(view.DuckMaterial(d)));
                    // Gates in world coordinates, as the cell's.
                    var worldInst = inst;
                    for (int g = 0; g < inst.GateCount; g++)
                    {
                        Vector3 w = root.transform.TransformPoint((float)inst.Gate(g).x, 0, (float)inst.Gate(g).y);
                        double4 gg = new double4(w.x, w.z, inst.Gate(g).z, 0);
                        if (g == 0) worldInst.Gate0 = gg; else if (g == 1) worldInst.Gate1 = gg; else if (g == 2) worldInst.Gate2 = gg; else worldInst.Gate3 = gg;
                    }
                    AssertGates(worldInst, view.DuckMaterial(d));
                    AssertGates(worldInst, view.InstanceMaterial(i));
                    d++;
                }
                Assert.AreEqual(k, d);
                for (int i = k; i < AlignedCellsView.MaxDuckViews; i++)
                    if (view.DuckTransform(i) != null) Assert.IsFalse(view.DuckTransform(i).gameObject.activeSelf);

                // Animate moves the ducks only: up and down, round, and bigger after the find.
                view.Animate(0.4, false, 0);
                Assert.AreEqual(AlignedCellsView.DuckBob(0.4), view.DuckTransform(0).localPosition.y, 1e-6);
                view.Animate(10.3, true, 10);
                Assert.AreEqual(1.4f, view.DuckTransform(0).localScale.x, 1e-5);

                int rented = pool.Rented;
                view.Apply(list, null);
                Assert.AreEqual(0, view.DuckViewCount);
                for (int i = 0; i < AlignedCellsView.MaxDuckViews; i++)
                    if (view.DuckTransform(i) != null) Assert.IsFalse(view.DuckTransform(i).gameObject.activeSelf);
                for (int i = 0; i < list.Count; i++) Assert.AreSame(view.CellMesh(0), view.InstanceMesh(i), "no maze, no kerbs");
                Assert.AreEqual(rented, pool.Rented, "the ducks keep their materials while hidden");
                view.Dispose();
                Assert.AreEqual(0, pool.Rented, "disposal gives every material back");
            }
            finally
            {
                view.Dispose(); pool.Dispose(); Object.DestroyImmediate(root);
            }
        }

        [Test, Category("Unity")]
        public void OffMazeTiles_AreVoid()
        {
            var root = new GameObject("DuckMazeVoid");
            MaterialPool pool = MaterialPool.CreateDefault(128);
            CellGrid grid = Grid4();
            var graph = new TileGraph(5);
            var list = new CellInstanceList();
            var view = new AlignedCellsView(root.transform, pool, grid);
            try
            {
                DuckMaze maze = DuckMaze.Generate(grid, graph, new int2(1, 1), 7);
                bool sawOff = false, sawOn = false;
                for (int t = 0; t < maze.TileCount && !(sawOff && sawOn); t++)
                {
                    maze.TryPlace(maze.Tile(t), out int2 cell, out CellFrame frame);
                    SightLines.Build(grid, graph, cell, frame, list);
                    view.Apply(list, maze);
                    for (int i = 0; i < list.Count; i++)
                    {
                        TileId tile = list[i].Frame.Tile;
                        float3 want = maze.Contains(tile) ? WorldRegionTint.Linear(Regions.RegionOf(tile)) : AtlasPalette.Linear(AtlasPalette.Void);
                        Vector4 tint = view.InstanceMaterial(i).GetVector(AtlasShaderIDs.BaseTint);
                        Assert.AreEqual(want.x, tint.x, 1e-6); Assert.AreEqual(want.y, tint.y, 1e-6); Assert.AreEqual(want.z, tint.z, 1e-6);
                        if (maze.Contains(tile)) sawOn = true; else sawOff = true;
                    }
                }
                Assert.IsTrue(sawOff, "an off tile was in view");
                Assert.IsTrue(sawOn);
                // Without a maze the off tiles take their region tint again.
                view.Apply(list, null);
                for (int i = 0; i < list.Count; i++)
                {
                    float3 want = WorldRegionTint.Linear(Regions.RegionOf(list[i].Frame.Tile));
                    Assert.AreEqual(want.x, view.InstanceMaterial(i).GetVector(AtlasShaderIDs.BaseTint).x, 1e-6);
                }
            }
            finally
            {
                view.Dispose(); pool.Dispose(); Object.DestroyImmediate(root);
            }
        }
#endif
    }
}
