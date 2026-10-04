// Runs the Console's own page script against a running Hub with no browser: a small stand-in for the DOM,
// built from the ids of index.html, receives everything app.js writes. It catches what a syntax check
// cannot: an id the page lacks, a frame the script cannot digest, an exception in a draw call.
//
//   node --experimental-websocket station/console-tests/page-smoke.mjs http://127.0.0.1:8080
//
// Needs a phone or tools/ReplayPhone on the Hub. Exit code 0 when the page ran clean.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const base = new URL(process.argv[2] || 'http://127.0.0.1:8080');
const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '../hub/wwwroot');
const html = readFileSync(path.join(root, 'index.html'), 'utf8');

const problems = [];
const noop = () => {};
// A canvas context that accepts every call and property.
const context = new Proxy({}, { get: () => noop, set: () => true });

class Element {
  constructor(tag, id = '') {
    this.tagName = tag.toUpperCase();
    this.id = id;
    this.children = [];
    this.dataset = {};
    this.style = {};
    this.listeners = {};
    this.attributes = {};
    this.text = '';
    this.value = '';
    this.hidden = false;
    this.className = '';
    this.clientWidth = 640;
    this.clientHeight = 200;
    const classes = new Set();
    this.classList = {
      add: (...c) => c.forEach((x) => classes.add(x)),
      remove: (...c) => c.forEach((x) => classes.delete(x)),
      toggle: (c, on) => (on === undefined ? (classes.has(c) ? classes.delete(c) : classes.add(c)) : on ? classes.add(c) : classes.delete(c)),
      contains: (c) => classes.has(c)
    };
  }
  get textContent() { return this.text + this.children.map((c) => (typeof c === 'string' ? c : c.textContent)).join(''); }
  set textContent(v) { this.text = String(v); this.children = []; }
  get firstElementChild() { return this.children.find((c) => typeof c !== 'string') || null; }
  get lastElementChild() { return [...this.children].reverse().find((c) => typeof c !== 'string') || null; }
  append(...nodes) { for (const n of nodes) { this.children.push(n); if (typeof n !== 'string') n.parent = this; } }
  prepend(...nodes) { this.children.unshift(...nodes); for (const n of nodes) if (typeof n !== 'string') n.parent = this; }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); }
  addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
  dispatch(type, event = {}) { for (const fn of this.listeners[type] || []) fn({ target: this, preventDefault: noop, ...event }); }
  setAttribute(k, v) { this.attributes[k] = v; }
  removeAttribute(k) { delete this.attributes[k]; }
  getContext() { return context; }
}

const byId = new Map();
for (const match of html.matchAll(/<([a-z0-9]+)\b[^>]*\bid="([^"]+)"/g)) byId.set(match[2], new Element(match[1], match[2]));
const pages = [...html.matchAll(/data-page="([^"]+)"/g)].map((m) => Object.assign(new Element('section'), { dataset: { page: m[1] } }));
const tabs = [...html.matchAll(/data-tab="([^"]+)"/g)].map((m) => Object.assign(new Element('a'), { dataset: { tab: m[1] } }));

const documentElement = new Element('html');
globalThis.document = {
  documentElement,
  getElementById(id) {
    if (!byId.has(id)) {
      problems.push('index.html has no element with id "' + id + '"');
      byId.set(id, new Element('div', id));
    }
    return byId.get(id);
  },
  createElement: (tag) => new Element(tag),
  querySelectorAll: (selector) => (selector === '[data-page]' ? pages : selector === '[data-tab]' ? tabs : []),
  addEventListener: noop
};
globalThis.getComputedStyle = () => ({ getPropertyValue: () => '#888888' });
globalThis.location = { protocol: base.protocol, host: base.host, hash: '#dashboard' };
const frameCallbacks = [];
globalThis.requestAnimationFrame = (fn) => frameCallbacks.push(fn);
globalThis.window = { devicePixelRatio: 2, addEventListener: (type, fn) => { if (type === 'hashchange') globalThis.onHash = fn; } };

// Relative requests go to the Hub, as they do in the browser.
const realFetch = globalThis.fetch;
globalThis.fetch = (url, init) => realFetch(new URL(url, base), init);

process.on('uncaughtException', (e) => problems.push('exception: ' + (e.stack || e)));
process.on('unhandledRejection', (e) => problems.push('rejection: ' + (e && e.stack || e)));

await import(path.join(root, 'js/app.js'));

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const paint = () => frameCallbacks.splice(0).forEach((fn) => fn(performance.now()));
const text = (id) => byId.get(id).textContent;

// Walk every page so each one draws, as an operator clicking through the tabs would.
for (const page of ['dashboard', 'map', 'tuner', 'operator', 'assets', 'pairing']) {
  location.hash = '#' + page;
  globalThis.onHash();
  await sleep(700);
  paint();
}

// Use the page as the operator does: drag a slider, press STOP, log a comfort score.
const groups = byId.get('tuner-groups');
const rows = groups.children.flatMap((g) => g.children[1].children);
const scaleRow = rows.find((r) => r.children[0].textContent.startsWith('render.scale'));
let echoNote = '';
if (!scaleRow) problems.push('the tuner has no row for render.scale');
else {
  const slider = scaleRow.children[1].children[0];
  const before = Number(slider.value);
  slider.value = String(Math.abs(before - 0.6) < 1e-6 ? 0.9 : 0.6);
  slider.dispatch('input');
  slider.dispatch('change');
  await sleep(400);
  echoNote = scaleRow.children[2].textContent;
  if (!/echo/.test(echoNote)) problems.push('no echo shown for render.scale: "' + echoNote + '"');
}
byId.get('stop').dispatch('click');
byId.get('fms-score').value = '4';
byId.get('fms-form').dispatch('submit');
byId.get('seq-score').value = '6';
byId.get('seq-form').dispatch('submit');
await sleep(600);
paint();

const report = {
  phone: text('phone-state'), rtt: text('phone-rtt'), clock: text('phone-clock'), state: text('state-hz'), loss: text('state-loss'),
  session: text('session-id'), space: text('space-now'), bump: text('bump-now'), groups: groups.children.length, parameters: rows.length,
  echo: echoNote, stop: text('stop-status'), comfort: text('fms-status'), seq: text('seq-status'), alert: text('fms-alert'), alertHidden: byId.get('fms-alert').hidden, rows: text('rows-summary'), walker: text('map-walker'),
  nearest: text('map-nearest'), room: text('map-room'), pairing: text('pair-url'), jobs: byId.get('job-rows').children.length
};
console.log(report);
if (report.phone !== 'connected') problems.push('the strip does not show a connected phone');
if (!/phone \d+ ms/.test(report.stop)) problems.push('STOP shows no acknowledgement from the phone: "' + report.stop + '"');
if (!/Logged 4/.test(report.comfort)) problems.push('the comfort score was not confirmed: "' + report.comfort + '"');
if (!/Logged 6/.test(report.seq)) problems.push('the SEQ score was not confirmed: "' + report.seq + '"');
if (report.alertHidden || !/score 4/.test(report.alert)) problems.push('no operator alert for an FMS entry at pg.fmsStop: "' + report.alert + '"');
if (!/rows shown/.test(report.rows)) problems.push('the dashboard shows no telemetry');
if (!/^atlas:\/\//.test(report.pairing)) problems.push('the pairing page shows no address');
if (report.groups === 0) problems.push('the tuner built no groups');

for (const p of problems) console.log('FAIL ' + p);
console.log(problems.length === 0 ? 'page smoke passed' : 'page smoke FAILED: ' + problems.length);
process.exit(problems.length === 0 ? 0 : 1);
