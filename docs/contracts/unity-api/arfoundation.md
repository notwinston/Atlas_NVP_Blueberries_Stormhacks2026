# AR Foundation 6.5.0, ARKit 6.5.0, XR Core Utils, XR Management, XR Simulation: verified API reference

Unity 6000.3.25f1. Everything below was read in the installed package sources or checked against the compiled
assemblies. Every code block was compiled (fragments inside wrapper methods) against `Library/ScriptAssemblies/*.dll` plus the
engine DLLs, with the iOS, Editor and Windows define sets. Nothing is listed that was not found. Where the sources cannot answer (the native ARKit and
Simulation plugins are closed), the item is marked **DEVICE** and collected in section 17.

| Package | Version | Short root used below |
|---|---|---|
| `com.unity.xr.arfoundation` | 6.5.0 | `AF` = `ATLAS/Library/PackageCache/com.unity.xr.arfoundation@13d2457b468b/Runtime`, `AFE` = same package `/Editor`, `AFD` = `/Documentation~` |
| `com.unity.xr.arkit` | 6.5.0 | `AK` = `ATLAS/Library/PackageCache/com.unity.xr.arkit@f7ae96e6506f/Runtime`, `AKD` = `/Documentation~` |
| `com.unity.xr.core-utils` | 2.6.0 | `CU` = `ATLAS/Library/PackageCache/com.unity.xr.core-utils@a8b900321199/Runtime` |
| `com.unity.xr.management` | 4.6.1 | `XM` = `ATLAS/Library/PackageCache/com.unity.xr.management@df145a5bc89e/Runtime` (`XME` = `/Editor`) |
| `com.unity.inputsystem` | 1.20.0 | `IS` = `ATLAS/Library/PackageCache/com.unity.inputsystem@7a4e1a2a8194/InputSystem` |
| engine XML | 6000.3.25f1 | `ENG` = `~/unity-managed/Editor/Data/Managed/UnityEngine` |
| project | | `PS` = `ATLAS/ProjectSettings`, `AX` = `ATLAS/Assets/XR`, `LSA` = `ATLAS/Library/ScriptAssemblies` |

Citations are `root/path:line`. "Namespace" and "assembly" are given once per section header.

---

## 0. Where the task's names differ from reality

| Asked for | Reality in 6.5.0 on 6000.3.25f1 | Use instead |
|---|---|---|
| `XROrigin.MakeContentAppearAt` | Does not exist: not on `XROrigin` (`CU/XROrigin.cs`), not on the obsolete `ARSessionOrigin` (`AF/ARFoundation/Session/ARSessionOrigin.cs:55-79`); absent from `Unity.XR.ARFoundation.dll` and `Unity.XR.CoreUtils.dll` | Move `XROrigin.Origin.transform` yourself (section 5.3) or use `MoveCameraToWorldLocation`, `MatchOriginUp*` |
| `ARSession.matchFrameRate` | Not a member (CS1061) | `matchFrameRateRequested` (get/set) and `matchFrameRateEnabled` (get) |
| `ARMeshManager` classification API | `submeshClassificationEnabled` and `TryGetSubmeshClassifications` are inside `#if UNITY_6000_4_OR_NEWER` (`AF/ARFoundation/Meshing/ARMeshManager.cs:152,165,237,339`). They are not in this build (verified in `Unity.XR.ARFoundation.dll`). The engine `XRMeshSubsystem` in 6000.3 has no classification member either (`UnityEngine.XRModule.dll`) | ARKit only: obsolete `ARKitMeshSubsystemExtensions` (section 9.4) |
| `ARMeshManager.meshesChanged` | `[Obsolete]` since 6.4 (`ARMeshManager.cs:201`) | `meshInfosChanged` (a `UnityEvent`) |
| `ARPlane.classification` | `[Obsolete]` (`AF/ARFoundation/Planes/ARPlane.cs:66`) | `classifications` (flags) |
| `ARPlaneManager.planesChanged`, `ARAnchorManager.anchorsChanged` | `[Obsolete]` | `trackablesChanged` (a `UnityEvent`, section 15) |
| Depth raycast in ARKit | `TrackableType.Depth` exists (`AF/ARSubsystems/Raycasts/TrackableType.cs:99`) but ARKit and Simulation do not implement it (`AKD/arkit-raycasts.md`) | Own CPU depth unprojection (section 8.4) |
| `XRCameraConfiguration` pixel format, HDR, binned flags | Fields are only `width`, `height`, `resolution`, `framerate` (`int?`), `depthSensorSupported`, `nativeConfigurationHandle` (`AF/ARSubsystems/Camera/XRCameraConfiguration.cs:28-61`) | Pick by resolution and framerate only |
| ARKit RoomPlan walls, doors, windows | `BoundingBoxClassifications` has furniture only, no Wall, Door, Window, Opening (`AF/ARSubsystems/BoundingBoxes/BoundingBoxClassifications.cs`) | Walls and floor come from planes or your own scan |

---

## 1. Assemblies, namespaces, platforms

| Assembly (asmdef `name`) | Namespace | Built for | Holds |
|---|---|---|---|
| `Unity.XR.ARFoundation` | `UnityEngine.XR.ARFoundation` | all | `ARSession`, `ARSessionState`, `ARCameraManager`, `ARCameraBackground`, `AROcclusionManager`, `ARMeshManager`, `ARPlaneManager`, `ARPlane`, `ARBoundingBoxManager`, `ARBoundingBox`, `ARAnchorManager`, `ARAnchor`, `ARRaycastManager`, `ARRaycastHit`, `ARInputManager`, `ARTrackablesChangedEventArgs<T>`, `LoaderUtility`, `TransformExtensions` |
| `Unity.XR.ARSubsystems` | `UnityEngine.XR.ARSubsystems` | all | `TrackingState`, `NotTrackingReason`, `XRCameraConfiguration`, `XRCameraIntrinsics`, `XRCpuImage`, `EnvironmentDepthMode`, `Human*Mode`, `PlaneClassifications`, `PlaneDetectionMode`, `BoundingBoxClassifications`, `TrackableType`, `TrackableId`, `Result<T>`, `XRResultStatus`, `XRMeshClassification`, `HandheldARInputDevice`, all `XR*Subsystem` base types |
| `Unity.XR.CoreUtils` | `Unity.XR.CoreUtils`, `Unity.XR.CoreUtils.Collections` | all | `XROrigin`, `TransformExtensions`, `ReadOnlyList<T>` |
| `Unity.XR.Management` | `UnityEngine.XR.Management` | all | `XRGeneralSettings`, `XRManagerSettings`, `XRLoader` |
| `Unity.XR.ARKit` | `UnityEngine.XR.ARKit` | **Editor and iOS only** (`AK/Unity.XR.ARKit.asmdef`, `includePlatforms`) | `ARKitLoader`, `ARKitSessionSubsystem`, `ARWorldMap`, `ARWorldMapRequest`, `ARWorldMapRequestStatus`, `ARWorldMappingStatus`, `RoomPlanBoundingBoxSubsystem`, `ARKitMeshSubsystemExtensions` (obsolete), `ARMeshClassification` (obsolete) |
| `Unity.XR.Simulation` | `UnityEngine.XR.Simulation` | **Editor, LinuxStandalone64, macOSStandalone, WindowsStandalone64** (`AF/Simulation/Unity.XR.Simulation.asmdef`) | `SimulationLoader`, `SimulatedBoundingBox`, `SimulationCameraPoseProvider` |
| `Unity.InputSystem` | `UnityEngine.InputSystem`, `UnityEngine.InputSystem.XR` | all | `TrackedPoseDriver`, `InputAction`, `InputActionProperty` |
| `Unity.Collections` | `Unity.Collections` | all | only for the `Reinterpret<T,U>(this NativeArray<T>)` extension (`ATLAS/Library/PackageCache/com.unity.collections@84e8eb3fc12e/Unity.Collections/NativeArrayExtensions.cs:174`); `NativeArray`, `Allocator` themselves are engine types |
| none (engine) | `UnityEngine.XR`, `Unity.Collections` | all | `XRMeshSubsystem`, `MeshId`, `XRInputSubsystem`, `Awaitable<T>`, `NativeArray<T>`, `Allocator` |

### 1.1 What an asmdef must reference

C# needs every assembly whose types appear in members you touch (CS0012). Current `Atlas.Scan` references `Unity.XR.ARFoundation`, `Unity.XR.ARSubsystems`, `Unity.XR.CoreUtils`, `Unity.Mathematics` only (`Assets/Atlas/Scan/Atlas.Scan.asmdef`).

| You use | Add to the asmdef | Guard in code |
|---|---|---|
| any AR Foundation manager, `ARSession` | `Unity.XR.ARFoundation`, `Unity.XR.ARSubsystems` (signatures expose `XRCpuImage`, `TrackableId`), `Unity.XR.CoreUtils` (`trackablesChanged` args use `ReadOnlyList<T>`; `meshInfosChanged` args too) | none |
| `XROrigin`, pose extensions | `Unity.XR.CoreUtils` | none |
| `LoaderUtility.GetActiveLoader()`, `XRGeneralSettings`, `XRLoader` | `Unity.XR.Management` (the return type is `XRLoader`) | none |
| `TrackedPoseDriver`, `InputAction` | `Unity.InputSystem` | none |
| `ARKitSessionSubsystem`, `ARWorldMap`, `ARKitLoader`, `RoomPlanBoundingBoxSubsystem`, `ARKitMeshSubsystemExtensions` | `Unity.XR.ARKit` (and `Unity.XR.Management`, `Unity.XR.ARSubsystems`) | `#if UNITY_EDITOR \|\| UNITY_IOS`; for calls that need the device `#if UNITY_IOS && !UNITY_EDITOR` |
| `SimulationLoader`, `SimulatedBoundingBox` (optional, typed detection) | `Unity.XR.Simulation` | `#if UNITY_EDITOR` (the assembly also exists in desktop players, but ATLAS only needs it in the Editor) |
| `unsafe` pointer reads of a `NativeArray` | `allowUnsafeCode: true` on your asmdef | none |
| `Reinterpret<byte,float>()` | `Unity.Collections` | none |

**Does `Unity.XR.ARKit` compile off iOS?** It is built only for the Editor and the iOS player. In an Android, Windows or macOS **player** the assembly does not exist, so any unguarded use fails to compile. In the Editor it always compiles, whatever the active build target, but `UNITY_XR_ARKIT_LOADER_ENABLED` is set only for the iPhone target (`PS/ProjectSettings.asset:842-843`, added by `AK/../Editor/ARKitBuildProcessor.cs:355`). Without that define every ARKit native call is a stub: `Api.AtLeast*()` return `false` (`AK/ARKitApi.cs:103-156`) and instance members throw `NotImplementedException("Apple ARKit XR Plug-in Provider not enabled in project settings.")` (`AK/Constants.cs:5`, `AK/ARKitSessionSubsystem.cs:550-701`). Static capability flags (`ARKitSessionSubsystem.worldMapSupported`) are safe anywhere; instance members need an instance, which only exists when the ARKit loader created one, so `session.subsystem as ARKitSessionSubsystem` returns `null` off device.

Harness note: `tools/headless/verify.py:57-66` defines `UNITY_XR_ARKIT_LOADER_ENABLED` in its `ios` and `editor` configs and excludes `Unity.XR.ARKit` from its `win` config. The `editor` config also defines `UNITY_IOS`, which a real Standalone-target Editor does not.

### 1.2 Same-named extension methods are ambiguous

`Unity.XR.CoreUtils.TransformExtensions` (`CU/Extensions/TransformExtensions.cs:9`) and `UnityEngine.XR.ARFoundation.TransformExtensions` (`AF/ARFoundation/Utils/TransformExtensions.cs:9`) both declare `TransformPose(this Transform, Pose)`, `InverseTransformPose(this Transform, Pose)` and `InverseTransformRay(this Transform, Ray)`. With both namespaces imported the call fails with **CS0121** (compiled and confirmed). Import only one, or call `TransformExtensions.InverseTransformPose(t, p)` with the class qualified. `GetWorldPose`, `SetWorldPose`, `GetLocalPose`, `SetLocalPose` exist only in the CoreUtils class.

---

## 2. What this project has configured

| Fact | Source |
|---|---|
| Standalone providers = `SimulationLoader`. iPhone providers = `AR Kit Loader`. Android and visionOS providers = none. `InitManagerOnStart = 1` everywhere | `AX/XRGeneralSettings.asset:34-35,83,141-142`, loader guids in `AX/Loaders/*.meta` |
| In the Editor, `XRGeneralSettings.Instance` is **always the Standalone group's settings**, whatever the active build target. Play Mode therefore loads XR Simulation | `XME/XRGeneralSettingsPerBuildTarget.cs:106,173` |
| ARKit loader never initialises outside the iOS player (`Initialize()` returns `false` unless `UNITY_IOS && !UNITY_EDITOR`) | `AK/ARKitLoader.cs:122-153` |
| Simulation environment prefab unset, fallback `DefaultSimulationEnvironment.prefab`. Environment layer **30**, named `XR Simulation` | `AX/UserSimulationSettings/Resources/XRSimulationPreferences.asset:16-17`, `AX/Resources/XRSimulationRuntimeSettings.asset:15`, `PS/TagManager.asset:38` |
| `activeInputHandler: 1`: only the Input System package is active, legacy `UnityEngine.Input` is off | `PS/ProjectSettings.asset:943` |
| iOS target 17.0, Camera Usage Description set, ARKit requirement `Required` (value 0) | `PS/ProjectSettings.asset:202,583`, `AX/Settings/AR Kit Settings.asset`, `AK/../Editor/ARKitSettings.cs:20-31` |
| A Windows player build keeps the XR Simulation settings in the player when the Standalone providers include it | `AFE/Simulation/SimulationSettingsBuildProcessor.cs:29-36`. The current Standalone provider list is exactly that and `InitManagerOnStart = 1`, so the Windows Spectator player will most likely create the Simulation subsystems at launch (inference; nothing runs without an `ARSession`). Decide deliberately |

Layer 30 is taken by the simulation environment; layers 0-7 and 31 cannot be chosen as the environment layer (`AF/Simulation/SimulationConstants.cs:5`). ATLAS must not assign layer 30.

---

## 3. Detecting the backend at run time

```csharp
using UnityEngine.XR.ARFoundation;     // LoaderUtility
using UnityEngine.XR.ARSubsystems;     // XRSessionSubsystem
using UnityEngine.XR.Management;       // XRLoader

public enum XrBackend { None, ARKit, Simulation, Other }

public static XrBackend Detect()
{
    XRLoader loader = LoaderUtility.GetActiveLoader();          // null when no loader is active
    if (loader == null) return XrBackend.None;
    XRSessionSubsystem session = loader.GetLoadedSubsystem<XRSessionSubsystem>();
    if (session == null) return XrBackend.Other;
    switch (session.subsystemDescriptor.id)
    {
        case "ARKit-Session":          return XrBackend.ARKit;
        case "XRSimulation-Session":   return XrBackend.Simulation;
        default:                       return XrBackend.Other;
    }
}
```

This needs no ARKit or Simulation reference and runs on every platform. Typed variants:

```csharp
var gs = XRGeneralSettings.Instance;                             // may be null
XRLoader active = (gs != null && gs.Manager != null) ? gs.Manager.activeLoader : null;
#if UNITY_EDITOR || UNITY_IOS        // needs Unity.XR.ARKit
bool arkit = active is UnityEngine.XR.ARKit.ARKitLoader;
#endif
#if UNITY_EDITOR                     // needs Unity.XR.Simulation
bool sim = active is UnityEngine.XR.Simulation.SimulationLoader;
#endif
```

| Item | Signature | Source | Gotcha |
|---|---|---|---|
| `XRGeneralSettings.Instance` | `static XRGeneralSettings Instance { get; }` | `XM/XRGeneralSettings.cs:22` | Player: set in the settings asset's `Awake` (`:70-78`), so it is null before that. Editor: Standalone group. Always null-check |
| `XRGeneralSettings.Manager` | `XRManagerSettings Manager { get; set; }` | `:36` | |
| `XRManagerSettings.activeLoader` | `XRLoader activeLoader { get; }` | `XM/XRManagerSettings.cs:137` | `ActiveLoaderAs<T>()` at `:145` |
| `XRLoader.GetLoadedSubsystem<T>()` | `abstract T GetLoadedSubsystem<T>() where T : class, ISubsystem` | `XM/XRLoader.cs:51` | |
| `LoaderUtility.GetActiveLoader()` | `static XRLoader GetActiveLoader()` | `AF/ARFoundation/Utils/LoaderUtility.cs:21` | Same null-safe chain as above. `Initialize()` (`:36`) / `Deinitialize()` (`:47`) create or destroy **all** subsystems; disable every AR component first |
| Subsystem ids, ARKit | `ARKit-Session`, `-Camera`, `-PointCloud`, `-Plane`, `-Anchor`, `-Raycast`, `-HumanBody`, `-EnvironmentProbe`, `-Input`, `-ImageTracking`, `-ObjectTracking`, `-Face`, `-Occlusion`, `-Participant`, `-Meshing`, `-RoomPlan-BoundingBox` | `AK/ARKitLoader.cs:125-142` | A descriptor is registered only if the OS version allows (Occlusion iOS 13+, RoomPlan iOS 17+, `ARKitOcclusionSubsystem.cs:21`, `RoomPlanBoundingBoxSubsystem.cs:144`). Anchor is registered only under `UNITY_IOS && !UNITY_EDITOR` (`ARKitAnchorSubsystem.cs:163`) |
| Subsystem ids, Simulation | `XRSimulation-Session`, `-Camera`, `-Input`, `-Plane`, `-PointCloud`, `-ImageTracking`, `-Raycast`, `-Meshing`, `-EnvironmentProbe`, `-Anchors`, `-Occlusion`, `-BoundingBox` | `AF/Simulation/SimulationLoader.cs:31-42` | |

Platform patterns:

| Need | Pattern |
|---|---|
| Code that calls ARKit native APIs (world map, mesh classification, RoomPlan, camera locks) | `#if UNITY_IOS && !UNITY_EDITOR`. `UNITY_IOS` is also defined in an Editor whose active target is iOS, so `!UNITY_EDITOR` matters |
| Code that only names ARKit types so it compiles in the Editor and on iOS | `#if UNITY_EDITOR \|\| UNITY_IOS` |
| Runtime branch | `Application.platform == RuntimePlatform.IPhonePlayer` (`ENG/UnityEngine.CoreModule.xml`, `F:UnityEngine.RuntimePlatform.IPhonePlayer`), `Application.isEditor`. `isEditor` is true in Play Mode whatever the build target |
| "Is a real AR session available" | `Detect() == XrBackend.ARKit`, never the platform alone |
| Query a feature before using it | `subsystem.subsystemDescriptor.<supports...>` (for example `ARCameraManager.descriptor.supportsCameraImage`, `AROcclusionManager.descriptor.environmentDepthImageSupported`) |

---

## 4. `ARSession`, state types

Namespace `UnityEngine.XR.ARFoundation`, assembly `Unity.XR.ARFoundation`. File `AF/ARFoundation/Session/ARSession.cs`. `sealed class ARSession : SubsystemLifecycleManager<XRSessionSubsystem, ...>`, `[DisallowMultipleComponent]`, execution order `int.MinValue` (`:21-25`).

| Member | Signature | Line | Gotcha |
|---|---|---|---|
| `state` | `static ARSessionState state { get; }` | 127 | Private setter. Set from `Update` (`:404-413`): tracking `None` or `Limited` gives `SessionInitializing`, `Tracking` gives `SessionTracking`. There is **no state for "tracking lost"**: Limited maps back to `SessionInitializing` |
| `stateChanged` | `static event Action<ARSessionStateChangedEventArgs>` (`.state`) | 122 | Fires only when `state` changes |
| `notTrackingReason` | `static NotTrackingReason notTrackingReason { get; }` | 34 | **Stale snapshot.** Updated only inside the `state` setter (`:137`, `UpdateNotTrackingReason` `:460`), which returns early when the state is unchanged (`:130`). A reason change while staying in `SessionInitializing` is not reflected. Read live: `session.subsystem.notTrackingReason` (`AF/ARSubsystems/Session/XRSessionSubsystem.cs:264`) and `.trackingState` (`:235`) |
| `subsystem`, `descriptor` | `XRSessionSubsystem subsystem { get; }`, `XRSessionSubsystemDescriptor descriptor` | `SubsystemLifecycleManager.cs:26,32` | Null until the subsystem is found. A manager polls for **5 s** each frame, then warns (`:18,76-97`) |
| `Reset()` | `void Reset()` | 151 | Destroys all trackables (anchors included) and resets device tracking (the session pose returns to its origin); sets `state = SessionInitializing` |
| `matchFrameRateRequested` | `bool { get; set; }` | 86 | Default `true` (serialized, `:59`). With ARKit (`supportsMatchFrameRate = true`, `ARKitSessionSubsystem.cs:403`) it blocks `Update` until the next AR frame and sets `Application.targetFrameRate = subsystem.frameRate` and `QualitySettings.vSyncCount = 0` every frame (`:385-396`). Restored in `OnDisable` (`:440-445`, the XML doc says otherwise; the code restores). Set `false` if the ATLAS frame loop owns the frame rate |
| `matchFrameRateEnabled` | `bool { get; }` | 65 | |
| `frameRate` | `int? { get; }` | 117 | `null` when unsupported |
| `attemptUpdate` | `bool { get; set; }` | 52 | Irrelevant to ARKit (`supportsInstall = false`, `AK/ARKitSessionSubsystem.cs:402`): an unsupported device goes straight to `Unsupported`, never `NeedsInstall` |
| `requestedTrackingMode`, `currentTrackingMode` | `TrackingMode` (`DontCare`, `RotationOnly`, `PositionAndRotation`) | 98,112 | Default `PositionAndRotation` |
| `CheckAvailability()`, `Install()` | `static IEnumerator` | 218,278 | `OnEnable` runs the check itself (`:350-371`) |

`ARSessionState`: `None, Unsupported, CheckingAvailability, NeedsInstall, Installing, Ready, SessionInitializing, SessionTracking` (`AF/ARFoundation/Session/ARSessionState.cs:12-53`).
`TrackingState`: `None, Limited, Tracking` (`AF/ARSubsystems/Common/TrackingState.cs:12-22`).
`NotTrackingReason`: `None, Initializing, Relocalizing, InsufficientLight, InsufficientFeatures, ExcessiveMotion, Unsupported, CameraUnavailable` (`AF/ARSubsystems/Session/NotTrackingReason.cs:12-47`).

Pausing: disabling the `ARSession` component calls `subsystem.Stop()`, which for ARKit is `UnityARKit_Session_Pause` (`AK/ARKitSessionSubsystem.cs:316`); enabling resumes (`:314`). Do not use it as a camera-off switch. `DefaultARKitSessionDelegate` resets a failed session up to `maxRetryCount = 5` times (`AK/DefaultARKitSessionDelegate.cs`).

```csharp
public static void Session(ARSession s)
{
    s.matchFrameRateRequested = false;
    ARSession.stateChanged += e => { ARSessionState st = e.state; };
    XRSessionSubsystem sub = s.subsystem;                       // live values
    if (sub != null) { TrackingState t = sub.trackingState; NotTrackingReason r = sub.notTrackingReason; }
    bool tracking = ARSession.state == ARSessionState.SessionTracking;
}
```

---

## 5. `XROrigin`

Namespace `Unity.XR.CoreUtils`, assembly `Unity.XR.CoreUtils`. File `CU/XROrigin.cs`. `[DisallowMultipleComponent]`. `ARSessionOrigin` is an obsolete subclass (`AF/ARFoundation/Session/ARSessionOrigin.cs:55-57`).

### 5.1 Members

| Member | Signature | Line | Gotcha |
|---|---|---|---|
| `Camera` | `Camera Camera { get; set; }` | 42 | `Awake` falls back to `Camera.main` and warns (`:531-538`). It warns when the camera has no `TrackedPoseDriver` (`:549-582`) |
| `TrackablesParent` | `Transform TrackablesParent { get; private set; }` | 54 | Created in `Awake` as child `"Trackables"` (`:542-545`); null before `Awake`. **On every `Application.onBeforeRender` its world pose is set to the pose of the camera's parent** (`:586-613`), not of the `XROrigin` object. If the camera has no parent the pose is `Pose.identity` and trackables ignore the origin entirely. Keep the standard chain `XR Origin > Camera Offset > Main Camera` |
| `TrackablesParentTransformChanged` | `event Action<ARTrackablesParentTransformChangedEventArgs>` | 61 | On change the spawner re-places every trackable (`AF/ARFoundation/Common/TrackingSubsystem/TrackableSpawner/TrackableSpawner.cs:359-372`), so moving the origin moves planes, anchors and meshes consistently within the same frame |
| `Origin` | `GameObject Origin { get; set; }` | 128 | The "rig base". Defaults to the `XROrigin` GameObject (`:628-629`). Move this one |
| `CameraFloorOffsetObject` | `GameObject { get; set; }` | 141 | Parent of the camera. If unassigned, `Awake` uses the `XROrigin` GameObject itself and warns (`:525-529`), then any height offset would move the whole origin |
| `RequestedTrackingOriginMode` | `TrackingOriginMode` (`NotSpecified, Device, Floor, Unbounded`) | 159 | Default `NotSpecified` |
| `CameraYOffset` | `float { get; set; }` | 176 | Default **1.1176 m** (`:119,170`). Applied to `CameraFloorOffsetObject.localPosition.y` only when the `XRInputSubsystem` reports `Device` or `Unbounded` (`:223-257`); for those modes `SetupCamera` also calls `TryRecenter()` (`:355-358`). ARKit and Simulation both create an input subsystem (`AK/ARKitLoader.cs:133`, `AF/Simulation/SimulationLoader.cs:33`). **DEVICE:** confirm `Camera Offset` local Y stays 0; set `CameraYOffset = 0` to be safe |
| `RotateAroundCameraUsingOriginUp(float)`, `RotateAroundCameraPosition(Vector3,float)` | `bool` | 388,400 | Rotate `Origin` about the camera position |
| `MatchOriginUp(Vector3)` | `bool` | 419 | Uses the `XROrigin` component's `transform.rotation` in the product, not `Origin`'s (`:430`); only equal when `Origin` is the same object |
| `MatchOriginUpCameraForward(Vector3,Vector3)`, `MatchOriginUpOriginForward(Vector3,Vector3)` | `bool` | 452,485 | |
| `MoveCameraToWorldLocation(Vector3)` | `bool` | 506 | Translates `Origin` so the camera lands at the given world point; no rotation |
| `OriginInCameraSpacePos`, `CameraInOriginSpacePos`, `CameraInOriginSpaceHeight` | `Vector3`, `Vector3`, `float` | 197-207 | |

### 5.2 Components that need an `XROrigin`

| Component | Rule | Source |
|---|---|---|
| `ARPlaneManager`, `ARAnchorManager`, `ARBoundingBoxManager`, `ARRaycastManager` (and every `ARTrackableManager<...>`) | `[RequireComponent(typeof(XROrigin))]`: **same GameObject** | `AF/ARFoundation/Common/TrackingSubsystem/ARTrackableManager.cs:23`, each manager's attribute line |
| `ARMeshManager` | Must sit on a **descendant** of the `XROrigin` object (search starts at `transform.parent`) | `ARMeshManager.cs:275-290,324-330` |
| `ARCameraManager`, `AROcclusionManager`, `ARCameraBackground` | On the camera GameObject; `[RequireComponent(typeof(Camera))]`; `ARCameraBackground` also requires `ARCameraManager` | `ARCameraManager.cs:18`, `AROcclusionManager.cs:17`, `ARCameraBackground.cs:26-27` |
| `ARSession` | none. The AR Foundation menu creates `AR Session` with `ARSession` + `ARInputManager` | `AFE/ARFoundation/SceneUtils.cs:37-38` |

### 5.3 Putting an anchor at the world origin by moving the origin

Derived from the `TrackablesParent` behaviour above (not an API in the packages): the anchor's world pose is `P(cameraParent) * A(session)`. Applying a rigid transform `M` to the whole rig scales nothing and moves the camera, planes, meshes and anchors together, so choose `M = inverse(anchorWorldPose)`.

```csharp
using Unity.XR.CoreUtils;

public static void PlaceAnchorAtWorldOrigin(XROrigin xr, Transform anchor, bool yawOnly)
{
    Transform o = xr.Origin.transform;
    Quaternion qa = anchor.rotation;
    if (yawOnly)                                   // keep Y up, align heading only
    {
        Vector3 f = Vector3.ProjectOnPlane(qa * Vector3.forward, Vector3.up);
        qa = f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
    }
    Quaternion qInv = Quaternion.Inverse(qa);
    o.SetPositionAndRotation(qInv * (o.position - anchor.position), qInv * o.rotation);
}
```

Gotchas: run it once; ARKit may later move the anchor relative to the session (drift, relocalisation) and it will leave the origin. Re-run after relocalisation or parent content to the anchor. `Origin` must be an ancestor of the camera (the default). `ARRaycastHit.distance` is wrong once the origin is not at identity (section 12.2).

---

## 6. `TrackedPoseDriver` and `ARInputManager`

`UnityEngine.InputSystem.XR.TrackedPoseDriver`, assembly `Unity.InputSystem`, file `IS/Runtime/Plugins/XR/TrackedPoseDriver.cs:22`. The legacy `UnityEngine.SpatialTracking.TrackedPoseDriver` (assembly `UnityEngine.SpatialTracking`) has the same simple name; fully qualify the type.

| Member | Signature | Line |
|---|---|---|
| `trackingType` | `TrackingType { get; set; }`: `RotationAndPosition`, `RotationOnly`, `PositionOnly` | 79 (enum 28-44) |
| `updateType` | `UpdateType { get; set; }`: `UpdateAndBeforeRender` (default), `Update`, `BeforeRender` | 126 (enum 90-117, default 120) |
| `ignoreTrackingState` | `bool { get; set; }` | 139 |
| `positionInput`, `rotationInput`, `trackingStateInput` | `InputActionProperty { get; set; }` (Vector3, Quaternion, Integer) | 152,174,218 |

Gotchas: it writes the **local** transform of its GameObject (`:579-600`). `BeforeRender` mode may not run when no XR device uses before-render timing (`:106-116` remarks). `HandheldARInputDevice` exposes only `devicePosition` and `deviceRotation` (`AF/ARSubsystems/Input/HandheldARInputDevice.cs`), so there is no tracking-state control: the driver infers validity from resolved controls (`:495-515`) and **keeps applying the pose while ARKit tracking is `Limited`**. Gate content on `ARSession.state` or `subsystem.trackingState`. The XR Origin factory configures it exactly like this (`AFE/ARFoundation/XROriginCreateUtil.cs:73-80`):

```csharp
var tpd = cam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
var pos = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
pos.AddBinding("<HandheldARInputDevice>/devicePosition");
var rot = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
rot.AddBinding("<HandheldARInputDevice>/deviceRotation");
tpd.positionInput = new InputActionProperty(pos);
tpd.rotationInput = new InputActionProperty(rot);
```

`ARInputManager` (`AF/ARFoundation/Input/ARInputManager.cs`) starts the `XRInputSubsystem` in `OnEnable` (`:23-29`). Without an enabled one, `HandheldARInputDevice` is not fed. Keep it on the `AR Session` object as the package's own factory does. ARKit registers the `HandheldARInputDevice` layout for product `(ARKit)` (`AK/Input/InputLayoutLoader.cs:39-43`), Simulation for `(XR Simulation)` (`AF/Simulation/Input/InputLayoutLoader.cs:34-38`).

---

## 7. `ARCameraManager`, `XRCameraConfiguration`, camera images

Namespace `UnityEngine.XR.ARFoundation` (assembly `Unity.XR.ARFoundation`); data types in `UnityEngine.XR.ARSubsystems` (assembly `Unity.XR.ARSubsystems`). File `AF/ARFoundation/Camera/ARCameraManager.cs`. `sealed`, `[RequireComponent(typeof(Camera))]`.

| Member | Signature | Line | Gotcha |
|---|---|---|---|
| `GetConfigurations` | `NativeArray<XRCameraConfiguration> GetConfigurations(Allocator allocator)` | 277 | Returns an **empty** array while `subsystem == null` (`:278`). Caller disposes. Wait for `cameraManager.subsystem != null` |
| `currentConfiguration` | `XRCameraConfiguration? { get; set; }` | 292 | Setter is silently ignored while `subsystem == null` (`:297-298`). Setting `null` throws `ArgumentNullException`; a config not in the list throws `ArgumentException`; an invalid ARKit session or an OS without support throws `InvalidOperationException` (`AK/Camera/ARKitCameraSubsystem.cs:410-435`). Simulation accepts anything (`SimulationCameraSubsystem.cs:59-69`) |
| `frameReceived` | `event Action<ARCameraFrameEventArgs>` | 36 | Raised from `Update` (`:343-369`) only if a frame and texture descriptors exist. `args.textures` and `args.propertyNameIds` are **static shared lists** (`:25-26,446-458`): do not keep them |
| `TryGetIntrinsics` | `bool TryGetIntrinsics(out XRCameraIntrinsics)` | 261 | Per frame. Fields are pixels: `focalLength`, `principalPoint` (from top-left), `resolution` (`AF/ARSubsystems/Camera/XRCameraIntrinsics.cs:29-52`). **Simulation returns `true` with an all-zero struct** (`SimulationCameraSubsystem.cs:151,489-493`): test `resolution.x > 0` |
| `TryAcquireLatestCpuImage` | `bool TryAcquireLatestCpuImage(out XRCpuImage)` | 310 | Dispose it. The CPU image resolution follows the current configuration (`XRCameraConfiguration.cs:7-13` remarks) |
| `permissionGranted` | `bool` | 213 | iOS Camera Usage Description is a build error if empty (`AK/../Editor/ARKitBuildProcessor.cs:135-137`) |
| `requestedBackgroundRenderingMode` | `CameraBackgroundRenderingMode` (`Any`, `BeforeOpaques`, `AfterOpaques`) | 191 | |

`XRCameraConfiguration` (`AF/ARSubsystems/Camera/XRCameraConfiguration.cs`): `int width`, `int height`, `Vector2Int resolution`, `int? framerate` (null when the platform gives none; zero is stored as null), `Supported depthSensorSupported` (`Unknown/Unsupported/Supported`), `IntPtr nativeConfigurationHandle` (ARKit: pointer to the `ARVideoFormat`, `AKD/arkit-camera.md`). Struct equality includes all four.

### 7.1 Lowest-resolution 60 Hz format

```csharp
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;

public static bool TrySelectLowestRes60Hz(ARCameraManager cam)
{
    using (NativeArray<XRCameraConfiguration> configs = cam.GetConfigurations(Allocator.Temp))
    {
        bool found = false; XRCameraConfiguration best = default; int bestPixels = int.MaxValue;
        for (int i = 0; i < configs.Length; i++)
        {
            XRCameraConfiguration c = configs[i];
            if (c.framerate != 60) continue;                    // int? != int
            int px = c.width * c.height;
            if (px < bestPixels) { best = c; bestPixels = px; found = true; }
        }
        if (!found) return false;                               // keep the default configuration
        cam.currentConfiguration = best;
        return true;
    }
}
```

Off device the list has **one** entry with `framerate == null` (`SimulationCameraSubsystem.cs:150,213-219`), so this returns `false` in Play Mode, which is the correct "do nothing". Which ARKit formats exist, their order, and whether switching restarts tracking are native (**DEVICE**). A change of `ARConfiguration` raises `ARKitSessionDelegate.OnConfigurationChanged` (`AK/ARKitSessionDelegate.cs:31-37`); whether a camera-format change does is native. With `matchFrameRateRequested = true` the Unity frame rate then follows `subsystem.frameRate` (section 4).

### 7.2 CPU image, in the sources' own terms

ARKit camera image format is `XRCpuImage.Format.IosYpCbCr420_8BiPlanarFullRange` and converts to `Alpha8, R8, RGB24, RGBA32, ARGB32, BGRA32` (`AK/Camera/ARKitCpuImageApi.cs:167-195`). Simulation's image is a colour format and converts only to `RGBA32, ARGB32, BGRA32, RGB24`; **`R8` is not supported there** (`AF/Simulation/Subsystems/CpuImage/SimulationXRCpuImageApi.cs:24-40,248-251`). Always check `image.FormatSupported(...)` first.

```csharp
public static bool TryGrayscale(ARCameraManager cam, NativeArray<byte> dst, out Vector2Int size)
{
    size = default;
    if (!cam.TryAcquireLatestCpuImage(out XRCpuImage image)) return false;
    using (image)                                               // always dispose
    {
        var p = new XRCpuImage.ConversionParams(image, TextureFormat.R8);
        if (!image.FormatSupported(p.outputFormat)) return false;
        if (dst.Length < image.GetConvertedDataSize(p)) return false;
        image.Convert(p, dst);                                  // NativeArray<byte> converts to NativeSlice<byte>
        size = new Vector2Int(image.width, image.height);
        return true;
    }
}
```

### 7.3 Does it work with the `Camera` component disabled or rendering nothing?

* Managed path: `ARCameraManager.Update` reads only `Camera.nearClipPlane`, `farClipPlane` and `Screen.*` (`:354-361`); `TryGetIntrinsics` and `TryAcquireLatestCpuImage` go straight to the subsystem (`:261-269,310-318`). Nothing consults `Camera.enabled`, culling, or a render having happened. Requirements: the GameObject is active, the `ARCameraManager` is enabled, a `Camera` component exists, camera permission is granted, the `ARSession` runs.
* Frames come from the native ARKit session, not from rendering (**DEVICE** to confirm, the provider is closed source).
* Simulation renders its own hidden `SimulationCamera` into a render texture sized from `xrOrigin.Camera.scaledPixelWidth/Height` (`CameraTextureProvider.cs:110`) and needs `XROrigin.Camera` non-null (`SimulationCameraSubsystem.cs:123-137`); behaviour with a disabled XR camera is untested here (**DEVICE**, Editor).
* Dispose images promptly: holding many `XRCpuImage` (including high-resolution ones) can stop ARKit producing frames (`AKD/arkit-camera.md` "High resolution CPU image"; `AFD/features/camera/image-capture.md`). An undisposed `AsyncConversion` leaks until the camera subsystem is destroyed.

---

## 8. Camera background, occlusion, depth, `XRCpuImage`

### 8.1 `ARCameraBackground`: no background and a pure VR view

`AF/ARFoundation/Camera/ARCameraBackground.cs`, `[RequireComponent(typeof(Camera))]`, `[RequireComponent(typeof(ARCameraManager))]` (`:26-27`).

| Behaviour | Source |
|---|---|
| While enabled, after the first camera frame it sets `Camera.clearFlags` to `Nothing` (`Depth` for after-opaques) and overwrites the projection matrix and FOV from the AR frame every frame | `:275,303-308,497-503` |
| `OnDisable` restores the saved clear flags and FOV and calls `camera.ResetProjectionMatrix()`; it unsubscribes from frames | `:248-261,285-298` |
| The URP `ARBackgroundRendererFeature` only enqueues its pass when the camera has an `ARCameraBackground` that is `backgroundRenderingEnabled` (true only after a first frame while enabled), so the feature can stay in the renderer asset at no cost | `AF/ARFoundation/Camera/ARBackgroundRendererFeature.cs:45-60` |
| Occlusion shader keywords reach the background material only through `ARCameraBackground` (or `ARShaderOcclusion`) | `:536-557` |

Recipe for a VR-only view that still reads AR data:

```csharp
var bg = cam.GetComponent<ARCameraBackground>();
if (bg != null) bg.enabled = false;               // no video, no AR projection override
cam.clearFlags = CameraClearFlags.SolidColor;     // or Skybox; the XR Origin factory uses Color/black
cam.backgroundColor = Color.black;
// FOV is now Unity's own. For the real camera's frustum, take ARCameraFrameEventArgs.projectionMatrix
// (Matrix4x4?) in ARCameraManager.frameReceived and assign camera.projectionMatrix yourself.
```

Keep `ARCameraManager` enabled for `frameReceived`, intrinsics and CPU images, and `AROcclusionManager` enabled for depth images; both work without the background component. `ARCameraManager` still builds its per-frame texture wrappers while the background is off (`:366-368`). In Simulation, disabling the background also removes the simulated environment picture (the environment is only visible through that background). Do not remove `ARCameraManager` while an `ARCameraBackground` exists.

### 8.2 `AROcclusionManager`

`AF/ARFoundation/Occlusion/AROcclusionManager.cs`, `sealed`, `[RequireComponent(typeof(Camera))]` (`:17`). Setters push to the subsystem only when `enabled` and the descriptor reports the feature `Supported` (`:52-58,103-106,133-140`); otherwise the value is only stored. `Update()` re-pushes the serialized fields every frame (`:453-459`), so a value set through the property sticks.

| Member | Signature | Line | Notes |
|---|---|---|---|
| `requestedEnvironmentDepthMode` | `EnvironmentDepthMode { get; set; }` | 98 | **Component default `Fastest`** (`:120`). Values `Disabled=0, Fastest=1, Medium=2, Best=3` (`AF/ARSubsystems/Occlusion/EnvironmentDepthMode.cs:13-31`). `Fastest` = "fastest resolution; no smoothing or other post-processing". ARKit only forwards the enum and sets `Feature.EnvironmentDepth` when not `Disabled` (`AK/Occlusion/ARKitOcclusionSubsystem.cs:122-130`); needs a LiDAR device on iOS 14+ (`AKD/arkit-occlusion.md`) |
| `currentEnvironmentDepthMode` | `EnvironmentDepthMode { get; }` | 112 | Simulation never overrides it, so it **always reads `Disabled`** there (`XROcclusionSubsystem.cs:453`) although a depth texture is produced |
| `environmentDepthTemporalSmoothingRequested` | `bool { get; set; }` | 130 | **Component default `true`** (`:123`). `environmentDepthTemporalSmoothingEnabled` (`:146`) is the effective value. ARKit support is a native check (`ARKitOcclusionSubsystem.cs:35`); Simulation: unsupported |
| `requestedHumanStencilMode` | `HumanSegmentationStencilMode`: `Disabled, Fastest, Medium, Best` | 45 | Default `Disabled`. ARKit: iOS 13+, A12 or newer. Simulation: unsupported |
| `requestedHumanDepthMode` | `HumanSegmentationDepthMode`: `Disabled, Fastest, Best` | 72 | Default `Disabled`. Same support |
| `requestedOcclusionPreferenceMode` | `OcclusionPreferenceMode`: `PreferEnvironmentOcclusion, PreferHumanOcclusion, NoOcclusion` | 151 | |
| `TryAcquireEnvironmentDepthCpuImage` | `bool (out XRCpuImage)` | 280 | Returns the **smoothed** image when smoothing is enabled, else raw. Guarded by `descriptor.environmentDepthImageSupported` |
| `TryAcquireRawEnvironmentDepthCpuImage`, `TryAcquireSmoothedEnvironmentDepthCpuImage` | `bool (out XRCpuImage)` | 301,325 | Deterministic variants. Smoothed only succeeds if smoothing was requested (`AKD/arkit-occlusion.md`) |
| `TryAcquireEnvironmentDepthConfidenceCpuImage` | `bool (out XRCpuImage)` | 263 | ARKit only, same LiDAR gate (`ARKitOcclusionSubsystem.cs:34`). Format is native; the converter accepts `OneComponent8` to `R8/Alpha8` (`ARKitCpuImageApi.cs:192`), which is the expected confidence format (**DEVICE**) |
| `TryAcquireHumanStencilCpuImage`, `TryAcquireHumanDepthCpuImage` | `bool (out XRCpuImage)` | 228,245 | |
| `TryGetEnvironmentDepthTexture` | `bool (out Texture)` | 343 | GPU path. This is the only depth path Simulation implements |

Every CPU-image accessor returns `false` and a default image when unsupported; it does not throw (`AROcclusionManager.cs:228-333`, `XROcclusionSubsystem.cs:214-221`). **Simulation provides no CPU depth** (`environmentDepthCpuImageApi` is not overridden in `AF/Simulation/Subsystems/Occlusion/SimulationOcclusionSubsystem.cs`, base value `null` at `XROcclusionSubsystem.cs:653`).

```csharp
public static void ConfigureOcclusion(AROcclusionManager occ)
{
    occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
    occ.environmentDepthTemporalSmoothingRequested = false;     // raw depth, no temporal filter
    occ.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
    occ.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
}
```

### 8.3 `XRCpuImage`

Namespace `UnityEngine.XR.ARSubsystems`, assembly `Unity.XR.ARSubsystems`, `partial struct XRCpuImage : IDisposable` (`AF/ARSubsystems/Common/Textures/XRCpuImage/XRCpuImage.cs:19`).

| Member | Signature | Line | Rules |
|---|---|---|---|
| `width`, `height`, `dimensions`, `planeCount`, `format`, `timestamp`, `valid` | | 158-207 | |
| `GetPlane(int)` | `Plane GetPlane(int planeIndex)` | 247 | No copy: `Plane.data` is a `NativeArray<byte>` **view into native memory**, plus `rowStride` and `pixelStride` **in bytes** (`Plane.cs`). Throws `ArgumentOutOfRangeException` or `InvalidOperationException`. Under collections checks the array carries the image's safety handle (`:264`), so use after `Dispose` throws in the Editor and reads freed memory in a player |
| `FormatSupported(TextureFormat)` | `bool` | 236 | |
| `GetConvertedDataSize(ConversionParams)` | `int` | 326 | Throws if the format is unsupported or the size exceeds the image |
| `Convert` | `void Convert(ConversionParams, IntPtr dst, int len)` and `(ConversionParams, NativeSlice<byte>)` | 346,375 | Synchronous, into your buffer. Throws `ArgumentException` when `len` is too small, `InvalidOperationException` on failure. `ConversionParams(image, format, Transformation = None)` selects the whole image; `Transformation` is `MirrorX \| MirrorY` flags |
| `ConvertAsync(ConversionParams)` | `AsyncConversion` | 393 | Safe to dispose the image before it completes. `AsyncConversion` must be disposed; its `GetData<T>()` view is valid only until then (`AsyncConversion.cs:67,93`). `status` is `Disposed, Pending, Processing, Ready, Failed`; helpers `IsDone()`, `IsError()` |
| `ConvertAsync(ConversionParams, Action<AsyncConversionStatus, ConversionParams, NativeArray<byte>>)` | `void` | 412 | The array is valid only during the callback |
| `Dispose()` | `void` | 507 | Idempotent (`m_NativeHandle == 0` returns). Invalidates every `Plane` taken from the image. `XRCpuImage` is a struct: copies share the native handle, dispose exactly one |

`XRCpuImage.Format`: `Unknown, AndroidYuv420_888, IosYpCbCr420_8BiPlanarFullRange, OneComponent8, DepthFloat32, DepthUint16, OneComponent32, ARGB32, RGBA32, BGRA32, RGB24` (`CpuImageFormat.cs:10-90`).

### 8.4 Reading environment depth, metres, without allocation

ARKit's depth converter accepts only `DepthFloat32` to `TextureFormat.RFloat` (`ARKitCpuImageApi.cs:193`), so the CPU image is binary32 floats, which `Format.DepthFloat32` documents as "the depth (distance to an object) in meters" (`CpuImageFormat.cs:55-60`; whether that is range or camera-axis depth is not stated, **DEVICE**). **The 256 x 192 size is not in any managed source** (it is native); size buffers from `image.width/height` on first use and log a warning if they differ (**DEVICE**). The depth image has its own dimensions, and `ConversionParams` offers only `MirrorX`/`MirrorY`, no rotation; its orientation relative to the screen and its field of view against the camera image are native (**DEVICE**). Mapping a depth pixel to a camera ray would use `XRCameraIntrinsics` scaled by `depth.width / intrinsics.resolution.x` once that is verified.

```csharp
// asmdef: allowUnsafeCode = true. No managed allocation, no copy.
public static unsafe bool TryReadDepthMeters(AROcclusionManager occ, int x, int y, out float meters)
{
    meters = 0f;
    if (!occ.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) return false;
    using (image)
    {
        if (image.format != XRCpuImage.Format.DepthFloat32 || image.planeCount < 1) return false;
        if ((uint)x >= (uint)image.width || (uint)y >= (uint)image.height) return false;
        XRCpuImage.Plane plane = image.GetPlane(0);
        byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(plane.data);
        meters = *(float*)(basePtr + y * plane.rowStride + x * plane.pixelStride);   // strides are bytes
        return true;
    }
}

// Whole frame into a preallocated NativeArray<float>, still no managed allocation.
public static unsafe bool TryCopyDepth(AROcclusionManager occ, NativeArray<float> dst, out Vector2Int size)
{
    size = default;
    if (!occ.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) return false;
    using (image)
    {
        var p = new XRCpuImage.ConversionParams(image, TextureFormat.RFloat);
        int bytes = image.GetConvertedDataSize(p);
        if (bytes > dst.Length * sizeof(float)) return false;
        image.Convert(p, new IntPtr(NativeArrayUnsafeUtility.GetUnsafePtr(dst)), bytes);
        size = new Vector2Int(image.width, image.height);
        return true;
    }
}
```

`using Unity.Collections.LowLevel.Unsafe;` for `NativeArrayUnsafeUtility`. `plane.data.Reinterpret<byte, float>()` (`Unity.Collections.NativeArrayExtensions`) also works if `rowStride == width * 4`; the pointer form does not assume that. In Simulation both calls return `false`; supply a stand-in (for example a raycast against your own proxy geometry).

---

## 9. `ARMeshManager`

Namespace `UnityEngine.XR.ARFoundation`, assembly `Unity.XR.ARFoundation`. `public class ARMeshManager : MonoBehaviour` (`AF/ARFoundation/Meshing/ARMeshManager.cs:30`). It is **not** an `ARTrackableManager`.

### 9.1 Hierarchy and lifecycle

| Rule | Source |
|---|---|
| Needs an `XROrigin` on an **ancestor**, not on itself (search starts at `transform.parent`). Otherwise `OnEnable` sets `enabled = false` and throws `InvalidOperationException`. In the Editor `OnValidate` shows a dialog and destroys the component | `:275-290,292-316,324-330` |
| `Reset()` sets `localScale = 10` when an origin exists | `:292-297` |
| Bounding volume = the manager's own `localPosition` and `localScale`, re-sent when `transform.hasChanged` (`SetBoundingVolume(origin, extents)`, an AABB; units are provider defined) | `:318-322,383-384`, `ENG/UnityEngine.XRModule.xml` `M:...XRMeshSubsystem.SetBoundingVolume` |
| Mesh objects are instantiated **under `XROrigin.TrackablesParent`**, not under the manager, inactive until generated | `:595,599,500` |
| `meshPrefab == null`: generation never runs, so no `MeshFilter`s and no events | `:388` |
| Enable / disable at run time = toggle `enabled`. `OnEnable` pushes density and bounding volume then `Start()` (`:334-341`); `OnDisable` calls `Stop()` (`:579-583`). A failed subsystem lookup logs a warning and disables the component (`:343-346,365`). Disabling does **not** remove meshes | |
| `DestroyAllMeshes()` clears the queues and destroys the mesh GameObjects, but does not notify the provider: a mesh returns only when the provider next reports it added or updated (derived from `UpdateMeshInfos`, `:533-566`). Full reset: `enabled = false; DestroyAllMeshes(); enabled = true;` | `:225-235` |

### 9.2 Members

| Member | Signature | Line | Gotcha |
|---|---|---|---|
| `meshPrefab` | `MeshFilter { get; set; }` | 66 | Colliders are generated asynchronously |
| `density` | `float { get; set; }` (0..1, throws outside) | 82 | Default 0.5. **ARKit does not support density** (`AKD/arkit-meshing.md` feature table) |
| `normals`, `tangents`, `textureCoordinates`, `colors` | `bool` | 104-146 | ARKit supports normals only. With `normals` the manager calls `RecalculateNormals` when the provider gave none (`:398-419`) |
| `concurrentQueueSize` | `int` | 192 | Default 4 |
| `meshInfosChanged` | `UnityEvent<ARMeshInfosChangedEventArgs>` (public field) | 207 | `args.added`, `.updated`, `.removed`: `ReadOnlyList<MeshUpdateInfo>`; `MeshUpdateInfo` has `TrackableId id` and `MeshFilter meshFilter` (`ARMeshInfosChangedEventArgs.cs`). Fires after generation completes, not when the provider first reports. Lists are the manager's own and are cleared right after the call |
| `meshesChanged` | `[Obsolete] event Action<ARMeshesChangedEventArgs>` | 201 | Allocates three lists per call (`:425-427`); do not use |
| `subsystem` | `XRMeshSubsystem { get; }` | 213 | Needed for the ARKit extension methods |
| `meshes` | `IList<MeshFilter> { get; }` | 220 | |

### 9.3 Subscribing

```csharp
meshManager.meshInfosChanged.AddListener(OnMeshInfos);
static void OnMeshInfos(ARMeshInfosChangedEventArgs a)      // needs Unity.XR.CoreUtils reference
{
    foreach (MeshUpdateInfo i in a.added)   { TrackableId id = i.id; MeshFilter f = i.meshFilter; }
    foreach (MeshUpdateInfo i in a.updated) { }
    foreach (MeshUpdateInfo i in a.removed) { }              // the mesh GameObject is destroyed right after this callback
}
```

### 9.4 ARKit classification (the only route on 6000.3)

`UnityEngine.XR.ARKit.ARKitMeshSubsystemExtensions` (assembly `Unity.XR.ARKit`, `AK/ARKitMeshSubsystemExtensions.cs:14`) and `ARMeshClassification` are `[Obsolete(..., false)]` since ARKit 6.3: a warning, not an error; use `#pragma warning disable CS0618`. The documented replacement is the `XRMeshSubsystem` classification API that exists only from Unity 6000.4.

| Member | Signature | Line | Notes |
|---|---|---|---|
| `GetClassificationEnabled`, `SetClassificationEnabled` | `static bool Get...(this XRMeshSubsystem)`, `static void Set...(this XRMeshSubsystem, bool)` | 160,170 | Native toggle |
| `GetFaceClassifications` | `static NativeArray<ARMeshClassification> (this XRMeshSubsystem, TrackableId meshId, Allocator)` | 64 | One value per **face**, raw ARKit enum |
| `GetMeshClassifications` | `static NativeArray<uint> (this XRMeshSubsystem, TrackableId meshId, Allocator)` | 99 | One `uint` per face, already mapped to `XRMeshClassification` |

**The two enums use different numbers.** `ARMeshClassification`: `None=0, Wall=1, Floor=2, Ceiling=3, Table=4, Seat=5, Window=6, Door=7` (`AK/ARMeshClassification.cs:15-50`). `XRMeshClassification` (`UnityEngine.XR.ARSubsystems`): `Unknown=0, Other=1, Floor=2, Ceiling=3, Wall=4, Table=5, Seat=6, Window=7, Door=8` (`AF/ARSubsystems/Meshing/XRMeshClassification.cs:11-51`). Never cast one to the other. Both calls hit native code: guard with `#if UNITY_IOS && !UNITY_EDITOR` (Simulation has `SimulatedMeshClassification` but no readable API in this version).

```csharp
#if UNITY_IOS && !UNITY_EDITOR
#pragma warning disable CS0618
using (NativeArray<uint> faces = meshManager.subsystem.GetMeshClassifications(id, Allocator.Temp))
{ for (int i = 0; i < faces.Length; i++) { var c = (XRMeshClassification)faces[i]; } }
#pragma warning restore CS0618
#endif
```

Call `SetClassificationEnabled(true)` once the subsystem exists. Check `faces.Length` against the mesh triangle count before indexing (they are read at different moments).

---

## 10. `ARPlaneManager`, `ARPlane`

Namespace `UnityEngine.XR.ARFoundation`, assembly `Unity.XR.ARFoundation`. `sealed class ARPlaneManager : ARTrackableManager<XRPlaneSubsystem, ..., BoundedPlane, ARPlane>, IRaycaster`, `[RequireComponent(typeof(XROrigin))]` (`AF/ARFoundation/Planes/ARPlaneManager.cs:19-28`).

| Member | Signature | Line | Gotcha |
|---|---|---|---|
| `requestedDetectionMode` | `PlaneDetectionMode { get; set; }` | 51 | Default `(PlaneDetectionMode)(-1)` = all bits (`:46`). Pushed only when `enabled` and the subsystem exists (`:55-58`) and at start (`:219-222`). `PlaneDetectionMode`: `None=0, Horizontal=1, Vertical=2, NotAxisAligned=4` (flags). ARKit supports Horizontal, Vertical (iOS 11.3+), **not** NotAxisAligned (`AK/PlaneTracking/ARKitPlaneSubsystem.cs:267-269`). `None` is equivalent to `Stop()` on ARKit (`AKD/arkit-plane-detection.md`); the subsystem throws `NotSupportedException` if a mode other than `None` is set where planes are unsupported |
| `currentDetectionMode` | `PlaneDetectionMode { get; }` | 67 | |
| `planePrefab` | `GameObject { get; set; }` | 38 | Null = bare object carrying only `ARPlane` (no mesh) |
| `GetPlane(TrackableId)` | `ARPlane` | 80 | |
| `trackables` | `TrackableCollection<ARPlane>` | `ARTrackableManager.cs:79` | Struct enumerator, no allocation |
| `Raycast(Ray, TrackableType, Allocator)` | `NativeArray<XRRaycastHit>` | 92 | Managed fallback used by `ARRaycastManager` when the provider has no world raycast. Registers itself as a fallback raycaster in `OnEnable` even if the subsystem is not yet there (`:270`, CHANGELOG 6.5.0-pre.3) |

`ARPlane` (`AF/ARFoundation/Planes/ARPlane.cs:23`), a `MonoBehaviour` whose GameObject is `"ARPlane <id>"` under `TrackablesParent`:

| Member | Type | Line | Note |
|---|---|---|---|
| `classifications` | `PlaneClassifications` (flags) | 80 | `None=0, Ceiling=1<<0, DoorFrame=1<<1, Floor=1<<2, WallArt=1<<3, WallFace=1<<4, WindowFrame=1<<5, Couch=1<<6, Seat=1<<7, SeatOfAnyType=Couch\|Seat, Table=1<<8, InvisibleWallFace=1<<9, InnerWallFace=1<<10, Other=1u<<31`. **ARKit assigns exactly one**, mapped: table to Table, seat to Seat, floor to Floor, ceiling to Ceiling, wall to WallFace, door to DoorFrame, window to WindowFrame (`AKD/arkit-plane-detection.md`). Supported flag is native (`ARKitPlaneSubsystem.cs:271`); Simulation sets none |
| `alignment` | `PlaneAlignment`: `None=0, HorizontalUp=100, HorizontalDown=101, Vertical=200, NotAxisAligned=300` | 61 | `HorizontalUp` is every upward horizontal surface (floor, table, seat); it is the only floor discriminator in Simulation, which sets no classification |
| `boundary` | `NativeArray<Vector2>` | 111 | Plane-local, `(x, y)` maps to local `(x, 0, y)`; a view of a persistent copy (no allocation per read). If boundary vertices are unsupported it is a 4-point rectangle from `extents` (`:133-151`). ARKit and Simulation both supply real vertices (`ARKitPlaneSubsystem.cs:270`, `SimulationPlaneSubsystem.cs:421`) |
| `boundaryChanged` | `event Action<ARPlaneBoundaryChangedEventArgs>` (`.plane`) | 45 | Fires only when a vertex moves more than `vertexChangedThreshold` (property at 36) and only if subscribed (`:159`) |
| `normal`, `center`, `centerInPlaneSpace`, `extents` (half size), `size`, `infinitePlane`, `subsumedBy` | | 50-105 | ARKit never merges planes (`subsumedBy` stays null) |
| `pose`, `trackingState`, `trackableId` | | `ARTrackable.cs` | `pose` is **session space**; use `transform` for world |

---

## 11. `ARBoundingBoxManager`, `ARBoundingBox`

It exists in 6.5.0. Namespace `UnityEngine.XR.ARFoundation`, assembly `Unity.XR.ARFoundation`.

| Member | Signature | Source |
|---|---|---|
| `ARBoundingBoxManager` | `public class ... : ARTrackableManager<XRBoundingBoxSubsystem, ..., XRBoundingBox, ARBoundingBox>, IRaycaster`, `[RequireComponent(typeof(XROrigin))]`; no `AddComponentMenu` entry | `AF/ARFoundation/BoundingBoxes/ARBoundingBoxManager.cs:13-22` |
| `boundingBoxPrefab` | `GameObject { get; set; }` | `:33` |
| `TryGetBoundingBox(TrackableId, out ARBoundingBox)` | `bool` | `:61` |
| `trackablesChanged` | `UnityEvent<ARTrackablesChangedEventArgs<ARBoundingBox>>` | section 15 |
| `ARBoundingBox.size` | `Vector3`, full dimensions in metres | `ARBoundingBox.cs:20`, `XRBoundingBox.cs:24-26` |
| `ARBoundingBox.classifications` | `BoundingBoxClassifications` (flags) | `ARBoundingBox.cs:25` |
| pose | `ARBoundingBox.pose` is session space; **world pose = `transform.position/rotation`** | `XRBoundingBox.cs:19-21` |
| descriptor flag | `XRBoundingBoxSubsystemDescriptor.supportsClassifications` (plural on the descriptor, singular on its `Cinfo`) | `XRBoundingBoxSubsystemDescriptor.cs:20,71` |

`BoundingBoxClassifications` (flags, `uint`): `None=0, Couch=1<<0, Table=1<<1, Bed=1<<2, Lamp=1<<3, Plant=1<<4, Screen=1<<5, Storage=1<<6, Bathtub=1<<7, Chair=1<<8, Dishwasher=1<<9, Fireplace=1<<10, Oven=1<<11, Refrigerator=1<<12, Sink=1<<13, Stairs=1<<14, Stove=1<<15, Toilet=1<<16, WasherDryer=1<<17, Keyboard=1<<18, Mouse=1<<19, Laptop=1<<20, Other=1u<<31`.

**ARKit RoomPlan** (`AK/RoomPlanBoundingBoxSubsystem.cs`, class `RoomPlanBoundingBoxSubsystem : XRBoundingBoxSubsystem`):

* Needs **iOS 17.0+ and a LiDAR device** (`Api.AtLeast17_0()` gate at `:144`, `AKD/arkit-bounding-boxes.md`). The project's deployment target is 17.0.
* The subsystem id is `ARKit-RoomPlan-BoundingBox`; `supportsClassification = true` (`:147-153`).
* "Users must complete a room capture before retrieving the bounding boxes" (`AKD/arkit-bounding-boxes.md`). Drive it explicitly, via `boundingBoxManager.subsystem as RoomPlanBoundingBoxSubsystem`:
  `bool SetupRoomCapture()` (101), `void StartRoomCapture()` (109), `void StopRoomCapture()` (117), `bool IsRoomCapturing()` (126), `void GetRoomCaptureInstruction(out XRBoundingBoxInstructions)` (135). `XRBoundingBoxInstructions` (flags, `uint`): `None, Normal, MoveCloseToWall, MoveAwayFromWall, TurnOnLight, SlowDown, LowTexture`.
* ARKit supplies only: Bathtub, Bed, Chair, Couch, Dishwasher, Fireplace, Oven, Refrigerator, Screen, Sink, Stairs, Storage, Stove, Table, Toilet, WasherDryer (`AKD/arkit-bounding-boxes.md` mapping table). Keyboard, Lamp, Laptop, Mouse, Other, Plant are not produced.
* Off device every native call throws `NotSupportedException` (`:196-225`); the `as` cast yields null there.
* Raycasts: `ARRaycastManager` reaches boxes only through the managed fallback (`TrackableType.BoundingBox`, `ARRaycastManager.cs:293-298`).

```csharp
bm.trackablesChanged.AddListener(OnBoxes);
#if UNITY_EDITOR || UNITY_IOS
var rp = bm.subsystem as RoomPlanBoundingBoxSubsystem;        // null on Simulation and below iOS 17
if (rp != null && rp.SetupRoomCapture()) rp.StartRoomCapture();
#endif
static void OnBoxes(ARTrackablesChangedEventArgs<ARBoundingBox> a)
{
    foreach (ARBoundingBox b in a.added)
    {
        Vector3 size = b.size; BoundingBoxClassifications c = b.classifications;
        Pose world = new Pose(b.transform.position, b.transform.rotation);
    }
}
```

---

## 12. `ARRaycastManager`

`sealed ... ARRaycastManager : ARTrackableManager<XRRaycastSubsystem, ..., XRRaycast, ARRaycast>`, `[RequireComponent(typeof(XROrigin))]` (`AF/ARFoundation/Raycasts/ARRaycastManager.cs:19-24`).

| Member | Signature | Line |
|---|---|---|
| `Raycast` (screen) | `bool Raycast(Vector2 screenPoint, List<ARRaycastHit> hitResults, TrackableType trackableTypes = TrackableType.AllTypes)` | 58 |
| `Raycast` (world ray) | `bool Raycast(Ray ray, List<ARRaycastHit> hitResults, TrackableType trackableTypes = TrackableType.AllTypes)` | 83 |
| `AddRaycast`, `RemoveRaycast` | tracked raycasts (ARKit iOS 13+) | 108,136 |

`hitResults` is cleared first, sorted nearest first, and the method returns `false` when empty. `screenPoint` is in **pixels** and is divided by `Screen.width/height` (`:218-223`), so the AR camera must fill the screen. Screen position source: the Input System (legacy `Input` is off in this project).

`TrackableType` flags: `PlaneWithinPolygon=1, PlaneWithinBounds=2, PlaneWithinInfinity=4, PlaneEstimated=8, Planes=15, FeaturePoint=16, Image=32, Face=64, Depth=128, BoundingBox=256, Marker=512, AllTypes=-1`.

| Backend | Viewport raycast | World-ray raycast | Types |
|---|---|---|---|
| ARKit (`AK/ARKitRaycastSubsystem.cs:115-126`) | native: `supportsViewportBasedRaycast = true`; tracked raycasts iOS 13+ | **not native** (`supportsWorldBasedRaycast = false`): managed fallback only (`ARPlaneManager` planes, `ARBoundingBoxManager` boxes), so **no FeaturePoint** through a world ray | native: `Planes \| FeaturePoint`; **no Depth**, no BoundingBox (`AKD/arkit-raycasts.md`) |
| Simulation | not supported: screen point is converted to a session-space ray and sent as a world raycast (`ARRaycastManager.cs:181-203`) | native | FeaturePoint, Planes (`SimulationRaycastSubsystem.cs:73-76`) |

Plane managed fallback semantics: `PlaneWithinInfinity` hits every plane's infinite extension, `PlaneWithinBounds` and `PlaneEstimated` test the extents rectangle, `PlaneWithinPolygon` tests the boundary polygon by winding number (`ARPlaneManager.cs:92-170,174`). It needs an **enabled** `ARPlaneManager`. `hit.trackable` is an `ARPlane` only when that manager is enabled (`:291`).

```csharp
static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>(16);
public static bool TryFloorHit(ARRaycastManager rc, Vector2 screenPos, out Pose pose)
{
    pose = default;
    if (!rc.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds)) return false;
    for (int i = 0; i < s_Hits.Count; i++)
        if (s_Hits[i].trackable is ARPlane pl && pl.alignment == PlaneAlignment.HorizontalUp)
        { pose = s_Hits[i].pose; return true; }          // world pose
    return false;
}
```

### 12.1 `ARRaycastHit` members
`ARRaycastHit` (`AF/ARFoundation/Raycasts/ARRaycastHit.cs`): `pose` (world), `sessionRelativePose`, `distance`, `sessionRelativeDistance`, `hitType`, `trackableId`, `trackable`.

### 12.2 `ARRaycastHit.distance` is unreliable once the origin moves
`TransformAndDisposeNativeHitResults` computes `(nativeHit.pose.position - rayOrigin).magnitude`, subtracting a **world-space** ray origin from a **session-space** hit position (`ARRaycastManager.cs:283`). It is correct only while `TrackablesParent` is at identity. Sorting uses the native session distance and is unaffected. After moving the origin (section 5.3) use `hit.pose` and measure yourself, or `sessionRelativeDistance`.

---

## 13. `ARAnchorManager`, `ARAnchor`

`sealed ... ARAnchorManager : ARTrackableManager<XRAnchorSubsystem, ..., XRAnchor, ARAnchor>`, `[RequireComponent(typeof(XROrigin))]` (`AF/ARFoundation/Anchors/ARAnchorManager.cs:28-36`).

| Member | Signature | Line | Gotcha |
|---|---|---|---|
| `TryAddAnchorAsync` | `async Awaitable<Result<ARAnchor>> TryAddAnchorAsync(Pose pose)` | 111 | `pose` is **world space** (converted at `:114`). Result: `r.status.IsSuccess()`, `r.value`. `NullReferenceException` if `subsystem` is null (`:115`): check `anchorManager.subsystem != null`. ARKit and Simulation add synchronously (`supportsSynchronousAdd`), so the `Awaitable` completes at once (`XRAnchorSubsystem.cs:294-301`) |
| `AttachAnchor` | `ARAnchor AttachAnchor(ARPlane plane, Pose pose)` | 137 | Throws if the manager is disabled or has no subsystem |
| `TryRemoveAnchor` | `bool TryRemoveAnchor(ARAnchor anchor)` | 160 | Throws if disabled |
| `GetAnchor` | `ARAnchor GetAnchor(TrackableId)` | 187 | |
| `anchorPrefab` | `GameObject { get; set; }` | 74 | |
| `TrySaveAnchorAsync`, `TryLoadAnchorAsync`, `TryEraseAnchorAsync`, `TryGetSavedAnchorIdsAsync` | | 212,289,405,469 | **Unsupported on ARKit and Simulation** (`supportsSaveAnchorDelegate = () => false`, `ARKitAnchorSubsystem.cs:171-174`, `SimulationAnchorSubsystem.cs:124-127`). Persist with `ARWorldMap` (section 14) |
| `trackablesChanged` | section 15 | | |

`ARAnchor` (`AF/ARFoundation/Anchors/ARAnchor.cs:17`): `Guid sessionId`; `pose` session space, `transform` world. **Disabling an `ARAnchor` component or its GameObject removes the anchor from the subsystem** (`OnDisable` calls `TryRemoveAnchor`, `:40-46`). Adding the component to a GameObject at run time adds it and disables itself with a warning if that fails (`:24-38`). `ARSession.Reset()` destroys all anchors. ARKit's anchor subsystem exists only on device (`ARKitAnchorSubsystem.cs:163`).

```csharp
public static async Awaitable<ARAnchor> AddAnchor(ARAnchorManager m, Pose worldPose)
{
    Result<ARAnchor> r = await m.TryAddAnchorAsync(worldPose);   // Result<T>: UnityEngine.XR.ARSubsystems
    return r.status.IsSuccess() ? r.value : null;
}
```

---

## 14. ARKit world map

Namespace `UnityEngine.XR.ARKit`, assembly `Unity.XR.ARKit` (Editor and iOS), file `AK/ARKitSessionSubsystem.cs`. Instance: `session.subsystem as ARKitSessionSubsystem` (`subsystemTypeOverride`, `:400`).

| Member | Signature | Line | Notes |
|---|---|---|---|
| `worldMapSupported` | `static bool` (iOS 12+) | 127 | `false` off device. Safe anywhere |
| `worldMappingStatus` | `ARWorldMappingStatus { get; }`: `NotAvailable=0, Limited, Extending, Mapped` | 134 | Poll; save when `Mapped` |
| `GetARWorldMapAsync()` | `ARWorldMapRequest GetARWorldMapAsync()` | 86 | Request struct: `status` (`ARWorldMapRequestStatus`: `Invalid, Pending, Success, ErrorUnknown, ErrorNotSupported, ErrorBadData, ErrorInsufficientFeatures`), `GetWorldMap()` (throws unless `Success`), `Dispose()` (`AK/ARWorldMapRequest.cs`). Dispose **both** request and map |
| `GetARWorldMapAsync(Action<ARWorldMapRequestStatus, ARWorldMap>)` | `void` | 113 | On `Success` the map must be disposed; otherwise it is default and needs no dispose |
| `ARWorldMap.Serialize` | `NativeArray<byte> Serialize(Allocator)` | `ARWorldMap.cs:51` | Caller disposes the array; throws `InvalidOperationException` if invalid |
| `ARWorldMap.TryDeserialize` | `static bool TryDeserialize(NativeArray<byte>, out ARWorldMap)` | `:79` | `false` off device and for bad data |
| `ARWorldMap.valid`, `Dispose()` | | `:32,18` | |
| `ApplyWorldMap` | `void ApplyWorldMap(ARWorldMap)` | 151 | "Attempts to relocalize"; equivalent to setting `initialWorldMap` on the `ARWorldTrackingConfiguration`; an invalid map sets it to `nil`. After a successful relocalisation the map's planes and anchors are added |

"Re-applying configuration": `ARSession.Update` calls `XRSessionSubsystem.Update`, which re-chooses a configuration from `requestedFeatures` **every frame** and hands it to `UnityARKit_Session_Update` (`AF/ARSubsystems/Session/XRSessionSubsystem.cs:127-146`, `AK/ARKitSessionSubsystem.cs:322`). Changing a requested feature (collaboration, depth, plane mode, world alignment through `requestedWorldAlignment` at `:203`) therefore takes effect next frame. Whether the native layer re-runs the `ARConfiguration` is not visible. `ARSession.Reset()` clears tracking and trackables; the sources do not say whether it keeps a previously applied map, so call `ApplyWorldMap` again after a reset. Watch `ARSession.notTrackingReason == Relocalizing` via the **subsystem** (`session.subsystem.notTrackingReason`, section 4).

```csharp
#if UNITY_EDITOR || UNITY_IOS
public static void SaveWorldMap(ARSession s, Action<NativeArray<byte>> sink)
{
    var sub = s.subsystem as ARKitSessionSubsystem;
    if (sub == null || !ARKitSessionSubsystem.worldMapSupported) return;
    if (sub.worldMappingStatus != ARWorldMappingStatus.Mapped) return;
    sub.GetARWorldMapAsync((status, map) =>
    {
        if (status != ARWorldMapRequestStatus.Success) return;
        using (map)
        using (NativeArray<byte> bytes = map.Serialize(Allocator.Temp))
            sink(bytes);
    });
}
public static bool LoadWorldMap(ARSession s, NativeArray<byte> bytes)
{
    var sub = s.subsystem as ARKitSessionSubsystem;
    if (sub == null || !ARWorldMap.TryDeserialize(bytes, out ARWorldMap map)) return false;
    using (map) { sub.ApplyWorldMap(map); }          // ARKit keeps its own reference (DEVICE: confirm)
    return true;
}
#endif
```

---

## 15. Trackable managers: `trackablesChanged`, shared base

`ARTrackableManager<TSubsystem, TDescriptor, TProvider, TSessionRelativeData, TTrackable>` (`AF/ARFoundation/Common/TrackingSubsystem/ARTrackableManager.cs:25`), `[RequireComponent(typeof(XROrigin))]` and `[DisallowMultipleComponent]`.

| Member | Signature | Line |
|---|---|---|
| `trackablesChanged` | `UnityEvent<ARTrackablesChangedEventArgs<TTrackable>> trackablesChanged { get; }` (a **UnityEvent**, not a C# `event`: use `AddListener`) | 51 |
| `trackables` | `TrackableCollection<TTrackable>` (`count`, indexer by `TrackableId`, `TryGetTrackable`, struct enumerator) | 79 |
| `SetTrackablesActive(bool)` | | 333 |
| `ARTrackablesChangedEventArgs<T>` | `ReadOnlyList<T> added`, `ReadOnlyList<T> updated`, **`ReadOnlyList<KeyValuePair<TrackableId, T>> removed`** | `ARTrackablesChangedEventArgs.cs:18-28` |

Gotchas: the three lists are **static, shared by every manager of the same trackable type, and cleared after the callback** (`ARTrackableManager.cs:33-45,167-192`); never store or mutate them. In `removed`, `Value` may be `null` (`:151-155`). `ReadOnlyList<T>.GetEnumerator()` returns `List<T>.Enumerator`, so `foreach` does not allocate (`CU/Collections/ReadOnlyList.cs:61`). Trackable GameObjects are destroyed after the callback when `destroyOnRemoval` (`:187-191`). `ARTrackable.pose` is session space; `transform` is world.

```csharp
pm.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
pm.trackablesChanged.AddListener(OnPlanes);
static void OnPlanes(ARTrackablesChangedEventArgs<ARPlane> a)
{
    foreach (ARPlane p in a.added) { bool floor = (p.classifications & PlaneClassifications.Floor) != 0; NativeArray<Vector2> b = p.boundary; }
    foreach (KeyValuePair<TrackableId, ARPlane> kv in a.removed) { ARPlane p = kv.Value; /* may be null */ }
}
```

---

## 16. XR Simulation

Assembly `Unity.XR.Simulation` (Editor and desktop players), namespace `UnityEngine.XR.Simulation`, native plugin `XRSimulationSubsystem` for macOS, Linux and Windows (`AF/Simulation/Plugins/`).

### 16.1 Enabling it for Play Mode
1. Project Settings > XR Plug-in Management > **Standalone** providers > enable **XR Simulation** (its loader metadata targets `BuildTargetGroup.Standalone`, `AFE/Simulation/SimulationPackageMetadata.cs:32-40`) (`AFD/xr-simulation/simulation-getting-started.md`). Already set here.
2. Pick an environment in Window > XR > AR Foundation > XR Environment. Here: fallback `DefaultSimulationEnvironment.prefab`.
3. The scene needs an `XROrigin` with a camera and an `ARSession`; Simulation locates the origin with `FindAnyObjectByType<XROrigin>()` and logs `An XR Origin is required in the scene` otherwise (`SimulationSessionSubsystem.cs:58-65`, `SimulationCameraSubsystem.cs:123-137`).
4. Move with right mouse + WASD/QE, Shift to go faster (`AFD/xr-simulation/simulation-getting-started.md`).

Environments: the sample set installs to `Assets/UnityXRContent/ARFoundation` from the XR Environment view (`AFD/xr-simulation/simulation-getting-started.md`). A custom environment is a prefab whose root carries the (internal) `SimulationEnvironment` component, with optional public components `SimulatedBoundingBox`, `SimulatedAnchor`, `SimulatedLight`, `SimulatedEnvironmentProbe`, `SimulatedTrackedImage` (`AF/Simulation/Environment/`). `SimulatedMeshClassification` and `SimulatedBoundedPlane` are internal; the plane one is not yet used by the plane subsystem (`SimulatedBoundedPlane.cs:7`).

What it does to your scene: creates an extra additive scene `Simulated Environment Scene <guid>` with its **own 3D physics scene** (`SimulationSceneManager.cs:13,71`, `BaseSimulationSceneManager.cs:11,60`), puts the environment on layer 30, removes layer 30 from the XR camera's culling mask (`SimulationSessionSubsystem.cs:116`), and creates a persistent hidden `SimulationCamera` object with a disabled `Camera` that renders into a render texture (`SimulationCameraPoseProvider.cs:94-108`). `Physics.Raycast` in your scene does not hit the environment; `SceneManager.sceneCount` grows by one.

### 16.2 What it provides

| Feature | In Simulation | Source |
|---|---|---|
| Session | Yes. `trackingState` always `Tracking`; no `matchFrameRate`; no install | `SimulationSessionSubsystem.cs:41,209` |
| Camera | One configuration, `framerate == null`; intrinsics all zero; image convertible to `RGBA32/ARGB32/BGRA32/RGB24` only; torch simulated; no display or timestamp support flags | `SimulationCameraSubsystem.cs:150,213-219,489,499-514` |
| Planes | Yes: horizontal and vertical, axis-aligned only; boundary vertices; **no classification** | `SimulationPlaneSubsystem.cs:418-421`, `AFD/xr-simulation/simulation-overview.md` |
| Meshes | Yes (via `XRMeshSubsystem`); all appear at Play Mode start. Classification exists only as a string in the native side and is not readable on 6000.3 | `AFD/.../simulation-overview.md`, `SimulatedMeshClassification.cs` |
| Depth | **GPU environment depth texture only** (`TryGetEnvironmentDepthTexture`). No CPU depth image, no confidence, no smoothing, no human stencil or depth. `currentEnvironmentDepthMode` reads `Disabled` | `SimulationOcclusionSubsystem.cs:121-124` |
| Bounding boxes | Yes, from `SimulatedBoundingBox` components in the environment prefab (`size`, `center`, `classifications`); classification supported | `SimulatedBoundingBox.cs`, `SimulationBoundingBoxSubsystem.cs:66-71` |
| Raycast | World-based only; FeaturePoint and Planes; boxes through the managed fallback | `SimulationRaycastSubsystem.cs:73-76` |
| Anchors | Add and attach yes (synchronous); save, load, erase no | `SimulationAnchorSubsystem.cs:117-128` |
| Point cloud, image tracking, environment probes | Yes | `SimulationLoader.cs:35-39` |
| World map | No (ARKit type) | |

### 16.3 Detect it
Use `Detect()` from section 3 (`"XRSimulation-Session"`), or in Editor-only code `activeLoader is UnityEngine.XR.Simulation.SimulationLoader`.

---

## 17. Device checks

| # | Item | Why the sources cannot settle it |
|---|---|---|
| D1 | `Camera Offset` local Y stays 0 on ARKit; `XRInputSubsystem.GetTrackingOriginMode()` value and `TryRecenter` effect | `XROrigin.cs:223-257,355-358`; the input subsystem is native |
| D2 | ARKit camera format list: is a 60 Hz entry present on the target phones; order; does `currentConfiguration` set restart tracking; effect on `frameRate` when `matchFrameRateRequested` is true | native |
| D3 | `ARCameraManager` frames, `TryAcquireLatestCpuImage`, `TryGetIntrinsics` with `Camera.enabled = false` | managed path is render-independent, native ARKit is not visible |
| D4 | Environment depth size, orientation (screen-rotated or sensor), field of view versus the camera image; the 256 x 192 size; confidence format | native; only `DepthFloat32` and the `RFloat` conversion are in the sources |
| D5 | LiDAR-less phones: `environmentDepthImageSupported`, `ARMeshManager`, RoomPlan all report unsupported; confirm graceful paths | descriptors are native delegates |
| D6 | `ARWorldMap` disposal right after `ApplyWorldMap`; behaviour of `ARSession.Reset()` with an applied map | native |
| D7 | RoomPlan capture needs an exclusive camera or its own ARSession; interaction with the running `ARSession` | `UnityARKit_BoundingBox_*` are native |
| D8 | Mesh classification array length equals mesh triangle count at read time | native, read at a different moment than generation |
| D9 | `ARRaycastHit.distance` value after the origin move (section 12.2) | read from code; confirm numerically |
| D10 | Simulation camera and depth with the XR camera disabled | Editor-only, runs a native plugin |
