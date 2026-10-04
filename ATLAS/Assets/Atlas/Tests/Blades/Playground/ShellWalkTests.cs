using System;
using System.Collections.Generic;
using Atlas.Blades.Playground.Shell;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Blades.Playground.Tests.ShellFakes;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C03 and P2-C04: the centre mark, the standing lock, the guided walk and the floor arrows.</summary>
    public class ShellWalkTests
    {
        static RoomModel Room() { return RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table); }

        // ---- HeadStillness --------------------------------------------------------------------------------

        [Test]
        public void Stillness_StandingAtOrBelowTenCentimetresPerSecond()
        {
            var still = new HeadStillness();
            double t = Stand(still, 0.0, 1.0);
            Assert.That(still.Standing, Is.True);
            Assert.That(still.StillSeconds, Is.GreaterThan(0.9));

            t = Walk(still, t, 1.0, 0.09f);
            Assert.That(still.Standing, Is.True, "0.09 m/s is below the limit");

            Walk(still, t, 1.0, 0.2f, 0f);
            Assert.That(still.Standing, Is.False);
            Assert.That(still.StillSeconds, Is.EqualTo(0.0));
        }

        [Test]
        public void Stillness_NoTrackingIsNotStanding()
        {
            var still = new HeadStillness();
            Stand(still, 0.0, 1.0);
            still.Update(Pose(2.0, 0f, 0f, TrackingState.NotTracking));
            Assert.That(still.Standing, Is.False);
            Assert.That(float.IsInfinity(still.Speed), Is.True);
        }

        // ---- Centre mark ----------------------------------------------------------------------------------

        [Test]
        public void Centre_EndsWithinThirtyCentimetresOfC0()
        {
            var mark = new CentreMark();
            mark.Enter();
            mark.Update(new float2(1f, 1f));
            Assert.That(mark.Complete, Is.False);
            mark.Update(new float2(0.31f, 0f));
            Assert.That(mark.Complete, Is.False);
            mark.Update(new float2(0.18f, 0.24f));    // 0.30 m exactly
            Assert.That(mark.Complete, Is.True);
            Assert.That(mark.Distance, Is.EqualTo(0.3f).Within(1e-5f));
            mark.Enter();
            Assert.That(mark.Complete, Is.False);
        }

        [Test]
        public void Centre_NaNNeverCompletes()
        {
            var mark = new CentreMark();
            mark.Update(new float2(float.NaN, 0f));
            Assert.That(mark.Complete, Is.False);
        }

        [Test]
        public void Centre_DiscIsAFlatFanAtTheOrigin()
        {
            var mesh = new AtlasMeshBuilder(64, 256);
            CentreMark.Build(mesh);
            Assert.That(mesh.TriangleCount, Is.EqualTo(CentreMark.Segments));
            Assert.That(mesh.BoundsCentre.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(mesh.BoundsCentre.z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(mesh.BoundsSize.x, Is.EqualTo(2f * CentreMark.EnterRadius).Within(1e-4f));
            Assert.That(mesh.BoundsMin.y, Is.EqualTo(CentreMark.Height));
            Assert.That(mesh.BoundsMax.y, Is.EqualTo(CentreMark.Height));
            AssertFacesUp(mesh);
        }

        // ---- Dial hand-off --------------------------------------------------------------------------------

        [Test]
        public void Dial_IgnoredAboveTenCentimetresPerSecondInTenOfTenTries()
        {
            var dial = new FakeDial();
            var still = new HeadStillness();
            var hand = new DialHandOff(dial, still);
            Walk(still, 0.0, 1.0, 0.25f);
            Assert.That(still.Standing, Is.False);

            int ignored = 0;
            for (int k = 0; k < 10; k++)
            {
                DialOutcome o = k % 4 == 0 ? hand.Select(5) : k % 4 == 1 ? hand.Next() : k % 4 == 2 ? hand.Prev() : hand.Cycle();
                if (o == DialOutcome.IgnoredMoving) ignored++;
            }
            Assert.That(ignored, Is.EqualTo(10));
            Assert.That(hand.Ignored, Is.EqualTo(10));
            Assert.That(dial.Calls, Is.EqualTo(0), "the dial was never asked");
            Assert.That(dial.Q, Is.EqualTo(4));
        }

        [Test]
        public void Dial_AppliedWhileStanding_AndRequestsAreNotQueued()
        {
            var dial = new FakeDial();
            var still = new HeadStillness();
            var hand = new DialHandOff(dial, still);
            double t = Walk(still, 0.0, 1.0, 0.25f);
            hand.Select(6);                              // dropped
            t = Stand(still, t, 1.0, 10f);
            Assert.That(dial.Q, Is.EqualTo(4), "a dropped request does not come back when the walker stops");

            Assert.That(hand.Select(5), Is.EqualTo(DialOutcome.Applied));
            Assert.That(hand.Select(5), Is.EqualTo(DialOutcome.Unchanged));
            Assert.That(hand.Next(), Is.EqualTo(DialOutcome.Applied));
            Assert.That(hand.Q, Is.EqualTo(6));
            Assert.That(hand.Next(), Is.EqualTo(DialOutcome.Unchanged), "clamped at 6");
            Assert.That(hand.Cycle(), Is.EqualTo(DialOutcome.Applied));
            Assert.That(hand.Q, Is.EqualTo(3));
            Assert.That(hand.Applied, Is.EqualTo(3));
        }

        [Test]
        public void Dial_UntrackedHeadIsLocked()
        {
            var dial = new FakeDial();
            var hand = new DialHandOff(dial, new HeadStillness());
            Assert.That(hand.Select(5), Is.EqualTo(DialOutcome.IgnoredMoving), "no pose yet: not standing");
        }

        [Test]
        public void Dial_SelectRejectsQOutsideThreeToSix()
        {
            var hand = new DialHandOff(new FakeDial(), new HeadStillness());
            Assert.Throws<ArgumentOutOfRangeException>(() => hand.Select(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => hand.Select(7));
        }

        [Test]
        public void Dial_EveryFixtureCanSelectQ3To6()
        {
            foreach (string name in RoomFixtures.All)
                Assert.That(DialHandOff.CanSelectAll(RoomFixtures.LoadFromProject(name)), Is.True, name);
            Assert.That(DialHandOff.CanSelectAll(new RoomModel()), Is.False, "no loop-fit: no dial");
        }

        [Test]
        public void DialBeat_EndsWhenQ5IsSelectedInsideTheBeat()
        {
            var dial = new FakeDial { };
            var still = new HeadStillness();
            Stand(still, 0.0, 1.0);
            var hand = new DialHandOff(dial, still);
            var beat = new DialBeat(hand);
            beat.Enter();
            beat.Update();
            Assert.That(beat.Complete, Is.False);

            hand.Select(3);
            beat.Update();
            Assert.That(beat.Complete, Is.False);
            hand.Select(5);
            beat.Update();
            Assert.That(beat.Complete, Is.True);

            beat.Enter();
            beat.Update();
            Assert.That(beat.Complete, Is.False, "q is already 5 but nothing was selected in this visit");
        }

        // ---- Guided walk ----------------------------------------------------------------------------------

        sealed class Rig
        {
            public readonly FakeWalk Walk = new FakeWalk();
            public readonly FakeDial Dial = new FakeDial();
            public readonly HeadStillness Still = new HeadStillness();
            public readonly RecordingLog Log = new RecordingLog();
            public readonly GuidedWalk Guided;
            public readonly RoomModel Room = ShellWalkTests.Room();

            public Rig()
            {
                Guided = new GuidedWalk(Walk, Dial, Still, Log);
                Guided.SetRoom(Room);
                Stand(Still, 0.0, 1.0);
            }

            public void Run(int steps)
            {
                for (int k = 0; k < steps; k++) Guided.Update();
            }
        }

        [Test]
        public void Walk_EnterArmsWithTheRoomsLapSideAndTheDialsLengthScale()
        {
            var rig = new Rig();
            rig.Dial.Set(5);
            rig.Guided.Enter();
            Assert.That(rig.Walk.ArmedQ, Is.EqualTo(5));
            Assert.That(rig.Walk.ArmedKappa, Is.EqualTo(-1.0));
            Assert.That(rig.Walk.ArmedScale, Is.EqualTo(rig.Room.Curvature.ForDial(5)).Within(1e-9));
            Assert.That(rig.Walk.ArmedSide, Is.EqualTo(rig.Room.LapSide).Within(1e-9));

            rig.Dial.Set(3);
            rig.Guided.Update();
            Assert.That(rig.Walk.ArmedQ, Is.EqualTo(3), "a dial change re-arms the walk");
            Assert.That(rig.Walk.ArmedKappa, Is.EqualTo(+1.0));
            Assert.That(rig.Walk.ArmedScale, Is.EqualTo(rig.Room.Curvature.ForDial(3)).Within(1e-9));

            rig.Dial.Set(4);
            rig.Guided.Update();
            Assert.That(rig.Walk.ArmedKappa, Is.EqualTo(0.0));
            Assert.That(rig.Walk.ArmedScale, Is.EqualTo(1.0));
        }

        [Test]
        public void Walk_ArmNeedsARoom()
        {
            var g = new GuidedWalk(new FakeWalk(), new FakeDial(), new HeadStillness(), null);
            Assert.Throws<InvalidOperationException>(() => g.Enter());
        }

        [Test]
        public void Walk_PassesStillnessToTheTracker()
        {
            var rig = new Rig();
            rig.Guided.Enter();
            rig.Guided.Update();
            Assert.That(rig.Walk.StillSeen, Is.True);
            Walk(rig.Still, 5.0, 1.0, 0.3f);
            rig.Guided.Update();
            Assert.That(rig.Walk.StillSeen, Is.False);
        }

        [Test]
        public void Walk_LoopAtQ5LogsCornersLapAndClosed()
        {
            var rig = new Rig();
            rig.Dial.Set(5);
            rig.Guided.Enter();
            rig.Walk.QueueLoop(5, 0.12, 90.4);
            rig.Run(6);

            Assert.That(rig.Log.Count("walk.corner"), Is.EqualTo(5));
            Assert.That(rig.Log.Count("walk.lap"), Is.EqualTo(1));
            Assert.That(rig.Log.Count("walk.closed"), Is.EqualTo(1));
            Assert.That(rig.Guided.Complete, Is.True);
            Assert.That(rig.Guided.ClosedLaps, Is.EqualTo(1));
            WalkLapResult lap = rig.Guided.LastLap;
            Assert.That(lap.Q, Is.EqualTo(5));
            Assert.That(lap.ClosureOk, Is.True);
            Assert.That(lap.TurnOk, Is.True);
            Assert.That(rig.Log.Json[rig.Log.Events.IndexOf("walk.closed")], Does.Contain("\"ok\":true"));
            Assert.That(rig.Log.Json[rig.Log.Events.IndexOf("walk.closed")], Does.Contain("\"closure\":0.12"));
        }

        [Test]
        public void Walk_ClosureAndTurnTolerances()
        {
            var rig = new Rig();
            rig.Dial.Set(5);
            rig.Guided.Enter();
            rig.Walk.QueueLoop(5, 0.25, 85.0);
            rig.Run(6);
            Assert.That(rig.Guided.LastLap.ClosureOk, Is.True, "0.25 m is the limit, inclusive");
            Assert.That(rig.Guided.LastLap.TurnOk, Is.True, "90 - 5");

            var bad = new Rig();
            bad.Dial.Set(5);
            bad.Guided.Enter();
            bad.Walk.QueueLoop(5, 0.26, 95.5);
            bad.Run(6);
            Assert.That(bad.Guided.LastLap.ClosureOk, Is.False);
            Assert.That(bad.Guided.LastLap.TurnOk, Is.False);
            Assert.That(bad.Log.Json[bad.Log.Events.IndexOf("walk.closed")], Does.Contain("\"ok\":false"));
            Assert.That(bad.Guided.Complete, Is.True, "the beat ends on closing, not on a good closure");
        }

        [Test]
        public void Walk_TurnSignIsNotJudged()
        {
            var rig = new Rig();
            rig.Dial.Set(5);
            rig.Guided.Enter();
            rig.Walk.QueueLoop(5, 0.1, -90.0);
            rig.Run(6);
            Assert.That(rig.Guided.LastLap.TurnOk, Is.True);
        }

        [Test]
        public void Walk_RepeatsAtQ3AndQ4WithoutEndingTheBeat()
        {
            var rig = new Rig();
            rig.Dial.Set(4);
            rig.Guided.Enter();
            rig.Walk.QueueLoop(4, 0.05, 0.0);
            rig.Run(5);
            Assert.That(rig.Guided.ClosedLaps, Is.EqualTo(1));
            Assert.That(rig.Guided.Complete, Is.False);
            Assert.That(rig.Guided.LastLap.TurnOk, Is.True, "no turn is asked for at q = 4");

            rig.Dial.Set(3);
            rig.Guided.Update();
            rig.Walk.QueueLoop(3, 0.2, 120.0);
            rig.Run(4);
            Assert.That(rig.Guided.ClosedLaps, Is.EqualTo(2));
            Assert.That(rig.Guided.LastLap.Lap, Is.EqualTo(1), "laps count per q");
            Assert.That(rig.Guided.Complete, Is.False);

            rig.Dial.Set(5);
            rig.Guided.Update();
            rig.Walk.QueueLoop(5, 0.1, 90.0);
            rig.Run(6);
            Assert.That(rig.Guided.Complete, Is.True);
            Assert.That(rig.Guided.ClosedLaps, Is.EqualTo(3));
        }

        [Test]
        public void Walk_ProbeIsDueAfterTheThirdLegAtQ4AndQ5Only()
        {
            var rig = new Rig();
            var due = new List<int>();
            rig.Guided.ProbeDue += q => due.Add(q);

            rig.Dial.Set(3);
            rig.Guided.Enter();
            rig.Walk.QueueLoop(3, 0.1, 0);
            rig.Run(4);
            Assert.That(due, Is.Empty, "q = 3 takes no probe");

            rig.Dial.Set(4);
            rig.Guided.Update();
            rig.Walk.QueueLoop(4, 0.1, 0);
            rig.Run(5);
            Assert.That(due, Is.EqualTo(new[] { 4 }));

            rig.Dial.Set(5);
            rig.Guided.Update();
            rig.Walk.QueueLoop(5, 0.1, 90);
            rig.Run(6);
            Assert.That(due, Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void Walk_LeaveDisarmsAndEnterStartsAgain()
        {
            var rig = new Rig();
            rig.Dial.Set(5);
            rig.Guided.Enter();
            rig.Walk.QueueLoop(5, 0.1, 90);
            rig.Run(6);
            Assert.That(rig.Guided.Complete, Is.True);
            rig.Guided.Leave();
            Assert.That(rig.Walk.Armed, Is.False);
            Assert.That(rig.Guided.ClosedLaps, Is.EqualTo(1), "the count stays for the later beats");
            rig.Guided.Enter();
            Assert.That(rig.Guided.Complete, Is.False);
            Assert.That(rig.Guided.ClosedLaps, Is.EqualTo(0));
            Assert.That(rig.Walk.Armed, Is.True);
        }

        [Test]
        public void Walk_WorksWithNoLog()
        {
            var walk = new FakeWalk();
            var dial = new FakeDial();
            dial.Set(5);
            var still = new HeadStillness();
            var g = new GuidedWalk(walk, dial, still, null);
            g.SetRoom(Room());
            g.Enter();
            walk.QueueLoop(5, 0.1, 90);
            for (int k = 0; k < 6; k++) g.Update();
            Assert.That(g.Complete, Is.True);
        }

        [Test]
        public void Walk_EventsCarryInvariantNumbers()
        {
            var rig = new Rig();
            rig.Dial.Set(5);
            rig.Guided.Enter();
            rig.Walk.Script.Enqueue(new FakeWalk.Step { Event = WalkEvent.Corner, Distance = 1.23456, Turn = double.NaN });
            rig.Guided.Update();
            string json = rig.Log.Json[0];
            Assert.That(json, Does.Contain("\"dist\":1.235"));
            Assert.That(json, Does.Contain("\"turn\":null"));
        }

        // ---- Floor arrows ---------------------------------------------------------------------------------

        [Test]
        public void Arrows_LeadFromTheHeadTowardsTheBeaconAndStopShort()
        {
            var buffer = new ArrowPlacement[FloorArrows.MaxCount];
            int n = FloorArrows.Layout(float2.zero, new float2(0f, 1f), 0.0, 3.0, buffer);
            Assert.That(n, Is.EqualTo(FloorArrows.MaxCount));
            Assert.That(buffer[0].Position.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(buffer[0].Position.y, Is.EqualTo(FloorArrows.FirstOffset).Within(1e-5f));
            Assert.That(buffer[1].Position.y - buffer[0].Position.y, Is.EqualTo(FloorArrows.Spacing).Within(1e-5f));

            int near = FloorArrows.Layout(float2.zero, new float2(0f, 1f), 0.0, 1.2, buffer);
            Assert.That(near, Is.EqualTo(1), "0.8 m fits before 1.2 - 0.3 m, 1.3 m does not");
            Assert.That(FloorArrows.Layout(float2.zero, new float2(0f, 1f), 0.0, 0.3, buffer), Is.EqualTo(0));
        }

        [Test]
        public void Arrows_BearingIsClockwiseFromTheHeading()
        {
            var buffer = new ArrowPlacement[FloorArrows.MaxCount];
            FloorArrows.Layout(float2.zero, new float2(0f, 1f), math.PI / 2.0, 3.0, buffer);
            Assert.That(buffer[0].Direction.x, Is.EqualTo(1f).Within(1e-5f), "a beacon to the right lies towards +X");
            Assert.That(buffer[0].Direction.y, Is.EqualTo(0f).Within(1e-5f));

            FloorArrows.Layout(new float2(2f, 3f), new float2(2f, 0f), math.PI, 3.0, buffer);
            Assert.That(buffer[0].Direction.x, Is.EqualTo(-1f).Within(1e-5f), "behind the walker");
            Assert.That(buffer[0].Position.x, Is.EqualTo(2f - FloorArrows.FirstOffset).Within(1e-5f));
        }

        [Test]
        public void Arrows_NothingForBadInput()
        {
            var buffer = new ArrowPlacement[FloorArrows.MaxCount];
            Assert.That(FloorArrows.Layout(float2.zero, new float2(0f, 1f), double.NaN, 3.0, buffer), Is.EqualTo(0));
            Assert.That(FloorArrows.Layout(float2.zero, new float2(0f, 1f), 0.0, double.NaN, buffer), Is.EqualTo(0));
            Assert.That(FloorArrows.Layout(float2.zero, float2.zero, 0.0, 3.0, buffer), Is.EqualTo(0));
            Assert.That(FloorArrows.Layout(float2.zero, new float2(0f, 1f), 0.0, 3.0, new ArrowPlacement[2]), Is.EqualTo(2));
        }

        [Test]
        public void Arrows_MeshIsOneFlatTriangleEachAndFacesUp()
        {
            var buffer = new ArrowPlacement[FloorArrows.MaxCount];
            int n = FloorArrows.Layout(float2.zero, new float2(1f, 0f), 0.3, 3.0, buffer);
            var mesh = new AtlasMeshBuilder(32, 64);
            FloorArrows.Build(buffer, n, mesh);
            Assert.That(mesh.TriangleCount, Is.EqualTo(n * FloorArrows.TrianglesEach));
            Assert.That(mesh.BoundsMax.y, Is.EqualTo(FloorArrows.Height));
            AssertFacesUp(mesh);
        }

        /// <summary>Every triangle faces the same way as the floor of the room shell: up.</summary>
        static void AssertFacesUp(AtlasMeshBuilder mesh)
        {
            var reference = new AtlasMeshBuilder(16, 64);
            reference.AddFloorGrid(new float2(0f, 0f), new float2(1f, 1f), 0f, float2.zero);
            float3 r0 = reference.Vertices[reference.Indices[0]].Position;
            float3 r1 = reference.Vertices[reference.Indices[1]].Position;
            float3 r2 = reference.Vertices[reference.Indices[2]].Position;
            float expected = math.sign(math.cross(r1 - r0, r2 - r0).y);
            Assert.That(expected, Is.Not.EqualTo(0f));
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                float3 a = mesh.Vertices[mesh.Indices[t]].Position;
                float3 b = mesh.Vertices[mesh.Indices[t + 1]].Position;
                float3 c = mesh.Vertices[mesh.Indices[t + 2]].Position;
                Assert.That(math.sign(math.cross(b - a, c - a).y), Is.EqualTo(expected), "triangle " + t / 3);
            }
        }
    }
}
