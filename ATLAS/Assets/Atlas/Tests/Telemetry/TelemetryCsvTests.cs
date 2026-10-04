using System;
using System.IO;
using System.Text;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>The perf CSV: header, column order and number format, byte for byte.</summary>
    public class TelemetryCsvTests
    {
        const string Header =
            "t_us,seq,sceneId,spaceId,tier,fps,p50,p95,p99,max,over17_2,over33_8,cpuMainP95,cpuRenderP95,gpuP95,renderScale," +
            "drawCalls,setPass,trisK,memMB,visibleTiles,copies,rttMs,lossPermille,cueCount,gcAllocB,thermal,battery,tracking," +
            "msaa,stateHz,bumpMax,assetsPlaced,assetsPending";

        static TelemetryMessage Example()
        {
            return new TelemetryMessage
            {
                TimeUs = 1234567890, Seq = 42, SceneId = 1, SpaceId = 1, Tier = 0,
                Fps = 60f, P50 = 16.667f, P95 = 16.9f, P99 = 17.4f, Max = 18.25f, Over17_2 = 0.5f, Over33_8 = 0f,
                CpuMainP95 = 4.1f, CpuRenderP95 = 2.9f, GpuP95 = 4.6f, RenderScale = 0.8f,
                DrawCalls = 142, SetPass = 22, TrisK = 231, MemMB = 612, VisibleTiles = 0, Copies = 25,
                RttMs = 3, LossPermille = 0, CueCount = 1, GcAllocB = 0,
                Thermal = 0, Battery = 87, Tracking = 0, Msaa = 4, StateHz = 60, BumpMax = 1, AssetsPlaced = 3, AssetsPending = 1
            };
        }

        [Test]
        public void Header_has_the_34_columns_in_wire_order()
        {
            Assert.That(TelemetryCsv.Header, Is.EqualTo(Header));
            Assert.That(TelemetryCsv.Header.Split(',').Length, Is.EqualTo(TelemetryCsv.ColumnCount));
        }

        [Test]
        public void Example_row_is_written_byte_for_byte()
        {
            var sb = new StringBuilder();
            TelemetryMessage row = Example();
            TelemetryCsv.AppendRow(sb, in row);
            Assert.That(sb.ToString(), Is.EqualTo(
                "1234567890,42,1,1,0,60.000,16.667,16.900,17.400,18.250,0.500,0.000,4.100,2.900,4.600,0.800," +
                "142,22,231,612,0,25,3,0,1,0,0,87,0,4,60,1,3,1\n"));
        }

        [Test]
        public void Every_column_lands_under_its_own_name()
        {
            // Each field gets a value no other field has, so a swapped column cannot hide.
            var row = new TelemetryMessage
            {
                TimeUs = 101, Seq = 102, SceneId = 103, SpaceId = 104, Tier = 105,
                Fps = 106, P50 = 107, P95 = 108, P99 = 109, Max = 110, Over17_2 = 111, Over33_8 = 112,
                CpuMainP95 = 113, CpuRenderP95 = 114, GpuP95 = 115, RenderScale = 116,
                DrawCalls = 117, SetPass = 118, TrisK = 119, MemMB = 120, VisibleTiles = 121, Copies = 122,
                RttMs = 123, LossPermille = 124, CueCount = 125, GcAllocB = 126,
                Thermal = 127, Battery = 128, Tracking = 129, Msaa = 130, StateHz = 131, BumpMax = 132, AssetsPlaced = 133, AssetsPending = 134
            };
            var sb = new StringBuilder();
            TelemetryCsv.AppendRow(sb, in row);
            string line = sb.ToString();
            Assert.That(line.EndsWith("\n"), Is.True);
            string[] cells = line.TrimEnd('\n').Split(',');
            string[] names = Header.Split(',');
            Assert.That(cells.Length, Is.EqualTo(names.Length));
            for (int i = 0; i < cells.Length; i++)
            {
                bool isFloat = i >= 5 && i <= 15;
                Assert.That(cells[i], Is.EqualTo((101 + i) + (isFloat ? ".000" : "")), names[i]);
            }
            Assert.That(names[26], Is.EqualTo("thermal"));
        }

        [Test]
        public void Floats_round_half_away_from_zero_to_three_decimals()
        {
            Assert.That(Fixed(0f), Is.EqualTo("0.000"));
            Assert.That(Fixed(0.0005f), Is.EqualTo("0.001"));
            Assert.That(Fixed(0.0004f), Is.EqualTo("0.000"));
            Assert.That(Fixed(-0.0004f), Is.EqualTo("0.000"), "no negative zero");
            Assert.That(Fixed(-1.5f), Is.EqualTo("-1.500"));
            Assert.That(Fixed(999.9996f), Is.EqualTo("1000.000"));
            Assert.That(Fixed(float.NaN), Is.EqualTo("0.000"));
            Assert.That(Fixed(float.PositiveInfinity), Is.EqualTo("0.000"));
            Assert.That(Fixed(0.125f), Is.EqualTo("0.125"));
        }

        [Test]
        public void Integers_are_plain_decimal()
        {
            var sb = new StringBuilder();
            TelemetryCsv.AppendInt(sb, 0); sb.Append(' ');
            TelemetryCsv.AppendInt(sb, -7); sb.Append(' ');
            TelemetryCsv.AppendInt(sb, 1000); sb.Append(' ');
            TelemetryCsv.AppendInt(sb, uint.MaxValue); sb.Append(' ');
            TelemetryCsv.AppendInt(sb, long.MaxValue); sb.Append(' ');
            TelemetryCsv.AppendInt(sb, long.MinValue);
            Assert.That(sb.ToString(), Is.EqualTo("0 -7 1000 4294967295 9223372036854775807 -9223372036854775808"));
        }

        static string Fixed(float v)
        {
            var sb = new StringBuilder();
            TelemetryCsv.AppendFixed3(sb, v);
            return sb.ToString();
        }

        [Test]
        public void Writer_puts_the_header_first_and_appends_rows_without_a_byte_order_mark()
        {
            string dir = Path.Combine(Path.GetTempPath(), "atlas-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var files = new LogFileWriter(dir, false))
                {
                    files.Open("20261004T153012Z-a3f9");
                    var sb = new StringBuilder();
                    TelemetryMessage row = Example();
                    for (int i = 0; i < 3; i++)
                    {
                        row.Seq = (uint)i;
                        sb.Length = 0;
                        TelemetryCsv.AppendRow(sb, in row);
                        files.Append(LogFileWriter.Perf, sb);
                        if (i == 0) files.Flush();
                    }
                    files.Append(LogFileWriter.Events, "{\"ev\":\"x\"}\n");
                    files.Flush();
                    Assert.That(files.Errors, Is.EqualTo(0));

                    string path = files.PathOf(LogFileWriter.Perf);
                    Assert.That(path, Is.EqualTo(Path.Combine(dir, "perf_20261004T153012Z-a3f9.csv")));
                    Assert.That(Path.GetFileName(files.PathOf(LogFileWriter.Events)), Is.EqualTo("events_20261004T153012Z-a3f9.jsonl"));
                    Assert.That(Path.GetFileName(files.PathOf(LogFileWriter.Phone)), Is.EqualTo("phone_20261004T153012Z-a3f9.log"));
                }
                byte[] bytes = File.ReadAllBytes(Path.Combine(dir, "perf_20261004T153012Z-a3f9.csv"));
                Assert.That(bytes[0], Is.EqualTo((byte)'t'), "no byte-order mark");
                string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');
                Assert.That(lines.Length, Is.EqualTo(5), "header, three rows, and the empty tail after the last line end");
                Assert.That(lines[0], Is.EqualTo(Header));
                Assert.That(lines[1].StartsWith("1234567890,0,"), Is.True);
                Assert.That(lines[3].StartsWith("1234567890,2,"), Is.True);
                Assert.That(lines[4], Is.EqualTo(string.Empty));
                Assert.That(File.ReadAllText(Path.Combine(dir, "events_20261004T153012Z-a3f9.jsonl")), Is.EqualTo("{\"ev\":\"x\"}\n"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Background_writer_delivers_on_dispose_and_a_new_session_gets_new_files()
        {
            string dir = Path.Combine(Path.GetTempPath(), "atlas-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                var files = new LogFileWriter(dir);
                files.Open("a");
                files.Append(LogFileWriter.Phone, "1 warn first\n");
                files.Open("b");
                files.Append(LogFileWriter.Phone, "2 warn second\n");
                files.Dispose();
                Assert.That(File.ReadAllText(Path.Combine(dir, "phone_a.log")), Is.EqualTo("1 warn first\n"));
                Assert.That(File.ReadAllText(Path.Combine(dir, "phone_b.log")), Is.EqualTo("2 warn second\n"));
                Assert.That(files.DeleteSession("a"), Is.EqualTo(1));
                Assert.That(File.Exists(Path.Combine(dir, "phone_a.log")), Is.False);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Session_id_has_the_documented_shape()
        {
            Assert.That(SessionId.Make(new DateTime(2026, 10, 4, 15, 30, 12, DateTimeKind.Utc), 0xa3f9), Is.EqualTo("20261004T153012Z-a3f9"));
            Assert.That(SessionId.New().Length, Is.EqualTo(21));
        }
    }

    /// <summary>The HUD text.</summary>
    public class HudTextTests
    {
        [Test]
        public void Five_lines_from_plain_values()
        {
            var row = new TelemetryMessage
            {
                Fps = 59.6f, P95 = 16.94f, Tier = 1, Thermal = 2, CpuMainP95 = 4.1f, CpuRenderP95 = 2.9f, GpuP95 = 4.6f,
                DrawCalls = 142, SetPass = 22, TrisK = 231, MemMB = 612, Battery = 87, GcAllocB = 0,
                SpaceId = 1, RttMs = 3, LossPermille = 12, AssetsPlaced = 3, AssetsPending = 1
            };
            var sb = new StringBuilder();
            HudText.Build(sb, in row, LinkState.Connected, 2);
            Assert.That(sb.ToString(), Is.EqualTo(
                "60 fps  p95 16.9  tier 1  therm 2\n" +
                "cpu 4.1 / 2.9  gpu 4.6 ms\n" +
                "draws 142  sp 22  tris 231k\n" +
                "mem 612 MB  bat 87  gc 0\n" +
                "S1  link 3 ms 1.2%  bump 2  assets 3/1"));

            HudText.Build(sb, in row, LinkState.Offline, 0, true);
            string[] lines = sb.ToString().Split('\n');
            Assert.That(lines.Length, Is.EqualTo(6));
            Assert.That(lines[4], Is.EqualTo("S1  link offline  bump 0  assets 3/1"));
            Assert.That(lines[5], Is.EqualTo("Session over"));
        }

        [Test]
        public void Building_the_text_allocates_nothing()
        {
            var row = new TelemetryMessage { Fps = 60f, P95 = 16.9f, MemMB = 612, Battery = 255 };
            var sb = new StringBuilder(512);
            HudText.Build(sb, in row, LinkState.Degraded, 3, true);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) HudText.Build(sb, in row, LinkState.Degraded, i & 3, true);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }
    }
}
