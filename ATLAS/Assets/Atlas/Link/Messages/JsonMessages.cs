using System;
using System.Collections.Generic;
using System.Text;

namespace Atlas.Link
{
    /// <summary>Who a link client is. Sent in <c>Hello</c>; the Hub routes by it (Master Plan section 7.1).</summary>
    public enum LinkRole : byte
    {
        Unknown = 0,
        /// <summary>The ATLAS app in the headset: the one source of <c>State</c> and the target of commands.</summary>
        Phone = 1,
        /// <summary>A Spectator build: receives the relayed <c>State</c>, <c>RoomOutline</c> and asset messages.</summary>
        Spectator = 2,
        /// <summary>A Unity Editor: receives like a Spectator and may send parameters and commands.</summary>
        Editor = 3,
        /// <summary>An operator console speaking the link directly: sends parameters and commands.</summary>
        Console = 4
    }

    /// <summary>Text forms of <see cref="LinkRole"/> as they appear in <c>Hello.role</c>.</summary>
    public static class LinkRoles
    {
        public static string Name(LinkRole role)
        {
            switch (role)
            {
                case LinkRole.Phone: return "phone";
                case LinkRole.Spectator: return "spectator";
                case LinkRole.Editor: return "editor";
                case LinkRole.Console: return "console";
                default: return "unknown";
            }
        }

        public static LinkRole Parse(string name)
        {
            switch (name)
            {
                case "phone": return LinkRole.Phone;
                case "spectator": return LinkRole.Spectator;
                case "editor": return LinkRole.Editor;
                case "console": return LinkRole.Console;
                default: return LinkRole.Unknown;
            }
        }

        /// <summary>True for the roles that receive the phone's relayed <c>State</c>.</summary>
        public static bool Watches(LinkRole role) { return role == LinkRole.Spectator || role == LinkRole.Editor; }

        /// <summary>True for the roles whose parameters and commands the Hub forwards to the phone.</summary>
        public static bool Operates(LinkRole role) { return role == LinkRole.Console || role == LinkRole.Editor; }
    }

    /// <summary>
    /// Base of the messages sent as one id byte followed by a UTF-8 JSON object (Master Plan section 7.2). These
    /// are on-demand control messages, so building their JSON may allocate; nothing here runs per frame.
    /// </summary>
    public abstract class JsonMessage
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false, false);

        /// <summary>The message id (byte 0).</summary>
        public abstract byte Id { get; }

        /// <summary>The message body. Member order is fixed, so equal messages give equal bytes.</summary>
        public abstract JsonValue ToJson();

        /// <summary>Fills this message from a body. Missing members take their defaults; unknown members are ignored.</summary>
        public abstract void FromJson(JsonValue json);

        /// <summary>The packet: id byte + compact UTF-8 JSON.</summary>
        public byte[] ToBytes() { return Encode(Id, ToJson()); }

        /// <summary>Writes the packet at <paramref name="offset"/> and returns its length.</summary>
        public int WriteTo(byte[] buffer, int offset)
        {
            string json = ToJson().ToJson(false);
            int need = 1 + s_utf8.GetByteCount(json);
            if (!Wire.InRange(buffer, offset, need)) throw new ArgumentException("Buffer too small for " + MessageId.Name(Id));
            buffer[offset] = Id;
            s_utf8.GetBytes(json, 0, json.Length, buffer, offset + 1);
            return need;
        }

        /// <summary>
        /// Reads a packet. Returns false when the id is not this message's or the body is not a JSON object.
        /// Anything after the JSON object is ignored.
        /// </summary>
        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            byte id;
            JsonValue body;
            if (!TryDecode(buffer, offset, length, out id, out body) || id != Id) return false;
            FromJson(body);
            return true;
        }

        /// <summary>Packs an id byte and a JSON body.</summary>
        public static byte[] Encode(byte id, JsonValue body)
        {
            string json = AtlasJson.ToJson(body, false);
            var bytes = new byte[1 + s_utf8.GetByteCount(json)];
            bytes[0] = id;
            s_utf8.GetBytes(json, 0, json.Length, bytes, 1);
            return bytes;
        }

        /// <summary>Splits a JSON packet into id and body. False when the body is not a JSON object.</summary>
        public static bool TryDecode(byte[] buffer, int offset, int length, out byte id, out JsonValue body)
        {
            id = 0;
            body = JsonValue.Null;
            if (length < 2 || !Wire.InRange(buffer, offset, length)) return false;
            id = buffer[offset];
            string error;
            return AtlasJson.TryParseUtf8(buffer, offset + 1, length - 1, true, out body, out error) && body.IsObject;
        }

        protected static string Text(JsonValue json, string key) { return json.GetString(key, string.Empty); }
        protected static JsonValue TextOrEmpty(string value) { return JsonValue.From(value ?? string.Empty); }
        protected static JsonValue ObjectOrEmpty(JsonValue value) { return value != null && !value.IsNull ? value : JsonValue.Object(); }
    }

    /// <summary><c>Hello</c> (0x01, client to Hub): who is connecting and which world it holds.</summary>
    public sealed class HelloMessage : JsonMessage
    {
        public int ProtoMajor = LinkProtocol.ProtoMajor;
        public int ProtoMinor = LinkProtocol.ProtoMinor;
        public LinkRole Role;
        public string Device = string.Empty;
        public string App = string.Empty;
        /// <summary>Short git hash of the build.</summary>
        public string Build = string.Empty;
        /// <summary>16 hex characters (Master Plan section 7.3.1).</summary>
        public string WorldHash = string.Empty;
        public string RoomId = string.Empty;
        /// <summary>
        /// The session the phone started (for example <c>20261004T153012Z-a3f9</c>). The Hub adopts it, so the
        /// phone's files and the Hub's log folder carry the same name. Empty from a Spectator.
        /// </summary>
        public string SessionId = string.Empty;

        public override byte Id { get { return MessageId.Hello; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object()
                .Set("proto", JsonValue.Array(ProtoMajor, ProtoMinor))
                .Set("role", LinkRoles.Name(Role))
                .Set("device", TextOrEmpty(Device))
                .Set("app", TextOrEmpty(App))
                .Set("build", TextOrEmpty(Build))
                .Set("worldHash", TextOrEmpty(WorldHash))
                .Set("roomId", TextOrEmpty(RoomId))
                .Set("sessionId", TextOrEmpty(SessionId));
        }

        public override void FromJson(JsonValue json)
        {
            ProtoMajor = json["proto"][0].AsInt();
            ProtoMinor = json["proto"][1].AsInt();
            Role = LinkRoles.Parse(json.GetString("role"));
            Device = Text(json, "device");
            App = Text(json, "app");
            Build = Text(json, "build");
            WorldHash = Text(json, "worldHash");
            RoomId = Text(json, "roomId");
            SessionId = Text(json, "sessionId");
        }
    }

    /// <summary><c>Welcome</c> (0x02, Hub to client): the session and the world the Hub expects.</summary>
    public sealed class WelcomeMessage : JsonMessage
    {
        public int ProtoMajor = LinkProtocol.ProtoMajor;
        public int ProtoMinor = LinkProtocol.ProtoMinor;
        public string SessionId = string.Empty;
        public string WorldHash = string.Empty;
        public string RoomId = string.Empty;
        public bool DemoLock;
        /// <summary>Base URL of the Forge, for example <c>http://192.168.1.20:8000</c>.</summary>
        public string ForgeUrl = string.Empty;

        public override byte Id { get { return MessageId.Welcome; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object()
                .Set("proto", JsonValue.Array(ProtoMajor, ProtoMinor))
                .Set("sessionId", TextOrEmpty(SessionId))
                .Set("worldHash", TextOrEmpty(WorldHash))
                .Set("roomId", TextOrEmpty(RoomId))
                .Set("demoLock", DemoLock)
                .Set("forgeUrl", TextOrEmpty(ForgeUrl));
        }

        public override void FromJson(JsonValue json)
        {
            ProtoMajor = json["proto"][0].AsInt();
            ProtoMinor = json["proto"][1].AsInt();
            SessionId = Text(json, "sessionId");
            WorldHash = Text(json, "worldHash");
            RoomId = Text(json, "roomId");
            DemoLock = json.GetBool("demoLock");
            ForgeUrl = Text(json, "forgeUrl");
        }
    }

    /// <summary>One parameter of a <c>ParamManifest</c>.</summary>
    public struct ParamManifestEntry
    {
        public ushort Id;
        public string Name;
        public string Group;
        public float Min, Max, Def, Val;
        public bool OnRelease;
        public ParamKind Kind;
        /// <summary>Value names of an enum parameter; null for the other kinds.</summary>
        public string[] Names;
        /// <summary>False while the parameter's group is inactive (its space is not entered).</summary>
        public bool Active;
        /// <summary>True when the parameter's changes are echoed at once (<see cref="ParamEntry.Live"/>).</summary>
        public bool Live;
    }

    /// <summary>
    /// <c>ParamManifest</c> (0x20, phone to Hub): every declared parameter, from which the Console builds its
    /// sliders. Beside the fields of Appendix B.2 each entry carries <c>kind</c>, <c>active</c> and, for enums,
    /// <c>names</c>.
    /// </summary>
    public sealed class ParamManifestMessage : JsonMessage
    {
        public readonly List<ParamManifestEntry> Params = new List<ParamManifestEntry>();

        public override byte Id { get { return MessageId.ParamManifest; } }

        /// <summary>The manifest of a registry as it stands now.</summary>
        public static ParamManifestMessage FromRegistry(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            var m = new ParamManifestMessage();
            for (int i = 0; i < registry.Count; i++)
            {
                ParamEntry e = registry[i];
                string[] names = null;
                if (e.EnumCount > 0)
                {
                    names = new string[e.EnumCount];
                    for (int n = 0; n < names.Length; n++) names[n] = e.EnumName(n);
                }
                m.Params.Add(new ParamManifestEntry
                {
                    Id = e.Id, Name = e.Name, Group = e.Group, Min = e.Min, Max = e.Max, Def = e.Default,
                    Val = e.Value, OnRelease = e.OnRelease, Kind = e.Kind, Names = names, Active = e.Active, Live = e.Live
                });
            }
            return m;
        }

        public override JsonValue ToJson()
        {
            JsonValue list = JsonValue.Array();
            for (int i = 0; i < Params.Count; i++)
            {
                ParamManifestEntry e = Params[i];
                JsonValue p = JsonValue.Object()
                    .Set("id", (int)e.Id)
                    .Set("name", TextOrEmpty(e.Name))
                    .Set("group", TextOrEmpty(e.Group))
                    .Set("min", e.Min)
                    .Set("max", e.Max)
                    .Set("def", e.Def)
                    .Set("val", e.Val)
                    .Set("onRelease", e.OnRelease)
                    .Set("kind", ParamRegistry.KindName(e.Kind))
                    .Set("active", e.Active);
                // Written only when set, as names is for enums: most parameters are not live.
                if (e.Live) p.Set("live", true);
                if (e.Names != null)
                {
                    JsonValue names = JsonValue.Array();
                    for (int n = 0; n < e.Names.Length; n++) names.Add(e.Names[n]);
                    p.Set("names", names);
                }
                list.Add(p);
            }
            return JsonValue.Object().Set("params", list);
        }

        public override void FromJson(JsonValue json)
        {
            Params.Clear();
            JsonValue list = json["params"];
            for (int i = 0; i < list.Count; i++)
            {
                JsonValue p = list[i];
                var e = new ParamManifestEntry
                {
                    Id = (ushort)p.GetInt("id"),
                    Name = Text(p, "name"),
                    Group = Text(p, "group"),
                    Min = p.GetFloat("min"),
                    Max = p.GetFloat("max"),
                    Def = p.GetFloat("def"),
                    Val = p.GetFloat("val"),
                    OnRelease = p.GetBool("onRelease"),
                    Kind = ParseKind(p.GetString("kind")),
                    Active = p.GetBool("active", true),
                    Live = p.GetBool("live")
                };
                JsonValue names = p["names"];
                if (names.IsArray)
                {
                    e.Names = new string[names.Count];
                    for (int n = 0; n < names.Count; n++) e.Names[n] = names[n].AsString(string.Empty);
                }
                Params.Add(e);
            }
        }

        /// <summary>
        /// Brings a registry in step with this manifest, as a Spectator does with its own copy of the phone's
        /// parameters: every entry the registry knows by name takes the manifest's value (clamped, as always) and
        /// its group's active state. Entries the registry does not know are skipped. Returns how many values
        /// changed.
        /// </summary>
        public int ApplyTo(ParamRegistry registry, ParamSource source = ParamSource.Remote)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            int changed = 0;
            for (int i = 0; i < Params.Count; i++)
            {
                ParamManifestEntry e = Params[i];
                ParamEntry entry;
                if (!registry.TryGet(e.Name, out entry)) continue;
                if (registry.IsGroupActive(entry.Group) != e.Active) registry.SetGroupActive(entry.Group, e.Active);
                if (registry.Set(entry, e.Val, source)) changed++;
            }
            return changed;
        }

        static ParamKind ParseKind(string kind)
        {
            switch (kind)
            {
                case "int": return ParamKind.Int;
                case "bool": return ParamKind.Bool;
                case "enum": return ParamKind.Enum;
                default: return ParamKind.Float;
            }
        }
    }

    /// <summary><c>SceneCmd</c> (0x22, Hub to phone): switch space. <c>{space: "S0".."S6", variant, args}</c>.</summary>
    public sealed class SceneCmdMessage : JsonMessage
    {
        public string Space = "S0";
        public string Variant = string.Empty;
        public JsonValue Args;

        public override byte Id { get { return MessageId.SceneCmd; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object()
                .Set("space", TextOrEmpty(Space))
                .Set("variant", TextOrEmpty(Variant))
                .Set("args", ObjectOrEmpty(Args));
        }

        public override void FromJson(JsonValue json)
        {
            Space = Text(json, "space");
            Variant = Text(json, "variant");
            Args = json["args"];
        }
    }

    /// <summary>
    /// <c>OperatorCmd</c> (0x23, Hub to phone): <c>{cmd: home | recentre | quality | cue | lock | walk | view, args}</c>.
    /// </summary>
    public sealed class OperatorCmdMessage : JsonMessage
    {
        public string Cmd = string.Empty;
        public JsonValue Args;

        public override byte Id { get { return MessageId.OperatorCmd; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object().Set("cmd", TextOrEmpty(Cmd)).Set("args", ObjectOrEmpty(Args));
        }

        public override void FromJson(JsonValue json)
        {
            Cmd = Text(json, "cmd");
            Args = json["args"];
        }
    }

    /// <summary>
    /// <c>Event</c> (0x40, phone to Hub): <c>{t_us, scene, ev, data}</c>. The same object, written compactly on
    /// one line, is a line of <c>events.jsonl</c> (Master Plan section 7.3.3).
    /// </summary>
    public sealed class EventMessage : JsonMessage
    {
        /// <summary>Phone monotonic clock, microseconds.</summary>
        public long TimeUs;
        /// <summary>The scene or space the event belongs to, for example <c>S2</c>.</summary>
        public string Scene = string.Empty;
        /// <summary>Event name, for example <c>param</c>.</summary>
        public string Ev = string.Empty;
        public JsonValue Data;

        public EventMessage() { }

        public EventMessage(long timeUs, string scene, string ev, JsonValue data)
        {
            TimeUs = timeUs;
            Scene = scene;
            Ev = ev;
            Data = data;
        }

        public override byte Id { get { return MessageId.Event; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object()
                .Set("t_us", TimeUs)
                .Set("scene", TextOrEmpty(Scene))
                .Set("ev", TextOrEmpty(Ev))
                .Set("data", ObjectOrEmpty(Data));
        }

        public override void FromJson(JsonValue json)
        {
            TimeUs = json.GetLong("t_us");
            Scene = Text(json, "scene");
            Ev = Text(json, "ev");
            Data = json["data"];
        }

        /// <summary>The event as one line of JSON, with no line break inside and none at the end.</summary>
        public string ToJsonLine() { return ToJson().ToJson(false); }

        /// <summary>Reads one line of an event log. False for a blank or malformed line.</summary>
        public static bool TryParseLine(string line, out EventMessage message)
        {
            message = null;
            JsonValue json;
            if (string.IsNullOrEmpty(line) || !AtlasJson.TryParse(line, out json) || !json.IsObject) return false;
            message = new EventMessage();
            message.FromJson(json);
            return true;
        }
    }

    /// <summary>Event names the link itself emits. Lanes add their own names; the format is the contract.</summary>
    public static class LinkEvents
    {
        /// <summary>A parameter took a new value: <c>{id, name, value, source}</c>.</summary>
        public const string Param = "param";
        /// <summary>An operator command reached the phone: <c>{cmd, ...}</c>.</summary>
        public const string Command = "cmd";
        /// <summary>The watchdog changed state: <c>{state}</c>.</summary>
        public const string Link = "link";
        /// <summary>
        /// The phone's world changed (<c>State.worldVer</c> or the space). The Hub keeps the newest one and hands
        /// it to a Spectator that joins later, which compares the world hash with its own.
        /// </summary>
        public const string World = "world";

        /// <summary>The phone dropped a <c>ParamSet</c>: <c>{reason, count, ids}</c> (lane D contract 2.6).</summary>
        public const string ParamIgnoredEvent = "param.ignored";
        /// <summary>Reason of <see cref="ParamIgnoredEvent"/>: the demo lock is on.</summary>
        public const string IgnoredLock = "lock";
        /// <summary>Reason of <see cref="ParamIgnoredEvent"/>: the link is not in a state that takes remote values.</summary>
        public const string IgnoredDegraded = "degraded";

        /// <summary>
        /// The <c>param.ignored</c> event for a dropped <c>ParamSet</c>. Besides the reason and the count it
        /// names the wire ids, so the Hub can put its record of those parameters back to what the phone holds.
        /// </summary>
        public static EventMessage ParamIgnored(long timeUs, string scene, string reason, ParamSetMessage set)
        {
            JsonValue ids = JsonValue.Array();
            int count = set != null ? set.Count : 0;
            for (int i = 0; i < count; i++) ids.Add((int)set.Ids[i]);
            return new EventMessage(timeUs, scene, ParamIgnoredEvent, JsonValue.Object()
                .Set("reason", reason ?? string.Empty).Set("count", count).Set("ids", ids));
        }

        /// <summary>The <c>param</c> event that echoes an applied value back to the Console (Master Plan section 7.3.2).</summary>
        public static EventMessage ParamChanged(long timeUs, string scene, ParamEntry entry, ParamSource source)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            string src = source == ParamSource.Remote ? "remote" : source == ParamSource.Restore ? "restore" : "local";
            return new EventMessage(timeUs, scene, Param, JsonValue.Object()
                .Set("id", (int)entry.Id)
                .Set("name", entry.Name)
                .Set("value", entry.Value)
                .Set("source", src));
        }

        /// <summary>
        /// Reads a <c>param</c> event as <see cref="ParamChanged"/> wrote it. False for any other event. A
        /// Spectator applies the value to its own registry by name; the id is the phone's wire id.
        /// </summary>
        public static bool TryReadParam(EventMessage message, out ushort id, out string name, out float value)
        {
            id = 0;
            name = null;
            value = 0f;
            if (message == null || message.Ev != Param || message.Data == null || !message.Data.IsObject) return false;
            JsonValue v = message.Data["value"];
            name = message.Data.GetString("name");
            if (name == null || !(v.IsNumber || v.IsBool)) return false;
            id = (ushort)message.Data.GetInt("id");
            value = v.IsBool ? (v.AsBool() ? 1f : 0f) : v.AsFloat();
            return true;
        }
    }

    /// <summary><c>Log</c> (0x42, phone to Hub): <c>{t_us, level, msg}</c>, warnings and errors only.</summary>
    public sealed class LogMessage : JsonMessage
    {
        public long TimeUs;
        /// <summary><c>warn</c> or <c>error</c>.</summary>
        public string Level = string.Empty;
        public string Msg = string.Empty;

        public override byte Id { get { return MessageId.Log; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object().Set("t_us", TimeUs).Set("level", TextOrEmpty(Level)).Set("msg", TextOrEmpty(Msg));
        }

        public override void FromJson(JsonValue json)
        {
            TimeUs = json.GetLong("t_us");
            Level = Text(json, "level");
            Msg = Text(json, "msg");
        }
    }

    /// <summary>
    /// <c>JobStatus</c> (0x50, Hub to phone and spectators): <c>{jobId, roomId, boxId, state, source, error}</c>.
    /// </summary>
    public sealed class JobStatusMessage : JsonMessage
    {
        public string JobId = string.Empty;
        public string RoomId = string.Empty;
        public int BoxId;
        /// <summary><c>queued</c>, <c>matting</c>, <c>generating</c>, <c>cleaning</c>, <c>ready</c> or <c>failed</c>.</summary>
        public string State = string.Empty;
        /// <summary><c>local</c>, <c>hosted</c> or <c>cache</c>.</summary>
        public string Source = string.Empty;
        public string Error = string.Empty;

        public override byte Id { get { return MessageId.JobStatus; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object()
                .Set("jobId", TextOrEmpty(JobId))
                .Set("roomId", TextOrEmpty(RoomId))
                .Set("boxId", BoxId)
                .Set("state", TextOrEmpty(State))
                .Set("source", TextOrEmpty(Source))
                .Set("error", TextOrEmpty(Error));
        }

        public override void FromJson(JsonValue json)
        {
            JobId = Text(json, "jobId");
            RoomId = Text(json, "roomId");
            BoxId = json.GetInt("boxId");
            State = Text(json, "state");
            Source = Text(json, "source");
            Error = Text(json, "error");
        }
    }

    /// <summary>
    /// <c>AssetReady</c> (0x51, Hub to phone and spectators):
    /// <c>{jobId, roomId, boxId, sha256, url, bytes, tris, maxEdge, bounds[3], palette}</c>, and, when the Forge made
    /// a light copy as well, <c>lightSha256, lightUrl, lightBytes, lightTris</c>. A reader that ignores the four
    /// shows the full copy everywhere.
    /// </summary>
    public sealed class AssetReadyMessage : JsonMessage
    {
        public string JobId = string.Empty;
        public string RoomId = string.Empty;
        public int BoxId;
        public string Sha256 = string.Empty;
        public string Url = string.Empty;
        public long Bytes;
        public int Tris;
        /// <summary>Longest triangle edge in metres.</summary>
        public float MaxEdge;
        /// <summary>Size of the mesh bounds in metres: x, y, z.</summary>
        public float BoundsX, BoundsY, BoundsZ;
        /// <summary>Palette cell assigned by the Forge.</summary>
        public int Palette;
        /// <summary>The light copy: SHA-256, path or URL, size in bytes and triangles. "" and 0 when there is none.</summary>
        public string LightSha256 = string.Empty;
        public string LightUrl = string.Empty;
        public long LightBytes;
        public int LightTris;

        public override byte Id { get { return MessageId.AssetReady; } }

        public override JsonValue ToJson()
        {
            JsonValue json = JsonValue.Object()
                .Set("jobId", TextOrEmpty(JobId))
                .Set("roomId", TextOrEmpty(RoomId))
                .Set("boxId", BoxId)
                .Set("sha256", TextOrEmpty(Sha256))
                .Set("url", TextOrEmpty(Url))
                .Set("bytes", Bytes)
                .Set("tris", Tris)
                .Set("maxEdge", MaxEdge)
                .Set("bounds", JsonValue.Array(BoundsX, BoundsY, BoundsZ))
                .Set("palette", Palette);
            // The four light fields travel together, and only when there is a light copy.
            if (!string.IsNullOrEmpty(LightSha256))
                json.Set("lightSha256", LightSha256).Set("lightUrl", TextOrEmpty(LightUrl))
                    .Set("lightBytes", LightBytes).Set("lightTris", LightTris);
            return json;
        }

        public override void FromJson(JsonValue json)
        {
            JobId = Text(json, "jobId");
            RoomId = Text(json, "roomId");
            BoxId = json.GetInt("boxId");
            Sha256 = Text(json, "sha256");
            Url = Text(json, "url");
            Bytes = json.GetLong("bytes");
            Tris = json.GetInt("tris");
            MaxEdge = json.GetFloat("maxEdge");
            BoundsX = json["bounds"][0].AsFloat();
            BoundsY = json["bounds"][1].AsFloat();
            BoundsZ = json["bounds"][2].AsFloat();
            Palette = json.GetInt("palette");
            LightSha256 = Text(json, "lightSha256");
            LightUrl = Text(json, "lightUrl");
            LightBytes = json.GetLong("lightBytes");
            LightTris = json.GetInt("lightTris");
        }
    }

    /// <summary>
    /// <c>AssetAck</c> (0x52, phone to Hub): <c>{jobId, boxId, sha256, ok, loadMs, worstFrameMs, reason}</c>.
    /// </summary>
    public sealed class AssetAckMessage : JsonMessage
    {
        public string JobId = string.Empty;
        public int BoxId;
        public string Sha256 = string.Empty;
        public bool Ok;
        public float LoadMs;
        public float WorstFrameMs;
        public string Reason = string.Empty;

        public override byte Id { get { return MessageId.AssetAck; } }

        public override JsonValue ToJson()
        {
            return JsonValue.Object()
                .Set("jobId", TextOrEmpty(JobId))
                .Set("boxId", BoxId)
                .Set("sha256", TextOrEmpty(Sha256))
                .Set("ok", Ok)
                .Set("loadMs", LoadMs)
                .Set("worstFrameMs", WorstFrameMs)
                .Set("reason", TextOrEmpty(Reason));
        }

        public override void FromJson(JsonValue json)
        {
            JobId = Text(json, "jobId");
            BoxId = json.GetInt("boxId");
            Sha256 = Text(json, "sha256");
            Ok = json.GetBool("ok");
            LoadMs = json.GetFloat("loadMs");
            WorstFrameMs = json.GetFloat("worstFrameMs");
            Reason = Text(json, "reason");
        }
    }

    /// <summary><c>Bye</c> (0x7F, both ways): <c>{reason}</c>, sent before a deliberate disconnect.</summary>
    public sealed class ByeMessage : JsonMessage
    {
        public string Reason = string.Empty;

        public ByeMessage() { }
        public ByeMessage(string reason) { Reason = reason; }

        public override byte Id { get { return MessageId.Bye; } }

        public override JsonValue ToJson() { return JsonValue.Object().Set("reason", TextOrEmpty(Reason)); }

        public override void FromJson(JsonValue json) { Reason = Text(json, "reason"); }
    }
}
