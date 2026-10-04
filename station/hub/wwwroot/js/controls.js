// From the phone's manifest to the tuner's controls (Master Plan section 7.3.2, lane D contract 3.5).
// The Console builds itself from the manifest: a new parameter needs no Console code. No DOM here.

import { clampParam, decimalsFor, formatParam, stepFor } from './format.js';

const GROUP_TITLES = {
  app: 'App', home: 'S0 Home', glued: 'S1 Glued', curved: 'S2 Curved Walk', cells: 'S3 Aligned Cells',
  planet: 'S4 Small Planet', portal: 'S5 Impossible Rooms', slicer: 'S6 Slicer', net: 'S7 Network', render: 'Render', pose: 'Pose',
  scan: 'Scan', link: 'Link', forge: 'Forge', hud: 'HUD'
};

/** The heading of a parameter group: "glued" is shown as "S1 Glued". An unknown key is shown capitalised. */
export function groupTitle(group) {
  if (!group) return 'Other';
  return GROUP_TITLES[group] || group.charAt(0).toUpperCase() + group.slice(1);
}

/** The lock is the one switch a ParamSet cannot set (contract 1.5): its toggle sends the lock command. */
export const LOCK_PARAM = 'demoLock';

/**
 * One manifest entry as a control: the widget for its kind, its step and precision, and the options of a
 * selector. Entries with a broken range are held to a single value rather than dropped.
 */
export function controlFor(entry) {
  const kind = entry.kind === 'int' || entry.kind === 'bool' || entry.kind === 'enum' ? entry.kind : 'float';
  let min = Number(entry.min);
  let max = Number(entry.max);
  if (!Number.isFinite(min)) min = 0;
  if (!Number.isFinite(max) || max < min) max = min;
  if (kind === 'bool') {
    min = 0;
    max = 1;
  }
  const names = Array.isArray(entry.names) ? entry.names.slice() : null;
  if (kind === 'enum' && names && names.length > 0) {
    min = 0;
    max = names.length - 1;
  }
  const step = stepFor(kind, min, max);
  const decimals = kind === 'float' ? decimalsFor(step) : 0;
  const control = {
    id: entry.id,
    name: String(entry.name || ''),
    group: String(entry.group || ''),
    kind,
    min,
    max,
    def: clampOr(kind, min, max, entry.def, min),
    val: clampOr(kind, min, max, entry.val, min),
    onRelease: entry.onRelease === true,
    active: entry.active !== false,
    names,
    step,
    decimals,
    widget: kind === 'bool' ? 'toggle' : kind === 'enum' ? 'select' : 'slider',
    options: null,
    viaLock: entry.name === LOCK_PARAM
  };
  if (kind === 'enum') {
    control.options = [];
    for (let i = min; i <= max; i++) control.options.push({ value: i, label: formatParam('enum', i, names) });
  }
  return control;
}

function clampOr(kind, min, max, raw, fallback) {
  const v = clampParam(kind, min, max, typeof raw === 'number' || typeof raw === 'boolean' ? raw : Number(raw));
  return v === null ? fallback : v;
}

/**
 * The manifest as tuner groups, in manifest order. A group is active while any of its parameters is: the
 * tuner folds and dims the others (their values can still be set).
 */
export function buildGroups(params) {
  const groups = [];
  const byKey = new Map();
  for (const entry of params || []) {
    if (!entry || !entry.name) continue;
    const control = controlFor(entry);
    let group = byKey.get(control.group);
    if (!group) {
      group = { key: control.group, title: groupTitle(control.group), active: false, controls: [] };
      byKey.set(control.group, group);
      groups.push(group);
    }
    group.controls.push(control);
    if (control.active) group.active = true;
  }
  return groups;
}

/** The value a control will hold for what the operator typed or dragged; null when the text is no number. */
export function requestValue(control, raw) {
  return clampParam(control.kind, control.min, control.max, raw);
}

/** The value of a control as text. */
export function displayValue(control, value) {
  return formatParam(control.kind, value === undefined ? control.val : value, control.names, control.decimals);
}

/**
 * The applied values an event from the phone carries. The link's echo is `param` with {id, name, value}; the
 * app's event log writes `param.set` with {values: {name or id: value}}. Anything else gives an empty list.
 */
export function readEcho(event) {
  if (!event || !event.data) return [];
  const data = event.data;
  if (event.ev === 'param' && typeof data.value === 'number') {
    return [{ name: typeof data.name === 'string' ? data.name : null, id: Number.isInteger(data.id) ? data.id : null, value: data.value }];
  }
  if (event.ev === 'param.set' && data.values && typeof data.values === 'object') {
    const list = [];
    for (const key of Object.keys(data.values)) {
      const value = data.values[key];
      if (typeof value !== 'number') continue;
      const numeric = /^\d+$/.test(key);
      list.push({ name: numeric ? null : key, id: numeric ? Number(key) : null, value });
    }
    return list;
  }
  return [];
}

/** Finds the control an echo names, by name first and by wire id second. */
export function findControl(groups, echo) {
  for (const group of groups) {
    for (const control of group.controls) {
      if (echo.name !== null ? control.name === echo.name : control.id === echo.id) return control;
    }
  }
  return null;
}

/** The current values of every control as a preset body, {"values": {name: value}}. */
export function presetOf(groups) {
  const values = {};
  for (const group of groups) for (const control of group.controls) if (!control.viaLock) values[control.name] = control.val;
  return { values };
}

/** A preset name the Hub accepts: 1 to 40 letters, digits, '-' or '_'. */
export function isPresetName(name) {
  return typeof name === 'string' && /^[A-Za-z0-9_-]{1,40}$/.test(name);
}

/**
 * Holds a dragged slider to 30 sends per second (contract 3.5). offer() answers whether to send now; the last
 * value offered and not sent is kept, and take() hands it over when the interval has passed or on release.
 */
export class SendThrottle {
  constructor(intervalMs = 1000 / 30) {
    this.intervalMs = intervalMs;
    this.lastSentMs = -Infinity;
    this.pending = null;
  }

  offer(value, nowMs) {
    if (nowMs - this.lastSentMs >= this.intervalMs) {
      this.lastSentMs = nowMs;
      this.pending = null;
      return true;
    }
    this.pending = value;
    return false;
  }

  /** The value still owed, or null. With force (the slider was released) the interval is not waited for. */
  take(nowMs, force = false) {
    if (this.pending === null) return null;
    if (!force && nowMs - this.lastSentMs < this.intervalMs) return null;
    const value = this.pending;
    this.pending = null;
    this.lastSentMs = nowMs;
    return value;
  }
}
