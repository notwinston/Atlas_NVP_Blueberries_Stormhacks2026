using System;
using NUnit.Framework;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// Bit-for-bit comparison of two Bump Guard cores that were given the same walk: what the depth step put
    /// out, what the step made of it, and every cell of the dynamic layer. The tests of the depth mask use it
    /// wherever a mask must change nothing.
    /// </summary>
    public static unsafe class BumpCompare
    {
        static bool SameBits(float a, float b)
        {
            return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        }

        /// <summary>
        /// What differs between the last frame of the two cores, or null when nothing does: the outputs of the
        /// depth step (the seven sectors, the wall disagreement and its sample count, the stamp and clear
        /// counters, the time, whether the map was trusted) and of the step (level, raw level, direction,
        /// clear(head), both paths, speed, the rescan advice and its median, the rising edges).
        /// </summary>
        public static string FrameDifference(BumpGuardCore a, BumpGuardCore b)
        {
            DepthFrameResult x = a.LastDepth, y = b.LastDepth;
            for (int k = 0; k < BumpConstants.SectorCount; k++)
                if (!SameBits(x.Sector(k), y.Sector(k))) return "sector " + k + ": " + x.Sector(k).ToString("R") + " against " + y.Sector(k).ToString("R");
            if (!SameBits(x.WallDisagreement, y.WallDisagreement)) return "wall disagreement: " + x.WallDisagreement + " against " + y.WallDisagreement;
            if (x.WallSamples != y.WallSamples) return "wall samples: " + x.WallSamples + " against " + y.WallSamples;
            if (x.Stamped != y.Stamped) return "stamped: " + x.Stamped + " against " + y.Stamped;
            if (x.Cleared != y.Cleared) return "cleared: " + x.Cleared + " against " + y.Cleared;
            if (x.Time != y.Time) return "time of the depth frame";
            if (x.MapTrusted != y.MapTrusted) return "map trusted";

            if (a.Level != b.Level) return "level: " + a.Level + " against " + b.Level;
            if (a.RawLevel != b.RawLevel) return "raw level: " + a.RawLevel + " against " + b.RawLevel;
            if (!SameBits(a.Direction.x, b.Direction.x) || !SameBits(a.Direction.y, b.Direction.y)) return "direction: " + a.Direction + " against " + b.Direction;
            if (!SameBits(a.Now, b.Now)) return "clear(head): " + a.Now.ToString("R") + " against " + b.Now.ToString("R");
            if (!SameBits(a.Path04, b.Path04)) return "path(0.4 s): " + a.Path04.ToString("R") + " against " + b.Path04.ToString("R");
            if (!SameBits(a.Path08, b.Path08)) return "path(0.8 s): " + a.Path08.ToString("R") + " against " + b.Path08.ToString("R");
            if (!SameBits(a.Speed, b.Speed)) return "speed: " + a.Speed.ToString("R") + " against " + b.Speed.ToString("R");
            if (a.RescanAdvised != b.RescanAdvised) return "rescan advised";
            BumpDiagnostics p = a.Diagnostics, q = b.Diagnostics;
            if (!SameBits(p.RescanMedian, q.RescanMedian)) return "rescan median: " + p.RescanMedian + " against " + q.RescanMedian;
            if (p.RisingEdges != q.RisingEdges) return "rising edges: " + p.RisingEdges + " against " + q.RisingEdges;
            return null;
        }

        /// <summary>
        /// What differs between the dynamic layers of the two cores, or null when nothing does: every cell's
        /// stored height and seen-empty state, the last hit of every occupied cell and whether it is pending, and
        /// the block counters.
        /// (The time left in an empty cell by its last tenant is not an output, and is not compared.)
        /// </summary>
        public static string LayerDifference(BumpGuardCore a, BumpGuardCore b)
        {
            DynamicGrid x = a.Dynamic, y = b.Dynamic;
            if (x.IsCreated != y.IsCreated) return "one core has no room";
            if (!x.IsCreated) return null;
            if (*x.ActiveCount != *y.ActiveCount) return "occupied cells: " + *x.ActiveCount + " against " + *y.ActiveCount;
            int cells = x.Spec.Width * x.Spec.Height;
            for (int k = 0; k < cells; k++)
            {
                if (!SameBits(x.TopHeight[k], y.TopHeight[k]) || x.EmptyState[k] != y.EmptyState[k] ||
                    (x.TopHeight[k] > 0f && (x.LastHit[k] != y.LastHit[k] || x.Pending[k] != y.Pending[k])))
                    return "dynamic cell " + k + " (" + (k % x.Spec.Width) + ", " + (k / x.Spec.Width) + ")";
            }
            int blocks = x.BlocksX * x.BlocksZ;
            for (int k = 0; k < blocks; k++)
                if (x.BlockCount[k] != y.BlockCount[k]) return "block counter " + k;
            return null;
        }

        /// <summary>Fails unless the last frame and the dynamic layer of the two cores are bit for bit the same.</summary>
        public static void Same(BumpGuardCore a, BumpGuardCore b, string scene, int frame)
        {
            string difference = FrameDifference(a, b) ?? LayerDifference(a, b);
            if (difference != null) Assert.Fail(scene + ", frame " + frame + ": " + difference);
        }
    }
}
