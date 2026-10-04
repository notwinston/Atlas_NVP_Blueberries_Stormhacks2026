import test from 'node:test';
import assert from 'node:assert/strict';
import {
  Trail, boundsOf, cellLabel, corners, fitView, headingVector, nearestObstacle, parseTile, rectCorners, relativeSide,
  toRoom, toScreen
} from '../hub/wwwroot/js/mapmath.js';

const close = (a, b, eps = 1e-9) => assert.ok(Math.abs(a - b) <= eps, a + ' is not ' + b);

// The room of ReplayPhone's synthetic walk, as the Hub's outline frame.
const room = {
  outline: [[-1.9, -2.4], [1.9, -2.4], [1.9, 2.4], [-1.9, 2.4]],
  height: 2.5,
  rect: { cx: 0, cz: 0, wf: 3, df: 4, yaw: 0 },
  boxes: [{ id: 1, cat: 1, cx: 1.6, cz: 0, w: 0.6, d: 1.2, h: 0.75, yaw: 0 }]
};

test('corners of an unturned and a turned box', () => {
  assert.deepEqual(corners(0, 0, 2, 4, 0), [[-1, -2], [1, -2], [1, 2], [-1, 2]]);
  // A quarter turn about +Y takes the box's +Z to the room's +X.
  const q = corners(0, 0, 2, 4, Math.PI / 2);
  close(q[2][0], 2);
  close(q[2][1], -1);
});

test('bounds cover the outline, the rectangle and the boxes', () => {
  const b = boundsOf(room);
  close(b.minX, -1.9);
  close(b.maxX, 1.9, 1e-9);
  close(b.minZ, -2.4);
  close(b.maxZ, 2.4);
  assert.deepEqual(boundsOf(null), { minX: -2, maxX: 2, minZ: -2, maxZ: 2 });
  assert.deepEqual(boundsOf({ outline: [], boxes: [{ id: 1, cx: 5, cz: 5, w: 2, d: 2, yaw: 0 }] }), { minX: 4, maxX: 6, minZ: 4, maxZ: 6 });
  assert.equal(rectCorners({ wf: 0, df: 1 }), null);
});

test('the view fits the room at one scale and puts +Z at the top', () => {
  const view = fitView(boundsOf(room), 428, 528, 24);
  close(view.scale, 100);
  assert.deepEqual(toScreen(view, 0, 0), [214, 264]);
  const [px, py] = toScreen(view, 1.9, 2.4);
  close(px, 404);
  close(py, 24);
  const back = toRoom(view, px, py);
  close(back[0], 1.9);
  close(back[1], 2.4);
});

test('a wide canvas is limited by the room depth', () => {
  const view = fitView(boundsOf(room), 2000, 528, 24);
  close(view.scale, 100);
});

test('heading 0 is +Z and 90 is +X', () => {
  const f0 = headingVector(0);
  close(f0[0], 0);
  close(f0[1], 1);
  const f90 = headingVector(90);
  close(f90[0], 1);
  close(f90[1], 0, 1e-12);
});

test('the trail keeps the newest points and skips a walker standing still', () => {
  const trail = new Trail(3, 0.01);
  assert.equal(trail.add(0, 0), true);
  assert.equal(trail.add(0.001, 0), false);
  trail.add(1, 0);
  trail.add(2, 0);
  trail.add(3, 0);
  assert.deepEqual(trail.points, [[1, 0], [2, 0], [3, 0]]);
  assert.equal(trail.add(NaN, 0), false);
  trail.clear();
  assert.equal(trail.points.length, 0);
});

test('the nearest obstacle is a wall or an object box', () => {
  const wall = nearestObstacle(room, 0, 2.0);
  assert.equal(wall.kind, 'wall');
  close(wall.distance, 0.4);
  close(wall.dirX, 0);
  close(wall.dirZ, 1);

  const box = nearestObstacle(room, 1.0, 0.2);
  assert.equal(box.kind, 'object');
  assert.equal(box.id, 1);
  close(box.distance, 0.3);
  close(box.dirX, 1);

  assert.equal(nearestObstacle({ outline: [], boxes: [] }, 0, 0), null);
  assert.equal(nearestObstacle(null, 0, 0), null);
});

test('sides are told from the walker', () => {
  assert.equal(relativeSide(0, 0, 1), 'ahead');
  assert.equal(relativeSide(0, 1, 0), 'right');
  assert.equal(relativeSide(0, -1, 0), 'left');
  assert.equal(relativeSide(0, 0, -1), 'behind');
  assert.equal(relativeSide(90, 1, 0), 'ahead');
  assert.equal(relativeSide(90, 0, 1), 'left');
});

test('tile ids stay text, and each space names its cell', () => {
  assert.deepEqual(parseTile('-1,0,0,0,0,1'), ['-1', '0', '0', '0', '0', '1']);
  assert.deepEqual(parseTile('9223372036854775807,0,0,0,0,0')[0], '9223372036854775807');
  assert.deepEqual(parseTile('1,2'), ['0', '0', '0', '0', '0', '0']);
  assert.deepEqual(parseTile('a,0,0,0,0,0'), ['0', '0', '0', '0', '0', '0']);
  assert.equal(cellLabel(0, '0,0,0,0,0,0', 0), 'room');
  assert.equal(cellLabel(1, '2,-1,0,0,0,0', 0), 'cell (2, -1, 0)');
  assert.equal(cellLabel(4, '3,0,0,0,0,0', 0), 'face 3');
  assert.equal(cellLabel(5, '2,0,0,0,0,0', 1), 'room 2 · sheet 1');
  assert.equal(cellLabel(2, '-1,0,0,0,0,1', 3), 'tile -1,0,0,0,0,1 · sheet 3');
});
