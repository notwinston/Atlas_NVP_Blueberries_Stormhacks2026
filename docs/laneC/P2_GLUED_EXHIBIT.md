# Glued exhibit (P2-C12, P2-C13, P2-C14)

Files: `ATLAS/Assets/Atlas/Blades/Playground/Exhibits/Glued*.cs`. Tests: `ATLAS/Assets/Atlas/Tests/Blades/PlaygroundGlued/`.

| Row | Where | Checked |
|---|---|---|
| P2-C12 wrap counter, `glued.wrap`, `glued.mode`, `glued.task`, tasks | `GluedExhibitCore` (engine-free), `GluedExhibit` (adapter over `GluedSpace`) | headless: 10 of 10 crossings logged, counter == crossings, cell sides == RoomModel `cells.glued` (5 fixtures) |
| P2-C13 chiral kit | `GluedChiralKit` | headless, built-in meshes: determinant and landmark hand == (-1)^row for 100% of copies; walker copy in 24 other cells (26 in T3) |
| P2-C14 gluing switch, T3 look-up | `GluedGluingSwitch`, `GluedLookUp` | headless: look-up above 60 degrees in T3 shows the copy, 59 does not; bumpers/sign/lever mapping |

Pending (phone in headset): S1 frame time and draw calls against the section 8.5 rows. The exhibit adds no
geometry, so the counted model (`GluedBudget.Space`) is unchanged and tested; the measured values wait for a run.

Assumptions to confirm at integration: the lever click steps the gluing only after the wave task (before that it
is the prompt's Confirm); the gluing sign is a gaze target only (its look is P2-A04); `Atlas.Blades.Playground.asmdef`
gains `Atlas.Spaces.Glued` and `Unity.Mathematics` (other Playground workers add references to the same list).
