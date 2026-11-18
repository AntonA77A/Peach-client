#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const { ModManager, PERMISSIONS } = require('../src/manager');
const { startServer } = require('../src/server');
const { supported } = require('../src/runners');
const templates = require('../src/templates');

const root = path.resolve(__dirname, '..');
const pkg = require('../package.json');

function loadConfig() {
  try { return JSON.parse(fs.readFileSync(path.join(root, 'peach.config.json'), 'utf8')); } catch { return {}; }
}

const HELP = `Peach Client ${pkg.version} - Windows mods in many languages

  peach start [--open] [--only <id>]   Run the client, start enabled mods, serve the dashboard
  peach list                           Show installed mods
  peach new <id> [--lang <lang>]       Create a mod (${Object.keys(templates).join(', ')})
  peach langs                          Show supported languages and whether an interpreter was found
`;

async function main() {
  const [cmd, ...rest] = process.argv.slice(2);
  const flag = name => { const i = rest.indexOf('--' + name); return i < 0 ? null : (rest[i + 1] && !rest[i + 1].startsWith('--') ? rest[i + 1] : true); };
  const config = loadConfig();

  switch (cmd) {
    case 'start': {
      const manager = new ModManager({ root, config, version: pkg.version });
      const { port, url } = await startServer({ manager, config });
      const only = flag('only');
      console.log(`Peach ${pkg.version} running - dashboard: ${url}`);
      if (only && only !== true) {
        try { manager.start(only); } catch (e) { console.error(e.message); }
      } else {
        manager.startEnabled();
      }
      console.log(`${manager.mods.size} mod(s) found in ${manager.modsDir}. Ctrl+C to quit.`);
      if (flag('open')) spawn('explorer.exe', [url], { detached: true, stdio: 'ignore' }).unref();
      const quit = async () => { await manager.stopAll(); process.exit(0); };
      process.on('SIGINT', quit); process.on('SIGTERM', quit);
      break;
    }
    case 'list': {
      const manager = new ModManager({ root, config, version: pkg.version });
      if (!manager.mods.size) return console.log('No mods yet. Try: peach new my-mod --lang py');
      for (const m of manager.mods.values()) {
        console.log(`${m.id.padEnd(20)} ${m.language.padEnd(22)} v${m.version.padEnd(8)} [${m.permissions.join(', ') || 'no permissions'}]`);
      }
      break;
    }
    case 'langs':
      for (const r of supported(config)) console.log(`${r.available ? '[x]' : '[ ]'} ${r.ext.padEnd(6)} ${r.label}`);
      break;
    case 'new': {
      const id = rest[0];
      if (!id || !/^[\w.-]+$/.test(id)) return console.error('Usage: peach new <id> [--lang js|ts|py|ps1|bat|vbs]');
      const lang = flag('lang') || 'js';
      const tpl = templates[lang];
      if (!tpl) return console.error('Unknown language. Choose one of: ' + Object.keys(templates).join(', '));
      const dir = path.join(root, config.modsDir || 'mods', id);
      if (fs.existsSync(dir)) return console.error('Mod already exists: ' + dir);
      fs.mkdirSync(dir, { recursive: true });
      fs.writeFileSync(path.join(dir, 'mod.json'), JSON.stringify({
        name: id, version: '0.1.0', description: '', entry: tpl.file, permissions: ['notify'], autostart: true,
      }, null, 2) + '\n');
      // Windows PowerShell 5.1 reads BOM-less .ps1 files as ANSI, which garbles non-ASCII text.
      const bom = lang === 'ps1' ? '﻿' : '';
      fs.writeFileSync(path.join(dir, tpl.file), bom + tpl.body.replace(/\{\{name\}\}/g, id));
      console.log(`Created ${dir}\nAvailable permissions: ${PERMISSIONS.join(', ')}`);
      break;
    }
    default:
      console.log(HELP);
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });
