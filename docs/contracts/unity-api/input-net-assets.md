# Unity API facts: input, network, assets, platform services

Unity 6000.3.25f1, iOS (IL2CPP). Input System 1.20.0, LiteNetLib 2.1.4, glTFast 6.20.0, uGUI 2.0.0 (contains TextMesh Pro), Newtonsoft Json 3.2.2.
Nothing here was run in the Editor or on a phone. Every code block marked ✔ was compiled (netstandard2.1, C# 9, `UNITY_IOS;ENABLE_IL2CPP;ENABLE_INPUT_SYSTEM`) against the real engine DLLs and `ATLAS/Library/ScriptAssemblies`.

## 0. Sources and abbreviations

| Tag | Path |
|---|---|
| `IS/` | `ATLAS/Library/PackageCache/com.unity.inputsystem@7a4e1a2a8194/InputSystem/Runtime/` |
| `ISdoc/` | `ATLAS/Library/PackageCache/com.unity.inputsystem@7a4e1a2a8194/Documentation~/` |
| `LN/` | `ATLAS/Library/PackageCache/com.revenantx.litenetlib@a79c0aceceb1/` |
| `GF/` | `ATLAS/Library/PackageCache/com.unity.cloud.gltfast@11ddc2436f97/` (`Runtime/Scripts/` unless stated) |
| `UGUI/` | `ATLAS/Library/PackageCache/com.unity.ugui@f3ea87521141/Runtime/` |
| `xml:<Module>` | `~/unity-managed/Editor/Data/Managed/UnityEngine/UnityEngine.<Module>.xml`, with the line of the `<member>` |
| `meta:<Module>` | member signature and attributes read from `UnityEngine.<Module>.dll` metadata (MetadataLoadContext) |
| `Build/iOS` | `ATLAS/Build/iOS`: an exported Xcode project from a real 6000.3.25f1 build (`unity-build.provenance.json`, 3 Oct 2026). Used only as evidence of what is in the player (`Classes/*.mm`, `Libraries/libiPhone-lib.a`, `Info.plist`, `Data/ScriptingAssemblies.json`, `Il2CppOutputProject/Source/il2cppOutput`) |

Engine members live in `UnityEngine.*Module` assemblies that every asmdef gets automatically unless it sets `noEngineReferences`. The "asmdef" column below names the reference you add for package code.

## 1. Project settings that change behaviour

| Setting | Value (file:line) | Consequence |
|---|---|---|
| Active Input Handling | `activeInputHandler: 1` (`ProjectSettings/ProjectSettings.asset:943`); 1 means "new Input System only" (`IS/../Editor/Settings/EditorPlayerSettingHelpers.cs:73-78`) | Legacy `UnityEngine.Input` is off. `StandaloneInputModule` throws `InvalidOperationException` in this mode (package `CHANGELOG.md:1271`). **Runtime IMGUI `OnGUI` receives no input events** (`ISdoc/use-imgui-alongside-input-system.md:7,10`, `ISdoc/KnownLimitations.md:12`). |
| Scripting backend | iPhone = IL2CPP (`ProjectSettings.asset:847-849`); stripping `1` = `ManagedStrippingLevel.Low` (`ProjectSettings.asset:853-854`, set by `Assets/Atlas/Editor/AtlasProjectSetup.cs:50`) | Reflection-only code can be stripped (see LiteNetLib `NetSerializer`, §3.7). |
| Language and profile | `apiCompatibilityLevel: 6` (`ProjectSettings.asset:941`) = `ApiCompatibilityLevel.NET_Standard`, "Profile that targets .NET Standard 2.1" (`UnityEditor.CoreModule.xml`, `meta`); C# 9 | `System.Text.Json` does not exist (§11). |
| iOS target | 17.0 (`ProjectSettings.asset:202`) | |
| Info.plist keys written by `Assets/Atlas/Editor/AtlasPostBuild.cs:26-35` | `NSCameraUsageDescription`, `NSLocalNetworkUsageDescription`, `NSAllowsLocalNetworking`, URL scheme `atlas`, `UIFileSharingEnabled` | Present in `Build/iOS/Info.plist:27-50`. Orientation is landscape-right only (`Info.plist:94-97`). |

## 2. Input System 1.20.0

### 2.1 Assembly and symbols

| What | Value | Confirmed at |
|---|---|---|
| asmdef to reference | `Unity.InputSystem` (also holds EnhancedTouch, the iOS device classes and `InputSystemUIInputModule`) | `IS/../Unity.InputSystem.asmdef:2`; `Atlas.Input.asmdef` already references it |
| UI Toolkit bridge | separate assembly `Unity.InputSystem.ForUI`, auto-referenced, runs by itself | `IS/Plugins/InputForUI/InputSystemForUI.asmdef`; listed in `Build/iOS/Data/ScriptingAssemblies.json` |
| Defines you can use | `ENABLE_INPUT_SYSTEM` (Unity sets it when the new handler is on). `ENABLE_LEGACY_INPUT_MANAGER` is not defined here. Package-internal `UNITY_INPUT_SYSTEM_ENABLE_UI` etc. are `versionDefines` of the package asmdef only | `IS/InputSystem.cs:3583` (iOS init), `IS/../Unity.InputSystem.asmdef` |
| Namespaces | `UnityEngine.InputSystem`, `.Haptics` (`IDualMotorRumble`), `.Controls`, `.EnhancedTouch`, `.UI`, `.iOS` | |
| Name clashes | `UnityEngine.InputSystem.TouchPhase` vs `UnityEngine.TouchPhase`, `EnhancedTouch.Touch` vs `UnityEngine.Touch`: alias or fully qualify | compile test |

### 2.2 Actions in code, no `.inputactions` asset ✔

```csharp
using UnityEngine.InputSystem;

public sealed class AtlasActions : System.IDisposable
{
    public readonly InputActionMap Map = new InputActionMap("Atlas");
    public readonly InputAction Move, Look, Confirm, Back, Trigger, Tap;

    public AtlasActions()
    {
        Move = Map.AddAction("move", InputActionType.Value, expectedControlLayout: "Vector2");
        Move.AddBinding("<Gamepad>/leftStick");
        Move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

        Look    = Map.AddAction("look", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");
        Look.AddBinding("<Mouse>/delta");
        Confirm = Map.AddAction("confirm", InputActionType.Button);
        Confirm.AddBinding("<Gamepad>/buttonSouth");
        Confirm.AddBinding("<Keyboard>/space");
        Back    = Map.AddAction("back", InputActionType.Button, "<Gamepad>/buttonEast");
        Trigger = Map.AddAction("trigger", InputActionType.Value, "<Gamepad>/rightTrigger", expectedControlLayout: "Axis");
        Tap     = Map.AddAction("tap", InputActionType.Button, "<Touchscreen>/primaryTouch/press");
        Tap.AddBinding("<Pointer>/press");
    }
    public void Enable() => Map.Enable();
    public void Disable() => Map.Disable();
    public void Dispose() => Map.Dispose();
}
```

| Member | Signature | Source | Gotcha |
|---|---|---|---|
| `new InputActionMap` | `InputActionMap(string name)` | `IS/Actions/InputActionMap.cs:334` | Implements `IDisposable` (`:352`). Not enabled until `Enable()` (`:541`). |
| `AddAction` (extension, `InputActionSetupExtensions`) | `InputAction AddAction(this InputActionMap map, string name, InputActionType type = default, string binding = null, string interactions = null, string processors = null, string groups = null, string expectedControlLayout = null)` | `IS/Actions/InputActionSetupExtensions.cs:170` | `default(InputActionType)` is **Value**, so say `InputActionType.Button` for buttons. Throws `InvalidOperationException` if the map is enabled or the name exists. **The package doc sample `map.AddAction("look", "<Gamepad>/leftStick")` does not compile** (`ISdoc/create-actions-in-code.md`; the 2nd parameter is the type): use `binding:`. |
| `AddBinding` | `BindingSyntax AddBinding(this InputAction action, string path, string interactions = null, string processors = null, string groups = null)` | `…SetupExtensions.cs:305` | Add bindings before `Enable()`. |
| `AddCompositeBinding` / `.With` | `CompositeSyntax AddCompositeBinding(this InputAction, string composite, …)`; `CompositeSyntax With(string name, string binding, …)` | `…:540`, `:1775` | `"2DVector"` parts: Up, Down, Left, Right (`IS/Actions/Composites/Vector2Composite.cs:48-76`); registered at `IS/InputManager.cs:2112`. |
| Free-standing action | `InputAction(string name = null, InputActionType type = default, string binding = null, string interactions = null, string processors = null, string expectedControlType = null)` | `IS/Actions/InputAction.cs:800` | Last parameter is `expectedControlType` here, `expectedControlLayout` on `AddAction`. |
| Project-wide actions | `static InputActionAsset InputSystem.actions` | `IS/InputSystem.cs:3042` | There is no `.inputactions` asset in `Assets` except the XRI sample, so treat as `null`. Setting it throws in Play mode. |

### 2.3 `<Gamepad>` binding paths

All paths are from the layout attributes on `GamepadState` (`IS/Devices/Gamepad.cs:117-129,138,149,162,173`).

| Control | Path | C# property (`Gamepad.cs`) | Value type |
|---|---|---|---|
| South / East / West / North | `<Gamepad>/buttonSouth`, `buttonEast`, `buttonWest`, `buttonNorth` (aliases a, b, x, y, cross, circle, square, triangle) | `buttonSouth` … (`:426-456`); `aButton`… (`:541-576`) | float 0..1 (button) |
| Shoulders | `leftShoulder`, `rightShoulder` | `:498`, `:508` | button |
| Triggers | `leftTrigger`, `rightTrigger` | `leftTrigger` `:528`, `rightTrigger` `:536` | float 0..1, `ReadValue<float>()` |
| Sticks | `leftStick`, `rightStick` (`leftStick/x`, `/up` …) | `:514`, `:520`; `stickDeadzone` processor, defaults min 0.125, max 0.925 (`IS/InputSettings.cs:783-784`) | `Vector2` |
| Stick click | `leftStickPress`, `rightStickPress` | properties are named `leftStickButton`, `rightStickButton` (`:462`, `:468`) | button |
| Menu | `start`, `select` | `startButton`, `selectButton` (`:475`, `:482`) | button |
| D-pad | `dpad`, `dpad/up`, `/down`, `/left`, `/right` | `dpad` `:488` | `Vector2` or buttons |

`defaultButtonPressPoint` = 0.5 (`IS/InputSettings.cs:789`), so `IsPressed()` on a trigger flips at half pull.
For a Switch Pro controller on iOS, the letter-named properties (`aButton` …) are only right on `SwitchProControlleriOS` (`IS/Plugins/iOS/IOSGameController.cs:200-205`); the positional names (`buttonSouth` …) are always positional.

### 2.4 Callbacks versus polling ✔

| | Callbacks | Polling |
|---|---|---|
| API | `event Action<InputAction.CallbackContext> started / performed / canceled` (`IS/Actions/InputAction.cs:582,597,611`); `ctx.ReadValue<T>()`, `ctx.ReadValueAsButton()`, `ctx.control`, `ctx.time` (`:2136…`) | `ReadValue<T>()` (`:1083`), `IsPressed()` (`:1228`), `WasPressedThisFrame()` (`:1307`), `WasReleasedThisFrame()` (`:1402`), `WasPerformedThisFrame()` (`:1509`) |
| When it runs | Inside the input update (start of dynamic Update by default) | Any time after that update |
| Allocation | A lambda that captures state allocates when you create it | `ReadValueAsObject()` "allocates GC memory" (doc at `:1103`); `ReadValue<T>()` is the form without a boxed result |
| Gotchas | Disabling an action in progress fires `canceled` from inside `Disable()` (`:978`) | `ReadValue<TValue>` throws `InvalidOperationException` if `TValue` does not match the control (`:1078`). `WasXxxThisFrame` ignores the enabled state for the rest of the frame (`:1295`). With `UpdateMode` other than dynamic, use `WasPressedThisDynamicUpdate` in `Update` (`:1351`). |

Button controls also expose `wasPressedThisFrame`, `wasReleasedThisFrame`, `isPressed` (`IS/Controls/ButtonControl.cs:198,282,327`), so `Gamepad.current.buttonSouth.wasPressedThisFrame` works without an action.

### 2.5 `Gamepad.current`, devices, rumble ✔

| Member | Signature | Source | Gotcha |
|---|---|---|---|
| `Gamepad.current` | `static Gamepad current { get; }` | `IS/Devices/Gamepad.cs:627` | `null` with no pad. The last pad that received non-noisy input. |
| `Gamepad.all` | `static ReadOnlyArray<Gamepad> all` | `:643` | No allocation; do not store it. |
| `InputSystem.onDeviceChange` | `static event Action<InputDevice, InputDeviceChange>` | `IS/InputSystem.cs:1103` | Values: Added, Removed, Disconnected, Reconnected, Enabled, Disabled, UsageChanged, ConfigurationChanged, SoftReset, HardReset, Destroyed. Poll `Gamepad.current` at use time as well as listening. |
| `Gamepad.SetMotorSpeeds` | `virtual void SetMotorSpeeds(float lowFrequency, float highFrequency)` | `:799` | Clamps to 0..1 (`IS/Devices/Haptics/DualMotorRumble.cs:119-129`) and sends `DualMotorRumbleCommand` ('RMBL') to the device; **the return value of `ExecuteCommand` is ignored**, so a device that does not handle it fails silently. |
| `IDualMotorRumble` | `UnityEngine.InputSystem.Haptics`; `void SetMotorSpeeds(float, float)`; base `IHaptics`: `PauseHaptics`, `ResumeHaptics`, `ResetHaptics` | `IS/Devices/Haptics/IDualMotorRumble.cs:10`, `IHaptics.cs:76` | `Gamepad : InputDevice, IDualMotorRumble` (`Gamepad.cs:416`). Global versions: `InputSystem.PauseHaptics()` etc. (`IS/InputSystem.cs:1884`). |

**Rumble on iOS in 6000.3.25f1: not implemented. Plan on no gamepad rumble.** Evidence:

1. The iOS classes `iOSGameController : Gamepad` (`IS/Plugins/iOS/IOSGameController.cs:171`), `XboxOneGampadiOS` (`:179`), `DualShock4GampadiOS`, `DualSenseGampadiOS`, `SwitchProControlleriOS` add no rumble code. They inherit `Gamepad.SetMotorSpeeds` unchanged.
2. The package documents it as newer: "Gamepads on iOS, tvOS, and visionOS. **Requires Unity 6000.7 or later.** … Apple's GameController framework exposes haptics" (`ISdoc/gamepad-haptics.md:32-36`). `CHANGELOG.md:46` says docs were added, not code.
3. The package's only native iOS source is `Runtime/Plugins/iOS/iOSStepCounter.mm`; none for haptics (also the only package source copied into `Build/iOS/Libraries/com.unity.inputsystem`).
4. The player's own native library `Build/iOS/Libraries/libiPhone-lib.a` (from this exact editor) contains `GCController` and `GCExtendedGamepad` but **zero** occurrences of `GCDeviceHaptics`, `GCHapticsLocality`, `CHHapticEngine`, `Rumble`. Its only "Haptic" symbols are `XRInputDevices_*Haptic*` (XR devices). `Build/iOS/Unity-iPhone.xcodeproj` links `GameController.framework` (weak) but not `CoreHaptics.framework`.

Closest real alternatives: `Handheld.Vibrate()` for the phone (no strength or pattern, §8); a native plugin for CoreHaptics or `UIImpactFeedbackGenerator` (§8); for a gamepad, Apple's `GCController.haptics` from native code (an Apple API, not checked here). Master Plan §4.10 (line 598: "the gamepad's rumble when a gamepad is in hand") can only be honoured on Editor and desktop, not on the phone.

`DualShockGamepad` has `SetLightBarColor` (`IS/Plugins/DualShock/DualShockGamepad.cs:133`); nothing in the iOS code overrides it, so assume it does nothing on iOS.

### 2.6 Screen tap from the headset lever ✔

The lever produces a screen touch. Detect the touch *beginning*; the `tap` control is stricter.

| Need | Code | Source | Gotcha |
|---|---|---|---|
| Any touch began this frame | `var ts = Touchscreen.current; ts != null && ts.primaryTouch.press.wasPressedThisFrame` | `Touchscreen.primaryTouch` `IS/Devices/Touchscreen.cs:492`; `TouchControl.press` `IS/Controls/TouchControl.cs:36`; `TouchPressControl` is 1 for phase Began, Stationary, Moved (`TouchPressControl.cs:40`) | `Touchscreen.current` (`:521`) is `null` in the Editor without a touch device. |
| Any pointer (touch, mouse, pen) | `var p = Pointer.current; p != null && p.press.wasPressedThisFrame` (`Pointer.current` `IS/Devices/Pointer.cs:178`; `press` `:164`) | | One code path for Editor (mouse) and phone (touch). Binding path `<Pointer>/press`. |
| Tap (released quickly) | binding `<Touchscreen>/primaryTouch/tap` (`TouchControl.tap` `:143`) | A touch is a tap only if it ends within `defaultTapTime` = 0.2 s **and** moves ≤ `tapRadius` = 5 px (`IS/Devices/Touchscreen.cs:715-720`, defaults `IS/InputSettings.cs:791,794`) | A lever that is held, or a shaky head, will not tap. It fires on release. Prefer `press` for "activate". |
| Position | `ts.primaryTouch.position.ReadValue()` | | Screen pixels. |

`primaryTouch` stays in progress until the last finger lifts (`Touchscreen.cs:485`). `Touchscreen.touches` has 10 `TouchControl`s by default (`:499`).

**EnhancedTouch** (`UnityEngine.InputSystem.EnhancedTouch`, same assembly): call `EnhancedTouchSupport.Enable()` (`IS/Plugins/EnhancedTouch/EnhancedTouchSupport.cs:83`; calls nest, `Disable()` undoes one) before `Touch.activeTouches` (`Touch.cs:444`), `Touch.fingers`, `Touch.onFingerDown/Up/Move` (`:516…`); all throw `InvalidOperationException` when not enabled. `Touch.phase` includes `Stationary`, which `Touchscreen` never reports (`Touchscreen.cs:430-436`). Its docs say it cannot be used in combination with `InputAction`s although both can run side by side (`EnhancedTouchSupport.cs:28`). `TouchSimulation.Enable()` (`TouchSimulation.cs:37`) fakes a touchscreen from the mouse in the Editor.

### 2.7 Keyboard and mouse for the Editor walker ✔

`Keyboard.current` (`IS/Devices/Keyboard.cs:2389`), `keyboard.wKey.isPressed` (`:1546`) or `keyboard[Key.LeftShift]` (`:2368`), `Mouse.current` (`IS/Devices/Mouse.cs:258`), `mouse.delta.ReadValue()`, `mouse.rightButton.isPressed`. All `null` on iOS (no such device); guard every use. In Play mode keyboard and pointers only receive input while the Game view has focus (`InputSettings.EditorInputBehaviorInPlayMode`, default `PointersAndKeyboardsRespectGameViewFocus`, `IS/InputSettings.cs:960-984`). Bind `<Keyboard>/…` and `<Mouse>/…` in the same actions as the gamepad (§2.2) so one action map serves both.

### 2.8 Update mode and settings ✔

| Member | Source | Notes |
|---|---|---|
| `InputSettings.updateMode` : `UpdateMode { ProcessEventsInDynamicUpdate = 1 (default), ProcessEventsInFixedUpdate, ProcessEventsManually }` | `IS/InputSettings.cs:55,835-867` | Wrong-phase reads log errors in the Editor and development builds (release returns the other phase's state). |
| `InputSystem.settings` get/set; `InputSystem.Update()` | `IS/InputSystem.cs:2857`, `:2783` | Code can set a new mode: `var s = ScriptableObject.CreateInstance<InputSettings>(); s.updateMode = InputSettings.UpdateMode.ProcessEventsManually; InputSystem.settings = s;`. With `ProcessEventsManually`, nothing is read until you call `InputSystem.Update()` (needed for the one frame loop, F5). |
| `InputSettings.backgroundBehavior` | `:487` | Default `ResetAndDisableNonBackgroundDevices`. |
| `InputSystem.onBeforeUpdate`, `onAfterUpdate` | `IS/InputSystem.cs:2811,2830` | |

There is no `InputSettings` asset in the project, so players run on the built-in defaults.

### 2.9 UI input without an asset

`InputSystemUIInputModule` (`UnityEngine.InputSystem.UI`, `IS/Plugins/UI/InputSystemUIInputModule.cs:108`): on enable it calls `AssignDefaultActions()` (`:1541`) when it has none (`:1647`), so uGUI works with no asset. UI Toolkit runtime input comes from `InputSystemProvider`, which falls back to `DefaultInputActions` when there are no project-wide actions (`IS/Plugins/InputForUI/InputSystemProvider.cs:713-734`), so UI Toolkit needs no EventSystem in 6000.3 despite `ISdoc/KnownLimitations.md:13-14`. Text entry into uGUI/TMP input fields still bypasses the Input System (`KnownLimitations.md:19`).

## 3. LiteNetLib 2.1.4

2.x splits the library into `LiteNetManager` / `LiteNetPeer` (base) and `NetManager` / `NetPeer` (multi-channel, the one to use). Names from 1.x code (`NetManager(listener)`, `Connect(host, port, key)`, `PollEvents()`) still exist; the listener and reader signatures changed.

### 3.1 Package facts

| What | Value | Confirmed at |
|---|---|---|
| asmdef | `LiteNetLib` (no references, `allowUnsafeCode: true`, `autoReferenced: true`, `noEngineReferences: false`, **no `versionDefines`, no `defineConstraints`**) | `LN/LiteNetLib.asmdef` |
| Atlas use | `Atlas.Link.asmdef` references `LiteNetLib` with `noEngineReferences: true`. This compiles: the public API exposes no `UnityEngine` type. ✔ (compiled with only `LiteNetLib.dll`) | |
| Namespaces | `LiteNetLib`, `LiteNetLib.Utils`, `LiteNetLib.Layers` | |
| Symbols read by the sources | `UNITY_2018_3_OR_NEWER` → file-local `#define UNITY_SOCKET_FIX` (`LN/LiteNetManager.cs:2`, `LiteNetManager.Socket.cs:2`; enables `PausedSocketFix`); `UNITY_5_3_OR_NEWER` → logs via `UnityEngine.Debug.Log` (`NetDebug.cs:51`); `DEBUG` / `SIMULATE_NETWORK` → latency and loss simulation fields (`LiteNetManager.cs:126-149`, inert otherwise); `DEBUG_MESSAGES` → trace logs; `DISABLE_IPV6` → `IPv6Support = false` (`LiteNetManager.Socket.cs:63`); `UNITY_ANDROID`, `UNITY_SWITCH`; `STATS_ENABLED` defined in `LiteNetPeer.cs:2`. `NET5_0_OR_GREATER` etc. are never defined by Unity. | |
| Hub/Station | `LN/LiteNetLib.csproj` targets `net8.0;netstandard2.1`, version 2.1.4; the NuGet package was not inspected | |

### 3.2 `NetManager` options ✔ (all public fields, defaults from source)

| Option | Type, default | Source | Gotcha |
|---|---|---|---|
| constructor | `NetManager(INetEventListener listener, PacketLayerBase extraPacketLayer = null)` | `LN/NetManager.cs:62` | The base constructor's `useSocketFix` (`LiteNetManager.cs:294`) is not exposed: the Unity socket-resume fix is always on. |
| `UnconnectedMessagesEnabled` | `bool`, false | `LN/LiteNetManager.cs:101` | Needed to receive `SendUnconnectedMessage` (`:1286`) in `OnNetworkReceiveUnconnected`. |
| `BroadcastReceiveEnabled` | `bool`, false | `:169` | |
| `UpdateTime` | `int` ms, 15 | `:113` | Logic-thread period. `TriggerUpdate()` (`:1307`) forces a send now. |
| `PingInterval` | `int` ms, 1000 | `:118` | |
| `DisconnectTimeout` | `int` ms, 5000 | `:124` | No packet (including keep-alives) for this long closes the peer. |
| `ReconnectDelay`, `MaxConnectAttempts` | 500 ms, 10 | `:174,179` | A failed connect takes ≈ 5 s to report. |
| `ChannelsCount` | `byte`, 1; setter throws unless 1..64 | `LN/NetManager.cs:22` | **Set before `Start`/`Connect` on both ends** and equal on both: peers allocate `ChannelsCount * 4` channel slots at creation (`NetPeer.cs:26`), a packet for a higher channel is dropped silently (`NetPeer.cs:225-230`). |
| `IPv6Enabled` | `bool`, **true** | `LiteNetManager.cs:233` | A second socket (`::`) binds the same port (`LiteNetManager.Socket.cs:366-378`); if that bind fails it is silently skipped. Use `false` for LAN IPv4 only. `LiteNetManager.IPv6Support` is `Socket.OSSupportsIPv6` (`Socket.cs:33,61-70`). |
| `UseNativeSockets` | `bool`, false | `:255` | "Only for Windows/Linux": reset to false at `Start` unless `NativeSocket.IsSupported` (`Socket.cs:345`), which is true only for Linux (`libc`) and Windows (`NativeSocket.cs:37,61,132-139`). Leave false on iOS. |
| `UnsyncedEvents`, `UnsyncedReceiveEvent`, `UnsyncedDeliveryEvent` | false | `:154,159,164` | True makes listener callbacks run on LiteNetLib's threads. Leave false, call `PollEvents()` from the main thread. One exception, inside `LiteNetTransport` only: `UnsyncedReceiveEvent` is true so that a packet is stamped on arrival and queued for the poll (lane D §2.7); listeners still hear it on the main thread. |
| `AutoRecycle` | `bool`, **false** | `:228` | When false, **call `reader.Recycle()` in every receive handler** (`NetPacketReader.cs:37-42`). |
| `MtuDiscovery` / `MtuOverride` | false / 0 | `:244`, `:238` | MTU stays at 1024 (`NetConstants.cs:89-103`, minus layer overhead), so one unreliable packet carries at most `peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)` = 1023 bytes (`LiteNetPeer.cs:482`). |
| `ReuseAddress`, `DontRoute`, `AllowPeerAddressChange`, `DisconnectOnUnreachable`, `NatPunchEnabled` | false | `:184,191,265,260,106` | |
| `MaxFragmentsCount`, `EnableStatistics`, `ReceivePollingTime` | 65535, false, 50000 µs | `:206,201`, `Socket.cs:41` | |

### 3.3 Client and listener ✔

```csharp
using LiteNetLib; using LiteNetLib.Utils;

public sealed class LinkListener : INetEventListener
{
    public NetManager Manager; public NetPeer Server;
    public void Create()
    {
        Manager = new NetManager(this)
        { UnconnectedMessagesEnabled = true, UpdateTime = 15, DisconnectTimeout = 5000,
          ChannelsCount = 2, IPv6Enabled = false, UseNativeSockets = false, AutoRecycle = false };
        if (!Manager.Start()) throw new System.InvalidOperationException("socket bind failed");  // port 0 = any
        Server = Manager.Connect("192.168.1.20", 9050, "ATLAS-1");                               // key string
    }
    public void Tick() => Manager.PollEvents();           // main thread, once per frame

    public void OnPeerConnected(NetPeer peer) { }
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) { /* info.Reason, info.SocketErrorCode */ }
    public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError e) { }
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    { int id = reader.GetInt(); string s = reader.GetString(); reader.Recycle(); }
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType t) { reader.Recycle(); }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnConnectionRequest(ConnectionRequest request) { request.AcceptIfKey("ATLAS-1"); }
}
```

| Member | Signature | Source | Gotcha |
|---|---|---|---|
| `Start` | `bool Start()` (any port), `Start(int port)`, `Start(IPAddress v4, IPAddress v6, int port)`, `Start(string v4, string v6, int port)` | `LN/LiteNetManager.cs:1189-1218` | Returns false if already running or the bind fails. Starts two background threads: `ReceiveThread(<port>)` and `LogicThread` (`Socket.cs:381-394`). Registers `Application.focusChanged` and `Application.quitting` handlers (`PausedSocketFix.cs:23-24`), and `Stop()` is called on quit. |
| `Connect` | `NetPeer Connect(string address, int port, string key)`; also `(…, NetDataWriter)`, `(IPEndPoint, string)`, `(IPEndPoint, NetDataWriter)`, `(IPEndPoint, ReadOnlySpan<byte>)` | `LN/NetManager.cs:358-399` | **Throws `InvalidOperationException("Client is not running")` before `Start()`** (`LiteNetManager.cs:1399`). Returns the existing peer if already connected or connecting, `null` if a request is pending. A hostname resolves **synchronously on the caller** (DNS, IPv6 first, `NetUtils.cs:47-66`); a bad host raises a Disconnect event `UnknownHost` and returns `null` (`LiteNetManager.cs:1364-1374`). Pass an IP literal. |
| `PollEvents` | `void PollEvents()` | `:1316` | Dispatches queued events on the caller thread. |
| `Stop` | `void Stop()`, `Stop(bool sendDisconnectMessages)` | `:1472-1479` | Joins the logic thread; peers are cleared. |
| Server side | `OnConnectionRequest(ConnectionRequest r)`: `AcceptIfKey(string)`, `Accept()`, `Reject()` | `LN/ConnectionRequest.cs:65,90,158` | A request you neither accept nor reject stays pending. |
| `EventBasedNetListener` | events `PeerConnectedEvent`, `PeerDisconnectedEvent`, `NetworkErrorEvent`, `NetworkReceiveEvent`, `NetworkReceiveUnconnectedEvent`, `NetworkLatencyUpdateEvent`, `ConnectionRequestEvent`, `DeliveryEvent`, `NtpResponseEvent`, `PeerAddressChangedEvent` | `LN/INetEventListener.cs:225-348` | Allocates delegates; `INetEventListener` is the lighter option (its doc says so, `:77`). |
| `ILiteNetEventListener` / `LiteNetManager` | channel-less base API (`OnNetworkReceive(LiteNetPeer, NetPacketReader, DeliveryMethod)`) | `:156-220` | Do not mix with `NetManager`. |

### 3.4 Sending

`DeliveryMethod` values (`LN/NetConstants.cs:11-26`): `Unreliable = 4`, `ReliableUnordered = 0`, `Sequenced = 1`, `ReliableOrdered = 2`, `ReliableSequenced = 3`.

| Overload on `NetPeer` | Source |
|---|---|
| `Send(NetDataWriter, byte channelNumber, DeliveryMethod)` | `LN/NetPeer.cs:45` |
| `Send(byte[], byte channelNumber, DeliveryMethod)` | `:152` |
| `Send(ReadOnlySpan<byte>, byte channelNumber, DeliveryMethod)` | `:166` |
| `Send(byte[], int start, int length, byte channelNumber, DeliveryMethod)` | `:182` |
| `SendWithDeliveryEvent(… , object userData)` (same four shapes) | `:58-111`, raises `DeliveryEvent` / `OnMessageDelivered`; reliable methods only |
| channel-0 forms `Send(byte[]/NetDataWriter/ReadOnlySpan<byte>, DeliveryMethod)` (inherited) | `LN/LiteNetPeer.cs:545-602` |
| `NetManager.SendToAll(NetDataWriter, byte channel, DeliveryMethod[, LiteNetPeer exclude])` | `LN/NetManager.cs:237-333` |

Gotchas:
- **`Send` silently does nothing** if the peer is not `Connected` or `channelNumber >= ChannelsCount` (`LiteNetPeer.cs:611`).
- **`Unreliable` has no channel**: it is sent without a channel byte and received as channel 0 (`LiteNetPeer.cs:618-621,1147`).
- Too big for one packet: `Unreliable`, `Sequenced` and `ReliableSequenced` throw `TooBigPacketException` ("Unreliable or ReliableSequenced packet size exceeded…", `:636-640`); only `ReliableOrdered` and `ReliableUnordered` are fragmented (up to `MaxFragmentsCount`). `ReliableSequenced` packets are dropped by the base-class receive path (`LiteNetPeer.cs:1051-1055`) but handled by `NetPeer`'s channels.
- The data is copied into a pooled packet before `Send` returns (`LiteNetPeer.cs:672-675`), so a `NetDataWriter` can be `Reset()` and reused immediately. `Send` looks designed to be called from other threads (`_unreliableChannelLock` at `LiteNetPeer.cs:679`, a `ConcurrentQueue` of channels at `NetPeer.cs:14`), but the docs do not promise it.
- `peer.RoundTripTime`, `peer.Ping` (= RTT/2), `peer.Mtu`, `peer.ConnectionState` (flags `Outgoing, Connected, ShutdownRequested, Disconnected, EndPointChange`), `peer.Tag`, `peer.Address`/`Port` (`LiteNetPeer : IPEndPoint`, `LiteNetPeer.cs:53,181-224`).

### 3.5 `NetDataWriter` / `NetDataReader` ✔

`NetDataWriter` (`LN/Utils/NetDataWriter.cs`): `Put(int/uint/long/ulong/short/ushort/byte/sbyte/float/double/bool/char/Guid/string/IPEndPoint/byte[]/ReadOnlySpan<byte>)`, `PutBytesWithLength`, `PutArray(…)`, `Put<T>(T) where T : INetSerializable`, `Reset()` (`:178`), `Data`, `Length`, `AsReadOnlySpan()`, static `FromString` (`:120`). `NetPacketReader : NetDataReader` (`LN/NetPacketReader.cs`): `GetInt/GetFloat/GetString/GetBytesWithLength/GetRemainingBytes/GetRemainingBytesSpan`, `AvailableBytes`, `EndOfData`, `Recycle()`.
- A reader is valid only until `Recycle()` / return from the handler (with `AutoRecycle = true` it is recycled for you, `NetManager.cs:201-204`). Copy what you keep.
- `NetPacketReader` handed to `OnPeerDisconnected` as `info.AdditionalData` follows the same rule.

### 3.6 Threading and manual mode

- Normal mode: two background threads (`LiteNetManager.cs:498` `UpdateLogic`, `Socket.cs` `ReceiveLogic`); events queue until `PollEvents()` (`:405-413,1316-1340`).
- `StartInManualMode(int port)` / `(IPAddress, IPAddress, int)` / `(string, string, int)` (`:1230-1256`): **no threads**. You call `PollEvents()` (it reads the socket, `:1318-1324`) and `ManualUpdate(float elapsedMilliseconds)` (`:600`) every frame; events fire inline inside them (`:399`, `:1016`). `ManualUpdate` returns immediately if not started in manual mode.
- A listener callback runs on whichever thread called `PollEvents` (main, by default). `NetDebug.WriteError` logs through `UnityEngine.Debug.Log` from LiteNetLib's threads (`NetDebug.cs:51`).

### 3.7 iOS, IL2CPP and Unity notes in the sources

- `PausedSocketFix` (`LN/PausedSocketFix.cs:44-62`) restarts the sockets on focus return only when a receive error flagged `NotConnected` (`Socket.cs:35,78`); it does not re-handshake peers. Expect `OnPeerDisconnected` after backgrounding and reconnect from your code.
- `BindSocket` swallows the `ReuseAddress` exception: "Unity with IL2CPP throws an exception here, it doesn't matter" (`Socket.cs:429`), and has an "AddressFamilyNotSupported" iOS hack (`:497`).
- `NetSerializer` / `NetPacketProcessor` use reflection on properties (`Utils/NetSerializer.cs:587`); with stripping Low they need `[Preserve]` (`LiteNetLib.Utils.PreserveAttribute`, `Utils/Preserve.cs`) or avoid them: write with `NetDataWriter` / `INetSerializable`.
- `FastBitConverter` uses `UnsafeUtility.MemCpy` on Android only (`Utils/FastBitConverter.cs:3,32`).
- `LiteNetLib.dll` is **not in the IL2CPP output** of the existing `Build/iOS` (`il2cppOutput` has no LiteNetLib file: no code used it yet, so it was stripped). `docs/versions.md` still lists IL2CPP on device as unverified.
- The plist already carries `NSLocalNetworkUsageDescription` (`Build/iOS/Info.plist:49`); the iOS Local Network prompt itself is an OS behaviour not visible in these sources (device check).

## 4. glTFast 6.20.0

### 4.1 Assembly

| What | Value | Source |
|---|---|---|
| asmdef to reference | `glTFast` (root namespace `GLTFast`). A loader that only uses `GltfImport` compiled with just `glTFast` referenced ✔; an `IInstantiator` implementation also needs `Unity.Collections` (`NativeArray<Vector3>?` in `AddPrimitiveInstanced`) | `GF/Runtime/Scripts/glTFast.asmdef:2` |
| Needs installed | URP, Burst, Collections, Mathematics (all present). Draco, KTX, meshopt are **not** installed | `GF/package.json`, asmdef `references` |
| JSON parser | `GltfImport` uses `JsonUtility` (`GltfJsonUtilityParser`); the `glTFast.Newtonsoft` assembly exists and is in the player but is not used by `GltfImport` | `GltfImport.cs:64-80` |
| Unity-object ambiguity | `using GLTFast.Schema;` together with `using UnityEngine;` makes `Mesh` and `Material` ambiguous: add `using Mesh = UnityEngine.Mesh;` | compile test |

### 4.2 Load, get meshes, no scene instantiation ✔

```csharp
using GLTFast; using GLTFast.Logging; using GLTFast.Materials; using GLTFast.Schema;
using Material = UnityEngine.Material; using Mesh = UnityEngine.Mesh;

// Gives every glTF material a private copy, so GltfImport.Dispose() never destroys the shared look material.
public sealed class AtlasMaterialGenerator : IMaterialGenerator
{
    readonly Material _shared;
    public AtlasMaterialGenerator(Material shared) { _shared = shared; }
    public Material GetDefaultMaterial(bool pointsSupport = false) => new Material(_shared);
    public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false) => new Material(_shared);
    public void SetLogger(ICodeLogger logger) { }
}

public static async Task<Mesh[]> LoadMeshes(byte[] glb, Material shared, CancellationToken ct)
{
    var gltf = new GltfImport(
        deferAgent: new UninterruptedDeferAgent(),                  // or the budgeted agent, §4.4
        materialGenerator: new AtlasMaterialGenerator(shared),
        logger: new ConsoleLogger());
    bool ok = await gltf.Load(glb, null, new ImportSettings(), ct); // byte[] overload
    if (!ok) { gltf.Dispose(); return System.Array.Empty<Mesh>(); }
    var result = new List<Mesh>();
    foreach (Mesh m in gltf.Meshes) result.Add(UnityEngine.Object.Instantiate(m)); // copy: Dispose destroys the originals
    gltf.Dispose();
    return result.ToArray();
}
```

| Member | Signature | Source | Gotcha |
|---|---|---|---|
| ctor | `GltfImport(IDownloadProvider downloadProvider = null, IDeferAgent deferAgent = null, IMaterialGenerator materialGenerator = null, ICodeLogger logger = null)` | `GF/…/GltfImport.cs:69` | **With no defer agent it creates a hidden `DontDestroyOnLoad` GameObject "glTF-StableFramerate"** (`:306-312`). A null `materialGenerator` builds the URP generator, which looks shaders up with `Shader.Find` (`MaterialGenerator.cs:222-252`, `:301`). **No logger means no logging at all** (`GF/Documentation~/ImportRuntime.md:154`); `ConsoleLogger` and `CollectingLogger` are public (`GLTFast.Logging`). |
| `Load` | `Task<bool> Load(byte[] data, Uri uri = null, ImportSettings importSettings = null, CancellationToken cancellationToken = default)`; also `Load(string url, …)`, `Load(Uri, …)`, `Load(NativeArray<byte>.ReadOnly, …)`, `LoadFile(string, …)`, `LoadStream(Stream, …)`, `LoadGltfJson(string, …)` | `:385-499,636` | Detects GLB by magic (`:456`). Returns false if `extensionsRequired` names an unsupported extension (`:1560-1567`): Draco, KTX2/Basis and meshopt GLBs fail. Textures are queued in `LoadContent` and awaited in `Prepare`, and materials are generated for every glTF material, whatever the generator does (`:2377-2405`, `GenerateMaterials` `:2712`), so ship mesh-only GLBs (the Forge contract already does). |
| `LoadGltfBinary(byte[], …)` | `[Obsolete("Use the generic Load instead.")]` | `:608-609` | Warning, not an error. |
| `Meshes` | `IReadOnlyCollection<UnityEngine.Mesh> Meshes` | `:1007` | `GetMeshes()` (array) is obsolete (`:997`). Per glTF mesh: `GetMeshCount(int meshIndex)`, `GetMeshes(int meshIndex)`, `GetMesh(int meshIndex, int meshNumeration)` (`:1015-1043`). One glTF mesh may become several Unity meshes (one per distinct primitive layout). |
| `InstantiateMainSceneAsync` | `Task<bool> InstantiateMainSceneAsync(Transform parent, CancellationToken = default)`; `(IInstantiator, CancellationToken = default)` | `:710-755` | Instantiates nothing and returns true if the glTF has no default scene (`:737-745`). `InstantiateMainScene` / `InstantiateScene` (sync) are `[Obsolete]` and block on `.Result`. |
| `IInstantiator` | `BeginScene`, `CreateNode(…, string name)`, `AddMesh(uint nodeIndex, string meshName, MeshResult, uint[] joints = null, uint? rootJoint = null, float[] morphTargetWeights = null, int meshNumeration = 0)`, `AddMeshInstanced`, `AddCamera`, `AddLightPunctual`, `AddAnimation(AnimationClip[])`, `EndScene` | `GF/…/IInstantiator.cs` | `AddAnimation` is in the compiled interface although the source guards it with `UNITY_ANIMATION`; implement it unconditionally. `MeshResult` carries `mesh`, `meshIndex`, `materialIndices` (`MeshResult.cs`). `AddPrimitive` is the obsolete name of `AddMesh`. |
| `Dispose` | `void Dispose()` | `:822-848` | **Destroys all meshes, materials and textures it made, even ones already given to your scene.** Nothing is accessible afterwards. |
| `LoadingDone`, `LoadingError` | `bool` | `:279,284` | |

Main thread only (inferred: `Load` creates `UnityEngine.Mesh`, `Texture2D` and `Material` objects, e.g. `MeshGenerator.cs:439`, and its `await`s resume on the caller's synchronization context; the package does not state it). Read the file and verify its SHA-256 off-thread, then call `Load` on the main thread (Master Plan §7.5 rule 2 lists "GLB parsing" among off-main-thread work: it cannot be). The heavy decode runs in Burst jobs inside glTFast.

Meshes and CPU access: the GLB is parsed into a `new Mesh` (`MeshGenerator.cs:439`) and `UploadMeshData(false)` is called only under `#if GLTFAST_KEEP_MESH_DATA` (`:537-541`). No code path calls `UploadMeshData(true)`, yet the package doc says data is discarded by default and `GLTFAST_KEEP_MESH_DATA` keeps it (`GF/Documentation~/ProjectSetup.md:84-88`). Source and doc disagree: **check `mesh.isReadable` on device** before using `mesh.vertices` / `triangles`, or add the scripting define for certainty.

Handedness: glTF right-handed to Unity left-handed is done by inverting the X axis (`GF/Documentation~/UpgradeGuides.md:222`). The Forge's "yaw in {0, 90, 180, 270}" is therefore mirrored in Unity; test with an asymmetric asset.

### 4.3 `ImportSettings`

`GLTFast.ImportSettings` (`ImportSettings.cs:16`): `NodeNameMethod` (default `NameImportMethod.Original`), `AnimationMethod` (default `AnimationMethod.Legacy`), `GenerateMipMaps` (false), `TexturesReadable` (false), `DefaultMinFilterMode` / `DefaultMagFilterMode` (Linear), `AnisotropicFilterLevel` (1). There is no mesh-readability option.

### 4.4 Frame budget ✔

| Type | Signature | Source | Gotcha |
|---|---|---|---|
| `IDeferAgent` | `bool ShouldDefer()`, `bool ShouldDefer(float duration)`, `Task BreakPoint()`, `Task BreakPoint(float duration)` | `GF/…/IDeferAgent.cs:15` | `ShouldDefer` must eventually return false or the import stalls silently. |
| `UninterruptedDeferAgent` | plain class, never defers | `UninterruptedDeferAgent.cs:14` | Can block the main thread for seconds (`ImportRuntime.md:185`). |
| `TimeBudgetPerFrameDeferAgent` | `MonoBehaviour`; `void SetFrameBudget(float newFrameBudget = 0.5f)` | `TimeBudgetPerFrameDeferAgent.cs:16,32` | Create with `AddComponent`. Budget = fraction ÷ `Application.targetFrameRate`, and **30 is assumed when that is negative** (`:41`); call `SetFrameBudget` after you set the frame rate. It does not bound texture uploads (`ImportRuntime.md:187`). |
| `GltfImport.SetDefaultDeferAgent(IDeferAgent)` / `UnsetDefaultDeferAgent` | static | `GltfImport.cs:331,347` | Global default for later `GltfImport`s. |

```csharp
var agent = host.AddComponent<TimeBudgetPerFrameDeferAgent>();
agent.SetFrameBudget(0.25f);
var gltf = new GltfImport(deferAgent: agent, materialGenerator: gen, logger: new ConsoleLogger());
```

## 5. UnityWebRequest ✔

Module `UnityEngine.UnityWebRequestModule` (`com.unity.modules.unitywebrequest` is in the manifest; the module is in the player). Namespace `UnityEngine.Networking`.

```csharp
using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
req.uploadHandler   = new UploadHandlerRaw(json) { contentType = "application/json" };
req.downloadHandler = new DownloadHandlerBuffer();
req.timeout = 10;                                   // seconds
await req.SendWebRequest();                         // AsyncOperation is awaitable via AsyncOperationAwaitableExtensions.GetAwaiter
bool ok = req.result == UnityWebRequest.Result.Success;
byte[] body = ok ? req.downloadHandler.data : null; // req.responseCode for the HTTP status
```

| Member | Signature | Source | Gotcha |
|---|---|---|---|
| `SendWebRequest` | `UnityWebRequestAsyncOperation SendWebRequest()`; `.webRequest`, `.completed` | `xml:UnityWebRequestModule:1050`; `meta` | `Send()` is `[Obsolete]`. `await` works through the `UnityEngine.AsyncOperationAwaitableExtensions.GetAwaiter(AsyncOperation)` extension (`meta:CoreModule`); or `yield return` in a coroutine, or `Awaitable.FromAsyncOperation(op, ct)` (`xml:CoreModule:2006`). |
| `timeout` | `int`, seconds | `xml:UnityWebRequestModule:537` | "Seconds after which it attempts to abort if no response is received". Set it. |
| `result` | `UnityWebRequest.Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }` | `:532,1017-1041` | `ProtocolError` = the server answered with an HTTP error. `isNetworkError` / `isHttpError` / `isError` are `[Obsolete]` (warnings). |
| `UploadHandlerRaw` | `UploadHandlerRaw(byte[] data)`, `(NativeArray<byte> data, bool transferOwnership)`, `(NativeArray<byte>.ReadOnly)`; base `UploadHandler.contentType` | `xml:UnityWebRequestModule:1143` | `contentType` is a property of the base `UploadHandler`. |
| `DownloadHandlerBuffer` | `.data` (`byte[]`), `.text`, `.nativeData` (`NativeArray<byte>.ReadOnly`) | `xml:UnityWebRequestModule:120`; `meta` | Read the body before the request is disposed. |
| Multipart | `UnityWebRequest.Post(string uri, List<IMultipartFormSection> sections)`, `(…, byte[] boundary)`; `MultipartFormDataSection(string name, byte[] data, string contentType)` and string forms; `MultipartFormFileSection(string name, byte[] data, string fileName, string contentType)` | `xml:UnityWebRequestModule:866,329` | The `Post(string uri, string postData)` form is `[Obsolete]`; use `PostWwwForm` or the content-type form. `WWWForm` is also present (`AddBinaryData`). |
| `UnityWebRequest.Put(string uri, byte[] bodyData)` | | `:964` | |

Use `UnityWebRequest` from the main thread. `NSAllowsLocalNetworking` is set (`Build/iOS/Info.plist:44`), the key that lets plain-`http://` LAN requests through App Transport Security (Apple behaviour, not visible in these sources).

## 6. Deep links, prefs, paths, permissions

| Member | Signature (module) | Source | Gotcha |
|---|---|---|---|
| `Application.deepLinkActivated` | `static event Action<string>` (CoreModule) | `xml:CoreModule:478`, `meta` | No description in the XML. Native side: `UnitySetAbsoluteURL` is called for a cold-start URL before the first scene's `Awake` (`Build/iOS/Classes/UI/UnityScene.mm:69-76`), for a URL opened while running (`UnityScene.mm:115`, `UnityAppController.mm:355`) and for universal links (`:368,379`), then `kUnityOnOpenURL` is posted. Whether the managed event fires on a warm open is not stated in these sources: handle both `absoluteURL` at start and the event. |
| `Application.absoluteURL` | `static string` | `xml:CoreModule:443` | "For Android, iOS or UWP this is a deep link URL". Empty when launched normally. |
| URL scheme | `atlas://<ip>:9050` registered by `AtlasPostBuild.cs:31-34` | `Build/iOS/Info.plist:27-37` | `new Uri("atlas://192.168.1.20:9050")` gives `Host` 192.168.1.20, `Port` 9050 (checked on .NET 8, not on IL2CPP). `atlas:192.168.1.20:9050` (no `//`) parses with an empty host. |
| `Application.persistentDataPath` | `static string` | `xml:CoreModule:610`; `meta` shows it is **not** flagged thread-safe (only `streamingAssetsPath` is) | Read it once on the main thread and cache the string before background file work. |
| `PlayerPrefs` | `SetString/GetString/SetInt/GetInt/SetFloat/GetFloat/HasKey/DeleteKey/DeleteAll/Save()` | `xml:CoreModule:31885-31904` | No thread-safe flag on any of them (`meta`): treat as main-thread only. The XML says only "Saves all modified preferences" for `Save()`; call it after writing something you cannot lose. |
| Camera permission (Unity) | `static AsyncOperation Application.RequestUserAuthorization(UserAuthorization.WebCam)`; `static bool Application.HasUserAuthorization(UserAuthorization.WebCam)` | `xml:CoreModule:889,778`: "Request authorization to use the webcam or microphone on iOS" | `UserAuthorization { WebCam, Microphone }`. `NSCameraUsageDescription` is set (`Info.plist:47`). |
| Camera permission (AR) | `ARCameraManager.permissionGranted : bool` (`Unity.XR.ARFoundation`, namespace `UnityEngine.XR.ARFoundation`) | `com.unity.xr.arfoundation@13d2457b468b/Runtime/ARFoundation/Camera/ARCameraManager.cs:214`; ARKit: `ARKitCameraSubsystem.cs:227` | The AR session owns the camera; read this rather than opening a `WebCamTexture`. |

Coroutine form ✔: `if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);`

## 7. JPEG encoding and threads ✔

Module `UnityEngine.ImageConversionModule`, class `UnityEngine.ImageConversion` (`com.unity.modules.imageconversion` in manifest).

| Member | Signature | Thread-safe? (evidence) |
|---|---|---|
| `EncodeToJPG` | `static byte[] EncodeToJPG(this Texture2D tex)` and `(this Texture2D tex, int quality)` | **No**: takes a `Texture2D`, no thread-safe flag (`meta:ImageConversionModule`: `NativeMethod(IsFreeFunction=True, ThrowsException=True)`). Main thread. `xml:ImageConversionModule:115` |
| `EncodeArrayToJPG` | `static byte[] EncodeArrayToJPG(Array array, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, int quality = 75)` (`UnityEngine.Experimental.Rendering.GraphicsFormat`) | **Yes**: bound as `FreeFunction("ImageConversionBindings::EncodeArrayToJPG", isThreadSafe: true)`. `xml:ImageConversionModule:28`. Safe on a worker thread with a managed `byte[]`. |
| `EncodeNativeArrayToJPG<T>` | `static NativeArray<byte> EncodeNativeArrayToJPG<T>(NativeArray<T> input, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, int quality = 75) where T : struct` | **Yes**: wraps `UnsafeEncodeNativeArrayToJPG`, flagged thread-safe. `xml:ImageConversionModule:70`. The XML does not state the allocator of the result; treat it as yours to `Dispose()`. |

Quality range 1..100, default 75 (`xml:…:28`). Use `GraphicsFormat.R8G8B8A8_SRGB` for RGBA32 camera crops ✔. `EncodeToJPG` cannot serve the "JPEG encoding off the main thread" rule (Master Plan §7.5 rule 2); copy pixels to a `byte[]` on the main thread, then call `EncodeArrayToJPG` on a worker.

## 8. Audio and haptics

### 8.1 Procedural audio ✔

Module `UnityEngine.AudioModule`.

```csharp
public static AudioClip Tick(int sr = 48000)
{
    int n = sr / 100;                                    // 10 ms
    var d = new float[n];
    for (int i = 0; i < n; i++) d[i] = Mathf.Sin(2f * Mathf.PI * 1800f * i / sr) * Mathf.Exp(-6f * i / n);
    var c = AudioClip.Create("tick", n, 1, sr, false);   // 5-argument form, not streamed
    c.SetData(d, 0);
    return c;
}
```

| Member | Signature | Source | Gotcha |
|---|---|---|---|
| `AudioClip.Create` | `static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)`; the overloads with a `_3D` bool are `[Obsolete]` ("use `spatialBlend` of AudioSource") | `xml:AudioModule:1470`, `meta` | `lengthSamples` is frames, not samples × channels. |
| `AudioClip.SetData` | `bool SetData(float[] data, int offsetSamples)`, `(ReadOnlySpan<float>, int)` | `xml:AudioModule:1616` | Returns false for a bad offset or empty data. Generate once and cache the clip; do not allocate per frame. |
| `AudioSource` | `spatialBlend` (0 = 2D, 1 = 3D), `panStereo` (−1..1), `minDistance`, `maxDistance`, `rolloffMode`, `PlayOneShot(AudioClip[, float volumeScale])` | `xml:AudioModule:2527,2492` | `panStereo` applies only to mono or stereo sources: for a left/right cue keep `spatialBlend = 0`; for a source in the room use `spatialBlend = 1` and a position relative to the `AudioListener` (put the listener on the tracked head). |
| `AudioSettings.Mobile.muteState` | `static bool` | `xml:AudioModule:2345` | "True if current device **media volume is 0**" (not the ring switch). `stopAudioOutputOnMute`, `StartAudioOutput()`, `StopAudioOutput()` also exist. |

### 8.2 Haptics

- `Handheld.Vibrate()` : `static void` (`UnityEngine.CoreModule`, `xml:CoreModule:17208`, "Triggers device vibration"). No strength, duration or pattern parameter.
- Neither CoreHaptics nor UIKit feedback generators are used by the engine: `libiPhone-lib.a` has no `UIImpactFeedbackGenerator`, `CoreHaptics` or `CHHapticEngine` strings, and the Xcode project links neither `CoreHaptics.framework` nor a haptics plugin (`Build/iOS/Unity-iPhone.xcodeproj`). `docs/versions.md` lists apple/unityplugins (Core + CoreHaptics) as "not added yet".
- Pattern to follow is the thermal bridge, which built into the export (`Build/iOS/Libraries/Plugins/iOS/AtlasThermal.mm` was copied into the Xcode project):

`Assets/Plugins/iOS/AtlasThermal.mm`
```objc
#import <Foundation/Foundation.h>
extern "C" int AtlasThermalState(void) { return (int)[NSProcessInfo processInfo].thermalState; }
```
`Assets/Atlas/Rendering/AtlasThermal.cs` (existing)
```csharp
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
public static class AtlasThermal
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int AtlasThermalState();
    public static int State => AtlasThermalState();
#else
    public static int State => 0;
#endif
}
```
Same pattern for haptics ✔ (the C# side compiled; the `.mm` side is a **proposal, not compiled here**: check in Xcode):
```csharp
#if UNITY_IOS && !UNITY_EDITOR
[DllImport("__Internal")] static extern void AtlasHapticImpact(int style, float intensity);   // 0 light .. 4 rigid
public static void Impact(int style, float intensity) => AtlasHapticImpact(style, intensity);
#else
public static void Impact(int style, float intensity) { }
#endif
```
```objc
#import <UIKit/UIKit.h>   // UIKit is already linked (Build/iOS project)
extern "C" void AtlasHapticImpact(int style, float intensity)
{
    UIImpactFeedbackGenerator* g = [[UIImpactFeedbackGenerator alloc] initWithStyle:(UIImpactFeedbackStyle)style];
    [g impactOccurredWithIntensity:intensity];
}
```
If CoreHaptics (`CHHapticEngine`) is wanted, add the framework in `AtlasPostBuild.cs`: `project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "CoreHaptics.framework", false)` (`UnityEditor.iOS.Xcode.PBXProject.AddFrameworkToProject(string targetGuid, string framework, bool weak)` ✔ exists). The `.mm` needs a `.meta`, which the repo tools generate (`Assets/Plugins/iOS/AtlasThermal.mm.meta` holds only a guid). Foundation F9 requires native plugins to sit behind C# wrappers that return neutral values off-device: the `#else` branch does.

## 9. UI in code

| Option | Status with `activeInputHandler = 1` | asmdef / module | Notes |
|---|---|---|---|
| IMGUI (`OnGUI`, `GUI`, `GUILayout`) | **Draws, but gets no input events at runtime** (`ISdoc/use-imgui-alongside-input-system.md:10`). Only "Both" restores it. Not usable for touch screens | `UnityEngine.IMGUIModule` (`com.unity.modules.imgui`) | Fine for a read-only debug HUD. |
| **UI Toolkit runtime (recommended for full-screen touch UI built in code)** | Pointer and touch input work through `Unity.InputSystem.ForUI`, no EventSystem, no `.inputactions` asset (§2.9) | `UnityEngine.UIElementsModule` (engine module, no asmdef reference) | `PanelSettings` and `UIDocument` are created in code ✔ (below). |
| uGUI | Works with `InputSystemUIInputModule` (never `StandaloneInputModule`) ✔ | asmdef `UnityEngine.UI` (+ `Unity.InputSystem`, + `Unity.TextMeshPro` for text) | Needs an `EventSystem` GameObject. `GraphicRaycaster` on the Canvas. |

UI Toolkit in code ✔:
```csharp
var go = new GameObject("OnboardingUI");
var doc = go.AddComponent<UIDocument>();
var ps = ScriptableObject.CreateInstance<PanelSettings>();
ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
ps.referenceResolution = new Vector2Int(1920, 1080);
ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; ps.match = 0.5f;
ps.sortingOrder = 10;
doc.panelSettings = ps;
VisualElement root = doc.rootVisualElement;
root.style.flexGrow = 1; root.style.backgroundColor = new StyleColor(Color.black);
var label = new Label("Welcome"); label.style.color = new StyleColor(Color.white); label.style.fontSize = 48;
var start = new Button(() => { }) { text = "Start" };
root.Add(label); root.Add(start);
```
- `PanelSettings.themeStyleSheet` is public (`xml:UIElementsModule:16118`) but nothing assigns a default in a player: the string "No Theme Style Sheet set to PanelSettings" is in `UnityEngine.UIElementsModule.dll`, and its suppress flag `disableNoThemeWarning` is internal. Expect that warning; set every colour, size and font on `element.style` yourself rather than relying on a theme. Not run: check that Label and Button render and receive touches on the phone.
- Orientation is landscape-right only (§1): reference resolution 1920×1080.
- uGUI screen in code ✔: `new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule))`, `new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))`, `CanvasScaler.uiScaleMode = ScaleWithScreenSize`, `Button` + `Image` + child `TextMeshProUGUI`.

### 9.1 Packages

| Package | Version / source | asmdef names | Note |
|---|---|---|---|
| uGUI | `com.unity.ugui` 2.0.0, source "builtin", **transitive** (`Packages/packages-lock.json:273-281`), not in `manifest.json` | `UnityEngine.UI` (`UGUI/UGUI/UnityEngine.UI.asmdef`), `Unity.TextMeshPro` (`UGUI/TMP/Unity.TextMeshPro.asmdef`; references `UnityEngine.UI` by GUID) | TMP lives inside this package; both assemblies are in the player (`Build/iOS/Data/ScriptingAssemblies.json`). |
| TMP use from an asmdef | | reference **both** `Unity.TextMeshPro` and `UnityEngine.UI` | Compiling `AddComponent<TextMeshPro>()` with only `Unity.TextMeshPro` fails with CS0012 (`MaskableGraphic` in `UnityEngine.UI`) ✔ tested. |

### 9.2 World-space text in the headset ✔

```csharp
var go = new GameObject("label"); go.transform.SetParent(parent, false);
var t = go.AddComponent<TextMeshPro>();            // namespace TMPro; adds MeshRenderer ([RequireComponent], UGUI/TMP/TextMeshPro.cs:17)
t.text = "Hello"; t.fontSize = 1.5f; t.alignment = TextAlignmentOptions.Center;
t.textWrappingMode = TextWrappingModes.NoWrap;     // enableWordWrapping is [Obsolete] (TMP_Text.cs:754)
t.color = Color.white; t.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
```
- **Default font exists**: `Assets/TextMesh Pro/Resources/TMP Settings.asset:27` points at `LiberationSans SDF.asset` (guid `8f586378…`), with `Resources/Fonts & Materials/` (SDF, Drop Shadow, Outline, Fallback) and `Resources/Style Sheets`, `Sprite Assets`. `TextMeshPro.LoadFontAsset` falls back to `TMP_Settings.defaultFontAsset` when none is assigned (`UGUI/TMP/TextMeshPro.cs:953-956`); `TMP_Settings.instance` loads `Resources.Load<TMP_Settings>("TMP Settings")` (`TMP_Settings.cs:455`).
- Coverage: `LiberationSans SDF.asset` is a static atlas (`m_AtlasPopulationMode: 0`) with 250 characters: all ASCII 32-126, `° · — – … × ÷ ± ’ ‘ “ ” •`, and **not** `→ ← ↑ ↓ ✓`. Its fallback asset `LiberationSans SDF - Fallback.asset` is dynamic (`m_AtlasPopulationMode: 1`, source font `Assets/TextMesh Pro/Fonts/LiberationSans.ttf`); whether Liberation Sans has the arrow glyphs was not checked.
- The default material uses shader `TextMeshPro/Mobile/Distance Field` (`TMP_SDF-Mobile.shader`, guid `fe393ace…` in the font asset). TMP writes `_CullMode` on the shared font material (`TextMeshPro.cs:969-970`) and draws with its own shader, a second one next to `Atlas/Surface` (Foundation F7 allows only `Atlas/Surface` and forbids `MaterialPropertyBlock` in `Atlas.*`; TMP's own code is outside `Atlas.*`). Either record an exception for TMP or keep TMP to onboarding and debug text. Not checked: how this shader renders under URP on device.
- Legacy `TextMesh` (`UnityEngine.TextMesh`, `UnityEngine.TextRenderingModule`, `xml:TextRenderingModule:615`; Unity itself says TMP is preferred) needs a `Font` and `MeshRenderer.sharedMaterial = font.material`. Built-in font: `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` is what uGUI itself uses (`UGUI/UGUI/UI/Core/Text.cs:594,600`) ✔; `Font.CreateDynamicFontFromOSFont(string, int)` also exists (`xml:TextRenderingModule:148`).

## 10. `Resources.Load<TextAsset>`

`static T Resources.Load<T>(string path) where T : Object` (`meta:CoreModule`; `LoadAsync` returns `ResourceRequest`). `TextAsset` has `.text`, `.bytes`, `.GetData()` (`xml:CoreModule:57490`). Example: `Resources.Load<TextAsset>("AtlasRooms/budget_room")` ✔ (the fixture location Foundation F8 plans, `Assets/Atlas/World/Resources/AtlasRooms/*.json`). The path is relative to any `Resources/` folder, **without extension**; text-like files (for example `.txt`, `.json`, `.bytes`) import as `TextAsset` (Unity behaviour, not checked in these sources). Existing `Resources` folders: `Assets/TextMesh Pro/Resources`, `Assets/XR/Resources`, `Assets/XR/UserSimulationSettings/Resources`, `Assets/XRI/Settings/Resources`; none under `Assets/Atlas`. No thread-safe flag in the bindings (`meta`): load on the main thread. The plan keeps downloaded files under `persistentDataPath/assets/<sha256>.glb` (Master Plan §7.4, line 1015); `UIFileSharingEnabled` is on (`Info.plist:68`).

## 11. JSON libraries

| Library | Available? | Evidence | What to use |
|---|---|---|---|
| `System.Text.Json` | **No** | The .NET Standard 2.1 reference surface has no such namespace: `~/.dotnet/packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1/netstandard.xml` has 0 occurrences and `netstandard.dll` has none of `System.Text.Json`, `Utf8JsonWriter`, `JsonDocument`. The IL2CPP output of the real build lists only `mscorlib, System, System.Core, System.Xml, System.Configuration, Mono.Security` for the BCL (`Build/iOS/Il2CppOutputProject/Source/il2cppOutput`). A `System.Text.Json.dll` exists in `Build/iOS/Il2CppOutputProject/IL2CPP/build/deploy_*`: that is the il2cpp compiler's own .NET 8 host (it sits next to `Bee.*.dll`), not a player assembly. | `Atlas.Link.AtlasJson` (Foundation F1) in engine-free code |
| `UnityEngine.JsonUtility` | Yes, `UnityEngine.JSONSerializeModule` (`com.unity.modules.jsonserialize`, in the manifest) | ✔ `JsonUtility.ToJson/FromJson<T>` compiled | `[Serializable]` classes with public or `[SerializeField]` fields; not usable from engine-free assemblies. |
| Newtonsoft Json | Yes, version **3.2.2** (Newtonsoft 13.0.2), but **not a direct dependency**: `Packages/manifest.json` does not list it. It arrives only through `com.coplaydev.unity-mcp` (`package.json:17`, `packages-lock.json:15`, depth 1). Removing the MCP package removes it, and its `Runtime/Newtonsoft.Json.dll` | The package has two DLLs with the same assembly name: `Runtime/Newtonsoft.Json.dll` (importer: **Editor only**) and `Runtime/AOT/Newtonsoft.Json.dll` (all player platforms including iOS, `iPhone: iOS enabled`); the player lists `Newtonsoft.Json.dll` (`Build/iOS/Data/ScriptingAssemblies.json`). No asmdef: reference it with `"overrideReferences": true, "precompiledReferences": ["Newtonsoft.Json.dll"]` (as `Atlas.Link` already overrides references), and add `com.unity.nuget.newtonsoft-json` to `manifest.json` before relying on it. Its `link.xml` preserves `System.ComponentModel` converters. | Prefer `JsonUtility` or `AtlasJson`; Newtonsoft only if a feature needs it. |

## 12. What only a device or the Editor can confirm

1. Gamepad rumble on iOS is a no-op (expected from §2.5): confirm with an MFi/Xbox/DualSense pad and `SetMotorSpeeds(1,1)`.
2. `Handheld.Vibrate()` behaviour on the phone, then the `.mm` haptic proposal in Xcode.
3. LiteNetLib on IL2CPP with stripping Low: connect, send 1 KB reliable fragmented, background and foreground the app, and check the Local Network prompt on a clean install.
4. glTFast: `Load` of a Forge GLB on the phone, `mesh.isReadable`, mirrored X, `Dispose` after copy.
5. UI Toolkit with no theme: Label and Button render and take lever touches; uGUI + TMP default font on device.
6. `Application.deepLinkActivated` on a warm open of `atlas://…`.
7. `Touchscreen` events for the lever's touch: duration (tap needs ≤ 0.2 s) and position.
