using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    public static class SmallPlanetTestUtil
    {
        public static readonly double[] LapSides = { 1.0, 1.5, 2.0, 3.0, 3.2 };

        // position in tile-zero coordinates (kernel.md §5.8): Frame(face) * U * o
        public static double3 WorldPoint(int face, in Iso2 u) => (CubeTiling.Frame(face) * new Iso2(u.M, 1)).M.c2;

        public static IEnumerable<PlanetHead> Feed(double2 start, double2[] legs)
        {
            yield return new PlanetHead(new double3(start.x, 1.6, start.y), true);
            double2 a = start;
            foreach (var leg in legs)
            {
                double len = math.length(leg);
                int n = (int)Math.Ceiling(len / 0.01);
                for (int j = 1; j <= n; j++)
                {
                    double2 p = a + leg * ((double)j / n);
                    yield return new PlanetHead(new double3(p.x, 1.6, p.y), true);
                }
                a += leg;
            }
        }

        public static float4x4 ToF4(Matrix4x4 m) => new float4x4(m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), m.GetColumn(3));

        public static SmallPlanetSpace NewSpace(double s)
        {
            var sp = new SmallPlanetSpace();
            sp.Configure(new SmallPlanetRoom(s));
            return sp;
        }

        public static double MaxAbsDiff(in double3x3 a, in double3x3 b)
            => math.max(math.cmax(math.abs(a.c0 - b.c0)), math.max(math.cmax(math.abs(a.c1 - b.c1)), math.cmax(math.abs(a.c2 - b.c2))));

        public static Iso2 ExactRot(int j)
        {
            var r = new Iso2(new double3x3(0, -1, 0, 1, 0, 0, 0, 0, 1), 1);
            var m = new Iso2(double3x3.identity, 1);
            for (int i = 0; i < ((j % 4) + 4) % 4; i++) m = m * r;
            return m;
        }

        public static bool ExactEq(in Iso2 a, in Iso2 b) => a.M.c0.Equals(b.M.c0) && a.M.c1.Equals(b.M.c1) && a.M.c2.Equals(b.M.c2);
    }
}
