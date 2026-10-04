using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>
    /// What the Hub gives a Spectator beside the relayed <c>State</c> (Master Plan section 7.3.1, lane D contract
    /// 3.2 and 5.8), over real UDP on 127.0.0.1: the source's session and world in <c>Welcome</c>, the parameter
    /// manifest, every parameter change, the <c>world</c> events and the room outline, also for a Spectator that
    /// joins late. And what the Hub measures with its own clock pings: latency for the <c>State</c> digest, and
    /// the phone's address for the link digest.
    /// </summary>
    [TestFixture]
    public class SpectatorTests
    {
        const string Session = "20261004T153012Z-a3f9";

        static List<EventMessage> Named(TestClient client, string ev)
        {
            var found = new List<EventMessage>();
            foreach (EventMessage e in client.Events) if (e.Ev == ev) found.Add(e);
            return found;
        }

        static JsonValue World(int worldVer, string worldHash, string roomId)
        {
            return JsonValue.Object().Set("worldVer", worldVer).Set("worldHash", worldHash).Set("space", 1).Set("roomId", roomId);
        }

        [Test]
        public async Task ASpectator_IsSentManifest_ParameterChanges_WorldEvents_AndTheRoom()
        {
            await using (TestHub t = await TestHub.StartAsync())
            using (var spectator = new TestClient(LinkRole.Spectator, t.Hub.UdpPort))
            {
                // The Spectator is up before the phone: there is no source yet.
                Wait.Ready(spectator);
                Assert.That(spectator.LastWelcome.RoomId, Is.EqualTo(string.Empty));
                string own = spectator.LastWelcome.SessionId;

                using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, "test-iphone", c =>
                {
                    c.SessionId = Session;
                    c.WorldHash = "141060c2fc138145";
                    c.RoomId = "sample_3x3";
                    c.Scene = "S1";
                }))
                {
                    Wait.Ready(phone);
                    Wait.Until(() => t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");

                    // Welcome, again, now that there is a source: session id, forgeUrl, the source's room and world.
                    Wait.Until(() => spectator.LastWelcome.SessionId == Session, "the Spectator to be welcomed with the source's session");
                    Assert.That(own, Is.Not.EqualTo(Session));
                    Assert.That(spectator.LastWelcome.RoomId, Is.EqualTo("sample_3x3"));
                    Assert.That(spectator.LastWelcome.WorldHash, Is.EqualTo("141060c2fc138145"));
                    Assert.That(spectator.LastWelcome.ForgeUrl, Is.EqualTo("http://127.0.0.1:8000"));
                    Assert.That(phone.LastWelcome.SessionId, Is.EqualTo(Session));

                    // The manifest, relayed when the phone sent it.
                    Wait.Until(() => spectator.Manifests.Count == 1, "the phone's manifest at the Spectator");
                    ParamManifestMessage manifest;
                    Assert.That(spectator.Manifests.TryPeek(out manifest), Is.True);
                    Assert.That(manifest.Params.Count, Is.EqualTo(3));
                    Assert.That(manifest.Params[0].Name, Is.EqualTo("curved.cullRho"));
                    Assert.That(manifest.Params[0].Val, Is.EqualTo(3f));

                    // A parameter set from the Console: the phone clamps it and the Spectator hears what it applied.
                    await t.PostJson("/api/params/curved.cullRho", "{\"value\": 99}");
                    Wait.Until(() => Named(spectator, LinkEvents.Param).Count == 1, "the parameter change at the Spectator");
                    ushort id;
                    string name;
                    float value;
                    Assert.That(LinkEvents.TryReadParam(Named(spectator, LinkEvents.Param)[0], out id, out name, out value), Is.True);
                    Assert.That(name, Is.EqualTo("curved.cullRho"));
                    Assert.That(value, Is.EqualTo(3.5f));
                    Assert.That(phone.Rho.Float, Is.EqualTo(3.5f));

                    // One changed on the phone itself (the on-device menu) travels the same way.
                    phone.Post(c => c.Registry.Set("glued.copies", 4f));
                    Wait.Until(() => Named(spectator, LinkEvents.Param).Count == 2, "the phone's own change at the Spectator");
                    Assert.That(Named(spectator, LinkEvents.Param)[1].Data.GetString("source"), Is.EqualTo("local"));

                    // The walker moves to another room: world event, outline, and a Welcome that names the room.
                    phone.Post(c => c.SendEvent("S1", LinkEvents.World, World(2, "bbbbbbbbbbbbbbbb", "sample_4x5")));
                    var outline = new RoomOutlineMessage { Height = 2.4f };
                    outline.AddPoint(0, 0); outline.AddPoint(4, 0); outline.AddPoint(4, 5); outline.AddPoint(0, 5);
                    outline.AddBox(new RoomOutlineBox { Id = 1, Category = 2, W = 1, D = 1, H = 1 });
                    phone.Post(c => c.SendRoomOutline(outline));
                    Wait.Until(() => Named(spectator, LinkEvents.World).Count == 1 && spectator.Outlines == 1, "the world event and the outline at the Spectator");
                    EventMessage world = Named(spectator, LinkEvents.World)[0];
                    Assert.That(world.Scene, Is.EqualTo("S1"));
                    Assert.That(world.Data.GetInt("worldVer"), Is.EqualTo(2));
                    Assert.That(world.Data.GetString("worldHash"), Is.EqualTo("bbbbbbbbbbbbbbbb"));
                    Wait.Until(() => spectator.LastWelcome.RoomId == "sample_4x5", "Welcome to follow the phone's world");
                    Assert.That(spectator.LastWelcome.WorldHash, Is.EqualTo("bbbbbbbbbbbbbbbb"));
                    Assert.That((await t.GetJson("/api/session"))["phone"].GetString("roomId"), Is.EqualTo("sample_4x5"));

                    // The Hub logs what it relays.
                    string events = LogFile.ReadShared(Path.Combine(t.Hub.Log.Folder, SessionLog.EventsFile));
                    Assert.That(events, Does.Contain("\"ev\":\"world\"").And.Contain("\"hub_us\":"));

                    // A Spectator that joins now is brought up to date: the session, the room, the values of this
                    // moment, the newest world.
                    using (var late = new TestClient(LinkRole.Spectator, t.Hub.UdpPort, "late-spectator"))
                    {
                        Wait.Ready(late);
                        Wait.Until(() => late.Manifests.Count == 1 && late.Outlines == 1 && late.Events.Count == 1, "the late Spectator's catch-up");
                        Assert.That(late.LastWelcome.SessionId, Is.EqualTo(Session));
                        Assert.That(late.LastWelcome.RoomId, Is.EqualTo("sample_4x5"));
                        Assert.That(late.LastWelcome.WorldHash, Is.EqualTo("bbbbbbbbbbbbbbbb"));
                        Assert.That(late.Manifests.TryPeek(out manifest), Is.True);
                        Assert.That(manifest.Params[0].Val, Is.EqualTo(3.5f), "the value the phone holds now");
                        Assert.That(manifest.Params[1].Val, Is.EqualTo(4f));
                        Assert.That(Named(late, LinkEvents.World).Count, Is.EqualTo(1));
                        Assert.That(Named(late, LinkEvents.World)[0].Data.GetInt("worldVer"), Is.EqualTo(2));

                        // And from then on it is fed like the first.
                        phone.SendStates = true;
                        Wait.Until(() => late.ReceivedSeqs.Count > 10 && spectator.ReceivedSeqs.Count > 10, "State at both Spectators");
                        phone.SendStates = false;
                        Assert.That((await t.GetJson("/api/session")).GetInt("spectators"), Is.EqualTo(2));
                    }
                }
            }
        }

        [Test]
        public async Task TheDigests_CarryLatency_AndThePhonesAddress()
        {
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, "test-iphone"))
            {
                Wait.Ready(phone);
                Wait.Until(() => t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");

                JsonValue session = await t.GetJson("/api/session");
                Assert.That(session["phone"].GetString("address"), Is.EqualTo("127.0.0.1"));
                Assert.That(session["phone"].GetString("device"), Is.EqualTo("test-iphone"));

                phone.SendStates = true;
                Thread.Sleep(1500);
                phone.SendStates = false;
                JsonValue state = await t.GetJson("/api/state");
                JsonValue latency = state["latencyMs"];
                Assert.That(latency.IsObject, Is.True);
                double p50 = latency.GetDouble("p50", -1), p95 = latency.GetDouble("p95", -1), p99 = latency.GetDouble("p99", -1);
                TestContext.Out.WriteLine("phone to Hub latency: p50 " + p50 + " ms, p95 " + p95 + " ms, p99 " + p99 + " ms");
                // One machine and one clock: the latency is what the loopback and the two poll loops add.
                Assert.That(p50, Is.InRange(0.0, 20.0));
                Assert.That(p95, Is.GreaterThanOrEqualTo(p50));
                Assert.That(p99, Is.GreaterThanOrEqualTo(p95));
                Assert.That(p99, Is.LessThan(250.0));

                string hubLog = await t.Http.GetStringAsync("/logs/" + t.Hub.SessionId + "/hub.log");
                Assert.That(hubLog, Does.Contain("\"address\":\"127.0.0.1\""), "the join line names where the phone is");
            }
        }

        /// <summary>A phone whose clock is nowhere near the Hub's, as every real phone's is.</summary>
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

        [Test]
        public async Task Latency_IsMeasuredOnTheHubsClock_WhateverThePhonesClockSays()
        {
            var clock = new ManualClock(1000000);
            var network = new LoopbackNetwork(clock, 7);
            await using (TestHub t = await TestHub.StartAsync(o =>
            {
                o.Transport = network.CreateEndpoint();
                o.Clock = clock;
                o.ManualPump = true;
                o.UdpPort = 9050;
            }))
            {
                // The phone's clock is 71 minutes ahead of the Hub's.
                var phone = new LinkClient(network.CreateEndpoint(), new ShiftedClock(clock, 4260000000L), LinkRole.Phone) { Device = "far-clock" };
                phone.Start("10.0.0.2", 9050);
                StateMessage state = StateMessage.Identity;

                void Run(double ms, bool send)
                {
                    for (double elapsed = 0; elapsed < ms; elapsed += 5.0)
                    {
                        clock.AdvanceMs(5.0);
                        t.Hub.Pump();
                        phone.Poll();
                        if (send) phone.SendState(ref state);
                        t.Hub.Pump();
                    }
                }

                Run(1500, false);
                Assert.That(t.Hub.PhoneState, Is.EqualTo(LinkState.Resync), "no manifest and no State yet");
                Assert.That(t.Hub.GetState(), Is.Null);

                // 20 ms each way on the wire.
                network.DelayMs = 20.0;
                Run(1000, false);
                Run(2000, true);

                JsonValue digest = t.Hub.GetState();
                Assert.That(digest, Is.Not.Null);
                JsonValue latency = digest["latencyMs"];
                TestContext.Out.WriteLine("latency digest: " + latency.ToJson());
                Assert.That(latency.GetDouble("p50"), Is.InRange(17.5, 27.5), "20 ms on the wire, read at the Hub's next 5 ms poll");
                Assert.That(latency.GetDouble("p95"), Is.InRange(17.5, 27.5));
                Assert.That(latency.GetDouble("p99"), Is.InRange(17.5, 27.5));
                Assert.That(digest.GetDouble("lossPct"), Is.EqualTo(0.0));

                JsonValue session = await t.GetJson("/api/session");
                Assert.That(session["phone"].GetString("address"), Is.EqualTo(LoopbackTransport.PeerAddress));
                Assert.That(session["phone"].GetString("device"), Is.EqualTo("far-clock"));
                phone.Stop();
            }
        }
    }
}
