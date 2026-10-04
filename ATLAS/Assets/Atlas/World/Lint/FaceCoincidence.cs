using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.World.Lint
{
    // Master Plan §3.4 "no coincident faces": Near meshes of two neighbouring tiles, the second mapped into the first's chart,
    // triangles within a band of the shared edge compared by centroid-to-triangle distance (D7).
    sealed class FaceCoincidence
    {
        readonly TileGraph graph;
        readonly double L, tol, band;
        readonly TileMeshCache meshes;
        readonly Dictionary<long, int> memo = new Dictionary<long, int>();

        public FaceCoincidence(TileGraph graph, double L, TileMeshCache meshes, double tolM = 0.001, double bandM = 0.10)
        { this.graph = graph; this.L = L; this.meshes = meshes; tol = tolM; band = bandM; }

        // t's edge e meets n's edge e2. Returns the number of coincident triangle pairs (both directions).
        public int Count(TileId t, int e, int kerbT, TileId n, int e2, int kerbN) =>
            Count(t, e, new WorldTileKey(Archetype.Plaza, kerbT), n, e2, new WorldTileKey(Archetype.Plaza, kerbN));

        // The same on the meshes the tiles are drawn with: shell, kerbs, wedges and props (P1-B07).
        public int Count(TileId t, int e, in WorldTileKey keyT, TileId n, int e2, in WorldTileKey keyN)
        {
            long key = ((long)((keyT.Code << 2) | e) << 16) | (uint)((keyN.Code << 2) | e2);
            if (memo.TryGetValue(key, out int r)) return r;
            var mt = meshes.Get(TileLod.Near, keyT);
            var mn = meshes.Get(TileLod.Near, keyN);
            // n's vertices into t's chart: P' = G * exp(x / L), chart = L log(P') (A.2, §4.6)
            var G = graph.StepMatrix(t, e).M;
            var vn = new Vector3[mn.Vertices.Length];
            for (int i = 0; i < vn.Length; i++)
            {
                var v = mn.Vertices[i];
                double3 P = math.mul(G, Geometry2.Exp(new double2(v.x, v.z) / L, -1));
                double2 c = L * Geometry2.Log(P, -1);
                vn[i] = new Vector3((float)c.x, v.y, (float)c.y);
            }
            // edge e's line: <P, N_e> = 0, N_e = R^e (cosh r, 0, sinh r) (A.3)
            double ch = Math.Cosh(graph.InRadius), sh = Math.Sinh(graph.InRadius);
            double2 nxz = e == 0 ? new double2(ch, 0) : e == 1 ? new double2(0, ch) : e == 2 ? new double2(-ch, 0) : new double2(0, -ch);
            var tt = Band(mt.Vertices, mt.Indices, nxz, sh);
            var tn = Band(vn, mn.Indices, nxz, sh);
            r = CountClose(mt.Vertices, mt.Indices, tt, vn, mn.Indices, tn) + CountClose(vn, mn.Indices, tn, mt.Vertices, mt.Indices, tt);
            memo[key] = r;
            return r;
        }

        List<int> Band(Vector3[] v, int[] idx, double2 nxz, double sh)
        {
            var list = new List<int>();
            for (int i = 0; i < idx.Length; i += 3)
            {
                var c = (v[idx[i]] + v[idx[i + 1]] + v[idx[i + 2]]) / 3f;
                double3 P = Geometry2.Exp(new double2(c.x, c.z) / L, -1);
                double ip = P.x * nxz.x + P.y * nxz.y - P.z * sh; // <P, N_e>
                if (L * Math.Asinh(Math.Abs(ip)) <= band) list.Add(i);
            }
            return list;
        }

        int CountClose(Vector3[] va, int[] ia, List<int> ta, Vector3[] vb, int[] ib, List<int> tb)
        {
            int count = 0;
            foreach (int i in ta)
            {
                var c = (va[ia[i]] + va[ia[i + 1]] + va[ia[i + 2]]) / 3f;
                foreach (int j in tb)
                    if (Dist(c, vb[ib[j]], vb[ib[j + 1]], vb[ib[j + 2]]) <= tol) { count++; break; }
            }
            return count;
        }

        // Closest point on a triangle (Ericson, Real-Time Collision Detection 5.1.5).
        static double Dist(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return (p - a).magnitude;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return (p - b).magnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).magnitude;
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return (p - c).magnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).magnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).magnitude;
            float den = 1f / (va + vb + vc);
            return (p - (a + ab * (vb * den) + ac * (vc * den))).magnitude;
        }
    }
}
