using System;
using Atlas.Rendering;
using Atlas.World;
using WorldTier = Atlas.World.QualityTier;
using WorldRow = Atlas.World.QualityRow;
using WorldTable = Atlas.World.QualityTable;

namespace Atlas.App
{
    /// <summary>
    /// The quality ladder as the other lanes read it (Master Plan section 8.6): wraps the rendering
    /// assembly's applier, feeds it the perf probe's percentiles at step 8b, and publishes the tier in force
    /// as IQualityState. The applier itself swaps the eye textures and sets the frame rate.
    /// </summary>
    public sealed class QualityLadder : IQualityState, IAfterFrameStep
    {
        readonly QualityApplier _applier;

        public QualityLadder(QualityApplier applier)
        {
            if (applier == null) throw new ArgumentNullException(nameof(applier));
            _applier = applier;
            _applier.TierChanged += OnTierChanged;
        }

        /// <summary>The applier, for parameter binding.</summary>
        public QualityApplier Applier { get { return _applier; } }
        /// <summary>Frame-time percentiles; null until the perf probe is published (thermal state alone then).</summary>
        public IPerfSource Perf { get; set; }

        public WorldTier Tier { get { return (WorldTier)(byte)_applier.Tier; } }
        public WorldRow Row { get { return WorldTable.Row(Tier); } }
        public int ThermalState { get { return _applier.ThermalState; } }
        public int PinnedTier { get { return _applier.ForcedTier; } }
        public float RenderScale { get { return _applier.RenderScale; } }
        public int Msaa { get { return _applier.Msaa; } }

        public event Action<WorldTier> TierChanged;

        public void AfterFrame(in FrameContext frame)
        {
            IPerfSource perf = Perf;
            if (perf != null) _applier.SetPerf(perf.HasData, perf.GpuP95Ms10s, perf.FrameP95Ms10s);
            _applier.Tick(frame.Time);
        }

        void OnTierChanged(Atlas.World.QualityTier tier)
        {
            Action<WorldTier> changed = TierChanged;
            if (changed != null) changed((WorldTier)(byte)tier);
        }
    }
}
