// Rule atlas.airway/2 (docs/case_contract.md section 10.2): every node of the model airway from (seed, address).
// Port of station/pipelines/atlas_pipelines/airway.py (uniforms, diameter_ratio, plane_rotation_deg, branch_angle_deg,
// child, Resolver). The floating-point operations are written in the order Python evaluates them, one IEEE double
// operation per Python operation, so that on a machine with the same libm the doubles are bit-equal.
// Work package P2-B01. The tree is a model: nothing here is measured on a person.
using System;
using System.Globalization;

namespace Atlas.Airway
{
    /// <summary>Three doubles in the model frame (mm, +x left, +y posterior, +z superior; not registered to a patient).</summary>
    public readonly struct Vec3d
    {
        public readonly double X, Y, Z;
        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        /// <summary>a.X*b.X + a.Y*b.Y + a.Z*b.Z, summed left to right (the order of airway.py's _dot).</summary>
        public static double Dot(in Vec3d a, in Vec3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static Vec3d Cross(in Vec3d a, in Vec3d b)
        {
            return new Vec3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        public static Vec3d Unit(in Vec3d a)
        {
            double n = Math.Sqrt(Dot(a, a));
            return new Vec3d(a.X / n, a.Y / n, a.Z / n);
        }

        public double this[int i]
        {
            get
            {
                if (i == 0) return X;
                if (i == 1) return Y;
                if (i == 2) return Z;
                throw new ArgumentOutOfRangeException(nameof(i));
            }
        }
    }

    /// <summary>
    /// One branch. <see cref="PosMm"/> is its distal end, the point where it forks; it starts at its parent's PosMm.
    /// Every node is model-generated (<see cref="Source"/>).
    /// </summary>
    public readonly struct AirwayNode
    {
        /// <summary>The "source" of every node, in the file and out of the resolver.</summary>
        public const string Source = "model";

        public readonly AirwayAddress Address;
        public readonly double RadiusMm, LenMm;
        public readonly Vec3d PosMm;
        /// <summary>Unit vector from the branch's start to its end.</summary>
        public readonly Vec3d Dir;
        /// <summary>Unit normal of the plane in which the branch left its parent. Carried while walking the address;
        /// not stored in tree.json, so it is (0, 0, 0) for a node read from the file.</summary>
        public readonly Vec3d Normal;

        public AirwayNode(AirwayAddress address, double radiusMm, double lenMm, Vec3d posMm, Vec3d dir, Vec3d normal)
        {
            Address = address; RadiusMm = radiusMm; LenMm = lenMm; PosMm = posMm; Dir = dir; Normal = normal;
        }

        public int Gen { get { return Address.Length; } }
        public double DiameterMm { get { return 2.0 * RadiusMm; } }
        public bool HasNormal { get { return Normal.X != 0.0 || Normal.Y != 0.0 || Normal.Z != 0.0; } }
    }

    /// <summary>
    /// Address -> node for one seed, lazily, with no stored tree. Keeps the nodes along the last resolved address, so a
    /// walk (or a route study) recomputes only what changed. No allocation after construction. Not thread-safe.
    /// </summary>
    public sealed class AirwayResolver
    {
        public const string Rule = "atlas.airway/2";
        public const long DefaultSeed = 101;
        /// <summary>Acceptance depth of P2-D07: any address up to this length resolves (tree.json addressable_gen).</summary>
        public const int AddressableGen = 16;

        public const double TracheaDiameterMm = 18.0;
        public const double TracheaLengthMm = 120.0;
        public const double DiameterJitter = 0.1;
        public const double LengthOverDiameter = 3.0;
        public const double LengthOverDiameterSpread = 1.0;
        public const double BranchAngleDegSymmetric = 37.0;
        public const double PlaneRotationDegMean = 90.0;
        public const double PlaneRotationSdDeg = 41.6;
        public const double PlaneRotationClipLoDeg = 0.0;
        public const double PlaneRotationClipHiDeg = 180.0;

        /// <summary>2^(-1/3) = 0.7937, by the runtime's pow as in Python (2.0 ** (-1.0 / 3.0)).</summary>
        public static readonly double DiameterRatio = Math.Pow(2.0, -1.0 / 3.0);
        static readonly double MwSymmetric = MinimumWorkAngle(DiameterRatio, DiameterRatio);
        const double DegToRad = Math.PI / 180.0; // math.radians multiplies by pi / 180
        const double RadToDeg = 180.0 / Math.PI; // math.degrees multiplies by 180 / pi
        const double Two53 = 9007199254740992.0;

        readonly long seed;
        readonly Sha256 sha = new Sha256();
        readonly byte[] msg;
        readonly int prefixLen;
        readonly AirwayNode[] path = new AirwayNode[AirwayAddress.MaxLength + 1];
        AirwayAddress cached; // path[0 .. cached.Length] hold the nodes along this address

        public AirwayResolver(long seed = DefaultSeed)
        {
            this.seed = seed;
            string prefix = Rule + "|" + seed.ToString(CultureInfo.InvariantCulture) + "|";
            prefixLen = prefix.Length;
            msg = new byte[prefixLen + AirwayAddress.MaxLength];
            for (int i = 0; i < prefixLen; i++) msg[i] = (byte)prefix[i];
            path[0] = Trachea();
            cached = AirwayAddress.Trachea;
        }

        public long Seed { get { return seed; } }

        public static AirwayNode Trachea()
        {
            return new AirwayNode(AirwayAddress.Trachea, TracheaDiameterMm / 2.0, TracheaLengthMm,
                new Vec3d(0.0, 0.0, -TracheaLengthMm), new Vec3d(0.0, 0.0, -1.0), new Vec3d(1.0, 0.0, 0.0));
        }

        /// <summary>Minimum-work branching angle (radians) of a child with diameter ratio a whose sibling has ratio b.</summary>
        public static double MinimumWorkAngle(double a, double b)
        {
            return Math.Acos((1.0 + Math.Pow(a, 4.0) - Math.Pow(b, 4.0)) / (2.0 * a * a));
        }

        /// <summary>u1..u4 of a node: the four big-endian u64 of the first 32 bytes of
        /// SHA-256("atlas.airway/2|seed|address"), each >> 11 and / 2^53; in [0, 1).</summary>
        public void Uniforms(AirwayAddress address, out double u1, out double u2, out double u3, out double u4)
        {
            int n = prefixLen + address.CopyAscii(msg, prefixLen);
            sha.Compute(msg, n);
            u1 = (sha.Word64(0) >> 11) / Two53;
            u2 = (sha.Word64(1) >> 11) / Two53;
            u3 = (sha.Word64(2) >> 11) / Two53;
            u4 = (sha.Word64(3) >> 11) / Two53;
        }

        static double RatioOf(double u1)
        {
            return DiameterRatio * (1.0 - DiameterJitter + 2.0 * DiameterJitter * u1);
        }

        /// <summary>q(address): the node's diameter over its parent's.</summary>
        public double DiameterRatioOf(AirwayAddress address)
        {
            double u1, u2, u3, u4;
            Uniforms(address, out u1, out u2, out u3, out u4);
            return RatioOf(u1);
        }

        /// <summary>Rotation (degrees) of the branching plane of the fork at this branch against the plane in which the
        /// branch left its own parent. 90 at the trachea's fork.</summary>
        public double PlaneRotationDeg(AirwayAddress fork)
        {
            if (fork.Length == 0) return PlaneRotationDegMean;
            double u1, u2, u3, u4;
            Uniforms(fork, out u1, out u2, out u3, out u4);
            double z = Math.Sqrt(-2.0 * Math.Log(1.0 - u3)) * Math.Cos(2.0 * Math.PI * u4);
            double v = PlaneRotationDegMean + PlaneRotationSdDeg * z;
            if (!(v > PlaneRotationClipLoDeg)) v = PlaneRotationClipLoDeg;
            if (v > PlaneRotationClipHiDeg) v = PlaneRotationClipHiDeg;
            return v;
        }

        /// <summary>Angle (degrees) of the branch against its parent's axis, from the rule. Not for the trachea.</summary>
        public double BranchAngleDeg(AirwayAddress address)
        {
            if (address.Length == 0) throw new ArgumentException("the trachea has no branching angle");
            double mw = MinimumWorkAngle(DiameterRatioOf(address), DiameterRatioOf(address.Sibling));
            return BranchAngleDegSymmetric + (mw - MwSymmetric) * RadToDeg;
        }

        /// <summary>One step of the rule: the child of parent on the given side. parent must carry its normal
        /// (a node out of this resolver, not one read from tree.json).</summary>
        public AirwayNode Child(in AirwayNode parent, bool right)
        {
            if (!parent.HasNormal) throw new ArgumentException("the parent has no normal: derive nodes from the rule, not from the quantised file values");
            AirwayAddress address = parent.Address.Child(right);
            double u1, u2, u3, u4;
            Uniforms(address, out u1, out u2, out u3, out u4);
            double q = RatioOf(u1);
            double radius = parent.RadiusMm * q;
            double length = 2.0 * radius * (LengthOverDiameter - LengthOverDiameterSpread + 2.0 * LengthOverDiameterSpread * u2);

            double rho = PlaneRotationDeg(parent.Address) * DegToRad;
            Vec3d turned = Vec3d.Cross(parent.Dir, parent.Normal);
            double cr = Math.Cos(rho), sr = Math.Sin(rho);
            Vec3d normal = Vec3d.Unit(new Vec3d(
                parent.Normal.X * cr + turned.X * sr,
                parent.Normal.Y * cr + turned.Y * sr,
                parent.Normal.Z * cr + turned.Z * sr));
            Vec3d side = Vec3d.Cross(normal, parent.Dir);

            double su1, su2, su3, su4;
            Uniforms(address.Sibling, out su1, out su2, out su3, out su4);
            double mw = MinimumWorkAngle(q, RatioOf(su1));
            double theta = (BranchAngleDegSymmetric + (mw - MwSymmetric) * RadToDeg) * DegToRad;
            double ct = Math.Cos(theta);
            double s = right ? -Math.Sin(theta) : Math.Sin(theta);
            Vec3d d = Vec3d.Unit(new Vec3d(
                parent.Dir.X * ct + side.X * s,
                parent.Dir.Y * ct + side.Y * s,
                parent.Dir.Z * ct + side.Z * s));
            Vec3d pos = new Vec3d(parent.PosMm.X + length * d.X, parent.PosMm.Y + length * d.Y, parent.PosMm.Z + length * d.Z);
            return new AirwayNode(address, radius, length, pos, d, normal);
        }

        /// <summary>The node at address. Pure in its result: same seed and address, same node. Walks at most the letters
        /// the address does not share with the previous call.</summary>
        public AirwayNode Node(AirwayAddress address)
        {
            int k = AirwayAddress.CommonPrefixLength(address, cached);
            if (k < address.Length)
            {
                for (int i = k; i < address.Length; i++) path[i + 1] = Child(path[i], address.IsRight(i));
                cached = address;
            }
            return path[address.Length];
        }

        public AirwayNode Node(string address) { return Node(AirwayAddress.Parse(address)); }
    }
}
