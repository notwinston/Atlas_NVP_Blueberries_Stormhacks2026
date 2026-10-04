using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// LinkClient and LinkHost talking over the in-memory transport: the handshake, the relay to Spectators, the
    /// six operator commands, live tuning, the lock, and the P1-D01 acceptance line "60 Hz State survives 2%
    /// simulated loss".
    /// </summary>
    public sealed class Link_Exchange
    {
        [Test]
        public void Handshake_runs_hello_welcome_pings_manifest()
        {
            var rig = new LinkRig();
            rig.Host.WorldHash = "0123456789abcdef";
            rig.Host.RoomId = "room_7";
            rig.Phone.WorldHash = "0123456789abcdef";
            rig.Phone.Build = "abc1234";

            var order = new List<string>();
            HelloMessage hello = null;
            rig.Host.PeerJoined += (peer, h) => { hello = h; order.Add("hello"); };
            rig.Host.ManifestReceived += (peer, m) => order.Add("manifest:" + m.Params.Count);
            rig.Phone.Welcomed += w => order.Add("welcome");
            rig.Phone.StateChanged += (previous, current) => order.Add(current.ToString());

            rig.StartAll();
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Offline));
            Assert.That(rig.Phone.IsReady, Is.False);
            rig.Run(1500);

            Assert.That(order, Is.EqualTo(new[] { "Resync", "hello", "welcome", "Connected", "manifest:3" }));
            Assert.That(hello.Role, Is.EqualTo(LinkRole.Phone));
            Assert.That(hello.Device, Is.EqualTo("test-phone"));
            Assert.That(hello.Build, Is.EqualTo("abc1234"));
            Assert.That(hello.ProtoMajor, Is.EqualTo(1));
            Assert.That(rig.Phone.IsReady, Is.True);
            Assert.That(rig.Phone.Welcome.SessionId, Is.EqualTo("s-test"));
            Assert.That(rig.Phone.Welcome.ForgeUrl, Is.EqualTo("http://10.0.0.2:8000"));
            Assert.That(rig.Phone.Welcome.RoomId, Is.EqualTo("room_7"));
            Assert.That(rig.Phone.WorldMatches, Is.True);
            Assert.That(rig.Phone.ClockSync.SampleCount, Is.GreaterThanOrEqualTo(8));
            // One shared clock and no delay; the pong is read one 5 ms rig step after it was written.
            Assert.That(Math.Abs(rig.Phone.ClockSync.OffsetUs), Is.LessThanOrEqualTo(2500L));
            Assert.That(rig.Host.HasPhone, Is.True);
            Assert.That(rig.Host.RoleOf(rig.Host.PhonePeer), Is.EqualTo(LinkRole.Phone));
            Assert.That(rig.Host.Manifest.Params[0].Name, Is.EqualTo("curved.cullRho"));
            Assert.That(rig.Host.PeerCount, Is.EqualTo(1));
        }

        [Test]
        public void World_mismatch_is_visible_to_the_client()
        {
            var rig = new LinkRig();
            rig.Host.WorldHash = "aaaaaaaaaaaaaaaa";
            LinkClient spectator = rig.AddClient(LinkRole.Spectator);
            spectator.WorldHash = "bbbbbbbbbbbbbbbb";
            rig.Connect();
            Assert.That(spectator.IsReady, Is.True);
            Assert.That(spectator.WorldMatches, Is.False, "the Spectator shows 'world mismatch' and draws no world");
            Assert.That(rig.Phone.WorldMatches, Is.False);
        }

        [Test]
        public void State_at_60_hz_survives_2_percent_loss_and_reaches_spectators()
        {
            var rig = new LinkRig(7);
            LinkClient spectator = rig.AddClient(LinkRole.Spectator);
            LinkClient editor = rig.AddClient(LinkRole.Editor);
            LinkClient console = rig.AddClient(LinkRole.Console);
            rig.Connect();
            Assert.That(rig.Host.CountRole(LinkRole.Spectator), Is.EqualTo(1));
            Assert.That(rig.Host.PeerCount, Is.EqualTo(4));

            rig.Network.Loss = 0.02;
            rig.Network.DelayMs = 2.0;
            rig.Network.JitterMs = 3.0;

            int atHub = 0, atSpectator = 0, atEditor = 0, atConsole = 0, rawLength = 0;
            uint lastHubSeq = 0, lastSpectatorSeq = 0;
            bool hubOrdered = true, spectatorOrdered = true;
            StateMessage lastSeen = default(StateMessage);
            rig.Host.StateReceived += (int peer, in StateMessage s, byte[] raw, int offset, int length) =>
            {
                if (atHub > 0 && s.Seq <= lastHubSeq) hubOrdered = false;
                lastHubSeq = s.Seq;
                rawLength = length;
                atHub++;
            };
            spectator.StateReceived += (in StateMessage s) =>
            {
                if (atSpectator > 0 && s.Seq <= lastSpectatorSeq) spectatorOrdered = false;
                lastSpectatorSeq = s.Seq;
                lastSeen = s;
                atSpectator++;
            };
            editor.StateReceived += (in StateMessage s) => atEditor++;
            console.StateReceived += (in StateMessage s) => atConsole++;

            const int frames = 600; // ten seconds at 60 Hz
            StateMessage state = StateMessage.Identity;
            state.SceneId = 3;
            state.SpaceId = 2;
            state.SetTile(11, -22, 33, -44, 55, -66);
            int sent = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                state.HeadPosX = frame * 0.01f;
                state.SetXform(2, frame * 0.5);
                state.WorldVer = 9;
                if (rig.Phone.SendState(ref state)) sent++;
                rig.Step(1000.0 / 60.0);
            }
            rig.Run(100);

            Assert.That(sent, Is.EqualTo(frames), "one State per frame");
            Assert.That(state.Seq, Is.EqualTo((uint)(frames - 1)), "the link stamps seq");
            Assert.That(rawLength, Is.EqualTo(202));
            Assert.That(hubOrdered && spectatorOrdered, Is.True, "sequenced: never out of order");
            // L3: at least 50 Hz at the Hub with under 2% configured loss (5% allows for the reorder drops).
            Assert.That(atHub, Is.GreaterThanOrEqualTo((int)(frames * 0.95)));
            Assert.That(atHub / 10.0, Is.GreaterThanOrEqualTo(50.0));
            Assert.That(atSpectator, Is.GreaterThanOrEqualTo((int)(frames * 0.90)), "two lossy hops");
            Assert.That(atEditor, Is.GreaterThanOrEqualTo((int)(frames * 0.90)), "an Editor watches like a Spectator");
            Assert.That(atConsole, Is.EqualTo(0), "a console is not sent State");

            // What the Spectator redraws from is what the phone sent.
            Assert.That(lastSeen.SceneId, Is.EqualTo(3));
            Assert.That(lastSeen.SpaceId, Is.EqualTo(2));
            Assert.That(lastSeen.GetTile(3), Is.EqualTo(-44L));
            Assert.That(lastSeen.GetXform(2), Is.EqualTo(lastSeen.Seq * 0.5));
            Assert.That(lastSeen.GetXform(4), Is.EqualTo(1.0));
            Assert.That(lastSeen.WorldVer, Is.EqualTo(9));
            Assert.That(lastSeen.TimeUs, Is.GreaterThan(0L));

            // Reliable traffic is untouched by the loss.
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.Network.PacketsLost, Is.GreaterThan(0L));
        }

        [Test]
        public void SendState_and_SendTelemetry_allocate_nothing()
        {
            var rig = new LinkRig();
            LinkClient spectator = rig.AddClient(LinkRole.Spectator);
            int hubStates = 0, spectatorStates = 0, rows = 0;
            rig.Host.StateReceived += (int peer, in StateMessage s, byte[] raw, int offset, int length) => hubStates++;
            rig.Host.TelemetryReceived += (int peer, in TelemetryMessage t) => rows++;
            spectator.StateReceived += (in StateMessage s) => spectatorStates++;
            rig.Connect();

            StateMessage state = StateMessage.Identity;
            var telemetry = new TelemetryMessage { Fps = 60f };
            // Warm-up: pools, lists and JIT settle before the measurement, and the 1 Hz clock ping is included.
            for (int i = 0; i < 300; i++)
            {
                rig.Phone.SendState(ref state);
                if (i % 60 == 0) { telemetry.TimeUs = 0; rig.Phone.SendTelemetry(ref telemetry); }
                rig.Step(1000.0 / 60.0);
            }

            int before = hubStates;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++)
            {
                rig.Phone.SendState(ref state);
                if (i % 60 == 0) { telemetry.TimeUs = 0; rig.Phone.SendTelemetry(ref telemetry); }
                rig.Step(1000.0 / 60.0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(hubStates - before, Is.EqualTo(600));
            Assert.That(spectatorStates, Is.GreaterThanOrEqualTo(600));
            Assert.That(rows, Is.EqualTo(15));
            Assert.That(allocated, Is.EqualTo(0L),
                "State send, relay and receive, Telemetry, clock pings, watchdog and poll: 0 B per frame (section 7.5, rule 6)");
        }

        [Test]
        public void StateHz_thins_the_stream()
        {
            var rig = new LinkRig();
            int atHub = 0;
            rig.Host.StateReceived += (int peer, in StateMessage s, byte[] raw, int offset, int length) => atHub++;
            rig.Connect();
            StateMessage state = StateMessage.Identity;

            foreach (int hz in new[] { 60, 30, 10 })
            {
                rig.Phone.StateHz = hz;
                rig.Run(200, 1000.0 / 60.0);
                atHub = 0;
                for (int frame = 0; frame < 600; frame++)
                {
                    rig.Phone.SendState(ref state);
                    rig.Step(1000.0 / 60.0);
                }
                Assert.That(atHub, Is.InRange(hz * 10 - 2, hz * 10 + 2), hz + " Hz for ten seconds");
            }
            rig.Phone.StateHz = 500;
            Assert.That(rig.Phone.StateHz, Is.EqualTo(60));
            rig.Phone.StateHz = 0;
            Assert.That(rig.Phone.StateHz, Is.EqualTo(1));
        }

        [Test]
        public void Live_tuning_clamps_on_the_phone_and_echoes_the_applied_value()
        {
            var rig = new LinkRig();
            LinkClient console = rig.AddClient(LinkRole.Console);
            ParamManifestMessage seenManifest = null;
            console.ManifestReceived += m => seenManifest = m;
            var echoes = new List<string>();
            rig.Host.JsonReceived += (peer, id, body) =>
            {
                if (id == MessageId.Event && body.GetString("ev") == "param")
                    echoes.Add(body["data"].GetString("name") + "=" + body["data"]["value"].ToJson() + ":" + body["data"].GetString("source"));
            };
            int applied = -1;
            rig.Phone.ParamsApplied += n => applied = n;
            rig.Connect();

            Assert.That(seenManifest, Is.Not.Null, "the Hub forwards the phone's manifest to consoles and editors");
            Assert.That(seenManifest.Params.Count, Is.EqualTo(3));

            // Hub side (the web Console): one slider, out of range.
            Assert.That(rig.Host.SendParam("curved.cullRho", 50f), Is.True);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(3.5f));
            Assert.That(applied, Is.EqualTo(1));
            Assert.That(echoes, Is.EqualTo(new[] { "curved.cullRho=3.5:remote" }));

            // Link console: a ParamSet by id goes through the Hub to the phone.
            Assert.That(console.SendParam(rig.Copies.Id, 4f), Is.True);
            rig.Run(50);
            Assert.That(rig.Copies.Int, Is.EqualTo(4));

            // A preset replays all its values in one ParamSet.
            int sets = 0;
            rig.Phone.ParamsApplied += n => sets++;
            int count = rig.Host.SendPreset(AtlasJson.Parse(
                "{\"values\":{\"curved.cullRho\":2.5,\"glued.copies\":5,\"pose.gyroPredict\":false,\"unknown.name\":1}}"));
            Assert.That(count, Is.EqualTo(3));
            rig.Run(50);
            Assert.That(sets, Is.EqualTo(1));
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f));
            Assert.That(rig.Copies.Int, Is.EqualTo(5));
            Assert.That(rig.Predict.Bool, Is.False);

            // A change made on the phone is echoed as local.
            echoes.Clear();
            rig.Rho.Set(3f);
            rig.Run(50);
            Assert.That(echoes, Is.EqualTo(new[] { "curved.cullRho=3.0:local" }));

            Assert.That(rig.Host.SendParam("no.such.param", 1f), Is.False);
            // A phone does not obey another phone-role packet path: a spectator's ParamSet is not forwarded.
            LinkClient spectator = rig.AddClient(LinkRole.Spectator);
            spectator.Start("10.0.0.2", LinkRig.Port);
            rig.Run(1500);
            spectator.SendParam(rig.Rho.Id, 2f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(3f));
        }

        [Test]
        public void The_six_operator_commands_reach_the_phone_queue_in_order()
        {
            var rig = new LinkRig();
            LinkClient console = rig.AddClient(LinkRole.Console);
            rig.Connect();

            rig.Host.SendCommand(OperatorCommand.SwitchSpace(2, "mode2"));
            rig.Host.SendCommand(OperatorCommand.Home());
            rig.Host.SendCommand(OperatorCommand.Recentre());
            console.SendCommand(OperatorCommand.Quality(1));
            console.SendCommand(OperatorCommand.Stop());
            rig.Run(20);
            rig.Host.SendCommand(OperatorCommand.Lock(true));
            Assert.That(rig.Phone.PendingCommands, Is.EqualTo(5), "queued, not applied inside the network callback");
            rig.Run(20);
            Assert.That(rig.Phone.PendingCommands, Is.EqualTo(6));

            var kinds = new List<OperatorCommandKind>();
            OperatorCommand command;
            OperatorCommand first = default(OperatorCommand), quality = default(OperatorCommand), lockCommand = default(OperatorCommand);
            while (rig.Phone.TryDequeueCommand(out command))
            {
                kinds.Add(command.Kind);
                if (command.Kind == OperatorCommandKind.SwitchSpace) first = command;
                if (command.Kind == OperatorCommandKind.Quality) quality = command;
                if (command.Kind == OperatorCommandKind.Lock) lockCommand = command;
            }
            Assert.That(kinds, Is.EquivalentTo(new[]
            {
                OperatorCommandKind.SwitchSpace, OperatorCommandKind.Home, OperatorCommandKind.Recentre,
                OperatorCommandKind.Quality, OperatorCommandKind.Cue, OperatorCommandKind.Lock
            }));
            Assert.That(kinds.GetRange(0, 3), Is.EqualTo(new[]
            {
                OperatorCommandKind.SwitchSpace, OperatorCommandKind.Home, OperatorCommandKind.Recentre
            }), "reliable and ordered");
            Assert.That(kinds[5], Is.EqualTo(OperatorCommandKind.Lock));
            Assert.That(first.Space, Is.EqualTo(2));
            Assert.That(first.Variant, Is.EqualTo("mode2"));
            Assert.That(quality.Tier, Is.EqualTo(1));
            Assert.That(lockCommand.On, Is.True);
            Assert.That(rig.Phone.TryDequeueCommand(out command), Is.False);
            Assert.That(rig.Phone.PendingCommands, Is.EqualTo(0));
        }

        [Test]
        public void Demo_lock_ignores_ParamSet_but_commands_still_work()
        {
            var rig = new LinkRig();
            rig.Connect();
            rig.Host.SendCommand(OperatorCommand.Lock(true));
            rig.Host.SendParam("curved.cullRho", 2.5f); // queued right behind the lock on the same channel
            rig.Run(50);
            Assert.That(rig.Phone.DemoLock, Is.True);
            Assert.That(rig.Host.DemoLock, Is.True);
            Assert.That(rig.Rho.Float, Is.EqualTo(3f), "ParamSet ignored under lock");

            rig.Host.SendCommand(OperatorCommand.Stop());
            rig.Run(50);
            OperatorCommand command;
            Assert.That(rig.Phone.TryDequeueCommand(out command) && command.Kind == OperatorCommandKind.Lock);
            Assert.That(rig.Phone.TryDequeueCommand(out command) && command.Kind == OperatorCommandKind.Cue, "STOP works under lock");

            StateMessage state = StateMessage.Identity;
            bool lockSeen = false;
            rig.Host.StateReceived += (int peer, in StateMessage s, byte[] raw, int offset, int length) => lockSeen = s.DemoLock;
            Assert.That(rig.Phone.SendStateCopy(in state), Is.True);
            rig.Run(20);
            Assert.That(lockSeen, Is.True, "State.flags bit 6");
            Assert.That(state.DemoLock, Is.False, "the caller's read-only state is left alone");

            rig.Host.SendCommand(OperatorCommand.Lock(false));
            rig.Run(20);
            rig.Host.SendParam("curved.cullRho", 2.5f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f));

            // A Hub started with the lock on locks every phone that joins.
            var locked = new LinkRig();
            locked.Host.DemoLock = true;
            locked.Connect();
            Assert.That(locked.Phone.DemoLock, Is.True);
        }

        [Test]
        public void Telemetry_events_logs_and_outline_reach_the_hub_and_the_outline_reaches_late_spectators()
        {
            var rig = new LinkRig();
            LinkClient early = rig.AddClient(LinkRole.Spectator);
            rig.Connect();

            var rows = new List<uint>();
            float fps = 0f;
            rig.Host.TelemetryReceived += (int peer, in TelemetryMessage t) => { rows.Add(t.Seq); fps = t.Fps; };
            var json = new List<string>();
            rig.Host.JsonReceived += (peer, id, body) => json.Add(MessageId.Name(id) + ":" + body.ToJson());
            int hubOutlinePoints = 0, earlyBoxes = 0, lateBoxes = 0;
            rig.Host.RoomOutlineReceived += (peer, o) => hubOutlinePoints = o.PointCount;
            early.RoomOutlineReceived += o => earlyBoxes = o.BoxCount;

            for (int i = 0; i < 3; i++)
            {
                var t = new TelemetryMessage { Fps = 59.5f, DrawCalls = 120 };
                Assert.That(rig.Phone.SendTelemetry(ref t), Is.True);
                Assert.That(t.TimeUs, Is.GreaterThan(0L));
            }
            Assert.That(rig.Phone.SendEvent("S1", "note", JsonValue.Object().Set("k", 1)), Is.True);
            Assert.That(rig.Phone.SendLog("warn", "tracking limited"), Is.True);

            var outline = new RoomOutlineMessage { Height = 2.4f };
            outline.AddPoint(0, 0); outline.AddPoint(4, 0); outline.AddPoint(4, 5); outline.AddPoint(0, 5);
            outline.AddBox(new RoomOutlineBox { Id = 1, Category = 2, W = 1, D = 1, H = 1 });
            outline.AddBox(new RoomOutlineBox { Id = 2, Category = 5, W = 2, D = 1, H = 0.5f });
            Assert.That(rig.Phone.SendRoomOutline(outline), Is.True);
            rig.Run(50);

            Assert.That(rows, Is.EqualTo(new uint[] { 0, 1, 2 }), "reliable, so the CSV has no holes");
            Assert.That(fps, Is.EqualTo(59.5f));
            Assert.That(json.Count, Is.EqualTo(2));
            Assert.That(json[0], Does.StartWith("Event:{\"t_us\":").And.EndWith(",\"scene\":\"S1\",\"ev\":\"note\",\"data\":{\"k\":1}}"));
            Assert.That(json[1], Does.StartWith("Log:{\"t_us\":").And.EndWith(",\"level\":\"warn\",\"msg\":\"tracking limited\"}"));
            Assert.That(hubOutlinePoints, Is.EqualTo(4));
            Assert.That(earlyBoxes, Is.EqualTo(2));

            LinkClient late = rig.AddClient(LinkRole.Spectator);
            late.RoomOutlineReceived += o => lateBoxes = o.BoxCount;
            late.Start("10.0.0.2", LinkRig.Port);
            rig.Run(1500);
            Assert.That(lateBoxes, Is.EqualTo(2), "a Spectator that joins later still gets the room");

            var statuses = new List<string>();
            rig.Phone.JobStatusReceived += s => statuses.Add("phone:" + s.State);
            late.JobStatusReceived += s => statuses.Add("spectator:" + s.State);
            rig.Host.SendJobStatus(new JobStatusMessage { JobId = "j-1", BoxId = 1, State = "generating", Source = "local" });
            rig.Run(50);
            Assert.That(statuses, Is.EquivalentTo(new[] { "phone:generating", "spectator:generating" }));
        }

        [Test]
        public void Unknown_ids_and_trailing_bytes_are_ignored_on_the_wire()
        {
            var rig = new LinkRig();
            rig.Connect();
            int hubStates = 0;
            StateMessage seen = default(StateMessage);
            rig.Host.StateReceived += (int peer, in StateMessage s, byte[] raw, int offset, int length) => { hubStates++; seen = s; };
            var json = new List<byte>();
            rig.Host.JsonReceived += (peer, id, body) => json.Add(id);
            ILinkTransport phoneWire = rig.Phone.Transport;
            ILinkTransport hubWire = rig.Host.Transport;
            int hubPeer = 0; // the phone's only peer

            // Unknown ids in both directions, including the reserved PeerState and an empty packet.
            foreach (byte id in new byte[] { MessageId.PeerState, 0x00, 0x05, 0x60, 0xFF })
            {
                var junk = new byte[40];
                junk[0] = id;
                Assert.That(phoneWire.Send(hubPeer, junk, 0, junk.Length, LinkDelivery.Reliable), Is.True);
                Assert.That(hubWire.Send(rig.Host.PhonePeer, junk, 0, junk.Length, LinkDelivery.Reliable), Is.True);
                Assert.That(hubWire.Send(rig.Host.PhonePeer, junk, 0, junk.Length, LinkDelivery.Sequenced), Is.True);
            }
            phoneWire.Send(hubPeer, new byte[0], 0, 0, LinkDelivery.Reliable);
            hubWire.Send(rig.Host.PhonePeer, new byte[0], 0, 0, LinkDelivery.Reliable);
            // Known ids, cut short: ignored as well.
            phoneWire.Send(hubPeer, new byte[] { MessageId.State, 1, 2, 3 }, 0, 4, LinkDelivery.Sequenced);
            hubWire.Send(rig.Host.PhonePeer, new byte[] { MessageId.ParamSet, 9 }, 0, 2, LinkDelivery.Reliable);
            hubWire.Send(rig.Host.PhonePeer, new byte[] { MessageId.OperatorCmd, (byte)'{' }, 0, 2, LinkDelivery.Reliable);
            rig.Run(50);
            Assert.That(hubStates, Is.EqualTo(0));
            Assert.That(json, Is.Empty);
            Assert.That(rig.Phone.PendingCommands, Is.EqualTo(0));
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));

            // A State from a future minor version, 30 bytes longer: read as far as this version knows.
            StateMessage state = StateMessage.Identity;
            state.SceneId = 77;
            var longer = new byte[StateMessage.Size + 30];
            state.WriteTo(longer, 0);
            phoneWire.Send(hubPeer, longer, 0, longer.Length, LinkDelivery.Sequenced);
            // A ParamSet with trailing bytes still applies.
            var set = new ParamSetMessage();
            set.Add(rig.Rho.Id, 2.25f);
            var setBytes = new byte[set.Size + 11];
            set.WriteTo(setBytes, 0);
            hubWire.Send(rig.Host.PhonePeer, setBytes, 0, setBytes.Length, LinkDelivery.Reliable);
            rig.Run(50);
            Assert.That(hubStates, Is.EqualTo(1));
            Assert.That(seen.SceneId, Is.EqualTo(77));
            Assert.That(rig.Rho.Float, Is.EqualTo(2.25f));
        }

        [Test]
        public void A_protocol_major_mismatch_is_refused_with_a_readable_reason()
        {
            var clock = new ManualClock(5000);
            var network = new LoopbackNetwork(clock);
            var host = new LinkHost(network.CreateEndpoint(), clock);
            host.Start(LinkRig.Port);
            int joined = 0;
            host.PeerJoined += (peer, hello) => joined++;

            // A hand-rolled client from the future.
            var wire = network.CreateEndpoint();
            var listener = new RecordingListener();
            wire.Listener = listener;
            wire.StartClient();
            wire.Connect("10.0.0.2", LinkRig.Port);
            for (int i = 0; i < 4; i++) { clock.AdvanceMs(5); host.Poll(); wire.Poll(); }
            Assert.That(listener.Connected, Is.EqualTo(1));

            byte[] hello2 = new HelloMessage { ProtoMajor = 2, ProtoMinor = 3, Role = LinkRole.Phone }.ToBytes();
            wire.Send(listener.Peer, hello2, 0, hello2.Length, LinkDelivery.Reliable);
            for (int i = 0; i < 60; i++) { clock.AdvanceMs(5); host.Poll(); wire.Poll(); }

            Assert.That(joined, Is.EqualTo(0));
            Assert.That(host.HasPhone, Is.False);
            Assert.That(listener.Disconnected, Is.EqualTo(1));
            Assert.That(listener.Packets.Count, Is.EqualTo(1));
            var bye = new ByeMessage();
            Assert.That(bye.ReadFrom(listener.Packets[0], 0, listener.Packets[0].Length), Is.True);
            Assert.That(bye.Reason, Is.EqualTo("protocol mismatch: the Hub speaks 1.0, the client sent 2.3"));
        }

        [Test]
        public void A_dropped_ParamSet_is_reported_and_the_snapshot_goes_back_to_what_the_phone_holds()
        {
            var rig = new LinkRig();
            rig.Connect();
            rig.Host.SendParam("curved.cullRho", 2.5f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f));

            // The phone locks itself (its menu, a build flag): the Hub does not know.
            rig.Phone.DemoLock = true;
            Assert.That(rig.Host.DemoLock, Is.False);
            string reason = null;
            int count = -1, restored = -1;
            var events = new List<JsonValue>();
            rig.Phone.ParamSetIgnored += (why, n) => { reason = why; count = n; };
            rig.Host.ParamSetIgnored += (why, n) => restored = n;
            rig.Host.JsonReceived += (peer, id, body) => { if (body.GetString("ev") == LinkEvents.ParamIgnoredEvent) events.Add(body.Clone()); };

            var set = new ParamSetMessage();
            set.Add(rig.Rho.Id, 3.25f);
            set.Add(rig.Copies.Id, 4f);
            Assert.That(rig.Host.SendParamSet(set), Is.True);
            rig.Run(50);

            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f), "ignored under lock");
            Assert.That(reason, Is.EqualTo("lock"));
            Assert.That(count, Is.EqualTo(2));
            Assert.That(events.Count, Is.EqualTo(1), "one param.ignored event reaches the Hub");
            Assert.That(events[0]["data"].GetString("reason"), Is.EqualTo("lock"));
            Assert.That(events[0]["data"].GetInt("count"), Is.EqualTo(2));
            Assert.That(restored, Is.EqualTo(2));
            float known;
            Assert.That(rig.Host.TryGetSnapshotValue("curved.cullRho", out known), Is.True);
            Assert.That(known, Is.EqualTo(2.5f), "the snapshot is what the phone holds, not what was asked");
            Assert.That(rig.Host.TryGetSnapshotValue("glued.copies", out known), Is.True);
            Assert.That(known, Is.EqualTo(5f));

            // A reconnect replays nothing that was never applied.
            rig.Phone.DemoLock = false;
            rig.Phone.ReconnectNow();
            rig.Run(2000);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.Rho.Float, Is.EqualTo(2.5f));
            Assert.That(rig.Copies.Int, Is.EqualTo(5));
        }

        [Test]
        public void Stop_delivers_the_Bye_reason_with_the_disconnect_in_both_directions()
        {
            var rig = new LinkRig();
            LinkClient spectator = rig.AddClient(LinkRole.Spectator);
            rig.Connect();
            var hubLog = new List<string>();
            rig.Host.JsonReceived += (peer, id, body) => { if (id == MessageId.Bye) hubLog.Add("bye " + body.GetString("reason")); };
            rig.Host.PeerLeft += (peer, role, why) => hubLog.Add("leave " + role);

            rig.Phone.Stop("link.enabled off");
            rig.Run(50);
            Assert.That(hubLog, Is.EqualTo(new[] { "bye link.enabled off", "leave Phone" }), "the reason, then the leave");
            Assert.That(rig.Host.HasPhone, Is.False, "the seat is free at once");

            string heard = null;
            spectator.ByeReceived += why => heard = why;
            rig.Host.Stop("hub stopping");
            rig.Clock.AdvanceMs(5);
            spectator.Poll();
            Assert.That(heard, Is.EqualTo("hub stopping"));
            Assert.That(spectator.LastByeReason, Is.EqualTo("hub stopping"));
        }

        [Test]
        public void A_second_phone_is_refused_while_the_first_holds_the_seat()
        {
            var rig = new LinkRig();
            rig.Connect();
            int firstPhone = rig.Host.PhonePeer;

            LinkClient second = rig.AddClient(LinkRole.Phone);
            string byeReason = null;
            second.ByeReceived += reason => byeReason = reason;
            second.Start("10.0.0.2", LinkRig.Port);
            rig.Run(600);
            Assert.That(rig.Host.PhonePeer, Is.EqualTo(firstPhone), "the phone in the seat keeps it");
            Assert.That(rig.Host.CountRole(LinkRole.Phone), Is.EqualTo(1));
            Assert.That(byeReason, Is.EqualTo("another phone is already connected"));
            Assert.That(second.LastByeReason, Is.EqualTo("another phone is already connected"));
            Assert.That(second.State, Is.Not.EqualTo(LinkState.Connected));
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));

            // The first phone leaves; the second gets the seat at its next attempt.
            rig.Phone.Stop();
            rig.Run(6000);
            Assert.That(second.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.Host.HasPhone, Is.True);
        }

        [Test]
        public void Stop_says_bye_and_everything_is_inert_without_a_hub()
        {
            var rig = new LinkRig();
            string leftReason = null;
            LinkRole leftRole = LinkRole.Unknown;
            var byes = new List<string>();
            rig.Host.PeerLeft += (peer, role, reason) => { leftRole = role; leftReason = reason; };
            rig.Host.JsonReceived += (peer, id, body) => { if (id == MessageId.Bye) byes.Add(body.GetString("reason")); };
            rig.Connect();

            rig.Phone.Stop("link.enabled off");
            rig.Run(50);
            Assert.That(byes, Is.EqualTo(new[] { "link.enabled off" }));
            Assert.That(leftRole, Is.EqualTo(LinkRole.Phone));
            Assert.That(leftReason, Is.Not.Null);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Offline));
            Assert.That(rig.Host.HasPhone, Is.False);

            // Stopped: every call is a cheap no-op and nothing throws (section 7.5, rule 4).
            StateMessage state = StateMessage.Identity;
            var telemetry = new TelemetryMessage();
            long before = GC.GetAllocatedBytesForCurrentThread();
            int accepted = 0;
            for (int i = 0; i < 100; i++)
            {
                rig.Phone.Poll();
                if (rig.Phone.SendState(ref state)) accepted++;
                if (rig.Phone.SendTelemetry(ref telemetry)) accepted++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(accepted, Is.EqualTo(0));
            Assert.That(allocated, Is.EqualTo(0L));
            Assert.That(rig.Phone.SendEvent("S0", "x", null), Is.False);
            Assert.That(rig.Phone.SendLog("warn", "x"), Is.False);
            Assert.That(rig.Phone.RttMs, Is.EqualTo(0));

            // And it can be started again.
            Assert.That(rig.Phone.Start("10.0.0.2", LinkRig.Port), Is.True);
            rig.Run(1500);
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
        }

        [Test]
        public void A_client_with_no_hub_keeps_trying_and_costs_nothing()
        {
            var clock = new ManualClock();
            var network = new LoopbackNetwork(clock);
            var registry = new ParamRegistry();
            var phone = new LinkClient(network.CreateEndpoint(), clock, LinkRole.Phone, registry);
            Assert.That(phone.Start("10.0.0.2"), Is.True);
            StateMessage state = StateMessage.Identity;
            for (int i = 0; i < 2000; i++)
            {
                clock.AdvanceMs(16.667);
                phone.Poll();
                Assert.That(phone.SendState(ref state), Is.False);
            }
            Assert.That(phone.State, Is.EqualTo(LinkState.Offline));
            Assert.That(phone.Watchdog.BackoffUs, Is.EqualTo(LinkWatchdog.MaxBackoffUs));

            // The Hub appears: the next attempt finds it.
            var host = new LinkHost(network.CreateEndpoint(), clock);
            host.Start(LinkProtocol.DefaultPort);
            for (int i = 0; i < 400; i++)
            {
                clock.AdvanceMs(16.667);
                host.Poll();
                phone.Poll();
                host.Poll();
            }
            Assert.That(phone.State, Is.EqualTo(LinkState.Connected));
        }

        [Test]
        public void Loopback_delivers_reliable_in_order_and_sequenced_without_reordering()
        {
            var clock = new ManualClock();
            var network = new LoopbackNetwork(clock, 99) { Loss = 0.3, DelayMs = 10, JitterMs = 40 };
            ILinkTransport a = network.CreateEndpoint(), b = network.CreateEndpoint();
            var atA = new RecordingListener();
            var atB = new RecordingListener();
            a.Listener = atA;
            b.Listener = atB;
            Assert.That(b.StartHost(0), Is.True);
            Assert.That(a.StartClient(), Is.True);
            a.Connect("x", b.LocalPort);
            for (int i = 0; i < 100; i++) { clock.AdvanceMs(1); a.Poll(); b.Poll(); }
            Assert.That(atA.Connected, Is.EqualTo(1));
            Assert.That(atB.Connected, Is.EqualTo(1));

            for (int i = 0; i < 200; i++)
            {
                a.Send(atA.Peer, new[] { (byte)1, (byte)i }, 0, 2, LinkDelivery.Reliable);
                a.Send(atA.Peer, new[] { (byte)2, (byte)i }, 0, 2, LinkDelivery.Sequenced);
                a.Send(atA.Peer, new[] { (byte)3, (byte)i }, 0, 2, LinkDelivery.Unreliable);
                clock.AdvanceMs(5);
                a.Poll();
                b.Poll();
            }
            for (int i = 0; i < 100; i++) { clock.AdvanceMs(1); b.Poll(); }

            var reliable = new List<byte>();
            var sequenced = new List<byte>();
            int unreliable = 0;
            foreach (byte[] packet in atB.Packets)
            {
                if (packet[0] == 1) reliable.Add(packet[1]);
                else if (packet[0] == 2) sequenced.Add(packet[1]);
                else unreliable++;
            }
            Assert.That(reliable.Count, Is.EqualTo(200), "reliable is never lost");
            for (int i = 0; i < 200; i++) Assert.That(reliable[i], Is.EqualTo((byte)i));
            Assert.That(sequenced.Count, Is.InRange(40, 170), "lossy, and late ones are dropped");
            for (int i = 1; i < sequenced.Count; i++) Assert.That(sequenced[i], Is.GreaterThan(sequenced[i - 1]));
            Assert.That(unreliable, Is.InRange(100, 180));
            Assert.That(a.Send(12345, new byte[1], 0, 1, LinkDelivery.Reliable), Is.False, "unknown peer");

            b.Stop();
            for (int i = 0; i < 100; i++) { clock.AdvanceMs(1); a.Poll(); }
            Assert.That(atA.Disconnected, Is.EqualTo(1));
        }

        sealed class RecordingListener : ILinkTransportListener
        {
            public int Connected, Disconnected, Peer = -1;
            public readonly List<byte[]> Packets = new List<byte[]>();

            public void OnPeerConnected(int peerId) { Connected++; Peer = peerId; }
            public void OnPeerDisconnected(int peerId, string reason) { Disconnected++; }

            public void OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
            {
                var copy = new byte[length];
                Buffer.BlockCopy(data, offset, copy, 0, length);
                Packets.Add(copy);
            }
        }
    }
}
