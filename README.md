# ATLAS

Our entry for Huawei Custom Challenge #1, "Beyond Euclid": your own room becomes the first tile of other
universes, and you walk through them on your own feet. The plan is `documents/ATLAS_Master_Plan.md`.

## What is in this repository

| Folder | What |
|---|---|
| `ATLAS/` | The Unity project (6000.3.25f1, URP, AR Foundation 6.5, iOS). The phone app, and the Spectator for the Station |
| `station/hub/` | The Hub: .NET 8 console that terminates the phone link and serves the operator Console |
| `station/forge/` | The Forge: Python service that turns object photos into furniture meshes |
| `tools/` | `build_ios.sh`, the headless verification harness, scene and `.meta` generators, ReplayPhone, room and perf tools, the Station rehearsal |
| `docs/contracts/` | The interfaces between the four lanes, frozen at gate G0 (`lane-b-kernel.md` is the one lane B page) |
| `docs/laneB/` | Lane B's integration notes, status, remaining placeholders, phone tests and per-task logs |
| `docs/verification/` | The check list for the Unity Editor and the phone |
| `recordings/` | Recorded walks replayed in the Editor and in tests (rooms are the fixtures in `ATLAS/Assets/Atlas/World/Resources/AtlasRooms/`) |
| `documents/` | The plan, the older documents, research memos and handouts |

## State

Phase 1 of all four lanes is written and combined on branch `phase1-integration`: A (platform, look, flat and 4D
spaces), B (kernel, warp, tile renderer, world generator and the curved spaces S2 Curved Walk, S3 Aligned Cells and
S4 Small Planet, with the Lung and FieldFit use cases of Phases 2 and 3), C (scan, room, safety) and D (link,
Station, Forge). One walker kernel serves all seven spaces. What Phase 1 still lacks is listed in
`documents/ATLAS_Whats_Left.md`.

The code compiles for the phone, the Station and the Editor, and its engine-free tests pass, without the Unity
Editor (`tools/headless/verify.py all`: 1,968 tests in 24 test assemblies, none failing, one skipped by decision). It has not yet been opened in Unity or run on a phone, no
shader has been compiled, and no curved walk has been done by a person:
`docs/verification/UNITY_SIDE_BRIEF.md` is the list of what to check there, in order.

## First time

```
tools/setup_git.sh                      # Git LFS and UnityYAMLMerge, once per clone
```

Open `ATLAS/` with Unity 6000.3.25f1. The Editor applies the render set-up on load; commit the files it
regenerates. Press Play in `Assets/Atlas/Scenes/Atlas_Boot.unity`: everything else is built in code.

## Checking without the Editor

```
python3 tools/headless/verify.py all --tag mine    # assembly rules, three build configurations, every engine-free test
```

`python3 tools/e2e/gates.py` runs that plus the Station's suites and rehearsal and prints, for each gate of the plan, what is proven
without a device and what waits for one. It needs the .NET 8 SDK and the Unity managed assemblies; `tools/headless/README.md` says how to point it at them and what `--tag`, `--config`, `--filter` and `wait` do.

## Building to a phone

On the Mac build seat, with the project closed in the Editor and a phone connected:

```
ATLAS_TEAM_ID=XXXXXXXXXX tools/build_ios.sh
```

It exports the Xcode project with Unity in batch mode, builds it with `xcodebuild -allowProvisioningUpdates` and
installs the app with `xcrun devicectl`. The team id is needed once; `docs/build-seat.md` has the rules of the
build seat and the queue.

## The Station

```
dotnet run --project station/hub        # UDP 9050, Console on http://<station>:8080
```

The Forge is set up and started as `station/forge/README.md` describes. With no phone, `tools/ReplayPhone` plays a
recorded or synthetic walk into the Hub. `tools/e2e/run_station_rehearsal.sh` runs the Hub, the Forge and
ReplayPhone together and checks their logs.
