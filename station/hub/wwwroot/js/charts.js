// The dashboard's charts (Master Plan section 7.3.3), drawn on canvas with no library: the Station's router
// has no internet. Colours come from the page's custom properties so the charts follow the stylesheet.

import { niceCeil } from './telemetry.js';
import { fixed } from './format.js';

const WINDOW_S = 900;   // fifteen minutes: the soak of test L11

function ink(name, fallback) {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fallback;
}

/** Sizes a canvas to its box at the screen's pixel ratio and returns a context scaled to CSS pixels. */
export function prepare(canvas) {
  const ratio = window.devicePixelRatio || 1;
  const width = Math.max(50, Math.floor(canvas.clientWidth));
  const height = Math.max(30, Math.floor(canvas.clientHeight));
  if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
  }
  const g = canvas.getContext('2d');
  g.setTransform(ratio, 0, 0, ratio, 0, 0);
  g.clearRect(0, 0, width, height);
  return { g, width, height };
}

// The window shown: the newest fifteen minutes, or from the first row while the session is younger.
function timeAxis(seconds) {
  const end = seconds.length > 0 ? seconds[seconds.length - 1] : 0;
  const start = Math.max(seconds.length > 0 ? seconds[0] : 0, end - WINDOW_S);
  return { start, end: Math.max(end, start + 60) };
}

function drawGaps(g, gaps, axis, x, top, bottom) {
  if (!gaps || gaps.length === 0) return;
  g.save();
  g.fillStyle = ink('--gap', 'rgba(229,72,77,0.18)');
  g.strokeStyle = ink('--danger', '#e5484d');
  g.setLineDash([3, 3]);
  g.lineWidth = 1;
  g.font = '11px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'top';
  for (const gap of gaps) {
    if (gap.toS < axis.start) continue;
    const a = x(Math.max(gap.fromS, axis.start));
    const b = x(gap.toS);
    g.fillRect(a, top, Math.max(b - a, 2), bottom - top);
    g.beginPath();
    g.moveTo(a + 0.5, top);
    g.lineTo(a + 0.5, bottom);
    g.moveTo(b + 0.5, top);
    g.lineTo(b + 0.5, bottom);
    g.stroke();
    if (b - a > 34) {
      g.fillStyle = ink('--danger', '#e5484d');
      g.fillText('gap ' + fixed(gap.seconds, 1) + ' s', 0.5 * (a + b), top + 2);
      g.fillStyle = ink('--gap', 'rgba(229,72,77,0.18)');
    }
  }
  g.restore();
}

function drawTimeLabels(g, axis, x, y) {
  g.fillStyle = ink('--muted', '#98a2b0');
  g.font = '11px system-ui, sans-serif';
  g.textBaseline = 'top';
  const span = axis.end - axis.start;
  const step = span > 600 ? 300 : span > 240 ? 120 : span > 90 ? 60 : 15;
  for (let ago = 0; ago <= span; ago += step) {
    const px = x(axis.end - ago);
    g.textAlign = ago === 0 ? 'right' : 'center';
    g.fillText(ago === 0 ? 'now' : '−' + (ago >= 60 ? ago / 60 + ' min' : ago + ' s'), px, y);
  }
}

/**
 * A line chart of one or more columns over time.
 * options: {seconds, series: [{values, color}], gaps, floor, limit: {value, label}, step}
 * `floor` is the least upper bound of the axis; `limit` draws a dashed reference line; `step` draws a
 * staircase (for the quality tier). A line is broken at every gap, so a hole is never drawn as data.
 */
export function drawLines(canvas, options) {
  const { g, width, height } = prepare(canvas);
  const left = 38;
  const right = 8;
  const top = 8;
  const bottom = height - 18;
  const seconds = options.seconds;
  const axis = timeAxis(seconds);
  const x = (s) => left + (s - axis.start) / (axis.end - axis.start) * (width - left - right);

  let peak = 0;
  for (const s of options.series) {
    for (let i = 0; i < s.values.length; i++) {
      if (seconds[i] >= axis.start && Number.isFinite(s.values[i]) && s.values[i] > peak) peak = s.values[i];
    }
  }
  if (options.limit && options.limit.value > peak) peak = options.limit.value;
  const yMax = options.yMax || niceCeil(peak * 1.05, options.floor || 1);
  const y = (v) => bottom - Math.min(v, yMax) / yMax * (bottom - top);

  // Grid: four divisions, hairlines, labels on the left.
  g.lineWidth = 1;
  g.font = '11px system-ui, sans-serif';
  g.textAlign = 'right';
  g.textBaseline = 'middle';
  for (let i = 0; i <= 4; i++) {
    const v = yMax * i / 4;
    const py = Math.round(y(v)) + 0.5;
    g.strokeStyle = ink(i === 0 ? '--border-strong' : '--grid', '#2a323c');
    g.beginPath();
    g.moveTo(left, py);
    g.lineTo(width - right, py);
    g.stroke();
    g.fillStyle = ink('--muted', '#98a2b0');
    g.fillText(Number.isInteger(v) ? String(v) : fixed(v, v < 10 ? 1 : 0), left - 6, py);
  }

  drawGaps(g, options.gaps, axis, x, top, bottom);

  if (options.limit) {
    const py = Math.round(y(options.limit.value)) + 0.5;
    g.save();
    g.setLineDash([5, 4]);
    g.strokeStyle = ink('--muted', '#98a2b0');
    g.beginPath();
    g.moveTo(left, py);
    g.lineTo(width - right, py);
    g.stroke();
    g.restore();
    g.fillStyle = ink('--muted', '#98a2b0');
    g.textAlign = 'right';
    g.textBaseline = 'bottom';
    g.fillText(options.limit.label, width - right - 2, py - 2);
  }

  const breaks = new Set();
  for (const gap of options.gaps || []) breaks.add(gap.index);

  g.lineJoin = 'round';
  g.lineWidth = 1.75;
  for (const s of options.series) {
    g.strokeStyle = s.color;
    g.beginPath();
    let pen = false;
    let lastY = 0;
    let points = 0;
    for (let i = 0; i < s.values.length; i++) {
      const v = s.values[i];
      if (seconds[i] < axis.start || !Number.isFinite(v)) {
        pen = false;
        continue;
      }
      if (breaks.has(i)) pen = false;
      const px = x(seconds[i]);
      const py = y(v);
      if (!pen) g.moveTo(px, py);
      else if (options.step) {
        g.lineTo(px, lastY);
        g.lineTo(px, py);
      } else g.lineTo(px, py);
      pen = true;
      lastY = py;
      points++;
    }
    g.stroke();
    // A single row is a point, not a line: draw it so the first second of a session is visible.
    if (points === 1) {
      const i = s.values.length - 1;
      g.fillStyle = s.color;
      g.beginPath();
      g.arc(x(seconds[i]), y(s.values[i]), 2.5, 0, 2 * Math.PI);
      g.fill();
    }
  }

  drawTimeLabels(g, axis, x, bottom + 4);
}

/**
 * The thermal timeline: one coloured segment per row, the state's colour from `colors`, with the same time
 * axis and gap marks as the line charts.
 */
export function drawTimeline(canvas, options) {
  const { g, width, height } = prepare(canvas);
  const left = 38;
  const right = 8;
  const top = 6;
  const bottom = height - 18;
  const seconds = options.seconds;
  const axis = timeAxis(seconds);
  const x = (s) => left + (s - axis.start) / (axis.end - axis.start) * (width - left - right);

  g.fillStyle = ink('--panel-2', '#1e242c');
  g.fillRect(left, top, width - left - right, bottom - top);

  const breaks = new Set();
  for (const gap of options.gaps || []) breaks.add(gap.index);
  for (let i = 0; i < seconds.length; i++) {
    if (seconds[i] < axis.start) continue;
    // A row covers the second that ended at its time; after a gap it covers one second only.
    const from = i > 0 && !breaks.has(i) ? Math.max(seconds[i - 1], axis.start) : Math.max(seconds[i] - 1, axis.start);
    const a = x(from);
    const b = x(seconds[i]);
    g.fillStyle = options.colors[options.values[i]] || ink('--muted', '#98a2b0');
    g.fillRect(a, top, Math.max(b - a, 1), bottom - top);
  }
  drawGaps(g, options.gaps, axis, x, top, bottom);
  drawTimeLabels(g, axis, x, bottom + 4);
}
