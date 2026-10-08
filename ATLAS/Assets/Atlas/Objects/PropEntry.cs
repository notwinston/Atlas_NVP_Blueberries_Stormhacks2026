using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Objects
{
    /// <summary>
    /// One prop of the library (standalone-phone plan section 6.3): an artist-made mesh at its reference size,
    /// box-local (origin at the base centre, +Y up, front +Z, Unity's frame), with what the matcher needs to choose
    /// it. Built by Telemetry's PropSet from the manifest and the prop's GLB. Read-only once built: worker threads
    /// share it.
    /// </summary>
    public sealed class PropEntry
    {
        /// <summary>The manifest id, e.g. "chair_kenney_dining_01".</summary>
        public string Id;
        public ObjectCategory Category;
        /// <summary>RoomPlan attribute strings the prop stands for. Never null once built; may be empty.</summary>
        public string[] Tags;
        /// <summary>Reference size (w along local X, h up, d along local Z), metres.</summary>
        public float3 Size;
        /// <summary>The 256-byte profile at reference size, same convention as RoomObject.Profile; null for none.</summary>
        public byte[] Profile;
        /// <summary>One default palette cell (64..127) per colour slot, in manifest slot order.</summary>
        public byte[] SlotCells;
        /// <summary>The mesh at reference size in the look's vertex format.</summary>
        public MeshData Mesh;
    }

    /// <summary>What the matcher knows about one scanned object.</summary>
    public struct ObjectQuery
    {
        public ObjectCategory Category;
        /// <summary>RoomPlan attribute strings; null counts as none.</summary>
        public string[] Tags;
        /// <summary>The scanned box (w, h, d), metres.</summary>
        public float3 Size;
        /// <summary>The object's profile (RoomObject.Profile): null, or 256 bytes.</summary>
        public byte[] Profile;
        /// <summary>The rule that decided the object's front: Profile and Clearance fix it, Box and Table leave the quarter turn open.</summary>
        public ObjectFacingRule Facing;
    }

    /// <summary>The matcher's choice: library index, the quarter turns the prop is turned by, its profile fit and its score.</summary>
    public struct PropMatch
    {
        /// <summary>Index into the library passed to <see cref="PropMatcher.Choose"/>.</summary>
        public int Index;
        /// <summary>0..3: in Unity's frame each turn carries (x, y, z) to (-z, y, x); odd turns swap the prop's w and d.</summary>
        public int QuarterTurns;
        /// <summary>The profile fit F (0 is perfect; 0 when the query's profile covers less than 30 %).</summary>
        public float Fit;
        /// <summary>F + AspectWeight * A - tag bonus; lower is better.</summary>
        public float Score;
    }
}
