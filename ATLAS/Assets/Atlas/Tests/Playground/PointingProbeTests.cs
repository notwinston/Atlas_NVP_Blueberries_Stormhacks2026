using System;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C17: the pointing probe, its <c>probe</c> event and the SEQ entry.</summary>
    [TestFixture]
    public class PointingProbeTests
    {
        /// <summary>A bearing that is told, for the cases that need a number the flat kernel cannot give (S2).</summary>
        sealed class FixedBearing : IHomeBearing
        {
            public double Az;
            public bool Available = true;
            public int Marks;
            public void MarkStart() { Marks++; }
            public bool TryGet(out double azimuthDeg, out double distanceMetres)
            {
                azimuthDeg = Az;
                distanceMetres = 3.0;
                return Available;
            }
        }

        static double2 Forward(double yawDeg)
        {
            double r = yawDeg * Math.PI / 180.0;
            return new double2(Math.Sin(r), Math.Cos(r));
        }

        [Test]
        public void Probe_LogsTheSpecExampleExactly()
        {
            // Engineering Spec 13.1: ray 212.4, geodesic 148.0, Euclid 205.1, errors 64.4 and 7.3.
            var log = new RecordingEvents();
            var bearing = new FixedBearing { Az = 148.0 };
            var probe = new PointingProbe(bearing, log);
            double2 toStart = Forward(205.1) * 3.0;
            probe.Begin(toStart.x, toStart.y);
            Assert.That(bearing.Marks, Is.EqualTo(1));
            Assert.That(probe.Arm(0), Is.True);

            double2 f = Forward(212.4);
            Assert.That(probe.Capture(0, 0, f.x, f.y, 1), Is.True);

            Assert.That(log.Count("probe"), Is.EqualTo(1));
            Assert.That(log.Last("probe").Json, Is.EqualTo(
                "{\"ray_az\":212.4,\"pred_geodesic\":148.0,\"pred_euclid\":205.1,\"err_geo\":64.4,\"err_euc\":7.3}"));
            Assert.That(probe.IsArmed, Is.False);
            Assert.That(probe.Taken, Is.EqualTo(1));
        }

        [Test]
        public void Probe_DoesNothingUntilArmed_AndOnlyOncePerArming()
        {
            var log = new RecordingEvents();
            var probe = new PointingProbe(new FixedBearing(), log);
            Assert.That(probe.Arm(0), Is.False, "no start yet");
            probe.Begin(0, 1);
            Assert.That(probe.Capture(0, 0, 0, 1, 0), Is.False);
            probe.Arm(0);
            Assert.That(probe.Capture(0, 0, 0, 1, 1), Is.True);
            Assert.That(probe.Capture(0, 0, 0, 1, 2), Is.False);
            Assert.That(log.Count("probe"), Is.EqualTo(1));
        }

        [Test]
        public void Probe_StaysArmedWithoutAKernelBearingOrWhenLookingStraightUp()
        {
            var log = new RecordingEvents();
            var bearing = new FixedBearing { Available = false };
            var probe = new PointingProbe(bearing, log);
            probe.Begin(0, 3);
            probe.Arm(0);
            Assert.That(probe.Capture(0, 0, 0, 1, 1), Is.False);
            Assert.That(probe.IsArmed, Is.True);
            bearing.Available = true;
            Assert.That(probe.Capture(0, 0, 0, 0, 2), Is.False, "forward has no floor component");
            Assert.That(probe.Capture(0, 0, 0, 1, 3), Is.True);
        }

        [Test]
        public void Probe_AngleErrorWrapsAround()
        {
            var log = new RecordingEvents();
            var probe = new PointingProbe(new FixedBearing { Az = 350.0 }, log);
            probe.Begin(0, 1);
            probe.Arm(0);
            double2 f = Forward(10.0);
            probe.Capture(0, 0, f.x, f.y, 1);
            Assert.That(probe.Last.ErrGeo, Is.EqualTo(20.0).Within(0.05));
            Assert.That(probe.Last.ErrEuc, Is.EqualTo(10.0).Within(0.05));
        }

        [Test]
        public void Probe_GazeTwin_FiresAfterTheDwellWhenTheHeadHoldsStill()
        {
            var log = new RecordingEvents();
            var probe = new PointingProbe(new FixedBearing { Az = 0.0 }, log);
            probe.Begin(0, 3);
            probe.Arm(0);
            double2 f = Forward(2.0);
            double t = 0;
            bool fired = false;
            for (int i = 0; i < 90 && !fired; i++)
            {
                t += 1.0 / 90.0;
                fired = probe.TickDwell(0, 0, f.x, f.y, 1.0 / 90.0, t);
            }
            Assert.That(fired, Is.True);
            // The first frame anchors the ray; the dwell is 700 ms after that.
            Assert.That(t, Is.EqualTo(PointingProbe.DwellSeconds + 1.0 / 90.0).Within(0.02));
            Assert.That(log.Count("probe"), Is.EqualTo(1));
        }

        [Test]
        public void Probe_GazeTwin_RestartsWhenTheHeadMoves()
        {
            var log = new RecordingEvents();
            var probe = new PointingProbe(new FixedBearing(), log);
            probe.Begin(0, 3);
            probe.Arm(0);
            double t = 0;
            // Sweeping the head 10 degrees per frame never holds still.
            for (int i = 0; i < 400; i++)
            {
                t += 1.0 / 90.0;
                double2 f = Forward(i * 10.0);
                Assert.That(probe.TickDwell(0, 0, f.x, f.y, 1.0 / 90.0, t), Is.False);
            }
            Assert.That(log.Count("probe"), Is.EqualTo(0));
        }

        [Test]
        public void Probe_ReplayInS0_PredictionsMatchTheKernelWithinTwoDegrees()
        {
            // A 3-leg walk of side 3 m from the tile centre of a 4 m tile: the walker crosses tile edges, so the
            // start is found through the kernel's candidate chain, not by a shortcut.
            var kernel = new FlatWalkerKernel();
            kernel.Configure(KernelConfig.Flat(4.0));
            kernel.ResetToFloor(new double2(0, 0));
            var log = new RecordingEvents();
            var probe = new PointingProbe(new KernelHomeBearing(kernel), log);
            probe.Begin(0, 0);

            double2[] corners = { new double2(0, 3), new double2(3, 3), new double2(3, 0) };
            double2 head = new double2(0, 0);
            foreach (double2 corner in corners)
            {
                const int steps = 60;
                double2 from = head;
                for (int n = 1; n <= steps; n++)
                {
                    head = from + (corner - from) * (n / (double)steps);
                    Assert.That(kernel.Step(head, true).Rejected, Is.False);
                }
            }
            probe.Arm(0);

            // The walker looks at the start: from (3, 0) it lies along -x, azimuth 270.
            double2 f = Forward(270.0);
            Assert.That(probe.Capture(head.x, head.y, f.x, f.y, 1), Is.True);
            ProbeResult r = probe.Last;
            Assert.That(r.PredEuclid, Is.EqualTo(270.0).Within(2.0));
            Assert.That(r.PredGeodesic, Is.EqualTo(270.0).Within(2.0));
            Assert.That(ProbeGeometry.AngleBetweenDeg(r.PredGeodesic, r.PredEuclid), Is.LessThan(2.0));
            Assert.That(r.ErrGeo, Is.LessThan(2.0));
            Assert.That(r.ErrEuc, Is.LessThan(2.0));
            Assert.That(kernel.Pose.Tile, Is.Not.EqualTo(TileId.Origin), "the walk left the first tile");
        }

        [Test]
        public void KernelHomeBearing_FlatDistanceAndBearing_FromSeveralPlaces()
        {
            var kernel = new FlatWalkerKernel();
            kernel.Configure(KernelConfig.Flat(4.0));
            kernel.ResetToFloor(new double2(1, -1));
            var home = new KernelHomeBearing(kernel);
            home.MarkStart();
            double az, d;
            Assert.That(home.TryGet(out az, out d), Is.True, "standing on the start");
            Assert.That(d, Is.EqualTo(0.0).Within(1e-9));

            double2 head = new double2(1, -1);
            double2[] places = { new double2(4, -1), new double2(1, 2), new double2(-2, -1), new double2(4, 3) };
            foreach (double2 place in places)
            {
                double2 from = head;
                for (int n = 1; n <= 60; n++)
                {
                    head = from + (place - from) * (n / 60.0);
                    kernel.Step(head, true);
                }
                Assert.That(home.TryGet(out az, out d), Is.True);
                double2 back = new double2(1, -1) - head;
                Assert.That(ProbeGeometry.AngleBetweenDeg(az, ProbeGeometry.AzimuthDeg(back.x, back.y)), Is.LessThan(0.5));
                Assert.That(d, Is.EqualTo(math.length(back)).Within(0.01));
            }
        }

        [Test]
        public void KernelHomeBearing_WithoutAStart_HasNoAnswer()
        {
            var kernel = new FlatWalkerKernel();
            kernel.Configure(KernelConfig.Flat(4.0));
            double az, d;
            Assert.That(new KernelHomeBearing(kernel).TryGet(out az, out d), Is.False);
        }

        [Test]
        public void Seq_TakesAScoreFromOneToSevenAndClosesTheQuestion()
        {
            var prompts = new PromptQueue(new RecordingSink());
            var seq = new SeqCollector(prompts);
            seq.Ask(0);
            Assert.That(seq.Pending, Is.True);
            Assert.That(prompts.Current.Kind, Is.EqualTo(PromptKind.Seq));
            Assert.That(seq.Submit(0, 1), Is.False);
            Assert.That(seq.Submit(8, 1), Is.False);
            Assert.That(seq.Pending, Is.True);

            Assert.That(seq.Submit(6, 2), Is.True);
            Assert.That(seq.Pending, Is.False);
            Assert.That(prompts.Current, Is.Null);
            Assert.That(seq.LastScore, Is.EqualTo(6));
        }

        [Test]
        public void Seq_FeedRoutesOnlySeqEntries()
        {
            var prompts = new PromptQueue(new RecordingSink());
            var seq = new SeqCollector(prompts);
            var feed = new ManualOperatorEntryFeed();
            seq.Bind(feed, () => 0.0);
            feed.Raise(OperatorEntryKind.Fms, 5);
            Assert.That(seq.Answers, Is.EqualTo(0));
            feed.Raise(OperatorEntryKind.Seq, 5);
            Assert.That(seq.Answers, Is.EqualTo(1));
        }
    }
}
