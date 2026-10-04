#!/usr/bin/env python3
# PLACEHOLDER (lane D, P2-D07/P2-D08/P2-D01/P3-D01): contract surface only; replaced by lane D's implementation
# Lane-B temporary tooling (PH-21): deterministic, Python stdlib only. Delete when lane D's `atlas case` fixtures land.
# Writes the lane D fixtures under ATLAS/Assets/StreamingAssets: cases/illustrative/{manifest.json, airway/tree.json,
# sky/*} (Master Plan §10.2.4 steps 7, 8, 9) and fieldloss/tunnel10.png (§10.4.6, P3-D01).
import hashlib, json, math, os, random, struct, sys, zlib

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "ATLAS", "Assets", "StreamingAssets")
CASE = os.path.join(ROOT, "cases", "illustrative")

def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data if isinstance(data, bytes) else data.encode())

# ---------------------------------------------------------------- airway (§10.2.4 step 7, atlas.tree/2)
RATIO, ANGLE, MAX_GEN, R0 = 0.794, math.radians(37.0), 16, 9.0

def add(a, b, s=1.0): return [a[i] + s * b[i] for i in range(3)]
def dot(a, b): return sum(a[i] * b[i] for i in range(3))
def cross(a, b): return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
def norm(a):
    n = math.sqrt(dot(a, a)); return [x / n for x in a]

def make_children(node, seed):
    """node: dict with address, gen, radius, len, pos, dir (stored, rounded) and u (in-plane axis). Returns 2 children."""
    out = []
    d, u = node["dir"], node["u"]
    n_end = add(node["pos"], d, node["len"])
    for tag, sgn in (("L", +1.0), ("R", -1.0)):
        c = norm(add([x * math.cos(ANGLE) for x in d], u, sgn * math.sin(ANGLE)))
        p = norm(add(u, c, -dot(u, c)))               # parent's plane axis, made perpendicular to the child
        uc = norm(cross(c, p))                        # plane rotated 90 degrees about the child's axis
        addr = node["address"] + tag
        rad = node["radius"] * RATIO
        ln = 2.0 * rad * random.Random("%d:%s" % (seed, addr)).uniform(2.0, 4.0)   # length/diameter 3 +- 1
        out.append({"address": addr, "gen": node["gen"] + 1, "radius": round(rad, 4), "len": round(ln, 3),
                    "pos": [round(x, 3) for x in n_end], "dir": [round(x, 5) for x in c], "u": uc})
    return out

def root(seed):
    ln = 2.0 * R0 * random.Random("%d:" % seed).uniform(2.0, 4.0)
    return {"address": "", "gen": 0, "radius": R0, "len": round(ln, 3), "pos": [0.0, 0.0, 0.0],
            "dir": [0.0, 0.0, -1.0], "u": [1.0, 0.0, 0.0]}

def compass_wrong(nodes, target):
    """Euclidean compass (brief D6): at each fork on the route pick the child with the larger dir . (T - child.pos).
    Returns (list of wrong fork addresses, smallest decision margin)."""
    t = nodes[target]
    tp = add(t["pos"], t["dir"], t["len"])
    wrong, margin = [], 1e9
    for k in range(len(target)):
        a = target[:k]
        s = [dot(nodes[a + c]["dir"], add(tp, nodes[a + c]["pos"], -1.0)) for c in "LR"]
        pick = "L" if s[0] > s[1] else "R"
        margin = min(margin, abs(s[0] - s[1]))
        if pick != target[k]: wrong.append(a)
    return wrong, margin

def build_airway():
    # PH-35: with the plan's geometry (37 degrees, plane rotation 90 degrees, ratio 0.794) the Euclidean compass is never wrong
    # on a 4-fork route (0 hits in 1,000 seeds x 16 targets), so the depth is raised until a route has exactly one wrong fork.
    for depth in range(4, 11):
        for seed in range(1, 200):
            top = {"": root(seed)}
            level = [top[""]]
            for _ in range(depth):
                nxt = []
                for nd in level:
                    for c in make_children(nd, seed): top[c["address"]] = c; nxt.append(c)
                level = nxt
            for i in range(2 ** depth):
                target = "".join("LR"[(i >> (depth - 1 - b)) & 1] for b in range(depth))
                wrong, margin = compass_wrong(top, target)
                if len(wrong) == 1 and margin > 0.01:
                    return seed, target, wrong[0]
    raise SystemExit("no seed found")

def write_airway():
    seed, target, fork = build_airway()
    nodes, level = [root(seed)], [root(seed)]
    for _ in range(MAX_GEN):
        nxt = []
        for nd in level: nxt.extend(make_children(nd, seed))
        nodes.extend(nxt); level = nxt
    parts = []
    for n in nodes:
        parts.append('{"address":"%s","gen":%d,"len_mm":%s,"radius_mm":%s,"pos_mm":[%s],"dir":[%s],"source":"model"}' % (
            n["address"], n["gen"], repr(n["len"]), repr(n["radius"]),
            ",".join(repr(x) for x in n["pos"]), ",".join(repr(x) for x in n["dir"])))
    text = '{"schema":"atlas.tree/2","nodes":[%s],"target":{"address":"%s"},"compass_fork":{"address":"%s"}}' % (
        ",".join(parts), target, fork)
    write(os.path.join(CASE, "airway", "tree.json"), text)
    print("tree.json: seed %d, %d nodes, target '%s', compass fork '%s'" % (seed, len(nodes), target, fork))

# ---------------------------------------------------------------- needle sky (§10.2.4 step 8)
W, H, PHASES, MM = 512, 256, 10, 0.2

def clear_byte(i, j, k):
    az = math.radians((i + 0.5) / W * 360.0 - 180.0)
    el = math.radians(90.0 - (j + 0.5) / H * 180.0)
    v = 12.0 + 10.0 * math.sin(2 * az) * math.cos(el) + 1.5 * math.sin(2 * math.pi * k / PHASES)
    return max(0, min(255, int(v / MM + 0.5)))        # quantised to 0.2 mm

def write_sky():
    maps = [bytearray(W * H) for _ in range(PHASES)]
    robust = bytearray(W * H)
    for j in range(H):
        for i in range(W):
            vals = [clear_byte(i, j, k) for k in range(PHASES)]
            for k in range(PHASES): maps[k][j * W + i] = vals[k]
            robust[j * W + i] = 255 if all(v * MM >= 5.0 - 1e-9 for v in vals) else 0   # AND over 10 phases of clearance >= 5 mm
    for k in range(PHASES): write(os.path.join(CASE, "sky", "risk_p%d.r8" % k), bytes(maps[k]))
    write(os.path.join(CASE, "sky", "robust.r8"), bytes(robust))
    rng, checks = random.Random(1), []
    for _ in range(20):
        i, j = rng.randrange(W), rng.randrange(H)
        per = [round(maps[k][j * W + i] * MM, 1) for k in range(PHASES)]
        checks.append({"az_deg": (i + 0.5) / W * 360.0 - 180.0, "el_deg": 90.0 - (j + 0.5) / H * 180.0,
                       "clear_mm_phase": per, "clear_mm": min(per)})
    sky = {"schema": "atlas.sky/1", "width": W, "height": H, "nside": 64, "phases": PHASES, "threshold_mm": 5,
           "mm_per_unit": MM, "target_mm": [10.0, 20.0, 30.0], "checks": checks}
    write(os.path.join(CASE, "sky", "sky.json"), json.dumps(sky, indent=1) + "\n")
    print("sky: 11 .r8 files and sky.json")

# ---------------------------------------------------------------- manifest (§10.2.4 step 9)
def write_manifest():
    paths = ["airway/tree.json", "sky/sky.json"] + ["sky/risk_p%d.r8" % k for k in range(PHASES)] + ["sky/robust.r8"]
    files = [{"path": p, "sha256": hashlib.sha256(open(os.path.join(CASE, p), "rb").read()).hexdigest()} for p in paths]
    m = {"schema": "atlas.case/1", "id": "illustrative", "kind": "illustrative",
         "labels": [{"content": "airway/tree.json", "label": "Model-generated airway"}],
         "licence": "CC0-1.0 (synthetic placeholder fixture)", "credit": "ATLAS lane B placeholder generator",
         "M": 15, "Lambda_m": 1.5, "files": files}
    write(os.path.join(CASE, "manifest.json"), json.dumps(m, indent=1) + "\n")
    print("manifest.json: %d files hashed" % len(files))

# ---------------------------------------------------------------- field-loss mask (§10.4.6, P3-D01)
def write_tunnel():
    n, c, r = 512, 256.0, 40.0                         # 4 px/deg: a 10 degree eccentricity is 40 px
    rows = []
    for y in range(n):
        row = bytearray(b"\x00")                       # PNG filter type 0
        for x in range(n):
            row.append(255 if math.hypot(x + 0.5 - c, y + 0.5 - c) <= r else 0)
        rows.append(bytes(row))
    def chunk(t, d):
        body = t + d
        return struct.pack(">I", len(d)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 0, 0, 0, 0)) + \
          chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b"")
    write(os.path.join(ROOT, "fieldloss", "tunnel10.png"), png)
    print("tunnel10.png written")

if __name__ == "__main__":
    write_airway(); write_sky(); write_manifest(); write_tunnel()
