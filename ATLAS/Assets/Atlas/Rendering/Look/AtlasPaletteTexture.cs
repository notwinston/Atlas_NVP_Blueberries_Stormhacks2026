using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// Builds the palette texture in code and binds it as the global _AtlasPalette (README F3: no painted file).
    /// 128 x 128, RGBA8 sRGB, no mip chain, uncompressed, point filtered, clamped.
    /// </summary>
    public static class AtlasPaletteTexture
    {
        static Texture2D s_Texture;

        /// <summary>The palette texture, or null before <see cref="Bind"/>.</summary>
        public static Texture2D Texture => s_Texture;

        /// <summary>A new palette texture. The caller owns it.</summary>
        public static Texture2D Create()
        {
            var texture = new Texture2D(AtlasPalette.Size, AtlasPalette.Size, TextureFormat.RGBA32, false, false)
            {
                name = "AtlasPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new byte[AtlasPalette.Size * AtlasPalette.Size * 4];
            AtlasPalette.WritePixels(pixels);
            texture.SetPixelData(pixels, 0);
            // The CPU copy is not needed again: colours for C# come from AtlasPalette.Linear.
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Creates the texture once and sets it as the global palette. Called at boot; safe to call again.</summary>
        public static Texture2D Bind()
        {
            if (s_Texture == null) s_Texture = Create();
            Shader.SetGlobalTexture(AtlasShaderIDs.Palette, s_Texture);
            return s_Texture;
        }
    }
}
