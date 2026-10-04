using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Input.Tests
{
    /// <summary>The twin rule (Master Plan section 0.5 rule 5): every action has a gamepad binding and a
    /// gaze-dwell route.</summary>
    public class InputTwinsTests
    {
        [Test]
        public void TheTableCoversTheEnum()
        {
            var values = (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction));
            Assert.That(values.Length, Is.EqualTo(InputTwins.ActionCount + 1), "ActionCount must follow the enum");
            foreach (AtlasAction action in values)
            {
                if (action == AtlasAction.None) continue;
                Assert.That(InputTwins.Of(action).Action, Is.EqualTo(action), "row of " + action);
            }
            var axes = (AtlasAxis[])System.Enum.GetValues(typeof(AtlasAxis));
            Assert.That(axes.Length, Is.EqualTo(InputTwins.AxisCount));
            foreach (AtlasAxis axis in axes)
                Assert.That(InputTwins.Of(axis).Axis, Is.EqualTo(axis), "row of " + axis);
        }

        [Test]
        public void EveryActionHasBothRoutes()
        {
            var actions = new List<AtlasAction>();
            var axes = new List<AtlasAxis>();
            Assert.That(InputTwins.IsComplete(actions, axes), Is.True,
                        "without a twin: " + string.Join(", ", actions) + " " + string.Join(", ", axes));
            Assert.That(actions, Is.Empty);
            Assert.That(axes, Is.Empty);

            foreach (AtlasAction action in (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction)))
            {
                if (action == AtlasAction.None) continue;
                ref readonly ActionTwin twin = ref InputTwins.Of(action);
                Assert.That(InputTwins.HasGamepadRoute(action), Is.True, action + " gamepad");
                Assert.That(twin.GamepadPath, Does.StartWith("<Gamepad>/"), action + " gamepad path");
                Assert.That(InputTwins.HasGazeRoute(action), Is.True, action + " gaze");
                Assert.That(twin.GazeTarget, Is.Not.Empty, action + " gaze target");
                Assert.That(twin.KeyPath, Does.StartWith("<Keyboard>/"), action + " Editor key (README F9)");
                Assert.That(twin.InSpace || twin.InMenu, Is.True, action + " is live somewhere");
            }
            foreach (AtlasAxis axis in (AtlasAxis[])System.Enum.GetValues(typeof(AtlasAxis)))
            {
                Assert.That(InputTwins.HasGamepadRoute(axis), Is.True, axis + " gamepad");
                Assert.That(InputTwins.HasGazeRoute(axis), Is.True, axis + " gaze");
            }
        }

        [Test]
        public void NoneHasNoRoute()
        {
            Assert.That(InputTwins.HasGamepadRoute(AtlasAction.None), Is.False);
            Assert.That(InputTwins.HasGazeRoute(AtlasAction.None), Is.False);
            Assert.That(InputTwins.HasGamepadRoute((AtlasAction)200), Is.False);
        }

        [Test]
        public void AGamepadControlIsSharedOnlyWhereARuleOrTheContextTellsTheActionsApart()
        {
            var values = (AtlasAction[])System.Enum.GetValues(typeof(AtlasAction));
            foreach (AtlasAction a in values)
            {
                foreach (AtlasAction b in values)
                {
                    if (a >= b || a == AtlasAction.None) continue;
                    ref readonly ActionTwin ta = ref InputTwins.Of(a);
                    ref readonly ActionTwin tb = ref InputTwins.Of(b);
                    if (ta.GamepadPath != tb.GamepadPath) continue;
                    bool sameContext = (ta.InSpace && tb.InSpace) || (ta.InMenu && tb.InMenu);
                    bool ruled = !string.IsNullOrEmpty(ta.GamepadRule) && !string.IsNullOrEmpty(tb.GamepadRule)
                                 && ta.GamepadRule != tb.GamepadRule;
                    Assert.That(!sameContext || ruled, Is.True, a + " and " + b + " share " + ta.GamepadPath);
                }
            }
        }

        [Test]
        public void ContextsFollowTheContract()
        {
            Assert.That(InputTwins.ContextMask(InputContext.InHand), Is.EqualTo(0u), "in the hand the screen is a touch UI");
            Assert.That(InputTwins.LiveIn(AtlasAction.Confirm, InputContext.Space), Is.True);
            Assert.That(InputTwins.LiveIn(AtlasAction.Confirm, InputContext.Menu), Is.True);
            Assert.That(InputTwins.LiveIn(AtlasAction.NextSpace, InputContext.Menu), Is.False);
            Assert.That(InputTwins.LiveIn(AtlasAction.NavUp, InputContext.Space), Is.False);
            Assert.That(InputTwins.LiveIn(AtlasAction.NavUp, InputContext.Menu), Is.True);
            Assert.That(InputTwins.LiveIn(AtlasAction.ReturnHome, InputContext.Menu), Is.False);
            Assert.That(InputTwins.Of(AtlasAction.Confirm).Lever, Is.True);
        }

        [Test]
        public void MenuRowsServeBothNavigationDirections()
        {
            Assert.That(InputTwins.GazeTargetServes(AtlasAction.NavDown, AtlasAction.NavUp), Is.True);
            Assert.That(InputTwins.GazeTargetServes(AtlasAction.NavUp, AtlasAction.NavDown), Is.True);
            Assert.That(InputTwins.GazeTargetServes(AtlasAction.NavLeft, AtlasAction.NavRight), Is.False);
            Assert.That(InputTwins.GazeTargetServes(AtlasAction.Primary, AtlasAction.Primary), Is.True);
            Assert.That(InputTwins.GazeTargetServes(AtlasAction.None, AtlasAction.None), Is.False);
        }
    }
}
