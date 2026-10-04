using System.Collections.Generic;
using Atlas.Telemetry;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// A replaced first shape is destroyed once, and only when the room content no longer draws it. Engine-free: the
    /// meshes are stand-in objects, "drawn" is a set the test edits as the room content's swaps begin and end.
    /// </summary>
    public class RetiredMeshesTests
    {
        sealed class Mesh { public readonly int Id; public Mesh(int id) { Id = id; } }

        readonly HashSet<Mesh> drawn = new HashSet<Mesh>();
        readonly List<Mesh> destroyed = new List<Mesh>();

        bool IsDrawn(Mesh mesh) { return drawn.Contains(mesh); }
        void Destroy(Mesh mesh) { destroyed.Add(mesh); }

        [SetUp]
        public void SetUp()
        {
            drawn.Clear();
            destroyed.Clear();
        }

        [Test]
        public void ALongSwapQueue_DestroysNothingBeforeTheSwapHasFinished()
        {
            var retired = new RetiredMeshes<Mesh>();
            var meshes = new Mesh[32];
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = new Mesh(i);
                drawn.Add(meshes[i]);                     // queued, then fading: still on a renderer
                retired.Add(meshes[i]);
            }
            // Far more sweeps than the old one second of frames; the swaps have not finished.
            for (int frame = 0; frame < 600; frame++) retired.Sweep(IsDrawn, Destroy);
            Assert.That(destroyed, Is.Empty);
            Assert.That(retired.Count, Is.EqualTo(32));
        }

        [Test]
        public void AfterTheSwap_TheOldMeshIsDestroyedOnce()
        {
            var retired = new RetiredMeshes<Mesh>();
            var a = new Mesh(1);
            var b = new Mesh(2);
            drawn.Add(a);
            drawn.Add(b);
            retired.Add(a);
            retired.Add(b);

            drawn.Remove(a);                               // a's cross-fade has ended
            retired.Sweep(IsDrawn, Destroy);
            Assert.That(destroyed, Is.EqualTo(new[] { a }));
            retired.Sweep(IsDrawn, Destroy);
            retired.Sweep(IsDrawn, Destroy);
            Assert.That(destroyed, Is.EqualTo(new[] { a }), "once, not again");

            drawn.Remove(b);
            retired.Sweep(IsDrawn, Destroy);
            Assert.That(destroyed, Is.EqualTo(new[] { a, b }));
            Assert.That(retired.Count, Is.EqualTo(0));
        }

        [Test]
        public void ARoomClearedWithSwapsPending_DestroysEachMeshOnce()
        {
            var retired = new RetiredMeshes<Mesh>();
            for (int i = 0; i < 8; i++)
            {
                var mesh = new Mesh(i);
                drawn.Add(mesh);
                retired.Add(mesh);
            }
            drawn.Clear();                                 // Clear: no view draws anything
            retired.Sweep(IsDrawn, Destroy);
            Assert.That(destroyed.Count, Is.EqualTo(8));
            Assert.That(new HashSet<Mesh>(destroyed).Count, Is.EqualTo(8), "no mesh twice");

            retired.Sweep(IsDrawn, Destroy);
            retired.DestroyAll(Destroy);                   // shutdown afterwards: nothing is left to destroy again
            Assert.That(destroyed.Count, Is.EqualTo(8));
        }

        [Test]
        public void DestroyAll_DestroysDrawnMeshesToo_AndEmptiesTheList()
        {
            var retired = new RetiredMeshes<Mesh>();
            var a = new Mesh(1);
            drawn.Add(a);
            retired.Add(a);
            retired.Add(null);                             // ignored
            retired.DestroyAll(Destroy);
            Assert.That(destroyed, Is.EqualTo(new[] { a }));
            retired.DestroyAll(Destroy);
            Assert.That(destroyed.Count, Is.EqualTo(1));
        }
    }
}
