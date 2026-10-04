using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// One LiDAR depth image with the pose it was taken at. The buffer belongs to the depth source and is
    /// overwritten by its next read: use it within the frame, never keep it.
    /// </summary>
    public struct DepthFrame
    {
        public const int NominalWidth = 256;
        public const int NominalHeight = 192;
        /// <summary>Metres: a nearer value is "no return".</summary>
        public const float MinRange = 0.3f;
        /// <summary>Metres: a farther value is "no return".</summary>
        public const float MaxRange = 5.0f;

        /// <summary>Width * Height floats, metres along the camera's forward axis. Row-major, row 0 at the top of
        /// the image. 0, NaN or a value outside [0.3, 5.0] means "no return".</summary>
        public float[] Depth;
        public int Width, Height;
        /// <summary>Pinhole intrinsics of this image, in its own pixels: fx, fy, cx, cy. cx, cy from the top-left corner.</summary>
        public float Fx, Fy, Cx, Cy;
        /// <summary>Pose of the tracked camera (not of the head) when the image was taken: same frame as HeadPose.Position.</summary>
        public float3 Position;
        public quaternion Rotation;
        /// <summary>Seconds, app clock.</summary>
        public double Time;
        /// <summary>Rises by one with every new image.</summary>
        public int Sequence;

        public bool IsValid { get { return Depth != null && Width > 0 && Height > 0 && Fx > 0f; } }

        /// <summary>True when d is a return: finite and inside [0.3, 5.0] metres.</summary>
        public static bool IsReturn(float d)
        {
            return d >= MinRange && d <= MaxRange;   // false for NaN
        }

        /// <summary>Camera-space point of pixel (u, v) at depth d: ((u + 0.5 - Cx) / Fx * d, -(v + 0.5 - Cy) / Fy * d, d).
        /// Camera space is Unity's: +X right, +Y up, +Z forward.</summary>
        public float3 Unproject(int u, int v, float d)
        {
            return new float3((u + 0.5f - Cx) / Fx * d, -(v + 0.5f - Cy) / Fy * d, d);
        }

        /// <summary>Position + Rotation * Unproject(u, v, d).</summary>
        public float3 ToWorld(int u, int v, float d)
        {
            return Position + math.mul(Rotation, Unproject(u, v, d));
        }
    }
}
