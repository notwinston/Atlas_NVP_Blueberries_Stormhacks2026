# ATLAS contracts

The interfaces between lanes, frozen at gate G0 (Master Plan §6.4, task `P1-X02`). A change to anything in this
folder is announced to all four lanes before it is committed.

| Page | Owner | Fixes |
|---|---|---|
| [lane-a-platform.md](lane-a-platform.md) | A | `ISpaceModule`, `FrameContext`, SpaceLab, services, the frame loop and where the tracked hands' hooks stand in it, shader include contract, `AtlasShaderIDs`, layer and stencil table, how the tracked hands are drawn |
| [lane-b-kernel.md](lane-b-kernel.md) | B | The one lane B page: `Iso2`, `Geometry2`, `TileId`, `WalkerPose`, `ITileGraph`, `IWalkerKernel` and `IDialKernel` (the walker the frame loop steps, flat, hyperbolic and spherical), exact tile ids and seeds, `TileFrame` and `RuleS`, metric and unit conventions, `AtlasWarp` and `AtlasGates` with `_Gate0` to `_Gate3`, what S2 to S4 write into `State`. [kernel.md](kernel.md) only points to it |
| [lane-c-room.md](lane-c-room.md) | C | RoomModel schema, coordinate frame and units, sample rooms and `budget_room`, pose and sensor types, `IBumpGuard` and the depth mask of the walker's own hands, `IRoomContent`, the input service and its hands door (touch and pinch), the tracked-hands types of `Atlas.World` |
| [lane-d-link.md](lane-d-link.md) | D | Parameter registry (the `hands.*` parameters included), link message ids and the `State` layout, event format (the `hands.*` events included), Hub and Forge HTTP routes, GLB asset contract |
| [network.md](network.md) | D (architect) | The Network Atlas: `atlas.network/1` ([network.schema.json](network.schema.json)), its fixtures and bundled files, `/api/network`, `net.*` parameters and events, the Python API of `network.py`, the C# API of `NetworkModel`, `PoincareMap` and `GreedyRouter`, file ownership and test names of NET-01 to NET-11 |
| [unity-api/](unity-api/) | all | Unity and package API facts verified against the installed sources, with file references |

Scene ownership is in [../scene-ownership.md](../scene-ownership.md). Pinned versions are in [../versions.md](../versions.md).

## Foundation

These rules apply to every lane. They settle the points where the Master Plan leaves room, and they are part of
the contract.

### F1 · Assemblies and what each may reference

Master Plan §6.2, with six clarifications marked ◆.

| Assembly | Lane | May reference (Atlas assemblies) | Notes |
|---|---|---|---|
| `Atlas.Kernel` | B (Mesh4D: A) | none | Mathematics, Burst, Collections only. No scene types |
| `Atlas.Link` | D | none | Engine-free (`noEngineReferences`). The same sources compile into the Hub. ◆ Also holds `AtlasJson`, the small engine-free JSON reader and writer used for events, parameter snapshots and RoomModel files |
| `Atlas.World` | A, B, C | Kernel, Link | From Link: message types, parameter types and `AtlasJson` only. Core types here use no `UnityEngine` type, so they run in headless tests |
| `Atlas.Rendering` | A | Kernel, World | ◆ Also holds the room shell and room content (`Rendering/Room/`, lane C files) so that every space and the Spectator can draw the scanned room. §6.2 lists "Home rebuild" under `Atlas.Scan`, which no space may reference. Lane B's tile meshes, tile renderer, draw rule and warp reference (`Rendering/Tiles/`, `Rendering/Warp/`) live here too, because they need the shared material pool and look |
| `Atlas.Scan` | C | Kernel, World | AR session, pose pipeline, scan routes, FloorScan core, RoomModel builder, photo capture. Uses AR Foundation only, so it compiles on every platform |
| `Atlas.Scan.ARKit` | C | Kernel, World, Scan | ◆ Editor and iOS only, because `Unity.XR.ARKit` exists only there. Holds everything that needs an ARKit type (world map save and relocalise, mesh face classification, ARKit session configuration) behind interfaces declared in `Atlas.Scan`, and registers itself at start-up. Nothing references it |
| `Atlas.Safety` | C | World | RoomModel types only. **Never Link or Telemetry**, and no source file uses a type from either |
| `Atlas.Input` | C | none | Input System only |
| `Atlas.Telemetry` | D | Kernel, Link, World | Parameter binding, perf probe, HUD, event log, asset client, link client glue, flat-twin map. ◆ Kernel is added to the §6.2 row for the vertex format (`AtlasVertex`) used by the built-in fallback meshes |
| `Atlas.Hands` | lead | Kernel, Link, World, Rendering, Input | Tracked hands (`documents/ATLAS_Hands_Design.md`). From Link: parameter types only, as a space. Engine-free core, the hand mesh, the sources. Compiles on every platform; referenced by `Atlas.App` only |
| `Atlas.Hands.ARKit` | lead | Kernel, World, Hands | Editor and iOS only. The phone's hand source over the native plugin `AtlasHands.swift`. Registers itself at start-up; nothing references it |
| `Atlas.App` | A | Kernel, Link, World, Rendering, Scan, Safety, Input, Telemetry, Hands | ◆ Link is added to the §6.2 row: `ISpaceModule` carries `ParamRegistry` and `StateMessage`, so the caller needs the reference. ◆ Also holds the on-device menu (`App/Menu/`, lane D files) and onboarding (`App/Onboarding/`, lane C files), which need Input, Scan and Telemetry together |
| `Atlas.Spaces.*` | per space | Kernel, Link, World, Rendering, Input, Telemetry | Spaces never reference each other, Scan, Safety or App |
| `Atlas.Spectator` | D | Kernel, Link, World, Rendering, Telemetry, Spaces.* | Windows only (`ATLAS_SPECTATOR`) |
| `Atlas.World.Lint.Editor` | B | Kernel, World, Rendering | Editor only. WorldLint (`World/Lint/`): the generator checked against the real tile meshes. Nothing references it but its tests |
| `Atlas.Blades.*` | per use case | Kernel, Link, World, Rendering, Scan, Safety, Input, Telemetry, Spaces.* | The use cases, Phase 2 onwards (§6.2). Never App, never another use case. From Safety only `IClearanceQuery`, read-only: a use case never switches Bump Guard |
| `Atlas.Editor` | A | any | Editor only |

`tools/headless/verify.py rules` checks this table (its `ALLOWED` dictionary: one entry per row, `Atlas.Spaces.*` and `Atlas.Blades.*` as patterns, Editor and test assemblies unrestricted) and `Tests/Architecture/AssemblyGraphTests.cs` repeats it as a test. A change to a row is made in all three.

`Safety_Independence` (§11.1) checks the **direct** references of `Atlas.Safety` and its source files for `Atlas.Link`,
`Atlas.Telemetry` and `Atlas.Hands` (`tools/headless/verify.py rules` and `Tests/Architecture/AssemblyGraphTests.cs`
both do, and `BumpGuardMaskFrameTests.TheSafetySources_NameNoHandsAssembly` reads the folder and the asmdef too). The hands reach Bump Guard
as plain numbers, a `DepthMask` inside the `SensorFrame` (lane C §7.3), and Bump Guard holds that mask to its own
limits (lane C §8.8). The closure through `Atlas.World` reaches `Atlas.Link` by design; `Atlas.World` does not
reference `Atlas.Hands`.

### F2 · Engine-free cores

Only one Mac builds to the phones and most tests run without a device, so logic is written to run with no engine.

- Anything that can be computed without the engine lives in plain or static classes that use only `System` and
  `Unity.Mathematics` (`float2`, `float3`, `quaternion`, `double2`, `math`).
- A core never calls `UnityEngine.*` (no `Vector3`, `Mathf`, `Debug`, `Time`, `Mesh`), never calls a method on
  `NativeArray`, `NativeList` or any other native container, and never allocates per frame.
- Burst jobs are thin: the job struct owns the native containers and passes raw pointers and lengths to a static
  kernel method. Tests call the same kernel with pinned managed arrays.
- MonoBehaviours are thin adapters: they read Unity state, call the core, and write the result back.
- A core takes time as an argument (`double timeSeconds`, `float dt`). It never reads a clock.

Tests of cores are plain NUnit (`Assert.That`) and run both in the Unity Test Runner and under
`tools/headless/verify.py` with no Unity installed. Tests that need the engine carry `[Category("Unity")]` and are
wrapped in `#if !ATLAS_HEADLESS`.

### F3 · Composition in code

Nothing in the base depends on hand-edited scene content.

- `Atlas_Boot.unity` holds the AR Session and the XR Origin and nothing else. Nobody edits it.
- `Atlas.App.AtlasBootstrap` (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`) builds
  the rest in code: the stereo rig under the tracked head, the lens blit, the systems, the frame loop, SpaceLab.
- A space module is a `MonoBehaviour` that implements `ISpaceModule` and builds its own content in code from the
  RoomModel. Its scene `Space_<Name>.unity` holds one root object with that component and is generated by
  `tools/gen_scenes.py`. SpaceLab loads the scene additively when it is in the build list. Otherwise, or when the
  loaded scene holds no module, it creates the module from the factory the space registered in `SpaceRegistry`.
- Systems (scan, Bump Guard, input, hands, link, HUD) are created by the composition root and published through
  `AtlasServices`. A system never finds another system by searching the scene. The hands (`Atlas.Hands`) are built
  from a set of ports (`HandsPorts`): plain references and delegates, so that the assembly names neither the app, the
  scan nor Bump Guard, and what the root builds after it (Bump Guard, the event log, the space host) is reached lazily.
- Meshes, materials and textures of the base are generated at run time or loaded from `Resources`. Material
  instances come from the shared pool and are never saved as assets.

### F4 · One coordinate frame

Once a RoomModel is active, **Unity world space is the room frame**.

- Origin: the centre of the free rectangle, on the floor. `y = 0` is the floor.
- `+X` runs along the free rectangle's `Wf` side, `+Z` along its `Df` side, `+Y` up. Unity's left-handed
  convention, metres, radians in code, degrees only in UI.
- Floor coordinates are `float2 (x, z)`.
- The pose pipeline moves the XR Origin every frame so that the room anchor sits at the world origin with
  identity rotation. Before a RoomModel is active, world space is the AR session frame.
- Room content, every flat space and every portal room is authored in this frame, so their transforms are the
  identity (D16).
- Bump Guard works in the same frame from the raw head pose.

### F5 · One frame loop

`Atlas.App.AtlasFrameLoop` (`[DefaultExecutionOrder(100)]`) makes steps 2 to 6 of §6.3 as plain calls, in order,
from one `Update`. No other script reads the head pose, steps the kernel, or ticks a space in its own `Update`.
Step 8 runs from the same driver after rendering. Nothing in steps 1 to 7 waits for the network or for a file.
The tracked hands are three hooks of this loop and have no `Update` of their own: `HandsSenseStep` at step 1 (after
the pose pipeline's, before input's), `HandsUploadStep` at step 6 and `HandsAfterFrameStep` at step 8b (lane A §7).
The loop puts no net under hooks at steps 6 and 8b, so these two catch their own exceptions.

### F6 · Parameters

Every tunable lives in the `ParamRegistry` (`Atlas.Link`), never in a scene.

- A space declares its parameters in `ISpaceModule.Configure`.
- `Atlas.Rendering`, `Atlas.Scan` and `Atlas.Input` do not reference `Atlas.Link`. They expose plain properties,
  and the composition root declares the registry entries that drive them.
- `Atlas.Hands` may reference `Atlas.Link` for parameter types, as a space does: `HandsSystem.DeclareParams` declares
  the `hands.*` entries. The composition root calls it after its own declarations, the `dev.*` ones included, so
  the hands take the wire ids that follow them; only the perf tour's `perf.*`, declared later, come after (lane D
  §1.4).
- `Atlas.Safety` declares none. Bump Guard's thresholds and depth rate are constants in code (§4.10), and so are the
  limits it holds a depth mask to (`BumpConstants`, lane C §8.2).
- Reading a parameter on the frame path allocates nothing.

### F7 · One look

One shader (`Atlas/Surface`), one palette texture, one pooled material instance per tile, room or object. No
`MaterialPropertyBlock` anywhere in `Atlas.*`. No new shader keyword without lane A. No post-processing, shadows
or bloom (D23). The shaders that exist besides `Atlas/Surface` are the ones of lane A's §10.1. The hands added one,
`Atlas/NearText`, the built-in font shader with its depth test on: panels within reach of the hands draw their text
with the legacy `TextMesh`, whose material has the depth test off, and that text would paint over a finger held in
front of it. Its material is made once for each font and is not pooled, like the materials of the other special shaders.
The hands, their rings and the near panels' surfaces are `Atlas/Surface` and come from the pool.

### F8 · Files

| What | Where |
|---|---|
| RoomModel fixtures (`sample_3x3`, `sample_L_3x4`, `sample_4x5_table`, `budget_room`, `tiny_standing`) | `ATLAS/Assets/Atlas/World/Resources/AtlasRooms/*.json`, loaded with `Resources.Load<TextAsset>` on device and read from disk by headless tests (`RoomFixtures.LoadFromProject`). They are the only room fixtures: lane B's tests use them too |
| RoomModel JSON schema | `docs/contracts/roommodel.schema.json` |
| Saved scans on the phone | `Application.persistentDataPath/rooms/room_<id>.json` and `room_<id>.worldmap` |
| Recorded walks for replay (`ATLAS-POSELOG 1`, in the frame of `sample_3x3`) | `recordings/walk_*.poselog`: three of lane C's and two of lane B's kernel and curved-space tests (`recordings/README.md`) |
| Recorded hand movements for replay (`ATLAS-HANDLOG 1`) | `recordings/hands_*.handlog`: three synthetic logs, `hands_wave`, `hands_press` and `hands_pinch`, which the tests of the hands' log and sources read (`recordings/README.md`) |
| Perf CSV and event log on the phone | `Application.persistentDataPath/logs/` |
| Pose logs and hand logs on the phone | `Application.persistentDataPath/logs/`: `walk_<UTC time>.poselog` while `pose.record` is on, `hands_<UTC time>.handlog` while `hands.record` is on |

No camera frame is written to disk (rule 10). A hand log holds joints and depths, never a picture.

### F9 · Editor first

Every flow runs in the Editor with no phone. The head pose comes from one of three sources behind one interface:
the tracked AR camera (device and XR Simulation), a recorded pose log, or a keyboard-and-mouse walker. A RoomModel
comes from a scan, from a saved file or from a fixture. The tracked hands come from one of three sources behind
`IHandsSource`: the phone's detector (`VisionHandsSource`, over the native plugin), a recorded hand log
(`ReplayHandsSource`, which tests and scripts use) or the Editor's mouse and keys (`EditorHandsSource`, available only
in the Editor and only while the developer parameter `hands.editor` is on); with none of them there are no hands and
nothing else changes. Native plugins sit behind C# wrappers that return neutral values off the device, so every
assembly compiles on Windows and macOS.
