# End-to-end test of peach.exe with real (simulated) key events:
#  Fast Type expansion, custom type-hotkey, Ctrl+Alt+P menu + toggles.
# Uses a temp config; your real config in %APPDATA%\PeachClient is untouched.
# Quit peach.exe first - it is a single-instance app.
param([string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'peach.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public class PK {
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, int e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, int e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
  public static void Force(IntPtr h) {
    uint pid; uint ft = GetWindowThreadProcessId(GetForegroundWindow(), out pid); uint me = GetCurrentThreadId();
    bool att = ft != 0 && ft != me && AttachThreadInput(me, ft, true);
    SetForegroundWindow(h); BringWindowToTop(h);
    if (att) AttachThreadInput(me, ft, false);
  }
}
'@
if (Get-Process peach -ErrorAction SilentlyContinue) { throw 'peach.exe is already running - quit it from the tray first' }

$failed = 0
function Check($ok, $name, $detail) { if ($ok) { "PASS $name" } else { $script:failed++; "FAIL $name  $detail" } }
function Pump($ms) { $end = [DateTime]::Now.AddMilliseconds($ms); while ([DateTime]::Now -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }
function Tap($vks, $mods = @()) {
  foreach ($m in $mods) { [PK]::keybd_event($m, 0, 0, 0) }
  foreach ($v in $vks) { [PK]::keybd_event($v, 0, 0, 0); Pump 40; [PK]::keybd_event($v, 0, 2, 0); Pump 40 }
  $rev = @($mods); [array]::Reverse($rev)
  foreach ($m in $rev) { [PK]::keybd_event($m, 0, 2, 0) }
  Pump 60
}
function Release($mods) { foreach ($m in $mods) { [PK]::keybd_event($m, 0, 2, 0) } }
function WinVisible($title) { $h = [PK]::FindWindow([NullString]::Value, $title); return ($h -ne [IntPtr]::Zero -and [PK]::IsWindowVisible($h)) }
function KeyboardVisible { WinVisible 'Peach Tastatur' }
# Same geometry as OnScreenKeyboard.cs: padding 12, gap 6, key height 54, keys share the row width by weight.
function ClickKey($row, [double[]]$weights, $idx) {
  $h = [PK]::FindWindow([NullString]::Value, 'Peach Tastatur'); $r = New-Object PK+RECT; [void][PK]::GetWindowRect($h, [ref]$r)
  $pad = 12; $gap = 6; $kh = 54; $n = $weights.Count; $sum = ($weights | Measure-Object -Sum).Sum
  $avail = ($r.Right - $r.Left) - 2 * $pad - $gap * ($n - 1); $x = $pad
  for ($i = 0; $i -lt $idx; $i++) { $x += $weights[$i] / $sum * $avail + $gap }
  $px = [int]($r.Left + $x + $weights[$idx] / $sum * $avail / 2); $py = [int]($r.Top + $pad + $row * ($kh + $gap) + $kh / 2)
  $old = [Windows.Forms.Cursor]::Position
  [Windows.Forms.Cursor]::Position = New-Object Drawing.Point($px, $py); Pump 80
  [PK]::mouse_event(2, 0, 0, 0, 0); Pump 40; [PK]::mouse_event(4, 0, 0, 0, 0); Pump 350
  [Windows.Forms.Cursor]::Position = $old
}
function EnsureFocus {
  # Someone (you, a notification, another app) may grab focus between steps. Re-take it, and if that fails send NO keys.
  for ($i = 0; $i -lt 3; $i++) {
    [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)
    [void][PK]::SetForegroundWindow($form.Handle); $tb.Focus(); Pump 250
    if ([PK]::GetForegroundWindow() -eq $form.Handle) { return }
  }
  throw 'test window lost focus and could not get it back; stopping without sending more keys'
}function MenuVisible { $h = [PK]::FindWindow([NullString]::Value,"Peach Men$([char]0xFC)"); return ($h -ne [IntPtr]::Zero -and [PK]::IsWindowVisible($h)) }

$cfg = Join-Path $env:TEMP "peach-e2e-$([guid]::NewGuid().ToString('N')).json"
$marker = Join-Path $env:TEMP "peach-e2e-launched-$([guid]::NewGuid().ToString('N')).txt"
@{ MouseEnabled = $false; MouseSpeed = 1200; FastTypeEnabled = $true; AppsSeeded = $true; ControllerMode = $false; GameBarAsked = $true   # "asked" so no dialog blocks the test and your Windows setting is never touched
   Apps = @(@{ Name = 'Marker'; Path = 'powershell.exe'; Args = "-NoProfile -WindowStyle Hidden -Command `"Set-Content -LiteralPath '$marker' -Value ok`"" })
   Hotkeys = @(@{ Combo = 'ctrl+alt+shift+f9'; Action = 'type'; Target = 'HELLO-HOTKEY' })
   Snippets = @(@{ Abbr = 'zzq'; Text = 'EXPANDED-OK' }) } | ConvertTo-Json -Depth 5 | Set-Content $cfg -Encoding UTF8

$proc = Start-Process $Exe -ArgumentList '--config', "`"$cfg`"", '--test-hooks' -PassThru   # --test-hooks: lets this script plug/unplug a fake controller
try {
  Start-Sleep -Milliseconds 1500
  Check (-not $proc.HasExited) 'peach.exe is running' ''

  $form = New-Object Windows.Forms.Form
  $tb = New-Object Windows.Forms.TextBox; $tb.Dock = 'Fill'; $tb.Multiline = $true
  $form.Controls.Add($tb); $form.TopMost = $true; $form.Text = 'peach-e2e-target'; $form.Size = '420,200'
  $form.Show(); Pump 300
  [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)   # Alt tap lifts the foreground lock
  [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)
  [void][PK]::SetForegroundWindow($form.Handle); $form.Activate(); $tb.Focus(); Pump 400
  # Safety: simulated keys go to whatever has focus. If we could not get focus, send NOTHING.
  if ([PK]::GetForegroundWindow() -ne $form.Handle) {
    $form.Close()
    'ABORTED: the test window could not take focus, so no keys were sent. Run this script from a normal PowerShell window while you are not typing.'
    exit 2
  }
  'PASS test window has focus'

  # 1) Fast Type: z z q  ->  EXPANDED-OK
  Tap @(0x5A, 0x5A, 0x51); Pump 900
  Check ($tb.Text -eq 'EXPANDED-OK') 'Fast Type expands "zzq"' "textbox = [$($tb.Text)]"

  # 2) normal typing is not touched
  EnsureFocus; $tb.Clear(); Tap @(0x48, 0x41, 0x4C, 0x4C, 0x4F); Pump 300   # h a l l o
  Check ($tb.Text -eq 'hallo') 'normal typing passes through' "textbox = [$($tb.Text)]"

  # 3) custom hotkey (type action)
  EnsureFocus; $tb.Clear(); Tap @(0x78) @(0x11, 0x12, 0x10); Pump 1200       # ctrl+alt+shift+F9
  Check ($tb.Text -eq 'HELLO-HOTKEY') 'custom hotkey types its text' "textbox = [$($tb.Text)]"

  # 4) menu: Ctrl+Alt+P opens, toggles via number keys, Esc closes
  Tap @(0x50) @(0x11, 0x12); Pump 900
  Check (MenuVisible) 'Ctrl+Alt+P opens the menu' ''
  Tap @(0x36); Pump 500                                         # "6" = Fast Type toggle
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.FastTypeEnabled -eq $false) 'menu item 6 switches Fast Type off (saved)' "FastTypeEnabled = $($c.FastTypeEnabled)"
  Tap @(0x36); Pump 500
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.FastTypeEnabled -eq $true) 'menu item 6 switches Fast Type on again' "FastTypeEnabled = $($c.FastTypeEnabled)"
  Tap @(0x34); Pump 500                                         # "4" = auto keyboard toggle
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.AutoKeyboard -eq $false) 'menu item 4 switches the automatic keyboard off (saved)' "AutoKeyboard = $($c.AutoKeyboard)"
  Tap @(0x34); Pump 300
  Tap @(0x33); Pump 500                                         # "3" = controller mouse toggle
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.MouseEnabled -eq $true) 'menu item 3 switches controller mouse on (saved)' "MouseEnabled = $($c.MouseEnabled)"
  Tap @(0x33); Pump 300
  Tap @(0x1B); Pump 500
  Check (-not (MenuVisible)) 'Esc closes the menu' ''

  # 4b) on-screen keyboard: shown via menu item 3, must NOT steal focus, keys typed by clicking
  [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)
  [void][PK]::SetForegroundWindow($form.Handle); $tb.Focus(); $tb.Clear(); Pump 300
  if ([PK]::GetForegroundWindow() -ne $form.Handle) { throw 'test window lost focus; stopping before the keyboard test' }
  Tap @(0x50) @(0x11, 0x12); Pump 700
  Tap @(0x35); Pump 900                                         # "5" = show on-screen keyboard
  Check (KeyboardVisible) 'menu item 5 shows the on-screen keyboard' ''
  [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)
  [void][PK]::SetForegroundWindow($form.Handle); $tb.Focus(); Pump 400
  Check ([PK]::GetForegroundWindow() -eq $form.Handle) 'keyboard window does not take focus from the text field' ''
  if ([PK]::GetForegroundWindow() -eq $form.Handle -and (KeyboardVisible)) {
    ClickKey 2 @(1,1,1,1,1,1,1,1,1,1,1,1.6) 5                   # h
    ClickKey 3 @(1.6,1,1,1,1,1,1,1,1,1,1) 0                     # Shift
    ClickKey 3 @(1.6,1,1,1,1,1,1,1,1,1,1) 1                     # y  -> Y
    ClickKey 4 @(1.6,1,5,1,1,1.2) 2                             # Leer
    Check ($tb.Text -eq 'hY ') 'clicking keys types "h", Shift+"y", space into the field' "textbox = [$($tb.Text)]"
    Check ([PK]::GetForegroundWindow() -eq $form.Handle) 'focus still on the text field after typing' ''
    ClickKey 4 @(1.6,1,5,1,1,1.2) 5                             # x = close
    Check (-not (KeyboardVisible)) 'x key closes the keyboard' ''
  } else { $failed++; 'FAIL keyboard typing test skipped (no focus or keyboard hidden)' }

  # 4b2) controller mode needs a controller: refused without one, allowed after "plugging in" a fake one (test hook)
  Tap @(0x50) @(0x11, 0x12); Pump 800
  if (-not (MenuVisible)) { throw 'menu did not open before the no-controller test; stopping without sending more keys' }
  Tap @(0x32); Pump 1200
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.ControllerMode -eq $false) 'without a controller, menu item 2 refuses to start controller mode (nothing saved)' "ControllerMode = $($c.ControllerMode)"
  Check (-not (WinVisible 'Peach Launcher')) 'no launcher appears without a controller' ''
  $fake = [Threading.EventWaitHandle]::OpenExisting('Peach.Client.TestPad')
  [void]$fake.Set(); Pump 900                                   # plug a fake controller in

  # 4c) controller mode: menu item 2 opens the launcher. It is full screen and NOT always-on-top, stays open when an app
  #     starts or a window opens in front, ignores Esc, and only the last tile ("Modus beenden") ends the mode.
  Tap @(0x50) @(0x11, 0x12); Pump 800
  if (-not (MenuVisible)) { throw 'menu did not open before the launcher test; stopping without sending more keys' }
  Tap @(0x32); Pump 1500                                        # "2" = controller mode on
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.ControllerMode -eq $true) 'menu item 2 switches controller mode on (saved)' "ControllerMode = $($c.ControllerMode)"
  Check (WinVisible 'Peach Launcher') 'the launcher opens in controller mode' ''
  $lh = [PK]::FindWindow([NullString]::Value, 'Peach Launcher'); $lr = New-Object PK+RECT; [void][PK]::GetWindowRect($lh, [ref]$lr)
  $scr = [Windows.Forms.Screen]::FromPoint([Windows.Forms.Cursor]::Position).Bounds
  Check ((($lr.Right - $lr.Left) -eq $scr.Width) -and (($lr.Bottom - $lr.Top) -eq $scr.Height)) 'the launcher covers the whole screen' "launcher $($lr.Right-$lr.Left)x$($lr.Bottom-$lr.Top) vs screen $($scr.Width)x$($scr.Height)"
  $ex = [PK]::GetWindowLong($lh, -20)
  Check (($ex -band 8) -eq 0) 'the launcher is not always-on-top, so apps open in front of it' "exstyle = $ex"
  function FocusLauncher { [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0); [void][PK]::SetForegroundWindow($lh); Pump 500; return ([PK]::GetForegroundWindow() -eq $lh) }
  if (FocusLauncher) {
    Tap @(0x1B); Pump 500
    Check (WinVisible 'Peach Launcher') 'Esc does not close the launcher' ''
    Tap @(0x0D); $end = [DateTime]::Now.AddSeconds(10); while (-not (Test-Path $marker) -and [DateTime]::Now -lt $end) { Pump 250 }
    Check (Test-Path $marker) 'Enter starts the selected app from the launcher' 'marker file never appeared'
    Pump 700
    Check (WinVisible 'Peach Launcher') 'starting an app leaves the launcher open behind it' ''
  } else { $failed++; 'FAIL launcher did not take focus; skipped the Esc/start-app checks' }
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.ControllerMode -eq $true) 'controller mode stays on after starting an app' ''
  # a window in front of the launcher must not close it
  [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)
  [void][PK]::SetForegroundWindow($form.Handle); Pump 500
  Check ($form.Visible -and (WinVisible 'Peach Launcher')) 'a window in front of the launcher leaves it running behind' ''
  # only the last tile ("Modus beenden") ends controller mode
  if (FocusLauncher) {
    Tap @(0x27); Pump 400; Tap @(0x0D); Pump 1300
    $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($c.ControllerMode -eq $false) 'the "Modus beenden" tile switches controller mode off (saved)' "ControllerMode = $($c.ControllerMode)"
    Check (-not (WinVisible 'Peach Launcher')) 'the launcher is gone after the mode is ended' ''
  } else { $failed++; 'FAIL launcher did not take focus; skipped the exit-tile check' }

  # 4d) unplugging pauses controller mode (launcher goes away, the preference stays); plugging in again brings it back
  Tap @(0x50) @(0x11, 0x12); Pump 900
  if (-not (MenuVisible)) { throw 'menu did not open for the unplug test; stopping without sending more keys' }
  Tap @(0x32); Pump 1500                                        # controller mode on again
  Check (WinVisible 'Peach Launcher') 'controller mode starts again with the (fake) controller connected' ''
  [void]$fake.Set(); Pump 1200                                  # unplug
  Check (-not (WinVisible 'Peach Launcher')) 'unplugging the controller hides the launcher (mode paused)' ''
  $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  Check ($c.ControllerMode -eq $true) 'the controller-mode preference is kept while unplugged' "ControllerMode = $($c.ControllerMode)"
  [void]$fake.Set(); Pump 1500                                  # plug in again
  Check (WinVisible 'Peach Launcher') 'plugging the controller back in brings the launcher back by itself' ''
  if (FocusLauncher) {
    Tap @(0x27); Pump 400; Tap @(0x0D); Pump 1300               # exit tile
    $c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($c.ControllerMode -eq $false -and -not (WinVisible 'Peach Launcher')) 'controller mode ended again via the exit tile' "ControllerMode = $($c.ControllerMode)"
  } else { $failed++; 'FAIL launcher did not take focus; could not end controller mode' }

  # 5) second launch pops up the running instance's menu
  Start-Process $Exe -Wait; Pump 900
  Check (MenuVisible) 'starting peach.exe again opens the menu of the running one' ''
  Tap @(0x1B); Pump 300
  $procs = @(Get-Process peach -ErrorAction SilentlyContinue).Count
  Check ($procs -eq 1) 'still exactly one peach.exe process' "found $procs"

  # 6) Fast Type off -> no expansion
  Tap @(0x50) @(0x11, 0x12); Pump 700; Tap @(0x36); Pump 300; Tap @(0x1B); Pump 400
  [PK]::keybd_event(0x12, 0, 0, 0); [PK]::keybd_event(0x12, 0, 2, 0)
  [void][PK]::SetForegroundWindow($form.Handle); $tb.Focus(); $tb.Clear(); Pump 300
  if ([PK]::GetForegroundWindow() -ne $form.Handle) { throw 'lost focus before the last check; stopping without sending keys' }
  Tap @(0x5A, 0x5A, 0x51); Pump 600
  Check ($tb.Text -eq 'zzq') 'with Fast Type off the abbreviation stays' "textbox = [$($tb.Text)]"
  $form.Close()
}
finally {
  Release @(0x10, 0x11, 0x12)
  Get-Process peach -ErrorAction SilentlyContinue | Stop-Process -Force
  Remove-Item $cfg, $marker -ErrorAction SilentlyContinue
}
if ($failed) { "$failed check(s) failed"; exit 1 } else { 'ALL PASSED' }
