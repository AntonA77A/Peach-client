using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Peach
{
    // What the embedded Discord window may navigate to / ask for. Pure functions (unit-tested).
    // The window shows the OFFICIAL Discord web app. Peach never sees your login or token: you sign in
    // on discord.com inside the window, exactly like in a browser.
    internal static class DiscordNav
    {
        // hosts the Discord page itself lives on
        static readonly string[] DiscordHosts = { "discord.com", "discordapp.com", "discordapp.net", "discord.gg", "discord.media" };
        // the login captcha is served from here
        static readonly string[] HelperHosts = { "hcaptcha.com" };

        static bool HostIn(string host, string[] list)
        {
            host = host.ToLowerInvariant();
            foreach (string d in list) if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) return true;
            return false;
        }

        static bool TryHttps(string url, out Uri u)
        {
            u = null;
            return !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps;
        }

        // May the window itself (top level) navigate here? Everything else is handed to the normal browser.
        public static bool IsAllowed(string url)
        {
            if (url == "about:blank") return true;
            Uri u;
            return TryHttps(url, out u) && (HostIn(u.Host, DiscordHosts) || HostIn(u.Host, HelperHosts));
        }

        public static bool IsDiscordPage(string url)
        {
            Uri u;
            return TryHttps(url, out u) && HostIn(u.Host, DiscordHosts);
        }

        // Permission prompts (WebView2 kinds by name): voice/video/notifications/paste only for Discord itself.
        public static bool AllowPermission(string kind, string originUrl)
        {
            if (!IsDiscordPage(originUrl)) return false;
            return kind == "Microphone" || kind == "Camera" || kind == "Notifications" || kind == "ClipboardRead";
        }

        // Only these may be opened in the user's default programs (no file:, ms-msdt:, ... from a web page).
        public static bool IsSafeExternal(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            return u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeMailto;
        }
    }

    // Paths like "peach:discord" in app lists (bar, launcher) open a Peach window instead of a program.
    internal static class PeachLinks
    {
        public static string Parse(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("peach:", StringComparison.OrdinalIgnoreCase)) return null;
            return path.Substring(6).Trim().Trim('/').ToLowerInvariant();
        }
    }

    // Finds Microsoft's WebView2 libraries in lib\webview2 (they are not next to peach.exe) and the right native loader.
    // Must run before anything that touches WebView2 types, so keep those out of Main.
    internal static class WebView2Setup
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);

        public static string LibDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "webview2"); } }

        static string Arch()
        {
            string a = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
            if (a == "ARM64") return "arm64";
            return Environment.Is64BitProcess ? "x64" : "x86";
        }

        public static string LoaderPath { get { return Path.Combine(LibDir, Arch(), "WebView2Loader.dll"); } }

        public static bool Available
        {
            get
            {
                return File.Exists(Path.Combine(LibDir, "Microsoft.Web.WebView2.Core.dll")) && File.Exists(Path.Combine(LibDir, "Microsoft.Web.WebView2.WinForms.dll")) && File.Exists(LoaderPath);
            }
        }

        static bool initialised;

        public static void Init()
        {
            if (initialised) return;
            initialised = true;
            AppDomain.CurrentDomain.AssemblyResolve += delegate (object s, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name;
                if (!name.StartsWith("Microsoft.Web.WebView2.", StringComparison.Ordinal)) return null;
                string f = Path.Combine(LibDir, name + ".dll");
                return File.Exists(f) ? Assembly.LoadFrom(f) : null;
            };
            try { if (File.Exists(LoaderPath)) LoadLibrary(LoaderPath); } catch { }   // the later DllImport("WebView2Loader.dll") finds the loaded module
        }

        // Version of the installed Microsoft Edge WebView2 Runtime (null = not installed).
        public static string RuntimeVersion()
        {
            string[] keys = {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" };
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (string k in keys)
                    try
                    {
                        using (RegistryKey rk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(k))
                        {
                            object v = rk == null ? null : rk.GetValue("pv");
                            if (v != null && v.ToString() != "0.0.0.0") return v.ToString();
                        }
                    }
                    catch { }
            return null;
        }
    }

    // Opens / ends the Discord window. Without the WebView2 libraries the build still works (feature off, with a hint).
    internal static class DiscordLauncher
    {
#if WEBVIEW2
        static DiscordForm form;

        public static bool Available { get { return WebView2Setup.Available; } }
        public static bool IsOpen { get { return form != null && !form.IsDisposed && form.Visible; } }

        public static void Open(Config cfg)
        {
            if (!Available) { Dialogs.Info(null, "Discord", "Die WebView2-Bibliotheken fehlen (Ordner lib\\webview2 neben peach.exe)."); return; }
            if (form == null || form.IsDisposed)
                form = new DiscordForm(System.IO.Path.Combine(cfg.ConfigDir(), "webview2", "discord"), "https://discord.com/app", cfg);
            form.ShowAndFocus();
        }

        // Settings (themes, commands, CSS, scripts): work with or without the window; an open window follows live.
        public static void OpenSettings(Config cfg)
        {
            using (DiscordSettingsForm f = new DiscordSettingsForm(cfg, delegate (bool scripts) { if (form != null && !form.IsDisposed) form.ApplySettings(scripts); })) f.ShowDialog();
        }

        // Ends the session (leaves voice) and hides the window.
        public static void Stop() { if (form != null && !form.IsDisposed) form.EndSession(); }

        public static void Dispose() { if (form != null && !form.IsDisposed) form.ForceClose(); form = null; }
#else
        public static bool Available { get { return false; } }
        public static bool IsOpen { get { return false; } }
        public static void Open(Config cfg) { Dialogs.Info(null, "Discord", "Diese Peach-Version wurde ohne WebView2 gebaut (lib\\webview2 fehlte beim Bauen)."); }
        public static void OpenSettings(Config cfg) { using (DiscordSettingsForm f = new DiscordSettingsForm(cfg, delegate (bool scripts) { })) f.ShowDialog(); }
        public static void Stop() { }
        public static void Dispose() { }
#endif
    }
}
