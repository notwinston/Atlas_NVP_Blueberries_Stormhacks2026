using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App
{
    /// <summary>
    /// A head that walks by itself, for a performance run with nobody in the headset and no recorded walk at
    /// hand (Master Plan section 11.4.1; contract README F9): laps of an ellipse inside the free rectangle
    /// at walking speed, looking along the path while the gaze sweeps left and right and up and down, so the
    /// view covers what a walker's would. In standing mode it turns on the spot. The same run gives the same
    /// poses on every device. Engine-free; poses are in the room frame.
    /// </summary>
    public sealed class SyntheticPoseTrack : IPoseSource
    {
        public const string SourceName = "synthetic";
        public const float EyeHeight = 1.6f;
        /// <summary>Metres per second along the lap.</summary>
        public const float Speed = 0.8f;
        /// <summary>Metres the lap stays inside the free rectangle.</summary>
        public const float Inset = 0.3f;
        /// <summary>Under this half-axis there is no lap to walk: the head turns on the spot.</summary>
        public const float MinHalfAxis = 0.2f;
        /// <summary>Radians per second when turning on the spot.</summary>
        public const float SpotTurnRate = 0.5f;

        const float GazeYaw = 0.6f, GazeYawPeriod = 7f;
        const float GazePitchMean = -0.15f, GazePitch = 0.25f, GazePitchPeriod = 11f;

        float _a, _b;
        float _angle;
        float _time;
        float _hold;

        public string Name { get { return SourceName; } }
        public PoseFrameKind Frame { get { return PoseFrameKind.Room; } }
        public bool DrivesRig { get { return false; } }

        /// <summary>Half-axes of the lap along X and Z, metres; both 0 when turning on the spot.</summary>
        public float2 HalfAxes { get { return new float2(_a, _b); } }
        /// <summary>Seconds walked since <see cref="SetRoom"/>.</summary>
        public float Time { get { return _time; } }

        /// <summary>Fits the lap to a room's free rectangle and starts again. Null stands at the origin.</summary>
        public void SetRoom(RoomModel room)
        {
            float wf = room != null ? room.FreeRect.Wf : 0f;
            float df = room != null ? room.FreeRect.Df : 0f;
            SetFreeRect(wf, df, room != null && room.Standing);
        }

        public void SetFreeRect(float wf, float df, bool standing)
        {
            _a = math.max(0f, 0.5f * wf - Inset);
            _b = math.max(0f, 0.5f * df - Inset);
            if (standing || math.max(_a, _b) < MinHalfAxis) _a = _b = 0f;
            _angle = 0f;
            _time = 0f;
            _hold = 0f;
        }

        /// <summary>Seconds the head still stands where it is, looking about, before it walks on.</summary>
        public float HoldSeconds { get { return _hold; } }

        /// <summary>Stands where the head is for this many seconds from now: the gaze goes on sweeping, the floor
        /// point does not move. The dial's curvature ramps only while the walker stands (section 4.6).</summary>
        public void HoldFor(float seconds)
        {
            _hold = math.max(0f, seconds);
        }

        public bool TryRead(double time, float deltaTime, out HeadPose pose)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;
            _time += dt;
            float2 floor;
            float heading;
            if (_a <= 0f && _b <= 0f)
            {
                floor = float2.zero;
                heading = SpotTurnRate * _time;
            }
            else
            {
                // Constant speed along the ellipse: the angle advances by distance over the tangent's length.
                float s, c;
                math.sincos(_angle, out s, out c);
                float tangent = math.sqrt(_a * _a * s * s + _b * _b * c * c);
                // A hold spends its seconds first; what is left of the frame is walked.
                float walked = math.max(0f, dt - _hold);
                _hold = math.max(0f, _hold - dt);
                _angle += Speed * walked / math.max(tangent, 0.05f);
                if (_angle > 2f * math.PI) _angle -= 2f * math.PI;
                math.sincos(_angle, out s, out c);
                floor = new float2(_a * c, _b * s);
                heading = math.atan2(-_a * s, _b * c);
            }

            float yaw = heading + GazeYaw * math.sin(2f * math.PI * _time / GazeYawPeriod);
            float pitch = GazePitchMean + GazePitch * math.sin(2f * math.PI * _time / GazePitchPeriod);
            // Unity's sense: a positive turn about X looks down, so looking up is a negative one.
            quaternion rotation = math.mul(quaternion.RotateY(yaw), quaternion.RotateX(-pitch));
            pose = new HeadPose(time, new float3(floor.x, EyeHeight, floor.y), rotation, TrackingState.Normal);
            return true;
        }
    }
}
