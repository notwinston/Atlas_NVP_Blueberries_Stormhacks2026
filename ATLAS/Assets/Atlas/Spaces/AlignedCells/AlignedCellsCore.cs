using System;
using System.Globalization;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // The logic of S3 Aligned Cells with no engine object (Master Plan §4.7, §4.8, §6.5): the grid from the RoomModel, the walker's
    // cell and tile, the sight-line instances, the parameters and the State. AlignedCellsSpace is the component round it.
    // The frame loop's kernel stays flat and registered in S3 (lane B contract §4); the tile under each cell is this class's own.
    public sealed class AlignedCellsCore
    {
        public const string ParamViewDepth = "cells.viewDepth";   // kill switch of §12.2: fewer neighbour cells drawn
        public const string ParamMode = "curved.mode";            // declared by the app (AppParams); 0 = Mode 1, 1 = Mode 2
        public const int ModeCurvedWalk = 0, ModeAlignedCells = 1;
        public const string EventCross = "cells.cross";           // §10.1.3: one per cell edge crossed
        public const string EventPostLoop = "cells.postLoop";     // §10.1.3: four or more quarter-turns round one interior post

        readonly TileGraph graph = new TileGraph(5);
        ParamEntry viewDepth, mode;
        bool modeTaken, modeArmed;
        // What the phone sent (ISpaceStateReader); the Station draws that, whatever its own registry and head say.
        bool remote;
        TileId remoteTile; int remoteTurn, remoteSheet, remoteDepth;
        int shownDepth = -1;

        public TileGraph Graph => graph;
        public CellGrid Grid { get; private set; }
        public CellWalker Walker { get; private set; }
        public CellInstanceList Instances { get; } = new CellInstanceList();
        public bool Configured => Grid != null;
        public bool Entered { get; private set; }
        public bool Remote => remote;
        public int Crossings { get; private set; }            // cell edges crossed since Enter
        public bool EnteredFromCurvedWalk { get; private set; }
        public PostLoops Loops { get; private set; }           // quarter-turns round the posts of the walker's cell
        // Where cells.cross and cells.postLoop go; null drops them. The phone's copy only: a Station copy (Remote) logs nothing.
        public IEventLog Events { get; set; }

        // Group "cells" (lane D contract §1.5). Safe to call again: the entry keeps its value.
        public static void DeclareParams(ParamRegistry registry)
        {
            registry.DeclareInt(ParamViewDepth, SpaceIds.ParamGroup(SpaceId.AlignedCells), 0, SightLines.ViewRange, SightLines.ViewRange);
        }

        // §4.7 grid from RoomModel.Aligned. A free rectangle whose cell side is below 0.4 m throws ArgumentOutOfRangeException;
        // SpaceLab then logs space.error and falls back to Home.
        public void Configure(RoomModel room, ParamRegistry registry)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (registry != null)
            {
                DeclareParams(registry);
                // The handles are kept and polled in Tick: no callback outlives this module.
                viewDepth = registry.Find(ParamViewDepth);
                mode = registry.Find(ParamMode);
            }
            Grid = CellGrid.FromRoom(room);
            Walker = new CellWalker(Grid, graph);
            Loops = new PostLoops(Grid);
            Instances.Clear();
            Entered = false; remote = false; modeTaken = false; modeArmed = false; shownDepth = -1;
        }

        public int ViewDepth => remote ? remoteDepth : viewDepth != null ? viewDepth.Int : SightLines.ViewRange;

        // §4.8: a start from Mode 1 (a pose of the {4,5} world, kappa = -1: SpaceLab hands it over) keeps the tile id and takes the
        // nearest quarter-turn; any other start is registered like every flat space: tile zero, unturned, in the cell under the head.
        // TurnFromCurvedWalk assumes the anchor axes equal the room axes (B10 review, X06 open point).
        public void Enter(in WalkerPose start, double2 headXZ)
        {
            if (!Configured) throw new InvalidOperationException("AlignedCellsCore.Enter before Configure");
            EnteredFromCurvedWalk = start.Local.Kappa == Curvature.Hyperbolic && !start.Tile.IsNone;
            if (!IsFinite(headXZ)) headXZ = double2.zero;
            if (EnteredFromCurvedWalk) Walker.EnterFromCurvedWalk(start, headXZ);
            else Walker.Reset(headXZ, new CellFrame(TileId.Zero, 0));
            Crossings = 0; Entered = true;
            Loops.Reset(Walker.Cell, Walker.Frame);
            Rebuild();
        }

        // Step 5. rejected: KernelStep.Rejected of this frame (step 4). True when the instance list changed.
        public bool Tick(double2 headXZ, bool rejected)
        {
            if (!Entered) return false;
            bool changed = false;
            if (remote)
            {
                var frame = new CellFrame(remoteTile, remoteTurn);
                var cell = new int2(remoteSheet % Grid.CountX, remoteSheet / Grid.CountX);
                if (frame != Walker.Frame || !cell.Equals(Walker.Cell))
                {
                    Walker.Place(cell, frame); changed = true;
                    Loops.Reset(Walker.Cell, Walker.Frame);
                }
            }
            else if (IsFinite(headXZ))
            {
                if (rejected)
                {
                    // The walker stands somewhere else on the same tile: no post was walked round.
                    changed = Walker.Reseat(headXZ);
                    if (changed) Loops.Reset(Walker.Cell, Walker.Frame);
                }
                else
                {
                    int n = Walker.Walk(headXZ);
                    for (int i = 0; i < n; i++) OnCrossing(i);
                    changed = n > 0;
                }
            }
            if (changed || ViewDepth != shownDepth) { Rebuild(); return true; }
            return false;
        }

        // curved.mode (§12.2, risk 5): the switch between the two modes is one parameter. This space holds it at Mode 2 while it is
        // entered (taken in the first Tick, when it is known whether a phone drives this copy) and gives it back at Exit, so
        // Curved Walk never finds a stale Mode 2. True, once, when the operator moved it to Mode 1: the adapter then asks the host
        // for S2 and the old space goes on ticking through the fade without asking again.
        public bool PollModeSwitch()
        {
            if (!Entered || remote || mode == null) return false;
            if (!modeTaken)
            {
                mode.Set(ModeAlignedCells);
                modeTaken = true;
                modeArmed = mode.Int == ModeAlignedCells;
                return false;
            }
            if (!modeArmed || mode.Int != ModeCurvedWalk) return false;
            modeArmed = false;
            return true;
        }

        // The host refused the switch (S2 not in this build, no room): the parameter goes back to what is shown.
        public void RefuseModeSwitch()
        {
            if (mode == null || !modeTaken) return;
            mode.Set(ModeAlignedCells);
            modeArmed = mode.Int == ModeAlignedCells;
        }

        public void Exit()
        {
            if (Entered && modeTaken && mode != null && mode.Int == ModeAlignedCells) mode.Set(ModeCurvedWalk);
            modeTaken = false; modeArmed = false; Entered = false; remote = false;
            Instances.Clear();
        }

        // App. B.1, lane D contract §2.3: tileId = the walker's tile, sheet = the cell index (row * columns + column), xform = the
        // tile's quarter-turn as the rotation Rot(-Turn * 90 deg), exact; p[3] = view depth, p[4], p[5] = columns and rows, p[6] = g.
        // Everything else is the frame loop's.
        public void WriteState(ref StateMessage state)
        {
            if (!Entered) return;
            CellFrame f = Walker.Frame;
            TileId t = f.Tile;
            state.SetTile(t.Xu, t.Xv, t.Yu, t.Yv, t.Tu, t.Tv);
            int2 c = Walker.Cell;
            state.Sheet = (short)(c.y * Grid.CountX + c.x);
            double cos = f.Turn == 0 ? 1 : f.Turn == 2 ? -1 : 0, sin = f.Turn == 1 ? -1 : f.Turn == 3 ? 1 : 0;
            state.Xform0 = cos; state.Xform1 = -sin; state.Xform2 = 0;
            state.Xform3 = sin; state.Xform4 = cos; state.Xform5 = 0;
            state.Xform6 = 0; state.Xform7 = 0; state.Xform8 = 1;
            state.P3 = shownDepth;
            state.P4 = Grid.CountX;
            state.P5 = Grid.CountZ;
            state.P6 = (float)Grid.Side;
            state.P7 = 0f;
        }

        // The Station's side of WriteState. A State of another space, with no tile or with a cell outside this grid is ignored.
        public void ReadState(in StateMessage state)
        {
            if (!Configured || state.SpaceId != (byte)SpaceId.AlignedCells) return;
            var tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
            if (tile.IsNone || state.Sheet < 0 || state.Sheet >= Grid.CountX * Grid.CountZ) return;
            remoteTile = tile;
            remoteTurn = CellWalker.TurnFromRotation(state.Xform0, state.Xform3);
            remoteSheet = state.Sheet;
            remoteDepth = math.clamp((int)math.round(state.P3), 0, SightLines.ViewRange);
            remote = true;
        }

        // Crossing i of this frame's Walk: count it, turn the posts round, and log cells.cross and, when due, cells.postLoop.
        // Allocates only on a crossing (the event text), as the walk.* events of S2 do; a steady frame allocates nothing.
        void OnCrossing(int i)
        {
            Walker.LastCrossing(i, out int2 from, out int dir, out CellFrame entered);
            Crossings++;
            bool loop = Loops.Cross(from, dir, entered);
            if (Events == null) return;
            int2 to = from + (dir == 0 ? new int2(1, 0) : dir == 1 ? new int2(0, 1) : dir == 2 ? new int2(-1, 0) : new int2(0, -1));
            Events.LogJson(EventCross, "{\"cell\":" + I(to.y * Grid.CountX + to.x) + ",\"dir\":" + I(dir) + ",\"crossings\":" + I(Crossings)
                + ",\"tile\":\"" + entered.Tile + "\",\"turn\":" + I(entered.Turn) + "}");
            if (loop)
                Events.LogJson(EventPostLoop, "{\"post_x\":" + I(Loops.LastPost.x) + ",\"post_z\":" + I(Loops.LastPost.y)
                    + ",\"quarter_turns\":" + I(Loops.LastTurns) + ",\"same_tile\":" + (Loops.LastSameTile ? "1" : "0")
                    + ",\"tile\":\"" + entered.Tile + "\"}");
        }

        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        void Rebuild()
        {
            shownDepth = ViewDepth;
            SightLines.Build(Grid, graph, Walker.Cell, Walker.Frame, Instances, shownDepth);
        }

        static bool IsFinite(double2 p) => math.all(math.isfinite(p));
    }
}
