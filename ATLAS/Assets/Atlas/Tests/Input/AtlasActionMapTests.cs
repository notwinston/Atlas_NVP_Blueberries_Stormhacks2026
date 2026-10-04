#if !ATLAS_HEADLESS
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Atlas.Input.Tests
{
    /// <summary>The action map as the Input System builds it: needs the engine.</summary>
    [Category("Unity")]
    public class AtlasActionMapTests
    {
        [Test]
        public void TheMapHasOneActionPerAtlasActionWithItsTwinBindings()
        {
            using (var map = new AtlasActionMap())
            {
                Assert.That(map.Map.name, Is.EqualTo("Atlas"));
                Assert.That(map.ActionFor(AtlasAction.None), Is.Null);
                foreach (AtlasAction action in (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction)))
                {
                    if (action == AtlasAction.None) continue;
                    InputAction a = map.ActionFor(action);
                    Assert.That(a, Is.Not.Null, action.ToString());
                    Assert.That(a.name, Is.EqualTo(action.ToString()));
                    Assert.That(a.type, Is.EqualTo(InputActionType.Button));
                    Assert.That(map.Map.FindAction(action.ToString()), Is.SameAs(a));

                    ActionTwin twin = InputTwins.Of(action);
                    bool pad = false, key = false;
                    foreach (InputBinding b in a.bindings)
                    {
                        pad |= b.path == twin.GamepadPath;
                        key |= b.path == twin.KeyPath;
                    }
                    Assert.That(pad, Is.True, action + " gamepad binding");
                    Assert.That(key, Is.True, action + " Editor key binding");
                }
            }
        }

        [Test]
        public void TheLeverIsAScreenTouch()
        {
            using (var map = new AtlasActionMap())
            {
                InputAction lever = map.Map.FindAction("Lever");
                Assert.That(lever, Is.Not.Null);
                Assert.That(lever.bindings[0].path, Is.EqualTo("<Touchscreen>/primaryTouch/press"));
            }
        }

        [Test]
        public void SamplingWithNoDeviceGivesZeros()
        {
            using (var map = new AtlasActionMap())
            {
                map.Enable();
                Assert.That(map.Map.enabled, Is.True);
                var raw = new RawInput();
                var walker = new WalkerInput();
                map.Sample(ref raw, ref walker);
                Assert.That(raw.Twist, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(walker.Speed, Is.GreaterThan(0f));
                map.Disable();
                Assert.That(map.Map.enabled, Is.False);
            }
        }

        [Test]
        public void InstallWithoutAHudDrawsNoReticleAndTicksSafely()
        {
            IAtlasInput input = AtlasInput.Install(null, null);
            try
            {
                Assert.That(input.Context, Is.EqualTo(InputContext.InHand));
                input.PushContext(InputContext.Space);
                GazeTargetId id = input.RegisterGazeTarget(GazeTarget.World(AtlasAction.Primary, new UnityEngine.Vector3(0f, 1.6f, 2f), 0.1f));
                Assert.That(id.IsValid, Is.True);
                int fired = 0;
                input.ActionFired += (a, s, t) => { if (a == AtlasAction.Primary && s == InputSource.Gaze) fired++; };
                for (int i = 0; i < 60; i++)
                    input.Tick(new UnityEngine.Vector3(0f, 1.6f, 0f), UnityEngine.Quaternion.identity, 1f / 60f);
                Assert.That(fired, Is.EqualTo(1));
                Assert.That(input.Gaze.Focused, Is.EqualTo(id));
                input.SetRumble(0.5f, 0.5f);
                input.UnregisterGazeTarget(id);
            }
            finally
            {
                ((System.IDisposable)input).Dispose();
            }
        }
    }
}
#endif
