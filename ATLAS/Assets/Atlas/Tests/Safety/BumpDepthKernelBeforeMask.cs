using Atlas.Safety;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// The depth step of Bump Guard as it was before it learnt about the walker's hands: BumpDepthKernel.Process
    /// and the two DynamicLayer calls it makes, copied word for word from the commit before the depth mask
    /// (d69e186). It is the reference of BumpGuard_Mask_EmptyIsIdentity: with an empty mask the real kernel must
    /// leave every output and every cell of the dynamic layer bit for bit as this one does, on whatever machine
    /// the test runs. Never edit it to follow the real kernel; that would make the comparison say nothing.
    /// </summary>
    public static unsafe class BumpDepthKernelBeforeMask
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

            for (int v = first; v < input.Height; v += stride)
            {
                float* row = depth + v * input.Width;
                float ny = -(v + 0.5f - input.Intrinsics.Cy) * invFy;
                for (int u = first; u < input.Width; u += stride)
                {
                    float d = row[u];
                    if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.DepthMax)) continue;

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
            }
            else if (height > old)
            {
                g.TopHeight[idx] = height;
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
    }
}
