using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// Link_Watchdog (Master Plan section 11.1): DEGRADED after 500 ms silence, OFFLINE at 3 s, back-off
    /// 0.5 / 1 / 2 / 4 s, RESYNC replays the parameter snapshot.
    /// </summary>
    public sealed class Link_Watchdog
    {
        const long Ms = 1000;

        static LinkWatchdog Connected(long nowUs, List<LinkState> log = null)
        {
            var w = new LinkWatchdog();
            if (log != null) w.StateChanged += (previous, current) => log.Add(current);
            w.Start(nowUs);
            Assert.That(w.ShouldConnect(nowUs), Is.True);
            w.OnTransportConnected(nowUs);
            w.OnResyncComplete(nowUs);
            Assert.That(w.State, Is.EqualTo(LinkState.Connected));
            return w;
        }

        [Test]
        public void Degraded_after_500_ms_of_silence()
        {
            LinkWatchdog w = Connected(0);
            w.OnPacket(100 * Ms);
            Assert.That(w.Tick(599 * Ms), Is.EqualTo(LinkState.Connected), "499 ms silent");
            Assert.That(w.AcceptsRemoteParams, Is.True);
            Assert.That(w.Tick(600 * Ms), Is.EqualTo(LinkState.Degraded), "500 ms silent");
            Assert.That(w.AcceptsRemoteParams, Is.False, "ParamSet is ignored while degraded");
            Assert.That(w.SilenceUs(600 * Ms), Is.EqualTo(500 * Ms));
        }

        [Test]
        public void Degraded_is_left_only_after_2_s_healthy()
        {
            LinkWatchdog w = Connected(0);
            Assert.That(w.Tick(700 * Ms), Is.EqualTo(LinkState.Degraded));

            // Packets resume at 1,000 ms, one every 100 ms.
            long now = 1000 * Ms;
            for (; now < 2990 * Ms; now += 100 * Ms)
            {
                w.OnPacket(now);
                Assert.That(w.Tick(now), Is.EqualTo(LinkState.Degraded), "healthy for " + (now / Ms - 1000) + " ms");
            }
            w.OnPacket(3000 * Ms);
            Assert.That(w.Tick(3000 * Ms), Is.EqualTo(LinkState.Connected), "2 s healthy");

            // A new gap in the middle of recovery starts the 2 s again.
            Assert.That(w.Tick(3600 * Ms), Is.EqualTo(LinkState.Degraded));
            w.OnPacket(3700 * Ms);
            w.OnPacket(4100 * Ms);
            w.OnPacket(4700 * Ms);              // 600 ms gap
            for (now = 4800 * Ms; now < 6700 * Ms; now += 100 * Ms)
            {
                w.OnPacket(now);
                Assert.That(w.Tick(now), Is.EqualTo(LinkState.Degraded));
            }
            w.OnPacket(6700 * Ms);
            Assert.That(w.Tick(6700 * Ms), Is.EqualTo(LinkState.Connected));
        }

        [Test]
        public void Offline_at_3_s_of_silence()
        {
            var log = new List<LinkState>();
            LinkWatchdog w = Connected(0, log);
            w.OnPacket(1000 * Ms);
            Assert.That(w.Tick(3999 * Ms), Is.EqualTo(LinkState.Degraded), "2,999 ms silent");
            Assert.That(w.HasPeer, Is.True);
            Assert.That(w.Tick(4000 * Ms), Is.EqualTo(LinkState.Offline), "3,000 ms silent");
            Assert.That(w.HasPeer, Is.False);
            Assert.That(log, Is.EqualTo(new[] { LinkState.Resync, LinkState.Connected, LinkState.Degraded, LinkState.Offline }));

            // Packets from a peer we gave up on change nothing.
            w.OnPacket(4100 * Ms);
            Assert.That(w.Tick(4100 * Ms), Is.EqualTo(LinkState.Offline));
        }

        [Test]
        public void Transport_disconnect_goes_offline_at_once()
        {
            LinkWatchdog w = Connected(0);
            w.OnTransportDisconnected(250 * Ms);
            Assert.That(w.State, Is.EqualTo(LinkState.Offline));
            Assert.That(w.NextAttemptUs, Is.EqualTo(750 * Ms));
        }

        [Test]
        public void Backoff_is_half_one_two_four_four_seconds()
        {
            LinkWatchdog w = Connected(0);
            long dropped = 10000 * Ms;
            w.OnTransportDisconnected(dropped);

            var attempts = new List<long>();
            for (long now = dropped; now <= dropped + 20000 * Ms; now += 10 * Ms)
            {
                if (!w.ShouldConnect(now)) continue;
                attempts.Add(now - dropped);
                // The attempt fails: the transport reports it, which must not disturb the schedule.
                w.OnTransportDisconnected(now + 5 * Ms);
            }
            Assert.That(attempts, Is.EqualTo(new[] { 500 * Ms, 1500 * Ms, 3500 * Ms, 7500 * Ms, 11500 * Ms, 15500 * Ms, 19500 * Ms }),
                "waits of 0.5, 1, 2, 4, 4, 4, 4 s");
        }

        [Test]
        public void Backoff_resets_after_a_successful_connection()
        {
            var w = new LinkWatchdog();
            w.Start(0);
            Assert.That(w.ShouldConnect(0), Is.True, "the first attempt is immediate");
            Assert.That(w.ShouldConnect(1), Is.False);
            Assert.That(w.ShouldConnect(500 * Ms), Is.True);
            Assert.That(w.ShouldConnect(1500 * Ms), Is.True);
            Assert.That(w.BackoffUs, Is.EqualTo(4000 * Ms));

            w.OnTransportConnected(1600 * Ms);
            Assert.That(w.State, Is.EqualTo(LinkState.Resync));
            Assert.That(w.ShouldConnect(9000 * Ms), Is.False, "no attempts while a peer exists");
            w.OnResyncComplete(1700 * Ms);
            w.OnTransportDisconnected(2000 * Ms);
            Assert.That(w.ShouldConnect(2499 * Ms), Is.False);
            Assert.That(w.ShouldConnect(2500 * Ms), Is.True, "back to 0.5 s");
            Assert.That(w.ShouldConnect(3500 * Ms), Is.True, "then 1 s");

            w.Stop();
            Assert.That(w.ShouldConnect(99000 * Ms), Is.False, "stopped");
        }

        [Test]
        public void Resync_sits_between_connect_and_connected_and_times_out_too()
        {
            var w = new LinkWatchdog();
            w.Start(0);
            w.ShouldConnect(0);
            w.OnTransportConnected(100 * Ms);
            Assert.That(w.State, Is.EqualTo(LinkState.Resync));
            Assert.That(w.AcceptsRemoteParams, Is.False);
            Assert.That(w.Tick(900 * Ms), Is.EqualTo(LinkState.Resync), "resync is not degraded by a slow handshake");
            Assert.That(w.Tick(3100 * Ms), Is.EqualTo(LinkState.Offline), "but a dead one ends at 3 s");
        }

        [Test]
        public void Silence_reported_by_the_transport_counts_as_packets()
        {
            LinkWatchdog w = Connected(0);
            // Keep-alives every 400 ms, seen only through the transport's silence figure.
            for (long now = 0; now <= 5000 * Ms; now += 10 * Ms)
            {
                w.OnSilence(now, now % (400 * Ms));
                Assert.That(w.Tick(now), Is.EqualTo(LinkState.Connected));
            }
            w.OnSilence(5600 * Ms, 600 * Ms);
            Assert.That(w.Tick(5600 * Ms), Is.EqualTo(LinkState.Degraded));
        }

        // ---- the same rules through LinkClient, and the snapshot replay ------------------------------------

        [Test]
        public void Client_follows_connected_degraded_offline_and_reconnects()
        {
            var rig = new LinkRig();
            rig.Connect();
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.PhoneStates, Is.EqualTo(new[] { LinkState.Resync, LinkState.Connected }));

            rig.Network.Cut = true;
            rig.Run(495);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
            rig.Run(10);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Degraded), "500 ms after the cable was pulled");
            rig.Run(2485);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Degraded));
            rig.Run(15);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Offline), "3 s after the cable was pulled");
            var sample = StateMessage.Identity;
            Assert.That(rig.Phone.SendState(ref sample), Is.False, "offline sends do nothing");

            rig.Run(10000);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Offline), "still unplugged");
            Assert.That(rig.Host.HasPhone, Is.False, "the Hub timed the phone out as well");

            rig.Network.Cut = false;
            rig.Run(5000);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected), "back within 5 s of the link returning (L12)");
            Assert.That(rig.Host.HasPhone, Is.True);
            Assert.That(rig.PhoneStates, Is.EqualTo(new[]
            {
                LinkState.Resync, LinkState.Connected, LinkState.Degraded, LinkState.Offline, LinkState.Resync, LinkState.Connected
            }));
        }

        [Test]
        public void ParamSet_is_ignored_while_degraded()
        {
            var rig = new LinkRig();
            rig.Connect();
            rig.Host.SendParam("curved.cullRho", 2.5f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f));

            // A 600 ms drop-out: the link is back and packets flow, but the phone stays DEGRADED for 2 s.
            rig.Network.Cut = true;
            rig.Run(600);
            rig.Network.Cut = false;
            rig.Run(100);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Degraded));
            rig.Host.SendParam("curved.cullRho", 3.4f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f), "ignored");

            rig.Run(2000);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected), "2 s healthy");
            rig.Host.SendParam("curved.cullRho", 3.4f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(3.4f), "applied again");
        }

        [Test]
        public void Resync_replays_the_parameter_snapshot()
        {
            var rig = new LinkRig();
            rig.Connect();

            // The operator tunes three values; one request is out of range and is clamped on the phone.
            rig.Host.SendParam("curved.cullRho", 2.25f);
            rig.Host.SendParam("glued.copies", 3f);
            rig.Host.SendParam("pose.gyroPredict", 0f);
            rig.Run(100);
            rig.Host.SendParam("curved.cullRho", 99f);
            rig.Run(100);
            Assert.That(rig.Rho.Float, Is.EqualTo(3.5f));
            Assert.That(rig.Copies.Int, Is.EqualTo(3));
            Assert.That(rig.Predict.Bool, Is.False);
            float known;
            Assert.That(rig.Host.TryGetSnapshotValue("curved.cullRho", out known), Is.True);
            Assert.That(known, Is.EqualTo(3.5f), "the snapshot holds the clamped value");

            // The link drops and the app comes back with its built-in defaults, as after a restart.
            rig.Network.Cut = true;
            rig.Run(3200);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Offline));
            rig.Registry.ResetAll();
            Assert.That(rig.Rho.Float, Is.EqualTo(3f));
            Assert.That(rig.Copies.Int, Is.EqualTo(5));
            Assert.That(rig.Predict.Bool, Is.True);

            int manifests = 0;
            rig.Host.ManifestReceived += (peer, manifest) => manifests++;
            rig.PhoneStates.Clear();
            rig.Network.Cut = false;
            rig.Run(5000);

            Assert.That(rig.PhoneStates, Is.EqualTo(new[] { LinkState.Resync, LinkState.Connected }));
            Assert.That(manifests, Is.EqualTo(1), "Hello, Welcome, eight pings, then one ParamManifest");
            Assert.That(rig.Phone.ClockSync.SampleCount, Is.GreaterThanOrEqualTo(ClockSync.BurstCount));
            Assert.That(rig.Rho.Float, Is.EqualTo(3.5f), "replayed");
            Assert.That(rig.Copies.Int, Is.EqualTo(3), "replayed");
            Assert.That(rig.Predict.Bool, Is.False, "replayed");
            Assert.That(rig.Host.SnapshotToJson().ToJson(),
                Is.EqualTo("{\"values\":{\"curved.cullRho\":3.5,\"glued.copies\":3.0,\"pose.gyroPredict\":0.0}}"));
        }

        [Test]
        public void A_fresh_hub_keeps_the_values_the_phone_arrives_with()
        {
            var rig = new LinkRig();
            rig.Rho.Set(2.75f);
            rig.Connect();
            Assert.That(rig.Rho.Float, Is.EqualTo(2.75f), "nothing to replay: the phone is the authority");
            float known;
            Assert.That(rig.Host.TryGetSnapshotValue("curved.cullRho", out known), Is.True);
            Assert.That(known, Is.EqualTo(2.75f));

            // A change made on the phone itself (the on-device menu) reaches the snapshot through its echo.
            rig.Copies.Set(4);
            rig.Run(50);
            Assert.That(rig.Host.TryGetSnapshotValue("glued.copies", out known), Is.True);
            Assert.That(known, Is.EqualTo(4f));
        }

        [Test]
        public void Unacknowledged_AssetReady_is_replayed_after_a_reconnect()
        {
            var rig = new LinkRig();
            rig.Connect();
            var received = new List<string>();
            rig.Phone.AssetReadyReceived += asset => received.Add(asset.JobId);

            rig.Host.SendAssetReady(new AssetReadyMessage { JobId = "j-1", BoxId = 1, Sha256 = "aa" });
            rig.Host.SendAssetReady(new AssetReadyMessage { JobId = "j-2", BoxId = 2, Sha256 = "bb" });
            rig.Run(50);
            Assert.That(received, Is.EqualTo(new[] { "j-1", "j-2" }));
            rig.Phone.Send(new AssetAckMessage { JobId = "j-1", BoxId = 1, Sha256 = "aa", Ok = true });
            rig.Run(50);
            Assert.That(rig.Host.UnackedAssetCount, Is.EqualTo(1));

            rig.Network.Cut = true;
            rig.Run(3200);
            rig.Network.Cut = false;
            received.Clear();
            rig.Run(5000);
            Assert.That(received, Is.EqualTo(new[] { "j-2" }), "only the one that was never acknowledged");
        }
    }
}
