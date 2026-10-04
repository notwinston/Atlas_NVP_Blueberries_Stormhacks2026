using System;
using System.Collections.Generic;

namespace Atlas.Blades.Playground
{
    /// <summary>What a prompt is for. Fms, Seq and Stop are operator-facing questions and alerts; the rest guide the walker.</summary>
    public enum PromptKind : byte { Beat = 0, Probe = 1, Seq = 2, Fms = 3, Stop = 4 }

    /// <summary>One line of guidance shown on the prompt chip (Master Plan section 10.1.3, "Confirm / next beat": A, prompt chip, click).</summary>
    public sealed class Prompt
    {
        public int Id;
        public PromptKind Kind;
        /// <summary>The beat of <see cref="BeatPrompts"/> for a Beat prompt; 0 otherwise.</summary>
        public int Beat;
        public string Text;
        /// <summary>Seconds on screen before it goes away on its own; 0 stays until confirmed or replaced.</summary>
        public double ExpireSeconds;
        /// <summary>When it was shown (the caller's clock), set by the queue.</summary>
        public double ShownAtS;

        /// <summary>Fms and Stop jump the line: they are shown at once and put what they replace back in front.</summary>
        public bool Urgent { get { return Kind == PromptKind.Fms || Kind == PromptKind.Stop; } }
    }

    /// <summary>The view of the prompt chip: the shell binds a caption chip or a test double. Main thread only.</summary>
    public interface IPromptSink
    {
        void Show(Prompt prompt);
        void Hide();
    }

    /// <summary>
    /// The tutorial prompts of the Playground (task P2-C09). One prompt per beat: entering a beat drops every
    /// older beat prompt and shows that beat's line. The comfort question (<see cref="PromptKind.Fms"/>) and the
    /// stop alert (<see cref="PromptKind.Stop"/>) are urgent: they take the chip at once and the prompt they
    /// replaced comes back when they are done. The walker's confirm (A, the prompt chip under gaze dwell, a
    /// headset click) calls <see cref="Confirm"/>. Engine-free; time is the caller's clock in seconds.
    /// </summary>
    public sealed class PromptQueue
    {
        readonly IPromptSink _sink;
        readonly List<Prompt> _waiting = new List<Prompt>(8);
        readonly Dictionary<int, int> _shownPerBeat = new Dictionary<int, int>();
        Prompt _current;
        int _nextId = 1;

        public PromptQueue(IPromptSink sink)
        {
            _sink = sink;
        }

        /// <summary>The prompt on the chip, or null.</summary>
        public Prompt Current { get { return _current; } }

        /// <summary>Prompts waiting behind the current one.</summary>
        public int WaitingCount { get { return _waiting.Count; } }

        /// <summary>How many times the prompt of this beat was shown. One per beat visit is the rule.</summary>
        public int ShownForBeat(int beat)
        {
            int n;
            return _shownPerBeat.TryGetValue(beat, out n) ? n : 0;
        }

        /// <summary>The walker or the operator entered a beat: shows its prompt, dropping the older beat prompts.</summary>
        public Prompt OnBeat(int beat, double nowS)
        {
            string text = BeatPrompts.TextFor(beat);
            if (text == null) return null;
            DropBeatPrompts();
            Prompt p = Make(PromptKind.Beat, beat, text, 0.0);
            Place(p, nowS);
            return p;
        }

        /// <summary>Queues a prompt of any kind (the probe's "look at where you started", the SEQ question).</summary>
        public Prompt Enqueue(PromptKind kind, string text, double nowS, double expireSeconds = 0.0)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Prompt p = Make(kind, 0, text, expireSeconds);
            Place(p, nowS);
            return p;
        }

        /// <summary>The walker confirmed. A Stop prompt does not answer to it: only <see cref="Clear"/> removes that.</summary>
        public bool Confirm(double nowS)
        {
            if (_current == null || _current.Kind == PromptKind.Stop) return false;
            Next(nowS);
            return true;
        }

        /// <summary>Removes every prompt of a kind, current or waiting. True when anything went.</summary>
        public bool Clear(PromptKind kind, double nowS)
        {
            bool any = _waiting.RemoveAll(p => p.Kind == kind) > 0;
            if (_current != null && _current.Kind == kind)
            {
                Next(nowS);
                any = true;
            }
            return any;
        }

        /// <summary>Takes prompts off the chip when their time ran out.</summary>
        public void Tick(double nowS)
        {
            while (_current != null && _current.ExpireSeconds > 0.0 && nowS - _current.ShownAtS >= _current.ExpireSeconds)
                Next(nowS);
        }

        Prompt Make(PromptKind kind, int beat, string text, double expire)
        {
            return new Prompt { Id = _nextId++, Kind = kind, Beat = beat, Text = text, ExpireSeconds = expire };
        }

        void DropBeatPrompts()
        {
            _waiting.RemoveAll(p => p.Kind == PromptKind.Beat);
            // A beat prompt on the chip is stale the moment the next beat starts; an urgent one stays.
            if (_current != null && _current.Kind == PromptKind.Beat) _current = null;
        }

        void Place(Prompt p, double nowS)
        {
            if (_current == null)
            {
                Show(p, nowS);
                return;
            }
            if (p.Urgent && (!_current.Urgent || p.Kind > _current.Kind))
            {
                // The prompt it displaces is put back in front, unless it is urgent too (an old Fms question).
                if (!_current.Urgent) _waiting.Insert(0, _current);
                Show(p, nowS);
                return;
            }
            // Behind the others of its rank: urgent ones before the plain ones.
            int at = _waiting.Count;
            if (p.Urgent)
            {
                at = 0;
                while (at < _waiting.Count && _waiting[at].Urgent) at++;
            }
            _waiting.Insert(at, p);
        }

        void Next(double nowS)
        {
            _current = null;
            if (_waiting.Count > 0)
            {
                Prompt p = _waiting[0];
                _waiting.RemoveAt(0);
                Show(p, nowS);
            }
            else if (_sink != null) _sink.Hide();
        }

        void Show(Prompt p, double nowS)
        {
            p.ShownAtS = nowS;
            _current = p;
            if (p.Kind == PromptKind.Beat)
            {
                int n;
                _shownPerBeat.TryGetValue(p.Beat, out n);
                _shownPerBeat[p.Beat] = n + 1;
            }
            if (_sink != null) _sink.Show(p);
        }
    }
}
