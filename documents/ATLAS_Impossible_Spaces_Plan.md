# ATLAS: Impossible Spaces & Escher-like Worlds Plan

*3 Oct 2026, for the 36-hour build. This follows the Engineering Spec (D8 topology first, D10 stencil portals, §3.4 phone/PC split, §6.2 world model, §6.4 tile rules, §6.7 tile solver), the Build Pipeline (stencil Render Objects, Glued Room copies, art direction) and the Implementation Brief (Glued Room, impossible rooms, Station Zero). It reuses the 4D Plan's slicer for one beat.*

---

## 1. What the brief asks for, and what we build for each line

The brief: *"Spaces where 'up' and 'down' loop continuously, corridors that fold into themselves, or environments where perspective breaks down and distant objects appear larger than nearby ones."*

| Brief phrase | Our exhibit | Mechanism | Honest name for it |
|---|---|---|---|
| "up and down loop continuously" | **Ascending Room**: walk through any wall and come back in through the opposite one, one step higher. Copies of the room climb forever ahead and descend behind | Glued Room copies with a **vertical shift** in the gluing | A *sheared torus*: flat room, walls glued by translation + lift |
| "up and down loop" (stronger, opt-in) | **Relativity Hatch** (Station Zero core): each hatch has its own "up" | Portal gluing by a 90° rotation; your floor always stays the real floor | Charts glued by SO(3) rotations |
| "corridors that fold into themselves" | **Folding Corridor**: four right turns never bring you back; the building keeps going | Overlapping rooms re-placed out of sight (tile solver + change blindness) | Impossible spaces (Suma 2012); topology, not curvature |
| "corridors that fold into themselves" | **Bigger-Inside Booth**: a 0.8 m cabinet opens into a 2 m gallery | Stencil portal + overlapping footprint | A room glued to a door that doesn't fit |
| "perspective breaks down" | **Penrose Gallery**: an impossible triangle and Escher stair that are only "real" from one taped spot; a live "camera photo" shows the impossible view while you walk around the truth | Viewpoint-locked (anamorphic) geometry | A perspective illusion: works from one eye point only |
| "distant objects appear larger" | Same gallery: a **reverse-perspective corridor** where the far door looks bigger than the near one (Ames-room trick, viewpoint-locked) | Forced perspective | An illusion; the honest curved version is S³ (roadmap) |

**Framing rule (spec, Brief §4):** call these *glued*, *wrapped*, *overlapping* or *illusion*. Never call them curved. The curvature lives in other blades (H² airway, S² sky). One beat (§3.6) uses the 4D slicer to explain overlapping rooms honestly.

---

## 2. Judge flow: "Escher House" (about 2.5 min, one blade)

| Time | Beat | Geometry code | What the judge does | Narrator line |
|---|---|---|---|---|
| 0:00 | **The door in your room** (AR) | E → P | In camera passthrough, a free-standing door frame stands on the real floor. Through it they see a lit gallery that can't fit behind it. Walking around the frame shows nothing behind it. Walking through fades to VR | "Nothing behind it. Everything through it." |
| 0:20 | **Bigger-Inside Booth** | P | Walk into a 0.8 m cabinet and find a 2 m gallery. Look back: the door is behind you, the cabinet's outside is gone | "This room is bigger than the box it's in." |
| 0:45 | **Folding Corridor** | P (overlap) | Follow a corridor: right, right, right, right. They don't arrive back; a new room is there. The Knife Map shows the physical path (one small loop) beside the virtual path (four rooms) | "Four right turns, and you're somewhere new." |
| 1:20 | **Ascending Room** | T (sheared) | Walk east through the wall and re-enter from the west. Ahead, copies of the room climb like stairs forever; behind, they descend. Their own copy is visible one step lower | "Every wall leads to the opposite wall, one step higher. Up loops back into down." |
| 1:55 | **Penrose Gallery** | E (illusion) | Stand on the taped X: an impossible triangle and an endless stair. A framed "photo" on the wall keeps showing that view. Step off the X and walk around: it's an open bent bar | "Escher needs one eye and one spot. You have two eyes and legs." |
| 2:20 | **4D peek** (optional) | 4s | One button slides the 4D slicer so the overlapping rooms ghost through each other | "The rooms that overlapped were stacked in a fourth direction." |
| 2:30 | Exit through the AR door | E | Passthrough returns | — |

**Short slot (5 min total demo):** Booth → Ascending Room → Penrose Gallery (about 75 s).

**Comfort order:** everything is real walking at 1:1. No gains, no world rotation, the floor is always the real floor. Relativity Hatch (rotating "up") is opt-in only, with a seated alternative.

---

## 3. How each exhibit works

### 3.1 Shared world model (from Spec §6.2)
- **World:** a graph G = (V, E). Nodes are `RoomModule`s; edges are `Gluing`s with a 4×4 transform `T_e` (translation, translation + lift, rotation or reflection).
- **Physical embedding:** each visible room has a pose P(v) ∈ SE(2) on the 0.5 m floor tile grid. It is recomputed per visit when the room is fully out of view. **Never store anything in physical coordinates;** everything is room-local.
- **Crossing a portal:** when the head crosses the portal plane, the current room becomes the destination. New physical pose = old pose × T_e⁻¹ restricted to the floor (so the floor stays put), and carried objects are transformed by T_e. Reflections flip parity, so mirror the object and swap its culling.
- **Rendering:** stencil portals (Spec D10, Build Pipeline §4). A doorway mask quad writes stencil ref N (`ColorMask 0`, `ZWrite Off`); a Render Objects feature draws layer RoomN where stencil = N; geometry in front of the door is clipped in the shader with `clip(dot(_PortalPlane.xyz, posWS) + _PortalPlane.w)`. Stencil refs 0–15, recursion depth 1 (2 only if spike 0.6 passes), a **portal box** for the moment one eye has crossed and the other hasn't.

### 3.2 The door in your room (AR entry)
- **Hub mode A (passthrough):** the ARKit/ARCore camera image as background, mono at a fixed depth (Spec D16 final pass). The door frame is a real 3D model anchored on a detected floor plane at the taped start line.
- **Through the door:** a stencil portal into the first VR room. Walking around the frame shows the real room (passthrough) behind it. That's the impossibility, in AR.
- **Crossing:** fade passthrough out over 0.4 s as the head passes the portal plane; VR mode inside. Safe Harbour = back to passthrough within 300 ms.
- **Prototype first:** this beat runs in WebXR on Android Chrome with three.js stencil portals, reusing the `demo/4d-ar` scaffolding (hit-test placement, DOM overlay).
- **Reference:** Tongzhou-Yu/ar-portal-arfoundation-urp (MIT) for the Unity version.

### 3.3 Bigger-Inside Booth
- **Outside:** a 0.8 × 0.8 m cabinet mesh in room A. **Inside:** room B, 2.0 × 2.0 m, whose footprint overlaps room A's floor behind the cabinet.
- Room B is drawn only through the cabinet door (stencil). Once the head is inside, room A's layer is hidden and B is drawn normally.
- **Rule (Spec §6.4):** co-visible rooms never overlap. A and B overlap physically, but from inside B, A is only visible through the door, where the stencil clips it.
- Clone objects that straddle the door (Spec §5.3) so a hand reaching through is drawn on both sides.

### 3.4 Folding Corridor (impossible overlap + tile solver)
- **Modules:**
  - L-shaped corridor segments, 0.5 m tile footprints, door sockets on tile edges.
  - Three small rooms (2 × 2 to 3 × 3 tiles) with distinct landmarks (a plant, a painting, a lamp), so the judge notices they're new.
- **Logical graph:** a fixed, authored loop of 4 corridors and 3 rooms for the demo, which guarantees the "four right turns" moment. The seeded generator (Spec §6.5) is the roadmap.
- **Placement:** each next module is placed by the **tile solver** (Spec §6.7 pseudo-code) when its door is closed or the corridor bend hides it. Constraints:
  - Every tile is inside the eroded floor (0.3–0.5 m margin).
  - Every exit socket faces at least one free tile.
  - Zero overlap with rooms that can be seen at the same time.
  - At most 50% overlap with hidden rooms.
  - **Turn-consistency rule:** taking the right door means turning right on the map.
- **Swap trigger (Build Pipeline §4):** swap only when the old room's bounds fail `TestPlanesAABB` for **both** eye frustums, the user is outside the overlap, and 3 frames of hysteresis have passed.
- **Phone vs PC:** the phone solver runs everything. The PC may only suggest which module comes next while doors are closed (Spec §3.4, principle 2). It never moves anything while visible.

### 3.5 Ascending Room (sheared glued room)
- **The Glued Room engine with one change:** copy (i, j) is drawn at `translate(i·W, i·h, j·D)` instead of `translate(i·W, 0, j·D)`, with W = D = 1.5 m and h = one stair step (0.18 m).
  - Room copies march upward to the east and downward to the west. North–south stays level, or add a second lift for a diagonal staircase.
- **The user never moves vertically.** Their own copy is at height 0 and the floor under them is the real floor. Only the other copies are offset, so there is no vestibular conflict.
- **Dressing:**
  - Stair treads on the room floor, so each copy's stairs line up with the next copy's floor and the whole view reads as one endless Escher staircase.
  - A banister that continues across copies.
  - The self-avatar (head and torso) in every copy except (0, 0), so they see themselves one step below.
- **Draw:** 7 × 3 copies (east–west longer), with fog by copy index to hide the edge and the lightness step from the art direction.
- **Maths card:** gluing g(x, y, z) = (x + W, y + h, z), k(x, y, z) = (x, y, z + D). The room is a fundamental domain of E³/⟨g, k⟩ restricted to a slab. It is flat everywhere, and "up" wraps around because the gluing lifts.
- **Klein option** (already in Glued Room): mirror alternate rows, so the staircase also flips handedness.

### 3.6 Penrose Gallery (perspective illusions, honest)
- **Penrose triangle:** three bars built in 3D as an open "U" with a gap. From one eye point the two ends line up and it looks closed and impossible. The taped X on the floor is that point (eye height calibrated).
- **Escher stair:** four flights in 3D whose top and bottom don't meet. From the X, the step edges line up into a closed loop.
- **Reverse-perspective corridor (Ames trick):** the far doorway is built larger and slightly raised, so from the X a person-sized figure at the far end looks bigger than one at the near end.
- **The stereo problem:** with two eyes 64 mm apart, binocular depth weakens monocular illusions.
  - Fix 1: a **"photo" frame** on the wall rendered from a fixed mono camera at the X (one render texture, 512², updated each frame). The photo always shows the impossible view, while the judge walks around the real object. This is the teaching beat, and it works in stereo.
  - Fix 2: a "close one eye" prompt on the X for the live illusion.
- **Comfort:** a static object, ordinary walking. The lowest-risk exhibit.

### 3.7 4D peek (optional; reuses the 4D slicer)
- Spec §6.2 framing: give each overlapping room a w-range (room A at w = 0, room B at w = 1, doorways are w-ramps). The user always lives in the slice w = w(t).
- One button slides the slice w₀ from 0 to 1 for 2 s, using `ISlicer4D`. Both rooms ghost through each other at 30% opacity, showing that the overlap is a stack in a fourth direction.
- It is exact and honest, and it ties the Impossible and 4D sections into one engine story.

### 3.8 Relativity Hatch (Tier B, opt-in; the Station Zero core)
- **Modules:** cube rooms with hatches on walls and ceiling. Each gluing is a 90° rotation.
- **Comfort rule:** the room you're in always has its floor on the real floor. You see neighbouring rooms rotated through their hatches (people standing on walls, as in Escher's *Relativity*). When you cross, the new room is re-embedded upright, so the world never rotates around you; only the portal transform changes.
- **Maths card:** walk once round a cube corner, crossing the three faces that meet there, and you come back turned by 90°. Three squares cover only 270° of a full 360° turn, so the corner has a 90° angle deficit. That is discrete holonomy: "up" drifts. Gauss–Bonnet holds: 8 corners × 90° = 720° = 4π. (Don't quote a single composed-rotation angle for a hatch sequence; it depends on which hatches and in what order.)
- **Same engine as the hyperbolic walk:** map 1 m floor tiles to cube faces ({4,3}: 3 squares per corner) instead of {4,5}. Walking around a physical corner (4 squares) on a cube (3 squares) rotates you 90°, the positive-curvature twin of Holonomy.

### 3.9 Roadmap (not this weekend)
- **Alice Door:** a portal with a scale factor (a similarity gluing). The participant shrinks; walking speed scales too, which is a translation gain and so a comfort risk.
- **Honest "distant objects larger":** the S³ Small Planet blade (two-pass render, Weeks 2002).
- **Folded Building:** a real venue folded into the booth (Spec §11, Card 2).
- **Procedural Escher world:** cyclic graph grammar + style cascade (Spec §6.5).

---

## 4. Architecture

```
Assets/Atlas/World/
  RoomModule.cs         ScriptableObject: prefab, footprint tiles (0.5 m), door sockets (tile edge + facing), style id
  Gluing.cs             From, To, double4x4 Transform, Kind {Portal, Wall, WallLift, Hatch, Mirror}
  RoomGraph.cs          authored graph for the demo (seeded generator later)
  Embedding.cs          per-visit SE(2) pose per room; never persisted
  TileSolver.cs         Spec §6.7 solve(); eroded free tiles from the scan or tape outline
  CrossingTracker.cs    head vs portal plane → current room; transforms carried objects; parity flip
  CoVisibility.cs       TestPlanesAABB on both eye frustums + hysteresis → swap permission
Assets/Atlas/Rendering/
  StencilPortalFeature  URP Render Objects per room layer (stencil 0-15), portal mask quads, portal box
  PortalClip.hlsl       clip by portal plane (in the shared palette shader)
  GluedCopies.cs        copy grid with optional lift h and mirror parity (Glued Room + Ascending Room)
  ViewpointPhoto.cs     fixed mono camera at the X → 512² render texture on the wall frame
Assets/Atlas/Blades/EscherHouse/
  EscherHouse.cs        beat sequencer (AR door → booth → corridor → ascending → gallery → exit), Knife Map state
  ArDoor.cs             floor-anchored door in passthrough; fade to VR on crossing
Tests/EditMode/World/   gluing algebra, solver constraints, crossing parity
```

**Per-frame order (Spec phone frame loop):** poll inputs → safety → **crossing check** (portal planes, update current room, transform carried objects) → co-visibility and **swap if allowed** → render each eye (current room + stencil portals at depth ≤ 1 + glued copies) → log.

**PC role (Spec §3.4):** optional. It logs, shows the physical-vs-virtual path on the Knife Map, and may suggest the next module behind a closed door. Everything else runs on the phone, and the blade runs fully offline.

---

## 5. Task list (one owner for World/portals, one for content)

| # | Task | Owner | Est. | Done when |
|---|---|---|---|---|
| 1 | `RoomModule`, `Gluing`, `RoomGraph`, `Embedding` + edit-mode tests | Dev | 1.5 h | Gluing inverse/compose tests pass; mirror flips parity |
| 2 | `StencilPortalFeature` + `PortalClip` + one door between two rooms | Dev | 2.0 h | Stereo-correct portal at 60 fps; < 1.5 ms GPU both eyes (spike 0.6) |
| 3 | `CrossingTracker` + portal box | Dev | 1.5 h | No flicker when one eye is through; carried mug transformed correctly |
| 4 | **Bigger-Inside Booth** | Dev + art | 1.0 h | Naive tester says "bigger inside" unprompted |
| 5 | **Ascending Room** (lift h in `GluedCopies`, stair dressing, self-avatar) | Dev + art | 1.5 h | 7 × 3 copies at 60 fps; staircase reads as continuous |
| 6 | `TileSolver` + `CoVisibility` + **Folding Corridor** (authored 4 corridors + 3 rooms) | Dev | 3.0 h | Four right turns in 3 × 3 m; no co-visible overlap ever (logged) |
| 7 | **Penrose Gallery**: triangle, stair, reverse corridor, `ViewpointPhoto` | Art + dev | 2.0 h | The photo shows a closed triangle; walking around shows the gap |
| 8 | **AR door** entry/exit with passthrough fade | Dev | 1.5 h | Passthrough ↔ VR in < 0.5 s; Safe Harbour < 300 ms |
| 9 | Knife Map: physical vs virtual path, gluing cards | Dev | 1.0 h | Live paths and equations for each exhibit |
| 10 | 4D peek (if the 4D slicer is done) | Dev | 0.5 h | Rooms ghost through each other on one button |
| 11 | Sound: portal chime pitched by depth, step tick, room beds | Art | 0.5 h | — |
| 12 | Rehearsals with 3 naive people | All | 1.0 h | Every beat understood; Fast Motion Sickness ≤ 2 |

**Total:** about 17 h across two people.
- **Must-have** (tasks 1–5, 7 = Booth + Ascending Room + Penrose Gallery): about 9.5 h.
- **Target:** + Folding Corridor + AR door.
- **Stretch:** 4D peek, Relativity Hatch.

**Prototype-first option:** tasks 2, 4, 5 and 8 can be prototyped in WebXR/three.js on Android in about 3 h, like `demo/4d-ar`, to check readability before the Unity build.

---

## 6. Content and art (Build Pipeline §2–3)

- **Look:** "lit architectural diagram". One palette texture, vertex-colour ambient occlusion, edge lines in the shader, fog per room hue, emissive portal frames, no bloom. It is also the Monument Valley / Manifold Garden language, which suits Escher.
- **Modules:** ProBuilder greybox on the 0.5 m grid, merged per room. Stairs and the Penrose pieces are modelled in Blender (exact angles for the viewpoint alignment).
- **Landmarks:** one distinct prop per room (CC0: Kenney, Quaternius, KayKit), so overlap swaps are noticed as *new rooms*, not glitches.
- **Penrose alignment:** compute the hidden gap analytically from the X-point camera: put the far bar's end on the ray from the eye through the near bar's end, so the illusion is exact from that point.

---

## 7. Safety and comfort

- Real walking only, gains at identity, the floor is always the real floor (Spec D8).
- Every module tile lies inside the eroded floor; co-visible rooms never overlap; warnings at 2 / 1 / 0.5 m; crouch-to-exit; tracking loss → passthrough (4D Plan §1 rule 7).
- No rotating "up" in the main path (Relativity Hatch is opt-in, seated alternative available).
- The logical maze stays at or below 3 × 3 to 4 × 4 tiles per region (Koltai 2020).
- Turn consistency (right door → right turn).

---

## 8. Tests and measures

- **Edit mode:**
  - T_e · T_e⁻¹ = I.
  - The composed loop in the Ascending Room = a translation (W·n, h·n, 0).
  - A mirror gluing flips the determinant sign.
  - The solver never returns a placement outside the eroded tiles, or overlapping a visible room.
- **Runtime logging:**
  - Co-visible overlap area (must be 0).
  - Swap count.
  - Physical vs virtual path length.
  - Portal GPU time.
- **People (rehearsals):**
  - "Did any room change while you were looking?" (target: no).
  - "Point to where you started" after the corridor: compare with the physical and virtual answers. This is the pointing probe from the Brief.
  - Fast Motion Sickness before and after.

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| Judges: "it's just portals" (Field Guide warning) | Name the space per exhibit (sheared torus, impossible overlap, illusion). Show the Knife Map's physical path vs virtual path. Tie to the 4D peek and the cube-corner holonomy card |
| A swap is seen (pop) | Both-eye frustum test + hysteresis + swap only behind closed doors/bends; landmarks make new rooms read as intended |
| Portal flicker when one eye crosses | Portal box (Spec D10); crossing uses the head centre, not an eye |
| Stencil cost in stereo | Depth 1 by default; spike 0.6 budget < 1.5 ms; render-texture fallback at half resolution |
| Penrose illusion fails in stereo | The mono "photo" frame carries the beat; "close one eye" prompt is the bonus |
| AR passthrough in stereo is awkward (Build Pipeline §1.2) | The AR door is mono passthrough at fixed depth; everything inside is VR |
| Tile solver finds no placement | Fallback module (smaller room) or a narrative reset ("the lights flicker, take the other door") |
| Scope | Must-have = three exhibits that reuse existing engines (Glued Room copies, one portal, one static gallery) |

---

## 10. Claims

**Say:**
- "Every room is flat. What's impossible is how they're glued together."
- "Every wall leads to the opposite wall, one step higher: up loops back into down."
- "Escher's drawings need one eye and one spot. Here you can walk around the trick."
- "The overlapping rooms are slices of a stack in a fourth direction."

**Avoid:**
- Calling portals, glued rooms or illusions "curved" or "hyperbolic".
- "First impossible-space VR" (Suma 2012, Unseen Diplomacy and Tea For God exist).
- Claiming the Ames corridor is "distant objects really appearing larger"; it's an illusion (the S³ version is the honest one).

---

## 11. References

- Suma, Lipps, Finkelstein, Krum, Bolas (2012). Impossible Spaces. IEEE TVCG 18(4).
- Vasylevska et al. (2013). Flexible Spaces. IEEE 3DUI.
- Koltai et al. (2020): maze complexity in impossible spaces.
- Warren et al. (2017). Wormholes in virtual space. Cognition.
- Tea For God devlog (void-room) and Unseen Diplomacy.
- Penrose & Penrose (1958). Impossible objects: a special type of visual illusion. Br. J. Psychology.
- Ames room; Escher: *Ascending and Descending* (1960), *Relativity* (1953), *Waterfall* (1961).
- Code: SebLague/Portals (MIT), daniel-ilett/portals-urp, Tongzhou-Yu/ar-portal-arfoundation-urp (MIT), HackerPoet/NonEuclidean (MIT), danielilett.com impossible geometry with stencils.
