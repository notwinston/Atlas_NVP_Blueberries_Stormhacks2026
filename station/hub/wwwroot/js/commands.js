// The six operator commands and their acknowledgements (Master Plan section 7.3.2, lane D contract 2.9).
// A command is acknowledged twice: by the Hub's HTTP answer, and by the phone's own event once it has handed
// the command to the app. The Console shows both and the time each took. No DOM here.

/** After this long with no event from the phone a command is shown as unacknowledged (test L10 allows 250 ms). */
export const ACK_TIMEOUT_MS = 3000;

/** The request that carries a command: {path, body}. Unknown commands give null. */
export function requestFor(name, args = {}) {
  switch (name) {
    case 'space': {
      const id = Number(args.space);
      if (!Number.isInteger(id) || id < 0 || id > 6) return null;
      return { path: '/api/scene', body: { space: 'S' + id, variant: args.variant || '', args: {} } };
    }
    case 'home': return { path: '/api/op', body: { cmd: 'home', args: {} } };
    case 'recentre': return { path: '/api/op', body: { cmd: 'recentre', args: {} } };
    case 'cue': return { path: '/api/op', body: { cmd: 'cue', args: {} } };
    case 'quality': {
      const tier = Number(args.tier);
      if (!Number.isInteger(tier) || tier < -1 || tier > 4) return null;
      return { path: '/api/op', body: { cmd: 'quality', args: { tier } } };
    }
    case 'lock': return { path: '/api/op', body: { cmd: 'lock', args: { on: args.on === true } } };
    case 'hands': return { path: '/api/op', body: { cmd: 'hands', args: { on: args.on === true } } };
    default: return null;
  }
}

/** What the operator reads for a command: "STOP", "switch to S1", "quality auto". */
export function describe(name, args = {}) {
  switch (name) {
    case 'space': return 'switch to S' + args.space;
    case 'home': return 'return to Home';
    case 'recentre': return 'recentre';
    case 'cue': return 'STOP';
    case 'quality': return 'quality ' + (args.tier === -1 ? 'auto' : 'tier ' + args.tier);
    case 'lock': return args.on ? 'lock on' : 'lock off';
    case 'hands': return args.on ? 'hands on' : 'hands off';
    default: return name;
  }
}

/** The comfort score the operator typed, 0 to 20 (Master Plan section 13.5); null when it is not one. */
export function comfortScore(raw) {
  const text = String(raw).trim();
  if (!/^\d{1,2}$/.test(text)) return null;
  const n = Number(text);
  return n <= 20 ? n : null;
}

/** The Single Ease Question score the operator typed, 1 to 7 (Master Plan section 11.2); null when it is not one. */
export function seqScore(raw) {
  const text = String(raw).trim();
  if (!/^[1-7]$/.test(text)) return null;
  return Number(text);
}

/** The operator alert text for a Hub `pg.fmsAlert` event ({score, stop}); the Hub judged it against pg.fmsStop. */
export function fmsAlertText(data) {
  if (!data || !Number.isFinite(data.score)) return '';
  const stop = Number.isFinite(data.stop) ? data.stop : '?';
  return 'STOP the walk: comfort score ' + data.score + ' reached the stop value ' + stop + ' (pg.fmsStop). Headset off, sit down, water.';
}

/** Commands sent and what came back, newest first. */
export class CommandTracker {
  constructor(limit = 40) {
    this.limit = limit;
    this.entries = [];
    this.nextId = 1;
  }

  /** Notes a command as sent. Returns its entry. */
  sent(name, args, nowMs) {
    const entry = {
      id: this.nextId++, name, args: args || {}, label: describe(name, args), sentMs: nowMs,
      hub: 'pending', hubMs: null, hubError: '', phone: 'pending', phoneMs: null
    };
    this.entries.unshift(entry);
    if (this.entries.length > this.limit) this.entries.length = this.limit;
    return entry;
  }

  /** The Hub answered the request. */
  hubAnswered(entry, ok, nowMs, error = '') {
    entry.hub = ok ? 'ok' : 'refused';
    entry.hubMs = nowMs - entry.sentMs;
    entry.hubError = ok ? '' : error;
    // A command the Hub did not send can have no acknowledgement from the phone.
    if (!ok) entry.phone = 'none';
    return entry;
  }

  /**
   * The phone's command event ("cmd" on the link, "op" in the app's log) arrived: the oldest command of that
   * name still waiting is acknowledged. Returns it, or null when the command was another Console's.
   */
  phoneEvent(event, nowMs) {
    if (!event || (event.ev !== 'cmd' && event.ev !== 'op') || !event.data) return null;
    const name = event.data.cmd;
    for (let i = this.entries.length - 1; i >= 0; i--) {
      const entry = this.entries[i];
      if (entry.name !== name || entry.phone !== 'pending') continue;
      entry.phone = 'ack';
      entry.phoneMs = nowMs - entry.sentMs;
      return entry;
    }
    return null;
  }

  /** Marks commands the phone never acknowledged. Returns true when any entry changed. */
  expire(nowMs, timeoutMs = ACK_TIMEOUT_MS) {
    let changed = false;
    for (const entry of this.entries) {
      if (entry.phone === 'pending' && nowMs - entry.sentMs > timeoutMs) {
        entry.phone = 'timeout';
        changed = true;
      }
    }
    return changed;
  }

  /** The newest entry of a command, for the mark beside its button. */
  latest(name) {
    for (const entry of this.entries) if (entry.name === name) return entry;
    return null;
  }
}

/** One line for an entry: "hub 3 ms · phone 21 ms". */
export function statusText(entry) {
  if (!entry) return '';
  const ms = (v) => (v === null ? '' : ' ' + Math.round(v) + ' ms');
  let hub;
  if (entry.hub === 'pending') hub = 'sending…';
  else if (entry.hub === 'ok') hub = 'hub' + ms(entry.hubMs);
  else hub = 'refused: ' + (entry.hubError || 'error');
  if (entry.hub !== 'ok') return hub;
  if (entry.phone === 'ack') return hub + ' · phone' + ms(entry.phoneMs);
  if (entry.phone === 'timeout') return hub + ' · no answer from the phone';
  return hub + ' · waiting for the phone';
}
