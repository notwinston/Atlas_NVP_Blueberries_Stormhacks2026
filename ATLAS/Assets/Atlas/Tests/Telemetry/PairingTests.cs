using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>The Station address: the three accepted forms, the refusals, and what is stored.</summary>
    public class PairingTests
    {
        [TestCase("atlas://192.168.0.10:9050", "192.168.0.10", 9050)]
        [TestCase("atlas://192.168.1.20", "192.168.1.20", 9050)]
        [TestCase("ATLAS://10.0.0.2:9051", "10.0.0.2", 9051)]
        [TestCase("192.168.0.10:1", "192.168.0.10", 1)]
        [TestCase("192.168.0.10:65535", "192.168.0.10", 65535)]
        [TestCase("192.168.0.10", "192.168.0.10", 9050)]
        [TestCase("  10.1.2.3:9050 \n", "10.1.2.3", 9050)]
        [TestCase("atlas://192.168.0.10:9050/", "192.168.0.10", 9050)]
        [TestCase("atlas://192.168.0.10:9050/pair?x=1", "192.168.0.10", 9050)]
        [TestCase("atlas://192.168.0.10/", "192.168.0.10", 9050)]
        [TestCase("0.0.0.0:9050", "0.0.0.0", 9050)]
        [TestCase("255.255.255.255", "255.255.255.255", 9050)]
        public void Accepted(string text, string host, int port)
        {
            string h;
            int p;
            Assert.That(PairingController.TryParse(text, out h, out p), Is.True, text);
            Assert.That(h, Is.EqualTo(host));
            Assert.That(p, Is.EqualTo(port));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("atlas:192.168.0.10")]
        [TestCase("atlas:192.168.0.10:9050")]
        [TestCase("atlas://")]
        [TestCase("atlas://station.local:9050")]
        [TestCase("station:9050")]
        [TestCase("localhost")]
        [TestCase("http://192.168.0.10:9050")]
        [TestCase("192.168.0.10:0")]
        [TestCase("192.168.0.10:70000")]
        [TestCase("192.168.0.10:")]
        [TestCase("192.168.0.10:abc")]
        [TestCase("192.168.0.10:9050x")]
        [TestCase("192.168.0")]
        [TestCase("192.168.0.256")]
        [TestCase("192.168.0.10.5")]
        [TestCase("192.168..10")]
        [TestCase("1921.168.0.10")]
        [TestCase("192.168.0.10.example.com")]
        [TestCase("[::1]:9050")]
        [TestCase("fe80::1")]
        [TestCase("-1.2.3.4")]
        public void Refused(string text)
        {
            string h;
            int p;
            Assert.That(PairingController.TryParse(text, out h, out p), Is.False, text ?? "null");
            Assert.That(h, Is.Null);
        }

        [Test]
        public void No_stored_address_means_the_reserved_default()
        {
            var store = new MemoryPairingStore();
            var pairing = new PairingController(store);
            Assert.That(pairing.HasStoredAddress, Is.False);
            Assert.That(pairing.WasPaired, Is.False);
            Assert.That(pairing.Address, Is.EqualTo("192.168.0.10:9050"));
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Idle));
        }

        [Test]
        public void A_deep_link_is_stored_under_the_documented_key_and_survives_a_restart()
        {
            var store = new MemoryPairingStore();
            var pairing = new PairingController(store);
            string changed = null;
            pairing.AddressChanged += a => changed = a;

            Assert.That(pairing.TrySetAddress("atlas://192.168.4.7:9050"), Is.True);
            Assert.That(PairingController.AddressKey, Is.EqualTo("atlas.hub.address"));
            Assert.That(store.GetString("atlas.hub.address", null), Is.EqualTo("192.168.4.7:9050"));
            Assert.That(store.Saves, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo("192.168.4.7:9050"));

            // The typed fallback.
            Assert.That(pairing.TrySetAddress("10.0.0.9"), Is.True);
            Assert.That(pairing.Address, Is.EqualTo("10.0.0.9:9050"));

            // A refused text changes nothing.
            Assert.That(pairing.TrySetAddress("station.local"), Is.False);
            Assert.That(pairing.Address, Is.EqualTo("10.0.0.9:9050"));
            Assert.That(store.Saves, Is.EqualTo(2));

            var restarted = new PairingController(store);
            Assert.That(restarted.HasStoredAddress, Is.True);
            Assert.That(restarted.Host, Is.EqualTo("10.0.0.9"));
            Assert.That(restarted.Port, Is.EqualTo(9050));
        }

        [Test]
        public void Phases_follow_the_clock_and_the_link()
        {
            var store = new MemoryPairingStore();
            var pairing = new PairingController(store);
            var phases = new System.Collections.Generic.List<PairingPhase>();
            pairing.PhaseChanged += phases.Add;

            pairing.Tick(0, LinkState.Offline);
            pairing.BeginPairing();
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Trying));
            pairing.Tick(14000000, LinkState.Offline);
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Trying));
            Assert.That(pairing.SecondsTrying, Is.EqualTo(14f).Within(1e-3f));
            pairing.Tick(15000000, LinkState.Offline);
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Hint));
            pairing.Tick(60000000, LinkState.Resync);
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.TimedOut));
            Assert.That(pairing.WasPaired, Is.False);

            pairing.Tick(61000000, LinkState.Connected);
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Connected));
            Assert.That(pairing.WasPaired, Is.True);
            Assert.That(store.GetInt(PairingController.PairedKey, 0), Is.EqualTo(1));
            Assert.That(store.GetString(PairingController.AddressKey, null), Is.EqualTo("192.168.0.10:9050"));
            Assert.That(phases, Is.EqualTo(new[] { PairingPhase.Trying, PairingPhase.Hint, PairingPhase.TimedOut, PairingPhase.Connected }));

            pairing.Skip();
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Connected), "nothing to skip once connected");

            var second = new PairingController(new MemoryPairingStore());
            second.BeginPairing();
            second.Skip();
            Assert.That(second.Phase, Is.EqualTo(PairingPhase.Skipped));
            second.Tick(99000000, LinkState.Offline);
            Assert.That(second.Phase, Is.EqualTo(PairingPhase.Skipped));
        }
    }
}
