using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // Z[phi] element u + v*phi, phi^2 = phi + 1 (Master Plan A.5). All arithmetic checked: overflow throws (kernel.md §5.6).
    internal readonly struct ZPhi : IEquatable<ZPhi>
    {
        public readonly long U, V;
        public ZPhi(long u, long v) { U = u; V = v; }
        public static readonly ZPhi Zero = new ZPhi(0, 0);
        public static readonly ZPhi One = new ZPhi(1, 0);
        public static ZPhi operator +(ZPhi a, ZPhi b) => new ZPhi(checked(a.U + b.U), checked(a.V + b.V));
        public static ZPhi operator -(ZPhi a, ZPhi b) => new ZPhi(checked(a.U - b.U), checked(a.V - b.V));
        public static ZPhi operator -(ZPhi a) => new ZPhi(checked(-a.U), checked(-a.V));
        // (u + v phi) * phi = v + (u + v) phi   // Master Plan A.5
        public ZPhi TimesPhi() => new ZPhi(V, checked(U + V));
        public double ToDouble() => U + V * ExactTiling.Phi;
        public bool Equals(ZPhi o) => U == o.U && V == o.V;
        public override bool Equals(object o) => o is ZPhi z && Equals(z);
        public override int GetHashCode() => unchecked((int)(U * 31 + V));
        public static bool operator ==(ZPhi a, ZPhi b) => a.Equals(b);
        public static bool operator !=(ZPhi a, ZPhi b) => !a.Equals(b);
    }

    // Tile frame M = [[a11,a12,sqrt(phi) b1],[a21,a22,sqrt(phi) b2],[sqrt(phi) c1,sqrt(phi) c2,d]], entries in Z[phi] (A.5, kernel.md §5.4).
    internal readonly struct XFrame : IEquatable<XFrame>
    {
        public readonly ZPhi A11, A12, A21, A22, B1, B2, C1, C2, D;

        public XFrame(ZPhi a11, ZPhi a12, ZPhi a21, ZPhi a22, ZPhi b1, ZPhi b2, ZPhi c1, ZPhi c2, ZPhi d)
        { A11 = a11; A12 = a12; A21 = a21; A22 = a22; B1 = b1; B2 = b2; C1 = c1; C2 = c2; D = d; }

        public static readonly XFrame Identity = new XFrame(ZPhi.One, ZPhi.Zero, ZPhi.Zero, ZPhi.One,
            ZPhi.Zero, ZPhi.Zero, ZPhi.Zero, ZPhi.Zero, ZPhi.One);

        public TileId Centre => new TileId(B1.U, B1.V, B2.U, B2.V, D.U, D.V);

        // M*R, R = [[0,-1,0],[1,0,0],[0,0,1]] (A.5)
        public XFrame Rot1() => new XFrame(A12, -A11, A22, -A21, B1, B2, C2, -C1, D);

        // M*T*H, T = [[phi,0,sqrt(phi)],[0,1,0],[sqrt(phi),0,phi]], H = diag(-1,-1,1); additions only (A.5, R3 §A5)
        public XFrame StepT() => new XFrame(
            -(A11 + B1).TimesPhi(), -A12,
            -(A21 + B2).TimesPhi(), -A22,
            A11 + B1.TimesPhi(), A21 + B2.TimesPhi(),
            -(C1.TimesPhi() + D), -C2,
            (C1 + D).TimesPhi());

        // M*S_k, S_k = R^k*T*H (A.5)
        public XFrame Step(int k)
        {
            var m = this;
            for (int i = 0; i < k; i++) m = m.Rot1();
            return m.StepT();
        }

        // Canonical frame (kernel.md §5.3, R3 §A5): tile zero keeps the identity; otherwise the lexicographically smallest key over M*R^j.
        public XFrame Canon()
        {
            if (Centre == TileId.Zero) return Identity;
            var best = this; var m = this;
            for (int j = 1; j < 4; j++)
            {
                m = m.Rot1();
                if (KeyLess(m, best)) best = m;
            }
            return best;
        }

        static bool KeyLess(XFrame a, XFrame b)
        {
            if (a.A11.U != b.A11.U) return a.A11.U < b.A11.U;
            if (a.A11.V != b.A11.V) return a.A11.V < b.A11.V;
            if (a.A21.U != b.A21.U) return a.A21.U < b.A21.U;
            if (a.A21.V != b.A21.V) return a.A21.V < b.A21.V;
            if (a.C1.U != b.C1.U) return a.C1.U < b.C1.U;
            return a.C1.V < b.C1.V;
        }

        public Iso2 ToIso2()
        {
            double s = ExactTiling.SqrtPhi;
            return new Iso2(new double3x3(
                A11.ToDouble(), A12.ToDouble(), s * B1.ToDouble(),
                A21.ToDouble(), A22.ToDouble(), s * B2.ToDouble(),
                s * C1.ToDouble(), s * C2.ToDouble(), D.ToDouble()), -1);
        }

        public bool Equals(XFrame o) => A11 == o.A11 && A12 == o.A12 && A21 == o.A21 && A22 == o.A22 &&
                                         B1 == o.B1 && B2 == o.B2 && C1 == o.C1 && C2 == o.C2 && D == o.D;
        public override bool Equals(object o) => o is XFrame f && Equals(f);
        public override int GetHashCode() => Centre.GetHashCode();
    }

    // A point (sqrt(phi) X, sqrt(phi) Y, T) relative to a frame.
    internal readonly struct ZPoint
    {
        public readonly ZPhi X, Y, T;
        public ZPoint(ZPhi x, ZPhi y, ZPhi t) { X = x; Y = y; T = t; }

        // S_k^-1 = H*T^-1*R^-k, additions only: R^-1 (x,y) = (y,-x); then x' = T - phi X, y' = -Y, T' = phi (T - X)
        public ZPoint InverseStep(int k)
        {
            ZPhi x = X, y = Y;
            for (int i = 0; i < k; i++) { var nx = y; y = -x; x = nx; }
            return new ZPoint(T - x.TimesPhi(), -y, (T - x).TimesPhi());
        }
    }

    internal static class ExactTiling
    {
        // Master Plan A.3; kernel.md §5.1
        public const double Phi = 1.618033988749895, SqrtPhi = 1.272019649514069;

        // Canonical frame of any {4,5} tile id by descent from tile zero (kernel.md §9 point 2, R3 §A5).
        public static XFrame CanonicalFrame(TileId id)
        {
            if (id == TileId.Zero) return XFrame.Identity;
            try
            {
                var m = XFrame.Identity;
                var p = new ZPoint(id.X, id.Y, id.T);
                double last = double.PositiveInfinity;
                for (int iter = 0; iter < 256; iter++)
                {
                    if (p.X == ZPhi.Zero && p.Y == ZPhi.Zero)
                    {
                        if (p.T != ZPhi.One) throw new ArgumentException("not a {4,5} tile id");
                        return m.Canon();
                    }
                    double x = p.X.ToDouble(), y = p.Y.ToDouble();
                    int k = 0; double best = x;
                    if (y > best) { best = y; k = 1; }
                    if (-x > best) { best = -x; k = 2; }
                    if (-y > best) { k = 3; }
                    m = m.Step(k); p = p.InverseStep(k);
                    double t = p.T.ToDouble();
                    if (!(t < last)) throw new ArgumentException("not a {4,5} tile id");
                    last = t;
                }
                throw new ArgumentException("not a {4,5} tile id");
            }
            catch (OverflowException)
            {
                throw new ArgumentException("not a {4,5} tile id (or beyond the 64-bit range)");
            }
        }
    }
}
