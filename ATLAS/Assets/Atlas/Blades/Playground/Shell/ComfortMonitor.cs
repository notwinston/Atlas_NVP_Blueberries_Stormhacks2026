using System;
using Atlas.Link;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// The comfort question of the Playground (task P2-C09): asks for the FMS score every 60 s and reacts to the
    /// entries the operator typed, with the stop line when one reaches <c>pg.fmsStop</c> (Master Plan section
    /// 10.1.4). Engine-free; time is the caller's clock in seconds.
    /// <para>One writer: the Hub writes the <c>fms</c> line and the <c>pg.fmsAlert</c> line (lane D contract 2.12,
    /// Hub-made events). This class never logs either, so an entry is never in <c>events.jsonl</c> twice. The
    /// operator alert is the Console's.</para>
    /// <para>The question is spoken by the operator and shown on the prompt chip as a reminder; the walker answers
    /// aloud and the operator types the number into the Console. A late frame never makes two questions in a row:
    /// the next one is always a full minute after the one asked.</para>
    /// </summary>
    public sealed class ComfortMonitor
    {
        public const string StopParamName = "pg.fmsStop";
        public const string ParamGroup = "playground";
        public const int MinScore = 0;
        public const int MaxScore = 20;
        /// <summary>Time between questions, seconds. The acceptance window is 60 s ± 5 s.</summary>
        public const double IntervalSeconds = 60.0;
        public const double ToleranceSeconds = 5.0;
        /// <summary>
        /// Provisional stop value. Open item 6 of the Master Plan (section 14): the older documents offer 4
        /// (Brief section 8.1), the performance research 10 (R5 D2). The cautious one is the default until the
        /// team measures it (<c>P4-C02</c>); the Console slider changes it live.
        /// </summary>
        public const int DefaultStop = 4;
        /// <summary>The comfort question stays on the chip this long unless it is confirmed.</summary>
        public const double PromptSeconds = 10.0;
        public const string QuestionText = "Comfort check: say your score, 0 to 20.";
        public const string StopText = "Stop. Headset off, sit down, water.";

        readonly PromptQueue _prompts;
        readonly Func<int> _stop;
        bool _running, _paused;
        double _nextAskS, _pausedAtS;

        /// <param name="prompts">Shows the question and the stop line.</param>
        /// <param name="stop">Reads <c>pg.fmsStop</c>; see <see cref="DeclareStopParam"/>.</param>
        public ComfortMonitor(PromptQueue prompts, Func<int> stop)
        {
            if (prompts == null) throw new ArgumentNullException("prompts");
            if (stop == null) throw new ArgumentNullException("stop");
            _prompts = prompts;
            _stop = stop;
        }

        /// <summary>Declares <c>pg.fmsStop</c> (0 to 20, integer) so the Console shows it. Safe to call twice.</summary>
        public static ParamEntry DeclareStopParam(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            return registry.DeclareInt(StopParamName, ParamGroup, MinScore, MaxScore, DefaultStop);
        }

        /// <summary>The same, wrapped for the constructor: reads the entry on each use, with no lookup by name.</summary>
        public static Func<int> StopReader(ParamEntry stop)
        {
            if (stop == null) throw new ArgumentNullException("stop");
            return () => stop.Int;
        }

        public bool IsRunning { get { return _running && !_paused; } }
        public int AskCount { get; private set; }
        public double LastAskS { get; private set; }
        /// <summary>The last valid entry; -1 before the first.</summary>
        public int LastScore { get; private set; } = -1;
        /// <summary>True from an entry at or above the stop value until a lower one or <see cref="Resume"/>.</summary>
        public bool AlertActive { get; private set; }
        public int StopValue { get { return _stop(); } }

        /// <summary>Raised with (score, stop) when an entry reaches the stop value: the phone's own reaction (the stop line on the chip).</summary>
        public event Action<int, int> Alerted;

        /// <summary>The walker is in the headset and the run is on: the first question comes one interval later.</summary>
        public void Start(double nowS)
        {
            _running = true;
            _paused = false;
            AskCount = 0;
            AlertActive = false;
            _nextAskS = nowS + IntervalSeconds;
        }

        /// <summary>Ends the run: no more questions.</summary>
        public void Stop()
        {
            _running = false;
            _paused = false;
        }

        /// <summary>The headset came off: the clock of the next question stops with it.</summary>
        public void Pause(double nowS)
        {
            if (!_running || _paused) return;
            _paused = true;
            _pausedAtS = nowS;
        }

        /// <summary>Headset back on after a pause: the next question is as far away as it was, and a stop alert is cleared.</summary>
        public void Resume(double nowS)
        {
            if (!_running) return;
            if (_paused)
            {
                _nextAskS += nowS - _pausedAtS;
                _paused = false;
            }
            ClearAlert(nowS);
        }

        /// <summary>Once per frame. Asks when a minute has passed since the last question.</summary>
        public void Tick(double nowS)
        {
            if (!_running || _paused || nowS < _nextAskS) return;
            AskCount++;
            LastAskS = nowS;
            // On time, the next one is exactly one interval after this one's due time; after a long hitch it
            // restarts from now, so the questions never come in a burst.
            _nextAskS = nowS - _nextAskS <= ToleranceSeconds ? _nextAskS + IntervalSeconds : nowS + IntervalSeconds;
            _prompts.Enqueue(PromptKind.Fms, QuestionText, nowS, PromptSeconds);
        }

        /// <summary>
        /// An entry the Console typed, as it reaches the phone. Rejects anything but a whole number from 0 to 20.
        /// A valid one answers the question on the chip; at or above <c>pg.fmsStop</c> it also shows the stop line
        /// and raises <see cref="Alerted"/>. It writes no event: the Hub has already logged the entry.
        /// </summary>
        public bool Submit(int score, double nowS)
        {
            if (score < MinScore || score > MaxScore) return false;
            int stop = _stop();
            LastScore = score;
            _prompts.Clear(PromptKind.Fms, nowS);
            if (score >= stop)
            {
                AlertActive = true;
                _prompts.Enqueue(PromptKind.Stop, StopText, nowS);
                Action<int, int> handler = Alerted;
                if (handler != null) handler(score, stop);
            }
            else ClearAlert(nowS);
            return true;
        }

        /// <summary>Routes FMS entries of a feed to <see cref="Submit"/>; returns the handler so a caller can unsubscribe.</summary>
        public Action<OperatorEntry> Bind(IOperatorEntryFeed feed, Func<double> clock)
        {
            if (feed == null) throw new ArgumentNullException("feed");
            if (clock == null) throw new ArgumentNullException("clock");
            Action<OperatorEntry> handler = e => { if (e.Kind == OperatorEntryKind.Fms) Submit(e.Value, clock()); };
            feed.Entered += handler;
            return handler;
        }

        void ClearAlert(double nowS)
        {
            if (!AlertActive) return;
            AlertActive = false;
            _prompts.Clear(PromptKind.Stop, nowS);
        }
    }
}
