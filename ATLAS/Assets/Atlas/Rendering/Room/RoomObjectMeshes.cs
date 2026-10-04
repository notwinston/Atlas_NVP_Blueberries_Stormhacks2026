using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Room
{
    /// <summary>
    /// What a scanned object is drawn as before its own mesh arrives, and where it stands (Master Plan
    /// section 4.5). Engine-free. Meshes are in the object's local frame: origin at the centre of the base of
    /// its box, +X along w, +Y up, +Z along d.
    /// </summary>
    public static class RoomObjectMeshes
    {
        /// <summary>A box whose base is lower than this stands on the floor and has no bottom face, metres.</summary>
        public const float OnFloorBelow = 0.01f;
        /// <summary>Upper bound for a built-in fallback mesh (Master Plan section 8.4).</summary>
        public const int BuiltInMaxTriangles = 1500;

        public static bool OnFloor(RoomObject obj)
        {
            return obj.Base.y < OnFloorBelow;
        }

        /// <summary>Palette coordinate of the object's category tint.</summary>
        public static float2 CategoryUv(ObjectCategory category)
        {
            return AtlasPalette.Uv(AtlasPalette.Category(ObjectCategories.TintIndex(category)));
        }

        /// <summary>Appends the placeholder: a box of the scanned size on a 0.25 m grid, five faces when it
        /// stands on the floor and six otherwise, darkened where it meets the floor.</summary>
        public static void Placeholder(RoomObject obj, AtlasMeshBuilder data)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (data == null) throw new ArgumentNullException(nameof(data));
            bool onFloor = OnFloor(obj);
            data.AddBox(float3.zero, obj.Size, 0f, CategoryUv(obj.Category), !onFloor, 0, RoomShellBuilder.Pitch,
                        onFloor ? RoomShellBuilder.OcclusionAtEdge : 1f);
        }

        public static int PlaceholderTriangles(RoomObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            return AtlasMeshBudget.BoxTriangles(obj.Size, !OnFloor(obj), RoomShellBuilder.Pitch);
        }

        /// <summary>Appends what Build draws for the object: the stand-in when its state is StandIn, the
        /// placeholder box otherwise.</summary>
        public static void Initial(RoomObject obj, AtlasMeshBuilder data)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (obj.Mesh == MeshState.StandIn) StandInMeshBuilder.Build(obj.Size, obj.Category, data);
            else Placeholder(obj, data);
        }

        /// <summary>
        /// Triangles the object costs in the state its RoomModel entry gives: the box for Placeholder and
        /// Requested, 5,000 for StandIn, and the upper bounds for Ready (its triangle target) and BuiltIn.
        /// </summary>
        public static int Triangles(RoomObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            switch (obj.Mesh)
            {
                case MeshState.StandIn: return StandInMeshBuilder.Triangles;
                case MeshState.Ready: return obj.TriTarget > 0 ? obj.TriTarget : AtlasMeshBudget.FurnitureMax;
                case MeshState.BuiltIn: return BuiltInMaxTriangles;
                default: return PlaceholderTriangles(obj);
            }
        }

        /// <summary>A point of the object's local frame in the room frame: turned by yaw about +Y (Unity's
        /// sense), then moved to the base.</summary>
        public static float3 ToRoom(RoomObject obj, float3 local)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            math.sincos(obj.Yaw, out float sin, out float cos);
            return obj.Base + new float3(cos * local.x + sin * local.z, local.y, -sin * local.x + cos * local.z);
        }
    }
}
