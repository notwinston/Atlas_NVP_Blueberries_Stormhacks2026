using Atlas.Safety;
using Atlas.World;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// Whether the walker stands still: head speed on the floor at or below 0.1 m/s with Normal tracking
    /// (Master Plan sections 4.6 and 10.1.4). It feeds the standing lock of the dial and the stillness gate of
    /// the 3-4-5 walk, and measures how long the head has been still. Engine-free.
    /// </summary>
    public sealed class HeadStillness
    {
        /// <summary>Metres per second: above this the walker is moving (Master Plan section 4.6).</summary>
        public const float MaxSpeed = 0.1f;

        HeadVelocityFilter m_Filter;
        double m_StillSince = double.NaN;

        /// <summary>Head speed on the floor, m/s, averaged over the last 0.2 s. Infinity without Normal tracking.</summary>
        public float Speed { get; private set; } = float.PositiveInfinity;
        /// <summary>Normal tracking and Speed at most <see cref="MaxSpeed"/>.</summary>
        public bool Standing { get; private set; }
        /// <summary>Seconds since the walker last started to move or tracking last failed; 0 while moving.</summary>
        public double StillSeconds { get; private set; }

        /// <summary>Feeds one head pose per frame, with the pose's own time.</summary>
        public void Update(in HeadPose pose)
        {
            if (pose.Tracking != TrackingState.Normal)
            {
                m_Filter.Reset();
                Speed = float.PositiveInfinity;
                Standing = false;
                m_StillSince = double.NaN;
                StillSeconds = 0.0;
                return;
            }

            m_Filter.Update(pose.Floor, pose.Time);
            // A pose jump zeroes the velocity; it is not a standing walker.
            Speed = m_Filter.Jumped ? float.PositiveInfinity : Unity.Mathematics.math.length(m_Filter.Velocity);
            Standing = Speed <= MaxSpeed;
            if (!Standing)
            {
                m_StillSince = double.NaN;
                StillSeconds = 0.0;
                return;
            }
            if (double.IsNaN(m_StillSince)) m_StillSince = pose.Time;
            StillSeconds = pose.Time - m_StillSince;
        }

        /// <summary>Forgets the history: the next pose only takes the position.</summary>
        public void Reset()
        {
            m_Filter.Reset();
            Speed = float.PositiveInfinity;
            Standing = false;
            m_StillSince = double.NaN;
            StillSeconds = 0.0;
        }
    }
}
