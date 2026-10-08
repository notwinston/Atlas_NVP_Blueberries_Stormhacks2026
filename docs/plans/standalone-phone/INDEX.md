# Standalone phone: scanned rooms with matched props, no Station at runtime

Branch: `experiment/standalone-phone`, cut from `main` at `4c92f70`. **This is an experiment. It is never merged into `main`.** The wrap-up stops at "accept": docs are updated and the plan is closed on this branch, and nothing is squashed into or pushed to `main`.

Paths below are relative to the repository root; `A/` means `ATLAS/Assets/Atlas/`.

## 1. Goal

On an iPhone 15 Pro Max, with no Station PC and no network, a walker:
1. scans a room;
2. sees walls, doors and windows, and every RoomPlan object, rebuilt as a **matched artist-made prop** within 10 s of entering Home. Each prop is scaled to its scanned box, faces the way the real one faces and is coloured from the object's photos. Objects RoomPlan cannot name become a cut-out of the LiDAR mesh;
3. walks Home and Curved Walk with those objects bent correctly.

The room objects are drawn in one draw call per eye instead of one per object, and the frame cost is measured before and after.

**End-to-end check (task S3).**
- **Setup:** Wi-Fi off; build `tools/build_ios.sh` (team id in `ATLAS_TEAM_ID`) installed on the phone.
- **Scan:** three scans of real rooms finish without a crash. Each gives walls, at least one door or window in `walls[].openings`, and objects. Every object with category ≠ Unknown/Other shows a matched prop (`mesh = matched`), and Unknown objects show `scanCut`.
- **Saved room:** saving a room and loading it again gives the same props, colours and orientations. The geometry hash is unchanged.
- **Performance:** a `perf.run = tour` on the scanned room and on `budget_room_120k` writes `perf_<session>.csv`. `tools/perf/summarise.py` shows fewer room draw calls than the S0 baseline and no frame-time regression.

## 2. Decisions

Agents must not re-decide these.

| # | Decision | Reason | Rejected |
|---|---|---|---|
| D1 | **Matched props**: CC0 artist props chosen per object by category, RoomPlan attributes, box proportions and the 16×16 profile, then scaled and coloured on the phone. | Clean shapes bend well under the warp, they are cheap per frame, and they keep the one-look contract (Master Plan D23) and privacy rule 10. | A photo-textured LiDAR twin: needs UV unwrap on the phone, a new shader path and a privacy-rule change, and comes out rough. **Kept open as a future option** (D10). Image-to-3D on the phone (TripoSR/SF3D): lower quality, large weights. |
| D2 | **One RoomPlan session**: our Swift plugin is the only `RoomCaptureSession`, on Unity's ARSession. It replaces Unity's `RoomPlanBoundingBoxSubsystem` and the wall bridge, and returns walls, doors, windows, openings, objects, object attributes and coaching instructions. | The 4 Oct crash came from a *second* RoomCaptureSession. One session is exactly the load that ran to the end on the phone. No world-map handoff is needed. | A native RoomPlan screen before Unity plus an ARWorldMap handoff: costs 5–15 s and alignment error. It is the fallback only if spike S1 fails. |
| D3 | **The Station is an optional extra.** The phone needs nothing from it. The Hub, Console and Spectator still work when a Station is paired. TripoSG/Forge leave the runtime path: `forge.backend` defaults to `off`, the onboarding TripoSG toggle is removed, and the Forge code stays in the tree, dormant. | The user's decision, 2026-10-06. | Deleting the Station; keeping the Forge as an upgrade path. |
| D4 | **Props come from CC0 kits**: Kenney Furniture Kit, KayKit Furniture Bits and Quaternius furniture packs (poly.pizza mirrors many as single GLBs). About **3 props per RoomPlan category**, 16 categories, about 48 props. Licences and credits go in `CREDITS.md`. | Master Plan §8.4 already names these kits; three per category lets the matcher tell variants apart. | Procedural Blender props; 1 or 5+ per category. |
| D5 | **A prop is a kit GLB** in the existing kit contract (`tools/kit/kitglb.py`), plus a manifest entry. Colour slots are marked by the prop's own palette cells: each slot is authored with one distinct default cell, and recolouring rewrites `Uv0`. No new vertex attribute. | Reuses `GlbMeshReader` and the 44-byte `AtlasVertex`. | A new slot attribute. |
| D6 | The matcher score is the **profile fit F** (`station/forge/forge/profile.py:fit_score`) plus an aspect penalty minus a tag bonus. Scaling uses the **Forge formula** (stretch clipped at a = 0.15). Edges are split to ≤ 0.25 m **after** scaling. | Reuses proven, tested rules; the Python code serves as the reference for tests. | MobileCLIP visual retrieval (later, if matches look wrong). |
| D7 | **Colour per slot from the crops.** Face centres are projected into the views and back-facing faces are skipped. The median is taken in linear light, then the nearest of the 64 object palette cells in OKLab is chosen. At most 6 cells per object. Crops stay in memory and are dropped after colouring. | A port of the Forge's `colour.py` rules, reduced to slots; rule 10 is unchanged. | Per-face colour with ray-cast visibility (Forge style): more work for little gain on props. |
| D8 | **LiDAR cut-out** is used for Unknown/Other objects, and for any object whose best fit F > 0.20 (the Forge's "poor" threshold) when the box holds ≥ 200 scanned triangles. The scan mesh is kept **in memory only**, in the room frame, until objects are built. | The real shape beats a wrong prop. A mesh is not a camera frame, so rule 10 is unchanged. | Today's stepped-column scan-cut, which stays as the fallback when there is no mesh. |
| D9 | **Batched room objects.** Vertex `COLOR.b` carries the object's slot (0..63; 255 = not batched, which is today's value everywhere). The global `float4x4 _AtlasObjectAnchors[64]` holds each object's anchor, set with `Shader.SetGlobalMatrixArray` (a global, not a `MaterialPropertyBlock`). There are no shader keywords. An object that is cross-fading leaves the batch until its fade ends. | One draw call per eye for all objects, with no keyword and no change to the frozen per-material block. | Single-pass stereo (needs keywords; large). GPU instancing (needs keywords). |
| D10 | **The textured-twin door stays open.** The scan mesh is exposed through `IScanMeshSink` (World). Nothing in this plan removes key-frame capture. | The user may switch to the twin later. | — |
| D11 | **RoomModel stays at schema version 1**, with additive optional keys `tags`, `prop` and `propCells`, and new mesh states `matched` and `scanCut`. `GeometryHash` is unchanged. | Old files still load; the Spectator and Forge are not broken. | A version 2 bump. |
| D12 | **The Master Plan is amended in place on this branch**: D20, D22, D23 (the one-material wording), §4.2 routes, §7.4, gates G6/G7, plus new Appendix D rows dated 2026-10-06. | Keeps the Master Plan the one source that wins (user's choice). | A separate decision document. |
| D13 | `scan.wallBridge` keeps its name but now means "merge RoomPlan walls, doors and windows from the shared session". It defaults to **on**. Boxes always come from the plugin when RoomPlan is available. | Lets walls be switched off alone if they make the floor worse. | A new parameter name, which would ripple into the Hub's parameter tables. |
| D14 | The engine-free object code goes in a **new assembly `Atlas.Objects`** (references: `Atlas.Kernel`, `Atlas.World`, `Unity.Mathematics`). `Atlas.Telemetry` gains a reference to it. | Headless-testable; one owner for mesh operations. | Adding it to Telemetry. |

## 3. Assumptions to test first

| Spike | Assumption | How it is tested | If false |
|---|---|---|---|
| **S0** perf baseline (device, user) | `perf.run = tour` runs on the phone with the Station off and writes `perf_<session>.csv`. | Build the branch at T1's end (before T10), run `tour` in Home on `budget_room_120k` and in Curved Walk; copy the logs via Finder/Files; run `tools/perf/summarise.py`. Results go in `docs/plans/standalone-phone/evidence/S0-baseline.md`. | Fix the probe path before T10. T10 still runs, but its "fewer draws" check uses the HUD's draw count. |
| **S1** RoomPlan single session (device, user) | One `RoomCaptureSession(arSession:)` on Unity's session: 3 scans of 60 s finish without a crash; the LiDAR mesh, face classes, planes and the depth image still arrive (the depth watchdog may restart depth); objects with attributes arrive; walls ≤ 3 cm from the mesh-derived walls (P1-C05). | T2's build. Log per snapshot: object count, attribute strings, wall count, depth present, mesh triangles. Results and **the exact attribute strings** go in `evidence/S1-roomplan.md`. | Switch D2 to the fallback (native screen plus world-map relocalise; `WorldMapStore` exists) and stop the run to re-plan T2. |
| **S1b** attribute strings | `CapturedRoom.Object.attributes` give stable strings (e.g. chair type, sofa shape, table shape). | Read from S1's log. | Tags stay empty; the matcher works without them (the tag bonus is 0). |
| **S2** props in the warp | A matched prop, scaled and edge-split, has every edge ≤ 0.25 m and draws without cracks in Curved Walk. | Covered by T5's tests (edge rule) and seen in S3. | Lower `PropMaxEdge` to 0.18 m (the cell-diagonal rule). |

## 4. Out of scope

- Photo-textured twin, Gaussian splats, Object Capture, image-to-3D models, MobileCLIP retrieval.
- Single-pass stereo, GPU instancing, tile batching (only room objects are batched).
- Deleting Station, Forge, Hub, Console or Spectator code; Spectator support for `matched` objects. The Spectator draws first shapes for them; this is listed as a known gap.
- A detector for objects RoomPlan misses (Cubify Anything, BoxFusion). The obstacle blobs stay the route for those.
- Any merge into `main`.

## 5. Open questions

1. The exact RoomPlan attribute strings. **Unknown until S1.** T4 authors tags from Apple's documentation, and T12 reconciles them with S1.
2. Whether RoomPlan's object attributes are present during `didUpdate`, or only after `RoomBuilder`. **Unknown.** S1 logs both; T2 reads `didUpdate`, and if attributes only come from `RoomBuilder`, T2 runs `RoomBuilder(options: [.beautifyObjects])` once at `EndSweep`.
3. Whether every RoomPlan category has 3 CC0 props available. **Unknown.** T4 records any gap. A category with fewer props uses the props it has, and a category with none keeps the existing built-in template.
4. Frame-time headroom. **Unknown until S0.**

## 6. Interfaces

All units are metres and radians. "Room frame" means the RoomModel's frame: origin at the free-rectangle centre on the floor, +Y up, Unity handedness. "Box-local" means origin at the base centre with local +Z as the object's front (`RoomObject` convention).

### 6.1 `Atlas.World` (T1 owns)

```csharp
// A/World/Room/RoomModel.cs
public enum MeshState : byte { Placeholder = 0, Requested = 1, Ready = 2, BuiltIn = 3, StandIn = 4, Matched = 5, ScanCut = 6 }
// JSON names add: "matched", "scanCut".

public sealed class RoomObject {
    // existing fields unchanged, plus:
    /// RoomPlan attribute strings, verbatim (e.g. from String(describing:)). Never null; may be empty.
    public string[] Tags = Array.Empty<string>();
    /// "" or a prop id from the prop manifest. Set with Mesh = Matched.
    public string Prop = "";
    /// null, or one palette cell (64..127) per colour slot of Prop, in manifest slot order.
    public byte[] PropCells;
}
```

- **JSON** (`RoomModelJson`, schema `docs/contracts/roommodel.schema.json`, still `version: 1`). The optional object keys are:
  - `"tags"`: array of strings, at most 16, each ≤ 48 characters.
  - `"prop"`: string, ≤ 64 characters.
  - `"propCells"`: array of 1–8 integers, each 64–127.
- Each key is written only when it is not empty. `GeometryHash` does not include them (assert this in tests).
- `tools/rooms/validate_room.py` accepts the new keys and states.

```csharp
// A/World/Room/ScanMesh.cs — the scanned LiDAR mesh, room frame. Immutable after construction; worker threads read it.
public sealed class ScanMesh {
    public readonly float3[] Vertices;   // room frame, metres
    public readonly int[] Indices;       // 3 per triangle
    public readonly byte[] Classes;      // null, or one per triangle: 0 None,1 Wall,2 Floor,3 Ceiling,4 Table,5 Seat,6 Window,7 Door (== Atlas.Scan.Grid.SurfaceClass values)
    public ScanMesh(float3[] vertices, int[] indices, byte[] classes);
    public int TriangleCount { get; }
}

// A/World/Room/IScanMeshSink.cs
public interface IScanMeshSink {
    /// Main thread. Called once per finished room, before IObjectPhotoSink.BeginRoom for that room. mesh is null when the scan had no LiDAR mesh (manual outline, fixture, saved room).
    void SetScanMesh(string roomId, ScanMesh mesh);
}
public sealed class NullScanMeshSink : IScanMeshSink { /* keeps nothing */ }

// A/World/Room/ObjectCategory.cs — add:
public static ObjectCategory FromRoomPlan(string category);
// RoomPlan CapturedRoom.Object.Category names: bathtub, bed, chair, dishwasher, fireplace, oven, refrigerator, sink, sofa→Couch, stairs, storage, stove, table, television→Screen, toilet, washerDryer. Anything else → Other.

// A/World/Room/ObjectPaletteColours.cs — the 64 object colours (cells 64..127) as linear float3, copied from Rendering's AtlasPalette.
public static class ObjectPaletteColours { public const int First = 64, Count = 64; public static float3 Linear(int cell); }
// A Rendering test asserts equality with AtlasPalette for every cell.

// A/World/Room/IRoomContent.cs — add:
/// An on-phone mesh (matched prop or scan cut-out), box-local, scale baked. state is Matched or ScanCut. Cross-fades like SetGeneratedMesh. The same mesh serves both furniture levels.
bool SetOnPhoneMesh(int objectId, Mesh mesh, MeshState state, float fadeSeconds = 0.3f);
```

`A/Rendering/Room/RoomContent.cs` implements `SetOnPhoneMesh` (T1), reusing the generated-mesh swap path. `RoomObjectMeshes` treats Matched and ScanCut like Ready for drawing and levels.

### 6.2 RoomPlan plugin JSON (T2 owns; T1's tests agent writes the reader tests)

`AtlasRoomPlanCopyJson` returns the schema `atlas.room/1`, which replaces `atlas.walls/1`:

```
{"schema":"atlas.room/1",
 "walls":[S],"doors":[S],"windows":[S],"openings":[S],
 "objects":[O],
 "instruction":"normal|moveCloseToWall|moveAwayFromWall|slowDown|turnOnLight|lowTexture"}
S = {"c":[x,y,z],"x":[x,y,z],"w":w,"h":h}                    // unchanged
O = {"id":"<uuid>","cat":"<category>","c":[x,y,z],"x":[x,y,z],"z":[x,y,z],"d":[w,h,d],"conf":"low|medium|high","attrs":["..."]}
```

- `c` is the centre of the box (transform column 3), and `x`/`z` are its axis columns 0 and 2, all in ARKit's world frame. `d` is `dimensions`.
- The reader flips z into Unity's frame, as `WallBridgeJson` does today.
- C# reader: `A/World/Room/RoomPlanJson.cs`, `public static bool TryRead(string json, out RoomPlanCapture capture)` with `RoomPlanCapture { Surface[] Walls, Doors, Windows, Openings; RoomPlanObject[] Objects; ScanHint-like string Instruction }`. `RoomPlanObject { string Id; string Category; float3 Centre; quaternion Rotation; float3 Size; string[] Attributes; }`, in Unity's session frame.
- `ScannedBox` (Scan) gains `string[] Tags`. `ObstacleBox` (Scan.Grid) gains `string[] Tags`. These flow to `RoomObject.Tags` in `RoomModelBuilder`.

### 6.3 Prop library (T3 owns the format; T4 fills it)

- **Files.**
  - `A/Telemetry/Resources/AtlasProps/<id>.glb.bytes`: kit GLB contract (`tools/kit/kitglb.py`).
  - `A/Telemetry/Resources/AtlasProps/manifest.json`.
- **Mesh rules, at reference size.**
  - Metres, box-local: origin at the base centre, +Y up, front +Z.
  - Edges ≤ 0.25 m. 300–2,500 triangles.
  - Every face's palette cell is one of the prop's slot default cells (64..127).
- **Manifest.**

```json
{"schema":"atlas.props/1",
 "props":[{"id":"chair_kenney_dining_01","category":"chair","tags":["dining"],
           "size":[0.45,0.90,0.50],"triangles":812,"sha256":"…",
           "slots":[{"name":"body","cell":97},{"name":"legs","cell":70}],
           "profile":"<base64 256 B: profile.of_mesh at reference size, Unity box frame, same convention as RoomObject.Profile>",
           "source":"Kenney Furniture Kit 2.0 / chairDesk.glb","licence":"CC0-1.0"}]}
```

- **Category names** are the `ObjectCategory` JSON names: couch, table, bed, chair and so on.
- **Offline tools** (Python 3, numpy; `tools/props/requirements.txt`; run in a venv at `tools/props/.venv`, which is git-ignored):
  - `tools/props/profile_tool.py`: computes `profile` by importing `station/forge/forge/profile.py:of_mesh`.
  - `tools/props/check.py`: validates every prop against the rules above and the manifest. It exits non-zero on failure.
  - `tools/props/reference.py`: writes the test vectors in `A/Tests/Objects/Fixtures/reference.json`: profile rotation, `fit_score`, the scale formula, and the matcher's choice on fixed cases, all using the Forge code as the reference.
  - `tools/props/build_props.py` (Blender 5.2 at `/Applications/Blender.app/Contents/MacOS/Blender`, run with `-b -P`): import, join, assign slot cells, base-centre, front +Z, export through `tools/kit/kitglb.py`.

### 6.4 `Atlas.Objects` (T5, T6, T7 own their files; namespace `Atlas.Objects`; engine-free: no `UnityEngine` type)

```csharp
public sealed class PropEntry {           // built by Telemetry's PropSet from the manifest + GLB
    public string Id; public ObjectCategory Category; public string[] Tags;
    public float3 Size; public byte[] Profile; public byte[] SlotCells; public MeshData Mesh;
}
public struct ObjectQuery {
    public ObjectCategory Category; public string[] Tags; public float3 Size;
    public byte[] Profile; public ObjectFacingRule Facing;
}
public struct PropMatch { public int Index; public int QuarterTurns; public float Fit; public float Score; }

public static class PropMatcher {          // T5
    public const float AspectWeight = 0.5f, TagBonus = 0.03f, MaxTagBonus = 0.06f, Tie = 0.02f, PoorFit = 0.20f, MinCoverage = 0.30f;
    /// false when no prop of the category exists. Deterministic.
    public static bool Choose(in ObjectQuery q, PropEntry[] library, out PropMatch match);
}
public static class PropFitter {           // T5
    public const float MaxAnisotropy = 0.15f, PropMaxEdge = 0.25f;
    /// Rotates by quarter turns, scales to size (Forge formula), splits edges ≤ PropMaxEdge, bakes shade into COLOR.r with AtlasShading, sets COLOR.b = 255. Writes into `into` (cleared first). false if the result exceeds maxTriangles.
    public static bool Build(PropEntry prop, in PropMatch match, float3 size, int maxTriangles, MeshData into);
}
public static class MeshOps {              // T5
    public static void SplitLongEdges(MeshData mesh, float maxEdge);              // shared midpoints, crack-free
    public static void ClusterSimplify(MeshData mesh, float cell);                // vertex clustering, deterministic
    public static void DropSmallParts(MeshData mesh, float minAreaShare);         // 0.005 = 0.5 %
    public static void BakeShade(MeshData mesh, float yaw);                       // same rule as FallbackMeshBuilder (room light rotated by yaw, floor occlusion 0.7→1 over 0.3 m)
}
public struct RgbImage { public int Width, Height; public byte[] Rgb; }          // sRGB, 3 bytes per pixel, row 0 at top
public static class PropColour {           // T6
    public const int MaxCells = 6;
    /// One cell (64..127) per slot of prop, in slot order. Views/images in the same order (ObjectPhotoSet.Views). A slot no view sees keeps its default cell.
    public static byte[] Choose(PropEntry prop, MeshData fitted, ObjectView[] views, RgbImage[] images);
    /// One cell for a whole mesh (scan cut-outs).
    public static byte ChooseOne(MeshData mesh, ObjectView[] views, RgbImage[] images, byte fallbackCell);
    public static void Apply(MeshData mesh, byte[] slotDefaultCells, byte[] cells); // rewrites Uv0 palette cells
}
public static class ScanCut {              // T7
    public const float Margin = 0.03f; public const int MinTriangles = 200;
    /// Triangles of mesh inside obj's box (+Margin), not Floor-class, moved to box-local, simplified to ≤ maxTriangles, small parts dropped, edges split ≤ 0.25 m, shade baked. false if fewer than MinTriangles were inside.
    public static bool Build(ScanMesh mesh, RoomObject obj, int maxTriangles, MeshData into);
}
```

**Matcher rule (T5, exact).**
1. **Candidates:** props with `Category == q.Category`.
2. **Allowed quarter turns:** {0} when `Facing` is Profile or Clearance; {0, 1, 2, 3} when Facing is Box or Table.
3. **For each candidate and turn:**
   - `e` is the prop's size rotated by the turn, and `λ_k = ln(size_k / e_k)`.
   - Aspect: `A = mean_k |λ_k − mean(λ)|`.
   - `F = fit_score(q.Profile, rotate(prop.Profile, turn))` when `Coverage(q.Profile) ≥ 0.30`, else `F = 0`.
   - `tags = min(MaxTagBonus, TagBonus · |q.Tags ∩ prop.Tags|)`.
   - `Score = F + AspectWeight·A − tags`.
4. **Choice:** lowest `Score`. Scores within `Tie` are broken by the smaller turn, then by ordinal `Id`.
5. **Result:** `match.Fit = F`.

The caller treats `Fit > PoorFit` as poor (D8).

### 6.5 Telemetry (T8 owns)

```csharp
// A/Telemetry/Assets/PropSet.cs — loads manifest + GLBs from Resources/AtlasProps via KitResources-style reader; verifies sha256 and triangles; builds PropEntry[].
public sealed class PropSet { public static PropSet Load(Func<string, byte[]> read); public PropEntry[] Props { get; } public string Error { get; } }
// A/Telemetry/Assets/OnPhoneObjectsCore.cs — engine-free core: IObjectPhotoSink + IScanMeshSink; owns per-object state; runs match/fit/colour/cut on IAssetWorker; places through IAssetRoom.PlaceOnPhone.
// IAssetRoom (A/Telemetry/Assets/ForgeTransport.cs) gains:
bool PlaceOnPhone(int objectId, MeshData mesh, MeshState state, string prop, byte[] propCells);
```

**Order per object.**
1. A first shape at once, as today.
2. When `Submit(photos)` arrives (or `ReportNoPhoto`, which means default colours):
   - **Unknown/Other:** ScanCut.
   - **Otherwise:** match, then:
     - if `Fit > 0.20` and ScanCut succeeds → ScanCut;
     - else fit the prop. If the fit fails the triangle cap (`max(TriTarget, 3000)`), try the next-best candidate, and after that keep the first shape.
3. Colour.
4. Place.

The work runs on `IAssetWorker`; JPEG decoding happens on the main thread (Unity `ImageConversion`), at most 2 objects per frame, inside an adapter in `AssetClient.cs`.

**Saved room.** When a room loads with `mesh = matched` plus `prop` and `propCells`, it rebuilds deterministically without photos. ScanCut objects reload as first shapes, because the mesh is not saved.

### 6.6 Batched room objects (T10 owns)

- **Vertex colour.** `COLOR.b` is 255 meaning "not batched" (today, everywhere), or the object slot 0..63, read as `round(COLOR.b * 255)`.
- **Globals** (in `AtlasCore.hlsl`, outside `UnityPerMaterial`): `float4x4 _AtlasObjectAnchors[64];`.
  - In warp modes 1–3, each entry has the same layout as `_Anchor` (an Iso2 in the upper-left 3×3, `m[1,3]` = base height).
  - In mode 0, each entry is the object's full local-to-world matrix. The renderer's own transform is identity.
- **`AtlasWarp(positionOS, color)`:** when the slot is < 64, the anchor comes from the array; otherwise it uses the existing path.
- **CPU mirror.** `A/Rendering/Warp/AtlasWarpCpu.cs` gets the same overload.
- **Batch build.** `RoomContent` builds one batched mesh from the drawn copy of every object that is not fading. It is rebuilt on a worker when any object's drawn mesh or level changes, and uses 32-bit indices when there are more than 65,535 vertices. The batch renderer uses one pooled material.
- **Draw calls.** `DrawCallsPerEye` becomes `shell(2) + 1 (batch, if any object) + fading objects`.

## 7. Task table

| ID | Task | Owns files | Depends on | Impact | Done check | Model | Status |
|----|------|------------|------------|--------|------------|-------|--------|
| T1 | World contracts, `Atlas.Objects` assembly, `SetOnPhoneMesh` | `A/World/Room/{RoomModel,RoomModelJson,ObjectCategory,IRoomContent,ScanMesh,IScanMeshSink,ObjectPaletteColours,RoomPlanJson}.cs`, `docs/contracts/roommodel.schema.json`, `tools/rooms/validate_room.py`, `A/Objects/Atlas.Objects.asmdef` + `AssemblyInfo.cs`, `A/Telemetry/Atlas.Telemetry.asmdef`, `docs/contracts/README.md` (assembly rows), `tools/headless/verify.py` (ALLOWED + build lists), `A/Tests/Architecture/AssemblyGraphTests.cs`, `A/Rendering/Room/RoomContent.cs`, `A/Rendering/Room/RoomObjectMeshes.cs` | — | core | `verify.py all` green; Unity EditMode green; TT1 green | opus | done (run 1) |
| TT1 | Tests for T1 from this plan | `A/Tests/World/RoomModelStandaloneTests.cs`, `A/Tests/World/RoomPlanJsonTests.cs`, `A/Tests/Rendering/ObjectPaletteColoursTests.cs` | — (runs alongside T1) | shared | Tests compile against §6.1–6.2 signatures; fail before T1, pass after | opus | done (run 1) |
| T2 | One RoomPlan session | `ATLAS/Assets/Plugins/iOS/AtlasRoomPlan.swift`, `A/Scan/ARKit/RoomPlan/ArKitWallBridge.cs` (renamed `ArKitRoomPlan.cs`), `A/Scan/ARKit/Mesh/ArKitMeshPlatform.cs`, `A/Scan/Routes/{WallBridge,WallBridgeMerge,ScanSensors,ScanSession,ScanSystems,ObjectBoxMerge,ScanTypes}.cs`, `A/Scan/Session/ArSessionRig.cs`, `A/Scan/FloorScan/FloorGridTypes.cs`, `A/Scan/FloorScan/FloorScanCore.cs` (Tags passthrough only), `A/Scan/Build/RoomModelBuilder.cs`, `A/App/Params/AppParams.cs` (`scan.wallBridge` default), `A/World/Room/WallBridgeJson.cs` (delete or forward), existing scan tests it must update | T1 | shared | `verify.py all` + EditMode green; iOS build links; S1 passes | opus | done (run 1) |
| S0 | Perf baseline on the phone | `docs/plans/standalone-phone/evidence/S0-baseline.md` | T1 (build from branch tip) | — | Table of fps/p95/draws/tris for Home and S2 on `budget_room_120k` | user + main session | todo |
| S1 | RoomPlan device spike | `docs/plans/standalone-phone/evidence/S1-roomplan.md` | T2 | — | 3 scans pass §3 S1; attribute strings recorded | user + main session | todo |
| T3 | Prop format and offline tools | `tools/props/{profile_tool,check,reference,requirements}.*`, `tools/props/README.md`, `tools/kit/kitglb.py` (add the 0.25 m edge check, opt-in flag), `A/Tests/Objects/Fixtures/reference.json`, `A/Tests/Objects/Fixtures/props_min/*` (3 tiny test props + manifest) | T1 | shared | `check.py` passes the fixtures; `reference.py` reproducible (byte-identical twice) | opus | done (run 1) |
| T4 | Source and convert ~48 CC0 props | `tools/props/build_props.py`, `art/props/**` (sources, licences, previews), `A/Telemetry/Resources/AtlasProps/**`, `CREDITS.md` | T3 | leaf | `check.py` passes all; contact-sheet PNG in `art/props/previews/`; credits complete | sonnet | done (run 1) |
| T5 | Matcher, fitter, mesh ops | `A/Objects/{PropEntry,PropMatcher,PropFitter,MeshOps}.cs` | T1, T3 | core | TT2's T5 tests green; `verify.py all` green | opus | done (run 1) |
| T6 | Colour from crops | `A/Objects/PropColour.cs` | T1, T3 | shared | TT2's T6 tests green | opus | done (run 1) |
| T7 | LiDAR cut-out | `A/Objects/ScanCut.cs` | T5 | shared | TT2's T7 tests green | opus | done (run 1) |
| TT2 | Tests for `Atlas.Objects` from this plan + `reference.json` | `A/Tests/Objects/**` except Fixtures, `A/Tests/Objects/Atlas.Objects.Tests.asmdef` | T3 | shared | Compile against §6.4; cover matcher rule, scale formula, edge rule ≤ 0.25 m after scaling, determinism, colour on synthetic images, cut-out on synthetic mesh, zero exceptions on empty input | opus | done (run 1) |
| T8 | On-phone asset path, wiring, save/reload | `A/Telemetry/Assets/{PropSet,OnPhoneObjectsCore,AssetClient,AssetClientInstaller,ForgeTransport}.cs`, `A/App/AtlasApp.cs` (wiring `IScanMeshSink`, photo sink selection), `A/Scan/Build/RoomStore.cs` (re-save after placement), `A/Tests/Telemetry/OnPhoneObjectsCoreTests.cs` | T2, T5, T6, T7 | core | Own tests green (Editor fixture room `sample_4x5_table` + `props_min`: every object leaves first shape for matched/scanCut, deterministic reload, no Forge request); `verify.py all` + EditMode green | opus | done (run 1) |
| T9 | Station optional in UI and params | `A/App/Onboarding/{OnboardingFlow,OnboardingScreens,OnboardingAdapters,OnboardingPorts}.cs`, `A/App/Params/AppParams.cs` (`forge.backend` default `off`), related onboarding tests | T2, T8 | leaf | Onboarding tests updated and green; no TripoSG toggle; app boots to scan with no Station paired | sonnet | done (run 1) |
| T10 | Batched room objects | `A/Rendering/Shaders/Resources/{AtlasWarp.hlsl,AtlasCore.hlsl,AtlasSurface.shader}`, `A/Rendering/Warp/AtlasWarpCpu.cs`, `A/Rendering/Room/{RoomContent,RoomObjectMeshes,RoomBatch}.cs`, `A/Rendering/Tiles/CurvedRoomView.cs`, `A/Rendering/Look/AtlasShaderIDs.cs` | T1, S0 | core | TT3 green; `ShaderContractTests` green (still no keyword); room draws per eye = 2 + 1 + fading on `budget_room_120k` | opus | todo |
| TT3 | Tests for T10 from this plan | `A/Tests/Rendering/RoomBatchTests.cs`, `A/Tests/Warp/WarpBatchTests.cs` | T1 | shared | CPU mirror: batched vertex == per-object path within 1 mm for κ = −1, 0, +1; slot 255 == old path; draw count; zero allocation per frame | opus | todo |
| T11 | Lasting docs and Master Plan amendment | `documents/ATLAS_Master_Plan.md`, `docs/contracts/{lane-a-platform,lane-c-room,lane-d-link}.md`, `docs/contracts/unity-api/arfoundation.md`, `README.md` §2, 3, 9, 10, 13, 15, 18, `documents/ATLAS_Mesh_Design.md` (status note), `docs/INDEX.md` (new), `docs/CHANGELOG.md` | T2, T8, T9, T10, S1 | leaf | Every D-row of §2 appears in the Master Plan; no doc still says Station required for G6/G7 on this branch | sonnet | todo |
| T12 | Reconcile tags with S1 | `A/Telemetry/Resources/AtlasProps/manifest.json` (tags only) | S1, T4 | leaf | Every prop tag is a string S1 recorded, or the tag is removed | sonnet | todo |
| S3 | End-to-end device check | `docs/plans/standalone-phone/evidence/S3-e2e.md` | all | — | §1 end-to-end check | user + main session | todo |
| R | Final review against this plan | fixes only | all | — | Report; reproducible bugs fixed (≤ 2 rounds) | opus | todo |

## 8. Task sections

### T1: World contracts, `Atlas.Objects`, `SetOnPhoneMesh`

**Why.** Every other task builds on these types. This task is core.

**Do.**
- Add everything in §6.1 exactly. `RoomPlanJson` (§6.2) also goes here, so the reader exists before T2.
- Keep `WallBridgeJson` compiling. T2 retires it.
- Create the `Atlas.Objects` assembly, following `Atlas.Kernel`'s asmdef pattern, with references Kernel, World, Unity.Mathematics.
- Add `Atlas.Objects` to the assembly table in all three places (`docs/contracts/README.md`, `verify.py` ALLOWED and its build lists for the phone, Station and Editor configurations, and `AssemblyGraphTests`). Telemetry's row gains Objects.
- Implement `RoomContent.SetOnPhoneMesh` on the generated-mesh swap path: the same cross-fade, the full and light slots both pointing at the mesh, and `RoomObject.Mesh = state`.

**Constraints.**
- `GeometryHash` is unchanged: the golden hashes in tests must not move.
- The `AtlasVertex` layout is unchanged.
- `RoomModelJson.Read` must still read every file in `A/World/Resources/AtlasRooms`.

**Pitfalls.**
- Unity needs `.meta` files for new files. Run Unity batch mode once (`docs/verification/UNITY_SIDE_BRIEF.md` has the command) and commit the generated metas.

### TT1: tests for T1 (from this plan, not from the code)

**Tests to write:**
- The new keys round-trip.
- Absent keys give the defaults.
- Old fixtures load.
- `GeometryHash` is identical with and without the new keys.
- Schema validation of a file with the new keys (call `validate_room.py` in a Python test, or add a vector).
- `FromRoomPlan` for all 16 names plus an unknown name.
- `RoomPlanJson` on a hand-written `atlas.room/1` sample: frame flip, quaternion from x/z columns, attributes.
- `ObjectPaletteColours` against `AtlasPalette` for cells 64..127.

### T2: one RoomPlan session

**Why.** It provides walls, doors, windows and object attributes without the second-session crash (D2).

**Do.**
- **Swift.**
  - Rewrite `AtlasRoomPlan.swift` to emit `atlas.room/1` (§6.2).
  - Add `captureSession(_:didProvide instruction:)` → `instruction`.
  - Read `room.objects` with `category`, `dimensions`, `transform`, `confidence` and attributes. Log the attribute strings with `print` for S1.
  - Keep `stop(pauseARSession:false)` and the scene-depth re-run.
- **C#.**
  - Replace the `IWallBridge` interface with `IRoomPlanCapture { bool Available; bool Start(IntPtr session); string TakeJson(); void Stop(); }`, same registration pattern.
  - `ScanSession.StartFeatures` starts the plugin instead of `ArMeshPlatform.StartRoomCapture`.
  - `ArSessionRig` stops enabling `ARBoundingBoxManager`, and `ArKitMeshPlatform` drops the room-capture calls.
  - `ScanSensors.ReadBoxes` builds `ScannedBox` from `RoomPlanJson` objects (category via `FromRoomPlan`, `Tags` = attributes) and still calls `ObjectBoxMerge.Merge`.
  - `RoomCaptureHint` comes from `instruction`, with the same priority order as today.
  - The depth watchdog (`ScanSession.WatchProvider`, 2 s) applies to the plugin as it did to Unity's capture.
- **Walls.** At `Finalise`, apply the walls, doors and windows through the existing `WallBridgeMerge` when `scan.wallBridge` is on (now the default, D13).
- **Scan mesh.**
  - At `Finalise`, convert the final snapshot's mesh (when `HasScannedMesh`) into the room frame (`RoomFrame` session→room) as a `ScanMesh`.
  - Hand it to an `IScanMeshSink` set on `ScanSystems` (default `NullScanMeshSink`). Call it before photo capture's `BeginRoom`.
  - The snapshot arrays may be passed without copying, since they are immutable.
- **Tags.** Carry tags through `ObstacleBox` → `RoomModelBuilder` → `RoomObject.Tags`.

**Constraints.**
- Bump Guard and depth behaviour are unchanged after the scan.
- Meshing and planes still go off at confirm.
- `scan.route` semantics are unchanged.

**Tests.** Update the existing `WallBridgeTests`, `ScanWallBridgeParamTests` (the default is now on), `ObjectBoxMergeTests` and `ScanFlowTests`, and add tests for tags passthrough and `ScanMesh` frame conversion.

**Done.**
- `verify.py all` and EditMode are green.
- `tools/build_ios.sh` produces an Xcode project that builds and links (the Swift plugin compiles).
- Then S1.

**Pitfalls.**
- `AtlasHands.swift` reads `ARSession.currentFrame`; nothing here may pause the session.
- RoomPlan removes `sceneDepth` from the configuration (Master Plan C.10).
- The 4 Oct crash notes are in `docs/contracts/lane-c-room.md` around l.3003.

### S0: perf baseline (needs the user and the phone)

1. Build with `ATLAS_TEAM_ID=57JG3585G4 tools/build_ios.sh` and install on the phone.
2. Station off. In the on-device menu, choose the room `budget_room_120k` (a fixture) and set `perf.run = tour` in Home, then in Curved Walk.
3. Copy `logs/` via Finder (the Files sharing is on).
4. Run `tools/perf/summarise.py`.
5. Record fps mean, p95 frame, CPU and GPU p95, draws, set-pass and triangles per space, plus the thermal state, in `evidence/S0-baseline.md`.

The main session prepares the build and tells the user what to do; the user runs it.

### S1: RoomPlan device spike (needs the user and the phone)

1. Do three scans: one large room, one small room, one cluttered room, about 60 s each.
2. Pass when:
   - none crashes;
   - the mesh triangle count is > 0 at confirm;
   - the depth image is present after confirm;
   - objects are listed with attribute strings;
   - walls merge with `quality.route = 4`.
3. Write the attribute strings verbatim into `evidence/S1-roomplan.md`.

**On failure:** stop the run, record the crash log (Xcode device logs), and return to planning with the D2 fallback.

### T3: prop format and offline tools

**Why.** T4, T5 and TT2 all depend on the prop format (§6.3).

**Do.**
- Write the four tools in §6.3.
- `reference.py` imports from `station/forge/forge/profile.py` and `cleanup.py`, reusing their scale and profile math, so the C# port has an independent reference. Document the venv in `tools/props/README.md`; numpy is not installed system-wide.
- Add a `check_edges` option to `kitglb.check_piece`, used for props.
- Create 3 tiny fixture props (a box chair, a slab table and a couch made of 3 boxes) plus a `manifest.json` in `A/Tests/Objects/Fixtures/props_min/`.
- `reference.json` holds:
  - profiles of the fixtures;
  - `rotate` for turns 0–3;
  - `fit_score` pairs;
  - scale results for 5 box sizes;
  - the expected `PropMatcher` choice for 6 queries (with and without a profile, with tags, each facing rule).

**Constraints.**
- Deterministic output.
- The profile convention must equal `A/Scan/Build/ObjectProfile.cs`: i along local X, j along local Z, row 0 at the lowest z, 0 = unseen, floor faces excluded. **Confirm this by running both on one fixture mesh**, and add that vector.

### T4: source and convert about 48 CC0 props

**Do.**
- Download the Kenney Furniture Kit (kenney.nl), KayKit Furniture Bits (itch.io; it may need a manual download) and Quaternius furniture (quaternius.com or poly.pizza). Use `curl` into `art/props/sources/<kit>/`, and keep the licence files.
- **If a download needs a browser, stop and ask the user to download that zip** to `art/props/sources/`.
- Pick up to 3 props per RoomPlan category (bathtub, bed, chair, dishwasher, fireplace, oven, refrigerator, sink, sofa→couch, stairs, storage, stove, table, television→screen, toilet, washerDryer). Prefer variants that differ in ways RoomPlan attributes describe: dining, swivel or stool chair; rectangular, L-shaped or single-seat sofa; rectangular or round table; cabinet or shelf storage.
- `build_props.py` (Blender, headless):
  1. Import, join and apply transforms.
  2. Set realistic metres (a reference size table in the script).
  3. Base-centre the origin and set the front to +Z.
  4. Decimate to ≤ 2,500 triangles if needed.
  5. Map materials to 1–4 slots, each with a distinct default cell in 64..127 that is close in OKLab to the source colour.
  6. Split edges to 0.25 m.
  7. Export via `kitglb.py`.
- Run `profile_tool.py` and `check.py`.
- Author tags from Apple's documented `CapturedRoom.Object.Attribute` names. T12 reconciles them.
- Render a contact sheet of all props (`art/props/previews/contact.png`) for the user to look at.

**Done.** `check.py` passes, every category has ≥ 1 prop or is listed as a gap in `art/props/README.md`, and `CREDITS.md` has every source.

### T5: matcher, fitter, mesh ops

**Do.**
- Implement §6.4 for `PropEntry`, `PropMatcher` (the exact rule in §6.4), `PropFitter` and `MeshOps`.
- **Scale:** `λ_k = ln(b_k/e_k)`, `u = exp(mean λ)`, `s_k = u · clip(e^{λ_k}/u, 1−a, 1+a)`. Then move the origin to the base centre.
- **`SplitLongEdges`:** longest-edge midpoint bisection with a shared-midpoint dictionary (port `AtlasMeshBuilder.Subdivide`'s rule) until every edge is ≤ maxEdge.
- **`BakeShade`:** ports `FallbackMeshBuilder`'s shade (lines 80–97) using `AtlasShading`.
- **`ClusterSimplify`:** a uniform grid of the given cell size. Cluster vertices by their mean position, drop degenerate triangles, and stay deterministic in input order.

**Constraints.**
- Engine-free.
- No allocation in the steady state beyond `MeshData` growth.
- Deterministic.

### T6: colour from crops

**Do.** Implement `PropColour`:
- For each face of `fitted`, take the centre and the normal.
- For each view: transform with `inverse(CamToBox)`, keep faces in front of the camera with `dot(normal, toCamera) > 0`, and project with `Intrinsics` into crop pixels. Sample a 3×3 mean.
- Convert sRGB to linear. Take the per-slot weighted median by area times `cos`.
- Choose the nearest `ObjectPaletteColours` cell in OKLab (port the OKLab conversion from `station/forge/forge/colour.py`).
- Cap at 6 distinct cells; merge the least-used slots into their nearest chosen cell.
- **Test with synthetic images:** a solid-colour image must give that colour's nearest cell.

### T7: LiDAR cut-out

**Do.** Implement `ScanCut`:
1. Select triangles whose three vertices lie inside the yawed box expanded by `Margin` and whose class is not Floor (2).
2. Move them to box-local and clip y to ≥ 0.
3. `ClusterSimplify` with a cell starting at 0.03 m, doubled until the triangle count is ≤ `maxTriangles`.
4. `DropSmallParts(0.005)`.
5. `SplitLongEdges(0.25)`.
6. `BakeShade`.

The palette cell comes from the caller (T8 uses `PropColour.ChooseOne`).

### TT2: tests for `Atlas.Objects`

Write these from §6.4 and `reference.json`, not from T5–T7's code.

**Must cover:**
- every `reference.json` vector (tolerance 1e-4 on floats, exact on choices);
- the edge rule after scaling for every fixture prop at sizes 0.3×, 1×, 2× and 3×;
- crack-freedom: no T-junctions after the split (each split edge is shared);
- determinism (two runs give byte-equal `MeshData`);
- the quarter-turn restriction by facing rule;
- category with no props → false;
- `PropColour` on synthetic images;
- `ScanCut` on a synthetic box-shaped mesh in a floor mesh: floor excluded, ≤ maxTriangles, edges ≤ 0.25.

### T8: on-phone asset path

**Why.** This is the runtime replacement for the Forge.

**Do.**
- `PropSet` (loads `AtlasProps`; verifies sha256 like `KitSet`).
- `OnPhoneObjectsCore`: engine-free, implements `IObjectPhotoSink` and `IScanMeshSink`, follows the order in §6.5, and keeps `AssetClientCore`'s first-shape behaviour (`RoomBuilt`/`TickBuiltIn`).
- `AssetClient` adapter: JPEG decode on the main thread at ≤ 2 objects per frame; `MeshData` → `Mesh`; `IRoomContent.SetOnPhoneMesh`.
- `IAssetRoom.PlaceOnPhone` sets `Prop` and `PropCells`.
- Set the `forge.backend` default to `off` in `AssetClientInstaller.cs:68`, matching T9's change in `AppParams.cs`.
- Wiring in `AtlasApp`:
  - when `forge.backend` is `off` (the new default, T9), the photo sink is `OnPhoneObjectsCore`;
  - otherwise it is today's `AssetClientCore`, unchanged;
  - `ScanSystems`' `IScanMeshSink` is `OnPhoneObjectsCore`.
- Saved rooms: after the last placement, re-save the room when saving is on (`scan.teamPrescan`). On load, rebuild Matched objects from `prop` and `propCells`.

**Constraints.**
- The frame loop never waits on this work.
- Zero allocation when idle (as `AssetClientCore`'s test asserts).
- Crops are released after colouring (rule 10).
- Budget: the sum of placed triangles stays ≤ 120,000; objects beyond the 32 largest keep first shapes, as today.

**Done.** Own tests green, `verify.py all` green, EditMode green.

### T9: Station optional in UI and params

**Do.**
- Set `forge.backend` to default `off` in `AppParams.cs:250`. T8 makes the same change in `AssetClientInstaller.cs:68`, which it owns.
- Remove the onboarding TripoSG toggle (`OnboardingAction.ToggleForge`, its label and its screen row).
- Keep the Station step: it is already skipped when `_station.Available` is false (`OnboardingFlow.EnterStation`).
- Update onboarding tests.

### T10: batched room objects

**Why.** One draw per eye for all objects (D9). It runs after S0, so the before-numbers exist.

**Do.**
- Implement §6.6.
- `CurvedRoomView.Upload` fills the anchor array instead of per-object materials, and still sets the per-object material for fading objects.
- Home (mode 0) fills local-to-world matrices.
- Keep `RoomContent.DrawCallsPerEye` honest.
- Rebuild the batch on a worker using double buffers, and swap on the main thread.

**Constraints.**
- No shader keyword, and no `MaterialPropertyBlock`.
- The frozen `UnityPerMaterial` order is unchanged.
- Gates, portal clip and near fade keep working (they are per-material and the same for all objects).
- Zero allocation per frame.

**Done.** TT3 green, `ShaderContractTests` green, EditMode green, and the S3 perf comparison.

### TT3: tests for T10

Write these from §6.6.

- The CPU mirror for batched vertices against the per-object path, within 1 mm, for κ = −1, 0 and +1 at distances up to 3 units.
- Slot 255 gives the old path, bit for bit.
- `DrawCallsPerEye` on `budget_room_120k` = 3 with no fade.
- A fading object is excluded from the batch until its fade ends.
- No allocation per frame once the batch is built.

### T11: docs

**Do.**
- Amend the Master Plan per D12, with new Appendix D rows dated 2026-10-06 naming what each supersedes:
  - D20 → matched props;
  - D22 and §12.3 → the Station is optional and no gate needs it;
  - D23 → one batched material for room objects;
  - §4.2 routes table → one RoomPlan session, route 4 the default;
  - §7.4 → on-phone path;
  - G6/G7 → the asset round trip is replaced by matched-prop checks;
  - honesty rules → "matched", never "generated".
- Update the lane contracts (lane-c §10 routes and §12 saved rooms; lane-d §4.4 and the Forge status; lane-a shader contract §10 with the `COLOR.b` slot and `_AtlasObjectAnchors`).
- Update `arfoundation.md` D7.
- Update the README sections listed in the table.
- Create `docs/INDEX.md`: a one-line map of each doc and which doc wins on conflict.
- Add a `docs/CHANGELOG.md` entry (newest first).
- State plainly that this lives on the experiment branch.

### T12: reconcile tags

Replace or remove every prop tag that is not among S1's recorded strings.

### S3: end-to-end device check

The §1 check, run by the user, with the main session reading the logs and the CSV. Record it in `evidence/S3-e2e.md`, including photos or screenshots of the props in Home if the user takes them.

### R: final review

One fresh Opus agent checks the result against this plan:
- every D-row honoured;
- every interface as written;
- the tests are the plan's.

It fixes bugs it can reproduce (at most 2 rounds) and reports the rest.

## 9. Run plan

- **Order.** T1 and TT1 start together, then TT3. Next, T2 and T3 in parallel. After T3: T4, T5, T6 and TT2 in parallel. After T5: T7. After T2, T5, T6 and T7: T8. After T8: T9.
- **Device stops.** S0 (after T1) and S1 (after T2) pause the run for the user. T10 starts as soon as S0 is recorded; T12 and T11 wait on S1.
- **Parallel safety.** Tasks running at the same time own separate files. `AppParams.cs` is owned by T2, then T9 (T9 depends on T2). `RoomContent.cs` and `RoomObjectMeshes.cs` are owned by T1, then T10.
- **Tests from the plan.** TT1 (for T1), TT2 (for T5–T7) and TT3 (for T10) are written by separate agents from this plan.
- **Integration check.**
  1. `python3 tools/headless/verify.py all`.
  2. Unity batch-mode EditMode on the Mac.
  3. `tools/build_ios.sh` → install → S3.
  4. What to look at: Home with your room's props facing right and in plausible colours; Curved Walk with props bending without cracks; the HUD draw count.
- **Rough cost.**
  - About 16 agent tasks: 12 Opus (T1, T2, T3, T5, T6, T7, T8, T10, TT1, TT2, TT3, R) and 4 Sonnet (T4, T9, T11, T12).
  - T4 is the longest: asset sourcing, conversion and visual checks, perhaps 2–3 h of agent time.
  - Agent wall time is about 6–8 h, plus three device sessions with the user (S0 about 20 min, S1 about 30 min, S3 about 45 min).
- **Commits.** Agents share this branch and tree and do not commit; each run ends in one commit on `experiment/standalone-phone`, which is pushed. **Never merged into `main`.**

## 10. Doc map

**Relies on:**
- `documents/ATLAS_Master_Plan.md` (§4.2, §4.5, §6.4, §7.4, §8, D20–D24, gates);
- `documents/ATLAS_Mesh_Design.md` (profile, fit, budget rules);
- `docs/contracts/README.md` (assembly table, F7, F8);
- `docs/contracts/lane-c-room.md` §10–12;
- `docs/contracts/lane-d-link.md` §4;
- `docs/contracts/lane-a-platform.md` §10, §15;
- `docs/contracts/roommodel.schema.json`;
- `docs/contracts/unity-api/arfoundation.md`;
- `station/forge/forge/{profile,cleanup,colour}.py` (reference math).

**Changes (T11, on this branch only):**
- the Master Plan (D12 list);
- lane-a, lane-c and lane-d contracts;
- `arfoundation.md`;
- `roommodel.schema.json` (T1);
- the README;
- `docs/INDEX.md` (new);
- `docs/CHANGELOG.md`;
- `CREDITS.md` (T4).

## 11. Run notes (added at `/run`, 2026-10-06)

**Order change (agreed with the user):** S0 and S1 are one phone session after run 1. Run 1 (`standalone-phone-core`) is T1, TT1, TT3, T2, T3, T4, T5, T6, TT2, T7, T8, T9. Run 2 (`standalone-phone-finish`) is T10, T12, T11, R. The S0 baseline therefore already includes matched props, so T10's before/after measures batching alone.

**Stale facts fixed:** `RoomStore.cs` is at `A/Scan/Build/`. T2 also owns `A/Scan/Routes/WallBridgeMerge.cs`.

**Toolchain for every agent (headless only):**
```sh
export PATH="$HOME/.dotnet:$PATH"
export ATLAS_UNITY_MANAGED="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting/Managed"
export ATLAS_UNITY_IOS_EXT="/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/iOSSupport"
python3 tools/headless/verify.py build <Assembly> --config all --tag <taskid>
python3 tools/headless/verify.py test <Assembly>.Tests --tag <taskid>
python3 tools/headless/verify.py rules --tag <taskid>
```
- Always pass `--tag <your task id>` (agents run at the same time).
- Build and test only the assemblies you touch and their test assemblies. Other agents are editing other assemblies right now; a compile error in a file you don't own is not yours to fix — report it.
- To wait for a dependency's assembly to compile: `verify.py wait <Assembly> --tag <taskid>`.
- **Do not launch Unity** (Editor, batch mode, `build_ios.sh`). It is one instance per project; the main session runs EditMode, generates `.meta` files and builds iOS at integration. Write `#if !ATLAS_HEADLESS` + `[Category("Unity")]` for engine tests as the repo does.
- Do not commit. Do not edit files outside your task's "Owns files" cell; if you must, stop and report why.

### Run 1 log (`standalone-phone-core`, wf_307d207d-348, 2026-10-06/07)

- **Order changes.** TT3 moved to run 2 beside T10 (its tests use `RoomBatch` and the new warp overload; written early they would stop `Atlas.Rendering.Tests` compiling). Check steps added: T1-check (T1 against TT1) and Objects-check (T5–T7 against TT2), because tests and code were written at the same time.
- **Ownership gaps fixed by the main session / check steps.** The 4 `IRoomContent` test fakes got a `SetOnPhoneMesh` stub. T1-check also fixed `World/Lint/WorldLint.cs`, `Blades/Playground/Shell/Home/HomeAudit.cs` and `tools/rooms/roommath.py` to know `matched`/`scanCut`. T9 also edited `OnboardingInstaller.cs` (2 lines, the removed Forge switch).
- **Interface notes.** `PropFitter.Build` has no yaw, so it bakes shade for yaw 0; `OnPhoneObjectsCore` calls `MeshOps.BakeShade(mesh, yaw)` after it. `ScanCut` output is in the category tint cell (< 64); T8 recolours it with `PropColour.Apply`. `FromRoomPlan` lives on `ObjectCategories`. `RoomPlanCapture` has an extra `int Skipped`. `SetOnPhoneMesh` leaves the light slot empty, so both levels draw the full copy.
- **T4.** 39 Kenney props; dishwasher and fireplace have none (they keep the built-in template). KayKit/Quaternius were not downloaded. Props are pastel until recoloured.
- **Known.** Splitting long edges grows big props fast (couch fixture at 3× → 5,888 tris), so large objects fall back to the next candidate or the first shape more often. ScanCut bottoms can float ~2 cm (cluster means). XR Simulation has no furniture boxes now (ARBoundingBoxManager off).
- **Integration.** `verify.py all`: no new failures against the pre-run baseline (pre-existing: `SpineTests.SpaceIds_CodesScenesAndGroups`, `MenuModelTests.Stepping_…`, 37 `E2E` hands/rehearsal tests; `B10_NoPerFrameCompute_ZeroAlloc` is flaky). Unity EditMode: 3,699 pass, 27 fail, none in a file this run changed (hands-on-by-default, menu scale, Playground `Destroy` in edit mode, Glued avatar, Network timing). `tools/build_ios.sh` exports, builds and links; the exported `AtlasRoomPlan.swift` equals the source. Not installed: the phone was not connected.
- **Device.** Batphone is an iPhone 14 Pro Max (the plan says 15 Pro Max); S0/S1/S3 numbers are for the 14.
