using System;
using System.IO;
using Atlas.Link;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.App
{
    /// <summary>
    /// The scripted performance runs in the running app (Master Plan tasks P1-A14 and P1-A07): the per-space
    /// tour on the budget fixture, the 15-minute soak with depth on or off, and the ladder check. A run is
    /// started by the parameter <c>perf.run</c>, which the on-device menu and the Console both show, or by
    /// <c>-atlasPerf</c> on the command line. This class is the thin adapter: it reads the perf probe and
    /// the ladder, moves SpaceLab and the registry, and appends the rows to
    /// <c>logs/tour_&lt;session&gt;.csv</c>; schedule, statistics and verdict are in <see cref="PerfTourCore"/>.
    /// </summary>
    public sealed class PerfTour : IPerfTour, IAfterFrameStep, IPerfTourDriver
    {
        public const string ParamRun = "perf.run";
        public const string ParamVisitSeconds = "perf.visitSeconds";
        public const string ParamSoakMinutes = "perf.soakMinutes";
        public const string ParamSoakInterval = "perf.soakIntervalSeconds";
        public const string ParamPose = "perf.pose";
        public const string ParamGluedMode = "glued.mode";
        public const string ParamTelemetryMode = "telemetry.mode";
        public const string FilePrefix = "tour_";

        // A run asked for at launch waits for the app's own first room to be handed over, so that the
        // budget room is the last one set.
        const double StartDelaySeconds = 1.0;
        const double RoomRetrySeconds = 2.0;
        const int TelemetryModeHud = 2;

        readonly SyntheticPoseTrack _track = new SyntheticPoseTrack();
        readonly PerfRoomKeeper _rooms = new PerfRoomKeeper(RoomFixtures.BudgetRoom);
        AtlasApp _app;
        PerfTourCore _core;
        PerfTourOptions _options;
        AtlasPerfProbe _probe;
        ParamEntry _run, _visit, _minutes, _interval, _pose;

        int _wanted = -1;
        bool _writing;
        PerfRunKind _launchRun;
        double _firstFrame = -1.0;
        double _lastFrame = -1.0;
        double _lastRoomLoad = double.NegativeInfinity;
        string _roomError;
        string _path;
        int _writeErrors;
        bool _shuttingDown;

        // What a run changed and gives back when it ends.
        int _savedPin, _savedGluedMode, _savedGluedCopies, _savedTelemetryMode, _savedSource;
        IDepthSource _savedDepth;
        bool _depthOff;
        bool _syntheticPose;
        bool _trackPlaced;

        public bool Running { get { return _core != null && _core.Running; } }
        /// <summary>One line for an overlay.</summary>
        public string Status { get { return _core != null ? _core.Status : "perf: off"; } }
        /// <summary>The file of the last run, or null.</summary>
        public string FilePath { get { return _path; } }
        public PerfTourCore Core { get { return _core; } }

        public void Install(AtlasApp app)
        {
            if (app == null || app.Registry == null || app.Loop == null) return;
            _app = app;
            _core = new PerfTourCore(this);
            _core.Finished += OnFinished;
            _options = PerfTourOptions.Parse(Environment.GetCommandLineArgs());
            _launchRun = _options.Run;

            ParamRegistry registry = app.Registry;
            _run = registry.DeclareEnum(ParamRun, "perf", PerfTourOptions.RunNames, 0, value =>
            {
                if (!_writing) _wanted = value;
            }, true);
            _visit = registry.DeclareInt(ParamVisitSeconds, "perf", 5, 120,
                _options.VisitSeconds > 0 ? _options.VisitSeconds : (int)PerfSchedule.DefaultVisitSeconds, null, true);
            _minutes = registry.DeclareInt(ParamSoakMinutes, "perf", 1, 30,
                _options.SoakMinutes > 0 ? _options.SoakMinutes : (int)(PerfSchedule.DefaultSoakSeconds / 60f), null, true);
            _interval = registry.DeclareInt(ParamSoakInterval, "perf", 15, 300,
                _options.IntervalSeconds > 0 ? _options.IntervalSeconds : (int)PerfSchedule.DefaultSoakIntervalSeconds, null, true);
            _pose = registry.DeclareEnum(ParamPose, "perf", PerfTourOptions.PoseNames, _options.Pose >= 0 ? _options.Pose : 0, null, true);

            _probe = app.TelemetrySystems != null ? app.TelemetrySystems.Probe : null;
            if (_probe != null) _probe.RowReady += OnRow;
            app.Loop.AddAfterFrame(this);
        }

        public void Shutdown()
        {
            _shuttingDown = true;
            if (_core != null && _core.Running) _core.Abort("the app stopped");
            if (_probe != null) _probe.RowReady -= OnRow;
            _probe = null;
        }

        // ---- step 8b --------------------------------------------------------------------------------------

        public void AfterFrame(in FrameContext frame)
        {
            // The run keeps real time: the app clock is clamped per frame, and a hitch must count in full.
            double now = Time.realtimeSinceStartupAsDouble;
            float delta = _lastFrame >= 0.0 ? (float)(now - _lastFrame) : frame.DeltaTime;
            _lastFrame = now;
            if (_firstFrame < 0.0) _firstFrame = now;

            if (!_core.Running)
            {
                if (now - _firstFrame < StartDelaySeconds) return;
                if (_launchRun != PerfRunKind.None)
                {
                    PerfRunKind run = _launchRun;
                    _launchRun = PerfRunKind.None;
                    _run.Set((int)run);
                }
                int wanted = _wanted;
                _wanted = -1;
                if (wanted > 0) Start((PerfRunKind)wanted, now);
                return;
            }

            if (_wanted >= 0)
            {
                int wanted = _wanted;
                _wanted = -1;
                if (wanted == 0)
                {
                    _core.Abort("stopped by " + ParamRun);
                    return;
                }
                // One run at a time: the parameter shows the one in progress again.
                WriteRun((int)_core.Schedule.Kind);
            }

            if (_core.Phase == PerfTourPhase.Measuring && _probe != null)
            {
                PerfRing ring = _probe.Core.Ring;
                if (ring.Count > 0)
                    _core.AddFrame(ring.Recent(PerfRing.ChannelFrame, 0), ring.Recent(PerfRing.ChannelCpuMain, 0),
                        ring.Recent(PerfRing.ChannelCpuRender, 0), ring.Recent(PerfRing.ChannelGpu, 0), delta);
            }

            QualityLadder ladder = _app.Ladder;
            _core.Tick(now, ladder != null ? ladder.ThermalState : 0, ladder != null ? (int)ladder.Tier : (int)frame.Tier);
            if (_roomError != null && _core.Running)
            {
                string error = _roomError;
                _roomError = null;
                _core.Abort(error);
            }
        }

        void OnRow(TelemetryMessage row)
        {
            if (_core == null || _core.Phase != PerfTourPhase.Measuring) return;
            _core.AddSecond(row.Fps, row.DrawCalls, row.SetPass, row.TrisK * 1000L, row.MemMB, row.GcAllocB);
        }

        // ---- starting and ending a run --------------------------------------------------------------------

        void Start(PerfRunKind kind, double now)
        {
            AtlasApp app = _app;
            if (app.Spaces == null || app.Flow == null || app.Pose == null)
            {
                WriteRun(0);
                return;
            }
            // The eyes must draw: the numbers are those of the headset view.
            if (!app.Flow.InHeadset && !app.Flow.EnterHeadset())
            {
                Debug.LogError("Atlas: performance run refused, the headset view is not allowed (" + app.Flow.LastError + ")");
                WriteRun(0);
                return;
            }

            Func<SpaceId, bool> available = app.Spaces.IsAvailable;
            PerfSchedule schedule;
            if (kind == PerfRunKind.Tour) schedule = PerfSchedule.Tour(_visit.Int, available);
            else if (kind == PerfRunKind.Ladder) schedule = PerfSchedule.Ladder();
            else schedule = PerfSchedule.Soak(kind, _minutes.Int * 60f, _interval.Int, available);

            ParamRegistry registry = app.Registry;
            _savedPin = registry.GetInt(AppParams.PinTier, -1);
            _savedGluedMode = registry.GetInt(ParamGluedMode, 0);
            _savedGluedCopies = registry.GetInt(AppParams.GluedCopies, 5);
            _savedTelemetryMode = registry.GetInt(ParamTelemetryMode, 0);
            _savedSource = app.SourceSwitch != null ? app.SourceSwitch.Source : AppParams.SourceTracked;
            _savedDepth = null;
            _depthOff = false;
            _syntheticPose = false;
            _trackPlaced = false;
            _roomError = null;
            _lastRoomLoad = double.NegativeInfinity;
            _writeErrors = 0;
            // The walker's own room and its anchor come back when the run ends.
            _rooms.Begin(kind, app.Spaces.Room, app.Pose.Pipeline != null ? app.Pose.Pipeline.Anchor : null);

            // The per-space numbers are those of the Nominal rung; the soak leaves the ladder to itself.
            if (kind == PerfRunKind.Tour) app.Commands.SetQuality(0);
            else if (kind != PerfRunKind.Ladder) app.Commands.SetQuality(-1);
            // Rows must reach the file whatever the HUD-only mode says.
            if (_savedTelemetryMode == TelemetryModeHud) registry.Set(ParamTelemetryMode, 0);

            string poseName = ChoosePose(kind);
            string depthName = ChooseDepth(kind);

            AtlasTelemetry telemetry = app.TelemetrySystems;
            string session = telemetry != null ? telemetry.BeginSession() : SessionId.New();
            string folder = telemetry != null && telemetry.Files != null
                ? telemetry.Files.Directory
                : Path.Combine(Application.persistentDataPath, "logs");
            _path = Path.Combine(folder, FilePrefix + session + ".csv");

            var info = new PerfRunInfo
            {
                Session = session,
                Room = _rooms.RoomIdOfRun(app.Spaces.Room),
                Pose = poseName,
                Depth = depthName,
                Device = SystemInfo.deviceModel ?? "",
                Build = (Application.version ?? "") + (Debug.isDebugBuild ? " development" : " release") +
                        (Application.isEditor ? " editor" : ""),
            };
            _core.Begin(schedule, in info, now);
            Debug.Log("Atlas: performance run " + PerfSchedule.RunName(kind) + " started, " + schedule.Steps.Length +
                      " steps, about " + Mathf.CeilToInt(schedule.TotalSeconds) + " s, file " + _path);
        }

        string ChoosePose(PerfRunKind kind)
        {
            AtlasApp app = _app;
            bool tracking = app.Pose.Rig != null && app.Pose.Rig.HasAr;
            PerfPoseChoice pose = PerfTourOptions.ResolvePose(kind, (PerfPoseChoice)_pose.Int, tracking, true);
            if (pose == PerfPoseChoice.Replay)
            {
                // The switch finds the newest recorded walk; with none it says so and the built-in lap stands in.
                app.Registry.Set(AppParams.PoseSource, AppParams.SourceReplay);
                if (app.SourceSwitch != null && app.SourceSwitch.Source == AppParams.SourceReplay) return "replay";
                Debug.LogWarning("Atlas: performance run has no recorded walk to replay, the built-in lap is used");
                pose = PerfPoseChoice.Synthetic;
            }
            if (pose == PerfPoseChoice.Synthetic)
            {
                _track.SetRoom(app.Spaces.Room);
                app.Pose.Pipeline.Source = _track;
                _syntheticPose = true;
                return SyntheticPoseTrack.SourceName;
            }
            if (_savedSource == AppParams.SourceReplay) app.Registry.Set(AppParams.PoseSource, AppParams.SourceTracked);
            return "tracked";
        }

        string ChooseDepth(PerfRunKind kind)
        {
            ArPoseSystems pose = _app.Pose;
            bool lidar = pose.LidarDepth != null && ReferenceEquals(pose.Pipeline.Depth, pose.LidarDepth);
            if (kind != PerfRunKind.SoakDepthOff) return lidar ? "on" : "simulated";

            // Depth off for the heat comparison: the image is neither read nor asked of ARKit, so the LiDAR rests.
            _savedDepth = pose.Pipeline.Depth;
            pose.Pipeline.Depth = new NullDepthSource();
            if (pose.Rig != null && pose.Rig.Occlusion != null)
                pose.Rig.Occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            _depthOff = true;
            return "off";
        }

        void OnFinished(bool aborted)
        {
            AtlasApp app = _app;
            if (_depthOff)
            {
                _depthOff = false;
                app.Pose.Pipeline.Depth = _savedDepth;
                if (app.Pose.Rig != null) app.Pose.Rig.ApplyDepth();
            }
            if (!_shuttingDown)
            {
                ParamRegistry registry = app.Registry;
                app.Commands.SetQuality(_savedPin);
                registry.Set(ParamGluedMode, _savedGluedMode);
                registry.Set(AppParams.GluedCopies, _savedGluedCopies);
                registry.Set(ParamTelemetryMode, _savedTelemetryMode);
                // The head goes back to where it came from; the registry's own callback does it when the
                // value changes, and the switch is told directly when only the built-in lap was in its place.
                if (!registry.Set(AppParams.PoseSource, _savedSource) && _syntheticPose && app.SourceSwitch != null)
                    app.SourceSwitch.SetSource(_savedSource);
                RestoreRoom();
                app.Spaces.ReturnHome();
                WriteRun(0);
            }
            _syntheticPose = false;

            AtlasTelemetry telemetry = app.TelemetrySystems;
            if (telemetry != null && telemetry.Files != null) telemetry.Files.Flush();
            Debug.Log("Atlas: performance run " + (aborted ? "ended early" : "finished") + ": " + _core.Result + ", " +
                      _core.Rows.Count + " rows, file " + _path + (_writeErrors > 0 ? " (" + _writeErrors + " write errors)" : ""));
            if (_options != null && _options.Quit && !_shuttingDown) Application.Quit();
        }

        // The room the run replaced is handed over again as it first was: anchor to the pose pipeline, room
        // to the space host and through it to the room frame, Bump Guard and the room content.
        void RestoreRoom()
        {
            AtlasApp app = _app;
            RoomModel room;
            object anchor;
            if (!_rooms.TakeBack(app.Spaces.Room, out room, out anchor) || app.HandOver == null) return;
            // An anchor ARKit dropped during the run is gone; the room's own anchor pose stands in then.
            Transform transform = anchor as Transform;
            app.HandOver.Activate(room, transform != null ? transform : null);
        }

        void WriteRun(int value)
        {
            _writing = true;
            _run.Set(value);
            _writing = false;
        }

        // ---- IPerfTourDriver ------------------------------------------------------------------------------

        public bool PrepareRoom()
        {
            AtlasApp app = _app;
            RoomModel room = app.Spaces.Room;
            if (_rooms.NeedsRunRoom(room))
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - _lastRoomLoad < RoomRetrySeconds) return false;
                _lastRoomLoad = now;
                try
                {
                    app.HandOver.Activate(RoomFixtures.Load(RoomFixtures.BudgetRoom), null);
                }
                catch (Exception e) when (e is ArgumentException || e is FormatException)
                {
                    _roomError = "the budget room could not be loaded: " + e.Message;
                }
                return false;
            }
            if (app.LoopCore.RoomResetPending) return false;
            if (_syntheticPose && !_trackPlaced)
            {
                _track.SetRoom(room);
                _trackPlaced = true;
            }
            return true;
        }

        public void ApplyVariant(PerfVariant variant)
        {
            if (variant == PerfVariant.None) return;
            ParamRegistry registry = _app.Registry;
            ParamEntry mode = registry.Find(ParamGluedMode);
            if (mode != null)
            {
                string name = variant == PerfVariant.GluedT3 ? "t3" : "t2";
                for (int i = 0; i < mode.EnumCount; i++)
                    if (string.Equals(mode.EnumName(i), name, StringComparison.OrdinalIgnoreCase)) registry.Set(mode, i);
            }
            registry.Set(AppParams.GluedCopies, variant == PerfVariant.GluedT3 ? 3 : 5);
        }

        public bool InSpace(SpaceId space)
        {
            SpaceLab spaces = _app.Spaces;
            return spaces.State == SpaceHostState.Running && spaces.HasSpace && spaces.Current == space;
        }

        public bool RequestSpace(SpaceId space)
        {
            return _app.Spaces.RequestSwitch(space);
        }

        public void ForceTier(int tier)
        {
            // The Console's own path: the operator command writes thermal.pinTier.
            if (!_shuttingDown) _app.Commands.SetQuality(tier);
        }

        public void WriteLines(string text)
        {
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.AppendAllText(_path, text);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The rows still go out as events; the count is reported when the run ends.
                if (_writeErrors++ == 0) Debug.LogWarning("Atlas: the performance run's file cannot be written: " + e.Message);
            }
        }

        public void LogEvent(string ev, string dataJson)
        {
            AtlasTelemetry telemetry = _app.TelemetrySystems;
            if (telemetry != null && telemetry.Events != null) telemetry.Events.LogJson(ev, dataJson);
        }
    }

    /// <summary>Announces the tour to the composition root, as a space announces itself to SpaceRegistry.</summary>
    static class PerfTourRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register()
        {
            AppHooks.PerfTour = () => new PerfTour();
        }
    }
}
