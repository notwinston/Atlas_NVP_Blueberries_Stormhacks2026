using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// Triangle targets of scanned objects (Mesh Design section 5.4): the 120,000 triangles of furniture are
    /// shared by surface area between the objects that receive meshes, at most thirty-two, largest first.
    /// Integer arithmetic only, so that this class, the Forge (budget.py) and tools/rooms/roommath.py agree.
    /// </summary>
    public static class ObjectMeshRules
    {
        public const int MaxMeshedObjects = 32;
        public const int FurnitureTriangles = 120000;
        public const int TriTargetMin = 3000;
        public const int TriTargetMax = 15000;
        public const int LightTriangles = 60000;
        public const int LightTargetMin = 1500;
        public const int LightTargetMax = 5000;
        /// <summary>The target of every object of a room that is all stand-ins (the stand-in mesh has 5,000 triangles).</summary>
        public const int StandInTriangles = 5000;

        /// <summary>
        /// The full target when every meshed object has the same surface area: clamp(120000 / n, 3000, 15000)
        /// with integer division; 0 when no object receives a mesh.
        /// </summary>
        public static int TriTarget(int meshedCount)
        {
            if (meshedCount <= 0) return 0;
            return Math.Min(Math.Max(FurnitureTriangles / meshedCount, TriTargetMin), TriTargetMax);
        }

        /// <summary>The light target of the room: clamp(60000 / n, 1500, 5000); 0 when no object receives a mesh.</summary>
        public static int LightTarget(int meshedCount)
        {
            if (meshedCount <= 0) return 0;
            return Math.Min(Math.Max(LightTriangles / meshedCount, LightTargetMin), LightTargetMax);
        }

        /// <summary>Metres to whole millimetres: floor(v * 1000 + 0.5), from the value as written in the file.</summary>
        static long Mm(float metres) { return (long)Math.Floor(RoomModelJson.Widen(metres) * 1000.0 + 0.5); }

        /// <summary>
        /// The full target of every object, in the order of the array: the thirty-two largest by volume in whole
        /// cubic millimetres (ties to the lower id) share the budget by surface area, the rest get 0.
        /// </summary>
        public static int[] Targets(RoomObject[] objects)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            int n = objects.Length;
            var w = new long[n];
            var h = new long[n];
            var d = new long[n];
            var volume = new long[n];
            var order = new int[n];
            for (int k = 0; k < n; k++)
            {
                w[k] = Mm(objects[k].Size.x); h[k] = Mm(objects[k].Size.y); d[k] = Mm(objects[k].Size.z);
                volume[k] = w[k] * h[k] * d[k];
                order[k] = k;
            }
            Array.Sort(order, (a, b) =>
            {
                int byVolume = volume[b].CompareTo(volume[a]);
                return byVolume != 0 ? byVolume : objects[a].Id.CompareTo(objects[b].Id);
            });
            int picked = Math.Min(n, MaxMeshedObjects);
            var targets = new int[n];
            if (picked == 0) return targets;

            var area = new long[picked];
            long total = 0;
            for (int k = 0; k < picked; k++)
            {
                int o = order[k];
                area[k] = 2 * (w[o] * h[o] + h[o] * d[o] + w[o] * d[o]);
                total += area[k];
            }
            var t = new long[picked];
            long sum = 0;
            for (int k = 0; k < picked; k++)
            {
                t[k] = Math.Min(Math.Max(FurnitureTriangles * area[k] / total, TriTargetMin), TriTargetMax);
                sum += t[k];
            }
            if (sum > FurnitureTriangles)
            {
                long extra = sum - (long)TriTargetMin * picked;
                long budget = FurnitureTriangles - (long)TriTargetMin * picked;
                for (int k = 0; k < picked; k++) t[k] = TriTargetMin + (t[k] - TriTargetMin) * budget / extra;
            }
            for (int k = 0; k < picked; k++) targets[order[k]] = (int)t[k];
            return targets;
        }

        /// <summary>Writes Targets into TriTarget of every object.</summary>
        public static void Assign(RoomObject[] objects)
        {
            int[] targets = Targets(objects);
            for (int k = 0; k < objects.Length; k++) objects[k].TriTarget = targets[k];
        }
    }

    /// <summary>
    /// The consistency rules of a RoomModel that a schema cannot express: the same rules, with the same
    /// numbers, as tools/rooms/validate_room.py (rules R1 to R21 of the room contract page). Engine-free.
    /// It allocates and is meant for load time, tests and tools, never for the frame path.
    /// </summary>
    public static class RoomModelValidator
    {
        const double Cell = 0.05;
        const int ObjectProfileBytes = 256;      // R21
        const double Tol = 1e-4;                 // metres; files carry six decimals
        const double LatticeTol = 1e-5;          // metres; a float holds a 0.025 m multiple to better than this
        const double FieldTol = 0.0015;          // metres; R5
        const double OverlapTol = 1e-4;          // metres; touching a cell is not overlapping it
        const double RectSideCap = 3.6;          // metres; a longer short side gains nothing (lap max + inset)

        /// <summary>
        /// Applies every rule. Errors and warnings are appended as text that starts with the rule's number.
        /// True when no error was added. The stand-in mesh itself (5,000 triangles, no edge over 0.25 m) is
        /// checked where it is built, in the renderer's tests.
        /// </summary>
        public static bool Validate(RoomModel room, List<string> errors, List<string> warnings = null)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            int before = errors.Count;
            if (room == null) { errors.Add("The room is null"); return false; }
            if (room.Sdf == null) { errors.Add("R3 the room has no distance field"); return false; }
            if (room.Outline == null || room.Outline.Length < 3) { errors.Add("R1 the outline has fewer than 3 points"); return false; }

            RoomSdf sdf = room.Sdf;
            int w = sdf.Width, h = sdf.Height;
            double ox = RoomModelJson.Widen(sdf.OriginX), oz = RoomModelJson.Widen(sdf.OriginZ);
            double wf = RoomModelJson.Widen(room.FreeRect.Wf), df = RoomModelJson.Widen(room.FreeRect.Df);
            double[] outline = Flatten(room.Outline);

            Outline(outline, errors);                                                     // R1
            GridExtent(outline, ox, oz, w, h, errors);                                    // R2
            bool[] free = FreeCells(sdf, outline, ox, oz, errors);                        // R3, R4
            FieldIsDistanceTransform(sdf, free, errors);                                  // R5
            FreeRectLattice(wf, df, errors);                                              // R6
            bool[] walk = WalkRegion(sdf, ox, oz, wf, df, errors, out int missing);       // R7
            BestRectangle(walk, w, h, wf, df, missing, errors);                           // R8
            WalkablePolygonMatches(room.Walkable, walk, w, h, ox, oz, errors);            // R9
            LoopFitValues(room, wf, df, errors, warnings);                                // R10 to R13
            Walls(room, outline, errors);                                                 // R14, R15
            Objects(room, ox, oz, errors);                                                // R16, R17, R21
            ObjectsOffFreeFloor(room, free, w, h, ox, oz, errors);                        // R18
            if (warnings != null && string.IsNullOrEmpty(room.Anchor.Id) && room.Quality.Route != ScanRoute.Fixture)
                warnings.Add("R19 anchor.id is empty for a scanned room");
            return errors.Count == before;
        }

        /// <summary>Every error of Validate, or an empty array for a valid room.</summary>
        public static string[] Errors(RoomModel room)
        {
            var errors = new List<string>();
            Validate(room, errors);
            return errors.ToArray();
        }

        /// <summary>Rules R6, R10 to R13, R16 and R17 only: null when they hold, otherwise the first violation.</summary>
        internal static string FirstCheapError(RoomModel room)
        {
            if (room == null) return "The room is null";
            if (room.Sdf == null) return "R3 the room has no distance field";
            var errors = new List<string>(4);
            double wf = RoomModelJson.Widen(room.FreeRect.Wf), df = RoomModelJson.Widen(room.FreeRect.Df);
            FreeRectLattice(wf, df, errors);
            if (errors.Count == 0) LoopFitValues(room, wf, df, errors, null);
            if (errors.Count == 0)
                Objects(room, RoomModelJson.Widen(room.Sdf.OriginX), RoomModelJson.Widen(room.Sdf.OriginZ), errors);
            return errors.Count == 0 ? null : errors[0];
        }

        // ---- R1, R2 --------------------------------------------------------------------------------------

        static void Outline(double[] p, List<string> errors)
        {
            int n = p.Length / 2;
            double area = Area(p);
            if (area <= 0.0) errors.Add("R1 outline is not counter-clockwise (signed area " + F(area) + ")");
            if (!IsSimple(p)) errors.Add("R1 outline crosses itself");
            for (int k = 0; k < n; k++)
            {
                int q = (k + 1) % n;
                if (Dist(p[2 * k], p[2 * k + 1], p[2 * q], p[2 * q + 1]) < Cell - LatticeTol)
                    errors.Add("R1 outline edge " + I(k) + " is shorter than 0.05 m");
            }
        }

        static void GridExtent(double[] outline, double ox, double oz, int w, int h, List<string> errors)
        {
            if (!IsMultiple(ox, 0.5 * Cell)) errors.Add("R2 sdf.originX = " + F(ox) + " is not a multiple of 0.025 m");
            if (!IsMultiple(oz, 0.5 * Cell)) errors.Add("R2 sdf.originZ = " + F(oz) + " is not a multiple of 0.025 m");
            double xLo = double.MaxValue, xHi = double.MinValue, zLo = double.MaxValue, zHi = double.MinValue;
            for (int k = 0; k < outline.Length; k += 2)
            {
                xLo = Math.Min(xLo, outline[k]); xHi = Math.Max(xHi, outline[k]);
                zLo = Math.Min(zLo, outline[k + 1]); zHi = Math.Max(zHi, outline[k + 1]);
            }
            double margin = RoomSdf.GridMarginCells * Cell - LatticeTol;
            if (xLo - ox < margin || ox + w * Cell - xHi < margin || zLo - oz < margin || oz + h * Cell - zHi < margin)
                errors.Add("R2 the grid does not reach 0.25 m beyond the outline on every side");
        }

        // ---- R3, R4, R5 ----------------------------------------------------------------------------------

        static bool[] FreeCells(RoomSdf sdf, double[] outline, double ox, double oz, List<string> errors)
        {
            int w = sdf.Width, h = sdf.Height;
            var free = new bool[w * h];
            int zeros = 0, outside = 0, onRim = 0;
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    float v = sdf.Metres[j * w + i];
                    if (v == 0f) { zeros++; continue; }
                    if (v < 0f) continue;
                    free[j * w + i] = true;
                    if (!InPolygon(ox + (i + 0.5) * Cell, oz + (j + 0.5) * Cell, outline)) outside++;
                    if (i == 0 || j == 0 || i == w - 1 || j == h - 1) onRim++;
                }
            }
            if (zeros > 0) errors.Add("R3 " + I(zeros) + " sdf cells are exactly 0; the field is never 0 at a cell centre");
            if (onRim > 0) errors.Add("R4 " + I(onRim) + " free cells on the rim of the grid");
            if (outside > 0) errors.Add("R4 " + I(outside) + " free cells lie outside the outline");
            return free;
        }

        static void FieldIsDistanceTransform(RoomSdf sdf, bool[] free, List<string> errors)
        {
            int w = sdf.Width, h = sdf.Height;
            double[] expect = SignedDistance(free, w, h);
            double worst = 0.0;
            for (int k = 0; k < expect.Length; k++)
            {
                double e = Math.Floor(expect[k] * 1000.0 + 0.5) / 1000.0;
                e = Math.Max(-32.768, Math.Min(32.767, e));
                worst = Math.Max(worst, Math.Abs(e - sdf.Metres[k]));
            }
            if (worst > FieldTol)
                errors.Add("R5 sdf differs from the distance transform of its sign by up to " + F(worst) + " m");
        }

        /// <summary>
        /// Exact distance, in metres, from every cell centre to the edge of the free region: positive in free
        /// cells (to the nearest not-free cell's square; cells beyond the grid count as not-free), negative in the
        /// others (to the nearest free cell's square). Row-major, row 0 at the lowest z.
        /// </summary>
        public static double[] SignedDistance(bool[] free, int width, int height)
        {
            if (free == null || free.Length != width * height) throw new ArgumentException("free needs width * height cells");
            double[] toBlocked = UnsignedTo(free, width, height, false);
            double[] toFree = UnsignedTo(free, width, height, true);
            var sdf = new double[width * height];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    int k = j * width + i;
                    if (free[k])
                    {
                        double d = Math.Min(toBlocked[k], Math.Min(Math.Min(i + 0.5, width - i - 0.5), Math.Min(j + 0.5, height - j - 0.5)));
                        sdf[k] = d * Cell;
                    }
                    else
                    {
                        sdf[k] = toFree[k] < Big ? -(toFree[k] * Cell) : -(Math.Max(width, height) * Cell);
                    }
                }
            }
            return sdf;
        }

        const double Big = 1e9;

        // Distance in cells from each centre to the nearest cell whose free flag equals `target`: one pass along
        // the rows for the horizontal gap, then the exact minimum down each column.
        static double[] UnsignedTo(bool[] free, int w, int h, bool target)
        {
            var row = new double[w * h];
            for (int j = 0; j < h; j++)
            {
                int last = -1;
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i;
                    if (free[k] == target) { last = i; row[k] = 0.0; }
                    else row[k] = last >= 0 ? (i - last) - 0.5 : Big;
                }
                last = -1;
                for (int i = w - 1; i >= 0; i--)
                {
                    int k = j * w + i;
                    if (free[k] == target) last = i;
                    else if (last >= 0) row[k] = Math.Min(row[k], (last - i) - 0.5);
                }
            }
            var result = new double[w * h];
            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++)
                {
                    double best = Big;
                    for (int jj = 0; jj < h; jj++)
                    {
                        double hgap = row[jj * w + i];
                        if (hgap >= Big) continue;
                        double vgap = Math.Max(Math.Abs(j - jj) - 0.5, 0.0);
                        if (vgap >= best) continue;
                        double d = Math.Sqrt(hgap * hgap + vgap * vgap);
                        if (d < best) best = d;
                    }
                    result[j * w + i] = best;
                }
            }
            return result;
        }

        // ---- R6 to R9 ------------------------------------------------------------------------------------

        static void FreeRectLattice(double wf, double df, List<string> errors)
        {
            if (wf > df + Tol) errors.Add("R6 freeRect.wf (" + F(wf) + ") is larger than freeRect.df (" + F(df) + ")");
            if (!IsMultiple(wf, Cell)) errors.Add("R6 freeRect.wf = " + F(wf) + " is not a multiple of 0.05 m");
            if (!IsMultiple(df, Cell)) errors.Add("R6 freeRect.df = " + F(df) + " is not a multiple of 0.05 m");
        }

        /// <summary>
        /// WALK: the 4-connected part of {sdf > 0.25 m} that holds the room origin (the component with the
        /// nearest cell when the origin itself is not walkable). Row-major mask of the field's size.
        /// </summary>
        public static bool[] WalkCells(RoomSdf sdf)
        {
            if (sdf == null) throw new ArgumentNullException(nameof(sdf));
            return WalkComponent(sdf, RoomModelJson.Widen(sdf.OriginX), RoomModelJson.Widen(sdf.OriginZ), out _);
        }

        static bool[] WalkComponent(RoomSdf sdf, double ox, double oz, out bool originWalkable)
        {
            int w = sdf.Width, h = sdf.Height;
            // The origin lies on a cell corner or a cell centre; a quarter cell below it names one cell either way.
            double seedX = -0.25 * Cell, seedZ = -0.25 * Cell;
            int si = (int)Math.Floor((seedX - ox) / Cell), sj = (int)Math.Floor((seedZ - oz) / Cell);
            originWalkable = si >= 0 && sj >= 0 && si < w && sj < h && sdf.Metres[sj * w + si] > RoomSdf.WalkThreshold;
            var walk = new bool[w * h];
            if (!originWalkable)
            {
                double bestDist = double.MaxValue;
                si = -1;
                for (int j = 0; j < h; j++)
                {
                    for (int i = 0; i < w; i++)
                    {
                        if (!(sdf.Metres[j * w + i] > RoomSdf.WalkThreshold)) continue;
                        double dx = ox + (i + 0.5) * Cell - seedX, dz = oz + (j + 0.5) * Cell - seedZ;
                        double d = dx * dx + dz * dz;
                        if (d < bestDist) { bestDist = d; si = i; sj = j; }
                    }
                }
                if (si < 0) return walk;
            }
            var stack = new Stack<int>(256);
            walk[sj * w + si] = true;
            stack.Push(sj * w + si);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int i = k % w, j = k / w;
                if (i > 0) Visit(sdf, walk, stack, k - 1);
                if (i < w - 1) Visit(sdf, walk, stack, k + 1);
                if (j > 0) Visit(sdf, walk, stack, k - w);
                if (j < h - 1) Visit(sdf, walk, stack, k + w);
            }
            return walk;
        }

        static void Visit(RoomSdf sdf, bool[] walk, Stack<int> stack, int k)
        {
            if (walk[k] || !(sdf.Metres[k] > RoomSdf.WalkThreshold)) return;
            walk[k] = true;
            stack.Push(k);
        }

        static bool[] WalkRegion(RoomSdf sdf, double ox, double oz, double wf, double df, List<string> errors, out int missing)
        {
            int w = sdf.Width, h = sdf.Height;
            bool[] walk = WalkComponent(sdf, ox, oz, out bool originWalkable);
            if (!originWalkable) errors.Add("R7 the origin is not inside the walkable region");
            missing = 0;
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    double x = ox + (i + 0.5) * Cell, z = oz + (j + 0.5) * Cell;
                    if (Math.Abs(x) < 0.5 * wf && Math.Abs(z) < 0.5 * df && !walk[j * w + i]) missing++;
                }
            }
            if (missing > 0) errors.Add("R7 " + I(missing) + " cells of the free rectangle are not walkable");
            return walk;
        }

        static void BestRectangle(bool[] walk, int w, int h, double wf, double df, int missing, List<string> errors)
        {
            // Largest square of walkable cells: its side is the longest short side any rectangle can have.
            int side = 0;
            var prev = new int[w + 1];
            var cur = new int[w + 1];
            for (int j = 0; j < h; j++)
            {
                Array.Clear(cur, 0, cur.Length);
                for (int i = 0; i < w; i++)
                {
                    if (!walk[j * w + i]) continue;
                    cur[i + 1] = 1 + Math.Min(prev[i], Math.Min(prev[i + 1], cur[i]));
                    if (cur[i + 1] > side) side = cur[i + 1];
                }
                int[] swap = prev; prev = cur; cur = swap;
            }
            int need = Math.Min(side, (int)Math.Round(RectSideCap / Cell));

            // Every maximal rectangle, by the histogram method; keep the largest area among those wide enough.
            int bestArea = 0;
            var heights = new int[w];
            var stackStart = new int[w + 1];
            var stackHeight = new int[w + 1];
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++) heights[i] = walk[j * w + i] ? heights[i] + 1 : 0;
                int top = 0;
                for (int i = 0; i <= w; i++)
                {
                    int now = i < w ? heights[i] : 0;
                    int start = i;
                    while (top > 0 && stackHeight[top - 1] >= now)
                    {
                        top--;
                        int rw = i - stackStart[top], rh = stackHeight[top];
                        if (rh > 0 && Math.Min(rw, rh) >= need) bestArea = Math.Max(bestArea, rw * rh);
                        start = stackStart[top];
                    }
                    stackStart[top] = start;
                    stackHeight[top] = now;
                    top++;
                }
            }

            int wfCells = (int)Math.Round(wf / Cell), dfCells = (int)Math.Round(df / Cell);
            if (Math.Min(wfCells, dfCells) < need)
                errors.Add("R8 a rectangle with a short side of " + F(need * Cell) + " m fits; freeRect has " + F(Math.Min(wf, df)) + " m");
            else if (wfCells * dfCells != bestArea && missing == 0)
                errors.Add("R8 freeRect area is " + F(wf * df) + " m2; the best rectangle has " + F(bestArea * Cell * Cell) + " m2");
        }

        static void WalkablePolygonMatches(WalkablePolygon polygon, bool[] walk, int w, int h, double ox, double oz, List<string> errors)
        {
            if (polygon == null || polygon.Outer == null || polygon.Outer.Length < 3)
            {
                errors.Add("R9 walkable.outer is missing");
                return;
            }
            double[] outer = Flatten(polygon.Outer);
            if (Area(outer) <= 0.0) errors.Add("R9 walkable.outer is not counter-clockwise");
            var holes = new double[polygon.Holes.Length][];
            for (int k = 0; k < holes.Length; k++)
            {
                holes[k] = Flatten(polygon.Holes[k]);
                if (Area(holes[k]) >= 0.0) errors.Add("R9 walkable.holes[" + I(k) + "] is not clockwise");
            }
            for (int r = -1; r < holes.Length; r++)
            {
                double[] ring = r < 0 ? outer : holes[r];
                for (int k = 0; k < ring.Length; k += 2)
                {
                    if (IsMultiple(ring[k] - ox, Cell) && IsMultiple(ring[k + 1] - oz, Cell)) continue;
                    errors.Add("R9 walkable vertex (" + F(ring[k]) + ", " + F(ring[k + 1]) + ") is not on a cell corner");
                    break;
                }
            }
            int mismatch = 0;
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    double x = ox + (i + 0.5) * Cell, z = oz + (j + 0.5) * Cell;
                    bool inside = InPolygon(x, z, outer);
                    for (int k = 0; inside && k < holes.Length; k++)
                        if (InPolygon(x, z, holes[k])) inside = false;
                    if (inside != walk[j * w + i]) mismatch++;
                }
            }
            if (mismatch > 0) errors.Add("R9 walkable polygon and {sdf > 0.25 m} disagree on " + I(mismatch) + " cells");
        }

        // ---- R10 to R13 ----------------------------------------------------------------------------------

        static void LoopFitValues(RoomModel room, double wf, double df, List<string> errors, List<string> warnings)
        {
            LoopFitResult fit = LoopFit.Fit(wf, df);
            if (!Near(room.LapSide, fit.S)) errors.Add("R10 lapSquare.side = " + F(room.LapSide) + ", expected " + F(fit.S));
            if (!Near(room.Curvature.L5, fit.L5)) errors.Add("R11 curvature.l5 = " + F(room.Curvature.L5) + ", expected " + F(fit.L5));
            if (!Near(room.Curvature.L6, fit.L6)) errors.Add("R11 curvature.l6 = " + F(room.Curvature.L6) + ", expected " + F(fit.L6));
            if (!Near(room.Curvature.R3, fit.R3)) errors.Add("R11 curvature.r3 = " + F(room.Curvature.R3) + ", expected " + F(fit.R3));
            if (warnings != null && (!Near(room.Curvature.FloorL, LoopFit.FloorL) || !Near(room.Curvature.FloorR, LoopFit.FloorR)))
                warnings.Add("R11 curvature floors differ from the constants in force");

            GluedGrid g = CellGridRules.Glued(wf, df);
            if (room.Glued.Nx != g.Nx || room.Glued.Nz != g.Nz || !Near(room.Glued.SizeX, g.SizeX) || !Near(room.Glued.SizeZ, g.SizeZ))
                errors.Add("R12 cells.glued differs from the rule: expected " + I(g.Nx) + " x " + I(g.Nz) + " of " + F(g.SizeX) + " x " + F(g.SizeZ));
            AlignedGrid a = CellGridRules.Aligned(wf, df);
            if (room.Aligned.N != a.N || room.Aligned.M != a.M || !Near(room.Aligned.G, a.G))
                errors.Add("R12 cells.aligned differs from the rule: expected " + I(a.N) + " x " + I(a.M) + ", g = " + F(a.G));
            if (room.Standing != LoopFit.IsStanding(wf, df))
                errors.Add("R13 standing = " + (room.Standing ? "true" : "false") + ", expected the opposite");
        }

        // ---- R14, R15 ------------------------------------------------------------------------------------

        static void Walls(RoomModel room, double[] outline, List<string> errors)
        {
            int n = outline.Length / 2;
            for (int k = 0; k < room.Walls.Length; k++)
            {
                RoomWall wall = room.Walls[k];
                double ax = wall.A.x, az = wall.A.y, bx = wall.B.x, bz = wall.B.y;
                double length = Dist(ax, az, bx, bz);
                if (length < Cell - LatticeTol)
                {
                    errors.Add("R14 walls[" + I(k) + "] is shorter than 0.05 m");
                    continue;
                }
                bool onEdge = false;
                for (int e = 0; e < n && !onEdge; e++)
                {
                    int f = (e + 1) % n;
                    double px = outline[2 * e], pz = outline[2 * e + 1], qx = outline[2 * f], qz = outline[2 * f + 1];
                    onEdge = PointSegmentDistance(ax, az, px, pz, qx, qz) <= 0.01 &&
                             PointSegmentDistance(bx, bz, px, pz, qx, qz) <= 0.01 &&
                             (bx - ax) * (qx - px) + (bz - az) * (qz - pz) > 0.0;
                }
                if (!onEdge) errors.Add("R14 walls[" + I(k) + "] does not lie on an outline edge, or runs against it");

                for (int m = 0; m < wall.Openings.Length; m++)
                {
                    WallOpening o = wall.Openings[m];
                    string at = "R15 walls[" + I(k) + "].openings[" + I(m) + "]";
                    if (o.Offset + o.Width > length + Tol) errors.Add(at + " runs past the end of the wall");
                    if (!(o.Bottom < o.Top && o.Top <= wall.Height + Tol)) errors.Add(at + " needs bottom < top <= height");
                    if (o.Kind == OpeningKind.Door && o.Bottom > Tol) errors.Add(at + ": a door starts at the floor");
                }
                // Openings sorted by offset must not run into the next one.
                var order = new int[wall.Openings.Length];
                for (int m = 0; m < order.Length; m++) order[m] = m;
                WallOpening[] openings = wall.Openings;
                Array.Sort(order, (x, y) => openings[x].Offset.CompareTo(openings[y].Offset));
                for (int m = 0; m + 1 < order.Length; m++)
                {
                    WallOpening o = openings[order[m]];
                    if (openings[order[m + 1]].Offset < o.Offset + o.Width - Tol)
                    {
                        errors.Add("R15 walls[" + I(k) + "] has overlapping openings");
                        break;
                    }
                }
            }
        }

        // ---- R16, R17, R18 -------------------------------------------------------------------------------

        static void Objects(RoomModel room, double ox, double oz, List<string> errors)
        {
            RoomObject[] objects = room.Objects;
            if (objects.Length > RoomModel.MaxObjects) errors.Add("R16 more than 64 objects");
            double xHi = ox + room.Sdf.Width * Cell, zHi = oz + room.Sdf.Height * Cell;
            int standIns = 0;
            for (int k = 0; k < objects.Length; k++)
            {
                RoomObject o = objects[k];
                for (int m = 0; m < k; m++)
                {
                    if (objects[m].Id != o.Id) continue;
                    errors.Add("R16 object ids are not unique (" + I(o.Id) + ")");
                    break;
                }
                if ((o.Mesh == MeshState.Ready) != !string.IsNullOrEmpty(o.AssetHash))
                    errors.Add("R16 object " + I(o.Id) + ": assetHash must be set exactly when mesh is ready");
                if (!(o.Base.x > ox && o.Base.x < xHi && o.Base.z > oz && o.Base.z < zHi))
                    errors.Add("R16 object " + I(o.Id) + " lies outside the grid");
                if (o.Base.y < -0.05f) errors.Add("R16 object " + I(o.Id) + " starts below the floor");
                if (!YawReadable(o.Yaw)) errors.Add("R16 object " + I(o.Id) + ": yaw is outside (-3.1415928, 3.1415928] once written");
                if (o.Mesh == MeshState.StandIn) standIns++;
                if (o.Profile != null && o.Profile.Length != ObjectProfileBytes)
                    errors.Add("R21 object " + I(o.Id) + ": profile is " + I(o.Profile.Length) + " bytes, expected 256");
            }
            if (standIns > 0)
            {
                if (standIns != objects.Length) errors.Add("R17 stand-in objects cannot be mixed with other mesh states");
                for (int k = 0; k < objects.Length; k++)
                    if (objects[k].Mesh == MeshState.StandIn && objects[k].TriTarget != ObjectMeshRules.StandInTriangles)
                        errors.Add("R17 stand-in object " + I(objects[k].Id) + " must have triTarget 5000");
                return;
            }
            int[] expected = ObjectMeshRules.Targets(objects);
            for (int k = 0; k < objects.Length; k++)
                if (objects[k].TriTarget != expected[k])
                    errors.Add("R17 object " + I(objects[k].Id) + ": triTarget = " + I(objects[k].TriTarget) + ", expected " + I(expected[k]));
        }

        /// <summary>
        /// True when the yaw, as RoomModelJson writes it (six decimals), is inside the range the reader and the
        /// schema accept: a room that validates must read back.
        /// </summary>
        static bool YawReadable(float yaw)
        {
            if (float.IsNaN(yaw) || float.IsInfinity(yaw)) return false;
            double written = double.Parse(RoomModelJson.FormatNumber(RoomModelJson.Widen(yaw)), NumberStyles.Float,
                CultureInfo.InvariantCulture);
            return written > -3.1415928 && written <= 3.1415928;
        }

        static void ObjectsOffFreeFloor(RoomModel room, bool[] free, int w, int h, double ox, double oz, List<string> errors)
        {
            for (int k = 0; k < room.Objects.Length; k++)
            {
                RoomObject o = room.Objects[k];
                if (!o.BlocksFloor) continue;
                double bx = o.Base.x, bz = o.Base.z, hx = 0.5 * o.Size.x, hz = 0.5 * o.Size.z;
                double reach = Math.Sqrt(hx * hx + hz * hz) + Cell;
                int iLo = Math.Max(0, (int)Math.Floor((bx - reach - ox) / Cell)), iHi = Math.Min(w - 1, (int)Math.Floor((bx + reach - ox) / Cell));
                int jLo = Math.Max(0, (int)Math.Floor((bz - reach - oz) / Cell)), jHi = Math.Min(h - 1, (int)Math.Floor((bz + reach - oz) / Cell));
                double c = Math.Cos(o.Yaw), s = Math.Sin(o.Yaw);
                int covered = 0;
                for (int j = jLo; j <= jHi; j++)
                    for (int i = iLo; i <= iHi; i++)
                        if (free[j * w + i] && FootprintOverlapsCell(bx, bz, hx, hz, c, s, ox + (i + 0.5) * Cell, oz + (j + 0.5) * Cell))
                            covered++;
                if (covered > 0) errors.Add("R18 object " + I(o.Id) + " stands on " + I(covered) + " free cells");
            }
        }

        // Separating-axis test of a turned box footprint against a cell square. Yaw is Unity's: local +X goes to
        // (cos, -sin) and local +Z to (sin, cos) in (x, z). Touching does not count.
        static bool FootprintOverlapsCell(double bx, double bz, double hx, double hz, double c, double s, double cx, double cz)
        {
            const double hc = 0.5 * Cell;
            double dx = cx - bx, dz = cz - bz;
            double uxX = c, uxZ = -s, uzX = s, uzZ = c;
            double rx = hx * Math.Abs(uxX) + hz * Math.Abs(uzX);
            double rz = hx * Math.Abs(uxZ) + hz * Math.Abs(uzZ);
            if (Math.Abs(dx) >= rx + hc - OverlapTol || Math.Abs(dz) >= rz + hc - OverlapTol) return false;
            if (Math.Abs(dx * uxX + dz * uxZ) >= hx + hc * (Math.Abs(uxX) + Math.Abs(uxZ)) - OverlapTol) return false;
            if (Math.Abs(dx * uzX + dz * uzZ) >= hz + hc * (Math.Abs(uzX) + Math.Abs(uzZ)) - OverlapTol) return false;
            return true;
        }

        // ---- small geometry ------------------------------------------------------------------------------

        static double[] Flatten(float2[] ring)
        {
            var p = new double[ring.Length * 2];
            for (int k = 0; k < ring.Length; k++)
            {
                p[2 * k] = RoomModelJson.Widen(ring[k].x);
                p[2 * k + 1] = RoomModelJson.Widen(ring[k].y);
            }
            return p;
        }

        static double Area(double[] p)
        {
            int n = p.Length / 2;
            double total = 0.0;
            for (int k = 0; k < n; k++)
            {
                int q = (k + 1) % n;
                total += p[2 * k] * p[2 * q + 1] - p[2 * q] * p[2 * k + 1];
            }
            return 0.5 * total;
        }

        static bool InPolygon(double x, double z, double[] p)
        {
            bool inside = false;
            int n = p.Length / 2;
            for (int k = 0; k < n; k++)
            {
                int q = (k + 1) % n;
                double x0 = p[2 * k], z0 = p[2 * k + 1], x1 = p[2 * q], z1 = p[2 * q + 1];
                if ((z0 > z) != (z1 > z) && x < x0 + (z - z0) * (x1 - x0) / (z1 - z0)) inside = !inside;
            }
            return inside;
        }

        static bool IsSimple(double[] p)
        {
            int n = p.Length / 2;
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    if ((b + 1) % n == a || (a + 1) % n == b) continue;
                    if (SegmentsCross(p, a, (a + 1) % n, b, (b + 1) % n)) return false;
                }
            }
            return true;
        }

        static double Orient(double[] p, int a, int b, int c)
        {
            return (p[2 * b] - p[2 * a]) * (p[2 * c + 1] - p[2 * a + 1]) - (p[2 * b + 1] - p[2 * a + 1]) * (p[2 * c] - p[2 * a]);
        }

        static bool SegmentsCross(double[] p, int a0, int a1, int b0, int b1)
        {
            double d1 = Orient(p, b0, b1, a0), d2 = Orient(p, b0, b1, a1);
            double d3 = Orient(p, a0, a1, b0), d4 = Orient(p, a0, a1, b1);
            return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
        }

        static double PointSegmentDistance(double px, double pz, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az;
            double length2 = dx * dx + dz * dz;
            double t = length2 == 0.0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((px - ax) * dx + (pz - az) * dz) / length2));
            return Dist(px, pz, ax + t * dx, az + t * dz);
        }

        static double Dist(double x0, double z0, double x1, double z1)
        {
            double dx = x1 - x0, dz = z1 - z0;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        static bool Near(double a, double b) { return Math.Abs(a - b) <= Tol; }

        static bool IsMultiple(double value, double step)
        {
            double q = value / step;
            return Math.Abs(q - Math.Floor(q + 0.5)) <= LatticeTol / step;
        }

        static string F(double value) { return value.ToString("0.0###", CultureInfo.InvariantCulture); }
        static string I(int value) { return value.ToString(CultureInfo.InvariantCulture); }
    }
}
