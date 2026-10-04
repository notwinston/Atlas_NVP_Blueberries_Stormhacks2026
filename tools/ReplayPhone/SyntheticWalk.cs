using System;
using Atlas.Link;

namespace Atlas.Tools
{
    /// <summary>
    /// A walker with no phone: laps of a rounded track inside a 3 m by 4 m free rectangle at walking speed, in
    /// the room frame of contract F4 (origin at the centre of the rectangle on the floor, +Y up, metres). It
    /// fills <c>State</c> the way a flat space does: tile zero and a tile-local rigid transform of the floor.
    /// </summary>
    public sealed class SyntheticWalk
    {
        public const float RoomW = 3f;
        public const float RoomD = 4f;
        const float TrackHalfW = 1.0f;
        const float TrackHalfD = 1.5f;
        const float SpeedMps = 0.8f;
        const float EyeHeight = 1.6f;

        /// <summary>Space shown, 0 (Home) to 6. Set by the switch-space command.</summary>
        public int Space;

        /// <summary>The tier reported in telemetry, as <c>Atlas.World.QualityTier</c>: Nominal 0, an unloaded phone's rung.</summary>
        public int QualityTier;
        public ushort WorldVer = 1;

        /// <summary>
        /// The head follows the Station's walk input (the app's <c>pose.source</c> = walker) instead of the
        /// track. It takes over where the track left it.
        /// </summary>
        public bool Driven;

        bool _wasDriven;
        double _lastT;
        double _x, _z, _yaw;
        OperatorCommand _input;
        float _holdLeft;

        /// <summary>The Map page's walk input, held as the app holds it (<see cref="OperatorCommand.WalkHoldSeconds"/>).</summary>
        public void Walk(in OperatorCommand command)
        {
            _input = command;
            _holdLeft = command.IsStill ? 0f : OperatorCommand.WalkHoldSeconds;
        }

        /// <summary>The state at <paramref name="t"/> seconds into the walk. Allocates nothing.</summary>
        public void Fill(ref StateMessage state, double t, ParamRegistry registry, ParamHandle cullRho)
        {
            double dt = Math.Max(0.0, t - _lastT);
            _lastT = t;
            if (!Driven || !_wasDriven) Track(t, out _x, out _z, out _yaw);
            else Step(dt);
            _wasDriven = Driven;
            float x = (float)_x, z = (float)_z;
            double yaw = _yaw;
            float bob = 0.015f * (float)Math.Sin(t * 2.0 * Math.PI * 1.8);   // head bob at step frequency

            state = StateMessage.Identity;
            state.Tracking = 2;
            // State has two bits for the tier; the high tier travels as 0 (lane D contract 2.4).
            state.QualityTier = QualityTier > 3 ? 0 : QualityTier;
            state.SceneId = (ushort)Space;
            state.SpaceId = (byte)Space;
            state.HeadPosX = x;
            state.HeadPosY = EyeHeight + bob;
            state.HeadPosZ = z;
            state.HeadRotX = 0f;
            state.HeadRotY = (float)Math.Sin(0.5 * yaw);
            state.HeadRotZ = 0f;
            state.HeadRotW = (float)Math.Cos(0.5 * yaw);

            // Bump Guard levels as it would raise them: warn within 0.45 m of the rectangle's edge, strong within 0.2 m.
            float margin = Math.Min(0.5f * RoomW - Math.Abs(x), 0.5f * RoomD - Math.Abs(z));
            state.BumpLevel = (byte)(margin < 0.2f ? 2 : margin < 0.45f ? 1 : 0);
            state.CueActive = state.BumpLevel > 0;

            state.SetTile(0, 0, 0, 0, 0, 0);
            state.Sheet = 0;
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            state.SetXform(0, c); state.SetXform(1, s); state.SetXform(2, x);
            state.SetXform(3, -s); state.SetXform(4, c); state.SetXform(5, z);
            state.SetXform(6, 0); state.SetXform(7, 0); state.SetXform(8, 1);

            state.SetParam(StateMessage.ParamCullRho, registry.GetFloat(cullRho));
            state.WorldVer = WorldVer;
        }

        static void Track(double t, out double x, out double z, out double yaw)
        {
            // An ellipse of the track's size; angular speed chosen so the ground speed stays near walking pace.
            double meanRadius = 0.5 * (TrackHalfW + TrackHalfD);
            double a = t * SpeedMps / meanRadius;
            x = TrackHalfW * Math.Cos(a);
            z = TrackHalfD * Math.Sin(a);
            // Heading along the tangent; yaw about +Y, measured from +Z towards +X (Unity's convention).
            double dx = -TrackHalfW * Math.Sin(a);
            double dz = TrackHalfD * Math.Cos(a);
            yaw = Math.Atan2(dx, dz);
        }

        // The app's keyboard walker: move in the heading frame, a diagonal no faster, turn at the asked rate.
        void Step(double dt)
        {
            if (_holdLeft <= 0f) return;
            _holdLeft -= (float)dt;
            _yaw += _input.Turn * Math.PI / 180.0 * dt;
            if (_yaw > Math.PI) _yaw -= 2.0 * Math.PI;
            else if (_yaw < -Math.PI) _yaw += 2.0 * Math.PI;
            double mx = _input.MoveX, my = _input.MoveY;
            double length = Math.Sqrt(mx * mx + my * my);
            if (length > 1.0) { mx /= length; my /= length; }
            double s = Math.Sin(_yaw), c = Math.Cos(_yaw);
            _x += (c * mx + s * my) * _input.Speed * dt;
            _z += (-s * mx + c * my) * _input.Speed * dt;
        }

        /// <summary>The room the walk happens in, for the Spectator's outline view.</summary>
        public static RoomOutlineMessage Outline()
        {
            var outline = new RoomOutlineMessage();
            outline.Clear();
            float hw = 0.5f * RoomW + 0.4f, hd = 0.5f * RoomD + 0.4f;
            outline.AddPoint(-hw, -hd);
            outline.AddPoint(hw, -hd);
            outline.AddPoint(hw, hd);
            outline.AddPoint(-hw, hd);
            outline.Height = 2.5f;
            outline.RectCx = 0f;
            outline.RectCz = 0f;
            outline.RectW = RoomW;
            outline.RectD = RoomD;
            outline.RectYaw = 0f;
            outline.AddBox(new RoomOutlineBox { Id = 1, Category = 1, Cx = hw - 0.3f, Cz = 0f, W = 0.6f, D = 1.2f, H = 0.75f, Yaw = 0f });
            return outline;
        }
    }
}
