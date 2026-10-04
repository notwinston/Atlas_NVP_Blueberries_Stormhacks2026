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

        /// <summary>The room's light total, and the bounds of the light target of one object (design section 5.4).</summary>
        public const int LightTotal = ObjectMeshRules.LightTriangles, LightMin = ObjectMeshRules.LightTargetMin,
            LightMax = ObjectMeshRules.LightTargetMax;

        /// <summary>
        /// The light target of every object in a room whose n objects have a triTarget above 0:
        /// clamp(60000 / n, 1500, 5000), whole-number division. 5000 for n = 0.
        /// </summary>
        public static int LightTarget(int n)
        {
            return n <= 0 ? LightMax : ObjectMeshRules.LightTarget(n);
        }

        /// <summary>The light target of a room's objects: LightTarget of the number whose TriTarget is above 0.</summary>
        public static int LightTarget(RoomObject[] objects)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            int n = 0;
            for (int k = 0; k < objects.Length; k++)
                if (objects[k].TriTarget > 0) n++;
            return LightTarget(n);
        }

        /// <summary>True when the light copy is the one drawn: the level is Light and the object has one. An object
        /// with no light copy draws its full copy at either level.</summary>
        public static bool DrawsLight(FurnitureLevel level, bool hasLightMesh)
        {
            return level == FurnitureLevel.Light && hasLightMesh;
        }

        /// <summary>
        /// As <see cref="Triangles(RoomObject)"/> at a furniture level. A generated object (state Ready) costs its
        /// full target at Full and lightTarget at Light; a placeholder, a stand-in and a built-in mesh cost the same at both.
        /// </summary>
        public static int Triangles(RoomObject obj, FurnitureLevel level, int lightTarget)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (level == FurnitureLevel.Light && obj.Mesh == MeshState.Ready) return lightTarget;
            return Triangles(obj);
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
