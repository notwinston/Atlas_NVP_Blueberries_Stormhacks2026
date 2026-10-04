using System;
using Atlas.Kernel;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    // Master Plan §3.4, §8.4: every vertex is defined as a Klein point and placed with the log chart (A.6).
    // Vertices are written in the one layout of the look shader (AtlasVertex, lane A contract §10.2): COLOR.r is the baked AO,
    // TEXCOORD1 on the floor runs from 0 to 1 across the tile, so the shader's lines fall on the tile's edges.
    public sealed class TileMeshGenerator
    {
        public const int MaxVertices = 1024, MaxIndices = 3072;

        readonly TilePalette palette;
        readonly AtlasVertex[] verts = new AtlasVertex[MaxVertices];
        readonly ushort[] idx = new ushort[MaxIndices];
        readonly int[] sideIdx = new int[4 * 17];
        readonly int[] poly = new int[16];
        readonly double3[] nHat = new double3[4];
        readonly double3[] cornerModel = new double3[4];
        readonly double2[] cornerK = new double2[4];
        int vc, ic;

        // per-build state
        int kappa;
        double scale, h;
        int kerbMask, postMask;

        public TileMeshGenerator(TilePalette palette) { this.palette = palette; }

        /// <summary>The vertices and indices of the last Build, valid up to the layout's VertexCount and IndexCount.</summary>
        public AtlasVertex[] Vertices => verts;
        public ushort[] Indices => idx;

        public TileMeshLayout Build(in TileMeshSpec spec, Mesh target)
        {
            var lay = Build(spec);
            Upload(target);
            return lay;
        }

        /// <summary>Engine-free: fills Vertices and Indices and uploads nothing.</summary>
        public TileMeshLayout Build(in TileMeshSpec spec)
        {
            vc = 0; ic = 0;
            kappa = spec.Kappa; scale = spec.Scale; kerbMask = spec.KerbMask; postMask = spec.PostMask;
            h = TileChart.HalfWidth(kappa, spec.Q, scale);
            int n = spec.GridCount, B = spec.BoundarySegments;
            PrepareFrames();

            var lay = new TileMeshLayout();
            BuildFloor(n, B);
            lay.FloorVertices = new RangeInt(0, vc);
            lay.FloorIndices = new RangeInt(0, ic);
            for (int e = 0; e < 4; e++)
            {
                int v0 = vc, i0 = ic;
                if ((kerbMask & (1 << e)) != 0) BuildKerb(e, n, B / n);
                lay.SetKerb(e, new RangeInt(v0, vc - v0), new RangeInt(i0, ic - i0));
            }
            for (int c = 0; c < 4; c++)
            {
                int v0 = vc, i0 = ic;
                if ((postMask & (1 << c)) != 0) BuildPost(c, spec.Q);
                lay.SetPost(c, new RangeInt(v0, vc - v0), new RangeInt(i0, ic - i0));
            }
            lay.VertexCount = vc; lay.IndexCount = ic;
            return lay;
        }

        void Upload(Mesh target)
        {
            float3 mn = verts[0].Position, mx = mn;
            for (int i = 1; i < vc; i++) { mn = math.min(mn, verts[i].Position); mx = math.max(mx, verts[i].Position); }
            var bounds = new Bounds((mn + mx) * 0.5f, mx - mn);
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            var arr = Mesh.AllocateWritableMeshData(1);
            var md = arr[0];
            md.SetVertexBufferParams(vc, AtlasMesh.Layout);
            md.SetIndexBufferParams(ic, IndexFormat.UInt16);
            NativeArray<AtlasVertex>.Copy(verts, md.GetVertexData<AtlasVertex>(), vc);
            NativeArray<ushort>.Copy(idx, md.GetIndexData<ushort>(), ic);
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, ic) { bounds = bounds, vertexCount = vc }, flags);
            Mesh.ApplyAndDisposeWritableMeshData(arr, target, flags);
            target.bounds = bounds;
        }

        static double2 RotE(double2 v, int e)
        {
            for (int i = 0; i < (e & 3); i++) v = new double2(-v.y, v.x);
            return v;
        }

        static double Ip(double3 a, double3 b, double k) => a.x * b.x + a.y * b.y + k * a.z * b.z;
        double C(double s) => kappa < 0 ? Math.Cosh(s) : Math.Cos(s);
        double S(double s) => kappa < 0 ? Math.Sinh(s) : Math.Sin(s);

        double Coord(int i, int B) => i == 0 ? -h : i == B ? h : -h + 2 * h * i / B;

        void PrepareFrames()
        {
            for (int c = 0; c < 4; c++)
            {
                cornerK[c] = RotE(new double2(h, h), c);
                cornerModel[c] = TileChart.KleinToModel(cornerK[c], kappa);
            }
            if (kappa == 0) return;
            // edge-0 line is <N,p> = 0 with N = (1, 0, -h/kappa)
            double3 N = new double3(1, 0, -h / kappa) / Math.Sqrt(1 + h * h / kappa);
            for (int e = 0; e < 4; e++) nHat[e] = new double3(RotE(N.xy, e), N.z);
        }

        int AddV(double2 k, float y, float2 uv, float ao, bool floor = false)
        {
            double2 c = TileChart.KleinToChart(k, kappa, scale);
            byte b = (byte)math.round(255f * ao);
            // Kerbs and posts carry no lines: a constant line coordinate and line weight 0. The shader does not light by the normal.
            float2 line = floor ? new float2((float)(0.5 + 0.5 * (k.x / h)), (float)(0.5 + 0.5 * (k.y / h))) : new float2(0.5f, 0.5f);
            verts[vc] = new AtlasVertex
            {
                Position = new float3((float)c.x, y, (float)c.y), Normal = new float3(0f, 1f, 0f),
                Color = AtlasVertex.PackColor(b, floor ? (byte)255 : (byte)0), Uv0 = uv, Uv1 = line,
            };
            return vc++;
        }

        void Tri(int a, int b, int c) { idx[ic++] = (ushort)a; idx[ic++] = (ushort)b; idx[ic++] = (ushort)c; }
        void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        float FloorAo(double2 k)
        {
            double dmin = double.MaxValue;
            double3 P = TileChart.KleinToModel(k, kappa);
            for (int e = 0; e < 4; e++)
            {
                if ((kerbMask & (1 << e)) == 0) continue;
                double d;
                if (kappa == 0) d = h - RotE(k, 4 - e).x - TileChart.KerbHalfThickness;
                else
                {
                    double ip = Math.Abs(Ip(P, nHat[e], kappa));
                    d = scale * (kappa < 0 ? Math.Asinh(ip) : Math.Asin(Math.Min(1, ip))) - TileChart.KerbHalfThickness;
                }
                dmin = Math.Min(dmin, d);
            }
            for (int c = 0; c < 4; c++)
            {
                if ((postMask & (1 << c)) == 0) continue;
                double d = (kappa == 0 ? math.length(k - cornerK[c]) : scale * Geometry2.Distance(P, cornerModel[c], kappa)) - TileChart.PostRadius;
                dmin = Math.Min(dmin, d);
            }
            if (dmin == double.MaxValue) return 1f;
            return (float)(TileChart.AoMin + (1 - TileChart.AoMin) * math.clamp(dmin / TileChart.AoRange, 0, 1));
        }

        // Master Plan §3.4 step 1, brief §3.3
        void BuildFloor(int n, int B)
        {
            int m = B / n, gw = n + 1;
            float2 uv = palette.Floor;
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    var k = new double2(Coord(i * m, B), Coord(j * m, B));
                    AddV(k, 0, uv, FloorAo(k), true);
                }
            for (int s = 0; s < 4; s++)
                for (int jb = 0; jb <= B; jb++)
                {
                    int q = jb / m;
                    if (jb % m == 0)
                    {
                        sideIdx[s * 17 + jb] = s == 0 ? q : s == 1 ? q * gw + n : s == 2 ? n * gw + q : q * gw;
                        continue;
                    }
                    double u = Coord(jb, B);
                    double2 k = s == 0 ? new double2(u, -h) : s == 1 ? new double2(h, u) : s == 2 ? new double2(u, h) : new double2(-h, u);
                    sideIdx[s * 17 + jb] = AddV(k, 0, uv, FloorAo(k), true);
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * gw + i, b = (j + 1) * gw + i, c = (j + 1) * gw + i + 1, d = j * gw + i + 1;
                    bool ring = m > 1 && (i == 0 || j == 0 || i == n - 1 || j == n - 1);
                    if (!ring) { Tri(a, b, c); Tri(a, c, d); continue; }
                    int pc = 0;
                    poly[pc++] = a;
                    if (i == 0) for (int jb = j * m + 1; jb < (j + 1) * m; jb++) poly[pc++] = sideIdx[3 * 17 + jb];
                    poly[pc++] = b;
                    if (j == n - 1) for (int jb = i * m + 1; jb < (i + 1) * m; jb++) poly[pc++] = sideIdx[2 * 17 + jb];
                    poly[pc++] = c;
                    if (i == n - 1) for (int jb = (j + 1) * m - 1; jb > j * m; jb--) poly[pc++] = sideIdx[1 * 17 + jb];
                    poly[pc++] = d;
                    if (j == 0) for (int jb = (i + 1) * m - 1; jb > i * m; jb--) poly[pc++] = sideIdx[0 * 17 + jb];
                    var mid = new double2((Coord(i * m, B) + Coord((i + 1) * m, B)) * 0.5, (Coord(j * m, B) + Coord((j + 1) * m, B)) * 0.5);
                    int ctr = AddV(mid, 0, uv, FloorAo(mid), true);
                    for (int p = 0; p < pc; p++) Tri(ctr, poly[p], poly[(p + 1) % pc]);
                }
        }

        // brief §3.4
        void BuildKerb(int e, int ks, int m)
        {
            int B = ks * m, row = ks + 1, k0 = vc;
            float2 uv = palette.Kerb;
            // rows OB, OT, IT, IB
            for (int r = 0; r < 4; r++)
                for (int t = 0; t <= ks; t++)
                {
                    double2 bt = RotE(new double2(h, Coord(t * m, B)), e);
                    double2 pk = bt;
                    if (r >= 2)
                    {
                        if (kappa == 0) pk = bt - TileChart.KerbHalfThickness * math.normalize(RotE(new double2(1, 0), e));
                        else
                        {
                            double s = TileChart.KerbHalfThickness / scale;
                            double3 q = C(s) * TileChart.KleinToModel(bt, kappa) - S(s) * nHat[e];
                            pk = TileChart.ModelToKlein(q);
                        }
                    }
                    bool top = r == 1 || r == 2;
                    AddV(pk, top ? TileChart.KerbHeight : 0f, uv, top ? 1f : (float)TileChart.AoMin);
                }
            for (int t = 0; t < ks; t++)
            {
                int OB = k0 + t, OT = k0 + row + t, IT = k0 + 2 * row + t, IB = k0 + 3 * row + t;
                Quad(OB, OT, OT + 1, OB + 1);   // outer, along +N
                Quad(OT, IT, IT + 1, OT + 1);   // top, +y
                Quad(IT, IB, IB + 1, IT + 1);   // inner, along -N
            }
        }

        // brief §3.5
        void BuildPost(int c, int q)
        {
            const int a = TileChart.PostArcSegments;
            float2 uv = palette.Post;
            int p0 = vc;
            double2 corner = cornerK[c];
            double2 pa = RotE(new double2(h, -h), c), pb = RotE(new double2(-h, h), c);
            double theta;
            double3 ta = default, e2 = default, Cm = cornerModel[c];
            double2 ta2 = default, e22 = default;
            if (kappa == 0)
            {
                ta2 = math.normalize(pa - corner); double2 tb2 = math.normalize(pb - corner);
                theta = Math.PI / 2; e22 = tb2;
            }
            else
            {
                double3 Pa = TileChart.KleinToModel(pa, kappa), Pb = TileChart.KleinToModel(pb, kappa);
                ta = Tangent(Cm, Pa); double3 tb = Tangent(Cm, Pb);
                double ipab = Ip(ta, tb, kappa);
                theta = Math.Acos(math.clamp(ipab, -1, 1));
                if (Math.Abs(theta - TileChart.CornerAngle(kappa, q)) > 1e-9) throw new InvalidOperationException("Corner angle mismatch");
                double3 r = tb - ipab * ta;
                e2 = r / Math.Sqrt(Ip(r, r, kappa));
            }
            AddV(corner, TileChart.PostHeight, uv, 1f);
            for (int row = 0; row < 2; row++)
                for (int j = 0; j <= a; j++)
                {
                    double ang = theta * j / a;
                    double2 k;
                    if (kappa == 0) k = corner + TileChart.PostRadius * (Math.Cos(ang) * ta2 + Math.Sin(ang) * e22);
                    else
                    {
                        double sw = TileChart.PostRadius / scale;
                        double3 d = Math.Cos(ang) * ta + Math.Sin(ang) * e2;
                        k = TileChart.ModelToKlein(C(sw) * Cm + S(sw) * d);
                    }
                    AddV(k, row == 0 ? 0f : TileChart.PostHeight, uv, row == 0 ? (float)TileChart.AoMin : 1f);
                }
            for (int j = 0; j < a; j++)
            {
                int AB = p0 + 1 + j, AT = p0 + 2 + a + j;
                Tri(p0, AT, AT + 1);
                Quad(AB, AB + 1, AT + 1, AT);
            }
        }

        double3 Tangent(double3 C0, double3 P)
        {
            double3 t = P - kappa * Ip(C0, P, kappa) * C0;
            return t / Math.Sqrt(Ip(t, t, kappa));
        }
    }
}
