using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The phone's telemetry on the link library's additions, with a Hub on the in-memory network: the session
    /// the phone made reaches the Hub in <c>Hello</c>, the phone's perf CSV is what the Hub writes from the rows
    /// it receives, byte for byte, and the row carries the asset client's counters.
    /// </summary>
    public class TelemetryWiringTests
    {
        const int Port = 9051;
        const string HubAddress = "10.0.0.2";
        const string Session = "20261004T153012Z-a3f9";
        const string NextSession = "20261004T160000Z-0b1c";

        sealed class Rig
        {
            public readonly ManualClock Clock = new ManualClock(1000000);
            public readonly LoopbackNetwork Network;
            public readonly LinkHost Host;
            public readonly LinkClient Client;
            public readonly EventLog Events;
            public readonly LinkPump Pump;
            public readonly List<string> HelloSessions = new List<string>();
            public readonly List<EventMessage> Logged = new List<EventMessage>();
            public readonly List<TelemetryMessage> HubRows = new List<TelemetryMessage>();
            public StateMessage State = StateMessage.Identity;

            public Rig(LogFileWriter files = null)
            {
                Network = new LoopbackNetwork(Clock);
                Host = new LinkHost(Network.CreateEndpoint(), Clock) { SessionId = "hub-own" };
                // What the Hub does with a Hello: it adopts the session the phone names.
                Host.HelloReceived += (peer, hello) =>
                {
                    HelloSessions.Add(hello.SessionId);
                    if (!string.IsNullOrEmpty(hello.SessionId)) Host.SessionId = hello.SessionId;
                };
                Host.TelemetryReceived += OnHubRow;

                Client = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Phone, new ParamRegistry()) { Device = "test-phone" };
                Events = new EventLog(Clock, null);
                Events.Logged += Logged.Add;
                Pump = new LinkPump(Client, Events, files);
                Pump.SetAddress(HubAddress, Port);
            }

            void OnHubRow(int peer, in TelemetryMessage row) { HubRows.Add(row); }

            public void Step(double ms = 5.0)
            {
                Clock.AdvanceMs(ms);
                Host.Poll();
                Pump.Send(in State);
                Pump.Tick();
                Host.Poll();
            }

            public void Run(double ms)
            {
                int steps = (int)Math.Round(ms / 5.0);
                for (int i = 0; i < steps; i++) Step();
            }

            public bool RunUntilConnected(double limitMs)
            {
                for (double t = 0; t < limitMs && Pump.State != LinkState.Connected; t += 5.0) Step();
                return Pump.State == LinkState.Connected;
            }

            public void Connect()
            {
                Host.Start(Port);
                Assert.That(Pump.Start(), Is.True);
                Assert.That(RunUntilConnected(3000), Is.True, "the handshake did not finish");
            }

            public int Count(string ev)
            {
                int n = 0;
                for (int i = 0; i < Logged.Count; i++) if (Logged[i].Ev == ev) n++;
                return n;
            }
        }

        // ---- the session ------------------------------------------------------------------------------

        [Test]
        public void The_session_the_phone_made_reaches_the_hub_in_hello()
        {
            var rig = new Rig();
            // The installer names the session before the link starts.
            rig.Client.SetSession(Session);
            rig.Connect();
            Assert.That(rig.HelloSessions, Is.EqualTo(new[] { Session }));
            Assert.That(rig.Host.SessionId, Is.EqualTo(Session), "the Hub's folder takes the phone's name");
            Assert.That(rig.Client.Welcome.SessionId, Is.EqualTo(Session), "and Welcome says it back");

            // A new walker (AtlasTelemetry.BeginSession): the Station is told on the live link.
            int linkEvents = rig.Count(LinkEvents.Link);
            rig.Client.SetSession(NextSession);
            rig.Run(100);
            Assert.That(rig.HelloSessions, Is.EqualTo(new[] { Session, NextSession }));
            Assert.That(rig.Host.SessionId, Is.EqualTo(NextSession));
            Assert.That(rig.Client.Welcome.SessionId, Is.EqualTo(NextSession));
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.Count(LinkEvents.Link), Is.EqualTo(linkEvents), "the link was not dropped for it");
        }

        [Test]
        public void A_session_named_while_the_station_is_away_goes_out_with_the_next_handshake()
        {
            var rig = new Rig();
            rig.Client.SetSession(Session);
            Assert.That(rig.Pump.Start(), Is.True);
            rig.Run(1000);
            rig.Client.SetSession(NextSession);
            Assert.That(rig.HelloSessions, Is.Empty);

            rig.Host.Start(Port);
            Assert.That(rig.RunUntilConnected(8000), Is.True);
            Assert.That(rig.HelloSessions, Is.EqualTo(new[] { NextSession }));
        }

        // ---- the perf CSV -----------------------------------------------------------------------------

        static PerfSample Sample(int i)
        {
            return new PerfSample
            {
                FrameMs = 16.667f + 0.001f * (i % 7), CpuMainMs = 4.1f, CpuRenderMs = 2.9f, GpuMs = 4.6f,
                DrawCalls = 142, SetPassCalls = 22, Triangles = 231400, GcAllocBytes = i % 3 == 0 ? 96 : 0,
                UsedMemoryBytes = 612L << 20
            };
        }

        // One second of frames through the probe's core, then the row as the probe makes it.
        static TelemetryMessage NextRow(PerfProbeCore core, ref double time, ref int frame, long nowUs, in PerfRowContext context)
        {
            bool second = false;
            while (!second)
            {
                PerfSample sample = Sample(frame++);
                second = core.AddFrame(time, in sample);
                time += 1.0 / 60.0;
            }
            TelemetryMessage row;
            core.BuildRow(nowUs, in context, out row);
            return row;
        }

        [Test]
        public void The_phones_csv_is_what_the_hub_writes_from_the_rows_it_receives()
        {
            string dir = Path.Combine(Path.GetTempPath(), "atlas-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                string phoneCsv;
                var hubCsv = new StringBuilder();
                using (var files = new LogFileWriter(dir, false))
                {
                    files.Open(Session);
                    var rig = new Rig(files);
                    rig.Client.SetSession(Session);
                    rig.Connect();

                    var core = new PerfProbeCore();
                    var context = new PerfRowContext
                    {
                        SceneId = 1, SpaceId = 1, Tier = 0, RenderScale = 0.8f, Thermal = 1, Battery = 87, Tracking = 0, Msaa = 4,
                        Copies = 25, RttMs = 3, CueCount = 1, StateHz = 60, BumpMax = 1, AssetsPlaced = 3, AssetsPending = 1
                    };
                    double time = 0.0;
                    int frame = 0;

                    // Two rows with the Station there, two while it is away, one after it came back.
                    for (int i = 0; i < 2; i++)
                    {
                        TelemetryMessage row = NextRow(core, ref time, ref frame, rig.Clock.NowUs, in context);
                        rig.Pump.SubmitRow(in row);
                        rig.Run(1000);
                    }
                    rig.Network.Cut = true;
                    rig.Run(4000);
                    Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Offline));
                    for (int i = 0; i < 2; i++)
                    {
                        TelemetryMessage row = NextRow(core, ref time, ref frame, rig.Clock.NowUs, in context);
                        rig.Pump.SubmitRow(in row);
                        rig.Run(1000);
                    }
                    rig.Network.Cut = false;
                    Assert.That(rig.RunUntilConnected(8000), Is.True, "the link did not come back");
                    TelemetryMessage last = NextRow(core, ref time, ref frame, rig.Clock.NowUs, in context);
                    rig.Pump.SubmitRow(in last);
                    rig.Run(200);

                    Assert.That(rig.HubRows.Count, Is.EqualTo(5));
                    for (int i = 0; i < 5; i++)
                        Assert.That(rig.HubRows[i].Seq, Is.EqualTo((uint)i), "the probe's row number, not the link's count of sends");
                    Assert.That(rig.HubRows[4].TimeUs, Is.EqualTo(last.TimeUs));

                    // The Hub's file: the link library's header and one row per Telemetry message.
                    Atlas.Link.TelemetryCsv.AppendHeader(hubCsv);
                    for (int i = 0; i < rig.HubRows.Count; i++) Atlas.Link.TelemetryCsv.AppendRow(hubCsv, rig.HubRows[i]);

                    files.Flush();
                    Assert.That(files.Errors, Is.EqualTo(0));
                    phoneCsv = files.PathOf(LogFileWriter.Perf);
                    Assert.That(Path.GetFileName(phoneCsv), Is.EqualTo("perf_" + Session + ".csv"));
                }
                byte[] phone = File.ReadAllBytes(phoneCsv);
                byte[] hub = new UTF8Encoding(false).GetBytes(hubCsv.ToString());
                Assert.That(Encoding.UTF8.GetString(phone), Is.EqualTo(hubCsv.ToString()));
                Assert.That(phone, Is.EqualTo(hub), "the same bytes, the header included");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void The_telemetry_writer_forwards_to_the_link_librarys()
        {
            Assert.That(TelemetryCsv.Header, Is.EqualTo(Atlas.Link.TelemetryCsv.Header));
            Assert.That(TelemetryCsv.ColumnCount, Is.EqualTo(Atlas.Link.TelemetryCsv.ColumnCount));

            var rows = new[]
            {
                new TelemetryMessage(),
                new TelemetryMessage
                {
                    TimeUs = long.MaxValue, Seq = uint.MaxValue, SceneId = ushort.MaxValue, SpaceId = 255, Tier = 255,
                    Fps = 59.9995f, P50 = 16.6665f, P95 = float.NaN, P99 = float.PositiveInfinity, Max = 1e20f, Over17_2 = -0.0004f,
                    Over33_8 = -1.5f, CpuMainP95 = 0.0005f, CpuRenderP95 = 999.9996f, GpuP95 = 0.125f, RenderScale = 0.8f,
                    DrawCalls = ushort.MaxValue, GcAllocB = uint.MaxValue, Battery = 255, AssetsPlaced = 255, AssetsPending = 255
                },
            };
            var here = new StringBuilder();
            var link = new StringBuilder();
            for (int i = 0; i < rows.Length; i++)
            {
                TelemetryCsv.AppendRow(here, in rows[i]);
                Atlas.Link.TelemetryCsv.AppendRow(link, in rows[i]);
            }
            Assert.That(here.ToString(), Is.EqualTo(link.ToString()));

            // The free-decimals writer agrees with the CSV's format at three decimals.
            foreach (float value in new[] { 0f, 0.0005f, -0.0004f, -1.5f, 999.9996f, 16.667f, float.NaN, 1e20f })
            {
                here.Length = 0;
                link.Length = 0;
                TelemetryCsv.AppendFixed(here, value, 3);
                Atlas.Link.TelemetryCsv.AppendFixed3(link, value);
                Assert.That(here.ToString(), Is.EqualTo(link.ToString()));
            }
            here.Length = 0;
            TelemetryCsv.AppendFixed(here, 0.25f, 1);
            here.Append(' ');
            TelemetryCsv.AppendFixed(here, -12.345f, 2);
            here.Append(' ');
            TelemetryCsv.AppendFixed(here, 7.5f, 0);
            Assert.That(here.ToString(), Is.EqualTo("0.3 -12.35 8"));
        }

        // ---- the asset counters -----------------------------------------------------------------------

        static RoomObject Box(int id, ObjectCategory category, float w, float h, float d)
        {
            return new RoomObject { Id = id, Category = category, Base = new float3(id, 0f, 0f), Size = new float3(w, h, d) };
        }

        static ObjectPhotoSet Photos(RoomObject o)
        {
            return new ObjectPhotoSet
            {
                ObjectId = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw, TriTarget = o.TriTarget,
                Views = new[]
                {
                    new ObjectView
                    {
                        Jpeg = new byte[] { 0xFF, 0xD8, (byte)o.Id, 0x0D, 0x0A, 0xFF, 0xD9 }, Width = 256, Height = 256, Score = 0.75f,
                        AzimuthDeg = 35f, Intrinsics = new float4(612f, 613f, 128f, 129f), CamToBox = float4x4.identity
                    }
                }
            };
        }

        [Test]
        public void The_row_carries_the_asset_clients_counters()
        {
            // The asset client with a Forge in memory and the three boxes its fixtures were made for.
            const int Table = 1, Chair = 2, Couch = 3;
            var forge = new FakeForge();
            var core = new AssetClientCore(forge, new FakeAssetRoom(), new MemoryAssetCache(), new InlineAssetWorker(),
                AssetFixtures.LoadFallback(), new RecordingEvents()) { ForgeUrl = FakeForge.Url };
            core.Tick(0.0, 16.7f);
            var objects = new[]
            {
                Box(Table, ObjectCategory.Table, 1.2f, 0.75f, 0.8f),
                Box(Chair, ObjectCategory.Chair, 0.5f, 0.9f, 0.5f),
                Box(Couch, ObjectCategory.Couch, 2.0f, 0.85f, 0.9f),
            };
            ObjectMeshRules.Assign(objects);
            var room = new RoomModel { Id = "room_a", Objects = objects };
            core.RoomBuilt(room);
            core.BeginRoom(room, "{\"version\":1,\"id\":\"room_a\",\"objects\":[]}");
            foreach (RoomObject o in objects) core.Submit(Photos(o));
            core.EndRoom();

            double time = 0.0;
            Action<double> run = seconds =>
            {
                for (double end = time + seconds; time < end - 1e-9;)
                {
                    time += 0.05;
                    forge.Now = time;
                    core.Tick(time, 16.7f);
                }
            };
            run(2.0);
            // The Forge finishes two of the three.
            forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
            run(4.0);
            Assert.That(core.PlacedCount, Is.EqualTo(2));
            Assert.That(core.PendingCount, Is.EqualTo(1));

            // What step 8b does with them once per second: into the probe's row, and on to the Station and the CSV.
            var context = new PerfRowContext
            {
                AssetsPlaced = (byte)Math.Min(255, core.PlacedCount),
                AssetsPending = (byte)Math.Min(255, core.PendingCount)
            };
            var probe = new PerfProbeCore();
            double probeTime = 0.0;
            int frame = 0;
            var rig = new Rig();
            rig.Connect();
            TelemetryMessage row = NextRow(probe, ref probeTime, ref frame, rig.Clock.NowUs, in context);
            Assert.That(row.AssetsPlaced, Is.EqualTo(2));
            Assert.That(row.AssetsPending, Is.EqualTo(1));

            rig.Pump.SubmitRow(in row);
            rig.Run(50);
            Assert.That(rig.HubRows.Count, Is.EqualTo(1));
            Assert.That(rig.HubRows[0].AssetsPlaced, Is.EqualTo(2));
            Assert.That(rig.HubRows[0].AssetsPending, Is.EqualTo(1));

            var line = new StringBuilder();
            TelemetryCsv.AppendRow(line, rig.HubRows[0]);
            string[] cells = line.ToString().TrimEnd('\n').Split(',');
            string[] names = TelemetryCsv.Header.Split(',');
            Assert.That(names[32], Is.EqualTo("assetsPlaced"));
            Assert.That(names[33], Is.EqualTo("assetsPending"));
            Assert.That(cells[32], Is.EqualTo("2"));
            Assert.That(cells[33], Is.EqualTo("1"));
        }
    }
}
