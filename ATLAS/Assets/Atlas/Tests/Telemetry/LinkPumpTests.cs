using System;
using System.Collections.Generic;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The link driver against a Hub on the in-memory network, with one manual clock stepped like a frame loop:
    /// step 8a (<c>Send</c>) and step 8b (<c>Tick</c>) once per 5 ms.
    /// </summary>
    public class LinkPumpTests
    {
        const int Port = 9050;
        const string HubAddress = "10.0.0.2";

        sealed class Commands : IOperatorCommands
        {
            public readonly List<string> Calls = new List<string>();
            public void SwitchSpace(SpaceId space, string variant) { Calls.Add("switch " + space + " " + (variant ?? "-")); }
            public void Home() { Calls.Add("home"); }
            public void Recentre() { Calls.Add("recentre"); }
            public void SetQuality(int tier) { Calls.Add("quality " + tier); }
            public void Cue() { Calls.Add("cue"); }
            public void SetLock(bool on) { Calls.Add("lock " + on); }
        }

        sealed class Rig
        {
            public readonly ManualClock Clock = new ManualClock(1000000);
            public readonly LoopbackNetwork Network;
            public readonly LinkHost Host;
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly LinkClient Client;
            public readonly EventLog Events;
            public readonly LinkPump Pump;
            public readonly List<EventMessage> Logged = new List<EventMessage>();
            public readonly List<JsonValue> HubEvents = new List<JsonValue>();
            public readonly List<TelemetryMessage> HubRows = new List<TelemetryMessage>();
            public readonly List<uint> HubStateSeq = new List<uint>();
            public byte HubLastSpace = 255;
            public int HubOutlines, HubOutlinePoints, HubOutlineBoxes;
            public ParamEntry Rho;
            public StateMessage State = StateMessage.Identity;

            public Rig()
            {
                Network = new LoopbackNetwork(Clock);
                Host = new LinkHost(Network.CreateEndpoint(), Clock) { SessionId = "s-test" };
                Host.StateReceived += OnHubState;
                Host.TelemetryReceived += OnHubRow;
                Host.JsonReceived += (peer, id, body) => { if (id == MessageId.Event) HubEvents.Add(body.Clone()); };
                Host.RoomOutlineReceived += (peer, outline) => { HubOutlines++; HubOutlinePoints = outline.PointCount; HubOutlineBoxes = outline.BoxCount; };

                Rho = Registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
                Client = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Phone, Registry) { Device = "test-phone" };
                Events = new EventLog(Clock, null);
                Events.Logged += Logged.Add;
                Pump = new LinkPump(Client, Events);
                Pump.SetAddress(HubAddress, Port);
            }

            void OnHubState(int peer, in StateMessage state, byte[] raw, int offset, int length)
            {
                HubStateSeq.Add(state.Seq);
                HubLastSpace = state.SpaceId;
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

            /// <summary>Runs until the link is CONNECTED; returns the milliseconds it took, or -1.</summary>
            public double RunUntilConnected(double limitMs)
            {
                for (double t = 0; t < limitMs; t += 5.0)
                {
                    if (Pump.State == LinkState.Connected) return t;
                    Step();
                }
                return Pump.State == LinkState.Connected ? limitMs : -1.0;
            }

            public void Connect()
            {
                Host.Start(Port);
                Assert.That(Pump.Start(), Is.True);
                Assert.That(RunUntilConnected(3000), Is.GreaterThanOrEqualTo(0.0), "the handshake did not finish");
            }

            public int Count(string ev)
            {
                int n = 0;
                for (int i = 0; i < Logged.Count; i++) if (Logged[i].Ev == ev) n++;
                return n;
            }

            public EventMessage Last(string ev)
            {
                for (int i = Logged.Count - 1; i >= 0; i--) if (Logged[i].Ev == ev) return Logged[i];
                return null;
            }

            public JsonValue LastHubEvent(string ev)
            {
                for (int i = HubEvents.Count - 1; i >= 0; i--) if (HubEvents[i].GetString("ev") == ev) return HubEvents[i];
                return null;
            }
        }

        [Test]
        public void Connects_to_the_stored_address_and_state_flows_every_frame()
        {
            var rig = new Rig();
            rig.State.SpaceId = 1;
            rig.State.SceneId = 1;
            rig.Connect();
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.Host.HasPhone, Is.True);
            Assert.That(rig.Client.Address, Is.EqualTo(HubAddress));

            int before = rig.HubStateSeq.Count;
            rig.Run(1000);
            int received = rig.HubStateSeq.Count - before;
            Assert.That(received, Is.EqualTo(200), "one State per frame, 200 frames");
            Assert.That(rig.HubLastSpace, Is.EqualTo(1));
            for (int i = 1; i < rig.HubStateSeq.Count; i++)
                Assert.That(rig.HubStateSeq[i], Is.EqualTo(rig.HubStateSeq[i - 1] + 1), "sequence numbers rise by one");

            Assert.That(rig.Last(LinkEvents.Link).Data.GetString("state"), Is.EqualTo("connected"));
            Assert.That(rig.LastHubEvent("space.entered").GetString("scene"), Is.EqualTo("S1"));
        }

        [Test]
        public void A_parameter_set_is_clamped_applied_and_echoed()
        {
            var rig = new Rig();
            rig.Connect();
            rig.HubEvents.Clear();
            rig.Logged.Clear();

            Assert.That(rig.Host.SendParam("curved.cullRho", 99f), Is.True);
            rig.Run(100);
            Assert.That(rig.Rho.Float, Is.EqualTo(3.5f), "clamped to the declared range on the phone");

            JsonValue echo = rig.LastHubEvent(LinkEvents.Param);
            Assert.That(echo, Is.Not.Null, "the applied value goes back to the Station");
            Assert.That(echo["data"].GetString("name"), Is.EqualTo("curved.cullRho"));
            Assert.That(echo["data"].GetFloat("value"), Is.EqualTo(3.5f));
            Assert.That(echo["data"].GetString("source"), Is.EqualTo("remote"));

            EventMessage local = rig.Last(LinkEvents.Param);
            Assert.That(local, Is.Not.Null, "and into the phone's own event log");
            Assert.That(local.Data.GetFloat("value"), Is.EqualTo(3.5f));
            Assert.That(rig.Count(LinkEvents.Param), Is.EqualTo(1));

            // Under demo lock the set is ignored.
            rig.Client.DemoLock = true;
            rig.Host.SendParam("curved.cullRho", 2f);
            rig.Run(100);
            Assert.That(rig.Rho.Float, Is.EqualTo(3.5f));
        }

        [Test]
        public void A_value_written_by_code_every_frame_is_logged_once_per_second()
        {
            var rig = new Rig();
            ParamEntry w = rig.Registry.DeclareFloat("slicer.w", "slicer", -1.1f, 1.1f, 0f);
            rig.Client.EchoParamChanges = false;
            rig.Connect();
            rig.Logged.Clear();
            for (int i = 0; i < 400; i++)
            {
                w.Set((i % 100) * 0.01f);
                rig.Step();
            }
            Assert.That(rig.Count(LinkEvents.Param), Is.InRange(1, 3), "two seconds of per-frame writes");
        }

        [Test]
        public void Operator_commands_reach_the_handler_as_plain_calls_and_are_logged()
        {
            var rig = new Rig();
            var commands = new Commands();
            rig.Pump.Commands = commands;
            rig.Connect();

            rig.Host.SendCommand(OperatorCommand.Stop());
            rig.Host.SendCommand(OperatorCommand.SwitchSpace(1, "klein", JsonValue.Object().Set("curved.cullRho", 2.5)));
            rig.Host.SendCommand(OperatorCommand.Quality(2));
            rig.Host.SendCommand(OperatorCommand.Home());
            rig.Host.SendCommand(OperatorCommand.Recentre());
            rig.Host.SendCommand(OperatorCommand.Lock(true));
            rig.Run(100);

            Assert.That(commands.Calls, Is.EqualTo(new[] { "cue", "switch Glued klein", "quality 2", "home", "recentre", "lock True" }));
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f), "the arguments of a scene command are parameter values");
            Assert.That(rig.Pump.CommandsApplied, Is.EqualTo(6));
            Assert.That(rig.Count(LinkEvents.Command), Is.EqualTo(6), "every command is written to the log");
            Assert.That(rig.Client.DemoLock, Is.True);

            // Commands still work under the lock.
            rig.Host.SendCommand(OperatorCommand.Stop());
            rig.Run(50);
            Assert.That(commands.Calls.Count, Is.EqualTo(7));
        }

        [Test]
        public void With_no_handler_commands_wait_in_a_queue_the_caller_reads()
        {
            var rig = new Rig();
            rig.Connect();
            rig.Host.SendCommand(OperatorCommand.Stop());
            rig.Host.SendCommand(OperatorCommand.Home());
            rig.Run(100);

            OperatorCommand command;
            Assert.That(rig.Pump.TryDequeueCommand(out command), Is.True);
            Assert.That(command.Kind, Is.EqualTo(OperatorCommandKind.Cue));
            Assert.That(rig.Pump.TryDequeueCommand(out command), Is.True);
            Assert.That(command.Kind, Is.EqualTo(OperatorCommandKind.Home));
            Assert.That(rig.Pump.TryDequeueCommand(out command), Is.False);
            Assert.That(rig.Count(LinkEvents.Command), Is.EqualTo(2));
        }

        [Test]
        public void A_handler_that_throws_does_not_stop_the_commands_behind_it()
        {
            var rig = new Rig();
            var commands = new ThrowingCommands();
            rig.Pump.Commands = commands;
            rig.Connect();
            rig.Host.SendCommand(OperatorCommand.Home());
            rig.Host.SendCommand(OperatorCommand.Stop());
            Assert.DoesNotThrow(() => rig.Run(100));
            Assert.That(commands.Cues, Is.EqualTo(1));
        }

        sealed class ThrowingCommands : IOperatorCommands
        {
            public int Cues;
            public void SwitchSpace(SpaceId space, string variant) { }
            public void Home() { throw new InvalidOperationException("no room"); }
            public void Recentre() { }
            public void SetQuality(int tier) { }
            public void Cue() { Cues++; }
            public void SetLock(bool on) { }
        }

        [Test]
        public void The_app_runs_unchanged_with_no_station_and_connects_within_a_second_of_one_appearing()
        {
            var rig = new Rig();
            Assert.That(rig.Pump.Start(), Is.True);
            // Ten seconds with nobody listening: nothing throws, nothing is sent, the state stays OFFLINE.
            rig.Run(10000);
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Offline));
            Assert.That(rig.HubStateSeq.Count, Is.EqualTo(0));

            rig.Host.Start(Port);
            double ms = rig.RunUntilConnected(4000);
            Assert.That(ms, Is.GreaterThanOrEqualTo(0.0));
            // One attempt per second, then Hello, Welcome and the eight-ping burst.
            Assert.That(ms, Is.LessThanOrEqualTo(2000.0), "the retry is every second, not the four-second back-off");
        }

        [Test]
        public void Offline_and_reconnect()
        {
            var rig = new Rig();
            rig.Connect();
            rig.Network.Cut = true;
            rig.Run(4000);
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Offline));
            int seen = rig.HubStateSeq.Count;
            rig.Run(1000);
            Assert.That(rig.HubStateSeq.Count, Is.EqualTo(seen), "nothing arrives while the cable is out");

            rig.Network.Cut = false;
            double ms = rig.RunUntilConnected(5000);
            Assert.That(ms, Is.GreaterThanOrEqualTo(0.0), "the link did not come back");
            Assert.That(ms, Is.LessThanOrEqualTo(5000.0), "back within five seconds (P1-D11)");
            rig.Run(200);
            Assert.That(rig.HubStateSeq.Count, Is.GreaterThan(seen));

            var states = new List<string>();
            for (int i = 0; i < rig.Logged.Count; i++)
                if (rig.Logged[i].Ev == LinkEvents.Link) states.Add(rig.Logged[i].Data.GetString("state"));
            Assert.That(states, Does.Contain("offline"));
            Assert.That(states[states.Count - 1], Is.EqualTo("connected"));
        }

        [Test]
        public void Link_disabled_sends_nothing_but_the_edges_are_still_logged()
        {
            var rig = new Rig();
            rig.Host.Start(Port);
            // Never started: link.enabled is off.
            rig.Run(500);
            rig.State.BumpLevel = 2;
            rig.Run(100);
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Offline));
            Assert.That(rig.Host.HasPhone, Is.False);
            Assert.That(rig.Count("bump.level"), Is.EqualTo(1));

            rig.Pump.Start();
            Assert.That(rig.RunUntilConnected(3000), Is.GreaterThanOrEqualTo(0.0));
            rig.Pump.Stop("link.enabled off");
            rig.Run(500);
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Offline));
            Assert.That(rig.Pump.IsStarted, Is.False);
        }

        [Test]
        public void Edges_of_the_state_become_events_and_feed_the_telemetry_row()
        {
            var rig = new Rig();
            rig.State.SpaceId = 0;
            rig.Connect();
            rig.Logged.Clear();
            var context = new PerfRowContext();
            rig.Pump.TakeSecond(ref context);

            byte spaceBefore = 255, spaceAfter = 255;
            rig.Pump.SpaceChanged += (a, b) => { spaceBefore = a; spaceAfter = b; };

            rig.State.BumpLevel = 1; rig.Step();
            rig.State.BumpLevel = 3; rig.Step();
            rig.State.BumpLevel = 0; rig.Step();
            rig.State.CueActive = true; rig.Step();          // an operator cue: no level
            rig.State.CueActive = false; rig.Step();
            rig.State.Tracking = 2; rig.Step();
            rig.State.Tracking = 0; rig.Step();
            rig.State.PoseJumpRejected = true; rig.Step();
            rig.State.PoseJumpRejected = false; rig.Step();
            rig.State.SpaceId = 1; rig.State.SceneId = 1; rig.State.P3 = 0f; rig.State.P4 = 5f; rig.State.WorldVer = 7; rig.Step();
            rig.Run(100);

            Assert.That(rig.Count("bump.level"), Is.EqualTo(3));
            Assert.That(rig.Count("tracking.lost"), Is.EqualTo(1));
            Assert.That(rig.Count("tracking.back"), Is.EqualTo(1));
            Assert.That(rig.Count("pose.jump"), Is.EqualTo(1));
            Assert.That(rig.Count("space.entered"), Is.EqualTo(1));
            Assert.That(rig.Last("space.entered").Data.GetString("from"), Is.EqualTo("S0"));
            Assert.That(rig.Last("space.entered").Data.GetString("to"), Is.EqualTo("S1"));
            Assert.That(rig.Last("space.entered").Scene, Is.EqualTo("S1"));
            Assert.That(rig.Last("world").Data.GetInt("worldVer"), Is.EqualTo(7));
            Assert.That(spaceBefore, Is.EqualTo(0));
            Assert.That(spaceAfter, Is.EqualTo(1));
            Assert.That(rig.Pump.BumpLevel, Is.EqualTo(0));

            rig.Pump.TakeSecond(ref context);
            Assert.That(context.BumpMax, Is.EqualTo(3), "the second's highest level");
            Assert.That(context.CueCount, Is.EqualTo(3), "two rises and one operator cue");
            Assert.That(context.SpaceId, Is.EqualTo(1));
            Assert.That(context.SceneId, Is.EqualTo(1));
            Assert.That(context.Copies, Is.EqualTo(25), "five copies per side in the torus");
            Assert.That(context.StateHz, Is.GreaterThan(20));
            rig.Pump.TakeSecond(ref context);
            Assert.That(context.BumpMax, Is.EqualTo(0));
            Assert.That(context.StateHz, Is.EqualTo(0));

            // The events reached the Station too.
            Assert.That(rig.LastHubEvent("pose.jump"), Is.Not.Null);
        }

        [Test]
        public void Telemetry_rows_go_out_once_each_and_wait_out_an_outage()
        {
            var rig = new Rig();
            rig.Connect();
            var row = new TelemetryMessage { TimeUs = 5, Fps = 60f, SpaceId = 1, Thermal = 1 };
            rig.Pump.SubmitRow(in row);
            rig.Run(50);
            Assert.That(rig.HubRows.Count, Is.EqualTo(1));
            Assert.That(rig.HubRows[0].Fps, Is.EqualTo(60f));
            Assert.That(rig.HubRows[0].Thermal, Is.EqualTo(1));

            rig.Network.Cut = true;
            rig.Run(4000);
            for (int i = 0; i < 5; i++)
            {
                row.TimeUs = 100 + i;
                rig.Pump.SubmitRow(in row);
            }
            Assert.That(rig.Pump.RowsWaiting, Is.EqualTo(5));
            rig.Network.Cut = false;
            Assert.That(rig.RunUntilConnected(5000), Is.GreaterThanOrEqualTo(0.0));
            rig.Run(200);
            Assert.That(rig.Pump.RowsWaiting, Is.EqualTo(0));
            Assert.That(rig.HubRows.Count, Is.EqualTo(6), "the Station's CSV has no hole");
            for (int i = 0; i < 5; i++) Assert.That(rig.HubRows[1 + i].TimeUs, Is.EqualTo(100 + i), "in order");

            // phoneCsv and hud modes send nothing.
            rig.Pump.Mode = TelemetryMode.PhoneCsv;
            rig.Pump.SubmitRow(in row);
            rig.Pump.Mode = TelemetryMode.Hud;
            rig.Pump.SubmitRow(in row);
            rig.Run(50);
            Assert.That(rig.HubRows.Count, Is.EqualTo(6));
        }

        [Test]
        public void The_backlog_keeps_the_newest_600_rows()
        {
            var rig = new Rig();
            rig.Pump.Start();
            var row = new TelemetryMessage();
            for (int i = 0; i < 700; i++)
            {
                row.TimeUs = i;
                rig.Pump.SubmitRow(in row);
            }
            Assert.That(rig.Pump.RowsWaiting, Is.EqualTo(LinkPump.RowBacklog));
            rig.Host.Start(Port);
            Assert.That(rig.RunUntilConnected(4000), Is.GreaterThanOrEqualTo(0.0));
            rig.Run(500);
            Assert.That(rig.HubRows.Count, Is.EqualTo(600));
            Assert.That(rig.HubRows[0].TimeUs, Is.EqualTo(100));
            Assert.That(rig.HubRows[599].TimeUs, Is.EqualTo(699));
        }

        [Test]
        public void The_room_outline_is_sent_now_and_again_after_a_reconnect()
        {
            var rig = new Rig();
            var room = new RoomModel
            {
                Id = "room-1",
                Outline = new[] { new float2(-2f, -1.5f), new float2(2f, -1.5f), new float2(2f, 1.5f), new float2(-2f, 1.5f) },
                FreeRect = new FreeRect(3f, 2f),
                Objects = new[] { new RoomObject { Id = 1, Base = new float3(1f, 0f, 1f), Size = new float3(0.5f, 0.8f, 0.5f) } }
            };
            // Before the link is up: kept, and sent after the handshake.
            rig.Pump.SetRoom(room);
            rig.Connect();
            rig.Run(100);
            Assert.That(rig.HubOutlines, Is.EqualTo(1));
            Assert.That(rig.HubOutlinePoints, Is.EqualTo(4));
            Assert.That(rig.HubOutlineBoxes, Is.EqualTo(1));

            rig.Network.Cut = true;
            rig.Run(4000);
            rig.Network.Cut = false;
            Assert.That(rig.RunUntilConnected(5000), Is.GreaterThanOrEqualTo(0.0));
            rig.Run(100);
            Assert.That(rig.HubOutlines, Is.EqualTo(2));
        }

        [Test]
        public void Events_logged_while_offline_are_sent_after_the_handshake()
        {
            var rig = new Rig();
            rig.Pump.Start();
            rig.Run(200);
            rig.Events.Log("scan.state", "state", "done");
            rig.Events.Log("room.ready", "objects", 3);
            rig.Events.LogJson("asset", "{\"boxId\":2,\"step\":\"placed\"}");
            rig.Events.LogJson("note", "not json");
            Assert.That(rig.Events.Queued, Is.GreaterThanOrEqualTo(4));

            rig.Host.Start(Port);
            Assert.That(rig.RunUntilConnected(4000), Is.GreaterThanOrEqualTo(0.0));
            rig.Run(300);
            Assert.That(rig.Events.Queued, Is.EqualTo(0));
            Assert.That(rig.LastHubEvent("scan.state")["data"].GetString("state"), Is.EqualTo("done"));
            Assert.That(rig.LastHubEvent("room.ready")["data"]["objects"].IsInteger, Is.True);
            Assert.That(rig.LastHubEvent("asset")["data"].GetString("step"), Is.EqualTo("placed"));
            Assert.That(rig.LastHubEvent("note")["data"].GetString("text"), Is.EqualTo("not json"));

            // The line format of the phone's file is the wire format.
            EventMessage e = rig.Last("room.ready");
            Assert.That(e.ToJsonLine(), Is.EqualTo("{\"t_us\":" + e.TimeUs + ",\"scene\":\"S0\",\"ev\":\"room.ready\",\"data\":{\"objects\":3}}"));
        }

        [Test]
        public void Pairing_drives_the_link_and_remembers_the_station()
        {
            var rig = new Rig();
            var store = new MemoryPairingStore();
            store.SetString(PairingController.AddressKey, "10.0.0.9:9050");
            var pairing = new PairingController(store, rig.Pump);
            Assert.That(rig.Pump.Host, Is.EqualTo("10.0.0.9"), "the stored address is where the link goes");

            rig.Host.Start(Port);
            Assert.That(pairing.TrySetAddress("atlas://" + HubAddress + ":" + Port), Is.True);
            pairing.BeginPairing();
            Assert.That(rig.Pump.IsStarted, Is.True);
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Trying));
            Assert.That(rig.RunUntilConnected(3000), Is.GreaterThanOrEqualTo(0.0));
            rig.Step();
            Assert.That(pairing.Phase, Is.EqualTo(PairingPhase.Connected));
            Assert.That(pairing.WasPaired, Is.True);
            Assert.That(store.GetInt(PairingController.PairedKey, 0), Is.EqualTo(1));

            LinkStatus status;
            bool forge;
            pairing.GetStatus(out status, out forge);
            Assert.That(status.State, Is.EqualTo(LinkState.Connected));
            Assert.That(status.Address, Is.EqualTo(HubAddress + ":" + Port));
            Assert.That(forge, Is.False);
        }

        [Test]
        public void The_state_path_allocates_nothing()
        {
            var rig = new Rig();
            rig.State.SpaceId = 1;
            rig.Connect();
            var row = new TelemetryMessage { Fps = 60f };
            var context = new PerfRowContext();
            // Warm up every path once, including a telemetry row.
            for (int i = 0; i < 400; i++)
            {
                rig.Step();
                if (i % 200 == 0) { rig.Pump.TakeSecond(ref context); rig.Pump.SubmitRow(in row); }
            }

            long allocated = 0;
            for (int i = 0; i < 600; i++)
            {
                rig.Clock.AdvanceMs(5.0);
                rig.Host.Poll();
                rig.State.HeadPosX = i * 0.001f;
                long before = GC.GetAllocatedBytesForCurrentThread();
                rig.Pump.Send(in rig.State);
                rig.Pump.Tick();
                if (i % 200 == 0) { rig.Pump.TakeSecond(ref context); rig.Pump.SubmitRow(in row); }
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                rig.Host.Poll();
            }
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Connected));
            Assert.That(allocated, Is.EqualTo(0), "steps 8a and 8b must not allocate in steady state");
        }

        [Test]
        public void The_state_path_allocates_nothing_with_no_station()
        {
            var rig = new Rig();
            rig.Pump.Start();
            rig.Run(3000);
            long allocated = 0;
            for (int i = 0; i < 600; i++)
            {
                rig.Clock.AdvanceMs(5.0);
                long before = GC.GetAllocatedBytesForCurrentThread();
                rig.Pump.Send(in rig.State);
                rig.Pump.Tick();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(rig.Pump.State, Is.EqualTo(LinkState.Offline));
            Assert.That(allocated, Is.EqualTo(0), "retrying every second must not allocate either");
        }
    }
}
