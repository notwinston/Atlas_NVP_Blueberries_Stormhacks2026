using System;
using Atlas.Blades.Playground.Shell;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>Joins <see cref="GluedExhibit"/> to the shell's <see cref="ExhibitRelay"/> (task P2-C01 wiring).</summary>
    public sealed class GluedExhibitLink : IExhibitLink
    {
        readonly GluedExhibit m_Exhibit;

        public GluedExhibitLink(GluedExhibit exhibit)
        {
            if (exhibit == null) throw new ArgumentNullException(nameof(exhibit));
            m_Exhibit = exhibit;
        }

        public string Prompt { get { return m_Exhibit.Prompt; } }
        public event Action<string> PromptChanged
        {
            add { m_Exhibit.PromptChanged += value; }
            remove { m_Exhibit.PromptChanged -= value; }
        }
        public void Restart() { m_Exhibit.Restart(); }
        public bool Completed { get { return m_Exhibit.Core != null && m_Exhibit.Core.Task == GluedTask.Done; } }
    }

    /// <summary>Joins <see cref="RoomsExhibit"/> to the shell's <see cref="ExhibitRelay"/> (task P2-C01 wiring).</summary>
    public sealed class RoomsExhibitLink : IExhibitLink
    {
        readonly RoomsExhibit m_Exhibit;

        public RoomsExhibitLink(RoomsExhibit exhibit)
        {
            if (exhibit == null) throw new ArgumentNullException(nameof(exhibit));
            m_Exhibit = exhibit;
        }

        public string Prompt { get { return m_Exhibit.Prompt; } }
        public event Action<string> PromptChanged
        {
            add { m_Exhibit.PromptChanged += value; }
            remove { m_Exhibit.PromptChanged -= value; }
        }
        public void Restart() { m_Exhibit.Restart(); }
        public bool Completed { get { return m_Exhibit.Core != null && m_Exhibit.Core.Completed; } }
    }
}
