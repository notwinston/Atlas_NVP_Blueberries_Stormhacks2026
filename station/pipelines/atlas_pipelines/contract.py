"""Readers and writers of the case contract (docs/case_contract.md, work package P2-D01).

The document is normative; this module and ``station/pipelines/csharp/Atlas.Cases`` implement it
and must produce identical bytes for the same content.

Contents
    canonical JSON      dumps_canonical / loads_json / write_json / read_json
    tube4d.bin          Tube4D, tube4d_to_bytes / tube4d_from_bytes / write_tube4d / read_tube4d,
                        prisms_to_tets, tube_from_phases, check_tube_layout
    table.json          Table, write_table / read_table
    manifest.json       Manifest, FileEntry, write_manifest / read_manifest,
                        build_manifest, verify_manifest
    frame.json          Frame (work file, not shipped), to_display / from_display
"""

from __future__ import annotations

import json
import math
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np

from . import labels
from .cache import sha256_file

# --------------------------------------------------------------------------------------------
# Constants fixed by the plan
# --------------------------------------------------------------------------------------------
M_DISPLAY = 15.0  #: display magnification M
LAMBDA_M = 1.5  #: lane length of one breath, metres
PHASES = 10  #: phases per breath; phase 10 repeats phase 0
TEMPLATE_TRIS = 600  #: template triangles => 18,000 tets

SCHEMA_CASE = "atlas.case/1"
SCHEMA_TABLE = "atlas.table/1"
SCHEMA_FRAME = "atlas.frame/1"
SCHEMA_TREE = "atlas.tree/2"
SCHEMA_GREEDY = "atlas.greedy/1"
SCHEMA_SKY = "atlas.sky/1"

OCC_SIZE = 64
SKY_WIDTH = 512
SKY_HEIGHT = 256


class ContractError(ValueError):
    """A file or value violates docs/case_contract.md."""


# --------------------------------------------------------------------------------------------
# Canonical JSON ("ATLAS canonical JSON", contract section 3)
# --------------------------------------------------------------------------------------------
FLOAT_DECIMALS = 6
_FLOAT_SCALE = 1_000_000
_MAX_MICRO = 2**53  # exclusive bound on |round(v * 1e6)|
_INT_MIN, _INT_MAX = -(2**63), 2**63 - 1

_ESCAPES = {'"': '\\"', "\\": "\\\\", "\b": "\\b", "\f": "\\f", "\n": "\\n", "\r": "\\r", "\t": "\\t"}


def format_float(v: float) -> str:
    """Canonical text of a float: q = round-half-even(v * 1e6) written as a fixed-point decimal
    with trailing zeros removed and at least one digit after the point ("15.0", "-0.25", "0.0")."""
    v = float(v)
    if not math.isfinite(v):
        raise ContractError("NaN and infinity cannot be written to canonical JSON")
    scaled = v * 1e6
    # Test the range before round(): v * 1e6 is infinite for |v| > about 1.8e302, and round(inf)
    # raises OverflowError, which is not the error a reader of this module catches.
    if not math.isfinite(scaled) or abs(scaled) >= _MAX_MICRO + 1:
        raise ContractError(f"float {v!r} is outside the canonical range (|v| < 2^53 / 1e6)")
    q = round(scaled)  # Python's round() on a float is round-half-to-even and returns an int
    if abs(q) >= _MAX_MICRO:
        raise ContractError(f"float {v!r} is outside the canonical range (|v| < 2^53 / 1e6)")
    sign = "-" if q < 0 else ""
    q = abs(q)
    whole, frac = divmod(q, _FLOAT_SCALE)
    digits = f"{frac:06d}".rstrip("0") or "0"
    return f"{sign}{whole}.{digits}"


def _format_string(s: str) -> str:
    out = ['"']
    for ch in s:
        esc = _ESCAPES.get(ch)
        if esc is not None:
            out.append(esc)
        elif ch < " ":
            out.append(f"\\u{ord(ch):04x}")
        elif 0xD800 <= ord(ch) <= 0xDFFF:
            raise ContractError("lone surrogate in string")
        else:
            out.append(ch)
    out.append('"')
    return "".join(out)


def _check_key(k: Any) -> str:
    if not isinstance(k, str):
        raise ContractError(f"object key must be a string, got {type(k).__name__}")
    if not k or any(not (" " <= c <= "~") for c in k):
        raise ContractError(f"object key must be non-empty printable ASCII: {k!r}")
    return k


def _emit(v: Any, out: list[str]) -> None:
    if v is None:
        out.append("null")
    elif isinstance(v, (bool, np.bool_)):
        out.append("true" if v else "false")
    elif isinstance(v, (int, np.integer)):
        i = int(v)
        if not _INT_MIN <= i <= _INT_MAX:
            raise ContractError(f"integer {i} outside the signed 64-bit range")
        out.append(str(i))
    elif isinstance(v, (float, np.floating)):
        out.append(format_float(float(v)))
    elif isinstance(v, str):
        out.append(_format_string(v))
    elif isinstance(v, np.ndarray):
        _emit(v.tolist(), out)
    elif isinstance(v, dict):
        out.append("{")
        first = True
        for k in sorted(_check_key(k) for k in v):
            if not first:
                out.append(",")
            first = False
            out.append(_format_string(k))
            out.append(":")
            _emit(v[k], out)
        out.append("}")
    elif isinstance(v, (list, tuple)):
        out.append("[")
        for i, item in enumerate(v):
            if i:
                out.append(",")
            _emit(item, out)
        out.append("]")
    else:
        raise ContractError(f"cannot write {type(v).__name__} to canonical JSON")


def dumps_canonical(obj: Any) -> bytes:
    """Canonical bytes of a JSON value: UTF-8, no whitespace, sorted ASCII keys, canonical numbers,
    one trailing LF. Python ``int`` is written as an integer, ``float`` always with a decimal point."""
    out: list[str] = []
    _emit(obj, out)
    out.append("\n")
    return "".join(out).encode("utf-8")


def _no_duplicates(pairs: list[tuple[str, Any]]) -> dict:
    d: dict = {}
    for k, v in pairs:
        if k in d:
            raise ContractError(f"duplicate object key {k!r}")
        d[k] = v
    return d


def _reject_constant(name: str):
    raise ContractError(f"{name} is not allowed in JSON")


def loads_json(data: bytes | str) -> Any:
    """Parse JSON. A number token with '.', 'e' or 'E' becomes float, otherwise int. Duplicate keys,
    NaN and Infinity are rejected. Accepts any valid JSON, not only the canonical form."""
    if isinstance(data, (bytes, bytearray)):
        if data[:3] == b"\xef\xbb\xbf":
            raise ContractError("a byte-order mark is not allowed")
        data = bytes(data).decode("utf-8")
    try:
        return json.loads(data, object_pairs_hook=_no_duplicates, parse_constant=_reject_constant)
    except json.JSONDecodeError as e:
        raise ContractError(f"invalid JSON: {e}") from e


def write_json(path: Path | str, obj: Any) -> bytes:
    """Write a JSON value in canonical form. Returns the bytes written."""
    data = dumps_canonical(obj)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(data)
    return data


def read_json(path: Path | str) -> Any:
    return loads_json(Path(path).read_bytes())


def is_canonical(data: bytes) -> bool:
    """True when ``data`` is exactly the canonical form of the value it encodes."""
    try:
        return dumps_canonical(loads_json(data)) == data
    except ContractError:
        return False


# -- typed access helpers ------------------------------------------------------------------------


def _need(obj: dict, key: str, what: str) -> Any:
    if not isinstance(obj, dict):
        raise ContractError(f"{what}: expected an object")
    if key not in obj:
        raise ContractError(f"{what}: missing key {key!r}")
    return obj[key]


def _as_float(v: Any, what: str) -> float:
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        raise ContractError(f"{what}: expected a number")
    v = float(v)
    if not math.isfinite(v):
        raise ContractError(f"{what}: not finite")
    return v


def _as_int(v: Any, what: str) -> int:
    if isinstance(v, bool) or not isinstance(v, int):
        raise ContractError(f"{what}: expected an integer")
    return v


def _as_str(v: Any, what: str) -> str:
    if not isinstance(v, str):
        raise ContractError(f"{what}: expected a string")
    return v


def _as_floats(v: Any, n: int, what: str) -> list[float]:
    if isinstance(v, np.ndarray):
        v = v.tolist()
    if not isinstance(v, (list, tuple)) or len(v) != n:
        raise ContractError(f"{what}: expected an array of {n} numbers")
    return [_as_float(x, what) for x in v]


def _as_float_rows(v: Any, rows: int, cols: int, what: str) -> list[list[float]]:
    if isinstance(v, np.ndarray):
        v = v.tolist()
    if not isinstance(v, (list, tuple)) or len(v) != rows:
        raise ContractError(f"{what}: expected {rows} rows")
    return [_as_floats(r, cols, f"{what}[{i}]") for i, r in enumerate(v)]


def _check_keys(obj: dict, required: Iterable[str], optional: Iterable[str], what: str) -> None:
    if not isinstance(obj, dict):
        raise ContractError(f"{what}: expected an object")
    required, optional = set(required), set(optional)
    missing = required - obj.keys()
    unknown = obj.keys() - required - optional
    if missing:
        raise ContractError(f"{what}: missing keys {sorted(missing)}")
    if unknown:
        raise ContractError(f"{what}: unknown keys {sorted(unknown)} (put additions under \"extra\")")


def _check_extra(obj: dict, what: str) -> None:
    """``extra`` is optional, but when the key is present its value must be an object: ``"extra": null``
    is rejected, as the C# reader does (CanonicalJson.AsObject). Leave the key out instead."""
    if "extra" in obj and not isinstance(obj["extra"], dict):
        raise ContractError(f"{what}: extra must be an object (omit the key when there is nothing to add)")


# --------------------------------------------------------------------------------------------
# tube4d.bin (contract section 4)
# --------------------------------------------------------------------------------------------
TUBE_MAGIC = b"ATL4"
TUBE_VERSION = 1
TUBE_HEADER = struct.Struct("<4sHIIHff")  # magic, version, nVerts, nTets, layers, M, Lambda
TUBE_HEADER_SIZE = 24
assert TUBE_HEADER.size == TUBE_HEADER_SIZE
MAX_LAYERS = 256  # slab id is a u8


@dataclass
class Tube4D:
    """A tetrahedral mesh in (x, y, z, tau).

    P      (nVerts, 4) float32: x, y, z, tau in metres, display frame (magnification M applied)
    T      (nTets, 4) int32: vertex indices
    slab   (nTets,) uint8: phase layer k of the prism the tet came from, 0 <= k < layers
    layers number of phase layers (10); the tube has layers + 1 vertex rings
    M      display magnification stored in the header (15)
    Lambda lane length of one breath in metres stored in the header (1.5)
    """

    P: np.ndarray
    T: np.ndarray
    slab: np.ndarray
    layers: int = PHASES
    M: float = M_DISPLAY
    Lambda: float = LAMBDA_M
    version: int = TUBE_VERSION

    def __post_init__(self) -> None:
        self.P = np.ascontiguousarray(self.P, dtype="<f4")
        self.T = np.ascontiguousarray(self.T, dtype="<i4")
        self.slab = np.ascontiguousarray(self.slab, dtype=np.uint8)

    @property
    def n_verts(self) -> int:
        return int(self.P.shape[0])

    @property
    def n_tets(self) -> int:
        return int(self.T.shape[0])

    def validate(self) -> None:
        if self.version != TUBE_VERSION:
            raise ContractError(f"tube4d: unsupported version {self.version}")
        if self.P.ndim != 2 or self.P.shape[1] != 4:
            raise ContractError("tube4d: P must be n x 4")
        if self.T.ndim != 2 or self.T.shape[1] != 4:
            raise ContractError("tube4d: T must be m x 4")
        if self.slab.shape != (self.n_tets,):
            raise ContractError("tube4d: slab must have one entry per tet")
        if not 1 <= int(self.layers) <= MAX_LAYERS:
            raise ContractError(f"tube4d: layers must be 1..{MAX_LAYERS}")
        if not np.isfinite(self.P).all():
            raise ContractError("tube4d: P holds NaN or infinity")
        if not (math.isfinite(self.M) and math.isfinite(self.Lambda) and self.M > 0 and self.Lambda > 0):
            raise ContractError("tube4d: M and Lambda must be finite and positive")
        if self.n_tets and (int(self.T.min()) < 0 or int(self.T.max()) >= self.n_verts):
            raise ContractError("tube4d: tet index out of range")
        if self.n_tets and int(self.slab.max()) >= int(self.layers):
            raise ContractError("tube4d: slab id >= layers")


def tube4d_to_bytes(tube: Tube4D) -> bytes:
    tube.validate()
    header = TUBE_HEADER.pack(TUBE_MAGIC, tube.version, tube.n_verts, tube.n_tets, int(tube.layers),
                              float(tube.M), float(tube.Lambda))
    return b"".join((header, tube.P.tobytes(order="C"), tube.T.tobytes(order="C"), tube.slab.tobytes()))


def tube4d_from_bytes(data: bytes) -> Tube4D:
    if len(data) < TUBE_HEADER_SIZE:
        raise ContractError("tube4d: file shorter than the 24-byte header")
    magic, version, n, m, layers, M, lam = TUBE_HEADER.unpack_from(data, 0)
    if magic != TUBE_MAGIC:
        raise ContractError(f"tube4d: bad magic {magic!r}")
    if version != TUBE_VERSION:
        raise ContractError(f"tube4d: unsupported version {version}")
    expect = TUBE_HEADER_SIZE + 16 * n + 17 * m
    if len(data) != expect:
        raise ContractError(f"tube4d: file is {len(data)} bytes, header implies {expect}")
    o = TUBE_HEADER_SIZE
    P = np.frombuffer(data, dtype="<f4", count=4 * n, offset=o).reshape(n, 4).copy()
    o += 16 * n
    T = np.frombuffer(data, dtype="<i4", count=4 * m, offset=o).reshape(m, 4).copy()
    o += 16 * m
    slab = np.frombuffer(data, dtype=np.uint8, count=m, offset=o).copy()
    tube = Tube4D(P=P, T=T, slab=slab, layers=layers, M=M, Lambda=lam, version=version)
    tube.validate()
    return tube


def write_tube4d(path: Path | str, tube: Tube4D) -> bytes:
    data = tube4d_to_bytes(tube)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(data)
    return data


def read_tube4d(path: Path | str) -> Tube4D:
    return tube4d_from_bytes(Path(path).read_bytes())


def prisms_to_tets(faces: np.ndarray, n_template_verts: int, layers: int = PHASES) -> tuple[np.ndarray, np.ndarray]:
    """Master Plan A.8 / R2 D.3: each template triangle carried from layer k to k + 1 is a prism,
    split into 3 tets with index-sorted diagonals. Vertex i of ring k has index k * nV + i.

    Returns (T, slab): T is (3 * nF * layers, 4) int32 in slab-major order, tet index
    3 * (k * nF + f) + j; slab[t] = k.
    """
    F = np.sort(np.asarray(faces, dtype=np.int64).reshape(-1, 3), axis=1)  # a < b < c
    if F.size and (F.min() < 0 or F.max() >= n_template_verts):
        raise ContractError("prisms_to_tets: face index out of range")
    a, b, c = F[:, 0], F[:, 1], F[:, 2]
    nF = F.shape[0]
    T = np.empty((layers, nF, 3, 4), dtype=np.int64)
    for k in range(layers):
        lo, hi = k * n_template_verts, (k + 1) * n_template_verts
        T[k, :, 0] = np.stack([a + lo, b + lo, c + lo, c + hi], axis=1)
        T[k, :, 1] = np.stack([a + lo, b + lo, b + hi, c + hi], axis=1)
        T[k, :, 2] = np.stack([a + lo, a + hi, b + hi, c + hi], axis=1)
    slab = np.repeat(np.arange(layers, dtype=np.uint8), nF * 3)
    return T.reshape(-1, 4).astype("<i4"), slab


def tube_from_phases(phase_verts_display_m: Sequence[np.ndarray], faces: np.ndarray,
                     M: float = M_DISPLAY, Lambda: float = LAMBDA_M) -> Tube4D:
    """Assemble a tube from per-phase vertex arrays that are ALREADY in the display frame
    (metres, see ``to_display``). ``phase_verts_display_m[k]`` is (nV, 3) for k = 0 .. layers - 1;
    ring ``layers`` repeats ring 0 with tau = Lambda so the tube closes."""
    layers = len(phase_verts_display_m)
    V = [np.asarray(v, dtype=np.float64) for v in phase_verts_display_m]
    nV = V[0].shape[0]
    if any(v.shape != (nV, 3) for v in V):
        raise ContractError("tube_from_phases: every phase needs the same (nV, 3) vertices")
    P = np.empty(((layers + 1) * nV, 4), dtype=np.float64)
    for k in range(layers + 1):
        P[k * nV:(k + 1) * nV, :3] = V[k % layers]
        P[k * nV:(k + 1) * nV, 3] = Lambda * k / layers
    T, slab = prisms_to_tets(faces, nV, layers)
    return Tube4D(P=P, T=T, slab=slab, layers=layers, M=M, Lambda=Lambda)


def check_tube_layout(tube: Tube4D, n_template_verts: int) -> None:
    """Raise ContractError unless the tube follows the ring layout of contract section 4.3."""
    L, nV = int(tube.layers), int(n_template_verts)
    if tube.n_verts != (L + 1) * nV:
        raise ContractError(f"tube4d: nVerts {tube.n_verts} != (layers + 1) * {nV}")
    tau = tube.P[:, 3].reshape(L + 1, nV)
    want = (np.float64(tube.Lambda) * np.arange(L + 1) / L).astype("<f4")
    if not np.array_equal(tau, np.broadcast_to(want[:, None], tau.shape)):
        raise ContractError("tube4d: tau of ring k must be float32(Lambda * k / layers)")
    if not np.array_equal(tube.P[:nV, :3], tube.P[L * nV:, :3]):
        raise ContractError("tube4d: the last ring must repeat ring 0")
    if tube.n_tets % (3 * L):
        raise ContractError("tube4d: nTets must be 3 * nFaces * layers")
    per = tube.n_tets // L
    if not np.array_equal(tube.slab, np.repeat(np.arange(L, dtype=np.uint8), per)):
        raise ContractError("tube4d: tets must be stored slab by slab")
    ring = tube.T // nV
    k = tube.slab.astype(np.int64)[:, None]
    if not (((ring == k) | (ring == k + 1)).all() and (ring[:, 0] == k[:, 0]).all() and (ring[:, 3] == k[:, 0] + 1).all()):
        raise ContractError("tube4d: a tet of slab k must join ring k to ring k + 1")


# --------------------------------------------------------------------------------------------
# frame.json (work file; contract section 2)
# --------------------------------------------------------------------------------------------
@dataclass
class Frame:
    """The case frame: display = M * (patient_mm - origin_mm) / 1000, axes unchanged (LPS)."""

    id: str
    origin_mm: list[float]
    M: float = M_DISPLAY
    Lambda: float = LAMBDA_M

    def to_obj(self) -> dict:
        return {"schema": SCHEMA_FRAME, "id": self.id, "origin_mm": [float(x) for x in self.origin_mm],
                "M": float(self.M), "Lambda": float(self.Lambda)}

    @staticmethod
    def from_obj(o: dict) -> "Frame":
        _check_keys(o, ("schema", "id", "origin_mm", "M", "Lambda"), (), "frame.json")
        if o["schema"] != SCHEMA_FRAME:
            raise ContractError(f"frame.json: schema must be {SCHEMA_FRAME!r}")
        return Frame(id=_as_str(o["id"], "id"), origin_mm=_as_floats(o["origin_mm"], 3, "origin_mm"),
                     M=_as_float(o["M"], "M"), Lambda=_as_float(o["Lambda"], "Lambda"))


def write_frame(path: Path | str, frame: Frame) -> bytes:
    return write_json(path, frame.to_obj())


def read_frame(path: Path | str) -> Frame:
    return Frame.from_obj(read_json(path))


def to_display(points_mm: np.ndarray, origin_mm: Sequence[float], M: float = M_DISPLAY) -> np.ndarray:
    """Patient LPS millimetres -> display metres: M * (p - origin) / 1000. Axes are not permuted."""
    return (np.asarray(points_mm, dtype=np.float64) - np.asarray(origin_mm, dtype=np.float64)) * (M / 1000.0)


def from_display(points_m: np.ndarray, origin_mm: Sequence[float], M: float = M_DISPLAY) -> np.ndarray:
    """Display metres -> patient LPS millimetres."""
    return np.asarray(points_m, dtype=np.float64) * (1000.0 / M) + np.asarray(origin_mm, dtype=np.float64)


# --------------------------------------------------------------------------------------------
# table.json (contract section 5)
# --------------------------------------------------------------------------------------------
_TABLE_REQUIRED = ("schema", "id", "phases", "substeps", "gtv_cm3", "itv_cm3", "core_cm3", "gating_cm3",
                   "centroid_mm", "centroid_range_mm", "argmax_rho_mm", "max_rho", "origin_mm", "occ")
_OCC_REQUIRED = ("dims", "bbox_min_mm", "bbox_max_mm")


@dataclass
class Table:
    """table.json. Volumes in cm3, lengths in patient LPS millimetres, rho in [0, 1].

    gating_cm3[s][l] is the volume of the union of the l + 1 consecutive phases s, s + 1, ...,
    s + l (mod 10): l = 0 is the single phase s, l = 9 is all ten phases.
    """

    id: str
    gtv_cm3: list[float]
    itv_cm3: float
    core_cm3: float
    gating_cm3: list[list[float]]
    centroid_mm: list[list[float]]
    centroid_range_mm: list[float]
    argmax_rho_mm: list[float]
    max_rho: float
    origin_mm: list[float]
    occ_bbox_min_mm: list[float]
    occ_bbox_max_mm: list[float]
    occ_dims: list[int] = field(default_factory=lambda: [OCC_SIZE] * 3)
    substeps: int = 4
    phases: int = PHASES
    extra: dict | None = None

    def validate(self) -> None:
        n = self.phases
        if n != PHASES:
            raise ContractError(f"table.json: phases must be {PHASES}")
        self.gtv_cm3 = _as_floats(self.gtv_cm3, n, "gtv_cm3")
        self.gating_cm3 = _as_float_rows(self.gating_cm3, n, n, "gating_cm3")
        self.centroid_mm = _as_float_rows(self.centroid_mm, n, 3, "centroid_mm")
        self.centroid_range_mm = _as_floats(self.centroid_range_mm, 3, "centroid_range_mm")
        self.argmax_rho_mm = _as_floats(self.argmax_rho_mm, 3, "argmax_rho_mm")
        self.origin_mm = _as_floats(self.origin_mm, 3, "origin_mm")
        self.occ_bbox_min_mm = _as_floats(self.occ_bbox_min_mm, 3, "occ.bbox_min_mm")
        self.occ_bbox_max_mm = _as_floats(self.occ_bbox_max_mm, 3, "occ.bbox_max_mm")
        self.itv_cm3 = _as_float(self.itv_cm3, "itv_cm3")
        self.core_cm3 = _as_float(self.core_cm3, "core_cm3")
        self.max_rho = _as_float(self.max_rho, "max_rho")
        self.substeps = _as_int(self.substeps, "substeps")
        self.occ_dims = [_as_int(int(d) if isinstance(d, np.integer) else d, "occ.dims") for d in self.occ_dims]
        if len(self.occ_dims) != 3 or any(d <= 0 for d in self.occ_dims):
            raise ContractError("table.json: occ.dims must be 3 positive integers")
        if not 0.0 <= self.max_rho <= 1.0:
            raise ContractError("table.json: max_rho must be in [0, 1]")
        if self.substeps < 0:
            raise ContractError("table.json: substeps must be >= 0")
        if self.extra is not None and not isinstance(self.extra, dict):
            raise ContractError("table.json: extra must be an object")
        _as_str(self.id, "id")

    def to_obj(self) -> dict:
        self.validate()
        o = {
            "schema": SCHEMA_TABLE,
            "id": self.id,
            "phases": self.phases,
            "substeps": self.substeps,
            "gtv_cm3": self.gtv_cm3,
            "itv_cm3": self.itv_cm3,
            "core_cm3": self.core_cm3,
            "gating_cm3": self.gating_cm3,
            "centroid_mm": self.centroid_mm,
            "centroid_range_mm": self.centroid_range_mm,
            "argmax_rho_mm": self.argmax_rho_mm,
            "max_rho": self.max_rho,
            "origin_mm": self.origin_mm,
            "occ": {"dims": self.occ_dims, "bbox_min_mm": self.occ_bbox_min_mm, "bbox_max_mm": self.occ_bbox_max_mm},
        }
        if self.extra is not None:
            o["extra"] = self.extra
        return o

    @staticmethod
    def from_obj(o: dict) -> "Table":
        _check_keys(o, _TABLE_REQUIRED, ("extra",), "table.json")
        _check_extra(o, "table.json")
        if o["schema"] != SCHEMA_TABLE:
            raise ContractError(f"table.json: schema must be {SCHEMA_TABLE!r}")
        occ = o["occ"]
        _check_keys(occ, _OCC_REQUIRED, (), "table.json occ")
        if not isinstance(occ["dims"], list):
            raise ContractError("table.json: occ.dims must be an array")
        t = Table(
            id=_as_str(o["id"], "id"),
            phases=_as_int(o["phases"], "phases"),
            substeps=_as_int(o["substeps"], "substeps"),
            gtv_cm3=o["gtv_cm3"],
            itv_cm3=o["itv_cm3"],
            core_cm3=o["core_cm3"],
            gating_cm3=o["gating_cm3"],
            centroid_mm=o["centroid_mm"],
            centroid_range_mm=o["centroid_range_mm"],
            argmax_rho_mm=o["argmax_rho_mm"],
            max_rho=o["max_rho"],
            origin_mm=o["origin_mm"],
            occ_dims=occ["dims"],
            occ_bbox_min_mm=occ["bbox_min_mm"],
            occ_bbox_max_mm=occ["bbox_max_mm"],
            extra=o.get("extra"),
        )
        t.validate()
        return t


def write_table(path: Path | str, table: Table) -> bytes:
    return write_json(path, table.to_obj())


def read_table(path: Path | str) -> Table:
    return Table.from_obj(read_json(path))


# --------------------------------------------------------------------------------------------
# manifest.json (contract section 6)
# --------------------------------------------------------------------------------------------
_MANIFEST_REQUIRED = ("schema", "id", "kind", "label", "footer", "labels", "licence", "credit", "citations",
                      "M", "Lambda", "origin_mm", "files")
_FILE_REQUIRED = ("path", "sha256", "size")
MANIFEST_NAME = "manifest.json"
_HEX = set("0123456789abcdef")


def _check_rel_path(p: str) -> str:
    if (not isinstance(p, str) or not p or p.startswith("/") or "\\" in p or p == MANIFEST_NAME
            or any(part in ("", ".", "..") for part in p.split("/")) or any(not (" " <= c <= "~") for c in p)):
        raise ContractError(f"manifest.json: bad file path {p!r}")
    return p


@dataclass
class FileEntry:
    path: str  #: relative to the package directory, '/' separators
    sha256: str  #: 64 lower-case hex digits
    size: int  #: bytes


@dataclass
class Manifest:
    """manifest.json, schema atlas.case/1."""

    id: str
    kind: str
    label: str
    labels: dict[str, str]
    licence: str
    credit: str
    citations: list[str]
    origin_mm: list[float]
    files: list[FileEntry]
    footer: str = labels.FOOTER
    M: float = M_DISPLAY
    Lambda: float = LAMBDA_M
    extra: dict | None = None

    def validate(self) -> None:
        if self.kind not in labels.KINDS:
            raise ContractError(f"manifest.json: kind must be one of {labels.KINDS}")
        for name in ("id", "label", "footer", "licence", "credit"):
            _as_str(getattr(self, name), name)
        self.M = _as_float(self.M, "M")
        self.Lambda = _as_float(self.Lambda, "Lambda")
        self.origin_mm = _as_floats(self.origin_mm, 3, "origin_mm")
        if not isinstance(self.citations, (list, tuple)):
            raise ContractError("manifest.json: citations must be an array of strings")
        self.citations = [_as_str(c, "citations") for c in self.citations]
        seen = set()
        for f in self.files:
            _check_rel_path(f.path)
            if f.path in seen:
                raise ContractError(f"manifest.json: duplicate file {f.path!r}")
            seen.add(f.path)
            if not (isinstance(f.sha256, str) and len(f.sha256) == 64 and set(f.sha256) <= _HEX):
                raise ContractError(f"manifest.json: bad sha256 for {f.path!r}")
            if _as_int(f.size, "size") < 0:
                raise ContractError(f"manifest.json: negative size for {f.path!r}")
        if not isinstance(self.labels, dict) or set(self.labels) != seen:
            raise ContractError("manifest.json: labels must have exactly one entry per file")
        for k, v in self.labels.items():
            _as_str(v, f"labels[{k!r}]")
        if self.extra is not None and not isinstance(self.extra, dict):
            raise ContractError("manifest.json: extra must be an object")

    def to_obj(self) -> dict:
        self.validate()
        o = {
            "schema": SCHEMA_CASE,
            "id": self.id,
            "kind": self.kind,
            "label": self.label,
            "footer": self.footer,
            "labels": dict(self.labels),
            "licence": self.licence,
            "credit": self.credit,
            "citations": list(self.citations),
            "M": self.M,
            "Lambda": self.Lambda,
            "origin_mm": self.origin_mm,
            "files": [{"path": f.path, "sha256": f.sha256, "size": f.size}
                      for f in sorted(self.files, key=lambda f: f.path)],
        }
        if self.extra is not None:
            o["extra"] = self.extra
        return o

    @staticmethod
    def from_obj(o: dict) -> "Manifest":
        _check_keys(o, _MANIFEST_REQUIRED, ("extra",), "manifest.json")
        _check_extra(o, "manifest.json")
        if o["schema"] != SCHEMA_CASE:
            raise ContractError(f"manifest.json: schema must be {SCHEMA_CASE!r}")
        if not isinstance(o["files"], list):
            raise ContractError("manifest.json: files must be an array")
        files = []
        for f in o["files"]:
            _check_keys(f, _FILE_REQUIRED, (), "manifest.json files[]")
            files.append(FileEntry(path=_as_str(f["path"], "path"), sha256=_as_str(f["sha256"], "sha256"),
                                   size=_as_int(f["size"], "size")))
        m = Manifest(
            id=_as_str(o["id"], "id"), kind=_as_str(o["kind"], "kind"), label=_as_str(o["label"], "label"),
            footer=_as_str(o["footer"], "footer"), labels=o["labels"], licence=_as_str(o["licence"], "licence"),
            credit=_as_str(o["credit"], "credit"), citations=o["citations"], M=o["M"], Lambda=o["Lambda"],
            origin_mm=o["origin_mm"], files=files, extra=o.get("extra"),
        )
        m.validate()
        return m


def write_manifest(path: Path | str, manifest: Manifest) -> bytes:
    return write_json(path, manifest.to_obj())


def read_manifest(path: Path | str) -> Manifest:
    return Manifest.from_obj(read_json(path))


def package_files(case_dir: Path | str) -> list[str]:
    """Relative POSIX paths of every package file except manifest.json, sorted. Files and
    directories whose name starts with '.' are not part of a package."""
    case_dir = Path(case_dir)
    rels = []
    for p in case_dir.rglob("*"):
        if not p.is_file():
            continue
        rel = p.relative_to(case_dir)
        if any(part.startswith(".") for part in rel.parts) or rel.as_posix() == MANIFEST_NAME:
            continue
        rels.append(rel.as_posix())
    return sorted(rels)


def build_manifest(case_dir: Path | str, case_id: str, kind: str, origin_mm: Sequence[float],
                   M: float = M_DISPLAY, Lambda: float = LAMBDA_M, extra: dict | None = None) -> Manifest:
    """Hash every file of a package directory and fill in the label, licence and credit strings
    from atlas_pipelines.labels. Write the result with write_manifest(case_dir / "manifest.json", m)."""
    case_dir = Path(case_dir)
    rels = package_files(case_dir)
    files = [FileEntry(path=r, sha256=sha256_file(case_dir / r), size=(case_dir / r).stat().st_size) for r in rels]
    return Manifest(
        id=case_id, kind=kind, label=labels.case_label(kind), footer=labels.FOOTER,
        labels={r: labels.label_for(kind, r) for r in rels}, licence=labels.licence(kind),
        credit=labels.credit(kind), citations=list(labels.citations(kind)), M=M, Lambda=Lambda,
        origin_mm=[float(x) for x in origin_mm], files=files, extra=extra,
    )


def verify_manifest(case_dir: Path | str) -> list[str]:
    """The hash check of P2-D09. Returns a list of problems; empty means every file listed in
    manifest.json exists with the recorded size and SHA-256 and no unlisted file is present."""
    case_dir = Path(case_dir)
    problems: list[str] = []
    try:
        m = read_manifest(case_dir / MANIFEST_NAME)
    except (OSError, ContractError) as e:
        return [f"manifest.json: {e}"]
    listed = set()
    for f in m.files:
        listed.add(f.path)
        p = case_dir / f.path
        if not p.is_file():
            problems.append(f"{f.path}: missing")
            continue
        size = p.stat().st_size
        if size != f.size:
            problems.append(f"{f.path}: size {size}, manifest says {f.size}")
        elif sha256_file(p) != f.sha256:
            problems.append(f"{f.path}: sha256 mismatch")
    for r in package_files(case_dir):
        if r not in listed:
            problems.append(f"{r}: present but not listed in manifest.json")
    return problems
