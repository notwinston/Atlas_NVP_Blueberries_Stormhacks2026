using System;
using System.Globalization;
using System.Net;
using Atlas.Link;

namespace Atlas.Tools
{
    /// <summary>How ReplayPhone is run.</summary>
    public sealed class ReplayOptions
    {
        /// <summary>The Hub's address: an IPv4 literal, as on the phone.</summary>
        public string Address = "127.0.0.1";

        public int Port = LinkProtocol.DefaultPort;

        /// <summary>
        /// A recorded <c>state.bin</c>, or the session folder that holds one; null plays the synthetic walk. The
        /// folder's <c>manifest.json</c>, <c>outline.bin</c> and <c>events.jsonl</c> are used when present.
        /// </summary>
        public string File;

        /// <summary>Stop after this long; 0 plays a recording once through and the synthetic walk until stopped.</summary>
        public double Seconds;

        /// <summary>Start a recording again when it ends.</summary>
        public bool Loop;

        /// <summary>Rate of the synthetic walk. A recording keeps its own timing.</summary>
        public int Hz = 60;

        /// <summary>Playback speed of a recording.</summary>
        public double Speed = 1.0;

        /// <summary>Give up when the Hub has not answered after this long.</summary>
        public double ConnectTimeoutSeconds = 10.0;

        /// <summary>Send a <c>Telemetry</c> row once a second, as the phone's perf probe does.</summary>
        public bool Telemetry = true;

        public bool Quiet;
        public bool ShowHelp;

        public const string Usage =
            "ReplayPhone: plays a recorded state.bin, or a synthetic walk, into a Hub as the phone\n" +
            "\n" +
            "  dotnet run --project tools/ReplayPhone -- [session folder or state.bin] [options]\n" +
            "\n" +
            "  recording           logs/<session>/ or its state.bin; without one a synthetic walk is played\n" +
            "  --hub <ip[:port]>   Hub address                                (default 127.0.0.1:9050)\n" +
            "  --port <n>          Hub UDP port, when not given with --hub    (default 9050)\n" +
            "  --seconds <s>       stop after s seconds                       (default: recording once, walk for ever)\n" +
            "  --loop              start the recording again when it ends\n" +
            "  --speed <x>         playback speed of a recording              (default 1)\n" +
            "  --hz <n>            State rate of the synthetic walk, 1 to 60  (default 60)\n" +
            "  --no-telemetry      send no Telemetry rows\n" +
            "  --quiet             print nothing but errors\n" +
            "  --help              this text\n";

        public static bool TryParse(string[] args, out ReplayOptions options, out string error)
        {
            options = new ReplayOptions();
            error = null;
            CultureInfo inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--help": case "-h": case "-?": options.ShowHelp = true; continue;
                    case "--loop": options.Loop = true; continue;
                    case "--quiet": options.Quiet = true; continue;
                    case "--no-telemetry": options.Telemetry = false; continue;
                }
                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    if (options.File != null) { error = "only one recording can be played"; return false; }
                    options.File = a;
                    continue;
                }
                if (i + 1 >= args.Length) { error = "option " + a + " needs a value"; return false; }
                string v = args[++i];
                switch (a)
                {
                    case "--hub":
                    {
                        // An IPv4 literal with an optional port, as the phone's pairing code accepts it. No host names.
                        string host = v;
                        int colon = v.LastIndexOf(':');
                        if (colon > 0)
                        {
                            host = v.Substring(0, colon);
                            if (!int.TryParse(v.Substring(colon + 1), NumberStyles.None, inv, out options.Port) || options.Port < 1 || options.Port > 65535)
                            { error = "--hub: '" + v + "' has a bad port"; return false; }
                        }
                        if (!IPAddress.TryParse(host, out IPAddress ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        { error = "--hub: '" + v + "' is not an IPv4 address"; return false; }
                        options.Address = host;
                        break;
                    }
                    case "--port":
                        if (!int.TryParse(v, NumberStyles.None, inv, out options.Port) || options.Port < 1 || options.Port > 65535)
                        { error = "--port: '" + v + "' is not a port"; return false; }
                        break;
                    case "--seconds":
                        if (!double.TryParse(v, NumberStyles.Float, inv, out options.Seconds) || options.Seconds < 0)
                        { error = "--seconds: '" + v + "' is not a duration"; return false; }
                        break;
                    case "--speed":
                        if (!double.TryParse(v, NumberStyles.Float, inv, out options.Speed) || options.Speed <= 0)
                        { error = "--speed: '" + v + "' is not a positive number"; return false; }
                        break;
                    case "--hz":
                        if (!int.TryParse(v, NumberStyles.None, inv, out options.Hz) || options.Hz < 1 || options.Hz > 60)
                        { error = "--hz: '" + v + "' is not between 1 and 60"; return false; }
                        break;
                    default:
                        error = "unknown option " + a;
                        return false;
                }
            }
            return true;
        }
    }
}
