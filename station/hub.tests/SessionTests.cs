using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>
    /// Who owns the session (lane D contract 5.5), and what that does to the logs: the phone names the session in
    /// its <c>Hello</c> and the Hub adopts it for its folder; a restarted Hub continues that folder; a
    /// <c>Telemetry</c> row is written once however often it arrives; and the phone's own CSV, uploaded after a
    /// session, is merged by <c>seq</c> into a file that is byte for byte the phone's (test L11).
    /// </summary>
    [TestFixture]
    public class SessionTests
    {
        const string Session = "20261004T153012Z-a3f9";

        static TelemetryMessage Row(uint seq)
        {
            return new TelemetryMessage
            {
                TimeUs = 1000000L * (seq + 1), Seq = seq, SceneId = 1, SpaceId = 1, Fps = 59.5f + seq * 0.125f, P50 = 16.667f, P95 = 16.9f,
                RenderScale = 0.8f, DrawCalls = 142, MemMB = 612, Battery = 87, Msaa = 4, StateHz = 60
            };
        }

        /// <summary>The phone's own file for rows 0 to count - 1, exactly as the app writes it.</summary>
        static string PhoneCsv(params uint[] seqs)
        {
            var sb = new StringBuilder();
            TelemetryCsv.AppendHeader(sb);
            foreach (uint seq in seqs) TelemetryCsv.AppendRow(sb, Row(seq));
            return sb.ToString();
        }

        /// <summary>Sends rows numbered by the caller, as the phone's perf probe numbers them, in this order.</summary>
        static void SendRows(TestClient phone, params uint[] seqs)
        {
            foreach (uint seq in seqs)
            {
                TelemetryMessage row = Row(seq);
                phone.Post(c => c.SendTelemetryRow(in row));
            }
        }

        /// <summary>The bytes of a log file the Hub still has open for writing.</summary>
        static byte[] ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return bytes;
            }
        }

        static async Task<HttpResponseMessage> Put(TestHub t, string path, string body)
        {
            using (var content = new StringContent(body, new UTF8Encoding(false), "text/csv"))
                return await t.Http.PutAsync(path, content);
        }

        static async Task<string> PerfCsvOf(TestHub t, string session)
        {
            return await t.Http.GetStringAsync("/logs/" + session + "/perf.csv");
        }

        [Test]
        public async Task ThePhoneNamesTheSession_AndTheHubAdoptsItForItsFolder()
        {
            await using (TestHub t = await TestHub.StartAsync())
            {
                string own = t.Hub.SessionId;
                string ownFolder = t.Hub.Log.Folder;
                Assert.That(own, Is.Not.Empty);
                Assert.That(Directory.Exists(ownFolder), Is.True, "until a phone names a session the Hub logs under its own");

                using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, "test-iphone", c => c.SessionId = Session))
                using (var spectator = new TestClient(LinkRole.Spectator, t.Hub.UdpPort))
                {
                    Wait.Ready(phone);
                    Wait.Ready(spectator);
                    Wait.Until(() => t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");

                    Assert.That(t.Hub.SessionId, Is.EqualTo(Session));
                    Assert.That(Path.GetFileName(t.Hub.Log.Folder), Is.EqualTo(Session));
                    Assert.That(phone.LastWelcome.SessionId, Is.EqualTo(Session), "to a phone: its own id back");
                    Wait.Until(() => spectator.LastWelcome != null && spectator.LastWelcome.SessionId == Session, "the Spectator to be told the source's session");

                    // The Hub's own folder held nothing but its start-up notes; they moved, and it is gone.
                    Assert.That(Directory.Exists(ownFolder), Is.False);
                    JsonValue sessions = await t.GetJson("/api/sessions");
                    Assert.That(sessions.GetString("current"), Is.EqualTo(Session));
                    Assert.That(sessions["sessions"].Count, Is.EqualTo(1));
                    Assert.That(sessions["sessions"][0].AsString(), Is.EqualTo(Session));
                    Assert.That((await t.GetJson("/api/session")).GetString("sessionId"), Is.EqualTo(Session));

                    // Everything of the session lands in the one folder.
                    phone.SendStates = true;
                    SendRows(phone, 0, 1);
                    Wait.Until(() => t.Hub.StatesReceived > 20 && t.Hub.TelemetryRows == 2, "State and Telemetry at the Hub");
                    phone.SendStates = false;
                    string hubLog = await t.Http.GetStringAsync("/logs/" + Session + "/hub.log");
                    Assert.That(hubLog, Does.Contain(" start {"), "what the Hub noted before the phone arrived came along");
                    Assert.That(hubLog, Does.Contain(" session {").And.Contain("\"from\":\"" + own + "\"").And.Contain("\"namedBy\":\"phone\""));
                    Assert.That(hubLog.IndexOf(" start {", StringComparison.Ordinal), Is.LessThan(hubLog.IndexOf(" session {", StringComparison.Ordinal)));
                    Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1)));
                    JsonValue files = await t.GetJson("/logs/" + Session);
                    Assert.That(files["files"].Count, Is.EqualTo(6), "perf.csv, events.jsonl, state.bin, phone.log, hub.log, manifest.json");
                }
            }
        }

        [Test]
        public async Task WithNoSessionInHello_OrOneFixedOnTheCommandLine_TheHubKeepsItsOwn()
        {
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort))
            {
                string own = t.Hub.SessionId;
                Wait.Ready(phone);
                Wait.Until(() => t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");
                Assert.That(t.Hub.SessionId, Is.EqualTo(own));
                Assert.That(phone.LastWelcome.SessionId, Is.EqualTo(own), "the Hub's own id, for a phone that brought none");
                Assert.That(Directory.Exists(t.Hub.Log.Folder), Is.True);
            }

            await using (TestHub pinned = await TestHub.StartAsync(o => o.SessionId = "demo-1"))
            using (var phone = new TestClient(LinkRole.Phone, pinned.Hub.UdpPort, null, c => c.SessionId = Session))
            {
                Wait.Ready(phone);
                Wait.Until(() => pinned.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");
                Assert.That(pinned.Hub.SessionId, Is.EqualTo("demo-1"), "--session fixes the folder");
                Assert.That(phone.LastWelcome.SessionId, Is.EqualTo("demo-1"));
            }

            // An id that could leave the log root is not a folder name.
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, null, c => c.SessionId = "../outside"))
            {
                string own = t.Hub.SessionId;
                Wait.Ready(phone);
                Wait.Until(() => t.Hub.PhoneState == LinkState.Connected, "the Hub to see the phone CONNECTED");
                Assert.That(t.Hub.SessionId, Is.EqualTo(own));
                Assert.That(Directory.Exists(Path.Combine(t.Folder, "outside")), Is.False);
                Assert.That(await t.Http.GetStringAsync("/logs/" + own + "/hub.log"), Does.Contain(" session {\"refused\":\"../outside\""));
            }
        }

        [Test]
        public async Task ANewSessionInMidConnection_StartsANewFolder_WithThePhonesParametersAndRoom()
        {
            const string second = "20261004T154500Z-07c2";
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, null, c => c.SessionId = Session))
            {
                Wait.Ready(phone);
                Wait.Until(() => t.Hub.GetParams()["params"].Count == 3, "the manifest");
                var outline = new RoomOutlineMessage { Height = 2.4f };
                outline.AddPoint(0, 0); outline.AddPoint(3, 0); outline.AddPoint(3, 3); outline.AddPoint(0, 3);
                phone.Post(c => c.SendRoomOutline(outline));
                SendRows(phone, 0, 1, 2);
                Wait.Until(() => t.Hub.TelemetryRows == 3 && File.Exists(Path.Combine(t.Hub.Log.Folder, SessionLog.OutlineFile)), "rows and room of the first session");
                string firstFolder = t.Hub.Log.Folder;

                // A new walker: the app starts a session and says Hello again on the same connection.
                phone.Post(c => c.SetSession(second));
                Wait.Until(() => t.Hub.SessionId == second, "the Hub to follow the phone into its new session");
                Assert.That(t.Hub.PhoneState, Is.EqualTo(LinkState.Connected), "no reconnect, no RESYNC");
                Wait.Until(() => phone.LastWelcome.SessionId == second, "the new id to come back in Welcome");
                Assert.That(t.Hub.TelemetryRows, Is.EqualTo(0), "the counters are the session's");

                // seq starts again at 0 in the new session and is not mistaken for the old session's row 0.
                SendRows(phone, 0, 1);
                Wait.Until(() => t.Hub.TelemetryRows == 2, "the new session's rows");
                Assert.That(await PerfCsvOf(t, second), Is.EqualTo(PhoneCsv(0, 1)));
                Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1, 2)), "the first session is left as it ended");

                // The new folder can be replayed on its own: it has the manifest and the room.
                Assert.That(File.Exists(Path.Combine(t.Hub.Log.Folder, SessionLog.ManifestFile)), Is.True);
                Assert.That(File.ReadAllBytes(Path.Combine(t.Hub.Log.Folder, SessionLog.OutlineFile)),
                    Is.EqualTo(File.ReadAllBytes(Path.Combine(firstFolder, SessionLog.OutlineFile))));
                JsonValue sessions = await t.GetJson("/api/sessions");
                Assert.That(sessions["sessions"].Count, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task ARestartedHub_ContinuesThePhonesSessionFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "atlas-hub-tests", Guid.NewGuid().ToString("N"));
            TestHub first = await TestHub.StartAsync(null, folder);
            int port = first.Hub.UdpPort;
            var phone = new TestClient(LinkRole.Phone, port, null, c => c.SessionId = Session);
            long sentBefore;
            try
            {
                Wait.Ready(phone);
                Wait.Until(() => first.Hub.SessionId == Session, "the first Hub to adopt the session");
                phone.SendStates = true;
                SendRows(phone, 0, 1, 2);
                phone.Post(c => c.SendEvent("S1", "note", JsonValue.Object().Set("text", "before the restart")));
                Wait.Until(() => first.Hub.StatesReceived > 30 && first.Hub.TelemetryRows == 3 && first.Hub.EventCount >= 1, "the first Hub's share of the session");
                phone.SendStates = false;
                sentBefore = phone.Sent;
                Wait.Until(() => first.Hub.StatesReceived == sentBefore, "every State to reach the first Hub", 2000);

                // The Hub goes down in mid-session; its folder stays.
                await first.Hub.StopAsync();
                first.Http.Dispose();
                Wait.Until(() => !phone.IsReady, "the phone to notice the Hub has gone", 4000);

                // It left a torn record behind, as a Hub that lost power would.
                string statePath = Path.Combine(folder, "logs", Session, SessionLog.StateFile);
                using (var torn = new FileStream(statePath, FileMode.Append, FileAccess.Write))
                    torn.Write(new byte[100], 0, 100);

                await using (TestHub second = await TestHub.StartAsync(o => o.UdpPort = port, folder))
                {
                    Wait.Until(() => phone.IsReady && second.Hub.PhoneState == LinkState.Connected, "the phone to reconnect to the new Hub", 6000);
                    Assert.That(second.Hub.SessionId, Is.EqualTo(Session), "the phone's Hello carries its session id, so the Hub appends to the same folder");
                    Assert.That(Directory.GetDirectories(Path.Combine(folder, "logs")).Length, Is.EqualTo(1), "one session, one folder, across two Hubs");

                    // The phone sends again the rows it is not sure about, and goes on.
                    phone.SendStates = true;
                    SendRows(phone, 1, 2, 3, 4);
                    phone.Post(c => c.SendEvent("S1", "note", JsonValue.Object().Set("text", "after the restart")));
                    Wait.Until(() => second.Hub.StatesReceived > 30 && second.Hub.TelemetryRows == 2 && second.Hub.EventCount >= 1, "the second Hub's share of the session");
                    phone.SendStates = false;
                    long sentAfter = phone.Sent - sentBefore;
                    Wait.Until(() => second.Hub.StatesReceived == sentAfter, "every State to reach the second Hub", 2000);

                    Assert.That(await PerfCsvOf(second, Session), Is.EqualTo(PhoneCsv(0, 1, 2, 3, 4)), "no hole and no row twice");

                    string events = await second.Http.GetStringAsync("/logs/" + Session + "/events.jsonl");
                    Assert.That(events, Does.Contain("before the restart").And.Contain("after the restart"));
                    Assert.That(events.IndexOf("before the restart", StringComparison.Ordinal), Is.LessThan(events.IndexOf("after the restart", StringComparison.Ordinal)));

                    string hubLog = await second.Http.GetStringAsync("/logs/" + Session + "/hub.log");
                    Assert.That(hubLog.Split(new[] { " start {" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2), "both Hubs' start in one hub.log");
                    Assert.That(hubLog, Does.Contain("\"resumed\":true"));

                    // state.bin is one recording: the first Hub's records, the torn one cut away, then the second's.
                    second.Hub.Log.Flush();
                    List<StateLog.Entry> log = StateLog.ReadAll(statePath);
                    Assert.That(log.Count, Is.EqualTo(sentBefore + sentAfter));
                    for (int i = 0; i < log.Count; i++) Assert.That(log[i].State.Seq, Is.EqualTo((uint)i), "the phone's own sequence, unbroken");
                }
            }
            finally
            {
                phone.Dispose();
                await first.Hub.DisposeAsync();
            }
        }

        [Test]
        public async Task ATelemetryRow_IsWrittenOnce_AndTheFileStaysInSeqOrder()
        {
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, null, c => c.SessionId = Session))
            {
                Wait.Ready(phone);
                Wait.Until(() => t.Hub.SessionId == Session, "the Hub to adopt the session");

                // Rows as a phone sends them after an outage: some twice, one late.
                SendRows(phone, 0, 1, 1, 3, 0, 2, 3, 4);
                Wait.Until(() => t.Hub.TelemetryRows == 5, "five different rows at the Hub");
                Thread.Sleep(100);
                Assert.That(t.Hub.TelemetryRows, Is.EqualTo(5));
                Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1, 2, 3, 4)), "byte for byte the phone's file");
                Assert.That((await t.GetJson("/api/telemetry")).GetLong("seq"), Is.EqualTo(4), "the newest row is the one with the highest seq");
            }

            // A phone that names no session may have been restarted: its counter starting again is not a duplicate.
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort))
            {
                Wait.Ready(phone);
                SendRows(phone, 0, 1, 0, 1);
                Wait.Until(() => t.Hub.TelemetryRows == 4, "every row at the Hub");
                Assert.That(await PerfCsvOf(t, t.Hub.SessionId), Is.EqualTo(PhoneCsv(0, 1, 0, 1)));
            }
        }

        [Test]
        public async Task ThePhonesCsv_UploadedAfterTheSession_IsMergedBySeq()
        {
            await using (TestHub t = await TestHub.StartAsync())
            using (var phone = new TestClient(LinkRole.Phone, t.Hub.UdpPort, null, c => c.SessionId = Session))
            {
                Wait.Ready(phone);
                Wait.Until(() => t.Hub.SessionId == Session, "the Hub to adopt the session");

                // The link was down for rows 2 and 4 to 6, longer than the phone kept them.
                SendRows(phone, 0, 1, 3, 7);
                Wait.Until(() => t.Hub.TelemetryRows == 4, "the rows that made it over the link");
                Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1, 3, 7)));

                string phoneFile = PhoneCsv(0, 1, 2, 3, 4, 5, 6, 7);
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/perf.csv", phoneFile))
                {
                    Assert.That((int)r.StatusCode, Is.EqualTo(204));
                    Assert.That((await r.Content.ReadAsByteArrayAsync()).Length, Is.EqualTo(0));
                }
                Assert.That(Encoding.UTF8.GetBytes(await PerfCsvOf(t, Session)), Is.EqualTo(Encoding.UTF8.GetBytes(phoneFile)), "byte for byte the phone's file (L11)");
                Assert.That(ReadShared(Path.Combine(t.Hub.Log.Folder, SessionLog.PerfFile)), Is.EqualTo(Encoding.UTF8.GetBytes(phoneFile)), "and no byte-order mark");

                // The session goes on after the upload: a new row is appended, an uploaded one is known.
                SendRows(phone, 5, 8);
                Wait.Until(() => t.Hub.Log.TelemetryRows == 9, "row 8 to be appended");
                Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1, 2, 3, 4, 5, 6, 7, 8)));

                // Uploading again, or uploading less, changes nothing.
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/perf.csv", phoneFile)) Assert.That((int)r.StatusCode, Is.EqualTo(204));
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/telemetry.csv", PhoneCsv(2, 3))) Assert.That((int)r.StatusCode, Is.EqualTo(204));
                Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1, 2, 3, 4, 5, 6, 7, 8)));
                Assert.That(await t.Http.GetStringAsync("/logs/" + Session + "/hub.log"), Does.Contain(" perf {").And.Contain("\"added\":4"));

                // A session the Hub never saw: the phone walked with no Station and uploads later (section 12.2).
                const string offline = "20261004T170000Z-0b0b";
                string offlineFile = PhoneCsv(0, 1, 2);
                using (HttpResponseMessage r = await Put(t, "/logs/" + offline + "/perf.csv", offlineFile.Replace("\n", "\r\n")))
                    Assert.That((int)r.StatusCode, Is.EqualTo(204));
                Assert.That(await PerfCsvOf(t, offline), Is.EqualTo(offlineFile), "stored with \\n line ends whatever the upload used");
                JsonValue sessions = await t.GetJson("/api/sessions");
                Assert.That(sessions["sessions"].Count, Is.EqualTo(2));
                Assert.That(sessions.GetString("current"), Is.EqualTo(Session));

                // A long day: more than the megabyte the Hub's other routes accept.
                const string longDay = "20261004T080000Z-1d1d";
                var seqs = new uint[9000];
                for (uint i = 0; i < seqs.Length; i++) seqs[i] = i;
                string longFile = PhoneCsv(seqs);
                Assert.That(longFile.Length, Is.GreaterThan(1 << 20));
                using (HttpResponseMessage r = await Put(t, "/logs/" + longDay + "/perf.csv", longFile)) Assert.That((int)r.StatusCode, Is.EqualTo(204));
                Assert.That(await PerfCsvOf(t, longDay), Is.EqualTo(longFile));

                // What is not a perf CSV is refused and changes nothing.
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/perf.csv", "seq,fps\n1,60\n"))
                {
                    Assert.That((int)r.StatusCode, Is.EqualTo(400));
                    Assert.That(AtlasJson.Parse(await r.Content.ReadAsStringAsync()).GetString("error"), Does.Contain("header"));
                }
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/perf.csv", "")) Assert.That((int)r.StatusCode, Is.EqualTo(400));
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/events.jsonl", "{}\n")) Assert.That((int)r.StatusCode, Is.EqualTo(404));
                using (HttpResponseMessage r = await Put(t, "/logs/bad%20name/perf.csv", phoneFile)) Assert.That((int)r.StatusCode, Is.EqualTo(400));
                using (HttpResponseMessage r = await Put(t, "/logs/..%2F..%2Foutside/perf.csv", phoneFile)) Assert.That((int)r.StatusCode, Is.EqualTo(400));
                Assert.That(Directory.Exists(Path.Combine(t.Folder, "outside")), Is.False);
                // Lines that are not rows are left out; the rows around them are taken.
                using (HttpResponseMessage r = await Put(t, "/logs/" + Session + "/perf.csv",
                           TelemetryCsv.Header + "\n" + "<script>alert(1)</script>\n" + "1,2,3\n" + PhoneCsv(9).Substring(TelemetryCsv.Header.Length + 1)))
                    Assert.That((int)r.StatusCode, Is.EqualTo(204));
                Assert.That(await PerfCsvOf(t, Session), Is.EqualTo(PhoneCsv(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)));
            }
        }

        [Test]
        public void PerfCsv_ReadsSeq_AndMergesWithoutEverDroppingARow()
        {
            string row5 = PhoneCsv(5).Substring(TelemetryCsv.Header.Length + 1).TrimEnd('\n');
            uint seq;
            Assert.That(PerfCsv.TryReadSeq(row5, out seq), Is.True);
            Assert.That(seq, Is.EqualTo(5u));
            Assert.That(PerfCsv.TryReadSeq(row5.Replace(",5,1,1,", ",4294967295,1,1,"), out seq), Is.True);
            Assert.That(seq, Is.EqualTo(uint.MaxValue));
            Assert.That(PerfCsv.TryReadSeq(row5.Replace(",5,1,1,", ",4294967296,1,1,"), out seq), Is.False, "seq is a u32");
            Assert.That(PerfCsv.TryReadSeq(row5 + ",1", out seq), Is.False, "35 columns");
            Assert.That(PerfCsv.TryReadSeq(row5.Substring(0, row5.LastIndexOf(',')), out seq), Is.False, "33 columns");
            Assert.That(PerfCsv.TryReadSeq(row5.Replace(",5,1,1,", ",,1,1,"), out seq), Is.False, "an empty field");
            Assert.That(PerfCsv.TryReadSeq(row5.Replace("60.125", "fast"), out seq), Is.False, "only numbers");
            Assert.That(PerfCsv.TryReadSeq(TelemetryCsv.Header, out seq), Is.False);
            Assert.That(PerfCsv.TryReadSeq("", out seq), Is.False);
            Assert.That(PerfCsv.TryReadSeq(null, out seq), Is.False);

            bool hasHeader;
            int skipped;
            List<string> rows = PerfCsv.Rows(PhoneCsv(3, 4) + "junk\n\n", out hasHeader, out skipped);
            Assert.That(hasHeader, Is.True);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(skipped, Is.EqualTo(1));
            Assert.That(PerfCsv.Rows(row5 + "\n", out hasHeader, out skipped).Count, Is.EqualTo(1));
            Assert.That(hasHeader, Is.False);

            string folder = Path.Combine(Path.GetTempPath(), "atlas-hub-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "perf.csv");
                Assert.That(PerfCsv.MergeFile(path, PerfCsv.Rows(PhoneCsv(4, 2, 2), out hasHeader, out skipped)), Is.EqualTo(2));
                Assert.That(File.ReadAllText(path), Is.EqualTo(PhoneCsv(2, 4)), "a new file, in seq order, each row once");
                Assert.That(PerfCsv.MergeFile(path, PerfCsv.Rows(PhoneCsv(1, 2, 3, 9), out hasHeader, out skipped)), Is.EqualTo(3));
                Assert.That(File.ReadAllText(path), Is.EqualTo(PhoneCsv(1, 2, 3, 4, 9)));
                DateTime written = File.GetLastWriteTimeUtc(path);
                Assert.That(PerfCsv.MergeFile(path, PerfCsv.Rows(PhoneCsv(1, 9), out hasHeader, out skipped)), Is.EqualTo(0));
                Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(written), "nothing to add: the file is not touched");

                // A file whose seq started again (a phone that named no session) keeps every row it has.
                File.WriteAllText(path, PhoneCsv(0, 1, 0, 1));
                Assert.That(PerfCsv.MergeFile(path, PerfCsv.Rows(PhoneCsv(1, 3, 2), out hasHeader, out skipped)), Is.EqualTo(2));
                Assert.That(File.ReadAllText(path), Is.EqualTo(PhoneCsv(0, 1, 0, 1, 2, 3)));

                // A torn last line and a missing header are repaired; the whole rows survive.
                File.WriteAllText(path, PhoneCsv(0, 1).Substring(TelemetryCsv.Header.Length + 1) + "123456,2,1");
                bool intact;
                Assert.That(PerfCsv.Read(path, out intact).Count, Is.EqualTo(2));
                Assert.That(intact, Is.False);
                Assert.That(PerfCsv.MergeFile(path, null), Is.EqualTo(0));
                Assert.That(File.ReadAllText(path), Is.EqualTo(PhoneCsv(0, 1)));
                Assert.That(PerfCsv.Read(path, out intact).Count, Is.EqualTo(2));
                Assert.That(intact, Is.True);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void SessionLog_ContinuesAFolder_AndKnowsWhenItHoldsNothingOfAPhone()
        {
            string root = Path.Combine(Path.GetTempPath(), "atlas-hub-tests", Guid.NewGuid().ToString("N"));
            try
            {
                var raw = new byte[StateMessage.Size];
                StateMessage state = StateMessage.Identity;
                using (var log = new SessionLog(root, "s1"))
                {
                    Assert.That(log.CreatedFolder, Is.True);
                    log.WriteHubLog(1, "start", "{}");
                    Assert.That(log.IsPristine, Is.True, "only the Hub's own notes so far");
                    Assert.That(log.ReadHubLog(), Is.EqualTo("1 start {}\n"));

                    state.Seq = 0;
                    state.WriteTo(raw, 0);
                    log.WriteState(100, raw, 0);
                    Assert.That(log.IsPristine, Is.False);
                    log.WriteEvent("{\"ev\":\"one\"}");
                    log.SkipKnownRows = true;
                    Assert.That(log.WriteTelemetry(Row(0)), Is.True);
                    Assert.That(log.WriteTelemetry(Row(0)), Is.False);
                    Assert.That(log.WriteTelemetry(Row(2)), Is.True);
                    Assert.That(log.WriteTelemetry(Row(1)), Is.True, "a late row is put in its place");
                    Assert.That(log.TelemetryRows, Is.EqualTo(3));
                }
                Assert.That(File.ReadAllText(Path.Combine(root, "s1", SessionLog.PerfFile)), Is.EqualTo(PhoneCsv(0, 1, 2)));

                // What a Hub that lost power leaves: part of a record, part of a line.
                using (var torn = new FileStream(Path.Combine(root, "s1", SessionLog.StateFile), FileMode.Append, FileAccess.Write))
                    torn.Write(new byte[57], 0, 57);
                File.AppendAllText(Path.Combine(root, "s1", SessionLog.EventsFile), "{\"ev\":\"to");
                File.AppendAllText(Path.Combine(root, "s1", SessionLog.PerfFile), "4000000,3,1,1");

                using (var log = new SessionLog(root, "s1") { SkipKnownRows = true })
                {
                    Assert.That(log.CreatedFolder, Is.False);
                    Assert.That(log.IsPristine, Is.False, "a folder that was there before is never this Hub's to leave behind");
                    Assert.That(log.StateRecords, Is.EqualTo(1));
                    Assert.That(log.TelemetryRows, Is.EqualTo(3));
                    state.Seq = 1;
                    state.WriteTo(raw, 0);
                    log.WriteState(200, raw, 0);
                    log.WriteEvent("{\"ev\":\"two\"}");
                    Assert.That(log.WriteTelemetry(Row(2)), Is.False, "a row the Hub before this one wrote");
                    Assert.That(log.WriteTelemetry(Row(3)), Is.True);
                    log.AppendHubLog("5 start {}\n6 join {}");
                    log.WriteHubLog(7, "session", "{}");
                }

                List<StateLog.Entry> entries = StateLog.ReadAll(Path.Combine(root, "s1", SessionLog.StateFile));
                Assert.That(entries.Count, Is.EqualTo(2));
                Assert.That(entries[1].State.Seq, Is.EqualTo(1u));
                Assert.That(entries[1].HubUs, Is.EqualTo(200L));
                Assert.That(File.ReadAllText(Path.Combine(root, "s1", SessionLog.EventsFile)), Is.EqualTo("{\"ev\":\"one\"}\n{\"ev\":\"to\n{\"ev\":\"two\"}\n"));
                Assert.That(File.ReadAllText(Path.Combine(root, "s1", SessionLog.PerfFile)), Is.EqualTo(PhoneCsv(0, 1, 2, 3)));
                Assert.That(File.ReadAllText(Path.Combine(root, "s1", SessionLog.HubLogFile)), Is.EqualTo("1 start {}\n5 start {}\n6 join {}\n7 session {}\n"));

                // A state.bin this build cannot continue is kept under another name, not written over.
                File.WriteAllText(Path.Combine(root, "s1", SessionLog.StateFile), "NOTASTATEFILE...and more");
                using (var log = new SessionLog(root, "s1")) Assert.That(log.StateRecords, Is.EqualTo(0));
                Assert.That(StateLog.ReadAll(Path.Combine(root, "s1", SessionLog.StateFile)).Count, Is.EqualTo(0));
                Assert.That(Directory.GetFiles(Path.Combine(root, "s1"), "state.*.bin").Length, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
