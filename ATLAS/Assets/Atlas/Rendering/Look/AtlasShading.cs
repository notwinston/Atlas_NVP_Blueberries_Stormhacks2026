using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// The baked light of the look (Master Plan D23): one fixed key direction, so every generator shades alike
    /// and the shader needs no normal. The result goes into the red channel of the vertex colour.
    /// </summary>
    public static class AtlasShading
    {
        /// <summary>Direction towards the fixed diagram light, room frame: normalize(0.35, 0.85, 0.40).</summary>
        public static readonly float3 LightDirection = math.normalize(new float3(0.35f, 0.85f, 0.40f));

        /// <summary>Brightness of a face that points away from the light.</summary>
        public const float ShadeFloor = 0.55f;

        /// <summary>ShadeFloor + (1 - ShadeFloor) * (0.5 + 0.5 * dot(normal, LightDirection)). Normal is unit length.</summary>
        public static float FaceShade(float3 normal)
        {
            return ShadeFloor + (1f - ShadeFloor) * (0.5f + 0.5f * math.dot(normal, LightDirection));
        }

        /// <summary>The same with abs(dot): for surfaces whose winding is not known (4D slices).</summary>
        public static float FaceShadeTwoSided(float3 normal)
        {
            return ShadeFloor + (1f - ShadeFloor) * (0.5f + 0.5f * math.abs(math.dot(normal, LightDirection)));
        }

        /// <summary>round(255 * saturate(shade * ambientOcclusion)).</summary>
        public static byte ToByte(float shade, float ambientOcclusion = 1f)
        {
            return (byte)math.round(255f * math.saturate(shade * ambientOcclusion));
        }
    }
}
