using System.Collections.Generic;
using Atlas.Spaces.Portal;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>What <see cref="RoomsFitLint"/> found for one RoomModel.</summary>
    public sealed class RoomsFitReport
    {
        public string RoomId;
        /// <summary>Layouts generated and checked.</summary>
        public int Layouts;
        /// <summary>Layouts whose every footprint lies inside the eroded free floor.</summary>
        public int Fitting;
        /// <summary>Layouts that came out as the real room alone: nothing to walk, so the exhibit cannot run in the room.</summary>
        public int RealRoomOnly;
        public readonly List<string> Failures = new List<string>(4);

        public float FitPercent { get { return Layouts == 0 ? 0f : 100f * Fitting / Layouts; } }
        /// <summary>The acceptance of P2-C15: 100% of the layouts fit the floor.</summary>
        public bool AllFit { get { return Layouts > 0 && Fitting == Layouts; } }
    }

    /// <summary>
    /// The physical-fit check of the Impossible Rooms exhibit (Master Plan task P2-C15, "every footprint inside
    /// the eroded floor"): every layout the exhibit can ask for, in every variant the seed can pick, is
    /// generated for a RoomModel and each of its footprints is probed against the eroded free floor on a
    /// finer grid than the generator's own check. Engine-free.
    /// </summary>
    public static class RoomsFitLint
    {
        /// <summary>Spacing of the probes on every cell, rim included. Metres. The generator probes at 0.25 m.</summary>
        public const float ProbeStep = 0.1f;
        /// <summary>Seeds beyond the room's own: they pick the booth's four corners and the loop's two entrance legs.</summary>
        public const int ExtraSeeds = 4;

        const float Slack = 1e-3f;

        public static RoomsFitReport Check(RoomModel room)
        {
            var report = new RoomsFitReport { RoomId = room != null ? room.Id : null };
            if (room == null)
            {
                report.Failures.Add("no RoomModel");
                return report;
            }
            var floor = new RoomModelFloor(room);
            uint roomSeed = PortalSpaceCore.SeedOf(room);
            foreach (PortalLayoutKind kind in new[] { PortalLayoutKind.BoothHall, PortalLayoutKind.Loop })
            {
                for (int rooms = PortalLayoutGenerator.MinLoopRooms; rooms <= PortalLayoutGenerator.MaxLoopRooms; rooms++)
                {
                    // The booth ignores the room count, so it is checked once.
                    if (kind == PortalLayoutKind.BoothHall && rooms != PortalLayoutGenerator.MinLoopRooms) continue;
                    for (int s = 0; s <= ExtraSeeds; s++)
                    {
                        uint seed = s == 0 ? roomSeed : (uint)(s - 1);
                        PortalLayoutRequest request = PortalLayoutRequest.FromRoom(room, kind, rooms, seed);
                        PortalLayout layout = PortalLayoutGenerator.Generate(in request);
                        report.Layouts++;
                        if (layout.Kind == PortalLayoutKind.RealRoomOnly) report.RealRoomOnly++;
                        string what = layout.Kind + " (asked " + kind + ", " + rooms + " rooms, seed " + seed + ")";
                        if (FootprintsInside(layout, room, floor, out string why)) report.Fitting++;
                        else report.Failures.Add(what + ": " + why);
                    }
                }
            }
            return report;
        }

        /// <summary>
        /// True when every cell of every room, and the booth, lies inside the free rectangle and on the eroded
        /// floor at every probe. The reason is set when it does not.
        /// </summary>
        public static bool FootprintsInside(PortalLayout layout, RoomModel room, IPortalFloor floor, out string why)
        {
            why = null;
            var rect = new FloorRect(new float2(-0.5f * room.FreeRect.Wf, -0.5f * room.FreeRect.Df),
                                     new float2(0.5f * room.FreeRect.Wf, 0.5f * room.FreeRect.Df));
            if (math.abs(layout.Wf - room.FreeRect.Wf) > 1e-4f || math.abs(layout.Df - room.FreeRect.Df) > 1e-4f)
            {
                why = "the layout was made for another free rectangle";
                return false;
            }
            for (int r = 0; r < layout.Rooms.Count; r++)
            {
                FloorRect[] cells = layout.Rooms[r].Cells;
                for (int c = 0; c < cells.Length; c++)
                    if (!CellInside(cells[c], rect, floor, out why)) { why = "room " + r + " cell " + c + " " + why; return false; }
            }
            if (layout.Kind == PortalLayoutKind.BoothHall && !CellInside(layout.Booth, rect, floor, out why))
            {
                why = "booth " + why;
                return false;
            }
            return true;
        }

        static bool CellInside(FloorRect cell, FloorRect rect, IPortalFloor floor, out string why)
        {
            why = null;
            if (!rect.Contains(cell.Min, Slack) || !rect.Contains(cell.Max, Slack))
            {
                why = "leaves the free rectangle";
                return false;
            }
            float2 size = cell.Size;
            if (size.x <= 2f * Slack || size.y <= 2f * Slack)
            {
                why = "has no area";
                return false;
            }
            int nx = math.max(1, (int)math.ceil(size.x / ProbeStep)), nz = math.max(1, (int)math.ceil(size.y / ProbeStep));
            float2 low = cell.Min + Slack, span = size - 2f * Slack;
            for (int j = 0; j <= nz; j++)
            {
                for (int i = 0; i <= nx; i++)
                {
                    float2 p = low + span * new float2((float)i / nx, (float)j / nz);
                    if (floor.Contains(p))
                        continue;
                    why = "leaves the eroded floor at (" + p.x.ToString("0.00") + ", " + p.y.ToString("0.00") + ")";
                    return false;
                }
            }
            return true;
        }
    }
}
