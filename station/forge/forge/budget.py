"""The furniture budget of Master Plan section 4.5.

At most 20 objects of a room receive generated meshes, largest boxes first, and
each gets clamp(60,000 / n, 3,000, 5,000) triangles, so one room's furniture
never exceeds 60,000 triangles.
"""
from __future__ import annotations

from typing import Iterable, Sequence

MAX_GENERATED_PER_ROOM = 20
ROOM_TRIANGLE_BUDGET = 60_000
TARGET_MIN = 3_000
TARGET_MAX = 5_000

# The asset validator's hard range (section 6.4); a job may ask for any target inside it.
VALIDATOR_MIN_TRIANGLES = 1_500
VALIDATOR_MAX_TRIANGLES = 5_000
MAX_EDGE_METRES = 0.25


def triangle_target(n_objects: int) -> int:
    """Triangle target per object when ``n_objects`` objects receive meshes."""
    n = max(1, int(n_objects))
    # Integer division keeps n * target at or below the room budget.
    return max(TARGET_MIN, min(TARGET_MAX, ROOM_TRIANGLE_BUDGET // n))


def box_volume(dims: Sequence[float]) -> float:
    return float(dims[0]) * float(dims[1]) * float(dims[2])


def rank_boxes(boxes: Iterable[tuple[int, Sequence[float]]]) -> list[int]:
    """Box ids ordered largest volume first; equal volumes keep the lower id first."""
    return [box_id for box_id, _ in sorted(boxes, key=lambda b: (-box_volume(b[1]), b[0]))]


def plan_room(boxes: Iterable[tuple[int, Sequence[float]]]) -> tuple[list[int], list[int], int]:
    """Split a room's boxes into those that receive meshes and those that stay placeholders.

    Returns ``(generated, placeholders, target)``; ``target`` is 0 for an empty room.
    """
    order = rank_boxes(boxes)
    generated = order[:MAX_GENERATED_PER_ROOM]
    placeholders = order[MAX_GENERATED_PER_ROOM:]
    target = triangle_target(len(generated)) if generated else 0
    return generated, placeholders, target
