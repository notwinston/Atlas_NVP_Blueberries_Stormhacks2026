# Credits

Third-party assets in ATLAS whose licence asks for attribution. Credit them wherever the app is shown or shipped.

## Rubber duck (S3 duck maze)

"Free Rubber Duck 3d Model" by Ryan King Art (https://sketchfab.com/ryankingart),
<https://sketchfab.com/3d-models/free-rubber-duck-3d-model-c5739cd7a94547dcb091bdd527536977>,
licensed under CC BY 4.0 (<http://creativecommons.org/licenses/by/4.0/>).

Modified: decimated, recoloured to the ATLAS palette, rescaled. (Merged into one mesh, decimated to 2,400 triangles,
scaled to 0.30 m tall, and recoloured with the four palette cells `DuckYellow`, `DuckOrange`, `DuckWhite` and
`DuckBlack`.)

Source file: `art/source/free_rubber_duck_3d_model.glb`; build steps: [art/blender/README.md](art/blender/README.md).
In the app: the kit piece `duck` (`ATLAS/Assets/Atlas/Spaces/Glued/Resources/AtlasKit/duck.glb.bytes`).

## Room props (matched furniture, standalone phone)

The phone replaces each scanned object with a matched prop. Today the props come from one kit.

"Furniture Kit 2.0" by Kenney (https://www.kenney.nl), <https://kenney.nl/assets/furniture-kit>, released under
CC0 1.0 (<https://creativecommons.org/publicdomain/zero/1.0/>). Credit is not required; we give it anyway.

Modified: each model is rescaled to a real furniture size, turned to face +Z, recoloured to the ATLAS object
palette (1 to 4 colour slots per prop), had its holes closed and its edges split to 0.25 m, and the beds and
stairs marked in `tools/props/build_props.py` as remeshed were rebuilt as one outer shell.

Sources: `art/props/sources/kenney_furniture/` (the used GLBs and the kit's `License.txt`). Build steps:
[art/props/README.md](art/props/README.md). In the app: `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasProps/`.
