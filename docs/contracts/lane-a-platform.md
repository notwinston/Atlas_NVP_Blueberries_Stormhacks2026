# Lane A · Platform contract

Task `P1-X05`, frozen at gate G0 (Master Plan §6, §8, §9.3; README F1 to F9). Owner: lane A.

This page fixes what every lane builds against on the platform side: the space interface and its host, the
frame loop, the composition root, the rendering facade, the one shader and its mesh format, the layer and
stencil table, and `Mesh4D`. Names and signatures are binding. Bodies are free.

| Part | In this build | Later |
|---|---|---|
| `SpaceId`, `ISpaceModule`, `FrameContext`, `ISpaceHost`, `SpaceRegistry`, `AtlasServices`, frame hooks | Final | unchanged |
| `AtlasBootstrap`, `AtlasApp`, `AtlasFrameLoop`, SpaceLab | Built | unchanged |
| Stereo rig, eye textures, lens blit, quality ladder | Built; lens numbers are placeholders (open item 1) | Headset numbers baked in |
| `Atlas/Surface`, `AtlasCore.hlsl`, `AtlasPortalClip.hlsl`, palette, `MaterialPool` | Built | unchanged |
| `AtlasWarp.hlsl`, `AtlasGates.hlsl` | Lane B's warp and gate clip (`P1-B03`, `P1-B10`), on lane A's property block | unchanged |
| S1 Glued, S5 Impossible Rooms, S6 Slicer, `Mesh4D` | Built | unchanged |
| S2, S3, S4 | Built by lane B and registered like the others (`Spaces/CurvedWalk`, `AlignedCells`, `SmallPlanet`); S2 and S4 configure the app's one walker kernel on entry and SpaceLab puts it back to flat after Exit | unchanged |
| The tracked hands' hooks in the frame loop (§7), `Atlas/NearText`, how the hands and the near panels are drawn (§10.9) | Built and tested headless; the Unity-only tests and a phone have not run | unchanged |

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
| `AtlasServices`, `AtlasBootContext`, `IEventLog` | same | `World/Services/AtlasServices.cs`, `World/Services/AtlasBootContext.cs`, `World/Services/IEventLog.cs` |
| `QualityTier`, `QualityRow`, `QualityTable`, `IQualityState`, `IPerfSource`, `QualityInputs` | same | `World/Quality/QualityTier.cs`, `World/Quality/QualityInputs.cs` |
| `MeshData` | same | `World/Meshing/MeshData.cs` |
| `AtlasVertex`, `AtlasShading` | `Atlas.Kernel` · `Atlas.Kernel` | `Kernel/Mesh/AtlasVertex.cs`, `Kernel/Mesh/AtlasShading.cs` |
| `Mesh4D`, `Polytopes4D`, `Rotation4D`, `Projector4D`, `Slicer4D`, `SliceJob` | `Atlas.Kernel` · `Atlas.Kernel.FourD` | `Kernel/Mesh4D/*.cs` |
| `AtlasBoot`, `AtlasThermal` | `Atlas.Rendering` · `Atlas.Rendering` | `Rendering/AtlasBoot.cs`, `Rendering/AtlasThermal.cs` |
| `AtlasShaderIDs`, `AtlasPalette`, `AtlasPaletteTexture`, `AtlasLook`, `AtlasLookState`, `AtlasMesh`, `AtlasMeshBuilder`, `AtlasMeshBudget`, `MaterialPool`, `PoolLedger` | same | `Rendering/Look/*.cs` |
| `IStereoRig`, `DisplayMode`, `Eye`; `StereoRig`; `IEyeTextures`, `EyeTextureSet`; `EyeTextureLayout`; `LensBlit`; `LensParams`, `EyeFrustum`, `LensModel` | same | `Rendering/Stereo/IStereoRig.cs`, `StereoRig.cs`, `EyeTextureSet.cs`, `EyeTextureLayout.cs`, `LensBlit.cs`, `LensBlitPass.cs`, `LensModel.cs` |
| `QualityApplier`; `LadderController` and its own `QualityInputs`; `FramePacing`; a second `QualityTier`, `QualityRow`, `QualityTable` with the same values as the `Atlas.World` ones (§9.5); `QualityFurniture` (the furniture column of §9.5) | same | `Rendering/Quality/QualityApplier.cs`, `LadderController.cs`, `FramePacing.cs`, `QualityTier.cs`, `QualityFurniture.cs` |
| `PortalRenderer`, `PortalRoom`, `PortalDoor`, `PortalFrameCore`, `PortalSwapRule`, `PortalMath`, `PortalStencilTable`, `PortalSlotAllocator` | same | `Rendering/Portals/*.cs` |
| Shaders and includes | – | `Rendering/Shaders/Resources/*`, `Rendering/Stereo/Resources/`, `Rendering/Portals/Resources/` (§10.1) |
| `AtlasBootstrap`; `AtlasApp`, `BootScene` | `Atlas.App` · `Atlas.App` | `App/AtlasBootstrap.cs`, `App/AtlasApp.cs` |
| `AtlasFrameLoop`; `FrameLoopCore`, `ISpaceFrame`, `IWalkerReset`, `IStateWriter`, `IEyePositions`; `InputSenseStep`, `HeadTransformSenseStep`, `BumpGuardStep`, `LookUploadStep`, `RigUploadStep`, `RigEyePositions` | same | `App/Frame/AtlasFrameLoop.cs`, `App/Frame/FrameLoopCore.cs`, `App/Frame/FrameSteps.cs` |
| `SpaceLab`, `ISpaceLoader`, `ISpaceStage`; `SceneSpaceLoader`; `SpaceStage`; `GridSpace`; `SpaceFurniture`, `FurnitureMode`, `FurnitureLevelPolicy` (§4.2) | same | `App/Spaces/SpaceLab.cs`, `SceneSpaceLoader.cs`, `SpaceStage.cs`, `GridSpace.cs`, `FurnitureLevels.cs` |
| `CommandRouter`; `SpaceActions`, `SpaceActionStep` | same | `App/Commands/CommandRouter.cs`, `App/Commands/SpaceActions.cs` |
| `AppParams`, `ScanSettings`; `QualityLadder` | same | `App/Params/AppParams.cs`, `App/Params/QualityLadder.cs` |
| `AppHooks`, `IOnboardingFlow`, `IAppMenu`, `IPerfTour` | same | `App/Hooks/AppHooks.cs` |
| `RoomHandOver`; `GuardedStereoRig`, `HeadsetGate`; `BootOptions`; `RehearsalFlow`; `PoseSourceSwitch`; `DevOverlay` | same | `App/Room/RoomHandOver.cs`, `App/Flow/*.cs` |
| `GluedSpace`, `PortalSpace`, `SlicerSpace` | `Atlas.Spaces.Glued`, `.Portal`, `.Slicer` | `Spaces/<Name>/<Name>Space.cs` |
| `AtlasBuild`, `AtlasPostBuild`, `AtlasProjectSetup`, `AtlasUsageText` | `Atlas.Editor` · `Atlas.EditorTools` | `Editor/*.cs` |

`Atlas.World` core types use only `System`, `Unity.Mathematics`, `Atlas.Kernel` and `Atlas.Link` (README F1,
F2). The one exception is `AtlasBootContext`, which carries `UnityEngine.Transform` references and is not a core
type.

There are two mesh-data classes with the same member names: `Atlas.World.MeshData` (engine-free, for
`Atlas.Telemetry`, which may not reference `Atlas.Rendering`) and `Atlas.Rendering.AtlasMeshBuilder` (the one
`AtlasMesh` uploads and every space and the room content use; it has more generators, §10.2).

### 0.2 Names this page takes from other pages

This page uses the names below and relies on nothing else about them. Where another page spells one of them
differently, that page wins and this one is corrected at the freeze.

| Name | Page | What this page relies on |
|---|---|---|
| `WalkerPose`, `WalkerPose.Start`, `TileId`, `Iso2` | lane B §4.2, §4.4, §4.5 | As written there (`Atlas.Kernel`) |
| `IWalkerKernel`, `IDialKernel`, `KernelStep`, `KernelConfig.Flat(double)` and `Curved(int, double)`, `FlatWalkerKernel`, `HolonomyWalkerKernel`, `KernelConstants.DefaultFlatTileSideMetres` | lane B §4.9 to §4.11 | `Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds)` (the head's floor point and the frame's time; the kernel forms the displacement itself, and the time tells standing from walking for the curvature ramp), the same without a time, `ResetToFloor(double2)`, `Reset(in WalkerPose)`, `Configure(in KernelConfig)`, `Pose`, `CullRho`, `MetresPerUnit`, `StepCount` |
| `AtlasWarp(float3)`, `AtlasWarpClip(float3)`, `AtlasGatesClip(float3)`, `_WarpMode`, `_Anchor`, `_Gate0` to `_Gate3`, `_HeadPosWS`, `_HypL` | lane B §6 | As written there |
| `RoomModel` | lane C | A class in `Atlas.World` with the fields of `roommodel.schema.json`: `freeRect.wf`, `freeRect.df`, `lapSquare.side`, `cells.glued` (`nx`, `nz`, `sizeX`, `sizeZ`), `standing`, `walls`, `objects`. C# member names are lane C's |
| `TrackingState`, `TrackingStates.ToWire` | lane C | An enum in `Atlas.World` (`World/Pose/TrackingState.cs`) with `NotTracking = 0`, `Limited = 1`, `Normal = 2`. `State.flags` bits 0 to 1 carry `TrackingStates.ToWire(state)` (0 normal, 1 limited, 2 lost), never the raw enum value |
| `IBumpGuard`, `BumpLevel`, `SensorFrame`, `BumpCue.Fire`, `CueSource` | lane C | `IBumpGuard.Tick(in SensorFrame)`, `IBumpGuard.Level`, `BumpCue.Fire(BumpLevel, float2, CueSource)`, `BumpCue.Shown` |
| `IPosePipeline` | lane C | `Atlas.World`. `Sensors` (the `SensorFrame` of this frame, with `RoomActive`), `Source`, `PredictionRotation`, `GyroPredictMs`, `ResetGeometry()` |
| `IAtlasInput`, `AtlasAction`, `AtlasAxis`, `InputContext`, `GazeTarget` | lane C | `Atlas.Input`. `Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)`, `Pressed(AtlasAction)`, `Axis(AtlasAxis)`, `Walker`, `DwellSeconds`, `PushContext`, `PopContext`, `RegisterGazeTarget` |
| `IRoomContent`, `WallMode`, object category | lane C | `Root`, `IsBuilt`, `SetWallMode(WallMode mode, float seconds = 0.5f)` with `WallMode.Full` and `WallMode.Kerbs`, `FloorVisible`, `ObjectsVisible`, `ResetPresentation()`, `FurnitureLevel FurnitureLevel { get; set; }` (the enum `FurnitureLevel { Full = 0, Light = 1 }` is in `Atlas.World`; `ResetPresentation()` returns it to `Full`), `bool IsMeshDrawn(Mesh)` (true while a view shows the mesh, holds it as its light copy, fades it out or has it waiting for its swap; the asset client destroys a replaced first shape only when it is false), `Built`, `IsBuilt`; the category as an integer 0 to 31 (the order of `objects[].category` in the schema) |
| `ParamRegistry`, `ParamEntry`, `ParamSource`, `StateMessage` | lane D | `Atlas.Link`. `DeclareFloat`, `DeclareInt`, `DeclareBool`, `DeclareEnum` (by name, idempotent, keep the current value; the last argument is `onRelease`), `Find`, `TryGet`, `Set`, `SetGroupActive(string group, bool active)`, `PushAll()` |
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

        /// <summary>True for the seven defined values.</summary>
        public static bool IsDefined(SpaceId id);
        /// <summary>"S0" to "S6": the code used by SceneCmd (Appendix B.2) and in logs.</summary>
        public static string Code(SpaceId id);
        /// <summary>Parses "S0" to "S6". False for anything else.</summary>
        public static bool TryParseCode(string code, out SpaceId id);
        /// <summary>"Space_Home" and so on: the scene name of Appendix C.9.</summary>
        public static string SceneName(SpaceId id);
        /// <summary>Lower-case parameter group: "home", "glued", "curved", "cells", "planet", "portal", "slicer".</summary>
        public static string ParamGroup(SpaceId id);
        /// <summary>The name shown to people: "Home", "Glued", "Curved Walk", "Aligned Cells", "Small Planet", "Impossible Rooms", "Slicer".</summary>
        public static string DisplayName(SpaceId id);
        /// <summary>State.sceneId of a SpaceLab space: the numeric value of the id.</summary>
        public static ushort SceneId(SpaceId id);
    }
}
```

| Value | Code | Member | Shown as | Scene | Assembly | Module class | Owner | `Registered` | In this build |
|---|---|---|---|---|---|---|---|---|---|
| 0 | S0 | `Home` | Home | `Space_Home` | `Atlas.Spaces.Home` | `HomeSpace` | C | true | yes |
| 1 | S1 | `Glued` | Glued | `Space_Glued` | `Atlas.Spaces.Glued` | `GluedSpace` | A | true | yes |
| 2 | S2 | `CurvedWalk` | Curved Walk | `Space_CurvedWalk` | `Atlas.Spaces.CurvedWalk` | `CurvedWalkSpace` | B | false | yes |
| 3 | S3 | `AlignedCells` | Aligned Cells | `Space_AlignedCells` | `Atlas.Spaces.AlignedCells` | `AlignedCellsSpace` | B | true | yes |
| 4 | S4 | `SmallPlanet` | Small Planet | `Space_SmallPlanet` | `Atlas.Spaces.SmallPlanet` | `SmallPlanetSpace` | B | false | yes |
| 5 | S5 | `Portal` | Impossible Rooms | `Space_Portal` | `Atlas.Spaces.Portal` | `PortalSpace` | A | true | yes |
| 6 | S6 | `Slicer` | Slicer | `Space_Slicer` | `Atlas.Spaces.Slicer` | `SlicerSpace` | A | true | yes |

- The module class is `<Name>Space` in the namespace of its assembly (decided here). `tools/gen_scenes.py` names
  each script by its path in its manifest.
- An undefined id gives `"S?"`, `"Space_Unknown"`, `"unknown"` and `"Unknown"` from the four name methods.
- While no Home space is registered and no `Space_Home` scene is in the build, the composition root registers
  `Atlas.App.GridSpace` (the room as it stands on a grid ground) under `SpaceId.Home`, so the app always has a
  space to enter.
- `State.sceneId`: 0 to 6 for a SpaceLab space, `0xFFFF` with `spaceId = 255` while no space is entered,
  100 and above reserved for use-case scenes of Phase 2 (decided here). The frame loop sends `SpaceIds.NoSpace`
  and `NoScene` during onboarding and scan, and lane D's Spectator shows its "no space" banner for them.
  (differs from plan Appendix B.1, which lists only 0 to 6.)

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

    /// <summary>Optional. A space that generates a world reports the seed, the generator version and the hash of
    /// what it made, constant from the end of Configure until Exit (after a change the space calls
    /// ISpaceHost.BumpWorldVersion). LinkPump puts them into the `world` event (lane D §2.6). S2 implements it.</summary>
    public interface ISpaceWorld
    {
        long WorldSeed { get; }
        int GeneratorVersion { get; }
        string ContentHash { get; }         // 16 lower-case hexadecimal digits (WorldGenerator.WorldHash), or empty
    }
}
```

A module is a `MonoBehaviour` (README F3). SpaceLab is its only caller on the phone. In the Spectator the host
is lane D's `SpectatorHost` (`Telemetry/Map/SpectatorHost.cs`), and it is the one that calls `ReadState`.

| Call | When | World visible? | The module may | The module must not |
|---|---|---|---|---|
| `Awake`, `OnEnable` | SpaceLab loads the scene or runs the factory | no (black) | Cache its own components | Read the room, the registry or any service; build content |
| `Configure(room, registry)` | Once per load, straight after creation. `room` is never null | no | Declare parameters (idempotent, §4.3). Take services from `AtlasServices`. Build meshes, rent materials, create children under its own transform. Allocate freely | Touch anything outside its own root except through `IRoomContent`, `AtlasLook` and `MaterialPool`. Wait for a file or the network |
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
10. **A module that throws is taken out.** An exception from `Configure` or `Enter` is logged as `space.error`,
    the module is unloaded and SpaceLab goes on with Home (§4.2, step 4). An exception from `Tick`, `Upload` or
    `WriteState` is logged as `space.error`, the module is not called again on the frame path, and at the next
    step 8b SpaceLab switches to Home, or to no space when Home is the broken one.

What `WriteState` receives and writes:

| Field | Filled by `FrameLoopCore` before the call | The module writes |
|---|---|---|
| `flags` (tracking, pose-jump bit, quality tier), `sceneId`, `headPos`, `headRot`, `spaceId`, `worldVer` | yes | never |
| `bumpLevel`, the cue flag | yes, by the guard step through `IStateWriter` (§7.1) | never |
| `msgType`, `seq`, `t_us`, the lock flag | no: lane D's link glue writes them when it sends | never |
| `p[0]`, `p[1]`, `p[2]` | `(float)MetresPerUnit`, `CullRho − 1`, `CullRho` from `IWalkerKernel` (lane B §7.3); `MetresPerUnit` is 1 in a flat space | never |
| `tileId` | six zeros while the kernel is flat (q = 4); otherwise `kernel.Pose.Tile`, read at step 8a | Its tile, cell or room id (Appendix B.1), when it is not the kernel's: S1, S3, S5 |
| `sheet` | 0 | Its sheet or cell index |
| `xform` | identity while the kernel is flat; otherwise `kernel.Pose.Local`, row-major | S3 writes its cell's quarter-turn |
| `p[3]` to `p[7]` | 0 | Its own values (§14) |

---

## 3 · `FrameContext`

One value per frame, owned by the frame loop (`FrameLoopCore`), passed by reference. Nobody keeps a copy across
frames.

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
| `Time` | `double` | s | The frame's time on the app clock: `Time.unscaledTimeAsDouble`, in replay too; a test passes its own clock to `FrameLoopCore.RunUpdate`. Monotonic | start of `Update` | A |
| `DeltaTime` | `float` | s | The engine's `Time.unscaledDeltaTime`, clamped to 0.004 to 0.1 (`FrameLoopCore.MinDeltaTime`, `MaxDeltaTime`) | start of `Update` | A |
| `RawHeadPosition` | `float3` | m · world | The tracked head: the midpoint between the eyes (§9.2). Relocalisation jumps included. What Bump Guard, the cameras and `State.headPos` use | step 1 | C |
| `RawHeadRotation` | `quaternion` | world | The tracked head's rotation, no prediction | step 1 | C |
| `Tracking` | `TrackingState` | – | ARKit tracking state of this frame (`NotTracking`, `Limited`, `Normal`). `Normal` when no sense step is installed | step 1 | C |
| `HeadPosition` | `float3` | m · world | The geometry head pose: what a space reads. Lane C's page defines how it differs from the raw pose | step 3 | C |
| `HeadRotation` | `quaternion` | world | Rotation of the geometry head pose | step 3 | C |
| `FloorDelta` | `float2` | m · world (x, z) | The floor displacement of the geometry pose this frame, as the pose filter wrote it. With no filter installed the loop writes `HeadPosition.xz` minus the previous frame's (zero on frame 0). Zero after a walker reset. For spaces to read: step 4 hands the kernel the head's floor point `HeadPosition.xz`, not this delta, and the kernel forms and may reject its own displacement (`Kernel.Rejected`) | step 3 | C (A with no filter) |
| `PoseJumpRejected` | `bool` | – | The pose filter dropped a jump this frame. `State.flags` bit 2 is this, or `Kernel.Rejected` while tracking is `Normal` | step 3 | C |
| `Walker` | `WalkerPose` | kernel units | `IWalkerKernel.Pose` after this frame's `Step` | step 4 | A from B |
| `Kernel` | `KernelStep` | – | What the step did: `Rejected`, `Crossings`, `LastEdge`, `AppliedMetres`, `TileChanged` | step 4 | A from B |
| `LeftEyePosition`, `RightEyePosition` | `float3` | m · world | The world positions of the two eye cameras, from `IStereoRig.GetEyePosition` through `IEyePositions`: the head with the eye offset ∓`ipd` / 2 along the eyes' local x. Both equal `RawHeadPosition` when there is no rig (Spectator) | step 1, after the sense steps | A |
| `Tier` | `QualityTier` | – | The quality tier in force this frame (§9.5) | start of `Update` | A |

- **Geometry pose** (decided here; lane C's page §7.5 matches it): after a rejected jump the geometry pose
  follows the raw pose again. The jump is dropped from the displacement, not kept as a standing offset, so a
  registered space stays registered. Registered spaces may therefore read either pose; they read `HeadPosition`.
- **No input in the context.** `Atlas.World` cannot name a type of `Atlas.Input`. Lane C's input system freezes
  one snapshot per frame at step 1; a space reads it through `AtlasServices.Get<IAtlasInput>()`.
- **No Bump Guard level in the context.** Spaces do not react to it. The guard step writes it into `State`
  itself (`IStateWriter`, §7.1).
- `FrameContext` is about 250 bytes. Always pass it with `in` or `ref`.

---

## 4 · `ISpaceHost` and SpaceLab

`Atlas.App.SpaceLab` hosts exactly one space at a time. Other lanes see it only as `ISpaceHost`. It is a plain
engine-free class, `SpaceLab : ISpaceHost, ISpaceFrame, IAfterFrameStep`, built as
`new SpaceLab(ParamRegistry registry, IWalkerKernel kernel, IWalkerReset walker, ISpaceLoader loader, ISpaceStage stage)`:
`SceneSpaceLoader` brings the scene-or-factory rule (§5.2) and `SpaceStage` the fade, the reset between two
spaces and the furniture level (below); `SpaceStage(IRoomContent room, FurnitureLevelPolicy furniture = null)`, and a second constructor `SpaceStage(room, furniture, Action resetLook, Action<float> setFade)` that takes the look's reset and fade as delegates, for a test that has no shader globals to write. A switch advances once per frame at step 8b.

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
        /// <summary>Starts a switch with a fade. False when refused (no room, unavailable, already there).</summary>
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
| `Room` is null | false, event `space.refused` with `{"space":"S1","reason":"noRoom"}` |
| `IsAvailable(target)` is false (a space this build has neither a scene nor a factory for) | false, event `space.refused` with reason `unavailable`. Nothing reaches the kernel |
| `target` is the entered space and `State` is `Running` | false, nothing happens (unless the entered module has faulted) |
| `State` is `Running` or `Empty` | true. The switch state changes at once; its steps (§4.2) run from the next step 8b |
| A switch is already under way | true. The newest request replaces the pending one and runs when the current switch reaches `Running` or `Empty` |

`ReturnHome()`: when Home is entered and running it resets the walker (`IWalkerReset.ResetWalkerToFloor`, which
is the frame loop's) with no fade and returns true; otherwise it is `RequestSwitch(SpaceId.Home)`. It is the
target of the operator command `home` and of the `AtlasAction.ReturnHome` action (`SpaceActions`, §14.4).

### 4.2 The switch, step by step

Constants in `SpaceLab` (decided here): `FadeOutSeconds = 0.25f`, `FadeInSeconds = 0.25f`. The fade is the global
`_AtlasFade` (§10.4), set through `ISpaceStage.SetFade`: the world and the background go to black, the
head-locked cue and HUD do not. A switch with a scene in the build is under 1.0 s end to end (proposed, G4). The
first fade frame starts within 250 ms of an operator command (test L10).

| # | State | What SpaceLab does |
|---|---|---|
| 1 | `FadingOut` | Ramps `_AtlasFade` 1 → 0 over `FadeOutSeconds`. The old module keeps ticking. Skipped when no space was entered |
| 2 | `Loading` | `old.Exit()`; raises `SpaceExited`; marks the old parameter group inactive; `ISpaceLoader.Unload`: destroys the old root, or `SceneManager.UnloadSceneAsync` for a scene |
| 3 | `Loading` | `ISpaceStage.ResetForSpace(SpaceId? next)`, with `next` the space about to be entered, or null when the switch ends in `Empty`: `AtlasLook.ResetForSpace()` (§10.8), `IRoomContent.ResetPresentation()` when the room content is built, and then `FurnitureLevelPolicy.EnterSpace(next)`, which sets the room's furniture level (below). A module that uses a `PortalRenderer` clears it in its own `Exit` |
| – | `Loading` | Waits here while `IWalkerReset.RoomResetPending`: a room was just made active and the head is not in its frame yet, so the walker cannot be placed |
| 4 | `Loading` | Creates the module by the scene-or-factory rule of §5.2. On failure, or when `Configure` or `Enter` throws: logs `space.loadFailed` and continues once with Home, telling the stage `ResetForSpace(Home)` again so that Home is entered at its own furniture level (`Full`) and not at the failed space's; if Home fails too, calls `ResetForSpace(null)` and ends in `Empty` with the fade at 1 and the room content visible |
| 5 | `Loading` | `module.Configure(Room, registry)`; `WorldVersion += 1`; marks the new parameter group active |
| 6 | `Loading` | If `module.Registered`: `IWalkerReset.ResetWalkerToFloor()` (lane B §7.2). Then `module.Enter(kernel.Pose)`, or, for S3 entered from S2 at q = 5, `module.Enter` with the tile and frame S2 stood in (below); caches `module as IUploadStep`; raises `SpaceEntered`, on which `AppParams` writes `space.id` back (§9.6) |
| 7 | `FadingIn` | From the next frame, ramps `_AtlasFade` 0 → 1 over `FadeInSeconds`. The new module ticks from the first frame |
| 8 | `Running` | `SwitchCount += 1`. Runs a pending request, if any |

Steps 2 to 6 run in one step 8b when the module comes from a factory, and over the frames of the asynchronous
load when it comes from a scene. Bump Guard and its cue run in every one of those frames.

**The kernel in a switch.** The walker's kernel is the app's one `HolonomyWalkerKernel` (lane B §4.11, §7.2). It is flat between two spaces. A curved space (S2, S4) configures it on `Enter` through `IDialKernel` and never restores it; SpaceLab does that after the space's `Exit` (step 2), with `kernel.Configure(KernelConfig.Flat(s))` and `ResetWalkerToFloor()` (the walker stays unplaced while a room reset is latched, and the frame loop places it). One switch keeps the walker instead: S2 to S3 (Mode 1 to Mode 2, plan §4.8 and §4.9). When S2 is entered at q = 5 with κ = −1 and no room reset pending, SpaceLab reads `kernel.Pose` before `Exit`, restores the flat kernel as above, and enters S3 with that tile and frame through `module.Enter(in keptPose)` in place of the registered start. The rule is inside SpaceLab: `ISpaceHost` and `ISpaceModule` gained nothing for it.

**The furniture level.** A generated object has a full and a light copy (mesh design §5.4). Each space asks
for one level, and the room content draws that copy. The level is set between frames, never inside one; on a
switch it is set in `ResetForSpace`, while the screen is black. The table is `SpaceFurniture.Level(SpaceId)`
(`App/Spaces/FurnitureLevels.cs`), and it is the single place the levels are written:

| Space | Level |
|---|---|
| S0 Home, S2 Curved Walk | `Full` |
| S1 Glued, S3 Aligned Cells, S4 Small Planet, S5 Impossible Rooms, S6 Slicer | `Light`, until the phone measurement of check M3 says a space holds 60 frames per second with `Full` |

`FurnitureLevelPolicy(IRoomContent room, QualityLadder ladder)` combines three inputs into the level it puts on
`room.FurnitureLevel`, and applies it again when any of them changes: on `EnterSpace`, on a ladder tier change
(`QualityLadder.TierChanged`), when `render.furniture` changes and when the room content raises `Built`. It does
nothing while the room is not built. `room` may be null (a sandbox with no room) and `ladder` may be null (the
space's level then stands). With no space entered the space's own level is `Full`.

| `render.furniture` | Level in force |
|---|---|
| `auto` (default) | The space's level, held down to `Light` when the ladder's furniture column (§9.5) says `Light` |
| `light` | `Light` in every space |
| `full` | `Full` in every space, whatever the ladder says |

The Spectator does not take part: `SpectatorApp` composes its own root, sets no level and shows the full copy.

SpaceLab never calls `Resources.UnloadUnusedAssets`: a module destroys what it created (rule 5 of §2). Twenty
switches leave memory within ±5 % (`P1-A08`).

`SetRoom(room)`: stores the room, `WorldVersion += 1`, calls `kernel.Configure(KernelConfig.Flat(s))` with s the
lap-square side (`room.LapSide`, or `KernelConstants.DefaultFlatTileSideMetres` when it is not above 0; skipped
while a curved space is entered, whose `Exit` restores the flat kernel of the new room) and
`IWalkerReset.LatchRoomReset()`, raises `RoomChanged` (the pose pipeline, Bump Guard, the room content and the
link take the room on it), then reloads the entered space through the sequence above. The walker is not reset
in `SetRoom` itself, because the head is still in the old frame: the frame loop resets it straight after step 3
of the first frame whose `SensorFrame.RoomActive` is set, before that frame's kernel step, and calls
`IPosePipeline.ResetGeometry()` with it. `SetRoom(null)` leaves the entered space through a fade and ends in
`Empty`. `SetRoom` does not enter a space by itself. `Atlas.App.RoomHandOver` is the one caller in the app:

```csharp
// File: App/Room/RoomHandOver.cs   (published in AtlasServices under its own type)
public sealed class RoomHandOver : IAfterFrameStep
{
    public SpaceId EntrySpace { get; set; }      // Home by default
    public bool AutoEnter { get; set; }          // true by default; false while onboarding owns the flow
    public bool EnterPending { get; }
    public event Action<RoomModel> Activated;
    /// <summary>Gives the anchor to the pose pipeline, calls ISpaceHost.SetRoom, raises Activated.</summary>
    public void Activate(RoomModel room, object anchor = null);
    /// <summary>Requests the entry space now if the room frame is in force. For onboarding, after its prompt.</summary>
    public bool EnterNow();
}
```

With `AutoEnter` set it requests `EntrySpace` at the first step 8b after the walker has been placed, never in
the frame of `Activate`.

### 4.3 Parameters of a space

| Rule | Detail |
|---|---|
| Group | A space's parameters are named `<group>.<name>` with the group of `SpaceIds.ParamGroup` (`glued.mode`) |
| Declared twice, kept once | Each space gives `SpaceRegistry` a `declareParams` delegate (§5). The composition root runs every delegate once at boot (`SpaceRegistry.DeclareAllParams`), so every parameter of §12.2 and §12.4 exists from the first frame and the on-device menu can change it with any space entered. `Configure` declares the same parameters again; a declaration by an existing name returns the existing entry with its current value |
| Values survive a switch | Nothing is deleted on `Exit`. A value tuned in a space is still there when the space is entered again |
| "Removed" means inactive | SpaceLab marks all seven space groups inactive when it is created, a group active after `Configure` and inactive again on `Exit` (`ParamRegistry.SetGroupActive`). The Console and the menu show an inactive group folded and greyed. The registry has no kill-switch flag: a kill switch of §12.4 that sits in a space's group (`glued.copies`, `curved.cullRho`, `portal.depth`, `slicer.source`) goes inactive with it, and the menu lists the kill switches by name |
| `onRelease` | A parameter that regenerates geometry is declared with `onRelease`; the module rebuilds (in the handler, or in the next `Tick` from a kept handle) and calls `BumpWorldVersion()` |
| Reading | A module keeps the `ParamEntry` handles it got in `Configure` and reads their values in `Tick`. No lookup by name on the frame path |

What this needs from `ParamRegistry` is listed in §0.2.

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
        /// <param name="id">One of the seven spaces; ArgumentOutOfRangeException otherwise.</param>
        /// <param name="factory">Creates a new GameObject named like the scene, adds the module, returns it. Not null.</param>
        /// <param name="declareParams">Declares the space's parameters; may be null.</param>
        public static void Register(SpaceId id, Func<ISpaceModule> factory, Action<ParamRegistry> declareParams = null);
        public static bool IsRegistered(SpaceId id);
        /// <summary>Number of registered spaces.</summary>
        public static int RegisteredCount { get; }
        /// <summary>Runs the factory. Null when the space is not registered.</summary>
        public static ISpaceModule Create(SpaceId id);
        /// <summary>Runs every declareParams delegate, in SpaceId order. Called once by the composition root at boot.</summary>
        public static void DeclareAllParams(ParamRegistry registry);
        /// <summary>For tests.</summary>
        public static void Clear();
    }
}
```

### 5.1 Registration

One static registration class per space assembly (binding pattern). The module class exposes
`public static ISpaceModule Create()` and `public static void DeclareParams(ParamRegistry registry)`. A space may
keep its logic in engine-free classes beside the module (README F2), as Glued, Portal, Slicer and Home do.

```csharp
// File: Spaces/Glued/GluedRegistration.cs
namespace Atlas.Spaces.Glued
{
    static class GluedRegistration
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Atlas.World.SpaceRegistry.Register(Atlas.World.SpaceId.Glued, GluedSpace.Create, GluedSpace.DeclareParams);
        }
    }
}
```

`BeforeSceneLoad` runs before `AtlasBootstrap` (`AfterSceneLoad`), so the registry is full when SpaceLab starts.
All seven spaces register: S2, S3 and S4 through `CurvedWalkRegistration`, `AlignedCellsRegistration` and `SmallPlanetRegistration`.

### 5.2 Scene or factory (README F3)

```text
name = SpaceIds.SceneName(target)
if Application.CanStreamedLevelBeLoaded(name):
    SceneManager.LoadSceneAsync(name, Additive); keep Atlas_Boot the active scene
    module = the first ISpaceModule on a root object of the loaded scene (GetComponent on each root)
    if module is null: unload the scene and fall through
if module is null and SpaceRegistry.IsRegistered(target):
    module = SpaceRegistry.Create(target); parent its GameObject under "Atlas/Space"
if module is null: the load failed
```

This is `Atlas.App.SceneSpaceLoader : ISpaceLoader` (`IsAvailable`, `Begin`, `TryFinish`, `Unload`).
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
        /// <summary>Publishes a service under the type T and returns the one it replaced, or null.</summary>
        public static T Replace<T>(T service) where T : class;
        /// <summary>Removes the service if it is the one published under T.</summary>
        public static void Unregister<T>(T service) where T : class;
        /// <summary>The service, or InvalidOperationException naming T when none is published.</summary>
        public static T Get<T>() where T : class;
        public static bool TryGet<T>(out T service) where T : class;
        /// <summary>True when a service is published under T.</summary>
        public static bool Has<T>() where T : class;
        /// <summary>True from the end of the composition root until Clear.</summary>
        public static bool BootCompleted { get; }
        /// <summary>Raised once when the composition root has finished. A late subscriber is called at once.</summary>
        public static event Action Booted;
        /// <summary>Called by the composition root as its last step: sets BootCompleted and raises Booted once.</summary>
        public static void MarkBooted();
        /// <summary>Removes everything: services, Booted subscribers and the boot flag. Called first by the composition root and by tests.</summary>
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
| `AtlasApp` | `Atlas.App` | `AtlasApp` itself (everything the root built, as properties, §8) | no |
| `ParamRegistry` | `Atlas.Link` | `AtlasApp` (the one instance) | yes |
| `IFrameLoop` | `Atlas.World` | `AtlasApp`: the `AtlasFrameLoop` component | yes (lane D's own loop) |
| `ISpaceHost` | `Atlas.World` | `AtlasApp`: `SpaceLab` | yes (lane D's own host) |
| `IQualityState` | `Atlas.World` | `AtlasApp`: `Atlas.App.QualityLadder` | no |
| `IOperatorCommands` | `Atlas.World` | `AtlasApp`: `CommandRouter` | no |
| `IWalkerKernel`, `IDialKernel` | `Atlas.Kernel` | `AtlasApp`: one `HolonomyWalkerKernel` under both keys (lane B §7.2) | `IWalkerKernel` yes, with the Spectator's own kernel (lane D §5.9); `IDialKernel` no |
| `MaterialPool` | `Atlas.Rendering` | `AtlasApp` (`MaterialPool.CreateDefault(256)`) | yes |
| `IStereoRig` | `Atlas.Rendering` | `AtlasApp`: a `GuardedStereoRig` round the `StereoRig` (§9.2) | no |
| `StereoRig` | `Atlas.Rendering` | `AtlasApp`: the concrete rig too, because FieldFit needs its eye-target override (§9.2). The headset refusal is enforced only through `IStereoRig` | no |
| `PortalRenderer` | `Atlas.Rendering` | Nobody in this build. `PortalSpace` uses a published one if there is one, and otherwise creates and drives its own (§12.2) | no |
| `RoomHandOver` | `Atlas.App` | `AtlasApp` (§4.2) | no |
| `IAtlasInput` | `Atlas.Input` | `AtlasApp`, from `AtlasInput.Install` | lane D's page |
| `IHands`, `IReachLayout` | `Atlas.World` | `AtlasApp`, from `HandsInstaller.Install`: the one `HandsRuntime` under both keys (lane C §7.7), withdrawn by `HandsSystem.Shutdown`. A view asks with `TryGet`: the Spectator, and a test that installs no hands, have neither | no |
| `IPerfSource`, `IEventLog` | `Atlas.World` | lane D's telemetry installer | lane D's page |
| `IRoomContent`, `IPosePipeline`, `IScanSession`, `IBumpGuard`, `IObjectPhotoSink` | their pages | their installers | their pages |

---

## 7 · The frame loop

`Atlas.App.AtlasFrameLoop` is the only script that reads the head pose, steps the kernel or ticks a space
(README F5). The component brings the engine's clock and callbacks; the order itself is the engine-free
`Atlas.App.FrameLoopCore`, which tests drive with their own clock.

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
        /// <summary>kernel.ResetToFloor(geometry head floor point); the next FloorDelta is zero.</summary>
        void ResetWalkerToFloor();
        /// <summary>kernel.Reset(pose); the next FloorDelta is zero.</summary>
        void ResetWalker(in Atlas.Kernel.WalkerPose pose);
    }

    /// <summary>The six operator commands as local calls (Master Plan section 7.3.2). No link type appears here.</summary>
    public interface IOperatorCommands
    {
        /// <summary>SceneCmd. variant may be null; its meaning is the space's own.</summary>
        void SwitchSpace(SpaceId space, string variant);
        void Home();
        void Recentre();
        /// <summary>-1 automatic, 0 to 3 the ladder rungs, 4 the high tier.</summary>
        void SetQuality(int tier);
        /// <summary>The Console's STOP button: the warning cue at STRONG.</summary>
        void Cue();
        void SetLock(bool on);
    }
}
```

`Atlas.Input` references no Atlas assembly (README F1), so it cannot implement `ISenseStep`.
`Atlas.App.InputSenseStep` is the adapter: at step 1, after the pose pipeline, it calls
`IAtlasInput.Tick(frame.RawHeadPosition, frame.RawHeadRotation, frame.DeltaTime)` and hands the keyboard walker
its input. `Atlas.App.BumpGuardStep` implements `ISafetyStep` and builds almost nothing itself: the pose pipeline
fills its `SensorFrame` at step 1 (raw pose, tracking, depth), and the step calls `IBumpGuard.Tick(in pipeline.Sensors)`.
The one thing it adds: given an `IDepthMaskSource` (the tracked hands), a frame that holds a new depth image goes to
Bump Guard as a copy that carries the mask for that image, and a source that throws is counted in
`BumpGuardStep.MaskFaults` while Bump Guard ticks on the frame as it was (lane C §7.3).

**The tracked hands** (`Atlas.Hands`) are three more implementations of the hooks above. `HandsInstaller.Install`
builds them and the composition root adds them in this order (§8.2): `HandsSenseStep` (step 1), after the pose
pipeline's step and before `InputSenseStep`, so that a touch or a pinch reaches this frame's input snapshot;
`HandsUploadStep` (step 6), after `RigUploadStep`: it skins the two hands and places the contact marks (§10.9);
`HandsAfterFrameStep` (step 8b), before `QualityLadder`: it logs the hands' edge events and plays their sounds. The
hands' own main-thread time, sense and upload together, feeds their self-governor (lane D §1.4, `hands.rate`).
`FrameLoopCore` puts a net under the hooks of step 1 and none under those of steps 6 and 8b (§7.2, rule 9), so the
hands' three hooks each catch their own exceptions: an exception in any of them switches the hands off for the
session and is logged once as `hands.fault`, and the frame carries on (decision R9 of the hands brief).

Four more interfaces, in `App/Frame/FrameLoopCore.cs` (namespace `Atlas.App`), connect the loop to its
neighbours without an engine type:

```csharp
/// <summary>The space host as the frame loop drives it: steps 5, 6 and 8a of the frame.</summary>
public interface ISpaceFrame
{
    void TickSpace(in FrameContext frame);
    void UploadSpace(in FrameContext frame);
    void WriteState(ref StateMessage state);
}

/// <summary>The walker resets the space host needs from the frame loop.</summary>
public interface IWalkerReset
{
    void ResetWalkerToFloor();
    /// <summary>A room was made active: the walker is reset straight after step 3 of the first frame whose
    /// head pose is in the room frame, before that frame's kernel step.</summary>
    void LatchRoomReset();
    bool RoomResetPending { get; }
}

/// <summary>A frame hook that also owns fields of the State (Bump Guard: level and cue flag).</summary>
public interface IStateWriter { void WriteState(ref StateMessage state); }

/// <summary>Where the two eyes are this frame. Implemented over the stereo rig (RigEyePositions).</summary>
public interface IEyePositions { void GetEyes(out float3 left, out float3 right); }
```

### 7.2 The call sequence

```csharp
// File: App/Frame/AtlasFrameLoop.cs
[DefaultExecutionOrder(100)]                     // after TrackedPoseDriver (Update only) and every other Update
public sealed class AtlasFrameLoop : MonoBehaviour, IFrameLoop
{
    public FrameLoopCore Core { get; }
    /// <summary>Creates the component, disabled, on a new object "FrameLoop" under parent.</summary>
    public static AtlasFrameLoop Create(Transform parent, FrameLoopCore core);

    void Update()     { _core.RunUpdate(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime); }   // 1 to 6
    void LateUpdate() { _core.RunState(); }                                                     // 8a
    void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras) { _core.RunAfterFrame(); }   // 8b
}

// File: App/Frame/FrameLoopCore.cs   (shape only)
public sealed class FrameLoopCore : IFrameLoop, IWalkerReset
{
    public FrameLoopCore(IWalkerKernel kernel);
    public ISpaceFrame Space { get; set; }                   // SpaceLab; null: no space is ever ticked
    public ISpaceHost Host { get; set; }                     // for the common fields of the State
    public IPosePipeline Pipeline { get; set; }
    public IQualityState Quality { get; set; }               // null: Nominal
    public IEyePositions Eyes { get; set; }                  // null: both eyes at the head
    public Action<string> Warning { get; set; }
    public int FrameCount { get; }
    public bool HasGuard { get; }
    public ref readonly StateMessage LastState { get; }
    public void Seal();                                      // after boot: a hook added later is reported

    public void RunUpdate(double time, float deltaTime)
    {
        if (the last frame was never rendered) { RunState(); RunAfterFrame(); }   // its tail is not lost
        // BeginFrame:                                             FrameIndex, Time, DeltaTime, Tier
        for (i) _sense[i].Sense(ref _frame);                 // 1  pose and depth, then input
        Eyes.GetEyes(out _frame.LeftEyePosition, out _frame.RightEyePosition);   // from IStereoRig
        _guard.Guard(in _frame);                             // 2  Bump Guard
        _filter.Filter(ref _frame);                          // 3  pose filter (writes FloorDelta too)
        if (_roomResetLatched && Pipeline.Sensors.RoomActive) { ResetWalkerToFloor(); Pipeline.ResetGeometry(); }
        _frame.Kernel = _kernel.Step(new double2(_frame.HeadPosition.x, _frame.HeadPosition.z),
                                     _frame.Tracking == TrackingState.Normal, _frame.Time);   // 4
        _frame.Walker = _kernel.Pose;
        Space.TickSpace(in _frame);                          // 5  ISpaceModule.Tick
        Space.UploadSpace(in _frame);                        // 6  the module's own IUploadStep, if any
        for (i) _upload[i].Upload(in _frame);                // 6  LookUploadStep, RigUploadStep, HandsUploadStep, TileStatsUploadStep
    }
    // 7  URP renders Eye Left, Eye Right, then the Screen camera with the lens blit

    public void RunState()                                   // 8a, once per frame
    {
        FillState(ref _state);                               //    common fields, section 2
        Space.WriteState(ref _state);                        //    ISpaceModule.WriteState while a space is entered
        _sink.Send(in _state);
    }

    public void RunAfterFrame()                              // 8b, once per frame (a second call does nothing)
    {
        for (i) _after[i].AfterFrame(in _frame);
    }
}
```

| Step (§6.3) | Call | Hook declared in | Implemented by | Lane |
|---|---|---|---|---|
| 1 Read | `ISenseStep.Sense(ref frame)`, each in order | `Atlas.World` | Pose pipeline in `Atlas.Scan`: moves the XR Origin so the room anchor is the world origin (README F4), then fills `RawHeadPosition`, `RawHeadRotation`, `Tracking` from `AtlasBootContext.Head`, takes the newest depth image and builds the frame's `SensorFrame` | C |
| 1 Read | same | `Atlas.World` | `Atlas.Hands.HandsSenseStep`, added after the pose pipeline's step and before `InputSenseStep`: polls the hand detector, lifts and filters the hands, runs the gate, builds this frame's depth mask for Bump Guard, places the near anchor and hands the fingertips and pinch edges to `IHandsInput` | the hands (lead) |
| 1 Read | same | `Atlas.World` | `Atlas.App.InputSenseStep`, added after the hands' step so the gaze sees this frame's head and fingertips: freezes the input snapshot | A (adapter), C (input) |
| 1 Read | `IEyePositions.GetEyes` | `Atlas.App` | `RigEyePositions` over `IStereoRig` | A |
| 2 Bump Guard | `ISafetyStep.Guard(in frame)` → `IBumpGuard.Tick(in SensorFrame)` | `Atlas.World`; `Atlas.Safety` | `Atlas.App.BumpGuardStep` and lane C's Bump Guard. The step puts the hands' depth mask on a copy of the frame (§7.1) | C (A adapter) |
| 3 Pose filter | `IPoseFilterStep.Filter(ref frame)` | `Atlas.World` | Pose pipeline in `Atlas.Scan` | C |
| 4 Kernel | `IWalkerKernel.Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds)` | `Atlas.Kernel` | `HolonomyWalkerKernel` (flat, {4,5}, {4,6} or the sphere, by configuration) | B |
| 5 Space | `ISpaceModule.Tick(in frame)` | `Atlas.World` | The entered module; skipped when none | space owner |
| 6 Upload | `IUploadStep.Upload(in frame)`: the module first, then the registered hooks | `Atlas.World` | `LookUploadStep` (`AtlasLook.Upload`: `_HeadPosWS`, `_HypL`, fog cross-fade, `_AtlasTime`, and every other global again), then `RigUploadStep` (the prediction offset), `HandsUploadStep` (the two hand meshes and the contact marks, §10.9), `TileStatsUploadStep` (the drawn-tile count for telemetry); a `PortalRenderer` is driven from `PortalSpace`'s own `Upload`; lane B's tile renderer, room view and markers from the module's own `Upload` | A, B, the hands |
| 7 Render | Unity and URP | – | Eye cameras into their textures, one lens blit to the screen (§9) | A |
| 8a State | `ISpaceModule.WriteState(ref state)`, `IStateSink.Send(in state)` | `Atlas.World` | The module; lane D's link glue in `Atlas.Telemetry` | space owner, D |
| 8b After the frame | `IAfterFrameStep.AfterFrame(in frame)`, each in order | `Atlas.World` | In this order: lane D's perf probe (samples counters); lane D's link pump (drains received parameters and commands; commands go to `IOperatorCommands`); lane D's HUD; hooks the asset client, the menu and onboarding added at rows 18 and 19; `HandsAfterFrameStep` (the hands' edge events and sounds); `QualityLadder`; `RehearsalFlow`; `RoomHandOver`; `SpaceActionStep`; `SpaceSigns`; `SpaceLab` (advances a switch, so a switch starts in the frame that asked for it) | D, C, A, the hands |

Rules:

1. Steps 1 to 6 are plain calls in this order from one `Update`. Nothing in them waits for the network, a file or
   a job that was not scheduled in the same frame.
2. Step 2 runs every frame from the first, in every display mode and during every fade and load. A step-1 hook
   that throws does not stop it: the fault is reported once through `FrameLoopCore.Warning`, the hook is called
   again next frame, and the frame goes on with what it has.
   A device build with no guard registered is a boot error: the app shows "Bump Guard missing"
   (`HeadsetGate.Message`) and `GuardedStereoRig` refuses `SetMode(DisplayMode.Headset)` (decided here). In the
   Editor a missing guard is a warning.
3. Step 4 is called exactly once per frame, also while tracking is lost and also with no space entered.
   `IWalkerKernel.StepCount` rises by one per frame (`P1-B02`).
4. A missing optional hook has a neutral default: no sense step → the raw pose stays as it was last set and
   `Tracking` is `Normal` (`HeadTransformSenseStep` reads the pose from a head transform where there is no pose
   pipeline); no filter → the geometry pose is the raw pose and the loop forms `FloorDelta`; no sink → no State.
5. Step 8a runs in `LateUpdate` of the same component (§7.2 of the plan). Step 8b runs from
   `RenderPipelineManager.endContextRendering`, after both eyes and the lens blit have been submitted; the
   Editor raises that event once per view, so the core runs 8b only for the first one of a frame (decided here).
   A frame that was never rendered (no camera, a paused Editor view) gets its 8a and 8b at the start of the next
   `Update`, so a space switch and the link keep moving.
   (differs from plan section 6.3: step 8 is two halves, `State` at 8a before rendering, counters and received
   parameters at 8b after it.)
6. Received parameters and commands are applied at 8b and take effect in the next frame. A parameter handler may
   swap eye-texture sets, rebuild geometry or start a switch there; it must not block.
7. Hooks are added during boot only. The composition root calls `FrameLoopCore.Seal()` after
   `AtlasServices.MarkBooted()`; a hook added later is still added, and reported through `Warning`.
8. Nobody else has an `Update` that does per-frame work on the frame path. Systems created by an installer may
   have `Update` for work that is not on the frame path (file writers, reconnect timers).
9. `FrameLoopCore` puts a net under the hooks of step 1 (rule 2) and under none of the hooks at steps 6 and 8b: an
   exception from one of them ends the remaining hooks of that step for the frame and leaves the loop. A hook added
   there that must not take the frame down catches its own. The tracked hands' do: they switch themselves off for
   the session, log `hands.fault` once and leave the frame as it was (decision R9 of the hands brief).

`CommandRouter` (`Atlas.App`, lane A) implements `IOperatorCommands`:

| Command | Local call |
|---|---|
| `SwitchSpace(space, variant)` | Sets the variant's parameter (`CommandRouter.VariantParam(space)`: `glued.mode`, `rooms.layout`, `slicer.object`, §14) to the enum value whose name equals `variant` without regard to case, or to `variant` read as an index; then `ISpaceHost.RequestSwitch(space)` |
| `Home()` | `ISpaceHost.ReturnHome()` |
| `Recentre()` | `IFrameLoop.ResetWalkerToFloor()`, then `IPosePipeline.ResetGeometry()` |
| `SetQuality(tier)` | Sets `thermal.pinTier` to the tier clamped to −1 … 4 (§9.5) |
| `Cue()` | `BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator)` (`CommandRouter.FireOperatorCue`) |
| `SetLock(on)` | Sets the parameter `demoLock` (lane D's page) |

Parameters are set with `ParamSource.Remote`. Each command takes effect within 250 ms (test L10) and is written
to the event log by the caller.

`CommandRouter(ISpaceHost host, IFrameLoop loop, ParamRegistry registry, IPosePipeline pipeline = null, Action cue = null)`.

`Atlas.App.SpaceActions` (`App/Commands/SpaceActions.cs`) turns the four SpaceLab actions of the input map into
the same local calls: `AtlasAction.ReturnHome`, `NextSpace`, `PrevSpace` and `SpaceMenu`. `SpaceActionStep` reads
them at step 8b, one per frame. Nothing happens while the phone is in the hand, while a switch is under way or
while `demoLock` is on.

---

## 8 · Composition root

`Atlas.App.AtlasBootstrap` is the entry point and `Atlas.App.AtlasApp` builds everything that is not in
`Atlas_Boot.unity` (README F3).

```csharp
// File: App/AtlasBootstrap.cs
namespace Atlas.App
{
    public static class AtlasBootstrap
    {
        public const string BootSceneName = "Atlas_Boot";
        public const string SandboxPrefix = "Sandbox_";
        public const string SandboxMarker = "AtlasBoot";

        /// <summary>The running app, or null.</summary>
        public static AtlasApp App { get; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot();

        /// <summary>The rule of which scene boots the app. Engine-free.</summary>
        public static bool ShouldBoot(string sceneName, bool hasSandboxMarker);
        /// <summary>Builds the app in the loaded scene. Also the entry point of sandboxes and play-mode tests.
        /// A second call while an app runs returns the running one. Null when the boot failed.</summary>
        public static AtlasApp Start(BootOptions options = null);
        /// <summary>Stops the running app and releases what it made. Safe to call with none running.</summary>
        public static void Stop();
    }
}

// File: App/AtlasApp.cs
namespace Atlas.App
{
    /// <summary>The three objects of the boot scene the composition root starts from.</summary>
    public struct BootScene
    {
        public ARSession Session;
        public XROrigin Origin;
        public Camera Camera;
        /// <summary>The one scene lookup of the app. What is absent (a sandbox, a test) is created in code.</summary>
        public static BootScene FindOrCreate();
    }

    /// <summary>Everything the composition root built, in one place. Published in AtlasServices.</summary>
    public sealed class AtlasApp
    {
        public const int PoolCapacity = 256;
        public static AtlasApp Create(BootScene scene, BootOptions options);
        public void Shutdown();
        // One read-only property per system: Boot, Options, Registry, Pool, Lens, Rig, Display, Eyes, Blit, Loop,
        // LoopCore, WalkerKernel, InputService, Pose, ScanSystems, ScanSettings, Guard, GuardInstalled,
        // RoomContent, Ladder, Spaces, Commands, SpaceActions, TelemetrySystems, Assets, HandOver, SourceSwitch,
        // Flow, Overlay, Onboarding, Menu, PerfTour, Hands (the HandsSystem: never null, off until hands.enabled is set).
    }
}
```

`Boot` runs only when the scene that was loaded is `Atlas_Boot`, or is named `Sandbox_…` and holds a root object
named `AtlasBoot` (decided here). In any other scene it does nothing, so EditMode and PlayMode tests are not
disturbed. It does nothing in a build with `ATLAS_SPECTATOR`: the Spectator composes its own root (lane D).
`BootOptions.Parse` reads the command line: `-atlasRoom <fixture or file>`, `-atlasReplay <pose log>`,
`-atlasSource tracked|replay|walker`, `-atlasSpace S0…S6`, `-atlasHand`. `Stop` runs on `Application.quitting`.

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
Atlas                           created by AtlasApp; DontDestroyOnLoad
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

`AtlasApp.Build` makes the rows below in order. Each lane exposes one static installer per assembly (decided
here); the calls the root makes are given in the table. It creates its systems under `boot.Systems`, publishes
its services and adds its frame hooks. `Atlas.Input` cannot see `AtlasBootContext`, so its installer takes the
head-locked parent and returns the input service, which the root publishes. The order is binding.

| # | What is created | By | Lane | Under | Publishes · hooks |
|---|---|---|---|---|---|
| 0 | `FramePacing.TargetFrameRate = 60`, `Screen.sleepTimeout = NeverSleep`, the developer watermark off; frame rate, VSync and screen MSAA re-asserted before every frame's rendering | `Atlas.Rendering.AtlasBoot`, `BeforeSceneLoad` | A | – | – |
| 0 | Space registrations; `AppHooks` factories of the menu, onboarding and the perf tour | each assembly, `BeforeSceneLoad` (§5.1) | A, B, C, D | – | `SpaceRegistry`, `AppHooks` |
| 1 | `AtlasServices.Clear()`; the `Atlas` objects of §8.1; `AtlasBootContext` | `AtlasApp` | A | – | `AtlasApp` |
| 2 | `ParamRegistry` | `AtlasApp` | A (type: D) | – | `ParamRegistry` |
| 3 | Look: palette built and bound, shader globals at their defaults, `RenderSettings.skybox` | `AtlasLook.Initialise()` | A | – | – |
| 4 | `MaterialPool` (256 instances) | `MaterialPool.CreateDefault(AtlasApp.PoolCapacity)` | A | – | `MaterialPool` |
| 5 | `LensModel` (screen size from `Screen.dpi`); `StereoRig`: `Atlas Head`, `Atlas Eyes`, eye cameras, `Hud`; `GuardedStereoRig`. The tracked camera stops drawing layers 6, 7 and 23 | `StereoRig.Create(boot.TrackedCamera, lens)` | A | `Main Camera` | `IStereoRig` (the guarded rig) |
| 6 | `EyeTextureSet` (four sets), `Screen` camera, `LensBlit` | `new EyeTextureSet(Screen.width, Screen.height)`, `Bind`, `LensBlit.Create(root, rig, eyes, lens)` | A | `Atlas/Screen` | – |
| 7 | – (no shared `PortalRenderer` in this build; §12.2) | – | A | – | – |
| 8 | Kernel: `new HolonomyWalkerKernel()` (flat until a curved space configures it) | `AtlasApp` | A (type: B) | – | `IWalkerKernel`, `IDialKernel` |
| 9 | `FrameLoopCore`; `AtlasFrameLoop` (disabled until row 20) | `AtlasFrameLoop.Create(boot.Systems, core)` | A | `Systems/FrameLoop` | `IFrameLoop` |
| 10 | Input | `AtlasInput.Install(boot.Hud, material)` | C | `boot.Hud` | `IAtlasInput` |
| 11 | AR session set-up, pose sources and pipeline; scan session, room store, photo capture, world maps | `ArPoseSystems.Create(boot, session, logs)`, `ScanSystems.Create(pose)`, each with `Publish(loop)` | C | `Systems` | `ISenseStep`, `IPoseFilterStep`; `IPosePipeline`, `IScanSession` and their own types |
| 11 | The tracked hands: their source (the phone's detector, the Editor's mouse and keys, or none), the runtime, the hand meshes with their marks, the sounds; then `InputSenseStep` | `HandsInstaller.Install(new HandsPorts { … })`, kept as `AtlasApp.Hands`; then `new InputSenseStep(input, pipeline, remoteWalk)` | the hands (lead); A for the step | `Systems` (the object "Atlas Hands") | `IHands`, `IReachLayout`; `AddSense(Hands.Sense)`, then `AddSense(InputSenseStep)` |
| 12 | Bump Guard and its cue (the cue under `boot.Hud`) | `SafetyInstaller.Install(boot, material)` | C | `Systems` | `IBumpGuard`; the root wraps it in `BumpGuardStep(guard, pipeline, Hands.Mask)` → `SetGuard`. If it throws: `Display.HeadsetLocked` on a device |
| 13 | Room content | `RoomContentInstaller.Install(boot, pool)` (`Atlas.Rendering.Room`, lane C files) | C | `Atlas/Room` | `IRoomContent` |
| 14 | Upload hooks, in this order: `LookUploadStep`, `RigUploadStep` (lane A) and `Hands.Upload` (the hands) | `AtlasApp` | A | – | `AddUpload` × 3 |
| 15 | `QualityLadder` round a `QualityApplier` on the eye textures; then `FurnitureLevelPolicy(RoomContent, Ladder)`, made here because it needs the room content (row 13) and the ladder, and kept as `AtlasApp.Furniture` | `AtlasApp` | A | – | `IQualityState` |
| 16 | `GridSpace` registered as Home when there is no Home space; `SpaceLab` with `SceneSpaceLoader` and `SpaceStage(RoomContent, Furniture)`; then the parameters of §9.6 (`AppParams`, with `DeclareRender` given `Furniture` as its last argument) and `SpaceRegistry.DeclareAllParams(registry)` | `AtlasApp` | A | – | `ISpaceHost` |
| 17 | `CommandRouter` | `AtlasApp` | A | – | `IOperatorCommands` |
| 18 | Link client, perf probe, HUD, event log, pairing; the asset client; then `RoomHandOver`, `RehearsalFlow`, `DevOverlay` and the `dev.*` parameters | `TelemetryInstaller.Install(boot)`, `AssetClientInstaller.Install(boot, telemetry)`; `AtlasApp` | D, A | `Systems`; HUD under `boot.Hud` | `SetStateSink`; `IPerfSource`, `IEventLog`, `IObjectPhotoSink`; `AddAfterFrame` × 3 (probe, pump, HUD); `RoomHandOver` |
| 19 | On-device menu, onboarding, perf tour; then, last of all the declarations, the hands' parameters (§9.6) | `AppHooks.CreateMenu()`, `CreateOnboarding()`, `CreatePerfTour()`, then `Install(app)` on each; `Hands.DeclareParams(Registry)` | D, C, A | `Systems` | their own |
| 20 | `AddAfterFrame` × 7: `Hands.AfterFrame`, `QualityLadder`, `RehearsalFlow`, `RoomHandOver`, `SpaceActionStep`, `SpaceSigns`, `SpaceLab`; `ParamRegistry.PushAll()`; the frame loop is enabled; `AtlasServices.MarkBooted()`; `FrameLoopCore.Seal()`; `Onboarding.Begin()` | `AtlasApp` | A | – | – |

Rows 10 and 12 get one pooled material for head-locked content (fog weight 0, fade weight 0); the reticle and
the cue use it only when their own shader is missing (§10.1).

The folders composed after the root (`App/Menu`, `App/Onboarding`, `App/PerfTour`) announce themselves the way
a space does: one `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` method sets a factory in `AppHooks`.

```csharp
// File: App/Hooks/AppHooks.cs
public interface IOnboardingFlow { bool OwnsFlow { get; } void Install(AtlasApp app); void Begin(); void Shutdown(); }
public interface IAppMenu       { void Install(AtlasApp app); void Shutdown(); }
public interface IPerfTour      { void Install(AtlasApp app); bool Running { get; } void Shutdown(); }

public static class AppHooks
{
    public static Func<IOnboardingFlow> Onboarding;
    public static Func<IAppMenu> Menu;
    public static Func<IPerfTour> PerfTour;
    public static IOnboardingFlow CreateOnboarding();   // an empty slot gives NullOnboarding
    public static IAppMenu CreateMenu();                // NullMenu
    public static IPerfTour CreatePerfTour();           // NullPerfTour
    public static void Clear();
}
```

After row 20 onboarding owns the flow when `IOnboardingFlow.OwnsFlow` is true: permissions, pairing, scan,
outline confirmation, headset prompt, then `RoomHandOver.Activate(room, anchor)`,
`IStereoRig.SetMode(DisplayMode.Headset)` and `RoomHandOver.EnterNow()` (`HandOver.AutoEnter` is false then).
Otherwise `Atlas.App.RehearsalFlow` owns it: it runs the scan session or takes a fixture room (`dev.room`, or the
room a replayed walk names), hands the room over with `AutoEnter` set and, off the device, puts the display in
the headset view by itself unless `-atlasHand` was given. The display starts in `DisplayMode.Hand` (§9.2).

`AtlasBootstrap` builds nothing in the Spectator build (`ATLAS_SPECTATOR`). Lane D's `SpectatorApp` composes its
own root and publishes `ParamRegistry`, `MaterialPool`, `IWalkerKernel`, `ISpaceHost` and `IFrameLoop`; a space
must not assume any other service is there (use `AtlasServices.TryGet`).

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
(differs from plan task P1-A03: one URP asset instead of the tier assets Nominal / Fair / Serious.)

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
| Renderer features | Those of §12.2, and the template's AR background feature, which stays because the hand-mode AR camera draws the camera image through this renderer. It enqueues its pass only for a camera with an enabled `ARCameraBackground`, so the eye and Screen cameras carry nothing from it (§9.2) | – |
| Screen MSAA | `AtlasBoot.EnforceScreenMsaa()` before every frame's rendering: `Screen.SetMSAASamples(1)` whenever the screen has more; `Camera.allowMSAA = false` on the Screen camera (U §6.3) | – |
| Watermark | `Watermark.showDeveloperWatermark = false` at boot, or URP stores the MSAA colour (U §6.4) | – |
| Frame rate | `FramePacing.TargetFrameRate` (60; 30 on `Critical`), written to `Application.targetFrameRate` by `AtlasBoot.ApplyPacing()` before every frame's rendering, with VSync count 1 at 60 and 2 at 30; ProMotion off; "Adjust iOS FPS based on thermal state" off | §8.2, C.4 |
| AR session | `ARSession.matchFrameRateRequested = false`, or the session overwrites the frame rate every update (U §12.4). Set by lane C's AR set-up | – |
| Unity fog, ambient probes, reflection probes | off; renderers use `lightProbeUsage = Off`, `reflectionProbeUsage = Off` | D23 |

`AtlasProjectSetup` (lane A, Editor) writes every row that lives in an asset or in Project Settings. Its render
part (`AtlasProjectSetup.ApplyRenderSetup()`: URP asset, renderer and features, layer names) also runs by itself
when the Editor loads a project whose assets are behind the code, and at the start of every player build.

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
        /// <summary>Switches between hand and headset display.</summary>
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

`StereoRig` (`Rendering/Stereo/StereoRig.cs`) is the component on `Atlas Head` that implements it. Beyond the
interface it has:

```csharp
public const float DefaultIpd = 0.064f, MinIpd = 0.055f, MaxIpd = 0.075f;
public const float NearPlane = 0.05f, FarPlane = 60f;
/// <summary>Builds "Atlas Head", "Atlas Eyes", the two eye cameras and "Hud" under the tracked camera. Starts in Hand.</summary>
public static StereoRig Create(Transform trackedCamera, LensModel lens);
public LensModel Lens { get; }
/// <summary>Raised by SetMode after the cameras have been switched.</summary>
public event Action<DisplayMode> ModeChanged;
/// <summary>The frustum of an eye as tangents, from the lens model.</summary>
public EyeFrustum GetEyeFrustum(Eye eye);

// Eye-target override (lane B's FieldFit, plan §10.4.3). Added to the concrete rig, not to IStereoRig.
/// <summary>The eye cameras draw into these textures instead of the eye textures, with a symmetric frustum of the
/// given vertical field of view and each texture's own aspect (explicit projection, per eye: perspective(fov,
/// width / height, 0.05, 60)). Re-asserted every frame in beginContextRendering, so a quality-ladder swap of the eye
/// textures cannot undo it. Calling it again replaces the textures and the field of view.
/// ArgumentNullException for a null texture.</summary>
public void OverrideEyeTargets(RenderTexture left, RenderTexture right, float verticalFovDegrees);
/// <summary>Gives the cameras back the eye textures the EyeTextureSet last assigned, and the lens projection.</summary>
public void ClearEyeTargetOverride();
public bool EyeTargetsOverridden { get; }
public RenderTexture GetEyeTargetOverride(Eye eye);     // null when not overridden
```

`SetMode` switches the cameras at once. What `AtlasServices` publishes as `IStereoRig` is
`Atlas.App.GuardedStereoRig`, which passes everything through to the rig except that `SetMode(Headset)` is
refused while `HeadsetLocked` is set (a phone with no Bump Guard, §7.2 rule 2); `Refused` is called with the line
to show.

| Item | Value | Source |
|---|---|---|
| Eye offset | Local x = ∓`Ipd` / 2 under `Atlas Eyes`; no toe-in, parallel axes | D7: ±32 mm |
| Projection | From `LensModel.GetProjection(eye, near, far)`, assigned to `Camera.projectionMatrix`: how far the eye sees through its lens to each edge of its half of the screen, clamped to 50.28° to each side and 48° up and down (`LensModel.MaxFovHorizontalDegrees`, `MaxFovVerticalDegrees`). With the default lens numbers the screen reaches past both limits, so the frustum is symmetric: vertical field of view **96°**, aspect **1398 / 1290** = 1.0837. It turns off-axis only where the screen ends before a limit (a narrow lens separation, a vertical lens offset) | §8.5 "about 50° to each side and 48° up and down" (decided here) |
| Near, far | 0.05 m, 60 m (decided here). Fog is opaque long before the far plane (ρ·L is at most 10.6 m on the high tier) | – |
| Camera order | `Camera.depth`: Eye Left 0, Eye Right 1, Screen 10; whole numbers, because URP sorts by `(int)depth` (U §6.2) | – |
| Clear | Skybox: the background shader of §10.8. No AR camera background | §8.1 |
| Culling mask | Every layer except `XR Simulation` and `AtlasSpectator` (§12.1) | – |
| Camera data | `renderPostProcessing`, `renderShadows`, `requiresDepthTexture`, `requiresColorTexture`, `allowXRRendering` all false; `antialiasing` none; `renderType` Base; `allowMSAA` true (the eye texture's own sample count applies); `allowHDR`, dynamic resolution and occlusion culling off | U §5 |
| Stereo API of `Camera` | Not used: no `stereoTargetEye`, no stereo matrices (U §12.1) | – |

Display modes (decided here):

| | `Hand` | `Headset` |
|---|---|---|
| `Main Camera` (AR) | `Camera` and `ARCameraBackground` enabled; draws the camera image and scan visuals full screen. Its culling mask leaves out layers 6, 7 and 23, so head-locked content does not cover the scan picture | `Camera` and `ARCameraBackground` disabled. `ARCameraManager` and `TrackedPoseDriver` keep running |
| Eye cameras, Screen camera | disabled | enabled |
| Who switches | The app starts in `Hand`. Onboarding, or `RehearsalFlow` when no onboarding owns the flow, calls `SetMode(Headset)` at the headset prompt and `SetMode(Hand)` for a rescan | |
| UI | Screen-space UI allowed | **No screen-space overlay canvas may be active.** In-headset UI is world-space under `Hud` |
| Frame loop | Runs in full (Bump Guard included) | Runs in full |

The head every lane reads is `Atlas Head` (`AtlasBootContext.Head`), not `Main Camera`. `HeadOffset` moves it
from the device camera to the midpoint between the walker's eyes; the offset depends on the headset and the phone
and is zero until measured (open item 2).

### 9.3 Eye textures

```csharp
// File: Rendering/Stereo/EyeTextureSet.cs
namespace Atlas.Rendering
{
    /// <summary>What the quality ladder needs from the eye textures. Lets the ladder run with no engine.</summary>
    public interface IEyeTextures
    {
        float RenderScale { get; }                   // of the active set: 0.8, 0.7 or 0.6
        int Msaa { get; }                            // of the active set: 4, 2 or 1
        void Select(float renderScale, int msaa);
    }

    /// <summary>The eye render textures: four sets allocated once at boot (Master Plan sections 8.2, 8.6).</summary>
    public sealed class EyeTextureSet : IEyeTextures, System.IDisposable
    {
        /// <summary>Allocates the four sets for a screen of the given pixel size (the longer side is the width).
        /// Pass Screen.width and Screen.height; a size that is not positive gives the 1398 x 1290 half-screen.</summary>
        public EyeTextureSet(int screenWidth, int screenHeight);

        public int HalfScreenWidth { get; }
        public int HalfScreenHeight { get; }
        public float RenderScale { get; }
        public int Msaa { get; }
        public UnityEngine.RenderTexture Left { get; }
        public UnityEngine.RenderTexture Right { get; }
        public UnityEngine.Rendering.RTHandle LeftHandle { get; }    // what the lens pass imports
        public UnityEngine.Rendering.RTHandle RightHandle { get; }

        /// <summary>Gives the eye cameras their targets: now, and again after every swap.</summary>
        public void Bind(UnityEngine.Camera leftCamera, UnityEngine.Camera rightCamera);
        /// <summary>
        /// Makes the set for (renderScale, msaa) the target of the eye cameras and the source of the lens blit.
        /// A preallocated set is swapped in with no allocation. Any other pair re-creates one custom set.
        /// renderScale snaps to 0.6, 0.7 or 0.8; msaa to 1, 2 or 4.
        /// </summary>
        public void Select(float renderScale, int msaa);
        /// <summary>Raised after a swap, with the new textures in place.</summary>
        public event System.Action Changed;
        public void Dispose();
    }
}
```

Sizes and sample counts are the engine-free `EyeTextureLayout` (`Rendering/Stereo/EyeTextureLayout.cs`):
`DefaultHalfWidth = 1398`, `DefaultHalfHeight = 1290`, `SetCount = 4`, `SetScale(set)`, `SetMsaa(set)`,
`HalfScreen`, `SnapScale`, `SnapMsaa`, `Scaled`, `Size`, `FindSet`.

| Set | Render scale | Size, px | MSAA | Memoryless | Rung that uses it | Resolved colour, both eyes |
|---|---|---|---|---|---|---|
| 0 | 0.8 | 1118 × 1032 | 4× | `MSAA \| Depth` | Nominal, High | 9.2 MB |
| 1 | 0.7 | 978 × 904 | 4× | `MSAA \| Depth` | Fair | 7.1 MB |
| 2 | 0.6 | 838 × 774 | 2× | `MSAA \| Depth` | Serious | 5.2 MB |
| 3 | 0.6 | 838 × 774 | off (1) | `Depth` | Critical | 5.2 MB |

- Size = 2 · round(w · scale / 2) by 2 · round(h · scale / 2) for a half-screen of w × h, halves rounding up
  (decided here: even sizes; the 0.8 row is the plan's 1118 × 1032). The table is for w × h = 1398 × 1290.
- The half-screen is taken from the screen size given to the constructor: half the longer side by the shorter
  side, so a size read before the landscape orientation has settled still gives the right answer (U §12.3). Both
  phones have a 2796 × 1290 screen, which gives 1398 × 1290; that is also the fallback for a size that is not
  positive. In the Editor the sizes follow the Game view.
- Colour format `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)`: 8 bits per channel, sRGB (the project is
  linear). Depth-stencil format `SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil)`: the stencil is needed
  for portals (U §6.1, §11). No mipmaps, `bindMS` false, clamp, bilinear.
- **Memoryless rules.** Multisampled colour and depth are never stored; only the resolved colour is. The flags
  are set only when `SystemInfo.supportsMemorylessTextures`. Set 3 has no multisampled surface, so only its depth
  is memoryless. This holds on a Metal player build; the Editor and Windows resolve through an intermediate
  texture (U §6.4).
- A `render.msaa` or `render.scale` value that matches no set (for example 0.8 with 2×) creates one custom set
  and releases the previous custom set. That allocates; the ladder never does it.
- Nobody but `EyeTextureSet` assigns `Camera.targetTexture` of the eye cameras.

### 9.4 Lens blit

One full-screen pass, drawn by the `Screen` camera, samples both eye textures, applies the per-eye barrel
pre-distortion and upscales to the screen. It is the only full-screen pass in the app.

```csharp
// File: Rendering/Stereo/LensModel.cs
namespace Atlas.Rendering
{
    /// <summary>Optics of the headset as five numbers. Lengths in metres.</summary>
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

    /// <summary>The view frustum of one eye as four tangents, all positive.</summary>
    public struct EyeFrustum { public float TanLeft, TanRight, TanDown, TanUp; }

    /// <summary>The lens model: the distortion polynomial and its inverse, and the per-eye field of view and
    /// projection that follow from the lens numbers and the physical screen. Engine-free. Shared by the rig
    /// and the lens blit; Version changes with every edit.</summary>
    public sealed class LensModel
    {
        public const float DefaultK1 = 0.34f, DefaultK2 = 0.55f, DefaultLensSeparation = 0.064f,
                           DefaultScreenToLens = 0.039f, DefaultVerticalOffset = 0f;
        public const float DefaultMaxFovHorizontalDegrees = 50.28f, DefaultMaxFovVerticalDegrees = 48f;
        public const float DefaultScreenWidth = 0.1544f, DefaultScreenHeight = 0.0712f;

        public int Version { get; }
        public float K1 { get; set; }
        public float K2 { get; set; }
        public float LensSeparation { get; set; }
        public float ScreenToLens { get; set; }
        public float VerticalOffset { get; set; }
        public float MaxFovHorizontalDegrees { get; set; }
        public float MaxFovVerticalDegrees { get; set; }
        public float ScreenWidth { get; set; }
        public float ScreenHeight { get; set; }
        public LensParams Params { get; set; }
        /// <summary>Screen size from pixels and density. False, and the default size, when the density is unknown.</summary>
        public bool SetScreenFromPixels(int widthPixels, int heightPixels, float pixelsPerInch);
        public EyeFrustum GetFrustum(Eye eye);
        public Unity.Mathematics.float4x4 GetProjection(Eye eye, float near, float far);
        /// <summary>What the lens pass computes for one screen position. The shader repeats this arithmetic.</summary>
        public Unity.Mathematics.float2 ScreenToEyeUv(Unity.Mathematics.float2 screenUv, bool distortion, out Eye eye);
    }
}

// File: Rendering/Stereo/LensBlit.cs
namespace Atlas.Rendering
{
    public sealed class LensBlit : UnityEngine.MonoBehaviour
    {
        public const string ShaderResource = "AtlasLensBlit";
        public const string ShaderName = "Atlas/LensBlit";
        public const int ScreenCameraOrder = 10;

        /// <summary>Creates the "Screen" object under parent with the Screen camera and the lens pass.</summary>
        public static LensBlit Create(UnityEngine.Transform parent, StereoRig rig, EyeTextureSet eyes, LensModel lens);
        /// <summary>False: plain side by side, no distortion, each eye texture stretched over its half.
        /// Starts true on a phone and false in the Editor.</summary>
        public bool Distortion { get; set; }
        public LensParams Lens { get; set; }
        public LensModel Model { get; }
        /// <summary>Physical size of the screen in landscape. Metres.</summary>
        public Unity.Mathematics.float2 ScreenSizeMetres { get; }
        public UnityEngine.Camera ScreenCamera { get; }

        /// <summary>Non-null: the one final pass draws with this material (pass 0, through Blitter.BlitTexture) instead of
        /// the lens material, also when the lens shader is missing. FieldFit's composed blit; the material samples its
        /// own sources. Null gives the screen back to the lens blit.</summary>
        public static UnityEngine.Material FinalBlitOverride { get; set; }
        /// <summary>Where the ordinary lens blit samples an eye's texture for a point of that eye's half of the screen:
        /// eye 0 = left, screenUV in 0..1 per half (bottom left origin). LensModel.ScreenToEyeUv of the active LensBlit
        /// with its Distortion flag: a plain copy in the Editor, barrel on a phone. The identity when no LensBlit is enabled.</summary>
        public static UnityEngine.Vector2 SourceUV(int eye, UnityEngine.Vector2 screenUV);
    }
}
```

The pass itself is `LensBlitPass` (`Rendering/Stereo/LensBlitPass.cs`), a render-graph raster pass that
`LensBlit` enqueues for the Screen camera only, from `RenderPipelineManager.beginCameraRendering`.

| Parameter | Default | Range in the registry | Where the default comes from |
|---|---|---|---|
| `K1` | 0.34 | 0 to 1 | Public Cardboard viewer profile of 2015 ("Cardboard v2"), used as numbers only (C.3) |
| `K2` | 0.55 | 0 to 1 | same |
| `LensSeparation` | 0.064 m | 0.055 to 0.075 | same; equals the default IPD (D7) |
| `ScreenToLens` | 0.039 m | 0.025 to 0.060 | same |
| `VerticalOffset` | 0 m | −0.02 to 0.02 | Lens centres at mid-height of the screen (decided here) |
| `Distortion` | on a phone, off in the Editor | 0 or 1 | §8.2; off for the Editor and for hand checks |
| Field-of-view limits | 50.28° to each side, 48° up and down | – (properties of `LensModel`, not in the registry) | §9.2 |
| Screen size | From `Screen.width`, `Screen.height` and `Screen.dpi`; 0.1544 × 0.0712 m when the density is unknown or the long side is not between 0.10 and 0.20 m | – | 2796 × 1290 px at 460 px per inch |

These are placeholders until the headset is measured (open item 1). They are registry parameters (§9.6), so they
are tuned live in the headset with a straight-line grid and then baked.

The mapping, for a screen pixel at (x, y) metres from the screen centre (x right, y up), in the half of eye e:

```text
c   = ( −LensSeparation / 2 for the left eye, +LensSeparation / 2 for the right ,  VerticalOffset )
r   = ( (x, y) − c ) / ScreenToLens                    # tangent of the view angle before the lens
r'  = r · ( 1 + K1·|r|² + K2·|r|⁴ )                    # tangent of the angle the eye sees
uv  = ( r' + (tanLeft, tanDown) ) / ( tanLeft + tanRight , tanDown + tanUp )     # the frustum of eye e (section 9.2)
      # with the defaults the frustum is symmetric and this is  0.5 + r' / ( 2 · (tanH, tanV) ),
      # tanV = tan(96° / 2) = 1.1106 ,  tanH = tanV · 1398 / 1290 = 1.2036
colour = eye texture e at uv,  black outside [0, 1]²
with Distortion off:  uv = ( 2·u − (0 or 1) , v )  for screen uv (u, v)
```

The eye camera's axis passes through the lens centre, and the blit reads the same `EyeFrustum` the eye camera
renders with, so the two always agree. The pass writes the back buffer directly (U §7), does the sRGB
conversion itself when `Display.main.requiresSrgbBlitToBackbuffer` is true (U §11), and costs at most 1.0 ms of
GPU time (`P1-A05`). `LensModel.ScreenToEyeUv` is the same arithmetic on the CPU, for tests.

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
        public QualityRow(QualityTier tier, float renderScale, int msaa, float cullRho, int gluedCopies, int frameRate);
    }

    public static class QualityTable
    {
        public const int Count = 5;
        /// <summary>The row of a tier. An undefined value gives the Nominal row.</summary>
        public static QualityRow Row(QualityTier tier);
        /// <summary>Bits 4 to 5 of State.flags: 0 to 3; High is sent as 0.</summary>
        public static byte ToWire(QualityTier tier);
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

| Tier | Value | Trigger | Eye set (scale) | MSAA | ρ | Glued copies | Furniture | Frame rate | Console |
|---|---|---|---|---|---|---|---|---|---|
| `High` | 4 | Only `thermal.pinTier = 4` | 0.8 | 4× | 3.5 | 7 × 7 | Full allowed | 60 | – |
| `Nominal` | 0 | – | 0.8 | 4× | 3 | 5 × 5 | Full allowed | 60 | – |
| `Fair` | 1 | Thermal Fair, or GPU p95 over 9 ms | 0.7 | 4× | 3 | 5 × 5 | Light | 60 | Amber; swap at the next change of walker |
| `Serious` | 2 | Thermal Serious, or GPU p95 over 12 ms | 0.6 | 2× | 2 | 3 × 3 | Light | 60 | "Swap phone" |
| `Critical` | 3 | Thermal Critical, or still missing 60 fps on `Serious` | 0.6 | off | 2 | 3 × 3 | Light | 30 | "End the session" |

The Furniture column (mesh design §5.4) is the highest furniture level the tier allows. `Nominal` and `High` allow
`Full`, so a space keeps the level it asks for (§4.2). `Fair` and every tier below it force `Light` in every space,
so the first step below nominal, `Fair`, drops the furniture to its light copies in the same step in which it lowers the render scale from 0.8 to 0.7; the table has no step that changes only the furniture.
The column is a table in `Rendering/Quality/QualityFurniture.cs` (`QualityFurniture.Cap(QualityTier)` and
`QualityFurniture.Limit(FurnitureLevel spaceLevel, QualityTier)`), not a field of `QualityRow`, which lives in
`Atlas.World`; moving the column onto the row is a later change in that assembly. `FurnitureLevel` is `Atlas.World.FurnitureLevel`
(`Full = 0`, `Light = 1`).

The tracked hands follow the tier too, and the ladder knows nothing of them: the hands read `FrameContext.Tier`
(hands design §7.3). Their gate holds at `Nominal`, `High` and `Fair`, with the detector at 30 detections a second at
`Nominal` and `High` and 15 at `Fair`. At `Serious` and `Critical` the gate closes with the reason `heat`
(`hands.off`), the detector stops, and the panels that had come within reach stay where they are and work by gaze.

T³ is 3 × 3 × 3 on every rung. `State.flags` bits 4 to 5 carry the value 0 to 3; `High` is sent as 0 (it shares
the Nominal texture set). `Telemetry.tier` carries the value 0 to 4 (decided here).

Inputs and stepping rule. Three classes carry the ladder:

- `Atlas.Rendering.LadderController` is the engine-free stepping rule the app runs.
- `Atlas.Rendering.QualityApplier` owns one, polls the thermal state every 2 s, evaluates the rule once per
  second (at once when the forced tier changes) and puts a tier into force: it swaps the eye-texture set, sets
  `FramePacing.TargetFrameRate` and holds the rung's `CullRho` and `GluedCopies` as plain properties and its `FurnitureCap` as a read-only one.
- `Atlas.App.QualityLadder : IQualityState, IAfterFrameStep` wraps the applier, feeds it the perf probe's
  percentiles at step 8b and publishes the tier in the `Atlas.World` types.

`QualityTier`, `QualityRow`, `QualityTable` and `QualityInputs` exist once, in `Atlas.World`; `Atlas.Rendering`
uses them. `QualityTable.Row` throws for an undefined tier. `QualityInputs` names the pin `PinTier`; the applier's
own property is `QualityApplier.ForcedTier`.

```csharp
// File: Rendering/Quality/LadderController.cs
namespace Atlas.Rendering
{
    public struct QualityInputs
    {
        public double Time;            // s, FrameContext.Time
        public int ThermalState;       // 0..3, polled every 2 s from AtlasThermal.State
        public bool PerfValid;         // IPerfSource present and HasData
        public float GpuP95Ms;         // IPerfSource.GpuP95Ms10s
        public float FrameP95Ms;       // IPerfSource.FrameP95Ms10s
        public int ForcedTier;         // thermal.pinTier: -1 automatic, 0..4
    }

    public sealed class LadderController
    {
        public const double DwellSeconds = 20.0;      // at most one automatic step per 20 s (section 8.6)
        public const float GpuToFairMs = 9f;          // section 8.6
        public const float GpuToSeriousMs = 12f;      // section 8.6
        public const float GpuBackToNominalMs = 6.5f; // hysteresis (proposed)
        public const float GpuBackToFairMs = 8f;      // hysteresis (proposed)
        public const float FrameMissMs = 17.2f;       // one vsync interval + 0.5 ms (section 11.2)

        public QualityTier Tier { get; }
        public bool IsForced { get; }
        /// <summary>Evaluates the rule once. True when Tier changed.</summary>
        public bool Step(in QualityInputs inputs);
        public void Reset(QualityTier tier = QualityTier.Nominal);
    }
}

// File: Rendering/Quality/QualityApplier.cs
namespace Atlas.Rendering
{
    public sealed class QualityApplier
    {
        public const double ThermalPollSeconds = 2.0; // P1-A07
        public const double EvaluateSeconds = 1.0;

        /// <summary>eyes may be null where there is no stereo rig. Starts on Nominal.</summary>
        public QualityApplier(IEyeTextures eyes);
        public QualityTier Tier { get; }
        public QualityRow Row { get; }
        public LadderController Ladder { get; }
        public int ThermalState { get; }
        /// <summary>-1 reads the device. 0 .. 3 replaces the device's thermal state: for rehearsal in the Editor.</summary>
        public int ThermalOverride { get; set; }
        /// <summary>-1: the ladder is automatic. 0 .. 4: this tier is forced, in force within one second.</summary>
        public int ForcedTier { get; set; }
        public float RenderScale { get; }
        public int Msaa { get; }
        public float CullRho { get; set; }
        public int GluedCopies { get; set; }
        /// <summary>The highest furniture level of the tier: QualityFurniture.Cap(Tier). Read-only.</summary>
        public Atlas.World.FurnitureLevel FurnitureCap { get; }
        public int FrameRate { get; }
        public event System.Action<QualityTier> TierChanged;
        public void SetPerf(bool valid, float gpuP95Ms, float frameP95Ms);
        /// <summary>Once per frame, after the frame has been rendered, with the app clock in seconds.</summary>
        public void Tick(double timeSeconds);
        public void Reset();
        public void Apply(QualityTier tier);
    }
}

// File: App/Params/QualityLadder.cs
namespace Atlas.App
{
    public sealed class QualityLadder : Atlas.World.IQualityState, Atlas.World.IAfterFrameStep
    {
        public QualityLadder(Atlas.Rendering.QualityApplier applier);
        public Atlas.Rendering.QualityApplier Applier { get; }
        /// <summary>Frame-time percentiles; null until the perf probe is published (thermal state alone then).</summary>
        public Atlas.World.IPerfSource Perf { get; set; }
        /// <summary>The applier's FurnitureCap: the highest furniture level of the tier in force.</summary>
        public Atlas.World.FurnitureLevel FurnitureCap { get; }
    }
}
```

```text
if ForcedTier ≥ 0:   Tier = ForcedTier at once (no dwell; within 1 s, P1-A07)                 # Console, menu
else, and only when Time − time of the last automatic step ≥ 20 s, move ONE rung:
  down (worse):  Nominal  → Fair      if thermal ≥ 1  or  GPU p95 > 9 ms
                 Fair     → Serious   if thermal ≥ 2  or  GPU p95 > 12 ms
                 Serious  → Critical  if thermal = 3  or  frame p95 > 17.2 ms               # "still missing 60 fps"
  up (better):   Fair     → Nominal   if thermal = 0  and  PerfValid  and  GPU p95 < 6.5 ms
                 Serious  → Fair      if thermal ≤ 1  and  PerfValid  and  GPU p95 < 8.0 ms
                 Critical → (never automatically; only by a pin or Reset at a new session)
  a down condition is tested first; with PerfValid false only the thermal terms step down, and nothing steps up
  leaving a pin (PinTier back to −1) starts from the pinned rung, or from Nominal after High,
  and restarts the 20 s dwell
```

- Hysteresis: the thermal state changes slowly by itself; the GPU term steps up only well below the threshold it
  stepped down at (9 ms at scale 0.8 is about 6.9 ms at scale 0.7). The two "back" values are proposed and are
  confirmed at G4.
- A step allocates nothing: it swaps to a preallocated texture set and writes four numbers.
- On `Critical` the applier sets `FramePacing.TargetFrameRate = 30` (VSync count 2) and lane D's HUD shows
  "Session over" (`HudText.SessionOver`). `State` then goes out at 30 Hz, one per frame (§7.2 of the plan). The
  ladder itself logs no event.
- A tier takes effect like this (README F6: `Atlas.Rendering` does not see the registry): `QualityApplier` swaps
  the eye-texture set itself (`IEyeTextures.Select`) and raises `TierChanged`; the handler that
  `AppParams.DeclareRender` installed then writes what is in force back into `render.scale`, `render.msaa`,
  `curved.cullRho` and `glued.copies`. The handler of `curved.cullRho` sets `IWalkerKernel.CullRho`; the Glued
  module reads `glued.copies` from its handle. The registry therefore always shows what is in force, and
  `State.p[2]` follows. The furniture column takes the other route: nothing is written back, and
  `FurnitureLevelPolicy` (§4.2) listens to `QualityLadder.TierChanged` and reads `QualityLadder.FurnitureCap`. Between two tier changes a manual change of one of the four stands (§12.2 of the plan:
  "the Console can pin any rung").

### 9.6 Parameters declared by the composition root

The static class `Atlas.App.AppParams` (`App/Params/AppParams.cs`) declares these at boot and binds them to the
plain properties above: `DeclareSpace`, `DeclareRender`, `DeclareUnownedKillSwitches`, `DeclarePose`,
`DeclareScan`, `DeclareInput`, `DeclareDev`. Each name is also a `public const string` there
(`AppParams.RenderScale` = `"render.scale"` and so on). Numeric ids are on lane D's page (the wire id is the
order of declaration); the names, ranges and defaults are binding here.

| Name | Group | Min | Max | Default | Unit | `onRelease` | Drives |
|---|---|---|---|---|---|---|---|
| `space.id` | `app` | 0 | 6 | 0 | space (enum) | yes | `ISpaceHost.RequestSwitch`; written back on `SpaceEntered`, and snapped back when a switch is refused. Ignored until the end of boot |
| `thermal.pinTier` | `render` | −1 | 4 | −1 | tier | yes | `QualityApplier.ForcedTier` |
| `render.scale` | `render` | 0.6 | 0.8 | 0.8 | – (0.6, 0.7, 0.8) | yes | `IEyeTextures.Select` |
| `render.msaa` | `render` | 1 | 4 | 4 | samples (1, 2, 4) | yes | `IEyeTextures.Select` |
| `render.ipd` | `render` | 0.055 | 0.075 | 0.064 | m | no | `IStereoRig.Ipd` |
| `render.lineWidth` | `render` | 0.5 | 3 | 1.5 | px | no | `AtlasLook.SetLineWidth` |
| `render.lens` | `render` | 0 | 1 | on a phone, off in the Editor | off, on (bool) | no | `LensBlit.Distortion` |
| `render.lensK1` | `render` | 0 | 1 | 0.34 | – | no | `LensModel.K1` |
| `render.lensK2` | `render` | 0 | 1 | 0.55 | – | no | `LensModel.K2` |
| `render.lensSeparation` | `render` | 0.055 | 0.075 | 0.064 | m | no | `LensModel.LensSeparation` |
| `render.screenToLens` | `render` | 0.025 | 0.060 | 0.039 | m | no | `LensModel.ScreenToLens` |
| `render.lensOffsetY` | `render` | −0.02 | 0.02 | 0 | m | no | `LensModel.VerticalOffset` |
| `render.headOffsetX`, `…Y`, `…Z` | `render` | −0.10 | 0.10 | 0 | m | no | `IStereoRig.HeadOffset` |
| `render.furniture` | `render` | 0 | 2 | 0 | `auto`, `light`, `full` (enum) | no | `FurnitureLevelPolicy.Mode` (§4.2). Not written by the ladder; `full` overrides it |
| `curved.cullRho` | `curved` | 2 | 3.5 | 3 | kernel units | no | `IWalkerKernel.CullRho`; written by the ladder |
| `glued.copies` | `glued` | 3 | 7 | 5 | per side (3, 5, 7) | yes | Glued lattice; written by the ladder |
| `curved.mode`, `portal.depth`, `slicer.source`, `forge.backend` | `curved`, `portal`, `slicer`, `forge` | | | 0, 1, 0, 0 | | yes, no, yes, no | The kill switches of §12.4, declared here only when their owner has not declared them, so the Console and the menu list them from the first frame. `curved.mode` is read by S2 (Mode 1 or Mode 2) |
| `pose.gyroPredict`, `pose.gyroLeadMs` | `pose` | 0 | 60 (ms) | off, 40 | bool, ms | no | `IPosePipeline.GyroPredictMs` (the lead while the switch is on, else 0) |
| `pose.source` | `pose` | 0 | 2 | 0 | `tracked`, `replay`, `walker` | yes | `PoseSourceSwitch.SetSource` |
| `replay.speed`, `replay.loop`, `pose.record` | `pose` | 0 | 4 | 1, on, off | | no | `PoseSourceSwitch`, the pose log recorder |
| `scan.route`, `scan.teamPrescan` | `scan` | 1 | 4 | 3, off | | no | `ScanSettings.Route`, `ScanSettings.TeamPrescan` |
| `scan.wallBridge` | `scan` | 0 | 1 | on | bool | no | `Atlas.Scan.WallBridge.Apply(bool)`: `WallBridge.Enabled` is the parameter and the bridge's own `Available`. A kill switch: off gives the outline's walls as they were |
| `input.dwell` | `input` | 0.6 | 0.8 | 0.7 | s | no | `IAtlasInput.DwellSeconds` |
| `dev.room` | `dev` | 0 | fixtures | `none`, or a fixture where there is no AR provider | enum | yes | The fixture RoomModel the rehearsal flow uses |
| `dev.overlay` | `dev` | 0 | 1 | on in the Editor and in a debug build | bool | no | `DevOverlay.Visible` |

`render.msaa` gains the value 1 and `glued.copies` the value 7 against §12.4, for the `Critical` and `High` rungs
(decided here). `RigUploadStep` copies the pipeline's `PredictionRotation` into `IStereoRig.PredictionOffset` at
step 6. `ParamRegistry.PushAll()` at the end of boot pushes every value to its handler once.

The tracked hands' own parameters, the `hands.*` area (`hands.enabled`, `hands.rate`, `hands.reach`, `hands.sound`,
`hands.draw`, `hands.editor`, `hands.bench`, `hands.record`), are not `AppParams`'. `HandsSystem.DeclareParams` declares
them and the root calls it after the dev rows (§8.2, row 18). Lane D's page 1.4 has their kinds, ranges and defaults and
1.5 the one kill switch among them, `hands.enabled`, which is off at the start.

---

## 10 · Shader contract

### 10.1 Files and owners

Every shader sits in a `Resources` folder, so a player build includes it with nothing referencing it.

| File | Shader name | Owner | Does |
|---|---|---|---|
| `Rendering/Shaders/Resources/AtlasSurface.shader` | `Atlas/Surface` | A | The one surface shader. One pass (`AtlasForward`), `LightMode = UniversalForward`. Holds the vertex and fragment programs of §10.5 |
| `Rendering/Shaders/Resources/AtlasCore.hlsl` | – | A | The `UnityPerMaterial` block, the globals, palette lookup, vertex shade, lines, fog, fade, dither |
| `Rendering/Shaders/Resources/AtlasWarp.hlsl` | – | B | `AtlasWarp`: object position in, world position out; `AtlasWarpClip`: the sphere's 2.6 rad discard. Lane B's (lane B §6.1) |
| `Rendering/Shaders/Resources/AtlasPortalClip.hlsl` | – | A | `AtlasPortalClip`: clip against a portal plane |
| `Rendering/Shaders/Resources/AtlasGates.hlsl` | – | B | `AtlasGatesClip`: clip against up to four sight-line gates, read from `_Gate0` to `_Gate3`. Lane B's (lane B §6.5) |
| `Rendering/Shaders/Resources/AtlasBackground.shader` | `Hidden/Atlas/Background` | A | The gradient background, used as the skybox material |
| `Rendering/Shaders/Resources/AtlasNearText.shader` | `Atlas/NearText` | A, for the hands | The text of panels within reach of the hands: Unity's built-in font shader (vertex colour times `_Color`, alpha from the font texture, blended, both faces, no depth write) with its depth test on, `ZTest LEqual` (decision R7 of the hands brief; below and §10.9) |
| `Rendering/Shaders/Resources/AtlasHexFlip.shader` | `Hidden/Atlas/HexFlip` | A | The hexagon dome that turns one space over into the next (`App/Spaces/HexTransition.cs`): one mesh, opaque, no fog and no fade, drawn before the world in a thin depth slab next to the near plane |
| `Rendering/Portals/Resources/AtlasPortalMask.shader` | `Hidden/Atlas/PortalMask` | A | Doorway mask: stencil mark and depth reset |
| `Rendering/Stereo/Resources/AtlasLensBlit.shader` | `Atlas/LensBlit` | A | The lens blit of §9.4 |
| `Rendering/Look/AtlasShaderIDs.cs` | – | A, with B | The property ids of the look, the warp and the portals (§10.6) |

- Lane B's tile meshes (`Rendering/Tiles/TileMeshGenerator.cs`, `LapMarkerMesh.cs`, `Props/`) write the kernel's
  `AtlasVertex` (§10.2): COLOR.r the baked shade, COLOR.g the line weight (255 on a tile floor, 0 on kerbs and posts),
  `Uv1` the line coordinates (0.5 + 0.5·klein/h on a floor, so 0 and 1 fall on the tile edges), `Uv0` a cell of
  `TilePalette.Default` (`AtlasPalette.Floor`, `Kerb`, `Post`: cells 16, 20, 21). Their materials are rented from
  `MaterialPool` (`TileRenderer` rents 128 at `Configure`, `LapMarkerView` 2), with the minor lines on
  (`MaterialPool.SetLines(mat, 1, 0, 0)`); fog goes through `AtlasLook.SetFog`. Nobody but `AtlasLook` calls
  `Shader.SetGlobal*` for look globals, and the warp's `_HypL` and `_HeadPosWS` have one writer, `LookUploadStep`.
- An include is edited only by its owner. `AtlasShaderIDs.cs` is edited by lanes A and B only; lane B's ids
  are in it. An id is added or changed only after all four lanes are told (§6.4);
  `docs/scene-ownership.md` says the same.
- The shaders reach the player through their `Resources` folders and are loaded by file name at boot:
  `Resources.Load<Shader>("AtlasSurface")` and `("AtlasBackground")` (`AtlasLook.LoadSurfaceShader`,
  `LoadBackgroundShader`), `("AtlasPortalMask")` (`PortalRenderer.MaskShaderResource`), `("AtlasLensBlit")`
  (`LensBlit.ShaderResource`), `("AtlasHexFlip")` (`HexTransition`) and `("AtlasNearText")` (`NearText.ShaderResource`,
  `Rendering/NearText.cs`). There is no shader-set asset; `Shader.Find` is only the lens blit's second try
  (U §9). **No other lane loads `Atlas/Surface` or creates a material of it.** Materials come from
  `MaterialPool`.
- `Atlas/NearText` exists for one reason. The text
  of the on-device menu and of the four SpaceLab signs is legacy `TextMesh` on the built-in font's own material, whose
  shader (`GUI/Text Shader`) passes the depth test always: right on the head-locked layer, wrong on the world layer,
  where a panel within reach of the hands stands, because its text would paint over a finger held in front of it.
  While a panel is within reach its view gives its text renderers the one `Atlas/NearText` material of the font
  (`NearText.For(font)`: made once for each font with the font's own texture and colour, kept in step when the font
  rebuilds its atlas, `HideAndDontSave`, so neither pooled nor saved) and gives the font's own material back when the
  panel goes back. The shader is `GUI/Text Shader` with the depth test on, written for the Universal pipeline with no
  keyword; its one pass has no `LightMode` tag, so URP draws it as `SRPDefaultUnlit` in the transparent queue, after
  the opaque panels have written their depth. It uses none of the look's block, globals, warp or fog. Without it in the
  build the font's own material stays: the text is readable and does not go behind a finger (a warning is logged once).
- Three shaders outside `Atlas.Rendering` exist in the code, each loaded from `Resources` by its owner:
  `Input/Resources/AtlasReticle.shader` (the gaze reticle), `Safety/Resources/AtlasCue.shader` (the warning
  cue, which must not depend on the look) and `Telemetry/Map/Resources/AtlasFlatTwin.shader` (the flat-twin
  map). They are not part of this contract; README F7 still names one shader.
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

        /// <summary>Size of one vertex in bytes.</summary>
        public const int Stride = 44;
        /// <summary>The baked shade, COLOR.r.</summary>
        public byte Shade { get; }
        /// <summary>The line weight, COLOR.g.</summary>
        public byte LineWeight { get; }
        /// <summary>R = shade, G = line weight, B = 255, A = 255.</summary>
        public static uint PackColor(byte shade, byte lineWeight = 255);
    }
}
```

| Attribute | Type | Meaning |
|---|---|---|
| `POSITION` | float3 | Object space, metres. For a registered space the object space of a root at identity is the room frame. Under the warp (lane B) x and z are tile-local metres and y is height |
| `NORMAL` | float3 | Unit, object space. **The shader does not light by it** (D23; lane B §9.4, item 3). It is required because the CPU bakes the shade from it, and for later rim or outline terms |
| `COLOR.r` | UNorm8 | **Shade**: baked brightness of the vertex, ambient occlusion times the face shade. 255 = fully lit, open. The shader maps it to `lerp(_AtlasShade.x, 1, r)` |
| `COLOR.g` | UNorm8 | **Line weight**: 255 = lines drawn at the material's strength, 0 = no lines at this vertex |
| `COLOR.b`, `COLOR.a` | UNorm8 | Reserved. Write 255. The shader does not read them |
| `TEXCOORD0` | float2 | **Palette coordinate** (§10.7). For a flat colour, the centre of the palette cell. Along a gradient strip, u runs along the strip |
| `TEXCOORD1` | float2 | **Line coordinates**. A line is drawn wherever either component crosses an integer. The generator scales them so integers fall where lines belong: metres ÷ 0.5 for the 0.5 m floor grid, tile-edge coordinates for tile edges. Absent or (0, 0) everywhere: no lines, and the material's line strengths must be 0 |

**One or more object cells per asset** (mesh design §5.3, §5.4). A generated object may use more than
one cell of the object block of the palette (cells 64 to 127, §10.7), so that it can show the colours of its parts.
Each face has one cell, and every vertex of that face carries the centre of that cell in `TEXCOORD0`, so
the interpolated coordinate is constant over a face. A vertex shared by two faces
of different cells is split into one vertex per cell. The vertex format, the 44-byte stride and the shader are
unchanged: `AtlasCore.hlsl` reads the palette at the vertex's own `uv0` and nothing else, as before.

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
`AtlasHud` layer. The code has two measures. The grid generators (`AddQuadGrid`, `AddBox`, `AddFloorGrid`) cut
cell **sides** to at most `maxEdge`, so a cell diagonal reaches 0.354 m; `LongestSide()` measures that.
`AtlasMeshBuilder.Subdivide()` is the strict pass, after which `LongestEdge()` (diagonals included) is at most
0.25 m, at about twice the triangles; the room shell's floor uses the strict rule.

Helpers every generator may use. The two classes have the same core members; `AtlasMeshBuilder` is the one
`AtlasMesh` takes.

```csharp
// File: World/Meshing/MeshData.cs   (engine-free; for assemblies that may not reference Atlas.Rendering)
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>A growable vertex and index list in the AtlasVertex layout.</summary>
    public sealed class MeshData
    {
        public const float DefaultMaxEdge = 0.25f;

        public AtlasVertex[] Vertices;      // valid up to VertexCount
        public int VertexCount;
        public int[] Indices;               // valid up to IndexCount
        public int IndexCount;
        public float3 BoundsMin, BoundsMax; // kept up to date by the Add methods

        public MeshData(int vertexCapacity = 1024, int indexCapacity = 4096);
        public int TriangleCount { get; }
        public void Clear();
        public void Reserve(int moreVertices, int moreIndices);
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
                                float ambientOcclusion = 1f, byte lineWeight = 255, float maxEdge = DefaultMaxEdge);

        /// <summary>A box standing on its base centre, turned by yaw about +Y (Unity's sense), five or six faces.
        /// size is (w along local X, h up, d along local Z). Line coordinates are zero.</summary>
        public void AddBox(float3 baseCentre, float3 size, float yaw, float2 uv0,
                           bool withBottom = false, byte lineWeight = 0, float maxEdge = DefaultMaxEdge);

        /// <summary>Length of the longest triangle edge. Metres.</summary>
        public float LongestEdge();
    }
}
```

```csharp
// File: Rendering/Look/AtlasMeshBuilder.cs   (engine-free code in Atlas.Rendering)
namespace Atlas.Rendering
{
    /// <summary>The same list with every generator of the look. Members beyond MeshData's:</summary>
    public sealed class AtlasMeshBuilder
    {
        public const float MaxEdge = 0.25f;       // the default maxEdge
        public const float GridMetres = 0.5f;     // spacing of the floor grid lines

        public bool IsEmpty { get; }
        public float3 BoundsCentre { get; }
        public float3 BoundsSize { get; }
        public void Reserve(int vertices, int indices);
        public void Append(AtlasMeshBuilder other);
        /// <summary>One quad of two triangles, with no line coordinates: for things that are never warped.</summary>
        public void AddQuad(float3 origin, float3 u, float3 v, float2 uv0,
                            float ambientOcclusion = 1f, byte lineWeight = 0);
        /// <summary>A horizontal rectangle at height y facing up, gridded, with line coordinates in world metres
        /// divided by lineSpacing, so neighbouring floors line up.</summary>
        public void AddFloorGrid(float2 min, float2 max, float y, float2 uv0,
                                 float lineSpacing = GridMetres, float ambientOcclusion = 1f,
                                 byte lineWeight = 255, float maxEdge = MaxEdge);
        /// <summary>As MeshData.AddBox, with line coordinates in metres / GridMetres and a darkened base row.</summary>
        public void AddBox(float3 baseCentre, float3 size, float yaw, float2 uv0,
                           bool withBottom = false, byte lineWeight = 0, float maxEdge = MaxEdge,
                           float baseOcclusion = 1f);
        // Also AddVertex(position, normal, uv0, lineCoord, ...), AddPost, AddExtrudedOutline, AddOutlineCap.
        /// <summary>The strict pass: splits triangles until no edge is longer than maxEdge. Crack-free.
        /// Returns the number of triangles added.</summary>
        public int Subdivide(float maxEdge = MaxEdge, int firstIndex = 0);
        /// <summary>Length of the longest triangle edge, diagonals included. Metres.</summary>
        public float LongestEdge();
        /// <summary>Length of the longest grid side: over all triangles, the longer of the two shorter edges.</summary>
        public float LongestSide();
        public bool WithinBudget(int triangles);
    }
}
```

`AtlasMeshBudget` (`Rendering/Look/AtlasMeshBudget.cs`) holds the triangle budgets of §15 as constants
(`FrameTriangles`, `HomeShell`, `GluedCell`, `PortalRoom`, `SelfAvatar`, `BuiltInFurniture`, `FurnitureTotal`)
and the counting helpers `Segments`, `QuadGridTriangles` and `BoxTriangles`.

```csharp
// File: Rendering/Look/AtlasMesh.cs
namespace Atlas.Rendering
{
    public static class AtlasMesh
    {
        /// <summary>Position, Normal, Color (UNorm8 x 4), TexCoord0, TexCoord1; all in stream 0.</summary>
        public static readonly UnityEngine.Rendering.VertexAttributeDescriptor[] Layout;
        /// <summary>The most vertices a mesh with 16-bit indices can hold.</summary>
        public const int MaxVertices16 = 65535;
        /// <summary>A new Mesh with the data, bounds from the data, UInt16 or UInt32 indices as needed.</summary>
        public static UnityEngine.Mesh Create(AtlasMeshBuilder data, string name);
        /// <summary>Replaces the contents of an existing mesh. Allocates nothing when the counts fit.</summary>
        public static void Apply(AtlasMeshBuilder data, UnityEngine.Mesh mesh);
        /// <summary>For meshes rewritten every frame: fixed capacity, UInt32 indices, MarkDynamic.</summary>
        public static UnityEngine.Mesh CreateDynamic(string name, int vertexCapacity, int indexCapacity);
        /// <summary>Uploads the first counts of the arrays into a mesh from CreateDynamic and sets the bounds. No allocation.</summary>
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
    float4x4 _Anchor;         //   0  B  tile, room or object anchor of the warp (lane B §6). Identity
    float4   _BaseTint;       //  64  A  rgb multiplies the palette colour, linear; a = opacity 0..1, dithered
    float4   _LineColor;      //  80  A  rgb line colour, linear; a unused
    float4   _LineParams;     //  96  A  x minor width, y major width (multiples of _AtlasLineWidth);
                              //         z minor strength 0..1, w major strength 0..1
    float4   _LineMajor;      // 112  A  xy = period of major lines in line-coordinate units (0 = none); zw unused
    float4   _PortalPlane;    // 128  A  world-space plane (n.xyz, d); fragments with dot(n, p) + d < 0 are clipped
    float4   _Gate0;          // 144  B  sight-line gates (lane B §6.5): (post.x, post.z, sign, unused)
    float4   _Gate1;          // 160  B
    float4   _Gate2;          // 176  B
    float4   _Gate3;          // 192  B
    float4   _SurfaceParams;  // 208  A  x emissive gain; y fog weight 0..1; z near-fade distance, metres (0 = off);
                              //         w fade weight: 1 follows _AtlasFade, 0 ignores it
    float    _WarpMode;       // 224  B  0 none, 1 hyperbolic, 2 spherical (lane B §6)
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
| `_StencilRef` | 0 | yes. Render state only (`Stencil { Ref [_StencilRef] … }`), not in the block |
| `_StencilComp` | 8 (Always); 3 = Equal | yes. Render state only. The pass never writes the stencil (pass, fail and depth-fail are Keep) |

`Atlas/Surface` carries the stencil test itself, so a room behind a doorway can be drawn either by a Render
Objects feature with a stencil override (§12.2), which replaces this block, or by `MaterialPool.SetStencil`.

There is no per-material texture: the palette is a global. Per-tile, per-room and per-object data live in this
block, one pooled material instance each. Camera-wide data are globals. **No `MaterialPropertyBlock`** (D14).

### 10.4 Globals

Declared in `AtlasCore.hlsl` outside the block. Set only by the owner named; everyone may read them in a shader.

| Name | HLSL type | Meaning | Default | Set by | When |
|---|---|---|---|---|---|
| `_AtlasPalette` | `Texture2D` + `sampler_AtlasPalette` | The palette (§10.7) | – | `AtlasPaletteTexture.Bind` | boot |
| `_HeadPosWS` | `float4` | xyz = the head, world, metres, one value for both eyes: `FrameContext.HeadPosition`, the geometry head that stepped the kernel this frame (the warp must be centred on the sample the anchors were built for). w unused | (0, 1.6, 0, 0) | `AtlasLook.Upload` (`LookUploadStep`) | step 6 |
| `_HypL` | `float` | `IWalkerKernel.MetresPerUnit`: L, or R on the sphere. Metres per kernel unit, above 0 | 1 | `AtlasLook.Upload` | step 6 |
| `_AtlasFogColor` | `float4` | rgb linear. Also the colour of the background at and below the horizon | palette `FogBase` | `AtlasLook` | on change; cross-fade at step 6 |
| `_AtlasFogParams` | `float4` | x = fog start, metres from the head on the floor; y = 1 / (end − start); z, w unused | (8, 1/6, 0, 0) | `AtlasLook.SetFog` | on change |
| `_AtlasSkyTop` | `float4` | rgb linear: the background at the zenith | palette `SkyTop` | `AtlasLook` | on change |
| `_AtlasShade` | `float4` | x = darkest shade a vertex can have (0.25, `AtlasLookState.DarkestShade`); y = global emissive gain (1); z, w unused | (0.25, 1, 0, 0) | `AtlasLook` (`SetEmissiveGain`) | boot |
| `_AtlasLineWidth` | `float` | Line width unit, pixels of the eye texture | 1.5 | `AtlasLook.SetLineWidth` | on change |
| `_AtlasFade` | `float` | 1 = world visible, 0 = world black. Scales surfaces whose fade weight is 1, and the background | 1 | `AtlasLook.SetFade` (SpaceLab only) | step 6 |
| `_AtlasTime` | `float` | `FrameContext.Time` modulo 3,600, seconds. For pulsing accents | 0 | `AtlasLook.Upload` | step 6 |

`AtlasLook.Upload` writes every global of this table again at step 6, so the look survives anything that resets
shader state.

URP's own globals (`_WorldSpaceCameraPos`, the view and projection matrices) are per eye and may be read. The
fog, the warp and the head-centred effects use `_HeadPosWS`, never the eye position, so both eyes agree.

### 10.5 Entry points

```hlsl
// ---- AtlasCore.hlsl (lane A) -----------------------------------------------------------------------------

// Linear rgb of the palette at uv0. Samples _AtlasPalette at mip 0.
half3 AtlasPaletteColor(float2 uv0);

// True when uv0 lies in the emissive block of the palette (uv0.y >= 0.75).
bool AtlasIsEmissive(float2 uv0);

// Coverage of the lines at the integers of coord, per axis, for a line widthPixels wide. coordPerPixel is fwidth(coord).
float2 AtlasLineAxes(float2 coord, float2 coordPerPixel, float widthPixels);

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
void AtlasWarpClip(float3 positionWS);        // fragment stage: sphere only, discard beyond 2.6 rad from the head

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
    AtlasGatesClip(input.positionWS);                         // first, for every material (lane B §6.5)
    AtlasPortalClip(input.positionWS);
    AtlasWarpClip(input.positionWS);                          // the far side of the sphere (lane B §6.1)
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
`public static readonly int X = Shader.PropertyToID("…")`. This is the complete list of the class. Two places
keep ids of their own: the lens blit (`LensBlit` and `LensBlitPass` hold private ids for `_LensScreen`, `_LensK`,
`_LensFrustumL`, `_LensFrustumR`, `_EyeL`, `_EyeR`), and `PortalRenderer`, which sets the mask shader's
`_StencilPass`, `_MaskZTest`, `_MaskZWrite` and `_MaskParams` by name when a mask material is made.

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
| `StencilRef` | `_StencilRef` | render state of `Atlas/Surface` and of `Hidden/Atlas/PortalMask` | `Material.SetFloat` | A |
| `StencilComp` | `_StencilComp` | render state of the same two shaders | `Material.SetFloat` | A |
| `EyeLeft`, `EyeRight`, `LensK`, `LensGeometry`, `LensFov` | `_AtlasEyeLeft`, `_AtlasEyeRight`, `_AtlasLensK`, `_AtlasLensGeometry`, `_AtlasLensFov` | Declared, but no shader has these properties and nothing sets them | – | A |

What the lens blit shader (`Atlas/LensBlit`) really reads, set by `LensBlit` and `LensBlitPass`:

| Property | Content |
|---|---|
| `_EyeL`, `_EyeR` | The two eye textures of the active set |
| `_LensScreen` | (screen width, screen height, half the lens separation, vertical offset), each ÷ `ScreenToLens` |
| `_LensK` | (K1, K2, distortion on = 1, write sRGB by hand = 1) |
| `_LensFrustumL`, `_LensFrustumR` | (tanLeft, tanDown, 1 / (tanLeft + tanRight), 1 / (tanDown + tanUp)) of each eye |

Vectors are passed as `Vector4`; a `float4` converts implicitly. A colour is passed linear.

### 10.7 The palette

One texture, 128 × 128, RGBA8 sRGB, no mipmaps, no compression, point filtered, clamp. `AtlasPalette`
(engine-free) holds the table below and writes the pixels; `AtlasPaletteTexture.Bind()` builds the texture at
boot (README F3: no hand-painted file) and binds it as `_AtlasPalette`. The strips are generated per texel, so
they step once per texel.

```csharp
// File: Rendering/Look/AtlasPalette.cs
namespace Atlas.Rendering
{
    public static class AtlasPalette
    {
        public const int Size = 128, CellsPerSide = 16, CellPixels = 8;
        public const int CellCount = CellsPerSide * CellsPerSide;
        public const int EmissiveRow = 12;
        public const float EmissiveV = 0.75f;
        public const int CategoryBase = 32, CategoryCount = 32;
        public const int ObjectBase = 64, ObjectCount = 64;
        public const int RegionBase = 128, RegionCount = 4, RegionSteps = 4;
        public const int Cell4DBase = 208, Cell4DCount = 32;
        /// <summary>Gradient strips: rows, for StripUv.</summary>
        public const int StripWarm = 9, StripCool = 10, StripDepth = 11, StripW = 15;

        /// <summary>Palette coordinate of the centre of cell index (0..255).</summary>
        public static Unity.Mathematics.float2 Uv(int index);
        /// <summary>Coordinate on gradient strip row (0..15) at t in 0..1 along the strip.</summary>
        public static Unity.Mathematics.float2 StripUv(int row, float t);
        /// <summary>The colour of a cell in linear rgb, for values that go into _BaseTint, fog or line colour.</summary>
        public static Unity.Mathematics.float3 Linear(int index);
        /// <summary>The colour of a cell in sRGB, 0..1, at the cell's centre.</summary>
        public static Unity.Mathematics.float3 Srgb(int index);
        /// <summary>Object colour cell (lane D's AssetReady.palette): ObjectBase + 16 * valueStep + column.</summary>
        public static int Object(int column, int valueStep);
        /// <summary>True when the cell lies in the emissive rows 12 to 15.</summary>
        public static bool IsEmissive(int index);
        /// <summary>Writes the whole palette as RGBA8, sRGB encoded, row 0 (the bottom row) first: Size * Size * 4 bytes.</summary>
        public static void WritePixels(byte[] rgba);
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
| 4 – 7 | 64 – 127 | Object colours | `ObjectBase` = 64. What the Forge and the built-in set may pick. An asset may use one or more of these cells (§10.2); `AssetReady.palette` names the cell that covers the most surface. Row 4 + k has value V = 0.28, 0.42, 0.58, 0.74 for k = 0 … 3. Columns 0 – 11: HSV(column · 30°, 0.50, V). Column 12: HSV(30°, 0.12, V). 13: HSV(210°, 0.12, V). 14: HSV(28°, 0.35, V). 15: HSV(0°, 0, V) |
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
| 207 | `Spare` | #000000 | Spare |

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
        /// <summary>Resources names of the two shaders of the look.</summary>
        public const string SurfaceShaderResource = "AtlasSurface", BackgroundShaderResource = "AtlasBackground";

        /// <summary>The values last written to the shader.</summary>
        public static AtlasLookState State { get; }
        /// <summary>Boot: builds and binds the palette, sets every global to its default and installs the background.</summary>
        public static void Initialise();
        /// <summary>Atlas/Surface, loaded from Resources. Null if missing.</summary>
        public static UnityEngine.Shader LoadSurfaceShader();
        public static UnityEngine.Shader LoadBackgroundShader();
        /// <summary>Makes the gradient the scene's skybox. Returns the material, or null when the shader is missing.</summary>
        public static UnityEngine.Material InstallBackground();

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
        /// <summary>Global gain of the emissive accents; 1 by default.</summary>
        public static void SetEmissiveGain(float gain);
        /// <summary>Defaults for a new space: default fog, FogBase and SkyTop colours, immediate. SpaceLab only.</summary>
        public static void ResetForSpace();
        /// <summary>Step 6: the head position, the metres per kernel unit, the time and the fog cross-fade.
        /// Writes every global. Called by the composition root's first upload hook (LookUploadStep) with
        /// frame.RawHeadPosition, IWalkerKernel.MetresPerUnit, frame.Time and frame.DeltaTime.</summary>
        public static void Upload(float3 headPositionWS, double metresPerUnit, double timeSeconds, float dt);
    }
}
```

The values and the fog cross-fade are the engine-free struct `AtlasLookState`
(`Rendering/Look/AtlasLookState.cs`), which tests step with no engine.

A space calls `SetFog`, `SetFogColor` and `SetSkyTop` in `Enter` or later; it never calls `Shader.SetGlobal…`
itself. Lane B's spaces set the fog from ρ and L the same way.

### 10.9 What the tracked hands and the near panels draw with

The tracked hands (`Atlas.Hands`, hands design §5) add nothing to the look: no shader of their own, no keyword and no
property block. `HandsView` (`Hands/View/HandsView.cs`) builds an object "Atlas Hands" under `boot.Systems`, never
under room content or a space, whose subtrees `PortalRenderer` and SpaceLab re-layer and switch off as rooms change, and
four children. Each is a `MeshFilter` and a `MeshRenderer` on **layer 0**, the world (§12.1), with shadows and probes
off and the renderer disabled until there is something to draw. Each has its **own pooled `Atlas/Surface` material**
(`MaterialPool.Rent()`, four in all, returned by `Dispose`):

| Object | Mesh | Material |
|---|---|---|
| `Hands Left`, `Hands Right` | A dynamic mesh each (`AtlasMesh.CreateDynamic`): 790 triangles and 409 vertices built in code from the template hand (`HandsMeshTemplate`; capacity `HandsConstants.MaxVertices` 640 and `MaxIndices` 2,700). Skinned on the CPU every frame at step 6 from the 21 world joints the hands' filter holds (`HandsSkin.Skin`: weights, a baked shade from `AtlasShading.LightDirection`, bounds from the joints) and uploaded with `AtlasMesh.Upload`; nothing is allocated | `SetSurface(1, 0, 0.12, 0)` and `SetCull(Off)`; tint white, opacity `HandPose.Opacity` every frame; the vertices' UV0 is the palette cell `AtlasPalette.AvatarBody` |
| `Hands Mark Left`, `Hands Mark Right` | A flat ring of unit diameter in the local XY plane (24 segments), set 2 mm proud of a target's face under a hovering or pressing fingertip, or of a surface of the room the fingertip is near, and left to fade for 0.4 s (hands design §6.2, §6.5) | `SetSurface(1, 0, 0, 0)` and `SetCull(Off)`; tint white, opacity 0.25 to 1; the palette cell `AtlasPalette.Reticle` (an emissive cell) |

- **No warp.** None of the four materials sets `_WarpMode`, `_Anchor` or a gate, so `AtlasWarp` returns the plain object-to-world
  position and nothing is clipped (lane B §10, item 5): in S2, S3 and S4 too the hands are in room coordinates, as the
  walker's own hands must be. The meshes are written in world coordinates, so their objects never move.
- **No fog, no fade.** Fog weight 0, so that a hand is never fogged (Glued's fog can start half a metre out), and fade
  weight 0, so that a space switch's `_AtlasFade` does not touch it.
- **Dithered opacity.** `HandPose.Opacity` goes to `_BaseTint.a` each frame (`MaterialPool.SetTint`) and the shader's
  4 × 4 ordered dither does the rest (§10.5): no blending and no sorting. The material is opaque geometry (`ZWrite On`,
  queue Geometry), so a hand is tested against the world, against a near panel and against the other hand. A hand with no
  opacity, or hidden, is not skinned and its renderer is off. The opacity is the smallest of five ramps (a hand
  entering or leaving, the edge of the camera's view, a Bump Guard warning, the gate, `IHands.HideThisFrame`; hands
  design §5.4).
- **The near fade.** `SetSurface`'s third value, `HandsConstants.NearFadeMetres` = 0.12: `AtlasShade` multiplies the
  opacity by `saturate(distance(positionWS, _WorldSpaceCameraPos) / 0.12)` for each eye, so the part of a hand within
  12 cm of an eye dissolves by the same dither and is not sliced by the near plane.
- **Both faces.** `SetCull(Off)`, so a hand that is given the wrong side never shows a hole.
- **Where it is in the frame.** `HandsUploadStep` is the third upload hook, after `LookUploadStep` and `RigUploadStep`
  (§7.2). It reads only what the hands' sense step froze at step 1, writes the meshes, the opacities and the marks, and
  has no look global of its own to write. The frame's other views place themselves in their own step 6 and then ask
  `ReachSlots.TryGetNear` (lane C §7.7), which reads the layout of the same frame.
- **Cost**, counted for both eyes as everywhere on this page and measured nowhere yet (hands design §5.7): four draw
  calls for the two hands and up to four for the two marks; 3,160 triangles for the two hands (790 each, in two eyes)
  and up to 192 for the rings (48 each); four of the 256 pooled materials.

**Panels within reach.** In a hands session the on-device menu, the four SpaceLab signs, the dial and mode chips of
Curved Walk and the dial chip of Small Planet stand at their slots about the near anchor (lane C §7.7), with their
size multiplied by `IReachLayout.Scale`. They keep the materials they have, `Atlas/Surface` from the pool. The menu
and the signs, which draw on `AtlasHud` when they are far, move to **layer 0** for as long as they are near, so that
the eye cameras test them against depth, and go back; the chips of Curved Walk and the plate of Small Planet are on
the world layer already. The text of the menu and the signs, which is legacy `TextMesh`, takes the font's
`Atlas/NearText` material (§10.1), so that a finger in front of a row covers the row and its text; Curved Walk's chip
text is TextMeshPro world text and does not use it. A child object does not take its parent's layer: the object and
each text under it are set one by one, and set back when the panel goes far.

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
        /// <summary>A pool on the shader loaded from Resources. Throws when the shader is not in the build.</summary>
        public static MaterialPool CreateDefault(int capacity = 256);

        /// <summary>A material with every property at the default of section 10.3. Never null.</summary>
        public Material Rent();
        /// <summary>Gives a material back. It is reset and reused; never destroy a rented material.</summary>
        public void Return(Material material);
        /// <summary>True when the pool made the material and it is rented now.</summary>
        public bool Owns(Material material);
        /// <summary>Destroys every instance. For shutdown and tests; the pool is unusable afterwards.</summary>
        public void Dispose();
        /// <summary>Sets every per-material property and render-state value to its default.</summary>
        public static void ResetToDefaults(Material material);

        public int Capacity { get; }
        public int Rented { get; }
        public Shader Shader { get; }

        // Typed setters for a rented instance. None allocates.
        public static void SetAnchor(Material material, in float4x4 anchor);
        public static void SetWarpMode(Material material, int mode);
        public static void SetTint(Material material, float3 linearRgb, float opacity = 1f);
        public static void SetTintIndex(Material material, int paletteIndex, float opacity = 1f);
        public static void SetLineColor(Material material, float3 linearRgb);
        public static void SetLines(Material material, float minorStrength, float majorStrength, float2 majorPeriod,
                                    float minorWidth = 1f, float majorWidth = 2f);
        public static void SetSurface(Material material, float emissiveGain = 1f, float fogWeight = 1f,
                                      float nearFadeMetres = 0f, float fadeWeight = 1f);
        public static void SetPortalPlane(Material material, float4 plane);
        public static void SetPortalPlane(Material material, float3 normal, float3 pointOnPlane);
        public static void ClearPortalPlane(Material material);
        public static void SetGate(Material material, int gate, float2 post, float sign);
        public static void ClearGates(Material material);
        public static void SetStencil(Material material, int reference,
                                      UnityEngine.Rendering.CompareFunction comparison = UnityEngine.Rendering.CompareFunction.Equal);
        public static void ClearStencil(Material material);
        public static void SetCull(Material material, UnityEngine.Rendering.CullMode cull);
    }
}
```

| Rule | Detail |
|---|---|
| Assign | `renderer.sharedMaterial = pool.Rent()`. Never `renderer.material` (it clones). Never a `MaterialPropertyBlock` |
| Set values | The typed setters above (`MaterialPool.SetTint(material, …)`), or `material.SetVector(AtlasShaderIDs.BaseTint, …)` and the like, on the rented instance |
| One per what | One instance per tile, per room shell, per object, per Glued cell copy that needs its own tint. Renderers that share every value share one instance |
| Return | In `ISpaceModule.Exit`, or when the object is destroyed. A material returned twice, or one the pool did not make, is an `ArgumentException` |
| Allocation | `Rent` and `Return` allocate nothing while `Rented` ≤ `Capacity`. Beyond it the pool doubles and logs the warning `materials.grow` once; that is a budget defect, not a crash |
| Budget | At most 256 rented at once (decided here; S2 at ρ = 3 needs 71 tiles + 21 room materials) |
| Never saved | Pool instances are never assets (§9.3) |

---

## 12 · Layers, stencil references and scenes

### 12.1 Layers

`AtlasProjectSetup` writes the names into `ProjectSettings/TagManager.asset`. Code uses the numbers. There is
no `AtlasLayers` class: the layer numbers of the portal table are constants of `PortalStencilTable`; the
Spectator layer (23) and the XR Simulation layer (30) are written as numbers where they are used (`StereoRig`,
`AtlasApp`, `AtlasProjectSetup`).

```csharp
// File: Rendering/Portals/PortalStencilTable.cs   (engine-free)
namespace Atlas.Rendering
{
    public static class PortalStencilTable
    {
        public const int MaxSlots = 15;
        public const int WorldLayer = 0;       // "Default"
        public const int MaskLayer = 6;        // "AtlasPortalMask"
        public const int HudLayer = 7;         // "AtlasHud"
        public const int FirstRoomLayer = 8;   // "AtlasRoom01" .. "AtlasRoom15" = 8 .. 22
        public const string MaskLayerName = "AtlasPortalMask";
        public const string HudLayerName = "AtlasHud";
        public const string MaskFeature = "Atlas Portal Masks";
        public const string HudFeature = "Atlas HUD";
        public const string HudTextFeature = "Atlas HUD Text";
        public const int MaskEvent = 300, HudEvent = 500, HudTextEvent = 501;
        public const int MarkQueue = 2000, ResetQueue = 2001;

        public static bool IsReference(int reference);        // 1 .. 15
        /// <summary>Layer of the room seen through the doorway that holds this reference: 8 .. 22.</summary>
        public static int Layer(int reference);
        public static string LayerName(int reference);        // "AtlasRoom01" .. "AtlasRoom15"
        public static string FeatureName(int reference);      // "Atlas Room 01" .. "Atlas Room 15"
        public static int PassEvent(int reference);           // 301 .. 315
        public static int ReferenceOfLayer(int layer);        // 0 for a layer that is no room layer
        /// <summary>The layers a renderer feature draws: out of the renderer's own opaque and transparent masks.</summary>
        public static int FeatureLayerMask { get; }
    }
}
```

| Layer | Name | Drawn by | Who puts objects on it |
|---|---|---|---|
| 0 | `Default` | The renderer's opaque pass. **The world**: everything a space or the room content draws in the room the walker is in | Every lane; it is the default of a new object |
| 6 | `AtlasPortalMask` | Feature "Atlas Portal Masks" | `PortalRenderer` only |
| 7 | `AtlasHud` | Features "Atlas HUD" and "Atlas HUD Text": after everything, depth test Always, depth write off | Lane C (cue, reticle), lane D (HUD, link dot, menu) |
| 8 – 22 | `AtlasRoom01` … `AtlasRoom15` | Feature "Atlas Room NN" with stencil Equal NN | `PortalRenderer` only: it moves a room's root between `Default` and a room layer |
| 23 | `AtlasSpectator` | Nothing on the phone. The Spectator's cameras and a map camera (lane D) | Lane D |
| 30 | `XR Simulation` | Nothing in the eye cameras | AR Foundation |
| 1 – 5, 24 – 29, 31 | Unity's own or free | Not drawn in headset mode | Nobody without asking lane A |

- The renderer's `opaqueLayerMask` and `transparentLayerMask` are the template's masks with the feature layers
  (6, 7, 8 to 22) taken out. A layer owned by a feature must be out of them or it is drawn twice (U §8).
- Content under `IStereoRig.Hud` on `AtlasHud` is drawn in both eyes at its real stereo depth, on top of the
  world. Keep it 1.0 to 2.0 m from the eyes. It uses `Atlas/Surface` with fog weight 0 and fade weight 0, or
  TextMeshPro. Budget: at most 20 draw calls per frame for everything on the layer (decided here).
- Two things on layer 0 are not room content and are never under it: the tracked hands and their marks (§10.9), and,
  in a hands session, the menu and the SpaceLab signs, which leave `AtlasHud` while they are within reach of the hands
  and are drawn depth-tested there; everything on `AtlasHud` is as above whenever they are far. Neither is under a
  room's root, which is all that `PortalRenderer` moves between layers.

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
| 5 | "Atlas HUD" (opaque queue, `Atlas/Surface`), then "Atlas HUD Text" (transparent queue, text) | `AfterRenderingTransparents` (500, 501) | `AtlasHud` | none | test Always, write off |

The mask feature and the room features are saved inactive. `PortalRenderer` switches them with `SetActive`: the
mask feature while a doorway is in view, a room feature only while its reference is in use, so a space without
portals pays for none of them (U §8). The two HUD features are always active. `PortalRenderer.FeaturesFound` is
false when the renderer asset does not carry them (the project set-up has not run).

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
        public const int MaxSlots = PortalStencilTable.MaxSlots;      // 15
        public const string MaskShaderResource = "AtlasPortalMask";

        /// <param name="maskParent">Parent of the mask objects, or null for a root object of their own.</param>
        public PortalRenderer(Transform maskParent = null);
        /// <summary>The eye cameras, for their frusta. Null in the Spectator: a doorway then counts as seen
        /// whenever the head is in front of it.</summary>
        public IStereoRig Rig { get; set; }
        /// <summary>The engine-free frame logic this renderer applies: the same object a headless test steps.</summary>
        public PortalFrameCore Core { get; }
        public int RoomCount { get; }
        public int DoorCount { get; }
        public PortalRoom GetRoom(int room);
        public PortalDoor GetDoor(int door);
        /// <summary>True when the renderer features of the project set-up were found.</summary>
        public bool FeaturesFound { get; }

        /// <summary>Registers a room: every renderer under root, with materials from the pool. Returns its id (0, 1, ...).</summary>
        public int AddRoom(Transform root, Bounds boundsWorld);
        /// <summary>Adds a second root to a room: geometry of that room that lives under another parent.</summary>
        public void AddRoomPart(int room, Transform root);
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
        /// <summary>Clear, and the mask materials are destroyed. The renderer can be used again afterwards.</summary>
        public void Dispose();

        /// <summary>The room the walker is in. Changed by the renderer when the head crosses a doorway.
        /// Placed from outside, the next frame tests no crossing: the head did not walk there.</summary>
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

The composition root creates no `PortalRenderer` and publishes none. `PortalSpace` takes one from
`AtlasServices` if somebody published it, and otherwise creates its own (`new PortalRenderer(transform)`), gives
it the `IStereoRig`, drives it from its own `IUploadStep.Upload` and disposes it in `Exit`.

The rules live in engine-free classes beside it, which the headless tests step: `PortalFrameCore` (the frame
logic), `PortalSwapRule` (`HoldFrames = 3`), `PortalMath` (`BoxDistance = 0.10f`, `DoorPlane`, `Crossed`,
`BoundsInFrustum`, `InOverlap`, `DoorViewArea`) and `PortalSlotAllocator`.

Behaviour fixed by `P1-A10`: the current room changes when the head (not an eye) crosses a doorway plane; a room
is switched off only when its bounds are outside both eye frusta (`PortalMath.BoundsInFrustum`) and the head is
outside the overlap volume, held for 3 consecutive frames; while one eye is within 0.10 m of a doorway plane the
mask covers the whole view of that eye (the portal box). With the renderer features missing the walker's room is
shown alone, and walking through a doorway still works. At most 1.0 ms of GPU time per visible portal.

### 12.3 Scenes

| Scene | Path | Owner | iOS build list | Windows (Spectator) | Holds |
|---|---|---|---|---|---|
| `Atlas_Boot` | `Scenes/Atlas_Boot.unity` | A | index 0 | – | AR Session, XR Origin. Nobody edits it (README F3) |
| `Space_Home` | `Scenes/Space_Home.unity` | C | index 1 | after `PC_Spectator` | One root `Space_Home` with `HomeSpace` |
| `Space_Glued`, `Space_Portal`, `Space_Slicer` | `Scenes/Space_<Name>.unity` | A | indices 2, 6, 7 | after `PC_Spectator` | One root with the module |
| `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` | `Scenes/Space_<Name>.unity` | B | indices 3, 4, 5 (the order of `EditorBuildSettings.asset`: boot, Home, Glued, CurvedWalk, AlignedCells, SmallPlanet, Portal, Slicer) | after `PC_Spectator` | One root with the module, generated by `tools/gen_scenes.py` like the others |
| `PC_Spectator` | `Scenes/PC_Spectator.unity` | D | never | index 0 | One root `AtlasSpectatorBoot` with no script. Lane D's page |
| `Sandbox_<name>` | `Scenes/Sandbox/` (none exists yet) | each engineer | never | never | Anything; boots only with a root object named `AtlasBoot` (§8) |
| `Sys_Scan`, `Sys_BumpGuard`, `Sys_Onboarding`, `Sys_Link`, `Sys_Hud` | – | C, D | not created | not created | Replaced by installers (README F3). The names stay reserved; `docs/scene-ownership.md` says the same |

`tools/gen_scenes.py` (lane A) writes the four `Space_*` scenes and `PC_Spectator` with their `.meta` files from
its manifest and lists the `Space_*` scenes in `EditorBuildSettings` after `Atlas_Boot`
(`gen_scenes.py --check` exits 1 if anything would change; run `tools/gen_meta.py` first). Without a scene a
space comes from its factory (§5.2); behaviour is the same. `Atlas_Boot` stays the active scene at all times, so
`RenderSettings` (the background) are the boot scene's. The Windows list is built by `AtlasBuild.Spectator`:
`PC_Spectator`, then every enabled `Space_*` scene (§16.1).
(differs from plan section 9.3: no `Sys_*` scene is created; systems are installed in code.)

---

## 13 · `Mesh4D` and the slicer

Lane A's code in `Atlas.Kernel` (README F1), namespace `Atlas.Kernel.FourD`, folder `Kernel/Mesh4D/`. Engine-free:
`System`, `Unity.Mathematics`, and for the job `Unity.Burst`, `Unity.Collections`, `Unity.Jobs`. One code path
for polytopes and for the worldtube (D17, §5.5).

### 13.1 Conventions

- A 4D point is `float4 (x, y, z, w)`. x, y, z are the three room directions in model units; w is the fourth
  direction (in ℝ³ × S¹: time or phase, scaled to length).
- Model units: polytopes have circumradius 1. The module scales the result to metres with its transform.
- A `Mesh4D` holds the **boundary** of a 4D body as tetrahedra, so a cut gives the surface of the 3D
  cross-section. Every tetrahedron is oriented so that `TetNormal(tet)` points out of the body; the generators
  leave them so, and that is what lets a slice be wound outward.
- The cut is the hyperplane `dot(normal, p) = offset` in the **slice frame**, where
  `p = mul(modelToSlice, vertex) + translation`. The 3D position of a cut point is `p.xyz`.
  With `normal = (0, 0, 0, 1)` that is an isometric picture of the cross-section: rotate the object, keep the cut
  (the three verbs of §5.1). A tilted `normal` with w dropped is the scanner picture of §5.4.
- Rotations are built in `double` and cast to `float4x4` last.

### 13.2 Types and generators

```csharp
// File: Kernel/Mesh4D/Mesh4D.cs
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
        public readonly float[] VertexData;    // one scalar per vertex, interpolated onto a slice; may be null

        public int VertexCount { get; }
        public int TetCount { get; }
        public int EdgeCount { get; }
        /// <summary>Upper bounds on one slice: 2 triangles and 6 vertices per tetrahedron.</summary>
        public int MaxSliceTriangles { get; }
        public int MaxSliceVertices { get; }

        /// <summary>ArgumentException when an index is out of range, a length does not fit, or a cell id is not below cellCount.</summary>
        public Mesh4D(float4[] vertices, int[] tetrahedra, int[] tetCell, int[] edges, int cellCount,
                      float wPeriod = 0f, float[] vertexData = null);

        /// <summary>The vector orthogonal to three edge vectors of a tetrahedron. Its length is six times the volume.</summary>
        public static float4 TetNormal(float4 a, float4 b, float4 c);
        public float4 TetNormal(int tet);
        public float4 TetCentroid(int tet);
        /// <summary>Triangles of the faces that separate two cells or lie on an open end. For the solid shadow. Allocates.</summary>
        public int[] BuildCellBoundaryTriangles();
    }
}
```

```csharp
// File: Kernel/Mesh4D/Polytopes4D.cs
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
        /// Cell id = the phase interval. Vertex data = the phase, w / wPeriod.
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
| `Ball(n)` | on the unit 3-sphere | 48 · n³ | 8 | 0 | For `Slicer_Ball` (n = 24: a slice at offset c is short by about 0.9 / (n² · (1 − c²)) of its radius); S6 shows it with n = 6 (`slicer.object` = `ball`, 10,368 tetrahedra) |
| `MovingSphere` with 10 phases | 11 rings | 600 × 10 × 3 = 18,000 | 10 | 0 | The size of the tumour worldtube (§5.5) |

```csharp
// File: Kernel/Mesh4D/Rotation4D.cs
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

    public static unsafe class Projector4D
    {
        /// <summary>Distance of the 4D eye along +w, model units (decided here: twice the circumradius).</summary>
        public const float DefaultFocal = 2f;
        /// <summary>Perspective shadow: p.xyz * focal / (focal - p.w). The depth is held above 5% of focal.</summary>
        public static float3 Perspective(float4 p, float focal = DefaultFocal);
        /// <summary>Orthographic shadow: p.xyz.</summary>
        public static float3 Orthographic(float4 p);
        /// <summary>Shadow of a whole mesh: one position per vertex and, when outW is not null, its w before projection.</summary>
        public static void ProjectVertices(float4* vertices, int vertexCount,
                                           in float4x4 modelToSlice, in float4 translation,
                                           bool perspective, float focal,
                                           float3* outPositions, float* outW);
    }
}
```

### 13.3 The slicer kernel and its job

```csharp
// File: Kernel/Mesh4D/Slicer4D.cs
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>Cuts tetrahedra in R^4 with a hyperplane (Appendix A.8). Static, pointer-based, allocation-free.</summary>
    public static unsafe class Slicer4D
    {
        public const int MaxTrianglesPerTet = 2;
        public const int MaxVerticesPerTet = 6;

        /// <param name="vertices">Model-space vertices.</param>
        /// <param name="tetrahedra">4 * tetCount vertex indices.</param>
        /// <param name="tetCount">Number of tetrahedra.</param>
        /// <param name="tetCell">tetCount cell ids, or null (every cell id is then 0).</param>
        /// <param name="vertexData">One scalar per vertex to interpolate, or null.</param>
        /// <param name="modelToSlice">Rotation of the object in R^4.</param>
        /// <param name="translation">Added after the rotation.</param>
        /// <param name="normal">Unit normal of the cut in the slice frame.</param>
        /// <param name="offset">The cut is dot(normal, p) = offset.</param>
        /// <param name="outPositions">Receives up to vertexCapacity positions, 3 per triangle.</param>
        /// <param name="outNormals">Receives the unit flat normal of its triangle for every vertex.</param>
        /// <param name="outCells">Receives the cell id of the tetrahedron each vertex came from.</param>
        /// <param name="outVertexData">Receives the interpolated scalar, or null. Unused when vertexData is null.</param>
        /// <param name="vertexCapacity">Length of every output buffer. Slicing stops before the first tetrahedron that would not fit.</param>
        /// <param name="outVertexCount">Vertices written (3 * outTriangleCount).</param>
        /// <param name="outTriangleCount">Triangles written.</param>
        /// <param name="outTetsCut">Tetrahedra whose cut was written.</param>
        public static void Slice(
            float4* vertices,
            int* tetrahedra, int tetCount,
            int* tetCell,
            float* vertexData,
            in float4x4 modelToSlice, in float4 translation,
            in float4 normal, float offset,
            float3* outPositions, float3* outNormals, int* outCells, float* outVertexData,
            int vertexCapacity,
            out int outVertexCount, out int outTriangleCount, out int outTetsCut);

        /// <summary>Sum of the triangle areas of a slice (three positions per triangle).</summary>
        public static float Area(float3* positions, int triangleCount);
        /// <summary>Extent of a slice along axis: the largest minus the smallest coordinate of its vertices.</summary>
        public static float Extent(float3* positions, int vertexCount, float3 axis);
    }
}
```

The kernel writes plain buffers, a triangle soup of three unshared vertices per triangle, so the index buffer of
the receiving mesh is the fixed sequence 0, 1, 2, … It does not write `AtlasVertex`: the module turns the
buffers into `AtlasVertex` (palette coordinate of the cell, baked shade) in its own job (`SlicerMeshKernel` in
`Spaces/Slicer/Core/`).

```csharp
// File: Kernel/Mesh4D/SliceJob.cs
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
        [ReadOnly] public NativeArray<int> TetCell;         // length 0 = no cell ids
        [ReadOnly] public NativeArray<float> VertexData;    // length 0 = none
        public float4x4 ModelToSlice;
        public float4 Translation;
        public float4 Normal;
        public float Offset;
        public NativeArray<float3> OutPositions;            // length = 6 * tetrahedra
        public NativeArray<float3> OutNormals;              // length = 6 * tetrahedra
        public NativeArray<int> OutCells;                   // length = 6 * tetrahedra
        public NativeArray<float> OutVertexData;            // length = 6 * tetrahedra, or 0
        public NativeArray<int> OutCounts;                  // length 3: vertices, triangles, tetrahedra cut
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
| 2 – 2 | One quadrilateral as two triangles. With negative b₀, b₁ and positive a₀, a₁ the corner cycle is b₀a₀, b₁a₀, b₁a₁, b₀a₁: it never crosses itself |
| Edge point | Interpolated from the negative end to the positive end of the edge (Appendix A.8), so two tetrahedra that share an edge compute bit-identical points: no cracks. The cut is tested in model space; only the cut points are transformed |
| `outPositions` | `p.xyz` of the edge point, three per triangle |
| `outNormals` | The unit flat normal of the triangle, the same for its three vertices |
| `outCells` | The cell id of the tetrahedron the vertex came from |
| `outVertexData` | The mesh's `VertexData` interpolated to the edge point (the phase of a worldtube), when both buffers are given |
| Winding | Defined: every triangle (a, b, c) is wound so that cross(b − a, c − a) points out of the body, along its stored normal (Unity's front face), provided the tetrahedra are oriented outward and `modelToSlice` is a rotation. The slice material of S6 still has `_Cull` = 0 (off) |
| Capacity | Stops before the first tetrahedron that would not fit. Never writes past `vertexCapacity`. `outTetsCut` counts what was written. 6 · `tetCount` vertices always suffice |
| Cost | No allocation, no scratch buffer. No exception for any finite input |

The module (`SlicerSpace`) allocates the native arrays (`SlicerBuffers`, `Allocator.Persistent`) when the object
is chosen and disposes them in `Exit`. In `Tick` it schedules its own Burst job, which calls `Slicer4D.Slice`
and packs the result into `AtlasVertex` (`SlicerMeshKernel`); it completes the job in its `IUploadStep.Upload`
and calls `AtlasMesh.Upload`. It reschedules only when the rotation, the offset or the object changed: nothing
is cut while the cut does not move (§5.5). The shadow is a second job, rebuilt only when the body turns and only
while it is shown. The job runs inside `ProfilerMarker("Atlas.Slicer4D")` and the upload inside
`ProfilerMarker("Atlas.Slicer4D.Upload")` (§11.2). `SliceJob` is the kernel's own thin wrapper, for callers
that want the plain buffers.

Tests (lane A, `Tests/Kernel/Mesh4D/`, assembly `Atlas.Kernel.Tests`, plain NUnit, pinned managed arrays):

| Test | Set-up | Pass |
|---|---|---|
| `Slicer_Tesseract` | `Tesseract()`, `modelToSlice = CornerFirst()`, `normal = +w`, `offset = (c − 2) / 2` for the cut x + y + z + w = c of the unit tesseract | `Area` = 0.8660 at c = 0.5 and 6.9282 at c = 2.0, to four decimals |
| `Slicer_Ball` | `Ball(24)`, three rotations (identity among them), `normal = +w`, `offset = c` | The radius from the slice's area, and half its extent along x, y and z, are within 1 % of √(1 − c²) at c = 0, 0.5, 0.9 |
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
| Needs from `IRoomContent` | `SetWallMode(WallMode.Kerbs)`, `FloorVisible = false` (the cells bring the floor), `ObjectsVisible = true`: every object drawn once at its true position (placeholder, generated or built-in mesh). The room is **not** copied (§4.9) |
| Lattice | Cell (i, j) covers x from −wf/2 + i·sizeX, z from −df/2 + j·sizeZ; the lattice continues past the free rectangle in every direction |
| Walker's cell | (ci, cj) = (floor((x + wf/2) / sizeX), floor((z + df/2) / sizeZ)) of `HeadPosition` |
| Copies drawn | T², Klein: n × n cells round (ci, cj), n = `glued.copies`. T³: 3 × 3 × 3, vertical period 2.6 m |
| Cell | One open frame of at most 1,500 triangles: four corner posts, a floor with the grid drawn by the shader, one chiral landmark. One mesh, one draw call per eye per copy |
| Klein bottle | A cell in an odd row j is mirrored in x (negative scale): the determinant sign of copy (row, column) is (−1)^row |
| Self-avatar | At most 600 triangles (head, torso, left shoulder `AvatarLeft`, right shoulder `AvatarRight`), in every drawn cell but the walker's own, at the walker's position in that cell, mirrored like the cell. The same mesh also holds the walker's hands while `IHands` reports them tracked: for each hand a palm box, five fingers of three boxes and a forearm from the avatar's elbow to the wrist, 17 boxes of 12 triangles (204 a hand) in the palette cell `AvatarLeft` or `AvatarRight`, joints taken into the avatar's own frame so that a mirrored copy shows the other hand, rebuilt at step 6 when a joint moved (`GluedAvatarHands`, `GluedAvatarRig`). They ride in the avatar's draw, so no copy gains a draw call, and they are added only where the body leaves room for both inside the 600 (`AtlasMeshBudget.SelfAvatar`): a body that does not is drawn without hands (decision R15 of the hands brief, being built, appends them at a reduced level instead). A fault in the hands service turns the copies' hands off for the visit |
| Fog | §10.8: opaque at (n − 1) / 2 cells |
| Standing mode | Runs in place: no prompt to walk across a cell edge |
| `tileId` in `State` | (ci, cj, 0, 0, 0, 0) |
| `p[3]` to `p[7]` | mode, copies per side as drawn (3 for T³), sizeX, sizeZ, 0 |
| Spectator | `GluedSpace` implements `ISpaceStateReader`: mode and copies come from `p[3]` and `p[4]` |
| Budget | Cells and avatars alone: at most 110 draw calls and 110k triangles at 5 × 5; 120 and 120k for T³ (`GluedBudget`). With the room: the S1 rows of §15 |
| Test | `Glued_Handedness` (§11.1) |

The rules are engine-free in `Spaces/Glued/GluedLattice.cs` and `GluedCore.cs`: `GluedLattice`
(`VerticalPeriod = 2.6`), `GluedRules` (`MinCopies = 3`, `DefaultCopies = 5`, `MaxCopies = 7`, `LayoutNames`,
`Fog`), `GluedBudget`.

| Parameter | Values | Default | `onRelease` | `SceneCmd.variant` |
|---|---|---|---|---|
| `glued.mode` | enum: 0 `t2` (torus T²), 1 `klein` (Klein bottle), 2 `t3` (3-torus T³) | 0 | yes | `t2`, `klein`, `t3` |
| `glued.copies` | 3, 5, 7 per side (§9.6; written by the ladder) | 5 | yes | – |
| `glued.avatar` | 0 off, 1 on | 1 | no | – |

### 14.2 S5 Impossible Rooms (`PortalSpace`)

| Item | Contract |
|---|---|
| `Registered` | true, room by room. The real room's walls and furniture are where they really are; a virtual room's walls are virtual (§4.9) |
| Needs from the RoomModel | `freeRect` (`wf`, `df`): every cell of every room lies inside it, and on the eroded free floor of the scan. `standing`. The lowest wall height, for the door height. The room's id, for the seed that picks the booth's corner and the loop's entrance |
| Needs from `IRoomContent` | Its root `Transform`, with every renderer on a pool material: the real room is room 0 of `PortalRenderer`. A change event when a mesh replaces a placeholder, to call `RefreshRoom(0)`. `SetWallMode(WallMode.Full, 0f)`, floor and objects visible |
| Layout 0, booth to hall | A booth of 1.0 × 1.0 m where the floor allows, never under 0.7 m wide (at most 800 triangles), in a corner of the free rectangle, door 0.9 m wide and as high as the lowest wall, clamped to 1.8 to 2.1 m, towards the centre. Through the door: a hall that fills the free rectangle (at most 2,000 triangles) |
| Layout 1, four-turn loop | L-shaped corridors of width clamp(short side / 3, 0.7 m, 1.0 m) along the sides of the free rectangle, walls 2.4 m high, reusing the same floor; four right turns end in a different room. The rooms cover eight corners between them: two laps. At most 2,000 triangles per room |
| Fallback | The loop is built only when the short side of the free rectangle is at least 2.4 m (`PortalLayoutGenerator.MinLoopSide`, the Master Plan's figure) and not in standing mode; otherwise layout 0, viewed in place when standing. A floor that cannot hold the booth either (short side under 1.1 m) gives the real room alone, with no doorway (`PortalLayoutKind.RealRoomOnly`). `PortalLayout.IsFallback` says so |
| Portals | `PortalRenderer` (§12.2), the module's own unless one is published. Every portal transform is the identity (D16). No recursion |
| `tileId` in `State` | (current room id, 0, 0, 0, 0, 0); `sheet` = current room id |
| `p[3]` to `p[7]` | layout kind as built, room count, portal depth, 0, 0 |
| Spectator | `PortalSpace` implements `ISpaceStateReader`: the current room comes from `sheet` |
| Budget | At most 60 draw calls and 150k triangles; at most 1.0 ms of GPU time per visible portal; at most 15 stencil references per frame |
| Test | `Portal_Graph` (§11.1) on an engine-free room graph |

| Parameter | Values | Default | `onRelease` | `SceneCmd.variant` |
|---|---|---|---|---|
| `rooms.layout` | enum: 0 `booth` (booth to hall), 1 `loop` (four-turn loop) | 0 | yes | `booth`, `loop` |
| `rooms.count` | 2 to 6 rooms in the loop | 6 | yes | – |
| `portal.depth` | 0 doorways show fog, 1 doorways show the next room | 1 | no | – |

All three are in the parameter group `portal` (`SpaceIds.ParamGroup(SpaceId.Portal)`), whatever their name
prefix. A released `rooms.layout` or `rooms.count` regenerates the rooms at the next `Tick`, puts the walker
back in the real room and bumps the world version. The layout rules are engine-free in
`Spaces/Portal/PortalLayout.cs`, `PortalLayoutGenerator.cs` and `PortalSpaceCore.cs`.

### 14.3 S6 Slicer (`SlicerSpace`)

| Item | Contract |
|---|---|
| `Registered` | true. Home stays as it is; the 4D object floats in it |
| Needs from the RoomModel | `freeRect` (`wf`, `df`), `standing` |
| Needs from `IRoomContent` | The default: walls in full, objects visible |
| Object | Centre at (+wf / 4, 1.25 m, 0). Radius clamp(0.2 · wf, 0.25 m, 0.5 m) for a model circumradius of 1 (decided here) |
| The w lane | A line on the floor at x = −wf / 4, along Z, of length min(df − 0.4 m, 3.0 m), centred on z = 0. Walking it moves the cut: w₀ = lerp(−1.1, +1.1, t) with t the walker's fraction along the lane. Its twin is the stick. The input touched last owns w₀ for 2 s (`SlicerController.OwnerHoldSeconds`) |
| Standing mode | No lane, and none either when the lane would be shorter than 0.8 m (`SlicerLayout.MinLane`). The cut is moved with the stick (`P1-A15`) |
| Views | Slice (the cross-section, a colour per cell) and shadow (the cross-section with the dithered, ghosted shadow of the whole body round it, coloured along the w-depth strip) |
| Rotation | In the planes XW, YW, ZW, by the sticks; the cut stays at `normal = +w`, tilted by `slicer.tilt` for the tube |
| Input | `AtlasAction.Primary`: next object. `AtlasAction.Secondary`: slice or shadow. `AtlasAxis.LeftY`: w. `LeftX`: XW. `RightY`: YW. `Twist`: ZW. Each has a gaze twin: hold handles for the axes, chips for the two actions |
| `tileId` in `State` | six zeros |
| `p[3]` to `p[7]` | w₀; the XW, YW, ZW angles in radians; object + 16 · view (`SlicerState.Pack`) |
| Spectator | `SlicerSpace` implements `ISpaceStateReader`: the cut, the turns, the object and the view come from `p[3]` to `p[7]` |
| Budget | At most 70 draw calls and 160k triangles with Home; slicer cost of §13.3 |

| Parameter | Values | Default | `onRelease` | `SceneCmd.variant` |
|---|---|---|---|---|
| `slicer.source` | enum: 0 `live`, 1 `stepped` (the cut moves in 32 fixed steps, so the slice is rebuilt only when it reaches the next step), 2 `tesseract` (the tesseract whatever `slicer.object` says) | 0 | yes | – |
| `slicer.object` | enum: 0 `tesseract`, 1 `cell5`, 2 `cell16`, 3 `cell24`, 4 `tube` (moving-sphere worldtube), 5 `ball` (`Ball(6)`) | 0 | yes | `tesseract`, `cell5`, `cell16`, `cell24`, `tube`, `ball` |
| `slicer.view` | enum: 0 `slice`, 1 `shadow` | 0 | no | – |
| `slicer.w` | −1.1 to 1.1, model units | 0 | no | – |
| `slicer.xw`, `slicer.yw`, `slicer.zw` | −180 to 180, degrees | 0 | no | – |
| `slicer.tilt` | 0 to 80, degrees; the tube only (the scanner tilt of §5.4) | 0 | no | – |

### 14.4 What lane A's spaces and SpaceLab need from the input contract

Each with a gamepad binding and a gaze-dwell twin (rule 5). Names are lane C's (`Atlas.Input.AtlasAction`,
`AtlasAxis`).

| Action | Kind | Used by |
|---|---|---|
| `AtlasAction.ReturnHome` | button | `SpaceActions` → `ISpaceHost.ReturnHome()` |
| `AtlasAction.NextSpace`, `PrevSpace` | two buttons | `SpaceActions` → `RequestSwitch` to the next or previous available space |
| `AtlasAction.SpaceMenu` | button | `SpaceActions` → lane D's menu, on its Spaces page |
| `AtlasAction.Primary`, `Secondary` | two buttons | S6 (next object, view). S1 and S5 read no input |
| `AtlasAxis.LeftY` | one axis, −1 to 1 | S6: w |
| `AtlasAxis.LeftX`, `RightY` | two axes | S6: XW, YW |
| `AtlasAxis.Twist` | one axis | S6: ZW |

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
| S0 Home | ≤ 80 | ≤ 250k | Up to 32 furniture meshes at the full copy, 120,000 triangles in total; shell ≤ 3,000. Held by `RoomBudget.HomeDrawCallsPerFrame` and `HomeTrianglesPerFrame` and by their tests |
| S1 Glued 5 × 5 | ≤ 150 | ≤ 250k | 25 cells, 24 avatars, the room once |
| S1 Glued T³ | ≤ 160 | ≤ 250k | 27 cells, 26 avatars, the room once |
| S2 Curved Walk, ρ = 3 | ≤ 240 | ≤ 370k | Up to 71 tiles, plus the room at Home's full load (80 draws, 250k triangles); the tiles' own share stays at 150 draws and 120k triangles |
| S3 Aligned Cells | ≤ 170 | ≤ 200k | Up to 57 cell instances; furniture once |
| S4 Small Planet | ≤ 70 | ≤ 190k | Six tiles plus the room |
| S5 Impossible Rooms | ≤ 60 | ≤ 150k | Two rooms through one portal, plus the room |
| S6 Slicer | ≤ 70 | ≤ 160k | Home plus the cross-section |

Home's and S2's rows are changed here, and only they are held by code: `RoomBudget.Estimate` gives about 246,000
triangles and 52 draw calls per frame for the 24 stand-ins of 5,000 triangles of `budget_room_120k` at `Full`
(`FurnitureLevelTests`), and the Home tests assert at most 80 draw calls and 250,000 triangles. That is a count of
the builders' output, not a phone measurement. S2 Curved Walk, which also shows the full copies, is held by the world lint: `WorldLintSettings` counts the room at Home's
full load and caps S2 at 240 draw calls and 370,000 triangles, the tiles' own share staying at 150 and 120,000
(`WorldLintTests` asserts all three); that is a count, and the phone measurement is still open. The mesh design also expects 24 more draw calls for each of S1, S3, S4, S5 and
S6 (their triangles stay as they are, since they show the light copies). Those last figures are planning figures: no
test holds them, and check M3 on the phone (`budget_room_120k` through the PerfTour) replaces them with measured
ones or moves the space to `Light`. The rows of S1, S3, S4, S5 and S6 above are therefore still the old ones.

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

**Per asset** (§8.4): room shell ≤ 3,000 triangles; a generated object, full copy, 1,500 to 15,000 and no more than its
target (3,000 to 15,000 by the area rule of the mesh design §5.4), and its light copy 1,500 to 5,000 and no more than the room's light target; a built-in fallback object ≤ 1,500; a Glued cell ≤ 1,500; a room of S5 ≤ 2,000; the self-avatar ≤ 600;
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
| 0 | Refuses anything but macOS with `xcodebuild`; refuses a project that is open in the Editor (`Temp/UnityLockfile`) | – |
| 1 | With `<branch>`: refuses a working tree with changes; `git fetch origin`, `checkout`, `pull --ff-only origin <branch>` (a failed fast-forward does not stop the build), `lfs pull` | – |
| 2 | Unity in batch mode: `-batchmode -quit -buildTarget iOS -executeMethod Atlas.EditorTools.AtlasBuild.IOS -buildOutput ATLAS/Build/iOS` (append mode when the folder already holds a project). The render set-up of §9.1 is applied first, as a build preprocess step | `ATLAS/Build/iOS/Unity-iPhone.xcodeproj`; log `ATLAS/Build/unity-build.log` |
| 3 | `xcodebuild -scheme Unity-iPhone -configuration Release -destination generic/platform=iOS -derivedDataPath ATLAS/Build/DerivedData -allowProvisioningUpdates CODE_SIGN_STYLE=Automatic` (`Debug` with `ATLAS_DEV=1`; `DEVELOPMENT_TEAM` from `ATLAS_TEAM_ID`) | `ATLAS/Build/DerivedData/Build/Products/Release-iphoneos/*.app`; log `ATLAS/Build/xcodebuild.log` |
| 4 | `xcrun devicectl device install app` on every paired physical iPhone; with `ATLAS_LAUNCH=1`, `devicectl device process launch` with the bundle id of `ProjectSettings.asset` | – |

It exits with 0 on success and 1 on any failure, after printing the failing lines of the log. The project must
be closed in the Unity Editor. Fallback: open the exported project in Xcode and press Run. The scenes built are
the enabled scenes of `EditorBuildSettings` (§12.3).

The Station's Spectator is built by `Atlas.EditorTools.AtlasBuild.Spectator`
(`-executeMethod Atlas.EditorTools.AtlasBuild.Spectator -buildOutput Build/Spectator/AtlasSpectator.exe`):
`StandaloneWindows64`, the define `ATLAS_SPECTATOR`, the scene `PC_Spectator` first and then every enabled
`Space_*` scene. No script wraps it.

### 16.2 Info.plist keys and build settings

`Editor/AtlasPostBuild.cs` (`[PostProcessBuild]`, lane A, compiled only under `UNITY_IOS`) writes every key.
Nobody edits the generated Xcode project by hand (Appendix C.5). A lane that needs another key asks lane A.

| Key | Value | Why |
|---|---|---|
| `NSCameraUsageDescription` | `AtlasUsageText.Camera`: "ATLAS uses the camera to track your position, scan your room, photograph objects and see your hands." | Tracking, scan, object photos and the tracked hands, whose detector reads the camera frames ARKit already makes |
| `NSLocalNetworkUsageDescription` | `AtlasUsageText.LocalNetwork` | Unicast UDP to the Hub raises the prompt |
| `NSAppTransportSecurity` → `NSAllowsLocalNetworking` | `YES` | HTTP to a LAN address |
| `UIFileSharingEnabled` | `YES` | The perf CSV and the event log are readable from the Mac |
| `UIRequiresFullScreen` | `YES` | – |
| `CFBundleURLTypes` | one entry: `CFBundleURLName` = the bundle id, scheme `atlas` | The pairing link `atlas://<ip>:9050` |
| `MetalHudEnabled` | `YES`, development builds only | Metal Performance HUD |
| Build setting `ENABLE_BITCODE` | `NO` on the project and both targets | – |

The same step also adds the RoomPlan wall bridge (scan route 4, lane C): `AtlasRoomPlanProject.CopySource` and
`AtlasRoomPlanProject.Apply` (`Editor/AtlasRoomPlanProject.cs`, tested by `Atlas.Editor.Tests`) put
`AtlasRoomPlan.swift` into the sources of the `UnityFramework` target, link `RoomPlan.framework` weakly, and set
`OTHER_SWIFT_FLAGS`, `SWIFT_VERSION` 5.0, `CLANG_ENABLE_MODULES` and `ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES`. Applying
it twice equals applying it once. The edit has been compiled and linked in Unity's export of this tree (the framework
target built; the app target stopped at its storyboards because the iOS platform component is not installed on the
build Mac), and the plugin has not run on a device.

The tracked hands' detector is `Assets/Plugins/iOS/AtlasHands.swift` (Apple Vision's hand-pose request on the frames
ARKit already makes, with the LiDAR depth under each joint; `Atlas.Hands.ARKit.VisionHandsSource` is its C# half). It
sits beside `AtlasRoomPlan.swift`, but `AtlasPostBuild` and `AtlasRoomPlanProject` name only that one: this file is left
to Unity's own handling of Swift plugin sources. It reads the AR session through `XRSessionSubsystem.nativePtr`, only
reads it, and configures, runs and pauses nothing. The camera usage text above is the only plist change made for it; the
plugin keeps no picture. The file type-checks against the iOS SDK (`xcrun -sdk iphoneos swiftc -typecheck -target
arm64-apple-ios17.0`); it has never been through an Xcode export or run on a phone (§17.1).

Not written, on purpose: `NSBonjourServices`, a microphone key, a multicast entitlement, and
`CADisableMinimumFrameDurationOnPhone` (the display stays at 60 Hz, §8.2).

Player settings applied by `Editor/AtlasProjectSetup.cs` (menu "Atlas/Apply Project Settings", or
`-executeMethod Atlas.EditorTools.AtlasProjectSetup.Apply`): product name `ATLAS`, IL2CPP, ARM64, Metal only,
iOS 17.0, full screen required, landscape left only, Frame Timing Stats on, managed stripping Low, VSync count 1,
"Adjust iOS FPS based on thermal state" off, static and dynamic batching off, plain http always allowed
(`insecureHttpOption`, for the Forge and the Hub on the local network), bundle id `com.winstonthov.atlas`,
automatic signing, the camera usage text, and the URP, renderer, feature and layer values of §9.1 and §12. It
also makes sure `Atlas_Boot.unity` exists with the AR Session and the XR Origin.

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
| The six shaders of the table in §10.1 load from their `Resources` folders in a player build. Without `Atlas/NearText` the near text is not depth-tested and a warning says so; without `Atlas/HexFlip` spaces fade | phone A |
| The Render Objects features of §12.2 are in the renderer asset after the set-up has run, and `PortalRenderer.FeaturesFound` is true | Editor, then phone A |
| Each ladder rung forced from the Console takes effect within 1 s | G4 |
| The tracked hands and the near panels, in the headset (hands design §9.3, §9.4): both hands draw in both eyes in front of the world and behind nearer things; no hand is cut by the near plane; a finger in front of a menu row covers the row and its text; the reticle is single over a panel at 0.45 m | phone A in the headset |
| `AtlasHands.swift` compiles and links into `UnityFramework` in Unity's export (six `_atlas_hands_` symbols), `HandsPlatform.HasPlatformSource` is true on the phone, and the detector's results reach the hands with the AR camera component disabled in headset mode (the checks of `docs/verification/UNITY_SIDE_BRIEF.md`, step 3i) | Xcode, phone A |
| Home and Curved Walk hold 60 frames per second with the full furniture copies (`budget_room_120k` through the PerfTour; mesh design check M3); the first ladder step below nominal drops the furniture to its light copies | phone A |

### 17.2 Where the repository differs from this page today

Lane A's own work list; nothing here is another lane's to fix. The code of `AtlasProjectSetup` writes what this
page says; the assets in the repository were last saved before it and are brought up to date the first time the
project is opened in the Editor, and at the start of every player build (§9.1).

| File | In the repository today | After the set-up has run |
|---|---|---|
| `Rendering/Settings/Atlas_URP_iOS.asset` | Render scale 0.8, main light on | Render scale 1.0, main and additional lights disabled, no shadows |
| `Rendering/Settings/Atlas_URP_iOS_Renderer.asset` | `intermediateTextureMode` `Always`; none of the features of §12.2 | `Auto`; the mask, the fifteen room and the two HUD features; feature layers out of the renderer's own masks |
| `ProjectSettings/TagManager.asset` | No Atlas layer name | §12.1 |
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
3. **Field of view.** The eye frusta are clamped to 50.28° to each side and 48° up and down (96° vertical at
   aspect 1398 : 1290 with the default lens numbers), taken from §8.5's "about 50° to each side and 48° up and
   down". Confirm against the headset's real field of view; a smaller one wastes pixels, a larger one shows a
   black border. The two limits are properties of `LensModel` and not registry parameters yet.
4. **Values added to §12.4.** `QualityTier.High = 4` (ρ = 3.5, 7 × 7), `glued.copies` = 7 and `render.msaa` = 1
   extend the kill-switch ranges so that the high and the critical rung are ordinary parameter values.
   `State.flags` has two bits for the tier, so `High` is sent as 0.
5. **Ladder details the plan leaves open.** Step-up thresholds 6.5 ms and 8.0 ms (proposed); "missing 60 fps"
   read as frame p95 over 17.2 ms across the last 10 s; no automatic step up from `Critical`; no automatic step
   up at all while the perf probe has no data; leaving a pin restarts the 20 s dwell. And what the walker sees
   on `Critical`: the HUD shows "Session over" and rendering goes on at 30 fps.
6. **Shading.** The shader does not light by the normal (D23); every generator bakes one fixed light into the
   vertex shade with `AtlasShading`. If the first headset view reads too flat, a normal term is an addition to
   the warp contract (lane B §9.4, item 3) and is announced to all four lanes.
7. **One look, more than one shader.** README F7 names one shader. The gaze reticle, the Bump Guard cue and the
   flat-twin map each draw with a shader of their own (§10.1), and so do the hexagon dome (`Hidden/Atlas/HexFlip`) and
   the text of panels within reach of the hands (`Atlas/NearText`, which cannot be `Atlas/Surface`: legacy `TextMesh`
   takes its glyph alpha from the font's texture). README F7 now names `Atlas/NearText`; accept the others as
   exceptions in F7 as well, or move them onto `Atlas/Surface`.
8. **The 0.25 m rule has two measures.** Gridded shapes keep cell sides to 0.25 m (diagonals to 0.354 m); the
   strict pass keeps every edge to 0.25 m at about twice the triangles (§10.2). Decide which one the warp of
   lane B needs, because the triangle budgets of §15 were written for the first.
