# Lane D · Link, Station and Forge contract

Task `P1-X08`, frozen at gate G0 (Master Plan §6.4, §7, §12.4, Appendix B, Appendix C.5 and C.7). Owner: lane D.

One file, five parts:

| Part | Fixes | Sections |
|---|---|---|
| [1 · Parameters](#part-1--parameters) | `ParamRegistry`, names and wire ids, the kill-switch list, `AtlasParamAttribute`, presets, `AtlasJson` | 1.1 – 1.8 |
| [2 · Link](#part-2--link) | Transport, channels, every message of Appendix B byte by byte, clock, watchdog, events, operator commands, `LinkClient`, `LinkHost`, `ReplayPhone` | 2.1 – 2.13 |
| [3 · Station HTTP](#part-3--station-http) | Hub process, relay rules, HTTP and WebSocket routes, log files, Console pages; Forge routes, job lifecycle, generator adapter, mesh clean-up | 3.1 – 3.10 |
| [4 · Assets](#part-4--assets) | GLB asset contract, validator checks, asset client, built-in fallback set | 4.1 – 4.4 |
| [5 · Telemetry](#part-5--telemetry) | Frame-loop step 8, `AtlasPerfProbe`, CSV columns, HUD, event log, pairing, on-device menu, Spectator, flat-twin mini-map | 5.1 – 5.9 |

Then: [6 · What lane D needs from the other lanes](#6--what-lane-d-needs-from-the-other-lanes),
[7 · Tests](#7--tests), [Open for the lead](#open-for-the-lead).

Marks: **(decided here)** = the plan left the point open and this page settles it. **(proposed)** = a number with
no measured source; confirmed or replaced at the gate that first checks it (§11).

---

## 0 · Conventions and files

- Wire format: little-endian. Byte 0 of every message is its id. `u8 u16 u32 i16 i64 f32 f64` are the usual fixed
  widths; `f32` and `f64` are IEEE 754.
- Units: metres, seconds, radians in code and on the wire. Degrees appear only in registry values meant for a
  person (README F4). Times on the wire are microseconds (`*_us`, `i64`) of the **sender's** monotonic clock.
- Poses on the wire: Unity convention (left-handed, Y up), quaternions `[x, y, z, w]`, in the room frame
  (README F4) once a RoomModel is active.
- `Atlas.Link` uses only `System` and LiteNetLib. No `UnityEngine` type, no `Unity.Mathematics` type (the Hub
  compiles the same sources with no Unity package). Vectors and matrices are plain `float` and `double` fields.
  Only `Transport/LiteNetTransport.cs` names a LiteNetLib type.
- Threading: every public member on this page is called on the main thread unless it says otherwise.
- Protocol version `proto` = `[1, 0]`. Connection key `atlas-v1`.
- Section numbers. `§1.1` to `§5.9`, `§6` and `§7` are sections of **this page**. Every other `§` is the Master
  Plan (`§6.2`, `§7.4`, `§8.6`, `§11.2`, `§12.4`, …); where a plan section falls in this page's range it is
  written `plan §4.5`. "B.1" to "B.4" and "C.5", "C.7" are the plan's appendices.
- Names of other lanes' types are those of their pages (`lane-a-platform.md`, `lane-b-kernel.md`,
  `lane-c-room.md`). §6 lists every one this page relies on.
- Unity and package facts (LiteNetLib, `UnityWebRequest`, deep links, TextMeshPro) are verified in
  `unity-api/input-net-assets.md`; this page cites it as "U-net".

| Area | Assembly | Namespace | Folder |
|---|---|---|---|
| Registry, JSON, messages, clock, watchdog, client, host, transports | `Atlas.Link` (engine-free) | `Atlas.Link` | `ATLAS/Assets/Atlas/Link/` |
| Installers, attribute binder, step 8 glue, perf probe, HUD, event log, asset client, pairing, flat twin, the Spectator's engine-free cores | `Atlas.Telemetry` | `Atlas.Telemetry` | `ATLAS/Assets/Atlas/Telemetry/` |
| `IEventLog` | `Atlas.World` (lane D file) | `Atlas.World` | `ATLAS/Assets/Atlas/World/Services/IEventLog.cs` |
| On-device menu | `Atlas.App` (lane D files) | `Atlas.App.Menu` | `ATLAS/Assets/Atlas/App/Menu/` |
| Spectator | `Atlas.Spectator` | `Atlas.Spectator` | `ATLAS/Assets/Atlas/Spectator/` |
| Tests | `Atlas.Link.Tests`, `Atlas.Telemetry.Tests`; the menu's in `Atlas.App.Tests` | same | `ATLAS/Assets/Atlas/Tests/Link/`, `…/Tests/Telemetry/`, `…/Tests/App/Menu/` |
| Hub and Console | .NET 8 web project `station/hub/Hub.csproj` (assembly `atlas-hub`) | `Atlas.Station` | `station/hub/` |
| Forge | Python 3.11 package `forge` | – | `station/forge/` |
| ReplayPhone | .NET 8 console | `Atlas.Tools` | `tools/ReplayPhone/` |
| Fallback-set and palette generators | Python scripts | – | `tools/gen_fallback_set.py`, `tools/gen_forge_palette.py` |

Files in `Atlas.Link`:

```
Link/Params/    ParamRegistry.cs (ParamKind, ParamSource, ParamChangedHandler, ParamHandle, ParamEntry, ParamRegistry)
Link/Json/      AtlasJson.cs  JsonValue.cs (JsonKind, JsonValue)
Link/Messages/  Wire.cs (MessageId, LinkProtocol, Wire)  StateMessage.cs
                BinaryMessages.cs (TPingMessage, TPongMessage, ParamSetMessage, TelemetryMessage,
                                   RoomOutlineBox, RoomOutlineMessage)
                JsonMessages.cs (LinkRole, LinkRoles, JsonMessage and the ten JSON messages, ParamManifestEntry, LinkEvents)
                TelemetryCsv.cs  WorldHash.cs
Link/Transport/ ILinkTransport.cs (LinkDelivery, ILinkTransportListener, ILinkPeerAddresses, ILinkTransport)
                LiteNetTransport.cs  LoopbackTransport.cs (LoopbackNetwork, LoopbackTransport)
Link/           LinkClient.cs  LinkHost.cs  OperatorCommand.cs
                ClockSync.cs (ILinkClock, StopwatchClock, ManualClock, ClockSync)
                LinkWatchdog.cs (LinkState, LinkWatchdog)
```

The same sources compile into `Atlas.Link` (C# 9, .NET Standard 2.1), into the Hub and into ReplayPhone
(`<Compile Include="../../ATLAS/Assets/Atlas/Link/**/*.cs" />`), so every byte layout exists once.

**In this build.** All four lanes' Phase 1 spaces and systems are built end to end, S2, S3 and S4 included. Their `State`
rows are in the table of §3 and their parameters in 1.5; lane B's page (§7.3) says what each writes.

---

# Part 1 · Parameters

Every tunable is one entry of one `ParamRegistry` (README F6). The composition root creates the one instance
(`AtlasBootContext.Params`, lane A §8.2 step 2), publishes it through `AtlasServices`, and SpaceLab hands it to
each space in `ISpaceModule.Configure`. The Spectator has its own instance. The Hub holds no registry: it
stores the manifest and values as data.

## 1.1 Names, wire ids and groups

A parameter has two identities.

| Identity | Type | Example | Used for |
|---|---|---|---|
| **Name** (`ParamEntry.Name`) | string | `render.scale` | Code, the contract pages, §12.2, §12.4, presets, logs, events. Stable across builds |
| **Id** (`ParamEntry.Id`) | `u16` | `3` | The wire: `ParamSet` and the `id` field of `ParamManifest`. Declaration order, starting at 0. Stable within one run of the app |

Appendix B.2 writes the manifest row as `{id, name, …}`: the same two. Where this page says "by name" it means
the string. A wire id is never written into source code: the Hub, the Console and the Spectator learn it from
the manifest.

Naming:

- `<area>.<name>`, lower camel case per segment. One exception, kept from §12.4: `demoLock`. The registry
  enforces only that the name is not empty; the form is a convention.
- The first segment is the owning area: `space`, `render`, `thermal`, `pose`, `replay`, `scan`, `input`, `hands`,
  `link`, `telemetry`, `forge`, `hud`, `dev`, `perf`, or a space's own: `home`, `glued`, `curved`, `cells`, `planet`,
  `portal`, `rooms`, `slicer`. A use case (Phase 2) takes its own first segment (`playground`, `lung`, …).
- A space's parameters are written as §12.2 and §12.4 write them: `glued.copies`, not `space.glued.copies`. The
  only name that starts with `space.` is `space.id`.
- The **group** is a lower-case key. For a space it is `SpaceIds.ParamGroup(id)` (lane A §1): `home`, `glued`,
  `curved`, `cells`, `planet`, `portal`, `slicer`. Others in use: `app`, `render`, `pose`, `scan`, `input`,
  `hands`, `link`, `forge`, `hud`, `dev`, `perf`. The Console and the menu show it as a heading. The group of an
  entry is the one of its first declaration.
- A group is **active** or **inactive** (lane A §4.3). SpaceLab marks a space's group inactive on `Exit` and
  active on `Enter` (`SetGroupActive`). An inactive group is shown folded and greyed; its parameters keep their
  values and can still be set.

Every value is one `float` on the wire and in the registry, whatever its kind (`ParamEntry.Sanitise`):

| Kind | Stored value | Clamp applied by `Set` |
|---|---|---|
| `Float` | the number | to `[min, max]`. The registry does not quantise: there is no step and no list of allowed values |
| `Int` | the integer as a float (exact below 2²⁴) | rounded half away from zero, then to `[min, max]` |
| `Bool` | 0 or 1 | `value ≥ 0.5` gives 1, else 0 |
| `Enum` | the option index 0 … n − 1 | rounded half away from zero, then to `[0, n − 1]` |

A `NaN` is never stored: `Set` leaves the value as it is and returns false.

## 1.2 `ParamRegistry`

```csharp
// File: ATLAS/Assets/Atlas/Link/Params/ParamRegistry.cs
using System;
using System.Collections.Generic;

namespace Atlas.Link
{
    public enum ParamKind : byte { Float = 0, Int = 1, Bool = 2, Enum = 3 }

    /// <summary>Who changed a parameter. The link echoes and the phone logs every change with its source.</summary>
    public enum ParamSource : byte
    {
        Local = 0,     // code on the phone or the on-device menu
        Remote = 1,    // a ParamSet from the Station, or an operator command
        Restore = 2    // a snapshot, a preset or the baked defaults being restored
    }

    /// <summary>Called after a parameter took a new value.</summary>
    public delegate void ParamChangedHandler(ParamEntry entry, ParamSource source);

    /// <summary>A cheap, copyable reference to one parameter: its wire id. default(ParamHandle) is invalid.</summary>
    public readonly struct ParamHandle : IEquatable<ParamHandle>
    {
        public bool IsValid { get; }
        public ushort Id { get; }                    // the wire id; only meaningful when IsValid
        public bool Equals(ParamHandle other);
    }

    /// <summary>One declared parameter. Keep the entry a Declare call returns and read Float, Int or Bool on
    /// the frame path: each is a field read.</summary>
    public sealed class ParamEntry
    {
        public ParamRegistry Registry { get; }
        public ushort Id { get; }                    // wire id: the order of declaration, starting at 0
        public string Name { get; }
        public string Group { get; }
        public ParamKind Kind { get; }
        public bool OnRelease { get; }               // the Console sends it when the slider is released
        public float Min { get; }
        public float Max { get; }
        public float Default { get; }
        public float Value { get; }                  // as it travels on the wire
        public float Float { get; }
        public int Int { get; }
        public bool Bool { get; }
        public ParamHandle Handle { get; }
        public bool Active { get; }                  // false while the entry's group is inactive
        public bool Live { get; set; }               // carried in the manifest as "live"; setting it bumps ManifestVersion
        public int EnumCount { get; }                // 0 for the other kinds
        public string EnumName(int value);           // null when out of range
        public string CurrentName { get; }
        public bool Set(float value, ParamSource source = ParamSource.Local);
        public bool Set(int value, ParamSource source = ParamSource.Local);
        public bool Set(bool value, ParamSource source = ParamSource.Local);
        public bool Reset(ParamSource source = ParamSource.Local);
        public float Sanitise(float requested);      // the value the entry would take: clamped, rounded
    }

    public sealed class ParamRegistry
    {
        public const int MaxPerMessage = 255;        // entries one ParamSet can carry

        public event Action<string, bool> GroupActiveChanged;   // (group, active)
        public event ParamChangedHandler Changed;                // after the entry's own callback
        public event Action<ParamEntry> Declared;                // first declaration of a name only
        public int Version { get; }                  // +1 on every value change
        public int ManifestVersion { get; }          // +1 per new entry, changed range, default, names, onRelease, Live or group state

        // ---- declare (see 1.3 for redeclaration) -------------------------------------------------
        public ParamEntry DeclareFloat(string name, string group, float min, float max, float defaultValue,
            Action<float> onChanged = null, bool onRelease = false);
        public ParamEntry DeclareInt(string name, string group, int min, int max, int defaultValue,
            Action<int> onChanged = null, bool onRelease = false);
        public ParamEntry DeclareBool(string name, string group, bool defaultValue,
            Action<bool> onChanged = null, bool onRelease = false);
        public ParamEntry DeclareEnum(string name, string group, string[] names, int defaultValue,
            Action<int> onChanged = null, bool onRelease = false);

        // ---- enumerate ---------------------------------------------------------------------------
        public int Count { get; }
        public ParamEntry this[int index] { get; }   // by position, which is the wire id
        public IReadOnlyList<ParamEntry> Entries { get; }
        public ParamEntry Find(string name);         // null when unknown
        public bool TryGet(string name, out ParamEntry entry);
        public bool TryGet(ushort id, out ParamEntry entry);
        public ParamEntry Get(ParamHandle handle);   // null when the handle is invalid for this registry
        public ParamHandle HandleOf(string name);    // an invalid handle when unknown
        public void GetGroups(List<string> into);    // distinct groups in order of first declaration

        // ---- groups ------------------------------------------------------------------------------
        public void SetGroupActive(string group, bool active);    // every group starts active
        public bool IsGroupActive(string group);

        // ---- read: allocation-free by handle; by name is a dictionary lookup ----------------------
        public float GetFloat(ParamHandle handle, float fallback = 0f);
        public int   GetInt(ParamHandle handle, int fallback = 0);
        public bool  GetBool(ParamHandle handle, bool fallback = false);
        public float GetFloat(string name, float fallback = 0f);
        public int   GetInt(string name, int fallback = 0);
        public bool  GetBool(string name, bool fallback = false);

        // ---- write: clamps, stores, calls back; true when the stored value changed ---------------
        public bool Set(ParamEntry entry, float value, ParamSource source = ParamSource.Local);
        public bool Set(ParamHandle handle, float value, ParamSource source = ParamSource.Local);
        public bool Set(ushort id, float value, ParamSource source = ParamSource.Local);
        public bool Set(string name, float value, ParamSource source = ParamSource.Local);
        public int  Apply(ParamSetMessage set, ParamSource source = ParamSource.Remote);   // how many changed
        public int  ResetAll(ParamSource source = ParamSource.Local);
        public void PushAll();                       // calls every entry's own callback with its current value

        // ---- link and persistence ----------------------------------------------------------------
        public int FillParamSet(ParamSetMessage into, int firstIndex = 0);   // returns the index to continue from
        public JsonValue ToManifestJson();           // the ParamManifest body of 2.5
        public JsonValue ToSnapshot();               // {"values":{"<name>": value}}; bools as true or false
        public int Restore(JsonValue snapshot, ParamSource source = ParamSource.Restore);   // how many changed
        public static string KindName(ParamKind kind);   // "float", "int", "bool", "enum"
    }
}
```

There is no table of parameters inside `Atlas.Link` and no class of name constants: a `new ParamRegistry()` is
empty. Each owner holds the names it declares as constants of its own (`AppParams`, `TelemetryInstaller`,
`AssetClientInstaller`, `FlatTwinHud`, the spaces, `CommandRouter`).

## 1.3 Rules

| Topic | Rule |
|---|---|
| Declaring twice | Declaring a name that exists with the **same kind** returns the **same entry** and keeps the current value. The new `min`, `max`, default, `names` and `onRelease` replace the old ones; the value is clamped into the new range; `ManifestVersion` rises if anything changed. The entry's callbacks are **replaced**: the old owner's are dropped and the `onChanged` of this call is installed, so a redeclaration with `onChanged` null leaves the entry with no callback. The group and `Live` are kept. An owner that only wants to read an entry somebody else declared calls `Find` or `TryGet` and listens to `Changed`, as `AssetClientInstaller` does for `forge.backend` |
| Declaring with another kind | `InvalidOperationException` |
| Bad declaration | `ArgumentException` for an empty name, a `NaN` in range or default, `min > max`, or empty `names`. A default outside the range is clamped, not refused. `InvalidOperationException` when the registry holds 65,536 entries |
| `Set` | Clamps as in 1.1. If the applied value equals the stored one, nothing else happens and the result is false. Otherwise: store, `Version` + 1, call the entry's own callback, raise `Changed`, return true. An unknown name, id or handle, or an entry of another registry, returns false |
| Flags | There are none. `OnRelease` and `Live` are the two properties an entry carries beside its range. The demo lock is enforced by the link (below), not by the registry, and "kill switch" is a list of names (1.5) |
| Reading | `ParamEntry.Float`, `Int`, `Bool` are field reads; `GetFloat`, `GetInt`, `GetBool` by handle are an array read with a fallback for an invalid handle. No allocation. The lookups by name are for set-up code and tools |
| Callbacks | Run synchronously inside `Set`, on the main thread. A callback that throws is not caught by the registry |
| `PushAll` | The composition root calls it once after all declarations (lane A §8.2), so the properties the callbacks drive start from the registry's values |
| Threading | Main thread only. The link applies `ParamSet` inside `LinkClient.Poll` at step 8b (§5.1), so callbacks never run inside steps 2 to 6 |
| Lock and watchdog | The registry knows nothing about the link. `LinkClient` drops a whole `ParamSet` while its `DemoLock` is on or the link is not CONNECTED (§2.8), before it reaches the registry |
| `OnRelease` | The Console sends one `ParamSet` when the slider is released. The owner's handler rebuilds and calls `ISpaceHost.BumpWorldVersion()` (lane A §4.3) |
| Removal | There is none. A parameter lives until the app exits. Leaving a space marks its group inactive (1.1) |
| Bump Guard | Declares nothing and reads nothing. Its thresholds and its depth rate are constants in `Atlas.Safety` (plan §4.10, §7.5 rule 3). `Atlas.Safety` cannot reference `Atlas.Link` at all (README F1) |

Who declares what (README F6):

- `Atlas.App.AppParams` declares the rows of the assemblies that may not reference `Atlas.Link` (rendering,
  pose, scan, input) with the callbacks that drive their plain properties, and the kill switches of §12.4 that
  have no owner in this build (`DeclareUnownedKillSwitches`: only when the name is not declared yet).
- Each space declares its own through the delegate it gave `SpaceRegistry.Register`
  (`SpaceRegistry.DeclareAllParams` at boot, in `SpaceId` order) and again in `Configure` (lane A §4.3, §5).
- Lane D's installers declare theirs (`TelemetryInstaller`, `FlatTwinHud`, `AssetClientInstaller`).
- The tracked hands declare theirs, the `hands.*` area of 1.4, through `Atlas.Hands.HandsSystem.DeclareParams`, which
  `Atlas.App.AtlasApp` calls after `AppParams`, the spaces, lane D's installers and the dev rows (lane A §8.2).
  `Atlas.Hands` may reference `Atlas.Link` for parameter types only, as a space does (README F1).
- The thermal ladder lives in `Atlas.Rendering`; `AppParams` writes each rung's values back with
  `ParamSource.Local` (lane A §9.5).

Typical use:

```csharp
// A space, in its DeclareParams delegate and in Configure(RoomModel room, ParamRegistry registry):
_copies = registry.DeclareInt("glued.copies", "glued", 3, 7, 5, OnCopiesChanged, onRelease: true);
// Frame path:
int n = _copies.Int;
// Atlas.App, for an owner that may not reference Atlas.Link:
registry.DeclareFloat(AppParams.Ipd, "render", StereoRig.MinIpd, StereoRig.MaxIpd, StereoRig.DefaultIpd, v => rig.Ipd = v);
```

## 1.4 Names declared in this build

In the order of a full boot of the phone app: `AppParams` (lane A §8.2 step 16), the spaces, lane D's
installers (step 18), then the dev rows, the perf tour's and, last of all, the tracked hands' rows. The wire id follows that order
but is read from the manifest, never from this table. **R** = `onRelease`. **K** = kill switch (1.5). Names, ranges and defaults of
lane A's rows are those of lane A §9.6 and §14.

| Name | Kind | Range or options | Default | Unit | | Group | Declared by · drives |
|---|---|---|---|---|---|---|---|
| `space.id` | Enum | `S0 Home`, `S1 Glued`, `S2 Curved Walk`, `S3 Aligned Cells`, `S4 Small Planet`, `S5 Impossible Rooms`, `S6 Slicer` | 0 | – | K R | `app` | `AppParams.DeclareSpace` · `ISpaceHost.RequestSwitch`; written back after every switch |
| `thermal.pinTier` | Int | −1 … 4: −1 automatic, 0 … 3 the rows of §8.6, 4 the high tier | −1 | – | K R | `render` | `AppParams` · the quality applier's forced tier |
| `render.scale` | Float | 0.6 … 0.8 | 0.8 | – | K R | `render` | `AppParams` · eye textures; written by the ladder |
| `render.msaa` | Int | 1 … 4 | 4 | samples | K R | `render` | `AppParams` · eye textures; written by the ladder |
| `render.ipd` | Float | 0.055 … 0.075 | 0.064 | m | | `render` | `AppParams` · `StereoRig.Ipd` |
| `render.lineWidth` | Float | 0.5 … 3 | 1.5 | px | | `render` | `AppParams` · `AtlasLook.SetLineWidth` |
| `render.lens` | Bool | | the lens blit's start state | – | | `render` | `AppParams` · `LensBlit.Distortion` |
| `render.lensK1` | Float | 0 … 1 | 0.34 | – | | `render` | `AppParams` · `LensModel.K1` |
| `render.lensK2` | Float | 0 … 1 | 0.55 | – | | `render` | `AppParams` · `LensModel.K2` |
| `render.lensSeparation` | Float | 0.055 … 0.075 | 0.064 | m | | `render` | `AppParams` · `LensModel.LensSeparation` |
| `render.screenToLens` | Float | 0.025 … 0.060 | 0.039 | m | | `render` | `AppParams` · `LensModel.ScreenToLens` |
| `render.lensOffsetY` | Float | −0.02 … 0.02 | 0 | m | | `render` | `AppParams` · `LensModel.VerticalOffset` |
| `render.headOffsetX`, `…Y`, `…Z` | Float | −0.10 … 0.10 | 0 | m | | `render` | `AppParams` · `StereoRig.HeadOffset` |
| `render.furniture` | Enum | `auto`, `light`, `full` | 0 | – | | `render` | `AppParams.DeclareRender` · `FurnitureLevelPolicy.Mode`: which copy of the furniture is drawn (§4.1). `auto`: the entered space's level (Full in Home and Curved Walk, Light in the others), held to Light by the quality ladder from its first step below nominal; `light`: Light in every space; `full`: Full in every space, whatever the ladder says |
| `curved.cullRho` | Float | 2 … 3.5 | 3 | kernel units | K | `curved` | `AppParams` · `IWalkerKernel.CullRho`; written by the ladder |
| `glued.copies` | Int | 3 … 7 | 5 | per side | K R | `glued` | `AppParams`, then S1 · S1; written by the ladder |
| `curved.mode` | Enum | `Mode 1 Curved Walk`, `Mode 2 Aligned Cells` | 0 | – | K R | `curved` | `AppParams.DeclareUnownedKillSwitches` · read by S2 (which asks SpaceLab for S3 when it becomes Mode 2) and S3 |
| `curved.q` | Int | 3 … 6 | 5 | tiles per corner | | `curved` | `CurvedDialParams.RegisterQ`, from S2 and S4 · the dial; S2 takes 4 to 6, 3 hands over to S4 |
| `curved.strong` | Bool | | false | – | R | `curved` | `CurvedDialParams.RegisterStrong` · the dial uses the loop-fit radius below the comfort floor |
| `cells.viewDepth` | Int | 0 … 2 | 2 | cells | K | `cells` | S3 (`AlignedCellsCore.DeclareParams`) · neighbour cells drawn |
| `cells.maze` | Bool | | true | – | | `cells` | S3 (`AlignedCellsCore.DeclareParams`) · the duck maze: on, every entry but from Curved Walk is a maze run on a 4 × 4 grid; off, classic S3. A change while S3 is entered reconfigures it |
| `cells.mazeSeed` | Int | 0 … 1,048,575 | 0 | – | | `cells` | S3 (`AlignedCellsCore.DeclareParams`) · the seed of every maze run; 0, a fresh seed each run |
| `planet.R` | Float | 0 … 4 | 0 (the loop-fit R) | m | K R | `planet` | S4 (`SmallPlanetCore.DeclareParams`) · the sphere's radius |
| `planet.fogRho` | Float | 0.5π … 0.8π | 0.8π | rad | K | `planet` | S4 · the opaque end of the fog |
| `portal.depth` | Int | 0 … 1 | 1 | – | K | `portal` | `AppParams` (unowned), then S5 |
| `slicer.source` | Enum | `live`, `stepped`, `tesseract` | 0 | – | K R | `slicer` | `AppParams` (unowned), then S6 |
| `forge.backend` | Enum | `local`, `hosted`, `cache`, `off` | 0 | – | K | `forge` | `AppParams` (unowned); read by `AssetClientInstaller` · asset client (§4.3) |
| `pose.gyroPredict` | Bool | | false | – | K | `pose` | `AppParams.DeclarePose` · pose pipeline |
| `pose.gyroLeadMs` | Float | 0 … 60 | 40 (proposed; §8.7 gives 30–60) | ms | | `pose` | the same |
| `pose.source` | Enum | `tracked`, `replay`, `walker` | 0 | – | R | `pose` | `AppParams` · choice of pose source (README F9) |
| `replay.speed` | Float | 0 … 4 | 1 | × | | `pose` | `AppParams` · pose-log replay |
| `replay.loop` | Bool | | true | – | | `pose` | `AppParams` · pose-log replay |
| `pose.record` | Bool | | false | – | | `pose` | `AppParams` · the pose-log recorder (lane C §7.6) |
| `scan.route` | Int | 1 … 4 | 3 | – | K | `scan` | `AppParams.DeclareScan` · `ScanSettings.Route` |
| `scan.teamPrescan` | Bool | | false | – | | `scan` | `AppParams` · `ScanSettings.TeamPrescan` |
| `scan.wallBridge` | Bool | | true | – | | `scan` | `AppParams.DeclareScan` · `WallBridge.Apply`: `WallBridge.Enabled` becomes the parameter and the registered bridge's own `Available`. Off gives the outline's walls, as before the bridge was switched on |
| `input.dwell` | Float | 0.6 … 0.8 | 0.7 | s | | `input` | `AppParams.DeclareInput` · `IAtlasInput.DwellSeconds` |
| `home.marks` | Bool | | true | – | | `home` | S0 |
| `glued.mode` | Enum | `t2`, `klein`, `t3` | 0 | – | K R | `glued` | S1 |
| `glued.avatar` | Bool | | true | – | | `glued` | S1 |
| `rooms.layout` | Enum | `booth`, `loop` | 0 | – | R | `portal` | S5 |
| `rooms.count` | Int | 2 … 6 | 6 | rooms | K R | `portal` | S5 |
| `slicer.object` | Enum | `tesseract`, `cell5`, `cell16`, `cell24`, `tube`, `ball` | 0 | – | R | `slicer` | S6 |
| `slicer.view` | Enum | `slice`, `shadow` | 0 | – | | `slicer` | S6 |
| `slicer.w` | Float | −1.1 … 1.1 | 0 | model units | | `slicer` | S6, which writes it every frame |
| `slicer.xw`, `slicer.yw`, `slicer.zw` | Float | −180 … 180 | 0 | degrees | | `slicer` | S6, which writes them every frame |
| `slicer.tilt` | Float | 0 … 80 | 0 | degrees | | `slicer` | S6 (the tube only) |
| `hud.map` | Bool | | false | – | | `hud` | `FlatTwinHud.Install` · mini-map in the headset (§5.9). Not declared in the Spectator |
| `demoLock` | Bool | | false | – | K | `app` | `TelemetryInstaller` · `LinkClient.DemoLock` |
| `link.enabled` | Bool | | true | – | K | `link` | `TelemetryInstaller` · starts and stops the link |
| `link.stateHz` | Int | 10 … 60 | 60 | Hz | K | `link` | `TelemetryInstaller` · `LinkClient.StateHz` |
| `telemetry.mode` | Enum | `live`, `phoneCsv`, `hud` | 0 | – | K | `link` | `TelemetryInstaller` · `LinkPump.Mode` |
| `hud.visible` | Bool | | false | – | | `hud` | `TelemetryInstaller` · HUD |
| `dev.room` | Enum | `none`, then the fixture rooms | by start options | – | R | `dev` | `AppParams.DeclareDev` · the rehearsal flow |
| `dev.overlay` | Bool | | on in the Editor and in a development build | – | | `dev` | `AppParams.DeclareDev` |
| `perf.run` | Enum | `off`, `tour`, `soak`, `soakNoDepth`, `ladder` | 0 | – | R | `perf` | the perf tour (`App/PerfTour`) |
| `perf.visitSeconds`, `perf.soakMinutes`, `perf.soakIntervalSeconds` | Int | 5 … 120, 1 … 30, 15 … 300 | by start options | s, min, s | R | `perf` | the perf tour |
| `perf.pose` | Enum | `auto`, `tracked`, `replay`, `synthetic` | by start options | – | R | `perf` | the perf tour |
| `hands.enabled` | Bool | | false | – | K | `hands` | `HandsSystem.DeclareParams` · `HandsRuntime.Enabled`: the kill switch of the tracked hands. Off, the hands' gate is closed (event `hands.off`, reason `param`): no detector, no hand drawn, no touch, no mask, and the panels stand where they always have |
| `hands.rate` | Enum | `auto`, `30`, `15` | 0 (`auto`) | – | | `hands` | the same · `HandsRuntime.RateOverride` (0, 30, 15 detections a second). `auto` follows the tier (30 at Nominal and High, 15 at Fair) with the self-governor and the idle stop; 30 and 15 fix the rate and switch both off. For the bench |
| `hands.reach` | Float | 0.40 … 0.60 | 0.48 | m | | `hands` | the same · `HandsRuntime.Reach`: how far from the rear camera the near anchor stands, where panels come to in a hands session |
| `hands.sound` | Bool | | true | – | | `hands` | the same · `HandsRuntime.Sound`: the three clicks (a press, a contact with a surface, a pinch) |
| `hands.draw` | Bool | | true | – | | `hands` | the same · `HandsRuntime.Draw`: off, the hands and their marks are not drawn while tracking, touch and the mask go on (the blind-reach check of the camera-to-eye offset) |
| `hands.editor` | Bool | | false | – | | `hands` | the same · `EditorHandsSource.Enabled`: the Editor's mouse-and-keys hands (verification brief, step 3i). Nothing happens on a phone or where there is no tracked camera: no such source is made there |
| `hands.bench` | Bool | | false | – | | `hands` | the same · `HandsRuntime.Bench`: one `hands.bench` event a second while a session runs (§2.6) |
| `hands.record` | Bool | | false | – | | `hands` | the same · `HandsRuntime.Record`: writes the detections to `hands_<UTC time>.handlog` in the phone's `logs` folder (`recordings/README.md`) |

Notes:

- Angles are **degrees** in the registry and radians in code and in `State.p[]` (README F4). The owner converts.
- `render.scale` is the real scale and `render.msaa` the real sample count, not an index. Against §12.4,
  `render.msaa` gains 1, `glued.copies` gains 7 and `thermal.pinTier` gains 4, for the last and the high rung
  (lane A, open item 4). The registry holds ranges, so `render.scale` 0.65 or `render.msaa` 3 are accepted;
  the owner maps them to what it can do.
- Build-dependent start values are set by `TelemetryInstaller`, not by the declaration: `demoLock` to 1 in a
  build with `ATLAS_DEMO_LOCK`, `hud.visible` to 1 when `Debug.isDebugBuild`.
- `space.id` set to a space that is not available (neither a scene in the build nor a registered factory) is refused by SpaceLab
  (`space.refused`), which writes the parameter back to the entered space.
- `telemetry.mode`: `live` sends `Telemetry` and writes the phone CSV; `phoneCsv` writes the CSV only; `hud`
  writes nothing and forces the HUD on (§12.2, the "Station: telemetry" row).
- `forge.backend`: §4.3. With `off` the phone sends nothing to the Forge.
- S6 writes `slicer.w` and the three angles every frame from the w lane and the sticks. Their values travel in
  `State.p[3..6]`; a change made by code (`ParamSource.Local`) raises no `param` event for these four names
  (`LinkPump`, §2.6).
- `pose.gyroPredict` is the on/off switch of §12.4 and `pose.gyroLeadMs` the lead; lane C's page writes the
  lead as one number.
- The eight `hands.*` entries are declared in the order of the table by `HandsSystem.DeclareParams`. `AtlasApp` calls
  it last of all, after the menu, onboarding and the perf tour are installed at row 19 of lane A §8.2: the `perf.*`
  rows, which the tour declares when it is installed, keep the wire ids they had before the hands existed, and the
  hands take the eight that follow. The menu reads the registry when it is opened, so it lists them all the same.
- `hands.enabled` is off at the start (decision R8 of the hands brief): a build with the tracked hands does nothing
  different until it is switched on, from the Console or from the on-device menu. Under the demo lock the Station
  cannot change it, so in a locked demo it is changed from the menu (1.5). `hands.editor`, `hands.bench` and
  `hands.record` are developer parameters and no kill switch.
- The hands add nothing to `State` and no message, only the `hands` operator command (§2.9), which works under the demo lock: their parameters travel in the manifest and
  `ParamSet` like any other, and their events (§2.6) are ordinary `Event` messages (§2.3 here; §8.3 of the hands
  design).

## 1.5 Kill switches

The union of the "Switch" column of §12.2 and the list of §12.4, and after them the switch of the tracked hands. It is
a list of names, in menu order (`Atlas.App.Menu.MenuModel.KillSwitchIds`), not a flag on the entry:

`space.id`, `demoLock`, `thermal.pinTier`, `render.scale`, `render.msaa`, `curved.cullRho`, `curved.mode`,
`cells.viewDepth`, `planet.R`, `planet.fogRho`, `glued.mode`, `glued.copies`, `rooms.count`, `portal.depth`,
`slicer.source`, `pose.gyroPredict`, `scan.route`, `link.enabled`, `link.stateHz`, `telemetry.mode`,
`forge.backend`, `hands.enabled`.

`cells.viewDepth`, `planet.R` and `planet.fogRho` are lane B's, declared by `AlignedCellsCore.DeclareParams` and `SmallPlanetCore.DeclareParams`
through `SpaceRegistry.DeclareAllParams` at boot, so the menu lists all 21 of the plan's from the first frame, and
`hands.enabled` with them (`HandsSystem.DeclareParams` runs before the frame loop is enabled). `cells.viewDepth` is Int 0 … 2 (default 2, the
view range of `SightLines`), `planet.R` Float 0 … 4 m (0 = the loop-fit value, on release) and `planet.fogRho` Float 0.5π … 0.8π rad
(default 0.8π, the opaque end of the fog; the start is 0.3π below it). The dial's own `curved.q` (Int 3 … 6, default 5) and
`curved.strong` (Bool, on release) are declared once by `CurvedDialParams` for S2 and S4 and are not kill switches.

The list is these 22 names: the 21 of the plan, and `hands.enabled` after them, so that no switch changes its place
and the hands can be switched off in the headset with the Station away (hands design §3.7, E11). The Quality page
shows `hands.enabled` last, above the reset row. `scan.wallBridge` (§1.4) does what a kill switch does, since the
Console or the menu can switch the RoomPlan wall bridge off and the walls are then the outline's, but it is not in the
list: the list is the union of the "Switch" column of §12.2 and the list of §12.4 with the hands' switch, and
`MenuModelTests` asserts its length (22, `QualityPage_HoldsTheDeclaredKillSwitches_InContractOrder`) and that
`hands.enabled` is the last (`QualityPage_HoldsTheHandsSwitch_AfterTheOthers`) against this section.
`render.furniture` is not in it either. Both are on the menu's Parameters page, which shows every group of the
registry, and in the Console. Putting either on the menu's Quality page is a change to this section, to
`MenuModel.KillSwitchIds` and to those tests together (open item 14).

Rules (§12.4):

- Each is settable from the Console and from the on-device menu (§5.7). The menu needs no link.
- Every change is logged: one `param` event (§2.6) in the phone's event log, with the source, and on the Hub
  when it is connected.
- `demoLock`: while the lock is on the phone drops every `ParamSet` and the Hub answers 423 to parameter
  requests, so the lock cannot be lifted by a slider. The Console's lock toggle sends `OperatorCmd lock`, which
  ends in `registry.Set("demoLock", on ? 1f : 0f, ParamSource.Remote)` (lane A's `CommandRouter.SetLock`). The
  menu sets it directly. The phone's `State.flags` carries the lock, and the Hub follows it.
- `link.enabled` set to off from the Console closes the link (`Bye {"reason": "link.enabled off"}`). It can then
  only be set back from the menu, and the link starts again only on a phone that has paired before.
- `hands.enabled` starts off (decision R8 of the hands brief), and "Reset switches to defaults" (§5.7) puts it off
  again.
- **Bump Guard has no kill switch, no parameter and no off state.** The hands' switch does not reach it: with the
  hands off, or not installed, Bump Guard runs as it always has (lane C §8.8).

## 1.6 `AtlasParamAttribute` and the binder

The attribute marks a field of a class as a registry parameter. The field is the fast read: reading it costs
nothing. Code never assigns the field; it calls `registry.Set`.

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Params/AtlasParamAttribute.cs, ParamBinder.cs
using System;
using Atlas.Link;

namespace Atlas.Telemetry
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class AtlasParamAttribute : Attribute
    {
        public AtlasParamAttribute(string id, float min, float max, float def);   // float or int field
        public AtlasParamAttribute(string id, bool def);                          // bool field
        public AtlasParamAttribute(string id, int def);                           // enum field: default option index

        public string Id { get; }                // the parameter's name
        public float Min { get; }
        public float Max { get; }
        public float Default { get; }
        public string Group { get; set; }        // null: the first segment of the name, or "app" when it has none
        public float Step { get; set; }          // carried for the Console; the registry does not quantise
        public bool OnRelease { get; set; }
        public bool KillSwitch { get; set; }     // a marker; the menu's list is 1.5
        public string ResolveGroup();
    }

    public static class ParamBinder
    {
        /// <summary>Declares every [AtlasParam] instance field of target (its own and its base types') and keeps
        /// each field equal to the registry value until the binding is disposed.</summary>
        public static ParamBinding Bind(object target, ParamRegistry registry);
        /// <summary>As Bind, for the attributed static fields of a type (base types are not searched).</summary>
        public static ParamBinding BindStatic(Type type, ParamRegistry registry);
    }

    /// <summary>Dispose stops the field updates. The parameters stay declared and keep their values.</summary>
    public sealed class ParamBinding : IDisposable
    {
        public ParamRegistry Registry { get; }
        public int Count { get; }
        public ParamHandle HandleOf(string id);      // KeyNotFoundException when the name was not bound here
        public ParamEntry EntryOf(string id);        // likewise
        public void Dispose();
    }
}
```

What `Bind` does, once, off the frame path:

1. Finds every instance field of `target` (public or not, declared on its type or a base type) that carries
   the attribute, by reflection.
2. Checks the field: `float`, `int`, `bool`, or an enum whose values are exactly 0 … n − 1, not read-only.
   Anything else, an empty name, `min > max`, or a constructor that does not fit the field type, is an
   `InvalidOperationException` that names the field.
3. If the name is already declared, the field **follows** the existing entry (its owner, range and value are
   kept; another kind is an `InvalidOperationException`). Otherwise it declares the parameter (`DeclareFloat`,
   `DeclareInt`, `DeclareBool`, or `DeclareEnum` with the enum's names as labels) with no callback.
4. Writes the registry's current value into the field.
5. Subscribes one listener to `registry.Changed` that writes the field on every change of a bound entry (it
   boxes; it runs only when a value changes).

```csharp
public sealed class GluedSpace : MonoBehaviour, ISpaceModule
{
    [AtlasParam("glued.postWidth", 0.03f, 0.20f, 0.06f, Group = "glued", Step = 0.005f)] float _postWidth;
    [AtlasParam("glued.avatar", true, Group = "glued")] bool _avatar;
    ParamBinding _params;

    public void Configure(RoomModel room, ParamRegistry registry) { _params = ParamBinder.Bind(this, registry); }
    public void Exit() { _params?.Dispose(); _params = null; }
}
```

The attribute is a convenience, not a duty: a space may call `Declare…` and keep entries, as lane A's spaces
do. `Atlas.Rendering`, `Atlas.Scan`, `Atlas.Input` and `Atlas.Safety` cannot use it (they do not reference
`Atlas.Telemetry`). The first three expose plain properties (README F6); Safety has no parameter.

## 1.7 Presets, snapshots and baked defaults

One JSON shape serves all three: `{"values": {"<name>": <number>, …}}`. Keys are names **(decided here)**;
Appendix B.3 writes the key as `id`. `ParamRegistry.ToSnapshot` writes a `Bool` as `true` or `false`;
`Restore` reads a number, a boolean, or for an enum the option's name, and also accepts the bare
name-to-value map. The Hub accepts a wire id as a key too and translates it through the manifest.

| What | Where | Written by | Read by |
|---|---|---|---|
| Preset | `station/hub/presets/<name>.json` | Hub, `POST /api/presets/{name}` | Hub; loading replays every value in one `ParamSet` (§3.3) |
| Baked defaults | `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json` | Hub, `POST /api/presets/{name}/bake`; committed by an engineer | `TelemetryInstaller` at boot: `Resources.Load<TextAsset>("AtlasParams/defaults")` → `AtlasJson.TryParse` → `registry.Restore(baked, ParamSource.Restore)` |
| Last snapshot | Hub memory (`LinkHost`), kept until the Hub stops | Hub: every value it sent, every `param` event and manifest of the phone | Hub, replayed at RESYNC (§2.8) |

- Baked defaults set the **value**, not the entry's `Default`: `Restore` for every name already declared, and
  a listener on `registry.Declared` sets the baked value of a name as it is declared later. A build started
  with the Hub off therefore runs with the baked values (§7.3.2); "Reset" still goes to the declared default.
  It runs once, in `TelemetryInstaller.Install` (step 18 of lane A §8.2), before the frame loop is enabled.
- A name in a preset that the phone does not know is skipped and counted in `hub.log`. A value out of range
  is clamped on the phone, as always.
- `defaults.json` is absent until the first Bake. An absent or unreadable file means "no baked values".

## 1.8 `AtlasJson`

The engine-free JSON reader and writer of `Atlas.Link` (README F1). Used for the JSON link messages, events,
parameter snapshots, RoomModel files and the JSON chunk of a GLB. No reflection, no attributes, no generics over
user types. It replaces `JsonUtility` on the phone (§7.2 names `JsonUtility`; `Atlas.Link` cannot reference the
engine, and `System.Text.Json` does not exist in Unity's profile, U-net §11). The Hub reads and writes every
HTTP and WebSocket body with it as well.

**Decided here:** one DOM type, `JsonValue`, for reading and for building. There is no separate forward
writer: a message builds a `JsonValue` and serialises it.

```csharp
// Files: ATLAS/Assets/Atlas/Link/Json/JsonValue.cs, AtlasJson.cs
using System.Text;

namespace Atlas.Link
{
    public enum JsonKind : byte { Null, Bool, Number, String, Array, Object }

    /// <summary>One JSON value. Reading never throws on a wrong kind or a missing member.</summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null;       // the value every failed lookup returns

        // ---- building ----------------------------------------------------------------------------
        public static JsonValue Object();
        public static JsonValue Array();
        public static JsonValue Array(params JsonValue[] items);
        public static JsonValue From(bool value);
        public static JsonValue From(double value);
        public static JsonValue From(float value);   // the shortest decimal that reads back to the same float
        public static JsonValue From(int value);
        public static JsonValue From(long value);
        public static JsonValue From(string value);  // null gives Null
        public static JsonValue Number(string text); // a number the caller formatted itself; FormatException if it is not one
        public static JsonValue Number(double value, string format);   // invariant culture, for example "F4"
        // implicit conversions from bool, int, long, float, double and string
        public JsonValue Set(string key, JsonValue value);   // adds or replaces; returns this for chaining
        public JsonValue Add(JsonValue item);                // appends to an array; returns this
        public bool Remove(string key);
        public void Clear();
        public JsonValue Clone();
        public bool DeepEquals(JsonValue other);

        // ---- reading -----------------------------------------------------------------------------
        public JsonKind Kind { get; }
        public bool IsNull { get; }
        public bool IsBool { get; }
        public bool IsNumber { get; }
        public bool IsString { get; }
        public bool IsArray { get; }
        public bool IsObject { get; }
        public bool IsInteger { get; }               // a Number written with no fraction and no exponent
        public int Count { get; }                    // elements of an Array, members of an Object, else 0
        public JsonValue this[string key] { get; set; }   // Null when missing or not an Object
        public JsonValue this[int index] { get; set; }    // Array element, or Object member value in document order; Null when out of range
        public string KeyAt(int index);              // Object member name in document order
        public bool Has(string key);
        public bool TryGet(string key, out JsonValue value);

        public bool   AsBool(bool fallback = false);
        public double AsDouble(double fallback = 0.0);
        public float  AsFloat(float fallback = 0f);
        public long   AsLong(long fallback = 0L);    // exact for an integer token; a fraction is truncated; out of range clamps
        public int    AsInt(int fallback = 0);       // AsLong, clamped to the int range
        public string AsString(string fallback = null);
        public bool   GetBool(string key, bool fallback = false);
        public double GetDouble(string key, double fallback = 0.0);
        public float  GetFloat(string key, float fallback = 0f);
        public long   GetLong(string key, long fallback = 0L);
        public int    GetInt(string key, int fallback = 0);
        public string GetString(string key, string fallback = null);

        // ---- text --------------------------------------------------------------------------------
        public static JsonValue Parse(string json);                        // AtlasJson.Parse
        public static bool TryParse(string json, out JsonValue value);
        public string ToJson(bool indent = false);   // compact: no whitespace at all; indented: two spaces and "\n"
        public void WriteTo(StringBuilder sb, bool indent = false);
        public override string ToString();           // ToJson(false)
    }

    public static class AtlasJson
    {
        public const int MaxDepth = 128;

        public static JsonValue Parse(string json);                        // FormatException with the error text
        public static bool TryParse(string json, out JsonValue value);
        public static bool TryParse(string json, out JsonValue value, out string error);
        public static bool TryParse(string json, bool allowTrailing, out JsonValue value, out string error);
        public static bool TryParseUtf8(byte[] buffer, int offset, int length, bool allowTrailing,
            out JsonValue value, out string error);                        // a byte-order mark is skipped
        public static JsonValue ParseUtf8(byte[] bytes);                   // a whole file image; FormatException
        public static string ToJson(JsonValue value, bool indent = false); // a null reference is written as null
        public static byte[] ToUtf8(JsonValue value, bool indent = false); // no byte-order mark
        public static string Quote(string text);
        /// <summary>Appends text as a quoted, escaped JSON string; a null reference is written as null.</summary>
        public static void AppendQuoted(StringBuilder sb, string text);
    }
}
```

| Topic | Rule |
|---|---|
| Grammar | RFC 8259. No comments, no trailing commas, no single quotes. `TryParseUtf8` skips a UTF-8 byte-order mark |
| Root | Any value. With `allowTrailing` the reader stops at the end of the root value and **ignores everything after it**: that is how the link reads its JSON messages (§7.5 rule 5). Without it, text after the root value is an error |
| Limits | Depth 128: a deeper document is refused instead of overflowing the stack. There is no size limit in the reader; callers bound their input (the Hub's HTTP bodies at 1 MB) |
| Errors | `error` reads `"<message> at character <index>"`; `value` is `JsonValue.Null` |
| Duplicate keys | The last one wins; the member keeps its first position |
| Numbers, reading | Held as `double`. An integer token in the `i64` range is also held exactly, for `AsLong` |
| Numbers, writing | An integer: decimal digits. A `double`: the round-trip format `"R"` under the invariant culture, with `.0` appended when the text has no `.` or exponent, so a whole-valued float reads back as a float (`0.0`, `2.0`). A `float` is first turned into the shortest decimal that reads back to the same float (0.1, not 0.10000000149011612). `Number(text)` is written exactly as given. `NaN` and infinities are written as `null` |
| Strings, writing | `"` `\` and the controls below U+0020 escaped (`\b`, `\f`, `\n`, `\r`, `\t` in short form, others `\u00xx`); U+2028 and U+2029 escaped too, so one event stays one line; everything else as it is |
| Member order | The order of `Set` calls. Every message writes its members in a fixed order, so equal messages give equal bytes |
| Unknown members | A reader of a message or a file ignores members it does not know and takes the documented default for members that are missing |
| Allocation | Parsing and building allocate. JSON is never parsed or built in steps 1 to 7 of the frame; in step 8 only when a JSON message is actually sent or received |
| 64-bit values | Tile ids are written as lane B's `TileId.ToString()` text, hashes as hexadecimal strings. Only `t_us` and counters are JSON numbers |

Lane C's `RoomModelJson` (lane C §2.6) reads through `AtlasJson.TryParse` and lays its canonical text out
itself (its own line breaks and number format), with `AppendQuoted` for strings. `JsonValue.Number` serves
a caller that builds a `JsonValue` but formats numbers itself.

---

# Part 2 · Link

## 2.1 Transport and channels

- **LiteNetLib 2.1.4** on both ends: OpenUPM `com.revenantx.litenetlib` in Unity (sources, never a precompiled
  DLL), NuGet `LiteNetLib` in the Hub and in ReplayPhone. Class `NetManager` (the multi-channel one), not
  `LiteNetManager`. `NetSerializer` and `NetPacketProcessor` are not used.
- LiteNetLib sits behind `ILinkTransport` (below). `LiteNetTransport` is the real one; `LoopbackTransport` is
  an in-memory one for tests. `LinkClient` and `LinkHost` name no LiteNetLib type.
- Unicast UDP to a known address only. No discovery, no broadcast, no Bonjour (R4 §3.2).
- The Hub is the only listener, on **UDP 9050**. The ATLAS app, the Spectator and Editors are clients and reject
  every connection request (`ConnectionRequest.Reject()`).
- Connection key: `atlas-v1`. A client connects with the key's bytes, made once
  (`NetManager.Connect(IPEndPoint, ReadOnlySpan<byte>)`), so an attempt allocates nothing; the Hub answers with
  `ConnectionRequest.AcceptIfKey("atlas-v1")`. The address must be an IP literal: a host name is refused, as
  it would resolve on the calling thread.

`NetManager` settings (§7.2, R4 §5.5), set in `LiteNetTransport`. Every field name below exists in the installed
package.

| Field | Clients | Hub | Why |
|---|---|---|---|
| `ChannelsCount` | 2 | 2 | Channel 0 control, channel 1 state |
| `PingInterval` | 500 | 500 | LiteNetLib's own keep-alive and RTT, ms |
| `DisconnectTimeout` | 3000 | 3000 | OFFLINE at 3 s of silence |
| `UpdateTime` | 5 | 5 | Logic thread period, ms |
| `IPv6Enabled` | false | false | The LAN is IPv4; one socket fewer |
| `AutoRecycle` | true | true | Readers are recycled after the receive callback |
| `EnableStatistics` | true | true | Loss counter for the link indicator |
| `UnconnectedMessagesEnabled`, `BroadcastReceiveEnabled` | false | false | Nothing unconnected is accepted |
| `ReconnectDelay` | 250 | default | With the next row: one connect attempt lasts 500 ms **(decided here)** |
| `MaxConnectAttempts` | 1 | default | Two requests 250 ms apart, then the attempt has failed; the back-off of §2.8 decides the next one |
| `AllowPeerAddressChange` | false | true | A phone that roams between access points keeps its peer |
| `MtuDiscovery` | default (off) | default (off) | MTU stays 1,024 |

| Ch | `LinkDelivery` · LiteNetLib call | Direction | Messages | Rate | Size |
|---|---|---|---|---|---|
| 0 | `Reliable` · `peer.Send(buf, off, n, 0, DeliveryMethod.ReliableOrdered)` | both | `Hello`, `Welcome`, `ParamManifest`, `ParamSet`, `SceneCmd`, `OperatorCmd`, `Telemetry`, `Event`, `RoomOutline`, `Log`, `JobStatus`, `AssetReady`, `AssetAck`, `Bye` | on demand; `Telemetry` 1 Hz | usually under 1 kB. A message over the MTU is fragmented by LiteNetLib (it does so for `ReliableOrdered` only); the largest binary one is a full `RoomOutline`, 8,697 B |
| 1 | `Sequenced` · `peer.Send(buf, off, n, 1, DeliveryMethod.Sequenced)` | phone → Hub → spectators | `State` | `link.stateHz`: 60, 30 or 10 Hz; never more than one per frame | 202 B; 234 B on the wire (4 B LiteNetLib header, 8 B UDP, 20 B IPv4), about 14 kB/s at 60 Hz |
| – | `Unreliable` · `peer.Send(buf, off, n, 0, DeliveryMethod.Unreliable)` | both | `TPing`, `TPong` | client: 8 at connect, then 1 Hz. Hub: 8 after a client's `Hello`, then 4 Hz to every client | 13 B, 29 B |
| HTTP | TCP | phone ⇄ Forge; Spectator → Forge | RoomModel, crops, GLB | on demand | Part 3 |

```csharp
// File: ATLAS/Assets/Atlas/Link/Transport/ILinkTransport.cs
namespace Atlas.Link
{
    public enum LinkDelivery : byte { Reliable = 0, Sequenced = 1, Unreliable = 2 }

    /// <summary>Every call arrives inside ILinkTransport.Poll, on the thread that called it.</summary>
    public interface ILinkTransportListener
    {
        void OnPeerConnected(int peerId);
        void OnPeerDisconnected(int peerId, string reason);    // also a failed connect attempt
        void OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery);   // bytes valid during the call only
    }

    public interface ILinkPeerAddresses { string GetPeerAddress(int peerId); }

    public interface ILinkTransport
    {
        ILinkTransportListener Listener { get; set; }
        bool IsRunning { get; }
        int LocalPort { get; }
        bool StartClient();
        bool StartHost(int port);                    // 0 picks a free port
        void Connect(string address, int port);      // returns at once; the outcome arrives through the listener
        void Poll();                                 // never blocks
        bool Send(int peerId, byte[] data, int offset, int length, LinkDelivery delivery);
        void Flush();
        void Disconnect(int peerId);
        void Disconnect(int peerId, byte[] data, int offset, int length);   // one last packet with the close itself
        void Stop();
        int GetRttMs(int peerId);
        float GetSilenceMs(int peerId);              // keep-alives included; negative when unknown
        int GetLossPermille(int peerId);
    }
}
```

- **Encoding.** Binary messages are written by array codecs (`int WriteTo(byte[] buffer, int offset)`,
  `bool ReadFrom(byte[] buffer, int offset, int length)`) into buffers `LinkClient` and `LinkHost` allocate
  once: zero allocation per send. Integers and floats go through the static `Wire` helpers (`PutU16` …
  `GetF64`, written out by hand, so they behave the same on Mono, IL2CPP and .NET). §7.2 names "one reused
  `NetDataWriter`"; the bytes on the wire are the same, and the codecs do not depend on LiteNetLib, so the
  Hub's log reader, ReplayPhone and the tests use them with no transport **(decided here)**.
- **JSON messages** are the id byte followed by the UTF-8 text of one JSON object: no length prefix, no
  terminator. The Hub parses the body, and hands the Console the parsed body with a member `t` added.
- **Send point.** `State` is sent once per frame from step 8a (§5.1), then `ILinkTransport.Flush()`
  (`NetManager.TriggerUpdate()`) puts it on the wire. `TPing`, `TPong` and `Hello` are flushed the same way.
- **Receive point.** `NetManager.PollEvents()` is called inside `LinkClient.Poll()`, once per frame from step
  8b on the phone, and every millisecond in the Hub's link loop. All callbacks therefore run on the caller's
  thread. One thing happens earlier: `LiteNetTransport` sets `UnsyncedReceiveEvent`, so LiteNetLib hands each
  data packet over on its receive thread, where the transport only stamps it, copies it into a pooled buffer
  and queues it for the next `Poll` (§2.7). Order is kept: a peer's packets are delivered after its connect
  and before its disconnect.

## 2.2 Message ids

```csharp
// File: ATLAS/Assets/Atlas/Link/Messages/Wire.cs
namespace Atlas.Link
{
    public static class MessageId
    {
        public const byte Hello = 0x01;
        public const byte Welcome = 0x02;
        public const byte TPing = 0x03;
        public const byte TPong = 0x04;
        public const byte State = 0x10;
        public const byte PeerState = 0x11;
        public const byte ParamManifest = 0x20;
        public const byte ParamSet = 0x21;
        public const byte SceneCmd = 0x22;
        public const byte OperatorCmd = 0x23;
        public const byte Telemetry = 0x30;
        public const byte Event = 0x40;
        public const byte RoomOutline = 0x41;
        public const byte Log = 0x42;
        public const byte JobStatus = 0x50;
        public const byte AssetReady = 0x51;
        public const byte AssetAck = 0x52;
        public const byte Bye = 0x7F;

        public static bool IsKnown(byte id);
        public static bool IsJson(byte id);
        public static string Name(byte id);
    }

    public static class LinkProtocol
    {
        public const int ProtoMajor = 1;
        public const int ProtoMinor = 0;
        public const string ConnectionKey = "atlas-v1";
        public const int DefaultPort = 9050;
        public const byte ChannelControl = 0;   // ReliableOrdered
        public const byte ChannelState = 1;     // Sequenced
        public const byte ChannelsCount = 2;
        public const int MaxBinaryMessageBytes = 2048;
    }
}
```

The HTTP ports are not link constants: 8080 is `HubOptions.HttpPort`'s default and 8000 is
`AssetClientInstaller.ForgePort`.

| Message | Id | Ch | Encoding | Direction | Size |
|---|---|---|---|---|---|
| `Hello` | 0x01 | 0 | JSON | client → Hub | – |
| `Welcome` | 0x02 | 0 | JSON | Hub → client | – |
| `TPing` | 0x03 | – | binary | both | 13 B |
| `TPong` | 0x04 | – | binary | both | 29 B |
| `State` | 0x10 | 1 | binary | phone → Hub → spectators | 202 B |
| `PeerState` | 0x11 | 1 | binary | Hub → phone | **Reserved** for Phase 3 (`P3-D07`). No codec in Phase 1. A receiver ignores it |
| `ParamManifest` | 0x20 | 0 | JSON | phone → Hub → spectators, editors, consoles | – |
| `ParamSet` | 0x21 | 0 | binary | Hub → phone; editor or console → Hub → phone | 2 + 6 n B |
| `SceneCmd` | 0x22 | 0 | JSON | Hub → phone; editor or console → Hub → phone | – |
| `OperatorCmd` | 0x23 | 0 | JSON | Hub → phone; editor or console → Hub → phone | – |
| `Telemetry` | 0x30 | 0 | binary | phone → Hub | 91 B |
| `Event` | 0x40 | 0 | JSON | phone → Hub → spectators, editors | – |
| `RoomOutline` | 0x41 | 0 | binary | phone → Hub → spectators, editors | 27 + 8 n + 26 m B |
| `Log` | 0x42 | 0 | JSON | phone → Hub | – |
| `JobStatus` | 0x50 | 0 | JSON | Hub → phone, spectators, editors | – |
| `AssetReady` | 0x51 | 0 | JSON | Hub → phone, spectators, editors | – |
| `AssetAck` | 0x52 | 0 | JSON | phone → Hub | – |
| `Bye` | 0x7F | 0 | JSON | both | – |

Compatibility (§7.5 rule 5), the same for every receiver:

- An **unknown id** is dropped without an error.
- **Trailing bytes** after a binary message are ignored. A binary message that is **too short**, or whose
  first byte is not its id, is dropped (`ReadFrom` returns false).
- A JSON message: unknown members are ignored; bytes after the root object are ignored; a body that is not a
  JSON object is dropped.
- A `Hello` whose `proto[0]` is not 1 is answered with `Bye {"reason": "protocol mismatch: the Hub speaks 1.0,
  the client sent 2.0"}` and the peer is disconnected 250 ms later (`LinkHost.Kick`), so that the reason
  arrives first. A different minor version is accepted.
- A new field is only ever **appended** to a binary message, and bumps `proto[1]`.

## 2.3 `State` (0x10, channel 1, 202 bytes)

Everything the Spectator needs to redraw the walker's frame (§7.3.1, Appendix B.1).

| Offset | Size | Field | Type | Unit, range | Meaning |
|---:|---:|---|---|---|---|
| 0 | 1 | `msgType` | u8 | 0x10 | |
| 1 | 1 | `flags` | u8 | bits | bits 0–1 tracking (0 normal, 1 limited, 2 lost); bit 2 pose jump rejected this frame; bit 3 warning cue active; bits 4–5 quality tier 0–3 (the rows of §8.6; the high tier is sent as 0); bit 6 demo lock; bit 7 zero |
| 2 | 2 | `sceneId` | u16 | | 0 … 6: the SpaceLab space (`SpaceIds.SceneId`, lane A §1); 0xFFFF while no space is entered; 100 and above are use-case scenes (Phase 2) |
| 4 | 4 | `seq` | u32 | | +1 per `State` sent; wraps |
| 8 | 8 | `t_us` | i64 | µs | phone clock when the message was written |
| 16 | 12 | `headPos` | 3 × f32 | m | physical head position `x, y, z`, room frame (README F4): `FrameContext.RawHeadPosition` |
| 28 | 16 | `headRot` | 4 × f32 | – | physical head rotation `x, y, z, w`, room frame: `FrameContext.RawHeadRotation` |
| 44 | 1 | `spaceId` | u8 | 0–6, 255 | S0–S6; 255 while no space is entered (onboarding, scan) |
| 45 | 1 | `bumpLevel` | u8 | 0–3 | 0 clear, 1 warn, 2 strong, 3 tracking lost (plan §4.10) |
| 46 | 48 | `tileId` | 6 × i64 | | lane B's wire order `Xu, Xv, Yu, Yv, Tu, Tv`; per space in the table below |
| 94 | 2 | `sheet` | i16 | | Mode 2 cell index, or portal sheet; else 0 |
| 96 | 72 | `xform` | 9 × f64 | kernel units | tile-local transform, row-major `m00, m01, m02, m10, …, m22`; identity when unused |
| 168 | 32 | `p` | 8 × f32 | | `[0]` L (m); `[1]` fog start ρ − 1; `[2]` ρ; `[3]` to `[7]` the space's own: slice w and 4D angles in S6 |
| 200 | 2 | `worldVer` | u16 | | +1 on every regeneration; wraps |

Total 1 + 1 + 2 + 4 + 8 + 12 + 16 + 1 + 1 + 48 + 2 + 72 + 32 + 2 = **202**.

The tracked hands change none of this: `State` has no field, no flag bit (bit 7 stays zero) and no message for them,
and the telemetry row keeps its columns (§5.3). The hands' state reaches the Station only as parameters (1.4) and
events (§2.6), so a recorded session replays on the Hub and the Spectator as it always did, and `tools/ReplayPhone`
needs no change.

```csharp
// File: ATLAS/Assets/Atlas/Link/Messages/StateMessage.cs
namespace Atlas.Link
{
    /// <summary>Appendix B.1. A plain struct of fields with no arrays: filling, copying and sending it allocates nothing.</summary>
    public struct StateMessage
    {
        public const int Size = 202;
        public const int TileLength = 6;
        public const int XformLength = 9;
        public const int ParamLength = 8;
        public const int ParamL = 0;                      // indices into p[] fixed by Appendix B.1
        public const int ParamFogStart = 1;
        public const int ParamCullRho = 2;
        public const int ParamSliceW = 3;

        public byte Flags;
        public ushort SceneId;
        public uint Seq;                                  // written by the link
        public long TimeUs;                               // written by the link
        public float HeadPosX, HeadPosY, HeadPosZ;
        public float HeadRotX, HeadRotY, HeadRotZ, HeadRotW;
        public byte SpaceId;
        public byte BumpLevel;
        public long Tile0, Tile1, Tile2, Tile3, Tile4, Tile5;      // Xu, Xv, Yu, Yv, Tu, Tv
        public short Sheet;
        public double Xform0, Xform1, Xform2, Xform3, Xform4, Xform5, Xform6, Xform7, Xform8;   // row-major: m00, m01, m02, m10, …
        public float P0, P1, P2, P3, P4, P5, P6, P7;
        public ushort WorldVer;

        /// <summary>All zero, except the head rotation (0, 0, 0, 1) and the xform, which are identities.</summary>
        public static StateMessage Identity { get; }

        // The bits of Flags.
        public int Tracking { get; set; }                 // 0 normal, 1 limited, 2 lost
        public bool PoseJumpRejected { get; set; }
        public bool CueActive { get; set; }
        public int QualityTier { get; set; }              // 0-3; the setter keeps the two low bits, so the high tier is sent as 0
        public bool DemoLock { get; set; }

        // Indexed access; an index out of range is an ArgumentOutOfRangeException.
        public long GetTile(int i);
        public void SetTile(int i, long value);
        public void SetTile(long t0, long t1, long t2, long t3, long t4, long t5);
        public double GetXform(int i);
        public void SetXform(int i, double value);
        public void SetXformIdentity();
        public float GetParam(int i);
        public void SetParam(int i, float value);

        /// <summary>Writes exactly 202 bytes at offset and returns 202. ArgumentException when the buffer is too small.</summary>
        public readonly int WriteTo(byte[] buffer, int offset);
        /// <summary>False when length is under 202 or the first byte is not 0x10. Extra bytes are ignored.</summary>
        public bool ReadFrom(byte[] buffer, int offset, int length);
    }
}
```

`byte[]` with an offset is the choice **(decided here)**: it is what the transport hands over and takes, it
compiles under IL2CPP with no unsafe code, and the `Wire` helpers write each width byte by byte, so nothing
depends on the host's endianness or on `BitConverter` overloads that .NET Standard 2.1 lacks.

### Who fills what

Step 8a of the frame, in `FrameLoopCore.RunState` (`App/Frame/FrameLoopCore.cs`, lane A §7.2):

| Field | Written by | From |
|---|---|---|
| `flags` (tracking, pose jump, tier), `sceneId`, `spaceId`, `worldVer`, `headPos`, `headRot` | `FrameLoopCore.FillState` (lane A) | `FrameContext` (raw head pose, tracking, pose-jump flag, tier), `ISpaceHost` |
| `bumpLevel`, the cue bit of `flags` | Bump Guard's state writer, called from `FillState` | lane C §8 |
| `p[0]`, `p[1]`, `p[2]` | `FrameLoopCore.FillState` | `(float)IWalkerKernel.MetresPerUnit`, `CullRho − 1`, `CullRho`, in every space (lane B §8.3) |
| `tileId`, `sheet`, `xform`, `p[3]` to `p[7]` | The entered space, in `ISpaceModule.WriteState(ref StateMessage state)` | Its own state. On entry they hold six zeros, 0, identity and zeros |
| `seq`, `t_us`, the lock bit of `flags` | `LinkClient.SendState`, through lane D's `IStateSink` (`LinkPump`) | Its counter, its clock and its `DemoLock`, at the send. Whatever the caller put there is overwritten |

`FillState` resets every field each frame. `WriteState` must not allocate and must write the values the
frame was drawn with. With no space entered the defaults are sent, with `spaceId` 255 (`SpaceIds.NoSpace`) and
`sceneId` 0xFFFF (`SpaceIds.NoScene`).

| Space | `tileId` | `sheet` | `xform` | `p[3]` to `p[7]` | Source |
|---|---|---|---|---|---|
| S0 Home | six zeros | 0 | identity | zeros | B.1 |
| S1 Glued | `(ci, cj, 0, 0, 0, 0)`: the lattice cell the head is in | 0 | identity | mode, copies per side, cell size x, cell size z, 0 | lane A §14.1 |
| S2 Curved Walk | `Pose.Tile` (the kernel's, written by `FillState`) | 0 | `Pose.Local` | `p[3]` the dial's q, `p[4]` the signed curvature of the frame (1/m²), the rest 0 | lane B §7.3 |
| S3 Aligned Cells | the cell frame's tile id: kept from Mode 1 when S3 is entered from Curved Walk (a classic run); a duck-maze run starts at tile zero | Mode 2 cell index `y · countX + x` | the cell's quarter-turn | `p[3]` shown depth, `p[4]`, `p[5]` cell counts, `p[6]` cell side in metres, `p[7]` the maze code (seed · 16 + start cell) in a duck-maze run, 0 otherwise | lane B §7.3 |
| S4 Small Planet | `Pose.Tile` (the face in word 0) | 0 | `Pose.Local` (κ = +1) | `p[3]` = 3, `p[4]` = 1/R², `p[5]` the opaque end of the fog in radians, `p[6]`, `p[7]` = 0 | lane B §7.3 |
| S5 Impossible Rooms | `(room id, 0, 0, 0, 0, 0)` | current room id | identity | layout, room count, portal depth, 0, 0 | lane A §14.2 |
| S6 Slicer | six zeros | 0 | identity | w₀; the XW, YW, ZW angles in radians; object + 16 · view | lane A §14.3 |

While the kernel is curved (q 3, 5 or 6) `FillState` writes `state.SetTile(t.Xu, …, t.Tv)` and the nine `Xform0` … `Xform8`
fields from `u.M00` … `u.M22`, row by row, with `t = kernel.Pose.Tile` and `u = kernel.Pose.Local` (lane B §4.5, §2.5), so a
space whose walker is the kernel (S2, S4) writes only its own `p[3..7]`; S3, whose walker is its own, overwrites tile, `sheet` and
`xform`. κ is not on the wire: the receiver takes it from `spaceId` (S2, S3 → −1, S4 → +1).

What reaches the Spectator, and how:

- Per-frame values travel in `State`. A space that also implements `ISpaceStateReader` (lane A §2) gets the
  newest `State` before each `Tick` in the Spectator and takes its per-frame values (`p[3..7]`, `tileId`,
  `sheet`) from it.
- Everything slower travels as parameters: `ParamManifest`, then one `param` event per change (§2.6). The
  Spectator applies them to its own registry by name.
- `worldVer` is `ISpaceHost.WorldVersion` (lane A §4). Every change of it is followed by one `world` event
  (§2.6), which carries the world hash.

### The 202-byte example

A `State` as S3 Aligned Cells sends it. `Link_RoundTrip` checks the same layout
field by field against a sample of its own.

| Offset | Size | Field | Type | Bytes (hex) | Value |
|---:|---:|---|---|---|---|
| 0 | 1 | `msgType` | u8 | `10` | State |
| 1 | 1 | `flags` | u8 | `18` | tracking normal, no jump, cue active (0x08), tier 1 (0x10), lock off |
| 2 | 2 | `sceneId` | u16 | `03 00` | 3 = the scene of S3 |
| 4 | 4 | `seq` | u32 | `39 30 00 00` | 12345 |
| 8 | 8 | `t_us` | i64 | `D2 02 96 49 00 00 00 00` | 1 234 567 890 µs |
| 16 | 4 | `headPos.x` | f32 | `00 00 80 3E` | 0.25 m |
| 20 | 4 | `headPos.y` | f32 | `00 00 C0 3F` | 1.5 m |
| 24 | 4 | `headPos.z` | f32 | `00 00 00 BF` | −0.5 m |
| 28 | 4 | `headRot.x` | f32 | `00 00 00 00` | 0 |
| 32 | 4 | `headRot.y` | f32 | `F3 04 35 3F` | 0.70710678 |
| 36 | 4 | `headRot.z` | f32 | `00 00 00 00` | 0 |
| 40 | 4 | `headRot.w` | f32 | `F3 04 35 3F` | 0.70710678 (a turn of 90° about +Y) |
| 44 | 1 | `spaceId` | u8 | `03` | S3 Aligned Cells |
| 45 | 1 | `bumpLevel` | u8 | `01` | warn |
| 46 | 8 | `tileId[0]` Xu | i64 | `FF FF FF FF FF FF FF FF` | −1 |
| 54 | 8 | `tileId[1]` Xv | i64 | `00 00 00 00 00 00 00 00` | 0 |
| 62 | 8 | `tileId[2]` Yu | i64 | `00 00 00 00 00 00 00 00` | 0 |
| 70 | 8 | `tileId[3]` Yv | i64 | `00 00 00 00 00 00 00 00` | 0 |
| 78 | 8 | `tileId[4]` Tu | i64 | `00 00 00 00 00 00 00 00` | 0 |
| 86 | 8 | `tileId[5]` Tv | i64 | `01 00 00 00 00 00 00 00` | 1 (X = −1, Y = 0, T = φ: a neighbour of tile zero in {4,5}; φ·X² − T² = −1) |
| 94 | 2 | `sheet` | i16 | `03 00` | Mode 2 cell index 3 |
| 96 | 8 | `xform.m00` | f64 | `00 00 00 00 00 00 00 00` | 0 |
| 104 | 8 | `xform.m01` | f64 | `00 00 00 00 00 00 F0 BF` | −1 |
| 112 | 8 | `xform.m02` | f64 | `00 00 00 00 00 00 E0 3F` | 0.5 |
| 120 | 8 | `xform.m10` | f64 | `00 00 00 00 00 00 F0 3F` | 1 |
| 128 | 8 | `xform.m11` | f64 | `00 00 00 00 00 00 00 00` | 0 |
| 136 | 8 | `xform.m12` | f64 | `00 00 00 00 00 00 D0 BF` | −0.25 |
| 144 | 8 | `xform.m20` | f64 | `00 00 00 00 00 00 00 00` | 0 |
| 152 | 8 | `xform.m21` | f64 | `00 00 00 00 00 00 00 00` | 0 |
| 160 | 8 | `xform.m22` | f64 | `00 00 00 00 00 00 F0 3F` | 1 (a quarter turn and a shift of (0.5, −0.25): a flat isometry) |
| 168 | 4 | `p[0]` | f32 | `AE 47 F1 3F` | L = 1.885 m |
| 172 | 4 | `p[1]` | f32 | `00 00 00 40` | fog start ρ − 1 = 2 |
| 176 | 4 | `p[2]` | f32 | `00 00 40 40` | ρ = 3 |
| 180 | 4 | `p[3]` | f32 | `00 00 00 00` | 0 (the space's own slots) |
| 184 | 4 | `p[4]` | f32 | `00 00 00 00` | 0 |
| 188 | 4 | `p[5]` | f32 | `00 00 00 00` | 0 |
| 192 | 4 | `p[6]` | f32 | `00 00 00 00` | 0 |
| 196 | 4 | `p[7]` | f32 | `00 00 00 00` | 0 |
| 200 | 2 | `worldVer` | u16 | `07 00` | 7 |

```text
0000  10 18 03 00 39 30 00 00 D2 02 96 49 00 00 00 00
0010  00 00 80 3E 00 00 C0 3F 00 00 00 BF 00 00 00 00
0020  F3 04 35 3F 00 00 00 00 F3 04 35 3F 03 01 FF FF
0030  FF FF FF FF FF FF 00 00 00 00 00 00 00 00 00 00
0040  00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
0050  00 00 00 00 00 00 01 00 00 00 00 00 00 00 03 00
0060  00 00 00 00 00 00 00 00 00 00 00 00 00 00 F0 BF
0070  00 00 00 00 00 00 E0 3F 00 00 00 00 00 00 F0 3F
0080  00 00 00 00 00 00 00 00 00 00 00 00 00 00 D0 BF
0090  00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
00A0  00 00 00 00 00 00 F0 3F AE 47 F1 3F 00 00 00 40
00B0  00 00 40 40 00 00 00 00 00 00 00 00 00 00 00 00
00C0  00 00 00 00 00 00 00 00 07 00
```

SHA-256 of the 202 bytes: `c84417e5bce743aa693e2fe125955e0be0a865f9258c1089a53b33704637bcd0`. A reader given
these bytes followed by any extra bytes returns the same struct; given the first 201 bytes it returns false.

## 2.4 The other binary messages

All are in `Messages/BinaryMessages.cs` and follow the pattern of `StateMessage`: a size, `int WriteTo(byte[]
buffer, int offset)`, `bool ReadFrom(byte[] buffer, int offset, int length)`.

### `TPing` (0x03, 13 B) and `TPong` (0x04, 29 B)

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `msgType` | u8 | 0x03 |
| 1 | `seq` | u32 | sender's ping counter |
| 5 | `t1_us` | i64 | sender's clock at send |

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `msgType` | u8 | 0x04 |
| 1 | `seq` | u32 | copied from the ping |
| 5 | `t1_us` | i64 | copied from the ping |
| 13 | `t2_us` | i64 | answerer's clock when the ping was received |
| 21 | `t3_us` | i64 | answerer's clock when the pong is sent |

Examples: ping `seq` 7, `t1` 1 000 000 → `03 07 00 00 00 40 42 0F 00 00 00 00 00`. Its pong with `t2`
5 000 250, `t3` 5 000 300 →
`04 07 00 00 00 40 42 0F 00 00 00 00 00 3A 4C 4C 00 00 00 00 00 6C 4C 4C 00 00 00 00 00`.

```csharp
public struct TPingMessage { public const int Size = 13;
                             public uint Seq; public long T1Us; /* WriteTo, ReadFrom */ }
public struct TPongMessage { public const int Size = 29;
                             public uint Seq; public long T1Us; public long T2Us; public long T3Us; /* WriteTo, ReadFrom */ }
```

### `ParamSet` (0x21, 2 + 6 n bytes)

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `msgType` | u8 | 0x21 |
| 1 | `count` | u8 | n = 0 … 255 |
| 2 + 6 i | `id` | u16 | wire id (1.1) |
| 4 + 6 i | `value` | f32 | requested value; the phone clamps it |

Example, the parameters with wire ids 3 and 16 to 0.7 and 3:
`21 02 03 00 33 33 33 3F 10 00 00 00 40 40` (14 B).

```csharp
/// <summary>A reusable buffer object: Clear, Add, WriteTo. It owns its arrays; nothing is allocated per send.</summary>
public sealed class ParamSetMessage
{
    public const int MaxCount = 255;
    public const int HeaderSize = 2;
    public const int EntrySize = 6;
    public const int MaxSize = HeaderSize + MaxCount * EntrySize;
    public readonly ushort[] Ids;                     // MaxCount
    public readonly float[] Values;                   // MaxCount
    public int Count { get; }
    public int Size { get; }                          // 2 + 6 * Count
    public void Clear();
    public bool Add(ushort id, float value);          // false when full
    public int WriteTo(byte[] buffer, int offset);
    public bool ReadFrom(byte[] buffer, int offset, int length);   // false when too short for its count
}
```

The phone applies the entries in order with `registry.Apply(set, ParamSource.Remote)`. An id it does not know
is skipped. The whole message is dropped under the demo lock and while the link is not CONNECTED (§2.8).

### `Telemetry` (0x30, 91 bytes)

One row per second (§7.3.3). The CSV has the same fields in the same order (§5.3).

| Offset | Field | Type | Unit | Meaning |
|---:|---|---|---|---|
| 0 | `msgType` | u8 | | 0x30 |
| 1 | `t_us` | i64 | µs | phone clock when the row was made |
| 9 | `seq` | u32 | | row counter, 0 at session start, +1 per row |
| 13 | `sceneId` | u16 | | as in `State` |
| 15 | `spaceId` | u8 | | 0–6 |
| 16 | `tier` | u8 | | quality tier 0–4 (`IQualityState.Tier`; 4 is the high tier) |
| 17 | `fps` | f32 | 1/s | frames in the last second ÷ elapsed time |
| 21 | `p50` | f32 | ms | frame time, median over the ring (§5.2) |
| 25 | `p95` | f32 | ms | |
| 29 | `p99` | f32 | ms | |
| 33 | `max` | f32 | ms | worst frame of the last second |
| 37 | `over17_2` | f32 | % | share of ring frames over 17.2 ms (§11.2) |
| 41 | `over33_8` | f32 | % | share of ring frames over 33.8 ms |
| 45 | `cpuMainP95` | f32 | ms | `cpuMainThreadFrameTime`, p95 over the ring |
| 49 | `cpuRenderP95` | f32 | ms | `cpuRenderThreadFrameTime` |
| 53 | `gpuP95` | f32 | ms | `gpuFrameTime`; 0 when the platform gives none |
| 57 | `renderScale` | f32 | | 0.8, 0.7 or 0.6 |
| 61 | `drawCalls` | u16 | | worst frame of the last second |
| 63 | `setPass` | u16 | | worst frame of the last second |
| 65 | `trisK` | u16 | thousands | worst frame of the last second, rounded |
| 67 | `memMB` | u16 | MiB | "System Used Memory" |
| 69 | `visibleTiles` | u16 | | tiles drawn by the generated world's renderer (`ITileDrawStats`, S2 at q = 5), copied at step 6 by the app; 0 in every other space |
| 71 | `copies` | u16 | | Glued cells drawn: n² for T² and Klein, 27 for T³ (from `State.p[3]`, `p[4]`); 0 outside S1 |
| 73 | `rttMs` | u16 | ms | LiteNetLib round trip; 0 when offline |
| 75 | `lossPermille` | u16 | ‰ | LiteNetLib packet loss |
| 77 | `cueCount` | u16 | | warning cues started since session start; wraps |
| 79 | `gcAllocB` | u32 | B | most managed bytes allocated in one frame of the last second |
| 83 | `thermal` | u8 | 0–3 | nominal, fair, serious, critical |
| 84 | `battery` | u8 | % | 255 when unknown |
| 85 | `tracking` | u8 | 0–2 | normal, limited, lost |
| 86 | `msaa` | u8 | | 1, 2 or 4 |
| 87 | `stateHz` | u8 | Hz | `State` messages sent in the last second |
| 88 | `bumpMax` | u8 | 0–3 | highest Bump Guard level of the last second |
| 89 | `assetsPlaced` | u8 | | objects showing a generated mesh |
| 90 | `assetsPending` | u8 | | objects waiting for one |

17 B of header, 11 × f32, 9 × u16, 1 × u32, 8 × u8 = **91**.

```csharp
public struct TelemetryMessage
{
    public const int Size = 91;
    public long TimeUs; public uint Seq; public ushort SceneId; public byte SpaceId; public byte Tier;
    public float Fps, P50, P95, P99, Max;
    public float Over17_2, Over33_8;
    public float CpuMainP95, CpuRenderP95, GpuP95, RenderScale;
    public ushort DrawCalls, SetPass, TrisK, MemMB, VisibleTiles, Copies, RttMs, LossPermille, CueCount;
    public uint GcAllocB;
    public byte Thermal;
    public byte Battery, Tracking, Msaa, StateHz, BumpMax, AssetsPlaced, AssetsPending;
    public const string CsvHeader = "t_us,seq,sceneId,…,assetsPending";   // the header line of 5.3
    public readonly int WriteTo(byte[] buffer, int offset);
    public bool ReadFrom(byte[] buffer, int offset, int length);
}
```

### `RoomOutline` (0x41, 27 + 8 n + 26 m bytes)

The floor plan the Spectator and the Console's map draw. Sent when a RoomModel becomes active and after every
handshake. All coordinates are in the room frame, metres and radians.

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `msgType` | u8 | 0x41 |
| 1 | `count` | u8 | n = 0 … 255 outline vertices |
| 2 + 8 i | `x`, `z` | 2 × f32 | `RoomModel.Outline[i]`: wall to wall, counter-clockwise seen from above |
| 2 + 8 n | `height` | f32 | the tallest of `RoomModel.Walls[i].Height`; `RoomModel.DefaultWallHeight` (2.4 m) when no wall gives one |
| 6 + 8 n | `rect` | 5 × f32 | free rectangle: centre x, centre z, Wf, Df, yaw. In the room frame the first two and the last are 0 |
| 26 + 8 n | `boxCount` | u8 | m = 0 … 255 objects |
| 27 + 8 n + 26 j | `id` | u8 | `Objects[j].Id` (the link's `boxId`) |
| + 1 | `cat` | u8 | `(byte)Objects[j].Category`: lane C's `ObjectCategory`, 0 `Unknown` … 22 `Other` |
| + 2 | `cx`, `cz` | 2 × f32 | `Base.x`, `Base.z` |
| + 10 | `w`, `d`, `h` | 3 × f32 | `Size.x`, `Size.z`, `Size.y` |
| + 22 | `yaw` | f32 | `Yaw` |

A 3 × 4 m rectangle with one table is 27 + 32 + 26 = 85 B.

```csharp
public struct RoomOutlineBox { public const int Size = 26; public byte Id; public byte Category; public float Cx, Cz, W, D, H, Yaw; }

/// <summary>A reusable buffer object: fill it, then WriteTo. It owns its arrays; nothing is allocated per send.</summary>
public sealed class RoomOutlineMessage
{
    public const int MaxPoints = 255;
    public const int MaxBoxes = 255;
    public const int MaxSize = 2 + MaxPoints * 8 + 4 + 20 + 1 + MaxBoxes * RoomOutlineBox.Size;
    public readonly float[] PointsXZ;                 // x0, z0, x1, z1, …; MaxPoints * 2
    public int PointCount;
    public float Height;
    public float RectCx, RectCz, RectW, RectD, RectYaw;
    public readonly RoomOutlineBox[] Boxes;           // MaxBoxes
    public int BoxCount;
    public int Size { get; }                          // 27 + 8 * PointCount + 26 * BoxCount
    public void Clear();
    public bool AddPoint(float x, float z);           // false when full
    public bool AddBox(in RoomOutlineBox box);
    public int WriteTo(byte[] buffer, int offset);
    public bool ReadFrom(byte[] buffer, int offset, int length);
}
```

`Atlas.Link` knows no RoomModel type. `Atlas.Telemetry.LinkPump.SetRoom(RoomModel)` fills a
`RoomOutlineMessage` from the RoomModel (lane C's types) and calls `LinkClient.SendRoomOutline`: the installer
subscribes it to `ISpaceHost.RoomChanged` (§5.1).

## 2.5 The JSON messages

Byte 0 is the id; the rest is one JSON object (§1.8). Every member is listed; a missing member takes the default
in brackets. Each message is a class in `JsonMessages.cs` named `<Message>Message`, derived from `JsonMessage`:

```csharp
public abstract class JsonMessage
{
    public abstract byte Id { get; }
    public abstract JsonValue ToJson();                              // member order is fixed
    public abstract void FromJson(JsonValue json);                   // missing members take defaults; unknown ones are ignored
    public byte[] ToBytes();                                         // id byte + compact UTF-8 JSON
    public int WriteTo(byte[] buffer, int offset);
    public bool ReadFrom(byte[] buffer, int offset, int length);     // false for another id or a body that is not an object
    public static byte[] Encode(byte id, JsonValue body);
    public static bool TryDecode(byte[] buffer, int offset, int length, out byte id, out JsonValue body);
}
// HelloMessage, WelcomeMessage, ParamManifestMessage, SceneCmdMessage, OperatorCmdMessage, EventMessage,
// LogMessage, JobStatusMessage, AssetReadyMessage, AssetAckMessage, ByeMessage: public fields named after the
// JSON members in PascalCase (HelloMessage.WorldHash, AssetReadyMessage.BoundsX, …).
```

**`Hello` (0x01), client → Hub.** Sent first after every connect, and again whenever the room, the world or
the session changes (`LinkClient.SetWorld`, `SetSession`).

```json
{"proto":[1,0],"role":"phone","device":"iPhone16,2","app":"atlas","build":"d0e9127",
 "worldHash":"141060c2fc138145","roomId":"sample_3x3","sessionId":"20261004T153012Z-a3f9"}
```

| Member | Type | Meaning |
|---|---|---|
| `proto` | `[int, int]` | `[1, 0]` |
| `role` | string | `phone`, `spectator`, `editor` or `console` (§2.11); anything else is read as unknown: that peer gets a `Welcome` and nothing more |
| `device` | string [`""`] | `SystemInfo.deviceModel`, or `ReplayPhone` |
| `app` | string [`""`] | `atlas` |
| `build` | string [`""`] | `Application.version` |
| `worldHash` | string [`""`] | 16 hexadecimal digits (below); empty while no space is entered |
| `roomId` | string [`""`] | `RoomModel.Id`; empty when there is no RoomModel |
| `sessionId` | string [`""`] | Phone only **(decided here)**: the session id the phone made at start (§5.5). The Hub adopts it, so the phone's files and the Hub's folder carry the same name. Empty from a spectator |

**`Welcome` (0x02), Hub → client.** Answers every `Hello`. Sent again to every spectator and editor when the
phone's session, world, room or the Forge URL changed since the last one that client got.

```json
{"proto":[1,0],"sessionId":"20261004T153012Z-a3f9","worldHash":"141060c2fc138145","roomId":"sample_3x3",
 "demoLock":false,"forgeUrl":"http://192.168.0.10:8000"}
```

| Member | Type | Meaning |
|---|---|---|
| `sessionId` | string | The session in force. To a phone: its own id back, unless the Hub was started with `--session` |
| `worldHash`, `roomId` | string | The phone's last values: from its `Hello`, kept current from its `world` events |
| `demoLock` | bool [false] | True asks the phone to switch `demoLock` on. False changes nothing: a lock is never lifted by `Welcome` |
| `forgeUrl` | string [`""`] | Base URL of the Forge, no trailing slash. Empty: the client uses `http://<hub address>:8000` |

A client that receives a `Welcome` with another `proto[0]` does not go on with the handshake and notes the
mismatch in `LastByeReason`.

**`worldHash`.** `WorldHash.Compute(spaceId, q, seed, generatorVersion, geometryHash, lMillimetres)` (§7.3.1):

```text
text      = "<spaceId>|4,<q>|<seed>|<generatorVersion>|<geometryHash>|<lMillimetres>"     decimal integers, UTF-8
worldHash = the first 16 hexadecimal digits (lower case) of SHA-256(text)

spaceId          State.spaceId                                     q        IWalkerKernel.Graph.Q (4 in a flat space)
seed             ISpaceWorld.WorldSeed of the entered module;      genVer   ISpaceWorld.GeneratorVersion; 1 without one
                 0 for a space with no generator (every one but S2)
geometryHash     lane C's RoomModelJson.GeometryHash(room): 64 lower-case hexadecimal digits (lane C §2.6);
                 64 zeros (WorldHash.NoGeometry) when no RoomModel is active or the room has no hash yet
lMillimetres     WorldHash.Millimetres(L): × 1000, rounded half away from zero. L is IDialKernel.ConfiguredMetresPerUnit (the dial's
                 target, constant through a curvature ramp) when the kernel is one, else IWalkerKernel.MetresPerUnit
```

`LinkPump` computes it at step 8a whenever `State.worldVer` or `spaceId` changes, and when the room changes;
it hands it to `LinkClient.SetWorld` (which says `Hello` again when it changed) and logs the `world` event.
The Spectator computes the same from its own kernel, room and space and compares. Test vector: space 1, q 4,
seed 0, version 1, geometry hash `9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08`,
L 1000 mm → text `1|4,4|0|1|9f86…0a08|1000` → `141060c2fc138145`.

**`ParamManifest` (0x20), phone → Hub → spectators, editors, consoles.** Sent as the last step of the
handshake and whenever `ManifestVersion` changed since the last one sent (compared every frame at step 8b by
`LinkPump`). Always the whole registry. A client that joins later is given the kept manifest with the values
the Hub knows at that moment.

```json
{"params":[
 {"id":2,"name":"render.scale","group":"render","min":0.6,"max":0.8,"def":0.8,"val":0.8,"onRelease":true,
  "kind":"float","active":true},
 {"id":31,"name":"glued.mode","group":"glued","min":0.0,"max":2.0,"def":0.0,"val":0.0,"onRelease":true,
  "kind":"enum","active":false,"names":["t2","klein","t3"]}]}
```

`id`, `name`, `group`, `min`, `max`, `def`, `val`, `onRelease` are Appendix B.2. `kind` (`float`, `int`,
`bool`, `enum`), `active` (the group's state, 1.1; read as true when missing), `names` (enum options, enums
only) and `live` (written only when true) are added **(decided here)** so the Console can draw a switch or a
selector and fold an inactive group with no further code. `ParamManifestMessage.FromRegistry(registry)` makes
it; `ApplyTo(registry)` brings another registry in step with it by name.

**`SceneCmd` (0x22), Hub → phone.**

```json
{"space":"S1","variant":"klein","args":{"glued.copies":3}}
```

| Member | Type | Meaning |
|---|---|---|
| `space` | string | `S0` … `S6`; anything else is dropped |
| `variant` | string [`""`] | One option of the space's variant parameter, by name, case ignored: `glued.mode` (S1), `rooms.layout` (S5), `slicer.object` (S6) (lane A §14). Other spaces ignore it |
| `args` | object [`{}`] | Parameter name → number or boolean, set before the switch |

**`OperatorCmd` (0x23), Hub → phone.**

```json
{"cmd":"quality","args":{"tier":2}}
```

| `cmd` | `args` | Meaning |
|---|---|---|
| `home` | `{}` | Return to Home |
| `recentre` | `{}` | Re-centre the current space on the walker |
| `quality` | `{"tier": −1 … 4}`: −1 automatic, 0 … 3 the ladder's rungs, 4 the high tier; clamped | Pin a ladder rung |
| `cue` | `{}` | The Console's **STOP** button: the warning cue at STRONG level |
| `lock` | `{"on": true \| false}` | Demo lock |

An unknown `cmd` is dropped. §2.9 gives the local call each one becomes. Neither command carries a counter or
a send time; the Hub notes the time it sent each one in `hub.log`.

**`Event` (0x40), phone → Hub → spectators, editors.** §2.6.

**`Log` (0x42), phone → Hub.** Warnings and errors from `Application.logMessageReceived`, and from lane D's own
code.

```json
{"t_us":1234567890,"level":"warn","msg":"Forge unreachable, placing the built-in set"}
```

`level` is `warn` or `error`. At most 20 lines per second are taken from `Application.logMessageReceived`; the
rest of that second are dropped.

**`JobStatus` (0x50), Hub → phone, spectators, editors.** The Forge's notification, relayed (§3.7).

```json
{"jobId":"1a2b3c4d5e6f7a8b","roomId":"sample_3x3","boxId":4,"state":"generating","source":"local","error":""}
```

`state`: `queued`, `matting`, `generating`, `cleaning`, `ready`, `failed`, and `placeholder` for a box that
receives no mesh. `source`: `local`, `hosted`, `cache`. `error`: empty unless `failed`; a sentence for a
person, not a code (§3.7).

**`AssetReady` (0x51), Hub → phone, spectators, editors.**

```json
{"jobId":"1a2b3c4d5e6f7a8b","roomId":"sample_3x3","boxId":4,
 "sha256":"3f0a…64 hexadecimal digits…9c1e","url":"/assets/3f0a…9c1e.glb",
 "bytes":183220,"tris":4998,"maxEdge":0.2496,"bounds":[1.2,0.75,0.8],"palette":110}
```

`url` is a path on the Forge, to which the client prepends the Forge URL, or an absolute `http://` URL when
the Forge was started with `FORGE_PUBLIC_URL`. `bounds` is the size of the mesh's bounding box `[x, y, z]` in
metres. `palette` is the palette cell that covers the most surface of the mesh (§4.1). `maxEdge` is in metres.

When the job has a light copy (§4.1) four more members follow, together, and only then:

```json
 ,"lightSha256":"7c1d…64 hexadecimal digits…02ab","lightUrl":"/assets/7c1d…02ab.glb","lightBytes":61240,"lightTris":1875
```

`lightSha256`, `lightUrl`, `lightBytes` and `lightTris` describe the light copy as `sha256`, `url`, `bytes` and `tris`
describe the full one. They are written only when `AssetReadyMessage.LightSha256` is not empty. A message without
them reads as `""` and 0, and a reader that ignores them shows the full copy everywhere. `lightSha256` can equal
`sha256`: a full copy that is already within the light target is also the light copy.

**`AssetAck` (0x52), phone → Hub.**

```json
{"jobId":"1a2b3c4d5e6f7a8b","boxId":4,"sha256":"3f0a…9c1e","ok":true,"loadMs":212.5,"worstFrameMs":17.25,"reason":""}
```

`reason` when `ok` is false: `download_failed`, `hash_mismatch`, `too_large`, `validate:<check>` (§4.2),
`load_failed`.

**`Bye` (0x7F), both.** `{"reason":"quit"}`. Reasons in use: `quit`, `stop`, `link.enabled off`, `hub
stopping`, `another phone is already connected`, and the proto refusal of §2.2. A side that leaves sends the
`Bye` inside the disconnect itself (`ILinkTransport.Disconnect(peer, bytes, …)`), so it cannot be overtaken;
the receiver does not answer.

## 2.6 Events

Wire and file format, exactly Appendix B.2:

```json
{"t_us":1234567890,"scene":"S1","ev":"space.entered","data":{"from":"S0","to":"S1"}}
```

| Member | Type | Rule |
|---|---|---|
| `t_us` | int | Phone clock, microseconds |
| `scene` | string | `S0` … `S6` while that space is entered, `none` otherwise; a use case's name in Phase 2 |
| `ev` | string | `<area>.<name>` or one word |
| `data` | object | Always present, `{}` when empty |

Every event is appended to the phone's event log (§5.5) whether or not the link is up, and sent as `Event` when
it is. While the link is not ready up to 256 events wait in a queue (`EventLog.MaxQueued`) and are sent after
the next handshake, 16 per frame, in order; older ones are dropped from the queue, never from the file.

Events in this build. A lane adds a name by adding a row.

| `ev` | `data` | Raised by | When |
|---|---|---|---|
| `boot` | `{build, device, os}` | D (`TelemetryInstaller`) | App start |
| `session` | `{sessionId}` | D | A session starts (§5.5) |
| `link` | `{state, prev}`: `offline`, `resync`, `connected`, `degraded` | D (`LinkPump`) | Watchdog state change |
| `param` | `{id, name, value, source: "local" \| "remote" \| "restore"}` (`LinkEvents.ParamChanged`) | D (`LinkPump`) | One event per applied change: this is the echo of §7.3.2. A `remote` or `restore` change is reported at once. A `local` change only marks the entry and is reported at step 8b at most once per second, with the value of that moment. A `local` change of `slicer.w`, `slicer.xw`, `slicer.yw`, `slicer.zw` is not reported. A change made while the Station is away goes to the phone's file only; the manifest of the next handshake carries the values |
| `param.set` | `{name, value, src: "menu"}` | D (`MenuModel`) | The on-device menu applied a value (§5.7); the `param` event follows |
| `param.ignored` | `{reason: "lock" \| "degraded", count, ids: [wire ids]}` | D (`LinkPump`) | A `ParamSet` was dropped. The Hub puts its record of those parameters back to what the phone holds |
| `cmd` | `{cmd: "space" \| "home" \| "recentre" \| "quality" \| "cue" \| "lock" \| "hands"}` plus `space` and `variant`, `tier`, or `on` (`OperatorCommand.ToEvent`) | D (`LinkPump`) | An operator command was handed to `IOperatorCommands` (§2.9) |
| `space.entered` | `{from, to}` | D (`LinkPump`), from `State.spaceId` at step 8a | A space was entered |
| `space.refused` | `{space, reason}` | A (SpaceLab) | `RequestSwitch` refused (lane A §4.1) |
| `space.loadFailed`, `space.error` | `{space}`, `{<where>: message}` | A (SpaceLab) | A module could not be created; a module threw |
| `world` | `{worldVer, worldHash, space, roomId, geomHash, q, seed, genVer, lMm}` | D (`LinkPump`), at step 8a | `State.worldVer` or `spaceId` changed (§2.5, `worldHash`). The Hub keeps the newest and gives it to a spectator that joins later |
| `quality.tier` | `{from, to, thermal}` | D, from `IQualityState.TierChanged` | The quality tier changed |
| `bump.level` | `{level, prev}` | D, from `State.bumpLevel` | The Bump Guard level changed |
| `tracking.lost`, `tracking.back` | `{state}`, `{}` | D, from `State.flags` | Tracking left normal (`state` 1 limited, 2 lost); tracking is normal again |
| `pose.jump` | `{}` | D, from `State.flags` | A pose jump was rejected |
| `room.active`, `room.noEntrySpace`, `room.loadFailed` | `{id}`, `{space}`, … | A (`RoomHandOver`) | A RoomModel became active; its entry space is not in the build; a room could not be loaded |
| `onboarding.step` | `{step}` | C (onboarding) | Onboarding moved to another step |
| `headset.on`, `headset.off` | `{}` | A | The display mode changed |
| `portal.cross`, `rooms.loop`, `slicer.object` | `{from, to}`, `{room, laps}`, `{object}` | A (S5, S6) | Space milestones |
| `asset` | `{step, boxId, …}`; `step`: `room`, `upload`, `requested`, `cached`, `ready`, `placed`, `builtin`, `failed`, `rejected`, `unreachable`, `reachable`, `light_placed`, `light_dropped`, `light_deferred`, `light_same`; with `jobId`, `sha256`, `reason`, `attempt`, `tris`, `loadMs` and, for `builtin`, `placed`, `kept` and `shape`, as the step has them (§4.3, step 8) | D (`AssetClientCore`) | Asset client (§4.3; §11.4.7 reads these) |
| `menu` | `{state: "open" \| "close"}` | D (`MenuModel`) | On-device menu opened or closed |
| `hands.on` | `{hz}` | Hands (`HandsRuntime`) | The hands' gate became active and the detector started, at 30 or 15 detections a second, or at the fixed rate of `hands.rate` |
| `hands.off` | `{reason: "mode" \| "pad" \| "param" \| "source" \| "pose" \| "heat"}` | Hands | The gate left active: the display is not the headset, a gamepad is connected, `hands.enabled` is off, the source is unavailable, the head does not come from the AR camera, or the tier is Serious or Critical. `hands.stats` follows at once |
| `hands.rate` | `{hz, why: "tier" \| "governor" \| "idle" \| "override"}` | Hands | The detector's rate changed while the gate stayed active: 30 and 15 by the tier, 15 when the self-governor found results older than 0.060 s or the hands' own frame time over 0.6 ms for 2 s, 5 after 20 s with no hand in view, or `hands.rate` fixed it |
| `hands.enter` | `{hand: "L" \| "R"}` | Hands | A hand became tracked. At most one a second for each hand: an enter that comes sooner is not logged and is counted in `quietEvents` of `hands.stats` |
| `hands.exit` | `{hand, cause: "edge" \| "lost", visibleMs}` | Hands | A hand stopped being tracked, 0.40 s after its last detection. `cause` is `edge` when its palm was within the picture's border band when it went, `lost` otherwise; `visibleMs` counts from its enter. Every exit is logged |
| `hands.touch` | `{hand, label, tag}` | Hands | A fingertip fired a gaze target: on the press, and on each repeat of a `Repeat` target. `label` and `tag` are the target's (`label` is `none` when it has none). The firing itself is an ordinary gaze firing (lane C §13.7); this event is the only record that a hand did it |
| `hands.pinch` | `{hand, label}` | Hands | A pinch fired the target under the gaze. A pinch with nothing under the gaze fires nothing and logs nothing |
| `hands.two` | `{phase: "start"}`, then `{phase: "end", dist, angle}` | Hands | A two-hand pinch started and ended: at the end the change of the distance between the two pinch points, metres, and of their angle, degrees. Recognised and reported; nothing is bound to it |
| `hands.stats` | `{hz, ageMsP50, ageMsP95, requestMs, mainMs, visibleL, visibleR, presses, pinches, maskFrames, scale, image, depth, fx, fy, quietEvents, source}` | Hands | With every `hands.off`, and when the app stops during a session as far as the event log is still open (the app shuts the telemetry down before the hands). `hz` is detections a second over the session so far, `ageMsP50` and `ageMsP95` the age of the last 256 results at the poll, `requestMs` the detector's last request, `mainMs` the hands' own main-thread time a frame (smoothed), `visibleL` and `visibleR` the shares of the session each hand was shown, `presses` and `pinches` the firings, `maskFrames` the frames that had a depth mask, `scale` the walker's hand size against the template, `image` and `depth` the sizes of the last picture and depth image as text (`<width>x<height>`), `fx` and `fy` the picture's focal lengths, `quietEvents` the enters not logged, `source` the source's name (`vision`, `editor`, `replay`, `none`). The counters start again after the event |
| `hands.bench` | the members of `hands.stats` | Hands | While `hands.bench` is on and a session runs, once a second, with the figures of the session so far |
| `hands.fault` | `{code}`: a number or text | Hands | The detector's fault code as a number (−1 not started, −2 the AR session is gone, −3 no new camera frame for 2 s, −4 thirty failed requests in a row, −5 the plugin's record is not `HandsDetection`'s), once when the code differs from the last one reported: the hands then ask the detector to start again every 5 s. Or text: `sense: …`, `upload: …` or `after: …` with the exception's type and message, once, after which the hands are off until the app restarts (decision R9); or `mask`, once, when Bump Guard's step first caught the mask source throwing and ran unmasked |
| `perf.runBegin`, `perf.runEnd`, `perf.summary`, `perf.note` | the perf tour's | A (`App/PerfTour`) | The scripted performance tour |
| `fms`, `seq`, `pg.fmsAlert`, `note`, `asset_ack` | `{score}` (0 to 20), `{score}` (1 to 7), `{score, stop}`, `{text}`, the `AssetAck` body | Hub | Console entries (§3.5) and acknowledgements; written by the Hub with the Hub clock and `"scene": "hub"`, never sent to the phone. `pg.fmsAlert` is written right after an `fms` whose score is at or above `pg.fmsStop` (the phone's value from the manifest and snapshot; 4 until the phone declares it); the phone never writes `fms`, `seq` or `pg.fmsAlert` (P2-C09, P2-C17) |

The hands log edges only, through `IEventLog`, and nothing per frame; the Hub records every event and the Console lists
one it does not know, so no Station code was needed. The gate's events (`hands.on`, `hands.off` with the `hands.stats`
that follows it, `hands.rate`) are logged at step 1, where the gate is decided; the others at step 8b, and a
`hands.fault` where the exception was caught (steps 1, 6 or 8b). No world-touch mark is logged. `State` is not touched
(§2.3).

Lane B's spaces log `walk.corner`, `walk.lap` and `walk.closed` (S2 and S4: `{q, corner or laps or corners, flag_m or closure_m, turn_deg}`), `cells.enter` (S3) and `curved.objectScale` (S2); the Lung and FieldFit blades log their own (`fork`, `compass_choice`, `blindspot_*`, `ff.*` and others) under their own scene through `EventLog.LogJson(scene, …)`.

**Open: how the phone learns the FMS score.** Lane B's `AirwayModeSwitch` and `AirwayRunReport` read an `fms` event from the phone's own event log for the automatic Mode 2 rule (FMS above 3 of 20, plan §10.2.3). The row above says `fms` is written by the Hub with scene `"hub"` and never sent to the phone, so on a real link the phone's log never holds one. No channel exists yet: lane C or D, or the lead, must choose one (a parameter written through `ParamSet`, a new operator command, or a new message). Nothing in this contract invents it.

`LinkEvents` in `Atlas.Link` holds the names the link itself reads: `Param` (`"param"`), `Command` (`"cmd"`),
`Link` (`"link"`), `World` (`"world"`), `ParamIgnoredEvent` (`"param.ignored"`), the two reasons, and
`ParamChanged(…)`, `ParamIgnored(…)`, `TryReadParam(EventMessage, out ushort id, out string name, out float value)`.

There is no `scan.state`, `object.mesh`, `input.action`, `thermal` or `quality.critical` event in this build:
a scan's progress is read from `onboarding.step` and `room.active`, an object's mesh from the `asset` events.

Tile ids inside `data` are lane B's `TileId.ToString()` text.

The API. The interface is in `Atlas.World` **(decided here)**, so that `Atlas.Rendering`, `Atlas.Scan`, the
spaces and `Atlas.App` can all log with no reference to `Atlas.Telemetry`. It carries no link type.

```csharp
// File: ATLAS/Assets/Atlas/World/Services/IEventLog.cs      (lane D's file; System only)
namespace Atlas.World
{
    /// <summary>The session's event log (Master Plan Appendix B.2, Event). Published through AtlasServices.</summary>
    public interface IEventLog
    {
        void Log(string ev);
        void Log(string ev, string key, double value);
        void Log(string ev, string key, string value);
        void Log(string ev, string key1, double value1, string key2, double value2);
        /// <summary>dataJson is one JSON object as text, for example {"from":"S0","to":"S1"}.</summary>
        void LogJson(string ev, string dataJson);
    }
}
```

```csharp
// File: ATLAS/Assets/Atlas/Telemetry/Events/EventLog.cs
namespace Atlas.Telemetry
{
    public sealed class EventLog : Atlas.World.IEventLog
    {
        public const int MaxQueued = 256;
        public const string NoScene = "none";

        public EventLog(Atlas.Link.ILinkClock clock, LogFileWriter files);   // files null: nothing goes to disk
        public Atlas.Link.LinkClient Link { get; set; }       // set by LinkPump
        public string Scene { get; set; }                     // "S0".."S6" or "none"; set by LinkPump at step 8a
        public int Count { get; }
        public int Queued { get; }
        public event System.Action<Atlas.Link.EventMessage> Logged;       // raised after the line is written

        /// <summary>For an event that belongs to a scene other than the entered space: a use case names itself
        /// ("lung", "fieldfit"). Null or empty keeps the current scene.</summary>
        public void LogJson(string scene, string ev, string dataJson);

        // IEventLog members, and for code that references Atlas.Link:
        public void Log(string ev, Atlas.Link.JsonValue data);
        public void Log(Atlas.Link.EventMessage message);
        public void LogLocal(Atlas.Link.EventMessage message);    // the phone's file only
        public int SendQueued(int max = 16);
        /// <summary>A warning or an error: one line in the phone log, and a Log message when toLink and the link is up.</summary>
        public void LogLine(string level, string message, bool toLink = true);
    }
}
```

- `AtlasServices.TryGet<IEventLog>(out log)`; a caller that finds none skips the event.
- **Scene.** `EventLog.Scene` is set by `LinkPump` to the code of the entered space ("S0" … "S6") on every change, so an event logged
  with `Log` or `LogJson(ev, data)` carries the space's code. An event of a use case that must carry its own scene (lane B's Lung and
  FieldFit blades log under "lung" and "fieldfit") uses `EventLog.LogJson(scene, ev, dataJson)`, or
  `EventLogExtensions.LogJson(this IEventLog, scene, ev, dataJson)` (`using Atlas.Telemetry;`) for a caller that holds the interface. A test
  double that implements only `IEventLog` receives the two-argument call and no scene: a test of a scene tag needs a real `EventLog`.
- `IEventLog.Log` writes a whole-valued `double` as an integer. `LogJson` with text that is not a JSON object
  logs `{"text": "<the text>"}`.
- Calls are main thread only, and allowed at step 8 and outside the frame path. Logging is for edges and
  milestones: a space does not log from `Tick`.
- `Atlas.Safety` raises no event and cannot (README F1): its level reaches the log through `State.bumpLevel`.
- `Atlas.Input` cannot either; the code that reacts to an input logs.

## 2.7 Clock sync

Both clocks are monotonic microseconds from `Stopwatch` (`StopwatchClock`). The Hub's clock is the reference
for the phone and for the spectators (§7.2).

```text
A sends TPing(seq, t1).   B receives it at t2 and answers at once with TPong(seq, t1, t2, t3).   A receives it at t4.
rtt    = (t4 − t1) − (t3 − t2)
offset = ((t2 − t1) + (t3 − t4)) / 2                      # B's clock minus A's clock, µs, integer
discard the pong when rtt < 0, or when it answers no open ping of this instance (a duplicate, one from before a restart)
keep the last 16 accepted samples; best = the one with the smallest rtt
first 8 accepted samples after a restart:   offset = best.offset
afterwards:                                 offset = offset + 0.1 × (best.offset − offset);  OffsetUs = round(offset)
```

- Schedule on a client: 8 pings 50 ms apart after `Welcome`, then one per second.
- The Hub also pings every client: 8 pings 50 ms apart after its `Hello`, then 4 times per second **(decided
  here)**. That is its heartbeat (§2.8) and gives the Hub its own offset and RTT per client, from which it
  computes the latency of each `State` (§3.2).
- `t4`, and `t2` of an answer, are the time the packet **arrived**, not the time `Poll` dispatched it: a
  transport that implements `ILinkReceiveStamps` (`StampWith(ILinkClock)`, `LastReceiveUs`) stamps each packet
  on its receive thread, and `LinkClient` and `LinkHost` read the stamp inside `OnReceive`. `t3` is the clock
  at the send, so the wait for the poll lies inside `t3 − t2`, where the other side subtracts it. Read at the
  poll instead, a steady 60 Hz loop cost the phone about 6 ms at p95 (every round trip measures a whole number
  of frames, so the minimum-RTT rule has nothing to choose from); stamped, both ends hold 2 ms at p95
  (`Link_ReceiveStamp`). A transport without the interface falls back to the poll time. `LoopbackNetwork`
  stamps arrivals only with `ArrivalStamps = true`.
- Example: `t1` 1 000 000, `t2` 5 000 250, `t3` 5 000 300, `t4` 1 000 650 → `rtt` 600 µs, `offset` 3 999 950 µs.
- LiteNetLib's `RoundTripTime` feeds the link indicator only. Its NTP helper is not used.

```csharp
// File: ATLAS/Assets/Atlas/Link/ClockSync.cs
namespace Atlas.Link
{
    public interface ILinkClock { long NowUs { get; } }
    public sealed class StopwatchClock : ILinkClock { public long NowUs { get; } }
    /// <summary>A clock that moves only when told to. Tests and replay tools.</summary>
    public sealed class ManualClock : ILinkClock
    {
        public ManualClock(long startUs = 0);
        public long NowUs { get; set; }
        public void AdvanceUs(long us);
        public void AdvanceMs(double ms);
    }

    /// <summary>Engine-free. Reads no clock: every time is an argument.</summary>
    public sealed class ClockSync
    {
        public const int BurstCount = 8;
        public const long BurstIntervalUs = 50000;
        public const long SteadyIntervalUs = 1000000;
        public const int WindowSize = 16;
        public const double Alpha = 0.1;
        public const long BurstGraceUs = 250000;
        public long SteadyPeriodUs = SteadyIntervalUs;    // the Hub sets 250 000

        public bool HasEstimate { get; }              // at least one pong accepted since the last restart
        public long OffsetUs { get; }                 // remote clock minus local clock
        public long RttUs { get; }                    // of the best sample in the window
        public long LastRttUs { get; }
        public int SampleCount { get; }
        public int PingsSent { get; }
        public bool BurstComplete(long nowUs);        // 8 pongs accepted, or 250 ms after the eighth ping left
        public void Restart(long nowUs);
        public void Stop();
        public bool TryNextPing(long nowUs, out TPingMessage ping);   // true when one is due; fills seq and t1
        public bool OnPong(in TPongMessage pong, long t4Us);          // false for a pong that was not accepted
        public static TPongMessage Answer(in TPingMessage ping, long t2Us, long t3Us);
        public long ToRemoteUs(long localUs);
        public long ToLocalUs(long remoteUs);
    }
}
```

## 2.8 Watchdog, reconnect and resync

| State | Condition | Behaviour on the phone |
|---|---|---|
| `Connected` | A packet from the Hub within the last 500 ms | Normal |
| `Degraded` | Silent for 500 ms or more, peer still connected. Left only after 2,000 ms in which the silence never reached 500 ms | `ParamSet` is dropped. Everything else continues |
| `Offline` | No peer: never connected, timed out after 3,000 ms of silence, or the transport dropped it | The app is unchanged. Reconnect attempts with back-off. The asset client polls the Forge (§4.3) |
| `Resync` | Connected a moment ago: from the transport's connect until the handshake is done | The sequence below. `ParamSet` is dropped until `Connected` |

- "A packet" is any LiteNetLib packet from the peer, keep-alives included: `ILinkTransport.GetSilenceMs`
  (`peer.TimeSinceLastPacket`). The Hub's 4 Hz ping makes the longest healthy gap 250 ms, so "two missed
  pings" (§7.2) is the 500 ms above.
- **Back-off.** One attempt is one `Connect` call; it has failed after 500 ms (§2.1). After `Start` the first
  attempt is made at once and the next ones follow 0.5, 1, 2, 4, 4, … s apart. After a drop the first attempt
  comes 0.5 s later, then 1, 2, 4, 4, … s. A successful connect resets it. `LinkClient.ReconnectNow()`, a new
  Hub address (`SetAddress`) and a return from the background while offline start an attempt at once and
  reset the back-off.
- **Resync**, in order: (1) the client sends `Hello`; (2) the Hub answers `Welcome`, or `Bye`; with no answer
  the watchdog goes `Offline` at 3 s of silence; (3) the clock burst, 8 pings; (4) a phone sends
  `ParamManifest` and is `Connected`; (5) at step 8b `LinkPump` then sends the `RoomOutline` if a RoomModel is
  active, its queued events (16 per frame) and up to 600 `Telemetry` rows it could not send (16 per frame);
  (6) on the first manifest of a new connection the Hub sends every snapshot value that differs from the
  manifest's in `ParamSet`s (matched by name) and every `AssetReady` that has no `AssetAck`.
- With `link.enabled` off there is no socket and the state is `Offline`.

```csharp
// File: ATLAS/Assets/Atlas/Link/LinkWatchdog.cs
namespace Atlas.Link
{
    public enum LinkState : byte { Offline = 0, Resync = 1, Connected = 2, Degraded = 3 }

    /// <summary>Engine-free state machine. Reads no clock and no socket: the caller reports what happened and when.</summary>
    public sealed class LinkWatchdog
    {
        public const long DefaultDegradedAfterUs = 500000;
        public const long DefaultOfflineAfterUs = 3000000;
        public const long DefaultRecoverAfterUs = 2000000;
        public const long FirstBackoffUs = 500000;
        public const long MaxBackoffUs = 4000000;
        public long DegradedAfterUs = DefaultDegradedAfterUs;
        public long OfflineAfterUs = DefaultOfflineAfterUs;
        public long RecoverAfterUs = DefaultRecoverAfterUs;

        public LinkState State { get; }
        public event System.Action<LinkState, LinkState> StateChanged;   // (previous, current)
        public bool AcceptsRemoteParams { get; }                // State == Connected
        public bool HasPeer { get; }
        public long BackoffUs { get; }
        public long NextAttemptUs { get; }
        public long SilenceUs(long nowUs);
        public void Start(long nowUs);                          // Offline, first attempt due at once
        public void Stop();
        public bool ShouldConnect(long nowUs);                  // true when an attempt is due; schedules the next
        public void OnTransportConnected(long nowUs);           // → Resync, back-off reset
        public void OnResyncComplete(long nowUs);               // → Connected
        public void OnTransportDisconnected(long nowUs);        // → Offline, first retry in 0.5 s
        public void OnPacket(long nowUs);
        public void OnSilence(long nowUs, long silenceUs);      // what the transport says, keep-alives included
        public LinkState Tick(long nowUs);                      // Connected ↔ Degraded, Offline at 3 s
    }
}
```

The Hub runs one `LinkWatchdog` per client as well and frees a phone's seat after 3 s of silence.

**Link indicator** (§7.2). In hand: a state dot (green `Connected`, amber `Degraded` or `Resync`, grey
`Offline`), Hub address, RTT, loss, Forge reachability, "Reconnect", "Change IP" (§5.6). In the headset: one
dot at the lower edge of both eyes, refreshed at 2 Hz, hidden while `Connected` under demo lock (§5.4).

## 2.9 The six operator commands

Exactly six (§7.3.2): switch space (`SceneCmd`), and `OperatorCmd` `home`, `recentre`, `quality`, `cue`, `lock`.
The path has three steps, and no link type goes further than the second:

```text
LinkClient (Atlas.Link)  ──TryDequeueCommand──▶  LinkPump (Atlas.Telemetry)  ──IOperatorCommands──▶  CommandRouter (Atlas.App)
   parses the JSON, queues an OperatorCommand      translates, logs the "cmd" event                 calls SpaceLab, the ladder, BumpCue.Fire
```

`IOperatorCommands` is lane A's interface in `Atlas.World` (lane A §7.1) and has no link type. `CommandRouter`
is the handler of §6.2 that turns `cue` into `BumpCue.Fire`, so `Atlas.Safety` never sees a link type.

```csharp
// File: ATLAS/Assets/Atlas/Link/OperatorCommand.cs
namespace Atlas.Link
{
    public enum OperatorCommandKind : byte { None = 0, SwitchSpace = 1, Home = 2, Recentre = 3, Quality = 4, Cue = 5, Lock = 6, Walk = 7, View = 8, Hands = 9 }

    /// <summary>One operator command as the app consumes it.</summary>
    public struct OperatorCommand
    {
        public const string CmdHome = "home";
        public const string CmdRecentre = "recentre";
        public const string CmdQuality = "quality";
        public const string CmdCue = "cue";
        public const string CmdLock = "lock";
        public const int TierAuto = -1;
        public const int TierHigh = 4;

        public OperatorCommandKind Kind;
        public int Space;                   // SwitchSpace: 0..6
        public string Variant;              // "" when none
        public int Tier;                    // Quality: -1..4
        public bool On;                     // Lock
        public JsonValue Args;              // the raw args object; may be null

        public static OperatorCommand SwitchSpace(int space, string variant = null, JsonValue args = null);
        public static OperatorCommand Home();
        public static OperatorCommand Recentre();
        public static OperatorCommand Quality(int tier);     // clamped to -1..4
        public static OperatorCommand Stop();                // the STOP button: Kind Cue
        public static OperatorCommand Lock(bool on);
        public JsonMessage ToMessage();                      // a SceneCmdMessage or an OperatorCmdMessage; null for None
        public static bool TryFrom(SceneCmdMessage message, out OperatorCommand command);
        public static bool TryFrom(OperatorCmdMessage message, out OperatorCommand command);
        public string Name { get; }                          // "space", "home", "recentre", "quality", "cue", "lock"
        public EventMessage ToEvent(long timeUs, string scene);   // the "cmd" event
    }
}
```

`LinkClient` keeps the newest 32 commands in a ring. `LinkPump` drains it at step 8b into
`LinkPump.Commands` (lane A's `IOperatorCommands`, taken from `AtlasServices` once the app has booted; absent
in the Spectator, where commands wait and are never applied) and does this:

| Command | `LinkPump` | `CommandRouter` then (lane A §7.2) |
|---|---|---|
| `SceneCmd` | For each member of `args`: `registry.Set(name, value, ParamSource.Remote)`. Then `SwitchSpace((SpaceId)Space, Variant)` (`null` for an empty variant) | Sets the variant's parameter, then `ISpaceHost.RequestSwitch` |
| `home` | `Home()` | `ISpaceHost.ReturnHome()` |
| `recentre` | `Recentre()` | `IFrameLoop.ResetWalkerToFloor()`, then the pose pipeline's `ResetGeometry()` |
| `quality` | `SetQuality(Tier)` | Sets `thermal.pinTier` |
| `cue` | `Cue()` | `BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator)` (lane C §8.8): the same local entry point Bump Guard uses (`P1-C14`). Indicator, tone and continuous haptic; the world keeps rendering; nothing freezes |
| `lock` | `SetLock(On)` | `registry.Set("demoLock", on ? 1f : 0f, ParamSource.Remote)` |
| `hands` | `LinkPump` itself: `registry.Set("hands.enabled", On ? 1f : 0f, ParamSource.Remote)` (not on `IOperatorCommands`), so it works under the demo lock | none |

- `OperatorCmd hands` (args `{on}`, `OperatorCommand.Hands(bool)`, `CmdHands = "hands"`) is a seventh operator command, added by the tracked hands (design 8.1, 8.2). The Console's Operator page has a Hands switch and status line; the status folds `hands.on`, `hands.off`, `hands.rate`, `hands.enter`, `hands.exit` and `hands.stats` events (active or off with the reason, rate, hands tracked).
- After each command `LinkPump` logs one `cmd` event (§2.6). A handler that throws is logged as an error line
  and does not stop the commands behind it. Test L10 compares the event's `t_us` (clock-corrected) with the
  time the Hub noted in `hub.log`: 250 ms at most.
- A command is handed over at step 8b of the frame in which it is polled, so it takes effect in the next frame.
  Nothing in steps 1 to 7 waits for it.
- All six work under the demo lock, the `args` of a `SceneCmd` included. The lock stops `ParamSet` only. An
  `OperatorCmd lock` sets `LinkClient.DemoLock` the moment it is received, so a `ParamSet` queued behind it is
  already dropped.
- `cue` is an extra trigger, never the only one (§7.5 rule 1). With no link, Bump Guard is unchanged. An
  operator cue is held for lane C's `OperatorHoldSeconds` (3.0 s); the command carries no duration.

## 2.10 `LinkClient`

One class for the phone, the Spectator and an Editor. It owns the handshake, the watchdog, the clock and the
reconnect back-off, and talks to the network through the `ILinkTransport` it is given. Its public API has no
LiteNetLib type.

```csharp
// File: ATLAS/Assets/Atlas/Link/LinkClient.cs      (LinkRole is in Messages/JsonMessages.cs)
using System;

namespace Atlas.Link
{
    public enum LinkRole : byte { Unknown = 0, Phone = 1, Spectator = 2, Editor = 3, Console = 4 }

    public static class LinkRoles
    {
        public static string Name(LinkRole role);            // "phone", "spectator", "editor", "console", "unknown"
        public static LinkRole Parse(string name);
        public static bool Watches(LinkRole role);           // Spectator, Editor: receive the relayed State
        public static bool Operates(LinkRole role);          // Console, Editor: their parameters and commands go to the phone
    }

    public delegate void LinkStateHandler(in StateMessage state);

    public sealed class LinkClient : ILinkTransportListener
    {
        /// <param name="registry">The phone's registry; null for a client that holds no parameters.</param>
        public LinkClient(ILinkTransport transport, ILinkClock clock, LinkRole role, ParamRegistry registry = null);

        // ---- identity sent in Hello ---------------------------------------------------------------
        public LinkRole Role { get; }
        public string Device;
        public string App;                                    // "atlas"
        public string Build;
        public string WorldHash;
        public string RoomId;
        public string SessionId;
        public string Scene;                                  // written into the events the client emits itself

        // ---- state --------------------------------------------------------------------------------
        public ParamRegistry Registry { get; }
        public ClockSync ClockSync { get; }
        public LinkWatchdog Watchdog { get; }
        public ILinkTransport Transport { get; }
        public LinkState State { get; }
        public bool IsReady { get; }                          // the handshake is done and messages flow
        public bool DemoLock { get; set; }                    // while true a received ParamSet is dropped
        public int StateHz { get; set; }                      // clamped to 1..60
        public bool EchoParamChanges;                         // default true; LinkPump sets it false and owns the echo
        public WelcomeMessage Welcome { get; }                // the last one received; null before the first
        public bool WorldMatches { get; }
        public string LastByeReason { get; }
        public string Address { get; }
        public int Port { get; }
        public int RttMs { get; }
        public int LossPermille { get; }
        public long NowUs { get; }
        public long HubNowUs { get; }                         // the Hub's clock now, from the clock estimate

        // ---- events, all raised inside Poll --------------------------------------------------------
        public event Action<LinkState, LinkState> StateChanged;   // (previous, current)
        public event Action<WelcomeMessage> Welcomed;
        public event LinkStateHandler StateReceived;              // spectator, editor
        public event Action<RoomOutlineMessage> RoomOutlineReceived;   // the instance is reused: copy what must be kept
        public event Action<JobStatusMessage> JobStatusReceived;
        public event Action<AssetReadyMessage> AssetReadyReceived;
        public event Action<ParamManifestMessage> ManifestReceived;
        public event Action<EventMessage> EventReceived;          // the phone's events, relayed
        public event Action<int> ParamsApplied;                   // a ParamSet was applied: how many values changed
        public event Action<string, int> ParamSetIgnored;         // (reason "lock" or "degraded", entries dropped)
        public ParamSetMessage IgnoredParamSet { get; }           // valid during ParamSetIgnored
        public event Action<string> ByeReceived;

        // ---- lifecycle ----------------------------------------------------------------------------
        public bool Start(string address, int port = LinkProtocol.DefaultPort);   // false when the socket cannot open
        public void SetAddress(string address, int port = LinkProtocol.DefaultPort);   // "Change IP": reconnects
        public void SetSession(string sessionId);             // a new id restarts the Telemetry Seq and says Hello again
        public void SetWorld(string worldHash, string roomId);    // says Hello again when either changed
        public void ReconnectNow();
        public void Stop(string reason = "stop");             // Bye, then closes the socket
        /// <summary>Step 8b, once per frame: delivers what arrived, runs the handshake, the clock pings, the
        /// watchdog and the back-off. Received ParamSets are applied to the registry here. Never blocks.</summary>
        public void Poll();

        // ---- sending. Each returns false, and sends nothing, when the link is not ready. -----------
        public bool SendState(ref StateMessage state);        // stamps Seq, TimeUs and the lock bit; channel 1; flushes
        public bool SendStateCopy(in StateMessage state);     // for a sink that holds the state read-only
        public bool SendTelemetry(ref TelemetryMessage telemetry);   // stamps Seq and, when zero, TimeUs
        public bool SendTelemetryRow(in TelemetryMessage row);       // exactly as given
        public bool SendRoomOutline(RoomOutlineMessage outline);
        public bool Send(JsonMessage message);                // any JSON message; from the moment a peer exists
        public bool SendEvent(string scene, string ev, JsonValue data);
        public bool SendLog(string level, string msg);
        public bool SendManifest();
        public bool SendParamSet(ParamSetMessage set);        // console and editor roles
        public bool SendParam(ushort id, float value);
        public bool SendCommand(in OperatorCommand command);

        // ---- received operator commands ------------------------------------------------------------
        public int PendingCommands { get; }
        public bool TryDequeueCommand(out OperatorCommand command);
    }
}
```

There are no sink interfaces: what the client receives is raised as events, and operator commands wait in its
queue. On the phone `LinkPump` (§5.1) is the client's only caller; other code reads it and subscribes.

| Topic | Rule |
|---|---|
| `SendState` rate | The caller offers one `State` per frame. At 60 Hz every call is sent. Below, `LinkClient` sends when at least `1 / StateHz` − 8 ms has passed since the last one sent, so 30 Hz on a 60 fps loop is every second frame |
| `ParamSet` | Applied in `Poll` with `ParamSource.Remote` when the state is `Connected` and `DemoLock` is off; otherwise dropped, and `ParamSetIgnored` is raised (`LinkPump` logs `param.ignored`) |
| `ParamManifest` | Sent by `LinkClient` itself as the last step of the handshake (role phone, when it has a registry). `LinkPump` sends it again when `ManifestVersion` changed |
| Echo | `LinkPump` subscribes to `registry.Changed` and logs the `param` events of §2.6. A bare `LinkClient` with `EchoParamChanges` true sends one `param` event per change itself, wherever the value is written |
| Allocation | `Poll`, `SendState`, `SendStateCopy`, `SendTelemetry`, `SendTelemetryRow` and the clock allocate nothing in steady state, a connect attempt included. JSON sends and receives allocate; they happen on demand |
| Failure | No public member throws for a network reason. A `Bye` from the Hub is kept in `LastByeReason` |
| Offline cost | With no peer every send returns false at once. `Poll` costs one `PollEvents` call and, when the back-off says so, one connect attempt |

## 2.11 `LinkHost`, roles and the loopback network

```csharp
// File: ATLAS/Assets/Atlas/Link/LinkHost.cs
using System;

namespace Atlas.Link
{
    public delegate void LinkPeerJoinedHandler(int peerId, HelloMessage hello);
    public delegate void LinkPeerLeftHandler(int peerId, LinkRole role, string reason);
    public delegate void LinkHostStateHandler(int peerId, in StateMessage state, byte[] raw, int offset, int length);
    public delegate void LinkHostTelemetryHandler(int peerId, in TelemetryMessage telemetry);
    public delegate void LinkHostJsonHandler(int peerId, byte messageId, JsonValue body);
    public delegate void LinkHostPongHandler(int peerId, in TPongMessage pong, long hubReceiveUs);

    public sealed class LinkHost : ILinkTransportListener
    {
        public const int HeartbeatMs = 250;
        public const long KickDelayUs = 250000;

        public LinkHost(ILinkTransport transport, ILinkClock clock);

        // What Welcome carries. The owner sets them; a handler of HelloReceived may set them for that Welcome.
        public string SessionId;
        public string WorldHash;
        public string RoomId;
        public string ForgeUrl;
        public bool DemoLock;                          // kept in step with OperatorCmd lock commands passing through

        public ILinkTransport Transport { get; }
        public bool IsRunning { get; }
        public int Port { get; }
        public int PeerCount { get; }                  // clients that have said Hello
        public int PhonePeer { get; }                  // -1 when no phone is connected
        public bool HasPhone { get; }
        public long NowUs { get; }
        public ParamManifestMessage Manifest { get; }  // the phone's last; null before the first
        public int UnackedAssetCount { get; }
        public LinkRole RoleOf(int peerId);
        public int CountRole(LinkRole role);
        public int GetRttMs(int peerId);
        public int GetLossPermille(int peerId);
        public string GetAddress(int peerId);
        public bool TryGetClock(int peerId, out long offsetUs, out long rttUs);   // the client's clock minus the Hub's
        public long ToHubUs(int peerId, long peerUs);

        // Events, all raised inside Poll.
        public event LinkPeerJoinedHandler PeerJoined;              // the first Hello of a connection
        public event LinkPeerJoinedHandler HelloReceived;           // every Hello, before its Welcome is written
        public event LinkPeerLeftHandler PeerLeft;
        public event LinkHostStateHandler StateReceived;            // decoded, with the raw 202 bytes; already relayed
        public event LinkHostTelemetryHandler TelemetryReceived;
        public event Action<int, ParamManifestMessage> ManifestReceived;
        public event Action<int, RoomOutlineMessage> RoomOutlineReceived;   // the instance is reused
        public event LinkHostJsonHandler JsonReceived;              // Hello, ParamManifest, SceneCmd, OperatorCmd, Event, Log, AssetAck, Bye
        public event Action<int, ParamSetMessage> ParamSetReceived; // from an editor or console; already forwarded
        public event LinkHostPongHandler TPongReceived;
        public event Action<string, int> ParamSetIgnored;           // (reason, values put back in the snapshot)

        public bool Start(int port = LinkProtocol.DefaultPort);     // false when the port is taken
        public void Stop(string reason = "hub stopping");           // Bye to every client
        public void Poll();                                         // call in a loop; never blocks

        // Towards the phone.
        public bool SendParam(ushort id, float value);
        public bool SendParam(string name, float value);            // resolved through the phone's manifest
        public bool SendParamSet(ParamSetMessage set);              // also records the values in the snapshot
        public int SendPreset(JsonValue preset);                    // one ParamSet; returns values sent
        public bool SendCommand(in OperatorCommand command);
        public void SendJobStatus(JobStatusMessage status);         // to the phone and every watcher
        public void SendAssetReady(AssetReadyMessage asset);        // likewise; kept until its AssetAck
        public bool SendTo(int peerId, JsonMessage message);
        public void SendToRole(LinkRole role, byte[] data, int offset, int length, LinkDelivery delivery);
        public void Kick(int peerId, string reason);                // Bye now, disconnect 250 ms later
        public int SendWelcomeToWatchers();                         // to those whose last Welcome is stale

        // The parameter snapshot.
        public JsonValue SnapshotToJson();                          // {"values":{name: value}}
        public bool TryGetSnapshotValue(string name, out float value);
        public void ClearSnapshot();
    }
}
```

`LinkHost` does by itself: `Hello` / `Welcome` and the proto refusal, the one-phone rule, `TPong` answers, the
heartbeat `TPing` with one `ClockSync` per client, the relay of `State`, `RoomOutline`, `ParamManifest` and
`Event`, forwarding `ParamSet` and commands from operators to the phone, the parameter snapshot and its replay,
the list of unacknowledged `AssetReady`, what a late joiner is given, and dropping unknown ids. The key check
is the transport's. Logs, the Console and the session are the owner's (§3.2).

```csharp
// File: ATLAS/Assets/Atlas/Link/Transport/LoopbackTransport.cs
namespace Atlas.Link
{
    /// <summary>An in-memory network for tests and offline tools, driven by an injected clock. Reliable packets
    /// always arrive and in order; sequenced ones may be lost but never arrive out of order; a silent peer is
    /// dropped after the disconnect timeout.</summary>
    public sealed class LoopbackNetwork
    {
        public LoopbackNetwork(ILinkClock clock, int seed = 1);
        public double Loss;                            // chance 0..1 that a sequenced or unreliable packet is lost
        public double DelayMs;                         // one way
        public double JitterMs;
        public double DisconnectTimeoutMs = 3000.0;
        public double ConnectFailMs = 500.0;
        public long PacketsSent { get; }
        public long PacketsLost { get; }
        public bool Cut { get; set; }                  // the cable is pulled
        public LoopbackTransport CreateEndpoint();
    }

    public sealed class LoopbackTransport : ILinkTransport, ILinkPeerAddresses { /* one endpoint */ }
}
```

**Loss and latency simulation** (§7.6, tests L9 and L12). In the headless tests it is `LoopbackNetwork`
(`Loss`, `DelayMs`, `JitterMs`, `Cut`) with a `ManualClock`. On the real link LiteNetLib compiles
`SimulatePacketLoss` and `SimulateLatency` only under `DEBUG` or `SIMULATE_NETWORK`; `LiteNetTransport.Manager`
exposes the `NetManager` for a tool that sets them. The Hub has no loss or latency option and no second build
configuration in this build (open item).

| Role | Who | Sends | Receives |
|---|---|---|---|
| `phone` | The ATLAS app, on a phone or in an Editor in play mode; ReplayPhone | `Hello`, `State`, `Telemetry`, `ParamManifest`, `RoomOutline`, `Event`, `Log`, `AssetAck`, `TPing`, `TPong`, `Bye` | `Welcome`, `ParamSet`, `SceneCmd`, `OperatorCmd`, `JobStatus`, `AssetReady`, `TPing`, `TPong`, `Bye` |
| `spectator` | The Spectator build, or an Editor running `PC_Spectator` | `Hello`, `TPing`, `TPong`, `Log`, `Bye` | `Welcome`, relayed `State`, `RoomOutline`, `ParamManifest`, `Event`, `JobStatus`, `AssetReady`, `TPing`, `TPong`, `Bye` |
| `editor` | A link client that watches and operates (no code in this build takes the role) | as `spectator`, and `ParamSet`, `SceneCmd`, `OperatorCmd` | as `spectator` |
| `console` | A link client that operates only (none in this build) | `Hello`, `ParamSet`, `SceneCmd`, `OperatorCmd`, `TPing`, `TPong`, `Bye` | `Welcome`, `ParamManifest`, `TPing`, `TPong`, `Bye` |
| browser Console | A browser on `http://<station>:8080/` | WebSocket `/ws` (§3.3). It is not a LiteNetLib peer and sends no `Hello` | JSON frames |

The walker's app is role `phone` wherever it runs, the Editor included (`TelemetryInstaller`), so the whole
flow can be rehearsed against the Station with no device.

**One phone (decided here).** The first `phone` to say `Hello` holds the seat: its `State` is relayed and it
alone receives commands and `ParamSet`. A second phone is refused with `Bye {"reason": "another phone is
already connected"}` and disconnected; it retries with its back-off and gets in once the seat is free (the
phone left, or the Hub's watchdog dropped it after 3 s of silence). A phone that changes address keeps its
peer, so a roamer is not locked out. For a hot swap, phone A is stopped before phone B connects.

A message from a role that may not send it is dropped.

## 2.12 ReplayPhone and `state.bin`

`state.bin` (Hub log, §3.4) is the replay source (R4 §6.5, Appendix C.8). `station/hub/Logging/StateLog.cs`
defines the layout; ReplayPhone compiles the same file.

```text
header, 16 bytes:  "ATLSTATE" (8 ASCII bytes)   version u16 = 1   recordSize u16 = 210   reserved u32 = 0
record, 210 bytes: hubReceive_us i64 (Hub clock)   then the 202 bytes of the State as received
```

`tools/ReplayPhone` is a .NET 8 console that compiles the `Atlas.Link` sources:

```text
dotnet run --project tools/ReplayPhone -- [session folder or state.bin] [--hub 127.0.0.1[:9050]] [--port 9050]
                                          [--seconds s] [--loop] [--speed 1.0] [--hz 60] [--no-telemetry] [--quiet]
```

- Connects as role `phone`, device `ReplayPhone`, with a new session id.
- With a recording: from the same folder, when present, it sends `manifest.json` as `ParamManifest`,
  `outline.bin` as `RoomOutline`, and the `world` events of `events.jsonl`. It sends each record at its
  original spacing (`hubReceive_us` differences ÷ `--speed`), rewrites `seq` and `t_us` to its own counter and
  clock and leaves every other byte alone. It holds no registry, so `ParamSet` changes nothing; commands are
  logged and otherwise ignored. It needs no Forge.
- With no recording it plays a **synthetic walk** at `--hz`: it declares a few of the app's parameters,
  applies what the Console sets, answers the six commands and sends `Telemetry` once a second, so the Console
  and the Spectator can be developed against it.

The Editor twin: the ATLAS app in play mode runs the same `LinkClient` as role `phone` against a Hub on
`127.0.0.1` (C.8). A recorded pose log (lane C) drives it with no phone.

## 2.13 Link rules

§7.5, with the place each one is enforced.

| # | Rule | Enforced by |
|---|---|---|
| 1 | Safety never touches the network. Bump Guard, tracking, the kernel and rendering never read link state. `cue` is an extra trigger, never the only one | `Atlas.Safety` references neither `Atlas.Link` nor `Atlas.Telemetry` (README F1, test `Safety_Independence`). Operator commands end in `Atlas.App.CommandRouter` (§2.9) |
| 2 | Nothing in `Update`, `LateUpdate` or a render callback waits for the link | Sockets on LiteNetLib's threads. HTTP through `UnityWebRequest`, never awaited in a frame. Hashing, GLB checks and file writes on worker threads (§4.3, §5.5). Received commands are applied at step 8b |
| 3 | Every remote parameter is clamped on the phone. Bump Guard has no registry parameter and no off switch | `ParamRegistry.Set` (1.3) |
| 4 | Hub unplugged: the app behaves identically apart from the link indicator. Forge unplugged: apart from the furniture, which stays at its first shapes | Tests L13, L14, L19 |
| 5 | A receiver ignores unknown ids and trailing bytes; a `proto` major mismatch is refused with a readable reason | §2.2 |
| 6 | `Atlas.Link` allocates 0 B per frame in steady state | §2.10; test L13 |

---

# Part 3 · Station HTTP

Two processes on the Station (§7.1). The Hub ends the LiteNetLib link and serves the Console. The Forge owns the
GPU and never sees live pose. Default ports: Hub UDP 9050 and TCP 8080, Forge TCP 8000, all bound to `0.0.0.0`.

## 3.1 Hub: process

```
station/hub/
  Hub.csproj          net8.0, Microsoft.NET.Sdk.Web, assembly atlas-hub. NuGet: LiteNetLib 2.1.4, QRCoder 1.6.0 (MIT).
                      <Compile Include="../../ATLAS/Assets/Atlas/Link/**/*.cs" />  (the Atlas.Link sources, linked)
  Program.cs          options, start, the banner, Ctrl+C
  HubOptions.cs       HubOptions (the command line), HubPaths, Names
  HubServer.cs        owns the LinkHost: session, watchdogs, digest, snapshot use, commands, the JSON views (§3.2)
  Web/HubWeb.cs       Kestrel: every route of §3.3 and the /ws socket
  Web/PairPage.cs     /pair, the QR code
  ConsoleFeed.cs      fan-out of frames to the connected browsers
  PresetStore.cs      presets/*.json, load, bake
  ForgeProbe.cs       GET <forge>/health every 2 s
  LanAddress.cs       the Station's LAN addresses
  Logging/            SessionLog.cs (the files of §3.4), StateLog.cs (state.bin), PerfCsv.cs, TelemetryJson.cs
  wwwroot/            index.html, console.css, js/*.js (ES modules, no bundler, no vendored library): the Console
  presets/            <name>.json (created on the first save)
  logs/<session>/     §3.4 (not committed)
station/hub.tests/            dotnet test: the Hub on the in-memory network
station/console-tests/        node tests of the Console's modules
station/setup_firewall.ps1    the three rules of Appendix C.7
```

Run: `dotnet run --project station/hub -- [options]`.

| Option | Default | Meaning |
|---|---|---|
| `--udp <port>` | 9050 | LiteNetLib listener; 0 picks a free port |
| `--http <port>` | 8080 | Kestrel; 0 picks a free port |
| `--bind <ip>` | `0.0.0.0` | The address the web server binds to |
| `--forge <url>` | `http://127.0.0.1:8000` | The Forge this Hub watches. A loopback URL is announced to clients as `http://<Station address>:<port>` in `Welcome.forgeUrl`; any other is announced as given |
| `--no-forge` | – | Do not poll the Forge |
| `--advertise <ip>` | the Station's LAN address | The address in the QR code and in `forgeUrl` |
| `--logs <dir>` | `station/hub/logs` | Parent of the session folders; on an encrypted volume (C.7) |
| `--presets <dir>` | `station/hub/presets` | |
| `--project <dir>` | the repository's `ATLAS` | Where Bake writes `defaults.json` (1.7) |
| `--wwwroot <dir>` | `wwwroot` beside the Hub | The Console's files |
| `--session <id>` | the phone's, else the Hub's start time | A fixed session id and log folder, kept whatever the phone says |
| `--demo-lock` | off | Start with the demo lock on: `Welcome.demoLock` is true |
| `--quiet` | off | No banner and no status line |

Run from its project the Hub finds its own folder (`HubPaths`); a published copy uses the current directory.

Threads: one link thread (`HubServer.Pump()`: `host.Poll()`, watchdogs, the 10 Hz digest, the once-a-second
work, then `Thread.Sleep(1)`); Kestrel's pool for HTTP and WebSocket. HTTP handlers reach the link through
one lock held for microseconds; no handler touches a LiteNetLib object.

At start the Hub prints its ports, the Console and pairing URLs and the session folder, then one status line
per second while a phone is connected. With no packet after 10 s it prints "no packets received yet: check
the firewall rule for UDP 9050 (station/setup_firewall.ps1)" (R4 §9).

## 3.2 Hub: what it does with each message

| From | Message | Hub |
|---|---|---|
| Phone | `Hello` | Adopts the phone's `sessionId` (a new or continued log folder, unless `--session` fixed one), its `worldHash` and `roomId`; `Welcome`; `Welcome` again to watchers whose last one is stale. On the first `Hello` of a connection: `hub.log` `join`, the phone's watchdog goes to RESYNC |
| Phone | `State` | Relays the 202 bytes unchanged to every spectator and editor (channel 1, sequenced) and flushes. Appends to `state.bin`. Updates rate, loss (from `seq`) and latency (Hub receive time minus `t_us` brought onto the Hub's clock by the Hub's own pings). Feeds the 10 Hz digest. Follows the lock bit of `flags`. Target: relay adds under 2 ms (`P1-D02`) |
| Phone | `Telemetry` | One line in `perf.csv`; in a session the phone named, a row whose `seq` the file already holds is skipped and the file is kept in `seq` order. WebSocket `telemetry` |
| Phone | `ParamManifest` | Keeps it. On the first of a connection: the snapshot replay of §2.8 and the unacknowledged `AssetReady`; the phone's watchdog goes to CONNECTED. Relays it to spectators, editors and consoles. Writes `manifest.json`. WebSocket `manifest` |
| Phone | `Event` | A `param` event updates the snapshot; a `param.ignored` event puts it back (§2.6); a `world` event is kept for late joiners and keeps `Welcome` current. Relays to spectators and editors. One line in `events.jsonl`. WebSocket `event` |
| Phone | `Log` | One line in `phone.log`. WebSocket `log` |
| Phone | `RoomOutline` | Keeps it. Writes `outline.bin`. Relays to spectators and editors. WebSocket `outline` |
| Phone | `AssetAck` | Forgets the `AssetReady` of that job. An `asset_ack` line in `events.jsonl`. WebSocket `job` |
| Second phone | `Hello` | `Bye`, then disconnect (§2.11) |
| Forge (`/internal/job`) | `JobStatus`, `AssetReady` | Sends to the phone and to every spectator and editor (channel 0). WebSocket `job`. Keeps each `AssetReady`, with its light members when it has them, until its `AssetAck`. One line in `hub.log` |
| Console | `param`, `preset` | Refused while the demo lock is on. Else `ParamSet` to the phone; the value the phone will hold after its clamp goes into the snapshot. One line in `hub.log` |
| Console | `scene`, `op` | `SceneCmd` / `OperatorCmd` to the phone. One line in `hub.log`, with the Hub's time |
| Editor, console (link) | `ParamSet`, `SceneCmd`, `OperatorCmd` | Forwarded to the phone; `hub.log` |
| New spectator or editor | `Hello` | `Welcome` with the session, `worldHash` and `roomId` of the phone; then the kept `RoomOutline`, the manifest with the values of that moment, the last `world` event and the unacknowledged `AssetReady` |
| Any client | `TPing` | `TPong` at once, before any `Hello` |
| Timer, 250 ms | – | `TPing` to every client that has said `Hello` |
| Any client | `Bye`, time-out | Removes the peer; `hub.log` `leave`; WebSocket `link`. A client silent for 3 s is disconnected by the Hub's own watchdog |

The Hub restarts cleanly: it keeps nothing a client cannot send again, except the parameter snapshot, which
is lost with the process. Every client reconnects within 5 s (`P1-D02`); the phone's `Hello` carries its
session id, so the Hub appends to the same session folder.

## 3.3 Hub: HTTP and WebSocket (port 8080)

Appendix B.3. Rows marked ✚ are added here **(decided here)**. JSON bodies are UTF-8, at most 1 MB; errors are
`{"error": "<text>"}` with the status shown. A request that needs the phone answers 409 when none is
connected, 423 while the demo lock is on, 404 for an unknown parameter, command, space or preset, 400 for a
bad body.

| Route | Request | Response |
|---|---|---|
| `GET /` | – | 200 `text/html`: the Console (§3.5), with its files under `/console.css` and `/js/…`, served with `Cache-Control: no-cache` |
| `GET /pair` | optional `?ip=<address>` | 200 `text/html`: the pairing page (§3.5) |
| ✚ `GET /pair/qr.svg` | optional `?ip=` | 200 `image/svg+xml`: the QR code of `atlas://<ip>:<udp port>` |
| ✚ `GET /api/pair` | optional `?ip=` | 200 `{"url": "atlas://…", "address": "…", "udpPort": 9050, "httpPort": 8080, "candidates": ["…"]}` |
| `WS /ws` | frames below | frames below; 400 for a plain request |
| `GET /api/session` | – | 200: the object below |
| ✚ `GET /api/state` | – | 200: the `state` digest below without `t`; 404 before the first `State` |
| ✚ `GET /api/telemetry` | – | 200: the last row, members named as the CSV columns of §5.3; 404 before the first |
| ✚ `GET /api/params` | – | 200 `{"ver": 3, "params": […], "locked": false}`: the manifest with the value the Hub knows for each parameter |
| ✚ `GET /api/params/{key}` | `key`: a name or a wire id | 200: one manifest row; 404 |
| ✚ `POST` or `PUT /api/params/{key}` | `{"value": number \| boolean}` | 200 `{"id": 2, "name": "render.scale", "value": 0.7, "sent": true}`, `value` being what the phone will hold; 404, 423 |
| ✚ `POST /api/params` | `{"values": {"<name or id>": number, …}}` | 200 `{"sent": n}`: one `ParamSet`; 409, 423 |
| ✚ `POST /api/op` | `{"cmd": "cue", "args": {}}` | 200 `{"ok": true, "cmd": "cue"}`; 404 unknown command; 409 (`lock` succeeds with no phone) |
| ✚ `POST /api/scene` | `{"space": "S1" \| 1, "variant": "klein", "args": {}}` | 200 `{"ok": true, "space": "S1"}`; 404, 409 |
| `GET /api/presets` | – | 200 `{"presets": [{"name": "demo", "count": 12, "modified": "2026-10-04T15:30:12Z"}]}` |
| `POST /api/presets/{name}` | `{"values": {"<name>": number, …}}`, or an empty body to save the Hub's current snapshot; `name` matches `[A-Za-z0-9_-]{1,40}` | 204; 400 for a bad name or body |
| ✚ `GET /api/presets/{name}` | – | 200 `{"name": "demo", "values": {…}}`; 404 |
| ✚ `DELETE /api/presets/{name}` | – | 204; 404 |
| ✚ `POST /api/presets/{name}/load` | – | 204: every value the phone's manifest knows, sent in **one** `ParamSet`; 404; 409 when no phone is connected; 423 |
| ✚ `POST /api/presets/{name}/bake` | – | 204: the preset written to `<project>/Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json` (1.7); 404; 409 when the project folder does not exist |
| ✚ `GET /api/sessions`, `GET /logs` | – | 200 `{"current": "…", "sessions": ["20261004T153012Z-a3f9", …]}`, newest first |
| ✚ `GET /logs/{session}` | – | 200 `{"session": "…", "files": [{"name": "perf.csv", "bytes": 1234}, …]}`; 404 |
| `GET /logs/{session}/perf.csv` | – | 200 `text/csv`, as an attachment; 404 |
| ✚ `GET /logs/{session}/{file}` | `file`: one of the names of §3.4; `telemetry.csv` is an alias of `perf.csv` | 200; 404 |
| ✚ `PUT /logs/{session}/perf.csv` | the phone's CSV, at most 32 MB | 204: rows the Hub lacks are merged by `seq` (§12.2, "CSV on the phone, uploaded over HTTP later"); a session the Hub never saw gets its folder. 400 when the first line is not the header |
| `POST /internal/job` | A `JobStatus` or an `AssetReady` body (§2.5) with a `jobId`; a body with `sha256` and `url` is an `AssetReady`. Other members (the Forge's `type`) are ignored | 204; 400. **Loopback only**: 403 from any other address |

`GET /api/session`, and the `link` frame, which is the same object with `"t": "link"`:

```json
{"sessionId":"20261004T153012Z-a3f9",
 "phone":{"state":"connected","rttMs":3,"loss":0.2,"stateHz":59.8,"states":12345,"lost":2,"clockOffsetMs":3999.95,
          "device":"iPhone16,2","build":"0.1.0","address":"192.168.0.23","worldHash":"141060c2fc138145","roomId":"sample_3x3"},
 "sources":[{"peer":1,"role":"phone","device":"iPhone16,2","active":true}],
 "spectators":1,"editors":0,"consoles":1,
 "forge":{"up":true,"queue":2,"debugKeep":false,"hosted":false,"gpu":true,"pipelineVer":"3"},
 "demoLock":false,"station":"192.168.0.10","udpPort":9050,"httpPort":8080,"pair":"atlas://192.168.0.10:9050",
 "uptimeS":812.4,"telemetryRows":812,"events":57}
```

`phone.state` is `connected`, `degraded`, `resync` or `offline`. `loss` is the percentage of `State` lost in
the last 5 s. `clockOffsetMs`, `device`, `build`, `address`, `worldHash` and `roomId` are present while a
phone is connected. `debugKeep` true makes the Console show the red "crops kept" banner (§7.4); `hosted`
the amber one (§3.10).

WebSocket frames are JSON objects with a member `t`. A frame the Hub cannot serve is answered with
`{"t": "error", "msg": "…"}`.

| Direction | Frame | Meaning |
|---|---|---|
| Console → Hub | `{"t": "param", "id": 3, "value": 0.7}` | Set a parameter by wire id. `id` may be the name as a string, or `"name": "render.scale"` may stand in for it ✚ |
| Console → Hub ✚ | `{"t": "preset", "values": {"<name>": number, …}}` | Several values in one `ParamSet` |
| Console → Hub | `{"t": "scene", "space": "S1", "variant": "klein", "args": {}}` | `SceneCmd` |
| Console → Hub | `{"t": "op", "cmd": "cue", "args": {}}` | `OperatorCmd` |
| Console → Hub ✚ | `{"t": "fms", "score": 3}` · `{"t": "note", "text": "…"}` | Comfort score (0–20) and operator note; written to `events.jsonl` |
| Console → Hub ✚ | `{"t": "seq", "score": 5}` | Single Ease Question (whole number 1–7); written to `events.jsonl` as `seq {score}`. Anything else gets `{"t":"error"}` ✚ (P2-C17) |
| Console → Hub ✚ | `{"t": "ping"}` | Answered with `{"t": "pong"}` |
| Hub → Console | `{"t": "manifest", "ver": 3, "params": […], "locked": false}` | The manifest rows of §2.5 with the values the Hub knows; on connect and on change. `ver` counts manifests received |
| Hub → Console | `{"t": "telemetry", "t_us": …, "seq": …, …}` | One per row; the members are the CSV column names of §5.3 |
| Hub → Console | `{"t": "state", …}` | 10 Hz digest, below |
| Hub → Console | `{"t": "event", …}` · `{"t": "log", …}` | The `Event` body with `hub_us` added, or the `Log` body |
| Hub → Console | `{"t": "job", "kind": "status" \| "ready" \| "ack", …}` | The `JobStatus`, `AssetReady` or `AssetAck` body |
| Hub → Console ✚ | `{"t": "outline", "outline": [[x, z], …], "height": 2.5, "rect": {"cx": 0, "cz": 0, "wf": 2.4, "df": 3.4, "yaw": 0}, "boxes": [{"id": 1, "cat": 2, "cx": 0, "cz": 0.5, "w": 1.2, "d": 0.8, "h": 0.75, "yaw": 0}]}` | The `RoomOutline`, decoded; `cat` is the category number |
| Hub → Console | `{"t": "link", …}` | The session object above; 1 Hz and on change |

On connect a Console is sent `link`, then `manifest`, the last `telemetry`, `outline` and `state` when there
are any.

The `state` digest, from the newest `State`:

```json
{"t":"state","seq":12345,"t_us":1234567890,"scene":3,"space":3,"tracking":0,"jump":false,"cue":true,"tier":1,
 "lock":false,"bump":1,"tile":"-1,0,0,0,0,1","sheet":3,"head":[0.25,1.5,-0.5],"yawDeg":90.0,
 "rot":[0.0,0.7071068,0.0,0.7071068],"p":[1.885,2.0,3.0,0.0,0.0,0.0,0.0,0.0],"worldVer":7,"hz":59.8,"lossPct":0.2,
 "latencyMs":{"p50":4.1,"p95":9.8,"p99":14.2},"received":12345,"lost":2}
```

`tile` is the six words as text (a 64-bit integer does not survive a JavaScript number). `yawDeg` is the heading
of the head's forward axis on the floor, `atan2(forward.x, forward.z)`. `latencyMs` is over the newest 512
`State` packets.

## 3.4 Hub: log files

`logs/<session>/`, one folder per session id (§5.5). A folder that already exists is continued, never
overwritten. Until a phone names the session the Hub uses an id of its own, `yyyyMMdd-HHmmss` of its start;
if nothing but the Hub's own start lines was written there, that folder is removed when the phone's session
takes over.

| File | Content |
|---|---|
| `perf.csv` | The header of §5.3, then one line per `Telemetry` row, in `seq` order, `\n` line ends, no byte-order mark. Byte for byte the phone's file when no row was lost (test L11) |
| `state.bin` | §2.12: every `State` received, with the Hub's receive time |
| `events.jsonl` | One event per line: the phone's object with a member `hub_us` (Hub clock at receipt) added. Hub-made events (`fms`, `seq`, `pg.fmsAlert`, `note`, `asset_ack`) carry `"scene": "hub"` and the Hub clock in `t_us` |
| `phone.log` | `<t_us> <level> <msg>` per `Log` |
| `hub.log` ✚ | `<hub_us> <kind> <JSON>` per line. Kinds: `start`, `stop`, `session`, `join`, `leave`, `bye`, `link`, `cmd`, `param`, `param.ignored`, `preset`, `lock`, `job`, `forge`, `perf` |
| `manifest.json` ✚ | The last `ParamManifest` body, indented |
| `outline.bin` ✚ | The last `RoomOutline` message, as received |

Motion logs are treated as health data (§13.5): the folder sits on an encrypted volume, and delete-by-id removes
it whole.

## 3.5 Console

One page (`GET /`) with six tabs, and a separate pairing page. It opens one WebSocket and builds itself from
the manifest: a new parameter needs no Console code.

Always visible, on every tab: session id; phone state dot with RTT and loss; current space; Bump Guard level
bar; quality tier and thermal state; lock badge; the red **"crops kept"** banner while `forge.debugKeep` is
true; the amber **"hosted fallback armed"** banner while `forge.hosted` is true (§3.10); a thermal banner; a
banner when the Console has lost the Hub.

| Page | Shows | Does |
|---|---|---|
| **Tuner** (`#tuner`) | One block per `group`, folded and greyed while the group is inactive; in it one control per parameter: slider (`float`, `int`), switch (`bool`), selector (`enum`). Each shows the applied value from the echo, min, max and a default mark | Dragging sends `param` at most 30 times per second; an `onRelease` parameter sends once, on release. "Reset" sends the default. Presets: save (current values), load, bake. Under lock the controls are greyed |
| **Operator** (`#operator`) | The six commands; walker's comfort score; notes | Seven space buttons S0 to S6, **Home**, **Recentre**, quality tier 0–4 and "auto", **STOP** (large, red; also the space bar), **Lock** switch, FMS entry 0–20, note field |
| **Dashboard** (`#dashboard`) | One tile per `Telemetry` field; line charts of frame-time p95 and fps over the last 15 minutes; a thermal colour bar; Forge queue length; `State` rate, loss and latency | CSV download link |
| **Map** (`#map`) | The room from `RoomOutline`: outline, free rectangle, object boxes coloured by category; the walker's position and heading at 10 Hz; the trail of the last 600 points; current tile or cell; the nearest wall or object | – |
| **Assets** (`#assets`) | The job list from the `job` frames, with `local`, `hosted`, `cache` tags and each job's state | – |
| **Pairing** (`#pairing`) and `GET /pair` | The QR code of `atlas://<ip>:9050`, the same address as text, every IPv4 address of the Station as a link (`?ip=`), and whether a phone is connected | – |

## 3.6 Forge: process and routes (port 8000)

```
station/forge/
  requirements.txt, requirements.lock   Python 3.11; FastAPI, uvicorn, pydantic v2, python-multipart, numpy, pillow,
                                        trimesh, fast-simplification. The lock is compiled with `uv pip compile
                                        --universal`, so it installs on Windows as well (uvloop is skipped there,
                                        colorama added). requirements-triposg.txt: what TripoSG needs, pinned.
                                        requirements-gpu.txt: rembg, the matting model TripoSG cannot run without
  forge/app.py             the routes below (create_app)
  forge/service.py         rooms, jobs, the queue and its one worker, the attempts, the cache, the Hub notifications (§3.7)
  forge/attempts.py        what the service and reclean share: the settings of the fit for a job, the photos the colours
                           come from, the order in which a job's attempts are offered for publishing (§3.7, §3.9)
  forge/models.py          request and response bodies
  forge/budget.py          the furniture budget: 32 meshes, 120,000 triangles, and the light target (§4.1)
  forge/imaging.py         crop decoding, background removal (rembg when installed), the picture framed for a
                           generator, dominant colour
  forge/generators/        base.py (Generator, GenerationRequest, RawMesh, GeneratorError), triposg.py, hosted.py, procedural.py (§3.8)
  forge/cleanup.py         §3.9
  forge/profile.py         the object profile of a mesh and the fit score (§3.9)
  forge/colour.py          colour per face from the photos (§3.9)
  forge/reclean.py         clean-up again from a kept record (§3.9)
  forge/validator.py       the checks of §4.2; also a command line
  forge/glb.py             the deterministic GLB writer and reader
  forge/palette.py         the 64 object colours of lane A's palette and the vertex-colour encoding (§4.1)
  forge/config.py          every setting
  tests/                   pytest; needs no GPU
  tools/                   check_station.py (checks an install), first_run.py (one object through a running Forge;
                           reads the generator's frame, §3.8)
  data/                    rooms/<roomId>/room.json, room.meta.json, mesh.bin · jobs/<jobId>/job.json (and the record
                           of §3.9 under FORGE_DEBUG_KEEP) · assets/<sha256>.glb, <sha256>.json (both copies of a mesh) ·
                           cache/<key>.json (not committed)
```

Run from `station/forge`: `uvicorn forge.app:app --host 0.0.0.0 --port 8000` (C.7). One process; one worker
thread owns the GPU.

| Variable | Default | Meaning |
|---|---|---|
| `FORGE_GENERATOR` | `triposg` | `triposg`, `procedural` or `hosted` |
| `FORGE_HOSTED_ENABLED`, `FORGE_HOSTED_URL`, `FORGE_HOSTED_KEY` | off, unset | The hosted fallback (§3.10). Unless all three are set, no request ever leaves the Station |
| `FORGE_DEBUG_KEEP` | unset | `1` keeps the record of each job on disk (§3.9): crops, mattes, the generator's raw output, both copies and their reports. Reported as `debugKeep` |
| `FORGE_HUB_URL` | `http://127.0.0.1:8080/internal/job` | Where job changes are posted; empty switches the notifications off |
| `FORGE_PUBLIC_URL` | empty | Prefix of the asset URL in `AssetReady`; empty gives the path `/assets/<sha256>.glb` |
| `FORGE_DATA_DIR` | `station/forge/data` | Storage root |
| `FORGE_MATTING_TIMEOUT_S`, `FORGE_GENERATE_TIMEOUT_S`, `FORGE_HOSTED_TIMEOUT_S`, `FORGE_CLEANING_TIMEOUT_S` | 10, 90, 120, 60 | Stage time-outs (§3.7) |
| `FORGE_MAX_ANISOTROPY` | 0.15 | Stretch allowed per axis when fitting a mesh to its box |
| `FORGE_POOR_FIT` | 0.20 | A fit to the scanned profile above this is poor, in fractions of the box height: the next photo is then tried (§3.7) |
| `FORGE_TRIPOSG_REPO`, `_WEIGHTS`, `_STEPS`, `_GUIDANCE`, `_SEED`, `_FP16`; `FORGE_RMBG_WEIGHTS` | empty, empty, 50, 7.0, 42, on; empty | TripoSG checkout, weights and inference settings |
| `FORGE_TRIPOSG_FALLBACK_DECODER` | on | `1`, `true`, `yes` or `on` (anything else is off): when TripoSG's default decoder fails, generate once more with `use_flash_decoder=False` (§3.8) |
| `FORGE_TRIPOSG_INSIDE_OUT` | off | `1`, `true`, `yes` or `on`: the adapter reverses the winding of every face TripoSG returns. For use when the first run (`tools/first_run.py` reports `outwards`, `inwards` or `unclear` from the sign of the volume the faces enclose) finds the mesh inside out. TripoSG's two decoders may wind differently, and one flag cannot serve both: not known |
| `FORGE_TRIPOSG_UP`, `FORGE_TRIPOSG_FRONT` | `Y`, `unknown` | Which axis of TripoSG's own frame is up, and where the front of its meshes is (§3.8). Read and checked at start-up; another value stops it |

Appendix B.4, with status codes. Errors are FastAPI's `{"detail": …}`: a sentence, or for a body that does not
validate the list of what is wrong.

| Route | Request | Response |
|---|---|---|
| `GET /health` | – | 200 `{"ok": true, "gpu": true, "gpuName": "NVIDIA GeForce RTX 4060 Laptop GPU", "queue": 2, "pipelineVer": "3", "debugKeep": false, "generator": "triposg", "generatorReady": true, "generatorNote": "", "hosted": false, "hostedEnabled": false, "matting": "rembg"}`. Answers at once and never waits for the model. `gpu` is false and `gpuName` null with no CUDA device, and for `triposg` until the model load has imported PyTorch. `queue` counts jobs not yet `ready`, `failed` or `placeholder`. `hostedEnabled` is the same flag as `hosted` under its older name. `matting` is `rembg` or `none`. For `triposg`, `generatorReady` is true only after the model has loaded and with a matting model; `generatorNote` is then empty, and otherwise says what is missing or going on (below) |
| `POST /rooms` | RoomModel JSON (`docs/contracts/roommodel.schema.json`), at most 2 MB. The Forge checks `version` 1, `id`, and each object's `id` (1 … 255, unique) and `size`, whose three sides must be finite and above zero (else 422, and the room is not stored); at most 64 objects | 201 `{"roomId": "…", "roomHash": "…", "generated": [ids], "placeholders": [ids], "triTarget": 15000, "triTargets": {"4": 15000, "7": 6120}, "triTargetLight": 5000}`, also when the id existed (the record is replaced). 422 when it does not validate; 413 |
| `PUT /rooms/{roomId}/mesh` | Binary mesh, at most 2 MB | 204; 404; 413. Optional (§7.4); the app does not send it in Phase 1 |
| `GET /rooms/{roomId}` | – | 200: the exact bytes that were posted; 404 |
| `DELETE /rooms/{roomId}` | – | 204: the room record, its mesh, its jobs and the GLBs made for it alone, both copies of each, are removed (§13.5); 404 |
| `POST /rooms/{roomId}/objects` | `multipart/form-data`: field `meta` (JSON, below); files `view0` (required), `view1`, `view2`, each at most 1 MB | 202 `{"jobId": "1a2b3c4d5e6f7a8b"}`; or 200 `{"jobId": "…", "cached": true, "sha256": "…"}` with `lightSha256`, `lightUrl`, `lightBytes` and `lightTris` when the cached mesh has a light copy (the phone learns of a cached mesh from this answer alone; a cache entry from before the light copy answers as before, with the full mesh only); or 200 `{"jobId": "…", "placeholder": true}` for a box beyond the room's 32. 422 bad `meta`, missing or empty view; 404 unknown room or a `boxId` the room does not have; 413 |
| `GET /jobs/{jobId}` | – | 200: the job object below; 404 |
| `GET /jobs?room={roomId}` | – | 200: a JSON **array** of the same objects, in order of submission; without `room`, every job |
| ✚ `GET /queue` | – | 200 `{"active": {…} \| null, "queued": […], "placeholders": […], "generator": "triposg"}`; `queued` is in the order the worker takes it. Entries are `{jobId, roomId, boxId, volume, state}` |
| `GET /assets/{sha256}.glb` | – | 200 `model/gltf-binary`, `ETag: "<sha256>"`, `Cache-Control: public, max-age=31536000, immutable`; 304 for a matching `If-None-Match`; 404. The light copy is an asset of its own under its own hash |

The job object of `GET /jobs/…`: `{"jobId", "roomId", "boxId", "state", "source", "error", "triTarget",
"cached", "generator"}`, with `triTargetLight` when the job has one, `decoder` (`flash` or `hierarchical`, for a
generator that has two decoders) and `queuePosition` while `queued`. When `ready` it holds the `AssetReady` members
(`sha256`, `url`, `bytes`, `tris`, `maxEdge`, `bounds`, `palette`), the four light members (`lightSha256`,
`lightUrl`, `lightBytes`, `lightTris`) when there is a light copy, `fitUp` and `fitYaw` (how clean-up turned the
mesh, §3.9), `fit` when the job had a profile that covers at least 0.10 of the box, `attempts` (the generations made for the object, failed ones
included; absent for an answer from the cache) and `report` (§3.9). Members with no value are absent, not `null`.

- `roomId` is the RoomModel's `id` (lane C's schema: `[A-Za-z0-9_]{1,40}`). The body is lane C's canonical text
  (`RoomModelJson.Write`). `roomHash` is the SHA-256 of the posted bytes, 64 lower-case hexadecimal digits: a
  receipt for the upload. It is **not** the hash inside `worldHash`, which is lane C's geometry hash (§2.5).
- Lane C keeps a RoomModel file under 300 kB (§7.4; lane C §2.6). The Forge accepts up to 2 MB, so the limit
  is the phone's to enforce and a larger grid does not need a Forge change **(decided here)**.
- At `POST /rooms` the Forge ranks the room's boxes by volume (`size[0] · size[1] · size[2]`, in whole cubic
  millimetres; equal volumes keep the lower id first). The 32 largest (fewer when `FORGE_MAX_GENERATED` or the 8 GB auto cap of 16 lowers it) are `generated`; the rest are
  `placeholders`. `triTargets` gives every box its full target by the rule of §4.1 (0 for a box in `placeholders`),
  `triTarget` is the largest of them (0 for an empty room) and `triTargetLight` is the room's light target (0 for
  an empty room). `forge/budget.py` writes the rule in whole numbers, as `ObjectMeshRules` does on the phone and
  `tools/rooms/roommath.py` in the tools.
- A job works to `min(triTarget sent, the box's own target of the room)` and to `min(triTargetLight sent, the
  room's light target)`, or the room's light target when none was sent.

`generatorNote` of `GET /health`, for `triposg`: empty when the model is loaded. Otherwise one or more of these,
joined by `"; "`: `PyTorch is not installed`; `FORGE_TRIPOSG_REPO does not point at a TripoSG checkout`; `diso is not
installed (TripoSG's decoder; it is built from source, see the README)`; `no matting model is installed (rembg; pip
install -r requirements-gpu.txt)`; `CUDA is not available to PyTorch`; while the worker loads, `PyTorch is being
imported`, `diso is being imported`, `the weights are being downloaded to <dir>` or `the model is being loaded`; before
the load has begun, `the model is not loaded yet`, with `, and the weights are not complete (<the gap>): they are downloaded first` when that is so, the gap being `no model_index.json in <dir>` or `a download into <dir> stopped before it was whole`; and after a failed load `PyTorch cannot be imported: …`, `diso
cannot be imported: …`, `the TripoSG package cannot be imported from <repo>: …`, `the weights could not be downloaded
to <dir>: …`, `the download left no model_index.json in <dir>`, `the model could not be loaded from <dir>: …` (ending ` (a download into <dir> stopped before it was whole)` when a file is still half-way) or `the
matting model could not be loaded: …`.

`meta`:

```json
{"boxId": 4, "category": "table", "dims": [1.2, 0.75, 0.8], "yaw": 0.0, "triTarget": 15000, "triTargetLight": 5000,
 "profile": "<344 characters: 256 bytes as standard base64>", "backend": "local",
 "views": [{"score": 0.82, "azimuthDeg": 35.0, "intrinsics": [612.0, 612.0, 384.0, 384.0],
            "camToBox": [1,0,0,0, 0,1,0,0.4, 0,0,1,-1.5, 0,0,0,1]}]}
```

| Member | Type | Meaning |
|---|---|---|
| `boxId` | int 1 … 255 | `RoomObject.Id` |
| `category` | string, at most 32 characters | `ObjectCategories.Name(Category)`: `couch`, `table`, … (lane C §6) |
| `dims` | 3 numbers, m | `RoomObject.Size`: `[w, h, d]` = extent along the box's local X, Y (up), Z. Each must be finite and above zero, else 422 |
| `yaw` | number, rad | `RoomObject.Yaw` |
| `triTarget` | int 1500 … 15000 | The full target of this object: the phone sends its own `RoomObject.TriTarget`, which `ObjectMeshRules.Targets` gave it by the rule of §4.1 (15,000 for an object when few share the budget, 3,000 at the floor). 422 outside the range. The Forge works to the smaller of this and its own figure for the box |
| `triTargetLight` | int 1500 … 5000, optional | The light copy's target: the phone sends `ObjectMeshRules.LightTarget(n)` = `clamp(60000 ÷ n, 1500, 5000)`, n = objects the client follows. 422 outside the range; `null` reads as absent. An older phone sends none and the Forge uses its own figure for the room |
| `profile` | string, optional | The object profile of the scan as the RoomModel holds it (lane C §2.1): standard base64 with padding of exactly 256 bytes, 344 characters. Anything else, the empty string included, is a 422; `null` reads as absent. Without it the Forge cannot fit (§3.9): the job makes one generation and orients by the bounds |
| `backend` | string | `local`, `hosted` or `cache`: the phone's `forge.backend`. `off` is never sent; it is a 422 |
| `views[i]` | one per file `view<i>`, at most 3 | Lane C's `ObjectView` (lane C §11.4). `score` 0 … 1; `azimuthDeg`: direction from the box centre to the camera in the box's local frame, `atan2(x, z)`; `intrinsics`: `fx, fy, cx, cy` of the crop, pixels (four numbers); `camToBox`: 4 × 4 row by row, 16 numbers, camera frame (Unity: +X right, +Y up, +Z forward) to the box-local frame of §4.1, metres. The colours of the mesh are projected through `intrinsics` and `camToBox` (§3.9) |

## 3.7 Forge: job lifecycle and queue

| State | Work | Time-out | Next |
|---|---|---|---|
| (at upload) | Cache lookup by a key made of the SHA-256 of every view's bytes, the job's `triTarget` and `triTargetLight`, `pipelineVer`, the box (`dims`, `yaw`), the category, the generator's name, the profile, every view's `intrinsics` and `camToBox`, and the settings `FORGE_TRIPOSG_UP`, `FORGE_TRIPOSG_FRONT`, `FORGE_POOR_FIT` and `FORGE_MAX_ANISOTROPY` | – | hit → `ready` (source `cache`, 200 `cached` with the four light members when the cached mesh has a light copy; `GET /jobs/{jobId}` and `GET /jobs?room=` carry them too; an older entry answers as before); `backend` `cache` and miss → `failed`; a box in `placeholders` → `placeholder`; else `queued` |
| `queued` | Waits for the worker | – | `matting` (or `ready` when the cache gained the asset meanwhile) |
| `matting` | Decode the JPEG of `view0`; background removal (`rembg` when installed). A removal that fails is skipped, and the plain crop goes on, for a generator that does not need matting; for `triposg` that view is not sent to the model. Another view is decoded and matted when an attempt or the colours need it | 10 s per view | `generating`; `failed` for a crop that cannot be decoded or a time-out |
| `generating` | The attempts, below: one generation per view, at most three. Each generated mesh is welded, reduced, turned and scaled to the box at once (§3.9, steps 1 to 5), which gives its fit | 240 s per generation (the wait for the card is not counted), 120 s for the hosted service; 60 s for the turn and scale of each mesh | `cleaning`; `failed` |
| `cleaning` | The other views are matted for the colours. The attempts are finished, the best fit first, until one gives a full copy that meets the contract: regrid, colour, shade, then the light copy, both validated by §4.2 (§3.9, steps 6 to 12) | 60 s for each attempt finished | `ready`; `failed` |
| `ready` | Both copies are stored under their hashes; the cache entry holds the whole answer | – | – |
| `failed` | `error` holds a sentence for a person, for example `view 0: …`, `clean-up: …`, `asset contract: [edge.long] …`; the sentences of several attempts are joined by `"; "` | – | – |
| `placeholder` | The box is beyond the 32 largest of its room and receives no mesh; `error` says `beyond the 32 largest boxes of the room; it receives no mesh and keeps its first shape` | – | – |

**Attempts** (decision M7). With a profile, a fit above `FORGE_POOR_FIT` (0.20) is poor and sends the next view to
the generator, view 1 and then view 2 when they exist; any other fit stops. The rules:

- At most **three generations** are made for an object, and a failed one is one of the three. A job with no profile
  has no fit, so it makes one generation and does not look at the other views.
- **A mesh is always published** (decision M6). The attempt that fits best is finished and published, however poor
  its fit; a poor fit is reported in `fit` and never hidden. Equal fits keep the earlier view first. When the best
  cannot be finished (clean-up cannot use it, or its full copy violates the contract) the next best is tried; the
  job fails only when no attempt gives a valid full copy.
- **How failures count.** A generation that raises, times out or returns a mesh clean-up cannot use is a failure.
  After two failures with no mesh the generator is given up for the object and the hosted service is tried if it
  is armed (§3.10), else the job fails. A failure after a poor mesh does not stop the third view. A view that
  cannot be matted, or whose matte is empty, makes no generation and is not a failure; the next view is tried.
- **The Station's own generator only** (`source` `local`) is sent another photo for a poor fit. The hosted service
  is given one view, `view0`, once.

- **Queue order:** largest box first, by volume `dims[0] · dims[1] · dims[2]`; ties in arrival order. One job on
  the GPU at a time. The order is across all rooms.
- **At most 32 objects per room** receive meshes (plan §4.5). The phone sends at most 32; a job for a box
  outside the Forge's own 32 (fewer when `FORGE_MAX_GENERATED` or the 8 GB auto cap of 16 lowers it) is answered with `placeholder: true` and is never generated.
- A second upload for a `boxId` whose job is still waiting or running returns that job.
- **Notification.** At every change of state the Forge posts `{"type": "JobStatus", "jobId", "roomId",
  "boxId", "state", "source", "error"}` to `FORGE_HUB_URL`; at `ready` it posts `{"type": "AssetReady", …}`
  as well, with the light members when there is a light copy. A post that fails is not retried: the phone polls (§4.3).
- **Crops and mattes** are held in memory and dropped when the job ends. `FORGE_DEBUG_KEEP=1` is the only thing that
  writes one to disk (§7.4, test L20); what it writes is the record of §3.9.
- **Restart.** Rooms, finished jobs and assets are read back from `data/`. A ready job whose full copy's file is gone
  is `failed`; one whose light copy's file is gone keeps its full copy and loses its light members. A job that was
  waiting or running is marked `failed` (its crops are gone) and the phone sends it again; with `FORGE_DEBUG_KEEP=1`
  it runs again by itself, with `attempts` 0 and an empty attempt log and timings. A room record from before the furniture budget is planned again.
- `jobId` is sixteen hexadecimal digits. `pipelineVer` is a version string (`"3"`), part of the cache key and of
  `asset.generator`: raising it makes every earlier cache entry a miss. It was raised to `"2"` when the picture TripoSG
  receives changed and to `"3"` with the fit, the colours and the light copy.
- A corrupt image ends as `failed`; the worker goes on with the next job (`P1-D08`).
- `DELETE /rooms/{roomId}` removes that room's jobs and both copies of each mesh made for it alone; one that is on
  the GPU is dropped when its stage ends.

## 3.8 Forge: generator adapter

```python
# station/forge/forge/generators/base.py
from dataclasses import dataclass
from typing import Optional, Protocol, Sequence
import numpy as np
from PIL import Image

class GeneratorError(RuntimeError): ...
class GeneratorUnavailable(GeneratorError): ...

@dataclass
class GenerationRequest:
    image: Image.Image          # the matted crop, RGBA; for a generator with needs_matting, the framed picture, RGB
    category: str
    dims: Sequence[float]       # scanned box [w, h, d], metres
    seed: int = 42

@dataclass
class RawMesh:
    vertices: np.ndarray        # (N, 3), the generator's own frame and scale
    faces: np.ndarray           # (M, 3)
    decoder: str = ""           # which of the generator's decoders made it ("flash", "hierarchical"); "" when it has one

class Generator(Protocol):
    name: str      # "triposg", "hosted" or "procedural"
    source: str    # what JobStatus.source reports: "local" or "hosted"
    needs_matting: bool         # true for "triposg": never given a picture that is not matted

    def availability(self) -> tuple[bool, str]: ...     # (usable, reason), without loading any model
    def warm_up(self) -> None: ...                      # loads what generate needs; nothing once loaded; raises GeneratorUnavailable
    def gpu(self) -> Optional[str]: ...                 # the GPU's name, or None
    def generate(self, request: GenerationRequest) -> RawMesh: ...
```

Contract of `generate`:

- Input: the matted crop, RGBA; alpha is the object. A generator with `needs_matting` receives instead the picture
  framed for it (below), RGB.
- Output: one triangle mesh, any scale, any triangle count, in the generator's own frame. Clean-up finds the
  up axis and the quarter turn that fit the box (§3.9).
- The worker runs it on a helper thread and gives up after the stage's time-out; any failure is a
  `GeneratorError`. One call at a time. `warm_up` is called when the service starts, on the worker, and again
  before each generation.
- It writes no file and keeps no reference to the image.
- `seed` is the fixed `FORGE_TRIPOSG_SEED` (42).

| Adapter | `source` | What it is | Usable when |
|---|---|---|---|
| `triposg` | `local` | TripoSG (MIT) on the RTX 4060 Laptop (8 GB). Written from the public repository's inference script. It ran once on 4 October 2026 at about 87 s per object; its tests drive a fake pipeline | PyTorch, a CUDA device, the TripoSG checkout, `diso` and a matting model are present and the model has loaded |
| `hosted` | `hosted` | A hosted image-to-3D service: open item 8. The interface exists; no vendor is wired (`HostedGenerator._request` raises `GeneratorUnavailable`) | `FORGE_HOSTED_ENABLED=1` and `FORGE_HOSTED_URL` and `FORGE_HOSTED_KEY` both set |
| `procedural` | `local` | Deterministic, no GPU, no network: a plausible mesh for the category built from the box size alone (table, chair, couch, bed, storage, lamp, plant, screen, stairs, basin, toilet, plain box), returned normalised to a unit cube. Every shape faces local +Z: the back of a chair, a couch, a bed and a toilet is on −Z, a cupboard's doors on +Z. The photo is not looked at, and it needs no matting. For the Forge's own tests, for a Station with no GPU, and for cache tests (L18) | Always |

**What the `triposg` adapter does around the model** (mesh design §5.1). None of it has run on the Station.

- **Matting is required.** TripoSG is never given a picture that is not matted. With no matting model, `generatorReady`
  is false and a job fails with `TripoSG cannot run: no matting model is installed …`. A view whose matting fails, whose
  matte is empty (`the matte is empty: no object was found in the crop`) or holds only specks (`the matte holds only specks: no object was found in the crop`) or holds an object under 16 pixels on its long
  side is not sent; the error names the view and ends `triposg is given matted pictures only` where matting was missing.
- **The picture is framed** as TripoSG's own script frames it (`imaging.frame_for_generator`): detached pieces of
  matte under 0.5 % of it are left out, the crop is cut to the bounding box of the matte, put on white, and padded to a
  square with a margin of `int(0.10 × the long side)` pixels on every side, the object in the middle. The model's
  processor resizes the short edge to 256 and keeps the central 224 × 224, so only a square reaches it whole. The crop
  and its matte stay in memory for the colours (§3.9).
- **The model is loaded when the service starts**, by the worker, so the first object does not pay for it; the matting
  model is loaded then too. Jobs wait in `queued` meanwhile. A load that failed is tried again by each generation.
  The weights count as incomplete when their folder has no `model_index.json` or holds a half-way file (`*.incomplete` under its download cache); such a folder is downloaded (resumed) before the load. `model_index.json` alone proves nothing, since it arrives first: a folder that shows no gap and still does not load is fetched once and read again, and a load that fails then says if a file is still half-way. After a download that returned without an error the load is tried whatever lies in the folder.
- **Two decoders.** The default (`flash`, which needs `diso`) is called exactly as upstream calls it. If it raises or
  returns nothing, the object is generated once more with `use_flash_decoder=False` (`hierarchical`) when
  `FORGE_TRIPOSG_FALLBACK_DECODER` is on, and the job reports which decoder made its mesh. Both calls share the one
  generation time-out. After two objects in a row on which the default decoder failed and the fallback made the mesh,
  the fallback is tried first; one success of the default decoder switches back. A job whose two decoders both fail
  ends `TripoSG inference failed: flash decoder: …; hierarchical decoder: …`.

`triposg_up` and `triposg_front` are settings, read from `FORGE_TRIPOSG_UP` and `FORGE_TRIPOSG_FRONT`, case
ignored, and describe TripoSG's meshes only; for any other generator clean-up assumes nothing about the frame.

| Setting | Values | Meaning |
|---|---|---|
| `triposg_up` (default `Y`) | `Y`, `Z`, `X` | The axis of TripoSG's own output frame that points up. It is the **only** up axis clean-up tries for a TripoSG mesh (another generator's meshes are tried with all three). Not measured: `Y` is the guess of the design |
| `triposg_front` (default `unknown`) | `canonical`, `view`, `unknown` | Where the front of a TripoSG mesh is. `canonical`: on the mesh's own +Z, so clean-up prefers no turn. `view`: towards the camera of the photo, so it prefers the turn that puts the mesh's +Z side on the side of the box that the `camToBox` of the view the mesh was generated from (not always view 0) places the camera on. `unknown`: nothing is preferred. It breaks ties of the fit (§3.9) and, for a job with no profile, decides alone among the orientations the bounds allow; with `unknown` and no profile the bounds choose |

**Unmeasured.** TripoSG's output frame has never been seen, so both settings are guesses until the first run on the
Station; `tools/first_run.py --analyse FILE --up-axis Y|Z|X --yaw-degrees 0|90|180|270` and the raw output kept under
`FORGE_DEBUG_KEEP` are how it is read (README, "First run on the Station", step 6). The fit chooses the orientation
by the scan whatever these say, so a wrong `triposg_front` costs only a worse tie-break, or a wrong turn for a job
with no profile. A wrong `FORGE_TRIPOSG_UP` costs more, because no other axis is tried: a mesh whose up is another axis
does not fill the box within the stretch limit, the bounds then decide, and the full copy is likely to fail the
validator's `frame.bounds` (`asset contract: [frame.bounds]`) for every job until the setting is corrected; a mesh that
stands on its head is not among the candidates at all, and a scan seen from above cannot tell it. Also unmeasured:
the time and memory per object, the size of the raw mesh, whether `diso` builds and runs on the Station, and whether
the fallback decoder works where the default one fails (it is inferred from the source and one open report).

## 3.9 Forge: mesh clean-up and its report

In this order (§7.4, `P1-D09`; mesh design §5.3; `forge/cleanup.py`, `clean(…)`, which is `orient` for steps 1 to 5
and `finish` for steps 6 to 12). Every step is deterministic: the same raw mesh, box and photos give the same bytes,
and so the same hash.

| # | Step | Detail |
|---:|---|---|
| 1 | Weld | Merge coincident vertices; drop degenerate, duplicate and unreferenced elements |
| 2 | Decimate | To the full target, when the mesh has more triangles than that (quadric collapse through `fast-simplification`; vertex clustering when that cannot do it). A raw TripoSG mesh is probably several hundred thousand triangles (inferred from its 505³ grid, never seen) |
| 3 | Drop floaters | Remove connected pieces holding less than 0.5 % of the surface area |
| 4 | Orient | By the fit to the scan, below |
| 5 | Scale to the box | One uniform scale, then a correction per axis of at most ± 15 % (`FORGE_MAX_ANISOTROPY`). Centre on x and z; lowest point at y = 0 |
| 6 | Regrid | Split every edge longer than 0.25 m (against 0.2499 m, so float32 storage cannot push one over). If the count then exceeds the target, decimate lower and split again, in up to six rounds. If it is under 1,500, split the longest edges until it is not |
| 7 | Colour per face | A palette cell for every face from the photos, below |
| 8 | Normals | Vertex normals that stay sharp across creases of more than 40°. Vertices are split at creases and again where two faces of different cells meet, so that every vertex of a face points at that face's cell |
| 9 | Shade | Per vertex: ambient occlusion (32 directions, mapped to 0.35 … 1) times the face shade of lane A's `AtlasShading`, with the normal turned into the room frame by `yaw`. Written to `COLOR_0` (§4.1) |
| 10 | Export | GLB with a fixed layout (§4.1), 16-bit indices, `asset.generator` `"ATLAS Forge <pipelineVer>"`; SHA-256 of the bytes |
| 11 | Light copy | From the full copy, below; steps 6 to 10 run again on it |
| 12 | Validate | Every check of §4.2, each copy at its own level. A violation of the full copy makes the attempt unusable (`asset contract: [<code>] …`); a light copy that violates its level is left out |

**The fit** (steps 4 and 5; `orient_by_fit`). The orientation is chosen by the scanned profile (the `profile` of
`meta`, lane C §2.1) and not by the bounds alone.

- **Candidates.** Every up axis of the generator, times four quarter turns about it (0°, 90°, 180°, 270°). The
  generator's up axes are Y, Z and X, or only `triposg_up` for TripoSG (§3.8). A candidate is kept when the largest
  stretch beyond the uniform scale that one axis needs to fill the box is at most `FORGE_MAX_ANISOTROPY`. When none is
  kept, the orientation the bounds choose and the same turned by a half are the candidates.
- **Score.** Each candidate is scaled to the box and given its own profile, made from the mesh by the same downward
  rays as the phone's (`forge/profile.py`; the mesh as it is after step 2). The score is `fit_score(scan, generated)`:
  the mean of |generated − scanned| ÷ 255 over the cells the scan saw, in fractions of the box height; a cell where the
  mesh has no surface counts as height 0. Lower is better; 0 is a perfect match.
- **Choice.** The lowest score wins, except that scores within `FIT_TIE` = 0.02 of the lowest are a tie, which is
  broken in this order: the turn `triposg_front` prefers (§3.8); then the generator's own up axis (the first of its up
  axes); then the lower score. The last two rules are an addition of the Forge **(decided here)**: a scan seen from
  above cannot tell a cube on a plinth from the same cube on its side, so another up axis must fit better by 0.02 to be
  taken.
- **No profile**, or a profile in which the scan saw no cell or covers under 0.10 of the box (`attempts.MIN_FIT_COVERAGE`, the fit's own floor, not the phone's 0.30 for facing and first shapes; the job still stores the profile the phone sent): the turn `triposg_front` prefers decides among the
  candidates the bounds allow (the one with the better bounds, if several); with nothing preferred, the bounds alone
  choose, as before the fit existed: the up axis among Y, Z and X and a yaw of 0° or 90° (bounds cannot tell 0° from
  180°), the generator's own frame winning unless another fits the box clearly better. `fit` is then absent.
- **Direction of the turn.** One quarter turn of clean-up is a right-handed +90° about +Y in the GLB frame, which is a
  yaw of −90° in Unity's box frame. The report's `yawDegrees` counts the turn in the GLB frame; `fitYaw` is the same
  turn as a yaw in Unity's box frame, `(360 − yawDegrees) mod 360`: the azimuth of the box at which the mesh's own +Z
  side stands.

**Colour per face** (step 7; `forge/colour.py`, decision M8). One cell of the palette's object colours (64 … 127) per
face, from every photo the phone sent whose crop is matted and whose `intrinsics` and `camToBox` make a camera. A view
that is not matted gives no colour, so on a machine with no matting model every object keeps one cell from the dominant
colour of the matted `view0`, as before.

1. A face is seen by a view when it turns towards the camera (cosine above 0.2), its centre projects inside the crop,
   the matte is solid there (0.5), and nothing lies between: an exact ray test from the camera to the face centre,
   tolerance 0.01 m, both sides of a triangle blocking. A view that would need more than 12,000,000 ray-triangle tests
   sees nothing.
2. The face's colour is the mean, in linear light, of the 5 × 5 pixels around its projected centre that belong to the
   matte, weighted by the cosine, over the views that see it. It is mapped to the nearest object cell with
   `nearest_entry`.
3. Smoothing: three passes in which a face takes the cell that two of its three neighbours share (each pass reads
   the one before); regions under 2 % of the surface merge into their largest neighbour; then the smallest regions
   merge until at most 6 cells remain.
4. A face no view saw takes the cell of the nearest seen face along the surface, centre to centre over faces that
   share a corner or an edge. The 2 % merge and the limit to 6 cells then run again over the whole mesh.
5. If more than half of the seen faces changed cell in the passes of step 3 and the 2 % merge, the photo and the mesh do not
   line up, and the object takes one cell, from the dominant colour of the matted `view0`.

`AssetReady.palette` and `report.colour.paletteIndex` are the cell that covers the most surface. The shader, the
palette and the vertex format do not change. Only faces wound counter-clockwise seen from outside count as seen (`FORGE_TRIPOSG_INSIDE_OUT`, §3.6, turns a mesh that is not).

**The light copy** (step 11; decision M10). It is made when the job has a light target: the full copy's surface is
decimated to the light target (1,500 … 5,000), put back on the full copy's bounds so that both fill the same box, then
regridded, coloured, shaded and exported as for the full copy, and validated at the light level. It is published under
its own SHA-256. When the full copy is already within the light target the light copy is the same file, and
`lightSha256` equals `sha256`. When a light copy cannot be made, or violates its level, there is none: the full copy is
published alone, the `light` member of the report says `{"error": "…"}`, and the light members are absent.

`report`, returned by `GET /jobs/{jobId}` when `ready` and kept beside the asset for §11.4.7. It describes the full
copy; the numbers here are an example, made up for the layout:

```json
{"triangles": 14998, "vertices": 9120, "longestEdge": 0.2496, "size": [1.2, 0.75, 0.8], "box": [1.2, 0.75, 0.8],
 "scaleError": [0.0, 0.0, 0.0], "maxScaleError": 0.0, "sourceTriangles": 321344, "triTarget": 15000,
 "upAxis": "Y", "yawDegrees": 90, "level": "full", "fit": 0.0814, "fitYaw": 270, "fitUp": "Y",
 "candidates": [{"up": "Y", "yawDegrees": 90, "fit": 0.0814, "stretch": 0.0412}],
 "colour": {"paletteIndex": 110, "name": "wood", "rgb": [148, 122, 96], "photoRgb": [151, 120, 90],
            "cells": [{"index": 110, "share": 0.8213}, {"index": 79, "share": 0.1787}], "views": 3},
 "bytes": 410220, "sha256": "3f0a…9c1e", "violations": [],
 "light": {"sha256": "7c1d…02ab", "bytes": 61240, "triangles": 1875},
 "attempts": 1, "notes": ["decimated from 321344 triangles"]}
```

| Key | Meaning |
|---|---|
| `triangles`, `vertices`, `longestEdge`, `size`, `box`, `bytes`, `sha256` | What the file holds, as stored (float32); the box is the scanned one |
| `scaleError`, `maxScaleError` | `scaleError[i]` is `(size[i] − box[i]) / box[i]`, and `maxScaleError` its largest magnitude (must: ≤ 0.10; should: ≤ 0.05, §11.2) |
| `sourceTriangles`, `triTarget` | The raw mesh after welding, and the target this copy worked to |
| `level` | `full` or `light`: which copy the report describes |
| `upAxis`, `fitUp` | The axis of the generator's frame taken as up: `Y`, `Z` or `X` |
| `yawDegrees` | Clean-up's own quarter turns, 0, 90, 180 or 270, right-handed in the GLB frame |
| `fitYaw` | The same turn as a yaw in Unity's box frame, `(360 − yawDegrees) mod 360` |
| `fit` | The score of the chosen orientation, 4 decimals; `null` for a job with no profile or one that covers under 0.10 |
| `candidates` | Every orientation that was scored, best first: `{up, yawDegrees, fit, stretch}`; empty with no profile |
| `colour` | `paletteIndex`, `name`, `rgb`: the cell that covers the most surface; `photoRgb`: the dominant colour of the photo; `cells`: `[{index, share}]`, every cell of the mesh and the share of the surface it covers, largest first; `views`: how many photos the colours were read from, 0 meaning one cell from the dominant colour |
| `violations` | What the validator found at the copy's level; empty for a published copy |
| `light` | In the full copy's report: `{sha256, bytes, triangles}` of the light copy, or `{error}` when there is none |
| `attempts` | The generations made for the object (added by the service) |
| `notes` | Sentences for a person, for example `decimated from 321344 triangles` |

The time each stage took is kept in the job record (`timings`: `matting`, `generating`, `cleaning`, seconds), not in
the report. The job record also holds `attemptLog`: one object per generation, `{view, generator, decoder, seconds,
fit, fitYaw, fitUp, error, published}`.

**The record** (decision M14). Under `FORGE_DEBUG_KEEP=1` a job's folder `data/jobs/<jobId>/` holds, beside
`job.json`: `view<i>.jpg`, the crops as uploaded (camera frames: they never leave the Station); `matte<i>.png`, the
matte of each matted crop; `raw_view<i>.npz` for the attempt from view `i` (`vertices` float64, `faces` int32,
`decoder`), the generator's output as it came; `full.glb` and `light.glb` (when there is one), the published copies; and
`report.json`, `{"full": …, "light": … or null}`. `job.json` holds the box, the targets, the profile, the cameras and
the attempt log. None of it enters git (`station/forge/.gitignore` holds `data/`); only the numbers of
`tools/meshcheck/check.py` and pictures rendered from the meshes go into `docs/evidence/mesh/`.

- `python -m forge.reclean <jobId or folder> [--data DIR] [--out DIR] [--json]` reads such a folder and runs clean-up
  again as the service did, without the generator, with the settings of the present environment: every kept
  attempt is turned and scored, the best is finished. It prints the fit of each attempt, the new report and whether the
  copies are the bytes that were published (`sameBytesAsPublished` with `--json`). It writes nothing into the record;
  `--out` names a folder for the new `full.glb`, `light.glb` and `report.json`. Exit 0 when a copy was made, 1 otherwise.
- `tools/meshcheck/check.py --data DIR --room ID [--truth FILE] [--poor-fit X] [--json FILE] [--pictures DIR]` reads a
  whole room's record and prints the numbers of checks M1 and M2 of the mesh design (§5.6 there), per object and in
  total. `truth.json` (`rooms/<roomId>/truth.json` of the data folder unless `--truth` names another) is written by
  hand: for each object id the side of its box, as the RoomModel holds it, that is really the front (`+Z`, `+X`, `-Z`,
  `-X`) or `none`; the phone's facing is right when it is `+Z`. Thresholds: 90 % facing, median fit 0.12, 60 s per
  attempt, poor fit 0.20. A line gives a verdict only for what the record settles: with nothing measured (for example no object has a profile), or with objects missing from `truth.json` or without a fit or a timed attempt, it gives none and names them. The exit code is 0 only when every line that asks for something passes, 1 when one fails, 3 when none fails and one is not settled (the last line prints `N of M lines pass, … no pass`), 2 when the record cannot be read. `--json` carries `cached` on each job and `asks` on each line; a box is judged by the last job that generated, a cached answer standing in only when it is the box's only job. It reads a mesh's front by the phone's own profile rule, so an object that is flat on top is named, not
  judged. `--pictures` writes the scan's profile beside the mesh's for each object.

## 3.10 Forge: the hosted fallback and its disclosure

The hosted service receives a crop of the walker's room. Its retention is not ours to control (§7.4).

| # | Rule |
|---:|---|
| 1 | Unless `FORGE_HOSTED_ENABLED=1` and a URL and a key are configured, no request ever leaves the Station, whatever `backend` says (test L21). `GET /health` reports `"hosted": false` |
| 2 | When it is armed, `"hosted": true`, and the Console shows the amber "hosted fallback armed" banner on every page |
| 3 | A crop is sent to the hosted service only when the job's `backend` is `hosted`, or it is `local` and the local tries gave no mesh (§3.7). It is sent `view0` and nothing else, once: a poor fit never sends it a second photo. Never when the `backend` is `cache` |
| 4 | Before a walker from outside the team scans, the operator tells them (the booth sign's data line says the same). If they decline, `forge.backend` is set to `cache` on the Tuner page: their objects come from the cache or the built-in set |
| 5 | Every hosted job says so: `JobStatus.source` is `hosted`, the Console's job list tags it, and the Forge's log records each use with the host, never the image |
| 6 | Hosted output is credited in the licence ledger (§8.4) |

---

# Part 4 · Assets

## 4.1 The GLB asset contract

What every mesh the Forge serves must satisfy (§6.4, §7.4, §8.4). The validators of §4.2 check it.

Every object comes in **two copies**, each held to its own level: the **full** copy, drawn in Home and Curved Walk, and a
**light** copy, drawn in the other spaces and whenever the quality ladder or `render.furniture` asks for it (§1.4).
The rows below hold for both except where a row names the level.

| | Full copy | Light copy |
|---|---|---|
| Triangles | 1,500 … 15,000, and at most the `triTarget` of its job | 1,500 … 5,000, and at most the `triTargetLight` of its job |
| File | At most 2 MB (2,097,152 B) | At most 1 MB (1,048,576 B) |

| Topic | Rule |
|---|---|
| Container | GLB, glTF 2.0: one JSON chunk, one BIN chunk. At most 2 MB (2,097,152 B) for the full copy, 1 MB (1,048,576 B) for the light copy |
| Content | Exactly one scene, one node, one mesh, one primitive. The node has no translation, rotation, scale or matrix. Primitive mode 4 (triangles) |
| Forbidden | `materials`, `textures`, `images`, `samplers`, `skins`, `animations`, `cameras`, `extensionsUsed`, `extensionsRequired`. The primitive has no `material`. No Draco, no meshopt, no KTX |
| Attributes | Exactly `POSITION`, `NORMAL`, `TEXCOORD_0`, `COLOR_0`, with the same count. Indices `UNSIGNED_SHORT` (the Forge writes nothing else; the phone also reads `UNSIGNED_INT`) |
| `POSITION` | `VEC3` `FLOAT`, metres, with the accessor's `min` and `max` |
| `NORMAL` | `VEC3` `FLOAT`, unit length within 0.01 |
| `TEXCOORD_0` | `VEC2` `FLOAT`: the **palette coordinate** of lane A's vertex format (lane A §10.2, §10.7): the centre of a palette cell, one of the object colours 64 … 127. An asset uses **one or more** cells, and a face is one flat colour: every vertex of a face has the same value, the centre of that face's cell, so a vertex shared by faces of different cells is split. `AssetReady.palette` is the cell that covers the most surface. Unity UV of cell i: `((i mod 16 + 0.5) / 16, (i div 16 + 0.5) / 16)`. Written in glTF's convention (v downwards): `(u, 1 − v)` |
| `COLOR_0` | `VEC4` `UNSIGNED_BYTE`, normalised, linear: lane A's vertex colour. **R = shade**: ambient occlusion times the face shade, `round(255 · saturate(ao · shade))`. **G = 255** (line weight; furniture has no line coordinates), **B = 255, A = 255** (reserved) |
| Units and axes | Metres, +Y up |
| Frame | The **box-local frame** of the object: origin at the centre of the box's base (RoomModel `objects[].base`), +X along the box's `w`, +Y up, +Z along its `d`, as lane C defines the box in Unity's left-handed convention |
| Handedness | A GLB is right-handed. The file stores `x_glTF = −x_box`, `y` and `z` unchanged, with counter-clockwise front faces. The phone's reader (`GlbMeshReader`) negates X on positions and normals, flips v and re-winds the triangles, which gives the box-local frame exactly. A mesh that looks mirrored on the phone means one side skipped this |
| Size | The mesh fills its box: on each axis the extent differs from `dims` by at most 10 % (should: 5 %). The Forge measures against that axis's own side, with 5 mm of slack; the phone against the longest side of the box |
| Placement | The Forge: footprint centre within 0.01 m of the origin on x and z, lowest point within 0.01 m of y = 0. The phone accepts 5 % of the longest side and 0.02 m (proposed) |
| Triangles | Per copy, as in the table above, and never more than the target of its job for that copy |
| Edges | No triangle edge longer than 0.25 m, so the mesh can bend in a curved space |
| Identity | The SHA-256 of the file is its name (`<sha256>.glb`), its cache key and its `ETag` |
| Material | None in the file. The phone draws it with a pooled instance of the shared look material (README F7) |

The room's furniture is at most 32 such objects and 120,000 triangles of full copies (plan §4.5; mesh design §5.4). The
32 largest boxes by volume (whole cubic millimetres, ties to the lower id) receive meshes and share the budget by surface
area (the Forge generates fewer when `FORGE_MAX_GENERATED` or the 8 GB auto cap of 16 lowers it; the targets stay these), in whole numbers so that the phone (`ObjectMeshRules`), the Forge (`forge/budget.py`) and `tools/rooms/roommath.py`
give the same figures:

```text
a_i    2·(w·h + h·d + w·d), with w, h, d in whole millimetres: mm(v) = floor(v·1000 + 0.5)
t_i    clamp( floor(120000 · a_i / A), 3000, 15000 )        A = the sum of a_i: the full target of object i
if the sum of t_i is above 120000:
    E = sum of (t_i − 3000)        B = 120000 − 3000·N      N = the objects that receive meshes
    t_i = 3000 + floor( (t_i − 3000) · B / E )
light  clamp( 60000 ÷ N, 1500, 5000 ), whole-number division, the same for every object
```

An object beyond the 32 has a target of 0 and keeps its first shape (§4.4). Twelve equal boxes get 10,000 each and a
light target of 5,000; thirty-two get 3,750 and 1,875. A room that is all stand-ins keeps 5,000 on each
(`ObjectMeshRules.StandInTriangles`). The total of the full copies never passes 120,000, and of the light copies
never 60,000. The worst full copy, every vertex split, is 45,000 vertices of 36 bytes and 90,000 bytes of indices:
1.71 MB, inside the 2 MB.

The shade, as the Forge bakes it (lane A's `AtlasShading`, the same formula every mesh generator uses):

```text
n_box   = (−n_glTF.x, n_glTF.y, n_glTF.z)                       the unit vertex normal in the box-local frame
n_room  = ( n_box.x·cos(yaw) + n_box.z·sin(yaw),  n_box.y,  −n_box.x·sin(yaw) + n_box.z·cos(yaw) )
light   = normalize(0.35, 0.85, 0.40)                           room frame
shade   = 0.55 + 0.45 · (0.5 + 0.5 · dot(n_room, light))
ao      = 0.35 … 1, from the ambient-occlusion pass (§3.9)
R       = round(255 · clamp(shade · ao, 0, 1))
```

The file has no `TEXCOORD_1`, so no grid or edge line is drawn on furniture; the reader leaves the line
coordinates at (0, 0).

The 64 object colours the Forge may choose are computed in `forge/palette.py` from the formulas of lane A
§10.7 (rows 4 to 7: index 64 + 16 k + column; value V = 0.28, 0.42, 0.58, 0.74 for k = 0 … 3; columns 0 … 11
HSV(column · 30°, 0.50, V); column 12 HSV(30°, 0.12, V); 13 HSV(210°, 0.12, V); 14 HSV(28°, 0.35, V); 15
HSV(0°, 0, V)). `tools/gen_forge_palette.py` prints the same table as JSON (index, cell-centre UV in Unity's
convention, sRGB colour), writes it with `--out`, and with `--check` compares it with what the Forge
computes. No `palette.json` is committed. The indices are frozen; if lane A retunes a colour the constants
are changed in both places.

## 4.2 The validators

The contract is checked twice: in the Forge before a job may become `ready` (`forge/validator.py`,
`validate_glb`), and on the phone on the downloaded bytes, on a worker thread, before a mesh is built from
them (`GlbValidator.Validate`). The two are separate implementations with their own code names. Both check a copy
at its **level**, full or light (§4.1): the phone takes it from `GlbExpectation.Level` (the default is the full copy),
the Forge from the `level` argument of `validate_glb`. The phone
stops at the first failed check, in the order of the table; its code is what appears in `AssetAck.reason`
(`validate:<code>`, except `hash_mismatch` and `too_large`). The Forge collects every violation; its codes
appear in `JobStatus.error` as `asset contract: [<code>] <sentence>`.

| # | Check | Phone code | Forge code |
|---:|---|---|---|
| 1 | Magic `glTF`, version 2, the declared length equals the file length, a JSON chunk then a BIN chunk | `container` | `glb.malformed` |
| 2 | File size ≤ 2,097,152 B (full) or ≤ 1,048,576 B (light) | `size` (`AssetAck` `too_large`) | `glb.size` |
| 3 | SHA-256 of the file equals `AssetReady.sha256` | `hash` (`AssetAck` `hash_mismatch`) | – |
| 4 | One scene, one node with no transform, one mesh, one primitive, mode triangles | `structure` | `mesh.count`, `frame.transform`, `mesh.mode` |
| 5 | None of the forbidden members of §4.1 | `no_material` | `material.present`, `texture.present`, `glb.extension`, `mesh.count` |
| 6 | Exactly the four attributes with the types of §4.1 and equal counts; the index count is a multiple of 3; every index is below the vertex count | `attributes` | `attributes.missing`, `glb.malformed` |
| 7 | Triangles ≥ 1,500 | `tris_min` | `triangles.range` |
| 8 | Triangles ≤ 15,000 (full) or ≤ 5,000 (light) | `tris_max` | `triangles.range` |
| 9 | Triangles ≤ the job's target for that copy: `triTarget` (full) or `triTargetLight` (light) | `tris_target` | `triangles.target` |
| 10 | Every position and normal is finite; no triangle of area under 10⁻¹⁰ m² | `finite` | `glb.malformed` (finite positions only) |
| 11 | Longest triangle edge ≤ 0.25 m (phone: 0.5 mm of slack; Forge: 0.01 mm) | `edge` | `edge.long` |
| 12 | Each bounding-box side within 10 % of the box (§4.1, Size) | `bounds` | `frame.bounds`; with no box given, `frame.units` for an extent over 6.6 m |
| 13 | Centred on x and z; lowest y at 0 (§4.1, Placement) | `origin` | `frame.origin` |
| 14 | Every vertex's palette cell (from `TEXCOORD_0`) is an object cell, 64 … 127, and the three vertices of a face are in one cell | – | `palette.cell`, `palette.face` |

Unit normals and `COLOR_0`'s G = B = A = 255 are the Forge's to write (`forge/palette.py`,
`encode_vertex_cells`) and are covered by its clean-up tests; neither validator checks them.

Check 14 is the Forge's alone: the phone's checks stay 1 to 13, and its validator checks no palette cell. The Forge's
codes are new for the palette (`palette.cell`, `palette.face`); `triangles.range`, `glb.size` and the others keep their
names and follow the level. Its result holds the `level` and the `paletteCells` the mesh uses.
`python -m forge.validator FILE --target N --box W H D --level full|light` runs it from the command line (`--level`
defaults to `full`).

On the phone, `triTarget` is the object's own `RoomObject.TriTarget` for a full copy and
`ObjectMeshRules.LightTarget(n)`, n being the objects the client follows, for a light one; `dims`
is the object's `Size`.

```csharp
// File: ATLAS/Assets/Atlas/Telemetry/Assets/GlbValidator.cs   (engine-free: System and Atlas.Link only)
namespace Atlas.Telemetry
{
    public enum GlbLevel : byte { Full = 0, Light = 1 }

    public struct GlbExpectation
    {
        public float DimX, DimY, DimZ;        // RoomModel size [w, h, d], metres
        public int TriTarget;                 // the job's target for this copy (3000..15000 full, 1500..5000 light); 0 skips check 9
        public GlbLevel Level;                // Full (the default) or Light
        public string Sha256;                 // null: skip check 3
    }

    public struct GlbReport
    {
        public bool Ok;
        public string FailedCheck;            // null when Ok; else the code of the first failed check
        public int Bytes, Vertices, Triangles;
        public float MaxEdge;                 // metres
        public float BoundsX, BoundsY, BoundsZ;
    }

    public static class GlbValidator
    {
        public const int MaxBytes = 2097152, MinTriangles = 1500, MaxTriangles = 15000;      // the full copy
        public const int MaxBytesLight = 1048576, MaxTrianglesLight = 5000;                  // the light copy
        public const float MaxEdgeMetres = 0.25f, EdgeSlack = 0.0005f;
        public const float BoundsTolerance = 0.10f, CentreTolerance = 0.05f, BaseTolerance = 0.02f;
        public const double MinTriangleArea = 1e-10;
        public const int MaxTemplateTriangles = 300;
        // CodeContainer = "container", CodeSize = "size", CodeHash = "hash", CodeStructure = "structure", … one const per code above

        /// <summary>Checks 1 to 13 on the raw file. No Unity API: safe on a worker thread.</summary>
        public static GlbReport Validate(byte[] glb, in GlbExpectation expect);
        public static GlbReport Validate(byte[] glb, int length, in GlbExpectation expect);
        /// <summary>The template row of section 4.4: POSITION and NORMAL only, at most 300 triangles, the unit box.</summary>
        public static GlbReport ValidateTemplate(byte[] glb);
        public static string Sha256Hex(byte[] bytes, int length);
        public static int MaxBytesOf(GlbLevel level);          // MaxBytes or MaxBytesLight
        public static int MaxTrianglesOf(GlbLevel level);      // MaxTriangles or MaxTrianglesLight
    }
}
```

Beside it, also engine-free: `GlbFile` (`Assets/GlbFile.cs`: the parsed container, `TryParse`, `TryAccessor`,
`TryPrimitive`) and `GlbMeshReader.TryRead(byte[] glb, int length, MeshData into)` (`Assets/GlbMeshReader.cs`).

## 4.3 The asset client

`Atlas.Telemetry.AssetClient`, created by `AssetClientInstaller.Install(boot, telemetry)` right after
`TelemetryInstaller.Install` (§5.1); null in the Spectator, which has its own reader (§5.8). It is the thin
Unity adapter round `AssetClientCore`, the engine-free state machine that uploads the RoomModel and the
crops, follows the jobs, downloads, validates, reads and hands each mesh to lane C's room content. It never
blocks a frame: HTTP goes through `UnityWebRequest` (`UnityForgeTransport`), started and looked at from step
8b; hashing, the validator, mesh reading and the cache files run on the thread pool; the frame only uploads
the finished vertex and index buffers.

It is driven from three sides, all on the main thread:

| From | Call | Meaning |
|---|---|---|
| Room content (lane C §9.1) | event `IRoomContent.Built(room)` (`AssetClient.Bind(content)`, once the app has booted) | A room is active and its placeholder boxes exist. The client starts a room: every object gets its first shape, and cached meshes are placed |
| Photo capture (lane C §11.4) | `IObjectPhotoSink.BeginRoom(room, roomJson)`, `Submit(photos)`, `ReportNoPhoto(objectId)`, `EndRoom()` | A fresh scan: the RoomModel text to post, then the crops of each object, largest first |
| The link | `LinkClient.JobStatusReceived`, `AssetReadyReceived` → `AssetClient.OnJobStatus`, `OnAssetReady` | Job progress from the Hub |

The Forge's base URL is `Welcome.forgeUrl` when the Hub gave one, else `http://<paired Station address>:8000`
(`AssetClientInstaller.ForgeUrl`).

### States per object

| State | Meaning | RoomModel `mesh` (`MeshState`) | On screen |
|---|---|---|---|
| `Placeholder` | Not one of the objects the client follows, or nothing sent yet | `Placeholder` | First shape (§4.4); the placeholder box until it is placed |
| `Uploading` | Its crops are waiting to be sent or being sent: attempt 1, 2 or 3 | `Placeholder` | The same |
| `Requested` | The Forge holds a job for it | `Requested` | The same |
| `Downloading` | `AssetReady` known; the GLB is being read from the cache or fetched, hashed, validated and read | `Requested` | The same |
| `Loading` | The mesh data is ready and waits for its hand-over | `Requested` | The same |
| `Ready` | The generated mesh is placed; `AssetAck ok` follows 0.5 s later | `Ready`, with `AssetHash`, and `AssetHashLight` once the light copy is placed | Generated mesh: the full copy, or the light one where the room content's level is Light |
| `BuiltIn` | The object keeps its first shape for good: no photo, a failed job or a rejected asset (§4.4) | `BuiltIn` | First shape, or the box when none could be made |

Every object the client follows, and every other object of the room, gets its first shape as soon as the room content is
built, whatever state the Forge is in (§4.4); the generated mesh replaces it. A first shape is placed with
`SetBuiltInMesh`, so the RoomModel's `mesh` reads `BuiltIn` from then on, and `SetObjectState(Requested)` is
refused for an object that shows one: the `mesh` column above holds only while the box is still the object's mesh.

The objects the client follows are those with `TriTarget > 0` whose `Mesh` is not `MeshState.StandIn`, the 32
largest by volume at most (`AssetClientCore.MaxObjects` = `ObjectMeshRules.MaxMeshedObjects`; ties to the lower id).
Every other object, one with `TriTarget` 0 or beyond the 32, is not sent to the Forge and keeps its first shape for good
(state `BuiltIn`). A stand-in object (the fixtures' own furniture) is left alone.

The RoomModel entry is changed only by room content, through the four calls the asset client makes:
`IRoomContent.SetObjectState(objectId, MeshState.Requested)`, `SetGeneratedMesh(objectId, mesh, assetHash)`,
`SetGeneratedLightMesh(objectId, mesh, assetHashLight)` and `SetBuiltInMesh(objectId, mesh, Vector3.one)` (lane C §9.1).
Room content keeps a full and a light mesh per object and draws one by its `FurnitureLevel`; an object that has no
light mesh draws its full one at either level. Saving the RoomModel when an object changes,
which keeps the `boxId → sha256` record of §7.4, is lane C's.

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Assets/AssetClient.cs, AssetClientCore.cs, AssetClientInstaller.cs, ForgeTransport.cs, FittedShapes.cs
namespace Atlas.Telemetry
{
    public enum ObjectAssetState : byte
    { Placeholder = 0, Uploading = 1, Requested = 2, Downloading = 3, Loading = 4, Ready = 5, BuiltIn = 6 }

    public sealed class AssetClient : Atlas.World.IObjectPhotoSink, Atlas.World.IAfterFrameStep, IAssetRoom
    {
        public const float LoadedRoomGraceSeconds = AssetClientCore.LoadedRoomGraceSeconds;   // 2 s

        public AssetClient(AssetClientCore core);
        public static AssetClient Create(IForgeTransport transport, IAssetCache cache, IAssetWorker worker,
                                         FallbackSet fallback, Atlas.World.IEventLog events);
        public AssetClientCore Core { get; }
        public bool ForgeReachable { get; }          // the last GET /health answered 200
        public int PlacedCount { get; }              // Ready                  → Telemetry.assetsPlaced
        public int PendingCount { get; }             // Uploading … Loading    → Telemetry.assetsPending
        public int BuiltInCount { get; }
        public ObjectAssetState GetState(int objectId);
        public void Bind(Atlas.World.IRoomContent content);   // follows its Built event; null detaches

        // IObjectPhotoSink (lane C section 11.4)
        public void BeginRoom(Atlas.World.RoomModel room, string roomJson);
        public void Submit(Atlas.World.ObjectPhotoSet photos);
        public void ReportNoPhoto(int objectId);
        public void EndRoom();

        // The link (section 2.10)
        public void OnJobStatus(Atlas.Link.JobStatusMessage status);
        public void OnAssetReady(Atlas.Link.AssetReadyMessage ready);

        /// <summary>DELETE /rooms/{roomId} and removal of this room's cached GLBs (section 13.5).</summary>
        public void ForgetRoom(string roomId);
        public void AfterFrame(in Atlas.World.FrameContext frame);   // step 8b
        public void Dispose();
    }

    public static class AssetClientInstaller
    {
        public const string ParamForgeBackend = "forge.backend";
        public const int ForgePort = 8000;
        /// <summary>Publishes the client as IObjectPhotoSink and as AssetClient. Null in the Spectator.</summary>
        public static AssetClient Install(Atlas.World.AtlasBootContext boot, AtlasTelemetry telemetry);
        public static string ForgeUrl(Atlas.Link.WelcomeMessage welcome, PairingController pairing);
    }

    // What the core is given, so that it runs with no engine and under test:
    public interface IForgeTransport          // UnityForgeTransport in the app
    {
        int Begin(string method, string url, byte[] body, string contentType, float timeoutSeconds);
        bool TryTake(int handle, out int status, out byte[] body);    // status 0: no answer
        void Cancel(int handle);
    }
    public interface IAssetRoom               // AssetClient, towards IRoomContent
    {
        bool SetRequested(int objectId);
        bool PlaceGenerated(int objectId, Atlas.Kernel.MeshData mesh, string sha256);
        bool PlaceGeneratedLight(int objectId, Atlas.Kernel.MeshData mesh, string sha256);   // the light copy
        bool PlaceBuiltIn(int objectId, Atlas.Kernel.MeshData mesh);                          // a first shape
    }
    public interface IAssetCache              // FileAssetCache: persistentDataPath/assets/<sha256>.glb
    {
        byte[] TryRead(string sha256);
        void Write(string sha256, byte[] glb);
        void Delete(string sha256);
    }
    public interface IAssetWorker { void Run(System.Action<object> work, object state); }   // ThreadPoolAssetWorker
}
```

`AssetClientCore` holds the constants of the tables below (`MaxAttempts`, `HealthTimeout`, `RoomTimeout`,
`ObjectTimeout`, `PollTimeout`, `DownloadTimeout`, `PollInterval`, `ProbeInterval`, `JobSilenceSeconds`,
`WorkTimeout`, `AckWindowSeconds`, `BuiltInPerTick`, `FirstShapeReason`, `LightRetrySeconds`, `LightRetryCapSeconds`) and `ForgeUrl`, `Backend`, `AckSink`, `RoomBuilt`,
`RoomCleared`, `Tick(double timeSeconds, float frameMs)`. `ForgeProtocol` builds the `meta` JSON and the
multipart body.

### The round trip

1. **`Built`.** The first shapes are due: every object of the room, followed or not, is given its first shape
   (step 9). Every followed object whose `AssetHash` is set goes straight to step 5 and is read from the
   cache (`persistentDataPath/assets/<sha256>.glb`): the pre-scan path needs no Station (D21). When its
   `AssetHashLight` is set too, the light copy is read from the cache after the full one (step 10). A `Built` for
   another room ends the previous one: its requests are cancelled and late answers are dropped. A `Built` for the
   same room again (the room content rebuilt its views) makes every first shape due again and puts the light
   copies behind the full ones again.
2. **`BeginRoom(room, roomJson)`.** `POST /rooms` with `roomJson` as UTF-8, before the first object, unless
   `forge.backend` is `off`. If no `BeginRoom` for this room has come within 2 s of `Built` (proposed), the
   room came from a file or a fixture and no photo will follow: every followed object that is still
   `Placeholder` becomes `BuiltIn` (reason `no_scan`) and keeps its first shape.
3. **`Submit(photos)`.** `POST /rooms/{roomId}/objects`, one upload at a time, in the order received (largest
   box first). `meta` is built from the `ObjectPhotoSet`: `boxId` = `ObjectId`, `category` =
   `ObjectCategories.Name(Category)`, `dims` = `Size`, `yaw`, `triTarget` = the object's own `RoomObject.TriTarget`,
   `triTargetLight` = the photo set's when above 0, else `ObjectMeshRules.LightTarget(n)` for the n objects the client
   follows, `profile` = the object's profile as base64, sent only when the object has one, `backend`, and per view
   `score`, `azimuthDeg`, `intrinsics` (`x, y, z, w` = fx, fy, cx, cy) and `camToBox` row by row (row r, column c =
   `CamToBox[c][r]`). 202 → `SetObjectState(Requested)`. 200 with `cached` → step 5. 200 with `placeholder` →
   the object keeps its first shape. A 404 means the Forge no longer knows the room: it is posted again first. Any other refusal
   → `BuiltIn`. The JPEG arrays are released when the upload ends. **`ReportNoPhoto(objectId)`**, a
   `Submit` with no view, and at **`EndRoom`** every object neither submitted nor reported → `BuiltIn` at once.
4. Job progress arrives as `JobStatus` and `AssetReady` through the Hub. While an object is `Requested` the
   client also polls `GET /jobs?room={roomId}` every 2 s (§7.4), whatever the link's state; it reads the
   Forge's array and a `{"jobs": […]}` wrapper alike. A job the Forge no longer lists → `BuiltIn`.
5. `failed` → `BuiltIn` at once (the first shape stays). `ready` → `Downloading`, one object at a time: the worker
   first looks in the cache; if the file is not there, `GET <forgeUrl><url>` and the worker again: SHA-256,
   `GlbValidator.Validate` at the full level against the object's own `TriTarget`, `GlbMeshReader.TryRead` into a
   `MeshData` in lane A's vertex format, and the cache write (`<sha256>.glb.part`, then renamed). A damaged file in
   the cache is fetched again. An `AssetReady`, a job in the list, or the answer to an upload that the cache satisfied
   that carries a valid `lightSha256` (and `lightUrl`, default `/assets/<sha256>.glb`) marks the object as having a
   light copy to fetch (step 10).
6. Hand-over, on the main thread at step 8b, at most one generated mesh per frame: the adapter makes a
   `UnityEngine.Mesh` from the `MeshData` (`SetVertexBufferData`, `SetIndexBufferData`, one sub-mesh, bounds
   from the data) and calls `IRoomContent.SetGeneratedMesh(objectId, mesh, sha256)`. Room content owns the
   mesh from then on, assigns the pooled look material, cross-fades from what is shown over 0.3 s and raises
   `ObjectChanged` (lane C §9.1). The asset client never touches a renderer or a material: `Atlas.Telemetry`
   cannot reference `Atlas.Rendering`.
   (differs from plan §7.4: the phone reads the GLB with its own reader instead of glTFast, so that all
   decoding is off the main thread and no importer object is made per asset; the reader accepts exactly the
   encoding of §4.1.)
7. `AssetAck`: `ok` true, `loadMs` = download complete → hand-over, `worstFrameMs` = the worst frame time
   from the hand-over until 0.5 s later (must: < 20 ms, test L17). Also sent with `ok` false and a `reason`
   whenever step 5 or 6 rejects the asset; that object becomes `BuiltIn` and keeps its first shape. A mesh that came
   from the cache of a saved room belongs to no job and is not acknowledged. The light copy is never acknowledged.
8. Every step writes one `asset` event (§2.6). §11.4.7 is read from them. The steps and their fields:

   | `step` | Fields besides `boxId` | When |
   |---|---|---|
   | `room`, `upload`, `requested`, `cached`, `ready`, `placed`, `rejected`, `failed`, `unreachable`, `reachable` | Unchanged | The round trip above |
   | `builtin` | `reason`, `placed`, `kept`, `shape`, `tris` | A first shape was placed or could not be, or an object that shows one is given up for good |
   | `light_placed` | `sha256`, `tris` | The light copy was handed to the room content |
   | `light_dropped` | `sha256`, `reason` | The light copy was given up for good (step 10) |
   | `light_deferred` | `sha256`, `reason`, `retryIn` | A transport failure; the light copy is tried again after `retryIn` seconds |
   | `light_same` | `sha256`, `reason` (`same_as_full`) | The light copy is the full copy; nothing is fetched |

   For `builtin`: `reason` is `first_shape` for the shape every object gets when the room content is built, and
   otherwise why the object will have nothing else (`unreachable`, `no_photo`, `no_scan`, `backend_off`, `job_failed`,
   `rejected`, …); `placed` is true when a shape stands; `shape` is `template`, `fitted` or `scancut`, or `kept` for the
   event that gives up an object whose first shape is already there; `kept` is empty when a shape was placed and
   otherwise says why the box stays: `no_template` (no template and a profile that cannot be used), `too_large`
   (the template and the scan-cut shape both pass 1,500 triangles) or `no_view`; `tris` is the shape's triangle count.
   The Spectator ignores the `builtin` events whose reason is `first_shape` (§5.8).
9. **First shapes.** On the main thread at step 8b, at most two (`BuiltInPerTick`) per frame, in the order of the
   objects (the followed ones, largest first, then the others): `FittedShapes.Build` makes the mesh from the category's
   template, fitted by the object's profile for a chair, couch or bed, or cut from the profile (§4.4); the adapter
   makes the `UnityEngine.Mesh` and calls `IRoomContent.SetBuiltInMesh`. The adapter keeps it, and destroys a
   mesh that was replaced only once the room content no longer draws it (§4.4, Hand-over). An object that reached `Ready` first
   gets none.
10. **The light copy.** One download slot serves the objects. Every full copy that waits goes first; only when none
    waits, and the room is built, does the slot take the light copy of the first `Ready` object that has one pending:
    the cache first, then `GET <forgeUrl><lightUrl>` with the same 20 s time-out and 3 attempts as the full copy,
    the SHA-256 against `lightSha256`, `GlbValidator.Validate` at `GlbLevel.Light` against
    `ObjectMeshRules.LightTarget(n)` (so at most `min(5,000, LightTarget(n))` triangles and 1,048,576 bytes), the same
    reader, and the hand-over at step 8b, one mesh per frame, through `IAssetRoom.PlaceGeneratedLight` to
    `IRoomContent.SetGeneratedLightMesh`. Any failure (`hash_mismatch`, `too_large`, `validate:<code>`,
    `download_failed`, a work time-out, `load_failed`, or `no_cache` for a saved room with no Forge) never disturbs
    the full copy: no `AssetAck`, no change of state; the object stays `Ready` and shows its full mesh. A transport
    failure (a failed download after the usual attempts, a work time-out, no Forge and nothing cached) keeps the light
    copy pending with a back-off (`light_deferred`, `retryIn`: 15 s, then 30, 60, at most 120 s, reset by a success, and
    cleared when the Forge is reachable again). A content failure (wrong hash, failed validation, cannot be loaded, the
    room content refuses it) gives it up for good: `light_dropped`, and a room built again does not re-arm it. When
    `lightSha256` equals `sha256` nothing further is fetched and `SetGeneratedLightMesh` is not called: the room content
    draws the full mesh at the Light level, `AssetHashLight` is recorded, and `light_same` is logged with reason
    `same_as_full`. A message
    with no light fields behaves exactly as before. The Spectator fetches no light copy (§5.8).

`forge.backend`: `local`, `hosted` and `cache` are sent in `meta.backend`. `off`: nothing is sent to the Forge;
every followed object with no cached mesh becomes `BuiltIn` at once.

### Time-outs and retries

| Step | Time-out | Retries | When it fails |
|---|---|---|---|
| Reachability, before every upload attempt: `GET /health` | 2 s | – | The attempt has failed |
| `POST /rooms` | 10 s | 3 attempts in all, waits of 1 s and 2 s between them | Forge unreachable |
| `POST /rooms/{roomId}/objects` | 15 s | 3 attempts in all, waits of 1 s and 2 s | Forge unreachable |
| Forge unreachable | – | `GET /health` every 4 s | Every object not yet `Ready` keeps its first shape, which stood from the moment the room content was built; the reason `unreachable` is logged at most 9 s after the first attempt (3 × 2 s + 1 s + 2 s), so within the 10 s of test L19. When `/health` answers again, the client posts the room and the crops it still holds again, and each generated mesh replaces its first shape |
| Job silence: no `JobStatus` and no successful poll | 30 s | – | Forge unreachable |
| `GET /jobs?room=` | 5 s | every 2 s while needed | – |
| `GET /assets/<sha256>.glb` | 20 s | 2 more | `AssetAck` `download_failed`; `BuiltIn` |
| Hash and validator | – | – | `AssetAck` `hash_mismatch`, `too_large` or `validate:<code>`; `BuiltIn` |
| Worker (hash, validate, read) | 10 s | – | `AssetAck` `load_failed`; `BuiltIn` |
| The same four rows for a light copy | as the full copy | as the full copy | A transport failure (`download_failed` after the 3 attempts, a work time-out, `no_cache`) is `light_deferred` and is tried again after 15 s, then 30, 60 and at most 120 s; a content failure (`hash_mismatch`, `too_large`, `validate:<code>`, `load_failed`) is `light_dropped`. The object stays `Ready` with its full mesh |

An object that is `BuiltIn` because its job failed, its asset was rejected, or it had no photo stays `BuiltIn`, with its
first shape.
Only "Forge unreachable" is retried. For that retry the client keeps the crops of objects it could not upload,
in memory, until they are uploaded or the room ends.

Privacy (§7.4): the client holds crops only until the upload that carries them has ended, or the room ends, and
writes none to disk. Nothing but crops, the RoomModel text and `meta` leaves the phone.

## 4.4 The built-in fallback set

One mesh per object category, shipped in the build (`P1-D12`, plan §4.5), and the first shapes made from them. Every
object has a **first shape** as soon as the room content is built, whether the Forge is reachable or not and before
any photo is sent; the generated mesh replaces it when it arrives, and it stays for good where there is none
(mesh design §5.2). Its mesh state is `builtIn` and its limits are the built-in ones below.

| Object | First shape |
|---|---|
| `chair`, `couch`, `bed` | The category's template fitted by the object's profile: the seat or mattress stands at the median height of the seen cells of the front half (rows 8 to 15 of the profile, local +Z being the front), as a fraction of the box height, kept within 0.25 … 0.70 for a chair, 0.25 … 0.65 for a couch and 0.30 … 0.75 for a bed (the templates' own heights are 0.50, 0.45 and 0.55); the back stays on local −Z at the full height. The template is stretched in height, piecewise linear, so that the floor, the seat and the top of the box stay where they are and every rectangle stays a rectangle. With no profile, a profile that sees under 0.30 of the footprint, or an unseen front half, the template is used as it is |
| Every other category with a template | The template, as before. It faces the right way because lane C turns the box to face local +Z |
| `unknown`, `other`, and any template that cannot be built within the limits | A shape cut from the scan: the profile reduced to 8 × 8 (the highest of each 2 × 2), unseen cells filled from their seen neighbours (the mean, in passes), one column per cell from the floor to its height, the top and the steps between columns as faces. If that passes 1,500 triangles it is built again at 4 × 4. Tinted with the cell of the category's placeholder box |
| No profile, or coverage under 0.30, and no template (or a scan-cut shape that is too large too) | The placeholder box stays. This is the only case in which a box is seen once the room content is built; the `builtin` event says `kept` `no_template` or `too_large` |

The same code (`FittedShapes`, `Telemetry/Assets/FittedShapes.cs`, engine-free; `FirstShapeKind`: `None`, `Template`,
`Fitted`, `ScanCut`) builds the first shapes on the phone and, from the RoomModel it fetches from the Forge, on the
Spectator.

| Topic | Rule |
|---|---|
| Categories | Every `ObjectCategory` of lane C §6 except `Unknown` and `Other` have a template: `couch`, `table`, `bed`, `lamp`, `plant`, `screen`, `storage`, `bathtub`, `chair`, `dishwasher`, `fireplace`, `oven`, `refrigerator`, `sink`, `stairs`, `stove`, `toilet`, `washerDryer`, `keyboard`, `mouse`, `laptop` (21), keyed by `ObjectCategories.Name`. An object of category `Unknown` or `Other` has none and takes the shape cut from the scan; only with no usable profile does it keep its placeholder box (`asset` event `builtin` with `kept` `no_template`) |
| Files | `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasFallback/<name>.glb.bytes`, loaded with `Resources.Load<TextAsset>("AtlasFallback/<name>.glb")`, and `manifest.json` beside them (`Resources` name `AtlasFallback/manifest`), read by `FallbackSet.Load` |
| Template | A GLB with one mesh, `POSITION` and `NORMAL` only, in the frame of §4.1, filling the **unit box**: x and z in [−0.5, 0.5], y in [0, 1]. At most 300 triangles. CC0 (`GlbValidator.ValidateTemplate`) |
| How they are made | `tools/gen_fallback_set.py` (lane D; Python standard library only) builds each one from a short list of boxes (a chair is a seat, a back and four legs) and writes the 21 files and `manifest.json`. Deterministic: running it again changes no byte. The meshes are the team's own work, released CC0; a CC0 kit mesh may replace any file if it meets the template row. `.meta` files come from the repository's own tool |
| Placement | `FallbackMeshBuilder.Build(template, size, yaw, into)` (engine-free, `Telemetry/Assets/FallbackMeshBuilder.cs`) fills a `MeshData` (lane A §10.2) for **this** box: scale the template to the box's `Size`; cut every face into a grid of cells no longer than 0.1767 m, so no edge passes 0.25 m and two faces that share an edge cut it alike (no crack); bisect what is still too long; normals from the template; palette coordinate = the cell centre of the colour below; shade = `AtlasShading.FaceShade` of the normal turned by the box's `Yaw`, times an occlusion of 0.7 at the floor rising to 1.0 at 0.3 m; line weight 255; line coordinates (0, 0) |
| Colour | A template: an object colour (cells 64 … 127), not the placeholder's tint, so that a built-in mesh can be told from a placeholder. A scan-cut shape: the category's placeholder tint, as the box had it. `manifest.json` gives the cell per category (`templates.<name>.palette`); the script's table is the source **(decided here)** |
| The `Mesh` | `AssetClient` builds a `UnityEngine.Mesh` from the `MeshData` with the five vertex attributes of lane A §10.2 in one stream. One mesh per object, already at the box's size. At most two are built and placed per frame (`BuiltInPerTick`) |
| Hand-over | `IRoomContent.SetBuiltInMesh(objectId, mesh, Vector3.one)`. Room content does not destroy a built-in mesh (lane C §9.1): the asset client keeps it and destroys it only when the room content no longer draws it (`IRoomContent.IsMeshDrawn(Mesh)`; the engine-free `RetiredMeshes<T>` in `Telemetry/Assets` keeps the replaced meshes and sweeps them each frame; `SpectatorFurniture` does the same), or at shutdown |
| Limits | At most 1,500 triangles and no edge over 0.25 m **after** scaling (§8.4, test L19). A template on a box so large that the split passes 1,500 triangles gives way to the shape cut from the scan, and when that is too large too, or has no profile to be cut from, the object keeps its placeholder box, with an `asset` event `builtin` and `kept` `too_large` |
| When | Always, as a first shape, as soon as the room content is built (`reason` `first_shape`). It then stays for good where (b) there is no photo; (c) the job `failed`, was lost or refused; (d) the asset was rejected; (g) the object is not one of the 32 the Forge meshes; and it stands until the generated mesh arrives where (a) the Forge is unreachable after 3 failed attempts; (e) `forge.backend` is `off`; (f) a room was loaded from a file with no mesh named for the object, or whose named mesh is not in the cache and no Forge answers |
| Tagging | The RoomModel object's `mesh` is `builtIn`; the `asset` event has step `builtin` with its `reason` and `shape` (§4.3, step 8) |
| Replacement | In case (a) each first shape is replaced by the generated mesh when the Forge returns |

---

# Part 5 · Telemetry

Everything of lane D that runs inside the Unity app: the installers, the step 8 glue, the perf probe, the HUD,
the event log, pairing, the on-device menu, the Spectator and the mini-map.

## 5.1 The installers and step 8

Lane A's frame loop gives lane D two kinds of hook (lane A §7): one `IStateSink` at step 8a, and
`IAfterFrameStep`s at step 8b. Lane D adds nothing else to the frame.

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/TelemetryInstaller.cs, AtlasTelemetry.cs, SessionId.cs
namespace Atlas.Telemetry
{
    public static class TelemetryInstaller
    {
        public const string ParamDemoLock = "demoLock";
        public const string ParamLinkEnabled = "link.enabled";
        public const string ParamStateHz = "link.stateHz";
        public const string ParamTelemetryMode = "telemetry.mode";
        public const string ParamHudVisible = "hud.visible";
        public const string BakedDefaultsResource = "AtlasParams/defaults";

        /// <summary>Step 18 of the creation order (lane A section 8.2). Creates lane D's systems under boot.Systems
        /// and boot.Hud, publishes IEventLog, IPerfSource, PairingController and AtlasTelemetry, and adds the frame
        /// hooks when the frame loop is already published. transport null means the real UDP one.</summary>
        public static AtlasTelemetry Install(Atlas.World.AtlasBootContext boot, Atlas.Link.ILinkTransport transport = null);
    }

    /// <summary>What the installer made. Published through AtlasServices for onboarding, the menu and the Spectator.</summary>
    public sealed class AtlasTelemetry
    {
        public Atlas.Link.ParamRegistry Params { get; }
        public Atlas.Link.LinkClient Link { get; }       // read it, do not poll it: its owner is Pump
        public LinkPump Pump { get; }
        public PairingController Pairing { get; }
        public EventLog Events { get; }
        public AtlasPerfProbe Probe { get; }
        public AtlasHud Hud { get; }
        public LogFileWriter Files { get; }              // null in the Spectator
        public string SessionId { get; }
        public Atlas.Link.LinkState LinkState { get; }
        public void AddHooks(Atlas.World.IFrameLoop loop);   // once; the installer calls it when the loop is published
        public bool Hooked { get; }
        /// <summary>A new session: a new id, new files, row numbers from 0 and the id announced to the Station.
        /// Onboarding calls it when a new walker starts.</summary>
        public string BeginSession();
        public void Shutdown();                          // Bye, flush, close; safe to call twice
    }

    public static class SessionId
    {
        public static string Make(System.DateTime utc, int random);
        public static string New();
    }
}
```

The asset client has its own installer, `AssetClientInstaller.Install(boot, telemetry)` (§4.3), which the
composition root calls next; the menu announces itself through `AppHooks.Menu` (§5.7).

What `TelemetryInstaller.Install` does, in order:

| # | Creates | Publishes · hooks |
|---|---|---|
| 1 | The session id; the cached `Application.persistentDataPath`; `LogFileWriter` with its writer thread (§5.5). No files in the Spectator | – |
| 2 | `EventLog` | `AtlasServices.Replace<IEventLog>` |
| 3 | `LinkClient` (role `phone` in the walker's app, the Editor included; `spectator` when `boot.IsSpectator`, then with no registry) on a `LiteNetTransport`, created by type name because `Atlas.Telemetry` does not reference LiteNetLib (`Telemetry/link.xml` keeps it through stripping). `LinkPump`; `PairingController` on `PlayerPrefs` | `Replace<PairingController>` |
| 4 | `AtlasPerfProbe`; `AtlasHud` under `boot.Hud` (§5.4); on the phone `FlatTwinHud.Install(boot)` (§5.9) | `Replace<IPerfSource>` |
| 5 | The parameters of 1.4 with their callbacks; baked defaults (1.7); `demoLock` and `hud.visible` for the build | – |
| 6 | Taps: `Application.deepLinkActivated` and `Application.absoluteURL` to pairing; `Application.logMessageReceived` to the phone log and `Log`; a `TelemetryLifecycle` component under `boot.Systems` for pause, resume and quit | `Replace<AtlasTelemetry>`; events `boot` and `session` |
| 7 | – | `AddHooks(loop)` now if `IFrameLoop` is published. On `AtlasServices.Booted`: the hooks if still missing; `IWalkerKernel` and `IOperatorCommands` for `LinkPump`; `ISpaceHost.RoomChanged` → `LinkPump.SetRoom`; `IQualityState` for the probe and the `quality.tier` event |
| 8 | The link is started when `link.enabled` is on **and** this phone has paired before (§5.6). On a first launch nothing is sent until onboarding asks | – |

The hooks, in the order they run:

| Step | Hook | Class | Work, in order |
|---|---|---|---|
| 8a | `IStateSink.Send(in StateMessage state)` | `LinkPump` | 1. Compare with the previous frame's `State` and log the edges: `bump.level`, `tracking.lost`, `tracking.back`, `pose.jump`; on a change of `worldVer` or `spaceId`, the world hash, `space.entered` and the `world` event. 2. Keep the second's highest `bumpLevel` and the cue count. 3. `LinkClient.SendStateCopy` (it thins to `link.stateHz`, §2.10); only role `phone` sends |
| 8b | `IAfterFrameStep.AfterFrame(in FrameContext frame)` | `FlatTwinHud` | While `hud.map` is on, rebuild the map every 0.1 s (§5.9) |
| 8b | the same | `AtlasPerfProbe` | Sample this frame's timings and counters. Once per second: take the link's share of the row from `LinkPump.TakeSecond`, make the `Telemetry` row, hand it to `LinkPump.SubmitRow` (§5.2, §5.3) |
| 8b | the same | `LinkPump` | 1. While pairing, one connect attempt per second. 2. `LinkClient.Poll()`: applies `ParamSet`. 3. Hands queued commands to `IOperatorCommands`. 4. When ready: the manifest if `ManifestVersion` changed, the `RoomOutline` if not yet sent, up to 16 queued events and 16 waiting rows. 5. The `param` events of `Local` changes, at most once per second. 6. Pairing tick |
| 8b | the same | `AtlasHud` | Show or hide the panel and the dot; rebuild their content every 0.5 s |
| 8b | the same | the asset client's wiring | `AssetClient.AfterFrame`: start or finish requests, at most one generated mesh handed over; then `AssetsPlaced` and `AssetsPending` to the probe and `ForgeReachable` to pairing |
| 6, 8b | `IUploadStep`, `IAfterFrameStep` | `OnDeviceMenu` | §5.7 |

`LinkPump`, the link driver (`Telemetry/Link/LinkPump.cs`), is what other code talks to:

```csharp
public enum TelemetryMode : byte { Live = 0, PhoneCsv = 1, Hud = 2 }

public sealed class LinkPump : Atlas.World.IStateSink, Atlas.World.IAfterFrameStep
{
    public const double RetrySeconds = 1.0;       // connect attempts while pairing
    public const int RowBacklog = 600;            // telemetry rows kept while offline

    public LinkPump(Atlas.Link.LinkClient client, EventLog events = null, LogFileWriter files = null);
    public Atlas.Link.LinkClient Client { get; }
    public Atlas.Link.LinkState State { get; }
    public long NowUs { get; }
    public bool IsStarted { get; }
    public string Host { get; }
    public int Port { get; }
    public Atlas.World.IOperatorCommands Commands { get; set; }
    public TelemetryMode Mode { get; set; }
    public PairingController Pairing { get; set; }
    public Atlas.Kernel.IWalkerKernel Kernel { get; set; }    // for the world hash; null: q 4, one metre per unit
    public string WorldHashNow { get; }
    public event System.Action<byte, byte> SpaceChanged;      // (previous, current); 255 is "no space"
    public int BumpLevel { get; }
    public int CommandsApplied { get; }
    public int RowsWaiting { get; }

    public void SetAddress(string host, int port);
    public bool Start();
    public bool Start(string host, int port);
    public void Stop(string reason = "stop");
    public void ReconnectNow();
    public void Send(in Atlas.Link.StateMessage state);                // step 8a
    public void AfterFrame(in Atlas.World.FrameContext frame);         // step 8b: Tick()
    public void Tick();
    public bool TryDequeueCommand(out Atlas.Link.OperatorCommand command);   // for a root that applies commands itself
    public static string StateName(Atlas.Link.LinkState state);       // "offline", "resync", "connected", "degraded"
    public void TakeSecond(ref PerfRowContext context);
    public void SubmitRow(in Atlas.Link.TelemetryMessage row);
    public void ResetSession();
    public void SetRoom(Atlas.World.RoomModel room);                   // the RoomOutline and the world's room; null clears
}
```

Rules:

- Nothing here waits. A request that is not finished is looked at again next frame.
- Cost on the main thread, whole of lane D: 0.2 ms target, 0.5 ms cap (§8.5, the "Link, telemetry, HUD" row).
  0 B allocated per frame in steady state.
- With no `IStateSink` call (no frame loop yet) nothing is sent. With `link.enabled` off nothing is sent either;
  the edge events of 8a, the probe, the HUD, the asset client's local work and the event file still run.
- On `RoomChanged` (`ISpaceHost`), `LinkPump.SetRoom` fills a `RoomOutlineMessage` from the RoomModel
  (`Outline`, the tallest wall, `FreeRect`, `Objects`: lane C §14.4), takes the room's id and geometry hash for
  the world hash, and sends the outline.
- Pause, resume and quit reach lane D through `TelemetryLifecycle`, a `MonoBehaviour` with no `Update`. On
  pause the files are flushed; on resume an offline link tries at once; on quit `AtlasTelemetry.Shutdown`.

## 5.2 `AtlasPerfProbe`

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Perf/AtlasPerfProbe.cs, PerfProbeCore.cs, PerfRing.cs
using System;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>Frame timings and render counters: a 3,600-frame ring, one Telemetry row per second
    /// (Master Plan sections 7.3.3, 8.8, 11.2). Reads the engine; the arithmetic is in PerfProbeCore.</summary>
    public sealed class AtlasPerfProbe : IPerfSource, IAfterFrameStep, IDisposable
    {
        public const int RingFrames = 3600;        // one minute at 60 fps
        public const int LadderFrames = 600;       // the window of IPerfSource
        public const int MinFramesForData = 120;

        public AtlasPerfProbe(LinkPump pump = null);
        public PerfProbeCore Core { get; }
        public IQualityState Quality { get; set; } // found through AtlasServices when null
        public byte AssetsPlaced { get; set; }     // written by the asset client's wiring
        public byte AssetsPending { get; set; }
        public ushort VisibleTiles { get; set; }   // written by the tile renderer; 0 until there is one

        // IPerfSource (lane A section 9.5): what the thermal ladder reads
        public bool HasData { get; }               // false until 120 frames have been sampled
        public float GpuP95Ms10s { get; }          // GPU frame time, p95 of the last 600 frames; refreshed once per second
        public float FrameP95Ms10s { get; }        // frame time, p95 of the last 600 frames
        public int ThermalState { get; }           // what the ladder last published, 0..3

        public int SampleCount { get; }            // frames in the ring, 0 .. 3600
        public bool TryGetLastRow(out TelemetryMessage row);
        public event Action<TelemetryMessage> RowReady;     // once per second, at step 8b

        public void AfterFrame(in FrameContext frame);
        public void ClearRing();                   // also done by itself when the space changes (LinkPump.SpaceChanged)
        public void ResetSession();                // empties the ring and restarts the row counter
        public void Dispose();
    }

    /// <summary>The engine-free arithmetic: AddFrame(timeSeconds, in PerfSample) returns true once per second;
    /// BuildRow(timeUs, in PerfRowContext, out TelemetryMessage) makes the row and numbers it.</summary>
    public sealed class PerfProbeCore { /* PerfSample, PerfRowContext beside it */ }

    /// <summary>Fixed arrays, percentiles on a scratch copy, no allocation after construction.</summary>
    public sealed class PerfRing
    {
        public const int ChannelFrame = 0;
        public const int ChannelCpuMain = 1;
        public const int ChannelCpuRender = 2;
        public const int ChannelGpu = 3;
        public const int ChannelCount = 4;
        public PerfRing(int capacity);
        public int Capacity { get; }
        public int Count { get; }
        public void Clear();
        public void Add(float frameMs, float cpuMainMs, float cpuRenderMs, float gpuMs);
        public float Recent(int channel, int age);
        /// <summary>Nearest rank over the last `window` samples (0 = all): the value at index ceil(q * n) - 1 of the
        /// sorted samples. 0 when there is no sample.</summary>
        public float Percentile(int channel, float q, int window = 0);
        public float ShareOver(float thresholdMs, int window = 0);     // percent of frame-time samples above it
        public float Mean(int channel, int window = 0);
        public float Max(int channel, int window = 0);
    }
}
```

**The thermal ladder's inputs.** `QualityLadder` (lane A) reads GPU p95 and frame p95 through `IPerfSource`,
which the probe publishes. It reads the thermal state itself (`AtlasThermal.State`, every 2 s) and publishes it
as `IQualityState.ThermalState`; the probe takes thermal state, tier, render scale and MSAA for the `Telemetry`
row from `IQualityState`. `Atlas.Telemetry` and `Atlas.Rendering` never reference each other.

| Value | Source |
|---|---|
| Frame time, CPU main, CPU render, GPU | When `FrameTimingManager.IsFeatureEnabled()`: `CaptureFrameTimings()`, then `GetLatestTimings(1, timings)`: `cpuFrameTime`, `cpuMainThreadFrameTime`, `cpuRenderThreadFrameTime`, `gpuFrameTime`, in ms. The values are a few frames late. When no timing is returned (Editor, feature off) the frame time is `FrameContext.DeltaTime` and the other three are 0 |
| Draw calls, set-pass calls, triangles | `ProfilerRecorder.StartNew(ProfilerCategory.Render, name)` with `"Draw Calls Count"`, `"SetPass Calls Count"`, `"Triangles Count"`; `LastValue` |
| Memory | `ProfilerCategory.Memory`, `"System Used Memory"`, in MiB |
| Managed allocation | `ProfilerCategory.Memory`, `"GC Allocated In Frame"` |
| Tier, thermal, render scale, MSAA | `IQualityState` (lane A §9.5) |
| Tracking | `FrameContext.Tracking`, coded by lane C's `TrackingStates.ToWire` |
| Battery | `SystemInfo.batteryLevel` × 100; 255 when it is negative |
| RTT, loss, `State` rate | `LinkClient`, through `LinkPump.TakeSecond` |
| Scene, space, highest bump level, cue count, Glued copies | The `State` of each frame, through `LinkPump`. The cue count rises with every rise of `bumpLevel` and with every rising edge of the cue flag that comes without one (an operator cue) |
| Assets placed and pending | `AssetClient.PlacedCount`, `PendingCount`, written to the probe once per frame |
| Visible tiles | The tiles the generated world's renderer drew (`ITileDrawStats`, S2 at q = 5), copied at step 6 by `TileStatsUploadStep`; 0 elsewhere |

- Percentiles of the row (`p50`, `p95`, `p99`, `over17_2`, `over33_8`, the three `…P95`) are over the whole ring.
  `fps`, `max`, the three counters and `gcAllocB` are over the last second.
- The ring is cleared when `State.spaceId` changes, so a row never mixes two spaces (§11.2: "per space").
  `HasData` is then false for 120 frames, and the ladder steps on the thermal state alone.
- A counter whose source is not built reads 0 (`P1-D04`).
- The probe allocates nothing after construction. Sorting 3,600 samples once per second is its only work of note.

## 5.3 The telemetry row and the CSV

One row per second while a frame loop runs. `telemetry.mode` decides where it goes: `live` to the Hub and the
phone's file; `phoneCsv` to the file only; `hud` nowhere.

The CSV: UTF-8, no byte-order mark, `\n` line ends, no quoting, no trailing comma. The header, exactly:

```text
t_us,seq,sceneId,spaceId,tier,fps,p50,p95,p99,max,over17_2,over33_8,cpuMainP95,cpuRenderP95,gpuP95,renderScale,drawCalls,setPass,trisK,memMB,visibleTiles,copies,rttMs,lossPermille,cueCount,gcAllocB,thermal,battery,tracking,msaa,stateHz,bumpMax,assetsPlaced,assetsPending
```

34 columns: the fields of `Telemetry` (§2.4) in wire order, with their wire names. Integers are written in
decimal. Every `f32` is written with exactly three decimals, rounded half away from zero from the value as a
`double` (`60.000`, `16.667`, `0.800`). `TelemetryCsv` in `Atlas.Link` does the formatting with its own
rounding, so the phone's file and the Hub's file are the same bytes (test L11) **(decided here)**.

```csharp
// File: ATLAS/Assets/Atlas/Link/Messages/TelemetryCsv.cs
namespace Atlas.Link
{
    public static class TelemetryCsv
    {
        public const string Header = TelemetryMessage.CsvHeader;               // the line above
        public const int ColumnCount = 34;
        public const int MaxRowLength = 384;
        public static void AppendHeader(System.Text.StringBuilder sb);         // with its \n
        public static void AppendRow(System.Text.StringBuilder sb, in TelemetryMessage row);   // one line with its \n
        public static void AppendInt(System.Text.StringBuilder sb, long value);
        public static void AppendFixed3(System.Text.StringBuilder sb, float value);
    }
}
```

`Atlas.Telemetry.TelemetryCsv` (`Telemetry/Perf/TelemetryCsv.cs`) forwards to it and adds
`AppendFixed(sb, value, decimals)` for the HUD.

Example row:

```text
1234567890,42,1,1,0,60.000,16.667,16.900,17.400,18.250,0.500,0.000,4.100,2.900,4.600,0.800,142,22,231,612,0,25,3,0,1,0,0,87,0,4,60,1,3,1
```

- The probe numbers the rows (`seq` 0 at session start). The row goes out as numbered
  (`LinkClient.SendTelemetryRow`), so the Hub recognises one it already holds.
- A row the phone could not send stays in a ring of 600 rows and is sent after the next handshake, so the Hub's
  CSV has no hole for an outage of up to ten minutes (§7.3.3: "reliable, so the CSV has no holes").
- §12.2, "CSV on the phone, uploaded over HTTP later": `PUT /logs/{session}/perf.csv` on the Hub (§3.3). The
  app does not make that request itself in this build; the file is taken off the phone (C.5) and uploaded
  from the Station.

## 5.4 HUD and link dot

Both hang under `boot.Hud` (lane A §8.1), on its layer (`AtlasHud`), so they are drawn in both eyes after the
world and are not faded with it. Text is TextMeshPro (§8.1), with the project's default TMP font asset; a
build without the TMP resources has no HUD and runs on. Together at most 4 draw calls per frame of the 20 the
layer may use (lane A §12.1).

| Item | Rule |
|---|---|
| HUD panel | One TextMeshPro object, 1.0 m ahead, 14° below the view axis, 0.45 m wide, five lines of 0.02 m. Shown while `hud.visible` is on, or `telemetry.mode` is `hud` |
| Refresh | Every 0.5 s, from the last `Telemetry` row, into one cached `StringBuilder` (`HudText.Build`, engine-free), then `TMP_Text.SetText(StringBuilder)`: no allocation. Cost under 0.2 ms (`P1-D04`) |
| Line 1 | `60 fps  p95 16.9  tier 0  therm 0` |
| Line 2 | `cpu 4.1 / 2.9  gpu 4.6 ms` |
| Line 3 | `draws 142  sp 22  tris 231k` |
| Line 4 | `mem 612 MB  bat 87  gc 0` (`bat -` when unknown) |
| Line 5 | `S1  link 3 ms 0.0%  bump 0  assets 3/1` (space; RTT and loss, prefixed `resync` or `degraded`, or `offline`; the Bump Guard level; placed / pending) |
| "Session over" | On tier `Critical` the panel is shown whatever `hud.visible` says, with a sixth line `Session over` (lane A §9.5) |
| Link dot | The character `•`, TextMeshPro, 1.0 m ahead, 22° below the view axis, centred. Green `Connected`, amber `Degraded` or `Resync`, grey `Offline` (three colours fixed in `AtlasHud`). Refreshed every 0.5 s. **Hidden while `Connected` under `demoLock`** (§7.2) and while `link.enabled` is off |
| In the hand | No HUD and no dot: the head-locked layer is not drawn in `DisplayMode.Hand`. The link panel of the onboarding screen is §5.6 |
| Mini-map | §5.9, while `hud.map` is on |

## 5.5 Session, event log and files on the phone

**Session.** A session id is made at app start and by `AtlasTelemetry.BeginSession()` (`SessionId.New()`):
`yyyyMMddTHHmmssZ-xxxx`, UTC time and four random hexadecimal digits, for example `20261004T153012Z-a3f9`. It
names the phone's files and, through `Hello`, the Hub's folder. `BeginSession` opens new files (lines still
waiting go to the old ones first), empties the probe's ring, restarts `seq` of `Telemetry` at 0, drops the
rows still waiting for the link, says `Hello` again and logs `session`.

**Files** (README F8: both logs under `logs/`).
(differs from plan §7.3.3: the plan puts the CSV in `persistentDataPath/perf/`; the code follows README F8 and
keeps one folder.)

| File | Content | Written |
|---|---|---|
| `<persistentDataPath>/logs/perf_<session>.csv` | §5.3, header first | One line per second |
| `<persistentDataPath>/logs/events_<session>.jsonl` | One event per line: the `Event` body of §2.6 | When an event is logged |
| `<persistentDataPath>/logs/phone_<session>.log` | `<t_us> <level> <msg>`: the lines that go out as `Log` | When a warning or an error is raised |
| `<persistentDataPath>/assets/<sha256>.glb` | The GLB cache (§4.3) | By the asset client's worker |

- `LogFileWriter` (`Telemetry/Files/LogFileWriter.cs`) owns the three log files (`Perf`, `Events`, `Phone`).
  The main thread hands it finished lines, which are copied into a preallocated buffer; one background thread
  appends and flushes once per second, and at pause and quit. A lost link loses nothing (§7.3.3). More than a
  megabyte waiting is dropped and counted (`Dropped`); a failed write is counted (`Errors`) and never takes
  the app down.
- `Application.persistentDataPath` is read once on the main thread and cached (it is not thread-safe, U-net §6).
- `UIFileSharingEnabled` makes the folder readable from the Mac (C.5).
- No camera frame and no crop is ever written (rule 10).
- The pose logs and hand logs that `pose.record` and `hands.record` start (`walk_<UTC time>.poselog`,
  `hands_<UTC time>.handlog`) go to the same folder. They are not named by the session and `LogFileWriter` does
  not touch them (`recordings/README.md`).
- §13.5: `LogFileWriter.DeleteSession(sessionId)` removes the three log files of a session;
  `AssetClient.ForgetRoom` removes a room's GLBs from the cache, the light copies as well as the full ones.

**Events.** §2.6 has the format, the names and `IEventLog`. The tracking, jump and bump events are taken from
`State` at step 8a (§5.1), so they need no reference to `Atlas.Safety`. Lane C's and lane A's own events are
logged by their code through `IEventLog`; lane D has no tap on the scan session, the room content or the input.

## 5.6 Pairing

`http://<station>:8080/pair` shows a QR code. The iOS Camera app reads it and opens the app through the URL
scheme `atlas` (written to Info.plist by lane A's post-build step, C.5).

| Topic | Rule |
|---|---|
| QR content | Exactly `atlas://<ip>:9050`, for example `atlas://192.168.0.10:9050`. `<ip>` is a dotted IPv4 address |
| Deep link | At start `Application.absoluteURL`, and afterwards the event `Application.deepLinkActivated` (U-net §6): both go to `TrySetAddress` |
| Accepted text | `atlas://<ip>[:port]`, `<ip>:<port>` or `<ip>`, with white space round it ignored. Port 1 … 65535, default 9050. Anything after the port is ignored. A host name is refused: LiteNetLib would resolve it on the calling thread (U-net §3.3). Parsed by `PairingAddress.TryParse`, not by `System.Uri` |
| Stored | `IPairingStore` (`PlayerPrefs` on the phone) key **`atlas.hub.address`** = `"<ip>:<port>"`; key `atlas.hub.paired` = 1 once the link has been `Connected`. `Save()` after each write |
| Default | With no stored address: `192.168.0.10:9050`, the Station's reserved address (C.7). The third number is the router's: open for the lead. A default address that connected is stored |
| Fallback | A typed address on the onboarding screen ("Change IP") |
| Local Network prompt | iOS raises it at the first UDP send to a local address. There is no API to ask for it or to read the answer. `BeginPairing()` makes that send: in the foreground, phone in hand, at onboarding step 1 (lane C §10.4). `NSLocalNetworkUsageDescription` is in the Info.plist (C.5) |
| Retry | While the phase is `Trying` or `Hint`, `LinkPump` makes one connect attempt every 1 s (C.7, R4 §3.3), because iOS drops the request while the prompt is up. After 15 s without a connection the phase is `Hint`: the screen shows "Settings › Privacy & Security › Local Network › ATLAS" and "Continue without Station". After 60 s it is `TimedOut` and the one-second attempts stop |
| Afterwards | The back-off of §2.8. On a later launch the link starts at boot, with no prompt (test L1) |
| After a delete and reinstall | The permission and the stored address are gone: pair again (C.7) |

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Pairing/PairingController.cs, PairingAddress.cs, PlayerPrefsPairingStore.cs
using System;
using Atlas.Link;

namespace Atlas.Telemetry
{
    public enum PairingPhase : byte { Idle = 0, Trying = 1, Connected = 2, Hint = 3, TimedOut = 4, Skipped = 5 }

    /// <summary>The link as the in-hand link panel shows it.</summary>
    public struct LinkStatus
    {
        public LinkState State;
        public string Address;                         // "<ip>:<port>" of the Station
        public int RttMs;
        public int LossPermille;
        public string LastError;                       // the reason of the Hub's last Bye, or null
    }

    public interface IPairingStore
    {
        string GetString(string key, string fallback);
        void SetString(string key, string value);
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        bool HasKey(string key);
        void Save();
    }
    // PlayerPrefsPairingStore on the phone; MemoryPairingStore for tests.

    public static class PairingAddress
    {
        public const string Scheme = "atlas://";
        public const int DefaultPort = LinkProtocol.DefaultPort;
        public static bool TryParse(string text, out string host, out int port);
        public static string Format(string host, int port);
    }

    public sealed class PairingController
    {
        public const string AddressKey = "atlas.hub.address";
        public const string PairedKey = "atlas.hub.paired";
        public const string DefaultAddress = "192.168.0.10";
        public const float RetrySeconds = 1f, HintAfterSeconds = 15f, GiveUpAfterSeconds = 60f;

        public PairingController(IPairingStore store, LinkPump pump = null);
        public string Address { get; }                 // "192.168.0.10:9050"
        public string Host { get; }
        public int Port { get; }
        public bool HasStoredAddress { get; }
        public bool WasPaired { get; }
        public PairingPhase Phase { get; }
        public float SecondsTrying { get; }
        public bool ForgeReachable { get; set; }       // written by the asset client
        public event Action<PairingPhase> PhaseChanged;
        public event Action<string> AddressChanged;    // a new address was stored

        /// <summary>True for the three accepted forms; host is a dotted IPv4 address.</summary>
        public static bool TryParse(string text, out string host, out int port);
        /// <summary>Parses, stores and reconnects. False, and nothing changes, when the text is not accepted.</summary>
        public bool TrySetAddress(string text);
        /// <summary>Onboarding step 1: starts the link if needed and tries every second. Raises the Local Network prompt.</summary>
        public void BeginPairing();
        /// <summary>"Continue without Station": stops the one-second attempts. The link keeps its normal back-off.</summary>
        public void Skip();
        public void Tick(long nowUs, LinkState link);  // called by LinkPump once per frame
        /// <summary>For the link panel: state, address, RTT, loss, last error, and whether the Forge answers.</summary>
        public void GetStatus(out LinkStatus link, out bool forgeReachable);
    }
}
```

Onboarding (lane C's files) draws the pairing step and the in-hand link panel from this class: a state dot
(green, amber, grey), the Hub address, RTT, loss, Forge reachability, and the buttons "Reconnect"
(`AtlasTelemetry.Pump.ReconnectNow()`) and "Change IP" (`TrySetAddress`) (§7.2). Lane D draws nothing in the
hand.

## 5.7 The on-device menu

`Atlas.App.Menu` (lane D's files in `Atlas.App`; `P1-D13`). An in-headset menu of four pages, driven by the
same registry. It needs no link: every change is `registry.Set(entry, value, ParamSource.Local)`, which is
clamped there, written to the phone's event log (`param.set` with `src` `menu`, then the `param` echo of
§2.6) and sent to the Station when the link is up (tests L22, G6).

```csharp
// Files: ATLAS/Assets/Atlas/App/Menu/MenuInstaller.cs, OnDeviceMenu.cs, MenuModel.cs, MenuLayout.cs, MenuText.cs, MenuView.cs
namespace Atlas.App.Menu
{
    public static class MenuInstaller
    {
        // [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] puts Create into AppHooks.Menu; the composition root
        // creates and installs the menu at row 19 of its creation order. The Spectator never runs the hook.
        public static IAppMenu Create();
    }

    /// <summary>The thin adapter: reads the input service, feeds the model, places the panel and its gaze targets.</summary>
    public sealed class OnDeviceMenu : IAppMenu, Atlas.World.IAfterFrameStep, Atlas.World.IUploadStep, IMenuActions
    {
        public const float StatusSeconds = 0.5f;
        public bool IsOpen { get; }
        public MenuModel Model { get; }                // null before Install
        public event System.Action RescanRequested;    // "Rescan the room", after the menu has closed
        public void Install(AtlasApp app);
        public void Shutdown();
        public void Open();
        public void OpenOnSpaces();                    // the space-menu action
        public void Close();
        public void AfterFrame(in Atlas.World.FrameContext frame);   // step 8b: input
        public void Upload(in Atlas.World.FrameContext frame);       // step 6: where the menu stands
        public void Recentre();
        public void Rescan();
    }

    public enum MenuPage : byte { Spaces = 0, Quality = 1, Parameters = 2, Session = 3 }
    public enum MenuRowKind : byte { Page = 0, Heading = 1, Info = 2, Param = 3, Space = 4, Command = 5 }
    public enum MenuCommand : byte { None = 0, ReturnHome = 1, ResetSwitches = 2, Recentre = 3, Rescan = 4, Close = 5 }
    public interface IMenuActions { void Recentre(); void Rescan(); }

    /// <summary>The engine-free core of the menu: pages, rows, focus, step sizes, pending values. Tested headless.</summary>
    public sealed class MenuModel
    {
        public const int VisibleRows = 8;
        public const int PageCount = 4;
        public const float FloatStepFraction = 1f / 20f;
        public const string EventMenu = "menu";
        public const string EventParamSet = "param.set";
        public static readonly string[] KillSwitchIds;     // the 22 names of 1.5, in order

        public MenuModel(Atlas.Link.ParamRegistry registry, Atlas.World.ISpaceHost spaces = null, IMenuActions actions = null);
        public Atlas.World.IEventLog Events { get; set; }
        public MenuPage Page { get; }
        public int RowCount { get; }
        public MenuRow Row(int index);
        public int Selected { get; }                   // the focused row
        public int Top { get; }                        // the first visible row
        public int Revision { get; }                   // goes up whenever what the view shows may have changed
        public bool CloseRequested { get; }
        public bool HasPending { get; }
        public float PendingValue { get; }
        public event System.Action<Atlas.World.SpaceId> SpaceRequested;

        public void NoteOpened();
        public void NoteClosed();
        public void Sync();                            // once per frame while open: rebuilds when the manifest or the space changed
        public void SetStatus(in MenuStatus status);
        public void Rebuild();
        public void SetPage(MenuPage page);
        public void Move(int delta);                   // focus up or down by that many focusable rows, stopping at the ends
        public void Select(int row);                   // a gaze dwell on a row
        public static float StepSize(Atlas.Link.ParamEntry entry);
        public static float Stepped(Atlas.Link.ParamEntry entry, float from, int direction);
        public void Step(int direction);               // -1 or +1 on the focused row
        public void Confirm();                         // applies a pending value, toggles a bool, runs a space or command row
        public int ResetSwitches();                    // every kill switch but space.id back to its default
    }
}
```

| Topic | Rule |
|---|---|
| Pages | Row 0 of every page is the page row; stepping it changes the page. **Spaces**: one row per available space, then "Return to Home". **Quality**: the tier in force, then every kill switch of 1.5 that is declared, in that order, then "Reset switches to defaults". **Parameters**: every parameter under its group heading, the entered space's group first. **Session**: "Recentre", "Rescan the room", link state and RTT, Station address, session id, room id, version, "Close the menu" |
| Row | Name and value (the option's name for an `Enum`, the number otherwise). Headings and status lines are skipped by the focus. An inactive group is marked "(inactive)" in its heading; its rows can be changed |
| A step | `Float`: ± 1/20 of the range, on the grid that starts at `min`. `Int`: ± 1. `Bool`: toggles. `Enum`: the next option, no wrap. A space or command row runs on "+" |
| Applying | At once, except a parameter with `OnRelease` (`space.id`, `glued.mode`, …): its row shows the pending value and `Confirm` applies it. That is the menu's "release". `Confirm` on a `Bool` toggles it |
| Reset | "Reset switches to defaults" resets the kill switches except `space.id`, which would walk the wearer out of the space they stand in; `hands.enabled` goes back to off with the others. It does not touch the rest of the registry |
| Input | The service `IAtlasInput`, taken from the app at `Install` (lane C §13.3) |
| Open and close | Lane C's action `AtlasAction.ParamMenu`: the gamepad chord `start` while `select` is held, F1 in the Editor (lane C §13.1). `ParamMenu` again or `AtlasAction.Back` closes. On open `IAtlasInput.PushContext(InputContext.Menu)`, on close `PopContext`. A space switch that is accepted, "Return to Home" and "Rescan" close the menu; so does leaving `DisplayMode.Headset` |
| Gamepad | `NavUp`, `NavDown` move the focus; `NavLeft`, `NavRight` step the row; `Confirm` applies; `Back` closes. Read with `IAtlasInput.Pressed` |
| Gaze dwell (rule 5) | While a space runs: one chip for `ParamMenu`, down and to the left at (−0.55, −0.40, 1.0) m in the menu frame, radius 0.06 m (proposed). While open: each visible focusable row is a target that selects it (`NavDown` with the row's slot + 1 in `Tag`); the focused row has a "−" and a "+" target (`NavLeft`, `NavRight`, `GazeMode.Repeat`); one chip each for `Confirm` ("apply") and `Back` ("close"). All registered with `IAtlasInput.RegisterGazeTarget` in the room frame, context `Menu`, and moved with the head's position |
| Where it stands | The gaze ray is the head's forward axis, so a panel that turned with the head could never be looked at off its centre. The menu follows the head's **position** and keeps its own heading, level (`MenuFrame`). Closed: the heading trails the walker's at 90°/s, never by more than 40°, and holds still while they look down past −8° or the gaze is on the chip. Open: the heading is fixed, and is brought round when the walker has turned 75° away |
| Panel | Under `boot.Hud`, 1.2 m ahead, 0.70 × 0.50 m: one mesh of flat quads (`Atlas/Surface`, pooled material) for the panel, the focused row and the chips, and text objects. Eight rows visible; the list scrolls with the focus. Six draw calls while open, two while closed. Text is legacy `TextMesh` with the built-in font, not TextMeshPro (open item 6) |
| In a hands session | The panel and the chip come within reach of the hands. They stand at their slots about the near anchor (`ReachSlot.MenuPanel` and `MenuChip`; lane A §10.9, lane C §7.7), their layout times `IReachLayout.Scale` (0.75: the panel 0.525 × 0.375 m, a row 3 cm tall), on the world layer 0 instead of `AtlasHud`, with their text on `Atlas/NearText` so that a finger in front of a row covers the row and its text. Their gaze targets stand with them, none under 3 cm across, and are turned in place while the anchor turns, so a dwell or a press that is under way survives it. A menu that opens within reach opens at once, at the anchor. With no layout, or one that says far (the hands are off, a gamepad is connected, the display is not the headset), the menu is exactly as the rows above say; it also goes back, and closes, when the display leaves the headset. When the hands stop for heat the panels stay near and work by gaze |
| The world | Keeps running. Bump Guard and its cue are unaffected and are drawn over the menu's layer order by lane C |
| Logging | `menu` event `{state}` on open and close; `param.set` `{name, value, src: "menu"}` per change |
| Cost | Closed: one action read at step 8b, one target and one transform moved at step 6. Open: text is rebuilt only when `Revision` moved; the status lines are read every 0.5 s |

## 5.8 The Spectator

The same Unity project built for Windows with `ATLAS_SPECTATOR`, or the Editor playing `PC_Spectator` (or any
scene with a root object named `AtlasSpectatorBoot`). It joins the Hub as role `spectator`, receives the
phone's `State`, and redraws the walker's view with one mono camera. No video is sent (§7.3.1). It never
integrates motion and never guesses a world. The Hub's address is `-atlasHub <ip>[:port]` on the command
line, default `127.0.0.1:9050`.

**What it consumes.**

| From the link | Used for |
|---|---|
| `Welcome` | `forgeUrl`, the phone's `roomId` and `worldHash` |
| `State`, the newest only | Head pose, space, tile id, sheet, tile-local transform, `p[]`, flags, bump level, `worldVer` |
| `ParamManifest`, `param` events | Its own registry: each value it knows by name is set with `ParamSource.Remote`; `active` sets the group state. Names that start with `link.` or `telemetry.` are the phone's own and are left alone |
| `world` event | Room id, world hash and geometry hash to compare with its own |
| `RoomOutline` | The floor plan while no RoomModel is held, and one of the three things the fetched room is checked against |
| `asset` events, `JobStatus`, `AssetReady` | The same furniture (`SpectatorAssetsCore`): the generated meshes by their hash, and a first shape for every object as soon as its room content is built, made by the Spectator itself from the RoomModel it fetched, profile included (§4.4); the `builtin` events with reason `first_shape` are not news to it, the other `builtin` events are. It shows the **full copy** everywhere: it ignores the light members of `AssetReady`, downloads no light copy and sets no furniture level. It uploads nothing and sends no `AssetAck`. The same 32 objects as the phone get meshes; the rest keep their first shape |

From the Forge: `GET /rooms/{roomId}` for the RoomModel (a fixture room is loaded from `Resources` instead),
`GET /jobs?room=` and `GET /assets/{sha256}.glb`.

**How it is put together.** `Atlas.Spectator` may not be referenced by `Atlas.App`, and `Atlas.App` does not
boot in a Station build, so the Spectator composes its own root
(differs from lane A §8.2, which described the same bootstrap running with `IsSpectator`):
`SpectatorBoot`, a `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` method, calls
`SpectatorApp.Create(options)`.

| The phone has | The Spectator has |
|---|---|
| Stereo rig, eye textures, lens blit | `Atlas.Spectator.SpectatorView`: a mirror `Camera` on the left 72 % of the window, vertical field of view 60° (proposed), near 0.05 m, far 60 m, MSAA allowed, no lens blit; and a second camera for the panels beside it, on layer `AtlasSpectator` (lane A §12.1). `LeftEyePosition` and `RightEyePosition` both equal the head position |
| `AtlasFrameLoop` | `SpectatorLoop : IFrameLoop` (`Telemetry/Map/SpectatorLoop.cs`), run by `SpectatorView`: the same steps, with the `State` in place of the sensors and of the kernel's integration. Guard, pose-filter and state-sink hooks are accepted and unused |
| AR session, pose pipeline, scan | `SpectatorSense : ISenseStep`: fills `RawHeadPosition`, `RawHeadRotation`, `Tracking`, `Tier` and the pose-jump flag from the newest `State`. No pose filter: the geometry pose is the raw pose |
| SpaceLab | `SpectatorHost : ISpaceHost`: enters the space `SpectatorCore` names, with no fade and no scene load; modules come from `SpaceRegistry` |
| Bump Guard | Nothing. A coloured bar shows `State.bumpLevel` |
| `CommandRouter`, input, menu, onboarding | Nothing. A spectator receives no command |
| `AssetClient` | `SpectatorFurniture` (`Atlas.Spectator`) round `SpectatorAssetsCore`; its `PlaceGeneratedLight` places nothing and returns false |
| `TelemetryInstaller.Install(boot)` | The same call, with `boot.IsSpectator`: link client of role `spectator`, event log with no files, probe, no mini-map of its own. Pairing is taken off the link driver; the link dials the address of the options |

`SpectatorCore` (`Telemetry/Map/SpectatorCore.cs`, engine-free) maps what the link delivers to what is shown:
`Attach(LinkClient)`, `OnWelcome`, `OnState`, `OnManifest`, `OnEvent`, `OnRoomOutline`, `Step(double now, bool
linkUp)`, and `Banner`, `State`, `Room`, `Outline`, `Trail`, `WorldVerified`, `StateHz`.

**Each frame.**

1. `SpectatorSense` takes the newest `State`. With none yet, it leaves the frame at the origin.
2. Room: when the phone names a room the Spectator does not hold, `GET /rooms/{roomId}` →
   `RoomModelJson.TryRead` → `ISpaceHost.SetRoom(room)`; room content builds on `RoomChanged`, as on the phone
   (lane C §14.2).
3. World check: the fetched room must agree with everything the phone has said about its world: the geometry
   hash and the world hash (`WorldHash.Compute(…)` with its own kernel) of the last `world` event or
   `Welcome`, and the floor of the `RoomOutline` to 2 mm. **On a mismatch it shows "world mismatch", enters
   no space, and fetches the room again every 2 s** (§7.3.1, test L8). The entered module's own generator is
   checked too: when it is an `ISpaceWorld`, its seed, its version and its `ContentHash` must be the `seed`,
   `genVer` and `genHash` of the phone's `world` event. A difference that lasts 0.5 s leaves the space, shows
   "world mismatch" and is tried again every 2 s or on the next `world` event.
4. Space: when `State.spaceId` differs from `ISpaceHost.Current`, `RequestSwitch`. With `spaceId` 255 it shows
   "no space" and the room only; a space that is not in the build shows "space not in this build".
5. Kernel and module: the kernel is a `HolonomyWalkerKernel`, as on the phone, and `SpectatorLoop` sets it from
   each new `State` without ever stepping it, so nothing is integrated. In S2 it is put on the tiling of the
   `world` event's `q` (without one, `p[3]`); with `p[4]` = 0 the walker stands flat on tile zero
   (`ResetToFloorFlat` at the translation of `xform`), otherwise the scale is `p[0]`
   (`SetCurvatureTarget(-1, p[0])`) and the pose is `Reset({tileId, Iso2.FromRowMajor(xform, -1)})`, which
   follows a ramp frame by frame. In S4 it is `Curved(3, p[0])` and `Reset` with κ = +1. In every other
   space, S3 included, it is the flat kernel of the room with the walker on the floor point under the head.
   `CullRho` is `p[2]`. A `State` that cannot be placed leaves the last good pose. Then, if the entered
   module implements `ISpaceStateReader`, `ReadState(in state)` (lane A §2): S2, S3 and S4 all do, and
   from the first `State` on they read the kernel or the `State` and drive nothing.
6. The frame then runs as on the phone: `Tick`, upload, render. `SpectatorView` puts its camera at the
   frame's raw head pose in `LateUpdate`, and polls the link after the frame is rendered.

No interpolation and no prediction in Phase 1 **(decided here)**: the picture is the newest `State`. Target: the
mirror lags the phone by at most 150 ms (test L5); tile ids equal on 100 % of samples (L7).

**Beside the mirror** (§7.3.1): the flat-twin map (§5.9), which draws the room from the RoomModel or, before
one is held, from the `RoomOutline`, with the walker's physical position and the object boxes; the Bump Guard
level as a coloured bar (0 clear, 1 warn, 2 strong, 3 tracking lost); link state, `State` rate and room; and
one banner (`SpectatorBanner`): "no hub", "no phone" (no `State` for 2 s), "no room", "fetching room", "world
mismatch", "no space", "space not in this build". While no room is held the phone's outline is also laid on
the mirror's floor.

## 5.9 The flat-twin mini-map

Rule 6 of §0.5: every curved or glued view has a flat twin. One renderer for the headset and the Station
(`P1-D07`): an engine-free draw list in `Atlas.Telemetry`, meshed by `FlatTwinMesher` and shown by
`FlatTwinPanel` (`Telemetry/Map/FlatTwinPanel.cs`): on the phone by `FlatTwinHud` (under `boot.Hud`, 15° right
of and 13° below the view axis, 0.25 m across at 1.0 m, while `hud.map` is on) and on the Station by
`SpectatorView`.

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Map/FlatTwin.cs, FlatTwinBuilder.cs, FlatTwinHud.cs
using System;

namespace Atlas.Telemetry
{
    public enum FlatTwinInk : byte
    {
        Outline = 0, Grid = 1, Cell = 2, Highlight = 3, Object = 4, Walker = 5, Trail = 6, Portal = 7,
        Good = 8, Warn = 9, Alert = 10, Muted = 11,       // the Station's indicators
    }
    public enum FlatTwinShape : byte { Line = 0, Dot = 1, Arrow = 2, Bar = 3 }
    public struct FlatTwinItem { public FlatTwinShape Shape; public FlatTwinInk Ink; public float A, B, C, D; }

    /// <summary>A retained list of lines, dots, arrows, bars and labels in map units: +X to the right, +Z up the
    /// page. Engine-free. Clearing and refilling it allocates nothing once it has grown.</summary>
    public sealed class FlatTwin
    {
        public const int MaxLabels = 12;
        public int Count { get; }
        public int LabelCount { get; }
        public float CentreX { get; }
        public float CentreZ { get; }
        public float HalfExtent { get; }
        public bool ClipToDisc { get; }
        public int Revision { get; }
        public void Clear();
        public void SetView(float centreX, float centreZ, float halfExtent, bool clipToDisc);
        public void FitView(float margin = 0.08f, bool clipToDisc = false);
        public void Line(float ax, float az, float bx, float bz, FlatTwinInk ink);
        public void Polygon(ReadOnlySpan<float> xz, FlatTwinInk ink);       // closed outline: x0, z0, x1, z1, …
        public void Rect(float minX, float minZ, float maxX, float maxZ, FlatTwinInk ink);
        public void Box(float cx, float cz, float width, float depth, float yaw, FlatTwinInk ink);
        public void Dot(float x, float z, float radius, FlatTwinInk ink);
        public void Arrow(float x, float z, float headingRadians, FlatTwinInk ink);
        public void Bar(float minX, float minZ, float maxX, float maxZ, FlatTwinInk ink);   // filled
        public void Label(float x, float z, string text);
        public ref readonly FlatTwinItem this[int index] { get; }
        public string GetLabel(int index, out float x, out float z);
    }

    /// <summary>Optional, on a space module: the space draws its own flat twin. Called at most 10 times per second.</summary>
    public interface IFlatTwinSource
    {
        void WriteFlatTwin(FlatTwin twin);
    }

    /// <summary>The built-in twins. Each needs only the RoomModel, the kernel and the newest State.</summary>
    public static class FlatTwinBuilder
    {
        public static void Build(FlatTwin twin, Atlas.World.RoomModel room, Atlas.Kernel.IWalkerKernel kernel,
                                 in Atlas.Link.StateMessage state, FlatTwinTrail trail);
        /// <summary>As above; while no RoomModel is held the room is drawn from the phone's RoomOutline.</summary>
        public static void Build(FlatTwin twin, Atlas.World.RoomModel room, Atlas.Link.RoomOutlineMessage outline,
                                 Atlas.Kernel.IWalkerKernel kernel, in Atlas.Link.StateMessage state, FlatTwinTrail trail);
        public static void Build(FlatTwin twin, IFlatTwinSource source);
        public static float Heading(in Atlas.Link.StateMessage state);     // radians from +Z towards +X
    }

    /// <summary>The last 600 walker positions at 10 Hz, and the last 64 tile ids, without repeats.</summary>
    public sealed class FlatTwinTrail
    {
        public const int MaxPositions = 600;
        public const int MaxTiles = 64;
        public int PositionCount { get; }
        public int TileCount { get; }
        public void GetPosition(int index, out float x, out float z);
        public Atlas.Kernel.TileId GetTile(int index);
        public void Add(in Atlas.Link.StateMessage state);
        public void Clear();
    }

    /// <summary>The in-headset map. Declares hud.map (off) and adds itself to step 8b.</summary>
    public sealed class FlatTwinHud : Atlas.World.IAfterFrameStep, IDisposable
    {
        public const string ParamMap = "hud.map";
        public const float PanelSize = 0.25f, Distance = 1.0f;
        public const float RebuildSeconds = 0.1f;
        public const float BudgetMs = 0.3f;
        public static FlatTwinHud Install(Atlas.World.AtlasBootContext boot, Atlas.World.IFrameLoop loop = null);
        public bool Visible { get; set; }
    }
}
```

| Space | Flat twin | Drawn from |
|---|---|---|
| S0 Home | Floor plan: outline, free rectangle, lap square, object boxes, the walker as an arrow, the trail | RoomModel; `State` head pose |
| S1 Glued | Lattice plan: n × n cells round the walker's cell, the fundamental cell highlighted, mirrored rows marked for the Klein bottle, the walker and its copies as dots | RoomModel `cells.glued`; `State.tileId`, `p[3]` (mode), `p[4]` (copies) |
| S2 Curved Walk | While `p[4]` = 0 (no curvature yet) the floor plan of S0. Then the Poincaré disc (`FlatTwinDisc`) centred on the walker's tile: the tiles within 3 units, the walked tile sequence, the room outline in tile zero at the scale `p[0]`, the walker at `xform` | Kernel graph and `Candidates` (lane B); `State.tileId`, `xform`, `p[0]`, `p[4]` |
| S3 Aligned Cells | The same disc round the tile of the walker's cell, the walker on its centre, no room. The kernel is flat in S3, so the builder keeps a {4,5} graph of its own | `State.tileId`, `xform` |
| S4 Small Planet | Unfolded cube net: six faces, the walker's face highlighted, the trail | `State.tileId` |
| S5 Impossible Rooms | Floor plan with "room k of n"; the room graph (rooms as nodes, portals as edges, the current room highlighted) when the space implements `IFlatTwinSource` | `State.tileId`, `p[4]`; the space |
| S6 Slicer | Floor plan, plus a strip that shows the cut w₀ on its range and the three angles | `State.p[3..6]` |
| Any space that implements `IFlatTwinSource` | Whatever it draws | The space |

**A kernel on the flat tiling in S2.** A graph with `Q == 4` (`FlatTileGraph`, κ = 0) has no disc. Should the
builder be handed one for S2 (a Station that could not place the `State`), it draws a **square-grid plan** in
metres: the cells of `Candidates`, the walker's cell highlighted, the trail of visited cells, and the room
outline over tile zero. The phone's own map fills `tileId`, `xform` and `p[0]` from the kernel, as the frame
loop does for the `State` it sends.

Rules: at most 0.3 ms on the phone (`P1-D07`); rebuilt at 10 Hz, not every frame; in replay the map shows the
same tile-id sequence as the log; the in-headset map is off by default, because a mini-map distracts (F.2: show
the flat twin late).

---

## 6 · What lane D needs from the other lanes

Names this page takes from the other pages. Where one of them is spelled differently there, that page wins and
this one is corrected.

| Name | Page | Used for |
|---|---|---|
| `AtlasBootContext` (`Params`, `Root`, `Systems`, `Hud`, `Head`, `RoomRoot`, `SpaceRoot`, `IsSpectator`, `IsDevice`), `AtlasServices` (`Replace`, `TryGet`, `Booted`, `MarkBooted`) | A §6, §8 | The installers (§5.1, §5.7, §5.8) |
| `AtlasApp` (`Registry`, `Spaces`, `Loop`, `Rig`, `Pool`, `InputService`, `Boot`, `Flow`), `AppHooks.Menu`, `IAppMenu` | A §8 | The menu (§5.7) |
| `IFrameLoop` (`SetStateSink`, `AddAfterFrame`, `AddUpload`, `AddSense`, `ResetWalker`, `ResetWalkerToFloor`, `Current`); `IStateSink`, `IAfterFrameStep`, `IUploadStep`, `ISenseStep` | A §7.1 | Step 8 (§5.1); the menu; the Spectator's loop |
| `FrameContext` (`RawHeadPosition`, `RawHeadRotation`, `Tracking`, `Tier`, `DeltaTime`, `Time`) | A §3 | Probe; HUD; asset client; Spectator |
| `ISpaceHost` (`Current`, `HasSpace`, `Module`, `Room`, `WorldVersion`, `IsAvailable`, `RequestSwitch`, `ReturnHome`, `SetRoom`, `SpaceEntered`, `RoomChanged`), `ISpaceModule`, `ISpaceStateReader`, `SpaceRegistry`, `SpaceId`, `SpaceIds` (`Code`, `ParamGroup`, `SceneId`, `NoSpace`, `NoScene`, `Count`) | A §1, §2, §4 | Events, `RoomOutline`, world hash, menu, Spectator |
| `IOperatorCommands` (`SwitchSpace`, `Home`, `Recentre`, `SetQuality`, `Cue`, `SetLock`) | A §7.1 | §2.9 |
| `IQualityState`, `IPerfSource`, `QualityTier` | A §9.5 | §5.2 |
| `AtlasVertex`, `AtlasShading`, `MeshData` (`Atlas.Kernel`) | A §10.2 | Built-in and generated meshes (§4.3, §4.4); the Forge's shade (§4.1); the map's mesh |
| Palette layout: the cell-centre formula, object colours 64 … 127 | A §10.7 | §4.1, §4.4 |
| Layers `AtlasHud` (through `boot.Hud`) and `AtlasSpectator` | A §12.1 | §5.4, §5.7, §5.8 |
| `IWalkerKernel`, `IDialKernel`, `WalkerPose`, `TileId`, `FlatWalkerKernel`, `HolonomyWalkerKernel`, `FlatTileGraph.TryGetCell`, `ISpaceWorld` | B §4.4, §4.5, §4.9 to §4.11; A §2 | World hash, Spectator, mini-map |
| `RoomModel` (`Id`, `Outline`, `Walls`, `FreeRect`, `Objects`, `DefaultWallHeight`), `RoomObject`, `MeshState` (`StandIn` among them), `ObjectCategory`, `ObjectCategories.Name`, `ObjectMeshRules` (`MaxMeshedObjects`, `Targets`, `LightTarget`), `RoomModelJson` (`TryRead`, `GeometryHash`), `RoomFixtures` | C §2, §6 | `RoomOutline`, world hash, asset client, Spectator |
| `IRoomContent` (`Built`, `IsBuilt`, `Room`, `SetObjectState`, `SetGeneratedMesh`, `SetGeneratedLightMesh`, `SetBuiltInMesh`, `FurnitureLevel`) | C §9.1 | §4.3, §4.4 |
| `IObjectPhotoSink`, `ObjectPhotoSet`, `ObjectView` | C §11.4 | §4.3 |
| `TrackingState`, `TrackingStates.ToWire`, `FromWire` | C §7.1 | `Telemetry.tracking`, the Spectator. The wire coding (0 normal, 1 limited, 2 lost) is not the enum's numbering |
| `IAtlasInput` (`Pressed`, `LastTag`, `PushContext`, `PopContext`, `RegisterGazeTarget`, `UnregisterGazeTarget`, `MoveGazeTarget`, `SetGazeTargetEnabled`, `Gaze`), `AtlasAction` (`ParamMenu`, `NavUp`, `NavDown`, `NavLeft`, `NavRight`, `Confirm`, `Back`), `InputContext.Menu`, `GazeTarget`, `GazeTargetId`, `GazeMode.Repeat` | C §13 | §5.7 |
| `BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator)` | C §8.8 | The end of the `cue` command, called by lane A's `CommandRouter`. Lane D never calls it |

README F1 carries what lane D asked for: `Atlas.Telemetry` references `Atlas.Kernel`, `Atlas.Link` and
`Atlas.World`; `IEventLog` lives in `Atlas.World`; `Atlas.App` and the spaces reference `Atlas.Link`.

---

## 7 · Tests

Engine-free tests run in the Unity Test Runner and under `tools/headless/verify.py` (README F2). The first three
are the rows of §11.1. Each row names the test class (the file of the same name).

| Test | Assembly | Pass condition |
|---|---|---|
| `Link_RoundTrip` (§11.1) | `Atlas.Link.Tests` | Every binary message packs to the documented bytes and reads back (the test holds its golden bytes inline). `State` is 202 B, reads at an offset, ignores trailing bytes and rejects a short or foreign packet; its flags pack into the documented bits. JSON messages give fixed bytes and tolerate missing and unknown members; an `AssetReady` carries its four light members only when `LightSha256` is not empty and reads as `""` and 0 without them. Every id of Appendix B is declared and `PeerState` has no codec. The six operator commands round-trip. Roles have fixed names |
| `Link_ClockSync` (§11.1) | `Atlas.Link.Tests` | `ClockSync` on a simulated link with loss, delay and jitter (proposed): offset error ≤ 2 ms at p95. A clean link gives the exact offset. The schedule is eight pings 50 ms apart, then one per second. Stale, duplicate and impossible pongs are rejected |
| `Link_Watchdog` (§11.1) | `Atlas.Link.Tests` | `LinkWatchdog`: `Degraded` at 500 ms of silence; `Connected` again only after 2,000 ms healthy; `Offline` at 3,000 ms; attempts after 0.5, 1, 2, 4, 4 s. With `LinkClient` and `LinkHost` on the loopback network: `ParamSet` is dropped while degraded; after a reconnect the snapshot `ParamSet` and the unacknowledged `AssetReady` arrive |
| `Link_Exchange` (`P1-D01`) | `Atlas.Link.Tests` | On `LoopbackNetwork`: `Hello`, `Welcome`, eight pings, manifest; `State` at 60 Hz with 2 % loss reaches spectators in order with gaps only; `SendState` and `SendTelemetry` allocate nothing; `StateHz` thins the stream; a value is clamped on the phone and echoed; the six commands arrive in order; the demo lock drops `ParamSet` but not commands; a dropped `ParamSet` is reported and the snapshot goes back; a `Hello` with another major version gets a readable `Bye`; a second phone is refused; unknown ids and trailing bytes are ignored |
| `Link_Spectator` | `Atlas.Link.Tests` | A spectator gets `Welcome`, the kept outline, the manifest with current values, the newest `world` event and `param` events; `ParamManifestMessage.ApplyTo` matches by name |
| `Link_Udp` | `Atlas.Link.Tests` | The same exchange over real LiteNetLib sockets on `127.0.0.1` |
| `ParamRegistryTests` | `Atlas.Link.Tests` | Declaration order is the wire id. Clamp rules of 1.1. Declaring twice returns the same entry and replaces range and callback; another kind throws. Callbacks and `Changed`. Snapshot and restore. Group state. The manifest JSON. More than 255 entries fill several `ParamSet`s |
| `AtlasJsonTests` | `Atlas.Link.Tests` | Objects, arrays, escapes, surrogate pairs, exact `i64`, the depth limit, trailing text with and without `allowTrailing`, the number rules of 1.8 |
| `Telemetry_Csv` | `Atlas.Link.Tests` | The header and the example row of §5.3, byte for byte |
| `WorldHash_Vector` | `Atlas.Link.Tests` | The vector of §2.5 gives `141060c2fc138145` |
| `GlbValidatorTests` | `Atlas.Telemetry.Tests` | The Forge-made fixtures in `Tests/Telemetry/Assets/Fixtures/` pass; the damaged ones each fail with the code of their row in §4.2 (`bad_too_many_triangles` has 18,000 triangles, over the full limit of 15,000). The triangle and file limits follow the level (1,500 to 15,000 and 2 MB full, 1,500 to 5,000 and 1 MB light), the job's target caps each level, and an expectation with no level is the full copy |
| `AssetClientCoreTests` | `Atlas.Telemetry.Tests` | The round trip of §4.3 against a scripted Forge: upload, job, download, hand-over, `AssetAck`; cache; unreachable Forge and its return; `off`; a loaded room; stand-in objects left alone. First shapes: every object has one as soon as the room content is built, with the Forge reachable too, and again when the content is built again. Budget: 32 objects at most, largest first, the other 8 of 40 keep their first shape; equal boxes share the triangles (five cubes 15,000 and 5,000; twelve boxes 10,000 and 5,000) and unequal boxes get their own targets, with the light target 1,875 (brief vector 3), each checked in the job's `meta`. `meta` carries the profile and the light target only when there are ones, and the phone sends and expects the object's own target. The light copy: fetched after the full one in order and handed over once; none when the message has no light fields; a wrong hash, a copy over its level's limit (5,001 triangles), over the room's light target (13 objects, 4,616 against 4,615), a failed download (after the usual retries) and a refusal by the room content each drop only the light copy and leave the full mesh; a room built again mid-download hands nothing stale over |
| `FittedShapesTests` | `Atlas.Telemetry.Tests` | Every category with a full profile builds within 1,500 triangles and 0.25 m; with no profile the template is unchanged; a fitted chair has its seat where the profile says and its back at the full height, clamped to the category's range; couch and bed are fitted too; the shape cut from test vector A is higher at the back than at the front, needs enough coverage, fills unseen cells from their neighbours and falls back to 4 × 4; a template too large for the box falls back to the scan-cut shape or the box |
| `FallbackSetTests` | `Atlas.Telemetry.Tests` | Each of the 21 files meets the template row of §4.4. Built for boxes of many sizes: at most 1,500 triangles and no edge over 0.25 m, or the builder reports the box too large |
| `PerfRingTests`, `PerfProbeCoreTests` | `Atlas.Telemetry.Tests` | Nearest-rank percentiles on known data; the 600-frame window; one row per second; no allocation after construction |
| `TelemetryCsvTests`, `HudTextTests` | `Atlas.Telemetry.Tests` | The phone's CSV equals the link library's; the five HUD lines |
| `PairingTests` | `Atlas.Telemetry.Tests` | The three accepted forms; refusals: a host name, a port of 0 or 70000, an empty string, `atlas:192.168.0.10`; the phases |
| `ParamBinderTests` | `Atlas.Telemetry.Tests` | Field kinds, existing names followed, wrong forms refused, `Dispose` |
| `LinkPumpTests`, `TelemetryWiringTests` | `Atlas.Telemetry.Tests` | Step 8: edge events, the `param` echo and its once-per-second rule, the world hash and `world` event, commands to `IOperatorCommands`, the row backlog, the manifest re-sent on a version change |
| `FlatTwinTests`, `FlatTwinDiscTests` | `Atlas.Telemetry.Tests` | With a `FlatWalkerKernel` walked round a square, the trail's tile-id sequence equals the kernel's; the S0 plan contains every outline segment of `sample_3x3`; each space's twin; the disc of {4,5} and {4,6}, and what the builder draws for S2 and S3 |
| `SpectatorCoreTests`, `SpectatorAssetsTests` | `Atlas.Telemetry.Tests` | Room fetch, world check and mismatch, space following, banners; the bytes of the `State`s of a lap in S2 and of a walk round S4 give the phone's tile and pose; a world generated differently is not shown; furniture from events and job lists; a first shape for every object, including those that get no mesh |
| `StationReplayTests` | `Atlas.Spaces.CurvedWalk.Tests` | The recorded 5-corner lap through the real Spectator core, host and loop into `CurvedWalkCore.ReadState`: the same tile, pose, tile zero and content hash on every frame; the dial to 6 |
| `ScanWallBridgeParamTests`, `FurnitureLevelPolicyTests` | `Atlas.App.Tests` (`Tests/App/`) | `scan.wallBridge` is a Bool in group `scan`, declared on, right after `scan.teamPrescan`, and switches the bridge on and off where it is available and not where it is not. `render.furniture` has the three values and defaults to `auto`; the space table is Full in Home and Curved Walk and Light elsewhere; the ladder's first step forces Light and nominal gives Full back; `light` and `full` override; rebuilding the room puts the level back |
| `MenuModelTests` | `Atlas.App.Tests` (`Tests/App/Menu/`) | The pages; the kill switches in the order of 1.5, 22 of them, `hands.enabled` last and above the reset row; stepping per kind; an `OnRelease` row changes only on `Confirm`; every change goes through the registry with `ParamSource.Local` and logs `param.set` |
| Forge | `python -m pytest station/forge/tests` | With the `procedural` generator, built shapes and a fake TripoSG pipeline (no GPU, no PyTorch): a crop becomes a pair of GLBs that pass `validator.py` at their levels; the same input gives the same hash; a corrupt JPEG ends as `failed`; a second identical upload is a cache hit with no job, whose answer names the light copy; a 33rd box is answered `placeholder`, the queue is largest first and capped at 32; `backend` `cache` with a miss fails at once; the hosted fallback is off by default and has no vendor; a restart keeps rooms, finished jobs and assets, and drops a light copy whose file is gone. `test_budget`: the rule of §4.1 on the vectors of the brief (the fourth sums to 119,990) and the table of equal sizes. `test_validator`: the two levels, file limits per level, `palette.cell` and `palette.face`, `--level`. `test_api`: `meta.profile` and `triTargetLight` and their 422 cases, what a job keeps, the `JobStatus` members, a room record planned again. `test_imaging`: the framed picture is square, centred, white, with a margin of a tenth of the long side. `test_triposg` (fake pipeline): what `GET /health` says for each missing piece, the load at start-up, weights without `model_index.json`, the default call as upstream makes it, the fallback decoder and its order switch, matting required, views that cannot be matted passed over. `test_config`: the settings, their variables and their checks. `test_profile`, `test_colour`: the phone's rays on a mesh (the test vectors of `ObjectProfile`), the fit score, the seen test, smoothing, the speckle fallback. `test_fit`: a mesh turned by each quarter turn is turned back, ties and the up-axis rule, `triposg_front`, two colours in the right places, both copies validate, a full copy within the light target is its own light copy. `test_attempts`: a poor fit runs views 1 and 2 and publishes the best, a mesh is published however poor its fit, how failures count, no second photo for the hosted service, the next best published when the best cannot be finished, the light copy as an asset of its own, `reclean` reproducing the published bytes. `test_meshcheck`, `test_first_run`: the numbers of `tools/meshcheck/check.py` and the reading of the generator's frame. `tools/e2e/station_rehearsal.py` checks the Forge's targets against `tools/rooms/roommath.py` and both copies of every mesh against their limits |
| Hub | `dotnet test station/hub.tests` | Relay of `State` unchanged; `perf.csv` equal to `TelemetryCsv`; presets saved, loaded as one `ParamSet`, baked; `/internal/job` relayed, with the four light members of an `AssetReady` passed through to the phone and the spectator unchanged (`ForgeJob_LightCopyFields_ArePassedThroughUnchanged`); session adoption and continuation; watchdogs; ReplayPhone against the Hub |
| Console | `node --test station/console-tests` | The Console's modules: controls, commands, formats, jobs, map maths, telemetry |

Only a device or the Station can confirm: LiteNetLib under IL2CPP with stripping Low, and `LiteNetTransport`
surviving it through `link.xml`; the Local Network prompt on a clean install; `Application.deepLinkActivated`
on a warm open; `FrameTimingManager` GPU times on Metal; the render counters in a release player; the
generated meshes on the phone (mirroring, the palette cells per face, shade); the first shapes and the swap to the
light copy on the phone; TextMeshPro on the `AtlasHud` layer in both eyes; TripoSG on the Station's GPU, which has
never generated a mesh, so also the fit, the colours and the light copy on a real generated mesh; the tests L1 to
L22 of §7.6.

---

## Open for the lead

1. **Hosted image-to-3D service** (open item 8 of §14; settle by G6): which one, what budget, what licence, what
   data-retention terms. Until then the `hosted` adapter has no vendor and rule 1 of §3.10 holds: nothing leaves
   the Station. Also wanted with it: the measured peak memory per object on the RTX 4060 Laptop (8 GB).
2. **The Station's address.** `192.168.0.10` is this page's default; the third number is the router's
   (`P1-X04`). One constant, `PairingController.DefaultAddress`.
3. **Where the phone's CSV lives.** README F8 says `persistentDataPath/logs/`, §7.3.3 says
   `persistentDataPath/perf/`. The code follows F8 and keeps the file name `perf_<session>.csv` (§5.5). Either
   the plan or the code changes.
4. **The unit §13.5 deletes by.** The phone makes the session id and the Hub adopts it (§3.2, §5.5); a new
   session starts when onboarding starts a new walker. Confirm that this is the unit of deletion.
5. **Console access.** Anyone on the Station's router can open the Console and send commands. There is no
   password in Phase 1. Acceptable on our own router; say so if the venue changes that.
6. **Text.** §8.1 names TextMeshPro for every label; README F7 names one shader. The HUD and the link dot use
   TextMeshPro; the menu, the mini-map's labels and the Spectator's panels use legacy `TextMesh` with the
   built-in font. Decide whether those move to TextMeshPro.
7. **No interpolation in the Spectator.** The picture is the newest `State`. If the mirror stutters at 30 or
   10 Hz, a 50 ms interpolation buffer is the fix, at the cost of that latency.
8. **Loss simulation on the real link.** The headless tests use `LoopbackNetwork`. The Hub has no loss or
   latency option; L9 and L12 on real sockets need LiteNetLib built with `SIMULATE_NETWORK`, on the Hub or on
   the Editor side. Decide which.
9. **Comfort stop value** (open item 6 of §14). The Console has the FMS entry; the score at which it alerts
   waits for the lead's number.
10. **Uploading the phone's CSV.** The Hub has `PUT /logs/{session}/perf.csv`; the app has no button or
    automatic upload for it. Decide whether the app sends it after a session or the operator does.
11. **TripoSG has never generated a mesh, and its output frame is unmeasured.** Every Forge result on this page comes
    from the procedural generator, built shapes or a fake pipeline. `FORGE_TRIPOSG_UP` (`Y`) and `FORGE_TRIPOSG_FRONT`
    (`unknown`) are guesses until the first run on the Station reads the frame (§3.8). A wrong `FORGE_TRIPOSG_UP` is the
    costly one: no other axis is tried, and the likely result is `asset contract: [frame.bounds]` on every job. Also
    unmeasured: the time and memory per object, the size of the raw mesh, whether `diso` builds and runs on the Station,
    and whether the fallback decoder works where the default one fails.
12. **The thresholds are starting values.** The Forge's poor fit 0.20 (`FORGE_POOR_FIT`) and tie width 0.02
    (`FIT_TIE`), the fit's coverage floor of 0.10, the colour step's 6 cells, 2 % merge and speckle share of one half, and the phone's coverage of 0.30
    for a first shape are set by reading, not by measurement. Checks M1 and M2 of the mesh design, on a recorded
    team scan (`tools/meshcheck/check.py`), are where they are tuned.
13. **What is unproven on a real generated mesh.** The fit has been seen only on perfect or clean cases: how a TripoSG
    mesh scores against a real LiDAR profile (noise, cells seen in part, the floor seen through a chair's legs, a shape
    that only resembles the object) is not known, nor whether the 15 % stretch filter leaves a candidate for an object
    photographed at an angle, nor how a raw mesh of several hundred thousand triangles behaves when reduced. The colours
    are proven only where the mesh matches the photo and the camera is exact: not with ARKit's intrinsics and
    `camToBox`, not with rembg on a real room photo, not with a mesh that does not line up with the photo, and a mesh
    wound clockwise from outside would be seen by no photo (one cell, and an inverted baked shade): the first run reads
    the winding, and `FORGE_TRIPOSG_INSIDE_OUT` turns it round, but TripoSG's two decoders may wind differently. What collapsing a
    real 15,000-triangle chair to its light target does to its legs is not known; the light copy is left out when it
    cannot keep every edge under 0.25 m. Three generations of one object take three times its time on the Station, and
    with 32 objects of poor fit the queue is long; neither is measured.
14. **The kill-switch list and the two new parameters.** `scan.wallBridge` and `render.furniture` are on the menu's
    Parameters page and in the Console but not in the 22 names of §1.5. Whether they belong on the menu's Quality page
    is a change to §1.5, to `MenuModel.KillSwitchIds` and to `MenuModelTests` together.
