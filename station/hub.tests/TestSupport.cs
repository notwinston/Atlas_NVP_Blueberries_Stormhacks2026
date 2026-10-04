using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>A Hub in this process on ports the system picks, with its logs in a folder of its own.</summary>
    sealed class TestHub : IAsyncDisposable
    {
        public HubServer Hub { get; private set; }
        public HttpClient Http { get; private set; }
        public string Folder { get; private set; }

        public static async Task<TestHub> StartAsync(Action<HubOptions> configure = null, string folder = null)
        {
            var test = new TestHub();
            test.Folder = folder ?? Path.Combine(Path.GetTempPath(), "atlas-hub-tests", Guid.NewGuid().ToString("N"));
            var options = new HubOptions
            {
                UdpPort = 0,
                HttpPort = 0,
                BindAddress = "127.0.0.1",
                AdvertiseAddress = "127.0.0.1",
                LogRoot = Path.Combine(test.Folder, "logs"),
                PresetFolder = Path.Combine(test.Folder, "presets"),
                ProjectFolder = Path.Combine(test.Folder, "ATLAS"),
                ProbeForge = false,
                Quiet = true
            };
            Directory.CreateDirectory(Path.Combine(options.ProjectFolder, "Assets"));   // stands in for the Unity project (Bake)
            if (configure != null) configure(options);
            test.Hub = new HubServer(options);
            await test.Hub.StartAsync();
            test.Http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + test.Hub.HttpPort), Timeout = TimeSpan.FromSeconds(10) };
            return test;
        }

        public async Task<JsonValue> GetJson(string path, int expectStatus = 200)
        {
            using (HttpResponseMessage response = await Http.GetAsync(path))
            {
                string text = await response.Content.ReadAsStringAsync();
                Assert.That((int)response.StatusCode, Is.EqualTo(expectStatus), path + " -> " + text);
                Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"), path);
                return AtlasJson.Parse(text);
            }
        }

        public async Task<JsonValue> PostJson(string path, string body, int expectStatus = 200)
        {
            using (var content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await Http.PostAsync(path, content))
            {
                string text = await response.Content.ReadAsStringAsync();
                Assert.That((int)response.StatusCode, Is.EqualTo(expectStatus), path + " -> " + text);
                return text.Length > 0 ? AtlasJson.Parse(text) : JsonValue.Null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await Hub.DisposeAsync();
            try { Directory.Delete(Folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// A link client over real UDP on 127.0.0.1 with a thread of its own standing in for the app's frame loop:
    /// it polls every millisecond and, when asked, sends <c>State</c> at 60 Hz.
    /// </summary>
    sealed class TestClient : IDisposable
    {
        public readonly LinkClient Client;
        public readonly ParamRegistry Registry;
        public readonly ParamEntry Rho, Copies, Predict, FmsStop;
        public readonly ConcurrentQueue<OperatorCommand> Commands = new ConcurrentQueue<OperatorCommand>();
        public readonly ConcurrentQueue<uint> ReceivedSeqs = new ConcurrentQueue<uint>();
        public readonly ConcurrentQueue<string> Jobs = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<LinkState> States = new ConcurrentQueue<LinkState>();
        /// <summary>What a Spectator is sent beside <c>State</c>: Welcomes, manifests, the phone's events, outlines.</summary>
        public readonly ConcurrentQueue<WelcomeMessage> Welcomes = new ConcurrentQueue<WelcomeMessage>();
        public readonly ConcurrentQueue<ParamManifestMessage> Manifests = new ConcurrentQueue<ParamManifestMessage>();
        public readonly ConcurrentQueue<EventMessage> Events = new ConcurrentQueue<EventMessage>();
        public volatile WelcomeMessage LastWelcome;
        public volatile int Outlines;
        public volatile bool SendStates;
        public volatile bool SendTelemetry;
        public volatile float LastReceivedHeadX;

        readonly Thread _thread;
        readonly ConcurrentQueue<Action<LinkClient>> _work = new ConcurrentQueue<Action<LinkClient>>();
        volatile bool _run = true;
        long _sent;
        volatile bool _ready;

        public long Sent { get { return Interlocked.Read(ref _sent); } }
        public bool IsReady { get { return _ready; } }

        /// <param name="configure">Runs before the client starts: the place to name a session or a world.</param>
        public TestClient(LinkRole role, int port, string device = null, Action<LinkClient> configure = null)
        {
            if (role == LinkRole.Phone)
            {
                Registry = new ParamRegistry();
                Rho = Registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
                Copies = Registry.DeclareInt("glued.copies", "glued", 3, 5, 5);
                Predict = Registry.DeclareBool("pose.gyroPredict", "pose", true);
                FmsStop = Registry.DeclareInt("pg.fmsStop", "playground", 0, 20, 4);
            }
            Client = new LinkClient(new LiteNetTransport(), new StopwatchClock(), role, Registry)
            {
                Device = device ?? "test-" + LinkRoles.Name(role),
                Build = "tests",
                Scene = "S0"
            };
            Client.StateChanged += (previous, current) => States.Enqueue(current);
            Client.StateReceived += OnState;
            Client.JobStatusReceived += m => Jobs.Enqueue("status:" + m.JobId + ":" + m.State);
            Client.AssetReadyReceived += m => Jobs.Enqueue("ready:" + m.JobId + ":" + m.Sha256);
            Client.Welcomed += w => { LastWelcome = w; Welcomes.Enqueue(w); };
            Client.ManifestReceived += m => Manifests.Enqueue(m);
            Client.EventReceived += e => Events.Enqueue(e);
            Client.RoomOutlineReceived += o => Outlines++;
            if (configure != null) configure(Client);
            Assert.That(Client.Start("127.0.0.1", port), Is.True, "the client could not open a UDP socket");
            _thread = new Thread(Loop) { IsBackground = true, Name = "test " + LinkRoles.Name(role) };
            _thread.Start();
        }

        void OnState(in StateMessage state)
        {
            LastReceivedHeadX = state.HeadPosX;
            ReceivedSeqs.Enqueue(state.Seq);
        }

        /// <summary>Runs something on the client's own thread, as app code would from the frame loop.</summary>
        public void Post(Action<LinkClient> action) { _work.Enqueue(action); }

        void Loop()
        {
            var clock = Stopwatch.StartNew();
            long frame = 0;
            double nextTelemetry = 0;
            var state = StateMessage.Identity;
            var telemetry = new TelemetryMessage();
            while (_run)
            {
                Client.Poll();
                _ready = Client.IsReady;
                Action<LinkClient> action;
                while (_work.TryDequeue(out action)) action(Client);
                OperatorCommand command;
                while (Client.TryDequeueCommand(out command)) Commands.Enqueue(command);

                double ms = clock.Elapsed.TotalMilliseconds;
                if (SendStates && Client.IsReady && ms >= frame * (1000.0 / 60.0))
                {
                    state.HeadPosX = frame * 0.01f;
                    state.HeadPosY = 1.6f;
                    state.SpaceId = 2;
                    state.SetTile(0, frame);
                    if (Rho != null) state.SetParam(StateMessage.ParamCullRho, Rho.Float);
                    if (Client.SendState(ref state)) Interlocked.Increment(ref _sent);
                    frame++;
                }
                else if (!SendStates)
                {
                    frame = (long)(ms / (1000.0 / 60.0)) + 1;
                }
                if (SendTelemetry && Client.IsReady && ms >= nextTelemetry)
                {
                    nextTelemetry = ms + 200;
                    telemetry = new TelemetryMessage { Fps = 59.5f, P95 = 16.9f, Thermal = 1, DrawCalls = 40, Battery = 87 };
                    Client.SendTelemetry(ref telemetry);
                }
                Thread.Sleep(1);
            }
            Client.Stop("test finished");
        }

        public void Dispose()
        {
            _run = false;
            _thread.Join(3000);
        }
    }

    static class Wait
    {
        /// <summary>Polls until the condition holds; fails the test with <paramref name="what"/> when it never does.</summary>
        public static void Until(Func<bool> condition, string what, int timeoutMs = 5000)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return;
                Thread.Sleep(5);
            }
            Assert.That(condition(), Is.True, "timed out after " + timeoutMs + " ms waiting for: " + what);
        }

        public static void Ready(TestClient client, int timeoutMs = 6000)
        {
            Until(() => client.IsReady, client.Client.Role + " handshake", timeoutMs);
        }

        public static List<T> Drain<T>(ConcurrentQueue<T> queue)
        {
            var list = new List<T>();
            T item;
            while (queue.TryDequeue(out item)) list.Add(item);
            return list;
        }
    }
}
