using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The phone's asset validator and mesh reader (docs/contracts/lane-d-link.md sections 4.1 and 4.2) on
    /// files made by the Forge's own generator and clean-up, and on the same files with one rule broken.
    /// The fixtures are written by Fixtures/Tools~/make_forge_fixtures.py.
    /// </summary>
    public class GlbValidatorTests
    {
        static IEnumerable<string> FixtureFiles()
        {
            JsonValue list = AssetFixtures.Json("fixtures.json")["fixtures"];
            for (int i = 0; i < list.Count; i++) yield return list[i].GetString("file");
        }

        static JsonValue Fixture(string file)
        {
            JsonValue list = AssetFixtures.Json("fixtures.json")["fixtures"];
            for (int i = 0; i < list.Count; i++) if (list[i].GetString("file") == file) return list[i];
            throw new ArgumentException(file);
        }

        static byte[] Bytes(string file) { return File.ReadAllBytes(Path.Combine(AssetFixtures.FixtureDirectory(), file)); }

        [TestCaseSource(nameof(FixtureFiles))]
        public void Glb_Fixture_GetsTheExpectedVerdict(string file)
        {
            JsonValue fixture = Fixture(file);
            byte[] glb = Bytes(file);
            GlbExpectation expect = AssetFixtures.Expect(AssetFixtures.Dims(fixture), fixture.GetInt("triTarget"), fixture.GetString("sha256"));
            GlbReport report = GlbValidator.Validate(glb, in expect);

            string want = fixture.GetString("expect");
            if (want == "ok")
            {
                Assert.That(report.Ok, Is.True, "rejected with " + report.FailedCheck);
                Assert.That(report.FailedCheck, Is.Null);
                Assert.That(report.Triangles, Is.EqualTo(fixture.GetInt("triangles")));
                Assert.That(report.Vertices, Is.EqualTo(fixture.GetInt("vertices")));
                Assert.That(report.Bytes, Is.EqualTo(glb.Length));
                Assert.That(report.MaxEdge, Is.LessThanOrEqualTo(0.25f).And.GreaterThan(0.01f));
                float3 dims = AssetFixtures.Dims(fixture);
                Assert.That(report.BoundsX, Is.EqualTo(dims.x).Within(0.01f));
                Assert.That(report.BoundsY, Is.EqualTo(dims.y).Within(0.01f));
                Assert.That(report.BoundsZ, Is.EqualTo(dims.z).Within(0.01f));
            }
            else
            {
                Assert.That(report.Ok, Is.False);
                Assert.That(report.FailedCheck, Is.EqualTo(want));
            }
        }

        [Test]
        public void Glb_TheFixturesCoverTheThreeEditedFaults()
        {
            var codes = new List<string>();
            foreach (string file in FixtureFiles()) codes.Add(Fixture(file).GetString("expect"));
            Assert.That(codes, Does.Contain("tris_max"));
            Assert.That(codes, Does.Contain("edge"));
            Assert.That(codes, Does.Contain("no_material"));
            Assert.That(codes.FindAll(c => c == "ok").Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Glb_TrianglesOverTheJobsTarget_AreRejected()
        {
            JsonValue fixture = Fixture("forge_table.glb.bytes");
            byte[] glb = Bytes("forge_table.glb.bytes");
            int triangles = fixture.GetInt("triangles");
            GlbExpectation exact = AssetFixtures.Expect(AssetFixtures.Dims(fixture), triangles);
            GlbExpectation under = AssetFixtures.Expect(AssetFixtures.Dims(fixture), triangles - 1);
            Assert.That(GlbValidator.Validate(glb, in exact).Ok, Is.True);
            Assert.That(GlbValidator.Validate(glb, in under).FailedCheck, Is.EqualTo("tris_target"));
        }

        static GlbExpectation Level(GlbLevel level, int triTarget)
        {
            GlbExpectation expect = AssetFixtures.Expect(TestGlb.Dims, triTarget);
            expect.Level = level;
            return expect;
        }

        [TestCase(GlbLevel.Full, 1499, "tris_min")]
        [TestCase(GlbLevel.Full, 1500, null)]
        [TestCase(GlbLevel.Full, 5001, null)]
        [TestCase(GlbLevel.Full, 15000, null)]
        [TestCase(GlbLevel.Full, 15001, "tris_max")]
        [TestCase(GlbLevel.Light, 1499, "tris_min")]
        [TestCase(GlbLevel.Light, 1500, null)]
        [TestCase(GlbLevel.Light, 5000, null)]
        [TestCase(GlbLevel.Light, 5001, "tris_max")]
        public void Glb_TriangleLimits_DependOnTheLevel(GlbLevel level, int triangles, string failed)
        {
            GlbExpectation expect = Level(level, 0);
            GlbReport report = GlbValidator.Validate(TestGlb.Make(triangles), in expect);
            Assert.That(report.Triangles, Is.EqualTo(triangles));
            Assert.That(report.FailedCheck, Is.EqualTo(failed));
            Assert.That(report.Ok, Is.EqualTo(failed == null));
        }

        [TestCase(GlbLevel.Full)]
        [TestCase(GlbLevel.Light)]
        public void Glb_TheJobsTarget_CapsEachLevel(GlbLevel level)
        {
            byte[] glb = TestGlb.Make(3001);
            GlbExpectation exact = Level(level, 3001), under = Level(level, 3000);
            Assert.That(GlbValidator.Validate(glb, in exact).Ok, Is.True);
            Assert.That(GlbValidator.Validate(glb, in under).FailedCheck, Is.EqualTo("tris_target"));
        }

        [Test]
        public void Glb_FileSizeLimits_DependOnTheLevel()
        {
            byte[] big = TestGlb.Make(1500, 30000);                  // between 1 MiB and 2 MiB
            Assert.That(big.Length, Is.GreaterThan(1048576).And.LessThanOrEqualTo(2097152));
            GlbExpectation full = Level(GlbLevel.Full, 0), light = Level(GlbLevel.Light, 0);
            Assert.That(GlbValidator.Validate(big, in full).Ok, Is.True);
            Assert.That(GlbValidator.Validate(big, in light).FailedCheck, Is.EqualTo("size"));

            byte[] huge = TestGlb.Make(1500, 55000);                 // past 2 MiB
            Assert.That(huge.Length, Is.GreaterThan(2097152));
            Assert.That(GlbValidator.Validate(huge, in full).FailedCheck, Is.EqualTo("size"));

            Assert.That(GlbValidator.MaxBytesOf(GlbLevel.Full), Is.EqualTo(2097152));
            Assert.That(GlbValidator.MaxBytesOf(GlbLevel.Light), Is.EqualTo(1048576));
            Assert.That(GlbValidator.MaxTrianglesOf(GlbLevel.Full), Is.EqualTo(15000));
            Assert.That(GlbValidator.MaxTrianglesOf(GlbLevel.Light), Is.EqualTo(5000));
        }

        [Test]
        public void Glb_ADefaultExpectation_IsTheFullCopy()
        {
            // The call shape of before: no level named. A copy of 6,000 triangles is a full copy.
            var expect = new GlbExpectation { DimX = TestGlb.Dims.x, DimY = TestGlb.Dims.y, DimZ = TestGlb.Dims.z, TriTarget = 6000 };
            Assert.That(expect.Level, Is.EqualTo(GlbLevel.Full));
            Assert.That(GlbValidator.Validate(TestGlb.Make(6000), in expect).Ok, Is.True);
        }

        [Test]
        public void Glb_AnotherHash_IsRejected()
        {
            JsonValue fixture = Fixture("forge_table.glb.bytes");
            GlbExpectation expect = AssetFixtures.Expect(AssetFixtures.Dims(fixture), 5000, new string('0', 64));
            Assert.That(GlbValidator.Validate(Bytes("forge_table.glb.bytes"), in expect).FailedCheck, Is.EqualTo("hash"));
        }

        [Test]
        public void Glb_AnotherBox_IsRejectedForItsBounds()
        {
            // The chair in the table's box: wrong scale, units or up axis look like this.
            JsonValue table = Fixture("forge_table.glb.bytes");
            GlbExpectation expect = AssetFixtures.Expect(AssetFixtures.Dims(table), 5000);
            Assert.That(GlbValidator.Validate(Bytes("forge_chair.glb.bytes"), in expect).FailedCheck, Is.EqualTo("bounds"));
        }

        [Test]
        public void Glb_BrokenContainers_AreRejected()
        {
            byte[] good = Bytes("forge_plant.glb.bytes");
            GlbExpectation expect = AssetFixtures.Expect(AssetFixtures.Dims(Fixture("forge_plant.glb.bytes")), 5000);

            Assert.That(GlbValidator.Validate(null, in expect).FailedCheck, Is.EqualTo("container"));
            Assert.That(GlbValidator.Validate(new byte[10], in expect).FailedCheck, Is.EqualTo("container"));

            var truncated = new byte[good.Length - 100];
            Array.Copy(good, truncated, truncated.Length);
            Assert.That(GlbValidator.Validate(truncated, in expect).FailedCheck, Is.EqualTo("container"));

            var magic = (byte[])good.Clone();
            magic[0] = (byte)'x';
            Assert.That(GlbValidator.Validate(magic, in expect).FailedCheck, Is.EqualTo("container"));

            var version = (byte[])good.Clone();
            version[4] = 1;
            Assert.That(GlbValidator.Validate(version, in expect).FailedCheck, Is.EqualTo("container"));
        }

        [Test]
        public void Glb_NotANumber_IsRejected()
        {
            byte[] glb = Bytes("forge_plant.glb.bytes");
            GlbFile file;
            string error;
            Assert.That(GlbFile.TryParse(glb, glb.Length, out file, out error), Is.True, error);
            JsonValue primitive;
            Assert.That(file.TryPrimitive(out primitive), Is.True);
            GlbAccessor position;
            Assert.That(file.TryAccessor(primitive["attributes"].GetInt("POSITION"), out position), Is.True);
            Array.Copy(BitConverter.GetBytes(float.NaN), 0, glb, position.Offset + 4, 4);

            GlbExpectation expect = AssetFixtures.Expect(AssetFixtures.Dims(Fixture("forge_plant.glb.bytes")), 5000);
            Assert.That(GlbValidator.Validate(glb, in expect).FailedCheck, Is.EqualTo("finite"));
        }

        [Test]
        public void Glb_Sha256_MatchesTheForges()
        {
            foreach (string file in FixtureFiles())
            {
                byte[] glb = Bytes(file);
                Assert.That(GlbValidator.Sha256Hex(glb, glb.Length), Is.EqualTo(Fixture(file).GetString("sha256")), file);
            }
        }

        [TestCase("forge_table.glb.bytes")]
        [TestCase("forge_chair.glb.bytes")]
        [TestCase("forge_couch.glb.bytes")]
        [TestCase("forge_plant.glb.bytes")]
        public void Glb_MeshReader_GivesTheLooksVertexFormat(string fileName)
        {
            JsonValue fixture = Fixture(fileName);
            byte[] glb = Bytes(fileName);
            var mesh = new MeshData();
            Assert.That(GlbMeshReader.TryRead(glb, glb.Length, mesh), Is.True);
            AssertIsFurniture(mesh, AssetFixtures.Dims(fixture), fixture.GetInt("triangles"), fixture.GetInt("vertices"));
        }

        /// <summary>
        /// The reader does what a glTF importer does for a right-handed file, vertex by vertex: x and the
        /// normal's x negated, v turned over, the colour bytes kept, the second and third corner exchanged.
        /// </summary>
        [Test]
        public void Glb_MeshReader_MirrorsXFlipsVAndTurnsTheWinding()
        {
            byte[] glb = Bytes("forge_table.glb.bytes");
            GlbFile file;
            string error;
            JsonValue primitive;
            Assert.That(GlbFile.TryParse(glb, glb.Length, out file, out error), Is.True, error);
            Assert.That(file.TryPrimitive(out primitive), Is.True);
            JsonValue attributes = primitive["attributes"];
            GlbAccessor position, normal, uv, colour, index;
            Assert.That(file.TryAccessor(attributes.GetInt("POSITION"), out position), Is.True);
            Assert.That(file.TryAccessor(attributes.GetInt("NORMAL"), out normal), Is.True);
            Assert.That(file.TryAccessor(attributes.GetInt("TEXCOORD_0"), out uv), Is.True);
            Assert.That(file.TryAccessor(attributes.GetInt("COLOR_0"), out colour), Is.True);
            Assert.That(file.TryAccessor(primitive.GetInt("indices"), out index), Is.True);

            var mesh = new MeshData();
            Assert.That(GlbMeshReader.TryRead(glb, glb.Length, mesh), Is.True);
            Assert.That(mesh.VertexCount, Is.EqualTo(position.Count));
            Assert.That(mesh.IndexCount, Is.EqualTo(index.Count));

            bool offCentre = false;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int p = position.Offset + v * position.Stride, n = normal.Offset + v * normal.Stride;
                int t = uv.Offset + v * uv.Stride, c = colour.Offset + v * colour.Stride;
                AtlasVertex vertex = mesh.Vertices[v];
                Assert.That(vertex.Position.x, Is.EqualTo(-file.Float(p)));
                Assert.That(vertex.Position.y, Is.EqualTo(file.Float(p + 4)));
                Assert.That(vertex.Position.z, Is.EqualTo(file.Float(p + 8)));
                Assert.That(vertex.Normal.x, Is.EqualTo(-file.Float(n)));
                Assert.That(vertex.Normal.y, Is.EqualTo(file.Float(n + 4)));
                Assert.That(vertex.Normal.z, Is.EqualTo(file.Float(n + 8)));
                Assert.That(vertex.Uv0.x, Is.EqualTo(file.Float(t)));
                Assert.That(vertex.Uv0.y, Is.EqualTo(1f - file.Float(t + 4)));
                Assert.That(vertex.Color & 255u, Is.EqualTo((uint)glb[c]), "R is the baked shade");
                Assert.That(vertex.Color >> 24, Is.EqualTo((uint)glb[c + 3]));
                offCentre |= math.abs(vertex.Position.x) > 0.01f;
            }
            Assert.That(offCentre, Is.True, "the fixture has vertices off the mirror plane, so the sign is tested");

            for (int k = 0; k < mesh.IndexCount; k += 3)
            {
                Assert.That((uint)mesh.Indices[k], Is.EqualTo(file.Index(in index, k)));
                Assert.That((uint)mesh.Indices[k + 1], Is.EqualTo(file.Index(in index, k + 2)));
                Assert.That((uint)mesh.Indices[k + 2], Is.EqualTo(file.Index(in index, k + 1)));
            }
        }

        /// <summary>Only the accessor types of section 4.1 are read; another valid glTF encoding is refused.</summary>
        [Test]
        public void Glb_MeshReader_RefusesAccessorTypesOutsideTheContract()
        {
            byte[] glb = Bytes("forge_table.glb.bytes");
            // COLOR_0 is the file's only UNSIGNED_BYTE accessor: declare it UNSIGNED_SHORT in the JSON chunk.
            int jsonLength = BitConverter.ToInt32(glb, 12), at = -1, found = 0;
            for (int i = 20; i + 4 <= 20 + jsonLength; i++)
                if (glb[i] == '5' && glb[i + 1] == '1' && glb[i + 2] == '2' && glb[i + 3] == '1') { at = i; found++; }
            Assert.That(found, Is.EqualTo(1));
            glb[at + 3] = (byte)'3';

            var mesh = new MeshData();
            Assert.That(GlbMeshReader.TryRead(glb, glb.Length, mesh), Is.False);
            Assert.That(mesh.VertexCount, Is.EqualTo(0));
            Assert.That(mesh.IndexCount, Is.EqualTo(0));
        }

        /// <summary>The file that came back from a running Forge over HTTP (Fixtures/Tools~/forge_round_trip.py).</summary>
        [Test]
        public void Glb_RoundTrip_TheForgesAnswerPassesThePhonesValidator()
        {
            JsonValue trip = AssetFixtures.Json("roundtrip.json");
            byte[] glb = AssetFixtures.Glb("roundtrip");
            float3 dims = AssetFixtures.Dims(trip);
            GlbExpectation expect = AssetFixtures.Expect(dims, trip.GetInt("triTarget"), trip.GetString("sha256"));

            GlbReport report = GlbValidator.Validate(glb, in expect);
            Assert.That(report.Ok, Is.True, "rejected with " + report.FailedCheck);
            // What the Forge reported in its JobStatus is what the phone measures.
            Assert.That(report.Bytes, Is.EqualTo(trip.GetInt("bytes")));
            Assert.That(report.Triangles, Is.EqualTo(trip.GetInt("tris")));
            Assert.That(report.MaxEdge, Is.EqualTo(trip.GetFloat("maxEdge")).Within(1e-4f));
            Assert.That(report.BoundsX, Is.EqualTo(trip["bounds"][0].AsFloat()).Within(1e-4f));
            Assert.That(report.BoundsY, Is.EqualTo(trip["bounds"][1].AsFloat()).Within(1e-4f));
            Assert.That(report.BoundsZ, Is.EqualTo(trip["bounds"][2].AsFloat()).Within(1e-4f));

            var mesh = new MeshData();
            Assert.That(GlbMeshReader.TryRead(glb, glb.Length, mesh), Is.True);
            AssertIsFurniture(mesh, dims, report.Triangles, report.Vertices);
            // The Forge's palette cell arrives as the look's palette coordinate.
            float2 uv = FallbackMeshBuilder.PaletteUv(trip.GetInt("palette"));
            Assert.That(mesh.Vertices[0].Uv0.x, Is.EqualTo(uv.x).Within(1e-5f));
            Assert.That(mesh.Vertices[0].Uv0.y, Is.EqualTo(uv.y).Within(1e-5f));
        }

        static void AssertIsFurniture(MeshData mesh, float3 dims, int triangles, int vertices)
        {
            Assert.That(mesh.TriangleCount, Is.EqualTo(triangles));
            Assert.That(mesh.VertexCount, Is.EqualTo(vertices));
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.2505f));

            // Box-local frame: centred on x and z, standing on y = 0, the size of the box.
            Assert.That(mesh.BoundsMin.y, Is.EqualTo(0f).Within(0.02f));
            Assert.That(mesh.BoundsMax.y, Is.EqualTo(dims.y).Within(0.02f));
            Assert.That(mesh.BoundsMin.x, Is.EqualTo(-0.5f * dims.x).Within(0.02f));
            Assert.That(mesh.BoundsMax.x, Is.EqualTo(0.5f * dims.x).Within(0.02f));
            Assert.That(mesh.BoundsMin.z, Is.EqualTo(-0.5f * dims.z).Within(0.02f));
            Assert.That(mesh.BoundsMax.z, Is.EqualTo(0.5f * dims.z).Within(0.02f));

            float2 uv0 = mesh.Vertices[0].Uv0;
            int column = (int)math.floor(uv0.x * 16f), row = (int)math.floor(uv0.y * 16f);
            Assert.That(row, Is.InRange(4, 7), "an object colour: palette rows 4 to 7");
            Assert.That(uv0.x, Is.EqualTo((column + 0.5f) / 16f).Within(1e-5f));
            Assert.That(uv0.y, Is.EqualTo((row + 0.5f) / 16f).Within(1e-5f));

            for (int v = 0; v < mesh.VertexCount; v++)
            {
                AtlasVertex vertex = mesh.Vertices[v];
                Assert.That(math.length(vertex.Normal), Is.EqualTo(1f).Within(0.01f));
                Assert.That(vertex.Color >> 8, Is.EqualTo(0xFFFFFFu), "line weight and the reserved bytes are 255");
                // Shade floor 0.55 times the deepest occlusion 0.35: never darker than 49.
                Assert.That(vertex.Color & 255u, Is.GreaterThanOrEqualTo(48u), "a baked shade, never black");
                Assert.That(vertex.Uv0.x, Is.EqualTo(uv0.x));
                Assert.That(vertex.Uv0.y, Is.EqualTo(uv0.y));
                Assert.That(vertex.Uv1.x, Is.EqualTo(0f));
            }

            // Unity's winding: the face normal cross(b - a, c - a) points the way the vertex normals do.
            int agree = 0;
            for (int k = 0; k < mesh.IndexCount; k += 3)
            {
                AtlasVertex a = mesh.Vertices[mesh.Indices[k]], b = mesh.Vertices[mesh.Indices[k + 1]], c = mesh.Vertices[mesh.Indices[k + 2]];
                float3 face = math.cross(b.Position - a.Position, c.Position - a.Position);
                if (math.dot(face, a.Normal + b.Normal + c.Normal) > 0f) agree++;
            }
            Assert.That(agree, Is.EqualTo(mesh.TriangleCount), "every triangle faces outwards after the mirror in x");
        }
    }
}
