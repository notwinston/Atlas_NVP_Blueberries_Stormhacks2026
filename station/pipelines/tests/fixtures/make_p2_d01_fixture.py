"""Writes the committed P2-D01 fixture: a 2-tetrahedron case package (tube4d.bin, table.json,
manifest.json) in tests/fixtures/p2_d01/. Synthetic numbers; no patient data.

    .venv/bin/python tests/fixtures/make_p2_d01_fixture.py [output directory]

tests/test_p2_d01_contract.py asserts that the committed files equal what this script writes.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np

from atlas_pipelines import contract, labels

FIXTURE_ID = "fixture2tet"


def tube() -> contract.Tube4D:
    """Two tetrahedra sharing the face (1, 2, 3); slabs 0 and 1 of a 2-layer lane."""
    P = np.array(
        [
            [0.0, 0.0, 0.0, 0.0],
            [0.1, 0.0, 0.0, 0.0],
            [0.0, 0.1, 0.0, 0.75],
            [0.0, 0.0, 0.1, 0.75],
            [-0.1, -0.2, 0.3, 1.5],
        ],
        dtype=np.float32,
    )
    T = np.array([[0, 1, 2, 3], [1, 2, 3, 4]], dtype=np.int32)
    slab = np.array([0, 1], dtype=np.uint8)
    return contract.Tube4D(P=P, T=T, slab=slab, layers=2, M=contract.M_DISPLAY, Lambda=contract.LAMBDA_M)


def table() -> contract.Table:
    gtv = [0.52 + 0.001 * k for k in range(10)]
    gating = [[round(gtv[s] + 0.13 * l + 0.0001 * s * l, 6) for l in range(10)] for s in range(10)]
    centroid = [[-12.5 + 0.1 * k, 30.25 - 0.3 * k, 100.0 + 1.5 * np.sin(k)] for k in range(10)]
    c = np.array(centroid)
    return contract.Table(
        id=FIXTURE_ID,
        gtv_cm3=gtv,
        itv_cm3=1.7,
        core_cm3=0.0,
        gating_cm3=gating,
        centroid_mm=centroid,
        centroid_range_mm=(c.max(axis=0) - c.min(axis=0)).tolist(),
        argmax_rho_mm=[-12.0, 29.0, 100.5],
        max_rho=0.9,
        origin_mm=[-12.05, 28.9, 100.2932],
        occ_bbox_min_mm=[-32.0, 9.0, 80.0],
        occ_bbox_max_mm=[8.0, 49.0, 120.0],
        substeps=4,
        extra={"note": "synthetic 2-tetrahedron fixture – not a patient", "values": [1, 2.5, True, None]},
    )


def write(out: Path) -> dict[str, bytes]:
    out.mkdir(parents=True, exist_ok=True)
    files = {
        "tube4d.bin": contract.write_tube4d(out / "tube4d.bin", tube()),
        "table.json": contract.write_table(out / "table.json", table()),
    }
    stale = out / contract.MANIFEST_NAME
    if stale.exists():
        stale.unlink()
    manifest = contract.build_manifest(out, FIXTURE_ID, labels.KIND_ILLUSTRATIVE, table().origin_mm)
    files["manifest.json"] = contract.write_manifest(out / contract.MANIFEST_NAME, manifest)
    return files


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "p2_d01"
    for name, data in write(target).items():
        print(f"{len(data):6d}  {target / name}")
