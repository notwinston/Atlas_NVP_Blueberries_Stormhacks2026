# Prop tools

Offline tools for the phone's prop library: the CC0 furniture props that stand in for scanned objects
(standalone-phone plan, `docs/plans/standalone-phone/INDEX.md`, decisions D1, D4 to D6 and section 6.3).

## Set-up

numpy is not installed system-wide, so the tools run in their own venv, `tools/props/.venv`. It is
git-ignored by the repository's `.venv/` rule.

```sh
python3 -m venv tools/props/.venv
tools/props/.venv/bin/pip install -r tools/props/requirements.txt
```

`requirements.txt` pins the numpy version from the Forge's `station/forge/requirements.lock`. The tools import
the Forge's own `profile.py` and `cleanup.py` (`station/forge/forge/`), so their numbers match the Station's.

## The tools

| Tool | What it does |
|---|---|
| `profile_tool.py [--dir DIR] [--write]` | Computes each prop's `profile`, `triangles` and `sha256` from its file. Without `--write` it reports what differs from the manifest and exits 1 if anything does. With `--write` it rewrites the manifest. |
| `check.py [DIR ...]` | Checks a library against the rules below. It exits 1 on any failure. |
| `reference.py` | Writes the test fixtures (`A/Tests/Objects/Fixtures/props_min/`) and the C# port's reference vectors (`A/Tests/Objects/Fixtures/reference.json`). The output is the same bytes on every run. |
| `build_props.py` | (task T4) Converts the kit sources in Blender into props. |

`DIR` defaults to the phone's library, `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasProps`. `A/` below means
`ATLAS/Assets/Atlas/`.

```sh
tools/props/.venv/bin/python tools/props/profile_tool.py --write          # after changing any prop
tools/props/.venv/bin/python tools/props/check.py                         # the phone's library
tools/props/.venv/bin/python tools/props/check.py ATLAS/Assets/Atlas/Tests/Objects/Fixtures/props_min
tools/props/.venv/bin/python tools/props/reference.py                     # fixtures + reference.json
tools/props/.venv/bin/python -m pytest tools/props -q                     # the tools' own tests
```

## The format

A library folder holds `manifest.json` and one `<id>.glb.bytes` per prop. On the phone the library is loaded
by `PropSet` from `Resources/AtlasProps`, which reads the files as `manifest` and `<id>.glb`.

**Files.** Each prop is a GLB in the kit contract: `tools/kit/kitglb.py`'s `write_glb`, read on the phone by
`GlbMeshReader`. The GLB stores:
- POSITION and NORMAL;
- TEXCOORD_0, the palette cell (`AtlasPalette.Uv`);
- TEXCOORD_1, the line coordinates (unused by props);
- COLOR_0, holding shade, 255, 255, 255. Blue at 255 means "not batched".

The file is right-handed (x negated, v downwards, front faces counter-clockwise). The reader turns it back
into Unity's frame.

**Mesh rules, at reference size.**
- Metres, box-local: origin at the centre of the base, +Y up, front +Z. The lowest point is at y = 0, the
  bounds are centred on x and z, and the bounds equal `size` within 1 mm.
- Closed, unit normals, front faces agree with the normals, no edge over 0.25 m (`kitglb.check_piece(...,
  check_edges=True)`), 300 to 2,500 triangles.
- Colour slots. Each face lies in a single palette cell, and that cell is one of the prop's slot cells.
  Every slot cell (64..127, distinct) marks at least one face. Recolouring rewrites `Uv0`, from each slot's
  default cell to the chosen cell.

**Manifest.**

```json
{"schema": "atlas.props/1",
 "props": [{"id": "chair_kenney_dining_01", "category": "chair", "tags": ["dining"],
            "size": [0.45, 0.90, 0.50], "triangles": 812, "sha256": "…",
            "slots": [{"name": "body", "cell": 97}, {"name": "legs", "cell": 70}],
            "profile": "<base64, 256 bytes>",
            "source": "Kenney Furniture Kit 2.0 / chairDesk.glb", "licence": "CC0-1.0"}]}
```

| Key | Rule |
|---|---|
| `id` | `[a-z0-9_]`, at most 64 characters (`RoomObject.Prop`), unique. The file is `<id>.glb.bytes`. |
| `category` | An `ObjectCategory` JSON name (`couch`, `table`, `bed`, `chair`, …; RoomPlan's `sofa` is `couch` and `television` is `screen`), not `unknown` or `other`. `check.py` reads the names from `ObjectCategory.cs`. |
| `tags` | At most 16 distinct strings of at most 48 characters. They are RoomPlan attribute strings, and task T12 reconciles them with the strings S1 recorded. |
| `size` | `[w, h, d]`, the reference size in metres. |
| `triangles`, `sha256` | Computed from the file (`profile_tool.py`). `PropSet` checks both. |
| `slots` | 1 to 8 `{name, cell}` entries, in the order `RoomObject.PropCells` follows. |
| `profile` | `station/forge/forge/profile.py:of_mesh` of the prop at `size`, in standard base64 (`profile_tool.py`). |
| `source`, `licence` | The kit and the file the prop was made from. The licence is `CC0-1.0`, and `CREDITS.md` names every source. |

**Profile convention.** The profile is indexed exactly like `RoomObject.Profile` (`A/Scan/Build/ObjectProfile.cs`):
- 16 × 16 cells, with i along local X and j along local Z;
- index `j * 16 + i`, row 0 at the lowest z;
- 0 for a cell no ray hit; Floor-class triangles are left out.

`reference.json` → `convention` proves it. The two vectors are the couch fixture plus Floor-class triangles,
once in the box frame and once yawed and moved. Both were run through `ObjectProfile.Compute`, which gave the
same 256 bytes as `of_mesh`.

## Reference vectors

`reference.json` holds the following sections:
- `profiles`: the fixtures' profiles.
- `convention`: the profile convention check above.
- `rotate`: turns 0 to 3 of the asymmetric couch.
- `fitScore`: fit-score pairs.
- `scale`: the Forge's scale-to-box rule (`cleanup._candidate`, a = 0.15) for six box sizes and turns, with the
  fitted mesh's profile.
- `matcher`: a library and the expected `PropMatcher` choice for nine queries, covering every facing rule,
  with and without a profile, low coverage, tags, a tie broken by id, and a category with no props.

`conventions` in the file states how the vectors read the plan's rules. It covers the quarter-turn direction
((x, z) → (−z, x) in Unity's frame, the turn whose profile is `rotate(profile, k)`) and the tie rule (every
score less than 0.02 above the lowest is tied, then the smaller turn wins, then the ordinal-smaller id).
`reference.py` refuses to write any matcher vector that lies within 1e-3 of a threshold, where float32 could
decide it the other way.
