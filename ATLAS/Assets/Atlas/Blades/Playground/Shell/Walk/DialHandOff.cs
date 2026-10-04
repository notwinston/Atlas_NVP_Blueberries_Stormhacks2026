using System;
using Atlas.World;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>What a dial request did.</summary>
    public enum DialOutcome : byte
    {
        /// <summary>The dial moved to a new q.</summary>
        Applied = 0,
        /// <summary>The walker stands and the dial is already there (or clamped at an end).</summary>
        Unchanged = 1,
        /// <summary>The walker is moving: the request was dropped (Master Plan section 10.1.4).</summary>
        IgnoredMoving = 2
    }

    /// <summary>
    /// The standing lock of the dial (Master Plan sections 4.6 and 10.1.4): curvature changes only while the
    /// walker stands, so a request made at a head speed above 0.1 m/s is ignored, not queued. Every input
    /// twin of the dial (D-pad, floor dial, lever) goes through here. Engine-free.
    /// </summary>
    public sealed class DialHandOff
    {
        readonly IDialPort m_Dial;
        readonly HeadStillness m_Still;

        public DialHandOff(IDialPort dial, HeadStillness still)
        {
            if (dial == null) throw new ArgumentNullException(nameof(dial));
            if (still == null) throw new ArgumentNullException(nameof(still));
            m_Dial = dial;
            m_Still = still;
        }

        /// <summary>Requests dropped because the walker was moving, since construction.</summary>
        public int Ignored { get; private set; }
        /// <summary>Requests that moved the dial.</summary>
        public int Applied { get; private set; }

        public int Q { get { return m_Dial.Q; } }

        /// <summary>Floor dial detent: q = 3, 4, 5 or 6.</summary>
        public DialOutcome Select(int q)
        {
            if (q < 3 || q > 6) throw new ArgumentOutOfRangeException(nameof(q), q, "The dial has q = 3 to 6.");
            if (!Allowed()) return Ignore();
            return Count(m_Dial.Set(q));
        }

        /// <summary>D-pad right.</summary>
        public DialOutcome Next()
        {
            if (!Allowed()) return Ignore();
            return Count(m_Dial.Next());
        }

        /// <summary>D-pad left.</summary>
        public DialOutcome Prev()
        {
            if (!Allowed()) return Ignore();
            return Count(m_Dial.Prev());
        }

        /// <summary>Headset lever click: 3 to 4 to 5 to 6 to 3.</summary>
        public DialOutcome Cycle()
        {
            if (!Allowed()) return Ignore();
            return Count(m_Dial.Cycle());
        }

        bool Allowed()
        {
            return m_Still.Standing;
        }

        DialOutcome Ignore()
        {
            Ignored++;
            return DialOutcome.IgnoredMoving;
        }

        DialOutcome Count(bool changed)
        {
            if (!changed) return DialOutcome.Unchanged;
            Applied++;
            return DialOutcome.Applied;
        }

        /// <summary>
        /// True when every detent q = 3, 4, 5 and 6 has a usable length scale for this room: R at q = 3 and L at
        /// q = 5 and 6 are finite and positive after the comfort floor, and q = 4 is flat. The check made at each
        /// load of a room (plan task P2-C03: "q = 3 to 6 each load").
        /// </summary>
        public static bool CanSelectAll(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            for (int q = 3; q <= 6; q++)
            {
                if (q == 4) continue;
                float scale = room.Curvature.ForDial(q);
                if (!(scale > 0f) || float.IsInfinity(scale)) return false;
            }
            return room.LapSide > 0f;
        }
    }

    /// <summary>
    /// Beat 5, Dial (Master Plan section 10.1.2): the walker turns q through 3, 4, 5 and 6 while standing, and
    /// the beat ends when q = 5 is selected. The dial may be at q = 5 already when the beat starts, but the beat
    /// only ends on a selection made inside it.
    /// </summary>
    public sealed class DialBeat : IBeatStep
    {
        public const int TargetQ = 5;

        readonly DialHandOff m_HandOff;
        int m_AppliedAtEntry;
        bool m_Selected;

        public DialBeat(DialHandOff handOff)
        {
            if (handOff == null) throw new ArgumentNullException(nameof(handOff));
            m_HandOff = handOff;
        }

        public PlaygroundBeat Beat { get { return PlaygroundBeat.Dial; } }

        public bool Complete { get { return m_Selected; } }

        public void Enter()
        {
            m_Selected = false;
            m_AppliedAtEntry = m_HandOff.Applied;
        }

        /// <summary>Per frame. Complete once q = 5 is selected by a request made since Enter.</summary>
        public void Update()
        {
            if (!m_Selected && m_HandOff.Applied > m_AppliedAtEntry && m_HandOff.Q == TargetQ) m_Selected = true;
        }
    }
}
