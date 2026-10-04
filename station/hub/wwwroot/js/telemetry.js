// The dashboard's store of Telemetry rows (Master Plan section 7.3.3): fifteen minutes at one row per second,
// in seq order with no duplicates, with the gaps the acceptance test L11 asks about. No DOM here.

export const CSV_HEADER = 't_us,seq,sceneId,spaceId,tier,fps,p50,p95,p99,max,over17_2,over33_8,cpuMainP95,cpuRenderP95,' +
  'gpuP95,renderScale,drawCalls,setPass,trisK,memMB,visibleTiles,copies,rttMs,lossPermille,cueCount,gcAllocB,thermal,' +
  'battery,tracking,msaa,stateHz,bumpMax,assetsPlaced,assetsPending';

export const FIELDS = CSV_HEADER.split(',');

/** A gap between two rows is marked when it is longer than this (L11: "no gap > 2 s"). */
export const GAP_US = 2000000;

/**
 * Nearest-rank percentile of a list of numbers, p from 0 to 1: the same rule the Hub uses for its latency
 * window. NaN for an empty list. The list is not changed.
 */
export function percentile(values, p) {
  const sorted = [];
  for (const v of values) if (Number.isFinite(v)) sorted.push(v);
  if (sorted.length === 0) return NaN;
  sorted.sort((a, b) => a - b);
  let rank = Math.ceil(p * sorted.length) - 1;
  if (rank < 0) rank = 0;
  if (rank >= sorted.length) rank = sorted.length - 1;
  return sorted[rank];
}

/** perf.csv as rows of numbers keyed by column name. Lines that are not rows of this header are left out. */
export function parseCsv(text) {
  const rows = [];
  if (typeof text !== 'string') return rows;
  const lines = text.split('\n');
  if (lines.length === 0) return rows;
  const header = lines[0].trim().split(',');
  if (header[0] !== 't_us' || header[1] !== 'seq') return rows;
  for (let i = 1; i < lines.length; i++) {
    const line = lines[i].trim();
    if (line === '') continue;
    const cells = line.split(',');
    if (cells.length !== header.length) continue;
    const row = {};
    let good = true;
    for (let c = 0; c < header.length; c++) {
      const n = Number(cells[c]);
      if (!Number.isFinite(n)) {
        good = false;
        break;
      }
      row[header[c]] = n;
    }
    if (good) rows.push(row);
  }
  return rows;
}

/** The newest rows of a session, oldest first. */
export class TelemetryBuffer {
  constructor(capacity = 900) {
    this.capacity = capacity;
    this.rows = [];
  }

  get length() {
    return this.rows.length;
  }

  get last() {
    return this.rows.length > 0 ? this.rows[this.rows.length - 1] : null;
  }

  clear() {
    this.rows.length = 0;
  }

  /**
   * Adds a row in seq order. A row already held is ignored (the phone sends again what it could not see
   * arrive); a row from the phone's backlog is put where it belongs. Returns true when the row was new.
   */
  push(row) {
    if (!row || !Number.isFinite(row.seq) || !Number.isFinite(row.t_us)) return false;
    const rows = this.rows;
    let i = rows.length;
    while (i > 0 && rows[i - 1].seq > row.seq) i--;
    if (i > 0 && rows[i - 1].seq === row.seq) return false;
    // A seq that starts again far below the newest is a new run of the phone, not a backlog row.
    if (i === 0 && rows.length > 0 && rows[0].seq - row.seq > this.capacity) {
      rows.length = 0;
      rows.push(row);
      return true;
    }
    if (i === 0 && rows.length >= this.capacity) return false;   // older than everything kept
    rows.splice(i, 0, row);
    if (rows.length > this.capacity) rows.splice(0, rows.length - this.capacity);
    return true;
  }

  pushAll(list) {
    let added = 0;
    for (const row of list) if (this.push(row)) added++;
    return added;
  }

  /** One column as an array, oldest first. */
  series(field) {
    const out = new Array(this.rows.length);
    for (let i = 0; i < out.length; i++) out[i] = this.rows[i][field];
    return out;
  }

  /** Row times in seconds from the first row kept. */
  seconds() {
    const out = new Array(this.rows.length);
    const t0 = this.rows.length > 0 ? this.rows[0].t_us : 0;
    for (let i = 0; i < out.length; i++) out[i] = (this.rows[i].t_us - t0) / 1e6;
    return out;
  }

  /** The gaps longer than thresholdUs: {index, fromS, toS, seconds}, index being the row after the gap. */
  gaps(thresholdUs = GAP_US) {
    const out = [];
    const rows = this.rows;
    if (rows.length === 0) return out;
    const t0 = rows[0].t_us;
    for (let i = 1; i < rows.length; i++) {
      const dt = rows[i].t_us - rows[i - 1].t_us;
      if (dt > thresholdUs) {
        out.push({ index: i, fromS: (rows[i - 1].t_us - t0) / 1e6, toS: (rows[i].t_us - t0) / 1e6, seconds: dt / 1e6 });
      }
    }
    return out;
  }

  /** Minimum, median, 95th percentile and maximum of one column over the rows kept. */
  stats(field) {
    const values = this.series(field);
    let min = Infinity;
    let max = -Infinity;
    for (const v of values) {
      if (!Number.isFinite(v)) continue;
      if (v < min) min = v;
      if (v > max) max = v;
    }
    if (min === Infinity) return { min: NaN, p50: NaN, p95: NaN, max: NaN };
    return { min, p50: percentile(values, 0.5), p95: percentile(values, 0.95), max };
  }
}

/**
 * A "nice" upper bound for a chart axis: the smallest of 1, 2, 2.5, 5 or 10 times a power of ten that is at
 * least the value, and at least floor.
 */
export function niceCeil(value, floor = 1) {
  const v = Math.max(Number.isFinite(value) ? value : 0, floor);
  const pow = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 2, 2.5, 5, 10]) {
    if (m * pow >= v - 1e-9) return Number((m * pow).toPrecision(6));
  }
  return 10 * pow;
}
