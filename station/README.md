# The Station

The PC beside the walker (Master Plan §7, Appendix C.7). Two processes run on it:

| Process | Folder | Listens on | Does |
|---|---|---|---|
| **Hub** (`atlas-hub`, .NET 8) | `station/hub` | UDP 9050, TCP 8080 | Ends the LiteNetLib link, relays the phone's `State` to Spectators, sends parameters and operator commands to the phone, writes the session logs, serves the Console and the pairing page |
| **Forge** (Python) | `station/forge` | TCP 8000 | Turns object photos into furniture meshes; see `station/forge/README.md` |

The app is complete without the Station (§7.5): with the Hub stopped only the link indicator changes.

```
station/
  hub/                 the Hub (task P1-D02)
    Hub.csproj         net8.0; compiles ATLAS/Assets/Atlas/Link/**/*.cs directly, NuGet LiteNetLib 2.1.4 and QRCoder
    Program.cs         command line, Ctrl+C
    HubServer.cs       link loop, watchdogs, relay bookkeeping, everything the routes call
    HubOptions.cs      options and default folders
    ConsoleFeed.cs     the browsers on /ws
    ForgeProbe.cs      GET <forge>/health every 2 s
    PresetStore.cs     presets/<name>.json, load, bake
    Logging/           SessionLog.cs, StateLog.cs (the state.bin layout), PerfCsv.cs (perf.csv merged by seq),
                       TelemetryJson.cs (a Telemetry row for the Console)
    Web/               HubWeb.cs (routes), PairPage.cs (QR code)
    wwwroot/           the Console's files; a minimal status page until the Console is built
  hub.tests/           NUnit tests; they start the Hub in-process on free ports
  setup_firewall.ps1   the three firewall rules
tools/ReplayPhone/     a phone with no phone
```

The Hub compiles the app's own link sources, so the phone and the Hub cannot disagree about a byte. That
includes the perf CSV: both write it through `Atlas.Link.TelemetryCsv`, so the two files are the same bytes.

## One-time set-up

1. .NET 8 SDK (`dotnet --version` reports 8.x).
2. The Station on Ethernet to its own router, DHCP reservation at `192.168.x.10`, Windows network profile
   **Private** (Appendix C.7).
3. Firewall, once, in an elevated PowerShell:

   ```
   powershell -ExecutionPolicy Bypass -File station\setup_firewall.ps1
   ```

   It adds "ATLAS Hub UDP" (UDP 9050), "ATLAS Hub HTTP" (TCP 8080) and "ATLAS Forge" (TCP 8000).
4. Put the log folder on an encrypted volume (`--logs`): motion logs are treated as health data (§13.5).

## Running the Hub

```
dotnet run --project station/hub
dotnet run --project station/hub -- --advertise 192.168.0.10 --logs D:\atlas-logs
```

It prints its addresses, the pairing URL and the session folder, then one status line a second while a phone
is connected. Ctrl+C (or a terminate signal) says `Bye` to every client and closes the logs. If no client has
connected after 10 s it reminds you of the firewall rule.

| Option | Default | Meaning |
|---|---|---|
| `--udp <port>` | 9050 | LiteNetLib listener: phone, Spectators, Editors |
| `--http <port>` | 8080 | Console, HTTP routes, WebSocket |
| `--bind <ip>` | `0.0.0.0` | Address the web server binds to |
| `--advertise <ip>` | the Station's first private IPv4 address | Address in the pairing QR and in `Welcome.forgeUrl` |
| `--logs <dir>` | `station/hub/logs` | Parent of the session folders |
| `--presets <dir>` | `station/hub/presets` | Parameter presets, one JSON file each |
| `--project <dir>` | `ATLAS` | Unity project; Bake writes `Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json` there |
| `--forge <url>` | `http://127.0.0.1:8000` | The Forge the Hub checks every 2 s |
| `--no-forge` | | Do not check the Forge |
| `--session <id>` | the phone's id; until a phone names one, the start time `yyyyMMdd-HHmmss` | A fixed session id and log folder name. Given, it is kept whatever the phone says (see "Sessions") |
| `--demo-lock` | off | Start locked: the phone ignores `ParamSet` |
| `--quiet` | off | No banner and no status line |

`--udp-port` and `--http-port` are accepted as longer spellings. The default folders are found from the
project file, so the Hub can be started from any directory; a published copy uses the current directory.

## Ports and roles

| Port | Protocol | Who connects |
|---|---|---|
| 9050 | UDP, LiteNetLib, key `atlas-v1` | The phone (role `phone`), Spectators (`spectator`), Editors (`editor`), link Consoles (`console`) |
| 8080 | HTTP and WebSocket | The Console in a browser; the pairing page; the Forge on `/internal/job` (loopback only) |
| 8000 | HTTP | The Forge (its own process); phones and Spectators talk to it directly |

- **One phone at a time.** A second phone is refused with `Bye {"reason": "another phone is already connected"}`
  and retries with back-off; it gets the seat when the first one leaves. This is the working default for the
  lead's open item 5 of the lane D contract ("a second phone"): the contract's alternative, a second phone that
  is accepted and waits while the Console switches sources, is not built. A hot swap today is: stop phone A,
  phone B connects within its next attempt (at most 4 s).
- `State` (202 bytes, 60 Hz) is relayed unchanged to every Spectator and Editor and appended to `state.bin`.
- **What a Spectator is sent** beside `State` (contract 5.8): `Welcome` with the session id, `forgeUrl` and the
  phone's `roomId` and `worldHash` (sent again when the phone arrives or changes them); the phone's
  `ParamManifest`; every `Event` of the phone, which carries each parameter change (`param`) and each `world`
  event; `RoomOutline`; `JobStatus` and `AssetReady`. A Spectator that joins late gets the kept outline, the
  manifest with the values of that moment, the newest `world` event and the assets the phone has not
  acknowledged.
- The Hub keeps the last value of every parameter. A phone that reconnects (RESYNC) gets them back.
- **Heartbeat.** The Hub pings every client: eight times 50 ms apart after its `Hello`, then four times per
  second. The answers give it a clock offset per client, from which the latency in the `state` digest is
  measured (Hub receive time minus the phone's send time on the Hub's clock).
- Watchdog per client: `connected`, `degraded` after 500 ms of silence, `offline` after 3 s, `resync` while a
  returning phone is brought back in step. The states appear in `/api/session`, on `/ws` and in `hub.log`.

**Loss and latency simulation is not built.** LiteNetLib compiles `SimulatePacketLoss` and `SimulateLatency`
only under `DEBUG` or `SIMULATE_NETWORK`, and the NuGet package the Hub uses defines neither. A `SimNet`
configuration that builds LiteNetLib 2.1.4 from source (contract 2.11, open item 10) would add `--sim-loss` and
`--sim-latency`; until it exists, run the loss tests (L9, L12) with the simulation on the Editor side, or on
the in-memory network the link tests use (`LoopbackNetwork.Loss`, `DelayMs`, `JitterMs`).

## Sessions

The phone makes the session id (`yyyyMMddTHHmmssZ-xxxx`, for example `20261004T153012Z-a3f9`) and sends it in
`Hello`. The Hub adopts it: the id becomes the name of the log folder, comes back in `Welcome`, and is what
`/api/session` reports. The phone's own files and the Hub's folder so carry one name.

- Until a phone names a session, the Hub logs under an id of its own, made from its start time. When the phone
  then arrives, the Hub's start-up notes move into the phone's folder and the empty folder is removed.
- A phone that names no session (ReplayPhone always names one) is logged under an id of the Hub's own, never
  under another phone's.
- `--session <id>` fixes the id; the phone's is then not adopted.
- A folder that already exists is continued, never overwritten. That is a Hub restarted in mid-session: the
  phone reconnects with the same id and the new Hub appends to `state.bin`, `events.jsonl`, `phone.log`,
  `hub.log` and `perf.csv` of the Hub before it. (An id of the Hub's own that is already taken gets `-2`, `-3`, …)
- A new session on the same connection (`LinkClient.SetSession`, a new walker) starts a new folder at once.

## Pairing

1. Start the Hub and open `http://<station>:8080/pair` on the Station's screen.
2. Point the iPhone Camera at the QR code. It holds `atlas://<station-ip>:9050`; opening it starts ATLAS, which
   stores the address and connects.
3. The first connect raises the iOS Local Network prompt; allow it. The app retries every second meanwhile.

The page takes `?ip=<address>` when the Station has several addresses (each is listed as a link), and shows
the address as text for the typed-IP fallback ("Change IP" in the app). The QR code is drawn by the Hub itself;
the page loads nothing from outside, because the router has no internet. `iproxy` forwards TCP only, so the
link needs the router even with the phone on a cable.

## Logs

One folder per session under the log root:

| File | Content |
|---|---|
| `state.bin` | Every `State` received. 16-byte header (`ATLSTATE`, version u16 = 1, record size u16 = 210, u32 0), then 210-byte records: Hub receive time (i64, µs, little-endian) and the 202 bytes as sent. The replay source |
| `events.jsonl` | One event per line: the phone's `{t_us, scene, ev, data}` with `hub_us` added. Hub-made events (`fms`, `note`, `asset_ack`) carry `"scene": "hub"` |
| `perf.csv` | Header, then one `Telemetry` row a second: 34 columns, floats with three decimals, `\n` line ends. In a session the phone named, a row whose `seq` is already written is skipped and the file is in `seq` order, so it is byte for byte the phone's own CSV; rows uploaded later are merged the same way |
| `phone.log` | `<t_us> <level> <msg>` per `Log` message from the phone |
| `hub.log` | `<hub_us> <kind> <json>`: start, stop, joins and leaves, link state changes, every parameter and command sent, preset actions, Forge jobs |
| `manifest.json` | The phone's last `ParamManifest` |
| `outline.bin` | The phone's last `RoomOutline` message, as received |

A restart never overwrites a session: the phone's session folder is continued (see "Sessions"), and an id of
the Hub's own that already has a folder gets `-2`, `-3`, … appended.

## HTTP and WebSocket (port 8080)

Bodies are JSON; errors are `{"error": "<text>"}`.

| Route | Answer |
|---|---|
| `GET /` | The Console (files of `wwwroot/`) |
| `GET /pair` · `GET /pair/qr.svg` · `GET /api/pair` | Pairing page · the QR code alone · `{url, address, udpPort, httpPort, candidates}` |
| `WS /ws` | The Console's live feed, below |
| `GET /api/session` | `{sessionId, phone: {state, rttMs, loss, stateHz, states, lost, device, build, address, worldHash, roomId}, sources, spectators, editors, consoles, forge: {up, queue, debugKeep, hosted}, demoLock, …}`. `loss` is the percentage of `State` lost in the last 5 s; `address` is the phone's IP address |
| `GET /api/params` | `{ver, params: [{id, name, group, min, max, def, val, onRelease, kind, active, names?}], locked}` with current values |
| `GET /api/params/{name or index}` | One entry; 404 |
| `POST` or `PUT /api/params/{name or index}` | Body `{"value": n}` → 200 `{id, name, value, sent}` with the value after the phone's clamp; 404 unknown, 423 under demo lock |
| `POST /api/params` | Body `{"values": {name: n}}` → 200 `{sent}`: all in one `ParamSet`; 409 with no phone |
| `POST /api/op` | Body `{"cmd": "home" \| "recentre" \| "quality" \| "cue" \| "lock", "args": {…}}` (`quality`: `{"tier": -1…4}`, `lock`: `{"on": bool}`; `cue` is STOP) → 200; 404 unknown, 409 with no phone |
| `POST /api/scene` | Body `{"space": "S0"…"S6", "variant": "", "args": {}}` → 200; 404, 409 |
| `GET /api/state` · `GET /api/telemetry` | The newest `State` digest · the newest `Telemetry` row; 404 before the first |
| `GET /api/presets` | `{presets: [{name, count, modified}]}` |
| `GET` · `POST` · `DELETE /api/presets/{name}` | `{name, values}` · save `{"values": {name: n}}` (an empty body saves the current values), 204 · 204 |
| `POST /api/presets/{name}/load` · `…/bake` | 204: every value in one `ParamSet` (409 with no phone) · 204: written into the Unity project's defaults |
| `GET /api/sessions` | `{current, sessions}`, newest first |
| `GET /logs/{session}` · `GET /logs/{session}/{file}` | File list · download of any file in the table above (`telemetry.csv` is an alias of `perf.csv`) |
| `PUT /logs/{session}/perf.csv` | Body: the phone's own CSV for that session, header line first, up to 32 MiB → 204. The rows the Hub lacks are merged in by `seq`; the rows it has are left alone. A session the Hub never saw gets its folder. 400 when the body does not start with the header or the session id is not a folder name |
| `POST /internal/job` | A Forge `JobStatus` or `AssetReady` body → 204, relayed to the phone and Spectators. Loopback only: 403 otherwise |

WebSocket frames are JSON objects with a member `t`.

- Console → Hub: `{"t":"param","id":3,"value":0.7}` (or `"name"` instead of `"id"`), `{"t":"scene","space":"S1","variant":"","args":{}}`,
  `{"t":"op","cmd":"cue","args":{}}`, `{"t":"fms","score":3}`, `{"t":"note","text":"…"}`. A frame the Hub cannot
  use is answered with `{"t":"error","msg":"…"}`.
- Hub → Console: `link` (on connect, on change and once a second; the body of `/api/session`), `manifest`,
  `state` (10 Hz digest: `seq, t_us, scene, space, tracking, jump, cue, tier, lock, bump, tile, sheet, head, yawDeg, p, worldVer, hz, lossPct, latencyMs: {p50, p95, p99}`),
  `telemetry` (the CSV columns), `event`, `log`, `job` (`kind`: `status`, `ready` or `ack`), `outline`.

## ReplayPhone

Plays into a Hub as the phone, so the Console and the Spectator can be developed with no phone (Appendix C.8).

```
dotnet run --project tools/ReplayPhone                                    # synthetic walk at 60 Hz, until Ctrl+C
dotnet run --project tools/ReplayPhone -- --seconds 30 --hub 192.168.0.10:9050
dotnet run --project tools/ReplayPhone -- station/hub/logs/<session> --loop
dotnet run --project tools/ReplayPhone -- recordings/state.bin --speed 2
```

- Every run names a new session (`yyyyMMddTHHmmssZ-xxxx`), so every run has a log folder of its own on the Hub.
- **Synthetic walk** (no recording given): laps of a 3 m × 4 m room at walking speed. It declares a few real
  parameters (`curved.cullRho`, `render.scale`, `render.msaa`, `glued.copies`, `pose.gyroPredict`,
  `link.stateHz`), applies what the Console sets, answers the six operator commands and sends `Telemetry`
  once a second.
- **Recording** (a session folder or its `state.bin`): each `State` at its original spacing (divided by
  `--speed`), with only `seq` and `t_us` rewritten. From the same folder it sends `manifest.json`,
  `outline.bin` and the `world` events of `events.jsonl` when they exist. Parameters and commands from the
  Console are logged and ignored: a recorded walk cannot change.

Options: `--hub <ip[:port]>`, `--port`, `--seconds`, `--loop`, `--speed`, `--hz`, `--no-telemetry`, `--quiet`.
Exit code 0 when done, 1 when no Hub answered within 10 s, 2 for a bad recording.

## Tests

```
dotnet build station/hub
dotnet test station/hub.tests
```

The tests start the Hub in-process on ports the system picks and talk to it with real `LinkClient`s over UDP
on `127.0.0.1`, with `HttpClient` and with a WebSocket: 60 Hz relay with no loss, every route, parameters and
the six commands end to end, a Hub restart, ReplayPhone in both modes, the session the phone names and a Hub
restarted in the middle of it, the perf CSV de-duplicated and merged byte for byte, and everything a Spectator
is sent. The watchdog, latency and zero-allocation tests run the same Hub on the in-memory network with a
manual clock. On a machine without ICU set
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
