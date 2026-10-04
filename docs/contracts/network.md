# Network Atlas contract (`atlas.network/1`)

Frozen for tasks NET-01 to NET-11 of the Network Atlas design (`documents/network_atlas/ATLAS_Hyperbolic_Network_Design.md`,
cited as §n). This page replaces per-task briefs: each task reads its own section (section 7), the data and API
sections it depends on, and the conflicts in section 9. Nothing here changes a doc name, constant or tolerance; where
the doc leaves a detail open, the choice is fixed here once so that every task makes the same one, and is marked ◆.

Reference implementation: `documents/network_atlas/hypernet.py` (imported as a module; its `main()` needs
matplotlib and is never run). Spec: §3 (the model), §6 (architecture), §7 (plan), §8 (claims).

## 0. Ground rules for every NET task

- **One shared worktree.** All coders edit the same tree at the same time. Edit only the files your section owns
  (section 7). Never touch, reformat or revert another task's files. No state-changing git command at all.
- **Binding rules:** contracts F1 to F9 ([README.md](README.md)): blades never reference `Atlas.Scan`, `Atlas.Safety`
  or `Atlas.App` (stricter than the F1 `Atlas.Blades.*` row, which allows Scan and Safety); engine-free core (F2);
  everything built in code (F3); one shader, pooled materials, no `MaterialPropertyBlock` (F7); no allocation per
  frame; §8.5 budgets; §10.1.4 comfort. The doc's names, constants and tolerances verbatim; nothing beyond the doc.
- **C# checks:** `python3 tools/headless/verify.py <build|test|rules> ... --tag na-<TASK>` (always your own tag). An
  error only in files you do not own means another task is mid-edit: wait (`verify.py wait <Assembly>`) and retry.
- **Python:** `/Users/lucas/Developer/StormHacks_2026_Blueberries/station/pipelines/.venv/bin/python` (numpy; no
  matplotlib, no jsonschema; never pip install), run from `station/pipelines` with `PYTHONPATH=.`.
- **Console:** `npm test` in `station/console-tests`. **Hub:** `dotnet test station/hub.tests`.
- **Editor-only or phone-only acceptance items:** implement them, add a headless proxy test where possible, and list
  them as deferred in your report.

## 1. Constants

| Name | Value | Source |
|---|---|---|
| Default network | N = 200, k̄ = 6, γ = 2.5, T = 0, ζ = 1 | §3 |
| Fixture and bundled seeds | 7, 83 | ◆ |
| Disc radius R for the defaults | 10.504399538733493 (10.504 rounded) | §3, eq. 13 |
| Study pairs | 2,000 (`study.pairs`) | §6.3 |
| Greedy path limit | 200 path entries (`hypernet.greedy(limit=200)`) | reference |
| Floor disc | 2.4 m across (radius 1.2 m), never beyond the free rectangle ◆ (section 6.2) | §4 V2 |
| Gaze dwell / re-centre / hop fade | 0.7 s / `net.recentreSeconds` (0.6 s) / 0.25 s out + 0.25 s in | §4 |
| Gaze node targets | at most `net.maxGazeNodes` (64), the nodes nearest the centre after projection | §4, §9 |
| Edge sampling | 16 segments per edge (17 points), the method of `hypernet.geodesic_disk` | §4 V2 budget |
| Screen mapping of the disc | half extent 1.03 (`FlatTwinDisc.ViewHalfExtent`) ◆ (section 6.1) | Figure 1 |
| Event scene | `"net"` ◆ | lane D contract (blade events) |

## 2. `atlas.network/1`: the network file

### 2.1 Shape

Schema: [network.schema.json](network.schema.json) (JSON Schema 2020-12). Example (§6.3, values abbreviated):

```json
{
  "schema": "atlas.network/1",
  "model": { "n": 200, "kbar": 6, "gamma": 2.5, "T": 0, "seed": 7, "zeta": 1, "R": 10.504399538733493 },
  "nodes": [ { "id": 0, "r": 9.84, "theta": 1.23, "degree": 4, "label": "AS-0" } ],
  "edges": [ [0, 6], [1, 43] ],
  "study": {
    "pairs": 2000,
    "hyperbolic":   { "success": 1.0,   "stretch": 1.0 },
    "flat_picture": { "success": 0.741, "stretch": 1.0256410256410255 }
  }
}
```

### 2.2 Field rules

| Field | Rule |
|---|---|
| `model.n`, `seed` | integers. `zeta` is the integer 1 |
| `model.kbar`, `model.T` | written as an integer when whole (`6`, `0`), otherwise as a float ◆ |
| `model.gamma` | float (`2.5`) |
| `model.R` | float, full precision: `2 ln(N / ν)` of §3 |
| `nodes` | all N nodes (isolated nodes included, degree 0), in id order; `nodes[i].id == i` |
| `nodes[i].r`, `theta` | the generated radius and angle, full precision; `0 ≤ theta < 2π` |
| `nodes[i].degree` | number of edges at node i |
| `nodes[i].label` | `"AS-<id>"` |
| `edges` | every edge once as `[i, j]` with `i < j`, sorted by `i` then `j` (the order of `numpy.nonzero(numpy.triu(A, 1))`) |
| `study` | `hypernet.route_stats(g, 2000, seed)`: `hyperbolic` is its `hyperbolic`, `flat_picture` its `euclidean_disk`; `success` the share delivered; `stretch` the mean hop stretch over delivered packets (`null` if none) |

Note on `study`: `route_stats` draws its pairs from the giant component with `numpy.random.default_rng(seed + 1)`,
2,000 pairs for the hyperbolic rule first, then 2,000 **further** pairs for the flat rule from the same generator.
The two rules are therefore measured on different pairs. Ports must keep this to reproduce the file.

### 2.3 Bytes

Every network file and every fixture of section 4 is written as ◆

```python
(json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")
```

Sorted keys, no whitespace, floats as Python `repr` (shortest round trip, full double precision), one trailing LF.
`contract.dumps_canonical` is **not** used: it rounds floats to 1e-6, and section 5 needs 1e-9. Readers (C#,
JavaScript) parse doubles with round-trip precision (`double.Parse(..., CultureInfo.InvariantCulture)`, `JSON.parse`).

### 2.4 Where the files are

| What | Path |
|---|---|
| Schema | `docs/contracts/network.schema.json` |
| Base fixtures (section 4) | `station/pipelines/tests/fixtures/network/` |
| Bundled networks (phone, Spectator, Hub) | `ATLAS/Assets/StreamingAssets/networks/network_7.json`, `network_83.json`, each with a `DefaultImporter` `.meta` like `StreamingAssets/cases` |
| `net.file` option order | ascending seed: index 0 = `network_7.json` (default), 1 = `network_83.json` |

## 3. Station interfaces

### 3.1 Python: `station/pipelines/atlas_pipelines/network.py` (NET-01)

A port of `hypernet.py` into the `atlas_pipelines` package, with the same numerics and random streams. Exact API:

```python
SCHEMA = "atlas.network/1"
STUDY_PAIRS = 2000
GREEDY_LIMIT = 200
BUNDLED_SEEDS = (7, 83)
DEFAULTS = dict(n=200, kbar=6, gamma=2.5, T=0)

def radius(n: int, kbar: float, gamma: float, T: float) -> tuple[float, float]       # (R, alpha), §3 rows 1-4
def link_probability(x, R: float, T: float)                                         # eq. 41, numpy-broadcast; T > 0
def generate(n: int, kbar: float, gamma: float, T: float, seed: int) -> dict         # keys r, th, R, alpha, A, D (as hypernet)
def distance_matrix(r, th) -> np.ndarray                                             # eq. 5, the hypernet expression
def poincare(r, th) -> tuple[np.ndarray, np.ndarray]                                 # tanh(r/2)(cos, sin)
def flat_matrix(r, th) -> np.ndarray                                                 # np.hypot of poincare differences
def giant(A) -> np.ndarray
def bfs(A, s: int) -> np.ndarray
def greedy(A, metric, s: int, t: int, limit: int = GREEDY_LIMIT) -> tuple[list[int], bool]
def route_stats(g: dict, pairs: int, seed: int) -> tuple[dict, np.ndarray]           # as hypernet (keys hyperbolic, euclidean_disk)
def geodesic_disk(r1, t1, r2, t2, d, k: int = 24) -> tuple[np.ndarray, np.ndarray]
def mobius(x, y, a: complex) -> tuple[np.ndarray, np.ndarray]
def ccdf_slope(degrees, kmin: float) -> float                                        # section 3.1.1
def to_document(g: dict, n: int, kbar: float, gamma: float, T: float, seed: int, pairs: int = STUDY_PAIRS) -> dict
def build(seed: int, n: int = 200, kbar: float = 6, gamma: float = 2.5, T: float = 0, pairs: int = STUDY_PAIRS) -> dict
def dumps(doc: dict) -> bytes                                                        # section 2.3
def write(doc: dict, path) -> None
def load(path) -> dict
def validate(doc, schema: dict | None = None) -> list[str]                           # [] when valid; section 3.1.2
def schema_path() -> pathlib.Path                                                    # docs/contracts/network.schema.json
def bundle_dir() -> pathlib.Path                                                     # ATLAS/Assets/StreamingAssets/networks
```

Command line (NET-01 adds the group to `atlas_pipelines/cli.py`):

```
atlas network build --seed 7 [--n 200 --kbar 6 --gamma 2.5 --T 0 --pairs 2000] --out DIR   # writes DIR/network_<seed>.json
atlas network bundle                                                                       # writes seeds 7 and 83 into bundle_dir()
```

`build(seed)` must return a document whose `dumps` equals the committed `network_<seed>.json` byte for byte.

#### 3.1.1 CCDF tail slope ◆

`ccdf_slope(degrees, kmin)`: drop zero degrees; `ks` = the distinct degrees `≥ kmin`; `P(k)` = share of the remaining
nodes with degree `≥ k`; return the slope of the least-squares line through `(ln k, ln P(k))` (`numpy.polyfit`, degree 1).
The acceptance test uses `kmin = k̄ = 6` on the pooled degrees of seeds 0 to 9 (section 9, C2).

#### 3.1.2 `validate`

No jsonschema package is available. `validate` walks `network.schema.json` itself and supports exactly the keywords
that file uses: `type` (string or list; `integer` excludes bool and accepts only ints; `number` excludes bool), `const`,
`required`, `properties`, `additionalProperties: false`, `items`, `minItems`, `maxItems`, `minimum`, `maximum`,
`exclusiveMinimum`, `exclusiveMaximum`, `minLength`, `$ref` to `#/$defs/...`. It also checks what JSON Schema cannot:
`nodes[i].id == i`, `len(nodes) == model.n`, every edge `i < j < n`, edges sorted and unique, each `degree` equal to the
count of edges at the node. Messages name the JSON path (`"nodes[3].r: below minimum 0"`).

### 3.2 Hub: `GET /api/network` (NET-03)

Served from `<HubOptions.ProjectFolder>/Assets/StreamingAssets/networks/` (`--project`; default `../../ATLAS`).

| Request | Response |
|---|---|
| `GET /api/network` | `200 {"files": ["network_7.json", "network_83.json"]}`: every file matching `^network_(\d+)\.json$`, ascending seed |
| `GET /api/network?file=network_7.json` | `200`, `Content-Type: application/json`, the file's bytes unchanged |
| `?file=` not matching `^network_\d+\.json$` | `400` with the Hub's usual `Error` body |
| `?file=` matching but absent | `404` with the Hub's usual `Error` body |

### 3.3 Console: `station/hub/wwwroot/js/network.js` (NET-03)

ES module, pure functions exported for `node --test` (no DOM in them), drawing on a canvas in the style of `mapview.js`:

```js
export const VIEW_HALF_EXTENT = 1.03;          // section 6.1
export const EDGE_SEGMENTS = 16;
export function parseNetwork(json)             // -> {model, nodes, edges, study, adjacency}; throws on a bad schema field
export function project(r, theta)              // -> [x, y] = tanh(r/2)(cos θ, sin θ)
export function mobius(x, y, ax, ay)           // -> [u, v] = (z − a)/(1 − ā z)
export function toScreen(x, y, size)           // section 6.1
export function distance(net, a, b)            // eq. 5, same expression and order as section 5.1
export function flatDistance(net, a, b)        // Euclidean in the Poincaré picture
export function greedyRoute(net, src, dst, rule)   // rule 'hyperbolic' | 'flat' -> {path, success, hops}; section 5.3 rules
export function shortestHops(net, src, dst)    // BFS
export function geodesicPoints(net, a, b, k = EDGE_SEGMENTS + 1)   // hypernet.geodesic_disk
export function drawNetwork(ctx, net, size, options)   // returns {nodes, edges, ms}
export function packetFrames(net, src, dst)    // hop-by-hop frames: hyperbolic route, then flat route
export function studyText(study)               // the study panel lines for both rules
```

## 4. Base fixtures (made from `hypernet.py`)

Written by `station/pipelines/tests/fixtures/make_network_fixtures.py` (owned by the architect; nobody edits it),
for seeds 7 and 83, γ 2.5, N 200, k̄ 6, T 0, into `station/pipelines/tests/fixtures/network/`:

| File | Content |
|---|---|
| `network_<seed>.json` | the `atlas.network/1` file (section 2). Also copied unchanged into `StreamingAssets/networks/` |
| `golden_distances_<seed>.json` | `{"seed", "network", "pairs": [[i, j, d], ...]}`: 500 pairs, `numpy.random.default_rng(seed + 2).choice(200, 2, replace=False)` each, `d = hypernet.distance_matrix(...)[i, j]` |
| `golden_routes_<seed>.json` | `{"seed", "network", "limit": 200, "routes": [{"src", "dst", "shortest", "hyperbolic": {"path", "success", "hops"}, "flat": {...}}]}`: 1,000 pairs of the giant component, `numpy.random.default_rng(seed + 3).choice(giant, 2, replace=False)` each; the same pair under both rules; `shortest` = BFS hops; `hops = len(path) − 1` |
| `golden_poincare_<seed>.json` | `{"seed", "network", "points": [[x, y], ...]}`: `hypernet.poincare` of every node, in id order |

Measured values (section 9 discusses seed 83):

| Seed | R | Edges | Mean degree | Max degree | Giant | Study hyperbolic (success, stretch) | Study flat (success, stretch) | Golden routes hyperbolic / flat success |
|---|---|---|---|---|---|---|---|---|
| 7 | 10.504 | 580 | 5.80 | 65 | 179 | 1.000, 1.000 | 0.741, 1.026 | 1.000 / 0.718 |
| 83 | 10.504 | 604 | 6.04 | 172 | 194 | 0.995, 1.000 | 0.735, 1.056 | 0.997 / 0.726 |

Seed 7 equals `documents/network_atlas/stats.json` (R 10.504, mean degree 5.8, maximum 65, giant 179, 580 edges,
success 1.0 / 0.741). Numerical margins in the goldens (checked when they were made): the smallest gap between the
best and second-best neighbour at any greedy step is 2.6e-6 (flat) and 2.9e-4 (hyperbolic), and a 1-ulp error in eq. 5
moves no golden distance by more than 1e-11, so a faithful C# or JS port reproduces them exactly.

## 5. Engine-free core (NET-05): exact public C# API

Assembly `Atlas.Blades.Network` (`ATLAS/Assets/Atlas/Blades/Network/Atlas.Blades.Network.asmdef`), folder `Core/`,
namespace **`Atlas.Blades.Network.Core`**. Only `System`, `System.Collections.Generic`, `System.IO`,
`System.Globalization` and `Unity.Mathematics` (`double2`, `double3`, `math`); no `UnityEngine` type; doubles
throughout; no allocation after construction except where marked. JSON is read with `Atlas.Link.AtlasJson`.

### 5.1 `NetworkModel`

```csharp
namespace Atlas.Blades.Network.Core
{
    public enum RoutingRule : byte { Hyperbolic = 0, Flat = 1 }      // net.rule option order

    public readonly struct NetworkStudy
    {
        public readonly int Pairs;
        public readonly double HyperbolicSuccess, HyperbolicStretch;  // stretch NaN when the file has null
        public readonly double FlatSuccess, FlatStretch;
        public NetworkStudy(int pairs, double hyperbolicSuccess, double hyperbolicStretch, double flatSuccess, double flatStretch);
    }

    public sealed class NetworkModel
    {
        public const string Schema = "atlas.network/1";
        public static readonly int[] BundledSeeds = { 7, 83 };
        public static string FileName(int seed);                     // "network_<seed>.json"
        public static NetworkModel Parse(string json);               // throws FormatException naming the bad field
        public static NetworkModel Load(string path);                // File.ReadAllText + Parse

        public int N { get; }                 // model.n (== NodeCount)
        public double Kbar { get; }
        public double Gamma { get; }
        public double T { get; }
        public int Seed { get; }
        public double R { get; }
        public int NodeCount { get; }
        public int EdgeCount { get; }
        public NetworkStudy Study { get; }

        public double Radius(int node);
        public double Theta(int node);
        public int Degree(int node);
        public string Label(int node);
        public int EdgeA(int edge);           // i of edges[edge] (i < j)
        public int EdgeB(int edge);           // j
        public int NeighbourCount(int node);
        public int Neighbour(int node, int k); // ascending node id, k in [0, NeighbourCount)

        public double Distance(int a, int b);       // precomputed at load from DistanceOf
        public double FlatDistance(int a, int b);   // precomputed at load from FlatDistanceOf
        public double3 HyperboloidPoint(int node);  // (sinh r cos θ, sinh r sin θ, cosh r), §3 last row

        // eq. 5 in exactly the reference's order of operations:
        //   dth = π − |π − |θa − θb||;  c = cosh(ra)·cosh(rb) − sinh(ra)·sinh(rb)·cos(dth);  d = acosh(max(c, 1))
        // with Math.Cosh, Math.Sinh, Math.Cos, Math.Abs, Math.Max, Math.Acosh and Math.PI. Distance(a, a) follows
        // the same formula and is not forced to 0, as in the reference.
        public static double DistanceOf(double ra, double ta, double rb, double tb);
        // ρ = tanh(r/2); x = ρ cos θ, y = ρ sin θ; return Math.Sqrt(dx·dx + dy·dy).
        public static double FlatDistanceOf(double ra, double ta, double rb, double tb);
    }
}
```

### 5.2 `PoincareMap`

```csharp
namespace Atlas.Blades.Network.Core
{
    public static class PoincareMap
    {
        public static double2 Project(double r, double theta);                // tanh(r/2) (cos θ, sin θ)
        public static double OneMinusRho2(double r);                          // 1 − tanh²(r/2), computed as 1 / cosh²(r/2)
        public static double2 Mobius(double2 z, double2 a);                   // (z − a) / (1 − ā z), complex arithmetic
        public static double2 MobiusInverse(double2 w, double2 a);            // (w + a) / (1 + ā w)
        // 1 − |Mobius(z, a)|², without cancellation: (1 − |a|²)(1 − |z|²) / |1 − ā z|²
        public static double MobiusOneMinusRho2(double2 z, double oneMinusZ2, double2 a, double oneMinusA2);
        // Hyperbolic distance of two disc points, stable form: 2 asinh(|z − w| / sqrt((1 − |z|²)(1 − |w|²)))
        public static double Distance(double2 z, double oneMinusZ2, double2 w, double oneMinusW2);
        public static double Distance(double2 z, double2 w);                  // same, 1 − |·|² from the points
        // The view centre a fraction t (0..1) of the way from `from` to `to` along their geodesic ◆:
        //   v = Mobius(to, from); s = 3t² − 2t³ (smoothstep, §10.1.4 comfort);
        //   c = MobiusInverse(tanh(s · atanh(|v|)) · v / |v|, from)   (c = from when |v| = 0)
        public static double2 EaseCentre(double2 from, double2 to, double t);
        // Re-centred picture of every node: points[i] = Mobius(Project(r_i, θ_i), centre),
        // oneMinusRho2[i] = MobiusOneMinusRho2(...). Arrays of length ≥ NodeCount; allocates nothing.
        public static void ProjectAll(NetworkModel model, double2 centre, double2[] points, double[] oneMinusRho2);
        // hypernet.geodesic_disk between two nodes (through the hyperboloid), k points, then re-centred on centre.
        // Writes k points into output starting at offset; allocates nothing.
        public static void Geodesic(NetworkModel model, int a, int b, double2 centre, int k, double2[] output, int offset);
    }
}
```

The view of the map is always `Mobius(z, c)` of the **original** points for the current centre `c` (never a
composition of earlier re-centres), so repeated re-centres do not drift.

### 5.3 `GreedyRouter`

```csharp
namespace Atlas.Blades.Network.Core
{
    public sealed class GreedyRouter
    {
        public const int Limit = 200;                       // path entries, as hypernet.greedy(limit=200)
        public GreedyRouter(NetworkModel model);            // allocates its BFS buffers and the giant component here
        public NetworkModel Model { get; }
        public int GiantCount { get; }
        public int GiantNode(int k);                        // the giant component, ascending id

        // Fills path[0..count) (path.Length ≥ Limit) and returns count; success when the last entry is dst.
        // Rules (hypernet.greedy): start at src; while v ≠ dst and count < Limit: if v has no neighbour, drop;
        // w = the neighbour (scanned in ascending id) with the strictly smallest metric(w, dst), the first one on a tie;
        // if metric(w, dst) ≥ metric(v, dst), drop; else append w. Metric: Distance for Hyperbolic,
        // FlatDistance for Flat. Allocates nothing.
        public int Route(int src, int dst, RoutingRule rule, int[] path, out bool success);
        public int ShortestHops(int src, int dst);          // BFS hops, −1 when unreachable; allocates nothing
        public static double Stretch(int hops, int shortest);   // hops / shortest (delivered packets only)
    }
}
```

## 6. Shared seams

### 6.1 Screen mapping of the disc (Console and Spectator) ◆

For a square drawing area of `size` pixels (origin top-left, y down): `scale = (size / 2) / 1.03`,
`px = size / 2 + scale · x`, `py = size / 2 − scale · y`, where `(x, y)` is the (re-centred) Poincaré point. Both NET-03
(`network.js toScreen`) and NET-04 (`NetworkDisc.ToScreen`) implement exactly this and each tests it against
`golden_poincare_7.json` at `size = 1000` to within **0.5 px**, so the two pictures agree to 1 px.

### 6.2 Floor disc in Home ◆

Centre at the room origin (F4: the centre of the free rectangle), on the floor; radius
`min(1.2, 0.5 · min(FreeRect.Wf, FreeRect.Df))` m, so the disc is 2.4 m across whenever the free rectangle allows it
and never leaves it. A disc point `(x, y)` lies at room `(x · radius, y_floor, y · radius)` (`+X` east, `+Z` along `Df`).

### 6.3 Parameters (§6.4), declared with `[AtlasParam]` (`Atlas.Telemetry`)

| Id | Field | Declaration |
|---|---|---|
| `net.file` | `NetworkFile` enum `{ Seed7 = 0, Seed83 = 1 }` | `[AtlasParam("net.file", 0)]` |
| `net.view` | `NetworkView` enum `{ Floor = 0, Walk = 1 }` | `[AtlasParam("net.view", 0)]` |
| `net.rule` | `RoutingRule` (section 5.1) | `[AtlasParam("net.rule", 0)]` |
| `net.recentreSeconds` | `float` | `[AtlasParam("net.recentreSeconds", 0.3f, 1.5f, 0.6f)]` |
| `net.maxGazeNodes` | `int` | `[AtlasParam("net.maxGazeNodes", 16f, 64f, 64f)]` |

All five live in `Blades/Network/NetworkParams.cs` (NET-07), bound like `Blades/Lung/Sky/NeedleSkyBeat.cs`. The views
expose plain fields with the same defaults (`RecentreSeconds = 0.6f`, `MaxGazeNodes = 64`, `Rule`, `File`) that
`NetworkParams` writes; no view reads the registry.

### 6.4 Events (§6.4), scene `"net"`

Written with `EventLogExtensions.LogJson(log, "net", ev, dataJson)` (`using Atlas.Telemetry;`) on the
`IEventLog` from `AtlasServices`; skipped when none is published (as `Blades/Lung/LungEvents.cs`). All in
`Blades/Network/NetworkEvents.cs` (NET-07):

| `ev` | `data` | When |
|---|---|---|
| `net.recentre` | `{"node": int, "r": double, "degree": int}` (`r` from the file) | each re-centre, when it starts |
| `net.packet` | `{"src": int, "dst": int, "rule": "hyperbolic" \| "flat", "hops": int, "shortest": int, "stretch": double \| null, "success": bool}` | each delivered or dropped packet. `hops = count − 1` of `GreedyRouter.Route`; `stretch = hops / shortest` when delivered, else `null` |
| `net.hop` (V3) | `{"from": int, "to": int, "distance": double}` (`NetworkModel.Distance`) | each hop |

`NetworkEvents` exposes `Recentre(int node, double r, int degree)`, `Packet(int src, int dst, RoutingRule rule, int hops,
int shortest, bool success)` and `Hop(int from, int to, double distance)`, formatting with invariant culture.

### 6.5 Twins (§6.4; lane C contract button table)

| Action | Gamepad | Gaze |
|---|---|---|
| Re-centre | stick moves a cursor between nodes (`AtlasAxis.LeftX/LeftY` or `NavLeft/Right/Up/Down`), A = `AtlasAction.Confirm` | dwell 0.7 s on the node |
| Send packet | Y = `AtlasAction.Primary` | dwell on the *packet* chip |
| Switch rule | X = `AtlasAction.Secondary` | dwell on the *rule* chip |

`GazeRegistry.Capacity` is 64 for the whole app; node targets are `min(net.maxGazeNodes, free slots − 2)` so the two
chips always fit (section 9, C4).

### 6.6 Home exhibit seam (NET-08)

```csharp
namespace Atlas.World
{
    // A use case shown inside Home (not a space of its own). Published with AtlasServices.Register<IHomeExhibit>.
    public interface IHomeExhibit
    {
        string Label { get; }      // the exhibit menu row text: "Network"
        bool Ready { get; }
        bool Active { get; }
        bool Begin();              // shows the exhibit in Home; false when it could not
        void End();                // hides it; Home and the RoomModel are left as they were
    }
}
```

`Atlas.Blades.Playground` cannot see `Atlas.Blades.Network` and the reverse (F1), so the exhibit menu reaches the
network through this interface in `Atlas.World`.

## 7. Tasks

Every C# test below is the NUnit full name; every Python test is the pytest node id from the repository root; every
Console test is `<file> › <test title>`. NET-11 cites these names; a task may add tests but must not rename these.

Common to all C# tasks: tests go under `ATLAS/Assets/Atlas/Tests/Network/` in assembly `Atlas.Blades.Network.Tests`
(namespace `Atlas.Blades.Network.Tests`), each new file with its `.meta`. Repository files are read through
`NetworkTestEnv` (NET-05): `ProjectDir` (`ATLAS_PROJECT_DIR` or the current directory, as `Tests/Lung/LungTestEnv.cs`),
`FixtureDir` = `<ProjectDir>/../station/pipelines/tests/fixtures/network`, `BundledDir` =
`<ProjectDir>/Assets/StreamingAssets/networks`.

### NET-01 · Network generator and study (lane D)

**Owns:** `station/pipelines/atlas_pipelines/network.py`, `station/pipelines/atlas_pipelines/cli.py` (adds the
`network` group; the only owner of `cli.py`), `station/pipelines/tests/test_network.py`.

**Reuses:** `documents/network_atlas/hypernet.py` (port it; do not import it from `network.py`),
`atlas_pipelines/paths.py` (`repo_root`, `PIPELINES_ROOT`), the step and group pattern of `atlas_pipelines/cli.py`,
`tests/conftest.py` (`fixtures_dir`, `repo_root`).

| Acceptance | Test |
|---|---|
| Mean degree within 10 % of k̄ for N = 200 over 10 seeds (mean over seeds 0 to 9) | `station/pipelines/tests/test_network.py::test_mean_degree_within_10pct_over_10_seeds` (section 9, C1: `xfail(strict=True)`) |
| CCDF tail slope within 0.3 of 1 − γ (section 3.1.1, seeds 0 to 9 pooled, kmin 6) | `station/pipelines/tests/test_network.py::test_ccdf_tail_slope_within_0_3_of_1_minus_gamma` |
| Hyperbolic greedy success ≥ 99 % over 2,000 pairs (seed 7) | `station/pipelines/tests/test_network.py::test_hyperbolic_greedy_success_at_least_99pct` |
| Flat-picture success ≤ 90 % over 2,000 pairs (seed 7) | `station/pipelines/tests/test_network.py::test_flat_picture_greedy_success_at_most_90pct` |
| Output validates against `atlas.network/1` | `station/pipelines/tests/test_network.py::test_output_validates_against_schema` |
| Output for the fixture seeds equals the base fixtures (bytes) | `station/pipelines/tests/test_network.py::test_fixture_seeds_equal_base_fixtures[7]`, `[83]` |

Formula tests (section 8): `test_alpha_xi_nu_eqs_25_29_31`, `test_radius_eq13`, `test_node_radius_inverts_cdf_eq17`,
`test_angle_uniform_in_0_2pi`, `test_distance_eq5_equals_golden`, `test_links_iff_distance_below_R_eq8`,
`test_hot_link_probability_eq41`, `test_greedy_routes_equal_golden`, `test_poincare_point_equals_golden`,
`test_mobius_preserves_distance_within_R`, all in `station/pipelines/tests/test_network.py`.

### NET-02 · Network contract finish (lane D)

**Owns:** `station/pipelines/tests/test_network_contract.py`; the bundled files
`ATLAS/Assets/StreamingAssets/networks/network_7.json`, `network_83.json` (regenerated only with
`atlas network bundle`; the `.meta` files and `networks.meta` stay as committed). Does **not** edit `network.md`,
`network.schema.json` or `README.md` (frozen; report a problem instead).

**Reuses:** `atlas_pipelines.network` (`validate`, `build`, `dumps`, `bundle_dir`, `schema_path`), the fixtures.

| Acceptance | Test |
|---|---|
| Schema checked in CI: both seeds pass | `station/pipelines/tests/test_network_contract.py::test_schema_accepts_bundled[7]`, `[83]` |
| Schema checked in CI: a broken file fails (missing key, wrong `schema`, `theta ≥ 2π`, negative `r`, edge `i ≥ j`, wrong degree, extra key) | `station/pipelines/tests/test_network_contract.py::test_schema_rejects_broken_file` (parametrised) |
| Two seeds bundled under `StreamingAssets/networks/`, each with its `.meta` | `station/pipelines/tests/test_network_contract.py::test_two_seeds_bundled_with_meta` |
| Regenerated by `network.py` equal to the committed files | `station/pipelines/tests/test_network_contract.py::test_bundled_equal_regenerated[7]`, `[83]` |

"CI" is the pipeline pytest suite (there is no workflow runner in the repository).

### NET-03 · V1 Console page (lane D)

**Owns:** `station/hub/wwwroot/js/network.js`, `station/hub/wwwroot/index.html`, `station/hub/wwwroot/js/app.js`,
`station/hub/wwwroot/console.css`, `station/hub/Web/HubWeb.cs` (the only owner; adds `/api/network`),
`station/console-tests/network.test.js`, `station/console-tests/page-smoke.mjs`,
`station/hub.tests/NetworkApiTests.cs`.

**Reuses:** the Hub's static file serving and `Error`/`Json` helpers in `HubWeb.cs`, `HubOptions.ProjectFolder`,
the tab and page pattern of `index.html` (`data-tab`, `data-page`), `mapview.js` drawing helpers, the fixtures (tests
read them from `../pipelines/tests/fixtures/network/`), `hub.tests/TestSupport.cs`.

| Acceptance | Test |
|---|---|
| Draws 200 nodes and 580 edges (seed 7) | `station/console-tests/network.test.js › network: draws 200 nodes and 580 edges for seed 7` |
| … in under 50 ms in a real browser | `station/console-tests/page-smoke.mjs` check `network page drew seed 7 in under 50 ms` (headless proxy timing; **real browser deferred: Station**) |
| A packet animates hop by hop, hyperbolic rule then flat-picture rule | `station/console-tests/network.test.js › network: packet animates hop by hop, hyperbolic rule then flat-picture rule` |
| Routes equal the reference | `station/console-tests/network.test.js › network: greedy routes equal golden_routes on 1,000 pairs, both rules` |
| Same picture as the Spectator (section 6.1) | `station/console-tests/network.test.js › network: projection equals golden_poincare within 0.5 px at 1000 px` |
| Study panel shows success and stretch for both rules | `station/console-tests/network.test.js › network: study panel shows success and stretch for both rules` |
| Node tests green | `npm test` in `station/console-tests` |
| Serves the bundled network JSON | `Atlas.Station.Tests.NetworkApiTests.List_ReturnsBundledNetworks`, `Atlas.Station.Tests.NetworkApiTests.Get_ReturnsBundledFileBytes`, `Atlas.Station.Tests.NetworkApiTests.Get_RejectsBadOrUnknownFile` |

The study panel shows the file's `study` block (2,000 pairs per rule, §6.3).

### NET-04 · V1 on the Spectator (lane D)

**Owns:** `ATLAS/Assets/Atlas/Telemetry/Map/NetworkDisc.cs` (engine-free: reads `atlas.network/1` with `AtlasJson`,
projects with section 6.1, preallocated buffers), `ATLAS/Assets/Atlas/Telemetry/Map/NetworkDiscPanel.cs` (the view),
`ATLAS/Assets/Atlas/Telemetry/Map/FlatTwinPanel.cs` (the only owner; shows the disc in the Spectator's map panel),
`ATLAS/Assets/Atlas/Tests/Telemetry/Map/NetworkDiscTests.cs`, each with its `.meta`.

**Reuses:** `Telemetry/Map/FlatTwinDisc.cs` (`ViewHalfExtent`, line drawing), `FlatTwinPanel.cs`,
`FlatTwinMesher.cs`, `SpectatorCore.cs` (read only), `Atlas.Link.AtlasJson`. `Atlas.Telemetry` cannot reference
`Atlas.Blades.Network` (F1), so `NetworkDisc` holds its own reader and projection, checked against the same golden.

| Acceptance | Test |
|---|---|
| Same picture as the Console for the same seed (node positions equal to 1 px) | `Atlas.Telemetry.Tests.NetworkDiscTests.Project_EqualsGoldenPoincare_Within0_5Px` (with NET-03's test, ≤ 1 px apart) |
| No per-frame allocation | `Atlas.Telemetry.Tests.NetworkDiscTests.Draw_AllocatesNothingPerFrame` |
| ≤ 0.3 ms per frame | `Atlas.Telemetry.Tests.NetworkDiscTests.Draw_CostProxy` (headless timing; **deferred: Station**) |

### NET-05 · Engine-free core (lane A or C)

**Owns:** `ATLAS/Assets/Atlas/Blades/Network.meta`, `Blades/Network/Atlas.Blades.Network.asmdef` (+ `.meta`),
`Blades/Network/AssemblyInfo.cs` (`InternalsVisibleTo("Atlas.Blades.Network.Tests")`), `Blades/Network/Core.meta`,
`Blades/Network/Core/NetworkModel.cs`, `Core/PoincareMap.cs`, `Core/GreedyRouter.cs`; `ATLAS/Assets/Atlas/Tests/Network.meta`,
`Tests/Network/Atlas.Blades.Network.Tests.asmdef` (+ `.meta`), `Tests/Network/NetworkTestEnv.cs`,
`Tests/Network/NetworkModelTests.cs`, `Tests/Network/PoincareMapTests.cs`, `Tests/Network/GreedyRouterTests.cs`,
`Tests/Network/NetworkCoreRulesTests.cs`, each with its `.meta`.

Blade asmdef: name and root namespace `Atlas.Blades.Network`; references exactly `Atlas.Kernel`, `Atlas.Link`,
`Atlas.World`, `Atlas.Rendering`, `Atlas.Input`, `Atlas.Telemetry`, `Unity.Mathematics` (never Scan, Safety, App or
another blade). Test asmdef: like `Tests/Lung/Atlas.Blades.Lung.Tests.asmdef` with references `Atlas.Blades.Network`,
`Atlas.Kernel`, `Atlas.Link`, `Atlas.World`, `Atlas.Rendering`, `Atlas.Input`, `Atlas.Telemetry`, `Unity.Mathematics`,
the test runners; Editor only; `UNITY_INCLUDE_TESTS`. `verify.py`'s `ALLOWED` already covers `Atlas.Blades.*`: no edit.

**Reuses:** `Kernel/Geometry2.cs` (`Distance`, κ = −1), `Link/Json/AtlasJson.cs`, `Tests/Lung/LungTestEnv.cs` (pattern).

| Acceptance | Test |
|---|---|
| Distances equal the Python ones to 1e-9 (500 golden pairs, both seeds) | `Atlas.Blades.Network.Tests.NetworkModelTests.Distances_EqualPython_1e9` |
| A Möbius re-centre preserves every pairwise hyperbolic distance to 1e-9 (section 9, C3) | `Atlas.Blades.Network.Tests.PoincareMapTests.Mobius_PreservesPairwiseDistances_1e9` |
| Greedy routes equal the Python routes on 1,000 pairs (both rules, paths, success, hops, shortest) | `Atlas.Blades.Network.Tests.GreedyRouterTests.Routes_EqualPython_1000Pairs_BothRules` |
| No UnityEngine type in Core | `Atlas.Blades.Network.Tests.NetworkCoreRulesTests.Core_UsesNoUnityEngineType` (scans `Blades/Network/Core/*.cs`) |
| verify.py rules and build (all configs) pass | `python3 tools/headless/verify.py rules --tag na-NET-05`, `python3 tools/headless/verify.py build --config all --tag na-NET-05` |

Also: `NetworkModelTests.Parse_FixtureCounts` (200 nodes; 580 and 474 edges), `NetworkModelTests.HyperboloidPoint_DistanceEqualsGeometry2_1e9`
(golden pairs), `PoincareMapTests.Project_EqualsPythonPoincare_1e9`, `PoincareMapTests.EaseCentre_EndsAtTargetAndStaysOnGeodesic`,
`GreedyRouterTests.Route_AllocatesNothing` (all `Atlas.Blades.Network.Tests.`).

### NET-06 · V2 floor map (lane A or C)

**Owns:** `Blades/Network/NetworkFloorMesher.cs` (engine-free: builds the three meshes into preallocated
`AtlasMeshBuilder`s), `Blades/Network/NetworkFocus.cs` (engine-free re-centre animation: `EaseCentre` over
`RecentreSeconds`, time passed in), `Blades/Network/NetworkFloorView.cs` (thin `MonoBehaviour`: three meshes, three pooled
materials, joins the frame loop through `IFrameLoop.AddUpload`), `Blades/Network/NetworkInstaller.cs` (the blade's
composition: `public static NetworkFloorView Install(ISpaceHost host, IFrameLoop loop, MaterialPool pool)` builds the view
in code, hidden, for Home, and `public static NetworkFloorView View { get; }` exposes it; a
`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` entry calls `Install` with the services from `AtlasServices` when
they are published. This is the only "registration in Home"; Home's own files are not edited. `NetworkFloorView`
has `public bool Visible { get; set; }`, which NET-08's exhibit drives), `Tests/Network/NetworkFloorTests.cs`, with `.meta`s.

**Reuses:** `Rendering/Look/AtlasMeshBuilder.cs`, `Rendering/Look/AtlasPalette.cs`, `Rendering/Look/MaterialPool.cs`
(`Rent`, `SetTintIndex`, `SetLineColor`), `Atlas.World.IFrameLoop`/`IUploadStep`/`ISpaceHost`, the core of NET-05,
the floor disc of section 6.2, `World/Room/RoomFixtures.cs` (test rooms). Post height grows with ln(degree) (§4); its two
constants are named in `NetworkFloorMesher` and listed in the report as not from the doc.

| Acceptance | Test |
|---|---|
| 3 draws per eye (3 merged meshes: nodes, edges, highlight; one material each) | `Atlas.Blades.Network.Tests.NetworkFloorTests.ThreeMeshes_ThreeDrawsPerEye` (device draw count deferred: phone) |
| ≤ 25k triangles (EditMode count from the mesh buffers) | `Atlas.Blades.Network.Tests.NetworkFloorTests.Triangles_AtMost25k` |
| 0 B managed allocation per frame at rest | `Atlas.Blades.Network.Tests.NetworkFloorTests.AtRest_ZeroManagedAllocationPerFrame` |
| Re-centre ≤ 2 ms CPU per frame | `Atlas.Blades.Network.Tests.NetworkFloorTests.Recentre_AtMost2msCpuPerFrame` (headless proxy; device timing deferred: phone) |
| Meshes rebuilt only during a re-centre, into preallocated buffers | `Atlas.Blades.Network.Tests.NetworkFloorTests.Meshes_RebuiltOnlyDuringRecentre_IntoPreallocatedBuffers` |
| AtlasMeshBuilder, AtlasPalette, MaterialPool only | `Atlas.Blades.Network.Tests.NetworkFloorTests.UsesOnlyAtlasLook` (source scan: no `MaterialPropertyBlock`, no `new Material`, no `Shader.Find`) |
| Disc inside the free rectangle (section 6.2) | `Atlas.Blades.Network.Tests.NetworkFloorTests.Disc_InsideFreeRectangle_AllRoomFixtures` |

### NET-07 · V2 twins and packet (lane C)

**Owns:** `Blades/Network/NetworkTwins.cs` (gaze targets, cursor, actions), `Blades/Network/NetworkPacket.cs`
(engine-free packet run: route, hop-by-hop highlight, caption text), `Blades/Network/NetworkCaption.cs` (the caption
chip: hop count and stretch), `Blades/Network/NetworkEvents.cs` (section 6.4), `Blades/Network/NetworkParams.cs`
(section 6.3), `Tests/Network/NetworkTwinsTests.cs`, `Tests/Network/NetworkPacketTests.cs`,
`Tests/Network/NetworkParamsTests.cs`, with `.meta`s.

**Reuses:** `Input/Core/GazeRegistry.cs`, `Input/Core/GazeTypes.cs`, `Input/AtlasAction.cs`, `Input/AtlasInput.cs`;
`Telemetry/Params/AtlasParamAttribute.cs` and `ParamBinder` (pattern: `Blades/Lung/Sky/NeedleSkyBeat.cs`);
`Telemetry/Events/EventLog.cs` and the `EventCapture` pattern of `Tests/Lung/LungTestEnv.cs`; the caption chip of the
Playground (`Blades/Playground/Shell/...CaptionChipView`) as a pattern only (no reference); NET-05's core; NET-06's view.

| Acceptance | Test |
|---|---|
| Every action by gamepad alone | `Atlas.Blades.Network.Tests.NetworkTwinsTests.Recentre_ByGamepadAlone`, `.SendPacket_ByGamepadAlone`, `.SwitchRule_ByGamepadAlone` |
| Every action by gaze alone | `Atlas.Blades.Network.Tests.NetworkTwinsTests.Recentre_ByGazeAlone`, `.SendPacket_ByGazeAlone`, `.SwitchRule_ByGazeAlone` |
| GazeRegistry targets ≤ `net.maxGazeNodes` | `Atlas.Blades.Network.Tests.NetworkTwinsTests.GazeNodeTargets_AtMostMaxGazeNodes` |
| `net.packet` logged for 10 of 10 packets with hops and stretch equal to the core's | `Atlas.Blades.Network.Tests.NetworkPacketTests.TenPackets_LoggedWithCoreHopsAndStretch` |
| §6.4 parameters declared with `[AtlasParam]` | `Atlas.Blades.Network.Tests.NetworkParamsTests.Section64Params_DeclaredWithAtlasParam` |

Device check of the twins in the headset deferred: phone.

### NET-08 · Exhibit wiring (lane C)

**Owns:** `ATLAS/Assets/Atlas/World/Spaces/IHomeExhibit.cs` (section 6.6, new, + `.meta`),
`Blades/Network/NetworkExhibit.cs` (implements `IHomeExhibit` over `NetworkInstaller.View`; its own static
`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` entry publishes it with `AtlasServices.Register<IHomeExhibit>`; Begin
sets `Visible = true`, End sets it false and leaves the RoomModel untouched), `Blades/Playground/Shell/Menu/ExhibitMenu.cs` and
`Blades/Playground/Shell/Menu/ExhibitMenuView.cs` (the only owner: adds the row `ExhibitId.Network` before `Done`,
read through `AtlasServices.TryGet<IHomeExhibit>`; `ExhibitSignLayout.Count` becomes 5),
`Blades/Playground/Shell/Actions/PgGazeTargets.cs` (only if the fifth sign needs a slot),
`Tests/Playground/Shell/ExhibitMenuTests.cs` (updates for five rows), `Tests/Network/NetworkExhibitTests.cs`.

**Reuses:** `Blades/Playground/Shell/Menu/ExhibitMenu.cs` (`IExhibitSlot` pattern of the Slicer row), `ISpaceHost`,
`World/Room/RoomModel.cs`, `RoomFixtures`.

| Acceptance | Test |
|---|---|
| Reachable from the exhibit menu | `Atlas.Blades.Playground.Tests.ExhibitMenuTests.NetworkRow_ReachableFromMenu` |
| Returns Home with the room unchanged (walls within 0.05 m), EditMode over the RoomModel | `Atlas.Blades.Network.Tests.NetworkExhibitTests.ReturnHome_RoomUnchanged_WallsWithin5cm` |
| Existing menu behaviour kept | every other test in `Atlas.Blades.Playground.Tests.ExhibitMenuTests` |

Headset run deferred: phone.

### NET-09 · V3 node placement (stretch; lane B)

**Owns:** `Blades/Network/Walk.meta`, `Blades/Network/Walk/NetworkTilePlacement.cs` (engine-free: snaps each node's
point P to the {4,5} tile graph by stepping from tile zero to the neighbour whose centre is nearest P, §4 V3; offset in
the tile chart), `Blades/Network/Walk/NetworkTowerView.cs` (towers in tiles), `Tests/Network/NetworkTilePlacementTests.cs`,
with `.meta`s.

**Reuses:** `Kernel/Tiling/TileGraph.cs` (`Neighbour`, `StepMatrix`), `Kernel/Tiling/TileGraphQueries.cs`,
`Kernel/Geometry2.cs`, `Blades/Lung/Walking/AirwayTreeBinding.cs` (pattern only), NET-05's `HyperboloidPoint`.

| Acceptance | Test |
|---|---|
| Every node's tile is the tile whose centre is nearest (brute force on 200 nodes) | `Atlas.Blades.Network.Tests.NetworkTilePlacementTests.Snap_EqualsBruteForceNearestCentre_200Nodes` |
| WorldLint passes | `Atlas.WorldGen.Tests.WorldLintTests` (all cases, unchanged) |

### NET-10 · V3 beacons and hops (stretch; lane B)

**Owns:** `Blades/Network/Walk/NetworkWingHost.cs` (registered with Curved Walk like `AirwayWingHost`),
`Blades/Network/Walk/NetworkBeaconView.cs`, `Blades/Network/Walk/NetworkBeacon.cs` (engine-free bearing from the
kernel's log map), `Blades/Network/Walk/NetworkHop.cs` (engine-free hop: fade out 0.25 s, re-root, fade in 0.25 s; time
passed in), `Tests/Network/NetworkBeaconTests.cs`, `Tests/Network/NetworkHopTests.cs`, with `.meta`s.

**Reuses:** `Blades/Lung/Airway/AirwayBeacon.cs`, `Blades/Lung/Walking/AirwayWingHost.cs` (patterns only),
`Kernel/Geometry2.cs` (`Log`), `Kernel/Walk/HolonomyWalkerKernel.cs`, `IFrameLoop.ResetWalker`, NET-07's
`NetworkEvents.Hop`, NET-09's placement.

| Acceptance | Test |
|---|---|
| Beacon bearing within 1° of the log map | `Atlas.Blades.Network.Tests.NetworkBeaconTests.Bearing_Within1DegreeOfLogMap` |
| A hop re-roots within 0.5 s with a fade | `Atlas.Blades.Network.Tests.NetworkHopTests.Hop_ReRootsWithinHalfSecond_WithFade` |
| No visible pop | deferred: phone |

### NET-11 · Maths card and docs (lane B)

**Owns:** `docs/maths.md` (new; a section "Network Atlas"), `station/pipelines/tests/test_network_maths_card.py`.
The deck slide is deferred (there is no deck in the repository).

| Acceptance | Test |
|---|---|
| Every formula of §3 names a test by its full name (section 8), and every named test exists | `station/pipelines/tests/test_network_maths_card.py::test_every_section3_formula_names_an_existing_test` |
| No claim beyond §8 | `station/pipelines/tests/test_network_maths_card.py::test_no_claim_beyond_section8` |

## 8. §3 formulas and the tests that prove them

| §3 row | Test(s) |
|---|---|
| α = (γ − 1) / 2 | `station/pipelines/tests/test_network.py::test_alpha_xi_nu_eqs_25_29_31` |
| ξ = α / (α − 1/2) | `station/pipelines/tests/test_network.py::test_alpha_xi_nu_eqs_25_29_31` |
| ν (T = 0 and 0 < T < 1) | `station/pipelines/tests/test_network.py::test_alpha_xi_nu_eqs_25_29_31` |
| R = 2 ln(N / ν); γ = 3 form | `station/pipelines/tests/test_network.py::test_radius_eq13` |
| Radius of a node (inverse CDF of eq. 17) | `station/pipelines/tests/test_network.py::test_node_radius_inverts_cdf_eq17` |
| Angle uniform in [0, 2π) | `station/pipelines/tests/test_network.py::test_angle_uniform_in_0_2pi` |
| Distance (eq. 5) | `station/pipelines/tests/test_network.py::test_distance_eq5_equals_golden`, `Atlas.Blades.Network.Tests.NetworkModelTests.Distances_EqualPython_1e9` |
| Link, T = 0 (eq. 8) | `station/pipelines/tests/test_network.py::test_links_iff_distance_below_R_eq8` |
| Link, T > 0 (eq. 41) | `station/pipelines/tests/test_network.py::test_hot_link_probability_eq41` |
| Greedy forwarding | `station/pipelines/tests/test_network.py::test_greedy_routes_equal_golden`, `Atlas.Blades.Network.Tests.GreedyRouterTests.Routes_EqualPython_1000Pairs_BothRules`, `station/console-tests/network.test.js › network: greedy routes equal golden_routes on 1,000 pairs, both rules` |
| Poincaré disc point ρ = tanh(r / 2) | `station/pipelines/tests/test_network.py::test_poincare_point_equals_golden`, `Atlas.Blades.Network.Tests.PoincareMapTests.Project_EqualsPythonPoincare_1e9` |
| Re-centring z → (z − a) / (1 − ā z) | `station/pipelines/tests/test_network.py::test_mobius_preserves_distance_within_R`, `Atlas.Blades.Network.Tests.PoincareMapTests.Mobius_PreservesPairwiseDistances_1e9` |
| Kernel frame P = (sinh r cos θ, sinh r sin θ, cosh r) | `Atlas.Blades.Network.Tests.NetworkModelTests.HyperboloidPoint_DistanceEqualsGeometry2_1e9` |

## 9. Conflicts found while making the fixtures (for the lead)

**C1. Mean degree over 10 seeds.** A faithful port of §3 (`hypernet.py`) gives, for N = 200, k̄ = 6, γ = 2.5, T = 0,
mean degrees 4.97, 3.83, 5.07, 4.60, 3.22, 7.00, 3.64, 5.80, 4.06, 3.75 for seeds 0 to 9: mean 4.59, 23 % below k̄
(seeds 1 to 10: 4.45). The 10 % row of NET-01 cannot pass while the output also equals the `hypernet.py` fixtures (the
ν of eq. 25 and 31 is the large-N limit). Work-around: NET-01 writes the test verbatim and marks it
`pytest.mark.xfail(strict=True, reason="contract network.md section 9 C1")`, so it reports the gap and flags any change of
the generator. Seed 7 alone (5.8) is within 10 %.

**C2. CCDF tail slope.** Per seed the slope (section 3.1.1, kmin 6) ranges from −1.14 to −1.93; pooled over seeds 0 to 9
it is −1.785, within 0.3 of 1 − γ = −1.5 (margin 0.015). Seed 7 alone gives −1.486. The test uses the pooled value.

**C3. Möbius re-centre to 1e-9.** In doubles a disc point at hyperbolic distance about 20 from the centre is only
resolved to about 1e-7 units, and eq. 5 itself loses up to 3e-8 on close pairs at r ≈ 10. Re-centring seed 7 on each
node, the stable disc distance (section 5.2) changes by at most 3.5e-11 for pairs whose both images stay within R of
the new centre (`|w| ≤ tanh(R/2)`), but by up to 6.2e-8 over all pairs (0.03 % of pairs exceed 1e-9). The test therefore
re-centres on every node of both fixtures and asserts 1e-9 for every pair within R of the new centre (the part of the
disc the floor map shows legibly), measuring both sides with `PoincareMap.Distance`; it logs the all-pairs maximum.

**C4. Gaze capacity.** `GazeRegistry.Capacity` is 64 for the whole app, the same as the default `net.maxGazeNodes`,
and the two chips need two more slots. Section 6.5 caps the node targets at the free slots minus two.

**C5. The second bundled seed was chosen by a scan.** The first choice, seed 11, delivered only 68.7 % hyperbolic greedy
success (study) and 69.0 % (golden pairs), against 100 % for seed 7, so it was replaced. Seeds 1 to 200 (excluding 7) were
generated at N 200, k̄ 6, γ 2.5, T 0 and kept when the hyperbolic success of the 2,000 study pairs was at least 0.99, the
flat-picture success at most 0.90 and the giant component at least 170. Of the qualifying seeds, seed 83 has the mean
degree closest to 6 (6.04; seed 48 follows with 6.06): hyperbolic 99.5 % against flat 73.5 % (study), 99.7 % against
72.6 % (golden pairs), giant component 194, 604 edges. The Console and the maths card quote the file's own numbers and
the §8 wording ("in *this* network").

**C6. Hop fade.** `AtlasLook.SetFade` is documented "SpaceLab only" and `SpaceLab` is in `Atlas.App`, which a blade may
not reference. NET-10's `NetworkHop` computes the fade level engine-free; how the view applies a world fade without
SpaceLab needs the lead (the doc says "reuse SpaceLab's fade").

**C7. Blade to blade.** The exhibit menu is in `Atlas.Blades.Playground` and the network in `Atlas.Blades.Network`;
F1 forbids either referencing the other. Section 6.6 adds `IHomeExhibit` to `Atlas.World` as the seam, and NET-08 edits
the Playground's menu files.
