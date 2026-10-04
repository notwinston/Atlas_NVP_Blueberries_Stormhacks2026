using System;
using Atlas.App;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.Spaces.Home;
using Atlas.Spaces.Portal;
using Atlas.Spaces.Slicer;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    // The four built spaces as SpaceLab drives them, with no engine. Each module here is the space's own
    // engine-free core behind ISpaceModule, called in the order its MonoBehaviour adapter calls it; what the
    // adapters add on top (meshes, materials, renderers) needs Unity and is left to the Unity-side check.

    /// <summary>S0 Home: <see cref="HomeSpaceCore"/>.</summary>
    sealed class HomeModule : ISpaceModule
    {
        public readonly HomeSpaceCore Core = new HomeSpaceCore();
        public int Ticks;

        public SpaceId Id { get { return SpaceId.Home; } }
        public bool Registered { get { return Core.Registered; } }

        public void Configure(RoomModel room, ParamRegistry registry) { Core.Configure(room, registry, null); }
        public void Enter(in WalkerPose start) { Core.Enter(); }
        public void Tick(in FrameContext frame) { Ticks++; }
        public void Exit() { Core.Exit(); }
        public void WriteState(ref StateMessage state) { HomeSpaceCore.WriteState(ref state); }
    }

    /// <summary>S1 Glued: <see cref="GluedLattice"/>, <see cref="GluedRules"/> and <see cref="GluedTierFollower"/>.</summary>
    sealed class GluedModule : ISpaceModule
    {
        readonly ISpaceHost _host;
        readonly GluedCell[] _cells = new GluedCell[GluedRules.MaxCells];
        ParamEntry _mode, _copies;
        GluedTierFollower _tier;
        int _shownMode = -1, _shownCopies = -1;
        bool _entered, _hasWalker;

        public GluedModule(ISpaceHost host) { _host = host; }

        public GluedLattice Lattice;
        public GluedCell Walker;
        /// <summary>Cell edges crossed since Enter.</summary>
        public int Crossings;
        /// <summary>Cells laid out round the walker at the last change.</summary>
        public int CellsDrawn;

        public SpaceId Id { get { return SpaceId.Glued; } }
        public bool Registered { get { return true; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            GluedSpace.DeclareParams(registry);
            _mode = registry.Find(GluedSpace.ParamMode);
            _copies = registry.Find(GluedSpace.ParamCopies);
            Lattice = new GluedLattice(room, GluedRules.LayoutOf(_mode.Int));
        }

        public void Enter(in WalkerPose start)
        {
            _entered = true;
            _hasWalker = false;
            Crossings = 0;
            _tier = default(GluedTierFollower);
            Walker = Lattice.CellOf(float3.zero);
            _shownMode = -1;
            Refresh(false);
        }

        public void Tick(in FrameContext frame)
        {
            if (!_entered) return;
            int wanted = _tier.Step(frame.Tier, _copies.Int);
            if (wanted != _copies.Int) _copies.Set(wanted);

            bool changed = Refresh(true);
            GluedCell cell = Lattice.CellOf(frame.HeadPosition);
            if (!_hasWalker || !cell.Equals(Walker) || changed)
            {
                if (_hasWalker && !changed && !cell.Equals(Walker)) Crossings++;
                Walker = cell;
                _hasWalker = true;
                CellsDrawn = Lattice.FillCopies(Walker, _shownCopies, _cells);
            }
        }

        public void Exit() { _entered = false; }

        public void WriteState(ref StateMessage state)
        {
            state.SetTile(Walker.Col, Walker.Row, 0, 0, 0, 0);
            state.P3 = (float)Lattice.Layout;
            state.P4 = GluedRules.CopiesPerSide(Lattice.Layout, _copies.Int);
            state.P5 = (float)Lattice.SizeX;
            state.P6 = (float)Lattice.SizeZ;
            state.P7 = 0f;
        }

        bool Refresh(bool announce)
        {
            int mode = (int)GluedRules.LayoutOf(_mode.Int);
            int copies = GluedRules.CopiesPerSide((GluedLayout)mode, _copies.Int);
            if (mode == _shownMode && copies == _shownCopies) return false;
            _shownMode = mode;
            _shownCopies = copies;
            Lattice = new GluedLattice(in Lattice, (GluedLayout)mode);
            if (announce && _host != null) _host.BumpWorldVersion();
            if (!announce) CellsDrawn = Lattice.FillCopies(Walker, _shownCopies, _cells);
            return true;
        }
    }

    /// <summary>
    /// S5 Impossible Rooms: <see cref="PortalSpaceCore"/> (the room graph), <see cref="PortalMath"/> (the head
    /// through a doorway) and <see cref="PortalSwapRule"/> (when a room that was left may be switched off).
    /// No camera exists here, so no eye ever sees a room through a doorway: the hold rule alone decides.
    /// </summary>
    sealed class PortalModule : ISpaceModule, IUploadStep
    {
        readonly ISpaceHost _host;
        readonly PortalSwapRule _swap = new PortalSwapRule(16);
        ParamRegistry _registry;
        RoomModel _room;
        float3[] _min = new float3[0], _max = new float3[0];
        bool[] _on = new bool[0];
        float3 _previousHead;
        bool _hasPrevious;

        public PortalModule(ISpaceHost host) { _host = host; }

        public readonly PortalSpaceCore Core = new PortalSpaceCore();
        public int CurrentRoom { get; private set; }
        /// <summary>Doorways the head went through since Enter.</summary>
        public int Doorways;
        /// <summary>Frame index of the last doorway, and of the last room switched off by the hold rule.</summary>
        public int LastDoorwayFrame = -1, LastSwapFrame = -1;
        public int LastSwappedRoom = -1;
        /// <summary>A room was switched off while the head stood where it and the walker's room overlap.</summary>
        public bool SwappedInOverlap;

        public SpaceId Id { get { return SpaceId.Portal; } }
        public bool Registered { get { return true; } }

        public bool IsOn(int room) { return room >= 0 && room < _on.Length && _on[room]; }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            _room = room;
            _registry = registry;
            Core.Configure(room, registry);
            Build();
        }

        public void Enter(in WalkerPose start)
        {
            _hasPrevious = false;
            Doorways = 0;
            SetCurrent(Core.Layout.StartRoom);
        }

        public void Tick(in FrameContext frame)
        {
            if (!Core.RebuildPending) return;
            Core.Generate();
            Build();
            SetCurrent(Core.Layout.StartRoom);
            if (_host != null) _host.BumpWorldVersion();
        }

        public void Upload(in FrameContext frame)
        {
            // The cameras are at the raw head pose, so the doorways are judged from it too.
            float3 head = frame.RawHeadPosition;
            if (_hasPrevious) DetectCrossing(head, frame.FrameIndex);
            _previousHead = head;
            _hasPrevious = true;

            for (int k = 0; k < _on.Length; k++)
            {
                if (k == CurrentRoom || !_on[k]) continue;
                bool inOverlap = PortalMath.InOverlap(head, _min[k], _max[k], _min[CurrentRoom], _max[CurrentRoom]);
                if (!_swap.Step(k, false, false, inOverlap)) continue;
                _on[k] = false;
                LastSwapFrame = frame.FrameIndex;
                LastSwappedRoom = k;
                if (inOverlap) SwappedInOverlap = true;
            }
        }

        public void Exit()
        {
            Core.Exit(_registry);
        }

        public void WriteState(ref StateMessage state) { Core.WriteState(ref state); }

        void DetectCrossing(float3 head, int frameIndex)
        {
            PortalLayout layout = Core.Layout;
            for (int k = 0; k < layout.Doors.Count; k++)
            {
                LayoutDoor door = layout.Doors[k];
                float4 plane = PortalMath.DoorPlane(door.A, door.B);
                for (int direction = 0; direction < 2; direction++)
                {
                    int viewer = direction == 0 ? door.From : door.To;
                    if (viewer != CurrentRoom) continue;
                    float2 left = direction == 0 ? door.A : door.B, right = direction == 0 ? door.B : door.A;
                    float4 viewerPlane = direction == 0 ? plane : PortalMath.Flip(plane);
                    if (!PortalMath.Crossed(viewerPlane, left, right, door.Height, _previousHead, head)) continue;
                    int old = CurrentRoom;
                    int target = direction == 0 ? door.To : door.From;
                    Doorways++;
                    LastDoorwayFrame = frameIndex;
                    SetCurrent(target);
                    Core.OnRoomChanged(old, target);
                    return;
                }
            }
        }

        void SetCurrent(int room)
        {
            CurrentRoom = room;
            Core.SetCurrentRoom(room);
            _on[room] = true;
            _swap.Reset(room);
        }

        void Build()
        {
            PortalLayout layout = Core.Layout;
            int count = layout.RoomCount;
            _min = new float3[count];
            _max = new float3[count];
            _on = new bool[count];
            _swap.EnsureCapacity(count);
            _swap.Reset();
            for (int r = 0; r < count; r++)
            {
                FloorRect[] cells = layout.Rooms[r].Cells;
                float2 lo, hi;
                float height;
                if (cells.Length == 0)
                {
                    // The scanned room, wall to wall, with the slack the adapter gives it.
                    lo = new float2(-0.5f * _room.FreeRect.Wf, -0.5f * _room.FreeRect.Df);
                    hi = -lo;
                    for (int k = 0; k < _room.Outline.Length; k++)
                    {
                        lo = math.min(lo, _room.Outline[k]);
                        hi = math.max(hi, _room.Outline[k]);
                    }
                    lo -= 0.5f;
                    hi += 0.5f;
                    height = _room.MaxWallHeight + 0.5f;
                }
                else
                {
                    lo = cells[0].Min;
                    hi = cells[0].Max;
                    for (int k = 1; k < cells.Length; k++)
                    {
                        lo = math.min(lo, cells[k].Min);
                        hi = math.max(hi, cells[k].Max);
                    }
                    height = math.max(layout.WallHeight, layout.BoothHeight);
                }
                _min[r] = new float3(lo.x, -0.1f, lo.y);
                _max[r] = new float3(hi.x, height, hi.y);
            }
        }
    }

    /// <summary>S6 Slicer: <see cref="SlicerParams"/>, <see cref="SlicerLayout"/> and <see cref="SlicerController"/>.</summary>
    sealed class SlicerModule : ISpaceModule
    {
        SlicerParams _params;
        bool _entered;

        public SlicerLayout Layout;
        public SlicerController Controller;

        public SpaceId Id { get { return SpaceId.Slicer; } }
        public bool Registered { get { return true; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            _params = SlicerParams.Declare(registry);
            Layout = SlicerLayout.FromRoom(room.FreeRect.Wf, room.FreeRect.Df, room.Standing);
            Controller = new SlicerController(in Layout);
        }

        public void Enter(in WalkerPose start)
        {
            _params.Load(Controller);
            _entered = true;
        }

        public void Tick(in FrameContext frame)
        {
            if (!_entered) return;
            _params.TakeOutsideChanges(Controller);
            SlicerInput input = default(SlicerInput);
            input.Floor = frame.HeadPosition.xz;
            input.DeltaTime = frame.DeltaTime;
            Controller.Step(in input);
            _params.Store(Controller);
        }

        public void Exit() { _entered = false; }

        public void WriteState(ref StateMessage state)
        {
            state.P3 = Controller.W;
            state.P4 = Controller.Xw;
            state.P5 = Controller.Yw;
            state.P6 = Controller.Zw;
            state.P7 = SlicerState.Pack(_params.ObjectValue, _params.ViewValue);
        }
    }

    /// <summary>
    /// Makes the modules above for SpaceLab, the way the scene loader makes the MonoBehaviour ones: a module
    /// is ready on the frame after it was asked for. The three spaces of lane B are not built and are refused.
    /// </summary>
    sealed class RehearsalSpaceLoader : ISpaceLoader
    {
        ISpaceHost _host;
        SpaceId _target;
        bool _begun;

        public ISpaceModule Last { get; private set; }
        public int Loads { get; private set; }
        public int Unloads { get; private set; }

        public void Bind(ISpaceHost host) { _host = host; }

        public bool IsAvailable(SpaceId id)
        {
            return id == SpaceId.Home || id == SpaceId.Glued || id == SpaceId.Portal || id == SpaceId.Slicer;
        }

        public void Begin(SpaceId id)
        {
            _target = id;
            _begun = true;
        }

        public bool TryFinish(out ISpaceModule module)
        {
            module = null;
            if (!_begun) return true;
            _begun = false;
            switch (_target)
            {
                case SpaceId.Home: module = new HomeModule(); break;
                case SpaceId.Glued: module = new GluedModule(_host); break;
                case SpaceId.Portal: module = new PortalModule(_host); break;
                case SpaceId.Slicer: module = new SlicerModule(); break;
            }
            Last = module;
            Loads++;
            return true;
        }

        public void Unload(ISpaceModule module)
        {
            Unloads++;
            if (ReferenceEquals(Last, module)) Last = null;
        }
    }

    /// <summary>The world fade, recorded: there is no room content to dim.</summary>
    sealed class RehearsalStage : ISpaceStage
    {
        public float Fade = 1f;
        public int Resets;
        /// <summary>The fade reached black at least once since the last look.</summary>
        public bool WentBlack;

        public void SetFade(float fade)
        {
            Fade = fade;
            if (fade <= 0f) WentBlack = true;
        }

        public void ResetForSpace() { Resets++; }
    }
}
