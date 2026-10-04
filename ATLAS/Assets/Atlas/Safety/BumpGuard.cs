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
    /// <para>A sensor frame may carry a depth mask: where the walker's own hands are in its depth image
    /// (documents/ATLAS_Hands_Design.md section 7.1). Bump Guard does not know or ask who made it, and trusts
    /// none of it: the regions of a mask built for another image are ignored here, and the depth kernel holds
    /// the rest to the limits in <see cref="BumpConstants"/>. A mask also says whether a hands session is
    /// running, with or without regions; in a session a near return must be seen twice before it counts
    /// (decision R13). A frame with no mask and no session is handled exactly as before masks existed.</para>
    /// </summary>
    public sealed unsafe class BumpGuard : IBumpGuard, IClearanceQuery, IDisposable
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
        double _time;
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

        /// <summary>
        /// clear(p) of section 4.10 at a floor point of the room frame, as of the last Tick: the smaller of the
        /// static distance field and the distance to the nearest live dynamic cell that counts (a cell that is
        /// pending in a hands session does not). Positive infinity while no room is loaded (the map is put away
        /// during a rescan too). Reads only; safe on the frame path.
        /// </summary>
        public float Clearance(float2 p)
        {
            return _disposed ? float.PositiveInfinity : _core.ClearAt(p, _time);
        }

        /// <summary>
        /// A depth mask as the depth kernel takes it, for the depth image numbered
        /// <paramref name="depthSequence"/>: plain numbers, at most <see cref="BumpConstants.MaskMaxRegions"/>
        /// regions, and no region unless the mask was built for exactly that image (its DepthSequence). The
        /// regions of a mask for an older or a later image would cover the wrong pixels, so they are dropped
        /// whole. The session flag is the frame's and is kept whatever the image and with no region at all.
        /// </summary>
        public static DepthKernelMask KernelMask(in DepthMask mask, int depthSequence)
        {
            DepthKernelMask kernel = default;
            kernel.Session = mask.Session != 0 ? 1 : 0;
            if (mask.Count <= 0 || mask.DepthSequence != depthSequence) return kernel;
            kernel.Count = mask.Count < BumpConstants.MaskMaxRegions ? mask.Count : BumpConstants.MaskMaxRegions;
            kernel.Region0 = KernelRegion(mask.Region0);
            if (kernel.Count > 1) kernel.Region1 = KernelRegion(mask.Region1);
            return kernel;
        }

        /// <summary>The first call of the frame. Never awaits.</summary>
        public void Tick(in SensorFrame sensors)
        {
            if (_disposed) return;
            _time = sensors.Time;
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
            DepthKernelInput input = _core.MakeDepthInput(width, height, intrinsics, sensors.Depth.Position,
                sensors.Depth.Rotation, sensors.Time, trackingNormal, sensors.AnchorTracked);
            // The walker's hands, if the frame says where they are in this very image, and whether a hands
            // session is running.
            input.Mask = KernelMask(sensors.Mask, sensors.Depth.Sequence);
            if (_useBurstJob && count > InlineMaxPixels)
            {
                if (_runner == null) _runner = new BumpDepthRunner();
                _runner.Run(_core, pixels, input);
            }
            else
            {
                fixed (float* p = pixels)
                {
                    _core.ProcessDepth(p, input);
                }
            }
        }

        static DepthKernelMaskRegion KernelRegion(in DepthMaskRegion region)
        {
            return new DepthKernelMaskRegion
            {
                MinU = region.MinU,
                MinV = region.MinV,
                MaxU = region.MaxU,
                MaxV = region.MaxV,
                ArmU0 = region.ArmU0,
                ArmV0 = region.ArmV0,
                ArmU1 = region.ArmU1,
                ArmV1 = region.ArmV1,
                ArmHalfWidth = region.ArmHalfWidth,
                MaxDepth = region.MaxDepth,
                ArmMaxDepth = region.ArmMaxDepth
            };
        }
    }
}
