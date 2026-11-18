# Peach native helper: global hotkeys, foreground-window changes, clipboard changes.
# Talks to the Node host over stdin/stdout, tab-separated lines.
#   in : reg <req> <id> <mods> <vk> | unreg <req> <id>
#   out: ready | res <req> ok|fail | hotkey <id> | focus <pid> <process> <title> | clip
$ErrorActionPreference = 'Stop'
$src = @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class PeachNative {
  [StructLayout(LayoutKind.Sequential)]
  struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
  [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

  static ConcurrentQueue<string[]> cmds = new ConcurrentQueue<string[]>();
  static volatile bool quit;

  static void Out(string s) { lock (Console.Out) { Console.Out.WriteLine(s); Console.Out.Flush(); } }

  public static void Run() {
    Console.OutputEncoding = new UTF8Encoding(false);
    Thread reader = new Thread(delegate () {
      string line;
      while ((line = Console.In.ReadLine()) != null) cmds.Enqueue(line.Split('\t'));
      quit = true;
    });
    reader.IsBackground = true;
    reader.Start();

    MSG msg;
    IntPtr lastWin = IntPtr.Zero;
    string lastTitle = "";
    uint lastClip = GetClipboardSequenceNumber();
    int tick = 0;
    Out("ready");

    while (!quit) {
      string[] c;
      while (cmds.TryDequeue(out c)) {
        if (c[0] == "reg" && c.Length >= 5) {
          bool ok = RegisterHotKey(IntPtr.Zero, int.Parse(c[2]), uint.Parse(c[3]) | 0x4000, uint.Parse(c[4]));
          Out("res\t" + c[1] + "\t" + (ok ? "ok" : "fail"));
        } else if (c[0] == "unreg" && c.Length >= 3) {
          UnregisterHotKey(IntPtr.Zero, int.Parse(c[2]));
          Out("res\t" + c[1] + "\tok");
        }
      }
      while (PeekMessage(out msg, IntPtr.Zero, 0x0312, 0x0312, 1)) Out("hotkey\t" + (int)(ulong)msg.wParam);

      if (++tick % 8 == 0) {
        IntPtr h = GetForegroundWindow();
        if (h != IntPtr.Zero) {
          StringBuilder sb = new StringBuilder(512);
          GetWindowText(h, sb, 512);
          string title = sb.ToString();
          if (h != lastWin || title != lastTitle) {
            lastWin = h; lastTitle = title;
            uint pid; GetWindowThreadProcessId(h, out pid);
            string pn = "";
            try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { }
            Out("focus\t" + pid + "\t" + pn + "\t" + title.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
          }
        }
        uint clip = GetClipboardSequenceNumber();
        if (clip != lastClip) { lastClip = clip; Out("clip"); }
      }
      Thread.Sleep(25);
    }
  }
}
'@
Add-Type -TypeDefinition $src -Language CSharp
[PeachNative]::Run()
