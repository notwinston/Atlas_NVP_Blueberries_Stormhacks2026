// Lane A engine-free core: small generators of test bodies (tesseract, moving-sphere tubes) for the
// acceptance checks of P2-A20 and P2-A28. Plain C#, System only. Load time only: allocates.
using System;
using System.Collections.Generic;

namespace Atlas.Lung.Core
{
    public static class TubeGenerator
    {
        /// <summary>
        /// Boundary of the unit tesseract [0, 1]^4: 8 cubes of 6 Kuhn tetrahedra, 48 in all (Appendix A.8 check).
        /// Cell id = the cube. Oriented outward.
        /// </summary>
        public static TubeMesh Tesseract()
        {
            var mesh = new TubeMesh();
            mesh.Vertices = new float[16 * 4];
            for (int i = 0; i < 16; i++)
                for (int a = 0; a < 4; a++) mesh.Vertices[4 * i + a] = (i >> (3 - a)) & 1;
            var tets = new List<int>();
            var cells = new List<int>();
            int[] free = new int[3];
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            for (int axis = 0; axis < 4; axis++)
            {
                for (int val = 0; val < 2; val++)
                {
                    int n = 0;
                    for (int a = 0; a < 4; a++) if (a != axis) free[n++] = a;
                    foreach (int[] perm in perms)
                    {
                        int v = val << (3 - axis);
                        tets.Add(v);
                        for (int step = 0; step < 3; step++)
                        {
                            v |= 1 << (3 - free[perm[step]]);
                            tets.Add(v);
                        }
                        cells.Add(2 * axis + val);
                    }
                }
            }
            mesh.Tetrahedra = tets.ToArray();
            mesh.TetCell = cells.ToArray();
            mesh.CellCount = 8;
            mesh.SlabStart = TubeLoader.SlabStarts(mesh.TetCell, 8);
            Orientation.OrientOutward(mesh.Vertices, mesh.Tetrahedra);
            mesh.Oriented = true;
            return mesh;
        }

        /// <summary>
        /// Unit icosphere, built exactly as in documents/research/scripts/R2_slicer_proto.py: 12 vertices and
        /// 20 triangles, each subdivision splits every triangle in four (2 subdivisions: 162 vertices, 320 triangles).
        /// </summary>
        public static void Icosphere(int subdivisions, out double[] vertices, out int[] triangles)
        {
            double t = (1.0 + Math.Sqrt(5.0)) / 2.0;
            double[] seed = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
            int[] faces = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var v = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                double x = seed[3 * i], y = seed[3 * i + 1], z = seed[3 * i + 2], len = Math.Sqrt(x * x + y * y + z * z);
                v.Add(x / len); v.Add(y / len); v.Add(z / len);
            }
            var f = new List<int>(faces);
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var f2 = new List<int>(f.Count * 4);
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(v, cache, a, b), bc = Mid(v, cache, b, c), ca = Mid(v, cache, c, a);
                    f2.Add(a); f2.Add(ab); f2.Add(ca);
                    f2.Add(b); f2.Add(bc); f2.Add(ab);
                    f2.Add(c); f2.Add(ca); f2.Add(bc);
                    f2.Add(ab); f2.Add(bc); f2.Add(ca);
                }
                f = f2;
            }
            vertices = v.ToArray();
            triangles = f.ToArray();
        }

        static int Mid(List<double> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = ((long)Math.Min(a, b) << 32) | (long)Math.Max(a, b);
            int at;
            if (cache.TryGetValue(key, out at)) return at;
            double x = v[3 * a] + v[3 * b], y = v[3 * a + 1] + v[3 * b + 1], z = v[3 * a + 2] + v[3 * b + 2];
            double len = Math.Sqrt(x * x + y * y + z * z);
            v.Add(x / len); v.Add(y / len); v.Add(z / len);
            at = v.Count / 3 - 1;
            cache[key] = at;
            return at;
        }

        /// <summary>The illustrative breathing curve (sections 10.2.4 and A.8): z(t) = -b cos^(2n)(pi t / period), z0 = 0.</summary>
        public static double LujanZ(double tSeconds, double b, double periodSeconds, int n = 2)
        {
            double c = Math.Cos(Math.PI * tSeconds / periodSeconds);
            return -b * Math.Pow(c * c, n);
        }

        /// <summary>
        /// A closed surface carried along the table axis z through a list of rings: ring k is the surface
        /// scaled by <paramref name="scale"/> and moved to z = ringZ[k], at w = ringW[k]. Each triangle between
        /// two rings is a prism split into three tetrahedra with its indices sorted (A.8), stored slab by slab
        /// like the case contract. Cell = the slab between rings k and k + 1. Vertex data = the ring's w
        /// divided by <paramref name="wPerPeriod"/>. Oriented outward.
        /// </summary>
        public static TubeMesh Tube(double[] surface, int[] triangles, double scale, double[] ringZ, double[] ringW,
                                    double wPerPeriod, bool wraps)
        {
            int rings = ringZ.Length, nv = surface.Length / 3, nf = triangles.Length / 3;
            if (rings < 2 || ringW.Length != rings) throw new ArgumentException("A tube needs two or more rings.");
            var mesh = new TubeMesh();
            mesh.Vertices = new float[4 * rings * nv];
            mesh.VertexData = new float[rings * nv];
            for (int k = 0; k < rings; k++)
            {
                for (int i = 0; i < nv; i++)
                {
                    int o = 4 * (k * nv + i);
                    mesh.Vertices[o] = (float)(scale * surface[3 * i]);
                    mesh.Vertices[o + 1] = (float)(scale * surface[3 * i + 1]);
                    mesh.Vertices[o + 2] = (float)(scale * surface[3 * i + 2] + ringZ[k]);
                    mesh.Vertices[o + 3] = (float)ringW[k];
                    mesh.VertexData[k * nv + i] = (float)(ringW[k] / wPerPeriod);
                }
            }
            int slabs = rings - 1;
            mesh.Tetrahedra = new int[12 * nf * slabs];
            mesh.TetCell = new int[3 * nf * slabs];
            int at = 0;
            for (int k = 0; k < slabs; k++)
            {
                int lo = k * nv, hi = lo + nv;
                for (int f = 0; f < nf; f++)
                {
                    int a = triangles[3 * f], b = triangles[3 * f + 1], c = triangles[3 * f + 2];
                    if (a > b) { int x = a; a = b; b = x; }
                    if (b > c) { int x = b; b = c; c = x; }
                    if (a > b) { int x = a; a = b; b = x; }
                    int[] T = mesh.Tetrahedra;
                    mesh.TetCell[at / 4] = k; T[at++] = lo + a; T[at++] = lo + b; T[at++] = lo + c; T[at++] = hi + c;
                    mesh.TetCell[at / 4] = k; T[at++] = lo + a; T[at++] = lo + b; T[at++] = hi + b; T[at++] = hi + c;
                    mesh.TetCell[at / 4] = k; T[at++] = lo + a; T[at++] = hi + a; T[at++] = hi + b; T[at++] = hi + c;
                }
            }
            mesh.CellCount = slabs;
            mesh.SlabStart = TubeLoader.SlabStarts(mesh.TetCell, slabs);
            mesh.WPeriod = wraps ? (float)(ringW[rings - 1] - ringW[0]) : 0f;
            Orientation.OrientOutward(mesh.Vertices, mesh.Tetrahedra);
            mesh.Oriented = true;
            return mesh;
        }

        /// <summary>
        /// Worldtube of a sphere on the breathing curve, as in R2_slicer_proto.py: an icosphere of the given
        /// diameter at z = LujanZ(t), sampled at <paramref name="phasesPerPeriod"/> instants per period over
        /// <paramref name="periods"/> periods (one more ring closes the last), with w = wPerSecond * t.
        /// Lengths are in whatever unit diameter, b and wPerSecond use; <paramref name="unitsPerMm"/> records it.
        /// </summary>
        public static TubeMesh LujanSphere(double diameter, double b, double periodSeconds, int phasesPerPeriod, int periods,
                                           double wPerSecond, int subdivisions = 2, double unitsPerMm = 1.0)
        {
            double[] surface; int[] triangles;
            Icosphere(subdivisions, out surface, out triangles);
            int rings = phasesPerPeriod * periods + 1;
            var z = new double[rings];
            var w = new double[rings];
            for (int k = 0; k < rings; k++)
            {
                double t = k * periodSeconds / phasesPerPeriod;
                z[k] = LujanZ(t, b, periodSeconds);
                w[k] = wPerSecond * t;
            }
            TubeMesh mesh = Tube(surface, triangles, 0.5 * diameter, z, w, wPerSecond * periodSeconds, periods == 1);
            mesh.UnitsPerMm = (float)unitsPerMm;
            return mesh;
        }

        /// <summary>The same sphere moving at constant speed u along z for the unit test D v / (v - u) of A.8.</summary>
        public static TubeMesh ConstantSpeedSphere(double diameter, double u, double seconds, int rings, double wPerSecond, int subdivisions = 2)
        {
            double[] surface; int[] triangles;
            Icosphere(subdivisions, out surface, out triangles);
            var z = new double[rings];
            var w = new double[rings];
            for (int k = 0; k < rings; k++)
            {
                double t = k * seconds / (rings - 1);
                z[k] = u * t;
                w[k] = wPerSecond * t;
            }
            return Tube(surface, triangles, 0.5 * diameter, z, w, wPerSecond * seconds, false);
        }
    }
}
