using Atlas.Blades.Lung.Endoluminal;
using Atlas.Blades.Lung.Endoluminal.Testing;
using TMPro;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public static class EndoTestUtil
    {
        public const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        public static EndoluminalWindow NewWindow(out GameObject go)
        {
            go = new GameObject("EndoluminalWindowTest");
            var w = go.AddComponent<EndoluminalWindow>();
            var surface = Shader.Find("Atlas/AtlasSurface");
            var display = Shader.Find("Universal Render Pipeline/Unlit");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Assert.NotNull(surface, "Atlas/AtlasSurface"); Assert.NotNull(display, "URP Unlit"); Assert.NotNull(font, "LiberationSans SDF");
            w.Initialise(surface, display, font);
            return w;
        }

        public static void Dispose(EndoluminalWindow w, GameObject go)
        {
            w.Release();
            Object.DestroyImmediate(go);
        }

        public static ForkView Fork(double3 dir, string address = "")
        {
            var parent = new AirwaySegmentMm(address, double3.zero, dir, 20, 3);
            var l = new AirwaySegmentMm(address + "L", dir * 20, dir, 10, 2.4);
            var r = new AirwaySegmentMm(address + "R", dir * 20, dir, 10, 2.4);
            return new ForkView(parent, l, r);
        }
    }
}
