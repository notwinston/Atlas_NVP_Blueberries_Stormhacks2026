using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Objects.Tests.ObjectsTestData;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// PropFitter.Build (docs/plans/standalone-phone/INDEX.md section 6.4 and T5): quarter turns, the Forge's scale
    /// formula (stretch clipped at a = 0.15), the origin at the base centre, edges split to 0.25 m after scaling
    /// without cracks, shade baked, COLOR.b = 255, the triangle cap. Checked against reference.json's scale vectors.
    /// </summary>
    public class PropFitterTests
    {
        const int Plenty = 1000000;

        [Test]
        public void Constants_AreThePlans()
        {
            Assert.That(PropFitter.MaxAnisotropy, Is.EqualTo(0.15f));
            Assert.That(PropFitter.MaxAnisotropy, Is.EqualTo(Reference["constants"].GetFloat("maxAnisotropy")));
            Assert.That(PropFitter.PropMaxEdge, Is.EqualTo(0.25f));
        }

        static MeshData Fit(PropEntry prop, int quarterTurns, float3 size, int maxTriangles, out bool ok)
        {
            var match = new PropMatch { Index = 0, QuarterTurns = quarterTurns, Fit = 0f, Score = 0f };
            var into = new MeshData();
            ok = PropFitter.Build(prop, in match, size, maxTriangles, into);
            return into;
        }

        // ------------------------------------------------------------------ reference.json: scale

        static IEnumerable<TestCaseData> ScaleVectors()
        {
            JsonValue v = Reference["scale"];
            for (int k = 0; k < v.Count; k++)
                yield return new TestCaseData(k).SetName("Reference_Scale_" + k + "_" + v[k].GetString("prop")
                                                         + "_turn" + v[k].GetInt("quarterTurns"));
        }

        /// <summary>The fitted mesh's extent and bounds are reference.py's (the Forge's scale formula, then the
        /// origin to the base centre): boundsMin and boundsMax within 1e-4.</summary>
        [TestCaseSource(nameof(ScaleVectors))]
        public void Reference_Scale_Bounds(int k)
        {
            JsonValue v = Reference["scale"][k];
            PropEntry prop = FixtureProp(v.GetString("prop"));
            MeshData fitted = Fit(prop, v.GetInt("quarterTurns"), Float3(v["box"]), Plenty, out bool ok);
            Assert.That(ok, Is.True);
            UsedBounds(fitted, out float3 min, out float3 max);
            float3 eMin = Float3(v["boundsMin"]), eMax = Float3(v["boundsMax"]);
            for (int c = 0; c < 3; c++)
            {
                Assert.That(min[c], Is.EqualTo(eMin[c]).Within(Tolerance), "boundsMin[" + c + "]");
                Assert.That(max[c], Is.EqualTo(eMax[c]).Within(Tolerance), "boundsMax[" + c + "]");
            }
        }

        /// <summary>
        /// The profile of the fitted mesh at its own extent is rotate(prop profile, k) (reference.json
        /// conventions.quarterTurn): the turn goes the same way as the matcher's rotate, so a matched prop faces
        /// the way its profile said.
        /// </summary>
        [TestCaseSource(nameof(ScaleVectors))]
        public void Reference_Scale_FittedProfile(int k)
        {
            JsonValue v = Reference["scale"][k];
            PropEntry prop = FixtureProp(v.GetString("prop"));
            MeshData fitted = Fit(prop, v.GetInt("quarterTurns"), Float3(v["box"]), Plenty, out bool ok);
            Assert.That(ok, Is.True);
            Arrays(fitted, out float3[] positions, out int[] indices);
            byte[] profile = ObjectProfile.Compute(positions, indices, null, float3.zero, Float3(v["extentOut"]), 0f);
            Assert.That(Hex(profile), Is.EqualTo(Hex(Profile(v["fittedProfile"]))));
        }

        // ------------------------------------------------------------------ edge rule after scaling

        static IEnumerable<TestCaseData> PropsAndScales()
        {
            foreach (string id in new[] { "chair_box_min", "table_slab_min", "couch_box3_min" })
                foreach (float s in new[] { 0.3f, 1f, 2f, 3f })
                    foreach (int turns in new[] { 0, 1 })
                        yield return new TestCaseData(id, s, turns).SetName("Edges_" + id + "_x" + s + "_turn" + turns);
        }

        /// <summary>Every edge is at most 0.25 m after scaling, and the split leaves no crack: the fixture props
        /// are closed, so every edge (by position) is used by an even number of triangles before and after.</summary>
        [TestCaseSource(nameof(PropsAndScales))]
        public void Edges_AtMostAQuarterMetre_AndNoCracks(string id, float scale, int turns)
        {
            PropEntry prop = FixtureProp(id);
            Assume.That(OddEdges(prop.Mesh), Is.EqualTo(0), "the fixture prop is closed");
            float3 size = prop.Size * scale;
            if ((turns & 1) == 1) size = size.zyx;
            MeshData fitted = Fit(prop, turns, size, Plenty, out bool ok);
            Assert.That(ok, Is.True);
            AssertIndicesValid(fitted, id);
            Assert.That(fitted.TriangleCount, Is.GreaterThanOrEqualTo(prop.Mesh.TriangleCount));
            Assert.That(fitted.LongestEdge(), Is.LessThanOrEqualTo(PropFitter.PropMaxEdge + 1e-5f));
            Assert.That(OddEdges(fitted), Is.EqualTo(0), "T-junctions after the split");
        }

        [Test]
        public void Split_KeepsTheSurfaceArea()
        {
            // At 1x with no clip, the fitted area equals the reference mesh's (the split adds no surface).
            PropEntry prop = FixtureProp("couch_box3_min");
            MeshData atOne = Fit(prop, 0, prop.Size, Plenty, out bool ok1);
            Assert.That(ok1, Is.True);
            Assert.That(Area(atOne), Is.EqualTo(Area(prop.Mesh)).Within(1e-3f * Area(prop.Mesh)));
            // Uniform 2x: four times the area.
            MeshData atTwo = Fit(prop, 0, prop.Size * 2f, Plenty, out bool ok2);
            Assert.That(ok2, Is.True);
            Assert.That(Area(atTwo), Is.EqualTo(4f * Area(prop.Mesh)).Within(4e-3f * Area(prop.Mesh)));
        }

        // ------------------------------------------------------------------ vertex attributes

        [Test]
        public void Vertices_KeepSlotCells_ShadeBaked_BlueIs255()
        {
            byte lowest = AtlasShading.ToByte(AtlasShading.ShadeFloor, 0.7f);
            foreach (PropEntry prop in FixtureProps)
            {
                MeshData fitted = Fit(prop, 1, prop.Size.zyx * 1.5f, Plenty, out bool ok);
                Assert.That(ok, Is.True, prop.Id);
                var allowed = new HashSet<int>();
                foreach (byte c in prop.SlotCells) allowed.Add(c);
                var used = new HashSet<int>();
                for (int k = 0; k < fitted.VertexCount; k++)
                {
                    AtlasVertex v = fitted.Vertices[k];
                    int cell = Cell(v.Uv0);
                    if (!allowed.Contains(cell)) Assert.Fail(prop.Id + ": vertex " + k + " has cell " + cell + ", not a slot default");
                    used.Add(cell);
                    if (((v.Color >> 16) & 255u) != 255u) Assert.Fail(prop.Id + ": vertex " + k + " COLOR.b is not 255");
                    if (v.Shade < lowest) Assert.Fail(prop.Id + ": vertex " + k + " shade " + v.Shade + " below " + lowest);
                    if (math.abs(math.length(v.Normal) - 1f) > 1e-3f) Assert.Fail(prop.Id + ": vertex " + k + " normal not unit");
                }
                Assert.That(used.Count, Is.EqualTo(prop.SlotCells.Length), prop.Id + ": every slot survives the fit");
            }
        }

        [Test]
        public void Build_DoesNotChangeTheProp()
        {
            PropEntry prop = FixtureProp("table_slab_min");
            MeshData before = Copy(prop.Mesh);
            Fit(prop, 3, new float3(1.9f, 0.8f, 2.6f), Plenty, out bool ok);
            Assert.That(ok, Is.True);
            AssertMeshEqual(before, prop.Mesh, "prop mesh after a fit");
        }

        // ------------------------------------------------------------------ cap, clearing, determinism

        [Test]
        public void TriangleCap_Exceeded_ReturnsFalse()
        {
            PropEntry prop = FixtureProp("couch_box3_min");
            Fit(prop, 0, prop.Size * 3f, Plenty, out bool okBig);
            Assert.That(okBig, Is.True);
            MeshData fitted = Fit(prop, 0, prop.Size * 3f, prop.Mesh.TriangleCount, out bool ok);
            Assert.That(ok, Is.False, "3x the couch needs more triangles than the reference mesh has");
        }

        [Test]
        public void TriangleCap_Met_ReturnsTrue()
        {
            PropEntry prop = FixtureProp("chair_box_min");
            MeshData free = Fit(prop, 0, prop.Size * 2f, Plenty, out bool ok1);
            Assert.That(ok1, Is.True);
            MeshData capped = Fit(prop, 0, prop.Size * 2f, free.TriangleCount, out bool ok2);
            Assert.That(ok2, Is.True, "exactly at the cap is allowed");
            Assert.That(capped.TriangleCount, Is.EqualTo(free.TriangleCount));
        }

        [Test]
        public void Into_IsClearedFirst()
        {
            PropEntry prop = FixtureProp("chair_box_min");
            MeshData fresh = Fit(prop, 0, prop.Size, Plenty, out bool ok1);
            Assert.That(ok1, Is.True);

            var into = new MeshData();
            into.AddBox(new float3(5f, 0f, 5f), new float3(1f), 0f, PaletteUv(64));
            var match = new PropMatch { Index = 0, QuarterTurns = 0 };
            Assert.That(PropFitter.Build(prop, in match, prop.Size, Plenty, into), Is.True);
            AssertMeshEqual(fresh, into, "build into a used MeshData");
        }

        [Test]
        public void Build_IsDeterministic()
        {
            foreach (PropEntry prop in FixtureProps)
            {
                float3 size = new float3(prop.Size.z * 1.7f, prop.Size.y * 1.2f, prop.Size.x * 2.1f);
                MeshData a = Fit(prop, 1, size, Plenty, out bool ok1);
                MeshData b = Fit(prop, 1, size, Plenty, out bool ok2);
                Assert.That(ok1 && ok2, Is.True);
                AssertMeshEqual(a, b, prop.Id);
            }
        }

        [Test]
        public void EmptyPropMesh_NoException()
        {
            PropEntry empty = SyntheticProp("empty", ObjectCategory.Chair, new float3(0.5f, 0.9f, 0.5f), new byte[] { 64 },
                                            new MeshData());
            var match = new PropMatch { Index = 0, QuarterTurns = 0 };
            var into = new MeshData();
            Assert.DoesNotThrow(() => PropFitter.Build(empty, in match, new float3(0.5f, 0.9f, 0.5f), Plenty, into));
            Assert.That(into.TriangleCount, Is.EqualTo(0));
        }
    }
}
