using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// The mathematics of "world space is the room frame" (contract README F4). The room anchor has a pose in
    /// the AR session frame; the XR Origin is placed so that this pose becomes the world origin with identity
    /// rotation. Only the anchor's yaw is used, so gravity stays up. Engine-free.
    /// </summary>
    public static class RoomFrame
    {
        /// <summary>
        /// The rotation about +Y nearest to a rotation: its forward vector projected on the floor. Identity
        /// when the forward vector is vertical.
        /// </summary>
        public static quaternion YawOnly(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            float lengthSq = f.x * f.x + f.z * f.z;
            if (!(lengthSq > 1e-8f)) return quaternion.identity;
            return quaternion.RotateY(math.atan2(f.x, f.z));
        }

        /// <summary>atan2 of the floor projection of a rotation's forward vector: radians, Unity's sense.</summary>
        public static float Yaw(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            return (f.x * f.x + f.z * f.z) > 1e-8f ? math.atan2(f.x, f.z) : 0f;
        }

        /// <summary>
        /// The world pose the AR session frame must have so that the anchor is at the world origin, unturned.
        /// anchorPosition and anchorRotation are the anchor's pose in the session frame.
        /// </summary>
        public static void SessionToRoom(float3 anchorPosition, quaternion anchorRotation,
            out float3 sessionPosition, out quaternion sessionRotation)
        {
            quaternion inverseYaw = math.inverse(YawOnly(anchorRotation));
            sessionRotation = inverseYaw;
            sessionPosition = -math.mul(inverseYaw, anchorPosition);
        }

        /// <summary>A pose in a child frame carried to the parent frame: parent * child.</summary>
        public static void Compose(float3 parentPosition, quaternion parentRotation,
            float3 childPosition, quaternion childRotation, out float3 position, out quaternion rotation)
        {
            position = parentPosition + math.mul(parentRotation, childPosition);
            rotation = math.normalize(math.mul(parentRotation, childRotation));
        }

        /// <summary>
        /// The pose a parent must have so that parent * childLocal equals the wanted world pose of the child.
        /// Used twice: the XR Origin from the session frame's wanted pose, and the tracked camera from the
        /// wanted pose of the head below it.
        /// </summary>
        public static void ParentFor(float3 worldPosition, quaternion worldRotation,
            float3 childLocalPosition, quaternion childLocalRotation, out float3 position, out quaternion rotation)
        {
            rotation = math.normalize(math.mul(worldRotation, math.inverse(childLocalRotation)));
            position = worldPosition - math.mul(rotation, childLocalPosition);
        }
    }
}
