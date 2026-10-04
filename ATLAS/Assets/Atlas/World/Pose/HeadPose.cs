using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>One sample of the head: the pose of "Atlas Head", the midpoint between the eyes.</summary>
    public readonly struct HeadPose
    {
        // Within 6 degrees of straight up or down the forward vector has almost no floor component.
        const float SteepSine = 0.10452846f;   // sin(6 degrees)

        /// <summary>Seconds on the app clock: FrameContext.Time of the frame the sample was read in.</summary>
        public readonly double Time;
        /// <summary>Metres. Room frame when a room is active, otherwise AR session frame.</summary>
        public readonly float3 Position;
        public readonly quaternion Rotation;
        public readonly TrackingState Tracking;

        public HeadPose(double time, float3 position, quaternion rotation, TrackingState tracking)
        {
            Time = time;
            Position = position;
            Rotation = rotation;
            Tracking = tracking;
        }

        /// <summary>A head at the origin, unturned, not tracked.</summary>
        public static HeadPose Identity
        {
            get { return new HeadPose(0.0, float3.zero, quaternion.identity, TrackingState.NotTracking); }
        }

        /// <summary>Position.xz.</summary>
        public float2 Floor { get { return Position.xz; } }

        /// <summary>Position.y: metres above the floor in the room frame.</summary>
        public float Height { get { return Position.y; } }

        /// <summary>Rotation * (0, 0, 1).</summary>
        public float3 Forward { get { return math.mul(Rotation, new float3(0f, 0f, 1f)); } }

        /// <summary>
        /// Forward projected on the floor, unit length. When the head looks within 6 degrees of straight up or
        /// down, the projection of the head's up vector is used instead (negated when looking up).
        /// </summary>
        public float2 Heading
        {
            get
            {
                float3 f = Forward;
                float2 h = f.xz;
                if (math.lengthsq(h) < SteepSine * SteepSine)
                {
                    float3 up = math.mul(Rotation, new float3(0f, 1f, 0f));
                    h = f.y > 0f ? -up.xz : up.xz;
                }
                float len = math.length(h);
                // A degenerate rotation (all zeros) has no heading; +Z keeps callers finite.
                return len > 1e-6f ? h / len : new float2(0f, 1f);
            }
        }

        /// <summary>atan2(Heading.x, Heading.y): radians, Unity's sense (positive turns +Z towards +X).</summary>
        public float Yaw
        {
            get
            {
                float2 h = Heading;
                return math.atan2(h.x, h.y);
            }
        }
    }
}
