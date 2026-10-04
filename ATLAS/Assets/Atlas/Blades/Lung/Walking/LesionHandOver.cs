using System;
using Atlas.Kernel;
using Atlas.World;

namespace Atlas.Blades.Lung.Walking
{
    // MP §10.2.8 P2-B09 / G6 §3.8: at the end tile of the selected route the walker stands still while curvature ramps to flat
    // (HolonomyRig, RampSeconds 4.0 s of still time, inside 3-5 s; MP §0.5 rule 4, §4.6), then S6 (Slicer) is requested once.
    // DV-B09-1: AirwayParams and Mode 2 (P2-B05/P2-B04) are not on this branch, so the route (lung.route) is a constructor value and
    // only the Mode 1 ramp exists; Mode 2 would call requestSpace(Slicer) at entry (DV-G6-14).
    public sealed class LesionHandOver
    {
        readonly AirwayWing _wing;
        readonly IForkTiles _tree;
        readonly Action<SpaceId> _request;

        // route 0 = tree.json target (PH-39: never a hard-coded 4); 1 = one-fork demo (G6 DV-G6-15).
        public LesionHandOver(AirwayWing wing, IForkTiles tree, Action<SpaceId> requestSpace, int route = 0)
        {
            _wing = wing ?? throw new ArgumentNullException(nameof(wing));
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _request = requestSpace ?? throw new ArgumentNullException(nameof(requestSpace));
            string t = tree.TargetAddress ?? "";
            EndAddress = route == 0 ? t : t.Substring(0, Math.Min(1, t.Length));
            wing.EndAddress = EndAddress;   // fork events judge "correct" against the selected route (G6 §3.5)
            var prev = wing.ChamberEntered;
            wing.ChamberEntered = (a, tile) => { prev?.Invoke(a, tile); OnChamberEntered(a, tile); };
        }

        public string EndAddress { get; }
        public bool Started { get; private set; }
        public TileId StartedTile { get; private set; }
        public bool S6Requested { get; private set; }
        public double RampStillSeconds { get; private set; }

        void OnChamberEntered(string address, TileId tile)
        {
            if (Started || address != EndAddress || tile != _tree.ForkTile(EndAddress)) return;
            _wing.Phase = ValvePhase.HandOver;   // shell stays Opaque; no choice, no valve
            Started = true; StartedTile = tile;
            _wing.Rig.SetCurvatureTarget(0, _wing.L);   // rig ramps only on still frames (HolonomyRig.Step)
        }

        // MP §6.3 step 5, after AirwayWing.Tick.
        public void Tick(in FrameContext f)
        {
            if (!Started || S6Requested) return;
            if (_wing.LastRigResult.CurvatureChanged) RampStillSeconds += f.DeltaTime;
            if (_wing.Rig.Curvature == 0)
            {
                S6Requested = true;
                _request(SpaceId.Slicer);
            }
        }
    }

    // PH-13: lane A's director enters beat b through ILungBeat (MP §10.2.2). DV-B09-2: wraps the wing and hand-over directly,
    // since AirwayWingModule (P2-B05) is not on this branch.
    public sealed class AirwayBeat : ILungBeat
    {
        readonly AirwayWing _wing;
        readonly LesionHandOver _handOver;
        public AirwayBeat(AirwayWing wing, LesionHandOver handOver) { _wing = wing; _handOver = handOver; }
        public LungBeat Beat => LungBeat.B;
        public void Enter() => _wing.Enter();
        public void Exit() => _wing.Exit();
        public bool Completed => _handOver.S6Requested;
    }
}
