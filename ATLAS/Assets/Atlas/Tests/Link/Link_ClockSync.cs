using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// Link_ClockSync (Master Plan section 11.1): the offset estimate survives a lossy link. Simulated 10% loss,
    /// 15 ms one-way delay, 0-5 ms jitter: error against the injected offset at most 2 ms at p95.
    /// </summary>
    public sealed class Link_ClockSync
    {
        struct InFlight
        {
            public long ArriveUs;
            public TPingMessage Ping;
            public TPongMessage Pong;
        }

        /// <summary>
        /// Runs one client against one answering host over a simulated link and returns the absolute error of
        /// the estimate, sampled every 100 ms once the opening burst is over.
        /// </summary>
        static List<long> Simulate(int seed, long trueOffsetUs, double loss, double delayMs, double jitterMs,
            double seconds, long pollStepUs, out ClockSync sync)
        {
            var random = new Random(seed);
            sync = new ClockSync();
            var pings = new List<InFlight>();
            var pongs = new List<InFlight>();
            var errors = new List<long>();
            long now = 1000000;                         // the client's clock
            long end = now + (long)(seconds * 1e6);
            long nextSample = now;
            sync.Restart(now);

            while (now < end)
            {
                // The host answers the instant a ping arrives (it has its own receive loop).
                for (int i = pings.Count - 1; i >= 0; i--)
                {
                    if (pings[i].ArriveUs > now) continue;
                    InFlight f = pings[i];
                    pings.RemoveAt(i);
                    long hostTime = f.ArriveUs + trueOffsetUs;
                    if (random.NextDouble() < loss) continue;
                    f.Pong = ClockSync.Answer(f.Ping, hostTime, hostTime);
                    f.ArriveUs = f.ArriveUs + (long)((delayMs + jitterMs * random.NextDouble()) * 1000.0);
                    pongs.Add(f);
                }
                // The client sees a pong when its poll runs, which is when it reads t4.
                for (int i = pongs.Count - 1; i >= 0; i--)
                {
                    if (pongs[i].ArriveUs > now) continue;
                    sync.OnPong(pongs[i].Pong, now);
                    pongs.RemoveAt(i);
                }

                TPingMessage ping;
                if (sync.TryNextPing(now, out ping) && random.NextDouble() >= loss)
                {
                    pings.Add(new InFlight
                    {
                        Ping = ping,
                        ArriveUs = now + (long)((delayMs + jitterMs * random.NextDouble()) * 1000.0)
                    });
                }

                if (now >= nextSample)
                {
                    nextSample += 100000;
                    if (sync.BurstComplete(now) && sync.HasEstimate) errors.Add(Math.Abs(sync.OffsetUs - trueOffsetUs));
                }
                now += pollStepUs;
            }
            return errors;
        }

        static long Percentile(List<long> values, double p)
        {
            values.Sort();
            int index = (int)Math.Ceiling(p * values.Count) - 1;
            return values[Math.Max(0, Math.Min(values.Count - 1, index))];
        }

        [Test]
        public void Offset_error_is_within_2_ms_at_p95_on_a_lossy_jittery_link()
        {
            long[] offsets = { 0, 5000000, -123456789, 987654321012L, -42 };
            var all = new List<long>();
            for (int seed = 1; seed <= 20; seed++)
            {
                ClockSync sync;
                List<long> errors = Simulate(seed, offsets[seed % offsets.Length], 0.10, 15.0, 5.0, 60.0, 1000, out sync);
                Assert.That(errors.Count, Is.GreaterThan(500), "the estimate was available for nearly the whole run");
                long p95 = Percentile(errors, 0.95);
                Assert.That(p95, Is.LessThanOrEqualTo(2000), "seed " + seed + " p95 error in microseconds");
                // Round trip of the best sample: twice the delay plus a little jitter.
                Assert.That(sync.RttUs, Is.InRange(30000, 40000), "seed " + seed);
                all.AddRange(errors);
            }
            Assert.That(Percentile(all, 0.95), Is.LessThanOrEqualTo(2000));
        }

        [Test]
        public void Polling_once_per_frame_costs_at_most_a_quarter_frame_of_accuracy()
        {
            // On the phone a pong waits for the next 60 Hz poll before t4 is read, which makes the return leg
            // look up to one frame longer. The estimate then carries that wait, halved: under 5 ms at p95.
            var all = new List<long>();
            for (int seed = 1; seed <= 20; seed++)
            {
                ClockSync sync;
                all.AddRange(Simulate(seed, 250000000, 0.10, 15.0, 5.0, 60.0, 16667, out sync));
            }
            Assert.That(Percentile(all, 0.95), Is.LessThanOrEqualTo(5000));
        }

        [Test]
        public void A_clean_link_gives_the_exact_offset()
        {
            ClockSync sync;
            List<long> errors = Simulate(3, 777777, 0.0, 15.0, 0.0, 10.0, 1000, out sync);
            Assert.That(Percentile(errors, 1.0), Is.LessThanOrEqualTo(500), "only the 1 ms poll step is left");
            Assert.That(sync.SampleCount, Is.GreaterThanOrEqualTo(ClockSync.BurstCount + 8));
            Assert.That(sync.ToLocalUs(sync.ToRemoteUs(123456)), Is.EqualTo(123456L));
        }

        [Test]
        public void Schedule_is_eight_pings_50_ms_apart_then_one_per_second()
        {
            var sync = new ClockSync();
            TPingMessage ping;
            Assert.That(sync.TryNextPing(0, out ping), Is.False, "not started");

            sync.Restart(1000);
            var times = new List<long>();
            for (long now = 1000; now <= 1000 + 5000000; now += 1000)
                if (sync.TryNextPing(now, out ping))
                {
                    Assert.That(ping.T1Us, Is.EqualTo(now));
                    Assert.That(ping.Seq, Is.EqualTo((uint)times.Count));
                    times.Add(now - 1000);
                }

            Assert.That(times.Count, Is.EqualTo(12));
            for (int i = 0; i < 8; i++) Assert.That(times[i], Is.EqualTo(i * 50000L), "burst ping " + i);
            for (int i = 8; i < 12; i++) Assert.That(times[i], Is.EqualTo(350000L + (i - 7) * 1000000L), "steady ping " + i);
            Assert.That(ClockSync.WindowSize, Is.EqualTo(16));
            Assert.That(ClockSync.Alpha, Is.EqualTo(0.1));
        }

        [Test]
        public void Formula_matches_the_four_timestamp_exchange()
        {
            var sync = new ClockSync();
            sync.Restart(0);
            TPingMessage ping;
            Assert.That(sync.TryNextPing(100, out ping), Is.True);
            // t1 = 100; host is 10,000 ahead; 30 up, 5 inside the host, 50 down.
            TPongMessage pong = ClockSync.Answer(ping, 100 + 30 + 10000, 100 + 30 + 5 + 10000);
            Assert.That(sync.OnPong(pong, 100 + 30 + 5 + 50), Is.True);
            Assert.That(sync.LastRttUs, Is.EqualTo(80L));
            Assert.That(sync.OffsetUs, Is.EqualTo(9990L), "((t2 - t1) + (t3 - t4)) / 2 = (10030 + 9950) / 2");
            Assert.That(sync.HasEstimate, Is.True);
        }

        [Test]
        public void Stale_duplicate_and_impossible_pongs_are_rejected()
        {
            var sync = new ClockSync();
            sync.Restart(0);
            TPingMessage ping;
            sync.TryNextPing(0, out ping);
            TPongMessage pong = ClockSync.Answer(ping, 500, 600);

            var forged = pong;
            forged.T1Us = 12345;
            Assert.That(sync.OnPong(forged, 2000), Is.False, "t1 does not match the ping we sent");
            var unknown = pong;
            unknown.Seq = 9;
            Assert.That(sync.OnPong(unknown, 2000), Is.False, "never sent");

            Assert.That(sync.OnPong(pong, 2000), Is.True);
            Assert.That(sync.OnPong(pong, 2100), Is.False, "duplicate");
            Assert.That(sync.SampleCount, Is.EqualTo(1));

            sync.TryNextPing(50000, out ping);
            TPongMessage impossible = ClockSync.Answer(ping, 0, 900000); // the host claims to have held it longer than the round trip
            Assert.That(sync.OnPong(impossible, 60000), Is.False);

            sync.TryNextPing(100000, out ping);
            TPongMessage late = ClockSync.Answer(ping, 100500, 100600);
            sync.Restart(200000);
            Assert.That(sync.OnPong(late, 201000), Is.False, "from before the restart");
            Assert.That(sync.HasEstimate, Is.False);
        }

        [Test]
        public void Burst_completes_on_eight_pongs_or_after_the_grace_time()
        {
            var sync = new ClockSync();
            sync.Restart(0);
            long now = 0;
            TPingMessage ping;
            for (int i = 0; i < 8; i++)
            {
                Assert.That(sync.BurstComplete(now), Is.False);
                Assert.That(sync.TryNextPing(now, out ping), Is.True);
                sync.OnPong(ClockSync.Answer(ping, now + 10, now + 10), now + 20);
                now += ClockSync.BurstIntervalUs;
            }
            Assert.That(sync.BurstComplete(now), Is.True, "eight pongs");

            // Every pong lost: the burst still ends, 250 ms after its last ping.
            sync.Restart(0);
            now = 0;
            for (int i = 0; i < 8; i++)
            {
                sync.TryNextPing(now, out ping);
                now += ClockSync.BurstIntervalUs;
            }
            long lastPing = 7 * ClockSync.BurstIntervalUs;
            Assert.That(sync.BurstComplete(lastPing + ClockSync.BurstGraceUs - 1), Is.False);
            Assert.That(sync.BurstComplete(lastPing + ClockSync.BurstGraceUs), Is.True);
            Assert.That(sync.HasEstimate, Is.False);
        }
    }
}
