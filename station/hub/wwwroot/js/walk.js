// The Map page's walk keys: WASD (or the arrows) drive the phone's walker head over the link (OperatorCmd walk).
// The phone holds an input for 0.6 s only, so a held key is repeated well inside that and a lost Console stops
// the walker by itself. No DOM here.

/** Metres per second, and degrees per second of turn; Shift gives the fast pair. */
export const WALK_SPEED = 1.0;
export const WALK_FAST_SPEED = 2.0;
export const TURN_RATE = 90;
export const TURN_FAST_RATE = 180;
/** How often a held input is sent again (the phone's hold is 600 ms). */
export const REPEAT_MS = 200;

// key code -> [right, forward, turn]: one unit each.
const KEYS = {
  KeyW: [0, 1, 0], ArrowUp: [0, 1, 0],
  KeyS: [0, -1, 0], ArrowDown: [0, -1, 0],
  KeyA: [-1, 0, 0],
  KeyD: [1, 0, 0],
  KeyQ: [0, 0, -1], ArrowLeft: [0, 0, -1],
  KeyE: [0, 0, 1], ArrowRight: [0, 0, 1]
};

/** True for a key that walks or turns. */
export function isWalkKey(code) {
  return Object.prototype.hasOwnProperty.call(KEYS, code);
}

/** The keys held now, and the walk input they make. */
export class WalkKeys {
  constructor() {
    this.held = new Set();
    this.fast = false;
  }

  /** A key went down; false for a key that is not a walk key. */
  down(code) {
    if (code === 'ShiftLeft' || code === 'ShiftRight') {
      this.fast = true;
      return false;
    }
    if (!isWalkKey(code)) return false;
    this.held.add(code);
    return true;
  }

  /** A key came up; false for a key that is not a walk key. */
  up(code) {
    if (code === 'ShiftLeft' || code === 'ShiftRight') {
      this.fast = false;
      return false;
    }
    if (!isWalkKey(code)) return false;
    this.held.delete(code);
    return true;
  }

  /** Every key let go: the page lost focus, or driving was switched off. */
  clear() {
    this.held.clear();
    this.fast = false;
  }

  /** {x, y, turn, speed}: x right and y forward from -1 to 1, turn in degrees per second (positive right). */
  input() {
    let x = 0;
    let y = 0;
    let turn = 0;
    for (const code of this.held) {
      const k = KEYS[code];
      x += k[0];
      y += k[1];
      turn += k[2];
    }
    // W and the up arrow together are still one forward; opposite keys cancel.
    const clamp = (v) => Math.max(-1, Math.min(1, v));
    return {
      x: clamp(x),
      y: clamp(y),
      turn: clamp(turn) * (this.fast ? TURN_FAST_RATE : TURN_RATE),
      speed: this.fast ? WALK_FAST_SPEED : WALK_SPEED
    };
  }
}

/** True for an input that neither moves nor turns. */
export function isStill(input) {
  return input.x === 0 && input.y === 0 && input.turn === 0;
}

function same(a, b) {
  return a.x === b.x && a.y === b.y && a.turn === b.turn && a.speed === b.speed;
}

/**
 * When to send: a changed input at once, a held one every REPEAT_MS, and one still input when the keys are
 * let go. Nothing while the walker stands still.
 */
export class WalkSender {
  constructor(repeatMs = REPEAT_MS) {
    this.repeatMs = repeatMs;
    this.last = { x: 0, y: 0, turn: 0, speed: WALK_SPEED };
    this.lastMs = -Infinity;
  }

  /** The input to send now, or null. */
  offer(input, nowMs) {
    const changed = !same(input, this.last);
    const still = isStill(input);
    // A change of speed alone while standing still is nothing to send.
    if (still && isStill(this.last)) {
      this.last = input;
      return null;
    }
    if (!changed && (still || nowMs - this.lastMs < this.repeatMs)) return null;
    this.last = input;
    this.lastMs = nowMs;
    return input;
  }

  /** Forgets what was sent: the next moving input goes out at once. */
  reset() {
    this.last = { x: 0, y: 0, turn: 0, speed: WALK_SPEED };
    this.lastMs = -Infinity;
  }
}
