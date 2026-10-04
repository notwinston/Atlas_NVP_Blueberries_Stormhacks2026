using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// Link_RoundTrip (Master Plan section 11.1): every Appendix B message survives encode and decode, byte for
    /// byte against a golden encoding made independently of the codec; State is 202 bytes; unknown types and
    /// trailing bytes are ignored. The reserved PeerState id has no layout and is not part of the round trip.
    /// </summary>
    public sealed class Link_RoundTrip
    {
        static byte[] Hex(string hex)
        {
            hex = hex.Replace(" ", string.Empty);
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        static byte[] Slice(byte[] buffer, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(buffer, 0, copy, 0, length);
            return copy;
        }

        static byte[] WithTrailing(byte[] packet, int extra)
        {
            var longer = new byte[packet.Length + extra];
            Buffer.BlockCopy(packet, 0, longer, 0, packet.Length);
            for (int i = packet.Length; i < longer.Length; i++) longer[i] = 0xEE;
            return longer;
        }

        static StateMessage SampleState()
        {
            var s = new StateMessage();
            s.Tracking = 1;
            s.PoseJumpRejected = true;
            s.CueActive = true;
            s.QualityTier = 2;
            s.DemoLock = true;
            s.SceneId = 0x1234;
            s.Seq = 0xDEADBEEF;
            s.TimeUs = 0x0102030405060708L;
            s.HeadPosX = 1.5f; s.HeadPosY = 1.62f; s.HeadPosZ = -2.25f;
            s.HeadRotX = 0.1f; s.HeadRotY = -0.2f; s.HeadRotZ = 0.3f; s.HeadRotW = 0.9f;
            s.SpaceId = 2;
            s.BumpLevel = 3;
            s.SetTile(1, -2, long.MaxValue, long.MinValue, 0x1122334455667788L, -1);
            s.Sheet = -3;
            for (int i = 0; i < StateMessage.XformLength; i++) s.SetXform(i, Math.PI * (i + 1) * (i % 2 == 0 ? 1 : -1));
            for (int i = 0; i < StateMessage.ParamLength; i++) s.SetParam(i, 0.25f * (i + 1));
            s.WorldVer = 0xBEEF;
            return s;
        }

        // The golden encoding of State, written field by field with BinaryWriter (little-endian by definition).
        static byte[] GoldenState(in StateMessage s)
        {
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write((byte)0x10);
            w.Write(s.Flags);
            w.Write(s.SceneId);
            w.Write(s.Seq);
            w.Write(s.TimeUs);
            w.Write(s.HeadPosX); w.Write(s.HeadPosY); w.Write(s.HeadPosZ);
            w.Write(s.HeadRotX); w.Write(s.HeadRotY); w.Write(s.HeadRotZ); w.Write(s.HeadRotW);
            w.Write(s.SpaceId);
            w.Write(s.BumpLevel);
            for (int i = 0; i < 6; i++) w.Write(s.GetTile(i));
            w.Write(s.Sheet);
            for (int i = 0; i < 9; i++) w.Write(s.GetXform(i));
            for (int i = 0; i < 8; i++) w.Write(s.GetParam(i));
            w.Write(s.WorldVer);
            w.Flush();
            return stream.ToArray();
        }

        [Test]
        public void State_is_202_bytes_and_matches_the_golden_layout()
        {
            Assert.That(StateMessage.Size, Is.EqualTo(202));
            StateMessage s = SampleState();
            var buffer = new byte[256];
            int n = s.WriteTo(buffer, 0);
            Assert.That(n, Is.EqualTo(202));
            byte[] golden = GoldenState(s);
            Assert.That(golden.Length, Is.EqualTo(202));
            Assert.That(Slice(buffer, n), Is.EqualTo(golden));

            // Field offsets of Appendix B.1, spelled out.
            Assert.That(buffer[0], Is.EqualTo(0x10));
            Assert.That(buffer[1], Is.EqualTo(0x6D), "flags: tracking 1, jump, cue, tier 2, lock");
            Assert.That(Slice(buffer, 16), Is.EqualTo(Hex("10 6D 34 12 EF BE AD DE 08 07 06 05 04 03 02 01")));
            Assert.That(buffer[44], Is.EqualTo(2), "spaceId");
            Assert.That(buffer[45], Is.EqualTo(3), "bumpLevel");
            Assert.That(buffer[46], Is.EqualTo(1), "tileId[0] low byte");
            Assert.That(buffer[94], Is.EqualTo(0xFD), "sheet low byte");
            Assert.That(buffer[95], Is.EqualTo(0xFF), "sheet high byte");
            Assert.That(BitConverter.ToDouble(buffer, 96), Is.EqualTo(Math.PI), "xform[0]");
            Assert.That(BitConverter.ToSingle(buffer, 168), Is.EqualTo(0.25f), "p[0]");
            Assert.That(buffer[200], Is.EqualTo(0xEF));
            Assert.That(buffer[201], Is.EqualTo(0xBE));

            var back = new StateMessage();
            Assert.That(back.ReadFrom(buffer, 0, n), Is.True);
            var again = new byte[202];
            back.WriteTo(again, 0);
            Assert.That(again, Is.EqualTo(golden));
            Assert.That(back.Tracking, Is.EqualTo(1));
            Assert.That(back.QualityTier, Is.EqualTo(2));
            Assert.That(back.PoseJumpRejected && back.CueActive && back.DemoLock);
            Assert.That(back.GetTile(3), Is.EqualTo(long.MinValue));
            Assert.That(back.GetXform(8), Is.EqualTo(Math.PI * 9));
            Assert.That(back.Sheet, Is.EqualTo(-3));
        }

        [Test]
        public void State_reads_at_an_offset_ignores_trailing_bytes_and_rejects_short_or_foreign_packets()
        {
            StateMessage s = SampleState();
            var buffer = new byte[300];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xEE;
            Assert.That(s.WriteTo(buffer, 17), Is.EqualTo(202));
            Assert.That(buffer[16], Is.EqualTo(0xEE));
            Assert.That(buffer[17 + 202], Is.EqualTo(0xEE));

            var back = new StateMessage();
            Assert.That(back.ReadFrom(buffer, 17, 250), Is.True, "trailing bytes are ignored");
            Assert.That(GoldenState(back), Is.EqualTo(GoldenState(s)));

            var untouched = StateMessage.Identity;
            Assert.That(untouched.ReadFrom(buffer, 17, 201), Is.False, "one byte short");
            buffer[17] = MessageId.PeerState;
            Assert.That(untouched.ReadFrom(buffer, 17, 202), Is.False, "another id");
            Assert.That(untouched.HeadRotW, Is.EqualTo(1f));
            Assert.That(untouched.GetXform(4), Is.EqualTo(1.0));
            Assert.That(() => s.WriteTo(new byte[201], 0), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void State_flags_pack_into_the_documented_bits()
        {
            var s = new StateMessage();
            s.Tracking = 2;
            Assert.That(s.Flags, Is.EqualTo(0x02));
            s.PoseJumpRejected = true;
            Assert.That(s.Flags, Is.EqualTo(0x06));
            s.CueActive = true;
            Assert.That(s.Flags, Is.EqualTo(0x0E));
            s.QualityTier = 3;
            Assert.That(s.Flags, Is.EqualTo(0x3E));
            s.DemoLock = true;
            Assert.That(s.Flags, Is.EqualTo(0x7E));
            s.Tracking = 0;
            s.QualityTier = 1;
            s.CueActive = false;
            Assert.That(s.Flags, Is.EqualTo(0x54));
            Assert.That(() => s.GetTile(6), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => s.SetXform(9, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => s.SetParam(-1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TPing_and_TPong_golden()
        {
            var ping = new TPingMessage { Seq = 0x01020304, T1Us = 0x1112131415161718L };
            var buffer = new byte[64];
            Assert.That(ping.WriteTo(buffer, 0), Is.EqualTo(13));
            Assert.That(TPingMessage.Size, Is.EqualTo(13));
            Assert.That(Slice(buffer, 13), Is.EqualTo(Hex("03 04 03 02 01 18 17 16 15 14 13 12 11")));
            var pingBack = new TPingMessage();
            Assert.That(pingBack.ReadFrom(WithTrailing(Slice(buffer, 13), 5), 0, 18), Is.True);
            Assert.That(pingBack.Seq, Is.EqualTo(ping.Seq));
            Assert.That(pingBack.T1Us, Is.EqualTo(ping.T1Us));
            Assert.That(pingBack.ReadFrom(buffer, 0, 12), Is.False);

            var pong = new TPongMessage { Seq = 7, T1Us = 1, T2Us = -2, T3Us = 0x0000010000000000L };
            Assert.That(pong.WriteTo(buffer, 0), Is.EqualTo(29));
            Assert.That(TPongMessage.Size, Is.EqualTo(29));
            Assert.That(Slice(buffer, 29), Is.EqualTo(Hex(
                "04 07 00 00 00  01 00 00 00 00 00 00 00  FE FF FF FF FF FF FF FF  00 00 00 00 00 01 00 00")));
            var pongBack = new TPongMessage();
            Assert.That(pongBack.ReadFrom(buffer, 0, 29), Is.True);
            Assert.That(pongBack.T2Us, Is.EqualTo(-2L));
            Assert.That(pongBack.T3Us, Is.EqualTo(0x0000010000000000L));
            Assert.That(pongBack.ReadFrom(buffer, 0, 28), Is.False);
            Assert.That(pingBack.ReadFrom(buffer, 0, 29), Is.False, "a pong is not a ping");
        }

        [Test]
        public void ParamSet_golden()
        {
            var set = new ParamSetMessage();
            set.Add(1, 1.0f);
            set.Add(0x0203, -2.5f);
            var buffer = new byte[ParamSetMessage.MaxSize];
            int n = set.WriteTo(buffer, 0);
            Assert.That(n, Is.EqualTo(14));
            Assert.That(Slice(buffer, n), Is.EqualTo(Hex("21 02  01 00 00 00 80 3F  03 02 00 00 20 C0")));

            var back = new ParamSetMessage();
            Assert.That(back.ReadFrom(WithTrailing(Slice(buffer, n), 3), 0, n + 3), Is.True);
            Assert.That(back.Count, Is.EqualTo(2));
            Assert.That(back.Ids[1], Is.EqualTo((ushort)0x0203));
            Assert.That(back.Values[1], Is.EqualTo(-2.5f));
            var again = new byte[n];
            back.WriteTo(again, 0);
            Assert.That(again, Is.EqualTo(Slice(buffer, n)));

            Assert.That(back.ReadFrom(buffer, 0, n - 1), Is.False, "the count promises more than the packet holds");
            Assert.That(back.Count, Is.EqualTo(2), "a failed read leaves the message as it was");

            var full = new ParamSetMessage();
            for (int i = 0; i < 255; i++) Assert.That(full.Add((ushort)i, i), Is.True);
            Assert.That(full.Add(999, 0f), Is.False);
            Assert.That(full.WriteTo(buffer, 0), Is.EqualTo(ParamSetMessage.MaxSize));
            Assert.That(back.ReadFrom(buffer, 0, ParamSetMessage.MaxSize), Is.True);
            Assert.That(back.Count, Is.EqualTo(255));
            Assert.That(back.Values[254], Is.EqualTo(254f));
        }

        static TelemetryMessage SampleTelemetry()
        {
            return new TelemetryMessage
            {
                TimeUs = 123456789012L, Seq = 42, SceneId = 3, SpaceId = 2, Tier = 1,
                Fps = 59.9f, P50 = 16.6f, P95 = 17.1f, P99 = 19.5f, Max = 33.4f, Over17_2 = 0.02f, Over33_8 = 0.001f,
                CpuMainP95 = 6.5f, CpuRenderP95 = 3.25f, GpuP95 = 9.75f, RenderScale = 0.8f,
                DrawCalls = 187, SetPass = 12, TrisK = 312, MemMB = 845, VisibleTiles = 45, Copies = 25, RttMs = 4,
                LossPermille = 7, CueCount = 2, GcAllocB = 0xA1B2C3D4,
                Thermal = 1, Battery = 83, Tracking = 0, Msaa = 4, StateHz = 60, BumpMax = 2, AssetsPlaced = 9, AssetsPending = 3
            };
        }

        [Test]
        public void Telemetry_is_91_bytes_and_matches_the_golden_layout()
        {
            TelemetryMessage t = SampleTelemetry();
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write((byte)0x30);
            w.Write(t.TimeUs); w.Write(t.Seq); w.Write(t.SceneId); w.Write(t.SpaceId); w.Write(t.Tier);
            w.Write(t.Fps); w.Write(t.P50); w.Write(t.P95); w.Write(t.P99); w.Write(t.Max); w.Write(t.Over17_2); w.Write(t.Over33_8);
            w.Write(t.CpuMainP95); w.Write(t.CpuRenderP95); w.Write(t.GpuP95); w.Write(t.RenderScale);
            w.Write(t.DrawCalls); w.Write(t.SetPass); w.Write(t.TrisK); w.Write(t.MemMB); w.Write(t.VisibleTiles);
            w.Write(t.Copies); w.Write(t.RttMs); w.Write(t.LossPermille); w.Write(t.CueCount);
            w.Write(t.GcAllocB);
            w.Write(t.Thermal); w.Write(t.Battery); w.Write(t.Tracking); w.Write(t.Msaa); w.Write(t.StateHz);
            w.Write(t.BumpMax); w.Write(t.AssetsPlaced); w.Write(t.AssetsPending);
            w.Flush();
            byte[] golden = stream.ToArray();
            Assert.That(golden.Length, Is.EqualTo(91));
            Assert.That(TelemetryMessage.Size, Is.EqualTo(91));

            var buffer = new byte[128];
            Assert.That(t.WriteTo(buffer, 0), Is.EqualTo(91));
            Assert.That(Slice(buffer, 91), Is.EqualTo(golden));

            var back = new TelemetryMessage();
            Assert.That(back.ReadFrom(WithTrailing(golden, 9), 0, 100), Is.True);
            var again = new byte[91];
            back.WriteTo(again, 0);
            Assert.That(again, Is.EqualTo(golden));
            Assert.That(back.GcAllocB, Is.EqualTo(0xA1B2C3D4));
            Assert.That(back.AssetsPending, Is.EqualTo(3));
            Assert.That(back.ReadFrom(golden, 0, 90), Is.False);

            // One CSV column per wire field (section 7.3.3): 34 fields.
            Assert.That(TelemetryMessage.CsvHeader.Split(',').Length, Is.EqualTo(34));
        }

        [Test]
        public void RoomOutline_golden()
        {
            var m = new RoomOutlineMessage();
            m.AddPoint(-2f, -2.5f);
            m.AddPoint(2f, -2.5f);
            m.AddPoint(2f, 2.5f);
            m.AddPoint(-2f, 2.5f);
            m.Height = 2.4f;
            m.RectCx = 0.1f; m.RectCz = -0.2f; m.RectW = 3f; m.RectD = 4f; m.RectYaw = 0.5f;
            m.AddBox(new RoomOutlineBox { Id = 7, Category = 3, Cx = 1f, Cz = -1f, W = 0.8f, D = 0.6f, H = 0.75f, Yaw = 1.5f });
            m.AddBox(new RoomOutlineBox { Id = 9, Category = 12, Cx = -1.25f, Cz = 2f, W = 2f, D = 0.9f, H = 0.45f, Yaw = -0.25f });

            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write((byte)0x41);
            w.Write((byte)4);
            w.Write(-2f); w.Write(-2.5f); w.Write(2f); w.Write(-2.5f); w.Write(2f); w.Write(2.5f); w.Write(-2f); w.Write(2.5f);
            w.Write(2.4f);
            w.Write(0.1f); w.Write(-0.2f); w.Write(3f); w.Write(4f); w.Write(0.5f);
            w.Write((byte)2);
            w.Write((byte)7); w.Write((byte)3); w.Write(1f); w.Write(-1f); w.Write(0.8f); w.Write(0.6f); w.Write(0.75f); w.Write(1.5f);
            w.Write((byte)9); w.Write((byte)12); w.Write(-1.25f); w.Write(2f); w.Write(2f); w.Write(0.9f); w.Write(0.45f); w.Write(-0.25f);
            w.Flush();
            byte[] golden = stream.ToArray();
            Assert.That(golden.Length, Is.EqualTo(2 + 32 + 4 + 20 + 1 + 52));
            Assert.That(m.Size, Is.EqualTo(golden.Length));

            var buffer = new byte[RoomOutlineMessage.MaxSize];
            int n = m.WriteTo(buffer, 0);
            Assert.That(Slice(buffer, n), Is.EqualTo(golden));

            var back = new RoomOutlineMessage();
            Assert.That(back.ReadFrom(WithTrailing(golden, 4), 0, golden.Length + 4), Is.True);
            Assert.That(back.PointCount, Is.EqualTo(4));
            Assert.That(back.BoxCount, Is.EqualTo(2));
            Assert.That(back.Boxes[1].Category, Is.EqualTo(12));
            Assert.That(back.Boxes[1].Yaw, Is.EqualTo(-0.25f));
            Assert.That(back.RectYaw, Is.EqualTo(0.5f));
            var again = new byte[golden.Length];
            back.WriteTo(again, 0);
            Assert.That(again, Is.EqualTo(golden));

            Assert.That(back.ReadFrom(golden, 0, golden.Length - 1), Is.False, "a box is cut short");
            Assert.That(back.ReadFrom(golden, 0, 30), Is.False, "the polygon is cut short");

            var empty = new RoomOutlineMessage();
            Assert.That(empty.WriteTo(buffer, 0), Is.EqualTo(27));
            Assert.That(back.ReadFrom(buffer, 0, 27), Is.True);
            Assert.That(back.PointCount + back.BoxCount, Is.EqualTo(0));
        }

        static void AssertJsonGolden(JsonMessage message, JsonMessage blank, string goldenJson)
        {
            byte[] golden = new byte[1 + Encoding.UTF8.GetByteCount(goldenJson)];
            golden[0] = message.Id;
            Encoding.UTF8.GetBytes(goldenJson, 0, goldenJson.Length, golden, 1);

            Assert.That(Encoding.UTF8.GetString(message.ToBytes(), 1, message.ToBytes().Length - 1), Is.EqualTo(goldenJson));
            Assert.That(message.ToBytes(), Is.EqualTo(golden), MessageId.Name(message.Id));

            var buffer = new byte[golden.Length + 16];
            Assert.That(message.WriteTo(buffer, 3), Is.EqualTo(golden.Length));
            var written = new byte[golden.Length];
            Buffer.BlockCopy(buffer, 3, written, 0, golden.Length);
            Assert.That(written, Is.EqualTo(golden));
            Assert.That(() => message.WriteTo(new byte[golden.Length - 1], 0), Throws.TypeOf<ArgumentException>());

            // Decode, re-encode: the same bytes. Trailing bytes after the JSON object are ignored.
            Assert.That(blank.ReadFrom(WithTrailing(golden, 6), 0, golden.Length + 6), Is.True);
            Assert.That(blank.ToBytes(), Is.EqualTo(golden), MessageId.Name(message.Id));

            // Another id is not this message.
            byte[] foreign = (byte[])golden.Clone();
            foreign[0] = MessageId.PeerState;
            Assert.That(blank.ReadFrom(foreign, 0, foreign.Length), Is.False);
            Assert.That(MessageId.IsJson(message.Id), Is.True);
        }

        [Test]
        public void Json_messages_golden()
        {
            AssertJsonGolden(
                new HelloMessage
                {
                    Role = LinkRole.Phone, Device = "iPhone15,3", App = "atlas", Build = "d0e9127", WorldHash = "0123456789abcdef", RoomId = "room_7",
                    SessionId = "20261004T153012Z-a3f9"
                },
                new HelloMessage(),
                "{\"proto\":[1,0],\"role\":\"phone\",\"device\":\"iPhone15,3\",\"app\":\"atlas\",\"build\":\"d0e9127\",\"worldHash\":\"0123456789abcdef\",\"roomId\":\"room_7\"," +
                "\"sessionId\":\"20261004T153012Z-a3f9\"}");

            AssertJsonGolden(
                new WelcomeMessage { SessionId = "s-20261004-01", WorldHash = "0123456789abcdef", RoomId = "room_7", DemoLock = true, ForgeUrl = "http://192.168.1.20:8000" },
                new WelcomeMessage(),
                "{\"proto\":[1,0],\"sessionId\":\"s-20261004-01\",\"worldHash\":\"0123456789abcdef\",\"roomId\":\"room_7\",\"demoLock\":true,\"forgeUrl\":\"http://192.168.1.20:8000\"}");

            var registry = new ParamRegistry();
            registry.DeclareFloat("_HypL", "curved", 0.5f, 6f, 2f, null, true).Set(2.5f);
            registry.DeclareInt("render.msaa", "render", 2, 4, 4);
            AssertJsonGolden(
                ParamManifestMessage.FromRegistry(registry),
                new ParamManifestMessage(),
                "{\"params\":[" +
                "{\"id\":0,\"name\":\"_HypL\",\"group\":\"curved\",\"min\":0.5,\"max\":6.0,\"def\":2.0,\"val\":2.5,\"onRelease\":true,\"kind\":\"float\",\"active\":true}," +
                "{\"id\":1,\"name\":\"render.msaa\",\"group\":\"render\",\"min\":2.0,\"max\":4.0,\"def\":4.0,\"val\":4.0,\"onRelease\":false,\"kind\":\"int\",\"active\":true}]}");

            AssertJsonGolden(
                new SceneCmdMessage { Space = "S2", Variant = "mode2", Args = JsonValue.Object().Set("rho", 3) },
                new SceneCmdMessage(),
                "{\"space\":\"S2\",\"variant\":\"mode2\",\"args\":{\"rho\":3}}");

            AssertJsonGolden(
                new OperatorCmdMessage { Cmd = "quality", Args = JsonValue.Object().Set("tier", 2) },
                new OperatorCmdMessage(),
                "{\"cmd\":\"quality\",\"args\":{\"tier\":2}}");

            AssertJsonGolden(
                new EventMessage(1234567890123L, "S2", "param", JsonValue.Object().Set("id", 3).Set("name", "curved.cullRho").Set("value", 3.25f)),
                new EventMessage(),
                "{\"t_us\":1234567890123,\"scene\":\"S2\",\"ev\":\"param\",\"data\":{\"id\":3,\"name\":\"curved.cullRho\",\"value\":3.25}}");

            AssertJsonGolden(
                new LogMessage { TimeUs = 99, Level = "warn", Msg = "tracking \"limited\"\nfor 2 s" },
                new LogMessage(),
                "{\"t_us\":99,\"level\":\"warn\",\"msg\":\"tracking \\\"limited\\\"\\nfor 2 s\"}");

            AssertJsonGolden(
                new JobStatusMessage { JobId = "j-12", RoomId = "room_7", BoxId = 4, State = "generating", Source = "local", Error = "" },
                new JobStatusMessage(),
                "{\"jobId\":\"j-12\",\"roomId\":\"room_7\",\"boxId\":4,\"state\":\"generating\",\"source\":\"local\",\"error\":\"\"}");

            AssertJsonGolden(
                new AssetReadyMessage
                {
                    JobId = "j-12", RoomId = "room_7", BoxId = 4, Sha256 = "ab12", Url = "http://192.168.1.20:8000/assets/ab12.glb",
                    Bytes = 412345, Tris = 4980, MaxEdge = 0.24f, BoundsX = 0.8f, BoundsY = 0.75f, BoundsZ = 0.6f, Palette = 5
                },
                new AssetReadyMessage(),
                "{\"jobId\":\"j-12\",\"roomId\":\"room_7\",\"boxId\":4,\"sha256\":\"ab12\",\"url\":\"http://192.168.1.20:8000/assets/ab12.glb\"," +
                "\"bytes\":412345,\"tris\":4980,\"maxEdge\":0.24,\"bounds\":[0.8,0.75,0.6],\"palette\":5}");

            AssertJsonGolden(
                new AssetAckMessage { JobId = "j-12", BoxId = 4, Sha256 = "ab12", Ok = true, LoadMs = 41.5f, WorstFrameMs = 17.25f, Reason = "" },
                new AssetAckMessage(),
                "{\"jobId\":\"j-12\",\"boxId\":4,\"sha256\":\"ab12\",\"ok\":true,\"loadMs\":41.5,\"worstFrameMs\":17.25,\"reason\":\"\"}");

            AssertJsonGolden(new ByeMessage("hub stopping"), new ByeMessage(), "{\"reason\":\"hub stopping\"}");
        }

        [Test]
        public void Json_messages_tolerate_missing_and_unknown_members()
        {
            byte[] packet = JsonMessage.Encode(MessageId.Welcome, AtlasJson.Parse("{\"sessionId\":\"s1\",\"futureField\":[1,2,3]}"));
            var welcome = new WelcomeMessage();
            Assert.That(welcome.ReadFrom(packet, 0, packet.Length), Is.True);
            Assert.That(welcome.SessionId, Is.EqualTo("s1"));
            Assert.That(welcome.ForgeUrl, Is.EqualTo(string.Empty));
            Assert.That(welcome.DemoLock, Is.False);
            Assert.That(welcome.ProtoMajor, Is.EqualTo(0), "a missing proto reads as 0 and is refused as a mismatch");

            // A Hello from a client that names no session (a Spectator, or a build from before the member existed).
            byte[] oldHello = JsonMessage.Encode(MessageId.Hello, AtlasJson.Parse("{\"proto\":[1,0],\"role\":\"spectator\",\"device\":\"pc\"}"));
            var hello = new HelloMessage { SessionId = "left over" };
            Assert.That(hello.ReadFrom(oldHello, 0, oldHello.Length), Is.True);
            Assert.That(hello.Role, Is.EqualTo(LinkRole.Spectator));
            Assert.That(hello.SessionId, Is.EqualTo(string.Empty));

            byte[] notJson = { MessageId.Welcome, (byte)'n', (byte)'o' };
            Assert.That(welcome.ReadFrom(notJson, 0, notJson.Length), Is.False);
            byte[] notObject = { MessageId.Welcome, (byte)'[', (byte)']' };
            Assert.That(welcome.ReadFrom(notObject, 0, notObject.Length), Is.False);
            Assert.That(welcome.ReadFrom(packet, 0, 1), Is.False);
        }

        [Test]
        public void Every_id_of_Appendix_B_is_declared_and_PeerState_has_no_codec()
        {
            Assert.That(MessageId.Hello, Is.EqualTo(0x01));
            Assert.That(MessageId.Welcome, Is.EqualTo(0x02));
            Assert.That(MessageId.TPing, Is.EqualTo(0x03));
            Assert.That(MessageId.TPong, Is.EqualTo(0x04));
            Assert.That(MessageId.State, Is.EqualTo(0x10));
            Assert.That(MessageId.PeerState, Is.EqualTo(0x11));
            Assert.That(MessageId.ParamManifest, Is.EqualTo(0x20));
            Assert.That(MessageId.ParamSet, Is.EqualTo(0x21));
            Assert.That(MessageId.SceneCmd, Is.EqualTo(0x22));
            Assert.That(MessageId.OperatorCmd, Is.EqualTo(0x23));
            Assert.That(MessageId.Telemetry, Is.EqualTo(0x30));
            Assert.That(MessageId.Event, Is.EqualTo(0x40));
            Assert.That(MessageId.RoomOutline, Is.EqualTo(0x41));
            Assert.That(MessageId.Log, Is.EqualTo(0x42));
            Assert.That(MessageId.JobStatus, Is.EqualTo(0x50));
            Assert.That(MessageId.AssetReady, Is.EqualTo(0x51));
            Assert.That(MessageId.AssetAck, Is.EqualTo(0x52));
            Assert.That(MessageId.Bye, Is.EqualTo(0x7F));

            int known = 0;
            for (int id = 0; id < 256; id++) if (MessageId.IsKnown((byte)id)) known++;
            Assert.That(known, Is.EqualTo(17));
            Assert.That(MessageId.IsKnown(MessageId.PeerState), Is.False);
            Assert.That(MessageId.Name(MessageId.PeerState), Is.EqualTo("PeerState"));
            Assert.That(MessageId.Name(0x99), Is.Null);
            Assert.That(LinkProtocol.ConnectionKey, Is.EqualTo("atlas-v1"));
            Assert.That(LinkProtocol.DefaultPort, Is.EqualTo(9050));
        }

        [Test]
        public void Event_is_one_json_line()
        {
            var e = new EventMessage(5, "S1", "note", JsonValue.Object().Set("text", "line one\nline two\u2028three"));
            string line = e.ToJsonLine();
            Assert.That(line.IndexOf('\n'), Is.LessThan(0));
            Assert.That(line.IndexOf('\r'), Is.LessThan(0));
            Assert.That(line.IndexOf((char)0x2028), Is.LessThan(0));
            Assert.That(line, Is.EqualTo("{\"t_us\":5,\"scene\":\"S1\",\"ev\":\"note\",\"data\":{\"text\":\"line one\\nline two\\u2028three\"}}"));

            EventMessage back;
            Assert.That(EventMessage.TryParseLine(line, out back), Is.True);
            Assert.That(back.TimeUs, Is.EqualTo(5L));
            Assert.That(back.Ev, Is.EqualTo("note"));
            Assert.That(back.Data["text"].AsString(), Is.EqualTo("line one\nline two\u2028three"));
            Assert.That(back.ToJsonLine(), Is.EqualTo(line));
            Assert.That(EventMessage.TryParseLine("", out back), Is.False);
            Assert.That(EventMessage.TryParseLine("{broken", out back), Is.False);
            Assert.That(EventMessage.TryParseLine("[1]", out back), Is.False);

            var registry = new ParamRegistry();
            ParamEntry rho = registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            Assert.That(LinkEvents.ParamChanged(10, "S2", rho, ParamSource.Remote).ToJsonLine(), Is.EqualTo(
                "{\"t_us\":10,\"scene\":\"S2\",\"ev\":\"param\",\"data\":{\"id\":0,\"name\":\"curved.cullRho\",\"value\":3.0,\"source\":\"remote\"}}"));
        }

        [Test]
        public void The_six_operator_commands_round_trip()
        {
            OperatorCommand[] commands =
            {
                OperatorCommand.SwitchSpace(4, "klein"), OperatorCommand.Home(), OperatorCommand.Recentre(),
                OperatorCommand.Quality(2), OperatorCommand.Stop(), OperatorCommand.Lock(true)
            };
            string[] golden =
            {
                "{\"space\":\"S4\",\"variant\":\"klein\",\"args\":{}}",
                "{\"cmd\":\"home\",\"args\":{}}",
                "{\"cmd\":\"recentre\",\"args\":{}}",
                "{\"cmd\":\"quality\",\"args\":{\"tier\":2}}",
                "{\"cmd\":\"cue\",\"args\":{}}",
                "{\"cmd\":\"lock\",\"args\":{\"on\":true}}"
            };
            for (int i = 0; i < commands.Length; i++)
            {
                JsonMessage message = commands[i].ToMessage();
                Assert.That(message.Id, Is.EqualTo(i == 0 ? MessageId.SceneCmd : MessageId.OperatorCmd));
                Assert.That(message.ToJson().ToJson(), Is.EqualTo(golden[i]));

                byte[] packet = message.ToBytes();
                OperatorCommand back;
                if (i == 0)
                {
                    var scene = new SceneCmdMessage();
                    Assert.That(scene.ReadFrom(packet, 0, packet.Length), Is.True);
                    Assert.That(OperatorCommand.TryFrom(scene, out back), Is.True);
                }
                else
                {
                    var op = new OperatorCmdMessage();
                    Assert.That(op.ReadFrom(packet, 0, packet.Length), Is.True);
                    Assert.That(OperatorCommand.TryFrom(op, out back), Is.True);
                }
                Assert.That(back.Kind, Is.EqualTo(commands[i].Kind));
                Assert.That(back.Space, Is.EqualTo(commands[i].Space));
                Assert.That(back.Tier, Is.EqualTo(commands[i].Tier));
                Assert.That(back.On, Is.EqualTo(commands[i].On));
            }
            Assert.That(commands[0].Variant, Is.EqualTo("klein"));
            Assert.That(Enum.GetValues(typeof(OperatorCommandKind)).Length, Is.EqualTo(8), "None, exactly six, and the Map page's walk");

            OperatorCommand none;
            Assert.That(OperatorCommand.TryFrom(new OperatorCmdMessage { Cmd = "panic" }, out none), Is.False);
            Assert.That(OperatorCommand.TryFrom(new SceneCmdMessage { Space = "S7" }, out none), Is.False);
            Assert.That(OperatorCommand.TryFrom(new SceneCmdMessage { Space = "home" }, out none), Is.False);
            Assert.That(OperatorCommand.Quality(9).Tier, Is.EqualTo(4));
            Assert.That(OperatorCommand.Quality(-5).Tier, Is.EqualTo(-1));
            Assert.That(OperatorCommand.Stop().ToEvent(3, "S0").ToJsonLine(),
                Is.EqualTo("{\"t_us\":3,\"scene\":\"S0\",\"ev\":\"cmd\",\"data\":{\"cmd\":\"cue\"}}"));
            Assert.That(default(OperatorCommand).ToMessage(), Is.Null);
        }

        [Test]
        public void The_walk_command_round_trips_and_is_clamped()
        {
            OperatorCommand walk = OperatorCommand.Walk(-1f, 0.5f, -90f, 1.5f);
            JsonMessage message = walk.ToMessage();
            Assert.That(message.Id, Is.EqualTo(MessageId.OperatorCmd));
            Assert.That(message.ToJson().ToJson(),
                Is.EqualTo("{\"cmd\":\"walk\",\"args\":{\"x\":-1.0,\"y\":0.5,\"turn\":-90.0,\"speed\":1.5}}"));

            byte[] packet = message.ToBytes();
            var op = new OperatorCmdMessage();
            Assert.That(op.ReadFrom(packet, 0, packet.Length), Is.True);
            OperatorCommand back;
            Assert.That(OperatorCommand.TryFrom(op, out back), Is.True);
            Assert.That(back.Kind, Is.EqualTo(OperatorCommandKind.Walk));
            Assert.That(new[] { back.MoveX, back.MoveY, back.Turn, back.Speed }, Is.EqualTo(new[] { -1f, 0.5f, -90f, 1.5f }));
            Assert.That(back.Name, Is.EqualTo("walk"));
            Assert.That(back.IsStill, Is.False);

            // Out of range, missing or not a number: clamped, defaulted, stopped.
            OperatorCommand wild = OperatorCommand.Walk(5f, float.NaN, 1000f, 99f);
            Assert.That(new[] { wild.MoveX, wild.MoveY, wild.Turn, wild.Speed },
                Is.EqualTo(new[] { 1f, 0f, OperatorCommand.WalkMaxTurn, OperatorCommand.WalkMaxSpeed }));
            Assert.That(OperatorCommand.TryFrom(new OperatorCmdMessage { Cmd = "walk" }, out back), Is.True);
            Assert.That(back.IsStill, Is.True);
            Assert.That(back.Speed, Is.EqualTo(OperatorCommand.WalkDefaultSpeed));
        }

        [Test]
        public void Roles_have_fixed_names()
        {
            Assert.That(LinkRoles.Name(LinkRole.Phone), Is.EqualTo("phone"));
            Assert.That(LinkRoles.Name(LinkRole.Spectator), Is.EqualTo("spectator"));
            Assert.That(LinkRoles.Name(LinkRole.Editor), Is.EqualTo("editor"));
            Assert.That(LinkRoles.Name(LinkRole.Console), Is.EqualTo("console"));
            Assert.That(LinkRoles.Parse("console"), Is.EqualTo(LinkRole.Console));
            Assert.That(LinkRoles.Parse("toaster"), Is.EqualTo(LinkRole.Unknown));
            Assert.That(LinkRoles.Watches(LinkRole.Spectator) && LinkRoles.Watches(LinkRole.Editor));
            Assert.That(LinkRoles.Watches(LinkRole.Phone) || LinkRoles.Watches(LinkRole.Console), Is.False);
            Assert.That(LinkRoles.Operates(LinkRole.Console) && LinkRoles.Operates(LinkRole.Editor));
        }
    }
}
