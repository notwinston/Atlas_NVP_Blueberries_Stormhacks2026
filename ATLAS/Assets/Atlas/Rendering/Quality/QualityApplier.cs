using System;
using Atlas.World;

namespace Atlas.Rendering
{
    /// <summary>
    /// Runs the quality ladder and puts its tier into force (Master Plan section 8.6, task P1-A07): swaps the
    /// eye-texture set, sets the frame rate of the rung, and publishes the fog radius and the Glued copies as
    /// plain properties that the kernel and the Glued space read. Polls the thermal state every 2 s and
    /// evaluates the ladder once per second, or at once when the forced tier changes.
    /// </summary>
    public sealed class QualityApplier
    {
        /// <summary>The thermal plugin is read this often. Seconds.</summary>
        public const double ThermalPollSeconds = 2.0;
        /// <summary>The ladder is evaluated this often, so a forced tier is in force within a second.</summary>
        public const double EvaluateSeconds = 1.0;

        readonly LadderController _ladder = new LadderController();
        readonly IEyeTextures _eyes;

        QualityRow _row;
        int _thermal;
        int _forcedTier = -1;
        bool _forcedDirty;
        bool _perfValid;
        float _gpuP95Ms;
        float _frameP95Ms;
        double _lastPoll = double.NegativeInfinity;
        double _lastEvaluate = double.NegativeInfinity;

        /// <summary>eyes may be null where there is no stereo rig (the Spectator). Starts on Nominal.</summary>
        public QualityApplier(IEyeTextures eyes)
        {
            _eyes = eyes;
            Put(QualityTier.Nominal);
        }

        /// <summary>The tier in force.</summary>
        public QualityTier Tier => _row.Tier;
        /// <summary>The table row of the tier in force.</summary>
        public QualityRow Row => _row;
        /// <summary>The stepping rule, for inspection.</summary>
        public LadderController Ladder => _ladder;

        /// <summary>Thermal state as last polled: 0 nominal, 1 fair, 2 serious, 3 critical. 0 off the device.</summary>
        public int ThermalState => _thermal;
        /// <summary>-1 reads the device. 0 .. 3 replaces the device's thermal state: for rehearsal in the Editor.</summary>
        public int ThermalOverride { get; set; } = -1;

        /// <summary>-1: the ladder is automatic. 0 .. 4: this tier is forced, in force within one second.</summary>
        public int ForcedTier
        {
            get => _forcedTier;
            set
            {
                int clamped = value < 0 ? -1 : (value >= QualityTable.Count ? QualityTable.Count - 1 : value);
                if (clamped == _forcedTier)
                    return;
                _forcedTier = clamped;
                _forcedDirty = true;
            }
        }

        /// <summary>Render scale of the eye textures in force.</summary>
        public float RenderScale => _eyes != null ? _eyes.RenderScale : _row.RenderScale;
        /// <summary>MSAA sample count of the eye textures in force.</summary>
        public int Msaa => _eyes != null ? _eyes.Msaa : _row.Msaa;
        /// <summary>Fog radius rho of the tier. Kernel units. May be set by hand between two tier changes.</summary>
        public float CullRho { get; set; }
        /// <summary>Glued copies per side of the tier. May be set by hand between two tier changes.</summary>
        public int GluedCopies { get; set; }
        /// <summary>Frames per second of the tier: 60, or 30 on Critical.</summary>
        public int FrameRate => _row.FrameRate;

        /// <summary>Raised after a new tier has been put into force.</summary>
        public event Action<QualityTier> TierChanged;

        /// <summary>
        /// The frame-time percentiles of the last 10 s, from the perf probe. valid is false until the probe has
        /// enough frames; the ladder then follows the thermal state alone.
        /// </summary>
        public void SetPerf(bool valid, float gpuP95Ms, float frameP95Ms)
        {
            _perfValid = valid;
            _gpuP95Ms = gpuP95Ms;
            _frameP95Ms = frameP95Ms;
        }

        /// <summary>
        /// Call once per frame, after the frame has been rendered, with the app clock in seconds. Allocates
        /// nothing: a change of tier swaps to textures that already exist and writes a few numbers.
        /// </summary>
        public void Tick(double timeSeconds)
        {
            if (timeSeconds - _lastPoll >= ThermalPollSeconds)
            {
                _lastPoll = timeSeconds;
                int state = ThermalOverride >= 0 ? ThermalOverride : AtlasThermal.State;
                _thermal = state < 0 ? 0 : (state > 3 ? 3 : state);
            }

            if (!_forcedDirty && timeSeconds - _lastEvaluate < EvaluateSeconds)
                return;
            _lastEvaluate = timeSeconds;
            _forcedDirty = false;

            var inputs = new QualityInputs
            {
                Time = timeSeconds,
                ThermalState = _thermal,
                PerfValid = _perfValid,
                GpuP95Ms = _gpuP95Ms,
                FrameP95Ms = _frameP95Ms,
                PinTier = _forcedTier,
            };
            if (_ladder.Step(in inputs))
                Apply(_ladder.Tier);
        }

        /// <summary>A new session: back to Nominal, automatic unless a tier is still forced.</summary>
        public void Reset()
        {
            _ladder.Reset();
            _lastEvaluate = double.NegativeInfinity;
            Apply(QualityTier.Nominal);
        }

        /// <summary>Puts a tier into force now. The ladder calls this; the next evaluation may change it again.</summary>
        public void Apply(QualityTier tier)
        {
            bool changed = tier != _row.Tier;
            Put(tier);
            if (changed)
                TierChanged?.Invoke(tier);
        }

        void Put(QualityTier tier)
        {
            _row = QualityTable.Row(tier);
            CullRho = _row.CullRho;
            GluedCopies = _row.GluedCopies;
            FramePacing.TargetFrameRate = _row.FrameRate;
            if (_eyes != null)
                _eyes.Select(_row.RenderScale, _row.Msaa);
        }
    }
}
