using System;

namespace Atlas.Kernel
{
    // Tile identity: X, Y, T in Z[phi], X = X0 + X1*phi, etc. (Master Plan A.5, §6.5). Bodies of Step/ContentSeed: P1-B04.
    public readonly struct TileId : IEquatable<TileId>
    {
        public readonly long X0, X1, Y0, Y1, T0, T1;

        public TileId(long x0, long x1, long y0, long y1, long t0, long t1)
        {
            X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; T0 = t0; T1 = t1;
        }

        // Tile zero's centre is the base point o = (0,0,1), so T = 1.
        public static readonly TileId Zero = new TileId(0, 0, 0, 0, 1, 0);

        public TileId Step(int edge) => throw new NotImplementedException("P1-B04");
        public ulong ContentSeed(uint worldSeed) => throw new NotImplementedException("P1-B04");

        public bool Equals(TileId o) =>
            X0 == o.X0 && X1 == o.X1 && Y0 == o.Y0 && Y1 == o.Y1 && T0 == o.T0 && T1 == o.T1;
        public override bool Equals(object o) => o is TileId t && Equals(t);
        public override int GetHashCode() => HashCode.Combine(X0, X1, Y0, Y1, T0, T1);
        public static bool operator ==(TileId a, TileId b) => a.Equals(b);
        public static bool operator !=(TileId a, TileId b) => !a.Equals(b);
        public override string ToString() => $"({X0}+{X1}φ, {Y0}+{Y1}φ, {T0}+{T1}φ)";
    }
}
