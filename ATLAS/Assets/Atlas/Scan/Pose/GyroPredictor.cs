using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// Rotation prediction for the eyes (Master Plan section 8.7, decision D8): the head's turn rate, taken
    /// from the last two raw rotations, carried forward by a lead time. Off unless a lead time is set. The
    /// kernel and Bump Guard never see the result. Engine-free.
    /// </summary>
    public sealed class GyroPredictor
    {
        /// <summary>Milliseconds: the longest lead accepted (section 8.7 gives 30 to 60).</summary>
        public const float MaxLeadMs = 60f;
        /// <summary>Radians: a predicted turn is never larger than this (about 12 degrees).</summary>
        public const float MaxAngle = 0.21f;

        quaternion _previous = quaternion.identity;
        bool _havePrevious;

        /// <summary>The rotation to add to the eyes, in head space; identity when off.</summary>
        public quaternion Prediction { get; private set; } = quaternion.identity;

        public void Reset()
        {
            _havePrevious = false;
            Prediction = quaternion.identity;
        }

        /// <summary>
        /// One frame. leadMs is 0 (off) to MaxLeadMs. usable is false when the sample must not be trusted
        /// (tracking not Normal, a pose jump): the prediction is then the identity.
        /// </summary>
        public void Update(quaternion rawRotation, float deltaTime, float leadMs, bool usable)
        {
            quaternion previous = _previous;
            bool havePrevious = _havePrevious;
            _previous = rawRotation;
            _havePrevious = true;

            Prediction = quaternion.identity;
            if (!(leadMs > 0f) || !usable || !havePrevious || !(deltaTime > 1e-4f)) return;

            // The turn of the last frame, expressed in head space.
            quaternion delta = math.mul(math.inverse(previous), rawRotation);
            float4 v = delta.value;
            if (v.w < 0f) v = -v;                       // the short way round
            float sinHalf = math.length(v.xyz);
            if (!(sinHalf > 1e-6f)) return;
            float angle = 2f * math.atan2(sinHalf, v.w);
            float lead = math.min(leadMs, MaxLeadMs) * 0.001f;
            float predicted = math.min(angle * (lead / deltaTime), MaxAngle);
            if (!math.isfinite(predicted)) return;
            Prediction = quaternion.AxisAngle(v.xyz / sinHalf, predicted);
        }
    }
}
