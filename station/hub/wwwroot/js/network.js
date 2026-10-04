// The Console's Network page (Network Atlas design section 4 V1; contract network.md 3.3): the Poincare disc of an
// atlas.network/1 file, edges as true geodesics, and a packet that hops under the hyperbolic greedy rule and then
// under the flat-picture rule. The functions up to studyText are pure (no DOM) and tested by network.test.js.

export const VIEW_HALF_EXTENT = 1.03;   // section 6.1
export const EDGE_SEGMENTS = 16;
const GREEDY_LIMIT = 200;               // hypernet.greedy(limit=200)
const SCHEMA = 'atlas.network/1';

function fail(path, why) { throw new Error(path + ': ' + why); }
function num(v, path) { if (typeof v !== 'number' || !Number.isFinite(v)) fail(path, 'expected a number'); return v; }
function int(v, path) { if (!Number.isInteger(v)) fail(path, 'expected an integer'); return v; }

function parseRule(s, path) {
  if (s === null || typeof s !== 'object') fail(path, 'expected an object');
  return { success: num(s.success, path + '.success'), stretch: s.stretch === null ? null : num(s.stretch, path + '.stretch') };
}

/** Reads and checks an atlas.network/1 file (object or JSON text). Throws an Error naming the bad field. */
export function parseNetwork(json) {
  const raw = typeof json === 'string' ? JSON.parse(json) : json;
  if (raw === null || typeof raw !== 'object') fail('$', 'expected an object');
  if (raw.schema !== SCHEMA) fail('schema', 'expected "' + SCHEMA + '"');
  const m = raw.model;
  if (m === null || typeof m !== 'object') fail('model', 'expected an object');
  const model = { n: int(m.n, 'model.n'), kbar: num(m.kbar, 'model.kbar'), gamma: num(m.gamma, 'model.gamma'), T: num(m.T, 'model.T'),
    seed: int(m.seed, 'model.seed'), zeta: num(m.zeta, 'model.zeta'), R: num(m.R, 'model.R') };
  if (!Array.isArray(raw.nodes) || raw.nodes.length !== model.n) fail('nodes', 'expected model.n entries');
  const n = model.n;
  const nodes = raw.nodes.map((q, i) => {
    const p = 'nodes[' + i + ']';
    if (q === null || typeof q !== 'object') fail(p, 'expected an object');
    if (int(q.id, p + '.id') !== i) fail(p + '.id', 'expected ' + i);
    const r = num(q.r, p + '.r');
    const theta = num(q.theta, p + '.theta');
    if (r < 0) fail(p + '.r', 'below minimum 0');
    if (theta < 0 || theta >= 2 * Math.PI) fail(p + '.theta', 'outside [0, 2π)');
    return { id: i, r, theta, degree: int(q.degree, p + '.degree'), label: String(q.label === undefined ? '' : q.label) };
  });
  if (!Array.isArray(raw.edges)) fail('edges', 'expected an array');
  const adjacency = nodes.map(() => []);
  const edges = raw.edges.map((e, k) => {
    const p = 'edges[' + k + ']';
    if (!Array.isArray(e) || e.length !== 2) fail(p, 'expected [i, j]');
    const i = int(e[0], p + '[0]');
    const j = int(e[1], p + '[1]');
    if (!(i >= 0 && i < j && j < n)) fail(p, 'expected 0 <= i < j < n');
    adjacency[i].push(j);
    adjacency[j].push(i);
    return [i, j];
  });
  for (const list of adjacency) list.sort((a, b) => a - b);
  nodes.forEach((q, i) => { if (q.degree !== adjacency[i].length) fail('nodes[' + i + '].degree', 'does not match the edges'); });
  const s = raw.study;
  if (s === null || typeof s !== 'object') fail('study', 'expected an object');
  const study = { pairs: int(s.pairs, 'study.pairs'), hyperbolic: parseRule(s.hyperbolic, 'study.hyperbolic'), flat_picture: parseRule(s.flat_picture, 'study.flat_picture') };
  const points = nodes.map((q) => project(q.r, q.theta));
  return { model, nodes, edges, study, adjacency, points, geo: null };
}

/** Poincare point of a node: tanh(r/2)(cos θ, sin θ). */
export function project(r, theta) {
  const rho = Math.tanh(r / 2);
  return [rho * Math.cos(theta), rho * Math.sin(theta)];
}

/** Mobius move of the disc taking a to the centre: (z − a)/(1 − ā z). */
export function mobius(x, y, ax, ay) {
  const nr = x - ax, ni = y - ay;                  // z − a
  const dr = 1 - (ax * x + ay * y), di = -(ax * y - ay * x);   // 1 − ā z
  const q = dr * dr + di * di;
  return [(nr * dr + ni * di) / q, (ni * dr - nr * di) / q];
}

/** Section 6.1: a square of `size` pixels, origin top-left, y down. */
export function toScreen(x, y, size) {
  const scale = (size / 2) / VIEW_HALF_EXTENT;
  return [size / 2 + scale * x, size / 2 - scale * y];
}

function dist(r1, t1, r2, t2) {
  const dth = Math.PI - Math.abs(Math.PI - Math.abs(t1 - t2));
  const c = Math.cosh(r1) * Math.cosh(r2) - Math.sinh(r1) * Math.sinh(r2) * Math.cos(dth);
  return Math.acosh(Math.max(c, 1.0));
}

/** Eq. 5, in the order of section 5.1. */
export function distance(net, a, b) {
  const p = net.nodes[a], q = net.nodes[b];
  return dist(p.r, p.theta, q.r, q.theta);
}

/** Euclidean distance between the two Poincare points. */
export function flatDistance(net, a, b) {
  const p = net.points[a], q = net.points[b];
  return Math.hypot(p[0] - q[0], p[1] - q[1]);
}

/** hypernet.greedy: the neighbour (ascending id, first on a tie) closest to dst; drop at a local minimum. */
export function greedyRoute(net, src, dst, rule) {
  const metric = rule === 'flat' ? flatDistance : distance;
  const path = [src];
  let v = src;
  while (v !== dst && path.length < GREEDY_LIMIT) {
    const nb = net.adjacency[v];
    if (nb.length === 0) return { path, success: false, hops: path.length - 1 };
    let w = nb[0], best = metric(net, w, dst);
    for (let k = 1; k < nb.length; k++) {
      const d = metric(net, nb[k], dst);
      if (d < best) { best = d; w = nb[k]; }
    }
    if (best >= metric(net, v, dst)) return { path, success: false, hops: path.length - 1 };
    path.push(w);
    v = w;
  }
  return { path, success: v === dst, hops: path.length - 1 };
}

/** BFS hops, −1 when unreachable. */
export function shortestHops(net, src, dst) {
  if (src === dst) return 0;
  const seen = new Int32Array(net.nodes.length).fill(-1);
  const queue = [src];
  seen[src] = 0;
  for (let h = 0; h < queue.length; h++) {
    const v = queue[h];
    for (const w of net.adjacency[v]) {
      if (seen[w] >= 0) continue;
      seen[w] = seen[v] + 1;
      if (w === dst) return seen[w];
      queue.push(w);
    }
  }
  return -1;
}

/** hypernet.geodesic_disk: k points of the geodesic a to b, through the hyperboloid, as Poincare points. */
export function geodesicPoints(net, a, b, k = EDGE_SEGMENTS + 1) {
  const p = net.nodes[a], q = net.nodes[b];
  const d = distance(net, a, b);
  const P = [Math.sinh(p.r) * Math.cos(p.theta), Math.sinh(p.r) * Math.sin(p.theta), Math.cosh(p.r)];
  const Q = [Math.sinh(q.r) * Math.cos(q.theta), Math.sinh(q.r) * Math.sin(q.theta), Math.cosh(q.r)];
  if (d < 1e-9) return [[P[0] / (1 + P[2]), P[1] / (1 + P[2])]];
  const out = [];
  const sd = Math.sinh(d);
  for (let i = 0; i < k; i++) {
    const s = k === 1 ? 0 : i / (k - 1);
    const u = Math.sinh((1 - s) * d) / sd, v = Math.sinh(s * d) / sd;
    const gx = u * P[0] + v * Q[0], gy = u * P[1] + v * Q[1], gz = u * P[2] + v * Q[2];
    out.push([gx / (1 + gz), gy / (1 + gz)]);
  }
  return out;
}

// Geodesic samples of every edge, computed once per file: Float64Array of edges x 17 x 2.
function edgeSamples(net) {
  if (net.geo) return net.geo;
  const k = EDGE_SEGMENTS + 1;
  const geo = new Float64Array(net.edges.length * k * 2);
  net.edges.forEach(([i, j], e) => {
    const pts = geodesicPoints(net, i, j, k);
    for (let s = 0; s < k; s++) {
      const pt = pts[Math.min(s, pts.length - 1)];
      geo[(e * k + s) * 2] = pt[0];
      geo[(e * k + s) * 2 + 1] = pt[1];
    }
  });
  net.geo = geo;
  return geo;
}

/**
 * Draws the disc on a 2D context of a square `size` px canvas. options: {focus: node id or null (re-centres on it),
 * routes: [{path, color}], packet: node id or null, src, dst, colors: {...}}.
 * Returns {nodes, edges, ms}: how many were drawn, and the time of the draw.
 */
export function drawNetwork(ctx, net, size, options = {}) {
  const t0 = performance.now();
  const colors = Object.assign({ disc: '#5b6b7a', edge: 'rgba(127,179,213,0.5)', node: '#c9d3df', src: '#27ae60', dst: '#c0392b', packet: '#f5c518' }, options.colors);
  const focus = options.focus === undefined || options.focus === null ? null : net.points[options.focus];
  const move = focus && (focus[0] !== 0 || focus[1] !== 0)
    ? (x, y) => mobius(x, y, focus[0], focus[1])
    : (x, y) => [x, y];
  const k = EDGE_SEGMENTS + 1;
  const geo = edgeSamples(net);

  ctx.clearRect(0, 0, size, size);
  ctx.lineWidth = 1.2;
  ctx.strokeStyle = colors.disc;
  const [cx, cy] = toScreen(0, 0, size);
  ctx.beginPath();
  ctx.arc(cx, cy, (size / 2) / VIEW_HALF_EXTENT, 0, 2 * Math.PI);
  ctx.stroke();

  ctx.lineWidth = 0.6;
  ctx.strokeStyle = colors.edge;
  ctx.beginPath();
  for (let e = 0; e < net.edges.length; e++) {
    for (let s = 0; s < k; s++) {
      const [x, y] = move(geo[(e * k + s) * 2], geo[(e * k + s) * 2 + 1]);
      const [px, py] = toScreen(x, y, size);
      if (s === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
    }
  }
  ctx.stroke();

  for (const route of options.routes || []) {
    ctx.lineWidth = 2.4;
    ctx.strokeStyle = route.color;
    ctx.beginPath();
    for (let h = 0; h + 1 < route.path.length; h++) {
      const pts = geodesicPoints(net, route.path[h], route.path[h + 1]);
      pts.forEach((pt, s) => {
        const [x, y] = move(pt[0], pt[1]);
        const [px, py] = toScreen(x, y, size);
        if (s === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
      });
    }
    ctx.stroke();
  }

  const screen = new Array(net.nodes.length);
  for (let i = 0; i < net.nodes.length; i++) {
    const [x, y] = move(net.points[i][0], net.points[i][1]);
    const [px, py] = toScreen(x, y, size);
    screen[i] = [px, py];
    ctx.fillStyle = colors.node;
    ctx.beginPath();
    ctx.arc(px, py, 1.6 + 0.9 * Math.sqrt(net.nodes[i].degree), 0, 2 * Math.PI);
    ctx.fill();
  }
  const mark = (id, color, radius) => {
    if (id === undefined || id === null || !screen[id]) return;
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(screen[id][0], screen[id][1], radius, 0, 2 * Math.PI);
    ctx.fill();
  };
  mark(options.src, colors.src, 6);
  mark(options.dst, colors.dst, 6);
  mark(options.packet, colors.packet, 5);
  return { nodes: net.nodes.length, edges: net.edges.length, ms: performance.now() - t0, screen };
}

/**
 * The hop-by-hop frames of one packet: the hyperbolic route first, then the flat-picture route.
 * Each frame: {rule, hop, node, path (so far), last, success}; `last` marks the end of a rule's route.
 */
export function packetFrames(net, src, dst) {
  const frames = [];
  for (const rule of ['hyperbolic', 'flat']) {
    const route = greedyRoute(net, src, dst, rule);
    route.path.forEach((node, hop) => {
      frames.push({ rule, hop, node, path: route.path.slice(0, hop + 1), last: hop === route.path.length - 1, success: route.success, hops: route.hops });
    });
  }
  return frames;
}

function pct(v) { return (100 * v).toFixed(1) + ' %'; }

/** The study panel: success and stretch for both rules over the file's pairs. */
export function studyText(study) {
  const line = (name, s) => name + ': success ' + pct(s.success) + ', stretch ' + (s.stretch === null ? 'n/a' : s.stretch.toFixed(3));
  return [
    'Study: ' + study.pairs + ' pairs per rule',
    line('Hyperbolic rule', study.hyperbolic),
    line('Flat-picture rule', study.flat_picture)
  ];
}

// ---- the page --------------------------------------------------------------------------------------

const STEP_MS = 600;

/** Wires the Network page. Called once by app.js; returns {show()} to call whenever the page is shown. */
export function mountNetworkPage() {
  const $ = (id) => document.getElementById(id);
  const state = { net: null, files: [], file: '', src: 0, dst: 1, frames: [], frame: -1, focus: null, timer: 0, loading: false };

  const ink = (name, fallback) => {
    const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return v || fallback;
  };

  function paint() {
    const canvas = $('net-canvas');
    if (!state.net) return;
    const size = Math.max(200, Math.floor(Math.min(canvas.clientWidth, canvas.clientHeight || canvas.clientWidth)));
    const ratio = window.devicePixelRatio || 1;
    if (canvas.width !== Math.round(size * ratio)) { canvas.width = Math.round(size * ratio); canvas.height = Math.round(size * ratio); }
    const g = canvas.getContext('2d');
    g.setTransform(ratio, 0, 0, ratio, 0, 0);
    const frame = state.frames[state.frame];
    const routes = [];
    if (frame) {
      const hyper = state.frames.filter((f) => f.rule === 'hyperbolic' && f.hop <= (frame.rule === 'hyperbolic' ? frame.hop : Infinity));
      if (hyper.length > 1) routes.push({ path: hyper.map((f) => f.node), color: ink('--accent', '#2e86c1') });
      if (frame.rule === 'flat') {
        const flat = state.frames.filter((f) => f.rule === 'flat' && f.hop <= frame.hop);
        if (flat.length > 1) routes.push({ path: flat.map((f) => f.node), color: ink('--warn', '#e67e22') });
      }
    }
    const result = drawNetwork(g, state.net, size, { focus: state.focus, routes, packet: frame ? frame.node : null, src: state.src, dst: state.dst });
    $('net-draw-ms').textContent = result.ms.toFixed(1) + ' ms';
    state.screen = result.screen;
    state.size = size;
  }

  function describe() {
    const frame = state.frames[state.frame];
    if (!frame) { $('net-packet').textContent = 'Press Send packet.'; return; }
    const name = frame.rule === 'hyperbolic' ? 'Hyperbolic rule' : 'Flat-picture rule';
    const end = frame.last ? (frame.success ? ' delivered in ' + frame.hops + ' hops' : ' dropped at node ' + frame.node + ' after ' + frame.hops + ' hops') : '';
    $('net-packet').textContent = name + ': hop ' + frame.hop + ' at node ' + frame.node + end;
  }

  function stop() { if (state.timer) { clearTimeout(state.timer); state.timer = 0; } }

  function step() {
    state.timer = 0;
    state.frame++;
    paint();
    describe();
    if (state.frame < state.frames.length - 1) state.timer = setTimeout(step, STEP_MS);
  }

  function send() {
    if (!state.net) return;
    stop();
    state.src = Number($('net-src').value) | 0;
    state.dst = Number($('net-dst').value) | 0;
    const n = state.net.nodes.length;
    if (!(state.src >= 0 && state.src < n && state.dst >= 0 && state.dst < n && state.src !== state.dst)) {
      $('net-packet').textContent = 'Pick two different node ids from 0 to ' + (n - 1) + '.';
      return;
    }
    state.frames = packetFrames(state.net, state.src, state.dst);
    state.frame = -1;
    step();
  }

  async function load(file) {
    if (state.loading) return;
    state.loading = true;
    try {
      const res = await fetch('/api/network?file=' + encodeURIComponent(file));
      if (!res.ok) throw new Error('HTTP ' + res.status);
      state.net = parseNetwork(await res.json());
      state.file = file;
      state.frames = []; state.frame = -1; state.focus = null;
      const lines = studyText(state.net.study);
      $('net-study').textContent = lines.join('\n');
      $('net-facts').textContent = state.net.nodes.length + ' nodes, ' + state.net.edges.length + ' edges, seed ' + state.net.model.seed;
      $('net-status').textContent = '';
      paint();
      describe();
    } catch (e) {
      $('net-status').textContent = 'Could not load ' + file + ': ' + e.message;
    } finally {
      state.loading = false;
    }
  }

  async function show() {
    if (state.net) { paint(); return; }
    try {
      const res = await fetch('/api/network');
      if (!res.ok) throw new Error('HTTP ' + res.status);
      state.files = (await res.json()).files;
      const select = $('net-file');
      select.textContent = '';
      for (const f of state.files) {
        const o = document.createElement('option');
        o.value = f; o.textContent = f;
        select.append(o);
      }
      if (state.files.length === 0) { $('net-status').textContent = 'The Hub has no bundled network files.'; return; }
      select.value = state.files[0];
      await load(state.files[0]);
    } catch (e) {
      $('net-status').textContent = 'Could not list networks: ' + e.message;
    }
  }

  $('net-file').addEventListener('change', () => load($('net-file').value));
  $('net-send').addEventListener('click', send);
  $('net-reset').addEventListener('click', () => { stop(); state.frames = []; state.frame = -1; state.focus = null; paint(); describe(); });
  $('net-canvas').addEventListener('click', (ev) => {
    if (!state.screen) return;
    const box = ev.target.getBoundingClientRect ? ev.target.getBoundingClientRect() : { left: 0, top: 0 };
    const x = ev.clientX - box.left, y = ev.clientY - box.top;
    let best = -1, bd = 14 * 14;
    state.screen.forEach((p, i) => { const d = (p[0] - x) ** 2 + (p[1] - y) ** 2; if (d < bd) { bd = d; best = i; } });
    if (best >= 0) { state.focus = best; paint(); }
  });
  return { show, load, send, state };
}
