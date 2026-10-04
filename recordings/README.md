# recordings/

PLACEHOLDER (lane C, P1-X07): contract surface only; replaced by lane C's implementation.

The files here are lane B's stand-ins for lane C's fixtures (Master Plan App. C.8). They are written by
`tools/placeholders/make_fixtures_c.py` (stdlib only, deterministic) and are replaced file-for-file by lane C's real ones.

- `room_3x3.json`, `room_3x4_L.json`, `room_4x5_table.json`, `budget_room.json`: RoomModel JSON, schema `atlas.room/1`
  (Master Plan §4.3). Anchor frame: origin at the free rectangle's centre on the floor, +x along Wf, +z along Df, +y up, metres.
  Distance field: base64 float32 little-endian, row-major (row = z index), metres. The aligned grid of `room_4x5_table`
  follows the "2 x 2" comment of §4.7 (PH-22). `budget_room` objects carry the hash `budget-standin-5000` (§4.3, PH-23).
- `walk_lap5_3x3.poselog`, `walk_still_60s.poselog`: pose logs, format `# atlas.poselog/1  t_s x y z qx qy qz qw tracking`
  (PH-24). 60 Hz, head y = 1.6 m.
