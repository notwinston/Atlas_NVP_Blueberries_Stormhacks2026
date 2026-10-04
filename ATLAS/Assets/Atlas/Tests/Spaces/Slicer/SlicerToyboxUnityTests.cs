#if !ATLAS_HEADLESS
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Spaces.Slicer;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.Tests
{
    /// <summary>The toybox in the real module (tasks P2-A01, P2-A02): the slice is named, the panel says so, looking down
    /// standing still shows the shadow, and the two switches exist.</summary>
    [Category("Unity")]
    public class SlicerToyboxUnityTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void TheModule_NamesTheSlice_ShowsItOnThePanel_AndPeeksAtTheShadow()
        {
            var room = new RoomModel { FreeRect = new FreeRect(3f, 4f) };
            var registry = new ParamRegistry();
            var module = (SlicerSpace)SlicerSpace.Create();
            try
            {
                module.Configure(room, registry);
                Assert.That(registry.Find(SlicerToyboxParams.LookDown), Is.Not.Null);
                Assert.That(registry.Find(SlicerToyboxParams.Tour), Is.Not.Null);
                module.Enter(default(WalkerPose));

                // The corner-first tesseract cut through its middle (w = 0) is the octahedron.
                registry.Set(SlicerParams.W, 0f, ParamSource.Remote);
                var head = new float3(-0.75f, 1.6f, 0f);
                var frame = new FrameContext { DeltaTime = Dt, HeadPosition = head, HeadRotation = quaternion.identity };
                for (int i = 0; i < 3; i++) { module.Tick(in frame); module.Upload(in frame); }
                Assert.That(module.Section.Shape, Is.EqualTo(SectionShape.Octahedron));
                if (module.PanelText != null)
                {
                    StringAssert.Contains("TESSERACT", module.PanelText);
                    StringAssert.Contains("Octahedron", module.PanelText);
                    StringAssert.Contains("8 faces", module.PanelText);
                    Assert.That(module.Strip.KindCount, Is.EqualTo(3), "tetrahedron, truncated tetrahedron, octahedron");
                    StringAssert.Contains("Step 1 of 5", module.PanelText);
                }

                // Standing still and looking down 50 degrees: the shadow shows within a second, and hides on looking up.
                frame.HeadRotation = quaternion.Euler(math.radians(50f), 0f, 0f);
                for (int i = 0; i < 60; i++) { frame.Time += Dt; module.Tick(in frame); module.Upload(in frame); }
                Assert.That(module.PeekingShadow, Is.True);
                Assert.That(module.ShownView, Is.EqualTo(SlicerView.Shadow));
                frame.HeadRotation = quaternion.identity;
                module.Tick(in frame); module.Upload(in frame);
                Assert.That(module.PeekingShadow, Is.False);
                Assert.That(module.ShownView, Is.EqualTo(SlicerView.Slice));

                // Off switch: no peek.
                registry.Set(SlicerToyboxParams.LookDown, 0f, ParamSource.Remote);
                frame.HeadRotation = quaternion.Euler(math.radians(50f), 0f, 0f);
                for (int i = 0; i < 60; i++) { module.Tick(in frame); module.Upload(in frame); }
                Assert.That(module.PeekingShadow, Is.False);

                module.Exit();
            }
            finally
            {
                Object.DestroyImmediate(module.gameObject);
            }
        }
    }
}
#endif
