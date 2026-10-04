using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>
    /// Pinhole intrinsics of the depth image, in pixels of that image. Pixel (u, v) with u to the right and
    /// v downwards (row 0 is the top row) and depth d, measured along the optical axis, is the sensor-frame
    /// point ((u + 0.5 - Cx) / Fx * d, -(v + 0.5 - Cy) / Fy * d, d): +x image right, +y image up, +z into the scene.
    /// </summary>
    public struct DepthIntrinsics
    {
        public float Fx;
        public float Fy;
        public float Cx;
        public float Cy;

        public DepthIntrinsics(float fx, float fy, float cx, float cy)
        {
            Fx = fx;
            Fy = fy;
            Cx = cx;
            Cy = cy;
        }
    }

    /// <summary>Everything the depth kernel needs about one depth frame besides the pixels.</summary>
    public struct DepthKernelInput
    {
        /// <summary>Image width, pixels (256).</summary>
        public int Width;
        /// <summary>Image height, pixels (192).</summary>
        public int Height;
        /// <summary>Subsampling stride, pixels. Samples are taken at stride / 2, stride / 2 + stride, ...</summary>
        public int Stride;
        public DepthIntrinsics Intrinsics;
        /// <summary>Sensor position in the room frame, metres. Only its height matters when the map is not trusted.</summary>
        public float3 SensorPosition;
        /// <summary>Sensor-to-room rotation (sensor frame as in <see cref="DepthIntrinsics"/>).</summary>
        public quaternion SensorRotation;
        /// <summary>Time of the frame, seconds.</summary>
        public double Time;
        /// <summary>Number of this depth image; each processed image has its own.</summary>
        public int Sequence;
        /// <summary>
        /// True when tracking is Normal, the anchor is tracked and a room is loaded. Only then is the dynamic
        /// layer stamped or cleared and the static map compared; otherwise the kernel fills the sectors only.
        /// </summary>
        public bool MapTrusted;
        /// <summary>
        /// The walker's own hands in this image, or the empty mask (the default). A sample it covers counts as
        /// no return. The kernel holds the mask to the limits of <see cref="BumpConstants"/> itself. The mask
        /// also says whether the image belongs to a hands session (<see cref="DepthKernelMask.Session"/>).
        /// </summary>
        public DepthKernelMask Mask;
    }

    /// <summary>What one depth frame produced.</summary>
    public struct DepthFrameResult
    {
        // Nearest return in each of the seven 10-degree sectors, left (-35 degrees) to right (+35 degrees):
        // horizontal distance from the sensor, metres; positive infinity when the sector saw nothing.
        public float Sector0, Sector1, Sector2, Sector3, Sector4, Sector5, Sector6;
        /// <summary>Median distance between wall hits and the static map's wall faces, metres, to 5 mm. Valid when <see cref="WallSamples"/> is large enough.</summary>
        public float WallDisagreement;
        /// <summary>Number of wall hits behind <see cref="WallDisagreement"/>.</summary>
        public int WallSamples;
        /// <summary>Depth samples that marked the dynamic layer this frame.</summary>
        public int Stamped;
        /// <summary>Dynamic cells seen empty and cleared this frame.</summary>
        public int Cleared;
        /// <summary>Depth samples the mask took out this frame: they counted as no return. 0 with no mask, and with one that was refused.</summary>
        public int Masked;
        /// <summary>Dynamic cells forgotten this frame because a masked sample pointed at them and they were under a second old.</summary>
        public int Forgotten;
        /// <summary>A mask came with this frame and was ignored: it would have taken out more than its share of the valid samples.</summary>
        public bool MaskRefused;
        /// <summary>Time of the frame, seconds.</summary>
        public double Time;
        /// <summary>Whether the frame was processed with the map trusted.</summary>
        public bool MapTrusted;

        /// <summary>Nearest return in sector <paramref name="index"/> (0 = leftmost, 6 = rightmost), metres.</summary>
        public float Sector(int index)
        {
            switch (index)
            {
                case 0: return Sector0;
                case 1: return Sector1;
                case 2: return Sector2;
                case 3: return Sector3;
                case 4: return Sector4;
                case 5: return Sector5;
                default: return Sector6;
            }
        }

        /// <summary>The nearest sector return and its sector; false when every sector is empty.</summary>
        public bool Nearest(out float distance, out int sector)
        {
            distance = float.PositiveInfinity;
            sector = BumpConstants.SectorCount / 2;
            for (int k = 0; k < BumpConstants.SectorCount; k++)
            {
                float d = Sector(k);
                if (d < distance)
                {
                    distance = d;
                    sector = k;
                }
            }
            return distance < float.PositiveInfinity;
        }

        /// <summary>Centre of sector <paramref name="index"/> as an angle from straight ahead, radians, positive to the right.</summary>
        public static float SectorAngle(int index)
        {
            float width = 2f * BumpConstants.SectorHalfSpan / BumpConstants.SectorCount;
            return -BumpConstants.SectorHalfSpan + (index + 0.5f) * width;
        }

        /// <summary>A result with nothing seen.</summary>
        public static DepthFrameResult Empty
        {
            get
            {
                DepthFrameResult r = default;
                r.Sector0 = r.Sector1 = r.Sector2 = r.Sector3 = r.Sector4 = r.Sector5 = r.Sector6 = float.PositiveInfinity;
                return r;
            }
        }
    }

    /// <summary>
    /// The live-depth half of Bump Guard (Master Plan section 4.10): one pass over the subsampled LiDAR depth
    /// image that stamps and clears the dynamic layer, fills the seven sector minima and measures how far the
    /// walls in view lie from the static map. Plain static code over raw pointers so that the Burst job and the
    /// headless tests run the same instructions (contracts F2). Allocates nothing.
    /// <para>Samples that the frame's depth mask covers are the walker's own hands
    /// (documents/ATLAS_Hands_Design.md section 7.1). They are left out of the sectors, the dynamic layer and the
    /// rescan check as if they had no return, and the cells a hand stamped before it was recognised are
    /// forgotten (<see cref="DynamicLayer.ForgetIfYoung"/>). With the empty mask every result is bit for bit
    /// what it was before the kernel knew of masks.</para>
    /// <para>In a hands session (the mask says so, with or without a region) a near return must be seen twice
    /// (decision R13): the cell it stamps is pending until a hit that comes
    /// <see cref="BumpConstants.PendingSeconds"/> or more after its first, so that a hand is masked before it
    /// can count. Returns at <see cref="BumpConstants.PendingNearMetres"/> or beyond stamp as they always did,
    /// the static map is never pending, and the sectors of the head-frame fallback and the rescan check, which
    /// read no dynamic cell, are filled as outside a session. Outside a session every result and every cell
    /// is bit for bit what it was before the rule.</para>
    /// </summary>
    public static unsafe class BumpDepthKernel
    {
        // Histogram of wall disagreements: 5 mm bins up to the 0.55 m that a compared sample can reach.
        const int WallBins = 112;
        const float WallBinSize = 0.005f;

        /// <summary>Index of the first row and column sampled at <paramref name="stride"/>.</summary>
        public static int FirstSample(int stride) => stride / 2;

        /// <summary>
        /// Processes one depth frame.
        /// </summary>
        /// <param name="depth">Width x Height depths in metres, row-major, row 0 at the top of the image. Zero, negative or NaN means no reading.</param>
        /// <param name="input">Frame description.</param>
        /// <param name="sdf">Static distance field; may be null when <c>input.MapTrusted</c> is false.</param>
        /// <param name="grid">Layout of <paramref name="sdf"/>.</param>
        /// <param name="dynamic">Dynamic layer; not touched when <c>input.MapTrusted</c> is false.</param>
        /// <param name="result">Sector minima, wall disagreement and counters.</param>
        public static void Process(float* depth, in DepthKernelInput input, float* sdf, in BumpGridSpec grid,
            in DynamicGrid dynamic, out DepthFrameResult result)
        {
            result = DepthFrameResult.Empty;
            result.Time = input.Time;

            bool trusted = input.MapTrusted && sdf != null && dynamic.IsCreated && grid.IsValid;
            result.MapTrusted = trusted;

            int stride = math.max(input.Stride, 1);
            float3x3 rot = new float3x3(input.SensorRotation);
            float3 origin = input.SensorPosition;
            float2 origin2 = origin.xz;

            // Horizontal view direction. Looking straight down or up, the image's up axis stands in for it.
            float2 fwd = rot.c2.xz;
            if (math.lengthsq(fwd) < 1e-4f) fwd = rot.c2.y < 0f ? rot.c1.xz : -rot.c1.xz;
            fwd = math.normalizesafe(fwd, new float2(0f, 1f));
            float2 right = new float2(fwd.y, -fwd.x);

            float* sector = stackalloc float[BumpConstants.SectorCount];
            for (int k = 0; k < BumpConstants.SectorCount; k++) sector[k] = float.PositiveInfinity;
            float sectorScale = BumpConstants.SectorCount / (2f * BumpConstants.SectorHalfSpan);

            int* wallBins = stackalloc int[WallBins];
            for (int k = 0; k < WallBins; k++) wallBins[k] = 0;
            int wallCount = 0;

            float invFx = 1f / input.Intrinsics.Fx;
            float invFy = 1f / input.Intrinsics.Fy;
            int first = FirstSample(stride);

            // The walker's own hands. Decided for the whole image before any sample is used: a mask that would
            // take out more than its share is not a pair of hands, and the image is read as if it had none.
            bool masking = false;
            float maskGate = 0f;
            if (input.Mask.Count > 0)
            {
                maskGate = input.Mask.Gate;
                masking = MaskWithinItsShare(depth, input, stride, first, maskGate, out result.MaskRefused);
            }
            // A hands session: a cell that a near return makes is pending until it is seen again.
            bool session = input.Mask.Session != 0;

            for (int v = first; v < input.Height; v += stride)
            {
                float* row = depth + v * input.Width;
                float ny = -(v + 0.5f - input.Intrinsics.Cy) * invFy;
                for (int u = first; u < input.Width; u += stride)
                {
                    float d = row[u];
                    if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.DepthMax)) continue;

                    // A hand: no return, for everything below.
                    if (masking && d <= maskGate && input.Mask.Covers(u + 0.5f, v + 0.5f, d))
                    {
                        result.Masked++;
                        continue;
                    }

                    float nx = (u + 0.5f - input.Intrinsics.Cx) * invFx;
                    float3 rel = math.mul(rot, new float3(nx * d, ny * d, d));
                    float3 hit = origin + rel;
                    float2 rel2 = rel.xz;
                    float flat = math.length(rel2);

                    // A ray that lands farther away has seen everything under it empty.
                    if (trusted && *dynamic.ActiveCount > 0)
                        result.Cleared += ClearAlongRay(dynamic, origin, rel, flat, input.Sequence, input.Time);

                    if (!(hit.y > BumpConstants.DynMinHeight && hit.y < BumpConstants.DynMaxHeight))
                        continue;

                    // Sector minimum, in the head frame.
                    float ahead = math.dot(rel2, fwd);
                    float side = math.dot(rel2, right);
                    float azimuth = math.atan2(side, ahead);
                    if (math.abs(azimuth) <= BumpConstants.SectorHalfSpan)
                    {
                        int s = math.min((int)((azimuth + BumpConstants.SectorHalfSpan) * sectorScale),
                            BumpConstants.SectorCount - 1);
                        if (flat < sector[s]) sector[s] = flat;
                    }

                    if (!trusted) continue;

                    if (NearerThanStatic(sdf, grid, hit, rel, flat))
                    {
                        bool near = session;
                        if (DynamicLayer.Stamp(dynamic, hit.xz, hit.y, input.Time, near)) result.Stamped++;
                    }

                    if (hit.y >= BumpConstants.WallBandMinHeight)
                    {
                        // A hit on a mapped wall lies one inset outside the edge of the free floor.
                        float edge = StaticClearance.SampleExtended(sdf, grid, hit.xz);
                        if (math.abs(edge) < BumpConstants.RescanNearEdge)
                        {
                            int bin = (int)(math.abs(edge + BumpConstants.StaticInset) / WallBinSize);
                            wallBins[math.min(bin, WallBins - 1)]++;
                            wallCount++;
                        }
                    }
                }
            }

            // The cells a hand stamped before it was masked. After the loop, so that a cell which a sample outside
            // the mask hit in this image is kept whichever of the two samples came first.
            if (masking && trusted && *dynamic.ActiveCount > 0)
                result.Forgotten = ForgetUnderMask(depth, input, rot, origin, dynamic, stride, first, maskGate);

            result.Sector0 = sector[0];
            result.Sector1 = sector[1];
            result.Sector2 = sector[2];
            result.Sector3 = sector[3];
            result.Sector4 = sector[4];
            result.Sector5 = sector[5];
            result.Sector6 = sector[6];
            result.WallSamples = wallCount;
            result.WallDisagreement = 0f;
            if (wallCount > 0)
            {
                int below = 0;
                for (int k = 0; k < WallBins; k++)
                {
                    below += wallBins[k];
                    if (2 * below >= wallCount)
                    {
                        result.WallDisagreement = (k + 0.5f) * WallBinSize;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// True when the hit is more than 0.3 m nearer than the static map predicts along its ray: the ray, carried
        /// on from the hit, covers 0.3 m without reaching the floor or the edge of the free floor.
        /// </summary>
        static bool NearerThanStatic(float* sdf, in BumpGridSpec grid, float3 hit, float3 rel, float flat)
        {
            float length = math.length(rel);
            if (!(length > 1e-4f)) return false;
            float need = BumpConstants.DynStaticMargin;

            // The floor is part of the static map.
            float down = -rel.y / length;
            if (down > 0f && hit.y <= need * down) return false;

            float2 p = hit.xz;
            float s = StaticClearance.Sample(sdf, grid, p);
            if (s <= 0f) return false;

            float flatPerMetre = flat / length;
            if (flatPerMetre < 1e-3f) return true; // straight down onto something standing in free floor
            float2 dir = rel.xz / flat;
            float limit = need * flatPerMetre;

            // March along the floor; the field cannot change faster than the distance covered, so a step of the
            // current clearance never jumps the edge.
            float t = 0f;
            for (int k = 0; k < 16 && t < limit; k++)
            {
                t = math.min(t + math.max(s, 0.5f * grid.Cell), limit);
                s = StaticClearance.Sample(sdf, grid, p + dir * t);
                if (s <= 0f) return false;
            }
            return t >= limit;
        }

        // Counts what the mask would take out of this image. True when it takes something and no more than its
        // share of the valid samples; refused is set when it would take more.
        static bool MaskWithinItsShare(float* depth, in DepthKernelInput input, int stride, int first, float gate,
            out bool refused)
        {
            int valid = 0, masked = 0;
            for (int v = first; v < input.Height; v += stride)
            {
                float* row = depth + v * input.Width;
                for (int u = first; u < input.Width; u += stride)
                {
                    float d = row[u];
                    if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.DepthMax)) continue;
                    valid++;
                    if (d <= gate && input.Mask.Covers(u + 0.5f, v + 0.5f, d)) masked++;
                }
            }
            refused = masked > BumpConstants.MaskMaxShare * valid;
            return masked > 0 && !refused;
        }

        // The young-cell rule for every sample the mask took out: the cell under the point it lands at.
        static int ForgetUnderMask(float* depth, in DepthKernelInput input, float3x3 rot, float3 origin,
            in DynamicGrid dynamic, int stride, int first, float gate)
        {
            float invFx = 1f / input.Intrinsics.Fx;
            float invFy = 1f / input.Intrinsics.Fy;
            int forgotten = 0;
            for (int v = first; v < input.Height; v += stride)
            {
                float* row = depth + v * input.Width;
                float ny = -(v + 0.5f - input.Intrinsics.Cy) * invFy;
                for (int u = first; u < input.Width; u += stride)
                {
                    float d = row[u];
                    if (!(d >= BumpConstants.DepthMin && d <= gate)) continue;
                    if (!input.Mask.Covers(u + 0.5f, v + 0.5f, d)) continue;
                    float nx = (u + 0.5f - input.Intrinsics.Cx) * invFx;
                    float3 hit = origin + math.mul(rot, new float3(nx * d, ny * d, d));
                    if (DynamicLayer.ForgetIfYoung(dynamic, hit.xz, hit.y, input.Time)) forgotten++;
                }
            }
            return forgotten;
        }

        static int ClearAlongRay(in DynamicGrid dynamic, float3 origin, float3 rel, float flat, int sequence, double time)
        {
            float reach = flat - BumpConstants.ClearRayMargin;
            if (!(reach > 0f)) return 0;
            float step = dynamic.Spec.Cell;
            float2 dir = rel.xz / flat;
            float slope = rel.y / flat;
            int cleared = 0;
            for (float t = 0f; t <= reach; t += step)
            {
                if (DynamicLayer.ClearIfSeenEmpty(dynamic, origin.xz + dir * t, origin.y + slope * t, sequence, time)) cleared++;
            }
            return cleared;
        }

    }
}
