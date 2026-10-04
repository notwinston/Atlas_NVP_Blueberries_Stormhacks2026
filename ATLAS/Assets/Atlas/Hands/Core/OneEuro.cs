using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The One-Euro filter (Casiez, Roussel and Vogel, "1 Euro Filter", CHI 2012): a low-pass whose cut-off
    /// rises with the speed of the signal, so that a still signal is smooth and a fast one hardly lags. The
    /// cut-off is <see cref="MinCutoff"/> plus <see cref="Beta"/> times the speed, and the speed is the
    /// derivative of the input, itself low-passed at <see cref="DerivativeCutoff"/>. It filters one scalar or one
    /// float3 (the speed of a float3 is the length of its derivative, so every axis shares one cut-off).
    ///
    /// Time is passed in, in seconds on any clock that only goes forward, and the sampling may be uneven: the
    /// smoothing factor follows the time between samples. The derivative is taken between raw samples, so for
    /// a signal at constant speed it is that speed, without the bias the filter's own lag would add; hands
    /// predict from it. Plain data and no allocation. Set the three numbers before the first sample (a default
    /// instance has no cut-off and holds its first sample for ever).
    /// </summary>
    public struct OneEuro
    {
        /// <summary>The cut-off of a signal at rest, Hz. Lower removes more jitter and adds more lag.</summary>
        public float MinCutoff;
        /// <summary>How fast the cut-off rises with speed, Hz per unit per second (Hz per m/s for a position).</summary>
        public float Beta;
        /// <summary>The cut-off of the low-pass on the derivative, Hz.</summary>
        public float DerivativeCutoff;

        bool _started;
        double _time;
        float3 _raw;
        float3 _value;
        float3 _derivative;

        /// <summary>A filter with the given settings (see the fields), waiting for its first sample.</summary>
        public OneEuro(float minCutoff, float beta, float derivativeCutoff)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
            _started = false;
            _time = 0.0;
            _raw = float3.zero;
            _value = float3.zero;
            _derivative = float3.zero;
        }

        /// <summary>True once a sample has been taken since the last <see cref="Reset"/>.</summary>
        public bool Started { get { return _started; } }

        /// <summary>The last filtered value.</summary>
        public float3 Value { get { return _value; } }

        /// <summary>The filtered derivative of the input, units per second. Zero until the second sample.</summary>
        public float3 Derivative { get { return _derivative; } }

        /// <summary>The time of the last sample taken.</summary>
        public double Time { get { return _time; } }

        /// <summary>Forgets every sample and keeps the three settings. The next sample passes through unchanged.</summary>
        public void Reset()
        {
            _started = false;
            _time = 0.0;
            _raw = float3.zero;
            _value = float3.zero;
            _derivative = float3.zero;
        }

        /// <summary>
        /// The smoothing factor of a first-order low-pass with the given cut-off (Hz) for samples dt seconds
        /// apart: 0 holds the old value, 1 takes the new one.
        /// </summary>
        public static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * math.PI * math.max(cutoff, 1e-6f));
            float step = math.max(dt, 0f);
            return step / (step + tau);
        }

        /// <summary>
        /// Takes one sample and returns the filtered value. A value that is not finite, a time that is not a
        /// number and a time that has not moved on from the last sample change nothing.
        /// </summary>
        public float3 Filter(float3 value, double time)
        {
            if (!math.all(math.isfinite(value)) || double.IsNaN(time) || double.IsInfinity(time)) return _value;
            if (!_started)
            {
                _started = true;
                _time = time;
                _raw = value;
                _value = value;
                _derivative = float3.zero;
                return _value;
            }
            double elapsed = time - _time;
            if (!(elapsed > 0.0)) return _value;

            float dt = (float)elapsed;
            float3 rate = (value - _raw) / dt;
            _derivative += Alpha(DerivativeCutoff, dt) * (rate - _derivative);
            float cutoff = MinCutoff + Beta * math.length(_derivative);
            _value += Alpha(cutoff, dt) * (value - _value);
            _raw = value;
            _time = time;
            return _value;
        }

        /// <summary>The same filter for one number. Do not mix it with the float3 form on one instance.</summary>
        public float Filter(float value, double time)
        {
            return Filter(new float3(value, 0f, 0f), time).x;
        }
    }

    /// <summary>
    /// The One-Euro filter for an orientation: the same cut-off rule on the angular speed (radians per second),
    /// and the smoothing is a spherical interpolation from the filtered orientation towards each new sample, by
    /// the short way round. <see cref="Beta"/> is in Hz per radian per second.
    /// </summary>
    internal struct OneEuroRotation
    {
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;

        bool _started;
        double _time;
        quaternion _raw;
        quaternion _value;
        float _speed;

        public quaternion Value { get { return _value; } }

        /// <summary>The filtered angular speed, radians per second.</summary>
        public float AngularSpeed { get { return _speed; } }

        public void Reset()
        {
            _started = false;
            _time = 0.0;
            _raw = quaternion.identity;
            _value = quaternion.identity;
            _speed = 0f;
        }

        public quaternion Filter(quaternion sample, double time)
        {
            float length = math.length(sample.value);
            if (!(length > 1e-6f) || float.IsInfinity(length) || double.IsNaN(time) || double.IsInfinity(time)) return _value;
            quaternion q = new quaternion(sample.value / length);
            if (!_started)
            {
                _started = true;
                _time = time;
                _raw = q;
                _value = q;
                _speed = 0f;
                return _value;
            }
            double elapsed = time - _time;
            if (!(elapsed > 0.0)) return _value;

            float dt = (float)elapsed;
            // The turn from the last sample to this one, as an angle: twice the angle whose sine is the length
            // of its vector part (the absolute value of w makes q and -q the same rotation).
            float4 turn = math.mul(q, math.conjugate(_raw)).value;
            float angle = 2f * math.atan2(math.length(turn.xyz), math.abs(turn.w));
            _speed += OneEuro.Alpha(DerivativeCutoff, dt) * (angle / dt - _speed);
            float cutoff = MinCutoff + Beta * _speed;
            _value = math.normalize(math.slerp(_value, q, OneEuro.Alpha(cutoff, dt)));
            _raw = q;
            _time = time;
            return _value;
        }
    }
}
