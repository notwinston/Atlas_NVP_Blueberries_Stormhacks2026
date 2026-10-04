using System;
using System.Collections.Generic;
using System.Text;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>
    /// HTTP as the asset client needs it: start a request, ask later whether it has ended. Nothing here waits,
    /// so the client can be driven from step 8b of the frame (contract README F5). The app uses
    /// UnityWebRequest; tests use a scripted Forge.
    /// </summary>
    public interface IForgeTransport
    {
        /// <summary>Starts a request and returns its handle, which is never 0.</summary>
        /// <param name="body">Null for a request with no body.</param>
        int Begin(string method, string url, byte[] body, string contentType, float timeoutSeconds);
        /// <summary>
        /// True once the request has ended; the handle is then released. <paramref name="status"/> is the HTTP
        /// status, or 0 when no answer came (no connection, time-out).
        /// </summary>
        bool TryTake(int handle, out int status, out byte[] body);
        /// <summary>Abandons a request; its answer is dropped.</summary>
        void Cancel(int handle);
    }

    /// <summary>Where the asset client shows its results. The app's implementation talks to the room content.</summary>
    public interface IAssetRoom
    {
        /// <summary>The Forge holds a job for the object.</summary>
        bool SetRequested(int objectId);
        /// <summary>The generated mesh, in the box-local frame. The data is only valid during the call.</summary>
        bool PlaceGenerated(int objectId, MeshData mesh, string sha256);
        /// <summary>The light copy of an object's generated mesh. False when the object has no generated mesh or cannot take it.</summary>
        bool PlaceGeneratedLight(int objectId, MeshData mesh, string sha256);
        /// <summary>The built-in mesh at the box's size. The data is only valid during the call.</summary>
        bool PlaceBuiltIn(int objectId, MeshData mesh);
    }

    /// <summary>The GLB cache, keyed by SHA-256. Called on the worker thread.</summary>
    public interface IAssetCache
    {
        /// <summary>The file, or null.</summary>
        byte[] TryRead(string sha256);
        void Write(string sha256, byte[] glb);
        void Delete(string sha256);
    }

    /// <summary>Runs hashing, validation and mesh reading off the frame.</summary>
    public interface IAssetWorker
    {
        void Run(Action<object> work, object state);
    }

    /// <summary>Runs the work at once on the calling thread: headless tests.</summary>
    public sealed class InlineAssetWorker : IAssetWorker
    {
        public void Run(Action<object> work, object state) { work(state); }
    }

    /// <summary>A cache in memory: tests, and a phone whose storage cannot be written.</summary>
    public sealed class MemoryAssetCache : IAssetCache
    {
        readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>();

        public int Count { get { lock (_files) return _files.Count; } }
        public bool Contains(string sha256) { lock (_files) return _files.ContainsKey(sha256); }
        public byte[] TryRead(string sha256) { lock (_files) { byte[] glb; return _files.TryGetValue(sha256, out glb) ? glb : null; } }
        public void Write(string sha256, byte[] glb) { lock (_files) _files[sha256] = glb; }
        public void Delete(string sha256) { lock (_files) _files.Remove(sha256); }
    }

    /// <summary>The bodies the client sends to the Forge and the answers it reads (Master Plan Appendix B.4).</summary>
    public static class ForgeProtocol
    {
        public const string Boundary = "atlas-forge-boundary-5f2c9a71d3e84b06";
        public const string MultipartContentType = "multipart/form-data; boundary=" + Boundary;
        public const string JsonContentType = "application/json";

        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>The <c>meta</c> field of POST /rooms/{roomId}/objects.</summary>
        /// <param name="triTargetLight">The room's light target, sent when the photo set carries none; 0 sends none.</param>
        public static JsonValue Meta(ObjectPhotoSet photos, int triTarget, string backend, int triTargetLight = 0)
        {
            JsonValue views = JsonValue.Array();
            int count = ViewCount(photos);
            for (int i = 0; i < count; i++)
            {
                ObjectView view = photos.Views[i];
                JsonValue camToBox = JsonValue.Array();
                // Row by row: row r, column c is CamToBox[c][r] in Unity.Mathematics' column-major storage.
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        camToBox.Add(view.CamToBox[c][r]);
                views.Add(JsonValue.Object()
                    .Set("score", view.Score)
                    .Set("azimuthDeg", view.AzimuthDeg)
                    .Set("intrinsics", JsonValue.Array(view.Intrinsics.x, view.Intrinsics.y, view.Intrinsics.z, view.Intrinsics.w))
                    .Set("camToBox", camToBox));
            }
            JsonValue meta = JsonValue.Object()
                .Set("boxId", photos.ObjectId)
                .Set("category", ObjectCategories.Name(photos.Category))
                .Set("dims", JsonValue.Array(photos.Size.x, photos.Size.y, photos.Size.z))
                .Set("yaw", photos.Yaw)
                .Set("triTarget", triTarget)
                .Set("backend", backend)
                .Set("views", views);
            // Both are optional: a Forge that does not know them works as before for the job.
            int light = photos.TriTargetLight > 0 ? photos.TriTargetLight : triTargetLight;
            if (light > 0) meta.Set("triTargetLight", light);
            if (photos.Profile != null) meta.Set("profile", Convert.ToBase64String(photos.Profile));
            return meta;
        }

        /// <summary>Views that carry a JPEG, at most three; the Forge reads view0, view1, view2 in order.</summary>
        public static int ViewCount(ObjectPhotoSet photos)
        {
            if (photos == null || photos.Views == null) return 0;
            int n = 0;
            while (n < photos.Views.Length && n < 3 && photos.Views[n].Jpeg != null && photos.Views[n].Jpeg.Length > 0) n++;
            return n;
        }

        /// <summary>The multipart body of POST /rooms/{roomId}/objects: the field <c>meta</c>, then the files.</summary>
        public static byte[] ObjectBody(ObjectPhotoSet photos, int triTarget, string backend, int triTargetLight = 0)
        {
            int count = ViewCount(photos);
            var heads = new byte[count + 1][];
            heads[0] = s_utf8.GetBytes("--" + Boundary + "\r\nContent-Disposition: form-data; name=\"meta\"\r\n\r\n"
                + Meta(photos, triTarget, backend, triTargetLight).ToJson() + "\r\n");
            int total = heads[0].Length;
            for (int i = 0; i < count; i++)
            {
                string name = "view" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                heads[i + 1] = s_utf8.GetBytes("--" + Boundary + "\r\nContent-Disposition: form-data; name=\"" + name
                    + "\"; filename=\"" + name + ".jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
                total += heads[i + 1].Length + photos.Views[i].Jpeg.Length + 2;
            }
            byte[] tail = s_utf8.GetBytes("--" + Boundary + "--\r\n");
            var body = new byte[total + tail.Length];
            int at = 0;
            Buffer.BlockCopy(heads[0], 0, body, at, heads[0].Length);
            at += heads[0].Length;
            for (int i = 0; i < count; i++)
            {
                byte[] jpeg = photos.Views[i].Jpeg;
                Buffer.BlockCopy(heads[i + 1], 0, body, at, heads[i + 1].Length);
                at += heads[i + 1].Length;
                Buffer.BlockCopy(jpeg, 0, body, at, jpeg.Length);
                at += jpeg.Length;
                body[at++] = (byte)'\r';
                body[at++] = (byte)'\n';
            }
            Buffer.BlockCopy(tail, 0, body, at, tail.Length);
            return body;
        }

        public static byte[] Utf8(string text) { return s_utf8.GetBytes(text ?? string.Empty); }

        /// <summary>Parses a JSON answer; Null when the body is empty or not JSON.</summary>
        public static JsonValue Json(byte[] body)
        {
            JsonValue value;
            string error;
            if (body == null || body.Length == 0 || !AtlasJson.TryParseUtf8(body, 0, body.Length, true, out value, out error))
                return JsonValue.Null;
            return value;
        }

        /// <summary>The path of an asset on the Forge.</summary>
        public static string AssetPath(string sha256) { return "/assets/" + sha256 + ".glb"; }

        /// <summary>True for 64 hexadecimal digits: the only thing that may become a file name.</summary>
        public static bool IsSha256(string text)
        {
            if (text == null || text.Length != 64) return false;
            for (int i = 0; i < 64; i++)
            {
                char c = text[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }
    }
}
