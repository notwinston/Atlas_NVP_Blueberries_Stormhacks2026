using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// Builds the depth mask (documents/ATLAS_Hands_Design.md section 7.1): where the walker's own hands are in the
    /// depth image Bump Guard is about to read, so that a hand held in front of the head is not taken for an
    /// obstacle in its path. Engine-free, allocation-free, and fed what the filter hands over each frame.
    ///
    /// Each qualifying hand gives one region: the box round its 21 joints, projected into the depth image with that
    /// image's own pose and intrinsics, and a strip along the forearm from the wrist to the border of the image,
    /// both grown for the hand's motion, each with a depth gate behind the palm: 0.20 m for the box, and for the
    /// strip outside the box its own, 0.05 m (decision R14). The mask is only a claim about where a hand is: Bump
    /// Guard enforces every limit of it again on its own side (decision R4), whatever is built here.
    ///
    /// Pixel coordinates in the mask are sample indices, as in <see cref="DepthFrame.Unproject"/>: the centre of the
    /// sample in column u and row v is at (u, v), so projecting is that method's inverse.
    /// </summary>
    public sealed class HandsMaskBuilder
    {
        /// <summary>The palm nearer to the camera than this cannot be seen in the depth image, metres: no region.</summary>
        internal const float MinPalmDepthMetres = 0.05f;

        /// <summary>A joint nearer to the camera's plane than this is behind the lens for the purpose of projecting it, metres: no region.</summary>
        internal const float MinJointDepthMetres = 0.01f;

        /// <summary>
        /// The size limit counts samples of the grid Bump Guard reads: every <see cref="ShareStride"/>th pixel in each
        /// direction, from pixel ShareStride / 2 (BumpConstants.DepthStride and BumpDepthKernel.FirstSample). The
        /// builder may not reference Atlas.Safety, so the number is repeated here.
        /// </summary>
        internal const int ShareStride = 4;

        /// <summary>The wrist and the middle knuckle closer than this in the picture, pixels, have no direction: the hand points at the camera and its forearm is behind it.</summary>
        internal const float MinArmDirectionPixels = 0.5f;

        /// <summary>
        /// The forearm strip's own gate lies this far behind the palm, metres (decision R14). The forearm runs from
        /// the wrist back towards the body and is no deeper than the palm, so a gate as deep as the hand's
        /// (<see cref="HandsConstants.MaskBehindPalmMetres"/>) would hide whatever stands behind the forearm along
        /// the whole strip. Never beyond <see cref="HandsConstants.MaskMaxDepthMetres"/>.
        /// </summary>
        internal const float ArmBehindPalmMetres = 0.05f;

        /// <summary>A hand's mask lives this long after the hand was last detected, seconds: the grace period of its loss and the linger.</summary>
        const float LifeSeconds = HandsConstants.LossGraceSeconds + HandsConstants.MaskLingerSeconds;

        /// <summary>What the builder remembers of one hand: the last pose it was handed while the hand was tracked.</summary>
        struct Held
        {
            public bool Has;
            /// <summary>World, as the filter handed them over.</summary>
            public JointSet Joints;
            /// <summary>World, metres per second.</summary>
            public float3 PalmVelocity;
            /// <summary>Confidence of the hand's last detection (not of the held pose, which the filter lets fall while the hand is lost).</summary>
            public float Confidence;
            /// <summary>App clock: when that detection was made.</summary>
            public double DetectedAt;
            /// <summary>App clock: when the hand was last handed over as tracked.</summary>
            public double SeenAt;
            /// <summary>The pose's age at <see cref="SeenAt"/>, seconds.</summary>
            public float AgeAtSeen;
        }

        Held _left, _right;
        float _lastShare;

        /// <summary>
        /// The share of the depth samples the last <see cref="Build"/> found masked, before the size limit decided
        /// (0 when nothing was counted). Kept for the tests and the debug read-out: the mask does not carry it.
        /// </summary>
        internal float LastMaskedShare { get { return _lastShare; } }

        /// <summary>Forgets both hands. Called when hands switch off or a session starts, so that no old pose lingers into the next.</summary>
        public void Reset()
        {
            _left = default(Held);
            _right = default(Held);
            _lastShare = 0f;
        }

        /// <summary>
        /// The mask for the depth image <paramref name="depth"/>: empty when there is nothing to mask. A hand
        /// qualifies when its last detection had at least <see cref="HandsConstants.InteractConfidence"/>, and keeps
        /// its mask until <see cref="HandsConstants.LossGraceSeconds"/> plus <see cref="HandsConstants.MaskLingerSeconds"/>
        /// after that detection: through the filter's grace period from the pose it holds, then, once the filter has
        /// stopped tracking it, from the last pose the builder was handed.
        ///
        /// From each <see cref="TrackedHand"/> it reads Tracked, Fresh (a detection arrived this frame), World,
        /// Confidence (taken on a fresh frame, on the hand's first frame and while it is not lost), LostSeconds,
        /// AgeSeconds and PalmVelocity. A hand is skipped when a joint is behind the camera or its palm is nearer
        /// than 0.05 m, and a region that cannot touch the image is left out.
        ///
        /// The mask is dropped whole when the samples it would hide are more than
        /// <see cref="HandsConstants.MaskMaxImageShare"/> of the samples Bump Guard reads. What counts is the samples
        /// with a return inside a region and inside the depth gate that holds there (the box's inside the box, the
        /// strip's own outside it), not the area of the regions: grown by 35 % on
        /// every side, two open hands at the default reach cover more than the limit by area alone, but they hide
        /// only the hands and the forearms, because the room behind them is deeper than the gate.
        ///
        /// <see cref="DepthMask.DepthSequence"/> is the image's own, also on an empty mask. Nothing is allocated.
        /// </summary>
        /// <param name="now">The frame's time, seconds on the app clock.</param>
        /// <param name="depth">The newest depth image, with the pose and intrinsics it was taken with.</param>
        /// <param name="left">The left hand as the filter hands it over this frame.</param>
        /// <param name="right">The right hand.</param>
        public DepthMask Build(double now, in DepthFrame depth, in TrackedHand left, in TrackedHand right)
        {
            DepthMask mask = default(DepthMask);
            mask.DepthSequence = depth.Sequence;
            _lastShare = 0f;

            // The hands are remembered even when this frame has no usable depth: the next one needs them.
            Observe(ref _left, now, in left);
            Observe(ref _right, now, in right);

            if (!Usable(in depth)) return mask;

            float pixelScale = depth.Width / (float)DepthFrame.NominalWidth;
            quaternion toCamera = math.inverse(depth.Rotation);
            DepthMaskRegion region;
            if (TryRegion(now, in depth, toCamera, pixelScale, ref _left, out region))
            {
                mask.Region0 = region;
                mask.Count = 1;
            }
            if (TryRegion(now, in depth, toCamera, pixelScale, ref _right, out region))
            {
                if (mask.Count == 0) mask.Region0 = region;
                else mask.Region1 = region;
                mask.Count++;
            }
            if (mask.Count == 0) return mask;

            _lastShare = MaskedShare(in depth, ref mask);
            if (_lastShare > HandsConstants.MaskMaxImageShare)
            {
                mask = default(DepthMask);
                mask.DepthSequence = depth.Sequence;
            }
            return mask;
        }

        // ---- What is remembered ----

        static void Observe(ref Held held, double now, in TrackedHand hand)
        {
            // A clock that ran backwards (a replay seeking, a session that restarted) makes what was held another
            // timeline's.
            if (held.Has && now < held.SeenAt) held.Has = false;

            if (!hand.Tracked) return;
            if (!hand.World.IsFinite())
            {
                held.Has = false;
                return;
            }

            float lost = hand.LostSeconds > 0f ? hand.LostSeconds : 0f;   // NaN counts as 0
            // The confidence that counts is the detection's. Between detections and while the hand is lost the
            // filter's own figure may smooth or fall, so it is read only when a detection has just arrived, when the
            // hand is not lost, and on the first frame the hand is seen.
            if (hand.Fresh || !held.Has || lost <= 0f) held.Confidence = hand.Confidence;
            held.Has = true;
            held.Joints = hand.World;
            held.PalmVelocity = hand.PalmVelocity;
            held.DetectedAt = now - lost;
            held.SeenAt = now;
            held.AgeAtSeen = hand.AgeSeconds > 0f ? hand.AgeSeconds : 0f;
        }

        static bool Usable(in DepthFrame depth)
        {
            return depth.Depth != null && depth.Width > 0 && depth.Height > 0 && depth.Fx > 0f && depth.Fy > 0f
                && depth.Depth.Length >= depth.Width * depth.Height;
        }

        // ---- One hand's region ----

        static bool TryRegion(double now, in DepthFrame depth, quaternion toCamera, float pixelScale, ref Held held,
                              out DepthMaskRegion region)
        {
            region = default(DepthMaskRegion);
            if (!held.Has) return false;
            if (now - held.DetectedAt > LifeSeconds)
            {
                held.Has = false;
                return false;
            }
            if (!(held.Confidence >= HandsConstants.InteractConfidence)) return false;

            ref JointSet joints = ref held.Joints;
            float fx = depth.Fx, fy = depth.Fy;

            // Every joint into the depth camera's space and then into its picture. The palm is the mean of the wrist
            // and the four knuckles.
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            float3 palm = float3.zero;
            float2 wrist = float2.zero, middleKnuckle = float2.zero;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float3 c = math.mul(toCamera, joints[joint] - depth.Position);
                if (!(c.z > MinJointDepthMetres)) return false;   // behind the camera (and not a number)
                float u = depth.Cx + c.x / c.z * fx - 0.5f;
                float v = depth.Cy - c.y / c.z * fy - 0.5f;
                minU = math.min(minU, u);
                maxU = math.max(maxU, u);
                minV = math.min(minV, v);
                maxV = math.max(maxV, v);
                switch (joint)
                {
                    case (int)HandJoint.Wrist:
                        wrist = new float2(u, v);
                        palm += c;
                        break;
                    case (int)HandJoint.MiddleMcp:
                        middleKnuckle = new float2(u, v);
                        palm += c;
                        break;
                    case (int)HandJoint.IndexMcp:
                    case (int)HandJoint.RingMcp:
                    case (int)HandJoint.LittleMcp:
                        palm += c;
                        break;
                }
            }
            palm *= 0.2f;
            float palmDepth = palm.z;
            if (!(palmDepth >= MinPalmDepthMetres)) return false;

            // Growth: a share of the box's longer side on every side, and the distance the palm can have moved in the
            // picture since the pose was made, by its speed there (the derivative of the projection along the
            // velocity) times the pose's age, at most the cap. The cap is in pixels of the nominal 256-wide image.
            float side = math.max(maxU - minU, maxV - minV);
            float3 velocity = math.mul(toCamera, held.PalmVelocity);
            float du = fx * (velocity.x - palm.x / palmDepth * velocity.z) / palmDepth;
            float dv = -fy * (velocity.y - palm.y / palmDepth * velocity.z) / palmDepth;
            float age = held.AgeAtSeen + (float)(now - held.SeenAt);
            float motion = math.sqrt(du * du + dv * dv) * (age > 0f ? age : 0f);
            float cap = HandsConstants.MaskMotionMaxPixels * pixelScale;
            if (!(motion >= 0f)) motion = 0f;      // not a number: no growth
            else if (motion > cap) motion = cap;
            float grow = HandsConstants.MaskGrow * side + motion;

            region.MinU = minU - grow;
            region.MaxU = maxU + grow;
            region.MinV = minV - grow;
            region.MaxV = maxV + grow;
            region.MaxDepth = math.min(palmDepth + HandsConstants.MaskBehindPalmMetres, HandsConstants.MaskMaxDepthMetres);
            // The strip outside the box has a nearer gate of its own: the forearm is no deeper than the palm.
            region.ArmMaxDepth = math.min(palmDepth + ArmBehindPalmMetres, HandsConstants.MaskMaxDepthMetres);

            // The forearm: from the wrist, away from the middle knuckle, to where that leaves the image; as wide as
            // MaskArmPalmWidths palm widths (the palm's real width at the palm's depth, so that a hand turned edge-on
            // does not make a thin arm) plus the same growth.
            float palmWidth = math.distance(joints[(int)HandJoint.IndexMcp], joints[(int)HandJoint.LittleMcp]);
            float armHalfWidth = 0.5f * HandsConstants.MaskArmPalmWidths * palmWidth * (0.5f * (fx + fy)) / palmDepth + grow;
            float2 away = wrist - middleKnuckle;
            float awayLength = math.length(away);
            float2 end;
            if (armHalfWidth > 0f && awayLength > MinArmDirectionPixels
                && ArmEnd(wrist, away / awayLength, depth.Width, depth.Height, out end))
            {
                region.ArmU0 = wrist.x;
                region.ArmV0 = wrist.y;
                region.ArmU1 = end.x;
                region.ArmV1 = end.y;
                region.ArmHalfWidth = armHalfWidth;
            }

            if (!IsFinite(in region)) return false;
            // A hand that is wholly outside the picture and has no forearm in it masks nothing.
            bool boxInImage = region.MaxU >= -0.5f && region.MinU <= depth.Width - 0.5f
                           && region.MaxV >= -0.5f && region.MinV <= depth.Height - 0.5f;
            return boxInImage || region.ArmHalfWidth > 0f;
        }

        /// <summary>
        /// Where the ray from the wrist, away from the fingers, leaves the image (its outer edge: half a pixel beyond
        /// the centres of the border samples). False when the ray never enters the image: the wrist is outside it and
        /// the forearm runs away from it.
        /// </summary>
        static bool ArmEnd(float2 wrist, float2 away, int width, int height, out float2 end)
        {
            end = wrist;
            float t0 = 0f, t1 = float.PositiveInfinity;
            if (!Clip(wrist.x, away.x, -0.5f, width - 0.5f, ref t0, ref t1)) return false;
            if (!Clip(wrist.y, away.y, -0.5f, height - 0.5f, ref t0, ref t1)) return false;
            if (!(t1 > t0)) return false;
            end = wrist + away * t1;
            return true;
        }

        /// <summary>One slab of the ray against the image: narrows the span [t0, t1] of the ray that is inside it.</summary>
        static bool Clip(float origin, float direction, float low, float high, ref float t0, ref float t1)
        {
            if (math.abs(direction) < 1e-6f) return origin >= low && origin <= high;
            float a = (low - origin) / direction, b = (high - origin) / direction;
            if (a > b)
            {
                float swap = a;
                a = b;
                b = swap;
            }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }

        static bool IsFinite(in DepthMaskRegion r)
        {
            return math.isfinite(r.MinU) && math.isfinite(r.MaxU) && math.isfinite(r.MinV) && math.isfinite(r.MaxV)
                && math.isfinite(r.ArmU0) && math.isfinite(r.ArmV0) && math.isfinite(r.ArmU1) && math.isfinite(r.ArmV1)
                && math.isfinite(r.ArmHalfWidth) && math.isfinite(r.MaxDepth) && math.isfinite(r.ArmMaxDepth);
        }

        // ---- The size limit ----

        /// <summary>
        /// The share of Bump Guard's samples that are returns the mask contains, each region's box and strip with
        /// its own gate. Only the samples inside the rectangle that holds every region are looked at; the
        /// denominator is the whole grid.
        /// </summary>
        static float MaskedShare(in DepthFrame depth, ref DepthMask mask)
        {
            const int stride = ShareStride;
            const int first = stride / 2;
            int columns = depth.Width > first ? (depth.Width - first + stride - 1) / stride : 0;
            int rows = depth.Height > first ? (depth.Height - first + stride - 1) / stride : 0;
            int total = columns * rows;
            if (total <= 0) return 0f;

            // The rectangle of samples the regions can reach.
            float minU = mask.Region0.MinU, minV = mask.Region0.MinV, maxU = mask.Region0.MaxU, maxV = mask.Region0.MaxV;
            Reach(in mask.Region0, ref minU, ref minV, ref maxU, ref maxV);
            if (mask.Count > 1) Reach(in mask.Region1, ref minU, ref minV, ref maxU, ref maxV);
            // Clamped before the conversion: a float far outside the range of int does not convert to anything useful.
            int c0 = (int)math.clamp(math.ceil((minU - first) / stride), 0f, columns);
            int c1 = (int)math.clamp(math.floor((maxU - first) / stride), -1f, columns - 1);
            int r0 = (int)math.clamp(math.ceil((minV - first) / stride), 0f, rows);
            int r1 = (int)math.clamp(math.floor((maxV - first) / stride), -1f, rows - 1);

            // Most samples are the room behind the hands, deeper than any gate: turned away before the regions are asked.
            float gate = math.max(mask.Region0.MaxDepth, mask.Region0.ArmMaxDepth);
            if (mask.Count > 1) gate = math.max(gate, math.max(mask.Region1.MaxDepth, mask.Region1.ArmMaxDepth));
            float[] buffer = depth.Depth;
            int masked = 0;
            for (int r = r0; r <= r1; r++)
            {
                int v = first + r * stride;
                int rowStart = v * depth.Width;
                for (int c = c0; c <= c1; c++)
                {
                    int u = first + c * stride;
                    float d = buffer[rowStart + u];
                    if (d <= gate && DepthFrame.IsReturn(d) && mask.Contains(u, v, d)) masked++;
                }
            }
            return masked / (float)total;
        }

        /// <summary>Widens a rectangle to take in a region's box and the whole of its strip.</summary>
        static void Reach(in DepthMaskRegion r, ref float minU, ref float minV, ref float maxU, ref float maxV)
        {
            minU = math.min(minU, r.MinU);
            minV = math.min(minV, r.MinV);
            maxU = math.max(maxU, r.MaxU);
            maxV = math.max(maxV, r.MaxV);
            if (r.ArmHalfWidth > 0f)
            {
                minU = math.min(minU, math.min(r.ArmU0, r.ArmU1) - r.ArmHalfWidth);
                minV = math.min(minV, math.min(r.ArmV0, r.ArmV1) - r.ArmHalfWidth);
                maxU = math.max(maxU, math.max(r.ArmU0, r.ArmU1) + r.ArmHalfWidth);
                maxV = math.max(maxV, math.max(r.ArmV0, r.ArmV1) + r.ArmHalfWidth);
            }
        }
    }
}
