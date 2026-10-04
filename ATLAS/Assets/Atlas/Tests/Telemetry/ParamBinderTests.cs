using System;
using System.Collections.Generic;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>The attribute binder: fields are declared in the registry and follow its values.</summary>
    public class ParamBinderTests
    {
        enum Mode { Torus = 0, Klein = 1, ThreeTorus = 2 }
        enum Gappy { A = 0, C = 2 }

        class BaseSpace
        {
            [AtlasParam("test.base", 0f, 10f, 4f)] float _inherited;
            public float Inherited { get { return _inherited; } }
        }

        sealed class Space : BaseSpace
        {
            [AtlasParam("test.postWidth", 0.03f, 0.20f, 0.06f, Group = "glued", Step = 0.005f)] float _postWidth;
            [AtlasParam("test.copies", 3f, 7f, 5f, OnRelease = true, KillSwitch = true)] public int Copies;
            [AtlasParam("test.avatar", true)] bool _avatar;
            [AtlasParam("test.mode", 1)] Mode _mode;
            public float NotAParameter = 9f;

            public float PostWidth { get { return _postWidth; } }
            public bool Avatar { get { return _avatar; } }
            public Mode ModeValue { get { return _mode; } }
        }

        static class Statics
        {
            [AtlasParam("test.static.gain", 0f, 2f, 1f)] public static float Gain;
            [AtlasParam("test.static.on", false)] public static bool On;
        }

        sealed class BadType { [AtlasParam("test.bad", 0f, 1f, 0f)] public string Text; }
        sealed class BadForm { [AtlasParam("test.badForm", true)] public float Value; }
        sealed class BadEnum { [AtlasParam("test.badEnum", 0)] public Gappy Value; }
        sealed class Clash { [AtlasParam("test.copies", true)] public bool Value; }
        sealed class Follower { [AtlasParam("curved.cullRho", 0f, 1f, 0.5f)] public float Rho; }

        [Test]
        public void Bind_declares_every_attributed_field_and_writes_the_defaults()
        {
            var registry = new ParamRegistry();
            var space = new Space();
            using (ParamBinding binding = ParamBinder.Bind(space, registry))
            {
                Assert.That(binding.Count, Is.EqualTo(5));
                Assert.That(registry.Count, Is.EqualTo(5));
                Assert.That(space.PostWidth, Is.EqualTo(0.06f));
                Assert.That(space.Copies, Is.EqualTo(5));
                Assert.That(space.Avatar, Is.True);
                Assert.That(space.ModeValue, Is.EqualTo(Mode.Klein));
                Assert.That(space.Inherited, Is.EqualTo(4f), "a private field of a base type is bound too");
                Assert.That(space.NotAParameter, Is.EqualTo(9f));

                ParamEntry width = registry.Find("test.postWidth");
                Assert.That(width.Kind, Is.EqualTo(ParamKind.Float));
                Assert.That(width.Group, Is.EqualTo("glued"));
                Assert.That(width.Min, Is.EqualTo(0.03f));
                Assert.That(width.Max, Is.EqualTo(0.20f));

                ParamEntry copies = registry.Find("test.copies");
                Assert.That(copies.Kind, Is.EqualTo(ParamKind.Int));
                Assert.That(copies.Group, Is.EqualTo("test"), "the group defaults to the first segment of the id");
                Assert.That(copies.OnRelease, Is.True);

                ParamEntry mode = registry.Find("test.mode");
                Assert.That(mode.Kind, Is.EqualTo(ParamKind.Enum));
                Assert.That(mode.EnumCount, Is.EqualTo(3));
                Assert.That(mode.EnumName(2), Is.EqualTo("ThreeTorus"));
                Assert.That(registry.Find("test.avatar").Kind, Is.EqualTo(ParamKind.Bool));

                Assert.That(binding.HandleOf("test.copies").Id, Is.EqualTo(copies.Id));
                Assert.Throws<KeyNotFoundException>(() => binding.HandleOf("test.unknown"));
            }
        }

        [Test]
        public void Changes_in_the_registry_are_written_back_clamped()
        {
            var registry = new ParamRegistry();
            var space = new Space();
            ParamBinding binding = ParamBinder.Bind(space, registry);

            registry.Set("test.postWidth", 0.1f);
            registry.Set("test.copies", 99f, ParamSource.Remote);
            registry.Set("test.avatar", 0f);
            registry.Set("test.mode", 2f);
            registry.Set("test.base", -5f);
            Assert.That(space.PostWidth, Is.EqualTo(0.1f));
            Assert.That(space.Copies, Is.EqualTo(7), "clamped to the declared range");
            Assert.That(space.Avatar, Is.False);
            Assert.That(space.ModeValue, Is.EqualTo(Mode.ThreeTorus));
            Assert.That(space.Inherited, Is.EqualTo(0f));

            // A ParamSet from the Station takes the same road.
            var set = new ParamSetMessage();
            set.Add(binding.HandleOf("test.postWidth").Id, 0.5f);
            registry.Apply(set);
            Assert.That(space.PostWidth, Is.EqualTo(0.20f));

            binding.Dispose();
            registry.Set("test.copies", 3f);
            Assert.That(space.Copies, Is.EqualTo(7), "a disposed binding no longer writes");
            Assert.That(registry.Find("test.copies").Int, Is.EqualTo(3), "the parameter stays declared");
            binding.Dispose();
        }

        [Test]
        public void Binding_again_keeps_the_current_value()
        {
            var registry = new ParamRegistry();
            var first = new Space();
            ParamBinder.Bind(first, registry).Dispose();
            registry.Set("test.copies", 4f);

            var second = new Space();
            using (ParamBinder.Bind(second, registry))
            {
                Assert.That(registry.Count, Is.EqualTo(5));
                Assert.That(second.Copies, Is.EqualTo(4));
            }
        }

        [Test]
        public void A_parameter_that_already_has_an_owner_keeps_its_range_and_callback()
        {
            var registry = new ParamRegistry();
            float seen = -1f;
            registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f, v => seen = v);
            var follower = new Follower();
            using (ParamBinder.Bind(follower, registry))
            {
                Assert.That(follower.Rho, Is.EqualTo(3f));
                registry.Set("curved.cullRho", 9f);
                Assert.That(follower.Rho, Is.EqualTo(3.5f));
                Assert.That(seen, Is.EqualTo(3.5f), "the owner still hears the change");
            }
        }

        [Test]
        public void Static_fields_bind_through_the_type()
        {
            var registry = new ParamRegistry();
            Statics.Gain = 0f;
            using (ParamBinding binding = ParamBinder.BindStatic(typeof(Statics), registry))
            {
                Assert.That(binding.Count, Is.EqualTo(2));
                Assert.That(Statics.Gain, Is.EqualTo(1f));
                Assert.That(Statics.On, Is.False);
                registry.Set("test.static.gain", 1.5f);
                registry.Set("test.static.on", 1f);
                Assert.That(Statics.Gain, Is.EqualTo(1.5f));
                Assert.That(Statics.On, Is.True);
            }
        }

        [Test]
        public void Wrong_field_types_and_attribute_forms_are_refused_by_name()
        {
            var registry = new ParamRegistry();
            var e = Assert.Throws<InvalidOperationException>(() => ParamBinder.Bind(new BadType(), registry));
            Assert.That(e.Message, Does.Contain("BadType.Text"));
            Assert.Throws<InvalidOperationException>(() => ParamBinder.Bind(new BadForm(), registry));
            Assert.Throws<InvalidOperationException>(() => ParamBinder.Bind(new BadEnum(), registry));

            ParamBinder.Bind(new Space(), registry);
            Assert.Throws<InvalidOperationException>(() => ParamBinder.Bind(new Clash(), registry), "test.copies is an int, not a bool");
            Assert.Throws<ArgumentNullException>(() => ParamBinder.Bind(null, registry));
        }

        [Test]
        public void Reading_a_bound_field_and_an_unrelated_change_allocate_nothing()
        {
            var registry = new ParamRegistry();
            var space = new Space();
            ParamEntry other = registry.DeclareFloat("other.value", "other", 0f, 1000f, 0f);
            using (ParamBinder.Bind(space, registry))
            {
                other.Set(1f);
                float sum = 0f;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 2; i < 200; i++)
                {
                    // Another parameter changes every frame (a live one, say): the binding only compares.
                    other.Set((float)i);
                    sum += space.PostWidth + space.Copies;
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.EqualTo(0));
                Assert.That(sum, Is.GreaterThan(0f));
            }
        }
    }
}
