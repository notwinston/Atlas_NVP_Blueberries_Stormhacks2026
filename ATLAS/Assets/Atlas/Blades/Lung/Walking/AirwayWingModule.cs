using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Safety;
using Atlas.Telemetry;
using Atlas.World;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B05. G6 §3.9: the airway wing as an ISpaceModule (S2, DV-G6-18). Frame order MP §6.3: the wing's rig steps first (step 4,
    // its own HolonomyRig, Phase 2), then the chambers and valves (step 5), both inside Tick.
    public sealed class AirwayWingModule : ISpaceModule
    {
        readonly IForkTiles _tree; readonly IClearanceQuery _clearance; readonly IForkChoice _choice; readonly IChamberView _view;
        readonly Action<SpaceId> _requestSpace;   // used by LesionHandOver (P2-B09)
        AirwayModeSwitch _switch;

        public AirwayWingModule(IForkTiles tree, IClearanceQuery clearance, IForkChoice choice, IChamberView view, Action<SpaceId> requestSpace)
        {
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _clearance = clearance ?? throw new ArgumentNullException(nameof(clearance));
            _choice = choice ?? throw new ArgumentNullException(nameof(choice));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _requestSpace = requestSpace;
        }

        public SpaceId Id => SpaceId.CurvedWalk;                          // DV-G6-18
        // Constant from Configure to Exit (ISpaceModule): the wing starts every entry in Mode 1 and never matches the room frame.
        public bool Registered => false;
        public AirwayWing Wing { get; private set; }
        public AirwayModeSwitch ModeSwitch => _switch;

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            _switch?.Dispose();
            AtlasServices.TryGet<IEventLog>(out var log);
            _switch = new AirwayModeSwitch(registry, room, log as EventLog);
            Wing = new AirwayWing(room, _tree, _clearance, _choice, _view);
        }

        public void Enter(in WalkerPose start) { Wing.Enter(); }

        public void Tick(in FrameContext frame)
        {
            Wing.KernelStep(frame);
            Wing.RequestedMode = _switch.Requested;
            Wing.Tick(frame);
        }

        public void Exit()
        {
            Wing?.Exit();
            _switch?.Dispose();
        }

        public void WriteState(ref StateMessage state) { Wing?.WriteState(ref state); }
    }
}
