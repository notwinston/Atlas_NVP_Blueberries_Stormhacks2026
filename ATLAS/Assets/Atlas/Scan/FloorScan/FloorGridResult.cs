using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan.Grid
{
    /// <summary>
    /// What FloorScan found, expressed in the room frame (contract F4): the origin is the centre of the free
    /// rectangle on the floor, +X runs along its shorter side Wf, +Z along Df, +Y up. Floor points are (x, z).
    /// </summary>
    public sealed class FloorGridResult
    {
        /// <summary>How the scan ended. Unless it is <see cref="FloorGridStatus.Ok"/> there is no free rectangle.</summary>
        public FloorGridStatus Status;

        /// <summary>Floor height y0 in the input frame, metres.</summary>
        public float FloorHeight;
        /// <summary>Position of the room frame's origin (the anchor) in the input frame.</summary>
        public float3 AnchorPosition;
        /// <summary>Rotation of the room frame about +Y in the input frame, radians, Unity sense, in (-pi/2, pi/2].</summary>
        public float AnchorYaw;
        /// <summary>Dominant wall direction in the input frame, radians in (-pi/4, pi/4], measured from +X towards +Z.</summary>
        public float WallDirection;

        /// <summary>Grid cell, metres.</summary>
        public float CellSize;
        /// <summary>Cells along X.</summary>
        public int Width;
        /// <summary>Cells along Z.</summary>
        public int Height;
        /// <summary>Room-frame x of the low edge of column 0.</summary>
        public float OriginX;
        /// <summary>Room-frame z of the low edge of row 0.</summary>
        public float OriginZ;
        /// <summary>Per cell, row-major with row 0 at the lowest z: index = j * Width + i.</summary>
        public FloorCell[] Cells;
        /// <summary>
        /// Signed distance to the edge of the free floor at each cell centre, metres, positive inside. It reads
        /// 0.05 m less than the true distance to a wall or obstacle.
        /// </summary>
        public float[] Sdf;

        /// <summary>Shorter side of the free rectangle, along room +X, metres.</summary>
        public float Wf;
        /// <summary>Longer side of the free rectangle, along room +Z, metres.</summary>
        public float Df;
        /// <summary>Lap square and curvature radii from the loop-fit rule.</summary>
        public LoopFitResult Fit;
        /// <summary>Glued cells (S1).</summary>
        public GluedGrid Glued;
        /// <summary>Aligned Cells grid (S3).</summary>
        public AlignedGrid Aligned;
        /// <summary>True when the free rectangle is smaller than 1.5 x 1.5 m.</summary>
        public bool Standing;

        /// <summary>The confirmed outline in the room frame, in the order it was given; null when none was given.</summary>
        public float2[] Outline;
        /// <summary>Outer ring of the walkable region, counter-clockwise from its lowest point.</summary>
        public float2[] WalkableOuter;
        /// <summary>Holes of the walkable region, clockwise, ordered by their lowest point.</summary>
        public float2[][] WalkableHoles;
        /// <summary>Supplied boxes first, in the order given, then the boxes of obstacle blobs no supplied box explains.</summary>
        public ObstacleBox[] Boxes;
        /// <summary>Share of cells inside the outline that were neither seen as floor nor as an obstacle.</summary>
        public float UnknownShare;

        // Input frame to room frame: room = M * (p - anchor).
        internal double M00, M01, M10, M11, AnchorX, AnchorZ;

        /// <summary>A floor point of the input frame, in the room frame.</summary>
        public float2 ToRoom(float2 inputXZ)
        {
            double dx = inputXZ.x - AnchorX, dz = inputXZ.y - AnchorZ;
            return new float2((float)(M00 * dx + M01 * dz), (float)(M10 * dx + M11 * dz));
        }

        /// <summary>A point of the input frame, in the room frame (y becomes the height above the floor).</summary>
        public float3 ToRoom(float3 input)
        {
            float2 xz = ToRoom(new float2(input.x, input.z));
            return new float3(xz.x, input.y - FloorHeight, xz.y);
        }

        /// <summary>A floor point of the room frame, in the input frame.</summary>
        public float2 ToInput(float2 roomXZ)
        {
            // M is a rotation, so its inverse is its transpose.
            return new float2((float)(M00 * roomXZ.x + M10 * roomXZ.y + AnchorX),
                              (float)(M01 * roomXZ.x + M11 * roomXZ.y + AnchorZ));
        }

        /// <summary>Index of the cell under a room-frame floor point, or -1 beyond the grid.</summary>
        public int CellIndex(float2 roomXZ)
        {
            int i = (int)Math.Floor((roomXZ.x - (double)OriginX) / CellSize);
            int j = (int)Math.Floor((roomXZ.y - (double)OriginZ) / CellSize);
            return i < 0 || j < 0 || i >= Width || j >= Height ? -1 : j * Width + i;
        }

        /// <summary>Flags of the cell under a room-frame floor point; <see cref="FloorCell.None"/> beyond the grid.</summary>
        public FloorCell CellAt(float2 roomXZ)
        {
            int c = CellIndex(roomXZ);
            return c < 0 ? FloorCell.None : Cells[c];
        }

        /// <summary>Room-frame centre of cell (i, j).</summary>
        public float2 CellCentre(int i, int j)
        {
            return new float2(OriginX + (i + 0.5f) * CellSize, OriginZ + (j + 0.5f) * CellSize);
        }

        /// <summary>
        /// Signed distance at the cell under a room-frame floor point; beyond the grid, the negative grid extent.
        /// </summary>
        public float SdfAt(float2 roomXZ)
        {
            int c = CellIndex(roomXZ);
            return c < 0 ? -Math.Max(Width, Height) * CellSize : Sdf[c];
        }
    }
}
