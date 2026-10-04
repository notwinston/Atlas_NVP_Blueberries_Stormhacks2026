using System;
using System.Globalization;
using System.Net;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>How the Hub is started. Every value has the default of Master Plan Appendix C.7.</summary>
    public sealed class HubOptions
    {
        /// <summary>LiteNetLib listener (UDP). 0 picks a free port.</summary>
        public int UdpPort = LinkProtocol.DefaultPort;

        /// <summary>Kestrel (TCP): Console, HTTP routes and the WebSocket. 0 picks a free port.</summary>
        public int HttpPort = 8080;

        /// <summary>The address Kestrel binds to.</summary>
        public string BindAddress = "0.0.0.0";

        /// <summary>Folder that receives one sub-folder per session. Null means <c>logs</c> in the Hub's folder.</summary>
        public string LogRoot;

        /// <summary>Folder of the preset JSON files (<c>/api/presets</c>). Null means <c>presets</c> in the Hub's folder.</summary>
        public string PresetFolder;

        /// <summary>The Unity project, where Bake writes the default parameter values. Null means <c>ATLAS</c> at the repository root.</summary>
        public string ProjectFolder;

        /// <summary>Console files. Null means <c>wwwroot</c> beside the executable.</summary>
        public string WebRoot;

        /// <summary>
        /// Session id and log folder name. Null (the default) lets the phone name the session in its <c>Hello</c>,
        /// with an id made from the Hub's start time until it does. An id given here is kept whatever the phone says.
        /// </summary>
        public string SessionId;

        /// <summary>The address printed in the pairing QR and sent to clients; null picks the Station's LAN address.</summary>
        public string AdvertiseAddress;

        /// <summary>Where the Hub reaches the Forge for its health check.</summary>
        public string ForgeUrl = "http://127.0.0.1:8000";

        /// <summary>False turns the Forge health check off.</summary>
        public bool ProbeForge = true;

        /// <summary>Start with the demo lock on: the phone then ignores <c>ParamSet</c> (section 7.3.2).</summary>
        public bool DemoLock;

        /// <summary>No status line once a second and no start-up banner.</summary>
        public bool Quiet;

        /// <summary>A transport other than LiteNetLib (tests run the Hub on the in-memory network).</summary>
        public ILinkTransport Transport;

        /// <summary>A clock other than the stopwatch (tests).</summary>
        public ILinkClock Clock;

        /// <summary>
        /// True leaves the link loop to the caller, who calls <see cref="HubServer.Pump"/>. Used with a manual
        /// clock, where time moves only when a test says so.
        /// </summary>
        public bool ManualPump;

        public bool ShowHelp;

        public const string Usage =
            "atlas-hub: the ATLAS Station hub (link relay, logs, Console web server)\n" +
            "\n" +
            "  dotnet run --project station/hub -- [options]\n" +
            "\n" +
            "  --udp <port>        LiteNetLib port for phone, Spectators and Editors   (default 9050)\n" +
            "  --http <port>       HTTP and WebSocket port for the Console             (default 8080)\n" +
            "  --bind <ip>         address the web server binds to                     (default 0.0.0.0)\n" +
            "  --logs <dir>        log root; one folder per session below it           (default station/hub/logs)\n" +
            "  --presets <dir>     folder of parameter presets                         (default station/hub/presets)\n" +
            "  --project <dir>     Unity project that Bake writes defaults.json into   (default ATLAS)\n" +
            "  --wwwroot <dir>     Console files                                       (default wwwroot beside the Hub)\n" +
            "  --session <id>      fixed session id and log folder                     (default: the phone's, else the start time)\n" +
            "  --advertise <ip>    Station address shown in the pairing QR             (default the LAN address)\n" +
            "  --forge <url>       Forge base URL for the health check                 (default http://127.0.0.1:8000)\n" +
            "  --no-forge          do not poll the Forge\n" +
            "  --demo-lock         start with the demo lock on\n" +
            "  --quiet             no status line\n" +
            "  --help              this text\n";

        /// <summary>Reads the command line. False with a readable <paramref name="error"/> when it is wrong.</summary>
        public static bool TryParse(string[] args, out HubOptions options, out string error)
        {
            options = new HubOptions();
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--help": case "-h": case "-?": options.ShowHelp = true; continue;
                    case "--no-forge": options.ProbeForge = false; continue;
                    case "--demo-lock": options.DemoLock = true; continue;
                    case "--quiet": options.Quiet = true; continue;
                }

                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "unexpected argument '" + a + "'";
                    return false;
                }
                if (i + 1 >= args.Length)
                {
                    error = "option " + a + " needs a value";
                    return false;
                }
                string v = args[++i];
                switch (a)
                {
                    case "--udp":
                    case "--udp-port":
                        if (!TryPort(v, out options.UdpPort)) { error = a + ": '" + v + "' is not a port"; return false; }
                        break;
                    case "--http":
                    case "--http-port":
                        if (!TryPort(v, out options.HttpPort)) { error = a + ": '" + v + "' is not a port"; return false; }
                        break;
                    case "--bind":
                        if (!IPAddress.TryParse(v, out _)) { error = "--bind: '" + v + "' is not an IP address"; return false; }
                        options.BindAddress = v;
                        break;
                    case "--advertise":
                        if (!IPAddress.TryParse(v, out _)) { error = "--advertise: '" + v + "' is not an IP address"; return false; }
                        options.AdvertiseAddress = v;
                        break;
                    case "--logs": options.LogRoot = v; break;
                    case "--presets": options.PresetFolder = v; break;
                    case "--project": options.ProjectFolder = v; break;
                    case "--wwwroot": options.WebRoot = v; break;
                    case "--session":
                        if (!Names.IsSafe(v)) { error = "--session: use letters, digits, '-', '_' and '.' only"; return false; }
                        options.SessionId = v;
                        break;
                    case "--forge":
                        if (!Uri.TryCreate(v, UriKind.Absolute, out Uri uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                        {
                            error = "--forge: '" + v + "' is not an http URL";
                            return false;
                        }
                        options.ForgeUrl = v.TrimEnd('/');
                        break;
                    default:
                        error = "unknown option " + a;
                        return false;
                }
            }
            return true;
        }

        static bool TryPort(string text, out int port)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port >= 0 && port <= 65535;
        }
    }

    /// <summary>
    /// Where the Hub keeps its files when the command line does not say. Run from its project
    /// (<c>dotnet run --project station/hub</c>, from any directory) the Hub finds its own folder and uses
    /// <c>station/hub/logs</c>, <c>station/hub/presets</c> and the repository's <c>ATLAS</c> project. A published
    /// copy, which has no project file above it, uses the current directory.
    /// </summary>
    public static class HubPaths
    {
        /// <summary>The folder holding <c>Hub.csproj</c> above the running binary, or null.</summary>
        public static string ProjectFolder()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; dir != null && depth < 6; depth++, dir = dir.Parent)
                if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Hub.csproj"))) return dir.FullName;
            return null;
        }

        /// <summary>Fills every folder the options left open.</summary>
        public static void Resolve(HubOptions options)
        {
            string home = ProjectFolder() ?? System.IO.Directory.GetCurrentDirectory();
            if (options.LogRoot == null) options.LogRoot = System.IO.Path.Combine(home, "logs");
            if (options.PresetFolder == null) options.PresetFolder = System.IO.Path.Combine(home, "presets");
            if (options.ProjectFolder == null) options.ProjectFolder = System.IO.Path.GetFullPath(System.IO.Path.Combine(home, "..", "..", "ATLAS"));
        }
    }

    /// <summary>Names that become file or folder names: sessions and presets.</summary>
    public static class Names
    {
        /// <summary>1 to 64 of letters, digits, '-', '_' and '.', not starting with a dot: never a path.</summary>
        public static bool IsSafe(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 || name[0] == '.') return false;
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>A preset name: 1 to 40 of letters, digits, '-' and '_' (lane D contract 3.3).</summary>
        public static bool IsPreset(string name)
        {
            return IsSafe(name) && name.Length <= 40 && name.IndexOf('.') < 0;
        }
    }
}
