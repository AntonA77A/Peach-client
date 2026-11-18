'use strict';
// Thin PowerShell bridge for Windows-specific features.
// User data always goes through environment variables, never into the script text.
const { execFile } = require('child_process');

const PRE = '[Console]::OutputEncoding=[Text.Encoding]::UTF8;$ErrorActionPreference="Stop";';

function ps(script, env = {}, timeout = 15000) {
  return new Promise((resolve, reject) => {
    execFile('powershell.exe',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', PRE + script],
      { env: { ...process.env, ...env }, windowsHide: true, timeout, maxBuffer: 8 * 1024 * 1024, encoding: 'utf8' },
      (err, stdout, stderr) => err ? reject(new Error((stderr || err.message).trim())) : resolve(stdout.trim()));
  });
}

function notify(title, message) {
  const script =
    'Add-Type -AssemblyName System.Windows.Forms;' +
    '$n=New-Object System.Windows.Forms.NotifyIcon;' +
    '$n.Icon=[System.Drawing.SystemIcons]::Information;$n.Visible=$true;' +
    '$n.ShowBalloonTip(5000,$env:P_TITLE,$env:P_MSG,[System.Windows.Forms.ToolTipIcon]::Info);' +
    'Start-Sleep -Seconds 6;$n.Dispose()';
  ps(script, { P_TITLE: String(title || 'Peach'), P_MSG: String(message || '') }, 20000).catch(() => {});
}

const clipboardGet = () => ps('Get-Clipboard -Raw');
const clipboardSet = text => ps('Set-Clipboard -Value $env:P_TEXT', { P_TEXT: String(text) }).then(() => true);

async function windowsList() {
  const out = await ps('@(Get-Process | Where-Object { $_.MainWindowTitle } | ' +
    'Select-Object Id,ProcessName,MainWindowTitle) | ConvertTo-Json -Compress');
  if (!out) return [];
  const data = JSON.parse(out);
  return (Array.isArray(data) ? data : [data]).map(w => ({ pid: w.Id, process: w.ProcessName, title: w.MainWindowTitle }));
}

async function registryGet(regPath, name) {
  if (!/^(HKCU|HKLM|HKCR|HKU|HKCC):\\/i.test(regPath)) throw new Error('Registry path must look like HKCU:\\Software\\...');
  const out = await ps('(Get-ItemProperty -LiteralPath $env:P_PATH -Name $env:P_NAME).($env:P_NAME) | ConvertTo-Json -Compress',
    { P_PATH: regPath, P_NAME: String(name) });
  return out ? JSON.parse(out) : null;
}

const wallpaperSet = file => ps(
  'Add-Type -TypeDefinition \'using System.Runtime.InteropServices; public class PeachW { [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int SystemParametersInfo(int a,int b,string c,int d); }\';' +
  '[void][PeachW]::SystemParametersInfo(20,0,$env:P_PATH,3)', { P_PATH: file }).then(() => true);

module.exports = { ps, notify, clipboardGet, clipboardSet, windowsList, registryGet, wallpaperSet };
