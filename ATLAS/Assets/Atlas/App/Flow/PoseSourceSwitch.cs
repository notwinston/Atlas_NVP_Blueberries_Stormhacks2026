using System;
using System.IO;
using Atlas.Scan;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// A pose source played at another speed: its clock runs at Speed times the app's. Wraps a pose-log
    /// replay so that replay.speed needs nothing from the replay source itself.
    /// </summary>
    public sealed class ReplayClock : IPoseSource
    {
        readonly IPoseSource _inner;
        double _time;

        public ReplayClock(IPoseSource inner)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            _inner = inner;
        }

        /// <summary>0 holds the walk still; 1 is real time. Clamped to 0 .. 4.</summary>
        public float Speed { get; set; } = 1f;
        public IPoseSource Inner { get { return _inner; } }
        /// <summary>Seconds on the replay's own clock.</summary>
        public double Time { get { return _time; } }

        public string Name { get { return _inner.Name; } }
        public PoseFrameKind Frame { get { return _inner.Frame; } }
        public bool DrivesRig { get { return _inner.DrivesRig; } }

        public bool TryRead(double time, float deltaTime, out HeadPose pose)
        {
            float speed = Speed < 0f ? 0f : (Speed > 4f ? 4f : Speed);
            float step = deltaTime * speed;
            _time += step;
            HeadPose sample;
            if (!_inner.TryRead(_time, step, out sample))
            {
                pose = sample;
                return false;
            }
            // The sample carries the app's time, not the replay's.
            pose = new HeadPose(time, sample.Position, sample.Rotation, sample.Tracking);
            return true;
        }
    }

    /// <summary>
    /// pose.source, replay.speed and replay.loop (contract README F9): chooses between the platform's own
    /// head (the tracked camera, or the keyboard walker where there is no AR), a recorded walk and the
    /// keyboard walker. A replay with no file to play falls back to the platform's source and says so.
    /// </summary>
    public sealed class PoseSourceSwitch
    {
        public const string WalkPattern = "walk_*.poselog";

        readonly ArPoseSystems _pose;
        readonly string[] _folders;
        ReplayClock _clock;
        PoseLogReplaySource _replay;
        float _speed = 1f;
        bool _loop = true;
        int _source = AppParams.SourceTracked;

        /// <param name="folders">Where recorded walks are looked for, in order.</param>
        public PoseSourceSwitch(ArPoseSystems pose, params string[] folders)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            _pose = pose;
            _folders = folders ?? new string[0];
        }

        /// <summary>A file to replay in place of the newest recorded walk; null for the newest.</summary>
        public string ReplayPath { get; set; }
        public int Source { get { return _source; } }
        /// <summary>The walk being replayed, or null.</summary>
        public PoseLog Log { get { return _replay != null ? _replay.Log : null; } }
        /// <summary>Why the last replay did not start, or null.</summary>
        public string LastError { get; private set; }
        public IEventLog Events { get; set; }

        /// <summary>Raised when a replay has started, with its log: the caller activates the log's room.</summary>
        public event Action<PoseLog> ReplayStarted;

        public void SetSource(int source)
        {
            LastError = null;
            IPoseSource before = _pose.Pipeline.Source;
            if (source == AppParams.SourceReplay && StartReplay())
            {
                _source = AppParams.SourceReplay;
                return;
            }
            _replay = null;
            _clock = null;
            _pose.StopReplay();
            if (source == AppParams.SourceWalker)
            {
                // The walker takes over where the head was, facing the same way, so the switch (from the
                // Station's Map page, say) does not throw the view across the room.
                if (!ReferenceEquals(before, _pose.Walker))
                {
                    HeadPose head = _pose.Pipeline.Raw;
                    _pose.Walker.Place(head.Floor, head.Yaw);
                }
                _pose.Pipeline.Source = _pose.Walker;
                _source = AppParams.SourceWalker;
            }
            else
            {
                _source = AppParams.SourceTracked;
            }
            Note("pose.source", SourceName(_source));
        }

        public void SetSpeed(float speed)
        {
            _speed = speed;
            if (_clock != null) _clock.Speed = speed;
        }

        public void SetLoop(bool loop)
        {
            if (_loop == loop) return;
            _loop = loop;
            // The replay source takes its loop flag at construction: the same walk starts again.
            if (_replay != null) Play(new PoseLogReplaySource(_replay.Log, ReplayMode.RealTime, loop));
        }

        bool StartReplay()
        {
            string path = !string.IsNullOrEmpty(ReplayPath) ? ReplayPath : FindNewestWalk(_folders);
            if (path == null)
            {
                LastError = "no recorded walk (" + WalkPattern + ") was found";
                Note("pose.replayFailed", LastError);
                return false;
            }
            PoseLogReplaySource replay;
            try
            {
                replay = PoseLogReplaySource.FromFile(path, ReplayMode.RealTime, _loop);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException ||
                                      e is ArgumentException || e is NotSupportedException)
            {
                LastError = e.Message;
                Note("pose.replayFailed", LastError);
                return false;
            }
            Play(replay);
            Note("pose.source", "replay " + Path.GetFileName(path));
            Action<PoseLog> started = ReplayStarted;
            if (started != null) started(replay.Log);
            return true;
        }

        void Play(PoseLogReplaySource replay)
        {
            replay.Restart();
            _replay = replay;
            _clock = new ReplayClock(replay) { Speed = _speed };
            _pose.Pipeline.Source = _clock;
        }

        void Note(string ev, string value)
        {
            IEventLog events = Events;
            if (events != null) events.Log(ev, "value", value);
        }

        public static string SourceName(int source)
        {
            return source == AppParams.SourceReplay ? "replay" : (source == AppParams.SourceWalker ? "walker" : "tracked");
        }

        /// <summary>The most recently written walk_*.poselog in the folders; the later name wins a tie. Null when there is none.</summary>
        public static string FindNewestWalk(string[] folders)
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            if (folders == null) return null;
            for (int f = 0; f < folders.Length; f++)
            {
                string folder = folders[f];
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder, WalkPattern); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { continue; }
                for (int i = 0; i < files.Length; i++)
                {
                    DateTime written = File.GetLastWriteTimeUtc(files[i]);
                    if (best == null || written > bestTime ||
                        written == bestTime && string.CompareOrdinal(Path.GetFileName(files[i]), Path.GetFileName(best)) > 0)
                    {
                        best = files[i];
                        bestTime = written;
                    }
                }
            }
            return best;
        }
    }
}
