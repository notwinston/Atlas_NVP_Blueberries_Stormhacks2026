// The Console's routes, checked against a running Hub with a phone (or tools/ReplayPhone) connected: every
// file is served, the feed carries State and telemetry, a parameter set as the Console sets it comes back as
// the phone's echo, and each of the six operator commands is acknowledged by the phone.
//
//   dotnet run --project station/hub -- --http 8080
//   dotnet run --project tools/ReplayPhone
//   node --experimental-websocket station/console-tests/live-check.mjs http://127.0.0.1:8080
//
// (Node 22 needs no flag.) Exit code 0 when every check passed.

import { readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const base = (process.argv[2] || 'http://127.0.0.1:8080').replace(/\/$/, '');
const here = path.dirname(fileURLToPath(import.meta.url));
const scripts = readdirSync(path.join(here, '../hub/wwwroot/js')).filter((f) => f.endsWith('.js')).sort();

let failed = 0;
function check(ok, text) {
  console.log((ok ? 'ok   ' : 'FAIL ') + text);
  if (!ok) failed++;
}

async function get(route, type) {
  const response = await fetch(base + route);
  const got = response.headers.get('content-type') || '';
  await response.arrayBuffer();
  check(response.status === 200 && got.startsWith(type), 'GET ' + route + ' -> ' + response.status + ' ' + got);
}

async function post(route, body, method = 'POST') {
  const response = await fetch(base + route, {
    method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body)
  });
  const text = await response.text();
  return { status: response.status, json: text ? JSON.parse(text) : null };
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

if (typeof WebSocket === 'undefined') {
  console.error('This Node has no WebSocket: run with  node --experimental-websocket');
  process.exit(2);
}

// ---- 1. files ----------------------------------------------------------------------------------------
await get('/', 'text/html');
await get('/index.html', 'text/html');
await get('/console.css', 'text/css');
for (const script of scripts) await get('/js/' + script, 'text/javascript');
await get('/pair', 'text/html');
await get('/pair/qr.svg', 'image/svg+xml');
await get('/api/pair', 'application/json');
await get('/api/session', 'application/json');
await get('/api/params', 'application/json');
await get('/api/presets', 'application/json');

// ---- 2. the feed -------------------------------------------------------------------------------------
const frames = [];
const waiters = [];
const socket = new WebSocket(base.replace(/^http/, 'ws') + '/ws');
socket.onmessage = (message) => {
  const frame = JSON.parse(message.data);
  frame.atMs = performance.now();
  frames.push(frame);
  for (let i = waiters.length - 1; i >= 0; i--) {
    if (waiters[i].match(frame)) {
      waiters[i].resolve(frame);
      waiters.splice(i, 1);
    }
  }
};
await new Promise((resolve, reject) => {
  socket.onopen = resolve;
  socket.onerror = () => reject(new Error('cannot open ' + base + '/ws'));
});

/** The next frame that matches, or null after the time-out. */
function next(match, timeoutMs = 2500) {
  return new Promise((resolve) => {
    const waiter = { match, resolve };
    waiters.push(waiter);
    setTimeout(() => {
      const i = waiters.indexOf(waiter);
      if (i >= 0) {
        waiters.splice(i, 1);
        resolve(null);
      }
    }, timeoutMs);
  });
}
const event = (ev, test = () => true) => next((f) => f.t === 'event' && f.ev === ev && test(f.data || {}));

await sleep(2500);
const count = (t) => frames.filter((f) => f.t === t).length;
const link = frames.find((f) => f.t === 'link');
check(!!link && link.phone.state === 'connected', 'feed: link, phone ' + (link ? link.phone.state : 'missing') + ', session ' + (link ? link.sessionId : '?'));
check(!!link && typeof link.phone.clockOffsetMs === 'number', 'feed: link carries the clock offset (' + (link && link.phone.clockOffsetMs) + ' ms)');
const manifest = frames.find((f) => f.t === 'manifest');
check(!!manifest && manifest.params.length > 0, 'feed: manifest with ' + (manifest ? manifest.params.length : 0) + ' parameters');
check(count('state') >= 15, 'feed: ' + count('state') + ' state digests in 2.5 s (10 Hz)');
check(count('telemetry') >= 2, 'feed: ' + count('telemetry') + ' telemetry rows in 2.5 s');
check(count('outline') === 1, 'feed: room outline');
const state = frames.filter((f) => f.t === 'state').pop();
check(!!state && Array.isArray(state.head) && state.head.length === 3 && typeof state.yawDeg === 'number' && state.hz > 50,
  'feed: state has head, yawDeg and ' + (state && state.hz) + ' Hz');

// ---- 3. a parameter, as the Console sets it ----------------------------------------------------------
if (link && link.demoLock) await post('/api/op', { cmd: 'lock', args: { on: false } });
const scale = manifest.params.find((p) => p.name === 'render.scale');
const target = Math.abs(scale.val - 0.7) < 1e-6 ? 0.9 : 0.7;
let waiting = event('param', (d) => d.name === 'render.scale');
let sentAt = performance.now();
socket.send(JSON.stringify({ t: 'param', name: 'render.scale', value: target }));
let echo = await waiting;
check(!!echo && Math.abs(echo.data.value - target) < 1e-6,
  'param over the WebSocket: render.scale ' + target + ' echoed by the phone' + (echo ? ' in ' + (echo.atMs - sentAt).toFixed(1) + ' ms' : ' (no echo)'));

const rho = manifest.params.find((p) => p.name === 'curved.cullRho');
await post('/api/params/curved.cullRho', { value: rho.min });
await sleep(300);
waiting = event('param', (d) => d.name === 'curved.cullRho');
const set = await post('/api/params/curved.cullRho', { value: 99 });
echo = await waiting;
check(set.status === 200 && set.json.value === rho.max && !!echo && echo.data.value === rho.max,
  'param over HTTP: curved.cullRho 99 clamped to ' + rho.max + ' by the Hub (' + set.status + ') and by the phone (' + (echo ? echo.data.value : 'no echo') + ')');
const read = await (await fetch(base + '/api/params/curved.cullRho')).json();
check(read.val === rho.max, 'GET /api/params/curved.cullRho now holds ' + read.val);

// ---- 4. the six operator commands --------------------------------------------------------------------
async function command(label, route, body, name) {
  const ack = event('cmd', (d) => d.cmd === name);
  const at = performance.now();
  const reply = await post(route, body);
  const hubMs = performance.now() - at;
  const got = await ack;
  check(reply.status === 200 && !!got, label + ': hub ' + reply.status + ' in ' + hubMs.toFixed(1) + ' ms, phone ' +
    (got ? 'acknowledged in ' + (got.atMs - at).toFixed(1) + ' ms' : 'did NOT acknowledge'));
}
await command('switch space S1', '/api/scene', { space: 'S1', variant: '', args: {} }, 'space');
const moved = await next((f) => f.t === 'state' && f.space === 1);
check(!!moved, 'State now reports space 1');
await command('return to Home', '/api/op', { cmd: 'home', args: {} }, 'home');
await command('recentre', '/api/op', { cmd: 'recentre', args: {} }, 'recentre');
await command('quality tier 2', '/api/op', { cmd: 'quality', args: { tier: 2 } }, 'quality');
const tier = await next((f) => f.t === 'state' && f.tier === 2);
check(!!tier, 'State now reports tier 2');
await command('quality auto', '/api/op', { cmd: 'quality', args: { tier: -1 } }, 'quality');
const cueSeen = next((f) => f.t === 'state' && f.cue === true && f.bump === 2, 1500);
await command('STOP (cue)', '/api/op', { cmd: 'cue', args: {} }, 'cue');
check(!!(await cueSeen), 'State shows the cue at STRONG level after STOP');
await command('lock on', '/api/op', { cmd: 'lock', args: { on: true } }, 'lock');
const locked = await post('/api/params/render.scale', { value: 0.6 });
check(locked.status === 423, 'under the lock a parameter set is refused: ' + locked.status);
const lockLink = frames.filter((f) => f.t === 'link').pop();
check(lockLink.demoLock === true, 'link frame reports demoLock');
await command('lock off', '/api/op', { cmd: 'lock', args: { on: false } }, 'lock');
const unknown = await post('/api/op', { cmd: 'freeze', args: {} });
check(unknown.status === 404, 'an unknown command is refused: ' + unknown.status);

// ---- 5. comfort score and note -----------------------------------------------------------------------
waiting = event('fms', (d) => d.score === 3);
socket.send(JSON.stringify({ t: 'fms', score: 3 }));
const fms = await waiting;
check(!!fms && fms.scene === 'hub', 'comfort score 3 logged as an event');
const refused = next((f) => f.t === 'error');
socket.send(JSON.stringify({ t: 'fms', score: 21 }));
check(!!(await refused), 'comfort score 21 is refused');
waiting = event('pg.fmsAlert', (d) => d.score === 6);
socket.send(JSON.stringify({ t: 'fms', score: 6 }));
const alerted = await waiting;
check(!!alerted && alerted.scene === 'hub' && Number.isFinite(alerted.data.stop), 'comfort score at or above pg.fmsStop raises pg.fmsAlert');
waiting = event('seq', (d) => d.score === 5);
socket.send(JSON.stringify({ t: 'seq', score: 5 }));
const seq = await waiting;
check(!!seq && seq.scene === 'hub', 'SEQ score 5 logged as a Hub event');
const seqRefused = next((f) => f.t === 'error');
socket.send(JSON.stringify({ t: 'seq', score: 8 }));
check(!!(await seqRefused), 'SEQ score 8 is refused');
waiting = event('note');
socket.send(JSON.stringify({ t: 'note', text: 'console live check' }));
check(!!(await waiting), 'note logged as an event');
const events = await (await fetch(base + '/logs/' + link.sessionId + '/events.jsonl')).text();
check(/"ev":"fms"/.test(events) && /"ev":"note"/.test(events) && /"ev":"cmd"/.test(events), 'events.jsonl holds the fms, note and cmd events');

// ---- 6. presets --------------------------------------------------------------------------------------
const name = 'console-check';
const saved = await post('/api/presets/' + name);
const listed = await (await fetch(base + '/api/presets')).json();
check(saved.status === 204 && listed.presets.some((p) => p.name === name && p.count > 0), 'preset saved from the current values and listed');
waiting = event('param', (d) => d.name === 'render.scale');
socket.send(JSON.stringify({ t: 'param', name: 'render.scale', value: target === 0.7 ? 0.9 : 0.7 }));
await waiting;
waiting = event('param', (d) => d.name === 'render.scale' && Math.abs(d.value - target) < 1e-6);
const loaded = await post('/api/presets/' + name + '/load');
check(loaded.status === 204 && !!(await waiting), 'preset loaded: the phone is back at render.scale ' + target);
const removed = await post('/api/presets/' + name, undefined, 'DELETE');
check(removed.status === 204, 'preset deleted');

// ---- 7. a Forge job, as the Forge posts it -----------------------------------------------------------
waiting = next((f) => f.t === 'job' && f.kind === 'status');
const job = await post('/internal/job', { jobId: 'j-check', roomId: 'check', boxId: 1, state: 'generating', source: 'local', error: '', queuePos: 0, triTarget: 5000 });
check(job.status === 204 && !!(await waiting), 'a JobStatus posted to the Hub reaches the Console as a job frame');

// ---- 8. the dashboard's backfill ---------------------------------------------------------------------
const csv = await (await fetch(base + '/logs/' + link.sessionId + '/perf.csv')).text();
const { parseCsv } = await import('../hub/wwwroot/js/telemetry.js');
check(parseCsv(csv).length >= 2, 'perf.csv of the session parses to ' + parseCsv(csv).length + ' rows');

socket.close();
console.log(failed === 0 ? '\nlive check passed' : '\nlive check FAILED: ' + failed);
process.exit(failed === 0 ? 0 : 1);
