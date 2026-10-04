using System;
using System.Text;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// Telemetry_Csv (lane D contract, section 5.3): the header and the example row byte for byte, the rounding
    /// rule, and a row that costs no allocation. The phone and the Hub both write their perf CSV through
    /// <see cref="TelemetryCsv"/>, so what holds here holds for both files.
    /// </summary>
    public sealed class Telemetry_Csv
    {
        const string ContractHeader =
            "t_us,seq,sceneId,spaceId,tier,fps,p50,p95,p99,max,over17_2,over33_8,cpuMainP95,cpuRenderP95,gpuP95,renderScale," +
            "drawCalls,setPass,trisK,memMB,visibleTiles,copies,rttMs,lossPermille,cueCount,gcAllocB,thermal,battery,tracking," +
            "msaa,stateHz,bumpMax,assetsPlaced,assetsPending";

        const string ContractRow =
            "1234567890,42,1,1,0,60.000,16.667,16.900,17.400,18.250,0.500,0.000,4.100,2.900,4.600,0.800,142,22,231,612,0,25,3,0,1,0,0,87,0,4,60,1,3,1\n";

        static TelemetryMessage ExampleRow()
        {
            return new TelemetryMessage
            {
                TimeUs = 1234567890, Seq = 42, SceneId = 1, SpaceId = 1, Tier = 0,
                Fps = 60f, P50 = 16.667f, P95 = 16.9f, P99 = 17.4f, Max = 18.25f, Over17_2 = 0.5f, Over33_8 = 0f,
                CpuMainP95 = 4.1f, CpuRenderP95 = 2.9f, GpuP95 = 4.6f, RenderScale = 0.8f,
                DrawCalls = 142, SetPass = 22, TrisK = 231, MemMB = 612, VisibleTiles = 0, Copies = 25, RttMs = 3, LossPermille = 0, CueCount = 1,
                GcAllocB = 0, Thermal = 0, Battery = 87, Tracking = 0, Msaa = 4, StateHz = 60, BumpMax = 1, AssetsPlaced = 3, AssetsPending = 1
            };
        }

        static string Fixed3(float value)
        {
            var sb = new StringBuilder();
            TelemetryCsv.AppendFixed3(sb, value);
            return sb.ToString();
        }

        [Test]
        public void Header_is_the_line_of_the_contract()
        {
            Assert.That(TelemetryCsv.Header, Is.EqualTo(ContractHeader));
            Assert.That(TelemetryCsv.Header, Is.EqualTo(TelemetryMessage.CsvHeader), "one header for the phone, the Hub and the Console");
            Assert.That(TelemetryCsv.Header.Split(',').Length, Is.EqualTo(TelemetryCsv.ColumnCount));
            Assert.That(TelemetryCsv.ColumnCount, Is.EqualTo(34));

            var sb = new StringBuilder();
            TelemetryCsv.AppendHeader(sb);
            Assert.That(sb.ToString(), Is.EqualTo(ContractHeader + "\n"), "\\n line ends, no byte-order mark, no trailing comma");
        }

        [Test]
        public void Example_row_matches_byte_for_byte()
        {
            var sb = new StringBuilder();
            TelemetryCsv.AppendRow(sb, ExampleRow());
            Assert.That(sb.ToString(), Is.EqualTo(ContractRow));
            Assert.That(Encoding.UTF8.GetBytes(sb.ToString()), Is.EqualTo(Encoding.ASCII.GetBytes(ContractRow)));
            Assert.That(sb.ToString().Split(',').Length, Is.EqualTo(TelemetryCsv.ColumnCount));

            // Appending is appending: a second row follows the first.
            TelemetryMessage next = ExampleRow();
            next.Seq = 43;
            TelemetryCsv.AppendRow(sb, next);
            Assert.That(sb.ToString(), Is.EqualTo(ContractRow + ContractRow.Replace(",42,", ",43,")));
        }

        [Test]
        public void A_row_that_crossed_the_wire_writes_the_same_line()
        {
            // The Hub writes what it decoded; the phone writes what it measured. Same 91 bytes, same line.
            var wire = new byte[TelemetryMessage.Size];
            ExampleRow().WriteTo(wire, 0);
            var received = new TelemetryMessage();
            Assert.That(received.ReadFrom(wire, 0, wire.Length), Is.True);
            var sb = new StringBuilder();
            TelemetryCsv.AppendRow(sb, received);
            Assert.That(sb.ToString(), Is.EqualTo(ContractRow));
        }

        [Test]
        public void Floats_have_three_decimals_rounded_half_away_from_zero()
        {
            Assert.That(Fixed3(60f), Is.EqualTo("60.000"));
            Assert.That(Fixed3(16.667f), Is.EqualTo("16.667"));
            Assert.That(Fixed3(0.8f), Is.EqualTo("0.800"));
            Assert.That(Fixed3(0f), Is.EqualTo("0.000"));
            Assert.That(Fixed3(16.6665f), Is.EqualTo("16.667"), "16.666500091... as a double");
            Assert.That(Fixed3(0.0005f), Is.EqualTo("0.001"), "0.000500000023... as a double");
            Assert.That(Fixed3(0.0004f), Is.EqualTo("0.000"));
            Assert.That(Fixed3(2.5f), Is.EqualTo("2.500"));
            Assert.That(Fixed3(0.125f), Is.EqualTo("0.125"));
            Assert.That(Fixed3(0.0625f), Is.EqualTo("0.063"), "an exact half goes away from zero, not to even");
            Assert.That(Fixed3(-0.0625f), Is.EqualTo("-0.063"));
            Assert.That(Fixed3(-1.2346f), Is.EqualTo("-1.235"));
            Assert.That(Fixed3(-0.0004f), Is.EqualTo("0.000"), "a value that rounds to zero carries no sign");
            Assert.That(Fixed3(999.9996f), Is.EqualTo("1000.000"), "the carry runs into the integer part");
            Assert.That(Fixed3(123456.79f), Is.EqualTo("123456.789"));
            Assert.That(Fixed3(float.NaN), Is.EqualTo("0.000"));
            Assert.That(Fixed3(float.PositiveInfinity), Is.EqualTo("0.000"));
            Assert.That(Fixed3(float.NegativeInfinity), Is.EqualTo("0.000"));
            Assert.That(Fixed3(float.MaxValue), Is.EqualTo("9000000000000.000"), "a nonsense value stays a readable number");
            Assert.That(Fixed3(float.MinValue), Is.EqualTo("-9000000000000.000"));
        }

        [Test]
        public void Integers_are_plain_decimal()
        {
            var sb = new StringBuilder();
            TelemetryCsv.AppendInt(sb, 0);
            sb.Append(' ');
            TelemetryCsv.AppendInt(sb, 4294967295L);
            sb.Append(' ');
            TelemetryCsv.AppendInt(sb, -17);
            sb.Append(' ');
            TelemetryCsv.AppendInt(sb, long.MaxValue);
            sb.Append(' ');
            TelemetryCsv.AppendInt(sb, long.MinValue);
            Assert.That(sb.ToString(), Is.EqualTo("0 4294967295 -17 9223372036854775807 -9223372036854775808"));
        }

        [Test]
        public void The_longest_row_fits_MaxRowLength()
        {
            var worst = new TelemetryMessage
            {
                TimeUs = long.MinValue, Seq = uint.MaxValue, SceneId = ushort.MaxValue, SpaceId = 255, Tier = 255,
                Fps = float.MinValue, P50 = float.MinValue, P95 = float.MinValue, P99 = float.MinValue, Max = float.MinValue,
                Over17_2 = float.MinValue, Over33_8 = float.MinValue, CpuMainP95 = float.MinValue, CpuRenderP95 = float.MinValue,
                GpuP95 = float.MinValue, RenderScale = float.MinValue,
                DrawCalls = ushort.MaxValue, SetPass = ushort.MaxValue, TrisK = ushort.MaxValue, MemMB = ushort.MaxValue,
                VisibleTiles = ushort.MaxValue, Copies = ushort.MaxValue, RttMs = ushort.MaxValue, LossPermille = ushort.MaxValue,
                CueCount = ushort.MaxValue, GcAllocB = uint.MaxValue,
                Thermal = 255, Battery = 255, Tracking = 255, Msaa = 255, StateHz = 255, BumpMax = 255, AssetsPlaced = 255, AssetsPending = 255
            };
            var sb = new StringBuilder();
            TelemetryCsv.AppendRow(sb, worst);
            Assert.That(sb.Length, Is.LessThanOrEqualTo(TelemetryCsv.MaxRowLength));
            Assert.That(sb.ToString().Split(',').Length, Is.EqualTo(TelemetryCsv.ColumnCount));
            Assert.That(sb.ToString(), Does.StartWith("-9223372036854775808,4294967295,65535,255,255,-9000000000000.000,"));
            Assert.That(sb.ToString(), Does.EndWith(",255,255\n"));
        }

        [Test]
        public void Appending_a_row_allocates_nothing()
        {
            var sb = new StringBuilder(TelemetryCsv.MaxRowLength);
            TelemetryMessage row = ExampleRow();
            for (int i = 0; i < 16; i++)
            {
                sb.Length = 0;
                TelemetryCsv.AppendRow(sb, row);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                row.Seq = (uint)i;
                row.Fps = 59f + i * 0.001f;
                sb.Length = 0;
                TelemetryCsv.AppendRow(sb, row);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0L), "one row per second for the whole session, and never a byte of garbage");
            Assert.That(sb.ToString(), Does.StartWith("1234567890,999,1,1,0,59.999,"));
        }
    }
}
