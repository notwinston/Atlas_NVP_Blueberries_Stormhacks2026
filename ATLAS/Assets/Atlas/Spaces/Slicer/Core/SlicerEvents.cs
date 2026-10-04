using System.Globalization;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The Slicer's events for the Station and the logs (Master Plan section 10.1.3, S6: <c>slicer.w</c>, <c>slicer.rot</c>,
    /// <c>slicer.shadow</c>, <c>slicer.poly</c>; and <c>slicer.task</c> for the tour). The cut and the turns move every frame
    /// while the walker walks or holds a stick, so they are logged when they settle (still for <see cref="SettleSeconds"/>) or
    /// at most every <see cref="MinInterval"/> seconds while they move by more than a step; the section when its shape has held
    /// for <see cref="SectionHoldSeconds"/>. Engine-free. Text is built only when an event is written; a quiet frame allocates
    /// nothing.
    /// </summary>
    public sealed class SlicerEvents
    {
        public const string EventW = "slicer.w", EventRot = "slicer.rot", EventShadow = "slicer.shadow",
                            EventPoly = "slicer.poly", EventTask = "slicer.task";
        public const float WStep = 0.1f, AngleStepDeg = 10f;
        public const float MinInterval = 0.5f, SettleSeconds = 1f, SectionHoldSeconds = 0.25f;

        static readonly string[] s_planes = { "xw", "yw", "zw" };

        readonly IEventLog m_Log;
        double m_Time;
        // The cut and the three turns: last logged value, the value now, when it last moved, when it was last logged.
        readonly float[] m_Logged = new float[4], m_Now = new float[4];
        readonly double[] m_Moved = new double[4], m_LoggedAt = new double[4];
        SectionShape m_SectionShown, m_SectionCandidate;
        double m_CandidateSince;
        bool m_Started;

        /// <param name="log">Where the events go; null makes every call a no-op.</param>
        public SlicerEvents(IEventLog log) { m_Log = log; }

        /// <summary>Starts again: the next frame's values become the baseline and are not logged.</summary>
        public void Reset() { m_Started = false; }

        /// <summary>
        /// One frame: the clock (seconds), the cut, the turns (radians), the section on screen and the body. Writes slicer.w,
        /// slicer.rot and slicer.poly when due.
        /// </summary>
        public void Step(double time, float w, float xw, float yw, float zw, in SectionInfo section, SlicerObject body)
        {
            if (m_Log == null) return;
            m_Time = time;
            float x = math.degrees(xw), y = math.degrees(yw), z = math.degrees(zw);
            if (!m_Started)
            {
                m_Started = true;
                Set(0, w); Set(1, x); Set(2, y); Set(3, z);
                for (int i = 0; i < 4; i++) { m_Logged[i] = m_Now[i]; m_Moved[i] = m_LoggedAt[i] = time; }
                m_SectionShown = m_SectionCandidate = section.Shape;
                m_CandidateSince = time;
                return;
            }
            Track(0, w, WStep);
            Track(1, x, AngleStepDeg);
            Track(2, y, AngleStepDeg);
            Track(3, z, AngleStepDeg);

            if (section.Shape != m_SectionCandidate) { m_SectionCandidate = section.Shape; m_CandidateSince = time; }
            if (m_SectionCandidate != m_SectionShown && time - m_CandidateSince >= SectionHoldSeconds)
            {
                m_SectionShown = m_SectionCandidate;
                CultureInfo c = CultureInfo.InvariantCulture;
                m_Log.LogJson(EventPoly, "{\"object\":\"" + SlicerObjects.Name(body) + "\",\"section\":\"" + SectionNamer.Name(section.Shape) +
                    "\",\"faces\":" + section.Faces.ToString(c) + ",\"vertices\":" + section.Vertices.ToString(c) +
                    ",\"w\":" + w.ToString("0.000", c) + "}");
            }
        }

        /// <summary>The shadow went on or off; <paramref name="byLook"/> when a look-down peek did it.</summary>
        public void Shadow(bool on, bool byLook)
        {
            if (m_Log == null) return;
            m_Log.LogJson(EventShadow, on ? (byLook ? "{\"on\":1,\"by\":\"look\"}" : "{\"on\":1,\"by\":\"button\"}")
                                          : (byLook ? "{\"on\":0,\"by\":\"look\"}" : "{\"on\":0,\"by\":\"button\"}"));
        }

        /// <summary>The tour moved on to <paramref name="task"/>.</summary>
        public void Task(SlicerTask task)
        {
            if (m_Log == null) return;
            m_Log.LogJson(EventTask, "{\"task\":\"" + SlicerTour.TaskName(task) + "\",\"step\":" + ((int)task + 1).ToString(CultureInfo.InvariantCulture) + "}");
        }

        void Set(int i, float v) { m_Now[i] = v; }

        // Logs value i when it has moved a step and the last log is MinInterval old, or when it has settled away from the last log.
        void Track(int i, float value, float step)
        {
            if (value != m_Now[i]) { m_Now[i] = value; m_Moved[i] = m_Time; }
            float moved = math.abs(m_Now[i] - m_Logged[i]);
            if (i > 0 && moved > 180f) moved = 360f - moved;            // angles wrap
            bool due = (moved >= step && m_Time - m_LoggedAt[i] >= MinInterval) ||
                       (moved > 0.01f * step && m_Time - m_Moved[i] >= SettleSeconds);
            if (!due) return;
            m_Logged[i] = m_Now[i];
            m_LoggedAt[i] = m_Time;
            CultureInfo c = CultureInfo.InvariantCulture;
            if (i == 0) m_Log.LogJson(EventW, "{\"w\":" + m_Now[0].ToString("0.000", c) + "}");
            else m_Log.LogJson(EventRot, "{\"plane\":\"" + s_planes[i - 1] + "\",\"deg\":" + m_Now[i].ToString("0.0", c) + "}");
        }
    }
}
