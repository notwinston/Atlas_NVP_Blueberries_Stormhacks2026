// PLACEHOLDER (lane A, P1-A06): contract surface only; replaced by lane A's implementation
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>The single 128 x 128 palette texture (Master Plan §8.1), 16 x 16 cells of 8 px (deviation PH-9).</summary>
    public static class AtlasPalette
    {
        public const int Size = 128;          // Master Plan §8.1
        public const int CellsPerSide = 16;

        /// <summary>Centre of cell index 0..255, row-major from the bottom-left.</summary>
        public static Vector2 CellUV(int index)
        {
            int cx = index % CellsPerSide, cy = index / CellsPerSide;
            return new Vector2((cx + 0.5f) / CellsPerSide, (cy + 0.5f) / CellsPerSide);
        }
    }
}
