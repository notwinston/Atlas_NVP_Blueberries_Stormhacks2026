using System.IO;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    /// <summary>
    /// The rubber duck of the S3 duck maze (2026-10-04 design, section 4): a kit piece modelled in Blender from
    /// art/source/free_rubber_duck_3d_model.glb and shipped beside the Glued landmark and avatar.
    /// </summary>
    public class DuckKitTests
    {
        static readonly int[] s_DuckCells = { AtlasPalette.DuckYellow, AtlasPalette.DuckOrange, AtlasPalette.DuckWhite, AtlasPalette.DuckBlack };

        static string KitDirectory() =>
            Path.Combine(Fixtures.ProjectDir, "Assets", "Atlas", "Spaces", "Glued", "Resources", KitSet.ResourceFolder);

        static byte[] Read(string file)
        {
            string path = Path.Combine(KitDirectory(), file == KitSet.ManifestName ? "manifest.json" : file + ".bytes");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        static MeshData Duck()
        {
            KitSet set = KitSet.Load(Read);
            Assert.That(set.Count, Is.GreaterThan(0), "the kit loads: " + set.LastError);
            var mesh = new MeshData();
            Assert.That(set.TryGet("duck", mesh, out string error), Is.True, "duck: " + error);
            return mesh;
        }

        static int CellOf(float2 uv)
        {
            for (int k = 0; k < s_DuckCells.Length; k++)
                if (math.all(math.abs(AtlasPalette.Uv(s_DuckCells[k]) - uv) < 1e-4f)) return k;
            return -1;
        }

        [Test]
        public void DuckCells_AreFourFlatCellsOfTheirOwnColour_BelowTheEmissiveRows()
        {
            uint[] expected = { 0xFFC21A, 0xFF4A12, 0xE8E8E8, 0x1A1A1A };
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int k = 0; k < 4; k++)
            {
                Assert.That(seen.Add(s_DuckCells[k]), Is.True, "the four cells differ");
                Assert.That(AtlasPalette.IsEmissive(s_DuckCells[k]), Is.False);
                float3 c = AtlasPalette.Srgb(s_DuckCells[k]);
                uint rgb = ((uint)math.round(c.x * 255f) << 16) | ((uint)math.round(c.y * 255f) << 8) | (uint)math.round(c.z * 255f);
                Assert.That(rgb, Is.EqualTo(expected[k]), "cell " + s_DuckCells[k]);
            }
        }

        [Test]
        public void Duck_LoadsFromTheKit_AndIsWithinTheMeshRules()
        {
            MeshData duck = Duck();
            Assert.That(duck.TriangleCount, Is.InRange(500, 2500));
            Assert.That(duck.VertexCount, Is.LessThanOrEqualTo(65535), "the file stores 16-bit indices");

            // The checks of GlbValidator that apply to a kit piece (the Forge contract's attribute list and its
            // 1,500-triangle floor are for generated assets, not for kit pieces): finite, no triangle without
            // area, no edge over the curved-space limit, a file under the size limit.
            float longest = 0f;
            for (int i = 0; i < duck.IndexCount; i += 3)
            {
                float3 a = duck.Vertices[duck.Indices[i]].Position, b = duck.Vertices[duck.Indices[i + 1]].Position, c = duck.Vertices[duck.Indices[i + 2]].Position;
                Assert.That(math.all(math.isfinite(a)) && math.all(math.isfinite(b)) && math.all(math.isfinite(c)), Is.True);
                Assert.That(0.5 * math.length(math.cross(b - a, c - a)), Is.GreaterThan(GlbValidator.MinTriangleArea), "triangle " + i / 3 + " has no area");
                longest = math.max(longest, math.max(math.distance(a, b), math.max(math.distance(b, c), math.distance(c, a))));
            }
            Assert.That(longest, Is.LessThanOrEqualTo(MeshData.DefaultMaxEdge + GlbValidator.EdgeSlack));
            Assert.That(Read("duck.glb").Length, Is.LessThanOrEqualTo(GlbValidator.MaxBytes));
        }

        [Test]
        public void Duck_IsThirtyCentimetresTall_StandingOnTheFloor_AndCentred()
        {
            MeshData duck = Duck();
            float height = duck.BoundsMax.y - duck.BoundsMin.y;
            TestContext.Out.WriteLine("duck: " + duck.TriangleCount + " triangles, bounds " + duck.BoundsMin + " .. " + duck.BoundsMax);
            Assert.That(height, Is.InRange(0.25f, 0.35f));
            Assert.That(duck.BoundsMin.y, Is.EqualTo(0f).Within(0.01f), "the bottom is at y = 0");
            Assert.That(0.5f * (duck.BoundsMin.x + duck.BoundsMax.x), Is.EqualTo(0f).Within(0.02f));
            Assert.That(0.5f * (duck.BoundsMin.z + duck.BoundsMax.z), Is.EqualTo(0f).Within(0.02f));
        }

        [Test]
        public void Duck_UsesOnlyTheFourDuckCells_AndIsMostlyYellow()
        {
            MeshData duck = Duck();
            for (int v = 0; v < duck.VertexCount; v++)
                Assert.That(CellOf(duck.Vertices[v].Uv0), Is.GreaterThanOrEqualTo(0), "vertex " + v + " is on " + duck.Vertices[v].Uv0);

            int yellow = 0;
            for (int i = 0; i < duck.IndexCount; i += 3)
            {
                // Corners of one triangle share a cell (the export gives each face one material).
                if (CellOf(duck.Vertices[duck.Indices[i]].Uv0) == 0) yellow++;
            }
            double share = yellow / (double)duck.TriangleCount;
            TestContext.Out.WriteLine("duck: " + (100 * share).ToString("F1") + " % of the triangles are yellow");
            Assert.That(share, Is.GreaterThanOrEqualTo(0.60));
        }
    }
}
