# Lane B phone and integration checks (deferred acceptance)

## P2-B02 Corridor and chamber kit (acceptance is Editor; integration check pending other groups)
- Everything in the acceptance cell passes in EditMode: `Atlas.Blades.Lung.AirwayViews.Tests` (kit tests), evidence in `docs/laneB/evidence/P2-B02/`.
- **Integration check** (Editor, once P1-B06, P2-B01 and P2-B03 are merged, in G6):
  1. Run the Play-mode harness in `Space_CurvedWalk` with the fixture tree and the lazy window.
  2. Walk the fixture route on the centre strip (6 forks, PH-39) with looks down to -90 deg.
  3. Read `UnityEditor.UnityStats.drawCalls` per frame for the wing renderers.
  4. Expected: <= 150 per frame (both eyes) at `curved.cullRho` = 3, and <= 106 by the bound.
- **Headset look** (an observation at the P2-B12 run, not acceptance): sibling corridors visible through membranes; no flicker between strip, glyph and floor; no gaps at corridor corners or at tile edges; the valve shell fully closed, with no world visible while it is opaque.

## G10 Lung airway and sky

### P2-B05 Mode switch (deferred: phone in headset)
- Scene: Lung_Airway.unity over the scanned room; Console connected; route `lung.route` = 0.
- Steps: walk two chambers in Mode 1; while walking the third corridor set `curved.mode` = 2 on the Console; enter the next chamber;
  then set `curved.mode` = 1 and enter the following chamber.
- Expected: the switch happens only inside a chamber with the shell opaque (nothing jumps outside it); the address on the Console
  and Spectator twin (fork events) is unchanged across both switches; the walk continues to the next fork. Record: address before
  and after each switch, frame of switch. 2 of 2 switches keep the address.
