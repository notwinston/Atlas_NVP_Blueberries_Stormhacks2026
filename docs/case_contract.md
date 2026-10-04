# Case contract (`atlas.case/1`)

Work package P2-D01. Normative for every file of a Lung Mission case package (Master Plan §10.2.4).
Writers: the Station pipeline (`station/pipelines/`, `atlas case <step>`). Readers: the app (lane A, B), the Hub, the Console.

Reference implementations, which must produce identical bytes for identical content:

| Language | Location | Covers |
|---|---|---|
| Python | `station/pipelines/atlas_pipelines/contract.py`, `glb.py`, `labels.py` | readers and writers for everything below |
| C# (System only, no Unity references, namespace `Atlas.Cases`) | `station/pipelines/csharp/Atlas.Cases/` | canonical JSON, `tube4d.bin`, `table.json`, `manifest.json`, hash check |

Test: `station/pipelines/tests/test_p2_d01_contract.py` (a 2-tetrahedron fixture in `tests/fixtures/p2_d01/` goes Python → C# → bytes and must come back byte-identical).

Status words used below: **Fixed** means settled by this document. **Owner finalises** means the layout is fixed here as far as stated and the step named there may add detail without changing what is fixed; such additions are reported back into this document.

A case package holds education and research visualisation data. Nothing in it is for clinical use, and no file or label makes a clinical claim.

## 1. Package layout

A package is one directory `cases/<id>/`. On the Station it is `station/pipelines/out/cases/<id>/`.

| Case id | `kind` | Content |
|---|---|---|
| `lung101` | `real` | TCIA 4D-Lung patient 101_HM10395: target lesion tube and shells, chest, model-generated airway, needle sky |
| `twin` | `illustrative` | A 10 mm nodule moving on a Lujan breathing curve: tube and shells only |

| File | `lung101` | `twin` | Written by | Section |
|---|---|---|---|---|
| `manifest.json` | yes | yes | `pack` | 6 |
| `tube4d.bin` | yes | yes | `tube4d` / `twin` | 4 |
| `tube4d_rigid.bin` | yes | yes | `tube4d` / `twin` | 4 |
| `phases.glb` | yes | yes | `tube4d` / `twin` | 8 |
| `itv.glb`, `core.glb`, `rho50.glb` | yes | yes | `shells` / `twin` | 8 |
| `occ.r8` | yes | yes | `shells` / `twin` | 7 |
| `table.json` | yes | yes | `shells` / `twin` | 5 |
| `chest.glb` | yes | no | `chest` | 8 |
| `sky/risk_p0.r8` … `sky/risk_p9.r8`, `sky/robust.r8`, `sky/sky.json` | yes | no | `sky` | 9 |
| `airway/tree.json`, `airway/greedy.json` | yes | no | `airway` | 10 |
| `CREDITS.txt` | yes | yes | `pack` | 11 |

Rules (Fixed):

- File names are exactly as above, lower case except `CREDITS.txt`. Relative paths use `/`.
- A package holds no other files. Files or directories whose name starts with `.` are not part of a package and are ignored by the manifest.
- A package never holds DICOM, and DICOM is never committed to the repository.
- Scratch that later steps need but the app does not (the case frame `frame.json`, cache stamps) lives in `out/work/<id>/`, outside the package.
- The wording in any text is "target lesion" for the real case and "nodule" only for the illustrative twin.

## 2. Coordinate frames and units

Three frames are used. No file mixes them.

**Patient frame (millimetres).** DICOM patient coordinates, LPS: +x to the patient's left, +y posterior, +z superior, right-handed. Used by `table.json` (`*_mm` keys), the `occ.r8` bounding box and `sky.json`. For the twin it is the model's own millimetre frame with the same axis meaning.

**Display frame (metres).** Used by `tube4d.bin` (x, y, z) and every `.glb`:

```
p_display [m] = M · (p_patient [mm] − origin_mm) / 1000
```

- `M` = 15 is the display magnification. It is **already applied** in the stored numbers: a 30 mm lesion is stored 0.45 m wide. Readers must not apply it again.
- `origin_mm` is one point per case, in the patient frame, chosen by the `tube4d` step (or `twin`): the mean of the ten per-phase lesion centroids. It is recorded in `table.json` and `manifest.json`, and in `out/work/<id>/frame.json` for the later steps. The lesion therefore sits near the display origin, which keeps float32 precision and lets the app place the case with one transform.
- Axes are **not** permuted or flipped: display x, y, z are L, P, S. The frame is right-handed.
- To get back: `p_patient [mm] = p_display · 1000 / M + origin_mm`.

Why this convention: R2 D.2 defines the 4D vertices as `P[k, i] = (M · V[k % 10][i], τ = Λ · k / 10)` with M the display magnification and Λ the room length of one breath, and the plan's header carries M and Λ, so the stored positions are display positions in metres. The only addition is the subtraction of `origin_mm`, without which patient coordinates of a few hundred millimetres times 15 would put the tube metres away from its own origin.

**Lane coordinate τ (metres).** The fourth coordinate of `tube4d.bin`. Phase k of 10 sits at `τ = Λ · k / 10`, Λ = 1.5 m, so phases are 0.15 m apart and breathing phase `φ ∈ [0, 1)` is `τ / Λ`. τ is a length along the lane, not a time.

**Mapping to Unity (informative, lane A decides).** Unity is left-handed with +y up. Reading `tube4d.bin` directly, `unity = (x, z, y)` of the display frame puts superior up and turns the right-handed frame into a left-handed one without mirroring the anatomy; triangle winding that is counter-clockwise from outside in the display frame becomes clockwise in Unity, which is Unity's front face. A glTF importer applies its own handedness conversion to `.glb` files, so the app must bring both to the same frame. This has not been verified in the Editor (deferred to Phase 1).

`frame.json` (work file, schema `atlas.frame/1`, canonical JSON): `{"Lambda":1.5,"M":15.0,"id":"<id>","origin_mm":[x,y,z],"schema":"atlas.frame/1"}`.

## 3. Canonical JSON

Every JSON file in a package is written in this form, so that Python and C# produce the same bytes and file hashes are stable. Fixed.

1. Encoding UTF-8, no byte-order mark.
2. No whitespace anywhere, except exactly one line feed (0x0A) after the top-level value. No carriage returns.
3. Objects: members sorted by key in ascending code-point order. Keys are non-empty strings of printable ASCII (0x20 to 0x7E) and unique within an object.
4. Arrays keep their order.
5. Strings: `"` and `\` are written `\"` and `\\`; U+0008, U+000C, U+000A, U+000D, U+0009 are written `\b`, `\f`, `\n`, `\r`, `\t`; any other character below U+0020 is written `\u00xx` with lower-case hex digits. Every other character, including `/`, U+007F and all non-ASCII characters, is written as itself in UTF-8. Lone surrogates are invalid.
6. Literals `true`, `false`, `null`.
7. **Integers** (counts, sizes, dimensions): decimal digits with an optional leading `-`, no leading zeros, no decimal point, no exponent. Range: signed 64-bit.
8. **Floats** (every physical quantity, also when the value is whole): fixed-point with a resolution of 10⁻⁶.
   - Compute `q = round_half_even(v · 10⁶)` in IEEE-754 double arithmetic: one double multiplication, then rounding of that double to the nearest integer, ties to even.
   - `|q|` must be below 2⁵³; NaN and infinities are invalid.
   - Write: `-` if `q < 0`; then `|q| div 10⁶` in decimal; then `.`; then `|q| mod 10⁶` as six digits with trailing zeros removed, leaving at least one digit.
   - Examples: 15 → `15.0`, 1.5 → `1.5`, 0 and −0 → `0.0`, −0.25 → `-0.25`, 47.4 → `47.4`, 1.5·10⁻⁶ → `0.000002`, 4·10⁻⁷ → `0.0`.
   - A number token is a float if and only if it contains `.`; canonical files never use exponents.
9. Which fields are integers and which are floats is part of each schema below. A reader accepts an integer token where a float is expected; a writer always writes the canonical form.

Readers accept any valid JSON (other whitespace, key order, exponents) but reject duplicate keys, NaN, Infinity and a byte-order mark. Re-writing a parsed file always gives the canonical bytes.

Python: `contract.dumps_canonical`, `contract.write_json`, `contract.loads_json`. C#: `CanonicalJson.Write`, `CanonicalJson.Parse`.

Schemas marked "strict" reject unknown keys. Each strict schema has one optional key `extra` (an object with any canonical JSON inside) for additions; readers preserve it and ignore its content.

## 4. `tube4d.bin` and `tube4d_rigid.bin`

A tetrahedral mesh in (x, y, z, τ): the worldtube of the lesion over one breath. Fixed.

`tube4d.bin` is the tube as contoured. `tube4d_rigid.bin` has the same format and the same template, with the phase-0 shape translated along the centroid path.

### 4.1 Byte layout

Little-endian throughout. No padding or alignment bytes anywhere: fields are packed at the offsets given.

| Offset | Size | Type | Field | Value |
|---|---|---|---|---|
| 0 | 4 | 4 bytes | magic | `41 54 4C 34` (ASCII `ATL4`) |
| 4 | 2 | u16 | version | 1 |
| 6 | 4 | u32 | nVerts | n |
| 10 | 4 | u32 | nTets | m |
| 14 | 2 | u16 | layers | 10 (range 1 to 256) |
| 16 | 4 | f32 | M | 15.0 |
| 20 | 4 | f32 | Λ | 1.5 (metres) |
| 24 | 16·n | f32 × 4 per vertex | P | x, y, z, τ in metres (display frame, §2) |
| 24 + 16·n | 16·m | i32 × 4 per tet | T | vertex indices, 0 ≤ index < n |
| 24 + 16·n + 16·m | m | u8 per tet | slab | phase layer k of the tet's prism, 0 ≤ k < layers |

Header size: 24 bytes. File size: exactly `24 + 16·n + 17·m` bytes; a reader rejects any other length. With a closed 600-triangle template of sphere topology (302 vertices, so n = 11 · 302 = 3,322, and m = 18,000) a file is 359,176 bytes.

Floats are IEEE-754 binary32. NaN and infinities are invalid. `M` and `Λ` equal the manifest's `M` and `Lambda`.

### 4.2 Reader checks

magic; version = 1; length; 1 ≤ layers ≤ 256; every index in range; every slab id < layers; all floats finite; M > 0 and Λ > 0. Both reference readers apply exactly these.

### 4.3 Tube layout (what the `tube4d` and `twin` steps write)

The format itself is a general tetrahedral 4D mesh (the 2-tetrahedron test fixture is one). A lesion tube additionally obeys:

- The template surface has nV vertices and nF = 600 triangles. There are `layers + 1` = 11 vertex rings. **Vertex i of ring k has index `k · nV + i`**, so n = 11 · nV.
- Ring k holds the template projected onto phase `k mod 10`, with `τ = float32(Λ · k / layers)` (the product and quotient computed in double). **Ring 10 repeats ring 0** (same x, y, z bit for bit, τ = Λ), so the tube closes; rings 0 and 10 are separate vertices.
- Each template triangle with its vertex indices sorted a < b < c, carried from ring k to ring k + 1, is a prism split into three tets, **in this vertex order** (Master Plan A.8):

  ```
  (a_k, b_k, c_k, c_k+1)    (a_k, b_k, b_k+1, c_k+1)    (a_k, a_k+1, b_k+1, c_k+1)        with v_k = k · nV + v
  ```

  Sorting by index makes neighbouring prisms agree on the diagonal of their shared quad: every interior face is shared by exactly 2 tets.
- **Tets are stored slab by slab**: tet index `3 · (k · nF + f) + j` for layer k, template triangle f, j = 0, 1, 2 in the order above. So m = 3 · nF · layers = 18,000, the tets of slab k are the contiguous range `[3·nF·k, 3·nF·(k + 1))`, and a slice at constant τ touches one slab in ten.
- `slab[t]` = k: the phase layer of the prism, i.e. the tet spans `τ ∈ [Λ·k/10, Λ·(k + 1)/10]`.
- Tet orientation (sign of the 4D volume) is not specified; a slicer must not depend on it.

Python: `contract.Tube4D`, `read_tube4d`, `write_tube4d`, `prisms_to_tets`, `tube_from_phases`, `check_tube_layout`. C#: `Tube4D.Read`, `Tube4D.Write`.

## 5. `table.json`

Schema `atlas.table/1`, canonical JSON, strict. Volumes in cm³, lengths in patient-frame millimetres (§2).

| Key | Type | Meaning |
|---|---|---|
| `schema` | string | `"atlas.table/1"` |
| `id` | string | case id |
| `phases` | integer | 10 |
| `substeps` | integer | interpolated sub-steps between consecutive phases used for `itv_cm3`, the shells and `occ.r8` (plan: 4; 0 = phases only) |
| `gtv_cm3` | 10 floats | lesion volume at phase k, k = 0 … 9 |
| `itv_cm3` | float | volume of the union over all phases (and sub-steps, wrapping 9 → 0) |
| `core_cm3` | float | volume of the intersection over the 10 phases; `0.0` when empty |
| `gating_cm3` | 10 × 10 floats | `gating_cm3[s][l]`: volume of the union of the `l + 1` consecutive phases s, s + 1, …, s + l (indices mod 10). First index = start bin s (0 … 9); second index = length index l (0 … 9), which means l + 1 phases |
| `centroid_mm` | 10 × 3 floats | lesion centroid at phase k: `[x, y, z]` |
| `centroid_range_mm` | 3 floats | max − min of `centroid_mm` per axis: `[LR, AP, SI]` |
| `argmax_rho_mm` | 3 floats | the point of highest occupancy ρ (the sky's target) |
| `max_rho` | float | the highest occupancy, in [0, 1] |
| `origin_mm` | 3 floats | origin of the display frame (§2) |
| `occ` | object | `{"dims":[64,64,64],"bbox_min_mm":[x,y,z],"bbox_max_mm":[x,y,z]}`: the grid of `occ.r8` (§7); `dims` are integers |
| `extra` | object, optional | additions by the owning step |

Length index: `gating_cm3[s][0]` is the single phase s; `gating_cm3[s][9]` is all ten phases for every s. The 30–70 % gate is `gating_cm3[3][4]` (phases 3, 4, 5, 6, 7).

Owner finalises (`shells`, P2-D05): whether `gating_cm3` unions include the interpolated sub-steps between the gated phases (record the choice in `extra`), how ties for `argmax_rho_mm` are broken, and any ranges added under `extra`.

Finalised by `atlas_pipelines/tubelib.py` (`build_shells`, used by `twin` and available to `shells`; P2-D02). Each choice is recorded in `extra`:

- Volumes are counts of grid voxels with signed distance < 0, times the voxel volume. `extra.voxel_mm` is the voxel size (0.25 for the twin).
- A sub-step between phases k and k + 1 at fraction a is the blend `(1 − a) · sdf_k(x − a·d) + a · sdf_k+1(x + (1 − a)·d)`, `d` = the step between the two centroids: each phase shape is carried along the straight segment between the centroids, then the two are blended. (A blend without the shift can never add to a union.)
- `gating_cm3[s][l]` includes the sub-steps **between** the gated phases s … s + l, and none before s or after s + l (`extra.gating_includes_substeps` = true). So `gating_cm3[s][0]` = `gtv_cm3[s]`, and `gating_cm3[s][9]` lacks only the sub-steps of the one interval (s + 9) → s, which makes it at most `itv_cm3`.
- `core_cm3` and ρ use the 10 phases only. `max_rho` is therefore a multiple of 0.1.
- `argmax_rho_mm`: of all voxels with the highest ρ, the one nearest to the centroid of that set (on a tie: lowest ix, then iy, then iz).
- The twin adds `extra.twin`: the model parameters (`diameter_mm`, `b_mm`, `n`, `period_s`, `b_ap_mm`, `delta_deg`, `tube_voxel_mm`).

Confirmed by `shells` for the real case (P2-D05), which uses the choices above unchanged and adds, under `extra`:

- `voxel_mm` is 1.0: the volumes of `lung101` are counts of 1 mm voxels of the smoothed phase fields (CT-grid mask → signed distance → 1 mm isotropic → Gaussian σ = 1 mm), the fields the tube is built from. `extra.fields` records the recipe (`source`, `ct_voxel_mm`, `recipe`, `sigma_mm`, `roi_margin_mm`).
- `centroid_source`: `centroid_mm` is the mean of the mask voxel centres on the CT grid.
- `itv_phases_only_cm3`: the union of the 10 phase fields without the sub-steps.
- `raw_masks`: `{method, union_cm3, intersection_cm3, gtv_cm3[10]}`, the plain union and intersection of the 10 raw CT-grid masks, with no smoothing and no sub-steps. These are for comparison; `itv_cm3` and `core_cm3` are the field values. `method_note` says why the two differ.

Python: `contract.Table`, `read_table`, `write_table`. C#: `CaseTable.Read`, `CaseTable.Write`.

## 6. `manifest.json`

Schema `atlas.case/1`, canonical JSON, strict. Written last, by `pack`.

| Key | Type | Meaning |
|---|---|---|
| `schema` | string | `"atlas.case/1"` |
| `id` | string | case id (`lung101`, `twin`) |
| `kind` | string | `"real"` or `"illustrative"` |
| `label` | string | the label of the case as a whole (strings below) |
| `footer` | string | `"Education and research visualisation. Not for clinical use."` |
| `labels` | object | **per-file label map**: exactly one entry per entry of `files`, key = that file's `path`, value = the one on-screen label of that asset |
| `licence` | string | licence of the case data |
| `credit` | string | required credit line |
| `citations` | array of strings | full citations, in order |
| `M` | float | 15.0 |
| `Lambda` | float | 1.5 (metres) |
| `origin_mm` | 3 floats | origin of the display frame (§2); equals `table.json` `origin_mm` |
| `files` | array of objects | one per package file except `manifest.json` itself, sorted by `path` ascending; each `{"path": string, "sha256": string, "size": integer}` |
| `extra` | object, optional | additions by the owning step |

- `path` is relative to the package directory, with `/` separators, printable ASCII, no leading `/`, no `.` or `..` segment.
- `sha256` is the SHA-256 of the file's bytes as 64 lower-case hex digits. `size` is the byte count.
- **Hash check** (P2-D09): every listed file exists with that size and hash, and the package holds no unlisted file (dot-files aside). Python: `contract.verify_manifest(case_dir)` returns the list of problems. C#: `CaseManifest.Verify(caseDir)`.

Label strings (Fixed; constants in `atlas_pipelines/labels.py`):

| Asset | Label |
|---|---|
| Real case and every file derived from its contours | `Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS.` |
| `tube4d_rigid.bin` of the real case | `Simplified: rigid shape` |
| Files under `airway/` | `Model-generated airway` |
| Every file of the twin | `Illustrative model.` |

`licence` is `CC BY 3.0` for the real case. The real case's `credit` names Hugo et al. 2016, *Data from 4D Lung Imaging of NSCLC Patients*, The Cancer Imaging Archive, doi 10.7937/K9/TCIA.2016.ELN8YGLE, and `citations` adds the TCIA citation (Clark et al. 2013). The exact texts are the constants `CREDIT_REAL`, `CITATIONS_REAL`, `CREDIT_TWIN`, `CITATIONS_TWIN`, `LICENCE_TWIN`.

Finalised by `atlas case pack` (P2-D09; `atlas_pipelines/steps/pack.py`):

- The twin's `tube4d_rigid.bin` keeps `Illustrative model.` (every twin asset is marked illustrative; `Simplified: rigid shape` is the label of the real case's `tube4d_rigid.bin`). One line in `labels.label_for` changes this if the owner decides otherwise.
- `sky/robust.r8` and `sky/sky.json`, which are the 5 mm clearance threshold, carry `Teaching threshold` (plan 10.2.1, last row). The ten `sky/risk_p<k>.r8` maps carry the real-data label. This is set in `pack.file_label`.
- `extra` is always written by `pack`: `{"complete": bool, "missing": [path, ...], "unexpected": [path, ...]}`. `missing` lists the expected files (section 1) that were absent when `pack` ran, sorted; `unexpected` lists packed files that are not in section 1. `complete` is `true` exactly when `missing` is empty. A reader that needs a full package must check `extra.complete`; the hash check alone does not.
- `pack` packs what is present, prints every missing file on stdout, and exits 1 on an incomplete package only with `--strict`. A DICOM file in the package directory stops `pack`.
- `atlas case verify` is the hash check: exit 1 on any missing, resized, altered or unlisted file.

Python: `contract.Manifest`, `build_manifest`, `read_manifest`, `write_manifest`. C#: `CaseManifest.Read`, `CaseManifest.Write`.

## 7. `occ.r8`

The occupancy ρ (the share of the breath during which a point is inside the lesion) on a 64³ grid. Raw bytes, no header: 262,144 bytes.

- Fixed: one u8 per voxel, `value = round(255 · ρ)`, ρ ∈ [0, 1].
- Fixed: axis order **x fastest, then y, then z**: `offset = ix + 64 · (iy + 64 · iz)`, with ix along +x (L), iy along +y (P), iz along +z (S). This is the order of a Unity `Texture3D` pixel array.
- Fixed: the grid's box is recorded in `table.json` under `occ` (`bbox_min_mm`, `bbox_max_mm`, patient frame). Voxel (ix, iy, iz) is centred at `bbox_min + (i + 0.5) · (bbox_max − bbox_min) / 64` per axis.
- Owner finalises (`shells`): the margin of the box around the union, and whether ρ averages the 10 phases or the phases and sub-steps (record in `table.json` `extra`).

- Finalised by `tubelib.build_shells` (P2-D02): the box is the bounding box of the union's voxels plus 2 mm on every side (`extra.occ_margin_mm`); it need not be a cube. ρ is the share of the **10 phases** (not the sub-steps) in which the voxel centre is inside, evaluated at the 64³ voxel centres from the phase fields, so the bytes take the 11 values `round(255 · k / 10)`.
## 8. `.glb` files

GLB 2.0, written by `atlas_pipelines.glb.write_glb` (deterministic bytes). Committed test fixtures must not have the `.glb` extension, because `.gitattributes` sends `*.glb` to Git LFS.

Fixed for every `.glb`:

- Positions are in the **display frame** (§2): metres, M applied, origin at `origin_mm`, axes L, P, S unchanged. This departs from glTF's "+Y up" reading of the axes on purpose, so that one transform in the app places the tube and all meshes together. No node has a transform.
- Triangle lists, uint32 indices, float32 `POSITION`, optional float32 `NORMAL`. Winding is counter-clockwise seen from outside, in the right-handed display frame.
- One node and one mesh with one primitive per named part. No materials, textures or animations.
- Morph targets (blend shapes) hold **displacements** from the base positions. Their names are in `mesh.extras.targetNames`, in target order; default weights are all 0.
- An empty surface (the twin's empty core) is a valid GLB with a scene and no meshes: `write_glb(path, [])`.

| File | Meshes | Morph targets | Triangle budget |
|---|---|---|---|
| `phases.glb` | one, named `phases` | 10: `phase_0` … `phase_9` | 600 |
| `itv.glb`, `core.glb`, `rho50.glb` | one, named `itv`, `core`, `rho50` | none | ≤ 1,500 each |
| `chest.glb` | four, in this order: `lung_r`, `lung_l`, `heart`, `trachea` | 10 per mesh: `phase_0` … `phase_9` | ≤ 40,000 in total (15,000 + 15,000 + 6,000 + 4,000) |

`phases.glb` (Fixed): the base mesh is the tube's template (the mean shape at the mean centroid). Its vertex i and its triangles are the template of §4.3, so base + `phase_k` equals the x, y, z of tube vertex `k · nV + i`. Setting weight `phase_k` = 1 and the others 0 shows phase k.

Owner finalises: `tube4d` whether `phases.glb` carries normals; `shells` the iso-level and decimation of each shell (`rho50` is the ρ = 0.5 surface). `chest.glb` is final (next paragraph).

`chest.glb` (Final, set by `chest`, P2-D06; real case only):

- Four meshes, each one node and one primitive, in the order `lung_r`, `lung_l`, `heart`, `trachea` (right and left are the patient's). No material in the file: the app assigns one material to all four.
- Each mesh is one closed, outward-wound surface with the same vertices and triangles at every phase. The base is the organ's **mean template** (the zero surface of the mean of the 10 phase distance fields), not a phase. Each mesh has 10 morph targets `phase_0` … `phase_9`; base + `phase_k` is the organ at phase k (phase k is the 10k % bin). Show phase k with weight `phase_k` = 1 and the others 0; blend between k and k + 1 with weights 1 − t and t; after `phase_9` comes `phase_0` (there is no `phase_10`).
- The base carries `NORMAL` (unit vertex normals of the template). The morph targets carry positions only, so the player recomputes normals or accepts the template's.
- Same display frame as the tube (§2): the step reads `frame.json` and fails if it is missing, so the target lesion sits inside `lung_r` or `lung_l` without any extra transform. Patient millimetres = `from_display(base + phase_k)`.
- Volume of an organ at phase k in litres at patient scale = closed-mesh volume of base + `phase_k` in m³, divided by M³, times 1000.
- Built from the rasterised contour masks (largest connected piece per phase); correspondence across phases comes from registering each phase's distance field to the mean field. The method and its measurements are in `atlas_pipelines/chest.py` and in the work file `out/work/lung101/chest_report.json` (not shipped).

Finalised by `atlas_pipelines/tubelib.py` (P2-D02; `write_tube_files`, `write_shell_files`):

- `phases.glb` carries `NORMAL` for the base mesh (area-weighted vertex normals of the template) and no normals for the morph targets.
- `itv.glb` is the zero surface of the union field (minimum over phases and sub-steps); `core.glb` of the intersection field (maximum over the 10 phases); `rho50.glb` is the boundary of {ρ ≥ 0.5}, taken as the zero surface of the 5th-smallest of the 10 phase fields. Each is marching cubes on the shell grid followed by quadric decimation to at most 1,500 triangles, with base normals. Shells may have several connected parts and are not guaranteed to be watertight after decimation.
- An empty shell is a valid GLB with a scene and **no meshes** (zero triangles), and its volume in `table.json` is `0.0`. The twin's `core.glb` is always empty.
- The template of `phases.glb` and the tubes is a closed, consistently oriented manifold of exactly 600 triangles.

## 9. `sky/`

The needle sky of the real case: for each direction from the target point, the clearance to the contoured organs.

- Fixed: each `.r8` file is raw u8, **512 × 256 = 131,072 bytes**, no header, equirectangular, stored row by row: `offset = col + 512 · row`.
- Fixed: files `sky/risk_p0.r8` … `sky/risk_p9.r8` (one per phase), `sky/robust.r8`, `sky/sky.json`.
- Fixed: `robust.r8` holds 255 where the direction is robust and 0 elsewhere, and equals the pixel-wise AND over the 10 phases of "clearance ≥ threshold".
- Fixed: the value encoding of the risk maps (how a byte maps to millimetres of clearance) is recorded in `sky.json`, not implied.

The 5 mm threshold is a teaching value (label "Teaching threshold"). The map is a geometric clearance map for education; it covers only the four contoured structures below.

Finalised by `atlas case sky` (P2-D08; code in `atlas_pipelines/sky.py`). The proposal for the projection was adopted unchanged.

### 9.1 Directions and pixels

- A direction is a unit vector in the patient frame (LPS, §2) pointing **from the target outwards**. `θ` is the polar angle from +z, `φ` the azimuth from +x towards +y: direction `(sin θ cos φ, sin θ sin φ, cos θ)`.
- Image pixel `(col, row)`, row 0 at +z (superior): `θ = π · (row + 0.5) / 256`, `φ = 2π · (col + 0.5) / 512`. The pixel containing a direction is `row = floor(256 · θ / π)`, `col = floor(512 · φ / 2π) mod 512`.
- Rays are cast on HEALPix, nside 64, **ring** ordering, 49,152 pixel centres (`θ` is the HEALPix colatitude, `φ` its longitude). Each image pixel holds the value of the HEALPix pixel that contains the direction of its centre: nearest pixel, no blending. So the image is piecewise constant, and the AND of the ten baked maps equals the baked AND exactly.
- The ring formula `ang2pix_ring(nside, θ, φ)` is written out in the docstring of `atlas_pipelines/sky.py` for the phone to re-implement; it matches astropy-healpix.

### 9.2 Byte encoding

- `risk_p<k>.r8`: `value = min(255, floor(clearance_mm / 0.1))`, so `clearance_mm = 0.1 · value`. The byte rounds **down**: the decoded value is a lower bound within 0.1 mm. `255` means 25.5 mm or more (no pixel of `lung101` saturates: the largest value is 123).
- The threshold of 5 mm is the byte value 50, and "value ≥ 50" is identical to "clearance ≥ 5 mm". `robust.r8` is 255 where all ten maps have value ≥ 50, else 0.

### 9.3 Method

- Distance field, per phase: the union of the masks of heart, oesophagus, spinal cord and trachea on the CT grid; Euclidean distance transform with the CT voxel size, giving the distance from a voxel centre to the nearest organ voxel centre (0 inside); read between voxel centres by trilinear interpolation. The distance is to voxel centres, not to the contour surface, so it can exceed the distance to the contour by up to half a voxel (0.49 mm in-plane, 1.5 mm between slices).
- Ray: starts at the target point (`argmax_rho_mm` of `table.json`), sampled every 1 mm, ends where it leaves the box of CT voxel centres or at 300 mm, whichever is first; the end point is also sampled.
- Clearance of a direction = minimum of the distance field over the samples of its ray. The target is a sample of every ray, so no direction has more clearance than the target point itself (11.2 to 12.3 mm over the phases for `lung101`).
- If `table.json` is absent the step computes the target from the raw target-lesion masks (highest share of phases inside; the voxel nearest to the centroid of all such voxels) and records `target_fallback_used: true`.

### 9.4 `sky.json` (canonical JSON, schema `atlas.sky/1`)

| Key | Type | Meaning |
|---|---|---|
| `schema`, `id`, `label`, `note` | strings | `atlas.sky/1`, the case id, the real-data label, a one-paragraph description |
| `width`, `height`, `phases` | ints | 512, 256, 10 |
| `nside`, `npix`, `ordering` | int, int, string | 64, 49152, `ring` |
| `files` | object | `risk`: the ten file names in phase order; `robust`: `robust.r8` |
| `frame` | string | the direction frame (patient LPS, from the target outwards) |
| `projection` | object | `type` `equirectangular`, `offset`, `theta`, `phi`, `direction`, `sampling` (the formulas of 9.1 as text) |
| `healpix` | object | `nside`, `ordering`, `theta`, `phi` |
| `encoding` | object | `type` `u8`, `mm_per_count` 0.1, `clearance_max_mm` 25.5, `rounding` `floor`, `formula`, `saturation`, `robust` |
| `threshold_mm`, `threshold_value`, `threshold_label` | float, int, string | 5.0, 50, `Teaching threshold` |
| `robust_rule` | string | the AND rule |
| `target_mm` | 3 floats | the target point, patient mm |
| `target_source`, `target_fallback_used` | string, bool | where the target came from |
| `organs` | 4 strings | `heart`, `esophagus`, `cord`, `trachea` (mask keys) |
| `distance_field` | object | `method`, `grid_origin_mm`, `grid_spacing_mm`, `grid_shape` |
| `ray` | object | `start`, `step_mm` 1.0, `max_length_mm` 300.0, `termination`, `box_min_mm`, `box_max_mm`, `clearance` |
| `stats` | object | per phase: `target_clearance_mm`, `max_clearance_mm`, `clear_share` (share of the sphere at or above the threshold); `robust_share`, `robust_pixels`, `share_note` |
| `checks` | 20 objects | test directions for the runtime (P2-B10): `col`, `row`, `dir`, `healpix`, `value[10]`, `clearance_mm[10]` (decoded bytes), `min_clearance_mm`, `robust` |

## 10. `airway/`

A model-generated airway tree (`atlas case airway`, P2-D07; code in `atlas_pipelines/airway.py`). It does not belong to patient 101 and is never presented as such. Label of both files: `Model-generated airway`. Both files are canonical JSON (§3). This section is final except for the open issue in 10.5.

### 10.1 Addresses and nodes

- A node is one branch. Its **address** is a string over the two letters `L` and `R`; the trachea is the empty string `""`; `gen` = length of the address. The children of node `A` are `A + "L"` and `A + "R"`.
- A **fork** is named by the address of the branch that divides: the fork `A` is the choice between `A + "L"` and `A + "R"`.
- **Frame of `pos_mm` and `dir`:** the model's own frame in millimetres, not registered to any patient and not the display frame of §2 (no `origin_mm`, no M). Axes are named as in LPS: +x left, +y posterior, +z superior. The trachea starts at (0, 0, 0) and runs along (0, 0, −1).
- **`pos_mm` is the distal end of the branch**, the point where it forks into its two children. A branch starts at its parent's `pos_mm` (the trachea at the origin), so `pos_mm(A + c) = pos_mm(A) + len_mm(A + c) · dir(A + c)`. `dir` is the unit vector from the start to the end.
- `radius_mm` is the lumen radius (diameter = 2 · `radius_mm`), `len_mm` the branch length.

### 10.2 Rule `atlas.airway/1`: every node from `(seed, address)`

Sixteen generations are 131,071 branches, about 20 MB of JSON, so the file holds generations 0 to `materialised_gen` (10: 2,047 nodes, the rendering depth of R2 C.4) and every deeper node is derived. A node depends on nothing but the seed and its own address, so the lazy window of lane B can compute any node by walking its address from the trachea (at most 23 steps), without the file and without the rest of the tree. The Python resolver is `airway.resolve(seed, address)` (or `airway.Resolver(seed).node(address)`); addresses up to length 23 are accepted, and 16 is the tested depth.

```
trachea ""    radius = 9, len = 120, start = (0,0,0), dir = (0,0,-1), normal = (1,0,0), pos = (0,0,-120)

child A + c   h      = SHA-256( ASCII bytes of  "atlas.airway/1|" + decimal(seed) + "|" + (A + c) )
              u1     = (h[0..8)  as big-endian u64  >> 11) / 2^53          0 <= u1 < 1
              u2     = (h[8..16) as big-endian u64  >> 11) / 2^53          0 <= u2 < 1
              radius = radius(A) * 2^(-1/3) * (0.9 + 0.2 * u1)             diameter ratio 0.794, jitter +/- 10 %
              len    = 2 * radius * (2 + 2 * u2)                           length / diameter = 3 +/- 1
              normal = normalise( dir(A) x normal(A) )                     branching plane turned 90 degrees
              side   = normal x dir(A)
              s      = +1 for c = "L", -1 for c = "R"
              dir    = normalise( dir(A) * cos(37 deg) + s * side * sin(37 deg) )
              pos    = pos(A) + len * dir
```

All arithmetic is IEEE double. `normal` is carried while walking and is not stored in the file. At the first fork `L` goes to +x (left). The file stores values quantised to 1e-6 (§3); a re-implementation agrees with the file to 1e-6, and derived nodes must be computed from the rule in double precision, not from the quantised file values.

### 10.3 `airway/tree.json`, schema `atlas.tree/2`

| Key | Type | Meaning |
|---|---|---|
| `schema` | string | `atlas.tree/2` |
| `label` | string | `Model-generated airway` |
| `rule` | string | `atlas.airway/1` |
| `seed` | integer | seed of the tree (default 101) |
| `frame` | string | description of the frame (10.1) |
| `generator` | object | `trachea_diameter_mm` 18.0, `trachea_length_mm` 120.0, `diameter_ratio` 0.793701, `diameter_jitter` 0.1, `length_over_diameter` 3.0, `length_over_diameter_spread` 1.0, `branch_angle_deg` 37.0, `plane_rotation_deg` 90.0 |
| `addressable_gen` | integer | 16 |
| `max_address_len` | integer | 23, the longest address the resolver accepts |
| `materialised_gen` | integer | deepest generation present in `nodes` (10) |
| `nodes` | array | generations 0..`materialised_gen`, breadth-first, `L` before `R`: the node with address `a` is at index `(2^gen − 1) + b`, where `b` is `a` read as a binary number with `L` = 0 and `R` = 1 |
| `target` | object | `{"address": string}` |
| `compass_fork` | object | `{"address": string or null}`: the one Two Compasses fork; `null` when the route has none (10.5) |
| `routes` | object | `four_forks`: `{"forks": the first 4 proper prefixes of the target address, "end": the first 4 letters of the target address, "compass_wrong_forks": the forks among those 4 at which the Euclidean compass is wrong}`; `one_fork`: `{"forks": [target address without its last letter], "end": target address}` |
| `target_route_nodes` | array, optional | present only when the target is deeper than `materialised_gen`: both children of every fork on the route below `materialised_gen`, same node format |

Each node: `address` (string), `gen` (integer), `len_mm`, `radius_mm`, `pos_mm` (3 floats), `dir` (3 floats, unit vector), `source` (`"model"` for every node). There are no `parent_iso` and no `R_m` keys.

**Beacon and Euclidean compass.** At fork `A` on the way to target `T` (`A` a proper prefix of `T`): the beacon's next step is the letter `T[len(A)]` (exact prefix comparison). The Euclidean compass takes `t = pos_mm(T) − pos_mm(A)` and picks the child `c` with the larger `dot(dir(A + c), t)`. The fork is a **Two Compasses fork** when the compass picks the child that the beacon does not; an exact tie is not one.

**Target choice.** The target is the first address of generation `target_gen` (default 4, so that the four forks from the trachea end at the target), in `nodes` order, with exactly one Two Compasses fork among the first four forks of its route.

### 10.4 `airway/greedy.json`, schema `atlas.greedy/1`

The offline greedy study. The measured share `wrong_share` is the only number quoted.

| Key | Type | Meaning |
|---|---|---|
| `schema`, `label` | string | `atlas.greedy/1`, `Model-generated airway` |
| `tree_rule`, `tree_seed` | string, integer | the tree studied |
| `seed` | integer | seed of the targets (default: the tree seed) |
| `method` | object of strings | `rule` (`atlas.greedy/1`), `targets`, `forks`, `compass`, `share`: the method in words |
| `n_targets` | integer | 1000 |
| `n_distinct_targets` | integer | distinct addresses among them (repeats are kept in the counts) |
| `target_gen` | integer | 16: every target is a generation-16 branch |
| `forks`, `wrong_forks` | integer | forks examined (`n_targets` · `target_gen`) and forks at which the Euclidean compass is wrong |
| `wrong_share` | float | `wrong_forks / forks` |
| `routes_with_wrong_fork` | integer | targets whose route has at least one wrong fork |
| `by_generation` | array of 16 objects | `{"gen", "forks", "wrong_forks", "wrong_share"}` by the generation of the fork (0 = the trachea's fork) |

Target `i` (0..999) is the first 16 bits, most significant first, of SHA-256(ASCII `"atlas.greedy/1|" + decimal(seed) + "|" + decimal(i)`), 0 = `L`, 1 = `R`. Every proper prefix of the target address is a fork of its route, the last one before the target included (there the compass cannot be wrong, because the line to the target is the child's own direction).

### 10.5 Open issue for the owner: the Two Compasses fork on a four-fork route

With this generator no target at the end of a four-fork route has a Two Compasses fork, for any seed. For a generation-4 target the difference of the two compass scores is a sum of branch lengths with fixed direction coefficients; over every corner of the allowed ranges (diameter ratio 0.714 to 0.874, length/diameter 2 to 4, all 16 targets, all 4 forks) the right child wins by at least 0.142 of the distance to the target. The Euclidean compass only goes wrong when the route continues well past the fork: in the greedy study it is wrong at about one fork in ten of generations 0 to 7 on the way to generation-16 targets.

The step therefore writes `compass_fork.address = null` and the target `LLLL` by default, prints a warning (`--strict` turns it into an error), and the acceptance test for "exactly 1 Two Compasses fork on the 4-fork route" fails. The option `--target-gen N` places the target deeper than the end of the four walked forks (the first qualifying generation for seed 101 is 7: target `LLLRRLL`, fork `""`); it is not the default because the plan ends the four-fork route at the lesion chamber. The owner decides between that reading and a change of generator.

## 11. `CREDITS.txt`

Plain text, UTF-8, LF line endings, no byte-order mark, ending with one LF. It must contain, for the real case: the case label, the licence (CC BY 3.0), the credit (Hugo et al. 2016, *Data from 4D Lung Imaging of NSCLC Patients*, TCIA, doi 10.7937/K9/TCIA.2016.ELN8YGLE), the TCIA citation, the statement that meshes are derived by ATLAS, and the footer. For the twin: the label `Illustrative model.`, the statement that it contains no patient data, and the footer. All strings come from `atlas_pipelines/labels.py`.

Finalised by `atlas case pack` (P2-D09). The text is built by `pack.credits_text` from the strings of `labels.py`, ASCII only, no timestamps, so it is reproducible. Layout, one block per heading, blank line between blocks: title line (`ATLAS Lung Mission case package: <id>`), the case label, the footer, `DATA` (source line "Hugo et al. 2016, Data from 4D Lung Imaging of NSCLC Patients, The Cancer Imaging Archive (TCIA), doi 10.7937/K9/TCIA.2016.ELN8YGLE, CC BY 3.0", the licence with its URL, the patient, the changes made, "No DICOM file is part of this package."), `CITATIONS` (dataset, Medical Physics 2017 paper, TCIA citation "Clark K, et al. ... doi 10.1007/s10278-013-9622-7" and its full form), `CREDIT` (the `credit` of the manifest), `WORDING` (the target-lesion wording; airway, sky and rigid-shape statements only when those files are present), `LABELS` (every file under its label, as in `manifest.json`), the footer again. The twin's file states "This is an illustrative model. It uses no patient data.", cites Lujan 1999 and has no Hugo, TCIA or target-lesion text. AeroPath is not credited because it is not used.

## 12. Versioning

- A change that an existing reader cannot parse raises the version: the u16 in `tube4d.bin`, or the number after the slash in a schema string (`atlas.case/1`, `atlas.table/1`, `atlas.tree/2`).
- Additions that old readers may ignore go under `extra` and do not raise the version.
- Readers reject versions they do not know.
