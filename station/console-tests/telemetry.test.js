import test from 'node:test';
import assert from 'node:assert/strict';
import { CSV_HEADER, FIELDS, TelemetryBuffer, niceCeil, parseCsv, percentile } from '../hub/wwwroot/js/telemetry.js';

const row = (seq, tS, extra = {}) => ({ seq, t_us: Math.round(tS * 1e6), p95: 16.9, ...extra });

test('the header is the contract header of 34 columns', () => {
  assert.equal(FIELDS.length, 34);
  assert.equal(FIELDS[0], 't_us');
  assert.equal(FIELDS[33], 'assetsPending');
});

test('percentile is nearest rank, as on the Hub', () => {
  const v = [];
  for (let i = 1; i <= 100; i++) v.push(i);
  assert.equal(percentile(v, 0.5), 50);
  assert.equal(percentile(v, 0.95), 95);
  assert.equal(percentile(v, 0.99), 99);
  assert.equal(percentile(v, 1), 100);
  assert.equal(percentile(v, 0), 1);
  assert.equal(percentile([7], 0.95), 7);
  assert.ok(Number.isNaN(percentile([], 0.5)));
  assert.equal(percentile([3, NaN, 1, 2], 0.5), 2);
});

test('percentile leaves its input alone', () => {
  const v = [3, 1, 2];
  percentile(v, 0.5);
  assert.deepEqual(v, [3, 1, 2]);
});

test('rows are kept in seq order with no duplicates', () => {
  const b = new TelemetryBuffer();
  assert.equal(b.push(row(1, 1)), true);
  assert.equal(b.push(row(3, 3)), true);
  assert.equal(b.push(row(2, 2)), true);    // a backlog row, sent late
  assert.equal(b.push(row(3, 3)), false);   // sent again
  assert.deepEqual(b.series('seq'), [1, 2, 3]);
  assert.equal(b.last.seq, 3);
  assert.equal(b.push(null), false);
  assert.equal(b.push({ seq: 4 }), false);
});

test('the buffer keeps the newest rows up to its capacity', () => {
  const b = new TelemetryBuffer(5);
  for (let i = 0; i < 12; i++) b.push(row(i, i));
  assert.equal(b.length, 5);
  assert.deepEqual(b.series('seq'), [7, 8, 9, 10, 11]);
  assert.equal(b.push(row(6, 6)), false);   // older than everything kept
  assert.deepEqual(b.seconds(), [0, 1, 2, 3, 4]);
});

test('a seq that starts again is a new run', () => {
  const b = new TelemetryBuffer(5);
  for (let i = 100; i < 110; i++) b.push(row(i, i));
  assert.equal(b.push(row(0, 500)), true);
  assert.deepEqual(b.series('seq'), [0]);
});

test('gaps over 2 s are found, a gap of exactly 2 s is not', () => {
  const b = new TelemetryBuffer();
  b.pushAll([row(0, 0), row(1, 1), row(2, 3), row(3, 7.5), row(4, 8.5)]);
  const gaps = b.gaps();
  assert.equal(gaps.length, 1);
  assert.deepEqual(gaps[0], { index: 3, fromS: 3, toS: 7.5, seconds: 4.5 });
  assert.deepEqual(new TelemetryBuffer().gaps(), []);
});

test('stats over the rows kept', () => {
  const b = new TelemetryBuffer();
  for (let i = 1; i <= 20; i++) b.push(row(i, i, { fps: i }));
  assert.deepEqual(b.stats('fps'), { min: 1, p50: 10, p95: 19, max: 20 });
  assert.ok(Number.isNaN(new TelemetryBuffer().stats('fps').p50));
});

test('perf.csv parses to rows and bad lines are left out', () => {
  const example = '1234567890,42,1,1,0,60.000,16.667,16.900,17.400,18.250,0.500,0.000,4.100,2.900,4.600,0.800,142,22,231,612,0,25,3,0,1,0,0,87,0,4,60,1,3,1';
  const rows = parseCsv(CSV_HEADER + '\n' + example + '\n\nbroken,line\n' + example.replace('42', 'x') + '\n');
  assert.equal(rows.length, 1);
  assert.equal(rows[0].seq, 42);
  assert.equal(rows[0].p95, 16.9);
  assert.equal(rows[0].memMB, 612);
  assert.equal(rows[0].assetsPending, 1);
  assert.deepEqual(parseCsv('not a perf file\n1,2'), []);
  assert.deepEqual(parseCsv(undefined), []);
});

test('niceCeil picks a round axis bound', () => {
  assert.equal(niceCeil(17.4, 1), 20);
  assert.equal(niceCeil(33.8, 1), 50);
  assert.equal(niceCeil(2.1, 1), 2.5);
  assert.equal(niceCeil(0, 20), 20);
  assert.equal(niceCeil(142, 1), 200);
  assert.equal(niceCeil(100, 1), 100);
});
