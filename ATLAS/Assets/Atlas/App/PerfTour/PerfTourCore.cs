using System;
using System.Collections.Generic;
using System.Text;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>What the tour's core asks of the running app. The adapter answers with SpaceLab, the registry and a file.</summary>
    public interface IPerfTourDriver
    {
        /// <summary>True once the budget room is the active room and the walker stands in it. Called every
        /// frame while the run prepares; the driver loads the room when another one, or none, is active.</summary>
        bool PrepareRoom();
        /// <summary>Sets the parameters of a layout before its space is entered, or while it is.</summary>
        void ApplyVariant(PerfVariant variant);
        /// <summary>True while the space is entered and no switch is under way.</summary>
        bool InSpace(SpaceId space);
        /// <summary>Asks SpaceLab for the space. False when refused; the core asks again.</summary>
        bool RequestSpace(SpaceId space);
        /// <summary>Forces a quality tier the way the Console does; -1 gives the ladder back.</summary>
        void ForceTier(int tier);
        /// <summary>Appends finished lines, line ends included, to the run's file.</summary>
        void WriteLines(string text);
        /// <summary>Logs an event whose data is one JSON object as text.</summary>
        void LogEvent(string ev, string dataJson);
    }

    /// <summary>The facts of a run that go into every row and into the head of the file.</summary>
    public struct PerfRunInfo
    {
        public string Session, Room, Pose, Depth, Device, Build;
    }

    public enum PerfTourPhase : byte
    {
        Idle = 0,
        /// <summary>Waiting for the budget room.</summary>
        Preparing = 1,
        /// <summary>Waiting for the step's space to be entered.</summary>
        Switching = 2,
        /// <summary>In the space, not measuring yet.</summary>
        Settling = 3,
        Measuring = 4,
        /// <summary>A tier was forced; waiting for it to be in force.</summary>
        Forcing = 5,
        /// <summary>The forced tier is in force and is held.</summary>
        Holding = 6,
    }

    /// <summary>
    /// The scripted performance runs as a state machine (Master Plan task P1-A14): plays a
    /// <see cref="PerfSchedule"/> step by step, collects the statistics of each space while it is measured,
    /// judges them against the must column and writes the rows. Engine-free and clock-free: time comes in
    /// with every call. While a step is measured nothing is allocated; rows are made between steps.
    /// </summary>
    public sealed class PerfTourCore
    {
        /// <summary>The budget room must be in force within this, or the run ends.</summary>
        public const double PrepareTimeoutSeconds = 30.0;
        /// <summary>A space must be entered within this, or its row fails with "notEntered".</summary>
        public const double SwitchTimeoutSeconds = 20.0;
        /// <summary>A forced tier that is not in force after this fails with "noEffect".</summary>
        public const double TierTimeoutSeconds = 5.0;

        readonly IPerfTourDriver _driver;
        readonly List<PerfStats> _pool = new List<PerfStats>(8);
        readonly PerfStats _total = new PerfStats();
        readonly StringBuilder _line = new StringBuilder(1024);
        readonly StringBuilder _scratch = new StringBuilder(128);
        readonly List<PerfSummary> _rows = new List<PerfSummary>(32);

        PerfSchedule _schedule;
        PerfRunInfo _info;
        int[] _statsOfStep;
        string[] _labels;
        int[] _labelStep;
        int _labelCount;

        PerfTourPhase _phase;
        int _step;
        double _phaseStart;
        double _runStart;
        double _now;
        bool _requested;
        bool _disturbed;
        float _effectMs;
        int _thermal = -1;
        float _toFair, _toSerious, _toCritical;
        int _failed, _incomplete;

        public PerfTourCore(IPerfTourDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            _driver = driver;
        }

        public PerfTourPhase Phase { get { return _phase; } }
        public bool Running { get { return _phase != PerfTourPhase.Idle; } }
        public PerfSchedule Schedule { get { return _schedule; } }
        /// <summary>The step in progress; the number of steps once the run has ended.</summary>
        public int StepIndex { get { return _step; } }
        /// <summary>The rows written by the last run, in order.</summary>
        public IReadOnlyList<PerfSummary> Rows { get { return _rows; } }
        /// <summary>"pass", "fail", "incomplete" or "aborted" for the last run; empty before the first.</summary>
        public string Result { get; private set; } = "";

        /// <summary>Raised when a run has ended, with true when it was cut short.</summary>
        public event Action<bool> Finished;

        /// <summary>One line for an overlay: what the run is doing. Allocates; not for every frame.</summary>
        public string Status
        {
            get
            {
                if (_phase == PerfTourPhase.Idle) return Result.Length > 0 ? "perf: " + Result : "perf: off";
                string run = PerfSchedule.RunName(_schedule.Kind);
                if (_phase == PerfTourPhase.Preparing) return "perf " + run + ": room";
                PerfStep step = _schedule.Steps[_step];
                return "perf " + run + " " + (_step + 1) + "/" + _schedule.Steps.Length + ": " + step.Label + " " +
                       _phase.ToString().ToLowerInvariant();
            }
        }

        /// <summary>Starts a run. The head of the file is written at once, so a run that ends early leaves its trace.</summary>
        public void Begin(PerfSchedule schedule, in PerfRunInfo info, double time)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (Running) throw new InvalidOperationException("A performance run is already in progress");
            _schedule = schedule;
            _info = info;
            _rows.Clear();
            Result = "";
            _failed = _incomplete = 0;
            _thermal = -1;
            _toFair = _toSerious = _toCritical = -1f;
            _now = time;

            // One set of statistics per label: a tour's steps each have their own, a soak's visits share two.
            PerfStep[] steps = schedule.Steps;
            _statsOfStep = new int[steps.Length];
            _labels = new string[steps.Length];
            _labelStep = new int[steps.Length];
            _labelCount = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                _statsOfStep[i] = -1;
                if (steps[i].Kind != PerfStepKind.Visit) continue;
                int k = Array.IndexOf(_labels, steps[i].Label, 0, _labelCount);
                if (k < 0)
                {
                    k = _labelCount++;
                    _labels[k] = steps[i].Label;
                    _labelStep[k] = i;
                    if (_pool.Count <= k) _pool.Add(new PerfStats());
                    _pool[k].Clear();
                }
                _statsOfStep[i] = k;
            }
            _total.Clear();

            _line.Length = 0;
            _line.Append(PerfTourCsv.Magic).Append('\n');
            PerfTourCsv.AppendFact(_line, "run", PerfSchedule.RunName(schedule.Kind));
            PerfTourCsv.AppendFact(_line, "session", info.Session);
            PerfTourCsv.AppendFact(_line, "room", info.Room);
            PerfTourCsv.AppendFact(_line, "pose", info.Pose);
            PerfTourCsv.AppendFact(_line, "depth", info.Depth);
            PerfTourCsv.AppendFact(_line, "device", info.Device);
            PerfTourCsv.AppendFact(_line, "build", info.Build);
            if (schedule.IntervalSeconds > 0f) PerfTourCsv.AppendFact(_line, "intervalSeconds", Number(schedule.IntervalSeconds));
            PerfTourCsv.AppendFact(_line, "plannedSeconds", Number(schedule.TotalSeconds));
            for (int i = 0; i < schedule.Notes.Length; i++) PerfTourCsv.AppendFact(_line, "note", schedule.Notes[i]);
            _line.Append(PerfTourCsv.Header).Append('\n');
            Flush();

            _driver.LogEvent(PerfTourCsv.BeginEvent, "{\"run\":\"" + PerfSchedule.RunName(schedule.Kind) + "\",\"steps\":" +
                                                     steps.Length + ",\"plannedSeconds\":" + Number(schedule.TotalSeconds) + "}");
            for (int i = 0; i < schedule.Notes.Length; i++) Note(schedule.Notes[i], false);

            _step = 0;
            _phase = PerfTourPhase.Preparing;
            _phaseStart = time;
        }

        /// <summary>
        /// Advances the run by one frame. time is seconds on a clock that never stops; thermal is 0 nominal to
        /// 3 critical; tier is the quality tier in force.
        /// </summary>
        public void Tick(double time, int thermal, int tier)
        {
            _now = time;
            if (_phase == PerfTourPhase.Idle) return;

            if (_phase == PerfTourPhase.Preparing)
            {
                if (_driver.PrepareRoom())
                {
                    _runStart = time;
                    TrackThermal(thermal);
                    StartStep(0);
                }
                else if (time - _phaseStart > PrepareTimeoutSeconds) Abort("the budget room did not become active");
                return;
            }

            TrackThermal(thermal);
            PerfStep step = _schedule.Steps[_step];
            double elapsed = time - _phaseStart;
            switch (_phase)
            {
                case PerfTourPhase.Switching:
                    if (_driver.InSpace(step.Space))
                    {
                        if (step.Kind == PerfStepKind.Enter) NextStep();
                        else Enter(PerfTourPhase.Settling);
                    }
                    else if (elapsed > SwitchTimeoutSeconds)
                    {
                        if (step.Kind == PerfStepKind.Visit) { _disturbed = true; EndVisit("notEntered"); }
                        else NextStep();
                    }
                    else if (!_requested) _requested = _driver.RequestSpace(step.Space);
                    break;

                case PerfTourPhase.Settling:
                    if (elapsed >= _schedule.SettleSeconds) Enter(PerfTourPhase.Measuring);
                    break;

                case PerfTourPhase.Measuring:
                    PerfStats stats = _pool[_statsOfStep[_step]];
                    stats.AddThermal(thermal);
                    _total.AddThermal(thermal);
                    if (!_driver.InSpace(step.Space)) _disturbed = true;
                    if (elapsed >= step.Seconds) EndVisit("leftSpace");
                    break;

                case PerfTourPhase.Forcing:
                    if (tier == step.Tier)
                    {
                        _effectMs = (float)(elapsed * 1000.0);
                        Enter(PerfTourPhase.Holding);
                    }
                    else if (elapsed > TierTimeoutSeconds)
                    {
                        _effectMs = -1f;
                        EndTier();
                    }
                    break;

                case PerfTourPhase.Holding:
                    if (elapsed >= step.Seconds) EndTier();
                    break;
            }
        }

        /// <summary>One frame's times, milliseconds, and what it lasted in seconds. Counted only while a step is measured.</summary>
        public void AddFrame(float frameMs, float cpuMainMs, float cpuRenderMs, float gpuMs, float deltaTime)
        {
            if (_phase != PerfTourPhase.Measuring) return;
            _pool[_statsOfStep[_step]].AddFrame(frameMs, cpuMainMs, cpuRenderMs, gpuMs, deltaTime);
            _total.AddFrame(frameMs, cpuMainMs, cpuRenderMs, gpuMs, deltaTime);
        }

        /// <summary>
        /// One second of the perf probe. Counted only when the whole second lies in the measured part of a
        /// step, so the frames of a switch never reach a row.
        /// </summary>
        public void AddSecond(float fps, int drawCalls, int setPass, long triangles, int memMB, long gcAllocBytes)
        {
            if (_phase != PerfTourPhase.Measuring || _now - _phaseStart < 1.0) return;
            _pool[_statsOfStep[_step]].AddSecond(fps, drawCalls, setPass, triangles, memMB, gcAllocBytes);
            _total.AddSecond(fps, drawCalls, setPass, triangles, memMB, gcAllocBytes);
        }

        /// <summary>Ends the run now. A soak still writes the rows of what it measured.</summary>
        public void Abort(string reason)
        {
            if (_phase == PerfTourPhase.Idle) return;
            bool begun = _phase != PerfTourPhase.Preparing;
            if (begun && _schedule.Aggregate) WriteAggregate(true);
            _line.Length = 0;
            PerfTourCsv.AppendFact(_line, "aborted", reason ?? "");
            Flush();
            End("aborted", true);
        }

        // ---- steps ----------------------------------------------------------------------------------------

        void StartStep(int index)
        {
            if (index == 0)
            {
                _line.Length = 0;
                PerfTourCsv.AppendFact(_line, "thermalAtStart", _thermal < 0 ? "unknown" : _thermal.ToString());
                Flush();
            }
            if (index >= _schedule.Steps.Length)
            {
                Complete();
                return;
            }
            _step = index;
            PerfStep step = _schedule.Steps[index];
            _disturbed = false;
            _requested = false;
            if (step.Kind == PerfStepKind.ForceTier)
            {
                _driver.ForceTier(step.Tier);
                Enter(PerfTourPhase.Forcing);
                return;
            }
            _driver.ApplyVariant(step.Variant);
            Enter(PerfTourPhase.Switching);
        }

        void NextStep() { StartStep(_step + 1); }

        void Enter(PerfTourPhase phase)
        {
            _phase = phase;
            _phaseStart = _now;
        }

        void EndVisit(string disturbedReason)
        {
            if (!_schedule.Aggregate)
            {
                int k = _statsOfStep[_step];
                WriteSpaceRow(k, true, _disturbed ? disturbedReason : null);
            }
            else if (_disturbed) Note(_schedule.Steps[_step].Label + ": " + disturbedReason + " at " + Number((float)(_now - _runStart)) + " s", true);
            NextStep();
        }

        void EndTier()
        {
            PerfStep step = _schedule.Steps[_step];
            PerfSummary s = NewRow("ladder", step.Label, step.Space, PerfVariant.None);
            s.Tier = step.Tier;
            s.EffectMs = _effectMs;
            s.Seconds = step.Seconds;
            s.ThermalMax = _thermal < 0 ? 0 : _thermal;
            PerfMust.JudgeTier(ref s);
            WriteRow(in s);
            NextStep();
        }

        void Complete()
        {
            if (_schedule.Aggregate) WriteAggregate(false);
            string result = _failed > 0 ? PerfMust.Fail : (_incomplete > 0 ? PerfMust.Incomplete : PerfMust.Pass);
            _line.Length = 0;
            PerfTourCsv.AppendFact(_line, "result", result);
            Flush();
            End(result, false);
        }

        void End(string result, bool aborted)
        {
            if (_schedule.Kind == PerfRunKind.Ladder) _driver.ForceTier(-1);
            Result = result;
            _step = _schedule.Steps.Length;
            _phase = PerfTourPhase.Idle;
            _driver.LogEvent(PerfTourCsv.EndEvent, "{\"run\":\"" + PerfSchedule.RunName(_schedule.Kind) + "\",\"result\":\"" +
                                                   result + "\",\"rows\":" + _rows.Count + "}");
            Action<bool> finished = Finished;
            if (finished != null) finished(aborted);
        }

        // ---- rows -----------------------------------------------------------------------------------------

        void WriteAggregate(bool aborted)
        {
            for (int k = 0; k < _labelCount; k++) WriteSpaceRow(k, false, null);
            PerfSummary total = NewRow("total", PerfSchedule.RunName(_schedule.Kind), SpaceId.Home, PerfVariant.None);
            total.Space = "";
            _total.Fill(ref total);
            // A soak that was cut short is judged as the rehearsal it became.
            PerfMust.JudgeSoak(ref total, aborted ? 0f : _schedule.TotalSeconds, _scratch);
            WriteRow(in total);
        }

        void WriteSpaceRow(int k, bool cold, string disturbed)
        {
            PerfStep step = _schedule.Steps[_labelStep[k]];
            PerfSummary s = NewRow("space", step.Label, step.Space, step.Variant);
            _pool[k].Fill(ref s);
            PerfMust.JudgeSpace(ref s, cold, _scratch);
            if (disturbed != null)
            {
                s.Failed = s.Failed.Length > 0 ? s.Failed + "|" + disturbed : disturbed;
                s.Result = PerfMust.Fail;
            }
            WriteRow(in s);
        }

        PerfSummary NewRow(string kind, string label, SpaceId space, PerfVariant variant)
        {
            PerfSummary s = PerfSummary.Empty;
            s.Run = PerfSchedule.RunName(_schedule.Kind);
            s.Kind = kind;
            s.Label = label;
            s.Space = SpaceIds.Code(space);
            s.Variant = PerfSchedule.VariantName(variant);
            s.Room = _info.Room ?? "";
            s.Pose = _info.Pose ?? "";
            s.Depth = _info.Depth ?? "";
            s.TimeToFair = _toFair;
            s.TimeToSerious = _toSerious;
            s.TimeToCritical = _toCritical;
            return s;
        }

        void WriteRow(in PerfSummary s)
        {
            _rows.Add(s);
            if (s.Result == PerfMust.Fail) _failed++;
            else if (s.Result == PerfMust.Incomplete) _incomplete++;
            _line.Length = 0;
            PerfTourCsv.AppendRow(_line, in s);
            Flush();
            _line.Length = 0;
            PerfTourCsv.AppendJson(_line, in s);
            _driver.LogEvent(PerfTourCsv.SummaryEvent, _line.ToString());
            _line.Length = 0;
        }

        void Note(string text, bool toFile)
        {
            if (toFile)
            {
                _line.Length = 0;
                PerfTourCsv.AppendFact(_line, "note", text);
                Flush();
            }
            _line.Length = 0;
            _line.Append("{\"text\":\"");
            int start = _line.Length;
            _line.Append(text);
            _line.Replace('"', '\'', start, _line.Length - start).Replace('\\', '/', start, _line.Length - start);
            _line.Append("\"}");
            _driver.LogEvent(PerfTourCsv.NoteEvent, _line.ToString());
            _line.Length = 0;
        }

        void TrackThermal(int thermal)
        {
            if (thermal == _thermal) return;
            bool first = _thermal < 0;
            _thermal = thermal;
            float t = (float)(_now - _runStart);
            if (thermal >= 1 && _toFair < 0f) _toFair = t;
            if (thermal >= 2 && _toSerious < 0f) _toSerious = t;
            if (thermal >= 3 && _toCritical < 0f) _toCritical = t;
            // The first reading is the start state, written with the head of the run.
            if (first) return;
            _line.Length = 0;
            PerfTourCsv.AppendFact(_line, "thermal", Number(t) + " " + thermal);
            Flush();
        }

        void Flush()
        {
            if (_line.Length == 0) return;
            _driver.WriteLines(_line.ToString());
            _line.Length = 0;
        }

        static string Number(float value)
        {
            return value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
