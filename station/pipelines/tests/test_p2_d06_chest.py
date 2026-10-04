"""P2-D06: `atlas case chest` for lung101 -> chest.glb.

Acceptance (Master Plan 10.2.8, row P2-D06), asserted exactly:
    * chest.glb has at most 40,000 triangles;
    * the volume of both lungs, measured on the lung meshes of chest.glb itself (closed-mesh volume at
      patient scale), is 4.42 L at phase 0 % and 3.87 L at phase 50 %, each within 3 %.

The reference values are contour area x 3 mm slice spacing (R2_rt_lung.py). The test prints the mesh
volume next to the mask volume and the contour-area volume.

The file is built to 36,000 triangles (13,500 + 13,500 + 5,400 + 3,600), 10 % under the limit, so the
limit is not met by a hair. The largest-piece rule drops one piece of the left-lung ROI at phases 6 and
7; `test_what_is_dropped_from_the_left_lung_is_right_lung` checks on the masks what that piece is.

The chest needs the display frame of the case (out/work/lung101/frame.json, written by `atlas case
tube4d`). When that file exists in the real output root the chest is built there (and cached there).
When it does not exist yet, the chest is built in a temporary output root with `--provisional-frame`
(origin = mean mask centroid of the target lesion). Volumes, triangle counts and the correspondence do
not depend on the origin; `test_built_in_the_tube_frame` says which of the two was used.
"""

from __future__ import annotations

import json
from types import SimpleNamespace

import numpy as np
import pytest

from atlas_pipelines import chest, contract, glb, labels, masks, paths, tubelib

# --- the plan's numbers (P2-D06) ---------------------------------------------------------------
MAX_TRIANGLES = 40_000
BUILT_TRIANGLES = {"lung_r": 13_500, "lung_l": 13_500, "heart": 5_400, "trachea": 3_600}  # 15 : 15 : 6 : 4
LUNG_VOLUME_L = {0: 4.42, 5: 3.87}  # phase index -> litres (phase 5 is the 50 % bin)
LUNG_TOLERANCE = 0.03
M = 15.0

PART_NAMES = ["lung_r", "lung_l", "heart", "trachea"]
LUNGS = ("lung_r", "lung_l")
TARGETS = [f"phase_{k}" for k in range(10)]


@pytest.fixture(scope="session")
def chest_pkg(tcia_101, run_atlas, out_dir, session_tmp_out):
    """Run the step (cached) and return paths, the parsed GLB and the report."""
    have_frame = paths.frame_path(paths.REAL_CASE, out_dir).exists()
    out = out_dir if have_frame else session_tmp_out
    args = ["chest", "--case", paths.REAL_CASE] + ([] if have_frame else ["--provisional-frame"])
    proc = run_atlas(*args, out=out)
    path = paths.case_dir(paths.REAL_CASE, out) / "chest.glb"
    report = json.loads((paths.work_dir(paths.REAL_CASE, out) / "chest_report.json").read_text())
    return SimpleNamespace(out=out, args=args, path=path, meshes={m.name: m for m in glb.read_glb(path)},
                           order=[m.name for m in glb.read_glb(path)], report=report, stdout=proc.stdout,
                           have_frame=have_frame, origin_mm=np.array(report["origin_mm"], dtype=np.float64))


def signed_volume(V: np.ndarray, F: np.ndarray) -> float:
    """Volume enclosed by a closed triangle mesh (divergence theorem), positive for outward winding."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    return float(np.einsum("ij,ij->i", a, np.cross(b, c)).sum() / 6.0)


def phase_vertices_display(mesh: glb.GlbMesh, phase: int) -> np.ndarray:
    return np.asarray(mesh.positions, np.float64) + np.asarray(mesh.targets[f"phase_{phase}"], np.float64)


def faces(mesh: glb.GlbMesh) -> np.ndarray:
    return np.asarray(mesh.indices, np.int64).reshape(-1, 3)


def lung_volume_l(pkg, phase: int) -> float:
    """Both lungs at ``phase``, litres at patient scale: display metres -> divide the volume by M^3."""
    total_m3 = sum(signed_volume(phase_vertices_display(pkg.meshes[n], phase), faces(pkg.meshes[n])) for n in LUNGS)
    return total_m3 / M ** 3 * 1000.0


# =============================================================================================
# Acceptance
# =============================================================================================
def test_acceptance_triangle_budget(chest_pkg):
    n = glb.triangle_count(chest_pkg.path)
    print(f"\nchest.glb: {n} triangles (budget {MAX_TRIANGLES})")
    assert n == sum(len(faces(m)) for m in chest_pkg.meshes.values())
    assert n <= MAX_TRIANGLES


def test_triangle_headroom_and_proportions(chest_pkg):
    """Not the acceptance: the file is built to 36,000 triangles, in the proportions 15 : 15 : 6 : 4,
    which leaves 10 % of the limit unused."""
    got = {name: len(faces(m)) for name, m in chest_pkg.meshes.items()}
    print(f"\nchest.glb parts: {got}, total {sum(got.values())}")
    assert sum(BUILT_TRIANGLES.values()) == 36_000 == chest.TARGET_TRIS_TOTAL
    assert {p.name: p.max_tris for p in chest.PARTS} == BUILT_TRIANGLES
    for name, budget in BUILT_TRIANGLES.items():
        assert 0.98 * budget <= got[name] <= budget, name
    assert sum(got.values()) <= 0.9 * MAX_TRIANGLES
    assert chest_pkg.report["max_triangles"] == MAX_TRIANGLES and chest_pkg.report["target_triangles"] == 36_000


@pytest.mark.parametrize("phase", [0, 5])
def test_acceptance_lung_volume(chest_pkg, tcia_101, phase):
    want = LUNG_VOLUME_L[phase]
    got = lung_volume_l(chest_pkg, phase)
    mask_l = tcia_101.summary["lungs"][phase]["volume_l"]
    contour_l = tcia_101.summary["lungs"][phase]["contour_volume_l"]
    print(f"\nlungs at phase {phase} ({10 * phase} %): mesh {got:.4f} L, raw mask {mask_l:.4f} L, "
          f"contour area x 3 mm {contour_l:.4f} L, plan {want} L, mesh vs plan {100 * (got / want - 1):+.2f} %")
    assert abs(got - want) <= LUNG_TOLERANCE * want


# =============================================================================================
# The file follows the contract
# =============================================================================================
def test_parts_and_morph_targets(chest_pkg):
    assert chest_pkg.order == PART_NAMES
    assert glb.morph_target_names(chest_pkg.path) == [TARGETS] * len(PART_NAMES)
    for name, m in chest_pkg.meshes.items():
        n = len(m.positions)
        assert m.normals is not None and np.asarray(m.normals).shape == (n, 3)
        assert np.allclose(np.linalg.norm(np.asarray(m.normals, np.float64), axis=1), 1.0, atol=1e-4)
        for t in TARGETS:
            assert np.asarray(m.targets[t]).shape == (n, 3), (name, t)
        assert faces(m).max() < n


def test_no_materials_one_primitive_per_part(chest_pkg):
    doc, _ = glb.parse_glb(chest_pkg.path.read_bytes())
    assert "materials" not in doc and "textures" not in doc and "animations" not in doc
    assert [len(m["primitives"]) for m in doc["meshes"]] == [1] * len(PART_NAMES)
    assert all("matrix" not in n and "translation" not in n and "scale" not in n and "rotation" not in n
               for n in doc["nodes"])


def test_every_part_is_closed_and_outward_at_every_phase(chest_pkg):
    for name, m in chest_pkg.meshes.items():
        F = faces(m)
        assert tubelib.is_closed_manifold(F), name
        assert signed_volume(np.asarray(m.positions, np.float64), F) > 0, name
        for k in range(10):
            assert signed_volume(phase_vertices_display(m, k), F) > 0, (name, k)


def test_report_matches_the_file(chest_pkg):
    """The numbers printed by the step are the numbers of the file (float32 rounding apart)."""
    rep = chest_pkg.report
    assert rep["triangles"] == glb.triangle_count(chest_pkg.path)
    for k in range(10):
        assert lung_volume_l(chest_pkg, k) == pytest.approx(rep["lungs_mesh_volume_l"][k], rel=1e-5)
    for name in PART_NAMES:
        m = chest_pkg.meshes[name]
        for k in range(10):
            v_cm3 = signed_volume(phase_vertices_display(m, k), faces(m)) / M ** 3 * 1e6
            assert v_cm3 == pytest.approx(rep["parts"][name]["mesh_volume_cm3"][k], rel=1e-4)


# =============================================================================================
# Faithful to the data, not tuned to the number
# =============================================================================================
def test_lung_mesh_volume_follows_the_mask_at_every_phase(chest_pkg):
    """Each lung mesh encloses the volume of the mask piece it was built from, within 1 %, at all 10
    phases (so the two acceptance volumes are not special cases)."""
    for name in LUNGS:
        r = chest_pkg.report["parts"][name]
        kept = np.array(r["mask_volume_cm3"]) - np.array(r["dropped_cm3"])
        mesh = np.array(r["mesh_volume_cm3"])
        print(f"\n{name}: mesh / mask per phase = {np.round(mesh / kept, 4).tolist()}")
        assert np.all(np.abs(mesh / kept - 1.0) <= 0.01), name


def test_nothing_dropped_from_the_lungs_at_the_acceptance_phases(chest_pkg):
    for name in LUNGS:
        d = chest_pkg.report["parts"][name]["dropped_cm3"]
        assert d[0] < 0.1 and d[5] < 0.1, (name, d)


def test_what_is_dropped_from_the_left_lung_is_right_lung(chest_pkg, tcia_101):
    """The pieces of the left-lung ROI that the largest-piece rule drops at phases 6 and 7 (81 and
    38 cm3) are right-lung contours filed under the left-lung ROI: every voxel of them is inside the
    right-lung mask of the same phase, on the patient's right. They are therefore already in lung_r,
    and adding them to lung_l would count that volume twice. Measured on the masks, not assumed."""
    from scipy import ndimage

    dropped = chest_pkg.report["parts"]["lung_l"]["dropped_cm3"]
    assert [k for k, d in enumerate(dropped) if d > 1.0] == [6, 7]
    assert dropped[6] == pytest.approx(81.4, abs=0.1) and dropped[7] == pytest.approx(37.7, abs=0.1)
    assert max(chest_pkg.report["parts"]["lung_r"]["dropped_cm3"]) < 0.1
    want_slices = {6: 2, 7: 1}
    for k in (6, 7):
        left, lo, sp = masks.load_mask(tcia_101.case, "llung", k, cache=tcia_101.cache)
        right, ro, _ = masks.load_mask(tcia_101.case, "rlung", k, cache=tcia_101.cache)
        lab, n = ndimage.label(left)
        size = np.bincount(lab.ravel())
        size[0] = 0
        stray = (lab != 0) & (lab != int(size.argmax()))
        idx = np.argwhere(stray)
        # the same voxels in the right-lung crop (both crops are cuts of one CT grid)
        j = idx + np.rint((lo - ro) / sp).astype(int)
        ok = np.all((j >= 0) & (j < np.array(right.shape)), axis=1)
        in_right = np.zeros(len(idx), bool)
        in_right[ok] = right[tuple(j[ok].T)]
        x = lo[0] + idx[:, 0] * sp[0]
        main_x = lo[0] + np.argwhere(lab == int(size.argmax()))[:, 0].mean() * sp[0]
        big = idx[np.isin(lab[tuple(idx.T)], np.flatnonzero(size > 100))]
        print(f"\nphase {k}: dropped {stray.sum() * np.prod(sp) / 1000:.1f} cm3 on {len(np.unique(big[:, 2]))} slice(s) "
              f"z = {np.unique(lo[2] + big[:, 2] * sp[2]).tolist()} mm, x {x.min():.1f} .. {x.max():.1f} mm "
              f"(left lung centroid x {main_x:.1f}), inside the right-lung mask: {100 * in_right.mean():.1f} %")
        assert in_right.all()
        assert x.max() < 0.0 < main_x  # +x is the patient's left
        assert len(np.unique(big[:, 2])) == want_slices[k]


def test_vertices_lie_on_the_phase_surface(chest_pkg):
    for name in PART_NAMES:
        assert max(chest_pkg.report["parts"][name]["residual_mm"]) < 0.05, name


@pytest.mark.parametrize("name,roi", [("lung_r", "rlung"), ("lung_l", "llung"), ("heart", "heart")])
@pytest.mark.parametrize("phase", [0, 5])
def test_mesh_sits_on_the_mask_in_the_patient_frame(chest_pkg, tcia_101, name, roi, phase):
    """Undo the display frame (contract.from_display with the report's origin) and compare with the mask
    of that phase: 3 mm inside the surface is mask, 3 mm outside is not. This checks the frame, the scale
    and the phase order of the morph targets independently of the builder."""
    m = chest_pkg.meshes[name]
    F = faces(m)
    V = contract.from_display(phase_vertices_display(m, phase), chest_pkg.origin_mm)
    N = tubelib.vertex_normals(V, F)
    mask, origin, spacing = masks.load_mask(tcia_101.case, roi, phase, cache=tcia_101.cache, pad=4)

    def inside(P):
        i = np.rint((P - origin) / spacing).astype(int)
        ok = np.all((i >= 0) & (i < np.array(mask.shape)), axis=1)
        out = np.zeros(len(P), bool)
        out[ok] = mask[i[ok, 0], i[ok, 1], i[ok, 2]]
        return out

    frac_in = inside(V - 3.0 * N).mean()
    frac_out = 1.0 - inside(V + 3.0 * N).mean()
    print(f"\n{name} phase {phase}: 3 mm inward in mask {frac_in:.4f}, 3 mm outward out of mask {frac_out:.4f}")
    assert frac_in >= 0.95 and frac_out >= 0.95


def test_phases_are_in_correspondence(chest_pkg):
    """A vertex is the same piece of surface at every phase: few triangles turn over, and the wrap
    9 -> 0 is a step like the others (lane A's blend-shape player interpolates across it)."""
    for name in PART_NAMES:
        r = chest_pkg.report["parts"][name]
        steps = np.array(r["step_mm"])
        print(f"\n{name}: largest vertex step per phase change (mm, last = wrap 9->0) {np.round(steps, 2).tolist()}; "
              f"reversed triangles {r['reversed_triangles']}")
        assert max(r["reversed_triangles"]) <= 0.005 * r["triangles"], name
        assert steps[9] <= steps[:9].max(), name


def test_lungs_breathe(chest_pkg):
    """Inhale (phase 0) is the largest lung volume and the 50 % phase is near the smallest."""
    v = np.array([lung_volume_l(chest_pkg, k) for k in range(10)])
    assert v.argmax() == 0
    assert v[5] <= v.min() * 1.01


# =============================================================================================
# Step behaviour
# =============================================================================================
def test_second_run_is_cached(chest_pkg, run_atlas):
    before = chest_pkg.path.read_bytes()
    proc = run_atlas(*chest_pkg.args, out=chest_pkg.out)
    assert "up to date (cached)" in proc.stdout
    assert chest_pkg.path.read_bytes() == before


def test_build_is_deterministic(chest_pkg, tcia_101):
    """Rebuilding one part in this process gives the bytes that are in the file (the trachea: seconds)."""
    part = next(p for p in chest.PARTS if p.name == "trachea")
    stack, origin, spacing = masks.load_mask_stack(tcia_101.case, part.roi, tcia_101.cache)
    a = chest.build_part(part, stack, origin, spacing)
    b = chest.build_part(part, stack, origin, spacing)
    assert np.array_equal(a.V, b.V) and np.array_equal(a.F, b.F) and np.array_equal(a.V0, b.V0)
    (g,) = chest.glb_meshes([a], chest_pkg.origin_mm)
    m = chest_pkg.meshes["trachea"]
    assert np.array_equal(np.asarray(g.positions, np.float32), np.asarray(m.positions, np.float32))
    assert np.array_equal(np.asarray(g.indices).reshape(-1, 3), faces(m))
    for t in TARGETS:
        assert np.array_equal(np.asarray(g.targets[t], np.float32), np.asarray(m.targets[t], np.float32))


def test_refuses_the_twin_and_a_missing_frame(run_atlas, tmp_out, tcia_101):
    p = run_atlas("chest", "--case", paths.TWIN_CASE, out=tmp_out, check=False)
    assert p.returncode == 1 and "real case" in (p.stdout + p.stderr)
    p = run_atlas("chest", "--case", paths.REAL_CASE, out=tmp_out, check=False)
    assert p.returncode == 1 and "frame.json" in (p.stdout + p.stderr)
    assert not (paths.case_dir(paths.REAL_CASE, tmp_out, create=False) / "chest.glb").exists()


def test_built_in_the_tube_frame(chest_pkg, out_dir):
    """Says which frame the tested file was built in. With frame.json present the origin must be the
    tube's; without it the build is provisional (tube4d has not run in this output root)."""
    if chest_pkg.have_frame:
        frame = contract.read_frame(paths.frame_path(paths.REAL_CASE, out_dir))
        assert chest_pkg.report["frame"] == "frame.json"
        assert np.allclose(chest_pkg.origin_mm, frame.origin_mm, atol=1e-6)
    else:
        print("\nNOT VERIFIED: frame.json of lung101 does not exist yet; chest built with --provisional-frame")
        assert chest_pkg.report["frame"] == "provisional"


def test_label(chest_pkg):
    assert labels.label_for(labels.KIND_REAL, "chest.glb") == \
        "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS."
