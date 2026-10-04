using System;
using System.Globalization;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// The state machine of the Geometry Playground run (Master Plan section 10.1.2, task P2-C01): ten beats, each
    /// waiting on its <see cref="IBeatStep"/>. A beat advances when its exit condition holds, or when the operator's
    /// <c>SceneCmd</c> asks for it, and every change logs <c>pg.beat {beat, name}</c>; the log of a complete run has
    /// ten of them. The beat's prompt line is handed to the <see cref="PromptQueue"/> at each entry, which is what
    /// makes it one prompt per beat. Engine-free; time is the caller's clock in seconds. Main thread only.
    /// <para>
    /// The <c>SceneCmd</c> forms (all to space S0): <c>variant "next"</c> advances one beat, and
    /// <c>args {"beat": N}</c> or <c>variant "beat:N"</c> enters beat N (1 to 10) directly, forward or back.
    /// </para>
    /// </summary>
    public sealed class PlaygroundDirectorCore
    {
        /// <summary>The event of a beat change; its data is <c>{"beat":int,"name":string}</c>.</summary>
        public const string BeatEvent = "pg.beat";
        /// <summary>The <c>SceneCmd</c> variant that advances one beat.</summary>
        public const string VariantNext = "next";
        /// <summary>The <c>SceneCmd</c> argument that enters a beat by its number.</summary>
        public const string ArgBeat = "beat";
        /// <summary>The same by variant, for a caller that has only the variant string: <c>"beat:7"</c>.</summary>
        public const string VariantBeatPrefix = "beat:";

        readonly IBeatStep[] m_Steps = new IBeatStep[PlaygroundBeats.Count + 1];
        readonly string[] m_Names = new string[PlaygroundBeats.Count + 1];
        readonly IEventLog m_Log;
        readonly PromptQueue m_Prompts;
        PlaygroundBeat m_Beat = PlaygroundBeat.None;

        /// <param name="log">The event log; null keeps the director silent.</param>
        /// <param name="prompts">Shows the line of each beat; null shows none.</param>
        /// <param name="names">The name of each beat, index 1 to 10 (a <c>Beats.asset</c> table); null or a
        /// missing entry takes <see cref="PlaygroundBeats.Name"/>.</param>
        public PlaygroundDirectorCore(IEventLog log, PromptQueue prompts, string[] names = null)
        {
            m_Log = log;
            m_Prompts = prompts;
            for (int i = 1; i <= PlaygroundBeats.Count; i++)
            {
                bool given = names != null && i < names.Length && !string.IsNullOrEmpty(names[i]);
                m_Names[i] = given ? names[i] : PlaygroundBeats.Name((PlaygroundBeat)i);
            }
        }

        /// <summary>The beat the run is in; None before <see cref="Begin"/> and after the last beat.</summary>
        public PlaygroundBeat Beat { get { return m_Beat; } }
        /// <summary>A beat is current.</summary>
        public bool Running { get { return m_Beat != PlaygroundBeat.None; } }
        /// <summary>The tenth beat completed: the run is over.</summary>
        public bool Finished { get; private set; }
        /// <summary>Beat changes logged since construction.</summary>
        public int BeatsEntered { get; private set; }

        /// <summary>A beat was entered, after its <see cref="IBeatStep.Enter"/> and its prompt.</summary>
        public event Action<PlaygroundBeat> BeatEntered;
        /// <summary>A beat is left, by its condition or by the operator: (left, next). next is None after the tenth beat.</summary>
        public event Action<PlaygroundBeat, PlaygroundBeat> BeatLeft;
        /// <summary>The tenth beat's condition held.</summary>
        public event Action RunFinished;

        /// <summary>The name logged for a beat.</summary>
        public string NameOf(PlaygroundBeat beat)
        {
            int i = (int)beat;
            return i >= 1 && i <= PlaygroundBeats.Count ? m_Names[i] : PlaygroundBeats.Name(PlaygroundBeat.None);
        }

        /// <summary>Sets the exit condition of a beat. A beat with no step ends only by the operator.</summary>
        public void SetStep(IBeatStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            int i = (int)step.Beat;
            if (i < 1 || i > PlaygroundBeats.Count) throw new ArgumentOutOfRangeException(nameof(step));
            m_Steps[i] = step;
        }

        public IBeatStep StepOf(PlaygroundBeat beat)
        {
            int i = (int)beat;
            return i >= 1 && i <= PlaygroundBeats.Count ? m_Steps[i] : null;
        }

        /// <summary>Starts the run at beat 1.</summary>
        public void Begin(double nowS)
        {
            Finished = false;
            Enter(PlaygroundBeat.Scan, nowS);
        }

        /// <summary>
        /// Once a frame: advances by one beat when the current beat's condition holds. One beat a call, so every
        /// beat is on screen for at least one frame even when its condition already holds (the pre-scan fast path).
        /// </summary>
        public void Update(double nowS)
        {
            if (!Running) return;
            IBeatStep step = m_Steps[(int)m_Beat];
            if (step != null && step.Complete) Advance(nowS);
        }

        /// <summary>Leaves the current beat for the next, or ends the run after the tenth. False when not running.</summary>
        public bool Advance(double nowS)
        {
            if (!Running) return false;
            PlaygroundBeat from = m_Beat;
            if (from == PlaygroundBeat.ReturnHome)
            {
                Leave(PlaygroundBeat.None);
                Finished = true;
                Action done = RunFinished;
                if (done != null) done();
                return true;
            }
            Enter((PlaygroundBeat)((int)from + 1), nowS);
            return true;
        }

        /// <summary>Enters a beat by number (the operator's jump). False for a number outside 1 to 10.</summary>
        public bool EnterBeat(int beat, double nowS)
        {
            if (beat < 1 || beat > PlaygroundBeats.Count) return false;
            Finished = false;
            Enter((PlaygroundBeat)beat, nowS);
            return true;
        }

        /// <summary>
        /// The operator's <c>SceneCmd</c> (variant "next", or args "beat"). True when it was a beat command.
        /// Every other command is not the director's and is left to the app's command router.
        /// </summary>
        public bool OnOperatorCommand(in OperatorCommand command, double nowS)
        {
            if (command.Kind != OperatorCommandKind.SwitchSpace || command.Space != 0) return false;
            if (command.Args != null && command.Args.Has(ArgBeat))
                return EnterBeat(command.Args.GetInt(ArgBeat), nowS);
            string variant = command.Variant;
            if (string.Equals(variant, VariantNext, StringComparison.OrdinalIgnoreCase))
                return Advance(nowS) || EnterBeat(1, nowS);
            if (variant != null && variant.StartsWith(VariantBeatPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (int.TryParse(variant.Substring(VariantBeatPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    return EnterBeat(n, nowS);
            }
            return false;
        }

        void Leave(PlaygroundBeat next)
        {
            PlaygroundBeat left = m_Beat;
            m_Beat = PlaygroundBeat.None;
            Action<PlaygroundBeat, PlaygroundBeat> handler = BeatLeft;
            if (handler != null && left != PlaygroundBeat.None) handler(left, next);
        }

        void Enter(PlaygroundBeat beat, double nowS)
        {
            if (m_Beat != PlaygroundBeat.None) Leave(beat);
            m_Beat = beat;
            BeatsEntered++;
            IBeatStep step = m_Steps[(int)beat];
            if (step != null) step.Enter();
            if (m_Prompts != null) m_Prompts.OnBeat((int)beat, nowS);
            if (m_Log != null)
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                m_Log.LogJson(BeatEvent, "{\"beat\":" + ((int)beat).ToString(c) + ",\"name\":\"" + NameOf(beat) + "\"}");
            }
            Action<PlaygroundBeat> handler = BeatEntered;
            if (handler != null) handler(beat);
        }
    }
}
