using System;
using System.Collections.Generic;
using Atlas.Scan.Grid;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>One furniture box as the AR provider reports it: a pose at the centre of the box, in the AR session frame.</summary>
    public struct ScannedBox
    {
        /// <summary>Centre of the box, metres.</summary>
        public float3 Centre;
        public quaternion Rotation;
        /// <summary>Full size along the box's own X, Y and Z, metres.</summary>
        public float3 Size;
        /// <summary>The bits of AR Foundation's BoundingBoxClassifications.</summary>
        public uint ClassBits;
        /// <summary>A number that tells the boxes of one scan apart.</summary>
        public int SourceId;
    }

    /// <summary>
    /// Scan route 3 (Master Plan section 4.2, task P1-C04): RoomPlan's furniture boxes brought into the form
    /// FloorScan reads, so that they and the obstacle blobs of the grid end up as one list in one frame
    /// (contract rule G13: supplied boxes first, then one box per blob no supplied box explains).
    /// Engine-free.
    /// </summary>
    public static class ObjectBoxMerge
    {
        /// <summary>Two boxes whose centres lie this close and whose sizes agree are one piece of furniture, metres.</summary>
        public const float SameBoxDistance = 0.15f;
        /// <summary>Sizes agree when no side differs by more, metres.</summary>
        public const float SameBoxSize = 0.20f;
        /// <summary>A thinner or larger box is a tracking artefact, metres.</summary>
        public const float MinSide = 0.02f, MaxSide = 6f;

        /// <summary>
        /// A provider box as an upright obstacle box in the same frame: the base is the centre of its
        /// underside, the yaw is the heading of its forward axis, the category is the contract's
        /// (ObjectCategories.FromBoundingBoxBits). False for a box with a side that is not finite or not
        /// between 0.02 and 6 m.
        /// </summary>
        public static bool TryConvert(in ScannedBox box, out ObstacleBox result)
        {
            result = default(ObstacleBox);
            if (!math.all(math.isfinite(box.Centre)) || !math.all(math.isfinite(box.Size)) ||
                !math.all(math.isfinite(box.Rotation.value))) return false;
            if (math.cmin(box.Size) < MinSide || math.cmax(box.Size) > MaxSide) return false;
            result = new ObstacleBox
            {
                Base = new float3(box.Centre.x, box.Centre.y - 0.5f * box.Size.y, box.Centre.z),
                Size = box.Size,
                Yaw = RoomFrame.Yaw(box.Rotation),
                Category = (int)ObjectCategories.FromBoundingBoxBits(box.ClassBits),
                SourceId = box.SourceId,
                FromScan = false,
            };
            return true;
        }

        /// <summary>
        /// Every usable box of a scan, converted, with repeats of the same piece of furniture removed (the
        /// larger one stays). The order of the input is kept.
        /// </summary>
        public static ObstacleBox[] Merge(ScannedBox[] boxes, int count)
        {
            var merged = new List<ObstacleBox>(count);
            for (int k = 0; k < count; k++)
            {
                ObstacleBox box;
                if (!TryConvert(in boxes[k], out box)) continue;
                int twin = -1;
                for (int m = 0; m < merged.Count && twin < 0; m++)
                    if (SameFurniture(merged[m], box)) twin = m;
                if (twin < 0) merged.Add(box);
                else if (Volume(box) > Volume(merged[twin]))
                {
                    // The larger report wins, but a category is never lost to an unclassified repeat.
                    if (box.Category == 0) box.Category = merged[twin].Category;
                    merged[twin] = box;
                }
                else if (merged[twin].Category == 0 && box.Category != 0)
                {
                    ObstacleBox kept = merged[twin];
                    kept.Category = box.Category;
                    merged[twin] = kept;
                }
            }
            return merged.ToArray();
        }

        /// <summary>How many boxes of a list came from the provider and how many from the grid's own blobs.</summary>
        public static void Count(ObstacleBox[] boxes, out int supplied, out int fromScan)
        {
            supplied = fromScan = 0;
            if (boxes == null) return;
            for (int k = 0; k < boxes.Length; k++)
            {
                if (boxes[k].FromScan) fromScan++;
                else supplied++;
            }
        }

        static bool SameFurniture(in ObstacleBox a, in ObstacleBox b)
        {
            float3 ca = a.Base + new float3(0f, 0.5f * a.Size.y, 0f), cb = b.Base + new float3(0f, 0.5f * b.Size.y, 0f);
            if (math.distance(ca, cb) > SameBoxDistance) return false;
            // The same box may be reported a quarter turn apart with width and depth exchanged.
            float3 sa = a.Size, sb = b.Size;
            bool straight = math.abs(sa.x - sb.x) <= SameBoxSize && math.abs(sa.z - sb.z) <= SameBoxSize;
            bool turned = math.abs(sa.x - sb.z) <= SameBoxSize && math.abs(sa.z - sb.x) <= SameBoxSize;
            return math.abs(sa.y - sb.y) <= SameBoxSize && (straight || turned);
        }

        static float Volume(in ObstacleBox box)
        {
            return box.Size.x * box.Size.y * box.Size.z;
        }
    }
}
