using System;
using System.IO;
using System.Text;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The kit pieces modelled in Blender (kit hero assets design, 2026-10-03): the fixture
    /// tools/kit/make_test_fixture.py writes, the refusals, and the pieces the build ships once they exist.
    /// </summary>
    public class KitSetTests
    {
        // AtlasPalette.Uv(Flag) and AtlasPalette.Uv(Landmark); this assembly does not reference Atlas.Rendering.
        static readonly float2 FlagUv = new float2(3.5f / 16f, 12.5f / 16f);
        static readonly float2 LandmarkUv = new float2(13.5f / 16f, 1.5f / 16f);

        static Func<string, byte[]> Fixture() { return AssetFixtures.KitReader(AssetFixtures.KitFixtureDirectory()); }

        static byte[] FixtureGlb() { return Fixture()("probe.glb"); }

        static byte[] Manifest(string name, byte[] glb, int triangles = 12)
        {
            string json = "{\"pieces\":{\"" + name + "\":{\"triangles\":" + triangles + ",\"sha256\":\""
                + GlbValidator.Sha256Hex(glb, glb.Length) + "\"}}}";
            return Encoding.UTF8.GetBytes(json);
        }

        // A reader with one manifest and one piece file, as Resources would give them.
        static Func<string, byte[]> Reader(byte[] manifest, string name, byte[] glb)
        {
            return file => file == KitSet.ManifestName ? manifest : file == name + ".glb" ? glb : null;
        }

        static void AssertFacesAgreeWithNormals(MeshData mesh, string label)
        {
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                AtlasVertex a = mesh.Vertices[mesh.Indices[t]], b = mesh.Vertices[mesh.Indices[t + 1]], c = mesh.Vertices[mesh.Indices[t + 2]];
                float3 front = math.cross(b.Position - a.Position, c.Position - a.Position);
                Assert.That(math.dot(front, a.Normal + b.Normal + c.Normal), Is.GreaterThan(0f), label + " triangle " + t / 3);
            }
        }

        [Test]
        public void KitSet_ReadsLineCoordinates()
        {
            KitSet set = KitSet.Load(Fixture());
            Assert.That(set.LastError, Is.Null);
            Assert.That(set.Count, Is.EqualTo(1));

            var mesh = new MeshData(8, 8);
            Assert.That(set.TryGet("probe", mesh, out string error), Is.True, error);
            Assert.That(error, Is.Null);
            Assert.That(mesh.VertexCount, Is.EqualTo(24));
            Assert.That(mesh.TriangleCount, Is.EqualTo(12));
            AssertFacesAgreeWithNormals(mesh, "probe");
            Assert.That(math.cmax(math.abs(mesh.BoundsMin - new float3(-0.1f, 0f, -0.1f))), Is.LessThan(1e-6f));
            Assert.That(math.cmax(math.abs(mesh.BoundsMax - new float3(0.1f, 0.2f, 0.1f))), Is.LessThan(1e-6f));

            int top = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                AtlasVertex vertex = mesh.Vertices[v];
                float3 p = vertex.Position;
                // The script wrote uv1 = (10 x + 1, 10 y + 2) in Unity's frame; v was stored downwards and comes back.
                Assert.That(vertex.Uv1.x, Is.EqualTo(10f * p.x + 1f).Within(1e-5f), "vertex " + v);
                Assert.That(vertex.Uv1.y, Is.EqualTo(10f * p.y + 2f).Within(1e-5f), "vertex " + v);
                Assert.That(vertex.LineWeight, Is.EqualTo(255));
                Assert.That(vertex.Color >> 16, Is.EqualTo(0xFFFFu), "B and A reserved at 255");
                Assert.That(vertex.Shade, Is.EqualTo(AtlasShading.ToByte(AtlasShading.FaceShade(vertex.Normal))).Within(1));
                if (vertex.Normal.y > 0.5f)
                {
                    top++;
                    Assert.That(vertex.Uv0, Is.EqualTo(FlagUv));
                }
                else Assert.That(vertex.Uv0, Is.EqualTo(LandmarkUv));
            }
            Assert.That(top, Is.EqualTo(4));
        }

        [Test]
        public void KitSet_RefusesBadInput_WithAReason()
        {
            byte[] glb = FixtureGlb();
            var mesh = new MeshData(8, 8);
            string error;

            // No manifest, or no reader at all: every piece is refused, nothing throws.
            KitSet none = KitSet.Load(file => null);
            Assert.That(none.LastError, Does.Contain("manifest"));
            Assert.That(none.TryGet("probe", mesh, out error), Is.False);
            Assert.That(error, Does.Contain("manifest"));
            Assert.That(KitSet.Load(null).TryGet("probe", mesh, out error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(KitSet.Load(Reader(Encoding.UTF8.GetBytes("not json"), "probe", glb)).LastError, Does.Contain("manifest"));

            // The manifest names a piece whose file is missing.
            KitSet missing = KitSet.Load(Reader(Manifest("probe", glb), "other", glb));
            Assert.That(missing.TryGet("probe", mesh, out error), Is.False);
            Assert.That(error, Does.Contain("probe").And.Contain("missing"));

            // A name the manifest does not list.
            KitSet good = KitSet.Load(Fixture());
            Assert.That(good.TryGet("nope", mesh, out error), Is.False);
            Assert.That(error, Does.Contain("nope"));
            Assert.That(good.TryGet(null, mesh, out error), Is.False);
            Assert.That(good.TryGet("probe", null, out error), Is.False);

            // One byte changed after the manifest was written.
            byte[] changed = (byte[])glb.Clone();
            changed[changed.Length - 40] ^= 0x5A;
            KitSet tampered = KitSet.Load(Reader(Manifest("probe", glb), "probe", changed));
            Assert.That(tampered.TryGet("probe", mesh, out error), Is.False);
            Assert.That(error, Does.Contain("probe").And.Contain("hash"));

            // Broken bytes whose hash the manifest agrees with: the hash is right, the file is not a mesh.
            byte[] broken = new byte[glb.Length / 2];
            Array.Copy(glb, broken, broken.Length);
            KitSet unreadable = KitSet.Load(Reader(Manifest("probe", broken), "probe", broken));
            Assert.That(unreadable.TryGet("probe", mesh, out error), Is.False);
            Assert.That(error, Does.Contain("probe").And.Contain("cannot be read"));

            // A manifest whose triangle count disagrees with the file.
            KitSet miscounted = KitSet.Load(Reader(Manifest("probe", glb, 13), "probe", glb));
            Assert.That(miscounted.TryGet("probe", mesh, out error), Is.False);
            Assert.That(error, Does.Contain("triangles"));

            // A refusal leaves the mesh empty, so a caller never draws half a piece.
            Assert.That(good.TryGet("probe", mesh, out error), Is.True, error);
            Assert.That(tampered.TryGet("probe", mesh, out error), Is.False);
            Assert.That(mesh.VertexCount, Is.EqualTo(0));
            Assert.That(mesh.IndexCount, Is.EqualTo(0));
        }

        [Test]
        public void KitSet_ReadsTheShippedPieces()
        {
            string directory = AssetFixtures.ShippedKitDirectory();
            if (!File.Exists(Path.Combine(directory, "manifest.json")))
                Assert.Ignore("No kit pieces shipped yet: run tools/kit/export_kit.py on art/blender/ATLAS_Kit.blend.");

            KitSet set = KitSet.Load(AssetFixtures.KitReader(directory));
            Assert.That(set.LastError, Is.Null);
            var mesh = new MeshData();
            foreach (string name in new[] { "landmark", "avatar" })
            {
                Assert.That(set.TryGet(name, mesh, out string error), Is.True, name + ": " + error);
                Assert.That(mesh.TriangleCount, Is.InRange(1, name == "landmark" ? 1500 : 600), name);
                AssertFacesAgreeWithNormals(mesh, name);
            }
        }
    }
}
