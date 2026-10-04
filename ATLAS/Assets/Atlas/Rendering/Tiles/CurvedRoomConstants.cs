namespace Atlas.Rendering
{
    // P1-B11, Master Plan §3.5, §4.6: constants of the room placed in curved space.
    public static class CurvedRoomConstants
    {
        public const float KerbHeightM = TileChart.KerbHeight;   // walls sink to 0.9 m kerbs when curvature is on (MP §3.5, §4.6)
        public const double NearFadeRadiusM = 0.3;                // MP §3.5, §4.6: solid geometry within 0.3 m of the head fades (shader: lane A, D13)
        public const float EdgeRuleM = 0.25f;                    // MP §8.4 "longest edge <= 0.25 m" for room content

        /// <summary>The near-head fade is engaged when the horizontal surface distance to the head is below 0.3 m.</summary>
        public static bool NearFadeEngaged(double surfaceDistanceM) => surfaceDistanceM < NearFadeRadiusM;
    }
}
