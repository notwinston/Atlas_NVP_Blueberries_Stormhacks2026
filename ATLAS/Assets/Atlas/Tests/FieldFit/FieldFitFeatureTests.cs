#if !ATLAS_HEADLESS
using System;
using System.IO;
using Atlas.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B03 tests, Master Plan §10.4.3, §8.2; Brief §9. CPU only (EditMode, also runs -nographics).
    public class FieldFitFeatureTests
    {
        internal const string ShaderPath = "Assets/Atlas/Blades/FieldFit/Shaders/FieldFitBlit.shader";

        internal sealed class Harness : IDisposable
        {
            public GameObject Go; public FieldFitFeature F; public FakeRig Rig = new FakeRig();
            public FakeLensParams P = new FakeLensParams(); public FakeEvents Ev = new FakeEvents();
            public FieldFitSettings Settings; public int Frame;

            public Harness(bool centreEye = false, bool noBadge = false)
            {
                Go = new GameObject("FieldFitTest"); Go.SetActive(false);   // OnEnable must not run: tests call Init
                F = Go.AddComponent<FieldFitFeature>();
                Settings = ScriptableObject.CreateInstance<FieldFitSettings>();
                Settings.blitShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                Settings.centreEyeSource = centreEye;
                Assert.IsNotNull(Settings.blitShader, "FieldFitBlit shader must import");
                F.Settings = Settings;
                F.FrameSource = () => Frame;
                if (!noBadge) F.TestBadgeTexture = Texture2D.whiteTexture;
                F.Init(Rig, P, Ev);
            }
            public void Step() { Frame++; F.FrameUpdate(); }
            public void Dispose() { F.Shutdown(); UnityEngine.Object.DestroyImmediate(Go); UnityEngine.Object.DestroyImmediate(Settings); }
        }

        [Test, Description("P3-B03 acceptance: exactly one final blit per frame; FieldFit makes no draws of its own (Master Plan §10.4.3; Brief §9)")]
        public void ExactlyOneFinalBlit_NoOwnDraws()
        {
            string[] banned = { "Graphics.Blit", ".Blit(", "Blitter.", "EnqueuePass", "ScriptableRenderPass", "ScriptableRendererFeature",
                                "DrawProcedural", "Camera.Render", "SubmitRenderRequest", "RenderPipelineManager" };
            var root = Path.Combine(Application.dataPath, "Atlas", "Blades", "FieldFit");
            int files = 0;
            foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                var text = File.ReadAllText(f);
                foreach (var b in banned) Assert.IsFalse(text.Contains(b), Path.GetFileName(f) + " contains " + b);
            }
            Assert.Greater(files, 5);
        }

        [Test, Description("P3-B03 acceptance: exactly one final blit per frame: the material is substituted exactly while the lens is active (Master Plan §10.4.3)")]
        public void ExactlyOneFinalBlit_Substitution()
        {
            using (var h = new Harness())
            {
                int activations = 0; bool was = false;
                for (int fr = 0; fr < 600; fr++)
                {
                    bool expect = (fr / 50) % 2 == 0;
                    h.P.On = expect;
                    h.Step();
                    Assert.AreEqual(expect, h.F.LensActive, "active at frame " + fr);
                    if (expect) { Assert.AreSame(h.F.BlitMaterial, h.Rig.BlitMaterial, "material at " + fr); Assert.IsNotNull(h.Rig.Targets[0]); Assert.IsNotNull(h.Rig.Targets[1]); }
                    else { Assert.IsNull(h.Rig.BlitMaterial, "cleared at " + fr); Assert.IsNull(h.Rig.Targets[0]); Assert.IsNull(h.Rig.Targets[1]); }
                    if (expect && !was) activations++;
                    was = expect;
                }
                Assert.AreEqual(6, activations);
                Assert.AreEqual(activations, h.Rig.SetMaterialCalls, "material is never set twice in a row without a change");
                Assert.AreEqual(activations, h.Rig.ClearMaterialCalls);
            }
        }

        static readonly int[] Sizes = { 1536, 1280, 1024, 1024, 2048 }, Msaa = { 4, 4, 2, 1, 4 };

        [Test, Description("P3-B03 acceptance: source targets per rung 1536/1280/1024/1024/2048, MSAA 4/4/2/1/4, allocated once (Master Plan §10.4.3, §8.2)")]
        public void SourceTargets_PerRung_AllocatedOnce()
        {
            using (var h = new Harness())
            {
                for (int r = 0; r < 5; r++)
                    for (int e = 0; e < 2; e++)
                    {
                        var rt = h.F.SourceTexture(r, e);
                        Assert.AreEqual(Sizes[r], rt.width, "size rung " + r); Assert.AreEqual(Sizes[r], rt.height);
                        Assert.AreEqual(Msaa[r], rt.descriptor.msaaSamples, "msaa rung " + r);
                        Assert.AreEqual(RenderTextureMemoryless.MSAA | RenderTextureMemoryless.Depth, rt.memorylessMode);
                        Assert.AreEqual(GraphicsFormat.R8G8B8A8_SRGB, rt.graphicsFormat);
                        Assert.IsFalse(rt.useMipMap);
                    }
                Assert.AreEqual(10, h.F.RenderTexturesCreated);
                for (int fr = 0; fr < 600; fr++) { h.Rig.Rung = (QualityRung)((fr / 7) % 5); h.Step(); }
                Assert.AreEqual(10, h.F.RenderTexturesCreated, "no RT created after enable");
                Assert.AreSame(h.F.SourceTexture((int)h.Rig.Rung, 0), h.Rig.Targets[0], "override follows the rung");
            }
        }

        [Test, Description("P3-B03 acceptance: source projection half-angle tan E = 1.3763819 at E = 54 deg, changing in the frame lens.E changes (Master Plan §10.4.3)")]
        public void Projection_HalfAngle_TanE()
        {
            using (var h = new Harness())
            {
                h.Step();
                Assert.AreEqual(1.3763819f, h.Rig.TanHalf[0], 1e-6f); Assert.AreEqual(1.3763819f, h.Rig.TanHalf[1], 1e-6f);
                h.P.EDeg = 50f; h.Step();
                Assert.AreEqual((float)Math.Tan(50.0 * Math.PI / 180.0), h.Rig.TanHalf[0], 1e-6f);
            }
        }

        [Test, Description("P3-B03 acceptance: lookup rebuilt only when a lens parameter changes (Master Plan §10.4.3)")]
        public void Rebuild_OnlyOnChange()
        {
            using (var h = new Harness())
            {
                h.Step(); Assert.AreEqual(1, h.F.Lut.RebuildCount);
                for (int i = 0; i < 600; i++) h.Step();
                Assert.AreEqual(1, h.F.Lut.RebuildCount, "600 unchanged frames");
                h.P.Rho0Deg = 12f; h.Step();
                Assert.AreEqual(2, h.F.Lut.RebuildCount, "one change, one rebuild, in that frame");
                for (int i = 0; i < 10; i++) h.Step();
                Assert.AreEqual(2, h.F.Lut.RebuildCount);
            }
        }

        [Test, Description("P3-B03 acceptance: per-frame allocation 0 B over 600 active unchanged frames (Master Plan §8.5 aim, §11.2 <= 1 kB)")]
        public void FrameUpdate_ZeroAlloc()
        {
            using (var h = new Harness())
            {
                for (int i = 0; i < 20; i++) h.Step();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) { h.Frame++; h.F.FrameUpdate(); }
                long grown = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0, grown, "managed bytes allocated by 600 frames");
                Assert.IsTrue(h.F.LensActive);
            }
        }

        [Test, Description("P3-B03 acceptance: the frame work runs once per Time.frameCount (Brief §8)")]
        public void FrameUpdate_OncePerFrame()
        {
            using (var h = new Harness())
            {
                h.Frame = 5; h.F.FrameUpdate(); int n = h.F.FrameWorkCount;
                h.F.FrameUpdate(); h.F.FrameUpdate();
                Assert.AreEqual(n, h.F.FrameWorkCount, "same frame: no extra work");
                h.Frame = 6; h.F.FrameUpdate();
                Assert.AreEqual(n + 1, h.F.FrameWorkCount);
            }
        }

        [Test, Description("P3-B03 acceptance: centre-eye fallback gives lateral offset 0 for both eyes, else the normal offset (Master Plan §10.4.9)")]
        public void CentreEye_Offset()
        {
            using (var h = new Harness(true)) { h.Step(); Assert.AreEqual(0f, h.Rig.Lateral[0]); Assert.AreEqual(0f, h.Rig.Lateral[1]); }
            using (var h = new Harness(false))
            {
                h.Step();
                Assert.AreEqual(h.Rig.NormalLateralOffset(0), h.Rig.Lateral[0]); Assert.AreEqual(h.Rig.NormalLateralOffset(1), h.Rig.Lateral[1]);
                Assert.AreNotEqual(0f, h.Rig.Lateral[0]);
            }
        }

        [Test, Description("P3-B03 acceptance: lensOn / lensOff events carry the §8 fields; lens off on lens.on = 0 with cause param (Brief §8)")]
        public void Events_OnParamOff()
        {
            using (var h = new Harness())
            {
                h.Step(); Assert.AreEqual(1, h.Ev.Count("lensOn"));
                StringAssert.Contains("\"kStar\":2.918", h.Ev.Log[0].Value);
                h.P.On = false; h.Step();
                Assert.AreEqual(1, h.Ev.Count("lensOff")); StringAssert.Contains("\"cause\":\"param\"", h.Ev.Log[1].Value);
                Assert.IsFalse(h.F.LensActive);
            }
        }

        [Test, Description("P3-B04 acceptance: a lens.t change rebuilds exactly once in that frame and the K* in lensOn is unchanged (Master Plan §10.4.3 Slider)")]
        public void LensT_Change_RebuildsOnce()
        {
            using (var h = new Harness())
            {
                h.Step(); Assert.AreEqual(1, h.F.Lut.RebuildCount);
                var on0 = h.Ev.Log[0].Value;
                h.P.T = 0.5f; h.Step();
                Assert.AreEqual(2, h.F.Lut.RebuildCount);
                for (int i = 0; i < 20; i++) h.Step();
                Assert.AreEqual(2, h.F.Lut.RebuildCount);
                Assert.AreEqual(1, h.Ev.Count("lensOn"), "no second lensOn");
                StringAssert.Contains("\"kStar\":2.918", on0);
                Assert.AreEqual(0.5, h.F.CurrentParams.T, 1e-9);
                Assert.AreEqual(LensMath.KStar(LensParams.Default), LensMath.KStar(h.F.CurrentParams), 1e-6, "K* does not depend on t");
            }
        }

        [Test, Description("P3-B03 acceptance: the blit shader and the saved scene/settings load (Brief §3, §12)")]
        public void ShaderSettingsScene_Present()
        {
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(sh); Assert.IsFalse(ShaderUtil.ShaderHasError(sh), "FieldFitBlit has shader errors"); Assert.IsTrue(sh.isSupported || UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);
            var st = AssetDatabase.LoadAssetAtPath<FieldFitSettings>("Assets/Atlas/Blades/FieldFit/FieldFitSettings.asset");
            Assert.IsNotNull(st); Assert.AreSame(sh, st.blitShader); Assert.AreEqual(30f, st.maskHalfWidthDeg); Assert.IsFalse(st.centreEyeSource); Assert.IsNotNull(st.badgeFont, "badge font assigned");
            foreach (var scenePath in new[] { "Assets/Atlas/Blades/FieldFit/Scenes/Blade_FieldFit.unity", "Assets/Atlas/Blades/FieldFit/Scenes/Blade_FieldFit_Soak.unity" })
            {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var go = scene.GetRootGameObjects();
                Assert.AreEqual(1, go.Length); Assert.AreEqual("FieldFit", go[0].name);
                var f = go[0].GetComponent<FieldFitFeature>(); Assert.IsNotNull(f);
                Assert.AreSame(st, f.Settings);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
            }
        }
    }
}
#endif
