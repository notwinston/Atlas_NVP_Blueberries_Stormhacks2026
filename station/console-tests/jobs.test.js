import test from 'node:test';
import assert from 'node:assert/strict';
import { JobList, phaseOf } from '../hub/wwwroot/js/jobs.js';

const outline = { boxes: [{ id: 4, cat: 2 }, { id: 1, cat: 1 }] };

test('every box of the room starts as a placeholder', () => {
  const jobs = new JobList();
  jobs.setRoom(outline, '');
  assert.deepEqual(jobs.list().map((r) => [r.boxId, r.category, r.state]), [[1, 'couch', 'placeholder'], [4, 'table', 'placeholder']]);
  assert.deepEqual(jobs.summary(), { waiting: 2, working: 0, done: 0, fallback: 0, failed: 0 });
});

test('a job runs from queued to placed', () => {
  const jobs = new JobList();
  jobs.setRoom(outline, '');
  jobs.onJob({ t: 'job', kind: 'status', jobId: 'j-1', roomId: 'sample_3x3', boxId: 4, state: 'queued', source: 'local', error: '', queuePos: 2 });
  let row = jobs.list()[1];
  assert.equal(row.roomId, 'sample_3x3');   // the placeholder row was taken over, not doubled
  assert.equal(jobs.list().length, 2);
  assert.equal(row.state, 'queued');
  assert.equal(row.queuePos, 2);
  assert.equal(row.category, 'table');

  jobs.onJob({ kind: 'status', jobId: 'j-1', roomId: 'sample_3x3', boxId: 4, state: 'generating', source: 'local', queuePos: 0 });
  assert.equal(jobs.summary().working, 1);

  jobs.onJob({ kind: 'ready', jobId: 'j-1', roomId: 'sample_3x3', boxId: 4, sha256: 'ab', url: '/assets/ab.glb', bytes: 183220, tris: 4998 });
  row = jobs.byJob('j-1');
  assert.equal(row.state, 'ready');
  assert.equal(row.tris, 4998);

  // AssetAck names the job and the box but no room.
  jobs.onJob({ kind: 'ack', jobId: 'j-1', boxId: 4, ok: true, loadMs: 212.5 });
  assert.equal(row.state, 'placed');
  assert.equal(row.loadMs, 212.5);
  assert.equal(jobs.list().length, 2);
  assert.equal(jobs.summary().done, 1);
});

test('a failed job and a rejected asset keep their reason', () => {
  const jobs = new JobList();
  jobs.onJob({ kind: 'status', jobId: 'j-2', roomId: 'r', boxId: 2, state: 'failed', source: 'local', error: 'generator_timeout' });
  assert.equal(jobs.byJob('j-2').error, 'generator_timeout');
  jobs.onJob({ kind: 'ack', jobId: 'j-3', boxId: 3, ok: false, reason: 'hash_mismatch' });
  const row = jobs.findBox(3);
  assert.equal(row.state, 'rejected');
  assert.equal(row.error, 'hash_mismatch');
  assert.equal(jobs.summary().failed, 2);
});

test('the phone\'s asset events fill in what no job frame says', () => {
  const jobs = new JobList();
  jobs.setRoom(outline, '');
  jobs.onEvent({ ev: 'asset', data: { boxId: 1, step: 'requested', jobId: 'j-9' } });
  assert.equal(jobs.findBox(1).state, 'requested');
  jobs.onEvent({ ev: 'asset', data: { boxId: 1, step: 'builtin', reason: 'forge_down' } });
  assert.equal(jobs.findBox(1).state, 'built-in');
  assert.equal(jobs.summary().fallback, 1);
  jobs.onEvent({ ev: 'asset', data: { boxId: 4, step: 'placed', ms: 180 } });
  assert.equal(jobs.findBox(4).state, 'placed');
  assert.equal(jobs.onEvent({ ev: 'bump.level', data: { level: 1 } }), null);
  assert.equal(jobs.onEvent({ ev: 'asset', data: { boxId: 4, step: 'nonsense' } }), null);
});

test('a frame with neither a box nor a known job changes nothing', () => {
  const jobs = new JobList();
  assert.equal(jobs.onJob({ kind: 'status', jobId: 'j-x', state: 'queued' }), null);
  assert.equal(jobs.onJob(null), null);
  assert.equal(jobs.list().length, 0);
});

test('phases', () => {
  assert.equal(phaseOf('placeholder'), 'waiting');
  assert.equal(phaseOf('matting'), 'working');
  assert.equal(phaseOf('ready'), 'done');
  assert.equal(phaseOf('built-in'), 'fallback');
  assert.equal(phaseOf('rejected'), 'failed');
});
