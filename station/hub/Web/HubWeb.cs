using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Station
{
    /// <summary>
    /// The Hub's web server (Master Plan Appendix B.3): the Console's static files, the pairing page, the
    /// WebSocket feed and the JSON routes. Every body is read and written with <see cref="AtlasJson"/>, the same
    /// code the phone uses, so a message forwarded to the Console is the message the phone sent.
    /// </summary>
    public static class HubWeb
    {
        const int MaxBodyBytes = 1 << 20;
        // A perf.csv grows by about half a megabyte per hour; a long day of walking must still fit.
        const int MaxPerfUploadBytes = 32 << 20;
        const int MaxSocketMessageBytes = 1 << 16;

        public static WebApplication Build(HubServer hub, HubOptions options)
        {
            string webRoot = Path.GetFullPath(options.WebRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"));
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(HubWeb).Assembly.GetName().Name
            });
            builder.Logging.ClearProviders();
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3));
            IPAddress bind = IPAddress.Parse(options.BindAddress);
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(bind, options.HttpPort);
                kestrel.Limits.MaxRequestBodySize = MaxBodyBytes;
                kestrel.AddServerHeader = false;
            });

            WebApplication app = builder.Build();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

            // An unexpected failure in a handler is a 500 with a JSON body, never a dropped connection.
            app.Use(async (context, next) =>
            {
                try { await next(context); }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    if (context.Response.HasStarted) return;
                    await Error(context, 500, e.GetType().Name + ": " + e.Message);
                }
            });

            if (Directory.Exists(webRoot))
            {
                var files = new PhysicalFileProvider(webRoot);
                app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = files,
                    // The Console is under development on the same machine: never serve a stale page.
                    OnPrepareResponse = c => c.Context.Response.Headers["Cache-Control"] = "no-cache"
                });
            }

            MapPairing(app, hub, options);
            MapFeed(app, hub);
            MapSession(app, hub);
            MapParams(app, hub);
            MapCommands(app, hub);
            MapPresets(app, hub, options);
            MapLogs(app, hub, options);
            MapNetwork(app, options);
            MapPerfUpload(app, hub);
            MapView(app, hub);
            MapInternal(app, hub);
            return app;
        }

        /// <summary>The port Kestrel is listening on, read back from the server when the options asked for 0.</summary>
        public static int BoundPort(WebApplication app, int requested)
        {
            IServerAddressesFeature feature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            if (feature != null)
            {
                foreach (string address in feature.Addresses)
                {
                    Uri uri;
                    if (Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.Port > 0) return uri.Port;
                }
            }
            return requested;
        }

        // ---- pairing ----------------------------------------------------------------------------------

        static void MapPairing(WebApplication app, HubServer hub, HubOptions options)
        {
            app.MapGet("/pair", (HttpContext c) =>
            {
                string address = PairAddress(c, hub, options);
                List<string> others = LanAddress.Candidates();
                others.Remove(address);
                string html = PairPage.Html(hub.PairUrl(address), address, hub.UdpPort, hub.HttpPort, others, hub.HasPhone);
                return Text(c, 200, "text/html; charset=utf-8", html);
            });

            app.MapGet("/pair/qr.svg", (HttpContext c) =>
                Text(c, 200, "image/svg+xml", PairPage.Svg(hub.PairUrl(PairAddress(c, hub, options)))));

            app.MapGet("/api/pair", (HttpContext c) =>
            {
                string address = PairAddress(c, hub, options);
                JsonValue candidates = JsonValue.Array();
                foreach (string ip in LanAddress.Candidates()) candidates.Add(ip);
                return Json(c, 200, JsonValue.Object()
                    .Set("url", hub.PairUrl(address)).Set("address", address).Set("udpPort", hub.UdpPort).Set("httpPort", hub.HttpPort)
                    .Set("candidates", candidates));
            });
        }

        // The address in the QR: the one asked for, else the configured one, else the address this very request
        // came in on (the interface the browser can reach is the one the phone can reach), else the best guess.
        static string PairAddress(HttpContext c, HubServer hub, HubOptions options)
        {
            string asked = c.Request.Query["ip"];
            IPAddress ip;
            if (!string.IsNullOrEmpty(asked) && IPAddress.TryParse(asked, out ip) && ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            if (!string.IsNullOrEmpty(options.AdvertiseAddress)) return options.AdvertiseAddress;
            IPAddress local = c.Connection.LocalIpAddress;
            if (local != null)
            {
                if (local.IsIPv4MappedToIPv6) local = local.MapToIPv4();
                if (local.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(local)) return local.ToString();
            }
            return hub.StationAddress;
        }

        // ---- WebSocket feed ---------------------------------------------------------------------------

        // ---- network (NET-03): the bundled atlas.network/1 files ----------------------------------------

        static readonly System.Text.RegularExpressions.Regex NetworkFileName =
            new System.Text.RegularExpressions.Regex(@"^network_(\d+)\.json$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        static void MapNetwork(WebApplication app, HubOptions options)
        {
            app.MapGet("/api/network", async (HttpContext c) =>
            {
                string folder = Path.Combine(options.ProjectFolder ?? string.Empty, "Assets", "StreamingAssets", "networks");
                string file = c.Request.Query["file"];
                if (file == null)
                {
                    var found = new List<KeyValuePair<long, string>>();
                    if (Directory.Exists(folder))
                        foreach (string path in Directory.GetFiles(folder))
                        {
                            string name = Path.GetFileName(path);
                            System.Text.RegularExpressions.Match m = NetworkFileName.Match(name);
                            long seed;
                            if (m.Success && long.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seed))
                                found.Add(new KeyValuePair<long, string>(seed, name));
                        }
                    found.Sort((a, b) => a.Key.CompareTo(b.Key));
                    JsonValue files = JsonValue.Array();
                    foreach (KeyValuePair<long, string> f in found) files.Add(f.Value);
                    await Json(c, 200, JsonValue.Object().Set("files", files));
                    return;
                }
                if (!NetworkFileName.IsMatch(file))
                {
                    await Error(c, 400, "file must look like network_7.json");
                    return;
                }
                string full = Path.Combine(folder, file);
                if (!File.Exists(full))
                {
                    await Error(c, 404, "no such network file");
                    return;
                }
                byte[] bytes = await File.ReadAllBytesAsync(full);
                c.Response.StatusCode = 200;
                c.Response.ContentType = "application/json";
                c.Response.ContentLength = bytes.Length;
                c.Response.Headers["Cache-Control"] = "no-store";
                await c.Response.Body.WriteAsync(bytes, 0, bytes.Length);
            });
        }

        static void MapFeed(WebApplication app, HubServer hub)
        {
            app.Map("/ws", async (HttpContext c) =>
            {
                if (!c.WebSockets.IsWebSocketRequest)
                {
                    await Error(c, 400, "/ws is a WebSocket endpoint");
                    return;
                }
                using (WebSocket socket = await c.WebSockets.AcceptWebSocketAsync())
                using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, hub.Stopping))
                {
                    ConsoleFeed.Client client = hub.Feed.Add();
                    hub.Greet(client);
                    Task sending = SendLoop(socket, client, stop.Token);
                    try
                    {
                        await ReceiveLoop(socket, client, hub, stop.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (WebSocketException) { }
                    finally
                    {
                        hub.Feed.Remove(client);
                        stop.Cancel();
                        try { await sending; } catch (OperationCanceledException) { } catch (WebSocketException) { }
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            using (var closing = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                            {
                                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closing.Token); }
                                catch (OperationCanceledException) { }
                                catch (WebSocketException) { }
                            }
                        }
                    }
                }
            });
        }

        static async Task SendLoop(WebSocket socket, ConsoleFeed.Client client, CancellationToken stop)
        {
            byte[] buffer = new byte[4096];
            while (await client.Reader.WaitToReadAsync(stop))
            {
                string json;
                while (client.Reader.TryRead(out json))
                {
                    int max = Encoding.UTF8.GetMaxByteCount(json.Length);
                    if (max > buffer.Length) buffer = new byte[max];
                    int n = Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
                    await socket.SendAsync(new ArraySegment<byte>(buffer, 0, n), WebSocketMessageType.Text, true, stop);
                }
            }
        }

        static async Task ReceiveLoop(WebSocket socket, ConsoleFeed.Client client, HubServer hub, CancellationToken stop)
        {
            var buffer = new byte[MaxSocketMessageBytes];
            while (socket.State == WebSocketState.Open)
            {
                int length = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), stop);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    length += result.Count;
                    if (length == buffer.Length && !result.EndOfMessage)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "message too large", stop);
                        return;
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text) continue;
                JsonValue message;
                string error;
                JsonValue reply = AtlasJson.TryParseUtf8(buffer, 0, length, false, out message, out error)
                    ? hub.HandleConsoleMessage(message)
                    : JsonValue.Object().Set("t", "error").Set("msg", "bad JSON: " + error);
                if (reply != null) client.Send(reply.ToJson());
            }
        }

        // ---- session, state, telemetry ----------------------------------------------------------------

        static void MapSession(WebApplication app, HubServer hub)
        {
            app.MapGet("/api/session", (HttpContext c) => Json(c, 200, hub.GetSession()));

            app.MapGet("/api/state", (HttpContext c) =>
            {
                JsonValue state = hub.GetState();
                return state != null ? Json(c, 200, state) : Error(c, 404, "no State received yet");
            });

            app.MapGet("/api/telemetry", (HttpContext c) =>
            {
                JsonValue row = hub.GetTelemetry();
                return row != null ? Json(c, 200, row) : Error(c, 404, "no Telemetry received yet");
            });
        }

        // ---- parameters -------------------------------------------------------------------------------

        static void MapParams(WebApplication app, HubServer hub)
        {
            app.MapGet("/api/params", (HttpContext c) => Json(c, 200, hub.GetParams()));

            app.MapPost("/api/params", async (HttpContext c) =>
            {
                JsonValue body = await ReadJson(c);
                if (body == null) return;
                int count;
                HubResult result = hub.SetParams(body, out count);
                if (result == HubResult.Ok) await Json(c, 200, JsonValue.Object().Set("sent", count));
                else await Error(c, StatusOf(result), HubServer.Describe(result));
            });

            app.MapGet("/api/params/{key}", (HttpContext c) =>
            {
                JsonValue param = hub.GetParam(Route(c, "key"));
                return param != null ? Json(c, 200, param) : Error(c, 404, "no such parameter");
            });

            RequestDelegate set = async c =>
            {
                JsonValue body = await ReadJson(c);
                if (body == null) return;
                JsonValue v = body.IsObject ? body["value"] : body;
                if (!(v.IsNumber || v.IsBool))
                {
                    await Error(c, 400, "expected {\"value\": number}");
                    return;
                }
                JsonValue applied;
                HubResult result = hub.SetParam(Route(c, "key"), v.IsBool ? (v.AsBool() ? 1f : 0f) : v.AsFloat(), out applied);
                if (result == HubResult.Ok) await Json(c, 200, applied);
                else await Error(c, StatusOf(result), result == HubResult.Unknown ? "no such parameter" : HubServer.Describe(result));
            };
            app.MapPost("/api/params/{key}", set);
            app.MapPut("/api/params/{key}", set);
        }

        // ---- operator commands ------------------------------------------------------------------------

        static void MapCommands(WebApplication app, HubServer hub)
        {
            app.MapPost("/api/op", async (HttpContext c) =>
            {
                JsonValue body = await ReadJson(c);
                if (body == null) return;
                string cmd = body.GetString("cmd", string.Empty);
                HubResult result = hub.SendOperator(cmd, body["args"].IsObject ? body["args"] : null);
                if (result == HubResult.Ok) await Json(c, 200, JsonValue.Object().Set("ok", true).Set("cmd", cmd));
                else await Error(c, StatusOf(result), result == HubResult.Unknown
                    ? "unknown command '" + cmd + "' (home, recentre, quality, cue, lock, walk, view)"
                    : HubServer.Describe(result));
            });

            app.MapPost("/api/scene", async (HttpContext c) =>
            {
                JsonValue body = await ReadJson(c);
                if (body == null) return;
                string space = HubServer.SpaceName(body["space"]);
                HubResult result = hub.SendScene(space, body.GetString("variant", string.Empty), body["args"].IsObject ? body["args"] : null);
                if (result == HubResult.Ok) await Json(c, 200, JsonValue.Object().Set("ok", true).Set("space", space));
                else await Error(c, StatusOf(result), result == HubResult.Unknown ? "unknown space '" + space + "' (S0 to S6)" : HubServer.Describe(result));
            });
        }

        // ---- presets ----------------------------------------------------------------------------------

        static void MapPresets(WebApplication app, HubServer hub, HubOptions options)
        {
            app.MapGet("/api/presets", (HttpContext c) =>
            {
                JsonValue list = JsonValue.Array();
                foreach (string name in hub.Presets.List())
                {
                    JsonValue preset;
                    if (!hub.Presets.TryLoad(name, out preset)) continue;
                    list.Add(JsonValue.Object().Set("name", name).Set("count", preset["values"].Count)
                        .Set("modified", hub.Presets.Modified(name).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)));
                }
                return Json(c, 200, JsonValue.Object().Set("presets", list));
            });

            app.MapGet("/api/presets/{name}", (HttpContext c) =>
            {
                string name = Route(c, "name");
                JsonValue preset;
                return hub.Presets.TryLoad(name, out preset)
                    ? Json(c, 200, JsonValue.Object().Set("name", name).Set("values", preset["values"]))
                    : Error(c, 404, "no such preset");
            });

            // Saves the values in the body, or the current value of every parameter when the body is empty.
            app.MapPost("/api/presets/{name}", async (HttpContext c) =>
            {
                string name = Route(c, "name");
                if (!Names.IsPreset(name))
                {
                    await Error(c, 400, "a preset name is 1 to 40 letters, digits, '-' or '_'");
                    return;
                }
                JsonValue body = await ReadJson(c, true);
                if (body == null) return;
                if (body.IsNull) body = hub.GetSnapshot();
                if (!body.IsObject || !body["values"].IsObject)
                {
                    await Error(c, 400, "expected {\"values\": {id: value}}");
                    return;
                }
                hub.Presets.Save(name, JsonValue.Object().Set("values", body["values"]));
                hub.LogPreset("save", name);
                c.Response.StatusCode = 204;
            });

            app.MapDelete("/api/presets/{name}", (HttpContext c) =>
            {
                string name = Route(c, "name");
                if (!hub.Presets.Delete(name)) return Error(c, 404, "no such preset");
                hub.LogPreset("delete", name);
                c.Response.StatusCode = 204;
                return Task.CompletedTask;
            });

            // Loading replays every value of the preset in one ParamSet (section 7.3.2).
            app.MapPost("/api/presets/{name}/load", (HttpContext c) =>
            {
                string name = Route(c, "name");
                JsonValue preset;
                if (!hub.Presets.TryLoad(name, out preset)) return Error(c, 404, "no such preset");
                int count;
                HubResult result = hub.SetParams(preset, out count);
                if (result != HubResult.Ok) return Error(c, StatusOf(result), HubServer.Describe(result));
                hub.LogPreset("load", name);
                c.Response.StatusCode = 204;
                return Task.CompletedTask;
            });

            // Bake writes the preset into the Unity project's defaults (section 7.3.2, contract 1.7).
            app.MapPost("/api/presets/{name}/bake", (HttpContext c) =>
            {
                string name = Route(c, "name");
                JsonValue preset;
                if (!hub.Presets.TryLoad(name, out preset)) return Error(c, 404, "no such preset");
                string written;
                if (!hub.Presets.Bake(name, options.ProjectFolder, out written))
                    return Error(c, 409, "no Unity project at " + options.ProjectFolder + " (start the Hub with --project)");
                hub.LogPreset("bake", name);
                c.Response.StatusCode = 204;
                return Task.CompletedTask;
            });
        }

        // ---- logs -------------------------------------------------------------------------------------

        static void MapLogs(WebApplication app, HubServer hub, HubOptions options)
        {
            string root = Path.GetFullPath(options.LogRoot);

            // Newest first. /logs is the same list under the folder's own name.
            RequestDelegate sessionList = c =>
            {
                var folders = new List<DirectoryInfo>();
                if (Directory.Exists(root))
                    foreach (DirectoryInfo dir in new DirectoryInfo(root).GetDirectories())
                        if (Names.IsSafe(dir.Name)) folders.Add(dir);
                folders.Sort((a, b) =>
                {
                    int byTime = b.CreationTimeUtc.CompareTo(a.CreationTimeUtc);
                    return byTime != 0 ? byTime : string.CompareOrdinal(b.Name, a.Name);
                });
                JsonValue sessions = JsonValue.Array();
                foreach (DirectoryInfo dir in folders) sessions.Add(dir.Name);
                return Json(c, 200, JsonValue.Object().Set("current", hub.SessionId).Set("sessions", sessions));
            };
            app.MapGet("/api/sessions", sessionList);
            app.MapGet("/logs", sessionList);

            app.MapGet("/logs/{session}", (HttpContext c) =>
            {
                string session = Route(c, "session");
                string folder = Path.Combine(root, session ?? string.Empty);
                if (!Names.IsSafe(session) || !Directory.Exists(folder)) return Error(c, 404, "no such session");
                if (session == hub.SessionId) hub.Log.Flush();
                JsonValue files = JsonValue.Array();
                foreach (string name in SessionLog.Files)
                {
                    var info = new FileInfo(Path.Combine(folder, name));
                    if (info.Exists) files.Add(JsonValue.Object().Set("name", name).Set("bytes", info.Length));
                }
                return Json(c, 200, JsonValue.Object().Set("session", session).Set("files", files));
            });

            app.MapGet("/logs/{session}/{file}", async (HttpContext c) =>
            {
                string session = Route(c, "session");
                string file = Route(c, "file");
                string type;
                switch (file)
                {
                    // telemetry.csv is the same file under the name the link research memo used.
                    case "telemetry.csv": file = SessionLog.PerfFile; type = "text/csv; charset=utf-8"; break;
                    case SessionLog.PerfFile: type = "text/csv; charset=utf-8"; break;
                    case SessionLog.EventsFile: type = "application/x-ndjson; charset=utf-8"; break;
                    case SessionLog.PhoneLogFile: type = "text/plain; charset=utf-8"; break;
                    case SessionLog.HubLogFile: type = "text/plain; charset=utf-8"; break;
                    case SessionLog.ManifestFile: type = "application/json; charset=utf-8"; break;
                    case SessionLog.StateFile: type = "application/octet-stream"; break;
                    case SessionLog.OutlineFile: type = "application/octet-stream"; break;
                    default: await Error(c, 404, "no such log file"); return;
                }
                string path = Path.Combine(root, session ?? string.Empty, file);
                if (!Names.IsSafe(session) || !File.Exists(path))
                {
                    await Error(c, 404, "no such session");
                    return;
                }
                if (session == hub.SessionId) hub.Log.Flush();
                // The current session's files are still being written: share them, and send what is there now.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, true))
                {
                    long length = stream.Length;
                    c.Response.StatusCode = 200;
                    c.Response.ContentType = type;
                    c.Response.ContentLength = length;
                    c.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + session + "_" + file + "\"";
                    var buffer = new byte[1 << 16];
                    long left = length;
                    while (left > 0)
                    {
                        int n = await stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, left), c.RequestAborted);
                        if (n <= 0) break;
                        await c.Response.Body.WriteAsync(buffer, 0, n, c.RequestAborted);
                        left -= n;
                    }
                }
            });
        }

        // The phone's own perf CSV, uploaded after a session (Master Plan section 12.2): the Hub adds the rows it
        // lacks, by seq, so its file is whole even when the link was down for longer than the phone's backlog.
        static void MapPerfUpload(WebApplication app, HubServer hub)
        {
            app.MapPut("/logs/{session}/{file}", async (HttpContext c) =>
            {
                string session = Route(c, "session");
                string file = Route(c, "file");
                if (file == "telemetry.csv") file = SessionLog.PerfFile;
                if (file != SessionLog.PerfFile)
                {
                    await Error(c, 404, "only perf.csv can be uploaded");
                    return;
                }
                if (!Names.IsSafe(session))
                {
                    await Error(c, 400, "a session id is 1 to 64 letters, digits, '-', '_' or '.'");
                    return;
                }

                IHttpMaxRequestBodySizeFeature limit = c.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit != null && !limit.IsReadOnly) limit.MaxRequestBodySize = MaxPerfUploadBytes;
                string text;
                try
                {
                    using (var reader = new StreamReader(c.Request.Body, Encoding.UTF8))
                        text = await reader.ReadToEndAsync();
                }
                catch (BadHttpRequestException e)
                {
                    await Error(c, e.StatusCode, "the upload is larger than " + (MaxPerfUploadBytes >> 20) + " MiB");
                    return;
                }

                int added;
                if (hub.MergePerf(session, text, out added) != HubResult.Ok)
                {
                    await Error(c, 400, "expected a perf.csv: the header line of the telemetry row, then its rows");
                    return;
                }
                c.Response.StatusCode = 204;
            });
        }

        // ---- the phone's view -------------------------------------------------------------------------

        const string ViewBoundary = "atlasframe";

        // The View page: the phone posts one eye's image as JPEG while anyone watches, and each viewer gets the
        // newest frame as an MJPEG stream an <img> can show.
        static void MapView(WebApplication app, HubServer hub)
        {
            ViewRelay view = hub.View;

            app.MapPost("/api/view/frame", async (HttpContext c) =>
            {
                byte[] jpeg;
                using (var buffer = new MemoryStream())
                {
                    try
                    {
                        await c.Request.Body.CopyToAsync(buffer, c.RequestAborted);
                    }
                    catch (BadHttpRequestException e)
                    {
                        await Error(c, e.StatusCode, "a frame is at most " + (ViewRelay.MaxFrameBytes >> 10) + " KiB");
                        return;
                    }
                    jpeg = buffer.ToArray();
                }
                if (!view.Post(jpeg, c.Request.Query["mode"]))
                {
                    await Error(c, 400, "expected one JPEG image of at most " + (ViewRelay.MaxFrameBytes >> 10) + " KiB");
                    return;
                }
                // The answer says whether anyone is still watching, so the phone stops at once when nobody is.
                await Json(c, 200, JsonValue.Object().Set("watching", view.Watching));
            });

            app.MapGet("/api/view", (HttpContext c) => Json(c, 200, view.Status()));

            app.MapGet("/api/view/latest.jpg", async (HttpContext c) =>
            {
                byte[] jpeg;
                long seq;
                if (!view.TryGetLatest(out jpeg, out seq))
                {
                    await Error(c, 404, "no frame from the phone yet");
                    return;
                }
                c.Response.ContentType = "image/jpeg";
                c.Response.ContentLength = jpeg.Length;
                c.Response.Headers["Cache-Control"] = "no-store";
                await c.Response.Body.WriteAsync(jpeg, 0, jpeg.Length, c.RequestAborted);
            });

            app.MapGet("/api/view/stream", async (HttpContext c) =>
            {
                c.Response.ContentType = "multipart/x-mixed-replace; boundary=" + ViewBoundary;
                c.Response.Headers["Cache-Control"] = "no-store";
                CancellationToken cancel = c.RequestAborted;
                using (view.Watch())
                {
                    long seq = 0;
                    while (!cancel.IsCancellationRequested)
                    {
                        await view.WaitNewer(seq, cancel);
                        byte[] jpeg;
                        if (!view.TryGetLatest(out jpeg, out seq)) continue;
                        byte[] head = Encoding.ASCII.GetBytes("--" + ViewBoundary + "\r\nContent-Type: image/jpeg\r\nContent-Length: " +
                                                              jpeg.Length + "\r\n\r\n");
                        await c.Response.Body.WriteAsync(head, 0, head.Length, cancel);
                        await c.Response.Body.WriteAsync(jpeg, 0, jpeg.Length, cancel);
                        await c.Response.Body.WriteAsync(CrLf, 0, CrLf.Length, cancel);
                        await c.Response.Body.FlushAsync(cancel);
                    }
                }
            });
        }

        static readonly byte[] CrLf = { 13, 10 };

        // ---- Forge to Hub -----------------------------------------------------------------------------

        static void MapInternal(WebApplication app, HubServer hub)
        {
            app.MapPost("/internal/job", async (HttpContext c) =>
            {
                // Loopback only (Appendix B.3): the Forge runs on the Station itself.
                IPAddress remote = c.Connection.RemoteIpAddress;
                if (remote == null || !IPAddress.IsLoopback(remote))
                {
                    await Error(c, 403, "/internal/job accepts requests from the Station only");
                    return;
                }
                JsonValue body = await ReadJson(c);
                if (body == null) return;
                if (hub.PostJob(body) != HubResult.Ok)
                {
                    await Error(c, 400, "expected a JobStatus or AssetReady body with a jobId");
                    return;
                }
                c.Response.StatusCode = 204;
            });
        }

        // ---- helpers ----------------------------------------------------------------------------------

        static int StatusOf(HubResult result)
        {
            switch (result)
            {
                case HubResult.Ok: return 200;
                case HubResult.Unknown: return 404;
                case HubResult.Locked: return 423;
                case HubResult.NoPhone: return 409;
                default: return 400;
            }
        }

        static string Route(HttpContext c, string name) { return c.Request.RouteValues[name] as string; }

        static Task Json(HttpContext c, int status, JsonValue body)
        {
            return Text(c, status, "application/json; charset=utf-8", body.ToJson());
        }

        static Task Error(HttpContext c, int status, string message)
        {
            return Json(c, status, JsonValue.Object().Set("error", message));
        }

        static Task Text(HttpContext c, int status, string contentType, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            c.Response.StatusCode = status;
            c.Response.ContentType = contentType;
            c.Response.ContentLength = bytes.Length;
            c.Response.Headers["Cache-Control"] = "no-store";
            return c.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Reads the request body as JSON. On a bad body it answers 400 itself and returns null. With
        /// <paramref name="allowEmpty"/> an empty body gives <see cref="JsonValue.Null"/>.
        /// </summary>
        static async Task<JsonValue> ReadJson(HttpContext c, bool allowEmpty = false)
        {
            string text;
            using (var reader = new StreamReader(c.Request.Body, Encoding.UTF8))
                text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                if (allowEmpty) return JsonValue.Null;
                await Error(c, 400, "the request needs a JSON body");
                return null;
            }
            JsonValue json;
            string error;
            if (AtlasJson.TryParse(text, out json, out error)) return json;
            await Error(c, 400, "bad JSON: " + error);
            return null;
        }
    }
}
