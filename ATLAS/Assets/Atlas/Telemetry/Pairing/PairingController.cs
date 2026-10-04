using System;
using System.Collections.Generic;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>Where pairing stands, for the onboarding screen.</summary>
    public enum PairingPhase : byte
    {
        Idle = 0,
        /// <summary>Trying to reach the Station; the Local Network prompt may be up.</summary>
        Trying = 1,
        Connected = 2,
        /// <summary>15 s without a connection: show where the Local Network switch is, and "Continue without Station".</summary>
        Hint = 3,
        /// <summary>60 s without a connection.</summary>
        TimedOut = 4,
        /// <summary>The walker chose to continue without the Station.</summary>
        Skipped = 5
    }

    /// <summary>The link as the in-hand link panel shows it (Master Plan section 7.2).</summary>
    public struct LinkStatus
    {
        public LinkState State;
        /// <summary><c>&lt;ip&gt;:&lt;port&gt;</c> of the Station.</summary>
        public string Address;
        public int RttMs;
        public int LossPermille;
        /// <summary>The reason of the Hub's last <c>Bye</c>, or null.</summary>
        public string LastError;
    }

    /// <summary>The small key-value store pairing keeps the Station address in: <c>PlayerPrefs</c> on the phone.</summary>
    public interface IPairingStore
    {
        string GetString(string key, string fallback);
        void SetString(string key, string value);
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        bool HasKey(string key);
        void Save();
    }

    /// <summary>An <see cref="IPairingStore"/> held in memory, for tests and for the Spectator.</summary>
    public sealed class MemoryPairingStore : IPairingStore
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>How many times <see cref="Save"/> ran.</summary>
        public int Saves { get; private set; }

        public string GetString(string key, string fallback)
        {
            string v;
            return _values.TryGetValue(key, out v) ? v : fallback;
        }

        public void SetString(string key, string value) { _values[key] = value; }

        public int GetInt(string key, int fallback)
        {
            string v;
            int parsed;
            return _values.TryGetValue(key, out v) && int.TryParse(v, out parsed) ? parsed : fallback;
        }

        public void SetInt(string key, int value) { _values[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        public bool HasKey(string key) { return _values.ContainsKey(key); }

        public void Save() { Saves++; }
    }

    /// <summary>
    /// Pairing with the Station (Master Plan Appendix C.7, task P1-D11): the stored address, the deep link from
    /// the QR code, the typed fallback, and the phase the onboarding screen shows. Engine-free: the store and
    /// the clock come from outside. The one-second connect attempts themselves are made by <see cref="LinkPump"/>.
    /// </summary>
    public sealed class PairingController
    {
        public const string AddressKey = "atlas.hub.address";
        public const string PairedKey = "atlas.hub.paired";
        public const string DefaultAddress = "192.168.0.10";
        public const float RetrySeconds = 1f, HintAfterSeconds = 15f, GiveUpAfterSeconds = 60f;

        readonly IPairingStore _store;
        readonly LinkPump _pump;
        string _host;
        int _port;
        long _tryingSinceUs;
        long _nowUs;

        /// <param name="store">Where the address is kept.</param>
        /// <param name="pump">The link driver, or null for a controller that only parses and stores.</param>
        public PairingController(IPairingStore store, LinkPump pump = null)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
            _pump = pump;
            HasStoredAddress = store.HasKey(AddressKey) &&
                               PairingAddress.TryParse(store.GetString(AddressKey, null), out _host, out _port);
            if (!HasStoredAddress)
            {
                _host = DefaultAddress;
                _port = PairingAddress.DefaultPort;
            }
            WasPaired = store.GetInt(PairedKey, 0) != 0;
            if (pump != null)
            {
                pump.Pairing = this;
                pump.SetAddress(_host, _port);
            }
        }

        /// <summary><c>&lt;ip&gt;:&lt;port&gt;</c>, for example <c>192.168.0.10:9050</c>.</summary>
        public string Address { get { return PairingAddress.Format(_host, _port); } }
        public string Host { get { return _host; } }
        public int Port { get { return _port; } }

        /// <summary>False while the address is the built-in default.</summary>
        public bool HasStoredAddress { get; private set; }

        /// <summary>True once this phone has been connected to a Station.</summary>
        public bool WasPaired { get; private set; }

        public PairingPhase Phase { get; private set; }

        /// <summary>Seconds since <see cref="BeginPairing"/>; 0 outside pairing.</summary>
        public float SecondsTrying
        {
            get { return Phase == PairingPhase.Trying || Phase == PairingPhase.Hint ? (_nowUs - _tryingSinceUs) / 1000000f : 0f; }
        }

        /// <summary>Set by the asset client once the Forge answered; shown on the link panel.</summary>
        public bool ForgeReachable { get; set; }

        public event Action<PairingPhase> PhaseChanged;

        /// <summary>Raised when a new address was stored, with the stored text.</summary>
        public event Action<string> AddressChanged;

        /// <summary>True for the three accepted forms; host is a dotted IPv4 address.</summary>
        public static bool TryParse(string text, out string host, out int port)
        {
            return PairingAddress.TryParse(text, out host, out port);
        }

        /// <summary>
        /// Parses, stores and reconnects: the deep link of the QR code and the typed "Change IP" both end here.
        /// False, and nothing changes, when the text is not accepted.
        /// </summary>
        public bool TrySetAddress(string text)
        {
            string host;
            int port;
            if (!PairingAddress.TryParse(text, out host, out port)) return false;
            _host = host;
            _port = port;
            HasStoredAddress = true;
            _store.SetString(AddressKey, Address);
            _store.Save();
            if (_pump != null) _pump.SetAddress(host, port);
            Action<string> changed = AddressChanged;
            if (changed != null) changed(Address);
            return true;
        }

        /// <summary>
        /// Onboarding step 1: starts the link if needed and tries once per second. The first attempt is what
        /// raises the iOS Local Network prompt, so this is called with the phone in the hand.
        /// </summary>
        public void BeginPairing()
        {
            if (_pump != null)
            {
                _nowUs = _pump.NowUs;
                if (!_pump.IsStarted) _pump.Start();
                else if (_pump.State == LinkState.Offline) _pump.ReconnectNow();
            }
            _tryingSinceUs = _nowUs;
            SetPhase(_pump != null && _pump.State == LinkState.Connected ? PairingPhase.Connected : PairingPhase.Trying);
        }

        /// <summary>"Continue without Station". The link keeps trying quietly in the background.</summary>
        public void Skip()
        {
            if (Phase != PairingPhase.Connected) SetPhase(PairingPhase.Skipped);
        }

        /// <summary>Advances the phase. Called once per frame by the link driver with the link clock.</summary>
        public void Tick(long nowUs, LinkState link)
        {
            _nowUs = nowUs;
            if (link == LinkState.Connected)
            {
                if (!WasPaired)
                {
                    WasPaired = true;
                    _store.SetInt(PairedKey, 1);
                    // A default address that worked is now this phone's Station.
                    if (!HasStoredAddress)
                    {
                        HasStoredAddress = true;
                        _store.SetString(AddressKey, Address);
                    }
                    _store.Save();
                }
                if (Phase == PairingPhase.Trying || Phase == PairingPhase.Hint || Phase == PairingPhase.TimedOut)
                    SetPhase(PairingPhase.Connected);
                return;
            }
            if (Phase != PairingPhase.Trying && Phase != PairingPhase.Hint) return;
            float seconds = (nowUs - _tryingSinceUs) / 1000000f;
            if (seconds >= GiveUpAfterSeconds) SetPhase(PairingPhase.TimedOut);
            else if (seconds >= HintAfterSeconds) SetPhase(PairingPhase.Hint);
        }

        /// <summary>For the link panel: state, address, RTT, loss, last error, and whether the Forge answers.</summary>
        public void GetStatus(out LinkStatus link, out bool forgeReachable)
        {
            link = new LinkStatus { Address = Address };
            if (_pump != null)
            {
                link.State = _pump.State;
                link.RttMs = _pump.Client.RttMs;
                link.LossPermille = _pump.Client.LossPermille;
                link.LastError = _pump.Client.LastByeReason;
            }
            forgeReachable = ForgeReachable;
        }

        void SetPhase(PairingPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            Action<PairingPhase> changed = PhaseChanged;
            if (changed != null) changed(phase);
        }
    }
}
