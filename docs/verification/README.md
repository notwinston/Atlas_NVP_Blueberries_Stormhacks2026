# Verification in the Unity Editor and on a phone

This folder is the hand-over between the session that writes the code (no Unity, no Xcode, no phone) and the
session on the Mac that can run them.

- `UNITY_SIDE_BRIEF.md`: the instructions for the Mac-side session. Start at its last section, "Paste this to start".
  It is rewritten from the code whenever the code moves; its opening lines name the commit it was last checked against.
- `report-YYYY-MM-DD.md`: what the Mac-side session found, one file per day, with the brief's template
  (step, expected, observed, evidence, fixed or open, severity). The code-writing session reads these and fixes what is open.
- `shots/`: screenshots, named `YYYYMMDD_<step>_<item>[_<room>].png`.
- `logs/`: Unity, test and Station logs kept as evidence, and the hand logs (`*.handlog`) copied off the phone for the bench of step 3i. Git ignores this folder; quote the lines that matter in the report.

Performance evidence for gate G4 does not go here: see `docs/evidence/README.md`. The bench numbers of the tracked hands (step 3i)
go in the report's table; the lead copies them into `docs/evidence/hands_bench.md`.
