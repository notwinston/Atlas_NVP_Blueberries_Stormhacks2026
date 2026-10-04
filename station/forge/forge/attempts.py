"""From a job's views and its generated meshes to the copies that are published.

What the service and `forge.reclean` share, so that clean-up run again from a
kept record does exactly what the service did (mesh design sections 5.3 and
5.6): how the fit is set up for a job, which photos give the colours, and in
which order the attempts of an object are offered for publishing.

Nothing here keeps state or touches a file.
"""
from __future__ import annotations

from typing import Mapping, Optional, Sequence

import numpy as np

from . import profile as scan_profile
from .cleanup import CleanResult, Oriented, finish, orient
from .colour import ColourView
from .config import Settings
from .imaging import MattedView, dominant_colour

# At most this many generations are made for one object (decision M7).
MAX_GENERATIONS = 3
# After this many failed generations with no mesh, the generator is given up for the object:
# one retry, as before the attempts existed.
MAX_FAILURES = 2


def scan_of(profile_text: str) -> Optional[np.ndarray]:
    """The object profile a job carries, or None when it has none or the scan saw nothing of the object."""
    if not profile_text:
        return None
    scan = scan_profile.decode(profile_text)
    return scan if scan.any() else None


def preferred_turns(front: str, views: Sequence[Mapping]) -> Optional[int]:
    """What `FORGE_TRIPOSG_FRONT` says of where a generated mesh's front is, as quarter turns of clean-up.

    ``canonical``: the front is on the mesh's own +Z, so no turn. ``view``: the
    mesh's +Z side is the one the camera of view 0 saw; with the camera on side
    k of the box (`profile.view_quarter_turn`) that takes (4 - k) % 4 turns of
    `_YAW90` in the GLB frame. ``unknown``, or ``view`` without a camera pose:
    None, nothing is preferred.
    """
    if front == "canonical":
        return 0
    if front == "view" and views and views[0].get("camToBox"):
        return (4 - scan_profile.view_quarter_turn(views[0]["camToBox"])) % 4
    return None


def up_axes_for(generator_name: str, settings: Settings) -> Optional[tuple[str, ...]]:
    """The up axes clean-up may try: the measured one for TripoSG, all three for any other generator."""
    return (settings.triposg_up,) if generator_name == "triposg" else None


def front_for(generator_name: str, settings: Settings) -> str:
    """`triposg_front` describes TripoSG's meshes only; nothing is known of another generator's front."""
    return settings.triposg_front if generator_name == "triposg" else "unknown"


def orient_attempt(vertices: np.ndarray, faces: np.ndarray, *, dims: Sequence[float], tri_target: int,
                   profile_text: str, views: Sequence[Mapping], generator_name: str, settings: Settings) -> Oriented:
    """Steps 1 to 3 of clean-up for one generated mesh of a job; its fit to the scan is in the result."""
    return orient(
        vertices, faces, dims, tri_target,
        scan=scan_of(profile_text),
        prefer_turns=preferred_turns(front_for(generator_name, settings), views),
        up_axes=up_axes_for(generator_name, settings),
        max_anisotropy=settings.max_anisotropy,
    )


def colour_views(matted: Mapping[int, MattedView], views: Sequence[Mapping]) -> list[ColourView]:
    """The photos the colours of a mesh can be read from, in the order of the views.

    A view is usable when it is matted and the phone sent its camera: the crop
    at the resolution its ``intrinsics`` describe, and ``camToBox``. Without a
    matte nothing tells the object from the room behind it, so such a view
    gives no colour and the object keeps one cell, as before. A camera that
    cannot be one (a focal length that is not positive, a matrix that cannot
    be inverted) is left out the same way.
    """
    usable = []
    for index in sorted(matted):
        view = matted[index]
        meta = views[index] if index < len(views) else {}
        if view.method == "none" or not meta.get("intrinsics") or not meta.get("camToBox"):
            continue
        intrinsics = tuple(float(k) for k in meta["intrinsics"])
        cam_to_box = np.asarray(meta["camToBox"], dtype=np.float64).reshape(4, 4)
        if not (np.isfinite(intrinsics).all() and intrinsics[0] > 0 and intrinsics[1] > 0
                and np.isfinite(cam_to_box).all() and abs(np.linalg.det(cam_to_box)) > 1e-9):
            continue
        usable.append(ColourView(
            rgb=np.asarray(view.crop.convert("RGB"), dtype=np.uint8),
            alpha=np.asarray(view.matte, dtype=np.uint8),
            intrinsics=intrinsics,
            cam_to_box=cam_to_box,
        ))
    return usable


def finish_attempt(oriented: Oriented, *, yaw: float, tri_target_light: int, matted: Mapping[int, MattedView],
                   views: Sequence[Mapping]) -> CleanResult:
    """Steps 4 to 8 of clean-up for the attempt that is to be published.

    The one colour an object takes where no photo sees it is the dominant
    colour of view 0, as before; ``matted`` must hold view 0.
    """
    return finish(
        oriented,
        dominant_colour(matted[0].cutout),
        yaw=yaw,
        views=colour_views(matted, views),
        tri_target_light=tri_target_light or None,
    )


def in_order_of_fit(fits: Sequence[Optional[float]]) -> list[int]:
    """The places of an object's attempts in the order they are offered for publishing.

    The lowest fit first; equal fits, and attempts with no fit (a job without a
    profile), in the order they were made.
    """
    return sorted(range(len(fits)), key=lambda k: (fits[k] is None, fits[k] if fits[k] is not None else 0.0, k))
