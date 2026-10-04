namespace Atlas.World.Lint
{
    // Defaults are the plan's numbers: Master Plan §3.4 (rho 2.5), §8.4/§8.5 (draw rule, budgets, S0/S2 rows), §11.1.
    public sealed class WorldLintSettings
    {
        public double Rho = 3.0;
        public int LintDepth = 6;
        public int ReachDepth = 8;
        public double EyeHeight = 1.6;
        public int Headings = 8;
        public double[] PitchesDeg = { 20, 0, -30, -60, -90 };
        public double HalfFovHDeg = 50, HalfFovVDeg = 48, MarginHDeg = 12, MarginVDeg = 4;
        public double TowerRadius = 2.5;
        public double SpanReach = 2.0;
        public int TileDrawCap = 150, DrawCap = 210, MustDrawCap = 300;
        public long TriCap = 250000, MustTriCap = 500000;
        public int RoomDraws = 60;
        public long RoomTris = 130000;
        public int MaxDrawnTilesPerEye = 71;
        public double FaceTolM = 0.001;
        public double FaceBandM = 0.10;
        public double FallbackL = 0; // used only when the footprint is null (D13)
    }
}
