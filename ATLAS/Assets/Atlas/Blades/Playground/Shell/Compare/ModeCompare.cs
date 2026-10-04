using System;
using Atlas.Kernel;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>What a press of the mode chip did.</summary>
    public struct ModeSwitchResult
    {
        /// <summary>The mode changed.</summary>
        public bool Switched;
        public CurvedMode From, To;
        /// <summary>The walker's tile id was the same before and after the switch (it must be).</summary>
        public bool TileKept;
        public TileId TileBefore, TileAfter;
    }

    /// <summary>
    /// The mode chip (Master Plan section 10.1.3, twins: Y, the mode chip by gaze, a double click). A press
    /// switches S2 (Mode 1) and S3 (Mode 2) and checks that the switch kept the walker's tile id.
    /// Engine-free.
    /// </summary>
    public sealed class ModeChip
    {
        /// <summary>The chip's text for a mode.</summary>
        public const string Mode1Label = "Mode 1", Mode2Label = "Mode 2";

        readonly IModePort m_Port;

        public ModeChip(IModePort port)
        {
            if (port == null) throw new ArgumentNullException(nameof(port));
            m_Port = port;
        }

        public CurvedMode Mode { get { return m_Port.Mode; } }

        /// <summary>The label of the mode shown now.</summary>
        public string Label { get { return m_Port.Mode == CurvedMode.CurvedWalk ? Mode1Label : Mode2Label; } }

        /// <summary>Switches to the other mode.</summary>
        public ModeSwitchResult Toggle()
        {
            return SwitchTo(m_Port.Mode == CurvedMode.CurvedWalk ? CurvedMode.AlignedCells : CurvedMode.CurvedWalk);
        }

        /// <summary>Switches to the given mode.</summary>
        public ModeSwitchResult SwitchTo(CurvedMode mode)
        {
            ModeSwitchResult result = new ModeSwitchResult { From = m_Port.Mode, TileBefore = m_Port.Tile };
            result.Switched = m_Port.Request(mode);
            result.To = m_Port.Mode;
            result.TileAfter = m_Port.Tile;
            result.TileKept = result.TileBefore == result.TileAfter;
            return result;
        }
    }

    /// <summary>
    /// Keeps the home beacon honest: the distance it shows is compared with the kernel's, every frame it is on
    /// (plan task P2-C05: they match within 0.1 m). Engine-free.
    /// </summary>
    public sealed class HomeBeaconLink
    {
        /// <summary>Metres: the beacon's distance and the kernel's differ by at most this.</summary>
        public const double ToleranceM = 0.1;

        readonly IHomeBeaconPort m_Beacon;

        public HomeBeaconLink(IHomeBeaconPort beacon)
        {
            if (beacon == null) throw new ArgumentNullException(nameof(beacon));
            m_Beacon = beacon;
        }

        public bool Enabled { get { return m_Beacon.Enabled; } set { m_Beacon.Enabled = value; } }

        /// <summary>Largest difference seen while the beacon was on, metres.</summary>
        public double MaxErrorM { get; private set; }

        /// <summary>The last frame's difference, metres; NaN when the beacon was off or a number was not finite.</summary>
        public double ErrorM { get; private set; } = double.NaN;

        /// <summary>Every frame seen so far had a difference within <see cref="ToleranceM"/>.</summary>
        public bool WithinTolerance { get { return MaxErrorM <= ToleranceM; } }

        /// <summary>Per frame.</summary>
        public void Update()
        {
            if (!m_Beacon.Enabled)
            {
                ErrorM = double.NaN;
                return;
            }
            double shown = m_Beacon.ShownDistanceM, kernel = m_Beacon.KernelDistanceM;
            if (double.IsNaN(shown) || double.IsNaN(kernel) || double.IsInfinity(shown) || double.IsInfinity(kernel))
            {
                // A beacon that shows no number is not within tolerance of the kernel.
                ErrorM = double.NaN;
                MaxErrorM = double.PositiveInfinity;
                return;
            }
            ErrorM = Math.Abs(shown - kernel);
            if (ErrorM > MaxErrorM) MaxErrorM = ErrorM;
        }

        /// <summary>Forgets the worst difference: a new run of the beat.</summary>
        public void Reset()
        {
            MaxErrorM = 0.0;
            ErrorM = double.NaN;
        }
    }

    /// <summary>
    /// Beats 7 and 8 (Master Plan section 10.1.2). Beat 7, Free Curved Walk: S2 in Mode 1, the mini-map revealed
    /// after the first lap and the home beacon on; the beat ends when the walker opens the mode chip, which
    /// switches to Mode 2. Beat 8, Compare: S3 in Mode 2; the beat ends when the walker opens the menu.
    /// </summary>
    public sealed class ModeCompare
    {
        readonly ModeChip m_Chip;
        readonly IMiniMapPort m_Map;
        readonly HomeBeaconLink m_Beacon;
        readonly GuidedWalk m_Walk;

        PlaygroundBeat m_Beat = PlaygroundBeat.None;
        bool m_Revealed;
        bool m_SwitchedToMode2;
        bool m_MenuOpened;

        public ModeCompare(ModeChip chip, IMiniMapPort map, HomeBeaconLink beacon, GuidedWalk walk)
        {
            if (chip == null) throw new ArgumentNullException(nameof(chip));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (beacon == null) throw new ArgumentNullException(nameof(beacon));
            if (walk == null) throw new ArgumentNullException(nameof(walk));
            m_Chip = chip;
            m_Map = map;
            m_Beacon = beacon;
            m_Walk = walk;
        }

        public ModeChip Chip { get { return m_Chip; } }
        public HomeBeaconLink Beacon { get { return m_Beacon; } }

        /// <summary>The mini-map has been revealed in this run.</summary>
        public bool MiniMapRevealed { get { return m_Revealed; } }

        /// <summary>The last press of the mode chip.</summary>
        public ModeSwitchResult LastSwitch { get; private set; }

        /// <summary>Beat 7, Free Curved Walk, is complete: the walker opened the mode chip.</summary>
        public bool FreeWalkComplete { get { return m_SwitchedToMode2; } }

        /// <summary>Beat 8, Compare, is complete: the walker opened the menu.</summary>
        public bool CompareComplete { get { return m_MenuOpened; } }

        /// <summary>The step the director waits on for the given beat (7 or 8); null for any other.</summary>
        public IBeatStep Step(PlaygroundBeat beat)
        {
            if (beat == PlaygroundBeat.FreeCurvedWalk) return new BeatStep(this, beat);
            if (beat == PlaygroundBeat.Compare) return new BeatStep(this, beat);
            return null;
        }

        /// <summary>Enters beat 7 (Mode 1) or beat 8 (Mode 2).</summary>
        public void Enter(PlaygroundBeat beat)
        {
            m_Beat = beat;
            if (beat == PlaygroundBeat.FreeCurvedWalk)
            {
                m_Revealed = false;
                m_SwitchedToMode2 = false;
                m_MenuOpened = false;
                m_Map.Visible = false;
                m_Beacon.Reset();
                m_Beacon.Enabled = true;
                // The loop was walked in beat 6, so the first lap is normally behind the walker already.
                RevealIfLapDone();
            }
            else if (beat == PlaygroundBeat.Compare)
            {
                m_MenuOpened = false;
            }
        }

        /// <summary>Per frame.</summary>
        public void Update()
        {
            if (m_Beat == PlaygroundBeat.FreeCurvedWalk) RevealIfLapDone();
            if (m_Beat == PlaygroundBeat.FreeCurvedWalk || m_Beat == PlaygroundBeat.Compare) m_Beacon.Update();
        }

        /// <summary>The walker presses the mode chip (Y, gaze on the chip, double click).</summary>
        public ModeSwitchResult OpenModeChip()
        {
            ModeSwitchResult result = m_Chip.Toggle();
            LastSwitch = result;
            if (result.Switched && m_Beat == PlaygroundBeat.FreeCurvedWalk && result.To == CurvedMode.AlignedCells)
                m_SwitchedToMode2 = true;
            return result;
        }

        /// <summary>The walker opens the exhibit menu (Start, an exhibit sign, a long press).</summary>
        public void OpenMenu()
        {
            if (m_Beat == PlaygroundBeat.Compare) m_MenuOpened = true;
        }

        /// <summary>Leaves beats 7 and 8: the map and the beacon go.</summary>
        public void Leave()
        {
            m_Beat = PlaygroundBeat.None;
            m_Map.Visible = false;
            m_Beacon.Enabled = false;
        }

        void RevealIfLapDone()
        {
            if (m_Revealed || m_Walk.ClosedLaps < 1) return;
            m_Revealed = true;
            m_Map.Visible = true;
        }

        sealed class BeatStep : IBeatStep
        {
            readonly ModeCompare m_Owner;
            readonly PlaygroundBeat m_Which;

            public BeatStep(ModeCompare owner, PlaygroundBeat which)
            {
                m_Owner = owner;
                m_Which = which;
            }

            public PlaygroundBeat Beat { get { return m_Which; } }
            public void Enter() { m_Owner.Enter(m_Which); }
            public bool Complete
            {
                get { return m_Which == PlaygroundBeat.FreeCurvedWalk ? m_Owner.FreeWalkComplete : m_Owner.CompareComplete; }
            }
        }
    }
}
