// The operator Console (Master Plan section 7.3, tasks P1-D03 and P1-D06; lane D contract 3.5). This file is
// the page's wiring: it reads the Hub's frames, keeps the small models of the other modules up to date and
// writes the DOM. Everything that can be wrong in an interesting way lives in those modules and is tested.

import { Feed } from './feed.js';
import {
  bumpName, fixed, formatCount, formatMs, formatOffset, formatPercent, spaceLabel, thermalAdvice, thermalName,
  tierLabel, SPACE_COUNT
} from './format.js';
import {
  SendThrottle, buildGroups, displayValue, findControl, isPresetName, readEcho, requestValue
} from './controls.js';
import { TelemetryBuffer, parseCsv } from './telemetry.js';
import { drawLines, drawTimeline } from './charts.js';
import { Trail, cellLabel, dialLabel, nearestObstacle, relativeSide } from './mapmath.js';
import { drawMap } from './mapview.js';
import { mountNetworkPage } from './network.js';
import { JobList, phaseOf } from './jobs.js';
import { CommandTracker, comfortScore, fmsAlertText, requestFor, seqScore, statusText } from './commands.js';
import { WalkKeys, WalkSender } from './walk.js';

const $ = (id) => document.getElementById(id);

const ECHO_WAIT_MS = 1500;

const model = {
  link: null,
  session: '',
  digest: null,
  room: null,
  groups: [],
  signature: '',
  locked: false,
  views: new Map(),        // parameter name -> the row's elements and state
  userOpen: new Map(),     // group key -> the fold the operator chose
  telemetry: new TelemetryBuffer(900),
  trail: new Trail(600),
  jobs: new JobList(),
  tracker: new CommandTracker(),
  page: 'tuner',
  dirtyCharts: false,
  dirtyMap: false,
  fmsPending: null,
  seqPending: null,
  notePending: false,
  walkKeys: new WalkKeys(),
  walkSender: new WalkSender(),
  driving: false,          // the Map page's keys drive the phone's walker
  driveRestore: 0          // the pose.source to go back to when driving stops
};

// ---- HTTP ------------------------------------------------------------------------------------------

/** One request to the Hub. Never throws: {ok, status, json, error}. */
async function api(method, path, body) {
  try {
    const init = { method, headers: {} };
    if (body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    }
    const response = await fetch(path, init);
    let json = null;
    const text = await response.text();
    if (text) {
      try {
        json = JSON.parse(text);
      } catch (e) {
        json = null;
      }
    }
    return { ok: response.ok, status: response.status, json, text, error: response.ok ? '' : (json && json.error) || 'HTTP ' + response.status };
  } catch (e) {
    return { ok: false, status: 0, json: null, text: '', error: 'the Hub does not answer' };
  }
}

function setStatus(el, text, tone) {
  el.textContent = text;
  el.classList.toggle('good', tone === 'good');
  el.classList.toggle('bad', tone === 'bad');
}

let errorTimer = 0;
function showError(text) {
  const el = $('banner-error');
  el.textContent = text;
  el.hidden = false;
  clearTimeout(errorTimer);
  errorTimer = setTimeout(() => { el.hidden = true; }, 6000);
}

function clockTime(date = new Date()) {
  const two = (n) => (n < 10 ? '0' + n : String(n));
  return two(date.getHours()) + ':' + two(date.getMinutes()) + ':' + two(date.getSeconds());
}

// ---- tabs ------------------------------------------------------------------------------------------

let networkPage = null;
const PAGES = ['tuner', 'operator', 'dashboard', 'map', 'view', 'assets', 'network', 'pairing'];

function showPage() {
  const wanted = location.hash.replace('#', '');
  model.page = PAGES.includes(wanted) ? wanted : 'tuner';
  for (const section of document.querySelectorAll('[data-page]')) section.hidden = section.dataset.page !== model.page;
  for (const tab of document.querySelectorAll('[data-tab]')) {
    const on = tab.dataset.tab === model.page;
    tab.classList.toggle('active', on);
    if (on) tab.setAttribute('aria-current', 'page');
    else tab.removeAttribute('aria-current');
  }
  // A hidden canvas has no size: draw when its page is shown.
  if (model.page === 'dashboard') drawCharts();
  if (model.page === 'map') paintMap();
  else stopWalking();   // the walk keys work on the Map page only
  showView();
  if (model.page === 'network') (networkPage || (networkPage = mountNetworkPage())).show();
  if (model.page === 'pairing') loadPairing('');
  if (model.page === 'tuner') refreshPresets();
}

// ---- the strip -------------------------------------------------------------------------------------

function renderLink(link) {
  model.link = link;
  const phone = link.phone || {};
  const state = phone.state || 'offline';
  $('phone-dot').className = 'dot ' + state;
  $('phone-state').textContent = state;
  $('phone-device').textContent = phone.device ? phone.device + (phone.build ? ' · ' + phone.build : '') : '';
  const online = state !== 'offline';
  $('phone-rtt').textContent = online ? formatMs(phone.rttMs) : '–';
  $('phone-clock').textContent = online ? formatOffset(phone.clockOffsetMs) : '–';
  $('state-hz').textContent = online ? fixed(phone.stateHz, 1) + ' Hz' : '–';
  $('state-loss').textContent = online ? formatPercent(phone.loss) : '–';
  if (!online) {
    $('state-latency').textContent = '–';
    for (const id of ['space-now', 'bump-now', 'tier-now']) $(id).textContent = '–';
    $('cell-now').textContent = '';
    $('bump-bar').removeAttribute('data-level');
  }

  const spectators = link.spectators || 0;
  $('spectators').textContent = String(spectators);
  $('spectator-dot').className = 'dot ' + (spectators > 0 ? 'connected' : '');

  const forge = link.forge || {};
  $('banner-crops').hidden = forge.debugKeep !== true;
  $('banner-hosted').hidden = forge.hosted !== true;
  $('forge-summary').textContent = forge.up
    ? 'Forge up · ' + (forge.queue || 0) + ' job' + (forge.queue === 1 ? '' : 's') + ' in the queue' + (forge.hosted ? ' · hosted fallback armed' : '')
    : 'Forge not reachable: the phone places built-in meshes.';

  setLocked(link.demoLock === true);

  if (link.sessionId && link.sessionId !== model.session) newSession(link.sessionId);
  $('pair-phone').textContent = online ? 'connected' + (phone.device ? ' (' + phone.device + ', ' + (phone.address || '') + ')' : '') : 'not connected';
}

// The session id names the log folder. A new one is a new walk: the charts and the trail start again, and
// the rows the Hub already wrote for it are fetched so that a Console opened late shows the whole session.
async function newSession(id) {
  model.session = id;
  $('session-id').textContent = id;
  $('csv-link').href = '/logs/' + encodeURIComponent(id) + '/perf.csv';
  $('events-link').href = '/logs/' + encodeURIComponent(id) + '/events.jsonl';
  model.telemetry.clear();
  model.trail.clear();
  const reply = await api('GET', '/logs/' + encodeURIComponent(id) + '/perf.csv');
  if (reply.ok && model.session === id) {
    model.telemetry.pushAll(parseCsv(reply.text));
    renderTelemetry();
  }
}

function setLocked(locked) {
  $('lock-badge').hidden = !locked;
  $('cmd-lock').checked = locked;
  if (locked === model.locked) return;
  model.locked = locked;
  applyLock();
  renderDrive();
}

function renderState(digest) {
  model.digest = digest;
  const dial = dialLabel(digest.space, digest.p);
  $('space-now').textContent = spaceLabel(digest.space) + (dial ? ' · ' + dial : '');
  $('cell-now').textContent = cellLabel(digest.space, digest.tile, digest.sheet, digest.p);
  $('bump-bar').dataset.level = String(digest.bump);
  $('bump-now').textContent = bumpName(digest.bump) + (digest.cue ? ' · cue' : '');
  $('tier-now').textContent = String(digest.tier);
  $('state-hz').textContent = fixed(digest.hz, 1) + ' Hz';
  $('state-loss').textContent = formatPercent(digest.lossPct);
  $('state-latency').textContent = digest.latencyMs ? formatMs(digest.latencyMs.p95) : '–';
  markCurrent();

  const head = digest.head || [0, 0, 0];
  model.trail.add(head[0], head[2]);
  model.dirtyMap = true;
}

function markCurrent() {
  const space = model.digest ? model.digest.space : -1;
  for (const button of $('space-buttons').children) button.classList.toggle('current', Number(button.dataset.space) === space);
  const pin = model.views.get('thermal.pinTier');
  const pinned = pin ? pin.control.val : null;
  for (const button of $('tier-buttons').children) button.classList.toggle('current', pinned !== null && Number(button.dataset.tier) === pinned);
}

// ---- tuner -----------------------------------------------------------------------------------------

function onManifest(frame) {
  const groups = buildGroups(frame.params);
  const signature = JSON.stringify(groups.map((g) => [g.key, g.active,
    g.controls.map((c) => [c.id, c.name, c.kind, c.min, c.max, c.def, c.onRelease, c.names])]));
  if (signature !== model.signature) {
    model.signature = signature;
    model.groups = groups;
    buildTuner();
  } else {
    // The same controls with fresh values: a reconnect, or a preset loaded.
    for (const group of groups) {
      for (const fresh of group.controls) {
        const view = model.views.get(fresh.name);
        if (!view) continue;
        view.control.val = fresh.val;
        if (view.pendingAt === null) showApplied(view, '');
      }
    }
  }
  if (typeof frame.locked === 'boolean') setLocked(frame.locked);
  const count = model.views.size;
  $('tuner-hint').textContent = count === 0
    ? 'Waiting for the phone\'s parameter manifest.'
    : count + ' parameters from the phone' + (model.locked ? ' · demo lock is on: the phone ignores changes' : '');
  markCurrent();
  renderDrive();
}

function buildTuner() {
  const host = $('tuner-groups');
  host.textContent = '';
  model.views.clear();
  for (const group of model.groups) {
    const details = document.createElement('details');
    details.className = 'group' + (group.active ? '' : ' inactive');
    details.open = model.userOpen.has(group.key) ? model.userOpen.get(group.key) : group.active;
    const summary = document.createElement('summary');
    summary.append(group.title);
    const count = document.createElement('span');
    count.className = 'count';
    count.textContent = group.controls.length + (group.controls.length === 1 ? ' parameter' : ' parameters');
    summary.append(count);
    if (!group.active) {
      const tag = document.createElement('span');
      tag.className = 'tag';
      tag.textContent = 'inactive';
      tag.title = 'This space is not entered. Its values are kept and can still be set.';
      summary.append(tag);
    }
    // The fold the operator chooses survives a rebuild of the tuner.
    summary.addEventListener('click', () => model.userOpen.set(group.key, !details.open));
    details.append(summary);

    const body = document.createElement('div');
    body.className = 'group-body';
    for (const control of group.controls) body.append(buildRow(control));
    details.append(body);
    host.append(details);
  }
  applyLock();
}

function buildRow(control) {
  const row = document.createElement('div');
  row.className = 'param';

  const name = document.createElement('div');
  name.className = 'param-name';
  name.textContent = control.name;
  if (control.onRelease) {
    const tag = document.createElement('span');
    tag.className = 'tag';
    tag.textContent = 'on release';
    tag.title = 'Rebuilds geometry: sent once, when the slider is let go';
    name.append(tag);
  }

  const input = document.createElement('div');
  input.className = 'param-input';

  const echo = document.createElement('div');
  echo.className = 'param-echo';
  const applied = document.createElement('span');
  applied.className = 'applied';
  const note = document.createElement('span');
  note.className = 'note';
  echo.append(applied, note);

  const reset = document.createElement('button');
  reset.type = 'button';
  reset.className = 'reset quiet';
  reset.textContent = '↺';
  reset.title = 'Reset to the default, ' + displayValue(control, control.def);
  reset.setAttribute('aria-label', 'Reset ' + control.name + ' to its default');

  const view = {
    control, row, applied, note, reset, slider: null, number: null, toggle: null, select: null,
    throttle: new SendThrottle(), timer: 0, editing: false, pendingAt: null, inputs: []
  };

  if (control.widget === 'slider') {
    const slider = document.createElement('input');
    slider.type = 'range';
    slider.min = String(control.min);
    slider.max = String(control.max);
    slider.step = String(control.step);
    slider.setAttribute('aria-label', control.name);
    const number = document.createElement('input');
    number.type = 'number';
    number.min = String(control.min);
    number.max = String(control.max);
    number.step = String(control.step);
    number.setAttribute('aria-label', control.name + ', value');
    const range = document.createElement('span');
    range.className = 'param-range';
    range.textContent = displayValue(control, control.min) + ' … ' + displayValue(control, control.max);
    input.append(slider, number, range);
    view.slider = slider;
    view.number = number;
    view.inputs.push(slider, number);

    slider.addEventListener('input', () => {
      view.editing = true;
      const value = requestValue(control, Number(slider.value));
      number.value = displayValue(control, value);
      if (control.onRelease) return;   // sent once, on release
      const now = performance.now();
      if (view.throttle.offer(value, now)) sendParam(view, value);
      else if (!view.timer) {
        view.timer = setTimeout(() => {
          view.timer = 0;
          const waiting = view.throttle.take(performance.now(), true);
          if (waiting !== null) sendParam(view, waiting);
        }, view.throttle.intervalMs);
      }
    });
    slider.addEventListener('change', () => {
      view.editing = false;
      clearTimeout(view.timer);
      view.timer = 0;
      view.throttle.take(performance.now(), true);
      sendParam(view, requestValue(control, Number(slider.value)));
    });
    number.addEventListener('focus', () => { view.editing = true; });
    number.addEventListener('blur', () => { view.editing = false; });
    number.addEventListener('change', () => {
      const value = requestValue(control, number.value);
      if (value === null) {
        showApplied(view, '');   // not a number: back to the applied value
        return;
      }
      number.value = displayValue(control, value);
      slider.value = String(value);
      sendParam(view, value);
    });
  } else if (control.widget === 'toggle') {
    const label = document.createElement('label');
    label.className = 'switch';
    const toggle = document.createElement('input');
    toggle.type = 'checkbox';
    toggle.setAttribute('aria-label', control.name);
    const track = document.createElement('span');
    track.className = 'track';
    label.append(toggle, track);
    input.append(label);
    view.toggle = toggle;
    view.inputs.push(toggle);
    toggle.addEventListener('change', () => sendParam(view, toggle.checked ? 1 : 0));
  } else {
    const select = document.createElement('select');
    select.setAttribute('aria-label', control.name);
    for (const option of control.options) {
      const el = document.createElement('option');
      el.value = String(option.value);
      el.textContent = option.label;
      select.append(el);
    }
    input.append(select);
    view.select = select;
    view.inputs.push(select);
    select.addEventListener('change', () => sendParam(view, Number(select.value)));
  }

  reset.addEventListener('click', () => sendParam(view, control.def));
  view.inputs.push(reset);

  row.append(name, input, echo, reset);
  model.views.set(control.name, view);
  showApplied(view, '');
  return row;
}

// Writes the applied value into the row and, unless the operator is holding the control, into the control.
function showApplied(view, noteText) {
  const control = view.control;
  view.applied.textContent = displayValue(control);
  view.note.textContent = noteText;
  if (view.editing) return;
  if (view.slider) {
    view.slider.value = String(control.val);
    view.number.value = displayValue(control);
  }
  if (view.toggle) view.toggle.checked = control.val >= 0.5;
  if (view.select) view.select.value = String(control.val);
}

function sendParam(view, value) {
  const control = view.control;
  if (value === null) return;
  // The lock is not a ParamSet (contract 1.5): it travels as the lock command.
  if (control.viaLock) {
    runCommand('lock', { on: value >= 0.5 });
    return;
  }
  if (!feed.send({ t: 'param', name: control.name, value })) {
    view.row.classList.remove('pending', 'echoed');
    view.note.textContent = 'not sent: no Hub';
    return;
  }
  const stamp = performance.now();
  view.pendingAt = stamp;
  view.row.classList.add('pending');
  view.row.classList.remove('echoed');
  view.note.textContent = 'sent ' + displayValue(control, value);
  setTimeout(() => {
    if (view.pendingAt !== stamp) return;
    // The phone echoes a change. No echo: the value was already in force, or the phone is not listening.
    view.pendingAt = null;
    view.row.classList.remove('pending');
    showApplied(view, model.link && model.link.phone && model.link.phone.state === 'connected' ? 'no change on the phone' : 'no echo: phone not connected');
  }, ECHO_WAIT_MS);
}

function onEcho(event) {
  for (const echo of readEcho(event)) {
    const control = findControl(model.groups, echo);
    if (!control) continue;
    const view = model.views.get(control.name);
    const value = requestValue(control, echo.value);
    if (value !== null) control.val = value;
    if (!view) continue;
    if (view.pendingAt !== null) {
      const ms = performance.now() - view.pendingAt;
      view.pendingAt = null;
      view.row.classList.remove('pending');
      view.row.classList.add('echoed');
      showApplied(view, 'echo ' + formatMs(ms));
    } else {
      view.row.classList.remove('echoed');
      showApplied(view, 'changed on the phone');
    }
  }
  markCurrent();
  renderDrive();
}

function applyLock() {
  for (const view of model.views.values()) {
    // Under the lock the phone ignores ParamSet; the lock's own switch stays live.
    const disabled = model.locked && !view.control.viaLock;
    for (const input of view.inputs) input.disabled = disabled;
  }
  for (const id of ['preset-load']) $(id).disabled = model.locked;
}

// ---- presets ---------------------------------------------------------------------------------------

async function refreshPresets(select) {
  const reply = await api('GET', '/api/presets');
  if (!reply.ok || !reply.json) return;
  const list = $('preset-list');
  const keep = select || list.value;
  list.textContent = '';
  for (const preset of reply.json.presets || []) {
    const option = document.createElement('option');
    option.value = preset.name;
    option.textContent = preset.name + '  (' + preset.count + ' values)';
    option.title = 'Saved ' + preset.modified;
    list.append(option);
  }
  if (keep) list.value = keep;
}

async function presetAction(action) {
  const status = $('preset-status');
  if (action === 'save') {
    const name = $('preset-name').value.trim();
    if (!isPresetName(name)) {
      setStatus(status, 'A name is 1 to 40 letters, digits, "-" or "_".', 'bad');
      return;
    }
    // An empty body saves the Hub's own record of every current value: the values the phone holds.
    const reply = await api('POST', '/api/presets/' + encodeURIComponent(name));
    setStatus(status, reply.ok ? 'Saved "' + name + '".' : 'Not saved: ' + reply.error, reply.ok ? 'good' : 'bad');
    if (reply.ok) refreshPresets(name);
    return;
  }
  const name = $('preset-list').value;
  if (!name) {
    setStatus(status, 'Choose a preset first.', 'bad');
    return;
  }
  if (action === 'load') {
    const reply = await api('POST', '/api/presets/' + encodeURIComponent(name) + '/load');
    setStatus(status, reply.ok ? 'Loaded "' + name + '": all values sent in one message.' : 'Not loaded: ' + reply.error, reply.ok ? 'good' : 'bad');
  } else if (action === 'bake') {
    const reply = await api('POST', '/api/presets/' + encodeURIComponent(name) + '/bake');
    setStatus(status, reply.ok ? 'Baked "' + name + '" into the project defaults.' : 'Not baked: ' + reply.error, reply.ok ? 'good' : 'bad');
  } else if (action === 'delete') {
    const reply = await api('DELETE', '/api/presets/' + encodeURIComponent(name));
    setStatus(status, reply.ok ? 'Deleted "' + name + '".' : 'Not deleted: ' + reply.error, reply.ok ? 'good' : 'bad');
    if (reply.ok) refreshPresets();
  }
}

// ---- operator --------------------------------------------------------------------------------------

function buildOperator() {
  const spaces = $('space-buttons');
  for (let id = 0; id < SPACE_COUNT; id++) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.space = String(id);
    button.textContent = spaceLabel(id);
    button.addEventListener('click', () => runCommand('space', { space: id }));
    spaces.append(button);
  }
  const tiers = $('tier-buttons');
  for (let tier = -1; tier <= 4; tier++) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.tier = String(tier);
    button.textContent = tier === -1 ? 'Auto' : 'Tier ' + tier;
    const small = document.createElement('small');
    small.textContent = tier === -1 ? 'thermal ladder' : tierLabel(tier).replace(/^tier \d \(|\)$/g, '');
    button.append(small);
    button.addEventListener('click', () => runCommand('quality', { tier }));
    tiers.append(button);
  }
  $('cmd-home').addEventListener('click', () => runCommand('home'));
  $('cmd-recentre').addEventListener('click', () => runCommand('recentre'));
  $('cmd-lock').addEventListener('change', (e) => runCommand('lock', { on: e.target.checked }));
  $('cmd-hands').addEventListener('change', (e) => runCommand('hands', { on: e.target.checked }));

  const stop = $('stop');
  stop.addEventListener('click', fireStop);
  // The space bar is STOP anywhere on the page except while typing or on another control.
  document.addEventListener('keydown', (e) => {
    if (e.code !== 'Space' || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
    const tag = e.target && e.target.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || tag === 'BUTTON' || tag === 'A' || tag === 'SUMMARY') return;
    e.preventDefault();
    fireStop();
  });

  $('fms-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const score = comfortScore($('fms-score').value);
    if (score === null) {
      setStatus($('fms-status'), 'A whole number from 0 to 20.', 'bad');
      return;
    }
    if (!feed.send({ t: 'fms', score })) {
      setStatus($('fms-status'), 'Not logged: no Hub.', 'bad');
      return;
    }
    model.fmsPending = score;
    setStatus($('fms-status'), 'Sending ' + score + '…', '');
  });
  $('seq-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const score = seqScore($('seq-score').value);
    if (score === null) {
      setStatus($('seq-status'), 'A whole number from 1 to 7.', 'bad');
      return;
    }
    if (!feed.send({ t: 'seq', score })) {
      setStatus($('seq-status'), 'Not logged: no Hub.', 'bad');
      return;
    }
    model.seqPending = score;
    setStatus($('seq-status'), 'Sending ' + score + '…', '');
  });
  $('note-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const text = $('note-text').value.trim();
    if (text === '') return;
    if (!feed.send({ t: 'note', text })) {
      setStatus($('note-status'), 'Not logged: no Hub.', 'bad');
      return;
    }
    model.notePending = true;
    setStatus($('note-status'), 'Sending…', '');
  });
}

let stopTimer = 0;
function fireStop() {
  const stop = $('stop');
  stop.classList.add('fired');
  clearTimeout(stopTimer);
  stopTimer = setTimeout(() => stop.classList.remove('fired'), 600);
  runCommand('cue');
}

async function runCommand(name, args = {}) {
  const request = requestFor(name, args);
  if (!request) return;
  const entry = model.tracker.sent(name, args, performance.now());
  renderCommands();
  const reply = await api('POST', request.path, request.body);
  model.tracker.hubAnswered(entry, reply.ok, performance.now(), reply.error);
  renderCommands();
}

const ACK_ELEMENTS = { space: 'ack-space', home: 'ack-home', recentre: 'ack-recentre', quality: 'ack-quality', lock: 'ack-lock', hands: 'ack-hands', cue: 'stop-status' };

function toneOf(entry) {
  if (entry.hub === 'refused' || entry.phone === 'timeout') return 'bad';
  // The lock is the Hub's state too and succeeds with no phone connected.
  if (entry.phone === 'ack') return 'good';
  return '';
}

function renderCommands() {
  for (const name of Object.keys(ACK_ELEMENTS)) {
    const entry = model.tracker.latest(name);
    const el = $(ACK_ELEMENTS[name]);
    if (!entry) continue;
    if (name === 'cue') el.textContent = statusText(entry);
    else setStatus(el, entry.label + ': ' + statusText(entry), toneOf(entry));
  }
  const log = $('command-log');
  log.textContent = '';
  for (const entry of model.tracker.entries) {
    const li = document.createElement('li');
    const what = document.createElement('span');
    what.className = 'what';
    what.textContent = entry.label;
    const result = document.createElement('span');
    result.className = 'result ' + toneOf(entry);
    result.textContent = statusText(entry);
    li.append(what, result);
    log.append(li);
  }
}

function logEvent(event) {
  const log = $('event-log');
  const first = log.firstElementChild;
  if (first && first.classList.contains('empty')) first.remove();
  const li = document.createElement('li');
  const when = document.createElement('span');
  when.className = 'when';
  when.textContent = clockTime() + ' ' + (event.scene || '') + ' ';
  const ev = document.createElement('span');
  ev.className = 'ev';
  ev.textContent = event.ev || 'log';
  let detail = event.data !== undefined ? JSON.stringify(event.data) : event.msg || '';
  if (detail === '{}') detail = '';
  if (detail.length > 180) detail = detail.slice(0, 180) + '…';
  li.append(when, ev, ' ' + detail);
  log.prepend(li);
  while (log.children.length > 80) log.lastElementChild.remove();
}

// The hands tile: the gate's state, its rate and the hands tracked, folded from the hands.* events (design 8.1).
const handsState = { on: false, reason: '', hz: 0, L: false, R: false };

function renderHands() {
  const h = handsState;
  const tracked = (h.L ? 'L' : '') + (h.R ? (h.L ? ' + ' : '') + 'R' : '') || 'none';
  $('hands-status').textContent = h.on
    ? 'Hands active · ' + Math.round(h.hz) + ' Hz · tracked: ' + tracked
    : 'Hands off' + (h.reason ? ' (' + h.reason + ')' : '') + ' · tracked: ' + tracked;
  $('cmd-hands').checked = h.on;
}

function onHandsEvent(event) {
  const d = event.data || {};
  switch (event.ev) {
    case 'hands.on': handsState.on = true; handsState.reason = ''; if (typeof d.hz === 'number') handsState.hz = d.hz; break;
    case 'hands.off': handsState.on = false; handsState.reason = d.reason || ''; handsState.L = handsState.R = false; break;
    case 'hands.rate': if (typeof d.hz === 'number') handsState.hz = d.hz; break;
    case 'hands.enter': if (d.hand === 'L' || d.hand === 'R') handsState[d.hand] = true; break;
    case 'hands.exit': if (d.hand === 'L' || d.hand === 'R') handsState[d.hand] = false; break;
    case 'hands.stats': if (typeof d.hz === 'number') handsState.hz = d.hz; break;
    default: return;
  }
  renderHands();
}

function onEvent(event) {
  logEvent(event);
  if (typeof event.ev === 'string' && event.ev.startsWith('hands.')) onHandsEvent(event);
  const now = performance.now();
  if (event.ev === 'cmd' || event.ev === 'op') {
    if (model.tracker.phoneEvent(event, now)) renderCommands();
  } else if (event.ev === 'param' || event.ev === 'param.set') {
    onEcho(event);
  } else if (event.ev === 'param.ignored') {
    showError('The phone ignored a parameter change (' + ((event.data && event.data.reason) || 'unknown reason') + ').');
  } else if (event.ev === 'space.refused') {
    showError('The phone refused the switch to ' + ((event.data && event.data.to) || 'that space') + ': ' + ((event.data && event.data.reason) || 'refused') + '.');
  } else if (event.ev === 'seq' && event.data) {
    if (model.seqPending === event.data.score) {
      model.seqPending = null;
      $('seq-score').value = '';
    }
    setStatus($('seq-status'), 'Logged ' + event.data.score + ' at ' + clockTime() + '.', 'good');
  } else if (event.ev === 'pg.fmsAlert') {
    // The Hub writes it right after the fms line it judged against pg.fmsStop; it stays until a lower entry.
    const alert = $('fms-alert');
    alert.textContent = fmsAlertText(event.data);
    alert.hidden = false;
    showError(alert.textContent);
  } else if (event.ev === 'fms' && event.data) {
    $('fms-alert').hidden = true;
    if (model.fmsPending === event.data.score) {
      model.fmsPending = null;
      $('fms-score').value = '';
    }
    setStatus($('fms-status'), 'Logged ' + event.data.score + ' at ' + clockTime() + '.', 'good');
  } else if (event.ev === 'note') {
    if (model.notePending) {
      model.notePending = false;
      $('note-text').value = '';
    }
    setStatus($('note-status'), 'Logged at ' + clockTime() + '.', 'good');
  } else if (event.ev === 'asset') {
    if (model.jobs.onEvent(event, now)) renderJobs();
  }
}

// ---- dashboard -------------------------------------------------------------------------------------

const THERMAL_VARS = ['--ok', '--warn', '--strong', '--danger'];

const TILES = [
  ['fps', 'fps', (r) => fixed(r.fps, 1), '', (r) => (r.fps < 50 ? 'bad' : r.fps < 58 ? 'warn' : '')],
  ['p50', 'frame p50', (r) => fixed(r.p50, 1), 'ms'],
  ['p95', 'frame p95', (r) => fixed(r.p95, 1), 'ms', (r) => (r.p95 > 33.8 ? 'bad' : r.p95 > 17.2 ? 'warn' : '')],
  ['p99', 'frame p99', (r) => fixed(r.p99, 1), 'ms'],
  ['max', 'frame max', (r) => fixed(r.max, 1), 'ms'],
  ['over17_2', 'over 17.2 ms', (r) => fixed(r.over17_2, 1), '%'],
  ['over33_8', 'over 33.8 ms', (r) => fixed(r.over33_8, 1), '%', (r) => (r.over33_8 > 0 ? 'warn' : '')],
  ['cpuMainP95', 'CPU main p95', (r) => fixed(r.cpuMainP95, 1), 'ms'],
  ['cpuRenderP95', 'CPU render p95', (r) => fixed(r.cpuRenderP95, 1), 'ms'],
  ['gpuP95', 'GPU p95', (r) => fixed(r.gpuP95, 1), 'ms', (r) => (r.gpuP95 > 12 ? 'bad' : r.gpuP95 > 9 ? 'warn' : '')],
  ['drawCalls', 'draw calls', (r) => formatCount(r.drawCalls), ''],
  ['setPass', 'set-pass calls', (r) => formatCount(r.setPass), ''],
  ['trisK', 'triangles', (r) => formatCount(r.trisK), 'k'],
  ['memMB', 'memory', (r) => formatCount(r.memMB), 'MB'],
  ['thermal', 'thermal', (r) => thermalName(r.thermal), '', (r) => (r.thermal >= 2 ? 'bad' : r.thermal === 1 ? 'warn' : '')],
  ['tier', 'quality tier', (r) => String(r.tier), ''],
  ['renderScale', 'render scale', (r) => fixed(r.renderScale, 2), ''],
  ['msaa', 'MSAA', (r) => (r.msaa <= 1 ? 'off' : r.msaa + '×'), ''],
  ['battery', 'battery', (r) => String(r.battery), '%', (r) => (r.battery < 20 ? 'warn' : '')],
  ['tracking', 'tracking', (r) => ['normal', 'limited', 'lost'][r.tracking] || '?', '', (r) => (r.tracking > 0 ? 'warn' : '')],
  ['stateHz', 'State sent', (r) => String(r.stateHz), 'Hz'],
  ['rttMs', 'RTT (phone)', (r) => String(r.rttMs), 'ms'],
  ['lossPermille', 'loss (phone)', (r) => fixed(r.lossPermille / 10, 1), '%'],
  ['bumpMax', 'bump max', (r) => bumpName(r.bumpMax), '', (r) => (r.bumpMax >= 2 ? 'warn' : '')],
  ['cueCount', 'cues', (r) => String(r.cueCount), ''],
  ['gcAllocB', 'GC alloc', (r) => formatCount(r.gcAllocB), 'B', (r) => (r.gcAllocB > 0 ? 'warn' : '')],
  ['visibleTiles', 'visible tiles', (r) => String(r.visibleTiles), ''],
  ['copies', 'copies', (r) => String(r.copies), ''],
  ['assetsPlaced', 'assets placed', (r) => String(r.assetsPlaced), ''],
  ['assetsPending', 'assets pending', (r) => String(r.assetsPending), '']
];

const tileViews = new Map();

function buildTiles() {
  const host = $('tiles');
  for (const [key, label, , unit] of TILES) {
    const tile = document.createElement('div');
    tile.className = 'tile';
    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = label;
    const value = document.createElement('div');
    value.className = 'value';
    const number = document.createElement('span');
    number.textContent = '–';
    value.append(number);
    if (unit) {
      const small = document.createElement('small');
      small.textContent = unit;
      value.append(small);
    }
    tile.append(name, value);
    host.append(tile);
    tileViews.set(key, { tile, number });
  }
}

function renderTelemetry() {
  const buffer = model.telemetry;
  const row = buffer.last;
  if (!row) {
    $('rows-summary').textContent = 'No telemetry yet.';
    for (const view of tileViews.values()) view.number.textContent = '–';
    model.dirtyCharts = true;
    return;
  }
  for (const [key, , format, , tone] of TILES) {
    const view = tileViews.get(key);
    view.number.textContent = format(row);
    const t = tone ? tone(row) : '';
    view.tile.classList.toggle('warn', t === 'warn');
    view.tile.classList.toggle('bad', t === 'bad');
  }
  const gaps = buffer.gaps();
  $('rows-summary').textContent = buffer.length + ' rows shown · ' +
    (gaps.length === 0 ? 'no gap over 2 s' : gaps.length + (gaps.length === 1 ? ' gap' : ' gaps') + ' over 2 s') + ' ·';
  $('lg-p50').textContent = fixed(row.p50, 1);
  $('lg-p95').textContent = fixed(row.p95, 1);
  $('lg-p99').textContent = fixed(row.p99, 1);
  $('lg-cpuMain').textContent = fixed(row.cpuMainP95, 1);
  $('lg-cpuRender').textContent = fixed(row.cpuRenderP95, 1);
  $('lg-gpu').textContent = fixed(row.gpuP95, 1);
  $('lg-fps').textContent = fixed(row.fps, 1);
  $('lg-draw').textContent = formatCount(row.drawCalls);
  $('lg-tris').textContent = formatCount(row.trisK) + ' k';
  $('lg-mem').textContent = formatCount(row.memMB) + ' MB';
  $('lg-thermal').textContent = thermalName(row.thermal);
  $('lg-tier').textContent = String(row.tier);
  $('thermal-now').textContent = thermalName(row.thermal);

  const advice = thermalAdvice(row.thermal);
  $('banner-thermal').hidden = advice === '';
  $('banner-thermal').className = 'banner ' + (row.thermal >= 2 ? 'danger' : 'warn');
  $('banner-thermal').textContent = 'Thermal state ' + thermalName(row.thermal) + ': ' + advice + '.';
  model.dirtyCharts = true;
}

function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

function drawCharts() {
  model.dirtyCharts = false;
  if (model.page !== 'dashboard') return;
  const b = model.telemetry;
  const seconds = b.seconds();
  const gaps = b.gaps();
  const s1 = cssVar('--s1');
  drawLines($('chart-frame'), {
    seconds, gaps, floor: 20, limit: { value: 16.67, label: '16.7 ms · 60 fps' },
    series: [{ values: b.series('p50'), color: s1 }, { values: b.series('p95'), color: cssVar('--s2') }, { values: b.series('p99'), color: cssVar('--s3') }]
  });
  drawLines($('chart-cpu'), {
    seconds, gaps, floor: 10,
    series: [{ values: b.series('cpuMainP95'), color: s1 }, { values: b.series('cpuRenderP95'), color: cssVar('--s4') }, { values: b.series('gpuP95'), color: cssVar('--s5') }]
  });
  drawLines($('chart-fps'), { seconds, gaps, yMax: 80, limit: { value: 60, label: '60' }, series: [{ values: b.series('fps'), color: s1 }] });
  drawLines($('chart-draw'), { seconds, gaps, floor: 100, series: [{ values: b.series('drawCalls'), color: s1 }] });
  drawLines($('chart-tris'), { seconds, gaps, floor: 100, series: [{ values: b.series('trisK'), color: s1 }] });
  drawLines($('chart-mem'), { seconds, gaps, floor: 500, series: [{ values: b.series('memMB'), color: s1 }] });
  drawTimeline($('chart-thermal'), { seconds, gaps, values: b.series('thermal'), colors: THERMAL_VARS.map(cssVar) });
  drawLines($('chart-tier'), { seconds, gaps, yMax: 4, step: true, series: [{ values: b.series('tier'), color: s1 }] });
}

// ---- map -------------------------------------------------------------------------------------------

function paintMap() {
  model.dirtyMap = false;
  const d = model.digest;
  const online = model.link && model.link.phone && model.link.phone.state !== 'offline';
  const walker = d && online ? { x: d.head[0], z: d.head[2], yawDeg: d.yawDeg } : null;
  const obstacle = walker ? nearestObstacle(model.room, walker.x, walker.z) : null;

  if (d) {
    $('map-space').textContent = spaceLabel(d.space);
    $('map-cell').textContent = cellLabel(d.space, d.tile, d.sheet, d.p);
    $('map-walker').textContent = 'x ' + fixed(d.head[0], 2) + ' m · z ' + fixed(d.head[2], 2) + ' m · eye ' + fixed(d.head[1], 2) + ' m';
    $('map-heading').textContent = fixed(d.yawDeg, 0) + '°';
    $('map-tracking').textContent = (['normal', 'limited', 'lost'][d.tracking] || '?') + (d.jump ? ' · pose jump rejected' : '');
    $('map-bump').textContent = bumpName(d.bump) + (d.cue ? ' · cue showing' : '');
    $('map-nearest').textContent = obstacle
      ? (obstacle.kind === 'wall' ? 'wall' : 'object ' + obstacle.id) + ' ' + fixed(obstacle.distance, 2) + ' m, ' + relativeSide(d.yawDeg, obstacle.dirX, obstacle.dirZ)
      : '–';
  }
  const room = model.room;
  $('map-room').textContent = room
    ? (room.rect && room.rect.wf > 0 ? 'free ' + fixed(room.rect.wf, 2) + ' × ' + fixed(room.rect.df, 2) + ' m · ' : '') +
      (room.boxes ? room.boxes.length : 0) + (room.boxes && room.boxes.length === 1 ? ' object' : ' objects') + ' · ceiling ' + fixed(room.height, 2) + ' m'
    : 'no outline yet';

  if (model.page !== 'map') return;
  const boxState = new Map();
  for (const row of model.jobs.list()) boxState.set(row.boxId, row.state);
  drawMap($('map-canvas'), { room, trail: model.trail, walker, bump: d ? d.bump : 0, cue: d ? d.cue : false, obstacle, boxState });
}

// ---- drive: the Map page's walk keys ---------------------------------------------------------------

// pose.source on the phone (AppParams): the walker is the head the keys move.
const POSE_SOURCE = 'pose.source';
const SOURCE_TRACKED = 0;
const SOURCE_WALKER = 2;
const SOURCE_NAMES = ['tracked', 'replay', 'walker'];

/** The phone's pose.source as it last reported it, or null before its manifest. */
function poseSource() {
  const view = model.views.get(POSE_SOURCE);
  return view ? view.control.val : null;
}

function setPoseSource(value) {
  const view = model.views.get(POSE_SOURCE);
  if (view) sendParam(view, value);
}

function setDriving(on) {
  $('drive-on').checked = on;
  if (on === model.driving) return;
  model.driving = on;
  $('drive').classList.toggle('on', on);
  if (on) {
    const before = poseSource();
    model.driveRestore = before !== null && before !== SOURCE_WALKER ? before : SOURCE_TRACKED;
    setPoseSource(SOURCE_WALKER);
  } else {
    stopWalking();
    if (poseSource() === SOURCE_WALKER) setPoseSource(model.driveRestore);
  }
  renderDrive();
}

/** Lets every key go; the phone gets one stop if the walker was moving. */
function stopWalking() {
  model.walkKeys.clear();
  tickWalk();
  renderKeys();
}

// Called every 50 ms and on every key: the sender decides whether anything goes out.
function tickWalk() {
  const keys = model.walkKeys;
  if (!model.driving) keys.clear();
  const input = model.walkSender.offer(keys.input(), performance.now());
  if (input) feed.send({ t: 'op', cmd: 'walk', args: input });
}

function renderKeys() {
  const keys = model.walkKeys;
  for (const kbd of document.querySelectorAll('#drive kbd[data-key]')) {
    const down = kbd.dataset.key.split(' ').some((code) => (code === 'Shift' ? keys.fast : keys.held.has(code)));
    kbd.classList.toggle('down', down);
  }
}

function renderDrive() {
  const status = $('drive-status');
  const source = poseSource();
  const name = source === null ? '' : SOURCE_NAMES[source] || String(source);
  if (!model.driving) {
    setStatus(status, source === SOURCE_WALKER
      ? 'Off. The phone\'s head is the walker (pose.source), and no keys drive it.'
      : 'Off. Switching on sets pose.source to walker: the phone\'s view then follows these keys instead of its own tracking.', '');
  } else if (source === null) {
    setStatus(status, 'Waiting for the phone\'s parameters: pose.source cannot be set yet.', 'bad');
  } else if (source !== SOURCE_WALKER) {
    setStatus(status, model.locked
      ? 'The demo lock is on: the phone keeps pose.source at ' + name + '. Unlock it on the Operator page.'
      : 'Asking the phone for the walker (pose.source is ' + name + ')…', model.locked ? 'bad' : '');
  } else {
    setStatus(status, 'Driving: the phone\'s camera follows the keys anywhere on this page. Switching off goes back to ' +
      (SOURCE_NAMES[model.driveRestore] || 'tracked') + '.', 'good');
  }
}

function buildDrive() {
  $('drive-on').addEventListener('change', (e) => setDriving(e.target.checked));
  document.addEventListener('keydown', (e) => {
    if (!model.driving || model.page !== 'map' || e.ctrlKey || e.metaKey || e.altKey) return;
    const target = e.target;
    const tag = target && target.tagName;
    if ((tag === 'INPUT' && target.type !== 'checkbox') || tag === 'TEXTAREA' || tag === 'SELECT') return;
    if (model.walkKeys.down(e.code)) e.preventDefault();
    if (!e.repeat) tickWalk();
    renderKeys();
  });
  document.addEventListener('keyup', (e) => {
    model.walkKeys.up(e.code);
    tickWalk();
    renderKeys();
  });
  // A key let go while the window is not looking would otherwise walk on until the phone's hold runs out.
  window.addEventListener('blur', stopWalking);
  setInterval(tickWalk, 50);
}

// ---- view: what the phone shows --------------------------------------------------------------------

// The stream is open only while the page is shown and the window visible: the Hub asks the phone to send
// its picture while anyone watches, and every frame costs the phone GPU time and heat.
let viewTimer = 0;

function showView() {
  const img = $('view-img');
  const wanted = model.page === 'view' && document.visibilityState === 'visible';
  const open = img.dataset.open === '1';
  if (wanted === open) return;
  if (wanted) {
    img.dataset.open = '1';
    img.src = '/api/view/stream?t=' + Date.now();
    pollView();
    viewTimer = setInterval(pollView, 1000);
  } else {
    img.dataset.open = '';
    img.removeAttribute('src');   // closes the stream; the last viewer leaving tells the phone to stop
    clearInterval(viewTimer);
    viewTimer = 0;
  }
}

async function pollView() {
  const reply = await api('GET', '/api/view');
  if (!reply.ok || !reply.json) {
    setStatus($('view-status'), 'The Hub does not answer.', 'bad');
    return;
  }
  const v = reply.json;
  const online = model.link && model.link.phone && model.link.phone.state !== 'offline';
  const fresh = v.ageMs >= 0 && v.ageMs < 2000;
  const empty = $('view-empty');
  empty.hidden = fresh;
  if (fresh) {
    setStatus($('view-status'), fixed(v.fps, 1) + ' fps · ' + Math.round(v.bytes / 1024) + ' KB a frame · ' +
      (v.mode === 'headset' ? 'headset view, left eye' : v.mode === 'hand' ? 'in-hand view' : 'phone view') +
      ' · ' + (v.viewers === 1 ? '1 viewer' : v.viewers + ' viewers'), 'good');
  } else {
    const why = !online ? 'The phone is not connected.'
      : v.frames === 0 ? 'Asked the phone for its picture; nothing yet. A build older than this page sends none.'
        : 'The phone stopped sending ' + fixed(v.ageMs / 1000, 0) + ' s ago.';
    setStatus($('view-status'), why, v.frames === 0 || !online ? '' : 'bad');
    empty.hidden = v.frames > 0;   // a stopped stream keeps its last picture; the status line says it is old
  }
}

// ---- assets ----------------------------------------------------------------------------------------

function renderJobs() {
  const body = $('job-rows');
  const rows = model.jobs.list();
  if (rows.length === 0) return;
  body.textContent = '';
  for (const row of rows) {
    const tr = document.createElement('tr');
    const cell = (text, className) => {
      const td = document.createElement('td');
      if (className) td.className = className;
      td.textContent = text;
      tr.append(td);
      return td;
    };
    cell(String(row.boxId), 'num');
    cell(row.category || '–');
    const state = cell('');
    const pill = document.createElement('span');
    pill.className = 'pill ' + phaseOf(row.state);
    pill.textContent = row.state;
    state.append(pill);
    cell(row.source || '–');
    cell(row.queuePos === null || phaseOf(row.state) !== 'working' ? '–' : String(row.queuePos), 'num');
    cell(row.tris === null ? '–' : formatCount(row.tris), 'num');
    cell(row.loadMs === null ? '–' : formatMs(row.loadMs), 'num');
    cell(row.jobId || '–', 'mono');
    cell(row.error || (row.state === 'placeholder' ? 'box placeholder in the headset' : ''));
    body.append(tr);
  }
  model.dirtyMap = true;
}

// ---- pairing ---------------------------------------------------------------------------------------

async function loadPairing(ip) {
  const query = ip ? '?ip=' + encodeURIComponent(ip) : '';
  const reply = await api('GET', '/api/pair' + query);
  if (!reply.ok || !reply.json) return;
  const pair = reply.json;
  $('pair-address').textContent = pair.address;
  $('pair-port').textContent = String(pair.udpPort);
  $('pair-url').textContent = pair.url;
  $('pair-qr').src = '/pair/qr.svg?ip=' + encodeURIComponent(pair.address);
  const host = $('pair-candidates');
  host.textContent = '';
  const others = (pair.candidates || []).filter((c) => c !== pair.address);
  if (others.length === 0) {
    const none = document.createElement('span');
    none.className = 'hint';
    none.textContent = 'None.';
    host.append(none);
  }
  for (const candidate of others) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'mono';
    button.textContent = candidate;
    button.addEventListener('click', () => loadPairing(candidate));
    host.append(button);
  }
}

// ---- the feed --------------------------------------------------------------------------------------

function onFrame(frame) {
  switch (frame.t) {
    case 'link': renderLink(frame); break;
    case 'state': renderState(frame); break;
    case 'manifest': onManifest(frame); break;
    case 'telemetry':
      if (model.telemetry.push(frame)) renderTelemetry();
      break;
    case 'event': onEvent(frame); break;
    case 'log': logEvent({ ev: 'log ' + (frame.level || ''), scene: '', msg: frame.msg || '' }); break;
    case 'job':
      if (model.jobs.onJob(frame, performance.now())) renderJobs();
      break;
    case 'outline':
      model.room = frame;
      model.jobs.setRoom(frame, (model.link && model.link.phone && model.link.phone.roomId) || '');
      renderJobs();
      model.dirtyMap = true;
      break;
    case 'error': showError('Hub: ' + (frame.msg || 'error')); break;
    default: break;   // pong, and frames a newer Hub may add
  }
}

function onSocket(open) {
  $('banner-console').hidden = open;
  if (!open) {
    $('phone-dot').className = 'dot';
    $('phone-state').textContent = 'unknown';
  }
}

const feed = new Feed((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws', onFrame, onSocket);

function start() {
  buildOperator();
  buildTiles();
  buildDrive();
  renderDrive();
  for (const [id, action] of [['preset-load', 'load'], ['preset-bake', 'bake'], ['preset-delete', 'delete'], ['preset-save', 'save']]) {
    $(id).addEventListener('click', () => presetAction(action));
  }
  $('trail-clear').addEventListener('click', () => {
    model.trail.clear();
    model.dirtyMap = true;
  });
  window.addEventListener('hashchange', showPage);
  document.addEventListener('visibilitychange', showView);
  window.addEventListener('resize', () => {
    model.dirtyCharts = true;
    model.dirtyMap = true;
  });
  showPage();
  feed.start();

  // Canvases are painted once per display frame at most, and only when something changed.
  const paint = () => {
    if (model.dirtyCharts) drawCharts();
    if (model.dirtyMap) paintMap();
    requestAnimationFrame(paint);
  };
  requestAnimationFrame(paint);
  setInterval(() => {
    if (model.tracker.expire(performance.now())) renderCommands();
  }, 500);
}

start();
