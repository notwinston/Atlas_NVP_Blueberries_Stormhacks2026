import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import {
  parseNetwork, project, mobius, toScreen, distance, flatDistance, greedyRoute, shortestHops, geodesicPoints,
  drawNetwork, packetFrames, studyText, EDGE_SEGMENTS, VIEW_HALF_EXTENT
} from '../hub/wwwroot/js/network.js';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), '../pipelines/tests/fixtures/network');
const read = (name) => JSON.parse(readFileSync(path.join(fixtures, name), 'utf8'));
const net7 = parseNetwork(read('network_7.json'));

// A canvas context that counts what is drawn.
function counting() {
  const c = { arcs: 0, lines: 0, moves: 0 };
  return Object.assign(c, {
    clearRect() {}, beginPath() {}, stroke() {}, fill() {}, moveTo() { c.moves++; }, lineTo() { c.lines++; }, arc() { c.arcs++; }
  });
}

test('network: draws 200 nodes and 580 edges for seed 7', () => {
  assert.equal(net7.nodes.length, 200);
  assert.equal(net7.edges.length, 580);
  const g = counting();
  const r = drawNetwork(g, net7, 800, {});
  assert.equal(r.nodes, 200);
  assert.equal(r.edges, 580);
  assert.equal(g.moves, 580);                             // one polyline per edge
  assert.equal(g.lines, 580 * EDGE_SEGMENTS);             // 16 segments each
  assert.equal(g.arcs, 200 + 1);                          // the nodes and the disc outline
  assert.ok(r.ms < 50, 'draw took ' + r.ms);
});

test('network: packet animates hop by hop, hyperbolic rule then flat-picture rule', () => {
  const golden = read('golden_routes_7.json').routes;
  const pair = golden.find((r) => r.hyperbolic.success && !r.flat.success && r.hyperbolic.hops > 2) || golden[0];
  const frames = packetFrames(net7, pair.src, pair.dst);
  const hyper = frames.filter((f) => f.rule === 'hyperbolic');
  const flat = frames.filter((f) => f.rule === 'flat');
  assert.equal(frames.length, hyper.length + flat.length);
  assert.deepEqual(frames.slice(0, hyper.length).map((f) => f.rule), hyper.map(() => 'hyperbolic'));
  assert.deepEqual(hyper.map((f) => f.node), pair.hyperbolic.path);
  assert.deepEqual(flat.map((f) => f.node), pair.flat.path);
  assert.deepEqual(hyper.map((f) => f.hop), hyper.map((_, i) => i));      // one frame per hop
  assert.equal(hyper[hyper.length - 1].last, true);
  assert.equal(flat[flat.length - 1].last, true);
  assert.equal(flat[flat.length - 1].success, pair.flat.success);
  for (let i = 1; i < hyper.length; i++) assert.ok(net7.adjacency[hyper[i - 1].node].includes(hyper[i].node));
});

test('network: greedy routes equal golden_routes on 1,000 pairs, both rules', () => {
  for (const seed of [7, 83]) {
    const net = parseNetwork(read('network_' + seed + '.json'));
    const g = read('golden_routes_' + seed + '.json');
    assert.equal(g.routes.length, 1000);
    for (const r of g.routes) {
      for (const [rule, key] of [['hyperbolic', 'hyperbolic'], ['flat', 'flat']]) {
        const got = greedyRoute(net, r.src, r.dst, rule);
        assert.deepEqual(got.path, r[key].path);
        assert.equal(got.success, r[key].success);
        assert.equal(got.hops, r[key].hops);
      }
      assert.equal(shortestHops(net, r.src, r.dst), r.shortest);
    }
  }
});

test('network: projection equals golden_poincare within 0.5 px at 1000 px', () => {
  for (const seed of [7, 83]) {
    const net = parseNetwork(read('network_' + seed + '.json'));
    const g = read('golden_poincare_' + seed + '.json');
    g.points.forEach(([gx, gy], i) => {
      const [x, y] = project(net.nodes[i].r, net.nodes[i].theta);
      const [px, py] = toScreen(x, y, 1000);
      const [qx, qy] = toScreen(gx, gy, 1000);
      assert.ok(Math.hypot(px - qx, py - qy) <= 0.5);
    });
  }
  assert.equal(VIEW_HALF_EXTENT, 1.03);
  assert.deepEqual(toScreen(0, 0, 1000), [500, 500]);
});

test('network: study panel shows success and stretch for both rules', () => {
  const text = studyText(net7.study).join('\n');
  assert.match(text, /2000 pairs/);
  assert.match(text, /Hyperbolic rule: success 100\.0 %, stretch 1\.000/);
  assert.match(text, /Flat-picture rule: success 74\.1 %, stretch 1\.026/);
  assert.match(studyText({ pairs: 5, hyperbolic: { success: 0, stretch: null }, flat_picture: { success: 0, stretch: null } }).join(), /stretch n\/a/);
});

test('network: distances equal golden_distances', () => {
  const g = read('golden_distances_7.json');
  assert.equal(g.pairs.length, 500);
  for (const [i, j, d] of g.pairs) assert.ok(Math.abs(distance(net7, i, j) - d) < 1e-9);
  assert.ok(flatDistance(net7, 0, 0) === 0);
});

test('network: mobius re-centres a point and geodesics end on their nodes', () => {
  const [ax, ay] = net7.points[5];
  const [u, v] = mobius(ax, ay, ax, ay);
  assert.ok(Math.hypot(u, v) < 1e-12);
  const [i, j] = net7.edges[0];
  const pts = geodesicPoints(net7, i, j);
  assert.equal(pts.length, EDGE_SEGMENTS + 1);
  assert.ok(Math.hypot(pts[0][0] - net7.points[i][0], pts[0][1] - net7.points[i][1]) < 1e-9);
  assert.ok(Math.hypot(pts[16][0] - net7.points[j][0], pts[16][1] - net7.points[j][1]) < 1e-9);
});

test('network: parseNetwork names a bad field', () => {
  const bad = read('network_7.json');
  bad.nodes[3].r = -1;
  assert.throws(() => parseNetwork(bad), /nodes\[3\]\.r/);
  assert.throws(() => parseNetwork({ ...read('network_7.json'), schema: 'x' }), /schema/);
});
