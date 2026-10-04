using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>What <see cref="FittedShapes.Build"/> made of an object.</summary>
    public enum FirstShapeKind : byte
    {
        /// <summary>Nothing: the object keeps its placeholder box.</summary>
        None = 0,
        /// <summary>The fixed template of the category.</summary>
        Template = 1,
        /// <summary>The template of a chair, couch or bed with its measures taken from the object's profile.</summary>
        Fitted = 2,
        /// <summary>A shape cut from the profile: columns on an 8 x 8 grid, or 4 x 4.</summary>
        ScanCut = 3
    }

    /// <summary>
    /// First shapes made from an object's profile (design section 5.2, "First shapes"; brief S3): the fitted
    /// templates of chair, couch and bed, and the shape cut from the scan for objects without a template.
    /// Engine-free. The meshes are built by <see cref="FallbackMeshBuilder"/>, so they keep its limits: no edge
    /// over 0.25 m and at most 1,500 triangles.
    /// </summary>
    public static class FittedShapes
    {
        /// <summary>The profile is 16 x 16 cells, index = j * 16 + i, row 0 at the lowest local z.</summary>
        public const int Grid = 16, Cells = Grid * Grid;
        /// <summary>A profile that sees fewer cells than this share of the footprint is not used.</summary>
        public const float MinCoverage = 0.30f;
        /// <summary>The first row of the front half (local +Z is the front).</summary>
        public const int FrontRow = 8;
        /// <summary>The palette cell of the category tint is 32 + the category's index (the placeholder box's colour).</summary>
        public const int CategoryPaletteBase = 32;

        // Where the seat (or mattress) top is in the template, and the range a fit may move it to, as fractions of h.
        struct Seat { public float Template, Min, Max; }

        static readonly Seat s_chair = new Seat { Template = 0.50f, Min = 0.25f, Max = 0.70f };
        static readonly Seat s_couch = new Seat { Template = 0.45f, Min = 0.25f, Max = 0.65f };
        static readonly Seat s_bed = new Seat { Template = 0.55f, Min = 0.30f, Max = 0.75f };

        /// <summary>True for a profile of exactly 256 bytes.</summary>
        public static bool IsProfile(byte[] profile) { return profile != null && profile.Length == Cells; }

        /// <summary>Cells that are not 0, over 256; 0 for no profile.</summary>
        public static float Coverage(byte[] profile)
        {
            if (!IsProfile(profile)) return 0f;
            int seen = 0;
            for (int i = 0; i < Cells; i++) if (profile[i] != 0) seen++;
            return seen / (float)Cells;
        }

        /// <summary>True when the profile sees enough of the footprint to shape anything.</summary>
        public static bool IsUsable(byte[] profile) { return Coverage(profile) >= MinCoverage; }

        /// <summary>
        /// The first shape of an object: the template of its category, fitted by the profile for chair, couch
        /// and bed; otherwise, for an object without a template or whose template cannot be built, the shape cut
        /// from the scan. <paramref name="into"/> is empty when the kind is <see cref="FirstShapeKind.None"/>.
        /// </summary>
        /// <param name="set">The built-in set; null or without the category's template gives no template.</param>
        /// <param name="profile">The object's profile, or null.</param>
        public static FirstShapeKind Build(FallbackMeshBuilder builder, FallbackSet set, ObjectCategory category, float3 size,
                                           float yaw, byte[] profile, MeshData into)
        {
            if (builder == null) throw new ArgumentNullException("builder");
            if (into == null) throw new ArgumentNullException("into");
            FallbackTemplate template = set != null ? set.Get(category) : null;
            if (template != null)
            {
                FallbackTemplate fitted = Fit(template, category, profile);
                if (builder.Build(fitted, size, yaw, into)) return ReferenceEquals(fitted, template) ? FirstShapeKind.Template : FirstShapeKind.Fitted;
            }
            if (BuildScanCut(builder, category, size, yaw, profile, into)) return FirstShapeKind.ScanCut;
            into.Clear();
            return FirstShapeKind.None;
        }

        // ---- Fitted templates ----------------------------------------------------------------------------------

        /// <summary>
        /// The template of a chair, couch or bed with its seat or mattress at the height the profile shows: the
        /// median height of the seen cells in the front half (rows 8 to 15) as a fraction of the box height,
        /// kept within the range of the category. The back keeps standing on local -Z to the full height. The
        /// template itself when the category is another one, there is no profile, its coverage is under
        /// <see cref="MinCoverage"/> or the front half is unseen.
        /// </summary>
        public static FallbackTemplate Fit(FallbackTemplate template, ObjectCategory category, byte[] profile)
        {
            if (template == null) throw new ArgumentNullException("template");
            Seat seat;
            switch (category)
            {
                case ObjectCategory.Chair: seat = s_chair; break;
                case ObjectCategory.Couch: seat = s_couch; break;
                case ObjectCategory.Bed: seat = s_bed; break;
                default: return template;
            }
            if (!IsUsable(profile)) return template;
            float median;
            if (!FrontMedian(profile, out median)) return template;
            float top = math.clamp(median, seat.Min, seat.Max);
            if (math.abs(top - seat.Template) < 1e-6f) return template;

            // A piecewise linear stretch of height that keeps the floor, the seat and the top of the box in place
            // of themselves: every rectangle of the template stays an axis-aligned rectangle at its place.
            float below = top / seat.Template, above = (1f - top) / (1f - seat.Template);
            var positions = new float3[template.Positions.Length];
            for (int v = 0; v < positions.Length; v++)
            {
                float3 p = template.Positions[v];
                p.y = p.y <= seat.Template ? p.y * below : top + (p.y - seat.Template) * above;
                positions[v] = p;
            }
            return new FallbackTemplate
            {
                Name = template.Name, PaletteCell = template.PaletteCell,
                Positions = positions, Normals = template.Normals, Indices = template.Indices
            };
        }

        /// <summary>The median of the seen cells of rows 8 to 15, as a fraction of the box height. False when none is seen.</summary>
        public static bool FrontMedian(byte[] profile, out float fraction)
        {
            fraction = 0f;
            if (!IsProfile(profile)) return false;
            var seen = new byte[Cells / 2];
            int n = 0;
            for (int k = FrontRow * Grid; k < Cells; k++) if (profile[k] != 0) seen[n++] = profile[k];
            if (n == 0) return false;
            Array.Sort(seen, 0, n);
            float middle = (n & 1) == 1 ? seen[n / 2] : 0.5f * (seen[n / 2 - 1] + seen[n / 2]);
            fraction = middle / 255f;
            return true;
        }

        // ---- The shape cut from the scan -----------------------------------------------------------------------

        /// <summary>
        /// The profile reduced to 8 x 8 (the highest of each 2 x 2), unseen cells filled from their seen
        /// neighbours, one column per cell from the floor to its height; the top faces, the faces between
        /// columns of different height and the faces round the outside. If that passes
        /// <see cref="FallbackMeshBuilder.MaxTriangles"/> it is built again at 4 x 4. False, with
        /// <paramref name="into"/> empty, when the profile cannot be used or the box is too large for either.
        /// Tinted with the cell of the category's placeholder box.
        /// </summary>
        public static bool BuildScanCut(FallbackMeshBuilder builder, ObjectCategory category, float3 size, float yaw,
                                        byte[] profile, MeshData into)
        {
            if (builder == null) throw new ArgumentNullException("builder");
            if (into == null) throw new ArgumentNullException("into");
            into.Clear();
            if (!IsUsable(profile)) return false;
            int cell = CategoryPaletteBase + ObjectCategories.TintIndex(category);
            for (int n = 8; n >= 4; n /= 2)
            {
                FallbackTemplate columns = ColumnTemplate(profile, n, cell);
                if (columns != null && builder.Build(columns, size, yaw, into)) return true;
            }
            into.Clear();
            return false;
        }

        /// <summary>The columns as a template of rectangles in the unit box, or null when nothing is seen.</summary>
        public static FallbackTemplate ColumnTemplate(byte[] profile, int n, int paletteCell)
        {
            int[] heights = Reduce(profile, n);
            if (heights == null) return null;

            var positions = new System.Collections.Generic.List<float3>(n * n * 12);
            var normals = new System.Collections.Generic.List<float3>(n * n * 12);
            var indices = new System.Collections.Generic.List<int>(n * n * 18);
            float step = 1f / n;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float x0 = -0.5f + i * step, x1 = x0 + step, z0 = -0.5f + j * step, z1 = z0 + step;
                    float top = heights[j * n + i] / 255f;
                    // The corners are listed as tools/gen_fallback_set.py lists a box's faces.
                    Quad(positions, normals, indices, new float3(0f, 1f, 0f),
                        new float3(x0, top, z0), new float3(x0, top, z1), new float3(x1, top, z1), new float3(x1, top, z0));
                    // Each side shows where its column is higher than the neighbour (the outside is the floor).
                    float to;
                    to = i + 1 < n ? heights[j * n + i + 1] / 255f : 0f;
                    if (top > to)
                        Quad(positions, normals, indices, new float3(1f, 0f, 0f),
                            new float3(x1, to, z0), new float3(x1, top, z0), new float3(x1, top, z1), new float3(x1, to, z1));
                    to = i > 0 ? heights[j * n + i - 1] / 255f : 0f;
                    if (top > to)
                        Quad(positions, normals, indices, new float3(-1f, 0f, 0f),
                            new float3(x0, to, z0), new float3(x0, to, z1), new float3(x0, top, z1), new float3(x0, top, z0));
                    to = j + 1 < n ? heights[(j + 1) * n + i] / 255f : 0f;
                    if (top > to)
                        Quad(positions, normals, indices, new float3(0f, 0f, 1f),
                            new float3(x0, to, z1), new float3(x1, to, z1), new float3(x1, top, z1), new float3(x0, top, z1));
                    to = j > 0 ? heights[(j - 1) * n + i] / 255f : 0f;
                    if (top > to)
                        Quad(positions, normals, indices, new float3(0f, 0f, -1f),
                            new float3(x0, to, z0), new float3(x0, top, z0), new float3(x1, top, z0), new float3(x1, to, z0));
                }
            }
            return new FallbackTemplate
            {
                Name = "scancut", PaletteCell = paletteCell,
                Positions = positions.ToArray(), Normals = normals.ToArray(), Indices = indices.ToArray()
            };
        }

        static void Quad(System.Collections.Generic.List<float3> positions, System.Collections.Generic.List<float3> normals,
                         System.Collections.Generic.List<int> indices, float3 normal, float3 a, float3 b, float3 c, float3 d)
        {
            int first = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            for (int k = 0; k < 4; k++) normals.Add(normal);
            indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
            indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
        }

        /// <summary>
        /// The profile on an n x n grid (n = 8 or 4, index = j * n + i): the highest value of each block, then
        /// unseen cells filled, in passes, with the mean of their seen neighbours (left, right, below, above)
        /// until none is unseen. Null when no cell is seen.
        /// </summary>
        public static int[] Reduce(byte[] profile, int n)
        {
            if (!IsProfile(profile) || n < 1 || Grid % n != 0) return null;
            int block = Grid / n;
            var grid = new int[n * n];
            int seen = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int best = 0;
                    for (int dj = 0; dj < block; dj++)
                        for (int di = 0; di < block; di++)
                            best = Math.Max(best, profile[(j * block + dj) * Grid + i * block + di]);
                    grid[j * n + i] = best;
                    if (best != 0) seen++;
                }
            }
            if (seen == 0) return null;

            var next = new int[n * n];
            while (seen < n * n)
            {
                Array.Copy(grid, next, grid.Length);
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (grid[j * n + i] != 0) continue;
                        int sum = 0, count = 0;
                        if (i > 0 && grid[j * n + i - 1] != 0) { sum += grid[j * n + i - 1]; count++; }
                        if (i + 1 < n && grid[j * n + i + 1] != 0) { sum += grid[j * n + i + 1]; count++; }
                        if (j > 0 && grid[(j - 1) * n + i] != 0) { sum += grid[(j - 1) * n + i]; count++; }
                        if (j + 1 < n && grid[(j + 1) * n + i] != 0) { sum += grid[(j + 1) * n + i]; count++; }
                        if (count == 0) continue;
                        next[j * n + i] = Math.Max(1, (2 * sum + count) / (2 * count));
                        seen++;
                    }
                }
                Array.Copy(next, grid, grid.Length);
            }
            return grid;
        }
    }
}
