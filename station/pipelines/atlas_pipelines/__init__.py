"""ATLAS Station offline pipelines (Master Plan 10.2.4): ``atlas case <step>``.

Shared modules
    paths     default directories, case and work directory helpers
    cache     input-hash caching of steps (StepCache, run_cached)
    labels    the exact on-screen label, footer, licence and credit strings
    contract  canonical JSON, tube4d.bin, table.json, manifest.json (docs/case_contract.md)
    glb       dependency-free GLB 2.0 writer and reader with morph targets
    cli       the ``atlas`` console script; steps are discovered in atlas_pipelines/steps/
"""

__version__ = "0.1.0"
