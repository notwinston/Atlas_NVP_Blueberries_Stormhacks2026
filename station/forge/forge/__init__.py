"""The Forge: the Station service that turns object photos into furniture meshes.

Master Plan sections 4.5 and 7.4, tasks P1-D08 (service) and P1-D09 (mesh clean-up).
"""

# Part of the cache key (section 7.4). Raise it whenever clean-up or a generator
# changes what a given photo produces, so stale cached meshes are not served.
PIPELINE_VER = "3"

__all__ = ["PIPELINE_VER"]
