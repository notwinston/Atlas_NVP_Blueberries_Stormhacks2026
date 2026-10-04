# Sources of the ATLAS master plan

`../ATLAS_Master_Plan.md` is assembled from the files in this folder. Edit here, then rebuild; do not edit the assembled file by hand, because its task tables, diagrams, wave table and area maps are generated.

| What | Where |
|---|---|
| Sections written by hand | `parts/` (title, §0–§6, §8, §9 prose, §14, Appendix A) and `clean/` (§7, §10–§13, Appendices B–F) |
| Phase 1 tasks, as data | `wbs/wbs_p1.py`: one `task(id, lane, title, depends on, output, acceptance test, runs on)` per task |
| Phase 2–4 work packages | The tables inside `clean/10a_playground.md`, `10b_lung.md`, `10c_house_fieldfit.md` and `13_demo_docs.md`; the "Depends on" cell drives the diagrams |
| Work areas for people | `wbs/areas.md`: per phase and lane, the human-sized pieces and the tasks inside each |
| Generators | `wbs/gen_wbs.py` (Phase 1 tables, lane diagrams, area overview, waves, gates), `wbs/gen_later.py` (Phase 2–4 diagrams and area maps), `assemble.py` |
| Checks | `check/tables.py` (table shape), `check/validate.py` (tables, task ids, section references, images, banned words, and with `--mermaid` every diagram) |

Rebuild from this folder:

```sh
cd wbs && python3 gen_wbs.py out && python3 gen_later.py ../clean out && cd ..
python3 assemble.py ../ATLAS_Master_Plan.md
python3 check/validate.py ../ATLAS_Master_Plan.md ../figures --mermaid
```

Then rebuild the team handout from the repository root: `python3 documents/tools/make_build_graph_pdf.py`.

The generators stop with a message if a dependency names a task that does not exist, if the task graph or the area graph has a cycle, or if a task is in no work area. Needs Python 3.9 or newer; the diagram check needs Node.js (`npx`).
