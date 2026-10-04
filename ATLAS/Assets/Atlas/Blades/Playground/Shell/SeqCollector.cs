using System;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// The Single Ease Question at the end of the run (task P2-C17): "how easy was that, 1 (very difficult) to 7
    /// (very easy)". The walker answers aloud, the operator types it into the Console, and the Hub logs it as
    /// <c>seq {score}</c> (Master Plan section 11.2, lane D contract 2.12). This class asks the question and
    /// takes the entry back to close it; it never logs, so the answer is never in <c>events.jsonl</c> twice.
    /// Engine-free.
    /// </summary>
    public sealed class SeqCollector
    {
        public const int MinScore = 1;
        public const int MaxScore = 7;
        public const string QuestionText = "How easy was that? 1 is very difficult, 7 is very easy.";

        readonly PromptQueue _prompts;
        bool _asked;

        public SeqCollector(PromptQueue prompts)
        {
            if (prompts == null) throw new ArgumentNullException("prompts");
            _prompts = prompts;
        }

        /// <summary>True from <see cref="Ask"/> until an entry arrives.</summary>
        public bool Pending { get { return _asked; } }

        /// <summary>The last valid entry; 0 before the first.</summary>
        public int LastScore { get; private set; }

        public int Answers { get; private set; }

        /// <summary>Shows the question. Called at the end of the run, and again for another use case.</summary>
        public void Ask(double nowS)
        {
            _asked = true;
            _prompts.Enqueue(PromptKind.Seq, QuestionText, nowS);
        }

        /// <summary>An entry from the Console: a whole number from 1 to 7. Anything else is rejected and the question stays.</summary>
        public bool Submit(int score, double nowS)
        {
            if (score < MinScore || score > MaxScore) return false;
            LastScore = score;
            Answers++;
            _asked = false;
            _prompts.Clear(PromptKind.Seq, nowS);
            return true;
        }

        /// <summary>Routes SEQ entries of a feed to <see cref="Submit"/>.</summary>
        public Action<OperatorEntry> Bind(IOperatorEntryFeed feed, Func<double> clock)
        {
            if (feed == null) throw new ArgumentNullException("feed");
            if (clock == null) throw new ArgumentNullException("clock");
            Action<OperatorEntry> handler = e => { if (e.Kind == OperatorEntryKind.Seq) Submit(e.Value, clock()); };
            feed.Entered += handler;
            return handler;
        }
    }
}
