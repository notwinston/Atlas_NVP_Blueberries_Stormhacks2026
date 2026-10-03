# R4: Phone ↔ PC link for ATLAS Phase 1

Research memo, 3 Oct 2026. Scope: the fastest reliable way to have the iPhone app connected to the PC by the end of Phase 1, and what the link must carry.

Tags: **[V]** = verified, fetched primary source · **[S]** = secondary / prior knowledge, not re-fetched · **[D]** = my derivation · **[E]** = estimate. URLs are in §12.

---

## 0. Recommendation in one page

1. **Keep LiteNetLib (Build Pipeline wins) and make both ends .NET.** LiteNetLib has its own handshake and packet framing, so a plain Python UDP socket cannot talk to it. [V]
2. **Terminate the link in a small .NET 8 console "hub" on the Windows PC** (LiteNetLib from NuGet + Kestrel for one web page). The phone, the Unity spectator and any Unity Editor are all LiteNetLib clients of that hub. Python stays, but only behind HTTP (assets, GPU jobs), never on the pose path. [D]
3. **Write the link once as a plain C# library** (`Atlas.Link`, no `UnityEngine` references). The same `LinkHost` class runs inside the console hub and inside a Unity "host scene", so the first connection can be made in about an hour with Unity as the host, before the hub exists. [D]
4. **Serialisation: hand-packed binary for 60 Hz pose/state, JSON for low-rate control.** No MessagePack or MemoryPack in Phase 1. [D]
5. **The spectator re-renders from state, not video:** scene id + tile id + a double-precision Lorentz transform + parameters, about 190 bytes per packet. [D]
6. **Remote parameter tuning and device perf telemetry ride the same link** and are the biggest iteration-speed win for the three people without the Mac. [D]
7. **Own router is mandatory.** iOS still shows the Local Network prompt for unicast UDP; trigger it on the onboarding screen with a retry loop. [V]

Spec items this replaces: WebSocket control + raw UDP pose + Python FastAPI link server + MessagePack string-keyed maps (Spec §10.1, §10.3, Appendix B). Spec items kept: clock-sync maths, watchdog states, "phone is primary" (§3.4), HTTP for assets, state-replicated spectator (§10.2).

---

## 1. LiteNetLib facts (question A)

| Fact | Detail | Tag |
|---|---|---|
| Latest release | **2.1.4**, published 19 May 2026 per the GitHub releases API and the OpenUPM registry (`latest: 2.1.4`). Repo last pushed 28 Sep 2026. | [V] |
| Licence | MIT | [V] |
| Target | .NET Standard 2.1. README: "Unity 2021.2 (Desktop platforms, Android, iOS, Switch)". Unity 6 is not named explicitly but is above the minimum. | [V] |
| Unity install | README: "Always use library sources or OpenUPM package instead of precompiled DLL files (because there are platform specific #ifdefs and workarounds for unity bugs)". Package id `com.revenantx.litenetlib`. | [V] |
| PC install | NuGet `LiteNetLib` | [V] |
| Delivery methods | `Unreliable`, `Sequenced` (unreliable, drops old and duplicate), `ReliableUnordered`, `ReliableOrdered`, `ReliableSequenced` (only the last packet is reliable) | [V] |
| Overhead | 1 byte for unreliable, 4 bytes for channelled (reliable and sequenced) packets; fragment header 6 bytes more | [V] |
| MTU | Starts at 1024 bytes; `MtuDiscovery` is off by default. Largest candidate is 1500 − 68. | [V] |
| Built-in ping | Ping every `PingInterval` (default 1000 ms). Exposes `Ping`, `RoundTripTime` (integer ms, averaged) and `RemoteTimeDelta`. | [V] |
| Built-in timeout | `DisconnectTimeout` default 5000 ms; `MaxConnectAttempts` 10 at `ReconnectDelay` 500 ms | [V] |
| Threads | Runs its own logic thread (`UpdateTime` 15 ms) unless started in manual mode. `PollEvents()` delivers events on the caller's thread. `TriggerUpdate()` forces a flush. | [V] |
| Statistics | `EnableStatistics` gives packets/bytes sent and received and a packet-loss counter | [V] |
| Loss/latency simulation | `SimulatePacketLoss`, `SimulateLatency` (+ chance, min/max latency). Source guards these with `DEBUG || SIMULATE_NETWORK`. | [V] |
| iOS backgrounding | `PausedSocketFix` hooks `Application.focusChanged` and restarts the socket when focus returns and the socket is dead. It does not restore the peer connection. | [V] |
| NTP helper | `CreateNtpRequest(address)` is an SNTP **client for a real NTP server** (default port 123), answered through `OnNtpResponse`. It is not a peer-to-peer time sync. | [V] |
| Discovery | Broadcast/multicast based ("Multicasting (for discovering hosts in local network)"). Unusable on iOS without the multicast entitlement. | [V] + [V, Apple] |

### Wire protocol: not interoperable with a plain socket
Read from `NetConstants.cs`, `NetPacket.cs`, `InternalPackets.cs`, `LiteNetPeer.cs` on master. [V]
- Byte 0 of every datagram is a `PacketProperty` (Unreliable, Channeled, Ack, Ping, Pong, ConnectRequest, ConnectAccept, Disconnect, MtuCheck, Merged, …) plus connection-number and fragment bits.
- Connecting needs a `ConnectRequest` (18-byte header: `ProtocolId = 13`, connection time, peer id, target address) answered by a 15-byte `ConnectAccept`.
- Data from an unknown endpoint is rejected (`PeerNotFound`); reliable channels need the library's ack windows; small packets are merged.
- Consequence: a Python process would have to re-implement the handshake, ping/pong, acks and merging. I found no maintained Python port. **Both ends must be LiteNetLib on .NET.** [V for the protocol; the "no Python port" part is in "Could not verify"]

### Two details that affect our design
- **`RemoteTimeDelta` uses `DateTime.UtcNow` and an integer-millisecond RTT.** Good to about ±1–2 ms, but wall clocks can step. We run our own monotonic ping (§5.4) and use the built-in RTT only for the HUD. [V for the mechanism, D for the conclusion]
- **Sends are queued and flushed by the logic thread every 15 ms** unless you call `TriggerUpdate()`. Call it once after the pose send, or set `UpdateTime = 5`. [V for the fields; the exact queueing of `Unreliable` sends was not traced end to end, see "Could not verify"]

### NetSerializer / NetPacketProcessor
- Uses `GetProperties` reflection and `Delegate.CreateDelegate` over generic types, and prefixes each packet with a 64-bit FNV hash of the type name (8 bytes). [V]
- Reflection over generics is the classic IL2CPP stripping risk. The source carries trimming attributes, but I did not test it on Unity 6 IL2CPP. **Do not use it. Use `NetDataWriter` / `NetDataReader` by hand.** [D]

---

## 2. Which PC process terminates the link (question A, decision)

| Option | Dev time [E] | Risk | Latency | Debuggability | Fits Build Pipeline |
|---|---|---|---|---|---|
| **A. Unity (Editor or Windows spectator build) hosts** | 1–1.5 h | Editor domain reload kills the socket on every script edit; the link dies whenever the spectator is restarted | Same | Unity console only | Yes |
| **B. .NET 8 console hub hosts; phone, spectator, Editors are clients** | 3–4 h | Low. One always-on process, restartable independently of Unity | Same (one extra LAN hop for the spectator, under 1 ms) | Plain log files, web page, can replay logs | Yes |
| C. Drop LiteNetLib: raw UDP + WebSocket to Python (the Spec's design) | 4–6 h | Two sockets, own sequencing, own ping, own reconnect; ATS and the prompt apply to both | Same | Python is pleasant | **No** |
| D. LiteNetLib to a .NET relay that forwards to Python FastAPI | 5–7 h | Two servers to keep alive | +1 localhost hop | Split across two processes | Yes, but more moving parts |

**Recommendation: B, reached through A.**
- Hour 0–1.5: `LinkHost` runs inside a Unity scene on the PC. This proves the phone connects (the Phase-1 exit criterion) with the least code.
- Hour 1.5–4: wrap the same `LinkHost` in the console hub and add the web page. Unity spectator becomes a client.
- Python (FastAPI) keeps the jobs the Spec gave it that are not link jobs: asset generation, GPU workers, case builds. The phone fetches assets from it over HTTP. If Python ever needs live pose, the hub forwards JSON to it over a localhost WebSocket (later phase).

Why not C, on the merits and not only on priority: the reliable control channel, RTT, loss counter, disconnect detection and loss simulation all come free with LiteNetLib and would each be hand-built in C. [D]

---

## 3. iOS specifics (question B)

### 3.1 Local Network privacy (Apple TN3179)
- Local network = any broadcast-capable interface: "Such interfaces include Wi-Fi and Ethernet, but not cellular (WWAN) or VPN." [V]
- Needs permission: outgoing TCP, **sending a UDP unicast**, connecting a UDP socket, all multicast/broadcast, resolving `.local` names, all Bonjour. [V]
- Does not need permission: listening for and accepting incoming TCP; receiving an incoming UDP unicast. [V]
- So the Spec's trick (phone listens on TCP, no prompt) does not carry over to UDP: the phone can receive a datagram without permission but cannot answer it. **With LiteNetLib the prompt is unavoidable.** [D]
- The checks sit "deep in the networking stack, and thus they apply to all networking APIs", including BSD sockets, so they apply to Unity's managed sockets. [V]
- "it may deny the operation immediately, before the user has responded to the alert … add appropriate retry logic." [V]
- If the app is in the background while the state is undetermined, the operation is denied with no alert and nothing is recorded. [V]
- "The simulator doesn't support local network privacy. Test … on a real device." [V]
- MDM cannot pre-grant it. [V]
- Info.plist: `NSLocalNetworkUsageDescription`; `NSBonjourServices` only if we use Bonjour. [V]

### 3.2 Why unicast
- "To send or receive multicast or broadcast traffic, sign your app with the `com.apple.developer.networking.multicast` entitlement." Unicast send and receive are listed as not requiring it. [V]
- The entitlement "requires permission from Apple before you can use it". Not obtainable in a weekend. [V]
- So: no LiteNetLib broadcast discovery, no "browse all Bonjour types". Unicast to a known IP only. [D]

### 3.3 Triggering the prompt deterministically
There is no API to request or query the permission; the only trigger is a real local-network operation in the foreground. [S]

Onboarding step, phone in hand, before the headset goes on [D]:
1. Screen: "ATLAS needs to talk to the operator PC. Tap Allow on the next prompt."
2. On "Continue": `client.Connect(ip, 9050, key)`. That unicast send raises the alert.
3. Retry every 1 s for up to 60 s (LiteNetLib gives up after 10 × 500 ms, so loop on `OnPeerDisconnected`).
4. Connected → green tick. Not connected after 15 s → show "Settings → Privacy & Security → Local Network → ATLAS" and a "Skip, run offline" button.
5. The decision persists across launches. It resets if the app is deleted. Do this once per phone (A and B) the night before the demo.

### 3.4 ATS and cleartext to a LAN IP
- "In iOS 17 … ATS no longer allows connections to IP addresses by default." `NSAllowsLocalNetworking` re-enables "unqualified domains, `.local` domains, and IP addresses". [V]
- ATS governs the URL loading system (`UnityWebRequest` → NSURLSession). Raw sockets, and therefore LiteNetLib, are not subject to it. [S]
- Action: set `NSAllowsLocalNetworking = YES` in a post-build script for the HTTP asset path. Do not rely on Unity's "Allow downloads over HTTP" alone. [D]

### 3.5 Backgrounding
- iOS suspends the app and reclaims the UDP socket. LiteNetLib's `PausedSocketFix` restarts the socket on focus; the hub will usually have timed the peer out by then. [V + S]
- Policy: on `OnApplicationPause(false)`, if no peer is connected, call `Connect` again and re-send `Hello`. Set `Screen.sleepTimeout = SleepTimeout.NeverSleep`. Set `AllowPeerAddressChange = true` on the hub. [D]

### 3.6 Wi-Fi versus USB
| Path | Carries UDP? | Notes | Tag |
|---|---|---|---|
| Own router, PC wired, phone on 5 GHz | Yes | The plan of record. iPhone 15 Pro Max supports Wi-Fi 6E, so 6 GHz is possible if the router has it. | [S] |
| Venue Wi-Fi | Assume no | Client isolation is common at events. Do not plan on it. | [S] |
| `iproxy` / usbmuxd | **No, TCP only** | "iproxy … Bind local TCP port". LiteNetLib cannot use it. | [V] |
| USB-C Ethernet adapter on the iPhone 15 → router | Yes | iPhone 15 supports USB Ethernet adapters; an Ethernet menu appears in Settings. Still a "local network", so the prompt still applies. Occupies the port, so no charging without a hub. | [S] |
| iPhone Personal Hotspot (Wi-Fi or USB), PC joins it | Yes | Emergency fallback. Needs a cellular plan with hotspot. | [S] |
| Windows Mobile Hotspot on the PC | Yes | Fallback with no router, if the PC has a Wi-Fi card. | [S] |

Recommendation: own router as primary; Windows Mobile Hotspot as the rehearsed fallback; a USB-C Ethernet adapter (about $20) as the stretch for a radio-hostile venue. Keep the Spec's rules: fixed non-DFS channel, AirDrop and Handoff off. [D]

### 3.7 Pairing UX
| Method | Cost [E] | Verdict |
|---|---|---|
| Router DHCP reservation, PC always `192.168.x.10:9050`, baked in as default and saved in `PlayerPrefs` | 10 min | **Minimal. Zero interaction on demo day.** |
| Typed IP field on the onboarding screen | 20 min | **Minimal.** Fallback when the network changes |
| Hub prints a QR of `atlas://<ip>:9050`; scan with the **iOS Camera app**; Unity reads `Application.deepLinkActivated` | 30–45 min | **Target.** No in-app scanner, no ARKit camera contention |
| In-app QR scan (ARKit CPU image + ZXing.Net) | 1.5 h | Skip |
| Bonjour | 2 h+ and a native plugin | Skip |
| LiteNetLib broadcast discovery | n/a | Blocked by the entitlement |

### 3.8 Windows side gotchas [S]
- Add an inbound firewall rule and set the network to Private:
  `netsh advfirewall firewall add rule name="ATLAS hub" dir=in action=allow protocol=UDP localport=9050`
  and the same for TCP 8080 (web page and assets).
- Bind Kestrel to `0.0.0.0`, not `localhost`.

---

## 4. Serialisation on IL2CPP (question C)

| Option | IL2CPP / AOT | Cross-language | Cost to adopt | Verdict |
|---|---|---|---|---|
| Hand-packed with `NetDataWriter` / `NetDataReader` | No reflection, no generics. Safe. | Trivial to parse anywhere (fixed layout) | 0 | **Use for pose/state, clock, telemetry, params** |
| `INetSerializable` structs (same thing with an interface) | Safe when called directly, not through `NetSerializer` | Same | 0 | Fine as a style choice |
| LiteNetLib `NetSerializer` / `NetPacketProcessor` | Reflection + `CreateDelegate` over generics; 8-byte hash per packet [V] | .NET only | Low | **Avoid** |
| MessagePack-CSharp v3 | Source generator, "AOT Safe"; on Unity non-Mono targets "AOT is required". Install = NuGetForUnity + `MessagePack.Unity` git package. String keys about 3× slower to read than int keys (218 vs 73 ns in their benchmark). [V] | Yes (Python, JS) | 1–2 h including the first IL2CPP build surprise | Defer. Use only if later blades need rich nested messages read by Python |
| MemoryPack | Source generator, IL2CPP supported, Unity ≥ 2022.3.12f1; cross-language is TypeScript only [V] | No Python | 1 h | Not needed |
| JSON (`JsonUtility` on the phone, `System.Text.Json` on the hub) | Safe | Everything, and the browser reads it natively | 0 | **Use for low-rate control and events (≤ 10 Hz)** |

Per channel [D]:
- **Pose/state 60 Hz, clock, telemetry, parameter set:** hand-packed little-endian binary. Zero allocation (reuse one `NetDataWriter`).
- **Hello, manifests, scene commands, operator commands, events, logs:** one type byte + a UTF-8 JSON string. Debuggable in the hub log and forwarded to the web page unchanged.
- **Assets and room meshes:** HTTP.

---

## 5. Link spec "v1-lite" (question D)

### 5.1 Topology
```
 iPhone (Unity, IL2CPP)          Windows PC                         any laptop
 LinkClient role=phone  --UDP--> atlas-hub (.NET 8)  <--UDP-- Unity spectator / Editor (role=spectator|editor)
                                   | Kestrel :8080  <--WS/HTTP-- browser: tuner + perf dashboard
                                   | logs/*.jsonl, logs/*.bin
 UnityWebRequest ------HTTP-----> asset server (hub static files now; Python FastAPI later)
```
The hub relays: phone → spectators (state), console/editor → phone (params, commands). It is the only listener, on UDP 9050.

### 5.2 Channels and rates
| Ch | Delivery | Direction | Messages | Rate | Size [E] |
|---|---|---|---|---|---|
| 0 | ReliableOrdered | both | `Hello`, `Welcome`, `ParamManifest`, `ParamSet`, `SceneCmd`, `OperatorCmd`, `Event`, `RoomOutline`, `Log`, `Bye` | on demand | < 1 kB each |
| 1 | Sequenced | phone → hub → spectators | `State` | 60 Hz target, 30 Hz minimal | about 190 B |
| 1 | Sequenced | phone → hub | `Telemetry` | 2 Hz | about 64 B |
| – | Unreliable | both | `TPing` / `TPong` | 8 at connect, then 1 Hz | 13 / 29 B |
| HTTP | TCP | phone ← PC, phone → PC | assets by hash; room-mesh upload (stretch) | on demand | MBs |

Bandwidth: 190 B × 60 Hz ≈ 11 kB/s. Every packet fits in one datagram below the 1024-byte initial MTU, so nothing fragments. [D]

Needs `NetManager` (multi-channel) with `ChannelsCount = 2`. If the team prefers the single-channel `LiteNetManager`, put everything on one channel: `Sequenced` and `ReliableOrdered` already use separate internal queues. [V for the classes, D for the choice]

### 5.3 Message schema v1-lite
All little-endian. First byte is `msgType`. Poses in Unity world space (left-handed, Y up, metres), quaternions `[x,y,z,w]`, as in the Spec.

**`State` (0x10): everything the spectator needs to draw the judge's view**
| Field | Type | Meaning |
|---|---|---|
| `msgType` | u8 | 0x10 |
| `flags` | u8 | bits 0–1 tracking (normal, limited, lost); bit 2 pose-jump rejected this frame; bit 3 safety override active |
| `sceneId` | u16 | which scene / blade |
| `seq` | u32 | increments per send |
| `t_us` | i64 | phone monotonic microseconds (`Stopwatch`) |
| `headPos` | 3 × f32 | physical head position in the ARKit session frame |
| `headRot` | 4 × f32 | physical head rotation |
| `geom` | u8 | 0 Euclid, 1 H²×E, 2 S², 3 glued torus, 4 glued Klein, 5 portal rooms, 6 4D slice |
| `tileId` | i32 | current tile (H²), cell index (glued), or room id (portal) |
| `sheet` | i16 | portal sheet / wrap count |
| `xform` | 9 × f64 | **the double-precision Lorentz matrix C, relative to the centre of `tileId`** (identity when unused) |
| `p` | 8 × f32 | per-geometry parameters: `[0]` L (curvature scale), `[1]` fog distance, `[2]` tiling q, `[3]` 4D slice w, `[4..7]` 4D rotation angles or spare |
| `worldVer` | u16 | bumps whenever tiles or rooms are regenerated |

Size: 2 + 2 + 4 + 8 + 12 + 16 + 1 + 4 + 2 + 72 + 32 + 2 = 157 B payload, about 190 B on the wire with LiteNetLib, UDP and IP headers.

Why this is enough, and the rules that make it deterministic [D]:
- The Build Pipeline already addresses hyperbolic space **by tile id with a double-precision transform on the CPU, re-anchored at the camera before casting to float**. The spectator runs the same re-anchor and the same shader, so sending (tile id, C) reproduces the view without sending video and without accumulating its own drift.
- Send the full 3×3 doubles, not 3 parameters, so the spectator never re-integrates boosts. ARM and x64 then differ only in the final float cast, which is invisible.
- **The tile id must be combinatorial** (a path in the tiling's group, or an index into a table generated by integer rules), never the order in which a float-based generator happened to emit tiles. Both sides exchange a `worldHash` in `Hello`/`Welcome`; on mismatch the spectator shows "world mismatch" instead of a wrong picture.
- H³ or other 4×4 cases (stretch): add a `StateH3` message with 16 × f64. Do not widen `State`.

**Other messages**
| Type | Id | Encoding | Fields |
|---|---|---|---|
| `Hello` | 0x01 | JSON | `proto:[1,0]`, `role` (`phone`, `spectator`, `editor`, `console`), `device`, `app`, `worldHash`, `build` (git short hash) |
| `Welcome` | 0x02 | JSON | `proto`, `sessionId`, `worldHash`, `demoLock` |
| `TPing` | 0x03 | binary | `seq u32`, `t1_us i64` |
| `TPong` | 0x04 | binary | `seq u32`, `t1_us i64`, `t2_us i64`, `t3_us i64` |
| `ParamManifest` | 0x20 | JSON | list of `{id, name, group, min, max, def, val}` |
| `ParamSet` | 0x21 | binary | `count u8`, then `count × {id u16, value f32}` |
| `SceneCmd` | 0x22 | JSON | `{scene, args}` |
| `OperatorCmd` | 0x23 | JSON | `{cmd: panic|recenter|reanchor|thermal_tier|lock, args}` |
| `Telemetry` | 0x30 | binary | `t_us i64`, `fps f32`, `frameMsP50 f32`, `frameMsP95 f32`, `frameMsMax f32`, `cpuMainMs f32`, `gpuMs f32`, `thermal u8` (0–3), `battery u8` (%), `tracking u8`, `renderScale f32`, `rttMs u16`, `lossPermille u16`, `drawCalls u16`, `trisK u16` |
| `Event` | 0x40 | JSON | `{t_us, scene, ev, data}` |
| `RoomOutline` | 0x41 | binary | `count u8`, `count × {x f32, z f32}` floor polygon + `height f32` (the scanned-room boundary, so the spectator can draw it) |
| `Log` | 0x42 | JSON | `{t_us, level, msg}` (warnings and errors only) |
| `Bye` | 0x7F | JSON | `{reason}` |

Compatibility rule: a receiver ignores unknown `msgType` values and trailing bytes; `proto` major mismatch → hub refuses with a readable reason.

### 5.4 Clock sync
- Method: the Spec's four-timestamp exchange, kept. `offset = ((t2 − t1) + (t3 − t4)) / 2`, `rtt = (t4 − t1) − (t3 − t2)`.
- Clocks: `Stopwatch.GetTimestamp()` on both sides converted to microseconds (monotonic; no wall-clock steps).
- Schedule: 8 exchanges 50 ms apart at connect, then 1 Hz. Keep the sample with the minimum RTT in a sliding window of 16; low-pass the offset (α = 0.1).
- Target: offset error ≤ 2 ms p95 on our own access point. That is far tighter than the 16.7 ms frame, and enough for spectator interpolation and log alignment. [E]
- LiteNetLib's own `RoundTripTime` feeds the HUD; its NTP helper is not used (it needs a real NTP server). [V]

### 5.5 Heartbeat, watchdog, reconnect
LiteNetLib settings: `PingInterval = 500`, `DisconnectTimeout = 3000`, `UpdateTime = 5`, `AutoRecycle = true`, `EnableStatistics = true`, `UnconnectedMessagesEnabled = false`, `BroadcastReceiveEnabled = false`, `IPv6Enabled = false` (one socket fewer; our LAN is IPv4). [D]

Phone states (the Spec's watchdog, re-expressed):
| State | Condition | Behaviour |
|---|---|---|
| CONNECTED | any packet from the hub in the last 500 ms | normal |
| DEGRADED | 500 ms–3 s silent (enter after 2 missed pings, leave after 2 s healthy) | ignore remote parameter changes; HUD amber |
| OFFLINE | peer disconnected | everything keeps running locally; reconnect with backoff 0.5, 1, 2, 4 s (cap 4 s); HUD grey |
| RESYNC | just reconnected | `Hello` → `Welcome` → 8 pings → `ParamManifest`; hub replays the last parameter snapshot |

No message needs an explicit heartbeat: `State` at 60 Hz one way and LiteNetLib pings the other way serve as one.

### 5.6 "Safety never touches the network"
Hard rules for the plan [D], consistent with Spec §3.4:
- Tracking, holonomy, rendering, the scanned-room boundary, the stop/passthrough response and pose-jump rejection run only on the phone and never read link state.
- No code path awaits the network inside `Update`, `LateUpdate` or a render callback.
- The operator's `panic` is an extra way to trigger the local safety response, never the only way.
- Remote parameters are clamped on the phone to each parameter's `[min, max]`; safety distances are not in the registry.
- `demoLock = true` (set at build time or by `OperatorCmd lock`) makes the phone ignore `ParamSet`.
- With the hub unplugged the app must behave identically apart from the HUD dot.

### 5.7 On-phone link HUD
- **In hand (onboarding and debug overlay):** state dot (green/amber/grey), hub `ip:port`, RTT ms, loss %, state send rate, clock offset ± jitter, last error, "Reconnect" and "Change IP" buttons.
- **In headset:** one small dot at the lower edge of both eyes; hidden while CONNECTED in demo mode. Drawn with the scene's own unlit shader; updated at 2 Hz; no layout rebuilds.

### 5.8 Sequences
**First connect (with the iOS prompt)**
```
User            Phone                               iOS                Hub
 |  Continue  -> |                                   |                  |
 |               | Start(); Connect(ip,9050,key) --> | alert shown      |
 |               | (request dropped while alert up)  |                  |
 |  Allow ----------------------------------------> |                  |
 |               | retry Connect every 1 s  ------------------------->  | OnConnectionRequest: key ok -> Accept
 |               | <-------------------------------------------------- | ConnectAccept
 |               | Hello{role:phone,proto,worldHash} ---------------->  |
 |               | <-------------------------------------------------- | Welcome{sessionId,demoLock}
 |               | TPing x8 (50 ms apart) <-------------------------->  | TPong
 |               | ParamManifest ------------------------------------>  | -> web page builds sliders
 |  green tick   | State @60 Hz, Telemetry @2 Hz -------------------->  | -> spectators, logs
```
**Live tuning**
```
Browser slider -> WS {id,value} -> Hub -> ParamSet (reliable) -> Phone: clamp, apply via Shader.SetGlobalFloat / setter
Phone -> next State.p[] and Telemetry reflect it -> Hub -> browser shows the applied value
"Save preset" -> Hub writes presets/<name>.json ; "Load" replays it as one ParamSet
```
**Loss and reconnect**
```
Wi-Fi drop -> Phone: no packets 500 ms -> DEGRADED -> 3 s -> OnPeerDisconnected -> OFFLINE (app unaffected)
           -> Connect() at 0.5,1,2,4,4,... s -> ConnectAccept -> RESYNC (Hello, 8 pings, manifest) -> CONNECTED
Hub: on phone Hello with a known device id -> replays last ParamSet snapshot, tells spectators "phone back"
```
**App backgrounded**
```
Home button -> iOS suspends, socket reclaimed -> Hub times the peer out after 3 s
Foreground  -> PausedSocketFix restarts socket -> our OnApplicationPause(false): no peer -> Connect() -> RESYNC
```

### 5.9 C# sketches
Shared library `Atlas.Link` (asmdef with **No Engine References**, also compiled into the hub as linked source files).

```csharp
// --- Phone side --------------------------------------------------------------
public sealed class LinkClient : INetEventListener {
    NetManager _net; NetPeer _hub; readonly NetDataWriter _w = new NetDataWriter();
    public LinkState State { get; private set; } = LinkState.Offline;
    string _ip; int _port; float _nextTry, _backoff = 0.5f; uint _seq;
    readonly ClockSync _clock = new ClockSync();

    public void Start(string ip, int port) {
        _ip = ip; _port = port;
        _net = new NetManager(this) {
            ChannelsCount = 2, PingInterval = 500, DisconnectTimeout = 3000, UpdateTime = 5,
            AutoRecycle = true, EnableStatistics = true, IPv6Enabled = false };
        _net.Start();                    // ephemeral local port
        TryConnect();
    }
    void TryConnect() { _net.Connect(_ip, _port, "atlas-v1"); State = LinkState.Connecting; }

    // Called from one MonoBehaviour.Update. Never blocks: sockets live on LiteNetLib's threads.
    public void Tick(float now) {
        _net.PollEvents();
        if (_hub == null && now >= _nextTry) { TryConnect(); _nextTry = now + _backoff; _backoff = Math.Min(_backoff * 2, 4f); }
        _clock.Tick(now, _hub, _w);      // 8 fast pings, then 1 Hz
        // watchdog: CONNECTED / DEGRADED from _hub.TimeSinceLastPacket
    }

    // Called once per frame from LateUpdate, after holonomy has run (execution order > 100).
    public void SendState(in StateMsg s) {
        if (_hub == null) return;        // offline: do nothing, cost ~0
        _w.Reset(); _w.Put((byte)0x10); _w.Put(s.flags); _w.Put(s.sceneId); _w.Put(_seq++);
        _w.Put(Clock.NowUs()); Put(_w, s.headPos); Put(_w, s.headRot);
        _w.Put(s.geom); _w.Put(s.tileId); _w.Put(s.sheet);
        for (int i = 0; i < 9; i++) _w.Put(s.xform[i]);   // doubles
        for (int i = 0; i < 8; i++) _w.Put(s.p[i]);
        _w.Put(s.worldVer);
        _hub.Send(_w, 1, DeliveryMethod.Sequenced);
        _net.TriggerUpdate();            // flush now instead of waiting for the logic tick
    }

    public void OnPeerConnected(NetPeer p) { _hub = p; _backoff = 0.5f; SendHello(); _clock.Restart(); State = LinkState.Resync; }
    public void OnPeerDisconnected(NetPeer p, DisconnectInfo i) { _hub = null; State = LinkState.Offline; }
    public void OnNetworkReceive(NetPeer p, NetPacketReader r, byte ch, DeliveryMethod m) {
        switch (r.GetByte()) {
            case 0x04: _clock.OnPong(r); break;
            case 0x21: if (!DemoLock && State == LinkState.Connected) ParamRegistry.Apply(r); break;
            case 0x22: case 0x23: Commands.Enqueue(r.GetString()); break;   // handled next frame, main thread
        }
    }
    public void OnConnectionRequest(ConnectionRequest r) => r.Reject();    // the phone never accepts
    /* remaining interface members: empty */
}

// --- Parameter registry (phone and Editor) ------------------------------------
public static class ParamRegistry {
    public sealed class P { public ushort id; public string name, group; public float min, max, def, val; public Action<float> apply; }
    static readonly List<P> _all = new List<P>();
    public static P Shader(string name, string group, float min, float max, float def) =>
        Add(name, group, min, max, def, v => UnityEngine.Shader.SetGlobalFloat(name, v));   // lives in the Unity-side partial
    public static void Apply(NetDataReader r) {
        int n = r.GetByte();
        for (int i = 0; i < n; i++) { var p = _all[r.GetUShort()]; p.val = Math.Clamp(r.GetFloat(), p.min, p.max); p.apply(p.val); }
    }
}
// Registration at boot, one line per knob:
//   ParamRegistry.Shader("_HypL", "Hyperbolic", 0.5f, 6f, 2f);
//   ParamRegistry.Shader("_FogDist", "Look", 2f, 12f, 6f);
//   ParamRegistry.Add("tiling.q", "Hyperbolic", 5, 8, 5, v => Tiling.SetQ((int)v));   // triggers regeneration + worldVer++
//   ParamRegistry.Add("slice.w", "4D", -1.5f, 1.5f, 0f, v => Slicer.W = v);

// --- Hub (.NET 8 console) ------------------------------------------------------
var host = new LinkHost(port: 9050, key: "atlas-v1");      // NetManager.Start(9050); AllowPeerAddressChange = true
host.OnState      = (peer, bytes) => { host.RelayToRole(Role.Spectator, bytes, 1, DeliveryMethod.Sequenced); binLog.Write(bytes); };
host.OnTelemetry  = t => { jsonl.Write(t); web.Broadcast(t); };
host.OnManifest   = m => web.SetManifest(m);
web.OnParam       = (id, v) => host.SendToRole(Role.Phone, ParamSet(id, v), 0, DeliveryMethod.ReliableOrdered);
while (true) { host.Poll(); Thread.Sleep(1); }              // Kestrel runs on its own threads
```

### 5.10 Acceptance tests (Phase 1 exit)
Run on our own access point, phone in the headset, hub on the Windows PC.

| # | Test | Pass |
|---|---|---|
| L1 | Cold connect: app launch (permission already granted) to CONNECTED | ≤ 3 s p95 over 10 launches |
| L2 | First-ever connect including the iOS prompt | ≤ 5 s after tapping Allow; no app restart needed |
| L3 | State rate received at the hub while walking for 5 min | ≥ 58 Hz mean at 60 fps (≥ 29 Hz in the 30 fps thermal tier) |
| L4 | Pose latency, phone `t_us` → hub receive (clock-corrected) | p50 ≤ 10 ms, p95 ≤ 25 ms, p99 ≤ 50 ms |
| L5 | Photon-to-spectator: slow-motion video of phone and monitor | ≤ 100 ms |
| L6 | Clock offset stability over 10 min | jitter ≤ 2 ms p95 |
| L7 | Packet loss: `SimulatePacketLoss` at 10 % | spectator stays smooth; a `ParamSet` still lands in ≤ 250 ms |
| L8 | Latency: `SimulateLatency` 30–100 ms | no change in phone frame time; HUD shows the RTT |
| L9 | Reconnect: router unplugged 10 s, then restored | OFFLINE within 3.5 s; CONNECTED again ≤ 5 s after Wi-Fi returns; parameters restored |
| L10 | Background 10 s, then foreground | CONNECTED ≤ 3 s |
| L11 | Hub killed and restarted | same as L9, no phone restart |
| L12 | **Zero effect on frame time:** 5 min with link on vs link off (hub unplugged) | main-thread p95 differs by ≤ 0.2 ms; link code ≤ 0.1 ms per frame in the Profiler; **0 B GC allocation per frame** from `Atlas.Link` |
| L13 | **Safety independence:** pull the PC's cable while walking toward the scanned boundary | boundary warning and stop behave identically; no hitch |
| L14 | Spectator correctness: walk a closed loop in H²×E | spectator `tileId` equals the phone's at every sample; view matches the phone's screen recording by eye |
| L15 | Remote tune: drag L, fog, q, w | visible on the phone ≤ 100 ms after the slider moves; value clamped at limits; ignored when `demoLock` |
| L16 | Telemetry: 15-minute soak | thermal state and frame-time p95 plotted on the PC with no gaps > 2 s; log file written |

Loss and latency simulation need a build where LiteNetLib's `DEBUG` or `SIMULATE_NETWORK` symbol is defined; run L7 and L8 in the Editor or a Development Build, or enable the simulation on the hub side only. [V for the guard, D for the procedure]

---

## 6. PC side in about 4 person-hours (question E)

### 6.1 Order of work
| Step | What | Time [E] | Result |
|---|---|---|---|
| 1 | `Atlas.Link` library: `LinkClient`, `LinkHost`, message read/write, `ClockSync`. Unit test: client and host in one process on loopback. | 1.0 h | Link works with no phone |
| 2 | Unity "HostScene" (runs in any Editor or a Windows build): hosts `LinkHost`, prints state, IMGUI sliders from the manifest | 0.5 h | **Phone connected to PC** (Phase-1 criterion met, minimal level) |
| 3 | Console hub: same `LinkHost`, JSONL + binary logs, QR in the terminal | 0.75 h | Always-on PC endpoint |
| 4 | One static HTML page served by Kestrel: sliders generated from `ParamManifest`, preset save/load, live tiles and two uPlot charts (frame-time p95, thermal) | 1.25 h | Tuner + perf dashboard |
| 5 | Spectator: the app's own render scene with ARKit stripped (`ATLAS_SPECTATOR`), camera driven by `State` | 0.5 h on top of shared rendering code | Mirror view on the big screen |

**Stand up the tuner before the spectator.** The tuner speeds up every later hour of shader and geometry work; the spectator matters for the demo and the documentation score, and it can only show something once the geometry renders. [D]

### 6.2 Logging
- `logs/<session>/state.bin`: raw `State` packets with a hub receive timestamp. Doubles as the **replay source** for the fake phone (§6.5).
- `logs/<session>/telemetry.jsonl`, `events.jsonl`, `phone.log` (forwarded warnings and errors).
- The hub prints one status line per second: rate, RTT, loss, thermal, fps.
- A 20-line Python or notebook script turns `telemetry.jsonl` into the performance graphs for the documentation (15 % of the rubric) and for the "30+ FPS" evidence.

### 6.3 Remote tuning
- Any float registered in `ParamRegistry` appears as a slider with no further code. Start with: `_HypL` (curvature scale), `_FogDist`, fog colour steps, tiling `q` (regenerates tiles), 4D slice `w` and rotation speeds, render scale, MSAA, grid-line width, IPD, eye viewport offsets.
- Presets are JSON on the PC and are committed to git. "Bake" copies a preset into the project's defaults so the phone is correct with no PC.
- Parameters that regenerate geometry (`q`) are applied on slider release, not on drag, and bump `worldVer`.
- The same page can send `SceneCmd` to jump between test scenes without taking the headset off.

### 6.4 Device perf to the PC
- Phone samples per frame into a ring buffer (no allocation) and sends `Telemetry` at 2 Hz: fps, frame-time p50/p95/max, CPU main-thread ms and GPU ms from `FrameTimingManager` (enable "Frame Timing Stats" in Player Settings), render scale, battery, tracking state, RTT, loss. [S for `FrameTimingManager` on iOS Metal; see "Could not verify"]
- Thermal state comes from the roughly 10-line native `.mm` plugin the Build Pipeline already requires (`NSProcessInfo.thermalState`), polled every 2 s. [Build Pipeline]
- Draw calls and triangles via `ProfilerRecorder` in Development Builds. [S]
- Deep dives still use the Unity Profiler. It can attach from an Editor on Windows to a Development Build on the iPhone via the Profiler's target dropdown → `<Enter IP>`. [V for the option existing; not tested Windows → iOS]

### 6.5 Dev-loop accelerators for the three people without the Mac
- **Editor play mode with XR Simulation.** "XR Simulation allows you to quickly test your AR app in the Editor without needing to build to a device". It loads a simulated environment with keyboard-and-mouse navigation and detects "planes, images, and point clouds". Limits: "Planes are only detectable on axis-aligned surfaces"; "Meshes are all detected immediately on entering Play Mode". Good for walking logic, holonomy, shaders and the link; not a substitute for real LiDAR scan behaviour. [V]
- **Editor as a fake phone.** `LinkClient` runs unchanged in play mode, so anyone can exercise the hub, tuner and spectator on their own laptop over loopback. [D]
- **Replay.** A `ReplayPhone` tool feeds a recorded `state.bin` to the hub at the original timing. One person records a real walk on the phone; everyone else develops the spectator and dashboards against it. AR Foundation has no built-in ARKit session record/replay, so this is ours. [D; the AR Foundation statement is S]
- **Recorded head path for rendering work.** Replay only `headPos`/`headRot` into the app's own tracking input in the Editor, so shader authors see a real walking path with real jitter. [D]
- **What Windows machines can do:** all C#, shaders, scenes, ProBuilder/Blender work; XR Simulation; Windows spectator builds; run the hub; attach the Profiler. [S]
- **What they cannot do:** produce an installable iOS build. "Xcode is only available for macOS, so if your development machine doesn't run macOS, you can't build an application locally." [V]
- **Mac as a build station.** One script on the MacBook: `git pull` → Unity batch-mode iOS build (append mode) → `xcodebuild` → install to the phone. Anyone can ask for a build of their branch; target ≤ 10 minutes from push to running on the phone. The Mac's owner should not be the only person who can press the button. [D/E]

---

## 7. Decision table

| # | Decision | Choice | Replaces | Tag |
|---|---|---|---|---|
| 1 | Transport | LiteNetLib 2.1.4 over UDP, unicast | WebSocket + raw UDP | Build Pipeline + [V] |
| 2 | PC endpoint | .NET 8 console hub; Unity host scene as the first step | Python FastAPI link server | [D] |
| 3 | Python's role | HTTP assets and GPU jobs only | link, clock sync, routing | [D] |
| 4 | Pose/state encoding | hand-packed binary, 157 B | MessagePack string-keyed maps | [D] |
| 5 | Control encoding | type byte + JSON | MessagePack | [D] |
| 6 | Spectator input | scene id + tile id + f64 Lorentz 3×3 + parameters | "chart, sheet, cell, xforms" (same idea, now concrete) | [D] |
| 7 | Clock sync | own monotonic 4-timestamp ping, min-RTT filter | same maths as Spec; not LiteNetLib NTP | [V] + [D] |
| 8 | Pairing | reserved IP default + typed IP; QR deep link via the Camera app as target | in-app QR or Bonjour | [D] |
| 9 | Network | own router; Windows hotspot fallback; USB-C Ethernet stretch | `iproxy` for runtime (TCP only) | [V] + [S] |
| 10 | Permission prompt | triggered by the first `Connect` on the onboarding screen, 1 s retry | "phone listens on TCP to avoid the prompt" | [V] + [D] |
| 11 | First PC tool | parameter tuner + perf dashboard, then spectator | operator console first | [D] |

---

## 8. Scope levels (4 people, about 36 h)

| Level | Contents | Cost [E] |
|---|---|---|
| **Minimal** (must have by end of Phase 1) | `Atlas.Link`; Unity host scene on the PC; typed/default IP; onboarding prompt with retry; `Hello`/`Welcome`; `State` at 30 Hz; watchdog + auto-reconnect; HUD dot; IMGUI sliders for L, fog, w; tests L1, L2, L9, L12, L13 | 3–4 person-hours |
| **Target** | Console hub; web tuner with presets; `Telemetry` + dashboard + logs; clock sync; 60 Hz; QR deep link; spectator re-rendering H²×E and one other geometry; replay tool; Mac build script; all tests L1–L16 | +5–7 person-hours |
| **Stretch** | `RoomOutline` and room-mesh upload so the spectator shows the scanned room; operator console functions from the Spec (blade queue, FMS entry); USB-C Ethernet path; tablet access to the web page; Python forwarder; second phone as partner client | +6–10 person-hours |

Parallelism: step 1 (library) is the only sequential dependency. After it, the phone-side integration (Mac owner), the hub + web page (one Windows dev) and the spectator scene (whoever owns rendering) proceed in parallel against the Editor fake phone. The link owner needs the Mac for about 30 minutes in total (first device connect, prompt test, L12).

---

## 9. Risks specific to the link

1. **Local Network permission denied or never shown** (first send made while backgrounded, or the user taps Don't Allow). Mitigation: onboarding step with retry and a Settings hint; test on both phones the night before.
2. **Windows Firewall silently drops inbound UDP.** Mitigation: rule in the setup script; the hub prints "no packets received yet, check firewall" after 10 s.
3. **Venue RF congestion.** Mitigation: own router on a fixed channel, phone within a few metres, USB-C Ethernet as stretch. The app does not depend on the link.
4. **Tile ids not reproducible across devices.** Mitigation: combinatorial ids + `worldHash` check (§5.3).
5. **Editor domain reloads kill a Unity-hosted link.** Mitigation: move to the console hub (target level).
6. **Remote tuning left enabled in the demo.** Mitigation: `demoLock`.
7. **API drift:** the source I read is LiteNetLib master (pushed 28 Sep 2026), the package is 2.1.4 (May 2026). Class and member names in the sketches should be checked against the installed package in the first 15 minutes.

---

## 10. Could not verify

- That LiteNetLib 2.1.4 runs on **Unity 6.3 + IL2CPP + iOS 26** specifically. The README says Unity 2021.2+ including iOS; no one in the sources reports this exact combination. First-hour spike.
- That **no Python implementation** of the LiteNetLib protocol exists. One search found none; I did not search exhaustively.
- The **release date of 2.1.4**: the GitHub API and OpenUPM both say 19 May 2026; a page summary rendered it as 2024. I trust the API.
- Whether `Unreliable` and `Sequenced` sends leave immediately or wait for the logic tick; I confirmed `UpdateTime = 15` and `TriggerUpdate()` exist but did not trace the send path fully.
- Whether LiteNetLib's loss/latency simulation is compiled into the OpenUPM package in a Unity release build.
- That the Local Network alert is reliably raised by a **Mono/IL2CPP managed UDP socket** send. TN3179 says the check applies to BSD sockets; I did not find a Unity-specific confirmation.
- Whether iOS offers any API to query Local Network permission state as of iOS 26 (I believe not).
- ATS not applying to raw sockets (standard knowledge, not re-fetched).
- XR Simulation working in the **Windows** Editor (the pages fetched do not name editor platforms) and the exact AR Foundation 6.5 feature list (I fetched the 6.2 overview).
- That a Windows Unity Editor can export an Xcode project with the iOS module installed (commonly done; Unity's page only says you cannot build locally without macOS).
- `FrameTimingManager` GPU times on iOS Metal in Unity 6.3.
- USB-C Ethernet and Personal Hotspot behaviour on iPhone 15 Pro Max with iOS 26 (press coverage only).
- Profiler attach from a Windows Editor to an iOS Development Build over Wi-Fi (the `<Enter IP>` option is documented; the combination is untested).
- Whether the venue Wi-Fi isolates clients (assumed yes).

---

## 11. Questions for the team

1. Is the Windows PC's role in Phase 1 only "spectator + tuner + logs", or must any Python service see live pose in Phase 1? (Decides whether the hub needs a Python forwarder now.)
2. Are you happy to retire the Spec's Python link server and MessagePack schema in favour of the .NET hub and v1-lite, keeping Python for assets and GPU jobs only?
3. Do you own the router already, and does it have 6 GHz? Does the PC have a Wi-Fi card (for the hotspot fallback) and a wired port to the router?
4. Is there a second iPhone (phone B)? Should the link support two phones in Phase 1 or only later?
5. How will tiles be identified in the hyperbolic generator: combinatorial path/index, or generated by float flood-fill? The spectator needs the former.
6. With free walking and a scanned room, should the spectator show the scanned room (outline or mesh) in Phase 1, or only the virtual space?
7. Who owns the Mac during Phase 1, and can it double as the shared build station with a one-command build script?
8. Should the operator be able to trigger `panic` and `recenter` from the PC in Phase 1, or is the PC strictly read + tune until the blades exist?

---

## 12. Sources

**LiteNetLib (primary, fetched)**
- README: https://github.com/RevenantX/LiteNetLib and https://raw.githubusercontent.com/RevenantX/LiteNetLib/master/README.md
- Releases API: https://api.github.com/repos/RevenantX/LiteNetLib/releases
- OpenUPM registry: https://package.openupm.com/com.revenantx.litenetlib · https://openupm.com/packages/com.revenantx.litenetlib/
- Source (master): https://raw.githubusercontent.com/RevenantX/LiteNetLib/master/LiteNetLib/ → `NetConstants.cs`, `NetPacket.cs`, `InternalPackets.cs`, `LiteNetPeer.cs`, `NetPeer.cs`, `LiteNetManager.cs`, `LiteNetManager.Socket.cs`, `NetManager.cs`, `PausedSocketFix.cs`, `NetStatistics.cs`, `INetEventListener.cs`, `Utils/NetSerializer.cs`, `Utils/NetPacketProcessor.cs`, `Utils/NtpRequest.cs`
- Serializer manual: https://revenantx.github.io/LiteNetLib/articles/netserializerusage.html

**Apple (primary, fetched as documentation JSON)**
- TN3179 Understanding local network privacy: https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy
- NSAllowsLocalNetworking: https://developer.apple.com/documentation/bundleresources/information-property-list/nsapptransportsecurity/nsallowslocalnetworking
- Multicast entitlement: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.networking.multicast

**Serialisation (primary, fetched)**
- MessagePack-CSharp README: https://github.com/MessagePack-CSharp/MessagePack-CSharp
- MemoryPack README: https://github.com/Cysharp/MemoryPack

**Unity (primary, fetched)**
- XR Simulation: https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.2/manual/xr-simulation/simulation.html and `.../simulation-overview.html`
- iOS environment setup: https://docs.unity3d.com/6000.2/Documentation/Manual/ios-environment-setup.html
- Profiling on a target device: https://docs.unity3d.com/6000.2/Documentation/Manual/profiling-target-device.html

**Other**
- libusbmuxd / iproxy README (fetched): https://github.com/libimobiledevice/libusbmuxd
- iPhone 15 USB-C Ethernet (secondary, press): https://habr.com/en/news/763042/ · https://kb.plugable.com/question/3306827
- XR Simulation meshing discussion (secondary): https://discussions.unity.com/t/can-you-get-realistic-meshing-in-xr-simulation-and-if-not-more-questions-about-the-real-meshing/1589467

**Project documents**
- Build Pipeline §0.5 (Networking, Hyperbolic precision), §1, §4, §5
- Spec §3.4 (L327–346), §10 (L1146–1256), Appendix B (L2042–2079)
