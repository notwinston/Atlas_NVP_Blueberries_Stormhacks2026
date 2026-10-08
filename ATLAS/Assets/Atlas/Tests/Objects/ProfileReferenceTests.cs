using Atlas.Link;
using Atlas.Scan;
using Atlas.Scan.Grid;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Objects.Tests.ObjectsTestData;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// The profile convention shared by the scan and the prop library (docs/plans/standalone-phone/INDEX.md,
    /// T3 constraint): the scan's ObjectProfile must give, on the same mesh, the bytes the Forge's of_mesh gave
    /// in reference.json, so prop profiles and scanned profiles can be compared cell by cell.
    /// </summary>
    public class ProfileReferenceTests
    {
        [Test]
        public void Reference_Profiles_OfTheFixturePropMeshes()
        {
            JsonValue profiles = Reference["profiles"];
            Assert.That(profiles.Count, Is.EqualTo(3));
            for (int k = 0; k < profiles.Count; k++)
            {
                JsonValue v = profiles[k];
                string id = v.GetString("prop");
                PropEntry prop = FixtureProp(id);
                byte[] expected = Profile(v["profile"]);
                Assert.That(Hex(prop.Profile), Is.EqualTo(Hex(expected)), id + ": manifest profile");
                Assert.That(prop.Size.x, Is.EqualTo(Float3(v["size"]).x).Within(Tolerance), id);
                Assert.That(prop.Size.y, Is.EqualTo(Float3(v["size"]).y).Within(Tolerance), id);
                Assert.That(prop.Size.z, Is.EqualTo(Float3(v["size"]).z).Within(Tolerance), id);

                Arrays(prop.Mesh, out float3[] positions, out int[] indices);
                byte[] computed = ObjectProfile.Compute(positions, indices, null, float3.zero, prop.Size, 0f);
                Assert.That(Hex(computed), Is.EqualTo(Hex(expected)), id + ": ObjectProfile.Compute of the GLB mesh");
                Assert.That(ObjectProfile.Coverage(computed), Is.EqualTo(v.GetFloat("coverage")).Within(Tolerance), id);
            }
        }

        [Test]
        public void Reference_Convention_ScanProfileEqualsForgeProfile()
        {
            JsonValue vectors = Reference["convention"];
            Assert.That(vectors.Count, Is.GreaterThan(0));
            for (int n = 0; n < vectors.Count; n++)
            {
                JsonValue v = vectors[n];
                JsonValue vs = v["vertices"], ix = v["indices"], cl = v["classes"];
                var positions = new float3[vs.Count / 3];
                for (int k = 0; k < positions.Length; k++)
                    positions[k] = new float3(vs[3 * k].AsFloat(), vs[3 * k + 1].AsFloat(), vs[3 * k + 2].AsFloat());
                var indices = new int[ix.Count];
                for (int k = 0; k < indices.Length; k++) indices[k] = ix[k].AsInt();
                SurfaceClass[] classes = null;
                if (cl.IsArray)
                {
                    classes = new SurfaceClass[cl.Count];
                    for (int k = 0; k < classes.Length; k++) classes[k] = (SurfaceClass)cl[k].AsInt();
                }
                byte[] computed = ObjectProfile.Compute(positions, indices, classes, Float3(v["boxBase"]),
                                                        Float3(v["boxSize"]), v.GetFloat("boxYaw"));
                Assert.That(Hex(computed), Is.EqualTo(Hex(Profile(v["profile"]))), v.GetString("name"));
            }
        }

        [Test]
        public void Reference_Rotate_ObjectProfileRotate()
        {
            byte[] source = FixtureProp(Reference.GetString("rotateSource")).Profile;
            JsonValue rotate = Reference["rotate"];
            Assert.That(rotate.Count, Is.EqualTo(4));
            for (int k = 0; k < rotate.Count; k++)
            {
                int turns = rotate[k].GetInt("turns");
                Assert.That(Hex(ObjectProfile.Rotate(source, turns)), Is.EqualTo(Hex(Profile(rotate[k]["profile"]))),
                            "turns " + turns);
            }
        }
    }
}
