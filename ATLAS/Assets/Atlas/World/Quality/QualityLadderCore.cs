namespace Atlas.World
{
    /// <summary>What the ladder rule looks at, sampled once per second.</summary>
    public struct QualityInputs
    {
        /// <summary>Seconds, FrameContext.Time.</summary>
        public double Time;
        /// <summary>0 to 3, polled every 2 s.</summary>
        public int ThermalState;
        /// <summary>A perf source is present and has data.</summary>
        public bool PerfValid;
        /// <summary>IPerfSource.GpuP95Ms10s.</summary>
        public float GpuP95Ms;
        /// <summary>IPerfSource.FrameP95Ms10s.</summary>
        public float FrameP95Ms;
        /// <summary>thermal.pinTier: -1 automatic, 0 to 4.</summary>
        public int PinTier;
    }

    /// <summary>
    /// The stepping rule of the thermal ladder (Master Plan section 8.6). Engine-free: time comes in with the
    /// inputs. A pin applies at once; otherwise the ladder moves one rung at most every 20 s, a worse rung
    /// being tested first.
    /// </summary>
    public sealed class QualityLadderCore
    {
        /// <summary>At most one automatic step per 20 s (section 8.6).</summary>
        public const double DwellSeconds = 20.0;
        public const double ThermalPollSeconds = 2.0;
        public const float GpuToFairMs = 9f;
        public const float GpuToSeriousMs = 12f;
        /// <summary>Hysteresis: step back up only well below the threshold stepped down at.</summary>
        public const float GpuBackToNominalMs = 6.5f;
        public const float GpuBackToFairMs = 8f;
        /// <summary>One vsync interval + 0.5 ms (section 11.2): "still missing 60 fps".</summary>
        public const float FrameMissMs = 17.2f;

        QualityTier _tier = QualityTier.Nominal;
        double _lastAutoStep = double.NegativeInfinity;
        bool _pinned;

        public QualityTier Tier { get { return _tier; } }

        /// <summary>Evaluates the rule. True when Tier changed.</summary>
        public bool Step(in QualityInputs inputs)
        {
            QualityTier before = _tier;
            if (inputs.PinTier >= 0)
            {
                _tier = (QualityTier)(inputs.PinTier > (int)QualityTier.High ? (int)QualityTier.High : inputs.PinTier);
                _pinned = true;
                return _tier != before;
            }

            if (_pinned)
            {
                // Leaving a pin: carry on from the pinned rung, or from Nominal after High, and give the
                // operator's choice a full dwell before the ladder moves by itself again.
                _pinned = false;
                if (_tier == QualityTier.High) _tier = QualityTier.Nominal;
                _lastAutoStep = inputs.Time;
                return _tier != before;
            }

            if (inputs.Time - _lastAutoStep < DwellSeconds) return false;

            int thermal = inputs.ThermalState;
            bool perf = inputs.PerfValid;
            QualityTier next = _tier;
            switch (_tier)
            {
                case QualityTier.Nominal:
                    if (thermal >= 1 || (perf && inputs.GpuP95Ms > GpuToFairMs)) next = QualityTier.Fair;
                    break;
                case QualityTier.Fair:
                    if (thermal >= 2 || (perf && inputs.GpuP95Ms > GpuToSeriousMs)) next = QualityTier.Serious;
                    else if (thermal == 0 && (!perf || inputs.GpuP95Ms < GpuBackToNominalMs)) next = QualityTier.Nominal;
                    break;
                case QualityTier.Serious:
                    if (thermal >= 3 || (perf && inputs.FrameP95Ms > FrameMissMs)) next = QualityTier.Critical;
                    else if (thermal <= 1 && (!perf || inputs.GpuP95Ms < GpuBackToFairMs)) next = QualityTier.Fair;
                    break;
                // Critical is left only by a pin or by Reset at a new session. High is only ever pinned.
            }

            if (next == _tier) return false;
            _tier = next;
            _lastAutoStep = inputs.Time;
            return true;
        }

        /// <summary>Back to a known rung with no pin and no dwell pending.</summary>
        public void Reset(QualityTier tier = QualityTier.Nominal)
        {
            _tier = tier;
            _pinned = false;
            _lastAutoStep = double.NegativeInfinity;
        }
    }
}
