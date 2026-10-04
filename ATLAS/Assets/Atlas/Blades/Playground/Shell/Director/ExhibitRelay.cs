using System;
using System.Collections.Generic;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// What the shell needs of an exhibit of beat 9: its task line, a signal when the line changes, a restart, and
    /// whether its tasks are done. <see cref="GluedExhibit"/> and <see cref="RoomsExhibit"/> are wrapped by
    /// <c>GluedExhibitLink</c> and <c>RoomsExhibitLink</c>; lane A's Slicer toybox (P2-A01) brings its own, or
    /// none, in which case its row has no task line.
    /// </summary>
    public interface IExhibitLink
    {
        /// <summary>The task line now; empty when the exhibit is not running.</summary>
        string Prompt { get; }
        event Action<string> PromptChanged;
        /// <summary>Starts the exhibit again. A no-op while it is not bound to a running space.</summary>
        void Restart();
        /// <summary>Every task of the exhibit is done.</summary>
        bool Completed { get; }
    }

    /// <summary>
    /// Joins the exhibit the walker chose in beat 9 to the shell (task P2-C01 wiring): the exhibit's task line goes
    /// to the <see cref="PromptQueue"/>, its completion ends beat 9, and entering Home drops it again. The menu's
    /// <c>Chosen</c> event starts it; the app calls <see cref="OnExhibitReady"/> once the space is entered and the
    /// exhibit bound to it. Engine-free. Main thread only.
    /// </summary>
    public sealed class ExhibitRelay
    {
        readonly Dictionary<ExhibitId, IExhibitLink> m_Links = new Dictionary<ExhibitId, IExhibitLink>();
        readonly PromptQueue m_Prompts;
        readonly Func<double> m_Clock;
        IExhibitLink m_Active;
        Action<string> m_OnPrompt;

        public ExhibitRelay(PromptQueue prompts, Func<double> clock)
        {
            if (prompts == null) throw new ArgumentNullException(nameof(prompts));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            m_Prompts = prompts;
            m_Clock = clock;
            m_OnPrompt = OnPromptChanged;
        }

        /// <summary>The exhibit chosen and not yet left; null in Home.</summary>
        public ExhibitId? ActiveId { get; private set; }

        /// <summary>The chosen exhibit's tasks are all done.</summary>
        public bool ActiveCompleted { get { return m_Active != null && m_Active.Completed; } }

        /// <summary>Registers the link of an exhibit (Glued, Impossible Rooms, Slicer). Replaces an earlier one.</summary>
        public void Register(ExhibitId id, IExhibitLink link)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            m_Links[id] = link;
        }

        /// <summary>The menu picked an exhibit: follow its task line and start it again.</summary>
        public void OnChosen(ExhibitId id)
        {
            Deactivate();
            IExhibitLink link;
            if (!m_Links.TryGetValue(id, out link)) return;
            m_Active = link;
            ActiveId = id;
            link.PromptChanged += m_OnPrompt;
            link.Restart();
        }

        /// <summary>The exhibit's space is entered and the exhibit is bound: its first line goes on the chip.</summary>
        public void OnExhibitReady()
        {
            if (m_Active != null) m_Prompts.SetExhibit(m_Active.Prompt, m_Clock());
        }

        /// <summary>Home was entered again (a return, or an operator command): the exhibit is over.</summary>
        public void Deactivate()
        {
            if (m_Active != null) m_Active.PromptChanged -= m_OnPrompt;
            if (m_Active != null) m_Prompts.Clear(PromptKind.Exhibit, m_Clock());
            m_Active = null;
            ActiveId = null;
        }

        void OnPromptChanged(string text)
        {
            m_Prompts.SetExhibit(text, m_Clock());
        }
    }
}
