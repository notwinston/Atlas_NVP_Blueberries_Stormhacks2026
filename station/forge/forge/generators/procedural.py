"""A deterministic generator that needs no GPU.

It builds a plausible piece of furniture for the category from the box
dimensions alone. It exists for tests and for running the whole scan and asset
round-trip on a machine with no graphics card; the photo is not looked at.

The mesh is returned the way an image-to-3D model returns one: normalised to a
unit cube around the origin, so clean-up's fit to the box is exercised.
"""
from __future__ import annotations

from typing import Optional

import numpy as np

from .base import GenerationRequest, RawMesh

_BOX_FACES = np.array(
    [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4],
     [1, 2, 6], [1, 6, 5], [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]]
)


class _Builder:
    """Collects solids in box-local metres: origin at the base centre, +Y up."""

    def __init__(self) -> None:
        self.vertices: list[np.ndarray] = []
        self.faces: list[np.ndarray] = []
        self.count = 0

    def _add(self, vertices: np.ndarray, faces: np.ndarray) -> None:
        self.vertices.append(vertices)
        self.faces.append(faces + self.count)
        self.count += len(vertices)

    def box(self, x0, x1, y0, y1, z0, z1) -> None:
        corners = np.array(
            [[x0, y0, z0], [x1, y0, z0], [x1, y0, z1], [x0, y0, z1],
             [x0, y1, z0], [x1, y1, z0], [x1, y1, z1], [x0, y1, z1]], dtype=np.float64
        )
        # Seen from outside every face must wind counter-clockwise.
        self._add(corners, _BOX_FACES[:, ::-1])

    def cylinder(self, cx, cz, y0, y1, r0, r1=None, segments: int = 12) -> None:
        """A capped cylinder or cone frustum about the vertical through (cx, cz)."""
        r1 = r0 if r1 is None else r1
        angle = np.arange(segments) * (2.0 * np.pi / segments)
        ring = np.stack([np.cos(angle), np.zeros(segments), np.sin(angle)], axis=1)
        bottom = ring * [r0, 0, r0] + [cx, y0, cz]
        top = ring * [r1, 0, r1] + [cx, y1, cz]
        vertices = np.vstack([bottom, top, [[cx, y0, cz], [cx, y1, cz]]])
        i = np.arange(segments)
        j = (i + 1) % segments
        b_centre, t_centre = 2 * segments, 2 * segments + 1
        faces = np.vstack(
            [
                np.stack([i, i + segments, j + segments], axis=1),
                np.stack([i, j + segments, j], axis=1),
                np.stack([np.full(segments, b_centre), i, j], axis=1),
                np.stack([np.full(segments, t_centre), j + segments, i + segments], axis=1),
            ]
        )
        self._add(vertices, faces)

    def ellipsoid(self, cx, cy, cz, rx, ry, rz, rings: int = 6, segments: int = 12) -> None:
        polar = np.linspace(0.0, np.pi, rings + 1)[1:-1]
        angle = np.arange(segments) * (2.0 * np.pi / segments)
        body = np.stack(
            [np.outer(np.sin(polar), np.cos(angle)), np.outer(np.cos(polar), np.ones(segments)),
             np.outer(np.sin(polar), np.sin(angle))], axis=2
        ).reshape(-1, 3)
        vertices = np.vstack([body, [[0, 1, 0], [0, -1, 0]]]) * [rx, ry, rz] + [cx, cy, cz]
        north, south = len(body), len(body) + 1
        i = np.arange(segments)
        j = (i + 1) % segments
        faces = [np.stack([np.full(segments, north), j, i], axis=1)]
        for r in range(rings - 2):
            a, b = r * segments, (r + 1) * segments
            faces.append(np.stack([a + i, a + j, b + j], axis=1))
            faces.append(np.stack([a + i, b + j, b + i], axis=1))
        last = (rings - 2) * segments
        faces.append(np.stack([np.full(segments, south), last + i, last + j], axis=1))
        self._add(vertices, np.vstack(faces))

    def mesh(self) -> tuple[np.ndarray, np.ndarray]:
        return np.vstack(self.vertices), np.vstack(self.faces)


def _legs(b: _Builder, w: float, d: float, height: float, leg: float, inset: float = 0.0) -> None:
    for sx in (-1, 1):
        for sz in (-1, 1):
            x = sx * (w / 2 - inset - leg / 2)
            z = sz * (d / 2 - inset - leg / 2)
            b.box(x - leg / 2, x + leg / 2, 0, height, z - leg / 2, z + leg / 2)


def _table(b, w, h, d):
    top = min(0.06, h * 0.12)
    leg = min(0.07, min(w, d) * 0.12)
    b.box(-w / 2, w / 2, h - top, h, -d / 2, d / 2)
    _legs(b, w, d, h - top, leg, inset=min(w, d) * 0.04)


def _chair(b, w, h, d):
    seat_h, seat_t = h * 0.48, min(0.06, h * 0.07)
    leg = min(0.05, min(w, d) * 0.12)
    b.box(-w / 2, w / 2, seat_h - seat_t, seat_h, -d / 2, d / 2)
    _legs(b, w, d, seat_h - seat_t, leg)
    b.box(-w / 2, w / 2, seat_h, h, d / 2 - min(0.06, d * 0.15), d / 2)


def _couch(b, w, h, d):
    arm = min(0.22, w * 0.14)
    back = min(0.25, d * 0.28)
    base_h = h * 0.32
    b.box(-w / 2, w / 2, 0, base_h, -d / 2, d / 2)
    b.box(-w / 2 + arm, w / 2 - arm, base_h, h * 0.52, -d / 2, d / 2 - back)
    b.box(-w / 2, w / 2, base_h, h, d / 2 - back, d / 2)
    b.box(-w / 2, -w / 2 + arm, base_h, h * 0.72, -d / 2, d / 2 - back)
    b.box(w / 2 - arm, w / 2, base_h, h * 0.72, -d / 2, d / 2 - back)


def _bed(b, w, h, d):
    frame_h = h * 0.38
    head = min(0.1, d * 0.06)
    b.box(-w / 2, w / 2, 0, frame_h, -d / 2, d / 2)
    b.box(-w / 2 * 0.96, w / 2 * 0.96, frame_h, h * 0.62, -d / 2 * 0.97, d / 2 - head)
    b.box(-w / 2, w / 2, frame_h, h, d / 2 - head, d / 2)
    b.box(-w * 0.4, w * 0.4, h * 0.62, h * 0.74, d / 2 - head - d * 0.2, d / 2 - head - d * 0.03)


def _storage(b, w, h, d):
    plinth = min(0.08, h * 0.08)
    board = min(0.04, min(w, h) * 0.06)
    b.box(-w / 2 * 0.94, w / 2 * 0.94, 0, plinth, -d / 2 * 0.9, d / 2)
    b.box(-w / 2, w / 2, plinth, h, -d / 2 * 0.96, d / 2)
    # Door fronts proud of the carcass, with a gap that reads as the split.
    b.box(-w / 2 + board, -board / 4, plinth + board, h - board, -d / 2, -d / 2 * 0.96 + 0.001)
    b.box(board / 4, w / 2 - board, plinth + board, h - board, -d / 2, -d / 2 * 0.96 + 0.001)


def _lamp(b, w, h, d):
    r = min(w, d) / 2
    b.cylinder(0, 0, 0, h * 0.04, r * 0.6)
    b.cylinder(0, 0, h * 0.04, h * 0.72, max(r * 0.07, 0.008))
    b.cylinder(0, 0, h * 0.7, h, r, r * 0.62)


def _plant(b, w, h, d):
    r = min(w, d) / 2
    b.cylinder(0, 0, 0, h * 0.3, r * 0.45, r * 0.6)
    b.cylinder(0, 0, h * 0.3, h * 0.5, max(r * 0.08, 0.008))
    b.ellipsoid(0, h * 0.7, 0, w / 2, h * 0.3, d / 2)


def _screen(b, w, h, d):
    panel = min(d, max(0.03, d * 0.25))
    b.box(-w * 0.2, w * 0.2, 0, h * 0.04, -d / 2, d / 2)
    b.box(-w * 0.04, w * 0.04, h * 0.04, h * 0.2, -panel / 2, panel / 2)
    b.box(-w / 2, w / 2, h * 0.16, h, -panel / 2, panel / 2)


def _stairs(b, w, h, d):
    steps = int(np.clip(round(h / 0.18), 2, 16))
    for i in range(steps):
        b.box(-w / 2, w / 2, 0, h * (i + 1) / steps, -d / 2 + d * i / steps, -d / 2 + d * (i + 1) / steps)


def _basin(b, w, h, d):
    rim = min(0.05, min(w, d) * 0.1)
    b.box(-w / 2, w / 2, 0, h * 0.85, -d / 2, d / 2)
    b.box(-w / 2, w / 2, h * 0.85, h, -d / 2, -d / 2 + rim)
    b.box(-w / 2, w / 2, h * 0.85, h, d / 2 - rim, d / 2)
    b.box(-w / 2, -w / 2 + rim, h * 0.85, h, -d / 2 + rim, d / 2 - rim)
    b.box(w / 2 - rim, w / 2, h * 0.85, h, -d / 2 + rim, d / 2 - rim)


def _toilet(b, w, h, d):
    b.box(-w / 2, w / 2, h * 0.45, h, d / 2 - d * 0.28, d / 2)
    b.cylinder(0, -d * 0.12, 0, h * 0.45, min(w, d * 0.72) * 0.32, min(w, d * 0.72) * 0.5)
    b.box(-w * 0.3, w * 0.3, 0, h * 0.45, 0, d / 2)
    b.box(-w / 2, w / 2, 0, h * 0.02, -d / 2, d / 2 - d * 0.28)


def _generic(b, w, h, d):
    plinth = min(0.05, h * 0.1)
    b.box(-w / 2 * 0.92, w / 2 * 0.92, 0, plinth, -d / 2 * 0.92, d / 2 * 0.92)
    b.box(-w / 2, w / 2, plinth, h, -d / 2, d / 2)


_SHAPES = {
    "table": _table,
    "chair": _chair,
    "couch": _couch,
    "sofa": _couch,
    "bed": _bed,
    "storage": _storage,
    "refrigerator": _storage,
    "dishwasher": _storage,
    "oven": _storage,
    "stove": _storage,
    "washerDryer": _storage,
    "fireplace": _storage,
    "lamp": _lamp,
    "plant": _plant,
    "screen": _screen,
    "stairs": _stairs,
    "bathtub": _basin,
    "sink": _basin,
    "toilet": _toilet,
}


def build_furniture(category: str, dims) -> tuple[np.ndarray, np.ndarray]:
    """A mesh for ``category`` filling the box ``[w, h, d]``; box-local metres, base-centre origin."""
    w, h, d = (float(v) for v in dims)
    builder = _Builder()
    _SHAPES.get(category, _generic)(builder, w, h, d)
    return builder.mesh()


class ProceduralGenerator:
    """Stands in for the image-to-3D model with no GPU. Reported as source "local"."""

    name = "procedural"
    source = "local"

    def availability(self) -> tuple[bool, str]:
        return True, ""

    def gpu(self) -> Optional[str]:
        return None

    def generate(self, request: GenerationRequest) -> RawMesh:
        vertices, faces = build_furniture(request.category, request.dims)
        lo, hi = vertices.min(axis=0), vertices.max(axis=0)
        vertices = (vertices - (lo + hi) * 0.5) / float((hi - lo).max())
        return RawMesh(vertices=vertices, faces=faces)
