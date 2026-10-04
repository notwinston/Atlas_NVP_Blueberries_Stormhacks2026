using System.Collections.Generic;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Blades.FieldFit.Tests
{
    // FieldFit's two lane D bindings against the real ParamRegistry and EventLog (Master Plan §6.5, §7.3; Brief §4 R9, R10).
    public class LensServicesTests
    {
        [TearDown] public void TearDown() { AtlasServices.Clear(); }

        static ParamRegistry Declared()
        {
            var r = new ParamRegistry();
            r.DeclareFloat("lens.E", "lens", 30f, 70f, 54f);
            r.DeclareFloat("lens.rho0", "lens", 0f, 30f, 10f);
            r.DeclareFloat("lens.e1", "lens", 0f, 20f, 4f);
            r.DeclareFloat("lens.t", "lens", 0f, 1f, 1f);
            r.DeclareInt("lens.profile", "lens", 0, 2, 1);
            r.DeclareBool("lens.on", "lens", false);
            return r;
        }

        [Test]
        public void Source_reads_the_six_registered_values()
        {
            var r = Declared();
            r.Set("lens.E", 50f); r.Set("lens.profile", 2f); r.Set("lens.on", 1f);
            var s = new RegistryLensSource(r);

            Assert.IsTrue(s.Resolved);
            Assert.IsTrue(s.TryRead(out float e, out float rho0, out float e1, out float t, out int profile, out bool on));
            Assert.AreEqual(50f, e); Assert.AreEqual(10f, rho0); Assert.AreEqual(4f, e1); Assert.AreEqual(1f, t);
            Assert.AreEqual(2, profile); Assert.IsTrue(on);
        }

        [Test]
        public void Source_is_unresolved_while_any_lens_parameter_is_undeclared()
        {
            var r = new ParamRegistry();
            r.DeclareFloat("lens.E", "lens", 30f, 70f, 54f);
            var s = new RegistryLensSource(r);

            Assert.IsFalse(s.Resolved);
            Assert.IsFalse(s.TryRead(out _, out _, out _, out _, out _, out bool on));
            Assert.IsFalse(on);
            Assert.DoesNotThrow(() => s.SetLensOn(true));
            Assert.IsFalse(new RegistryLensSource(null).Resolved);
        }

        [Test]
        public void SetLensOn_writes_the_registry_entry_like_a_menu_change()
        {
            var r = Declared();
            r.Set("lens.on", 1f);
            var s = new RegistryLensSource(r);

            s.SetLensOn(false);

            Assert.IsFalse(r.Find("lens.on").Bool);
            s.TryRead(out _, out _, out _, out _, out _, out bool on);
            Assert.IsFalse(on);
        }

        [Test]
        public void CreateSource_takes_the_registry_from_the_services()
        {
            Assert.IsNull(LensParamBinding.CreateSource(), "no registry published");
            AtlasServices.Register(new ParamRegistry());
            Assert.IsNull(LensParamBinding.CreateSource(), "lens.* not declared");
            AtlasServices.Replace(Declared());
            Assert.IsNotNull(LensParamBinding.CreateSource());
        }

        [Test]
        public void Sink_logs_under_the_blade_scene_and_skips_when_there_is_no_log()
        {
            var sink = LensParamBinding.CreateSink();
            Assert.DoesNotThrow(() => sink.Emit("lensOn", "{}"));

            var logged = new List<EventMessage>();
            var log = new EventLog(new ManualClock(7000), null) { Scene = "S0" };
            log.Logged += logged.Add;
            AtlasServices.Register<IEventLog>(log);

            sink.Emit("lensOff", "{\"cause\":\"tap\"}");

            Assert.AreEqual(1, logged.Count);
            Assert.AreEqual("Blade_FieldFit", logged[0].Scene);
            Assert.AreEqual("lensOff", logged[0].Ev);
            Assert.AreEqual("tap", logged[0].Data.GetString("cause"));
            Assert.AreEqual("S0", log.Scene);
        }
    }
}
