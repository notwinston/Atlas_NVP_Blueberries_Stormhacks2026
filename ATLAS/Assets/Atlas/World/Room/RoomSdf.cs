using System;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// The distance field of a room on the 5 cm grid: positive on free floor, negative elsewhere, metres to the
    /// edge of the free region (Master Plan section 4.3). Immutable. Safe to read from any thread.
    /// Cell (i, j) covers x from OriginX + 0.05 i and z from OriginZ + 0.05 j; its value belongs to its centre.
    /// </summary>
    public sealed class RoomSdf
    {
        /// <summary>Metres.</summary>
        public const float CellSize = 0.05f;
        /// <summary>Metres the field reads short of a wall (section 4.2).</summary>
        public const float UnderRead = 0.05f;
        /// <summary>WALK = {sdf > 0.25}.</summary>
        public const float WalkThreshold = 0.25f;
        public const int GridMarginCells = 5;

        const double Cell = 0.05;

        public readonly int Width, Height;
        /// <summary>Low corner of cell (0, 0), metres.</summary>
        public readonly float OriginX, OriginZ;
        /// <summary>Width * Height, index = j * Width + i, row 0 at the lowest z.</summary>
        public readonly float[] Metres;

        /// <summary>Takes ownership of the array. ArgumentException when the length is not width * height.</summary>
        public RoomSdf(int width, int height, float originX, float originZ, float[] metres)
        {
            if (width < 2 || height < 2) throw new ArgumentException("A distance field is at least 2 x 2 cells");
            if (metres == null || metres.Length != width * height)
                throw new ArgumentException("The distance field needs width * height values", nameof(metres));
            Width = width;
            Height = height;
            OriginX = originX;
            OriginZ = originZ;
            Metres = metres;
        }

        /// <summary>The stored value of a cell. No bounds check beyond the array's own.</summary>
        public float At(int i, int j) { return Metres[j * Width + i]; }

        public float2 CellCentre(int i, int j)
        {
            return new float2((float)(OriginX + (i + 0.5) * Cell), (float)(OriginZ + (j + 0.5) * Cell));
        }

        /// <summary>Cell that contains the point; may lie outside the grid.</summary>
        public void CellOf(float2 xz, out int i, out int j)
        {
            i = (int)Math.Floor((xz.x - (double)OriginX) / Cell);
            j = (int)Math.Floor((xz.y - (double)OriginZ) / Cell);
        }

        public bool InGrid(int i, int j) { return i >= 0 && j >= 0 && i < Width && j < Height; }

        /// <summary>
        /// The field at any floor point. u = (x - OriginX) / 0.05 - 0.5 and v likewise; (u, v) is clamped to
        /// [0, Width - 1] x [0, Height - 1]; the value is bilinear between the four nearest centres, minus
        /// 0.05 * the distance in cells between (u, v) and its clamped point. So it keeps falling outside the grid.
        /// </summary>
        public float Sample(float2 xz)
        {
            double u = (xz.x - (double)OriginX) / Cell - 0.5;
            double v = (xz.y - (double)OriginZ) / Cell - 0.5;
            double uc = Math.Min(Math.Max(u, 0.0), Width - 1.0);
            double vc = Math.Min(Math.Max(v, 0.0), Height - 1.0);
            int i0 = Math.Min((int)Math.Floor(uc), Width - 2);
            int j0 = Math.Min((int)Math.Floor(vc), Height - 2);
            double fu = uc - i0, fv = vc - j0;
            int k = j0 * Width + i0;
            double low = Metres[k] * (1.0 - fu) + Metres[k + 1] * fu;
            double high = Metres[k + Width] * (1.0 - fu) + Metres[k + Width + 1] * fu;
            double value = low * (1.0 - fv) + high * fv;
            double du = u - uc, dv = v - vc;
            if (du != 0.0 || dv != 0.0) value -= Math.Sqrt(du * du + dv * dv) * Cell;
            return (float)value;
        }

        /// <summary>Direction of increasing clearance at a point: central differences of Sample at +-0.05 m,
        /// normalised; zero when the gradient is shorter than 1e-4.</summary>
        public float2 Gradient(float2 xz)
        {
            float gx = Sample(new float2(xz.x + CellSize, xz.y)) - Sample(new float2(xz.x - CellSize, xz.y));
            float gz = Sample(new float2(xz.x, xz.y + CellSize)) - Sample(new float2(xz.x, xz.y - CellSize));
            var g = new float2(gx, gz) / (2f * CellSize);
            float len = math.length(g);
            return len < 1e-4f ? float2.zero : g / len;
        }

        /// <summary>The cell that contains the point is free. False outside the grid.</summary>
        public bool IsFree(float2 xz)
        {
            int i, j;
            CellOf(xz, out i, out j);
            return InGrid(i, j) && Metres[j * Width + i] > 0f;
        }

        /// <summary>Sample(xz) > WalkThreshold.</summary>
        public bool IsWalkable(float2 xz) { return Sample(xz) > WalkThreshold; }
    }
}
