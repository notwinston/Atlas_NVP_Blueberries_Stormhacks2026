using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    public sealed class ParamRegistryTests
    {
        static readonly string[] Backends = { "local", "hosted", "cache", "off" };

        [Test]
        public void Declares_each_kind_with_ids_in_order()
        {
            var r = new ParamRegistry();
            ParamEntry rho = r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry copies = r.DeclareInt("glued.copies", "glued", 3, 5, 5);
            ParamEntry predict = r.DeclareBool("pose.gyroPredict", "pose", true);
            ParamEntry backend = r.DeclareEnum("forge.backend", "forge", Backends, 0);

            Assert.That(r.Count, Is.EqualTo(4));
            Assert.That(new[] { rho.Id, copies.Id, predict.Id, backend.Id }, Is.EqualTo(new ushort[] { 0, 1, 2, 3 }));
            Assert.That(rho.Kind, Is.EqualTo(ParamKind.Float));
            Assert.That(rho.Float, Is.EqualTo(3f));
            Assert.That(rho.Min, Is.EqualTo(2f));
            Assert.That(rho.Max, Is.EqualTo(3.5f));
            Assert.That(rho.Default, Is.EqualTo(3f));
            Assert.That(rho.Group, Is.EqualTo("curved"));
            Assert.That(copies.Int, Is.EqualTo(5));
            Assert.That(predict.Bool, Is.True);
            Assert.That(predict.Min, Is.EqualTo(0f));
            Assert.That(predict.Max, Is.EqualTo(1f));
            Assert.That(backend.Int, Is.EqualTo(0));
            Assert.That(backend.EnumCount, Is.EqualTo(4));
            Assert.That(backend.CurrentName, Is.EqualTo("local"));
            Assert.That(backend.EnumName(2), Is.EqualTo("cache"));
            Assert.That(backend.EnumName(9), Is.Null);
            Assert.That(backend.Max, Is.EqualTo(3f));

            Assert.That(r[1], Is.SameAs(copies));
            Assert.That(r.Entries[3], Is.SameAs(backend));
            Assert.That(r.Find("glued.copies"), Is.SameAs(copies));
            Assert.That(r.Find("nope"), Is.Null);
            ParamEntry found;
            Assert.That(r.TryGet((ushort)2, out found), Is.True);
            Assert.That(found, Is.SameAs(predict));
            Assert.That(r.TryGet((ushort)40, out found), Is.False);
            Assert.That(r.TryGet("forge.backend", out found), Is.True);

            var groups = new List<string>();
            r.GetGroups(groups);
            Assert.That(groups, Is.EqualTo(new[] { "curved", "glued", "pose", "forge" }));
        }

        [Test]
        public void Set_clamps_rounds_and_reports_change()
        {
            var r = new ParamRegistry();
            ParamEntry rho = r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry copies = r.DeclareInt("glued.copies", "glued", 3, 5, 5);
            ParamEntry predict = r.DeclareBool("pose.gyroPredict", "pose", true);
            ParamEntry backend = r.DeclareEnum("forge.backend", "forge", Backends, 0);

            Assert.That(rho.Set(99f), Is.True);
            Assert.That(rho.Float, Is.EqualTo(3.5f));
            Assert.That(rho.Set(99f), Is.False, "same clamped value is no change");
            Assert.That(rho.Set(-4f), Is.True);
            Assert.That(rho.Float, Is.EqualTo(2f));
            Assert.That(rho.Set(float.NaN), Is.False);
            Assert.That(rho.Float, Is.EqualTo(2f));
            Assert.That(rho.Set(float.PositiveInfinity), Is.True);
            Assert.That(rho.Float, Is.EqualTo(3.5f));

            Assert.That(copies.Set(3.6f), Is.True);
            Assert.That(copies.Int, Is.EqualTo(4));
            Assert.That(copies.Value, Is.EqualTo(4f));
            copies.Set(-100);
            Assert.That(copies.Int, Is.EqualTo(3));

            Assert.That(predict.Set(0.4f), Is.True);
            Assert.That(predict.Bool, Is.False);
            predict.Set(17f);
            Assert.That(predict.Bool, Is.True);
            predict.Set(false);
            Assert.That(predict.Bool, Is.False);

            backend.Set(2);
            Assert.That(backend.CurrentName, Is.EqualTo("cache"));
            backend.Set(12);
            Assert.That(backend.Int, Is.EqualTo(3));

            Assert.That(r.Set("curved.cullRho", 2.5f), Is.True);
            Assert.That(r.Set((ushort)0, 2.75f), Is.True);
            Assert.That(r.Set(rho.Handle, 3f), Is.True);
            Assert.That(r.Set("unknown", 1f), Is.False);
            Assert.That(r.Set((ushort)77, 1f), Is.False);
            Assert.That(r.Set(default(ParamHandle), 1f), Is.False);
            Assert.That(rho.Reset(), Is.False, "already at default");
            Assert.That(r.ResetAll(), Is.EqualTo(3));
            Assert.That(copies.Int, Is.EqualTo(5));
        }

        [Test]
        public void Callbacks_and_Changed_fire_once_per_real_change()
        {
            var r = new ParamRegistry();
            float lastFloat = -1f;
            int lastInt = -1, floatCalls = 0, changedCalls = 0;
            bool lastBool = false;
            ParamEntry lastEntry = null;
            ParamSource lastSource = ParamSource.Local;
            r.Changed += (e, s) => { changedCalls++; lastEntry = e; lastSource = s; };

            ParamEntry scale = r.DeclareFloat("render.scale", "render", 0.6f, 0.8f, 0.8f, v => { lastFloat = v; floatCalls++; });
            ParamEntry msaa = r.DeclareInt("render.msaa", "render", 2, 4, 4, v => lastInt = v);
            ParamEntry depth = r.DeclareBool("portal.depth", "portal", false, v => lastBool = v);
            Assert.That(floatCalls, Is.EqualTo(0), "declaring does not call back");
            int version = r.Version;

            scale.Set(0.7f, ParamSource.Remote);
            Assert.That(lastFloat, Is.EqualTo(0.7f));
            Assert.That(changedCalls, Is.EqualTo(1));
            Assert.That(lastEntry, Is.SameAs(scale));
            Assert.That(lastSource, Is.EqualTo(ParamSource.Remote));
            Assert.That(r.Version, Is.EqualTo(version + 1));

            scale.Set(0.7f);
            Assert.That(floatCalls, Is.EqualTo(1));
            Assert.That(changedCalls, Is.EqualTo(1));

            scale.Set(5f); // clamped to 0.8: a change, reported with the clamped value
            Assert.That(lastFloat, Is.EqualTo(0.8f));
            msaa.Set(2);
            Assert.That(lastInt, Is.EqualTo(2));
            depth.Set(true);
            Assert.That(lastBool, Is.True);
            Assert.That(changedCalls, Is.EqualTo(4));
            Assert.That(lastSource, Is.EqualTo(ParamSource.Local));

            lastFloat = -1f; lastInt = -1; lastBool = false;
            r.PushAll();
            Assert.That(lastFloat, Is.EqualTo(0.8f));
            Assert.That(lastInt, Is.EqualTo(2));
            Assert.That(lastBool, Is.True);
            Assert.That(changedCalls, Is.EqualTo(4), "PushAll is not a change");
        }

        [Test]
        public void Redeclaring_is_idempotent_and_keeps_the_value()
        {
            var r = new ParamRegistry();
            int declared = 0;
            r.Declared += e => declared++;
            ParamEntry a = r.DeclareFloat("glued.period", "glued", 1f, 4f, 2.6f);
            a.Set(3.2f);
            int manifest = r.ManifestVersion;

            int calls = 0;
            ParamEntry b = r.DeclareFloat("glued.period", "glued", 1f, 4f, 2.6f, v => calls++);
            Assert.That(b, Is.SameAs(a));
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(declared, Is.EqualTo(1));
            Assert.That(b.Float, Is.EqualTo(3.2f));
            Assert.That(r.ManifestVersion, Is.EqualTo(manifest), "same range, same manifest");

            // A narrower range re-clamps the kept value and replaces the callback.
            r.DeclareFloat("glued.period", "glued", 1f, 3f, 2f, v => calls++);
            Assert.That(a.Float, Is.EqualTo(3f));
            Assert.That(a.Default, Is.EqualTo(2f));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(r.ManifestVersion, Is.GreaterThan(manifest));

            Assert.That(() => r.DeclareInt("glued.period", "glued", 1, 3, 2), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => r.DeclareFloat("", "g", 0f, 1f, 0f), Throws.TypeOf<ArgumentException>());
            Assert.That(() => r.DeclareFloat("x", "g", 2f, 1f, 0f), Throws.TypeOf<ArgumentException>());
            Assert.That(() => r.DeclareEnum("e", "g", new string[0], 0), Throws.TypeOf<ArgumentException>());
            Assert.That(r.DeclareFloat("clamped.default", "g", 0f, 1f, 9f).Float, Is.EqualTo(1f));
        }

        [Test]
        public void Handles_read_without_allocating()
        {
            var r = new ParamRegistry();
            ParamEntry rho = r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry copies = r.DeclareInt("glued.copies", "glued", 3, 5, 5);
            ParamEntry predict = r.DeclareBool("pose.gyroPredict", "pose", true);
            ParamHandle hRho = rho.Handle, hCopies = r.HandleOf("glued.copies"), hPredict = predict.Handle;

            Assert.That(default(ParamHandle).IsValid, Is.False);
            Assert.That(r.HandleOf("nope").IsValid, Is.False);
            Assert.That(hCopies, Is.EqualTo(copies.Handle));
            Assert.That(r.Get(hRho), Is.SameAs(rho));
            Assert.That(r.GetFloat(default(ParamHandle), 9f), Is.EqualTo(9f));
            Assert.That(r.GetFloat("curved.cullRho"), Is.EqualTo(3f));
            Assert.That(r.GetInt("glued.copies"), Is.EqualTo(5));
            Assert.That(r.GetBool("pose.gyroPredict"), Is.True);
            Assert.That(r.GetBool("missing", true), Is.True);

            float sum = 0f;
            // Warm up, then measure: reads and sets on the frame path must not allocate.
            for (int i = 0; i < 10; i++) sum += r.GetFloat(hRho) + r.GetInt(hCopies) + (r.GetBool(hPredict) ? 1 : 0) + rho.Float;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                sum += r.GetFloat(hRho) + r.GetInt(hCopies) + (r.GetBool(hPredict) ? 1 : 0) + rho.Float + copies.Int;
                r.Set(hRho, 2f + (i & 1));
                for (int k = 0; k < r.Count; k++) sum += r[k].Value;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0L));
            Assert.That(sum, Is.GreaterThan(0f));
        }

        [Test]
        public void Snapshot_round_trips_through_json()
        {
            var r = new ParamRegistry();
            r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f).Set(2.25f);
            r.DeclareInt("glued.copies", "glued", 3, 5, 5).Set(3);
            r.DeclareBool("pose.gyroPredict", "pose", true).Set(false);
            r.DeclareEnum("forge.backend", "forge", Backends, 0).Set(2);

            string json = r.ToSnapshot().ToJson();
            Assert.That(json, Is.EqualTo("{\"values\":{\"curved.cullRho\":2.25,\"glued.copies\":3,\"pose.gyroPredict\":false,\"forge.backend\":2}}"));

            var fresh = new ParamRegistry();
            ParamEntry rho = fresh.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry copies = fresh.DeclareInt("glued.copies", "glued", 3, 5, 5);
            ParamEntry predict = fresh.DeclareBool("pose.gyroPredict", "pose", true);
            ParamEntry backend = fresh.DeclareEnum("forge.backend", "forge", Backends, 0);
            ParamSource seen = ParamSource.Local;
            fresh.Changed += (e, s) => seen = s;

            Assert.That(fresh.Restore(AtlasJson.Parse(json)), Is.EqualTo(4));
            Assert.That(seen, Is.EqualTo(ParamSource.Restore));
            Assert.That(rho.Float, Is.EqualTo(2.25f));
            Assert.That(copies.Int, Is.EqualTo(3));
            Assert.That(predict.Bool, Is.False);
            Assert.That(backend.CurrentName, Is.EqualTo("cache"));
            Assert.That(fresh.Restore(AtlasJson.Parse(json)), Is.EqualTo(0), "restoring twice changes nothing");
            Assert.That(fresh.ToSnapshot().ToJson(), Is.EqualTo(json));
        }

        [Test]
        public void Restore_clamps_skips_unknown_names_and_accepts_enum_names()
        {
            var r = new ParamRegistry();
            ParamEntry rho = r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry backend = r.DeclareEnum("forge.backend", "forge", Backends, 0);
            ParamEntry predict = r.DeclareBool("pose.gyroPredict", "pose", false);

            // The bare name-to-value map is accepted too.
            int changed = r.Restore(AtlasJson.Parse(
                "{\"curved.cullRho\":50,\"forge.backend\":\"Hosted\",\"pose.gyroPredict\":1,\"gone.param\":4,\"curved.other\":[1]}"));
            Assert.That(changed, Is.EqualTo(3));
            Assert.That(rho.Float, Is.EqualTo(3.5f));
            Assert.That(backend.Int, Is.EqualTo(1));
            Assert.That(predict.Bool, Is.True);
            Assert.That(r.Restore(null), Is.EqualTo(0));
            Assert.That(r.Restore(AtlasJson.Parse("[1,2]")), Is.EqualTo(0));
            Assert.That(r.Restore(AtlasJson.Parse("{\"forge.backend\":\"no-such-name\"}")), Is.EqualTo(0));
        }

        [Test]
        public void Manifest_lists_every_field_of_the_schema()
        {
            var r = new ParamRegistry();
            r.DeclareFloat("_HypL", "curved", 0.5f, 6f, 2f, null, true).Set(2.5f);
            r.DeclareEnum("curved.mode", "curved", new[] { "mode1", "mode2" }, 1);
            string json = r.ToManifestJson().ToJson();
            Assert.That(json, Is.EqualTo(
                "{\"params\":[" +
                "{\"id\":0,\"name\":\"_HypL\",\"group\":\"curved\",\"min\":0.5,\"max\":6.0,\"def\":2.0,\"val\":2.5,\"onRelease\":true,\"kind\":\"float\",\"active\":true}," +
                "{\"id\":1,\"name\":\"curved.mode\",\"group\":\"curved\",\"min\":0.0,\"max\":1.0,\"def\":1.0,\"val\":1.0,\"onRelease\":false,\"kind\":\"enum\",\"active\":true,\"names\":[\"mode1\",\"mode2\"]}" +
                "]}"));
        }

        [Test]
        public void Groups_can_be_marked_inactive_without_touching_values()
        {
            var r = new ParamRegistry();
            ParamEntry period = r.DeclareFloat("glued.period", "glued", 1f, 4f, 2.6f);
            ParamEntry scale = r.DeclareFloat("render.scale", "render", 0.6f, 0.8f, 0.8f);
            string changedGroup = null;
            bool changedTo = true;
            r.GroupActiveChanged += (g, a) => { changedGroup = g; changedTo = a; };
            int manifest = r.ManifestVersion;

            Assert.That(period.Active, Is.True);
            r.SetGroupActive("glued", false);
            Assert.That(period.Active, Is.False);
            Assert.That(scale.Active, Is.True);
            Assert.That(r.IsGroupActive("glued"), Is.False);
            Assert.That(changedGroup, Is.EqualTo("glued"));
            Assert.That(changedTo, Is.False);
            Assert.That(r.ManifestVersion, Is.GreaterThan(manifest));

            Assert.That(period.Set(3f), Is.True, "an inactive parameter can still be set");
            Assert.That(r.DeclareInt("glued.copies", "glued", 3, 5, 5).Active, Is.False, "declared into an inactive group");
            Assert.That(ParamManifestMessage.FromRegistry(r).Params[0].Active, Is.False);

            changedGroup = null;
            r.SetGroupActive("glued", false);
            Assert.That(changedGroup, Is.Null, "no event without a change");
            r.SetGroupActive("glued", true);
            Assert.That(period.Active, Is.True);
            Assert.That(period.Float, Is.EqualTo(3f));
        }

        [Test]
        public void ParamSet_applies_clamped_and_skips_unknown_ids()
        {
            var r = new ParamRegistry();
            ParamEntry rho = r.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry copies = r.DeclareInt("glued.copies", "glued", 3, 5, 5);
            var set = new ParamSetMessage();
            set.Add(rho.Id, 100f);
            set.Add(999, 1f);
            set.Add(copies.Id, 3f);
            ParamSource seen = ParamSource.Local;
            r.Changed += (e, s) => seen = s;

            Assert.That(r.Apply(set), Is.EqualTo(2));
            Assert.That(seen, Is.EqualTo(ParamSource.Remote));
            Assert.That(rho.Float, Is.EqualTo(3.5f));
            Assert.That(copies.Int, Is.EqualTo(3));

            var all = new ParamSetMessage();
            Assert.That(r.FillParamSet(all), Is.EqualTo(2));
            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(all.Ids[1], Is.EqualTo(copies.Id));
            Assert.That(all.Values[0], Is.EqualTo(3.5f));
        }

        [Test]
        public void FillParamSet_pages_through_a_large_registry()
        {
            var r = new ParamRegistry();
            for (int i = 0; i < 300; i++) r.DeclareFloat("p" + i, "g", 0f, 1000f, i);
            var set = new ParamSetMessage();
            int next = r.FillParamSet(set, 0);
            Assert.That(next, Is.EqualTo(255));
            Assert.That(set.Count, Is.EqualTo(255));
            next = r.FillParamSet(set, next);
            Assert.That(next, Is.EqualTo(300));
            Assert.That(set.Count, Is.EqualTo(45));
            Assert.That(set.Ids[0], Is.EqualTo((ushort)255));
            Assert.That(set.Values[44], Is.EqualTo(299f));
        }
    }
}
