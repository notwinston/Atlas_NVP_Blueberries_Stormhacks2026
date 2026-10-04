using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// The same client and host over real UDP on 127.0.0.1 with LiteNetLib: connection key, two channels, the
    /// handshake, a State relayed to a Spectator and a parameter set from the Hub.
    /// </summary>
    public sealed class Link_Udp
    {
        static bool Pump(LinkHost host, LinkClient a, LinkClient b, int timeoutMs, System.Func<bool> done)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                host.Poll();
                a.Poll();
                if (b != null) b.Poll();
                if (done()) return true;
                Thread.Sleep(2);
            }
            return done();
        }

        [Test]
        public void Phone_hub_and_spectator_exchange_over_udp_loopback()
        {
            var clock = new StopwatchClock();
            var hostWire = new LiteNetTransport();
            var phoneWire = new LiteNetTransport();
            var spectatorWire = new LiteNetTransport();
            var host = new LinkHost(hostWire, clock) { SessionId = "udp-test" };
            var registry = new ParamRegistry();
            ParamEntry rho = registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            var phone = new LinkClient(phoneWire, clock, LinkRole.Phone, registry);
            var spectator = new LinkClient(spectatorWire, clock, LinkRole.Spectator);
            try
            {
                Assert.That(host.Start(0), Is.True, "the Hub socket opens on a free port");
                int port = host.Port;
                Assert.That(port, Is.GreaterThan(0));
                Assert.That(phone.Start("127.0.0.1", port), Is.True);
                Assert.That(spectator.Start("127.0.0.1", port), Is.True);

                Assert.That(Pump(host, phone, spectator, 8000,
                        () => phone.State == LinkState.Connected && spectator.State == LinkState.Connected && host.Manifest != null),
                    Is.True, "both clients CONNECTED: phone " + phone.State + ", spectator " + spectator.State);
                Assert.That(phone.Welcome.SessionId, Is.EqualTo("udp-test"));
                Assert.That(host.CountRole(LinkRole.Spectator), Is.EqualTo(1));
                Assert.That(phone.ClockSync.HasEstimate, Is.True);
                Assert.That(System.Math.Abs(phone.ClockSync.OffsetUs), Is.LessThan(2000L), "one machine, one clock");

                int relayed = 0;
                StateMessage seen = default(StateMessage);
                spectator.StateReceived += (in StateMessage s) => { relayed++; seen = s; };
                StateMessage state = StateMessage.Identity;
                state.SpaceId = 4;
                state.SetTile(1, 2, 3, 4, 5, 6);
                bool gotState = Pump(host, phone, spectator, 3000, () =>
                {
                    phone.SendState(ref state);
                    return relayed >= 20;
                });
                Assert.That(gotState, Is.True, "State goes phone -> Hub -> Spectator on the sequenced channel");
                Assert.That(seen.SpaceId, Is.EqualTo(4));
                Assert.That(seen.GetTile(5), Is.EqualTo(6L));

                Assert.That(host.SendParam("curved.cullRho", 2.5f), Is.True);
                Assert.That(Pump(host, phone, spectator, 3000, () => rho.Float == 2.5f), Is.True, "ParamSet on the reliable channel");

                host.SendCommand(OperatorCommand.Stop());
                Assert.That(Pump(host, phone, spectator, 3000, () => phone.PendingCommands == 1), Is.True);
            }
            finally
            {
                phone.Stop();
                spectator.Stop();
                host.Stop();
            }
        }

        [Test]
        public void A_spectator_mirrors_the_phone_over_udp_loopback()
        {
            var clock = new StopwatchClock();
            var host = new LinkHost(new LiteNetTransport(), clock) { SessionId = "hub-own", ForgeUrl = "http://127.0.0.1:8000" };
            // What the Hub process does: the phone names the session and the world.
            host.HelloReceived += (peer, hello) =>
            {
                if (hello.Role != LinkRole.Phone) return;
                if (hello.SessionId.Length > 0) host.SessionId = hello.SessionId;
                host.WorldHash = hello.WorldHash;
                host.RoomId = hello.RoomId;
            };
            int pongs = 0;
            host.TPongReceived += (int peer, in TPongMessage pong, long hubReceiveUs) => pongs++;

            var registry = new ParamRegistry();
            ParamEntry rho = registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            var phone = new LinkClient(new LiteNetTransport(), clock, LinkRole.Phone, registry)
            {
                SessionId = "20261004T153012Z-a3f9", WorldHash = "141060c2fc138145", RoomId = "sample_3x3", Scene = "S1"
            };

            var mirrorRegistry = new ParamRegistry();
            ParamEntry mirrorRho = mirrorRegistry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            var spectator = new LinkClient(new LiteNetTransport(), clock, LinkRole.Spectator, mirrorRegistry);
            int manifests = 0, worlds = 0, changes = 0, outlines = 0;
            string worldHash = null;
            spectator.ManifestReceived += m => { manifests++; m.ApplyTo(mirrorRegistry); };
            spectator.RoomOutlineReceived += o => outlines++;
            spectator.EventReceived += e =>
            {
                ushort id;
                string name;
                float value;
                if (LinkEvents.TryReadParam(e, out id, out name, out value))
                {
                    changes++;
                    mirrorRegistry.Set(name, value, ParamSource.Remote);
                }
                else if (e.Ev == LinkEvents.World)
                {
                    worlds++;
                    worldHash = e.Data.GetString("worldHash");
                }
            };
            try
            {
                Assert.That(host.Start(0), Is.True);
                int port = host.Port;
                // The Spectator is there first and learns of the phone when it arrives.
                Assert.That(spectator.Start("127.0.0.1", port), Is.True);
                Assert.That(Pump(host, spectator, null, 8000, () => spectator.State == LinkState.Connected), Is.True, "spectator CONNECTED");
                Assert.That(spectator.Welcome.SessionId, Is.EqualTo("hub-own"));

                Assert.That(phone.Start("127.0.0.1", port), Is.True);
                Assert.That(Pump(host, phone, spectator, 8000, () => phone.State == LinkState.Connected && manifests == 1),
                    Is.True, "phone CONNECTED and its manifest relayed: phone " + phone.State + ", manifests " + manifests);
                Assert.That(phone.Welcome.SessionId, Is.EqualTo("20261004T153012Z-a3f9"));
                Assert.That(Pump(host, phone, spectator, 3000, () => spectator.Welcome.SessionId == "20261004T153012Z-a3f9"),
                    Is.True, "the Spectator is welcomed again with the source's session");
                Assert.That(spectator.Welcome.RoomId, Is.EqualTo("sample_3x3"));
                Assert.That(spectator.Welcome.WorldHash, Is.EqualTo("141060c2fc138145"));
                Assert.That(spectator.Welcome.ForgeUrl, Is.EqualTo("http://127.0.0.1:8000"));
                Assert.That(mirrorRho.Float, Is.EqualTo(3f));

                // A parameter change, a world event and the room, phone to Hub to Spectator on the reliable channel.
                Assert.That(host.SendParam("curved.cullRho", 2.5f), Is.True);
                Assert.That(Pump(host, phone, spectator, 3000, () => changes == 1), Is.True, "the applied value is relayed as a param event");
                Assert.That(rho.Float, Is.EqualTo(2.5f));
                Assert.That(mirrorRho.Float, Is.EqualTo(2.5f));

                Assert.That(phone.SendEvent("S1", LinkEvents.World,
                    JsonValue.Object().Set("worldVer", 3).Set("worldHash", "141060c2fc138145").Set("roomId", "sample_3x3")), Is.True);
                var outline = new RoomOutlineMessage { Height = 2.4f };
                outline.AddPoint(0, 0); outline.AddPoint(3, 0); outline.AddPoint(3, 3); outline.AddPoint(0, 3);
                Assert.That(phone.SendRoomOutline(outline), Is.True);
                Assert.That(Pump(host, phone, spectator, 3000, () => worlds == 1 && outlines == 1), Is.True, "world event and outline relayed");
                Assert.That(worldHash, Is.EqualTo("141060c2fc138145"));

                // The Hub's own pings: an address, a clock offset and a round trip per client.
                Assert.That(Pump(host, phone, spectator, 3000, () => pongs >= 16), Is.True, "both clients answer the Hub's pings");
                long offsetUs, rttUs;
                Assert.That(host.TryGetClock(host.PhonePeer, out offsetUs, out rttUs), Is.True);
                Assert.That(System.Math.Abs(offsetUs), Is.LessThan(2000L), "one machine, one clock");
                Assert.That(rttUs, Is.InRange(0L, 20000L));
                Assert.That(host.GetAddress(host.PhonePeer), Is.EqualTo("127.0.0.1"));
            }
            finally
            {
                phone.Stop();
                spectator.Stop();
                host.Stop();
            }
        }

        /// <summary>Counts what a transport reports while nobody answers.</summary>
        sealed class AttemptListener : ILinkTransportListener
        {
            public int Connected, Failed, NotAnAddress;
            public string LastReason;

            public void OnPeerConnected(int peerId) { Connected++; }

            public void OnPeerDisconnected(int peerId, string reason)
            {
                if (peerId < 0) NotAnAddress++;
                else Failed++;
                LastReason = reason;
            }

            public void OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery) { }
        }

        [Test]
        public void With_no_station_a_repeated_connect_allocates_nothing_and_an_attempt_fails_in_half_a_second()
        {
            // The Station's address with no Hub running: a UDP port that is open and never answers.
            using (var silent = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                int port = ((System.Net.IPEndPoint)silent.Client.LocalEndPoint).Port;
                var wire = new LiteNetTransport();
                var listener = new AttemptListener();
                wire.Listener = listener;
                try
                {
                    Assert.That(wire.StartClient(), Is.True);
                    Assert.That(wire.Manager.ReconnectDelay, Is.EqualTo(LiteNetTransport.ClientReconnectDelayMs));
                    Assert.That(wire.Manager.ReconnectDelay, Is.EqualTo(250));
                    Assert.That(wire.Manager.MaxConnectAttempts, Is.EqualTo(1));

                    // The first call parses the address and LiteNetLib makes its peer. Every further call while
                    // that attempt runs (the watchdog's back-off, the Reconnect button) must cost nothing.
                    Stopwatch watch = Stopwatch.StartNew();
                    wire.Connect("127.0.0.1", port);
                    wire.Connect("127.0.0.1", port);
                    long before = System.GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 64; i++) wire.Connect("127.0.0.1", port);
                    long repeated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                    Assert.That(watch.ElapsedMilliseconds, Is.LessThan(200L), "measured while the first attempt was still running");
                    Assert.That(repeated, Is.EqualTo(0L), "bytes allocated by 64 Connect calls to the same address");

                    // One attempt: two requests 250 ms apart, failed after 500 ms. The library does not go on
                    // retrying for seconds underneath the link's own back-off.
                    while (listener.Failed == 0 && watch.ElapsedMilliseconds < 4000)
                    {
                        wire.Poll();
                        Thread.Sleep(2);
                    }
                    long failedAfterMs = watch.ElapsedMilliseconds;
                    Assert.That(listener.Failed, Is.EqualTo(1));
                    Assert.That(listener.LastReason, Is.EqualTo("ConnectionFailed"));
                    Assert.That(failedAfterMs, Is.InRange(400L, 1500L));
                    Assert.That(listener.Connected, Is.EqualTo(0));

                    // The next attempt is a new peer inside LiteNetLib; the address is not parsed again and the key
                    // is not written again. Reported, not asserted: those bytes are the library's.
                    before = System.GC.GetAllocatedBytesForCurrentThread();
                    wire.Connect("127.0.0.1", port);
                    long fresh = System.GC.GetAllocatedBytesForCurrentThread() - before;
                    TestContext.Out.WriteLine("attempt failed after " + failedAfterMs + " ms; a new attempt allocates " + fresh + " B inside LiteNetLib");

                    // A host name is refused without a DNS lookup, and saying so twice does not allocate either.
                    wire.Connect("station.local", port);
                    Assert.That(listener.NotAnAddress, Is.EqualTo(1));
                    Assert.That(listener.LastReason, Is.EqualTo("address is not an IP literal"));
                    before = System.GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 8; i++) wire.Connect("station.local", port);
                    Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0L));
                    Assert.That(listener.NotAnAddress, Is.EqualTo(9));

                    // "Change IP": another address is parsed once and used from then on.
                    wire.Connect("127.0.0.2", port);
                    wire.Connect("127.0.0.2", port);
                    before = System.GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 8; i++) wire.Connect("127.0.0.2", port);
                    Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0L));
                }
                finally
                {
                    wire.Stop();
                }
            }
        }

        [Test]
        public void A_client_rejects_incoming_connections_and_a_wrong_key_is_refused()
        {
            var clock = new StopwatchClock();
            var a = new LiteNetTransport();
            var b = new LiteNetTransport();
            var phone = new LinkClient(a, clock, LinkRole.Phone);
            var other = new LinkClient(b, clock, LinkRole.Spectator);
            try
            {
                // A client that is not connected to anything: start it against a port nobody listens on.
                Assert.That(phone.Start("127.0.0.1", 1), Is.True);
                Assert.That(a.LocalPort, Is.GreaterThan(0));
                // Another client dials the phone's own port. The phone never accepts (section 7.1).
                Assert.That(other.Start("127.0.0.1", a.LocalPort), Is.True);
                Stopwatch watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 1500)
                {
                    phone.Poll();
                    other.Poll();
                    Thread.Sleep(2);
                }
                Assert.That(other.State, Is.EqualTo(LinkState.Offline));
                Assert.That(phone.State, Is.EqualTo(LinkState.Offline));
            }
            finally
            {
                phone.Stop();
                other.Stop();
            }
        }
    }
}
