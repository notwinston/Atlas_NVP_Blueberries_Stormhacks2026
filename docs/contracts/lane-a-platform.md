# Lane A · Platform contract

Task `P1-X05`, frozen at gate G0 (Master Plan §6, §8, §9.3; README F1 to F9). Owner: lane A.

This page fixes what every lane builds against on the platform side: the space interface and its host, the
frame loop, the composition root, the rendering facade, the one shader and its mesh format, the layer and
stencil table, and `Mesh4D`. Names and signatures are binding. Bodies are free.

| Part | In this build | Later |
|---|---|---|
| `SpaceId`, `ISpaceModule`, `FrameContext`, `ISpaceHost`, `SpaceRegistry`, `AtlasServices`, frame hooks | Final | unchanged |
| `AtlasBootstrap`, `AtlasFrameLoop`, SpaceLab | Built | unchanged |
| Stereo rig, eye textures, lens blit, quality ladder | Built; lens numbers are placeholders (open item 1) | Headset numbers baked in |
| `Atlas/Surface`, `AtlasCore.hlsl`, `AtlasPortalClip.hlsl`, palette, `MaterialPool` | Built | unchanged |
| `AtlasWarp.hlsl`, `AtlasGates.hlsl` | Identity bodies from lane B's page | Lane B fills them (`P1-B03`, `P1-B10`) |
| S1 Glued, S5 Impossible Rooms, S6 Slicer, `Mesh4D` | Built | unchanged |
| S2, S3, S4 | Not registered; a switch to them is refused | Lane B |

---

## 0 · Conventions

- Paths are relative to `/workspace/ATLAS/Assets/Atlas/` unless they start with `tools/`, `docs/` or `ATLAS/`.
- Metres, seconds, radians. Degrees appear only in registry parameters that an operator reads, and say so.
- World space is the room frame once a RoomModel is active (README F4): origin on the floor at the centre of the
  free rectangle, +X along `wf`, +Z along `df`, +Y up, left-handed. Floor points are `float2 (x, z)`.
- Quaternions are Unity's: `(x, y, z, w)`, head forward is local +Z, right is +X, up is +Y.
- "(decided here)" marks a choice the plan leaves open and this page makes.
- "(proposed)" marks a number with no measured source, to be confirmed at the gate named beside it.
- Draw calls and triangles are counted per frame, both eyes, as in §8.5.

### 0.1 Where lane A's types live

| Type | Assembly · namespace | File |
|---|---|---|
| `SpaceId`, `SpaceIds` | `Atlas.World` · `Atlas.World` | `World/Spaces/SpaceId.cs` |
| `ISpaceModule`, `ISpaceStateReader` | same | `World/Spaces/ISpaceModule.cs` |
| `ISpaceHost`, `SpaceHostState` | same | `World/Spaces/ISpaceHost.cs` |
| `SpaceRegistry` | same | `World/Spaces/SpaceRegistry.cs` |
| `FrameContext` | same | `World/Frame/FrameContext.cs` |
| `ISenseStep`, `ISafetyStep`, `IPoseFilterStep`, `IUploadStep`, `IStateSink`, `IAfterFrameStep`, `IFrameLoop`, `IOperatorCommands` | same | `World/Frame/FrameHooks.cs` |
| `AtlasServices`, `AtlasBootContext` | same | `World/Services/AtlasServices.cs`, `World/Services/AtlasBootContext.cs` |
| `QualityTier`, `QualityRow`, `QualityTable`, `IQualityState`, `IPerfSource`, `QualityInputs`, `QualityLadderCore` | same | `World/Quality/*.cs` |
| `MeshData` | same | `World/Meshing/MeshData.cs` |
| `AtlasVertex`, `AtlasShading` | `Atlas.Kernel` · `Atlas.Kernel` | `Kernel/Mesh/AtlasVertex.cs`, `Kernel/Mesh/AtlasShading.cs` |
| `Mesh4D`, `Polytopes4D`, `Rotation4D`, `Projector4D`, `Slicer4D`, `SliceJob` | `Atlas.Kernel` · `Atlas.Kernel.FourD` | `Kernel/FourD/*.cs` |
| `AtlasBoot`, `AtlasThermal` (exist) | `Atlas.Rendering` · `Atlas.Rendering` | `Rendering/AtlasBoot.cs`, `Rendering/AtlasThermal.cs` |
| `AtlasShaderIDs`, `AtlasLayers`, `AtlasPalette`, `AtlasLook`, `AtlasMesh`, `MaterialPool` | same | `Rendering/Look/*.cs` |
| `IStereoRig`, `StereoRig`, `DisplayMode`, `Eye`, `EyeTextures`, `LensBlit`, `LensParams` | same | `Rendering/Stereo/*.cs` |
| `QualityLadder` | same | `Rendering/Quality/QualityLadder.cs` |
| `PortalRenderer`, `PortalRoom`, `PortalDoor` | same | `Rendering/Portals/*.cs` |
| Shaders and includes | – | `Rendering/Shaders/*` (§10.1) |
| `AtlasBootstrap`, `AtlasFrameLoop`, `SpaceLab`, `CommandRouter`, `RenderParams`, `QualityBinding`, `BumpGuardStep`, `InputSenseStep` | `Atlas.App` · `Atlas.App` | `App/*.cs` |
| `GluedSpace`, `PortalSpace`, `SlicerSpace` | `Atlas.Spaces.Glued`, `.Portal`, `.Slicer` | `Spaces/<Name>/<Name>Space.cs` |
| `AtlasBuild`, `AtlasPostBuild`, `AtlasProjectSetup`, `AtlasUsageText` (exist) | `Atlas.Editor` · `Atlas.EditorTools` | `Editor/*.cs` |

`Atlas.World` core types use only `System`, `Unity.Mathematics`, `Atlas.Kernel` and `Atlas.Link` (README F1,
F2). The one exception is `AtlasBootContext`, which carries `UnityEngine.Transform` references and is not a core
type.

### 0.2 Names this page takes from other pages

This page uses the names below and relies on nothing else about them. Where another page spells one of them
differently, that page wins and this one is corrected at the freeze.

| Name | Page | What this page relies on |
|---|---|---|
| `WalkerPose`, `WalkerPose.Start`, `TileId`, `Iso2` | lane B §2 | As written there (`Atlas.Kernel`) |
| `IWalkerKernel`, `KernelStep`, `KernelConfig.Flat(double)`, `FlatWalkerKernel` | lane B §4 | `Step(double2 deltaMetres, bool trackingNormal)`, `ResetToFloor(double2)`, `Reset(in WalkerPose)`, `Configure`, `Pose`, `CullRho`, `MetresPerUnit` |
| `AtlasWarp(float3)`, `AtlasGatesClip(float3)`, `_WarpMode`, `_Anchor`, `_Gate0` to `_Gate3`, `_HeadPosWS`, `_HypL` | lane B §5, §6 | As written there |
| `RoomModel` | lane C | A class in `Atlas.World` with the fields of `roommodel.schema.json`: `freeRect.wf`, `freeRect.df`, `lapSquare.side`, `cells.glued` (`nx`, `nz`, `sizeX`, `sizeZ`), `standing`, `walls`, `objects`. C# member names are lane C's |
| `TrackingState` | lane C | An enum in `Atlas.World` with `Normal = 0`, `Limited = 1`, `Lost = 2`, the coding of `State.flags` bits 0 to 1 (name assumed) |
| `IBumpGuard`, `BumpLevel`, `SensorFrame`, `BumpCue.Fire` | lane C | §6.5 of the plan |
| `IRoomContent`, wall modes, object category | lane C | A root `Transform` for all room renderers; a mode in which walls are kerbs; objects on or off; a change event; the category as an integer 0 to 31 (the order of `objects[].category` in the schema) |
| `ParamRegistry`, `StateMessage` | lane D | `Atlas.Link`. Declaring a parameter by name is idempotent and keeps the current value; a group can be marked active or inactive; a parameter can be set locally |
| `AtlasPerfProbe` | lane D | Implements `IPerfSource` (§9.5) |

---

## 1 · `SpaceId`

```csharp
// File: World/Spaces/SpaceId.cs
namespace Atlas.World
{
    /// <summary>The seven spaces of Phase 1 (Master Plan section 3.1). The value is State.spaceId (Appendix B.1).</summary>
    public enum SpaceId : byte
    {
        Home = 0,
        Glued = 1,
        CurvedWalk = 2,
        AlignedCells = 3,
        SmallPlanet = 4,
        Portal = 5,
        Slicer = 6,
    }

    public static class SpaceIds
    {
        public const int Count = 7;
        /// <summary>State.spaceId while no space is entered (onboarding, scan).</summary>
        public const byte NoSpace = 255;
        /// <summary>State.sceneId while no space is entered.</summary>
        public const ushort NoScene = 0xFFFF;

        /// <summary>"S0" to "S6": the code used by SceneCmd (Appendix B.2) and in logs.</summary>
        public static string Code(SpaceId id);
        /// <summary>Parses "S0" to "S6". False for anything else.</summary>
        public static bool TryParseCode(string code, out SpaceId id);
        /// <summary>"Space_Home" and so on: the scene name of Appendix C.9.</summary>
        public static string SceneName(SpaceId id);
        /// <summary>Lower-case parameter group: "home", "glued", "curved", "cells", "planet", "portal", "slicer".</summary>
        public static string ParamGroup(SpaceId id);
        /// <summary>State.sceneId of a SpaceLab space: the numeric value of the id.</summary>
        public static ushort SceneId(SpaceId id);
    }
}
```

| Value | Code | Member | Shown as | Scene | Assembly | Module class | Owner | `Registered` | In this build |
|---|---|---|---|---|---|---|---|---|---|
| 0 | S0 | `Home` | Home | `Space_Home` | `Atlas.Spaces.Home` | `HomeSpace` | C | true | yes |
| 1 | S1 | `Glued` | Glued | `Space_Glued` | `Atlas.Spaces.Glued` | `GluedSpace` | A | true | yes |
| 2 | S2 | `CurvedWalk` | Curved Walk | `Space_CurvedWalk` | `Atlas.Spaces.CurvedWalk` | `CurvedWalkSpace` | B | false | no |
| 3 | S3 | `AlignedCells` | Aligned Cells | `Space_AlignedCells` | `Atlas.Spaces.AlignedCells` | `AlignedCellsSpace` | B | true | no |
| 4 | S4 | `SmallPlanet` | Small Planet | `Space_SmallPlanet` | `Atlas.Spaces.SmallPlanet` | `SmallPlanetSpace` | B | false | no |
| 5 | S5 | `Portal` | Impossible Rooms | `Space_Portal` | `Atlas.Spaces.Portal` | `PortalSpace` | A | true | yes |
| 6 | S6 | `Slicer` | Slicer | `Space_Slicer` | `Atlas.Spaces.Slicer` | `SlicerSpace` | A | true | yes |

- The module class is `<Name>Space` in the namespace of its assembly (decided here). `tools/gen_scenes.py` relies
  on it.
- `State.sceneId`: 0 to 6 for a SpaceLab space, `0xFFFF` with `spaceId = 255` while no space is entered,
  100 and above reserved for use-case scenes of Phase 2 (decided here).

---

## 2 · `ISpaceModule`

Exactly §6.5 of the plan.

```csharp
// File: World/Spaces/ISpaceModule.cs
using Atlas.Kernel;
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>One space of the catalogue: configured from the room, entered, ticked and left by SpaceLab.</summary>
    public interface ISpaceModule
    {
        SpaceId Id { get; }
        /// <summary>Does this space match the real room? Constant from the end of Configure until Exit.</summary>
        bool Registered { get; }
        /// <summary>Every size comes from the RoomModel. Declares the space's parameters.</summary>
        void Configure(RoomModel room, ParamRegistry registry);
        void Enter(in WalkerPose start);
        /// <summary>Step 5 of the frame, after Bump Guard and the pose filter.</summary>
        void Tick(in FrameContext frame);
        void Exit();
        /// <summary>What the Spectator needs to redraw this frame.</summary>
        void WriteState(ref StateMessage state);
    }

    /// <summary>Optional. Lets the Spectator drive the module from a received State (Master Plan section 7.3.1).</summary>
    public interface ISpaceStateReader
    {
        /// <summary>Called by SpaceLab in the Spectator build before Tick, with the newest State.</summary>
        void ReadState(in StateMessage state);
    }
}
```

A module is a `MonoBehaviour` (README F3). SpaceLab is its only caller.

| Call | When | World visible? | The module may | The module must not |
|---|---|---|---|---|
| `Awake`, `OnEnable` | SpaceLab loads the scene or runs the factory | no (black) | Cache its own components | Read the room, the registry or any service; build content |
| `Configure(room, registry)` | Once per load, straight after creation. `room` is never null | no | Declare parameters (idempotent, §4.3). Build meshes, rent materials, create children under its own transform. Allocate freely | Touch anything outside its own root except through `IRoomContent`, `AtlasLook` and `MaterialPool`. Wait for a file or the network |
| `Enter(in start)` | Once, in the same frame as `Configure`. For a registered space `start` is `IWalkerKernel.Pose` after `ResetToFloor`; otherwise the kernel's pose as it stands | no | Set the wall mode, fog and background; show its content; start its state from `start` | Change `Registered` |
| `Tick(in frame)` | Every frame from the frame after `Enter` until `Exit`, at step 5, also during the fades | yes | Interaction, simulation, setting material values, scheduling a job | Allocate managed memory. Read the head pose from anywhere but `frame`. Call Bump Guard or read its level. Wait for a job it did not schedule this frame, for the network or for a file |
| `IUploadStep.Upload(in frame)` (only if the module also implements `IUploadStep`) | Every frame at step 6, after every `Tick` | yes | Complete this frame's jobs and upload mesh and material data | Allocate; simulate |
| `WriteState(ref state)` | Every frame after `Tick`, at step 8a, while entered | yes | Write `tileId`, `sheet`, `xform`, `p[3]` to `p[7]` | Write any other field. Allocate. Change its own state |
| `Exit()` | Once, world black | no | Return every rented material, complete jobs, dispose native containers, destroy its own meshes and textures | Leave anything alive outside its root |
| `OnDestroy` | SpaceLab destroys the root or unloads the scene | no | Nothing that `Exit` should have done | – |

Rules:

1. **Budget of a switch.** `Configure` plus `Enter` take at most 50 ms on phone A (proposed, checked at G4). The
   main thread is blocked for that time and Bump Guard with it, so a longer build is spread over several `Tick`
   calls behind the fade.
2. **No allocation on the frame path** (rule 8 of §8.9, §11.2): `Tick`, `Upload` and `WriteState` allocate 0 B of
   managed memory. No LINQ, no string building, no boxing, no lambda that captures, no `foreach` over an interface.
   Native containers are `Allocator.Persistent`, created in `Configure` and disposed in `Exit`.
3. **Sizes come from the room.** A module holds no size constant that should follow the room. Fixed constants
   named in the plan (the 2.6 m period of T³, the 0.25 m edge rule) are allowed.
4. **Tunables come from the registry** (README F6). A module has no serialised tunable and reads no scene value.
5. **Own root only.** Everything a module creates is a child of its own `transform`. SpaceLab destroys that root.
6. **Transforms are the identity** for a registered space (README F4): the module's root sits at the world origin
   with no rotation and no scale. Glued copies and mirrored rows are children with their own transforms.
7. **A module never touches the rig, the cameras or the XR Origin.** Eye positions are in `FrameContext`. That is
   what lets the Spectator run the same module.
8. **One look** (README F7): `Atlas/Surface` through `MaterialPool`, `renderer.sharedMaterial`, no
   `MaterialPropertyBlock`, no other shader except TextMeshPro for text.
9. **Safety is not a module's business.** A module cannot switch Bump Guard off, hide its cue or delay it.

What `WriteState` receives and writes:

| Field | Filled by `AtlasFrameLoop` before the call | The module writes |
|---|---|---|
| `msgType`, `flags`, `sceneId`, `seq`, `t_us`, `headPos`, `headRot`, `spaceId`, `bumpLevel`, `worldVer` | yes | never |
| `p[0]`, `p[1]`, `p[2]` | `(float)MetresPerUnit`, `CullRho − 1`, `CullRho` from `IWalkerKernel` (lane B §8.3) | never |
| `tileId` | six zeros | Its tile, cell or room id (Appendix B.1) |
| `sheet` | 0 | Its sheet or cell index |
| `xform` | identity, row-major | `Pose.Local` in a curved space |
| `p[3]` to `p[7]` | 0 | Its own values (§14) |

---

## 3 · `FrameContext`

One value per frame, owned by `AtlasFrameLoop`, passed by reference. Nobody keeps a copy across frames.

```csharp
// File: World/Frame/FrameContext.cs
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>Everything a system may know about the current frame (Master Plan section 6.3).</summary>
    public struct FrameContext
    {
        public int FrameIndex;
        public double Time;
        public float DeltaTime;

        public float3 RawHeadPosition;
        public quaternion RawHeadRotation;
        public TrackingState Tracking;

        public float3 HeadPosition;
        public quaternion HeadRotation;
        public float2 FloorDelta;
        public bool PoseJumpRejected;

        public WalkerPose Walker;
        public KernelStep Kernel;

        public float3 LeftEyePosition;
        public float3 RightEyePosition;

        public QualityTier Tier;
    }
}
```

| Field | Type | Unit · frame | Meaning | Written at | By |
|---|---|---|---|---|---|
| `FrameIndex` | `int` | frames | 0 on the loop's first frame, then +1 per frame. Never reset | start of `Update` | A |
| `Time` | `double` | s | The frame's time on the app clock: `Time.unscaledTimeAsDouble` on the device; the pose log's clock in replay. Monotonic | start of `Update` | A |
| `DeltaTime` | `float` | s | `Time` minus the previous frame's `Time`, clamped to 0.004 to 0.1. 1/60 on frame 0 | start of `Update` | A |
| `RawHeadPosition` | `float3` | m · world | The tracked head: the midpoint between the eyes (§9.2). Relocalisation jumps included. What Bump Guard, the cameras and `State.headPos` use | step 1 | C |
| `RawHeadRotation` | `quaternion` | world | The tracked head's rotation, no prediction | step 1 | C |
| `Tracking` | `TrackingState` | – | ARKit tracking state of this frame | step 1 | C |
| `HeadPosition` | `float3` | m · world | The geometry head pose: what a space reads. Lane C's page defines how it differs from the raw pose | step 3 | C |
| `HeadRotation` | `quaternion` | world | Rotation of the geometry head pose | step 3 | C |
| `FloorDelta` | `float2` | m · world (x, z) | `HeadPosition.xz` minus the previous frame's. Zero on frame 0 and on the frame after a walker reset. It is what step 4 hands to the kernel; the kernel may still reject it (`Kernel.Rejected`) | step 3, by the loop after the filter | A |
| `PoseJumpRejected` | `bool` | – | The pose filter dropped a jump this frame. `State.flags` bit 2 | step 3 | C |
| `Walker` | `WalkerPose` | kernel units | `IWalkerKernel.Pose` after this frame's `Step` | step 4 | A from B |
| `Kernel` | `KernelStep` | – | What the step did: `Rejected`, `Crossings`, `LastEdge`, `AppliedMetres`, `TileChanged` | step 4 | A from B |
| `LeftEyePosition`, `RightEyePosition` | `float3` | m · world | Raw head pose with the eye offset: `RawHeadPosition ∓ RawHeadRotation · (ipd / 2, 0, 0)`. Both equal the head position when there is no rig (hand mode, Spectator) | step 1, after the sense steps | A |
| `Tier` | `QualityTier` | – | The quality tier in force this frame (§9.5) | start of `Update` | A |

- **Geometry pose** (decided here, to be matched by lane C's page): after a rejected jump the geometry pose
  follows the raw pose again. The jump is dropped from the displacement, not kept as a standing offset, so a
  registered space stays registered. Registered spaces may therefore read either pose; they read `HeadPosition`.
- **No input in the context.** `Atlas.World` cannot name a type of `Atlas.Input`. Lane C's input system freezes
  one snapshot per frame at step 1; a space reads it through the input service (lane C's page).
- **No Bump Guard level in the context.** Spaces do not react to it. `AtlasFrameLoop` reads it for `State`.
- `FrameContext` is about 250 bytes. Always pass it with `in` or `ref`.

---

## 4 · `ISpaceHost` and SpaceLab

`Atlas.App.SpaceLab` hosts exactly one space at a time. Other lanes see it only as `ISpaceHost`.

```csharp
// File: World/Spaces/ISpaceHost.cs
using System;

namespace Atlas.World
{
    public enum SpaceHostState : byte
    {
        Empty = 0,       // no space entered: onboarding, scan
        FadingOut = 1,   // the old space still ticks; the world fades to black
        Loading = 2,     // black: Exit, unload, load, Configure, Enter
        FadingIn = 3,    // the new space ticks; the world fades in
        Running = 4,
    }

    /// <summary>SpaceLab as the other lanes see it (Master Plan task P1-A08).</summary>
    public interface ISpaceHost
    {
        SpaceHostState State { get; }
        /// <summary>True from the end of Enter until Exit.</summary>
        bool HasSpace { get; }
        /// <summary>The entered space. Valid only while HasSpace.</summary>
        SpaceId Current { get; }
        /// <summary>The entered module, or null.</summary>
        ISpaceModule Module { get; }

        /// <summary>The active RoomModel, or null before the first scan, load or fixture.</summary>
        RoomModel Room { get; }
        /// <summary>Standing mode (Master Plan section 4.4): Room is not null and its standing flag is set.</summary>
        bool Standing { get; }
        /// <summary>State.worldVer: +1 on every Configure, SetRoom and BumpWorldVersion. Wraps at 65,536.</summary>
        ushort WorldVersion { get; }

        /// <summary>True when the space has a scene in the build or a registered factory.</summary>
        bool IsAvailable(SpaceId id);
        /// <summary>Starts a switch with a fade. False when refused; see the table below.</summary>
        bool RequestSwitch(SpaceId target);
        /// <summary>Return to Home (Master Plan section 4.6): fade, walker reset, Home registered again.</summary>
        bool ReturnHome();
        /// <summary>Makes a RoomModel active. With a space entered, that space is reloaded through a fade.</summary>
        void SetRoom(RoomModel room);
        /// <summary>For a module whose geometry was regenerated by a parameter with onRelease set.</summary>
        void BumpWorldVersion();

        /// <summary>After Enter, before the fade-in starts.</summary>
        event Action<SpaceId> SpaceEntered;
        /// <summary>After Exit, before the old content is destroyed.</summary>
        event Action<SpaceId> SpaceExited;
        /// <summary>After SetRoom, before any space is reloaded.</summary>
        event Action<RoomModel> RoomChanged;
    }
}
```

### 4.1 `RequestSwitch`

| Situation | Result |
|---|---|
| `Room` is null | false, event `space.refused` with reason `noRoom` |
| `IsAvailable(target)` is false (S2, S3, S4 in this build) | false, event `space.refused` with reason `unavailable`. Nothing reaches the kernel |
| `target` is the entered space and `State` is `Running` | false, nothing happens |
| `State` is `Running` or `Empty` | true, the sequence of §4.2 starts in the same frame |
| A switch is already under way | true. The newest request replaces the pending one and runs when the current switch reaches `Running` |

`ReturnHome()`: when Home is entered and running it resets the walker (`IFrameLoop.ResetWalkerToFloor`) with no
fade and returns true; otherwise it is `RequestSwitch(SpaceId.Home)`. It is the target of the operator command
`home` and of the gamepad's Home action.

### 4.2 The switch, step by step

Constants in `SpaceLab` (decided here): `FadeOutSeconds = 0.25f`, `FadeInSeconds = 0.25f`. The fade is the global
`_AtlasFade` (§10.4): the world and the background go to black, the head-locked cue and HUD do not. A switch with
a scene in the build is under 1.0 s end to end (proposed, G4). The first fade frame starts within 250 ms of an
operator command (test L10).

| # | State | What SpaceLab does |
|---|---|---|
| 1 | `FadingOut` | Ramps `_AtlasFade` 1 → 0 over `FadeOutSeconds`. The old module keeps ticking. Skipped when `State` was `Empty` |
| 2 | `Loading` | `old.Exit()`; raises `SpaceExited`; marks the old parameter group inactive; destroys the old root, or `SceneManager.UnloadSceneAsync` for a scene |
| 3 | `Loading` | `AtlasLook.ResetForSpace()` (§10.8); `IRoomContent` back to its default mode (lane C's page); `PortalRenderer.Clear()` |
| 4 | `Loading` | Creates the module by the scene-or-factory rule of §5.2. On failure: logs `space.loadFailed` and continues with Home; if Home fails too, ends in `Empty` with the room content visible |
| 5 | `Loading` | `module.Configure(Room, registry)`; `WorldVersion += 1`; marks the new parameter group active |
| 6 | `Loading` | If `module.Registered`: `IFrameLoop.ResetWalkerToFloor()` (lane B §4.1). Then `module.Enter(kernel.Pose)`; caches `module as IUploadStep`; writes `space.id`; raises `SpaceEntered` |
| 7 | `FadingIn` | Ramps `_AtlasFade` 0 → 1 over `FadeInSeconds`. The new module ticks from the first frame |
| 8 | `Running` | Runs a pending request, if any |

Steps 2 to 6 run in one frame when the module comes from a factory, and over the frames of the asynchronous load
when it comes from a scene. Bump Guard and its cue run in every one of those frames.

SpaceLab never calls `Resources.UnloadUnusedAssets`: a module destroys what it created (rule 5 of §2). Twenty
switches leave memory within ±5 % (`P1-A08`).

`SetRoom(room)`: stores the room, `WorldVersion += 1`, calls `kernel.Configure(KernelConfig.Flat(s))` with s the
lap-square side and `IFrameLoop.ResetWalkerToFloor()` (lane B §4.1), raises `RoomChanged` (room content rebuilds
on it), then reloads the entered space through the sequence above. It does not enter a space by itself: after the
first scan, onboarding calls `RequestSwitch(SpaceId.Home)`.

### 4.3 Parameters of a space

| Rule | Detail |
|---|---|
| Group | A space's parameters are named `<group>.<name>` with the group of `SpaceIds.ParamGroup` (`glued.mode`) |
| Declared twice, kept once | Each space gives `SpaceRegistry` a `declareParams` delegate (§5). SpaceLab runs every delegate once at boot, so every parameter of §12.2 and §12.4 exists from the first frame and the on-device menu can change it with any space entered. `Configure` declares the same parameters again; a declaration by an existing name returns the existing entry with its current value |
| Values survive a switch | Nothing is deleted on `Exit`. A value tuned in a space is still there when the space is entered again |
| "Removed" means inactive | On `Exit` SpaceLab marks the group inactive; on `Enter`, active. The Console and the menu show an inactive group folded and greyed. The kill switches of §12.4 stay active always |
| `onRelease` | A parameter that regenerates geometry is declared with `onRelease`; its change handler rebuilds and calls `BumpWorldVersion()` |
| Reading | A module keeps the parameter handles it got in `Configure` and reads their values in `Tick`. No lookup by name on the frame path |

What this needs from `ParamRegistry` is listed in §0.2. If lane D's registry has no group flag, the fallback is
that every group stays listed; nothing else changes.

### 4.4 Standing mode

`ISpaceHost.Standing` is the RoomModel's `standing` flag (`wf` under 1.5 m, §4.4). Every module reads it in
`Configure`. S1 and S6 run in place (§14); a module never prompts the walker to walk while it is set.

---

## 5 · `SpaceRegistry`

```csharp
// File: World/Spaces/SpaceRegistry.cs
using System;
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>Where each space assembly announces itself, so that Atlas.App needs no reference to it.</summary>
    public static class SpaceRegistry
    {
        /// <summary>Registers or replaces the entry of a space. Safe to call twice (no domain reload in the Editor).</summary>
        /// <param name="factory">Creates a new GameObject named like the scene, adds the module, returns it.</param>
        /// <param name="declareParams">Declares the space's parameters; may be null.</param>
        public static void Register(SpaceId id, Func<ISpaceModule> factory, Action<ParamRegistry> declareParams = null);
        public static bool IsRegistered(SpaceId id);
        /// <summary>Runs the factory. Null when the space is not registered.</summary>
        public static ISpaceModule Create(SpaceId id);
        /// <summary>Runs every declareParams delegate, in SpaceId order. Called once by SpaceLab at boot.</summary>
        public static void DeclareAllParams(ParamRegistry registry);
        /// <summary>For tests.</summary>
        public static void Clear();
    }
}
```

### 5.1 Registration

One static method per space assembly, and nothing else outside the module class (binding pattern):

```csharp
// File: Spaces/Glued/GluedRegistration.cs
namespace Atlas.Spaces.Glued
{
    static class GluedRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() =>
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.Glued, GluedSpace.Create, GluedSpace.DeclareParams);
    }
}
```

`BeforeSceneLoad` runs before `AtlasBootstrap` (`AfterSceneLoad`), so the registry is full when SpaceLab starts.
S2, S3 and S4 register nothing in this build (lane B §8.2).

### 5.2 Scene or factory (README F3)

```text
name = SpaceIds.SceneName(target)
if Application.CanStreamedLevelBeLoaded(name):
    load it additively; keep Atlas_Boot the active scene
    module = the first ISpaceModule on a root object of the loaded scene
    if module is null: unload the scene and fall through
if module is null and SpaceRegistry.IsRegistered(target):
    module = SpaceRegistry.Create(target); parent its GameObject under "Atlas/Space"
if module is null: the load failed
```

`IsAvailable(id)` = the scene can be loaded, or the space is registered. A space's scene holds one root object
named like the scene, with the module component and nothing else; `tools/gen_scenes.py` (lane A) writes it. The
module behaves the same either way, because it builds its content in `Configure`.

---

## 6 · `AtlasServices`

```csharp
// File: World/Services/AtlasServices.cs
using System;

namespace Atlas.World
{
    /// <summary>Typed service locator. The composition root fills it; nothing searches the scene (README F3).</summary>
    public static class AtlasServices
    {
        /// <summary>Publishes a service under the type T. Replaces an earlier one. Null: ArgumentNullException.</summary>
        public static void Register<T>(T service) where T : class;
        /// <summary>Removes the service if it is the one published under T.</summary>
        public static void Unregister<T>(T service) where T : class;
        /// <summary>The service, or InvalidOperationException naming T when none is published.</summary>
        public static T Get<T>() where T : class;
        public static bool TryGet<T>(out T service) where T : class;
        /// <summary>True from the end of AtlasBootstrap until Clear.</summary>
        public static bool BootCompleted { get; }
        /// <summary>Raised once when the composition root has finished. A late subscriber is called at once.</summary>
        public static event Action Booted;
        /// <summary>Removes everything. Called first by AtlasBootstrap and by tests.</summary>
        public static void Clear();
    }
}
```

- `T` is the key. Publish under the interface, not the class: `Register<ISpaceHost>(spaceLab)`.
- `Get<T>()` is a static field read: no dictionary, no allocation, safe on the frame path. Still, take what you
  need once in your installer or in `Configure` and keep it.
- Main thread only.
- A service that needs another takes it in its installer, after the row above it in §8.2 has run.

Services of lane A, and the slots this page reserves for the others:

| Key | Declared in | Published by | Present in the Spectator |
|---|---|---|---|
| `ParamRegistry` | `Atlas.Link` | `AtlasBootstrap` (the one instance) | yes |
| `IFrameLoop` | `Atlas.World` | `AtlasFrameLoop` | yes |
| `ISpaceHost` | `Atlas.World` | `SpaceLab` | yes |
| `IQualityState` | `Atlas.World` | `QualityLadder` | yes (always `Nominal`) |
| `IOperatorCommands` | `Atlas.World` | `CommandRouter` | no |
| `IWalkerKernel` | `Atlas.Kernel` | `AtlasBootstrap` (`new FlatWalkerKernel()`, lane B §4.1) | yes |
| `MaterialPool` | `Atlas.Rendering` | `AtlasBootstrap` | yes |
| `IStereoRig` | `Atlas.Rendering` | `StereoRig` | no |
| `PortalRenderer` | `Atlas.Rendering` | `AtlasBootstrap` | yes |
| `IPerfSource` | `Atlas.World` | lane D's perf probe | no |
| `IRoomContent`, the pose source, the scan session, the input service, `IBumpGuard`, the asset client, the event log | their pages | their installers | their pages |

---

## 7 · The frame loop

`Atlas.App.AtlasFrameLoop` is the only script that reads the head pose, steps the kernel or ticks a space
(README F5).

### 7.1 Hooks

```csharp
// File: World/Frame/FrameHooks.cs
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>Step 1. Reads sensors once per frame and fills the raw fields of the frame.</summary>
    public interface ISenseStep { void Sense(ref FrameContext frame); }

    /// <summary>Step 2. Bump Guard. Runs before anything can be skipped; never waits.</summary>
    public interface ISafetyStep { void Guard(in FrameContext frame); }

    /// <summary>Step 3. Fills HeadPosition, HeadRotation and PoseJumpRejected.</summary>
    public interface IPoseFilterStep { void Filter(ref FrameContext frame); }

    /// <summary>Step 6. Writes per-frame data to materials, meshes and shader globals.</summary>
    public interface IUploadStep { void Upload(in FrameContext frame); }

    /// <summary>Step 8a. Takes the frame's State for sending. Never blocks.</summary>
    public interface IStateSink { void Send(in StateMessage state); }

    /// <summary>Step 8b. Runs after the frame has been rendered.</summary>
    public interface IAfterFrameStep { void AfterFrame(in FrameContext frame); }

    /// <summary>The frame loop as installers see it.</summary>
    public interface IFrameLoop
    {
        /// <summary>The context of the frame in progress. Valid from step 1.</summary>
        ref readonly FrameContext Current { get; }
        /// <summary>Step 1 hooks run in the order they were added.</summary>
        void AddSense(ISenseStep step);
        /// <summary>Exactly one. A second call replaces the first and logs a warning.</summary>
        void SetGuard(ISafetyStep step);
        void SetPoseFilter(IPoseFilterStep step);
        /// <summary>Step 6 hooks run in the order they were added, after the entered module's own Upload.</summary>
        void AddUpload(IUploadStep step);
        void SetStateSink(IStateSink sink);
        /// <summary>Step 8b hooks run in the order they were added.</summary>
        void AddAfterFrame(IAfterFrameStep step);
        /// <summary>kernel.ResetToFloor(geometry head floor point); the next FloorDelta is zero (lane B section 4.2).</summary>
        void ResetWalkerToFloor();
        /// <summary>kernel.Reset(pose); the next FloorDelta is zero.</summary>
        void ResetWalker(in Atlas.Kernel.WalkerPose pose);
    }

    /// <summary>The six operator commands as local calls (Master Plan section 7.3.2). No link type appears here.</summary>
    public interface IOperatorCommands
    {
        /// <summary>SceneCmd. variant may be null; its meaning per space is in section 14.</summary>
        void SwitchSpace(SpaceId space, string variant);
        void Home();
        void Recentre();
        /// <summary>-1 automatic, 0 to 3 the ladder rungs, 4 the high tier.</summary>
        void SetQuality(int tier);
        /// <summary>The Console's STOP button: BumpCue.Fire at STRONG.</summary>
        void Cue();
        void SetLock(bool on);
    }
}
```

`Atlas.Input` references no Atlas assembly (README F1), so it cannot implement `ISenseStep`. `Atlas.App` adds a
small adapter that calls lane C's input poll at step 1. The same holds for step 2 when `SensorFrame` is declared
in `Atlas.Safety`: `Atlas.App.BumpGuardStep` implements `ISafetyStep`, copies the raw pose, the tracking state and
lane C's newest depth frame into a `SensorFrame`, and calls `IBumpGuard.Tick(in sensors)`.

### 7.2 The call sequence

```csharp
// File: App/AtlasFrameLoop.cs   (shape only)
[DefaultExecutionOrder(100)]                     // after TrackedPoseDriver (Update only) and every other Update
public sealed class AtlasFrameLoop : MonoBehaviour, IFrameLoop
{
    void Update()
    {
        BeginFrame();                                        //    FrameIndex, Time, DeltaTime, Tier
        for (i) _sense[i].Sense(ref _frame);                 // 1  pose, depth, input
        FillEyes(ref _frame);                                //    from IStereoRig
        _guard.Guard(in _frame);                             // 2  Bump Guard
        _filter.Filter(ref _frame);                          // 3  pose filter
        FillFloorDelta(ref _frame);                          //    HeadPosition.xz minus the previous one
        _frame.Kernel = _kernel.Step(_frame.FloorDelta, _frame.Tracking == TrackingState.Normal);   // 4
        _frame.Walker = _kernel.Pose;
        _spaceLab.TickSpace(in _frame);                      // 5  ISpaceModule.Tick
        _spaceLab.UploadSpace(in _frame);                    // 6  the module's own IUploadStep, if any
        for (i) _upload[i].Upload(in _frame);                // 6  AtlasLook, PortalRenderer, StereoRig, lane B later
    }
    // 7  URP renders Eye Left, Eye Right, then the Screen camera with the lens blit

    void LateUpdate()                                        // 8a
    {
        FillState(ref _state);                               //    common fields, section 2
        _spaceLab.WriteState(ref _state);                    //    ISpaceModule.WriteState while a space is entered
        _sink.Send(in _state);
    }

    void OnEndContextRendering(...)                          // 8b, once per frame (guarded by Time.frameCount)
    {
        for (i) _after[i].AfterFrame(in _frame);
    }
}
```

| Step (§6.3) | Call | Hook declared in | Implemented by | Lane |
|---|---|---|---|---|
| 1 Read | `ISenseStep.Sense(ref frame)`, each in order | `Atlas.World` | Pose pipeline in `Atlas.Scan`: moves the XR Origin so the room anchor is the world origin (README F4), then fills `RawHeadPosition`, `RawHeadRotation`, `Tracking` from `AtlasBootContext.Head`, and takes the newest depth image | C |
| 1 Read | same | `Atlas.World` | `Atlas.App.InputSenseStep`: freezes the input snapshot | A (adapter), C (input) |
| 1 Read | `FillEyes` | – | `AtlasFrameLoop` from `IStereoRig` | A |
| 2 Bump Guard | `ISafetyStep.Guard(in frame)` → `IBumpGuard.Tick(in SensorFrame)` | `Atlas.World`; `Atlas.Safety` | `Atlas.App.BumpGuardStep` and lane C's Bump Guard | C (A adapter) |
| 3 Pose filter | `IPoseFilterStep.Filter(ref frame)` | `Atlas.World` | Pose pipeline in `Atlas.Scan` | C |
| 4 Kernel | `IWalkerKernel.Step(double2, bool)` | `Atlas.Kernel` | `FlatWalkerKernel` | B |
| 5 Space | `ISpaceModule.Tick(in frame)` | `Atlas.World` | The entered module; skipped when none | space owner |
| 6 Upload | `IUploadStep.Upload(in frame)`: the module first, then the registered hooks | `Atlas.World` | `AtlasLook` (`_HeadPosWS`, `_HypL`, fog cross-fade, `_AtlasTime`, `_AtlasFade`), `PortalRenderer`, `StereoRig` (prediction offset); lane B's tile renderer later | A, B |
| 7 Render | Unity and URP | – | Eye cameras into their textures, one lens blit to the screen (§9) | A |
| 8a State | `ISpaceModule.WriteState(ref state)`, `IStateSink.Send(in state)` | `Atlas.World` | The module; lane D's link glue in `Atlas.Telemetry` | space owner, D |
| 8b After the frame | `IAfterFrameStep.AfterFrame(in frame)`, each in order | `Atlas.World` | In this order: lane D's perf probe (samples counters); lane D's link glue (drains received parameters and commands; commands go to `IOperatorCommands`); `QualityLadder`; `SpaceLab` (advances a switch) | D, A |

Rules:

1. Steps 1 to 6 are plain calls in this order from one `Update`. Nothing in them waits for the network, a file or
   a job that was not scheduled in the same frame.
2. Step 2 runs every frame from the first, in every display mode and during every fade and load.
   A device build with no guard registered is a boot error: the app shows "Bump Guard missing" and refuses
   headset mode (decided here). In the Editor a missing guard is a warning.
3. Step 4 is called exactly once per frame, also while tracking is lost and also with no space entered.
   `IWalkerKernel.StepCount` rises by one per frame (`P1-B02`).
4. A missing optional hook has a neutral default: no sense step → the raw pose is read from
   `AtlasBootContext.Head`; no filter → the geometry pose is the raw pose; no sink → no State.
5. Step 8a runs in `LateUpdate` of the same component (§7.2 of the plan). Step 8b runs from
   `RenderPipelineManager.endContextRendering`, after both eyes and the lens blit have been submitted; the
   Editor raises that event once per view, so the loop runs 8b only for the first one of a frame (decided here).
6. Received parameters and commands are applied at 8b and take effect in the next frame. A parameter handler may
   swap eye-texture sets, rebuild geometry or start a switch there; it must not block.
7. Hooks are added during boot only. Adding or removing one after `AtlasServices.Booted` is an error.
8. Nobody else has an `Update` that does per-frame work on the frame path. Systems created by an installer may
   have `Update` for work that is not on the frame path (file writers, reconnect timers).

`CommandRouter` (`Atlas.App`, lane A) implements `IOperatorCommands`:

| Command | Local call |
|---|---|
| `SwitchSpace(space, variant)` | Sets the variant's parameter (§14), then `ISpaceHost.RequestSwitch(space)` |
| `Home()` | `ISpaceHost.ReturnHome()` |
| `Recentre()` | `IFrameLoop.ResetWalkerToFloor()`, then lane C's recentre entry point if its page has one |
| `SetQuality(tier)` | Sets `thermal.pinTier` (§9.5) |
| `Cue()` | `BumpCue.Fire(BumpLevel.Strong, direction)` with the direction lane C's page gives for an operator cue |
| `SetLock(on)` | Sets the registry's demo lock (lane D's page) |

Each takes effect within 250 ms (test L10) and is written to the event log by the caller.

---

## 8 · Composition root

`Atlas.App.AtlasBootstrap` builds everything that is not in `Atlas_Boot.unity` (README F3).

```csharp
// File: App/AtlasBootstrap.cs
namespace Atlas.App
{
    public static class AtlasBootstrap
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot();
    }
}
```

`Boot` runs only when the scene that was loaded is `Atlas_Boot`, or is named `Sandbox_…` and holds an object
named `AtlasBoot` (decided here). In any other scene it does nothing, so EditMode and PlayMode tests are not
disturbed.

### 8.1 Objects

```text
AR Session                      from the scene
XR Origin (Mobile AR)           from the scene; moved every frame by the pose pipeline (README F4)
└─ Camera Offset
   └─ Main Camera               from the scene: the tracked device camera. TrackedPoseDriver on Update only,
      │                         ARCameraManager. Its Camera draws only in hand mode (section 9.2)
      └─ Atlas Head             the eye midpoint: local position = head offset (section 9.2). What every lane reads
         └─ Atlas Eyes          local rotation = the gyro prediction offset; identity when off
            ├─ Eye Left         Camera, local x = -ipd / 2
            ├─ Eye Right        Camera, local x = +ipd / 2
            └─ Hud              head-locked content of lanes C and D; layer AtlasHud
Atlas                           created by AtlasBootstrap; DontDestroyOnLoad
├─ Systems                      one child per system, created by its installer
├─ Room                         room content (lane C); identity transform = the room frame
├─ Space                        parent of a module created by a factory
└─ Screen                       the Screen camera with LensBlit
```

```csharp
// File: World/Services/AtlasBootContext.cs
namespace Atlas.World
{
    /// <summary>What an installer gets from the composition root.</summary>
    public sealed class AtlasBootContext
    {
        public UnityEngine.Transform Root;          // "Atlas"
        public UnityEngine.Transform Systems;       // "Atlas/Systems"
        public UnityEngine.Transform RoomRoot;      // "Atlas/Room"
        public UnityEngine.Transform SpaceRoot;     // "Atlas/Space"
        public UnityEngine.Transform XrOrigin;      // null in the Spectator
        public UnityEngine.Transform TrackedCamera; // "Main Camera"; null in the Spectator
        public UnityEngine.Transform Head;          // "Atlas Head": the head every lane reads
        public UnityEngine.Transform Hud;           // head-locked parent, layer AtlasHud
        public Atlas.Link.ParamRegistry Params;
        public bool IsSpectator;                    // built with ATLAS_SPECTATOR
        public bool IsDevice;                       // iOS player, not the Editor
    }
}
```

### 8.2 Creation order

Each lane exposes one static installer per assembly (decided here):
`public static void Install(Atlas.World.AtlasBootContext boot)`. It creates its systems under `boot.Systems`,
publishes its services and adds its frame hooks. `Atlas.Input` cannot see `AtlasBootContext`, so its installer is
`Install(UnityEngine.Transform parent)` and returns the input service, which the root publishes. Class names of
installers outside lane A are those of their pages; the order is binding.

| # | What is created | By | Lane | Under | Publishes · hooks |
|---|---|---|---|---|---|
| 0 | `Application.targetFrameRate = 60`, `Screen.sleepTimeout = NeverSleep` | `Atlas.Rendering.AtlasBoot`, `BeforeSceneLoad` (exists) | A | – | – |
| 0 | Space registrations | each space assembly, `BeforeSceneLoad` (§5.1) | A, B, C | – | `SpaceRegistry` |
| 1 | `AtlasServices.Clear()`; the `Atlas` objects of §8.1; `AtlasBootContext` | `AtlasBootstrap` | A | – | – |
| 2 | `ParamRegistry` | `AtlasBootstrap` | A (type: D) | – | `ParamRegistry` |
| 3 | Look: shaders found, palette built, shader globals at their defaults, `RenderSettings.skybox` | `AtlasPalette`, `AtlasLook` | A | – | – |
| 4 | `MaterialPool` (256 instances) | `AtlasBootstrap` | A | – | `MaterialPool` |
| 5 | `StereoRig`: `Atlas Head`, `Atlas Eyes`, eye cameras, `Hud` | `StereoRig` | A | `Main Camera` | `IStereoRig` |
| 6 | `EyeTextures` (four sets), `Screen` camera, `LensBlit` | `AtlasBootstrap` | A | `Atlas/Screen` | – |
| 7 | `PortalRenderer` | `AtlasBootstrap` | A | – | `PortalRenderer`; `IUploadStep` at step 14 |
| 8 | `AtlasFrameLoop` (disabled until step 20) | `AtlasBootstrap` | A | `Systems/FrameLoop` | `IFrameLoop` |
| 9 | Kernel: `new FlatWalkerKernel()` | `AtlasBootstrap` | A (type: B) | – | `IWalkerKernel` |
| 10 | Input | lane C's input installer | C | `Systems/Input` | input service; `InputSenseStep` |
| 11 | AR session set-up, pose pipeline, scan session, photo capture, world map | `Atlas.Scan` installer | C | `Systems/Scan` | `ISenseStep`, `IPoseFilterStep`; its services |
| 12 | Bump Guard and its cue (the cue under `boot.Hud`) | `Atlas.Safety` installer | C | `Systems/BumpGuard` | `IBumpGuard`; the root wraps it in `BumpGuardStep` → `SetGuard` |
| 13 | Room content | room content installer in `Atlas.Rendering` (lane C files) | C | `Atlas/Room` | `IRoomContent` |
| 14 | Upload hooks of lane A, in this order: `AtlasLook`, `PortalRenderer`, `StereoRig` | `AtlasBootstrap` | A | – | `AddUpload` × 3 |
| 15 | `QualityLadder`; `RenderParams` and `QualityBinding` declare the parameters of §9.6 | `AtlasBootstrap` | A | `Systems/Quality` | `IQualityState` |
| 16 | `SpaceLab`; `SpaceRegistry.DeclareAllParams(registry)` | `AtlasBootstrap` | A | `Systems/SpaceLab` | `ISpaceHost` |
| 17 | `CommandRouter` | `AtlasBootstrap` | A | – | `IOperatorCommands` |
| 18 | Link client, parameter binding scan, perf probe, HUD, event log, asset client | `Atlas.Telemetry` installer | D | `Systems/Link`, `Systems/Telemetry`; HUD under `boot.Hud` | `IStateSink`, `IPerfSource`; `AddAfterFrame` × 2 (probe, then link) |
| 19 | On-device menu; onboarding | installers in `App/Menu` (D) and `App/Onboarding` (C) | D, C | `Systems/Menu`, `Systems/Onboarding` | – |
| 20 | `AddAfterFrame(QualityLadder)`, `AddAfterFrame(SpaceLab)`; the frame loop is enabled; `AtlasServices.Booted` | `AtlasBootstrap` | A | – | – |

After step 20 onboarding owns the flow: permissions, pairing, scan, outline confirmation, headset prompt, then
`ISpaceHost.SetRoom(room)`, `IStereoRig.SetMode(DisplayMode.Headset)` and `ISpaceHost.RequestSwitch(SpaceId.Home)`.
The display starts in `DisplayMode.Hand` (§9.2).

In the Spectator build (`ATLAS_SPECTATOR`) steps 5, 6, 10 to 12, 17 and 19 are skipped; lane D's page says what
replaces them. Everything a space uses (steps 2 to 4, 7 to 9, 13 to 16) is present.

No `Sys_*` scene is loaded. §9.3 of the plan reserves `Sys_Scan`, `Sys_BumpGuard`, `Sys_Onboarding`, `Sys_Link`
and `Sys_Hud` for their owners; README F3 replaces them with installers, so they are not created (§12.3).

---

## 9 · Rendering facade (`Atlas.Rendering`)

Engine facts used below are verified in [unity-api/urp-rendering.md](unity-api/urp-rendering.md); section numbers
of that page are given as "U §n".

### 9.1 Render set-up

One URP asset, `Rendering/Settings/Atlas_URP_iOS.asset`, with one renderer, `Atlas_URP_iOS_Renderer.asset`. The
tiers of `P1-A03` are rows of the ladder table (§9.5), not separate URP assets (decided here): a tier changes the
eye-texture set and three numbers, and nothing in the URP asset.

| Setting | Value | Source |
|---|---|---|
| Path | Forward, Render Graph (the only path in URP 17.3, U §2) | §8.2 |
| HDR, depth texture, opaque texture, depth priming, post-processing, FXAA, STP | off; `postProcessData` null | §8.2, C.4 |
| Main light, additional lights, shadows | disabled, written through `SerializedObject` (U §3) | C.4 |
| URP asset MSAA | **4×**. It must be above 1 or the eye texture's own sample count is ignored (U §6.2) | C.4 |
| URP render scale | **1.0**. The eye-texture size carries the scale | §8.2, C.4 |
| SRP Batcher | on | C.4 |
| GPU Resident Drawer, dynamic and static batching, baked occlusion culling | off | §8.2 |
| Renderer `intermediateTextureMode` | `Auto`. With `Always` any active feature costs a full-screen copy per eye (U §4) | §8.2 check |
| Renderer `defaultStencilState.overrideStencilState` | false | U §4 |
| Renderer features | Those of §12.2 only. No AR background feature on this renderer in headset mode (§9.2) | – |
| Screen MSAA | `Screen.SetMSAASamples(1)` at boot; `Camera.allowMSAA = false` on the Screen camera (U §6.3) | – |
| Watermark | `Watermark.showDeveloperWatermark = false` at boot, or URP stores the MSAA colour (U §6.4) | – |
| Frame rate | `Application.targetFrameRate = 60`; VSync count 1; ProMotion off; "Adjust iOS FPS based on thermal state" off | §8.2, C.4 |
| AR session | `ARSession.matchFrameRateRequested = false`, or the session overwrites the frame rate every update (U §12.4). Set by lane C's AR set-up | – |
| Unity fog, ambient probes, reflection probes | off; renderers use `lightProbeUsage = Off`, `reflectionProbeUsage = Off` | D23 |

`AtlasProjectSetup` (lane A, Editor) writes every row that lives in an asset or in Project Settings.

**Rule for every lane: nothing may break the one native pass per eye** (§8.2). No second renderer feature, no
`ScriptableRenderPass` that reads colour or depth, no `ConfigureInput`, no `requiresIntermediateTexture`, no camera
stacking, no `Camera.rect` other than full, no `OnRenderImage`, no shader that samples `_CameraOpaqueTexture` or
`_CameraDepthTexture`, no extra camera in headset mode. A new pass is a request to lane A.

### 9.2 Stereo rig

```csharp
// File: Rendering/Stereo/IStereoRig.cs
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    public enum DisplayMode
    {
        /// <summary>Phone in the hand: the AR camera draws full screen, mono, with the camera image (scan, onboarding).</summary>
        Hand = 0,
        /// <summary>Phone in the headset: two eye cameras, eye textures, one lens blit. No camera image.</summary>
        Headset = 1,
    }

    public enum Eye { Left = 0, Right = 1 }

    /// <summary>The two-camera rig under the tracked head (Master Plan D7, task P1-A04).</summary>
    public interface IStereoRig
    {
        DisplayMode Mode { get; }
        /// <summary>Takes effect at the start of the next frame.</summary>
        void SetMode(DisplayMode mode);

        /// <summary>Distance between the eye cameras. Metres. Clamped to 0.055 .. 0.075. Default 0.064.</summary>
        float Ipd { get; set; }
        /// <summary>Position of the eye midpoint in the tracked camera's local frame. Metres. Default (0, 0, 0).</summary>
        float3 HeadOffset { get; set; }
        /// <summary>Extra rotation of the eyes about the head, in head space: the gyro prediction. Identity = off.</summary>
        quaternion PredictionOffset { get; set; }

        /// <summary>"Atlas Head": the eye midpoint, unpredicted. World pose = the raw head pose of the frame.</summary>
        Transform Head { get; }
        /// <summary>Head-locked parent for the warning cue, the HUD and the gaze reticle. Layer AtlasHud.</summary>
        Transform Hud { get; }
        Camera GetEyeCamera(Eye eye);

        /// <summary>World position of an eye for the current frame. Metres.</summary>
        float3 GetEyePosition(Eye eye);
        /// <summary>The six frustum planes of an eye in world space, normals inwards, in Unity's order
        /// (left, right, down, up, near, far). planes must have length 6.</summary>
        void GetEyeFrustumPlanes(Eye eye, Plane[] planes);
    }
}
```

| Item | Value | Source |
|---|---|---|
| Eye offset | Local x = ∓`Ipd` / 2 under `Atlas Eyes`; no toe-in, parallel axes | D7: ±32 mm |
| Projection | Symmetric. Vertical field of view **96°**, aspect **1398 / 1290** = 1.0837, so 50.3° to each side and 48° up and down | §8.5 "about 50° to each side and 48° up and down" (decided here) |
| Near, far | 0.05 m, 60 m (decided here). Fog is opaque long before the far plane (ρ·L is at most 10.6 m on the high tier) | – |
| Camera order | `Camera.depth`: Eye Left 0, Eye Right 1, Screen 10; whole numbers, because URP sorts by `(int)depth` (U §6.2) | – |
| Clear | Skybox: the background shader of §10.8. No AR camera background | §8.1 |
| Culling mask | Every layer except `XR Simulation` and `AtlasSpectator` (§12.1) | – |
| Camera data | `renderPostProcessing`, `renderShadows`, `requiresDepthTexture`, `requiresColorTexture`, `allowXRRendering` all false; `antialiasing` none; `renderType` Base | U §5 |
| Stereo API of `Camera` | Not used: no `stereoTargetEye`, no stereo matrices (U §12.1) | – |

Display modes (decided here):

| | `Hand` | `Headset` |
|---|---|---|
| `Main Camera` (AR) | `Camera` and `ARCameraBackground` enabled; draws the camera image and scan visuals full screen | `Camera` and `ARCameraBackground` disabled. `ARCameraManager` and `TrackedPoseDriver` keep running |
| Eye cameras, Screen camera | disabled | enabled |
| Who switches | The app starts in `Hand`. Onboarding calls `SetMode(Headset)` at the headset prompt and `SetMode(Hand)` for a rescan | |
| UI | Screen-space UI allowed | **No screen-space overlay canvas may be active.** In-headset UI is world-space under `Hud` |
| Frame loop | Runs in full (Bump Guard included) | Runs in full |

The head every lane reads is `Atlas Head` (`AtlasBootContext.Head`), not `Main Camera`. `HeadOffset` moves it
from the device camera to the midpoint between the walker's eyes; the offset depends on the headset and the phone
and is zero until measured (open item 2).

### 9.3 Eye textures

```csharp
// File: Rendering/Stereo/EyeTextures.cs
namespace Atlas.Rendering
{
    /// <summary>The eye render textures: four sets allocated once at boot (Master Plan sections 8.2, 8.6).</summary>
    public sealed class EyeTextures
    {
        public const int HalfScreenWidth = 1398;     // half of 2796 px, iPhone 14 and 15 Pro Max in landscape
        public const int HalfScreenHeight = 1290;

        public float RenderScale { get; }            // of the active set: 0.8, 0.7 or 0.6
        public int Msaa { get; }                     // of the active set: 4, 2 or 1
        public UnityEngine.RenderTexture Left { get; }
        public UnityEngine.RenderTexture Right { get; }

        /// <summary>
        /// Makes the set for (renderScale, msaa) the target of the eye cameras and the source of the lens blit.
        /// A preallocated set is swapped in with no allocation. Any other pair re-creates one custom set.
        /// renderScale snaps to 0.6, 0.7 or 0.8; msaa to 1, 2 or 4.
        /// </summary>
        public void Select(float renderScale, int msaa);
        /// <summary>Raised after a swap, with the new textures in place.</summary>
        public event System.Action Changed;
    }
}
```

| Set | Render scale | Size, px | MSAA | Memoryless | Rung that uses it | Resolved colour, both eyes |
|---|---|---|---|---|---|---|
| 0 | 0.8 | 1118 × 1032 | 4× | `MSAA \| Depth` | Nominal, High | 9.2 MB |
| 1 | 0.7 | 978 × 904 | 4× | `MSAA \| Depth` | Fair | 7.1 MB |
| 2 | 0.6 | 838 × 774 | 2× | `MSAA \| Depth` | Serious | 5.2 MB |
| 3 | 0.6 | 838 × 774 | off (1) | `Depth` | Critical | 5.2 MB |

- Size = 2 · round(1398 · scale / 2) by 2 · round(1290 · scale / 2) (decided here: even sizes; the 0.8 row is
  the plan's 1118 × 1032).
- The half-screen size is the constant 1398 × 1290, not `Screen.width / 2`: both phones have a 2796 × 1290
  screen, and `Screen.width` is not trusted in the first frame (U §12.3). In the Editor the same sizes are used.
- Colour format `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)`: 8 bits per channel, sRGB (the project is
  linear). Depth-stencil format `SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil)`: the stencil is needed
  for portals (U §6.1, §11). No mipmaps, `bindMS` false, clamp, bilinear.
- **Memoryless rules.** Multisampled colour and depth are never stored; only the resolved colour is. The flags
  are set only when `SystemInfo.supportsMemorylessTextures`. Set 3 has no multisampled surface, so only its depth
  is memoryless. This holds on a Metal player build; the Editor and Windows resolve through an intermediate
  texture (U §6.4).
- A `render.msaa` or `render.scale` value that matches no set (for example 0.8 with 2×) creates one custom set
  and releases the previous custom set. That allocates; the ladder never does it.
- Nobody but `EyeTextures` assigns `Camera.targetTexture`.

### 9.4 Lens blit

One full-screen pass, drawn by the `Screen` camera, samples both eye textures, applies the per-eye barrel
pre-distortion and upscales to the screen. It is the only full-screen pass in the app.

```csharp
// File: Rendering/Stereo/LensBlit.cs
namespace Atlas.Rendering
{
    /// <summary>Optics of the headset. Lengths in metres.</summary>
    public struct LensParams
    {
        public float K1;                // radial distortion, first coefficient
        public float K2;                // radial distortion, second coefficient
        public float LensSeparation;    // distance between the two lens centres
        public float ScreenToLens;      // distance from the screen to the lens
        public float VerticalOffset;    // lens centre above (+) or below (-) the screen centre
        /// <summary>K1 0.34, K2 0.55, LensSeparation 0.064, ScreenToLens 0.039, VerticalOffset 0.</summary>
        public static LensParams Default { get; }
    }

    public sealed class LensBlit : UnityEngine.MonoBehaviour
    {
        /// <summary>False: plain side by side, no distortion, each eye texture stretched over its half.</summary>
        public bool Distortion { get; set; }
        public LensParams Lens { get; set; }
        /// <summary>Physical size of the screen in landscape. Metres. From Screen.dpi; 0.1544 x 0.0712 when dpi is unknown.</summary>
        public Unity.Mathematics.float2 ScreenSizeMetres { get; }
    }
}
```

| Parameter | Default | Range in the registry | Where the default comes from |
|---|---|---|---|
| `K1` | 0.34 | 0 to 1 | Public Cardboard viewer profile of 2015 ("Cardboard v2"), used as numbers only (C.3) |
| `K2` | 0.55 | 0 to 1 | same |
| `LensSeparation` | 0.064 m | 0.055 to 0.075 | same; equals the default IPD (D7) |
| `ScreenToLens` | 0.039 m | 0.025 to 0.060 | same |
| `VerticalOffset` | 0 m | −0.02 to 0.02 | Lens centres at mid-height of the screen (decided here) |
| `Distortion` | on | 0 or 1 | §8.2; off for the Editor and for hand checks |
| Screen size | 0.1544 × 0.0712 m | – | 2796 × 1290 px at 460 px per inch |

These are placeholders until the headset is measured (open item 1). They are registry parameters (§9.6), so they
are tuned live in the headset with a straight-line grid and then baked.

The mapping, for a screen pixel at (x, y) metres from the screen centre (x right, y up), in the half of eye e:

```text
c   = ( −LensSeparation / 2 for the left eye, +LensSeparation / 2 for the right ,  VerticalOffset )
r   = ( (x, y) − c ) / ScreenToLens                    # tangent of the view angle before the lens
r'  = r · ( 1 + K1·|r|² + K2·|r|⁴ )                    # tangent of the angle the eye sees
uv  = 0.5 + r' / ( 2 · (tanH, tanV) )                  # tanV = tan(96° / 2) = 1.1106 ,  tanH = tanV · 1398 / 1290 = 1.2036
colour = eye texture e at uv,  black outside [0, 1]²
with Distortion off:  uv = ( 2·u − (0 or 1) , v )  for screen uv (u, v)
```

The eye camera's axis passes through the lens centre, so the same symmetric projection serves both eyes. The
pass writes the back buffer directly (U §7), does the sRGB conversion itself when
`Display.main.requiresSrgbBlitToBackbuffer` is true (U §11), and costs at most 1.0 ms of GPU time (`P1-A05`).

### 9.5 Quality tiers and the thermal ladder

`QualityTier`, the table and the read-only state are in `Atlas.World`, because `Atlas.Telemetry` may not
reference `Atlas.Rendering` (README F1) and needs them for the `Telemetry` row (decided here).

```csharp
// File: World/Quality/QualityTier.cs
using System;

namespace Atlas.World
{
    /// <summary>Rungs of the thermal ladder (Master Plan section 8.6). 0 to 3 are OperatorCmd quality tiers.</summary>
    public enum QualityTier : byte
    {
        Nominal = 0,
        Fair = 1,
        Serious = 2,
        Critical = 3,
        /// <summary>For a cool phone and for video capture. Never entered automatically.</summary>
        High = 4,
    }

    /// <summary>What one rung sets.</summary>
    public readonly struct QualityRow
    {
        public readonly QualityTier Tier;
        public readonly float RenderScale;   // eye-texture set
        public readonly int Msaa;            // 4, 2 or 1
        public readonly float CullRho;       // fog radius rho, kernel units
        public readonly int GluedCopies;     // copies per side of the Glued lattice
        public readonly int FrameRate;       // 60 or 30
    }

    public static class QualityTable
    {
        public static QualityRow Row(QualityTier tier);
    }

    /// <summary>The ladder as every lane reads it.</summary>
    public interface IQualityState
    {
        QualityTier Tier { get; }
        QualityRow Row { get; }
        /// <summary>NSProcessInfo.thermalState: 0 nominal, 1 fair, 2 serious, 3 critical. 0 off the device.</summary>
        int ThermalState { get; }
        /// <summary>-1 while the ladder is automatic, else the pinned tier.</summary>
        int PinnedTier { get; }
        float RenderScale { get; }           // of the active eye-texture set
        int Msaa { get; }                    // of the active eye-texture set
        /// <summary>Raised at step 8b when the tier changes.</summary>
        event Action<QualityTier> TierChanged;
    }

    /// <summary>GPU and frame timings the ladder needs. Implemented by lane D's perf probe.</summary>
    public interface IPerfSource
    {
        /// <summary>False until 120 frames have been sampled.</summary>
        bool HasData { get; }
        /// <summary>GPU frame time p95 over the last 600 frames. Milliseconds. Refreshed once per second.</summary>
        float GpuP95Ms10s { get; }
        /// <summary>Frame time p95 over the last 600 frames. Milliseconds. Refreshed once per second.</summary>
        float FrameP95Ms10s { get; }
    }
}
```

The ladder (§8.6):

| Tier | Value | Trigger | Eye set (scale) | MSAA | ρ | Glued copies | Frame rate | Console |
|---|---|---|---|---|---|---|---|---|
| `High` | 4 | Only `thermal.pinTier = 4` | 0.8 | 4× | 3.5 | 7 × 7 | 60 | – |
| `Nominal` | 0 | – | 0.8 | 4× | 3 | 5 × 5 | 60 | – |
| `Fair` | 1 | Thermal Fair, or GPU p95 over 9 ms | 0.7 | 4× | 3 | 5 × 5 | 60 | Amber; swap at the next change of walker |
| `Serious` | 2 | Thermal Serious, or GPU p95 over 12 ms | 0.6 | 2× | 2 | 3 × 3 | 60 | "Swap phone" |
| `Critical` | 3 | Thermal Critical, or still missing 60 fps on `Serious` | 0.6 | off | 2 | 3 × 3 | 30 | "End the session" |

T³ is 3 × 3 × 3 on every rung. `State.flags` bits 4 to 5 carry the value 0 to 3; `High` is sent as 0 (it shares
the Nominal texture set). `Telemetry.tier` carries the value 0 to 4 (decided here).

Inputs and stepping rule. The rule is the engine-free `QualityLadderCore` (`Atlas.World`); `QualityLadder`
(`Atlas.Rendering`) feeds it once per second at step 8b.

```csharp
// File: World/Quality/QualityLadderCore.cs
namespace Atlas.World
{
    public struct QualityInputs
    {
        public double Time;            // s, FrameContext.Time
        public int ThermalState;       // 0..3, polled every 2 s from AtlasThermal.State
        public bool PerfValid;         // IPerfSource present and HasData
        public float GpuP95Ms;         // IPerfSource.GpuP95Ms10s
        public float FrameP95Ms;       // IPerfSource.FrameP95Ms10s
        public int PinTier;            // thermal.pinTier: -1 automatic, 0..4
    }

    public sealed class QualityLadderCore
    {
        public const double DwellSeconds = 20.0;      // at most one automatic step per 20 s (section 8.6)
        public const double ThermalPollSeconds = 2.0; // P1-A07
        public const float GpuToFairMs = 9f;          // section 8.6
        public const float GpuToSeriousMs = 12f;      // section 8.6
        public const float GpuBackToNominalMs = 6.5f; // hysteresis (proposed)
        public const float GpuBackToFairMs = 8f;      // hysteresis (proposed)
        public const float FrameMissMs = 17.2f;       // one vsync interval + 0.5 ms (section 11.2)

        public QualityTier Tier { get; }
        /// <summary>Evaluates the rule. True when Tier changed.</summary>
        public bool Step(in QualityInputs inputs);
        public void Reset(QualityTier tier = QualityTier.Nominal);
    }
}
```

```text
if PinTier ≥ 0:   Tier = PinTier at once (no dwell; within 1 s, P1-A07)                       # Console, menu
else, and only when Time − time of the last automatic step ≥ 20 s, move ONE rung:
  down (worse):  Nominal  → Fair      if thermal ≥ 1  or  GPU p95 > 9 ms
                 Fair     → Serious   if thermal ≥ 2  or  GPU p95 > 12 ms
                 Serious  → Critical  if thermal = 3  or  frame p95 > 17.2 ms               # "still missing 60 fps"
  up (better):   Fair     → Nominal   if thermal = 0  and  GPU p95 < 6.5 ms
                 Serious  → Fair      if thermal ≤ 1  and  GPU p95 < 8.0 ms
                 Critical → (never automatically; only by a pin or Reset at a new session)
  a down condition is tested first; with PerfValid false only the thermal terms count
  leaving a pin (PinTier back to −1) starts from the pinned rung, or from Nominal after High
```

- Hysteresis: the thermal state changes slowly by itself; the GPU term steps up only well below the threshold it
  stepped down at (9 ms at scale 0.8 is about 6.9 ms at scale 0.7). The two "back" values are proposed and are
  confirmed at G4.
- A step allocates nothing: it swaps to a preallocated texture set and writes four numbers.
- On `Critical` the ladder sets `Application.targetFrameRate = 30`, the HUD shows "Session over" and the event
  `quality.critical` is logged. `State` then goes out at 30 Hz, one per frame (§7.2 of the plan).
- A tier takes effect like this (README F6: `Atlas.Rendering` does not see the registry): `QualityLadder` raises
  `TierChanged`; `Atlas.App.QualityBinding` writes the row's values into `render.scale`, `render.msaa`,
  `curved.cullRho` and `glued.copies`; the parameters' own handlers apply them (`EyeTextures.Select`,
  `IWalkerKernel.CullRho`, the Glued module). The registry therefore always shows what is in force, and
  `State.p[2]` follows. Between two tier changes a manual change of one of the four stands (§12.2 of the plan:
  "the Console can pin any rung").

### 9.6 Parameters declared by the composition root

`Atlas.App.RenderParams` and `QualityBinding` declare these at boot and bind them to the plain properties above.
Numeric ids are on lane D's page; the names, ranges and defaults are binding here.

| Name | Min | Max | Default | Unit | `onRelease` | Drives |
|---|---|---|---|---|---|---|
| `render.ipd` | 0.055 | 0.075 | 0.064 | m | no | `IStereoRig.Ipd` |
| `render.scale` | 0.6 | 0.8 | 0.8 | – (0.6, 0.7, 0.8) | yes | `EyeTextures.Select` |
| `render.msaa` | 1 | 4 | 4 | samples (1, 2, 4) | yes | `EyeTextures.Select` |
| `render.lineWidth` | 0.5 | 3 | 1.5 | px | no | `AtlasLook.SetLineWidth` |
| `render.lens` | 0 | 1 | 1 | off, on | no | `LensBlit.Distortion` |
| `render.lensK1` | 0 | 1 | 0.34 | – | no | `LensParams.K1` |
| `render.lensK2` | 0 | 1 | 0.55 | – | no | `LensParams.K2` |
| `render.lensSeparation` | 0.055 | 0.075 | 0.064 | m | no | `LensParams.LensSeparation` |
| `render.screenToLens` | 0.025 | 0.060 | 0.039 | m | no | `LensParams.ScreenToLens` |
| `render.lensOffsetY` | −0.02 | 0.02 | 0 | m | no | `LensParams.VerticalOffset` |
| `render.headOffsetX`, `…Y`, `…Z` | −0.10 | 0.10 | 0 | m | no | `IStereoRig.HeadOffset` |
| `thermal.pinTier` | −1 | 4 | −1 | tier | yes | `QualityInputs.PinTier` |
| `space.id` | 0 | 6 | 0 | space | yes | `ISpaceHost.RequestSwitch`; written back after every switch |
| `curved.cullRho` | 2 | 3.5 | 3 | kernel units | no | `IWalkerKernel.CullRho`; written by the ladder |
| `glued.copies` | 3 | 7 | 5 | per side (3, 5, 7) | yes | Glued lattice; written by the ladder |

`render.msaa` gains the value 1 and `glued.copies` the value 7 against §12.4, for the `Critical` and `High` rungs
(decided here). `pose.gyroPredict` is lane C's parameter; the composition root copies the pipeline's prediction
rotation into `IStereoRig.PredictionOffset` at step 6.

---

## 10 · Shader contract

### 10.1 Files and owners

All under `Rendering/Shaders/`.

| File | Shader name | Owner | Does |
|---|---|---|---|
| `AtlasSurface.shader` | `Atlas/Surface` | A | The one surface shader. One pass, `LightMode = UniversalForward`. Holds the vertex and fragment programs of §10.5 |
| `AtlasCore.hlsl` | – | A | The `UnityPerMaterial` block, the globals, palette lookup, vertex shade, lines, fog, fade, dither |
| `AtlasWarp.hlsl` | – | B | `AtlasWarp`: object position in, world position out. Identity in this build (lane B §5) |
| `AtlasPortalClip.hlsl` | – | A | `AtlasPortalClip`: clip against a portal plane |
| `AtlasGates.hlsl` | – | B | `AtlasGatesClip`: clip against up to four sight-line gates. No clip in this build (lane B §6) |
| `AtlasBackground.shader` | `Hidden/Atlas/Background` | A | The gradient background, used as the skybox material |
| `AtlasPortalMask.shader` | `Hidden/Atlas/PortalMask` | A | Doorway mask: stencil mark and depth reset |
| `AtlasLensBlit.shader` | `Hidden/Atlas/LensBlit` | A | The lens blit of §9.4 |
| `../Look/AtlasShaderIDs.cs` | – | A, with B | Every property id (§10.6) |

- An include is edited only by its owner. `AtlasShaderIDs.cs` is edited by lane A in this build; lane B's ids
  are already in it. An id is added or changed only after all four lanes are told (§6.4).
- The four shaders reach the player through `Rendering/Resources/AtlasShaderSet.asset`, a `ScriptableObject`
  that references them, written by `AtlasProjectSetup` and loaded with `Resources.Load` at boot (decided here;
  `Shader.Find` is not relied on, U §9). **No other lane loads a shader or creates a `Material`.** Materials come
  from `MaterialPool`.
- No keyword, no `multi_compile`, no `shader_feature` in any of them: one variant per pass (`Shader_Contract`:
  at most 8). A new keyword is a request to lane A (README F7).
- `Atlas/Surface` has no shadow, depth-only, meta or motion-vector pass. With the settings of §9.1 URP draws
  none of them (U §9).

Include order inside `AtlasSurface.shader`, binding because the later files use what the earlier ones declare:

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "AtlasCore.hlsl"         // block, globals, look functions
#include "AtlasWarp.hlsl"         // uses _WarpMode, _Anchor, _HeadPosWS, _HypL
#include "AtlasPortalClip.hlsl"   // uses _PortalPlane
#include "AtlasGates.hlsl"        // uses _Gate0 .. _Gate3
```

### 10.2 Mesh vertex format

Every mesh drawn with `Atlas/Surface` carries these attributes with these meanings. Run-time generators emit
exactly this layout in one stream; a GLB from the Forge carries the same semantics in glTF's own encoding.

```csharp
// File: Kernel/Mesh/AtlasVertex.cs
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>The one vertex layout of the look shader. 44 bytes, one stream.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AtlasVertex
    {
        public float3 Position;   // offset  0   POSITION   Float32 x 3
        public float3 Normal;     // offset 12   NORMAL     Float32 x 3
        public uint Color;        // offset 24   COLOR      UNorm8 x 4, R in the lowest byte
        public float2 Uv0;        // offset 28   TEXCOORD0  Float32 x 2
        public float2 Uv1;        // offset 36   TEXCOORD1  Float32 x 2

        /// <summary>R = shade, G = line weight, B = 255, A = 255.</summary>
        public static uint PackColor(byte shade, byte lineWeight = 255);
    }
}
```

| Attribute | Type | Meaning |
|---|---|---|
| `POSITION` | float3 | Object space, metres. For a registered space the object space of a root at identity is the room frame. Under the warp (lane B) x and z are tile-local metres and y is height |
| `NORMAL` | float3 | Unit, object space. **The shader does not light by it** (D23; lane B open item 10). It is required because the CPU bakes the shade from it, and for later rim or outline terms |
| `COLOR.r` | UNorm8 | **Shade**: baked brightness of the vertex, ambient occlusion times the face shade. 255 = fully lit, open. The shader maps it to `lerp(_AtlasShade.x, 1, r)` |
| `COLOR.g` | UNorm8 | **Line weight**: 255 = lines drawn at the material's strength, 0 = no lines at this vertex |
| `COLOR.b`, `COLOR.a` | UNorm8 | Reserved. Write 255. The shader does not read them |
| `TEXCOORD0` | float2 | **Palette coordinate** (§10.7). For a flat colour, the centre of the palette cell. Along a gradient strip, u runs along the strip |
| `TEXCOORD1` | float2 | **Line coordinates**. A line is drawn wherever either component crosses an integer. The generator scales them so integers fall where lines belong: metres ÷ 0.5 for the 0.5 m floor grid, tile-edge coordinates for tile edges. Absent or (0, 0) everywhere: no lines, and the material's line strengths must be 0 |

- Vertex colour is linear data, not sRGB.
- A grey colour (s, s, s, 255) from a tool that knows only "ambient occlusion" is accepted: the shade is read
  from R, and G only matters when the material draws lines.
- Index format: `UInt16` up to 65,535 vertices, else `UInt32`. One sub-mesh. Triangles.
- One mesh is one draw call per eye. A tile shell, a room shell, a Glued cell or an object is one mesh (§8.9).

Baking the shade, so that every generator lights alike (engine-free, `Atlas.Kernel`):

```csharp
// File: Kernel/Mesh/AtlasShading.cs
using Unity.Mathematics;

namespace Atlas.Kernel
{
    public static class AtlasShading
    {
        /// <summary>Direction towards the fixed diagram light, room frame: normalize(0.35, 0.85, 0.40).</summary>
        public static readonly float3 LightDirection;
        /// <summary>Brightness of a face that points away from the light.</summary>
        public const float ShadeFloor = 0.55f;
        /// <summary>ShadeFloor + (1 - ShadeFloor) * (0.5 + 0.5 * dot(normal, LightDirection)). Normal is unit length.</summary>
        public static float FaceShade(float3 normal);
        /// <summary>The same with abs(dot): for surfaces whose winding is not known (4D slices).</summary>
        public static float FaceShadeTwoSided(float3 normal);
        /// <summary>round(255 * saturate(shade * ambientOcclusion)).</summary>
        public static byte ToByte(float shade, float ambientOcclusion = 1f);
    }
}
```

**The 0.25 m rule.** No edge longer than 0.25 m on anything that can stand in a curved space (§8.4): the room
shell, placeholder boxes, generated and built-in furniture, authored props, and the flat variant of a tile.
Exempt, because they are never warped: Glued cell frames, the virtual rooms of S5, slices of S6, anything on the
`AtlasHud` layer. `MeshData.LongestEdge()` is the test.

Helpers every generator may use:

```csharp
// File: World/Meshing/MeshData.cs   (engine-free)
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>A growable vertex and index list in the AtlasVertex layout.</summary>
    public sealed class MeshData
    {
        public AtlasVertex[] Vertices;      // valid up to VertexCount
        public int VertexCount;
        public int[] Indices;               // valid up to IndexCount
        public int IndexCount;
        public float3 BoundsMin, BoundsMax; // kept up to date by the Add methods

        public MeshData(int vertexCapacity = 1024, int indexCapacity = 4096);
        public int TriangleCount { get; }
        public void Clear();
        public int AddVertex(in AtlasVertex vertex);
        public void AddTriangle(int a, int b, int c);

        /// <summary>
        /// Adds the parallelogram origin, origin + u, origin + u + v, origin + v as a grid whose cells are at most
        /// maxEdge long on both sides. Front face towards cross(v, u) (Unity's clockwise winding).
        /// uv0 is one palette coordinate for the whole quad. Line coordinates are line0 + s * lineU + t * lineV
        /// for s, t in 0..1 along u and v. shade is baked from AtlasShading.FaceShade of the face normal.
        /// </summary>
        public void AddQuadGrid(float3 origin, float3 u, float3 v, float2 uv0,
                                float2 line0, float2 lineU, float2 lineV,
                                float ambientOcclusion = 1f, byte lineWeight = 255, float maxEdge = 0.25f);

        /// <summary>A box standing on its base centre, turned by yaw about +Y (Unity's sense), five or six faces.</summary>
        public void AddBox(float3 baseCentre, float3 size, float yaw, float2 uv0,
                           bool withBottom = false, byte lineWeight = 0, float maxEdge = 0.25f);

        /// <summary>Length of the longest triangle edge. Metres.</summary>
        public float LongestEdge();
    }
}
```

```csharp
// File: Rendering/Look/AtlasMesh.cs
namespace Atlas.Rendering
{
    public static class AtlasMesh
    {
        /// <summary>Position, Normal, Color (UNorm8 x 4), TexCoord0, TexCoord1; all in stream 0.</summary>
        public static readonly UnityEngine.Rendering.VertexAttributeDescriptor[] Layout;
        /// <summary>A new Mesh with the data, bounds from the data, UInt16 or UInt32 indices as needed.</summary>
        public static UnityEngine.Mesh Create(Atlas.World.MeshData data, string name);
        /// <summary>Replaces the contents of an existing mesh. Allocates nothing when the counts fit.</summary>
        public static void Apply(Atlas.World.MeshData data, UnityEngine.Mesh mesh);
        /// <summary>For meshes rewritten every frame: fixed capacity, UInt32 indices, MarkDynamic.</summary>
        public static UnityEngine.Mesh CreateDynamic(string name, int vertexCapacity, int indexCapacity);
        /// <summary>Uploads the first counts of the arrays and sets the bounds. No allocation.</summary>
        public static void Upload(UnityEngine.Mesh mesh,
                                  Unity.Collections.NativeArray<Atlas.Kernel.AtlasVertex> vertices, int vertexCount,
                                  Unity.Collections.NativeArray<int> indices, int indexCount,
                                  UnityEngine.Bounds bounds);
    }
}
```

### 10.3 The `UnityPerMaterial` block

The single block, declared in `AtlasCore.hlsl` and nowhere else. Order and types are binding.

```hlsl
CBUFFER_START(UnityPerMaterial)
    float4x4 _Anchor;         //   0  B  tile, room or object anchor of the warp (lane B section 5). Identity
    float4   _BaseTint;       //  64  A  rgb multiplies the palette colour, linear; a = opacity 0..1, dithered
    float4   _LineColor;      //  80  A  rgb line colour, linear; a unused
    float4   _LineParams;     //  96  A  x minor width, y major width (multiples of _AtlasLineWidth);
                              //         z minor strength 0..1, w major strength 0..1
    float4   _LineMajor;      // 112  A  xy = period of major lines in line-coordinate units (0 = none); zw unused
    float4   _PortalPlane;    // 128  A  world-space plane (n.xyz, d); fragments with dot(n, p) + d < 0 are clipped
    float4   _Gate0;          // 144  B  sight-line gates (lane B section 6): (post.x, post.z, sign, unused)
    float4   _Gate1;          // 160  B
    float4   _Gate2;          // 176  B
    float4   _Gate3;          // 192  B
    float4   _SurfaceParams;  // 208  A  x emissive gain; y fog weight 0..1; z near-fade distance, metres (0 = off);
                              //         w fade weight: 1 follows _AtlasFade, 0 ignores it
    float    _WarpMode;       // 224  B  0 none, 1 hyperbolic, 2 spherical (lane B section 5)
CBUFFER_END
```

| Property | Default (set by `MaterialPool`) | Declared in `Properties` |
|---|---|---|
| `_Anchor` | identity | no (matrices cannot be) |
| `_BaseTint` | (1, 1, 1, 1) | yes |
| `_LineColor` | palette `GridLine` in linear: (0.159, 0.392, 0.578, 1) | yes |
| `_LineParams` | (1, 2, 0, 0): lines off | yes |
| `_LineMajor` | (0, 0, 0, 0) | yes |
| `_PortalPlane` | (0, 0, 0, 1): never clips | yes |
| `_Gate0` to `_Gate3` | (0, 0, 0, 0): off | yes |
| `_SurfaceParams` | (1, 1, 0, 1) | yes |
| `_WarpMode` | 0 | yes |
| `_Cull` | 2 (Back); 0 = Off, 1 = Front | yes. Render state only (`Cull [_Cull]`), not in the block |

There is no per-material texture: the palette is a global. Per-tile, per-room and per-object data live in this
block, one pooled material instance each. Camera-wide data are globals. **No `MaterialPropertyBlock`** (D14).

### 10.4 Globals

Declared in `AtlasCore.hlsl` outside the block. Set only by the owner named; everyone may read them in a shader.

| Name | HLSL type | Meaning | Default | Set by | When |
|---|---|---|---|---|---|
| `_AtlasPalette` | `Texture2D` + `sampler_AtlasPalette` | The palette (§10.7) | – | `AtlasPalette` | boot |
| `_HeadPosWS` | `float4` | xyz = raw head position, world, metres: the eye midpoint, one value for both eyes. w unused | (0, 1.6, 0, 0) | `AtlasLook.Upload` | step 6 |
| `_HypL` | `float` | `IWalkerKernel.MetresPerUnit`: L, or R on the sphere. Metres per kernel unit, above 0 | 1 | `AtlasLook.Upload` | step 6 |
| `_AtlasFogColor` | `float4` | rgb linear. Also the colour of the background at and below the horizon | palette `FogBase` | `AtlasLook` | on change; cross-fade at step 6 |
| `_AtlasFogParams` | `float4` | x = fog start, metres from the head on the floor; y = 1 / (end − start); z, w unused | (8, 1/6, 0, 0) | `AtlasLook.SetFog` | on change |
| `_AtlasSkyTop` | `float4` | rgb linear: the background at the zenith | palette `SkyTop` | `AtlasLook` | on change |
| `_AtlasShade` | `float4` | x = darkest shade a vertex can have (0.25); y = global emissive gain (1); z, w unused | (0.25, 1, 0, 0) | `AtlasLook` | boot |
| `_AtlasLineWidth` | `float` | Line width unit, pixels of the eye texture | 1.5 | `AtlasLook.SetLineWidth` | on change |
| `_AtlasFade` | `float` | 1 = world visible, 0 = world black. Scales surfaces whose fade weight is 1, and the background | 1 | `AtlasLook.SetFade` (SpaceLab only) | step 6 |
| `_AtlasTime` | `float` | `FrameContext.Time` modulo 3,600, seconds. For pulsing accents | 0 | `AtlasLook.Upload` | step 6 |

URP's own globals (`_WorldSpaceCameraPos`, the view and projection matrices) are per eye and may be read. The
fog, the warp and the head-centred effects use `_HeadPosWS`, never the eye position, so both eyes agree.

### 10.5 Entry points

```hlsl
// ---- AtlasCore.hlsl (lane A) -----------------------------------------------------------------------------

// Linear rgb of the palette at uv0. Samples _AtlasPalette at mip 0.
half3 AtlasPaletteColor(float2 uv0);

// True when uv0 lies in the emissive block of the palette (uv0.y >= 0.75).
bool AtlasIsEmissive(float2 uv0);

// Coverage 0..1 of the grid and edge lines at these line coordinates. Anti-aliased with fwidth.
// lineWeight is COLOR.g. Returns 0 when both strengths in _LineParams are 0.
half AtlasLineMask(float2 lineCoord, half lineWeight);

// Fog amount 0..1 from the horizontal distance between positionWS and _HeadPosWS, scaled by _SurfaceParams.y.
half AtlasFogFactor(float3 positionWS);

// Ordered dither: clips the fragment when opacity is below a 4 x 4 Bayer threshold at this pixel.
void AtlasDitherClip(float4 positionCS, half opacity);

// The background gradient for a view direction (unit, world): _AtlasFogColor at and below the horizon,
// _AtlasSkyTop at the zenith, both scaled by _AtlasFade.
half3 AtlasBackground(float3 viewDirWS);

// The whole look for one fragment. Returns the final colour, alpha 1.
//   positionWS  world position, metres          uv0        palette coordinate
//   lineCoord   TEXCOORD1                       vertexColor COLOR (r shade, g line weight)
//   positionCS  SV_POSITION, for the dither
half4 AtlasShade(float3 positionWS, float2 uv0, float2 lineCoord, half4 vertexColor, float4 positionCS);

// ---- AtlasWarp.hlsl (lane B) -----------------------------------------------------------------------------
float3 AtlasWarp(float3 positionOS);          // vertex stage: object position in, world position out

// ---- AtlasPortalClip.hlsl (lane A) -----------------------------------------------------------------------
void AtlasPortalClip(float3 positionWS);      // fragment stage: clip(dot(_PortalPlane.xyz, positionWS) + _PortalPlane.w)

// ---- AtlasGates.hlsl (lane B) ----------------------------------------------------------------------------
void AtlasGatesClip(float3 positionWS);       // fragment stage: clip against the enabled gates
```

The programs in `AtlasSurface.shader`:

```hlsl
struct AtlasAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4  color      : COLOR;
    float2 uv0        : TEXCOORD0;
    float2 uv1        : TEXCOORD1;
};

struct AtlasVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float2 uv0        : TEXCOORD1;
    float2 lineCoord  : TEXCOORD2;
    half4  color      : COLOR;
};

AtlasVaryings AtlasVert(AtlasAttributes input)
{
    AtlasVaryings output;
    float3 positionWS = AtlasWarp(input.positionOS);          // the only place a position is made
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.uv0        = input.uv0;
    output.lineCoord  = input.uv1;
    output.color      = input.color;
    return output;
}

half4 AtlasFrag(AtlasVaryings input) : SV_Target
{
    AtlasGatesClip(input.positionWS);                         // first, for every material (lane B section 6)
    AtlasPortalClip(input.positionWS);
    return AtlasShade(input.positionWS, input.uv0, input.lineCoord, input.color, input.positionCS);
}
```

What `AtlasShade` computes, in order (binding as behaviour, free as code):

```text
opacity = _BaseTint.a ;  if _SurfaceParams.z > 0:  opacity *= saturate( distance(positionWS, camera) / _SurfaceParams.z )
AtlasDitherClip(positionCS, opacity)                                   # placeholder-to-mesh cross-fade; near fade of 0.3 m
c = AtlasPaletteColor(uv0) · _BaseTint.rgb
if AtlasIsEmissive(uv0):  c *= _SurfaceParams.x · _AtlasShade.y        # emissive accent: no shade, no lines
else:                     c *= lerp(_AtlasShade.x, 1, vertexColor.r)   # baked shade
                          c  = lerp(c, _LineColor.rgb, AtlasLineMask(lineCoord, vertexColor.g))
c = lerp(c, _AtlasFogColor.rgb, AtlasFogFactor(positionWS))            # everything fades to the one fog colour
c *= lerp(1, _AtlasFade, _SurfaceParams.w)                             # space switch
```

Lines: a minor line where a component of the line coordinate crosses an integer, width `_LineParams.x ·
_AtlasLineWidth` pixels, strength `_LineParams.z`; a major line where it crosses a multiple of `_LineMajor`, width
`_LineParams.y · _AtlasLineWidth`, strength `_LineParams.w`. A line narrower than a pixel fades instead of
aliasing, so it is stable in both eyes.

### 10.6 `AtlasShaderIDs`

File `Rendering/Look/AtlasShaderIDs.cs`, namespace `Atlas.Rendering`. Each member is
`public static readonly int X = Shader.PropertyToID("…")`. This is the complete list; nobody calls
`Shader.PropertyToID` elsewhere.

| Member | Property | Kind | Set with | Owner |
|---|---|---|---|---|
| `Anchor` | `_Anchor` | per material | `Material.SetMatrix` | B |
| `WarpMode` | `_WarpMode` | per material | `Material.SetFloat` | B |
| `Gate0`, `Gate1`, `Gate2`, `Gate3` | `_Gate0` … `_Gate3` | per material | `Material.SetVector` | B |
| `BaseTint` | `_BaseTint` | per material | `Material.SetVector` | A |
| `LineColor` | `_LineColor` | per material | `Material.SetVector` | A |
| `LineParams` | `_LineParams` | per material | `Material.SetVector` | A |
| `LineMajor` | `_LineMajor` | per material | `Material.SetVector` | A |
| `PortalPlane` | `_PortalPlane` | per material | `Material.SetVector` | A |
| `SurfaceParams` | `_SurfaceParams` | per material | `Material.SetVector` | A |
| `Cull` | `_Cull` | render state | `Material.SetFloat` | A |
| `Palette` | `_AtlasPalette` | global texture | `Shader.SetGlobalTexture` | A |
| `HeadPosWS` | `_HeadPosWS` | global | `Shader.SetGlobalVector` | A sets, B defines |
| `HypL` | `_HypL` | global | `Shader.SetGlobalFloat` | A sets, B defines |
| `FogColor` | `_AtlasFogColor` | global | `Shader.SetGlobalVector` | A |
| `FogParams` | `_AtlasFogParams` | global | `Shader.SetGlobalVector` | A |
| `SkyTop` | `_AtlasSkyTop` | global | `Shader.SetGlobalVector` | A |
| `Shade` | `_AtlasShade` | global | `Shader.SetGlobalVector` | A |
| `LineWidth` | `_AtlasLineWidth` | global | `Shader.SetGlobalFloat` | A |
| `Fade` | `_AtlasFade` | global | `Shader.SetGlobalFloat` | A |
| `Time` | `_AtlasTime` | global | `Shader.SetGlobalFloat` | A |
| `StencilRef` | `_StencilRef` | render state of `Hidden/Atlas/PortalMask` | `Material.SetFloat` | A |
| `EyeLeft`, `EyeRight` | `_AtlasEyeLeft`, `_AtlasEyeRight` | lens blit textures | `Material.SetTexture` | A |
| `LensK` | `_AtlasLensK` | lens blit: (K1, K2, distortion on = 1, 0) | `Material.SetVector` | A |
| `LensGeometry` | `_AtlasLensGeometry` | lens blit: (screen width, screen height, lens separation, vertical offset), each ÷ `ScreenToLens` | `Material.SetVector` | A |
| `LensFov` | `_AtlasLensFov` | lens blit: (tanH, tanV, 0, 0) | `Material.SetVector` | A |

Vectors are passed as `Vector4`; a `float4` converts implicitly. A colour is passed linear.

### 10.7 The palette

One texture, 128 × 128, `R8G8B8A8_SRGB`, no mipmaps, no compression, bilinear, clamp. Built in code at boot by
`AtlasPalette` from the table below (README F3: no hand-painted file) and bound as `_AtlasPalette`.

```csharp
// File: Rendering/Look/AtlasPalette.cs
namespace Atlas.Rendering
{
    public static class AtlasPalette
    {
        public const int Size = 128, CellsPerSide = 16, CellPixels = 8;

        /// <summary>Palette coordinate of the centre of cell index (0..255).</summary>
        public static Unity.Mathematics.float2 Uv(int index);
        /// <summary>Coordinate on gradient strip row (0..15) at t in 0..1 along the strip.</summary>
        public static Unity.Mathematics.float2 StripUv(int row, float t);
        /// <summary>The colour of a cell in linear rgb, for values that go into _BaseTint, fog or line colour.</summary>
        public static Unity.Mathematics.float3 Linear(int index);
        /// <summary>Tint cell of an object category (0..31): CategoryBase + category.</summary>
        public static int Category(int category);
        /// <summary>Cell of region r (0..3) at lightness step t (0..3): RegionBase + 4 * r + t.</summary>
        public static int Region(int region, int step);
        /// <summary>Emissive cell of a 4D cell: Cell4DBase + (cell mod 32).</summary>
        public static int Cell4D(int cell);

        // Named cells: see the tables below. Every name there is a public const int here.
    }
}
```

Layout: 16 × 16 cells of 8 × 8 pixels. Cell index = row · 16 + column. Row 0 is the bottom row (v = 0).
`Uv(index)` = ((column + 0.5) / 16, (row + 0.5) / 16).

| Rows | Indices | Block | Content |
|---|---|---|---|
| 0 | 0 – 15 | Neutrals (the dark base) | HSV(215°, 0.25 − 0.01·i, 0.03 + 0.05·i) for i = 0 … 15 |
| 1 | 16 – 31 | Structure | Named cells, table below |
| 2 – 3 | 32 – 63 | Object category tints | `CategoryBase` = 32. Cell 32 + c for category c (lane C's order; 0 = unknown). Category 0: HSV(0°, 0, 0.45). Others: HSV((c · 137.5°) mod 360°, 0.40, 0.50) |
| 4 – 7 | 64 – 127 | Object colours | `ObjectBase` = 64. What the Forge and the built-in set may pick (`AssetReady.palette`). Row 4 + k has value V = 0.28, 0.42, 0.58, 0.74 for k = 0 … 3. Columns 0 – 11: HSV(column · 30°, 0.50, V). Column 12: HSV(30°, 0.12, V). 13: HSV(210°, 0.12, V). 14: HSV(28°, 0.35, V). 15: HSV(0°, 0, V) |
| 8 | 128 – 143 | Region hues | `RegionBase` = 128. Region r, step t: HSV(h_r, 0.45, 0.22 + 0.12·t) with h = 200°, 150°, 35°, 320° |
| 9 | 144 – 159 | Strip: warm | Continuous along u: HSV(15°, 0.55, 0.15 → 0.75) |
| 10 | 160 – 175 | Strip: cool | HSV(205°, 0.55, 0.15 → 0.75) |
| 11 | 176 – 191 | Strip: depth step | Neutral ramp, HSV(215°, 0.2, 0.40 → 0.05): lightness against copy depth |
| 12 | 192 – 207 | **Emissive** accents | Named cells, table below |
| 13 – 14 | 208 – 239 | **Emissive** 4D cell colours | `Cell4DBase` = 208. Cell k: HSV((k · 137.5°) mod 360°, 0.65, 0.95) |
| 15 | 240 – 255 | **Emissive** strip: w depth | Continuous: sRGB #FF7F6A (near) → #8E6BFF (far) |

**Emissive rule.** A fragment whose palette coordinate has v ≥ 0.75 (rows 12 to 15) is an emissive accent: full
palette colour times the emissive gain, no shade, no lines, still fogged. Nothing else is emissive. No bloom.

Named cells (sRGB values; lane A may retune a value without notice, the index and its meaning are frozen):

| Index | Name | sRGB | Used for |
|---|---|---|---|
| 0 | `Void` | neutral ramp, i = 0 | The darkest tone |
| 1 | `FogBase` | neutral ramp, i = 1 | Default fog and horizon colour |
| 3 | `SkyTop` | neutral ramp, i = 3 | Default zenith colour |
| 15 | `Text` | neutral ramp, i = 15 | Labels |
| 16 | `Floor` | #1B222B | Room floor |
| 17 | `FloorAlt` | #222B36 | Second floor tone |
| 18 | `Wall` | #2A3542 | Room wall |
| 19 | `WallAlt` | #33404F | Second wall tone |
| 20 | `Kerb` | #3A4858 | Walls sunk to kerbs |
| 21 | `Post` | #50627A | Corner posts |
| 22 | `DoorFrame` | #6B7F99 | Door and window frames |
| 23 | `Ceiling` | #161C24 | Ceilings of virtual rooms |
| 24 | `Opening` | #0E1319 | Reveal of a door or window |
| 25 | `Placeholder` | #3B4654 | Placeholder box base |
| 26 | `AvatarBody` | #7A8794 | Self-avatar |
| 27 | `AvatarLeft` | #D9534F | Self-avatar's left shoulder |
| 28 | `AvatarRight` | #3FA7D6 | Self-avatar's right shoulder |
| 29 | `Landmark` | #C9A227 | Chiral landmark |
| 30 | `Booth` | #4A3F6B | Booth of S5 |
| 31 | `VirtualWall` | #2E2A45 | Walls of a virtual room of S5 |
| 192 | `PortalFrame` | #7DF9FF | Portal frames |
| 193 | `Beacon` | #FFD166 | Towers, pole beacon |
| 194 | `HomeBeacon` | #9BE564 | Arrow to tile zero |
| 195 | `Flag` | #FF6B6B | Start flag of the 3-4-5 walk |
| 196 | `LapMarker` | #FFE66D | Lap markers |
| 197 | `CueWarn` | #FFB020 | Bump Guard WARN |
| 198 | `CueStrong` | #FF3B30 | Bump Guard STRONG |
| 199 | `CueRescan` | #5AC8FA | Rescan icon |
| 200 | `CueStandStill` | #FFFFFF | Stand-still icon |
| 201 | `LinkOk` | #34C759 | Link dot, connected |
| 202 | `LinkDegraded` | #FFCC00 | Link dot, degraded |
| 203 | `LinkOff` | #8E8E93 | Link dot, offline |
| 204 | `Reticle` | #FFFFFF | Gaze reticle |
| 205 | `SliceEdge` | #FFFFFF | Outline of a 4D slice |
| 206 | `GridLine` | #6FA8C8 | Default `_LineColor` |
| 207 | – | – | Spare |

A GLB stores v downwards and glTFast flips it on import (`com.unity.cloud.gltfast`, `Runtime/Scripts/Jobs.cs`:
`tmp.y = 1 - tmp.y`), so the Forge writes `TEXCOORD_0 = (u, 1 − v)` for a cell whose Unity coordinate is (u, v).

### 10.8 Fog, background and the look facade

| Rule | Detail |
|---|---|
| Fog distance | Horizontal: the distance on the floor between the fragment and `_HeadPosWS`. Height does not count, so the T³ copy above the walker stays visible (§8.5) and the radius equals the draw rule's |
| Fog ramp | Linear from `start` (no fog) to `end` (fully the fog colour) |
| Curved spaces | start = (ρ − 1) · L, end = ρ · L, with ρ = `curved.cullRho` and L = `MetresPerUnit` (lane B) |
| Glued | end = (n − 1) / 2 cells for an n × n lattice, start one cell nearer (half a cell at 3 × 3); a cell is the shorter cell side (§8.5) |
| Other flat spaces | The default: start 8 m, end 14 m (decided here), unless the module sets its own |
| One fog colour | Every surface fades to `_AtlasFogColor`. The background equals it at and below the horizon. Geometry at the fog radius cannot be told from the background (§8.1, G4) |
| Fog colour change | Cross-fades over 1.0 s (§8.1) |
| Background | `Hidden/Atlas/Background` as `RenderSettings.skybox`, eye cameras clear with it. `lerp(_AtlasFogColor, _AtlasSkyTop, smoothstep(0, 0.7, viewDir.y))`. No texture |
| Fade | `_AtlasFade` scales the world and the background. A material with fade weight 0 (`_SurfaceParams.w`) ignores it: the warning cue, the HUD, the reticle |
| Per-material fog | `_SurfaceParams.y` scales the fog on a material: 0 for head-locked content, 1 for everything in the world |
| Near fade | `_SurfaceParams.z` > 0 dithers a surface out within that distance of the eye. 0.3 for solid-looking things in a curved space (§3.5) |
| Transparency | None. Dither (`_BaseTint.a`) only (§8.1) |

```csharp
// File: Rendering/Look/AtlasLook.cs
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>The shader globals of the look. A space sets fog and background in Enter; SpaceLab resets them.</summary>
    public static class AtlasLook
    {
        public const float DefaultFogStart = 8f, DefaultFogEnd = 14f;   // metres
        public const float FogCrossFadeSeconds = 1f;

        /// <summary>Fog ramp in metres from the head, on the floor. end must exceed start.</summary>
        public static void SetFog(float startMetres, float endMetres);
        /// <summary>Fog and horizon colour, linear rgb. Cross-fades over FogCrossFadeSeconds unless immediate.</summary>
        public static void SetFogColor(float3 linearRgb, bool immediate = false);
        /// <summary>Zenith colour of the background, linear rgb.</summary>
        public static void SetSkyTop(float3 linearRgb);
        /// <summary>Line width unit in pixels (render.lineWidth).</summary>
        public static void SetLineWidth(float pixels);
        /// <summary>World fade 0..1. SpaceLab only.</summary>
        public static void SetFade(float fade);
        /// <summary>Defaults for a new space: default fog, FogBase and SkyTop colours, immediate. SpaceLab only.</summary>
        public static void ResetForSpace();
        /// <summary>Step 6: _HeadPosWS, _HypL (from IWalkerKernel.MetresPerUnit), _AtlasTime, the fog cross-fade.
        /// Called by the composition root's first upload hook.</summary>
        public static void Upload(in Atlas.World.FrameContext frame, double metresPerUnit);
    }
}
```

A space calls `SetFog`, `SetFogColor` and `SetSkyTop` in `Enter` or later; it never calls `Shader.SetGlobal…`
itself. Lane B's spaces set the fog from ρ and L the same way.

---

## 11 · `MaterialPool`

```csharp
// File: Rendering/Look/MaterialPool.cs
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>The only source of Atlas/Surface materials: one instance per tile, room or object (D14).</summary>
    public sealed class MaterialPool
    {
        /// <param name="surface">The Atlas/Surface shader.</param>
        /// <param name="capacity">Instances created up front. The composition root passes 256.</param>
        public MaterialPool(Shader surface, int capacity);

        /// <summary>A material with every property at the default of section 10.3. Never null.</summary>
        public Material Rent();
        /// <summary>Gives a material back. It is reset and reused; never destroy a rented material.</summary>
        public void Return(Material material);
        /// <summary>Sets every property of section 10.3 to its default.</summary>
        public static void ResetToDefaults(Material material);

        public int Capacity { get; }
        public int Rented { get; }
    }
}
```

| Rule | Detail |
|---|---|
| Assign | `renderer.sharedMaterial = pool.Rent()`. Never `renderer.material` (it clones). Never a `MaterialPropertyBlock` |
| Set values | `material.SetVector(AtlasShaderIDs.BaseTint, …)` and the like, on the rented instance |
| One per what | One instance per tile, per room shell, per object, per Glued cell copy that needs its own tint. Renderers that share every value share one instance |
| Return | In `ISpaceModule.Exit`, or when the object is destroyed. A material returned twice, or one the pool did not make, is an `ArgumentException` |
| Allocation | `Rent` and `Return` allocate nothing while `Rented` ≤ `Capacity`. Beyond it the pool doubles and logs `materials.grow` once; that is a budget defect, not a crash |
| Budget | At most 256 rented at once (decided here; S2 at ρ = 3 needs 71 tiles + 21 room materials) |
| Never saved | Pool instances are never assets (§9.3) |

---

## 12 · Layers, stencil references and scenes

### 12.1 Layers

`AtlasProjectSetup` writes the names into `ProjectSettings/TagManager.asset`. Code uses the constants.

```csharp
// File: Rendering/Look/AtlasLayers.cs
namespace Atlas.Rendering
{
    public static class AtlasLayers
    {
        public const int World = 0;            // "Default"
        public const int PortalMask = 6;       // "AtlasPortalMask"
        public const int Hud = 7;              // "AtlasHud"
        public const int Room01 = 8;           // "AtlasRoom01" .. "AtlasRoom15" = 8 .. 22
        public const int RoomCount = 15;
        public const int Spectator = 23;       // "AtlasSpectator"
        public const int XrSimulation = 30;    // "XR Simulation" (exists)

        /// <summary>Layer of the room seen through the portal that holds stencil reference 1..15.</summary>
        public static int Room(int stencilRef);   // 7 + stencilRef
        /// <summary>Sets the layer on a transform and every child. No allocation.</summary>
        public static void SetLayerRecursively(UnityEngine.Transform root, int layer);
    }
}
```

| Layer | Name | Drawn by | Who puts objects on it |
|---|---|---|---|
| 0 | `Default` | The renderer's opaque pass. **The world**: everything a space or the room content draws in the room the walker is in | Every lane; it is the default of a new object |
| 6 | `AtlasPortalMask` | Feature "Atlas Portal Masks" | `PortalRenderer` only |
| 7 | `AtlasHud` | Features "Atlas HUD": after everything, depth test off, depth write off | Lane C (cue, reticle), lane D (HUD, link dot, menu) |
| 8 – 22 | `AtlasRoom01` … `AtlasRoom15` | Feature "Atlas Room NN" with stencil Equal NN | `PortalRenderer` only: it moves a room's root between `Default` and a room layer |
| 23 | `AtlasSpectator` | Nothing on the phone. The Spectator's cameras and a map camera (lane D) | Lane D |
| 30 | `XR Simulation` | Nothing in the eye cameras | AR Foundation |
| 1 – 5, 24 – 29, 31 | Unity's own or free | Not drawn in headset mode | Nobody without asking lane A |

- The renderer's `opaqueLayerMask` and `transparentLayerMask` are `Default` only. A layer owned by a feature
  must be out of them or it is drawn twice (U §8).
- Content under `IStereoRig.Hud` on `AtlasHud` is drawn in both eyes at its real stereo depth, on top of the
  world. Keep it 1.0 to 2.0 m from the eyes. It uses `Atlas/Surface` with fog weight 0 and fade weight 0, or
  TextMeshPro. Budget: at most 20 draw calls per frame for everything on the layer (decided here).

### 12.2 Renderer features and the stencil table

Stencil values 0 to 15 only (D16). URP's cross-fade bits stay unused because LOD cross-fade is not the stencil
kind (U §8). The features live in `Atlas_URP_iOS_Renderer.asset`, added by `AtlasProjectSetup`, with
`overrideMode = None` (the default `Material` would replace the surface shader, U §8).

| Order in the eye's one pass | Feature | `RenderPassEvent` | Layer | Stencil | Depth |
|---|---|---|---|---|---|
| 1 | (URP opaque pass) | – | `Default` | not touched: 0 everywhere | normal |
| 2 | Atlas Portal Masks | `AfterRenderingOpaques` (300) | `AtlasPortalMask` | The mask shader's own: writes reference N where the doorway is visible | Marks at the doorway's depth; then resets depth to far where the stencil is N |
| 3 | Atlas Room 01 … 15 | 301 … 315 | `AtlasRoom01` … `15` | override: reference N, compare Equal, pass Keep | normal |
| 4 | (URP skybox) | 350 | – | none | Fills what is still at far depth, through portals too |
| 5 | Atlas HUD, two features: opaque queue (`Atlas/Surface`), then transparent queue (TextMeshPro) | `AfterRenderingTransparents` (500, 501) | `AtlasHud` | none | test Always, write off |

A room feature is switched with `SetActive` and is active only while its slot is in use, so a space without
portals pays for none of them (U §8).

One row per room layer and stencil reference (§6.4):

| Stencil reference | Layer index | Layer name | Feature | `RenderPassEvent` | Shows |
|---|---|---|---|---|---|
| 0 | 0 | `Default` | (URP opaque pass) | – | The room the walker is in, and every space without portals |
| 1 | 8 | `AtlasRoom01` | Atlas Room 01 | 301 | The room behind the visible doorway with the largest screen area this frame |
| 2 | 9 | `AtlasRoom02` | Atlas Room 02 | 302 | The second largest |
| 3 | 10 | `AtlasRoom03` | Atlas Room 03 | 303 | The third |
| 4 | 11 | `AtlasRoom04` | Atlas Room 04 | 304 | The fourth |
| 5 | 12 | `AtlasRoom05` | Atlas Room 05 | 305 | The fifth |
| 6 | 13 | `AtlasRoom06` | Atlas Room 06 | 306 | The sixth |
| 7 | 14 | `AtlasRoom07` | Atlas Room 07 | 307 | The seventh |
| 8 | 15 | `AtlasRoom08` | Atlas Room 08 | 308 | The eighth |
| 9 | 16 | `AtlasRoom09` | Atlas Room 09 | 309 | The ninth |
| 10 | 17 | `AtlasRoom10` | Atlas Room 10 | 310 | The tenth |
| 11 | 18 | `AtlasRoom11` | Atlas Room 11 | 311 | The eleventh |
| 12 | 19 | `AtlasRoom12` | Atlas Room 12 | 312 | The twelfth |
| 13 | 20 | `AtlasRoom13` | Atlas Room 13 | 313 | The thirteenth |
| 14 | 21 | `AtlasRoom14` | Atlas Room 14 | 314 | The fourteenth |
| 15 | 22 | `AtlasRoom15` | Atlas Room 15 | 315 | The fifteenth |

Each room feature: layer mask = its one layer, opaque queue, `overrideMode = None`, stencil override with its
reference, compare `Equal`, pass, fail and depth-fail `Keep`, no depth override.

- References are given per frame by `PortalRenderer`, largest doorway first, at most 15 (`Portal_Graph`). No
  recursion: a room seen through a doorway shows no further room (D16).
- A room on a room layer has `_PortalPlane` set on each of its materials to its doorway's plane, normal pointing
  into that room, so that its geometry in front of the doorway is clipped. A room on `Default` has
  (0, 0, 0, 1).
- The mask resets depth inside the doorway, so a room may have geometry behind its own doorways (the booth
  stands in front of the real wall).
- Curved spaces have no portals and portal rooms are never warped (§8.3): the clip and the warp never meet in one
  material.

```csharp
// File: Rendering/Portals/PortalRenderer.cs
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>Stencil portals between rooms authored in the room frame (Master Plan D16, task P1-A10).</summary>
    public sealed class PortalRenderer : Atlas.World.IUploadStep
    {
        public const int MaxSlots = 15;

        /// <summary>Registers a room: every renderer under root, with materials from the pool. Returns its id (0, 1, ...).</summary>
        public int AddRoom(Transform root, Bounds boundsWorld);
        /// <summary>Re-reads the renderers under a room's root after its content changed.</summary>
        public void RefreshRoom(int room);
        /// <summary>
        /// A doorway between two rooms: a vertical rectangle from floor point a to floor point b, of the given height,
        /// seen from fromRoom with a on the left. Creates both directions. Returns the door id.
        /// </summary>
        public int AddDoor(int fromRoom, int toRoom, float2 a, float2 b, float height);
        public void SetDoorOpen(int door, bool open);
        /// <summary>Removes every room and door and puts every root back on Default with the clip off.</summary>
        public void Clear();

        /// <summary>The room the walker is in. Changed by the renderer when the head crosses a doorway.</summary>
        public int CurrentRoom { get; set; }
        /// <summary>0: doorways show the fog colour. 1: doorways show the next room (portal.depth).</summary>
        public int Depth { get; set; }
        /// <summary>Stencil references in use this frame: 0 .. 15.</summary>
        public int SlotsInUse { get; }
        /// <summary>Raised when CurrentRoom changes: (old, new).</summary>
        public event System.Action<int, int> RoomChanged;

        /// <summary>Step 6: visibility through doorways for both eyes, slots, layers, masks, clip planes, room change.</summary>
        public void Upload(in Atlas.World.FrameContext frame);
    }
}
```

Behaviour fixed by `P1-A10`: the current room changes when the head (not an eye) crosses a doorway plane; a room
is switched off only when its bounds fail `GeometryUtility.TestPlanesAABB` for both eye frusta and the head is
outside the overlap volume, held for 3 consecutive frames; while one eye is within 0.10 m of a doorway plane the
mask covers the whole view of that eye (the portal box). At most 1.0 ms of GPU time per visible portal.

### 12.3 Scenes

| Scene | Path | Owner | iOS build list | Windows (Spectator) | Holds |
|---|---|---|---|---|---|
| `Atlas_Boot` | `Scenes/Atlas_Boot.unity` | A | index 0 | – | AR Session, XR Origin. Nobody edits it (README F3) |
| `Space_Home` | `Scenes/Space_Home.unity` | C | when generated | when generated | One root `Space_Home` with `HomeSpace` |
| `Space_Glued`, `Space_Portal`, `Space_Slicer` | `Scenes/Space_<Name>.unity` | A | when generated | when generated | One root with the module |
| `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` | `Scenes/Space_<Name>.unity` | B | not in this build | not in this build | – |
| `PC_Spectator` | `Scenes/PC_Spectator.unity` | D | never | index 0 | Lane D's page |
| `Sandbox_<name>` | `Scenes/Sandbox/` | each engineer | never | never | Anything; boots only with an `AtlasBoot` object (§8) |
| `Sys_Scan`, `Sys_BumpGuard`, `Sys_Onboarding`, `Sys_Link`, `Sys_Hud` | – | C, D | not created | not created | Replaced by installers (README F3). The names stay reserved |

`tools/gen_scenes.py` (lane A) writes the `Space_*` scenes and their `.meta` files and adds them to
`EditorBuildSettings`. Until it has run, every space comes from its factory (§5.2); behaviour is the same.
`Atlas_Boot` stays the active scene at all times, so `RenderSettings` (the background) are the boot scene's.

---

## 13 · `Mesh4D` and the slicer

Lane A's code in `Atlas.Kernel` (README F1), namespace `Atlas.Kernel.FourD`, folder `Kernel/FourD/`. Engine-free:
`System`, `Unity.Mathematics`, and for the job `Unity.Burst`, `Unity.Collections`, `Unity.Jobs`. One code path
for polytopes and for the worldtube (D17, §5.5).

### 13.1 Conventions

- A 4D point is `float4 (x, y, z, w)`. x, y, z are the three room directions in model units; w is the fourth
  direction (in ℝ³ × S¹: time or phase, scaled to length).
- Model units: polytopes have circumradius 1. The module scales the result to metres with its transform.
- A `Mesh4D` holds the **boundary** of a 4D body as tetrahedra, so a cut gives the surface of the 3D
  cross-section.
- The cut is the hyperplane `dot(normal, p) = offset` in the **slice frame**, where
  `p = mul(modelToSlice, vertex) + translation`. The 3D position of a cut point is `p.xyz`.
  With `normal = (0, 0, 0, 1)` that is an isometric picture of the cross-section: rotate the object, keep the cut
  (the three verbs of §5.1). A tilted `normal` with w dropped is the scanner picture of §5.4.
- Rotations are built in `double` and cast to `float4x4` last.

### 13.2 Types and generators

```csharp
// File: Kernel/FourD/Mesh4D.cs
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>Tetrahedra in R^4 (Master Plan section 5.5). Immutable once built.</summary>
    public sealed class Mesh4D
    {
        public readonly float4[] Vertices;     // model units
        public readonly int[] Tetrahedra;      // 4 vertex indices per tetrahedron
        public readonly int[] TetCell;         // one cell id (0 .. CellCount - 1) per tetrahedron
        public readonly int[] Edges;           // 2 vertex indices per edge of the body, for the shadow; may be empty
        public readonly int CellCount;
        public readonly float WPeriod;         // > 0 when w wraps (R^3 x S^1); 0 otherwise

        public int VertexCount { get; }
        public int TetCount { get; }

        /// <summary>ArgumentException when an index is out of range, a length does not fit, or a cell id is not below cellCount.</summary>
        public Mesh4D(float4[] vertices, int[] tetrahedra, int[] tetCell, int[] edges, int cellCount, float wPeriod = 0f);
    }
}
```

```csharp
// File: Kernel/FourD/Polytopes4D.cs
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    public static class Polytopes4D
    {
        public static Mesh4D Tesseract();
        public static Mesh4D Cell5();
        public static Mesh4D Cell16();
        public static Mesh4D Cell24();
        /// <summary>Boundary of the unit 4D ball: the tesseract's cells cut n times per side and pushed onto the 3-sphere.</summary>
        public static Mesh4D Ball(int n);

        /// <summary>
        /// A closed triangle surface carried through a periodic motion (Appendix A.8): ring k is the surface moved by
        /// phaseOffsets[k] at w = k * wPeriod / phaseOffsets.Length; one more ring repeats ring 0 at w = wPeriod.
        /// Each triangle between two rings is a prism split into three tetrahedra with its indices sorted.
        /// Cell id = the phase interval.
        /// </summary>
        public static Mesh4D Tube(float3[] surfaceVertices, int[] surfaceTriangles, float3[] phaseOffsets, float wPeriod);
        /// <summary>Tube of a sphere of 600 triangles (20 segments, 16 bands).</summary>
        public static Mesh4D MovingSphere(float diameter, float3[] phaseOffsets, float wPeriod);
        /// <summary>Offsets along +z for a constant speed: z = speed * k * period / phases.</summary>
        public static float3[] OffsetsConstantSpeed(int phases, float speed, float periodSeconds);
        /// <summary>Offsets of the illustrative breathing curve: z = -travel * cos^4(pi * k / phases) (Appendix A.8).</summary>
        public static float3[] OffsetsBreathing(int phases, float travel);
    }
}
```

| Generator | Vertices | Tetrahedra | Cells | Edges | Notes |
|---|---|---|---|---|---|
| `Tesseract()` | 16: (±0.5, ±0.5, ±0.5, ±0.5) | 48: 8 cubes × 6, the same Kuhn split in every cube so shared faces agree | 8 | 32 | Edge 1, circumradius 1 |
| `Cell5()` | 5 | 5 | 5 | 10 | Regular, circumradius 1 |
| `Cell16()` | 8: ± each axis | 16 | 16 | 24 | Circumradius 1 |
| `Cell24()` | 24: permutations of (±1, ±1, 0, 0) / √2 | 96: 24 octahedra × 4 | 24 | 96 | Circumradius 1 |
| `Ball(n)` | on the unit 3-sphere | 48 · n³ | 8 | 0 | For `Slicer_Ball` (n = 12); not shown in S6 |
| `MovingSphere` with 10 phases | 11 rings | 600 × 10 × 3 = 18,000 | 10 | 0 | The size of the tumour worldtube (§5.5) |

```csharp
// File: Kernel/FourD/Rotation4D.cs
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    public static class Rotation4D
    {
        /// <summary>Rotation by angle in the plane of axes i and j (0 = x, 1 = y, 2 = z, 3 = w), radians:
        /// p_i' = p_i cos - p_j sin, p_j' = p_i sin + p_j cos.</summary>
        public static double4x4 Plane(int i, int j, double angle);
        /// <summary>The six plane rotations applied in the fixed order XY, XZ, XW, YZ, YW, ZW. Radians.</summary>
        public static double4x4 FromPlaneAngles(double xy, double xz, double xw, double yz, double yw, double zw);
        /// <summary>The rotation that takes (1, 1, 1, 1) / 2 onto +w: the corner-first view of the tesseract.</summary>
        public static double4x4 CornerFirst();
        /// <summary>Gram-Schmidt on the rows.</summary>
        public static double4x4 Orthonormalised(in double4x4 m);
    }

    public static class Projector4D
    {
        /// <summary>Distance of the 4D eye along +w, model units (decided here: twice the circumradius).</summary>
        public const float DefaultFocal = 2f;
        /// <summary>Perspective shadow: p.xyz * focal / (focal - p.w).</summary>
        public static float3 Perspective(float4 p, float focal = DefaultFocal);
        /// <summary>Orthographic shadow: p.xyz.</summary>
        public static float3 Orthographic(float4 p);
    }
}
```

### 13.3 The slicer kernel and its job

```csharp
// File: Kernel/FourD/Slicer4D.cs
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>Cuts tetrahedra in R^4 with a hyperplane (Appendix A.8). Static, pointer-based, allocation-free.</summary>
    public static unsafe class Slicer4D
    {
        public const int MaxVerticesPerTet = 4;
        public const int MaxIndicesPerTet = 6;

        /// <param name="vertices">vertexCount model-space vertices.</param>
        /// <param name="tetrahedra">4 * tetCount vertex indices.</param>
        /// <param name="tetCell">tetCount cell ids, or null (every cell id is then 0).</param>
        /// <param name="cellUv">cellUvCount palette coordinates; a cut of cell c gets cellUv[c % cellUvCount]. At least one.</param>
        /// <param name="modelToSlice">Rotation of the object in R^4.</param>
        /// <param name="translation">Added after the rotation.</param>
        /// <param name="normal">Unit normal of the cut in the slice frame.</param>
        /// <param name="offset">The cut is dot(normal, p) = offset.</param>
        /// <param name="scratch">vertexCount entries; receives the transformed vertices.</param>
        /// <param name="outVertices">Receives up to vertexCapacity vertices: 3 or 4 per cut tetrahedron, not shared.</param>
        /// <param name="outIndices">Receives up to indexCapacity indices: 3 or 6 per cut tetrahedron.</param>
        public static void Slice(
            float4* vertices, int vertexCount,
            int* tetrahedra, int tetCount,
            int* tetCell,
            float2* cellUv, int cellUvCount,
            in float4x4 modelToSlice, in float4 translation,
            in float4 normal, float offset,
            float4* scratch,
            AtlasVertex* outVertices, int vertexCapacity,
            int* outIndices, int indexCapacity,
            out int outVertexCount, out int outIndexCount, out int outTetsCut);

        /// <summary>Sum of the triangle areas of a slice. For tests.</summary>
        public static float Area(AtlasVertex* vertices, int* indices, int indexCount);
    }
}
```

```csharp
// File: Kernel/FourD/SliceJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>Thin Burst wrapper round Slicer4D.Slice (README F2).</summary>
    [BurstCompile]
    public unsafe struct SliceJob : IJob
    {
        [ReadOnly] public NativeArray<float4> Vertices;
        [ReadOnly] public NativeArray<int> Tetrahedra;
        [ReadOnly] public NativeArray<int> TetCell;       // length 0 = no cell ids
        [ReadOnly] public NativeArray<float2> CellUv;
        public float4x4 ModelToSlice;
        public float4 Translation;
        public float4 Normal;
        public float Offset;
        public NativeArray<float4> Scratch;               // length = Vertices.Length
        public NativeArray<AtlasVertex> OutVertices;      // length = 4 * tetrahedra
        public NativeArray<int> OutIndices;               // length = 6 * tetrahedra
        public NativeArray<int> OutCounts;                // length 3: vertices, indices, tetrahedra cut
        public void Execute();
    }
}
```

Behaviour of `Slice`, binding:

| Item | Rule |
|---|---|
| Side test | d_k = dot(normal, p_k) − offset. A vertex with d ≥ 0 is on the positive side. One test for every tetrahedron |
| 4 – 0, 0 – 4 | Nothing |
| 1 – 3, 3 – 1 | One triangle: the three edges that cross |
| 2 – 2 | One quadrilateral as two triangles. With positive a₀, a₁ and negative b₀, b₁ the corner cycle is a₀b₀, a₀b₁, a₁b₁, a₁b₀: it never crosses itself |
| Edge point | p_i + d_i / (d_i − d_j) · (p_j − p_i) (Appendix A.8) |
| `Position` | `p.xyz` of the edge point |
| `Normal` | The flat normal of the triangle (of the quadrilateral's plane for both its triangles). Its sign is not defined |
| `Color` | `PackColor(AtlasShading.ToByte(AtlasShading.FaceShadeTwoSided(normal)), 0)`: baked shade, no lines |
| `Uv0` | `cellUv[cell % cellUvCount]`: the colour of the cell the face came from |
| `Uv1` | (0, 0) |
| Winding | Not defined. The slice material has `_Cull` = 0 (off) |
| Capacity | Stops before the first tetrahedron that would not fit. Never writes past a capacity. `outTetsCut` counts what was written |
| Cost | No allocation. No exception for any finite input |

The module (`SlicerSpace`) allocates the native arrays in `Configure` (`Allocator.Persistent`), schedules one
`SliceJob` in `Tick`, completes it in its `IUploadStep.Upload` and calls `AtlasMesh.Upload`. It reschedules only
when the rotation, the offset or the object changed: nothing is cut while the cut does not move (§5.5). The
schedule-to-upload span is inside `ProfilerMarker("Atlas.Slicer4D")` (§11.2).

Tests (lane A, `Tests/Kernel/`, assembly `Atlas.Kernel.Tests`, plain NUnit, pinned managed arrays):

| Test | Set-up | Pass |
|---|---|---|
| `Slicer_Tesseract` | `Tesseract()`, `modelToSlice = CornerFirst()`, `normal = +w`, `offset = (c − 2) / 2` for the cut x + y + z + w = c of the unit tesseract | `Area` = 0.8660 at c = 0.5 and 6.9282 at c = 2.0, to four decimals |
| `Slicer_Ball` | `Ball(12)`, identity, `normal = +w`, `offset = c` | Every output vertex has ‖`Position`‖ within 1 % of √(1 − c²) at c = 0, 0.5, 0.9 |
| `Slicer_MovingSphere` | `MovingSphere` at constant speed u, w = time × c; `normal = (0, 0, −sin θ, cos θ)` with tan θ = c / v | Extent of the slice along z = D · v / (v − u) within 2 %; at θ = 0, `Area` within 2 % of π D² |
| `Polytopes_Counts` | every generator | The counts of the table in §13.2; every tetrahedron of `Tesseract()` has volume 1/6 (six fill one unit cube) |
| `Rotation4D_Basics` | – | R · Rᵀ = I to 1e-9; four quarter turns in XW give the identity; `CornerFirst()` maps (1, 1, 1, 1) / 2 to (0, 0, 0, 1) to 1e-12 |

On phone A: tesseract at most 0.5 ms (should: 0.2 ms), the 18,000-tetrahedron tube at most 1.0 ms at p95, zero
allocation per frame (`P1-A12`).

---

## 14 · The flat and 4D spaces of lane A

What other lanes can rely on. `rooms`, `freeRect` and the like are the JSON names of `roommodel.schema.json`;
the C# members are lane C's.

### 14.1 S1 Glued (`GluedSpace`)

| Item | Contract |
|---|---|
| `Registered` | true. The lattice is fixed to the floor (D15) |
| Needs from the RoomModel | `cells.glued` (`nx`, `nz`, `sizeX`, `sizeZ`), `freeRect` (`wf`, `df`), `standing` |
| Needs from `IRoomContent` | Walls as kerbs; every object visible, drawn once at its true position (placeholder, generated or built-in mesh). The room is **not** copied (§4.9) |
| Lattice | Cell (i, j) covers x from −wf/2 + i·sizeX, z from −df/2 + j·sizeZ; the lattice continues past the free rectangle in every direction |
| Walker's cell | (ci, cj) = (floor((x + wf/2) / sizeX), floor((z + df/2) / sizeZ)) of `HeadPosition` |
| Copies drawn | T², Klein: n × n cells round (ci, cj), n = `glued.copies`. T³: 3 × 3 × 3, vertical period 2.6 m |
| Cell | One open frame of at most 1,500 triangles: four corner posts, a floor with the grid drawn by the shader, one chiral landmark. One mesh, one draw call per eye per copy |
| Klein bottle | A cell in an odd row j is mirrored in x (negative scale): the determinant sign of copy (row, column) is (−1)^row |
| Self-avatar | At most 600 triangles (head, torso, left shoulder `AvatarLeft`, right shoulder `AvatarRight`), in every drawn cell but the walker's own, at the walker's position in that cell, mirrored like the cell |
| Fog | §10.8: opaque at (n − 1) / 2 cells |
| Standing mode | Runs in place: no prompt to walk across a cell edge |
| `tileId` in `State` | (ci, cj, 0, 0, 0, 0) |
| `p[3]` to `p[7]` | mode, copies per side, sizeX, sizeZ, 0 |
| Budget | Cells and avatars alone: at most 110 draw calls and 110k triangles at 5 × 5; 120 and 120k for T³. With the room: the S1 rows of §15 |
| Test | `Glued_Handedness` (§11.1) |

| Parameter | Values | Default | `onRelease` | `SceneCmd.variant` |
|---|---|---|---|---|
| `glued.mode` | 0 torus T², 1 Klein bottle, 2 3-torus T³ | 0 | yes | `t2`, `klein`, `t3` |
| `glued.copies` | 3, 5, 7 per side (§9.6; written by the ladder) | 5 | yes | – |
| `glued.avatar` | 0 off, 1 on | 1 | no | – |

### 14.2 S5 Impossible Rooms (`PortalSpace`)

| Item | Contract |
|---|---|
| `Registered` | true, room by room. The real room's walls and furniture are where they really are; a virtual room's walls are virtual (§4.9) |
| Needs from the RoomModel | `freeRect` (`wf`, `df`): every cell of every room lies inside it. `standing`. The lowest wall height, for the door height |
| Needs from `IRoomContent` | Its root `Transform`, with every renderer on a pool material: the real room is room 0 of `PortalRenderer`. A change event when a mesh replaces a placeholder, to call `RefreshRoom(0)`. Walls in full |
| Layout 0, booth to hall | A booth of 1.0 × 1.0 m (at most 800 triangles) in a corner of the free rectangle, door 0.9 × 2.1 m towards the centre. Through the door: a hall that fills the free rectangle (at most 2,000 triangles) |
| Layout 1, four-turn loop | L-shaped corridors of width clamp(wf / 3, 0.7 m, 1.0 m) along the sides of the free rectangle, reusing the same floor; four right turns end in a different room. At most 2,000 triangles per room |
| Fallback | A free rectangle too small for the loop (wf under 2.1 m) gives layout 0. Standing mode gives layout 0, viewed in place |
| Portals | `PortalRenderer` (§12.2). Every portal transform is the identity (D16). No recursion |
| `tileId` in `State` | (current room id, 0, 0, 0, 0, 0); `sheet` = current room id |
| `p[3]` to `p[7]` | layout, room count, portal depth, 0, 0 |
| Budget | At most 60 draw calls and 150k triangles; at most 1.0 ms of GPU time per visible portal; at most 15 stencil references per frame |
| Test | `Portal_Graph` (§11.1) on an engine-free room graph |

| Parameter | Values | Default | `onRelease` | `SceneCmd.variant` |
|---|---|---|---|---|
| `rooms.layout` | 0 booth to hall, 1 four-turn loop | 0 | yes | `booth`, `loop` |
| `rooms.count` | 2 to 6 rooms in the loop | 6 | yes | – |
| `portal.depth` | 0 doorways show fog, 1 doorways show the next room | 1 | no | – |

### 14.3 S6 Slicer (`SlicerSpace`)

| Item | Contract |
|---|---|
| `Registered` | true. Home stays as it is; the 4D object floats in it |
| Needs from the RoomModel | `freeRect` (`wf`, `df`), `standing` |
| Needs from `IRoomContent` | The default: walls in full, objects visible |
| Object | Centre at (+wf / 4, 1.25 m, 0). Radius clamp(0.2 · wf, 0.25 m, 0.5 m) for a model circumradius of 1 (decided here) |
| The w lane | A line on the floor at x = −wf / 4, along Z, of length min(df − 0.4 m, 3.0 m), centred on z = 0. Walking it moves the cut: w₀ = lerp(−1.1, +1.1, t) with t the walker's fraction along the lane. Its twin is the stick. The input touched last owns w₀ for 2 s |
| Standing mode | No lane. The cut is moved with the stick (`P1-A15`) |
| Views | Slice (the cross-section, a colour per cell) and shadow (the projected edges, coloured along the w-depth strip) |
| Rotation | In the planes XW, YW, ZW, by the sticks; the cut stays at `normal = +w` |
| `tileId` in `State` | six zeros |
| `p[3]` to `p[7]` | w₀; the XW, YW, ZW angles in radians; object + 16 · view |
| Budget | At most 70 draw calls and 160k triangles with Home; slicer cost of §13.3 |

| Parameter | Values | Default | `onRelease` | `SceneCmd.variant` |
|---|---|---|---|---|
| `slicer.source` | 0 live, 1 stepped (precomputed slices, no blending), 2 tesseract only | 0 | yes | – |
| `slicer.object` | 0 tesseract, 1 5-cell, 2 16-cell, 3 24-cell, 4 moving-sphere tube | 0 | yes | `tesseract`, `cell5`, `cell16`, `cell24`, `tube` |
| `slicer.view` | 0 slice, 1 shadow | 0 | no | – |
| `slicer.w` | −1.1 to 1.1, model units | 0 | no | – |
| `slicer.xw`, `slicer.yw`, `slicer.zw` | −180 to 180, degrees | 0 | no | – |
| `slicer.tilt` | 0 to 80, degrees; the tube only (the scanner tilt of §5.4) | 0 | no | – |

### 14.4 What lane A's spaces and SpaceLab need from the input contract

Each with a gamepad binding and a gaze-dwell twin (rule 5). Names are lane C's.

| Action | Kind | Used by |
|---|---|---|
| Return to Home | button | SpaceLab |
| Next space, previous space | two buttons | SpaceLab |
| Open the menu | chord | lane D's menu |
| Primary, secondary | two buttons | S1 (mode), S5 (–), S6 (view, next object) |
| Axis w | one axis, −1 to 1 | S6 |
| Rotate A | two axes | S6: XW, YW |
| Rotate B | one axis | S6: ZW |

---

## 15 · Budgets

Every lane's content counts against these. A gate measures them on `budget_room` (§8.5, §11.2).

**Per frame, both eyes (must):**

| Quantity | Must | Should |
|---|---|---|
| Draw calls | ≤ 300 | ≤ 200 |
| Set-pass calls | ≤ 60 | ≤ 30 |
| Triangles | ≤ 500k | ≤ 300k |
| Resident memory | ≤ 1.5 GB | ≤ 1.0 GB |
| Managed allocation per frame | ≤ 1 kB | 0 |
| Frame time p50 / p95 / p99 | ≤ 16.7 / 20 / 33.3 ms | ≤ 16.7 / 17.5 / 25 ms |
| GPU p95, cold / hot | ≤ 9 / 14 ms | ≤ 7 / 11 ms |
| CPU main / render p95 | ≤ 8 / 6 ms | ≤ 6 / 5 ms |

**Per space (planning figures of §8.5, replaced by measured values at G4):**

| Space | Draw calls | Triangles | Driven by |
|---|---|---|---|
| S0 Home | ≤ 60 | ≤ 130k | Up to 20 furniture meshes, 60,000 triangles in total; shell ≤ 3,000 |
| S1 Glued 5 × 5 | ≤ 150 | ≤ 250k | 25 cells, 24 avatars, the room once |
| S1 Glued T³ | ≤ 160 | ≤ 250k | 27 cells, 26 avatars, the room once |
| S2 Curved Walk, ρ = 3 | ≤ 210 | ≤ 250k | Up to 71 tiles, plus the room |
| S3 Aligned Cells | ≤ 170 | ≤ 200k | Up to 57 cell instances; furniture once |
| S4 Small Planet | ≤ 70 | ≤ 190k | Six tiles plus the room |
| S5 Impossible Rooms | ≤ 60 | ≤ 150k | Two rooms through one portal, plus the room |
| S6 Slicer | ≤ 70 | ≤ 160k | Home plus the cross-section |

**Shares inside a frame** (so that the sums hold; phone A, p95):

| Item | Owner | Budget | Source |
|---|---|---|---|
| ARKit update with live depth, pose, input | C | 1.5 ms target, 2.5 cap | §8.5 |
| Bump Guard | C | ≤ 0.3 ms | `P1-C09` |
| Warning cue | C | ≤ 0.2 ms, no allocation | `P1-C14` |
| Kernel step; a tile-crossing frame | B | ≤ 0.1 ms; ≤ 2 ms | §11.2 |
| Space `Tick`, with the slicer | space owner | Inside the 0.8 ms target, 1.5 cap shared with the three rows above | §8.5 |
| Slicer: tesseract; 18k tube | A | ≤ 0.5 ms; ≤ 1.0 ms | §11.2 |
| Upload (globals, portals, rig) | A | ≤ 0.2 ms (decided here) | – |
| Link, telemetry, HUD together | D | 0.2 ms target, 0.5 cap; HUD alone < 0.2 ms | §8.5, `P1-D04` |
| Flat-twin map | D | ≤ 0.3 ms | `P1-D07` |
| Lens blit | A | ≤ 1.0 ms GPU | `P1-A05` |
| One visible portal | A | ≤ 1.0 ms GPU | `P1-A10` |
| Head-locked content (`AtlasHud`) | C, D | ≤ 20 draw calls | §12.1 |
| A mesh swap (GLB load) | D | Worst frame < 20 ms; one asset instantiated per frame | §7.4 |
| A space switch (`Configure` + `Enter`) | space owner | ≤ 50 ms (proposed) | §2 |
| Pool materials rented | all | ≤ 256 | §11 |

**Per asset** (§8.4): room shell ≤ 3,000 triangles; a generated object 1,500 to 5,000 and no more than its
target; a built-in fallback object ≤ 1,500; a Glued cell ≤ 1,500; a room of S5 ≤ 2,000; the self-avatar ≤ 600;
longest edge 0.25 m where §10.2 says so. A texture other than the palette: ASTC 6 × 6 (4 × 4 for UI), mipmaps on,
at most 1024; a data texture: uncompressed, no mipmaps, clamped, not sRGB (§8.1).

---

## 16 · Build

### 16.1 `tools/build_ios.sh`

One command from source to a running app on every connected phone (`P1-A13`, Appendix C.8). It runs on the build
seat only; [../build-seat.md](../build-seat.md) holds the queue and the rules.

```text
tools/build_ios.sh                 build the working tree as it is, install on every paired phone
tools/build_ios.sh <branch>        fetch, switch to <branch>, fast-forward, pull LFS, then build and install
```

| Environment variable | Effect |
|---|---|
| `ATLAS_TEAM_ID=XXXXXXXXXX` | Apple team id of the signing owner (the Personal Team). Needed for the first build; Xcode keeps it afterwards |
| `ATLAS_DEV=1` | Development build: Unity gets `-atlasDevelopment`, Xcode builds `Debug`, the Metal HUD key is written. Published numbers come from a release build, which is the default |
| `ATLAS_NO_INSTALL=1` | Stop after the Xcode build |
| `ATLAS_LAUNCH=1` | Launch the app on each phone after installing |
| `ATLAS_UNITY=/path/Unity` | Unity binary, when it is not in the Hub's default folder for the version in `ProjectVersion.txt` |

| Step | Command | Output |
|---|---|---|
| 1 | With `<branch>`: refuses a working tree with changes; `git fetch`, `checkout`, `pull --ff-only`, `lfs pull` | – |
| 2 | Unity in batch mode: `-buildTarget iOS -executeMethod Atlas.EditorTools.AtlasBuild.IOS -buildOutput ATLAS/Build/iOS` (append mode when the folder already holds a project) | `ATLAS/Build/iOS/Unity-iPhone.xcodeproj`; log `ATLAS/Build/unity-build.log` |
| 3 | `xcodebuild -scheme Unity-iPhone -configuration Release -destination generic/platform=iOS -allowProvisioningUpdates` | `ATLAS/Build/DerivedData/Build/Products/Release-iphoneos/*.app`; log `ATLAS/Build/xcodebuild.log` |
| 4 | `xcrun devicectl device install app` on every paired physical iPhone | – |

It exits with 0 on success and 1 on any failure, after printing the failing lines of the log. The project must
be closed in the Unity Editor. Fallback: open the exported project in Xcode and press Run. The scenes built are
the enabled scenes of `EditorBuildSettings` (§12.3).

### 16.2 Info.plist keys and build settings

`Editor/AtlasPostBuild.cs` (`[PostProcessBuild]`, lane A) writes every key. Nobody edits the generated Xcode
project by hand (Appendix C.5). A lane that needs another key asks lane A.

| Key | Value | Why |
|---|---|---|
| `NSCameraUsageDescription` | `AtlasUsageText.Camera` | Tracking, scan, object photos |
| `NSLocalNetworkUsageDescription` | `AtlasUsageText.LocalNetwork` | Unicast UDP to the Hub raises the prompt |
| `NSAppTransportSecurity` → `NSAllowsLocalNetworking` | `YES` | HTTP to a LAN address |
| `UIFileSharingEnabled` | `YES` | The perf CSV and the event log are readable from the Mac |
| `UIRequiresFullScreen` | `YES` | – |
| `CFBundleURLTypes` | one entry, scheme `atlas` | The pairing link `atlas://<ip>:9050` |
| `MetalHudEnabled` | `YES`, development builds only | Metal Performance HUD |
| Build setting `ENABLE_BITCODE` | `NO` on the project and both targets | – |

Not written, on purpose: `NSBonjourServices`, a microphone key, a multicast entitlement, and
`CADisableMinimumFrameDurationOnPhone` (the display stays at 60 Hz, §8.2).

Player settings applied by `Editor/AtlasProjectSetup.cs` (menu "Atlas/Apply Project Settings"): IL2CPP, ARM64,
Metal only, iOS 17.0, landscape left only, Frame Timing Stats on, managed stripping Low, VSync count 1, "Adjust
iOS FPS based on thermal state" off, bundle id `com.winstonthov.atlas`, automatic signing, and the URP, renderer,
layer and shader-set values of §9.1, §10.1 and §12.

---

## 17 · Checks and gaps

### 17.1 Only a device or the Editor can confirm

| Check | Where |
|---|---|
| The shader is reported SRP-Batcher compatible; at most 8 variants (`Shader_Contract`) | Editor, Frame Debugger and the shader inspector |
| One native pass per eye; colour resolved with store action MultisampleResolve; depth and stencil discarded; no multisampled attachment loaded; exactly one blit to the screen | Xcode GPU capture, phone A, release build (`P1-A05`) |
| The engine accepts memoryless multisampled eye textures with the settings of §9.1 | same |
| ARKit keeps delivering poses and depth with the AR `Camera` component disabled in headset mode. In the source, `ARCameraManager` only reads the camera's clip planes (`ARCameraManager.cs`, `Update`). Fallback if not: keep the camera enabled, culling nothing, into a 16 × 16 texture | phone A |
| Which formats Metal returns for the eye textures, and `Display.main.requiresSrgbBlitToBackbuffer` | phone A, logged once at boot |
| A straight-line grid looks straight through the lenses; lens numbers and head offset tuned | phone A in the headset, three testers |
| The shader set loads from `Resources` in a player build | phone A |
| Each ladder rung forced from the Console takes effect within 1 s | G4 |

### 17.2 Where the repository differs from this page today

Lane A's own work list; nothing here is another lane's to fix.

| File | Today | This page |
|---|---|---|
| `Editor/AtlasProjectSetup.cs` | Writes URP render scale 0.8; leaves `intermediateTextureMode` at `Always`, the main light on, and the AR background feature in the renderer | Render scale 1.0, `Auto`, main light disabled, the features of §12.2, layers of §12.1, the shader set, `Screen` MSAA 1 |
| `ProjectSettings/TagManager.asset` | No Atlas layer | §12.1 |
| `docs/scene-ownership.md` | `AtlasShaderIDs.cs` is "B only"; `Sys_*` scenes are loaded by the boot scene | §10.1 and §12.3 (open items 6 and 7) |
| `Scenes/Atlas_Boot.unity` | Also holds `ARPlaneManager`, `ARRaycastManager` and `ARCameraBackground` from the template | Unchanged by lane A. Lane C's AR set-up switches them at run time |

---

## Open for the lead

1. **Headset lens numbers** (open item 1 of §14 of the plan; settle by G4). Until they are measured the lens blit
   uses K1 = 0.34, K2 = 0.55, lens separation 0.064 m, screen-to-lens 0.039 m, vertical offset 0: the public
   Cardboard viewer profile of 2015, as numbers only. Lane A re-checks the four numbers against the Cardboard
   SDK source before G1. All five are registry parameters and are tuned live.
2. **Camera-to-eye offset.** ARKit tracks the phone's rear camera, which sits several centimetres from the
   midpoint between the walker's eyes. `IStereoRig.HeadOffset` corrects it and is (0, 0, 0) until measured for
   the chosen headset and each phone. With zero offset the 5 cm wall test of `P1-C07` is at risk. Measure with
   item 1.
3. **Field of view.** The eye cameras use 96° vertical at aspect 1398 : 1290, taken from §8.5's "about 50° to
   each side and 48° up and down". Confirm against the headset's real field of view; a smaller one wastes
   pixels, a larger one shows a black border.
4. **Values added to §12.4.** `QualityTier.High = 4` (ρ = 3.5, 7 × 7), `glued.copies` = 7 and `render.msaa` = 1
   extend the kill-switch ranges so that the high and the critical rung are ordinary parameter values.
   `State.flags` has two bits for the tier, so `High` is sent as 0.
5. **Ladder details the plan leaves open.** Step-up thresholds 6.5 ms and 8.0 ms (proposed); "missing 60 fps"
   read as frame p95 over 17.2 ms across the last 10 s; no automatic step up from `Critical`. And what the walker
   sees on `Critical`: this page shows "Session over" on the HUD and keeps rendering at 30 fps.
6. **Who edits `AtlasShaderIDs.cs`.** §6.4 says lanes A and B, `docs/scene-ownership.md` says B only. This page
   has lane A create and edit it until lane B starts (lane B's page proposes the same). The ownership file needs
   that line changed.
7. **`Sys_*` scenes.** §9.3 lists five system scenes; README F3 builds systems in code. This page creates none.
   Confirm, and correct `docs/scene-ownership.md`.
8. **One URP asset.** `P1-A03` asks for "tier assets Nominal / Fair / Serious". The eye-texture sets carry scale
   and MSAA, so this page keeps one URP asset and makes the tiers rows of a table. Confirm.
9. **Step 8 in two halves.** `State` is sent from `LateUpdate` (§7.2 of the plan); counters are sampled and
   received parameters applied after rendering (README F5). Confirm the split.
10. **No space entered.** `State.spaceId = 255` and `sceneId = 0xFFFF` during onboarding and scan. Appendix B.1
    only lists 0 to 6. Lane D's codec and the Spectator must accept it.
11. **Shading.** The shader does not light by the normal (D23); every generator bakes one fixed light into the
    vertex shade with `AtlasShading`. If the first headset view reads too flat, a normal term is an addition to
    the warp contract (lane B's open item 10) and is announced to all four lanes.
12. **Geometry pose after a jump.** This page assumes it follows the raw pose again, so registered spaces stay
    registered; only the displacement of the jump frame is dropped. Lane C's page defines it; if it differs,
    registered spaces read the raw pose.
13. **Parameters are never deleted.** A space's parameters exist from boot and keep their values across
    switches; leaving a space only marks its group inactive. This needs a group flag in lane D's registry; the
    fallback is that every group stays listed.
14. **A device build with no Bump Guard does not enter headset mode.** A missing guard is treated as a boot
    error on the phone and as a warning in the Editor.
