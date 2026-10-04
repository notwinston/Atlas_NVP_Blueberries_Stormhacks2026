// [P1-B07] The meshes of the generated world (MP 3.4 table, 8.4, 8.5): budgets, the 0.25 m edge rule in the curved metric,
// determinism per seed, props inside their own tile, and the spanning beam meeting on the shared edge. Engine-free.
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Tiles.Tests
{
    public class WorldPropMeshTests
    {
        static readonly double[] Scales = { 1.4, 1.9, 3.0 };   // MP 8.4: the three length scales tile meshes are accepted at
        static readonly uint[] Seeds = { 1, 42, 20261003 };
        static RoomFootprint Room => RoomFootprint.Rectangle(3, 3, 2.0 / 1.0613);

        // Every key a world can produce: each archetype, every feature edge, every Garden variant, every kerb pair of a Corridor.
        static IEnumerable<WorldTileKey> AllKeys()
        {
            yield return new WorldTileKey(Archetype.Home, 0);
            yield return new WorldTileKey(Archetype.Plaza, 0);
            foreach (int mask in new[] { 0, 1, 2, 4, 8, 5, 10 }) yield return new WorldTileKey(Archetype.Corridor, mask);
            yield return new WorldTileKey(Archetype.Tower, 0);
            for (int v = 0; v < WorldTileKey.GardenVariants; v++) yield return new WorldTileKey(Archetype.Garden, 0, -1, v);
            for (int e = 0; e < 4; e++)
            {
                yield return new WorldTileKey(Archetype.Doorway, 0, e);
                yield return new WorldTileKey(Archetype.SpanA, 0, e);
                yield return new WorldTileKey(Archetype.SpanB, 0, e);
            }
        }

        // <P, N_e> for the four edge lines of a {4, q} tile (A.3): negative inside the tile, zero on edge e.
        static double4 EdgeSides(float3 chart, int q, double L)
        {
            double r = WorldPropMeshBuilder.InRadius(q), ch = Math.Cosh(r), sh = Math.Sinh(r);
            double3 p = Geometry2.Exp(new double2(chart.x, chart.z) / L, -1);
            return new double4(p.x * ch - p.z * sh, p.y * ch - p.z * sh, -p.x * ch - p.z * sh, -p.y * ch - p.z * sh);
        }

        [Test]
        public void EveryArchetype_WithinTriangleBudget_AndEdgeLimit()
        {
            var props = new WorldPropMeshBuilder();
            var data = new AtlasMeshBuilder();
            foreach (int q in new[] { 5, 6 })
                foreach (double L in Scales)
                    foreach (var key in AllKeys())
                    {
                        data.Clear();
                        int tris = props.Build(q, L, key, data);
                        int cap = WorldPropBudget.Triangles(key.Archetype);
                        string what = $"{key} q={q} L={L}";
                        Assert.AreEqual(data.TriangleCount, tris, what);
                        Assert.LessOrEqual(tris, cap, "MP 8.4 triangle cap: " + what);
                        if (cap > 0) Assert.Greater(tris, 0, "an archetype with props draws something: " + what);
                        else Assert.AreEqual(0, tris, "Home, Plaza and Corridor carry no props: " + what);
                        double edge = WorldPropMeshBuilder.LongestCurvedEdge(data, 0, L);
                        Assert.LessOrEqual(edge, WorldPropBudget.MaxEdge + 1e-4, "MP 8.4 no edge over 0.25 m in the curved metric: " + what);
                        float top = 0f;
                        for (int i = 0; i < data.VertexCount; i++)
                        {
                            float3 p = data.Vertices[i].Position;
                            Assert.IsTrue(math.all(math.isfinite(p)), what);
                            Assert.GreaterOrEqual(p.y, 0f, what);
                            top = math.max(top, p.y);
                        }
                        Assert.LessOrEqual(top, WorldPropBudget.MaxHeight + 1e-5f, "nothing above the draw rule's shell height: " + what);
                        if (key.Archetype == Archetype.Garden) Assert.Less(top, WorldPropBudget.GardenMaxHeight, "MP 3.4 Garden: under 0.5 m: " + what);
                        if (key.Archetype == Archetype.Tower) Assert.AreEqual(WorldPropBudget.MaxHeight, top, 1e-5f, "MP 3.4 Tower: tall");
                    }
        }

        // The cell diagonals are already inside the limit, so the measured edge is the grid's, not an accident of the strict pass.
        [Test]
        public void EdgeMeasure_IsTheCurvedMetric()
        {
            var data = new AtlasMeshBuilder();
            const double L = 1.9;
            // one triangle with a 0.2 m chart edge across the radial direction, 1.5 m from the tile centre
            int a = data.AddVertex(new float3(1.5f, 0f, -0.1f), new float3(0, 1, 0), float2.zero, float2.zero);
            int b = data.AddVertex(new float3(1.5f, 0f, 0.1f), new float3(0, 1, 0), float2.zero, float2.zero);
            int c = data.AddVertex(new float3(1.5f, 0.01f, 0f), new float3(0, 1, 0), float2.zero, float2.zero);
            data.AddTriangle(a, b, c);
            double rho = 1.5 / L;
            Assert.AreEqual(0.2 * Math.Sinh(rho) / rho, WorldPropMeshBuilder.LongestCurvedEdge(data, 0, L), 2e-3, "A.2: a chart length across the radius is stretched by sinh(rho)/rho");
            Assert.Greater(WorldPropMeshBuilder.LongestCurvedEdge(data, 0, L), data.LongestEdge());
        }

        [Test]
        public void Props_StayInsideTheirTile_SpansEndOnTheSharedEdge()
        {
            var props = new WorldPropMeshBuilder();
            var data = new AtlasMeshBuilder();
            foreach (double L in Scales)
                foreach (var key in AllKeys())
                {
                    data.Clear();
                    if (props.Build(5, L, key, data) == 0) continue;
                    bool span = key.Archetype == Archetype.SpanA || key.Archetype == Archetype.SpanB;
                    double worst = double.MinValue;
                    for (int i = 0; i < data.VertexCount; i++) worst = Math.Max(worst, math.cmax(EdgeSides(data.Vertices[i].Position, 5, L)));
                    if (span)
                    {
                        Assert.LessOrEqual(worst, 1e-6, $"a span half ends on the shared edge, not beyond it: {key} L={L}");
                        Assert.AreEqual(4, EdgeVertices(data, key.FeatureEdge, L).Count, $"the open end of the beam is its four corners on the edge: {key} L={L}");
                    }
                    else Assert.Less(worst, -1e-3, $"a prop lies strictly inside its own tile: {key} L={L}");
                }
        }

        // MP 3.4 "two neighbouring tiles that each carry half of one long object"; MP 3.5 "visible from the start".
        [Test]
        public void StartSpan_ReachesBothTiles_AndMeetsOnTheEdge([ValueSource(nameof(Seeds))] uint seed)
        {
            const double L = 1.885;
            var world = new WorldGenerator(seed, Room);
            Assert.IsTrue(world.TryGetStartSpan(out TileId ta, out TileId tb));
            TileContent ca = world.Content(ta), cb = world.Content(tb);
            Assert.AreEqual(Archetype.SpanA, ca.Archetype); Assert.AreEqual(Archetype.SpanB, cb.Archetype);
            Assert.AreEqual(tb, world.Graph.Neighbour(ta, ca.FeatureEdge)); Assert.AreEqual(ta, world.Graph.Neighbour(tb, cb.FeatureEdge));

            var props = new WorldPropMeshBuilder();
            AtlasMeshBuilder a = new AtlasMeshBuilder(), b = new AtlasMeshBuilder();
            Assert.Greater(props.Build(5, L, WorldTileKey.From(ca), a), 0);
            Assert.Greater(props.Build(5, L, WorldTileKey.From(cb), b), 0);

            // each half reaches SpanHalfLength into its own tile, measured from the shared edge
            double reach = WorldPropMeshBuilder.SpanHalfLength(5, L);
            Assert.AreEqual(reach, Depth(a, ca.FeatureEdge, L), 1e-3);
            Assert.AreEqual(reach, Depth(b, cb.FeatureEdge, L), 1e-3);
            Assert.Greater(reach, 0.5, "long enough to be read as one object across two tiles");

            // b's end, taken into a's chart by the step across the edge (MP 4.6 G), lands on a's end: one beam, no gap, no step
            double3x3 G = world.Graph.StepMatrix(ta, ca.FeatureEdge).M;
            var endA = EdgeVertices(a, ca.FeatureEdge, L); var endB = EdgeVertices(b, cb.FeatureEdge, L);
            Assert.AreEqual(4, endA.Count); Assert.AreEqual(4, endB.Count);
            foreach (float3 vb in endB)
            {
                double2 c = L * Geometry2.Log(math.mul(G, Geometry2.Exp(new double2(vb.x, vb.z) / L, -1)), -1);
                var inA = new float3((float)c.x, vb.y, (float)c.y);
                float best = float.MaxValue;
                foreach (float3 va in endA) best = math.min(best, math.distance(va, inA));
                Assert.Less(best, 1e-4f, "the two halves share their end points on the edge");
            }
        }

        static double Depth(AtlasMeshBuilder data, int edge, double L)
        {
            double deepest = 0;
            for (int i = 0; i < data.VertexCount; i++)
            {
                float3 p = data.Vertices[i].Position;
                if (p.y < WorldPropMeshBuilder.SpanBottom - 1e-4f || p.y > WorldPropMeshBuilder.SpanBottom + WorldPropMeshBuilder.SpanThickness + 1e-4f) continue;   // the beam, not its marker
                deepest = Math.Max(deepest, L * Math.Asinh(-EdgeSides(p, 5, L)[edge]));   // A.2: distance to a line
            }
            return deepest;
        }

        // The distinct positions that lie on the edge's line (the faces of the beam do not share vertices).
        static List<float3> EdgeVertices(AtlasMeshBuilder data, int edge, double L)
        {
            var list = new List<float3>();
            for (int i = 0; i < data.VertexCount; i++)
            {
                float3 p = data.Vertices[i].Position;
                if (Math.Abs(EdgeSides(p, 5, L)[edge]) >= 1e-6) continue;
                bool known = false;
                foreach (float3 o in list) if (math.distance(o, p) < 1e-5f) known = true;
                if (!known) list.Add(p);
            }
            return list;
        }

        [Test]
        public void SameSeed_SameMeshes_OnTwoRuns([ValueSource(nameof(Seeds))] uint seed)
        {
            const double L = 1.885;
            WorldGenerator w1 = new WorldGenerator(seed, Room), w2 = new WorldGenerator(seed, Room);
            WorldTileMeshBuilder b1 = new WorldTileMeshBuilder(), b2 = new WorldTileMeshBuilder();
            foreach (TileId t in w1.Ball(5))
            {
                TileContent c1 = w1.Content(t), c2 = w2.Content(t);
                Assert.AreEqual(WorldTileKey.From(c1), WorldTileKey.From(c2));
                var lod = t == TileId.Zero ? TileLod.Near : TileLod.Mid;
                WorldTileMeshInfo i1 = b1.Build(5, L, lod, c1), i2 = b2.Build(5, L, lod, c2);
                Assert.AreEqual(i1.Triangles, i2.Triangles);
                Assert.AreEqual(b1.Data.VertexCount, b2.Data.VertexCount);
                for (int i = 0; i < b1.Data.VertexCount; i++)
                {
                    AtlasVertex u = b1.Data.Vertices[i], v = b2.Data.Vertices[i];
                    Assert.IsTrue(u.Position.Equals(v.Position) && u.Color == v.Color && u.Uv0.Equals(v.Uv0) && u.Uv1.Equals(v.Uv1), $"vertex {i} of {t}");
                }
                for (int i = 0; i < b1.Data.IndexCount; i++) Assert.AreEqual(b1.Data.Indices[i], b2.Data.Indices[i]);
            }
        }

        // What the three lint seeds hold to depth 6 (597 tiles): every archetype of MP 3.4 is drawn somewhere.
        [Test]
        public void ThreeSeeds_HoldEveryArchetype()
        {
            var total = new int[8];
            foreach (uint seed in Seeds)
            {
                var world = new WorldGenerator(seed, Room);
                var count = new int[8];
                foreach (TileId t in world.Ball(6)) count[(int)world.Content(t).Archetype]++;
                TestContext.WriteLine($"seed {seed}: " + string.Join(" ", Array.ConvertAll((Archetype[])Enum.GetValues(typeof(Archetype)), a => $"{a}={count[(int)a]}")));
                foreach (Archetype a in new[] { Archetype.Home, Archetype.Plaza, Archetype.Corridor, Archetype.Tower, Archetype.SpanA, Archetype.SpanB, Archetype.Garden })
                    Assert.Greater(count[(int)a], 0, $"seed {seed} holds a {a}");
                for (int i = 0; i < 8; i++) total[i] += count[i];
            }
            Assert.Greater(total[(int)Archetype.Doorway], 0, "a Doorway (a region gate) occurs in the three worlds");
        }

        // The Garden's layout comes from the tile's content seed (bits 16..18), so gardens differ and each is repeatable.
        [Test]
        public void Garden_VariantComesFromTheContentSeed()
        {
            var world = new WorldGenerator(1, Room);
            var props = new WorldPropMeshBuilder();
            var variants = new HashSet<int>(); var shapes = new HashSet<long>();
            foreach (TileId t in world.Ball(6))
            {
                TileContent c = world.Content(t);
                if (c.Archetype != Archetype.Garden) continue;
                WorldTileKey key = WorldTileKey.From(c);
                Assert.AreEqual((int)((t.ContentSeed(1) >> 16) & 7), key.Variant);
                if (!variants.Add(key.Variant)) continue;
                var data = new AtlasMeshBuilder();
                props.Build(5, 1.885, key, data);
                float3 sum = float3.zero;
                for (int i = 0; i < data.VertexCount; i++) sum += data.Vertices[i].Position;
                shapes.Add(((long)math.round(sum.x * 1000) << 24) ^ ((long)math.round(sum.z * 1000) << 4) ^ data.VertexCount);
            }
            Assert.Greater(variants.Count, 3, "several Garden variants occur to depth 6");
            Assert.AreEqual(variants.Count, shapes.Count, "each variant is a different layout");
        }

        [Test]
        public void Key_HoldsOnlyWhatDecidesTheMesh()
        {
            var world = new WorldGenerator(42, Room);
            var codes = new HashSet<int>();
            foreach (TileId t in world.Ball(6))
            {
                TileContent c = world.Content(t);
                WorldTileKey key = WorldTileKey.From(c);
                Assert.Less(key.Code, WorldTileKey.CodeCount);
                Assert.AreEqual(c.Archetype, key.Archetype); Assert.AreEqual(c.KerbMask, key.KerbMask);
                bool feature = c.Archetype == Archetype.Doorway || c.Archetype == Archetype.SpanA || c.Archetype == Archetype.SpanB;
                Assert.AreEqual(feature ? c.FeatureEdge : -1, key.FeatureEdge);
                if (c.Archetype != Archetype.Garden) Assert.AreEqual(0, key.Variant);
                if (c.Archetype != Archetype.Corridor) Assert.AreEqual(0, c.KerbMask, "only a Corridor builds kerbs (MP 3.4)");
                codes.Add(key.Code);
            }
            Assert.Less(codes.Count, 48, "597 tiles share a few dozen meshes per level of detail");
        }

        // MP 8.5: a tile is one draw call, so its mesh is the shell with its kerbs and wedges, then the props.
        [Test]
        public void TileMesh_IsShellThenProps()
        {
            const double L = 1.885;
            var builder = new WorldTileMeshBuilder();
            var shell = new TileMeshGenerator(TilePalette.Default);
            foreach (TileLod lod in new[] { TileLod.Near, TileLod.Mid, TileLod.Far })
                foreach (var key in AllKeys())
                {
                    WorldTileMeshInfo info = builder.Build(5, L, lod, key);
                    TileMeshLayout lay = shell.Build(TileMeshSpec.Hyperbolic(5, L, lod, key.KerbMask, 0xF));
                    Assert.AreEqual(lay.TriangleCount, info.ShellTriangles);
                    Assert.AreEqual(lay.IndexCount, info.PropFirstIndex);
                    Assert.AreEqual(info.Triangles, builder.Data.TriangleCount);
                    for (int i = 0; i < lay.VertexCount; i++) Assert.IsTrue(shell.Vertices[i].Position.Equals(builder.Data.Vertices[i].Position));
                    for (int c = 0; c < 4; c++) Assert.Greater(info.Shell.PostIndices(c).length, 0, "MP 3.5 a post at every corner");
                    for (int e = 0; e < 4; e++) Assert.AreEqual((key.KerbMask & (1 << e)) != 0, info.Shell.KerbIndices(e).length > 0, $"kerb on edge {e} of {key}");
                    Assert.LessOrEqual(info.Triangles, TileChart.TriangleCap(lod) + WorldPropBudget.Triangles(key.Archetype));
                    Assert.Less(builder.Data.VertexCount, AtlasMesh.MaxVertices16, "16-bit indices");
                }
        }

        // MP 3.4 "each has its own hue": the tint keeps the palette's region hue and does not darken the tile.
        [Test]
        public void RegionTints_AreFourHues_AtFullBrightness()
        {
            for (int r = 0; r < Regions.Count; r++)
            {
                float3 tint = WorldRegionTint.Linear(r);
                Assert.AreEqual(1f, math.cmax(tint), 1e-6f);
                Assert.Greater(math.cmin(tint), 0.1f);
                float3 cell = AtlasPalette.Linear(AtlasPalette.Region(r, AtlasPalette.RegionSteps - 1));
                Assert.Less(math.length(math.normalize(tint) - math.normalize(cell)), 1e-5f, "the hue of the palette's region cell");
                for (int o = 0; o < r; o++) Assert.Greater(math.distance(tint, WorldRegionTint.Linear(o)), 0.1f, $"regions {o} and {r} differ");
                Assert.IsTrue(WorldRegionTint.FogLinear(r).Equals(AtlasPalette.Linear(AtlasPalette.Region(r, 0))));
            }
            Assert.AreEqual(Regions.Count, AtlasPalette.RegionCount);
        }
    }
}
