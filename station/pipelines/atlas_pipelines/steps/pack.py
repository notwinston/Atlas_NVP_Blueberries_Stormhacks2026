"""Pack a case: write CREDITS.txt and manifest.json (hashes, labels, licence, credit) of a package.

Master Plan 10.2.4 step 9, work package P2-D09 (Station half).

    atlas case pack [--case lung101|twin] [--strict]

Hashes every file that is present in ``<out>/cases/<id>/``, writes ``CREDITS.txt`` and then
``manifest.json`` (atlas.case/1, docs/case_contract.md section 6). Nothing is silently completed:
the expected files that are absent are listed on stdout and recorded in the manifest as
``extra.missing`` with ``extra.complete = false``. ``extra.complete`` means "every expected file is
present" and nothing more: it is not a statement that the files pass their acceptance tests (the note
``extra.note`` in the manifest says so). ``--strict`` makes an incomplete package a failure (exit 1). Files that are not part of the package layout are packed, recorded as ``extra.unexpected`` and
reported; a DICOM file is never packed (the step stops).

Run ``atlas case verify`` afterwards to re-hash the package against its manifest.
"""

from __future__ import annotations

import hashlib
from pathlib import Path

from .. import cache, contract, labels, paths

NAME = "pack"
HELP = "hash a package and write manifest.json and CREDITS.txt (records missing expected files)"
#: The key depends on the modules this step calls: the label strings and per-file rule (labels.VERSION)
#: and the manifest schema and number format of contract.py (which has no code-version constant).
CODE_VERSION = f"pack/2+{labels.VERSION}+{contract.SCHEMA_CASE}+float{contract.FLOAT_DECIMALS}"

#: ``extra.note`` of every manifest: what ``extra.complete`` does and does not say.
BUILD_NOTE = ("Build note: complete means only that every expected file of the package layout is present. "
              "It is not a statement that the files meet their acceptance tests.")

CREDITS_NAME = "CREDITS.txt"

# --- the files each package is expected to hold (docs/case_contract.md section 1) -------------------
_COMMON = (
    "tube4d.bin", "tube4d_rigid.bin", "phases.glb", "itv.glb", "core.glb", "rho50.glb",
    "occ.r8", "table.json", CREDITS_NAME,
)
_REAL_ONLY = (
    "chest.glb", "airway/tree.json", "airway/greedy.json",
    *(f"sky/risk_p{k}.r8" for k in range(contract.PHASES)), "sky/robust.r8", "sky/sky.json",
)
EXPECTED = {
    labels.KIND_REAL: tuple(sorted(_COMMON + _REAL_ONLY)),
    labels.KIND_ILLUSTRATIVE: tuple(sorted(_COMMON)),
}
CASE_KIND = {paths.REAL_CASE: labels.KIND_REAL, paths.TWIN_CASE: labels.KIND_ILLUSTRATIVE}

def kind_of(case: str) -> str:
    try:
        return CASE_KIND[case]
    except KeyError:
        from ..cli import StepError
        raise StepError(f"pack: unknown case {case!r} (known: {', '.join(sorted(CASE_KIND))})") from None


def file_label(kind: str, rel_path: str) -> str:
    """The one label of a package file: ``labels.label_for``, with no exception. The sky files are
    derived from the real contours and carry the real-data label, the same string as the ``label``
    key inside ``sky/sky.json``; "Teaching threshold" labels the 5 mm value (``threshold_label`` in
    ``sky/sky.json``), not a file."""
    return labels.label_for(kind, rel_path)


def _is_dicom(path: Path) -> bool:
    if path.suffix.lower() in (".dcm", ".dicom", ".ima"):
        return True
    try:
        with path.open("rb") as f:
            head = f.read(132)
    except OSError:
        return False
    return len(head) == 132 and head[128:132] == b"DICM"


# --- CREDITS.txt ----------------------------------------------------------------------------------
def _groups(kind: str, rels: list[str]) -> dict[str, list[str]]:
    g: dict[str, list[str]] = {}
    for r in rels:
        g.setdefault(file_label(kind, r), []).append(r)
    return g


def credits_text(kind: str, rels: list[str]) -> str:
    """The text of CREDITS.txt for the files ``rels`` (without CREDITS.txt itself being required).
    Plain ASCII, LF endings, one trailing LF, no timestamps. Every string comes from labels.py."""
    present = set(rels)
    L: list[str] = []
    if kind == labels.KIND_REAL:
        L += [
            "ATLAS Lung Mission case package: lung101",
            labels.LABEL_REAL,
            "",
            labels.FOOTER,
            "",
            "DATA",
            "Hugo et al. 2016, Data from 4D Lung Imaging of NSCLC Patients, The Cancer Imaging Archive (TCIA), "
            "doi 10.7937/K9/TCIA.2016.ELN8YGLE, CC BY 3.0",
            f"Licence: {labels.LICENCE_REAL} ({labels.LICENCE_REAL_URL})",
            "Patient 101_HM10395, ten breathing phases (4D CT) with the contours of the target lesion and of "
            "lungs, heart, oesophagus, spinal cord and trachea.",
            "Changes made: ATLAS rasterised the contours, smoothed them and turned them into meshes, tables "
            "and maps. The meshes are derived by ATLAS; they are not the original images or contours.",
            "No DICOM file is part of this package.",
            "",
            "CITATIONS",
            f"[1] {labels.CITATION_DATASET}",
            f"[2] {labels.CITATION_PAPER}",
            f"[3] Clark K, et al. The Cancer Imaging Archive (TCIA): Maintaining and Operating a Public "
            f"Information Repository. Journal of Digital Imaging 26(6):1045-1057, 2013. "
            f"doi 10.1007/s10278-013-9622-7",
            f"    Full form: {labels.CITATION_TCIA}",
            "",
            "CREDIT",
            labels.CREDIT_REAL,
            "",
            "WORDING",
            "The tumour of this case is called the target lesion. The motion of this case is never exaggerated.",
        ]
        if any(r.startswith("airway/") for r in present):
            L += [
                f"The airway tree is generated by a model ({labels.LABEL_AIRWAY}). It is not taken from this "
                "patient's images.",
            ]
        if any(r.startswith("sky/") for r in present):
            L += [
                f"The 5 mm clearance threshold of the needle sky is a teaching value ({labels.LABEL_TEACHING_THRESHOLD}). "
                "The sky is a geometric clearance map for education, derived from the contours of this case.",
            ]
        if "tube4d_rigid.bin" in present:
            L += [f"tube4d_rigid.bin is a simplified shape ({labels.LABEL_RIGID}): the shape of phase 0 moved "
                  "along the measured centroid path."]
    else:
        L += [
            "ATLAS Lung Mission case package: twin",
            labels.LABEL_TWIN,
            "",
            labels.FOOTER,
            "",
            "DATA",
            "This is an illustrative model. It uses no patient data. Generated by ATLAS from an analytic model.",
            "A 10 mm nodule moves on a Lujan breathing curve (15 mm head-to-feet motion, 4 s period) with a "
            "small hysteresis loop. Every number comes from this analytic model.",
            "",
            "CITATIONS",
            f"[1] {labels.CITATION_LUJAN}",
            "",
            "CREDIT",
            labels.CREDIT_TWIN,
        ]
    groups = _groups(kind, sorted(present | {CREDITS_NAME}))
    L += ["", "LABELS (one per file, as in manifest.json)"]
    for lab in sorted(groups):
        L.append(f"{lab}")
        for r in groups[lab]:
            L.append(f"    {r}")
    L += ["", labels.FOOTER]
    text = "\n".join(L) + "\n"
    assert text.isascii()
    return text


# --- the step ---------------------------------------------------------------------------------------
def add_arguments(parser) -> None:
    parser.add_argument("--strict", action="store_true",
                        help="exit 1 when expected files are missing (the manifest is still written)")


def _origin(case_dir: Path, case: str, out: Path) -> tuple[list[float], Path]:
    """origin_mm of the case and the file it was read from (table.json, else frame.json)."""
    from ..cli import StepError
    table = case_dir / "table.json"
    if table.is_file():
        try:
            return list(contract.read_table(table).origin_mm), table
        except (OSError, contract.ContractError) as e:
            raise StepError(f"pack: {table}: {e}") from e
    frame = paths.frame_path(case, out)
    if frame.is_file():
        try:
            return list(contract.read_frame(frame).origin_mm), frame
        except (OSError, contract.ContractError) as e:
            raise StepError(f"pack: {frame}: {e}") from e
    raise StepError(f"pack: neither {table} nor {frame} exists, so origin_mm is unknown: "
                    f"run `atlas case {'twin' if case == paths.TWIN_CASE else 'tube4d'} --case {case}` first")


def pack_case(case: str, out: Path, force: bool = False) -> tuple[bool, list[str], list[str]]:
    """Pack one case. Returns (ran, missing, unexpected)."""
    from ..cli import StepError
    kind = kind_of(case)
    case_dir = paths.case_dir(case, out, create=False)
    if not case_dir.is_dir():
        raise StepError(f"pack: {case_dir} does not exist: nothing to pack")
    present = [r for r in contract.package_files(case_dir) if r != CREDITS_NAME]
    for r in present:
        if _is_dicom(case_dir / r):
            raise StepError(f"pack: {r} is a DICOM file; a package never holds DICOM (remove it)")
    expected = EXPECTED[kind]
    missing = [r for r in expected if r != CREDITS_NAME and r not in present]
    unexpected = [r for r in present if r not in expected]
    if not present:
        raise StepError(f"pack: {case_dir} holds no files: nothing to pack")
    origin, origin_src = _origin(case_dir, case, out)

    text = credits_text(kind, present)
    credits_path = case_dir / CREDITS_NAME
    manifest_path = case_dir / contract.MANIFEST_NAME
    inputs = [case_dir / r for r in present]
    if origin_src.parent != case_dir:  # frame.json fallback is outside the package
        inputs.append(origin_src)
    params = {
        "case": case, "kind": kind, "files": present, "missing": missing,
        "credits_sha256": hashlib.sha256(text.encode("ascii")).hexdigest(),
        "labels": {r: file_label(kind, r) for r in present + [CREDITS_NAME]},
        "origin_mm": [float(v) for v in origin],
        "note": BUILD_NOTE,
        "texts": [labels.FOOTER, labels.case_label(kind), labels.licence(kind), labels.credit(kind),
                  list(labels.citations(kind))],
    }

    def work() -> None:
        credits_path.write_bytes(text.encode("ascii"))
        m = contract.build_manifest(case_dir, case, kind, origin,
                                    extra={"complete": not missing, "missing": list(missing),
                                           "note": BUILD_NOTE, "unexpected": list(unexpected)})
        m.labels = {r: file_label(kind, r) for r in m.labels}
        contract.write_manifest(manifest_path, m)

    ran = cache.run_cached(
        stamp=paths.stamp_path(case, NAME, out), code_version=CODE_VERSION, params=params,
        inputs=inputs, outputs=[credits_path, manifest_path], work=work, force=force)
    return ran, missing, unexpected


def run(args) -> int:
    from ..cli import StepError
    case_dir = paths.case_dir(args.case, args.out, create=False)
    ran, missing, unexpected = pack_case(args.case, args.out, args.force)
    m = contract.read_manifest(case_dir / contract.MANIFEST_NAME)
    total = sum(f.size for f in m.files)
    print(f"pack: {'packed' if ran else 'up to date (cached)'} {case_dir}")
    print(f"pack: {len(m.files)} files, {total:,} bytes, plus manifest.json")
    if missing:
        print(f"pack: INCOMPLETE: {len(missing)} expected file(s) missing, recorded in manifest.json extra.missing:")
        for r in missing:
            print(f"pack:   missing {r}")
    else:
        print("pack: complete: every expected file is present (this says nothing about acceptance tests)")
    for r in unexpected:
        print(f"pack: WARNING unexpected file {r} (packed and recorded in extra.unexpected)")
    if missing and args.strict:
        raise StepError(f"{len(missing)} expected file(s) missing (--strict)")
    return 0
