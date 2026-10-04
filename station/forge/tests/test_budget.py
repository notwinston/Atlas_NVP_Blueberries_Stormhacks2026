from forge.budget import MAX_GENERATED_PER_ROOM, plan_room, rank_boxes, triangle_target


def test_triangle_target_rule():
    # clamp(60,000 / n, 3,000, 5,000): 5,000 up to twelve objects, 3,000 at twenty (section 7.4).
    assert triangle_target(1) == 5000
    assert triangle_target(12) == 5000
    assert triangle_target(13) == 4615
    assert triangle_target(15) == 4000
    assert triangle_target(20) == 3000
    assert triangle_target(40) == 3000
    assert triangle_target(0) == 5000


def test_room_never_exceeds_sixty_thousand_triangles():
    for n in range(1, MAX_GENERATED_PER_ROOM + 1):
        assert n * triangle_target(n) <= 60_000


def test_largest_boxes_first_and_cap_of_twenty():
    boxes = [(i + 1, (0.1 * (i + 1), 1.0, 1.0)) for i in range(25)]
    generated, placeholders, target = plan_room(boxes)
    assert generated == list(range(25, 5, -1))
    assert placeholders == [5, 4, 3, 2, 1]
    assert target == 3000


def test_equal_volumes_keep_id_order_and_empty_room():
    assert rank_boxes([(3, (1, 1, 1)), (1, (1, 1, 1)), (2, (2, 1, 1))]) == [2, 1, 3]
    assert plan_room([]) == ([], [], 0)
