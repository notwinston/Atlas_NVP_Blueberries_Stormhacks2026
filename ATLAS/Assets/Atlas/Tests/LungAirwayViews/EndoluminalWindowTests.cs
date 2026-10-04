using System;
using System.Reflection;
using Atlas.Blades.Lung.Endoluminal;
using Atlas.Blades.Lung.Endoluminal.Testing;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class EndoluminalWindowTests
    {
        // Criterion: P2-B07 output "512x512" render texture.
        [Test]
        public void Endo_TextureSpec()
        {
            var w = EndoTestUtil.NewWindow(out var go);
            try
            {
                var forks = SyntheticForks.Route(4);
                w.Show(forks[0]);
                var tex = w.Texture;
                Assert.AreEqual(512, tex.width); Assert.AreEqual(512, tex.height);
                Assert.GreaterOrEqual(tex.depth, 24); Assert.AreEqual(4, tex.antiAliasing); Assert.IsFalse(tex.useMipMap);
                w.Show(forks[1]); w.Show(forks[2]);
                Assert.AreSame(tex, w.Texture);
            }
            finally { EndoTestUtil.Dispose(w, go); }
        }

        // Criterion: P2-B07 "rendered once per fork"; "render cost in 1 frame only" (the 299 further calls cost nothing).
        [Test]
        public void Endo_RenderOncePerFork()
        {
            var w = EndoTestUtil.NewWindow(out var go);
            try
            {
                var forks = SyntheticForks.Route(4);
                Assert.IsTrue(w.Show(forks[0]));
                Assert.AreEqual(1, w.RenderCount); Assert.AreEqual(Time.frameCount, w.LastRenderFrame);
                Assert.AreEqual(forks[0].Address, w.RenderedAddress);
                for (int i = 0; i < 299; i++) Assert.IsFalse(w.Show(forks[0]));
                Assert.AreEqual(1, w.RenderCount);
                Assert.IsTrue(w.Show(forks[1])); Assert.AreEqual(2, w.RenderCount);
                Assert.IsTrue(w.Show(forks[0])); Assert.AreEqual(3, w.RenderCount);
                Debug.Log($"[P2-B07] render-frame CPU time (Editor, last Show) = {w.LastRenderCpuMs:F3} ms");
            }
            finally { EndoTestUtil.Dispose(w, go); }
        }

        // Criterion: P2-B07 "0 ms afterwards": no per-frame or render-callback methods; only Display is active after Show.
        [Test]
        public void Endo_NoPerFrameWork()
        {
            var banned = new[] { "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnRenderObject", "OnWillRenderObject", "OnPreCull", "OnPreRender", "OnPostRender" };
            var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in typeof(EndoluminalWindow).GetMethods(flags)) Assert.IsFalse(Array.IndexOf(banned, m.Name) >= 0, "per-frame method " + m.Name);
            var w = EndoTestUtil.NewWindow(out var go);
            try
            {
                foreach (Transform t in go.transform) Assert.IsFalse(t.gameObject.activeSelf, t.name + " active before Show");
                w.Show(SyntheticForks.Route(1)[0]);
                int active = 0;
                foreach (Transform t in go.transform) if (t.gameObject.activeSelf) { active++; Assert.AreEqual("Display", t.name); }
                Assert.AreEqual(1, active);
                Assert.AreSame(go.transform.Find("Display"), w.Display);
                w.Hide();
                Assert.IsFalse(w.Display.gameObject.activeSelf);
            }
            finally { EndoTestUtil.Dispose(w, go); }
        }

        // Criterion: MP 10.2.3 clock-position labels: root fork of the synthetic route labels L = 9 o'clock, R = 3 o'clock.
        [Test]
        public void Endo_Labels_SyntheticRoot()
        {
            var w = EndoTestUtil.NewWindow(out var go);
            try
            {
                w.Show(SyntheticForks.Route(1)[0]);
                Assert.AreEqual("9 o'clock", w.LabelText(0)); Assert.AreEqual("3 o'clock", w.LabelText(1));
                Assert.AreEqual(9, w.ClockHour(0)); Assert.AreEqual(3, w.ClockHour(1));
            }
            finally { EndoTestUtil.Dispose(w, go); }
        }

        // Criterion: P2-B07 phone test scene (deferred run): the scene loads and the driver references the three assets.
        [Test]
        public void Endo_TestScene_References()
        {
            const string path = "Assets/Atlas/Blades/Lung/Endoluminal/TestScene/Lung_EndoluminalTest.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var driver = UnityEngine.Object.FindFirstObjectByType<EndoluminalTestDriver>();
                Assert.NotNull(driver);
                var so = new UnityEditor.SerializedObject(driver);
                Assert.AreEqual("Atlas/AtlasSurface", ((Shader)so.FindProperty("surfaceShader").objectReferenceValue).name);
                Assert.AreEqual("Universal Render Pipeline/Unlit", ((Shader)so.FindProperty("displayShader").objectReferenceValue).name);
                Assert.NotNull(so.FindProperty("labelFont").objectReferenceValue);
                Assert.NotNull(Camera.main);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
