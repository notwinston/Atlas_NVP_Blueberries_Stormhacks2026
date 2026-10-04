# Lane D · Link, Station and Forge contract

Task `P1-X08`, frozen at gate G0 (Master Plan §6.4, §7, §12.4, Appendix B, Appendix C.5 and C.7). Owner: lane D.

One file, five parts:

| Part | Fixes | Sections |
|---|---|---|
| [1 · Parameters](#part-1--parameters) | `ParamRegistry`, ids, the kill-switch list, `AtlasParamAttribute`, presets, `AtlasJson` | 1.1 – 1.8 |
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
- Threading: every public member on this page is called on the main thread unless it says otherwise.
- Protocol version `proto` = `[1, 0]`. Connection key `atlas-v1`.
- Section numbers. `§1.1` to `§5.9`, `§6` and `§7` are sections of **this page**. Every other `§` is the Master
  Plan (`§6.2`, `§7.4`, `§8.6`, `§11.2`, `§12.4`, …); where a plan section falls in this page's range it is
  written `plan §4.5`. "B.1" to "B.4" and "C.5", "C.7" are the plan's appendices.
- Names of other lanes' types are those of their pages (`lane-a-platform.md`, `lane-b-kernel.md`,
  `lane-c-room.md`). §6 lists every one this page relies on.
- Unity and package facts (LiteNetLib, glTFast, `UnityWebRequest`, deep links, TextMeshPro) are verified in
  `unity-api/input-net-assets.md`; this page cites it as "U-net".

| Area | Assembly | Namespace | Folder |
|---|---|---|---|
| Registry, JSON, messages, clock, watchdog, client, host | `Atlas.Link` (engine-free) | `Atlas.Link` | `ATLAS/Assets/Atlas/Link/` |
| Installer, attribute binder, step 8 glue, perf probe, HUD, event log, asset client, pairing, flat-twin core | `Atlas.Telemetry` | `Atlas.Telemetry` | `ATLAS/Assets/Atlas/Telemetry/` |
| `IEventLog` | `Atlas.World` (lane D file) | `Atlas.World` | `ATLAS/Assets/Atlas/World/Services/IEventLog.cs` |
| On-device menu | `Atlas.App` (lane D files) | `Atlas.App.Menu` | `ATLAS/Assets/Atlas/App/Menu/` |
| Spectator | `Atlas.Spectator` | `Atlas.Spectator` | `ATLAS/Assets/Atlas/Spectator/` |
| Tests | `Atlas.Link.Tests`, `Atlas.Telemetry.Tests` | same | `ATLAS/Assets/Atlas/Tests/Link/`, `…/Tests/Telemetry/` |
| Hub and Console | .NET 8 console | `Atlas.Hub` | `station/hub/` |
| Forge | Python 3.11 package `forge` | – | `station/forge/` |
| ReplayPhone | .NET 8 console | `Atlas.Tools` | `tools/ReplayPhone/` |
| Fallback-set and palette generators | Python scripts | – | `tools/gen_fallback_set.py`, `tools/gen_forge_palette.py` |

Files in `Atlas.Link` (one public type per file unless listed together):

```
Link/Params/   ParamTypes.cs (ParamKind, ParamFlags, ParamSource, ParamHandle, ParamInfo, ParamChanged)
               ParamRegistry.cs  ParamSnapshot.cs  AtlasParams.cs
Link/Json/     AtlasJson.cs  JsonValue.cs  JsonWriter.cs
Link/Messages/ MsgId.cs (MsgId, LinkProto)  LinkBytes.cs  StateMessage.cs (StateMessage, StateFlags)
               TelemetryMessage.cs  TelemetryCsv.cs  TimeMessages.cs (TPingMessage, TPongMessage)
               ParamSetMessage.cs  RoomOutlineMessage.cs (RoomOutlineMessage, OutlineBox)
               JsonMessages.cs (Hello, Welcome, SceneCmd, OperatorCmd, JobStatus, AssetReady, AssetAck, enums)
               WorldHash.cs
Link/Transport/ LinkClient.cs  LinkHost.cs  LoopbackLink.cs  LinkSinks.cs (the three sink interfaces)
               ClockSync.cs  LinkWatchdog.cs  LinkClock.cs (ILinkClock, StopwatchClock)
```

The public surface of `Atlas.Link` on this page was compiled against .NET Standard 2.1 with C# 9 and the
project's LiteNetLib 2.1.4 assembly, and the `State` codec was run against the bytes of §2.3.

**In this build.** Lanes A, C and D are built end to end. Everything on this page is built. Lane B's spaces
(S2, S3, S4) are not: their parameters and their `State` rows are fixed here so that nothing changes when they
arrive.

---

# Part 1 · Parameters

Every tunable is one entry of one `ParamRegistry` (README F6). The composition root creates the one instance
(`AtlasBootContext.Params`, lane A §8.2 step 2), publishes it through `AtlasServices`, and SpaceLab hands it to
each space in `ISpaceModule.Configure`. The Spectator has its own instance. The Hub holds no registry: it
stores the manifest and values as data.

## 1.1 Ids, indices and groups

A parameter has two names.

| Name | Type | Example | Used for |
|---|---|---|---|
| **Id** | string | `render.scale` | Code, the contract pages, §12.2, §12.4, presets, logs, events. Stable across builds |
| **Index** | `u16` | `3` | The wire: `ParamSet` and the `id` field of `ParamManifest`. Declaration order. Stable within one run of the app |

Appendix B.2 writes the manifest row as `{id, name, …}`: its `id` is the index and its `name` is the id. Lane A's
page calls the id the "name".

Id rules:

- `<area>.<name>`; each segment matches `[a-z][a-zA-Z0-9]*`; at most 40 characters. One exception, kept from
  §12.4: `demoLock`.
- The first segment is the owning area: `space`, `render`, `thermal`, `pose`, `replay`, `scan`, `link`,
  `telemetry`, `forge`, `hud`, or a space's own: `home`, `glued`, `curved`, `cells`, `planet`, `portal`, `rooms`,
  `slicer`. A use case (Phase 2) takes its own first segment (`playground`, `lung`, …).
- A space's parameters are written as §12.2 and §12.4 write them: `glued.copies`, not `space.glued.copies`. The
  only id that starts with `space.` is `space.id`.
- The **group** is a lower-case key. For a space it is `SpaceIds.ParamGroup(id)` (lane A §1): `home`, `glued`,
  `curved`, `cells`, `planet`, `portal`, `slicer`. Others in use: `app`, `render`, `pose`, `scan`, `link`,
  `forge`, `hud`. The Console and the menu show it as a heading (`glued` as "S1 Glued", and so on).
- A group is **active** or **inactive** (lane A §4.3). SpaceLab marks a space's group inactive on `Exit` and
  active on `Enter`. An inactive group is shown folded and greyed; its parameters keep their values and can
  still be set. Parameters flagged as kill switches are shown always.

Every value is one `float` on the wire and in the registry, whatever its kind:

| Kind | Stored value | Clamp applied by `Set` |
|---|---|---|
| `Float` | the number | to `[min, max]`; then to the nearest `min + k·step` when `step > 0`; or to the nearest entry of `choices` (a tie goes to the lower entry) |
| `Int` | the integer as a float (exact below 2²⁴) | rounded half away from zero, then to `[min, max]`, or to the nearest entry of `choices` |
| `Bool` | 0 or 1 | `value ≥ 0.5` gives 1, else 0 |
| `Enum` | the option index 0 … n − 1 | rounded, then to `[0, n − 1]` |

A `NaN` is never stored: `Set` returns the current value unchanged.

## 1.2 `ParamRegistry`

```csharp
// Files: ATLAS/Assets/Atlas/Link/Params/ParamTypes.cs, ParamRegistry.cs, ParamSnapshot.cs, AtlasParams.cs
using System;

namespace Atlas.Link
{
    public enum ParamKind : byte { Float = 0, Int = 1, Bool = 2, Enum = 3 }

    [Flags]
    public enum ParamFlags : byte
    {
        None       = 0,
        OnRelease  = 1 << 0,   // the Console sends it when the slider is released; it regenerates geometry
        KillSwitch = 1 << 1,   // listed by the on-device menu (sections 12.2 and 12.4); never hidden
        NoRemote   = 1 << 2,   // Set with ParamSource.Link is ignored (demoLock)
        ReadOnly   = 1 << 3,   // only ParamSource.Code may change it; shown greyed
        Live       = 1 << 4,   // its owner writes it every frame; Code changes are not echoed as events
    }

    /// <summary>Who asked for a change. Written to the event log with every change.</summary>
    public enum ParamSource : byte { Code = 0, Link = 1, Menu = 2, Operator = 3, Ladder = 4, Restore = 5 }

    /// <summary>A parameter, resolved once. default(ParamHandle) is invalid.</summary>
    public readonly struct ParamHandle : IEquatable<ParamHandle>
    {
        public bool IsValid { get; }
        public ushort Index { get; }                 // the wire index; 65535 when invalid
        public bool Equals(ParamHandle other);
    }

    /// <summary>Called on the main thread, after the value is stored.</summary>
    public delegate void ParamChanged(ParamHandle handle, float value, ParamSource source);

    /// <summary>One row of the manifest. Returned by value; Labels and Choices are shared arrays: do not write.</summary>
    public readonly struct ParamInfo
    {
        public readonly ParamHandle Handle;
        public readonly string Id;
        public readonly string Group;
        public readonly ParamKind Kind;
        public readonly ParamFlags Flags;
        public readonly float Min, Max, Default, Step, Value;
        public readonly string[] Labels;             // Enum: one per option; otherwise null
        public readonly float[] Choices;             // Float or Int with a fixed value set; otherwise null
        public readonly bool GroupActive;
    }

    public sealed class ParamRegistry
    {
        public const int MaxParams = 1024;

        /// <summary>A registry that already holds every row of section 1.4, in table order (indices 0..47).</summary>
        public ParamRegistry();
        /// <summary>wellKnown false: an empty registry, for tests.</summary>
        public ParamRegistry(bool wellKnown);

        // ---- declare (idempotent by id; see 1.3) -------------------------------------------------
        public ParamHandle DeclareFloat(string id, string group, float min, float max, float def,
            float step = 0f, ParamFlags flags = ParamFlags.None, ParamChanged onChanged = null);
        public ParamHandle DeclareFloat(string id, string group, float[] choices, float def,
            ParamFlags flags = ParamFlags.None, ParamChanged onChanged = null);
        public ParamHandle DeclareInt(string id, string group, int min, int max, int def,
            ParamFlags flags = ParamFlags.None, ParamChanged onChanged = null);
        public ParamHandle DeclareInt(string id, string group, int[] choices, int def,
            ParamFlags flags = ParamFlags.None, ParamChanged onChanged = null);
        public ParamHandle DeclareBool(string id, string group, bool def,
            ParamFlags flags = ParamFlags.None, ParamChanged onChanged = null);
        public ParamHandle DeclareEnum(string id, string group, string[] labels, int def,
            ParamFlags flags = ParamFlags.None, ParamChanged onChanged = null);

        // ---- read: an array read, no allocation, safe on the frame path --------------------------
        public float GetFloat(ParamHandle h);        // the stored value, any kind
        public int   GetInt(ParamHandle h);          // rounded to nearest
        public bool  GetBool(ParamHandle h);         // value >= 0.5

        // ---- find: a dictionary lookup; do it once and keep the handle ---------------------------
        public ParamHandle Find(string id);          // KeyNotFoundException when unknown
        public bool TryFind(string id, out ParamHandle h);

        // ---- write: clamps, stores, calls back, returns the applied value ------------------------
        public float Set(ParamHandle h, float value, ParamSource source = ParamSource.Code);
        public bool  TrySet(string id, float value, ParamSource source, out float applied);
        public float ResetToDefault(ParamHandle h, ParamSource source = ParamSource.Code);

        // ---- listen ------------------------------------------------------------------------------
        public void Subscribe(ParamHandle h, ParamChanged listener);
        public void Unsubscribe(ParamHandle h, ParamChanged listener);
        public event ParamChanged Changed;           // every applied change of any parameter

        // ---- groups ------------------------------------------------------------------------------
        public void SetGroupActive(string group, bool active);    // every group starts active
        public bool IsGroupActive(string group);

        // ---- enumerate (Console manifest, on-device menu) ----------------------------------------
        public int Count { get; }
        public int ManifestVersion { get; }          // +1 per new declaration, changed range, default, labels or group state
        public ParamInfo GetInfo(int index);         // 0 .. Count-1, in declaration order
        public ParamInfo GetInfo(ParamHandle h);
        public void WriteManifest(JsonWriter w);     // the ParamManifest body of 2.5

        // ---- snapshot, restore, baked defaults ---------------------------------------------------
        public void Snapshot(ParamSnapshot into);                                // every id and current value
        public int  Restore(ParamSnapshot from, ParamSource source = ParamSource.Restore);  // returns values changed
        public void ApplyBakedDefaults(ParamSnapshot baked);                     // 1.7
    }

    /// <summary>A set of (id, value) pairs: a preset, a saved state, the baked defaults.</summary>
    public sealed class ParamSnapshot
    {
        public int Count { get; }
        public string IdAt(int i);
        public float ValueAt(int i);
        public void Clear();
        public void Add(string id, float value);                 // replaces an existing id
        public bool TryGet(string id, out float value);
        public void Write(JsonWriter w);                         // {"values":{"render.scale":0.8,...}}
        public static bool TryRead(JsonValue json, out ParamSnapshot snapshot);
    }

    /// <summary>The ids of section 1.4 as constants, and the method that declares them.</summary>
    public static class AtlasParams
    {
        public const string SpaceId = "space.id";
        public const string DemoLock = "demoLock";
        public const string ThermalPinTier = "thermal.pinTier";
        public const string RenderScale = "render.scale";
        // ... one constant per row of 1.4, named after the id in PascalCase.

        /// <summary>Declares every row of 1.4 in table order. Called by the default constructor of ParamRegistry.</summary>
        public static void DeclareWellKnown(ParamRegistry registry);
    }
}
```

## 1.3 Rules

| Topic | Rule |
|---|---|
| Declaring twice | Declaring an id that exists returns the **same handle** and keeps the current value. The kind is the one of 1.4. If a later declaration uses another kind (an `Int` 0 … 6 for the `Enum` `space.id`, say), the first kind, its labels and its choices are kept, an error is logged once, and the handle is still returned: a mismatch between two lanes must not stop the boot. Otherwise the new `min`, `max`, `def`, `group` and `labels` replace the old ones. A declaration by range keeps an existing `choices` list (cut to the new range) and an existing `step` when it passes `step` 0, so an owner that declares `render.scale` as plain 0.6 … 0.8 keeps the three choices of 1.4. Flags are OR-ed; a non-null `onChanged` replaces the owner callback; the value is clamped into the new range. `ManifestVersion` rises if anything changed |
| Bad declaration | `ArgumentException` for an id that breaks 1.1, `min > max`, `def` outside the range, empty `labels` or `choices`, more than 64 labels or choices, or more than `MaxParams` entries |
| `Set` | Clamps as in 1.1. If the applied value equals the stored one, nothing else happens. Otherwise: store, call the owner callback, call subscribers, raise `Changed`. Returns the applied value |
| `Set` and flags | `ReadOnly`: ignored unless `source == Code`. `NoRemote`: ignored when `source == Link`. Ignored means the stored value is returned and no callback runs |
| Invalid handle | `ArgumentException` from every method that takes a handle |
| Reading | `GetFloat`, `GetInt`, `GetBool` are an array read: no allocation, no lock, no branch on kind. Keep the handle in a field; never call `Find` on the frame path |
| Callbacks | Run synchronously inside `Set`, on the main thread. A callback may call `Set` on another parameter; it must not declare. A callback that throws is caught and logged as an error; the value stays applied |
| Threading | Main thread only. The link applies `ParamSet` at step 8b (§5.1), so callbacks never run inside steps 2 to 6 |
| Lock and watchdog | The registry knows nothing about the link. `LinkClient` drops a whole `ParamSet` while `demoLock` is on or the link is DEGRADED (§2.8), before it reaches the registry |
| `OnRelease` | The Console sends one `ParamSet` when the slider is released. The owner's handler rebuilds and calls `ISpaceHost.BumpWorldVersion()` (lane A §4.3) |
| Removal | There is none. A parameter lives until the app exits. Leaving a space marks its group inactive (1.1) |
| Bump Guard | Declares nothing and reads nothing. Its thresholds and its depth rate are constants in `Atlas.Safety` (plan §4.10, §7.5 rule 3). `Atlas.Safety` cannot reference `Atlas.Link` at all (README F1) |

Who declares what (README F6):

- `new ParamRegistry()` already holds the table of 1.4. That fixes the indices, and it makes every switch of
  §12.2 and §12.4 exist from the first frame, lane B's included (test L22).
- The owner then declares its rows again, with the same values, to attach its handler: `RenderParams` and
  `QualityBinding` in `Atlas.App` for the rendering rows (lane A §9.6); each space through its `DeclareParams`
  delegate at boot and again in `Configure` (lane A §4.3, §5); `Atlas.App` for lane C's rows, whose owners
  cannot reference `Atlas.Link`; lane D's installer for its own.
- A parameter that is not in 1.4 is declared by its owner the same way and takes the next free index. Its row
  is added to 1.4 by the rule of §6.4 (announce, then commit).
- The thermal ladder lives in `Atlas.Rendering`. `QualityBinding` writes each rung's row with
  `registry.Set(handle, value, ParamSource.Ladder)` (lane A §9.5).

Typical use:

```csharp
// A space, in DeclareParams and in Configure(RoomModel room, ParamRegistry registry):
_copies = registry.DeclareInt(AtlasParams.GluedCopies, "glued", new[] { 3, 5, 7 }, 5,
                              ParamFlags.KillSwitch | ParamFlags.OnRelease, OnCopiesChanged);
// Frame path:
int n = registry.GetInt(_copies);
// Atlas.App, for an owner that may not reference Atlas.Link:
var ipd = registry.Find(AtlasParams.RenderIpd);
registry.Subscribe(ipd, (h, v, src) => rig.Ipd = v);
rig.Ipd = registry.GetFloat(ipd);
```

## 1.4 Ids known now

Declared by `AtlasParams.DeclareWellKnown` in this order; the row number is the index. Flags: **K** kill switch,
**R** on release, **N** no remote, **L** live. Names, ranges and defaults of lane A's rows are those of lane A
§9.6 and §14; this table adds the index, the kind and the flags. Rows marked *(B)* have no consumer in this
build.

| # | Id | Kind | Range or options | Default | Unit | Flags | Group | Owner · consumer |
|---:|---|---|---|---|---|---|---|---|
| 0 | `space.id` | Enum | `S0 Home`, `S1 Glued`, `S2 Curved Walk`, `S3 Aligned Cells`, `S4 Small Planet`, `S5 Impossible Rooms`, `S6 Slicer` | 0 | – | K R | `app` | A · `ISpaceHost.RequestSwitch`; written back after every switch |
| 1 | `demoLock` | Bool | | false | – | K N | `app` | D · `LinkClient` |
| 2 | `thermal.pinTier` | Int | −1 … 4: −1 automatic, 0 … 3 the rows of §8.6, 4 the high tier | −1 | – | K R | `render` | A · `QualityInputs.PinTier` |
| 3 | `render.scale` | Float | choices 0.8, 0.7, 0.6 | 0.8 | – | K R | `render` | A · `EyeTextures.Select` |
| 4 | `render.msaa` | Int | choices 1, 2, 4 | 4 | samples | K R | `render` | A · `EyeTextures.Select` |
| 5 | `render.ipd` | Float | 0.055 … 0.075, step 0.0005 | 0.064 | m | | `render` | A · `IStereoRig.Ipd` |
| 6 | `render.lineWidth` | Float | 0.5 … 3, step 0.1 | 1.5 | px | | `render` | A · `AtlasLook.SetLineWidth` |
| 7 | `render.lens` | Bool | | true | – | | `render` | A · `LensBlit.Distortion` |
| 8 | `render.lensK1` | Float | 0 … 1, step 0.005 | 0.34 | – | | `render` | A · `LensParams.K1` |
| 9 | `render.lensK2` | Float | 0 … 1, step 0.005 | 0.55 | – | | `render` | A · `LensParams.K2` |
| 10 | `render.lensSeparation` | Float | 0.055 … 0.075, step 0.0005 | 0.064 | m | | `render` | A · `LensParams.LensSeparation` |
| 11 | `render.screenToLens` | Float | 0.025 … 0.060, step 0.0005 | 0.039 | m | | `render` | A · `LensParams.ScreenToLens` |
| 12 | `render.lensOffsetY` | Float | −0.02 … 0.02, step 0.0005 | 0 | m | | `render` | A · `LensParams.VerticalOffset` |
| 13 | `render.headOffsetX` | Float | −0.10 … 0.10, step 0.001 | 0 | m | | `render` | A · `IStereoRig.HeadOffset` |
| 14 | `render.headOffsetY` | Float | −0.10 … 0.10, step 0.001 | 0 | m | | `render` | A · the same |
| 15 | `render.headOffsetZ` | Float | −0.10 … 0.10, step 0.001 | 0 | m | | `render` | A · the same |
| 16 | `curved.cullRho` | Float | 2 … 3.5, step 0.05 | 3 | kernel units | K | `curved` | B · `IWalkerKernel.CullRho`; written by the ladder |
| 17 | `curved.mode` | Enum | `Mode 1 Curved Walk`, `Mode 2 Aligned Cells` | 0 | – | K R | `curved` | *(B)* |
| 18 | `curved.L` | Float | 0 … 6, step 0.05; 0 = the loop-fit value of the RoomModel | 0 | m | R | `curved` | *(B)* (`_HypL`, §7.3.2) |
| 19 | `cells.viewDepth` | Int | 0 … 3 | 3 (proposed) | cells | K | `cells` | *(B)* |
| 20 | `planet.R` | Float | 0 … 4, step 0.05; 0 = the loop-fit value | 0 | m | K R | `planet` | *(B)* |
| 21 | `planet.fogRho` | Float | 0.5 … 3.2, step 0.05 | 1.6 (proposed) | kernel units | K | `planet` | *(B)* |
| 22 | `glued.mode` | Enum | `T2`, `Klein`, `T3` | 0 | – | K R | `glued` | A · S1 |
| 23 | `glued.copies` | Int | choices 3, 5, 7 | 5 | per side | K R | `glued` | A · S1; written by the ladder |
| 24 | `glued.avatar` | Bool | | true | – | | `glued` | A · S1 |
| 25 | `rooms.layout` | Enum | `booth`, `loop` | 0 | – | R | `portal` | A · S5 |
| 26 | `rooms.count` | Int | 2 … 6 | 6 | rooms | K R | `portal` | A · S5 |
| 27 | `portal.depth` | Int | 0 … 1 | 1 | – | K | `portal` | A · S5 |
| 28 | `slicer.source` | Enum | `live`, `stepped`, `tesseract` | 0 | – | K R | `slicer` | A · S6 |
| 29 | `slicer.object` | Enum | `tesseract`, `cell5`, `cell16`, `cell24`, `tube` | 0 | – | R | `slicer` | A · S6 |
| 30 | `slicer.view` | Enum | `slice`, `shadow` | 0 | – | | `slicer` | A · S6 |
| 31 | `slicer.w` | Float | −1.1 … 1.1, step 0.01 | 0 | model units | L | `slicer` | A · S6 |
| 32 | `slicer.xw` | Float | −180 … 180, step 1 | 0 | degrees | L | `slicer` | A · S6 |
| 33 | `slicer.yw` | Float | −180 … 180, step 1 | 0 | degrees | L | `slicer` | A · S6 |
| 34 | `slicer.zw` | Float | −180 … 180, step 1 | 0 | degrees | L | `slicer` | A · S6 |
| 35 | `slicer.tilt` | Float | 0 … 80, step 1 | 0 | degrees | | `slicer` | A · S6 (the tube only) |
| 36 | `pose.gyroPredict` | Bool | | false | – | K | `pose` | C · pose pipeline |
| 37 | `pose.gyroLeadMs` | Float | 0 … 60, step 1 | 40 (proposed; §8.7 gives 30–60) | ms | | `pose` | C · pose pipeline |
| 38 | `pose.source` | Enum | `tracked`, `replay`, `walker` | 0 | – | R | `pose` | C · choice of pose source (README F9) |
| 39 | `replay.speed` | Float | 0 … 4, step 0.25 | 1 | × | | `pose` | C · pose-log replay |
| 40 | `replay.loop` | Bool | | true | – | | `pose` | C · pose-log replay |
| 41 | `scan.route` | Int | 1 … 4 | 3 | – | K | `scan` | C · scan session |
| 42 | `link.enabled` | Bool | | true | – | K | `link` | D · `LinkClient` |
| 43 | `link.stateHz` | Int | choices 10, 30, 60 | 60 | Hz | K | `link` | D · `State` send rate |
| 44 | `telemetry.mode` | Enum | `live`, `phoneCsv`, `hud` | 0 | – | K | `link` | D · telemetry |
| 45 | `forge.backend` | Enum | `local`, `hosted`, `cache`, `off` | 0 | – | K | `forge` | D · asset client |
| 46 | `hud.visible` | Bool | | false | – | | `hud` | D · HUD |
| 47 | `hud.map` | Bool | | false | – | | `hud` | D · mini-map in the headset |

Notes:

- Angles are **degrees** in the registry and radians in code and in `State.p[]` (README F4). The owner converts.
- `render.scale` is the real scale and `render.msaa` the real sample count, not an index. Against §12.4,
  `render.msaa` gains 1, `glued.copies` gains 7 and `thermal.pinTier` gains 4, for the last and the high rung
  (lane A, open item 4).
- Build-dependent start values are set by lane D's installer, not by the table: `demoLock` to true in a build
  with `ATLAS_DEMO_LOCK`, `hud.visible` to true in a development build.
- `space.id` set to a space that is not available (S2, S3, S4 in this build) is refused by SpaceLab
  (`space.refused`), which writes the parameter back to the entered space.
- `telemetry.mode`: `live` sends `Telemetry` and writes the phone CSV; `phoneCsv` writes the CSV only; `hud`
  writes nothing and forces the HUD on (§12.2, the "Station: telemetry" row).
- `forge.backend`: §4.3. With `off` the phone sends nothing to the Forge.
- **L**: S6 writes `slicer.w` and the three angles every frame from the w lane and the sticks. Their values
  travel in `State.p[3..6]`; they raise no `param.set` event unless a person set them.
- Lane C's rows (36 to 41) are this page's reading of §8.7, plan §4.2, §12.4 and README F9. If lane C's page gives a
  different range or default, that page wins and this table is corrected at the freeze.

## 1.5 Kill switches

The union of the "Switch" column of §12.2 and the list of §12.4: the 21 rows flagged **K** above. In menu order:

`space.id`, `demoLock`, `thermal.pinTier`, `render.scale`, `render.msaa`, `curved.cullRho`, `curved.mode`,
`cells.viewDepth`, `planet.R`, `planet.fogRho`, `glued.mode`, `glued.copies`, `rooms.count`, `portal.depth`,
`slicer.source`, `pose.gyroPredict`, `scan.route`, `link.enabled`, `link.stateHz`, `telemetry.mode`,
`forge.backend`.

Rules (§12.4):

- Each is settable from the Console and from the on-device menu (§5.7). The menu needs no link.
- Every change is logged: one `param.set` event (§2.6) in the phone's event log, with the source, and on the
  Hub when it is connected.
- `demoLock` is the one switch the Console cannot set with `ParamSet` (flag N): the Console's lock toggle sends
  `OperatorCmd lock`, which ends in `registry.Set(registry.Find(AtlasParams.DemoLock), on ? 1 : 0,
  ParamSource.Operator)` (lane A's `CommandRouter.SetLock`). The menu sets it directly.
- `link.enabled` set to off from the Console closes the link (`Bye {"reason": "link.enabled off"}`). It can then
  only be set back from the menu.
- **Bump Guard has no kill switch, no parameter and no off state.**

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

        public string Id { get; }
        public float Min { get; }
        public float Max { get; }
        public float Default { get; }
        public string Group { get; set; }        // null: the first id segment
        public float Step { get; set; }          // float fields only
        public bool OnRelease { get; set; }
        public bool KillSwitch { get; set; }
    }

    public static class ParamBinder
    {
        /// <summary>Declares every [AtlasParam] field of target and keeps the field equal to the registry value.</summary>
        public static ParamBinding Bind(object target, ParamRegistry registry);
    }

    /// <summary>Dispose stops the field updates. The parameters stay declared.</summary>
    public sealed class ParamBinding : IDisposable
    {
        public int Count { get; }
        public ParamHandle HandleOf(string id);      // KeyNotFoundException when the id was not bound here
        public void Dispose();
    }
}
```

What `Bind` does, once, off the frame path:

1. Finds every instance field of `target` (public or not, declared on its type or a base type) that carries
   the attribute, by reflection.
2. Checks the field type: `float`, `int`, `bool`, or an enum whose values are exactly 0 … n − 1. Anything else,
   or a constructor that does not fit the field type, is an `InvalidOperationException` that names the field.
3. Declares the parameter (`DeclareFloat`, `DeclareInt`, `DeclareBool`, or `DeclareEnum` with
   `Enum.GetNames` as labels). Declaring is idempotent (1.3), so binding a well-known id is allowed.
4. Writes the registry's current value into the field.
5. Subscribes a listener that writes the field on every change (it boxes; it runs only when a value changes).

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

The attribute is a convenience, not a duty: a space may call `Declare…` and keep handles, as lane A's spaces
do. `Atlas.Rendering`, `Atlas.Scan`, `Atlas.Input` and `Atlas.Safety` cannot use it (they do not reference
`Atlas.Telemetry`). The first three expose plain properties (README F6); Safety has no parameter.

## 1.7 Presets, snapshots and baked defaults

One JSON shape serves all three: `{"values": {"<id>": <number>, …}}`. Keys are ids, never indices **(decided
here)**; Appendix B.3 writes the key as `id`.

| What | Where | Written by | Read by |
|---|---|---|---|
| Preset | `station/hub/presets/<name>.json` | Hub, `POST /api/presets/{name}` | Hub; loading replays every value in one `ParamSet` (§3.3) |
| Baked defaults | `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json` | Hub, `POST /api/presets/{name}/bake`; committed by an engineer | Lane D's installer at boot: `Resources.Load<TextAsset>("AtlasParams/defaults")` → `ParamSnapshot.TryRead` → `registry.ApplyBakedDefaults` |
| Last snapshot | Hub memory, per session | Hub, from every value the Console set | Hub, replayed at RESYNC (§2.8) |

- `ApplyBakedDefaults(baked)`: for every id in `baked` that is declared, the default becomes the baked value
  (clamped) and the value is set to it with `ParamSource.Restore`. For an id declared later, the baked value
  replaces the `def` passed to `Declare…`. A build started with the Hub off therefore runs with the baked values
  (§7.3.2). It is called once, in lane D's installer (step 18 of lane A §8.2), before the frame loop is enabled.
- An id in a preset that the phone does not know is skipped and reported in the Hub log. A value out of range
  is clamped on the phone, as always.
- `defaults.json` is absent until the first Bake. An absent or unreadable file means "no baked values".

## 1.8 `AtlasJson`

The engine-free JSON reader and writer of `Atlas.Link` (README F1). Used for the JSON link messages, events,
parameter snapshots, RoomModel files and the JSON chunk of a GLB. No reflection, no attributes, no generics over
user types. It replaces `JsonUtility` on the phone (§7.2 names `JsonUtility`; `Atlas.Link` cannot reference the
engine, and `System.Text.Json` does not exist in Unity's profile, U-net §11). The Hub uses `System.Text.Json`
for its own HTTP bodies and passes link JSON through as bytes.

**Decided here:** a small immutable DOM for reading, a forward writer for writing.

```csharp
// Files: ATLAS/Assets/Atlas/Link/Json/JsonValue.cs, AtlasJson.cs, JsonWriter.cs
using System;

namespace Atlas.Link
{
    public enum JsonKind : byte { Null = 0, Bool = 1, Number = 2, String = 3, Array = 4, Object = 5 }

    /// <summary>One parsed JSON value. Immutable. Never throws on a wrong kind or a missing member.</summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null;       // the value every failed lookup returns

        public JsonKind Kind { get; }
        public bool IsNull { get; }
        public int Count { get; }                    // elements of an Array, members of an Object, else 0
        public JsonValue this[int index] { get; }    // Array element, or Object member value in document order; Null when out of range
        public JsonValue this[string key] { get; }   // Object member; Null when missing or not an Object
        public bool Has(string key);
        public string KeyAt(int index);              // Object member name in document order; null otherwise

        public bool   AsBool(bool fallback = false);
        public double AsDouble(double fallback = 0.0);
        public float  AsFloat(float fallback = 0f);
        public int    AsInt(int fallback = 0);       // a Number with no fraction that fits; else fallback
        public long   AsLong(long fallback = 0L);    // exact for every integer token in the i64 range
        public string AsString(string fallback = null);
        public byte[] AsBase64();                    // a String holding padded base64; null otherwise
    }

    public static class AtlasJson
    {
        public const int MaxDepth = 32;
        public const int MaxBytes = 4 * 1024 * 1024;

        public static bool TryParse(string text, out JsonValue value, out string error);
        public static bool TryParse(ReadOnlySpan<byte> utf8, out JsonValue value, out string error);
        public static JsonValue Parse(string text);                           // FormatException with the same error text
        public static string Serialize(JsonValue value, bool indented = false);
        /// <summary>Appends value as a quoted, escaped JSON string: for text a caller lays out by hand.</summary>
        public static void AppendQuoted(System.Text.StringBuilder sb, string value);
    }

    /// <summary>Writes compact UTF-8 into one growing buffer. Reset and reuse it; it does not shrink.</summary>
    public sealed class JsonWriter
    {
        public JsonWriter(int initialCapacity = 1024);
        public JsonWriter Reset();
        public JsonWriter BeginObject();
        public JsonWriter EndObject();
        public JsonWriter BeginArray();
        public JsonWriter EndArray();
        public JsonWriter Name(string name);         // inside an object, before its value
        public JsonWriter Value(string v);           // null writes null
        public JsonWriter Value(bool v);
        public JsonWriter Value(int v);
        public JsonWriter Value(long v);
        public JsonWriter Value(float v);
        public JsonWriter Value(double v);
        public JsonWriter Null();
        public JsonWriter Base64(ReadOnlySpan<byte> bytes);
        public JsonWriter RawNumber(string text);    // a number the caller formatted itself; checked against the grammar
        public JsonWriter Raw(JsonValue v);          // re-serialises a parsed value in place

        public int Length { get; }                   // UTF-8 bytes written
        public ReadOnlySpan<byte> AsUtf8();          // valid until the next write or Reset
        public int CopyTo(Span<byte> destination);
        public override string ToString();
    }
}
```

| Topic | Rule |
|---|---|
| Grammar | RFC 8259. UTF-8, no byte-order mark. No comments, no trailing commas, no single quotes |
| Root | Any value. The reader stops at the end of the root value and **ignores every byte after it** (§7.5 rule 5) |
| Limits | Depth 32, input 4 MB. Beyond either: `TryParse` returns false |
| Errors | `error` reads `"<line>:<column> <message>"`; `value` is `JsonValue.Null` |
| Duplicate keys | The last one wins for `this[string]`; all are kept for `this[int]` and `KeyAt` |
| Numbers, reading | Held as `double`. An integer token in the `i64` range is also held exactly, for `AsLong` |
| Numbers, writing | `int` and `long`: decimal digits. `float`: the shorter of formats `G7` and `G9` that parses back to the same `float`. `double`: likewise `G15` and `G17`. Invariant culture. `NaN` and infinities are written as `null` |
| Strings, writing | `"` `\` and controls below U+0020 escaped (`\n`, `\r`, `\t` in short form, others `\u00XX`); everything else as UTF-8 |
| Writer misuse | `Name` outside an object, a value with no `Name` inside an object, an unbalanced `End…`: `InvalidOperationException` |
| Unknown members | A reader of a message or a file ignores members it does not know and takes the documented default for members that are missing |
| Allocation | Parsing allocates (the DOM). Writing allocates nothing once the buffer has grown, except for `float` and `double` values. JSON is never parsed or written in steps 1 to 7 of the frame; in step 8 only when a JSON message is actually sent or received |
| 64-bit values | Tile ids are written as lane B's `TileId.ToString()` text, hashes as hexadecimal strings. Only `t_us` and counters are JSON numbers |

Golden files of the test `Link_RoundTrip` use only numbers that print the same on Mono, IL2CPP and .NET 8:
integers and binary fractions such as 0.25 and 12.5.

Lane C's `RoomModelJson` (lane C §2.6) reads through `AtlasJson.TryParse` and lays its canonical text out
itself (its own line breaks and number format), with `AppendQuoted` for strings. `JsonWriter.RawNumber` serves
a caller that uses the writer but formats numbers itself.

---

# Part 2 · Link

## 2.1 Transport and channels

- **LiteNetLib 2.1.4** on both ends: OpenUPM `com.revenantx.litenetlib` in Unity (sources, never a precompiled
  DLL), NuGet `LiteNetLib` in the Hub and in ReplayPhone. Class `NetManager` (the multi-channel one), not
  `LiteNetManager`. `NetSerializer` and `NetPacketProcessor` are not used.
- Unicast UDP to a known address only. No discovery, no broadcast, no Bonjour (R4 §3.2).
- The Hub is the only listener, on **UDP 9050**. The ATLAS app, the Spectator and Editors are clients and reject
  every connection request (`ConnectionRequest.Reject()`).
- Connection key: `atlas-v1` (`NetManager.Connect(address, port, "atlas-v1")`;
  the Hub answers with `ConnectionRequest.AcceptIfKey("atlas-v1")`).

`NetManager` settings (§7.2, R4 §5.5). Every field name below exists in the installed package.

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
| `AllowPeerAddressChange` | – | true | A phone that returns from the background keeps its peer |
| `MtuDiscovery` | false | false | MTU stays 1,024; no datagram of ours reaches it |

| Ch | LiteNetLib call | Direction | Messages | Rate | Size |
|---|---|---|---|---|---|
| 0 | `peer.Send(buf, 0, n, 0, DeliveryMethod.ReliableOrdered)` | both | `Hello`, `Welcome`, `ParamManifest`, `ParamSet`, `SceneCmd`, `OperatorCmd`, `Telemetry`, `Event`, `RoomOutline`, `Log`, `JobStatus`, `AssetReady`, `AssetAck`, `Bye` | on demand; `Telemetry` 1 Hz | usually under 1 kB; at most 32,768 B. A message over the MTU is fragmented by LiteNetLib (it does so for `ReliableOrdered` only) |
| 1 | `peer.Send(buf, 0, n, 1, DeliveryMethod.Sequenced)` | phone → Hub → spectators | `State` | `link.stateHz`: 60, 30 or 10 Hz; never more than one per frame | 202 B; 234 B on the wire (4 B LiteNetLib header, 8 B UDP, 20 B IPv4), about 14 kB/s at 60 Hz |
| – | `peer.Send(buf, 0, n, 0, DeliveryMethod.Unreliable)` | both | `TPing`, `TPong` | client: 8 at connect, then 1 Hz. Hub: 4 Hz to every client | 13 B, 29 B |
| HTTP | TCP | phone ⇄ Forge; Spectator → Forge | RoomModel, crops, GLB | on demand | Part 3 |

- **Encoding.** Binary messages are written by span codecs (`WriteTo(Span<byte>)`, `ReadFrom(ReadOnlySpan<byte>)`)
  into one reused 32,768-byte send buffer per `LinkClient` and per `LinkHost`: zero allocation. §7.2 names "one
  reused `NetDataWriter`"; the bytes on the wire are the same, and the codecs do not depend on LiteNetLib, so the
  Hub's log reader, ReplayPhone and the tests use them with no transport **(decided here)**.
- **JSON messages** are the id byte followed by the UTF-8 text of one JSON object: no length prefix, no
  terminator. The Hub forwards the bytes to the Console unchanged.
- **Send point.** `State` is sent once per frame from step 8a (§5.1), then `NetManager.TriggerUpdate()` flushes
  it. Unreliable sends (`TPing`, `TPong`) are also followed by `TriggerUpdate()`.
- **Receive point.** `NetManager.PollEvents()` is called once per frame from step 8b on the phone, and every
  millisecond in the Hub's main loop. All callbacks therefore run on the caller's thread.

## 2.2 Message ids

```csharp
// File: ATLAS/Assets/Atlas/Link/Messages/MsgId.cs
namespace Atlas.Link
{
    public static class MsgId
    {
        public const byte Hello = 0x01, Welcome = 0x02, TPing = 0x03, TPong = 0x04;
        public const byte State = 0x10, PeerState = 0x11;
        public const byte ParamManifest = 0x20, ParamSet = 0x21, SceneCmd = 0x22, OperatorCmd = 0x23;
        public const byte Telemetry = 0x30;
        public const byte Event = 0x40, RoomOutline = 0x41, Log = 0x42;
        public const byte JobStatus = 0x50, AssetReady = 0x51, AssetAck = 0x52;
        public const byte Bye = 0x7F;
    }

    public static class LinkProto
    {
        public const int Major = 1, Minor = 0;
        public const string Key = "atlas-v1";
        public const int HubUdpPort = 9050, HubHttpPort = 8080, ForgeHttpPort = 8000;
        public const byte ChannelControl = 0, ChannelState = 1;
        public const int MaxMessageBytes = 32768;
    }
}
```

| Message | Id | Ch | Encoding | Direction | Size |
|---|---|---|---|---|---|
| `Hello` | 0x01 | 0 | JSON | client → Hub | – |
| `Welcome` | 0x02 | 0 | JSON | Hub → client | – |
| `TPing` | 0x03 | – | binary | both | 13 B |
| `TPong` | 0x04 | – | binary | both | 29 B |
| `State` | 0x10 | 1 | binary | phone → Hub → spectators | 202 B |
| `PeerState` | 0x11 | 1 | binary | Hub → phone | **Reserved** for Phase 3 (`P3-D07`). No codec in Phase 1. A receiver ignores it |
| `ParamManifest` | 0x20 | 0 | JSON | phone → Hub → spectators | – |
| `ParamSet` | 0x21 | 0 | binary | Hub → phone | 2 + 6 n B |
| `SceneCmd` | 0x22 | 0 | JSON | Hub → phone | – |
| `OperatorCmd` | 0x23 | 0 | JSON | Hub → phone | – |
| `Telemetry` | 0x30 | 0 | binary | phone → Hub | 91 B |
| `Event` | 0x40 | 0 | JSON | phone → Hub → spectators | – |
| `RoomOutline` | 0x41 | 0 | binary | phone → Hub → spectators | 27 + 8 n + 26 m B |
| `Log` | 0x42 | 0 | JSON | phone → Hub | – |
| `JobStatus` | 0x50 | 0 | JSON | Hub → phone, spectators | – |
| `AssetReady` | 0x51 | 0 | JSON | Hub → phone, spectators | – |
| `AssetAck` | 0x52 | 0 | JSON | phone → Hub | – |
| `Bye` | 0x7F | 0 | JSON | both | – |

Compatibility (§7.5 rule 5), the same for every receiver:

- An **unknown id** is dropped without an error. A counter (`UnknownMessages`) is kept for the link panel.
- **Trailing bytes** after a binary message are ignored. A binary message that is **too short** is dropped.
- A JSON message: unknown members are ignored; bytes after the root object are ignored.
- A `Hello` whose `proto[0]` is not 1 is answered with `Bye {"reason": "proto 2.0 not supported, hub speaks
  1.0"}` and the peer is disconnected. A different minor version is accepted.
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

```csharp
// File: ATLAS/Assets/Atlas/Link/Messages/StateMessage.cs
using System;

namespace Atlas.Link
{
    public static class StateFlags
    {
        public const byte TrackingMask = 0x03;          // 0 normal, 1 limited, 2 lost
        public const byte PoseJumpRejected = 0x04;
        public const byte CueActive = 0x08;
        public const byte TierMask = 0x30;
        public const int  TierShift = 4;
        public const byte DemoLock = 0x40;
        public static byte Pack(int tracking, bool poseJumpRejected, bool cueActive, int tier, bool demoLock);
    }

    /// <summary>Appendix B.1. A plain blittable struct: no arrays, no references.</summary>
    public struct StateMessage
    {
        public const byte Id = MsgId.State;
        public const int Size = 202;

        public byte Flags;
        public ushort SceneId;
        public uint Seq;                                  // set by LinkClient.SendState
        public long TimeUs;                               // set by LinkClient.SendState
        public float HeadPosX, HeadPosY, HeadPosZ;
        public float HeadRotX, HeadRotY, HeadRotZ, HeadRotW;
        public byte SpaceId;
        public byte BumpLevel;
        public long Tile0, Tile1, Tile2, Tile3, Tile4, Tile5;      // Xu, Xv, Yu, Yv, Tu, Tv
        public short Sheet;
        public double M00, M01, M02, M10, M11, M12, M20, M21, M22;  // row-major
        public float P0, P1, P2, P3, P4, P5, P6, P7;
        public ushort WorldVer;

        /// <summary>All zero, except the head rotation (0, 0, 0, 1) and the xform, which are identities.</summary>
        public static StateMessage Identity { get; }

        public void SetTile(long xu, long xv, long yu, long yv, long tu, long tv);
        public void SetXform(double m00, double m01, double m02,
                             double m10, double m11, double m12,
                             double m20, double m21, double m22);
        public void SetXformIdentity();
        public readonly float GetP(int i);                // i = 0..7, else ArgumentOutOfRangeException
        public void SetP(int i, float value);

        /// <summary>Writes exactly 202 bytes and returns 202. ArgumentException when destination is shorter.</summary>
        public readonly int WriteTo(Span<byte> destination);
        /// <summary>False when source is shorter than 202 bytes or source[0] is not 0x10. Extra bytes are ignored.</summary>
        public static bool ReadFrom(ReadOnlySpan<byte> source, out StateMessage message);
    }
}
```

`Span<byte>` is the choice **(decided here)**. It is part of .NET Standard 2.1 and compiles under IL2CPP with no
unsafe code: integers go through `System.Buffers.Binary.BinaryPrimitives`, `f32` through
`BitConverter.SingleToInt32Bits` / `Int32BitsToSingle`, `f64` through `DoubleToInt64Bits` / `Int64BitsToDouble`
(`BinaryPrimitives.WriteSingleLittleEndian` does not exist in .NET Standard 2.1). `LinkBytes` holds these
helpers. A `byte[]` converts to a span implicitly, so `state.WriteTo(buffer)` works with an array.

### Who fills what

Step 8a of the frame, in `AtlasFrameLoop.LateUpdate` (lane A §7.2):

| Field | Written by | From |
|---|---|---|
| `flags`, `sceneId`, `spaceId`, `worldVer`, `headPos`, `headRot`, `bumpLevel` | `AtlasFrameLoop.FillState` (lane A) | `FrameContext` (raw head pose, tracking, pose-jump flag), `IBumpGuard.Level`, whether a cue is showing, `IQualityState.Tier`, `demoLock`, `ISpaceHost` |
| `p[0]`, `p[1]`, `p[2]` | `AtlasFrameLoop.FillState` | `(float)IWalkerKernel.MetresPerUnit`, `CullRho − 1`, `CullRho`, in every space (lane B §8.3) |
| `tileId`, `sheet`, `xform`, `p[3]` to `p[7]` | The entered space, in `ISpaceModule.WriteState(ref StateMessage state)` | Its own state. On entry they hold six zeros, 0, identity and zeros |
| `seq`, `t_us` | Lane D's `IStateSink`, through `LinkClient.SendState` | Its counter and its clock, at the send. Whatever the caller put there is overwritten |

`FillState` starts from `StateMessage.Identity`. `WriteState` must not allocate and must write the values the
frame was drawn with. With no space entered the defaults are sent, with `spaceId` 255 and `sceneId` 0xFFFF.

| Space | `tileId` | `sheet` | `xform` | `p[3]` to `p[7]` | Source |
|---|---|---|---|---|---|
| S0 Home | six zeros | 0 | identity | zeros | B.1 |
| S1 Glued | `(ci, cj, 0, 0, 0, 0)`: the lattice cell the head is in | 0 | identity | mode, copies per side, cell size x, cell size z, 0 | lane A §14.1 |
| S2 Curved Walk | `Pose.Tile` | 0 | `Pose.Local` | lane B, when it lands | lane B §8.3 |
| S3 Aligned Cells | `Pose.Tile` | Mode 2 cell index | `Pose.Local` | lane B | lane B §8.3 |
| S4 Small Planet | `Pose.Tile` (the face) | 0 | `Pose.Local` | lane B | B.1 |
| S5 Impossible Rooms | `(room id, 0, 0, 0, 0, 0)` | current room id | identity | layout, room count, portal depth, 0, 0 | lane A §14.2 |
| S6 Slicer | six zeros | 0 | identity | w₀; the XW, YW, ZW angles in radians; object + 16 · view | lane A §14.3 |

A curved space writes `state.SetTile(t.Xu, t.Xv, t.Yu, t.Yv, t.Tu, t.Tv)` and
`state.SetXform(u.M00, u.M01, u.M02, u.M10, u.M11, u.M12, u.M20, u.M21, u.M22)` with `t = kernel.Pose.Tile` and
`u = kernel.Pose.Local` (lane B §2.4, §2.2).

What reaches the Spectator, and how:

- Per-frame values travel in `State`. A space that also implements `ISpaceStateReader` (lane A §2) gets the
  newest `State` before each `Tick` in the Spectator and takes its per-frame values (`p[3..7]`, `tileId`,
  `sheet`) from it.
- Everything slower travels as parameters: `ParamManifest`, then one `param.set` event per change (§2.6). The
  Spectator applies them to its own registry by id.
- `worldVer` is `ISpaceHost.WorldVersion` (lane A §4). Every change of it is followed by one `world` event
  (§2.6), which carries the world hash.

### The 202-byte example

A `State` as S3 Aligned Cells would send it once lane B has landed. It is the golden vector of `Link_RoundTrip`
(`Tests/Link/Golden/state.hex`).

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

All follow the pattern of `StateMessage`: `const byte Id`, a size, `int WriteTo(Span<byte>)`,
`static bool ReadFrom(ReadOnlySpan<byte>, out T)`.

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
public struct TPingMessage { public const byte Id = MsgId.TPing; public const int Size = 13;
                             public uint Seq; public long T1Us; /* WriteTo, ReadFrom */ }
public struct TPongMessage { public const byte Id = MsgId.TPong; public const int Size = 29;
                             public uint Seq; public long T1Us, T2Us, T3Us; /* WriteTo, ReadFrom */ }
```

### `ParamSet` (0x21, 2 + 6 n bytes)

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `msgType` | u8 | 0x21 |
| 1 | `count` | u8 | n = 1 … 255 |
| 2 + 6 i | `id` | u16 | parameter index (1.1) |
| 4 + 6 i | `value` | f32 | requested value; the phone clamps it |

Example, `render.scale` (index 3) to 0.7 and `curved.cullRho` (index 16) to 3:
`21 02 03 00 33 33 33 3F 10 00 00 00 40 40` (14 B).

```csharp
public static class ParamSetMessage
{
    public const byte Id = MsgId.ParamSet;
    public const int MaxEntries = 255;
    public static int SizeOf(int count);                                       // 2 + 6 * count
    public static int Begin(Span<byte> destination, int count);               // writes id and count, returns 2
    public static int Put(Span<byte> destination, int offset, ushort index, float value);   // returns the next offset
    public static bool TryReadCount(ReadOnlySpan<byte> source, out int count); // false when too short for count
    public static void ReadEntry(ReadOnlySpan<byte> source, int i, out ushort index, out float value);
}
```

The phone applies the entries in order with `registry.Set(handle, value, ParamSource.Link)`. An index it does not
know is skipped. The whole message is dropped under `demoLock` and while DEGRADED (§2.8).

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
| 69 | `visibleTiles` | u16 | | tiles drawn (lane B); 0 in this build |
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
    public const byte Id = MsgId.Telemetry;
    public const int Size = 91;
    public long TimeUs; public uint Seq; public ushort SceneId; public byte SpaceId; public byte Tier;
    public float Fps, P50, P95, P99, Max, Over17_2, Over33_8, CpuMainP95, CpuRenderP95, GpuP95, RenderScale;
    public ushort DrawCalls, SetPass, TrisK, MemMB, VisibleTiles, Copies, RttMs, LossPermille, CueCount;
    public uint GcAllocB;
    public byte Thermal, Battery, Tracking, Msaa, StateHz, BumpMax, AssetsPlaced, AssetsPending;
    public readonly int WriteTo(Span<byte> destination);
    public static bool ReadFrom(ReadOnlySpan<byte> source, out TelemetryMessage message);
}
```

### `RoomOutline` (0x41, 27 + 8 n + 26 m bytes)

The floor plan the Spectator and the Console's map draw. Sent when a RoomModel becomes active and at every
RESYNC. All coordinates are in the room frame, metres and radians.

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `msgType` | u8 | 0x41 |
| 1 | `count` | u8 | n = 3 … 255 outline vertices |
| 2 + 8 i | `x`, `z` | 2 × f32 | `RoomModel.Outline[i]`: wall to wall, counter-clockwise seen from above |
| 2 + 8 n | `height` | f32 | `RoomModel.MaxWallHeight` |
| 6 + 8 n | `rect` | 5 × f32 | free rectangle: centre x, centre z, Wf, Df, yaw. In the room frame the first two and the last are 0 |
| 26 + 8 n | `boxCount` | u8 | m = 0 … 64 objects |
| 27 + 8 n + 26 j | `id` | u8 | `Objects[j].Id` (the link's `boxId`) |
| + 1 | `cat` | u8 | `(byte)Objects[j].Category`: lane C's `ObjectCategory`, 0 `Unknown` … 22 `Other` |
| + 2 | `cx`, `cz` | 2 × f32 | `Base.x`, `Base.z` |
| + 10 | `w`, `d`, `h` | 3 × f32 | `Size.x`, `Size.z`, `Size.y` |
| + 22 | `yaw` | f32 | `Yaw` |

A 3 × 4 m rectangle with one table is 27 + 32 + 26 = 85 B.

```csharp
public struct OutlineBox { public byte Id, Category; public float Cx, Cz, W, D, H, Yaw; }

/// <summary>A reusable buffer object: fill it, then WriteTo. It owns its arrays; nothing is allocated per send.</summary>
public sealed class RoomOutlineMessage
{
    public const byte Id = MsgId.RoomOutline;
    public const int MaxVertices = 255, MaxBoxes = 64;
    public int VertexCount;
    public readonly float[] X, Z;                     // MaxVertices each
    public float Height;
    public float RectCx, RectCz, RectWf, RectDf, RectYaw;
    public int BoxCount;
    public readonly OutlineBox[] Boxes;               // MaxBoxes
    public int Size { get; }                          // 27 + 8 * VertexCount + 26 * BoxCount
    public int WriteTo(Span<byte> destination);
    public static bool ReadFrom(ReadOnlySpan<byte> source, RoomOutlineMessage into);
}
```

`Atlas.Link` knows no RoomModel type. `Atlas.Telemetry` fills a `RoomOutlineMessage` from the RoomModel
(lane C's types) and calls `LinkClient.SendRoomOutline`: lane D's glue does it on `ISpaceHost.RoomChanged` (§5.1).

## 2.5 The JSON messages

Byte 0 is the id; the rest is one JSON object (§1.8). Every member is listed; a missing member takes the default
in brackets. Each message is a struct in `JsonMessages.cs` with `void Write(JsonWriter w)` and
`static bool TryRead(JsonValue v, out T message)`.

**`Hello` (0x01), client → Hub.** Sent first after every connect, and again whenever the room or the session
changes.

```json
{"proto":[1,0],"role":"phone","device":"iPhone16,2","app":"atlas","build":"d0e9127",
 "worldHash":"141060c2fc138145","roomId":"sample_3x3","sessionId":"20261004T153012Z-a3f9"}
```

| Member | Type | Meaning |
|---|---|---|
| `proto` | `[int, int]` | `[1, 0]` |
| `role` | string | `phone`, `spectator` or `editor` (§2.11) |
| `device` | string [`""`] | `SystemInfo.deviceModel`, or `ReplayPhone` |
| `app` | string [`"atlas"`] | |
| `build` | string [`""`] | Git short hash, or `Application.version` |
| `worldHash` | string [`""`] | 16 hexadecimal digits (below); empty when there is no world |
| `roomId` | string [`""`] | `RoomModel.Id`; empty when there is no RoomModel |
| `sessionId` | string [`""`] | Phone and editor only **(decided here)**: the session id the phone made at start (§5.5). The Hub adopts it, so the phone's files and the Hub's folder carry the same name. Empty from a spectator |

**`Welcome` (0x02), Hub → client.**

```json
{"proto":[1,0],"sessionId":"20261004T153012Z-a3f9","worldHash":"141060c2fc138145","roomId":"sample_3x3",
 "demoLock":false,"forgeUrl":"http://192.168.0.10:8000"}
```

| Member | Type | Meaning |
|---|---|---|
| `sessionId` | string | The session in force. To a phone: its own id back. To a spectator: the active source's |
| `worldHash`, `roomId` | string | To a spectator: the active source's last values. To a phone: its own, echoed |
| `demoLock` | bool [false] | True asks the phone to switch `demoLock` on. False changes nothing: a lock is never lifted by `Welcome` |
| `forgeUrl` | string [`""`] | Base URL of the Forge, no trailing slash. Empty: the client uses `http://<hub address>:8000` |

**`worldHash`.** `WorldHash.Compute(spaceId, q, seed, generatorVersion, geometryHash, lMillimetres)` (§7.3.1):

```text
text      = "<spaceId>|4,<q>|<seed>|<generatorVersion>|<geometryHash>|<lMillimetres>"     decimal integers, UTF-8
worldHash = the first 16 hexadecimal digits (lower case) of SHA-256(text)

spaceId          State.spaceId                                     q        IWalkerKernel.Graph.Q (4 in this build)
seed             the world generator's seed; 0 in this build       genVer   1 in Phase 1
geometryHash     lane C's RoomModelJson.GeometryHash(room): 64 lower-case hexadecimal digits (lane C §2.6);
                 64 zeros when no RoomModel is active
lMillimetres     round(IWalkerKernel.MetresPerUnit × 1000)
```

Lane D's glue computes it at step 8a whenever `State.worldVer` or `spaceId` changes, puts it into the next
`Hello` (`LinkClient.SetWorld`) and logs the `world` event. The Spectator computes the same from its own kernel,
room and space and compares. Test vector: space 1, q 4, seed 0, version 1, geometry hash
`9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08`, L 1000 mm → text
`1|4,4|0|1|9f86…0a08|1000` → `141060c2fc138145`.

**`ParamManifest` (0x20), phone → Hub → spectators.** Sent in RESYNC and whenever `ManifestVersion` changed
since the last one sent (checked once per second). Always the whole registry.

```json
{"ver":3,"params":[
 {"id":3,"name":"render.scale","group":"render","min":0.6,"max":0.8,"def":0.8,"val":0.8,"onRelease":true,
  "kind":"float","step":0,"flags":3,"active":true,"choices":[0.8,0.7,0.6]},
 {"id":22,"name":"glued.mode","group":"glued","min":0,"max":2,"def":0,"val":0,"onRelease":true,
  "kind":"enum","step":1,"flags":3,"active":false,"labels":["T2","Klein","T3"]}]}
```

`id`, `name`, `group`, `min`, `max`, `def`, `val`, `onRelease` are Appendix B.2. `ver`, `kind` (`float`, `int`,
`bool`, `enum`), `step`, `flags` (the `ParamFlags` bits), `active` (the group's state, 1.1), `choices` and
`labels` are added **(decided here)** so the Console can draw a switch, a selector or stepped buttons, and fold
an inactive group, with no further code.

**`SceneCmd` (0x22), Hub → phone.**

```json
{"space":"S1","variant":"klein","args":{"glued.copies":3},"n":17,"t_us":5000250}
```

| Member | Type | Meaning |
|---|---|---|
| `space` | string | `S0` … `S6` |
| `variant` | string [`""`] | A name the space understands: `t2`, `klein`, `t3` (S1); `booth`, `loop` (S5); `tesseract`, `cell5`, `cell16`, `cell24`, `tube` (S6) (lane A §14) |
| `args` | object [`{}`] | Parameter id → number, applied before the switch |
| `n` | int [0] | Hub's command counter, echoed in the `op` event **(decided here)** |
| `t_us` | int [0] | Hub clock at send, for test L10 **(decided here)** |

**`OperatorCmd` (0x23), Hub → phone.**

```json
{"cmd":"quality","args":{"tier":2},"n":18,"t_us":5000900}
```

| `cmd` | `args` | Meaning |
|---|---|---|
| `home` | `{}` | Return to Home |
| `recentre` | `{}` | Re-centre the current space on the walker |
| `quality` | `{"tier": 0 … 3}`; also −1 (automatic) and 4 (the high tier), as lane A's `SetQuality` takes them | Pin a ladder rung |
| `cue` | `{}` | The Console's **STOP** button: the warning cue at STRONG level |
| `lock` | `{"on": true \| false}` | Demo lock |

An unknown `cmd` is ignored and logged. §2.9 gives the local call each one becomes.

**`Event` (0x40), phone → Hub → spectators.** §2.6.

**`Log` (0x42), phone → Hub.** Warnings and errors from `Application.logMessageReceived`, and from lane D's own
code.

```json
{"t_us":1234567890,"level":"warning","msg":"Forge unreachable, placing the built-in set"}
```

`level` is `warning` or `error`. `msg` is cut at 500 characters. At most 10 per second are sent; the rest are
counted and reported once per second as `"<n> more suppressed"`.

**`JobStatus` (0x50), Hub → phone, spectators.** The Forge's body, relayed (§3.7).

```json
{"jobId":"j-1a2b3c4d","roomId":"sample_3x3","boxId":4,"state":"generating","source":"local","error":"",
 "queuePos":0,"triTarget":5000}
```

`state`: `queued`, `matting`, `generating`, `cleaning`, `ready`, `failed`. `source`: `local`, `hosted`, `cache`.
`error`: empty unless `failed` (codes in §3.7). `queuePos` and `triTarget` are added **(decided here)**.

**`AssetReady` (0x51), Hub → phone, spectators.**

```json
{"jobId":"j-1a2b3c4d","roomId":"sample_3x3","boxId":4,
 "sha256":"3f0a…64 hexadecimal digits…9c1e","url":"/assets/3f0a…9c1e.glb",
 "bytes":183220,"tris":4998,"maxEdge":0.2496,"bounds":[1.2,0.75,0.8],"palette":110}
```

`url` is a path on the Forge; the client prepends `forgeUrl`. `bounds` is the size of the mesh's bounding box
`[x, y, z]` in metres. `palette` is the palette entry (§4.1). `maxEdge` is in metres.

**`AssetAck` (0x52), phone → Hub.**

```json
{"jobId":"j-1a2b3c4d","boxId":4,"sha256":"3f0a…9c1e","ok":true,"loadMs":212.5,"worstFrameMs":17.25,"reason":""}
```

`reason` when `ok` is false: `download_failed`, `hash_mismatch`, `too_large`, `validate:<check>` (§4.2),
`load_failed`, `room_changed`.

**`Bye` (0x7F), both.** `{"reason":"quit"}`. Reasons in use: `quit`, `link.enabled off`, `hub shutdown`,
`replaced`, and the proto refusal of §2.2. The sender disconnects after sending; the receiver does not answer.

## 2.6 Events

Wire and file format, exactly Appendix B.2:

```json
{"t_us":1234567890,"scene":"S1","ev":"space.entered","data":{"from":"S0","to":"S1"}}
```

| Member | Type | Rule |
|---|---|---|
| `t_us` | int | Phone clock, microseconds |
| `scene` | string | `S0` … `S6` while that space is entered, `none` otherwise; a use case's name in Phase 2 |
| `ev` | string | `<area>.<name>` or one word; segments match `[a-z][A-Za-z0-9]*`; at most 40 characters |
| `data` | object | Always present, `{}` when empty; at most 2 kB |

Every event is appended to the phone's event log (§5.5) whether or not the link is up, and sent as `Event` when
it is. While OFFLINE up to 256 events wait in a queue and are sent at RESYNC; older ones are dropped from the
queue, never from the file.

Events known now. A lane adds a name by adding a row.

| `ev` | `data` | Raised by | When |
|---|---|---|---|
| `boot` | `{build, device, os}` | D | App start |
| `session` | `{sessionId}` | D | A session starts (§5.5) |
| `link.state` | `{state, prev}` | D | Watchdog state change |
| `param.set` | `{"src": "link" \| "menu" \| "operator" \| "ladder" \| "restore" \| "code", "values": {"<id>": number, …}}` | D | Every applied change: one event per frame at most, per source, with all changes of that frame. A change of a `Live` parameter by `Code` is left out. This is the echo of §7.3.2 |
| `param.ignored` | `{reason: "lock" \| "degraded", count}` | D | A `ParamSet` was dropped |
| `op` | `{cmd, args, n}` | D | An operator command was handed to `IOperatorCommands` (§2.9) |
| `space.entered` | `{from, to}` | D, from `ISpaceHost.SpaceEntered` | A space was entered |
| `space.refused` | `{to, reason: "noRoom" \| "unavailable"}` | A (SpaceLab) | `RequestSwitch` refused (lane A §4.1) |
| `space.loadFailed` | `{to}` | A (SpaceLab) | A module could not be created |
| `world` | `{worldVer, worldHash, space, roomId, geomHash, q, seed, genVer, lMm}` | D, at step 8a | `State.worldVer` or `spaceId` changed (§2.5, `worldHash`) |
| `quality.tier` | `{from, to, thermal}` | D, from `IQualityState.TierChanged` | The quality tier changed |
| `quality.critical` | `{}` | A (ladder) | The last rung was reached |
| `thermal` | `{state}` | D | `IQualityState.ThermalState` changed |
| `bump.level` | `{level, prev}` | D, from `State.bumpLevel` | The Bump Guard level changed |
| `tracking.lost`, `tracking.back` | `{}` | D, from `State.flags` | Tracking left `Normal`; tracking is `Normal` again |
| `pose.jump` | `{}` | D, from `State.flags` | A pose jump was rejected |
| `scan.state` | `{state, route}` | D's `EventTaps`, from `IScanSession.StateChanged` | Scan milestones (§11.2: "scan end → walkable Home" is the time from the scan's last state to `room.ready`) |
| `room.ready` | `{roomId, objects}` | `EventTaps`, from `IRoomContent.Built` | Home's shell and placeholder boxes exist |
| `object.mesh` | `{id, mesh}` | `EventTaps`, from `IRoomContent.ObjectChanged` | An object's `MeshState` changed |
| `input.action` | `{action, source, tag}` | `EventTaps`, from `IAtlasInput.ActionFired` | A walker's action fired (the numeric values of lane C §13.1) |
| `asset` | `{boxId, step, jobId, sha256, ms, reason}`; `step`: `upload`, `requested`, `cached`, `ready`, `placed`, `builtin`, `failed`, `rejected` | D | Asset client (§4.3; §11.4.7 reads these) |
| `menu` | `{open}` | D | On-device menu opened or closed |
| `materials.grow` | `{capacity}` | A (`MaterialPool`) | The pool had to grow (lane A §11) |
| `fms`, `note` | `{score}`, `{text}` | Hub | Console entries (§3.5); written by the Hub with the Hub clock and `"scene": "hub"`, never sent to the phone |

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
        // IEventLog members, and for code that references Atlas.Link:
        /// <summary>Starts an event and returns the shared writer, positioned inside "data":{ . Add members, then End().
        /// Not re-entrant.</summary>
        public Atlas.Link.JsonWriter Begin(string ev);
        public void End();
    }
}
```

- `AtlasServices.TryGet<IEventLog>(out log)`; a caller that finds none skips the event.
- Calls are main thread only, cheap (one queue entry), and allowed at step 8 and outside the frame path. A space
  does not log from `Tick`.
- `Atlas.Safety` raises no event and cannot (README F1): its level reaches the log through `State.bumpLevel`.
- `Atlas.Input` cannot either; the code that reacts to an input logs.

## 2.7 Clock sync

Both clocks are monotonic microseconds from `Stopwatch` (`StopwatchClock`, zero at process start). The Hub's
clock is the reference for the phone and for the spectators (§7.2).

```text
A sends TPing(seq, t1).   B receives it at t2 and answers at once with TPong(seq, t1, t2, t3).   A receives it at t4.
rtt    = (t4 − t1) − (t3 − t2)
offset = ((t2 − t1) + (t3 − t4)) / 2                      # B's clock minus A's clock, µs, integer
discard the sample when rtt < 0 or rtt > 500 000
keep the last 16 accepted samples; best = the one with the smallest rtt (the newest on a tie)
first 8 accepted samples after a restart:   OffsetUs = best.offset
afterwards:                                 OffsetUs = OffsetUs + round(0.1 × (best.offset − OffsetUs))
```

- Schedule on a client: 8 pings 50 ms apart at connect, then one per second.
- The Hub also pings every client, 4 times per second **(decided here)**. That is its heartbeat (§2.8) and gives
  the Hub its own offset and RTT per client, used for the latency figures of tests L4 and L10.
- `t4` is read when `Poll` dispatches the pong, so on the phone it can be up to one frame late. The
  minimum-RTT rule is what removes that delay.
- Example: `t1` 1 000 000, `t2` 5 000 250, `t3` 5 000 300, `t4` 1 000 650 → `rtt` 600 µs, `offset` 3 999 950 µs.
- LiteNetLib's `RoundTripTime` feeds the link indicator only. Its NTP helper is not used.

```csharp
// Files: ATLAS/Assets/Atlas/Link/Transport/LinkClock.cs, ClockSync.cs
namespace Atlas.Link
{
    public interface ILinkClock { long NowUs { get; } }
    public sealed class StopwatchClock : ILinkClock { public long NowUs { get; } }

    /// <summary>Engine-free. Reads no clock: every time is an argument.</summary>
    public sealed class ClockSync
    {
        public const int BurstCount = 8, BurstSpacingMs = 50, SteadyPeriodMs = 1000, Window = 16;
        public const double Alpha = 0.1;
        public const long MaxRttUs = 500000;

        public bool HasOffset { get; }
        public long OffsetUs { get; }                 // remote clock minus local clock
        public long RttUs { get; }                    // of the best sample in the window
        public bool BurstDone { get; }                // 8 samples accepted, or 600 ms since Restart
        public void Restart(long nowUs);
        public bool ShouldPing(long nowUs, out TPingMessage ping);   // true when one is due; fills seq and t1
        public void OnPong(in TPongMessage pong, long t4Us);
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
| `Offline` | No peer: never connected, timed out after 3,000 ms of silence, or closed by `Bye` | The app is unchanged. Reconnect attempts with back-off. The asset client polls the Forge (§4.3) |
| `Resync` | Connected a moment ago | The sequence below; `ParamSet` is accepted |

- "A packet" is any LiteNetLib packet from the peer: `silence = peer.TimeSinceLastPacket` (milliseconds). The
  Hub's 4 Hz ping makes the longest healthy gap 250 ms, so "two missed pings" (§7.2) is the 500 ms above.
- **Back-off.** One attempt is one `Connect` call; it has failed after 500 ms (§2.1). The wait between the end
  of a failed attempt and the next one is 0.5, 1, 2, 4, 4, … s. A successful connect resets it to 0.5 s.
  `Reconnect()`, a new Hub address and a return from the background (`OnApplicationPause(false)` with no peer)
  start an attempt at once and reset the back-off.
- **Resync**, in order: (1) client sends `Hello`; (2) Hub answers `Welcome`, or `Bye`; no answer in 2 s → the
  client disconnects and is `Offline`; (3) the clock burst, 8 pings; (4) the client sends `ParamManifest`,
  `RoomOutline` if a RoomModel is active, the last `world` event, then its queued events and up to 600 `Telemetry`
  rows it could not send; (5) the client is `Connected`; (6) on the manifest the Hub sends its last parameter
  snapshot as one `ParamSet` (matched by id) and every `AssetReady` of this room that has no `AssetAck`.
- With `link.enabled` off there is no socket and the state is `Offline`.

```csharp
// File: ATLAS/Assets/Atlas/Link/Transport/LinkWatchdog.cs
namespace Atlas.Link
{
    public enum LinkState : byte { Offline = 0, Resync = 1, Connected = 2, Degraded = 3 }

    /// <summary>Engine-free state machine. Reads no clock and no socket.</summary>
    public sealed class LinkWatchdog
    {
        public const int DegradedAfterMs = 500, HealthyHoldMs = 2000, OfflineAfterMs = 3000, AttemptMs = 500;
        public static int BackoffMs(int failedAttempts);        // 0 → 500, 1 → 1000, 2 → 2000, 3 and more → 4000

        public LinkState State { get; }
        public int FailedAttempts { get; }
        public void OnAttemptStarted(long nowMs);
        public void OnPeerConnected(long nowMs);                // → Resync
        public void OnResyncDone(long nowMs);                   // → Connected
        public void OnPeerDisconnected(long nowMs);             // → Offline; also a failed attempt
        public void Tick(long nowMs, int silenceMs);            // Connected ↔ Degraded
        public bool ShouldConnect(long nowMs);                  // Offline and the back-off has run out
        public void ResetBackoff(long nowMs);                   // the next ShouldConnect is true
    }
}
```

**Link indicator** (§7.2). In hand: a state dot (green `Connected`, amber `Degraded` or `Resync`, grey
`Offline`), Hub address, RTT, loss, Forge reachability, "Reconnect", "Change IP" (§5.6). In the headset: one
dot at the lower edge of both eyes, refreshed at 2 Hz, hidden while `Connected` under demo lock (§5.4).

## 2.9 The six operator commands

Exactly six (§7.3.2): switch space (`SceneCmd`), and `OperatorCmd` `home`, `recentre`, `quality`, `cue`, `lock`.
The path has three steps, and no link type goes further than the second:

```text
LinkClient (Atlas.Link)  ──ILinkCommandSink──▶  OperatorBridge (Atlas.Telemetry)  ──IOperatorCommands──▶  CommandRouter (Atlas.App)
   parses the JSON                                 translates, logs the "op" event                       calls SpaceLab, the ladder, BumpCue.Fire
```

`IOperatorCommands` is lane A's interface in `Atlas.World` (lane A §7.1) and has no link type. `CommandRouter`
is the handler of §6.2 that turns `cue` into `BumpCue.Fire`, so `Atlas.Safety` never sees a link type.

```csharp
// File: ATLAS/Assets/Atlas/Link/Transport/LinkSinks.cs
namespace Atlas.Link
{
    public enum OperatorVerb : byte { Unknown = 0, Home = 1, Recentre = 2, Quality = 3, Cue = 4, Lock = 5 }

    public struct SceneCmd
    {
        public byte Space;                  // 0..6
        public string Variant;              // "" when none
        public JsonValue Args;              // an Object; JsonValue.Null when absent
        public uint N; public long HubTimeUs;
    }

    public struct OperatorCmd
    {
        public OperatorVerb Verb;
        public int Tier;                    // quality: -1..4
        public bool On;                     // lock
        public JsonValue Args;
        public uint N; public long HubTimeUs;
    }

    /// <summary>Called on the main thread from LinkClient.Poll, at step 8b.</summary>
    public interface ILinkCommandSink
    {
        void OnSceneCmd(in SceneCmd cmd);
        void OnOperatorCmd(in OperatorCmd cmd);
    }
}
```

`Atlas.Telemetry.OperatorBridge : ILinkCommandSink` takes `IOperatorCommands` from `AtlasServices` (absent in the
Spectator, where commands are ignored) and does this:

| Command | `OperatorBridge` | `CommandRouter` then (lane A §7.2) |
|---|---|---|
| `SceneCmd` | For each member of `args`: `registry.TrySet(id, value, ParamSource.Operator, out _)`. Then `SwitchSpace((SpaceId)Space, Variant)` | Sets the variant's parameter, then `ISpaceHost.RequestSwitch` |
| `home` | `Home()` | `ISpaceHost.ReturnHome()` |
| `recentre` | `Recentre()` | `IFrameLoop.ResetWalkerToFloor()`, then lane C's recentre entry point if there is one |
| `quality` | `SetQuality(Tier)` | Sets `thermal.pinTier` |
| `cue` | `Cue()` | `BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator)` (lane C §8.8): the same local entry point Bump Guard uses (`P1-C14`). Indicator, tone and continuous haptic; the world keeps rendering; nothing freezes |
| `lock` | `SetLock(On)` | `registry.Set(registry.Find(AtlasParams.DemoLock), on ? 1 : 0, ParamSource.Operator)` |

- After each command `OperatorBridge` logs one `op` event with `cmd`, `args` and `n`. Test L10 compares its
  `t_us` (clock-corrected) with the Hub's send time: 250 ms at most.
- A command is handed over at step 8b of the frame in which it is polled, so it takes effect in the next frame.
  Nothing in steps 1 to 7 waits for it.
- All six work under `demoLock`. The lock stops `ParamSet` only.
- `cue` is an extra trigger, never the only one (§7.5 rule 1). With no link, Bump Guard is unchanged. An
  operator cue is held for lane C's `OperatorHoldSeconds` (3.0 s); the command carries no duration.

## 2.10 `LinkClient`

One class for the phone, the Spectator and an Editor. It owns the socket, the watchdog, the clock and the
resync. Its public API has no LiteNetLib type, so only `Atlas.Link` references LiteNetLib.

```csharp
// File: ATLAS/Assets/Atlas/Link/Transport/LinkClient.cs
using System;

namespace Atlas.Link
{
    public enum LinkRole : byte { Phone = 0, Spectator = 1, Editor = 2 }
    public enum LogLevel : byte { Warning = 0, Error = 1 }

    public struct LinkIdentity { public string Device, App, Build, SessionId; }

    /// <summary>A copy of the link's state for a UI. Strings are shared, nothing is allocated.</summary>
    public struct LinkStatus
    {
        public LinkState State; public bool Enabled;
        public string HubAddress; public int HubPort;
        public int RttMs; public int LossPermille; public float StateHz;
        public bool ClockSynced; public long ClockOffsetUs;       // Hub clock minus local clock
        public string SessionId; public string ForgeUrl; public string LastError;
        public int UnknownMessages;
    }

    public sealed class LinkClient : IDisposable
    {
        public LinkClient(LinkRole role, ParamRegistry registry, LinkIdentity identity, ILinkClock clock = null);

        public LinkState State { get; }
        public bool Enabled { get; set; }                         // follows link.enabled; false closes the socket
        public ILinkClock Clock { get; }
        public void GetStatus(out LinkStatus status);
        public event Action<LinkState, LinkState> StateChanged;   // (previous, current), main thread
        public event Action<string, int> ParamSetIgnored;         // (reason "lock" or "degraded", entries dropped)

        public void Start(string hubAddress, int port = LinkProto.HubUdpPort);   // opens the socket, first attempt
        public void Stop(string reason = "quit");                 // Bye, then closes
        public void Reconnect();                                  // attempt now, back-off reset
        public void SetHubAddress(string hubAddress, int port = LinkProto.HubUdpPort);   // then Reconnect
        public void SetWorld(string worldHash, string roomId);    // goes into the next Hello; re-sends Hello when connected
        public void SetSession(string sessionId);                 // likewise
        public void OnApplicationPause(bool paused);

        /// <summary>Step 8b, once per frame: PollEvents, watchdog, clock pings, back-off, resync steps.
        /// Received ParamSets are applied to the registry here; commands and asset messages reach their sinks here.</summary>
        public void Poll();

        // ---- phone → Hub. Each returns false, and sends nothing, when there is no peer. -----------
        public bool SendState(ref StateMessage state);            // stamps Seq and TimeUs; channel 1; TriggerUpdate
        public bool SendTelemetry(in TelemetryMessage row);
        public bool SendRoomOutline(RoomOutlineMessage outline);  // also kept and re-sent at every Resync
        public bool SendEvent(long timeUs, string scene, string ev, JsonWriter data);   // data: a complete object, or null for {}
        public bool SendLog(LogLevel level, string message);
        public bool SendAssetAck(in AssetAck ack);

        // ---- Hub → client ----------------------------------------------------------------------
        public ILinkCommandSink CommandSink { get; set; }         // SceneCmd, OperatorCmd (phone, editor)
        public ILinkAssetSink AssetSink { get; set; }             // JobStatus, AssetReady (every role)
        public ILinkMirrorSink MirrorSink { get; set; }           // relayed State, outline, manifest, events (spectator)

        public void Dispose();
    }

    public interface ILinkAssetSink
    {
        void OnJobStatus(in JobStatus status);
        void OnAssetReady(in AssetReady ready);
    }

    public interface ILinkMirrorSink
    {
        void OnWelcome(in Welcome welcome);
        void OnState(in StateMessage state, long localReceiveUs);
        void OnRoomOutline(RoomOutlineMessage outline);           // valid during the call only
        void OnParamManifest(JsonValue manifest);
        void OnEvent(long timeUs, string scene, string ev, JsonValue data);
    }
}
```

| Topic | Rule |
|---|---|
| `SendState` rate | The caller offers one `State` per frame. `LinkClient` sends it when at least `1 / link.stateHz` − 2 ms has passed since the last one sent, so 60, 30 and 10 Hz hold at any frame rate |
| `ParamSet` | Applied in `Poll` with `ParamSource.Link` when the state is `Connected` or `Resync` and `demoLock` is off; otherwise dropped, and `ParamSetIgnored` is raised (lane D's glue logs `param.ignored`) |
| `ParamManifest` | Sent by `LinkClient` itself, from the registry it was given: in Resync and when `ManifestVersion` changes |
| Echo | Lane D's glue subscribes to `registry.Changed` and logs the batched `param.set` event of §2.6; `LinkClient` only carries it |
| Allocation | `Poll`, `SendState`, `SendTelemetry` and the clock allocate nothing in steady state. JSON sends and receives may allocate; they happen on demand |
| Failure | No public member throws for a network reason. A socket error sets `LastError` and the state |
| Offline cost | With no peer `SendState` returns at once. `Poll` costs one `PollEvents` call |

## 2.11 `LinkHost`, roles and the loopback link

```csharp
// Files: ATLAS/Assets/Atlas/Link/Transport/LinkHost.cs, LoopbackLink.cs
using System;
using System.Collections.Generic;

namespace Atlas.Link
{
    public struct LinkPeerInfo
    {
        public int PeerId; public LinkRole Role; public string Device, Build, Address;
        public int RttMs; public long ClockOffsetUs; public bool Active;      // Active: the source in force
    }

    /// <summary>What the owner of a LinkHost implements: the Hub, a Unity host scene, a test.</summary>
    public interface ILinkHostHandler
    {
        /// <summary>Return false to refuse; refuseReason goes out as Bye. LinkHost has already checked proto.</summary>
        bool OnHello(int peerId, in Hello hello, out Welcome welcome, out string refuseReason);
        void OnPeerLeft(int peerId, string reason);
        void OnState(int peerId, ReadOnlySpan<byte> message, long hubReceiveUs);     // the 202 bytes, for relay and log
        void OnTelemetry(int peerId, in TelemetryMessage row);
        void OnRoomOutline(int peerId, ReadOnlySpan<byte> message);
        void OnJson(int peerId, byte msgId, ReadOnlySpan<byte> utf8Json);            // every JSON message but Hello
    }

    public sealed class LinkHost : IDisposable
    {
        public const int HeartbeatMs = 250;

        public LinkHost(ILinkHostHandler handler, int port = LinkProto.HubUdpPort,
                        string key = LinkProto.Key, ILinkClock clock = null);
        public bool Start();                           // false when the port is taken
        public void Stop();                            // Bye {"reason":"hub shutdown"} to every peer
        public void Poll();                            // call every millisecond
        public int Port { get; }
        public ILinkClock Clock { get; }
        public int PeerCount { get; }
        public void GetPeers(List<LinkPeerInfo> into);
        public bool TryGetPeer(int peerId, out LinkPeerInfo info);

        public bool Send(int peerId, ReadOnlySpan<byte> message, byte channel);          // ch 0 reliable ordered, ch 1 sequenced
        public int SendToRole(LinkRole role, ReadOnlySpan<byte> message, byte channel);  // returns peers reached
        public void Disconnect(int peerId, string reason);

        /// <summary>Loss in percent, latency range in ms, on everything this host receives. Returns false, and does
        /// nothing, when LiteNetLib was built without DEBUG or SIMULATE_NETWORK (see below).</summary>
        public bool SimulateNetwork(int lossPercent, int minLatencyMs, int maxLatencyMs);
        public void Dispose();
    }

    /// <summary>A LinkHost and a phone LinkClient joined over 127.0.0.1 in one process: the "loopback link" stub
    /// of gate G0 and the fixture of the link tests.</summary>
    public sealed class LoopbackLink : IDisposable
    {
        public LoopbackLink(ParamRegistry registry, ILinkHostHandler handler = null);    // a free port; null: accept everyone
        public LinkHost Host { get; }
        public LinkClient Client { get; }
        public void Pump(int milliseconds);            // polls both ends for that long
        public void Dispose();
    }
}
```

**Loss and latency simulation** (§7.6, tests L9 and L12). LiteNetLib compiles `SimulatePacketLoss` and
`SimulateLatency` only under `DEBUG` or `SIMULATE_NETWORK`. The Unity Editor and development builds define
`DEBUG`, so the simulation works there and in the headless tests. The NuGet release build defines neither. The
Hub therefore has a second build configuration, `SimNet`, that compiles LiteNetLib 2.1.4 from source with
`/p:SimulateNetwork=true` **(decided here)**; `--sim-loss` and `--sim-latency` are refused in the normal
configuration with a message that says so (§3.1).

`LinkHost` does by itself: the key check, `Hello` / `Welcome` and the proto refusal, `TPong` answers, the 4 Hz
heartbeat `TPing` with one `ClockSync` per peer, and dropping unknown ids. Everything else is the handler's.

| Role | Who | Transport | Sends | Receives |
|---|---|---|---|---|
| `phone` | The ATLAS app on a phone; ReplayPhone | LiteNetLib | `Hello`, `State`, `Telemetry`, `ParamManifest`, `RoomOutline`, `Event`, `Log`, `AssetAck`, `TPing`, `TPong`, `Bye` | `Welcome`, `ParamSet`, `SceneCmd`, `OperatorCmd`, `JobStatus`, `AssetReady`, `TPing`, `TPong`, `Bye` |
| `editor` | The ATLAS app in an Editor in play mode | LiteNetLib | as `phone` | as `phone` |
| `spectator` | The Spectator build, or an Editor running `PC_Spectator` | LiteNetLib | `Hello`, `TPing`, `TPong`, `Log`, `Bye` | `Welcome`, relayed `State`, `RoomOutline`, `ParamManifest`, `Event`, `JobStatus`, `AssetReady`, `TPing`, `TPong`, `Bye` |
| console | A browser on `http://<station>:8080/` | WebSocket `/ws` (§3.3). It is not a LiteNetLib peer and sends no `Hello` | `param`, `scene`, `op` and the frames of §3.3 | JSON digests |

**The active source (decided here).** `phone` and `editor` peers are sources. One source is active at a time:
the first `phone` to connect; an `editor` only while no `phone` is connected. The active source's `State` is
relayed, and it alone receives commands and `ParamSet`. A second phone is accepted and waits (its `State` is
logged, not relayed). When the active source leaves, the longest-connected remaining source becomes active. The
Console can switch by hand (§3.3). This is what lets phone B stand by during a hot swap.

A message from a role that may not send it is dropped and counted.

## 2.12 ReplayPhone and `state.bin`

`state.bin` (Hub log, §3.4) is the replay source (R4 §6.5, Appendix C.8).

```text
header, 16 bytes:  "ATLSTATE" (8 ASCII bytes)   version u16 = 1   recordSize u16 = 210   reserved u32 = 0
record, 210 bytes: hubReceive_us i64 (Hub clock)   then the 202 bytes of the State as received
```

`tools/ReplayPhone` is a .NET 8 console that compiles the `Atlas.Link` sources:

```text
dotnet run --project tools/ReplayPhone -- <session folder or state.bin> [--hub 127.0.0.1:9050] [--loop] [--speed 1.0]
```

- Connects as role `phone`, device `ReplayPhone`, with a new session id.
- From the same folder, when present: sends `manifest.json` as `ParamManifest`, `outline.bin` as `RoomOutline`,
  and the `world` events of `events.jsonl`.
- Sends each record at its original spacing (`hubReceive_us` differences ÷ `--speed`). It rewrites `seq` and
  `t_us` to its own counter and clock and leaves every other byte alone.
- It ignores `ParamSet` and commands, and logs them. It needs no Forge.

The Editor twin: the ATLAS app in play mode runs the same `LinkClient` as role `editor` against a Hub on
`127.0.0.1` (C.8). A recorded pose log (lane C) drives it with no phone.

## 2.13 Link rules

§7.5, with the place each one is enforced.

| # | Rule | Enforced by |
|---|---|---|
| 1 | Safety never touches the network. Bump Guard, tracking, the kernel and rendering never read link state. `cue` is an extra trigger, never the only one | `Atlas.Safety` references neither `Atlas.Link` nor `Atlas.Telemetry` (README F1, test `Safety_Independence`). Operator commands end in `Atlas.App.CommandRouter` (§2.9) |
| 2 | Nothing in `Update`, `LateUpdate` or a render callback waits for the link | Sockets on LiteNetLib's threads. HTTP through `UnityWebRequest`, never awaited in a frame. Hashing, GLB checks and file writes on worker threads (§4.3, §5.5). Received commands are applied at step 8b |
| 3 | Every remote parameter is clamped on the phone. Bump Guard has no registry parameter and no off switch | `ParamRegistry.Set` (1.3) |
| 4 | Hub unplugged: the app behaves identically apart from the link indicator. Forge unplugged: apart from the furniture, which is the built-in set | Tests L13, L14, L19 |
| 5 | A receiver ignores unknown ids and trailing bytes; a `proto` major mismatch is refused with a readable reason | §2.2 |
| 6 | `Atlas.Link` allocates 0 B per frame in steady state | §2.10; test L13 |

---

# Part 3 · Station HTTP

Two processes on the Station (§7.1). The Hub ends the LiteNetLib link and serves the Console. The Forge owns the
GPU and never sees live pose. Default ports: Hub UDP 9050 and TCP 8080, Forge TCP 8000, all bound to `0.0.0.0`.

## 3.1 Hub: process

```
station/hub/
  AtlasHub.csproj     net8.0, Microsoft.NET.Sdk.Web. NuGet: LiteNetLib 2.1.4, QRCoder (MIT).
                      <Compile Include="../../ATLAS/Assets/Atlas/Link/**/*.cs" />  (the Atlas.Link sources, linked)
                      Configuration SimNet: LiteNetLib from source with SIMULATE_NETWORK (§2.11)
  Program.cs          options, Kestrel, the main loop
  HubCore.cs          ILinkHostHandler: roles, active source, relay, snapshot, session (§3.2)
  HubLogs.cs          the files of §3.4
  ConsoleSocket.cs    /ws: fan-out to browsers, the 10 Hz digest
  Presets.cs          presets/*.json, load, bake
  ForgeWatch.cs       GET /health every 2 s; POST /internal/job
  Pairing.cs          /pair, the QR code
  wwwroot/            index.html, console.js, console.css, uPlot (vendored, MIT): the Console
  presets/            <name>.json
  logs/<session>/     §3.4 (not committed)
station/setup_firewall.ps1    the three rules of Appendix C.7
```

Run: `dotnet run --project station/hub -- [options]`.

| Option | Default | Meaning |
|---|---|---|
| `--udp <port>` | 9050 | LiteNetLib listener |
| `--http <port>` | 8080 | Kestrel, on `0.0.0.0` |
| `--forge <url>` | `http://127.0.0.1:8000` | The Forge this Hub watches. Clients are told `http://<advertised address>:8000` in `Welcome.forgeUrl` |
| `--advertise <ip>` | the first private IPv4 address of the machine | The address in the QR code and in `forgeUrl` |
| `--logs <dir>` | `logs` | Parent of the session folders; on an encrypted volume (C.7) |
| `--presets <dir>` | `presets` | |
| `--project <dir>` | `../../ATLAS` | Where Bake writes `defaults.json` (1.7) |
| `--demo-lock` | off | `Welcome.demoLock` is true |
| `--sim-loss <percent>`, `--sim-latency <min>-<max>` | off | `SimNet` configuration only (§2.11) |

Threads: one main loop (`host.Poll()`, timers, file appends, then `Thread.Sleep(1)`); Kestrel's pool for HTTP and
WebSocket. The two sides exchange work through concurrent queues; no handler touches a LiteNetLib object.

At start the Hub prints its addresses, the pairing URL and the session folder. With no packet after 10 s it
prints "no packets received yet: check the firewall rule for UDP 9050" (R4 §9).

## 3.2 Hub: what it does with each message

| From | Message | Hub |
|---|---|---|
| Active source | `State` | Relays the 202 bytes unchanged to every spectator (channel 1, sequenced). Appends to `state.bin`. Updates rate, loss (from `seq`) and latency (`hubReceive − ToLocal(t_us)`). Feeds the 10 Hz digest. Target: relay adds under 2 ms (`P1-D02`) |
| Active source | `Telemetry` | One line in `perf.csv` (rows with a `seq` already written are skipped). WebSocket `telemetry` |
| Active source | `ParamManifest` | Keeps it. Writes `manifest.json`. WebSocket `manifest`. Relays to spectators. Then the snapshot replay of §2.8 |
| Active source | `Event` | One line in `events.jsonl`. WebSocket `event`. Relays to spectators |
| Active source | `Log` | One line in `phone.log`. WebSocket `log` |
| Active source | `RoomOutline` | Keeps it. Writes `outline.bin`. Relays to spectators. WebSocket `outline` |
| Active source | `AssetAck` | Marks the `AssetReady` acknowledged. `events.jsonl`. WebSocket `job` |
| Waiting source | `State`, `Telemetry` | Logged to its own session folder; not relayed |
| Forge (`/internal/job`) | `JobStatus`, `AssetReady` | Sends to the active source and to every spectator (channel 0). WebSocket `job`. Keeps each `AssetReady` until its `AssetAck` |
| Console | `param` | `ParamSet` to the active source. Stores the value in the snapshot, by id |
| Console | `scene`, `op` | `SceneCmd` / `OperatorCmd` to the active source, with `n` and `t_us` added. One line in `hub.log` |
| New spectator | `Hello` | `Welcome` with the active source's session, `worldHash` and `roomId`; then the kept `ParamManifest`, `RoomOutline`, last `world` event and unacknowledged `AssetReady` |
| Any client | `TPing` | `TPong` at once |
| Timer, 250 ms | – | `TPing` to every client |
| Any client | `Bye`, time-out | Removes the peer; a new active source is chosen (§2.11); WebSocket `link` |

The Hub restarts cleanly: it keeps nothing a client cannot send again. Every client reconnects within 5 s
(`P1-D02`); the phone's `Hello` carries its session id, so the Hub appends to the same session folder.

## 3.3 Hub: HTTP and WebSocket (port 8080)

Appendix B.3. Rows marked ✚ are added here **(decided here)**. JSON bodies are UTF-8; errors are
`{"error": "<text>"}` with the status shown.

| Route | Request | Response |
|---|---|---|
| `GET /` | – | 200 `text/html`: the Console (§3.5) |
| `GET /pair` | optional `?ip=<address>` | 200 `text/html`: the pairing page (§3.5) |
| ✚ `GET /pair/qr.svg` | optional `?ip=` | 200 `image/svg+xml`: the QR code of `atlas://<ip>:9050` |
| `WS /ws` | frames below | frames below |
| `GET /api/session` | – | 200 `{"sessionId": "…", "phone": {"state": "connected" \| "degraded" \| "offline", "rttMs": 3, "loss": 0.2}, "spectators": 1, "forge": {"up": true, "queue": 2, "debugKeep": false, "hosted": false}}`. `loss` is the percentage of `State` lost in the last 5 s. `debugKeep` true makes the Console show the red "crops kept" banner (§7.4). ✚ `hosted` (§3.10) |
| `GET /api/presets` | – | 200 `{"presets": [{"name": "demo", "count": 12, "modified": "2026-10-04T15:30:12Z"}]}` |
| `POST /api/presets/{name}` | `{"values": {"<id>": number, …}}`; `name` matches `[A-Za-z0-9_-]{1,40}` | 204; 400 for a bad name or body |
| ✚ `GET /api/presets/{name}` | – | 200 `{"name": "demo", "values": {…}}`; 404 |
| ✚ `POST /api/presets/{name}/load` | – | 204: every value sent to the phone in **one** `ParamSet`; 404; 409 when no phone is connected |
| ✚ `POST /api/presets/{name}/bake` | – | 204: the preset written to `<project>/Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json` (1.7); 404 |
| `GET /logs/{session}/perf.csv` | – | 200 `text/csv`; 404 |
| ✚ `PUT /logs/{session}/perf.csv` | the phone's CSV | 204: rows the Hub lacks are merged by `seq` (§12.2, "CSV on the phone, uploaded over HTTP later") |
| ✚ `GET /api/sessions` | – | 200 `{"sessions": ["20261004T153012Z-a3f9", …]}`, newest first |
| `POST /internal/job` | A `JobStatus` or an `AssetReady` body (§2.5); a body with `sha256` and `url` is an `AssetReady` | 204. **Loopback only**: 403 from any other address |

WebSocket frames are JSON objects with a member `t`.

| Direction | Frame | Meaning |
|---|---|---|
| Console → Hub | `{"t": "param", "id": 3, "value": 0.7}` | Set a parameter by index. `"name": "render.scale"` may stand in for `id` ✚ |
| Console → Hub | `{"t": "scene", "space": "S1", "variant": "klein", "args": {}}` | `SceneCmd` |
| Console → Hub | `{"t": "op", "cmd": "cue", "args": {}}` | `OperatorCmd` |
| Console → Hub ✚ | `{"t": "fms", "score": 3}` · `{"t": "note", "text": "…"}` | Comfort score (0–20) and operator note; written to `events.jsonl` |
| Console → Hub ✚ | `{"t": "source", "peer": 2}` | Make that source active (§2.11) |
| Hub → Console | `{"t": "manifest", "ver": 3, "params": […]}` | The `ParamManifest` body; on connect and on change |
| Hub → Console | `{"t": "telemetry", "t_us": …, "seq": …, …}` | One per row; the members are the CSV column names of §5.3 |
| Hub → Console | `{"t": "state", …}` | 10 Hz digest, below |
| Hub → Console | `{"t": "event", …}` · `{"t": "log", …}` | The `Event` or `Log` body |
| Hub → Console | `{"t": "job", "kind": "status" \| "ready" \| "ack", …}` | The `JobStatus`, `AssetReady` or `AssetAck` body |
| Hub → Console ✚ | `{"t": "outline", "outline": [[x, z], …], "height": 2.5, "rect": {"wf": 2.4, "df": 3.4}, "boxes": [{"id": 1, "cat": "table", "cx": 0, "cz": 0.5, "w": 1.2, "d": 0.8, "h": 0.75, "yaw": 0}]}` | The `RoomOutline`, decoded |
| Hub → Console | `{"t": "link", "sessionId": "…", "phone": {"state": "connected", "rttMs": 3, "loss": 0.2, "device": "iPhone16,2", "build": "d0e9127", "address": "192.168.0.23"}, "sources": [{"peer": 1, "role": "phone", "device": "iPhone16,2", "active": true}], "spectators": 1, "forge": {"up": true, "queue": 2, "debugKeep": false, "hosted": false}}` | 1 Hz and on change |

The `state` digest, from the newest `State`:

```json
{"t":"state","seq":12345,"t_us":1234567890,"scene":3,"space":3,"tracking":0,"jump":false,"cue":true,"tier":1,
 "lock":false,"bump":1,"tile":"-1,0,0,0,0,1","sheet":3,"head":[0.25,1.5,-0.5],"yawDeg":90,
 "p":[1.885,2,3,0,0,0,0,0],"worldVer":7,"hz":59.8,"lossPct":0.2,"latencyMs":{"p50":4.1,"p95":9.8,"p99":14.2}}
```

`tile` is the six words as text (a 64-bit integer does not survive a JavaScript number). `yawDeg` is the heading
of the head's forward axis on the floor, `atan2(forward.x, forward.z)`.

## 3.4 Hub: log files

`logs/<session>/`, one folder per session id (§5.5).

| File | Content |
|---|---|
| `perf.csv` | The header of §5.3, then one line per `Telemetry` row, in `seq` order, `\n` line ends, no byte-order mark. Byte for byte the phone's file when no row was lost (test L11) |
| `state.bin` | §2.12: every `State` received, with the Hub's receive time |
| `events.jsonl` | One event per line: the phone's object with a member `hub_us` (Hub clock at receipt) added. Hub-made events (`fms`, `note`, `asset_ack`, `source`) carry `"scene": "hub"` and the Hub clock in `t_us` |
| `phone.log` | `<t_us> <level> <msg>` per `Log` |
| `hub.log` ✚ | Connects and leaves, every command and `ParamSet` sent (with `n` and Hub time), preset actions, refused or dropped messages |
| `manifest.json` ✚ | The last `ParamManifest` body |
| `outline.bin` ✚ | The last `RoomOutline` message, as received |

Motion logs are treated as health data (§13.5): the folder sits on an encrypted volume, and delete-by-id removes
it whole.

## 3.5 Console

One page (`GET /`) with four tabs and a separate pairing page. It opens one WebSocket and builds itself from the
manifest: a new parameter needs no Console code.

Always visible, on every tab: session id; phone state dot with RTT and loss; current space; Bump Guard level
bar; quality tier and thermal state; lock badge; the red **"crops kept"** banner while `forge.debugKeep` is
true; the amber **"hosted fallback armed"** banner while `forge.hosted` is true (§3.10).

| Page | Shows | Does |
|---|---|---|
| **Tuner** (`#tuner`) | One block per `group`, folded and greyed while the group is inactive; in it one control per parameter: slider (`float`, `int`), stepped buttons (`choices`), switch (`bool`), selector (`enum`). Each shows the applied value from the echo, min, max and a default mark | Dragging sends `param` at most 30 times per second; an `onRelease` parameter sends once, on release. "Reset" sends the default. Presets: save (current values), load, bake. Under lock the controls are greyed |
| **Operator** (`#operator`) | The six commands; walker's comfort score; notes; the job list with `local`, `hosted`, `cache` and `built-in` tags | Seven space buttons S0 to S6 (unavailable spaces greyed), **Home**, **Recentre**, quality tier 0–4 and "auto", **STOP** (large, red; also the space bar), **Lock** switch, `forge.backend` selector, FMS entry 0–20, note field, source selector |
| **Dashboard** (`#dashboard`) | One tile per `Telemetry` field; line charts of frame-time p95 and fps over the last 15 minutes; a thermal colour bar; Forge queue length; `State` rate, loss and latency | CSV download link |
| **Map** (`#map`) | The room from `RoomOutline`: outline, free rectangle, object boxes coloured by category and marked placeholder, requested, ready or built-in; the walker's position and heading at 10 Hz; the trail of the last 600 points; current tile or cell | – |
| **Pairing** (`GET /pair`) | The QR code of `atlas://<ip>:9050`, the same address as text, every IPv4 address of the Station as a link (`?ip=`), and whether a phone is connected | – |

## 3.6 Forge: process and routes (port 8000)

```
station/forge/
  requirements.lock        Python 3.11; FastAPI, uvicorn, pydantic v2, trimesh, rembg, gltfpack; TripoSG at a pinned commit
  forge/app.py             the routes below
  forge/jobs.py            queue, one GPU worker, lifecycle (§3.7)
  forge/store.py           rooms, assets, cache index
  forge/matting.py         rembg
  forge/generators/        __init__.py (Generator, GeneratorError), triposg.py, hosted.py, test.py (§3.8)
  forge/cleanup.py         §3.9
  forge/validate.py        the checks of §4.2
  forge/glb.py             the deterministic GLB writer
  forge/palette.json       the 64 object colours of lane A's palette, written by tools/gen_forge_palette.py (§4.1)
  forge/hub.py             POST /internal/job
  tests/
  data/                    rooms/<roomId>/room.json, mesh.bin · assets/<sha256>.glb · cache.json (not committed)
```

Run: `uvicorn forge.app:app --host 0.0.0.0 --port 8000` (C.7). One process; one worker thread owns the GPU.

| Variable | Default | Meaning |
|---|---|---|
| `FORGE_GENERATOR` | `triposg` | `triposg` or `test` |
| `FORGE_HOSTED_URL`, `FORGE_HOSTED_KEY` | unset | The hosted fallback (§3.10). Unset: no request ever leaves the Station |
| `FORGE_DEBUG_KEEP` | unset | `1` keeps crops in `data/debug/<jobId>/`; reported as `debugKeep` |
| `FORGE_HUB` | `http://127.0.0.1:8080` | Where job changes are posted |
| `FORGE_DATA` | `data` | Storage root |

Appendix B.4, with status codes. Errors are `{"error": "<code>"}`.

| Route | Request | Response |
|---|---|---|
| `GET /health` | – | 200 `{"ok": true, "gpu": "NVIDIA GeForce RTX 5070", "queue": 2, "pipelineVer": "1.0.0", "debugKeep": false, "hosted": false, "generator": "triposg"}`. `gpu` is null with no CUDA device. `queue` counts jobs not yet `ready` or `failed`. ✚ `hosted`, `generator` |
| `POST /rooms` | RoomModel JSON (`docs/contracts/roommodel.schema.json`), at most 1 MB | 201 `{"roomId": "…", "roomHash": "…"}`; 200 with the same body when the id existed (the record is replaced). 400 `bad_room` when it does not validate; 413 |
| `PUT /rooms/{roomId}/mesh` | Binary mesh, at most 2 MB | 204; 404; 413. Optional (§7.4); the app does not send it in Phase 1 |
| `GET /rooms/{roomId}` | – | 200: the exact bytes that were posted; 404 |
| `DELETE /rooms/{roomId}` | – | 204: the room record, its mesh, its jobs and the GLBs made for it are removed (§13.5); 404 |
| `POST /rooms/{roomId}/objects` | `multipart/form-data`: field `meta` (JSON, below); files `view0` (required), `view1`, `view2` (`image/jpeg`, each at most 200 kB and 768 × 768) | 202 `{"jobId": "j-1a2b3c4d"}`; or 200 `{"jobId": "…", "cached": true, "sha256": "…"}`. 400 `bad_meta`; 404; 409 `room_full` for a 21st object of a room; 413 |
| `GET /jobs/{jobId}` | – | 200: the `JobStatus` body; when `ready`, also the `AssetReady` members and `report` (§3.9); 404 |
| `GET /jobs?room={roomId}` | – | 200 `{"jobs": [ … ]}`: the same objects, largest box first |
| `GET /assets/{sha256}.glb` | – | 200 `model/gltf-binary`, `ETag: "<sha256>"`, `Cache-Control: public, max-age=31536000, immutable`; 404 |

- `roomId` is the RoomModel's `id` (lane C's schema: `[A-Za-z0-9_]{1,40}`). The body is lane C's canonical text
  (`RoomModelJson.Write`). `roomHash` is the SHA-256 of the posted bytes, 64 lower-case hexadecimal digits: a
  receipt for the upload. It is **not** the hash inside `worldHash`, which is lane C's geometry hash (§2.5).
- Lane C keeps a RoomModel file under 300 kB (§7.4; lane C §2.6). The Forge accepts up to 1 MB, so the limit
  is the phone's to enforce and a larger grid does not need a Forge change **(decided here)**.

`meta`:

```json
{"boxId": 4, "category": "table", "dims": [1.2, 0.75, 0.8], "yaw": 0.0, "triTarget": 5000, "backend": "local",
 "views": [{"score": 0.82, "azimuthDeg": 35.0, "intrinsics": [612.0, 612.0, 384.0, 384.0],
            "camToBox": [1,0,0,0, 0,1,0,0.4, 0,0,1,-1.5, 0,0,0,1]}]}
```

| Member | Type | Meaning |
|---|---|---|
| `boxId` | int 1 … 255 | `RoomObject.Id` |
| `category` | string | `ObjectCategories.Name(Category)`: `couch`, `table`, … (lane C §6) |
| `dims` | 3 numbers, m | `RoomObject.Size`: `[w, h, d]` = extent along the box's local X, Y (up), Z |
| `yaw` | number, rad | `RoomObject.Yaw` |
| `triTarget` | int 3000 … 5000 | `clamp(floor(60000 / n), 3000, 5000)`, n = objects of this room that receive meshes (plan §4.5): 5,000 up to twelve objects, 3,000 at twenty |
| `backend` | string | `local`, `hosted` or `cache`: the phone's `forge.backend`. `off` is never sent; it is a 400 |
| `views[i]` | one per file `view<i>` | Lane C's `ObjectView` (lane C §11.4). `score` 0 … 1; `azimuthDeg`: direction from the box centre to the camera in the box's local frame, `atan2(x, z)`; `intrinsics`: `fx, fy, cx, cy` of the crop, pixels; `camToBox`: 4 × 4 row by row, camera frame (Unity: +X right, +Y up, +Z forward) to the box-local frame of §4.1, metres |

## 3.7 Forge: job lifecycle and queue

| State | Work | Time-out | Next |
|---|---|---|---|
| `queued` | Cache lookup by SHA-256 of the `view0` bytes, `triTarget` and `pipelineVer`. Waits for the GPU | – | hit → `ready` (source `cache`); `backend` `cache` and miss → `failed` `cache_miss`; else `matting` |
| `matting` | Decode the JPEG; background removal (`rembg`) | 10 s | `generating`; `failed` `bad_image` or `matting_timeout` |
| `generating` | The generator (§3.8). `local`: TripoSG with `view0`, 90 s; then once more with `view1` (with `view0` again when there is none), 90 s; then the hosted service if a key is configured, 120 s. `hosted`: the hosted service at once, 120 s | as stated | `cleaning`; `failed` `generate_timeout`, `generate_failed` or `no_generator` |
| `cleaning` | §3.9, then the validator of §4.2 | 20 s | `ready`; `failed` `clean_failed` or `validate:<check>` |
| `ready` | The GLB is stored under its hash; the cache index is updated | – | – |
| `failed` | `error` holds the code | – | – |

- **Queue order:** largest box first, by volume `dims[0] · dims[1] · dims[2]`; ties in arrival order. One job on
  the GPU at a time. The order is across all rooms.
- **At most 20 objects per room** receive meshes (plan §4.5). The phone sends at most 20; the Forge refuses a 21st
  `boxId` of a room with 409.
- A second upload for a `boxId` replaces its earlier job unless that job is on the GPU, in which case the new one
  queues behind it.
- **Notification.** At every change of state the Forge posts the `JobStatus` to `FORGE_HUB/internal/job`; at
  `ready` it posts the `AssetReady` as well. If the post fails it is not retried: the phone polls (§4.3).
- **Crops** are held in memory and dropped when the job reaches `ready` or `failed`. `FORGE_DEBUG_KEEP=1` is the
  only thing that writes one to disk (§7.4, test L20).
- `jobId` is `j-` and eight hexadecimal digits. `pipelineVer` is a version string; changing it empties the cache.
- A corrupt image ends as `failed` `bad_image`; the worker goes on with the next job (`P1-D08`).
- `DELETE /rooms/{roomId}` ends that room's jobs as `failed` `cancelled`.

## 3.8 Forge: generator adapter

```python
# station/forge/forge/generators/__init__.py
from typing import Protocol
import PIL.Image, trimesh

class GeneratorError(Exception): ...

class Generator(Protocol):
    name: str      # "triposg", "hosted" or "test"
    source: str    # what JobStatus.source reports: "local" or "hosted"

    def available(self) -> bool: ...
    def generate(self, image: PIL.Image.Image, *, seed: int, timeout_s: float) -> trimesh.Trimesh: ...
```

Contract of `generate`:

- Input: the matted crop, RGBA, square, at most 768 × 768; alpha is the object.
- Output: one triangle mesh, any scale and triangle count, **+Y up, the photographed side towards +Z**. The
  adapter converts from its model's own convention.
- It returns within `timeout_s` or raises `TimeoutError`; any other failure is a `GeneratorError`.
- It writes no file and keeps no reference to the image.
- `seed` is the first four bytes of the SHA-256 of the view's JPEG, as an unsigned integer.

| Adapter | `source` | What it is | `available()` |
|---|---|---|---|
| `triposg` | `local` | TripoSG (MIT) on the RTX 5070, about 8 GB of memory, commit pinned in the lock file | Weights loaded and a CUDA device present |
| `hosted` | `hosted` | A hosted image-to-3D service: open item 8 | `FORGE_HOSTED_URL` and `FORGE_HOSTED_KEY` both set |
| `test` | `local` | Deterministic, no GPU, no network: a closed, bumpy 20,000-triangle solid whose proportions follow the crop's silhouette and whose bumps come from `seed`. For the Forge's own tests, for a Station with no GPU, and for cache tests (L18) | Always |

## 3.9 Forge: mesh clean-up and its report

In this order (§7.4, `P1-D09`). Every step is deterministic: the same raw mesh and `meta` give the same bytes,
and so the same hash.

| # | Step | Detail |
|---:|---|---|
| 1 | Repair | Weld vertices closer than 0.01 mm; drop degenerate faces; keep the components that hold at least 2 % of the area; outward normals |
| 2 | Orient | +Y up from the adapter. Yaw among 0°, 90°, 180°, 270°: the one whose bounding box best matches `dims` after a uniform scale; on a tie, the one whose +Z faces the camera of `view0` (`azimuthDeg`) |
| 3 | Scale to the box | One uniform scale, then a correction per axis of at most ± 15 %. Centre on x and z; lowest point at y = 0 |
| 4 | Decimate | To `triTarget` (quadric edge collapse) |
| 5 | Regrid | Split every edge longer than 0.25 m. If the count now exceeds `triTarget`, decimate lower and repeat, at most five rounds. If it is under 1,500, subdivide until it is not |
| 6 | Palette | The dominant colour of the matted `view0`; the nearest of the 64 object colours of `palette.json` (cells 64 to 127) in OKLab; `TEXCOORD_0` of every vertex is that cell's centre |
| 7 | Normals | Area-weighted vertex normals |
| 8 | Shade | Per vertex: ambient occlusion (64 hemisphere rays of 0.5 m, mapped to 0.35 … 1) times the face shade of lane A's `AtlasShading`, with the normal turned into the room frame by `yaw`. Written to `COLOR_0` (§4.1) |
| 9 | Export | GLB with a fixed layout and no generator stamp (§4.1); SHA-256 of the bytes |
| 10 | Validate | Every check of §4.2. A failure is `failed` `validate:<check>` |

`report`, returned by `GET /jobs/{jobId}` when `ready` and kept for §11.4.7:

```json
{"tris": 4998, "verts": 2601, "maxEdge": 0.2496, "bounds": [1.2, 0.75, 0.8], "scaleErr": 0.03, "yawDeg": 90,
 "palette": 110, "source": "local", "generator": "triposg", "pipelineVer": "1.0.0", "triTarget": 5000,
 "retries": 0, "bytes": 183220, "sha256": "3f0a…9c1e",
 "ms": {"queue": 4100, "matting": 900, "generating": 31000, "cleaning": 2400, "total": 38400}}
```

`scaleErr` is the largest `|bounds[i] − dims[i]|` divided by the longest side of the box (must: ≤ 0.10; should:
≤ 0.05, §11.2). "Forge job start" in §11.2 is the moment the job leaves `queued`; `ms.queue` is reported apart.

## 3.10 Forge: the hosted fallback and its disclosure

The hosted service receives a crop of the walker's room. Its retention is not ours to control (§7.4).

| # | Rule |
|---:|---|
| 1 | With no key configured, no request ever leaves the Station, whatever `backend` says (test L21). `GET /health` reports `"hosted": false` |
| 2 | With a key configured, `"hosted": true`, and the Console shows the amber "hosted fallback armed" banner on every page |
| 3 | A crop is sent to the hosted service only when the job's `backend` is `hosted`, or it is `local` and both local tries failed. Never when it is `cache` |
| 4 | Before a walker from outside the team scans, the operator tells them (the booth sign's data line says the same). If they decline, the operator sets `forge.backend` to `cache` on the Operator page: their objects come from the cache or the built-in set |
| 5 | Every hosted job says so: `JobStatus.source` is `hosted`, the Console's job list tags it, and `forge.log` records job id, host and byte count, never the image |
| 6 | Hosted output is credited in the licence ledger (§8.4) |

---

# Part 4 · Assets

## 4.1 The GLB asset contract

What every mesh the Forge serves must satisfy (§6.4, §7.4, §8.4). The validator of §4.2 checks all of it.

| Topic | Rule |
|---|---|
| Container | GLB, glTF 2.0: one JSON chunk, one BIN chunk. At most 1 MB (1,048,576 B) |
| Content | Exactly one scene, one node, one mesh, one primitive. The node has no translation, rotation, scale or matrix. Primitive mode 4 (triangles) |
| Forbidden | `materials`, `textures`, `images`, `samplers`, `skins`, `animations`, `cameras`, `extensionsUsed`, `extensionsRequired`. The primitive has no `material`. No Draco, no meshopt, no KTX |
| Attributes | Exactly `POSITION`, `NORMAL`, `TEXCOORD_0`, `COLOR_0`, with the same count. Indices `UNSIGNED_SHORT` (or `UNSIGNED_INT` above 65,535 vertices) |
| `POSITION` | `VEC3` `FLOAT`, metres, with the accessor's `min` and `max` |
| `NORMAL` | `VEC3` `FLOAT`, unit length within 0.01 |
| `TEXCOORD_0` | `VEC2` `FLOAT`: the **palette coordinate** of lane A's vertex format (lane A §10.2, §10.7). The same value on every vertex: the centre of cell `palette`, one of the object colours 64 … 127. Unity UV of cell i: `((i mod 16 + 0.5) / 16, (i div 16 + 0.5) / 16)`. Written in glTF's convention (v downwards): `(u, 1 − v)` |
| `COLOR_0` | `VEC4` `UNSIGNED_BYTE`, normalised, linear: lane A's vertex colour. **R = shade**: ambient occlusion times the face shade, `round(255 · saturate(ao · shade))`. **G = 255** (line weight; furniture has no line coordinates), **B = 255, A = 255** (reserved) |
| Units and axes | Metres, +Y up |
| Frame | The **box-local frame** of the object: origin at the centre of the box's base (RoomModel `objects[].base`), +X along the box's `w`, +Y up, +Z along its `d`, as lane C defines the box in Unity's left-handed convention |
| Handedness | A GLB is right-handed. The file stores `x_glTF = −x_box`, `y` and `z` unchanged, with counter-clockwise front faces. glTFast negates X and re-winds the triangles on import, which gives the box-local frame exactly. A mesh that looks mirrored on the phone means the Forge skipped this |
| Size | The mesh fills its box: on each axis the extent differs from `dims` by at most 10 % of the longest side of the box (should: 5 %) |
| Placement | Centred on x and z within 5 % of the longest side (proposed); lowest point within 0.02 m of y = 0 (proposed) |
| Triangles | 1,500 … 5,000, and never more than the `triTarget` of its job |
| Edges | No triangle edge longer than 0.25 m, so the mesh can bend in a curved space |
| Identity | The SHA-256 of the file is its name (`<sha256>.glb`), its cache key and its `ETag` |
| Material | None in the file. The phone draws it with a pooled instance of the shared look material (README F7) |

The room's furniture is at most 20 such meshes and 60,000 triangles (plan §4.5): `triTarget = clamp(floor(60000 /
n), 3000, 5000)`.

The shade, as the Forge bakes it (lane A's `AtlasShading`, the same formula every mesh generator uses):

```text
n_box   = (−n_glTF.x, n_glTF.y, n_glTF.z)                       the unit vertex normal in the box-local frame
n_room  = ( n_box.x·cos(yaw) + n_box.z·sin(yaw),  n_box.y,  −n_box.x·sin(yaw) + n_box.z·cos(yaw) )
light   = normalize(0.35, 0.85, 0.40)                           room frame
shade   = 0.55 + 0.45 · (0.5 + 0.5 · dot(n_room, light))
ao      = 0.35 … 1, from the ambient-occlusion pass (§3.9)
R       = round(255 · clamp(shade · ao, 0, 1))
```

The file has no `TEXCOORD_1`, so no grid or edge line is drawn on furniture; the object's material keeps its
line strengths at 0.

`palette.json` in the Forge lists the 64 object colours the Forge may choose: index, cell-centre UV (Unity
convention) and sRGB colour. `tools/gen_forge_palette.py` writes it from the formulas of lane A §10.7 (rows 4 to
7: index 64 + 16 k + column; value V = 0.28, 0.42, 0.58, 0.74 for k = 0 … 3; columns 0 … 11 HSV(column · 30°,
0.50, V); column 12 HSV(30°, 0.12, V); 13 HSV(210°, 0.12, V); 14 HSV(28°, 0.35, V); 15 HSV(0°, 0, V)). The
indices are frozen; if lane A retunes a colour the script is run again.

## 4.2 The validator

The same checks run twice: in the Forge before a job may become `ready` (`forge/validate.py`), and on the phone
on the downloaded bytes, on a worker thread, before glTFast sees them. The code in the last column is what
appears in `JobStatus.error` (`validate:<code>`) and in `AssetAck.reason`.

| # | Check | Forge | Phone | Code |
|---:|---|---|---|---|
| 1 | Magic `glTF`, version 2, the declared length equals the file length, a JSON chunk then a BIN chunk | ✔ | ✔ | `container` |
| 2 | File size ≤ 1,048,576 B | ✔ | ✔ | `size` |
| 3 | SHA-256 of the file equals `AssetReady.sha256` | – | ✔ | `hash` |
| 4 | One scene, one node with no transform, one mesh, one primitive, mode triangles | ✔ | ✔ | `structure` |
| 5 | None of the forbidden members of §4.1 | ✔ | ✔ | `no_material` |
| 6 | Exactly the four attributes with the types of §4.1 and equal counts; the index count is a multiple of 3; every index is below the vertex count | ✔ | ✔ | `attributes` |
| 7 | Triangles ≥ 1,500 | ✔ | ✔ | `tris_min` |
| 8 | Triangles ≤ 5,000 | ✔ | ✔ | `tris_max` |
| 9 | Triangles ≤ the job's `triTarget` | ✔ | ✔ | `tris_target` |
| 10 | Longest triangle edge ≤ 0.25 m (0.5 mm of slack for rounding) | ✔ | ✔ | `edge` |
| 11 | Each bounding-box side within 10 % of the longest box side of `dims` | ✔ | ✔ | `bounds` |
| 12 | Centred on x and z within 5 % of the longest box side; lowest y within 0.02 m of 0 | ✔ | ✔ | `origin` |
| 13 | Every position and normal is finite; no triangle of area under 10⁻¹⁰ m² | ✔ | ✔ | `finite` |
| 14 | Every normal has length 1 ± 0.01 | ✔ | – | `normals` |
| 15 | Every `TEXCOORD_0` is the cell centre of `palette`, and `palette` is 64 … 127 | ✔ | – | `palette` |
| 16 | `COLOR_0`: G = B = A = 255 | ✔ | – | `colour` |

On the phone, `triTarget` and `dims` come from the RoomModel (`objects[].triTarget`, `objects[].size`).

```csharp
// File: ATLAS/Assets/Atlas/Telemetry/Assets/GlbValidator.cs   (engine-free core: System and Atlas.Link only)
using System;

namespace Atlas.Telemetry
{
    public struct GlbExpectation
    {
        public float DimX, DimY, DimZ;        // RoomModel size [w, h, d], metres
        public int TriTarget;                 // 3000..5000
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
        public const int MaxBytes = 1048576, MinTriangles = 1500, MaxTriangles = 5000;
        public const float MaxEdgeMetres = 0.25f, EdgeSlack = 0.0005f;
        public const float BoundsTolerance = 0.10f, CentreTolerance = 0.05f, BaseTolerance = 0.02f;

        /// <summary>Checks 1 to 13 on the raw file. No Unity API: safe on a worker thread.</summary>
        public static GlbReport Validate(ReadOnlySpan<byte> glb, in GlbExpectation expect);
    }
}
```

## 4.3 The asset client

`Atlas.Telemetry.AssetClient`, created by lane D's installer (§5.1). It uploads the RoomModel and the crops,
follows the jobs, downloads, validates, loads and hands each mesh to lane C's room content. It never blocks a
frame: HTTP goes through `UnityWebRequest`, started and completed at step 8b; hashing, the validator and file
writes run on a worker thread; glTFast runs under a time budget.

It is driven from three sides, all on the main thread:

| From | Call | Meaning |
|---|---|---|
| Room content (lane C §9.1) | event `IRoomContent.Built(room)` | A room is active and its placeholder boxes exist. The client starts a room: cached meshes are placed |
| Photo capture (lane C §11.4) | `IObjectPhotoSink.BeginRoom(room, roomJson)`, `Submit(photos)`, `ReportNoPhoto(objectId)`, `EndRoom()` | A fresh scan: the RoomModel text to post, then the crops of each object, largest first |
| The link | `ILinkAssetSink.OnJobStatus`, `OnAssetReady` | Job progress from the Hub |

### States per object

| State | Meaning | RoomModel `mesh` (`MeshState`) | On screen |
|---|---|---|---|
| `Placeholder` | Not picked (`TriTarget` 0), or nothing sent yet | `Placeholder` | Placeholder box |
| `Uploading` | Its crops are being sent: attempt 1, 2 or 3 | `Placeholder` | Placeholder box |
| `Requested` | The Forge holds a job for it | `Requested` | Placeholder box |
| `Downloading` | `AssetReady` known; the GLB is being fetched, hashed and validated | `Requested` | Placeholder box |
| `Loading` | glTFast import | `Requested` | Placeholder box |
| `Ready` | The generated mesh is placed; `AssetAck ok` sent | `Ready`, with `AssetHash` | Generated mesh |
| `BuiltIn` | The built-in mesh of its category is placed (§4.4) | `BuiltIn` | Built-in mesh |

The RoomModel entry is changed only by room content, through the three calls the asset client makes:
`IRoomContent.SetObjectState(objectId, MeshState.Requested)`, `SetGeneratedMesh(objectId, mesh, assetHash)` and
`SetBuiltInMesh(objectId, mesh, Vector3.one)` (lane C §9.1). The composition root saves the RoomModel on
`ObjectChanged`, which keeps the `boxId → sha256` record of §7.4.

```csharp
// File: ATLAS/Assets/Atlas/Telemetry/Assets/AssetClient.cs
namespace Atlas.Telemetry
{
    public enum ObjectAssetState : byte
    { Placeholder = 0, Uploading = 1, Requested = 2, Downloading = 3, Loading = 4, Ready = 5, BuiltIn = 6 }

    public sealed class AssetClient : Atlas.World.IObjectPhotoSink, Atlas.Link.ILinkAssetSink
    {
        public const float LoadedRoomGraceSeconds = 2f;

        public bool ForgeReachable { get; }          // the last GET /health answered 200
        public int PlacedCount { get; }              // Ready                  → Telemetry.assetsPlaced
        public int PendingCount { get; }             // Uploading … Loading    → Telemetry.assetsPending
        public int BuiltInCount { get; }
        public ObjectAssetState GetState(int objectId);

        // IObjectPhotoSink (lane C section 11.4)
        public void BeginRoom(Atlas.World.RoomModel room, string roomJson);
        public void Submit(Atlas.World.ObjectPhotoSet photos);
        public void ReportNoPhoto(int objectId);
        public void EndRoom();

        // ILinkAssetSink (section 2.10)
        public void OnJobStatus(in Atlas.Link.JobStatus status);
        public void OnAssetReady(in Atlas.Link.AssetReady ready);

        /// <summary>DELETE /rooms/{roomId} and removal of this room's cached GLBs (section 13.5).</summary>
        public void ForgetRoom(string roomId);
    }
}
```

### The round trip

1. **`Built`.** The objects that take part are those with `TriTarget > 0` (lane C picks them: all, or the 20
   largest by volume, §7.4). Every object whose `AssetHash` is set and whose file is in the cache
   (`persistentDataPath/assets/<sha256>.glb`) goes straight to step 6: the pre-scan path needs no Station (D21).
   A `Built` for another room ends the previous one: its requests are cancelled and late answers are dropped
   (`room_changed`).
2. **`BeginRoom(room, roomJson)`.** `POST /rooms` with `roomJson` as UTF-8, unless `forge.backend` is `off`.
   If no `BeginRoom` for this room has come within 2 s of `Built` (proposed), the room came from a file or a
   fixture and no photo will follow: every taking-part object with no cached mesh becomes `BuiltIn`.
3. **`Submit(photos)`.** `POST /rooms/{roomId}/objects`, in the order received (largest box first). `meta` is
   built from the `ObjectPhotoSet`: `boxId` = `ObjectId`, `category` = `ObjectCategories.Name(Category)`,
   `dims` = `Size`, `yaw`, `triTarget`, `backend`, and per view `score`, `azimuthDeg`, `intrinsics`
   (`x, y, z, w` = fx, fy, cx, cy) and `camToBox` row by row (row r, column c = `CamToBox[c][r]`). 202 →
   `SetObjectState(Requested)`. 200 with `cached` → step 5. The JPEG arrays are released when the request ends.
   **`ReportNoPhoto(objectId)`** → `BuiltIn` at once.
4. Job progress arrives as `JobStatus` and `AssetReady` through the Hub. While the link is not `Connected` and
   an object is `Requested`, the client polls `GET /jobs?room={roomId}` every 2 s (§7.4).
5. `failed` → `BuiltIn` at once. `AssetReady` → `Downloading`: `GET <forgeUrl><url>` into
   `persistentDataPath/assets/<sha256>.glb.part`; on a worker thread, SHA-256 and `GlbValidator.Validate`; then
   rename to `<sha256>.glb`. A file already in the cache skips the download.
6. `Loading`: on the main thread, `new GltfImport(null, deferAgent, materialGenerator, logger)` and
   `Load(byte[], …)` (U-net §4: glTFast loads on the main thread only; its decoding runs in jobs). The defer
   agent is one shared `TimeBudgetPerFrameDeferAgent` with `SetFrameBudget(0.1f)` (proposed: 1.7 ms per frame at
   60 fps). The material generator is lane D's own and is never asked for a material: the file has none and
   nothing is instantiated. **One import at a time.** The one mesh of `GltfImport.Meshes` is copied
   (`Object.Instantiate`) and the import disposed, because `Dispose` destroys what it made.
7. Hand-over: `IRoomContent.SetGeneratedMesh(objectId, mesh, sha256)`. Room content owns the mesh from then on,
   assigns the pooled look material, cross-fades from what is shown over 0.3 s, begins at most one swap per
   frame, and raises `ObjectChanged` (lane C §9.1). The asset client never touches a renderer or a material:
   `Atlas.Telemetry` cannot reference `Atlas.Rendering`.
8. `AssetAck`: `ok` true, `loadMs` = download complete → hand-over, `worstFrameMs` = the worst frame time
   from the hand-over until 0.5 s later (must: < 20 ms, test L17). Also sent with `ok` false and a `reason`
   whenever step 5 or 6 rejects the asset; that object becomes `BuiltIn`.
9. Every step writes one `asset` event (§2.6): `upload`, `requested` or `cached`, `ready`, `placed`,
   `builtin`, `failed`, `rejected`. §11.4.7 is read from them.

`forge.backend`: `local`, `hosted` and `cache` are sent in `meta.backend`. `off`: nothing is sent to the Forge;
every taking-part object with no cached mesh becomes `BuiltIn` at once.

### Time-outs and retries

| Step | Time-out | Retries | When it fails |
|---|---|---|---|
| Reachability, before every upload attempt: `GET /health` | 2 s | – | The attempt has failed |
| `POST /rooms` | 10 s | 3 attempts in all, waits of 1 s and 2 s between them | Forge unreachable |
| `POST /rooms/{roomId}/objects` | 15 s | 3 attempts in all, waits of 1 s and 2 s | Forge unreachable |
| Forge unreachable | – | `GET /health` every 4 s | Every object not yet `Ready` becomes `BuiltIn`. At most 9 s after the first attempt (3 × 2 s + 1 s + 2 s), so within the 10 s of test L19. When `/health` answers again, the client posts the room and the crops it still holds again, and each generated mesh replaces its built-in one |
| Job silence: no `JobStatus` and no successful poll | 30 s | – | Forge unreachable |
| `GET /jobs?room=` | 5 s | every 2 s while needed | – |
| `GET /assets/<sha256>.glb` | 20 s | 2 more, waits of 1 s and 2 s | `AssetAck` `download_failed`; `BuiltIn` |
| Hash and validator | – | – | `AssetAck` `hash_mismatch`, `too_large` or `validate:<code>`; the `.part` file is deleted; `BuiltIn` |
| glTFast import | 10 s | – | `AssetAck` `load_failed`; `BuiltIn` |

An object that is `BuiltIn` because its job failed, its asset was rejected, or it had no photo stays `BuiltIn`.
Only "Forge unreachable" is retried. For that retry the client keeps the crops of objects it could not upload,
in memory, until they are uploaded or the room ends.

Privacy (§7.4): the client holds crops only until the upload that carries them has ended, or the room ends, and
writes none to disk. Nothing but crops, the RoomModel text and `meta` leaves the phone.

## 4.4 The built-in fallback set

One mesh per object category, shipped in the build, placed when the Forge cannot deliver (`P1-D12`, plan §4.5).

| Topic | Rule |
|---|---|
| Categories | Every `ObjectCategory` of lane C §6 except `Unknown` and `Other`: `couch`, `table`, `bed`, `lamp`, `plant`, `screen`, `storage`, `bathtub`, `chair`, `dishwasher`, `fireplace`, `oven`, `refrigerator`, `sink`, `stairs`, `stove`, `toilet`, `washerDryer`, `keyboard`, `mouse`, `laptop` (21), keyed by `ObjectCategories.Name`. An object of category `Unknown` or `Other` keeps its placeholder box |
| Files | `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasFallback/<name>.glb.bytes`, loaded with `Resources.Load<TextAsset>("AtlasFallback/<name>.glb")`, and `manifest.json` beside them |
| Template | A GLB with one mesh, `POSITION` and `NORMAL` only, in the frame of §4.1, filling the **unit box**: x and z in [−0.5, 0.5], y in [0, 1]. At most 300 triangles. CC0 |
| How they are made | `tools/gen_fallback_set.py` (lane D; Python standard library only) builds each one from a short list of boxes (a chair is a seat, a back and four legs) and writes the 21 files and `manifest.json`. Deterministic: running it again changes no byte. The meshes are the team's own work, released CC0; a CC0 kit mesh may replace any file if it meets the template row. `.meta` files come from the repository's own tool |
| Placement | `FallbackMeshBuilder` (engine-free core, `Telemetry/Assets/FallbackMeshBuilder.cs`) fills a `MeshData` (lane A §10.2) for **this** box: scale the template to the box's `Size`; split every edge longer than 0.25 m (both triangles of a shared edge together, so no crack); recompute normals; palette coordinate = the cell centre of the colour below; shade = `AtlasShading.FaceShade` of the normal turned by the box's `Yaw`, times an occlusion of 0.7 at the floor rising to 1.0 at 0.3 m; line weight 255; line coordinates (0, 0) |
| Colour | An object colour (cells 64 … 127), not the placeholder's tint, so that a built-in mesh can be told from a placeholder. `manifest.json` gives the cell per category; the script's table is the source **(decided here)** |
| The `Mesh` | Lane D builds a `UnityEngine.Mesh` from the `MeshData` with the five vertex attributes of lane A §10.2 in one stream. One mesh per object, already at the box's size |
| Hand-over | `IRoomContent.SetBuiltInMesh(objectId, mesh, Vector3.one)`. Room content does not destroy a built-in mesh (lane C §9.1): the asset client keeps it and destroys it when the object's mesh is replaced or the room ends |
| Limits | At most 1,500 triangles and no edge over 0.25 m **after** scaling (§8.4, test L19). A box so large that the split passes 1,500 triangles keeps its placeholder box, with an `asset` event `builtin` and reason `too_large` |
| When | (a) Forge unreachable after 3 failed attempts; (b) no photo; (c) job `failed`; (d) asset rejected; (e) `forge.backend` `off`; (f) a room loaded from a file with no cached mesh for the object |
| Tagging | The RoomModel object's `mesh` is `builtIn`; the `asset` event has step `builtin`; the Console's map and job list show "built-in" |
| Replacement | In case (a) each built-in mesh is replaced by the generated one when the Forge returns |

---

# Part 5 · Telemetry

Everything of lane D that runs inside the Unity app: the installer, the step 8 glue, the perf probe, the HUD,
the event log, pairing, the on-device menu, the Spectator and the mini-map.

## 5.1 The installer and step 8

Lane A's frame loop gives lane D three hooks (lane A §7): `IStateSink` at step 8a, and two `IAfterFrameStep`
at step 8b. Lane D adds nothing else to the frame.

```csharp
// File: ATLAS/Assets/Atlas/Telemetry/TelemetryInstaller.cs
namespace Atlas.Telemetry
{
    public static class TelemetryInstaller
    {
        /// <summary>Step 18 of the creation order (lane A section 8.2). Creates lane D's systems under boot.Systems
        /// and boot.Hud, publishes its services and adds its three hooks.</summary>
        public static void Install(Atlas.World.AtlasBootContext boot);
    }

    /// <summary>What the installer made. Published through AtlasServices for lane D's code in Atlas.App and
    /// Atlas.Spectator, and for onboarding.</summary>
    public sealed class AtlasTelemetry
    {
        public Atlas.Link.ParamRegistry Params { get; }
        public Atlas.Link.LinkClient Link { get; }
        public PairingController Pairing { get; }
        public EventLog Events { get; }
        public AtlasPerfProbe Probe { get; }
        public AssetClient Assets { get; }
        public string SessionId { get; }
        /// <summary>A new session: a new id, new files, a new Hello. Onboarding calls it when a new walker starts.</summary>
        public string BeginSession();
    }
}
```

What `Install` does, in order:

| # | Creates | Publishes · hooks |
|---|---|---|
| 1 | The session id; the cached `Application.persistentDataPath`; the file writer thread (§5.5) | – |
| 2 | Baked defaults into `boot.Params` (1.7); `demoLock` and `hud.visible` for the build (1.4); handlers of lane D's own parameters | – |
| 3 | `EventLog`; the `Application.logMessageReceived` tap for `Log` | `AtlasServices.Register<IEventLog>` |
| 4 | `LinkClient` (role `phone` on a device, `editor` in the Editor, `spectator` when `boot.IsSpectator`); `PairingController`; `OperatorBridge` (§2.9) | `Register<PairingController>` |
| 5 | `AtlasPerfProbe` | `Register<IPerfSource>`; `IFrameLoop.AddAfterFrame(probe)` |
| 6 | `LinkPump` | `IFrameLoop.SetStateSink(pump)`; `IFrameLoop.AddAfterFrame(pump)`. In the Spectator also `IFrameLoop.AddSense(spectatorSense)` (§5.8) |
| 7 | `AssetClient` | `Register<IObjectPhotoSink>`; subscribes to `IRoomContent.Built` and `ObjectChanged` |
| 8 | HUD and link dot under `boot.Hud` (§5.4) | – |
| 9 | `AtlasTelemetry` | `Register<AtlasTelemetry>` |
| 10 | The link is started when `link.enabled` is on **and** this phone has paired before (§5.6). On a first launch nothing is sent until onboarding asks | – |

The three hooks:

| Step | Hook | Class | Work, in order |
|---|---|---|---|
| 8a, `LateUpdate` | `IStateSink.Send(in StateMessage state)` | `LinkPump` | 1. Compare with the previous frame's `State` and log the edges: `bump.level`, `tracking.lost`, `tracking.back`, `pose.jump`; on a change of `worldVer` or `spaceId`, the world hash and the `world` event. 2. Keep the second's highest `bumpLevel` and the cue count. 3. `LinkClient.SendState` (it rate-limits to `link.stateHz`, §2.10) |
| 8b, after rendering | `IAfterFrameStep.AfterFrame(in FrameContext frame)` | `AtlasPerfProbe` | Sample this frame's timings and counters. Once per second: make the `Telemetry` row, queue its CSV line, send it (§5.2, §5.3) |
| 8b | `IAfterFrameStep.AfterFrame(in FrameContext frame)` | `LinkPump` | 1. `LinkClient.Poll()`: applies `ParamSet`, hands commands to `OperatorBridge` and asset messages to `AssetClient`. 2. Sends the batched `param.set` event of this frame. 3. `AssetClient` tick: start or finish requests, at most one mesh hand-over. 4. Pairing tick. 5. HUD and link dot, every 0.5 s. 6. Hands queued log lines to the writer thread |

Rules:

- Nothing here waits. A request that is not finished is looked at again next frame.
- Cost on the main thread, whole of lane D: 0.2 ms target, 0.5 ms cap (§8.5, the "Link, telemetry, HUD" row).
  0 B allocated per frame in steady state.
- With no `IStateSink` call (no frame loop yet) nothing is sent. With `link.enabled` off nothing is sent either;
  the edge events of 8a, the probe, the HUD, the asset client's local work and the event file still run.
- On `RoomChanged` (`ISpaceHost`), `LinkPump` fills a `RoomOutlineMessage` from the RoomModel (`Outline`,
  `MaxWallHeight`, `FreeRect`, `Objects`: lane C §14.4) and sends it.
- `OnApplicationPause` and `OnApplicationQuit` reach lane D through a small `MonoBehaviour` the installer puts
  under `boot.Systems`. It does no per-frame work.

## 5.2 `AtlasPerfProbe`

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Perf/AtlasPerfProbe.cs, PerfRing.cs
using System;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>Frame timings and render counters: a 3,600-frame ring, one Telemetry row per second
    /// (Master Plan sections 7.3.3, 8.8, 11.2).</summary>
    public sealed class AtlasPerfProbe : IPerfSource, IAfterFrameStep
    {
        public const int RingFrames = 3600;        // one minute at 60 fps
        public const int LadderFrames = 600;       // the window of IPerfSource
        public const int MinFramesForData = 120;

        // IPerfSource (lane A section 9.5): what the thermal ladder reads
        public bool HasData { get; }               // false until 120 frames have been sampled
        public float GpuP95Ms10s { get; }          // GPU frame time, p95 of the last 600 frames; refreshed once per second
        public float FrameP95Ms10s { get; }        // frame time, p95 of the last 600 frames

        public int SampleCount { get; }            // frames in the ring, 0 .. 3600
        public bool TryGetLastRow(out TelemetryMessage row);
        public event Action<TelemetryMessage> RowReady;     // once per second, at step 8b

        public void AfterFrame(in FrameContext frame);
        public void ClearRing();                   // also done by itself when State.spaceId changes
    }

    /// <summary>The engine-free core: fixed arrays, percentiles on a scratch copy, no allocation after construction.</summary>
    public sealed class PerfRing
    {
        public PerfRing(int capacity);
        public int Count { get; }
        public void Clear();
        public void Add(float frameMs, float cpuMainMs, float cpuRenderMs, float gpuMs);
        /// <summary>Nearest rank over the last `window` samples (0 = all): the value at index ceil(q * n) - 1 of the
        /// sorted samples. channel: 0 frame, 1 CPU main, 2 CPU render, 3 GPU. 0 when there is no sample.</summary>
        public float Percentile(int channel, float q, int window = 0);
        public float ShareOver(float thresholdMs, int window = 0);     // percent of frame-time samples above it
    }
}
```

**The thermal ladder's inputs.** `QualityLadder` (lane A) reads GPU p95 and frame p95 through `IPerfSource`,
which the probe publishes. It reads the thermal state itself (`AtlasThermal.State`, every 2 s) and publishes it
as `IQualityState.ThermalState`; the probe takes thermal state, tier, render scale and MSAA for the `Telemetry`
row from `IQualityState`. `Atlas.Telemetry` and `Atlas.Rendering` never reference each other.

| Value | Source |
|---|---|
| Frame time, CPU main, CPU render, GPU | `FrameTimingManager.CaptureFrameTimings()`, then `GetLatestTimings(1, timings)`: `cpuFrameTime`, `cpuMainThreadFrameTime`, `cpuRenderThreadFrameTime`, `gpuFrameTime`, in ms. The values are a few frames late. When no timing is returned (Editor, feature off) the frame time is `FrameContext.DeltaTime` and the other three are 0 |
| Draw calls, set-pass calls, triangles | `ProfilerRecorder.StartNew(ProfilerCategory.Render, name)` with `"Draw Calls Count"`, `"SetPass Calls Count"`, `"Triangles Count"`; `LastValue` |
| Memory | `ProfilerCategory.Memory`, `"System Used Memory"`, in MiB |
| Managed allocation | `ProfilerCategory.Memory`, `"GC Allocated In Frame"` |
| Tier, thermal, render scale, MSAA | `IQualityState` (lane A §9.5) |
| Tracking | `FrameContext.Tracking`, coded by lane C's `TrackingStates.ToWire` |
| Battery | `SystemInfo.batteryLevel` × 100; 255 when it is negative |
| RTT, loss, `State` rate | `LinkClient` |
| Scene, space, highest bump level, cue count, Glued copies | The `State` of each frame, through `LinkPump`. The cue count rises with every rise of `bumpLevel` and with every rising edge of the cue flag that comes without one (an operator cue): lane C's `BumpCue.FireCount` |
| Assets placed and pending | `AssetClient` |
| Visible tiles | 0 until lane B's tile renderer reports it |

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
        public const string Header = "t_us,seq,sceneId,…,assetsPending";       // the line above
        public static void AppendRow(System.Text.StringBuilder sb, in TelemetryMessage row);   // one line with its \n
    }
}
```

Example row:

```text
1234567890,42,1,1,0,60.000,16.667,16.900,17.400,18.250,0.500,0.000,4.100,2.900,4.600,0.800,142,22,231,612,0,25,3,0,1,0,0,87,0,4,60,1,3,1
```

- A row the phone could not send (OFFLINE) stays in a ring of 600 rows and is sent at RESYNC, so the Hub's CSV
  has no hole for an outage of up to ten minutes (§7.3.3: "reliable, so the CSV has no holes").
- §12.2, "CSV on the phone, uploaded over HTTP later": `PUT /logs/{session}/perf.csv` on the Hub (§3.3).

## 5.4 HUD and link dot

Both hang under `boot.Hud` (lane A §8.1), on its layer (`AtlasHud`), so they are drawn in both eyes after the
world and are not faded with it. Text is TextMeshPro (§8.1). Together at most 4 draw calls per frame of the 20
the layer may use (lane A §12.1).

| Item | Rule |
|---|---|
| HUD panel | One TextMeshPro object, 1.0 m ahead, 14° below the view axis, 0.45 m wide, five lines. Shown while `hud.visible` is on, or `telemetry.mode` is `hud` |
| Refresh | Every 0.5 s, from the last `Telemetry` row, into one cached `StringBuilder`, then `TMP_Text.SetText(StringBuilder)`: no allocation. Cost under 0.2 ms (`P1-D04`) |
| Line 1 | `60 fps  p95 16.9  tier 0  therm 0` |
| Line 2 | `cpu 4.1 / 2.9  gpu 4.6 ms` |
| Line 3 | `draws 142  sp 22  tris 231k` |
| Line 4 | `mem 612 MB  bat 87  gc 0` |
| Line 5 | `S1  link 3 ms 0.0%  assets 3/1` (space; RTT and loss, or `offline`; placed / pending) |
| "Session over" | On tier `Critical` the panel shows that line whatever `hud.visible` says (lane A §9.5) |
| Link dot | The character `•`, TextMeshPro, 1.0 m ahead, 22° below the view axis, centred. Green `Connected`, amber `Degraded` or `Resync`, grey `Offline` (the colours of lane A's cells `LinkOk`, `LinkDegraded`, `LinkOff`). Refreshed every 0.5 s. **Hidden while `Connected` under `demoLock`** (§7.2) and while `link.enabled` is off |
| In the hand | No HUD and no dot: the head-locked layer is not drawn in `DisplayMode.Hand`. The link panel of the onboarding screen is §5.6 |
| Mini-map | §5.9, while `hud.map` is on |

## 5.5 Session, event log and files on the phone

**Session.** A session id is made at app start and by `AtlasTelemetry.BeginSession()`: `yyyyMMddTHHmmssZ-xxxx`,
UTC time and four random hexadecimal digits, for example `20261004T153012Z-a3f9`. It names the phone's files
and, through `Hello`, the Hub's folder. `BeginSession` closes the open files, opens new ones, restarts `seq` of
`Telemetry` at 0 and sends `Hello` again.

**Files** (README F8, which puts both logs under `logs/`; §7.3.3 wrote `perf/`):

| File | Content | Written |
|---|---|---|
| `<persistentDataPath>/logs/perf_<session>.csv` | §5.3 | One line per second |
| `<persistentDataPath>/logs/events_<session>.jsonl` | One event per line: the `Event` body of §2.6 | When an event is logged |
| `<persistentDataPath>/logs/phone_<session>.log` | `<t_us> <level> <msg>`: the lines that go out as `Log` | When a warning or an error is raised |
| `<persistentDataPath>/assets/<sha256>.glb` | The GLB cache (§4.3) | By the asset client's worker |

- One writer thread owns the three log files. The main thread hands it finished lines through a queue; the
  thread appends and flushes once per second, and at pause and quit. A lost link loses nothing (§7.3.3).
- `Application.persistentDataPath` is read once on the main thread and cached (it is not thread-safe, U-net §6).
- `UIFileSharingEnabled` makes the folder readable from the Mac (C.5).
- No camera frame and no crop is ever written (rule 10).
- §13.5: delete-by-id removes the three log files of a session; `AssetClient.ForgetRoom` removes a room's GLBs.

**Events.** §2.6 has the format, the names and `IEventLog`. Where lane C's events come from: lane D's code in
`Atlas.App` (`App/Menu/EventTaps.cs`, created by `MenuInstaller`) subscribes to `IScanSession.StateChanged`,
`IRoomContent.Built` and `ObjectChanged`, and `IAtlasInput.ActionFired`, and logs `scan.state`, `room.ready`,
`object.mesh` and `input.action` (lane C §14.4). The tracking, jump and bump events are taken from `State` at
step 8a (§5.1), so they need no reference to `Atlas.Safety`.

## 5.6 Pairing

`http://<station>:8080/pair` shows a QR code. The iOS Camera app reads it and opens the app through the URL
scheme `atlas` (written to Info.plist by lane A's post-build step, C.5).

| Topic | Rule |
|---|---|
| QR content | Exactly `atlas://<ip>:9050`, for example `atlas://192.168.0.10:9050`. `<ip>` is a dotted IPv4 address |
| Deep link | At start `Application.absoluteURL`, and afterwards the event `Application.deepLinkActivated` (U-net §6): both go to `TrySetAddress` |
| Accepted text | `atlas://<ip>[:port]`, `<ip>:<port>` or `<ip>`. Port 1 … 65535, default 9050. Anything after the port is ignored. A host name is refused: LiteNetLib would resolve it on the calling thread (U-net §3.3). Parsed by lane D's own code, not by `System.Uri` |
| Stored | `PlayerPrefs` key **`atlas.hub.address`** = `"<ip>:<port>"`; key `atlas.hub.paired` = 1 once the link has been `Connected`. `PlayerPrefs.Save()` after each write |
| Default | With no stored address: `192.168.0.10:9050`, the Station's reserved address (C.7). The third number is the router's: open for the lead |
| Fallback | A typed address on the onboarding screen ("Change IP") |
| Local Network prompt | iOS raises it at the first UDP send to a local address. There is no API to ask for it or to read the answer. `BeginPairing()` makes that send: in the foreground, phone in hand, at onboarding step 1 (lane C §10.4). `NSLocalNetworkUsageDescription` is in the Info.plist (C.5) |
| Retry | While pairing: one connect attempt every 1 s (C.7, R4 §3.3), because iOS drops the request while the prompt is up. After 15 s without a connection the screen shows "Settings › Privacy & Security › Local Network › ATLAS" and "Continue without Station". After 60 s the attempts stop |
| Afterwards | The back-off of §2.8. On a later launch the link starts at boot, with no prompt (test L1) |
| After a delete and reinstall | The permission and the stored address are gone: pair again (C.7) |

```csharp
// File: ATLAS/Assets/Atlas/Telemetry/Pairing/PairingController.cs
using System;
using Atlas.Link;

namespace Atlas.Telemetry
{
    public enum PairingPhase : byte { Idle = 0, Trying = 1, Connected = 2, Hint = 3, TimedOut = 4, Skipped = 5 }

    public sealed class PairingController
    {
        public const string AddressKey = "atlas.hub.address";
        public const string PairedKey = "atlas.hub.paired";
        public const string DefaultAddress = "192.168.0.10";
        public const float RetrySeconds = 1f, HintAfterSeconds = 15f, GiveUpAfterSeconds = 60f;

        public string Address { get; }                 // "192.168.0.10:9050"
        public bool HasStoredAddress { get; }
        public bool WasPaired { get; }
        public PairingPhase Phase { get; }
        public float SecondsTrying { get; }
        public event Action<PairingPhase> PhaseChanged;

        /// <summary>True for the three accepted forms; host is a dotted IPv4 address.</summary>
        public static bool TryParse(string text, out string host, out int port);
        /// <summary>Parses, stores and reconnects. False, and nothing changes, when the text is not accepted.</summary>
        public bool TrySetAddress(string text);
        /// <summary>Onboarding step 1: starts the link if needed and tries every second. Raises the Local Network prompt.</summary>
        public void BeginPairing();
        /// <summary>"Continue without Station": stops the one-second attempts. The link keeps its normal back-off.</summary>
        public void Skip();
        /// <summary>For the link panel: state, address, RTT, loss, last error, and whether the Forge answers.</summary>
        public void GetStatus(out LinkStatus link, out bool forgeReachable);
    }
}
```

Onboarding (lane C's files) draws the pairing step and the in-hand link panel from this class: a state dot
(green, amber, grey), the Hub address, RTT, loss, Forge reachability, and the buttons "Reconnect"
(`LinkClient.Reconnect`) and "Change IP" (`TrySetAddress`) (§7.2). Lane D draws nothing in the hand.

## 5.7 The on-device menu

`Atlas.App.Menu` (lane D's files in `Atlas.App`; `P1-D13`). An in-headset list of the kill switches, driven by
the same registry. It needs no link: every change is `registry.Set(handle, value, ParamSource.Menu)` and is
written to the phone's event log as `param.set` with `src` `menu` (tests L22, G6).

```csharp
// Files: ATLAS/Assets/Atlas/App/Menu/MenuInstaller.cs, OnDeviceMenu.cs, EventTaps.cs, FlatTwinPanel.cs
namespace Atlas.App.Menu
{
    public static class MenuInstaller
    {
        /// <summary>Step 19 of the creation order (lane A section 8.2). Not called in the Spectator.</summary>
        public static void Install(Atlas.World.AtlasBootContext boot);
    }

    /// <summary>The panel. Ticked at step 8b as an IAfterFrameStep, after IAtlasInput.Tick of the same frame.</summary>
    public sealed class OnDeviceMenu : Atlas.World.IAfterFrameStep
    {
        public bool IsOpen { get; }
        public void Open();
        public void Close();
        public void AfterFrame(in Atlas.World.FrameContext frame);
    }
}

// File: ATLAS/Assets/Atlas/Telemetry/Menu/MenuModel.cs
namespace Atlas.Telemetry
{
    /// <summary>The engine-free core of the menu: rows, selection, stepping, pending values. Tested headless.</summary>
    public sealed class MenuModel
    {
        public MenuModel(Atlas.Link.ParamRegistry registry);
        public int RowCount { get; }                   // kill switches + "Reset all" + "Close"
        public int Selected { get; }
        public void Refresh();                         // re-reads the registry when ManifestVersion changed
        public void Move(int delta);                   // selection up or down, clamped
        public void Select(int row);                   // a gaze dwell on a row
        public void Step(int direction);               // -1 or +1 on the selected row
        public void Confirm();                         // applies a pending value, or runs "Reset all" / "Close"
        public bool CloseRequested { get; }
    }
}
```

| Topic | Rule |
|---|---|
| Lists | Every parameter flagged `KillSwitch`, in index order: the 21 of 1.5, which are the parameters of §12.2 and §12.4. Then "Reset all to defaults" and "Close". A header shows link state, Hub address, session id, tier and thermal state |
| Row | Id, value (the label for a `Bool` or `Enum`, the number with its unit otherwise), and a mark when it differs from the default. An inactive group's rows are greyed but can be changed |
| A step | `Float`: ± `step`, or ± 1/20 of the range when `step` is 0, or the next entry of `choices`. `Int`: ± 1, or the next entry of `choices`. `Bool`: toggles. `Enum`: the next option, no wrap |
| Applying | At once, except a parameter flagged `OnRelease` (`space.id`, `glued.mode`, …): its row shows the pending value and `Confirm` applies it. That is the menu's "release" |
| Input | The service `IAtlasInput`, taken once from `AtlasServices` (lane C §13.3) |
| Open and close | Lane C's action `AtlasAction.ParamMenu`: the gamepad chord `start` while `select` is held, F1 in the Editor (lane C §13.1). `AtlasAction.Back` closes. On open `IAtlasInput.PushContext(InputContext.Menu)`, on close `PopContext` |
| Gamepad | `NavUp`, `NavDown` move the selection; `NavLeft`, `NavRight` step the row; `Confirm` applies; `Back` closes. Read with `IAtlasInput.Pressed` |
| Gaze dwell (rule 5) | While closed: one target for `ParamMenu`, head-locked at (−0.55, −0.40, 1.0) m, radius 0.05 m (proposed). While open: each visible row is a target that selects it (`NavDown` with the row in `Tag`); the selected row has a "−" and a "+" target (`NavLeft`, `NavRight`, `GazeMode.Repeat`); one target each for `Confirm` and `Back`. All registered with `IAtlasInput.RegisterGazeTarget`, `GazeSpace.Head`, context `Menu` |
| Panel | Under `boot.Hud`, 1.2 m ahead, 0.7 × 0.5 m: one background quad (`Atlas/Surface`, pooled material, fog and fade weights 0) and two TextMeshPro objects. Eight rows visible; the list scrolls with the selection. At most 6 draw calls per frame |
| The world | Keeps running. Bump Guard and its cue are unaffected and are drawn over the menu's layer order by lane C |
| Logging | `menu` event on open and close; `param.set` per change |
| Cost | Closed: one `Pressed` read per frame. Open: text is rebuilt only when something changed |

## 5.8 The Spectator

The same Unity project built for Windows with `ATLAS_SPECTATOR`, or the Editor playing `PC_Spectator`. It joins
the Hub as role `spectator`, receives the active source's `State`, and redraws the walker's view with one mono
camera. No video is sent (§7.3.1). It never integrates motion and never guesses a world.

**What it consumes.**

| From the link | Used for |
|---|---|
| `Welcome` | Session id, `forgeUrl`, the source's `roomId` and `worldHash` |
| `State`, the newest only | Head pose, space, tile id, sheet, tile-local transform, `p[]`, flags, bump level, `worldVer` |
| `ParamManifest`, `param.set` events | Its own registry: each value is set by id with `ParamSource.Link`; `active` sets the group state |
| `world` event | The world hash to compare with its own |
| `RoomOutline` | The outline panel beside the mirror |
| `object.mesh` and `asset` events, `JobStatus`, `AssetReady` | The same furniture: the Spectator's `AssetClient` downloads the same GLBs and places the same built-in meshes. It uploads nothing and sends no `AssetAck` |

From the Forge: `GET /rooms/{roomId}` for the RoomModel, `GET /assets/{sha256}.glb`.

**How it is put together.** `AtlasBootstrap` runs with `AtlasBootContext.IsSpectator` (lane A §8.2). The steps
it skips there, and what stands in their place:

| Skipped (lane A §8.2) | In the Spectator |
|---|---|
| 5, 6: stereo rig, eye textures, lens blit | `Atlas.Spectator.SpectatorView`: one `Camera`, vertical field of view 60° (proposed), near 0.05 m, far 60 m, 4× MSAA, no lens blit. `LeftEyePosition` and `RightEyePosition` both equal the head position |
| 10: input | Keyboard shortcuts of `SpectatorView` only |
| 11: AR session, pose pipeline, scan | `SpectatorSense : ISenseStep` in `Atlas.Telemetry` (step 1): fills `RawHeadPosition`, `RawHeadRotation` and `Tracking` from the newest `State`. No pose filter: the geometry pose is the raw pose |
| 12: Bump Guard | Nothing. A coloured bar shows `State.bumpLevel` |
| 17: `CommandRouter` | Nothing. A spectator receives no command |
| 19: menu, onboarding | `SpectatorView`'s panels |

`Atlas.Spectator` may not be referenced by `Atlas.App`, so it installs itself: a
`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` method subscribes to `AtlasServices.Booted` and then creates
`SpectatorView`. It adds no frame hook; the one hook the Spectator needs is added by lane D's installer.

**Each frame.**

1. `SpectatorSense` takes the newest `State`. With none yet, it leaves the frame at the origin.
2. Room: when `roomId` is new, `GET /rooms/{roomId}` → `RoomModelJson.TryRead` → `ISpaceHost.SetRoom(room)`;
   room content builds on `RoomChanged`, as on the phone (lane C §14.2).
3. World check: its own `WorldHash.Compute(…)` against the hash of the last `world` event. **On a mismatch it
   shows "world mismatch", enters no space, and fetches the room again every 2 s** (§7.3.1, test L8).
4. Space: when `State.spaceId` differs from `ISpaceHost.Current`, `RequestSwitch`. With `spaceId` 255 it shows
   "no space" and the room outline only.
5. Module: if the entered module implements `ISpaceStateReader`, `ReadState(in state)` (lane A §2). For a curved
   space (lane B), `IFrameLoop.ResetWalker` with the pose built from `tileId` and `xform`, so that
   `FrameContext.Walker` is the phone's pose and nothing is integrated. A flat space needs no walker reset.
6. The frame loop then runs as on the phone: kernel step, `Tick`, upload, render. `SpectatorView` puts its
   camera at the frame's raw head pose in `LateUpdate`.

No interpolation and no prediction in Phase 1 **(decided here)**: the picture is the newest `State`. Target: the
mirror lags the phone by at most 150 ms (test L5); tile ids equal on 100 % of samples (L7).

**Beside the mirror** (§7.3.1): the flat-twin mini-map (§5.9); the room outline from `RoomOutline` with the
walker's physical position and the object boxes; the Bump Guard level as a coloured bar (0 clear, 1 warn,
2 strong, 3 tracking lost); link state, `State` rate and latency; the banners "world mismatch", "no phone" and
"crops kept". The panels are drawn by cameras on layer `AtlasSpectator` (lane A §12.1).

## 5.9 The flat-twin mini-map

Rule 6 of §0.5: every curved or glued view has a flat twin. One renderer for the headset and the Station
(`P1-D07`): an engine-free draw list in `Atlas.Telemetry`, shown by `FlatTwinPanel` on the phone (under
`boot.Hud`, lower right, 0.25 m across at 1.0 m, while `hud.map` is on) and by `SpectatorView` on the Station.

```csharp
// Files: ATLAS/Assets/Atlas/Telemetry/Map/FlatTwin.cs, FlatTwinBuilder.cs
using System;

namespace Atlas.Telemetry
{
    public enum FlatTwinInk : byte { Outline = 0, Grid = 1, Cell = 2, Highlight = 3, Object = 4, Walker = 5, Trail = 6, Portal = 7 }

    /// <summary>A retained list of lines, polygons, dots and labels in map units. Engine-free.</summary>
    public sealed class FlatTwin
    {
        public void Clear();
        public void SetView(float centreX, float centreZ, float halfExtent, bool clipToDisc);
        public void Line(float ax, float az, float bx, float bz, FlatTwinInk ink);
        public void Polygon(ReadOnlySpan<float> xz, FlatTwinInk ink);       // closed outline: x0, z0, x1, z1, …
        public void Dot(float x, float z, float radius, FlatTwinInk ink);
        public void Arrow(float x, float z, float headingRadians, FlatTwinInk ink);
        public void Label(float x, float z, string text);
    }

    /// <summary>Optional, on a space module: the space draws its own flat twin. Called at most 10 times per second.</summary>
    public interface IFlatTwinSource
    {
        void WriteFlatTwin(FlatTwin twin);
    }

    /// <summary>The built-in twins. Each needs only the RoomModel, the registry, the kernel and the newest State.</summary>
    public static class FlatTwinBuilder
    {
        public static void Build(FlatTwin twin, Atlas.World.RoomModel room, Atlas.Kernel.IWalkerKernel kernel,
                                 in Atlas.Link.StateMessage state, FlatTwinTrail trail);
    }

    /// <summary>The last 600 walker positions at 10 Hz, and the last 64 tile ids, without repeats.</summary>
    public sealed class FlatTwinTrail
    {
        public void Add(in Atlas.Link.StateMessage state);
        public void Clear();
    }
}
```

| Space | Flat twin | Drawn from |
|---|---|---|
| S0 Home | Floor plan: outline, free rectangle, lap square, object boxes, the walker as an arrow, the trail | RoomModel; `State` head pose |
| S1 Glued | Lattice plan: n × n cells round the walker's cell, the fundamental cell highlighted, mirrored rows marked for the Klein bottle, the walker and its copies as dots | RoomModel `cells.glued`; `State.tileId`, `p[3]` (mode), `p[4]` (copies) |
| S2, S3 | Poincaré disc of the tile graph centred on the walker: the tiles of `IWalkerKernel.Candidates` within ρ, the walked tile sequence, the room outline in tile zero | Kernel (lane B) |
| S4 Small Planet | Unfolded cube net: six faces, the walker's face highlighted, the trail | `State.tileId` |
| S5 Impossible Rooms | Floor plan with "room k of n"; the room graph (rooms as nodes, portals as edges, the current room highlighted) when the space implements `IFlatTwinSource` | `State.tileId`, `p[4]`; the space |
| S6 Slicer | Floor plan, plus a strip that shows the cut w₀ on its range and the three angles | `State.p[3..6]` |
| Any space that implements `IFlatTwinSource` | Whatever it draws | The space |

**While lane B's tile graph is the flat stub.** A graph with `Q == 4` (`FlatTileGraph`, κ = 0) has no disc. The
builder then draws a **square-grid plan** in metres: the cells of `Candidates`, each a square of side
`Graph.CentreSpacing · MetresPerUnit`, the walker's cell (`FlatTileGraph.TryGetCell(Pose.Tile, …)`) highlighted,
the walker at `Pose.Local`'s translation inside it, the trail of visited cells, and the room outline over tile
zero. No projection and no hyperbolic formula is involved. When lane B's {4,5} graph lands, the same call draws
the Poincaré disc from `VisibleTile.Chain`; nothing else changes. In this build no curved space can be entered,
so the grid plan is seen only in lane B's sandbox and in the test below.

Rules: at most 0.3 ms on the phone (`P1-D07`); rebuilt at 10 Hz, not every frame; in replay the map shows the
same tile-id sequence as the log; the in-headset map is off by default, because a mini-map distracts (F.2: show
the flat twin late).

---

## 6 · What lane D needs from the other lanes

Names this page takes from the other pages. Where one of them is spelled differently there, that page wins and
this one is corrected at the freeze.

| Name | Page | Used for |
|---|---|---|
| `AtlasBootContext` (`Params`, `Systems`, `Hud`, `Head`, `IsSpectator`, `IsDevice`), `AtlasServices` | A §6, §8 | The installers (§5.1, §5.7, §5.8) |
| `IFrameLoop` (`SetStateSink`, `AddAfterFrame`, `AddSense`, `ResetWalker`, `Current`); `IStateSink`, `IAfterFrameStep`, `ISenseStep` | A §7.1 | Step 8 (§5.1); the Spectator's sense step |
| `FrameContext` (`RawHeadPosition`, `RawHeadRotation`, `Tracking`, `DeltaTime`) | A §3 | Probe; Spectator |
| `ISpaceHost` (`Current`, `HasSpace`, `Module`, `Room`, `WorldVersion`, `RequestSwitch`, `SetRoom`, `SpaceEntered`, `RoomChanged`), `ISpaceStateReader`, `SpaceId`, `SpaceIds` | A §1, §2, §4 | Events, `RoomOutline`, world hash, Spectator |
| `IOperatorCommands` | A §7.1 | §2.9 |
| `IQualityState`, `IPerfSource`, `QualityTier` | A §9.5 | §5.2 |
| `AtlasVertex`, `AtlasShading`, `MeshData` | A §10.2 | Built-in meshes (§4.4); the Forge's shade (§4.1) |
| Palette layout: the cell-centre formula, object colours 64 … 127, link colours 201 … 203 | A §10.7 | §4.1, §4.4, §5.4 |
| Layers `AtlasHud` (through `boot.Hud`) and `AtlasSpectator` | A §12.1 | §5.4, §5.7, §5.8 |
| `IWalkerKernel`, `WalkerPose`, `TileId`, `Iso2`, `FlatTileGraph.TryGetCell` | B §2, §3, §4 | World hash, Spectator, mini-map |
| `RoomModel` (`Id`, `Outline`, `MaxWallHeight`, `FreeRect`, `Objects`), `RoomObject`, `MeshState`, `ObjectCategory`, `ObjectCategories.Name`, `RoomModelJson` (`TryRead`, `Write`, `GeometryHash`) | C §2, §6 | `RoomOutline`, world hash, asset client, Spectator |
| `IRoomContent` (`Built`, `ObjectChanged`, `SetObjectState`, `SetGeneratedMesh`, `SetBuiltInMesh`) | C §9.1 | §4.3, §4.4 |
| `IObjectPhotoSink`, `ObjectPhotoSet`, `ObjectView` | C §11.4 | §4.3 |
| `TrackingState`, `TrackingStates.ToWire` | C §7.1 | `Telemetry.tracking`. The wire coding (0 normal, 1 limited, 2 lost) is not the enum's numbering |
| `IAtlasInput` (`Pressed`, `LastTag`, `PushContext`, `PopContext`, `RegisterGazeTarget`, `ActionFired`), `AtlasAction` (`ParamMenu`, `NavUp`, `NavDown`, `NavLeft`, `NavRight`, `Confirm`, `Back`), `InputContext.Menu`, `GazeTarget` | C §13 | §5.7 |
| `IScanSession.StateChanged` | C §10.3 | `scan.state` events |
| `BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator)` | C §8.8 | The end of the `cue` command, called by lane A's `CommandRouter`. Lane D never calls it |

Requests: changes in files lane D does not own.

| # | To | Request | Why |
|---:|---|---|---|
| 1 | README F1 | `Atlas.Telemetry` may also reference `Atlas.Kernel` | `FrameContext`, the argument of `IAfterFrameStep`, carries `WalkerPose` and `KernelStep`; `MeshData` carries `AtlasVertex`; the mini-map reads `IWalkerKernel` |
| 2 | README F1 | `Atlas.World` holds one lane D file, `World/Services/IEventLog.cs` | So that `Atlas.Rendering`, `Atlas.Scan` and the spaces can log (§2.6) |
| 3 | A (asmdef) | `Atlas.Telemetry`: `Atlas.Link`, `Atlas.World`, `Atlas.Kernel`, `Unity.Mathematics`, `Unity.Collections`, `Unity.Profiling.Core`, `glTFast`, `Unity.TextMeshPro`, `UnityEngine.UI` | TextMeshPro needs both of the last two (U-net §9.1) |
| 4 | A (asmdef) | `Atlas.World` and every `Atlas.Spaces.*` reference `Atlas.Link`; `Atlas.App` references `Atlas.Link`, `Atlas.Telemetry`, `Unity.TextMeshPro`, `UnityEngine.UI` | README F1; the menu |
| 5 | A (asmdef) | `Atlas.Spectator` references `Atlas.Kernel`, `Atlas.Telemetry`, `Unity.Mathematics`, `Unity.TextMeshPro`, `UnityEngine.UI`, `Unity.InputSystem`; a test assembly `Atlas.Telemetry.Tests` exists | README F1; §7 |
| 6 | A | `AtlasBootstrap` creates the registry with `new ParamRegistry()`, the constructor that declares the table of 1.4 | The indices of 1.4 |
| 7 | A | Step 18 calls `Atlas.Telemetry.TelemetryInstaller.Install(boot)`; step 19 calls `Atlas.App.Menu.MenuInstaller.Install(boot)` | §5.1, §5.7 |
| 8 | A | `AtlasBootstrap` also boots in `PC_Spectator` when built with `ATLAS_SPECTATOR` (lane A §8 names `Atlas_Boot` and `Sandbox_…` only) | §5.8 |
| 9 | A | `FillState` starts from `StateMessage.Identity`, packs `flags` with `StateFlags.Pack`, and leaves `seq` and `t_us` to the sink | §2.3 |
| 10 | A | `QualityBinding` writes with `ParamSource.Ladder`; `CommandRouter.SetLock` uses the line of 1.5; SpaceLab calls `registry.SetGroupActive(SpaceIds.ParamGroup(id), …)` | 1.3, 1.5 |
| 11 | C | Lane C's onboarding installer (step 19) wires photo capture to the sink lane D publishes at step 18 as `IObjectPhotoSink` (lane C §14.1). Nothing more is asked | §4.3 |
| 12 | A | Optional: `PortalSpace` implements `IFlatTwinSource` to draw its room graph | §5.9 |
| 13 | C | Onboarding calls `PairingController.BeginPairing()` at its step 1, reads `Phase` at step 2, draws the link panel from `GetStatus`, and calls `AtlasTelemetry.BeginSession()` when a new walker starts | §5.5, §5.6 |
| 14 | C | The rows 36 to 41 of 1.4 (`pose.*`, `replay.*`, `scan.route`) are consumed through plain properties that `Atlas.App` wires | README F6 |
| 15 | Repository tools | `.meta` files for `Telemetry/Resources/AtlasFallback/*.glb.bytes` and `Telemetry/Resources/AtlasParams/defaults.json`; the scene `PC_Spectator.unity` (empty) | `.meta` files and scenes are generated, never written by hand |

---

## 7 · Tests

Engine-free tests run in the Unity Test Runner and under `tools/headless/verify.py` (README F2). The first three
are the rows of §11.1.

| Test | Assembly | Pass condition |
|---|---|---|
| `Link_RoundTrip` (§11.1) | `Atlas.Link.Tests` | For every message of §2.2 except the reserved `PeerState`: decode the golden file in `Tests/Link/Golden/`, encode again, get the same bytes. `State` is 202 B and equals the table of §2.3 (SHA-256 `c84417e5…bcd0`). The golden bytes followed by extra bytes decode to the same value. A message one byte short is rejected. Ids 0x11 and 0x60 are ignored with no exception |
| `Link_ClockSync` (§11.1) | `Atlas.Link.Tests` | `ClockSync` on a simulated link with 10 % loss, 15 ms one-way delay and 0–5 ms jitter (proposed), injected offset 3,999,950 µs: error ≤ 2 ms at p95 over 600 simulated seconds |
| `Link_Watchdog` (§11.1) | `Atlas.Link.Tests` | `LinkWatchdog`: `Degraded` at 500 ms of silence; `Connected` again only after 2,000 ms healthy; `Offline` at 3,000 ms; attempts after 0.5, 1, 2, 4, 4 s. With `LoopbackLink`: after a reconnect the host's snapshot `ParamSet` arrives and is applied |
| `Link_Loopback` (`P1-D01`) | `Atlas.Link.Tests` | `LoopbackLink`: `Hello`, `Welcome`, eight pings, manifest; 600 `State` at 60 Hz with 2 % simulated loss arrive in order with gaps only; a `Hello` with `proto` `[2, 0]` gets a readable `Bye`; `ParamSet` is dropped under `demoLock` |
| `Param_Registry` | `Atlas.Link.Tests` | `new ParamRegistry()` holds the 48 rows of 1.4 with their indices, kinds, ranges, defaults and flags. Declaring twice returns the same handle. Clamp, step and choice rules of 1.1. `NoRemote`, `ReadOnly`. Callback order. Snapshot and restore. Baked defaults. Group state. The manifest JSON equals its golden file |
| `Json_RoundTrip` | `Atlas.Link.Tests` | Objects, arrays, escapes, surrogate pairs, exact `i64`, depth 32 accepted and 33 refused, bytes after the root ignored, the number rules of 1.8 |
| `Telemetry_Csv` | `Atlas.Link.Tests` | The header and the example row of §5.3, byte for byte |
| `WorldHash_Vector` | `Atlas.Link.Tests` | The vector of §2.5 gives `141060c2fc138145` |
| `Glb_Validator` | `Atlas.Telemetry.Tests` | A valid GLB built by the test passes; thirteen variants each fail with the code of its row in §4.2 |
| `Perf_Ring` | `Atlas.Telemetry.Tests` | Nearest-rank percentiles on known data; the 600-frame window; no allocation after construction |
| `Pairing_Parse` | `Atlas.Telemetry.Tests` | The three accepted forms; refusals: a host name, a port of 0 or 70000, an empty string, `atlas:192.168.0.10` |
| `Fallback_Set` | `Atlas.Telemetry.Tests` | Each of the 21 files meets the template row of §4.4. Built for boxes from 0.3 m to 2.4 m: at most 1,500 triangles and no edge over 0.25 m, or the builder reports `too_large` |
| `Menu_Model` | `Atlas.Telemetry.Tests` | The 21 switches in the order of 1.5; stepping per kind; an `OnRelease` row changes only on `Confirm`; every change raises `Changed` with `ParamSource.Menu` |
| `FlatTwin_Grid` | `Atlas.Telemetry.Tests` | With a `FlatWalkerKernel` walked round a square, the trail's tile-id sequence equals the kernel's; the S0 plan contains every outline segment of `sample_3x3` |
| Forge | `pytest station/forge/tests` | With the `test` generator: a crop becomes a GLB that passes `validate.py`; the same input gives the same hash; a corrupt JPEG ends as `failed` `bad_image`; a second identical upload is a cache hit with no job; a 21st object is 409; `backend` `cache` with a miss fails at once; with no hosted key no socket is opened to another host |
| Hub | `dotnet test station/hub.tests` | Relay of `State` unchanged; `perf.csv` equal to `TelemetryCsv`; presets saved, loaded as one `ParamSet`, baked; `/internal/job` refused from a non-loopback address; snapshot replay and `AssetReady` replay at RESYNC |

Only a device or the Station can confirm: LiteNetLib under IL2CPP with stripping Low; the Local Network prompt on
a clean install; `Application.deepLinkActivated` on a warm open; `FrameTimingManager` GPU times on Metal; the
render counters in a release player; glTFast on the phone (mirroring, `Dispose` after the copy); TextMeshPro on
the `AtlasHud` layer in both eyes; the tests L1 to L22 of §7.6.

---

## Open for the lead

1. **Hosted image-to-3D service** (open item 8 of §14; settle by G6): which one, what budget, what licence, what
   data-retention terms. Until then the `hosted` adapter has no key and rule 1 of §3.10 holds: nothing leaves
   the Station. Also wanted with it: the measured time per object on the RTX 5070.
2. **The Station's address.** `192.168.0.10` is this page's default; the third number is the router's
   (`P1-X04`). One constant, `PairingController.DefaultAddress`.
3. **Where the phone's CSV lives.** README F8 says `persistentDataPath/logs/`, §7.3.3 says
   `persistentDataPath/perf/`. This page follows F8 and keeps the file name `perf_<session>.csv`.
4. **Who owns the session.** The phone makes the session id and the Hub adopts it, so the phone's files and
   the Hub's folder match with no link at start. A new session starts when onboarding starts a new walker. Confirm
   that this is the unit §13.5 deletes by.
5. **A second phone.** Phone B may connect while phone A is active; it waits, and the Console can switch
   (§2.11). Confirm, or have the Hub refuse a second phone.
6. **Console access.** Anyone on the Station's router can open the Console and send commands. There is no
   password in Phase 1. Acceptable on our own router; say so if the venue changes that.
7. **TextMeshPro for the HUD and the menu.** §8.1 names TextMeshPro for every label; README F7 names one shader.
   This page reads §8.1 as the exception. Lane A's page does the same for text.
8. **`IEventLog` in `Atlas.World`, and `Atlas.Kernel` for `Atlas.Telemetry`.** Two small additions to README F1
   (requests 1 and 2 of §6).
9. **No interpolation in the Spectator.** The picture is the newest `State`. If the mirror stutters at 30 or
   10 Hz, a 50 ms interpolation buffer is the fix, at the cost of that latency.
10. **Loss simulation on the Hub** needs LiteNetLib built from source (`SimNet`, §2.11). Otherwise run the loss
    tests with the simulation on the Editor side.
11. **Comfort stop value** (open item 6 of §14). The Console has the FMS entry; the score at which it alerts
    waits for the lead's number.
