'use strict';
const fs = require('fs');
const path = require('path');

// Each runner: label + list of candidate command lines. First one whose
// executable is found wins. "{entry}" is replaced with the mod's entry file.
const BUILTIN = {
  '.js':   { label: 'JavaScript', candidates: [[process.execPath, '{entry}']] },
  '.mjs':  { label: 'JavaScript (ESM)', candidates: [[process.execPath, '{entry}']] },
  '.ts':   { label: 'TypeScript', candidates: [[process.execPath, '{entry}']] }, // Node >= 23.6 strips types
  '.py':   { label: 'Python', candidates: [['python', '-u', '{entry}'], ['py', '-3', '-u', '{entry}']] },
  '.ps1':  { label: 'PowerShell', candidates: [['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '{entry}'], ['pwsh', '-NoProfile', '-File', '{entry}']] },
  '.bat':  { label: 'Batch', candidates: [['cmd.exe', '/d', '/c', '{entry}']] },
  '.cmd':  { label: 'Batch', candidates: [['cmd.exe', '/d', '/c', '{entry}']] },
  '.vbs':  { label: 'VBScript', candidates: [['cscript.exe', '//nologo', '{entry}']] },
  '.wsf':  { label: 'Windows Script', candidates: [['cscript.exe', '//nologo', '{entry}']] },
  '.ahk':  { label: 'AutoHotkey', candidates: [['AutoHotkey64.exe', '{entry}'], ['AutoHotkey.exe', '{entry}'], ['AutoHotkey32.exe', '{entry}']] },
  '.lua':  { label: 'Lua', candidates: [['lua', '{entry}'], ['luajit', '{entry}']] },
  '.rb':   { label: 'Ruby', candidates: [['ruby', '{entry}']] },
  '.pl':   { label: 'Perl', candidates: [['perl', '{entry}']] },
  '.php':  { label: 'PHP', candidates: [['php', '{entry}']] },
  '.sh':   { label: 'Shell', candidates: [['bash', '{entry}']] },
  '.go':   { label: 'Go', candidates: [['go', 'run', '{entry}']] },
  '.cs':   { label: 'C# (.NET 10 file app)', candidates: [['dotnet', 'run', '{entry}']] },
  '.java': { label: 'Java', candidates: [['java', '{entry}']] },
  '.r':    { label: 'R', candidates: [['Rscript', '{entry}']] },
  '.jl':   { label: 'Julia', candidates: [['julia', '{entry}']] },
  '.dart': { label: 'Dart', candidates: [['dart', 'run', '{entry}']] },
};

function which(cmd) {
  if (path.isAbsolute(cmd)) return fs.existsSync(cmd) ? cmd : null;
  const exts = (process.env.PATHEXT || '.EXE;.CMD;.BAT').split(';');
  for (const dir of (process.env.PATH || '').split(path.delimiter)) {
    if (!dir) continue;
    for (const ext of ['', ...exts]) {
      const file = path.join(dir, cmd + ext);
      try { if (fs.statSync(file).isFile()) return file; } catch {
        // Microsoft Store app-execution aliases (e.g. python.exe) are reparse points that statSync rejects.
        try { if (!fs.lstatSync(file).isDirectory()) return file; } catch { /* next */ }
      }
    }
  }
  return null;
}

function allRunners(config) {
  const merged = { ...BUILTIN };
  for (const [ext, cands] of Object.entries((config && config.runners) || {})) {
    merged[ext.toLowerCase()] = { label: ext.slice(1).toUpperCase() + ' (custom)', candidates: cands };
  }
  return merged;
}

function resolveRunner(entryFile, config) {
  const ext = path.extname(entryFile).toLowerCase();
  const runner = allRunners(config)[ext];
  if (!runner) throw new Error(`No runner for "${ext}". Add one under "runners" in peach.config.json.`);
  for (const cand of runner.candidates) {
    const exe = which(cand[0]);
    if (exe) return { label: runner.label, cmd: exe, args: cand.slice(1).map(a => a.replace('{entry}', entryFile)) };
  }
  throw new Error(`${runner.label} interpreter not found (tried: ${runner.candidates.map(c => c[0]).join(', ')})`);
}

function languageLabel(entryFile, config) {
  const r = allRunners(config)[path.extname(entryFile).toLowerCase()];
  return r ? r.label : 'Unknown';
}

function supported(config) {
  return Object.entries(allRunners(config)).map(([ext, r]) => {
    let available = false;
    try { available = r.candidates.some(c => which(c[0])); } catch { /* ignore */ }
    return { ext, label: r.label, available };
  });
}

module.exports = { resolveRunner, languageLabel, supported, which };
