// PLACEHOLDER (lane A, P1-A06): contract surface only; replaced by lane A's implementation
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>Every shader property id (Master Plan §8.3, §6.4). Lane A section only; lane B adds its ids in P1-B03 (kernel.md §6.7).</summary>
    public static class AtlasShaderIDs
    {
        public static readonly int PaletteTex = Shader.PropertyToID("_PaletteTex");
        public static readonly int FogColor   = Shader.PropertyToID("_FogColor");
        public static readonly int FogStart   = Shader.PropertyToID("_FogStart");   // m
        public static readonly int FogEnd     = Shader.PropertyToID("_FogEnd");     // m
        public static readonly int Tint       = Shader.PropertyToID("_Tint");
    }
}
