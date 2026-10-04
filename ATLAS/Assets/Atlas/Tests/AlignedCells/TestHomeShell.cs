// PENDING DELETION (with its .meta): replaced by RoomFixtures and the IRoomContent double in Fixtures.cs. Compiled out, text untouched below; the integration agent could not delete files.
#if ATLAS_PENDING_DELETION
using System;
using System.Collections.Generic;
using Atlas.World;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // IHomeShell test double (placeholders.md S3): tests use their own Home, never Atlas.Scan or HomeRebuild.
    sealed class TestHomeShell : IHomeShell
    {
        public enum Mode { Functional, BudgetStandIns, PerObject }

        readonly RoomObject[] records;
        readonly Mesh[] meshes;
        readonly List<Mesh> owned = new List<Mesh>();

        public Mesh Floor { get; }
        public Mesh Walls { get; }
        public int ObjectCount => records.Length;
        public RoomObject ObjectRecord(int i) => records[i];
        public Mesh ObjectMesh(int i) => meshes[i];
        public event Action<int> ObjectMeshChanged;

        public TestHomeShell(RoomModel room, Mode mode = Mode.Functional, int perObjectTris = 0)
        {
            records = room.objects ?? new RoomObject[0];
            meshes = new Mesh[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                Vector3 s = records[i].size;
                switch (mode)
                {
                    case Mode.Functional: meshes[i] = Box(s, 1, 1, 1); break;
                    case Mode.BudgetStandIns: meshes[i] = Box(s, StandInDivisions(s)); break;
                    default: meshes[i] = Box(s, PerObjectDivisions(perObjectTris)); break;
                }
                owned.Add(meshes[i]);
            }
            Walls = BuildWalls(room); Floor = BuildFloor(room);
            owned.Add(Walls); owned.Add(Floor);
        }

        // Master Plan §4.3 budget_room: 5,000 triangles each; [P1-B12 D9] (30, 20, 13) along the 0.5, 0.8, 0.3 m edges, by edge rank.
        static Vector3Int StandInDivisions(Vector3 s)
        {
            float[] v = { s.x, s.y, s.z };
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => v[b].CompareTo(v[a]));   // largest first
            var d = new int[3];
            d[order[0]] = 20; d[order[1]] = 30; d[order[2]] = 13;
            return new Vector3Int(d[0], d[1], d[2]);
        }

        // 3,000 triangles: (15, 10, 24): 4(150 + 240 + 360) = 3000.
        static Vector3Int PerObjectDivisions(int tris)
        {
            if (tris == 3000) return new Vector3Int(15, 10, 24);
            throw new ArgumentException("only 3000 supported");
        }

        public void RaiseMeshChanged(int i, Mesh m) { meshes[i] = m; ObjectMeshChanged?.Invoke(i); }

        public static Mesh Box(Vector3 size, Vector3Int n) => Box(size, n.x, n.y, n.z);

        // Origin at the centre of the base, +Y up (Master Plan §6.4). Triangles = 4(nx ny + ny nz + nz nx).
        public static Mesh Box(Vector3 size, int nx, int ny, int nz)
        {
            var b = new Builder();
            float hx = size.x / 2, hz = size.z / 2, h = size.y;
            b.Quad(new Vector3(-hx, 0, -hz), new Vector3(2 * hx, 0, 0), new Vector3(0, h, 0), nx, ny);              // -z
            b.Quad(new Vector3(hx, 0, hz), new Vector3(-2 * hx, 0, 0), new Vector3(0, h, 0), nx, ny);               // +z
            b.Quad(new Vector3(hx, 0, -hz), new Vector3(0, 0, 2 * hz), new Vector3(0, h, 0), nz, ny);               // +x
            b.Quad(new Vector3(-hx, 0, hz), new Vector3(0, 0, -2 * hz), new Vector3(0, h, 0), nz, ny);              // -x
            b.Quad(new Vector3(-hx, h, -hz), new Vector3(2 * hx, 0, 0), new Vector3(0, 0, 2 * hz), nx, nz);         // top
            b.Quad(new Vector3(-hx, 0, hz), new Vector3(2 * hx, 0, 0), new Vector3(0, 0, -2 * hz), nx, nz);         // bottom
            return b.ToMesh("TestBox");
        }

        // 0.25 m quads over the walls' bounding box (Master Plan §4.6, §8.4).
        static Mesh BuildFloor(RoomModel room)
        {
            float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
            foreach (var w in room.walls)
                foreach (var p in new[] { w.a, w.b })
                { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            var b = new Builder();
            b.Quad(new Vector3(x0, 0, z0), new Vector3(x1 - x0, 0, 0), new Vector3(0, 0, z1 - z0),
                   Mathf.CeilToInt((x1 - x0) / 0.25f), Mathf.CeilToInt((z1 - z0) / 0.25f));
            return b.ToMesh("TestFloor");
        }

        static Mesh BuildWalls(RoomModel room)
        {
            var b = new Builder();
            foreach (var w in room.walls)
            {
                var a = new Vector3(w.a.x, 0, w.a.z); var e = new Vector3(w.b.x - w.a.x, 0, w.b.z - w.a.z);
                b.Quad(a, e, new Vector3(0, w.height, 0), Mathf.CeilToInt(e.magnitude / 0.25f), Mathf.CeilToInt(w.height / 0.25f));
            }
            return b.ToMesh("TestWalls");
        }

        public void Destroy()
        {
            foreach (var m in owned) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            owned.Clear();
        }

        sealed class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<int> t = new List<int>();

            // nu x nv quads over origin + u*s + w*t, two triangles each.
            public void Quad(Vector3 o, Vector3 u, Vector3 w, int nu, int nv)
            {
                for (int j = 0; j < nv; j++)
                    for (int i = 0; i < nu; i++)
                    {
                        int k = v.Count;
                        v.Add(o + u * (i / (float)nu) + w * (j / (float)nv));
                        v.Add(o + u * ((i + 1) / (float)nu) + w * (j / (float)nv));
                        v.Add(o + u * ((i + 1) / (float)nu) + w * ((j + 1) / (float)nv));
                        v.Add(o + u * (i / (float)nu) + w * ((j + 1) / (float)nv));
                        t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2);
                    }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v); m.SetTriangles(t, 0);
                return m;
            }
        }
    }
}
#endif
