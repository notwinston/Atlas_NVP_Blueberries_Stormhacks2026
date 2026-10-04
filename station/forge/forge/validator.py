"""The asset validator: the asset contract of Master Plan section 6.4.

A generated GLB must hold exactly one mesh with no textures and no materials
(the phone assigns the shared one), in metres, +Y up, with its origin at the
centre of the box's base; no edge may be longer than 0.25 m; it must have 1,500
to 5,000 triangles and never more than the triangle target sent with its job.

Library:  ``validate_glb(data, tri_target=..., box=(w, h, d))``
CLI:      ``python -m forge.validator asset.glb --target 5000 --box 1.0 0.75 0.6``

Units, the up axis and the origin cannot be read from a file on their own, so
they are checked against the scanned box when it is given: every side of the
bounds must be within 10 % of the box (the phone's own acceptance test, section
7.4), the lowest point must sit on y = 0 and the footprint must be centred.
Without the box the validator still checks the base and the centring and
rejects anything larger than a RoomModel object can be.
"""
from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional, Sequence

import numpy as np

from .budget import MAX_EDGE_METRES, VALIDATOR_MAX_TRIANGLES, VALIDATOR_MIN_TRIANGLES
from .glb import GlbError, read_glb

REQUIRED_ATTRIBUTES = ("POSITION", "NORMAL", "TEXCOORD_0", "COLOR_0")
MAX_BYTES = 1024 * 1024  # GLB budget, section 7.4
EDGE_EPSILON = 1e-5  # float32 round-off on a 0.25 m edge
ORIGIN_TOLERANCE = 0.01  # metres
BOUNDS_TOLERANCE = 0.10  # share of the box side
BOUNDS_SLACK = 0.005  # metres, so very thin boxes are not held to sub-millimetre fits
MAX_OBJECT_SIDE = 6.0  # the largest box side a RoomModel may hold


@dataclass
class Violation:
    code: str
    message: str


@dataclass
class ValidationResult:
    violations: list[Violation] = field(default_factory=list)
    triangles: int = 0
    vertices: int = 0
    longest_edge: float = 0.0
    bounds_min: tuple[float, float, float] = (0.0, 0.0, 0.0)
    bounds_max: tuple[float, float, float] = (0.0, 0.0, 0.0)
    bytes: int = 0

    @property
    def ok(self) -> bool:
        return not self.violations

    @property
    def size(self) -> tuple[float, float, float]:
        return tuple(float(b - a) for a, b in zip(self.bounds_min, self.bounds_max))  # type: ignore[return-value]

    @property
    def codes(self) -> list[str]:
        return [v.code for v in self.violations]

    def add(self, code: str, message: str) -> None:
        self.violations.append(Violation(code, message))

    def to_dict(self) -> dict:
        return {
            "ok": self.ok,
            "triangles": self.triangles,
            "vertices": self.vertices,
            "longestEdge": round(self.longest_edge, 6),
            "boundsMin": [round(v, 6) for v in self.bounds_min],
            "boundsMax": [round(v, 6) for v in self.bounds_max],
            "size": [round(v, 6) for v in self.size],
            "bytes": self.bytes,
            "violations": [{"code": v.code, "message": v.message} for v in self.violations],
        }


def longest_edge(positions: np.ndarray, faces: np.ndarray) -> float:
    """Length of the longest triangle edge, in the units of ``positions``."""
    if len(faces) == 0:
        return 0.0
    p = np.asarray(positions, dtype=np.float64)
    a, b, c = p[faces[:, 0]], p[faces[:, 1]], p[faces[:, 2]]
    lengths = np.concatenate(
        [np.linalg.norm(b - a, axis=1), np.linalg.norm(c - b, axis=1), np.linalg.norm(a - c, axis=1)]
    )
    return float(lengths.max())


def _has_transform(node: dict) -> bool:
    if "matrix" in node:
        return not np.allclose(node["matrix"], np.eye(4).reshape(-1), atol=1e-6)
    return (
        not np.allclose(node.get("translation", [0, 0, 0]), [0, 0, 0], atol=1e-6)
        or not np.allclose(node.get("rotation", [0, 0, 0, 1]), [0, 0, 0, 1], atol=1e-6)
        or not np.allclose(node.get("scale", [1, 1, 1]), [1, 1, 1], atol=1e-6)
    )


def validate_glb(
    data: bytes,
    tri_target: Optional[int] = None,
    box: Optional[Sequence[float]] = None,
) -> ValidationResult:
    """Check ``data`` against the asset contract and return every violation found."""
    result = ValidationResult(bytes=len(data))
    try:
        glb = read_glb(data)
    except GlbError as exc:
        result.add("glb.malformed", str(exc))
        return result
    doc = glb.doc

    if len(data) > MAX_BYTES:
        result.add("glb.size", f"{len(data)} bytes; the budget is {MAX_BYTES}")
    if doc.get("extensionsRequired"):
        result.add("glb.extension", f"required extensions are not allowed: {doc['extensionsRequired']}")

    if doc.get("materials"):
        result.add("material.present", f"{len(doc['materials'])} material(s); the phone assigns the shared one")
    for key in ("textures", "images", "samplers"):
        if doc.get(key):
            result.add("texture.present", f"{len(doc[key])} entr(ies) under '{key}'; textures are not allowed")

    meshes = doc.get("meshes") or []
    nodes = doc.get("nodes") or []
    if len(meshes) != 1:
        result.add("mesh.count", f"{len(meshes)} meshes; exactly one is required")
        return result
    primitives = meshes[0].get("primitives") or []
    if len(primitives) != 1:
        result.add("mesh.count", f"{len(primitives)} primitives (material slots); exactly one is required")
        return result
    mesh_nodes = [n for n in nodes if "mesh" in n]
    if len(nodes) != 1 or len(mesh_nodes) != 1:
        result.add("mesh.count", f"{len(nodes)} nodes, {len(mesh_nodes)} with a mesh; exactly one node is required")
    elif _has_transform(mesh_nodes[0]):
        result.add("frame.transform", "the mesh node carries a transform; vertices must be in the box frame")
    for key in ("skins", "animations", "cameras"):
        if doc.get(key):
            result.add("mesh.count", f"'{key}' is not part of a furniture asset")

    prim = primitives[0]
    if "material" in prim:
        result.add("material.present", "the primitive references a material")
    if prim.get("mode", 4) != 4:
        result.add("mesh.mode", f"primitive mode {prim.get('mode')}; triangles (4) are required")
        return result
    attributes = prim.get("attributes") or {}
    missing = [a for a in REQUIRED_ATTRIBUTES if a not in attributes]
    if missing:
        result.add("attributes.missing", f"missing vertex attributes: {', '.join(missing)}")
    if "POSITION" not in attributes:
        return result

    try:
        positions = np.asarray(glb.accessor(attributes["POSITION"]), dtype=np.float64)
        if positions.ndim != 2 or positions.shape[1] != 3:
            raise GlbError("POSITION is not VEC3")
        if "indices" in prim:
            index = np.asarray(glb.accessor(prim["indices"]), dtype=np.int64).reshape(-1)
        else:
            index = np.arange(len(positions), dtype=np.int64)
        for name in REQUIRED_ATTRIBUTES[1:]:
            if name in attributes and len(glb.accessor(attributes[name])) != len(positions):
                raise GlbError(f"{name} and POSITION differ in length")
    except GlbError as exc:
        result.add("glb.malformed", str(exc))
        return result
    if len(index) % 3 != 0 or (len(index) and (index.min() < 0 or index.max() >= len(positions))):
        result.add("glb.malformed", "the index buffer does not describe triangles of this vertex buffer")
        return result
    if len(positions) == 0 or not np.isfinite(positions).all():
        result.add("glb.malformed", "positions are empty or not finite")
        return result
    faces = index.reshape(-1, 3)

    result.triangles = int(len(faces))
    result.vertices = int(len(positions))
    result.longest_edge = longest_edge(positions, faces)
    lo, hi = positions.min(axis=0), positions.max(axis=0)
    result.bounds_min = tuple(float(v) for v in lo)  # type: ignore[assignment]
    result.bounds_max = tuple(float(v) for v in hi)  # type: ignore[assignment]

    if not VALIDATOR_MIN_TRIANGLES <= result.triangles <= VALIDATOR_MAX_TRIANGLES:
        result.add(
            "triangles.range",
            f"{result.triangles} triangles; the contract allows {VALIDATOR_MIN_TRIANGLES} to {VALIDATOR_MAX_TRIANGLES}",
        )
    if tri_target is not None and result.triangles > tri_target:
        result.add("triangles.target", f"{result.triangles} triangles; the job's target is {tri_target}")
    if result.longest_edge > MAX_EDGE_METRES + EDGE_EPSILON:
        result.add("edge.long", f"longest edge {result.longest_edge:.4f} m; the limit is {MAX_EDGE_METRES} m")

    # Origin at the centre of the box's base, +Y up.
    centre_x, centre_z = (lo[0] + hi[0]) * 0.5, (lo[2] + hi[2]) * 0.5
    if abs(lo[1]) > ORIGIN_TOLERANCE:
        result.add("frame.origin", f"lowest point is at y = {lo[1]:.4f} m; the base must sit on y = 0")
    if abs(centre_x) > ORIGIN_TOLERANCE or abs(centre_z) > ORIGIN_TOLERANCE:
        result.add(
            "frame.origin",
            f"footprint centre is at ({centre_x:.4f}, {centre_z:.4f}) m; the origin must be the centre of the base",
        )

    size = hi - lo
    if box is not None:
        box = [float(v) for v in box]
        for axis, name in enumerate("XYZ"):
            allowed = box[axis] * BOUNDS_TOLERANCE + BOUNDS_SLACK
            if abs(size[axis] - box[axis]) > allowed:
                result.add(
                    "frame.bounds",
                    f"{name} extent {size[axis]:.4f} m against a box side of {box[axis]:.4f} m "
                    f"(more than {BOUNDS_TOLERANCE:.0%} off: wrong scale, units or up axis)",
                )
    elif size.max() > MAX_OBJECT_SIDE * (1.0 + BOUNDS_TOLERANCE):
        result.add("frame.units", f"largest extent {size.max():.2f} m; the mesh is not in metres")
    return result


def validate_file(path, tri_target: Optional[int] = None, box: Optional[Sequence[float]] = None) -> ValidationResult:
    return validate_glb(Path(path).read_bytes(), tri_target=tri_target, box=box)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="python -m forge.validator",
        description="Check GLB files against the ATLAS asset contract (Master Plan section 6.4).",
    )
    parser.add_argument("files", nargs="+", help="GLB files to check")
    parser.add_argument("--target", type=int, default=None, help="triangle target sent with the job")
    parser.add_argument("--box", type=float, nargs=3, metavar=("W", "H", "D"), default=None,
                        help="scanned box size in metres")
    parser.add_argument("--json", action="store_true", help="print one JSON object per file")
    args = parser.parse_args(argv)

    failed = 0
    for name in args.files:
        try:
            result = validate_file(name, tri_target=args.target, box=args.box)
        except OSError as exc:
            print(f"FAIL {name}: {exc}")
            failed += 1
            continue
        failed += 0 if result.ok else 1
        if args.json:
            print(json.dumps({"file": name, **result.to_dict()}))
            continue
        size = result.size
        print(
            f"{'PASS' if result.ok else 'FAIL'} {name}: {result.triangles} triangles, "
            f"longest edge {result.longest_edge:.4f} m, size {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f} m, "
            f"{result.bytes} bytes"
        )
        for violation in result.violations:
            print(f"  [{violation.code}] {violation.message}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
