using Unity.Mathematics;

namespace Atlas.Rendering
{
    // Line-for-line float mirror of AtlasWarp.hlsl (P1-B03). Every literal is float.
    public static class AtlasWarpCpu
    {
        public const float ModeNone = 0f, ModeHyperbolic = 1f, ModeSpherical = 2f; // kernel.md §6.1
        /// <summary>S3, the Small Planet's "Hyperbolica look" (planet.view3d): the floor is a great 2-sphere of the round 3-space.</summary>
        public const float ModeS3 = 3f;

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
            if (s > 1e-6f) return p.xy * (math.atan2(s, p.z) / s);
            return p.z < 0f ? new float2(3.14159265f, 0f) : p.xy; // A.2 at the cut point: distance pi, as Geometry2.Log
        }

        // AtlasWarp.hlsl LogS3: q = (cos hr p, sin hr), E = (0, 0, cos er, sin er); Log_E(q) / R as (right, up, forward)
        public static float3 LogS3(float3 p, float hr, float er)
        {
            math.sincos(new float2(hr, er), out float2 sn, out float2 cs);
            float3 v = new float3(cs.x * p.x, cs.y * sn.x - sn.y * cs.x * p.z, cs.x * p.y);
            float c = cs.x * cs.y * p.z + sn.x * sn.y;
            float s = math.length(v);
            if (s > 1e-6f) return v * (math.atan2(s, c) / s);
            return c < 0f ? new float3(0f, 3.14159265f, 0f) : v;
        }

        // Master Plan §4.6 / §8.3
        public static float3 AtlasWarp(float3 positionOS, float4x4 anchor, float warpMode, float hypL, float3 headPosWS, float4x4 objectToWorld)
        {
            if (warpMode < 0.5f) return math.transform(objectToWorld, positionOS);
            float3x3 a = new float3x3(anchor);
            float h = positionOS.y + anchor.c3.y; // c3.y is _Anchor._m13
            float2 t;
            if (warpMode < 1.5f) t = LogH2(math.mul(a, ExpH2(positionOS.xz / hypL))) * hypL;
            else
            {
                float3 p = math.mul(a, ExpS2(positionOS.xz / hypL));
                if (warpMode > 2.5f) return headPosWS + LogS3(p, h / hypL, headPosWS.y / hypL) * hypL;
                t = LogS2(p) * hypL;
            }
            return new float3(headPosWS.x + t.x, h, headPosWS.z + t.y);
        }
    }
}
