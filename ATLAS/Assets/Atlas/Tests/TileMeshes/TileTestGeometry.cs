using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.TileMeshes.Tests
{
    public static class TileTestGeometry
    {
        public static readonly double3 O = new double3(0, 0, 1);

        // Master Plan A.3: 2r = acosh(phi) for {4,5}, acosh 2 for {4,6}, pi/2 for {4,3}
        public static double TwoR(int kappa, int q)
        {
            if (kappa > 0) return Math.PI / 2;
            return Math.Acosh(q == 5 ? (1 + Math.Sqrt(5)) / 2 : 2.0);
        }

        // Master Plan A.5: S_k = Rot(k pi/2) Boost(2r, 0) Rot(pi)
        public static Iso2 Step(int k, int kappa, int q)
            => Iso2.Rot(k * Math.PI / 2) * Iso2.Boost(new double2(TwoR(kappa, q), 0), kappa) * Iso2.Rot(Math.PI);

        public static double Dist(Iso2 f) => Geometry2.Distance(math.mul(f.M, O), O, -1);

        // BFS over step isometries; tile centres within maxD0 (units of L) of tile zero.
        public static List<Iso2> Tiles(double maxD0, int q = 5)
        {
            var frames = new List<Iso2> { Iso2.Identity };
            var centres = new List<double3> { O };
            for (int head = 0; head < frames.Count; head++)
                for (int k = 0; k < 4; k++)
                {
                    var g = frames[head] * Step(k, -1, q);
                    var c = math.mul(g.M, O);
                    if (Geometry2.Distance(c, O, -1) > maxD0) continue;
                    bool dup = false;
                    for (int i = 0; i < centres.Count && !dup; i++) dup = math.length(centres[i] - c) < 1e-6;
                    if (dup) continue;
                    frames.Add(g); centres.Add(c);
                }
            return frames;
        }

        public static double3x3 ToFloat(double3x3 m)
        {
            return new double3x3((float3x3)m);
        }

        public static double3 Warp(float3 pos, double3x3 A, double L)
        {
            var p = Geometry2.Exp(new double2(pos.x, pos.z) / L, -1);
            var t = Geometry2.Log(math.mul(A, p), -1) * L;
            return new double3(t.x, pos.y, t.y);
        }

        public static Iso2 Walker(double2 w)
            => Iso2.Boost(Geometry2.Log(TileChart.KleinToModel(w, -1), -1), -1);

        public static double2 Rot(double2 v, int e)
        {
            for (int i = 0; i < ((e % 4) + 4) % 4; i++) v = new double2(-v.y, v.x);
            return v;
        }

        public static double Ip(double3 a, double3 b, double k) => a.x * b.x + a.y * b.y + k * a.z * b.z;
    }
}
