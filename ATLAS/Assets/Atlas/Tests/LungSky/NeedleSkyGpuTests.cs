#if !ATLAS_HEADLESS
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public class NeedleSkyGpuTests
    {
        const string SettingsPath = "Assets/Atlas/Blades/Lung/Sky/NeedleSkySettings.asset";

        [Test, Category("Unity")] // P2-B10 acceptance, rendering path; catches a v-flip or az sign error (graphics sign-off run)
        public void B10_Shader_RawOrientation_GpuOnly()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "no graphics device (-nographics)");
            var settings = AssetDatabase.LoadAssetAtPath<NeedleSkySettings>(SettingsPath);
            Assume.That(settings != null && settings.skyShader != null && settings.skyShader.isSupported, "settings asset or shader unavailable");
            var maps = SkyMapsFactory.Fixture();
            var view = new NeedleSkyView(maps, settings.skyShader, null);
            var camGo = new GameObject("SkyTestCam");
            var rt = new RenderTexture(1, 1, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                view.Material.SetFloat(NeedleSkyShaderIDs.DebugRaw, 1f);
                view.Material.SetTexture(NeedleSkyShaderIDs.PhaseTex, view.Textures.Phase(0));
                view.GameObject.SetActive(true);
                var cam = camGo.AddComponent<Camera>();
                cam.targetTexture = rt; cam.fieldOfView = 1f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 10f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                foreach (var c in maps.Meta.checks)
                {
                    camGo.transform.rotation = Quaternion.LookRotation((Vector3)(Unity.Mathematics.float3)SkyDirection.FromAzEl(c.az_deg, c.el_deg), Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); tex.Apply(); RenderTexture.active = null;
                    SkyDirection.Pixel(c.az_deg, c.el_deg, out int i, out int j);
                    int expect = maps.RiskBytes(0)[j * 512 + i];
                    Assert.LessOrEqual(Math.Abs(Mathf.RoundToInt(tex.GetPixel(0, 0).r * 255f) - expect), 1, "az " + c.az_deg + " el " + c.el_deg);
                }
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                view.Dispose(); UnityEngine.Object.DestroyImmediate(camGo); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
#endif
