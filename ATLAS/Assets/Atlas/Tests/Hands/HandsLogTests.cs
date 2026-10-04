using System;
using System.IO;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>The hand-log format, the sample logs and the files of /recordings (documents/ATLAS_Hands_Design.md section 4.5).</summary>
    public unsafe class HandsLogTests
    {
        static byte[] Raw(in HandsDetection detection)
        {
            var raw = new byte[HandsDetection.NativeSize];
            fixed (byte* p = raw)
            fixed (HandsDetection* s = &detection)
                Buffer.MemoryCopy(s, p, raw.Length, raw.Length);
            return raw;
        }

        static byte[] Record(double time, float3 head, quaternion rotation, in HandsDetection detection)
        {
            var record = new byte[HandsLog.RecordLength];
            HandsLog.Encode(record, 0, time, head, rotation, in detection);
            return record;
        }

        static int OffsetOfVersion()
        {
            HandsDetection d = default(HandsDetection);
            return (int)((byte*)&d.Version - (byte*)&d);
        }

        static int OffsetOfHandCount()
        {
            HandsDetection d = default(HandsDetection);
            return (int)((byte*)&d.HandCount - (byte*)&d);
        }

        /// <summary>A small valid log: the first three records of the wave.</summary>
        static byte[] SmallLog()
        {
            var stream = new MemoryStream();
            using (var writer = new HandsLogWriter(stream, true))
            {
                HandsDetection d = default(HandsDetection);
                for (int k = 0; k < 3; k++)
                {
                    SampleHands.Detection(SampleHands.WaveName, k, ref d, out float3 p, out quaternion q);
                    writer.Write(k / SampleHands.Rate, p, q, in d);
                }
            }
            return stream.ToArray();
        }

        static int RecordStart(int index)
        {
            return HandsLog.HeaderLength + index * HandsLog.RecordLength;
        }

        [Test]
        public void Hands_Log_Format_IsTheDocumentedOne()
        {
            Assert.That(HandsLog.Magic, Is.EqualTo("ATLAS-HANDLOG 1"));
            Assert.That(HandsLog.HeaderLength, Is.EqualTo(HandsLog.Magic.Length + 1));
            Assert.That(HandsLog.HeadLength, Is.EqualTo(8 + 12 + 16));
            Assert.That(HandsLog.RecordLength, Is.EqualTo(HandsLog.HeadLength + 2712));
            Assert.That(sizeof(HandsDetection), Is.EqualTo(HandsDetection.NativeSize), "the record holds the struct as the plugin lays it out");

            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 12, ref d, out float3 _, out quaternion _);
            var head = new float3(1.25f, 1.5f, -0.75f);
            quaternion turn = quaternion.RotateY(0.5f);
            var stream = new MemoryStream();
            using (var writer = new HandsLogWriter(stream, true)) writer.Write(0.4, head, turn, in d);
            byte[] file = stream.ToArray();

            Assert.That(file.Length, Is.EqualTo(HandsLog.HeaderLength + HandsLog.RecordLength));
            Assert.That(System.Text.Encoding.ASCII.GetString(file, 0, HandsLog.HeaderLength), Is.EqualTo("ATLAS-HANDLOG 1\n"));
            int at = HandsLog.HeaderLength;
            Assert.That(BitConverter.ToDouble(file, at), Is.EqualTo(0.4));
            Assert.That(BitConverter.ToSingle(file, at + 8), Is.EqualTo(head.x));
            Assert.That(BitConverter.ToSingle(file, at + 12), Is.EqualTo(head.y));
            Assert.That(BitConverter.ToSingle(file, at + 16), Is.EqualTo(head.z));
            Assert.That(BitConverter.ToSingle(file, at + 20), Is.EqualTo(turn.value.x));
            Assert.That(BitConverter.ToSingle(file, at + 24), Is.EqualTo(turn.value.y));
            Assert.That(BitConverter.ToSingle(file, at + 28), Is.EqualTo(turn.value.z));
            Assert.That(BitConverter.ToSingle(file, at + 32), Is.EqualTo(turn.value.w));

            byte[] inFile = new byte[HandsDetection.NativeSize];
            Array.Copy(file, at + HandsLog.HeadLength, inFile, 0, inFile.Length);
            Assert.That(inFile, Is.EqualTo(Raw(in d)), "the detection is stored byte for byte");
        }

        [TestCaseSource(typeof(SampleHands), nameof(SampleHands.Names))]
        public void Hands_Log_RoundTrip(string name)
        {
            byte[] file = SampleHands.Bytes(name);
            var reader = new HandsLogReader(file);
            Assert.That(reader.Count, Is.EqualTo(SampleHands.Records(name)));
            Assert.That(reader.TrailingBytes, Is.EqualTo(0));
            Assert.That(file.Length, Is.EqualTo(HandsLog.HeaderLength + reader.Count * HandsLog.RecordLength));

            // Written again, the file is the same to the last byte.
            var again = new MemoryStream();
            using (var writer = new HandsLogWriter(again, true))
            {
                HandsDetection d = default(HandsDetection);
                for (int i = 0; i < reader.Count; i++)
                {
                    reader.Read(i, ref d, out float3 head, out quaternion rotation);
                    writer.Write(reader.Time(i), head, rotation, in d);
                }
                Assert.That(writer.Count, Is.EqualTo(reader.Count));
            }
            Assert.That(again.ToArray(), Is.EqualTo(file));

            // And every record is what the generator makes for its index, head pose and time included.
            HandsDetection want = default(HandsDetection), got = default(HandsDetection);
            for (int i = 0; i < reader.Count; i++)
            {
                SampleHands.Detection(name, i, ref want, out float3 wantHead, out quaternion wantRotation);
                reader.Read(i, ref got, out float3 head, out quaternion rotation);
                Assert.That(reader.Time(i), Is.EqualTo(i / SampleHands.Rate), "time of record " + i);
                Assert.That(head, Is.EqualTo(wantHead), "head of record " + i);
                Assert.That(rotation.value, Is.EqualTo(wantRotation.value), "head rotation of record " + i);
                Assert.That(Raw(in got), Is.EqualTo(Raw(in want)), "detection of record " + i);
            }
        }

        [Test]
        public void Hands_Log_KeepsEveryByteOfARecord()
        {
            // A pattern in every byte of the struct, so that a field the codec forgot cannot hide. The values
            // are nonsense apart from the two the reader checks.
            HandsDetection d = default(HandsDetection);
            byte* p = (byte*)&d;
            for (int i = 0; i < HandsDetection.NativeSize; i++) p[i] = (byte)(i * 37 + 11);
            d.Version = HandsDetection.CurrentVersion;
            d.HandCount = 2;

            var head = new float3(-3.5f, 1.625f, 12.75f);
            var rotation = new quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            var stream = new MemoryStream();
            using (var writer = new HandsLogWriter(stream, true))
            {
                writer.Write(0.25, head, rotation, in d);
                writer.Write(1234.5678, head + 1f, rotation, in d);
            }

            var reader = new HandsLogReader(stream.ToArray());
            Assert.That(reader.Count, Is.EqualTo(2));
            HandsDetection back = default(HandsDetection);
            reader.Read(1, ref back, out float3 backHead, out quaternion backRotation);
            Assert.That(reader.Time(0), Is.EqualTo(0.25));
            Assert.That(reader.Time(1), Is.EqualTo(1234.5678));
            Assert.That(backHead, Is.EqualTo(head + 1f));
            Assert.That(backRotation.value, Is.EqualTo(rotation.value));
            Assert.That(Raw(in back), Is.EqualTo(Raw(in d)));
        }

        [TestCaseSource(typeof(SampleHands), nameof(SampleHands.Names))]
        public void Hands_Log_FileEqualsGenerator(string name)
        {
            byte[] expected = SampleHands.Bytes(name);
            string path = SampleHands.PathOf(name);
            // To regenerate the files after a change to the generator, run the tests once with this set.
            if (Environment.GetEnvironmentVariable("ATLAS_WRITE_RECORDINGS") == "1")
            {
                Directory.CreateDirectory(SampleHands.RecordingsDirectory());
                File.WriteAllBytes(path, expected);
            }
            Assert.That(File.Exists(path), Is.True, path);
            byte[] onDisk = File.ReadAllBytes(path);
            int length = Math.Min(onDisk.Length, expected.Length);
            for (int i = 0; i < length; i++)
            {
                if (onDisk[i] == expected[i]) continue;
                int record = i < HandsLog.HeaderLength ? -1 : (i - HandsLog.HeaderLength) / HandsLog.RecordLength;
                Assert.Fail(path + " differs from the generator at byte " + i + " (record " + record + "). " +
                            "Regenerate with ATLAS_WRITE_RECORDINGS=1 and commit the file.");
            }
            Assert.That(onDisk.Length, Is.EqualTo(expected.Length), path);
        }

        [Test]
        public void Hands_Log_ReaderRefusesWhatIsNotAHandLog()
        {
            Assert.Throws<ArgumentNullException>(() => new HandsLogReader(null));
            Assert.Throws<FormatException>(() => new HandsLogReader(new byte[0]));
            Assert.Throws<FormatException>(() => new HandsLogReader(new byte[HandsLog.HeaderLength]));

            byte[] good = SmallLog();
            Assert.That(new HandsLogReader(good).Count, Is.EqualTo(3));

            // A pose log is not a hand log.
            byte[] poseLog = System.Text.Encoding.ASCII.GetBytes("ATLAS-POSELOG 1\nframe room\ndata\n0.0 0 0 0 0 0 0 1 2\n");
            var wrongName = Assert.Throws<FormatException>(() => new HandsLogReader(poseLog));
            Assert.That(wrongName.Message, Does.Contain("ATLAS-HANDLOG"));

            // A newer format is named, not misread.
            byte[] newer = (byte[])good.Clone();
            newer[HandsLog.HeaderLength - 2] = (byte)'2';
            var version = Assert.Throws<FormatException>(() => new HandsLogReader(newer));
            Assert.That(version.Message, Does.Contain("version 2"));

            // No line end where the header should end.
            byte[] noLineEnd = (byte[])good.Clone();
            noLineEnd[HandsLog.HeaderLength - 1] = (byte)' ';
            Assert.Throws<FormatException>(() => new HandsLogReader(noLineEnd));
        }

        [Test]
        public void Hands_Log_ReaderRefusesDamagedRecords()
        {
            byte[] good = SmallLog();

            byte[] badVersion = (byte[])good.Clone();
            BitConverter.GetBytes(HandsDetection.CurrentVersion + 1).CopyTo(badVersion, RecordStart(1) + HandsLog.HeadLength + OffsetOfVersion());
            var version = Assert.Throws<FormatException>(() => new HandsLogReader(badVersion));
            Assert.That(version.Message, Does.Contain("record 1"));

            byte[] badCount = (byte[])good.Clone();
            BitConverter.GetBytes(3).CopyTo(badCount, RecordStart(2) + HandsLog.HeadLength + OffsetOfHandCount());
            var count = Assert.Throws<FormatException>(() => new HandsLogReader(badCount));
            Assert.That(count.Message, Does.Contain("record 2"));

            byte[] backwards = (byte[])good.Clone();
            BitConverter.GetBytes(0.001).CopyTo(backwards, RecordStart(2));
            var time = Assert.Throws<FormatException>(() => new HandsLogReader(backwards));
            Assert.That(time.Message, Does.Contain("record 2"));

            byte[] notANumber = (byte[])good.Clone();
            BitConverter.GetBytes(double.NaN).CopyTo(notANumber, RecordStart(0));
            Assert.Throws<FormatException>(() => new HandsLogReader(notANumber));
        }

        [Test]
        public void Hands_Log_ReaderKeepsTheWholeRecordsOfACutFile()
        {
            byte[] good = SmallLog();

            // A phone that died while writing leaves part of a record at the end: the whole ones are good.
            var cut = new byte[good.Length - 100];
            Array.Copy(good, cut, cut.Length);
            var reader = new HandsLogReader(cut);
            Assert.That(reader.Count, Is.EqualTo(2));
            Assert.That(reader.TrailingBytes, Is.EqualTo(HandsLog.RecordLength - 100));

            var headerOnly = new byte[HandsLog.HeaderLength];
            Array.Copy(good, headerOnly, headerOnly.Length);
            Assert.That(new HandsLogReader(headerOnly).Count, Is.EqualTo(0));
        }

        [Test]
        public void Hands_Log_ReaderChecksItsIndex()
        {
            var reader = new HandsLogReader(SmallLog());
            HandsDetection d = default(HandsDetection);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Time(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Time(3));
            Assert.That(reader.Duration, Is.EqualTo(2.0 / SampleHands.Rate));
            // Read has ref and out arguments, so it is called here and not from a lambda.
            bool threw = false;
            try { reader.Read(3, ref d, out float3 _, out quaternion _); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Assert.That(threw, Is.True);
            threw = false;
            try { reader.Read(-1, ref d, out float3 _, out quaternion _); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Assert.That(threw, Is.True);
        }

        [Test]
        public void Hands_Log_WriterRefusesATimeThatIsNotFiniteOrGoesBack()
        {
            HandsDetection d = default(HandsDetection);
            SampleHands.Detection(SampleHands.WaveName, 0, ref d, out float3 head, out quaternion rotation);
            var stream = new MemoryStream();
            using (var writer = new HandsLogWriter(stream, true))
            {
                writer.Write(1.0, head, rotation, in d);
                bool threw = false;
                try { writer.Write(0.5, head, rotation, in d); } catch (ArgumentException) { threw = true; }
                Assert.That(threw, Is.True, "a time that goes back");
                threw = false;
                try { writer.Write(double.NaN, head, rotation, in d); } catch (ArgumentException) { threw = true; }
                Assert.That(threw, Is.True, "a time that is not a number");
                writer.Write(1.0, head, rotation, in d);       // the same time again is fine
                Assert.That(writer.Count, Is.EqualTo(2));
            }
            Assert.That(new HandsLogReader(stream.ToArray()).Count, Is.EqualTo(2));
        }

        [Test]
        public void Hands_Log_WriterClosesItsStreamUnlessToldNotTo()
        {
            var owned = new MemoryStream();
            new HandsLogWriter(owned).Dispose();
            Assert.That(owned.CanWrite, Is.False);

            var kept = new MemoryStream();
            new HandsLogWriter(kept, true).Dispose();
            Assert.That(kept.CanWrite, Is.True);
            Assert.That(kept.Length, Is.EqualTo(HandsLog.HeaderLength), "the header is written at once, so an empty log is a valid file");
            Assert.That(new HandsLogReader(kept.ToArray()).Count, Is.EqualTo(0));
        }

        // ---- The sample logs are what their names say ----

        [TestCaseSource(typeof(SampleHands), nameof(SampleHands.Names))]
        public void Hands_Fixtures_AreTimedLikeThePhone(string name)
        {
            var log = SampleHands.Log(name);
            Assert.That(log.Count, Is.EqualTo((int)Math.Round(SampleHands.SecondsOf(name) * 30.0) + 1));
            HandsDetection d = default(HandsDetection);
            for (int i = 0; i < log.Count; i++)
            {
                log.Read(i, ref d, out float3 _, out quaternion _);
                Assert.That(log.Time(i), Is.EqualTo(i / 30.0));
                Assert.That(d.Version, Is.EqualTo(HandsDetection.CurrentVersion));
                Assert.That(d.Sequence, Is.EqualTo(i + 1), "the sequence rises by one a result");
                Assert.That(d.PollTime - d.CaptureTime, Is.EqualTo(0.080).Within(1e-9), "age of record " + i);
                Assert.That(d.PollTime, Is.EqualTo(SampleHands.DetectorClockOffset + i / 30.0).Within(1e-9));
                Assert.That(d.ImageWidth, Is.EqualTo(1280));
                Assert.That(d.ImageHeight, Is.EqualTo(720));
                Assert.That(d.DepthWidth, Is.EqualTo(256));
                Assert.That(d.DepthHeight, Is.EqualTo(144));
                Assert.That(d.Skipped, Is.EqualTo(0));
                Assert.That(d.RequestMs, Is.EqualTo(SampleHands.RequestMs));
            }
        }

        [TestCaseSource(typeof(SampleHands), nameof(SampleHands.Names))]
        public void Hands_Fixtures_KeepEveryJointInThePicture_AndAreTheTruth(string name)
        {
            var log = SampleHands.Log(name);
            HandsDetection d = default(HandsDetection);
            for (int i = 0; i < log.Count; i++)
            {
                log.Read(i, ref d, out float3 _, out quaternion _);
                for (int h = 0; h < d.HandCount; h++)
                {
                    DetectedHand hand = h == 0 ? d.Hand0 : d.Hand1;
                    Assert.That(SampleHands.Truth(name, i, hand.Side == 0 ? HandSide.Left : HandSide.Right, out JointSet truth), Is.True);
                    for (int j = 0; j < HandJoints.Count; j++)
                    {
                        JointSample s = hand.GetJoint(j);
                        string where = name + " record " + i + " hand " + h + " joint " + j;
                        Assert.That(s.Pixel.x, Is.InRange(20f, 1280f - 20f), where);
                        Assert.That(s.Pixel.y, Is.InRange(20f, 720f - 20f), where);
                        Assert.That(s.Confidence, Is.GreaterThan(0.9f), where);
                        // The depth is LiDAR's: there is none under 0.3 m.
                        float z = truth[j].z;
                        Assert.That(s.DepthConfidence >= 0, Is.EqualTo(z >= 0.3f), where);
                        if (s.DepthConfidence < 0) continue;
                        float3 back = d.Unproject(s.Pixel, s.Depth);
                        Assert.That(math.distance(back, truth[j]), Is.LessThan(1e-3f), where);
                    }
                }
            }
        }

        [Test]
        public void Hands_Fixtures_Wave_IsTwoHandsWaving()
        {
            var log = SampleHands.Log(SampleHands.WaveName);
            Assert.That(log.Count, Is.EqualTo(151));
            HandsDetection d = default(HandsDetection);
            float leftMin = float.MaxValue, leftMax = float.MinValue, rightMin = float.MaxValue, rightMax = float.MinValue;
            float headMin = float.MaxValue, headMax = float.MinValue;
            for (int i = 0; i < log.Count; i++)
            {
                log.Read(i, ref d, out float3 head, out quaternion _);
                Assert.That(d.HandCount, Is.EqualTo(2));
                Assert.That(d.Hand0.Side, Is.EqualTo((int)HandSide.Left));
                Assert.That(d.Hand1.Side, Is.EqualTo((int)HandSide.Right));
                SampleHands.Truth(SampleHands.WaveName, i, HandSide.Left, out JointSet left);
                SampleHands.Truth(SampleHands.WaveName, i, HandSide.Right, out JointSet right);
                // The middle fingertip's sideways travel in the camera's picture.
                leftMin = math.min(leftMin, left[HandJoint.MiddleTip].x);
                leftMax = math.max(leftMax, left[HandJoint.MiddleTip].x);
                rightMin = math.min(rightMin, right[HandJoint.MiddleTip].x);
                rightMax = math.max(rightMax, right[HandJoint.MiddleTip].x);
                headMin = math.min(headMin, head.x);
                headMax = math.max(headMax, head.x);
                Assert.That(left[HandJoint.MiddleTip].x, Is.LessThan(right[HandJoint.MiddleTip].x), "the left hand stays on the left");
                // Open hands, held up: the fingertips are above the wrists.
                Assert.That(right[HandJoint.MiddleTip].y, Is.GreaterThan(right[HandJoint.Wrist].y + 0.10f));
            }
            Assert.That(leftMax - leftMin, Is.GreaterThan(0.06f), "the left hand waves");
            Assert.That(rightMax - rightMin, Is.GreaterThan(0.06f), "the right hand waves");
            Assert.That(headMax - headMin, Is.GreaterThan(0.01f), "the head sways, so the camera pose of a record matters");
        }

        [Test]
        public void Hands_Fixtures_Press_IsOneIndexFingerPressingThreeTimes()
        {
            string name = SampleHands.PressName;
            var log = SampleHands.Log(name);
            HandsDetection d = default(HandsDetection);
            int presses = 0;
            bool pressed = false;
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i < log.Count; i++)
            {
                log.Read(i, ref d, out float3 head, out quaternion _);
                Assert.That(d.HandCount, Is.EqualTo(1));
                Assert.That(d.Hand0.Side, Is.EqualTo((int)HandSide.Right));
                Assert.That(head, Is.EqualTo(new float3(0f, 1.6f, 0f)), "the head stands still");
                Assert.That(SampleHands.Truth(name, i, HandSide.Right, out JointSet joints), Is.True);
                Assert.That(SampleHands.Truth(name, i, HandSide.Left, out JointSet _), Is.False);
                float3 tip = joints[HandJoint.IndexTip];
                // The tip moves along a line straight away from the camera.
                Assert.That(tip.x, Is.EqualTo(0.04f).Within(1e-4f));
                Assert.That(tip.y, Is.EqualTo(0.02f).Within(1e-4f));
                lowest = math.min(lowest, tip.z);
                highest = math.max(highest, tip.z);
                // The index is straight, the others are curled: the index tip is the farthest fingertip from the wrist.
                float index = math.distance(tip, joints[HandJoint.Wrist]);
                Assert.That(index, Is.GreaterThan(math.distance(joints[HandJoint.MiddleTip], joints[HandJoint.Wrist]) + 0.04f));
                bool nowPressed = tip.z > 0.46f;
                if (nowPressed && !pressed) presses++;
                pressed = nowPressed;
            }
            Assert.That(lowest, Is.EqualTo(SampleHands.PressRetractedMetres).Within(1e-3f));
            Assert.That(highest, Is.EqualTo(SampleHands.PressPressedMetres).Within(1e-3f));
            Assert.That(presses, Is.EqualTo(SampleHands.PressCount));
        }

        [Test]
        public void Hands_Fixtures_Pinch_IsThreePinches()
        {
            string name = SampleHands.PinchName;
            var log = SampleHands.Log(name);
            HandsDetection d = default(HandsDetection);
            int pinches = 0;
            bool closed = false;
            float widest = 0f, tightest = float.MaxValue;
            for (int i = 0; i < log.Count; i++)
            {
                log.Read(i, ref d, out float3 _, out quaternion _);
                Assert.That(d.HandCount, Is.EqualTo(1));
                Assert.That(SampleHands.Truth(name, i, HandSide.Right, out JointSet joints), Is.True);
                float gap = math.distance(joints[HandJoint.ThumbTip], joints[HandJoint.IndexTip]);
                widest = math.max(widest, gap);
                tightest = math.min(tightest, gap);
                // The design's pinch ratio: the gap over the palm width. Closed under 0.25, open over 0.40.
                float ratio = gap / HandModel.PalmWidth;
                bool nowClosed = ratio < HandsConstants.PinchClose;
                if (nowClosed && !closed) pinches++;
                closed = nowClosed;
                // Bones keep their lengths however far the hand has closed.
                for (int j = 1; j < HandJoints.Count; j++)
                    Assert.That(math.distance(joints[j], joints[HandJoints.Parent(j)]), Is.EqualTo(HandModel.BoneLength(j)).Within(2e-4f),
                        "record " + i + " joint " + j);
            }
            Assert.That(tightest, Is.LessThan(1e-3f), "the thumb and the index touch");
            Assert.That(widest / HandModel.PalmWidth, Is.GreaterThan(HandsConstants.PinchOpen * 2f), "and open wide");
            Assert.That(pinches, Is.EqualTo(SampleHands.PinchCount));
        }

        [Test]
        public void Hands_Fixtures_HeadPoseIsTheHeadAtThePoll_AndTheDetectionsIsTheHeadAtCapture()
        {
            // The wave's head sways, so the two differ: the camera pose in a detection is 80 ms older than the head pose of its record.
            var wave = SampleHands.Log(SampleHands.WaveName);
            HandsDetection d = default(HandsDetection);
            int differing = 0;
            for (int i = 0; i < wave.Count; i++)
            {
                wave.Read(i, ref d, out float3 head, out quaternion _);
                if (math.distance(d.CameraToSession.c3.xyz, head) > 1e-4f) differing++;
            }
            Assert.That(differing, Is.GreaterThan(wave.Count / 2));

            // The press stands still: they are the same pose.
            var press = SampleHands.Log(SampleHands.PressName);
            press.Read(40, ref d, out float3 pressHead, out quaternion pressRotation);
            Assert.That(math.distance(d.CameraToSession.c3.xyz, pressHead), Is.LessThan(1e-6f));
            Assert.That(pressRotation.value, Is.EqualTo(quaternion.identity.value));
        }

        [Test]
        public void Hands_Fixtures_TheNamedLogsAreTheThreeOfTheDesign()
        {
            Assert.That(SampleHands.Names, Is.EqualTo(new[] { "hands_wave", "hands_press", "hands_pinch" }));
            Assert.That(SampleHands.SecondsOf(SampleHands.WaveName), Is.EqualTo(5.0));
            Assert.Throws<ArgumentException>(() => SampleHands.Bytes("hands_other"));
        }
    }
}
