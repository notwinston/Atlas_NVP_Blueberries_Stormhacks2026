# Atlas.Lung.Core

Engine-free cores of lane A's Lung Mission runtime (Master Plan §10.2.5, §10.2.6, §10.2.8, Appendix A.8).
Plain C#, `System` only, netstandard2.1 / C# 9, deterministic, no clock, no per-call allocation on the frame
paths. Depends on `Atlas.Cases` (the case contract readers) and nothing else.

Education and research visualisation. Not for clinical use.

## What is here

| File | Work package | Content |
|---|---|---|
| `TubeMesh.cs` | P2-A20 | `TubeLoader.Load(Tube4D)` → `TubeMesh` (flat arrays shaped like Phase 1 `Mesh4D`), slab ranges, `Orientation.OrientOutward` |
| `ReferenceSlicer4D.cs` | P2-A20 (and the core of P2-A21) | `SliceFrame`, `ReferenceSlicer4D.Slice` (A.8 cut, packed or fixed slots, no allocation), `SlabIndex`, `TubeSliceState` (fixed slots, dirty flag, slab skipping) |
| `TubeGenerator.cs` | tests of P2-A20, P2-A28 | tesseract (48 Kuhn tetrahedra), icosphere, Lujan and constant-speed sphere tubes |
| `ScannerTilt.cs` | P2-A28 | z–τ hyperplane for table speed v (tan θ = c / v), 5 detents, v read-out, periodic copies, apparent length |
| `PhaseColour.cs` | P2-A22 | u = frac(τ / Λ), cyclic row lookup (nearest and linear), stand-in row |
| `BlendShapePlayer.cs` | P2-A24 | weights of the 10 chest phases with wrap 9 → 0, largest vertex step per frame |
| `Gating.cs` | P2-A27 | arc (start bin, length) → volume from `table.json`, duty, best arc of equal duty, ITV reduction, arc from two flags |
| `Scoring.cs` | P2-A29 | `OccupancyGrid` (trilinear `occ.r8`), M2, M3, M4, payload structs `M2Place`, `M4Gate`, `M3Pick`, event names |

Meant to move into the Unity assembly of the 4D space: `ATLAS/Assets/Atlas/Spaces/Slicer/` (`Atlas.Spaces.Slicer`,
S6), next to a copy of `Atlas.Cases`. Namespaces can stay. Nothing was written under `ATLAS/`.

## How `TubeLoader` output maps onto Phase 1 `Mesh4D`

`Atlas.Kernel.FourD.Mesh4D(float4[] vertices, int[] tetrahedra, int[] tetCell, int[] edges, int cellCount, float wPeriod, float[] vertexData)`:

| `Mesh4D` argument | `TubeMesh` field | Note |
|---|---|---|
| `vertices` (`float4[]`) | `Vertices` (`float[]`, 4 per vertex: x, y, z, w) | Same memory layout. `var v = new float4[mesh.VertexCount]; for (i) v[i] = new float4(V[4i], V[4i+1], V[4i+2], V[4i+3]);` (or one `MemoryMarshal.Cast`). x, y, z are display metres (M = 15 already applied, origin at `origin_mm`), w = τ in metres |
| `tetrahedra` | `Tetrahedra` | Same. Stored slab by slab, as in the file; indices of a tetrahedron reordered only to orient it |
| `tetCell` | `TetCell` | The slab (phase layer) k, 0..9 |
| `edges` | – | pass `null`; the tube has no wireframe shadow |
| `cellCount` | `CellCount` | 10 |
| `wPeriod` | `WPeriod` | Λ = 1.5 |
| `vertexData` | `VertexData` | τ / Λ in [0, 1]: the `tau` vertex channel of P2-A22 (ring 10 carries 1.0) |

- **Orientation.** The case contract leaves the sign of a tetrahedron open; `Mesh4D` needs every tetrahedron
  oriented so that `Mesh4D.TetNormal` points out of the body, or the slice is wound at random.
  `TubeLoader` orients them (coherence across shared faces, side from the flux of (x, y, z); no assumption that
  the lesion is star-shaped). `Orientation.TetNormal` is the same formula and sign as `Mesh4D.TetNormal`.
- **Slab index.** `SlabStart[k] .. SlabStart[k + 1]` are the tetrahedra of slab k (1,800 each). The Phase 1
  `Slicer4D.Slice` takes a pointer and a count, so one slab is sliced by passing `tetrahedra + 4 * SlabStart[k]`,
  `tetCell + SlabStart[k]` and the count 1,800. `SlabIndex.Touches(k, frame)` says which slabs a cut can meet.
- **Phase slice and the wrap.** A vertex exactly on the cut counts as positive (Phase 1's rule), so a cut at
  τ = Λ·k/10 lies in slab k − 1 and returns ring k. For phase 0 use τ = Λ, not 0 (`TubeMesh.PhaseW`): the cut
  at τ = 0 is empty, because no vertex lies below it.
- **`SliceFrame` and Phase 1's arguments.** `E0, E1, E2` are the first three rows of `modelToSlice`, `N` is its
  fourth row, `translation` is 0, `normal` is +w for `AtW` / `InPlane`. For `DropW` (scanner picture) pass
  `modelToSlice = identity`, `normal = N`, `offset = Offset`, as the Phase 1 test `Slicer_MovingSphere` does.
- **Frame.** No axis is swapped: x, y, z are L, P, S of the display frame (right-handed). The adapter chooses
  the swap to Unity's y-up frame (contract §2 suggests `unity = (x, z, y)`); if it swaps before building the
  `Mesh4D`, call `Orientation.OrientOutward` after the swap and pass `tableAxis = 1` to `ScannerTilt.Frame`.

## Decisions taken here that the owner should confirm

1. **Detent speeds.** The plan fixes 5 detents and the test speed 20 mm/s only. Default: ∞ (snapshot), 80, 40, 20, 10 mm/s
   (`ScannerTilt.DefaultDetentSpeedsMmPerS`).
2. **What "spans 11.9–34.8 mm" is taken over** (P2-A28). The set-up of `documents/research/scripts/R2_slicer_proto.py`:
   20 mm icosphere (320 triangles), z(t) = −15·cos⁴(πt / 4 s) mm, 10 phases per period joined by straight prisms,
   3 periods, v = 20 mm/s; 41 scans, scan i placed to meet the sphere's centre at ts = 4.0 + 0.1·i s (one whole
   period). Apparent length of a scan = extent of its tilted slice along z. The two numbers are the smallest and
   the largest apparent length over the 41 scans. The result does not depend on the time scale c.
3. **Tilted cut of a one-period tube.** A tilted hyperplane crosses several breaths; a case tube stores one.
   `ScannerTilt.ForCopy(frame, j, Λ)` gives the frame that slices copy j; the scanner view is the union over j.
4. **M4 score** = (ITV − V(arc)) / (ITV − V(best arc of equal duty)), in [0, 1]; `m4_gate.itv_cm3` is the chosen
   arc's gated volume and `best_cm3` the best arc's. An arc of length 10 uses `gating_cm3[s][9]` (the table entry),
   which the contract allows to be slightly below `itv_cm3`.
5. **M2 outside the grid's box** returns 0; inside, trilinear between voxel centres, clamped at the outermost centres.
6. **Event names.** `m2_place`, `m4_gate`, `m3_pick` are the plan's (§10.2.6). The lane D link contract (§2.6) allows
   only `[a-z][A-Za-z0-9]*` segments in `ev`, so the underscore is not valid there: one of the two has to change.
7. **Edge points on the cut.** When the positive end of an edge lies exactly on the cut the reference slicer returns
   that vertex itself; Phase 1 `Slicer4D` computes `a + 1·(b − a)`, which can differ by a rounding step and leaves
   zero-width sliver triangles at a cut exactly on a ring. Harmless on screen; it changes triangle counts.
8. **Stand-in colour row** (`PhaseColour.Default`): cool at phase 0, warm at 50 %, lightness varied. The real row
   belongs to the palette texture of P1-A06.

## What the Unity adapter still has to do

- P2-A20/A21: build the `Mesh4D` from `TubeMesh`, copy to native containers, run `SliceJob` per touched slab into
  fixed slots (6 vertices per tetrahedron), upload with `SetVertexBufferData` (dirty range from `TubeSliceState`
  or its own), skip the job when the frame is unchanged.
- P2-A22: put the phase row into the palette, feed the `tau` channel, sample with wrap inside the strip.
- P2-A24: import `chest.glb`, call `BlendShapePlayer.Weights` each frame with the time passed in, set weights × 100.
- P2-A27: flags on the lane → `Gating.ArcFromFlags`; draw the phases with `Gating.Contains` opaque; show volume and duty.
- P2-A28: floor dial → `DetentFromDial` → `DetentTheta` → `ScannerTilt.Frame`; slice the copies; show v and the length.
- P2-A29: task UI; convert the picked point to patient mm (`M2FromDisplay`); write the structs as `Event` data.

## Acceptance checks

Run here (`tests/test_p2_a_lung_core.py`, which builds and runs `Atlas.Lung.Core.Tests`; `ATLAS_CASES_DIR` points at
a directory holding `twin/` and `lung101/`, otherwise the twin is generated and the lung101 checks are skipped):

| Check | Result here |
|---|---|
| A.8 tesseract: area 0.8660 at c = 0.5, 6.9282 at c = 2 (4 decimals) | 0.866025, 6.928203 |
| P2-A20: twin, slice at θ = 0, area within 2 % of 4πR² (R = 5 mm; file in display metres, M = 15) | 310.69 mm² = 0.9889 × 314.16 at phase 0; worst of 100 phase positions 1.11 % below |
| §10.2.5: one slab in ten touched for a pure phase slice | 1,800 of 18,000 tetrahedra, both packages, 100 phase positions |
| P2-A28: apparent length spans 11.9–34.8 mm ± 0.5 mm at v = 20 mm/s | 11.915 mm and 34.823 mm (same in display units and with periodic copies) |
| P2-A22: colour at τ = 0 and τ = Λ identical | ΔRGB = 0 (nearest and linear; also on the phase-0 slice of both packages) |
| P2-A27: volume equals table entry within 1 %; duty = length / 10 | exact for all 100 arcs, both packages |
| P2-A29: M2 hit equals the `occ.r8` sample within 0.01 | worst difference to an independent scipy sample at 200 seeded points: 2·10⁻¹⁶, both packages |
| 0 allocations over 1,000 moving-slice frames (reference slicer, desktop) | 0 bytes |

Still needs the Editor or the phone:

- P2-A20: the same area check with the production path (`Mesh4D` + Burst `SliceJob`) in the Editor.
- P2-A21: ≤ 1.0 ms at the 95th percentile on the phone (here: 0.12 ms on a desktop CPU with the plain C# slicer, not comparable).
- P2-A22: the rendered colour through the shader and the palette texture.
- P2-A24: "no visible jump at the wrap (vertex step ≤ 1 mm at display scale)". Measured here on lung101's `chest.glb`
  (18,006 vertices) at 60 fps and a 4 s breath with linear blending: largest step per frame 11.93 mm at display scale
  (0.795 mm at patient scale); on the frames of the wrap 9 → 0 it is 9.08 mm, smaller than elsewhere, so the wrap
  adds no jump of its own. The 1 mm figure is not met at M = 15 by any frame; whether it is meant at patient scale
  is the owner's call.
- P2-A27, P2-A29: the displayed numbers and the emitted events on the phone in the headset.
- P2-A28: the dial, and the test with the tube generator of P1-A12 in the Editor.
