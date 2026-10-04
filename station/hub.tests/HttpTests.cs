using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>Every route of Master Plan Appendix B.3, against a Hub with a phone connected over UDP.</summary>
    [TestFixture]
    public class HttpTests
    {
        TestHub _t;
        TestClient _phone;

        [SetUp]
        public async Task SetUp()
        {
            _t = await TestHub.StartAsync();
            _phone = new TestClient(LinkRole.Phone, _t.Hub.UdpPort, "test-iphone");
            Wait.Ready(_phone);
            Wait.Until(() => _t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");
        }

        [TearDown]
        public async Task TearDown()
        {
            _phone.Dispose();
            await _t.DisposeAsync();
        }

        [Test]
        public async Task Root_ServesTheConsolePage()
        {
            using (HttpResponseMessage response = await _t.Http.GetAsync("/"))
            {
                Assert.That((int)response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo("text/html"));
                string html = await response.Content.ReadAsStringAsync();
                Assert.That(html, Does.Contain("ATLAS Hub"));
                Assert.That(html, Does.Contain("/ws"));
            }
        }

        [Test]
        public async Task Pair_ShowsAQrOfTheStationAddress()
        {
            string url = "atlas://127.0.0.1:" + _t.Hub.UdpPort;
            string html = await _t.Http.GetStringAsync("/pair");
            Assert.That(html, Does.Contain("<svg"));
            Assert.That(html, Does.Contain(url));

            string svg = await _t.Http.GetStringAsync("/pair/qr.svg");
            Assert.That(svg, Does.StartWith("<svg"));
            Assert.That(svg, Is.EqualTo(PairPage.Svg(url)));

            string other = await _t.Http.GetStringAsync("/pair?ip=192.168.1.10");
            Assert.That(other, Does.Contain("atlas://192.168.1.10:" + _t.Hub.UdpPort));

            JsonValue pair = await _t.GetJson("/api/pair");
            Assert.That(pair.GetString("url"), Is.EqualTo(url));
            Assert.That(pair.GetInt("udpPort"), Is.EqualTo(_t.Hub.UdpPort));
            Assert.That(pair["candidates"].IsArray, Is.True);
        }

        [Test]
        public async Task Session_HasTheDocumentedShape()
        {
            JsonValue s = await _t.GetJson("/api/session");
            Assert.That(s.GetString("sessionId"), Is.EqualTo(_t.Hub.SessionId));
            Assert.That(s["phone"].GetString("state"), Is.EqualTo("connected"));
            Assert.That(s["phone"]["rttMs"].IsNumber, Is.True);
            Assert.That(s["phone"]["loss"].IsNumber, Is.True);
            Assert.That(s["phone"].GetString("device"), Is.EqualTo("test-iphone"));
            Assert.That(s["spectators"].IsNumber, Is.True);
            Assert.That(s.GetInt("spectators"), Is.EqualTo(0));
            Assert.That(s["forge"]["up"].IsBool, Is.True);
            Assert.That(s["forge"]["queue"].IsNumber, Is.True);
            Assert.That(s["forge"]["debugKeep"].IsBool, Is.True);
            Assert.That(s["forge"]["hosted"].IsBool, Is.True);
            Assert.That(s["sources"][0].GetString("role"), Is.EqualTo("phone"));
            Assert.That(s["sources"][0].GetBool("active"), Is.True);
            Assert.That(s.GetBool("demoLock"), Is.False);
        }

        [Test]
        public async Task Params_ListAndGet_ComeFromThePhonesManifest()
        {
            Wait.Until(() => _t.Hub.GetParams()["params"].Count == 4, "the manifest");
            JsonValue list = await _t.GetJson("/api/params");
            Assert.That(list["params"].Count, Is.EqualTo(4));
            JsonValue rho = list["params"][0];
            foreach (string key in new[] { "id", "name", "group", "min", "max", "def", "val", "onRelease", "kind", "active" })
                Assert.That(rho.Has(key), Is.True, key);
            Assert.That(rho.GetString("name"), Is.EqualTo("curved.cullRho"));
            Assert.That(rho.GetFloat("min"), Is.EqualTo(2f));
            Assert.That(rho.GetFloat("max"), Is.EqualTo(3.5f));
            Assert.That(rho.GetFloat("val"), Is.EqualTo(3f));

            JsonValue byName = await _t.GetJson("/api/params/glued.copies");
            Assert.That(byName.GetString("kind"), Is.EqualTo("int"));
            JsonValue byId = await _t.GetJson("/api/params/" + byName.GetInt("id"));
            Assert.That(byId.GetString("name"), Is.EqualTo("glued.copies"));

            JsonValue missing = await _t.GetJson("/api/params/no.such", 404);
            Assert.That(missing.GetString("error"), Is.Not.Empty);
        }

        [Test]
        public async Task ParamSet_FromHttp_ReachesThePhone_IsClamped_AndIsEchoed()
        {
            Wait.Until(() => _t.Hub.GetParams()["params"].Count == 4, "the manifest");

            JsonValue applied = await _t.PostJson("/api/params/curved.cullRho", "{\"value\": 2.5}");
            Assert.That(applied.GetString("name"), Is.EqualTo("curved.cullRho"));
            Assert.That(applied.GetFloat("value"), Is.EqualTo(2.5f));
            Assert.That(applied.GetBool("sent"), Is.True);
            Wait.Until(() => _phone.Rho.Float == 2.5f, "the phone to apply 2.5");

            // The phone echoes what it applied as an Event param; the Hub logs it and serves it.
            Wait.Until(() => LogFile.ReadShared(Path.Combine(_t.Hub.Log.Folder, SessionLog.EventsFile)).Contains("\"source\":\"remote\""), "the echo in events.jsonl");
            string events = LogFile.ReadShared(Path.Combine(_t.Hub.Log.Folder, SessionLog.EventsFile));
            EventMessage echo = null;
            foreach (string line in events.Split('\n'))
            {
                EventMessage e;
                if (EventMessage.TryParseLine(line, out e) && e.Ev == LinkEvents.Param) echo = e;
            }
            Assert.That(echo, Is.Not.Null);
            Assert.That(echo.Data.GetString("name"), Is.EqualTo("curved.cullRho"));
            Assert.That(echo.Data.GetFloat("value"), Is.EqualTo(2.5f));

            // Out of range: the phone clamps to [2, 3.5] and the Hub reports what the phone holds.
            JsonValue clamped = await _t.PostJson("/api/params/curved.cullRho", "{\"value\": 99}");
            Assert.That(clamped.GetFloat("value"), Is.EqualTo(3.5f));
            Wait.Until(() => _phone.Rho.Float == 3.5f, "the phone to clamp to 3.5");
            Assert.That((await _t.GetJson("/api/params/curved.cullRho")).GetFloat("val"), Is.EqualTo(3.5f));

            // By wire id, with PUT, and a boolean.
            int id = (await _t.GetJson("/api/params/pose.gyroPredict")).GetInt("id");
            using (var body = new StringContent("{\"value\": false}", Encoding.UTF8, "application/json"))
            using (HttpResponseMessage put = await _t.Http.PutAsync("/api/params/" + id, body))
                Assert.That((int)put.StatusCode, Is.EqualTo(200));
            Wait.Until(() => !_phone.Predict.Bool, "the phone to apply the boolean");

            // Several at once, as a preset does: one ParamSet.
            JsonValue many = await _t.PostJson("/api/params", "{\"values\": {\"curved.cullRho\": 2.25, \"glued.copies\": 4, \"no.such\": 1}}");
            Assert.That(many.GetInt("sent"), Is.EqualTo(2));
            Wait.Until(() => _phone.Rho.Float == 2.25f && _phone.Copies.Int == 4, "the phone to apply both");

            await _t.PostJson("/api/params/no.such", "{\"value\": 1}", 404);
            await _t.PostJson("/api/params/curved.cullRho", "{\"value\": \"high\"}", 400);
            await _t.PostJson("/api/params/curved.cullRho", "{broken", 400);
        }

        [Test]
        public async Task Seq_IsValidatedAndLoggedByTheHub()
        {
            foreach (string bad in new[] { "0", "8", "3.5", "-1", "\"4\"", "null" })
                Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"seq\",\"score\":" + bad + "}")).GetString("t"), Is.EqualTo("error"), bad);
            Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"seq\"}")).GetString("t"), Is.EqualTo("error"));
            Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"seq\",\"score\":1}")), Is.Null);
            Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"seq\",\"score\":7}")), Is.Null);
            string events = await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/events.jsonl");
            string[] seq = Array.FindAll(events.Split('\n', StringSplitOptions.RemoveEmptyEntries), l => l.Contains("\"ev\":\"seq\""));
            Assert.That(seq.Length, Is.EqualTo(2), "only the two valid entries");
            foreach (string line in seq)
            {
                EventMessage e;
                Assert.That(EventMessage.TryParseLine(line, out e), Is.True, line);
                Assert.That(e.Scene, Is.EqualTo("hub"));
            }
            Assert.That(seq[0], Does.Contain("\"score\":1"));
            Assert.That(seq[1], Does.Contain("\"score\":7"));
        }

        [Test]
        public async Task Fms_AtOrAboveFmsStop_LogsAnAlert_UsingThePhonesValue()
        {
            Wait.Until(() => _t.Hub.GetParams()["params"].Count == 4, "the manifest");
            // The phone declares pg.fmsStop = 4: 3 is quiet, 4 and 9 alert.
            _t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"fms\",\"score\":3}"));
            _t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"fms\",\"score\":4}"));
            // The operator moves the threshold from the Console; the next entry is judged against the new value.
            JsonValue applied = await _t.PostJson("/api/params/pg.fmsStop", "{\"value\": 10}");
            Assert.That(applied.GetFloat("value"), Is.EqualTo(10f));
            Wait.Until(() => _phone.FmsStop.Int == 10, "the phone to apply 10");
            Wait.Until(() => _t.Hub.GetParams()["params"][3].GetFloat("val") == 10f, "the Hub to know 10");
            _t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"fms\",\"score\":9}"));
            _t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"fms\",\"score\":10}"));

            string events = await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/events.jsonl");
            string[] lines = events.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string[] fms = Array.FindAll(lines, l => l.Contains("\"ev\":\"fms\""));
            string[] alerts = Array.FindAll(lines, l => l.Contains("\"ev\":\"pg.fmsAlert\""));
            Assert.That(fms.Length, Is.EqualTo(4), "one fms line per entry, all written by the Hub");
            Assert.That(alerts.Length, Is.EqualTo(2));
            Assert.That(alerts[0], Does.Contain("\"score\":4").And.Contain("\"stop\":4").And.Contain("\"scene\":\"hub\""));
            Assert.That(alerts[1], Does.Contain("\"score\":10").And.Contain("\"stop\":10"));
        }

        [Test]
        public async Task ParamSet_UnderDemoLock_IsRefused_AndThePhoneKeepsItsValue()
        {
            Wait.Until(() => _t.Hub.GetParams()["params"].Count == 4, "the manifest");
            await _t.PostJson("/api/op", "{\"cmd\": \"lock\", \"args\": {\"on\": true}}");
            Wait.Until(() => _phone.Commands.Count == 1, "the lock to reach the phone");
            Assert.That((await _t.GetJson("/api/session")).GetBool("demoLock"), Is.True);

            JsonValue refused = await _t.PostJson("/api/params/curved.cullRho", "{\"value\": 2.5}", 423);
            Assert.That(refused.GetString("error"), Does.Contain("lock"));
            Thread.Sleep(200);
            Assert.That(_phone.Rho.Float, Is.EqualTo(3f));

            await _t.PostJson("/api/op", "{\"cmd\": \"lock\", \"args\": {\"on\": false}}");
            await _t.PostJson("/api/params/curved.cullRho", "{\"value\": 2.5}");
            Wait.Until(() => _phone.Rho.Float == 2.5f, "the phone to apply the value once unlocked");
        }

        [Test]
        public async Task EachOperatorCommand_ReachesThePhone()
        {
            await _t.PostJson("/api/scene", "{\"space\": \"S3\", \"variant\": \"corner\"}");
            await _t.PostJson("/api/op", "{\"cmd\": \"home\"}");
            await _t.PostJson("/api/op", "{\"cmd\": \"recentre\"}");
            await _t.PostJson("/api/op", "{\"cmd\": \"quality\", \"args\": {\"tier\": 2}}");
            await _t.PostJson("/api/op", "{\"cmd\": \"cue\"}");
            await _t.PostJson("/api/op", "{\"cmd\": \"lock\", \"args\": {\"on\": true}}");

            Wait.Until(() => _phone.Commands.Count == 6, "six commands at the phone");
            List<OperatorCommand> got = Wait.Drain(_phone.Commands);
            Assert.That(got[0].Kind, Is.EqualTo(OperatorCommandKind.SwitchSpace));
            Assert.That(got[0].Space, Is.EqualTo(3));
            Assert.That(got[0].Variant, Is.EqualTo("corner"));
            Assert.That(got[1].Kind, Is.EqualTo(OperatorCommandKind.Home));
            Assert.That(got[2].Kind, Is.EqualTo(OperatorCommandKind.Recentre));
            Assert.That(got[3].Kind, Is.EqualTo(OperatorCommandKind.Quality));
            Assert.That(got[3].Tier, Is.EqualTo(2));
            Assert.That(got[4].Kind, Is.EqualTo(OperatorCommandKind.Cue));
            Assert.That(got[5].Kind, Is.EqualTo(OperatorCommandKind.Lock));
            Assert.That(got[5].On, Is.True);

            // A space given as a number, and the requests the Hub must refuse.
            await _t.PostJson("/api/scene", "{\"space\": 5}");
            Wait.Until(() => _phone.Commands.Count == 1, "the numeric space");
            Assert.That(Wait.Drain(_phone.Commands)[0].Space, Is.EqualTo(5));
            await _t.PostJson("/api/op", "{\"cmd\": \"panic\"}", 404);
            await _t.PostJson("/api/scene", "{\"space\": \"S9\"}", 404);
            await _t.PostJson("/api/op", "", 400);
        }

        [Test]
        public async Task Commands_WithNoPhone_AreRefusedWith409()
        {
            await using (TestHub alone = await TestHub.StartAsync())
            {
                JsonValue s = await alone.GetJson("/api/session");
                Assert.That(s["phone"].GetString("state"), Is.EqualTo("offline"));
                JsonValue refused = await alone.PostJson("/api/op", "{\"cmd\": \"cue\"}", 409);
                Assert.That(refused.GetString("error"), Does.Contain("no phone"));
                await alone.PostJson("/api/scene", "{\"space\": \"S1\"}", 409);
                await alone.GetJson("/api/state", 404);
                await alone.GetJson("/api/telemetry", 404);
                Assert.That((await alone.GetJson("/api/params"))["params"].Count, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task StateAndTelemetry_AreServedAndLogged()
        {
            _phone.SendStates = true;
            _phone.SendTelemetry = true;
            Wait.Until(() => _t.Hub.StatesReceived > 30 && _t.Hub.TelemetryRows >= 2, "State and Telemetry at the Hub");

            JsonValue state = await _t.GetJson("/api/state");
            foreach (string key in new[] { "seq", "t_us", "scene", "space", "tracking", "jump", "cue", "tier", "lock", "bump", "tile", "sheet",
                                           "head", "yawDeg", "p", "worldVer", "hz", "lossPct" })
                Assert.That(state.Has(key), Is.True, key);
            Assert.That(state.GetInt("space"), Is.EqualTo(2));
            Assert.That(state["head"].Count, Is.EqualTo(3));
            Assert.That(state["head"][1].AsFloat(), Is.EqualTo(1.6f));
            Assert.That(state["tile"].IsString, Is.True, "64-bit tile ids travel as text");
            Assert.That(state.GetString("tile").Split(',').Length, Is.EqualTo(6));
            Assert.That(state.GetDouble("yawDeg"), Is.EqualTo(0.0), "the identity rotation looks along +Z");
            Assert.That(state.GetDouble("lossPct"), Is.EqualTo(0.0));
            Assert.That(state["p"].Count, Is.EqualTo(8));
            Assert.That(state["p"][StateMessage.ParamCullRho].AsFloat(), Is.EqualTo(3f));

            JsonValue row = await _t.GetJson("/api/telemetry");
            Assert.That(row.GetFloat("fps"), Is.EqualTo(59.5f));
            Assert.That(row.GetInt("battery"), Is.EqualTo(87));
            foreach (string column in TelemetryMessage.CsvHeader.Split(',')) Assert.That(row.Has(column), Is.True, column);

            JsonValue sessions = await _t.GetJson("/api/sessions");
            Assert.That(sessions.GetString("current"), Is.EqualTo(_t.Hub.SessionId));
            Assert.That(sessions["sessions"][0].AsString(), Is.EqualTo(_t.Hub.SessionId));
            JsonValue files = await _t.GetJson("/logs/" + _t.Hub.SessionId);
            Assert.That(files["files"].Count, Is.EqualTo(6), "perf.csv, events.jsonl, state.bin, phone.log, hub.log, manifest.json");

            string csv = await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/perf.csv");
            string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines[0], Is.EqualTo(TelemetryMessage.CsvHeader));
            Assert.That(lines.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(lines[1].Split(',').Length, Is.EqualTo(34));
            Assert.That(lines[1].Split(',')[5], Is.EqualTo("59.500"));
            Assert.That(csv, Does.Not.Contain("\r"));
            Assert.That(await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/telemetry.csv"), Does.StartWith(TelemetryMessage.CsvHeader));

            string hubLog = await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/hub.log");
            Assert.That(hubLog, Does.Contain(" start {"));
            Assert.That(hubLog, Does.Contain(" join {"));
            JsonValue manifest = AtlasJson.Parse(await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/manifest.json"));
            Assert.That(manifest["params"].Count, Is.EqualTo(4));

            // A comfort score and a note from the Console land in events.jsonl as Hub events.
            Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"fms\",\"score\":3}")), Is.Null);
            Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"note\",\"text\":\"walker turned left\"}")), Is.Null);
            Assert.That(_t.Hub.HandleConsoleMessage(AtlasJson.Parse("{\"t\":\"fms\",\"score\":21}")).GetString("t"), Is.EqualTo("error"));
            string events = await _t.Http.GetStringAsync("/logs/" + _t.Hub.SessionId + "/events.jsonl");
            Assert.That(events, Does.Contain("\"ev\":\"fms\"").And.Contain("\"score\":3"));
            Assert.That(events, Does.Contain("walker turned left"));
            foreach (string line in events.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                EventMessage e;
                Assert.That(EventMessage.TryParseLine(line, out e), Is.True, line);
            }

            byte[] bin = await _t.Http.GetByteArrayAsync("/logs/" + _t.Hub.SessionId + "/state.bin");
            Assert.That(bin.Length, Is.GreaterThan(StateLog.HeaderSize + 30 * StateLog.RecordSize));
            Assert.That((bin.Length - StateLog.HeaderSize) % StateLog.RecordSize, Is.EqualTo(0));
            Assert.That(Encoding.ASCII.GetString(bin, 0, 8), Is.EqualTo("ATLSTATE"));

            using (HttpResponseMessage r = await _t.Http.GetAsync("/logs/" + _t.Hub.SessionId + "/secrets.txt"))
                Assert.That((int)r.StatusCode, Is.EqualTo(404));
            using (HttpResponseMessage r = await _t.Http.GetAsync("/logs/..%2F..%2Fetc/perf.csv"))
                Assert.That((int)r.StatusCode, Is.EqualTo(404));
            using (HttpResponseMessage r = await _t.Http.GetAsync("/logs/nope/perf.csv"))
                Assert.That((int)r.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task Presets_SaveListLoad()
        {
            Wait.Until(() => _t.Hub.GetParams()["params"].Count == 4, "the manifest");
            Assert.That((await _t.GetJson("/api/presets"))["presets"].Count, Is.EqualTo(0));

            await _t.PostJson("/api/presets/demo", "{\"values\": {\"curved.cullRho\": 2.75, \"glued.copies\": 3}}", 204);
            // An empty body saves what the phone holds now.
            await _t.PostJson("/api/presets/current", "", 204);
            await _t.PostJson("/api/presets/bad%20name", "{\"values\": {}}", 400);
            await _t.PostJson("/api/presets/bad.name", "{\"values\": {}}", 400);
            await _t.PostJson("/api/presets/demo2", "{\"vals\": {}}", 400);

            JsonValue list = await _t.GetJson("/api/presets");
            Assert.That(list["presets"].Count, Is.EqualTo(2));
            Assert.That(list["presets"][0].GetString("name"), Is.EqualTo("current"));
            Assert.That(list["presets"][0].GetInt("count"), Is.EqualTo(4));
            Assert.That(list["presets"][1].GetString("name"), Is.EqualTo("demo"));
            Assert.That(list["presets"][1].GetInt("count"), Is.EqualTo(2));
            Assert.That(list["presets"][1].GetString("modified"), Does.Match("^\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ$"));
            JsonValue current = await _t.GetJson("/api/presets/current");
            Assert.That(current.GetString("name"), Is.EqualTo("current"));
            Assert.That(current["values"].GetFloat("curved.cullRho"), Is.EqualTo(3f), "keyed by id, the parameter's name");
            Assert.That((await _t.GetJson("/api/presets/demo"))["values"].GetFloat("curved.cullRho"), Is.EqualTo(2.75f));
            await _t.GetJson("/api/presets/none", 404);

            // Load: every value in one ParamSet.
            await _t.PostJson("/api/presets/demo/load", "", 204);
            Wait.Until(() => _phone.Rho.Float == 2.75f && _phone.Copies.Int == 3, "the preset at the phone");
            await _t.PostJson("/api/presets/none/load", "", 404);

            // Bake: the preset becomes the project's defaults.json.
            await _t.PostJson("/api/presets/demo/bake", "", 204);
            string baked = Path.Combine(_t.Folder, "ATLAS", "Assets", "Atlas", "Telemetry", "Resources", "AtlasParams", "defaults.json");
            Assert.That(AtlasJson.Parse(System.IO.File.ReadAllText(baked))["values"].GetFloat("curved.cullRho"), Is.EqualTo(2.75f));
            await _t.PostJson("/api/presets/none/bake", "", 404);

            using (HttpResponseMessage r = await _t.Http.DeleteAsync("/api/presets/demo"))
                Assert.That((int)r.StatusCode, Is.EqualTo(204));
            Assert.That((await _t.GetJson("/api/presets"))["presets"].Count, Is.EqualTo(1));
        }

        [Test]
        public async Task WebSocket_FeedsTheConsole_AndCarriesItsCommands()
        {
            Wait.Until(() => _t.Hub.GetParams()["params"].Count == 4, "the manifest");
            using (var socket = new ClientWebSocket())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + _t.Hub.HttpPort + "/ws"), timeout.Token);
                var seen = new Dictionary<string, JsonValue>();
                int states = 0;

                async Task ReadUntil(Func<bool> done)
                {
                    var buffer = new byte[1 << 16];
                    while (!done())
                    {
                        int length = 0;
                        WebSocketReceiveResult r;
                        do
                        {
                            r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
                            length += r.Count;
                        }
                        while (!r.EndOfMessage);
                        JsonValue m = AtlasJson.Parse(Encoding.UTF8.GetString(buffer, 0, length));
                        string type = m.GetString("t");
                        if (type == "state") states++;
                        if (type == "event" && m.GetString("ev") != LinkEvents.Param && seen.ContainsKey("event")) continue;
                        seen[type] = m;
                    }
                }

                async Task Send(string json)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
                }

                // On connect: the link summary and the manifest with current values.
                await ReadUntil(() => seen.ContainsKey("link") && seen.ContainsKey("manifest"));
                Assert.That(seen["link"]["phone"].GetString("state"), Is.EqualTo("connected"));
                Assert.That(seen["link"].GetInt("consoles"), Is.EqualTo(1));
                Assert.That(seen["manifest"]["params"].Count, Is.EqualTo(4));
                Assert.That(seen["manifest"].GetInt("ver"), Is.GreaterThanOrEqualTo(1));

                // State as a digest at about 10 Hz, not at 60, and Telemetry as it arrives.
                _phone.SendStates = true;
                _phone.SendTelemetry = true;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await ReadUntil(() => clock.ElapsedMilliseconds >= 1500 && seen.ContainsKey("telemetry"));
                Assert.That(states, Is.InRange(8, 20), "about 10 Hz for 1.5 s");
                Assert.That(seen["state"].GetInt("space"), Is.EqualTo(2));
                Assert.That(seen["telemetry"].GetFloat("fps"), Is.EqualTo(59.5f));

                // {t: "param", id, value}: by wire id, clamped on the phone, echoed back as an event.
                int id = seen["manifest"]["params"][0].GetInt("id");
                await Send("{\"t\": \"param\", \"id\": " + id + ", \"value\": 1.0}");
                Wait.Until(() => _phone.Rho.Float == 2f, "the phone to clamp the slider value to its minimum");
                await ReadUntil(() => seen.ContainsKey("event") && seen["event"].GetString("ev") == LinkEvents.Param);
                Assert.That(seen["event"]["data"].GetFloat("value"), Is.EqualTo(2f));
                Assert.That(seen["event"]["data"].GetString("source"), Is.EqualTo("remote"));

                // {t: "op"} and {t: "scene"}.
                await Send("{\"t\": \"op\", \"cmd\": \"cue\"}");
                await Send("{\"t\": \"scene\", \"space\": \"S2\"}");
                Wait.Until(() => _phone.Commands.Count == 2, "the Console's commands at the phone");
                List<OperatorCommand> got = Wait.Drain(_phone.Commands);
                Assert.That(got[0].Kind, Is.EqualTo(OperatorCommandKind.Cue));
                Assert.That(got[1].Kind, Is.EqualTo(OperatorCommandKind.SwitchSpace));
                Assert.That(got[1].Space, Is.EqualTo(2));

                // A message the Hub cannot use is answered, not dropped.
                await Send("{\"t\": \"dance\"}");
                await ReadUntil(() => seen.ContainsKey("error"));
                Assert.That(seen["error"].GetString("msg"), Does.Contain("dance"));

                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            }
            Wait.Until(() => _t.Hub.Feed.Count == 0, "the Hub to forget the closed Console");

            using (HttpResponseMessage plain = await _t.Http.GetAsync("/ws"))
                Assert.That((int)plain.StatusCode, Is.EqualTo(400));
        }
    }

    /// <summary>Reads a log file the Hub still has open for writing.</summary>
    static class LogFile
    {
        public static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
