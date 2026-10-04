using System;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Core
{
    // The Poincare disc picture of the network and its Mobius re-centring (contract section 5.2). Engine-free; no allocation.
    public static class PoincareMap
    {
        public static double2 Project(double r, double theta)
        {
            double rho = Math.Tanh(r / 2);
            return new double2(rho * Math.Cos(theta), rho * Math.Sin(theta));
        }

        public static double OneMinusRho2(double r)
        {
            double c = Math.Cosh(r / 2);
            return 1.0 / (c * c);
        }

        // (z - a) / (1 - conj(a) z)
        public static double2 Mobius(double2 z, double2 a)
        {
            double nr = z.x - a.x, ni = z.y - a.y;
            double dr = 1 - (a.x * z.x + a.y * z.y), di = -(a.x * z.y - a.y * z.x);
            double q = dr * dr + di * di;
            return new double2((nr * dr + ni * di) / q, (ni * dr - nr * di) / q);
        }

        // (w + a) / (1 + conj(a) w)
        public static double2 MobiusInverse(double2 w, double2 a)
        {
            double nr = w.x + a.x, ni = w.y + a.y;
            double dr = 1 + (a.x * w.x + a.y * w.y), di = a.x * w.y - a.y * w.x;
            double q = dr * dr + di * di;
            return new double2((nr * dr + ni * di) / q, (ni * dr - nr * di) / q);
        }

        public static double MobiusOneMinusRho2(double2 z, double oneMinusZ2, double2 a, double oneMinusA2)
        {
            double dr = 1 - (a.x * z.x + a.y * z.y), di = -(a.x * z.y - a.y * z.x);
            return oneMinusA2 * oneMinusZ2 / (dr * dr + di * di);
        }

        public static double Distance(double2 z, double oneMinusZ2, double2 w, double oneMinusW2)
        {
            double dx = z.x - w.x, dy = z.y - w.y;
            return 2 * Math.Asinh(Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(oneMinusZ2 * oneMinusW2));
        }

        public static double Distance(double2 z, double2 w)
        {
            return Distance(z, 1 - (z.x * z.x + z.y * z.y), w, 1 - (w.x * w.x + w.y * w.y));
        }

        public static double2 EaseCentre(double2 from, double2 to, double t)
        {
            double2 v = Mobius(to, from);
            double len = Math.Sqrt(v.x * v.x + v.y * v.y);
            if (len == 0) return from;
            double s = 3 * t * t - 2 * t * t * t;
            double scale = Math.Tanh(s * Math.Atanh(len)) / len;
            return MobiusInverse(new double2(v.x * scale, v.y * scale), from);
        }

        public static void ProjectAll(NetworkModel model, double2 centre, double2[] points, double[] oneMinusRho2)
        {
            double oneMinusA2 = 1 - (centre.x * centre.x + centre.y * centre.y);
            for (int i = 0, n = model.NodeCount; i < n; i++)
            {
                double2 z = Project(model.Radius(i), model.Theta(i));
                points[i] = Mobius(z, centre);
                oneMinusRho2[i] = MobiusOneMinusRho2(z, OneMinusRho2(model.Radius(i)), centre, oneMinusA2);
            }
        }

        // hypernet.geodesic_disk (k points, through the hyperboloid), re-centred. When the nodes coincide (d < 1e-9) the
        // reference returns one point; it is written k times so the caller's count stays fixed.
        public static void Geodesic(NetworkModel model, int a, int b, double2 centre, int k, double2[] output, int offset)
        {
            double3 p = model.HyperboloidPoint(a), q = model.HyperboloidPoint(b);
            double d = model.Distance(a, b);
            if (d < 1e-9)
            {
                double2 z0 = Mobius(new double2(p.x / (1 + p.z), p.y / (1 + p.z)), centre);
                for (int i = 0; i < k; i++) output[offset + i] = z0;
                return;
            }
            double sinhD = Math.Sinh(d);
            for (int i = 0; i < k; i++)
            {
                double s = k > 1 ? (double)i / (k - 1) : 0.0;   // np.linspace(0, 1, k)
                double w1 = Math.Sinh((1 - s) * d), w2 = Math.Sinh(s * d);
                double gx = (w1 * p.x + w2 * q.x) / sinhD, gy = (w1 * p.y + w2 * q.y) / sinhD, gz = (w1 * p.z + w2 * q.z) / sinhD;
                output[offset + i] = Mobius(new double2(gx / (1 + gz), gy / (1 + gz)), centre);
            }
        }
    }
}
