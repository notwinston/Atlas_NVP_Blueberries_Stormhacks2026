"""P2-D02 `atlas case twin`: the illustrative twin, and the tube / shells library it is built with.

Acceptance (Master Plan 10.2.8, row P2-D02):
    GTV 0.52 cm3 within 3 %; with the hysteresis off (b_AP = 0) ITV 1.70 cm3 within 3 %; with
    b_AP = 4 mm and delta = 24 deg ITV about 2.2 cm3 (pinned from the first run of the pipeline's
    own 0.25 mm voxel union, then held within 3 %); core volume 0 in both.
Also: 18,000 tets, no cracks, the tau-slice at each phase within 1 mm Hausdorff (patient mm,
before M) of that phase's mesh, and the slice at phase 0 a sphere of area within 2 % of
4 pi R^2 (the number lane A checks later, P2-A20).

How the volumes are measured: every volume in table.json is a count of 0.25 mm voxels with
signed distance < 0, times the voxel volume. The ITV is the union over the 10 phases and the 4
sub-steps between each pair of neighbouring phases (wrapping 9 -> 0), 50 positions in all.
"""

from __future__ import annotations

import math

import numpy as np
import pytest

from atlas_pipelines import contract, glb, paths, tubelib
from atlas_pipelines.steps import twin

# ---- the plan's numbers ------------------------------------------------------------------------
GTV_CM3 = 0.52
ITV_NO_HYSTERESIS_CM3 = 1.70
TOL = 0.03
# Pinned on 2026-10-04 from the first run of `atlas case twin` (b_AP = 4 mm, delta = 24 deg):
# the pipeline's own 0.25 mm voxel union over 10 phases x 4 sub-steps, table.json "itv_cm3"
# (136,714 voxels of 0.25 mm). The plan says "about 2.2 cm3".
ITV_HYSTERESIS_PINNED_CM3 = 2.13615625
N_TETS = 18_000
HAUSDORFF_MM = 1.0
AREA_TOL = 0.02
R_MM = 5.0
M = contract.M_DISPLAY

PACKAGE_FILES = ["core.glb", "itv.glb", "occ.r8", "phases.glb", "rho50.glb", "table.json", "tube4d.bin", "tube4d_rigid.bin"]


# ---- fixtures ----------------------------------------------------------------------------------
@pytest.fixture(scope="session")
def twin_dir(run_atlas, out_dir):
    """The real twin package (default b_AP = 4 mm, delta = 24 deg) in the pipeline's output directory."""
    run_atlas("twin", out=out_dir)
    return paths.case_dir("twin", out_dir)


@pytest.fixture(scope="session")
def twin0_dir(run_atlas, tmp_path_factory):
    """The twin with the hysteresis off (b_AP = 0), in a scratch output directory."""
    out = tmp_path_factory.mktemp("twin_b0")
    run_atlas("twin", "--b-ap", "0", out=out)
    return paths.case_dir("twin", out)


@pytest.fixture(scope="session")
def tube(twin_dir):
    return contract.read_tube4d(twin_dir / "tube4d.bin")


@pytest.fixture(scope="session")
def table(twin_dir):
    return contract.read_table(twin_dir / "table.json")


@pytest.fixture(scope="session")
def table0(twin0_dir):
    return contract.read_table(twin0_dir / "table.json")


def ring_mm(tube: contract.Tube4D, k: int, origin_mm) -> np.ndarray:
    nV = tube.n_verts // (tube.layers + 1)
    return contract.from_display(tube.P[k * nV:(k + 1) * nV, :3], origin_mm)


# ---- acceptance: volumes -----------------------------------------------------------------------
def test_gtv_052_within_3_percent(table, table0):
    for t in (table, table0):
        assert len(t.gtv_cm3) == 10
        for v in t.gtv_cm3:
            assert abs(v - GTV_CM3) <= TOL * GTV_CM3, v


def test_itv_170_within_3_percent_without_hysteresis(table0):
    assert table0.extra["twin"]["b_ap_mm"] == 0.0
    assert table0.substeps == 4
    assert table0.extra["voxel_mm"] == 0.25
    assert abs(table0.itv_cm3 - ITV_NO_HYSTERESIS_CM3) <= TOL * ITV_NO_HYSTERESIS_CM3, table0.itv_cm3


def test_itv_with_hysteresis_holds_pinned_value(table):
    assert table.extra["twin"]["b_ap_mm"] == 4.0
    assert table.extra["twin"]["delta_deg"] == 24.0
    assert table.substeps == 4
    assert table.extra["voxel_mm"] == 0.25
    assert abs(table.itv_cm3 - ITV_HYSTERESIS_PINNED_CM3) <= TOL * ITV_HYSTERESIS_PINNED_CM3, table.itv_cm3


def test_core_volume_is_zero_in_both(table, table0, twin_dir, twin0_dir):
    assert table.core_cm3 == 0.0
    assert table0.core_cm3 == 0.0
    for d in (twin_dir, twin0_dir):
        assert glb.triangle_count(d / "core.glb") == 0
        assert glb.read_glb(d / "core.glb") == []


@pytest.mark.parametrize("b_ap", [0.0, 4.0])
def test_itv_matches_an_independent_voxel_union(b_ap, table, table0):
    """The same measurement done directly: 0.25 mm voxels inside any of the 50 spheres (10 phase
    centres and 4 points on each straight segment between neighbouring centres)."""
    t = table if b_ap else table0
    c = twin.centres_mm(b_ap)
    pos = [c[k] + (c[(k + 1) % 10] - c[k]) * j / 5.0 for k in range(10) for j in range(5)]
    g = twin.grid_for(c, 0.25, 3.0)
    X, Y, Z = g.points()
    inside = np.zeros(g.shape, dtype=bool)
    for p in pos:
        inside |= (X - p[0]) ** 2 + (Y - p[1]) ** 2 + (Z - p[2]) ** 2 < R_MM ** 2
    direct = inside.sum() * 0.25 ** 3 / 1000.0
    assert abs(t.itv_cm3 - direct) <= 0.002 * direct, (t.itv_cm3, direct)


# ---- the package -------------------------------------------------------------------------------
def test_package_files(twin_dir, twin0_dir):
    assert sorted(p.name for p in twin0_dir.iterdir()) == PACKAGE_FILES  # pack adds manifest.json and CREDITS.txt
    for name in PACKAGE_FILES:
        assert (twin_dir / name).is_file(), name
    assert not any(p.suffix.lower() == ".dcm" for p in twin_dir.rglob("*"))


def test_frame_and_origin(twin_dir, out_dir, table):
    frame = contract.read_frame(paths.frame_path("twin", out_dir))
    c = np.array(table.centroid_mm)
    assert frame.id == "twin" and frame.M == 15.0 and frame.Lambda == 1.5
    np.testing.assert_allclose(frame.origin_mm, c.mean(axis=0), atol=2e-6)
    np.testing.assert_allclose(table.origin_mm, frame.origin_mm, atol=2e-6)


def test_lujan_centres(table, table0):
    c0 = np.array(table0.centroid_mm)
    # R2 C.4: phase-bin positions for b = 15 mm, n = 2, in mm from exhale
    np.testing.assert_allclose(-c0[:, 2], [15.0, 12.3, 6.4, 1.8, 0.1, 0.0, 0.1, 1.8, 6.4, 12.3], atol=0.05)
    assert np.abs(c0[:, :2]).max() == 0.0
    np.testing.assert_allclose(table0.centroid_range_mm, [0.0, 0.0, 15.0], atol=1e-6)
    c = np.array(table.centroid_mm)
    np.testing.assert_allclose(c[:, 2], c0[:, 2], atol=1e-6)
    k = np.arange(10)
    want_y = -4.0 * np.cos(math.pi * k / 10.0 - math.radians(24.0)) ** 4
    np.testing.assert_allclose(c[:, 1], want_y, atol=1e-6)


def test_table_is_consistent(table, twin_dir):
    assert contract.is_canonical((twin_dir / "table.json").read_bytes())
    assert table.id == "twin" and table.phases == 10
    g = np.array(table.gating_cm3)
    np.testing.assert_allclose(g[:, 0], table.gtv_cm3, atol=1e-6)  # one phase
    assert (np.diff(g, axis=1) >= -1e-9).all()  # a longer arc never shrinks the union
    assert (g <= table.itv_cm3 + 1e-9).all()
    assert table.extra["gating_includes_substeps"] is True
    assert 0.0 < table.max_rho <= 1.0
    assert abs(table.max_rho * 10 - round(table.max_rho * 10)) < 1e-9  # a share of 10 phases
    lo, hi = np.array(table.occ_bbox_min_mm), np.array(table.occ_bbox_max_mm)
    p = np.array(table.argmax_rho_mm)
    assert (lo < p).all() and (p < hi).all()
    # the point of highest occupancy lies inside exactly max_rho * 10 of the phase spheres
    inside = (np.linalg.norm(np.array(table.centroid_mm) - p, axis=1) < R_MM).sum()
    assert inside == round(table.max_rho * 10)


def test_occupancy_texture(twin_dir, table):
    data = (twin_dir / "occ.r8").read_bytes()
    assert len(data) == 64 ** 3
    assert table.occ_dims == [64, 64, 64]
    occ = np.frombuffer(data, dtype=np.uint8).reshape(64, 64, 64)  # [iz, iy, ix]
    assert int(occ.max()) == round(255 * table.max_rho)
    assert set(np.unique(occ)) <= {round(255 * k / 10) for k in range(11)}
    # spot check against the analytic spheres at every voxel centre
    lo, hi = np.array(table.occ_bbox_min_mm), np.array(table.occ_bbox_max_mm)
    ax = [lo[a] + (np.arange(64) + 0.5) * (hi[a] - lo[a]) / 64 for a in range(3)]
    Z, Y, X = np.meshgrid(ax[2], ax[1], ax[0], indexing="ij")
    count = np.zeros(occ.shape, dtype=int)
    for c in np.array(table.centroid_mm):
        count += (X - c[0]) ** 2 + (Y - c[1]) ** 2 + (Z - c[2]) ** 2 < R_MM ** 2
    want = np.rint(255.0 * count / 10.0).astype(np.uint8)
    assert (occ != want).mean() < 0.002  # only voxels within rounding of a sphere surface may differ


def test_shells_within_triangle_budget(twin_dir, table):
    for name in ("itv", "rho50"):
        meshes = glb.read_glb(twin_dir / f"{name}.glb")
        assert len(meshes) == 1 and meshes[0].name == name
        n = glb.triangle_count(twin_dir / f"{name}.glb")
        assert 0 < n <= 1500, (name, n)
    V = contract.from_display(glb.read_glb(twin_dir / "itv.glb")[0].positions, table.origin_mm)
    F = glb.read_glb(twin_dir / "itv.glb")[0].indices.reshape(-1, 3).astype(np.int64)
    vol = tubelib.mesh_volume(V, F) / 1000.0
    assert abs(vol - table.itv_cm3) <= 0.03 * table.itv_cm3  # the shell encloses the measured union


# ---- the tube ----------------------------------------------------------------------------------
def test_tube_has_18000_tets(tube, twin_dir):
    assert tube.n_tets == N_TETS
    assert tube.layers == 10 and tube.M == 15.0 and tube.Lambda == 1.5
    nV = tube.n_verts // 11
    assert tube.n_verts == 11 * nV
    contract.check_tube_layout(tube, nV)
    assert (twin_dir / "tube4d.bin").stat().st_size == 24 + 16 * tube.n_verts + 17 * tube.n_tets
    rigid = contract.read_tube4d(twin_dir / "tube4d_rigid.bin")
    assert rigid.n_tets == N_TETS
    contract.check_tube_layout(rigid, nV)
    assert np.array_equal(rigid.T, tube.T)


def test_tube_has_no_cracks(tube, twin_dir):
    nV = tube.n_verts // 11
    for t in (tube, contract.read_tube4d(twin_dir / "tube4d_rigid.bin")):
        rep = tubelib.tube_crack_report(t, nV)
        # every interior face is shared by exactly 2 tets; the only faces held once are the two end caps
        assert rep["overshared"] == 0
        assert rep["boundary_on_caps"]
        assert rep["boundary_ring0"] == 600 and rep["boundary_last_ring"] == 600
        assert rep["boundary"] == 1200
        assert rep["interior"] == (4 * N_TETS - 1200) // 2
        assert rep["faces"] == rep["interior"] + rep["boundary"]
        assert rep["caps_coincide"]  # ring 10 repeats ring 0, so the tube also closes over the breath


def test_template_is_a_closed_600_triangle_manifold(twin_dir):
    (m,) = glb.read_glb(twin_dir / "phases.glb")
    F = m.indices.reshape(-1, 3).astype(np.int64)
    assert len(F) == 600
    assert tubelib.is_closed_manifold(F)
    assert len(m.positions) == 302  # sphere topology: V - E + F = 2
    assert tubelib.mesh_volume(m.positions, F) > 0  # counter-clockwise from outside


def test_slice_at_each_phase_within_1mm_hausdorff(tube, table):
    c = np.array(table.centroid_mm)
    g = twin.grid_for(c, 1.0, twin.TUBE_MARGIN_MM)
    fields = twin.sphere_fields(c, g)
    worst = 0.0
    for k in range(10):
        tris_mm = contract.from_display(tubelib.slice_tau(tube, tube.Lambda * k / 10.0), table.origin_mm)
        assert len(tris_mm) == 600
        Vk, Fk = tubelib.marching_mesh(fields[k], g)  # that phase's mesh: its 1 mm field contoured at 0
        h = tubelib.hausdorff_tris(tris_mm, Vk, Fk)
        worst = max(worst, h["hausdorff"])
        assert h["hausdorff"] < HAUSDORFF_MM, (k, h)
        # and against the analytic sphere of that phase
        r = np.linalg.norm(tris_mm.reshape(-1, 3) - c[k], axis=1)
        assert np.abs(r - R_MM).max() < 0.05, (k, r.min(), r.max())
    print(f"worst Hausdorff over the 10 phases: {worst:.4f} mm")


def test_slice_at_phase_0_is_a_sphere_within_2_percent_area(tube):
    tris = tubelib.slice_tau(tube, 0.0)
    area_mm2 = tubelib.triangles_area(tris) * (1000.0 / M) ** 2
    want = 4.0 * math.pi * R_MM ** 2
    assert abs(area_mm2 - want) <= AREA_TOL * want, (area_mm2, want)
    # the same slice with every slab cut (no slab shortcut) and at tau = Lambda (the wrap)
    assert np.array_equal(tubelib.slice_tau(tube, 0.0, use_slabs=False), tris)
    assert np.array_equal(tubelib.slice_tau(tube, tube.Lambda), tris)


def test_slices_between_phases_are_closed_surfaces(tube):
    for frac in (0.03, 0.25, 0.5, 0.77):
        for k in (0, 4, 9):
            tris = tubelib.slice_tau(tube, tube.Lambda * (k + frac) / 10.0)
            V, F = tubelib.weld(tris, tol=1e-7)
            e = np.sort(np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), axis=1)
            _, counts = np.unique(e, axis=0, return_counts=True)
            assert (counts == 2).all(), (k, frac)  # no open edge: no crack in the slice


def test_rigid_tube_is_phase_0_translated(twin_dir, tube, table):
    rigid = contract.read_tube4d(twin_dir / "tube4d_rigid.bin")
    c = np.array(table.centroid_mm)
    r0 = ring_mm(rigid, 0, table.origin_mm)
    np.testing.assert_allclose(r0, ring_mm(tube, 0, table.origin_mm), atol=1e-9)
    for k in range(11):
        np.testing.assert_allclose(ring_mm(rigid, k, table.origin_mm), r0 + (c[k % 10] - c[0]), atol=2e-4)


def test_phases_glb_matches_the_tube(twin_dir, tube):
    (m,) = glb.read_glb(twin_dir / "phases.glb")
    assert m.name == "phases"
    assert glb.morph_target_names(twin_dir / "phases.glb") == [[f"phase_{k}" for k in range(10)]]
    nV = len(m.positions)
    assert tube.n_verts == 11 * nV
    for k in range(10):
        ring = tube.P[k * nV:(k + 1) * nV, :3]
        np.testing.assert_allclose(m.positions + m.targets[f"phase_{k}"], ring, atol=1e-6)
    # the tets are the prisms of the same triangles
    T, _ = contract.prisms_to_tets(m.indices.reshape(-1, 3), nV, 10)
    assert np.array_equal(T, tube.T)


# ---- the step ----------------------------------------------------------------------------------
def test_step_is_cached_and_deterministic(run_atlas, twin_dir, out_dir, tmp_path):
    again = run_atlas("twin", out=out_dir)
    assert "up to date" in again.stdout
    fresh = run_atlas("twin", "--force", out=tmp_path)
    assert "built" in fresh.stdout
    for name in PACKAGE_FILES:
        a = (twin_dir / name).read_bytes()
        b = (paths.case_dir("twin", tmp_path) / name).read_bytes()
        assert a == b, f"{name} differs between two runs"


def test_no_clinical_wording_in_table(twin_dir):
    text = (twin_dir / "table.json").read_text().lower()
    for word in ("tumour", "tumor", "lesion", "patient", "treatment", "diagnos", "clinical"):
        assert word not in text


# ---- library helpers ---------------------------------------------------------------------------
def _tesseract_boundary():
    """The unit tesseract's boundary: 8 cubes, each split into 6 tets along its main diagonal (48 tets)."""
    import itertools

    P = np.array(list(itertools.product((0.0, 1.0), repeat=4)))
    index = {tuple(p): i for i, p in enumerate(P)}
    T = []
    for axis in range(4):
        free = [a for a in range(4) if a != axis]
        for side in (0.0, 1.0):
            for perm in itertools.permutations(free):
                v = [0.0] * 4
                v[axis] = side
                path = [tuple(v)]
                for a in perm:
                    v[a] = 1.0
                    path.append(tuple(v))
                T.append([index[p] for p in path])
    return P, np.array(T)


def test_slice_tets_tesseract_checks():
    """Appendix A.8: x + y + z + w = c cuts the unit tesseract in a regular tetrahedron of area
    0.8660 at c = 0.5 and a regular octahedron of area 6.9282 at c = 2."""
    P, T = _tesseract_boundary()
    assert len(T) == 48
    n = np.ones(4) / 2.0
    for c, want in ((0.5, 0.8660), (2.0, 6.9282)):
        tris = tubelib.slice_tets(P, T, n, c / 2.0)
        # area in 4D: Gram determinant of the two edge vectors
        u, v = tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]
        uu, vv, uv = (u * u).sum(1), (v * v).sum(1), (u * v).sum(1)
        area = 0.5 * np.sqrt(np.maximum(uu * vv - uv ** 2, 0.0)).sum()
        assert abs(area - want) < 1e-4, (c, area)


def _octahedron(r=1.0):
    V = r * np.array([[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]], float)
    F = np.array([[0, 2, 4], [2, 1, 4], [1, 3, 4], [3, 0, 4], [2, 0, 5], [1, 2, 5], [3, 1, 5], [0, 3, 5]])
    return V, F


def test_mesh_volume_area_and_manifold():
    V, F = _octahedron(1.0)
    assert tubelib.is_closed_manifold(F)
    assert abs(tubelib.mesh_volume(V, F) - 4.0 / 3.0) < 1e-12
    assert abs(tubelib.mesh_area(V, F) - 4.0 * math.sqrt(3.0)) < 1e-12
    assert not tubelib.is_closed_manifold(F[:-1])  # a hole
    flipped = F.copy()
    flipped[0] = flipped[0][::-1]
    assert not tubelib.is_closed_manifold(flipped)  # inconsistent orientation


def test_hausdorff_of_known_shapes():
    V, F = _octahedron(1.0)
    assert tubelib.hausdorff(V, F, V, F)["hausdorff"] < 1e-12
    # the same octahedron scaled by 2: the farthest points are the corners, 1 apart
    h = tubelib.hausdorff(V, F, 2.0 * V, F)
    assert abs(h["hausdorff"] - 1.0) < 1e-9
    # a shift of 0.3 along x
    h = tubelib.hausdorff(V, F, V + [0.3, 0.0, 0.0], F)
    assert abs(h["hausdorff"] - 0.3) < 1e-9
    # exact point-to-triangle distances: above a face centre, beyond a corner
    d = tubelib.point_mesh_distance(np.array([[1.0, 1.0, 1.0], [3.0, 0.0, 0.0], [0.0, 0.0, 0.0]]), V, F)
    np.testing.assert_allclose(d, [2.0 / math.sqrt(3.0), 2.0, 1.0 / math.sqrt(3.0)], atol=1e-12)


def test_face_sharing_counts():
    # two tets glued on one face: 7 faces, 1 interior, 6 boundary
    T = np.array([[0, 1, 2, 3], [1, 2, 3, 4]])
    rep = tubelib.face_sharing(T)
    assert (rep["faces"], rep["interior"], rep["boundary"], rep["overshared"]) == (7, 1, 6, 0)
    # a third tet on the same face is a fault
    rep = tubelib.face_sharing(np.vstack([T, [[1, 2, 3, 5]]]))
    assert rep["overshared"] == 1
    # a prism split with unsorted diagonals leaves a crack; the sorted split does not
    F = np.array([[0, 1, 2], [0, 2, 3], [0, 3, 1], [1, 3, 2]])  # a tetrahedron surface
    T_ok, _ = contract.prisms_to_tets(F, 4, 1)
    rep = tubelib.face_sharing(T_ok)
    assert rep["overshared"] == 0 and rep["boundary"] == 8


def test_decimate_exact_keeps_a_closed_manifold():
    g = tubelib.Grid.covering([-12, -9, -7], [12, 9, 7], 1.0, 4.0)
    X, Y, Z = g.points()
    f = (np.sqrt((X / 12.0) ** 2 + (Y / 9.0) ** 2 + (Z / 7.0) ** 2) - 1.0) * 7.0
    V, F = tubelib.marching_mesh(f, g)
    assert tubelib.is_closed_manifold(F) and len(F) > 1200
    for n in (600, 300):
        Vd, Fd = tubelib.decimate_exact(V, F, n)
        assert len(Fd) == n and tubelib.is_closed_manifold(Fd)
        assert len(Vd) == n // 2 + 2
        assert tubelib.mesh_volume(Vd, Fd) > 0.9 * tubelib.mesh_volume(V, F)


def test_sdf_from_mask_sphere():
    """The real case's entry point (a sanity check, not a plan number): an anisotropic mask
    (1 x 1 x 3 mm voxels) of a sphere of radius 12 mm gives a field on a 1 mm grid whose inside
    volume is within 5 % of the MASK's own volume (voxel count x voxel volume). Measured on
    2026-10-04: 6.873 against 7.041 cm3, -2.4 %; shape-based interpolation between 3 mm slices
    and the 1 mm Gaussian both shave a convex shape."""
    sp = np.array([1.0, 1.0, 3.0])
    org = np.array([-20.0, -20.0, -21.0])
    ix, iy, iz = np.meshgrid(np.arange(41), np.arange(41), np.arange(15), indexing="ij")
    mask = (org[0] + ix * sp[0]) ** 2 + (org[1] + iy * sp[1]) ** 2 + (org[2] + iz * sp[2]) ** 2 < 12.0 ** 2
    g = tubelib.Grid.covering([-12, -12, -12], [12, 12, 12], 1.0, 6.0)
    sdf = tubelib.sdf_from_mask(mask, org, sp, g)
    assert sdf.shape == g.shape and sdf.dtype == np.float32
    vol = tubelib.voxel_volume_cm3(sdf < 0, g)
    want = mask.sum() * float(np.prod(sp)) / 1000.0
    assert abs(vol - want) < 0.05 * want, (vol, want)
    np.testing.assert_allclose(tubelib.centroid_of_inside(sdf, g), [0.0, 0.0, 0.0], atol=0.2)
