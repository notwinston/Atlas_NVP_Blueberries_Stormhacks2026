using System.Collections.Generic;
using Atlas.Kernel.FourD;
using Atlas.Rendering;
using Atlas.Spaces.Slicer;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// The Slicer's meshes (Master Plan section 5.5; lane A contract, sections 10.2 and 13.3): the buffers
    /// hold every slice of every body, each face carries the colour of its cell, and the shadow is coloured
    /// by depth along w. The same kernel runs in the Burst jobs.
    /// </summary>
    public unsafe class SlicerMeshTests
    {
        static readonly float3 Centre = new float3(0.75f, 1.25f, 0f);
        const float Scale = 0.5f;
        const int Guard = 12;

        sealed class Run
        {
            public AtlasVertex[] Vertices;
            public int[] Cells;
            public int VertexCount, TriangleCount, TetsCut;
            public float3 Min, Max;
        }

        static readonly Dictionary<SlicerObject, Mesh4D> s_meshes = new Dictionary<SlicerObject, Mesh4D>();

        static Mesh4D MeshOf(SlicerObject body)
        {
            if (!s_meshes.TryGetValue(body, out Mesh4D mesh))
            {
                mesh = SlicerObjects.Build(body);
                s_meshes.Add(body, mesh);
            }
            return mesh;
        }

        // Slices into buffers of the given capacity, with guard entries after the end that must stay untouched.
        static Run Slice(SlicerObject body, float3 turns, float offset, float tilt, int capacity, Run reuse = null)
        {
            Mesh4D mesh = MeshOf(body);
            var uv = new float2[SlicerObjects.CellUvCount];
            SlicerObjects.FillCellUv(body, uv);
            SlicerController.GetTransform(body, turns.x, turns.y, turns.z, out float4x4 m, out float4 t);
            float4 normal = SlicerController.CutNormal(body, tilt);

            Run run = reuse ?? new Run();
            if (run.Vertices == null || run.Vertices.Length != capacity + Guard)
            {
                run.Vertices = new AtlasVertex[capacity + Guard];
                run.Cells = new int[capacity + Guard];
            }
            var positions = new float3[capacity + Guard];
            var normals = new float3[capacity + Guard];
            for (int i = capacity; i < capacity + Guard; i++)
            {
                run.Vertices[i].Color = 0xDEADBEEF;
                run.Cells[i] = -77;
                positions[i] = new float3(-77f);
                normals[i] = new float3(-77f);
            }

            fixed (float4* v = mesh.Vertices)
            fixed (int* tets = mesh.Tetrahedra, cell = mesh.TetCell, outCells = run.Cells)
            fixed (float2* cellUv = uv)
            fixed (float3* p = positions, n = normals)
            fixed (AtlasVertex* outVertices = run.Vertices)
            {
                SlicerMeshKernel.SliceToVertices(v, tets, mesh.TetCount, cell, in m, in t, in normal, offset,
                    cellUv, uv.Length, Centre, Scale, AtlasShading.LightDirection,
                    p, n, outCells, outVertices, capacity,
                    out run.VertexCount, out run.TriangleCount, out run.TetsCut, out run.Min, out run.Max);
            }

            for (int i = capacity; i < capacity + Guard; i++)
            {
                Assert.That(run.Vertices[i].Color, Is.EqualTo(0xDEADBEEF), "wrote past the vertex buffer");
                Assert.That(run.Cells[i], Is.EqualTo(-77), "wrote past the cell buffer");
                Assert.That(positions[i].x, Is.EqualTo(-77f), "wrote past the position buffer");
                Assert.That(normals[i].x, Is.EqualTo(-77f), "wrote past the normal buffer");
            }
            return run;
        }

        [Test]
        public void SliceBuffers_NeverOverflow_ForEveryBodyAtEveryOffset()
        {
            var turnSets = new[] { new float3(0f), new float3(0.7f, -1.9f, 0.35f), new float3(math.PI / 4f, math.PI / 2f, -math.PI) };
            for (int b = 0; b < SlicerObjects.Count; b++)
            {
                var body = (SlicerObject)b;
                Mesh4D mesh = MeshOf(body);
                Assert.That(mesh.TetCount, Is.EqualTo(SlicerObjects.TetCount(body)), body + ": tetrahedra");
                int capacity = mesh.MaxSliceVertices;
                Assert.That(capacity, Is.EqualTo(6 * mesh.TetCount));

                bool heavy = mesh.TetCount > 1000;
                int steps = heavy ? 22 : 88;
                var tilts = body == SlicerObject.Tube ? new[] { 0f, math.radians(80f) } : new[] { 0f };
                Run run = null, roomy = null;
                int mostVertices = 0;
                foreach (float3 turns in turnSets)
                {
                    foreach (float tilt in tilts)
                    {
                        for (int s = 0; s <= steps; s++)
                        {
                            float offset = math.lerp(-SlicerObjects.CutRange, SlicerObjects.CutRange, s / (float)steps);
                            run = Slice(body, turns, offset, tilt, capacity, run);
                            Assert.That(run.VertexCount, Is.LessThanOrEqualTo(capacity), body + " at " + offset);
                            Assert.That(run.VertexCount, Is.EqualTo(3 * run.TriangleCount));
                            Assert.That(run.TetsCut, Is.LessThanOrEqualTo(mesh.TetCount));
                            mostVertices = math.max(mostVertices, run.VertexCount);

                            // Nothing was left out: twice the room gives the same slice.
                            if (!heavy || s % 7 == 0)
                            {
                                roomy = Slice(body, turns, offset, tilt, 2 * capacity, roomy);
                                Assert.That(run.TetsCut, Is.EqualTo(roomy.TetsCut), body + " at " + offset);
                                Assert.That(run.VertexCount, Is.EqualTo(roomy.VertexCount), body + " at " + offset);
                            }
                        }
                    }
                }
                Assert.That(mostVertices, Is.GreaterThan(0), body + " is cut somewhere in the range");

                // Past both ends of the range the body has gone clean through the cut.
                Assert.That(Slice(body, turnSets[1], -SlicerObjects.CutRange, 0f, capacity, run).VertexCount, Is.EqualTo(0), body + " below");
                Assert.That(Slice(body, turnSets[1], SlicerObjects.CutRange, 0f, capacity, run).VertexCount, Is.EqualTo(0), body + " above");
            }
        }

        [Test]
        public void ATooSmallBuffer_IsNeverWrittenPast()
        {
            // The octahedron slice of the tesseract needs far more than 9 vertices; the kernel stops in time.
            Run full = Slice(SlicerObject.Tesseract, float3.zero, 0f, 0f, 6 * 48);
            Run small = Slice(SlicerObject.Tesseract, float3.zero, 0f, 0f, 9);
            Assert.That(full.VertexCount, Is.GreaterThan(9));
            Assert.That(small.VertexCount, Is.LessThanOrEqualTo(9));
            Assert.That(small.TetsCut, Is.LessThan(full.TetsCut));
        }

        [Test]
        public void TheTesseract_SlicedThroughItsMiddle_IsAnOctahedronOfEightColours()
        {
            Run run = Slice(SlicerObject.Tesseract, float3.zero, 0f, 0f, 6 * 48);
            Assert.That(run.TriangleCount, Is.GreaterThan(0));

            // Eight faces, one per cubic cell, each in its own colour.
            var cells = new HashSet<int>();
            var colours = new HashSet<float2>();
            double area = 0.0;
            for (int i = 0; i < run.VertexCount; i += 3)
            {
                Assert.That(run.Cells[i], Is.EqualTo(run.Cells[i + 1]));
                Assert.That(run.Cells[i], Is.EqualTo(run.Cells[i + 2]));
                cells.Add(run.Cells[i]);
                colours.Add(run.Vertices[i].Uv0);
                float3 a = run.Vertices[i].Position, b = run.Vertices[i + 1].Position, c = run.Vertices[i + 2].Position;
                area += 0.5 * math.length(math.cross(b - a, c - a));
            }
            Assert.That(cells.Count, Is.EqualTo(8));
            Assert.That(colours.Count, Is.EqualTo(8));

            // Area 6.9282 for the unit tesseract (Master Plan section 5.5), scaled to the room: the body of
            // circumradius 1 has edge 1, and the scale is metres per model unit.
            Assert.That(area / (Scale * Scale), Is.EqualTo(6.9282).Within(1e-3));
        }

        [Test]
        public void EveryVertex_CarriesItsCellsColourAndTheTwoSidedShade()
        {
            for (int b = 0; b < SlicerObjects.Count; b++)
            {
                var body = (SlicerObject)b;
                if (MeshOf(body).TetCount > 1000 && body != SlicerObject.Ball) continue;   // the tube is covered below
                Run run = Slice(body, new float3(0.3f, 0.2f, -0.6f), 0.15f, 0f, MeshOf(body).MaxSliceVertices);
                Assert.That(run.VertexCount, Is.GreaterThan(0), body.ToString());
                CheckVertices(body, run);
            }
            Run tube = Slice(SlicerObject.Tube, float3.zero, 0.2f, math.radians(40f), MeshOf(SlicerObject.Tube).MaxSliceVertices);
            Assert.That(tube.VertexCount, Is.GreaterThan(0));
            CheckVertices(SlicerObject.Tube, tube);
        }

        static void CheckVertices(SlicerObject body, Run run)
        {
            float3 lo = new float3(float.MaxValue), hi = new float3(float.MinValue);
            for (int i = 0; i < run.VertexCount; i++)
            {
                AtlasVertex vertex = run.Vertices[i];
                float2 expected = AtlasPalette.Uv(SlicerObjects.PaletteCell(body, run.Cells[i]));
                Assert.That(vertex.Uv0, Is.EqualTo(expected), body + ": palette coordinate of cell " + run.Cells[i]);
                Assert.That(vertex.Uv1, Is.EqualTo(float2.zero), "no line coordinates");
                Assert.That(vertex.LineWeight, Is.EqualTo(0), "no lines on a slice");
                Assert.That(vertex.Shade, Is.EqualTo(AtlasShading.ToByte(AtlasShading.FaceShadeTwoSided(vertex.Normal))),
                            body + ": baked two-sided shade");
                Assert.That(vertex.Color >> 16, Is.EqualTo(0xFFFFu), "reserved channels are 255");
                Assert.That(math.length(vertex.Normal), Is.EqualTo(1f).Within(1e-3f));
                // A body of circumradius 1 sliced anywhere stays within its radius of the centre.
                Assert.That(math.distance(vertex.Position, Centre), Is.LessThanOrEqualTo(Scale * 1.45f));
                lo = math.min(lo, vertex.Position);
                hi = math.max(hi, vertex.Position);
            }
            Assert.That(run.Min, Is.EqualTo(lo), "bounds minimum");
            Assert.That(run.Max, Is.EqualTo(hi), "bounds maximum");
        }

        [Test]
        public void AnEmptySlice_HasBoundsAtTheCentre()
        {
            Run run = Slice(SlicerObject.Cell16, float3.zero, -1.1f, 0f, 96);
            Assert.That(run.VertexCount, Is.EqualTo(0));
            Assert.That(run.Min, Is.EqualTo(Centre));
            Assert.That(run.Max, Is.EqualTo(Centre));
        }

        [Test]
        public void CellColours_AreDistinctWithinABody()
        {
            for (int b = 0; b < SlicerObjects.Count; b++)
            {
                var body = (SlicerObject)b;
                Mesh4D mesh = MeshOf(body);
                var table = new float2[SlicerObjects.CellUvCount];
                SlicerObjects.FillCellUv(body, table);
                var seen = new HashSet<int>();
                for (int cell = 0; cell < mesh.CellCount; cell++)
                {
                    int palette = SlicerObjects.PaletteCell(body, cell);
                    Assert.That(seen.Add(palette), Is.True, body + ": cell " + cell + " shares a colour");
                    Assert.That(table[cell], Is.EqualTo(AtlasPalette.Uv(palette)));
                    if (SlicerObjects.IsPolytope(body))
                    {
                        Assert.That(palette, Is.EqualTo(AtlasPalette.Cell4D(cell)), "the emissive 4D cell colours");
                        Assert.That(AtlasPalette.IsEmissive(palette), Is.True);
                    }
                    else
                    {
                        // Curved bodies need the baked shade to read as solids, so their colours are not emissive.
                        Assert.That(AtlasPalette.IsEmissive(palette), Is.False);
                    }
                }
                Assert.That(mesh.CellCount, Is.LessThanOrEqualTo(SlicerObjects.CellUvCount));
            }
        }

        [Test]
        public void TheShade_MatchesTheLooksTwoSidedShade()
        {
            var normals = new[]
            {
                new float3(0f, 1f, 0f), new float3(0f, -1f, 0f), new float3(1f, 0f, 0f),
                math.normalize(new float3(-0.3f, 0.2f, 0.9f)), AtlasShading.LightDirection, -AtlasShading.LightDirection,
            };
            foreach (float3 n in normals)
                Assert.That(SlicerMeshKernel.ShadeTwoSided(n, AtlasShading.LightDirection),
                            Is.EqualTo(AtlasShading.ToByte(AtlasShading.FaceShadeTwoSided(n))));
            Assert.That(SlicerMeshKernel.ShadeTwoSided(normals[0], AtlasShading.LightDirection),
                        Is.EqualTo(SlicerMeshKernel.ShadeTwoSided(normals[1], AtlasShading.LightDirection)), "the same from both sides");
        }

        [Test]
        public void TheDepthColour_RunsAlongTheWStrip()
        {
            foreach (float w in new[] { -2f, -1.1f, -0.4f, 0f, 0.7f, 1.1f, 3f })
            {
                float2 uv = SlicerMeshKernel.DepthUv(w);
                float2 expected = AtlasPalette.StripUv(AtlasPalette.StripW, SlicerMeshKernel.DepthT(w));
                Assert.That(uv.x, Is.EqualTo(expected.x).Within(1e-6f));
                Assert.That(uv.y, Is.EqualTo(expected.y).Within(1e-6f));
                Assert.That(uv.y, Is.GreaterThanOrEqualTo(AtlasPalette.EmissiveV));
            }
            Assert.That(SlicerMeshKernel.DepthT(SlicerObjects.CutRange), Is.EqualTo(0f), "near the 4D eye");
            Assert.That(SlicerMeshKernel.DepthT(-SlicerObjects.CutRange), Is.EqualTo(1f), "far from it");
        }

        [Test]
        public void TheShadow_ProjectsEveryBoundaryTriangle()
        {
            for (int b = 0; b < SlicerObjects.Count; b++)
            {
                var body = (SlicerObject)b;
                Mesh4D mesh = MeshOf(body);
                int[] triangles = mesh.BuildCellBoundaryTriangles();
                int count = triangles.Length / 3;
                Assert.That(count, Is.GreaterThan(0), body + " has a shadow");

                SlicerController.GetTransform(body, 0.5f, -0.25f, 1.2f, out float4x4 m, out float4 t);
                var vertices = new AtlasVertex[count * 3 + Guard];
                for (int i = count * 3; i < vertices.Length; i++) vertices[i].Color = 0xDEADBEEF;
                float3 lo, hi;
                bool perspective = SlicerObjects.PerspectiveShadow(body);
                fixed (float4* v = mesh.Vertices)
                fixed (int* tri = triangles)
                fixed (AtlasVertex* o = vertices)
                {
                    SlicerMeshKernel.ShadowToVertices(v, tri, count, in m, in t, perspective, Projector4D.DefaultFocal,
                        Centre, Scale, AtlasShading.LightDirection, o, out lo, out hi);
                }
                for (int i = count * 3; i < vertices.Length; i++)
                    Assert.That(vertices[i].Color, Is.EqualTo(0xDEADBEEF), "wrote past the shadow buffer");

                float stripV = AtlasPalette.StripUv(AtlasPalette.StripW, 0f).y;
                for (int i = 0; i < count * 3; i++)
                {
                    float4 p = math.mul(m, mesh.Vertices[triangles[i]]) + t;
                    float3 flat = perspective ? Projector4D.Perspective(p, Projector4D.DefaultFocal) : p.xyz;
                    Assert.That(math.distance(vertices[i].Position, Centre + Scale * flat), Is.LessThan(1e-5f), body.ToString());
                    Assert.That(vertices[i].Uv0.y, Is.EqualTo(stripV).Within(1e-6f), "on the w-depth strip");
                    Assert.That(vertices[i].Uv0.x, Is.EqualTo(SlicerMeshKernel.DepthUv(p.w).x).Within(1e-6f));
                    Assert.That(math.all(vertices[i].Position >= lo - 1e-6f) && math.all(vertices[i].Position <= hi + 1e-6f), Is.True);
                    // The perspective shadow of a unit body, seen from w = 2, is at most twice its size.
                    Assert.That(math.distance(vertices[i].Position, Centre), Is.LessThanOrEqualTo(Scale * 2.01f));
                }
            }
        }

        [Test]
        public void Fixtures_HaveALaneOnlyWhenTheWalkerCanWalk()
        {
            var walking = new AtlasMeshBuilder(256, 512);
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            SlicerFixtures.Build(in layout, walking);
            Assert.That(walking.TriangleCount, Is.GreaterThan(0));
            Assert.That(walking.BoundsMin.y, Is.EqualTo(SlicerFixtures.LaneLift).Within(1e-6f), "the lane lies on the floor");
            Assert.That(walking.BoundsMin.z, Is.EqualTo(-1.5f).Within(1e-5f));
            Assert.That(walking.BoundsMin.x, Is.EqualTo(layout.LaneX - 0.5f * SlicerLayout.LaneWidth).Within(1e-5f));

            var standing = new AtlasMeshBuilder(256, 512);
            SlicerLayout still = SlicerLayout.FromRoom(1.4f, 1.4f, true);
            SlicerFixtures.Build(in still, standing);
            Assert.That(standing.TriangleCount, Is.EqualTo(walking.TriangleCount - 2), "everything but the lane");
            Assert.That(standing.BoundsMin.y, Is.GreaterThan(0.5f), "nothing on the floor in standing mode");

            var marker = new AtlasMeshBuilder(64, 128);
            SlicerFixtures.BuildMarker(marker);
            Assert.That(marker.TriangleCount, Is.EqualTo(12));
            Assert.That(math.length(marker.BoundsCentre), Is.LessThan(1e-6f), "about the origin, so its transform is its place");
        }
    }
}
