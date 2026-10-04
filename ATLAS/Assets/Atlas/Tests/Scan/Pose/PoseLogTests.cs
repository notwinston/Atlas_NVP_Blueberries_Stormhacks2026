using System;
using System.IO;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Pose
{
    /// <summary>The pose-log text format with the sample walks, their timing, the recorder and the files of /recordings.</summary>
    public class PoseLogTests
    {
        static RoomModel Room()
        {
            return RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
        }

        static void AssertSame(PoseLog expected, PoseLog actual, float tolerance)
        {
            Assert.That(actual.RoomId, Is.EqualTo(expected.RoomId));
            Assert.That(actual.Frame, Is.EqualTo(expected.Frame));
            Assert.That(actual.CreatedUtc, Is.EqualTo(expected.CreatedUtc));
            Assert.That(actual.Device, Is.EqualTo(expected.Device));
            Assert.That(actual.Samples.Length, Is.EqualTo(expected.Samples.Length));
            for (int i = 0; i < expected.Samples.Length; i++)
            {
                HeadPose e = expected.Samples[i], a = actual.Samples[i];
                Assert.That(a.Time, Is.EqualTo(e.Time).Within(1e-6), "t of sample " + i);
                Assert.That(math.cmax(math.abs(a.Position - e.Position)), Is.LessThanOrEqualTo(tolerance), "position of sample " + i);
                Assert.That(math.cmax(math.abs(a.Rotation.value - e.Rotation.value)), Is.LessThanOrEqualTo(tolerance), "rotation of sample " + i);
                Assert.That(a.Tracking, Is.EqualTo(e.Tracking), "tracking of sample " + i);
            }
        }

        [TestCaseSource(typeof(SampleWalks), nameof(SampleWalks.Names))]
        public void RoundTrip_KeepsEverySample(string name)
        {
            PoseLog log = SampleWalks.ByName(name, Room());
            string text = SampleWalks.ToText(log);
            PoseLog back = PoseLog.Read(new StringReader(text));
            AssertSame(log, back, 1e-6f);
            // Written again, the text is the same to the last character.
            Assert.That(SampleWalks.ToText(back), Is.EqualTo(text));
            Assert.That(text.IndexOf('\r'), Is.EqualTo(-1));
            Assert.That(text, Does.StartWith("ATLAS-POSELOG 1\nroom sample_3x3\nframe room\n"));
        }

        [TestCaseSource(typeof(SampleWalks), nameof(SampleWalks.Names))]
        public void Timing_IsSixtySamplesASecond_FromZero(string name)
        {
            PoseLog log = PoseLog.Read(new StringReader(SampleWalks.ToText(SampleWalks.ByName(name, Room()))));
            Assert.That(log.Samples[0].Time, Is.EqualTo(0.0));
            for (int i = 1; i < log.Samples.Length; i++)
            {
                Assert.That(log.Samples[i].Time, Is.GreaterThan(log.Samples[i - 1].Time));
                Assert.That(log.Samples[i].Time, Is.EqualTo(i / 60.0).Within(1e-6));
            }
            Assert.That(log.Duration, Is.EqualTo((log.Samples.Length - 1) / 60.0).Within(1e-6));
        }

        [Test]
        public void Lap_WalksTheLapSquareAtHalfAMetrePerSecond()
        {
            RoomModel room = Room();
            PoseLog log = SampleWalks.Lap(room.LapSide);
            float h = 0.5f * room.LapSide;
            Assert.That(log.Duration, Is.EqualTo(4.0 * room.LapSide / 0.5).Within(1e-6));
            for (int i = 0; i < log.Samples.Length; i++)
            {
                HeadPose s = log.Samples[i];
                Assert.That(s.Height, Is.EqualTo(1.6f));
                // On the square: the larger of |x| and |z| is the half side.
                Assert.That(math.cmax(math.abs(s.Floor)), Is.EqualTo(h).Within(1e-5f));
                Assert.That(room.FreeRect.Contains(s.Floor), Is.True);
                if (i > 0)
                    Assert.That(math.distance(s.Floor, log.Samples[i - 1].Floor) * 60f, Is.EqualTo(0.5f).Within(0.01f));
            }
            Assert.That(math.distance(log.Samples[0].Floor, log.Samples[log.Samples.Length - 1].Floor), Is.LessThan(1e-5f));
        }

        [Test]
        public void WallApproach_GoesStraightAtOneMetrePerSecond_AndStopsShortOfTheWall()
        {
            RoomModel room = Room();
            float wall = SampleWalks.HalfDepth(room);
            PoseLog log = SampleWalks.WallApproach(wall);
            HeadPose last = log.Samples[log.Samples.Length - 1];
            Assert.That(wall, Is.EqualTo(1.5f));
            Assert.That(last.Position.z, Is.EqualTo(wall - 0.3f).Within(1e-5f));
            Assert.That(log.Samples[60].Position.z - log.Samples[0].Position.z, Is.EqualTo(1.0f).Within(1e-4f));
            for (int i = 0; i < log.Samples.Length; i++)
            {
                Assert.That(log.Samples[i].Position.x, Is.EqualTo(0f));
                Assert.That(log.Samples[i].Heading.y, Is.EqualTo(1f).Within(1e-6f));      // looking at the wall
            }
        }

        [Test]
        public void JumpAndLoss_HasOneJumpOfHalfAMetre_AndOneSecondOfLimitedTracking()
        {
            PoseLog log = SampleWalks.JumpAndLoss();
            int jumps = 0, limited = 0;
            for (int i = 0; i < log.Samples.Length; i++)
            {
                if (log.Samples[i].Tracking != TrackingState.Normal) limited++;
                if (i == 0) continue;
                float d = math.distance(log.Samples[i].Floor, log.Samples[i - 1].Floor);
                if (d > PoseFilter.JumpDistance)
                {
                    jumps++;
                    Assert.That(i, Is.EqualTo(SampleWalks.JumpFrame));
                    Assert.That(d, Is.EqualTo(0.5f).Within(0.01f));
                }
            }
            Assert.That(jumps, Is.EqualTo(1));
            Assert.That(limited, Is.EqualTo(60));
        }

        [TestCaseSource(typeof(SampleWalks), nameof(SampleWalks.Names))]
        public void RecordingsFile_IsTheGeneratorsOutput(string name)
        {
            PoseLog log = SampleWalks.ByName(name, Room());
            string path = Path.Combine(SampleWalks.RecordingsDirectory(), name + ".poselog");
            // To regenerate the files after a change to the generator, run the tests once with this set.
            if (Environment.GetEnvironmentVariable("ATLAS_WRITE_RECORDINGS") == "1")
            {
                Directory.CreateDirectory(SampleWalks.RecordingsDirectory());
                File.WriteAllText(path, SampleWalks.ToText(log));
            }
            Assert.That(File.Exists(path), Is.True, path);
            PoseLog onDisk;
            using (StreamReader reader = File.OpenText(path)) onDisk = PoseLog.Read(reader);
            AssertSame(log, onDisk, 2e-6f);
        }

        [Test]
        public void Recorder_WritesWhatItIsGiven_OffTheCallingThread()
        {
            PoseLog log = SampleWalks.JumpAndLoss();
            string folder = Path.Combine(Path.GetTempPath(), "atlas_poselog_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "walk_test.poselog");
            try
            {
                using (var recorder = new PoseLogRecorder())
                {
                    Assert.That(recorder.IsRecording, Is.False);
                    recorder.Record(in log.Samples[0]);         // ignored: not recording
                    Assert.That(recorder.Start(path, log.RoomId, log.Frame, log.CreatedUtc, log.Device), Is.True);
                    Assert.That(recorder.IsRecording, Is.True);
                    // The app clock does not start at zero: the file's t is measured from the first sample.
                    for (int i = 0; i < log.Samples.Length; i++)
                    {
                        HeadPose s = log.Samples[i];
                        var shifted = new HeadPose(100.0 + s.Time, s.Position, s.Rotation, s.Tracking);
                        recorder.Record(in shifted);
                    }
                    recorder.Stop();
                    Assert.That(recorder.IsRecording, Is.False);
                    Assert.That(recorder.Dropped, Is.EqualTo(0));
                    Assert.That(recorder.Error, Is.Null);
                }
                PoseLog back;
                using (StreamReader reader = File.OpenText(path)) back = PoseLog.Read(reader);
                AssertSame(log, back, 2e-6f);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void Recorder_ReportsAPathItCannotOpen()
        {
            using (var recorder = new PoseLogRecorder())
            {
                // A file cannot be created below a path that is itself a file.
                string file = Path.GetTempFileName();
                try
                {
                    Assert.That(recorder.Start(Path.Combine(file, "walk.poselog"), "", PoseFrameKind.Room, ""), Is.False);
                    Assert.That(recorder.IsRecording, Is.False);
                    Assert.That(recorder.Error, Is.Not.Null);
                }
                finally
                {
                    File.Delete(file);
                }
            }
        }
    }
}
