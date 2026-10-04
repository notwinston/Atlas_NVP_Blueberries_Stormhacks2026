using Atlas.Link;
using Atlas.Spaces.Slicer;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>The parameters of group "slicer" and the State values (lane A contract, section 14.3).</summary>
    public class SlicerParamsTests
    {
        static SlicerInput Standing(float dt = 1f / 60f)
        {
            return new SlicerInput { DeltaTime = dt };
        }

        [Test]
        public void TheGroup_IsDeclaredAsTheContractSays()
        {
            var registry = new ParamRegistry();
            SlicerParams.Declare(registry);

            ParamEntry source = registry.Find(SlicerParams.Source);
            Assert.That(source.Kind, Is.EqualTo(ParamKind.Enum));
            Assert.That(source.OnRelease, Is.True);
            Assert.That(new[] { source.EnumName(0), source.EnumName(1), source.EnumName(2) },
                        Is.EqualTo(new[] { "live", "stepped", "tesseract" }));

            ParamEntry body = registry.Find(SlicerParams.Object);
            Assert.That(body.OnRelease, Is.True);
            Assert.That(body.EnumCount, Is.EqualTo(SlicerObjects.Count));
            Assert.That(body.EnumName(0), Is.EqualTo("tesseract"));
            Assert.That(body.EnumName(1), Is.EqualTo("cell5"));
            Assert.That(body.EnumName(2), Is.EqualTo("cell16"));
            Assert.That(body.EnumName(3), Is.EqualTo("cell24"));
            Assert.That(body.EnumName(4), Is.EqualTo("tube"));
            Assert.That(body.EnumName(5), Is.EqualTo("ball"));

            ParamEntry view = registry.Find(SlicerParams.View);
            Assert.That(view.EnumCount, Is.EqualTo(2));
            Assert.That(view.OnRelease, Is.False);

            ParamEntry w = registry.Find(SlicerParams.W);
            Assert.That(w.Min, Is.EqualTo(-1.1f));
            Assert.That(w.Max, Is.EqualTo(1.1f));
            Assert.That(w.Default, Is.EqualTo(0f));
            foreach (string name in new[] { SlicerParams.Xw, SlicerParams.Yw, SlicerParams.Zw })
            {
                ParamEntry angle = registry.Find(name);
                Assert.That(angle.Min, Is.EqualTo(-180f), name);
                Assert.That(angle.Max, Is.EqualTo(180f), name);
            }
            Assert.That(registry.Find(SlicerParams.Tilt).Max, Is.EqualTo(80f));

            for (int i = 0; i < registry.Count; i++)
                Assert.That(registry[i].Group, Is.EqualTo("slicer"), registry[i].Name);
            Assert.That(registry.Count, Is.EqualTo(8));
        }

        [Test]
        public void DeclaringAgain_KeepsTheValues()
        {
            var registry = new ParamRegistry();
            SlicerParams.Declare(registry);
            registry.Set(SlicerParams.W, 0.5f);
            registry.Set(SlicerParams.Object, 3f);
            int count = registry.Count;

            SlicerParams again = SlicerParams.Declare(registry);
            Assert.That(registry.Count, Is.EqualTo(count));
            Assert.That(registry.GetFloat(SlicerParams.W), Is.EqualTo(0.5f));
            Assert.That(again.ObjectValue, Is.EqualTo(SlicerObject.Cell24));
        }

        [Test]
        public void TheControllersState_GoesToTheRegistryAndBack()
        {
            var registry = new ParamRegistry();
            SlicerParams parameters = SlicerParams.Declare(registry);
            SlicerLayout layout = SlicerLayout.FromRoom(1.4f, 1.4f, true);
            var controller = new SlicerController(in layout);
            parameters.Load(controller);

            var input = Standing();
            input.AxisW = 1f;
            input.AxisYw = -1f;
            for (int i = 0; i < 30; i++)
            {
                parameters.TakeOutsideChanges(controller);
                controller.Step(in input);
                parameters.Store(controller);
            }
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Stick), "its own writes are not taken for outside changes");
            Assert.That(registry.GetFloat(SlicerParams.W), Is.EqualTo(controller.W));
            Assert.That(registry.GetFloat(SlicerParams.W), Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(registry.GetFloat(SlicerParams.Yw), Is.EqualTo(-30f).Within(1e-3f), "degrees in the registry");
            Assert.That(controller.Yw, Is.EqualTo(math.radians(-30f)).Within(1e-5f), "radians in the controller");

            // A second controller started from the registry is in the same state.
            var second = new SlicerController(in layout);
            SlicerParams.Declare(registry).Load(second);
            Assert.That(second.W, Is.EqualTo(controller.W).Within(1e-6f));
            Assert.That(second.Xw, Is.EqualTo(controller.Xw).Within(1e-6f));
            Assert.That(second.Yw, Is.EqualTo(controller.Yw).Within(1e-6f));
            Assert.That(second.Zw, Is.EqualTo(controller.Zw).Within(1e-6f));
        }

        [Test]
        public void TheStation_SetsTheCutAndTheTurnsLive()
        {
            var registry = new ParamRegistry();
            SlicerParams parameters = SlicerParams.Declare(registry);
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            var controller = new SlicerController(in layout);
            parameters.Load(controller);

            registry.Set(SlicerParams.W, -0.75f, ParamSource.Remote);
            registry.Set(SlicerParams.Zw, 90f, ParamSource.Remote);
            parameters.TakeOutsideChanges(controller);
            controller.Step(Standing());
            parameters.Store(controller);

            Assert.That(controller.W, Is.EqualTo(-0.75f));
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Parameter));
            Assert.That(controller.Zw, Is.EqualTo(math.PI / 2f).Within(1e-6f));
            Assert.That(registry.GetFloat(SlicerParams.W), Is.EqualTo(-0.75f), "the value set is not written over");
            Assert.That(registry.GetFloat(SlicerParams.Zw), Is.EqualTo(90f).Within(1e-4f));

            // Out of range from the Station: the registry clamps, and the controller takes the clamped value.
            registry.Set(SlicerParams.W, 7f, ParamSource.Remote);
            parameters.TakeOutsideChanges(controller);
            Assert.That(controller.W, Is.EqualTo(SlicerObjects.CutRange));
        }

        [Test]
        public void ObjectViewSourceAndTilt_AreReadFromTheRegistry()
        {
            var registry = new ParamRegistry();
            SlicerParams parameters = SlicerParams.Declare(registry);
            Assert.That(parameters.ObjectValue, Is.EqualTo(SlicerObject.Tesseract));
            Assert.That(parameters.ViewValue, Is.EqualTo(SlicerView.Slice));
            Assert.That(parameters.SourceValue, Is.EqualTo(SlicerSource.Live));

            parameters.SetObject(SlicerObject.Ball);
            parameters.SetView(SlicerView.Shadow);
            registry.Set(SlicerParams.Tilt, 45f);
            Assert.That(registry.GetInt(SlicerParams.Object), Is.EqualTo(5));
            Assert.That(parameters.ObjectValue, Is.EqualTo(SlicerObject.Ball));
            Assert.That(parameters.ViewValue, Is.EqualTo(SlicerView.Shadow));
            Assert.That(parameters.TiltRadians, Is.EqualTo(math.PI / 4f).Within(1e-6f));

            // The kill switch: tesseract only, whatever the object parameter says.
            registry.Set(SlicerParams.Source, 2f);
            Assert.That(parameters.ObjectValue, Is.EqualTo(SlicerObject.Tesseract));
            Assert.That(registry.GetInt(SlicerParams.Object), Is.EqualTo(5), "the choice is kept for when the switch is released");
        }

        [Test]
        public void ASnapshot_RestoresTheGroup()
        {
            var registry = new ParamRegistry();
            SlicerParams.Declare(registry);
            registry.Set(SlicerParams.W, 0.33f);
            registry.Set(SlicerParams.Xw, -120f);
            registry.Set(SlicerParams.Object, 2f);
            registry.Set(SlicerParams.View, 1f);
            JsonValue snapshot = registry.ToSnapshot();

            var other = new ParamRegistry();
            SlicerParams restored = SlicerParams.Declare(other);
            other.Restore(snapshot);
            Assert.That(other.GetFloat(SlicerParams.W), Is.EqualTo(0.33f).Within(1e-6f));
            Assert.That(other.GetFloat(SlicerParams.Xw), Is.EqualTo(-120f).Within(1e-4f));
            Assert.That(restored.ObjectValue, Is.EqualTo(SlicerObject.Cell16));
            Assert.That(restored.ViewValue, Is.EqualTo(SlicerView.Shadow));
        }

        [Test]
        public void State_PacksObjectAndView()
        {
            for (int body = 0; body < SlicerObjects.Count; body++)
            {
                foreach (SlicerView view in new[] { SlicerView.Slice, SlicerView.Shadow })
                {
                    float packed = SlicerState.Pack((SlicerObject)body, view);
                    Assert.That(packed, Is.EqualTo(body + 16 * (int)view));
                    SlicerState.Unpack(packed, out SlicerObject gotBody, out SlicerView gotView);
                    Assert.That(gotBody, Is.EqualTo((SlicerObject)body));
                    Assert.That(gotView, Is.EqualTo(view));
                }
            }
            SlicerState.Unpack(float.NaN, out SlicerObject fallback, out SlicerView fallbackView);
            Assert.That(fallback, Is.EqualTo(SlicerObject.Tesseract));
            Assert.That(fallbackView, Is.EqualTo(SlicerView.Slice));
            SlicerState.Unpack(15f, out fallback, out fallbackView);
            Assert.That(fallback, Is.EqualTo(SlicerObject.Tesseract), "an unknown body falls back to the tesseract");
        }
    }
}
