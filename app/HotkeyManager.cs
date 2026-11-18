using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Peach
{
    // Hotkey strings use the same "ctrl+alt+k" format as the script-mod API.
    internal static class Combo
    {
        static readonly Dictionary<string, Keys> Named = new Dictionary<string, Keys>
        {
            { "space", Keys.Space }, { "enter", Keys.Enter }, { "esc", Keys.Escape }, { "tab", Keys.Tab },
            { "backspace", Keys.Back }, { "delete", Keys.Delete }, { "insert", Keys.Insert }, { "home", Keys.Home },
            { "end", Keys.End }, { "pageup", Keys.PageUp }, { "pagedown", Keys.PageDown },
            { "left", Keys.Left }, { "up", Keys.Up }, { "right", Keys.Right }, { "down", Keys.Down },
        };

        public static string KeyName(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return ((char)('a' + (k - Keys.A))).ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return "f" + (k - Keys.F1 + 1);
            foreach (KeyValuePair<string, Keys> kv in Named) if (kv.Value == k) return kv.Key;
            return null;
        }

        static bool KeyFromName(string name, out Keys key)
        {
            key = Keys.None;
            if (name.Length == 1 && name[0] >= 'a' && name[0] <= 'z') { key = Keys.A + (name[0] - 'a'); return true; }
            if (name.Length == 1 && name[0] >= '0' && name[0] <= '9') { key = Keys.D0 + (name[0] - '0'); return true; }
            if (name.Length >= 2 && name[0] == 'f')
            {
                int n;
                if (int.TryParse(name.Substring(1), out n) && n >= 1 && n <= 24) { key = Keys.F1 + (n - 1); return true; }
            }
            return Named.TryGetValue(name, out key);
        }

        public static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(combo)) return false;
            string[] parts = combo.ToLowerInvariant().Split('+');
            if (parts.Length < 2) return false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim())
                {
                    case "ctrl": case "control": case "strg": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": mods |= Native.MOD_WIN; break;
                    default: return false;
                }
            }
            Keys key;
            if (!KeyFromName(parts[parts.Length - 1].Trim(), out key)) return false;
            vk = (uint)key;
            return true;
        }

        public static string Normalize(string combo)
        {
            uint mods, vk;
            if (!Parse(combo, out mods, out vk)) return null;
            StringBuilder sb = new StringBuilder();
            if ((mods & Native.MOD_CONTROL) != 0) sb.Append("ctrl+");
            if ((mods & Native.MOD_ALT) != 0) sb.Append("alt+");
            if ((mods & Native.MOD_SHIFT) != 0) sb.Append("shift+");
            if ((mods & Native.MOD_WIN) != 0) sb.Append("win+");
            sb.Append(KeyName((Keys)vk));
            return sb.ToString();
        }
    }

    internal class HotkeyManager : NativeWindow, IDisposable
    {
        const int MenuId = 1;
        public const string MenuCombo = "ctrl+alt+p";
        readonly Dictionary<int, HotkeyEntry> entries = new Dictionary<int, HotkeyEntry>();
        int nextId = 100;
        public event Action MenuRequested;

        public HotkeyManager()
        {
            CreateParams cp = new CreateParams();
            cp.Parent = new IntPtr(-3);   // HWND_MESSAGE: invisible, message-only
            CreateHandle(cp);
        }

        public bool RegisterMenuHotkey()
        {
            return Native.RegisterHotKey(Handle, MenuId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.P);
        }

        // Returns the combos Windows refused (usually because another program owns them).
        public List<string> Reload(IList<HotkeyEntry> list)
        {
            foreach (int id in entries.Keys) Native.UnregisterHotKey(Handle, id);
            entries.Clear();
            List<string> failed = new List<string>();
            foreach (HotkeyEntry e in list)
            {
                uint mods, vk;
                if (!Combo.Parse(e.Combo, out mods, out vk) || Combo.Normalize(e.Combo) == MenuCombo) { failed.Add(e.Combo); continue; }
                int id = nextId++;
                if (Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk)) entries[id] = e;
                else failed.Add(e.Combo);
            }
            return failed;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = (int)m.WParam;
                HotkeyEntry e;
                if (id == MenuId) { Action h = MenuRequested; if (h != null) h(); }
                else if (entries.TryGetValue(id, out e)) Run(e);
                return;
            }
            base.WndProc(ref m);
        }

        public static void Run(HotkeyEntry e)
        {
            string target = e.Target ?? "";
            if (e.Action == "type")
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Native.WaitModifiersUp(true);
                    Native.TypeText(TextExpander.Expand(target), 0);
                });
            }
            else
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(target);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch { /* bad path: nothing sensible to do from a background hotkey */ }
            }
        }

        public void Dispose()
        {
            foreach (int id in entries.Keys) Native.UnregisterHotKey(Handle, id);
            Native.UnregisterHotKey(Handle, MenuId);
            entries.Clear();
            DestroyHandle();
        }
    }
}
