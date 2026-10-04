#if !ATLAS_HEADLESS
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>Checks of the kit that need the engine: the pieces load from Resources as GluedSpace and S3 load them.</summary>
    [Category("Unity")]
    public class KitUnityTests
    {
        [Test]
        public void KitSet_LoadsFromResources()
        {
            if (KitResources.Read(KitSet.ManifestName) == null)
                Assert.Ignore("No kit pieces in Resources yet: run tools/kit/export_kit.py on art/blender/ATLAS_Kit.blend.");

            KitSet set = KitResources.Shared();
            Assert.That(set.LastError, Is.Null);
            Assert.That(KitResources.Shared(), Is.SameAs(set), "read once per run, shared by every space");
            var mesh = new MeshData();
            foreach (string name in new[] { "landmark", "avatar", "duck" })
            {
                Assert.That(set.TryGet(name, mesh, out string error), Is.True, name + ": " + error);
                Assert.That(mesh.TriangleCount, Is.GreaterThan(0), name);
            }
        }
    }
}
#endif
