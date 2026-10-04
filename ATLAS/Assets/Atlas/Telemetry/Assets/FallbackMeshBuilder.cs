using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Fits a template of the built-in set to one object's box (docs/contracts/lane-d-link.md section 4.4):
    /// scales it to the box, splits it until no edge is longer than 0.25 m, and bakes the shade and the palette
    /// coordinate into the look's vertex format. Engine-free. One instance keeps its scratch buffers, so a
    /// second build allocates nothing once they have grown.
    /// </summary>
    public sealed class FallbackMeshBuilder
    {
        /// <summary>A built-in mesh never has more triangles than this after scaling (Master Plan section 8.4, test L19).</summary>
        public const int MaxTriangles = 1500;
        /// <summary>Metres.</summary>
        public const float MaxEdge = MeshData.DefaultMaxEdge;
        /// <summary>The floor darkens what stands on it: this much at the floor, none from OcclusionHeight up.</summary>
        public const float FloorOcclusion = 0.7f;
        public const float OcclusionHeight = 0.3f;

        /// <summary>
        /// Longest side of a grid cell, metres: just under MaxEdge / sqrt(2), so that the cell's diagonal stays
        /// under <see cref="MaxEdge"/>. The number of cells along a side depends on the side's length alone, so
        /// two faces that share an edge cut it alike.
        /// </summary>
        public const float MaxCellSide = 0.1767f;

        float3[] _positions = new float3[1024];
        float3[] _normals = new float3[1024];
        int _vertexCount;
        float3 _size, _inverse;
        int[] _stack = new int[3 * 256];
        int _stackCount;

        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with the template at the box's size.
        /// False, with <paramref name="into"/> empty, when the box is so large that the split passes
        /// <see cref="MaxTriangles"/>: such an object keeps its placeholder box.
        /// </summary>
        /// <param name="size">The box: (w along local X, h up, d along local Z), metres.</param>
        /// <param name="yaw">The box's turn about +Y in the room, radians: it fixes where the room's light falls.</param>
        public bool Build(FallbackTemplate template, float3 size, float yaw, MeshData into)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (into == null) throw new ArgumentNullException("into");
            into.Clear();
            if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f)) return false;

            _size = size;
            _inverse = 1f / size;
            _vertexCount = 0;
            _stackCount = 0;
            int emitted = 0;
            int[] source = template.Indices;
            for (int k = 0; k + 2 < source.Length;)
            {
                // The templates are made of rectangles, each written as two triangles (a, b, c), (a, c, d).
                // A rectangle becomes a grid, which needs the fewest triangles; anything else is bisected.
                if (k + 5 < source.Length && source[k + 3] == source[k] && source[k + 4] == source[k + 2]
                    && TryGrid(template, source[k], source[k + 1], source[k + 2], source[k + 5], into, ref emitted))
                {
                    k += 6;
                }
                else
                {
                    Push(AddVertex(template, source[k]), AddVertex(template, source[k + 1]), AddVertex(template, source[k + 2]));
                    Bisect(into, ref emitted);
                    k += 3;
                }
                if (emitted > MaxTriangles)
                {
                    into.Clear();
                    return false;
                }
            }

            // Bake: one palette cell, shade from the room's fixed light and the floor's occlusion.
            float2 uv0 = PaletteUv(template.PaletteCell);
            float cos = math.cos(yaw), sin = math.sin(yaw);
            into.Reserve(_vertexCount, 0);
            AtlasVertex vertex;
            vertex.Uv0 = uv0;
            vertex.Uv1 = float2.zero;
            for (int v = 0; v < _vertexCount; v++)
            {
                float3 normal = _normals[v];
                var inRoom = new float3(normal.x * cos + normal.z * sin, normal.y, -normal.x * sin + normal.z * cos);
                float occlusion = math.lerp(FloorOcclusion, 1f, math.saturate(_positions[v].y / OcclusionHeight));
                vertex.Position = _positions[v];
                vertex.Normal = normal;
                vertex.Color = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(inRoom), occlusion), 255);
                into.AddVertex(in vertex);
            }
            return true;
        }

        /// <summary>Centre of palette cell <paramref name="index"/> (lane A section 10.7): 16 by 16 cells, row 0 at v = 0.</summary>
        public static float2 PaletteUv(int index)
        {
            return new float2(((index & 15) + 0.5f) / 16f, (((index >> 4) & 15) + 0.5f) / 16f);
        }

        // A rectangle a, b, c, d (b - a and d - a its sides) as a grid of cells no longer than MaxCellSide.
        // False, with nothing added, when the four corners are not a rectangle.
        bool TryGrid(FallbackTemplate template, int ia, int ib, int ic, int id, MeshData into, ref int emitted)
        {
            float3 a = template.Positions[ia] * _size, b = template.Positions[ib] * _size;
            float3 c = template.Positions[ic] * _size, d = template.Positions[id] * _size;
            float3 u = b - a, v = d - a;
            float lu = math.length(u), lv = math.length(v);
            if (!(lu > 0f) || !(lv > 0f)) return false;
            if (math.length(a + c - b - d) > 1e-5f * (lu + lv) || math.abs(math.dot(u, v)) > 1e-4f * lu * lv) return false;

            int nu = Cells(lu), nv = Cells(lv);
            emitted += 2 * nu * nv;
            if (emitted > MaxTriangles) return true;        // the caller sees the count and gives up
            float3 normal = math.normalizesafe(template.Normals[ia] * _inverse, new float3(0f, 1f, 0f));
            int first = _vertexCount;
            EnsureVertices(_vertexCount + (nu + 1) * (nv + 1));
            for (int j = 0; j <= nv; j++)
            {
                float t = (float)j / nv;
                for (int i = 0; i <= nu; i++)
                {
                    _positions[_vertexCount] = a + u * ((float)i / nu) + v * t;
                    _normals[_vertexCount++] = normal;
                }
            }
            int stride = nu + 1;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int p00 = first + j * stride + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    into.AddTriangle(p00, p10, p11);
                    into.AddTriangle(p00, p11, p01);
                }
            }
            return true;
        }

        static int Cells(float length)
        {
            // The slack keeps a side that is a whole number of cells from gaining one through rounding.
            return math.max(1, (int)math.ceil(length / MaxCellSide - 1e-4f));
        }

        // Longest-edge bisection of what is on the stack. An edge is cut exactly when it is longer than the
        // limit and always at its midpoint, so the two triangles that share it cut it at the same point.
        void Bisect(MeshData into, ref int emitted)
        {
            const double limitSq = (double)MaxEdge * MaxEdge;
            while (_stackCount > 0)
            {
                _stackCount -= 3;
                int a = _stack[_stackCount], b = _stack[_stackCount + 1], c = _stack[_stackCount + 2];
                double ab = DistanceSq(a, b), bc = DistanceSq(b, c), ca = DistanceSq(c, a);
                double longest = Math.Max(ab, Math.Max(bc, ca));
                if (longest <= limitSq)
                {
                    into.AddTriangle(a, b, c);
                    emitted++;
                }
                else
                {
                    // Turn the triangle so that its longest edge is a-b; the order of the corners is kept.
                    if (bc == longest) { int t = a; a = b; b = c; c = t; }
                    else if (ca == longest && ab != longest) { int t = c; c = b; b = a; a = t; }
                    int m = AddMidpoint(a, b);
                    Push(m, b, c);
                    Push(a, m, c);
                }
                if (emitted + _stackCount / 3 > MaxTriangles)
                {
                    emitted = MaxTriangles + 1;
                    _stackCount = 0;
                    return;
                }
            }
        }

        int AddVertex(FallbackTemplate template, int index)
        {
            EnsureVertices(_vertexCount + 1);
            _positions[_vertexCount] = template.Positions[index] * _size;
            // A normal of a scaled surface is the old one divided by the scale, made unit again.
            _normals[_vertexCount] = math.normalizesafe(template.Normals[index] * _inverse, new float3(0f, 1f, 0f));
            return _vertexCount++;
        }

        double DistanceSq(int a, int b)
        {
            float3 p = _positions[a], q = _positions[b];
            double x = (double)p.x - q.x, y = (double)p.y - q.y, z = (double)p.z - q.z;
            return x * x + y * y + z * z;
        }

        int AddMidpoint(int a, int b)
        {
            EnsureVertices(_vertexCount + 1);
            // (p + q) / 2 is the same number whichever end comes first, which is what closes the seams.
            _positions[_vertexCount] = (_positions[a] + _positions[b]) * 0.5f;
            _normals[_vertexCount] = math.normalizesafe(_normals[a] + _normals[b], _normals[a]);
            return _vertexCount++;
        }

        void Push(int a, int b, int c)
        {
            if (_stackCount + 3 > _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
            _stack[_stackCount++] = a;
            _stack[_stackCount++] = b;
            _stack[_stackCount++] = c;
        }

        void EnsureVertices(int count)
        {
            if (count <= _positions.Length) return;
            int size = math.max(count, _positions.Length * 2);
            Array.Resize(ref _positions, size);
            Array.Resize(ref _normals, size);
        }
    }
}
