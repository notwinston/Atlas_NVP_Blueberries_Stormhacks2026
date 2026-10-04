using Atlas.Blades.Lung.Endoluminal;
using Atlas.Blades.Lung.Endoluminal.Testing;
using Atlas.Rendering;
using TMPro;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public static class EndoTestUtil
    {
#if !ATLAS_HEADLESS
        public const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // The pool each window rents its one material from (the real MaterialPool on the Atlas/Surface shader from Resources).
        static readonly System.Collections.Generic.Dictionary<EndoluminalWindow, MaterialPool> Pools = new System.Collections.Generic.Dictionary<EndoluminalWindow, MaterialPool>();

        public static EndoluminalWindow NewWindow(out GameObject go)
        {
            go = new GameObject("EndoluminalWindowTest");
            var w = go.AddComponent<EndoluminalWindow>();
            var display = Shader.Find("Universal Render Pipeline/Unlit");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Assert.NotNull(display, "URP Unlit"); Assert.NotNull(font, "LiberationSans SDF");
            var pool = MaterialPool.CreateDefault(2);
            Pools[w] = pool;
            w.Initialise(pool, display, font);
            return w;
        }

        public static void Dispose(EndoluminalWindow w, GameObject go)
        {
            w.Release();
            if (Pools.TryGetValue(w, out var pool)) { pool.Dispose(); Pools.Remove(w); }
            Object.DestroyImmediate(go);
        }

#endif

        public static ForkView Fork(double3 dir, string address = "")
        {
            var parent = new AirwaySegmentMm(address, double3.zero, dir, 20, 3);
            var l = new AirwaySegmentMm(address + "L", dir * 20, dir, 10, 2.4);
            var r = new AirwaySegmentMm(address + "R", dir * 20, dir, 10, 2.4);
            return new ForkView(parent, l, r);
        }
    }
}
