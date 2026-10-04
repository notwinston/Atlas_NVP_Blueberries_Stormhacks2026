import test from 'node:test';
import assert from 'node:assert/strict';
import {
  REPEAT_MS, TURN_FAST_RATE, TURN_RATE, WALK_FAST_SPEED, WALK_SPEED, WalkKeys, WalkSender, isStill, isWalkKey
} from '../hub/wwwroot/js/walk.js';

test('WASD and the arrows are walk keys; other keys are not', () => {
  for (const code of ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight']) {
    assert.equal(isWalkKey(code), true, code);
  }
  for (const code of ['Space', 'KeyX', 'ShiftLeft', 'Enter']) assert.equal(isWalkKey(code), false, code);
});

test('held keys make the walk input', () => {
  const keys = new WalkKeys();
  assert.deepEqual(keys.input(), { x: 0, y: 0, turn: 0, speed: WALK_SPEED });

  assert.equal(keys.down('KeyW'), true);
  assert.deepEqual(keys.input(), { x: 0, y: 1, turn: 0, speed: WALK_SPEED });
  keys.down('KeyD');
  assert.deepEqual(keys.input(), { x: 1, y: 1, turn: 0, speed: WALK_SPEED }, 'diagonal: the phone keeps it at full speed, not faster');
  keys.down('ArrowUp');
  assert.equal(keys.input().y, 1, 'W and the up arrow together are still one forward');
  keys.down('KeyS');
  keys.up('ArrowUp');
  assert.equal(keys.input().y, 0, 'opposite keys cancel');

  keys.clear();
  keys.down('KeyE');
  assert.equal(keys.input().turn, TURN_RATE, 'E turns right');
  keys.up('KeyE');
  keys.down('ArrowLeft');
  assert.equal(keys.input().turn, -TURN_RATE, 'the left arrow turns left');
});

test('Shift walks and turns faster', () => {
  const keys = new WalkKeys();
  assert.equal(keys.down('ShiftLeft'), false, 'Shift is not a walk key itself');
  keys.down('KeyW');
  keys.down('KeyQ');
  assert.deepEqual(keys.input(), { x: 0, y: 1, turn: -TURN_FAST_RATE, speed: WALK_FAST_SPEED });
  keys.up('ShiftLeft');
  assert.equal(keys.input().speed, WALK_SPEED);
});

test('clear lets every key go', () => {
  const keys = new WalkKeys();
  keys.down('KeyW');
  keys.down('ShiftRight');
  keys.clear();
  assert.equal(isStill(keys.input()), true);
  assert.equal(keys.input().speed, WALK_SPEED);
});

test('the sender sends a change at once, repeats a held input, and sends one stop', () => {
  const sender = new WalkSender();
  const still = { x: 0, y: 0, turn: 0, speed: WALK_SPEED };
  const forward = { x: 0, y: 1, turn: 0, speed: WALK_SPEED };

  assert.equal(sender.offer(still, 0), null, 'nothing while standing still');
  assert.deepEqual(sender.offer(forward, 10), forward, 'a key down goes out at once');
  assert.equal(sender.offer(forward, 10 + REPEAT_MS - 1), null);
  assert.deepEqual(sender.offer(forward, 10 + REPEAT_MS), forward, 'a held key is repeated');
  const right = { x: 1, y: 1, turn: 0, speed: WALK_SPEED };
  assert.deepEqual(sender.offer(right, 10 + REPEAT_MS + 5), right, 'a change goes out at once');
  assert.deepEqual(sender.offer(still, 400), still, 'letting go sends one stop');
  assert.equal(sender.offer(still, 400 + 5 * REPEAT_MS), null, 'and nothing after it');
  assert.equal(sender.offer({ ...still, speed: WALK_FAST_SPEED }, 2000), null, 'Shift alone moves nothing');
});

test('reset makes the next moving input go out at once', () => {
  const sender = new WalkSender();
  const forward = { x: 0, y: 1, turn: 0, speed: WALK_SPEED };
  sender.offer(forward, 0);
  sender.reset();
  assert.deepEqual(sender.offer(forward, 1), forward);
});
