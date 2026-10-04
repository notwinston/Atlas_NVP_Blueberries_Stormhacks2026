using System;
using Atlas.Kernel;
using Atlas.Safety;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking
{
    // MP §10.2.3 airway walking, Mode 1 (curved walk), valve chamber steps 1-5 (P2-B03). Mode 2 (P2-B04), the mode switch (P2-B05)
    // and the hand-over (P2-B09) extend this class; ChamberEntered and the internal Phase setter are their hooks.
    // Frame order MP §6.3: KernelStep = step 4 (rig), Tick = step 5 (chambers, valves).
    public sealed class AirwayWing
    {
        public const int WalkableGenerations = 16;   // MP §10.2.3 "Walkable depth"

        readonly RoomModel _room;
        readonly IForkTiles _tree;
        readonly IChamberView _view;
        readonly IForkChoice _choice;
        readonly TileGraph _graph = new TileGraph(5);   // canonical, exact ids equal P2-B01's (kernel.md §5.3)
        readonly LapCorners _lap;
        readonly ValveSolver _solver;
        readonly HolonomyRig _rig;
        readonly double _l;

        bool _pendingEnter, _entered, _nextIsFork;
        double2 _headXZ;
        long _frame;
        HolonomyRigResult _last;

        public AirwayWing(RoomModel room, IForkTiles tree, IClearanceQuery clearance, IForkChoice choice, IChamberView view)
        {
            _room = room ?? throw new ArgumentNullException(nameof(room));
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _choice = choice ?? throw new ArgumentNullException(nameof(choice));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _l = room.curvature.L5;
            _lap = new LapCorners(room.lapSquare.s);
            _solver = new ValveSolver(_lap, clearance);
            _rig = new HolonomyRig((TileId t, int e, out Iso2 g) => { g = _graph.StepMatrix(t, e); return _graph.Neighbour(t, e); });
            Address = "";
            ExitEdge = -1;
        }

        public AirwayMode Mode => AirwayMode.CurvedWalk;
        public string Address { get; private set; }
        public TileId CurrentTile { get; private set; }
        public TileId NextTile { get; private set; }
        public int ExitEdge { get; private set; }
        public ValvePhase Phase { get; internal set; } = ValvePhase.Walking;
        public ShellState Shell { get; private set; } = ShellState.Dithered;
        public double L => _l;                                    // room.curvature.L5, never changed while walking
        public double ChamberEnteredTime { get; private set; }
        public HolonomyRig Rig => _rig;
        public HolonomyRigResult LastRigResult => _last;
        public Func<bool> BeaconShown { get; set; } = () => false;
        public Action<string, TileId> ChamberEntered { get; set; }
        public event Action<ReaimRecord> Reaimed;

        public double2 CurrentChamberWorld => _entered && Geom(CurrentTile, out _, out var w) ? w : _headXZ;
        public double2 NextChamberWorld => _entered && Geom(NextTile, out _, out var w) ? w : _headXZ;
        public double CurrentChamberDistance => _entered && Geom(CurrentTile, out var d, out _) ? d : double.PositiveInfinity;
        public double NextChamberDistance => _entered && Geom(NextTile, out var d, out _) ? d : double.PositiveInfinity;

        public void Enter() { _pendingEnter = true; }

        public void Exit() { _pendingEnter = false; _entered = false; }

        // MP §6.3 step 4.
        public void KernelStep(in FrameContext f)
        {
            _frame = f.Frame;
            _headXZ = new double2(f.HeadPosition.x, f.HeadPosition.z);
            if (!_pendingEnter) _last = _rig.Step(f.Frame, new HeadSample(f.Frame, f.Time, (double3)f.HeadPosition, f.TrackingNormal));
            if (_pendingEnter)
            {
                _pendingEnter = false;
                // DV-G6-11: the root chamber centre goes to the lap corner nearest the head; U = Boost(-v/L), heading 0.
                // DV-B03-3: Reset runs before this frame's rig.Step (G6 §3.4 has it after) so the first Step after Reset only
                // records the head and no frame of displacement is lost between the placement and the next frame.
                double2 v = _lap.Corner(_lap.Nearest(_headXZ)) - _headXZ;
                _rig.Reset(new WalkerPose { Tile = _tree.Root, Local = Iso2.Boost(-v / _l, -1) }, -1, _l);
                CurrentTile = NextTile = _tree.Root;
                Address = ""; ExitEdge = -1; _nextIsFork = false;
                _entered = true;
                ChamberEnteredTime = f.Time;
                Shell = ShellState.Opaque;
                _view.SetShell(CurrentTile, ShellState.Opaque);
                _view.SetIris(CurrentTile, -1);
                _last = _rig.Step(f.Frame, new HeadSample(f.Frame, f.Time, (double3)f.HeadPosition, f.TrackingNormal));
                Phase = Address.Length >= WalkableGenerations ? ValvePhase.DeadEnd : ValvePhase.AwaitChoice;
            }
        }

        // MP §6.3 step 5.
        public void Tick(in FrameContext f)
        {
            if (!_entered) return;
            _headXZ = new double2(f.HeadPosition.x, f.HeadPosition.z);
            switch (Phase)
            {
                case ValvePhase.Walking:
                    // step 1: entering within 0.5 m of the next centre closes the valve and makes the shell opaque
                    if (NextChamberDistance <= ValveSolver.ChamberRadius) EnterChamber(f);
                    break;
                case ValvePhase.AwaitChoice:
                    if (_choice.TryGetChoice(Address, out char c) && (c == 'L' || c == 'R'))
                    {
                        Choose(c, f);
                        TryValve(f);
                    }
                    break;
                case ValvePhase.AwaitGlyph:
                case ValvePhase.Waiting:
                    TryValve(f);
                    break;
            }
        }

        void EnterChamber(in FrameContext f)
        {
            bool enteredFork = _nextIsFork;
            CurrentTile = NextTile;
            Shell = ShellState.Opaque;
            _view.SetShell(CurrentTile, ShellState.Opaque);
            _view.SetIris(CurrentTile, -1);
            _view.SetTurnPrompt(false, double2.zero, false);
            ChamberEnteredTime = f.Time;
            // bend (corridor) chambers have one exit, towards the fork of Address; a fork offers two branches (step 2)
            if (!enteredFork) { NextTile = _tree.ForkTile(Address); ExitEdge = Edge(CurrentTile, NextTile); _nextIsFork = true; }
            else _nextIsFork = false;
            ChamberEntered?.Invoke(Address, CurrentTile);
            if (Phase == ValvePhase.HandOver) return;   // B09 took over
            if (!enteredFork) { Phase = ValvePhase.AwaitGlyph; return; }
            if (Address.Length >= WalkableGenerations) { Phase = ValvePhase.DeadEnd; _view.SetTurnPrompt(true, double2.zero, true); }   // DV-G6-13
            else Phase = ValvePhase.AwaitChoice;
        }

        // MP §10.2.3 step 2 and G6 §3.4 "Tree position".
        void Choose(char c, in FrameContext f)
        {
            string a = Address;
            ForkEvent.Emit(new ForkEventData
            {
                address = a, choice = c.ToString(), correct = IsCorrect(a, c), beacon_shown = BeaconShown(),
                t_s = (float)(f.Time - ChamberEnteredTime)
            });
            Address = a + c;
            _tree.Focus(Address);
            if (!_graph.TryCommonNeighbour(_tree.ForkTile(a), _tree.ForkTile(Address), out var corridor))
                throw new InvalidOperationException("no unique corridor tile between forks '" + a + "' and '" + Address + "'");
            NextTile = corridor;
            ExitEdge = Edge(CurrentTile, NextTile);
            _nextIsFork = false;
            Phase = ValvePhase.AwaitGlyph;
        }

        // G6 §3.5: before a route is selected (B09) the end address is tree.TargetAddress.
        bool IsCorrect(string address, char c)
        {
            string end = EndAddress ?? _tree.TargetAddress;
            return end != null && end.Length > address.Length && end.StartsWith(address, StringComparison.Ordinal) && end[address.Length] == c;
        }

        internal string EndAddress { get; set; }

        int Edge(TileId from, TileId to)
        {
            int e = _graph.EdgeTo(from, to);
            if (e < 0) throw new InvalidOperationException("tiles are not adjacent");
            return e;
        }

        // Steps 3-5. Waits for the walker on the glyph (DV-G6-1), then solves, re-aims while opaque and opens the iris.
        void TryValve(in FrameContext f)
        {
            // DV-B03-4: "stands on the glyph" = within 0.15 m and still (speed <= 0.1 m/s); rotating a moving walker would move the
            // chamber centre by up to 2 delta (G6 §8 pitfall 3).
            if (!_last.Still || !Geom(CurrentTile, out double dist, out double2 world) || dist > ValveSolver.GlyphRadius) return;
            var d = _solver.Solve(_lap.Nearest(world));   // DV-G6-2
            if (!d.Open)
            {
                if (Phase != ValvePhase.Waiting) { Phase = ValvePhase.Waiting; _view.SetTurnPrompt(true, double2.zero, true); }
                return;
            }
            double alpha = ValveSolver.ReaimAngle(_rig.Pose.Local, ExitEdge, d.Direction);
            ReaimOpaque(alpha, f);
            _view.SetTurnPrompt(true, d.Direction, false);   // step 4: floor arrow along d*
            _view.SetIris(CurrentTile, ExitEdge);            // step 5: iris opens at d*
            Shell = ShellState.Dithered;
            _view.SetShell(CurrentTile, ShellState.Dithered);
            Phase = ValvePhase.Walking;
        }

        // MP §0.5 rule 3 exception: the rotation is applied only while the shell is opaque.
        void ReaimOpaque(double alpha, in FrameContext f)
        {
            if (Shell != ShellState.Opaque) throw new InvalidOperationException("valve rotation while the shell is not opaque");
            _rig.Reaim(alpha);
            Reaimed?.Invoke(new ReaimRecord(f.Frame, alpha, Shell, CurrentTile, Mode, 0, 0));
        }

        // Distance (m) and anchor-xz position (MP §4.6 warp) of the centre of tile t seen from the walker.
        bool Geom(TileId t, out double dist, out double2 world)
        {
            var pose = _rig.Pose;
            double3 q;
            if (pose.Tile == t) q = Geometry2.BasePoint;
            else
            {
                int e = _graph.EdgeTo(pose.Tile, t);
                if (e < 0) { dist = double.PositiveInfinity; world = new double2(double.NaN, double.NaN); return false; }
                q = _graph.StepMatrix(pose.Tile, e).M.c2;
            }
            dist = _l * Geometry2.Distance(pose.Local.M.c2, q, -1);
            world = _headXZ + _l * Geometry2.Log(math.mul(pose.Local.Inverse().M, q), -1);
            return true;
        }
    }
}
