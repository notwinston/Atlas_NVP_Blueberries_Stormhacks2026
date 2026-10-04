"""Model-generated airway tree (P2-D07): generator, address resolver, Two Compasses search, greedy study.

The tree is a model. It is not derived from any scan and every node carries ``source: "model"``.

Rule "atlas.airway/1" (docs/case_contract.md section 10 states it for the C# re-implementation)
----------------------------------------------------------------------------------------------
A node is one branch. Its address is a string over {L, R}; the trachea is the empty string and
``gen = len(address)``. Every value of a node is a pure function of ``(seed, address)``:

    trachea ("")   radius 9 mm, length 120 mm, start (0, 0, 0), dir (0, 0, -1), normal (1, 0, 0)
    child A + c    h   = SHA-256(ASCII "atlas.airway/1|<seed>|<A + c>")
                   u1  = (big-endian u64 of h[0:8]  >> 11) / 2^53          in [0, 1)
                   u2  = (big-endian u64 of h[8:16] >> 11) / 2^53          in [0, 1)
                   radius = radius(A) * 2^(-1/3) * (0.9 + 0.2 * u1)        diameter ratio 0.794, jitter +/- 10 %
                   len    = 2 * radius * (2 + 2 * u2)                      length / diameter = 3 +/- 1
                   normal = dir(A) x normal(A)                             branching plane turned 90 degrees
                   dir    = dir(A) * cos(37 deg) + s * (normal x dir(A)) * sin(37 deg),  s = +1 for L, -1 for R
                   pos    = pos(A) + len * dir

``pos_mm`` is the distal end of the branch, which is the point where it forks into its two children;
the branch starts at its parent's ``pos_mm``. The frame is the model's own, in millimetres, axes named
as in LPS (+x left, +y posterior, +z superior); it is not registered to any patient.

Because a node depends on nothing but the seed and its address, any address is resolvable without
holding the tree: ``resolve(seed, address)`` walks the address from the trachea (at most 23 steps).
"""

from __future__ import annotations

import hashlib
import math
from dataclasses import dataclass
from typing import Iterable, Sequence

from . import contract, labels

RULE = "atlas.airway/1"
GREEDY_RULE = "atlas.greedy/1"

DEFAULT_SEED = 101
ADDRESSABLE_GEN = 16  # acceptance: any address up to this length resolves
MAX_ADDRESS_LEN = 23  # the plan's addressable depth (10.2.3); the resolver accepts up to this
MATERIALISED_GEN = 10  # nodes written to tree.json: generations 0..10 = 2,047 branches (R2 C.4)
ROUTE_FORKS = 4  # the "four forks" route of the Lung Console
GREEDY_TARGETS = 1000
GREEDY_TARGET_GEN = 16

TRACHEA_DIAMETER_MM = 18.0
TRACHEA_LENGTH_MM = 120.0
DIAMETER_RATIO = 2.0 ** (-1.0 / 3.0)  # 0.7937
DIAMETER_JITTER = 0.1  # ratio * (1 - j .. 1 + j)
LENGTH_OVER_DIAMETER = 3.0
LENGTH_OVER_DIAMETER_SPREAD = 1.0  # 3 +/- 1
BRANCH_ANGLE_DEG = 37.0
PLANE_ROTATION_DEG = 90.0

_COS = math.cos(math.radians(BRANCH_ANGLE_DEG))
_SIN = math.sin(math.radians(BRANCH_ANGLE_DEG))
_TWO53 = float(2**53)

Vec = tuple[float, float, float]


class AddressError(ValueError):
    """The string is not an airway address."""


@dataclass(frozen=True)
class Node:
    address: str
    radius_mm: float
    len_mm: float
    pos_mm: Vec  # distal end = fork point
    dir: Vec  # unit vector, proximal to distal
    normal: Vec  # normal of the plane in which this branch left its parent (not written to the file)

    @property
    def gen(self) -> int:
        return len(self.address)

    def to_json(self) -> dict:
        return {
            "address": self.address,
            "gen": self.gen,
            "len_mm": float(self.len_mm),
            "radius_mm": float(self.radius_mm),
            "pos_mm": [float(v) for v in self.pos_mm],
            "dir": [float(v) for v in self.dir],
            "source": labels.AIRWAY_NODE_SOURCE,
        }


def check_address(address: str, max_len: int = MAX_ADDRESS_LEN) -> str:
    if not isinstance(address, str):
        raise AddressError(f"an address is a string over {{L, R}}, got {type(address).__name__}")
    if len(address) > max_len:
        raise AddressError(f"address {address!r} is longer than {max_len}")
    if address.strip("LR"):
        raise AddressError(f"address {address!r} has characters other than L and R")
    return address


def node_index(address: str) -> int:
    """Index of a materialised node in tree.json ``nodes``: (2^gen - 1) + the address read as binary, L = 0, R = 1."""
    v = 0
    for c in address:
        v = 2 * v + (1 if c == "R" else 0)
    return (1 << len(address)) - 1 + v


def uniforms(seed: int, address: str) -> tuple[float, float]:
    h = hashlib.sha256(f"{RULE}|{seed}|{address}".encode("ascii")).digest()
    u1 = (int.from_bytes(h[0:8], "big") >> 11) / _TWO53
    u2 = (int.from_bytes(h[8:16], "big") >> 11) / _TWO53
    return u1, u2


def _cross(a: Vec, b: Vec) -> Vec:
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _dot(a: Sequence[float], b: Sequence[float]) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _unit(a: Vec) -> Vec:
    n = math.sqrt(_dot(a, a))
    return (a[0] / n, a[1] / n, a[2] / n)


def trachea() -> Node:
    return Node(
        address="",
        radius_mm=TRACHEA_DIAMETER_MM / 2.0,
        len_mm=TRACHEA_LENGTH_MM,
        pos_mm=(0.0, 0.0, -TRACHEA_LENGTH_MM),
        dir=(0.0, 0.0, -1.0),
        normal=(1.0, 0.0, 0.0),
    )


def child(seed: int, parent: Node, letter: str) -> Node:
    address = parent.address + letter
    u1, u2 = uniforms(seed, address)
    radius = parent.radius_mm * DIAMETER_RATIO * (1.0 - DIAMETER_JITTER + 2.0 * DIAMETER_JITTER * u1)
    length = 2.0 * radius * (LENGTH_OVER_DIAMETER - LENGTH_OVER_DIAMETER_SPREAD + 2.0 * LENGTH_OVER_DIAMETER_SPREAD * u2)
    normal = _unit(_cross(parent.dir, parent.normal))
    side = _cross(normal, parent.dir)
    s = _SIN if letter == "L" else -_SIN
    d = _unit(tuple(parent.dir[i] * _COS + side[i] * s for i in range(3)))
    pos = tuple(parent.pos_mm[i] + length * d[i] for i in range(3))
    return Node(address=address, radius_mm=radius, len_mm=length, pos_mm=pos, dir=d, normal=normal)


class Resolver:
    """Address -> node for one seed, with a memo so that shared prefixes are computed once."""

    def __init__(self, seed: int = DEFAULT_SEED):
        self.seed = int(seed)
        self._memo: dict[str, Node] = {"": trachea()}

    def node(self, address: str) -> Node:
        check_address(address)
        hit = self._memo.get(address)
        if hit is not None:
            return hit
        # longest known prefix, then walk down
        k = len(address) - 1
        while address[:k] not in self._memo:
            k -= 1
        n = self._memo[address[:k]]
        for c in address[k:]:
            n = child(self.seed, n, c)
            self._memo[n.address] = n
        return n

    def forget(self, min_len: int) -> None:
        """Drop memoised nodes with addresses of at least ``min_len`` letters (bounds memory in the study)."""
        self._memo = {a: n for a, n in self._memo.items() if len(a) < min_len}


def resolve(seed: int, address: str) -> Node:
    """The node at ``address`` for ``seed``. Pure: same arguments, same node."""
    return Resolver(seed).node(address)


def addresses_of_gen(gen: int) -> Iterable[str]:
    for v in range(1 << gen):
        yield "".join("R" if (v >> (gen - 1 - i)) & 1 else "L" for i in range(gen))


def materialise(resolver: Resolver, max_gen: int = MATERIALISED_GEN) -> list[Node]:
    """Generations 0..max_gen in the order of ``node_index``."""
    return [resolver.node(a) for g in range(max_gen + 1) for a in addresses_of_gen(g)]


# --------------------------------------------------------------------------------------------
# Compasses
# --------------------------------------------------------------------------------------------
def beacon_choice(fork_address: str, target_address: str) -> str:
    """The beacon: the next letter of the target's address after the fork's (exact prefix comparison)."""
    if not target_address.startswith(fork_address) or len(target_address) <= len(fork_address):
        raise AddressError(f"fork {fork_address!r} is not a proper prefix of target {target_address!r}")
    return target_address[len(fork_address)]


def compass_scores(fork_pos: Sequence[float], target_pos: Sequence[float],
                   dir_l: Sequence[float], dir_r: Sequence[float]) -> tuple[float, float]:
    """Dot products of the two children's directions with the straight line from the fork to the target
    (not normalised: only their order matters)."""
    t = (target_pos[0] - fork_pos[0], target_pos[1] - fork_pos[1], target_pos[2] - fork_pos[2])
    return _dot(dir_l, t), _dot(dir_r, t)


def compass_choice(fork_pos, target_pos, dir_l, dir_r) -> str:
    """The Euclidean compass: the child whose direction is closer to the straight line to the target.
    "L" or "R"; "" on an exact tie (a tie is never counted as wrong)."""
    sl, sr = compass_scores(fork_pos, target_pos, dir_l, dir_r)
    return "L" if sl > sr else "R" if sr > sl else ""


def compass_wrong(resolver: Resolver, fork_address: str, target_address: str) -> bool:
    fork = resolver.node(fork_address)
    target = resolver.node(target_address)
    c = compass_choice(fork.pos_mm, target.pos_mm,
                       resolver.node(fork_address + "L").dir, resolver.node(fork_address + "R").dir)
    return c != "" and c != beacon_choice(fork_address, target_address)


def route_forks(target_address: str) -> list[str]:
    """The forks walked from the trachea to the target: every proper prefix of its address."""
    return [target_address[:k] for k in range(len(target_address))]


def wrong_forks(resolver: Resolver, target_address: str) -> list[str]:
    return [f for f in route_forks(target_address) if compass_wrong(resolver, f, target_address)]


def choose_target(resolver: Resolver, forks: int = ROUTE_FORKS, target_gen: int = ROUTE_FORKS) -> tuple[str, str] | None:
    """The first address of generation ``target_gen`` (in ``node_index`` order) for which exactly one of the
    first ``forks`` forks on its route is a fork at which the Euclidean compass is wrong.
    Returns (target address, that fork's address) or None when no address qualifies."""
    for a in addresses_of_gen(target_gen):
        w = [f for f in route_forks(a)[:forks] if compass_wrong(resolver, f, a)]
        if len(w) == 1:
            return a, w[0]
    return None


def fallback_target(target_gen: int = ROUTE_FORKS) -> str:
    """The target written when no address qualifies: the first address of the generation."""
    return "L" * target_gen


# --------------------------------------------------------------------------------------------
# Greedy study
# --------------------------------------------------------------------------------------------
def greedy_target(study_seed: int, i: int, gen: int = GREEDY_TARGET_GEN) -> str:
    """Target ``i`` of the study: bit j (most significant first) of SHA-256("atlas.greedy/1|<seed>|<i>")
    gives letter j, 0 = L and 1 = R."""
    h = hashlib.sha256(f"{GREEDY_RULE}|{study_seed}|{i}".encode("ascii")).digest()
    bits = int.from_bytes(h, "big")
    return "".join("R" if (bits >> (255 - j)) & 1 else "L" for j in range(gen))


def greedy_study(tree_seed: int, study_seed: int, n_targets: int = GREEDY_TARGETS,
                 gen: int = GREEDY_TARGET_GEN) -> dict:
    """For every fork on the route to each of ``n_targets`` seeded random targets of generation ``gen``:
    does the Euclidean compass pick the child that is not on the route? Returns the greedy.json object."""
    resolver = Resolver(tree_seed)
    forks_by_gen = [0] * gen
    wrong_by_gen = [0] * gen
    routes_with_wrong = 0
    targets = [greedy_target(study_seed, i, gen) for i in range(n_targets)]
    for t in targets:
        any_wrong = False
        for f in route_forks(t):
            forks_by_gen[len(f)] += 1
            if compass_wrong(resolver, f, t):
                wrong_by_gen[len(f)] += 1
                any_wrong = True
        routes_with_wrong += 1 if any_wrong else 0
        resolver.forget(9)
    forks = sum(forks_by_gen)
    wrong = sum(wrong_by_gen)
    return {
        "schema": contract.SCHEMA_GREEDY,
        "label": labels.LABEL_AIRWAY,
        "tree_rule": RULE,
        "tree_seed": int(tree_seed),
        "seed": int(study_seed),
        "method": {
            "rule": GREEDY_RULE,
            "targets": "target i = the first target_gen bits, most significant first, of "
                       "SHA-256(ASCII 'atlas.greedy/1|<seed>|<i>'), 0 = L, 1 = R; i = 0..n_targets-1; "
                       "repeats are kept",
            "forks": "every proper prefix of the target address (generations 0..target_gen-1), "
                     "the last fork before the target included",
            "compass": "at a fork, t = pos_mm(target) - pos_mm(fork); the Euclidean compass picks the child "
                       "with the larger dot(dir(child), t); the fork is wrong when that child is not the next "
                       "letter of the target address; an exact tie is not wrong",
            "share": "wrong forks / forks",
        },
        "n_targets": int(n_targets),
        "n_distinct_targets": len(set(targets)),
        "target_gen": int(gen),
        "forks": int(forks),
        "wrong_forks": int(wrong),
        "wrong_share": float(wrong / forks),
        "routes_with_wrong_fork": int(routes_with_wrong),
        "by_generation": [
            {"gen": g, "forks": forks_by_gen[g], "wrong_forks": wrong_by_gen[g],
             "wrong_share": float(wrong_by_gen[g] / forks_by_gen[g])}
            for g in range(gen)
        ],
    }


# --------------------------------------------------------------------------------------------
# tree.json
# --------------------------------------------------------------------------------------------
def build_tree(seed: int = DEFAULT_SEED, materialised_gen: int = MATERIALISED_GEN,
               target_gen: int = ROUTE_FORKS) -> dict:
    """The tree.json object.

    The target is a node of generation ``target_gen``; the four-fork route is the first four forks from the
    trachea towards it (with the default ``target_gen`` = 4 it ends at the target). When no target has
    exactly one Two Compasses fork on that route, ``compass_fork.address`` is null and the target is
    ``fallback_target``: the file never names a fork at which the compass is not actually wrong."""
    if not ROUTE_FORKS <= target_gen <= MAX_ADDRESS_LEN:
        raise ValueError(f"target_gen must be {ROUTE_FORKS}..{MAX_ADDRESS_LEN}")
    resolver = Resolver(seed)
    chosen = choose_target(resolver, ROUTE_FORKS, target_gen)
    target, fork = chosen if chosen is not None else (fallback_target(target_gen), None)
    four = route_forks(target)[:ROUTE_FORKS]
    wrong = [f for f in four if compass_wrong(resolver, f, target)]
    nodes = materialise(resolver, materialised_gen)
    node_list = [n.to_json() for n in nodes]
    extra_nodes = []
    if target_gen > materialised_gen:
        # the route to a deeper target is written too, so that the compass can be checked from the file alone
        for k in range(materialised_gen + 1, target_gen + 1):
            parent = target[:k - 1]
            extra_nodes += [resolver.node(parent + "L").to_json(), resolver.node(parent + "R").to_json()]
    tree = {
        "schema": contract.SCHEMA_TREE,
        "label": labels.LABEL_AIRWAY,
        "rule": RULE,
        "seed": int(seed),
        "frame": "model frame in mm, not registered to a patient; +x left, +y posterior, +z superior; "
                 "the trachea starts at (0, 0, 0)",
        "generator": {
            "trachea_diameter_mm": TRACHEA_DIAMETER_MM,
            "trachea_length_mm": TRACHEA_LENGTH_MM,
            "diameter_ratio": float(DIAMETER_RATIO),
            "diameter_jitter": DIAMETER_JITTER,
            "length_over_diameter": LENGTH_OVER_DIAMETER,
            "length_over_diameter_spread": LENGTH_OVER_DIAMETER_SPREAD,
            "branch_angle_deg": BRANCH_ANGLE_DEG,
            "plane_rotation_deg": PLANE_ROTATION_DEG,
        },
        "addressable_gen": ADDRESSABLE_GEN,
        "max_address_len": MAX_ADDRESS_LEN,
        "materialised_gen": int(materialised_gen),
        "nodes": node_list,
        "target": {"address": target},
        "compass_fork": {"address": fork},
        "routes": {
            "four_forks": {"forks": four, "end": target[:ROUTE_FORKS], "compass_wrong_forks": wrong},
            "one_fork": {"forks": [target[:-1]], "end": target},
        },
    }
    if extra_nodes:
        tree["target_route_nodes"] = extra_nodes
    return tree
