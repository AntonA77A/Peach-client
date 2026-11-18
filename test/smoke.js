'use strict';
// Starts Peach on a free port, runs every mod once, and checks each one logged its greeting
// through the API. Mods need the "notify" permission, so also assert that a call without
// permission is rejected.
const path = require('path');
const { ModManager } = require('../src/manager');
const { startServer } = require('../src/server');

(async () => {
  const root = path.resolve(__dirname, '..');
  const config = { port: 47900 };
  const manager = new ModManager({ root, config, version: 'test' });
  const { server } = await startServer({ manager, config });
  const expect = { 'hello-js': 'JavaScript', 'hello-ts': 'TypeScript', 'hello-py': 'Python', 'hello-ps1': 'PowerShell', 'hello-bat': 'Batch', 'hello-vbs': 'VBScript' };
  const state = manager.state.enabled;
  for (const id of Object.keys(expect)) if (manager.mods.has(id)) manager.start(id);
  await new Promise(r => setTimeout(r, 9000));

  let failed = 0;
  for (const [id, word] of Object.entries(expect)) {
    if (!manager.mods.has(id)) continue;
    const lines = (manager.logs.get(id) || []).map(l => l.m).join('\n');
    const ok = lines.includes('Hello from ' + word);
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'} ${id}${ok ? '' : '\n' + lines}`);
  }

  // permission check
  const mod = manager.mods.get('hello-js');
  const res = await fetch(manager.apiBase + '/shell.run', { method: 'POST', headers: { authorization: 'Bearer nope' }, body: '{}' });
  const bad = res.status === 401; if (!bad) failed++;
  console.log(`${bad ? 'PASS' : 'FAIL'} invalid token rejected (${res.status})`);
  const { createApi } = require('../src/api');
  let denied = false;
  try { await createApi({ manager }).call('shell.run', { command: 'cmd' }, mod); } catch (e) { denied = /lacks permission/.test(e.message); }
  if (!denied) failed++;
  console.log(`${denied ? 'PASS' : 'FAIL'} missing permission rejected`);

  await manager.stopAll();
  // clean up test state so the examples don't autostart unexpectedly
  for (const k of Object.keys(state)) delete state[k];
  manager._saveState();
  server.close();
  process.exit(failed ? 1 : 0);
})();
