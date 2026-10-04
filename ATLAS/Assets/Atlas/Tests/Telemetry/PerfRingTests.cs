using System;
using System.Text;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>The statistics core of the perf probe against sequences whose answers are known by hand.</summary>
    public class PerfRingTests
    {
        [Test]
        public void Empty_ring_reads_zero()
        {
            var ring = new PerfRing(8);
            Assert.That(ring.Count, Is.EqualTo(0));
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 0.95f), Is.EqualTo(0f));
            Assert.That(ring.ShareOver(17.2f), Is.EqualTo(0f));
            Assert.That(ring.Mean(PerfRing.ChannelGpu), Is.EqualTo(0f));
        }

        [Test]
        public void Nearest_rank_percentiles_of_one_to_hundred()
        {
            var ring = new PerfRing(100);
            // Added out of order: the ring sorts a copy.
            for (int i = 0; i < 100; i++) ring.Add((i * 37) % 100 + 1, 0f, 0f, 0f);
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 0.50f), Is.EqualTo(50f));
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 0.95f), Is.EqualTo(95f));
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 0.99f), Is.EqualTo(99f));
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 1.00f), Is.EqualTo(100f));
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 0.00f), Is.EqualTo(1f));
            Assert.That(ring.Mean(PerfRing.ChannelFrame), Is.EqualTo(50.5f).Within(1e-4f));
            Assert.That(ring.Max(PerfRing.ChannelFrame), Is.EqualTo(100f));
        }

        [Test]
        public void Nearest_rank_on_a_short_sequence()
        {
            var ring = new PerfRing(16);
            float[] ms = { 15f, 20f, 35f, 40f, 50f };
            for (int i = 0; i < ms.Length; i++) ring.Add(ms[i], 0f, 0f, 0f);
            // The textbook example: ranks ceil(0.3*5)=2, ceil(0.4*5)=2, ceil(0.5*5)=3, ceil(1.0*5)=5.
            Assert.That(ring.Percentile(0, 0.30f), Is.EqualTo(20f));
            Assert.That(ring.Percentile(0, 0.40f), Is.EqualTo(20f));
            Assert.That(ring.Percentile(0, 0.50f), Is.EqualTo(35f));
            Assert.That(ring.Percentile(0, 1.00f), Is.EqualTo(50f));
        }

        [Test]
        public void Share_of_frames_over_one_and_two_vsync_intervals()
        {
            var ring = new PerfRing(200);
            for (int i = 0; i < 190; i++) ring.Add(16.7f, 0f, 0f, 0f);
            for (int i = 0; i < 8; i++) ring.Add(20f, 0f, 0f, 0f);
            for (int i = 0; i < 2; i++) ring.Add(40f, 0f, 0f, 0f);
            Assert.That(ring.ShareOver(17.2f), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(ring.ShareOver(33.8f), Is.EqualTo(1f).Within(1e-4f));
            // A frame exactly on the threshold is not over it.
            ring.Clear();
            ring.Add(17.2f, 0f, 0f, 0f);
            Assert.That(ring.ShareOver(17.2f), Is.EqualTo(0f));
        }

        [Test]
        public void Channels_are_independent()
        {
            var ring = new PerfRing(10);
            for (int i = 1; i <= 10; i++) ring.Add(i, 10 * i, 100 * i, 1000 * i);
            Assert.That(ring.Percentile(PerfRing.ChannelFrame, 0.95f), Is.EqualTo(10f));
            Assert.That(ring.Percentile(PerfRing.ChannelCpuMain, 0.50f), Is.EqualTo(50f));
            Assert.That(ring.Percentile(PerfRing.ChannelCpuRender, 0.50f), Is.EqualTo(500f));
            Assert.That(ring.Percentile(PerfRing.ChannelGpu, 0.10f), Is.EqualTo(1000f));
            Assert.That(ring.Mean(PerfRing.ChannelGpu), Is.EqualTo(5500f).Within(1e-2f));
        }

        [Test]
        public void Ring_wraps_and_keeps_the_newest_samples()
        {
            var ring = new PerfRing(4);
            for (int i = 1; i <= 10; i++) ring.Add(i, 0f, 0f, 0f);
            Assert.That(ring.Count, Is.EqualTo(4));
            // 7, 8, 9, 10 remain.
            Assert.That(ring.Percentile(0, 0f), Is.EqualTo(7f));
            Assert.That(ring.Percentile(0, 1f), Is.EqualTo(10f));
            Assert.That(ring.Mean(0), Is.EqualTo(8.5f).Within(1e-5f));
            Assert.That(ring.Recent(0, 0), Is.EqualTo(10f));
            Assert.That(ring.Recent(0, 3), Is.EqualTo(7f));
            Assert.That(ring.Recent(0, 4), Is.EqualTo(0f));
        }

        [Test]
        public void Full_size_ring_wraps_at_3600()
        {
            var ring = new PerfRing(AtlasPerfProbe.RingFrames);
            for (int i = 0; i < 3600; i++) ring.Add(100f, 0f, 0f, 0f);
            for (int i = 0; i < 3600; i++) ring.Add(10f, 0f, 0f, 0f);
            Assert.That(ring.Count, Is.EqualTo(3600));
            Assert.That(ring.Percentile(0, 1f), Is.EqualTo(10f), "the first minute has been overwritten");
            ring.Add(50f, 0f, 0f, 0f);
            Assert.That(ring.Count, Is.EqualTo(3600));
            Assert.That(ring.Percentile(0, 1f), Is.EqualTo(50f));
        }

        [Test]
        public void Window_looks_at_the_last_samples_only()
        {
            var ring = new PerfRing(3600);
            for (int i = 0; i < 1000; i++) ring.Add(30f, 0f, 0f, 12f);
            for (int i = 0; i < 600; i++) ring.Add(16f, 0f, 0f, 5f);
            Assert.That(ring.Percentile(PerfRing.ChannelGpu, 0.95f, 600), Is.EqualTo(5f));
            Assert.That(ring.Percentile(PerfRing.ChannelGpu, 0.95f), Is.EqualTo(12f));
            Assert.That(ring.ShareOver(17.2f, 600), Is.EqualTo(0f));
            Assert.That(ring.ShareOver(17.2f), Is.EqualTo(62.5f).Within(1e-3f));
            // A window larger than the ring's content means everything.
            Assert.That(ring.Mean(0, 100000), Is.EqualTo(ring.Mean(0)));
        }

        [Test]
        public void Bad_samples_count_as_zero()
        {
            var ring = new PerfRing(4);
            ring.Add(float.NaN, -1f, float.PositiveInfinity, 3f);
            Assert.That(ring.Percentile(0, 1f), Is.EqualTo(0f));
            Assert.That(ring.Percentile(1, 1f), Is.EqualTo(0f));
            Assert.That(ring.Percentile(2, 1f), Is.EqualTo(0f));
            Assert.That(ring.Percentile(3, 1f), Is.EqualTo(3f));
        }

        [Test]
        public void No_allocation_after_construction()
        {
            var core = new PerfProbeCore();
            var sample = new PerfSample { FrameMs = 16.7f, CpuMainMs = 4f, CpuRenderMs = 3f, GpuMs = 5f, DrawCalls = 100 };
            var context = new PerfRowContext();
            var csv = new StringBuilder(1024);
            TelemetryMessage row;
            double t = 0;
            // Warm up: fill the ring and run every path once.
            for (int i = 0; i < 4000; i++)
            {
                t += 1.0 / 60.0;
                if (core.AddFrame(t, in sample)) { core.BuildRow(1, in context, out row); csv.Length = 0; TelemetryCsv.AppendRow(csv, in row); }
            }
            int rows = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++)
            {
                t += 1.0 / 60.0;
                sample.FrameMs = 16f + (i % 7);
                if (!core.AddFrame(t, in sample)) continue;
                core.BuildRow(1, in context, out row);
                csv.Length = 0;
                TelemetryCsv.AppendRow(csv, in row);
                rows++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(rows, Is.GreaterThanOrEqualTo(9));
            Assert.That(allocated, Is.EqualTo(0), "the probe core and the CSV row must not allocate");
        }
    }

    /// <summary>The per-second row of the probe core.</summary>
    public class PerfProbeCoreTests
    {
        [Test]
        public void One_row_per_second_with_the_seconds_figures()
        {
            var core = new PerfProbeCore();
            var context = new PerfRowContext { SceneId = 1, SpaceId = 1, Tier = 2, RenderScale = 0.6f, Thermal = 2, Battery = 87, Msaa = 2, StateHz = 60, BumpMax = 1 };
            int rows = 0;
            TelemetryMessage row = default(TelemetryMessage);
            double t = 0;
            for (int i = 0; i <= 125; i++)
            {
                var s = new PerfSample
                {
                    FrameMs = i == 30 ? 40f : 16.7f, CpuMainMs = 4f, CpuRenderMs = 3f, GpuMs = 6f,
                    DrawCalls = i == 10 ? 150 : 100, SetPassCalls = 20, Triangles = i == 12 ? 231400 : 100000,
                    GcAllocBytes = i == 5 ? 2048 : 0, UsedMemoryBytes = 612L << 20
                };
                if (core.AddFrame(t, in s))
                {
                    rows++;
                    if (rows == 1) core.BuildRow(777, in context, out row);
                    else { TelemetryMessage second; core.BuildRow(778, in context, out second); Assert.That(second.Seq, Is.EqualTo(1u)); Assert.That(second.Max, Is.EqualTo(16.7f)); Assert.That(second.DrawCalls, Is.EqualTo(100)); }
                }
                t += 1.0 / 60.0;
            }
            Assert.That(rows, Is.EqualTo(2));
            Assert.That(row.TimeUs, Is.EqualTo(777));
            Assert.That(row.Seq, Is.EqualTo(0u));
            Assert.That(row.Fps, Is.EqualTo(60f).Within(1.5f));
            Assert.That(row.Max, Is.EqualTo(40f));
            Assert.That(row.P50, Is.EqualTo(16.7f));
            Assert.That(row.DrawCalls, Is.EqualTo(150), "worst frame of the second");
            Assert.That(row.SetPass, Is.EqualTo(20));
            Assert.That(row.TrisK, Is.EqualTo(231));
            Assert.That(row.MemMB, Is.EqualTo(612));
            Assert.That(row.GcAllocB, Is.EqualTo(2048u));
            Assert.That(row.CpuMainP95, Is.EqualTo(4f));
            Assert.That(row.CpuRenderP95, Is.EqualTo(3f));
            Assert.That(row.GpuP95, Is.EqualTo(6f));
            Assert.That(row.Tier, Is.EqualTo(2));
            Assert.That(row.Thermal, Is.EqualTo(2), "the thermal state is in the row");
            Assert.That(row.RenderScale, Is.EqualTo(0.6f));
            Assert.That(row.Battery, Is.EqualTo(87));
            Assert.That(row.StateHz, Is.EqualTo(60));
            Assert.That(row.BumpMax, Is.EqualTo(1));
        }

        [Test]
        public void Ladder_figures_need_120_frames_and_use_the_last_600()
        {
            var core = new PerfProbeCore();
            var s = new PerfSample { FrameMs = 16.7f, GpuMs = 12f };
            double t = 0;
            for (int i = 0; i < 119; i++) { core.AddFrame(t, in s); t += 1.0 / 60.0; }
            Assert.That(core.HasData, Is.False);
            core.AddFrame(t, in s);
            Assert.That(core.HasData, Is.True);
            Assert.That(core.GpuP95Ms10s, Is.EqualTo(12f));

            s.GpuMs = 5f;
            for (int i = 0; i < 660; i++) { t += 1.0 / 60.0; core.AddFrame(t, in s); }
            Assert.That(core.GpuP95Ms10s, Is.EqualTo(5f), "ten seconds later the old frames have left the window");
            Assert.That(core.FrameP95Ms10s, Is.EqualTo(16.7f));

            core.ClearRing();
            Assert.That(core.HasData, Is.False);
            Assert.That(core.GpuP95Ms10s, Is.EqualTo(0f));
        }
    }
}
