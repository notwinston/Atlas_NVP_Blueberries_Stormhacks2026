using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // {4,3} tiling: the six faces of a cube blown onto the unit sphere (Master Plan A.3, A.5; kernel.md §5.8).
    // All frames are exact integer matrices: A.2 with c = cos(pi/2) = 0 and s = sin(pi/2) = 1, never built from Math.Cos.
    public static class CubeTiling
    {
        public const int FaceCount = 6;
        public const double InRadius = Math.PI / 4;      // A.3 in-radius r
        public const double CentreSpacing = Math.PI / 2; // A.3 centre spacing 2r
        public static readonly double Circumradius = Math.Atan(Math.Sqrt(2)); // A.3: cos r_c = cot(pi/4) cot(pi/3)

        static readonly Iso2[] stepMat = new Iso2[4];
        static readonly Iso2[] frames = new Iso2[FaceCount];
        static readonly Iso2[] steps = new Iso2[FaceCount * 4];
        static readonly Iso2[] chains = new Iso2[FaceCount * FaceCount];
        static readonly int[] nbr = new int[FaceCount * 4];
        static readonly int[] jTab = new int[FaceCount * 4];
        static readonly TileId[] ids = new TileId[FaceCount];

        static Iso2 Make(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
            => new Iso2(new double3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22), 1);

        static bool Eq(in Iso2 a, in Iso2 b) => a.M.c0.Equals(b.M.c0) && a.M.c1.Equals(b.M.c1) && a.M.c2.Equals(b.M.c2);

        static Iso2 Transposed(in Iso2 a) => new Iso2(math.transpose(a.M), 1);

        static CubeTiling()
        {
            var rot = Make(0, -1, 0, 1, 0, 0, 0, 0, 1);       // A.5 Rot(+90 deg)
            var t = Make(0, 0, 1, 0, 1, 0, -1, 0, 0);          // Boost((pi/2, 0), +1), exact
            var h = Make(-1, 0, 0, 0, -1, 0, 0, 0, 1);        // Rot(pi)
            var rk = Make(1, 0, 0, 0, 1, 0, 0, 0, 1);
            for (int k = 0; k < 4; k++) { stepMat[k] = rk * t * h; rk = rk * rot; } // S_k = R^k T H (A.5)
            frames[0] = Make(1, 0, 0, 0, 1, 0, 0, 0, 1);
            for (int k = 0; k < 4; k++) frames[k + 1] = stepMat[k];
            frames[5] = stepMat[0] * stepMat[2]; // deviation D3: the plan is silent on numbering
            for (int f = 0; f < FaceCount; f++) ids[f] = new TileId(f, 0, 0, 0, 0, 0);
            for (int a = 0; a < FaceCount; a++)
                for (int b = 0; b < FaceCount; b++) chains[a * FaceCount + b] = Transposed(frames[a]) * frames[b];

            for (int f = 0; f < FaceCount; f++)
                for (int k = 0; k < 4; k++)
                {
                    double3 c = (frames[f] * stepMat[k]).M.c2;
                    int g = -1;
                    for (int q = 0; q < FaceCount; q++) if (frames[q].M.c2.Equals(c)) g = q;
                    if (g < 0) throw new InvalidOperationException("CubeTiling: step leaves the face table");
                    var G = chains[f * FaceCount + g];
                    int j = -1;
                    var rj = Make(1, 0, 0, 0, 1, 0, 0, 0, 1);
                    for (int jj = 0; jj < 4; jj++)
                    {
                        if (Eq(G, stepMat[k] * rj)) { j = jj; break; }
                        rj = rj * rot;
                    }
                    if (j < 0) throw new InvalidOperationException("CubeTiling: no J with G = S_k R^J");
                    nbr[f * 4 + k] = g; jTab[f * 4 + k] = j; steps[f * 4 + k] = G;
                }
        }

        static void CheckFace(int f) { if ((uint)f >= FaceCount) throw new ArgumentOutOfRangeException(nameof(f)); }
        static void CheckEdge(int k) { if ((uint)k >= 4) throw new ArgumentOutOfRangeException(nameof(k)); }

        public static TileId FaceId(int face) { CheckFace(face); return ids[face]; }

        public static int FaceOf(in TileId id)
        {
            for (int f = 0; f < FaceCount; f++) if (id == ids[f]) return f;
            return -1;
        }

        public static Iso2 StepMatrix(int edge) { CheckEdge(edge); return stepMat[edge]; }
        public static Iso2 Frame(int face) { CheckFace(face); return frames[face]; }
        public static int Neighbour(int face, int edge) { CheckFace(face); CheckEdge(edge); return nbr[face * 4 + edge]; }
        public static int BackEdge(int face, int edge) { CheckFace(face); CheckEdge(edge); return (4 - jTab[face * 4 + edge]) & 3; }
        public static Iso2 Step(int face, int edge) { CheckFace(face); CheckEdge(edge); return steps[face * 4 + edge]; }
        public static Iso2 Chain(int from, int to) { CheckFace(from); CheckFace(to); return chains[from * FaceCount + to]; }

        public static int Antipode(int face)
        {
            CheckFace(face);
            double3 c = frames[face].M.c2;
            for (int q = 0; q < FaceCount; q++) if (frames[q].M.c2.Equals(-c)) return q;
            throw new InvalidOperationException();
        }

        public static int GraphDistance(int a, int b) => a == b ? 0 : Antipode(a) == b ? 2 : 1;
    }

    // kernel.md §4.6: on q = 3 all six tiles are always returned.
    public sealed class CubeTileGraph : ITileGraph
    {
        public int Q => 3;

        // Merge fix (P1-B09 worktree): P1-B04's VisibleTile is a readonly struct (Id, F, GraphDistance); F = Chain(from, face).
        static VisibleTile Make(int face, int from)
            => new VisibleTile(CubeTiling.FaceId(face), CubeTiling.Chain(from, face), CubeTiling.GraphDistance(from, face));

        public void VisibleSet(TileId centre, double rho, List<VisibleTile> into)
        {
            int c = CubeTiling.FaceOf(centre);
            if (c < 0) throw new ArgumentException("not a face id", nameof(centre));
            into.Clear();
            into.Add(Make(c, c));
            for (int k = 0; k < 4; k++) into.Add(Make(CubeTiling.Neighbour(c, k), c));
            into.Add(Make(CubeTiling.Antipode(c), c));
        }
    }
}
