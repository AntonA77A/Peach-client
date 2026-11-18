# Builds peach.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$sources = Get-ChildItem (Join-Path $root 'app') -Filter *.cs | ForEach-Object FullName
$out  = Join-Path $root 'peach.exe'
$ico  = Join-Path $root 'app\peach.ico'
$wpf  = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
        (Join-Path $wpf 'UIAutomationClient.dll'), (Join-Path $wpf 'UIAutomationTypes.dll'), (Join-Path $wpf 'WindowsBase.dll'),
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Security.dll'
$common = @('/nologo', '/target:winexe', '/platform:anycpu', '/codepage:65001', '/optimize+', '/warn:3') +
          ($refs | ForEach-Object { "/r:$_" })

# Discord window: needs Microsoft's WebView2 libraries in lib\webview2 (NuGet package Microsoft.Web.WebView2). Optional.
$wv = Join-Path $root 'lib\webview2'
$wvCore = Join-Path $wv 'Microsoft.Web.WebView2.Core.dll'
$wvForms = Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'
if ((Test-Path $wvCore) -and (Test-Path $wvForms)) {
  $common += '/define:WEBVIEW2', "/r:$wvCore", "/r:$wvForms"
  'WebView2 libraries found: Discord window enabled'
} else { 'WebView2 libraries not found in lib\webview2: building without the Discord window' }

# Pass 1 builds without an icon so the exe can render its own icon; pass 2 embeds it.
$tmp = Join-Path $env:TEMP 'peach-build.exe'
& $csc @common "/out:$tmp" @sources
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
$p = Start-Process $tmp -ArgumentList '--make-icon', "`"$ico`"" -Wait -PassThru
if ($p.ExitCode -ne 0 -or -not (Test-Path $ico)) { throw 'icon generation failed' }

if (Get-Process peach -ErrorAction SilentlyContinue) { throw 'peach.exe is running - quit it from the tray first' }
& $csc @common "/out:$out" "/win32icon:$ico" @sources
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
Remove-Item $tmp -ErrorAction SilentlyContinue
"Built $out"
