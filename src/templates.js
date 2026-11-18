'use strict';
// Starter files for `peach new <id> --lang <lang>`.
const T = {
  js: { file: 'main.js', body: `const peach = require('peach');

(async () => {
  await peach.log('Hello from JavaScript!');
  await peach.notify('{{name}}', 'JavaScript mod is running');
})();
` },
  ts: { file: 'main.ts', body: `// Needs Node >= 23.6 (runs TypeScript natively)
const peach = require('peach') as any;

(async (): Promise<void> => {
  await peach.log('Hello from TypeScript!');
  await peach.notify('{{name}}', 'TypeScript mod is running');
})();
` },
  py: { file: 'main.py', body: `import peach

peach.log("Hello from Python!")
peach.notify("{{name}}", "Python mod is running")
` },
  ps1: { file: 'main.ps1', body: `. "$env:PEACH_SDK\\peach.ps1"

Peach-Log "Hello from PowerShell!"
Peach-Notify "{{name}}" "PowerShell mod is running"
` },
  bat: { file: 'main.bat', body: `@echo off
rem Any language can talk to Peach over plain HTTP. curl.exe ships with Windows 10+.
curl.exe -s -X POST "%PEACH_API%/log" -H "Authorization: Bearer %PEACH_TOKEN%" -d "{\\"message\\":\\"Hello from Batch!\\"}"
echo.
` },
  vbs: { file: 'main.vbs', body: `Set env = CreateObject("WScript.Shell").Environment("PROCESS")
Set http = CreateObject("MSXML2.ServerXMLHTTP.6.0")
http.Open "POST", env("PEACH_API") & "/log", False
http.SetRequestHeader "Authorization", "Bearer " & env("PEACH_TOKEN")
http.Send "{""message"":""Hello from VBScript!""}"
WScript.Echo http.ResponseText
` },
};

module.exports = T;
