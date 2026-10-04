using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>Files of the asset tests: the Forge's GLBs next to this file and the built-in set of the build.</summary>
    static class AssetFixtures
    {
        static string Project()
        {
            string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
            return string.IsNullOrEmpty(project) ? Directory.GetCurrentDirectory() : project;
        }

        public static string FixtureDirectory()
        {
            return Path.Combine(Project(), "Assets", "Atlas", "Tests", "Telemetry", "Assets", "Fixtures");
        }

        public static string FallbackDirectory()
        {
            return Path.Combine(Project(), "Assets", "Atlas", "Telemetry", "Resources", "AtlasFallback");
        }

        public static byte[] Glb(string name) { return File.ReadAllBytes(Path.Combine(FixtureDirectory(), name + ".glb.bytes")); }

        public static JsonValue Json(string file) { return AtlasJson.ParseUtf8(File.ReadAllBytes(Path.Combine(FixtureDirectory(), file))); }

        /// <summary>The reader the app uses, with the disk in place of Resources.</summary>
        public static byte[] ReadFallback(string name)
        {
            string path = Path.Combine(FallbackDirectory(), name == FallbackSet.ManifestName ? "manifest.json" : name + ".bytes");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public static FallbackSet LoadFallback() { return FallbackSet.Load(ReadFallback); }

        public static float3 Dims(JsonValue entry)
        {
            JsonValue d = entry["dims"];
            return new float3(d[0].AsFloat(), d[1].AsFloat(), d[2].AsFloat());
        }

        public static GlbExpectation Expect(float3 dims, int triTarget, string sha = null)
        {
            return new GlbExpectation { DimX = dims.x, DimY = dims.y, DimZ = dims.z, TriTarget = triTarget, Sha256 = sha };
        }
    }

    /// <summary>What the asset client asked of the room content, in order.</summary>
    sealed class FakeAssetRoom : IAssetRoom
    {
        public readonly List<string> Calls = new List<string>();
        public readonly HashSet<int> Requested = new HashSet<int>();
        public readonly Dictionary<int, string> Generated = new Dictionary<int, string>();
        public readonly Dictionary<int, int> GeneratedTriangles = new Dictionary<int, int>();
        /// <summary>The light copy each object holds: sha and triangles.</summary>
        public readonly Dictionary<int, string> GeneratedLight = new Dictionary<int, string>();
        public readonly Dictionary<int, int> GeneratedLightTriangles = new Dictionary<int, int>();
        /// <summary>Set to refuse every light mesh, as a room content without a generated mesh does.</summary>
        public bool RefuseLight;
        public readonly Dictionary<int, int> BuiltInTriangles = new Dictionary<int, int>();
        /// <summary>What each object shows now: "box", "builtin" or "generated".</summary>
        public readonly Dictionary<int, string> Shown = new Dictionary<int, string>();

        public bool SetRequested(int objectId)
        {
            Requested.Add(objectId);
            Calls.Add("requested:" + objectId);
            return true;
        }

        public bool PlaceGenerated(int objectId, MeshData mesh, string sha256)
        {
            Generated[objectId] = sha256;
            GeneratedTriangles[objectId] = mesh.TriangleCount;
            Shown[objectId] = "generated";
            Calls.Add("generated:" + objectId);
            return true;
        }

        public bool PlaceGeneratedLight(int objectId, MeshData mesh, string sha256)
        {
            if (RefuseLight || ShownFor(objectId) != "generated")
            {
                Calls.Add("lightrefused:" + objectId);
                return false;
            }
            GeneratedLight[objectId] = sha256;
            GeneratedLightTriangles[objectId] = mesh.TriangleCount;
            Calls.Add("light:" + objectId);
            return true;
        }

        public bool PlaceBuiltIn(int objectId, MeshData mesh)
        {
            BuiltInTriangles[objectId] = mesh.TriangleCount;
            Shown[objectId] = "builtin";
            Calls.Add("builtin:" + objectId);
            return true;
        }

        public string ShownFor(int objectId)
        {
            string shown;
            return Shown.TryGetValue(objectId, out shown) ? shown : "box";
        }
    }

    /// <summary>Keeps every event as parsed JSON.</summary>
    sealed class RecordingEvents : IEventLog
    {
        public readonly List<JsonValue> Assets = new List<JsonValue>();

        public void Log(string ev) { }
        public void Log(string ev, string key, double value) { }
        public void Log(string ev, string key, string value) { }
        public void Log(string ev, string key1, double value1, string key2, double value2) { }
        public void LogJson(string ev, string dataJson)
        {
            if (ev == AssetClientCore.EventName) Assets.Add(AtlasJson.Parse(dataJson));
        }

        public int CountOf(string step, int boxId = -1)
        {
            int n = 0;
            foreach (JsonValue e in Assets)
                if (e.GetString("step") == step && (boxId < 0 || e.GetInt("boxId", -1) == boxId)) n++;
            return n;
        }

        public JsonValue Last(string step, int boxId)
        {
            for (int i = Assets.Count - 1; i >= 0; i--)
                if (Assets[i].GetString("step") == step && Assets[i].GetInt("boxId", -1) == boxId) return Assets[i];
            return JsonValue.Null;
        }
    }

    /// <summary>
    /// A Forge in memory with the routes and answers of the real one (station/forge/forge/app.py): health, rooms,
    /// objects, the job list and assets. A test decides how each job ends. While it is down no request is
    /// answered: each ends with status 0 when its time-out has passed, as a phone on a network with no Forge sees it.
    /// </summary>
    sealed class FakeForge : IForgeTransport
    {
        public sealed class Job
        {
            public string JobId, RoomId, State = "queued", Error = "", Sha, LightSha;
            public int BoxId, TriTarget;
            public JsonValue Meta;
            public int Views;
        }

        sealed class Pending { public double At; public int Status; public byte[] Body; }

        public const string Url = "http://forge.test:8000";

        public double Now;
        public bool Up = true;
        /// <summary>Seconds a request takes while the Forge is up.</summary>
        public double Latency = 0.05;
        /// <summary>Status of POST /rooms; 201 as the real Forge answers.</summary>
        public int RoomStatus = 201;
        public readonly List<string> Requests = new List<string>();
        public readonly List<string> Rooms = new List<string>();
        public readonly List<Job> Jobs = new List<Job>();
        public readonly Dictionary<string, byte[]> Assets = new Dictionary<string, byte[]>();
        /// <summary>Boxes the Forge answers with a cached asset at once: boxId -> hash.</summary>
        public readonly Dictionary<int, string> Cached = new Dictionary<int, string>();
        /// <summary>Boxes whose cached answer also names a light copy: boxId -> its hash.</summary>
        public readonly Dictionary<int, string> CachedLight = new Dictionary<int, string>();
        public int Cancelled;

        readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        int _handle;

        public int Begin(string method, string url, byte[] body, string contentType, float timeoutSeconds)
        {
            string path = url.StartsWith(Url, StringComparison.Ordinal) ? url.Substring(Url.Length) : url;
            Requests.Add(method + " " + path);
            var pending = new Pending();
            if (!Up) pending.At = Now + timeoutSeconds;
            else
            {
                pending.At = Now + Latency;
                Answer(method, path, body, contentType, pending);
            }
            _pending[++_handle] = pending;
            return _handle;
        }

        public bool TryTake(int handle, out int status, out byte[] body)
        {
            status = 0;
            body = null;
            Pending pending;
            if (!_pending.TryGetValue(handle, out pending)) return true;
            if (Now < pending.At) return false;
            status = pending.Status;
            body = pending.Body;
            _pending.Remove(handle);
            return true;
        }

        public void Cancel(int handle)
        {
            if (_pending.Remove(handle)) Cancelled++;
        }

        public int CountOf(string prefix)
        {
            int n = 0;
            foreach (string r in Requests) if (r.StartsWith(prefix, StringComparison.Ordinal)) n++;
            return n;
        }

        public Job JobOf(int boxId)
        {
            for (int i = Jobs.Count - 1; i >= 0; i--) if (Jobs[i].BoxId == boxId) return Jobs[i];
            return null;
        }

        /// <summary>The job of the box becomes ready with this GLB.</summary>
        public string Finish(int boxId, byte[] glb, string claimedSha = null)
        {
            string sha = claimedSha ?? GlbValidator.Sha256Hex(glb, glb.Length);
            Assets[sha] = glb;
            Job job = JobOf(boxId);
            job.State = "ready";
            job.Sha = sha;
            return sha;
        }

        /// <summary>The job becomes ready with a full and a light GLB.</summary>
        public string FinishWithLight(int boxId, byte[] full, byte[] light, string claimedLightSha = null)
        {
            string sha = Finish(boxId, full);
            string lightSha = claimedLightSha ?? GlbValidator.Sha256Hex(light, light.Length);
            Assets[lightSha] = light;
            JobOf(boxId).LightSha = lightSha;
            return sha;
        }

        public void Fail(int boxId, string error)
        {
            Job job = JobOf(boxId);
            job.State = "failed";
            job.Error = error;
        }

        void Answer(string method, string path, byte[] body, string contentType, Pending pending)
        {
            pending.Status = 404;
            if (method == "GET" && path == "/health")
            {
                Reply(pending, 200, JsonValue.Object().Set("ok", true).Set("generator", "procedural"));
            }
            else if (method == "POST" && path == "/rooms")
            {
                JsonValue room = AtlasJson.ParseUtf8(body);
                if (RoomStatus == 201) Rooms.Add(room.GetString("id"));
                Reply(pending, RoomStatus, JsonValue.Object().Set("roomId", room.GetString("id")));
            }
            else if (method == "POST" && path.StartsWith("/rooms/", StringComparison.Ordinal) && path.EndsWith("/objects", StringComparison.Ordinal))
            {
                string roomId = path.Substring(7, path.Length - 7 - 8);
                if (!Rooms.Contains(roomId)) return;
                if (contentType != ForgeProtocol.MultipartContentType) { pending.Status = 422; return; }
                JsonValue meta = MetaOf(body);
                var job = new Job
                {
                    JobId = (Jobs.Count + 1).ToString("x16"), RoomId = roomId, BoxId = meta.GetInt("boxId"),
                    TriTarget = meta.GetInt("triTarget"), Meta = meta, Views = CountViews(body)
                };
                Jobs.Add(job);
                string cached;
                if (Cached.TryGetValue(job.BoxId, out cached))
                {
                    job.State = "ready";
                    job.Sha = cached;
                    JsonValue answer = JsonValue.Object().Set("jobId", job.JobId).Set("cached", true).Set("sha256", cached);
                    string cachedLight;
                    if (CachedLight.TryGetValue(job.BoxId, out cachedLight))
                    {
                        job.LightSha = cachedLight;
                        answer.Set("lightSha256", cachedLight).Set("lightUrl", "/assets/" + cachedLight + ".glb");
                    }
                    Reply(pending, 200, answer);
                }
                else Reply(pending, 202, JsonValue.Object().Set("jobId", job.JobId));
            }
            else if (method == "GET" && path.StartsWith("/jobs?room=", StringComparison.Ordinal))
            {
                string roomId = path.Substring(11);
                JsonValue list = JsonValue.Array();
                foreach (Job job in Jobs)
                {
                    if (job.RoomId != roomId) continue;
                    JsonValue j = JsonValue.Object().Set("jobId", job.JobId).Set("roomId", job.RoomId).Set("boxId", job.BoxId)
                        .Set("state", job.State).Set("source", "local").Set("error", job.Error).Set("triTarget", job.TriTarget);
                    if (job.State == "ready") j.Set("sha256", job.Sha).Set("url", "/assets/" + job.Sha + ".glb");
                    if (job.State == "ready" && job.LightSha != null)
                        j.Set("lightSha256", job.LightSha).Set("lightUrl", "/assets/" + job.LightSha + ".glb");
                    list.Add(j);
                }
                Reply(pending, 200, list);
            }
            else if (method == "GET" && path.StartsWith("/assets/", StringComparison.Ordinal))
            {
                byte[] glb;
                if (Assets.TryGetValue(path.Substring(8, path.Length - 8 - 4), out glb))
                {
                    pending.Status = 200;
                    pending.Body = glb;
                }
            }
            else if (method == "DELETE" && path.StartsWith("/rooms/", StringComparison.Ordinal))
            {
                pending.Status = Rooms.Remove(path.Substring(7)) ? 204 : 404;
            }
        }

        static void Reply(Pending pending, int status, JsonValue json)
        {
            pending.Status = status;
            pending.Body = Encoding.UTF8.GetBytes(json.ToJson());
        }

        // The field `meta` of the multipart body, as a form parser finds it.
        static JsonValue MetaOf(byte[] body)
        {
            string text = Encoding.GetEncoding("iso-8859-1").GetString(body);
            const string head = "name=\"meta\"\r\n\r\n";
            int start = text.IndexOf(head, StringComparison.Ordinal) + head.Length;
            int end = text.IndexOf("\r\n--" + ForgeProtocol.Boundary, start, StringComparison.Ordinal);
            return AtlasJson.Parse(text.Substring(start, end - start));
        }

        static int CountViews(byte[] body)
        {
            string text = Encoding.GetEncoding("iso-8859-1").GetString(body);
            int n = 0;
            while (text.IndexOf("name=\"view" + n + "\"; filename=\"view" + n + ".jpg\"\r\nContent-Type: image/jpeg\r\n\r\n", StringComparison.Ordinal) >= 0) n++;
            return n;
        }
    }
}
