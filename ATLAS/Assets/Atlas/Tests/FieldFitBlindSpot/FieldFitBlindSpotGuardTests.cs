using System.Linq;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using Atlas.Blades.FieldFit.ScotomaRiver;
using NUnit.Framework;
#if !ATLAS_HEADLESS
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
#endif

namespace Atlas.Blades.FieldFit.BlindSpotTests
{
    public class FieldFitBlindSpotGuardTests
    {
#if !ATLAS_HEADLESS
        [Test]
        public void Dependencies()
        {
            // checks: Master Plan §6.2 references; no Atlas.Blades.* and no Atlas.Spectator
            var asm = CompilationPipeline.GetAssemblies().Single(a => a.name == "Atlas.Blades.FieldFit");
            var atlas = asm.assemblyReferences.Select(r => r.name).Where(n => n.StartsWith("Atlas.")).ToList();
            var allowed = new[] { "Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Scan", "Atlas.Safety", "Atlas.Input", "Atlas.Telemetry", "Atlas.App" };
            foreach (var n in atlas)
                Assert.IsTrue(allowed.Contains(n) || n.StartsWith("Atlas.Spaces."), n);
            Assert.IsFalse(atlas.Any(n => n.StartsWith("Atlas.Blades.") || n == "Atlas.Spectator"));
        }

#endif

        [Test]
        public void NoClinicalClaim()
        {
            // checks: G13: no clinical claim in any string (Brief §2.4 Avoid list)
            var sample = new BlindSpotResult(EyeSide.Right, 1, true, false, 0.27, -0.03, 0.048, 8.6, default, 6, 18, 0, 96, 100);
            var strings = new[] { BlindSpotStrings.Intro, BlindSpotStrings.NotFound, BlindSpotStrings.RiverPrompt, BlindSpotStrings.NoRig,
                BlindSpotStrings.Result(sample) }.Concat(RiverText.DefaultLines);
            string all = string.Join("\n", strings).ToLowerInvariant();
            foreach (var bad in new[] { "diagnos", "detects", "treat", "rehabilit", "expands your visual field", "impair", "normal", "plans your procedure" })
                StringAssert.DoesNotContain(bad, all);
        }

#if !ATLAS_HEADLESS
        [Test]
        public void TestScene_Opens()
        {
            // checks: test scene holds one BlindSpotTestScene and no missing scripts (D13)
            var scene = EditorSceneManager.OpenScene("Assets/Atlas/Blades/FieldFit/Scenes/FieldFit_BlindSpotTest.unity", OpenSceneMode.Additive);
            try
            {
                var found = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BlindSpotTestScene>(true)).ToList();
                Assert.AreEqual(1, found.Count);
                foreach (var g in scene.GetRootGameObjects())
                    Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
#endif
    }
}
