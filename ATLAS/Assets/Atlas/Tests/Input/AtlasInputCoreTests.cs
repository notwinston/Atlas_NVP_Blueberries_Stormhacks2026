using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>The frame snapshot: gamepad rules, contexts, and the twins ending in the same action.</summary>
    public class AtlasInputCoreTests
    {
        private const float Dt = 1f / 60f;
        private static readonly float3 Head = new float3(0f, 1.6f, 0f);
        private static readonly quaternion Away = quaternion.RotateY(math.radians(150f));

        private AtlasInputCore _input;
        private List<(AtlasAction action, InputSource source, int tag)> _events;

        [SetUp]
        public void SetUp()
        {
            _input = new AtlasInputCore();
            _events = new List<(AtlasAction, InputSource, int)>();
            _input.ActionFired += (a, s, t) => _events.Add((a, s, t));
            _input.PushContext(InputContext.Space);
        }

        private static RawInput Pad(params AtlasAction[] down)
        {
            var raw = new RawInput { GamepadPresent = true };
            foreach (AtlasAction a in down) raw.PadDown |= InputTwins.Bit(a);
            return raw;
        }

        private static RawInput Keys(params AtlasAction[] down)
        {
            var raw = new RawInput();
            foreach (AtlasAction a in down) raw.KeyDown |= InputTwins.Bit(a);
            return raw;
        }

        // buttonEast is bound to both of its actions, so the adapter reports both bits.
        private static RawInput East => Pad(AtlasAction.Back, AtlasAction.ReturnHome);

        private void Tick(RawInput raw, int frames = 1, quaternion? rotation = null)
        {
            for (int i = 0; i < frames; i++) _input.Tick(raw, Head, rotation ?? Away, Dt);
        }

        private List<AtlasAction> Fired()
        {
            var list = new List<AtlasAction>();
            foreach (var e in _events) list.Add(e.action);
            return list;
        }

        [Test]
        public void NothingHappensWithNoDevices()
        {
            Tick(default, 120);
            Assert.That(_events, Is.Empty);
            Assert.That(_input.GamepadPresent, Is.False);
            foreach (AtlasAction a in (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction)))
            {
                Assert.That(_input.Pressed(a), Is.False);
                Assert.That(_input.Held(a), Is.False);
                Assert.That(_input.LastSource(a), Is.EqualTo(InputSource.None));
            }
            foreach (AtlasAxis a in (AtlasAxis[])System.Enum.GetValues(typeof(AtlasAxis)))
                Assert.That(_input.Axis(a), Is.EqualTo(0f));
            Assert.That(_input.Pressed((AtlasAction)250), Is.False, "an unknown value is harmless");
            Assert.That(_input.Axis((AtlasAxis)250), Is.EqualTo(0f));
        }

        [Test]
        public void AButtonIsPressedForOneFrameAndHeldWhileDown()
        {
            Tick(Pad(AtlasAction.Primary));
            Assert.That(_input.Pressed(AtlasAction.Primary), Is.True);
            Assert.That(_input.Held(AtlasAction.Primary), Is.True);
            Assert.That(_input.LastSource(AtlasAction.Primary), Is.EqualTo(InputSource.Gamepad));
            Assert.That(_input.GamepadPresent, Is.True);

            Tick(Pad(AtlasAction.Primary));
            Assert.That(_input.Pressed(AtlasAction.Primary), Is.False);
            Assert.That(_input.Held(AtlasAction.Primary), Is.True);

            Tick(Pad());
            Assert.That(_input.Held(AtlasAction.Primary), Is.False);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.Primary }));
        }

        [Test]
        public void EastTappedIsBackAndHeldIsReturnHome()
        {
            Tick(East, 10);                       // 0.17 s
            Assert.That(_events, Is.Empty, "Back waits for the release");
            Tick(Pad());
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.Back }));
            Assert.That(_input.Pressed(AtlasAction.Back), Is.True);

            _events.Clear();
            Tick(East, 50);                       // 0.83 s: too long for Back, too short for home
            Tick(Pad(), 2);
            Assert.That(_events, Is.Empty);

            Tick(East, 59);
            Assert.That(_events, Is.Empty);
            Tick(East, 5);                        // past 1.0 s
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.ReturnHome }));
            Tick(East, 120);
            Tick(Pad(), 2);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.ReturnHome }), "once per hold, and no Back on the release");
        }

        [Test]
        public void StartIsTheSpaceMenuAndWithSelectTheParamMenu()
        {
            RawInput start = Pad(AtlasAction.SpaceMenu, AtlasAction.ParamMenu);
            Tick(start);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.SpaceMenu }));
            Tick(Pad());

            _events.Clear();
            RawInput select = Pad();
            select.SelectDown = true;
            Tick(select, 5);
            start.SelectDown = true;
            Tick(start, 5);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.ParamMenu }));
        }

        [Test]
        public void TheDpadSwitchesSpacesInASpaceAndNavigatesInAMenu()
        {
            RawInput up = Pad(AtlasAction.NextSpace, AtlasAction.NavUp);
            Tick(up);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.NextSpace }));
            Tick(Pad());

            _events.Clear();
            _input.PushContext(InputContext.Menu);
            Tick(up);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.NavUp }));
            Assert.That(_input.Pressed(AtlasAction.NextSpace), Is.False);
            Tick(Pad());

            _input.PopContext(InputContext.Menu);
            Assert.That(_input.Context, Is.EqualTo(InputContext.Space));
        }

        [Test]
        public void MenuNavigationRepeats()
        {
            _input.PushContext(InputContext.Menu);
            RawInput down = Pad(AtlasAction.PrevSpace, AtlasAction.NavDown);
            var times = new List<float>();
            for (int i = 0; i < 60; i++)
            {
                Tick(down);
                if (_input.Pressed(AtlasAction.NavDown)) times.Add(i * Dt);
            }
            // 0, 0.4, 0.55, 0.70, 0.85 within the first second.
            Assert.That(times.Count, Is.EqualTo(5));
            Assert.That(times[0], Is.EqualTo(0f));
            Assert.That(times[1], Is.EqualTo(0.4f).Within(Dt + 1e-4f));
            for (int i = 2; i < times.Count; i++)
                Assert.That(times[i] - times[i - 1], Is.EqualTo(0.15f).Within(Dt + 1e-4f));
        }

        [Test]
        public void InTheHandNothingFiresAndATouchIsATap()
        {
            _input.PopContext(InputContext.Space);
            Assert.That(_input.Context, Is.EqualTo(InputContext.InHand));

            RawInput raw = Pad(AtlasAction.Confirm, AtlasAction.Primary);
            raw.PointerDown = true;
            raw.PointerPosition = new float2(320f, 200f);
            raw.LeftStick = new float2(1f, 0f);
            Tick(raw);
            Assert.That(_events, Is.Empty);
            Assert.That(_input.Held(AtlasAction.Confirm), Is.False);
            Assert.That(_input.Axis(AtlasAxis.LeftX), Is.EqualTo(0f));
            Assert.That(_input.TryGetTap(out float2 p), Is.True);
            Assert.That(p, Is.EqualTo(new float2(320f, 200f)));

            Tick(raw);
            Assert.That(_input.TryGetTap(out _), Is.False, "only the frame in which the touch began");

            // Going into the headset with the buttons still down fires nothing.
            _input.PushContext(InputContext.Space);
            Tick(raw, 3);
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void TheLeverConfirmsOrFiresTheTargetUnderTheGaze()
        {
            var touch = new RawInput { PointerDown = true };
            Tick(touch);
            Assert.That(_events, Is.EqualTo(new[] { (AtlasAction.Confirm, InputSource.Lever, 0) }));
            Assert.That(_input.TryGetTap(out _), Is.False, "in the headset a touch is the lever, not a tap");
            Tick(touch, 30);
            Assert.That(_events.Count, Is.EqualTo(1), "a held lever fires once");
            Tick(default);

            _events.Clear();
            var t = new GazeTargetData
            {
                Action = AtlasAction.NextSpace, Space = GazeSpace.World, Centre = Head + new float3(0f, 0f, 2f),
                Radius = 0.2f, Context = InputContext.Space, Tag = 3
            };
            _input.Targets.Add(t);
            Tick(default, 5, quaternion.identity);
            Tick(touch, 1, quaternion.identity);
            Assert.That(_events, Is.EqualTo(new[] { (AtlasAction.NextSpace, InputSource.Lever, 3) }));
            Assert.That(_input.LastTag(AtlasAction.NextSpace), Is.EqualTo(3));
        }

        [Test]
        public void EveryActionCanBeDoneWithTheGamepadAloneAndWithGazeAlone()
        {
            foreach (AtlasAction action in (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction)))
            {
                if (action == AtlasAction.None) continue;
                InputContext context = InputTwins.Of(action).InSpace ? InputContext.Space : InputContext.Menu;

                // Gamepad alone, nothing under the gaze.
                var pad = new AtlasInputCore();
                pad.PushContext(context);
                bool byPad = false;
                pad.ActionFired += (a, s, t) => byPad |= a == action && s == InputSource.Gamepad;
                RawInput raw = Pad(action);
                if (action == AtlasAction.ParamMenu)
                {
                    raw = Pad(AtlasAction.SpaceMenu, AtlasAction.ParamMenu);
                    raw.SelectDown = true;
                }
                if (action == AtlasAction.Back || action == AtlasAction.ReturnHome) raw = East;
                int holdFrames = action == AtlasAction.ReturnHome ? 70 : 3;
                for (int i = 0; i < holdFrames; i++) pad.Tick(raw, Head, Away, Dt);
                pad.Tick(Pad(), Head, Away, Dt);
                Assert.That(byPad, Is.True, action + " by gamepad alone");

                // Gaze alone, no device at all.
                var gaze = new AtlasInputCore();
                gaze.PushContext(context);
                bool byGaze = false;
                gaze.ActionFired += (a, s, t) => byGaze |= a == action && s == InputSource.Gaze;
                gaze.Targets.Add(new GazeTargetData
                {
                    Action = action, Space = GazeSpace.World, Centre = Head + new float3(0f, 0f, 2f), Radius = 0.1f,
                    Context = context
                });
                bool pressed = false;
                for (int i = 0; i < 60; i++)
                {
                    gaze.Tick(default, Head, quaternion.identity, Dt);
                    pressed |= gaze.Pressed(action);
                }
                Assert.That(byGaze, Is.True, action + " by gaze dwell alone");
                Assert.That(pressed, Is.True, action + " reads as Pressed from the gaze twin");
                Assert.That(gaze.LastSource(action), Is.EqualTo(InputSource.Gaze));
            }
        }

        [Test]
        public void TheKeyboardFiresEveryAction()
        {
            foreach (AtlasAction action in (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction)))
            {
                if (action == AtlasAction.None) continue;
                var input = new AtlasInputCore();
                input.PushContext(InputTwins.Of(action).InSpace ? InputContext.Space : InputContext.Menu);
                input.Tick(Keys(action), Head, Away, Dt);
                Assert.That(input.Pressed(action), Is.True, action.ToString());
                Assert.That(input.LastSource(action), Is.EqualTo(InputSource.Keyboard));
            }
        }

        [Test]
        public void AxesComeFromTheSticksTheBumpersAndTheTriggers()
        {
            RawInput raw = Pad(AtlasAction.VariantNext);
            raw.LeftStick = new float2(0.25f, -0.5f);
            raw.RightStick = new float2(-1f, 2f);
            raw.Twist = -0.75f;
            Tick(raw);
            Assert.That(_input.Axis(AtlasAxis.Sweep), Is.EqualTo(1f));
            Assert.That(_input.Axis(AtlasAxis.Twist), Is.EqualTo(-0.75f));
            Assert.That(_input.Axis(AtlasAxis.LeftX), Is.EqualTo(0.25f));
            Assert.That(_input.Axis(AtlasAxis.LeftY), Is.EqualTo(-0.5f));
            Assert.That(_input.Axis(AtlasAxis.RightX), Is.EqualTo(-1f));
            Assert.That(_input.Axis(AtlasAxis.RightY), Is.EqualTo(1f), "clamped");

            Tick(Pad(AtlasAction.VariantNext, AtlasAction.VariantPrev));
            Assert.That(_input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));

            // In a menu the left stick navigates: the axes it shares are silent.
            _input.PushContext(InputContext.Menu);
            Tick(raw);
            Assert.That(_input.Axis(AtlasAxis.LeftX), Is.EqualTo(0f));
            Assert.That(_input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));
            Assert.That(_input.Axis(AtlasAxis.RightX), Is.EqualTo(-1f));
        }

        [Test]
        public void AHoldTargetDrivesItsAxisOrHoldsItsAction()
        {
            var axis = new GazeTargetData
            {
                Axis = AtlasAxis.Sweep, AxisValue = -1f, Mode = GazeMode.Hold, Space = GazeSpace.World,
                Centre = Head + new float3(0f, 0f, 2f), Radius = 0.1f, Context = InputContext.Space
            };
            GazeTargetId id = _input.Targets.Add(axis);
            Tick(default, 30, quaternion.identity);
            Assert.That(_input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));
            Tick(default, 30, quaternion.identity);
            Assert.That(_input.Axis(AtlasAxis.Sweep), Is.EqualTo(-1f));
            Assert.That(_events, Is.Empty);
            Tick(default, 20);
            Assert.That(_input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));
            _input.Targets.Remove(id);

            var hold = new GazeTargetData
            {
                Action = AtlasAction.Secondary, Mode = GazeMode.Hold, Space = GazeSpace.World,
                Centre = Head + new float3(0f, 0f, 2f), Radius = 0.1f, Context = InputContext.Space
            };
            _input.Targets.Add(hold);
            Tick(default, 60, quaternion.identity);
            Assert.That(_input.Held(AtlasAction.Secondary), Is.True);
            Assert.That(Fired(), Is.EqualTo(new[] { AtlasAction.Secondary }), "pressed once when the hold begins");
            Tick(default, 20);
            Assert.That(_input.Held(AtlasAction.Secondary), Is.False);
        }

        [Test]
        public void ReportsPolledActionsThatHaveNoGazeTwin()
        {
            var missing = new List<AtlasAction>();
            var missingAxes = new List<AtlasAxis>();
            Tick(default);
            _input.Pressed(AtlasAction.Primary);
            _input.Held(AtlasAction.Secondary);
            _input.Pressed(AtlasAction.NavUp);             // not live in a space: not reported
            _input.Axis(AtlasAxis.Sweep);
            _input.CopyActionsWithoutGazeTwin(missing);
            _input.CopyAxesWithoutGazeTwin(missingAxes);
            Assert.That(missing, Is.EquivalentTo(new[] { AtlasAction.Primary, AtlasAction.Secondary }));
            Assert.That(missingAxes, Is.EqualTo(new[] { AtlasAxis.Sweep }));

            _input.Targets.Add(new GazeTargetData { Action = AtlasAction.Primary, Radius = 0.1f, Context = InputContext.Space });
            GazeTargetId look = _input.Targets.Add(new GazeTargetData
            {
                Action = AtlasAction.Secondary, Space = GazeSpace.LookDown, PitchBelowDeg = -35f, Context = InputContext.Space
            });
            _input.Targets.Add(new GazeTargetData { Axis = AtlasAxis.Sweep, AxisValue = 1f, Mode = GazeMode.Hold, Radius = 0.1f, Context = InputContext.Space });
            _input.CopyActionsWithoutGazeTwin(missing);
            _input.CopyAxesWithoutGazeTwin(missingAxes);
            Assert.That(missing, Is.Empty);
            Assert.That(missingAxes, Is.Empty);

            _input.Targets.SetEnabled(look, false);
            _input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.EqualTo(new[] { AtlasAction.Secondary }), "a disabled target is no twin");

            // An action nobody polls any more drops off the report after a frame.
            Tick(default, 2);
            _input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.Empty);

            // Menu rows are the twin of both navigation directions.
            _input.PushContext(InputContext.Menu);
            Tick(default);
            _input.Pressed(AtlasAction.NavUp);
            _input.Pressed(AtlasAction.NavDown);
            _input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.EquivalentTo(new[] { AtlasAction.NavUp, AtlasAction.NavDown }));
            _input.Targets.Add(new GazeTargetData { Action = AtlasAction.NavDown, Radius = 0.1f, Context = InputContext.Menu, Tag = 2 });
            _input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void ContextsNest()
        {
            Assert.That(_input.Context, Is.EqualTo(InputContext.Space));
            _input.PushContext(InputContext.Menu);
            _input.PushContext(InputContext.Menu);
            _input.PopContext(InputContext.Menu);
            Assert.That(_input.Context, Is.EqualTo(InputContext.Menu));
            _input.PopContext(InputContext.Space);          // a rescan while a menu is open: the menu stays on top
            Assert.That(_input.Context, Is.EqualTo(InputContext.Menu));
            _input.PopContext(InputContext.Menu);
            Assert.That(_input.Context, Is.EqualTo(InputContext.InHand));
            _input.PopContext(InputContext.Menu);           // nothing to pop: harmless
            Assert.That(_input.Context, Is.EqualTo(InputContext.InHand));
            for (int i = 0; i < 20; i++) _input.PushContext(InputContext.Space);
            Assert.That(_input.Context, Is.EqualTo(InputContext.Space));
        }

        [Test]
        public void DwellSecondsIsClamped()
        {
            Assert.That(_input.DwellSeconds, Is.EqualTo(0.7f));
            _input.DwellSeconds = 0f;
            Assert.That(_input.DwellSeconds, Is.EqualTo(0.6f));
            _input.DwellSeconds = 2f;
            Assert.That(_input.DwellSeconds, Is.EqualTo(0.8f));
        }

        [Test]
        public void ATickAllocatesNothing()
        {
            for (int i = 0; i < 8; i++)
                _input.Targets.Add(new GazeTargetData
                {
                    Action = AtlasAction.Confirm, Centre = Head + new float3(i - 4f, 0f, 3f), Radius = 0.2f,
                    Shape = i % 2 == 0 ? GazeShape.Sphere : GazeShape.Rect, HalfSize = new float2(0.2f, 0.1f),
                    Mode = GazeMode.Repeat, Context = InputContext.Space
                });
            RawInput raw = Pad(AtlasAction.Primary);
            for (int i = 0; i < 200; i++) _input.Tick(i % 2 == 0 ? raw : default, Head, quaternion.identity, Dt);   // warm up

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                _input.Tick(i % 2 == 0 ? raw : default, Head, quaternion.RotateY(i * 0.01f), Dt);
                _input.Pressed(AtlasAction.Primary);
                _input.Axis(AtlasAxis.Sweep);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            // The handler of SetUp adds to a list; its growth is the test's, so allow for that and nothing more.
            Assert.That(allocated, Is.LessThan(256 * 1024), "bytes allocated by 2000 ticks");
        }
    }
}
