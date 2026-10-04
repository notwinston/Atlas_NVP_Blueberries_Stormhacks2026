using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// What a Spectator needs from the link (Master Plan section 7.3.1, lane D contract 5.8), over the in-memory
    /// transport: the session and world in <c>Welcome</c>, the phone's parameter manifest, every parameter
    /// change and <c>world</c> event, the room outline and the Forge's job messages; the catch-up of a Spectator
    /// that joins late; the session id in <c>Hello</c>; and the Hub's own clock pings, from which it measures
    /// latency.
    /// </summary>
    public sealed class Link_Spectator
    {
        /// <summary>A client's clock that runs ahead of the rig's by a fixed amount, as a phone's does of the Hub's.</summary>
        sealed class ShiftedClock : ILinkClock
        {
            readonly ILinkClock _inner;
            readonly long _shiftUs;

            public ShiftedClock(ILinkClock inner, long shiftUs)
            {
                _inner = inner;
                _shiftUs = shiftUs;
            }

            public long NowUs { get { return _inner.NowUs + _shiftUs; } }
        }

        /// <summary>A Spectator with its own registry, declared as the phone's is.</summary>
        sealed class Mirror
        {
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly ParamEntry Rho, Copies, Predict;
            public readonly LinkClient Client;
            public readonly List<WelcomeMessage> Welcomes = new List<WelcomeMessage>();
            public readonly List<ParamManifestMessage> Manifests = new List<ParamManifestMessage>();
            public readonly List<EventMessage> Events = new List<EventMessage>();
            public readonly List<string> Jobs = new List<string>();
            public int Outlines, OutlineBoxes;

            public Mirror(LinkRig rig, LinkRole role = LinkRole.Spectator)
            {
                Rho = Registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
                Copies = Registry.DeclareInt("glued.copies", "glued", 3, 5, 5);
                Predict = Registry.DeclareBool("pose.gyroPredict", "pose", true);
                Client = new LinkClient(rig.Network.CreateEndpoint(), rig.Clock, role, Registry) { Device = "test-" + LinkRoles.Name(role) };
                Client.Welcomed += w => Welcomes.Add(w);
                Client.ManifestReceived += m =>
                {
                    Manifests.Add(m);
                    m.ApplyTo(Registry);
                };
                Client.EventReceived += e =>
                {
                    Events.Add(e);
                    ushort id;
                    string name;
                    float value;
                    if (LinkEvents.TryReadParam(e, out id, out name, out value)) Registry.Set(name, value, ParamSource.Remote);
                };
                Client.RoomOutlineReceived += o => { Outlines++; OutlineBoxes = o.BoxCount; };
                Client.JobStatusReceived += s => Jobs.Add("status:" + s.JobId + ":" + s.State);
                Client.AssetReadyReceived += a => Jobs.Add("ready:" + a.JobId + ":" + a.Sha256);
                rig.Others.Add(Client);
            }

            public List<EventMessage> EventsNamed(string ev)
            {
                return Events.FindAll(e => e.Ev == ev);
            }
        }

        static RoomOutlineMessage Room()
        {
            var outline = new RoomOutlineMessage { Height = 2.4f, RectW = 3f, RectD = 4f };
            outline.AddPoint(-2, -2.5f); outline.AddPoint(2, -2.5f); outline.AddPoint(2, 2.5f); outline.AddPoint(-2, 2.5f);
            outline.AddBox(new RoomOutlineBox { Id = 1, Category = 2, W = 1, D = 1, H = 1 });
            outline.AddBox(new RoomOutlineBox { Id = 2, Category = 5, W = 2, D = 1, H = 0.5f });
            return outline;
        }

        static JsonValue World(int worldVer, string worldHash, string roomId)
        {
            return JsonValue.Object().Set("worldVer", worldVer).Set("worldHash", worldHash).Set("space", 1).Set("roomId", roomId);
        }

        // ---- the live relay ---------------------------------------------------------------------------

        [Test]
        public void A_spectator_receives_manifest_parameter_changes_and_world_events()
        {
            var rig = new LinkRig();
            rig.Host.WorldHash = "141060c2fc138145";
            rig.Host.RoomId = "sample_3x3";
            var spectator = new Mirror(rig);
            LinkClient console = rig.AddClient(LinkRole.Console);
            int consoleEvents = 0;
            console.EventReceived += e => consoleEvents++;
            rig.Connect();

            // Welcome: session id, forgeUrl, the source's room and world.
            Assert.That(spectator.Client.IsReady, Is.True);
            Assert.That(spectator.Welcomes.Count, Is.EqualTo(1));
            Assert.That(spectator.Client.Welcome.SessionId, Is.EqualTo("s-test"));
            Assert.That(spectator.Client.Welcome.ForgeUrl, Is.EqualTo("http://10.0.0.2:8000"));
            Assert.That(spectator.Client.Welcome.RoomId, Is.EqualTo("sample_3x3"));
            Assert.That(spectator.Client.Welcome.WorldHash, Is.EqualTo("141060c2fc138145"));

            // The manifest the phone sent at the end of its handshake.
            Assert.That(spectator.Manifests.Count, Is.EqualTo(1));
            Assert.That(spectator.Manifests[0].Params.Count, Is.EqualTo(3));
            Assert.That(spectator.Manifests[0].Params[0].Name, Is.EqualTo("curved.cullRho"));
            Assert.That(spectator.Manifests[0].Params[0].Val, Is.EqualTo(3f));

            // A value changed on the phone (the on-device menu) reaches the Spectator's registry.
            rig.Rho.Set(2.25f);
            rig.Run(50);
            List<EventMessage> changes = spectator.EventsNamed(LinkEvents.Param);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Data.GetString("name"), Is.EqualTo("curved.cullRho"));
            Assert.That(changes[0].Data.GetString("source"), Is.EqualTo("local"));
            Assert.That(spectator.Rho.Float, Is.EqualTo(2.25f));

            // A value set from the Station is clamped on the phone; the Spectator gets what the phone applied.
            Assert.That(rig.Host.SendParam("curved.cullRho", 50f), Is.True);
            rig.Host.SendParam("pose.gyroPredict", 0f);
            rig.Run(50);
            Assert.That(rig.Rho.Float, Is.EqualTo(3.5f));
            Assert.That(spectator.Rho.Float, Is.EqualTo(3.5f));
            Assert.That(spectator.Predict.Bool, Is.False);
            Assert.That(spectator.EventsNamed(LinkEvents.Param).Count, Is.EqualTo(3));

            // The world event, with the hash the Spectator compares against its own.
            Assert.That(rig.Phone.SendEvent("S1", LinkEvents.World, World(7, "141060c2fc138145", "sample_3x3")), Is.True);
            Assert.That(rig.Phone.SendEvent("S1", "object.mesh", JsonValue.Object().Set("id", 4).Set("mesh", 2)), Is.True);
            rig.Run(50);
            List<EventMessage> worlds = spectator.EventsNamed(LinkEvents.World);
            Assert.That(worlds.Count, Is.EqualTo(1));
            Assert.That(worlds[0].Scene, Is.EqualTo("S1"));
            Assert.That(worlds[0].TimeUs, Is.GreaterThan(0L), "the phone's own time stamp");
            Assert.That(worlds[0].Data.GetInt("worldVer"), Is.EqualTo(7));
            Assert.That(worlds[0].Data.GetString("worldHash"), Is.EqualTo("141060c2fc138145"));
            Assert.That(spectator.EventsNamed("object.mesh").Count, Is.EqualTo(1), "every event of the phone is relayed");

            // Room outline, job status, asset.
            Assert.That(rig.Phone.SendRoomOutline(Room()), Is.True);
            rig.Host.SendJobStatus(new JobStatusMessage { JobId = "j-1", BoxId = 1, State = "generating", Source = "local" });
            rig.Host.SendAssetReady(new AssetReadyMessage { JobId = "j-1", RoomId = "sample_3x3", BoxId = 1, Sha256 = "ab12", Url = "/assets/ab12.glb" });
            rig.Run(50);
            Assert.That(spectator.Outlines, Is.EqualTo(1));
            Assert.That(spectator.OutlineBoxes, Is.EqualTo(2));
            Assert.That(spectator.Jobs, Is.EqualTo(new[] { "status:j-1:generating", "ready:j-1:ab12" }));

            // A link Console operates; it does not mirror.
            Assert.That(consoleEvents, Is.EqualTo(0));
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
            Assert.That(spectator.Client.State, Is.EqualTo(LinkState.Connected));
        }

        [Test]
        public void An_editor_mirrors_like_a_spectator_and_gets_the_manifest_once()
        {
            var rig = new LinkRig();
            var editor = new Mirror(rig, LinkRole.Editor);
            rig.Connect();
            Assert.That(editor.Manifests.Count, Is.EqualTo(1), "an Editor watches and operates, and is still sent one manifest");

            rig.Copies.Set(4);
            rig.Phone.SendEvent("S1", LinkEvents.World, World(1, "aaaaaaaaaaaaaaaa", "room_1"));
            rig.Run(50);
            Assert.That(editor.Copies.Int, Is.EqualTo(4));
            Assert.That(editor.EventsNamed(LinkEvents.World).Count, Is.EqualTo(1));
        }

        // ---- joining late -----------------------------------------------------------------------------

        [Test]
        public void A_late_spectator_is_brought_up_to_date()
        {
            var rig = new LinkRig();
            rig.Host.RoomId = "sample_3x3";
            rig.Connect();

            // A session in progress: values changed since the manifest, two worlds, a room, two assets.
            rig.Rho.Set(2.5f);
            rig.Host.SendParam("glued.copies", 3f);
            rig.Phone.SendRoomOutline(Room());
            rig.Phone.SendEvent("S1", LinkEvents.World, World(1, "aaaaaaaaaaaaaaaa", "sample_3x3"));
            rig.Phone.SendEvent("S1", "note", JsonValue.Object().Set("text", "not kept"));
            rig.Phone.SendEvent("S2", LinkEvents.World, World(2, "bbbbbbbbbbbbbbbb", "sample_3x3"));
            rig.Host.SendJobStatus(new JobStatusMessage { JobId = "j-0", BoxId = 9, State = "generating" });
            rig.Host.SendAssetReady(new AssetReadyMessage { JobId = "j-1", BoxId = 1, Sha256 = "aaaa", Url = "/assets/aaaa.glb" });
            rig.Host.SendAssetReady(new AssetReadyMessage { JobId = "j-2", BoxId = 2, Sha256 = "bbbb", Url = "/assets/bbbb.glb" });
            rig.Run(50);
            // The phone placed the first; the Hub keeps the second until it is acknowledged.
            rig.Phone.Send(new AssetAckMessage { JobId = "j-1", BoxId = 1, Sha256 = "aaaa", Ok = true });
            rig.Run(50);
            Assert.That(rig.Host.UnackedAssetCount, Is.EqualTo(1));

            var late = new Mirror(rig);
            late.Client.Start("10.0.0.2", LinkRig.Port);
            rig.Run(1500);

            Assert.That(late.Client.IsReady, Is.True);
            Assert.That(late.Client.Welcome.SessionId, Is.EqualTo("s-test"));
            Assert.That(late.Client.Welcome.RoomId, Is.EqualTo("sample_3x3"));
            Assert.That(late.Outlines, Is.EqualTo(1), "the kept room outline");
            Assert.That(late.OutlineBoxes, Is.EqualTo(2));

            // The manifest carries the values of this moment, not those of the phone's handshake.
            Assert.That(late.Manifests.Count, Is.EqualTo(1));
            Assert.That(late.Manifests[0].Params.Count, Is.EqualTo(3));
            Assert.That(late.Manifests[0].Params[0].Val, Is.EqualTo(2.5f));
            Assert.That(late.Manifests[0].Params[1].Val, Is.EqualTo(3f));
            Assert.That(late.Rho.Float, Is.EqualTo(2.5f));
            Assert.That(late.Copies.Int, Is.EqualTo(3));
            Assert.That(rig.Host.Manifest.Params[0].Val, Is.EqualTo(3f), "the Hub's record of what the phone sent is left as it was");

            // The newest world event only, and the asset still waiting for its acknowledgement.
            Assert.That(late.Events.Count, Is.EqualTo(1));
            Assert.That(late.Events[0].Ev, Is.EqualTo(LinkEvents.World));
            Assert.That(late.Events[0].Scene, Is.EqualTo("S2"));
            Assert.That(late.Events[0].Data.GetString("worldHash"), Is.EqualTo("bbbbbbbbbbbbbbbb"));
            Assert.That(late.Jobs, Is.EqualTo(new[] { "ready:j-2:bbbb" }));

            // From here on it is fed like any other Spectator.
            rig.Rho.Set(2f);
            rig.Run(50);
            Assert.That(late.Rho.Float, Is.EqualTo(2f));
        }

        [Test]
        public void A_manifest_brings_a_registry_in_step_and_skips_what_it_does_not_know()
        {
            var phone = new ParamRegistry();
            phone.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f).Set(2.75f);
            phone.DeclareInt("glued.copies", "glued", 3, 5, 5).Set(4);
            phone.DeclareBool("phone.only", "pose", true);
            phone.SetGroupActive("glued", false);

            var mine = new ParamRegistry();
            ParamEntry copies = mine.DeclareInt("glued.copies", "glued", 3, 5, 5);    // declared in another order
            ParamEntry rho = mine.DeclareFloat("curved.cullRho", "curved", 2.5f, 3f, 3f); // and with a narrower range
            ParamEntry own = mine.DeclareFloat("spectator.fov", "view", 30f, 90f, 60f);
            var sources = new List<ParamSource>();
            mine.Changed += (entry, source) => sources.Add(source);

            // Through the wire, as a Spectator gets it.
            byte[] packet = ParamManifestMessage.FromRegistry(phone).ToBytes();
            var manifest = new ParamManifestMessage();
            Assert.That(manifest.ReadFrom(packet, 0, packet.Length), Is.True);

            Assert.That(manifest.ApplyTo(mine), Is.EqualTo(2));
            Assert.That(rho.Float, Is.EqualTo(2.75f), "matched by name, not by wire id");
            Assert.That(copies.Int, Is.EqualTo(4));
            Assert.That(own.Float, Is.EqualTo(60f));
            Assert.That(copies.Active, Is.False, "the group is folded as it is on the phone");
            Assert.That(rho.Active, Is.True);
            Assert.That(sources, Is.EqualTo(new[] { ParamSource.Remote, ParamSource.Remote }));
            Assert.That(manifest.ApplyTo(mine), Is.EqualTo(0), "nothing left to change");

            // Whatever the manifest says, a value is clamped to the range declared here.
            manifest.Params[0] = new ParamManifestEntry { Name = "curved.cullRho", Val = 99f, Active = true };
            Assert.That(manifest.ApplyTo(mine, ParamSource.Restore), Is.EqualTo(1));
            Assert.That(rho.Float, Is.EqualTo(3f));
            Assert.That(() => manifest.ApplyTo(null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void A_param_event_reads_back_as_it_was_written()
        {
            var registry = new ParamRegistry();
            ParamEntry rho = registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            ParamEntry predict = registry.DeclareBool("pose.gyroPredict", "pose", true);
            rho.Set(2.75f);

            // Through the wire, as the Hub relays it.
            byte[] packet = LinkEvents.ParamChanged(123, "S2", rho, ParamSource.Remote).ToBytes();
            var received = new EventMessage();
            Assert.That(received.ReadFrom(packet, 0, packet.Length), Is.True);
            ushort id;
            string name;
            float value;
            Assert.That(LinkEvents.TryReadParam(received, out id, out name, out value), Is.True);
            Assert.That(id, Is.EqualTo(rho.Id));
            Assert.That(name, Is.EqualTo("curved.cullRho"));
            Assert.That(value, Is.EqualTo(2.75f));

            Assert.That(LinkEvents.TryReadParam(LinkEvents.ParamChanged(1, "S0", predict, ParamSource.Local), out id, out name, out value), Is.True);
            Assert.That(id, Is.EqualTo(predict.Id));
            Assert.That(value, Is.EqualTo(1f));

            Assert.That(LinkEvents.TryReadParam(new EventMessage(1, "S0", LinkEvents.World, JsonValue.Object()), out id, out name, out value), Is.False);
            Assert.That(LinkEvents.TryReadParam(new EventMessage(1, "S0", LinkEvents.Param, JsonValue.Object().Set("name", "x")), out id, out name, out value), Is.False);
            Assert.That(LinkEvents.TryReadParam(new EventMessage(1, "S0", LinkEvents.Param, null), out id, out name, out value), Is.False);
            Assert.That(LinkEvents.TryReadParam(null, out id, out name, out value), Is.False);
        }

        // ---- session and Welcome ----------------------------------------------------------------------

        // What the Hub process does with a Hello: the phone names the session and the world.
        static void AdoptThePhone(LinkHost host, List<HelloMessage> hellos)
        {
            host.HelloReceived += (peer, hello) =>
            {
                hellos.Add(hello);
                if (hello.Role != LinkRole.Phone) return;
                if (!string.IsNullOrEmpty(hello.SessionId)) host.SessionId = hello.SessionId;
                host.WorldHash = hello.WorldHash;
                host.RoomId = hello.RoomId;
            };
        }

        [Test]
        public void Hello_carries_the_phones_session_and_welcome_gives_it_back()
        {
            var rig = new LinkRig();
            var hellos = new List<HelloMessage>();
            AdoptThePhone(rig.Host, hellos);
            rig.Phone.SessionId = "20261004T153012Z-a3f9";
            rig.Phone.WorldHash = "141060c2fc138145";
            rig.Phone.RoomId = "sample_3x3";
            rig.Connect();

            Assert.That(hellos.Count, Is.EqualTo(1));
            Assert.That(hellos[0].SessionId, Is.EqualTo("20261004T153012Z-a3f9"));
            Assert.That(rig.Host.SessionId, Is.EqualTo("20261004T153012Z-a3f9"));
            Assert.That(rig.Phone.Welcome.SessionId, Is.EqualTo("20261004T153012Z-a3f9"), "to a phone: its own id back");
            Assert.That(rig.Phone.Welcome.WorldHash, Is.EqualTo("141060c2fc138145"));
            Assert.That(rig.Phone.Welcome.RoomId, Is.EqualTo("sample_3x3"));
            Assert.That(rig.Phone.WorldMatches, Is.True);

            // A phone that names no session is given the Hub's own.
            var plain = new LinkRig();
            AdoptThePhone(plain.Host, new List<HelloMessage>());
            plain.Connect();
            Assert.That(plain.Phone.SessionId, Is.EqualTo(string.Empty));
            Assert.That(plain.Phone.Welcome.SessionId, Is.EqualTo("s-test"));
            Assert.That(plain.Host.SessionId, Is.EqualTo("s-test"));
        }

        [Test]
        public void A_spectator_that_joined_first_is_welcomed_again_when_the_phone_arrives()
        {
            var rig = new LinkRig();
            var hellos = new List<HelloMessage>();
            AdoptThePhone(rig.Host, hellos);
            var spectator = new Mirror(rig);

            // Only the Hub and the Spectator are up: there is no source yet.
            rig.Host.Start(LinkRig.Port);
            spectator.Client.Start("10.0.0.2", LinkRig.Port);
            rig.Run(1500);
            Assert.That(spectator.Welcomes.Count, Is.EqualTo(1));
            Assert.That(spectator.Client.Welcome.SessionId, Is.EqualTo("s-test"));
            Assert.That(spectator.Client.Welcome.RoomId, Is.EqualTo(string.Empty));
            Assert.That(rig.Host.SendWelcomeToWatchers(), Is.EqualTo(0), "nothing changed, nothing sent");

            rig.Phone.SessionId = "20261004T153012Z-a3f9";
            rig.Phone.WorldHash = "141060c2fc138145";
            rig.Phone.RoomId = "sample_3x3";
            rig.Phone.Start("10.0.0.2", LinkRig.Port);
            rig.Run(1500);

            Assert.That(spectator.Welcomes.Count, Is.EqualTo(2));
            Assert.That(spectator.Client.Welcome.SessionId, Is.EqualTo("20261004T153012Z-a3f9"), "to a spectator: the source's session");
            Assert.That(spectator.Client.Welcome.WorldHash, Is.EqualTo("141060c2fc138145"));
            Assert.That(spectator.Client.Welcome.RoomId, Is.EqualTo("sample_3x3"));
            Assert.That(spectator.Client.Welcome.ForgeUrl, Is.EqualTo("http://10.0.0.2:8000"));
            Assert.That(spectator.Client.State, Is.EqualTo(LinkState.Connected), "a second Welcome is news, not a new handshake");
            Assert.That(spectator.Manifests.Count, Is.EqualTo(1));

            // An owner that changes what Welcome carries at another time says so itself.
            rig.Host.RoomId = "sample_4x5";
            Assert.That(rig.Host.SendWelcomeToWatchers(), Is.EqualTo(1));
            Assert.That(rig.Host.SendWelcomeToWatchers(), Is.EqualTo(0));
            rig.Run(50);
            Assert.That(spectator.Welcomes.Count, Is.EqualTo(3));
            Assert.That(spectator.Client.Welcome.RoomId, Is.EqualTo("sample_4x5"));
        }

        [Test]
        public void SetSession_and_SetWorld_say_hello_again_without_a_new_handshake()
        {
            var rig = new LinkRig();
            var hellos = new List<HelloMessage>();
            AdoptThePhone(rig.Host, hellos);
            var spectator = new Mirror(rig);
            int joined = 0, manifests = 0, applied = 0, phoneWelcomes = 0;
            rig.Host.PeerJoined += (peer, hello) => joined++;
            rig.Host.ManifestReceived += (peer, m) => manifests++;
            rig.Phone.ParamsApplied += n => applied++;
            rig.Phone.Welcomed += w => phoneWelcomes++;
            var rows = new List<uint>();
            rig.Host.TelemetryReceived += (int peer, in TelemetryMessage t) => rows.Add(t.Seq);

            // Before there is a Hub the id simply waits for the first Hello.
            rig.Phone.SetSession("20261004T153012Z-a3f9");
            rig.Connect();
            Assert.That(hellos.FindAll(h => h.Role == LinkRole.Phone).Count, Is.EqualTo(1));
            Assert.That(rig.Phone.Welcome.SessionId, Is.EqualTo("20261004T153012Z-a3f9"));
            Assert.That(joined, Is.EqualTo(2));
            Assert.That(manifests, Is.EqualTo(1));

            var row = new TelemetryMessage { Fps = 60f };
            rig.Phone.SendTelemetry(ref row);
            rig.Phone.SendTelemetry(ref row);
            rig.Host.SendParam("curved.cullRho", 2.5f);
            rig.Run(50);
            Assert.That(applied, Is.EqualTo(1));

            // A new walker: a new session, said to the Hub at once.
            rig.Phone.SetSession("20261004T154500Z-07c2");
            rig.Run(50);
            Assert.That(hellos.FindAll(h => h.Role == LinkRole.Phone).Count, Is.EqualTo(2));
            Assert.That(rig.Host.SessionId, Is.EqualTo("20261004T154500Z-07c2"));
            Assert.That(rig.Phone.Welcome.SessionId, Is.EqualTo("20261004T154500Z-07c2"));
            Assert.That(phoneWelcomes, Is.EqualTo(2));
            Assert.That(spectator.Client.Welcome.SessionId, Is.EqualTo("20261004T154500Z-07c2"));
            Assert.That(joined, Is.EqualTo(2), "the same client, not a new one");
            Assert.That(manifests, Is.EqualTo(1));
            Assert.That(applied, Is.EqualTo(1), "no RESYNC: the snapshot is not replayed");
            Assert.That(rig.Phone.State, Is.EqualTo(LinkState.Connected));
            Assert.That(rig.Phone.IsReady, Is.True);

            // The stamped row counter starts again with the session.
            rig.Phone.SendTelemetry(ref row);
            rig.Run(50);
            Assert.That(rows, Is.EqualTo(new uint[] { 0, 1, 0 }));

            // The same id again is not news.
            rig.Phone.SetSession("20261004T154500Z-07c2");
            rig.Phone.SetSession(null);
            rig.Phone.SetSession("20261004T154500Z-07c2");
            rig.Run(50);
            Assert.That(hellos.FindAll(h => h.Role == LinkRole.Phone).Count, Is.EqualTo(4), "empty and back are two changes");

            // The walker enters a scanned room: the Spectator's next Welcome names it.
            rig.Phone.SetWorld("141060c2fc138145", "sample_3x3");
            rig.Phone.SetWorld("141060c2fc138145", "sample_3x3");
            rig.Run(50);
            Assert.That(hellos.FindAll(h => h.Role == LinkRole.Phone).Count, Is.EqualTo(5));
            Assert.That(rig.Phone.RoomId, Is.EqualTo("sample_3x3"));
            Assert.That(spectator.Client.Welcome.RoomId, Is.EqualTo("sample_3x3"));
            Assert.That(spectator.Client.Welcome.WorldHash, Is.EqualTo("141060c2fc138145"));
            Assert.That(rig.Phone.WorldMatches, Is.True);
            Assert.That(manifests, Is.EqualTo(1));
        }

        [Test]
        public void SendTelemetryRow_sends_the_row_as_the_caller_numbered_it()
        {
            var rig = new LinkRig();
            var seen = new List<TelemetryMessage>();
            rig.Host.TelemetryReceived += (int peer, in TelemetryMessage t) => seen.Add(t);

            var row = new TelemetryMessage { TimeUs = 1234567890, Seq = 42, Fps = 60f, Battery = 87 };
            Assert.That(rig.Phone.SendTelemetryRow(in row), Is.False, "no Hub, no send");
            rig.Connect();

            Assert.That(rig.Phone.SendTelemetryRow(in row), Is.True);
            row.Seq = 41;
            row.TimeUs = 0;
            Assert.That(rig.Phone.SendTelemetryRow(in row), Is.True);
            rig.Run(50);

            Assert.That(seen.Count, Is.EqualTo(2));
            Assert.That(seen[0].Seq, Is.EqualTo(42u));
            Assert.That(seen[0].TimeUs, Is.EqualTo(1234567890L));
            Assert.That(seen[0].Battery, Is.EqualTo(87));
            Assert.That(seen[1].Seq, Is.EqualTo(41u), "not renumbered");
            Assert.That(seen[1].TimeUs, Is.EqualTo(0L), "not re-stamped");

            // The stamping send is untouched by it.
            var stamped = new TelemetryMessage();
            rig.Phone.SendTelemetry(ref stamped);
            Assert.That(stamped.Seq, Is.EqualTo(0u));
        }

        // ---- the Hub's clock pings --------------------------------------------------------------------

        [Test]
        public void The_hub_pings_each_client_four_times_per_second_and_learns_its_clock()
        {
            const long shiftUs = 3999950;
            var rig = new LinkRig();
            var phoneClock = new ShiftedClock(rig.Clock, shiftUs);
            var phone = new LinkClient(rig.Network.CreateEndpoint(), phoneClock, LinkRole.Phone) { Device = "shifted" };
            int phonePeer = -1;
            rig.Host.PeerJoined += (peer, hello) => { if (hello.Device == "shifted") phonePeer = peer; };
            var pongs = new List<long>();
            rig.Host.TPongReceived += (int peer, in TPongMessage pong, long hubReceiveUs) =>
            {
                if (peer != phonePeer) return;
                Assert.That(hubReceiveUs, Is.EqualTo(rig.Clock.NowUs));
                // With no delay on the wire the ping is read within one rig step of being written.
                if (rig.Network.DelayMs == 0.0)
                    Assert.That(pong.T2Us - pong.T1Us, Is.InRange(shiftUs, shiftUs + 5000), "t2 is the client's clock, t1 the Hub's");
                pongs.Add(hubReceiveUs);
            };

            long offset, rtt;
            Assert.That(rig.Host.TryGetClock(0, out offset, out rtt), Is.False, "nobody there yet");
            Assert.That(rig.Host.ToHubUs(0, 777), Is.EqualTo(777L), "no estimate: the time is passed through");
            Assert.That(rig.Host.GetAddress(0), Is.EqualTo(string.Empty));

            rig.Host.Start(LinkRig.Port);
            phone.Start("10.0.0.2", LinkRig.Port);
            rig.Others.Add(phone);
            rig.Run(1500);
            Assert.That(phone.State, Is.EqualTo(LinkState.Connected));
            Assert.That(phonePeer, Is.GreaterThanOrEqualTo(0));

            // An opening burst of eight, then the heartbeat.
            Assert.That(pongs.Count, Is.GreaterThanOrEqualTo(8));
            pongs.Clear();
            rig.Run(2000);
            Assert.That(pongs.Count, Is.InRange(7, 9), "four per second");
            for (int i = 1; i < pongs.Count; i++)
                Assert.That(pongs[i] - pongs[i - 1], Is.EqualTo(LinkHost.HeartbeatMs * 1000L));

            Assert.That(rig.Host.TryGetClock(phonePeer, out offset, out rtt), Is.True);
            Assert.That(offset, Is.InRange(shiftUs - 2500, shiftUs + 2500), "client clock minus Hub clock");
            Assert.That(rtt, Is.InRange(0L, 10000L));
            Assert.That(rig.Host.ToHubUs(phonePeer, phoneClock.NowUs), Is.InRange(rig.Clock.NowUs - 2500, rig.Clock.NowUs + 2500));
            Assert.That(rig.Host.GetAddress(phonePeer), Is.EqualTo(LoopbackTransport.PeerAddress));
            Assert.That(rig.Host.GetAddress(phonePeer + 100), Is.EqualTo(string.Empty));

            // The client keeps its own estimate of the Hub's clock, the other way round.
            Assert.That(phone.ClockSync.OffsetUs, Is.InRange(-shiftUs - 2500, -shiftUs + 2500));

            // Latency of a State: Hub receive time minus the send time on the Hub's clock.
            rig.Network.DelayMs = 20.0;
            rig.Run(500);
            var latencies = new List<long>();
            rig.Host.StateReceived += (int peer, in StateMessage s, byte[] raw, int o, int length) =>
                latencies.Add(rig.Host.NowUs - rig.Host.ToHubUs(peer, s.TimeUs));
            StateMessage state = StateMessage.Identity;
            for (int frame = 0; frame < 60; frame++)
            {
                phone.SendState(ref state);
                rig.Step(1000.0 / 60.0);
            }
            rig.Run(100);
            Assert.That(latencies.Count, Is.EqualTo(60));
            foreach (long latency in latencies)
                Assert.That(latency, Is.InRange(17500L, 42500L), "20 ms on the wire, plus up to one poll, give or take the clock estimate");
        }

        [Test]
        public void A_client_that_has_not_said_hello_is_not_pinged()
        {
            var clock = new ManualClock(5000);
            var network = new LoopbackNetwork(clock);
            var host = new LinkHost(network.CreateEndpoint(), clock);
            host.Start(LinkRig.Port);

            var wire = network.CreateEndpoint();
            var listener = new CountingListener();
            wire.Listener = listener;
            wire.StartClient();
            wire.Connect("10.0.0.2", LinkRig.Port);
            for (int i = 0; i < 400; i++) { clock.AdvanceMs(5); host.Poll(); wire.Poll(); }
            Assert.That(listener.Connected, Is.True);
            Assert.That(listener.Packets, Is.EqualTo(0), "the Hub says nothing to a stranger");

            byte[] hello = new HelloMessage { Role = LinkRole.Spectator }.ToBytes();
            wire.Send(listener.Peer, hello, 0, hello.Length, LinkDelivery.Reliable);
            for (int i = 0; i < 400; i++) { clock.AdvanceMs(5); host.Poll(); wire.Poll(); }
            Assert.That(listener.Welcomes, Is.EqualTo(1));
            // Eight in the first 350 ms, then one every 250 ms of the remaining 1.65 s.
            Assert.That(listener.Pings, Is.InRange(13, 16));
        }

        sealed class CountingListener : ILinkTransportListener
        {
            public bool Connected;
            public int Peer, Packets, Pings, Welcomes;

            public void OnPeerConnected(int peerId) { Connected = true; Peer = peerId; }
            public void OnPeerDisconnected(int peerId, string reason) { Connected = false; }

            public void OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
            {
                Packets++;
                if (data[offset] == MessageId.TPing && delivery == LinkDelivery.Unreliable && length == TPingMessage.Size) Pings++;
                if (data[offset] == MessageId.Welcome) Welcomes++;
            }
        }
    }
}
