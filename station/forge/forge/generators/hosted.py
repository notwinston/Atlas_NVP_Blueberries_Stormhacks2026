"""The hosted image-to-3D fallback (decision D20; Master Plan sections 7.4 and 14, open item 8).

No vendor is chosen yet, so none is wired: this class fixes the interface the
rest of the Forge calls and the rules around it, and `_request` is the single
method a vendor adapter has to supply.

Using it sends the walker's crop to a third party whose retention the team does
not control. It is therefore off unless `FORGE_HOSTED_ENABLED=1`, GET /health
reports whether it is on, every job that used it reports `source: "hosted"`,
and each use is logged. The walker must have been told before the scan.
"""
from __future__ import annotations

import logging
from typing import Optional

from ..config import Settings
from .base import GenerationRequest, GeneratorUnavailable, RawMesh

log = logging.getLogger("forge.hosted")


class HostedGenerator:
    name = "hosted"
    source = "hosted"

    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def availability(self) -> tuple[bool, str]:
        if not self._settings.hosted_enabled:
            return False, "the hosted fallback is disabled (FORGE_HOSTED_ENABLED is not set)"
        if not self._settings.hosted_url or not self._settings.hosted_key:
            return False, "no hosted service is configured (FORGE_HOSTED_URL, FORGE_HOSTED_KEY)"
        return True, ""

    def gpu(self) -> Optional[str]:
        return None

    def generate(self, request: GenerationRequest) -> RawMesh:
        ok, reason = self.availability()
        if not ok:
            raise GeneratorUnavailable(reason)
        log.warning("sending a crop to the hosted service at %s (third party)", self._settings.hosted_url)
        return self._request(request)

    def _request(self, request: GenerationRequest) -> RawMesh:
        """Upload the crop, wait for the result and return it as a raw mesh.

        A vendor adapter subclasses HostedGenerator and overrides this method.
        Until one exists the fallback reports itself unavailable, so a job that
        reaches it fails and its box takes the built-in mesh.
        """
        raise GeneratorUnavailable("no hosted vendor adapter is installed (Master Plan section 14, open item 8)")
