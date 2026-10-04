namespace Atlas.World
{
    /// <summary>
    /// One hand in a depth image: a box round the hand and a strip along the forearm, in depth-image pixels
    /// (column u, row v, row 0 at the top), and the depths beyond which nothing is masked: one for the box, one
    /// for the strip outside it.
    /// </summary>
    public struct DepthMaskRegion
    {
        /// <summary>The hand: u in [MinU, MaxU], v in [MinV, MaxV].</summary>
        public float MinU, MinV, MaxU, MaxV;
        /// <summary>The forearm: every pixel within ArmHalfWidth of the segment (ArmU0, ArmV0) to (ArmU1, ArmV1). A half width of 0 or less means no strip.</summary>
        public float ArmU0, ArmV0, ArmU1, ArmV1, ArmHalfWidth;
        /// <summary>The gate of the box, metres from the camera plane: a sample inside the box and deeper than this is never masked.</summary>
        public float MaxDepth;
        /// <summary>
        /// The gate of the strip where it lies outside the box, metres from the camera plane: a sample there and
        /// deeper than this is never masked. The forearm is no deeper than the palm, so this gate lies nearer than
        /// the box's. 0, a negative number or one that is not a number means the box's gate.
        /// </summary>
        public float ArmMaxDepth;

        /// <summary>
        /// True when the sample at pixel (u, v) with this depth belongs to the hand or its forearm: inside the box
        /// and no deeper than <see cref="MaxDepth"/>, or outside the box, inside the strip and no deeper than
        /// <see cref="ArmMaxDepth"/>.
        /// </summary>
        public bool Contains(float u, float v, float depth)
        {
            if (u >= MinU && u <= MaxU && v >= MinV && v <= MaxV) return depth <= MaxDepth;      // false for NaN
            if (!(ArmHalfWidth > 0f)) return false;
            if (!(depth <= (ArmMaxDepth > 0f ? ArmMaxDepth : MaxDepth))) return false;           // also false for NaN
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
    /// Where the walker's own hands are in the depth image Bump Guard is about to read
    /// (documents/ATLAS_Hands_Design.md section 7.1). Built by Atlas.Hands in step 1 for the depth image of
    /// that frame and handed to Bump Guard inside SensorFrame. Plain data: Atlas.Safety never learns where it
    /// came from. The default value is the empty mask, which changes nothing.
    /// </summary>
    public struct DepthMask
    {
        public const int MaxRegions = 2;

        /// <summary>0, 1 or 2 regions in use.</summary>
        public int Count;
        public DepthMaskRegion Region0, Region1;
        /// <summary>DepthFrame.Sequence of the image the mask was built for. It is ignored for any other image.</summary>
        public int DepthSequence;
        /// <summary>
        /// 1 while a hands session is running (the detector is active), whether or not a hand is in view; 0
        /// otherwise. In a hands session Bump Guard holds a new return nearer than its hands range as pending until
        /// it is seen again, so that a hand entering the view is masked before it can raise the cue (decision H17).
        /// The flag belongs to the frame, not to the regions: Bump Guard reads it also when the regions were built
        /// for another image and are ignored.
        /// </summary>
        public int Session;

        public bool IsEmpty { get { return Count <= 0; } }

        /// <summary>True when the sample belongs to one of the hands.</summary>
        public bool Contains(float u, float v, float depth)
        {
            if (Count <= 0) return false;
            if (Region0.Contains(u, v, depth)) return true;
            return Count > 1 && Region1.Contains(u, v, depth);
        }
    }

    /// <summary>Who can say where the hands are in a depth image. Implemented by Atlas.Hands; asked by the App's guard step.</summary>
    public interface IDepthMaskSource
    {
        /// <summary>
        /// The mask for the depth image with this sequence number. True while a hands session is running, with
        /// regions or with none: the mask then says so (<see cref="DepthMask.Session"/>). False (and the default
        /// mask) when there is no session.
        /// </summary>
        bool TryGetMask(int depthSequence, out DepthMask mask);
    }
}
