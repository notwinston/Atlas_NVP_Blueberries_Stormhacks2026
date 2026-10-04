import test from 'node:test';
import assert from 'node:assert/strict';
import { CommandTracker, comfortScore, describe, fmsAlertText, seqScore, requestFor, statusText } from '../hub/wwwroot/js/commands.js';

test('the six commands map to the Hub\'s two routes', () => {
  assert.deepEqual(requestFor('space', { space: 1 }), { path: '/api/scene', body: { space: 'S1', variant: '', args: {} } });
  assert.deepEqual(requestFor('home'), { path: '/api/op', body: { cmd: 'home', args: {} } });
  assert.deepEqual(requestFor('recentre'), { path: '/api/op', body: { cmd: 'recentre', args: {} } });
  assert.deepEqual(requestFor('quality', { tier: -1 }), { path: '/api/op', body: { cmd: 'quality', args: { tier: -1 } } });
  assert.deepEqual(requestFor('cue'), { path: '/api/op', body: { cmd: 'cue', args: {} } });
  assert.deepEqual(requestFor('lock', { on: true }), { path: '/api/op', body: { cmd: 'lock', args: { on: true } } });
});

test('a command outside its range is not sent', () => {
  assert.equal(requestFor('space', { space: 7 }), null);
  assert.equal(requestFor('space', {}), null);
  assert.equal(requestFor('quality', { tier: 5 }), null);
  assert.equal(requestFor('freeze'), null);
});

test('labels', () => {
  assert.equal(describe('cue'), 'STOP');
  assert.equal(describe('space', { space: 5 }), 'switch to S5');
  assert.equal(describe('quality', { tier: -1 }), 'quality auto');
  assert.equal(describe('lock', { on: false }), 'lock off');
});

test('the comfort score is a whole number from 0 to 20', () => {
  assert.equal(comfortScore('0'), 0);
  assert.equal(comfortScore(' 20 '), 20);
  assert.equal(comfortScore(7), 7);
  assert.equal(comfortScore('21'), null);
  assert.equal(comfortScore('-1'), null);
  assert.equal(comfortScore('3.5'), null);
  assert.equal(comfortScore(''), null);
});

test('a command is acknowledged by the Hub and then by the phone, each with its latency', () => {
  const tracker = new CommandTracker();
  const entry = tracker.sent('cue', {}, 1000);
  assert.equal(statusText(entry), 'sending…');
  tracker.hubAnswered(entry, true, 1004);
  assert.equal(statusText(entry), 'hub 4 ms · waiting for the phone');
  const acked = tracker.phoneEvent({ ev: 'cmd', data: { cmd: 'cue' } }, 1021);
  assert.equal(acked, entry);
  assert.equal(entry.phoneMs, 21);
  assert.equal(statusText(entry), 'hub 4 ms · phone 21 ms');
  assert.equal(tracker.latest('cue'), entry);
});

test('the phone can answer before the Hub\'s HTTP reply is read', () => {
  const tracker = new CommandTracker();
  const entry = tracker.sent('home', {}, 0);
  tracker.phoneEvent({ ev: 'op', data: { cmd: 'home', n: 3 } }, 12);
  tracker.hubAnswered(entry, true, 15);
  assert.equal(statusText(entry), 'hub 15 ms · phone 12 ms');
});

test('acknowledgements go to the oldest waiting command of that name', () => {
  const tracker = new CommandTracker();
  const first = tracker.sent('space', { space: 1 }, 0);
  const second = tracker.sent('space', { space: 0 }, 5);
  assert.equal(tracker.phoneEvent({ ev: 'cmd', data: { cmd: 'space', space: 1 } }, 20), first);
  assert.equal(tracker.phoneEvent({ ev: 'cmd', data: { cmd: 'space', space: 0 } }, 30), second);
  assert.equal(tracker.phoneEvent({ ev: 'cmd', data: { cmd: 'space', space: 0 } }, 40), null);   // another Console's
  assert.equal(tracker.phoneEvent({ ev: 'bump.level', data: {} }, 40), null);
});

test('a refused command waits for nothing, and a silent phone is reported', () => {
  const tracker = new CommandTracker();
  const refused = tracker.sent('recentre', {}, 0);
  tracker.hubAnswered(refused, false, 3, 'no phone connected');
  assert.equal(statusText(refused), 'refused: no phone connected');
  assert.equal(tracker.expire(10000), false);

  const silent = tracker.sent('home', {}, 100);
  tracker.hubAnswered(silent, true, 102);
  assert.equal(tracker.expire(1000), false);
  assert.equal(tracker.expire(100 + 3001), true);
  assert.equal(statusText(silent), 'hub 2 ms · no answer from the phone');
});

test('the tracker keeps a bounded history, newest first', () => {
  const tracker = new CommandTracker(3);
  for (let i = 0; i < 5; i++) tracker.sent('home', {}, i);
  assert.equal(tracker.entries.length, 3);
  assert.equal(tracker.entries[0].sentMs, 4);
});

test('the SEQ field takes a whole number from 1 to 7 and nothing else', () => {
  for (let n = 1; n <= 7; n++) assert.equal(seqScore(String(n)), n);
  assert.equal(seqScore(' 4 '), 4);
  assert.equal(seqScore(7), 7);
  for (const bad of ['0', '8', '10', '-1', '3.5', '', 'x']) assert.equal(seqScore(bad), null, bad);
});

test('the FMS alert text names the score and the stop value the Hub judged it against', () => {
  const text = fmsAlertText({ score: 6, stop: 4 });
  assert.match(text, /score 6/);
  assert.match(text, /stop value 4/);
  assert.match(text, /pg\.fmsStop/);
  assert.equal(fmsAlertText(null), '');
  assert.equal(fmsAlertText({}), '');
});
