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
        // S3 duck maze (design 2026-10-04 §2, §5): on by default; a fixed seed, or 0 for a fresh seed each run.
        public const string ParamMaze = "cells.maze", ParamMazeSeed = "cells.mazeSeed";
        public const string EventMazeStart = "maze.start", EventMazeWall = "maze.wall", EventMazeFound = "maze.found", EventMazeUnavailable = "maze.unavailable";
        public const double RestartSeconds = 5.0;                  // a new maze this long after the find

        readonly TileGraph graph = new TileGraph(5);
        ParamEntry viewDepth, mode, mazeParam, mazeSeedParam;
        bool modeTaken, modeArmed;
        // What the phone sent (ISpaceStateReader); the Station draws that, whatever its own registry and head say.
        bool remote;
        TileId remoteTile; int remoteTurn, remoteSheet, remoteDepth;
        int shownDepth = -1;
        // The duck maze. now: the time of the last Tick. A run is started at Enter, before the time is known and before it is
        // known whether a phone drives this copy: its clock and its maze.start (and maze.unavailable) wait for the first Tick,
        // so the Station's copy, entered before its first State, logs nothing.
        double now, runStart;
        bool runPending, unavailablePending, mazeUnavailable;
        int runCounter, remoteCode;
        CellFrame before;                                       // the walker's frame before this frame's Walk
        // This room's two grids: the maze's (null when the floor is too small for it) and the classic one (made when first needed).
        // A Station copy takes whichever of them the phone's State is on (ReadState); gridSwitched makes the next Tick redraw.
        RoomModel room;
        CellGrid mazeGrid, classicGrid;
        bool mazeRequested, gridSwitched;                       // mazeRequested: cells.maze at Configure

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

        public DuckMaze Maze { get; private set; }             // null when not in maze mode (on the Station: until a State brings one)
        public bool MazeMode { get; private set; }             // cells.maze on and the floor big enough for the 4 x 4 (or 3 x 3) grid
        public bool MazeFound { get; private set; }
        public double MazeFoundTime { get; private set; }      // core time (seconds) of the find
        public double MazeElapsed { get; private set; }        // seconds since the run started, frozen at the find
        public int MazeSteps { get; private set; }             // cell edges crossed this run
        public int MazeWallsCrossed { get; private set; }
        // cells.maze changed while entered: the space configures again and re-enters, as a fresh entry does.
        public bool NeedsReconfigure { get; private set; }

        // Group "cells" (lane D contract §1.5). Safe to call again: the entry keeps its value.
        public static void DeclareParams(ParamRegistry registry)
        {
            string group = SpaceIds.ParamGroup(SpaceId.AlignedCells);
            registry.DeclareInt(ParamViewDepth, group, 0, SightLines.ViewRange, SightLines.ViewRange);
            registry.DeclareBool(ParamMaze, group, true);
            registry.DeclareInt(ParamMazeSeed, group, 0, DuckMaze.MaxSeed, 0);
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
                mazeParam = registry.Find(ParamMaze);
                mazeSeedParam = registry.Find(ParamMazeSeed);
            }
            // The duck maze's 4 x 4 grid (3 x 3 on a 1.2 to 1.6 m floor) when cells.maze is on; a floor too small for it runs today's S3 (maze.unavailable).
            this.room = room;
            mazeRequested = mazeParam != null && mazeParam.Bool;
            mazeGrid = CellGrid.Maze(room);
            classicGrid = null;
            mazeUnavailable = mazeRequested && mazeGrid == null;
            Grid = null;
            if (mazeRequested && mazeGrid != null) UseGrid(mazeGrid, true);
            else UseGrid(ClassicGrid(), false);
            Instances.Clear();
            Entered = false; remote = false; modeTaken = false; modeArmed = false; shownDepth = -1;
            Maze = null; MazeFound = false; MazeFoundTime = 0; MazeElapsed = 0; MazeSteps = 0; MazeWallsCrossed = 0;
            NeedsReconfigure = false; runPending = false; unavailablePending = false; remoteCode = 0; gridSwitched = false;
        }

        // §4.7: the classic grid. FromRoom throws for a free rectangle whose cell side would be below 0.4 m.
        CellGrid ClassicGrid() => classicGrid ?? (classicGrid = CellGrid.FromRoom(room));

        CellGrid TryClassicGrid()
        {
            try { return ClassicGrid(); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        // The grid the walker is on: a new one gets a new walker and post loops, and ends the maze run shown.
        void UseGrid(CellGrid grid, bool maze)
        {
            MazeMode = maze;
            if (ReferenceEquals(Grid, grid)) return;
            Grid = grid;
            Walker = new CellWalker(grid, graph);
            Loops = new PostLoops(grid);
            Instances.Clear();
            Maze = null; MazeFound = false; MazeFoundTime = 0; MazeElapsed = 0; MazeSteps = 0; MazeWallsCrossed = 0;
        }

        public int ViewDepth => remote ? remoteDepth : viewDepth != null ? viewDepth.Int : SightLines.ViewRange;

        // §4.8: a start from Mode 1 (a pose of the {4,5} world, kappa = -1: SpaceLab hands it over) keeps the tile id and takes the
        // nearest quarter-turn; any other start is registered like every flat space: tile zero, unturned, in the cell under the head.
        // TurnFromCurvedWalk assumes the anchor axes equal the room axes (B10 review, X06 open point).
        // With cells.maze on, any start but Mode 1's begins a maze run instead: the maze is a game and always begins at tile zero
        // (duck maze design §2). Entered from Mode 1, the tile is kept and this entry runs classic S3 on the classic grid (no
        // maze, P7 = 0); the next entry from anywhere else is a maze run again. A room whose classic grid cannot be built runs
        // the maze.
        public void Enter(in WalkerPose start, double2 headXZ)
        {
            if (!Configured) throw new InvalidOperationException("AlignedCellsCore.Enter before Configure");
            if (!IsFinite(headXZ)) headXZ = double2.zero;
            Crossings = 0; Entered = true;
            unavailablePending = mazeUnavailable;
            bool fromCurvedWalk = start.Local.Kappa == Curvature.Hyperbolic && !start.Tile.IsNone;
            if (mazeRequested && mazeGrid != null)
            {
                CellGrid classic = fromCurvedWalk ? TryClassicGrid() : null;
                if (classic != null) UseGrid(classic, false);
                else UseGrid(mazeGrid, true);
            }
            if (MazeMode)
            {
                EnteredFromCurvedWalk = false;
                StartRun(headXZ);
            }
            else
            {
                EnteredFromCurvedWalk = fromCurvedWalk;
                if (EnteredFromCurvedWalk) Walker.EnterFromCurvedWalk(start, headXZ);
                else Walker.Reset(headXZ, new CellFrame(TileId.Zero, 0));
                Loops.Reset(Walker.Cell, Walker.Frame);
            }
            Rebuild();
        }

        // Step 5 with the time of the last Tick: a maze run never restarts through this overload.
        public bool Tick(double2 headXZ, bool rejected) => Tick(headXZ, rejected, now);

        // Step 5. rejected: KernelStep.Rejected of this frame (step 4); time: FrameContext.Time in seconds. True when the
        // instance list changed or a new maze run began.
        public bool Tick(double2 headXZ, bool rejected, double time)
        {
            if (!Entered) return false;
            now = time;
            FlushPending();
            bool changed = false;
            if (remote)
            {
                var frame = new CellFrame(remoteTile, remoteTurn);
                var cell = new int2(remoteSheet % Grid.CountX, remoteSheet / Grid.CountX);
                if (gridSwitched) { gridSwitched = false; changed = true; }
                if (changed || frame != Walker.Frame || !cell.Equals(Walker.Cell))
                {
                    Walker.Place(cell, frame); changed = true;
                    Loops.Reset(Walker.Cell, Walker.Frame);
                }
                changed |= TickRemoteMaze();
            }
            else
            {
                if (IsFinite(headXZ))
                {
                    if (rejected)
                    {
                        // The walker stands somewhere else on the same tile: no post was walked round.
                        changed = Walker.Reseat(headXZ);
                        if (changed) Loops.Reset(Walker.Cell, Walker.Frame);
                    }
                    else
                    {
                        before = Walker.Frame;
                        int n = Walker.Walk(headXZ);
                        for (int i = 0; i < n; i++) OnCrossing(i);
                        changed = n > 0;
                    }
                }
                if (MazeMode && Maze != null) changed |= TickMaze(IsFinite(headXZ) ? headXZ : Walker.LastHead);
            }
            // cells.maze moved while entered: compared with what Configure was asked for, not with the grid running (a floor too
            // small for the maze, or a Station that took the phone's grid, runs classic S3 with cells.maze on).
            if (mazeParam != null && mazeParam.Bool != mazeRequested) NeedsReconfigure = true;
            if (changed || ViewDepth != shownDepth) { Rebuild(); return true; }
            return false;
        }

        // The phone's run after this frame's crossings: the find, then the new maze RestartSeconds later. True on a new run.
        bool TickMaze(double2 headXZ)
        {
            if (!MazeFound)
            {
                // The crossings found it already when the duck's tile was entered on the way; this covers the walker standing on it.
                MazeElapsed = now - runStart;
                if (Walker.Frame.Tile.Equals(Maze.DuckTile)) Found();
                return false;
            }
            if (now - MazeFoundTime < RestartSeconds) return false;
            StartRun(headXZ);
            FlushPending();
            return true;
        }

        // The duck's tile reached: the run's steps, walls and clock stop here, and the phone logs maze.found once.
        void Found()
        {
            MazeFound = true; MazeFoundTime = now; MazeElapsed = now - runStart;
            if (Events != null)
                Events.LogJson(EventMazeFound, "{\"steps\":" + I(MazeSteps) + ",\"walls\":" + I(MazeWallsCrossed)
                    + ",\"seconds\":" + MazeElapsed.ToString("0.###", CultureInfo.InvariantCulture) + "}");
        }

        // A new maze run: tile zero, turn 0, in the cell under the head; the seed is cells.mazeSeed, or a fresh one when it is 0.
        void StartRun(double2 headXZ)
        {
            Walker.Reset(headXZ, new CellFrame(TileId.Zero, 0));
            Loops.Reset(Walker.Cell, Walker.Frame);
            int fixedSeed = mazeSeedParam != null ? mazeSeedParam.Int : 0;
            int seed = fixedSeed > 0 ? fixedSeed
                : 1 + (int)(Mix((ulong)(uint)Environment.TickCount ^ ((ulong)(uint)++runCounter << 40)) % DuckMaze.MaxSeed);
            Maze = DuckMaze.Generate(Grid, graph, Walker.Cell, seed);
            MazeSteps = 0; MazeWallsCrossed = 0; MazeFound = false; MazeFoundTime = 0; MazeElapsed = 0;
            runPending = true;
        }

        // The first Tick of a run: its clock starts, and the phone's copy logs it. A Station copy (remote) logs nothing.
        void FlushPending()
        {
            if (runPending)
            {
                runPending = false;
                runStart = now;
                if (!remote && Events != null && Maze != null)
                {
                    int2 c = Maze.StartCell;
                    Events.LogJson(EventMazeStart, "{\"seed\":" + I(Maze.Seed) + ",\"tiles\":" + I(Maze.TileCount) + ",\"duckDepth\":"
                        + I(Maze.DuckDepth) + ",\"startCell\":" + I(c.y * Grid.CountX + c.x) + "}");
                }
            }
            if (unavailablePending)
            {
                unavailablePending = false;
                if (!remote && Events != null) Events.Log(EventMazeUnavailable);
            }
        }

        // The Station's maze is the phone's, rebuilt from State.P7 (seed and start cell) and never from its own seed. The find
        // follows from the walker's tile and is latched, as the phone's is, for the rest of the run: the walker stepping off the
        // duck's tile during the celebration keeps it. The run ends with a new code, P7 = 0, or the walker on tile zero (a restart
        // relabels the walker to tile zero, where no duck stands: this catches a fixed seed restarted in the same cell, whose code
        // is the same). No events, no restarts. True when the maze changed.
        bool TickRemoteMaze()
        {
            bool wasFound = MazeFound;
            DuckMaze old = Maze;
            if (!MazeMode || !DuckMaze.TryDecode(remoteCode, out int seed, out int2 start)) Maze = null;
            else if (Maze == null || Maze.Code != remoteCode) Maze = DuckMaze.Generate(Grid, graph, start, seed);
            TileId tile = Walker.Frame.Tile;
            bool kept = wasFound && Maze != null && ReferenceEquals(old, Maze) && !tile.Equals(TileId.Zero);
            MazeFound = Maze != null && (tile.Equals(Maze.DuckTile) || kept);
            if (MazeFound && !wasFound) MazeFoundTime = now;
            return !ReferenceEquals(old, Maze);
        }

        // SplitMix64's finaliser (Steele, Lea, Flood 2014): a fresh seed from the clock and the run count.
        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
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
            state.P7 = MazeMode && Maze != null ? Maze.Code : 0f;     // duck maze: seed * 16 + start cell (< 2^24, exact); 0 = none
        }

        // The Station's side of WriteState. A State of another space, with no tile, on a grid that is neither this room's maze grid
        // nor its classic grid (P4 columns, P5 rows, P6 side), or with a cell outside its grid is ignored, so the Sheet is never read
        // with the wrong column count. A State on this room's other grid (the Station's cells.maze lags the phone's, or the phone
        // entered from Curved Walk and runs classic S3) switches this copy to that grid: the phone's grid is the one shown.
        public void ReadState(in StateMessage state)
        {
            if (!Configured || state.SpaceId != (byte)SpaceId.AlignedCells) return;
            var tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
            if (tile.IsNone) return;
            CellGrid grid = SentGrid(in state);
            if (grid == null || state.Sheet < 0 || state.Sheet >= grid.CountX * grid.CountZ) return;
            if (!ReferenceEquals(grid, Grid))
            {
                UseGrid(grid, ReferenceEquals(grid, mazeGrid));
                gridSwitched = true;
            }
            remoteTile = tile;
            remoteTurn = CellWalker.TurnFromRotation(state.Xform0, state.Xform3);
            remoteSheet = state.Sheet;
            remoteDepth = math.clamp((int)math.round(state.P3), 0, SightLines.ViewRange);
            remoteCode = (int)Math.Round(state.P7);
            remote = true;
        }

        // The grid a State is on: this copy's own, else this room's other one, when columns, rows and side match; else null.
        CellGrid SentGrid(in StateMessage state)
        {
            if (SameGrid(Grid, in state)) return Grid;
            CellGrid other = ReferenceEquals(Grid, mazeGrid) ? TryClassicGrid() : mazeGrid;
            return SameGrid(other, in state) ? other : null;
        }

        static bool SameGrid(CellGrid g, in StateMessage state)
            => g != null && (int)Math.Round(state.P4) == g.CountX && (int)Math.Round(state.P5) == g.CountZ
               && Math.Abs(state.P6 - g.Side) <= 1e-4 * Math.Max(1.0, g.Side);

        // Crossing i of this frame's Walk: count it, turn the posts round, and log cells.cross and, when due, cells.postLoop.
        // Allocates only on a crossing (the event text), as the walk.* events of S2 do; a steady frame allocates nothing.
        void OnCrossing(int i)
        {
            Walker.LastCrossing(i, out int2 from, out int dir, out CellFrame entered);
            Crossings++;
            bool loop = Loops.Cross(from, dir, entered);
            if (MazeMode && Maze != null) OnMazeCrossing(i, dir, in entered);
            if (Events == null) return;
            int2 to = from + (dir == 0 ? new int2(1, 0) : dir == 1 ? new int2(0, 1) : dir == 2 ? new int2(-1, 0) : new int2(0, -1));
            Events.LogJson(EventCross, "{\"cell\":" + I(to.y * Grid.CountX + to.x) + ",\"dir\":" + I(dir) + ",\"crossings\":" + I(Crossings)
                + ",\"tile\":\"" + entered.Tile + "\",\"turn\":" + I(entered.Turn) + "}");
            if (loop)
                Events.LogJson(EventPostLoop, "{\"post_x\":" + I(Loops.LastPost.x) + ",\"post_z\":" + I(Loops.LastPost.y)
                    + ",\"quarter_turns\":" + I(Loops.LastTurns) + ",\"same_tile\":" + (Loops.LastSameTile ? "1" : "0")
                    + ",\"tile\":\"" + entered.Tile + "\"}");
        }

        // Duck maze: crossing i of this frame is a step of the run; through a wall of the tile left it is counted and logged.
        // A crossing that enters the duck's tile is the find, even when the same frame walks on (a diagonal past a post corner);
        // after the find the run's counts stand still until the next maze.
        void OnMazeCrossing(int i, int dir, in CellFrame entered)
        {
            if (MazeFound) return;
            CellFrame left = before;
            if (i > 0) Walker.LastCrossing(i - 1, out _, out _, out left);
            MazeSteps++;
            int edge = left.TileEdge(dir);
            if (Maze.IsWall(left.Tile, edge))
            {
                MazeWallsCrossed++;
                if (Events != null) Events.LogJson(EventMazeWall, "{\"tile\":\"" + left.Tile + "\",\"edge\":" + I(edge) + "}");
            }
            if (entered.Tile.Equals(Maze.DuckTile)) Found();
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
