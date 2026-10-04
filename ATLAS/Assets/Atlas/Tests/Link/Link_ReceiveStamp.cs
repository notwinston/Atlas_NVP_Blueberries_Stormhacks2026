using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// The clock exchange with packets stamped when they arrive instead of when the poll reads them (Master Plan
    /// section 7.2: offset error at most 2 ms at p95). A phone that polls once per 60 Hz frame used to carry up
    /// to a frame of waiting in t4, and in t2 of its answers to the Hub's pings.
    /// </summary>
    public sealed class Link_ReceiveStamp
    {
        sealed class ShiftedClock : ILinkClock
        {
            readonly ILinkClock _inner;
            readonly long _shiftUs;

            public ShiftedClock(ILinkClock inner, long shiftUs)
            {
                _inner = inner;
                _shiftUs = shiftUs;
            }

            public long NowUs { get { return _inner.NowUs + _shiftUs; } }
        }

        const long PhoneShiftUs = 250000000;    // the phone's clock runs this far ahead of the Hub's
        const long TickUs = 250;                // the simulation's time step
        const long HubPollUs = 1000;            // the Hub's link loop
        const long FrameUs = 16667;             // the phone's poll, once per 60 Hz frame
        const long SteadyFrameJitterUs = 200;   // a display-locked loop: frames this much long or short
        const long RoughFrameJitterUs = 3000;   // a loop that is missing its frame time

        /// <summary>
        /// A phone polling once per jittered 60 Hz frame against a Hub polling every millisecond, over 10% loss,
        /// 15 ms one-way delay and 0-5 ms jitter. Adds the absolute error of both estimates, sampled every
        /// 100 ms once the phone is ready: the phone's of the Hub clock, and the Hub's of the phone clock.
        /// </summary>
        static void Simulate(int seed, bool stamped, long frameJitterUs, double seconds, List<long> phoneErrors, List<long> hubErrors)
        {
            var clock = new ManualClock(1000000);
            var network = new LoopbackNetwork(clock, seed) { Loss = 0.10, DelayMs = 15.0, JitterMs = 5.0, ArrivalStamps = stamped };
            var random = new Random(seed);
            var host = new LinkHost(network.CreateEndpoint(), clock);
            var phone = new LinkClient(network.CreateEndpoint(), new ShiftedClock(clock, PhoneShiftUs), LinkRole.Phone) { Device = "sim-phone" };
            int phonePeer = -1;
            host.PeerJoined += (peer, hello) => phonePeer = peer;
            Assert.That(host.Start(LinkRig.Port), Is.True);
            Assert.That(phone.Start("10.0.0.2", LinkRig.Port), Is.True);

            long end = clock.NowUs + (long)(seconds * 1e6);
            long nextHubPoll = clock.NowUs, nextFrame = clock.NowUs, nextSample = clock.NowUs;
            while (clock.NowUs < end)
            {
                clock.AdvanceUs(TickUs);
                long now = clock.NowUs;
                if (now >= nextHubPoll)
                {
                    nextHubPoll += HubPollUs;
                    host.Poll();
                }
                if (now >= nextFrame)
                {
                    nextFrame = now + FrameUs + (long)((random.NextDouble() * 2.0 - 1.0) * frameJitterUs);
                    phone.Poll();
                }
                if (now < nextSample) continue;
                nextSample += 100000;
                if (!phone.IsReady) continue;
                phoneErrors.Add(Math.Abs(phone.ClockSync.OffsetUs + PhoneShiftUs));
                long offset, rtt;
                if (host.TryGetClock(phonePeer, out offset, out rtt)) hubErrors.Add(Math.Abs(offset - PhoneShiftUs));
            }
            phone.Stop();
            host.Stop();
        }

        static long Percentile(List<long> values, double p)
        {
            values.Sort();
            int index = (int)Math.Ceiling(p * values.Count) - 1;
            return values[Math.Max(0, Math.Min(values.Count - 1, index))];
        }

        static void Run(bool stamped, long frameJitterUs, out long phoneP95, out long hubP95)
        {
            var phoneErrors = new List<long>();
            var hubErrors = new List<long>();
            for (int seed = 1; seed <= 10; seed++) Simulate(seed, stamped, frameJitterUs, 40.0, phoneErrors, hubErrors);
            Assert.That(phoneErrors.Count, Is.GreaterThan(3000), "the phone was ready for nearly every run");
            Assert.That(hubErrors.Count, Is.GreaterThan(3000));
            phoneP95 = Percentile(phoneErrors, 0.95);
            hubP95 = Percentile(hubErrors, 0.95);
        }

        [Test]
        public void At_a_60_Hz_poll_the_offset_error_is_within_2_ms_at_p95_on_both_ends()
        {
            foreach (long frameJitterUs in new[] { 0L, SteadyFrameJitterUs, RoughFrameJitterUs })
            {
                long phoneP95, hubP95;
                Run(true, frameJitterUs, out phoneP95, out hubP95);
                TestContext.Progress.WriteLine("receive stamps, frame jitter " + frameJitterUs + " us: phone p95 " + phoneP95 + " us, hub p95 " + hubP95 + " us");
                Assert.That(phoneP95, Is.LessThanOrEqualTo(2000), "the phone's estimate of the Hub clock, microseconds, frame jitter " + frameJitterUs);
                Assert.That(hubP95, Is.LessThanOrEqualTo(2000), "the Hub's estimate of the phone clock, microseconds, frame jitter " + frameJitterUs);
            }
        }

        [Test]
        public void Without_the_stamps_the_same_link_misses_2_ms()
        {
            // The path this replaces, kept as the reference: every receive time read at the poll. A pong then
            // waits for the frame before t4 is read. The minimum-RTT rule hides that only when the frames are
            // irregular enough for some pong to land just before a poll; on a steady 60 Hz loop the round trips
            // all measure a whole number of frames and the rule has nothing to choose from. The Hub's side
            // comes off better, since its four pings per second are not in step with the phone's frames.
            long phoneP95, hubP95;
            Run(false, SteadyFrameJitterUs, out phoneP95, out hubP95);
            TestContext.Progress.WriteLine("poll times, frame jitter " + SteadyFrameJitterUs + " us: phone p95 " + phoneP95 + " us, hub p95 " + hubP95 + " us");
            Assert.That(phoneP95, Is.GreaterThan(3000), "t4 waits for the frame");
        }

        [Test]
        public void The_loopback_stamp_is_the_arrival_time_on_the_listeners_clock()
        {
            var clock = new ManualClock(5000000);
            var network = new LoopbackNetwork(clock) { DelayMs = 15.0, ArrivalStamps = true };
            LoopbackTransport a = network.CreateEndpoint();
            LoopbackTransport b = network.CreateEndpoint();
            var seen = new List<long>();
            var listener = new StampListener(b, seen);
            a.Listener = new StampListener(a, new List<long>());
            b.Listener = listener;
            b.StampWith(new ShiftedClock(clock, 1000));
            Assert.That(b.StartHost(LinkRig.Port), Is.True);
            Assert.That(a.StartClient(), Is.True);
            a.Connect("10.0.0.2", LinkRig.Port);
            for (int i = 0; i < 10; i++)
            {
                clock.AdvanceMs(10);
                a.Poll();
                b.Poll();
            }

            long sentUs = clock.NowUs;
            Assert.That(a.Send(0, new byte[] { 1, 2, 3 }, 0, 3, LinkDelivery.Reliable), Is.True);
            clock.AdvanceMs(40);    // 25 ms after the packet arrived
            b.Poll();
            Assert.That(seen.Count, Is.EqualTo(1));
            Assert.That(seen[0], Is.EqualTo(sentUs + 15000 + 1000), "arrival, on the shifted clock");

            // Polled the instant it arrives, the stamp is now.
            a.Send(0, new byte[] { 4 }, 0, 1, LinkDelivery.Unreliable);
            clock.AdvanceMs(15);
            b.Poll();
            Assert.That(seen.Count, Is.EqualTo(2));
            Assert.That(seen[1], Is.EqualTo(clock.NowUs + 1000));
        }

        sealed class StampListener : ILinkTransportListener
        {
            readonly ILinkReceiveStamps _stamps;
            readonly List<long> _seen;
            public readonly List<string> Order = new List<string>();

            public StampListener(ILinkReceiveStamps stamps, List<long> seen)
            {
                _stamps = stamps;
                _seen = seen;
            }

            public void OnPeerConnected(int peerId) { Order.Add("connected"); }
            public void OnPeerDisconnected(int peerId, string reason) { Order.Add("disconnected"); }

            public void OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
            {
                _seen.Add(_stamps.LastReceiveUs);
                Order.Add("data " + data[offset] + " x" + length);
            }
        }

        [Test]
        public void Over_udp_a_packet_is_stamped_on_arrival_and_stays_in_order_with_the_disconnect()
        {
            var clock = new StopwatchClock();
            var hostWire = new LiteNetTransport();
            var clientWire = new LiteNetTransport();
            var stamps = new List<long>();
            var hostSide = new StampListener(hostWire, stamps);
            var clientSide = new StampListener(clientWire, new List<long>());
            hostWire.Listener = hostSide;
            clientWire.Listener = clientSide;
            hostWire.StampWith(clock);
            try
            {
                Assert.That(hostWire.StartHost(0), Is.True);
                Assert.That(clientWire.StartClient(), Is.True);
                clientWire.Connect("127.0.0.1", hostWire.LocalPort);
                Stopwatch watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 8000 && (hostSide.Order.Count == 0 || clientSide.Order.Count == 0))
                {
                    hostWire.Poll();
                    clientWire.Poll();
                    Thread.Sleep(2);
                }
                Assert.That(hostSide.Order, Is.EqualTo(new[] { "connected" }));
                Assert.That(clientSide.Order, Is.EqualTo(new[] { "connected" }));

                // Three packets, one larger than a datagram, and the close straight after. The host does not
                // poll for 150 ms: all of it waits in the queue.
                var big = new byte[5000];
                big[0] = 2;
                long sentUs = clock.NowUs;
                Assert.That(clientWire.Send(0, new byte[] { 1, 0 }, 0, 2, LinkDelivery.Reliable), Is.True);
                Assert.That(clientWire.Send(0, big, 0, big.Length, LinkDelivery.Reliable), Is.True);
                Assert.That(clientWire.Send(0, new byte[] { 3 }, 0, 1, LinkDelivery.Reliable), Is.True);
                clientWire.Flush();
                Thread.Sleep(100);
                clientWire.Disconnect(0, new byte[] { 4, 0, 0 }, 0, 3);
                Thread.Sleep(50);
                long polledUs = clock.NowUs;

                watch.Restart();
                while (watch.ElapsedMilliseconds < 3000 && !hostSide.Order.Contains("disconnected"))
                {
                    hostWire.Poll();
                    Thread.Sleep(2);
                }
                Assert.That(hostSide.Order, Is.EqualTo(new[] { "connected", "data 1 x2", "data 2 x5000", "data 3 x1", "data 4 x3", "disconnected" }));
                // The three packets arrived within the first 100 ms, long before the poll that delivered them.
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(stamps[i], Is.GreaterThanOrEqualTo(sentUs), "packet " + i);
                    Assert.That(stamps[i], Is.LessThan(polledUs - 50000), "packet " + i + " carries its arrival, not the poll");
                }
                Assert.That(stamps[3], Is.GreaterThanOrEqualTo(polledUs), "the packet inside the disconnect is stamped at the poll");
            }
            finally
            {
                clientWire.Stop();
                hostWire.Stop();
            }
        }

        [Test]
        public void Over_udp_a_phone_polled_once_per_frame_still_agrees_with_the_hub_clock()
        {
            // One machine, one clock: the true offset is zero. The phone is polled every 16 ms or so, the Hub
            // every millisecond or two, and both estimates must stay inside the plan's 2 ms.
            var clock = new StopwatchClock();
            var host = new LinkHost(new LiteNetTransport(), clock) { SessionId = "udp-stamp" };
            var phone = new LinkClient(new LiteNetTransport(), clock, LinkRole.Phone);
            try
            {
                Assert.That(host.Start(0), Is.True);
                Assert.That(phone.Start("127.0.0.1", host.Port), Is.True);
                Stopwatch watch = Stopwatch.StartNew();
                long nextFrameMs = 0;
                long readyAtMs = -1;
                while (watch.ElapsedMilliseconds < 10000)
                {
                    host.Poll();
                    if (watch.ElapsedMilliseconds >= nextFrameMs)
                    {
                        nextFrameMs = watch.ElapsedMilliseconds + 16;
                        phone.Poll();
                    }
                    if (readyAtMs < 0 && phone.IsReady && host.HasPhone) readyAtMs = watch.ElapsedMilliseconds;
                    // Two seconds of heartbeat after the handshake: eight more of the Hub's pings.
                    if (readyAtMs >= 0 && watch.ElapsedMilliseconds - readyAtMs >= 2000) break;
                    Thread.Sleep(1);
                }
                Assert.That(readyAtMs, Is.GreaterThanOrEqualTo(0L), "the phone connects: " + phone.State);
                Assert.That(phone.ClockSync.SampleCount, Is.GreaterThanOrEqualTo(ClockSync.BurstCount));
                Assert.That(Math.Abs(phone.ClockSync.OffsetUs), Is.LessThanOrEqualTo(2000L), "the phone's estimate, microseconds");
                long offsetUs, rttUs;
                Assert.That(host.TryGetClock(host.PhonePeer, out offsetUs, out rttUs), Is.True);
                Assert.That(Math.Abs(offsetUs), Is.LessThanOrEqualTo(2000L), "the Hub's estimate, microseconds");
                Assert.That(rttUs, Is.InRange(0L, 5000L), "the frame wait is no longer inside the round trip");
            }
            finally
            {
                phone.Stop();
                host.Stop();
            }
        }
    }
}
