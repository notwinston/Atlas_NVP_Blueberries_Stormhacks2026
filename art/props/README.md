# Prop sources

The CC0 models behind the phone's matched props (standalone-phone plan, task T4). The finished props are in
`ATLAS/Assets/Atlas/Telemetry/Resources/AtlasProps/` (39 props, `manifest.json`, 2.6 MB). Credits are in
`CREDITS.md`.

## What is here

| Path | Holds |
|---|---|
| `sources/kenney_furniture/glb/` | The Kenney Furniture Kit 2.0 models that were used (GLB), plus `License.txt` (CC0). Downloaded from `https://kenney.nl/media/pages/assets/furniture-kit/440e0608a4-1677580847/kenney_furniture-kit.zip` (sha256 `e67652d0932cee41683f74711c03d3e192a2af9979ef8e6b237711f5482d46b0`). The rest of the zip was dropped. |
| `previews/contact.png` | Every prop, seen from the front-left above (the front is the side that faces the camera), with a 1 m red ruler beside it and its id below. Regenerate with the `contact` command below. |

## Rebuild

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b -P tools/props/build_props.py -- build
tools/props/.venv/bin/python tools/props/profile_tool.py --write
tools/props/.venv/bin/python tools/props/check.py
/Applications/Blender.app/Contents/MacOS/Blender -b -P tools/props/build_props.py -- contact
```

The build is deterministic (two runs give identical files). `tools/props/build_props.py` holds the table of props:
id, category, tags, source file, quarter turns and reference size.

## Coverage

| Category | Props | Note |
|---|---|---|
| bathtub | 1 | gap: needs 2 more |
| bed | 2 | single, double (double is rebuilt as a shell) |
| chair | 4 | dining, swivel, stool, armchair |
| couch | 4 | straight, corner, chaise, design |
| oven | 1 | **stand-in**: the electric range of the kit, also used for `stove` (the kit has no wall oven) |
| refrigerator | 3 | standard, built-in, small |
| sink | 3 | kitchen, bathroom pedestal, vanity |
| stairs | 2 | closed, corner (both rebuilt as shells) |
| storage | 5 | low shelf, wide shelf, bookcase, sideboard, counter cabinet |
| stove | 2 | gas, electric |
| table | 4 | rectangular, round, coffee, desk |
| screen | 3 | modern, vintage, monitor |
| toilet | 2 | round, square |
| washerDryer | 3 | washer, dryer, stacked |
| **dishwasher** | 0 | **gap**: no model in the kit. The category keeps the built-in template. |
| **fireplace** | 0 | **gap**: no model in the kit. The category keeps the built-in template. |

39 props, not the planned 48.

## Gaps and what was not done

- **KayKit Furniture Bits (itch.io) and Quaternius furniture packs (Google Drive) were not downloaded.** Both need a
  browser. No zip of them was in `art/props/sources/`. Dropping either zip there and adding rows to `PROPS` in
  `build_props.py` is all it takes (the build reads `glb` files; the table names the folder). They would fill the
  dishwasher, fireplace, oven and the second and third bathtub.
- Four models could not be kept: the bunk bed, the open stairs, the open bookcase and the large fridge came out
  of the sharp remesh in pieces (thin posts and open shelves fall between the octree cells), and as plain sources
  they need 4,000 to 14,000 triangles for the 0.25 m edge rule, over the 2,500 ceiling.
- Remeshed props (`REMESH` in `build_props.py`) lose small details: the double bed's pillows and the stairs'
  treads are flat patches.
- **Tags are a guess.** They are authored from Apple's documented `CapturedRoom.Object.Attribute` names (chair
  type: dining, swivel, stool; table shape: rectangular, circular, oval; storage kind: shelf, cabinet, dresser)
  plus plain words for couch and bed variants (`straight`, `corner`, `chaise`, `single`, `double`, `arm`). Task T12
  reconciles them with the strings spike S1 records.
- **Fronts.** Kenney models face +Z (glTF), so no prop needed a turn except the stairs (one quarter turn, so the
  climb runs along the box's Z, bottom step at +Z). The contact sheet was read to confirm that sofas, ovens,
  washers, toilets, TVs and beds face the camera side. The stairs' front (the bottom step) is a choice: RoomPlan
  does not define which end of a staircase is the front.
- Sizes keep Kenney's proportions within about 1.6x stretch at most (the stoves and fridges, whose Kenney models
  are squat); the fitter stretches props to the scanned box anyway.
- Colours: a prop keeps its source materials as slots (1 to 4), each mapped to the nearest free object palette
  cell. Most Kenney materials are pastel, so many props read as grey or beige until the phone recolours them from
  the photos.
