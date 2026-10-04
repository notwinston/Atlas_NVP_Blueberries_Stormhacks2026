"""Photo handling: decoding crops, the matte, the picture a generator receives and the dominant colour."""
from __future__ import annotations

import importlib.util
import io
import logging
from dataclasses import dataclass

import numpy as np
from PIL import Image

log = logging.getLogger("forge.imaging")

# Alpha at or above this is the object; below it the matte is not solid.
MATTE_SOLID = 128
# The picture a generator is given: the object cut to its matte, with this share of the
# long side of that cut as white margin on every side (TripoSG's own framing).
FRAME_MARGIN = 0.10
# Detached pieces of matte holding less than this share of it are specks, not the object.
SPECK_SHARE = 0.005
# The soft edge of the matte that is kept around the object, in pixels.
_EDGE_PIXELS = 2
# An object smaller than this on its long side is no object.
_MIN_OBJECT_PIXELS = 16

MATTING_MISSING = "no matting model is installed (rembg; pip install -r requirements-gpu.txt)"


class ImageError(ValueError):
    """A crop could not be decoded, or no picture could be made from it."""


def decode_crop(data: bytes) -> Image.Image:
    """Decode an uploaded crop to RGB; a corrupt file raises ImageError."""
    try:
        image = Image.open(io.BytesIO(data))
        image.load()
        return image.convert("RGB")
    except Exception as exc:  # Pillow raises many unrelated types for bad files
        raise ImageError(f"crop is not a decodable image: {exc}") from exc


# --------------------------------------------------------------------------- matting

_rembg_session = None


def matting_method() -> str:
    """``"rembg"`` when a matting model is installed, ``"none"`` otherwise; reported by GET /health."""
    return "rembg" if importlib.util.find_spec("rembg") is not None else "none"


def load_matting() -> str:
    """Create the matting session now, so that the first job does not pay for it. Returns the method."""
    if matting_method() == "none":
        return "none"
    import rembg  # optional; pulls in onnxruntime

    global _rembg_session
    if _rembg_session is None:
        _rembg_session = rembg.new_session()
    return "rembg"


def remove_background(image: Image.Image) -> tuple[Image.Image, str]:
    """Return ``(RGBA image, method)``.

    Uses rembg when it is installed (Master Plan section 7.4, state `matting`).
    Without it the crop passes through with a full alpha channel and the method
    is "none": the phone already crops tightly to the projected box. That is
    enough for the dominant colour; a generator that needs a matted picture is
    never given such a crop (mesh design section 5.1).
    """
    if load_matting() == "none":
        return image.convert("RGBA"), "none"
    import rembg

    cut = rembg.remove(image, session=_rembg_session)
    return cut.convert("RGBA"), "rembg"


@dataclass
class MattedView:
    """One view of a job: the crop as it was uploaded and its matte.

    Kept in memory until the job ends: the generator's picture is framed from
    it, and the colour step projects the crop onto the mesh through the matte.
    """

    crop: Image.Image  # RGB, the size the phone sent; `intrinsics` of the view are in these pixels
    matte: Image.Image  # L, the same size; 255 is the object
    method: str  # "rembg", or "none" when the crop is not matted
    note: str = ""  # why this view is not matted, when a matting model is installed and failed on it

    @property
    def cutout(self) -> Image.Image:
        """The crop with the matte as its alpha channel (RGBA)."""
        image = self.crop.convert("RGBA")
        image.putalpha(self.matte)
        return image


# --------------------------------------------------------------------------- framing


def _pieces(mask: np.ndarray) -> np.ndarray:
    """Label the 4-connected pieces of ``mask``. Every pixel of a piece gets the same number; outside is -1."""
    height, width = mask.shape
    outside = height * width
    label = np.where(mask, np.arange(outside).reshape(height, width), outside)
    while True:
        new = label.copy()
        np.minimum(new[1:], label[:-1], out=new[1:])
        np.minimum(new[:-1], label[1:], out=new[:-1])
        np.minimum(new[:, 1:], label[:, :-1], out=new[:, 1:])
        np.minimum(new[:, :-1], label[:, 1:], out=new[:, :-1])
        new[~mask] = outside
        # A label is the index of a pixel of the same piece: follow it to that pixel's own label.
        flat = new.reshape(-1)
        inside = flat < outside
        flat[inside] = flat[flat[inside]]
        if np.array_equal(new, label):
            break
        label = new
    return np.where(mask, label, -1)


def object_matte(matte: Image.Image) -> tuple[np.ndarray, tuple[int, int, int, int]]:
    """The matte without its specks, and the bounding box of the object in it.

    Returns ``(alpha, (x0, y0, x1, y1))``: alpha as uint8 with everything that
    is not the object or its soft edge set to 0, and the box of the solid matte
    with ``x1`` and ``y1`` one past the last pixel. Raises ImageError when the
    matte holds no object.
    """
    alpha = np.asarray(matte.convert("L"), dtype=np.uint8)
    solid = alpha >= MATTE_SOLID
    if not solid.any():
        raise ImageError("the matte is empty: no object was found in the crop")
    label = _pieces(solid)
    names, counts = np.unique(label[solid], return_counts=True)
    specks = names[counts < SPECK_SHARE * solid.sum()]
    if len(specks):
        solid &= ~np.isin(label, specks)
    rows, columns = np.flatnonzero(solid.any(axis=1)), np.flatnonzero(solid.any(axis=0))
    x0, x1, y0, y1 = int(columns[0]), int(columns[-1]) + 1, int(rows[0]), int(rows[-1]) + 1
    if max(x1 - x0, y1 - y0) < _MIN_OBJECT_PIXELS:
        raise ImageError(f"the matte holds an object of only {x1 - x0} x {y1 - y0} pixels")
    near = solid
    for _ in range(_EDGE_PIXELS):
        grown = near.copy()
        grown[1:] |= near[:-1]
        grown[:-1] |= near[1:]
        grown[:, 1:] |= near[:, :-1]
        grown[:, :-1] |= near[:, 1:]
        near = grown
    return np.where(near, alpha, 0).astype(np.uint8), (x0, y0, x1, y1)


def frame_for_generator(crop: Image.Image, matte: Image.Image) -> Image.Image:
    """The picture TripoSG is given, framed the way its own script frames one (mesh design section 5.1).

    The crop is cut to the bounding box of its matte, put on white, and padded
    to a square with a margin of 10 % of the long side of that box on every
    side, the object in the middle. The model's processor resizes the short
    edge to 256 and keeps the central 224 x 224, so only a square picture
    reaches it whole. Returns RGB.
    """
    rgb = np.asarray(crop.convert("RGB"), dtype=np.float32)
    if matte.size != crop.size:
        matte = matte.resize(crop.size, Image.BILINEAR)
    alpha, (x0, y0, x1, y1) = object_matte(matte)
    cover = alpha[y0:y1, x0:x1].astype(np.float32)[..., None] / 255.0
    cut = rgb[y0:y1, x0:x1] * cover + 255.0 * (1.0 - cover)

    width, height = x1 - x0, y1 - y0
    long_side = max(width, height)
    margin = int(long_side * FRAME_MARGIN)
    side = long_side + 2 * margin
    # An odd difference between the sides cannot be halved: the spare pixel goes to the right or the bottom.
    left, top = margin + (long_side - width) // 2, margin + (long_side - height) // 2
    picture = np.full((side, side, 3), 255, dtype=np.uint8)
    picture[top : top + height, left : left + width] = np.rint(cut).astype(np.uint8)
    return Image.fromarray(picture)


# --------------------------------------------------------------------------- colour


def dominant_colour(image: Image.Image) -> tuple[int, int, int]:
    """The most common colour of the object in an RGBA crop, as sRGB 0-255.

    Pixels the matte removed are ignored. When there is no matte the border of
    the crop is ignored instead, because the box is padded by 12 % on each side.
    """
    rgba = np.asarray(image.convert("RGBA").resize((96, 96), Image.BILINEAR), dtype=np.uint8)
    alpha = rgba[..., 3]
    mask = alpha > 127
    if mask.all():
        mask = np.zeros_like(mask)
        mask[19:77, 19:77] = True
    if mask.sum() < 16:
        mask = np.ones_like(mask)
    pixels = rgba[..., :3][mask].astype(np.int64)
    # Histogram on 4 bits per channel; the fullest bin gives the colour.
    bins = (pixels[:, 0] >> 4) * 256 + (pixels[:, 1] >> 4) * 16 + (pixels[:, 2] >> 4)
    counts = np.bincount(bins, minlength=4096)
    best = int(np.argmax(counts))
    mean = pixels[bins == best].mean(axis=0)
    return int(round(mean[0])), int(round(mean[1])), int(round(mean[2]))
