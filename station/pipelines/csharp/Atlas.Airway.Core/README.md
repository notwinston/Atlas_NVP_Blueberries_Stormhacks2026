# Atlas.Airway.Core

Engine-free core of the airway wing (Lung Mission beat b). Plain C# (netstandard2.1, C# 9, `System` plus the
`Atlas.Cases` JSON reader), no Unity types, time-free (time is an argument), deterministic. The airway is a **model**:
every node is `source: "model"`, the label is "Model-generated airway", nothing is measured on a person and no clinical
claim is made.

## Work packages served

- **P2-B01 `AirwayTree`** (the part that needs no tile graph): `tree.json` reader; resolver of rule `atlas.airway/2`
  (any address over {L, R} up to 23 letters, lazily, no stored tree); address -> Rule S steps; lazy window.
- **P2-B06 Beacon and route cost**: next step by prefix comparison (with backing up when off the route), the 8 s rule,
  the `fork {address, choice, correct, beacon_shown, t_s}` payload, the route cost read-out.
- **P2-B08 Two Compasses**: Euclidean compass, beacon choice, margin, the `compass_choice` payload, the greedy study.

## Files

| File | What |
|---|---|
| `AirwayAddress.cs` | `AirwayAddress` (bits + length, L = 0, R = 1, first letter most significant; `default` is the trachea): `Child`, `Parent`, `Sibling`, `Prefix`, `IsPrefixOf`, `CommonPrefixLength`, `NodeIndex` (index in `tree.json` `nodes`), `Parse` / `TryParse` / `ToString`. `AirwayChoice` {None, Left, Right} |
| `Sha256.cs` | SHA-256 with its buffers in the instance (no allocation per hash); checked against `System.Security.Cryptography` |
| `AirwayResolver.cs` | `Vec3d`, `AirwayNode` {Address, Gen, LenMm, RadiusMm, PosMm, Dir, Normal; `Source` = "model"}, `AirwayResolver(seed)`: `Node(address)`, `Child(parent, right)`, `Uniforms`, `DiameterRatioOf`, `PlaneRotationDeg`, `BranchAngleDeg`. Port of `atlas_pipelines/airway.py`, same operation order |
| `AirwayTreeFile.cs` | `AirwayTreeFile.Parse` (schema `atlas.tree/2`: nodes, target, compass fork and margin, candidates, routes) and `GreedyFile.Parse` (`atlas.greedy/1`). Rejects another schema, rule or label, a node whose source is not "model", nodes out of order |
| `AirwaySteps.cs` | `AirwayStep` {TurnLeft, TurnRight, Straight}; `AirwaySteps.Count / StepAt / Fill / Fold / ExitEdge`; `AirwayWindow.Fill` |
| `Beacon.cs` | `Beacon.NextStep / Apply / GenerationsToTarget / Shown / IsCorrect / Fork`; `RouteCost` |
| `TwoCompasses.cs` | `TwoCompasses.Scores / Compass / BeaconChoice / Read / At / Choice`, `CompassReading`; `GreedyStudy.Target / Run` |
| `AirwayEvents.cs` | `ForkEvent`, `CompassChoiceEvent`, their JSON, the event names |

## Resolver and what "equal" means

`AirwayResolver.Node(address)` walks the address from the trachea, one rule step per letter; a step hashes three
strings (the node, its sibling, its parent). The resolver keeps the nodes along the last address it resolved, so walking
one generation further costs one step, and nothing is allocated after construction. One resolver per thread.

Measured here (Linux aarch64, .NET 10, CPython 3 with the same libm):

- against the Python resolver: **bit-equal doubles** on 2,200 of 2,200 values of the committed 200 addresses and on
  22,000 of 22,000 values of 2,000 fresh addresses of generations 11 to 16 (radius, length, position, direction, normal).
  The asserted tolerance is 1e-9 (mm, and absolute for the unit vectors); the measured worst difference is 0.
- against `tree.json`: 16,376 of 16,376 values (2,047 nodes x radius, length, 3 position, 3 direction) are equal after the
  file's own 1e-6 quantisation. The raw difference is at most 5.0e-7, which is the file's rounding, not the resolver's.

Bit-equality rests on the runtime's `pow`, `acos`, `cos`, `sin`, `log` and `sqrt` giving the same doubles as the
Station's and on no fused multiply-add being introduced. **Not checked on the phone**: IL2CPP on arm64 uses another
libm (bionic) and its C++ compiler may contract `a * b + c`. Differences there would be in the last bits (far below
1e-9 mm at generation 16), but the compass at a fork whose two scores are nearly tied could in principle flip. The
designed fork has margin 0.142047, so it cannot; for anything shown as a number from `greedy.json`, quote the file.
Derive deep nodes from the rule, never from the quantised file values (`Child` refuses a parent read from the file).

## Rule S: address -> steps -> tile ids (the half that needs Phase 1)

The plan (10.2.3): "each generation is one turn tile (left or right) plus one straight tile; the next fork is the tile
at the end. ... fork tile id = `FrameOf(address, trachea)`". This core gives the steps and stops there; tile ids
belong to the kernel's {4,5} `ITileGraph` (`P1-B04`).

`AirwaySteps.StepAt(address, i)`: for letter j of the address, step 2j is `TurnLeft` (L) or `TurnRight` (R), step
2j + 1 is `Straight`. Two tile steps per generation: 8 on the four-fork route, 2 on the one-fork route, 32 to
generation 16, 46 to generation 23.

How it plugs into `FrameOf`, in the conventions of `docs/contracts/lane-b-kernel.md` section 1.6:

```csharp
// state: the tile the walker is in and the edge through which it was entered
struct TileStepper : IAirwayStepper<(TileId tile, int entry)> {
    public ITileGraph Graph;
    public (TileId, int) Apply((TileId tile, int entry) s, AirwayStep step) {
        int exit = AirwaySteps.ExitEdge(s.entry, step);          // left (b+3)%4, straight (b+2)%4, right (b+1)%4
        return (Graph.Neighbour(s.tile, exit), Graph.BackEdge(s.tile, exit));   // never compute the back edge
    }
}
var fork = AirwaySteps.Fold(address, (tracheaTile, tracheaEntryEdge), new TileStepper { Graph = h45 });
```

`ExitEdge` is the contract's own rule ("after entering a tile through its edge b, leave through edge (b + 3) mod 4"
turns left), extended to straight and right. **My reading, to be confirmed in the Editor:** the turn is taken when
leaving the fork tile (through its left or right edge), and the straight step continues through the edge opposite the
entry edge, so fork, turn tile and next fork lie on one geodesic 2 x 1.0613 = 2.1226 units long, the plan's fork
spacing; the edge ahead of a fork tile is solid tissue. The plan's check "32,767 nodes to depth 14 map to distinct
ids" is the test of that reading and needs the {4,5} graph; it is **not run here** (the runner prints NOT RUN). What is
asserted here is that the 32,767 addresses give 32,767 distinct step sequences.

Lazy window: `AirwayWindow.Fill(current, maxGen, span)` writes "current node, 2 ancestors, descendants to depth 3":
at most 17 addresses (34 tiles), inside the plan's bound of 60 nodes and 120 tiles.

## Beacon, fork event, route cost

- `Beacon.NextStep(walker, target)`: `Arrived` when equal, `Left` / `Right` (the target's next letter) when the walker's
  address is a proper prefix of the target's, otherwise `Back` (go to the parent). Integer comparison only, exact.
- `Beacon.Shown(secondsAtFork, isTwoCompassesFork)`: `secondsAtFork >= 8` and not the Two Compasses fork
  (`AirwayTreeFile.IsTwoCompassesFork`). The adapter owns the clock.
- `Beacon.Fork(fork, choice, target, secondsAtFork, isTwoCompassesFork)` -> `ForkEvent`; `ToJson()` gives
  `{"address":"RR","choice":"R","correct":true,"beacon_shown":false,"t_s":3.25}`. `correct` = the choice is the beacon's
  step; at a fork off the route no child is correct. `t_s` is taken as the seconds from entering the fork chamber to the
  choice (the plan gives the field name only).
- `RouteCost` (plan: "Insertion depth (Σ len_mm); sharp turns (branch angle > 45°, our definition); narrowest lumen against
  scope outer diameter 3.5 / 4.2 / 4.0 mm"): `InsertionDepthMm` sums every branch on the route, the trachea (120 mm) and
  the last branch included; `SharpTurns` counts branches leaving their parent at more than 45 degrees; `NarrowestLumenMm`
  is the smallest diameter (2 x `radius_mm`) with its generation; `Passes(od)`. `Start` / `Add` update it one branch at
  a time while walking. `SymmetricFirstGenerationBelow(4.2)` is 7, the plan's derived number, asserted exactly.
  Model values of the default route `RRRL`: 254.964 mm, 2 sharp turns, narrowest lumen 7.731 mm.

## Two Compasses

At fork `A` towards target `T`: `t = pos_mm(T) - pos_mm(A)`; the Euclidean compass is the child with the larger
`dot(dir(child), t)`, none on an exact tie; the beacon is `T[len(A)]`; `margin = (dot(dir(off), t) - dot(dir(on), t)) / |t|`,
positive exactly where the compass is wrong (`docs/case_contract.md` 10.3). `TwoCompasses.At(resolver, fork, target)`
computes it from the rule; `At(tree, fork, target)` from the file's values.

`compass_choice`: the plan names the event and no fields. `choice` (text) and `correct` are the two members
`atlas_pipelines/pilot.py` reads; this core writes
`{"address":"","choice":"beacon","correct":true,"taken":"R","compass":"L","beacon":"R","margin":0.142047,"t_s":6.50}`,
with `choice` one of `beacon`, `compass`, `both`, `neither`. **Open points for lane D**: the extra members; `choice` of a
`fork` event is the letter `"L"` / `"R"` and `address` a string over {L, R} (the synthetic pilot session uses 0 / 1 and
digit strings, which `pilot.py` does not interpret); `compass_choice` has an underscore, which the event-name pattern of
`docs/contracts/lane-d-link.md` section 2.6 does not allow. The names are constants in `AirwayEvents`.

## Unity side (what the adapter still has to do)

Intended home: the lane-B lung assembly (proposed `Atlas.Spaces.Lung`), next to a copy of `Atlas.Cases`. The `.cs`
files move in unchanged.

1. Read `airway/tree.json` as bytes, `AirwayTreeFile.Parse`; one `AirwayResolver(tree.Seed)` on the main thread.
2. `AirwayTree` proper: fold `AirwaySteps` through the {4,5} graph as above for tile ids; run the plan's distinct-id test.
3. Each frame: `AirwayWindow.Fill` for the nodes to draw, `Beacon.NextStep` for the arrow, `Beacon.Shown` with the
   seconds the walker has stood in the fork chamber, `RouteCost` for the read-out. None of these allocates.
4. On a choice: `Beacon.Fork(...).ToJson()` under `AirwayEvents.ForkName`; at `tree.CompassFork` also
   `TwoCompasses.Choice(reading, taken, seconds).ToJson()` under `AirwayEvents.CompassChoiceName`.
5. Arrow, floor glyphs, chambers, valves, the endoluminal window and the mapping of `pos_mm` / `dir` into the window's
   camera are the adapter's (`P2-B02` to `P2-B07`).

## Acceptance checks

Run here (`station/pipelines/tests/test_p2_b_airway_core.py` builds and runs `Atlas.Airway.Core.Tests`; the committed
copies in `tests/fixtures/p2_b_airway/` are byte-equal to the built `lung101` package, which is checked when
`ATLAS_CASES_DIR` is set):

| Check | Result |
|---|---|
| P2-B01 row: "32,767 nodes to depth 14 map to distinct ids; window ≤ 60 nodes" | 32,767 addresses, 32,767 distinct step sequences; largest window 17 nodes. Distinct **tile ids**: NOT RUN (needs `P1-B04`, Editor) |
| Resolver = `tree.json` on every materialised node | 2,047 nodes, 16,376 / 16,376 values equal after the 1e-6 quantisation; worst raw difference 5.0e-7 (the file's rounding) |
| Resolver = Python on 2,000 random addresses, generations 11 to 16 | 22,000 / 22,000 doubles bit-equal; asserted ≤ 1e-9 |
| P2-B06 row: "Beacon step equals the address-prefix step on 1,000 random targets" | 1,000 targets, 33,000 walker positions on and off the route, 0 mismatches; 1,000 walks from random branches reach the target in the fewest steps |
| "the lumen falls below 4.2 mm at generation 7" (symmetric model) | 7 (4.5000 mm at generation 6, 3.5717 mm at generation 7) |
| P2-B08 row: "Compass and beacon point to different children at the designed fork" | fork `""`: compass L, beacon R; forks `R`, `RR`, `RRR`: same child |
| Margins equal `tree.json` | 0.142047 at the designed fork; candidates LRLL 0.048798, RRRL 0.142047, RRRR 0.113102, all reproduced |
| Greedy study re-run over the same 1,000 targets | 1,391 wrong forks of 16,000 (file: 1,391), share 0.086938, 823 routes, 16 / 16 generations equal |
| Allocation | 0 bytes in 20,000 resolutions and in 20,000 beacon + fork + compass + route cost + window + steps calls |

Still needs the Editor or the phone: tile ids and the distinct-id test (`P1-B04`); the reading of Rule S above; P2-B06
arrow and read-out on screen; P2-B08 "phone in headset" (the fork UI and that the walker sees the two compasses
disagree); resolver bit-equality under IL2CPP on the device; the 0 allocation claim under IL2CPP.
