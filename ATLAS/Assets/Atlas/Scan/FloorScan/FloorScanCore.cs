using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan.Grid
{
    /// <summary>
    /// FloorScan: from a classified triangle soup to the free rectangle (Master Plan section 4.2, research memo
    /// R1 section C.3). Plain C# on managed arrays with no AR type, so it runs in EditMode and headless tests and
    /// on a worker thread on the phone, once when the sweep ends and again if the walker corrects the outline.
    /// </summary>
    public static class FloorScanCore
    {
        /// <summary>Grid cell, metres.</summary>
        public const double Cell = 0.05;
        /// <summary>The grid reaches this many cells beyond the outline's bounding box.</summary>
        public const int GridMarginCells = 5;
        /// <summary>A floor triangle counts when all of it lies within this of y0, metres.</summary>
        public const double FloorBand = 0.08;
        /// <summary>Lower things are not obstacles, metres above the floor.</summary>
        public const double ObstacleMinHeight = 0.10;
        /// <summary>Higher things are not obstacles, metres above the floor.</summary>
        public const double ObstacleMaxHeight = 2.0;
        /// <summary>WALK = {SDF &gt; 0.25 m}, which is 0.30 m from every wall and obstacle.</summary>
        public const double WalkSdf = 0.25;
        /// <summary>Floor is taken as seen within this distance of a point of the walker's own path, metres.</summary>
        public const double PathRadius = 0.15;
        /// <summary>Unseen specks of at most this many cells are closed.</summary>
        public const int PinholeMaxCells = 4;
        /// <summary>A longer short side gains nothing: the lap square is capped at 3.2 m plus its 0.4 m inset.</summary>
        public const double RectSideCap = 3.6;
        /// <summary>Obstacle blobs smaller than this are noise and get no box.</summary>
        public const int MinBlobCells = 4;
        /// <summary>A plane this large may stand in for the floor when none is classified, square metres.</summary>
        public const double FallbackPlaneArea = 1.0;

        // Geometry closer than this to a cell edge does not reach into the cell. It absorbs the rounding of
        // single-precision input turned into the wall-aligned frame.
        const double OverlapTolerance = 1e-4;
        const int MaxGridSide = 2048;

        /// <summary>Run the whole pipeline.</summary>
        public static FloorGridResult Run(FloorGridInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            float3[] verts = input.Vertices ?? Array.Empty<float3>();
            int[] indices = input.Indices ?? Array.Empty<int>();
            int triCount = indices.Length / 3;
            SurfaceClass[] classes = input.TriangleClasses;
            if (classes != null && classes.Length < triCount)
                throw new ArgumentException("TriangleClasses is shorter than the triangle count.", nameof(input));
            bool hasOutline = input.Outline != null && input.Outline.Length >= 3;

            var result = new FloorGridResult { CellSize = (float)Cell, Status = FloorGridStatus.NoFloor };
            double y0 = FindFloorHeight(input, verts, indices, triCount);
            if (double.IsNaN(y0)) return Empty(result);
            result.FloorHeight = (float)y0;

            // 1. Turn everything so that the dominant wall direction lies along the grid axes.
            double theta = DominantWallDirection(input, verts, indices, triCount);
            result.WallDirection = (float)theta;
            double ct = Math.Cos(theta), st = Math.Sin(theta);
            var ax = new double[verts.Length];
            var az = new double[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                ax[v] = ct * verts[v].x + st * verts[v].z;
                az[v] = -st * verts[v].x + ct * verts[v].z;
            }
            double[] outX = null, outZ = null;
            if (hasOutline)
            {
                outX = new double[input.Outline.Length];
                outZ = new double[input.Outline.Length];
                for (int k = 0; k < outX.Length; k++)
                {
                    outX[k] = ct * input.Outline[k].x + st * input.Outline[k].y;
                    outZ[k] = -st * input.Outline[k].x + ct * input.Outline[k].y;
                }
            }
            int pathCount = input.WalkerPath != null ? input.WalkerPath.Length : 0;
            var pathX = new double[pathCount];
            var pathZ = new double[pathCount];
            for (int k = 0; k < pathCount; k++)
            {
                pathX[k] = ct * input.WalkerPath[k].x + st * input.WalkerPath[k].y;
                pathZ[k] = -st * input.WalkerPath[k].x + ct * input.WalkerPath[k].y;
            }
            double walkerX = ct * input.Walker.x + st * input.Walker.y;
            double walkerZ = -st * input.Walker.x + ct * input.Walker.y;

            // 2. The grid. Its lattice starts at the low corner of the footprint, so a room measured in whole
            //    cells gives the same cells wherever and however it lies in the session frame.
            double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
            if (hasOutline)
            {
                for (int k = 0; k < outX.Length; k++) Extend(outX[k], outZ[k], ref minX, ref minZ, ref maxX, ref maxZ);
            }
            else
            {
                for (int t = 0; t < triCount; t++)
                {
                    if (!IsFloorTriangle(t, verts, indices, classes, y0)) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = indices[3 * t + k];
                        Extend(ax[v], az[v], ref minX, ref minZ, ref maxX, ref maxZ);
                    }
                }
                for (int k = 0; k < pathCount; k++) Extend(pathX[k], pathZ[k], ref minX, ref minZ, ref maxX, ref maxZ);
            }
            if (minX > maxX) return Empty(result);
            int width = (int)Math.Ceiling((maxX - minX) / Cell - 1e-3) + 2 * GridMarginCells;
            int height = (int)Math.Ceiling((maxZ - minZ) / Cell - 1e-3) + 2 * GridMarginCells;
            if (width > MaxGridSide || height > MaxGridSide) return Empty(result);
            double gridX = minX - GridMarginCells * Cell, gridZ = minZ - GridMarginCells * Cell;
            int cells = width * height;

            // 3. Occupancy.
            var floor = new bool[cells];
            var obst = new bool[cells];
            var furniture = new bool[cells];     // obstacle cells that are not only wall, window or door
            var top = new float[cells];          // highest obstacle point above the floor, metres
            for (int t = 0; t < triCount; t++)
            {
                int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
                SurfaceClass cls = classes != null ? classes[t] : SurfaceClass.None;
                if (IsFloorTriangle(t, verts, indices, classes, y0))
                {
                    Rasterise(ax[a] - gridX, az[a] - gridZ, ax[b] - gridX, az[b] - gridZ, ax[c] - gridX, az[c] - gridZ,
                        width, height, floor, null, null, 0f, false);
                    continue;
                }
                double lo = Math.Min(verts[a].y, Math.Min(verts[b].y, verts[c].y)) - y0;
                double hi = Math.Max(verts[a].y, Math.Max(verts[b].y, verts[c].y)) - y0;
                // Any part between 0.10 m and 2.0 m above the floor blocks. For the small triangles of a scan
                // this is the plan's "a vertex between"; it also catches a large triangle that spans the band.
                if (!(hi > ObstacleMinHeight && lo < ObstacleMaxHeight)) continue;
                bool structure = cls == SurfaceClass.Wall || cls == SurfaceClass.Window || cls == SurfaceClass.Door;
                Rasterise(ax[a] - gridX, az[a] - gridZ, ax[b] - gridX, az[b] - gridZ, ax[c] - gridX, az[c] - gridZ,
                    width, height, obst, furniture, top, (float)hi, !structure);
            }
            // The walker's own path is floor: what was walked is free. Each sample covers the cells within
            // 0.15 m of it, and so does the straight step from one sample to the next.
            for (int k = 0; k < pathCount; k++)
            {
                MarkDisc(pathX[k] - gridX, pathZ[k] - gridZ, width, height, floor);
                if (k == 0) continue;
                double dx = pathX[k] - pathX[k - 1], dz = pathZ[k] - pathZ[k - 1];
                int steps = (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / Cell);
                for (int s = 1; s < steps; s++)
                {
                    double f = (double)s / steps;
                    MarkDisc(pathX[k - 1] + f * dx - gridX, pathZ[k - 1] + f * dz - gridZ, width, height, floor);
                }
            }
            // Supplied boxes block the floor like scanned furniture.
            int externalCount = input.ExternalBoxes != null ? input.ExternalBoxes.Length : 0;
            for (int e = 0; e < externalCount; e++)
            {
                ObstacleBox box = input.ExternalBoxes[e];
                double bottom = box.Base.y - y0, boxTop = bottom + box.Size.y;
                if (!(boxTop > ObstacleMinHeight && bottom < ObstacleMaxHeight)) continue;
                double bx = ct * box.Base.x + st * box.Base.z - gridX, bz = -st * box.Base.x + ct * box.Base.z - gridZ;
                double yaw = box.Yaw + theta;
                double c = Math.Cos(yaw), s = Math.Sin(yaw);
                double hx = 0.5 * box.Size.x, hz = 0.5 * box.Size.z;
                // Local +X is (c, -s) and local +Z is (s, c) in (x, z).
                double x0 = bx - hx * c - hz * s, z0 = bz + hx * s - hz * c;
                double x1 = bx + hx * c - hz * s, z1 = bz - hx * s - hz * c;
                double x2 = bx + hx * c + hz * s, z2 = bz - hx * s + hz * c;
                double x3 = bx - hx * c + hz * s, z3 = bz + hx * s + hz * c;
                Rasterise(x0, z0, x1, z1, x2, z2, width, height, obst, furniture, top, (float)boxTop, true);
                Rasterise(x0, z0, x2, z2, x3, z3, width, height, obst, furniture, top, (float)boxTop, true);
            }

            // 4. Inside the outline, unseen specks, quality.
            var inside = new bool[cells];
            var unknown = new bool[cells];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    int c = j * width + i;
                    inside[c] = !hasOutline || FloorGridMath.PointInPolygon(
                        gridX + (i + 0.5) * Cell, gridZ + (j + 0.5) * Cell, outX, outZ);
                    unknown[c] = inside[c] && !floor[c] && !obst[c];
                }
            }
            FloorGridMath.ClosePinholes(floor, unknown, width, height, PinholeMaxCells);
            if (!hasOutline)
            {
                // No confirmed outline: the edge of what was seen stands in for it.
                for (int c = 0; c < cells; c++) inside[c] = floor[c] || obst[c];
            }
            result.UnknownShare = UnknownShare(floor, obst, inside, width, height, hasOutline);

            // 5. FREE = FLOOR minus (OBST and everything beyond the outline) grown by one cell.
            var blocked = new bool[cells];
            for (int c = 0; c < cells; c++) blocked[c] = obst[c] || !inside[c];
            bool[] grown = FloorGridMath.Dilate8(blocked, width, height);
            var free = new bool[cells];
            for (int c = 0; c < cells; c++) free[c] = floor[c] && !grown[c];

            // 6. Distance field, walkable region, free rectangle.
            float[] sdf = FloorGridMath.SignedDistance(free, width, height, Cell);
            bool[] walk = FloorGridMath.WalkComponent(sdf, width, height, Cell, WalkSdf, walkerX - gridX, walkerZ - gridZ);
            int ri0 = 0, rj0 = 0, ri1 = 0, rj1 = 0;
            bool hasRect = walk != null && FloorGridMath.FreeRectangle(walk, width, height, Cell,
                (int)Math.Round(RectSideCap / Cell), walkerX - gridX, walkerZ - gridZ, out ri0, out rj0, out ri1, out rj1);
            if (walk == null) walk = new bool[cells];
            int centreHalfX, centreHalfZ;      // the anchor on the half-cell lattice, from the grid's low corner
            if (hasRect)
            {
                centreHalfX = ri0 + ri1;
                centreHalfZ = rj0 + rj1;
            }
            else
            {
                // Nothing walkable: the frame sits on the walker's own cell and the room is standing-only.
                centreHalfX = 2 * Clamp((int)Math.Floor((walkerX - gridX) / Cell), 0, width - 1) + 1;
                centreHalfZ = 2 * Clamp((int)Math.Floor((walkerZ - gridZ) / Cell), 0, height - 1) + 1;
            }

            // 7. The room frame: +X along the shorter side. When the rectangle is wider than deep the frame
            //    takes a quarter turn, in the sense that keeps its yaw within (-90, 90] degrees.
            int quarter = 0;
            if (hasRect && ri1 - ri0 > rj1 - rj0) quarter = theta >= 0.0 ? 1 : -1;
            double centreX = gridX + 0.5 * centreHalfX * Cell, centreZ = gridZ + 0.5 * centreHalfZ * Cell;
            result.AnchorX = ct * centreX - st * centreZ;
            result.AnchorZ = st * centreX + ct * centreZ;
            result.AnchorPosition = new float3((float)result.AnchorX, (float)y0, (float)result.AnchorZ);
            double frameYaw = -theta + quarter * 0.5 * Math.PI;
            result.AnchorYaw = (float)frameYaw;
            if (quarter == 0) { result.M00 = ct; result.M01 = st; result.M10 = -st; result.M11 = ct; }
            else if (quarter > 0) { result.M00 = st; result.M01 = -ct; result.M10 = ct; result.M11 = st; }
            else { result.M00 = -st; result.M01 = ct; result.M10 = -ct; result.M11 = -st; }

            // The same cells re-indexed, so nothing is resampled: the turn is exact.
            int roomW = quarter == 0 ? width : height, roomH = quarter == 0 ? height : width;
            int originHalfX, originHalfZ;
            if (quarter == 0) { originHalfX = -centreHalfX; originHalfZ = -centreHalfZ; }
            else if (quarter > 0) { originHalfX = centreHalfZ - 2 * height; originHalfZ = -centreHalfX; }
            else { originHalfX = -centreHalfZ; originHalfZ = centreHalfX - 2 * width; }
            result.Width = roomW;
            result.Height = roomH;
            result.OriginX = (float)(originHalfX * Cell * 0.5);
            result.OriginZ = (float)(originHalfZ * Cell * 0.5);
            var roomCells = new FloorCell[cells];
            var roomSdf = new float[cells];
            var roomWalk = new bool[cells];
            var roomBlob = new bool[cells];
            var roomTop = new float[cells];
            for (int j = 0; j < roomH; j++)
            {
                for (int i = 0; i < roomW; i++)
                {
                    int src = quarter == 0 ? j * width + i
                        : quarter > 0 ? (height - 1 - i) * width + j
                        : i * width + (width - 1 - j);
                    int dst = j * roomW + i;
                    FloorCell flags = FloorCell.None;
                    if (floor[src]) flags |= FloorCell.Floor;
                    if (obst[src]) flags |= FloorCell.Obstacle;
                    if (inside[src]) flags |= FloorCell.Inside;
                    if (free[src]) flags |= FloorCell.Free;
                    if (walk[src]) flags |= FloorCell.Walk;
                    roomCells[dst] = flags;
                    roomSdf[dst] = sdf[src];
                    roomWalk[dst] = walk[src];
                    roomBlob[dst] = obst[src] && furniture[src] && inside[src];
                    roomTop[dst] = top[src];
                }
            }
            result.Cells = roomCells;
            result.Sdf = roomSdf;

            if (hasOutline)
            {
                result.Outline = new float2[input.Outline.Length];
                for (int k = 0; k < result.Outline.Length; k++) result.Outline[k] = result.ToRoom(input.Outline[k]);
            }

            // 8. Walkable polygon with holes, traced along the cells.
            var outers = new List<int2[]>();
            var holes = new List<int2[]>();
            FloorGridMath.TraceRegion(roomWalk, roomW, roomH, outers, holes);
            double ox = originHalfX * Cell * 0.5, oz = originHalfZ * Cell * 0.5;
            result.WalkableOuter = outers.Count > 0 ? ToMetres(outers[0], ox, oz) : Array.Empty<float2>();
            holes.Sort((p, q) => p[0].y != q[0].y ? p[0].y.CompareTo(q[0].y) : p[0].x.CompareTo(q[0].x));
            result.WalkableHoles = new float2[holes.Count][];
            for (int k = 0; k < holes.Count; k++) result.WalkableHoles[k] = ToMetres(holes[k], ox, oz);

            // 9. Obstacle boxes, merged with the supplied ones in the room frame.
            result.Boxes = BuildBoxes(input, result, roomBlob, roomTop, roomW, roomH, ox, oz, frameYaw, y0);

            // 10. Free rectangle, loop-fit and cell grids.
            if (!hasRect)
            {
                result.Status = FloorGridStatus.NoWalkableFloor;
                result.Standing = true;
                result.Fit = LoopFit.Fit(0.0, 0.0);
                return result;
            }
            double wx = (ri1 - ri0) * Cell, wz = (rj1 - rj0) * Cell;
            double wf = Math.Min(wx, wz), df = Math.Max(wx, wz);
            result.Status = FloorGridStatus.Ok;
            result.Wf = (float)wf;
            result.Df = (float)df;
            result.Fit = LoopFit.Fit(wf, df);
            result.Glued = CellGridRules.Glued(wf, df);
            result.Aligned = CellGridRules.Aligned(wf, df);
            result.Standing = LoopFit.IsStanding(wf, df);
            return result;
        }

        /// <summary>
        /// Floor height y0: the given value; else the largest plane classified floor; else the lowest plane of at
        /// least 1 square metre; else the area-weighted median height of the floor triangles. NaN when none exists.
        /// </summary>
        public static double FindFloorHeight(FloorGridInput input, float3[] verts, int[] indices, int triCount)
        {
            if (!float.IsNaN(input.FloorHeight)) return input.FloorHeight;
            if (input.Planes != null)
            {
                int best = -1;
                for (int k = 0; k < input.Planes.Length; k++)
                    if (input.Planes[k].IsFloor && (best < 0 || input.Planes[k].Area > input.Planes[best].Area)) best = k;
                if (best < 0)
                {
                    for (int k = 0; k < input.Planes.Length; k++)
                        if (input.Planes[k].Area >= FallbackPlaneArea && (best < 0 || input.Planes[k].Height < input.Planes[best].Height)) best = k;
                }
                if (best >= 0) return input.Planes[best].Height;
            }
            if (input.TriangleClasses == null) return double.NaN;
            var heights = new List<double>();
            var areas = new List<double>();
            double total = 0.0;
            for (int t = 0; t < triCount; t++)
            {
                if (input.TriangleClasses[t] != SurfaceClass.Floor) continue;
                float3 a = verts[indices[3 * t]], b = verts[indices[3 * t + 1]], c = verts[indices[3 * t + 2]];
                double area = 0.5 * Math.Abs((double)(b.x - a.x) * (c.z - a.z) - (double)(b.z - a.z) * (c.x - a.x));
                heights.Add(((double)a.y + b.y + c.y) / 3.0);
                areas.Add(area);
                total += area;
            }
            if (heights.Count == 0 || total <= 0.0) return double.NaN;
            double[] h = heights.ToArray(), w = areas.ToArray();
            Array.Sort(h, w);
            double run = 0.0;
            for (int k = 0; k < h.Length; k++)
            {
                run += w[k];
                if (run >= 0.5 * total) return h[k];
            }
            return h[h.Length - 1];
        }

        /// <summary>
        /// Dominant wall direction modulo 90 degrees, radians in (-pi/4, pi/4], measured from +X towards +Z.
        /// Sources, in order: the wall planes (normals weighted by length), the outline's edges, the triangles
        /// classified as wall (weighted by area). A normal and its wall differ by 90 degrees, so both fold to the
        /// same answer: the angle that maximises the sum of weight * cos(4 (angle - answer)).
        /// </summary>
        public static double DominantWallDirection(FloorGridInput input, float3[] verts, int[] indices, int triCount)
        {
            double sin4 = 0.0, cos4 = 0.0;
            if (input.Walls != null)
            {
                for (int k = 0; k < input.Walls.Length; k++)
                    Accumulate(input.Walls[k].Normal.x, input.Walls[k].Normal.y, input.Walls[k].Length, ref sin4, ref cos4);
            }
            if (Negligible(sin4, cos4) && input.Outline != null && input.Outline.Length >= 3)
            {
                sin4 = cos4 = 0.0;
                for (int k = 0; k < input.Outline.Length; k++)
                {
                    float2 p = input.Outline[k], q = input.Outline[(k + 1) % input.Outline.Length];
                    double dx = (double)q.x - p.x, dz = (double)q.y - p.y;
                    Accumulate(dx, dz, Math.Sqrt(dx * dx + dz * dz), ref sin4, ref cos4);
                }
            }
            if (Negligible(sin4, cos4) && input.TriangleClasses != null)
            {
                sin4 = cos4 = 0.0;
                for (int t = 0; t < triCount; t++)
                {
                    if (input.TriangleClasses[t] != SurfaceClass.Wall) continue;
                    float3 a = verts[indices[3 * t]], b = verts[indices[3 * t + 1]], c = verts[indices[3 * t + 2]];
                    double ux = (double)b.x - a.x, uy = (double)b.y - a.y, uz = (double)b.z - a.z;
                    double vx = (double)c.x - a.x, vy = (double)c.y - a.y, vz = (double)c.z - a.z;
                    // Horizontal part of the area normal: its length is the triangle's area seen from the side.
                    double nx = uy * vz - uz * vy, nz = ux * vy - uy * vx;
                    Accumulate(nx, nz, 0.5 * Math.Sqrt(nx * nx + nz * nz), ref sin4, ref cos4);
                }
            }
            if (Negligible(sin4, cos4)) return 0.0;
            double theta = 0.25 * Math.Atan2(sin4, cos4);
            return Math.Abs(theta) < 1e-9 ? 0.0 : theta;
        }

        static void Accumulate(double dx, double dz, double weight, ref double sin4, ref double cos4)
        {
            if (weight <= 0.0 || (dx == 0.0 && dz == 0.0)) return;
            double a = 4.0 * Math.Atan2(dz, dx);
            sin4 += weight * Math.Sin(a);
            cos4 += weight * Math.Cos(a);
        }

        static bool Negligible(double sin4, double cos4)
        {
            return Math.Abs(sin4) < 1e-9 && Math.Abs(cos4) < 1e-9;
        }

        static bool IsFloorTriangle(int t, float3[] verts, int[] indices, SurfaceClass[] classes, double y0)
        {
            if (classes == null || classes[t] != SurfaceClass.Floor) return false;
            for (int k = 0; k < 3; k++)
                if (Math.Abs(verts[indices[3 * t + k]].y - y0) > FloorBand) return false;
            return true;
        }

        static void Extend(double x, double z, ref double minX, ref double minZ, ref double maxX, ref double maxZ)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        // Mark the cells whose centre lies within PathRadius of the point (metres from the grid's low corner).
        static void MarkDisc(double x, double z, int width, int height, bool[] mask)
        {
            int iLo = Math.Max(0, (int)Math.Floor((x - PathRadius) / Cell)), iHi = Math.Min(width - 1, (int)Math.Floor((x + PathRadius) / Cell));
            int jLo = Math.Max(0, (int)Math.Floor((z - PathRadius) / Cell)), jHi = Math.Min(height - 1, (int)Math.Floor((z + PathRadius) / Cell));
            for (int j = jLo; j <= jHi; j++)
            {
                for (int i = iLo; i <= iHi; i++)
                {
                    double dx = (i + 0.5) * Cell - x, dz = (j + 0.5) * Cell - z;
                    if (dx * dx + dz * dz <= PathRadius * PathRadius) mask[j * width + i] = true;
                }
            }
        }

        // Mark every cell the triangle's footprint reaches into. Coordinates are metres from the grid's low corner.
        static void Rasterise(double x0, double z0, double x1, double z1, double x2, double z2, int width, int height,
            bool[] mask, bool[] furniture, float[] top, float topValue, bool isFurniture)
        {
            int iLo = Clamp((int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)) / Cell), 0, width - 1);
            int iHi = Clamp((int)Math.Floor(Math.Max(x0, Math.Max(x1, x2)) / Cell), 0, width - 1);
            int jLo = Clamp((int)Math.Floor(Math.Min(z0, Math.Min(z1, z2)) / Cell), 0, height - 1);
            int jHi = Clamp((int)Math.Floor(Math.Max(z0, Math.Max(z1, z2)) / Cell), 0, height - 1);
            for (int j = jLo; j <= jHi; j++)
            {
                for (int i = iLo; i <= iHi; i++)
                {
                    if (!FloorGridMath.TriangleOverlapsCell(x0, z0, x1, z1, x2, z2,
                            (i + 0.5) * Cell, (j + 0.5) * Cell, 0.5 * Cell, OverlapTolerance)) continue;
                    int c = j * width + i;
                    mask[c] = true;
                    if (top != null && topValue > top[c]) top[c] = topValue;
                    if (isFurniture && furniture != null) furniture[c] = true;
                }
            }
        }

        // Quality: with an outline, the share of its cells that were never seen; without one, the share of
        // unseen cells in the bounding rectangle of what was seen.
        static float UnknownShare(bool[] floor, bool[] obst, bool[] inside, int width, int height, bool hasOutline)
        {
            int total = 0, unseen = 0;
            if (hasOutline)
            {
                for (int c = 0; c < floor.Length; c++)
                {
                    if (!inside[c]) continue;
                    total++;
                    if (!floor[c] && !obst[c]) unseen++;
                }
            }
            else
            {
                int iLo = width, iHi = -1, jLo = height, jHi = -1;
                for (int c = 0; c < floor.Length; c++)
                {
                    if (!inside[c]) continue;
                    int i = c % width, j = c / width;
                    if (i < iLo) iLo = i;
                    if (i > iHi) iHi = i;
                    if (j < jLo) jLo = j;
                    if (j > jHi) jHi = j;
                }
                for (int j = jLo; j <= jHi; j++)
                {
                    for (int i = iLo; i <= iHi; i++)
                    {
                        total++;
                        if (!inside[j * width + i]) unseen++;
                    }
                }
            }
            return total > 0 ? (float)unseen / total : 0f;
        }

        static float2[] ToMetres(int2[] ring, double originX, double originZ)
        {
            var points = new float2[ring.Length];
            for (int k = 0; k < ring.Length; k++)
                points[k] = new float2((float)(originX + ring[k].x * Cell), (float)(originZ + ring[k].y * Cell));
            return points;
        }

        // Supplied boxes moved into the room frame, then one oriented box per obstacle blob that no supplied
        // box explains (a blob is explained when at least half its cells lie under a supplied box).
        static ObstacleBox[] BuildBoxes(FloorGridInput input, FloorGridResult result, bool[] blob, float[] top,
            int width, int height, double originX, double originZ, double frameYaw, double y0)
        {
            var boxes = new List<ObstacleBox>();
            int externalCount = input.ExternalBoxes != null ? input.ExternalBoxes.Length : 0;
            for (int e = 0; e < externalCount; e++)
            {
                ObstacleBox box = input.ExternalBoxes[e];
                float2 xz = result.ToRoom(new float2(box.Base.x, box.Base.z));
                box.Base = new float3(xz.x, (float)(box.Base.y - y0), xz.y);
                box.Yaw = (float)WrapAngle(box.Yaw - frameYaw);
                box.FromScan = false;
                boxes.Add(box);
            }
            var seen = new bool[blob.Length];
            var stack = new int[blob.Length];
            var members = new List<int>();
            for (int start = 0; start < blob.Length; start++)
            {
                if (!blob[start] || seen[start]) continue;
                members.Clear();
                int sp = 0;
                stack[sp++] = start;
                seen[start] = true;
                while (sp > 0)
                {
                    int c = stack[--sp];
                    members.Add(c);
                    int i = c % width, j = c / width;
                    if (i + 1 < width && blob[c + 1] && !seen[c + 1]) { seen[c + 1] = true; stack[sp++] = c + 1; }
                    if (i > 0 && blob[c - 1] && !seen[c - 1]) { seen[c - 1] = true; stack[sp++] = c - 1; }
                    if (j + 1 < height && blob[c + width] && !seen[c + width]) { seen[c + width] = true; stack[sp++] = c + width; }
                    if (j > 0 && blob[c - width] && !seen[c - width]) { seen[c - width] = true; stack[sp++] = c - width; }
                }
                if (members.Count < MinBlobCells) continue;
                if (Explained(members, boxes, externalCount, width, originX, originZ)) continue;
                boxes.Add(FitBox(members, top, width, originX, originZ));
            }
            return boxes.ToArray();
        }

        static bool Explained(List<int> members, List<ObstacleBox> boxes, int externalCount, int width,
            double originX, double originZ)
        {
            for (int e = 0; e < externalCount; e++)
            {
                ObstacleBox box = boxes[e];
                double c = Math.Cos(box.Yaw), s = Math.Sin(box.Yaw);
                double hx = 0.5 * box.Size.x + Cell, hz = 0.5 * box.Size.z + Cell;
                int under = 0;
                for (int k = 0; k < members.Count; k++)
                {
                    double dx = originX + (members[k] % width + 0.5) * Cell - box.Base.x;
                    double dz = originZ + (members[k] / width + 0.5) * Cell - box.Base.z;
                    if (Math.Abs(dx * c - dz * s) <= hx && Math.Abs(dx * s + dz * c) <= hz) under++;
                }
                if (2 * under >= members.Count) return true;
            }
            return false;
        }

        // Smallest bounding rectangle of the blob's cells over yaws of 0 to 85 degrees in 5 degree steps. The
        // room-aligned box wins unless a turned one is clearly smaller, because most furniture stands square.
        static ObstacleBox FitBox(List<int> members, float[] top, int width, double originX, double originZ)
        {
            double bestArea = double.MaxValue, bestYaw = 0.0, bestU = 0.0, bestV = 0.0, bestW = 0.0, bestD = 0.0;
            for (int step = 0; step < 18; step++)
            {
                double yaw = step * (Math.PI / 36.0);
                double c = Math.Cos(yaw), s = Math.Sin(yaw);
                double uLo = double.MaxValue, uHi = double.MinValue, vLo = double.MaxValue, vHi = double.MinValue;
                for (int k = 0; k < members.Count; k++)
                {
                    double x = (members[k] % width + 0.5) * Cell, z = (members[k] / width + 0.5) * Cell;
                    double u = x * c - z * s, v = x * s + z * c;
                    if (u < uLo) uLo = u;
                    if (u > uHi) uHi = u;
                    if (v < vLo) vLo = v;
                    if (v > vHi) vHi = v;
                }
                // A cell reaches half a cell (times |cos| + |sin|) beyond its centre along a turned axis.
                double pad = 0.5 * Cell * (Math.Abs(c) + Math.Abs(s));
                double w = uHi - uLo + 2.0 * pad, d = vHi - vLo + 2.0 * pad;
                double area = w * d * (step == 0 ? 0.9 : 1.0);
                if (area >= bestArea) continue;
                bestArea = area; bestYaw = yaw; bestW = w; bestD = d;
                bestU = 0.5 * (uLo + uHi); bestV = 0.5 * (vLo + vHi);
            }
            float tallest = 0f;
            for (int k = 0; k < members.Count; k++) tallest = Math.Max(tallest, top[members[k]]);
            double bc = Math.Cos(bestYaw), bs = Math.Sin(bestYaw);
            return new ObstacleBox
            {
                Base = new float3((float)(originX + bestU * bc + bestV * bs), 0f, (float)(originZ - bestU * bs + bestV * bc)),
                Size = new float3((float)bestW, tallest, (float)bestD),
                Yaw = (float)bestYaw,
                FromScan = true,
            };
        }

        static double WrapAngle(double a)
        {
            a = Math.IEEERemainder(a, 2.0 * Math.PI);
            return a <= -Math.PI ? a + 2.0 * Math.PI : a;
        }

        // A result that has no grid: empty arrays, so readers need no null checks.
        static FloorGridResult Empty(FloorGridResult result)
        {
            result.Status = FloorGridStatus.NoFloor;
            result.Standing = true;
            result.Cells = Array.Empty<FloorCell>();
            result.Sdf = Array.Empty<float>();
            result.WalkableOuter = Array.Empty<float2>();
            result.WalkableHoles = Array.Empty<float2[]>();
            result.Boxes = Array.Empty<ObstacleBox>();
            result.Fit = LoopFit.Fit(0.0, 0.0);
            result.M00 = 1.0;
            result.M11 = 1.0;
            return result;
        }
    }
}
