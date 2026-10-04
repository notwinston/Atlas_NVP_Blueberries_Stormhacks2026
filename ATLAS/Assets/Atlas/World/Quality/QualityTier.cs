using System;

namespace Atlas.World
{
    /// <summary>Rungs of the thermal ladder (Master Plan section 8.6). 0 to 3 are OperatorCmd quality tiers.</summary>
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
        /// <summary>Scale of the eye-texture set.</summary>
        public readonly float RenderScale;
        /// <summary>4, 2 or 1.</summary>
        public readonly int Msaa;
        /// <summary>Fog radius rho, kernel units.</summary>
        public readonly float CullRho;
        /// <summary>Copies per side of the Glued lattice.</summary>
        public readonly int GluedCopies;
        /// <summary>60 or 30.</summary>
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

    /// <summary>The ladder of Master Plan section 8.6 as a table.</summary>
    public static class QualityTable
    {
        public const int Count = 5;

        static readonly QualityRow[] s_rows =
        {
            new QualityRow(QualityTier.Nominal, 0.8f, 4, 3.0f, 5, 60),
            new QualityRow(QualityTier.Fair, 0.7f, 4, 3.0f, 5, 60),
            new QualityRow(QualityTier.Serious, 0.6f, 2, 2.0f, 3, 60),
            new QualityRow(QualityTier.Critical, 0.6f, 1, 2.0f, 3, 30),
            new QualityRow(QualityTier.High, 0.8f, 4, 3.5f, 7, 60),
        };

        /// <summary>The row of a tier. An undefined value gives the Nominal row.</summary>
        public static QualityRow Row(QualityTier tier)
        {
            int index = (int)tier;
            if (index < 0 || index >= Count)
                throw new System.ArgumentOutOfRangeException(nameof(tier));
            return s_rows[index];
        }

        /// <summary>Bits 4 to 5 of State.flags: 0 to 3; High is sent as 0 because it shares the Nominal texture set.</summary>
        public static byte ToWire(QualityTier tier)
        {
            return tier == QualityTier.High || (int)tier >= Count ? (byte)0 : (byte)tier;
        }
    }

    /// <summary>The ladder as every lane reads it.</summary>
    public interface IQualityState
    {
        QualityTier Tier { get; }
        QualityRow Row { get; }
        /// <summary>NSProcessInfo.thermalState: 0 nominal, 1 fair, 2 serious, 3 critical. 0 off the device.</summary>
        int ThermalState { get; }
        /// <summary>-1 while the ladder is automatic, else the pinned tier.</summary>
        int PinnedTier { get; }
        /// <summary>Scale of the active eye-texture set.</summary>
        float RenderScale { get; }
        /// <summary>MSAA of the active eye-texture set.</summary>
        int Msaa { get; }
        /// <summary>Raised at step 8b when the tier changes.</summary>
        event Action<QualityTier> TierChanged;
    }

    /// <summary>GPU and frame timings the ladder needs. Implemented by the perf probe.</summary>
    public interface IPerfSource
    {
        /// <summary>False until 120 frames have been sampled.</summary>
        bool HasData { get; }
        /// <summary>GPU frame time p95 over the last 600 frames. Milliseconds. Refreshed once per second.</summary>
        float GpuP95Ms10s { get; }
        /// <summary>Frame time p95 over the last 600 frames. Milliseconds. Refreshed once per second.</summary>
        float FrameP95Ms10s { get; }
    }
}
