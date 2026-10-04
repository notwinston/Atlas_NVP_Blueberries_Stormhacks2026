using System;
using System.Collections.Generic;
using Atlas.Blades.Lung.AirwayKit;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    // Shared helpers for the P2-B02 tests (public API only).
    public static class KitTestUtil
    {
        public const double RIn = 0.5306375309525181;   // acosh(cos(pi/5)/sin(pi/4)), brief 2.2
        public const double FloatTol = 2e-6;            // float32 vertex storage (deviation: brief's 1e-9 applies to the double path)
        public static readonly double[] SideS = { 1.0, 2.0, 3.2 };   // loop-fit range, MP 4.4 / App. A
        public static readonly double[] FlatG = { 1.0, 1.5 };
        public static readonly KitKind[] Kinds = { KitKind.Fork, KitKind.Straight, KitKind.BendRight, KitKind.BendLeft, KitKind.End };
        public static readonly TileLod[] Lods = { TileLod.Near, TileLod.Mid, TileLod.Far };

        public static double LFromS(double s) => s / (2 * RIn);

        // (kappa, scale) pairs: three L values and two flat cells.
        public static List<(int kappa, double scale)> Geoms()
        {
            var l = new List<(int, double)>();
            foreach (var s in SideS) l.Add((-1, LFromS(s)));
            foreach (var g in FlatG) l.Add((0, g));
            return l;
        }

        public static TileMeshSpec Spec(int kappa, double scale, TileLod lod)
            => kappa < 0 ? TileMeshSpec.Hyperbolic(5, scale, lod, 0, 0) : TileMeshSpec.FlatCell(scale, 0, 0);

        public static AirwayKitMeshBuilder NewBuilder() => new AirwayKitMeshBuilder(TilePalette.Default, AirwayPalette.Default);

        // The vertices and indices of the builder's last Build (engine-free), copied: count is the layout's VertexCount or IndexCount.
        public static AtlasVertex[] Verts(AirwayKitMeshBuilder b, int count) => Copy(b.Vertices, count);
        public static ushort[] Idx(AirwayKitMeshBuilder b, int count) => Copy(b.Indices, count);

        public static T[] Copy<T>(T[] source, int count)
        {
            var copy = new T[count];
            Array.Copy(source, copy, count);
            return copy;
        }

        public static HashSet<int> Used(ushort[] idx, RangeInt r)
        {
            var s = new HashSet<int>();
            for (int i = r.start; i < r.start + r.length; i++) s.Add(idx[i]);
            return s;
        }

        public static double MaxEdge(AtlasVertex[] v, ushort[] idx, RangeInt r)
        {
            double mx = 0;
            for (int i = r.start; i < r.start + r.length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    var a = v[idx[i + k]].Position; var b = v[idx[i + (k + 1) % 3]].Position;
                    mx = Math.Max(mx, math.distance((double3)a, (double3)b));
                }
            return mx;
        }

        public static double2 Chart(AtlasVertex v) => new double2(v.Position.x, v.Position.z);

        public static List<int> OpenArms(KitKind k)
        {
            var l = new List<int>(); int m = KitGeometry.KitMask(k);
            for (int e = 0; e < 4; e++) if ((m & (1 << e)) != 0) l.Add(e);
            return l;
        }

        public static int RotL(int m, int k) => ((m << k) | (m >> (4 - k))) & 15;

        public static double3 Model(double2 chart, int kappa, double scale)
            => Geometry2.Exp(kappa < 0 ? chart / scale : chart, kappa);

        public static double2 FromModel(double3 p, int kappa, double scale)
            => kappa < 0 ? Geometry2.Log(p, kappa) * scale : Geometry2.Log(p, kappa);

        public static double2 Apply(Iso2 f, double2 chart, int kappa, double scale)
            => FromModel(math.mul(f.M, Model(chart, kappa, scale)), kappa, scale);

        // Edge-e geodesic distance |<Exp(chart/L), n_e>| * L (hyperbolic), kappa = -1.
        public static double EdgeResidual(double2 chart, int e, double L)
        {
            double h = Math.Tanh(RIn);
            double3 N = new double3(1, 0, h) / Math.Sqrt(1 - h * h);
            double2 nxy = N.xy;
            for (int i = 0; i < e; i++) nxy = new double2(-nxy.y, nxy.x);
            double3 P = Geometry2.Exp(chart / L, -1);
            return Math.Abs(nxy.x * P.x + nxy.y * P.y - N.z * P.z) * L;
        }
    }
}
