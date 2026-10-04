// Value formatting and clamping for the Console (Master Plan section 7.3.2). No DOM: tested with node --test.
// The phone clamps too and is the authority; the Console clamps so that what it shows is what will be applied.

/** Clamps a number to [min, max]. NaN gives min. */
export function clamp(value, min, max) {
  if (Number.isNaN(value)) return min;
  return value < min ? min : value > max ? max : value;
}

/** Rounds half away from zero, as the registry does for an int parameter. */
export function roundHalfAway(value) {
  const r = value < 0 ? -Math.round(-value) : Math.round(value);
  return r === 0 ? 0 : r;   // never negative zero
}

/**
 * A slider step for a range: 1 for integers, else the largest of 1, 2 or 5 times a power of ten that gives
 * at least 200 positions across the range.
 */
export function stepFor(kind, min, max) {
  if (kind !== 'float') return 1;
  const span = max - min;
  if (!(span > 0)) return 1;
  const raw = span / 200;
  const pow = Math.pow(10, Math.floor(Math.log10(raw)));
  const lead = raw / pow;
  const nice = lead >= 5 ? 5 : lead >= 2 ? 2 : 1;
  return Number((nice * pow).toPrecision(6));
}

/** Decimals needed to show a step without loss, 0 to 6. */
export function decimalsFor(step) {
  if (!(step > 0) || step >= 1) return 0;
  let d = 0;
  let s = step;
  while (d < 6 && Math.abs(s - Math.round(s)) > 1e-6) {
    s *= 10;
    d++;
  }
  return d;
}

/**
 * The value a parameter will hold after a request: text or number in, the stored float out. Mirrors the
 * registry's clamp (lane D contract 1.1): bool is 0 or 1, int and enum are rounded, all are held in range.
 * Returns null for text that is not a number, so the caller keeps the current value.
 */
export function clampParam(kind, min, max, raw) {
  let value;
  if (typeof raw === 'boolean') value = raw ? 1 : 0;
  else if (typeof raw === 'number') value = raw;
  else {
    const text = String(raw).trim().replace(',', '.');
    if (text === '') return null;
    value = Number(text);
  }
  if (!Number.isFinite(value)) return null;
  if (kind === 'bool') return value >= 0.5 ? 1 : 0;
  if (kind === 'int' || kind === 'enum') value = roundHalfAway(value);
  return clamp(value, min, max);
}

/** A number with a fixed count of decimals and no negative zero. */
export function fixed(value, decimals) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '–';
  const text = value.toFixed(decimals);
  return Number(text) === 0 ? (0).toFixed(decimals) : text;
}

/** A parameter value as the tuner shows it: "on"/"off", the option's name, or a number at the step's precision. */
export function formatParam(kind, value, names, decimals) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '–';
  if (kind === 'bool') return value >= 0.5 ? 'on' : 'off';
  if (kind === 'enum') {
    const i = roundHalfAway(value);
    return names && i >= 0 && i < names.length ? names[i] : String(i);
  }
  if (kind === 'int') return String(roundHalfAway(value));
  return fixed(value, decimals === undefined ? 3 : decimals);
}

export function formatMs(ms) {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) return '–';
  return (Math.abs(ms) < 10 ? fixed(ms, 1) : fixed(ms, 0)) + ' ms';
}

/** A clock offset: signed milliseconds while small, seconds beyond one second (two clocks with different starts). */
export function formatOffset(ms) {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) return '–';
  const a = Math.abs(ms);
  const text = a < 1000 ? fixed(a, 1) + ' ms' : fixed(a / 1000, 3) + ' s';
  // No sign on a value that rounds to zero.
  return (Number.parseFloat(text) === 0 ? '' : ms > 0 ? '+' : '−') + text;
}

export function formatPercent(p) {
  if (p === null || p === undefined || !Number.isFinite(p)) return '–';
  return (p < 10 ? fixed(p, 1) : fixed(p, 0)) + ' %';
}

/** Seconds as m:ss or h:mm:ss. */
export function formatDuration(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return '–';
  const s = Math.floor(seconds);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const r = s % 60;
  const two = (n) => (n < 10 ? '0' + n : String(n));
  return h > 0 ? h + ':' + two(m) + ':' + two(r) : m + ':' + two(r);
}

/** A count with thin thousands separators: 12 345. */
export function formatCount(n) {
  if (n === null || n === undefined || !Number.isFinite(n)) return '–';
  return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
}

const SPACE_NAMES = ['Home', 'Glued', 'Curved Walk', 'Aligned Cells', 'Small Planet', 'Impossible Rooms', 'Slicer'];

/** "S1 Glued" for 1; "S?" for a number outside 0 to 6. */
export function spaceLabel(id) {
  return Number.isInteger(id) && id >= 0 && id < SPACE_NAMES.length ? 'S' + id + ' ' + SPACE_NAMES[id] : 'S?';
}

export const SPACE_COUNT = SPACE_NAMES.length;

/** Spaces whose geometry is lane B's and which this build cannot enter; the phone answers space.refused. */
export function spaceBuilt(id) {
  return id !== 2 && id !== 3 && id !== 4;
}

const THERMAL = ['nominal', 'fair', 'serious', 'critical'];

/** iOS thermal state 0 to 3 by name. */
export function thermalName(state) {
  return THERMAL[state] || 'unknown';
}

/** What the operator should do at a thermal state (Master Plan section 8.6). Empty when nothing. */
export function thermalAdvice(state) {
  if (state === 1) return 'swap phone at the next change of walker';
  if (state === 2) return 'swap phone';
  if (state === 3) return 'end the session';
  return '';
}

const TIERS = ['0.8 · 4× MSAA', '0.7 · 4× MSAA', '0.6 · 2× MSAA', '0.6 · no MSAA', 'high'];

/** A quality tier as "tier 1 (0.7 · 4× MSAA)"; -1 is "auto". */
export function tierLabel(tier) {
  if (tier === -1) return 'auto';
  return TIERS[tier] ? 'tier ' + tier + ' (' + TIERS[tier] + ')' : 'tier ?';
}

const BUMP = ['clear', 'warn', 'strong', 'tracking lost'];

/** Bump Guard level 0 to 3 by name (Master Plan section 4.10). */
export function bumpName(level) {
  return BUMP[level] || 'unknown';
}

const CATEGORIES = ['unknown', 'couch', 'table', 'bed', 'lamp', 'plant', 'screen', 'storage', 'bathtub', 'chair',
  'dishwasher', 'fireplace', 'oven', 'refrigerator', 'sink', 'stairs', 'stove', 'toilet', 'washerDryer', 'keyboard',
  'mouse', 'laptop', 'other'];

/** An object category as RoomOutline carries it (a number) or as the Forge names it (text). */
export function categoryName(cat) {
  if (typeof cat === 'string') return cat;
  return CATEGORIES[cat] || 'unknown';
}
