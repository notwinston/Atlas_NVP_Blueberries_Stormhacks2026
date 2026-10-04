using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// Which side of an object's box is its front (mesh design section 5.2 and Appendix B). Decided once, when
    /// the RoomModel is built, by the first rule in the category's list that gives an answer: the profile, the
    /// nearest table, the free floor, and last the box's own axis. The answer is a number of quarter turns k
    /// to add to the yaw, after which local +Z is the front: the front was found on old +Z for k = 0, old +X
    /// for 1, old -Z for 2, old -X for 3. Everything is in the room frame. Engine-free.
    /// </summary>
    public static class ObjectFacing
    {
        /// <summary>A profile that covers less of the box is not used.</summary>
        public const float MinCoverage = 0.30f;
        /// <summary>Rows of the profile in the edge strip of a side: the outer 3 of 16.</summary>
        public const int StripRows = 3;
        /// <summary>Share of the back strip's cells that must be seen.</summary>
        public const float MinStripSeen = 0.25f;
        /// <summary>The back strip's mean height must exceed the next side's by this, as a fraction of the box height.</summary>
        public const float ProfileMargin = 0.15f;
        /// <summary>A table counts when its box lies within this of the object's box on the floor, metres.</summary>
        public const float TableReach = 1.0f;
        /// <summary>The distance field is read this far outside the middle of each side, metres.</summary>
        public const float ClearanceProbe = 0.30f;
        /// <summary>The front's reading must exceed the opposite side's, which is blocked and counts as 0, by this, metres.</summary>
        public const float ClearanceMargin = 0.10f;
        /// <summary>A reading within half a cell of zero is neither free floor nor blocked floor, metres.</summary>
        public const float ClearanceSlack = 0.5f * RoomSdf.CellSize;

        /// <summary>
        /// The quarter turns that bring the front of an object onto local +Z, and the rule that answered.
        /// The box is the centre of its base, (w, h, d) and its yaw in the room frame; profile may be null;
        /// sdf is the room's distance field (null: the clearance rule gives no answer); objects are the room's
        /// objects, of which those of category Table are the tables (null: none). Rule Box always gives 0.
        /// </summary>
        public static int Decide(ObjectCategory category, float3 boxBase, float3 boxSize, float boxYaw, byte[] profile,
            RoomSdf sdf, RoomObject[] objects, out ObjectFacingRule rule)
        {
            int turns;
            if (Tries(category, ObjectFacingRule.Profile) && TryProfile(profile, out turns))
            {
                rule = ObjectFacingRule.Profile;
                return turns;
            }
            if (Tries(category, ObjectFacingRule.Table) && TryTable(boxBase, boxSize, boxYaw, objects, out turns))
            {
                rule = ObjectFacingRule.Table;
                return turns;
            }
            if (Tries(category, ObjectFacingRule.Clearance) && TryClearance(boxBase, boxSize, boxYaw, sdf, out turns))
            {
                rule = ObjectFacingRule.Clearance;
                return turns;
            }
            rule = ObjectFacingRule.Box;
            return 0;
        }

        /// <summary>
        /// True when the rule is in the category's list. The lists, each in the order profile, table,
        /// clearance, box: a chair tries all four; couch, bed and toilet leave out the table; storage, screen,
        /// refrigerator, oven, dishwasher, washerDryer, stove, fireplace, sink and bathtub try clearance and
        /// box; every other category keeps the box's own axis.
        /// </summary>
        public static bool Tries(ObjectCategory category, ObjectFacingRule rule)
        {
            if (rule == ObjectFacingRule.Box) return true;
            switch (category)
            {
                case ObjectCategory.Chair:
                    return true;
                case ObjectCategory.Couch:
                case ObjectCategory.Bed:
                case ObjectCategory.Toilet:
                    return rule != ObjectFacingRule.Table;
                case ObjectCategory.Storage:
                case ObjectCategory.Screen:
                case ObjectCategory.Refrigerator:
                case ObjectCategory.Oven:
                case ObjectCategory.Dishwasher:
                case ObjectCategory.WasherDryer:
                case ObjectCategory.Stove:
                case ObjectCategory.Fireplace:
                case ObjectCategory.Sink:
                case ObjectCategory.Bathtub:
                    return rule == ObjectFacingRule.Clearance;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Applies an answer: the yaw is turned by the quarter turns and wrapped to (-pi, pi], size.w and
        /// size.d are swapped on an odd turn, and the profile (null stays null) is turned with them. Nothing is
        /// touched for no turn.
        /// </summary>
        public static void Apply(int quarterTurns, ref float yaw, ref float3 size, ref byte[] profile)
        {
            int turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0) return;
            yaw = WrapAngle(yaw + turns * (0.5 * Math.PI));
            if ((turns & 1) != 0) size = new float3(size.z, size.y, size.x);
            if (profile != null) profile = ObjectProfile.Rotate(profile, turns);
        }

        /// <summary>
        /// The profile rule: the back is the side whose edge strip is highest on average, the front is opposite.
        /// The back strip of the candidate front +Z is rows j = 0..2, of +X columns i = 0..2, of -Z rows
        /// 13..15, of -X columns 13..15; a strip's mean is over its seen cells, as height / box height.
        /// Answers when the profile covers at least 0.30 of the box, the highest strip has at least a quarter
        /// of its cells seen, and its mean exceeds every other strip's by 0.15.
        /// </summary>
        public static bool TryProfile(byte[] profile, out int quarterTurns)
        {
            quarterTurns = 0;
            if (profile == null || profile.Length != ObjectProfile.Length) return false;
            if (ObjectProfile.Coverage(profile) < MinCoverage) return false;

            int best = 0;
            float bestMean = -1f, nextMean = -1f;
            int bestSeen = 0;
            for (int side = 0; side < 4; side++)
            {
                int seen;
                float mean = StripMean(profile, side, out seen);
                if (mean > bestMean)
                {
                    nextMean = bestMean;
                    bestMean = mean;
                    bestSeen = seen;
                    best = side;
                }
                else if (mean > nextMean) nextMean = mean;
            }
            if (bestSeen < MinStripSeen * (StripRows * ObjectProfile.Side)) return false;
            if (bestMean - nextMean < ProfileMargin) return false;
            quarterTurns = best;
            return true;
        }

        /// <summary>
        /// The mean height, as a fraction of the box height, of the seen cells in the back strip of a candidate
        /// front (0: +Z, 1: +X, 2: -Z, 3: -X), and how many of its 48 cells were seen. 0 when none was.
        /// </summary>
        public static float StripMean(byte[] profile, int side, out int seen)
        {
            const int n = ObjectProfile.Side;
            seen = 0;
            int sum = 0;
            for (int across = 0; across < StripRows; across++)
            {
                for (int along = 0; along < n; along++)
                {
                    int i, j;
                    switch (side & 3)
                    {
                        case 0: i = along; j = across; break;
                        case 1: i = across; j = along; break;
                        case 2: i = along; j = n - 1 - across; break;
                        default: i = n - 1 - across; j = along; break;
                    }
                    byte value = profile[ObjectProfile.Index(i, j)];
                    if (value == 0) continue;
                    seen++;
                    sum += value;
                }
            }
            return seen > 0 ? sum / (255f * seen) : 0f;
        }

        /// <summary>
        /// The table rule: the front is the side whose outward normal points most towards the centre of the
        /// nearest table. Answers when a table's box lies within 1.0 m of the object's box, measured between
        /// their footprints on the floor, and its centre is not the object's own.
        /// </summary>
        public static bool TryTable(float3 boxBase, float3 boxSize, float boxYaw, RoomObject[] objects, out int quarterTurns)
        {
            quarterTurns = 0;
            if (objects == null) return false;
            float nearest = float.MaxValue, nearestCentre = float.MaxValue;
            float2 towards = float2.zero;
            bool found = false;
            for (int k = 0; k < objects.Length; k++)
            {
                RoomObject table = objects[k];
                if (table == null || table.Category != ObjectCategory.Table) continue;
                float gap = FootprintDistance(boxBase.xz, boxSize, boxYaw, table.Base.xz, table.Size, table.Yaw);
                if (gap > TableReach) continue;
                float2 offset = table.Base.xz - boxBase.xz;
                float centre = math.length(offset);
                // The nearest box; of two equally near (both under the chair, say) the nearer centre.
                if (found && (gap > nearest || (gap == nearest && centre >= nearestCentre))) continue;
                found = true;
                nearest = gap;
                nearestCentre = centre;
                towards = offset;
            }
            if (!found || nearestCentre < 1e-4f) return false;

            float sin, cos;
            math.sincos(boxYaw, out sin, out cos);
            // The offset in the box's frame; the side normals are +Z, +X, -Z, -X.
            float lx = towards.x * cos - towards.y * sin, lz = towards.x * sin + towards.y * cos;
            quarterTurns = Highest(lz, lx, -lz, -lx);
            return true;
        }

        /// <summary>
        /// The clearance rule. The room's distance field is read 0.30 m outside the middle of each side. A side
        /// can be the front when the floor there is free and the floor outside the opposite side is blocked:
        /// the object stands with its back against a wall or another object. Answers when exactly one side can
        /// be the front and its reading is at least 0.10 m. In a room corner two sides can, the floor cannot
        /// tell which, and the rule gives no answer: the box keeps its axis.
        /// </summary>
        public static bool TryClearance(float3 boxBase, float3 boxSize, float boxYaw, RoomSdf sdf, out int quarterTurns)
        {
            quarterTurns = 0;
            if (sdf == null) return false;
            float sin, cos;
            math.sincos(boxYaw, out sin, out cos);
            var forward = new float2(sin, cos);       // local +Z
            var right = new float2(cos, -sin);        // local +X
            float2 centre = boxBase.xz;
            float alongZ = 0.5f * boxSize.z + ClearanceProbe, alongX = 0.5f * boxSize.x + ClearanceProbe;
            // The readings outside +Z, +X, -Z and -X, in the order of the quarter turns.
            var floor = new float4(sdf.Sample(centre + forward * alongZ), sdf.Sample(centre + right * alongX),
                sdf.Sample(centre - forward * alongZ), sdf.Sample(centre - right * alongX));

            int front = -1;
            for (int side = 0; side < 4; side++)
            {
                bool free = floor[side] > ClearanceSlack, backed = floor[(side + 2) & 3] < -ClearanceSlack;
                if (!free || !backed) continue;
                if (front >= 0) return false;       // a second side with free floor in front and none behind: it cannot tell
                front = side;
            }
            if (front < 0 || floor[front] < ClearanceMargin) return false;
            quarterTurns = front;
            return true;
        }

        /// <summary>
        /// Metres between two box footprints on the floor (centre, (w, h, d), yaw each); 0 when they touch or
        /// overlap.
        /// </summary>
        public static float FootprintDistance(float2 centreA, float3 sizeA, float yawA, float2 centreB, float3 sizeB, float yawB)
        {
            var a = new float2[4];
            var b = new float2[4];
            Corners(centreA, sizeA, yawA, a);
            Corners(centreB, sizeB, yawB, b);
            if (Overlap(a, b)) return 0f;
            // Two convex shapes that do not overlap are nearest at a corner of one and an edge of the other.
            float best = float.MaxValue;
            for (int p = 0; p < 4; p++)
            {
                for (int e = 0; e < 4; e++)
                {
                    best = math.min(best, PointToSegment(a[p], b[e], b[(e + 1) & 3]));
                    best = math.min(best, PointToSegment(b[p], a[e], a[(e + 1) & 3]));
                }
            }
            return best;
        }

        // The index of the highest of four values; the first of equals.
        static int Highest(float v0, float v1, float v2, float v3)
        {
            int best = 0;
            float value = v0;
            if (v1 > value) { best = 1; value = v1; }
            if (v2 > value) { best = 2; value = v2; }
            if (v3 > value) best = 3;
            return best;
        }

        static void Corners(float2 centre, float3 size, float yaw, float2[] into)
        {
            float sin, cos;
            math.sincos(yaw, out sin, out cos);
            float2 ux = new float2(cos, -sin) * (0.5f * size.x), uz = new float2(sin, cos) * (0.5f * size.z);
            into[0] = centre - ux - uz;
            into[1] = centre + ux - uz;
            into[2] = centre + ux + uz;
            into[3] = centre - ux + uz;
        }

        // Separating axes of two rectangles: the edge directions of each.
        static bool Overlap(float2[] a, float2[] b)
        {
            return !Separated(a, b, a[1] - a[0]) && !Separated(a, b, a[3] - a[0]) &&
                   !Separated(a, b, b[1] - b[0]) && !Separated(a, b, b[3] - b[0]);
        }

        static bool Separated(float2[] a, float2[] b, float2 axis)
        {
            float aLo = float.MaxValue, aHi = float.MinValue, bLo = float.MaxValue, bHi = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float pa = math.dot(a[k], axis), pb = math.dot(b[k], axis);
                aLo = math.min(aLo, pa);
                aHi = math.max(aHi, pa);
                bLo = math.min(bLo, pb);
                bHi = math.max(bHi, pb);
            }
            return aHi < bLo || bHi < aLo;
        }

        static float PointToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lengthSq = math.lengthsq(ab);
            float t = lengthSq > 0f ? math.saturate(math.dot(p - a, ab) / lengthSq) : 0f;
            return math.distance(p, a + t * ab);
        }

        // Into (-pi, pi].
        static float WrapAngle(double a)
        {
            double w = Math.IEEERemainder(a, 2.0 * Math.PI);
            if (w <= -Math.PI) w += 2.0 * Math.PI;
            return (float)w;
        }
    }
}
