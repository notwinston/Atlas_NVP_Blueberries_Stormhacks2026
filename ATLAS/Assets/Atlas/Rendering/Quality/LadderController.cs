using Atlas.World;

namespace Atlas.Rendering
{
    /// <summary>
    /// The stepping rule of the thermal quality ladder (Master Plan section 8.6, task P1-A07). Engine-free and
    /// clock-free: time comes in with the inputs. Automatic moves are one rung at a time and at most one per
    /// 20 s; stepping back up needs clearly better numbers than stepping down did (hysteresis). A forced tier
    /// applies on the evaluation that sees it.
    /// </summary>
    public sealed class LadderController
    {
        /// <summary>At most one automatic step in this many seconds.</summary>
        public const double DwellSeconds = 20.0;
        /// <summary>Nominal to Fair above this GPU p95. Milliseconds.</summary>
        public const float GpuToFairMs = 9f;
        /// <summary>Fair to Serious above this GPU p95. Milliseconds.</summary>
        public const float GpuToSeriousMs = 12f;
        /// <summary>Fair back to Nominal only below this: 9 ms at scale 0.8 is about 6.9 ms at 0.7.</summary>
        public const float GpuBackToNominalMs = 6.5f;
        /// <summary>Serious back to Fair only below this.</summary>
        public const float GpuBackToFairMs = 8f;
        /// <summary>"Still missing 60 fps": frame p95 above one 60 Hz interval plus 0.5 ms.</summary>
        public const float FrameMissMs = 17.2f;

        QualityTier _tier = QualityTier.Nominal;
        double _lastStepTime = double.NegativeInfinity;
        bool _forced;

        /// <summary>The tier to apply.</summary>
        public QualityTier Tier => _tier;
        /// <summary>True while a forced tier is in effect.</summary>
        public bool IsForced => _forced;

        /// <summary>Back to a tier (a new session), with the 20 s spacing cleared.</summary>
        public void Reset(QualityTier tier = QualityTier.Nominal)
        {
            _tier = tier;
            _lastStepTime = double.NegativeInfinity;
            _forced = false;
        }

        /// <summary>Evaluates the rule once. True when <see cref="Tier"/> changed.</summary>
        public bool Step(in QualityInputs inputs)
        {
            QualityTier before = _tier;

            if (inputs.PinTier >= 0)
            {
                // A forced tier ignores the spacing: the Console must see it within a second.
                int forced = inputs.PinTier < QualityTable.Count ? inputs.PinTier : QualityTable.Count - 1;
                _tier = (QualityTier)forced;
                _forced = true;
                return _tier != before;
            }

            if (_forced)
            {
                // Leaving a forced tier: carry on from that rung (High is not on the automatic ladder), and
                // let it stand for a full spacing before the first automatic move.
                _forced = false;
                if (_tier == QualityTier.High)
                    _tier = QualityTier.Nominal;
                _lastStepTime = inputs.Time;
                return _tier != before;
            }

            if (inputs.Time - _lastStepTime < DwellSeconds)
                return false;

            QualityTier next = Next(_tier, in inputs);
            if (next == _tier)
                return false;
            _tier = next;
            _lastStepTime = inputs.Time;
            return true;
        }

        // One rung at most. A reason to step down is tested before a reason to step up.
        static QualityTier Next(QualityTier tier, in QualityInputs i)
        {
            int thermal = i.ThermalState;
            bool perf = i.PerfValid;
            switch (tier)
            {
                case QualityTier.Nominal:
                    if (thermal >= 1 || (perf && i.GpuP95Ms > GpuToFairMs)) return QualityTier.Fair;
                    return tier;
                case QualityTier.Fair:
                    if (thermal >= 2 || (perf && i.GpuP95Ms > GpuToSeriousMs)) return QualityTier.Serious;
                    if (thermal <= 0 && perf && i.GpuP95Ms < GpuBackToNominalMs) return QualityTier.Nominal;
                    return tier;
                case QualityTier.Serious:
                    if (thermal >= 3 || (perf && i.FrameP95Ms > FrameMissMs)) return QualityTier.Critical;
                    if (thermal <= 1 && perf && i.GpuP95Ms < GpuBackToFairMs) return QualityTier.Fair;
                    return tier;
                default:
                    // Critical ends the session: it is left only by a forced tier or a Reset.
                    return tier;
            }
        }
    }
}
