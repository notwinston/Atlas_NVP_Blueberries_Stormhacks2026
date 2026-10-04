using System;

namespace Atlas.Kernel
{
    // Exact oriented {4,5} tile frame = canonical frame of Tile * R^Spin (kernel.md §5.3, Master Plan A.5). [P2-B01]
    // Pure tile-graph arithmetic in Z[phi]; the XFrame never leaves the assembly (kernel.md §3 rule 7). default(TileFrame) is invalid: use Zero.
    public readonly struct TileFrame : IEquatable<TileFrame>
    {
        internal readonly XFrame F;

        internal TileFrame(XFrame f) { F = f; }

        // Tile zero, identity, Spin 0.
        public static readonly TileFrame Zero = new TileFrame(XFrame.Identity);

        public TileId Tile => F.Centre;

        // 0..3: the unique j with F == F.Canon() * R^j (Canon() keeps tile zero at the identity, R3 §A5).
        public int Spin
        {
            get
            {
                var m = F.Canon();
                for (int j = 0; j < 4; j++)
                {
                    if (m.Equals(F)) return j;
                    m = m.Rot1();
                }
                throw new InvalidOperationException("frame is not a rotation of its canonical frame");
            }
        }

        // F * S_edge; edge is in THIS frame's numbering. The result's edge 0 faces back (kernel.md §5.3, S_k = R^k T H).
        public TileFrame Step(int edge)
        {
            if (edge < 0 || edge > 3) throw new ArgumentOutOfRangeException(nameof(edge));
            return new TileFrame(F.Step(edge));
        }

        // F * R^n, n any integer, taken mod 4.
        public TileFrame Rotate(int quarterTurns)
        {
            int n = ((quarterTurns % 4) + 4) % 4;
            var m = F;
            for (int i = 0; i < n; i++) m = m.Rot1();
            return new TileFrame(m);
        }

        // The same edge in TileGraph / WalkerPose (canonical) numbering.
        public int CanonicalEdge(int edge) => (edge + Spin) & 3;

        // Absolute double frame, kappa = -1. Debug and shallow tests only (kernel.md §3 rule 6).
        public Iso2 ToIso2() => F.ToIso2();

        public bool Equals(TileFrame o) => F.Equals(o.F);
        public override bool Equals(object o) => o is TileFrame t && Equals(t);
        public override int GetHashCode() => Tile.GetHashCode();
        public static bool operator ==(TileFrame a, TileFrame b) => a.Equals(b);
        public static bool operator !=(TileFrame a, TileFrame b) => !a.Equals(b);
        public override string ToString() => $"{Tile} spin {Spin}";
    }
}
