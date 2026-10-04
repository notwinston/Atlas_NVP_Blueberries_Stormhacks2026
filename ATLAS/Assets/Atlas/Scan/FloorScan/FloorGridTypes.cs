using Unity.Mathematics;

namespace Atlas.Scan.Grid
{
    /// <summary>
    /// Class of one scanned triangle. The values are ARKit's own face-class numbers, the same as the scan
    /// layer's <c>FloorClass</c>, so the adapter that feeds a live scan casts them without a table.
    /// </summary>
    public enum SurfaceClass : byte
    {
        None = 0,
        Wall = 1,
        Floor = 2,
        Ceiling = 3,
        Table = 4,
        Seat = 5,
        Window = 6,
        Door = 7,
    }

    /// <summary>A detected horizontal plane, used to find the floor height.</summary>
    public struct HorizontalPlane
    {
        /// <summary>Height of the plane in the input frame, metres.</summary>
        public float Height;
        /// <summary>Area, square metres.</summary>
        public float Area;
        /// <summary>True when the plane is classified as floor.</summary>
        public bool IsFloor;
    }

    /// <summary>A detected wall plane, used for the dominant wall direction.</summary>
    public struct WallPlane
    {
        /// <summary>Horizontal normal (x, z) in the input frame. It need not be normalised.</summary>
        public float2 Normal;
        /// <summary>Length of the wall along the floor, metres. It is the weight of this wall.</summary>
        public float Length;
    }

    /// <summary>An oriented box standing in the room: furniture, or a blob the scan could not walk through.</summary>
    public struct ObstacleBox
    {
        /// <summary>Centre of the base. Y is the height of the underside.</summary>
        public float3 Base;
        /// <summary>Full size: width along local X, height, depth along local Z, metres.</summary>
        public float3 Size;
        /// <summary>Rotation about +Y, radians, Unity sense: it carries local +X to (cos, -sin) in (x, z).</summary>
        public float Yaw;
        /// <summary>Category given by the source of the box; 0 for boxes found by the scan.</summary>
        public int Category;
        /// <summary>Identifier given by the source of the box; 0 for boxes found by the scan.</summary>
        public int SourceId;
        /// <summary>True when the box was made from an obstacle blob of the grid, false when it was supplied.</summary>
        public bool FromScan;
    }

    /// <summary>
    /// Everything FloorScan reads (Master Plan section 4.2). All coordinates are in one frame with +Y up,
    /// normally the AR session frame; floor points are (x, z). The arrays are only read.
    /// </summary>
    public sealed class FloorGridInput
    {
        /// <summary>Mesh vertices.</summary>
        public float3[] Vertices;
        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Indices;
        /// <summary>One class per triangle. Null means every triangle is <see cref="SurfaceClass.None"/>.</summary>
        public SurfaceClass[] TriangleClasses;
        /// <summary>Floor height y0 when it is known. NaN (the default) derives it from the planes or the mesh.</summary>
        public float FloorHeight = float.NaN;
        /// <summary>Horizontal planes to derive y0 from. Optional.</summary>
        public HorizontalPlane[] Planes;
        /// <summary>Floor points the walker passed over during the sweep, in order. Optional.</summary>
        public float2[] WalkerPath;
        /// <summary>Where the walker stands when the sweep ends.</summary>
        public float2 Walker;
        /// <summary>The outline the walker confirmed. Optional; either winding.</summary>
        public float2[] Outline;
        /// <summary>Wall planes for the dominant wall direction. Optional.</summary>
        public WallPlane[] Walls;
        /// <summary>Boxes from another source (RoomPlan), in the same frame as the mesh. Optional.</summary>
        public ObstacleBox[] ExternalBoxes;
    }

    /// <summary>What FloorScan found in one 5 cm cell.</summary>
    [System.Flags]
    public enum FloorCell : byte
    {
        None = 0,
        /// <summary>Floor was seen here, or the walker passed over it.</summary>
        Floor = 1,
        /// <summary>Something stands here between 0.10 m and 2.0 m above the floor.</summary>
        Obstacle = 2,
        /// <summary>Inside the confirmed outline (without an outline: the cell was seen).</summary>
        Inside = 4,
        /// <summary>Free floor: seen, inside the outline and more than one cell from every obstacle.</summary>
        Free = 8,
        /// <summary>Walkable: part of the connected region at least 0.30 m from every wall and obstacle.</summary>
        Walk = 16,
    }

    /// <summary>How a scan ended.</summary>
    public enum FloorGridStatus
    {
        /// <summary>A free rectangle was found.</summary>
        Ok,
        /// <summary>No floor triangle, outline or path to build a grid from, or a footprint too large to be a room.</summary>
        NoFloor,
        /// <summary>A grid was built but no cell is 0.30 m clear of walls and obstacles.</summary>
        NoWalkableFloor,
    }
}
