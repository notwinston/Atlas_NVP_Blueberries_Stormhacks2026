"""Download the TCIA 4D-Lung study of the real case into the DICOM cache (10 CT + 10 RTSTRUCT series).

Master Plan 10.2.4 step 1, work package P2-D03. Source: TCIA NBIA REST, collection "4D-Lung", patient
101_HM10395, the study UID of R2 C.2. Licence CC BY 3.0 (Hugo et al. 2016). The DICOM is never committed.

    atlas case fetch [--case lung101] [--jobs 4] [--verify] [--force]

Result, under ``<cache>/tcia/101_HM10395/``::

    index.json                       schema "atlas.tcia_index/1":
        collection, patient_id, study_uid, licence, licence_url, source,
        counts {"CT": 10, "RTSTRUCT": 10},
        phases [10]: {phase, percent, ct, rtstruct, ct_instances, rtstruct_instances, description}
                     (ct / rtstruct are SeriesInstanceUIDs = directory names; phase k is the 10k % bin)
        series {uid: {modality, phase, instances, bytes, sha256}}   sha256 = digest of that series' file list
    <SeriesInstanceUID>/*.dcm        the instances
    <SeriesInstanceUID>/series.json  {uid, modality, description, phase, files: [{name, size, sha256}]}

``index.json`` is byte-stable for unchanged data, so later steps use it as their cache input instead of
hashing 0.8 GB of DICOM.

Behaviour
- Idempotent: a series whose ``series.json`` matches the files on disk (names and sizes; with ``--verify``
  also SHA-256) is not downloaded again. When all 20 are present and ``index.json`` exists, the step makes
  no network request at all.
- Resumable: series are independent, so an interrupted run continues with the missing ones. Inside a series
  the zip is written to ``.download.zip.part`` and continued with an HTTP Range request when the server
  honours it (otherwise it restarts that one series).
- Retried: every request up to 5 times with a growing pause.
- Verified: zip integrity (CRC of every member), the instance count against ``ImageCount`` of ``getSeries``,
  SeriesInstanceUID and Modality of every instance, exactly one CT and one RTSTRUCT per phase 0..90 %
  (from SeriesDescription "Gated, NN%"), and each RTSTRUCT references the CT series of its own phase.
- At most 4 parallel downloads.

Python API: ``ensure(cache, case="lung101", jobs=4, force=False, verify=False) -> FetchResult``.
"""

from __future__ import annotations

import concurrent.futures
import hashlib
import json
import os
import shutil
import time
import zipfile
import zlib
from dataclasses import dataclass, field
from pathlib import Path

from .. import cache as cachelib
from .. import dicomio, paths

NAME = "fetch"
HELP = "download the TCIA 4D-Lung study of patient 101_HM10395 (10 CT + 10 RTSTRUCT series, about 0.8 GB)"

API = "https://services.cancerimagingarchive.net/nbia-api/services/v1"
EXPECTED = {"CT": 10, "RTSTRUCT": 10}
MAX_JOBS = 4
RETRIES = 5
TIMEOUT_S = 120
_PART = ".download.zip.part"


def add_arguments(parser) -> None:
    parser.add_argument("--jobs", type=int, default=MAX_JOBS, help=f"parallel downloads, 1..{MAX_JOBS} (default {MAX_JOBS})")
    parser.add_argument("--verify", action="store_true", help="re-hash every cached instance (SHA-256) instead of checking sizes only")


@dataclass
class FetchResult:
    patient_dir: Path
    index_path: Path
    index: dict
    downloaded_series: list[str] = field(default_factory=list)
    downloaded_bytes: int = 0
    requests: int = 0  # network requests made by this call (0 when everything was cached)


class FetchError(RuntimeError):
    pass


# ---------------------------------------------------------------------------------------------
# network (the only two functions that touch it; tests replace them to prove a run stays offline)
# ---------------------------------------------------------------------------------------------
_counter = {"requests": 0}


def _session():
    import requests

    s = requests.Session()
    s.headers["User-Agent"] = "atlas-pipelines/fetch"
    return s


def http_get_json(url: str, params: dict) -> list:
    last = None
    for attempt in range(RETRIES):
        try:
            _counter["requests"] += 1
            r = _session().get(url, params=params, timeout=TIMEOUT_S)
            r.raise_for_status()
            return r.json()
        except Exception as e:  # network errors, HTTP errors, bad JSON: all retried
            last = e
            time.sleep(2.0 * (attempt + 1))
    raise FetchError(f"GET {url} failed after {RETRIES} attempts: {last}")


def http_download(url: str, params: dict, dest: Path) -> int:
    """Download to ``dest`` (a .part file, continued when the server supports Range). Returns bytes received."""
    received = 0
    last = None
    for attempt in range(RETRIES):
        try:
            have = dest.stat().st_size if dest.exists() else 0
            headers = {"Range": f"bytes={have}-"} if have else {}
            _counter["requests"] += 1
            with _session().get(url, params=params, headers=headers, stream=True, timeout=TIMEOUT_S) as r:
                if have and r.status_code == 416:  # nothing left to send: the part is already whole
                    return received
                r.raise_for_status()
                mode = "ab" if (have and r.status_code == 206) else "wb"
                with open(dest, mode) as f:
                    for chunk in r.iter_content(1 << 20):
                        f.write(chunk)
                        received += len(chunk)
            if zipfile.is_zipfile(dest):
                return received
            last = FetchError("the response is not a complete zip archive")
            dest.unlink(missing_ok=True)
        except Exception as e:
            last = e
        time.sleep(2.0 * (attempt + 1))
    raise FetchError(f"download {params} failed after {RETRIES} attempts: {last}")


# ---------------------------------------------------------------------------------------------
# one series
# ---------------------------------------------------------------------------------------------
def _write_json_atomic(path: Path, obj) -> None:
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(obj, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(tmp, path)


def series_ok(series_dir: Path, expect_instances: int | None = None, verify: bool = False) -> dict | None:
    """The series' ``series.json`` if the directory is complete, else None."""
    meta_path = series_dir / "series.json"
    try:
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        files = meta["files"]
    except (OSError, ValueError, KeyError):
        return None
    if not files or (expect_instances is not None and len(files) != expect_instances):
        return None
    on_disk = {p.name for p in series_dir.iterdir() if p.suffix.lower() == ".dcm"}
    if on_disk != {f["name"] for f in files}:
        return None
    for f in files:
        p = series_dir / f["name"]
        if p.stat().st_size != f["size"]:
            return None
        if verify and cachelib.sha256_file(p) != f["sha256"]:
            return None
    return meta


def _fetch_series(pdir: Path, s: dict) -> tuple[dict, int]:
    """Download, verify and unpack one series. Returns (series.json content, bytes downloaded)."""
    import pydicom

    uid = s["SeriesInstanceUID"]
    sdir = pdir / uid
    sdir.mkdir(parents=True, exist_ok=True)
    part = sdir / _PART
    last = None
    received = 0
    for _attempt in range(3):
        received += http_download(f"{API}/getImage", {"SeriesInstanceUID": uid}, part)
        try:
            with zipfile.ZipFile(part) as z:
                bad = z.testzip()
                if bad is not None:
                    raise FetchError(f"zip member {bad} fails its CRC")
                members = [i for i in z.infolist() if i.filename.lower().endswith(".dcm")]
                if len(members) != int(s["ImageCount"]):
                    raise FetchError(f"{len(members)} instances in the zip, getSeries says {s['ImageCount']}")
                names = [Path(i.filename).name for i in members]
                if len(set(names)) != len(names):
                    raise FetchError("duplicate instance file names in the zip")
                for old in sdir.iterdir():  # leftovers of an interrupted unpack
                    if old.name != _PART:
                        old.unlink()
                files = []
                for info, name in zip(members, names):
                    data = z.read(info)
                    (sdir / name).write_bytes(data)
                    files.append({"name": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
            for f in files:
                ds = pydicom.dcmread(sdir / f["name"], stop_before_pixels=True)
                if str(ds.SeriesInstanceUID) != uid or str(ds.Modality) != s["Modality"]:
                    raise FetchError(f"{f['name']}: series {ds.SeriesInstanceUID} / {ds.Modality} is not the one requested")
            meta = {
                "uid": uid,
                "modality": s["Modality"],
                "description": s.get("SeriesDescription"),
                "phase": dicomio.phase_from_description(s.get("SeriesDescription")),
                "files": sorted(files, key=lambda f: f["name"]),
            }
            _write_json_atomic(sdir / "series.json", meta)  # written last: its presence marks the series complete
            part.unlink()
            return meta, received
        except (FetchError, zipfile.BadZipFile, zlib.error, EOFError, OSError) as e:
            last = e
            part.unlink(missing_ok=True)
    raise FetchError(f"series {uid}: {last}")


def _series_digest(meta: dict) -> str:
    h = hashlib.sha256()
    for f in meta["files"]:
        h.update(f"{f['name']}:{f['size']}:{f['sha256']}\n".encode())
    return h.hexdigest()


# ---------------------------------------------------------------------------------------------
# the whole study
# ---------------------------------------------------------------------------------------------
def _check_index_on_disk(pdir: Path, verify: bool) -> dict | None:
    """The existing index if every series it lists is complete on disk, else None."""
    try:
        idx = dicomio.read_index(pdir.parents[1], _case_of(pdir))
    except (FileNotFoundError, dicomio.DicomError, ValueError):
        return None
    try:
        counts = {m: 0 for m in EXPECTED}
        for uid, rec in idx["series"].items():
            meta = series_ok(pdir / uid, rec["instances"], verify)
            if meta is None or _series_digest(meta) != rec["sha256"]:
                return None
            counts[rec["modality"]] += 1
        if counts != EXPECTED or len(idx["phases"]) != dicomio.PHASES:
            return None
    except (KeyError, TypeError):
        return None
    return idx


def _case_of(pdir: Path) -> str:
    for case, ids in dicomio.TCIA_CASES.items():
        if ids["patient_id"] == pdir.name:
            return case
    raise FetchError(f"no case for {pdir}")


def ensure(cache: Path | str | None = None, case: str = paths.REAL_CASE, jobs: int = MAX_JOBS,
           force: bool = False, verify: bool = False, log=None) -> FetchResult:
    """Make the DICOM cache of ``case`` complete; download only what is missing. Idempotent."""
    log = log or (lambda msg: None)
    ids = dicomio.tcia_ids(case)
    pdir = dicomio.patient_dir(cache, case)
    pdir.mkdir(parents=True, exist_ok=True)
    ipath = pdir / "index.json"
    jobs = max(1, min(int(jobs), MAX_JOBS))
    requests_before = _counter["requests"]

    if force:
        shutil.rmtree(pdir)
        pdir.mkdir(parents=True)
    else:
        idx = _check_index_on_disk(pdir, verify)
        if idx is not None:
            return FetchResult(pdir, ipath, idx)

    listing = http_get_json(f"{API}/getSeries", {
        "Collection": ids["collection"], "PatientID": ids["patient_id"],
        "StudyInstanceUID": ids["study_uid"], "format": "json"})
    listing = [s for s in listing if s.get("StudyInstanceUID") == ids["study_uid"]]
    counts = {m: sum(1 for s in listing if s.get("Modality") == m) for m in EXPECTED}
    if counts != EXPECTED or len(listing) != sum(EXPECTED.values()):
        raise FetchError(f"study {ids['study_uid']}: {len(listing)} series {counts}, expected {EXPECTED}")

    by_phase: dict[int, dict[str, dict]] = {}
    for s in listing:
        k = dicomio.phase_from_description(s.get("SeriesDescription"))
        if s["Modality"] in by_phase.setdefault(k, {}):
            raise FetchError(f"two {s['Modality']} series for phase {k}")
        by_phase[k][s["Modality"]] = s
    if sorted(by_phase) != list(range(dicomio.PHASES)) or any(set(v) != set(EXPECTED) for v in by_phase.values()):
        raise FetchError(f"phases do not map one CT and one RTSTRUCT each to 0..90 %: { {k: sorted(v) for k, v in by_phase.items()} }")

    metas: dict[str, dict] = {}
    todo = []
    for s in listing:
        meta = series_ok(pdir / s["SeriesInstanceUID"], int(s["ImageCount"]), verify)
        if meta is None:
            todo.append(s)
        else:
            metas[s["SeriesInstanceUID"]] = meta
    result = FetchResult(pdir, ipath, {})
    if todo:
        log(f"fetch: downloading {len(todo)} of {len(listing)} series with {jobs} parallel connections")
        with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
            futures = {pool.submit(_fetch_series, pdir, s): s for s in todo}
            for fut in concurrent.futures.as_completed(futures):
                s = futures[fut]
                meta, nbytes = fut.result()
                metas[s["SeriesInstanceUID"]] = meta
                result.downloaded_series.append(s["SeriesInstanceUID"])
                result.downloaded_bytes += nbytes
                log(f"fetch:   {s['Modality']:8s} phase {meta['phase']} ({len(meta['files'])} instances, {nbytes / 1e6:.1f} MB)")

    # cross-check: each RTSTRUCT is drawn on the CT of its own phase
    phases = []
    for k in range(dicomio.PHASES):
        ct, rt = by_phase[k]["CT"], by_phase[k]["RTSTRUCT"]
        ref = dicomio.referenced_series_uid(dicomio.read_rtstruct(pdir / rt["SeriesInstanceUID"]))
        if ref != ct["SeriesInstanceUID"]:
            raise FetchError(f"phase {k}: RTSTRUCT {rt['SeriesInstanceUID']} references CT {ref}, "
                             f"but the phase's CT series is {ct['SeriesInstanceUID']}")
        phases.append({
            "phase": k, "percent": 10 * k,
            "ct": ct["SeriesInstanceUID"], "rtstruct": rt["SeriesInstanceUID"],
            "ct_instances": len(metas[ct["SeriesInstanceUID"]]["files"]),
            "rtstruct_instances": len(metas[rt["SeriesInstanceUID"]]["files"]),
            "description": ct.get("SeriesDescription"),
        })
    lic = {s.get("LicenseName") for s in listing}, {s.get("LicenseURI") for s in listing}
    index = {
        "schema": dicomio.INDEX_SCHEMA,
        "collection": ids["collection"],
        "patient_id": ids["patient_id"],
        "study_uid": ids["study_uid"],
        "source": API,
        "licence": sorted(str(v) for v in lic[0]),
        "licence_url": sorted(str(v) for v in lic[1]),
        "counts": counts,
        "phases": phases,
        "series": {
            uid: {"modality": m["modality"], "phase": m["phase"], "instances": len(m["files"]),
                  "bytes": sum(f["size"] for f in m["files"]), "sha256": _series_digest(m)}
            for uid, m in sorted(metas.items())
        },
    }
    _write_json_atomic(ipath, index)
    result.index = index
    result.requests = _counter["requests"] - requests_before
    return result


def run(args) -> int:
    from ..cli import StepError

    try:
        res = ensure(args.cache, args.case, jobs=args.jobs, force=args.force, verify=args.verify, log=print)
    except (FetchError, dicomio.DicomError) as e:
        raise StepError(f"fetch: {e}") from e
    idx = res.index
    total = sum(s["bytes"] for s in idx["series"].values())
    if res.downloaded_series:
        state = f"downloaded {len(res.downloaded_series)} series ({res.downloaded_bytes / 1e6:.1f} MB zipped)"
    else:
        state = "up to date (cached), nothing downloaded"
    print(f"fetch: {state}; {idx['counts']['CT']} CT + {idx['counts']['RTSTRUCT']} RTSTRUCT series, "
          f"{total / 1e6:.1f} MB in {res.patient_dir}")
    return 0
