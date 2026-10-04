using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Atlas.Rendering
{
    /// <summary>
    /// The single final pass (Master Plan section 8.2): one full-screen triangle that samples both eye textures
    /// and writes the back buffer. Recorded only in the Screen camera's graph.
    /// </summary>
    sealed class LensBlitPass : ScriptableRenderPass
    {
        static readonly int s_EyeL = Shader.PropertyToID("_EyeL");
        static readonly int s_EyeR = Shader.PropertyToID("_EyeR");

        sealed class PassData
        {
            public Material material;
            public RTHandle left;
            public RTHandle right;
            public TextureHandle screen;
        }

        Material _material;
        RTHandle _left;
        RTHandle _right;

        public LensBlitPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        /// <summary>The handles are owned by the eye-texture sets; nothing is allocated here.</summary>
        public void Setup(Material material, RTHandle left, RTHandle right)
        {
            _material = material;
            _left = left;
            _right = right;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _left == null || _right == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle screen = resources.backBufferColor;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Atlas Lens Blit", out var data))
            {
                // The eye textures are not imported into this graph: each holds colour and depth in one texture
                // (the eye cameras render into it), and the render graph refuses to import such a texture. They
                // are finished before this camera renders (Screen depth 10 after the eyes), so the pass only
                // binds them to the material.
                data.material = _material;
                data.left = _left;
                data.right = _right;
                data.screen = screen;
                builder.SetRenderAttachment(screen, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.material.SetTexture(s_EyeL, d.left.rt);
                    d.material.SetTexture(s_EyeR, d.right.rt);
                    // Eye textures have their origin at the bottom left; the back buffer reports its own. URP's
                    // rule for a blit: flip V when the two differ.
                    bool flip = context.GetTextureUVOrigin(in d.screen) != TextureUVOrigin.BottomLeft;
                    Vector4 scaleBias = flip ? new Vector4(1f, -1f, 0f, 1f) : new Vector4(1f, 1f, 0f, 0f);
                    Blitter.BlitTexture(context.cmd, scaleBias, d.material, 0);
                });
            }

            // The screen is written here, so URP must not add its own final blit after this pass.
            resources.SwitchActiveTexturesToBackbuffer();
        }
    }
}
