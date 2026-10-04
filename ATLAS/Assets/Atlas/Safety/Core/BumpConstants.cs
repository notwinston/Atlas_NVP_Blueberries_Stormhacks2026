namespace Atlas.Safety
{
    /// <summary>
    /// Every number Bump Guard uses. These are constants in code by decision: Bump Guard has no entry in the
    /// parameter registry and no off switch (Master Plan section 4.10, "Constants, not parameters").
    /// They are starting values, confirmed or changed here by the BumpGuard_Replay test (section 14).
    /// </summary>
    public static class BumpConstants
    {
        // ---- Level logic (section 4.10, logic block) ----

        /// <summary>WARN when the clearance at the head is under this, metres (4.10: "now &lt; 0.30 m").</summary>
        public const float WarnNow = 0.30f;
        /// <summary>STRONG when the clearance at the head is under this, metres (4.10: "now &lt; 0.20 m").</summary>
        public const float StrongNow = 0.20f;
        /// <summary>A path is blocked when its smallest clearance is under this, metres (4.10: "path(T) &lt; 0.05 m").</summary>
        public const float PathClear = 0.05f;
        /// <summary>Look-ahead of the WARN path test, seconds (4.10: "path(0.8 s)").</summary>
        public const float WarnHorizon = 0.8f;
        /// <summary>Look-ahead of the STRONG path test, seconds (4.10: "path(0.4 s)").</summary>
        public const float StrongHorizon = 0.4f;
        /// <summary>Margin added to every threshold before a level may fall, metres (4.10: "relax only after 0.05 m").</summary>
        public const float RelaxDistance = 0.05f;
        /// <summary>Time the margin must hold before a level falls, seconds (4.10: "and 0.3 s of margin").</summary>
        public const float RelaxSeconds = 0.3f;
        /// <summary>Spacing of the samples along a predicted path, metres (one cell; decided in the lane C contract).</summary>
        public const float PathStep = 0.05f;
        /// <summary>Speed is clamped to this for the path tests, metres per second (decided in the lane C contract).</summary>
        public const float MaxPredictSpeed = 2.0f;

        // ---- Head velocity and pose (4.10, inputs table and "Raw pose") ----

        /// <summary>Window of the head-velocity low-pass, seconds (4.10: "low-passed over 0.2 s").</summary>
        public const float VelocityWindow = 0.2f;
        /// <summary>A larger floor displacement in one frame is a pose jump, metres (4.10: "more than 0.2 m in one frame").</summary>
        public const float JumpDistance = 0.20f;

        // ---- Tracking (4.10, "Tracking") ----

        /// <summary>Tracking not Normal for this long raises TrackingLost, seconds (4.10: "raised at 0.5 s").</summary>
        public const float TrackingLostSeconds = 0.5f;

        // ---- Static map (sections 4.2 and 4.3) ----

        /// <summary>Cell size of the distance field and of the dynamic layer, metres (4.3: "5 cm grid").</summary>
        public const float CellSize = 0.05f;
        /// <summary>The distance field reads this much less than the true distance to a wall, metres (4.10, worked numbers; 4.2).</summary>
        public const float StaticInset = 0.05f;
        /// <summary>Clearance reported for a point outside the grid: blocked.</summary>
        public const float BlockedClearance = 0f;

        // ---- Dynamic layer (4.10, logic block) ----

        /// <summary>Lowest depth return that counts as an obstacle, metres above the floor (4.10: "between 0.10 m and 2.0 m").</summary>
        public const float DynMinHeight = 0.10f;
        /// <summary>Highest depth return that counts as an obstacle, metres above the floor (4.10: "between 0.10 m and 2.0 m").</summary>
        public const float DynMaxHeight = 2.0f;
        /// <summary>A return marks the dynamic layer only when it is more than this nearer than the static map predicts, metres (4.10: "more than 0.3 m nearer").</summary>
        public const float DynStaticMargin = 0.30f;
        /// <summary>A dynamic cell with no new hit for this long is cleared, seconds (4.10: "after 30 s without a new hit").</summary>
        public const double DynExpirySeconds = 30.0;
        /// <summary>Processed depth images that must see a dynamic cell empty, with no hit between, before it is cleared (decided in the lane C contract).</summary>
        public const int ClearConfirmFrames = 3;
        /// <summary>
        /// A ray counts as passing through a cell only when it is this far below the cell's stored height, metres.
        /// Keeps a ray that grazes the top of an obstacle from erasing it. One cell; chosen here.
        /// </summary>
        public const float ClearHeightTolerance = 0.05f;
        /// <summary>A ray sees cells empty only up to this far short of where it lands, metres ("lands farther away", 4.10). Two cells.</summary>
        public const float ClearRayMargin = 0.10f;
        /// <summary>
        /// Farthest dynamic cell the clearance query looks for, metres. Every threshold including the relax
        /// margin is at most 0.35 m, so a cell farther than this cannot change the level. Equal to one block.
        /// </summary>
        public const float DynamicQueryRange = 0.40f;
        /// <summary>log2 of the side, in cells, of the occupancy blocks that make the nearest-cell query cheap.</summary>
        public const int BlockShift = 3;
        /// <summary>Occupancy blocks checked for expired cells per step (housekeeping; the query itself ignores expired cells).</summary>
        public const int SweepBlocksPerStep = 8;

        // ---- Live depth (4.10, inputs table, "Live depth" and "Depth sectors") ----

        /// <summary>Nominal width of the LiDAR depth image, pixels (4.10: "256 x 192").</summary>
        public const int DepthWidth = 256;
        /// <summary>Nominal height of the LiDAR depth image, pixels (4.10: "256 x 192").</summary>
        public const int DepthHeight = 192;
        /// <summary>The depth rate: images processed per second. Every one the phone delivers (4.10: "60 Hz").</summary>
        public const int DepthProcessHz = 60;
        /// <summary>The depth rate may be lowered in a rebuild, never below this, Hz (4.10: "never below 15 Hz").</summary>
        public const int DepthProcessHzMin = 15;
        /// <summary>Every fourth pixel in both directions is used: 64 x 48 samples (4.10: "the subsampled image").</summary>
        public const int DepthStride = 4;
        /// <summary>Nearest valid LiDAR reading, metres; nearer values are "no return" (section 4.2: "about 0.3 to 5 m").</summary>
        public const float DepthMin = 0.3f;
        /// <summary>Farthest valid LiDAR reading, metres (section 4.2: "about 0.3 to 5 m").</summary>
        public const float DepthMax = 5.0f;
        /// <summary>Number of depth sectors (4.10: "seven sectors").</summary>
        public const int SectorCount = 7;
        /// <summary>The sectors cover this many degrees to each side of the view direction (4.10: "+/-35 degrees").</summary>
        public const float SectorHalfSpanDeg = 35f;
        /// <summary><see cref="SectorHalfSpanDeg"/> in radians.</summary>
        public const float SectorHalfSpan = SectorHalfSpanDeg * 3.14159265f / 180f;
        /// <summary>Sector ranges older than this are not used by the head-frame fallback, seconds (four images at 15 Hz).</summary>
        public const double DepthResultMaxAgeSeconds = 0.25;

        // ---- Depth mask: the walker's own hands (documents/ATLAS_Hands_Design.md section 7.1 and its review) ----
        // Bump Guard holds a mask to every one of these itself, whatever built the mask.

        /// <summary>Regions of a depth mask that are read: one for each hand. A mask that claims more is cut to this.</summary>
        public const int MaskMaxRegions = 2;
        /// <summary>No sample deeper than this is ever masked, metres, whatever gate a region asks for (7.1: "never beyond 0.80 m").</summary>
        public const float MaskMaxDepth = 0.80f;
        /// <summary>
        /// A mask that would take out more than this share of an image's valid samples (those with a return) is
        /// ignored for that image. Counted over the samples it would really take, inside a region and inside its
        /// gate, not over the regions' area. Measured in BumpGuard_Mask_Limits: two open hands with their
        /// forearms take 12 to 18 % at arm's length (0.45 to 0.60 m) and 31 % at 0.32 m, the nearest LiDAR sees
        /// them, where every sample has a return, and up to 31 % at arm's length where a third of the picture is
        /// beyond LiDAR's range; the same hands with a wall inside their gate, which would hide most of the view,
        /// take 44 to 76 %.
        /// </summary>
        public const float MaskMaxShare = 0.35f;
        /// <summary>
        /// A dynamic cell under a masked sample is forgotten only if it was first stamped less than this long ago,
        /// seconds: long enough for the cells a hand leaves before it is detected, too short for an obstacle that
        /// has been in view. Counted from the first stamp, never from the last hit, which every image renews.
        /// </summary>
        public const double MaskYoungSeconds = 1.0;
        /// <summary>
        /// ... and only if its stored height is within this of the masked sample's own height, metres. A cell
        /// whose top is lower or higher than that belongs to something else standing in the same 5 cm of floor.
        /// </summary>
        public const float MaskHeightTolerance = 0.15f;

        // ---- Rescan check (4.10, "Static map") ----

        /// <summary>Median disagreement of live depth and static map that advises a rescan, metres (4.10: "exceeds 0.15 m").</summary>
        public const float RescanDisagreement = 0.15f;
        /// <summary>RescanAdvised falls when the median stays below this for <see cref="RescanSeconds"/>, metres (decided in the lane C contract).</summary>
        public const float RescanClearDisagreement = 0.10f;
        /// <summary>How long the disagreement must last, seconds (4.10: "for 2 s").</summary>
        public const float RescanSeconds = 2.0f;
        /// <summary>Fewer wall samples in view: the disagreement test is skipped (decided in the lane C contract).</summary>
        public const int RescanMinSamples = 50;
        /// <summary>A return is compared with the map only when it lies within this of the edge of the free floor, metres (lane C contract, 8.7).</summary>
        public const float RescanNearEdge = 0.5f;
        /// <summary>
        /// Returns at or above this height are taken as "walls in view" for the rescan check, metres. Above table
        /// and chair height, so the tops of scanned furniture are left out. Chosen here.
        /// </summary>
        public const float WallBandMinHeight = 1.0f;
        /// <summary>A rescan timer restarts when no wall has been compared for this long, seconds.</summary>
        public const double WallCheckMaxGapSeconds = 0.5;

        // ---- Cue (4.10, outputs table; used by BumpCue) ----

        /// <summary>Haptic pulses per second at WARN (4.10: "two pulses per second").</summary>
        public const int WarnPulseHz = 2;
        /// <summary>Haptic pulses per second at STRONG (4.10: "six per second").</summary>
        public const int StrongPulseHz = 6;
        /// <summary>How long an operator cue lasts, seconds (decided in the lane C contract).</summary>
        public const float OperatorHoldSeconds = 3.0f;
    }
}
