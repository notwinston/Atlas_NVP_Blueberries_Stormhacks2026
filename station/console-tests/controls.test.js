import test from 'node:test';
import assert from 'node:assert/strict';
import {
  SendThrottle, buildGroups, controlFor, displayValue, findControl, groupTitle, isPresetName, presetOf, readEcho,
  requestValue
} from '../hub/wwwroot/js/controls.js';

// The manifest ReplayPhone's synthetic walk declares, as GET /api/params returns it.
const manifest = [
  { id: 0, name: 'curved.cullRho', group: 'curved', min: 2, max: 3.5, def: 3, val: 3, onRelease: false, kind: 'float', active: false },
  { id: 1, name: 'render.scale', group: 'render', min: 0.5, max: 1, def: 1, val: 0.8, onRelease: false, kind: 'float', active: true },
  { id: 2, name: 'render.msaa', group: 'render', min: 0, max: 2, def: 2, val: 2, onRelease: false, kind: 'enum', active: true, names: ['off', '2x', '4x'] },
  { id: 3, name: 'glued.copies', group: 'glued', min: 3, max: 5, def: 5, val: 5, onRelease: true, kind: 'int', active: true },
  { id: 4, name: 'pose.gyroPredict', group: 'pose', min: 0, max: 1, def: 1, val: 1, onRelease: false, kind: 'bool', active: true },
  { id: 5, name: 'demoLock', group: 'app', min: 0, max: 1, def: 0, val: 0, onRelease: false, kind: 'bool', active: true }
];

test('a control per kind', () => {
  assert.equal(controlFor(manifest[0]).widget, 'slider');
  assert.equal(controlFor(manifest[2]).widget, 'select');
  assert.equal(controlFor(manifest[3]).widget, 'slider');
  assert.equal(controlFor(manifest[4]).widget, 'toggle');
  assert.equal(controlFor(manifest[3]).step, 1);
  assert.equal(controlFor(manifest[0]).decimals, 3);
  assert.deepEqual(controlFor(manifest[2]).options, [{ value: 0, label: 'off' }, { value: 1, label: '2x' }, { value: 2, label: '4x' }]);
});

test('groups keep manifest order and know whether they are active', () => {
  const groups = buildGroups(manifest);
  assert.deepEqual(groups.map((g) => g.key), ['curved', 'render', 'glued', 'pose', 'app']);
  assert.equal(groups[0].active, false);
  assert.equal(groups[1].active, true);
  assert.equal(groups[1].controls.length, 2);
  assert.equal(groups[0].title, 'S2 Curved Walk');
  assert.equal(groupTitle('glued'), 'S1 Glued');
  assert.equal(groupTitle('playground'), 'Playground');
  assert.equal(groupTitle(''), 'Other');
});

test('a group with one active parameter is active', () => {
  const groups = buildGroups([
    { id: 0, name: 'a.x', group: 'a', min: 0, max: 1, def: 0, val: 0, kind: 'float', active: false },
    { id: 1, name: 'a.y', group: 'a', min: 0, max: 1, def: 0, val: 0, kind: 'float', active: true }
  ]);
  assert.equal(groups[0].active, true);
});

test('an empty or missing manifest gives no groups', () => {
  assert.deepEqual(buildGroups([]), []);
  assert.deepEqual(buildGroups(undefined), []);
  assert.deepEqual(buildGroups([null, {}]), []);
});

test('values clamp to the declared range', () => {
  const rho = controlFor(manifest[0]);
  assert.equal(requestValue(rho, '9'), 3.5);
  assert.equal(requestValue(rho, -1), 2);
  assert.equal(requestValue(rho, 'x'), null);
  const copies = controlFor(manifest[3]);
  assert.equal(requestValue(copies, 4.4), 4);
  assert.equal(requestValue(copies, 40), 5);
  assert.equal(requestValue(controlFor(manifest[4]), true), 1);
  assert.equal(requestValue(controlFor(manifest[2]), 7), 2);
});

test('a manifest value outside its range is shown clamped, and a broken range is held to one value', () => {
  assert.equal(controlFor({ id: 0, name: 'a.b', group: 'a', min: 0, max: 1, def: 0, val: 5, kind: 'float' }).val, 1);
  const broken = controlFor({ id: 0, name: 'a.b', group: 'a', min: 2, max: 1, def: 0, val: 5, kind: 'float' });
  assert.equal(broken.min, 2);
  assert.equal(broken.max, 2);
  assert.equal(broken.val, 2);
});

test('an enum takes its range from its names', () => {
  const c = controlFor({ id: 0, name: 'a.b', group: 'a', min: 0, max: 9, def: 0, val: 1, kind: 'enum', names: ['x', 'y'] });
  assert.equal(c.max, 1);
  assert.equal(displayValue(c), 'y');
});

test('the lock is flagged so that its toggle sends the lock command', () => {
  assert.equal(controlFor(manifest[5]).viaLock, true);
  assert.equal(controlFor(manifest[4]).viaLock, false);
});

test('displayValue', () => {
  assert.equal(displayValue(controlFor(manifest[1])), '0.800');
  assert.equal(displayValue(controlFor(manifest[3]), 3), '3');
  assert.equal(displayValue(controlFor(manifest[4])), 'on');
});

test('readEcho reads the link echo and the app log form', () => {
  assert.deepEqual(readEcho({ ev: 'param', data: { id: 1, name: 'render.scale', value: 0.7, source: 'remote' } }),
    [{ name: 'render.scale', id: 1, value: 0.7 }]);
  assert.deepEqual(readEcho({ ev: 'param.set', data: { src: 'link', values: { 'render.scale': 0.6, 3: 5 } } }),
    [{ name: null, id: 3, value: 5 }, { name: 'render.scale', id: null, value: 0.6 }]);
  assert.deepEqual(readEcho({ ev: 'cmd', data: { cmd: 'home' } }), []);
  assert.deepEqual(readEcho(null), []);
});

test('findControl matches by name, then by wire id', () => {
  const groups = buildGroups(manifest);
  assert.equal(findControl(groups, { name: 'render.msaa', id: null, value: 1 }).id, 2);
  assert.equal(findControl(groups, { name: null, id: 3, value: 1 }).name, 'glued.copies');
  assert.equal(findControl(groups, { name: 'nope', id: null, value: 1 }), null);
});

test('presetOf carries every value but the lock', () => {
  const preset = presetOf(buildGroups(manifest));
  assert.equal(preset.values['render.scale'], 0.8);
  assert.equal(Object.keys(preset.values).length, 5);
  assert.equal('demoLock' in preset.values, false);
});

test('preset names', () => {
  assert.equal(isPresetName('demo_1-a'), true);
  assert.equal(isPresetName(''), false);
  assert.equal(isPresetName('has space'), false);
  assert.equal(isPresetName('x'.repeat(41)), false);
});

test('a dragged slider sends at most 30 times a second and never loses the last value', () => {
  const throttle = new SendThrottle();
  let sent = 0;
  let last = null;
  // 1 kHz of input for one second.
  for (let ms = 0; ms < 1000; ms++) {
    if (throttle.offer(ms, ms)) {
      sent++;
      last = ms;
    }
  }
  assert.ok(sent <= 31 && sent >= 29, 'sent ' + sent);
  assert.notEqual(last, 999);
  assert.equal(throttle.take(1000, true), 999);
  assert.equal(throttle.take(1001, true), null);
});

test('the throttle hands over a waiting value once the interval has passed', () => {
  const throttle = new SendThrottle(100);
  assert.equal(throttle.offer(1, 0), true);
  assert.equal(throttle.offer(2, 50), false);
  assert.equal(throttle.take(60), null);
  assert.equal(throttle.take(100), 2);
  assert.equal(throttle.offer(3, 150), false);
});
