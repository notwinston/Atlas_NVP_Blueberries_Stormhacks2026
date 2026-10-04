using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>
    /// The app's Bump Guard: the core plus the cue (Master Plan section 4.10, task P1-C09). Created by
    /// <see cref="SafetyInstaller"/> and ticked first in the frame (section 6.3, step 2) with the raw pose.
    /// A plain class: it reads no clock, no link, no parameter and no file, and has no off switch. It works
    /// with no depth image (the static map alone) and with no room (live depth alone, in the head frame).
    /// Tick allocates nothing, except on the one frame a room becomes active again.
    /// </summary>
    public sealed unsafe class BumpGuard : IBumpGuard, IDisposable
    {
        /// <summary>
        /// Images up to this many pixels are processed inline: the Editor's synthetic depth (64 x 48) costs less
        /// than the copy into the job's buffer.
        /// </summary>
        public const int InlineMaxPixels = 64 * 48;

        readonly BumpGuardCore _core = new BumpGuardCore();
        readonly bool _useBurstJob;
        BumpDepthRunner _runner;
        RoomModel _room;
        bool _disposed;

        public BumpGuard() : this(true)
        {
        }

        /// <param name="useBurstJob">False runs the depth kernel inline for every image: for tests with no engine.</param>
        public BumpGuard(bool useBurstJob)
        {
            _useBurstJob = useBurstJob;
        }

        /// <summary>The logic. For tests, the HUD's diagnostics and telemetry counters.</summary>
        public BumpGuardCore Core { get { return _core; } }

        public BumpLevel Level { get { return _core.Level; } }
        public float2 Direction { get { return _core.Direction; } }
        public bool RescanAdvised { get { return _core.RescanAdvised; } }

        /// <summary>
        /// True from the headset prompt of onboarding until the next scan starts. While false, Tick still
        /// computes Level but asks for no cue: the phone is in the hand. Set only by onboarding. No link
        /// message, parameter or menu row reaches it. An operator cue is shown either way.
        /// </summary>
        public bool Armed { get; set; }

        /// <summary>The room whose distance field is the static map, or null.</summary>
        public RoomModel Room { get { return _room; } }

        /// <summary>
        /// Gives Bump Guard the room's static map: the distance field of room.Sdf, copied. Null, or a room with
        /// no field, drops the map: live depth alone is used until the next room. Not for the frame path.
        /// </summary>
        public void SetRoom(RoomModel room)
        {
            _room = room != null && room.Sdf != null ? room : null;
            if (_room != null) LoadRoom();
            else _core.ClearRoom();
        }

        /// <summary>The first call of the frame. Never awaits.</summary>
        public void Tick(in SensorFrame sensors)
        {
            if (_disposed) return;
            bool trackingNormal = sensors.Head.Tracking == TrackingState.Normal;

            // The static map holds only while the head is in the room frame. While it is not (a rescan is
            // running), the map is put away and live depth alone warns; it comes back with the room frame.
            if (!sensors.RoomActive)
            {
                if (_core.HasRoom) _core.ClearRoom();
            }
            else if (!_core.HasRoom && _room != null)
            {
                LoadRoom();
            }

            if (sensors.HasDepth) ProcessDepth(sensors, trackingNormal);

            _core.Step(sensors.Time, sensors.Head.Position, sensors.Head.Rotation, trackingNormal, sensors.AnchorTracked);

            if (Armed)
            {
                BumpCue.Fire(_core.Level, _core.Direction);
                BumpCue.Rescan(_core.RescanAdvised);
            }
            else
            {
                BumpCue.Fire(BumpLevel.Clear, float2.zero);
                BumpCue.Rescan(false);
            }
            BumpCue.Advance(sensors.Time, sensors.DeltaTime);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_runner != null) _runner.Dispose();
            _runner = null;
            _core.Dispose();
        }

        void LoadRoom()
        {
            RoomSdf sdf = _room.Sdf;
            _core.SetRoom(sdf.Metres, sdf.Width, sdf.Height, RoomSdf.CellSize, sdf.OriginX, sdf.OriginZ);
        }

        void ProcessDepth(in SensorFrame sensors, bool trackingNormal)
        {
            // A frame that says it has depth but carries none is skipped: the static map still warns.
            float[] pixels = sensors.Depth.Depth;
            int width = sensors.Depth.Width, height = sensors.Depth.Height;
            if (pixels == null || width <= 0 || height <= 0 || !(sensors.Depth.Fx > 0f) || !(sensors.Depth.Fy > 0f))
                return;
            int count = width * height;
            if (count > pixels.Length) return;

            var intrinsics = new DepthIntrinsics(sensors.Depth.Fx, sensors.Depth.Fy, sensors.Depth.Cx, sensors.Depth.Cy);
            // The frame's own time, not the image's: the core compares it with the time of Step.
            if (_useBurstJob && count > InlineMaxPixels)
            {
                if (_runner == null) _runner = new BumpDepthRunner();
                DepthKernelInput input = _core.MakeDepthInput(width, height, intrinsics, sensors.Depth.Position,
                    sensors.Depth.Rotation, sensors.Time, trackingNormal, sensors.AnchorTracked);
                _runner.Run(_core, pixels, input);
            }
            else
            {
                fixed (float* p = pixels)
                {
                    _core.ProcessDepth(p, width, height, intrinsics, sensors.Depth.Position, sensors.Depth.Rotation,
                        sensors.Time, trackingNormal, sensors.AnchorTracked);
                }
            }
        }
    }
}
