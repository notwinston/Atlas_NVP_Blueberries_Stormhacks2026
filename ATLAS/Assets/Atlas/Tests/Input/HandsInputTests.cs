using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>
    /// Hands beside the routes input already has (documents/ATLAS_Hands_Design.md sections 6.3 and 6.7): a touch
    /// is a twin of the dwell, the dwell waits for a finger that is on its way, a pinch fires what the gaze is
    /// on, and without hands nothing is different.
    /// </summary>
    public class HandsInputTests
    {
        private const float Dt = HandsRig.Dt;
        /// <summary>A beacon out in the room, too far to touch.</summary>
        private static readonly float3 Beacon = HandsRig.Eye + new float3(0f, 0f, 2f);
        /// <summary>A hand held up in view, over no target.</summary>
        private static readonly float3 Aside = HandsRig.Eye + new float3(0.30f, -0.30f, 0.30f);

        [Test]
        public void Input_Hands_AreATwinOfDwell()
        {
            // By dwell: the walker looks at the chip.
            var byDwell = new HandsRig();
            GazeTargetId dwelt = byDwell.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel, tag: 7));
            byDwell.LookAt(HandsRig.Panel);
            bool pressedByDwell = false;
            for (int i = 0; i < 60; i++)
            {
                byDwell.NoHands(Dt);
                pressedByDwell |= byDwell.Input.Pressed(AtlasAction.Primary);
            }

            // By touch: the walker looks elsewhere and presses the same chip.
            var byTouch = new HandsRig();
            GazeTargetId touched = byTouch.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel, tag: 7));
            HandsRig.Face face = byTouch.FaceOf(touched);
            bool pressedByTouch = false;
            int frames = HandsRig.Frames(0.3f);
            for (int i = 0; i <= frames; i++)
            {
                byTouch.Right(face.At(0f, 0f, math.lerp(0.10f, -0.02f, i / (float)frames)));
                pressedByTouch |= byTouch.Input.Pressed(AtlasAction.Primary);
            }

            Assert.That(byDwell.Events, Is.EqualTo(new[] { (AtlasAction.Primary, InputSource.Gaze, 7) }));
            Assert.That(byTouch.Events, Is.EqualTo(byDwell.Events), "the same action, source and tag");
            Assert.That(byDwell.Calls, Is.EqualTo(new[] { (dwelt, 7) }));
            Assert.That(byTouch.Calls, Is.EqualTo(new[] { (touched, 7) }), "the same callback, with the target and its tag");
            Assert.That(pressedByDwell && pressedByTouch, Is.True, "both read as Pressed");
            Assert.That(byTouch.Input.LastSource(AtlasAction.Primary), Is.EqualTo(byDwell.Input.LastSource(AtlasAction.Primary)));
            Assert.That(byTouch.Input.LastTag(AtlasAction.Primary), Is.EqualTo(byDwell.Input.LastTag(AtlasAction.Primary)));

            // Hands are no column of the twin table and no new source (decision R1).
            Assert.That(InputTwins.IsComplete(), Is.True);
            Assert.That(System.Enum.GetNames(typeof(InputSource)), Is.EqualTo(new[] { "None", "Gamepad", "Gaze", "Lever", "Keyboard" }));
        }

        [Test]
        public void Input_Hands_ATouchResetsTheDwellAsTheLeverDoes()
        {
            // Once: the gaze rests on the chip while the finger presses it. One firing, and the dwell does not add another.
            var rig = new HandsRig();
            GazeTargetId chip = rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = rig.FaceOf(chip);
            rig.LookAt(HandsRig.Panel);
            rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(rig.Events.Count, Is.EqualTo(1));
            Assert.That(rig.RightFired, Is.EqualTo(1), "fired by the touch, before the dwell was through");
            Assert.That(rig.Input.GazeProgress, Is.EqualTo(0f), "the dwell starts over");
            rig.Rest(face.At(0f, 0f, -0.02f), 0.3f);
            rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.10f), 0.2f);
            rig.NoHands(3f);
            Assert.That(rig.Events.Count, Is.EqualTo(1), "spent, as after the lever: the gaze must leave it first");
            // The gaze leaves and returns: it can be dwelt on again.
            rig.Looking = HandsRig.LookingUp;
            rig.NoHands(0.5f);
            rig.LookAt(HandsRig.Panel);
            rig.NoHands(1f);
            Assert.That(rig.Events.Count, Is.EqualTo(2));

            // Repeat: the gaze has been stepping the target; a touch steps it once and the dwell's interval starts over.
            var stepping = new HandsRig();
            GazeTargetId plus = stepping.Add(HandsRig.Chip(AtlasAction.NavRight, HandsRig.Panel, mode: GazeMode.Repeat));
            HandsRig.Face plusFace = stepping.FaceOf(plus);
            stepping.LookAt(HandsRig.Panel);
            stepping.NoHands(1.6f);                                   // 0.7, 1.1, 1.5
            Assert.That(stepping.Events.Count, Is.EqualTo(3));
            stepping.Glide(plusFace.At(0f, 0f, 0.25f), plusFace.At(0f, 0f, 0.10f), 0.6f);     // on its way: the dwell waits
            stepping.Glide(plusFace.At(0f, 0f, 0.10f), plusFace.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(stepping.Events.Count, Is.EqualTo(4), "one step from the touch, none from the dwell meanwhile");
            Assert.That(stepping.RightFired, Is.EqualTo(1));
            stepping.Glide(plusFace.At(0f, 0f, -0.02f), plusFace.At(0f, 0f, 0.25f), 0.3f);
            int before = stepping.Events.Count;
            float left = stepping.Time;
            while (stepping.Events.Count == before && stepping.Time < left + 2f) stepping.NoHands(Dt);
            Assert.That(stepping.Time - left, Is.EqualTo(0.4f).Within(0.1f), "a whole interval after the hand has gone");
        }

        [Test]
        public void Input_Hands_DwellPausesNearAFinger()
        {
            var rig = new HandsRig();
            GazeTargetId chip = rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = rig.FaceOf(chip);
            rig.LookAt(HandsRig.Panel);

            rig.NoHands(0.3f);
            float progress = rig.Input.GazeProgress;
            Assert.That(progress, Is.InRange(0.3f, 0.5f));
            Assert.That(rig.Input.HandsTouch.AnyNear, Is.False);

            // A fingertip 11 cm from the chip, beside it and over nothing: for three seconds the dwell stands still.
            float3 beside = face.At(0.15f, 0f, 0.05f);
            rig.Rest(beside, 3f);
            Assert.That(rig.Events, Is.Empty);
            Assert.That(rig.Input.GazeProgress, Is.EqualTo(progress), "it does not advance, and it is not lost");
            Assert.That(rig.Input.HandsTouch.AnyNear, Is.True);
            Assert.That(rig.R.Target.IsValid, Is.False, "near is not hovering");

            // The hand goes: the dwell runs on from where it stood.
            float gone = rig.Time;
            while (rig.Events.Count == 0 && rig.Time < gone + 2f) rig.NoHands(Dt);
            Assert.That(rig.Events.Count, Is.EqualTo(1));
            Assert.That(rig.Time - gone, Is.EqualTo(0.7f * (1f - progress)).Within(2f * Dt));

            // 25 cm away is not near: the dwell fires on time.
            Assert.That(SecondsToDwell(face.At(0.30f, 0f, 0.05f), 1f), Is.EqualTo(0.7f).Within(2f * Dt));
            // 19 cm away is.
            Assert.That(SecondsToDwell(face.At(0.24f, 0f, 0.02f), 1f), Is.EqualTo(float.PositiveInfinity));
            // A hand that is not trusted holds nothing up.
            Assert.That(SecondsToDwell(beside, 0.5f), Is.EqualTo(0.7f).Within(2f * Dt));
        }

        /// <summary>Looks at a chip on the panel with a fingertip held at a point; returns when the dwell fired, or infinity if it did not in two seconds.</summary>
        private static float SecondsToDwell(float3 fingertip, float confidence)
        {
            var rig = new HandsRig();
            rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel));
            rig.LookAt(HandsRig.Panel);
            while (rig.Time < 2f)
            {
                rig.Right(fingertip, confidence);
                if (rig.Events.Count > 0) return rig.Time;
            }
            return float.PositiveInfinity;
        }

        [Test]
        public void Input_Hands_DwellPausesOnlyOnTheTargetTheFingerIsNear()
        {
            // The gaze is on a beacon across the room; a fingertip is at a near row. The beacon's dwell does not wait.
            var rig = new HandsRig();
            GazeTargetId far = rig.Add(HandsRig.Chip(AtlasAction.NextSpace, Beacon, radius: 0.2f));
            GazeTargetId row = rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = rig.FaceOf(row);
            rig.LookAt(Beacon);
            rig.Rest(face.At(0f, 0f, 0.05f), 1f);
            Assert.That(rig.Input.GazeFocused, Is.EqualTo(far));
            Assert.That(rig.Input.HandsTouch.AnyNear, Is.True);
            Assert.That(rig.Fired(), Is.EqualTo(new[] { AtlasAction.NextSpace }));

            // A Repeat target stops stepping from the gaze while a finger is near it, and goes on when the finger has gone.
            var stepping = new HandsRig();
            GazeTargetId plus = stepping.Add(HandsRig.Chip(AtlasAction.NavRight, HandsRig.Panel, mode: GazeMode.Repeat));
            HandsRig.Face plusFace = stepping.FaceOf(plus);
            stepping.LookAt(HandsRig.Panel);
            stepping.NoHands(1.2f);                                   // 0.7, 1.1
            Assert.That(stepping.Events.Count, Is.EqualTo(2));
            stepping.Rest(plusFace.At(0.15f, 0f, 0.05f), 2f);
            Assert.That(stepping.Events.Count, Is.EqualTo(2));
            stepping.NoHands(0.5f);
            Assert.That(stepping.Events.Count, Is.EqualTo(3));
        }

        [Test]
        public void Input_Hands_PinchFiresTheGazedTarget()
        {
            var rig = new HandsRig();
            GazeTargetId far = rig.Add(HandsRig.Chip(AtlasAction.NextSpace, Beacon, tag: 3, radius: 0.2f));
            GazeTargetId row = rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = rig.FaceOf(row);
            var free = new HandsInput { Active = true, Right = HandsRig.Tip(Aside) };
            var pinch = new HandsInput { Active = true, Right = HandsRig.Tip(Aside), PinchDownRight = true };

            // Nothing under the gaze: a pinch fires nothing, not even Confirm.
            rig.Tick(pinch);
            Assert.That(rig.Events, Is.Empty);
            Assert.That(rig.Input.HandsTouch.PinchFired, Is.False);
            Assert.That(rig.Input.HandsTouch.PinchTarget.IsValid, Is.False);

            // The gaze on the beacon: the pinch fires it at once, as the gaze itself would have after its dwell.
            rig.LookAt(Beacon);
            rig.Tick(free);
            Assert.That(rig.Input.GazeFocused, Is.EqualTo(far));
            rig.Tick(pinch);
            Assert.That(rig.Events, Is.EqualTo(new[] { (AtlasAction.NextSpace, InputSource.Gaze, 3) }));
            Assert.That(rig.Calls, Is.EqualTo(new[] { (far, 3) }));
            Assert.That(rig.Input.Pressed(AtlasAction.NextSpace), Is.True);
            Assert.That(rig.Input.HandsTouch.PinchFired, Is.True);
            Assert.That(rig.Input.HandsTouch.PinchTarget, Is.EqualTo(far));
            rig.Tick(free);
            Assert.That(rig.Input.HandsTouch.PinchFired, Is.False, "for the frame it fired");
            // The target is spent as after the lever: the dwell does not fire it again while the gaze stays.
            rig.Rest(Aside, 2f);
            Assert.That(rig.Events.Count, Is.EqualTo(1));

            // While that hand's fingertip hovers a target its pinch is not a pinch: touch wins.
            float3 hovering = face.At(0.05f, 0f, 0.04f);
            rig.Tick(new HandsInput { Active = true, Right = HandsRig.Tip(hovering), PinchDownRight = true });
            Assert.That(rig.R.Target, Is.EqualTo(row));
            Assert.That(rig.Events.Count, Is.EqualTo(1));
            Assert.That(rig.Input.HandsTouch.PinchFired, Is.False);
            // The other hand, over nothing, is free to pinch.
            rig.Tick(new HandsInput { Active = true, Left = HandsRig.Tip(Aside), Right = HandsRig.Tip(hovering), PinchDownLeft = true });
            Assert.That(rig.Events.Count, Is.EqualTo(2));
            Assert.That(rig.Events[1], Is.EqualTo((AtlasAction.NextSpace, InputSource.Gaze, 3)));

            // A hand that is not trusted, and hands that are not active, do not pinch.
            rig.Rest(Aside, 1f);
            rig.Tick(new HandsInput { Active = true, Right = HandsRig.Tip(Aside, 0.5f), PinchDownRight = true });
            rig.Tick(new HandsInput { Active = true, Right = new HandsFingertip { Valid = false, World = Aside, Confidence = 1f }, PinchDownRight = true });
            rig.Tick(new HandsInput { Active = false, Right = HandsRig.Tip(Aside), PinchDownRight = true });
            Assert.That(rig.Events.Count, Is.EqualTo(2));

            // The lever is still the lever: with a pinch in the same frame the firing is the lever's.
            rig.Tick(new RawInput { PointerDown = true, Hands = pinch });
            Assert.That(rig.Events.Count, Is.EqualTo(3));
            Assert.That(rig.Events[2], Is.EqualTo((AtlasAction.NextSpace, InputSource.Lever, 3)));
            Assert.That(rig.Input.HandsTouch.PinchFired, Is.False);
        }

        [Test]
        public void Input_Hands_APinchJustAfterTheDwellFiredIsSwallowed()
        {
            var rig = new HandsRig();
            rig.Add(HandsRig.Chip(AtlasAction.NextSpace, Beacon, radius: 0.2f));
            rig.LookAt(Beacon);
            rig.Rest(Aside, 0.75f);
            Assert.That(rig.Events.Count, Is.EqualTo(1), "the dwell got there first");
            var pinch = new HandsInput { Active = true, Right = HandsRig.Tip(Aside), PinchDownRight = true };
            rig.Tick(pinch);
            Assert.That(rig.Events.Count, Is.EqualTo(1), "the pinch that was already closing is not a second firing");
            rig.Rest(Aside, 0.6f);
            rig.Tick(pinch);
            Assert.That(rig.Events.Count, Is.EqualTo(2), "a later one is");

            // The same after a touch: one hand presses a chip the walker is looking at, the other pinches in that moment.
            var both = new HandsRig();
            GazeTargetId chip = both.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = both.FaceOf(chip);
            both.LookAt(HandsRig.Panel);
            both.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(both.Events.Count, Is.EqualTo(1));
            var leftPinch = new HandsInput { Active = true, Left = HandsRig.Tip(Aside), Right = HandsRig.Tip(face.At(0f, 0f, -0.02f)), PinchDownLeft = true };
            both.Tick(leftPinch);
            Assert.That(both.Events.Count, Is.EqualTo(1), "the press and the pinch were one intent");
            both.Rest(face.At(0f, 0f, -0.02f), 0.6f);
            both.Tick(leftPinch);
            Assert.That(both.Events.Count, Is.EqualTo(2));
        }

        [Test]
        public void Input_Hands_TheLabelAndTagOfAFiredTargetOutliveIt()
        {
            var rig = new HandsRig();
            GazeTargetData close = HandsRig.Row(AtlasAction.Back, HandsRig.Panel, tag: 12);
            close.Label = "menu close";
            GazeTargetId id = default;
            // The menu's close chip removes itself when it fires.
            close.Callback = (fired, tag) => rig.Input.Targets.Remove(fired);
            id = rig.Input.Targets.Add(close);
            HandsRig.Face face = rig.FaceOf(id);
            Assert.That(rig.Input.LabelOf(id), Is.EqualTo("menu close"));
            Assert.That(rig.Input.TagOf(id), Is.EqualTo(12));

            rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.011f), 0.3f);
            Assert.That(rig.R.Fired, Is.True);
            Assert.That(rig.R.Target, Is.EqualTo(id));
            Assert.That(rig.Input.Targets.Contains(id), Is.False, "gone in its own callback");
            Assert.That(rig.Input.LabelOf(rig.R.Target), Is.EqualTo("menu close"), "the event log can still name what was touched");
            Assert.That(rig.Input.TagOf(rig.R.Target), Is.EqualTo(12));
            Assert.That(rig.Input.LabelOf(default), Is.Null);
            Assert.That(rig.Input.TagOf(default), Is.EqualTo(0));
        }

        [Test]
        public void Input_Hands_NoHandsNoChange()
        {
            // The same session three times: with no hands at all, as every session was before; with hands that are
            // not active, whatever they say; and with active hands of which none is valid.
            List<string> plain = Session(frame => default(HandsInput));
            List<string> inactive = Session(frame => new HandsInput
            {
                Active = false,
                Left = HandsRig.Tip(HandsRig.Panel + new float3(-0.05f, 0f, 0.05f * math.sin(frame * 0.3f))),
                Right = HandsRig.Tip(HandsRig.Panel + new float3(0.05f, 0f, 0.05f * math.cos(frame * 0.2f))),
                PinchDownLeft = true,
                PinchDownRight = frame % 3 == 0
            });
            List<string> invalid = Session(frame => new HandsInput
            {
                Active = true,
                Left = new HandsFingertip { Valid = false, World = HandsRig.Panel, Confidence = 1f },
                Right = new HandsFingertip { Valid = true, World = HandsRig.Panel + new float3(0f, 0f, 0.04f * math.sin(frame * 0.3f)), Confidence = 0.4f },
                PinchDownRight = frame % 5 == 0
            });

            Assert.That(plain.Count, Is.GreaterThan(600), "the session is not empty");
            Assert.That(plain.FindAll(line => line.Contains("fired")).Count, Is.GreaterThan(12), "and it fires by every route");
            Assert.That(inactive, Is.EqualTo(plain));
            Assert.That(invalid, Is.EqualTo(plain));
        }

        /// <summary>
        /// Six hundred frames of gamepad, keyboard, lever and gaze, with the hands a caller chooses. Returns
        /// everything a reader of the service could see: each firing, and each frame's snapshot.
        /// </summary>
        private static List<string> Session(System.Func<int, HandsInput> hands)
        {
            var log = new List<string>();
            var input = new AtlasInputCore();
            int frame = 0;
            input.ActionFired += (action, source, tag) => log.Add(frame + " fired " + action + " " + source + " " + tag);
            input.PushContext(InputContext.Space);

            float3 near = HandsRig.Panel;
            float3 handle = HandsRig.Eye + new float3(0.6f, 0f, 1.5f);
            input.Targets.Add(HandsRig.Chip(AtlasAction.NextSpace, Beacon, tag: 3, radius: 0.2f));
            GazeTargetData plus = HandsRig.Row(AtlasAction.DialRight, near, tag: 1, mode: GazeMode.Repeat);
            plus.Callback = (id, tag) => log.Add(frame + " called " + tag);
            input.Targets.Add(plus);
            GazeTargetData sweep = HandsRig.Chip(AtlasAction.None, handle, mode: GazeMode.Hold, radius: 0.15f);
            sweep.Axis = AtlasAxis.Sweep;
            sweep.AxisValue = 1f;
            input.Targets.Add(sweep);
            GazeTargetData row = HandsRig.Row(AtlasAction.NavDown, near, tag: 2);
            row.Context = InputContext.Menu;
            input.Targets.Add(row);

            for (frame = 0; frame < 600; frame++)
            {
                float3 lookAt = frame < 120 ? Beacon : frame < 300 ? near : frame < 420 ? handle : frame < 520 ? near : HandsRig.Eye + new float3(0f, 5f, 1f);
                quaternion looking = quaternion.LookRotationSafe(math.normalize(lookAt - HandsRig.Eye), new float3(0f, 1f, 0f));
                var raw = new RawInput { Hands = hands(frame), GamepadPresent = true };
                if (frame % 97 < 3) raw.PadDown |= InputTwins.Bit(AtlasAction.Primary);
                if (frame % 140 > 120) raw.PadDown |= InputTwins.Bit(AtlasAction.Back) | InputTwins.Bit(AtlasAction.ReturnHome);
                if (frame % 61 == 7) raw.KeyDown |= InputTwins.Bit(AtlasAction.Secondary);
                if (frame % 150 == 40 || frame == 290 || frame == 555) raw.PointerDown = true;
                raw.RightStick = new float2(math.sin(frame * 0.05f), 0f);
                raw.Twist = frame % 200 < 20 ? 0.5f : 0f;
                if (frame == 440) input.PushContext(InputContext.Menu);
                if (frame == 540) input.PopContext(InputContext.Menu);

                input.Tick(in raw, HandsRig.Eye, looking, Dt);

                uint pressed = 0u, held = 0u;
                for (int a = 1; a <= InputTwins.ActionCount; a++)
                {
                    if (input.Pressed((AtlasAction)a)) pressed |= 1u << a;
                    if (input.Held((AtlasAction)a)) held |= 1u << a;
                }
                ref readonly HandsTouchState touch = ref input.HandsTouch;
                Assert.That(touch.Left.Target.IsValid || touch.Right.Target.IsValid || touch.PinchFired || touch.AnyNear
                            || touch.Left.Fired || touch.Right.Fired || touch.Left.Pressed || touch.Right.Pressed, Is.False,
                            "frame " + frame + ": the hands did something");
                log.Add(frame + " " + input.Context + " " + pressed + " " + held + " " + input.Axis(AtlasAxis.Sweep) + " "
                        + input.Axis(AtlasAxis.Twist) + " " + input.Axis(AtlasAxis.RightX) + " " + input.GazeFocused + " "
                        + input.GazeProgress.ToString("R") + " " + input.GazePoint);
            }
            return log;
        }

        [Test]
        public void Input_Hands_AllocatesNothing()
        {
            var input = new AtlasInputCore();
            int fired = 0;
            input.ActionFired += (action, source, tag) => fired++;
            input.PushContext(InputContext.Space);

            // A near panel of rows and chips in every mode, and a beacon under the gaze for the pinch.
            for (int i = 0; i < 6; i++)
            {
                GazeTargetData row = HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel - new float3(0f, i * HandsRig.RowPitch, 0f), i,
                                                  i % 3 == 1 ? GazeMode.Repeat : GazeMode.Once);
                row.Label = "row";
                row.Callback = (id, tag) => fired++;
                input.Targets.Add(row);
            }
            for (int i = 0; i < 4; i++)
            {
                GazeTargetData chip = HandsRig.Chip(i == 3 ? AtlasAction.None : AtlasAction.Primary,
                                                    HandsRig.Panel + new float3(-0.18f + 0.12f * i, 0.10f, 0f), i,
                                                    i >= 2 ? GazeMode.Hold : GazeMode.Once);
                chip.Axis = AtlasAxis.Sweep;
                chip.AxisValue = 1f;
                input.Targets.Add(chip);
            }
            GazeTargetId beacon = input.Targets.Add(HandsRig.Chip(AtlasAction.NextSpace, Beacon, radius: 0.2f));
            quaternion looking = quaternion.LookRotationSafe(math.normalize(Beacon - HandsRig.Eye), new float3(0f, 1f, 0f));

            int presses = 0, pinches = 0;
            long allocated = 0;
            for (int frame = 0; frame < 1200; frame++)
            {
                // Warm up for 600 frames, then measure 600: both hands press up and down the panel, and pinch.
                long before = frame >= 600 ? System.GC.GetAllocatedBytesForCurrentThread() : 0;

                float beat = frame / 40f;
                float depth = 0.05f - 0.07f * math.abs(math.sin(beat * math.PI));       // in and out, 2 cm through
                var raw = new RawInput();
                raw.Hands.Active = true;
                bool pinching = frame % 90 == 45;                                        // a pinch needs its hand over nothing
                raw.Hands.Left = HandsRig.Tip(pinching ? Aside : HandsRig.Panel + new float3(-0.10f, -HandsRig.RowPitch * (frame / 80 % 6), -depth));
                raw.Hands.Right = HandsRig.Tip(HandsRig.Panel + new float3(-0.18f + 0.12f * (frame / 80 % 4), 0.10f, 0.01f - depth));
                raw.Hands.PinchDownLeft = pinching;
                input.Tick(in raw, HandsRig.Eye, looking, Dt);
                ref readonly HandsTouchState touch = ref input.HandsTouch;
                if (touch.Left.Fired) presses++;
                if (touch.Right.Fired) presses++;
                if (touch.PinchFired) pinches++;
                string label = input.LabelOf(touch.Left.Target);
                int tagged = input.TagOf(touch.Right.Target) + (label != null ? 1 : 0);
                float axis = input.Axis(AtlasAxis.Sweep) + tagged;

                if (frame >= 600) allocated += System.GC.GetAllocatedBytesForCurrentThread() - before;
                if (float.IsNaN(axis)) presses = -1;
            }

            Assert.That(presses, Is.GreaterThan(20), "the frames did press");
            Assert.That(pinches, Is.GreaterThan(3), "and pinch");
            Assert.That(fired, Is.GreaterThan(20), "and the firings reached their listeners");
            Assert.That(input.Targets.Contains(beacon), Is.True);
            Assert.That(allocated, Is.EqualTo(0L), "bytes allocated by 600 frames of touch and pinch");
        }

        [Test]
        public void Input_Hands_AnythingCanBeHandedOver()
        {
            // Twenty thousand frames of whatever a faulty source might say, over targets of every kind that come and
            // go while they are being pressed: nothing throws, and what is reported always hangs together.
            var rng = new System.Random(5);
            var input = new AtlasInputCore();
            var live = new List<GazeTargetId>();
            int fired = 0;
            bool otherSource = false;
            input.ActionFired += (action, source, tag) =>
            {
                fired++;
                otherSource |= source != InputSource.Gaze;
            };
            input.PushContext(InputContext.Space);
            System.Func<float> unit = () => (float)rng.NextDouble();
            System.Func<float3> nearPanel = () => HandsRig.Panel + new float3(unit() - 0.5f, unit() - 0.5f, unit() - 0.5f) * 0.4f;
            float3 head = HandsRig.Eye;
            quaternion looking = quaternion.identity;

            for (int frame = 0; frame < 20000; frame++)
            {
                // Targets appear, move, turn, are disabled and go.
                if (live.Count < 24 && unit() < 0.05f)
                {
                    var target = new GazeTargetData
                    {
                        Action = (AtlasAction)rng.Next(0, InputTwins.ActionCount + 1),
                        Axis = (AtlasAxis)rng.Next(0, InputTwins.AxisCount),
                        AxisValue = unit() * 2f - 1f,
                        Space = (GazeSpace)rng.Next(0, 3),
                        Shape = (GazeShape)rng.Next(0, 2),
                        Centre = unit() < 0.5f ? nearPanel() : new float3(0f, -0.1f, 0.45f) + (new float3(unit(), unit(), unit()) - 0.5f) * 0.2f,
                        Rotation = unit() < 0.1f ? default(quaternion) : quaternion.Euler(unit() * 6f, unit() * 6f, unit() * 6f),
                        HalfSize = new float2(unit() * 0.3f, unit() * 0.05f),
                        Radius = unit() < 0.1f ? 0f : unit() * 0.1f,
                        MaxDistance = unit() < 0.2f ? unit() : 0f,
                        Mode = (GazeMode)rng.Next(0, 3),
                        RepeatSeconds = unit() < 0.5f ? 0f : unit(),
                        Context = unit() < 0.8f ? InputContext.Space : InputContext.Menu,
                        Tag = rng.Next(0, 9),
                        Label = "fuzz"
                    };
                    if (unit() < 0.3f) target.Callback = (id, tag) => { if (live.Remove(id)) input.Targets.Remove(id); };
                    GazeTargetId added = input.Targets.Add(target);
                    if (added.IsValid) live.Add(added);
                }
                if (live.Count > 0 && unit() < 0.02f)
                {
                    GazeTargetId victim = live[rng.Next(live.Count)];
                    float what = unit();
                    if (what < 0.3f) { live.Remove(victim); input.Targets.Remove(victim); }
                    else if (what < 0.6f) input.Targets.Move(victim, nearPanel());
                    else if (what < 0.8f) input.Targets.SetRotation(victim, quaternion.Euler(unit() * 6f, unit() * 6f, unit() * 6f));
                    else input.Targets.SetEnabled(victim, unit() < 0.5f);
                }
                if (unit() < 0.002f) input.PushContext(unit() < 0.5f ? InputContext.Menu : InputContext.Space);
                if (unit() < 0.002f) input.PopContext(unit() < 0.5f ? InputContext.Menu : InputContext.Space);
                if (unit() < 0.01f) head = HandsRig.Eye + (new float3(unit(), unit(), unit()) - 0.5f) * 0.3f;
                if (unit() < 0.05f) looking = unit() < 0.02f ? default(quaternion) : quaternion.Euler(unit() - 0.5f, unit() * 6f, 0f);

                var hands = new HandsInput { Active = unit() < 0.95f, PinchDownLeft = unit() < 0.05f, PinchDownRight = unit() < 0.05f };
                for (int hand = 0; hand < 2; hand++)
                {
                    float kind = unit();
                    float3 where = kind < 0.90f ? nearPanel()
                                 : kind < 0.93f ? new float3(float.NaN, 0f, 0f)
                                 : kind < 0.96f ? new float3(0f, float.PositiveInfinity, 0f)
                                 : new float3(1e20f, -1e20f, 1e20f);
                    var tip = new HandsFingertip
                    {
                        Valid = unit() < 0.9f, World = where, VelocityWorld = new float3(float.NaN),
                        Confidence = unit() < 0.05f ? float.NaN : unit() * 1.2f
                    };
                    if (hand == 0) hands.Left = tip;
                    else hands.Right = tip;
                }
                float dt = unit() < 0.01f ? (unit() < 0.5f ? 0f : -1f) : unit() < 0.01f ? 1f : Dt;
                input.Tick(new RawInput { Hands = hands }, head, looking, dt);

                ref readonly HandsTouchState touch = ref input.HandsTouch;
                for (int hand = 0; hand < 2; hand++)
                {
                    HandsTouchHand h = hand == 0 ? touch.Left : touch.Right;
                    string where = "frame " + frame + ", hand " + hand;
                    Assert.That(h.Hover, Is.InRange(0f, 1f), where);
                    if (h.Fired) Assert.That(h.Pressed, Is.True, where);
                    if (h.Pressed || h.Hover > 0f) Assert.That(h.Target.IsValid, Is.True, where);
                    if (!h.Target.IsValid) continue;
                    Assert.That(hands.Active, Is.True, where);
                    Assert.That(math.all(math.isfinite(h.FacePoint)), Is.True, where);
                    Assert.That(math.length(h.FaceNormal), Is.EqualTo(1f).Within(1e-3f), where);
                    Assert.That(input.LabelOf(h.Target), Is.EqualTo("fuzz"), where);
                }
                if (touch.PinchFired) Assert.That(touch.PinchTarget.IsValid, Is.True, "frame " + frame);
                if (touch.AnyNear) Assert.That(hands.Active, Is.True, "frame " + frame);
            }

            Assert.That(fired, Is.GreaterThan(50), "the frames did fire things");
            Assert.That(otherSource, Is.False, "with no device and no lever, everything that fired carried the source Gaze");
        }

        [Test]
        public void Input_Hands_TheServiceGainsNoMemberForThem()
        {
            // IAtlasInput has six test fakes: hands reach input through IHandsInput, which only the real service implements.
            var names = new List<string>();
            foreach (System.Reflection.MethodInfo method in typeof(IAtlasInput).GetMethods()) names.Add(method.Name);
            names.Sort(string.CompareOrdinal);
            var expected = new List<string>
            {
                "Tick", "get_Context", "PushContext", "PopContext", "Pressed", "Held", "Axis", "LastSource", "LastTag",
                "add_ActionFired", "remove_ActionFired", "get_DwellSeconds", "set_DwellSeconds", "RegisterGazeTarget",
                "MoveGazeTarget", "SetGazeTargetEnabled", "UnregisterGazeTarget", "get_Gaze", "CopyActionsWithoutGazeTwin",
                "TryGetTap", "get_GamepadPresent", "SetRumble", "get_Walker"
            };
            expected.Sort(string.CompareOrdinal);
            Assert.That(names, Is.EqualTo(expected));
            Assert.That(typeof(IHandsInput).IsAssignableFrom(typeof(AtlasInput)), Is.True, "reached with \"input as IHandsInput\"");
            Assert.That(typeof(IAtlasInput).IsAssignableFrom(typeof(IHandsInput)), Is.False);
        }
    }
}
