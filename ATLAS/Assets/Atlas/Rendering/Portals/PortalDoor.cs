using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// A doorway between two rooms (Master Plan D16): a vertical rectangle on the floor line from A to B.
    /// It has two directions. Forward is seen from FromRoom, with A on the left; backward is the same
    /// rectangle seen from ToRoom. The rooms share the room frame, so walking through moves nothing.
    /// </summary>
    public sealed class PortalDoor
    {
        /// <summary>The masks of one direction: the doorway itself and the box for an eye that straddles it.</summary>
        internal struct Masks
        {
            public MeshRenderer QuadMark, QuadReset, BoxMark, BoxReset;
            /// <summary>The stencil reference the materials were last set for; 0 = hidden.</summary>
            public int Reference;
            public bool Box;
        }

        internal Masks Forward, Backward;
        internal Mesh QuadMesh, ForwardBoxMesh, BackwardBoxMesh;
        internal GameObject Holder;

        internal PortalDoor(int id, int fromRoom, int toRoom, float2 a, float2 b, float height)
        {
            Id = id;
            FromRoom = fromRoom;
            ToRoom = toRoom;
            A = a;
            B = b;
            Height = height;
            Plane = PortalMath.DoorPlane(a, b);
            Open = true;
        }

        public int Id { get; }
        public int FromRoom { get; }
        public int ToRoom { get; }
        /// <summary>Floor point on the left as seen from FromRoom. Metres, room frame (x, z).</summary>
        public float2 A { get; }
        /// <summary>Floor point on the right as seen from FromRoom.</summary>
        public float2 B { get; }
        /// <summary>Metres above the floor.</summary>
        public float Height { get; }
        public float Width { get { return math.distance(A, B); } }
        /// <summary>(n.xyz, d): the normal points into FromRoom, at a walker who looks at the doorway from there.</summary>
        public float4 Plane { get; }
        /// <summary>A closed doorway shows nothing and lets nobody through.</summary>
        public bool Open { get; internal set; }

        /// <summary>The room a walker stands in to see this direction (0 forward, 1 backward).</summary>
        public int ViewerRoom(int direction) { return direction == 0 ? FromRoom : ToRoom; }
        /// <summary>The room this direction shows and leads to.</summary>
        public int TargetRoom(int direction) { return direction == 0 ? ToRoom : FromRoom; }
        /// <summary>The doorway's plane with its normal towards the viewer of this direction.</summary>
        public float4 ViewerPlane(int direction) { return direction == 0 ? Plane : PortalMath.Flip(Plane); }
    }
}
