using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Peach
{
    // "Fast Type": type an abbreviation and it is replaced by the full text.
    // Uses a low-level keyboard hook. Only the last few typed characters are kept in
    // memory (never written anywhere) and the buffer is wiped on Enter, Tab, shortcuts
    // and window switches.
    internal class TextExpander : IDisposable
    {
        readonly Config cfg;
        readonly LowLevelProc proc;
        readonly StringBuilder buf = new StringBuilder();
        IntPtr hook;
        IntPtr lastWindow;
        public bool Enabled;

        public TextExpander(Config cfg)
        {
            this.cfg = cfg;
            Enabled = cfg.FastTypeEnabled;
            proc = HookProc;   // keep a reference so the GC cannot collect the callback
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);
        }

        public bool Active { get { return hook != IntPtr.Zero; } }

        // Pure matching logic (unit-tested): the abbreviation must end the buffer and start a "word".
        public static Snippet Find(string buffer, IList<Snippet> snippets)
        {
            Snippet best = null;
            foreach (Snippet s in snippets)
            {
                if (s == null || string.IsNullOrEmpty(s.Abbr) || s.Abbr.Length > buffer.Length) continue;
                if (!buffer.EndsWith(s.Abbr, StringComparison.OrdinalIgnoreCase)) continue;
                int start = buffer.Length - s.Abbr.Length;
                if (start > 0 && char.IsLetterOrDigit(buffer[start - 1]) && char.IsLetterOrDigit(s.Abbr[0])) continue;
                if (best == null || s.Abbr.Length > best.Abbr.Length) best = s;
            }
            return best;
        }

        public static string Expand(string text)
        {
            DateTime now = DateTime.Now;
            return (text ?? "").Replace("{date}", now.ToString("dd.MM.yyyy")).Replace("{time}", now.ToString("HH:mm"));
        }

        IntPtr HookProc(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && Enabled)
            {
                int msg = (int)w;
                if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(Native.KBDLLHOOKSTRUCT));
                    if (k.dwExtraInfo != Native.Magic && OnKey(k)) return new IntPtr(1);   // swallow the completing key
                }
            }
            return Native.CallNextHookEx(hook, code, w, l);
        }

        static bool Down(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        bool OnKey(Native.KBDLLHOOKSTRUCT k)
        {
            uint vk = k.vkCode;
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C) return false;

            IntPtr fg = Native.GetForegroundWindow();
            if (fg != lastWindow) { buf.Length = 0; lastWindow = fg; }

            bool ctrl = Down(0x11), alt = Down(0x12), win = Down(0x5B) || Down(0x5C);
            bool altGr = ctrl && alt;              // AltGr = Ctrl+Alt on German layouts (@ { } \ ...)
            if (win || (ctrl && !alt) || (alt && !ctrl)) { buf.Length = 0; return false; }

            if (vk == 0x08) { if (buf.Length > 0) buf.Length--; return false; }

            char c;
            if (!ToChar(k, altGr, fg, out c) || char.IsControl(c)) { buf.Length = 0; return false; }

            buf.Append(c);
            if (buf.Length > 48) buf.Remove(0, buf.Length - 48);

            Snippet s = Find(buf.ToString(), cfg.Snippets);
            if (s == null) return false;

            int erase = s.Abbr.Length - 1;         // the last abbreviation char is swallowed, never reaches the app
            string text = Expand(s.Text);
            buf.Length = 0;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Native.WaitModifiersUp(false);
                Native.TypeText(text, erase);
            });
            return true;
        }

        static bool ToChar(Native.KBDLLHOOKSTRUCT k, bool altGr, IntPtr fg, out char c)
        {
            c = '\0';
            byte[] st = new byte[256];
            if (Down(0x10)) st[0x10] = 0x80;
            if ((Native.GetKeyState(0x14) & 1) != 0) st[0x14] = 1;
            if (altGr) { st[0x11] = 0x80; st[0x12] = 0x80; }
            uint pid;
            IntPtr hkl = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(fg, out pid));
            StringBuilder sb = new StringBuilder(8);
            int r = Native.ToUnicodeEx(k.vkCode, k.scanCode, st, sb, 8, 4, hkl);   // flag 4: do not disturb dead-key state
            if (r != 1) return false;
            c = sb[0];
            return true;
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
    }
}
