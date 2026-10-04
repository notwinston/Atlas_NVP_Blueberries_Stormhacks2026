using System;
using UnityEngine;

namespace Atlas.World
{
    /// <summary>How the room's walls are shown by a space.</summary>
    public enum WallMode : byte { Full = 0, Kerbs = 1, Hidden = 2 }

    /// <summary>One scanned object as drawn: a placeholder box, a stand-in, a generated mesh or a built-in one.</summary>
    public interface IRoomObjectView
    {
        int ObjectId { get; }
        /// <summary>The RoomModel entry.</summary>
        RoomObject Data { get; }
        /// <summary>What is drawn now.</summary>
        MeshState Shown { get; }
        /// <summary>At Data.Base, rotated by Data.Yaw, scale one. Its child carries the mesh.</summary>
        Transform Transform { get; }
        /// <summary>One renderer per object.</summary>
        Renderer Renderer { get; }
        /// <summary>One pooled instance per object (contract README F7).</summary>
        Material Material { get; }
        int TriangleCount { get; }
    }

    /// <summary>The room shell and the object views (Master Plan sections 4.5, 4.9 and 7.4). Main thread only.</summary>
    public interface IRoomContent
    {
        /// <summary>Null before Build and after Clear.</summary>
        RoomModel Room { get; }
        bool IsBuilt { get; }
        /// <summary>Builds the shell and one view per object, replacing whatever was built. Objects appear as
        /// placeholder boxes, or as stand-in meshes when their state is StandIn. Ends with ResetPresentation.</summary>
        void Build(RoomModel room);
        void Clear();

        /// <summary>Parent of everything: "Atlas/Room" of the boot context. Identity in the room frame.</summary>
        Transform Root { get; }

        WallMode Walls { get; }
        /// <summary>Full: walls at their height, with openings. Kerbs: a low kerb along every wall, doors left
        /// open. Hidden: no walls. The change takes `seconds` (0 = at once): the walls sink or rise.</summary>
        void SetWallMode(WallMode mode, float seconds = 0.5f);
        /// <summary>Kerb height, metres, 0.05 to 0.9. Default 0.10.</summary>
        float KerbHeight { get; set; }
        /// <summary>A space that draws its own floor hides this one.</summary>
        bool FloorVisible { get; set; }
        bool ObjectsVisible { get; set; }
        /// <summary>The default mode: walls Full at once, floor and objects visible, KerbHeight 0.10, every
        /// material back to the pool's defaults except its palette and line values. SpaceLab calls it between
        /// two spaces.</summary>
        void ResetPresentation();

        /// <summary>
        /// Which copy of a generated mesh is drawn. Light: the light copy when the object has one, its full copy
        /// otherwise. Full: the full copy. Set between frames, on entering a space: the drawn mesh changes at
        /// once, with no cross-fade, and nothing is allocated. A built-in or placeholder mesh is unaffected.
        /// ResetPresentation (and so Build) returns it to Full.
        /// </summary>
        FurnitureLevel FurnitureLevel { get; set; }

        Renderer FloorRenderer { get; }
        /// <summary>The full walls or the kerbs, whichever is shown.</summary>
        Renderer WallRenderer { get; }
        /// <summary>One pooled instance for floor, walls and kerbs.</summary>
        Material ShellMaterial { get; }
        /// <summary>Floor + walls as drawn now.</summary>
        int ShellTriangleCount { get; }

        int ObjectCount { get; }
        /// <summary>0 .. ObjectCount - 1, in RoomModel order.</summary>
        IRoomObjectView GetObject(int index);
        bool TryGetObject(int objectId, out IRoomObjectView view);

        /// <summary>Records a state that changes no geometry: Requested, or back to Placeholder. Updates the
        /// RoomModel entry. False when the id is unknown or the state is another one.</summary>
        bool SetObjectState(int objectId, MeshState state);

        /// <summary>
        /// The asset client's call when a generated mesh is ready (state Ready). The mesh is in the box's local
        /// frame, metres, origin at the centre of the base (GLB contract). The room content takes ownership of
        /// it and destroys it when it is replaced or on Clear. Cross-fades from what is shown over fadeSeconds,
        /// then sets Data.Mesh = Ready and Data.AssetHash (and clears Data.AssetHashLight: the light copy follows). False when the id is unknown or the mesh is null.
        /// </summary>
        bool SetGeneratedMesh(int objectId, Mesh mesh, string assetHash, float fadeSeconds = 0.3f);

        /// <summary>
        /// The light copy of the generated mesh of an object, in the same frame as the full one. Call it after
        /// SetGeneratedMesh for the same object (also while that mesh is still waiting its turn). The room
        /// content takes ownership and destroys it when it is replaced, when a new full mesh or a built-in
        /// mesh takes the object's place, or on Clear. Drawn at once when the level is Light. Sets
        /// Data.AssetHashLight. False when the id is unknown, the mesh is null or the object has no generated mesh.
        /// </summary>
        bool SetGeneratedLightMesh(int objectId, Mesh mesh, string assetHashLight);

        /// <summary>
        /// True while the mesh is on a renderer of the room or is about to be: the mesh an object shows, its light
        /// copy, the mesh that is fading out and a mesh waiting for its swap. A client that owns a mesh the room
        /// content never destroys (a first shape) asks this before destroying it once it has been replaced.
        /// False after Clear.
        /// </summary>
        bool IsMeshDrawn(Mesh mesh);

        /// <summary>
        /// The asset client's call when a first shape or a built-in fallback mesh takes the object's place (state
        /// BuiltIn). The mesh belongs to the caller, which builds one per object and destroys it, and is never
        /// destroyed here; `scale` fits it to the box and is put on the mesh's own transform. Sets
        /// Data.Mesh = BuiltIn and clears Data.AssetHash.
        /// </summary>
        bool SetBuiltInMesh(int objectId, Mesh sharedMesh, Vector3 scale, float fadeSeconds = 0.3f);

        /// <summary>After Build.</summary>
        event Action<RoomModel> Built;
        /// <summary>Object id, after a state or a mesh change.</summary>
        event Action<int> ObjectChanged;
    }
}
