using System;
using System.Collections.Generic;
using Atlas.Scan.Grid;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// Everything a scan has gathered at one moment, in the AR session frame with +Y up: plain arrays that
    /// are never changed after the snapshot is taken, so a worker thread may read them. Floor points are
    /// (x, z).
    /// </summary>
    public sealed class ScanSnapshot
    {
        /// <summary>The classified mesh; null or empty when there is none (route 1).</summary>
        public float3[] Vertices;
        /// <summary>Three per triangle.</summary>
        public int[] Indices;
        /// <summary>One per triangle.</summary>
        public SurfaceClass[] Classes;
        /// <summary>True when the triangles were laid over detected planes (XR Simulation, no LiDAR) and are no scanned mesh.</summary>
        public bool MeshFromPlanes;
        public HorizontalPlane[] Planes;
        public WallPlane[] Walls;
        /// <summary>Height of the tallest wall plane's upper edge in the session frame, metres; NaN when no wall was seen.</summary>
        public float WallTop = float.NaN;
        /// <summary>Furniture boxes from the provider, already merged (ObjectBoxMerge). Null for none.</summary>
        public ObstacleBox[] Boxes;
        /// <summary>Floor points the walker passed over, in order.</summary>
        public float2[] Path;
        /// <summary>Where the walker stands.</summary>
        public float2 Walker;
        /// <summary>Floor height y0 when a floor plane or a tapped corner gave it; NaN lets FloorScan derive it.</summary>
        public float FloorHeight = float.NaN;
        /// <summary>The outline the walker tapped or corrected, either winding; null asks for a proposal.</summary>
        public float2[] Outline;

        public int TriangleCount { get { return Indices != null ? Indices.Length / 3 : 0; } }
    }

    /// <summary>What a snapshot gives: a room, or the reason there is none.</summary>
    public sealed class ScanSolution
    {
        public ScanFailure Failure;
        /// <summary>The highest route that contributed to Room.</summary>
        public ScanRoute Route;
        /// <summary>The grid behind Room; for a failed solve, the last grid that was built, or null.</summary>
        public FloorGridResult Grid;
        /// <summary>The room in its own frame; null when Failure is set. Room.Anchor is the pose of that frame in the session frame.</summary>
        public RoomModel Room;
        /// <summary>The outline in the session frame, counter-clockwise; null when none could be made.</summary>
        public float2[] Outline;
        /// <summary>y0 in the session frame.</summary>
        public float FloorY;
        /// <summary>Square metres of floor cells found.</summary>
        public float FloorArea;
        /// <summary>Share of cells inside the outline that were never seen, 0..1.</summary>
        public float UnknownShare;
    }

    /// <summary>
    /// The scan routes as one calculation (Master Plan section 4.2): from a snapshot to a RoomModel by the
    /// highest route that succeeds. Route 2 (and 3) run FloorScan on the classified mesh; when that gives no
    /// walkable floor the outline alone is used (route 1), first with the furniture boxes and then without.
    /// Engine-free: the scan session calls it on a worker thread, tests call it directly.
    /// </summary>
    public static class ScanSolver
    {
        /// <summary>A proposed outline follows the cells to within this, metres.</summary>
        public const float ProposalTolerance = 0.08f;
        /// <summary>Wall height limits when it is taken from the wall planes, metres.</summary>
        public const float MinWallHeight = 2.0f, MaxWallHeight = 6.0f;

        /// <summary>
        /// Solves a snapshot. With buildRoom false only the outline and the progress numbers are produced
        /// (the preview during the sweep).
        /// </summary>
        public static ScanSolution Solve(ScanSnapshot scan, string roomId, float defaultWallHeight = RoomModel.DefaultWallHeight,
            bool buildRoom = true)
        {
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            var solution = new ScanSolution { Route = ScanRoute.ManualOutline, FloorY = scan.FloorHeight };
            bool hasMesh = scan.TriangleCount > 0;
            bool hasBoxes = scan.Boxes != null && scan.Boxes.Length > 0;

            // 1. The outline: the walker's own when there is one, otherwise a proposal from what was seen.
            float2[] outline = null;
            if (scan.Outline != null)
            {
                outline = OutlineBuilder.Normalise(scan.Outline, scan.Outline.Length);
                if (outline == null)
                {
                    solution.Failure = ScanFailure.OutlineInvalid;
                    return solution;
                }
            }
            if (outline == null && hasMesh)
            {
                FloorGridResult seen = FloorScanCore.Run(MeshInput(scan, null, hasBoxes));
                solution.Grid = seen;
                if (seen.Status != FloorGridStatus.NoFloor)
                {
                    solution.FloorY = seen.FloorHeight;
                    solution.FloorArea = FloorArea(seen);
                    solution.UnknownShare = seen.UnknownShare;
                    outline = ProposeOutline(seen, scan.Walker);
                }
            }
            if (outline == null && scan.Path != null)
                outline = OutlineBuilder.FromPath(scan.Path, scan.Path.Length);
            if (outline == null)
            {
                solution.Failure = ScanFailure.NoFloor;
                return solution;
            }
            solution.Outline = outline;
            if (!buildRoom && solution.Grid != null) return solution;

            // 2. The grid, by the highest route that gives walkable floor.
            FloorGridResult grid = null;
            if (hasMesh)
            {
                grid = FloorScanCore.Run(MeshInput(scan, outline, hasBoxes));
                solution.Route = scan.MeshFromPlanes ? ScanRoute.ManualOutline : ScanRoute.FloorScan;
                if (Usable(grid)) grid = BlockUnderScanBoxes(scan, outline, grid, hasBoxes);
            }
            float floorY = FloorHeightOf(scan, grid);
            if (grid == null || !Usable(grid))
            {
                grid = FloorScanCore.Run(RoomModelBuilder.OutlineInput(outline, hasBoxes ? scan.Boxes : null, scan.Walker, floorY));
                solution.Route = ScanRoute.ManualOutline;
                if (!Usable(grid) && hasBoxes)
                {
                    // A box in the wrong place must not cost the walker the room: the outline alone stands.
                    hasBoxes = false;
                    grid = FloorScanCore.Run(RoomModelBuilder.OutlineInput(outline, null, scan.Walker, floorY));
                }
            }
            solution.Grid = grid;
            solution.FloorY = grid.Status == FloorGridStatus.NoFloor ? floorY : grid.FloorHeight;
            solution.FloorArea = FloorArea(grid);
            solution.UnknownShare = grid.UnknownShare;
            if (!Usable(grid))
            {
                solution.Failure = grid.Status == FloorGridStatus.NoFloor ? ScanFailure.NoFloor : ScanFailure.TooSmall;
                return solution;
            }
            if (hasBoxes) solution.Route = ScanRoute.ObjectBoxes;
            if (!buildRoom) return solution;

            // 3. The room.
            solution.Room = RoomModelBuilder.Build(grid, roomId, "", solution.Route,
                WallHeight(scan.WallTop, solution.FloorY, defaultWallHeight));
            return solution;
        }

        /// <summary>
        /// The outline FloorScan proposes from a grid built with none: the edge of everything that was seen
        /// (floor and obstacles) around the walker, simplified and squared up. In the grid's input frame,
        /// counter-clockwise. Null when nothing was seen.
        /// </summary>
        public static float2[] ProposeOutline(FloorGridResult grid, float2 walker)
        {
            if (grid == null || grid.Cells == null || grid.Cells.Length == 0) return null;
            int w = grid.Width, h = grid.Height;
            var seen = new bool[w * h];
            for (int c = 0; c < seen.Length; c++) seen[c] = (grid.Cells[c] & FloorCell.Inside) != 0;
            var outers = new List<int2[]>();
            var holes = new List<int2[]>();
            FloorGridMath.TraceRegion(seen, w, h, outers, holes);
            if (outers.Count == 0) return null;

            // The ring round the walker; failing that, the largest.
            float2 walkerRoom = grid.ToRoom(walker);
            double wx = (walkerRoom.x - grid.OriginX) / grid.CellSize, wz = (walkerRoom.y - grid.OriginZ) / grid.CellSize;
            int best = -1;
            double bestArea = 0.0;
            for (int k = 0; k < outers.Count; k++)
            {
                double area = LatticeArea(outers[k]);
                if (Contains(outers[k], wx, wz)) { best = k; break; }
                if (area > bestArea) { bestArea = area; best = k; }
            }
            int2[] ring = outers[best];
            var points = new float2[ring.Length];
            for (int k = 0; k < ring.Length; k++)
                points[k] = grid.ToInput(new float2(grid.OriginX + ring[k].x * grid.CellSize, grid.OriginZ + ring[k].y * grid.CellSize));
            float2[] simplified = OutlineBuilder.Simplify(points, points.Length, ProposalTolerance);
            float2[] clean = OutlineBuilder.Normalise(simplified, simplified.Length);
            if (clean == null) return null;
            return OutlineBuilder.SquareUp(clean);
        }

        /// <summary>
        /// Wall height above the floor: from the tallest wall plane when one was seen and gives a height a
        /// room can have, rounded to 5 cm; otherwise the default (2.4 m when the ceiling was not seen).
        /// </summary>
        public static float WallHeight(float wallTop, float floorY, float defaultHeight)
        {
            float height = wallTop - floorY;
            if (float.IsNaN(height) || height < MinWallHeight || height > MaxWallHeight) return defaultHeight;
            return (float)(Math.Round(height / 0.05) * 0.05);
        }

        // A box fitted round an obstacle blob is a rectangle, the blob need not be: under the corner of an
        // L-shaped couch the grid may hold free floor that the couch's box covers. The room shows the box, so
        // the floor under it must not be free (RoomModel rule R18): the scan's own boxes are handed back to
        // FloorScan as boxes that block. When that leaves no room, the first grid stands.
        static FloorGridResult BlockUnderScanBoxes(ScanSnapshot scan, float2[] outline, FloorGridResult grid, bool withBoxes)
        {
            ObstacleBox[] found = grid.Boxes;
            int supplied = withBoxes ? scan.Boxes.Length : 0;
            if (found == null || found.Length <= supplied) return grid;
            var all = new ObstacleBox[found.Length];
            for (int k = 0; k < supplied; k++) all[k] = scan.Boxes[k];
            for (int k = supplied; k < found.Length; k++)
            {
                // Back from the room frame to the session frame.
                ObstacleBox box = found[k];
                float2 xz = grid.ToInput(box.Base.xz);
                box.Base = new float3(xz.x, box.Base.y + grid.FloorHeight, xz.y);
                box.Yaw += grid.AnchorYaw;
                all[k] = box;
            }
            FloorGridInput input = MeshInput(scan, outline, false);
            input.ExternalBoxes = all;
            FloorGridResult blocked = FloorScanCore.Run(input);
            if (!Usable(blocked)) return grid;
            // They are still the scan's own finds, not the provider's.
            for (int k = supplied; k < blocked.Boxes.Length; k++) blocked.Boxes[k].FromScan = true;
            return blocked;
        }

        static bool Usable(FloorGridResult grid)
        {
            return grid.Status == FloorGridStatus.Ok && grid.Wf >= RoomModelBuilder.MinFreeSide;
        }

        static FloorGridInput MeshInput(ScanSnapshot scan, float2[] outline, bool withBoxes)
        {
            return new FloorGridInput
            {
                Vertices = scan.Vertices,
                Indices = scan.Indices,
                TriangleClasses = scan.Classes,
                FloorHeight = scan.FloorHeight,
                Planes = scan.Planes,
                WalkerPath = scan.Path,
                Walker = scan.Walker,
                Outline = outline,
                Walls = scan.Walls,
                ExternalBoxes = withBoxes ? scan.Boxes : null,
            };
        }

        // y0 for an outline-only grid: what the scan knew, what the mesh grid found, the planes, or zero (a
        // session with no floor in sight starts at the walker's feet only by convention).
        static float FloorHeightOf(ScanSnapshot scan, FloorGridResult meshGrid)
        {
            if (!float.IsNaN(scan.FloorHeight)) return scan.FloorHeight;
            if (meshGrid != null && meshGrid.Status != FloorGridStatus.NoFloor) return meshGrid.FloorHeight;
            if (scan.Planes != null && scan.Planes.Length > 0)
            {
                double y0 = FloorScanCore.FindFloorHeight(new FloorGridInput { Planes = scan.Planes }, Array.Empty<float3>(), Array.Empty<int>(), 0);
                if (!double.IsNaN(y0)) return (float)y0;
            }
            return 0f;
        }

        static float FloorArea(FloorGridResult grid)
        {
            if (grid == null || grid.Cells == null) return 0f;
            int floor = 0;
            for (int c = 0; c < grid.Cells.Length; c++)
                if ((grid.Cells[c] & FloorCell.Floor) != 0) floor++;
            return floor * grid.CellSize * grid.CellSize;
        }

        static double LatticeArea(int2[] ring)
        {
            double total = 0.0;
            for (int k = 0; k < ring.Length; k++)
            {
                int2 p = ring[k], q = ring[(k + 1) % ring.Length];
                total += (double)p.x * q.y - (double)q.x * p.y;
            }
            return Math.Abs(0.5 * total);
        }

        static bool Contains(int2[] ring, double x, double z)
        {
            bool inside = false;
            for (int k = 0, prev = ring.Length - 1; k < ring.Length; prev = k++)
            {
                int2 p = ring[prev], q = ring[k];
                if ((p.y > z) != (q.y > z) && x < p.x + (z - p.y) * (double)(q.x - p.x) / (q.y - p.y)) inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>
    /// The neutral stand-in for ARKit's face classes, used when a mesh arrives without them (no classification
    /// on the platform, or a class array that does not match the mesh): a face that looks up and lies in the
    /// floor band is floor, one that looks up above it is a table top, a steep one is wall, one that looks
    /// down above head height is ceiling. Engine-free.
    /// </summary>
    public static class NeutralSurfaceClasses
    {
        /// <summary>A face is level when its normal is within about 25 degrees of vertical.</summary>
        public const float LevelNormalY = 0.9f;
        /// <summary>A face is steep when its normal is within about 25 degrees of horizontal.</summary>
        public const float SteepNormalY = 0.42f;

        /// <summary>The class of one triangle with counter-clockwise or clockwise winding (the sign of the normal is not trusted near the floor).</summary>
        public static SurfaceClass Classify(float3 a, float3 b, float3 c, float floorY)
        {
            float3 normal = math.cross(b - a, c - a);
            float length = math.length(normal);
            if (!(length > 1e-12f)) return SurfaceClass.None;
            float ny = normal.y / length;
            float lo = math.min(a.y, math.min(b.y, c.y)) - floorY, hi = math.max(a.y, math.max(b.y, c.y)) - floorY;
            if (math.abs(ny) >= LevelNormalY)
            {
                if (math.abs(lo) <= (float)FloorScanCore.FloorBand && math.abs(hi) <= (float)FloorScanCore.FloorBand) return SurfaceClass.Floor;
                if (lo >= (float)FloorScanCore.ObstacleMaxHeight) return SurfaceClass.Ceiling;
                return lo > 0f ? SurfaceClass.Table : SurfaceClass.None;
            }
            return math.abs(ny) <= SteepNormalY ? SurfaceClass.Wall : SurfaceClass.None;
        }

        /// <summary>Fills classes[firstTriangle ..] for triangleCount triangles whose indices start at firstIndex.</summary>
        public static void Fill(float3[] vertices, int[] indices, int firstIndex, int triangleCount, float floorY,
            SurfaceClass[] classes, int firstTriangle)
        {
            for (int t = 0; t < triangleCount; t++)
            {
                int i = firstIndex + 3 * t;
                classes[firstTriangle + t] = Classify(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], floorY);
            }
        }

        /// <summary>
        /// A floor height for a mesh with no plane to give one: the height below which a twentieth of the
        /// level faces' area lies. NaN when the mesh has no level face.
        /// </summary>
        public static float EstimateFloor(float3[] vertices, int[] indices, int indexCount)
        {
            var heights = new List<float>();
            var areas = new List<float>();
            float total = 0f;
            for (int i = 0; i + 2 < indexCount; i += 3)
            {
                float3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                float3 normal = math.cross(b - a, c - a);
                float length = math.length(normal);
                if (!(length > 1e-12f) || math.abs(normal.y) / length < LevelNormalY) continue;
                heights.Add((a.y + b.y + c.y) / 3f);
                areas.Add(0.5f * length);
                total += 0.5f * length;
            }
            if (heights.Count == 0) return float.NaN;
            float[] h = heights.ToArray(), w = areas.ToArray();
            Array.Sort(h, w);
            float run = 0f;
            for (int k = 0; k < h.Length; k++)
            {
                run += w[k];
                if (run >= 0.05f * total) return h[k];
            }
            return h[h.Length - 1];
        }
    }
}
