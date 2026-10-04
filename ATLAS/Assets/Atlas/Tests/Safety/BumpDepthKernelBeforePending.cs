using Atlas.Safety;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// The depth step of Bump Guard as it was before near returns had to be seen twice (decision R13):
    /// BumpDepthKernel.Process with its helpers, the DynamicLayer calls it makes and the depth mask's own test,
    /// copied word for word from commit 515d14c. It is the reference of
    /// BumpGuard_Pending_OutsideASessionNothingChanges: outside a hands session the real kernel must leave every
    /// output and every cell of the dynamic layer bit for bit as this one does, with a depth mask or with none,
    /// on whatever machine the test runs. It reads a region as that commit did: one gate for the hand's
    /// rectangle and its forearm strip alike. Never edit it to follow the real kernel; that would make the
    /// comparison say nothing.
    /// </summary>
    public static unsafe class BumpDepthKernelBeforePending
    {
        const int Shift = BumpConstants.BlockShift;
        const int WallBins = 112;
        const float WallBinSize = 0.005f;

        static int FirstSample(int stride) => stride / 2;

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
                maskGate = Gate(input.Mask);
                masking = MaskWithinItsShare(depth, input, stride, first, maskGate, out result.MaskRefused);
            }

            for (int v = first; v < input.Height; v += stride)
            {
                float* row = depth + v * input.Width;
                float ny = -(v + 0.5f - input.Intrinsics.Cy) * invFy;
                for (int u = first; u < input.Width; u += stride)
                {
                    float d = row[u];
                    if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.DepthMax)) continue;

                    // A hand: no return, for everything below.
                    if (masking && d <= maskGate && Covers(input.Mask, u + 0.5f, v + 0.5f, d))
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
                        if (Stamp(dynamic, hit.xz, hit.y, input.Time)) result.Stamped++;
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
                    if (d <= gate && Covers(input.Mask, u + 0.5f, v + 0.5f, d)) masked++;
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
                    if (!Covers(input.Mask, u + 0.5f, v + 0.5f, d)) continue;
                    float nx = (u + 0.5f - input.Intrinsics.Cx) * invFx;
                    float3 hit = origin + math.mul(rot, new float3(nx * d, ny * d, d));
                    if (ForgetIfYoung(dynamic, hit.xz, hit.y, input.Time)) forgotten++;
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
                if (ClearIfSeenEmpty(dynamic, origin.xz + dir * t, origin.y + slope * t, sequence, time)) cleared++;
            }
            return cleared;
        }

        // ---- The dynamic layer's part, as it was ----

        static bool Stamp(in DynamicGrid g, float2 p, float height, double time)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float old = g.TopHeight[idx];
            if (old <= 0f)
            {
                g.BlockCount[(j >> Shift) * g.BlocksX + (i >> Shift)]++;
                (*g.ActiveCount)++;
                g.TopHeight[idx] = height;
                g.FirstHit[idx] = time;
            }
            else
            {
                if (height > old) g.TopHeight[idx] = height;
                if (g.LastHit[idx] < time - BumpConstants.DynExpirySeconds) g.FirstHit[idx] = time;
            }
            g.LastHit[idx] = time;
            g.EmptyState[idx] = 0;
            return true;
        }

        static bool ClearIfSeenEmpty(in DynamicGrid g, float2 p, float rayHeight, int sequence, double time)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float top = g.TopHeight[idx];
            if (top <= 0f || !(rayHeight < top - BumpConstants.ClearHeightTolerance)) return false;
            if (g.LastHit[idx] == time) return false;

            int mark = (sequence & 0x7FFFFF) + 1;
            int state = g.EmptyState[idx];
            if (state >> 8 == mark) return false; // already counted in this image
            int seen = (state & 0xFF) + 1;
            if (seen >= BumpConstants.ClearConfirmFrames)
            {
                Remove(g, i, j);
                return true;
            }
            g.EmptyState[idx] = (mark << 8) | seen;
            return false;
        }

        static bool ForgetIfYoung(in DynamicGrid g, float2 p, float height, double time)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float top = g.TopHeight[idx];
            if (top <= 0f) return false;
            if (g.LastHit[idx] == time) return false;
            // Written so that a time that is not a number, or a first stamp later than now, keeps the cell.
            double age = time - g.FirstHit[idx];
            if (!(age >= 0.0 && age < BumpConstants.MaskYoungSeconds)) return false;
            if (!(math.abs(top - height) <= BumpConstants.MaskHeightTolerance)) return false;
            Remove(g, i, j);
            return true;
        }

        static void Remove(in DynamicGrid g, int i, int j)
        {
            g.TopHeight[j * g.Spec.Width + i] = 0f;
            g.EmptyState[j * g.Spec.Width + i] = 0;
            g.BlockCount[(j >> Shift) * g.BlocksX + (i >> Shift)]--;
            (*g.ActiveCount)--;
        }

        static bool CellOf(in BumpGridSpec s, float2 p, out int i, out int j)
        {
            float inv = 1f / s.Cell;
            float u = (p.x - s.OriginX) * inv;
            float v = (p.y - s.OriginZ) * inv;
            i = (int)math.floor(u);
            j = (int)math.floor(v);
            return u >= 0f && v >= 0f && i < s.Width && j < s.Height;
        }

        // ---- The depth mask's own test, as it was ----

        static bool Covers(in DepthKernelMaskRegion r, float u, float v, float depth)
        {
            if (!(depth <= r.MaxDepth && depth <= BumpConstants.MaskMaxDepth)) return false;
            if (u >= r.MinU && u <= r.MaxU && v >= r.MinV && v <= r.MaxV) return true;
            if (!(r.ArmHalfWidth > 0f)) return false;
            float du = r.ArmU1 - r.ArmU0, dv = r.ArmV1 - r.ArmV0;
            float lengthSq = du * du + dv * dv;
            float t = lengthSq > 1e-6f ? ((u - r.ArmU0) * du + (v - r.ArmV0) * dv) / lengthSq : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            float ru = u - (r.ArmU0 + t * du), rv = v - (r.ArmV0 + t * dv);
            return ru * ru + rv * rv <= r.ArmHalfWidth * r.ArmHalfWidth;
        }

        static float Gate(in DepthKernelMask m)
        {
            if (m.Count <= 0) return 0f;
            float gate = m.Region0.MaxDepth;
            if (m.Count > 1 && !(m.Region1.MaxDepth <= gate)) gate = m.Region1.MaxDepth;
            return gate <= BumpConstants.MaskMaxDepth ? gate : BumpConstants.MaskMaxDepth;
        }

        static bool Covers(in DepthKernelMask m, float u, float v, float depth)
        {
            if (m.Count <= 0) return false;
            if (Covers(m.Region0, u, v, depth)) return true;
            return m.Count > 1 && Covers(m.Region1, u, v, depth);
        }
    }
}
