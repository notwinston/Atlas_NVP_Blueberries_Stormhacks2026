using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Atlas.World.Lint;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.WorldGen.Tests
{
    // P1-B07: the conditions of MP §11.1 WorldLint_All that look beyond the generator: the meshes that are drawn (§8.4), the
    // region table shared by the phone and the Station, the room's scan boxes, and the bounds of warped renderers.
    // Each check is shown passing on the real data and failing on a broken copy.
    public class WorldLintConditionsTests
    {
        static RoomFootprint Footprint(RoomModel room)
        {
            double L = CurvatureDial.Target(5, room.Curvature.L5, room.Curvature.L6, room.Curvature.R3, false).LengthScaleM;
            return RoomFootprint.FromRoomModel(room, L);
        }

        // §11.1 "the region of every tile to depth 6 (597 tiles) is the same ... on the phone and on the Station"
        [Test]
        public void RegionHash_IsTheGoldenTable()
        {
            var gold = new Dictionary<TileId, int>(); var ids = new List<TileId>();
            foreach (var line in File.ReadAllLines(WorldGeneratorTests.GoldenPath("Region_Depth6_golden.csv")))
            {
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var f = line.Split(',');
                var id = new TileId(long.Parse(f[0]), long.Parse(f[1]), long.Parse(f[2]), long.Parse(f[3]), long.Parse(f[4]), long.Parse(f[5]));
                gold[id] = int.Parse(f[6]); ids.Add(id);
            }
            Assert.AreEqual(597, ids.Count);
            Assert.AreEqual(WorldLint.RegionHashDepth6, WorldLint.RegionHash(ids, t => gold[t]), "the frozen number is the golden table's");
            foreach (uint seed in WorldGeneratorTests.Seeds)
                Assert.AreEqual(WorldLint.RegionHashDepth6, WorldLint.RegionHash(new WorldGenerator(seed, WorldGeneratorTests.R33).Ball(6)), "regions do not depend on the seed or the room");
            ids.Reverse();
            Assert.AreEqual(WorldLint.RegionHashDepth6, WorldLint.RegionHash(ids, t => gold[t]), "nor on the order the tiles were found in");
            // the check can fail: one tile in another region
            TileId moved = ids[300];
            Assert.AreNotEqual(WorldLint.RegionHashDepth6, WorldLint.RegionHash(ids, t => t == moved ? (gold[t] + 1) & 3 : gold[t]));
        }

        // §11.1 "every scan box has a generated mesh, a fallback mesh or a placeholder"
        [TestCase(RoomFixtures.Sample3x3)] [TestCase(RoomFixtures.SampleL3x4)] [TestCase(RoomFixtures.Sample4x5Table)] [TestCase(RoomFixtures.BudgetRoom)]
        public void RoomContent_EveryFixtureBoxIsCovered(string name)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            var rep = new WorldLintReport();
            WorldLint.CheckRoomContent(room, rep);
            Assert.AreEqual(room.Objects.Length, rep.RoomObjects);
            Assert.AreEqual(0, rep.RoomObjectsWithoutMesh);
            Assert.IsTrue(rep.Passed, rep.ToString());
            if (name == RoomFixtures.BudgetRoom) Assert.AreEqual(12, rep.RoomObjects);
        }

        [Test]
        public void RoomContent_DetectsABoxWithNothingToDraw()
        {
            static WorldLintReport Lint(Action<RoomObject> breakIt)
            {
                RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
                breakIt(room.Objects[3]);
                var rep = new WorldLintReport();
                WorldLint.CheckRoomContent(room, rep);
                return rep;
            }
            Assert.AreEqual(1, Lint(o => { o.Mesh = MeshState.Ready; o.AssetHash = ""; }).RoomObjectsWithoutMesh, "ready, but no asset");
            Assert.AreEqual(1, Lint(o => o.Mesh = (MeshState)9).RoomObjectsWithoutMesh, "a state that draws nothing");
            Assert.AreEqual(1, Lint(o => o.Size = new Unity.Mathematics.float3(0.5f, 0f, 0.5f)).RoomObjectsWithoutMesh, "no box to stand in");
            Assert.IsFalse(Lint(o => o.Mesh = (MeshState)9).Passed);
            Assert.AreEqual(0, Lint(o => { o.Mesh = MeshState.Ready; o.AssetHash = "abc"; }).RoomObjectsWithoutMesh);
            Assert.AreEqual(0, Lint(o => o.Mesh = MeshState.Requested).RoomObjectsWithoutMesh, "the placeholder stands while the Forge works");
            Assert.AreEqual(0, Lint(o => o.Mesh = MeshState.Placeholder).RoomObjectsWithoutMesh);
            Assert.AreEqual(0, Lint(o => o.Mesh = MeshState.BuiltIn).RoomObjectsWithoutMesh);
        }

        // The whole lint on a fixture room, with its boxes: the run a space is checked with.
        [Test, Timeout(600000)]
        public void Run_WithTheRoom_ChecksItsBoxes()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            var rep = WorldLint.Run(1u, Footprint(room), room);
            Assert.IsTrue(rep.Passed, rep.ToString());
            Assert.AreEqual(12, rep.RoomObjects);
            Assert.AreEqual(WorldLint.RegionHashDepth6, rep.RegionHash);
            StringAssert.Contains("RoomObjects=12", rep.ToString());

            room.Objects[0].Mesh = (MeshState)9;
            rep = WorldLint.Run(1u, Footprint(room), room);
            Assert.IsFalse(rep.Passed);
            Assert.IsTrue(rep.Failures.Exists(f => f.Contains("scan boxes")), rep.ToString());
        }

        // §8.4: the lint measures the meshes that are drawn, and its caps bind.
        [Test, Timeout(600000)]
        public void Props_AreMeasured_AndTheEdgeRuleBinds()
        {
            var rep = WorldLint.Run(1, WorldGeneratorTests.R33);
            Assert.IsTrue(rep.Passed, rep.ToString());
            Assert.Greater(rep.TileMeshKeys, 10);
            Assert.Greater(rep.MaxPropTriangles, 0);
            Assert.LessOrEqual(rep.MaxPropTriangles, WorldPropBudget.Tower);
            Assert.Greater(rep.MaxPropEdge, 0.1); Assert.LessOrEqual(rep.MaxPropEdge, 0.2501);

            rep = WorldLint.Run(1, WorldGeneratorTests.R33, new WorldLintSettings { MaxPropEdgeM = 0.2 });
            Assert.IsFalse(rep.Passed);
            Assert.Greater(rep.PropEdgeViolations, 0);
            Assert.IsTrue(rep.Failures.Exists(f => f.Contains("edge over")), rep.ToString());
        }

        // §3.4 "no coincident faces between neighbours", on the meshes with their props: a kerb, a door frame, a tower or half
        // a beam never lies on a face of the neighbouring tile; two kerbs on one edge do (the check can fail).
        [Test]
        public void DrawnMeshes_ShareNoFaceAcrossAnEdge()
        {
            double L = WorldGeneratorTests.R33.L;
            var g = new WorldGenerator(1, WorldGeneratorTests.R33);
            var fc = new FaceCoincidence(g.Graph, L, new TileMeshCache(L));
            var plain = WorldTileKey.Plain;
            for (int e = 0; e < 4; e++)
            {
                TileId n = g.Graph.Neighbour(TileId.Zero, e);
                int back = g.BackEdge(TileId.Zero, e);
                Assert.AreEqual(0, fc.Count(TileId.Zero, e, new WorldTileKey(Archetype.Corridor, 1 << e), n, back, plain), $"kerb on edge {e} against a plain tile");
                Assert.AreEqual(0, fc.Count(TileId.Zero, e, new WorldTileKey(Archetype.Corridor, 1 << e), n, back, new WorldTileKey(Archetype.Tower, 0)), "kerb against a tower tile");
                Assert.AreEqual(0, fc.Count(TileId.Zero, e, new WorldTileKey(Archetype.Corridor, 1 << e), n, back, new WorldTileKey(Archetype.Garden, 0, -1, e)), "kerb against a garden");
                Assert.AreEqual(0, fc.Count(TileId.Zero, e, new WorldTileKey(Archetype.Doorway, 0, e), n, back, plain), "door frame against a plain tile");
                Assert.AreEqual(0, fc.Count(TileId.Zero, e, new WorldTileKey(Archetype.SpanA, 0, e), n, back, new WorldTileKey(Archetype.SpanB, 0, back)), "the two halves of a beam meet end to end");
                Assert.Greater(fc.Count(TileId.Zero, e, new WorldTileKey(Archetype.Corridor, 1 << e), n, back, new WorldTileKey(Archetype.Corridor, 1 << back)), 0, "two kerbs on one edge coincide");
            }
        }

        // §8.3, §11.1 "enlarged localBounds on every warped renderer": the rule itself, engine-free.
        [Test]
        public void WarpedBounds_Rule()
        {
            Assert.IsTrue(WorldLint.BoundsEnlarged(new Vector3(AtlasWarpBounds.Size, AtlasWarpBounds.Size, AtlasWarpBounds.Size)));
            Assert.IsFalse(WorldLint.BoundsEnlarged(new Vector3(2f, 2.6f, 2f)), "a tile's own bounds are not enough");
            Assert.IsFalse(WorldLint.BoundsEnlarged(new Vector3(AtlasWarpBounds.Size, 2.6f, AtlasWarpBounds.Size)));
        }

#if !ATLAS_HEADLESS
        // The same on live renderers: the generated world's tiles pass; one with its bounds put back fails.
        [Test, Category("Unity")]
        public void WarpedBounds_OnTheWorldRenderer()
        {
            const double L = 1.885;
            var pool = MaterialPool.CreateDefault(TileSelection.MaxDrawn);
            var go = new GameObject("WorldLintBoundsTest") { hideFlags = HideFlags.DontSave };
            try
            {
                var world = new WorldGenerator(1, WorldGeneratorTests.R33);
                var r = go.AddComponent<WorldContentRenderer>();
                r.Configure(world, L, pool);
                var selection = new TileSelection(world.Graph, L);
                var cone = TileViewCone.FromHead(new Unity.Mathematics.float3(0f, 1.6f, 0f), Unity.Mathematics.quaternion.identity, TileDrawRule.DefaultHalfSideDeg, TileDrawRule.DefaultHalfUpDownDeg);
                int n = selection.Update(new WalkerPose { Tile = TileId.Zero, Local = Iso2.IdentityFor(-1) }, cone);
                r.Draw(selection.Drawn, n);
                Assert.Greater(n, 0);

                var rep = new WorldLintReport();
                WorldLint.CheckWarpedRenderers(go, rep);
                Assert.AreEqual(n, rep.WarpedRenderers);
                Assert.AreEqual(0, rep.WarpedRenderersSmallBounds);
                Assert.IsTrue(rep.Passed, rep.ToString());

                r.Slots[0].ResetLocalBounds();
                rep = new WorldLintReport();
                WorldLint.CheckWarpedRenderers(go, rep);
                Assert.AreEqual(1, rep.WarpedRenderersSmallBounds);
                Assert.IsFalse(rep.Passed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                pool.Dispose();
            }
        }
#endif
    }
}
