using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// The shader globals of the look (lane A contract, sections 10.4 and 10.8). A space sets fog and background
    /// in Enter; SpaceLab resets them. Nobody else calls Shader.SetGlobal for these names.
    /// </summary>
    public static class AtlasLook
    {
        public const float DefaultFogStart = 8f, DefaultFogEnd = 14f;   // metres
        public const float FogCrossFadeSeconds = 1f;

        /// <summary>Resources names of the two shaders of the look.</summary>
        public const string SurfaceShaderResource = "AtlasSurface", BackgroundShaderResource = "AtlasBackground";

        static AtlasLookState s_State = AtlasLookState.Default();
        static Material s_Background;

        /// <summary>The values last written to the shader.</summary>
        public static AtlasLookState State => s_State;

        /// <summary>
        /// Boot: builds and binds the palette, sets every global to its default and installs the background.
        /// Safe to call again.
        /// </summary>
        public static void Initialise()
        {
            AtlasPaletteTexture.Bind();
            s_State = AtlasLookState.Default();
            Push();
            InstallBackground();
        }

        /// <summary>Atlas/Surface, loaded from Resources so that it is in a player build. Null if missing.</summary>
        public static Shader LoadSurfaceShader()
        {
            return Resources.Load<Shader>(SurfaceShaderResource);
        }

        /// <summary>Hidden/Atlas/Background, loaded from Resources. Null if missing.</summary>
        public static Shader LoadBackgroundShader()
        {
            return Resources.Load<Shader>(BackgroundShaderResource);
        }

        /// <summary>
        /// Makes the gradient the scene's skybox, so a camera that clears with the skybox draws it once per eye
        /// after the opaque geometry. Returns the material, or null when the shader is missing.
        /// </summary>
        public static Material InstallBackground()
        {
            if (s_Background == null)
            {
                Shader shader = LoadBackgroundShader();
                if (shader == null)
                {
                    Debug.LogError("look.background: shader 'AtlasBackground' is not in Resources.");
                    return null;
                }
                s_Background = new Material(shader) { name = "AtlasBackground", hideFlags = HideFlags.HideAndDontSave };
            }
            RenderSettings.skybox = s_Background;
            return s_Background;
        }

        /// <summary>Fog ramp in metres from the head, on the floor. end must exceed start.</summary>
        public static void SetFog(float startMetres, float endMetres)
        {
            s_State.SetFog(startMetres, endMetres);
            Shader.SetGlobalVector(AtlasShaderIDs.FogParams, s_State.FogParams);
        }

        /// <summary>Fog and horizon colour, linear rgb. Cross-fades over FogCrossFadeSeconds unless immediate.</summary>
        public static void SetFogColor(float3 linearRgb, bool immediate = false)
        {
            s_State.SetFogColor(linearRgb, immediate);
            Shader.SetGlobalVector(AtlasShaderIDs.FogColor, ToVector(s_State.FogColor, 1f));
        }

        /// <summary>Zenith colour of the background, linear rgb.</summary>
        public static void SetSkyTop(float3 linearRgb)
        {
            s_State.SkyTop = linearRgb;
            Shader.SetGlobalVector(AtlasShaderIDs.SkyTop, ToVector(linearRgb, 1f));
        }

        /// <summary>Line width unit in pixels (render.lineWidth).</summary>
        public static void SetLineWidth(float pixels)
        {
            s_State.LineWidth = math.max(pixels, 0f);
            Shader.SetGlobalFloat(AtlasShaderIDs.LineWidth, s_State.LineWidth);
        }

        /// <summary>World fade 0..1. SpaceLab only.</summary>
        public static void SetFade(float fade)
        {
            s_State.Fade = math.saturate(fade);
            Shader.SetGlobalFloat(AtlasShaderIDs.Fade, s_State.Fade);
        }

        /// <summary>Global gain of the emissive accents; 1 by default.</summary>
        public static void SetEmissiveGain(float gain)
        {
            s_State.EmissiveGain = math.max(gain, 0f);
            Shader.SetGlobalVector(AtlasShaderIDs.Shade, s_State.Shade);
        }

        /// <summary>Defaults for a new space: default fog, FogBase and SkyTop colours, immediate. SpaceLab only.</summary>
        public static void ResetForSpace()
        {
            s_State.ResetForSpace();
            Push();
        }

        /// <summary>
        /// Step 6 of the frame: the head position, the metres per kernel unit, the time and the fog cross-fade.
        /// Writes every global, so the look survives anything that resets shader state. Allocates nothing.
        /// </summary>
        /// <param name="headPositionWS">Head position (the eye midpoint), world, metres: the filtered sample that stepped the kernel (FrameContext.HeadPosition).</param>
        /// <param name="metresPerUnit">IWalkerKernel.MetresPerUnit; 1 in a flat space.</param>
        /// <param name="timeSeconds">The frame's time, seconds.</param>
        /// <param name="dt">The frame's duration, seconds.</param>
        public static void Upload(float3 headPositionWS, double metresPerUnit, double timeSeconds, float dt)
        {
            s_State.Step(headPositionWS, metresPerUnit, timeSeconds, dt);
            Push();
        }

        static void Push()
        {
            Shader.SetGlobalVector(AtlasShaderIDs.HeadPosWS, ToVector(s_State.HeadPosition, 0f));
            Shader.SetGlobalFloat(AtlasShaderIDs.HypL, s_State.MetresPerUnit);
            Shader.SetGlobalVector(AtlasShaderIDs.FogColor, ToVector(s_State.FogColor, 1f));
            Shader.SetGlobalVector(AtlasShaderIDs.FogParams, s_State.FogParams);
            Shader.SetGlobalVector(AtlasShaderIDs.SkyTop, ToVector(s_State.SkyTop, 1f));
            Shader.SetGlobalVector(AtlasShaderIDs.Shade, s_State.Shade);
            Shader.SetGlobalFloat(AtlasShaderIDs.LineWidth, s_State.LineWidth);
            Shader.SetGlobalFloat(AtlasShaderIDs.Fade, s_State.Fade);
            Shader.SetGlobalFloat(AtlasShaderIDs.Time, s_State.Time);
        }

        static Vector4 ToVector(float3 rgb, float w)
        {
            return new Vector4(rgb.x, rgb.y, rgb.z, w);
        }
    }
}
