'use strict';
// Exercises hotkeys + focus/clipboard events end to end through the real native helper.
// Simulates Ctrl+Alt+Shift+F11 with keybd_event. Your clipboard is saved and restored.
const { EventHub, parseCombo } = require('../src/events');
const win = require('../src/windows');

const assert = (cond, msg) => { console.log((cond ? 'PASS ' : 'FAIL ') + msg); if (!cond) process.exitCode = 1; };
const mod = { id: 'test', permissions: ['hotkeys', 'windows', 'clipboard'] };

(async () => {
  const p = parseCombo('Ctrl+Alt+P');
  assert(p.norm === 'ctrl+alt+p' && p.mods === 3 && p.vk === 0x50, 'parseCombo ctrl+alt+p');
  assert(parseCombo('shift+win+f5').vk === 0x74, 'parseCombo F5');
  assert((() => { try { parseCombo('p'); } catch { return true; } })(), 'rejects hotkey without modifier');

  const saved = await win.clipboardGet().catch(() => '');
  const hub = new EventHub();
  try {
    await hub.subscribe(mod, ['focus', 'clipboard']);
    const r = await hub.register(mod, 'ctrl+alt+shift+f11');
    assert(r.combo === 'ctrl+alt+shift+f11', 'hotkey registered');
    let dup = false; try { await hub.register({ id: 'other', permissions: [] }, 'ctrl+alt+shift+f11'); } catch { dup = true; }
    assert(dup, 'duplicate hotkey rejected');
    let noperm = false; try { await hub.subscribe({ id: 'x', permissions: [] }, ['focus']); } catch { noperm = true; }
    assert(noperm, 'focus subscription needs permission');

    const seen = new Set();
    const pollFor = async ms => { const end = Date.now() + ms; while (Date.now() < end) (await hub.poll('test', 500)).forEach(e => seen.add(e.type)); };
    await pollFor(1500);
    assert(seen.has('focus'), 'focus event delivered');

    await win.ps('Set-Clipboard -Value "peach-event-test"');
    await pollFor(1500);
    assert(seen.has('clipboard'), 'clipboard event delivered');

    await win.ps(
      'Add-Type -TypeDefinition \'using System.Runtime.InteropServices; public class K { [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, int e); }\';' +
      '$k=[K]; foreach($v in 0x11,0x12,0x10,0x7A){ $k::keybd_event($v,0,0,0) }; Start-Sleep -Milliseconds 100; foreach($v in 0x7A,0x10,0x12,0x11){ $k::keybd_event($v,0,2,0) }');
    await pollFor(2000);
    assert(seen.has('hotkey'), 'hotkey event delivered');

    assert(await hub.unregister(mod, 'ctrl+alt+shift+f11'), 'hotkey unregistered');
  } catch (e) { assert(false, 'unexpected error: ' + e.message); }
  hub.cleanup('test'); hub.shutdown();
  await win.clipboardSet(saved || '').catch(() => {});
  setTimeout(() => process.exit(), 1500);
})();
