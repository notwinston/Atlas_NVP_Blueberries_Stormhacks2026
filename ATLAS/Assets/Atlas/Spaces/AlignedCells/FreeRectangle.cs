using System;

namespace Atlas.Spaces.AlignedCells
{
    // Plain room input owned by lane B: the free rectangle of Master Plan §4.3, centred on the anchor.
    public readonly struct FreeRectangle
    {
        public readonly double Width;   // Wf, metres, along anchor +x
        public readonly double Depth;   // Df, metres, along anchor +z

        public FreeRectangle(double width, double depth)
        {
            if (!(width > 0) || double.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width));
            if (!(depth > 0) || double.IsInfinity(depth)) throw new ArgumentOutOfRangeException(nameof(depth));
            Width = width; Depth = depth;
        }
    }
}
