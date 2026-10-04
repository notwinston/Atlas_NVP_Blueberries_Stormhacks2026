"""P2-D01 case contract (Master Plan 10.2.8).

Acceptance test: "A 2-tet fixture round-trips Python -> C# byte-identical" for tube4d.bin,
table.json and manifest.json. Plus Python-only round trips, header checks, the canonical JSON
rules, and the shared foundation modules (cli discovery, cache, glb, labels).

The C# part needs the .NET SDK. It is looked up on PATH, in $DOTNET_ROOT and in ~/.dotnet; when
none is found the C# tests are skipped with that reason (they are never silently passed).
"""

from __future__ import annotations

import importlib.util
import os
import random
import shutil
import struct
import subprocess
from pathlib import Path

import numpy as np
import pytest

from atlas_pipelines import cache, cli, contract, glb, labels, paths

FIXTURE_FILES = {"tube4d.bin": "tube4d", "table.json": "table", "manifest.json": "manifest"}


@pytest.fixture(scope="session")
def fx(fixtures_dir) -> Path:
    return fixtures_dir / "p2_d01"


@pytest.fixture(scope="session")
def make_fixture(fixtures_dir):
    spec = importlib.util.spec_from_file_location("make_p2_d01_fixture", fixtures_dir / "make_p2_d01_fixture.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# =============================================================================================
# C# toolchain
# =============================================================================================
def _find_dotnet() -> str | None:
    candidates = []
    on_path = shutil.which("dotnet")
    if on_path:
        candidates.append(Path(on_path))
    if os.environ.get("DOTNET_ROOT"):
        candidates.append(Path(os.environ["DOTNET_ROOT"]) / "dotnet")
    candidates.append(Path.home() / ".dotnet" / "dotnet")
    for c in candidates:
        if c.is_file() and os.access(c, os.X_OK):
            return str(c)
    return None


def _dotnet_env(exe: str) -> dict:
    env = dict(os.environ)
    env.setdefault("DOTNET_ROOT", str(Path(exe).resolve().parent))
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"  # the SDK itself aborts without ICU otherwise
    return env


@pytest.fixture(scope="session")
def csharp(pipelines_root):
    """Builds the C# projects once; returns run(project, *args) -> CompletedProcess."""
    exe = _find_dotnet()
    if exe is None:
        pytest.skip("C# round trip NOT RUN: no .NET SDK found on PATH, in $DOTNET_ROOT or in ~/.dotnet "
                    "(install with dotnet-install.sh --channel LTS --install-dir ~/.dotnet)")
    env = _dotnet_env(exe)
    root = pipelines_root / "csharp"
    dlls = {}
    for name in ("Atlas.Cases.RoundTrip", "Atlas.Cases.Tests"):
        proj = root / name / f"{name}.csproj"
        build = subprocess.run([exe, "build", str(proj), "-c", "Release", "-v", "quiet"],
                               capture_output=True, text=True, env=env)
        assert build.returncode == 0, f"dotnet build {name} failed:\n{build.stdout}\n{build.stderr}"
        found = sorted((root / name / "bin" / "Release").glob(f"*/{name}.dll"))
        assert found, f"{name}.dll not produced"
        dlls[name] = found[0]

    def run(project: str, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run([exe, str(dlls[project]), *map(str, args)], capture_output=True, text=True, env=env)

    return run


# =============================================================================================
# Acceptance: 2-tet fixture, Python -> C# -> bytes byte-identical
# =============================================================================================
def test_fixture_is_two_tets_written_by_python(fx, make_fixture, tmp_path):
    tube = contract.read_tube4d(fx / "tube4d.bin")
    assert tube.n_tets == 2
    assert tube.M == 15.0 and tube.Lambda == 1.5
    # the committed bytes are exactly what the Python writers produce today
    fresh = make_fixture.write(tmp_path / "regen")
    assert set(fresh) == set(FIXTURE_FILES)
    for name, data in fresh.items():
        assert (fx / name).read_bytes() == data, f"{name}: committed fixture differs from the Python writer"


@pytest.mark.parametrize("name", sorted(FIXTURE_FILES))
def test_acceptance_python_to_csharp_byte_identical(csharp, fx, tmp_path, name):
    src = fx / name
    dst = tmp_path / name
    proc = csharp("Atlas.Cases.RoundTrip", FIXTURE_FILES[name], src, dst)
    assert proc.returncode == 0, proc.stderr
    assert dst.read_bytes() == src.read_bytes(), f"{name}: C# output differs from the Python bytes"


def test_csharp_unit_tests_pass(csharp, fx):
    proc = csharp("Atlas.Cases.Tests", fx)
    assert proc.returncode == 0, f"{proc.stdout}\n{proc.stderr}"
    assert " 0 failed" in proc.stdout


def test_csharp_hash_check_of_fixture(csharp, fx):
    proc = csharp("Atlas.Cases.RoundTrip", "verify", fx)
    assert proc.returncode == 0, proc.stderr


def test_csharp_and_python_agree_on_canonical_json(csharp, tmp_path):
    """Number formatting, key order and string escaping agree on 3,000 random and edge-case values."""
    rng = random.Random(20261004)
    floats = [0.0, -0.0, 0.5e-6, 1.5e-6, 2.5e-6, -2.5e-6, 0.1, 0.2, 0.3, 1 / 3, 2 / 3, 15.0, 1.5, 47.4, 17.2,
              4.42, 3.87, 0.9999995, 0.9999994999, 123456.7890125, 123456.7890135, 9007199254.740990,
              -9007199254.740990, 1e-300, 5e-324, 1e9, 1.0000005, 1.0000015, 0.0000005000000001]
    floats += [rng.uniform(-1, 1) for _ in range(1000)]
    floats += [rng.uniform(-500, 500) for _ in range(1000)]
    floats += [(rng.randrange(-10**9, 10**9) + 0.5) / 1e6 for _ in range(500)]  # ties and near-ties
    floats += [rng.uniform(-1e9, 1e9) for _ in range(500)]
    doc = {
        "floats": floats,
        "ints": [0, -1, 1, 2**53, -(2**63), 2**63 - 1, 600, 18000],
        "strings": ["", "plain", 'quote " backslash \\ slash /', "tab\tnl\ncr\rbs\bff\f", "\x00\x01\x1f\x7f",
                    "é Λ τ ρ – 中", "\U0001F600"],
        "nested": {"b": [], "a": {}, "Z": None, "t": True, "f": False, "~": 1, " ": 2, "a b": [[1.0], [[2]]]},
    }
    # hand the C# side a deliberately non-canonical spelling of the same values
    import json
    src = tmp_path / "in.json"
    src.write_text(json.dumps(doc, indent=3, ensure_ascii=True), encoding="utf-8")
    dst = tmp_path / "out.json"
    proc = csharp("Atlas.Cases.RoundTrip", "json", src, dst)
    assert proc.returncode == 0, proc.stderr
    want = contract.dumps_canonical(contract.loads_json(src.read_bytes()))
    assert want == contract.dumps_canonical(doc)
    assert dst.read_bytes() == want


def test_csharp_rejects_bad_tube(csharp, fx, tmp_path):
    data = bytearray((fx / "tube4d.bin").read_bytes())
    data[0:4] = b"ATL3"
    bad = tmp_path / "bad.bin"
    bad.write_bytes(data)
    proc = csharp("Atlas.Cases.RoundTrip", "tube4d", bad, tmp_path / "o.bin")
    assert proc.returncode == 1 and "bad magic" in proc.stderr


def test_csharp_sources_have_no_unity_or_third_party_references(pipelines_root):
    lib = pipelines_root / "csharp" / "Atlas.Cases"
    sources = sorted(lib.glob("*.cs"))
    assert {p.name for p in sources} >= {"CanonicalJson.cs", "Tube4D.cs", "CaseTable.cs", "CaseManifest.cs"}
    for p in sources:
        text = p.read_text(encoding="utf-8")
        assert "namespace Atlas.Cases" in text
        for line in text.splitlines():
            if line.startswith("using "):
                assert line.startswith("using System"), f"{p.name}: {line}"
        assert "UnityEngine" not in text and "Newtonsoft" not in text and "System.Text.Json" not in text
    assert "PackageReference" not in (lib / "Atlas.Cases.csproj").read_text(encoding="utf-8")


# =============================================================================================
# tube4d.bin: header and round trip (Python)
# =============================================================================================
def test_tube4d_header_layout(fx):
    data = (fx / "tube4d.bin").read_bytes()
    assert contract.TUBE_HEADER_SIZE == 24
    assert data[0:4] == b"ATL4"
    assert struct.unpack_from("<H", data, 4)[0] == 1  # version
    n = struct.unpack_from("<I", data, 6)[0]
    m = struct.unpack_from("<I", data, 10)[0]
    layers = struct.unpack_from("<H", data, 14)[0]
    M, lam = struct.unpack_from("<ff", data, 16)
    assert (n, m, layers) == (5, 2, 2)
    assert M == 15.0 and lam == 1.5
    assert len(data) == 24 + 16 * n + 17 * m == 138  # no padding anywhere
    # P: float32 n x 4 directly after the header; T: int32 m x 4; slab: u8 per tet
    P = np.frombuffer(data, "<f4", 4 * n, 24).reshape(n, 4)
    T = np.frombuffer(data, "<i4", 4 * m, 24 + 16 * n).reshape(m, 4)
    slab = np.frombuffer(data, np.uint8, m, 24 + 16 * n + 16 * m)
    assert P[4].tolist() == [np.float32(-0.1), np.float32(-0.2), np.float32(0.3), 1.5]
    assert T.tolist() == [[0, 1, 2, 3], [1, 2, 3, 4]]
    assert slab.tolist() == [0, 1]


def test_tube4d_python_round_trip(fx, tmp_path):
    data = (fx / "tube4d.bin").read_bytes()
    tube = contract.tube4d_from_bytes(data)
    assert tube.P.dtype == np.dtype("<f4") and tube.T.dtype == np.dtype("<i4") and tube.slab.dtype == np.uint8
    assert contract.tube4d_to_bytes(tube) == data
    out = tmp_path / "t.bin"
    contract.write_tube4d(out, tube)
    assert out.read_bytes() == data
    again = contract.read_tube4d(out)
    assert np.array_equal(again.P, tube.P) and np.array_equal(again.T, tube.T) and np.array_equal(again.slab, tube.slab)


def test_tube4d_reader_rejects_malformed(fx):
    data = (fx / "tube4d.bin").read_bytes()
    with pytest.raises(contract.ContractError, match="magic"):
        contract.tube4d_from_bytes(b"ATL5" + data[4:])
    with pytest.raises(contract.ContractError, match="version"):
        contract.tube4d_from_bytes(data[:4] + b"\x02\x00" + data[6:])
    with pytest.raises(contract.ContractError, match="bytes"):
        contract.tube4d_from_bytes(data[:-1])
    with pytest.raises(contract.ContractError, match="bytes"):
        contract.tube4d_from_bytes(data + b"\x00")
    with pytest.raises(contract.ContractError, match="slab"):
        contract.tube4d_from_bytes(data[:-1] + b"\x02")
    with pytest.raises(contract.ContractError, match="index"):
        bad = bytearray(data)
        struct.pack_into("<i", bad, 24 + 16 * 5, 5)
        contract.tube4d_from_bytes(bytes(bad))


def test_prisms_to_tets_matches_plan():
    # Master Plan A.8: (a_k, b_k, c_k, c_k+1), (a_k, b_k, b_k+1, c_k+1), (a_k, a_k+1, b_k+1, c_k+1), a < b < c
    T, slab = contract.prisms_to_tets(np.array([[2, 0, 1]]), n_template_verts=3, layers=1)
    assert T.tolist() == [[0, 1, 2, 5], [0, 1, 4, 5], [0, 3, 4, 5]]
    assert slab.tolist() == [0, 0, 0]


def test_template_600_triangles_gives_18000_crack_free_tets():
    """600 triangles x 10 layers x 3 = 18,000 tets, and every interior face is shared by exactly 2."""
    # an icosahedron subdivided to 320 faces has no 600-triangle level, so build a closed 600-triangle
    # surface directly: a 15 x 20 torus grid of quads split into 2 triangles each
    nu, nv = 15, 20
    faces = []
    for i in range(nu):
        for j in range(nv):
            a, b = i * nv + j, i * nv + (j + 1) % nv
            c, d = ((i + 1) % nu) * nv + j, ((i + 1) % nu) * nv + (j + 1) % nv
            faces += [[a, b, d], [a, d, c]]
    F = np.array(faces)
    assert len(F) == contract.TEMPLATE_TRIS == 600
    nV = nu * nv
    u = np.repeat(np.arange(nu), nv) * 2 * np.pi / nu
    v = np.tile(np.arange(nv), nu) * 2 * np.pi / nv
    V = np.stack([(0.05 + 0.02 * np.cos(v)) * np.cos(u), (0.05 + 0.02 * np.cos(v)) * np.sin(u), 0.02 * np.sin(v)], 1)
    tube = contract.tube_from_phases([V + [0, 0, 0.001 * k] for k in range(10)], F)
    assert tube.n_tets == 18000
    assert tube.n_verts == 11 * nV and tube.layers == 10
    contract.check_tube_layout(tube, nV)
    assert tube.P[:, 3].max() == np.float32(1.5) and tube.P[:, 3].min() == 0.0
    assert np.array_equal(tube.P[:nV, :3], tube.P[10 * nV:, :3])  # phase 10 repeats phase 0
    # face sharing: a face is interior unless it lies entirely in ring 0 or ring 10
    tri = np.sort(np.concatenate([tube.T[:, [0, 1, 2]], tube.T[:, [0, 1, 3]], tube.T[:, [0, 2, 3]],
                                  tube.T[:, [1, 2, 3]]]), axis=1)
    uniq, counts = np.unique(tri, axis=0, return_counts=True)
    ring = uniq // nV
    cap = ((ring == 0).all(axis=1)) | ((ring == 10).all(axis=1))
    assert (counts[~cap] == 2).all()
    assert (counts[cap] == 1).all() and cap.sum() == 2 * 600
    # and it survives the file format
    assert contract.tube4d_to_bytes(contract.tube4d_from_bytes(contract.tube4d_to_bytes(tube))) == contract.tube4d_to_bytes(tube)
    assert len(contract.tube4d_to_bytes(tube)) == 24 + 16 * tube.n_verts + 17 * 18000


def test_display_frame_mapping():
    origin = [10.0, -20.0, 30.0]
    p_mm = np.array([[12.0, -20.0, 29.0]])
    d = contract.to_display(p_mm, origin)  # M * (p - origin) / 1000, axes unchanged
    assert np.allclose(d, [[0.030, 0.0, -0.015]])
    assert np.allclose(contract.from_display(d, origin), p_mm)


# =============================================================================================
# canonical JSON, table.json, manifest.json (Python)
# =============================================================================================
@pytest.mark.parametrize("value, text", [
    (15.0, "15.0"), (1.5, "1.5"), (0.0, "0.0"), (-0.0, "0.0"), (-0.25, "-0.25"), (0.1, "0.1"), (47.4, "47.4"),
    (1234567.891234, "1234567.891234"), (4e-7, "0.0"), (-4e-7, "0.0"), (1.5e-6, "0.000002"), (1e-6, "0.000001"),
    (np.float32(1.5), "1.5"), (np.float64(17.2), "17.2"),
])
def test_canonical_float_format(value, text):
    assert contract.format_float(value) == text


def test_canonical_json_rules():
    obj = {"b": [1, 2.0, True, None, "x"], "a": 'q"\\\n\x01é', "B": -7}
    assert contract.dumps_canonical(obj) == '{"B":-7,"a":"q\\"\\\\\\n\\u0001é","b":[1,2.0,true,null,"x"]}\n'.encode("utf-8")
    assert contract.is_canonical(contract.dumps_canonical(obj))
    assert not contract.is_canonical(b'{"a": 1}\n')  # whitespace
    assert not contract.is_canonical(b'{"a":1}')  # no trailing LF
    assert not contract.is_canonical(b'{"b":1,"a":2}\n')  # unsorted keys
    assert not contract.is_canonical(b'{"a":1.50}\n')  # trailing zero
    for bad in (float("nan"), float("inf"), 1e10, {"é": 1}, {1: 2}, object()):
        with pytest.raises(contract.ContractError):
            contract.dumps_canonical(bad)
    with pytest.raises(contract.ContractError):
        contract.loads_json(b'{"a":1,"a":2}')
    with pytest.raises(contract.ContractError):
        contract.loads_json(b"\xef\xbb\xbf1")
    assert isinstance(contract.loads_json(b"15"), int) and isinstance(contract.loads_json(b"15.0"), float)


def test_fixture_json_files_are_canonical(fx):
    for name in ("table.json", "manifest.json"):
        data = (fx / name).read_bytes()
        assert contract.is_canonical(data), name
        assert data.endswith(b"\n") and b"\r" not in data and not data.startswith(b"\xef\xbb\xbf")


def test_table_python_round_trip_and_keys(fx, tmp_path):
    data = (fx / "table.json").read_bytes()
    table = contract.read_table(fx / "table.json")
    assert contract.write_table(tmp_path / "table.json", table) == data
    obj = contract.loads_json(data)
    assert set(obj) == {"schema", "id", "phases", "substeps", "gtv_cm3", "itv_cm3", "core_cm3", "gating_cm3",
                        "centroid_mm", "centroid_range_mm", "argmax_rho_mm", "max_rho", "origin_mm", "occ", "extra"}
    assert obj["schema"] == "atlas.table/1"
    assert len(obj["gtv_cm3"]) == 10
    assert len(obj["gating_cm3"]) == 10 and all(len(r) == 10 for r in obj["gating_cm3"])  # [start bin][length]
    assert len(obj["centroid_mm"]) == 10 and all(len(r) == 3 for r in obj["centroid_mm"])
    assert obj["occ"]["dims"] == [64, 64, 64]
    assert table.gating_cm3[3][0] == table.gtv_cm3[3]  # length index 0 = the single phase
    bad = dict(obj)
    bad["gating_cm3"] = obj["gating_cm3"][:9]
    with pytest.raises(contract.ContractError):
        contract.Table.from_obj(bad)
    bad = dict(obj, surprise=1)
    with pytest.raises(contract.ContractError, match="unknown"):
        contract.Table.from_obj(bad)


def test_manifest_python_round_trip_keys_and_hash_check(fx, tmp_path):
    data = (fx / "manifest.json").read_bytes()
    man = contract.read_manifest(fx / "manifest.json")
    assert contract.write_manifest(tmp_path / "manifest.json", man) == data
    obj = contract.loads_json(data)
    assert set(obj) == {"schema", "id", "kind", "label", "footer", "labels", "licence", "credit", "citations",
                        "M", "Lambda", "origin_mm", "files"}
    assert obj["schema"] == "atlas.case/1"
    assert obj["M"] == 15.0 and obj["Lambda"] == 1.5
    assert isinstance(obj["M"], float) and isinstance(obj["Lambda"], float)
    assert obj["kind"] in ("real", "illustrative")
    assert [f["path"] for f in obj["files"]] == ["table.json", "tube4d.bin"]  # sorted, manifest itself excluded
    assert set(obj["labels"]) == {f["path"] for f in obj["files"]}  # exactly one label per file
    for f in obj["files"]:
        assert set(f) == {"path", "sha256", "size"}
        assert f["sha256"] == cache.sha256_file(fx / f["path"]) and f["size"] == (fx / f["path"]).stat().st_size
    assert contract.verify_manifest(fx) == []
    # the hash check notices a changed byte, a missing file and an unlisted file
    pkg = tmp_path / "pkg"
    shutil.copytree(fx, pkg)
    raw = bytearray((pkg / "tube4d.bin").read_bytes())
    raw[30] ^= 1
    (pkg / "tube4d.bin").write_bytes(raw)
    assert contract.verify_manifest(pkg) == ["tube4d.bin: sha256 mismatch"]
    (pkg / "tube4d.bin").unlink()
    (pkg / "stray.txt").write_text("x")
    assert sorted(contract.verify_manifest(pkg)) == ["stray.txt: present but not listed in manifest.json",
                                                     "tube4d.bin: missing"]


def test_build_manifest_labels_real_case(tmp_path):
    pkg = tmp_path / "cases" / "lung101"
    for rel in ("tube4d.bin", "tube4d_rigid.bin", "phases.glb", "chest.glb", "table.json", "airway/tree.json",
                "airway/greedy.json", "sky/robust.r8", "sky/sky.json", "CREDITS.txt"):
        (pkg / rel).parent.mkdir(parents=True, exist_ok=True)
        (pkg / rel).write_bytes(rel.encode())
    (pkg / ".hidden").write_text("not part of the package")
    man = contract.build_manifest(pkg, "lung101", labels.KIND_REAL, [1.0, 2.0, 3.0])
    contract.write_manifest(pkg / "manifest.json", man)
    assert contract.verify_manifest(pkg) == []
    back = contract.read_manifest(pkg / "manifest.json")
    assert back.label == "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS."
    assert back.labels["tube4d_rigid.bin"] == "Simplified: rigid shape"
    assert back.labels["airway/tree.json"] == back.labels["airway/greedy.json"] == "Model-generated airway"
    assert back.labels["tube4d.bin"] == back.labels["sky/robust.r8"] == back.label
    assert back.licence == "CC BY 3.0" and "10.7937/K9/TCIA.2016.ELN8YGLE" in back.credit
    assert back.footer == "Education and research visualisation. Not for clinical use."
    assert back.M == 15.0 and back.Lambda == 1.5
    assert ".hidden" not in back.labels and "manifest.json" not in back.labels


# =============================================================================================
# labels
# =============================================================================================
def test_label_strings_are_exact():
    assert labels.LABEL_REAL == "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS."
    assert labels.LABEL_TWIN == "Illustrative model"  # plan 10.2.1: no trailing full stop
    assert labels.LABEL_RIGID == "Simplified: rigid shape"
    assert labels.AIRWAY_NODE_SOURCE == "model"
    assert labels.FOOTER == "Education and research visualisation. Not for clinical use."
    assert labels.OBJECT_NOUN == {"real": "target lesion", "illustrative": "nodule"}
    assert labels.case_label("real") == labels.LABEL_REAL and labels.case_label("illustrative") == labels.LABEL_TWIN
    assert all(labels.label_for("illustrative", p) == "Illustrative model" for p in ("tube4d.bin", "tube4d_rigid.bin"))
    assert paths.REAL_CASE == "lung101" and paths.TWIN_CASE == "twin"
    assert contract.M_DISPLAY == 15.0 and contract.LAMBDA_M == 1.5 and contract.PHASES == 10


# =============================================================================================
# foundation: CLI discovery, cache, GLB
# =============================================================================================
def test_cli_discovers_steps_and_runs_info(run_atlas, tmp_out):
    steps, broken = cli.discover_steps()
    assert "info" in steps
    proc = run_atlas("info", "--case", "twin", "--json", out=tmp_out)
    import json
    info = json.loads(proc.stdout)
    assert info["case"] == "twin" and "info" in info["steps"]
    assert Path(info["case_dir"]) == tmp_out / "cases" / "twin"
    assert cli.main(["case", "info", "--out", str(tmp_out)]) == 0
    assert paths.default_out_dir() == paths.PIPELINES_ROOT / "out" or "ATLAS_PIPELINES_OUT" in os.environ
    assert paths.default_cache_dir() == paths.PIPELINES_ROOT / ".cache" or "ATLAS_PIPELINES_CACHE" in os.environ


def test_cli_picks_up_a_new_step_module_without_editing_cli(tmp_path, monkeypatch, capsys):
    from atlas_pipelines import steps as steps_pkg

    extra = tmp_path / "extra_steps"
    extra.mkdir()
    (extra / "zz_demo.py").write_text(
        'NAME = "zz-demo"\nDEFAULT_CASE = "twin"\n'
        "def add_arguments(p):\n    p.add_argument('--n', type=int, default=3)\n"
        "def run(args):\n    print(f'demo {args.case} {args.n} {args.force} {args.out.name}')\n    return 0\n")
    (extra / "zz_broken.py").write_text("import a_module_that_does_not_exist\n")
    (extra / "_helper.py").write_text("raise RuntimeError('helpers are not imported by discovery')\n")
    monkeypatch.setattr(steps_pkg, "__path__", list(steps_pkg.__path__) + [str(extra)])
    steps, broken = cli.discover_steps()
    assert "zz-demo" in steps and "zz_broken" in broken and "info" in steps
    assert cli.main(["case", "zz-demo", "--n", "7", "--force", "--out", str(tmp_path / "o")]) == 0
    assert capsys.readouterr().out.strip() == "demo twin 7 True o"
    assert cli.main(["case", "zz_broken"]) == 2  # a broken step does not hide the others


def test_cache_skips_when_unchanged_and_reruns_when_anything_changes(tmp_path):
    src = tmp_path / "in.txt"
    src.write_text("one")
    out = tmp_path / "pkg" / "out.txt"
    stamp = tmp_path / "work" / "stamps" / "demo.json"
    calls = []

    def work():
        calls.append(1)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(src.read_text().upper())

    def go(version="v1", params=None, force=False):
        return cache.run_cached(stamp, version, params or {"a": 1}, [src], [out], work, force=force)

    assert go() is True and out.read_text() == "ONE"
    assert go() is False and len(calls) == 1  # cached
    assert go(params={"a": 2}) is True  # parameter change
    assert go(params={"a": 2}) is False
    assert go(version="v2", params={"a": 2}) is True  # code version change
    src.write_text("two")
    assert go(version="v2", params={"a": 2}) is True and out.read_text() == "TWO"  # input content change
    out.write_text("tampered")
    assert go(version="v2", params={"a": 2}) is True and out.read_text() == "TWO"  # output changed
    out.unlink()
    assert go(version="v2", params={"a": 2}) is True  # output missing
    n = len(calls)
    assert go(version="v2", params={"a": 2}, force=True) is True and len(calls) == n + 1  # --force
    assert go(version="v2", params={"a": 2}) is False
    with pytest.raises(FileNotFoundError):
        cache.run_cached(stamp, "v", {}, [tmp_path / "missing"], [out], work)
    with pytest.raises(FileNotFoundError):  # a step must write what it declares
        cache.run_cached(stamp, "v3", {}, [src], [tmp_path / "never_written"], work)


def test_cache_hashes_directories(tmp_path):
    d = tmp_path / "d"
    (d / "sub").mkdir(parents=True)
    (d / "a").write_text("1")
    (d / "sub" / "b").write_text("2")
    h1 = cache.sha256_path(d)
    (d / "sub" / "b").write_text("3")
    assert cache.sha256_path(d) != h1
    assert cache.compute_key("v", {"x": 1}, [d]) != cache.compute_key("v", {"x": 2}, [d])
    assert cache.sha256_bytes(b"") == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"


def test_glb_round_trip_with_morph_targets(tmp_path):
    V = np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]], dtype=np.float32) * 0.1
    F = np.array([[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]])
    N = V / np.maximum(np.linalg.norm(V, axis=1, keepdims=True), 1e-9)
    targets = {f"phase_{k}": np.full((4, 3), 0.001 * k, dtype=np.float32) for k in range(10)}
    path = tmp_path / "phases.bin"  # fixtures and temp files only: *.glb in the repo goes to Git LFS
    data = glb.write_glb(path, [glb.GlbMesh(positions=V, indices=F, normals=N, targets=targets, name="phases")])
    assert data[:4] == b"glTF" and struct.unpack_from("<II", data, 4) == (2, len(data)) and len(data) % 4 == 0
    assert glb.glb_bytes([glb.GlbMesh(positions=V, indices=F, normals=N, targets=targets, name="phases")]) == data
    (mesh,) = glb.read_glb(path)
    assert glb.triangle_count(path) == 4 and mesh.n_triangles == 4
    assert mesh.target_names == [f"phase_{k}" for k in range(10)] == glb.morph_target_names(data)[0]
    assert np.array_equal(mesh.positions, V) and np.array_equal(mesh.indices, F) and np.array_equal(mesh.normals, N)
    assert all(np.array_equal(mesh.targets[k], targets[k]) for k in targets)
    doc, _ = glb.parse_glb(data)
    assert doc["asset"]["version"] == "2.0"
    assert doc["meshes"][0]["extras"]["targetNames"] == list(targets)
    assert len(doc["meshes"][0]["primitives"][0]["targets"]) == 10 and doc["meshes"][0]["weights"] == [0.0] * 10
    pos_acc = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
    assert pos_acc["min"] == [0.0, 0.0, 0.0] and pos_acc["count"] == 4
    # trimesh (the pipeline's mesh library) reads the same geometry
    import trimesh
    tm = trimesh.load(path, file_type="glb", force="mesh", process=False)
    assert len(tm.faces) == 4 and np.allclose(np.asarray(tm.vertices), V)


def test_glb_empty_and_multi_mesh(tmp_path):
    empty = glb.write_glb(tmp_path / "core.bin", [])
    assert glb.read_glb(empty) == [] and glb.triangle_count(empty) == 0
    V = np.eye(3, dtype=np.float32)
    two = glb.glb_bytes([glb.GlbMesh(positions=V, indices=[[0, 1, 2]], name="lungs"),
                         glb.GlbMesh(positions=V * 2, indices=[[0, 1, 2], [2, 1, 0]], name="heart")])
    meshes = glb.read_glb(two)
    assert [m.name for m in meshes] == ["lungs", "heart"] and glb.triangle_count(two) == 3
    with pytest.raises(ValueError):
        glb.glb_bytes([glb.GlbMesh(positions=V, indices=[[0, 1, 3]])])
