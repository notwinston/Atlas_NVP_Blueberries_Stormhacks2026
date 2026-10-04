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
            public TextureHandle left;
            public TextureHandle right;
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
            TextureHandle left = renderGraph.ImportTexture(_left);
            TextureHandle right = renderGraph.ImportTexture(_right);
            TextureHandle screen = resources.backBufferColor;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Atlas Lens Blit", out var data))
            {
                data.material = _material;
                data.left = left;
                data.right = right;
                data.screen = screen;
                builder.UseTexture(left, AccessFlags.Read);
                builder.UseTexture(right, AccessFlags.Read);
                builder.SetRenderAttachment(screen, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.material.SetTexture(s_EyeL, d.left);
                    d.material.SetTexture(s_EyeR, d.right);
                    // Eye textures are imported with their origin at the bottom left; the back buffer reports
                    // its own. URP's rule for a blit: flip V when the two differ.
                    bool flip = context.GetTextureUVOrigin(in d.left) != context.GetTextureUVOrigin(in d.screen);
                    Vector4 scaleBias = flip ? new Vector4(1f, -1f, 0f, 1f) : new Vector4(1f, 1f, 0f, 0f);
                    Blitter.BlitTexture(context.cmd, scaleBias, d.material, 0);
                });
            }

            // The screen is written here, so URP must not add its own final blit after this pass.
            resources.SwitchActiveTexturesToBackbuffer();
        }
    }
}
