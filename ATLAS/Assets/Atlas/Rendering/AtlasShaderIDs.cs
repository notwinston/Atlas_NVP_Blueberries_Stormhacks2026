// PLACEHOLDER (lane A, P1-A06): contract surface only; replaced by lane A's implementation
// [P1-B06 X-B06-1] Union with lane B's block (kernel.md §6.7): lane A's real file must keep the lane B block.
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>Every shader property id (Master Plan §8.3, §6.4). Lane A section, then lane B's block (P1-B03, kernel.md §6.7).</summary>
    public static class AtlasShaderIDs
    {
        public static readonly int PaletteTex = Shader.PropertyToID("_PaletteTex");
        public static readonly int FogColor   = Shader.PropertyToID("_FogColor");
        public static readonly int FogStart   = Shader.PropertyToID("_FogStart");   // m
        public static readonly int FogEnd     = Shader.PropertyToID("_FogEnd");     // m
        public static readonly int Tint       = Shader.PropertyToID("_Tint");

        // lane B: AtlasWarp.hlsl
        public static readonly int HypL = Shader.PropertyToID("_HypL");           // global float, metres (L, or R in S4)
        public static readonly int HeadPosWS = Shader.PropertyToID("_HeadPosWS"); // global float3, metres, world
        public static readonly int Anchor = Shader.PropertyToID("_Anchor");       // per material float4x4
        public static readonly int WarpMode = Shader.PropertyToID("_WarpMode");   // per material float: 0 none, 1 hyperbolic, 2 spherical

        // lane B: AtlasGates.hlsl
        public static readonly int Gates = Shader.PropertyToID("_Gates");         // per material float4x4, rows = gates
    }
}
