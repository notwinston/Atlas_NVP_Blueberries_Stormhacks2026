"""The shared palette and the one place that encodes it into vertex data.

Contract page lane-a-platform.md, sections 10.2 (mesh vertex format) and 10.7
(the palette); Master Plan section 8.1. Every surface samples one 128 x 128
palette texture of 16 x 16 flat cells, and shading is baked into the vertex
colour. The Forge may pick an object colour from cells 64 to 127 only.

The bytes written per vertex come from ``encode_vertex_cells`` alone (and
``encode_vertex_attributes``, the same for a mesh of one colour), and the shade
that goes into them from ``face_shade``; both mirror lane A's
`AtlasVertex.PackColor` and `AtlasShading.FaceShade`.
"""
from __future__ import annotations

import colorsys
from dataclasses import dataclass

import numpy as np

PALETTE_TEXTURE_SIZE = 128
PALETTE_GRID = 16  # cells per side; cell index = row * 16 + column, row 0 at the bottom (v = 0)
OBJECT_BASE = 64  # `AtlasPalette.ObjectBase`: first of the 64 object colours (rows 4 to 7)

# Lane A's diagram light (AtlasShading): direction towards the light in the room frame, and
# the brightness of a face that points away from it.
LIGHT_DIRECTION = np.array([0.35, 0.85, 0.40]) / np.linalg.norm([0.35, 0.85, 0.40])
SHADE_FLOOR = 0.55


@dataclass(frozen=True)
class PaletteEntry:
    index: int  # palette cell, 64..127; sent as `AssetReady.palette`
    name: str
    rgb: tuple[int, int, int]  # sRGB, 0-255

    @property
    def hex(self) -> str:
        return "#%02x%02x%02x" % self.rgb


_VALUES = (0.28, 0.42, 0.58, 0.74)  # rows 4 to 7
_HUES = ("red", "orange", "yellow", "lime", "green", "spring", "cyan", "azure", "blue", "violet", "magenta", "rose")
# Columns 12 to 15: (name, hue in degrees, saturation).
_NEUTRALS = (("warm grey", 30.0, 0.12), ("cool grey", 210.0, 0.12), ("wood", 28.0, 0.35), ("grey", 0.0, 0.0))


def _object_colours() -> tuple[PaletteEntry, ...]:
    entries = []
    for k, value in enumerate(_VALUES):
        for column in range(PALETTE_GRID):
            if column < 12:
                name, hue, saturation = _HUES[column], column * 30.0, 0.50
            else:
                name, hue, saturation = _NEUTRALS[column - 12]
            r, g, b = colorsys.hsv_to_rgb(hue / 360.0, saturation, value)
            entries.append(
                PaletteEntry(OBJECT_BASE + k * PALETTE_GRID + column, f"{name} {k}",
                             (int(round(r * 255)), int(round(g * 255)), int(round(b * 255))))
            )
    return tuple(entries)


PALETTE: tuple[PaletteEntry, ...] = _object_colours()


def _srgb_to_oklab(rgb: np.ndarray) -> np.ndarray:
    """sRGB in 0-255 to OKLab; distances there follow perceived difference."""
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m1 = np.array(
        [[0.4122214708, 0.5363325363, 0.0514459929],
         [0.2119034982, 0.6806995451, 0.1073969566],
         [0.0883024619, 0.2817188376, 0.6299787005]]
    )
    m2 = np.array(
        [[0.2104542553, 0.7936177850, -0.0040720468],
         [1.9779984951, -2.4285922050, 0.4505937099],
         [0.0259040371, 0.7827717662, -0.8086757660]]
    )
    lms = np.cbrt(lin @ m1.T)
    return lms @ m2.T


def nearest_entry(rgb) -> PaletteEntry:
    """The palette entry closest to ``rgb`` (sRGB 0-255)."""
    target = _srgb_to_oklab(np.asarray(rgb, dtype=np.float64))
    table = _srgb_to_oklab(np.array([e.rgb for e in PALETTE], dtype=np.float64))
    return PALETTE[int(np.argmin(((table - target) ** 2).sum(axis=1)))]


def entry(index: int) -> PaletteEntry:
    """The object colour stored in palette cell ``index`` (64..127)."""
    return PALETTE[index - OBJECT_BASE]


def cell_uv(index: int) -> tuple[float, float]:
    """`AtlasPalette.Uv`: centre of palette cell ``index``, v measured from the bottom."""
    column, row = index % PALETTE_GRID, index // PALETTE_GRID
    return (column + 0.5) / PALETTE_GRID, (row + 0.5) / PALETTE_GRID


def face_shade(normals: np.ndarray, yaw: float = 0.0) -> np.ndarray:
    """`AtlasShading.FaceShade` for normals as the GLB stores them.

    The light is fixed in the room frame, so the normal is taken there first:
    the importer on the phone inverts X (glTF is right-handed, Unity is not),
    and the object then stands in the room turned by ``yaw`` about +Y in
    Unity's sense (positive turns +Z towards +X).
    """
    n = np.asarray(normals, dtype=np.float64)
    x, y, z = -n[:, 0], n[:, 1], n[:, 2]
    cos, sin = np.cos(yaw), np.sin(yaw)
    room = np.stack([x * cos + z * sin, y, -x * sin + z * cos], axis=1)
    return SHADE_FLOOR + (1.0 - SHADE_FLOOR) * (0.5 + 0.5 * (room @ LIGHT_DIRECTION))


def encode_vertex_cells(cells, shade: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Per-vertex TEXCOORD_0 and COLOR_0 for a mesh whose vertices each point at a palette cell.

    ``cells`` holds one palette cell per vertex (64..127 for a furniture
    mesh); every vertex of a face must carry that face's cell
    (`colour.split_by_cell`). ``shade`` holds one value per vertex in [0, 1]:
    the face shade times the ambient occlusion, 1 meaning fully lit and open.
    Returns ``(uv, colour)``. uv is (N, 2) float32 and points at the centre of
    the vertex's cell. colour is (N, 4) uint8, linear: R is the shade, G the
    line weight (255) and B and A are the reserved 255.

    A GLB stores v downwards and the importer on the phone flips it, so v is
    written as 1 - v for a cell whose Unity coordinate is (u, v).
    """
    cells = np.asarray(cells, dtype=np.int64).reshape(-1)
    shade = np.clip(np.asarray(shade, dtype=np.float64), 0.0, 1.0)
    if len(cells) != len(shade):
        raise ValueError("one cell and one shade per vertex")
    uv = np.empty((len(cells), 2), dtype=np.float32)
    uv[:, 0] = (cells % PALETTE_GRID + 0.5) / PALETTE_GRID
    uv[:, 1] = 1.0 - (cells // PALETTE_GRID + 0.5) / PALETTE_GRID
    colour = np.full((len(cells), 4), 255, dtype=np.uint8)
    colour[:, 0] = np.rint(shade * 255.0).astype(np.uint8)
    return uv, colour


def encode_vertex_attributes(entry: PaletteEntry, shade: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """``encode_vertex_cells`` for a mesh of one palette colour."""
    return encode_vertex_cells(np.full(len(shade), entry.index, dtype=np.int64), shade)
