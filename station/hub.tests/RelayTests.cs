using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>The relay over real UDP on 127.0.0.1 (Master Plan sections 7.1 and 7.3.1, task P1-D02).</summary>
    [TestFixture]
    public class RelayTests
    {
        [Test]
        public async Task State_At60Hz_IsCountedBySequenceWithNoLoss_AndRelayedToTheSpectator()
        {
            string folder;
            long sent;
            await using (TestHub t = await TestHub.StartAsync())
            {
                folder = t.Hub.Log.Folder;
                using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort))
                using (var spectator = new TestClient(LinkRole.Spectator, t.Hub.UdpPort))
                {
                    Wait.Ready(phone);
                    Wait.Ready(spectator);
                    Wait.Until(() => t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");
                    Assert.That(t.Hub.Spectators, Is.EqualTo(1));

                    phone.SendStates = true;
                    Thread.Sleep(3000);
                    phone.SendStates = false;
                    sent = phone.Sent;
                    Wait.Until(() => t.Hub.StatesReceived == sent, "every State to reach the Hub (" + t.Hub.StatesReceived + "/" + sent + ")", 2000);

                    Assert.That(sent, Is.InRange(165, 185), "three seconds at 60 Hz");
                    Assert.That(t.Hub.StatesLost, Is.EqualTo(0), "gaps in the sequence numbers");

                    Wait.Until(() => spectator.ReceivedSeqs.Count >= sent * 99 / 100, "the Spectator to receive the relayed State", 2000);
                    List<uint> seqs = Wait.Drain(spectator.ReceivedSeqs);
                    for (int i = 1; i < seqs.Count; i++) Assert.That(seqs[i], Is.GreaterThan(seqs[i - 1]), "relayed State is in order");
                    Assert.That(seqs[seqs.Count - 1], Is.EqualTo((uint)(sent - 1)), "the phone's own sequence numbers arrive untouched");
                    Assert.That(spectator.LastReceivedHeadX, Is.GreaterThan(0f), "the relayed packet carries the phone's pose");

                    JsonValue session = await t.GetJson("/api/session");
                    Assert.That(session["phone"].GetLong("states"), Is.EqualTo(sent));
                    Assert.That(session["phone"].GetLong("lost"), Is.EqualTo(0));
                }

                // After a clean shutdown state.bin holds every packet, in order, with a rising receive time.
                await t.Hub.StopAsync();
                List<StateLog.Entry> log = StateLog.ReadAll(Path.Combine(folder, SessionLog.StateFile));
                Assert.That(log.Count, Is.EqualTo(sent));
                for (int i = 0; i < log.Count; i++)
                {
                    Assert.That(log[i].State.Seq, Is.EqualTo((uint)i));
                    Assert.That(log[i].State.GetTile(0), Is.EqualTo(log[i].State.Seq + log[0].State.GetTile(0)));
                    if (i > 0) Assert.That(log[i].HubUs, Is.GreaterThanOrEqualTo(log[i - 1].HubUs));
                }

                // Phone and Hub share this process's stopwatch here, so receive time minus send time is the delay
                // up to the moment the Hub handled the packet, which is when it relays it (budget: under 2 ms).
                var delays = new List<long>();
                foreach (StateLog.Entry e in log) delays.Add(e.HubUs - e.State.TimeUs);
                delays.Sort();
                long median = delays[delays.Count / 2], p95 = delays[delays.Count * 95 / 100];
                TestContext.Out.WriteLine("phone to Hub relay point: median " + median + " us, p95 " + p95 + " us");
                Assert.That(median, Is.InRange(0, 2000), "median delay to the relay point, microseconds");
            }
        }

        [Test]
        public async Task HubRestart_OnTheSamePort_BringsThePhoneBackWithinFiveSeconds()
        {
            int port;
            string folder;
            TestClient phone;
            await using (TestHub first = await TestHub.StartAsync())
            {
                port = first.Hub.UdpPort;
                folder = first.Folder;
                phone = new TestClient(LinkRole.Phone, port);
                Wait.Ready(phone);
            }
            try
            {
                Wait.Until(() => !phone.IsReady, "the phone to notice the Hub has gone", 4000);
                await using (TestHub second = await TestHub.StartAsync(o => o.UdpPort = port))
                {
                    Wait.Until(() => phone.IsReady && second.Hub.PhoneState == LinkState.Connected, "the phone to reconnect to the new Hub", 5000);
                    Assert.That(second.Hub.SessionId, Is.Not.Empty);
                }
            }
            finally
            {
                phone.Dispose();
            }
        }

        [Test]
        public async Task ForgeJob_PostedOnLoopback_ReachesThePhoneAndTheSpectator()
        {
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort))
            using (var spectator = new TestClient(LinkRole.Spectator, t.Hub.UdpPort))
            {
                Wait.Ready(phone);
                Wait.Ready(spectator);

                await t.PostJson("/internal/job", "{\"jobId\":\"j1\",\"roomId\":\"r1\",\"boxId\":3,\"state\":\"running\"}", 204);
                await t.PostJson("/internal/job",
                    "{\"jobId\":\"j1\",\"roomId\":\"r1\",\"boxId\":3,\"sha256\":\"abc123\",\"url\":\"/assets/abc123.glb\",\"bytes\":1000,\"tris\":3000}", 204);

                Wait.Until(() => phone.Jobs.Count == 2 && spectator.Jobs.Count == 2, "the job messages to be relayed");
                Assert.That((await t.GetJson("/api/session"))["phone"].GetString("state"), Is.EqualTo("connected"));
                Assert.That(Wait.Drain(phone.Jobs), Is.EqualTo(new[] { "status:j1:running", "ready:j1:abc123" }));
                Assert.That(Wait.Drain(spectator.Jobs), Is.EqualTo(new[] { "status:j1:running", "ready:j1:abc123" }));

                await t.PostJson("/internal/job", "{\"state\":\"running\"}", 400);
                await t.PostJson("/internal/job", "not json", 400);
            }
        }
    }
}
