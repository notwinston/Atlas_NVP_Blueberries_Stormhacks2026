// PLACEHOLDER (lane C, P1-C07): contract surface only; replaced by lane C's implementation
using System;
using System.Collections.Generic;
using Atlas.World;
using UnityEngine;

namespace Atlas.Scan
{
    // Master Plan §4.5, §4.6. Deterministic and minimal: the floor on a 0.25 m grid, wall quads, one box per object.
    public static class HomeRebuild
    {
        const float Grid = 0.25f;   // Master Plan §4.5
        internal const string BudgetStandInHash = "budget-standin-5000";   // Master Plan §4.3 budget_room

        public static IHomeShell Build(RoomModel room) => new Shell(room);

        sealed class Shell : IHomeShell
        {
            readonly RoomModel _room;
            readonly Mesh _floor, _walls;
            readonly Mesh[] _objects;

            public Shell(RoomModel room)
            {
                _room = room;
                _floor = BuildFloor(room);
                _walls = BuildWalls(room);
                var objs = room.objects ?? new RoomObject[0];
                _objects = new Mesh[objs.Length];
                for (int i = 0; i < objs.Length; i++)
                    _objects[i] = objs[i].hash == BudgetStandInHash ? BuildStandIn(objs[i].size) : BuildBox(objs[i].size);
            }

            public Mesh Floor => _floor;
            public Mesh Walls => _walls;
            public int ObjectCount => _objects.Length;
            public RoomObject ObjectRecord(int i) => _room.objects[i];
            public Mesh ObjectMesh(int i) => _objects[i];
            public event Action<int> ObjectMeshChanged { add { } remove { } }   // no events fire in the placeholder
        }

        static bool Inside(Wall[] w, float x, float z)
        {
            bool inside = false;
            for (int i = 0; i < w.Length; i++)
            {
                var a = w[i].a; var b = w[i].b;
                if ((a.z > z) != (b.z > z) && x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x) inside = !inside;
            }
            return inside;
        }

        static Mesh BuildFloor(RoomModel room)
        {
            var w = room.walls;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var wall in w)
            {
                x0 = Mathf.Min(x0, wall.a.x); x1 = Mathf.Max(x1, wall.a.x);
                z0 = Mathf.Min(z0, wall.a.z); z1 = Mathf.Max(z1, wall.a.z);
            }
            int nx = Mathf.CeilToInt((x1 - x0) / Grid - 1e-4f), nz = Mathf.CeilToInt((z1 - z0) / Grid - 1e-4f);
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float ax = x0 + i * Grid, az = z0 + j * Grid;
                    if (!Inside(w, ax + Grid * 0.5f, az + Grid * 0.5f)) continue;
                    int k = v.Count;
                    v.Add(new Vector3(ax, 0, az)); v.Add(new Vector3(ax, 0, az + Grid));
                    v.Add(new Vector3(ax + Grid, 0, az + Grid)); v.Add(new Vector3(ax + Grid, 0, az));
                    t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });   // faces up (clockwise from above in Unity)
                }
            return Make("HomeFloor", v, t);
        }

        static Mesh BuildWalls(RoomModel room)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            // Quads face into the room (AtlasSurface culls back faces): the winding follows the outline's orientation,
            // anticlockwise in (x, z) (positive shoelace area) for the fixtures. Openings are not cut in the placeholder.
            float area = 0;
            foreach (var wall in room.walls) area += wall.a.x * wall.b.z - wall.b.x * wall.a.z;
            bool ccw = area > 0;
            foreach (var wall in room.walls)
            {
                int k = v.Count;
                v.Add(new Vector3(wall.a.x, 0, wall.a.z)); v.Add(new Vector3(wall.a.x, wall.height, wall.a.z));
                v.Add(new Vector3(wall.b.x, wall.height, wall.b.z)); v.Add(new Vector3(wall.b.x, 0, wall.b.z));
                if (ccw) t.AddRange(new[] { k, k + 3, k + 2, k, k + 2, k + 1 });   // a, b, b_top ; a, b_top, a_top
                else t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            return Make("HomeWalls", v, t);
        }

        // 12-triangle box, origin at the centre of the base, +Y up (Master Plan §6.4).
        static Mesh BuildBox(Vector3 size) => BuildGrid(size, 1, 1, 1);

        // Master Plan §4.3 budget_room: 30, 20 and 13 quads along the 0.5, 0.8 and 0.3 m edges;
        // 2(30*20 + 20*13 + 13*30) = 5,000 triangles, no edge over 0.25 m.
        static Mesh BuildStandIn(Vector3 size) => BuildGrid(size, Count(size.x), Count(size.y), Count(size.z));

        static int Count(float edge)
        {
            if (Mathf.Abs(edge - 0.5f) < 1e-3f) return 30;
            if (Mathf.Abs(edge - 0.8f) < 1e-3f) return 20;
            if (Mathf.Abs(edge - 0.3f) < 1e-3f) return 13;
            return Mathf.Max(1, Mathf.CeilToInt(edge / 0.05f));
        }

        static Mesh BuildGrid(Vector3 s, int nx, int ny, int nz)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            Vector3 lo = new Vector3(-s.x / 2, 0, -s.z / 2);
            // each face: origin o, axes u (a cells) and w (b cells); outward normal = u x w
            Face(v, t, lo, Vector3.right * s.x, Vector3.up * s.y, nx, ny);                                   // -z face, normal -z
            Face(v, t, lo + new Vector3(0, 0, s.z), Vector3.up * s.y, Vector3.right * s.x, ny, nx);         // +z face, normal +z
            Face(v, t, lo, Vector3.up * s.y, Vector3.forward * s.z, ny, nz);                                 // -x face
            Face(v, t, lo + new Vector3(s.x, 0, 0), Vector3.forward * s.z, Vector3.up * s.y, nz, ny);       // +x face
            Face(v, t, lo, Vector3.forward * s.z, Vector3.right * s.x, nz, nx);                              // bottom, -y
            Face(v, t, lo + new Vector3(0, s.y, 0), Vector3.right * s.x, Vector3.forward * s.z, nx, nz);    // top, +y
            return Make("HomeObject", v, t);
        }

        // Unity front faces are clockwise; each face is wound (o, o+w, o+u+w, o+u), so its outward normal is -(u x w).
        static void Face(List<Vector3> v, List<int> t, Vector3 o, Vector3 u, Vector3 w, int a, int b)
        {
            for (int j = 0; j < b; j++)
                for (int i = 0; i < a; i++)
                {
                    Vector3 p = o + u * ((float)i / a) + w * ((float)j / b);
                    Vector3 du = u / a, dw = w / b;
                    int k = v.Count;
                    v.Add(p); v.Add(p + dw); v.Add(p + du + dw); v.Add(p + du);
                    t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
                }
        }

        static Mesh Make(string name, List<Vector3> v, List<int> t)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
