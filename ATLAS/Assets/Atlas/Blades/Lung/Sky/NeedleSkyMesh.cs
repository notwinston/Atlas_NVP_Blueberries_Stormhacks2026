using System.Collections.Generic;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>One inverted sphere (MP §10.2.5): triangles wound inward, normals inward. Pole rows emit one triangle per quad (no degenerates).</summary>
    public static class NeedleSkyMesh
    {
        public static Mesh Build(int lon = 32, int lat = 16, float radius = NeedleSkyConstants.SphereRadiusM)
        {
            var v = new Vector3[(lon + 1) * (lat + 1)];
            var n = new Vector3[v.Length];
            for (int y = 0; y <= lat; y++)
            {
                float el = Mathf.PI / 2f - Mathf.PI * y / lat;
                for (int x = 0; x <= lon; x++)
                {
                    float az = 2f * Mathf.PI * x / lon;
                    var d = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                    v[y * (lon + 1) + x] = d * radius; n[y * (lon + 1) + x] = -d;
                }
            }
            var t = new List<int>(lon * lat * 6);
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    if (y > 0) Tri(v, t, a, b, c);               // degenerate at the north pole (a == b in space): skipped
                    if (y < lat - 1) Tri(v, t, b, d, c);         // degenerate at the south pole (c == d): skipped
                }
            var m = new Mesh { name = "NeedleSkySphere" };
            m.vertices = v; m.normals = n; m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2f);
            return m;
        }

        // Appends the triangle wound so that cross(b-a, c-a) points inward (front face seen from inside under Cull Back).
        static void Tri(Vector3[] v, List<int> t, int i, int j, int k)
        {
            var c = (v[i] + v[j] + v[k]) / 3f;
            if (Vector3.Dot(Vector3.Cross(v[j] - v[i], v[k] - v[i]), c) < 0) { t.Add(i); t.Add(j); t.Add(k); }
            else { t.Add(i); t.Add(k); t.Add(j); }
        }
    }
}
