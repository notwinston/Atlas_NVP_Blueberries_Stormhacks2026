using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>The replay source: a hand log played against the clock it is given (documents/ATLAS_Hands_Design.md section 4.5).</summary>
    public unsafe class HandsSourcesTests
    {
        static byte[] Raw(in HandsDetection detection)
        {
            var raw = new byte[HandsDetection.NativeSize];
            fixed (byte* p = raw)
            fixed (HandsDetection* s = &detection)
                Buffer.MemoryCopy(s, p, raw.Length, raw.Length);
            return raw;
        }

        [Test]
        public void Hands_Replay_Timing()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.WaveName);
            var source = new ReplayHandsSource(log);
            Assert.That(source.Name, Is.EqualTo("replay"));
            Assert.That(source.Available, Is.True);
            Assert.That(source.Fault, Is.EqualTo(0));
            Assert.That(source.Loop, Is.False);
            Assert.That(source.Ended, Is.False);
            Assert.That(source.Index, Is.EqualTo(-1));

            // The app clock does not start at zero, and the frame is faster than the log: 120 Hz against 30 Hz.
            const double start = 12.5, step = 1.0 / 120.0;
            HandsDetection d = default(HandsDetection), wanted = default(HandsDetection);
            int delivered = 0;
            for (int n = 0; n < 700 && !source.Ended; n++)
            {
                double time = start + n * step;
                if (!source.TryRead(time, ref d)) continue;

                // Each record comes once and in order...
                Assert.That(d.Sequence, Is.EqualTo(delivered + 1), "order at step " + n);
                Assert.That(source.Index, Is.EqualTo(delivered));
                // ...at the first frame that has reached its time, counted from the first read...
                double local = time - start;
                Assert.That(local, Is.GreaterThanOrEqualTo(log.Time(delivered) - 1e-6), "not before its time, record " + delivered);
                Assert.That(local, Is.LessThan(log.Time(delivered) + step + 1e-6), "and not after the next frame, record " + delivered);
                // ...and it is the record of the log, byte for byte.
                log.Read(delivered, ref wanted, out float3 head, out quaternion rotation);
                Assert.That(Raw(in d), Is.EqualTo(Raw(in wanted)), "record " + delivered);
                Assert.That(source.HeadPosition, Is.EqualTo(head));
                Assert.That(source.HeadRotation.value, Is.EqualTo(rotation.value));
                delivered++;
            }
            Assert.That(delivered, Is.EqualTo(log.Count), "every record came");
            Assert.That(source.Ended, Is.True, "and then there is no more");
            Assert.That(source.TryRead(start + 100.0, ref d), Is.False);
            Assert.That(source.Ended, Is.True);
        }

        [Test]
        public void Hands_Replay_FirstReadStartsTheClock_WhateverTheClockSays()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.PressName);
            HandsDetection d = default(HandsDetection);
            foreach (double start in new[] { 0.0, 3.7, 86400.25 })
            {
                var source = new ReplayHandsSource(log);
                Assert.That(source.TryRead(start, ref d), Is.True, "the first read gets record 0");
                Assert.That(d.Sequence, Is.EqualTo(1));
                Assert.That(source.TryRead(start, ref d), Is.False, "once");
                Assert.That(source.TryRead(start + 0.02, ref d), Is.False, "the next record is due at 1/30 s");
                Assert.That(source.TryRead(start + 1.0 / 30.0, ref d), Is.True);
                Assert.That(d.Sequence, Is.EqualTo(2));
            }
        }

        [Test]
        public void Hands_Replay_CountsFromTheFirstRecordOfTheLog_NotFromZero()
        {
            // A log written by something other than the recorder may not start at 0: the replay is relative to its first record.
            var stream = new System.IO.MemoryStream();
            HandsDetection d = default(HandsDetection);
            using (var writer = new HandsLogWriter(stream, true))
                for (int k = 0; k < 4; k++)
                {
                    SampleHands.Detection(SampleHands.PinchName, k, ref d, out float3 head, out quaternion rotation);
                    writer.Write(3.0 + 0.1 * k, head, rotation, in d);
                }
            var source = new ReplayHandsSource(new HandsLogReader(stream.ToArray()));
            Assert.That(source.TryRead(50.0, ref d), Is.True, "the first read gets the first record, at once");
            Assert.That(d.Sequence, Is.EqualTo(1));
            Assert.That(source.TryRead(50.05, ref d), Is.False);
            Assert.That(source.TryRead(50.1, ref d), Is.True);
            Assert.That(d.Sequence, Is.EqualTo(2));
            Assert.That(source.TryRead(50.3, ref d), Is.True);
            Assert.That(d.Sequence, Is.EqualTo(4));
            Assert.That(source.Ended, Is.True);
        }

        [Test]
        public void Hands_Replay_AClockThatGoesBackDoesNotGoBackInTheLog()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.WaveName);
            var source = new ReplayHandsSource(log);
            HandsDetection d = default(HandsDetection);
            Assert.That(source.TryRead(10.0, ref d), Is.True);
            Assert.That(source.TryRead(10.5, ref d), Is.True);
            int sequence = d.Sequence;
            Assert.That(sequence, Is.EqualTo(16), "15 records in half a second, and the first");
            Assert.That(source.TryRead(10.2, ref d), Is.False, "nothing older than what was handed out comes again");
            Assert.That(source.TryRead(10.5 + 1.0 / 30.0, ref d), Is.True);
            Assert.That(d.Sequence, Is.EqualTo(sequence + 1));
        }

        [Test]
        public void Hands_Replay_ASlowReaderGetsTheNewestRecord_AndNeverAnOlderOne()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.WaveName);
            var source = new ReplayHandsSource(log);
            HandsDetection d = default(HandsDetection);
            int last = 0;
            // 10 frames a second against a log of 30 results a second: two of every three records are skipped, as
            // the phone's detector keeps only its newest result.
            for (int n = 0; n <= 50; n++)
            {
                Assert.That(source.TryRead(3.0 + n * 0.1, ref d), Is.True);
                int expected = (int)Math.Floor(n * 0.1 * 30.0 + 1e-6) + 1;
                Assert.That(d.Sequence, Is.EqualTo(expected), "step " + n);
                Assert.That(d.Sequence, Is.GreaterThan(last));
                last = d.Sequence;
            }
            Assert.That(last, Is.EqualTo(log.Count), "the last record came");
            Assert.That(source.Ended, Is.True);
        }

        [Test]
        public void Hands_Replay_LoopKeepsTheCadenceAndTheClockRising()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.WaveName);
            var source = new ReplayHandsSource(log, true);
            Assert.That(source.Loop, Is.True);
            HandsDetection d = default(HandsDetection), first = default(HandsDetection);
            log.Read(0, ref first, out float3 _, out quaternion _);

            // Two and a half laps at 60 Hz: 151 records a lap, and the lap is 151 / 30 s so the cadence has no hiccup at the seam.
            const double start = 40.0, step = 1.0 / 60.0;
            int delivered = 0;
            double previousPoll = double.NegativeInfinity;
            for (int n = 0; n <= 760; n++)
            {
                double time = start + n * step;
                if (!source.TryRead(time, ref d)) continue;
                Assert.That(d.Sequence, Is.EqualTo(delivered + 1), "the sequence keeps rising across laps, step " + n);
                if (delivered > 0)
                    Assert.That(d.PollTime - previousPoll, Is.EqualTo(1.0 / 30.0).Within(1e-6), "the detector clock keeps its cadence, record " + delivered);
                Assert.That(d.PollTime - d.CaptureTime, Is.EqualTo(0.080).Within(1e-9), "the age is kept");
                previousPoll = d.PollTime;
                int inLap = delivered % log.Count;
                HandsDetection wanted = default(HandsDetection);
                log.Read(inLap, ref wanted, out float3 head, out quaternion _);
                Assert.That(source.HeadPosition, Is.EqualTo(head));
                Assert.That(source.Index, Is.EqualTo(inLap));
                delivered++;
            }
            Assert.That(source.Ended, Is.False, "a loop does not end");
            Assert.That(source.LoopCount, Is.EqualTo(2));
            // 760 steps of 1/60 s is 12.67 s: records at 0, 1/30 s ... that have been reached.
            Assert.That(delivered, Is.EqualTo((int)Math.Floor(760 * step * 30.0 + 1e-6) + 1));

            // The first lap is the log untouched; from the second, the clock and the sequence are carried on.
            var again = new ReplayHandsSource(log, true);
            Assert.That(again.TryRead(0.0, ref d), Is.True);
            Assert.That(Raw(in d), Is.EqualTo(Raw(in first)));
        }

        [Test]
        public void Hands_Replay_ALogOfOneRecordLoops()
        {
            var stream = new System.IO.MemoryStream();
            HandsDetection one = default(HandsDetection);
            SampleHands.Detection(SampleHands.PinchName, 10, ref one, out float3 head, out quaternion rotation);
            using (var writer = new HandsLogWriter(stream, true)) writer.Write(0.0, head, rotation, in one);
            var source = new ReplayHandsSource(new HandsLogReader(stream.ToArray()), true);
            HandsDetection d = default(HandsDetection);
            int delivered = 0;
            for (int n = 0; n < 90; n++)
                if (source.TryRead(n / 30.0, ref d))
                {
                    delivered++;
                    Assert.That(d.Sequence, Is.EqualTo(one.Sequence + delivered - 1), "one record, handed out once a lap, with the sequence rising");
                }
            Assert.That(delivered, Is.EqualTo(90), "one every 1/30 s");
        }

        [Test]
        public void Hands_Replay_AnEmptyLogEndsAtOnce()
        {
            var stream = new System.IO.MemoryStream();
            new HandsLogWriter(stream, true).Dispose();
            var source = new ReplayHandsSource(new HandsLogReader(stream.ToArray()));
            HandsDetection d = default(HandsDetection);
            Assert.That(source.TryRead(0.0, ref d), Is.False);
            Assert.That(source.Ended, Is.True);
            Assert.That(new ReplayHandsSource(new HandsLogReader(stream.ToArray()), true).TryRead(0.0, ref d), Is.False);
        }

        [Test]
        public void Hands_Replay_RateZeroIdlesIt_AndStopRewindsIt()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.PressName);
            var source = new ReplayHandsSource(log);
            HandsDetection d = default(HandsDetection);
            Assert.That(source.TryRead(0.0, ref d), Is.True);
            Assert.That(d.Sequence, Is.EqualTo(1));

            // Idle: nothing comes, whatever the clock says, and the clock goes on.
            source.SetRate(0);
            Assert.That(source.TryRead(0.5, ref d), Is.False);
            source.SetRate(HandsConstants.RateReduced);
            Assert.That(source.TryRead(1.0, ref d), Is.True, "running again");
            Assert.That(d.Sequence, Is.EqualTo(31), "with the newest record that is due");

            // A stopped source hands out nothing; asked for a rate it starts again, from the first record.
            source.Stop();
            Assert.That(source.TryRead(1.1, ref d), Is.False);
            Assert.That(source.Index, Is.EqualTo(-1));
            source.SetRate(HandsConstants.RateFull);
            Assert.That(source.TryRead(50.0, ref d), Is.True);
            Assert.That(d.Sequence, Is.EqualTo(1));
        }

        [Test]
        public void Hands_Replay_RestartPlaysTheLogAgain()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.PinchName);
            var source = new ReplayHandsSource(log);
            HandsDetection d = default(HandsDetection);
            for (int n = 0; n < 400; n++) source.TryRead(n / 60.0, ref d);
            Assert.That(source.Ended, Is.True);
            source.Restart();
            Assert.That(source.Ended, Is.False);
            Assert.That(source.TryRead(99.0, ref d), Is.True);
            Assert.That(d.Sequence, Is.EqualTo(1));
        }

        [Test]
        public void Hands_Replay_AllocatesNothing()
        {
            HandsLogReader log = SampleHands.Log(SampleHands.WaveName);
            var warm = new ReplayHandsSource(log, true);
            HandsDetection d = default(HandsDetection);
            for (int n = 0; n < 50; n++) warm.TryRead(n / 60.0, ref d);

            var source = new ReplayHandsSource(log, true);
            int delivered = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 600; n++)
                if (source.TryRead(n / 60.0, ref d)) delivered++;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(delivered, Is.GreaterThan(250));
            Assert.That(allocated, Is.EqualTo(0), "600 frames of a replay, 300 results");
        }

        [Test]
        public void Hands_Replay_RefusesNoLog()
        {
            Assert.Throws<ArgumentNullException>(() => new ReplayHandsSource(null));
        }

        [Test]
        public void Hands_Replay_PlaysTheCommittedFiles()
        {
            // What the lift, the filter and the touch tests will do: read a file of /recordings and play it.
            foreach (string name in SampleHands.Names)
            {
                ReplayHandsSource source = ReplayHandsSource.FromFile(SampleHands.PathOf(name));
                HandsDetection d = default(HandsDetection);
                int delivered = 0;
                for (int n = 0; n < 5000 && !source.Ended; n++)
                    if (source.TryRead(n / 60.0, ref d)) delivered++;
                Assert.That(delivered, Is.EqualTo(SampleHands.Records(name)), name);
            }
        }
    }
}
