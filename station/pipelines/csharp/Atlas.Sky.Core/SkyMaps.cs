// The baked needle-sky maps and the direction -> clearance lookup (P2-B10). Plain C#, System only.
//
// Reads sky/risk_p0..9.r8 and sky/robust.r8 (raw u8, width x height, row-major, row 0 at +z, no header). A lookup is a
// direction -> pixel -> byte -> millimetres: no per-call allocation, no state, nothing computed per frame ("0 ms compute").
using System;
using System.IO;
using Atlas.Cases;

namespace Atlas.Sky
{
    public sealed class SkyMaps
    {
        public readonly SkyInfo Info;
        public readonly int Width, Height, Phases;
        readonly byte[][] risk;
        readonly byte[] robust;
        double robustShare = -2.0;

        SkyMaps(SkyInfo info, byte[][] risk, byte[] robust)
        {
            Info = info; Width = info.Width; Height = info.Height; Phases = info.Phases;
            this.risk = risk; this.robust = robust;
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Loads sky.json and the eleven maps from the directory that holds them (<c>cases/lung101/sky</c>).</summary>
        public static SkyMaps Load(string skyDirectory)
        {
            SkyInfo info = SkyInfo.Parse(File.ReadAllBytes(Path.Combine(skyDirectory, "sky.json")));
            var risk = new byte[info.Phases][];
            for (int k = 0; k < info.Phases; k++) risk[k] = File.ReadAllBytes(Path.Combine(skyDirectory, info.RiskFiles[k]));
            byte[] robust = File.ReadAllBytes(Path.Combine(skyDirectory, info.RobustFile));
            return FromBytes(info, risk, robust);
        }

        /// <summary>For an adapter that has the bytes already (a TextAsset, a streaming-assets read). The arrays are kept, not copied.</summary>
        public static SkyMaps FromBytes(SkyInfo info, byte[][] risk, byte[] robust)
        {
            if (info == null || risk == null || robust == null) throw new ArgumentNullException();
            long n = (long)info.Width * info.Height;
            if (risk.Length != info.Phases) throw new ContractException("sky: expected " + info.Phases + " risk maps, got " + risk.Length);
            for (int k = 0; k < risk.Length; k++)
                if (risk[k] == null || risk[k].Length != n)
                    throw new ContractException("sky: " + info.RiskFiles[k] + " is not " + info.Width + " x " + info.Height + " bytes");
            if (robust.Length != n) throw new ContractException("sky: " + info.RobustFile + " is not " + info.Width + " x " + info.Height + " bytes");
            for (int i = 0; i < robust.Length; i++)
                if (robust[i] != 0 && robust[i] != 255) throw new ContractException("sky: robust map holds a value other than 0 and 255");
            return new SkyMaps(info, risk, robust);
        }

        // ------------------------------------------------------------------ direction -> pixel

        /// <summary>The pixel containing a direction (patient frame, any non-zero length). False for a zero or non-finite vector.</summary>
        public bool TryPixelOf(double x, double y, double z, out int col, out int row)
        {
            col = 0; row = 0;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || (x == 0.0 && y == 0.0 && z == 0.0)) return false;
            double theta, phi;
            SkyFrame.DirectionToAngles(x, y, z, out theta, out phi);
            SkyFrame.AnglesToPixel(theta, phi, Width, Height, out col, out row);
            return true;
        }

        /// <summary>The HEALPix pixel (ring, sky.json's nside) that contains a direction; what the baker used for its rays.</summary>
        public int HealpixIndex(double x, double y, double z)
        {
            return HealpixRing.Vec2PixRing(Info.Nside, x, y, z);
        }

        // ------------------------------------------------------------------ pixel -> clearance

        /// <summary>The byte of a phase map at a pixel.</summary>
        public int RiskValue(int phase, int col, int row)
        {
            if ((uint)phase >= (uint)Phases) throw new ArgumentOutOfRangeException("phase");
            if ((uint)col >= (uint)Width || (uint)row >= (uint)Height) throw new ArgumentOutOfRangeException("col/row");
            return risk[phase][col + Width * row];
        }

        /// <summary>Clearance in mm of a phase at a pixel: value * mm_per_count, a lower bound within one count (0.1 mm). 255 means "25.5 mm or more".</summary>
        public double ClearanceMm(int phase, int col, int row)
        {
            return RiskValue(phase, col, row) * Info.MmPerCount;
        }

        /// <summary>Smallest byte over the phases at a pixel.</summary>
        public int MinRiskValue(int col, int row)
        {
            if ((uint)col >= (uint)Width || (uint)row >= (uint)Height) throw new ArgumentOutOfRangeException("col/row");
            int o = col + Width * row;
            int m = 255;
            for (int k = 0; k < risk.Length; k++) { int v = risk[k][o]; if (v < m) m = v; }
            return m;
        }

        public double MinClearanceMm(int col, int row) { return MinRiskValue(col, row) * Info.MmPerCount; }

        public bool IsRobust(int col, int row)
        {
            if ((uint)col >= (uint)Width || (uint)row >= (uint)Height) throw new ArgumentOutOfRangeException("col/row");
            return robust[col + Width * row] != 0;
        }

        // ------------------------------------------------------------------ direction -> clearance

        /// <summary>Clearance in mm of one phase along a direction; NaN for a zero or non-finite vector.</summary>
        public double ClearanceMm(int phase, double x, double y, double z)
        {
            int c, r;
            return TryPixelOf(x, y, z, out c, out r) ? ClearanceMm(phase, c, r) : double.NaN;
        }

        /// <summary>Minimum over the ten phases along a direction (the number the walker reads); NaN for an unusable vector.</summary>
        public double MinClearanceMm(double x, double y, double z)
        {
            int c, r;
            return TryPixelOf(x, y, z, out c, out r) ? MinClearanceMm(c, r) : double.NaN;
        }

        /// <summary>Whether a direction lies in the robust window (clearance at or above the threshold in all ten phases).</summary>
        public bool IsRobust(double x, double y, double z)
        {
            int c, r;
            return TryPixelOf(x, y, z, out c, out r) && IsRobust(c, r);
        }

        /// <summary>The sky_pick payload for a direction: its az/el, minimum clearance over the phases and the robust flag.</summary>
        public bool TryPick(double x, double y, double z, out SkyPick pick)
        {
            int c, r;
            if (!TryPixelOf(x, y, z, out c, out r)) { pick = default(SkyPick); return false; }
            double az, el;
            SkyFrame.DirectionToAzEl(x, y, z, out az, out el);
            pick = new SkyPick(az, el, MinClearanceMm(c, r), IsRobust(c, r));
            return true;
        }

        // ------------------------------------------------------------------ read-outs and self-checks (load time, not per frame)

        /// <summary>
        /// Share of the sphere (solid angle) in the robust window, from the pixels: pixels weighted by sin(theta) of their row centre.
        /// The "robust-window share of sky in %" read-out is this times 100. Computed once and kept.
        /// </summary>
        public double RobustShare()
        {
            if (robustShare >= -1.0) return robustShare;
            double sum = 0.0, all = 0.0;
            for (int row = 0; row < Height; row++)
            {
                double w = Math.Sin(Math.PI * (row + 0.5) / Height);
                int o = row * Width, n = 0;
                for (int col = 0; col < Width; col++) if (robust[o + col] != 0) n++;
                sum += w * n;
                all += w * Width;
            }
            robustShare = sum / all;
            return robustShare;
        }

        /// <summary>
        /// Number of pixels where the robust map differs from "byte >= threshold_value in every phase"
        /// (docs/case_contract.md section 9: it must be 0). Costs one pass over eleven maps; for tests and the load check, not the frame loop.
        /// </summary>
        public int RobustRuleViolations()
        {
            int bad = 0;
            int thr = Info.ThresholdValue;
            for (int i = 0; i < robust.Length; i++)
            {
                bool all = true;
                for (int k = 0; k < risk.Length; k++) if (risk[k][i] < thr) { all = false; break; }
                if (all != (robust[i] != 0)) bad++;
            }
            return bad;
        }

        static bool IsFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
    }
}
