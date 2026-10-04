using System;
using Atlas.Spaces.Glued;
using Atlas.World;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>
    /// The Glued exhibit of the Geometry Playground (Master Plan tasks P2-C12 to P2-C14): the guided steps of the S1 space
    /// (<see cref="GluedTour"/>) as the shell's exhibit, so its task line reaches the prompt chip and its completion ends
    /// beat 9. The steps, the wrap counter, the glued.* events, the info card and the gluing sign live in the space
    /// itself, so S1 teaches the same way whether it is reached from the exhibit menu, the space menu or the Station.
    /// Engine-free: bound to the space's readings. The space adds the card and the sign to its draw count
    /// (<see cref="GluedBudget"/>).
    /// </summary>
    public sealed class GluedExhibit
    {
        IGluedReadings m_Space;
        GluedTour m_Tour;
        Action<string> m_OnPrompt;

        /// <summary>The caption chip of S1 (Master Plan section 10.1.3).</summary>
        public const string Caption = CaptionTexts.Glued;

        /// <summary>The space the exhibit follows, or null outside S1.</summary>
        public IGluedReadings Space { get { return m_Space; } }
        /// <summary>What the walker is asked to do now; empty outside S1.</summary>
        public string Prompt { get { return m_Tour != null ? m_Tour.Prompt : string.Empty; } }
        public bool Completed { get { return m_Tour != null && m_Tour.Task == GluedTask.Done; } }
        public event Action<string> PromptChanged;

        /// <summary>Follows an entered Glued space, whose steps have started.</summary>
        public void Bind(IGluedReadings space)
        {
            if (space == null) throw new ArgumentNullException(nameof(space));
            Unbind();
            m_Space = space;
            m_Tour = space.Tour;
            if (m_Tour != null)
            {
                m_OnPrompt = OnPrompt;
                m_Tour.PromptChanged += m_OnPrompt;
            }
        }

        public void Unbind()
        {
            if (m_Tour != null && m_OnPrompt != null) m_Tour.PromptChanged -= m_OnPrompt;
            m_Space = null;
            m_Tour = null;
            m_OnPrompt = null;
        }

        /// <summary>Starts the steps again, in the gluing on screen. A no-op outside S1.</summary>
        public void Restart()
        {
            if (m_Tour != null) m_Tour.Begin(m_Space.Layout);
        }

        void OnPrompt(string text)
        {
            Action<string> changed = PromptChanged;
            if (changed != null) changed(text);
        }
    }

    /// <summary>
    /// The S1 equation card (Master Plan section 10.1.3: "x mod a, z mod b; cell a x b; wraps; copy parity (-1)^row"; task
    /// P2-A03): where the walker stands inside the cell, the cell, the edges crossed and whether this copy is mirrored.
    /// Reads the space through the exhibit; every row is a dash outside S1. No allocation in Read.
    /// </summary>
    public sealed class GluedEquationSource : IEquationSource
    {
        readonly GluedExhibit m_Exhibit;

        public GluedEquationSource(GluedExhibit exhibit)
        {
            if (exhibit == null) throw new ArgumentNullException(nameof(exhibit));
            m_Exhibit = exhibit;
        }

        public SpaceId Space { get { return SpaceId.Glued; } }
        /// <summary>The gluing in force, as the identification of the cell's points.</summary>
        public string Template
        {
            get
            {
                IGluedReadings s = m_Exhibit.Space;
                switch (s != null ? s.Layout : GluedLayout.Torus)
                {
                    case GluedLayout.Klein: return "Klein bottle:  (x, z) ~ (x + a, z) ~ (−x, z + b)";
                    case GluedLayout.Torus3: return "3-torus:  (x, y, z) ~ (x + a, y, z) ~ (x, y, z + b) ~ (x, y + 2.6 m, z)";
                    default: return "torus:  (x, z) ~ (x + a, z) ~ (x, z + b)";
                }
            }
        }
        public int MaxRows { get { return 6; } }

        public int Read(EquationRow[] rows)
        {
            IGluedReadings s = m_Exhibit.Space;
            double nan = double.NaN;
            GluedCardValues c = s != null ? s.Card : default;
            rows[0] = EquationRow.Number("x mod a", s != null ? c.XMod : nan, "m");
            rows[1] = EquationRow.Number("z mod b", s != null ? c.ZMod : nan, "m");
            rows[2] = EquationRow.Number("a", s != null ? c.A : nan, "m");
            rows[3] = EquationRow.Number("b", s != null ? c.B : nan, "m");
            rows[4] = EquationRow.Number("wraps", s != null ? c.Wraps : nan, "");
            rows[5] = EquationRow.Number("(−1)^row", s != null ? c.Parity : nan, "");
            return 6;
        }
    }
}
