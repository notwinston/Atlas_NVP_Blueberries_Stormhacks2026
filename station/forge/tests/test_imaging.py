import numpy as np
import pytest
from PIL import Image

from forge import imaging
from forge.imaging import ImageError, MattedView, frame_for_generator, object_matte

OBJECT = (150, 112, 70)


def scene(width, height, box, colour=OBJECT, seed=0):
    """A cluttered crop with a flat-coloured object in ``box`` (x0, y0, x1, y1), and its matte."""
    rng = np.random.default_rng(seed)
    pixels = rng.integers(0, 200, (height, width, 3), dtype=np.uint8)  # the room behind the object
    alpha = np.zeros((height, width), dtype=np.uint8)
    x0, y0, x1, y1 = box
    pixels[y0:y1, x0:x1] = colour
    alpha[y0:y1, x0:x1] = 255
    return Image.fromarray(pixels), Image.fromarray(alpha)


def object_box(picture: Image.Image, colour=OBJECT):
    """Where the object's own colour is in a framed picture: (x0, y0, x1, y1)."""
    pixels = np.asarray(picture)
    hit = (pixels == np.array(colour, dtype=np.uint8)).all(axis=2)
    rows, columns = np.flatnonzero(hit.any(axis=1)), np.flatnonzero(hit.any(axis=0))
    return int(columns[0]), int(rows[0]), int(columns[-1]) + 1, int(rows[-1]) + 1


def is_white_outside(picture: Image.Image, box) -> bool:
    pixels = np.asarray(picture).copy()
    x0, y0, x1, y1 = box
    pixels[y0:y1, x0:x1] = 255
    return bool((pixels == 255).all())


@pytest.mark.parametrize("box", [
    (100, 120, 300, 180),  # wide: 200 x 60
    (150, 40, 210, 260),  # tall: 60 x 220
    (60, 50, 260, 250),  # square: 200 x 200
    (0, 0, 400, 300),  # the whole crop
    (10, 200, 111, 260),  # 101 x 60: the difference of the sides is odd
])
def test_framing_is_square_centred_and_white_with_a_tenth_of_margin(box):
    crop, matte = scene(400, 300, box)
    picture = frame_for_generator(crop, matte)
    width, height = box[2] - box[0], box[3] - box[1]
    long_side = max(width, height)
    margin = int(long_side * 0.10)

    assert picture.mode == "RGB"
    assert picture.size == (long_side + 2 * margin, long_side + 2 * margin)  # square
    x0, y0, x1, y1 = object_box(picture)
    assert (x1 - x0, y1 - y0) == (width, height)  # the object is neither scaled nor cut
    # The margin on the long side is 10 % of it, on both ends.
    if width >= height:
        assert x0 == margin and picture.size[0] - x1 == margin
    if height >= width:
        assert y0 == margin and picture.size[1] - y1 == margin
    # Centred: the two margins of an axis differ by the one pixel an odd difference leaves, at most.
    assert 0 <= (picture.size[0] - x1) - x0 <= 1 and 0 <= (picture.size[1] - y1) - y0 <= 1
    # Everything that is not the object is white: nothing of the room behind it is left.
    assert is_white_outside(picture, (x0, y0, x1, y1))


def test_margin_is_a_tenth_of_the_long_side_for_a_range_of_sizes():
    for long_side in (16, 37, 100, 255, 640):
        crop, matte = scene(700, 700, (20, 30, 20 + long_side, 30 + max(8, long_side // 3)))
        picture = frame_for_generator(crop, matte)
        assert picture.size == (long_side + 2 * int(long_side * 0.10),) * 2
        # The object fills four fifths of the picture or a little more, as the model was trained.
        assert 0.8 <= long_side / picture.size[0] <= 0.9


def test_a_soft_matte_blends_into_white_and_keeps_its_edge():
    crop, matte = scene(200, 200, (50, 60, 150, 140), colour=(0, 0, 0))
    alpha = np.asarray(matte).copy()
    alpha[60:140, 150:152] = 100  # a soft edge to the right of the solid matte
    alpha[10:20, 10:20] = 60  # a faint ghost far from the object
    picture = np.asarray(frame_for_generator(crop, Image.fromarray(alpha)))
    assert picture.shape == (120, 120, 3)  # the box is the solid matte's: 100 x 80, margin 10
    assert (picture[20:100, 10:110] == 0).all()
    # The soft columns lie just outside the box, inside the margin; the ghost is gone.
    assert (picture[:20] == 255).all() and (picture[100:] == 255).all() and (picture[:, :10] == 255).all()
    assert (picture[:, 110:] == 255).all()

    # Soft pixels inside the box blend with white by their alpha.
    alpha = np.asarray(matte).copy()
    alpha[60:140, 100:150] = 128
    half = np.asarray(frame_for_generator(crop, Image.fromarray(alpha)))
    assert (half[20:100, 10:60] == 0).all()
    assert abs(int(half[60, 80, 0]) - round(255 * (1 - 128 / 255))) <= 1


def test_specks_do_not_widen_the_frame_and_detached_parts_do():
    crop, matte = scene(400, 300, (100, 100, 300, 200))
    alpha = np.asarray(matte).copy()
    pixels = np.asarray(crop).copy()
    alpha[5:8, 390:393] = 255  # nine pixels of 20,000: a speck in the corner
    with_speck = frame_for_generator(crop, Image.fromarray(alpha))
    assert with_speck.size == (240, 240) and object_box(with_speck) == (20, 70, 220, 170)
    assert is_white_outside(with_speck, (20, 70, 220, 170))

    # A chair leg the matte cut loose from the seat is part of the object.
    alpha[230:280, 110:125] = 255  # 750 pixels, 3.6 % of the matte
    pixels[230:280, 110:125] = OBJECT
    with_leg = frame_for_generator(Image.fromarray(pixels), Image.fromarray(alpha))
    assert with_leg.size == (240, 240)
    assert object_box(with_leg) == (20, 30, 220, 210)  # 200 wide, 180 high: rows 100 to 279 of the crop
    _, bounds = object_matte(Image.fromarray(alpha))
    assert bounds == (100, 100, 300, 280)


def test_a_speck_inside_the_frame_is_whitened():
    crop, matte = scene(300, 300, (50, 50, 250, 60))  # a thin bar: 200 x 10
    alpha = np.asarray(matte).copy()
    alpha[150:300, 100:200] = 255  # and a large body below it
    alpha[100:102, 120:122] = 255  # a speck between the two, inside the bounding box
    picture = frame_for_generator(crop, Image.fromarray(alpha))
    cleaned, bounds = object_matte(Image.fromarray(alpha))
    assert bounds == (50, 50, 250, 300) and cleaned[100, 120] == 0 and cleaned[55, 100] == 255
    margin = int(250 * 0.10)
    left, top = margin + (250 - 200) // 2, margin
    assert (np.asarray(picture)[top + 50, left + 70] == 255).all()  # where the speck was


def test_an_empty_or_tiny_matte_is_refused():
    crop, _ = scene(200, 200, (50, 50, 150, 150))
    with pytest.raises(ImageError, match="empty"):
        frame_for_generator(crop, Image.new("L", crop.size, 0))
    with pytest.raises(ImageError, match="empty"):
        frame_for_generator(crop, Image.new("L", crop.size, 127))  # nothing is solid
    tiny = np.zeros((200, 200), dtype=np.uint8)
    tiny[100:108, 100:110] = 255
    with pytest.raises(ImageError, match="only 10 x 8 pixels"):
        frame_for_generator(crop, Image.fromarray(tiny))


def test_a_matte_of_specks_alone_is_refused_as_an_empty_one_is():
    # Dots of four pixels on a grid: 625 pieces, each far under 0.5 % of the matte. Nothing is left of it.
    crop, _ = scene(200, 200, (50, 50, 150, 150))
    dots = np.zeros((200, 200), dtype=np.uint8)
    dots[::8, ::8] = dots[1::8, ::8] = dots[::8, 1::8] = dots[1::8, 1::8] = 255
    with pytest.raises(ImageError, match="only specks"):
        frame_for_generator(crop, Image.fromarray(dots))
    with pytest.raises(ImageError, match="only specks"):
        object_matte(Image.fromarray(dots))
    # The same dots beside a real object are left out, and the object is framed.
    dots[60:140, 70:130] = 255
    assert frame_for_generator(crop, Image.fromarray(dots)).size == (96, 96)


def test_a_matte_of_another_size_is_brought_to_the_crop():
    crop, matte = scene(400, 300, (100, 90, 300, 210))
    small = matte.resize((200, 150), Image.NEAREST)
    picture = frame_for_generator(crop, small)
    assert abs(picture.size[0] - 240) <= 4 and picture.size[0] == picture.size[1]


def test_matted_view_cutout_and_dominant_colour():
    crop, matte = scene(128, 128, (30, 30, 100, 100), colour=(40, 54, 92))
    view = MattedView(crop=crop, matte=matte, method="rembg")
    cutout = view.cutout
    assert cutout.mode == "RGBA" and cutout.size == crop.size
    assert np.array_equal(np.asarray(cutout)[..., 3], np.asarray(matte))
    assert np.array_equal(np.asarray(cutout)[..., :3], np.asarray(crop))
    assert imaging.dominant_colour(cutout) == (40, 54, 92)


def test_without_a_matting_model_the_crop_passes_through(monkeypatch):
    # As on a machine without rembg: the method is "none" and nothing is cut away.
    monkeypatch.setattr(imaging, "matting_method", lambda: "none")
    assert imaging.load_matting() == "none"
    crop, _ = scene(64, 64, (10, 10, 50, 50))
    image, method = imaging.remove_background(crop)
    assert method == "none" and image.mode == "RGBA" and np.asarray(image)[..., 3].min() == 255
