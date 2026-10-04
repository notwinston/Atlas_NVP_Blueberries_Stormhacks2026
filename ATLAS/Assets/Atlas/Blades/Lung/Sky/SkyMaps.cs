using System;
using System.IO;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>10 risk maps + robust map (MP §10.2.4 step 8, §10.2.5): CPU lookup for the read-outs.</summary>
    public sealed class SkyMaps
    {
        public const int Width = 512, Height = 256, Phases = 10;        // MP §10.2.4 step 8
        public const int MapBytes = Width * Height;                      // 131,072
        public const int TextureBytes = (Phases + 1) * MapBytes;         // 1,441,792 = "11 R8 textures (1.4 MB)", MP §10.2.5

        readonly byte[][] _risk;
        readonly byte[] _robust;

        public SkyMaps(SkyFile meta, byte[][] risk, byte[] robust)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            meta.Validate();
            if (risk == null || risk.Length != Phases) throw new InvalidDataException("risk maps: " + (risk == null ? 0 : risk.Length) + " files, expected 10");
            for (int k = 0; k < Phases; k++) CheckLength(risk[k], "risk_p" + k + ".r8");
            CheckLength(robust, "robust.r8");
            Meta = meta; _risk = risk; _robust = robust;
            RobustSharePercent = ComputeShare();
        }

        static void CheckLength(byte[] b, string name)
        {
            int n = b == null ? 0 : b.Length;
            if (n != MapBytes) throw new InvalidDataException(name + ": " + n + " bytes, expected " + MapBytes + " (Git LFS pointer? run git lfs pull)");
        }

        /// <summary>Reads &lt;caseRoot&gt;/sky/{sky.json, risk_p0..9.r8, robust.r8} (MP §10.2.4 step 8).</summary>
        public static SkyMaps Load(string caseRoot)
        {
            string dir = Path.Combine(caseRoot, "sky");
            var meta = SkyFile.Parse(File.ReadAllText(Path.Combine(dir, "sky.json")));
            var risk = new byte[Phases][];
            for (int k = 0; k < Phases; k++) risk[k] = File.ReadAllBytes(Path.Combine(dir, "risk_p" + k + ".r8"));
            return new SkyMaps(meta, risk, File.ReadAllBytes(Path.Combine(dir, "robust.r8")));
        }

        public SkyFile Meta { get; }
        public byte[] RiskBytes(int phase) => _risk[phase];
        public byte[] RobustBytes => _robust;

        public float ClearanceMm(int phase, int i, int j) => _risk[phase][j * Width + i] * Meta.mm_per_unit;

        public float MinClearanceMm(int i, int j)
        {
            int idx = j * Width + i, m = 255;
            for (int k = 0; k < Phases; k++) { int b = _risk[k][idx]; if (b < m) m = b; }
            return m * Meta.mm_per_unit;
        }

        public bool IsRobust(int i, int j) => _robust[j * Width + i] != 0;   // D16

        /// <summary>Equal-area row weights (D17), computed once.</summary>
        public double RobustSharePercent { get; }

        double ComputeShare()
        {
            double sum = 0;
            for (int j = 0; j < Height; j++)
            {
                int n = 0, o = j * Width;
                for (int i = 0; i < Width; i++) if (_robust[o + i] != 0) n++;
                if (n == 0) continue;
                double top = (90.0 - 180.0 * j / Height) * Math.PI / 180.0, bot = (90.0 - 180.0 * (j + 1) / Height) * Math.PI / 180.0;
                sum += (Math.Sin(top) - Math.Sin(bot)) / 2.0 * n / Width;
            }
            return 100.0 * sum;
        }
    }
}
