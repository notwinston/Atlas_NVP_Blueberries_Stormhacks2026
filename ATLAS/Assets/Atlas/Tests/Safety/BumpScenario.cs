using System;
using System.Collections.Generic;
using Atlas.Safety;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>An axis-aligned box standing in the room, seen only by the synthetic depth image.</summary>
    public struct SimBox
    {
        public float3 Min;
        public float3 Max;

        /// <summary>A box standing on the floor, centred on (x, z).</summary>
        public static SimBox OnFloor(float x, float z, float sizeX, float sizeZ, float height)
        {
            return new SimBox
            {
                Min = new float3(x - 0.5f * sizeX, 0f, z - 0.5f * sizeZ),
                Max = new float3(x + 0.5f * sizeX, height, z + 0.5f * sizeZ)
            };
        }

        public bool ContainsFloorPoint(float2 p) => p.x >= Min.x && p.x <= Max.x && p.y >= Min.z && p.y <= Max.z;
    }

    /// <summary>One frame of a scripted walk.</summary>
    public struct SimPose
    {
        public float2 Position;
        /// <summary>Heading, radians; 0 looks along +Z, positive turns towards +X.</summary>
        public float Yaw;
    }

    /// <summary>What a run recorded at a change of level.</summary>
    public struct LevelChange
    {
        public double Time;
        public BumpLevel From;
        public BumpLevel To;
        public float2 Position;
    }

    /// <summary>
    /// A small deterministic stand-in for a walker in a scanned room: a rectangular room whose distance field is
    /// what the scan would have produced (free floor one 5 cm cell short of every wall, section 4.2), boxes
    /// placed after the scan, a level-looking depth camera rendered by ray casting, and a head that follows a
    /// scripted path at 60 Hz. It drives a real <see cref="BumpGuardCore"/>.
    /// </summary>
    public sealed unsafe class BumpScenario : IDisposable
    {
        public const float FrameRate = 60f;
        public const float HeadHeight = 1.6f;
        public const float CeilingHeight = 2.5f;
        public const float GridMargin = 0.25f;
        public const int DepthW = BumpConstants.DepthWidth;
        public const int DepthH = BumpConstants.DepthHeight;

        public readonly BumpGuardCore Core = new BumpGuardCore();
        public readonly List<SimBox> Boxes = new List<SimBox>();
        public readonly List<LevelChange> Changes = new List<LevelChange>(16);

        /// <summary>Half extents of the mapped room (walls at +/- these).</summary>
        public readonly float2 MapHalf;
        /// <summary>Half extents of the room the depth camera sees; differs from the map to simulate a stale scan.</summary>
        public float2 RealHalf;
        /// <summary>Vertical field of view about 48 degrees (section 4.10).</summary>
        public readonly DepthIntrinsics Intrinsics;
        public bool DepthEnabled = true;
        public bool TrackingNormal = true;
        public bool AnchorTracked = true;
        public double Time;
        public int Frame;

        /// <summary>The last rendered depth image, metres.</summary>
        public readonly float[] Depth = new float[DepthW * DepthH];

        public BumpScenario(float halfX, float halfZ)
        {
            MapHalf = new float2(halfX, halfZ);
            RealHalf = MapHalf;
            float f = (DepthH * 0.5f) / math.tan(math.radians(24f));
            Intrinsics = new DepthIntrinsics(f, f, DepthW * 0.5f, DepthH * 0.5f);

            float cell = BumpConstants.CellSize;
            int w = (int)math.round(2f * (halfX + GridMargin) / cell);
            int h = (int)math.round(2f * (halfZ + GridMargin) / cell);
            float ox = -(halfX + GridMargin), oz = -(halfZ + GridMargin);
            var sdf = new float[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    sdf[j * w + i] = MapClearance(new float2(ox + (i + 0.5f) * cell, oz + (j + 0.5f) * cell));
            Core.SetRoom(sdf, w, h, cell, ox, oz);
        }

        /// <summary>The distance field of the scan at p: distance to the nearest wall, less the one-cell inset.</summary>
        public float MapClearance(float2 p)
        {
            float2 d = MapHalf - math.abs(p);
            return math.min(d.x, d.y) - BumpConstants.StaticInset;
        }

        /// <summary>Advances one frame with the head at the given pose.</summary>
        public void StepFrame(SimPose pose)
        {
            StepFrame(new float3(pose.Position.x, HeadHeight, pose.Position.y), quaternion.RotateY(pose.Yaw));
        }

        public void StepFrame(float3 head, quaternion rotation)
        {
            if (DepthEnabled)
            {
                Render(head, rotation);
                fixed (float* d = Depth)
                    Core.ProcessDepth(d, DepthW, DepthH, Intrinsics, head, rotation, Time, TrackingNormal, AnchorTracked);
            }
            BumpLevel before = Core.Level;
            Core.Step(Time, head, rotation, TrackingNormal, AnchorTracked);
            if (Core.Level != before)
                Changes.Add(new LevelChange { Time = Time, From = before, To = Core.Level, Position = head.xz });
            Frame++;
            Time = Frame / (double)FrameRate;
        }

        /// <summary>Stands at one pose for a while.</summary>
        public void Hold(SimPose pose, float seconds)
        {
            int frames = (int)math.round(seconds * FrameRate);
            for (int k = 0; k < frames; k++) StepFrame(pose);
        }

        /// <summary>Walks in a straight line from <paramref name="from"/> at a constant speed, looking where it goes.</summary>
        public void Walk(float2 from, float2 direction, float speed, float seconds, Func<BumpScenario, float2, bool> stop = null)
        {
            direction = math.normalize(direction);
            float yaw = math.atan2(direction.x, direction.y);
            int frames = (int)math.round(seconds * FrameRate);
            for (int k = 0; k < frames; k++)
            {
                float2 p = from + direction * (speed * k / FrameRate);
                if (stop != null && stop(this, p)) return;
                StepFrame(new SimPose { Position = p, Yaw = yaw });
            }
        }

        /// <summary>Number of times the cue started (Clear to anything else).</summary>
        public int RisingEdges()
        {
            int n = 0;
            foreach (LevelChange c in Changes) if (c.From == BumpLevel.Clear) n++;
            return n;
        }

        public bool FirstChangeTo(BumpLevel level, out LevelChange change)
        {
            foreach (LevelChange c in Changes)
                if (c.To == level)
                {
                    change = c;
                    return true;
                }
            change = default;
            return false;
        }

        public void Dispose() => Core.Dispose();

        // Depth image by ray casting, only at the pixels the kernel reads.
        public void Render(float3 origin, quaternion rotation)
        {
            int stride = BumpConstants.DepthStride;
            int first = BumpDepthKernel.FirstSample(stride);
            float3x3 rot = new float3x3(rotation);
            float3 lo = new float3(-RealHalf.x, 0f, -RealHalf.y);
            float3 hi = new float3(RealHalf.x, CeilingHeight, RealHalf.y);
            for (int v = first; v < DepthH; v += stride)
                for (int u = first; u < DepthW; u += stride)
                {
                    float3 dir = math.mul(rot, new float3((u + 0.5f - Intrinsics.Cx) / Intrinsics.Fx, -(v + 0.5f - Intrinsics.Cy) / Intrinsics.Fy, 1f));
                    // Inside the room: the first wall, floor or ceiling the ray leaves through.
                    float t = float.PositiveInfinity;
                    for (int a = 0; a < 3; a++)
                    {
                        if (dir[a] > 1e-9f) t = math.min(t, (hi[a] - origin[a]) / dir[a]);
                        else if (dir[a] < -1e-9f) t = math.min(t, (lo[a] - origin[a]) / dir[a]);
                    }
                    for (int b = 0; b < Boxes.Count; b++)
                    {
                        float tb = EnterBox(Boxes[b], origin, dir);
                        if (tb < t) t = tb;
                    }
                    Depth[v * DepthW + u] = t >= BumpConstants.DepthMin && t <= BumpConstants.DepthMax ? t : 0f;
                }
        }

        static float EnterBox(in SimBox box, float3 origin, float3 dir)
        {
            float tNear = 0f, tFar = float.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                if (math.abs(dir[a]) < 1e-9f)
                {
                    if (origin[a] < box.Min[a] || origin[a] > box.Max[a]) return float.PositiveInfinity;
                    continue;
                }
                float t0 = (box.Min[a] - origin[a]) / dir[a];
                float t1 = (box.Max[a] - origin[a]) / dir[a];
                if (t0 > t1)
                {
                    float s = t0;
                    t0 = t1;
                    t1 = s;
                }
                tNear = math.max(tNear, t0);
                tFar = math.min(tFar, t1);
                if (tNear > tFar) return float.PositiveInfinity;
            }
            return tNear > 0f ? tNear : float.PositiveInfinity;
        }
    }
}
