// Lane A engine-free core, P2-A28 (scanner tilt). Plain C#, System only. Nothing here allocates.
using System;

namespace Atlas.Lung.Core
{
    /// <summary>
    /// The z-tau hyperplane of a scanner table moving at speed v (Appendix A.8): the table samples z at time
    /// t(z) = t0 + (z - z0) / v. With the time axis scaled by c (w units per second) that is a hyperplane
    /// tilted by theta against the snapshot w = const, tan(theta) = c / v. theta = 0 is the snapshot
    /// (v infinite). The normal is (0, 0, -sin theta, cos theta) on (table axis, w): the convention of the
    /// Phase 1 slicer tests, equal to +w at theta = 0.
    /// </summary>
    public static class ScannerTilt
    {
        public const int DetentCount = 5;

        /// <summary>
        /// Table speeds of the five dial detents in mm/s, detent 0 being the snapshot (theta = 0).
        /// The plan fixes the number of detents (5) and the test speed 20 mm/s, not the other speeds: these
        /// are the speeds of the R2 prototype table plus 80 mm/s, and are a default for the owner to confirm.
        /// </summary>
        public static readonly double[] DefaultDetentSpeedsMmPerS = { double.PositiveInfinity, 80.0, 40.0, 20.0, 10.0 };

        /// <summary>Time scale c of a case tube in display units per second: one breath (Lambda) per breathing period.</summary>
        public static double TimeScale(double lambda, double periodSeconds) { return lambda / periodSeconds; }

        /// <summary>tan(theta) = c / v, with c and v in the same length unit per second. theta = 0 for an infinite v.</summary>
        public static double Theta(double c, double v)
        {
            if (double.IsPositiveInfinity(v)) return 0.0;
            return Math.Atan2(c, v);
        }

        /// <summary>The table speed read-out for a tilt: v = c / tan(theta); infinite at theta = 0.</summary>
        public static double Speed(double c, double theta)
        {
            double tan = Math.Tan(theta);
            return tan == 0.0 ? double.PositiveInfinity : c / tan;
        }

        /// <summary>Tilt of a detent for a tube in display units: c = Lambda / period, v = table speed in mm/s times M / 1000.</summary>
        public static double DetentTheta(int detent, double[] detentSpeedsMmPerS, double lambda, double periodSeconds, double unitsPerMm)
        {
            return Theta(TimeScale(lambda, periodSeconds), detentSpeedsMmPerS[detent] * unitsPerMm);
        }

        /// <summary>The detent nearest to a dial position in [0, 1] (0 = snapshot), clamped.</summary>
        public static int DetentFromDial(double dial01, int detents = DetentCount)
        {
            int d = (int)Math.Floor(dial01 * (detents - 1) + 0.5);
            return d < 0 ? 0 : d > detents - 1 ? detents - 1 : d;
        }

        /// <summary>One detent up or down (triggers), clamped at both ends.</summary>
        public static int StepDetent(int detent, int delta, int detents = DetentCount)
        {
            int d = detent + delta;
            return d < 0 ? 0 : d > detents - 1 ? detents - 1 : d;
        }

        /// <summary>
        /// The cut for tilt theta that passes through table position <paramref name="zAt"/> at lane coordinate
        /// <paramref name="wAt"/> (the scan "meets" that point), with the scanner picture (w dropped).
        /// <paramref name="tableAxis"/> is the index of the table axis among x, y, z: 2 in the case contract's
        /// display frame (z = superior), 1 after a swap to Unity's y-up frame.
        /// </summary>
        public static SliceFrame Frame(double theta, int tableAxis, double zAt, double wAt)
        {
            double s = Math.Sin(theta), c = Math.Cos(theta);
            double nx = tableAxis == 0 ? -s : 0.0, ny = tableAxis == 1 ? -s : 0.0, nz = tableAxis == 2 ? -s : 0.0;
            return SliceFrame.DropW(nx, ny, nz, c, -s * zAt + c * wAt);
        }

        /// <summary>
        /// The same cut applied to period copy j of a tube whose w wraps with period lambda: slicing the copy
        /// shifted by j lambda equals slicing the stored period with this frame. A tilted cut crosses several
        /// periods, so the scanner view is the union of the slices for j = ..., -1, 0, 1, ...
        /// </summary>
        public static SliceFrame ForCopy(in SliceFrame frame, int j, double lambda)
        {
            SliceFrame f = frame;
            f.Offset = (float)((double)frame.Offset - (double)frame.Nw * j * lambda);
            return f;
        }

        /// <summary>Apparent length along the table axis in millimetres from the slice's extent in display units.</summary>
        public static double ApparentLengthMm(float min, float max, double unitsPerMm) { return ((double)max - min) / unitsPerMm; }

        /// <summary>Unit test value of A.8: a sphere of diameter D moving at constant speed u appears with length D v / (v - u).</summary>
        public static double ConstantSpeedLength(double diameter, double v, double u) { return diameter * v / (v - u); }
    }
}
