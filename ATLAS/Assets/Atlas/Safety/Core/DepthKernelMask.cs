namespace Atlas.Safety
{
    /// <summary>
    /// One hand in a depth image, as the depth kernel reads it: a box round the hand and a strip along the
    /// forearm, in the image's pixel coordinates (u to the right, v downwards from the top; pixel (u, v) has its
    /// centre at (u + 0.5, v + 0.5), as in <see cref="DepthIntrinsics"/>), and the depths beyond which nothing is
    /// masked: one for the box, one for the strip outside it. Plain numbers, so that it travels inside
    /// <see cref="DepthKernelInput"/> to the Burst job. The methods are read-only, so the kernel can call them
    /// on its input without copying it.
    /// </summary>
    public struct DepthKernelMaskRegion
    {
        /// <summary>The hand: u in [MinU, MaxU], v in [MinV, MaxV].</summary>
        public float MinU, MinV, MaxU, MaxV;
        /// <summary>The forearm: everything within ArmHalfWidth of the segment (ArmU0, ArmV0) to (ArmU1, ArmV1). A half width of 0 or less means no strip.</summary>
        public float ArmU0, ArmV0, ArmU1, ArmV1, ArmHalfWidth;
        /// <summary>The gate of the box, metres along the optical axis: a sample inside the box and deeper than this is never masked. Read as at most <see cref="BumpConstants.MaskMaxDepth"/>.</summary>
        public float MaxDepth;
        /// <summary>
        /// The gate of the strip where it lies outside the box, metres along the optical axis: the forearm is
        /// no deeper than the palm, so its gate lies nearer than the hand's (decision R14). 0, a negative number
        /// or one that is not a number means the box's gate, as in a mask built before the strip had its own.
        /// Read as at most <see cref="BumpConstants.MaskMaxDepth"/>.
        /// </summary>
        public float ArmMaxDepth;

        /// <summary>The deepest a sample can be and still be covered by this region, before the cap: the larger of the gates in use.</summary>
        public readonly float Gate
        {
            get
            {
                float gate = MaxDepth;
                if (ArmHalfWidth > 0f && ArmMaxDepth > 0f && !(ArmMaxDepth <= gate)) gate = ArmMaxDepth;
                return gate;
            }
        }

        /// <summary>
        /// True when the sample at image position (u, v) with this depth is one the region takes out: inside
        /// the box and no deeper than <see cref="MaxDepth"/>, or outside the box, inside the strip and no deeper
        /// than <see cref="ArmMaxDepth"/>. The cap of <see cref="BumpConstants.MaskMaxDepth"/> comes first:
        /// whatever the region's numbers are, nothing deeper is ever covered, and a gate that is not a number
        /// covers nothing.
        /// </summary>
        public readonly bool Covers(float u, float v, float depth)
        {
            if (!(depth <= BumpConstants.MaskMaxDepth)) return false;
            if (u >= MinU && u <= MaxU && v >= MinV && v <= MaxV) return depth <= MaxDepth;
            if (!(ArmHalfWidth > 0f)) return false;
            if (!(depth <= (ArmMaxDepth > 0f ? ArmMaxDepth : MaxDepth))) return false;
            float du = ArmU1 - ArmU0, dv = ArmV1 - ArmV0;
            float lengthSq = du * du + dv * dv;
            float t = lengthSq > 1e-6f ? ((u - ArmU0) * du + (v - ArmV0) * dv) / lengthSq : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            float ru = u - (ArmU0 + t * du), rv = v - (ArmV0 + t * dv);
            return ru * ru + rv * rv <= ArmHalfWidth * ArmHalfWidth;
        }
    }

    /// <summary>
    /// Where the walker's own hands are in one depth image (documents/ATLAS_Hands_Design.md section 7.1), as the
    /// depth kernel reads it. The default value is the empty mask: with it the kernel does exactly what it did
    /// before it knew of any mask. The kernel does not trust what it is given: it reads at most
    /// <see cref="BumpConstants.MaskMaxRegions"/> regions, never masks beyond <see cref="BumpConstants.MaskMaxDepth"/>,
    /// and ignores the whole mask for an image of which it would take more than <see cref="BumpConstants.MaskMaxShare"/>.
    /// <para>It also says whether the image belongs to a hands session (<see cref="Session"/>), with or without
    /// a region: in a session a near return must be seen twice before it counts (decision R13).</para>
    /// </summary>
    public struct DepthKernelMask
    {
        /// <summary>Regions in use: 0, 1 or 2. More than 2 is read as 2.</summary>
        public int Count;
        public DepthKernelMaskRegion Region0, Region1;
        /// <summary>
        /// Not 0 while a hands session is running, whether or not a hand is in view (a mask with no region still
        /// says so). In a session a dynamic cell first stamped by a return nearer than
        /// <see cref="BumpConstants.PendingNearMetres"/> is pending until it is seen again
        /// (<see cref="DynamicLayer"/>). 0, the default: every return counts at once, as it always did.
        /// </summary>
        public int Session;

        /// <summary>True when no region is in use.</summary>
        public readonly bool IsEmpty => Count <= 0;

        /// <summary>The deepest a sample can be and still be covered, metres: the largest gate, cut at the cap. 0 for the empty mask.</summary>
        public readonly float Gate
        {
            get
            {
                if (Count <= 0) return 0f;
                float gate = Region0.Gate;
                if (Count > 1)
                {
                    float other = Region1.Gate;
                    if (!(other <= gate)) gate = other;
                }
                return gate <= BumpConstants.MaskMaxDepth ? gate : BumpConstants.MaskMaxDepth;
            }
        }

        /// <summary>True when the sample at image position (u, v) with this depth is one the mask takes out.</summary>
        public readonly bool Covers(float u, float v, float depth)
        {
            if (Count <= 0) return false;
            if (Region0.Covers(u, v, depth)) return true;
            return Count > 1 && Region1.Covers(u, v, depth);
        }
    }
}
