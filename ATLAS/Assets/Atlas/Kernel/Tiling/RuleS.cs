using System;

namespace Atlas.Kernel
{
    // Master Plan §10.2.3 Rule S: each generation = one turn tile + one straight tile; the next fork is the tile at the end
    // (R3 §D3, convention verbatim). All arithmetic is checked: overflow throws. [P2-B01]
    public static class RuleS
    {
        // Edge numbering in a tree frame (a fork or bend frame produced by Step): 0 faces back (R3 §D3).
        public const int BackEdge = 0, RightEdge = 1, StraightEdge = 2, LeftEdge = 3;

        // MP §10.2.3: "23 generations is the addressable depth".
        public const int MaxAddressLength = 23;

        // acosh(1 + 2 phi), MP §10.2.3 "2.1226 units"; units of L.
        public const double ForkSpacing = 2.1225501;

        // Fork tile frame of an address (MP §10.2.3 FrameOf): folds M = BendToFork(ChildBend(M, c)).
        public static TileFrame FrameOf(string address, in TileFrame trachea)
        {
            Check(address);
            var m = trachea;
            for (int i = 0; i < address.Length; i++) m = BendToFork(ChildBend(m, address[i]));
            return m;
        }

        // Forward frame of the bend tile in front of a non-root fork address: ChildBend(FrameOf(parent), last letter).
        public static TileFrame BendFrameOf(string address, in TileFrame trachea)
        {
            Check(address);
            if (address.Length == 0) throw new ArgumentException("the root fork has no bend tile", nameof(address));
            return ChildBend(FrameOf(address.Substring(0, address.Length - 1), trachea), address[address.Length - 1]);
        }

        // The turn tile: fork.Step(left = 3, right = 1).
        public static TileFrame ChildBend(in TileFrame fork, char branch)
        {
            if (branch != 'L' && branch != 'R') throw new ArgumentException("branch must be L or R", nameof(branch));
            return fork.Step(branch == 'L' ? LeftEdge : RightEdge);
        }

        // The straight tile after the turn: the child fork.
        public static TileFrame BendToFork(in TileFrame bend) => bend.Step(StraightEdge);

        // Inverse of BendToFork (S_2 S_0 = R^2): the bend's forward frame again.
        public static TileFrame ForkToBend(in TileFrame fork) => fork.Step(BackEdge).Rotate(2);

        // Inverse of ChildBend (S_k S_0 = R^k): the parent fork's frame again.
        public static TileFrame BendToParentFork(in TileFrame bend, char branch)
        {
            if (branch != 'L' && branch != 'R') throw new ArgumentException("branch must be L or R", nameof(branch));
            return bend.Step(BackEdge).Rotate(-(branch == 'L' ? LeftEdge : RightEdge));
        }

        static void Check(string address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.Length > MaxAddressLength) throw new ArgumentOutOfRangeException(nameof(address), "more than 23 generations");
            for (int i = 0; i < address.Length; i++)
                if (address[i] != 'L' && address[i] != 'R') throw new ArgumentException("address letters must be L or R", nameof(address));
        }
    }
}
