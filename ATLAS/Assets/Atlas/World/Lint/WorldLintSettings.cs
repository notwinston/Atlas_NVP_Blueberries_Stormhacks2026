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
        // The room is Home's row: in Curved Walk it is drawn with the full furniture copies (mesh design section 5.4),
        // so S2's row is the tiles' 150 draws and 120,000 triangles on top of it.
        public int TileDrawCap = 150, DrawCap = 240, MustDrawCap = 300;
        public long TriCap = 370000, MustTriCap = 500000;
        public int RoomDraws = 80;
        public long RoomTris = 250000;
        public int MaxDrawnTilesPerEye = 71;
        public double FaceTolM = 0.001;
        public double FaceBandM = 0.10;
        public double MaxPropEdgeM = 0.2501; // §8.4 "no edge longer than 0.25 m", with float rounding
        public double FallbackL = 0; // used only when the footprint is null (D13)
    }
}
