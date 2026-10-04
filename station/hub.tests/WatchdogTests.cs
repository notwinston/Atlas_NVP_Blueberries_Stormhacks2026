using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>
    /// The Hub's watchdog and RESYNC (Master Plan section 7.2, test L12), on the in-memory network with a manual
    /// clock: the test pulls the cable and moves time itself, so every threshold is hit exactly.
    /// </summary>
    [TestFixture]
    public class WatchdogTests
    {
        sealed class Rig
        {
            public readonly ManualClock Clock = new ManualClock(1000000);
            public readonly LoopbackNetwork Network;
            public TestHub Test;
            public HubServer Hub;
            public ParamRegistry Registry;
            public ParamEntry Rho;
            public LinkClient Phone;

            public Rig() { Network = new LoopbackNetwork(Clock, 7); }

            public async Task StartAsync()
            {
                Test = await TestHub.StartAsync(o =>
                {
                    o.Transport = Network.CreateEndpoint();
                    o.Clock = Clock;
                    o.ManualPump = true;
                    o.UdpPort = 9050;
                });
                Hub = Test.Hub;
            }

            /// <summary>A phone as it is after a fresh launch: every parameter at its default.</summary>
            public void NewPhone()
            {
                Registry = new ParamRegistry();
                Rho = Registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
                Registry.DeclareInt("glued.copies", "glued", 3, 5, 5);
                Phone = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Phone, Registry) { Device = "rig-phone", Scene = "S0" };
                Phone.Start("10.0.0.2", 9050);
            }

            public void Run(double ms, bool pollPhone = true)
            {
                for (double t = 0; t < ms; t += 5.0)
                {
                    Clock.AdvanceMs(5.0);
                    Hub.Pump();
                    if (pollPhone && Phone != null) Phone.Poll();
                    Hub.Pump();
                }
            }
        }

        [Test]
        public async Task PhoneStops_DegradedThenOffline_AndReturns_ConnectedWithTheSnapshotReplayed()
        {
            var rig = new Rig();
            await rig.StartAsync();
            await using (rig.Test)
            {
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Offline));
                rig.NewPhone();
                rig.Run(1500);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Connected));
                Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));

                // The operator tunes a value; it reaches the phone and the Hub's snapshot.
                JsonValue applied;
                Assert.That(rig.Hub.SetParam("curved.cullRho", 2.4f, out applied), Is.EqualTo(HubResult.Ok));
                rig.Run(100);
                Assert.That(rig.Rho.Float, Is.EqualTo(2.4f));

                // The phone falls silent (router off): DEGRADED at 500 ms, OFFLINE at 3 s.
                rig.Network.Cut = true;
                rig.Run(400, false);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Connected), "400 ms of silence is still CONNECTED");
                rig.Run(200, false);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Degraded), "600 ms of silence");
                Assert.That((await rig.Test.GetJson("/api/session"))["phone"].GetString("state"), Is.EqualTo("degraded"));
                rig.Run(2300, false);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Degraded), "2.9 s of silence");
                rig.Run(300, false);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Offline), "3.2 s of silence");
                Assert.That(rig.Hub.HasPhone, Is.False, "the seat is free for the phone's return");
                Assert.That((await rig.Test.GetJson("/api/session"))["phone"].GetString("state"), Is.EqualTo("offline"));

                // A value set while the phone is away is kept for it.
                Assert.That(rig.Hub.SetParam("glued.copies", 4f, out applied), Is.EqualTo(HubResult.Ok));
                Assert.That(applied.GetBool("sent"), Is.False);

                // The app was restarted meanwhile, so it comes back with its defaults...
                rig.Phone.Stop();
                rig.NewPhone();
                Assert.That(rig.Rho.Float, Is.EqualTo(3f));
                rig.Network.Cut = false;
                rig.Run(1500);

                // ...and the Hub puts the tuned values back while the link is in RESYNC.
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Connected));
                Assert.That(rig.Rho.Float, Is.EqualTo(2.4f), "the snapshot was replayed");
                Assert.That(rig.Registry.GetInt("glued.copies"), Is.EqualTo(4), "and so was the value set while it was away");
                Assert.That((await rig.Test.GetJson("/api/params/curved.cullRho")).GetFloat("val"), Is.EqualTo(2.4f));

                // hub.log tells the same story in order.
                string events = LogFile.ReadShared(System.IO.Path.Combine(rig.Hub.Log.Folder, SessionLog.HubLogFile));
                int connected = events.IndexOf("\"state\":\"connected\"", System.StringComparison.Ordinal);
                int degraded = events.IndexOf("\"state\":\"degraded\"", System.StringComparison.Ordinal);
                int offline = events.IndexOf("\"state\":\"offline\"", System.StringComparison.Ordinal);
                int again = events.LastIndexOf("\"state\":\"connected\"", System.StringComparison.Ordinal);
                Assert.That(connected, Is.GreaterThan(0));
                Assert.That(degraded, Is.GreaterThan(connected));
                Assert.That(offline, Is.GreaterThan(degraded));
                Assert.That(again, Is.GreaterThan(offline));
            }
        }

        [Test]
        public async Task RelayingState_AllocatesNothing_InSteadyState()
        {
            var rig = new Rig();
            await rig.StartAsync();
            await using (rig.Test)
            {
                rig.NewPhone();
                var spectator = new LinkClient(rig.Network.CreateEndpoint(), rig.Clock, LinkRole.Spectator);
                spectator.Start("10.0.0.2", 9050);
                int relayed = 0;
                spectator.StateReceived += (in StateMessage s) => relayed++;
                StateMessage state = StateMessage.Identity;

                // Three frames a step is 600 Hz: far more than the phone sends, so every buffer reaches its size.
                void Frames(int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        rig.Clock.AdvanceMs(16.0);
                        rig.Phone.Poll();
                        state.HeadPosX = i;
                        rig.Phone.SendState(ref state);
                        rig.Hub.Pump();
                        spectator.Poll();
                    }
                }

                Frames(400);   // handshakes, clock bursts, log buffers, the once-a-second work
                long statesBefore = rig.Hub.StatesReceived;
                int relayedBefore = relayed;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                Frames(600);   // ten seconds of frames, ten once-a-second ticks and log flushes
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(rig.Hub.StatesReceived - statesBefore, Is.EqualTo(600));
                Assert.That(relayed - relayedBefore, Is.EqualTo(600));
                Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 600 frames of phone, Hub and Spectator");
            }
        }

        [Test]
        public async Task ShortSilence_Degrades_AndRecoversAfterTwoHealthySeconds()
        {
            var rig = new Rig();
            await rig.StartAsync();
            await using (rig.Test)
            {
                rig.NewPhone();
                rig.Run(1500);
                rig.Network.Cut = true;
                rig.Run(800);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Degraded));
                rig.Network.Cut = false;
                rig.Run(1000);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Degraded), "one healthy second is not enough");
                rig.Run(1200);
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Connected), "two healthy seconds are");
                Assert.That(rig.Hub.HasPhone, Is.True);
            }
        }

        [Test]
        public async Task SecondPhone_IsRefused_AndASpectatorIsCounted()
        {
            var rig = new Rig();
            await rig.StartAsync();
            await using (rig.Test)
            {
                rig.NewPhone();
                var spectator = new LinkClient(rig.Network.CreateEndpoint(), rig.Clock, LinkRole.Spectator);
                var intruder = new LinkClient(rig.Network.CreateEndpoint(), rig.Clock, LinkRole.Phone) { Device = "second" };
                spectator.Start("10.0.0.2", 9050);
                intruder.Start("10.0.0.2", 9050);
                for (int i = 0; i < 300; i++)
                {
                    rig.Run(5);
                    spectator.Poll();
                    intruder.Poll();
                }
                JsonValue s = await rig.Test.GetJson("/api/session");
                Assert.That(s.GetInt("spectators"), Is.EqualTo(1));
                Assert.That(s["phone"].GetString("device"), Is.EqualTo("rig-phone"));
                Assert.That(intruder.IsReady, Is.False);
                Assert.That(intruder.LastByeReason, Does.Contain("another phone"));
                Assert.That(rig.Hub.PhoneState, Is.EqualTo(LinkState.Connected));
            }
        }
    }
}
