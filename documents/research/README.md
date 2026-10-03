# Research memos behind the ATLAS master plan

Six memos written on 3 October 2026 while preparing `../ATLAS_Master_Plan.md`. The plan cites them as (R1) to (R6). Each memo tags every claim as verified against a primary source, secondary, derived, or estimated, and ends with a list of what could not be verified.

| Memo | Question it answers |
|---|---|
| `R1_freewalk_hyperbolic_scan.md` | How to walk freely in a scanned room mapped into hyperbolic space; what Holonomy (TU Delft) really does; scan options on the iPhone; the loop-fit rule |
| `R2_medical_4d.md` | What the lung's "4D" is, why it is useful, which public data can be used, and how to slice it |
| `R3_graph_generation.md` | Exact tile ids, tile counts, level of detail, portal room graphs, trees in the hyperbolic plane |
| `R4_phone_pc_link.md` | The phone-to-PC link: LiteNetLib, the Hub, message layout, iOS network rules |
| `R5_performance_metrics.md` | Budgets, latency, thermal behaviour, the metric set and how to capture it |
| `R6_world_generation_catalogue.md` | What spaces and tiles to generate and how; legibility and comfort rules |

The memos were written before some of the team lead's decisions (for example they still discuss a ghost-room guardian, passthrough and hour budgets). Where a memo and the plan differ, the plan wins.

`scripts/` holds the small Python scripts the memos used to check their numbers. They are our own work. `R2_4dlung_motion_table.json` is derived from the TCIA 4D-Lung collection (CC BY 3.0). No third-party source code or papers are stored here.
