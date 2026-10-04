using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>The gaze target registry: handles, capacity, and changes made while the dwell is being evaluated.</summary>
    public class GazeRegistryTests
    {
        private const float Dt = 1f / 60f;
        private static readonly float3 Head = new float3(0f, 1.6f, 0f);

        private static GazeTargetData Ahead(AtlasAction action, float distance = 2f, InputContext context = InputContext.Space)
        {
            return new GazeTargetData
            {
                Action = action, Space = GazeSpace.World, Centre = Head + new float3(0f, 0f, distance), Radius = 0.1f,
                Context = context, Label = action.ToString()
            };
        }

        private static RawInput NoDevices => default;

        [Test]
        public void HandlesAreValidUntilRemoved()
        {
            var registry = new GazeRegistry();
            Assert.That(default(GazeTargetId).IsValid, Is.False);
            Assert.That(registry.Contains(default), Is.False);

            GazeTargetId a = registry.Add(Ahead(AtlasAction.Primary));
            Assert.That(a.IsValid, Is.True);
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.TryGet(a, out GazeTargetData data), Is.True);
            Assert.That(data.Label, Is.EqualTo("Primary"));
            Assert.That(data.RepeatSeconds, Is.EqualTo(0.4f), "0 means the default interval");
            Assert.That(registry.IsEnabled(a), Is.True);

            Assert.That(registry.Remove(a), Is.True);
            Assert.That(registry.Remove(a), Is.False);
            Assert.That(registry.Count, Is.EqualTo(0));

            // The slot is reused; the old handle stays dead.
            GazeTargetId b = registry.Add(Ahead(AtlasAction.Secondary));
            Assert.That(b, Is.Not.EqualTo(a));
            Assert.That(registry.Contains(a), Is.False);
            Assert.That(registry.Move(a, float3.zero), Is.False);
            Assert.That(registry.SetEnabled(a, false), Is.False);
            Assert.That(registry.TryGet(b, out data), Is.True);
            Assert.That(data.Action, Is.EqualTo(AtlasAction.Secondary));
        }

        [Test]
        public void HoldsSixtyFourTargets()
        {
            var registry = new GazeRegistry();
            var ids = new List<GazeTargetId>();
            for (int i = 0; i < GazeRegistry.Capacity; i++) ids.Add(registry.Add(Ahead(AtlasAction.Confirm)));
            Assert.That(ids.TrueForAll(id => id.IsValid), Is.True);
            Assert.That(registry.Add(Ahead(AtlasAction.Confirm)).IsValid, Is.False, "the 65th is refused");
            registry.Remove(ids[10]);
            Assert.That(registry.Add(Ahead(AtlasAction.Confirm)).IsValid, Is.True);
            registry.Clear();
            Assert.That(registry.Count, Is.EqualTo(0));
            Assert.That(registry.Contains(ids[0]), Is.False);
        }

        [Test]
        public void TargetsCanBeAddedAndRemovedWhileWalkingTheRegistry()
        {
            var registry = new GazeRegistry();
            for (int i = 0; i < 10; i++)
            {
                GazeTargetData t = Ahead(AtlasAction.Confirm);
                t.Tag = i;
                registry.Add(t);
            }

            // Remove the even ones and add a new one for each, inside the walk.
            int seen = 0;
            for (int slot = 0; slot < registry.SlotLimit; slot++)
            {
                if (!registry.TryGetSlot(slot, out GazeTargetId id)) continue;
                registry.TryGet(id, out GazeTargetData data);
                if (data.Tag >= 100) continue;                // one added during this walk
                seen++;
                if (data.Tag % 2 != 0) continue;
                registry.Remove(id);
                GazeTargetData added = Ahead(AtlasAction.Back);
                added.Tag = 100 + data.Tag;
                Assert.That(registry.Add(added).IsValid, Is.True);
            }
            Assert.That(seen, Is.EqualTo(10), "every original target was visited exactly once");
            Assert.That(registry.Count, Is.EqualTo(10));

            int odd = 0, fresh = 0;
            for (int slot = 0; slot < registry.SlotLimit; slot++)
            {
                if (!registry.TryGetSlot(slot, out GazeTargetId id)) continue;
                registry.TryGet(id, out GazeTargetData data);
                if (data.Tag >= 100) fresh++;
                else if (data.Tag % 2 == 1) odd++;
                else Assert.Fail("an even target survived: " + data.Tag);
            }
            Assert.That(odd, Is.EqualTo(5));
            Assert.That(fresh, Is.EqualTo(5));
        }

        [Test]
        public void ACallbackMayRemoveItsOwnTargetAndAddOthers()
        {
            var input = new AtlasInputCore();
            input.PushContext(InputContext.Space);
            GazeRegistry registry = input.Targets;

            GazeTargetId chip = default;
            GazeTargetId next = default;
            int callbacks = 0;
            var events = new List<AtlasAction>();
            input.ActionFired += (action, source, tag) => events.Add(action);

            GazeTargetData first = Ahead(AtlasAction.Primary);
            first.Tag = 7;
            first.Callback = (id, tag) =>
            {
                callbacks++;
                Assert.That(id, Is.EqualTo(chip));
                Assert.That(tag, Is.EqualTo(7));
                Assert.That(input.Pressed(AtlasAction.Primary), Is.True, "the snapshot is frozen before the callback runs");
                registry.Remove(id);                                         // the chip is used up ...
                next = registry.Add(Ahead(AtlasAction.Secondary, 2f));       // ... and replaced, in the same place
                for (int i = 0; i < 5; i++) registry.Add(Ahead(AtlasAction.Confirm, 50f + i));
            };
            chip = registry.Add(first);

            for (int i = 0; i < 60; i++) input.Tick(NoDevices, Head, quaternion.identity, Dt);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(new[] { AtlasAction.Primary }));
            Assert.That(registry.Contains(chip), Is.False);
            Assert.That(registry.Count, Is.EqualTo(6));
            Assert.That(next.Slot, Is.EqualTo(chip.Slot), "the replacement took the freed slot");

            // The replacement sits in the same slot and under the same gaze: it must get a full dwell of its
            // own and must not inherit the old target's state.
            events.Clear();
            for (int i = 0; i < 60; i++) input.Tick(NoDevices, Head, quaternion.identity, Dt);
            Assert.That(events, Is.EqualTo(new[] { AtlasAction.Secondary }));
            Assert.That(input.GazeFocused, Is.EqualTo(next));
        }

        [Test]
        public void AnEventHandlerMayClearTheRegistryAndChangeTheContext()
        {
            var input = new AtlasInputCore();
            input.PushContext(InputContext.Space);
            input.Targets.Add(Ahead(AtlasAction.SpaceMenu));
            GazeTargetId row = default;
            input.ActionFired += (action, source, tag) =>
            {
                if (action != AtlasAction.SpaceMenu) return;
                input.Targets.Clear();
                input.PushContext(InputContext.Menu);
                row = input.Targets.Add(Ahead(AtlasAction.Confirm, 2f, InputContext.Menu));
            };

            for (int i = 0; i < 50; i++) input.Tick(NoDevices, Head, quaternion.identity, Dt);
            Assert.That(input.Context, Is.EqualTo(InputContext.Menu));
            Assert.That(input.Targets.Count, Is.EqualTo(1));

            var fired = new List<AtlasAction>();
            input.ActionFired += (action, source, tag) => fired.Add(action);
            for (int i = 0; i < 50; i++) input.Tick(NoDevices, Head, quaternion.identity, Dt);
            Assert.That(fired, Is.EqualTo(new[] { AtlasAction.Confirm }));
            Assert.That(input.GazeFocused, Is.EqualTo(row));
        }
    }
}
