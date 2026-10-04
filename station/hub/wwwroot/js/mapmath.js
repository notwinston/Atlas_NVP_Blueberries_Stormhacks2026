// The flat twin of the room on the Station (Master Plan sections 7.3.1 and 4.10, lane D contract 5.9): map
// projection, the walker's trail and the nearest obstacle. Room frame of contract F4: metres, +X right, +Z
// forward, origin at the centre of the free rectangle on the floor. No DOM here; flat geometry only.

/** The four corners of a box or rectangle centred at (cx, cz), w along its local X and d along its local Z, turned by yaw (radians about +Y). */
export function corners(cx, cz, w, d, yaw) {
  const c = Math.cos(yaw || 0);
  const s = Math.sin(yaw || 0);
  const hw = 0.5 * w;
  const hd = 0.5 * d;
  const out = [];
  for (const [lx, lz] of [[-hw, -hd], [hw, -hd], [hw, hd], [-hw, hd]]) {
    // Unity's yaw about +Y turns +Z towards +X.
    out.push([cx + lx * c + lz * s, cz - lx * s + lz * c]);
  }
  return out;
}

/** The free rectangle of an outline frame as corners; null when the frame has none. */
export function rectCorners(rect) {
  if (!rect || !(rect.wf > 0) || !(rect.df > 0)) return null;
  return corners(rect.cx || 0, rect.cz || 0, rect.wf, rect.df, rect.yaw || 0);
}

/** The bounds of everything an outline frame draws, {minX, maxX, minZ, maxZ}; a 4 m square when it is empty. */
export function boundsOf(room) {
  const b = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
  const add = (p) => {
    if (!Number.isFinite(p[0]) || !Number.isFinite(p[1])) return;
    if (p[0] < b.minX) b.minX = p[0];
    if (p[0] > b.maxX) b.maxX = p[0];
    if (p[1] < b.minZ) b.minZ = p[1];
    if (p[1] > b.maxZ) b.maxZ = p[1];
  };
  if (room) {
    for (const p of room.outline || []) add(p);
    const rect = rectCorners(room.rect);
    if (rect) rect.forEach(add);
    for (const box of room.boxes || []) corners(box.cx, box.cz, box.w, box.d, box.yaw).forEach(add);
  }
  if (b.minX === Infinity) return { minX: -2, maxX: 2, minZ: -2, maxZ: 2 };
  return b;
}

/**
 * The view that fits bounds into a canvas of width by height pixels with a margin, at one scale on both axes.
 * +X runs right and +Z runs up, as a floor plan seen from above with the walker's forward at the top.
 */
export function fitView(bounds, width, height, margin = 24) {
  const spanX = Math.max(bounds.maxX - bounds.minX, 0.5);
  const spanZ = Math.max(bounds.maxZ - bounds.minZ, 0.5);
  const scale = Math.max(1, Math.min((width - 2 * margin) / spanX, (height - 2 * margin) / spanZ));
  return {
    scale,
    centreX: 0.5 * (bounds.minX + bounds.maxX),
    centreZ: 0.5 * (bounds.minZ + bounds.maxZ),
    width,
    height
  };
}

/** Room metres to canvas pixels. */
export function toScreen(view, x, z) {
  return [0.5 * view.width + (x - view.centreX) * view.scale, 0.5 * view.height - (z - view.centreZ) * view.scale];
}

/** Canvas pixels back to room metres. */
export function toRoom(view, px, py) {
  return [view.centreX + (px - 0.5 * view.width) / view.scale, view.centreZ - (py - 0.5 * view.height) / view.scale];
}

/** The unit forward vector on the floor for a heading in degrees, atan2(forward.x, forward.z). */
export function headingVector(yawDeg) {
  const a = (yawDeg || 0) * Math.PI / 180;
  return [Math.sin(a), Math.cos(a)];
}

/** The walker's last positions: 600 points (a minute at the digest's 10 Hz), with steps under 1 cm left out. */
export class Trail {
  constructor(capacity = 600, minStep = 0.01) {
    this.capacity = capacity;
    this.minStep = minStep;
    this.points = [];
  }

  add(x, z) {
    if (!Number.isFinite(x) || !Number.isFinite(z)) return false;
    const n = this.points.length;
    if (n > 0) {
      const last = this.points[n - 1];
      if (Math.hypot(x - last[0], z - last[1]) < this.minStep) return false;
    }
    this.points.push([x, z]);
    if (this.points.length > this.capacity) this.points.shift();
    return true;
  }

  clear() {
    this.points.length = 0;
  }
}

function closestOnSegment(px, pz, a, b) {
  const dx = b[0] - a[0];
  const dz = b[1] - a[1];
  const len2 = dx * dx + dz * dz;
  let t = len2 > 0 ? ((px - a[0]) * dx + (pz - a[1]) * dz) / len2 : 0;
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return [a[0] + t * dx, a[1] + t * dz];
}

function closestOnLoop(px, pz, loop, kind, id, best) {
  for (let i = 0; i < loop.length; i++) {
    const q = closestOnSegment(px, pz, loop[i], loop[(i + 1) % loop.length]);
    const d = Math.hypot(q[0] - px, q[1] - pz);
    if (d < best.distance) {
      best.distance = d;
      best.x = q[0];
      best.z = q[1];
      best.kind = kind;
      best.id = id;
    }
  }
}

/**
 * The nearest wall or object box to a floor position, from the outline frame: {distance, x, z, dirX, dirZ,
 * kind: "wall" | "object", id}. State carries the Bump Guard level but not its direction, so the map shows
 * this as the direction of the nearest thing in the scanned room; the level itself is always the phone's.
 * Null when the room has neither an outline nor boxes.
 */
export function nearestObstacle(room, x, z) {
  if (!room) return null;
  const best = { distance: Infinity, x: 0, z: 0, kind: '', id: 0 };
  if (room.outline && room.outline.length >= 2) closestOnLoop(x, z, room.outline, 'wall', 0, best);
  for (const box of room.boxes || []) closestOnLoop(x, z, corners(box.cx, box.cz, box.w, box.d, box.yaw), 'object', box.id, best);
  if (best.distance === Infinity) return null;
  const dx = best.x - x;
  const dz = best.z - z;
  const len = Math.hypot(dx, dz);
  best.dirX = len > 1e-6 ? dx / len : 0;
  best.dirZ = len > 1e-6 ? dz / len : 0;
  return best;
}

/**
 * Where an obstacle lies relative to the walker's heading, as the walker would say it: "ahead", "right",
 * "behind" or "left".
 */
export function relativeSide(yawDeg, dirX, dirZ) {
  const f = headingVector(yawDeg);
  const ahead = f[0] * dirX + f[1] * dirZ;
  const right = f[1] * dirX - f[0] * dirZ;
  if (Math.abs(ahead) >= Math.abs(right)) return ahead >= 0 ? 'ahead' : 'behind';
  return right >= 0 ? 'right' : 'left';
}

/** The six words of a tile id from the digest's text; zeros for text that is not six integers. */
export function parseTile(text) {
  const parts = typeof text === 'string' ? text.split(',') : [];
  const out = ['0', '0', '0', '0', '0', '0'];
  if (parts.length !== 6) return out;
  for (let i = 0; i < 6; i++) {
    const p = parts[i].trim();
    if (!/^-?\d+$/.test(p)) return ['0', '0', '0', '0', '0', '0'];
    out[i] = p;
  }
  return out;
}

// State.p[] of the curved spaces (lane B's modules write them): Curved Walk sends the dial in p[3] and the
// signed curvature of the frame, 1/m^2, in p[4], with the metres per unit of the frame in p[0]; Small Planet
// sends its radius in p[0]; Aligned Cells sends its columns and rows in p[4] and p[5].
function param(p, i) {
  const v = Array.isArray(p) ? Number(p[i]) : NaN;
  return Number.isFinite(v) ? v : NaN;
}

/**
 * The curvature dial of a curved space as the Console words it: the tiling's q and the length scale the
 * walker is drawn with. Empty for a flat space and before the first State of the space.
 */
export function dialLabel(space, p) {
  if (space === 2) {
    const q = Math.round(param(p, 3));
    if (!(q >= 3 && q <= 6)) return '';
    const l = param(p, 0);
    // Until the ramp begins, and after it has come back down, the walker stands in the flat room.
    if (!(param(p, 4) < 0) || !(l > 0)) return 'q = ' + q + ' · flat';
    return 'q = ' + q + ' · L = ' + l.toFixed(2) + ' m';
  }
  if (space === 4) {
    const r = param(p, 0);
    return r > 0 ? 'q = 3 · R = ' + r.toFixed(2) + ' m' : '';
  }
  return '';
}

/**
 * The walker's cell as the map names it for a space (StateMessage: S1 cell (i, j, k), S2 the tile of the
 * {4,q} tiling, S3 the floor cell and the tile it shows, S4 face index, S5 room id; a sheet where the space
 * has one). p is State.p[] and may be left out.
 */
export function cellLabel(space, tileText, sheet, p) {
  const t = parseTile(tileText);
  switch (space) {
    case 0: return 'room';
    case 1: return 'cell (' + t[0] + ', ' + t[1] + ', ' + t[2] + ')';
    case 2: {
      const q = Math.round(param(p, 3));
      const tiling = q === 5 || q === 6 ? '{4,' + q + '} · ' : '';
      return tiling + 'tile ' + t.join(',') + (sheet ? ' · sheet ' + sheet : '');
    }
    case 3: {
      // The sheet is the cell's index, row by row; with the grid's width it is a column and a row.
      const columns = Math.round(param(p, 4));
      const cell = columns > 0 ? '(' + (sheet % columns) + ', ' + Math.floor(sheet / columns) + ')' : String(sheet);
      return 'cell ' + cell + ' · tile ' + t.join(',');
    }
    case 4: return 'face ' + t[0];
    case 5: return 'room ' + t[0] + (sheet ? ' · sheet ' + sheet : '');
    case 6: return 'room';
    default: return 'tile ' + t.join(',') + (sheet ? ' · sheet ' + sheet : '');
  }
}
