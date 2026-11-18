using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace Peach
{
    internal class StartApp
    {
        public string Name;
        public string Id;
        public string Direct;     // set for Peach's own entries (peach:discord ...): used as the path as it is
        // Works for classic programs (Discord, Steam, Chrome) and Store apps (Spotify, Calculator) alike.
        public string Path { get { return Direct ?? ("shell:AppsFolder\\" + Id); } }
    }

    // Everything Windows shows in the Start menu, via Get-StartApps.
    internal static class StartApps
    {
        public static List<StartApp> Load()
        {
            List<StartApp> result = new List<StartApp>();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Sort-Object Name | ConvertTo-Json -Compress\"");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    string json = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } }
                    result = Parse(json);
                }
            }
            catch { /* no PowerShell: the manual "add" dialog still works */ }
            return result;
        }

        public static List<StartApp> Parse(string json)
        {
            List<StartApp> list = new List<StartApp>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            object root = new JavaScriptSerializer().DeserializeObject(json);
            IEnumerable items = root as IDictionary != null ? new object[] { root } : root as IEnumerable;
            if (items == null) return list;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object o in items)
            {
                IDictionary d = o as IDictionary;
                if (d == null) continue;
                string name = d["Name"] as string, id = d["AppID"] as string;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;   // web links in the Start menu
                if (!seen.Add(id)) continue;
                list.Add(new StartApp { Name = name, Id = id });
            }
            return list;
        }
    }

    // One shared icon cache for the launcher tiles and the Peach bar (file paths, folders, PATH programs, shell: targets).
    internal static class AppIcons
    {
        static readonly System.Collections.Generic.Dictionary<string, Bitmap> cache = new System.Collections.Generic.Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)) return path;
                if (path.IndexOfAny(new char[] { '\\', '/', ':' }) >= 0) return null;
                List<string> dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator));
                dirs.Add(Environment.SystemDirectory);
                dirs.Add(Environment.GetEnvironmentVariable("windir") ?? "");
                foreach (string d in dirs)
                {
                    if (d.Length == 0) continue;
                    string f = System.IO.Path.Combine(d.Trim('"'), path);
                    if (System.IO.File.Exists(f)) return f;
                }
            }
            catch { }
            return null;
        }

        static Bitmap PeachIcon(string path)
        {
            string what = PeachLinks.Parse(path);
            Color c = what == "discord" ? Color.FromArgb(88, 101, 242) : Color.FromArgb(255, 122, 89);
            string letter = what == "discord" ? "D" : (what == "addons" ? "A" : "P");
            Bitmap bmp = new Bitmap(64, 64);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Brush b = new SolidBrush(c)) g.FillEllipse(b, 2, 2, 60, 60);
                using (Font f = new Font("Segoe UI Semibold", 26f))
                using (Brush w = new SolidBrush(Color.White))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(letter, f, w, new RectangleF(0, 2, 64, 64), sf);
            }
            return bmp;
        }

        // Returns null when there is no icon (callers draw a coloured letter instead). The bitmap is shared: never dispose it.
        public static Bitmap For(string path)
        {
            string key = path ?? "";
            lock (cache)
            {
                Bitmap bmp;
                if (cache.TryGetValue(key, out bmp)) return bmp;
                bmp = null;
                if (PeachLinks.Parse(path) != null) { bmp = PeachIcon(path); cache[key] = bmp; return bmp; }
                string real = ResolvePath(path);
                if (real != null)
                {
                    try { using (Icon ic = Icon.ExtractAssociatedIcon(real)) bmp = ic.ToBitmap(); } catch { bmp = null; }
                }
                else if (path != null && path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    bmp = ShellIcon.FromShell(path);   // Store apps, Discord, Steam ... as listed in the Start menu
                }
                cache[key] = bmp;
                return bmp;
            }
        }
    }

    // Icons for "shell:" targets (Store apps and Start menu entries have no file path to extract from).
    internal static class ShellIcon
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll")]
        static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attrs, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_PIDL = 0x8;

        public static Bitmap FromShell(string path)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                uint attrs;
                if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attrs) != 0 || pidl == IntPtr.Zero) return null;
                SHFILEINFO info = new SHFILEINFO();
                IntPtr ok = SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);
                if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                try { using (Icon ic = Icon.FromHandle(info.hIcon)) return ic.ToBitmap(); }
                finally { DestroyIcon(info.hIcon); }
            }
            catch { return null; }
            finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
        }
    }
}
