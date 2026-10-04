using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>What the Forge said at its last health check (Master Plan Appendix B.4, <c>GET /health</c>).</summary>
    public sealed class ForgeStatus
    {
        public static readonly ForgeStatus Down = new ForgeStatus();

        public bool Up;
        public bool Gpu;
        public int Queue;
        public bool DebugKeep;
        /// <summary>The hosted fallback is armed (lane D contract 3.10).</summary>
        public bool Hosted;
        public string PipelineVer = string.Empty;

        public JsonValue ToJson()
        {
            return JsonValue.Object().Set("up", Up).Set("queue", Queue).Set("debugKeep", DebugKeep).Set("hosted", Hosted).Set("gpu", Gpu).Set("pipelineVer", PipelineVer);
        }

        public bool SameAs(ForgeStatus other)
        {
            return other != null && Up == other.Up && Gpu == other.Gpu && Queue == other.Queue && DebugKeep == other.DebugKeep && Hosted == other.Hosted &&
                   PipelineVer == other.PipelineVer;
        }
    }

    /// <summary>
    /// Asks the Forge for its health every two seconds on a pool thread (section 7.1), so that
    /// <c>/api/session</c> and the Console can show whether it is up, its queue and the "crops kept" flag.
    /// </summary>
    public sealed class ForgeProbe : IDisposable
    {
        static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

        readonly HttpClient _http;
        readonly string _healthUrl;
        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        volatile ForgeStatus _status = ForgeStatus.Down;
        Task _loop;

        public ForgeProbe(string forgeUrl)
        {
            _healthUrl = forgeUrl.TrimEnd('/') + "/health";
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        }

        public ForgeStatus Status { get { return _status; } }

        /// <summary>Raised on a pool thread when the answer changes.</summary>
        public event Action<ForgeStatus> Changed;

        public void Start() { _loop = Task.Run(RunAsync); }

        async Task RunAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                ForgeStatus next = await ProbeOnceAsync().ConfigureAwait(false);
                if (!next.SameAs(_status))
                {
                    _status = next;
                    Action<ForgeStatus> changed = Changed;
                    if (changed != null) changed(next);
                }
                try { await Task.Delay(Interval, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>
        /// Reads the body of the Forge's <c>GET /health</c>. Anything that is not a JSON object is "down". The
        /// hosted flag is <c>hosted</c> (lane D contract 3.6); a Forge that still names it
        /// <c>hostedEnabled</c> is understood too, so the disclosure banner cannot be lost to a field name.
        /// </summary>
        public static ForgeStatus Parse(string body)
        {
            JsonValue json;
            if (body == null || !AtlasJson.TryParse(body, out json) || !json.IsObject) return ForgeStatus.Down;
            return new ForgeStatus
            {
                Up = json.GetBool("ok", true),
                Gpu = json.GetBool("gpu"),
                Queue = json.GetInt("queue"),
                DebugKeep = json.GetBool("debugKeep"),
                Hosted = json.GetBool("hosted") || json.GetBool("hostedEnabled"),
                PipelineVer = json.GetString("pipelineVer", string.Empty) ?? string.Empty
            };
        }

        async Task<ForgeStatus> ProbeOnceAsync()
        {
            try
            {
                using (HttpResponseMessage response = await _http.GetAsync(_healthUrl, _stop.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return ForgeStatus.Down;
                    return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            catch (Exception)
            {
                // Refused, timed out or cancelled: the Forge is not reachable, which is a normal state (section 7.5, rule 4).
                return ForgeStatus.Down;
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { if (_loop != null) _loop.Wait(2000); } catch (AggregateException) { }
            _http.Dispose();
        }
    }
}
