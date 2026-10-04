using System;

namespace Atlas.Rendering
{
    /// <summary>Rungs of the thermal ladder (Master Plan section 8.6). 0 to 3 are the Console's quality tiers.</summary>
    public enum QualityTier : byte
    {
        Nominal = 0,
        Fair = 1,
        Serious = 2,
        Critical = 3,
        /// <summary>For a cool phone and for video capture. Never entered automatically.</summary>
        High = 4,
    }

    /// <summary>What one rung sets.</summary>
    public readonly struct QualityRow
    {
        public readonly QualityTier Tier;
        /// <summary>Render scale of the eye-texture set: 0.8, 0.7 or 0.6.</summary>
        public readonly float RenderScale;
        /// <summary>MSAA sample count of the eye textures: 4, 2 or 1 (off).</summary>
        public readonly int Msaa;
        /// <summary>Fog radius rho: where the fog is opaque and from which the draw rule follows. Kernel units.</summary>
        public readonly float CullRho;
        /// <summary>Copies per side of the Glued lattice: 7, 5 or 3.</summary>
        public readonly int GluedCopies;
        /// <summary>Frames per second: 60, or 30 on the last rung.</summary>
        public readonly int FrameRate;

        public QualityRow(QualityTier tier, float renderScale, int msaa, float cullRho, int gluedCopies, int frameRate)
        {
            Tier = tier;
            RenderScale = renderScale;
            Msaa = msaa;
            CullRho = cullRho;
            GluedCopies = gluedCopies;
            FrameRate = frameRate;
        }
    }

    /// <summary>The ladder of Master Plan section 8.6 as data.</summary>
    public static class QualityTable
    {
        /// <summary>Number of tiers, High included.</summary>
        public const int Count = 5;

        //                                                             scale  MSAA  rho   copies  fps
        static readonly QualityRow[] s_rows =
        {
            new QualityRow(QualityTier.Nominal,  0.8f, 4, 3.0f, 5, 60),
            new QualityRow(QualityTier.Fair,     0.7f, 4, 3.0f, 5, 60),   // thermal Fair, or GPU p95 over 9 ms
            new QualityRow(QualityTier.Serious,  0.6f, 2, 2.0f, 3, 60),   // thermal Serious, or GPU p95 over 12 ms
            new QualityRow(QualityTier.Critical, 0.6f, 1, 2.0f, 3, 30),   // thermal Critical, or still missing 60 fps
            new QualityRow(QualityTier.High,     0.8f, 4, 3.5f, 7, 60),   // cool phone, video capture
        };

        /// <summary>The row of a tier.</summary>
        public static QualityRow Row(QualityTier tier)
        {
            int index = (int)tier;
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(tier));
            return s_rows[index];
        }
    }
}
