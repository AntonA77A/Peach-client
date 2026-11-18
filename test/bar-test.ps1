# Does the Peach bar really reserve screen space (like a taskbar) and give it back?
# Sends NO keys or clicks. Your windows briefly shrink by the bar height while it runs.
# Uses a temp config; quit peach.exe first (single-instance app).
param([string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'peach.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public class BT {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
}
'@
if (Get-Process peach -ErrorAction SilentlyContinue) { throw 'peach.exe is already running - quit it from the tray first' }

$failed = 0
function Check($ok, $name, $detail) { if ($ok) { "PASS $name" } else { $script:failed++; "FAIL $name  $detail" } }
function BarWindow { [BT]::FindWindow([NullString]::Value, 'Peach Leiste') }
function BarVisible { $h = BarWindow; return ($h -ne [IntPtr]::Zero -and [BT]::IsWindowVisible($h)) }
function WaitFor($cond, $ms) { $end = [DateTime]::Now.AddMilliseconds($ms); while ([DateTime]::Now -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 150 }; return (& $cond) }

function RunCase($position, $height) {
  $cfg = Join-Path $env:TEMP "peach-bar-$([guid]::NewGuid().ToString('N')).json"
  @{ MouseEnabled = $false; AppsSeeded = $true; GameBarAsked = $true
     Bar = @{ Enabled = $true; Position = $position; Height = $height; ShowWindows = $true; ShowClock = $true; ShowDate = $true } } |
    ConvertTo-Json -Depth 5 | Set-Content $cfg -Encoding UTF8
  $before = [Windows.Forms.SystemInformation]::WorkingArea
  $screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
  $proc = Start-Process $Exe -ArgumentList '--config', "`"$cfg`"" -PassThru
  try {
    Check (WaitFor { BarVisible } 8000) "[$position] the bar window appears" ''
    Start-Sleep -Milliseconds 800
    $h = BarWindow; $r = New-Object BT+RECT; [void][BT]::GetWindowRect($h, [ref]$r)
    $after = [Windows.Forms.SystemInformation]::WorkingArea
    $barH = $r.Bottom - $r.Top
    Check ($barH -eq $height) "[$position] bar is $height px high" "height = $barH"
    Check (($r.Right - $r.Left) -eq $screen.Width) "[$position] bar spans the full screen width" "width = $($r.Right - $r.Left) vs $($screen.Width)"
    if ($position -eq 'top') { Check ($r.Top -eq $screen.Top) '[top] bar sits at the top edge' "top = $($r.Top)" }
    else { Check ($r.Bottom -le $before.Bottom -and $r.Bottom -gt $before.Bottom - $height - 2) '[bottom] bar sits right above the Windows taskbar (inside the old work area)' "bar bottom = $($r.Bottom), old work area bottom = $($before.Bottom)" }
    $lost = $before.Height - $after.Height
    Check ($lost -eq $height) "[$position] the work area shrank by the bar height (maximized windows stay clear)" "lost $lost px, expected $height"
    $ex = [BT]::GetWindowLong($h, -20)
    Check ((($ex -band 0x08000000) -ne 0) -and (($ex -band 0x8) -ne 0)) "[$position] bar is no-activate + topmost (clicks never steal focus)" "exstyle = $ex"
  }
  finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $cfg -ErrorAction SilentlyContinue
  }
  $back = WaitFor { ([Windows.Forms.SystemInformation]::WorkingArea.Height) -eq $before.Height } 6000
  Check $back "[$position] the work area is restored when Peach ends" "now $([Windows.Forms.SystemInformation]::WorkingArea.Height), expected $($before.Height)"
}

RunCase 'bottom' 48
RunCase 'top' 40
if ($failed) { "$failed check(s) failed"; exit 1 } else { 'ALL PASSED' }
