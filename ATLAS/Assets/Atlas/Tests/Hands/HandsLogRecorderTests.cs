using System;
using System.IO;
using System.Threading;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>The recorder behind the developer parameter hands.record: a ring fed by the frame, a thread that writes.</summary>
    public unsafe class HandsLogRecorderTests
    {
        /// <summary>Storage that has stopped answering: every write after Armed waits until Release is set.</summary>
        sealed class StalledStream : MemoryStream
        {
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
            public volatile bool Armed;

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Armed) Release.Wait();
                base.Write(buffer, offset, count);
            }
        }

        static byte[] Raw(in HandsDetection detection)
        {
            var raw = new byte[HandsDetection.NativeSize];
            fixed (byte* p = raw)
            fixed (HandsDetection* s = &detection)
                Buffer.MemoryCopy(s, p, raw.Length, raw.Length);
            return raw;
        }

        static string TempFolder()
        {
            return Path.Combine(Path.GetTempPath(), "atlas_handlog_" + Guid.NewGuid().ToString("N"));
        }

        [Test, Timeout(60000)]
        public void Hands_Recorder_NeverBlocksOrAllocates()
        {
            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 20, ref d, out float3 head, out quaternion rotation);

            // The first call of a method allocates for the runtime itself: let a recorder of no importance take it.
            using (var warm = new HandsLogRecorder())
            {
                Assert.That(warm.Start(new MemoryStream()), Is.True);
                for (int i = 0; i < 20; i++) warm.Offer(i / 30.0, head, rotation, in d);
                warm.Stop();
            }

            var storage = new StalledStream();
            var recorder = new HandsLogRecorder();
            Assert.That(recorder.Start(storage), Is.True);
            storage.Armed = true;       // the header is out; from here on storage never answers

            long before = GC.GetAllocatedBytesForCurrentThread();
            int accepted = 0;
            for (int i = 0; i < 1000; i++)
                if (recorder.Offer(100.0 + i / 30.0, head, rotation, in d)) accepted++;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 1,000 offers came back at once with the writer stuck: the frame never waited for storage. What
            // the ring could hold was kept, the rest was dropped and counted.
            Assert.That(allocated, Is.EqualTo(0), "bytes the frame allocated for 1,000 offers");
            Assert.That(accepted, Is.EqualTo(HandsLogRecorder.RingSize));
            Assert.That(recorder.Dropped, Is.EqualTo(1000 - HandsLogRecorder.RingSize));
            Assert.That(recorder.IsRecording, Is.True);

            // Storage answers again: what was held is written, in order, from time 0.
            storage.Release.Set();
            recorder.Stop();
            var log = new HandsLogReader(storage.ToArray());
            Assert.That(log.Count, Is.EqualTo(HandsLogRecorder.RingSize));
            Assert.That(log.Time(0), Is.EqualTo(0.0));
            Assert.That(log.Time(HandsLogRecorder.RingSize - 1), Is.EqualTo((HandsLogRecorder.RingSize - 1) / 30.0).Within(1e-9));
            Assert.That(recorder.Written, Is.EqualTo(HandsLogRecorder.RingSize));
            Assert.That(recorder.Error, Is.Null);
        }

        [Test]
        public void Hands_Recorder_WritesWhatItIsGiven()
        {
            var source = SampleHands.Log(SampleHands.PressName);
            string folder = TempFolder();
            string path = Path.Combine(folder, "hands_test.handlog");
            try
            {
                using (var recorder = new HandsLogRecorder())
                {
                    HandsDetection d = default(HandsDetection);
                    source.Read(0, ref d, out float3 head0, out quaternion rotation0);
                    Assert.That(recorder.IsRecording, Is.False);
                    Assert.That(recorder.Offer(1.0, head0, rotation0, in d), Is.False, "not recording: ignored");
                    Assert.That(recorder.Start(path), Is.True);
                    Assert.That(recorder.IsRecording, Is.True);
                    Assert.That(recorder.Path, Is.EqualTo(path));

                    // The app clock does not start at zero: the file's time is measured from the first record.
                    for (int i = 0; i < 100; i++)
                    {
                        source.Read(i, ref d, out float3 head, out quaternion rotation);
                        Assert.That(recorder.Offer(100.0 + source.Time(i), head, rotation, in d), Is.True);
                    }
                    recorder.Stop();
                    Assert.That(recorder.IsRecording, Is.False);
                    Assert.That(recorder.Dropped, Is.EqualTo(0));
                    Assert.That(recorder.Written, Is.EqualTo(100));
                    Assert.That(recorder.Error, Is.Null);
                }

                HandsLogReader back = HandsLogReader.FromFile(path);
                Assert.That(back.Count, Is.EqualTo(100));
                Assert.That(back.TrailingBytes, Is.EqualTo(0));
                HandsDetection want = default(HandsDetection), got = default(HandsDetection);
                for (int i = 0; i < back.Count; i++)
                {
                    source.Read(i, ref want, out float3 wantHead, out quaternion wantRotation);
                    back.Read(i, ref got, out float3 head, out quaternion rotation);
                    Assert.That(back.Time(i), Is.EqualTo(source.Time(i)).Within(1e-9), "time of record " + i);
                    Assert.That(head, Is.EqualTo(wantHead));
                    Assert.That(rotation.value, Is.EqualTo(wantRotation.value));
                    Assert.That(Raw(in got), Is.EqualTo(Raw(in want)), "detection of record " + i);
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void Hands_Recorder_TakesTheHeadAsAPoseToo()
        {
            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 5, ref d, out float3 head, out quaternion rotation);
            var pose = new HeadPose(7.0, head, rotation, TrackingState.Normal);
            var one = new MemoryStream();
            var two = new MemoryStream();
            using (var recorder = new HandsLogRecorder())
            {
                Assert.That(recorder.Start(one), Is.True);
                recorder.Offer(7.0, head, rotation, in d);
                recorder.Stop();
                Assert.That(recorder.Start(two), Is.True);
                recorder.Offer(7.0, in pose, in d);
                recorder.Stop();
            }
            Assert.That(two.ToArray(), Is.EqualTo(one.ToArray()));
        }

        [Test]
        public void Hands_Recorder_HoldsTheTimeWhenTheClockIsSetBack()
        {
            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 0, ref d, out float3 head, out quaternion rotation);
            var stream = new MemoryStream();
            using (var recorder = new HandsLogRecorder())
            {
                Assert.That(recorder.Start(stream), Is.True);
                foreach (double t in new[] { 10.0, 10.5, 9.0, 11.0 }) recorder.Offer(t, head, rotation, in d);
                recorder.Stop();
            }
            // The reader refuses a time that runs backwards, so a clock that was set back is held where it was.
            var log = new HandsLogReader(stream.ToArray());
            Assert.That(log.Count, Is.EqualTo(4));
            Assert.That(log.Time(0), Is.EqualTo(0.0));
            Assert.That(log.Time(1), Is.EqualTo(0.5));
            Assert.That(log.Time(2), Is.EqualTo(0.5));
            Assert.That(log.Time(3), Is.EqualTo(1.0));
        }

        [Test]
        public void Hands_Recorder_DropsATimeThatIsNotANumber_AndStartsItsClockAtTheFirstGoodOne()
        {
            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 0, ref d, out float3 head, out quaternion rotation);
            var stream = new MemoryStream();
            using (var recorder = new HandsLogRecorder())
            {
                Assert.That(recorder.Start(stream), Is.True);
                Assert.That(recorder.Offer(double.NaN, head, rotation, in d), Is.False);
                Assert.That(recorder.Offer(double.PositiveInfinity, head, rotation, in d), Is.False);
                Assert.That(recorder.Offer(42.0, head, rotation, in d), Is.True);
                Assert.That(recorder.Offer(double.NaN, head, rotation, in d), Is.True, "once the clock has started, a bad time is held where it was");
                Assert.That(recorder.Offer(42.5, head, rotation, in d), Is.True);
                recorder.Stop();
            }
            var log = new HandsLogReader(stream.ToArray());
            Assert.That(log.Count, Is.EqualTo(3));
            Assert.That(log.Time(0), Is.EqualTo(0.0));
            Assert.That(log.Time(1), Is.EqualTo(0.0));
            Assert.That(log.Time(2), Is.EqualTo(0.5));
        }

        [Test]
        public void Hands_Recorder_CanStartAgainAfterStopping()
        {
            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 0, ref d, out float3 head, out quaternion rotation);
            var first = new MemoryStream();
            var second = new MemoryStream();
            using (var recorder = new HandsLogRecorder())
            {
                recorder.Start(first);
                for (int i = 0; i < 5; i++) recorder.Offer(50.0 + i, head, rotation, in d);
                recorder.Stop();
                Assert.That(recorder.Written, Is.EqualTo(5));

                // Starting while recording stops the first log: nothing is lost and nothing is mixed.
                recorder.Start(second);
                recorder.Offer(900.0, head, rotation, in d);
                recorder.Start(new MemoryStream());
                Assert.That(recorder.Written, Is.EqualTo(0), "the count is the new log's");
                recorder.Stop();
            }
            Assert.That(new HandsLogReader(first.ToArray()).Count, Is.EqualTo(5));
            var back = new HandsLogReader(second.ToArray());
            Assert.That(back.Count, Is.EqualTo(1));
            Assert.That(back.Time(0), Is.EqualTo(0.0), "a new log starts at 0");
        }

        [Test]
        public void Hands_Recorder_ReportsAPathItCannotOpen()
        {
            using (var recorder = new HandsLogRecorder())
            {
                // A file cannot be created below a path that is itself a file.
                string file = Path.GetTempFileName();
                try
                {
                    Assert.That(recorder.Start(Path.Combine(file, "hands.handlog")), Is.False);
                    Assert.That(recorder.IsRecording, Is.False);
                    Assert.That(recorder.Error, Is.Not.Null);
                    HandsDetection d = default(HandsDetection);
                    Assert.That(recorder.Offer(0.0, float3.zero, quaternion.identity, in d), Is.False);
                }
                finally
                {
                    File.Delete(file);
                }
            }
        }

        [Test]
        public void Hands_Recorder_EnabledStartsAFileInItsDirectory()
        {
            string folder = TempFolder();
            try
            {
                HandsDetection d = default(HandsDetection);
                SampleHands.Detection(SampleHands.PinchName, 30, ref d, out float3 head, out quaternion rotation);
                using (var recorder = new HandsLogRecorder { Directory = folder })
                {
                    Assert.That(recorder.Enabled, Is.False);
                    recorder.Enabled = true;
                    Assert.That(recorder.IsRecording, Is.True);
                    Assert.That(Path.GetDirectoryName(recorder.Path), Is.EqualTo(folder));
                    Assert.That(Path.GetFileName(recorder.Path), Does.StartWith("hands_").And.EndsWith(".handlog"));
                    recorder.Offer(3.0, head, rotation, in d);
                    recorder.Offer(3.1, head, rotation, in d);
                    recorder.Enabled = false;
                    Assert.That(recorder.IsRecording, Is.False);
                    Assert.That(HandsLogReader.FromFile(recorder.Path).Count, Is.EqualTo(2));
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
