using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// The values behind the shader globals of the look, and the one-second cross-fade of the fog colour
    /// (Master Plan section 8.1). Engine-free: <see cref="AtlasLook"/> writes these to the shader.
    /// </summary>
    public struct AtlasLookState
    {
        /// <summary>The darkest shade a vertex can have (_AtlasShade.x).</summary>
        public const float DarkestShade = 0.25f;
        /// <summary>Period of _AtlasTime, seconds: keeps the value small enough for single precision.</summary>
        public const double TimePeriod = 3600.0;

        /// <summary>Head position (the eye midpoint), world, metres.</summary>
        public float3 HeadPosition;
        /// <summary>Metres per kernel unit, above 0.</summary>
        public float MetresPerUnit;
        /// <summary>Fog and horizon colour as drawn this frame, linear rgb.</summary>
        public float3 FogColor;
        /// <summary>Fog ramp, metres from the head on the floor.</summary>
        public float FogStart, FogEnd;
        /// <summary>Zenith colour of the background, linear rgb.</summary>
        public float3 SkyTop;
        /// <summary>Line width unit, pixels.</summary>
        public float LineWidth;
        /// <summary>World fade: 1 visible, 0 black.</summary>
        public float Fade;
        /// <summary>Global emissive gain (_AtlasShade.y).</summary>
        public float EmissiveGain;
        /// <summary>Seconds, modulo <see cref="TimePeriod"/>.</summary>
        public float Time;

        float3 m_FogFrom, m_FogTo;
        float m_FogFadeElapsed, m_FogFadeSeconds;

        /// <summary>True while the fog colour is cross-fading.</summary>
        public bool FogFading => m_FogFadeElapsed < m_FogFadeSeconds;
        /// <summary>The fog colour the cross-fade ends on.</summary>
        public float3 FogColorTarget => m_FogTo;
        /// <summary>_AtlasFogParams: (start, 1 / (end - start), 0, 0).</summary>
        public float4 FogParams => new float4(FogStart, 1f / math.max(FogEnd - FogStart, 1e-3f), 0f, 0f);
        /// <summary>_AtlasShade: (darkest shade, emissive gain, 0, 0).</summary>
        public float4 Shade => new float4(DarkestShade, EmissiveGain, 0f, 0f);

        /// <summary>The state of a freshly booted look: default fog, palette FogBase and SkyTop, no fade.</summary>
        public static AtlasLookState Default()
        {
            var state = new AtlasLookState
            {
                HeadPosition = new float3(0f, 1.6f, 0f),
                MetresPerUnit = 1f,
                LineWidth = 1.5f,
                Fade = 1f,
                EmissiveGain = 1f,
                Time = 0f,
            };
            state.ResetForSpace();
            return state;
        }

        /// <summary>Defaults for a new space: default fog ramp, FogBase and SkyTop colours, immediately.</summary>
        public void ResetForSpace()
        {
            SetFog(AtlasLook.DefaultFogStart, AtlasLook.DefaultFogEnd);
            SetFogColor(AtlasPalette.Linear(AtlasPalette.FogBase), true);
            SkyTop = AtlasPalette.Linear(AtlasPalette.SkyTop);
        }

        /// <summary>Fog ramp in metres. An end at or before the start is moved just beyond it.</summary>
        public void SetFog(float startMetres, float endMetres)
        {
            FogStart = math.max(startMetres, 0f);
            FogEnd = math.max(endMetres, FogStart + 1e-3f);
        }

        /// <summary>Sets the fog colour, at once or as a cross-fade from the colour drawn now.</summary>
        public void SetFogColor(float3 linearRgb, bool immediate, float fadeSeconds = AtlasLook.FogCrossFadeSeconds)
        {
            m_FogTo = linearRgb;
            if (immediate || fadeSeconds <= 0f)
            {
                FogColor = linearRgb;
                m_FogFrom = linearRgb;
                m_FogFadeElapsed = 0f;
                m_FogFadeSeconds = 0f;
                return;
            }
            m_FogFrom = FogColor;
            m_FogFadeElapsed = 0f;
            m_FogFadeSeconds = fadeSeconds;
        }

        /// <summary>One frame: the head, the unit, the time and the fog cross-fade.</summary>
        public void Step(float3 headPosition, double metresPerUnit, double timeSeconds, float dt)
        {
            HeadPosition = headPosition;
            MetresPerUnit = metresPerUnit > 0.0 ? (float)metresPerUnit : 1f;
            Time = (float)(timeSeconds - TimePeriod * System.Math.Floor(timeSeconds / TimePeriod));

            if (m_FogFadeElapsed < m_FogFadeSeconds)
            {
                m_FogFadeElapsed = math.min(m_FogFadeElapsed + math.max(dt, 0f), m_FogFadeSeconds);
                FogColor = math.lerp(m_FogFrom, m_FogTo, m_FogFadeElapsed / m_FogFadeSeconds);
            }
            else
            {
                FogColor = m_FogTo;
            }
        }
    }
}
