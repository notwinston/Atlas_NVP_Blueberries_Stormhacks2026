using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The gaze twin of the shadow verb (Master Plan section 10.1.3: "Shadow: X, or look down past -35 degrees"; task P2-A02):
    /// while the walker looks down past <see cref="OnPitchDeg"/> and stands still, the shadow shows; looking back up past
    /// <see cref="OffPitchDeg"/>, or walking off, hides it again. Standing still is required because a walker on the w lane
    /// looks at the floor all the time: walking never peeks. The chip and the X button still toggle the shadow for good.
    /// Engine-free; one step per frame; no allocation.
    /// </summary>
    public sealed class LookDownShadow
    {
        /// <summary>Head pitch below the horizon that shows the shadow, and the one above which it hides again (2 degrees of
        /// hysteresis either side of the plan's -35).</summary>
        public const float OnPitchDeg = -37f, OffPitchDeg = -33f;
        /// <summary>Head speed under which the walker stands still, metres per second, and how long they must.</summary>
        public const float StillSpeed = 0.15f, StillSeconds = 0.5f;
        /// <summary>Head speed above which a peek ends at once.</summary>
        public const float WalkSpeed = 0.3f;
        /// <summary>Time constant of the head-speed filter, seconds.</summary>
        public const float SpeedSmoothing = 0.15f;

        float3 m_Last;
        bool m_HasLast;
        float m_Speed;
        float m_Still;

        /// <summary>True while the shadow is shown because the walker looks down.</summary>
        public bool Peeking { get; private set; }
        /// <summary>The filtered head speed, metres per second.</summary>
        public float Speed { get { return m_Speed; } }

        /// <summary>Forgets the head: the next step starts afresh, not peeking.</summary>
        public void Reset()
        {
            m_HasLast = false;
            m_Speed = 0f;
            m_Still = 0f;
            Peeking = false;
        }

        /// <summary>One frame. <paramref name="enabled"/> false (the kill switch) ends any peek. True when Peeking changed.</summary>
        public bool Step(float3 head, quaternion rotation, float deltaTime, bool enabled = true)
        {
            bool before = Peeking;
            float dt = math.clamp(deltaTime, 0f, 0.1f);
            if (m_HasLast && dt > 0f)
            {
                float v = math.length((head - m_Last).xz) / dt;
                float k = 1f - math.exp(-dt / SpeedSmoothing);
                m_Speed += (v - m_Speed) * k;
            }
            m_Last = head;
            m_HasLast = true;
            m_Still = m_Speed < StillSpeed ? m_Still + dt : 0f;

            float pitch = PitchDegrees(rotation);
            if (!enabled || m_Speed > WalkSpeed || pitch > OffPitchDeg) Peeking = false;
            else if (!Peeking && pitch < OnPitchDeg && m_Still >= StillSeconds) Peeking = true;
            return Peeking != before;
        }

        /// <summary>Pitch of the view direction above the horizon, degrees: -90 looking straight down.</summary>
        public static float PitchDegrees(quaternion rotation)
        {
            float3 forward = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.degrees(math.asin(math.clamp(forward.y, -1f, 1f)));
        }
    }
}
