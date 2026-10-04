using System.IO;
using System.Text;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>The Hub's small parts on their own: options, the state.bin layout, the CSV row, the QR code.</summary>
    [TestFixture]
    public class UnitTests
    {
        [Test]
        public void Options_Defaults_AreThePlansPorts()
        {
            HubOptions o;
            string error;
            Assert.That(HubOptions.TryParse(new string[0], out o, out error), Is.True);
            Assert.That(o.UdpPort, Is.EqualTo(9050));
            Assert.That(o.HttpPort, Is.EqualTo(8080));
            Assert.That(o.BindAddress, Is.EqualTo("0.0.0.0"));
            Assert.That(o.ProbeForge, Is.True);

            // Folders left open resolve to the Hub's own project folder when it runs from the repository, and to
            // the current directory otherwise (as here, where the running binary is the test assembly).
            HubPaths.Resolve(o);
            string home = HubPaths.ProjectFolder() ?? System.IO.Directory.GetCurrentDirectory();
            Assert.That(o.LogRoot, Is.EqualTo(System.IO.Path.Combine(home, "logs")));
            Assert.That(o.PresetFolder, Is.EqualTo(System.IO.Path.Combine(home, "presets")));
            Assert.That(System.IO.Path.GetFileName(o.ProjectFolder), Is.EqualTo("ATLAS"));
        }

        [Test]
        public void Options_Parse_AndRejectNonsense()
        {
            HubOptions o;
            string error;
            string[] args =
            {
                "--udp", "9150", "--http", "8181", "--project", "/data/ATLAS", "--bind", "127.0.0.1", "--logs", "/data/logs", "--session", "demo-1",
                "--advertise", "192.168.1.10", "--forge", "http://127.0.0.1:8001/", "--no-forge", "--demo-lock", "--quiet"
            };
            Assert.That(HubOptions.TryParse(args, out o, out error), Is.True, error);
            Assert.That(o.UdpPort, Is.EqualTo(9150));
            Assert.That(o.HttpPort, Is.EqualTo(8181));
            Assert.That(o.BindAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(o.LogRoot, Is.EqualTo("/data/logs"));
            Assert.That(o.SessionId, Is.EqualTo("demo-1"));
            Assert.That(o.AdvertiseAddress, Is.EqualTo("192.168.1.10"));
            Assert.That(o.ForgeUrl, Is.EqualTo("http://127.0.0.1:8001"));
            Assert.That(o.ProbeForge, Is.False);
            Assert.That(o.DemoLock, Is.True);
            Assert.That(o.Quiet, Is.True);

            Assert.That(o.ProjectFolder, Is.EqualTo("/data/ATLAS"));
            Assert.That(HubOptions.TryParse(new[] { "--udp-port", "9151", "--http-port", "8182" }, out o, out error), Is.True, "the long spellings");
            Assert.That(o.UdpPort, Is.EqualTo(9151));
            Assert.That(o.HttpPort, Is.EqualTo(8182));
            Assert.That(HubOptions.TryParse(new[] { "--udp-port", "70000" }, out o, out error), Is.False);
            Assert.That(HubOptions.TryParse(new[] { "--bind", "everywhere" }, out o, out error), Is.False);
            Assert.That(HubOptions.TryParse(new[] { "--session", "../x" }, out o, out error), Is.False);
            Assert.That(HubOptions.TryParse(new[] { "--logs" }, out o, out error), Is.False);
            Assert.That(HubOptions.TryParse(new[] { "--frobnicate", "1" }, out o, out error), Is.False);
            Assert.That(error, Does.Contain("--frobnicate"));
            Assert.That(HubOptions.TryParse(new[] { "--help" }, out o, out error), Is.True);
            Assert.That(o.ShowHelp, Is.True);
        }

        [Test]
        public void SafeNames_AreNeverPaths()
        {
            Assert.That(Names.IsSafe("20261004-101500"), Is.True);
            Assert.That(Names.IsSafe("demo_2.final"), Is.True);
            Assert.That(Names.IsSafe(""), Is.False);
            Assert.That(Names.IsSafe(".."), Is.False);
            Assert.That(Names.IsSafe(".hidden"), Is.False);
            Assert.That(Names.IsSafe("a/b"), Is.False);
            Assert.That(Names.IsSafe("a\\b"), Is.False);
            Assert.That(Names.IsSafe(new string('a', 65)), Is.False);
            Assert.That(Names.IsPreset("demo_2-final"), Is.True);
            Assert.That(Names.IsPreset("demo.2"), Is.False);
            Assert.That(Names.IsPreset(new string('a', 41)), Is.False);
        }

        [Test]
        public void StateLog_RoundTrips_AndIgnoresATornLastRecord()
        {
            var stream = new MemoryStream();
            StateLog.WriteHeader(stream);
            var record = new byte[StateLog.RecordSize];
            var raw = new byte[StateMessage.Size + 3];
            for (int i = 0; i < 5; i++)
            {
                StateMessage s = StateMessage.Identity;
                s.Seq = (uint)(100 + i);
                s.TimeUs = 5000 + i;
                s.HeadPosX = 0.25f * i;
                s.SetTile(5, long.MinValue + i);
                s.SetXform(2, 1.0 / 3.0 + i);
                s.WorldVer = 7;
                s.WriteTo(raw, 3);
                StateLog.PackRecord(record, 9000000000L + i, raw, 3);
                stream.Write(record, 0, record.Length);
            }
            stream.Write(record, 0, 100);   // the Hub was killed in mid-write

            Assert.That(stream.Length, Is.EqualTo(StateLog.HeaderSize + 5 * 210 + 100));
            stream.Position = 0;
            string error;
            Assert.That(StateLog.TryReadHeader(stream, out error), Is.True, error);
            StateLog.Entry e;
            for (int i = 0; i < 5; i++)
            {
                Assert.That(StateLog.TryReadRecord(stream, record, out e), Is.True);
                Assert.That(e.HubUs, Is.EqualTo(9000000000L + i));
                Assert.That(e.State.Seq, Is.EqualTo((uint)(100 + i)));
                Assert.That(e.State.HeadPosX, Is.EqualTo(0.25f * i));
                Assert.That(e.State.GetTile(5), Is.EqualTo(long.MinValue + i));
                Assert.That(e.State.GetXform(2), Is.EqualTo(1.0 / 3.0 + i));
                Assert.That(e.State.WorldVer, Is.EqualTo(7));
            }
            Assert.That(StateLog.TryReadRecord(stream, record, out e), Is.False);

            Assert.That(StateLog.TryReadHeader(new MemoryStream(Encoding.ASCII.GetBytes("NOTASTATEFILE...")), out error), Is.False);
            Assert.That(error, Does.Contain("magic"));
            Assert.That(StateLog.TryReadHeader(new MemoryStream(new byte[4]), out error), Is.False);
        }

        [Test]
        public void ForgeProbe_ReadsTheForgesHealthBody()
        {
            // The body the Forge's GET /health really sends (station/forge/forge/models.py, Health).
            ForgeStatus armed = ForgeProbe.Parse(
                "{\"ok\":true,\"gpu\":false,\"gpuName\":null,\"queue\":2,\"pipelineVer\":\"p1\",\"debugKeep\":true," +
                "\"generator\":\"procedural\",\"generatorReady\":true,\"generatorNote\":\"\",\"hosted\":true,\"hostedEnabled\":true,\"matting\":\"none\"}");
            Assert.That(armed.Up, Is.True);
            Assert.That(armed.Queue, Is.EqualTo(2));
            Assert.That(armed.DebugKeep, Is.True);
            Assert.That(armed.Hosted, Is.True, "the hosted fallback is disclosed");
            Assert.That(armed.PipelineVer, Is.EqualTo("p1"));
            Assert.That(armed.ToJson().GetBool("hosted"), Is.True, "and reaches /api/session and the link frame");

            // A Forge that only knows the older name is understood too.
            Assert.That(ForgeProbe.Parse("{\"ok\":true,\"queue\":0,\"hostedEnabled\":true}").Hosted, Is.True);
            Assert.That(ForgeProbe.Parse("{\"ok\":true,\"queue\":0,\"hostedEnabled\":false}").Hosted, Is.False);
            Assert.That(ForgeProbe.Parse("not json").Up, Is.False);
            Assert.That(ForgeProbe.Parse(null).Up, Is.False);
        }

        [Test]
        public void TelemetryCsv_HasOneValuePerHeaderColumn_InWireOrder()
        {
            var t = new TelemetryMessage
            {
                TimeUs = 123456789, Seq = 42, SceneId = 3, SpaceId = 2, Tier = 1, Fps = 59.94f, P50 = 16.6f, P95 = 17.25f, P99 = 20f, Max = 33.3333f,
                Over17_2 = 0.5f, Over33_8 = 0f, CpuMainP95 = 6.125f, CpuRenderP95 = 3f, GpuP95 = 8.5f, RenderScale = 0.85f,
                DrawCalls = 40, SetPass = 6, TrisK = 180, MemMB = 640, VisibleTiles = 61, Copies = 5, RttMs = 4, LossPermille = 12, CueCount = 2,
                GcAllocB = 4000000000u, Thermal = 1, Battery = 87, Tracking = 2, Msaa = 4, StateHz = 60, BumpMax = 1, AssetsPlaced = 3, AssetsPending = 2
            };
            var sb = new StringBuilder();
            TelemetryCsv.AppendRow(sb, t);
            Assert.That(sb.ToString(), Is.EqualTo(
                "123456789,42,3,2,1,59.940,16.600,17.250,20.000,33.333,0.500,0.000,6.125,3.000,8.500,0.850,40,6,180,640,61,5,4,12,2,4000000000,1,87,2,4,60,1,3,2\n"));

            // Three decimals, half away from zero, from the float's value as a double; never the runtime's rounding.
            foreach (var pair in new[] { (60f, "60.000"), (16.6665f, "16.667"), (0.0005f, "0.001"), (0.0004f, "0.000"), (-0.0004f, "0.000"),
                                         (-1.2346f, "-1.235"), (999.9996f, "1000.000"), (float.NaN, "0.000"), (0.8f, "0.800") })
            {
                var one = new StringBuilder();
                TelemetryCsv.AppendFixed3(one, pair.Item1);
                Assert.That(one.ToString(), Is.EqualTo(pair.Item2), pair.Item1.ToString("R"));
            }

            string[] header = TelemetryMessage.CsvHeader.Split(',');
            Assert.That(sb.ToString().Split(',').Length, Is.EqualTo(header.Length));
            JsonValue json = TelemetryJson.ToJson(t);
            Assert.That(json.Count, Is.EqualTo(header.Length));
            for (int i = 0; i < header.Length; i++) Assert.That(json.KeyAt(i), Is.EqualTo(header[i]));
            Assert.That(json.GetLong("gcAllocB"), Is.EqualTo(4000000000L));
        }

        [Test]
        public void PairPage_DrawsAValidQrSymbol()
        {
            bool[,] m = PairPage.Modules("atlas://192.168.1.10:9050");
            int n = m.GetLength(0);
            int symbol = n - 8;   // four quiet modules on each side
            Assert.That(m.GetLength(1), Is.EqualTo(n));
            Assert.That(symbol, Is.GreaterThanOrEqualTo(21));
            Assert.That((symbol - 17) % 4, Is.EqualTo(0), "a QR symbol is 17 + 4 * version modules wide");

            // Quiet zone, then the three finder patterns: a dark 7x7 ring round a light ring round a dark 3x3.
            for (int i = 0; i < n; i++)
                for (int q = 0; q < 4; q++)
                {
                    Assert.That(m[q, i], Is.False);
                    Assert.That(m[i, q], Is.False);
                    Assert.That(m[n - 1 - q, i], Is.False);
                    Assert.That(m[i, n - 1 - q], Is.False);
                }
            foreach (int[] origin in new[] { new[] { 4, 4 }, new[] { 4, n - 11 }, new[] { n - 11, 4 } })
            {
                for (int y = 0; y < 7; y++)
                    for (int x = 0; x < 7; x++)
                    {
                        int ring = System.Math.Max(System.Math.Abs(x - 3), System.Math.Abs(y - 3));
                        Assert.That(m[origin[0] + y, origin[1] + x], Is.EqualTo(ring != 2), "finder at " + origin[0] + "," + origin[1]);
                    }
            }
            // The timing pattern alternates along row 6 of the symbol.
            for (int x = 8; x < symbol - 8; x++) Assert.That(m[4 + 6, 4 + x], Is.EqualTo(x % 2 == 0));

            string svg = PairPage.Svg("atlas://192.168.1.10:9050");
            Assert.That(svg, Does.StartWith("<svg"));
            Assert.That(svg, Does.Contain("viewBox=\"0 0 " + n + " " + n + "\""));
            Assert.That(svg, Does.EndWith("</svg>"));

            string html = PairPage.Html("atlas://192.168.1.10:9050", "192.168.1.10", 9050, 8080, null);
            Assert.That(html, Does.Contain("atlas://192.168.1.10:9050"));
            // The router has no internet: the page must not load anything from outside the Station.
            Assert.That(html, Does.Not.Contain("src="));
            Assert.That(html, Does.Not.Contain("href=\"http"));
        }

        [Test]
        public void StateNames_AreThePlansWatchdogStates()
        {
            Assert.That(HubServer.StateName(LinkState.Connected), Is.EqualTo("connected"));
            Assert.That(HubServer.StateName(LinkState.Degraded), Is.EqualTo("degraded"));
            Assert.That(HubServer.StateName(LinkState.Offline), Is.EqualTo("offline"));
            Assert.That(HubServer.StateName(LinkState.Resync), Is.EqualTo("resync"));
        }
    }
}
