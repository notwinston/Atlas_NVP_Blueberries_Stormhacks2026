using System;
using Atlas.Blades.Playground.Shell;
using Atlas.Spaces.Slicer;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>
    /// The Slicer toybox of the Geometry Playground (Master Plan task P2-A01): the guided steps of the S6 space
    /// (<see cref="SlicerTour"/>) as the shell's exhibit, so its task line reaches the prompt chip and its completion ends beat 9.
    /// The steps, the slice's name, the panel and the look-down shadow live in the space itself, so S6 teaches the same way
    /// whether it is reached from the exhibit menu, the overlay or the Station. Engine-free: bound to the space's readings.
    /// </summary>
    public sealed class ToyboxExhibit
    {
        ISlicerReadings m_Space;
        Action<SlicerTask> m_OnTask;

        /// <summary>The caption chip of S6 (Master Plan section 10.1.3).</summary>
        public const string Caption = CaptionTexts.Slicer;

        /// <summary>The space the exhibit follows, or null outside S6.</summary>
        public ISlicerReadings Space { get { return m_Space; } }
        /// <summary>What the walker is asked to do now; empty outside S6.</summary>
        public string Prompt { get { return m_Space != null && m_Space.Tour != null ? m_Space.Tour.Prompt : string.Empty; } }
        public bool Completed { get { return m_Space != null && m_Space.Tour != null && m_Space.Tour.Completed; } }
        public event Action<string> PromptChanged;

        /// <summary>Follows an entered Slicer space.</summary>
        public void Bind(ISlicerReadings space)
        {
            if (space == null) throw new ArgumentNullException(nameof(space));
            Unbind();
            m_Space = space;
            if (m_Space.Tour != null)
            {
                m_OnTask = OnTask;
                m_Space.Tour.TaskChanged += m_OnTask;
            }
        }

        public void Unbind()
        {
            if (m_Space != null && m_Space.Tour != null && m_OnTask != null) m_Space.Tour.TaskChanged -= m_OnTask;
            m_Space = null;
            m_OnTask = null;
        }

        /// <summary>Starts the steps again. A no-op outside S6.</summary>
        public void Restart()
        {
            if (m_Space != null && m_Space.Tour != null) m_Space.Tour.Restart();
        }

        void OnTask(SlicerTask task)
        {
            Action<string> changed = PromptChanged;
            if (changed != null) changed(SlicerTour.PromptOf(task));
        }
    }

    /// <summary>Joins <see cref="ToyboxExhibit"/> to the shell's <see cref="ExhibitRelay"/> (task P2-A01 wiring).</summary>
    public sealed class ToyboxExhibitLink : IExhibitLink
    {
        readonly ToyboxExhibit m_Exhibit;

        public ToyboxExhibitLink(ToyboxExhibit exhibit)
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
        public bool Completed { get { return m_Exhibit.Completed; } }
    }

    /// <summary>
    /// The S6 equation card (Master Plan section 10.1.3: "w0, rotation angle, section name, face count"; task P2-A03): the cut
    /// x . n = w0 and what it gives. Reads the space through the toybox; every row is a dash outside S6. No allocation in Read.
    /// </summary>
    public sealed class SlicerEquationSource : IEquationSource
    {
        readonly ToyboxExhibit m_Toybox;

        public SlicerEquationSource(ToyboxExhibit toybox)
        {
            if (toybox == null) throw new ArgumentNullException(nameof(toybox));
            m_Toybox = toybox;
        }

        public SpaceId Space { get { return SpaceId.Slicer; } }
        public string Template { get { return "slice:  x · n = w₀   (n along w, the body turned in XW, YW, ZW)"; } }
        public int MaxRows { get { return 6; } }

        public int Read(EquationRow[] rows)
        {
            ISlicerReadings s = m_Toybox.Space;
            double nan = double.NaN;
            rows[0] = EquationRow.Number("w₀", s != null ? s.W : nan, "");
            rows[1] = EquationRow.Number("XW", s != null ? math.degrees(s.Xw) : nan, "deg");
            rows[2] = EquationRow.Number("YW", s != null ? math.degrees(s.Yw) : nan, "deg");
            rows[3] = EquationRow.Number("ZW", s != null ? math.degrees(s.Zw) : nan, "deg");
            rows[4] = s != null ? EquationRow.Words("section", s.Section.Name) : EquationRow.Number("section", nan, "");
            rows[5] = EquationRow.Number("faces", s != null && s.Section.Faces > 0 ? s.Section.Faces : nan, "");
            return 6;
        }
    }
}
