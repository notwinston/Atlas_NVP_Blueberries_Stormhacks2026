using System;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Objects.Tests.ObjectsTestData;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// ScanCut (docs/plans/standalone-phone/INDEX.md section 6.4, D8 and T7) on a synthetic scan: a finely
    /// meshed box standing on a floor mesh, plus clutter elsewhere. The cut keeps the box's triangles (not the
    /// Floor-class ones), in box-local coordinates, within the triangle cap, edges at most 0.25 m, shade baked.
    /// </summary>
    public class ScanCutTests
    {
        static readonly float3 BoxBase = new float3(1.0f, 0f, -0.5f);
        static readonly float3 BoxSize = new float3(0.6f, 0.5f, 0.4f);
        const float BoxYaw = 0.5f;

        [Test]
        public void Constants_AreThePlans()
        {
            Assert.That(ScanCut.Margin, Is.EqualTo(0.03f));
            Assert.That(ScanCut.MinTriangles, Is.EqualTo(200));
        }

        static RoomObject Object()
        {
            return new RoomObject
            {
                Id = 7, Category = ObjectCategory.Unknown, Base = BoxBase, Size = BoxSize, Yaw = BoxYaw,
                Mesh = MeshState.Placeholder, Facing = ObjectFacingRule.Box,
            };
        }

        /// <summary>
        /// The scan: the object's box (no bottom, 5 cm cells, class Seat) first, then, when withSurroundings,
        /// a 4 m floor at y = 0 (10 cm cells, class Floor) and a second box 1.5 m away (class Table).
        /// </summary>
        static ScanMesh Scene(bool withSurroundings, float boxCell = 0.05f)
        {
            var parts = new MeshData();
            parts.AddBox(BoxBase, BoxSize, BoxYaw, PaletteUv(64), withBottom: false, maxEdge: boxCell);
            int boxTriangles = parts.TriangleCount;
            int floorTriangles = 0;
            if (withSurroundings)
            {
                int before = parts.TriangleCount;
                // AddQuadGrid's front is cross(v, u): cross(z, x) = +Y.
                parts.AddQuadGrid(new float3(-2f, 0f, -2f), new float3(4f, 0f, 0f), new float3(0f, 0f, 4f), PaletteUv(64),
                                  float2.zero, float2.zero, float2.zero, 1f, 255, 0.1f);
                floorTriangles = parts.TriangleCount - before;
                parts.AddBox(new float3(-0.8f, 0f, 0.6f), new float3(0.5f, 0.7f, 0.5f), 0f, PaletteUv(64), maxEdge: 0.05f);
            }
            int clutterTriangles = parts.TriangleCount - boxTriangles - floorTriangles;

            Arrays(parts, out float3[] positions, out int[] indices);
            var classes = new byte[parts.TriangleCount];
            for (int t = 0; t < classes.Length; t++)
                classes[t] = t < boxTriangles ? ScanMesh.ClassSeat
                           : t < boxTriangles + floorTriangles ? ScanMesh.ClassFloor
                           : ScanMesh.ClassTable;
            Assume.That(clutterTriangles, Is.GreaterThanOrEqualTo(0));
            return new ScanMesh(positions, indices, classes);
        }

        static MeshData Cut(ScanMesh scan, RoomObject obj, int maxTriangles, out bool ok)
        {
            var into = new MeshData();
            ok = ScanCut.Build(scan, obj, maxTriangles, into);
            return into;
        }

        // ------------------------------------------------------------------ the cut

        [TestCase(3000)]
        [TestCase(300)]
        public void Build_BoxOnFloor_BoxLocal_WithinCapAndEdgeRule(int maxTriangles)
        {
            MeshData cut = Cut(Scene(true), Object(), maxTriangles, out bool ok);
            Assert.That(ok, Is.True);
            AssertIndicesValid(cut, "cut");
            Assert.That(cut.TriangleCount, Is.GreaterThan(0));
            Assert.That(cut.TriangleCount, Is.LessThanOrEqualTo(maxTriangles));
            Assert.That(cut.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-5f));

            // Box-local, clipped to y >= 0: inside the box grown by the margin.
            UsedBounds(cut, out float3 min, out float3 max);
            float m = ScanCut.Margin + 1e-4f;
            Assert.That(min.x, Is.GreaterThanOrEqualTo(-BoxSize.x * 0.5f - m));
            Assert.That(max.x, Is.LessThanOrEqualTo(BoxSize.x * 0.5f + m));
            Assert.That(min.z, Is.GreaterThanOrEqualTo(-BoxSize.z * 0.5f - m));
            Assert.That(max.z, Is.LessThanOrEqualTo(BoxSize.z * 0.5f + m));
            Assert.That(min.y, Is.GreaterThanOrEqualTo(-1e-5f));
            Assert.That(max.y, Is.LessThanOrEqualTo(BoxSize.y + m));
        }

        /// <summary>With room for the detail, the cut still spans the box (a wrong yaw or origin would not):
        /// each side within 6 cm of the box's.</summary>
        [Test]
        public void Build_SpansTheBox_InItsOwnFrame()
        {
            MeshData cut = Cut(Scene(true), Object(), 3000, out bool ok);
            Assert.That(ok, Is.True);
            UsedBounds(cut, out float3 min, out float3 max);
            const float slack = 0.06f;
            Assert.That(min.x, Is.EqualTo(-BoxSize.x * 0.5f).Within(slack));
            Assert.That(max.x, Is.EqualTo(BoxSize.x * 0.5f).Within(slack));
            Assert.That(min.z, Is.EqualTo(-BoxSize.z * 0.5f).Within(slack));
            Assert.That(max.z, Is.EqualTo(BoxSize.z * 0.5f).Within(slack));
            Assert.That(max.y, Is.EqualTo(BoxSize.y).Within(slack));
            Assert.That(min.y, Is.EqualTo(0f).Within(slack));
        }

        /// <summary>The floor under the box and the clutter elsewhere change nothing: the cut of the box alone and
        /// the cut of the whole scene are the same, byte for byte (the box comes first in both).</summary>
        [Test]
        public void Build_FloorAndClutter_AreLeftOut()
        {
            MeshData alone = Cut(Scene(false), Object(), 3000, out bool ok1);
            MeshData inScene = Cut(Scene(true), Object(), 3000, out bool ok2);
            Assert.That(ok1 && ok2, Is.True);
            AssertMeshEqual(alone, inScene, "box alone vs box on the floor");
        }

        /// <summary>The floor lies at y = 0 exactly; the box's own triangles each reach 5 cm up. A triangle lying
        /// wholly on the floor plane can only be a floor triangle.</summary>
        [Test]
        public void Build_NoTriangleOnTheFloorPlane()
        {
            MeshData cut = Cut(Scene(true), Object(), 3000, out bool ok);
            Assert.That(ok, Is.True);
            for (int k = 0; k < cut.IndexCount; k += 3)
            {
                float top = math.max(cut.Vertices[cut.Indices[k]].Position.y,
                                     math.max(cut.Vertices[cut.Indices[k + 1]].Position.y, cut.Vertices[cut.Indices[k + 2]].Position.y));
                Assert.That(top, Is.GreaterThanOrEqualTo(0.005f), "a floor triangle at " + k / 3);
            }
        }

        [Test]
        public void Build_ShadeBaked_BlueIs255()
        {
            MeshData cut = Cut(Scene(true), Object(), 3000, out bool ok);
            Assert.That(ok, Is.True);
            byte lowest = AtlasShading.ToByte(AtlasShading.ShadeFloor, 0.7f);
            for (int k = 0; k < cut.VertexCount; k++)
            {
                AtlasVertex v = cut.Vertices[k];
                if (v.Shade < lowest) Assert.Fail("vertex " + k + " shade " + v.Shade);
                if (((v.Color >> 16) & 255u) != 255u) Assert.Fail("vertex " + k + " COLOR.b is not 255 (D9: 255 = not batched)");
            }
        }

        [Test]
        public void Build_IsDeterministic()
        {
            MeshData a = Cut(Scene(true), Object(), 400, out bool ok1);
            MeshData b = Cut(Scene(true), Object(), 400, out bool ok2);
            Assert.That(ok1 && ok2, Is.True);
            AssertMeshEqual(a, b, "cut twice");
        }

        [Test]
        public void Build_IntoIsClearedFirst()
        {
            MeshData fresh = Cut(Scene(true), Object(), 3000, out bool ok1);
            var into = new MeshData();
            into.AddBox(new float3(9f, 0f, 9f), new float3(1f), 0f, PaletteUv(64));
            Assert.That(ScanCut.Build(Scene(true), Object(), 3000, into), Is.True);
            Assert.That(ok1, Is.True);
            AssertMeshEqual(fresh, into, "cut into a used MeshData");
        }

        // ------------------------------------------------------------------ too little scan

        [Test]
        public void Build_FewerThan200TrianglesInside_ReturnsFalse()
        {
            // The same box meshed with 25 cm cells: 52 triangles.
            ScanMesh coarse = Scene(false, 0.25f);
            Assume.That(coarse.TriangleCount, Is.LessThan(ScanCut.MinTriangles));
            Assert.That(ScanCut.Build(coarse, Object(), 3000, new MeshData()), Is.False);
        }

        [Test]
        public void Build_OnlyFloorUnderTheBox_ReturnsFalse()
        {
            // Plenty of floor triangles inside the box, all Floor class.
            RoomObject obj = Object();
            obj.Base = new float3(-1.2f, 0f, -1.2f);
            Assert.That(ScanCut.Build(Scene(true), obj, 3000, new MeshData()), Is.False);
        }

        [Test]
        public void Build_EmptyMesh_ReturnsFalse_NoException()
        {
            var empty = new ScanMesh(Array.Empty<float3>(), Array.Empty<int>(), null);
            bool ok = true;
            Assert.DoesNotThrow(() => ok = ScanCut.Build(empty, Object(), 3000, new MeshData()));
            Assert.That(ok, Is.False);

            var emptyClasses = new ScanMesh(Array.Empty<float3>(), Array.Empty<int>(), Array.Empty<byte>());
            Assert.DoesNotThrow(() => ok = ScanCut.Build(emptyClasses, Object(), 3000, new MeshData()));
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Build_ZeroSizedBox_ReturnsFalse_NoException()
        {
            RoomObject obj = Object();
            obj.Size = float3.zero;
            bool ok = true;
            Assert.DoesNotThrow(() => ok = ScanCut.Build(Scene(true), obj, 3000, new MeshData()));
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Build_NoClasses_StillCutsTheBox()
        {
            // A mesh without face classes (null): the box alone gives a cut.
            ScanMesh withClasses = Scene(false);
            var noClasses = new ScanMesh(withClasses.Vertices, withClasses.Indices, null);
            MeshData cut = Cut(noClasses, Object(), 3000, out bool ok);
            Assert.That(ok, Is.True);
            Assert.That(cut.TriangleCount, Is.GreaterThan(0));
        }
    }
}
