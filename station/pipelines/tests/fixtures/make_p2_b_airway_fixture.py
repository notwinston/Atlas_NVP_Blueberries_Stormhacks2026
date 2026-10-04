"""Writes the committed fixtures of the engine-free airway core (P2-B01 / P2-B06 / P2-B08), into tests/fixtures/p2_b_airway/.
Model-generated numbers only; no patient data. Run from station/pipelines/ with PYTHONPATH=. :

    python tests/fixtures/make_p2_b_airway_fixture.py [output directory]

Writes
    tree.json, greedy.json
        What `atlas case airway` writes for the default seed 101 (airway.build_tree, airway.greedy_study, canonical
        JSON): the same bytes as cases/lung101/airway/ of a built package. About 350 kB together.
    resolver_ref_200.txt
        200 seeded random addresses of generations 11 to 16 and the node the Python resolver gives for each, every
        double written as the 16 hex digits of its IEEE-754 bits (big-endian), so the C# test can compare bit for bit:

            <address> <radius_mm> <len_mm> <pos x> <pos y> <pos z> <dir x> <dir y> <dir z> <normal x> <normal y> <normal z>

        Committed so that the C# test runs without Python. tests/test_p2_b_airway_core.py generates a second list of
        2,000 addresses at test time with ``reference_lines``.

tests/test_p2_b_airway_core.py asserts that the committed files equal what this script writes.
"""

from __future__ import annotations

import random
import struct
import sys
from pathlib import Path

from atlas_pipelines import airway, contract

TREE_SEED = airway.DEFAULT_SEED
SEED_REF = 20261004
N_REF = 200
GEN_LO, GEN_HI = 11, 16
REF_NAME = f"resolver_ref_{N_REF}.txt"


def _hex(v: float) -> str:
    return struct.pack(">d", float(v)).hex()


def random_addresses(rng_seed: int, n: int, gen_lo: int = GEN_LO, gen_hi: int = GEN_HI) -> list[str]:
    rng = random.Random(rng_seed)
    out = []
    for _ in range(n):
        gen = rng.randint(gen_lo, gen_hi)
        out.append("".join(rng.choice("LR") for _ in range(gen)))
    return out


def reference_lines(tree_seed: int, rng_seed: int, n: int, gen_lo: int = GEN_LO, gen_hi: int = GEN_HI) -> list[str]:
    """Header line "seed <tree seed>", then one line per address with the Python resolver's node in hex doubles.
    Each address is resolved by a fresh call of ``airway.resolve`` (no state shared between addresses)."""
    lines = [f"seed {tree_seed}"]
    for a in random_addresses(rng_seed, n, gen_lo, gen_hi):
        node = airway.resolve(tree_seed, a)
        values = [node.radius_mm, node.len_mm, *node.pos_mm, *node.dir, *node.normal]
        lines.append(a + " " + " ".join(_hex(v) for v in values))
    return lines


def files() -> dict[str, bytes]:
    ref = ("# Python resolver (atlas_pipelines.airway.resolve), rule atlas.airway/2; written by make_p2_b_airway_fixture.py\n"
           f"# {N_REF} random addresses of generations {GEN_LO} to {GEN_HI}, random.Random({SEED_REF}); doubles as IEEE-754 hex\n"
           + "\n".join(reference_lines(TREE_SEED, SEED_REF, N_REF)) + "\n")
    return {
        "tree.json": contract.dumps_canonical(airway.build_tree(TREE_SEED)),
        "greedy.json": contract.dumps_canonical(airway.greedy_study(TREE_SEED, TREE_SEED)),
        REF_NAME: ref.encode("ascii"),
    }


def write(out_dir: Path) -> dict[str, bytes]:
    out_dir.mkdir(parents=True, exist_ok=True)
    data = files()
    for rel, blob in data.items():
        (out_dir / rel).write_bytes(blob)
    return data


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "p2_b_airway"
    for name, blob in write(target).items():
        print(f"{target / name}: {len(blob)} bytes")
