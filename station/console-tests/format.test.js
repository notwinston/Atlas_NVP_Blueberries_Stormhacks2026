import test from 'node:test';
import assert from 'node:assert/strict';
import {
  bumpName, categoryName, clamp, clampParam, decimalsFor, fixed, formatCount, formatDuration, formatMs, formatOffset, formatParam,
  formatPercent, roundHalfAway, spaceBuilt, spaceLabel, stepFor, thermalAdvice, thermalName, tierLabel
} from '../hub/wwwroot/js/format.js';

test('clamp holds a value in its range and maps NaN to the minimum', () => {
  assert.equal(clamp(5, 0, 3), 3);
  assert.equal(clamp(-1, 0, 3), 0);
  assert.equal(clamp(2, 0, 3), 2);
  assert.equal(clamp(NaN, 1, 3), 1);
});

test('roundHalfAway rounds as the registry does', () => {
  assert.equal(roundHalfAway(2.5), 3);
  assert.equal(roundHalfAway(-2.5), -3);
  assert.equal(roundHalfAway(-0.4), 0);
});

test('stepFor gives 1 for integers and a 1-2-5 step for floats', () => {
  assert.equal(stepFor('int', 0, 6), 1);
  assert.equal(stepFor('enum', 0, 2), 1);
  assert.equal(stepFor('float', 2, 3.5), 0.005);
  assert.equal(stepFor('float', 0.5, 1), 0.002);
  assert.equal(stepFor('float', -180, 180), 1);
  assert.equal(stepFor('float', 0.055, 0.075), 0.0001);
  assert.equal(stepFor('float', 1, 1), 1);
});

test('decimalsFor shows a step without loss', () => {
  assert.equal(decimalsFor(1), 0);
  assert.equal(decimalsFor(0.5), 1);
  assert.equal(decimalsFor(0.005), 3);
  assert.equal(decimalsFor(0.0001), 4);
});

test('clampParam mirrors the registry clamp for every kind', () => {
  assert.equal(clampParam('float', 2, 3.5, 99), 3.5);
  assert.equal(clampParam('float', 2, 3.5, '1'), 2);
  assert.equal(clampParam('float', 2, 3.5, '2,75'), 2.75);
  assert.equal(clampParam('int', 3, 5, 4.5), 5);
  assert.equal(clampParam('int', 3, 5, -7), 3);
  assert.equal(clampParam('enum', 0, 2, 1.4), 1);
  assert.equal(clampParam('enum', 0, 2, 9), 2);
  assert.equal(clampParam('bool', 0, 1, true), 1);
  assert.equal(clampParam('bool', 0, 1, 0.49), 0);
  assert.equal(clampParam('bool', 0, 1, 7), 1);
});

test('clampParam refuses what is not a number', () => {
  assert.equal(clampParam('float', 0, 1, 'abc'), null);
  assert.equal(clampParam('float', 0, 1, ''), null);
  assert.equal(clampParam('float', 0, 1, NaN), null);
  assert.equal(clampParam('float', 0, 1, Infinity), null);
});

test('formatParam shows each kind', () => {
  assert.equal(formatParam('bool', 1), 'on');
  assert.equal(formatParam('bool', 0), 'off');
  assert.equal(formatParam('enum', 1, ['off', '2x', '4x']), '2x');
  assert.equal(formatParam('enum', 5, ['off']), '5');
  assert.equal(formatParam('int', 4.0), '4');
  assert.equal(formatParam('float', 0.8, null, 2), '0.80');
  assert.equal(formatParam('float', undefined), '–');
});

test('number formatting', () => {
  assert.equal(fixed(-0.0001, 2), '0.00');
  assert.equal(fixed(NaN, 2), '–');
  assert.equal(formatMs(4.13), '4.1 ms');
  assert.equal(formatMs(16.7), '17 ms');
  assert.equal(formatMs(null), '–');
  assert.equal(formatOffset(1.26), '+1.3 ms');
  assert.equal(formatOffset(-2500), '−2.500 s');
  assert.equal(formatOffset(-0.02), '0.0 ms');
  assert.equal(formatOffset(undefined), '–');
  assert.equal(formatPercent(0.25), '0.3 %');
  assert.equal(formatPercent(12.4), '12 %');
  assert.equal(formatDuration(75), '1:15');
  assert.equal(formatDuration(3725), '1:02:05');
  assert.equal(formatCount(1234567), '1 234 567');
  assert.equal(formatCount(12), '12');
});

test('names of spaces, tiers, thermal states, bump levels and categories', () => {
  assert.equal(spaceLabel(0), 'S0 Home');
  assert.equal(spaceLabel(5), 'S5 Impossible Rooms');
  assert.equal(spaceLabel(9), 'S?');
  assert.equal(spaceBuilt(1), true);
  assert.equal(spaceBuilt(3), false);
  assert.equal(tierLabel(-1), 'auto');
  assert.match(tierLabel(2), /^tier 2 \(0\.6/);
  assert.equal(thermalName(2), 'serious');
  assert.equal(thermalAdvice(2), 'swap phone');
  assert.equal(thermalAdvice(0), '');
  assert.equal(bumpName(3), 'tracking lost');
  assert.equal(categoryName(2), 'table');
  assert.equal(categoryName('couch'), 'couch');
  assert.equal(categoryName(99), 'unknown');
});
