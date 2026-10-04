using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering.Room
{
    /// <summary>
    /// One scanned object as drawn (Master Plan section 4.5): an anchor transform at the box's base, turned by
    /// its yaw, and under it one renderer that starts with the placeholder box or the stand-in and later takes
    /// the generated or built-in mesh. The anchor is the object's own: a curved space re-anchors the object
    /// rigidly by writing its anchor matrix to <see cref="Material"/> (Master Plan section 4.6).
    /// Created and driven by <see cref="RoomContent"/>.
    /// </summary>
    public sealed class RoomObjectView : IRoomObjectView
    {
        internal GameObject AnchorObject;
        internal Transform MeshTransformInternal;
        internal MeshFilter Filter;
        internal MeshRenderer MeshRenderer;
        internal Material MaterialInternal;
        internal Mesh Mesh;
        internal bool OwnsMesh;
        internal RoomObject DataInternal;
        internal MeshState ShownInternal;
        internal int Triangles;

        // The mesh that is fading out, on its own rented material.
        internal GameObject OutObject;
        internal MeshFilter OutFilter;
        internal MeshRenderer OutRenderer;
        internal Material OutMaterial;
        internal Mesh OutMesh;
        internal bool OutOwned;
        internal int OutTriangles;
        internal bool Fading;
        internal float FadeTime, FadeSeconds;

        // The mesh waiting for its turn: at most one swap begins per frame.
        internal bool HasPending;
        internal Mesh PendingMesh;
        internal bool PendingOwned;
        internal MeshState PendingState;
        internal string PendingHash;
        internal Vector3 PendingScale;
        internal float PendingSeconds;

        internal RoomObjectView() { }

        public int ObjectId { get { return DataInternal.Id; } }
        public RoomObject Data { get { return DataInternal; } }
        public MeshState Shown { get { return ShownInternal; } }
        /// <summary>The anchor: at Data.Base, rotated by Data.Yaw, scale one.</summary>
        public Transform Transform { get { return AnchorObject.transform; } }
        public Renderer Renderer { get { return MeshRenderer; } }
        public Material Material { get { return MaterialInternal; } }
        public int TriangleCount { get { return Triangles; } }

        /// <summary>The child that carries the mesh; a built-in mesh's fitting scale is on it.</summary>
        public Transform MeshTransform { get { return MeshTransformInternal; } }
        /// <summary>The object's anchor in the room frame: the centre of the base of its box, metres.</summary>
        public float3 AnchorPosition { get { return DataInternal.Base; } }
        /// <summary>The object's turn about +Y in the room frame, radians.</summary>
        public float AnchorYaw { get { return DataInternal.Yaw; } }
        /// <summary>True while the mesh it replaced is still fading out: one more draw call.</summary>
        public bool IsFading { get { return Fading; } }
    }
}
