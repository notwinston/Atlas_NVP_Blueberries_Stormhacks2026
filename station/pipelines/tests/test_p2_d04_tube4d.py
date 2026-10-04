"""P2-D04 `atlas case tube4d`: the 4D tube of the real case's target lesion (lung101).

Master Plan 10.2.8, row P2-D04:
    18,000 tets; no cracks (every interior face shared by exactly 2 tets); slice at each phase
    within 1 mm Hausdorff of that phase's contour mesh.

TWO APPROVED DEVIATIONS (decided by the plan owner's delegate, 4 Oct 2026; the numbers of the second
were fixed before the results of the first were known):

1. Method (plan 10.2.4 step 3 says "Newton projection"). The template is first warped onto each phase
   by a demons registration of the mean shape onto the phase field, then projected by the plan's 5
   Newton steps, a step being withheld where it would turn a triangle more than 90 degrees from where
   the registration put it. See ``atlas_pipelines/steps/tube4d.py``.

2. Acceptance of the third statement. "Within 1 mm Hausdorff" is not reachable with one closed
   600-triangle surface for this lesion, by any method:
     - phase 3's field has a detached piece (2 voxels) 2.5 to 3.7 mm from the body, and the fields of
       phases 5 and 8 have a handle (Euler characteristic 0); the template is one piece without one;
     - even a 600-triangle quadric decimation of each phase's OWN contour mesh, with no shared
       connectivity at all, is 0.73 to 0.94 mm from it on the other seven phases.
   The test is therefore, per phase and in BOTH directions (slice -> contour mesh, contour mesh ->
   slice): mean surface distance <= 0.5 mm AND 95th-percentile surface distance <= 1.5 mm. The
   two-sided Hausdorff distance (the maximum) is still computed, printed and stored in
   out/work/lung101/tube4d_report.json, next to the plan's 1 mm, but not asserted.

What the statements mean here, precisely:

* 18,000 tetrahedra: 600 template triangles x 10 layers x 3, in tube4d.bin and in tube4d_rigid.bin.

* No cracks. The tube is (a closed surface in 3D) x (the lane interval 0 <= tau <= Lambda), cut
  into tetrahedra. A triangular face of a tetrahedron is either
      - interior: shared by exactly 2 tetrahedra, or
      - boundary: held by exactly 1 tetrahedron.
  Because the template is a closed surface the tube has no boundary in x, y, z. Its only boundary
  is at the two ends of the lane: the 600 template triangles at layer 0 (tau = 0) and the same
  600 at layer 10 (tau = Lambda). So: exactly 1,200 faces are held by one tetrahedron, they are
  exactly those triangles, every other face (35,400) is shared by exactly 2, and no face is held
  by 3 or more. Layer 10 repeats layer 0 exactly, so the two ends fit each other and the tube
  closes over one breath.

* Surface distance. "That phase's contour mesh" is the undecimated marching-cubes surface of that
  phase's own smoothed signed distance field (CT-grid mask -> signed distance -> 1 mm isotropic
  -> Gaussian sigma 1 mm). Distances are in patient millimetres (before the magnification M)
  between that mesh and the tau-slice of tube4d.bin at tau = Lambda * k / 10. Both directions are
  measured: slice -> contour on a lattice of 15 points per slice triangle, contour -> slice on a
  lattice of 15 points per contour triangle, each with the exact point-to-triangle distance. Mean
  and 95th percentile are over the lattice points of one direction (percentile by linear
  interpolation); the Hausdorff distance is the larger of the two maxima.

* No folds. Every phase mesh, and the surfaces a quarter, half and three quarters of the way between
  neighbouring phases, do not cut through themselves (no edge passes through a triangle), and no
  triangle is folded flat onto its neighbour. A closed surface that does not cut through itself and
  has a positive volume faces outward everywhere.
"""

from __future__ import annotations

import numpy as np
import pytest

from atlas_pipelines import contract, glb, masks, paths, tubelib
from atlas_pipelines.steps import tube4d

# ---- the plan's numbers ------------------------------------------------------------------------
N_TETS = 18_000
N_TRIS = 600
LAYERS = 10
HAUSDORFF_MM = 1.0  # the plan's acceptance number: reported, not asserted (deviation 2)
SIGMA_MM = 1.0
VOXEL_MM = 1.0
ROI_MARGIN_MM = 15.0
NEWTON_ITERS = 5
M = 15.0
LAMBDA_M = 1.5

# ---- the acceptance numbers in force (deviation 2; fixed before the results were known) -----------
MEAN_MM = 0.5
P95_MM = 1.5

CASE = "lung101"
TUBE_FILES = ["phases.glb", "tube4d.bin", "tube4d_rigid.bin"]


# ---- fixtures ----------------------------------------------------------------------------------
@pytest.fixture(scope="session")
def lung_dir(tcia_101, run_atlas, out_dir):
    """The real package directory after `atlas case tube4d` (cached when already built)."""
    run_atlas("tube4d", "--case", CASE, out=out_dir)
    return paths.case_dir(CASE, out_dir)


@pytest.fixture(scope="session")
def tube(lung_dir):
    return contract.read_tube4d(lung_dir / "tube4d.bin")


@pytest.fixture(scope="session")
def rigid(lung_dir):
    return contract.read_tube4d(lung_dir / "tube4d_rigid.bin")


@pytest.fixture(scope="session")
def frame(lung_dir, out_dir):
    return contract.read_frame(paths.frame_path(CASE, out_dir))


@pytest.fixture(scope="session")
def fields(tcia_101):
    """The 10 smoothed phase fields, rebuilt from the masks (not read from any step output)."""
    return tube4d.fields(CASE, tcia_101.cache)


@pytest.fixture(scope="session")
def template_faces(lung_dir):
    (mesh,) = glb.read_glb(lung_dir / "phases.glb")
    return np.asarray(mesh.indices, dtype=np.int64).reshape(-1, 3)


@pytest.fixture(scope="session")
def hausdorff_per_phase(tube, frame, fields):
    """Surface distances per phase (both directions: maximum, mean, 95th percentile), measured here
    from tube4d.bin and the phase fields. "a" is the slice, "b" the contour mesh."""
    out = []
    for k in range(LAYERS):
        tris_m = tubelib.slice_tau(tube, LAMBDA_M * k / LAYERS)
        tris_mm = contract.from_display(tris_m.reshape(-1, 3), frame.origin_mm).reshape(-1, 3, 3)
        Vc, Fc = tubelib.marching_mesh(fields.sdfs[k], fields.grid, 0.0)
        out.append(tubelib.hausdorff_tris(tris_mm, Vc, Fc, level=4))
    return out


def n_template_verts(t: contract.Tube4D) -> int:
    return t.n_verts // (t.layers + 1)


def ring_mm(t: contract.Tube4D, k: int, origin_mm) -> np.ndarray:
    nV = n_template_verts(t)
    return contract.from_display(t.P[k * nV:(k + 1) * nV, :3], origin_mm)


def face_counts(T: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Every triangular face of the tets (sorted vertex indices) and how many tets hold it."""
    T = np.asarray(T, dtype=np.int64).reshape(-1, 4)
    faces = np.sort(np.concatenate([T[:, [0, 1, 2]], T[:, [0, 1, 3]], T[:, [0, 2, 3]], T[:, [1, 2, 3]]]), axis=1)
    return np.unique(faces, axis=0, return_counts=True)


# ---- the method is the plan's, plus the registration of deviation 1 --------------------------------
def test_method_parameters_are_the_plans_plus_the_registration():
    assert tube4d.PARAMS == {
        "roi": "tumor",
        "roi_margin_mm": ROI_MARGIN_MM,
        "voxel_mm": VOXEL_MM,
        "sigma_mm": SIGMA_MM,
        "n_tris": N_TRIS,
        "newton_iters": NEWTON_ITERS,
        "interp": "cubic",
        # deviation 1: the registration, with the chest step's own parameters, and the fold-free projection
        "correspondence": "register",
        "reg_iters": 200,
        "reg_sigma_vox": 1.0,
        "reg_max_step_vox": 0.5,
        "no_fold_cos_min": 0.0,
        "no_fold_max_step_mm": 1.0,
    }
    from atlas_pipelines import chest

    assert (tubelib.REG_ITERS, tubelib.REG_SIGMA_VOX, tubelib.REG_MAX_STEP_VOX) == \
        (chest.REG_ITERS, chest.REG_SIGMA_VOX, chest.REG_MAX_STEP_VOX)
    assert (tube4d.ACCEPT_MEAN_MM, tube4d.ACCEPT_P95_MM, tube4d.PLAN_HAUSDORFF_MM) == (MEAN_MM, P95_MM, HAUSDORFF_MM)


def test_fields_are_built_as_the_plan_says(fields, tcia_101):
    g = fields.grid
    assert g.spacing_mm == VOXEL_MM
    assert len(fields.sdfs) == 10 and all(s.shape == g.shape for s in fields.sdfs)
    np.testing.assert_allclose(fields.spacing_mm, [0.9766, 0.9766, 3.0], atol=1e-4)  # the CT grid
    # ROI = bounding box of the union of the masks + 15 mm (rounded outward to whole millimetres)
    stack, origin, spacing = masks.load_mask_stack(CASE, "tumor", tcia_101.cache)
    lo = origin - 0.5 * spacing
    hi = origin + (np.array(stack.shape[1:]) - 0.5) * spacing
    g_lo = np.array(g.origin_mm)
    g_hi = g_lo + (np.array(g.shape) - 1) * g.spacing_mm
    assert np.all(g_lo <= lo - ROI_MARGIN_MM) and np.all(g_lo > lo - ROI_MARGIN_MM - 1.0)
    assert np.all(g_hi >= hi + ROI_MARGIN_MM) and np.all(g_hi < hi + ROI_MARGIN_MM + 1.0)
    # the field of phase 0 is the R2 D.2 recipe, recomputed here
    from scipy import ndimage

    st, org, sp = masks.load_mask_stack(CASE, "tumor", tcia_101.cache, pad=20)
    sd = ndimage.distance_transform_edt(~st[0], sampling=sp) - ndimage.distance_transform_edt(st[0], sampling=sp)
    X, Y, Z = g.points()
    coords = np.stack(np.broadcast_arrays((X - org[0]) / sp[0], (Y - org[1]) / sp[1], (Z - org[2]) / sp[2]))
    ref = ndimage.gaussian_filter(ndimage.map_coordinates(sd, coords, order=1, mode="nearest"), SIGMA_MM / VOXEL_MM, mode="nearest")
    np.testing.assert_allclose(fields.sdfs[0], ref, atol=1e-4)
    # centroids are the mask centroids of P2-D03
    summary = tcia_101.summary["tumor_stats"]
    np.testing.assert_allclose(np.ptp(fields.centroids_mm, axis=0), summary["centroid_range_mm"], atol=1e-3)


# ---- acceptance 1: 18,000 tetrahedra -----------------------------------------------------------
def test_18000_tetrahedra(lung_dir, tube, rigid):
    for name, t in (("tube4d.bin", tube), ("tube4d_rigid.bin", rigid)):
        assert t.n_tets == N_TETS, name
        assert t.T.shape == (N_TETS, 4)
        assert t.layers == LAYERS and t.M == M and t.Lambda == LAMBDA_M
        nV = n_template_verts(t)
        assert t.n_verts == (LAYERS + 1) * nV
        assert (lung_dir / name).stat().st_size == 24 + 16 * t.n_verts + 17 * t.n_tets
        contract.check_tube_layout(t, nV)
        # 1,800 tets per slab, slab-major
        assert np.array_equal(t.slab, np.repeat(np.arange(LAYERS), N_TETS // LAYERS))


def test_template_is_600_triangles_and_closed(template_faces, tube):
    F = template_faces
    assert F.shape == (N_TRIS, 3)
    nV = n_template_verts(tube)
    assert len(np.unique(F)) == nV
    assert nV - 3 * N_TRIS // 2 + N_TRIS == 2  # Euler characteristic of a sphere: V - E + F = 2
    assert tubelib.is_closed_manifold(F)


# ---- acceptance 2: no cracks -------------------------------------------------------------------
@pytest.mark.parametrize("which", ["tube4d.bin", "tube4d_rigid.bin"])
def test_no_cracks_every_interior_face_is_shared_by_exactly_two_tets(which, lung_dir, template_faces):
    t = contract.read_tube4d(lung_dir / which)
    nV = n_template_verts(t)
    faces, counts = face_counts(t.T)
    # no face is held by 3 or more tets, and none by 0
    assert counts.min() >= 1 and counts.max() == 2
    # the faces held by one tet are exactly the template triangles at layer 0 and at layer 10
    boundary = faces[counts == 1]
    expected = np.sort(np.concatenate([template_faces, template_faces + LAYERS * nV]), axis=1)
    expected = np.unique(expected, axis=0)
    assert len(expected) == 2 * N_TRIS
    assert np.array_equal(boundary, expected)
    # every other face is interior and shared by exactly 2 tets
    assert int((counts == 2).sum()) == (4 * N_TETS - 2 * N_TRIS) // 2 == 35_400
    assert len(faces) == 35_400 + 1_200
    # the two ends coincide: layer 10 repeats layer 0, so the tube closes over one breath
    assert np.array_equal(t.P[:nV, :3], t.P[LAYERS * nV:, :3])
    assert np.all(t.P[:nV, 3] == 0.0) and np.all(t.P[LAYERS * nV:, 3] == np.float32(LAMBDA_M))
    # the library's report says the same
    rep = tubelib.tube_crack_report(t, nV)
    assert rep["overshared"] == 0 and rep["boundary_on_caps"] and rep["caps_coincide"]
    assert rep["boundary_ring0"] == N_TRIS and rep["boundary_last_ring"] == N_TRIS


def test_no_tetrahedron_is_degenerate_in_4d(tube):
    P = tube.P.astype(np.float64)
    e = P[tube.T[:, 1:]] - P[tube.T[:, :1]]  # (m, 3, 4): three edge vectors in 4D
    gram = np.einsum("mik,mjk->mij", e, e)
    assert np.linalg.det(gram).min() > 0.0  # squared 3-volume (x 36) of every tet


def test_slices_are_closed_surfaces(tube):
    """A cut between two phases is a closed surface too: every edge of the slice is used twice."""
    for tau in (0.5 * LAMBDA_M / LAYERS, 3.37 * LAMBDA_M / LAYERS, 9.5 * LAMBDA_M / LAYERS):
        tris = tubelib.slice_tau(tube, tau)
        V, F = tubelib.weld(tris, tol=1e-7)
        e = np.sort(np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), axis=1)
        _, counts = np.unique(e, axis=0, return_counts=True)
        assert (counts == 2).all(), tau


# ---- acceptance 3 (deviation 2): the slice at each phase against that phase's contour mesh --------
def test_slice_at_each_phase_is_that_ring(tube, template_faces):
    nV = n_template_verts(tube)
    for k in range(LAYERS):
        tris = tubelib.slice_tau(tube, LAMBDA_M * k / LAYERS)
        assert len(tris) == N_TRIS
        ring = tube.P[k * nV:(k + 1) * nV, :3].astype(np.float64)
        area = tubelib.triangles_area(tris)
        assert abs(area - tubelib.mesh_area(ring, template_faces)) < 1e-9 * max(area, 1.0)
        V, _ = tubelib.weld(tris, tol=1e-9)
        assert len(V) == nV
        d = np.abs(V[:, None, :] - ring[None, :, :]).max(axis=2).min(axis=1)
        assert d.max() < 1e-9


def test_slice_at_each_phase_mean_0_5mm_and_p95_1_5mm_from_that_phases_contour_mesh(hausdorff_per_phase):
    """The acceptance in force: per phase and per direction, mean <= 0.5 mm and 95th percentile <= 1.5 mm.
    The Hausdorff distance (the plan's quantity) is printed and not asserted."""
    print("phase: mean slice->contour / contour->slice, p95 slice->contour / contour->slice, "
          "Hausdorff (slice->contour / contour->slice), all in patient mm")
    for k, h in enumerate(hausdorff_per_phase):
        print(f"  {k}: mean {h['mean_a_to_b']:.3f} / {h['mean_b_to_a']:.3f}, p95 {h['p95_a_to_b']:.3f} / {h['p95_b_to_a']:.3f}, "
              f"Hausdorff {h['hausdorff']:.3f} ({h['a_to_b']:.3f} / {h['b_to_a']:.3f})")
    worst_h = max(h["hausdorff"] for h in hausdorff_per_phase)
    print(f"  largest Hausdorff {worst_h:.3f} mm (plan: {HAUSDORFF_MM} mm; reported, not asserted)")
    for k, h in enumerate(hausdorff_per_phase):
        assert h["mean_a_to_b"] <= MEAN_MM and h["mean_b_to_a"] <= MEAN_MM, (k, h)
        assert h["p95_a_to_b"] <= P95_MM and h["p95_b_to_a"] <= P95_MM, (k, h)
        assert h["hausdorff"] == max(h["a_to_b"], h["b_to_a"]) and np.isfinite(h["hausdorff"])


def test_report_holds_the_same_distances_and_the_hausdorff(lung_dir, out_dir, hausdorff_per_phase):
    """out/work/lung101/tube4d_report.json stores what this file measures, including the Hausdorff
    distance that is no longer asserted, and says plainly that the plan's 1 mm is not met."""
    rep = contract.read_json(paths.work_dir(CASE, out_dir) / tube4d.REPORT_NAME)
    assert rep["hausdorff_measured"] is True
    assert rep["tets"] == N_TETS and rep["template_triangles"] == N_TRIS
    per = rep["hausdorff_mm"]
    assert len(per) == LAYERS
    for mine, theirs in (("hausdorff", "hausdorff"), ("a_to_b", "slice_to_contour"), ("b_to_a", "contour_to_slice"),
                         ("mean_a_to_b", "mean_slice_to_contour"), ("mean_b_to_a", "mean_contour_to_slice"),
                         ("p95_a_to_b", "p95_slice_to_contour"), ("p95_b_to_a", "p95_contour_to_slice")):
        np.testing.assert_allclose([p[theirs] for p in per], [h[mine] for h in hausdorff_per_phase], atol=1e-5, err_msg=theirs)
    assert abs(rep["hausdorff_max_mm"] - max(p["hausdorff"] for p in per)) < 1e-6
    acc = rep["acceptance"]
    assert acc["mean_mm"] == MEAN_MM and acc["p95_mm"] == P95_MM and acc["plan_hausdorff_mm"] == HAUSDORFF_MM
    assert acc["met"] is (rep["mean_max_mm"] <= MEAN_MM and rep["p95_max_mm"] <= P95_MM)
    assert acc["plan_hausdorff_met"] is (rep["hausdorff_max_mm"] <= HAUSDORFF_MM)
    # the comparison with the plan's projection alone is recorded next to it
    assert rep["plan_method"]["correspondence"] == "translate" and len(rep["plan_method"]["hausdorff_mm"]) == LAYERS


# ---- no folds, and how far the surface moves from phase to phase -----------------------------------
def test_no_phase_mesh_cuts_through_itself_or_folds_flat(tube, frame, template_faces, lung_dir, out_dir):
    nV = n_template_verts(tube)
    V = np.stack([ring_mm(tube, k, frame.origin_mm) for k in range(LAYERS)])
    F = template_faces
    crease = []
    for k in range(LAYERS):
        assert tubelib.self_intersections(V[k], F) == 0, k
        assert tubelib.mesh_volume(V[k], F) > 0, k  # closed, not self-crossing, positive volume: outward everywhere
        nxt = V[(k + 1) % LAYERS]
        for a in (0.25, 0.5, 0.75):  # the surfaces between two phases
            assert tubelib.self_intersections((1 - a) * V[k] + a * nxt, F) == 0, (k, a)
        crease.append(tubelib.min_adjacent_normal_cos(V[k], F))
    print("sharpest crease per phase (cosine between neighbouring triangle normals):", np.round(crease, 3))
    assert min(crease) > np.cos(np.radians(170.0))  # no triangle is folded flat onto its neighbour
    rep = contract.read_json(paths.work_dir(CASE, out_dir) / tube4d.REPORT_NAME)
    assert rep["folds"]["self_intersections"] == [0] * LAYERS and rep["folds"]["self_intersections_between"] == [0] * LAYERS
    np.testing.assert_allclose(rep["folds"]["min_adjacent_normal_cos"], crease, atol=1e-4)
    # the registration itself does not fold space near the surface
    assert min(rep["registration"]["min_jacobian"]) > 0.0
    assert nV == V.shape[1]


def test_self_intersection_counter_on_known_shapes():
    V = np.array([[1.0, 1, 1], [1, -1, -1], [-1, 1, -1], [-1, -1, 1]])
    F = np.array([[0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2]])
    assert tubelib.mesh_volume(V, F) > 0 and tubelib.self_intersections(V, F) == 0
    assert abs(tubelib.min_adjacent_normal_cos(V, F) - (-1.0 / 3.0)) < 1e-12  # regular tetrahedron
    two = (np.concatenate([V, V + [0.5, 0.4, 0.3]]), np.concatenate([F, F + 4]))
    assert tubelib.self_intersections(*two) > 0  # two tetrahedra pushed into each other
    apart = (np.concatenate([V, V + [5.0, 0.0, 0.0]]), np.concatenate([F, F + 4]))
    assert tubelib.self_intersections(*apart) == 0


def test_vertex_steps_between_neighbouring_phases_are_reported(tube, frame, out_dir):
    """Not an acceptance number: the largest vertex move from one phase to the next (wrap 9 -> 0
    included), for the tube as shipped and for the plan's projection alone, stored in the report."""
    V = np.stack([ring_mm(tube, k, frame.origin_mm) for k in range(LAYERS)])
    steps = tubelib.phase_steps_mm(V)
    rep = contract.read_json(paths.work_dir(CASE, out_dir) / tube4d.REPORT_NAME)
    np.testing.assert_allclose(rep["folds"]["step_mm"], steps, atol=1e-3)
    before = rep["plan_method"]["folds"]
    print(f"largest vertex step per interval (mm), shipped: {np.round(steps, 2)} max {steps.max():.2f}; "
          f"plan's projection alone: {np.round(before['step_mm'], 2)} max {before['step_max_mm']:.2f}")
    assert len(before["step_mm"]) == LAYERS


def test_tube_vertices_lie_on_the_phase_surfaces_or_report_how_far(tube, frame, fields):
    """Not an acceptance number: the 5 Newton iterations leave each vertex this far from its phase
    surface (a vertex whose step was withheld to avoid a fold stays off it). Guards against a broken
    projection (a vertex left many millimetres away)."""
    worst = []
    for k in range(LAYERS):
        v = ring_mm(tube, k, frame.origin_mm)
        worst.append(float(np.abs(tubelib.FieldSampler(fields.sdfs[k], fields.grid).value(v)).max()))
    assert max(worst) < 1.0, worst


# ---- the other outputs -------------------------------------------------------------------------
def test_frame_origin_is_the_mean_phase_centroid(frame, fields, tube):
    assert frame.id == CASE and frame.M == M and frame.Lambda == LAMBDA_M
    np.testing.assert_allclose(frame.origin_mm, fields.centroids_mm.mean(axis=0), atol=2e-6)
    # the tube is centred on it: the mean of all ring vertices is within the lesion's size of the origin
    assert np.abs(tube.P[:, :3].astype(np.float64).mean(axis=0)).max() < M * 5.0 / 1000.0


def test_rigid_tube_is_phase_0_translated_along_the_centroid_path(tube, rigid, frame, fields):
    nV = n_template_verts(rigid)
    assert np.array_equal(rigid.T, tube.T) and np.array_equal(rigid.slab, tube.slab)
    np.testing.assert_array_equal(rigid.P[:nV], tube.P[:nV])  # phase 0 is the contoured shape
    c = fields.centroids_mm
    r0 = ring_mm(rigid, 0, frame.origin_mm)
    for k in range(LAYERS + 1):
        np.testing.assert_allclose(ring_mm(rigid, k, frame.origin_mm), r0 + (c[k % LAYERS] - c[0]), atol=2e-4)
    np.testing.assert_array_equal(rigid.P[:, 3], tube.P[:, 3])


def test_phases_glb_blend_shapes_equal_the_tube_rings(lung_dir, tube):
    path = lung_dir / "phases.glb"
    assert glb.triangle_count(path) == N_TRIS
    assert glb.morph_target_names(path) == [[f"phase_{k}" for k in range(LAYERS)]]
    (mesh,) = glb.read_glb(path)
    assert mesh.name == "phases" and mesh.normals is not None
    nV = n_template_verts(tube)
    base = np.asarray(mesh.positions, dtype=np.float64)
    assert base.shape == (nV, 3)
    for k in range(LAYERS):
        ring = tube.P[k * nV:(k + 1) * nV, :3].astype(np.float64)
        np.testing.assert_allclose(base + np.asarray(mesh.targets[f"phase_{k}"], dtype=np.float64), ring, atol=2e-6)


def test_lesion_size_in_the_display_frame(tube, fields):
    """M = 15: a lesion of about 40 mm is about 0.6 m in the room."""
    ext_m = np.ptp(tube.P[:n_template_verts(tube), :3].astype(np.float64), axis=0)
    inside = np.argwhere(fields.sdfs[0] < 0)
    ext_mm = (inside.max(axis=0) - inside.min(axis=0)) * fields.grid.spacing_mm
    np.testing.assert_allclose(ext_m * 1000.0 / M, ext_mm, atol=3.0)


def test_package_holds_no_dicom_and_the_tube_files(lung_dir):
    for name in TUBE_FILES:
        assert (lung_dir / name).is_file(), name
    assert not any(p.suffix.lower() in (".dcm", ".npz") for p in lung_dir.rglob("*"))


# ---- the step: cached, idempotent, real case only ----------------------------------------------
def test_step_is_cached_and_idempotent(tcia_101, run_atlas, lung_dir, tmp_out):
    first = run_atlas("tube4d", "--case", CASE, "--no-report", out=tmp_out)
    assert "tube4d: built" in first.stdout
    second = run_atlas("tube4d", "--case", CASE, "--no-report", out=tmp_out)
    assert "up to date (cached)" in second.stdout
    d = paths.case_dir(CASE, tmp_out)
    assert sorted(p.name for p in d.iterdir()) == TUBE_FILES
    for name in TUBE_FILES:  # a second build in another place gives the same bytes
        assert (d / name).read_bytes() == (lung_dir / name).read_bytes(), name
    # a damaged output is rebuilt
    (d / "tube4d.bin").write_bytes(b"broken")
    third = run_atlas("tube4d", "--case", CASE, "--no-report", out=tmp_out)
    assert "tube4d: built" in third.stdout
    assert (d / "tube4d.bin").read_bytes() == (lung_dir / "tube4d.bin").read_bytes()


def test_step_refuses_the_twin(run_atlas, tmp_out):
    proc = run_atlas("tube4d", "--case", "twin", out=tmp_out, check=False)
    assert proc.returncode == 1
    assert "atlas case twin" in proc.stdout + proc.stderr
    assert not paths.case_dir("twin", tmp_out).exists() or not any(paths.case_dir("twin", tmp_out).iterdir())
