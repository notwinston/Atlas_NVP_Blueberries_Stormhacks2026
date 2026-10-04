using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    // P1-B11 [D10]: the walls of the Home rebuild sunk to kerbs for curved space (Master Plan §3.5, §4.6): each wall a-b of
    // RoomModel.walls becomes a two-sided strip of height heightM, split into the intervals between the openings that start below
    // that height (door gaps stay open). Columns of at most 0.18 m by six 0.15 m rows, so no edge (diagonals included) exceeds 0.234 m
    // (the warp bends long edges, MP §8.4). Both windings, opposite horizontal normals, palette Kerb texel, AO 255.
    public static class KerbMesh
    {
        public const float ColumnM = 0.18f, RowM = 0.15f;

        public static int Build(Wall[] walls, float heightM, Mesh target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uvs = new List<Vector2>(); var col = new List<Color32>();
            var idx = new List<int>();
            var uv = new Vector2(TilePalette.Provisional.Kerb.x, TilePalette.Provisional.Kerb.y);
            var white = new Color32(255, 255, 255, 255);
            int rows = Mathf.Max(1, Mathf.CeilToInt(heightM / RowM - 1e-4f));
            var gaps = new List<float2>();
            if (walls != null)
                foreach (var w in walls)
                {
                    var a = new Vector2(w.a.x, w.a.z); var b = new Vector2(w.b.x, w.b.z);
                    float len = (b - a).magnitude;
                    if (len < 1e-4f) continue;
                    Vector2 d = (b - a) / len;
                    var n = new Vector3(d.y, 0, -d.x);   // horizontal normal of side A; side B is -n
                    gaps.Clear();
                    if (w.openings != null)
                        foreach (var o in w.openings)
                            if (o.bottom < heightM) gaps.Add(new float2(Mathf.Clamp(o.from, 0, len), Mathf.Clamp(o.to, 0, len)));
                    gaps.Sort((p, q) => p.x.CompareTo(q.x));
                    float cursor = 0;
                    foreach (var g in gaps)
                    {
                        if (g.x > cursor) Strip(a, d, n, cursor, g.x, heightM, rows, uv, white, pos, nrm, uvs, col, idx);
                        cursor = Mathf.Max(cursor, g.y);
                    }
                    if (len > cursor) Strip(a, d, n, cursor, len, heightM, rows, uv, white, pos, nrm, uvs, col, idx);
                }
            target.Clear();
            if (pos.Count > 65000) target.indexFormat = IndexFormat.UInt32;
            if (pos.Count > 0)
            {
                target.SetVertices(pos); target.SetNormals(nrm); target.SetUVs(0, uvs); target.SetColors(col);
                target.SetTriangles(idx, 0, false);
                target.RecalculateBounds();
            }
            return idx.Count / 3;
        }

        static void Strip(Vector2 a, Vector2 d, Vector3 n, float s0, float s1, float height, int rows, Vector2 uv, Color32 white,
            List<Vector3> pos, List<Vector3> nrm, List<Vector2> uvs, List<Color32> col, List<int> idx)
        {
            float len = s1 - s0;
            if (len < 1e-4f) return;
            int cols = Mathf.Max(1, Mathf.CeilToInt(len / ColumnM - 1e-4f));
            for (int side = 0; side < 2; side++)
            {
                int b0 = pos.Count;
                Vector3 nn = side == 0 ? n : -n;
                for (int j = 0; j <= rows; j++)
                    for (int i = 0; i <= cols; i++)
                    {
                        float s = s0 + len * i / cols;
                        pos.Add(new Vector3(a.x + d.x * s, height * j / rows, a.y + d.y * s));
                        nrm.Add(nn); uvs.Add(uv); col.Add(white);
                    }
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < cols; i++)
                    {
                        int p00 = b0 + j * (cols + 1) + i, p10 = p00 + 1, p01 = p00 + cols + 1, p11 = p01 + 1;
                        if (side == 0) { idx.Add(p00); idx.Add(p01); idx.Add(p11); idx.Add(p00); idx.Add(p11); idx.Add(p10); }
                        else { idx.Add(p00); idx.Add(p11); idx.Add(p01); idx.Add(p00); idx.Add(p10); idx.Add(p11); }
                    }
            }
        }
    }
}
