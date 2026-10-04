"""Input-hash caching for pipeline steps ("every step is idempotent and cached by input hash").

A step's *key* is the SHA-256 of: a code version string, its parameters, and the contents of its
input files. After the work is done a *stamp* (JSON) records the key and the SHA-256 and size of
every output. On the next run the work is skipped when the key is unchanged and every output
still exists with the recorded hash.

Usage in a step module::

    from atlas_pipelines import cache, paths

    CODE_VERSION = "shells/1"          # bump whenever the step's code changes its outputs

    def run(args):
        case = paths.case_dir(args.case, args.out, create=True)
        outputs = [case / "itv.glb", case / "core.glb", case / "table.json"]
        ran = cache.run_cached(
            stamp=paths.stamp_path(args.case, NAME, args.out),
            code_version=CODE_VERSION,
            params={"substeps": 4, "case": args.case},      # JSON-able values only
            inputs=[case / "tube4d.bin", masks_dir],        # files or directories (hashed by content)
            outputs=outputs,
            work=lambda: build(case),                       # must write every path in `outputs`
            force=args.force,
        )
        print("built" if ran else "cached")
        return 0

or step by step, when the outputs are only known after the work::

    sc = cache.StepCache(paths.stamp_path(args.case, NAME, args.out))
    key = sc.key(CODE_VERSION, params, inputs)
    if not args.force and sc.is_fresh(key):      # checks the outputs recorded in the stamp
        return 0
    outputs = build(...)
    sc.commit(key, outputs)

Notes
- Stamps live outside the package, in ``<out>/work/<id>/stamps/<step>.json`` (paths.stamp_path), so
  that ``<out>/cases/<id>/`` holds package files only.
- Output paths are stored relative to the stamp's ``root`` (default: the stamp's directory), with
  ``..`` segments when the output lies outside it (``../../../cases/<id>/chest.glb`` for a package
  file). So an ``--out`` tree that is moved or copied as a whole keeps fresh stamps: the stamp and
  its outputs keep their relative position. An output that shares no directory with the root (only
  the filesystem root, or another drive) is stored absolute. Stamps written before this rule hold
  absolute paths for outputs outside the root; they are still read and stay fresh in place.
- A directory given as an input is hashed recursively (sorted relative paths + contents); a
  directory given as an output likewise.
- Hashing reads the files: about 1 GB per 2 s. Large inputs (the DICOM cache) can instead be
  represented by a small digest file that the producing step writes.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
from typing import Any, Callable, Iterable

STAMP_SCHEMA = "atlas.stamp/1"
_CHUNK = 1 << 20


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path | str) -> str:
    """SHA-256 of a file's contents, lower-case hex."""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            block = f.read(_CHUNK)
            if not block:
                break
            h.update(block)
    return h.hexdigest()


def _iter_tree(root: Path) -> list[Path]:
    return sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: p.relative_to(root).as_posix())


def sha256_path(path: Path | str) -> str:
    """SHA-256 of a file, or of a directory tree (relative POSIX paths and contents, sorted)."""
    path = Path(path)
    if path.is_dir():
        h = hashlib.sha256()
        for p in _iter_tree(path):
            rel = p.relative_to(path).as_posix().encode("utf-8")
            h.update(len(rel).to_bytes(4, "little"))
            h.update(rel)
            h.update(bytes.fromhex(sha256_file(p)))
        return h.hexdigest()
    return sha256_file(path)


def _path_size(path: Path) -> int:
    if path.is_dir():
        return sum(p.stat().st_size for p in _iter_tree(path))
    return path.stat().st_size


def _canonical_params(params: Any) -> str:
    try:
        return json.dumps(params, sort_keys=True, separators=(",", ":"), ensure_ascii=True, allow_nan=False)
    except (TypeError, ValueError) as e:
        raise TypeError(f"cache params must be JSON-able (str, int, float, bool, None, list, dict): {e}") from e


def compute_key(code_version: str, params: Any = None, inputs: Iterable[Path | str] = ()) -> str:
    """The cache key: SHA-256 over code version, parameters and input contents.

    Inputs are identified by their position in ``inputs`` and their base name, not by their
    absolute path, so the key itself does not change when the ``--out`` / ``--cache`` directory is
    moved (as long as the step puts no absolute path into ``params``). Whether the stamp is still
    fresh after a move also depends on how its outputs were recorded: see ``StepCache._rel``.
    A missing input raises FileNotFoundError.
    """
    h = hashlib.sha256()
    h.update(b"atlas.cachekey/1\n")
    h.update(code_version.encode("utf-8") + b"\n")
    h.update(_canonical_params(params).encode("utf-8") + b"\n")
    for i, p in enumerate(inputs):
        p = Path(p)
        if not p.exists():
            raise FileNotFoundError(f"cache input missing: {p}")
        h.update(f"{i}:{p.name}:{sha256_path(p)}\n".encode("utf-8"))
    return h.hexdigest()


class StepCache:
    """One stamp file guarding one unit of work."""

    def __init__(self, stamp: Path | str, root: Path | str | None = None):
        self.stamp = Path(stamp)
        self.root = Path(root) if root is not None else self.stamp.parent

    # -- key -----------------------------------------------------------------------------
    @staticmethod
    def key(code_version: str, params: Any = None, inputs: Iterable[Path | str] = ()) -> str:
        return compute_key(code_version, params, inputs)

    # -- stamp ---------------------------------------------------------------------------
    def _rel(self, path: Path) -> str:
        """How an output is named in the stamp: relative to ``root`` (``..`` segments allowed) when
        the two share a directory other than the filesystem root, else the absolute path."""
        path = Path(path).resolve()
        root = self.root.resolve()
        try:
            common = Path(os.path.commonpath([path, root]))
        except ValueError:  # different drives
            return path.as_posix()
        if common == Path(common.anchor):
            return path.as_posix()
        return Path(os.path.relpath(path, root)).as_posix()

    def _recorded_name(self, path: Path, recorded: dict) -> str | None:
        """The key under which ``path`` is recorded: its relative name, or its absolute path (stamps
        written before outputs outside the root were stored relative). None when it is not recorded."""
        rel = self._rel(path)
        if rel in recorded:
            return rel
        absolute = Path(path).resolve().as_posix()
        return absolute if absolute in recorded else None

    def _abs(self, rel: str) -> Path:
        p = Path(rel)
        return p if p.is_absolute() else self.root / p

    def load(self) -> dict | None:
        try:
            with open(self.stamp, "r", encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError):
            return None
        if not isinstance(data, dict) or data.get("schema") != STAMP_SCHEMA:
            return None
        return data

    def recorded_outputs(self) -> list[Path]:
        data = self.load()
        return [self._abs(r) for r in data["outputs"]] if data else []

    def is_fresh(self, key: str, outputs: Iterable[Path | str] | None = None) -> bool:
        """True when the stamp has this key and every output exists with the recorded hash.

        ``outputs``: when given, each of these must also be recorded in the stamp (so adding a
        new output to a step invalidates an old stamp). When None, the stamp's own list is used.
        """
        data = self.load()
        if data is None or data.get("key") != key:
            return False
        recorded: dict = data.get("outputs", {})
        if outputs is not None:
            for p in outputs:
                if self._recorded_name(Path(p), recorded) is None:
                    return False
        for rel, meta in recorded.items():
            p = self._abs(rel)
            if not p.exists():
                return False
            if _path_size(p) != meta.get("size"):
                return False
            if sha256_path(p) != meta.get("sha256"):
                return False
        return True

    def commit(self, key: str, outputs: Iterable[Path | str]) -> None:
        """Record the key and the current hash of every output. Raises if an output is missing."""
        rec = {}
        for p in outputs:
            p = Path(p)
            if not p.exists():
                raise FileNotFoundError(f"step did not write its declared output: {p}")
            rec[self._rel(p)] = {"sha256": sha256_path(p), "size": _path_size(p)}
        data = {"schema": STAMP_SCHEMA, "key": key, "outputs": rec}
        self.stamp.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.stamp.with_name(self.stamp.name + f".tmp{os.getpid()}")
        with open(tmp, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, sort_keys=True, indent=1)
            f.write("\n")
        os.replace(tmp, self.stamp)

    def invalidate(self) -> None:
        try:
            self.stamp.unlink()
        except FileNotFoundError:
            pass


def run_cached(
    stamp: Path | str,
    code_version: str,
    params: Any,
    inputs: Iterable[Path | str],
    outputs: Iterable[Path | str],
    work: Callable[[], Any],
    force: bool = False,
    root: Path | str | None = None,
) -> bool:
    """Run ``work()`` unless the stamp says the outputs are up to date. Returns True when it ran."""
    inputs = [Path(p) for p in inputs]
    outputs = [Path(p) for p in outputs]
    sc = StepCache(stamp, root=root)
    key = sc.key(code_version, params, inputs)
    if not force and sc.is_fresh(key, outputs):
        return False
    sc.invalidate()
    work()
    sc.commit(key, outputs)
    return True
