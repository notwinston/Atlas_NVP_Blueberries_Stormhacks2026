using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    /// <summary>
    /// Frame pacing and screen settings for the whole app (Master Plan section 8.2, Appendix C.4). iOS defaults
    /// to 30 fps, and a running ARSession writes Application.targetFrameRate and vSyncCount on every update
    /// while its "match frame rate" option is on, so the wanted values are re-asserted once per frame, at
    /// render time, after every Update has run.
    /// </summary>
    public static class AtlasBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            FramePacing.TargetFrameRate = FramePacing.DefaultFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            // A visible development watermark makes URP store the multisampled eye colour instead of
            // resolving it on the tile.
            Watermark.showDeveloperWatermark = false;
            ApplyPacing();

            // Unsubscribe first: with domain reload off in the Editor this method runs again on every Play.
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            RenderPipelineManager.beginContextRendering += OnBeginContext;
        }

        static void OnBeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            ApplyPacing();
            EnforceScreenMsaa();
        }

        /// <summary>Writes the wanted frame rate if something else has changed it. No allocation.</summary>
        public static void ApplyPacing()
        {
            int rate = FramePacing.TargetFrameRate;
            if (Application.targetFrameRate != rate)
                Application.targetFrameRate = rate;
            int vSync = FramePacing.VSyncCount(rate);
            if (QualitySettings.vSyncCount != vSync)
                QualitySettings.vSyncCount = vSync;
        }

        /// <summary>
        /// Keeps the screen's own buffer at one sample. URP copies its asset's 4x MSAA into the quality
        /// settings when the pipeline is created, and lowers it again only when a single camera renders; the
        /// eye textures carry the MSAA, the screen needs none.
        /// </summary>
        public static void EnforceScreenMsaa()
        {
            if (Screen.msaaSamples > 1)
                Screen.SetMSAASamples(1);
        }
    }
}
