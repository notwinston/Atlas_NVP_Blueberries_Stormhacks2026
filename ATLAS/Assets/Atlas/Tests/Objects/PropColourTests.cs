using System;
using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Objects.Tests.ObjectsTestData;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// PropColour (docs/plans/standalone-phone/INDEX.md section 6.4, D7 and T6) on synthetic images. The camera
    /// convention is the Forge's (station/forge/forge/colour.py): CamToBox carries a camera point (+X right, +Y
    /// up, +Z forward) into the box frame, and a camera point (x, y, z), z > 0, lands on pixel
    /// (cx + fx x / z, cy - fy y / z), row 0 at the top of the crop.
    /// </summary>
    public class PropColourTests
    {
        const int Side = 128;
        const float Focal = 128f;

        [Test]
        public void MaxCells_IsSix()
        {
            Assert.That(PropColour.MaxCells, Is.EqualTo(6));
        }

        // ------------------------------------------------------------------ scene helpers

        /// <summary>A camera at (0, 0.5, distance) in the box frame looking along box -Z (towards the object),
        /// +Y up.</summary>
        static ObjectView FrontView(float distance = 2f)
        {
            float4x4 camToBox = float4x4.TRS(new float3(0f, 0.5f, distance), quaternion.RotateY(math.PI), new float3(1f));
            return new ObjectView
            {
                Jpeg = null, Width = Side, Height = Side, Score = 1f, AzimuthDeg = 0f,
                Intrinsics = new float4(Focal, Focal, Side * 0.5f, Side * 0.5f), CamToBox = camToBox,
            };
        }

        /// <summary>The same camera turned to look away from the object (along box +Z).</summary>
        static ObjectView LookingAway()
        {
            ObjectView v = FrontView();
            v.CamToBox = float4x4.TRS(new float3(0f, 0.5f, 2f), quaternion.identity, new float3(1f));
            return v;
        }

        static RgbImage Solid(byte[] rgb)
        {
            var image = new RgbImage { Width = Side, Height = Side, Rgb = new byte[Side * Side * 3] };
            for (int p = 0; p < Side * Side; p++)
            {
                image.Rgb[3 * p] = rgb[0];
                image.Rgb[3 * p + 1] = rgb[1];
                image.Rgb[3 * p + 2] = rgb[2];
            }
            return image;
        }

        /// <summary>Each row coloured by the box height y its centre looks at through FrontView (at z = 0):
        /// the band whose [lo, hi) holds y, else grey.</summary>
        static RgbImage Bands(float[] lo, float[] hi, int[] cells, float distance = 2f)
        {
            var image = new RgbImage { Width = Side, Height = Side, Rgb = new byte[Side * Side * 3] };
            for (int row = 0; row < Side; row++)
            {
                float y = 0.5f + (Side * 0.5f - (row + 0.5f)) * distance / Focal;
                byte[] rgb = { 128, 128, 128 };
                for (int b = 0; b < cells.Length; b++)
                    if (y >= lo[b] && y < hi[b]) rgb = CellSrgb(cells[b]);
                for (int col = 0; col < Side; col++)
                {
                    int p = 3 * (row * Side + col);
                    image.Rgb[p] = rgb[0];
                    image.Rgb[p + 1] = rgb[1];
                    image.Rgb[p + 2] = rgb[2];
                }
            }
            return image;
        }

        /// <summary>A quad in the plane z = z0 spanning x in [-0.2, 0.2] and y in [y0, y1], facing +Z (towards
        /// FrontView) or -Z, in one palette cell.</summary>
        static void AddPanel(MeshData m, float y0, float y1, float z0, bool facesCamera, int cell)
        {
            float2 none = float2.zero;
            var x = new float3(0.4f, 0f, 0f);
            var y = new float3(0f, y1 - y0, 0f);
            var origin = new float3(-0.2f, y0, z0);
            // MeshData.AddQuadGrid's front is cross(v, u): cross(x, y) = +Z, cross(y, x) = -Z.
            if (facesCamera) m.AddQuadGrid(origin, y, x, PaletteUv(cell), none, none, none, 1f, 255, 0.1f);
            else m.AddQuadGrid(origin, x, y, PaletteUv(cell), none, none, none, 1f, 255, 0.1f);
        }

        static PropEntry Prop(MeshData mesh, params byte[] slotCells)
        {
            return SyntheticProp("synthetic", ObjectCategory.Storage, new float3(0.4f, 1f, 0.4f), slotCells, mesh);
        }

        // ------------------------------------------------------------------ Choose

        static IEnumerable<int> SomeCells()
        {
            return new[] { 64, 66, 71, 76, 79, 87, 92, 100, 109, 111, 113, 120, 124, 127 };
        }

        [TestCaseSource(nameof(SomeCells))]
        public void Choose_SolidImage_GivesThatColoursCell(int cell)
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.55f, 0.9f, 0f, true, 70);
            AddPanel(mesh, 0.1f, 0.45f, 0f, true, 97);
            PropEntry prop = Prop(mesh, 70, 97);
            byte[] cells = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { Solid(CellSrgb(cell)) });
            Assert.That(cells, Is.EqualTo(new[] { (byte)cell, (byte)cell }));
        }

        [Test]
        public void Choose_ColourNearACell_GivesThatCell()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0f, true, 70);
            PropEntry prop = Prop(mesh, 70);
            byte[] rgb = CellSrgb(102);
            var off = new[] { (byte)math.clamp(rgb[0] + 3, 0, 255), (byte)math.clamp(rgb[1] - 2, 0, 255), (byte)math.clamp(rgb[2] + 2, 0, 255) };
            byte[] cells = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { Solid(off) });
            Assert.That(cells, Is.EqualTo(new byte[] { 102 }));
        }

        [Test]
        public void Choose_TwoBands_EachSlotTakesItsOwn()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.55f, 0.9f, 0f, true, 70);   // upper slot
            AddPanel(mesh, 0.1f, 0.45f, 0f, true, 97);   // lower slot
            PropEntry prop = Prop(mesh, 70, 97);
            RgbImage image = Bands(new[] { 0.5f, 0f }, new[] { 2f, 0.5f }, new[] { 83, 117 });
            byte[] cells = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { image });
            Assert.That(cells, Is.EqualTo(new byte[] { 83, 117 }));
        }

        [Test]
        public void Choose_SlotFacingAway_KeepsItsDefault()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0.1f, true, 70);    // seen
            AddPanel(mesh, 0.2f, 0.8f, -0.1f, false, 97);  // its back to the camera
            PropEntry prop = Prop(mesh, 70, 97);
            byte[] cells = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { Solid(CellSrgb(88)) });
            Assert.That(cells, Is.EqualTo(new byte[] { 88, 97 }));
        }

        [Test]
        public void Choose_ObjectBehindTheCamera_KeepsDefaults()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0f, true, 70);
            PropEntry prop = Prop(mesh, 70);
            byte[] cells = PropColour.Choose(prop, mesh, new[] { LookingAway() }, new[] { Solid(CellSrgb(88)) });
            Assert.That(cells, Is.EqualTo(new byte[] { 70 }));
        }

        [Test]
        public void Choose_NoViews_GivesTheDefaults_AsANewArray()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0f, true, 70);
            AddPanel(mesh, 0.2f, 0.8f, -0.1f, false, 97);
            PropEntry prop = Prop(mesh, 70, 97);
            byte[] cells = null;
            Assert.DoesNotThrow(() => cells = PropColour.Choose(prop, mesh, Array.Empty<ObjectView>(), Array.Empty<RgbImage>()));
            Assert.That(cells, Is.EqualTo(new byte[] { 70, 97 }));
            Assert.That(prop.SlotCells, Is.EqualTo(new byte[] { 70, 97 }), "the prop's defaults are not changed");
        }

        [Test]
        public void Choose_EmptyMesh_GivesTheDefaults()
        {
            PropEntry prop = Prop(new MeshData(), 70, 97);
            byte[] cells = null;
            Assert.DoesNotThrow(() => cells = PropColour.Choose(prop, new MeshData(), new[] { FrontView() }, new[] { Solid(CellSrgb(88)) }));
            Assert.That(cells, Is.EqualTo(new byte[] { 70, 97 }));
        }

        [Test]
        public void Choose_TwoViews_OneSeesNothing_UsesTheOther()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0f, true, 70);
            PropEntry prop = Prop(mesh, 70);
            byte[] cells = PropColour.Choose(prop, mesh, new[] { LookingAway(), FrontView() },
                                             new[] { Solid(CellSrgb(65)), Solid(CellSrgb(122)) });
            Assert.That(cells, Is.EqualTo(new byte[] { 122 }));
        }

        [Test]
        public void Choose_EightSlots_AtMostSixDistinctCells_AllObjectCells()
        {
            // Eight 6 cm panels with 4 cm gaps, from y = 0.1 up, each in a band of a different colour.
            var mesh = new MeshData();
            int[] colours = { 64, 68, 72, 76, 98, 102, 106, 110 };
            var lo = new float[8];
            var hi = new float[8];
            var defaults = new byte[8];
            for (int s = 0; s < 8; s++)
            {
                float y0 = 0.1f + 0.1f * s;
                lo[s] = y0 - 0.02f;
                hi[s] = y0 + 0.08f;
                defaults[s] = (byte)(80 + s);
                AddPanel(mesh, y0, y0 + 0.06f, 0f, true, defaults[s]);
            }
            PropEntry prop = Prop(mesh, defaults);
            byte[] cells = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { Bands(lo, hi, colours) });

            Assert.That(cells.Length, Is.EqualTo(8));
            var distinct = new HashSet<byte>(cells);
            Assert.That(distinct.Count, Is.LessThanOrEqualTo(PropColour.MaxCells));
            var seenColours = new HashSet<int>(colours);
            foreach (byte c in cells)
            {
                Assert.That((int)c, Is.InRange(64, 127));
                Assert.That(seenColours.Contains(c), Is.True, "a merged slot takes one of the chosen cells, got " + c);
            }
        }

        [Test]
        public void Choose_IsDeterministic()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.55f, 0.9f, 0f, true, 70);
            AddPanel(mesh, 0.1f, 0.45f, 0f, true, 97);
            PropEntry prop = Prop(mesh, 70, 97);
            RgbImage image = Bands(new[] { 0.5f, 0f }, new[] { 2f, 0.5f }, new[] { 83, 117 });
            byte[] a = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { image });
            byte[] b = PropColour.Choose(prop, mesh, new[] { FrontView() }, new[] { image });
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void Choose_FittedFixtureProp_OneCellPerSlot_InRange()
        {
            PropEntry chair = FixtureProp("chair_box_min");
            var into = new MeshData();
            var match = new PropMatch { Index = 0, QuarterTurns = 0 };
            Assert.That(PropFitter.Build(chair, in match, chair.Size, 100000, into), Is.True);
            byte[] cells = PropColour.Choose(chair, into, new[] { FrontView(2.5f) }, new[] { Solid(CellSrgb(93)) });
            Assert.That(cells.Length, Is.EqualTo(chair.SlotCells.Length));
            foreach (byte c in cells) Assert.That((int)c, Is.InRange(64, 127));
            Assert.That(Array.IndexOf(cells, (byte)93), Is.GreaterThanOrEqualTo(0), "the front of the chair is seen");
        }

        // ------------------------------------------------------------------ ChooseOne

        [Test]
        public void ChooseOne_SolidImage_GivesThatCell()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0f, true, 64);
            byte cell = PropColour.ChooseOne(mesh, new[] { FrontView() }, new[] { Solid(CellSrgb(115)) }, 79);
            Assert.That((int)cell, Is.EqualTo(115));
        }

        [Test]
        public void ChooseOne_NothingSeen_GivesTheFallback()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.2f, 0.8f, 0f, true, 64);
            Assert.That((int)PropColour.ChooseOne(mesh, Array.Empty<ObjectView>(), Array.Empty<RgbImage>(), 79), Is.EqualTo(79));
            Assert.That((int)PropColour.ChooseOne(mesh, new[] { LookingAway() }, new[] { Solid(CellSrgb(115)) }, 79), Is.EqualTo(79));
            Assert.That((int)PropColour.ChooseOne(new MeshData(), new[] { FrontView() }, new[] { Solid(CellSrgb(115)) }, 79), Is.EqualTo(79));
        }

        // ------------------------------------------------------------------ Apply

        static MeshData TwoSlotMesh()
        {
            var mesh = new MeshData();
            AddPanel(mesh, 0.55f, 0.9f, 0f, true, 70);
            AddPanel(mesh, 0.1f, 0.45f, 0f, true, 97);
            return mesh;
        }

        static int[] CellsOf(MeshData m)
        {
            var cells = new int[m.VertexCount];
            for (int k = 0; k < cells.Length; k++) cells[k] = Cell(m.Vertices[k].Uv0);
            return cells;
        }

        [Test]
        public void Apply_RewritesEachSlotsCell()
        {
            MeshData mesh = TwoSlotMesh();
            int[] before = CellsOf(mesh);
            PropColour.Apply(mesh, new byte[] { 70, 97 }, new byte[] { 110, 65 });
            int[] after = CellsOf(mesh);
            for (int k = 0; k < before.Length; k++)
            {
                int expected = before[k] == 70 ? 110 : 65;
                Assert.That(after[k], Is.EqualTo(expected), "vertex " + k);
                // The coordinate is the new cell's centre.
                Assert.That(mesh.Vertices[k].Uv0.x, Is.EqualTo(PaletteUv(expected).x).Within(1e-6f));
                Assert.That(mesh.Vertices[k].Uv0.y, Is.EqualTo(PaletteUv(expected).y).Within(1e-6f));
            }
        }

        [Test]
        public void Apply_SwappedCells_EachSlotTakesItsOwn()
        {
            MeshData mesh = TwoSlotMesh();
            int[] before = CellsOf(mesh);
            PropColour.Apply(mesh, new byte[] { 70, 97 }, new byte[] { 97, 70 });
            int[] after = CellsOf(mesh);
            for (int k = 0; k < before.Length; k++)
                Assert.That(after[k], Is.EqualTo(before[k] == 70 ? 97 : 70), "vertex " + k);
        }

        [Test]
        public void Apply_DefaultCells_ChangesNothing()
        {
            MeshData mesh = TwoSlotMesh();
            MeshData before = Copy(mesh);
            PropColour.Apply(mesh, new byte[] { 70, 97 }, new byte[] { 70, 97 });
            AssertMeshEqual(before, mesh, "apply of the defaults");
        }

        [Test]
        public void Apply_EmptyInput_NoException()
        {
            var empty = new MeshData();
            Assert.DoesNotThrow(() => PropColour.Apply(empty, new byte[] { 70 }, new byte[] { 100 }));
            Assert.That(empty.VertexCount, Is.EqualTo(0));
            MeshData mesh = TwoSlotMesh();
            MeshData before = Copy(mesh);
            Assert.DoesNotThrow(() => PropColour.Apply(mesh, Array.Empty<byte>(), Array.Empty<byte>()));
            AssertMeshEqual(before, mesh, "apply of no slots");
        }

        [Test]
        public void Apply_TouchesOnlyUv0()
        {
            MeshData mesh = TwoSlotMesh();
            MeshData before = Copy(mesh);
            PropColour.Apply(mesh, new byte[] { 70, 97 }, new byte[] { 100, 101 });
            for (int k = 0; k < mesh.VertexCount; k++)
            {
                Assert.That(mesh.Vertices[k].Position, Is.EqualTo(before.Vertices[k].Position));
                Assert.That(mesh.Vertices[k].Normal, Is.EqualTo(before.Vertices[k].Normal));
                Assert.That(mesh.Vertices[k].Color, Is.EqualTo(before.Vertices[k].Color));
                Assert.That(mesh.Vertices[k].Uv1, Is.EqualTo(before.Vertices[k].Uv1));
            }
            Assert.That(mesh.IndexCount, Is.EqualTo(before.IndexCount));
        }
    }
}
