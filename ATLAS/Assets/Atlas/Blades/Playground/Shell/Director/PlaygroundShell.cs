using System;
using Atlas.Input;
using Atlas.Link;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// Everything the shell is built from (task P2-C01, the composition root's input). Lane B's ports and lane D's
    /// mini-map are bound by the integrator, the rest comes from the app's services. Null members that are marked
    /// optional leave that part of the shell out.
    /// </summary>
    public sealed class PlaygroundParts
    {
        public IEventLog Log;
        public ISpaceHost Host;
        /// <summary>Declares <c>pg.fmsStop</c>.</summary>
        public ParamRegistry Params;
        /// <summary>Optional: with none there are no gaze targets, no pointing-probe press and no menu context.</summary>
        public IAtlasInput Input;
        public IRoomContent Content;
        /// <summary>The prompt chip. Null: the shell makes a <see cref="PromptChip"/>, which a view can draw.</summary>
        public IPromptSink PromptSink;
        /// <summary>The Console's FMS and SEQ entries. Null: a <see cref="ManualOperatorEntryFeed"/>.</summary>
        public IOperatorEntryFeed Entries;
        /// <summary>The app clock in seconds (<c>FrameContext.Time</c>).</summary>
        public Func<double> Clock;
        /// <summary>Beat names from <c>Beats.asset</c>, index 1 to 10. Null: <see cref="PlaygroundBeats.Name"/>.</summary>
        public string[] BeatNames;

        // Lane B and lane D, through lane C's ports (PlaygroundPorts.cs).
        public IDialPort Dial;
        public IWalkPort Walk;
        public IModePort Mode;
        public IHomeBeaconPort Beacon;
        public IMiniMapPort MiniMap;
        public IHomeBearing HomeBearing;
        public ICurvatureRamp Ramp;
        public IFloorProjection Projection;
    }

    /// <summary>What the shell reads of one frame (the frame loop's context, without its type).</summary>
    public struct PlaygroundFrame
    {
        public double Time;
        public float DeltaTime;
        public float3 HeadPosition;
        public quaternion HeadRotation;
        public TrackingState Tracking;
        public WalkerPose Walker;
    }

    /// <summary>
    /// The composition root of the Playground shell (task P2-C01): builds the director and the objects of every
    /// other shell row, and wires them the way the plan's flow says. The director owns the beats; the action map's
    /// twins drive the walker's choices; the exhibit menu and the exhibits' task lines reach the prompt queue; the
    /// operator's <c>SceneCmd</c> reaches the director. Engine-free: the Unity side is
    /// <see cref="PlaygroundInstaller"/>. Main thread only.
    /// </summary>
    public sealed class PlaygroundShell : IDisposable
    {
        public readonly PlaygroundParts Parts;
        public readonly PromptChip Chip;
        public readonly PromptQueue Prompts;
        public readonly HeadStillness Still = new HeadStillness();
        public readonly ScanToHome Scan;
        public readonly CentreMark Centre = new CentreMark();
        public readonly DialHandOff DialHand;
        public readonly DialBeat DialStep;
        public readonly GuidedWalk Walk;
        public readonly ModeCompare Compare;
        public readonly ActionMap Map = new ActionMap();
        public readonly ExhibitMenu Menu;
        public readonly HomeReturn Return;
        public readonly PointingProbe Probe;
        public readonly PointingProbeInput ProbeInput;
        public readonly SeqCollector Seq;
        public readonly ComfortMonitor Comfort;
        public readonly CaptionChip Caption = new CaptionChip();
        public readonly EquationCard Card = new EquationCard();
        public readonly ExhibitRelay Exhibits;
        public readonly PlaygroundDirectorCore Director;
        public readonly ParamEntry FmsStop;

        readonly IOperatorEntryFeed m_Entries;
        readonly Func<double> m_Clock;
        readonly Action<OperatorEntry> m_OnFms, m_OnSeq;
        readonly BeatSteps m_Menu9;
        readonly BeatSteps m_Return10;
        int m_ProbeQ;
        int m_DialShown;
        bool m_Disposed;

        public PlaygroundShell(PlaygroundParts parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (parts.Host == null) throw new ArgumentNullException("parts.Host");
            if (parts.Params == null) throw new ArgumentNullException("parts.Params");
            if (parts.Content == null) throw new ArgumentNullException("parts.Content");
            if (parts.Clock == null) throw new ArgumentNullException("parts.Clock");
            if (parts.Dial == null || parts.Walk == null || parts.Mode == null || parts.Beacon == null ||
                parts.MiniMap == null || parts.HomeBearing == null || parts.Ramp == null || parts.Projection == null)
                throw new ArgumentException("PlaygroundParts: every lane B and lane D port must be bound.", nameof(parts));
            Parts = parts;
            m_Clock = parts.Clock;

            Chip = new PromptChip();
            Prompts = new PromptQueue(parts.PromptSink ?? Chip);
            m_Entries = parts.Entries ?? new ManualOperatorEntryFeed();

            Scan = new ScanToHome(parts.Content, Still);
            DialHand = new DialHandOff(parts.Dial, Still);
            DialStep = new DialBeat(DialHand);
            Walk = new GuidedWalk(parts.Walk, parts.Dial, Still, parts.Log);
            Compare = new ModeCompare(new ModeChip(parts.Mode), parts.MiniMap, new HomeBeaconLink(parts.Beacon), Walk);
            Menu = new ExhibitMenu(parts.Host, parts.Input, Map);
            Return = new HomeReturn(parts.Host, parts.Ramp, parts.Projection, parts.Log);
            Probe = new PointingProbe(parts.HomeBearing, parts.Log, Prompts);
            if (parts.Input != null) ProbeInput = new PointingProbeInput(parts.Input, Probe, m_Clock);
            Seq = new SeqCollector(Prompts, parts.Log);
            FmsStop = ComfortMonitor.DeclareStopParam(parts.Params);
            Comfort = new ComfortMonitor(Prompts, parts.Log, ComfortMonitor.StopReader(FmsStop));
            Exhibits = new ExhibitRelay(Prompts, m_Clock);
            Director = new PlaygroundDirectorCore(parts.Log, Prompts, parts.BeatNames);

            m_Menu9 = new BeatSteps(PlaygroundBeat.ExhibitMenu, EnterMenu, () => MenuDone);
            m_Return10 = new BeatSteps(PlaygroundBeat.ReturnHome, EnterReturn, () => Return.State == HomeReturnState.Done);
            Director.SetStep(Scan.Step(PlaygroundBeat.Scan));
            Director.SetStep(Scan.Step(PlaygroundBeat.HomeAppears));
            Director.SetStep(Scan.Step(PlaygroundBeat.HeadsetOn));
            Director.SetStep(Centre);
            Director.SetStep(DialStep);
            Director.SetStep(new BeatSteps(PlaygroundBeat.Walk345, EnterWalk, () => Walk.Complete));
            Director.SetStep(Compare.Step(PlaygroundBeat.FreeCurvedWalk));
            Director.SetStep(Compare.Step(PlaygroundBeat.Compare));
            Director.SetStep(m_Menu9);
            Director.SetStep(m_Return10);

            Director.BeatEntered += OnBeatEntered;
            Director.BeatLeft += OnBeatLeft;
            Director.RunFinished += OnRunFinished;
            Walk.ProbeDue += OnProbeDue;
            Map.Fired += OnAction;
            Menu.Chosen += OnChosen;
            Menu.DonePicked += OnDonePicked;
            parts.Host.SpaceEntered += OnSpaceEntered;
            parts.Host.RoomChanged += OnRoomChanged;
            Caption.Attach(parts.Host);
            m_OnFms = Comfort.Bind(m_Entries, m_Clock);
            m_OnSeq = Seq.Bind(m_Entries, m_Clock);
        }

        /// <summary>The Done row was picked, or the exhibit chosen has run its tasks, or the walker asked to go home.</summary>
        bool MenuDone { get { return m_DonePicked || Exhibits.ActiveCompleted || Return.Active; } }
        bool m_DonePicked;

        /// <summary>The walker's room has been scanned or loaded: offered to beat 1 and the walk.</summary>
        public bool SetRoom(RoomModel room)
        {
            bool ok = Scan.SetRoom(room);
            if (ok) Walk.SetRoom(Scan.Room);
            return ok;
        }

        /// <summary>Starts the run at beat 1. A room the host already has (the pre-scan fast path) is taken first.</summary>
        public void Begin()
        {
            if (Parts.Host.Room != null) SetRoom(Parts.Host.Room);
            Director.Begin(m_Clock());
        }

        /// <summary>The operator's <c>SceneCmd</c>, handed in by the app at step 8b. True when it was the director's.</summary>
        public bool OnOperatorCommand(in OperatorCommand command)
        {
            return Director.OnOperatorCommand(in command, m_Clock());
        }

        /// <summary>The app or the exhibit installer bound the chosen exhibit to its entered space.</summary>
        public void OnExhibitReady()
        {
            Exhibits.OnExhibitReady();
        }

        /// <summary>Once a frame, after the kernel has stepped and after the input service's tick.</summary>
        public void Update(in PlaygroundFrame frame)
        {
            if (m_Disposed) return;
            double now = frame.Time;
            Still.Update(new HeadPose(frame.Time, frame.HeadPosition, frame.HeadRotation, frame.Tracking));
            var floor = new float2(frame.HeadPosition.x, frame.HeadPosition.z);
            switch (Director.Beat)
            {
                case PlaygroundBeat.Centre: Centre.Update(floor); break;
                case PlaygroundBeat.Dial: DialStep.Update(); break;
                case PlaygroundBeat.Walk345:
                    Walk.Update();
                    BeginProbeWalk();
                    break;
                case PlaygroundBeat.FreeCurvedWalk:
                case PlaygroundBeat.Compare:
                    Compare.Update();
                    break;
            }
            if (Director.Beat >= PlaygroundBeat.Dial && Director.Beat <= PlaygroundBeat.Compare && Parts.Dial.Q != m_DialShown)
            {
                m_DialShown = Parts.Dial.Q;
                Caption.SetDial(m_DialShown);
            }
            if (ProbeInput != null) ProbeInput.Tick(new UnityEngine.Vector3(floor.x, frame.HeadPosition.y, floor.y),
                new UnityEngine.Quaternion(frame.HeadRotation.value.x, frame.HeadRotation.value.y, frame.HeadRotation.value.z,
                    frame.HeadRotation.value.w), frame.DeltaTime);
            Return.Tick(frame.DeltaTime, in frame.Walker, new double2(floor.x, floor.y));
            Comfort.Tick(now);
            Prompts.Tick(now);
            Card.Tick(frame.DeltaTime);
            Director.Update(now);
        }

        /// <summary>The Console's FMS or SEQ entry, when the app carries entries by hand (the Editor, a keyboard).</summary>
        public void Enter(OperatorEntryKind kind, int value)
        {
            ManualOperatorEntryFeed manual = m_Entries as ManualOperatorEntryFeed;
            if (manual != null) manual.Raise(kind, value);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Director.BeatEntered -= OnBeatEntered;
            Director.BeatLeft -= OnBeatLeft;
            Director.RunFinished -= OnRunFinished;
            Walk.ProbeDue -= OnProbeDue;
            Map.Fired -= OnAction;
            Menu.Chosen -= OnChosen;
            Menu.DonePicked -= OnDonePicked;
            Parts.Host.SpaceEntered -= OnSpaceEntered;
            Parts.Host.RoomChanged -= OnRoomChanged;
            m_Entries.Entered -= m_OnFms;
            m_Entries.Entered -= m_OnSeq;
            Caption.Dispose();
            Menu.Dispose();
            if (ProbeInput != null) ProbeInput.Dispose();
        }

        // Beat 9 and 10 entry actions. The menu beat opens nothing by itself: the walker opens the menu (Start, a
        // sign, a long press), as in beat 8's exit.
        void EnterMenu()
        {
            m_DonePicked = false;
        }

        // The walk needs the room for its loop side; an operator jump here before the scan leaves it unarmed.
        void EnterWalk()
        {
            if (Scan.Room == null && Parts.Host.Room != null) SetRoom(Parts.Host.Room);
            if (Scan.Room != null) Walk.Enter();
        }

        void EnterReturn()
        {
            Menu.Close();
            Return.Begin();
        }

        void OnBeatEntered(PlaygroundBeat beat)
        {
            switch (beat)
            {
                case PlaygroundBeat.Centre:
                    if (!Comfort.IsRunning) Comfort.Start(m_Clock());
                    break;
                case PlaygroundBeat.Walk345:
                    m_ProbeQ = 0;
                    break;
                case PlaygroundBeat.FreeCurvedWalk:
                case PlaygroundBeat.Compare:
                    Caption.ShowText(CaptionTexts.ForMode((int)Parts.Mode.Mode));
                    break;
            }
        }

        void OnBeatLeft(PlaygroundBeat beat, PlaygroundBeat next)
        {
            switch (beat)
            {
                case PlaygroundBeat.Walk345:
                    Probe.Disarm(m_Clock());
                    Walk.Leave();
                    break;
                case PlaygroundBeat.Compare:
                    Compare.Leave();
                    break;
                case PlaygroundBeat.ExhibitMenu:
                    // Before the next beat's line is shown, so that it does not wait behind the exhibit's.
                    Exhibits.Deactivate();
                    break;
                case PlaygroundBeat.FreeCurvedWalk:
                    // An operator jump past beat 8 leaves the beacon and map on; leaving 7 for 8 keeps them.
                    if (next != PlaygroundBeat.Compare) Compare.Leave();
                    break;
            }
        }

        void OnRunFinished()
        {
            Comfort.Stop();
            Seq.Ask(m_Clock());
        }

        // The pointing probe measures from where the walk started. The flag is at c0 = (-s/2, -s/2) of the room frame.
        void BeginProbeWalk()
        {
            if (Walk.Q == m_ProbeQ || Walk.Q == 0 || Scan.Room == null) return;
            m_ProbeQ = Walk.Q;
            double half = Scan.Room.LapSide * 0.5;
            Probe.Begin(-half, -half);
        }

        void OnProbeDue(int q)
        {
            Probe.Arm(m_Clock());
        }

        void OnChosen(ExhibitId id)
        {
            Exhibits.OnChosen(id);
        }

        void OnDonePicked()
        {
            m_DonePicked = true;
        }

        void OnSpaceEntered(SpaceId id)
        {
            Card.BindSpace(id);
            if (id == SpaceId.Home) Exhibits.Deactivate();
        }

        void OnRoomChanged(RoomModel room)
        {
            if (room != null) SetRoom(room);
        }

        // The twins of the plan's table, in the order of its rows.
        void OnAction(PgActionEvent e)
        {
            double now = m_Clock();
            PlaygroundBeat beat = Director.Beat;
            switch (e.Action)
            {
                case PgAction.Confirm:
                    // While the probe is armed, A is the probe's press, not a confirmation.
                    if (Probe.IsArmed) break;
                    if (beat == PlaygroundBeat.HomeAppears) Scan.Confirm();
                    Prompts.Confirm(now);
                    break;
                case PgAction.DialStep:
                    if (DialLive(beat)) { if (e.Value >= 0) DialHand.Next(); else DialHand.Prev(); }
                    break;
                case PgAction.DialTo:
                    if (DialLive(beat)) DialHand.Select(e.Value);
                    break;
                case PgAction.Mode:
                    if (beat == PlaygroundBeat.FreeCurvedWalk || beat == PlaygroundBeat.Compare)
                    {
                        Compare.OpenModeChip();
                        Caption.ShowText(CaptionTexts.ForMode((int)Parts.Mode.Mode));
                    }
                    break;
                case PgAction.ExhibitOpen:
                case PgAction.ExhibitStep:
                case PgAction.ExhibitPick:
                    Menu.OnAction(e);
                    if (e.Action == PgAction.ExhibitOpen && Menu.IsOpen) Compare.OpenMenu();
                    break;
                case PgAction.ReturnHome:
                    Return.OnAction(e);
                    break;
            }
        }

        static bool DialLive(PlaygroundBeat beat)
        {
            return beat >= PlaygroundBeat.Dial && beat <= PlaygroundBeat.Compare;
        }

        /// <summary>A beat step made of two delegates, for beats 9 and 10 whose conditions live in the shell.</summary>
        sealed class BeatSteps : IBeatStep
        {
            readonly PlaygroundBeat m_Beat;
            readonly Action m_Enter;
            readonly Func<bool> m_Complete;

            public BeatSteps(PlaygroundBeat beat, Action enter, Func<bool> complete)
            {
                m_Beat = beat;
                m_Enter = enter;
                m_Complete = complete;
            }

            public PlaygroundBeat Beat { get { return m_Beat; } }
            public void Enter() { m_Enter(); }
            public bool Complete { get { return m_Complete(); } }
        }
    }
}
