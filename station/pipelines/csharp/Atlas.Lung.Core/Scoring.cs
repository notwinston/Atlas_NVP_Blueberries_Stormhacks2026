// Lane A engine-free core, P2-A29 (tasks M2, M3, M4): scoring and the event payloads of section 10.2.6.
// Plain C#, System only. Scoring does not allocate (the payloads are plain structs).
using System;
using Atlas.Cases;

namespace Atlas.Lung.Core
{
    /// <summary>Event names and scene of section 10.2.6 (wire format: lane D contract, section 2.6).</summary>
    public static class LungEvents
    {
        public const string Scene = "lung";
        public const string M2Place = "m2_place";
        public const string M4Gate = "m4_gate";
        public const string M3Pick = "m3_pick";
    }

    /// <summary><c>m2_place {p_mm, hit, best}</c>: the placed point (patient mm), H = rho(p), best = max rho.</summary>
    public struct M2Place
    {
        public double PxMm, PyMm, PzMm;
        public double Hit;
        public double Best;
    }

    /// <summary><c>m4_gate {start, len, itv_cm3, duty, best_cm3}</c>: the chosen arc, its gated volume, its duty, the volume of the best arc of equal duty.</summary>
    public struct M4Gate
    {
        public int Start, Len;
        public double ItvCm3;
        public double Duty;
        public double BestCm3;
    }

    /// <summary><c>m3_pick {choice, correct, when}</c>.</summary>
    public struct M3Pick
    {
        public int Choice;
        public bool Correct;
        /// <summary>"before" or "after" (beat f).</summary>
        public string When;
    }

    /// <summary>
    /// <c>occ.r8</c> (case contract section 7): occupancy rho on a grid, one byte per voxel = round(255 rho),
    /// x fastest. Voxel (ix, iy, iz) is centred at bbox_min + (i + 0.5) (bbox_max - bbox_min) / dims.
    /// </summary>
    public sealed class OccupancyGrid
    {
        public readonly byte[] Voxels;
        public readonly int Nx, Ny, Nz;
        public readonly double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public OccupancyGrid(byte[] occR8, CaseTable table)
            : this(occR8, (int)table.OccDims[0], (int)table.OccDims[1], (int)table.OccDims[2], table.OccBboxMinMm, table.OccBboxMaxMm) { }

        public OccupancyGrid(byte[] occR8, int nx, int ny, int nz, double[] bboxMinMm, double[] bboxMaxMm)
        {
            if (occR8 == null || occR8.Length != (long)nx * ny * nz) throw new ContractException("occ.r8: size does not match occ.dims");
            Voxels = occR8; Nx = nx; Ny = ny; Nz = nz;
            MinX = bboxMinMm[0]; MinY = bboxMinMm[1]; MinZ = bboxMinMm[2];
            MaxX = bboxMaxMm[0]; MaxY = bboxMaxMm[1]; MaxZ = bboxMaxMm[2];
        }

        /// <summary>
        /// rho at a point in patient millimetres: trilinear between voxel centres, clamped to the outermost
        /// centres inside the box (a GPU's clamp sampling of the same Texture3D), and 0 outside the box.
        /// </summary>
        public double Sample(double xMm, double yMm, double zMm)
        {
            if (xMm < MinX || xMm > MaxX || yMm < MinY || yMm > MaxY || zMm < MinZ || zMm > MaxZ) return 0.0;
            double gx = (xMm - MinX) / (MaxX - MinX) * Nx - 0.5;
            double gy = (yMm - MinY) / (MaxY - MinY) * Ny - 0.5;
            double gz = (zMm - MinZ) / (MaxZ - MinZ) * Nz - 0.5;
            int x0, y0, z0; double tx, ty, tz;
            Cell(gx, Nx, out x0, out tx); Cell(gy, Ny, out y0, out ty); Cell(gz, Nz, out z0, out tz);
            int x1 = x0 + 1 < Nx ? x0 + 1 : x0, y1 = y0 + 1 < Ny ? y0 + 1 : y0, z1 = z0 + 1 < Nz ? z0 + 1 : z0;
            double c00 = At(x0, y0, z0) + tx * (At(x1, y0, z0) - At(x0, y0, z0));
            double c10 = At(x0, y1, z0) + tx * (At(x1, y1, z0) - At(x0, y1, z0));
            double c01 = At(x0, y0, z1) + tx * (At(x1, y0, z1) - At(x0, y0, z1));
            double c11 = At(x0, y1, z1) + tx * (At(x1, y1, z1) - At(x0, y1, z1));
            double c0 = c00 + ty * (c10 - c00), c1 = c01 + ty * (c11 - c01);
            return (c0 + tz * (c1 - c0)) / 255.0;
        }

        static void Cell(double g, int n, out int i0, out double t)
        {
            if (g <= 0.0) { i0 = 0; t = 0.0; return; }
            if (g >= n - 1) { i0 = n - 1; t = 0.0; return; }
            i0 = (int)Math.Floor(g);
            t = g - i0;
        }

        double At(int ix, int iy, int iz) { return Voxels[ix + Nx * (iy + Ny * iz)]; }

        /// <summary>The largest stored value as rho.</summary>
        public double MaxRho()
        {
            byte m = 0;
            for (int i = 0; i < Voxels.Length; i++) if (Voxels[i] > m) m = Voxels[i];
            return m / 255.0;
        }
    }

    /// <summary>Scores of the Core tasks M2, M3, M4 (section 10.2.6).</summary>
    public static class LungScoring
    {
        /// <summary>M2 needle dwell point: hit fraction H = rho(p) from occ.r8, best = max rho (table.json max_rho).</summary>
        public static M2Place M2(OccupancyGrid occ, CaseTable table, double xMm, double yMm, double zMm)
        {
            M2Place e;
            e.PxMm = xMm; e.PyMm = yMm; e.PzMm = zMm;
            e.Hit = occ.Sample(xMm, yMm, zMm);
            e.Best = table.MaxRho;
            return e;
        }

        /// <summary>The same for a point picked in the display frame (metres, M applied): p_mm = p * 1000 / M + origin_mm.</summary>
        public static M2Place M2FromDisplay(OccupancyGrid occ, CaseTable table, double m, double x, double y, double z)
        {
            return M2(occ, table, x * 1000.0 / m + table.OriginMm[0], y * 1000.0 / m + table.OriginMm[1], z * 1000.0 / m + table.OriginMm[2]);
        }

        /// <summary>
        /// M4 gating window. The payload carries the chosen arc's gated volume (<c>itv_cm3</c>), its duty and
        /// the volume of the best arc of equal duty (<c>best_cm3</c>). The score is the arc's ITV reduction
        /// divided by the best arc's: (ITV - V(arc)) / (ITV - V(best)), 1 for the best arc, and 1 when no arc of
        /// that duty reduces the ITV at all.
        /// </summary>
        public static M4Gate M4(CaseTable table, int start, int length, out double score)
        {
            M4Gate e;
            e.Start = start; e.Len = length;
            e.ItvCm3 = Gating.VolumeCm3(table, start, length);
            e.Duty = Gating.Duty(length);
            int bestStart;
            Gating.BestArc(table, length, out bestStart, out e.BestCm3);
            double best = table.ItvCm3 - e.BestCm3;
            double got = table.ItvCm3 - e.ItvCm3;
            score = best <= 0.0 ? 1.0 : got <= 0.0 ? 0.0 : got >= best ? 1.0 : got / best;
            return e;
        }

        public const string Before = "before";
        public const string After = "after";

        /// <summary>M3 spot the artefact: the pick among three shapes is correct or not, before or after beat f.</summary>
        public static M3Pick M3(int choice, int trueShape, bool afterBeatF)
        {
            if (choice < 0 || choice > 2) throw new ArgumentOutOfRangeException("choice", "choice must be 0, 1 or 2");
            M3Pick e;
            e.Choice = choice;
            e.Correct = choice == trueShape;
            e.When = afterBeatF ? After : Before;
            return e;
        }
    }
}
