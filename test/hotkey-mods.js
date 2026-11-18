'use strict';
// Starts the three hotkey demo mods through the real manager + SDKs, presses their hotkeys
// with simulated key events, and checks the mods reacted. Clipboard is saved and restored.
const path = require('path');
const { ModManager } = require('../src/manager');
const { startServer } = require('../src/server');
const win = require('../src/windows');

const sleep = ms => new Promise(r => setTimeout(r, ms));
const press = (vkKey) => win.ps(
  'Add-Type -TypeDefinition \'using System.Runtime.InteropServices; public class K2 { [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, int e); }\';' +
  `$k=[K2]; foreach($v in 0x11,0x12,${vkKey}){ $k::keybd_event($v,0,0,0) }; Start-Sleep -Milliseconds 100; foreach($v in ${vkKey},0x12,0x11){ $k::keybd_event($v,0,2,0) }`);

(async () => {
  const root = path.resolve(__dirname, '..');
  const config = { port: 47910 };
  const manager = new ModManager({ root, config, version: 'test' });
  const { server } = await startServer({ manager, config });
  const saved = await win.clipboardGet().catch(() => '');
  const logOf = id => (manager.logs.get(id) || []).map(l => l.m).join('\n');
  const waitFor = async (id, text, ms = 15000) => { const end = Date.now() + ms; while (Date.now() < end) { if (logOf(id).includes(text)) return true; await sleep(300); } return false; };
  let failed = 0;
  const check = (ok, msg, id) => { console.log((ok ? 'PASS ' : 'FAIL ') + msg + (ok ? '' : '\n' + logOf(id))); if (!ok) failed++; };

  const nativeHotkeys = [];   // diagnostics: which hotkey ids the helper reported
  manager.hub.native.on('hotkey', id => nativeHotkeys.push(manager.hub.hotkeys.get(id)?.combo || id));
  const ids = ['hotkeys-js', 'hotkeys-py', 'hotkeys-ps1'];
  ids.forEach(id => manager.start(id));
  for (const id of ids) check(await waitFor(id, 'Bereit'), `${id} is ready (hotkey registered)`, id);

  check(await waitFor('hotkeys-js', 'Fokus:'), 'hotkeys-js received a focus event', 'hotkeys-js');

  await press(0x4A); // J
  check(await waitFor('hotkeys-js', 'Hotkey ctrl+alt+j pressed'), 'JS hotkey handler fired', 'hotkeys-js');

  await win.clipboardSet('peach loud');
  await sleep(500);
  await press(0x55); // U
  check(await waitFor('hotkeys-py', 'Großbuchstaben'), 'Python hotkey handler fired', 'hotkeys-py');
  check((await win.clipboardGet()) === 'PEACH LOUD', 'Python mod rewrote the clipboard');

  check(await waitFor('hotkeys-ps1', 'Zwischenablage hat sich geändert'), 'PowerShell clipboard event fired', 'hotkeys-ps1');
  await press(0x48); // H
  check(await waitFor('hotkeys-ps1', 'Hotkey ctrl+alt+h pressed'), 'PowerShell hotkey handler fired', 'hotkeys-ps1');

  await manager.stop('hotkeys-js', { persist: false });
  check(manager.hub.hotkeys.size === 2, 'hotkey released when a mod stops');

  if (failed) console.log('diagnostics: helper reported hotkeys =', JSON.stringify(nativeHotkeys));
  await manager.stopAll();
  await win.clipboardSet(saved || '').catch(() => {});
  manager.state.enabled = {}; manager._saveState();
  server.close();
  setTimeout(() => process.exit(failed ? 1 : 0), 500);
})();
