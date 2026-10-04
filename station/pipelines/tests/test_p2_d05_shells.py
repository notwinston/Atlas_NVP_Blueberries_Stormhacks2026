"""P2-D05 `atlas case shells`: union, intersection and occupancy of the real case's target lesion.

Master Plan 10.2.8, row P2-D05: "ITV 47.4 cm3 and core 17.2 cm3 within 5 % (R2 C.2)". Shells of at
most 1,500 triangles each, occ.r8 of 64^3 (10.2.4 step 4).

APPROVED DEVIATION for the ITV (decided by the plan owner's delegate, 4 Oct 2026). There are two
unions, and the plan's sentence mixes them:

* R2 C.2 measured 47.4 cm3 as the plain union of the 10 raw contour masks (no smoothing, no sub-steps).
* Plan 10.2.4 step 4 builds the shell from the smoothed signed distance fields (sigma = 1 mm on 3 mm
  slices) with 4 sub-steps. That union is smaller (43.98 cm3, -7.2 %), because the smoothing rounds
  off what a single slice or a single phase adds to the outline.

``itv_cm3`` of table.json stays the step-4 value, because the app shows table.json beside itv.glb and
the two must agree. So the acceptance is, in three tests:

1. the raw-mask union, stored in table.json under ``extra.raw_masks.union_cm3`` and recounted here
   from the masks, is 47.4 cm3 within 5 %;
2. ``itv_cm3`` of table.json agrees within 1 % with the volume enclosed by the shipped itv.glb;
3. the core, ``core_cm3`` of table.json (step-4 method), is 17.2 cm3 within 5 % (unchanged).

The step-4 numbers are counts of 1 mm voxels of the smoothed fields (CT-grid mask -> signed distance
-> 1 mm isotropic -> Gaussian sigma 1 mm); union over the 10 phases and the 4 sub-steps between
neighbouring phases, wrapping 9 -> 0; intersection over the 10 phases.
"""

from __future__ import annotations

import numpy as np
import pytest

from atlas_pipelines import contract, glb, masks, paths, tubelib
from atlas_pipelines.steps import shells, tube4d

# ---- the plan's numbers ------------------------------------------------------------------------
ITV_CM3 = 47.4  # R2 C.2: the plain union of the raw masks
CORE_CM3 = 17.2
TOL = 0.05
ITV_TABLE_VS_GLB_TOL = 0.01  # table.json itv_cm3 against the volume enclosed by itv.glb
MAX_SHELL_TRIS = 1500
OCC_SIZE = 64
SUBSTEPS = 4
CENTROID_RANGE_MM = (3.1, 3.1, 6.8)  # P2-D03

CASE = "lung101"
SHELL_FILES = ["core.glb", "itv.glb", "occ.r8", "rho50.glb", "table.json"]
SHELLS = ("itv", "core", "rho50")


# ---- fixtures ----------------------------------------------------------------------------------
@pytest.fixture(scope="session")
def lung_dir(tcia_101, run_atlas, out_dir):
    """The real package directory after `atlas case tube4d` and `atlas case shells` (cached when built)."""
    run_atlas("tube4d", "--case", CASE, out=out_dir)
    run_atlas("shells", "--case", CASE, out=out_dir)
    return paths.case_dir(CASE, out_dir)


@pytest.fixture(scope="session")
def table(lung_dir):
    return contract.read_table(lung_dir / "table.json")


@pytest.fixture(scope="session")
def fields(tcia_101):
    return tube4d.fields(CASE, tcia_101.cache)


@pytest.fixture(scope="session")
def raw_stack(tcia_101):
    """The 10 raw masks on the CT grid, straight from the masks cache: (bool [10, ix, iy, iz], voxel cm3)."""
    stack, _, spacing = masks.load_mask_stack(CASE, "tumor", tcia_101.cache)
    return stack, float(np.prod(spacing)) / 1000.0


def shell_mesh_mm(lung_dir, name, origin_mm):
    (mesh,) = glb.read_glb(lung_dir / f"{name}.glb")
    V = contract.from_display(np.asarray(mesh.positions, dtype=np.float64), origin_mm)
    return V, np.asarray(mesh.indices, dtype=np.int64).reshape(-1, 3)


def directed_edge_counts(F):
    """How often each directed edge (a, b) occurs among the triangles."""
    from collections import Counter

    return Counter(map(tuple, np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]).tolist()))


# ---- acceptance (see the module text for the approved deviation) ----------------------------------
def test_raw_mask_union_47_4_cm3_within_5_percent(table, raw_stack):
    """Acceptance 1: R2's 47.4 cm3 is the plain union of the 10 raw masks."""
    stack, vox = raw_stack
    union = float(stack.any(axis=0).sum()) * vox  # recounted here from the masks
    stored = table.extra["raw_masks"]["union_cm3"]
    assert abs(stored - union) < 1e-5
    print(f"raw-mask union {stored:.3f} cm3 ({100.0 * (stored / ITV_CM3 - 1.0):+.2f} % against {ITV_CM3}); "
          f"table.json itv_cm3 (smoothed fields, {table.substeps} sub-steps) {table.itv_cm3:.3f} cm3 "
          f"({100.0 * (table.itv_cm3 / ITV_CM3 - 1.0):+.2f} %)")
    assert abs(stored - ITV_CM3) <= TOL * ITV_CM3, stored


def test_itv_in_table_agrees_with_the_shipped_itv_glb_within_1_percent(lung_dir, table):
    """Acceptance 2: the number shown (table.json itv_cm3) is the volume of the surface shown (itv.glb)."""
    assert table.substeps == SUBSTEPS
    V, F = shell_mesh_mm(lung_dir, "itv", table.origin_mm)
    # the shipped surface has no open edge: every directed edge is matched by its reverse, equally often.
    # (So the enclosed volume is defined. It need not be a manifold: decimation may pinch two sheets
    # together along an edge; the number of such edges is printed.)
    edges = directed_edge_counts(F)
    assert all(edges.get((b, a), 0) == n for (a, b), n in edges.items())
    pinched = sum(1 for n in edges.values() if n > 1) // 2
    mesh_cm3 = tubelib.mesh_volume(V, F) / 1000.0
    gap = mesh_cm3 / table.itv_cm3 - 1.0
    print(f"itv.glb encloses {mesh_cm3:.4f} cm3 ({len(F)} triangles, {pinched} pinched edges); table.json itv_cm3 "
          f"{table.itv_cm3:.4f} cm3; difference {100.0 * gap:+.3f} %")
    assert abs(gap) <= ITV_TABLE_VS_GLB_TOL, (mesh_cm3, table.itv_cm3)


def test_core_17_2_cm3_within_5_percent(table):
    """Acceptance 3 (the plan's, unchanged)."""
    assert abs(table.core_cm3 - CORE_CM3) <= TOL * CORE_CM3, (
        f"core by the plan's step-4 method is {table.core_cm3:.3f} cm3, {100.0 * (table.core_cm3 / CORE_CM3 - 1.0):+.2f} % "
        f"against {CORE_CM3}; the plain intersection of the raw masks is {table.extra['raw_masks']['intersection_cm3']:.3f} cm3")


@pytest.mark.parametrize("name", SHELLS)
def test_shell_has_at_most_1500_triangles(name, lung_dir):
    n = glb.triangle_count(lung_dir / f"{name}.glb")
    assert 0 < n <= MAX_SHELL_TRIS, n
    (mesh,) = glb.read_glb(lung_dir / f"{name}.glb")
    assert mesh.name == name and not mesh.targets


def test_occ_r8_is_64_cubed(lung_dir, table):
    data = (lung_dir / "occ.r8").read_bytes()
    assert len(data) == OCC_SIZE ** 3 == 262_144
    assert table.occ_dims == [OCC_SIZE] * 3


# ---- table.json records how the volumes were computed ------------------------------------------
def test_table_records_how_volumes_were_computed(table):
    ex = table.extra
    assert table.id == CASE and table.phases == 10 and table.substeps == SUBSTEPS
    assert ex["voxel_mm"] == 1.0
    assert ex["volume_method"] == "voxel count of sdf < 0 on the shell grid"
    assert ex["fields"]["sigma_mm"] == 1.0 and ex["fields"]["roi_margin_mm"] == 15.0
    np.testing.assert_allclose(ex["fields"]["ct_voxel_mm"], [0.9766, 0.9766, 3.0], atol=1e-4)
    assert ex["gating_includes_substeps"] is True
    assert ex["rho_samples"] == "the 10 phases"
    assert "no smoothing, no sub-steps" in ex["raw_masks"]["method"]
    assert set(ex["raw_masks"]) == {"method", "union_cm3", "intersection_cm3", "gtv_cm3"}
    assert ex["method_note"] == shells.METHOD_NOTE
    text = contract.dumps_canonical(table.to_obj()).decode("ascii").lower()
    assert "nodule" not in text and "target lesion" in text  # wording of the real case


def test_table_volumes_equal_an_independent_count_on_the_fields(table, fields):
    """The plan-method numbers, recomputed here from the fields without tubelib.build_shells."""
    g, S = fields.grid, np.stack(fields.sdfs)
    vox = g.spacing_mm ** 3 / 1000.0
    np.testing.assert_allclose(table.gtv_cm3, [(s < 0).sum() * vox for s in S], atol=1e-6)
    assert abs(table.core_cm3 - (S.max(axis=0) < 0).sum() * vox) < 1e-6
    phases_only = (S.min(axis=0) < 0).sum() * vox
    assert abs(table.extra["itv_phases_only_cm3"] - phases_only) < 1e-6
    # the sub-steps can only add to the union of the 10 phases, and here they add little
    assert phases_only <= table.itv_cm3 <= 1.02 * phases_only
    # the union with sub-steps, recomputed: each phase field carried along the centroid segment and blended
    c = fields.centroids_mm
    union = S.min(axis=0) < 0
    for k in range(10):
        kn = (k + 1) % 10
        d = c[kn] - c[k]
        for j in range(1, SUBSTEPS + 1):
            a = j / (SUBSTEPS + 1.0)
            f = (1 - a) * tubelib.shifted(S[k], g, a * d) + a * tubelib.shifted(S[kn], g, -(1 - a) * d)
            union |= f < 0
    assert abs(table.itv_cm3 - union.sum() * vox) < 1e-6


def test_raw_mask_values_in_the_table_are_the_raw_masks(table, raw_stack):
    stack, vox = raw_stack
    raw = table.extra["raw_masks"]
    assert abs(raw["union_cm3"] - stack.any(axis=0).sum() * vox) < 1e-5
    assert abs(raw["intersection_cm3"] - stack.all(axis=0).sum() * vox) < 1e-5
    np.testing.assert_allclose(raw["gtv_cm3"], stack.sum(axis=(1, 2, 3)) * vox, atol=1e-5)


def test_raw_masks_reproduce_the_r2_prototype(raw_stack):
    """The R2 numbers (47.4 and 17.2 cm3) were measured as the plain union and intersection of the 10
    raw masks; the raw masks of this pipeline give both within 5 %. (The union half is acceptance 1;
    the raw intersection is extra: the core's acceptance is the step-4 value.)"""
    stack, vox = raw_stack
    union, core = stack.any(axis=0).sum() * vox, stack.all(axis=0).sum() * vox
    assert abs(union - ITV_CM3) <= TOL * ITV_CM3, union
    assert abs(core - CORE_CM3) <= TOL * CORE_CM3, core


# ---- the rest of table.json --------------------------------------------------------------------
def test_centroids_and_origin(table, fields, out_dir, lung_dir):
    np.testing.assert_allclose(table.centroid_mm, fields.centroids_mm, atol=2e-6)
    for got, want in zip(table.centroid_range_mm, CENTROID_RANGE_MM):
        assert abs(got - want) <= 1.0  # P2-D03's number, carried into the table
    frame = contract.read_frame(paths.frame_path(CASE, out_dir))
    np.testing.assert_allclose(table.origin_mm, frame.origin_mm, atol=2e-6)
    np.testing.assert_allclose(table.origin_mm, fields.centroids_mm.mean(axis=0), atol=2e-6)


def test_gating_table(table):
    g = np.array(table.gating_cm3)
    assert g.shape == (10, 10)
    np.testing.assert_allclose(g[:, 0], table.gtv_cm3, atol=1e-6)  # one phase
    assert (np.diff(g, axis=1) >= -1e-9).all()  # a longer gate never shrinks the union
    assert (g[:, 9] <= table.itv_cm3 + 1e-9).all()
    assert (g[:, 9] >= table.extra["itv_phases_only_cm3"] - 1e-9).all()
    assert table.core_cm3 < min(table.gtv_cm3) <= max(table.gtv_cm3) < g[3][4] < table.itv_cm3  # 30-70 % gate


def test_occupancy(lung_dir, table, fields):
    occ = np.frombuffer((lung_dir / "occ.r8").read_bytes(), dtype=np.uint8).reshape(OCC_SIZE, OCC_SIZE, OCC_SIZE)  # [iz, iy, ix]
    # contract section 7: round half to even on doubles, so 76 and 178 (not 77 and 179)
    assert [int(np.rint(255.0 * k / 10.0)) for k in range(11)] == [0, 26, 51, 76, 102, 128, 153, 178, 204, 230, 255]
    assert set(np.unique(occ).tolist()) <= {0, 26, 51, 76, 102, 128, 153, 178, 204, 230, 255}
    assert table.max_rho == 1.0 and occ.max() == 255  # the core is not empty: some point is inside in all 10 phases
    lo, hi = np.array(table.occ_bbox_min_mm), np.array(table.occ_bbox_max_mm)
    step = (hi - lo) / OCC_SIZE
    # the box holds the whole union with its 2 mm margin, and the texture is empty on its border
    inside = np.argwhere(np.min(np.stack(fields.sdfs), axis=0) < 0)
    assert np.all(fields.grid.to_mm(inside.min(axis=0)) >= lo + 2.0 - 1e-6)
    assert np.all(fields.grid.to_mm(inside.max(axis=0)) <= hi - 2.0 + 1e-6)
    assert occ[0].max() == 0 and occ[-1].max() == 0 and occ[:, 0].max() == 0 and occ[:, -1].max() == 0
    # volume integrals of the texture: rho = 1 is the core, rho > 0 the union of the 10 phases
    cell = float(np.prod(step)) / 1000.0
    assert abs((occ == 255).sum() * cell - table.core_cm3) <= 0.03 * table.core_cm3
    assert abs((occ > 0).sum() * cell - table.extra["itv_phases_only_cm3"]) <= 0.03 * table.extra["itv_phases_only_cm3"]
    # the sky's target: a point of the highest occupancy, inside the core. Contract section 5: it is a
    # voxel centre of the 1 mm shell grid (whole millimetres), not of the 64^3 occ grid
    p = np.array(table.argmax_rho_mm)
    assert np.array_equal(p, np.round(p)) and table.extra["voxel_mm"] == 1.0
    assert not np.allclose((p - lo) / step - 0.5, np.round((p - lo) / step - 0.5), atol=1e-6)  # not an occ cell centre
    i = np.floor((p - lo) / step).astype(int)
    assert occ[i[2], i[1], i[0]] == 255
    assert all(tubelib.FieldSampler(s, fields.grid).value(p[None])[0] < 0 for s in fields.sdfs)


# ---- the shell meshes --------------------------------------------------------------------------
def test_shell_meshes_enclose_the_table_volumes(lung_dir, table, fields):
    """After decimation to 1,500 triangles each shell still encloses its volume within 3 %."""
    S = np.stack(fields.sdfs)
    rho50_cm3 = tubelib.voxel_volume_cm3(np.sort(S, axis=0)[4] < 0, fields.grid)  # inside at least 5 of 10 phases
    want = {"itv": table.itv_cm3, "core": table.core_cm3, "rho50": rho50_cm3}
    got = {}
    for name in SHELLS:
        V, F = shell_mesh_mm(lung_dir, name, table.origin_mm)
        got[name] = tubelib.mesh_volume(V, F) / 1000.0
        assert abs(got[name] - want[name]) <= 0.03 * want[name], (name, got[name], want[name])
    assert got["core"] < got["rho50"] < got["itv"]


def test_shell_surfaces_lie_on_their_fields(lung_dir, table, fields):
    S = np.stack(fields.sdfs)
    flds = {"core": S.max(axis=0), "rho50": np.sort(S, axis=0)[4]}
    for name, fld in flds.items():
        V, _ = shell_mesh_mm(lung_dir, name, table.origin_mm)
        d = np.abs(tubelib.sample_trilinear(fld, fields.grid, V)[0])
        assert d.max() < 1.5, (name, d.max())  # decimation moves vertices off the surface by less than this


def test_package_files(lung_dir):
    for name in SHELL_FILES:
        assert (lung_dir / name).is_file(), name
    assert not any(p.suffix.lower() in (".dcm", ".npz") for p in lung_dir.rglob("*"))
    assert contract.is_canonical((lung_dir / "table.json").read_bytes())


# ---- the step: cached, idempotent, real case only ----------------------------------------------
def test_step_is_cached_and_idempotent(tcia_101, run_atlas, lung_dir, tmp_out):
    missing = run_atlas("shells", "--case", CASE, out=tmp_out, check=False)
    assert missing.returncode == 1 and "tube4d" in missing.stdout + missing.stderr  # needs frame.json
    run_atlas("tube4d", "--case", CASE, "--no-report", out=tmp_out)
    first = run_atlas("shells", "--case", CASE, out=tmp_out)
    assert "shells: built" in first.stdout
    second = run_atlas("shells", "--case", CASE, out=tmp_out)
    assert "up to date (cached)" in second.stdout
    d = paths.case_dir(CASE, tmp_out)
    for name in SHELL_FILES:  # a second build in another place gives the same bytes
        assert (d / name).read_bytes() == (lung_dir / name).read_bytes(), name
    (d / "table.json").unlink()
    third = run_atlas("shells", "--case", CASE, out=tmp_out)
    assert "shells: built" in third.stdout
    assert (d / "table.json").read_bytes() == (lung_dir / "table.json").read_bytes()


def test_step_refuses_the_twin(run_atlas, tmp_out):
    proc = run_atlas("shells", "--case", "twin", out=tmp_out, check=False)
    assert proc.returncode == 1
    assert "atlas case twin" in proc.stdout + proc.stderr
