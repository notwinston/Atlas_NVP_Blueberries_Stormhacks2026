## Appendix C. Set-up checklist

Three set-up tasks have no dependency and run side by side: `P1-X01` (C.1 and C.2: build seat, signing and Unity; lane A leads), `P1-X03` (the Station's tools, the first line of C.7; lane D) and `P1-X04` (phones and router: the phone lines of C.1 and C.6 and the router lines of C.7; lane C). The remaining lines are ticked by the Phase 1 tasks that build on them (§9.5). Within a section, work top to bottom.

"verify" means: confirm on the machine, then write the installed number into `docs/versions.md`. Port numbers are defaults assumed here; Appendix B wins if it differs.

### C.1 Accounts and machines

| Who / what | Needs |
|---|---|
| Signing owner (lane A) | Apple account, two-factor on, signed in to Xcode on the Mac as a Personal Team (Spec §2.8) |
| All four engineers | Repository access, Git LFS, Unity account, Unity Editor at the pinned patch with iOS and Windows Build Support (licence terms: verify) |
| MacBook Pro M3 Max | Xcode 26, Unity with iOS Build Support. The only iOS build seat (§9.3) |
| Station PC (RTX 5070) | .NET 8 SDK, Python 3.11, NVIDIA driver with CUDA 12.8, Unity with Windows Build Support |
| Phone A (USB-C), phone B (Lightning) | One cable each; Developer Mode; iOS version recorded |

Free Personal Team rules (Spec App. E):

- [ ] One owner account. A build signed by another team cannot install over the existing app.
- [ ] One fixed bundle id, `com.<owner>.atlas`, for the whole project (limit: 10 new app ids per 7 days).
- [ ] At most 3 registered devices (phone A, phone B, one spare) and 3 apps per device. No TestFlight.
- [ ] Builds stop launching 7 days after install; any reinstall renews them.
- [ ] The app ships with no entitlements.

### C.2 Versions to pin

| Component | Pin | Source / status |
|---|---|---|
| Unity | 6000.3 LTS, patch ≥ 6000.3.21f1, exact patch in `ProjectVersion.txt` | (R5 C1) thermal frame-rate Player Setting exists from this patch |
| URP | 17.x as bundled | (Spec §2.2) verify |
| AR Foundation, ARKit XR Plugin | 6.5.x, same version for both | (BP §0.5) verify patch |
| Xcode | 26 | (BP §0.5) required by ARKit XR Plugin 6.4+ |
| iOS deployment target | 17.0 | (Spec §2.2) |
| iOS on the phones | Recorded; drift test run | (R1 C.1) drift regression reported in 26.4–26.6: verify |
| LiteNetLib | 2.1.4: OpenUPM `com.revenantx.litenetlib` in Unity, NuGet `LiteNetLib` in the Hub | (R4 §1) on Unity 6.3 + IL2CPP + iOS 26: verify |
| .NET SDK | 8 | (R4 §2) |
| glTFast | `com.unity.cloud.gltfast` | (Spec §2.3) verify version; furniture arrives as GLB at run time |
| Burst, Collections, Mathematics, Input System, Test Framework | As resolved for 6000.3, committed in `packages-lock.json` | (Spec §2.3) |
| Python | 3.11 | (Spec §2.2) |
| PyTorch | ≥ 2.7, cu128 wheels | (Spec §2.2) Blackwell needs CUDA 12.8 |
| TripoSG | Commit hash in the Forge lock file | (BP §0.5) MIT, 8 GB VRAM; on cu128: verify |
| FastAPI, uvicorn, pydantic v2, trimesh, `rembg` (background removal), `gltfpack` | Lock file committed | (Spec §2.5) |
| TotalSegmentator | Optional, `station/pipelines` only | (BP §0.5) airway generations 1–2 only |

### C.3 Unity packages: keep or drop

| Package (Spec §2.3 unless noted) | Decision | Reason |
|---|---|---|
| URP; AR Foundation, ARKit, XR Management | Keep | ARKit is the only XR loader; no display provider |
| Input System; Burst, Collections, Mathematics | Keep | Input; kernel, Mesh4D, Bump Guard jobs |
| glTFast | Keep | Run-time GLB from the Forge |
| meshopt decompress, KTX | Only if the Forge emits them | Palette-coloured GLB needs neither |
| Test Framework, `com.unity.profiling.core` | Keep | EditMode and PlayMode tests; perf probe |
| apple/unityplugins Core + CoreHaptics | Keep | Bump Guard haptic pulses |
| ProBuilder (BP §0), LiteNetLib (R4) | Add | Greybox rooms; link transport |
| ARCore | Drop | No Android client; re-add only if the team wants Android later |
| Cardboard SDK / XR plugin | Drop | Conflicts with the ARKit loader (BP §1.1); distortion is our own blit, Cardboard coefficients as numbers only |
| MessagePack-CSharp | Drop | Hand-packed binary plus JSON (R4 §4) |
| LiveKit, UniVRM, uLipSync, Steam Audio | Drop | Voice is Stretch (§10.3.1) and would not use LiveKit; lip-synced VRM avatars are not in the plan (its avatars are plain meshes); simple panning is enough |
| UXF, Sentry | Drop | Own CSV telemetry and `Log` messages |
| HoloKit Unity SDK (BP §0.5) | Not in the manifest | Optional hand-tracking spike on a branch; needs iOS 18+ |

### C.4 Project and Player settings

- [ ] Project from the AR Mobile template (BP §6); XR Plug-in Management → iOS → ARKit only.
- [ ] Editor: Asset Serialisation = Force Text; Visible Meta Files; UnityYAMLMerge as the Git merge tool; Git LFS for `*.glb`, `*.ktx2`, `*.png`, `*.fbx` (Spec §2.7).
- [ ] Player (iOS): IL2CPP, ARM64, Metal only; target iOS 17.0; landscape left only; Frame Timing Stats on; Managed Stripping Level Low; VSync count 1 (Spec §2.7).
- [ ] Player (iOS): "Adjust iOS FPS based on thermal state" off; the thermal ladder owns quality (R5 C1).
- [ ] Signing Team ID = the Personal Team; Automatically Sign on; bundle id fixed.
- [ ] Boot script: `Application.targetFrameRate = 60`; `Screen.sleepTimeout = SleepTimeout.NeverSleep` (BP §5, R4 §3.5).
- [ ] ARKit video: the lowest-resolution 60 Hz video format from `ARCameraManager.GetConfigurations()` (BP §5). The environment depth image must be non-null at that format (checked in C.6).
- [ ] ARKit depth (`P1-C01`): `AROcclusionManager` with environment depth mode Fastest, temporal smoothing off, human stencil and human depth off. The depth image is read only with `TryAcquireEnvironmentDepthCpuImage`, at 256 × 192 (§8.2).
- [ ] ARKit after the scan: meshing, plane detection, light estimation and environment probes off; LiDAR depth stays on (D19).

URP asset `Atlas_URP_iOS` (BP §4, R5 E1; this plan's value where BP gives a range):

| Setting | Value |
|---|---|
| Rendering path | Forward, Render Graph on |
| HDR; Depth Texture; Opaque Texture; Depth Priming | Off |
| MSAA | 4× |
| Render scale | 1.0 in the URP asset (the eye-texture size carries the 0.8) |
| Post-processing, FXAA, STP | Off |
| Shadows; additional lights | Off |
| SRP Batcher | On |
| GPU Resident Drawer, static batching, baked occlusion culling | Off |

### C.5 Info.plist and build settings

`Assets/Atlas/Editor/AtlasPostBuild.cs` (`[PostProcessBuild]`, lane A) writes every key. Nobody edits the generated Xcode project by hand.

| Key | Value | Why |
|---|---|---|
| `NSCameraUsageDescription` | Text | Tracking, scan, object photos |
| `NSLocalNetworkUsageDescription` | Text | Unicast UDP to the Hub raises the prompt (R4 §3.1) |
| `NSAppTransportSecurity` → `NSAllowsLocalNetworking` | `YES` | HTTP to a LAN address on iOS 17+ (R4 §3.4) |
| `UIFileSharingEnabled` | `YES` | Local perf CSV readable from the Mac |
| `CFBundleURLTypes` | scheme `atlas` | QR deep link `atlas://<ip>:9050` (R4 §3.7) |
| `MetalHudEnabled` | `YES`, development builds only | Metal Performance HUD (R5 E2) |

- [ ] Also `UIRequiresFullScreen = YES` and build setting `ENABLE_BITCODE = NO` (Spec §2.4). No `NSBonjourServices`, no microphone key, no multicast entitlement (R4 §3.2).
- [ ] `Assets/Plugins/iOS/AtlasThermal.mm` returns `NSProcessInfo.thermalState`; an Editor stub returns 0 (BP §1.4). It is delivered with the first build (`P1-A02`) and must return a value on the phone at G1.
- [ ] On a patch below 6000.3.21f1 the script also rewrites `adjustFrameRateForThermalState:` to return the target rate (R5 C1).

### C.6 First build to both phones

- [ ] Xcode › Settings › Accounts shows "Name (Personal Team)".
- [ ] Each phone: connect, Trust This Computer, Settings › Privacy & Security › Developer Mode › on, restart, Enable.
- [ ] Build the empty stereo scene (AR Session, XR Origin, two-camera rig, no camera background); install with `tools/build_ios.sh` (C.8) or Xcode Run.
- [ ] First launch: Settings › General › VPN & Device Management › owner account › Trust.
- [ ] Accept the camera prompt in hand, not in the headset.

| Device check (both phones) | Pass |
|---|---|
| Empty stereo scene, Release build | Metal HUD reads 60 fps (BP §6) |
| Tracking | Session state reaches tracking; head pose moves both eyes |
| LiDAR | `ARMeshManager` mesh subsystem non-null and running; environment depth image non-null at the chosen video format (C.4) |

If phone B fails the LiDAR check, raise it in §14.

### C.7 Station set-up

- [ ] Tools (`P1-X03`): .NET 8 SDK (`dotnet --version` reports 8.x); Python 3.11; NVIDIA driver with CUDA 12.8 and the cu128 PyTorch wheels (`nvidia-smi` and a PyTorch CUDA test pass); the `station/` folders of C.9 exist.
- [ ] Own Wi-Fi router, no internet needed, fixed non-DFS channel; Station on Ethernet; phones on 5 GHz; AirDrop and Handoff off (R4 §3.6).
- [ ] DHCP reservation: Station at `192.168.x.10`; Windows network profile Private (R4 §3.7–3.8). Check (`P1-X04`): each phone opens a test page served from that address.
- [ ] Firewall, once, as administrator (`station/setup_firewall.ps1`):

```
netsh advfirewall firewall add rule name="ATLAS Hub UDP" dir=in action=allow protocol=UDP localport=9050
netsh advfirewall firewall add rule name="ATLAS Hub HTTP" dir=in action=allow protocol=TCP localport=8080
netsh advfirewall firewall add rule name="ATLAS Forge" dir=in action=allow protocol=TCP localport=8000
```

- [ ] Hub: `dotnet run --project station/hub`. UDP 9050; Kestrel on `0.0.0.0:8080` serves the Console (R4 §3.8).
- [ ] The Hub's `logs/` folder is on an encrypted volume; motion logs are treated as health data (§13.5).
- [ ] Forge: Python 3.11 virtual environment in `station/forge`; `torch.cuda.is_available()` is true; `uvicorn forge.app:app --host 0.0.0.0 --port 8000`. One process owns the GPU (Spec §2.5). Port 8000: (derived).
- [ ] Spectator: Windows build with the `ATLAS_SPECTATOR` define, or an Editor in play mode; joins the Hub as role `spectator` (R4 §6.1).
- [ ] Pairing: open `http://192.168.x.10:8080/pair`, scan the QR with the iOS Camera app; `Application.deepLinkActivated` stores the address in `PlayerPrefs`. Fallback: typed address (R4 §3.7).
- [ ] Raise the Local Network prompt on the onboarding screen, phone in hand; the client retries the connect every 1 s (R4 §3.3). Repeat after any delete-and-reinstall.

`iproxy` forwards TCP only, so LiteNetLib traffic does not pass through a USB tunnel (R4 §3.6): on-device development needs the router as well as the cable.

Acceptance: the Console shows the phone connected; a `GET` to the Forge health route from the phone returns 200; with the Hub stopped the app runs unchanged apart from the link indicator.

### C.8 Development loop and build seat

Three engineers have no Mac; their loop is the Editor (R4 §6.5):

- [ ] XR Simulation on for Editor play mode (Windows Editor: verify). It does not replace a real scan: axis-aligned planes only.
- [ ] `recordings/` holds `room_*.json` (RoomModel from a real scan) and `walk_*.poselog` (timestamped head poses). The Editor loads a RoomModel and replays a pose log into the pose pipeline in place of ARKit.
- [ ] `ReplayPhone` feeds a recorded `state.bin` to the Hub, so the Console and Spectator need no phone; the Editor also runs `LinkClient` unchanged against a Hub on loopback.
- [ ] Kernel, tile-id, FloorScan, loop-fit and message-packing logic runs as EditMode tests; native code sits behind stubs so every assembly compiles on Windows.

Build seat:

- [ ] `tools/build_ios.sh <branch>`: fetch → Unity batch-mode iOS build, Append mode → `xcodebuild -allowProvisioningUpdates` → `xcrun devicectl device install app` to each connected phone. With a Personal Team: verify (Spec §2.8). Fallback: Xcode Run.
- [ ] Any engineer can run the script. Build queue in one shared file: who, branch, what is tested, pass number. One integration build at every gate.
- [ ] Phones are booked, test protocol written first. Hand-held before head-mounted. Thermal state recorded at the start of every measurement.
- [ ] Published numbers: Release build, debugger detached, Metal API Validation off, in the headset (R5 E2).

### C.9 Repository layout and ownership

One assembly definition per folder; lane in brackets.

```
atlas/
├── unity/
│   ├── Assets/Atlas/
│   │   ├── Kernel/       Atlas.Kernel (B; Mesh4D and its slicing job: A; no scene types)
│   │   ├── World/        Atlas.World (A, B, C; see §6.2)
│   │   ├── Rendering/    Atlas.Rendering (A; warp and gates includes, tile renderer: B)
│   │   │   └── Shaders/  AtlasCore.hlsl, AtlasWarp.hlsl, AtlasPortalClip.hlsl, AtlasGates.hlsl
│   │   ├── Scan/         Atlas.Scan (C)
│   │   ├── Safety/       Atlas.Safety (C; Bump Guard; must not reference Atlas.Link or Atlas.Telemetry)
│   │   ├── Input/        Atlas.Input (C; action map and twins)
│   │   ├── Link/         Atlas.Link (D; plain C#, no engine references, shared with the Hub)
│   │   ├── Telemetry/    Atlas.Telemetry (D)
│   │   ├── Spaces/       Atlas.Spaces.Home (C); .Glued, .Portal, .Slicer (A); .CurvedWalk, .AlignedCells, .SmallPlanet (B)
│   │   ├── Blades/       Atlas.Blades.Playground, .Lung, .SharedHouse, .FieldFit
│   │   ├── Spectator/    Atlas.Spectator (D)
│   │   ├── Editor/       AtlasPostBuild.cs
│   │   └── Tests/        one test assembly per area
│   ├── Assets/Plugins/iOS/   AtlasThermal.mm
│   ├── Packages/             manifest.json, packages-lock.json
│   └── ProjectSettings/
├── station/
│   ├── hub/          .NET 8 console; compiles the Atlas.Link sources; wwwroot/ = Console
│   ├── forge/        Python FastAPI, TripoSG
│   └── pipelines/    medical data preparation
├── recordings/       RoomModel JSON, pose logs, state.bin
├── tools/            build_ios.sh, ReplayPhone
├── docs/             contracts/, evidence/, versions.md, licence ledger
└── figures/
```

Who may edit which scene, prefab and shader file is set out once, in §9.3 and §8.3; the rule for the shader property ids (`AtlasShaderIDs.cs`) is in §6.4. Scene names are those of §9.3; each space's scene takes the suffix of its assembly: `Space_Home`, `Space_Glued`, `Space_Portal`, `Space_Slicer`, `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet`.

Rules: tunables live in the parameter registry, never in scenes; no new shader keyword without lane A; the shared shader stays SRP-Batcher compatible at every gate; Asset Store packs stay out of the repository (BP §3d).

### C.10 Things that bite at first set-up

| Trap | Rule | Source |
|---|---|---|
| Frame rate stuck at 30 fps | iOS default, or the Unity thermal cap: check both C.4 settings in the built app | BP §1.3, R5 C1 |
| Phone never connects | In order: Local Network prompt denied or raised while backgrounded; firewall rule missing; server bound to `localhost` | R4 §3.1, §3.8 |
| LiteNetLib precompiled DLL, or its `NetSerializer`, under IL2CPP | OpenUPM package or sources only; pack messages by hand | R4 §1 |
| App will not open after 7 days; "Untrusted Developer"; identifier mismatch | Reinstall; trust the owner; delete the copy signed by another team | Spec App. E |
| RoomPlan capture removes scene depth from frames | Re-apply the session configuration after capture starts: verify | R1 C.1 |
| Older CUDA code on the RTX 5070 | cu128 wheels; rebuild custom kernels | Spec §2.7 |
