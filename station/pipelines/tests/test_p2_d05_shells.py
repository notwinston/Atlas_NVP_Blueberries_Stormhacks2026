"""P2-D05 `atlas case shells`: union, intersection and occupancy of the real case's target lesion.

Acceptance (Master Plan 10.2.8, row P2-D05):
    ITV 47.4 cm3 and core 17.2 cm3 within 5 % (R2 C.2). Shells of at most 1,500 triangles each,
    occ.r8 of 64^3 (10.2.4 step 4).

What is measured: ``itv_cm3`` and ``core_cm3`` of table.json, which are the plan's step-4 values:
counts of 1 mm voxels of the smoothed signed distance fields (CT-grid mask -> signed distance ->
1 mm isotropic -> Gaussian sigma 1 mm); union over the 10 phases and the 4 sub-steps between
neighbouring phases, wrapping 9 -> 0; intersection over the 10 phases.

For comparison only, and never in place of the acceptance values: table.json also records under
``extra.raw_masks`` the plain union and intersection of the 10 raw masks counted on the CT grid,
with no smoothing and no sub-steps. The R2 prototype's 47.4 and 17.2 cm3 are numbers of that
raw-mask kind (its masks were rasterised at 1 mm in-plane on the 3 mm slices).
``test_raw_masks_reproduce_the_r2_prototype`` checks those; it is not the acceptance test.
"""

from __future__ import annotations

import numpy as np
import pytest

from atlas_pipelines import contract, glb, masks, paths, tubelib
from atlas_pipelines.steps import shells, tube4d

# ---- the plan's numbers ------------------------------------------------------------------------
ITV_CM3 = 47.4
CORE_CM3 = 17.2
TOL = 0.05
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


# ---- acceptance: the plan's numbers, on the plan's method ----------------------------------------
def test_itv_47_4_cm3_within_5_percent(table):
    assert table.substeps == SUBSTEPS
    assert abs(table.itv_cm3 - ITV_CM3) <= TOL * ITV_CM3, (
        f"ITV by the plan's step-4 method (smoothed fields, {table.substeps} sub-steps) is {table.itv_cm3:.3f} cm3, "
        f"{100.0 * (table.itv_cm3 / ITV_CM3 - 1.0):+.2f} % against {ITV_CM3}; "
        f"the plain union of the raw masks is {table.extra['raw_masks']['union_cm3']:.3f} cm3")


def test_core_17_2_cm3_within_5_percent(table):
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
    """NOT the acceptance test. The R2 numbers (47.4 and 17.2 cm3) were measured as the plain union
    and intersection of the 10 raw masks; the raw masks of this pipeline give the same within 5 %."""
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
    assert set(np.unique(occ).tolist()) <= {int(round(255 * k / 10)) for k in range(11)}
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
    # the sky's target: a point of the highest occupancy, inside the core
    p = np.array(table.argmax_rho_mm)
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
