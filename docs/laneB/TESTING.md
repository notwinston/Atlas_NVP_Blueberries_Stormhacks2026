# Lane B phone and headset testing (deferred acceptance)

One section per task: scene, steps, expected numbers. A human fills in the measured values. The master list is
`docs/laneB/checklists/P3-B03-B05-B08_phone.md`.

## P1-B06 Tile renderer (deferred: phone A in the headset)

- Scene: `Assets/Atlas/Spaces/CurvedWalk/Scenes/CurvedWalk_TileProbe.unity` loaded additively over `Atlas_Boot`; L = 1.885 (room_3x3 L5) or the room's loop-fit L5; ρ (`cullRho`) = 3.
- Build: Release, Frame Timing Stats on, debugger detached, phone A in the closed headset, thermal Nominal at start. Safety as for G2 (MP §11.3): team walker only, cleared floor, spotter within arm's reach (Bump Guard is still a placeholder).
- Steps and table: items 1-6 of `docs/laneB/checklists/P1-B06_phone.md`.
- Expected (MP §9.5.6 P1-B06, §8.5, §11.2): tile-shell draws (2 × drawn) ≤ 150 per frame at every pitch; total draw calls ≤ 300; 60 fps (frame time p50 ≤ 16.7 ms, p95 ≤ 20 ms); 0 B GC on every crossing frame; crossing frame ≤ 2 ms (`Atlas.Tiles.Select` + `Atlas.Tiles.Upload`); screenshot: the edge of the drawn world cannot be told from the background (blocked until lane A's P1-A06 background gradient whose horizon colour equals `_FogColor`).
- A missed number is reported with its measured value and not relaxed (MP §9.9 rule 7).

## P1-B09 Curvature dial and the 3-4-5 walk (deferred: phone A in the headset, G7 integrated build)

- Scenes: `Assets/Atlas/Spaces/CurvedWalk/Scenes/Space_CurvedWalk.unity` and `Assets/Atlas/Spaces/SmallPlanet/Scenes/Space_SmallPlanet.unity`, loaded by `Atlas_Boot` (lane A) with the live RoomModel (lane C). Each scene holds one component (`CurvedWalkSpaceScene`, `SmallPlanetSpaceScene`) whose `Module` lane A registers with `SpaceLab`.
- Host wiring (lane A): `SpaceRequested` to `SpaceLab.Request`; `Home` = `SpaceLab.Home`; `Actions` = lane C's action map. Without `Actions` the dial still follows `curved.q` (Console / registry).
- Safety as for G2 (MP §11.3): team walker, cleared floor, spotter within arm's reach.
- Steps:
  1. Stand at the room centre, dial to 5 (D-pad), wait about 4 s for the ramp, walk to the flag, follow the beacons round one lap and on to the fifth corner.
  2. Return to the centre, dial to 3 (S4 loads once K has reached 0), walk 3 corners.
  3. Dial to 4 and walk 4 corners.
  4. Repeat step 1 pressing D-pad left/right while walking.
  5. Load a standing RoomModel (min(Wf, Df) < 1.5 m) and cycle the dial 3 to 6.
- Expected (MP §9.5.6 P1-B09, §10.1.3, §11.2):
  - `walk.closed` `closure_m` < 0.25 at q = 5, 4 and 3.
  - `walk.lap` `turn_deg` 90 +- 5 at q = 5 (the world-turn read-out, logged).
  - Zero frames in `state.bin` plus the head-pose log where `p[0]` changes while head speed exceeds 0.1 m/s.
  - D-pad presses while walking change nothing; the same presses on a still frame do.
  - Standing room: the dial snaps 3..6, q = 5 reaches K = -1/1.4^2, no flag or beacon is visible, at q = 3 S4 is still requested.
- The read-outs are not drawn as text (no text package, same open lead item as P3-B01; deviation D7): read `walk.corner` / `walk.lap` / `walk.closed` on the Console or in the event log.
- Editor-proved here: `Atlas.Spaces.CurvedWalk.Tests` (closure q = 3, 4, 5, 6 on replayed walks, curvature frozen while head speed > 0.1 m/s, ramp duration, S4 request, standing room, logged events) and `Atlas.Kernel.Tests` (`CurvatureDialTests`, `LapWalkTests`, `HolonomyRigGraphTests`).

## P1-B11 The room becomes tile zero (deferred: phone A in the headset)

- Scene: `Assets/Atlas/Spaces/CurvedWalk/Scenes/Space_CurvedWalk.unity` with the live Home rebuild (P1-C07) as `Home` (`SpaceLab.Home`) and the live RoomModel.
- Steps:
  1. q = 4 (flat): touch three points on a wall and one table edge with the controller or a finger; note them.
  2. Dial 5, wait about 4 s, dial 4, wait about 4 s, touch the same points.
  3. Walk one lap, press Return Home (B held), touch the same points again.
  4. Run the WorldLint menu on the session RoomModel (`Atlas/World/Run WorldLint`).
  5. Walk the lap on the lap square and watch the tile id on the Spectator.
  6. Look at the start-corner post from the flag.
- Expected (MP §9.5.6 P1-B11, §4.6, §3.5, A.4):
  1. Each point within 1 cm of the real one (the room at kappa = 0 is exactly Home).
  2. Within 2 cm; `LastRegistrationErrorM` is logged by the space (read it in the test build's debug overlay or the event log).
  3. Within 2 cm after Return Home; `SelectedQ` reads 4.
  4. `PropTilesInFootprint` 0 and `FootprintTilesNotHome` 0.
  5. No flicker: every tile run is at least 0.25 s (15 frames at 60 Hz); the sequence is tile zero, then one neighbour per side, back to tile zero.
  6. The corner post is 0.17 m from the flag at L = 1.88 m (0.0920553 L) and fades once lane A's near-head fade lands (the 0.3 m fade is in lane A's AtlasCore, deviation D13).
- Editor-proved here: `Atlas.Spaces.CurvedWalk.Tests` (`CurvedRoomViewTests`, `TileZeroTests`), `Atlas.WorldGen.Tests` (`FootprintLintTests`) and `Atlas.Kernel.Tests` (`TileZeroPlacementTests`).
