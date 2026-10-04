using UnityEngine;

namespace Atlas.Rendering
{
    // Property ids (Master Plan §6.4; kernel.md §6.7). Lane A adds its own ids to this file.
    public static class AtlasShaderIDs
    {
        // lane B: AtlasWarp.hlsl
        public static readonly int HypL = Shader.PropertyToID("_HypL");           // global float, metres (L, or R in S2)
        public static readonly int HeadPosWS = Shader.PropertyToID("_HeadPosWS"); // global float3, metres, world
        public static readonly int Anchor = Shader.PropertyToID("_Anchor");       // per material float4x4
        public static readonly int WarpMode = Shader.PropertyToID("_WarpMode");   // per material float: 0 none, 1 hyperbolic, 2 spherical

        // lane B: AtlasGates.hlsl
        public static readonly int Gates = Shader.PropertyToID("_Gates");         // per material float4x4, rows = gates

        // lane A: Palette, fog, tint
        public static readonly int PaletteTex = Shader.PropertyToID("_PaletteTex");
        public static readonly int FogColor   = Shader.PropertyToID("_FogColor");
        public static readonly int FogStart   = Shader.PropertyToID("_FogStart");   // m
        public static readonly int FogEnd     = Shader.PropertyToID("_FogEnd");     // m
        public static readonly int Tint       = Shader.PropertyToID("_Tint");
    }
}
