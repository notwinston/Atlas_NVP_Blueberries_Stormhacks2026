using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Objects
{
    /// <summary>An sRGB picture: 3 bytes per pixel (r, g, b), row 0 at the top, Width * Height pixels.</summary>
    public struct RgbImage
    {
        public int Width, Height;
        public byte[] Rgb;
    }

    /// <summary>
    /// Colours a matched prop (or a scan cut-out) from the object's crops (standalone-phone plan D7, T6): a port of
    /// the Forge's colour rules (station/forge/forge/colour.py) reduced to one cell per colour slot.
    ///
    /// Frames. The mesh is box-local (origin at the base centre, +Y up, front +Z, Unity handedness), the frame a
    /// view's CamToBox maps into. A camera point (x, y, z) with z &gt; 0 lands on pixel (cx + fx x / z, cy - fy y / z),
    /// origin at the top-left of the crop; pixel (column, row) covers [column, column + 1) x [row, row + 1).
    ///
    /// Rule. Each face's centre is taken into every view; a face counts for a view when it turns towards the camera
    /// (dot(normal, toCamera) &gt; 0), lies in front of it and lands inside the crop. Its colour there is the mean, in
    /// linear light, of the 3 x 3 pixels around that pixel, weighted by area * cos. Per slot, the weighted median of
    /// those samples (per channel, linear light) picks the nearest object cell (64..127) in OKLab. There is no
    /// occlusion test. Crops are only read. Pure and deterministic; safe on a worker thread; never throws on empty
    /// or malformed input (a view or image it cannot use is skipped).
    /// </summary>
    public static class PropColour
    {
        public const int MaxCells = 6;

        const int CellsPerSide = 16;
        const float Near = 1e-4f;   // metres in front of the camera

        static readonly float[] s_SrgbToLinear = BuildSrgbTable();
        static readonly double3[] s_CellLab = BuildCellLab();

        /// <summary>One cell (64..127) per slot of prop, in slot order. Views/images in the same order
        /// (ObjectPhotoSet.Views). A slot no view sees keeps its default cell.</summary>
        public static byte[] Choose(PropEntry prop, MeshData fitted, ObjectView[] views, RgbImage[] images)
        {
            byte[] defaults = prop != null ? prop.SlotCells : null;
            if (defaults == null || defaults.Length == 0) return Array.Empty<byte>();
            int slots = defaults.Length;
            var cells = (byte[])defaults.Clone();
            if (fitted == null || fitted.IndexCount < 3) return cells;

            // The slot of every face, from its palette cell (the majority of its three corners).
            int faces = fitted.IndexCount / 3;
            var faceSlot = new int[faces];
            var slotArea = new double[slots];
            for (int f = 0; f < faces; f++)
            {
                int slot = SlotOf(FaceCell(fitted, f), defaults);
                faceSlot[f] = slot;
                if (slot >= 0) slotArea[slot] += FaceArea(fitted, f);
            }

            Samples samples = Gather(fitted, faceSlot, views, images);

            // Per slot, the weighted median of its samples.
            if (samples.Count > 0)
            {
                var order = new int[samples.Count];
                var keys = new float[samples.Count];
                for (int slot = 0; slot < slots; slot++)
                {
                    int n = 0;
                    for (int s = 0; s < samples.Count; s++)
                        if (samples.Group[s] == slot) order[n++] = s;
                    if (n == 0) continue;
                    float3 median = WeightedMedian(samples, order, n, keys);
                    cells[slot] = NearestCell(median);
                }
            }

            Cap(cells, slotArea);
            return cells;
        }

        /// <summary>One cell for a whole mesh (scan cut-outs). fallbackCell when no view sees any face.</summary>
        public static byte ChooseOne(MeshData mesh, ObjectView[] views, RgbImage[] images, byte fallbackCell)
        {
            if (mesh == null || mesh.IndexCount < 3) return fallbackCell;
            var faceGroup = new int[mesh.IndexCount / 3];      // every face in group 0
            Samples samples = Gather(mesh, faceGroup, views, images);
            if (samples.Count == 0) return fallbackCell;
            var order = new int[samples.Count];
            for (int s = 0; s < order.Length; s++) order[s] = s;
            return NearestCell(WeightedMedian(samples, order, order.Length, new float[samples.Count]));
        }

        /// <summary>Rewrites the Uv0 palette cell of every vertex whose cell is slotDefaultCells[i] to cells[i].
        /// Vertices of any other cell are left alone. Nothing happens for null input; extra entries of the longer
        /// array are ignored.</summary>
        public static void Apply(MeshData mesh, byte[] slotDefaultCells, byte[] cells)
        {
            if (mesh == null || slotDefaultCells == null || cells == null) return;
            int slots = math.min(slotDefaultCells.Length, cells.Length);
            if (slots == 0) return;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int slot = SlotOf(CellOf(mesh.Vertices[v].Uv0), slotDefaultCells, slots);
                if (slot >= 0 && cells[slot] != slotDefaultCells[slot]) mesh.Vertices[v].Uv0 = CellUv(cells[slot]);
            }
        }

        // ------------------------------------------------------------------ sampling

        sealed class Samples
        {
            public int Count;
            public int[] Group = new int[64];
            public float3[] Colour = new float3[64];
            public float[] Weight = new float[64];

            public void Add(int group, float3 colour, float weight)
            {
                if (Count == Group.Length)
                {
                    Array.Resize(ref Group, Count * 2);
                    Array.Resize(ref Colour, Count * 2);
                    Array.Resize(ref Weight, Count * 2);
                }
                Group[Count] = group;
                Colour[Count] = colour;
                Weight[Count] = weight;
                Count++;
            }
        }

        // One sample per (view, face) the view sees, for faces whose group is not negative. Views in order,
        // faces in order: the samples come out in the same order every time.
        static Samples Gather(MeshData mesh, int[] faceGroup, ObjectView[] views, RgbImage[] images)
        {
            var samples = new Samples();
            if (views == null || images == null) return samples;
            int count = math.min(views.Length, images.Length);
            int faces = mesh.IndexCount / 3;
            for (int k = 0; k < count; k++)
            {
                RgbImage image = images[k];
                if (image.Rgb == null || image.Width <= 0 || image.Height <= 0) continue;
                if ((long)image.Width * image.Height * 3 > image.Rgb.Length) continue;

                ObjectView view = views[k];
                float4 intrinsics = view.Intrinsics;
                // A picture decoded at another size than the crop it came from: scale the intrinsics with it.
                if (view.Width > 0 && view.Height > 0 && (view.Width != image.Width || view.Height != image.Height))
                {
                    float sx = image.Width / (float)view.Width, sy = image.Height / (float)view.Height;
                    intrinsics = new float4(intrinsics.x * sx, intrinsics.y * sy, intrinsics.z * sx, intrinsics.w * sy);
                }
                if (!(intrinsics.x > 0f) || !(intrinsics.y > 0f) || !IsFinite(intrinsics)) continue;

                float4x4 camToBox = view.CamToBox;
                if (!IsFinite(camToBox) || !(math.abs(math.determinant(camToBox)) > 1e-12f)) continue;
                float4x4 boxToCam = math.inverse(camToBox);
                if (!IsFinite(boxToCam)) continue;
                float3 eye = camToBox.c3.xyz;

                for (int f = 0; f < faces; f++)
                {
                    int group = faceGroup[f];
                    if (group < 0) continue;
                    float3 a = mesh.Vertices[mesh.Indices[3 * f]].Position;
                    float3 b = mesh.Vertices[mesh.Indices[3 * f + 1]].Position;
                    float3 c = mesh.Vertices[mesh.Indices[3 * f + 2]].Position;
                    float3 cross = FrontCross(mesh, f, a, b, c);
                    float doubleArea = math.length(cross);
                    if (!(doubleArea > 1e-12f)) continue;
                    float3 normal = cross / doubleArea;

                    float3 centre = (a + b + c) * (1f / 3f);
                    float3 toCamera = eye - centre;
                    float distance = math.length(toCamera);
                    if (!(distance > 1e-6f)) continue;
                    float cosine = math.dot(normal, toCamera) / distance;
                    if (!(cosine > 0f)) continue;

                    float3 p = math.mul(boxToCam, new float4(centre, 1f)).xyz;
                    if (!(p.z > Near)) continue;
                    float u = intrinsics.z + intrinsics.x * p.x / p.z;
                    float v = intrinsics.w - intrinsics.y * p.y / p.z;
                    if (!(u >= 0f && u < image.Width && v >= 0f && v < image.Height)) continue;

                    float3 colour = WindowMean(in image, (int)math.floor(u), (int)math.floor(v));
                    samples.Add(group, colour, 0.5f * doubleArea * cosine);
                }
            }
            return samples;
        }

        // The mean, in linear light, of the 3 x 3 pixels around (column, row) that lie inside the picture.
        static float3 WindowMean(in RgbImage image, int column, int row)
        {
            float3 sum = float3.zero;
            int n = 0;
            for (int r = math.max(0, row - 1); r <= math.min(image.Height - 1, row + 1); r++)
            {
                for (int c = math.max(0, column - 1); c <= math.min(image.Width - 1, column + 1); c++)
                {
                    int i = (r * image.Width + c) * 3;
                    sum += new float3(s_SrgbToLinear[image.Rgb[i]], s_SrgbToLinear[image.Rgb[i + 1]], s_SrgbToLinear[image.Rgb[i + 2]]);
                    n++;
                }
            }
            return sum / n;     // n >= 1: (column, row) itself is inside
        }

        // Per channel: the smallest value whose cumulative weight reaches half the total. Equal values give the same
        // answer in any order, so the unstable sort keeps this deterministic.
        static float3 WeightedMedian(Samples samples, int[] order, int n, float[] keys)
        {
            var items = new int[n];
            float3 result = float3.zero;
            for (int channel = 0; channel < 3; channel++)
            {
                double total = 0;
                for (int i = 0; i < n; i++)
                {
                    items[i] = order[i];
                    keys[i] = samples.Colour[order[i]][channel];
                    total += samples.Weight[order[i]];
                }
                Array.Sort(keys, items, 0, n);
                double half = 0.5 * total, running = 0;
                float value = keys[n - 1];
                for (int i = 0; i < n; i++)
                {
                    running += samples.Weight[items[i]];
                    if (running >= half) { value = keys[i]; break; }
                }
                result[channel] = value;
            }
            return result;
        }

        // ------------------------------------------------------------------ the cap

        // At most MaxCells distinct cells: the least-used cell (by slot area; ties: the higher cell) merges into the
        // nearest remaining one in OKLab, until the cap holds.
        static void Cap(byte[] cells, double[] slotArea)
        {
            while (true)
            {
                int distinct = 0;
                int weakest = -1;
                double weakestUse = double.MaxValue;
                for (int s = 0; s < cells.Length; s++)
                {
                    if (FirstSlotOf(cells, cells[s]) != s) continue;
                    distinct++;
                    double use = 0;
                    for (int t = 0; t < cells.Length; t++) if (cells[t] == cells[s]) use += slotArea[t];
                    if (use < weakestUse || (use == weakestUse && cells[s] > weakest))
                    {
                        weakestUse = use;
                        weakest = cells[s];
                    }
                }
                if (distinct <= MaxCells) return;

                int into = -1;
                double best = double.MaxValue;
                for (int s = 0; s < cells.Length; s++)
                {
                    int cell = cells[s];
                    if (cell == weakest || FirstSlotOf(cells, cells[s]) != s) continue;
                    double d = DistanceSq(LabOf(weakest), LabOf(cell));
                    if (d < best || (d == best && cell < into)) { best = d; into = cell; }
                }
                for (int s = 0; s < cells.Length; s++) if (cells[s] == weakest) cells[s] = (byte)into;
            }
        }

        static int FirstSlotOf(byte[] cells, byte cell)
        {
            for (int s = 0; s < cells.Length; s++) if (cells[s] == cell) return s;
            return -1;
        }

        // ------------------------------------------------------------------ cells and colours

        /// <summary>The object cell (64..127) nearest to a linear rgb colour in OKLab; ties go to the lower cell.</summary>
        internal static byte NearestCell(float3 linear)
        {
            double3 lab = LinearToOklab(linear);
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int k = 0; k < ObjectPaletteColours.Count; k++)
            {
                double d = DistanceSq(lab, s_CellLab[k]);
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            return (byte)(ObjectPaletteColours.First + best);
        }

        /// <summary>AtlasPalette.Uv: the centre of cell (0..255), v upwards.</summary>
        internal static float2 CellUv(int cell)
        {
            int column = cell & (CellsPerSide - 1);
            int row = (cell >> 4) & (CellsPerSide - 1);
            return new float2((column + 0.5f) / CellsPerSide, (row + 0.5f) / CellsPerSide);
        }

        /// <summary>The palette cell a palette coordinate falls in.</summary>
        internal static int CellOf(float2 uv)
        {
            int column = math.clamp((int)math.floor(uv.x * CellsPerSide), 0, CellsPerSide - 1);
            int row = math.clamp((int)math.floor(uv.y * CellsPerSide), 0, CellsPerSide - 1);
            return row * CellsPerSide + column;
        }

        static int FaceCell(MeshData mesh, int face)
        {
            int a = CellOf(mesh.Vertices[mesh.Indices[3 * face]].Uv0);
            int b = CellOf(mesh.Vertices[mesh.Indices[3 * face + 1]].Uv0);
            int c = CellOf(mesh.Vertices[mesh.Indices[3 * face + 2]].Uv0);
            return b == c ? b : a;
        }

        static int SlotOf(int cell, byte[] slotCells) { return SlotOf(cell, slotCells, slotCells.Length); }

        static int SlotOf(int cell, byte[] slotCells, int count)
        {
            for (int s = 0; s < count; s++) if (slotCells[s] == cell) return s;
            return -1;
        }

        static double FaceArea(MeshData mesh, int face)
        {
            float3 a = mesh.Vertices[mesh.Indices[3 * face]].Position;
            float3 b = mesh.Vertices[mesh.Indices[3 * face + 1]].Position;
            float3 c = mesh.Vertices[mesh.Indices[3 * face + 2]].Position;
            float area = 0.5f * math.length(math.cross(b - a, c - a));
            return area > 0f && !float.IsInfinity(area) ? area : 0.0;   // NaN fails the test
        }

        // The face's area vector, pointing to its front: Unity's clockwise winding (MeshData) puts the front along
        // cross(b - a, c - a). When the stored vertex normals disagree, they win: a mesh read with the other winding
        // is still coloured from the side it shows.
        static float3 FrontCross(MeshData mesh, int face, float3 a, float3 b, float3 c)
        {
            float3 cross = math.cross(b - a, c - a);
            float3 normals = mesh.Vertices[mesh.Indices[3 * face]].Normal
                           + mesh.Vertices[mesh.Indices[3 * face + 1]].Normal
                           + mesh.Vertices[mesh.Indices[3 * face + 2]].Normal;
            return math.dot(cross, normals) < 0f ? -cross : cross;
        }

        // OKLab from linear rgb (Björn Ottosson's matrices, as station/forge/forge/palette.py:_srgb_to_oklab).
        static double3 LinearToOklab(float3 linear)
        {
            double r = math.max(0f, linear.x), g = math.max(0f, linear.y), b = math.max(0f, linear.z);
            double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
            double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
            double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
            return new double3(
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        static double3 LabOf(int cell) { return s_CellLab[cell - ObjectPaletteColours.First]; }

        static double DistanceSq(double3 p, double3 q)
        {
            double3 d = p - q;
            return d.x * d.x + d.y * d.y + d.z * d.z;
        }

        static double3[] BuildCellLab()
        {
            var lab = new double3[ObjectPaletteColours.Count];
            for (int k = 0; k < lab.Length; k++) lab[k] = LinearToOklab(ObjectPaletteColours.Linear(ObjectPaletteColours.First + k));
            return lab;
        }

        static float[] BuildSrgbTable()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            }
            return table;
        }

        static bool IsFinite(float4 v) { return math.all(math.isfinite(v)); }

        static bool IsFinite(float4x4 m) { return IsFinite(m.c0) && IsFinite(m.c1) && IsFinite(m.c2) && IsFinite(m.c3); }
    }
}
