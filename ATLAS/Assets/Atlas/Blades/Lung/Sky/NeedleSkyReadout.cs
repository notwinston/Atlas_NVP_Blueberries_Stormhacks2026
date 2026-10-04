namespace Atlas.Blades.Lung.Sky
{
    /// <summary>The numbers on the panel (MP §10.2.2 beat g "Numbers on screen").</summary>
    public readonly struct NeedleSkyReadout
    {
        public readonly float ClearanceShownMm;    // at the cursor, min over 10 phases, rounded to 0.1 mm
        public readonly bool CursorRobust;
        public readonly float RobustSharePercent;  // rounded to 0.1 %
        public readonly int Picks;                 // 0..3
        public readonly float AngleSumShownDeg;    // NaN until 3 picks (B11), rounded to 0.1 deg

        public NeedleSkyReadout(float clearanceShownMm, bool cursorRobust, float robustSharePercent, int picks, float angleSumShownDeg)
        { ClearanceShownMm = clearanceShownMm; CursorRobust = cursorRobust; RobustSharePercent = robustSharePercent; Picks = picks; AngleSumShownDeg = angleSumShownDeg; }
    }
}
