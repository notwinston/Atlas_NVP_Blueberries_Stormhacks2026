# Lane B phone and headset testing (deferred acceptance)

One section per task: scene, steps, expected numbers. A human fills in the measured values. The master list is
`docs/laneB/checklists/P3-B03-B05-B08_phone.md`.

## P1-B06 Tile renderer (deferred: phone A in the headset)

- Scene: `Assets/Atlas/Spaces/CurvedWalk/Scenes/CurvedWalk_TileProbe.unity` loaded additively over `Atlas_Boot`; L = 1.885 (room_3x3 L5) or the room's loop-fit L5; ρ (`cullRho`) = 3.
- Build: Release, Frame Timing Stats on, debugger detached, phone A in the closed headset, thermal Nominal at start. Safety as for G2 (MP §11.3): team walker only, cleared floor, spotter within arm's reach (Bump Guard is still a placeholder).
- Steps and table: items 1-6 of `docs/laneB/checklists/P1-B06_phone.md`.
- Expected (MP §9.5.6 P1-B06, §8.5, §11.2): tile-shell draws (2 × drawn) ≤ 150 per frame at every pitch; total draw calls ≤ 300; 60 fps (frame time p50 ≤ 16.7 ms, p95 ≤ 20 ms); 0 B GC on every crossing frame; crossing frame ≤ 2 ms (`Atlas.Tiles.Select` + `Atlas.Tiles.Upload`); screenshot: the edge of the drawn world cannot be told from the background (blocked until lane A's P1-A06 background gradient whose horizon colour equals `_FogColor`).
- A missed number is reported with its measured value and not relaxed (MP §9.9 rule 7).
