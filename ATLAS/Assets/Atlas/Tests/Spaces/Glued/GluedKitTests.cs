using System;
using System.IO;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// S1 with the kit pieces modelled in Blender (kit hero assets design, 2026-10-03): the hooks in the cell and
    /// avatar builders, and the shipped landmark and avatar against the rules of the design once they exist.
    /// </summary>
    public class GluedKitTests
    {
        static string ShippedDirectory()
        {
            string root = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(root)) root = Directory.GetCurrentDirectory();
            return Path.Combine(root, "Assets", "Atlas", "Spaces", "Glued", "Resources", KitSet.ResourceFolder);
        }

        // The shipped piece, read as GluedSpace reads it; the test is skipped while the export has not run.
        static MeshData Shipped(string name)
        {
            string directory = ShippedDirectory();
            if (!File.Exists(Path.Combine(directory, "manifest.json")))
                Assert.Ignore("No kit pieces shipped yet: run tools/kit/export_kit.py on art/blender/ATLAS_Kit.blend.");
            KitSet set = KitSet.Load(file =>
            {
                string path = Path.Combine(directory, file == KitSet.ManifestName ? "manifest.json" : file + ".bytes");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            });
            var mesh = new MeshData();
            Assert.That(set.TryGet(name, mesh, out string error), Is.True, name + ": " + error);
            return mesh;
        }

        // A stand-in for a kit piece: a closed box in the Booth cell, foot at the origin.
        static MeshData Stand(float3 size)
        {
            var mesh = new MeshData(64, 128);
            mesh.AddBox(float3.zero, size, 0f, AtlasPalette.Uv(AtlasPalette.Booth), true);
            return mesh;
        }

        static int CountCells(AtlasMeshBuilder mesh, int cell)
        {
            float2 uv = AtlasPalette.Uv(cell);
            int n = 0;
            for (int v = 0; v < mesh.VertexCount; v++) if (math.all(mesh.Vertices[v].Uv0 == uv)) n++;
            return n;
        }

        [Test]
        public void CellMesh_WithAKitLandmark_StandsItAtTheLandmarkBase_InPlaceOfTheTetracube()
        {
            MeshData kit = Stand(new float3(0.1f, 0.6f, 0.2f));
            var built = new AtlasMeshBuilder();
            var plain = new AtlasMeshBuilder();
            GluedCellMesh.Build(1.2f, 1.7f, built, kit);
            GluedCellMesh.Build(1.2f, 1.7f, plain);
            Assert.That(CountCells(plain, GluedCellMesh.LandmarkEdge), Is.GreaterThan(0));
            Assert.That(CountCells(plain, GluedCellMesh.LandmarkBallColour), Is.GreaterThan(0));

            // The tetracube, its ball and its ring are gone; the piece is the last thing added, moved to the foot.
            Assert.That(CountCells(built, GluedCellMesh.LandmarkEdge), Is.EqualTo(0));
            Assert.That(CountCells(built, GluedCellMesh.LandmarkBallColour), Is.EqualTo(0));
            float2 foot = GluedCellMesh.LandmarkBase(1.2f, 1.7f);
            int first = built.VertexCount - kit.VertexCount;
            for (int v = 0; v < kit.VertexCount; v++)
                Assert.That(built.Vertices[first + v].Position, Is.EqualTo(kit.Vertices[v].Position + new float3(foot.x, 0f, foot.y)));
            Assert.That(built.TriangleCount, Is.EqualTo(GluedBudget.CellTriangles(1.2f, 1.7f, kit)));
            Assert.That(built.TriangleCount - kit.TriangleCount, Is.LessThan(plain.TriangleCount));
            Assert.That(built.BoundsMin.x, Is.EqualTo(-0.6f).Within(1e-5f));
            Assert.That(built.BoundsMax.z, Is.EqualTo(0.85f).Within(1e-5f));
        }

        [Test]
        public void AvatarMesh_WithAKitAvatar_IsTheKitAvatar()
        {
            MeshData kit = Stand(new float3(0.3f, 0.1f, 0.2f));
            var built = new AtlasMeshBuilder();
            GluedAvatarMesh.Build(built, kit);
            Assert.That(built.VertexCount, Is.EqualTo(kit.VertexCount));
            Assert.That(built.TriangleCount, Is.EqualTo(kit.TriangleCount));
            Assert.That(built.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles(kit)));
            for (int v = 0; v < kit.VertexCount; v++) Assert.That(built.Vertices[v].Position, Is.EqualTo(kit.Vertices[v].Position));
        }

        [Test]
        public void KitLandmark_IsChiral()
        {
            MeshData landmark = Shipped("landmark");
            // No turn by a multiple of 90 degrees brings its mirror image in x onto it: the farthest vertex of the
            // turned mirror image lies more than 2 cm from every vertex of the original.
            float closest = float.MaxValue;
            for (int quarter = 0; quarter < 4; quarter++)
            {
                math.sincos(0.5f * math.PI * quarter, out float sin, out float cos);
                float worst = 0f;
                for (int i = 0; i < landmark.VertexCount; i++)
                {
                    float3 p = landmark.Vertices[i].Position;
                    float mx = -p.x;
                    var turned = new float3(cos * mx + sin * p.z, p.y, -sin * mx + cos * p.z);
                    float nearest = float.MaxValue;
                    for (int j = 0; j < landmark.VertexCount; j++)
                        nearest = math.min(nearest, math.distancesq(turned, landmark.Vertices[j].Position));
                    worst = math.max(worst, nearest);
                }
                closest = math.min(closest, math.sqrt(worst));
            }
            TestContext.Out.WriteLine("landmark: the nearest turned mirror image is " + closest + " m off");
            Assert.That(closest, Is.GreaterThan(0.02f));
        }

        [Test]
        public void KitLandmark_FitsTheSmallestCell()
        {
            MeshData landmark = Shipped("landmark");
            Assert.That(landmark.TriangleCount, Is.InRange(1, 1500));
            float2 foot = GluedCellMesh.LandmarkBase(1.2f, 1.7f);
            Assert.That(foot.x + landmark.BoundsMin.x, Is.GreaterThan(-0.6f));
            Assert.That(foot.x + landmark.BoundsMax.x, Is.LessThan(0.6f));
            Assert.That(foot.y + landmark.BoundsMin.z, Is.GreaterThan(-0.85f));
            Assert.That(foot.y + landmark.BoundsMax.z, Is.LessThan(0.85f));
            Assert.That(landmark.BoundsMax.y, Is.LessThan(GluedCellMesh.PostHeight));
            Assert.That(landmark.BoundsMin.y, Is.GreaterThan(-1e-3f), "the foot stands on the floor");

            // In the cell it changes nothing of the footprint, so mirrored rows still meet exactly.
            var cell = new AtlasMeshBuilder();
            GluedCellMesh.Build(1.2f, 1.7f, cell, landmark);
            Assert.That(cell.BoundsMin.x, Is.EqualTo(-0.6f).Within(1e-5f));
            Assert.That(cell.BoundsMax.x, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(cell.BoundsMin.z, Is.EqualTo(-0.85f).Within(1e-5f));
            Assert.That(cell.BoundsMax.z, Is.EqualTo(0.85f).Within(1e-5f));
        }

        [Test]
        public void KitAvatar_KeepsItsHands()
        {
            MeshData avatar = Shipped("avatar");
            Assert.That(avatar.TriangleCount, Is.InRange(1, AtlasMeshBudget.SelfAvatar));
            float2 left = AtlasPalette.Uv(AtlasPalette.AvatarLeft), right = AtlasPalette.Uv(AtlasPalette.AvatarRight);
            float2 text = AtlasPalette.Uv(AtlasPalette.Text);
            double leftX = 0, rightX = 0, textZ = 0;
            int leftCount = 0, rightCount = 0, textCount = 0;
            for (int v = 0; v < avatar.VertexCount; v++)
            {
                AtlasVertex vertex = avatar.Vertices[v];
                if (math.all(vertex.Uv0 == left)) { leftX += vertex.Position.x; leftCount++; }
                else if (math.all(vertex.Uv0 == right)) { rightX += vertex.Position.x; rightCount++; }
                else if (math.all(vertex.Uv0 == text)) { textZ += vertex.Position.z; textCount++; }
            }
            Assert.That(leftCount, Is.GreaterThan(0), "no AvatarLeft cells");
            Assert.That(rightCount, Is.GreaterThan(0), "no AvatarRight cells");
            Assert.That(textCount, Is.GreaterThan(0), "no Text face marker");
            Assert.That(leftX / leftCount, Is.LessThan(0.0), "AvatarLeft is on the walker's left, -X");
            Assert.That(rightX / rightCount, Is.GreaterThan(0.0), "AvatarRight is on the walker's right, +X");
            Assert.That(textZ / textCount, Is.GreaterThan(0.0), "the face marker is in front, +Z");
            Assert.That(avatar.BoundsMax.y, Is.LessThanOrEqualTo(0.15f), "the top is at most 0.15 m above the eyes");
        }

        [Test]
        public void GluedBudget_WithKitPieces()
        {
            MeshData landmark = Shipped("landmark"), avatar = Shipped("avatar");
            // The largest cell the rule can give, so the bound holds in every room.
            int cell = GluedBudget.CellTriangles(2.25f, 2.25f, landmark), body = GluedBudget.AvatarTriangles(avatar);
            int prompt = GluedBudget.PromptTriangles();
            Assert.That(cell, Is.LessThanOrEqualTo(AtlasMeshBudget.GluedCell), "the whole cell, floor and posts with the landmark");
            Assert.That(body, Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 })
            {
                GluedDrawCost cost = GluedBudget.Lattice(layout, 5, cell, body, true, prompt);
                TestContext.Out.WriteLine(layout + ": " + cost.TrianglesPerFrame + " triangles per frame (cell " + cell
                    + ", avatar " + body + ")");
                Assert.That(GluedBudget.LatticeFits(layout, in cost), Is.True, layout.ToString());
            }
        }
    }
}
