namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Every constant of the needle sky with its citation (MP = ATLAS_Master_Plan.md; D# = docs/laneB/needle_sky.md).</summary>
    public static class NeedleSkyConstants
    {
        public const int Width = 512, Height = 256, Phases = 10;   // MP §10.2.4 step 8
        public const float ThresholdMm = 5f;                       // MP §10.2.4 step 8; §10.2.1 "Teaching threshold"
        public const int NsideExpected = 64;                       // MP §10.2.4 step 8
        public const double DwellSeconds = 0.7;                    // MP §10.2.2 "dwell 700 ms" (own constant, GazeTarget not referenced)
        public const double DwellConeDeg = 1.5;                    // D5 (half-angle; Spec §11.3.3 3 deg disc is informative)
        public const double MinPickSeparationDeg = 3.0;            // D7 (from every pick and from its antipode)
        public const int PicksPerCheck = 3;                        // MP §10.2.2 "marks three directions"
        public const double StickRateDegPerS = 90.0;               // D6
        public const float StickDeadzone = 0.2f;                   // D6
        public const double ElClampDeg = 89.0;                     // D6
        public const double CompleteAfterCheckSeconds = 5.0;       // D9 (Spec §11.3.3 "(5 s)")
        public const float SphereRadiusM = 2.0f;                   // D11
        public const float PanelBelowDeg = 15f, PanelDistanceM = 1.0f, PanelFontSize = 0.5f;   // D12
        public const float ArcHalfWidthDeg = 0.25f, PickMarkerRadiusDeg = 0.75f,
                           CursorRingRadiusDeg = 1.5f, CursorRingHalfWidthDeg = 0.075f;        // D10
        /// <summary>Time comparison slack: float dt accumulated in double (0.01f x 70 = 0.69999998). D21.</summary>
        public const double TimeEps = 1e-6;
    }
}
