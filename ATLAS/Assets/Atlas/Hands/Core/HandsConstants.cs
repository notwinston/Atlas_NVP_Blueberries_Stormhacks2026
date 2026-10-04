namespace Atlas.Hands
{
    /// <summary>
    /// Every number the tracked hands use (documents/ATLAS_Hands_Design.md Appendix B). Constants in code, as
    /// Bump Guard's are: they are starting values, corrected here after the bench of section 9.3. The limits of
    /// the depth mask that Bump Guard itself enforces are in Atlas.Safety's BumpConstants.
    /// </summary>
    public static class HandsConstants
    {
        // ---- Detector (sections 3.5, 7.3) ----

        /// <summary>Detections per second at tier Nominal and High.</summary>
        public const int RateFull = 30;
        /// <summary>At tier Fair, and after the self-governor has stepped in.</summary>
        public const int RateReduced = 15;
        /// <summary>After no hand has been seen for <see cref="IdleAfterSeconds"/>.</summary>
        public const int RateIdle = 5;
        public const float IdleAfterSeconds = 20f;
        /// <summary>A result older than this when polled is not used, seconds.</summary>
        public const float MaxResultAgeSeconds = 0.2f;
        /// <summary>A gamepad must stay connected or absent this long before the gate follows it, seconds.</summary>
        public const float GamepadHoldSeconds = 1f;
        /// <summary>Self-governor: results older than this for <see cref="GovernorSeconds"/> drop the rate, seconds.</summary>
        public const float GovernorAgeSeconds = 0.060f;
        /// <summary>Self-governor: the hands' own main-thread time over this for <see cref="GovernorSeconds"/> drops the rate, milliseconds.</summary>
        public const float GovernorMainThreadMs = 0.6f;
        public const float GovernorSeconds = 2f;
        /// <summary>After a fault the source is started again after this long, seconds.</summary>
        public const float RestartAfterFaultSeconds = 5f;

        // ---- Lift (section 4.3) ----

        /// <summary>A joint below this confidence is not used.</summary>
        public const float JointConfidenceFloor = 0.3f;
        /// <summary>Fewer usable joints than this: no hand.</summary>
        public const int MinUsableJoints = 12;
        /// <summary>Palm plane: fewest depth samples left after outlier rejection.</summary>
        public const int PalmMinSamples = 12;
        /// <summary>Palm plane: a sample farther than this from the first fit is dropped, metres.</summary>
        public const float PalmOutlierMetres = 0.025f;
        /// <summary>LiDAR is trusted on a hand between these distances, metres.</summary>
        public const float DepthMinMetres = 0.32f, DepthMaxMetres = 0.90f;
        /// <summary>LiDAR at a finger joint counts when the spread of its 3 x 3 pixels is under this, metres.</summary>
        public const float JointDepthMaxSpread = 0.03f;
        /// <summary>The two answers of a bone must differ by more than this for LiDAR to choose between them, metres.</summary>
        public const float JointDepthDecides = 0.02f;
        /// <summary>A finger may bend backwards this far before the lift penalises it, degrees.</summary>
        public const float BackwardBendDegrees = 20f;
        /// <summary>Confidence is capped here when the palm's distance came from the hand's size, not from LiDAR.</summary>
        public const float ConfidenceWithoutDepth = 0.5f;
        /// <summary>A hand whose palm is farther from the camera than this is someone else's, metres.</summary>
        public const float MaxHandDistance = 0.9f;
        /// <summary>The walker's hand size relative to the template stays inside this range.</summary>
        public const float ScaleMin = 0.80f, ScaleMax = 1.25f;
        /// <summary>Good size measurements needed before the scale leaves 1.</summary>
        public const int ScaleSamples = 30;

        // ---- Filter, prediction, loss (section 4.4) ----

        /// <summary>One-Euro filter on the palm: minimum cut-off (Hz) and speed coefficient (Hz per m/s).</summary>
        public const float PalmMinCutoff = 1.5f, PalmBeta = 12f;
        /// <summary>One-Euro filter on finger joints, in the palm's frame.</summary>
        public const float FingerMinCutoff = 2.5f, FingerBeta = 20f;
        /// <summary>Cut-off of the One-Euro filter's derivative, Hz.</summary>
        public const float DerivativeCutoff = 1f;
        /// <summary>The palm is moved along its velocity by the result's age, at most this, seconds.</summary>
        public const float MaxPredictSeconds = 0.050f;
        /// <summary>A detection is matched to an existing hand whose predicted palm is within this, metres.</summary>
        public const float AssociateMetres = 0.20f;
        /// <summary>A lost hand's coasting velocity decays with this time constant, seconds.</summary>
        public const float CoastDecaySeconds = 0.1f;
        /// <summary>A lost hand starts to fade after this long, seconds.</summary>
        public const float LossFadeStartSeconds = 0.10f;
        /// <summary>A lost hand stops being tracked after this long, seconds (the grace period).</summary>
        public const float LossGraceSeconds = 0.25f;
        /// <summary>A hand that returns within the grace period and this distance carries on; otherwise its filter restarts, metres.</summary>
        public const float ReacquireMetres = 0.15f;
        /// <summary>Left or right is the majority of this many labels.</summary>
        public const int SideVotes = 15;

        // ---- Visibility (section 5.4) ----

        /// <summary>Detections in a row, each at least this confident, before a hand is shown.</summary>
        public const int EnterDetections = 2;
        public const float EnterConfidence = 0.5f;
        /// <summary>Below this confidence a hand fades out.</summary>
        public const float ExitConfidence = 0.3f;
        /// <summary>Opacity ramps, seconds: appearing, disappearing, hiding for a Bump Guard warning, returning after it, the gate going inactive.</summary>
        public const float FadeInSeconds = 0.12f, FadeOutSeconds = 0.15f, WarningOutSeconds = 0.10f,
                           WarningBackSeconds = 0.30f, GateOutSeconds = 0.20f;
        /// <summary>The palm's centre must be this far inside the picture's border, as a share of its height, to be fully visible.</summary>
        public const float EdgeFadeBand = 0.08f;
        /// <summary>The part of a hand nearer to an eye than this dissolves, metres.</summary>
        public const float NearFadeMetres = 0.12f;

        // ---- Touch (section 6.2; the state machine is in Atlas.Input and repeats the numbers it uses) ----

        /// <summary>Touch and the depth mask need at least this confidence.</summary>
        public const float InteractConfidence = 0.6f;
        /// <summary>The press point sits this far back from the index tip joint, metres.</summary>
        public const float FingerPadMetres = 0.004f;

        // ---- Pinch and two hands (sections 6.3, 6.4) ----

        /// <summary>Thumb-to-index gap over palm width: a pinch closes under the first and opens over the second.</summary>
        public const float PinchClose = 0.25f, PinchOpen = 0.40f;
        /// <summary>Detections in a row under <see cref="PinchClose"/> before a pinch closes.</summary>
        public const int PinchDetections = 3;
        /// <summary>No pinch this soon after a hand appears, seconds.</summary>
        public const float PinchQuietAfterEnterSeconds = 0.3f;
        /// <summary>No pinch from a hand moving faster than this, metres per second.</summary>
        public const float PinchMaxHandSpeed = 1f;
        /// <summary>No second pinch from the same hand sooner than this, seconds.</summary>
        public const float PinchRefractorySeconds = 0.4f;

        // ---- Reach and near panels (section 6.1) ----

        /// <summary>The near anchor sits this far from the head, metres (the default of hands.reach).</summary>
        public const float ReachMetres = 0.45f;
        public const float ReachMinMetres = 0.30f, ReachMaxMetres = 0.60f;
        /// <summary>The near anchor sits this far below the horizontal through the head, degrees.</summary>
        public const float ReachPitchDegrees = 25f;
        /// <summary>A panel's own layout is multiplied by this in near mode.</summary>
        public const float ReachPanelScale = 0.75f;
        /// <summary>The anchor follows when the head has moved this far from where the anchor was set, metres.</summary>
        public const float ReachFollowMetres = 0.25f;
        /// <summary>The anchor follows when the heading differs by this much for <see cref="ReachFollowSeconds"/>, degrees.</summary>
        public const float ReachFollowDegrees = 40f;
        public const float ReachFollowSeconds = 0.5f;
        /// <summary>The anchor eases to its new place over this long, seconds.</summary>
        public const float ReachEaseSeconds = 0.3f;
        /// <summary>The anchor never moves while a fingertip is this close to it, metres.</summary>
        public const float ReachFreezeMetres = 0.15f;

        // ---- World touch (section 6.5) ----

        /// <summary>A fingertip this close to a surface shows the glow, metres.</summary>
        public const float GlowMetres = 0.08f;
        /// <summary>A fingertip this close to a surface is touching it, metres.</summary>
        public const float ContactMetres = 0.015f;
        /// <summary>The contact ring fades over this long after the finger leaves, seconds.</summary>
        public const float MarkFadeSeconds = 0.4f;
        /// <summary>Diameter of the glow at 8 cm and at contact, and of the contact ring, metres.</summary>
        public const float GlowFarMetres = 0.01f, GlowNearMetres = 0.03f, RingMetres = 0.03f;
        /// <summary>A mark lies this far proud of its surface, metres.</summary>
        public const float MarkLiftMetres = 0.002f;

        // ---- Depth mask, as built here (section 7.1) ----

        /// <summary>The hand's box is grown by this share of its longer side.</summary>
        public const float MaskGrow = 0.35f;
        /// <summary>The forearm strip is this many palm widths wide.</summary>
        public const float MaskArmPalmWidths = 1.2f;
        /// <summary>Growth for motion: the hand's speed in the picture times the result's age, at most this, depth pixels.</summary>
        public const float MaskMotionMaxPixels = 20f;
        /// <summary>A sample is masked only up to the palm's depth plus this, metres.</summary>
        public const float MaskBehindPalmMetres = 0.20f;
        /// <summary>And never beyond this, metres.</summary>
        public const float MaskMaxDepthMetres = 0.80f;
        /// <summary>A mask covering more than this share of the image is dropped.</summary>
        public const float MaskMaxImageShare = 0.35f;
        /// <summary>The mask outlives the grace period of a loss by this long, seconds.</summary>
        public const float MaskLingerSeconds = 0.15f;

        // ---- Events (section 8.1) ----

        /// <summary>At most one enter and one exit event per hand in this long, seconds.</summary>
        public const float EnterExitEventSeconds = 1f;

        // ---- Mesh (section 5.1) ----

        /// <summary>Sides of a finger's tube.</summary>
        public const int FingerSides = 8;
        /// <summary>Capacity of one hand's dynamic mesh.</summary>
        public const int MaxVertices = 640, MaxIndices = 3 * 900;
    }
}
