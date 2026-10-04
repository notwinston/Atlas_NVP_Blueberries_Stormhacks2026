using System.Collections.Generic;
using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// A growable vertex and index list in the <see cref="AtlasVertex"/> layout, with the shapes every generator
    /// needs: gridded quads, boxes, floor grids, extruded outlines and posts (lane A contract, section 10.2).
    /// Engine-free; <see cref="AtlasMesh"/> uploads the result. Reuse one builder with <see cref="Clear"/>: once
    /// its arrays have grown, building allocates nothing.
    ///
    /// Winding is Unity's: a triangle is seen from the front when its corners run clockwise. Shade is baked from
    /// <see cref="AtlasShading"/>. The gridded shapes cut every side into cells no longer than maxEdge, which is
    /// the 0.25 m grid of Master Plan section 8.4; a cell's diagonal can be up to 1.42 times longer.
    /// <see cref="Subdivide"/> is the strict pass: after it no triangle edge at all is longer than maxEdge.
    /// </summary>
    public sealed class AtlasMeshBuilder
    {
        /// <summary>The longest edge of anything that can stand in a curved space, metres.</summary>
        public const float MaxEdge = 0.25f;
        /// <summary>Spacing of the floor grid lines, metres.</summary>
        public const float GridMetres = 0.5f;
        /// <summary>Lengths within this fraction of the limit count as equal to it.</summary>
        const float EdgeTolerance = 1e-4f;

        public AtlasVertex[] Vertices;      // valid up to VertexCount
        public int VertexCount;
        public int[] Indices;               // valid up to IndexCount
        public int IndexCount;
        public float3 BoundsMin, BoundsMax; // kept up to date by the Add methods

        // Scratch of Subdivide and of the outline caps, kept so that a reused builder allocates nothing.
        Dictionary<long, int> m_Midpoints;
        int[] m_Ring;

        public AtlasMeshBuilder(int vertexCapacity = 1024, int indexCapacity = 4096)
        {
            Vertices = new AtlasVertex[math.max(vertexCapacity, 4)];
            Indices = new int[math.max(indexCapacity, 6)];
            Clear();
        }

        public int TriangleCount => IndexCount / 3;
        /// <summary>True when nothing has been added.</summary>
        public bool IsEmpty => VertexCount == 0;
        /// <summary>Centre of the bounds; zero for an empty builder.</summary>
        public float3 BoundsCentre => VertexCount == 0 ? float3.zero : 0.5f * (BoundsMin + BoundsMax);
        /// <summary>Size of the bounds; zero for an empty builder.</summary>
        public float3 BoundsSize => VertexCount == 0 ? float3.zero : BoundsMax - BoundsMin;

        /// <summary>Empties the lists and keeps the arrays.</summary>
        public void Clear()
        {
            VertexCount = 0;
            IndexCount = 0;
            BoundsMin = new float3(float.MaxValue);
            BoundsMax = new float3(float.MinValue);
        }

        /// <summary>Makes room for this many more vertices and indices, so the adds that follow do not grow the arrays.</summary>
        public void Reserve(int vertices, int indices)
        {
            int needV = VertexCount + vertices;
            if (needV > Vertices.Length) Array.Resize(ref Vertices, math.max(needV, Vertices.Length * 2));
            int needI = IndexCount + indices;
            if (needI > Indices.Length) Array.Resize(ref Indices, math.max(needI, Indices.Length * 2));
        }

        /// <summary>Adds a vertex and returns its index.</summary>
        public int AddVertex(in AtlasVertex vertex)
        {
            if (VertexCount == Vertices.Length) Array.Resize(ref Vertices, Vertices.Length * 2);
            Vertices[VertexCount] = vertex;
            BoundsMin = math.min(BoundsMin, vertex.Position);
            BoundsMax = math.max(BoundsMax, vertex.Position);
            return VertexCount++;
        }

        /// <summary>Adds a vertex whose shade is baked from its normal and the ambient occlusion.</summary>
        public int AddVertex(float3 position, float3 normal, float2 uv0, float2 lineCoord,
                             float ambientOcclusion = 1f, byte lineWeight = 255)
        {
            var vertex = new AtlasVertex
            {
                Position = position,
                Normal = normal,
                Color = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(normal), ambientOcclusion), lineWeight),
                Uv0 = uv0,
                Uv1 = lineCoord,
            };
            return AddVertex(vertex);
        }

        /// <summary>Adds a triangle. Front face: a, b, c clockwise.</summary>
        public void AddTriangle(int a, int b, int c)
        {
            if (IndexCount + 3 > Indices.Length) Array.Resize(ref Indices, Indices.Length * 2);
            Indices[IndexCount] = a;
            Indices[IndexCount + 1] = b;
            Indices[IndexCount + 2] = c;
            IndexCount += 3;
        }

        /// <summary>Appends everything in another builder, unchanged.</summary>
        public void Append(AtlasMeshBuilder other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            Reserve(other.VertexCount, other.IndexCount);
            int offset = VertexCount;
            Array.Copy(other.Vertices, 0, Vertices, VertexCount, other.VertexCount);
            VertexCount += other.VertexCount;
            for (int i = 0; i < other.IndexCount; i++) Indices[IndexCount + i] = other.Indices[i] + offset;
            IndexCount += other.IndexCount;
            if (other.VertexCount > 0)
            {
                BoundsMin = math.min(BoundsMin, other.BoundsMin);
                BoundsMax = math.max(BoundsMax, other.BoundsMax);
            }
        }

        /// <summary>
        /// Appends a mesh read from a file (a kit piece) moved by offset. Normals, shade, palette and line
        /// coordinates are copied unchanged. Allocates nothing once the arrays are large enough.
        /// </summary>
        public void Append(MeshData source, float3 offset)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.VertexCount == 0) return;
            Reserve(source.VertexCount, source.IndexCount);
            int first = VertexCount;
            for (int v = 0; v < source.VertexCount; v++)
            {
                AtlasVertex vertex = source.Vertices[v];
                vertex.Position += offset;
                Vertices[VertexCount++] = vertex;
            }
            for (int i = 0; i < source.IndexCount; i++) Indices[IndexCount + i] = source.Indices[i] + first;
            IndexCount += source.IndexCount;
            BoundsMin = math.min(BoundsMin, source.BoundsMin + offset);
            BoundsMax = math.max(BoundsMax, source.BoundsMax + offset);
        }

        // ---- Quads -------------------------------------------------------------------------------------------

        /// <summary>
        /// Adds the parallelogram origin, origin + u, origin + u + v, origin + v as a grid whose cells are at most
        /// maxEdge long on both sides. Front face towards cross(v, u) (Unity's clockwise winding).
        /// uv0 is one palette coordinate for the whole quad. Line coordinates are line0 + s * lineU + t * lineV
        /// for s, t in 0..1 along u and v. shade is baked from AtlasShading.FaceShade of the face normal.
        /// </summary>
        public void AddQuadGrid(float3 origin, float3 u, float3 v, float2 uv0,
                                float2 line0, float2 lineU, float2 lineV,
                                float ambientOcclusion = 1f, byte lineWeight = 255, float maxEdge = MaxEdge)
        {
            int nu = AtlasMeshBudget.Segments(math.length(u), maxEdge);
            int nv = AtlasMeshBudget.Segments(math.length(v), maxEdge);
            AddQuadCells(origin, u, v, uv0, line0, lineU, lineV, ambientOcclusion, ambientOcclusion, lineWeight, nu, nv);
        }

        /// <summary>One quad of two triangles, with no line coordinates: for things that are never warped.</summary>
        public void AddQuad(float3 origin, float3 u, float3 v, float2 uv0,
                            float ambientOcclusion = 1f, byte lineWeight = 0)
        {
            AddQuadCells(origin, u, v, uv0, float2.zero, float2.zero, float2.zero,
                         ambientOcclusion, ambientOcclusion, lineWeight, 1, 1);
        }

        /// <summary>Segments in each corner of <see cref="AddSquircle"/>: smooth at HUD sizes, 37 vertices a shape.</summary>
        public const int SquircleSegments = 8;

        /// <summary>
        /// A rounded rectangle in the plane z = centre.z facing -z (towards a walker looking down +z), as one
        /// fan: for HUD panels and chips, which are never warped. The corners are continuous ("squircle")
        /// curves: each spans 1.5 radius along its edges as a quarter superellipse of exponent 4, so the edge
        /// flows into the bend. A radius that reaches the short side makes a capsule with round ends.
        /// </summary>
        public void AddSquircle(float3 centre, float halfWidth, float halfHeight, float radius, float2 uv0,
                                float ambientOcclusion = 1f, byte lineWeight = 0)
        {
            const int n = SquircleSegments;
            float shortest = math.min(halfWidth, halfHeight);
            float extent = math.min(1.5f * radius, shortest);
            // A capsule's ends are half circles; smaller corners take the squircle's exponent.
            float power = extent >= shortest ? 1f : 0.5f;   // 2 / exponent
            var normal = new float3(0f, 0f, -1f);
            uint color = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(normal), ambientOcclusion), lineWeight);

            Reserve(1 + 4 * (n + 1), 3 * 4 * (n + 1));
            AtlasVertex vertex = default;
            vertex.Normal = normal;
            vertex.Uv0 = uv0;
            vertex.Color = color;
            vertex.Position = centre;
            int hub = AddVertex(vertex);
            float2 inner = new float2(halfWidth - extent, halfHeight - extent);
            // Anticlockwise from the right edge, one quarter turn per corner.
            for (int corner = 0; corner < 4; corner++)
            {
                float2 sign = new float2(corner == 0 || corner == 3 ? 1f : -1f, corner < 2 ? 1f : -1f);
                for (int i = 0; i <= n; i++)
                {
                    float angle = (corner + (float)i / n) * (0.5f * math.PI);
                    math.sincos(angle, out float sin, out float cos);
                    float2 bend = new float2(math.pow(math.abs(cos), power), math.pow(math.abs(sin), power));
                    float2 p = sign * (inner + extent * bend);
                    vertex.Position = centre + new float3(p.x, p.y, 0f);
                    AddVertex(vertex);
                }
            }
            // Seen from -z the ring runs anticlockwise, so each fan triangle takes its two rim points reversed.
            int ring = 4 * (n + 1);
            for (int i = 0; i < ring; i++) AddTriangle(hub, hub + 1 + (i + 1) % ring, hub + 1 + i);
        }

        /// <summary>
        /// A horizontal rectangle at height y facing up, from floor point min to max (x, z), gridded, with line
        /// coordinates in world metres divided by lineSpacing: lines fall on the room frame's grid, so
        /// neighbouring floors line up.
        /// </summary>
        public void AddFloorGrid(float2 min, float2 max, float y, float2 uv0,
                                 float lineSpacing = GridMetres, float ambientOcclusion = 1f,
                                 byte lineWeight = 255, float maxEdge = MaxEdge)
        {
            float2 size = max - min;
            float scale = lineSpacing > 0f ? 1f / lineSpacing : 0f;
            AddQuadGrid(new float3(min.x, y, min.y), new float3(size.x, 0f, 0f), new float3(0f, 0f, size.y), uv0,
                        min * scale, new float2(size.x * scale, 0f), new float2(0f, size.y * scale),
                        ambientOcclusion, lineWeight, maxEdge);
        }

        // The grid itself: nu by nv cells, shared vertices inside the quad. The row at t = 0 takes its own
        // ambient occlusion, which is how a wall or a box darkens where it meets the floor.
        void AddQuadCells(float3 origin, float3 u, float3 v, float2 uv0,
                          float2 line0, float2 lineU, float2 lineV,
                          float occlusion, float occlusionAtV0, byte lineWeight, int nu, int nv)
        {
            float3 normal = math.normalizesafe(math.cross(v, u), new float3(0f, 1f, 0f));
            float shade = AtlasShading.FaceShade(normal);
            uint color = AtlasVertex.PackColor(AtlasShading.ToByte(shade, occlusion), lineWeight);
            uint colorAtV0 = AtlasVertex.PackColor(AtlasShading.ToByte(shade, occlusionAtV0), lineWeight);

            Reserve((nu + 1) * (nv + 1), nu * nv * 6);
            int first = VertexCount;
            float invU = 1f / nu, invV = 1f / nv;
            AtlasVertex vertex;
            vertex.Normal = normal;
            vertex.Uv0 = uv0;
            for (int j = 0; j <= nv; j++)
            {
                float t = j * invV;
                float3 rowOrigin = origin + v * t;
                float2 rowLine = line0 + lineV * t;
                vertex.Color = j == 0 ? colorAtV0 : color;
                for (int i = 0; i <= nu; i++)
                {
                    float s = i * invU;
                    vertex.Position = rowOrigin + u * s;
                    vertex.Uv1 = rowLine + lineU * s;
                    Vertices[VertexCount++] = vertex;
                }
            }

            // The corners bound the whole grid.
            Grow(origin);
            Grow(origin + u);
            Grow(origin + v);
            Grow(origin + u + v);

            int stride = nu + 1;
            int o = IndexCount;
            for (int j = 0; j < nv; j++)
            {
                int row = first + j * stride;
                for (int i = 0; i < nu; i++)
                {
                    int p00 = row + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    Indices[o] = p00; Indices[o + 1] = p01; Indices[o + 2] = p11;
                    Indices[o + 3] = p00; Indices[o + 4] = p11; Indices[o + 5] = p10;
                    o += 6;
                }
            }
            IndexCount = o;
        }

        void Grow(float3 point)
        {
            BoundsMin = math.min(BoundsMin, point);
            BoundsMax = math.max(BoundsMax, point);
        }

        // ---- Boxes -------------------------------------------------------------------------------------------

        /// <summary>
        /// A box standing on its base centre, turned by yaw about +Y (Unity's sense, radians), five or six faces.
        /// Line coordinates are metres along each face divided by GridMetres. baseOcclusion darkens the row of
        /// vertices that touches the floor (1 = none).
        /// </summary>
        public void AddBox(float3 baseCentre, float3 size, float yaw, float2 uv0,
                           bool withBottom = false, byte lineWeight = 0, float maxEdge = MaxEdge,
                           float baseOcclusion = 1f)
        {
            math.sincos(yaw, out float sin, out float cos);
            float3 axisX = new float3(cos, 0f, -sin);
            float3 axisZ = new float3(sin, 0f, cos);
            float3 up = new float3(0f, size.y, 0f);
            float3 hx = axisX * (0.5f * size.x);
            float3 hz = axisZ * (0.5f * size.z);
            float scale = 1f / GridMetres;
            int nx = AtlasMeshBudget.Segments(size.x, maxEdge);
            int ny = AtlasMeshBudget.Segments(size.y, maxEdge);
            int nz = AtlasMeshBudget.Segments(size.z, maxEdge);
            float2 lineX = new float2(size.x * scale, 0f), lineZ = new float2(size.z * scale, 0f);
            float2 lineY = new float2(0f, size.y * scale);

            // Top: cross(z, x) = +y.
            AddQuadCells(baseCentre + up - hx - hz, 2f * hx, 2f * hz, uv0, float2.zero, lineX,
                         new float2(0f, size.z * scale), 1f, 1f, lineWeight, nx, nz);
            if (withBottom)
                AddQuadCells(baseCentre - hx - hz, 2f * hz, 2f * hx, uv0, float2.zero, lineZ,
                             new float2(0f, size.x * scale), baseOcclusion, baseOcclusion, lineWeight, nz, nx);

            // Sides: with v up, the face looks along cross(up, u).
            AddQuadCells(baseCentre + hx - hz, 2f * hz, up, uv0, float2.zero, lineZ, lineY, 1f, baseOcclusion, lineWeight, nz, ny);   // +x
            AddQuadCells(baseCentre - hx + hz, -2f * hz, up, uv0, float2.zero, lineZ, lineY, 1f, baseOcclusion, lineWeight, nz, ny);  // -x
            AddQuadCells(baseCentre + hx + hz, -2f * hx, up, uv0, float2.zero, lineX, lineY, 1f, baseOcclusion, lineWeight, nx, ny);  // +z
            AddQuadCells(baseCentre - hx - hz, 2f * hx, up, uv0, float2.zero, lineX, lineY, 1f, baseOcclusion, lineWeight, nx, ny);   // -z
        }

        // ---- Posts -------------------------------------------------------------------------------------------

        /// <summary>
        /// A round post standing on its base centre: a prism of the given number of sides with smooth normals and,
        /// optionally, a top. The side count is raised until no rim edge is longer than maxEdge.
        /// </summary>
        public void AddPost(float3 baseCentre, float radius, float height, float2 uv0,
                            int sides = 8, bool withTop = true, byte lineWeight = 0, float maxEdge = MaxEdge,
                            float baseOcclusion = 1f)
        {
            sides = math.max(sides, 3);
            if (maxEdge > 0f)
            {
                while (2f * radius * math.sin(math.PI / sides) > maxEdge * (1f + EdgeTolerance) && sides < 256) sides++;
            }
            int rows = AtlasMeshBudget.Segments(height, maxEdge);
            Reserve((sides + 1) * (rows + 1) + sides + 1, sides * rows * 6 + sides * 3);

            int first = VertexCount;
            float step = 2f * math.PI / sides;
            for (int j = 0; j <= rows; j++)
            {
                float y = height * j / rows;
                float occlusion = j == 0 ? baseOcclusion : 1f;
                for (int i = 0; i <= sides; i++)
                {
                    // The last column repeats the first, so the line coordinate does not wrap inside a face.
                    math.sincos(step * (i % sides), out float sin, out float cos);
                    float3 normal = new float3(sin, 0f, cos);
                    AddVertex(baseCentre + new float3(radius * sin, y, radius * cos), normal, uv0,
                              new float2(i, y / GridMetres), occlusion, lineWeight);
                }
            }

            int stride = sides + 1;
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int p00 = first + j * stride + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    // Seen from outside the angle grows to the right, so clockwise is along, then up.
                    AddTriangle(p00, p11, p01);
                    AddTriangle(p00, p10, p11);
                }
            }

            if (!withTop) return;
            int capStart = IndexCount;
            float3 upNormal = new float3(0f, 1f, 0f);
            int centre = AddVertex(baseCentre + new float3(0f, height, 0f), upNormal, uv0, float2.zero, 1f, lineWeight);
            int rim = VertexCount;
            for (int i = 0; i < sides; i++)
            {
                math.sincos(step * i, out float sin, out float cos);
                AddVertex(baseCentre + new float3(radius * sin, height, radius * cos), upNormal, uv0, float2.zero, 1f, lineWeight);
            }
            for (int i = 0; i < sides; i++) AddTriangle(centre, rim + i, rim + (i + 1) % sides);
            if (maxEdge > 0f && radius > maxEdge) Subdivide(maxEdge, capStart);
        }

        // ---- Spheres -----------------------------------------------------------------------------------------

        /// <summary>
        /// A latitude and longitude sphere with smooth normals, faces outwards, no lines: segments round the axis, rings from
        /// pole to pole (the rows at the poles are one triangle per segment). Not gridded to maxEdge.
        /// </summary>
        public void AddSphere(float3 centre, float radius, int segments, int rings, float2 uv0)
        {
            Reserve((segments + 1) * (rings + 1), segments * rings * 6);
            int first = VertexCount;
            for (int j = 0; j <= rings; j++)
            {
                math.sincos(math.PI * j / rings, out float sinPolar, out float cosPolar);
                for (int i = 0; i <= segments; i++)
                {
                    math.sincos(2f * math.PI * (i % segments) / segments, out float sin, out float cos);
                    float3 normal = new float3(sinPolar * sin, cosPolar, sinPolar * cos);
                    AddVertex(centre + radius * normal, normal, uv0, float2.zero, 1f, 0);
                }
            }
            int stride = segments + 1;
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int p00 = first + j * stride + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    // The top and bottom rows collapse to the poles: one triangle each instead of two.
                    if (j > 0) AddTriangleOutward(centre, p00, p10, p11);
                    if (j < rings - 1) AddTriangleOutward(centre, p00, p11, p01);
                }
            }
        }

        /// <summary>Adds the triangle wound so that its front (clockwise, Unity's rule) faces away from centre.</summary>
        public void AddTriangleOutward(float3 centre, int a, int b, int c)
        {
            float3 pa = Vertices[a].Position, pb = Vertices[b].Position, pc = Vertices[c].Position;
            float3 front = math.cross(pb - pa, pc - pa);
            if (math.dot(front, (pa + pb + pc) / 3f - centre) >= 0f) AddTriangle(a, b, c);
            else AddTriangle(a, c, b);
        }

        // ---- Extruded outlines -------------------------------------------------------------------------------

        /// <summary>
        /// Walls along a floor outline (x, z) from height y0 to y1, and optionally the flat top and bottom.
        /// A closed outline may run either way round; its walls face outwards, or inwards when inward is set (a
        /// room seen from inside). An open outline has no caps and its walls face to the left of the direction of
        /// travel seen from above, or to the right when inward is set. The outline must not cross itself; it may be
        /// concave. Wall line coordinates are metres along the outline and metres of height, divided by GridMetres.
        /// </summary>
        public void AddExtrudedOutline(float2[] outline, int count, float y0, float y1, float2 uv0,
                                       bool closed = true, bool inward = false,
                                       bool capTop = true, bool capBottom = false,
                                       byte lineWeight = 0, float maxEdge = MaxEdge, float baseOcclusion = 1f)
        {
            if (outline == null) throw new ArgumentNullException(nameof(outline));
            if (count > outline.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count < 2 || (closed && count < 3)) return;

            if (y1 < y0)
            {
                float swap = y0;
                y0 = y1;
                y1 = swap;
            }

            float area2 = 0f;
            for (int i = 0; i < count; i++)
            {
                float2 a = outline[i], b = outline[(i + 1) % count];
                area2 += a.x * b.y - b.x * a.y;
            }
            // Seen from above (x right, z up) a positive area runs anticlockwise.
            bool anticlockwise = closed && area2 > 0f;
            bool caps = closed && (capTop || capBottom);
            // Walls face the left of travel on a clockwise outline; that is outwards.
            bool faceLeft = anticlockwise == inward;

            float height = y1 - y0;
            float scale = 1f / GridMetres;
            int rows = AtlasMeshBudget.Segments(height, maxEdge);
            int edges = closed ? count : count - 1;
            float travelled = 0f;
            for (int i = 0; i < edges; i++)
            {
                float2 a = outline[i], b = outline[(i + 1) % count];
                float length = math.distance(a, b);
                if (length <= 0f) continue;
                // A cap is cut by halving, so the wall under it is cut the same way and shares its vertices.
                int columns = caps ? AtlasMeshBudget.DyadicSegments(length, maxEdge) : AtlasMeshBudget.Segments(length, maxEdge);
                float3 pa = new float3(a.x, y0, a.y), pb = new float3(b.x, y0, b.y);
                float2 lineV = new float2(0f, height * scale);
                if (faceLeft)
                    AddQuadCells(pb, pa - pb, new float3(0f, height, 0f), uv0,
                                 new float2((travelled + length) * scale, y0 * scale), new float2(-length * scale, 0f), lineV,
                                 1f, baseOcclusion, lineWeight, columns, rows);
                else
                    AddQuadCells(pa, pb - pa, new float3(0f, height, 0f), uv0,
                                 new float2(travelled * scale, y0 * scale), new float2(length * scale, 0f), lineV,
                                 1f, baseOcclusion, lineWeight, columns, rows);
                travelled += length;
            }

            if (!caps) return;
            if (capTop) AddOutlineCap(outline, count, y1, uv0, true, anticlockwise, lineWeight, maxEdge);
            if (capBottom) AddOutlineCap(outline, count, y0, uv0, false, anticlockwise, lineWeight, maxEdge);
        }

        /// <summary>
        /// A flat polygon at height y over a closed floor outline (x, z), facing up or down, cut so that no edge is
        /// longer than maxEdge. Line coordinates are world metres divided by GridMetres.
        /// </summary>
        public void AddOutlineCap(float2[] outline, int count, float y, float2 uv0, bool faceUp = true,
                                  byte lineWeight = 0, float maxEdge = MaxEdge)
        {
            if (outline == null) throw new ArgumentNullException(nameof(outline));
            if (count > outline.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count < 3) return;
            float area2 = 0f;
            for (int i = 0; i < count; i++)
            {
                float2 a = outline[i], b = outline[(i + 1) % count];
                area2 += a.x * b.y - b.x * a.y;
            }
            AddOutlineCap(outline, count, y, uv0, faceUp, area2 > 0f, lineWeight, maxEdge);
        }

        // Ear clipping. The ring is walked anticlockwise (seen from above); an ear is a convex corner with no
        // other corner inside it.
        void AddOutlineCap(float2[] outline, int count, float y, float2 uv0, bool faceUp, bool anticlockwise,
                           byte lineWeight, float maxEdge)
        {
            if (m_Ring == null || m_Ring.Length < count) m_Ring = new int[math.max(count, 16)];
            int[] ring = m_Ring;
            for (int i = 0; i < count; i++) ring[i] = anticlockwise ? i : count - 1 - i;

            int capStart = IndexCount;
            int first = VertexCount;
            float3 normal = new float3(0f, faceUp ? 1f : -1f, 0f);
            float scale = 1f / GridMetres;
            Reserve(count, (count - 2) * 3);
            for (int i = 0; i < count; i++)
                AddVertex(new float3(outline[i].x, y, outline[i].y), normal, uv0, outline[i] * scale, 1f, lineWeight);

            int remaining = count;
            int guard = count * count + 8;
            int at = 0;
            while (remaining > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int tries = 0; tries < remaining; tries++)
                {
                    int i = (at + tries) % remaining;
                    int ip = ring[(i + remaining - 1) % remaining], ic = ring[i], inx = ring[(i + 1) % remaining];
                    if (!IsEar(outline, ring, remaining, ip, ic, inx)) continue;
                    AddCapTriangle(first + ip, first + ic, first + inx, faceUp);
                    for (int k = i; k < remaining - 1; k++) ring[k] = ring[k + 1];
                    remaining--;
                    at = i == 0 ? 0 : i - 1;
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // A degenerate outline (collinear or touching corners): clip anyway rather than stall.
                    AddCapTriangle(first + ring[remaining - 1], first + ring[0], first + ring[1], faceUp);
                    for (int k = 0; k < remaining - 1; k++) ring[k] = ring[k + 1];
                    remaining--;
                    at = 0;
                }
            }
            if (remaining == 3) AddCapTriangle(first + ring[0], first + ring[1], first + ring[2], faceUp);

            if (maxEdge > 0f) Subdivide(maxEdge, capStart);
        }

        // a, b, c are anticlockwise seen from above; a face that looks up needs them clockwise.
        void AddCapTriangle(int a, int b, int c, bool faceUp)
        {
            if (faceUp) AddTriangle(a, c, b);
            else AddTriangle(a, b, c);
        }

        static bool IsEar(float2[] outline, int[] ring, int remaining, int ip, int ic, int inx)
        {
            float2 a = outline[ip], b = outline[ic], c = outline[inx];
            if (Cross(b - a, c - b) <= 0f) return false;   // reflex or flat corner
            for (int k = 0; k < remaining; k++)
            {
                int other = ring[k];
                if (other == ip || other == ic || other == inx) continue;
                float2 p = outline[other];
                if (Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f) return false;
            }
            return true;
        }

        static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        // ---- Subdivision and measures ------------------------------------------------------------------------

        /// <summary>
        /// The strict pass: splits triangles until no triangle edge is longer than maxEdge (Master Plan section
        /// 8.4). Works on the triangles from index firstIndex on. A long edge is always cut at its midpoint and
        /// the longest edge of a triangle first, so two triangles that share an edge cut it at the same points and
        /// no crack opens; triangles that share vertex indices share the new vertices too. Attributes are
        /// interpolated. Returns the number of triangles added.
        /// </summary>
        public int Subdivide(float maxEdge = MaxEdge, int firstIndex = 0)
        {
            if (!(maxEdge > 0f)) throw new ArgumentOutOfRangeException(nameof(maxEdge));
            if (firstIndex < 0 || firstIndex % 3 != 0) throw new ArgumentOutOfRangeException(nameof(firstIndex));

            if (m_Midpoints == null) m_Midpoints = new Dictionary<long, int>(256);
            m_Midpoints.Clear();
            float limit = maxEdge * (1f + EdgeTolerance);
            float limitSq = limit * limit;
            int before = IndexCount;

            // Triangles appended by a split are reached later by the same loop.
            for (int t = firstIndex; t < IndexCount; t += 3)
            {
                while (true)
                {
                    int i0 = Indices[t], i1 = Indices[t + 1], i2 = Indices[t + 2];
                    float3 p0 = Vertices[i0].Position, p1 = Vertices[i1].Position, p2 = Vertices[i2].Position;
                    float d01 = math.distancesq(p0, p1), d12 = math.distancesq(p1, p2), d20 = math.distancesq(p2, p0);
                    float longest = math.max(d01, math.max(d12, d20));
                    if (!(longest > limitSq)) break;

                    if (d01 >= d12 && d01 >= d20)
                    {
                        int m = Midpoint(i0, i1);
                        Indices[t + 1] = m;             // (i0, m, i2)
                        AddTriangle(m, i1, i2);
                    }
                    else if (d12 >= d20)
                    {
                        int m = Midpoint(i1, i2);
                        Indices[t + 2] = m;             // (i0, i1, m)
                        AddTriangle(i0, m, i2);
                    }
                    else
                    {
                        int m = Midpoint(i2, i0);
                        Indices[t + 2] = m;             // (i0, i1, m)
                        AddTriangle(m, i1, i2);
                    }
                }
            }
            return (IndexCount - before) / 3;
        }

        int Midpoint(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (m_Midpoints.TryGetValue(key, out int index)) return index;

            AtlasVertex va = Vertices[a], vb = Vertices[b];
            AtlasVertex mid;
            // (a + b) / 2 is the same whichever end comes first, so both sides of an edge agree to the bit.
            mid.Position = (va.Position + vb.Position) * 0.5f;
            mid.Normal = math.normalizesafe(va.Normal + vb.Normal, va.Normal);
            mid.Uv0 = (va.Uv0 + vb.Uv0) * 0.5f;
            mid.Uv1 = (va.Uv1 + vb.Uv1) * 0.5f;
            mid.Color = AverageColor(va.Color, vb.Color);
            index = AddVertex(mid);
            m_Midpoints.Add(key, index);
            return index;
        }

        static uint AverageColor(uint a, uint b)
        {
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                uint ca = (a >> shift) & 255u, cb = (b >> shift) & 255u;
                result |= ((ca + cb + 1u) >> 1) << shift;
            }
            return result;
        }

        /// <summary>Length of the longest triangle edge, diagonals included. Metres.</summary>
        public float LongestEdge()
        {
            float longest = 0f;
            for (int t = 0; t + 2 < IndexCount; t += 3)
            {
                float3 p0 = Vertices[Indices[t]].Position, p1 = Vertices[Indices[t + 1]].Position, p2 = Vertices[Indices[t + 2]].Position;
                longest = math.max(longest, math.max(math.distancesq(p0, p1), math.max(math.distancesq(p1, p2), math.distancesq(p2, p0))));
            }
            return math.sqrt(longest);
        }

        /// <summary>
        /// Length of the longest grid side: over all triangles, the longer of the two shorter edges. On a gridded
        /// shape this is the cell side, the measure of "generated on a 0.25 m grid". Metres.
        /// </summary>
        public float LongestSide()
        {
            float longest = 0f;
            for (int t = 0; t + 2 < IndexCount; t += 3)
            {
                float3 p0 = Vertices[Indices[t]].Position, p1 = Vertices[Indices[t + 1]].Position, p2 = Vertices[Indices[t + 2]].Position;
                float a = math.distancesq(p0, p1), b = math.distancesq(p1, p2), c = math.distancesq(p2, p0);
                float second = a + b + c - math.max(a, math.max(b, c)) - math.min(a, math.min(b, c));
                longest = math.max(longest, second);
            }
            return math.sqrt(longest);
        }

        /// <summary>True when the triangle count is within the budget (see <see cref="AtlasMeshBudget"/>).</summary>
        public bool WithinBudget(int triangles)
        {
            return TriangleCount <= triangles;
        }
    }
}
