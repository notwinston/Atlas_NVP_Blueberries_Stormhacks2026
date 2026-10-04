#!/usr/bin/env python3
"""Turns the six tiles of the Small Planet (S4) world, modelled in Blender, into kit pieces for Unity.

The Blender side (tools/kit/export_planet.py) hands this module triangles in Blender's tile-chart frame; this
module turns them into one kitglb.Piece per tile and writes them with kitglb.write_glb, so a later loader can
read them with KitSet exactly like Glued's pieces. Python standard library only, no bpy, so everything here is
tested on any machine (tools/kit/test_planetglb.py).

What differs from a Glued kit piece (kitglb.check_piece is not used):

- A tile is an open sheet (the ground) plus props, so it is not closed; check_tile checks everything else.
- The frame. Blender authors every tile in its own chart (smallplanet_build.py, TILE_DEFS): metres, origin at
  the tile centre, +Z up. Unity's chart of the same tile (Atlas.Kernel.CubeTiling frames, read by AtlasWarp.hlsl
  in warp mode 2 as positionOS.xz) agrees on Home and Beacon, but on the four side tiles it is turned a quarter:
  Blender's side tiles have +Y towards Home, Unity's have +X towards Home (frame column c0 = Home's centre).
  So on East, North, West and South the Unity chart is (x_u, y_u) = (y_b, -x_b). Then the usual Blender to Unity
  swap: positionOS = (x_u, z_b, y_u).
- The scale. The planet's radius R is a runtime setting, so x and z (the chart) are stored divided by the radius
  the file was authored at (R0, recorded in the manifest); y (height) stays in metres. The runtime multiplies x
  and z by its own R. Normals are the true metric normals of the tile at R = R0 (not scaled).
- Colour. A face's palette cell comes from the face attribute 'Pal' when the build wrote one, otherwise from
  the nearest cell (OKLab) to its 'Col' colour; lantern faces (material SP_Lantern) take the emissive Beacon
  cell. Whatever brightness 'Col' has beyond the cell goes into the shade byte, times the look's fixed light.
- Lines. Ground faces get line coordinates that run 0..1 across the tile in the gnomonic (Klein) square, as
  TileMeshGenerator's floor has them, so the look's minor lines fall exactly on the tile edges. Props get a
  constant (0.5, 0.5): no lines.
"""
from __future__ import annotations

import colorsys
import json
import math
from dataclasses import dataclass, field

import kitglb

# CubeTiling face order; also the order of Blender's TILE_DEFS. Piece names are tile0 .. tile5.
TILE_NAMES = ("Home", "East", "North", "West", "South", "Beacon")
SIDE_TILES = frozenset((1, 2, 3, 4))
HALF_WIDTH = math.pi / 4          # tile half-width along a chart axis, in units of R (R * atan(1))

# Triangles per tile. The S4 budget is <= 190k triangles and <= 70 draw calls for both eyes (Master Plan 8.5);
# six tiles at this ceiling are 2 x 6 x 3000 = 36k, which leaves the room, the avatar and the Hub their share.
# The tiles authored at PLANET_SCALE 1.5 use 650 to 2,700.
TILE_BUDGET = 3000

EMISSIVE_ROW = 12
BEACON_CELL = 193                 # AtlasPalette.Beacon: emissive accent FFD166
LANTERN_MATERIAL = "SP_Lantern"
NO_LINES = (0.5, 0.5)

# Brightness factor the shade may carry when a cell is chosen from 'Col' alone.
SEARCH_GAIN = (0.85, 1.15)


# ---- Palette (a port of AtlasPalette.TexelSrgb at the cell centres) ------------------------------------------

_STRUCTURE = (0x1B222B, 0x222B36, 0x2A3542, 0x33404F, 0x3A4858, 0x50627A, 0x6B7F99, 0x161C24,
              0x0E1319, 0x3B4654, 0x7A8794, 0xD9534F, 0x3FA7D6, 0xC9A227, 0x4A3F6B, 0x2E2A45)
_ACCENTS = (0x7DF9FF, 0xFFD166, 0x9BE564, 0xFF6B6B, 0xFFE66D, 0xFFB020, 0xFF3B30, 0x5AC8FA,
            0xFFFFFF, 0x34C759, 0xFFCC00, 0x8E8E93, 0xFFFFFF, 0xFFFFFF, 0x6FA8C8, 0x000000)


def _hsv(h, s, v):
    return colorsys.hsv_to_rgb((h % 360.0) / 360.0, s, v)


def _hex(u):
    return ((u >> 16) & 255) / 255.0, ((u >> 8) & 255) / 255.0, (u & 255) / 255.0


def palette_srgb(index: int) -> tuple[float, float, float]:
    """sRGB 0..1 of AtlasPalette cell index (0..255) at the cell centre, before 8-bit rounding."""
    column, row = index & 15, (index >> 4) & 15
    along = (column * 8 + 4) / 127.0
    if row == 0:
        return _hsv(215, 0.25 - 0.01 * column, 0.03 + 0.05 * column)
    if row == 1:
        return _hex(_STRUCTURE[column])
    if row in (2, 3):
        k = (row - 2) * 16 + column
        if 28 <= k <= 31:                      # cells 60 to 63: the rubber duck (AtlasPalette.DuckYellow .. DuckBlack)
            return _hex((0xFFC21A, 0xFF4A12, 0xE8E8E8, 0x1A1A1A)[k - 28])
        return _hsv(0, 0, 0.45) if k == 0 else _hsv(k * 137.5, 0.40, 0.50)
    if 4 <= row <= 7:
        v = (0.28, 0.42, 0.58, 0.74)[row - 4]
        if column < 12:
            return _hsv(column * 30, 0.5, v)
        return {12: _hsv(30, 0.12, v), 13: _hsv(210, 0.12, v), 14: _hsv(28, 0.35, v)}.get(column, _hsv(0, 0, v))
    if row == 8:
        return _hsv((200, 150, 35, 320)[column // 4], 0.45, 0.22 + 0.12 * (column % 4))
    if row == 9:
        return _hsv(15, 0.55, 0.15 + 0.6 * along)
    if row == 10:
        return _hsv(205, 0.55, 0.15 + 0.6 * along)
    if row == 11:
        return _hsv(215, 0.20, 0.40 - 0.35 * along)
    if row == 12:
        return _hex(_ACCENTS[column])
    if row in (13, 14):
        return _hsv(((row - 13) * 16 + column) * 137.5, 0.65, 0.95)
    return tuple(a + (b - a) * along for a, b in zip(_hex(0xFF7F6A), _hex(0x8E6BFF)))


def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def linear_to_srgb(c: float) -> float:
    c = min(1.0, max(0.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def hex_srgb(linear_rgb) -> str:
    return "#%02X%02X%02X" % tuple(round(255 * linear_to_srgb(c)) for c in linear_rgb)


def palette_linear(index: int) -> tuple[float, float, float]:
    """AtlasPalette.Linear: the stored 8-bit value, linearised, as the shader samples it."""
    return tuple(srgb_to_linear(round(c * 255) / 255.0) for c in palette_srgb(index))


def oklab(linear_rgb) -> tuple[float, float, float]:
    """OKLab of a linear sRGB colour (the matrices of station/forge/forge/palette.py)."""
    r, g, b = (max(0.0, c) for c in linear_rgb)
    l_ = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m_ = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s_ = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l_, m_, s_ = (math.copysign(abs(v) ** (1.0 / 3.0), v) for v in (l_, m_, s_))
    return (0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
            1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
            0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_)


def oklab_distance(a, b) -> float:
    return math.dist(oklab(a), oklab(b))


# Non-emissive cells: what a lit face may take. Rows 12..15 glow and are only given on purpose (the lantern).
SURFACE_CELLS = tuple(range(EMISSIVE_ROW * 16))
_LINEAR = {i: palette_linear(i) for i in range(256)}


def gain(rgb, cell: int) -> float:
    """The brightness factor that best scales the cell's colour onto rgb (least squares in linear rgb)."""
    p = _LINEAR[cell]
    pp = sum(c * c for c in p)
    return sum(a * b for a, b in zip(rgb, p)) / pp if pp > 0 else 1.0


def _scaled(cell, f):
    return tuple(c * f for c in _LINEAR[cell])


@dataclass(frozen=True)
class Colour:
    cell: int        # palette cell
    gain: float      # brightness factor for the shade
    error: float     # OKLab distance between the authored colour and cell * gain (before the light)


def match_colour(rgb, cell: int | None = None) -> Colour:
    """The palette cell of an authored linear colour, its brightness factor and the colour error left.

    cell given (the 'Pal' attribute): that cell, the factor that fits best. None: the non-emissive cell that,
    scaled by a factor within SEARCH_GAIN, comes nearest in OKLab."""
    if cell is not None:
        f = max(0.0, gain(rgb, cell))
        return Colour(cell, f, oklab_distance(rgb, _scaled(cell, f)))
    target = oklab(rgb)
    best = None
    for c in SURFACE_CELLS:
        f = min(SEARCH_GAIN[1], max(SEARCH_GAIN[0], gain(rgb, c)))
        d = math.dist(target, oklab(_scaled(c, f)))
        if best is None or d < best.error:
            best = Colour(c, f, d)
    return best


# ---- Frame ---------------------------------------------------------------------------------------------------

def unity_chart(tile: int, x: float, y: float) -> tuple[float, float]:
    """Blender's chart of a tile to Unity's (CubeTiling): the four side tiles turn a quarter, (x, y) -> (y, -x)."""
    if not 0 <= tile < len(TILE_NAMES):
        raise ValueError(f"tile {tile} is not one of the six faces")
    return (y, -x) if tile in SIDE_TILES else (x, y)


def to_unity(tile: int, p, radius: float) -> tuple[float, float, float]:
    """A Blender chart point (metres, Z up) to the stored position: (x_u / R0, height, y_u / R0)."""
    xu, yu = unity_chart(tile, p[0], p[1])
    return (xu / radius, p[2], yu / radius)


def normal_to_unity(tile: int, n) -> tuple[float, float, float]:
    """A Blender chart normal to Unity's frame (metric, not scaled by 1/R0), unit length."""
    xu, yu = unity_chart(tile, n[0], n[1])
    length = math.sqrt(xu * xu + yu * yu + n[2] * n[2])
    return (xu / length, n[2] / length, yu / length)


def klein(chart_xz_over_r) -> tuple[float, float]:
    """The gnomonic (Klein) point of a chart point given in units of R: k = c / |c| * tan(|c|). +-1 on the edges."""
    cx, cz = chart_xz_over_r
    d = math.hypot(cx, cz)
    if d < 1e-12:
        return (0.0, 0.0)
    t = math.tan(d) / d
    return (cx * t, cz * t)


def ground_lines(position) -> tuple[float, float]:
    """TileMeshGenerator's floor line coordinate: 0.5 + 0.5 k, so 0 and 1 on the tile's edges."""
    kx, kz = klein((position[0], position[2]))
    return (0.5 + 0.5 * kx, 0.5 + 0.5 * kz)


# ---- Tiles ---------------------------------------------------------------------------------------------------

@dataclass
class Face:
    """One authored polygon of a tile, in Blender's chart of that tile (metres, Z up, counter-clockwise front).

    triangles: corner triples of the polygon (Blender's loop triangles), each corner (x, y, z)."""
    triangles: list
    normal: tuple
    rgb: tuple                    # authored linear colour ('Col', averaged over the corners)
    cell: int | None = None       # 'Pal' when the build wrote it
    lantern: bool = False
    ground: bool = False
    source: str = ""


@dataclass
class TileStats:
    triangles: int = 0
    vertices: int = 0
    faces: int = 0
    skipped_faces: int = 0
    bounds_min: tuple = (0.0, 0.0, 0.0)
    bounds_max: tuple = (0.0, 0.0, 0.0)
    cells: dict = field(default_factory=dict)        # cell -> triangles
    worst_error: float = 0.0
    worst_at: str = ""
    mean_error: float = 0.0
    pal_disagreements: int = 0                       # faces where 'Pal' and the OKLab search differ
    shade_clamped: int = 0


def build_tile(tile: int, faces, radius: float) -> tuple[kitglb.Piece, TileStats]:
    """The faces of one tile as a piece in Unity's tile frame (xz in units of radius), with its statistics."""
    if not radius > 0:
        raise ValueError(f"radius {radius} must be positive")
    piece = kitglb.Piece()
    stats = TileStats()
    seen: dict[tuple, int] = {}
    error_sum = 0.0
    for face in faces:
        stats.faces += 1
        if face.lantern:
            colour = Colour(BEACON_CELL, 1.0, 0.0)
        else:
            colour = match_colour(face.rgb, face.cell)
            if face.cell is not None and match_colour(face.rgb).cell != face.cell:
                stats.pal_disagreements += 1
        error_sum += colour.error
        if colour.error > stats.worst_error:
            stats.worst_error = colour.error
            stats.worst_at = f"{face.source}: col {hex_srgb(face.rgb)} -> cell {colour.cell} {hex_srgb(_LINEAR[colour.cell])}"
        normal = normal_to_unity(tile, face.normal)
        shade = kitglb.face_shade(normal) * colour.gain
        if shade > 1.0:
            stats.shade_clamped += 1
            shade = 1.0
        uv0 = kitglb.palette_uv(colour.cell)
        for corners in face.triangles:
            ids = []
            for p in corners:
                position = to_unity(tile, p, radius)
                line = ground_lines(position) if face.ground else NO_LINES
                key = (position, normal, uv0, line, shade)
                if key not in seen:
                    seen[key] = len(piece.positions)
                    piece.positions.append(position)
                    piece.normals.append(normal)
                    piece.uv0.append(uv0)
                    piece.uv1.append(line)
                    piece.shade.append(shade)
                ids.append(seen[key])
            piece.indices += list(kitglb.blender_triangle(*ids))   # the y/z swap is a mirror: the winding turns
            stats.cells[colour.cell] = stats.cells.get(colour.cell, 0) + 1
    stats.triangles = piece.triangles
    stats.vertices = len(piece.positions)
    if piece.positions:
        stats.bounds_min = tuple(min(p[i] for p in piece.positions) for i in range(3))
        stats.bounds_max = tuple(max(p[i] for p in piece.positions) for i in range(3))
    stats.mean_error = error_sum / stats.faces if stats.faces else 0.0
    return piece, stats


def check_tile(name: str, piece: kitglb.Piece, max_triangles: int = TILE_BUDGET, max_extent: float = 1.0) -> None:
    """kitglb.check_piece without the closed-mesh rule (a tile is an open sheet), plus: xz within max_extent
    (units of R) of the tile centre. ValueError naming the tile when it cannot ship."""
    count = len(piece.positions)
    if count == 0 or not piece.indices:
        raise ValueError(f"{name}: the tile is empty")
    if count > 65535:
        raise ValueError(f"{name}: {count} vertices; a piece has at most 65535 (16-bit indices)")
    for label in ("normals", "uv0", "uv1", "shade"):
        n = len(getattr(piece, label))
        if n != count:
            raise ValueError(f"{name}: {label} has {n} entries for {count} positions")
    if len(piece.indices) % 3:
        raise ValueError(f"{name}: {len(piece.indices)} indices is not a whole number of triangles")
    if piece.triangles > max_triangles:
        raise ValueError(f"{name}: {piece.triangles} triangles, over the budget of {max_triangles}")
    for k, i in enumerate(piece.indices):
        if not 0 <= i < count:
            raise ValueError(f"{name}: index {i} at position {k} is out of range ({count} vertices)")
    for k, n in enumerate(piece.normals):
        length = math.sqrt(sum(c * c for c in n))
        if abs(length - 1.0) > kitglb.NORMAL_TOLERANCE:
            raise ValueError(f"{name}: normal {k} is not unit length ({length:.4f})")
    for k, p in enumerate(piece.positions):
        if abs(p[0]) > max_extent or abs(p[2]) > max_extent:
            raise ValueError(f"{name}: vertex {k} at xz ({p[0]:.4f}, {p[2]:.4f}) R lies off the tile")


def radius_from_ground(ground_points) -> float:
    """The authored radius from a tile's ground sheet: its widest chart coordinate is R * atan(1) = R pi/4 (the
    edge midpoints of the gnomonic square)."""
    widest = max(max(abs(p[0]), abs(p[1])) for p in ground_points)
    return widest / HALF_WIDTH


def write_manifest(pieces: dict[str, bytes], planet: dict) -> bytes:
    """kitglb.write_manifest's {"pieces": ...} (what KitSet reads and checks), plus a "planet" block."""
    doc = json.loads(kitglb.write_manifest(pieces).decode("utf-8"))
    doc["planet"] = planet
    return (json.dumps(doc, indent=2, sort_keys=True) + "\n").encode("utf-8")
