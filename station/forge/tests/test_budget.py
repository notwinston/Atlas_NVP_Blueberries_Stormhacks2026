import pytest

from forge.budget import MAX_GENERATED_PER_ROOM, light_target, mm, plan, plan_room, rank_boxes

# The test vectors of the mesh design brief (S5): boxes, the full targets in the order of the ids, the light target.
VECTORS = [
    ([(i, (1.0, 1.0, 1.0)) for i in range(1, 6)], [15000] * 5, 5000),
    ([(1, (2.0, 0.8, 0.9)), (2, (0.5, 0.9, 0.5)), (3, (1.2, 0.75, 0.6))], [15000, 15000, 15000], 5000),
    ([(i, (1.5, 1.0, 1.0)) for i in range(1, 9)] + [(i, (0.2, 0.2, 0.2)) for i in range(9, 33)],
     [6000] * 8 + [3000] * 24, 1875),
    ([(i, (0.3 + 0.05 * i, 0.5, 0.4)) for i in range(1, 41)],
     [0] * 8 + [3000] * 9 + [3044, 3134, 3226, 3316, 3407, 3498, 3589, 3679, 3770, 3861, 3952, 4043, 4134, 4225,
                             4315, 4407, 4497, 4588, 4679, 4770, 4861, 4952, 5043], 1875),
    ([(7, (0.6, 0.45, 0.6))], [15000], 5000),
    ([(i, (1.8, 0.8, 0.9)) for i in range(1, 13)], [10000] * 12, 5000),
]


@pytest.mark.parametrize("boxes, full, light", VECTORS)
def test_the_triangle_rule_vectors(boxes, full, light):
    targets, room_light = plan(boxes)
    assert [targets[box_id] for box_id, _ in boxes] == full
    assert room_light == light


def test_the_fourth_vector_sums_to_119990():
    targets, _ = plan(VECTORS[3][0])
    assert sum(targets.values()) == 119_990


def test_whole_millimetres_round_halves_up():
    assert mm(0.3 + 0.05 * 3) == 450  # 0.44999999999999996 in floating point
    assert mm(0.0005) == 1 and mm(0.00049) == 0 and mm(1.0) == 1000


def test_the_table_of_the_design_for_objects_of_equal_size():
    # Mesh design section 5.4: objects of equal size, the full target each and the light target each.
    for n, full, light in ((5, 15000, 5000), (8, 15000, 5000), (12, 10000, 5000), (20, 6000, 3000), (32, 3750, 1875)):
        targets, room_light = plan([(i, (0.8, 0.9, 0.7)) for i in range(1, n + 1)])
        assert set(targets.values()) == {full} and room_light == light


def test_room_never_exceeds_its_budgets_and_no_mesh_is_under_three_thousand():
    for n in range(1, 65):
        boxes = [(i, (0.2 + 0.07 * i, 0.3 + 0.01 * (i % 9), 0.25 + 0.03 * (i % 5))) for i in range(1, n + 1)]
        room = plan_room(boxes)
        meshed = [room.targets[i] for i in room.generated]
        assert len(room.generated) == min(n, MAX_GENERATED_PER_ROOM)
        assert sum(meshed) <= 120_000 and all(3000 <= t <= 15000 for t in meshed)
        assert all(room.targets[i] == 0 for i in room.placeholders)
        assert len(meshed) * room.light <= 60_000 and 1500 <= room.light <= 5000
        assert room.largest == max(meshed)


def test_light_target_rule():
    # clamp(60,000 / n, 1,500, 5,000): 5,000 up to twelve objects, 1,875 at thirty-two.
    assert [light_target(n) for n in (1, 12, 13, 20, 32, 40, 64)] == [5000, 5000, 4615, 3000, 1875, 1500, 1500]


def test_largest_boxes_first_and_cap_of_thirty_two():
    boxes = [(i + 1, (0.1 * (i + 1), 1.0, 1.0)) for i in range(40)]
    room = plan_room(boxes)
    assert room.generated == list(range(40, 8, -1))
    assert room.placeholders == [8, 7, 6, 5, 4, 3, 2, 1]
    assert room.light == 1875 and sorted(room.targets) == list(range(1, 41))


def test_equal_volumes_keep_id_order_and_empty_room():
    assert rank_boxes([(3, (1, 1, 1)), (1, (1, 1, 1)), (2, (2, 1, 1))]) == [2, 1, 3]
    assert plan_room([]) == ([], [], {}, 0)
    assert plan([]) == ({}, 0) and plan_room([]).largest == 0
