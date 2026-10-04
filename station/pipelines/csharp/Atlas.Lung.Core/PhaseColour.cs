// Lane A engine-free core, P2-A22 (phase colouring). Plain C#, System only. Sampling does not allocate.
using System;

namespace Atlas.Lung.Core
{
    /// <summary>
    /// Phase colour (section 10.2.5): one cyclic row of the shared palette, looked up at u = frac(tau / Lambda).
    /// The row is cyclic: texel N - 1 is followed by texel 0, so tau = 0 and tau = Lambda give the same colour.
    /// </summary>
    public sealed class PhaseColour
    {
        /// <summary>3 bytes (R, G, B) per texel.</summary>
        public readonly byte[] Row;
        public readonly int Texels;

        public PhaseColour(byte[] rowRgb)
        {
            if (rowRgb == null || rowRgb.Length < 3 || rowRgb.Length % 3 != 0) throw new ArgumentException("The row is RGB triples.");
            Row = rowRgb;
            Texels = rowRgb.Length / 3;
        }

        /// <summary>u = frac(tau / Lambda) in [0, 1).</summary>
        public static double U(double tau, double lambda)
        {
            double x = tau / lambda;
            double u = x - Math.Floor(x);
            return u >= 1.0 ? 0.0 : u;      // -1e-20 - floor(-1e-20) rounds to 1
        }

        /// <summary>u from the slice's interpolated vertex channel (tau / Lambda in [0, 1], ring 10 carries 1).</summary>
        public static double UFromPhase(double phase01) { return U(phase01, 1.0); }

        /// <summary>The texel that holds u: texel i covers [i / N, (i + 1) / N).</summary>
        public int Texel(double u)
        {
            int i = (int)Math.Floor(U(u, 1.0) * Texels);
            return i >= Texels ? 0 : i;
        }

        /// <summary>Nearest texel (point sampling).</summary>
        public void SampleNearest(double tau, double lambda, out byte r, out byte g, out byte b)
        {
            int i = 3 * Texel(U(tau, lambda));
            r = Row[i]; g = Row[i + 1]; b = Row[i + 2];
        }

        /// <summary>
        /// Linear blend between neighbouring texels with wrap (bilinear sampling of a repeating row). Texel i is
        /// taken at its left edge u = i / N, so u = 0 returns texel 0 exactly. Components in [0, 255].
        /// </summary>
        public void SampleLinear(double tau, double lambda, out float r, out float g, out float b)
        {
            double x = U(tau, lambda) * Texels;
            int i = (int)Math.Floor(x);
            if (i >= Texels) i = 0;
            float t = (float)(x - Math.Floor(x));
            int a = 3 * i, c = 3 * ((i + 1) % Texels);
            r = Row[a] + t * (Row[c] - Row[a]);
            g = Row[a + 1] + t * (Row[c + 1] - Row[a + 1]);
            b = Row[a + 2] + t * (Row[c + 2] - Row[a + 2]);
        }

        /// <summary>
        /// A stand-in row until the palette texture of P1-A06 has its phase strip: phase 0 (end of inhale) cool,
        /// phase 50 % (end of exhale) warm, lightness varied so the two halves of the breath differ. Smooth and cyclic.
        /// </summary>
        public static PhaseColour Default(int texels = 64)
        {
            var row = new byte[3 * texels];
            for (int i = 0; i < texels; i++)
            {
                double a = 2.0 * Math.PI * i / texels;
                double warm = 0.5 * (1.0 - Math.Cos(a));
                double light = 1.0 + 0.18 * Math.Sin(a);
                row[3 * i] = Byte(light * (0.20 + warm * (0.98 - 0.20)));
                row[3 * i + 1] = Byte(light * 0.55);
                row[3 * i + 2] = Byte(light * (0.95 + warm * (0.20 - 0.95)));
            }
            return new PhaseColour(row);
        }

        static byte Byte(double v)
        {
            double x = Math.Round(255.0 * v);
            return (byte)(x < 0.0 ? 0.0 : x > 255.0 ? 255.0 : x);
        }
    }
}
