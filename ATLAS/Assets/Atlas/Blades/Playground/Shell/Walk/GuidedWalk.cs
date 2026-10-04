using System;
using System.Globalization;
using Atlas.Kernel;
using Atlas.World;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>How a closed lap compared with the plan's tolerances.</summary>
    public struct WalkLapResult
    {
        /// <summary>Tiles per corner of the walk this lap belongs to.</summary>
        public int Q;
        /// <summary>Laps closed at this q since the walk was armed with it, this one included.</summary>
        public int Lap;
        /// <summary>Metres from the walker to the start flag when the loop closed.</summary>
        public double ClosureM;
        /// <summary>Degrees the world turned by, as the read-out shows it.</summary>
        public double TurnDeg;
        /// <summary>ClosureM at most 0.25 m.</summary>
        public bool ClosureOk;
        /// <summary>q = 5 only: the turn is 90 degrees within 5. True for the other q, which the plan does not
        /// ask for.</summary>
        public bool TurnOk;
    }

    /// <summary>
    /// Beat 6, the guided 3-4-5 walk (Master Plan sections 3.3 and 10.1.2, plan task P2-C04). It arms lane B's
    /// loop walk through <see cref="IWalkPort"/> with the room's lap side and the dial's length scale, lets the
    /// walker repeat the loop at q = 3, 4 and 5, logs <c>walk.corner</c>, <c>walk.lap</c> and <c>walk.closed</c>,
    /// and ends when the loop closes at q = 5. The markers and read-outs themselves (corner counter, distance from
    /// the start flag, "world turned by N degrees") are lane B's P1-B09 views; this class wires them to the beat.
    /// Engine-free.
    /// </summary>
    public sealed class GuidedWalk : IBeatStep
    {
        /// <summary>Metres: the loop closes within this of the start flag (plan task P2-C04, S2 test).</summary>
        public const double ClosureToleranceM = 0.25;
        /// <summary>Degrees the world turns by in one lap at q = 5 (Master Plan section 10.1.3, S2).</summary>
        public const double TurnExpectedDeg = 90.0;
        /// <summary>Degrees of tolerance on <see cref="TurnExpectedDeg"/>.</summary>
        public const double TurnToleranceDeg = 5.0;
        /// <summary>The walk that ends the beat.</summary>
        public const int FinalQ = 5;
        /// <summary>The corner after which the pointing probe is taken, at q = 4 and q = 5 (section 10.1.2).</summary>
        public const int ProbeCorner = 3;

        readonly IWalkPort m_Walk;
        readonly IDialPort m_Dial;
        readonly IEventLog m_Log;
        readonly HeadStillness m_Still;

        RoomModel m_Room;
        int m_Q;
        int m_Laps;
        bool m_Done;

        /// <summary>The log may be null (nothing is logged).</summary>
        public GuidedWalk(IWalkPort walk, IDialPort dial, HeadStillness still, IEventLog log)
        {
            if (walk == null) throw new ArgumentNullException(nameof(walk));
            if (dial == null) throw new ArgumentNullException(nameof(dial));
            if (still == null) throw new ArgumentNullException(nameof(still));
            m_Walk = walk;
            m_Dial = dial;
            m_Still = still;
            m_Log = log;
        }

        public PlaygroundBeat Beat { get { return PlaygroundBeat.Walk345; } }

        /// <summary>The loop closed at q = 5.</summary>
        public bool Complete { get { return m_Done; } }

        /// <summary>The q the walk is armed with; 0 when it is not armed.</summary>
        public int Q { get { return m_Q; } }

        /// <summary>Laps closed in this run of the beat, at any q.</summary>
        public int ClosedLaps { get; private set; }

        /// <summary>The last closed lap. Default before the first.</summary>
        public WalkLapResult LastLap { get; private set; }

        /// <summary>The walker has reached corner 3 at q = 4 or q = 5 and not yet taken the probe: the shell
        /// raises it once for the pointing probe (plan task P2-C17).</summary>
        public event Action<int> ProbeDue;

        /// <summary>The walk is armed for a room and the dial's q.</summary>
        public bool Armed { get { return m_Walk.Armed; } }

        /// <summary>Sets the room whose lap side the walk uses. Call before Enter, and again at each room load.</summary>
        public void SetRoom(RoomModel room)
        {
            m_Room = room;
        }

        public void Enter()
        {
            m_Done = false;
            ClosedLaps = 0;
            LastLap = default(WalkLapResult);
            Arm(m_Dial.Q);
        }

        /// <summary>The walker leaves the beat: the markers go.</summary>
        public void Leave()
        {
            m_Walk.Disarm();
            m_Q = 0;
        }

        /// <summary>
        /// Arms the loop for q (the walker repeats it at q = 3 and q = 4). The length scale is what the dial
        /// resolves: R at q = 3, 1 at q = 4, L after the comfort floor at q = 5 and 6.
        /// </summary>
        public void Arm(int q)
        {
            if (m_Room == null) throw new InvalidOperationException("GuidedWalk: SetRoom comes before Arm.");
            double kappa = Curvature.OfQ(q);
            double scale = q == 4 ? 1.0 : m_Room.Curvature.ForDial(q);
            m_Walk.Arm(q, kappa, scale, m_Room.LapSide);
            m_Q = q;
            m_Laps = 0;
        }

        /// <summary>
        /// Per frame, after the kernel has stepped. Re-arms when the dial moved to another q, then advances
        /// the tracker with the head's stillness and logs what it found.
        /// </summary>
        public void Update()
        {
            if (m_Room == null || m_Done) return;
            if (m_Dial.Q != m_Q) Arm(m_Dial.Q);

            WalkEvent ev = m_Walk.Update(m_Still.Standing);
            switch (ev)
            {
                case WalkEvent.Corner:
                    LogCorner();
                    RaiseProbe();
                    break;
                case WalkEvent.Closed:
                    // Closing is also the last corner: it is logged as a corner too, so the log reads 1..q.
                    LogCorner();
                    RaiseProbe();
                    OnClosed();
                    break;
            }
        }

        void RaiseProbe()
        {
            if (m_Walk.Corners == ProbeCorner && (m_Q == 4 || m_Q == FinalQ) && ProbeDue != null) ProbeDue(m_Q);
        }

        void OnClosed()
        {
            m_Laps++;
            ClosedLaps++;
            double closure = m_Walk.DistanceToFlagM;
            double turn = m_Walk.WorldTurnDeg;
            WalkLapResult lap = new WalkLapResult
            {
                Q = m_Q,
                Lap = m_Laps,
                ClosureM = closure,
                TurnDeg = turn,
                ClosureOk = closure <= ClosureToleranceM,
                TurnOk = m_Q != FinalQ || Math.Abs(Math.Abs(turn) - TurnExpectedDeg) <= TurnToleranceDeg
            };
            LastLap = lap;
            if (m_Log != null)
            {
                string tail = ",\"q\":" + I(m_Q) + ",\"closure\":" + D(closure) + ",\"turn\":" + D(turn);
                m_Log.LogJson("walk.lap", "{\"lap\":" + I(m_Laps) + tail + "}");
                m_Log.LogJson("walk.closed", "{\"ok\":" + (lap.ClosureOk ? "true" : "false") + tail + "}");
            }
            if (m_Q == FinalQ) m_Done = true;
        }

        void LogCorner()
        {
            if (m_Log == null) return;
            m_Log.LogJson("walk.corner",
                "{\"q\":" + I(m_Q) + ",\"k\":" + I(m_Walk.Corners) + ",\"dist\":" + D(m_Walk.DistanceToFlagM)
                + ",\"turn\":" + D(m_Walk.WorldTurnDeg) + "}");
        }

        static string I(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // Millimetres and hundredths of a degree are all a reader of the log can use.
        static string D(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "null";
            return Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
