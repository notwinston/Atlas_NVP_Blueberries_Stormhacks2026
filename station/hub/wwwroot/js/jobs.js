// The Forge's work as the Console sees it (Master Plan section 7.4, lane D contract 2.5 and 4.3): one row per
// object box, built from the Hub's `job` frames (JobStatus, AssetReady, AssetAck) and the phone's `asset` and
// `object.mesh` events. No DOM here.

import { categoryName } from './format.js';

const ACTIVE = new Set(['queued', 'matting', 'generating', 'cleaning']);

/** The objects of the room in force and what became of each. */
export class JobList {
  constructor() {
    this.rows = new Map();   // "roomId/boxId" -> row
  }

  clear() {
    this.rows.clear();
  }

  row(roomId, boxId) {
    const key = (roomId || '') + '/' + boxId;
    let row = this.rows.get(key);
    if (!row) {
      row = {
        key, roomId: roomId || '', boxId, category: '', jobId: '', state: 'placeholder', source: '', error: '',
        queuePos: null, tris: null, bytes: null, loadMs: null, placed: false, updated: 0
      };
      this.rows.set(key, row);
    }
    return row;
  }

  /** The row a job id belongs to, when a frame names the job and not its box. */
  byJob(jobId) {
    if (!jobId) return null;
    for (const row of this.rows.values()) if (row.jobId === jobId) return row;
    return null;
  }

  /** The room's boxes from a RoomOutline frame: every box gets a row, as a placeholder until a job says more. */
  setRoom(outline, roomId) {
    for (const box of (outline && outline.boxes) || []) {
      const row = this.row(roomId, box.id);
      row.category = categoryName(box.cat);
    }
  }

  /** A `job` frame from the Hub. Returns the row it changed, or null. */
  onJob(frame, nowMs = 0) {
    if (!frame) return null;
    // AssetAck names its job and box but no room: the job's own row is found first.
    let row = this.byJob(frame.jobId);
    if (!row && Number.isInteger(frame.boxId)) {
      row = frame.roomId ? this.adopt(frame.roomId, frame.boxId) : this.findBox(frame.boxId) || this.row('', frame.boxId);
    }
    if (!row) return null;
    if (frame.jobId) row.jobId = frame.jobId;
    row.updated = nowMs;
    if (frame.kind === 'ready') {
      row.state = 'ready';
      row.error = '';
      if (Number.isFinite(frame.tris)) row.tris = frame.tris;
      if (Number.isFinite(frame.bytes)) row.bytes = frame.bytes;
    } else if (frame.kind === 'ack') {
      if (frame.ok === true) {
        row.state = 'placed';
        row.placed = true;
        row.error = '';
        if (Number.isFinite(frame.loadMs)) row.loadMs = frame.loadMs;
      } else {
        row.state = 'rejected';
        row.error = frame.reason || 'rejected';
      }
    } else {
      if (typeof frame.state === 'string' && frame.state !== '') row.state = frame.state;
      if (typeof frame.source === 'string') row.source = frame.source;
      row.error = frame.state === 'failed' ? frame.error || 'failed' : '';
      row.queuePos = Number.isFinite(frame.queuePos) ? frame.queuePos : null;
    }
    return row;
  }

  // A job names its room; rows made from the outline before the room id was known are taken over.
  adopt(roomId, boxId) {
    const anonymous = this.rows.get('/' + boxId);
    if (roomId && anonymous) {
      this.rows.delete(anonymous.key);
      anonymous.roomId = roomId;
      anonymous.key = roomId + '/' + boxId;
      this.rows.set(anonymous.key, anonymous);
      return anonymous;
    }
    return this.row(roomId, boxId);
  }

  /** An `asset` or `object.mesh` event of the phone. Returns the row it changed, or null. */
  onEvent(event, nowMs = 0) {
    if (!event || !event.data) return null;
    const d = event.data;
    if (event.ev === 'asset' && Number.isInteger(d.boxId)) {
      const row = this.byJob(d.jobId) || this.findBox(d.boxId) || this.row('', d.boxId);
      row.updated = nowMs;
      if (d.jobId) row.jobId = d.jobId;
      switch (d.step) {
        case 'upload': case 'requested': if (!ACTIVE.has(row.state)) row.state = 'requested'; break;
        case 'cached': row.source = 'cache'; break;
        case 'ready': if (row.state !== 'placed') row.state = 'ready'; break;
        case 'placed': row.state = 'placed'; row.placed = true; if (Number.isFinite(d.ms)) row.loadMs = d.ms; break;
        case 'builtin': row.state = 'built-in'; row.source = 'built-in'; break;
        case 'failed': row.state = 'failed'; row.error = d.reason || 'failed'; break;
        case 'rejected': row.state = 'rejected'; row.error = d.reason || 'rejected'; break;
        default: return null;
      }
      return row;
    }
    return null;
  }

  findBox(boxId) {
    for (const row of this.rows.values()) if (row.boxId === boxId) return row;
    return null;
  }

  /** Rows in box order. */
  list() {
    return Array.from(this.rows.values()).sort((a, b) => a.boxId - b.boxId || a.roomId.localeCompare(b.roomId));
  }

  /** How many objects are in each broad state: {waiting, working, done, fallback, failed}. */
  summary() {
    const s = { waiting: 0, working: 0, done: 0, fallback: 0, failed: 0 };
    for (const row of this.rows.values()) s[phaseOf(row.state)]++;
    return s;
  }
}

/** The broad phase of a job state, for the row's colour and the map's box marks. */
export function phaseOf(state) {
  if (state === 'placed' || state === 'ready') return 'done';
  if (state === 'built-in') return 'fallback';
  if (state === 'failed' || state === 'rejected') return 'failed';
  if (state === 'placeholder') return 'waiting';
  return 'working';
}
