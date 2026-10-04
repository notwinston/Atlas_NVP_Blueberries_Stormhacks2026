## Appendix B. Message schema v1

Little-endian. Byte 0 is the message id. Poses: Unity convention (left-handed, Y up, metres), quaternions `[x, y, z, w]`. `t_us` = sender's monotonic microseconds. JSON messages are id byte + UTF-8 string. `proto` = `[1, 0]`.

### B.1 `State` (0x10, channel 1, binary, 202 B)

| Field | Type | Meaning |
|---|---|---|
| `msgType` | u8 | 0x10 |
| `flags` | u8 | bits 0–1 tracking (normal, limited, lost); bit 2 pose jump rejected; bit 3 warning cue active; bits 4–5 quality tier; bit 6 demo lock |
| `sceneId` | u16 | SpaceLab module or use-case scene |
| `seq` | u32 | per send |
| `t_us` | i64 | phone clock |
| `headPos` | 3 × f32 | physical head position, RoomModel anchor frame |
| `headRot` | 4 × f32 | physical head rotation |
| `spaceId` | u8 | 0–6 = S0–S6 |
| `bumpLevel` | u8 | 0 clear, 1 warn, 2 strong, 3 tracking lost (ARKit tracking not Normal for 0.5 s, §4.10) |
| `tileId` | 6 × i64 | {4,5}: the six ℤ[φ] integers; S1: cell (i, j, k, 0, 0, 0); S4: face index; S5: room id; else 0 |
| `sheet` | i16 | Mode 2 cell index or portal sheet; in Shared House, the sheet id (§10.3.6) |
| `xform` | 9 × f64 | tile-local transform, row-major (identity when unused) |
| `p` | 8 × f32 | `[0]` L, `[1]` fog start (ρ − 1), `[2]` ρ, the radius at which the fog is fully opaque and from which the draw rule follows (`curved.cullRho`, §8.5), `[3]` slice w, `[4..7]` 4D angles or spare |
| `worldVer` | u16 | bumps on regeneration |

### B.2 Other LiteNetLib messages

| Message | Id | Ch | Encoding | Direction | Fields |
|---|---|---|---|---|---|
| `Hello` | 0x01 | 0 | JSON | client → Hub | `proto`, `role` (`phone`, `spectator`, `editor`), `device`, `app`, `build`, `worldHash`, `roomId` |
| `Welcome` | 0x02 | 0 | JSON | Hub → client | `proto`, `sessionId`, `worldHash`, `roomId`, `demoLock`, `forgeUrl` |
| `TPing` | 0x03 | – | binary | both | `seq u32`, `t1_us i64` |
| `TPong` | 0x04 | – | binary | both | `seq u32`, `t1_us`, `t2_us`, `t3_us` (i64) |
| `PeerState` | 0x11 | 1 | binary | Hub → phone | Reserved, Phase 3 (Extended, `P3-D07`): a peer's `State`, relayed by the Hub. `P3-D07` fixes the layout and adds it here; Phase 1 has no codec for it and a receiver ignores the id (§7.5, rule 5) |
| `ParamManifest` | 0x20 | 0 | JSON | phone → Hub | `params: [{id, name, group, min, max, def, val, onRelease}]` |
| `ParamSet` | 0x21 | 0 | binary | Hub → phone | `count u8`, then `count × {id u16, value f32}` |
| `SceneCmd` | 0x22 | 0 | JSON | Hub → phone | `{space: "S0"…"S6", variant, args}` |
| `OperatorCmd` | 0x23 | 0 | JSON | Hub → phone | `{cmd: home \| recentre \| quality \| cue \| lock, args}`; `quality` takes `{tier: 0–3}`, `lock` takes `{on}`; `cue` is the Console's STOP button (§7.3.2) |
| `Telemetry` | 0x30 | 0 | binary | phone → Hub | `t_us i64`, `seq u32`, `sceneId u16`, `spaceId u8`, `tier u8`; f32: `fps`, `p50`, `p95`, `p99`, `max`, `over17_2`, `over33_8` (share of frames over 17.2 ms and over 33.8 ms, §11.2), `cpuMainP95`, `cpuRenderP95`, `gpuP95`, `renderScale`; u16: `drawCalls`, `setPass`, `trisK`, `memMB`, `visibleTiles`, `copies`, `rttMs`, `lossPermille`, `cueCount`; `gcAllocB u32`; u8: `thermal` (0–3), `battery`, `tracking`, `msaa`, `stateHz`, `bumpMax`, `assetsPlaced`, `assetsPending` |
| `Event` | 0x40 | 0 | JSON | phone → Hub | `{t_us, scene, ev, data}` |
| `RoomOutline` | 0x41 | 0 | binary | phone → Hub → spectators | `count u8`, `count × {x f32, z f32}`, `height f32`, `rect 5 × f32` (the free rectangle: centre x, z, Wf, Df, yaw), `boxCount u8`, `boxCount × {id u8, cat u8, cx, cz, w, d, h, yaw f32}` |
| `Log` | 0x42 | 0 | JSON | phone → Hub | `{t_us, level, msg}` (warnings and errors) |
| `JobStatus` | 0x50 | 0 | JSON | Hub → phone, spectators | `{jobId, roomId, boxId, state, source: local \| hosted \| cache, error}` |
| `AssetReady` | 0x51 | 0 | JSON | Hub → phone, spectators | `{jobId, roomId, boxId, sha256, url, bytes, tris, maxEdge, bounds[3], palette}` |
| `AssetAck` | 0x52 | 0 | JSON | phone → Hub | `{jobId, boxId, sha256, ok, loadMs, worstFrameMs, reason}` |
| `Bye` | 0x7F | 0 | JSON | both | `{reason}` |

### B.3 Hub HTTP (port 8080)

| Route | Request | Response |
|---|---|---|
| `GET /` | – | Console page |
| `GET /pair` | – | page with the QR of `atlas://<ip>:9050` |
| `WS /ws` | Console → Hub: `{t: "param", id, value}`, `{t: "scene", …}`, `{t: "op", cmd, args}` | Hub → Console: `manifest`, `telemetry`, `state` (10 Hz digest), `event`, `log`, `job`, `link` as JSON |
| `GET /api/session` | – | `{sessionId, phone: {state, rttMs, loss}, spectators, forge: {up, queue, debugKeep}}`; `debugKeep` true makes the Console show the red "crops kept" banner (§7.4) |
| `GET /api/presets` · `POST /api/presets/{name}` | – · `{values: {id: value}}` | list · 204 |
| `GET /logs/{session}/perf.csv` | – | CSV |
| `POST /internal/job` (loopback only) | `JobStatus` or `AssetReady` body | 204 |

### B.4 Forge HTTP (port 8000)

| Route | Request | Response |
|---|---|---|
| `GET /health` | – | `{ok, gpu, queue, pipelineVer, debugKeep}`; `debugKeep` is true when the Forge runs with `FORGE_DEBUG_KEEP=1` |
| `POST /rooms` | RoomModel JSON | 201 `{roomId, roomHash}` |
| `PUT /rooms/{roomId}/mesh` | binary mesh, ≤ 2 MB | 204 |
| `GET /rooms/{roomId}` | – | RoomModel JSON |
| `DELETE /rooms/{roomId}` | – | 204; removes the room record, its mesh and the GLBs made for it (§13.5) |
| `POST /rooms/{roomId}/objects` | multipart: `meta` = `{boxId, category, dims[3], yaw, triTarget, backend, views: [{score, azimuthDeg, intrinsics[4], camToBox[16]}]}`; files `view0`…`view2` (JPEG). `triTarget` is the triangle target of §7.4 (3,000–5,000); `backend` is the phone's `forge.backend` value (`local`, `hosted` or `cache`) | 202 `{jobId}` or 200 `{jobId, cached: true, sha256}` |
| `GET /jobs/{jobId}` · `GET /jobs?room={roomId}` | – | `JobStatus` (with the `AssetReady` fields when ready) · list |
| `GET /assets/{sha256}.glb` | – | GLB; `ETag` = hash; 404 if unknown |
