using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking
{
    public enum AirwayMode : byte { CurvedWalk = 1, AlignedCells = 2 }        // curved.mode values (G6 DV-G6-8)
    public enum ShellState : byte { Dithered, Opaque }                          // MP §10.2.3 steps 1, 5
    public enum ValvePhase : byte { Walking, AwaitChoice, AwaitGlyph, Waiting, HandOver, DeadEnd }

    // MP §10.2.3 step 4: one valve rotation, recorded with the shell state it happened under.
    public readonly struct ReaimRecord
    {
        public readonly long Frame; public readonly double Alpha; public readonly ShellState Shell;
        public readonly TileId Tile; public readonly AirwayMode Mode; public readonly int TurnBefore, TurnAfter;
        public ReaimRecord(long frame, double alpha, ShellState shell, TileId tile, AirwayMode mode, int turnBefore, int turnAfter)
        { Frame = frame; Alpha = alpha; Shell = shell; Tile = tile; Mode = mode; TurnBefore = turnBefore; TurnAfter = turnAfter; }
    }

    public interface IForkTiles                    // binds P2-B01 (AirwayTreeBinding.cs)
    {
        TileId Root { get; }                       // ForkTile("") the trachea
        TileId ForkTile(string address);           // MP §10.2.3: FrameOf(address, trachea).Centre, exact in Z[phi]
        string TargetAddress { get; }              // tree.json target.address (MP §10.2.4)
        string CompassForkAddress { get; }         // tree.json compass_fork.address
        void Focus(string address);                // P2-B01 lazy-window recentre (MP §10.2.3 "Lazy window")
    }

    public interface IForkChoice { bool TryGetChoice(string forkAddress, out char letter); }   // 'L' or 'R'

    public interface IChamberView
    {
        void SetShell(TileId chamber, ShellState s);
        void SetIris(TileId chamber, int tileEdge);                      // -1 = all closed; MP §10.2.3 step 5
        void SetTurnPrompt(bool visible, double2 direction, bool wait);  // MP §10.2.3 steps 3-4 (anchor xz)
    }
}
