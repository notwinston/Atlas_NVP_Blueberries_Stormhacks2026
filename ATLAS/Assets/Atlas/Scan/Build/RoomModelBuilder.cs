using System;
using Atlas.Scan.Grid;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>Why a scan produced no room, or why a correction was refused.</summary>
    public enum ScanFailure : byte { None = 0, NoFloor = 1, TooSmall = 2, OutlineInvalid = 3, TrackingLost = 4, Cancelled = 5 }

    /// <summary>
    /// From a FloorScan grid result to the RoomModel every space reads (Master Plan sections 4.3 and 4.4, task
    /// P1-C06). Engine-free: it runs on the scan's worker thread and in headless tests.
    /// </summary>
    public static class RoomModelBuilder
    {
        /// <summary>A free rectangle with a shorter side than this is no room at all, metres.</summary>
        public const float MinFreeSide = 0.5f;

        // How far the floor of an outline-only room is laid beyond the outline's bounding box. The outline
        // cuts it back; the margin only has to cover the grid's own five cells.
        const float FloorMargin = 1.0f;
        // The largest yaw written to a RoomModel, either way round: pi cut, not rounded, at six decimals.
        const float MaxStoredYaw = 3.141592f;

        /// <summary>
        /// grid.Status must be Ok and the grid must carry an outline. Fills every field: the anchor from
        /// AnchorPosition and AnchorYaw (Id empty until the ARAnchor exists), outline, distance field,
        /// walkable polygon, free rectangle, loop-fit and cell grids, objects (the 64 largest boxes, ids by
        /// decreasing volume, state Placeholder, triangle targets) and quality. Walls: one per outline edge
        /// at wallHeight with no opening, unless a list is given. With a snapshot that holds a scanned mesh
        /// (ScanSnapshot.HasScannedMesh; the frame the grid was made from) every object also gets its profile
        /// and, unless decideFacing is false, its front (Objects).
        /// </summary>
        public static RoomModel Build(FloorGridResult grid, string id, string createdUtc, ScanRoute route,
            float wallHeight = RoomModel.DefaultWallHeight, RoomWall[] walls = null, ScanSnapshot scan = null, bool decideFacing = true)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (grid.Status != FloorGridStatus.Ok)
                throw new ArgumentException("The grid holds no free rectangle: " + grid.Status, nameof(grid));
            if (grid.Outline == null || grid.Outline.Length < 3)
                throw new ArgumentException("A RoomModel needs the confirmed outline.", nameof(grid));

            var room = new RoomModel
            {
                Id = id ?? "",
                CreatedUtc = createdUtc ?? "",
                Anchor = new RoomAnchor { Id = "", Position = grid.AnchorPosition, Yaw = grid.AnchorYaw },
                Outline = CounterClockwise(grid.Outline),
                Sdf = new RoomSdf(grid.Width, grid.Height, grid.OriginX, grid.OriginZ, Quantise(grid.Sdf)),
                FreeRect = new FreeRect(Round6(grid.Wf), Round6(grid.Df)),
                Quality = new RoomQuality { UnknownShare = Round6(grid.UnknownShare), Route = route },
            };
            room.Walkable.Outer = Round6(grid.WalkableOuter);
            var holes = new float2[grid.WalkableHoles != null ? grid.WalkableHoles.Length : 0][];
            for (int k = 0; k < holes.Length; k++) holes[k] = Round6(grid.WalkableHoles[k]);
            room.Walkable.Holes = holes;
            room.ApplyLoopFit();
            room.Walls = walls ?? WallsFromOutline(room.Outline, wallHeight);
            room.Objects = Objects(grid, room.Sdf, scan, decideFacing);
            return room;
        }

        /// <summary>
        /// Route 1 and synthetic rooms: an outline, boxes and the walker's position in one flat frame with
        /// the floor at y = 0. FloorScanCore.Run with the whole outline as floor, then Build. Null when the
        /// outline holds no walkable floor. The reference is tools/rooms/roommath.py build_room_model.
        /// </summary>
        public static RoomModel FromOutline(string id, float2[] outline, ObstacleBox[] boxes, float2 walker,
            float wallHeight = RoomModel.DefaultWallHeight, ScanRoute route = ScanRoute.ManualOutline)
        {
            FloorGridResult grid = FloorScanCore.Run(OutlineInput(outline, boxes, walker, 0f));
            if (grid.Status != FloorGridStatus.Ok || grid.Wf < MinFreeSide) return null;
            return Build(grid, id, "", route, wallHeight);
        }

        /// <summary>
        /// What FloorScan reads for an outline with nothing scanned inside it: one sheet of floor at
        /// floorHeight that covers the outline, so every cell inside is floor (contract rule G4).
        /// </summary>
        public static FloorGridInput OutlineInput(float2[] outline, ObstacleBox[] boxes, float2 walker, float floorHeight)
        {
            if (outline == null || outline.Length < 3) throw new ArgumentException("An outline has at least 3 points.", nameof(outline));
            float2 lo = outline[0], hi = outline[0];
            for (int k = 1; k < outline.Length; k++)
            {
                lo = math.min(lo, outline[k]);
                hi = math.max(hi, outline[k]);
            }
            lo -= FloorMargin;
            hi += FloorMargin;
            return new FloorGridInput
            {
                Vertices = new[]
                {
                    new float3(lo.x, floorHeight, lo.y), new float3(hi.x, floorHeight, lo.y),
                    new float3(hi.x, floorHeight, hi.y), new float3(lo.x, floorHeight, hi.y),
                },
                Indices = new[] { 0, 1, 2, 0, 2, 3 },
                TriangleClasses = new[] { SurfaceClass.Floor, SurfaceClass.Floor },
                FloorHeight = floorHeight,
                Walker = walker,
                Outline = outline,
                ExternalBoxes = boxes != null && boxes.Length > 0 ? boxes : null,
            };
        }

        /// <summary>One wall per edge of a counter-clockwise outline, in outline order, with no opening.</summary>
        public static RoomWall[] WallsFromOutline(float2[] outline, float height)
        {
            if (outline == null) throw new ArgumentNullException(nameof(outline));
            float h = Round6(math.clamp(height, 0.5f, 6f));
            var walls = new RoomWall[outline.Length];
            for (int k = 0; k < walls.Length; k++)
                walls[k] = new RoomWall { A = outline[k], B = outline[(k + 1) % outline.Length], Height = h };
            return walls;
        }

        /// <summary>
        /// The objects of a room from the grid's boxes (room frame): boxes that are not usable are dropped,
        /// the 64 largest by volume are kept and numbered 1, 2, ... by decreasing volume, each starts as a
        /// placeholder, and the thirty-two largest share the 120,000 triangle budget by surface area (mesh
        /// design section 5.4). No profile, and every box keeps its own axis.
        /// </summary>
        public static RoomObject[] Objects(FloorGridResult grid)
        {
            return Objects(grid, null, null, false);
        }

        /// <summary>
        /// The objects of a room, with what the scanned mesh says about them (mesh design section 5.2). When
        /// the snapshot holds a scanned mesh, in the frame the grid was made from, every object gets its
        /// profile (none when no ray hit anything) and, with decideFacing, its front: the box is turned by
        /// whole quarter turns so that local +Z is the front, size.w and size.d swapped on an odd turn, the
        /// profile turned with it, and the rule that answered is kept in Facing. sdf is the room's distance
        /// field. With no scanned mesh: no profile, Facing stays Box, nothing is turned.
        /// The boxes are turned before they are ordered, so the ids, the 64 that are kept and the triangle
        /// targets all come from the final boxes. A turn keeps the footprint, so the grid, which was made
        /// from the boxes as scanned, still has no free floor under any of them.
        /// </summary>
        public static RoomObject[] Objects(FloorGridResult grid, RoomSdf sdf, ScanSnapshot scan, bool decideFacing = true)
        {
            ObstacleBox[] boxes = grid.Boxes ?? Array.Empty<ObstacleBox>();
            var kept = new RoomObject[boxes.Length];
            int count = 0;
            for (int k = 0; k < boxes.Length; k++)
            {
                RoomObject obj;
                if (!TryObject(boxes[k], out obj)) continue;
                if (grid.CellIndex(obj.Base.xz) < 0) continue;      // its base lies beyond the grid: not in this room
                kept[count++] = obj;
            }
            if (count > 0 && scan != null && scan.HasScannedMesh)
            {
                ProfileObjects(kept, count, scan, grid.AnchorPosition, grid.AnchorYaw);
                if (decideFacing) FaceObjects(kept, count, sdf);
            }
            // Largest first; equal volumes keep the order the scan gave them.
            var order = new int[count];
            for (int k = 0; k < count; k++) order[k] = k;
            Array.Sort(order, (a, b) =>
            {
                int byVolume = kept[b].Volume.CompareTo(kept[a].Volume);
                return byVolume != 0 ? byVolume : a.CompareTo(b);
            });
            int n = Math.Min(count, RoomModel.MaxObjects);
            var objects = new RoomObject[n];
            for (int k = 0; k < n; k++)
            {
                objects[k] = kept[order[k]];
                objects[k].Id = k + 1;
            }
            ObjectMeshRules.Assign(objects);
            return objects;
        }

        // The profile of each of the first count objects: the mesh is in the session frame, where the room
        // frame sits at anchorPosition turned by anchorYaw. A box in which no ray hit anything has none.
        static void ProfileObjects(RoomObject[] objects, int count, ScanSnapshot scan, float3 anchorPosition, float anchorYaw)
        {
            var boxes = new PhotoBox[count];
            for (int k = 0; k < count; k++) boxes[k] = PhotoScore.ToCaptureFrame(objects[k], anchorPosition, anchorYaw);
            byte[][] profiles = ObjectProfile.Compute(scan.Vertices, scan.Indices, scan.Classes, boxes);
            for (int k = 0; k < count; k++)
                if (ObjectProfile.Coverage(profiles[k]) > 0f) objects[k].Profile = profiles[k];
        }

        // The front of each of the first count objects. A table keeps its axis, so the order in which the
        // objects are turned does not change which way a chair finds its table.
        static void FaceObjects(RoomObject[] objects, int count, RoomSdf sdf)
        {
            for (int k = 0; k < count; k++)
            {
                RoomObject obj = objects[k];
                ObjectFacingRule rule;
                int turns = ObjectFacing.Decide(obj.Category, obj.Base, obj.Size, obj.Yaw, obj.Profile, sdf, objects, out rule);
                ObjectFacing.Apply(turns, ref obj.Yaw, ref obj.Size, ref obj.Profile);
                obj.Yaw = StoredYaw(obj.Yaw);
                obj.Facing = rule;
            }
        }

        static bool TryObject(in ObstacleBox box, out RoomObject obj)
        {
            obj = null;
            float3 size = box.Size, bottom = box.Base;
            if (!math.all(math.isfinite(size)) || !math.all(math.isfinite(bottom)) || !math.isfinite(box.Yaw)) return false;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return false;
            // A box that dips below the floor is cut at the floor rather than sunk into it.
            if (bottom.y < 0f)
            {
                size.y += bottom.y;
                bottom.y = 0f;
                if (size.y <= 0f) return false;
            }
            size = math.clamp(size, 0.02f, 6f);
            int category = box.Category;
            if (category < 0 || category >= ObjectCategories.Count) category = (int)ObjectCategory.Unknown;
            obj = new RoomObject
            {
                Category = (ObjectCategory)category,
                Base = new float3(Round6(bottom.x), Round6(bottom.y), Round6(bottom.z)),
                Size = new float3(Round6(size.x), Round6(size.y), Round6(size.z)),
                Yaw = StoredYaw(box.Yaw),
                Mesh = MeshState.Placeholder,
                AssetHash = "",
            };
            return true;
        }

        /// <summary>The ring with a positive signed area: reversed when it was given clockwise. Values at six decimals.</summary>
        static float2[] CounterClockwise(float2[] ring)
        {
            float2[] copy = Round6(ring);
            if (WalkablePolygon.SignedArea(copy) < 0f) Array.Reverse(copy);
            return copy;
        }

        // The field as a RoomModel file holds it: whole millimetres. A room built here then equals the same
        // room read back from its file.
        static float[] Quantise(float[] metres)
        {
            var field = new float[metres.Length];
            for (int k = 0; k < field.Length; k++)
            {
                double mm = Math.Floor((double)metres[k] * 1000.0 + 0.5);
                if (mm == 0.0) mm = metres[k] > 0f ? 1.0 : -1.0;      // the sign is "free or not" and is never lost
                mm = mm > 32767.0 ? 32767.0 : mm < -32767.0 ? -32767.0 : mm;
                field[k] = (float)(mm / 1000.0);
            }
            return field;
        }

        static float Round6(float value)
        {
            return (float)Math.Round((double)value, 6);
        }

        static float2[] Round6(float2[] ring)
        {
            if (ring == null) return Array.Empty<float2>();
            var copy = new float2[ring.Length];
            for (int k = 0; k < copy.Length; k++) copy[k] = new float2(Round6(ring[k].x), Round6(ring[k].y));
            return copy;
        }

        /// <summary>
        /// A yaw as a RoomModel holds it: wrapped into (-pi, pi], at six decimals, and never beyond 3.141592.
        /// Pi itself rounds to 3.141593 at six decimals, which is past what a reader of the file accepts
        /// (RoomModelJson stops at 3.1415928), so half a turn is stored as 3.141592: under a micro-radian off.
        /// </summary>
        internal static float StoredYaw(float yaw)
        {
            return math.clamp(Round6(WrapAngle(yaw)), -MaxStoredYaw, MaxStoredYaw);
        }

        // Into (-pi, pi].
        static float WrapAngle(float a)
        {
            double w = Math.IEEERemainder(a, 2.0 * Math.PI);
            if (w <= -Math.PI) w += 2.0 * Math.PI;
            return (float)w;
        }
    }
}
