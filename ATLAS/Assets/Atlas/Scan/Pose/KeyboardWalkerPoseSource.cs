using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// The Editor walker (contract README F9): a head that walks on the floor of the room frame under
    /// keyboard and mouse input. Engine-free: the input arrives through SetInput, time through TryRead.
    /// </summary>
    public sealed class KeyboardWalkerPoseSource : IPoseSource
    {
        /// <summary>Metres: the walker's eye height.</summary>
        public const float EyeHeight = 1.6f;
        /// <summary>Degrees: the pitch never passes this, so the heading stays defined.</summary>
        public const float MaxPitchDegrees = 85f;
        /// <summary>Metres per second when the caller gives no speed.</summary>
        public const float DefaultSpeed = 1.0f;

        float2 _move;
        float2 _look;
        float _speed = DefaultSpeed;
        float2 _floor;
        float _yaw;      // radians
        float _pitch;    // radians, positive looks up

        public string Name { get { return "walker"; } }
        public PoseFrameKind Frame { get { return PoseFrameKind.Room; } }
        public bool DrivesRig { get { return false; } }

        /// <summary>The tracking state reported with every sample. Set it to inject a tracking loss.</summary>
        public TrackingState Tracking { get; set; } = TrackingState.Normal;
        /// <summary>Metres (x, z): where the walker stands.</summary>
        public float2 Floor { get { return _floor; } }
        /// <summary>Radians, Unity's sense.</summary>
        public float Yaw { get { return _yaw; } }
        /// <summary>Radians, positive looks up.</summary>
        public float Pitch { get { return _pitch; } }
        /// <summary>True once SetInput has been called by the application. The built-in keyboard reader then stays out.</summary>
        public bool HasExternalInput { get; private set; }

        /// <summary>
        /// The input for the next sample. move is in the head's heading frame (x right, y forward, -1 to 1);
        /// lookDegrees is the yaw and pitch increment for the next sample (pitch positive looks up); speed is
        /// metres per second.
        /// </summary>
        public void SetInput(float2 move, float2 lookDegrees, float speed)
        {
            HasExternalInput = true;
            Feed(move, lookDegrees, speed);
        }

        /// <summary>The same as SetInput, for the built-in keyboard reader: it does not claim the input.</summary>
        internal void Feed(float2 move, float2 lookDegrees, float speed)
        {
            _move = move;
            _look = lookDegrees;
            _speed = speed > 0f ? speed : DefaultSpeed;
        }

        /// <summary>Moves the walker at once: a pose jump for the pipeline to reject.</summary>
        public void Teleport(float2 xz)
        {
            _floor = xz;
        }

        /// <summary>Puts the walker at a floor point with a heading, looking level.</summary>
        public void Place(float2 xz, float yawRadians)
        {
            _floor = xz;
            _yaw = yawRadians;
            _pitch = 0f;
        }

        public bool TryRead(double time, float deltaTime, out HeadPose pose)
        {
            Integrate(deltaTime);
            pose = new HeadPose(time, new float3(_floor.x, EyeHeight, _floor.y), Rotation, Tracking);
            return true;
        }

        /// <summary>RotateY(yaw) * RotateX(-pitch).</summary>
        public quaternion Rotation
        {
            get { return math.mul(quaternion.RotateY(_yaw), quaternion.RotateX(-_pitch)); }
        }

        void Integrate(float deltaTime)
        {
            // The look increment is per sample, not per second: a mouse delta is already a distance.
            _yaw += math.radians(_look.x);
            if (_yaw > math.PI) _yaw -= 2f * math.PI;
            else if (_yaw < -math.PI) _yaw += 2f * math.PI;
            float limit = math.radians(MaxPitchDegrees);
            _pitch = math.clamp(_pitch + math.radians(_look.y), -limit, limit);
            _look = float2.zero;

            if (!(deltaTime > 0f)) return;
            float2 move = _move;
            float lengthSq = math.lengthsq(move);
            if (lengthSq > 1f) move /= math.sqrt(lengthSq);     // a diagonal is not faster
            float s = math.sin(_yaw), c = math.cos(_yaw);
            float2 forward = new float2(s, c);
            float2 right = new float2(c, -s);
            _floor += (right * move.x + forward * move.y) * (_speed * deltaTime);
        }
    }
}
