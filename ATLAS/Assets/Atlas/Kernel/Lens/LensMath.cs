using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // Master Plan §10.4.3; Spec §11.5 (lens maths only, App. D.3). P3-B01, deviation DV1: lives in Atlas.Kernel
    // so Atlas.Spectator (P3-D03) can use it without referencing Atlas.Blades.* (§6.2).
    /// <summary>Lens profile. 3 is reserved for Teichmueller (Stretch, P3-D05); nothing is built for it.</summary>
    public enum LensProfile : byte { None = 0, Morph = 1, Tanh = 2 }

    /// <summary>Lens parameters in radians; T in [0,1] (Master Plan §10.4.3).</summary>
    public readonly struct LensParams : IEquatable<LensParams>
    {
        public readonly LensProfile Profile;
        public readonly double E, Rho0, E1, T;

        public LensParams(LensProfile profile, double e, double rho0, double e1, double t)
        { Profile = profile; E = e; Rho0 = rho0; E1 = e1; T = t; }

        // Master Plan §10.4.3 "Default": Morph, 54 deg, 10 deg, 4 deg, T = 1.
        public static LensParams Default => new LensParams(LensProfile.Morph,
            54.0 * math.PI_DBL / 180.0, 10.0 * math.PI_DBL / 180.0, 4.0 * math.PI_DBL / 180.0, 1.0);

        // Master Plan §10.4.3: all finite; 0 < e1 < rho0 < E < pi/2; 0 <= T <= 1; profile in {0,1,2}.
        public bool IsValid =>
            math.isfinite(E) && math.isfinite(Rho0) && math.isfinite(E1) && math.isfinite(T)
            && E1 > 0.0 && E1 < Rho0 && Rho0 < E && E < math.PI_DBL * 0.5
            && T >= 0.0 && T <= 1.0
            && (byte)Profile <= (byte)LensProfile.Tanh;

        public bool Equals(LensParams o) =>
            Profile == o.Profile && E == o.E && Rho0 == o.Rho0 && E1 == o.E1 && T == o.T;
        public override bool Equals(object obj) => obj is LensParams o && Equals(o);
        public override int GetHashCode() => (int)math.hash(new double4(E, Rho0, E1, T).xy) ^ ((int)Profile * 397) ^ (int)math.hash(new double4(E, Rho0, E1, T).zw);
    }

    /// <summary>Lens maths, double precision, Burst-compatible (Master Plan §10.4.3).</summary>
    public static class LensMath
    {
        // Master Plan §10.4.3: stereographic radius s = 2 tan(e/2).
        public static double Stereo(double e) => 2.0 * math.tan(0.5 * e);
        public static double InvStereo(double s) => 2.0 * math.atan(0.5 * s);

        public static double S(in LensParams p) => Stereo(p.E);
        public static double Sigma0(in LensParams p) => Stereo(p.Rho0);
        public static double S1(in LensParams p) => Stereo(p.E1);

        // Master Plan §10.4.3: K* = ln(S/s1) / ln(sigma0/s1).
        public static double KStar(in LensParams p)
        {
            double s1 = S1(p);
            return math.log(S(p) / s1) / math.log(Sigma0(p) / s1);
        }

        // Master Plan §10.4.2: Schwarz bound tan(rho0/2)/tan(E/2).
        public static double SchwarzBound(in LensParams p) => math.tan(0.5 * p.Rho0) / math.tan(0.5 * p.E);

        public static double SigmaUniform(double s, in LensParams p) => s * Sigma0(p) / S(p);

        public static double SigmaPower(double s, in LensParams p)
        {
            double s1 = S1(p);
            return s <= s1 ? s : s1 * math.pow(s / s1, 1.0 / KStar(p));
        }

        // Master Plan §10.4.3 Slider: sigma_t = (1-t) sigma_u + t sigma_p.
        public static double SigmaMorph(double s, in LensParams p)
            => (1.0 - p.T) * SigmaUniform(s, p) + p.T * SigmaPower(s, p);

        public static double SigmaUniformInverse(double sigma, in LensParams p) => sigma * S(p) / Sigma0(p);

        public static double SigmaPowerInverse(double sigma, in LensParams p)
        {
            double s1 = S1(p);
            return sigma <= s1 ? sigma : s1 * math.pow(sigma / s1, KStar(p));
        }

        // Cautionary lens, plain angles (Master Plan §10.4.3; Spec §11.5).
        public static double TanhForward(double e, in LensParams p) => p.Rho0 * math.tanh(e / p.Rho0);

        public static double TanhInverse(double eOut, in LensParams p)
        {
            if (eOut >= p.Rho0) return double.PositiveInfinity;
            double x = eOut / p.Rho0;
            return p.Rho0 * 0.5 * math.log((1.0 + x) / (1.0 - x));   // atanh x = 1/2 ln((1+x)/(1-x))
        }

        /// <summary>Source angle to shown angle. None: identity.</summary>
        public static double Forward(double e, in LensParams p)
        {
            switch (p.Profile)
            {
                case LensProfile.Morph: return InvStereo(SigmaMorph(Stereo(e), p));
                case LensProfile.Tanh: return TanhForward(e, p);
                default: return e;
            }
        }

        /// <summary>Shown angle to source angle; +inf where no source maps there (Morph and Tanh beyond rho0).</summary>
        public static double Inverse(double eOut, in LensParams p)
        {
            switch (p.Profile)
            {
                case LensProfile.Morph:
                {
                    double sigma = Stereo(eOut), s0 = Sigma0(p);
                    if (sigma > s0)
                    {
                        if (sigma - s0 > 1e-12) return double.PositiveInfinity;   // beyond rho0 (§10.4.3)
                        sigma = s0;                                               // rounding at the rim
                    }
                    double s;
                    if (p.T <= 0.0) s = SigmaUniformInverse(sigma, p);
                    else if (p.T >= 1.0) s = SigmaPowerInverse(sigma, p);
                    else
                    {
                        // Bisection on s in [0, S], 64 iterations (strictly increasing sigma_t).
                        double lo = 0.0, hi = S(p);
                        for (int i = 0; i < 64; i++)
                        {
                            double mid = 0.5 * (lo + hi);
                            if (SigmaMorph(mid, p) < sigma) lo = mid; else hi = mid;
                        }
                        s = 0.5 * (lo + hi);
                    }
                    return InvStereo(s);
                }
                case LensProfile.Tanh: return TanhInverse(eOut, p);
                default: return eOut;
            }
        }

        // Master Plan §7.1: K(e) = max(lr/lt, lt/lr) at source angle e.
        public static double Dilatation(double e, in LensParams p)
        {
            switch (p.Profile)
            {
                case LensProfile.Morph:
                {
                    double s = Stereo(e);
                    if (s <= 0.0) return 1.0;
                    double s1 = S1(p);
                    double lt = SigmaMorph(s, p) / s;
                    double dp = s <= s1 ? 1.0 : SigmaPower(s, p) / (KStar(p) * s);
                    double lr = (1.0 - p.T) * Sigma0(p) / S(p) + p.T * dp;
                    return math.max(lr / lt, lt / lr);
                }
                case LensProfile.Tanh:
                {
                    if (e <= 0.0) return 1.0;
                    double c = math.cosh(e / p.Rho0);
                    double lr = 1.0 / (c * c);                       // sech^2(e/rho0)
                    double lt = math.sin(TanhForward(e, p)) / math.sin(e);
                    return math.max(lr / lt, lt / lr);
                }
                default: return 1.0;
            }
        }
    }
}
