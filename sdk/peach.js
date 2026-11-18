'use strict';
// Peach SDK for JavaScript / TypeScript mods.  const peach = require('peach');
const API = process.env.PEACH_API;
const TOKEN = process.env.PEACH_TOKEN;

async function call(method, params = {}) {
  if (!API) throw new Error('Not running under Peach (PEACH_API is not set)');
  const res = await fetch(`${API}/${method}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: 'Bearer ' + TOKEN },
    body: JSON.stringify(params),
  });
  const json = await res.json();
  if (!json.ok) throw new Error(json.error);
  return json.result;
}

const sleep = ms => new Promise(r => setTimeout(r, ms));
const handlers = {};
let looping = false;

// Long-poll loop; keeps the mod process alive while handlers are registered.
async function loop() {
  if (looping) return;
  looping = true;
  for (;;) {
    let events;
    try { events = await call('events.poll', { timeoutMs: 25000 }); }
    catch (e) {
      if (/invalid token|fetch failed/i.test(e.message)) process.exit(1); // Peach is gone
      await sleep(1000); continue;
    }
    for (const ev of events) {
      for (const h of handlers[ev.type] || []) {
        try { await h(ev); } catch (err) { console.error(err); }
      }
    }
  }
}

// peach.on('focus' | 'clipboard' | 'hotkey', handler)
async function on(type, fn) {
  (handlers[type] = handlers[type] || []).push(fn);
  if (type === 'focus' || type === 'clipboard') await call('events.subscribe', { events: [type] });
  loop();
}

// peach.hotkey('ctrl+alt+p', handler)  - needs the "hotkeys" permission
async function hotkey(combo, fn) {
  const r = await call('hotkey.register', { combo });
  await on('hotkey', ev => { if (ev.combo === r.combo) return fn(ev); });
  return r;
}

module.exports = {
  call, on, hotkey,
  hotkeys: { unregister: combo => call('hotkey.unregister', { combo }) },
  events: { poll: (timeoutMs = 25000) => call('events.poll', { timeoutMs }), subscribe: events => call('events.subscribe', { events }) },
  mod: process.env.PEACH_MOD,
  sleep,
  log: (...a) => call('log', { message: a.join(' ') }),
  notify: (title, message) => call('notify', { title, message }),
  systemInfo: () => call('system.info'),
  storage: {
    get: key => call('storage.get', { key }),
    set: (key, value) => call('storage.set', { key, value }),
  },
  clipboard: {
    get: () => call('clipboard.get'),
    set: text => call('clipboard.set', { text }),
  },
  windows: { list: () => call('windows.list') },
  registry: { get: (path, name) => call('registry.get', { path, name }) },
  wallpaper: { set: path => call('wallpaper.set', { path }) },
  shell: {
    run: (command, args = [], timeoutMs) => call('shell.run', { command, args, timeoutMs }),
    open: target => call('shell.open', { target }),
  },
};
