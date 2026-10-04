using System;
using System.Collections.Generic;
using System.Globalization;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>What a merge of RoomPlan's walls into a room did.</summary>
    public struct WallBridgeReport
    {
        /// <summary>A capture was there to read. False when the bridge did not run or gave nothing.</summary>
        public bool Tried;
        /// <summary>Why the capture could not be read, or null.</summary>
        public string Error;
        /// <summary>Walls of the room that took their height and openings from RoomPlan.</summary>
        public int Merged;
        /// <summary>RoomPlan walls that lie on no wall of the room.</summary>
        public int Unmatched;
        /// <summary>Doors, windows and openings now in the room.</summary>
        public int Openings;
        /// <summary>Doors, windows and openings left out: on no merged wall, too small, or more than a wall holds.</summary>
        public int DroppedOpenings;
        /// <summary>Surfaces of the file that were not usable.</summary>
        public int Skipped;
        /// <summary>Largest distance of a matched RoomPlan wall from the room's wall, metres (task P1-C05 accepts 0.03).</summary>
        public float MaxOffset;
        /// <summary>Matched RoomPlan walls further than 0.03 m from the room's wall.</summary>
        public int OffLine;

        /// <summary>One line for the log.</summary>
        public string Text
        {
            get
            {
                if (!Tried) return "wall bridge: no capture";
                if (Error != null) return "wall bridge: capture not readable (" + Error + "), walls from the outline";
                if (Merged == 0) return "wall bridge: no RoomPlan wall lies on the outline (" + I(Unmatched) + " seen), walls from the outline";
                return "wall bridge: " + I(Merged) + " walls merged, " + I(Unmatched) + " unmatched, " + I(Openings) + " openings (" +
                       I(DroppedOpenings) + " dropped), largest wall offset " + MaxOffset.ToString("0.000", CultureInfo.InvariantCulture) +
                       " m, " + I(OffLine) + " beyond 0.03 m";
            }
        }

        static string I(int value) { return value.ToString(CultureInfo.InvariantCulture); }
    }

    /// <summary>
    /// Scan route 4 (Master Plan section 4.2, task P1-C05): the walls, doors and windows of a RoomPlan capture
    /// laid onto the walls a room already has. The room's walls stay where the confirmed outline puts them
    /// (RoomModel rule R14); a wall that a RoomPlan wall lies on takes that wall's height and the openings
    /// found in it. With no capture, or none that fits, the room is left exactly as it was. Engine-free.
    /// </summary>
    public static class WallBridgeMerge
    {
        /// <summary>A RoomPlan surface belongs to a wall it is parallel to within this, degrees.</summary>
        public const float MaxAngleDegrees = 10f;
        /// <summary>... and whose line its centre is no further from than this, metres.</summary>
        public const float MaxDistance = 0.3f;
        /// <summary>... and which it runs along for at least this, metres.</summary>
        public const float MinOverlap = 0.05f;
        /// <summary>The acceptance of task P1-C05: RoomPlan's wall within this of the mesh-derived one, metres. Reported, not enforced.</summary>
        public const float PositionTolerance = 0.03f;
        /// <summary>A narrower or lower opening is left out, metres (a RoomModel file takes none below 0.1).</summary>
        public const float MinOpening = 0.1f;
        /// <summary>A RoomModel file takes this many openings in one wall.</summary>
        public const int MaxOpeningsPerWall = 16;

        /// <summary>
        /// Parses a capture (WallBridgeJson) and merges it. Text that is empty gives Tried false; text that
        /// cannot be read gives Error. Either way the room is untouched.
        /// </summary>
        public static WallBridgeReport Merge(RoomModel room, string json)
        {
            var report = new WallBridgeReport();
            if (string.IsNullOrEmpty(json)) return report;
            report.Tried = true;
            BridgeSurface[] surfaces;
            int skipped;
            string error;
            if (!WallBridgeJson.TryParse(json, out surfaces, out skipped, out error))
            {
                report.Error = error ?? "unreadable";
                return report;
            }
            WallBridgeReport merged = Merge(room, surfaces);
            merged.Skipped = skipped;
            return merged;
        }

        /// <summary>
        /// surfaces are in the AR session frame; the room's anchor carries them to the room frame. When at
        /// least one RoomPlan wall lies on a wall of the room, room.Walls is replaced (same walls, same
        /// order, new heights and openings) and Quality.Route becomes WallBridge. Otherwise nothing changes.
        /// </summary>
        public static WallBridgeReport Merge(RoomModel room, BridgeSurface[] surfaces)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            var report = new WallBridgeReport { Tried = true };
            RoomWall[] walls = room.Walls;
            if (surfaces == null || surfaces.Length == 0 || walls.Length == 0) return report;

            // Session frame to room frame, as the pose pipeline places it (RoomFrame.SessionToRoom).
            float3 shift;
            quaternion turn;
            RoomFrame.SessionToRoom(room.Anchor.Position, quaternion.RotateY(room.Anchor.Yaw), out shift, out turn);

            var top = new float[walls.Length];
            var merged = new bool[walls.Length];
            var openings = new List<WallOpening>[walls.Length];

            // 1. Walls: each RoomPlan wall to the wall of the room it lies on.
            for (int s = 0; s < surfaces.Length; s++)
            {
                if (surfaces[s].Kind != BridgeSurfaceKind.Wall) continue;
                Placed placed = Place(in surfaces[s], shift, turn);
                float from, to, distance;
                int k = Match(walls, null, in placed, out from, out to, out distance);
                if (k < 0)
                {
                    report.Unmatched++;
                    continue;
                }
                merged[k] = true;
                top[k] = math.max(top[k], placed.Top);
                report.MaxOffset = math.max(report.MaxOffset, distance);
                if (distance > PositionTolerance) report.OffLine++;
            }
            for (int k = 0; k < walls.Length; k++) if (merged[k]) report.Merged++;
            if (report.Merged == 0) return report;

            // RoomPlan may have seen only the lower part of a wall: a height that is no ceiling's is not taken.
            var height = new float[walls.Length];
            for (int k = 0; k < walls.Length; k++)
            {
                bool plausible = merged[k] && top[k] >= ScanSolver.MinWallHeight && top[k] <= ScanSolver.MaxWallHeight;
                height[k] = plausible ? Round6(top[k]) : walls[k].Height;
            }

            // 2. Doors, windows and openings: each into the merged wall it lies in.
            for (int s = 0; s < surfaces.Length; s++)
            {
                if (surfaces[s].Kind == BridgeSurfaceKind.Wall) continue;
                Placed placed = Place(in surfaces[s], shift, turn);
                float from, to, distance;
                int k = Match(walls, merged, in placed, out from, out to, out distance);
                if (k < 0)
                {
                    report.DroppedOpenings++;
                    continue;
                }
                OpeningKind kind = surfaces[s].Kind == BridgeSurfaceKind.Door ? OpeningKind.Door
                    : surfaces[s].Kind == BridgeSurfaceKind.Window ? OpeningKind.Window : OpeningKind.Opening;
                var opening = new WallOpening
                {
                    Kind = kind, Offset = from, Width = to - from,
                    // A door starts at the floor (rule R15); nothing starts below it or ends above the wall.
                    Bottom = kind == OpeningKind.Door ? 0f : math.max(placed.Bottom, 0f),
                    Top = math.min(placed.Top, height[k]),
                };
                if (opening.Width < MinOpening || opening.Top - opening.Bottom < MinOpening)
                {
                    report.DroppedOpenings++;
                    continue;
                }
                if (openings[k] == null) openings[k] = new List<WallOpening>(4);
                openings[k].Add(opening);
            }

            // 3. The room's walls again, in the same order.
            var result = new RoomWall[walls.Length];
            for (int k = 0; k < walls.Length; k++)
            {
                result[k] = new RoomWall { A = walls[k].A, B = walls[k].B, Height = height[k], Openings = walls[k].Openings };
                if (!merged[k]) continue;
                result[k].Openings = Tidy(openings[k], walls[k].Length, ref report);
                report.Openings += result[k].Openings.Length;
            }
            room.Walls = result;
            room.Quality.Route = ScanRoute.WallBridge;
            return report;
        }

        // A surface in the room frame: the ends of its width on the floor and its edges above the floor.
        struct Placed
        {
            public float2 P, Q;
            public float Bottom, Top;
        }

        static Placed Place(in BridgeSurface surface, float3 shift, quaternion turn)
        {
            float3 centre = shift + math.mul(turn, surface.Centre);
            float3 along = math.mul(turn, new float3(surface.Along.x, 0f, surface.Along.y));
            float2 half = 0.5f * surface.Width * new float2(along.x, along.z);
            return new Placed
            {
                P = centre.xz - half, Q = centre.xz + half,
                Bottom = centre.y - 0.5f * surface.Height, Top = centre.y + 0.5f * surface.Height,
            };
        }

        // The wall a surface lies on: parallel, near, and overlapping; the nearest one when several are.
        // only limits the search to some walls. from and to are the surface's extent along that wall, from
        // its A, cut to the wall's length.
        static int Match(RoomWall[] walls, bool[] only, in Placed placed, out float from, out float to, out float distance)
        {
            from = to = 0f;
            distance = float.MaxValue;
            int best = -1;
            float minCos = math.cos(math.radians(MaxAngleDegrees));
            float2 mid = 0.5f * (placed.P + placed.Q);
            float2 run = placed.Q - placed.P;
            float runLength = math.length(run);
            if (!(runLength > 1e-6f)) return -1;
            for (int k = 0; k < walls.Length; k++)
            {
                if (only != null && !only[k]) continue;
                float length = walls[k].Length;
                if (!(length > 1e-6f)) continue;
                float2 e = (walls[k].B - walls[k].A) / length;
                if (math.abs(math.dot(run, e)) < minCos * runLength) continue;
                float2 toMid = mid - walls[k].A;
                float d = math.abs(toMid.x * e.y - toMid.y * e.x);
                if (d > MaxDistance || d >= distance) continue;
                float t0 = math.dot(placed.P - walls[k].A, e), t1 = math.dot(placed.Q - walls[k].A, e);
                float lo = math.max(math.min(t0, t1), 0f), hi = math.min(math.max(t0, t1), length);
                if (hi - lo < MinOverlap) continue;
                best = k;
                distance = d;
                from = lo;
                to = hi;
            }
            return best;
        }

        // The openings of one wall as a RoomModel holds them: in order along the wall, none running into the
        // next (the later one gives way), none past the end, at most 16, values at six decimals.
        static WallOpening[] Tidy(List<WallOpening> found, float length, ref WallBridgeReport report)
        {
            if (found == null || found.Count == 0) return Array.Empty<WallOpening>();
            found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            var kept = new List<WallOpening>(found.Count);
            float end = 0f;
            for (int m = 0; m < found.Count; m++)
            {
                WallOpening o = found[m];
                float stop = math.min(o.Offset + o.Width, length);
                o.Offset = Round6(math.max(o.Offset, end));
                o.Width = Round6(stop - o.Offset);
                if (o.Offset + o.Width > length) o.Width = length - o.Offset;
                if (o.Width < MinOpening || kept.Count >= MaxOpeningsPerWall)
                {
                    report.DroppedOpenings++;
                    continue;
                }
                o.Bottom = Round6(o.Bottom);
                o.Top = Round6(o.Top);
                kept.Add(o);
                end = o.Offset + o.Width;
            }
            return kept.ToArray();
        }

        static float Round6(float value)
        {
            return (float)Math.Round((double)value, 6);
        }
    }
}
