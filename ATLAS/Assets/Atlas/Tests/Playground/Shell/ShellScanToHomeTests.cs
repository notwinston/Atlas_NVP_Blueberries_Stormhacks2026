using System.Collections.Generic;
using Atlas.Blades.Playground.Shell;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests.ShellFlow
{
    using static Atlas.Blades.Playground.Tests.ShellFlow.ShellWalkFakes;
    /// <summary>P2-C02: beats 1 to 3, the placeholder audit and the swap drift.</summary>
    public class ShellScanToHomeTests
    {
        static RoomModel Table() { return RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table); }

        [Test]
        public void Beats_AreNumberedAsThePlanTable()
        {
            Assert.That((int)PlaygroundBeat.Scan, Is.EqualTo(1));
            Assert.That((int)PlaygroundBeat.ReturnHome, Is.EqualTo(PlaygroundBeats.Count));
            Assert.That(PlaygroundBeats.Name(PlaygroundBeat.Walk345), Is.EqualTo("walk345"));
            Assert.That(PlaygroundBeats.Name((PlaygroundBeat)99), Is.EqualTo("none"));
        }

        [Test]
        public void Scan_EndsOnAValidRoomOnly()
        {
            var flow = new ScanToHome(new FakeContent(), new HeadStillness());
            IBeatStep scan = flow.Step(PlaygroundBeat.Scan);
            scan.Enter();
            Assert.That(scan.Complete, Is.False);

            Assert.That(flow.SetRoom(null), Is.False);
            var broken = Table();
            broken.Outline = new float2[0];
            Assert.That(flow.SetRoom(broken), Is.False);
            Assert.That(flow.RoomErrors, Is.Not.Empty);
            Assert.That(scan.Complete, Is.False);

            Assert.That(flow.SetRoom(Table()), Is.True);
            Assert.That(flow.RoomErrors, Is.Empty);
            Assert.That(scan.Complete, Is.True);
        }

        [Test]
        public void Scan_FastPathKeepsARoomSetBeforeTheBeat()
        {
            var flow = new ScanToHome(new FakeContent(), new HeadStillness());
            flow.SetRoom(Table());
            IBeatStep scan = flow.Step(PlaygroundBeat.Scan);
            scan.Enter();
            Assert.That(scan.Complete, Is.True);
        }

        [Test]
        public void Home_NeedsConfirmNetworkAnswerAndEveryBoxDrawn()
        {
            var content = new FakeContent();
            var flow = new ScanToHome(content, new HeadStillness());
            IBeatStep home = flow.Step(PlaygroundBeat.HomeAppears);
            home.Enter();

            flow.Confirm();    // nothing built yet: the press does not count
            Assert.That(flow.Confirmed, Is.False);

            RoomModel room = Table();
            Assert.That(room.Objects.Length, Is.GreaterThan(0));
            flow.SetRoom(room);
            content.Build(room);
            flow.Confirm();
            Assert.That(flow.Confirmed, Is.True);
            Assert.That(home.Complete, Is.False, "the Local Network prompt is still open");

            flow.LocalNetworkAnswered = true;
            Assert.That(home.Complete, Is.True);

            content.HideViews = true;
            Assert.That(home.Complete, Is.False, "a box without a view is not drawn");
        }

        [Test]
        public void Home_EnterForgetsAnEarlierConfirm()
        {
            var content = new FakeContent();
            var flow = new ScanToHome(content, new HeadStillness());
            content.Build(Table());
            flow.LocalNetworkAnswered = true;
            IBeatStep home = flow.Step(PlaygroundBeat.HomeAppears);
            home.Enter();
            flow.Confirm();
            Assert.That(home.Complete, Is.True);
            home.Enter();
            Assert.That(home.Complete, Is.False);
        }

        [Test]
        public void Audit_EveryBoxIsAPlaceholderOrAMesh()
        {
            var content = new FakeContent();
            RoomModel room = Table();
            content.Build(room);
            int n = room.Objects.Length;

            HomeAuditResult all = HomeAudit.Count(content);
            Assert.That(all.Objects, Is.EqualTo(n));
            Assert.That(all.Placeholders, Is.EqualTo(n));
            Assert.That(all.Fraction, Is.EqualTo(1f));
            Assert.That(all.AllDrawn, Is.True);

            content.SetObjectState(room.Objects[0].Id, MeshState.Requested);
            content.Views[room.Objects[0].Id].State = MeshState.Ready;
            HomeAuditResult mixed = HomeAudit.Count(content);
            Assert.That(mixed.Meshes, Is.EqualTo(1));
            Assert.That(mixed.Placeholders, Is.EqualTo(n - 1));
            Assert.That(mixed.Fraction, Is.EqualTo(1f), "100%: placeholder or mesh, never nothing");
        }

        [Test]
        public void Audit_CountsAMissingViewAsNotDrawn()
        {
            var content = new FakeContent();
            content.Build(Table());
            content.HideViews = true;
            HomeAuditResult r = HomeAudit.Count(content);
            Assert.That(r.Drawn, Is.EqualTo(0));
            Assert.That(r.Fraction, Is.EqualTo(0f));
            Assert.That(HomeAudit.Count(new FakeContent()).Objects, Is.EqualTo(0), "no room, nothing to draw");
        }

        [Test]
        public void Headset_NeedsStereoAndAStillHead()
        {
            var still = new HeadStillness();
            var flow = new ScanToHome(new FakeContent(), still);
            IBeatStep headset = flow.Step(PlaygroundBeat.HeadsetOn);
            headset.Enter();

            double t = Stand(still, 0.0, 2.0);
            Assert.That(headset.Complete, Is.False, "stereo is not on");

            flow.StereoOn = true;
            Assert.That(headset.Complete, Is.True);

            t = Walk(still, t, 0.5, 0.5f);
            Assert.That(headset.Complete, Is.False, "the head is moving");

            Stand(still, t, 0.5, 5f);
            Assert.That(headset.Complete, Is.False, "still for under a second");
            Stand(still, t + 0.5, 1.0, 5f);
            Assert.That(headset.Complete, Is.True);
        }

        [Test]
        public void Headset_LostTrackingIsNotStable()
        {
            var still = new HeadStillness();
            var flow = new ScanToHome(new FakeContent(), still) { StereoOn = true };
            double t = Stand(still, 0.0, 2.0);
            Assert.That(flow.HeadsetComplete, Is.True);
            still.Update(Pose(t, 0f, 0f, TrackingState.Limited));
            Assert.That(flow.HeadsetComplete, Is.False);
        }

        [Test]
        public void Step_IsNullOutsideItsBeats()
        {
            var flow = new ScanToHome(new FakeContent(), new HeadStillness());
            Assert.That(flow.Step(PlaygroundBeat.Dial), Is.Null);
            Assert.That(flow.Step(PlaygroundBeat.Scan).Beat, Is.EqualTo(PlaygroundBeat.Scan));
        }

        [Test]
        public void SwapMonitor_AFaithfulSwapMovesTheCentreByNothing()
        {
            RoomModel room = Table();
            var monitor = new SwapMonitor();
            monitor.Baseline(room);
            foreach (RoomObject o in room.Objects) monitor.Observe(o, o.Base);
            Assert.That(monitor.Swaps, Is.EqualTo(room.Objects.Length));
            Assert.That(monitor.MaxShiftM, Is.EqualTo(0f));
            Assert.That(monitor.AllWithin, Is.True);
        }

        [Test]
        public void SwapMonitor_FlagsAShiftOfFiveCentimetres()
        {
            RoomModel room = Table();
            RoomObject o = room.Objects[0];
            var monitor = new SwapMonitor();
            monitor.Baseline(room);

            float ok = monitor.Observe(o, o.Base + new float3(0.049f, 0f, 0f));
            Assert.That(ok, Is.EqualTo(0.049f).Within(1e-5f));
            Assert.That(monitor.AllWithin, Is.True);

            monitor.Observe(o, o.Base + new float3(0f, 0f, 0.05f));
            Assert.That(monitor.Violations, Is.EqualTo(1));
            Assert.That(monitor.MaxShiftM, Is.EqualTo(0.05f).Within(1e-5f));
            Assert.That(monitor.AllWithin, Is.False);
        }

        [Test]
        public void SwapMonitor_NaNPositionIsAViolationAndUnknownObjectIsNaN()
        {
            RoomModel room = Table();
            var monitor = new SwapMonitor();
            monitor.Baseline(room);
            monitor.Observe(room.Objects[0], new float3(float.NaN, 0f, 0f));
            Assert.That(monitor.AllWithin, Is.False);

            var stranger = new RoomObject { Id = 250, Size = new float3(1f, 1f, 1f) };
            Assert.That(float.IsNaN(monitor.Observe(stranger, float3.zero)), Is.True);
        }

        [Test]
        public void SwapMonitor_BoxCentreIsBasePlusHalfTheHeight()
        {
            var o = new RoomObject { Id = 1, Base = new float3(1f, 0f, 2f), Size = new float3(0.5f, 0.8f, 0.5f) };
            Assert.That(SwapMonitor.BoxCentre(o, o.Base), Is.EqualTo(new float3(1f, 0.4f, 2f)));
        }

        [Test]
        public void SwapMonitor_BaselineResetsTheCounters()
        {
            RoomModel room = Table();
            var monitor = new SwapMonitor();
            monitor.Baseline(room);
            monitor.Observe(room.Objects[0], room.Objects[0].Base + new float3(1f, 0f, 0f));
            Assert.That(monitor.AllWithin, Is.False);
            monitor.Baseline(room);
            Assert.That(monitor.Swaps, Is.EqualTo(0));
            Assert.That(monitor.AllWithin, Is.True);
        }
    }
}
