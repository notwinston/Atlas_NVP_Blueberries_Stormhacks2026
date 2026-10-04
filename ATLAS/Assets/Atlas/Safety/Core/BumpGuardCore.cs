using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>Numbers behind the level, for tests, the HUD and telemetry counters.</summary>
    public struct BumpDiagnostics
    {
        /// <summary>clear(head), metres; positive infinity when nothing is known. Dynamic cells farther than 0.40 m are not counted.</summary>
        public float ClearNow;
        /// <summary>path(0.8 s), metres.</summary>
        public float PathWarn;
        /// <summary>path(0.4 s), metres.</summary>
        public float PathStrong;
        /// <summary>Speed used for the prediction, metres per second, after the low-pass.</summary>
        public float Speed;
        /// <summary>How long tracking has not been Normal, or the anchor not tracked, seconds.</summary>
        public float NotNormalSeconds;
        /// <summary>Occupied cells in the dynamic layer.</summary>
        public int DynamicCells;
        /// <summary>Times the level rose since Reset.</summary>
        public int RisingEdges;
        /// <summary>The static map was not used this step: live depth alone, in the head frame.</summary>
        public bool HeadFrameFallback;
        /// <summary>Median wall disagreement, metres; NaN when the last depth image did not measure it.</summary>
        public float RescanMedian;
    }

    /// <summary>
    /// The warning logic of Bump Guard (Master Plan section 4.10) with no engine in it: static clearance from the
    /// room's distance field, a dynamic layer fed by live depth, head velocity, straight-path prediction over
    /// 0.4 s and 0.8 s, the level with its relax hysteresis, the head-frame fallback while the map cannot be
    /// trusted, and the rescan check. A thin adapter feeds it once per frame; BumpGuard_Replay runs it headless.
    /// It reads no clock and no link, and allocates only in <see cref="SetRoom"/>. Not thread-safe.
    /// </summary>
    public sealed unsafe partial class BumpGuardCore : IDisposable
    {
        // Room storage, pinned so that the depth job can work on it through raw pointers.
        float[] _sdf;
        float[] _topHeight;
        double[] _lastHit;
        int[] _emptyState;
        int[] _blockCount;
        readonly int[] _activeCount = new int[1];
        GCHandle _sdfPin, _topPin, _hitPin, _emptyPin, _blockPin, _activePin;
        bool _pinned;
        BumpGridSpec _grid;
        DynamicGrid _dynamic;
        int _sweepCursor;
        int _depthSequence;

        HeadVelocityFilter _filter;
        float _lastValidSpeed;
        double _notNormalSince = -1.0;
        double _relaxSince = -1.0;
        int _risingEdges;

        DepthFrameResult _depth = DepthFrameResult.Empty;
        bool _hasDepth;
        double _disagreeSince = -1.0;
        double _agreeSince = -1.0;
        double _lastWallCheck = -1.0;
        float _rescanMedian = float.NaN;
        float _notNormalSeconds;

        /// <summary>The level to show. Rises at once, relaxes only after 0.05 m and 0.3 s of margin.</summary>
        public BumpLevel Level { get; private set; }
        /// <summary>
        /// Unit vector from the head towards the hazard in the head's level frame: x to the walker's right,
        /// y ahead (pitch and roll are left out). Zero while the level is Clear or when no direction is known.
        /// </summary>
        public float2 Direction { get; private set; }
        /// <summary>Live depth and the static map have disagreed by more than 0.15 m for 2 s.</summary>
        public bool RescanAdvised { get; private set; }

        /// <summary>Diagnostic: clearance at the head, metres. Dynamic cells farther than 0.40 m are not counted.</summary>
        public float Now { get; private set; } = float.PositiveInfinity;
        /// <summary>Diagnostic: smallest clearance along the next 0.4 s of straight walking, metres.</summary>
        public float Path04 { get; private set; } = float.PositiveInfinity;
        /// <summary>Diagnostic: smallest clearance along the next 0.8 s of straight walking, metres.</summary>
        public float Path08 { get; private set; } = float.PositiveInfinity;
        /// <summary>Diagnostic: head speed on the floor used for the prediction, metres per second.</summary>
        public float Speed { get; private set; }
        /// <summary>Diagnostic: low-passed head velocity on the floor (x, z), room frame, metres per second.</summary>
        public float2 Velocity => _filter.Velocity;
        /// <summary>Diagnostic: whether the last step used the static map (false: live depth alone).</summary>
        public bool MapTrusted { get; private set; }
        /// <summary>Diagnostic: the level before hysteresis.</summary>
        public BumpLevel RawLevel { get; private set; }
        /// <summary>Diagnostic: the last step saw a pose jump.</summary>
        public bool PoseJumped { get; private set; }
        /// <summary>Diagnostic: occupied cells in the dynamic layer.</summary>
        public int DynamicCellCount => _activeCount[0];
        /// <summary>Diagnostic: the last depth image's result.</summary>
        public DepthFrameResult LastDepth => _depth;

        /// <summary>The diagnostic values in one struct, for logging.</summary>
        public BumpDiagnostics Diagnostics => new BumpDiagnostics
        {
            ClearNow = Now,
            PathWarn = Path08,
            PathStrong = Path04,
            Speed = Speed,
            NotNormalSeconds = _notNormalSeconds,
            DynamicCells = _activeCount[0],
            RisingEdges = _risingEdges,
            HeadFrameFallback = !MapTrusted,
            RescanMedian = _rescanMedian
        };

        /// <summary>True once a room's distance field is loaded.</summary>
        public bool HasRoom => _pinned;
        /// <summary>Layout of the static and dynamic grids.</summary>
        public BumpGridSpec Grid => _grid;
        /// <summary>The static distance field, for the depth job. Null without a room.</summary>
        public float* StaticField => _pinned ? (float*)_sdfPin.AddrOfPinnedObject() : null;
        /// <summary>The dynamic layer, for the depth job. Not created without a room.</summary>
        public DynamicGrid Dynamic => _dynamic;

        /// <summary>
        /// Loads the static map: the RoomModel distance field as metres at cell centres, positive inside the free
        /// floor, row-major with row 0 at the lowest z (index = j * width + i). The values are copied. Clears the
        /// dynamic layer, the velocity window and the timers. Not for the frame path: this allocates.
        /// </summary>
        public void SetRoom(float[] sdfMetres, int width, int height, float cellSize, float originX, float originZ)
        {
            if (sdfMetres == null) throw new ArgumentNullException(nameof(sdfMetres));
            if (width <= 0 || height <= 0 || sdfMetres.Length < width * height)
                throw new ArgumentException("The distance field is smaller than width x height.");
            if (!(cellSize > 0f)) throw new ArgumentException("Cell size must be positive.");

            ReleaseRoom();
            int cells = width * height;
            _grid = new BumpGridSpec(width, height, cellSize, originX, originZ);
            _sdf = new float[cells];
            Array.Copy(sdfMetres, _sdf, cells);
            _topHeight = new float[cells];
            _lastHit = new double[cells];
            _emptyState = new int[cells];
            int bx = DynamicLayer.BlocksFor(width), bz = DynamicLayer.BlocksFor(height);
            _blockCount = new int[bx * bz];
            _activeCount[0] = 0;

            _sdfPin = GCHandle.Alloc(_sdf, GCHandleType.Pinned);
            _topPin = GCHandle.Alloc(_topHeight, GCHandleType.Pinned);
            _hitPin = GCHandle.Alloc(_lastHit, GCHandleType.Pinned);
            _emptyPin = GCHandle.Alloc(_emptyState, GCHandleType.Pinned);
            _blockPin = GCHandle.Alloc(_blockCount, GCHandleType.Pinned);
            _activePin = GCHandle.Alloc(_activeCount, GCHandleType.Pinned);
            _pinned = true;

            _dynamic = new DynamicGrid
            {
                TopHeight = (float*)_topPin.AddrOfPinnedObject(),
                LastHit = (double*)_hitPin.AddrOfPinnedObject(),
                EmptyState = (int*)_emptyPin.AddrOfPinnedObject(),
                BlockCount = (int*)_blockPin.AddrOfPinnedObject(),
                ActiveCount = (int*)_activePin.AddrOfPinnedObject(),
                Spec = _grid,
                BlocksX = bx,
                BlocksZ = bz
            };
            Reset();
        }

        /// <summary>Drops the room. Until the next <see cref="SetRoom"/> only live depth is used.</summary>
        public void ClearRoom()
        {
            ReleaseRoom();
            Reset();
        }

        /// <summary>Back to the state after <see cref="SetRoom"/>: empty dynamic layer, no velocity, level Clear, timers at zero.</summary>
        public void Reset()
        {
            ClearDynamic();
            _sweepCursor = 0;
            _filter.Reset();
            _lastValidSpeed = 0f;
            _notNormalSince = -1.0;
            _notNormalSeconds = 0f;
            _relaxSince = -1.0;
            _risingEdges = 0;
            _depth = DepthFrameResult.Empty;
            _hasDepth = false;
            _disagreeSince = -1.0;
            _agreeSince = -1.0;
            _lastWallCheck = -1.0;
            _rescanMedian = float.NaN;
            Level = BumpLevel.Clear;
            RawLevel = BumpLevel.Clear;
            Direction = float2.zero;
            RescanAdvised = false;
            Now = Path04 = Path08 = float.PositiveInfinity;
            Speed = 0f;
            MapTrusted = false;
            PoseJumped = false;
        }

        /// <summary>Empties the dynamic layer.</summary>
        public void ClearDynamic()
        {
            if (_pinned) DynamicLayer.ClearAll(_dynamic);
        }

        /// <summary>Test hook: marks the dynamic cell under a floor point as hit at this height and time.</summary>
        public void StampDynamic(float2 xz, float height, double time)
        {
            if (_pinned) DynamicLayer.Stamp(_dynamic, xz, height, time);
        }

        /// <summary>The seven sector ranges of the last processed depth image, metres, left to right.</summary>
        public float SectorRange(int sector) => _depth.Sector(sector);

        /// <summary>
        /// Describes a depth image for <see cref="BumpDepthKernel.Process"/>, for callers that run the kernel in
        /// a job and hand the result to <see cref="SubmitDepthResult"/>. Gives the image its sequence number.
        /// </summary>
        public DepthKernelInput MakeDepthInput(int width, int height, in DepthIntrinsics intrinsics,
            float3 sensorPosition, quaternion sensorRotation, double time, bool trackingNormal, bool anchorTracked)
        {
            return new DepthKernelInput
            {
                Width = width,
                Height = height,
                Stride = BumpConstants.DepthStride,
                Intrinsics = intrinsics,
                SensorPosition = sensorPosition,
                SensorRotation = sensorRotation,
                Time = time,
                Sequence = ++_depthSequence,
                MapTrusted = _pinned && trackingNormal && anchorTracked
            };
        }

        /// <summary>
        /// Processes one depth image now, on the calling thread: stamps and clears the dynamic layer, fills the
        /// sectors and feeds the rescan check. Call before <see cref="Step"/> on frames that deliver depth.
        /// </summary>
        /// <param name="depth">width x height depths in metres, row 0 at the top of the image.</param>
        public void ProcessDepth(float* depth, int width, int height, in DepthIntrinsics intrinsics,
            float3 sensorPosition, quaternion sensorRotation, double time, bool trackingNormal, bool anchorTracked)
        {
            DepthKernelInput input = MakeDepthInput(width, height, intrinsics, sensorPosition, sensorRotation, time,
                trackingNormal, anchorTracked);
            BumpDepthKernel.Process(depth, input, StaticField, _grid, _dynamic, out DepthFrameResult result);
            SubmitDepthResult(result);
        }

        /// <summary>Takes the result of a depth image that was processed by the job.</summary>
        public void SubmitDepthResult(in DepthFrameResult result)
        {
            _depth = result;
            _hasDepth = true;
            _rescanMedian = float.NaN;
            if (!result.MapTrusted || result.WallSamples < BumpConstants.RescanMinSamples) return;

            // Rescan check: advised after 2 s above 0.15 m, withdrawn after 2 s below 0.10 m, counted over
            // images that see enough wall. A gap in those images restarts the count.
            float median = result.WallDisagreement;
            _rescanMedian = median;
            double t = result.Time;
            bool gap = _lastWallCheck >= 0.0 && t - _lastWallCheck > BumpConstants.WallCheckMaxGapSeconds;
            _lastWallCheck = t;
            if (median > BumpConstants.RescanDisagreement)
            {
                _agreeSince = -1.0;
                if (_disagreeSince < 0.0 || gap) _disagreeSince = t;
                if (t - _disagreeSince >= BumpConstants.RescanSeconds) RescanAdvised = true;
            }
            else if (median < BumpConstants.RescanClearDisagreement)
            {
                _disagreeSince = -1.0;
                if (_agreeSince < 0.0 || gap) _agreeSince = t;
                if (t - _agreeSince >= BumpConstants.RescanSeconds) RescanAdvised = false;
            }
            else
            {
                _disagreeSince = -1.0;
                _agreeSince = -1.0;
            }
        }

        /// <summary>
        /// One frame of Bump Guard. Call first in the frame, after any depth image has been processed.
        /// </summary>
        /// <param name="time">Monotonic time of this frame, seconds.</param>
        /// <param name="headPosition">Raw head position in the room frame, metres (never the filtered pose).</param>
        /// <param name="headRotation">Raw head rotation in the room frame.</param>
        /// <param name="trackingNormal">ARKit tracking state is Normal.</param>
        /// <param name="anchorTracked">The room's ARAnchor is tracked (true when the pose source has no anchor).</param>
        public void Step(double time, float3 headPosition, quaternion headRotation, bool trackingNormal, bool anchorTracked)
        {
            // Velocity from the raw pose. While tracking is not Normal the pose is not used and the last
            // valid speed stands.
            PoseJumped = false;
            if (trackingNormal)
            {
                _filter.Update(headPosition.xz, time);
                PoseJumped = _filter.Jumped;
                _lastValidSpeed = math.min(math.length(_filter.Velocity), BumpConstants.MaxPredictSpeed);
            }
            Speed = _lastValidSpeed;

            bool normal = trackingNormal && anchorTracked;
            if (normal) _notNormalSince = -1.0;
            else if (_notNormalSince < 0.0) _notNormalSince = time;
            _notNormalSeconds = normal ? 0f : (float)(time - _notNormalSince);

            // The static map is trusted only while tracking is Normal and the anchor is tracked.
            bool trusted = normal && _pinned;
            MapTrusted = trusted;

            float2 direction;
            if (trusted)
            {
                DynamicLayer.SweepExpired(_dynamic, time, ref _sweepCursor, BumpConstants.SweepBlocksPerStep);
                float2 world = EvaluateMap(time, headPosition.xz);
                float2 forward = HorizontalForward(headRotation);
                float2 right = new float2(forward.y, -forward.x);
                direction = math.normalizesafe(new float2(math.dot(world, right), math.dot(world, forward)));
            }
            else
            {
                direction = EvaluateDepthOnly(time);
            }

            bool lost = !normal && _notNormalSeconds >= BumpConstants.TrackingLostSeconds;
            BumpLevel want = lost ? BumpLevel.TrackingLost : LevelFor(Now, Path04, Path08, 0f);
            BumpLevel floor = lost ? BumpLevel.TrackingLost : LevelFor(Now, Path04, Path08, BumpConstants.RelaxDistance);
            RawLevel = want;

            // Rise at once; fall only after the thresholds plus the margin have held for 0.3 s without a break.
            if (want > Level)
            {
                Level = want;
                _relaxSince = -1.0;
                _risingEdges++;
            }
            else if (floor < Level)
            {
                if (_relaxSince < 0.0) _relaxSince = time;
                else if (time - _relaxSince >= BumpConstants.RelaxSeconds)
                {
                    Level = floor;
                    _relaxSince = -1.0;
                }
            }
            else
            {
                _relaxSince = -1.0;
            }

            if (Level == BumpLevel.Clear) Direction = float2.zero;
            else if (math.lengthsq(direction) > 0f) Direction = direction;
            // Otherwise no bearing is known this frame: the last direction stands.
        }

        /// <summary>clear(p) of section 4.10: the smaller of the static clearance and the distance to the nearest dynamic cell, metres.</summary>
        public float ClearAt(float2 xz, double time)
        {
            if (!_pinned) return float.PositiveInfinity;
            float s = StaticClearance.Sample(StaticField, _grid, xz);
            float d = DynamicLayer.Distance(_dynamic, xz, time, out _);
            return math.min(s, d);
        }

        public void Dispose()
        {
            ReleaseRoom();
        }

        // now, path(0.4 s) and path(0.8 s) on the map. Returns the room-frame direction towards the hazard
        // (not normalised; zero when unknown).
        float2 EvaluateMap(double time, float2 head)
        {
            float* sdf = StaticField;
            float2 velocity = _filter.Velocity;
            float speed = math.length(velocity);
            if (speed > BumpConstants.MaxPredictSpeed)
            {
                velocity *= BumpConstants.MaxPredictSpeed / speed;
                speed = BumpConstants.MaxPredictSpeed;
            }
            float reach = speed * BumpConstants.WarnHorizon;

            // One sample per PathStep of travel, both ends included; an even number of segments so that the
            // end of the 0.4 s path is a sample too.
            int segments = 0;
            if (reach > 1e-3f)
            {
                segments = (int)math.ceil(reach / BumpConstants.PathStep);
                segments += segments & 1;
            }
            int half = segments / 2;

            float now = 0f, path04 = float.PositiveInfinity, path08 = float.PositiveInfinity;
            bool nowDynamic = false, worstAtHead = true;
            float2 nowCell = head;

            for (int k = 0; k <= segments; k++)
            {
                float2 p = k == 0 ? head : head + velocity * (BumpConstants.WarnHorizon * k / segments);
                float s = StaticClearance.Sample(sdf, _grid, p);
                float d = DynamicLayer.Distance(_dynamic, p, time, out float2 cell);
                float c = math.min(s, d);
                if (k == 0)
                {
                    now = c;
                    nowDynamic = d < s;
                    nowCell = cell;
                }
                if (k <= half && c < path04) path04 = c;
                if (c < path08)
                {
                    path08 = c;
                    worstAtHead = k == 0;
                }
            }

            Now = now;
            Path04 = path04;
            Path08 = path08;

            // Lane C contract 8.6: ahead on the path, the hazard lies in the direction of travel; at the head,
            // towards the dynamic cell or down the gradient of the static field.
            if (!worstAtHead) return velocity;
            if (nowDynamic) return nowCell - head;
            return -StaticClearance.Gradient(sdf, _grid, head);
        }

        // Live depth alone, in the head frame: the nearest sector return against the same thresholds, with the
        // last valid speed. Returns the head-space direction of that return.
        float2 EvaluateDepthOnly(double time)
        {
            Now = Path04 = Path08 = float.PositiveInfinity;
            if (!_hasDepth) return float2.zero;
            double age = time - _depth.Time;
            if (age > BumpConstants.DepthResultMaxAgeSeconds || age < -BumpConstants.DepthResultMaxAgeSeconds)
                return float2.zero;
            if (!_depth.Nearest(out float nearest, out int sector)) return float2.zero;

            Now = nearest;
            Path04 = nearest - _lastValidSpeed * BumpConstants.StrongHorizon;
            Path08 = nearest - _lastValidSpeed * BumpConstants.WarnHorizon;
            float angle = DepthFrameResult.SectorAngle(sector);
            return new float2(math.sin(angle), math.cos(angle));
        }

        static BumpLevel LevelFor(float now, float path04, float path08, float margin)
        {
            if (now < BumpConstants.StrongNow + margin || path04 < BumpConstants.PathClear + margin)
                return BumpLevel.Strong;
            if (now < BumpConstants.WarnNow + margin || path08 < BumpConstants.PathClear + margin)
                return BumpLevel.Warn;
            return BumpLevel.Clear;
        }

        static float2 HorizontalForward(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            float2 flat = f.xz;
            if (math.lengthsq(flat) < 1e-4f)
            {
                // Looking straight down or up: the top of the head stands in for forwards.
                float3 up = math.mul(rotation, new float3(0f, 1f, 0f));
                flat = f.y < 0f ? up.xz : -up.xz;
            }
            return math.normalizesafe(flat, new float2(0f, 1f));
        }

        void ReleaseRoom()
        {
            if (_pinned)
            {
                _sdfPin.Free();
                _topPin.Free();
                _hitPin.Free();
                _emptyPin.Free();
                _blockPin.Free();
                _activePin.Free();
                _pinned = false;
            }
            _dynamic = default;
            _grid = default;
            _sdf = null;
            _topHeight = null;
            _lastHit = null;
            _emptyState = null;
            _blockCount = null;
            _activeCount[0] = 0;
        }
    }
}
