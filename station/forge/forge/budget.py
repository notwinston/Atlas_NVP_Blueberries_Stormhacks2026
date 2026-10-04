"""The furniture budget (mesh design section 5.4, decision M9; Master Plan section 4.5).

At most 32 objects of a room receive generated meshes, the largest boxes first.
The room's 120,000 furniture triangles are shared between them by surface area,
3,000 to 15,000 each, and every mesh also has a light copy whose target is the
same for the whole room: clamp(60,000 / n, 1,500, 5,000).

The rule is written in whole numbers so that the phone (`ObjectMeshRules`), the
reference (`tools/rooms/roommath.py`) and this file give the same targets.
"""
from __future__ import annotations

import math
from typing import Iterable, NamedTuple, Sequence

MAX_GENERATED_PER_ROOM = 32
ROOM_TRIANGLE_BUDGET = 120_000
TARGET_MIN = 3_000
TARGET_MAX = 15_000

# The light copies of a room: the budget the furniture had before the full copies existed.
ROOM_LIGHT_BUDGET = 60_000
LIGHT_TARGET_MIN = 1_500
LIGHT_TARGET_MAX = 5_000

# The asset validator's hard ranges (section 5.4); a job may ask for any target inside them.
VALIDATOR_MIN_TRIANGLES = 1_500
VALIDATOR_MAX_TRIANGLES = 15_000
VALIDATOR_MAX_TRIANGLES_LIGHT = 5_000
MAX_EDGE_METRES = 0.25


class RoomPlan(NamedTuple):
    """Which boxes of a room receive meshes, and how many triangles each may have."""

    generated: list[int]  # largest first; the order the worker takes them in
    placeholders: list[int]  # beyond the 32 largest: they keep their first shape
    targets: dict[int, int]  # the full target of every box; 0 for a box that receives no mesh
    light: int  # the light target, the same for every mesh of the room; 0 for an empty room

    @property
    def largest(self) -> int:
        """The largest full target of the room; 0 when no box receives a mesh."""
        return max(self.targets.values(), default=0)


def mm(value: float) -> int:
    """Metres to whole millimetres, halves upwards."""
    return int(math.floor(float(value) * 1000 + 0.5))


def box_volume(dims: Sequence[float]) -> float:
    return float(dims[0]) * float(dims[1]) * float(dims[2])


def _volume_mm3(dims: Sequence[float]) -> int:
    return mm(dims[0]) * mm(dims[1]) * mm(dims[2])


def _area_mm2(dims: Sequence[float]) -> int:
    w, h, d = mm(dims[0]), mm(dims[1]), mm(dims[2])
    return 2 * (w * h + h * d + w * d)


def light_target(n_objects: int) -> int:
    """The light target of a room in which ``n_objects`` objects receive meshes."""
    n = max(1, int(n_objects))
    return max(LIGHT_TARGET_MIN, min(LIGHT_TARGET_MAX, ROOM_LIGHT_BUDGET // n))


def rank_boxes(boxes: Iterable[tuple[int, Sequence[float]]]) -> list[int]:
    """Box ids ordered largest volume first; equal volumes keep the lower id first."""
    return [box_id for box_id, _ in sorted(boxes, key=lambda b: (-_volume_mm3(b[1]), b[0]))]


def plan(boxes: Iterable[tuple[int, Sequence[float]]]) -> tuple[dict[int, int], int]:
    """``(full target per box id, the room's light target)``: the rule itself, in whole numbers."""
    boxes = [(int(box_id), dims) for box_id, dims in boxes]
    order = sorted(boxes, key=lambda b: (-_volume_mm3(b[1]), b[0]))
    meshed = order[:MAX_GENERATED_PER_ROOM]
    n = len(meshed)
    targets = {box_id: 0 for box_id, _ in boxes}
    if n == 0:
        return targets, 0
    area = {box_id: _area_mm2(dims) for box_id, dims in meshed}
    total = sum(area.values())
    share = {i: min(max((ROOM_TRIANGLE_BUDGET * a) // total, TARGET_MIN), TARGET_MAX) for i, a in area.items()}
    if sum(share.values()) > ROOM_TRIANGLE_BUDGET:
        # The floor of 3,000 pushed the room over: scale what each object has above the floor.
        excess = sum(v - TARGET_MIN for v in share.values())
        spare = ROOM_TRIANGLE_BUDGET - TARGET_MIN * n
        share = {i: TARGET_MIN + ((v - TARGET_MIN) * spare) // excess for i, v in share.items()}
    targets.update(share)
    return targets, light_target(n)


def plan_room(boxes: Iterable[tuple[int, Sequence[float]]]) -> RoomPlan:
    """Split a room's boxes into those that receive meshes and those that stay without, with the targets."""
    boxes = [(int(box_id), dims) for box_id, dims in boxes]
    order = rank_boxes(boxes)
    targets, light = plan(boxes)
    return RoomPlan(order[:MAX_GENERATED_PER_ROOM], order[MAX_GENERATED_PER_ROOM:], targets, light)
