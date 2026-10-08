using System;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>Highest scan route that produced the room (Master Plan section 4.2). Values are the JSON values.</summary>
    public enum ScanRoute : byte { Fixture = 0, ManualOutline = 1, FloorScan = 2, ObjectBoxes = 3, WallBridge = 4 }

    /// <summary>What is drawn for an object. JSON names: placeholder, requested, ready, builtIn, standIn, matched,
    /// scanCut. Matched: an artist-made prop chosen, scaled and coloured on the phone (RoomObject.Prop and PropCells
    /// say which and how). ScanCut: the object's own triangles cut out of the scanned LiDAR mesh, which is not saved.</summary>
    public enum MeshState : byte { Placeholder = 0, Requested = 1, Ready = 2, BuiltIn = 3, StandIn = 4, Matched = 5, ScanCut = 6 }

    /// <summary>Which rule decided an object's front (mesh design section 5.2). JSON names: box, profile, table, clearance.</summary>
    public enum ObjectFacingRule : byte { Box = 0, Profile = 1, Table = 2, Clearance = 3 }

    /// <summary>Which copy of the furniture meshes is drawn (mesh design section 5.4).</summary>
    public enum FurnitureLevel : byte { Full = 0, Light = 1 }

    /// <summary>JSON names: door, window, opening.</summary>
    public enum OpeningKind : byte { Door = 0, Window = 1, Opening = 2 }

    /// <summary>Pose of the room frame in the AR session frame when the room was saved.</summary>
    public struct RoomAnchor
    {
        /// <summary>ARAnchor trackable id as text, or "".</summary>
        public string Id;
        /// <summary>Metres, AR session frame; y is the floor.</summary>
        public float3 Position;
        /// <summary>Radians, (-pi/2, pi/2].</summary>
        public float Yaw;
    }

    /// <summary>The free rectangle: centred on the origin, Wf along X, Df along Z, Wf not larger than Df.</summary>
    public struct FreeRect
    {
        /// <summary>Metres, short side, along X.</summary>
        public float Wf;
        /// <summary>Metres, long side, along Z.</summary>
        public float Df;

        public FreeRect(float wf, float df) { Wf = wf; Df = df; }

        /// <summary>(-Wf/2, -Df/2).</summary>
        public float2 Min { get { return new float2(-0.5f * Wf, -0.5f * Df); } }
        /// <summary>(+Wf/2, +Df/2).</summary>
        public float2 Max { get { return new float2(0.5f * Wf, 0.5f * Df); } }

        /// <summary>True for a floor point inside the rectangle or on its edge.</summary>
        public bool Contains(float2 xz)
        {
            return math.abs(xz.x) <= 0.5f * Wf && math.abs(xz.y) <= 0.5f * Df;
        }
    }

    /// <summary>Loop-fit radii (Master Plan section 4.4). L5, L6, R3 are the exact loop-fit values.</summary>
    public struct RoomCurvature
    {
        /// <summary>Metres, q = 5.</summary>
        public float L5;
        /// <summary>Metres, q = 6.</summary>
        public float L6;
        /// <summary>Metres, q = 3.</summary>
        public float R3;
        /// <summary>Metres, comfort floor for L.</summary>
        public float FloorL;
        /// <summary>Metres, comfort floor for R.</summary>
        public float FloorR;

        /// <summary>Radius the dial uses: q = 3 gives max(R3, FloorR), q = 5 max(L5, FloorL), q = 6 max(L6, FloorL).
        /// With strong = true the exact loop-fit value is returned. Any other q: ArgumentOutOfRangeException.</summary>
        public float ForDial(int q, bool strong = false)
        {
            switch (q)
            {
                case 3: return strong ? R3 : math.max(R3, FloorR);
                case 5: return strong ? L5 : math.max(L5, FloorL);
                case 6: return strong ? L6 : math.max(L6, FloorL);
                default: throw new ArgumentOutOfRangeException(nameof(q), "The dial has radii for q = 3, 5 and 6 only.");
            }
        }
    }

    // GluedGrid (Nx, Nz, SizeX, SizeZ) and AlignedGrid (N, M, G) are declared in CellGridRules.cs.

    /// <summary>A door, window or plain opening in a wall.</summary>
    public struct WallOpening
    {
        public OpeningKind Kind;
        /// <summary>Metres from the wall's A to the near edge.</summary>
        public float Offset;
        /// <summary>Metres along the wall.</summary>
        public float Width;
        /// <summary>Metres above the floor; 0 for a door.</summary>
        public float Bottom;
        /// <summary>Metres above the floor.</summary>
        public float Top;
    }

    /// <summary>One wall segment on an edge of the outline.</summary>
    public sealed class RoomWall
    {
        /// <summary>Metres, room frame; the room is on the left of A to B.</summary>
        public float2 A, B;
        /// <summary>Metres above the floor.</summary>
        public float Height;
        /// <summary>Never null.</summary>
        public WallOpening[] Openings = Array.Empty<WallOpening>();

        public float Length { get { return math.distance(A, B); } }
    }

    /// <summary>One scanned object: an oriented box, its category and the state of its mesh.</summary>
    public sealed class RoomObject
    {
        /// <summary>Metres above the floor: lower things do not block it.</summary>
        public const float BlockMinHeight = 0.10f;
        /// <summary>Metres above the floor: higher things do not block it.</summary>
        public const float BlockMaxHeight = 2.0f;

        /// <summary>1..255, unique in the room. The link calls it boxId.</summary>
        public int Id;
        public ObjectCategory Category;
        /// <summary>Metres, room frame: centre of the box's base.</summary>
        public float3 Base;
        /// <summary>Metres: (w along local X, h up, d along local Z).</summary>
        public float3 Size;
        /// <summary>Radians, (-pi, pi], about +Y in Unity's sense.</summary>
        public float Yaw;
        /// <summary>Changed only through IRoomContent, on the main thread.</summary>
        public MeshState Mesh;
        /// <summary>"" unless Mesh == Ready.</summary>
        public string AssetHash = "";
        /// <summary>0, or 3000..15000: the triangle target of the full copy.</summary>
        public int TriTarget;
        /// <summary>The object profile: null, or exactly 256 bytes, index = j * 16 + i over the box's footprint
        /// (i along local X, j along local Z, row 0 at the lowest z). 0 is unseen; otherwise the height of the
        /// scanned surface as clamp(floor(255 * height / h + 0.5), 1, 255). Not part of the geometry hash.</summary>
        public byte[] Profile;
        /// <summary>The rule that decided the front. Local +Z is the front of every object.</summary>
        public ObjectFacingRule Facing;
        /// <summary>SHA-256 of the light copy of the generated GLB, or "".</summary>
        public string AssetHashLight = "";
        /// <summary>RoomPlan attribute strings, verbatim (e.g. from String(describing:)). Never null; may be empty.
        /// Not part of the geometry hash. A file holds at most MaxTags of them, each at most MaxTagLength characters:
        /// RoomModelJson.Write leaves out any other.</summary>
        public string[] Tags = Array.Empty<string>();
        /// <summary>"" or a prop id from the prop manifest (at most MaxPropLength characters). Set with Mesh = Matched.
        /// Not part of the geometry hash.</summary>
        public string Prop = "";
        /// <summary>null, or one palette cell (64..127) per colour slot of Prop, in manifest slot order (at most
        /// MaxPropCells). Not part of the geometry hash.</summary>
        public byte[] PropCells;

        /// <summary>Limits of the optional keys tags, prop and propCells of a RoomModel file.</summary>
        public const int MaxTags = 16, MaxTagLength = 48, MaxPropLength = 64, MaxPropCells = 8;
        /// <summary>The object colours of the palette: a prop cell lies in FirstPropCell..LastPropCell.</summary>
        public const int FirstPropCell = 64, LastPropCell = 127;

        public float Volume { get { return Size.x * Size.y * Size.z; } }
        public quaternion Rotation { get { return quaternion.RotateY(Yaw); } }

        /// <summary>True when some part lies between 0.10 m and 2.0 m above the floor.</summary>
        public bool BlocksFloor
        {
            get { return Base.y + Size.y > BlockMinHeight && Base.y < BlockMaxHeight; }
        }
    }

    /// <summary>How well the scan went.</summary>
    public struct RoomQuality
    {
        /// <summary>0..1: cells inside the outline never seen, over cells inside the outline.</summary>
        public float UnknownShare;
        public ScanRoute Route;
    }

    /// <summary>Polygon with holes. Outer ring counter-clockwise, holes clockwise.</summary>
    public sealed class WalkablePolygon
    {
        public float2[] Outer = Array.Empty<float2>();
        /// <summary>Never null.</summary>
        public float2[][] Holes = Array.Empty<float2[]>();

        /// <summary>Even-odd rule: inside the outer ring and in no hole.</summary>
        public bool Contains(float2 xz)
        {
            if (!RingContains(Outer, xz)) return false;
            for (int k = 0; k < Holes.Length; k++)
                if (RingContains(Holes[k], xz)) return false;
            return true;
        }

        /// <summary>Even-odd test of one ring. Points exactly on an edge are not specified.</summary>
        public static bool RingContains(float2[] ring, float2 xz)
        {
            if (ring == null) return false;
            bool inside = false;
            int n = ring.Length;
            for (int k = 0, prev = n - 1; k < n; prev = k++)
            {
                float2 p = ring[prev], q = ring[k];
                if ((p.y > xz.y) != (q.y > xz.y) && xz.x < p.x + (xz.y - p.y) * (q.x - p.x) / (q.y - p.y))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Signed area of a ring, square metres: positive when counter-clockwise seen from above.</summary>
        public static float SignedArea(float2[] ring)
        {
            if (ring == null) return 0f;
            double total = 0.0;
            int n = ring.Length;
            for (int k = 0; k < n; k++)
            {
                float2 p = ring[k], q = ring[(k + 1) % n];
                total += (double)p.x * q.y - (double)q.x * p.y;
            }
            return (float)(0.5 * total);
        }
    }

    /// <summary>
    /// The scanned room (Master Plan section 4.3). Treated as immutable once built, with one exception:
    /// Objects[k].Mesh and .AssetHash (and .Prop and .PropCells) change when furniture arrives. No UnityEngine type.
    /// World space is the room frame: origin at the centre of the free rectangle on the floor, +X along Wf,
    /// +Z along Df, +Y up; floor points are float2 (x, z).
    /// </summary>
    public sealed class RoomModel
    {
        public const int SchemaVersion = 1;
        public const int MaxObjects = 64;
        public const int MaxOutlinePoints = 255;
        /// <summary>Wall height used when the scan did not see the ceiling, metres.</summary>
        public const float DefaultWallHeight = 2.4f;

        public int Version = SchemaVersion;
        public string Id = "";
        public string CreatedUtc = "";
        public RoomAnchor Anchor = new RoomAnchor { Id = "" };
        /// <summary>The floor outline, wall to wall. Simple polygon, counter-clockwise.</summary>
        public float2[] Outline = Array.Empty<float2>();
        public RoomSdf Sdf;
        public WalkablePolygon Walkable = new WalkablePolygon();
        public FreeRect FreeRect;
        /// <summary>Side s of the lap square, metres.</summary>
        public float LapSide;
        public RoomCurvature Curvature;
        /// <summary>cells.glued.</summary>
        public GluedGrid Glued;
        /// <summary>cells.aligned.</summary>
        public AlignedGrid Aligned;
        public bool Standing;
        /// <summary>Never null.</summary>
        public RoomWall[] Walls = Array.Empty<RoomWall>();
        /// <summary>Never null.</summary>
        public RoomObject[] Objects = Array.Empty<RoomObject>();
        public RoomQuality Quality;

        /// <summary>Fills LapSide, Curvature, Glued, Aligned and Standing from FreeRect (LoopFit, CellGridRules).</summary>
        public void ApplyLoopFit()
        {
            // The sides are multiples of 0.05 m held as floats; rounding to micrometres gives the rules the
            // decimal value that was meant (2.4, not 2.4000000954).
            double wf = Math.Round((double)FreeRect.Wf, 6);
            double df = Math.Round((double)FreeRect.Df, 6);
            LoopFitResult fit = LoopFit.Fit(wf, df);
            LapSide = (float)fit.S;
            // Derived values are kept at the six decimals a RoomModel file carries, so a room built here and
            // the same room read back from its file hold identical numbers on the phone and on the Station.
            Curvature = new RoomCurvature
            {
                L5 = (float)Math.Round(fit.L5, 6), L6 = (float)Math.Round(fit.L6, 6), R3 = (float)Math.Round(fit.R3, 6),
                FloorL = LoopFit.FloorL, FloorR = LoopFit.FloorR
            };
            GluedGrid glued = CellGridRules.Glued(wf, df);
            glued.SizeX = Math.Round(glued.SizeX, 6);
            glued.SizeZ = Math.Round(glued.SizeZ, 6);
            Glued = glued;
            AlignedGrid aligned = CellGridRules.Aligned(wf, df);
            aligned.G = Math.Round(aligned.G, 6);
            Aligned = aligned;
            Standing = LoopFit.IsStanding(wf, df);
        }

        public bool TryGetObject(int id, out RoomObject obj)
        {
            RoomObject[] objects = Objects;
            for (int k = 0; k < objects.Length; k++)
            {
                if (objects[k].Id == id)
                {
                    obj = objects[k];
                    return true;
                }
            }
            obj = null;
            return false;
        }

        /// <summary>Number of objects with TriTarget > 0.</summary>
        public int MeshedObjectCount
        {
            get
            {
                int n = 0;
                for (int k = 0; k < Objects.Length; k++) if (Objects[k].TriTarget > 0) n++;
                return n;
            }
        }

        /// <summary>Largest wall height, or 2.4 when there is no wall.</summary>
        public float MaxWallHeight
        {
            get
            {
                if (Walls.Length == 0) return DefaultWallHeight;
                float h = 0f;
                for (int k = 0; k < Walls.Length; k++) h = math.max(h, Walls[k].Height);
                return h;
            }
        }
    }
}
