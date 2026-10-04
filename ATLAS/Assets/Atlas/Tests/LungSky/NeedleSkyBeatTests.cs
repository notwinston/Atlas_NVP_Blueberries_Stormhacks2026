using Atlas.Blades.Lung;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public class NeedleSkyBeatTests
    {
        const float Dt = 0.01f;
        SkyMaps _maps; FakeSkyEvents _ev; FakePresenter _p; ParamRegistry _reg; NeedleSkyBeat _b; long _f;

        [SetUp]
        public void SetUp()
        {
            _maps = SkyMapsFactory.Fixture(); _ev = new FakeSkyEvents(); _p = new FakePresenter(); _reg = new ParamRegistry();
            _b = new NeedleSkyBeat(_maps, _reg, _ev, null, _p);
        }

        void Tick(int n = 1, quaternion? rot = null)
        {
            for (int k = 0; k < n; k++) _b.Tick(SkyTestEnv.Frame(_f++, _f * Dt, Dt, float3.zero, rot ?? quaternion.identity));
        }

        void Set(string id, float v) { Assert.IsNotNull(_reg.Find(id), id); _reg.Set(_reg.Find(id), v); }

        [Test] // MP §10.2.2 "click on sky panel"; via ParamSet
        public void B10_ConsoleTwin_Params()
        {
            foreach (var id in new[] { "lung.skyOn", "lung.skyPhase", "lung.skyRobust", "lung.skyPickAz", "lung.skyPickEl", "lung.skyPick" })
                Assert.IsNotNull(_reg.Find(id), id);
            _b.Enter();
            Set("lung.skyPickAz", 30); Set("lung.skyPickEl", 20); Set("lung.skyPick", 1);
            Tick();
            Assert.AreEqual(1, _ev.Count("sky_pick"));
            Assert.AreEqual(30f, _ev.Pick(0).az_deg, 1e-4); Assert.AreEqual(20f, _ev.Pick(0).el_deg, 1e-4);
            Tick(); Assert.AreEqual(1, _ev.Count("sky_pick"));

            var ev2 = new FakeSkyEvents(); var reg2 = new ParamRegistry();
            var b2 = new NeedleSkyBeat(_maps, reg2, ev2, null, null);
            reg2.Set(reg2.Find("lung.skyPick"), 5f);
            b2.Enter();
            b2.Tick(SkyTestEnv.Frame(0, 0, Dt, float3.zero, quaternion.identity));
            Assert.AreEqual(0, ev2.Count("sky_pick"));
        }

        [Test] // P2-B10 "phase and robust layers"
        public void B10_PhaseAndRobustLayers()
        {
            _b.Enter();
            Set("lung.skyPhase", 7); Tick();
            Assert.AreEqual(7, _p.Last.Phase); Assert.AreEqual(7, _b.ActivePhaseLayer);
            Set("lung.skyRobust", 0); Tick();
            Assert.IsFalse(_p.Last.RobustOn); Assert.IsFalse(_b.RobustLayerOn);
            Set("lung.skyPhase", 12); Tick();
            Assert.AreEqual(9, _p.Last.Phase);
        }

        [Test] // G11 "kill switch for everything Phase 2 added"
        public void B10_KillSwitch()
        {
            _b.Enter();
            Set("lung.skyOn", 0);
            Tick(200);
            Assert.IsFalse(_p.Visible); Assert.IsFalse(_b.SkyOn);
            Assert.AreEqual(0, _ev.Count("sky_pick"));
        }

        [Test] // MP §0.5 rule 5: the beat reads the one action source, Confirm and the left stick
        public void B10_ReadsAtlasInput_ConfirmAndStick()
        {
            var input = new FakeAtlasInput();
            var ev = new FakeSkyEvents(); var reg = new ParamRegistry();
            var b = new NeedleSkyBeat(_maps, reg, ev, input, null);
            b.Enter();
            long f = 0;
            input.StickValue = new UnityEngine.Vector2(1f, 0f);
            for (int k = 0; k < 50; k++) b.Tick(SkyTestEnv.Frame(f++, f * Dt, Dt, float3.zero, quaternion.identity));
            SkyDirection.ToAzEl(b.Model.Cursor, out double az, out _);
            Assert.AreEqual(45.0, az, 0.5, "the stick moved the cursor");
            Assert.IsFalse(b.Model.CursorAttached);
            Assert.AreEqual(0, ev.Count("sky_pick"));
            input.StickValue = default; input.ConfirmNext = true;
            b.Tick(SkyTestEnv.Frame(f++, f * Dt, Dt, float3.zero, quaternion.identity));
            Assert.AreEqual(1, ev.Count("sky_pick"), "Confirm picked at the cursor");
            Assert.AreEqual(45.0, ev.Pick(0).az_deg, 0.5);
        }

        [Test] // P2-A32 interface (PH-13)
        public void B10_Beat_Contract()
        {
            Assert.AreEqual(LungBeat.G, _b.Beat);
            _b.Enter(); _b.Enter();
            Assert.IsTrue(_p.Visible);
            Tick(5); Assert.AreEqual(5, _p.PresentCalls);
            _b.Exit();
            Assert.IsFalse(_p.Visible);
            int n = _p.PresentCalls;
            Tick(5); Assert.AreEqual(n, _p.PresentCalls);
            Assert.AreEqual(0, _ev.Count("sky_pick"));
        }
    }
}
