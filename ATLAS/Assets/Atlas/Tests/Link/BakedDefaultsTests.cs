using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>Baked defaults (Hub off): values are restored into the registry, clamped, and a bad id is skipped.</summary>
    public sealed class BakedDefaultsTests
    {
        static readonly string[] Backends = { "local", "hosted", "cache", "off" };

        const string Baked =
            "{\"values\":{\"curved.cullRho\":2.75,\"glued.copies\":4,\"pose.gyroPredict\":false,\"forge.backend\":\"cache\"," +
            "\"old.removed\":1.5,\"glued.postWidth\":99,\"bad.shape\":[1,2],\"curved.mode\":null}}";

        static ParamRegistry Declared()
        {
            var r = new ParamRegistry();
            r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            r.DeclareInt("glued.copies", "glued", 3, 5, 5);
            r.DeclareBool("pose.gyroPredict", "pose", true);
            r.DeclareEnum("forge.backend", "forge", Backends, 0);
            r.DeclareFloat("glued.postWidth", "glued", 0.03f, 0.2f, 0.06f);
            return r;
        }

        [Test]
        public void Apply_restores_number_bool_and_enum_name()
        {
            ParamRegistry r = Declared();
            int changed = BakedDefaults.Apply(r, AtlasJson.Parse(Baked));

            Assert.That(r.GetFloat("curved.cullRho"), Is.EqualTo(2.75f));
            Assert.That(r.GetInt("glued.copies"), Is.EqualTo(4));
            Assert.That(r.GetBool("pose.gyroPredict"), Is.False);
            Assert.That(r.GetInt("forge.backend"), Is.EqualTo(2), "an enum given by name");
            Assert.That(changed, Is.EqualTo(5));
        }

        [Test]
        public void Apply_clamps_and_leaves_the_declared_default_alone()
        {
            ParamRegistry r = Declared();
            BakedDefaults.Apply(r, AtlasJson.Parse(Baked));

            ParamEntry width = r.Find("glued.postWidth");
            Assert.That(width.Float, Is.EqualTo(0.2f), "99 is clamped to the range");
            Assert.That(width.Default, Is.EqualTo(0.06f));
            Assert.That(r.Find("curved.cullRho").Default, Is.EqualTo(3f), "Bake sets the value, not the default");
            width.Reset();
            Assert.That(width.Float, Is.EqualTo(0.06f), "Reset still goes to the declared default");
        }

        [Test]
        public void Apply_skips_an_unknown_name_and_a_value_of_the_wrong_shape()
        {
            ParamRegistry r = Declared();
            int before = r.Count;
            Assert.DoesNotThrow(() => BakedDefaults.Apply(r, AtlasJson.Parse(Baked)));
            Assert.That(r.Count, Is.EqualTo(before), "an unknown id is not declared by baking");
            Assert.That(r.Find("old.removed"), Is.Null);
        }

        [Test]
        public void Apply_sets_a_name_declared_later_and_the_declared_default_is_unchanged()
        {
            ParamRegistry r = Declared();
            BakedDefaults.Apply(r, AtlasJson.Parse(
                "{\"values\":{\"late.gain\":1.5,\"late.on\":true,\"late.mode\":\"b\",\"late.low\":-5}}"));

            ParamEntry gain = r.DeclareFloat("late.gain", "late", 0f, 2f, 1f);
            ParamEntry on = r.DeclareBool("late.on", "late", false);
            ParamEntry mode = r.DeclareEnum("late.mode", "late", new[] { "a", "b", "c" }, 0);
            ParamEntry low = r.DeclareFloat("late.low", "late", 0f, 2f, 1f);
            ParamEntry untouched = r.DeclareFloat("late.other", "late", 0f, 2f, 1.25f);

            Assert.That(gain.Float, Is.EqualTo(1.5f));
            Assert.That(gain.Default, Is.EqualTo(1f));
            Assert.That(on.Bool, Is.True);
            Assert.That(on.Default, Is.EqualTo(0f));
            Assert.That(mode.Int, Is.EqualTo(1));
            Assert.That(low.Float, Is.EqualTo(0f), "clamped when declared");
            Assert.That(untouched.Float, Is.EqualTo(1.25f));
        }

        [Test]
        public void Apply_reports_a_restore_to_listeners_and_changed()
        {
            ParamRegistry r = Declared();
            var seen = new List<ParamSource>();
            r.Changed += (e, s) => seen.Add(s);
            BakedDefaults.Apply(r, AtlasJson.Parse("{\"values\":{\"curved.cullRho\":2.5}}"));
            r.DeclareFloat("late.gain", "late", 0f, 2f, 1f);
            Assert.That(seen, Is.EqualTo(new[] { ParamSource.Restore }));
        }

        [Test]
        public void Apply_accepts_the_bare_name_to_value_map()
        {
            ParamRegistry r = Declared();
            Assert.That(BakedDefaults.Apply(r, AtlasJson.Parse("{\"curved.cullRho\":2.25}")), Is.EqualTo(1));
            Assert.That(r.GetFloat("curved.cullRho"), Is.EqualTo(2.25f));
        }

        [Test]
        public void Apply_never_sets_demoLock_link_enabled_or_space_id()
        {
            var r = new ParamRegistry();
            r.DeclareBool("demoLock", "app", false);
            r.DeclareBool("link.enabled", "link", true);
            r.DeclareInt("space.id", "app", 0, 5, 0);
            const string json = "{\"values\":{\"demoLock\":true,\"link.enabled\":false,\"space.id\":3}}";

            Assert.That(BakedDefaults.Apply(r, AtlasJson.Parse(json)), Is.EqualTo(0));
            Assert.That(r.GetBool("demoLock"), Is.False);
            Assert.That(r.GetBool("link.enabled"), Is.True);
            Assert.That(r.GetInt("space.id"), Is.EqualTo(0));

            var later = new ParamRegistry();
            BakedDefaults.Apply(later, AtlasJson.Parse(json));
            later.DeclareBool("demoLock", "app", false);
            Assert.That(later.GetBool("demoLock"), Is.False, "nor when declared later");
        }

        [Test]
        public void An_absent_or_invalid_file_is_no_baked_values()
        {
            ParamRegistry r = Declared();
            float rho = r.GetFloat("curved.cullRho");
            int version = r.Version;

            Assert.That(BakedDefaults.ApplyText(r, null), Is.False);
            Assert.That(BakedDefaults.ApplyText(r, ""), Is.False);
            Assert.That(BakedDefaults.ApplyText(r, "{not json"), Is.False);
            Assert.That(BakedDefaults.ApplyText(r, "[1,2,3]"), Is.False);
            Assert.That(BakedDefaults.Apply(r, null), Is.EqualTo(0));
            Assert.That(BakedDefaults.Apply(r, JsonValue.Null), Is.EqualTo(0));
            Assert.That(BakedDefaults.ApplyText(r, "{\"values\":{}}"), Is.True, "the empty file is legal");

            Assert.That(r.GetFloat("curved.cullRho"), Is.EqualTo(rho));
            Assert.That(r.Version, Is.EqualTo(version));
        }

        [Test]
        public void ApplyText_loads_the_text_of_the_file()
        {
            ParamRegistry r = Declared();
            Assert.That(BakedDefaults.ApplyText(r, Baked), Is.True);
            Assert.That(r.GetFloat("curved.cullRho"), Is.EqualTo(2.75f));
        }

        [Test]
        public void A_snapshot_taken_from_a_registry_applies_to_another_with_the_same_names()
        {
            ParamRegistry tuned = Declared();
            tuned.Set("curved.cullRho", 3.25f);
            tuned.Set("forge.backend", 3f);
            tuned.Set("pose.gyroPredict", 0f);

            ParamRegistry fresh = Declared();
            BakedDefaults.Apply(fresh, AtlasJson.Parse(tuned.ToSnapshot().ToJson()));
            for (int i = 0; i < tuned.Count; i++)
                Assert.That(fresh[i].Value, Is.EqualTo(tuned[i].Value), tuned[i].Name);
        }
    }
}
