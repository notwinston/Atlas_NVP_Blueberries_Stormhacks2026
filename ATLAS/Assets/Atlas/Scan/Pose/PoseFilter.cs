using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// The engine-free core of the pose pipeline's step 3 (Master Plan sections 6.3 and 4.6, decision D8).
    /// It takes the raw head pose once per frame and gives the geometry stream: the same pose, plus the floor
    /// displacement with pose jumps dropped, plus the tracking-lost timer.
    /// </summary>
    public sealed class PoseFilter
    {
        /// <summary>Metres. A floor displacement longer than this in one frame is a relocalisation, not a step (D8).</summary>
        public const float JumpDistance = 0.20f;
        /// <summary>Seconds of tracking that is not Normal before tracking counts as lost (task P1-C11).</summary>
        public const float TrackingLostSeconds = 0.5f;

        // Sixty additions of 1/60 s do not sum to exactly 0.5 s; this keeps the event on the intended frame.
        const double TimerSlack = 1e-4;

        float2 _previousFloor;
        bool _havePrevious;
        double _notNormalSeconds;

        /// <summary>The pose a space reads: the raw pose of the frame.</summary>
        public HeadPose Geometry { get; private set; } = HeadPose.Identity;
        /// <summary>Metres (x, z): this frame's floor displacement, zero when rejected.</summary>
        public float2 Step { get; private set; }
        /// <summary>Metres (x, z): the raw floor point at the last reset plus every Step since.</summary>
        public float2 PathFloor { get; private set; }
        /// <summary>A displacement over JumpDistance was dropped this frame.</summary>
        public bool JumpRejected { get; private set; }
        /// <summary>Metres: the length of the dropped jump; 0 when none was dropped this frame.</summary>
        public float JumpLength { get; private set; }
        /// <summary>Jumps dropped since construction.</summary>
        public int JumpCount { get; private set; }
        /// <summary>Tracking has not been Normal for TrackingLostSeconds or more.</summary>
        public bool TrackingLost { get; private set; }
        /// <summary>TrackingLost became true in the last Update.</summary>
        public bool LostThisFrame { get; private set; }
        /// <summary>TrackingLost became false in the last Update.</summary>
        public bool RegainedThisFrame { get; private set; }
        /// <summary>Seconds tracking has been other than Normal; 0 while it is Normal.</summary>
        public double NotNormalSeconds { get { return _notNormalSeconds; } }

        /// <summary>
        /// The recentre entry point. The next Update takes its raw floor point as the new PathFloor and gives a
        /// zero Step. The tracking-lost timer is not touched.
        /// </summary>
        public void Reset()
        {
            _havePrevious = false;
            Step = float2.zero;
            JumpRejected = false;
            JumpLength = 0f;
        }

        /// <summary>One frame. deltaTime is the time since the previous frame, seconds.</summary>
        public void Update(in HeadPose raw, float deltaTime)
        {
            float2 floor = raw.Floor;
            bool normal = raw.Tracking == TrackingState.Normal;

            float2 d = float2.zero;
            if (_havePrevious) d = floor - _previousFloor;
            else if (math.all(math.isfinite(floor))) PathFloor = floor;

            float length = math.length(d);
            bool finite = math.isfinite(length);
            bool jump = finite && length > JumpDistance;

            Step = (jump || !normal || !finite) ? float2.zero : d;
            PathFloor += Step;
            JumpRejected = jump;
            JumpLength = jump ? length : 0f;
            if (jump) JumpCount++;

            // A sample that is not a number never becomes the reference: the next good one is measured from
            // the last good one.
            if (math.all(math.isfinite(floor)))
            {
                _previousFloor = floor;
                _havePrevious = true;
            }

            Geometry = raw;

            LostThisFrame = false;
            RegainedThisFrame = false;
            if (normal)
            {
                _notNormalSeconds = 0.0;
                if (TrackingLost)
                {
                    TrackingLost = false;
                    RegainedThisFrame = true;
                }
            }
            else
            {
                _notNormalSeconds += deltaTime > 0f ? deltaTime : 0f;
                if (!TrackingLost && _notNormalSeconds >= TrackingLostSeconds - TimerSlack)
                {
                    TrackingLost = true;
                    LostThisFrame = true;
                }
            }
        }
    }
}
