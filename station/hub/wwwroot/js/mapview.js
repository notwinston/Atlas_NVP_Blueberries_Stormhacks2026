// The Station's flat twin of the room (Master Plan section 7.3.1, lane D contract 5.9): the outline, the free
// rectangle, the object boxes, the walker with its trail, and the Bump Guard level. Flat floor plan only: no
// curved space can be entered in this build.

import { prepare } from './charts.js';
import { corners, fitView, boundsOf, headingVector, rectCorners, toScreen } from './mapmath.js';
import { categoryName } from './format.js';
import { phaseOf } from './jobs.js';

function ink(name, fallback) {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fallback;
}

function path(g, view, points) {
  g.beginPath();
  for (let i = 0; i < points.length; i++) {
    const [px, py] = toScreen(view, points[i][0], points[i][1]);
    if (i === 0) g.moveTo(px, py);
    else g.lineTo(px, py);
  }
  g.closePath();
}

const BUMP_INK = ['--ok', '--warn', '--strong', '--danger'];

/**
 * Draws the map. model: {room, trail, walker: {x, z, yawDeg} | null, bump, cue, obstacle, boxState: Map(boxId -> state)}
 */
export function drawMap(canvas, model) {
  const { g, width, height } = prepare(canvas);
  const view = fitView(boundsOf(model.room), width, height, 34);
  const room = model.room;

  // A one-metre grid, so distances can be read off the map.
  g.lineWidth = 1;
  g.strokeStyle = ink('--grid', '#222932');
  const x0 = Math.floor(view.centreX - 0.5 * width / view.scale);
  const x1 = Math.ceil(view.centreX + 0.5 * width / view.scale);
  const z0 = Math.floor(view.centreZ - 0.5 * height / view.scale);
  const z1 = Math.ceil(view.centreZ + 0.5 * height / view.scale);
  g.beginPath();
  for (let x = x0; x <= x1; x++) {
    const px = Math.round(toScreen(view, x, 0)[0]) + 0.5;
    g.moveTo(px, 0);
    g.lineTo(px, height);
  }
  for (let z = z0; z <= z1; z++) {
    const py = Math.round(toScreen(view, 0, z)[1]) + 0.5;
    g.moveTo(0, py);
    g.lineTo(width, py);
  }
  g.stroke();

  if (!room) {
    g.fillStyle = ink('--muted', '#98a2b0');
    g.font = '14px system-ui, sans-serif';
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.fillText('No room outline yet: the phone sends it once a room is confirmed.', width / 2, height / 2);
  } else {
    if (room.outline && room.outline.length >= 3) {
      path(g, view, room.outline);
      g.fillStyle = ink('--panel-2', '#1e242c');
      g.fill();
      g.strokeStyle = ink('--text', '#e6e9ee');
      g.lineWidth = 2;
      g.stroke();
    }
    const rect = rectCorners(room.rect);
    if (rect) {
      path(g, view, rect);
      g.save();
      g.setLineDash([6, 5]);
      g.strokeStyle = ink('--accent', '#5ab0ff');
      g.lineWidth = 1.25;
      g.stroke();
      g.restore();
    }
    g.font = '11px system-ui, sans-serif';
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    for (const box of room.boxes || []) {
      const phase = phaseOf((model.boxState && model.boxState.get(box.id)) || 'placeholder');
      const colour = phase === 'done' ? ink('--ok', '#4cc38a') : phase === 'failed' ? ink('--danger', '#e5484d')
        : phase === 'working' ? ink('--warn', '#f0b429') : phase === 'fallback' ? ink('--violet', '#b392f0') : ink('--muted', '#98a2b0');
      path(g, view, corners(box.cx, box.cz, box.w, box.d, box.yaw));
      g.fillStyle = ink('--panel-3', '#2a323c');
      g.fill();
      g.strokeStyle = colour;
      g.lineWidth = 1.5;
      g.stroke();
      const [bx, by] = toScreen(view, box.cx, box.cz);
      g.fillStyle = ink('--text', '#e6e9ee');
      g.fillText(box.id + ' ' + categoryName(box.cat), bx, by);
    }
  }

  // Trail: older points fainter, in four bands so the path costs four strokes.
  const points = model.trail ? model.trail.points : [];
  if (points.length > 1) {
    g.lineWidth = 2;
    g.lineJoin = 'round';
    g.strokeStyle = ink('--accent', '#5ab0ff');
    const bands = 4;
    for (let band = 0; band < bands; band++) {
      const from = Math.floor(points.length * band / bands);
      const to = Math.min(points.length - 1, Math.floor(points.length * (band + 1) / bands));
      if (to <= from) continue;
      g.globalAlpha = 0.2 + 0.6 * (band + 1) / bands;
      g.beginPath();
      for (let i = from; i <= to; i++) {
        const [px, py] = toScreen(view, points[i][0], points[i][1]);
        if (i === from) g.moveTo(px, py);
        else g.lineTo(px, py);
      }
      g.stroke();
    }
    g.globalAlpha = 1;
  }

  const walker = model.walker;
  if (walker) {
    const [wx, wy] = toScreen(view, walker.x, walker.z);
    const level = model.bump || 0;
    const levelInk = ink(BUMP_INK[level] || '--ok', '#4cc38a');

    // Bump Guard: a ring in the level's colour, and a pointer to the nearest wall or object while it warns.
    if (level > 0 || model.cue) {
      g.strokeStyle = levelInk;
      g.lineWidth = 3;
      g.beginPath();
      g.arc(wx, wy, 18, 0, 2 * Math.PI);
      g.stroke();
      const o = model.obstacle;
      if (o && level < 3) {
        const [ox, oy] = toScreen(view, o.x, o.z);
        g.lineWidth = 2;
        g.beginPath();
        g.moveTo(wx, wy);
        g.lineTo(ox, oy);
        g.stroke();
        g.fillStyle = levelInk;
        g.beginPath();
        g.arc(ox, oy, 4, 0, 2 * Math.PI);
        g.fill();
      }
    }

    // The walker: an arrow along the head's forward axis.
    const f = headingVector(walker.yawDeg);
    const fx = f[0];
    const fy = -f[1];   // +Z is up on the map, and canvas y runs down
    g.fillStyle = ink('--text', '#e6e9ee');
    g.strokeStyle = ink('--bg', '#0f1216');
    g.lineWidth = 1.5;
    g.beginPath();
    g.moveTo(wx + fx * 13, wy + fy * 13);
    g.lineTo(wx - fx * 7 - fy * 7, wy - fy * 7 + fx * 7);
    g.lineTo(wx - fx * 3, wy - fy * 3);
    g.lineTo(wx - fx * 7 + fy * 7, wy - fy * 7 - fx * 7);
    g.closePath();
    g.fill();
    g.stroke();
  }

  // Scale bar: one metre.
  const bar = view.scale;
  g.strokeStyle = ink('--muted', '#98a2b0');
  g.fillStyle = ink('--muted', '#98a2b0');
  g.lineWidth = 2;
  g.beginPath();
  g.moveTo(12, height - 12);
  g.lineTo(12 + bar, height - 12);
  g.stroke();
  g.font = '11px system-ui, sans-serif';
  g.textAlign = 'left';
  g.textBaseline = 'bottom';
  g.fillText('1 m', 12, height - 16);
}
