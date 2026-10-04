using System;
using Atlas.Scan.Grid;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// The object profile (mesh design section 5.2 and Appendix B): a 16 x 16 grid of heights of the scanned
    /// surface inside an object's box, made by casting rays straight down onto the scan's triangles. It decides
    /// facing, fits the first shape and orients the generated mesh.
    /// The box's local frame is Unity's: origin at the centre of the base, +Y up, local +X along size.w, local
    /// +Z along size.d; yaw psi carries local +Z to (sin psi, cos psi) and local +X to (cos psi, -sin psi).
    /// Cell (i, j) covers local x from -w/2 + i*w/16 and local z from -d/2 + j*d/16; index = j * 16 + i, row 0
    /// at the lowest z. A value is 0 for a cell no ray hit, otherwise clamp(floor(255 * height / h + 0.5), 1, 255).
    /// Engine-free: the scan solver calls it on its worker thread.
    /// </summary>
    public static class ObjectProfile
    {
        /// <summary>Cells along each side of the footprint.</summary>
        public const int Side = 16;
        /// <summary>Bytes of a profile.</summary>
        public const int Length = Side * Side;
        /// <summary>Rays along each side of a cell: at its quarter points, 0.25 and 0.75 of the cell.</summary>
        public const int RaysPerCell = 2;
        /// <summary>Metres above the top of the box at which a ray starts; a hit above it does not count.</summary>
        public const float RayStartAbove = 0.05f;
        /// <summary>Metres above the base below which a hit does not count: the floor's own noise.</summary>
        public const float MinHitHeight = 0.02f;

        const int Rays = Side * RaysPerCell;
        // Metres added to the box's column when triangles are sorted out by their bounds, so rounding drops none.
        const float BoundsSlack = 1e-3f;

        /// <summary>Index of cell (i, j): i along local X, j along local Z.</summary>
        public static int Index(int i, int j) { return j * Side + i; }

        /// <summary>
        /// The profile of one box: 256 bytes, all 0 when nothing was hit. The mesh (three indices per triangle,
        /// one class per triangle or null for none) and the box (centre of its base, (w, h, d), Unity yaw) are
        /// in one frame with +Y up. Triangles of class Floor are left out. A hit counts when its height above
        /// the base lies in [0.02, h + 0.05]; a ray through an edge or a vertex of a triangle hits it.
        /// </summary>
        public static byte[] Compute(float3[] vertices, int[] indices, SurfaceClass[] classes, float3 boxBase, float3 boxSize, float boxYaw)
        {
            return Compute(vertices, indices, classes, new[] { new PhotoBox { Base = boxBase, Size = boxSize, Yaw = boxYaw } })[0];
        }

        /// <summary>
        /// The profiles of many boxes of one scan, in the order of the boxes, each as Compute gives it for one.
        /// The mesh is gone through once: a triangle is tested against the rays of a box only when its bounds
        /// overlap that box's column. The boxes are in the mesh's frame (PhotoScore.ToCaptureFrame carries a
        /// room object there). Null boxes give an empty array.
        /// </summary>
        public static byte[][] Compute(float3[] vertices, int[] indices, SurfaceClass[] classes, PhotoBox[] boxes)
        {
            int count = boxes != null ? boxes.Length : 0;
            var profiles = new byte[count][];
            var columns = new Column[count];
            var bounds = new ColumnBounds[count];
            // Everything outside the bounds of all columns together (the floor, the ceiling, the far walls) is passed over at once.
            var all = new ColumnBounds
            {
                XLo = float.MaxValue, YLo = float.MaxValue, ZLo = float.MaxValue,
                XHi = float.MinValue, YHi = float.MinValue, ZHi = float.MinValue
            };
            int live = 0;
            for (int k = 0; k < count; k++)
            {
                profiles[k] = new byte[Length];
                float3 size = boxes[k].Size;
                // A box with no size has no rays and no column.
                if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f)) continue;
                columns[k] = new Column(boxes[k].Base, size, boxes[k].Yaw, out bounds[k]);
                all.XLo = math.min(all.XLo, bounds[k].XLo);
                all.XHi = math.max(all.XHi, bounds[k].XHi);
                all.YLo = math.min(all.YLo, bounds[k].YLo);
                all.YHi = math.max(all.YHi, bounds[k].YHi);
                all.ZLo = math.min(all.ZLo, bounds[k].ZLo);
                all.ZHi = math.max(all.ZHi, bounds[k].ZHi);
                live++;
            }
            if (live == 0 || vertices == null || indices == null) return profiles;

            int triangles = indices.Length / 3;
            int classed = classes != null ? Math.Min(classes.Length, triangles) : 0;
            for (int t = 0; t < triangles; t++)
            {
                if (t < classed && classes[t] == SurfaceClass.Floor) continue;
                float3 a = vertices[indices[3 * t]], b = vertices[indices[3 * t + 1]], c = vertices[indices[3 * t + 2]];
                float yLo = a.y < b.y ? (a.y < c.y ? a.y : c.y) : (b.y < c.y ? b.y : c.y);
                float yHi = a.y > b.y ? (a.y > c.y ? a.y : c.y) : (b.y > c.y ? b.y : c.y);
                if (yHi < all.YLo || yLo > all.YHi) continue;
                float xLo = a.x < b.x ? (a.x < c.x ? a.x : c.x) : (b.x < c.x ? b.x : c.x);
                float xHi = a.x > b.x ? (a.x > c.x ? a.x : c.x) : (b.x > c.x ? b.x : c.x);
                if (xHi < all.XLo || xLo > all.XHi) continue;
                float zLo = a.z < b.z ? (a.z < c.z ? a.z : c.z) : (b.z < c.z ? b.z : c.z);
                float zHi = a.z > b.z ? (a.z > c.z ? a.z : c.z) : (b.z > c.z ? b.z : c.z);
                if (zHi < all.ZLo || zLo > all.ZHi) continue;

                for (int k = 0; k < count; k++)
                {
                    if (xHi < bounds[k].XLo || xLo > bounds[k].XHi || zHi < bounds[k].ZLo || zLo > bounds[k].ZHi ||
                        yHi < bounds[k].YLo || yLo > bounds[k].YHi || columns[k] == null) continue;
                    columns[k].Cast(a, b, c);
                }
            }

            for (int k = 0; k < count; k++)
                if (columns[k] != null) columns[k].Read(profiles[k]);
            return profiles;
        }

        /// <summary>The byte of a counted hit: clamp(floor(255 * height / h + 0.5), 1, 255). Never 0, which means unseen.</summary>
        public static byte Quantise(double height, double boxHeight)
        {
            double q = Math.Floor(255.0 * height / boxHeight + 0.5);
            return (byte)(q < 1.0 ? 1.0 : q > 255.0 ? 255.0 : q);
        }

        /// <summary>Cells that are not 0, divided by 256. 0 for no profile.</summary>
        public static float Coverage(byte[] profile)
        {
            if (profile == null || profile.Length != Length) return 0f;
            int seen = 0;
            for (int c = 0; c < Length; c++) if (profile[c] != 0) seen++;
            return seen / (float)Length;
        }

        /// <summary>The height a value stands for, as a fraction of the box height: v / 255. 0 is an unseen cell, not a height.</summary>
        public static float HeightFraction(byte value)
        {
            return value / 255f;
        }

        /// <summary>
        /// The profile after the box's yaw has been increased by quarterTurns quarter turns (one turn: the new
        /// local +Z is the old local +X, the new local +X is the old local -Z, and size.w and size.d swap).
        /// New cell (i, j) takes old cell (j, 15 - i) for one turn, (15 - i, 15 - j) for two, (15 - j, i) for
        /// three. Always a new array; null for no profile.
        /// </summary>
        public static byte[] Rotate(byte[] profile, int quarterTurns)
        {
            if (profile == null) return null;
            if (profile.Length != Length) throw new ArgumentException("A profile holds exactly 256 bytes.", nameof(profile));
            int turns = ((quarterTurns % 4) + 4) % 4;
            var turned = new byte[Length];
            const int last = Side - 1;
            for (int j = 0; j < Side; j++)
            {
                for (int i = 0; i < Side; i++)
                {
                    int oi, oj;
                    switch (turns)
                    {
                        case 1: oi = j; oj = last - i; break;
                        case 2: oi = last - i; oj = last - j; break;
                        case 3: oi = last - j; oj = i; break;
                        default: oi = i; oj = j; break;
                    }
                    turned[Index(i, j)] = profile[Index(oi, oj)];
                }
            }
            return turned;
        }

        // The bounds of a box's column in the mesh's frame: its footprint, from the lowest to the highest hit that counts.
        struct ColumnBounds
        {
            public float XLo, XHi, YLo, YHi, ZLo, ZHi;
        }

        // The rays of one box and the highest counted hit of each.
        sealed class Column
        {
            readonly float3 _base;
            readonly double _w, _h, _d, _cos, _sin, _stepX, _stepZ, _lowest, _highest;
            // Where the rays stand, local frame: the quarter points of every cell.
            readonly double[] _rayX = new double[Rays], _rayZ = new double[Rays];
            // Height above the base; negative for no hit.
            readonly double[] _top = new double[Rays * Rays];

            public Column(float3 boxBase, float3 boxSize, float boxYaw, out ColumnBounds bounds)
            {
                _base = boxBase;
                _w = boxSize.x;
                _h = boxSize.y;
                _d = boxSize.z;
                _cos = Math.Cos(boxYaw);
                _sin = Math.Sin(boxYaw);
                for (int r = 0; r < Rays; r++)
                {
                    double along = r / RaysPerCell + (r % RaysPerCell == 0 ? 0.25 : 0.75);
                    _rayX[r] = -0.5 * _w + along * _w / Side;
                    _rayZ[r] = -0.5 * _d + along * _d / Side;
                }
                _stepX = _w / Rays;
                _stepZ = _d / Rays;
                _lowest = MinHitHeight;
                _highest = _h + RayStartAbove;
                for (int r = 0; r < _top.Length; r++) _top[r] = -1.0;

                float reachX = (float)(0.5 * (Math.Abs(_cos) * _w + Math.Abs(_sin) * _d)) + BoundsSlack;
                float reachZ = (float)(0.5 * (Math.Abs(_sin) * _w + Math.Abs(_cos) * _d)) + BoundsSlack;
                bounds = new ColumnBounds
                {
                    XLo = boxBase.x - reachX, XHi = boxBase.x + reachX,
                    ZLo = boxBase.z - reachZ, ZHi = boxBase.z + reachZ,
                    YLo = boxBase.y + MinHitHeight - BoundsSlack, YHi = boxBase.y + boxSize.y + RayStartAbove + BoundsSlack
                };
            }

            // One triangle of the mesh against the rays its plan bounds can hold.
            public void Cast(float3 a, float3 b, float3 c)
            {
                // Into the box's frame.
                double ax = a.x - (double)_base.x, az = a.z - (double)_base.z, ay = a.y - (double)_base.y;
                double bx = b.x - (double)_base.x, bz = b.z - (double)_base.z, by = b.y - (double)_base.y;
                double cx = c.x - (double)_base.x, cz = c.z - (double)_base.z, cy = c.y - (double)_base.y;
                double lax = ax * _cos - az * _sin, laz = ax * _sin + az * _cos;
                double lbx = bx * _cos - bz * _sin, lbz = bx * _sin + bz * _cos;
                double lcx = cx * _cos - cz * _sin, lcz = cx * _sin + cz * _cos;

                // One ray more on each side, so that a ray on an edge is not lost to rounding. A triangle with a
                // coordinate that is no number fails every comparison and is left out here.
                double fromX = Math.Floor((Math.Min(lax, Math.Min(lbx, lcx)) + 0.5 * _w) / _stepX - 0.5);
                double toX = Math.Ceiling((Math.Max(lax, Math.Max(lbx, lcx)) + 0.5 * _w) / _stepX - 0.5);
                double fromZ = Math.Floor((Math.Min(laz, Math.Min(lbz, lcz)) + 0.5 * _d) / _stepZ - 0.5);
                double toZ = Math.Ceiling((Math.Max(laz, Math.Max(lbz, lcz)) + 0.5 * _d) / _stepZ - 0.5);
                if (!(toX >= 0.0) || !(toZ >= 0.0) || !(fromX < Rays) || !(fromZ < Rays)) return;
                int r0 = (int)Math.Max(fromX, 0.0), r1 = (int)Math.Min(toX, Rays - 1.0);
                int s0 = (int)Math.Max(fromZ, 0.0), s1 = (int)Math.Min(toZ, Rays - 1.0);

                for (int s = s0; s <= s1; s++)
                {
                    double pz = _rayZ[s];
                    for (int r = r0; r <= r1; r++)
                    {
                        double px = _rayX[r];
                        double e0 = Edge(lax, laz, lbx, lbz, px, pz);
                        double e1 = Edge(lbx, lbz, lcx, lcz, px, pz);
                        double e2 = Edge(lcx, lcz, lax, laz, px, pz);
                        bool inside = (e0 >= 0.0 && e1 >= 0.0 && e2 >= 0.0) || (e0 <= 0.0 && e1 <= 0.0 && e2 <= 0.0);
                        if (!inside) continue;
                        double twice = e0 + e1 + e2;
                        if (twice == 0.0) continue;      // seen edge-on from above: it has no plan area to hit
                        // A level triangle gives its own height exactly, wherever the ray meets it.
                        double height = ay + (e2 * (by - ay) + e0 * (cy - ay)) / twice;
                        if (height < _lowest || height > _highest) continue;
                        int at = s * Rays + r;
                        if (height > _top[at]) _top[at] = height;
                    }
                }
            }

            // The cells: the highest hit of each one's four rays as a byte, 0 where none hit.
            public void Read(byte[] profile)
            {
                for (int j = 0; j < Side; j++)
                {
                    for (int i = 0; i < Side; i++)
                    {
                        double best = -1.0;
                        for (int n = 0; n < RaysPerCell; n++)
                            for (int m = 0; m < RaysPerCell; m++)
                                best = Math.Max(best, _top[(j * RaysPerCell + n) * Rays + i * RaysPerCell + m]);
                        profile[Index(i, j)] = best >= 0.0 ? Quantise(best, _h) : (byte)0;
                    }
                }
            }

            // Twice the signed plan area of (a, b, p). The two end points are always taken in the same order, so
            // two triangles that share an edge get the same number with opposite signs and leave no gap between them.
            static double Edge(double ax, double az, double bx, double bz, double px, double pz)
            {
                if (ax > bx || (ax == bx && az > bz)) return -((ax - bx) * (pz - bz) - (az - bz) * (px - bx));
                return (bx - ax) * (pz - az) - (bz - az) * (px - ax);
            }
        }
    }
}
