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
| `docs/contracts/` | The interfaces between the four lanes, frozen at gate G0 |
| `docs/verification/` | The check list for the Unity Editor and the phone |
| `recordings/` | Recorded walks and rooms replayed in the Editor |
| `documents/` | The plan, the older documents, research memos and handouts |

## State

Phase 1 of lanes A (platform, look, flat and 4D spaces), C (scan, room, safety) and D (link, Station, Forge) is
written. Lane B (the hyperbolic and spherical spaces) is not: its contract types exist with a flat stub.

The code compiles for the phone, the Station and the Editor, and its engine-free tests pass, without the Unity
Editor (`tools/headless/verify.py all`). It has not yet been opened in Unity or run on a phone:
`docs/verification/UNITY_SIDE_BRIEF.md` is the list of what to check there, in order.

## First time

```
tools/setup_git.sh                      # Git LFS and UnityYAMLMerge, once per clone
```

Open `ATLAS/` with Unity 6000.3.25f1. The Editor applies the render set-up on load; commit the files it
regenerates. Press Play in `Assets/Atlas/Scenes/Atlas_Boot.unity`: everything else is built in code.

## Checking without the Editor

```
python3 tools/headless/verify.py all    # assembly rules, three build configurations, every engine-free test
```

It needs the .NET 8 SDK and the Unity managed assemblies; `tools/headless/README.md` says how to point it at them.

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
