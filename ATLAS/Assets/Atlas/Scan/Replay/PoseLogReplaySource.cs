using System;
using System.IO;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>How a replay picks the sample for a frame.</summary>
    public enum ReplayMode : byte
    {
        /// <summary>By the clock: the last sample at or before the time since the replay started.</summary>
        RealTime = 0,
        /// <summary>The next sample on each call, whatever the clock says. For tests.</summary>
        EveryFrame = 1,
    }

    /// <summary>
    /// A recorded walk played back as the head (contract README F9, Master Plan task P1-C01). Engine-free.
    /// The sample is delivered in the log's own frame; the rig is not driven by AR Foundation.
    /// </summary>
    public sealed class PoseLogReplaySource : IPoseSource
    {
        // Seconds. Half of the last decimal the file keeps, so a 60 Hz clock lands on its 60 Hz sample.
        const double ClockSlack = 0.5e-6;

        readonly PoseLog _log;
        readonly HeadPose[] _samples;
        readonly ReplayMode _mode;
        readonly bool _loop;

        double _start;
        bool _started;
        int _cursor;        // RealTime: index of the last sample at or before the replay time. EveryFrame: next index
        double _lastLocal;

        public PoseLogReplaySource(PoseLog log, ReplayMode mode = ReplayMode.RealTime, bool loop = false)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            _log = log;
            _samples = log.Samples ?? new HeadPose[0];
            _mode = mode;
            _loop = loop;
        }

        /// <summary>Reads a pose-log file. Throws what File.OpenText and PoseLog.Read throw.</summary>
        public static PoseLogReplaySource FromFile(string path, ReplayMode mode = ReplayMode.RealTime, bool loop = false)
        {
            using (StreamReader reader = File.OpenText(path))
                return new PoseLogReplaySource(PoseLog.Read(reader), mode, loop);
        }

        public string Name { get { return "poselog"; } }
        public PoseFrameKind Frame { get { return _log.Frame; } }
        public bool DrivesRig { get { return false; } }

        public PoseLog Log { get { return _log; } }
        public ReplayMode Mode { get { return _mode; } }
        public bool Loop { get { return _loop; } }
        /// <summary>The log has no further sample: TryRead returns false until Restart.</summary>
        public bool Ended { get; private set; }
        /// <summary>Seconds on the log's clock of the sample last delivered.</summary>
        public double SampleTime { get; private set; }
        /// <summary>Index of the sample last delivered (the earlier one when interpolating); -1 before the first.</summary>
        public int SampleIndex { get; private set; } = -1;
        /// <summary>How many times a looping replay has started again.</summary>
        public int LoopCount { get; private set; }

        /// <summary>
        /// RealTime only. When true the pose is interpolated between the two samples around the replay time, so
        /// a log replays smoothly at any frame rate. Two samples further apart than PoseFilter.JumpDistance, or
        /// with different tracking states, are never blended: a recorded jump stays a jump. Default false.
        /// </summary>
        public bool Interpolate { get; set; }

        /// <summary>The next TryRead starts the log again from its first sample.</summary>
        public void Restart()
        {
            _started = false;
            _cursor = 0;
            Ended = false;
            SampleIndex = -1;
            SampleTime = 0.0;
        }

        public bool TryRead(double time, float deltaTime, out HeadPose pose)
        {
            pose = default(HeadPose);
            int count = _samples.Length;
            if (count == 0 || Ended) { Ended = true; return false; }
            return _mode == ReplayMode.EveryFrame ? ReadNext(time, count, out pose) : ReadByClock(time, count, out pose);
        }

        bool ReadNext(double time, int count, out HeadPose pose)
        {
            pose = default(HeadPose);
            if (_cursor >= count)
            {
                if (!_loop) { Ended = true; return false; }
                _cursor = 0;
                LoopCount++;
            }
            Deliver(_cursor, time, out pose);
            _cursor++;
            return true;
        }

        bool ReadByClock(double time, int count, out HeadPose pose)
        {
            pose = default(HeadPose);
            if (!_started)
            {
                _started = true;
                _start = time;
                _cursor = 0;
                _lastLocal = 0.0;
            }
            double first = _samples[0].Time;
            double duration = _samples[count - 1].Time - first;
            double local = time - _start;
            if (local < 0.0) local = 0.0;

            // The clock is a sum of frame times: the last sample must not be missed by a rounding error.
            if (local > duration && local <= duration + ClockSlack) local = duration;
            if (local > duration)
            {
                if (!_loop || !(duration > 0.0))
                {
                    if (!_loop) { Ended = true; return false; }
                    local = 0.0;
                }
                else
                {
                    double laps = Math.Floor(local / duration);
                    local -= laps * duration;
                    _start += laps * duration;
                    LoopCount += (int)laps;
                }
            }
            // Time only moves forward within a lap, so the cursor does too. A new lap, or a clock that was set
            // back, starts the search again.
            if (local < _lastLocal) _cursor = 0;
            _lastLocal = local;

            double t = first + local;
            int i = _cursor;
            while (i + 1 < count && _samples[i + 1].Time <= t + ClockSlack) i++;
            _cursor = i;

            if (Interpolate && i + 1 < count && CanBlend(in _samples[i], in _samples[i + 1]))
            {
                double span = _samples[i + 1].Time - _samples[i].Time;
                float u = span > 0.0 ? (float)math.saturate((t - _samples[i].Time) / span) : 0f;
                pose = new HeadPose(time,
                    math.lerp(_samples[i].Position, _samples[i + 1].Position, u),
                    math.slerp(_samples[i].Rotation, _samples[i + 1].Rotation, u),
                    _samples[i].Tracking);
                SampleIndex = i;
                SampleTime = t - first;
                return true;
            }
            Deliver(i, time, out pose);
            return true;
        }

        static bool CanBlend(in HeadPose a, in HeadPose b)
        {
            return a.Tracking == b.Tracking && math.distance(a.Floor, b.Floor) <= PoseFilter.JumpDistance;
        }

        void Deliver(int index, double time, out HeadPose pose)
        {
            HeadPose s = _samples[index];
            pose = new HeadPose(time, s.Position, s.Rotation, s.Tracking);
            SampleIndex = index;
            SampleTime = s.Time - _samples[0].Time;
        }
    }
}
