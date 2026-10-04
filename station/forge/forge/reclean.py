"""Run clean-up again from a kept record, without the generator (mesh design section 5.6).

    python -m forge.reclean <jobId or job folder> [--data DIR] [--out DIR] [--json]

A job that ran under FORGE_DEBUG_KEEP=1 leaves in its folder what clean-up was
given: the crops, their mattes, the generator's raw output of each attempt and
`job.json` with the box, the targets, the profile and the cameras. This reads
them, does what the service did (turn and scale each attempt, take the one that
fits the scan best, finish it into the two copies) with the settings of the
present environment, and prints the new report. With the settings the job ran
under it gives the bytes that were published, so a change to clean-up, or to
`FORGE_TRIPOSG_UP`, `FORGE_TRIPOSG_FRONT` and `FORGE_MAX_ANISOTROPY`, can be
tried on recorded objects without the Station's card.

Nothing is written unless `--out` names a folder; the record itself is never changed.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Sequence

import numpy as np
from PIL import Image

from . import attempts
from .cleanup import CleanResult, CleanupError
from .config import Settings
from .imaging import MattedView, decode_crop


class RecordError(ValueError):
    """The folder does not hold a record clean-up can be run from."""


@dataclass
class Recleaned:
    """What running clean-up again gave: one line per kept attempt, and the copies of the best one."""

    job: dict
    attempts: list[dict]  # view, generator, fit, fitYaw, fitUp, error, published
    result: Optional[CleanResult]
    problems: list[str]

    @property
    def same_full(self) -> bool:
        return self.result is not None and self.result.report.sha256 == self.job.get("sha256")

    @property
    def same_light(self) -> bool:
        light = self.result.light_report if self.result is not None else None
        return (light.sha256 if light is not None else "") == self.job.get("lightSha256", "")


def read_record(folder: Path) -> tuple[dict, dict[int, MattedView], dict[int, tuple[np.ndarray, np.ndarray]]]:
    """``(job, views, raw meshes)`` of a job folder; the views and the meshes by view index."""
    folder = Path(folder)
    if not (folder / "job.json").is_file():
        raise RecordError(f"{folder} holds no job.json: it is not a job folder")
    job = json.loads((folder / "job.json").read_text(encoding="utf-8"))

    raws: dict[int, tuple[np.ndarray, np.ndarray]] = {}
    for path in sorted(folder.glob("raw_view*.npz")):
        match = re.fullmatch(r"raw_view(\d+)\.npz", path.name)
        if match:
            with np.load(path) as raw:
                raws[int(match.group(1))] = (np.asarray(raw["vertices"]), np.asarray(raw["faces"]))
    if not raws:
        raise RecordError(f"{folder} holds no raw_view<i>.npz: the job did not run under FORGE_DEBUG_KEEP=1, "
                          "or no generation of it succeeded")

    views: dict[int, MattedView] = {}
    for index in range(3):
        crop_path, matte_path = folder / f"view{index}.jpg", folder / f"matte{index}.png"
        if not crop_path.is_file():
            break
        crop = decode_crop(crop_path.read_bytes())
        if matte_path.is_file():
            with Image.open(matte_path) as matte:
                views[index] = MattedView(crop=crop, matte=matte.convert("L"), method="rembg")
        else:
            views[index] = MattedView(crop=crop, matte=Image.new("L", crop.size, 255), method="none")
    if 0 not in views:
        raise RecordError(f"{folder} holds no view0.jpg: the crops were not kept")
    return job, views, raws


def reclean(folder: Path, settings: Optional[Settings] = None) -> Recleaned:
    """Clean-up, as the service runs it, on every attempt kept in ``folder``."""
    settings = settings or Settings.from_env()
    job, views, raws = read_record(folder)
    generator_of = {entry["view"]: entry.get("generator", "") for entry in job.get("attemptLog", [])}

    lines, made = [], []
    for index in sorted(raws):
        vertices, faces = raws[index]
        generator = generator_of.get(index) or job.get("generator", "")
        line = {"view": index, "generator": generator, "fit": None, "error": "", "published": False}
        lines.append(line)
        try:
            oriented = attempts.orient_attempt(
                vertices, faces, dims=job["dims"], tri_target=job["triTarget"], profile_text=job.get("profile", ""),
                views=job.get("views", []), generator_name=generator, settings=settings)
        except CleanupError as exc:
            line["error"] = f"clean-up: {exc}"
            continue
        line.update(fit=None if oriented.fit is None else round(oriented.fit, 4),
                    fitYaw=(360 - oriented.turns * 90) % 360, fitUp=oriented.up_axis)
        made.append((line, oriented))

    problems: list[str] = [line["error"] for line in lines if line["error"]]
    for place in attempts.in_order_of_fit([oriented.fit for _, oriented in made]):
        line, oriented = made[place]
        try:
            result = attempts.finish_attempt(oriented, yaw=job.get("yaw", 0.0),
                                             tri_target_light=job.get("triTargetLight", 0), matted=views,
                                             views=job.get("views", []))
        except CleanupError as exc:
            problems.append(f"clean-up: {exc}")
            continue
        if result.report.violations:
            problems.append("asset contract: " + "; ".join(result.report.violations))
            continue
        line["published"] = True
        return Recleaned(job=job, attempts=lines, result=result, problems=problems)
    return Recleaned(job=job, attempts=lines, result=None, problems=problems)


def _folder(argument: str, data: Optional[str]) -> Path:
    path = Path(argument)
    if path.is_dir():
        return path
    root = Path(data) if data else Settings.from_env().data_dir
    return root / "jobs" / argument


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="python -m forge.reclean",
        description="Run clean-up again from a job's kept record and print the new report (mesh design 5.6).")
    parser.add_argument("job", help="a job id, or the job's folder")
    parser.add_argument("--data", default=None, help="the Forge's data folder (FORGE_DATA_DIR when not given)")
    parser.add_argument("--out", default=None, help="a folder to write full.glb, light.glb and report.json to")
    parser.add_argument("--json", action="store_true", help="print one JSON object instead of the text")
    args = parser.parse_args(argv)

    try:
        done = reclean(_folder(args.job, args.data))
    except RecordError as exc:
        print(f"FAIL {exc}")
        return 1
    result = done.result
    light = result.light_report if result is not None else None
    answer = {
        "jobId": done.job.get("jobId"),
        "attempts": done.attempts,
        "problems": done.problems,
        "report": None if result is None else result.report.to_dict(),
        "lightReport": None if light is None else light.to_dict(),
        "sameBytesAsPublished": {"full": done.same_full, "light": done.same_light},
    }
    if args.out and result is not None:
        out = Path(args.out)
        out.mkdir(parents=True, exist_ok=True)
        (out / "full.glb").write_bytes(result.glb)
        if result.light_glb is not None:
            (out / "light.glb").write_bytes(result.light_glb)
        (out / "report.json").write_text(json.dumps({"full": answer["report"], "light": answer["lightReport"]},
                                                    indent=1), encoding="utf-8")
    if args.json:
        print(json.dumps(answer))
        return 0 if result is not None else 1

    print(f"job {answer['jobId']}: box {done.job.get('boxId')} ({done.job.get('category')}), "
          f"{len(done.attempts)} kept attempt(s)")
    for line in done.attempts:
        if line["error"]:
            print(f"  view {line['view']}: {line['error']}")
        else:
            fit = "no profile" if line["fit"] is None else f"fit {line['fit']:.4f}"
            print(f"  view {line['view']}: {fit}, up {line['fitUp']}, yaw {line['fitYaw']}"
                  + ("  <- published" if line["published"] else ""))
    if result is None:
        print("no copy could be made: " + "; ".join(done.problems))
        return 1
    print("full copy: " + json.dumps(answer["report"], indent=1))
    if light is not None:
        print(f"light copy: {light.triangles} triangles, {light.bytes} bytes, {light.sha256}")
    else:
        print("light copy: none" + (f" ({result.report.light['error']})" if result.report.light else ""))
    for name, same, old in (("full", done.same_full, done.job.get("sha256")),
                            ("light", done.same_light, done.job.get("lightSha256"))):
        print(f"the {name} copy is " + ("the same bytes as the published one" if same
                                        else f"not the published one ({old or 'none was published'})"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
