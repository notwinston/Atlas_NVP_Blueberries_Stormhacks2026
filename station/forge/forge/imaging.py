"""Photo handling: decoding crops, the background-removal hook and the dominant colour."""
from __future__ import annotations

import io
import logging

import numpy as np
from PIL import Image

log = logging.getLogger("forge.imaging")


class ImageError(ValueError):
    """A crop could not be decoded."""


def decode_crop(data: bytes) -> Image.Image:
    """Decode an uploaded crop to RGB; a corrupt file raises ImageError."""
    try:
        image = Image.open(io.BytesIO(data))
        image.load()
        return image.convert("RGB")
    except Exception as exc:  # Pillow raises many unrelated types for bad files
        raise ImageError(f"crop is not a decodable image: {exc}") from exc


_rembg_session = None


def remove_background(image: Image.Image) -> tuple[Image.Image, str]:
    """Return ``(RGBA image, method)``.

    Uses rembg when it is installed (Master Plan section 7.4, state `matting`).
    Without it the crop passes through with a full alpha channel and the method
    is "none": the phone already crops tightly to the projected box.
    """
    try:
        import rembg  # optional; pulls in onnxruntime
    except ImportError:
        return image.convert("RGBA"), "none"
    global _rembg_session
    if _rembg_session is None:
        _rembg_session = rembg.new_session()
    cut = rembg.remove(image, session=_rembg_session)
    return cut.convert("RGBA"), "rembg"


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
