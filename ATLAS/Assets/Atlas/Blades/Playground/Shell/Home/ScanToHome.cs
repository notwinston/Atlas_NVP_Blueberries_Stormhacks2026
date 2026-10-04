using System;
using System.Collections.Generic;
using Atlas.World;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// Beats 1 to 3 of the run (Master Plan section 10.1.2, plan task P2-C02): Scan, Home appears, Headset on.
    /// It holds the three exit conditions and nothing else; the scan itself (P1-C06), the room content
    /// (P1-C07) and the asset client's swaps (P1-D10) do their own work and report here. Engine-free.
    /// </summary>
    /// <remarks>
    /// Beat 1 ends when a valid RoomModel exists (<see cref="SetRoom"/>). Beat 2 ends when the walker confirms,
    /// the Local Network prompt has been answered (Master Plan section 0.5, <see cref="LocalNetworkAnswered"/>)
    /// and the Home content is drawn, every box as a placeholder or a mesh. Beat 3 ends when the stereo view is
    /// on and the head has been still for <see cref="StableSeconds"/>.
    /// </remarks>
    public sealed class ScanToHome
    {
        /// <summary>Seconds the head must stay still in the headset before beat 3 ends (derived: "head pose
        /// stable" has no number in the plan; one second is longer than the 0.2 s speed window and shorter than
        /// a walker's patience).</summary>
        public const double StableSeconds = 1.0;

        readonly IRoomContent m_Content;
        readonly HeadStillness m_Still;
        readonly List<string> m_Errors = new List<string>();

        bool m_Confirmed;

        public ScanToHome(IRoomContent content, HeadStillness still)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (still == null) throw new ArgumentNullException(nameof(still));
            m_Content = content;
            m_Still = still;
        }

        /// <summary>The room the run walks in, once the scan produced a valid one; null before.</summary>
        public RoomModel Room { get; private set; }

        /// <summary>Why the last room offered was refused; empty when it was accepted.</summary>
        public IReadOnlyList<string> RoomErrors { get { return m_Errors; } }

        /// <summary>The Local Network prompt has been answered, yes or no (Master Plan section 0.5).</summary>
        public bool LocalNetworkAnswered { get; set; }

        /// <summary>The stereo view is on: the phone is in the headset.</summary>
        public bool StereoOn { get; set; }

        /// <summary>The walker's confirmation (A, the prompt chip, a click) was pressed in beat 2.</summary>
        public bool Confirmed { get { return m_Confirmed; } }

        /// <summary>
        /// Offers a room from the scan or the world-map fast path. It is kept only when RoomModelValidator accepts
        /// it. Returns whether it was kept.
        /// </summary>
        public bool SetRoom(RoomModel room)
        {
            m_Errors.Clear();
            if (room == null)
            {
                m_Errors.Add("no room");
                return false;
            }
            if (!RoomModelValidator.Validate(room, m_Errors)) return false;
            Room = room;
            return true;
        }

        /// <summary>The walker confirms the rebuilt Home. Only counts once the content is built.</summary>
        public void Confirm()
        {
            if (m_Content.IsBuilt) m_Confirmed = true;
        }

        /// <summary>What the Home content shows now: how many boxes are drawn, as what.</summary>
        public HomeAuditResult Audit()
        {
            return HomeAudit.Count(m_Content);
        }

        public bool ScanComplete { get { return Room != null; } }

        public bool HomeComplete
        {
            get { return m_Confirmed && LocalNetworkAnswered && m_Content.IsBuilt && Audit().AllDrawn; }
        }

        public bool HeadsetComplete
        {
            get { return StereoOn && m_Still.Standing && m_Still.StillSeconds >= StableSeconds; }
        }

        /// <summary>The step the director waits on for beat 1, 2 or 3; null for any other.</summary>
        public IBeatStep Step(PlaygroundBeat beat)
        {
            if (beat == PlaygroundBeat.Scan || beat == PlaygroundBeat.HomeAppears || beat == PlaygroundBeat.HeadsetOn)
                return new BeatStep(this, beat);
            return null;
        }

        void Enter(PlaygroundBeat beat)
        {
            switch (beat)
            {
                case PlaygroundBeat.Scan:
                    // A room set before the beat (the pre-scan and world-map fast path) stays: the beat
                    // then ends at once.
                    m_Confirmed = false;
                    break;
                case PlaygroundBeat.HomeAppears:
                    m_Confirmed = false;
                    break;
            }
        }

        sealed class BeatStep : IBeatStep
        {
            readonly ScanToHome m_Owner;
            readonly PlaygroundBeat m_Which;

            public BeatStep(ScanToHome owner, PlaygroundBeat which)
            {
                m_Owner = owner;
                m_Which = which;
            }

            public PlaygroundBeat Beat { get { return m_Which; } }
            public void Enter() { m_Owner.Enter(m_Which); }

            public bool Complete
            {
                get
                {
                    switch (m_Which)
                    {
                        case PlaygroundBeat.Scan: return m_Owner.ScanComplete;
                        case PlaygroundBeat.HomeAppears: return m_Owner.HomeComplete;
                        default: return m_Owner.HeadsetComplete;
                    }
                }
            }
        }
    }
}
