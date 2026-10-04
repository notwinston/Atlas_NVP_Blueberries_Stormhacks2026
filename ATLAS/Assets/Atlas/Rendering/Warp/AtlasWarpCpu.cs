using Unity.Mathematics;

namespace Atlas.Rendering
{
    // Line-for-line float mirror of AtlasWarp.hlsl (P1-B03). Every literal is float.
    public static class AtlasWarpCpu
    {
        public const float ModeNone = 0f, ModeHyperbolic = 1f, ModeSpherical = 2f; // kernel.md §6.1

        // Master Plan A.2 Exp at o (kappa = -1)
        public static float3 ExpH2(float2 t)
        {
            float r = math.length(t);
            float e = math.exp(r);
            float ie = 1f / e;
            float sOverR = r > 1e-6f ? 0.5f * (e - ie) / r : 1f;
            return new float3(t * sOverR, 0.5f * (e + ie));
        }

        // Master Plan A.2 Log at o (kappa = -1)
        public static float2 LogH2(float3 p)
        {
            float s = math.length(p.xy);
            return s > 1e-6f ? p.xy * (math.log(s + math.sqrt(s * s + 1f)) / s) : p.xy;
        }

        // Master Plan A.2 Exp at o (kappa = +1)
        public static float3 ExpS2(float2 t)
        {
            float r = math.length(t);
            math.sincos(r, out float sn, out float cs);
            float sOverR = r > 1e-6f ? sn / r : 1f;
            return new float3(t * sOverR, cs);
        }

        // Master Plan A.2 Log at o (kappa = +1)
        public static float2 LogS2(float3 p)
        {
            float s = math.length(p.xy);
            return s > 1e-6f ? p.xy * (math.atan2(s, p.z) / s) : p.xy;
        }

        // Master Plan §4.6 / §8.3
        public static float3 AtlasWarp(float3 positionOS, float4x4 anchor, float warpMode, float hypL, float3 headPosWS, float4x4 objectToWorld)
        {
            if (warpMode < 0.5f) return math.transform(objectToWorld, positionOS);
            float3x3 a = new float3x3(anchor);
            float2 t;
            if (warpMode < 1.5f) t = LogH2(math.mul(a, ExpH2(positionOS.xz / hypL))) * hypL;
            else t = LogS2(math.mul(a, ExpS2(positionOS.xz / hypL))) * hypL;
            return new float3(headPosWS.x + t.x, positionOS.y, headPosWS.z + t.y);
        }
    }
}
