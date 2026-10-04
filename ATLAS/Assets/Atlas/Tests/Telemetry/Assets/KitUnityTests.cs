#if !ATLAS_HEADLESS
using Atlas.World;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Telemetry.Tests
{
    /// <summary>Checks of the kit that need the engine: the pieces load from Resources as GluedSpace loads them.</summary>
    [Category("Unity")]
    public class KitUnityTests
    {
        static byte[] ReadKitResource(string name)
        {
            var asset = Resources.Load<TextAsset>(KitSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        [Test]
        public void KitSet_LoadsFromResources()
        {
            if (ReadKitResource(KitSet.ManifestName) == null)
                Assert.Ignore("No kit pieces in Resources yet: run tools/kit/export_kit.py on art/blender/ATLAS_Kit.blend.");

            KitSet set = KitSet.Load(ReadKitResource);
            Assert.That(set.LastError, Is.Null);
            var mesh = new MeshData();
            foreach (string name in new[] { "landmark", "avatar" })
            {
                Assert.That(set.TryGet(name, mesh, out string error), Is.True, name + ": " + error);
                Assert.That(mesh.TriangleCount, Is.GreaterThan(0), name);
            }
        }
    }
}
#endif
