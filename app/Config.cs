using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Peach
{
    public class HotkeyEntry
    {
        public string Combo;    // e.g. "ctrl+alt+k"
        public string Action;   // "open" | "type"
        public string Target;   // program / file / URL, or the text to type
    }

    public class Snippet
    {
        public string Abbr;
        public string Text;
    }

    public class AppEntry
    {
        public string Name;
        public string Path;     // program, file, folder, URL or protocol like ms-settings:
        public string Args;
    }

    // The Peach bar: your own bar at the top or bottom of the screen (normal desktop mode only).
    public class BarConfig
    {
        public bool Enabled = false;
        public string Position = "bottom";     // "bottom" | "top"
        public int Height = 44;                // 28..96
        public string Color = "#261C19";       // background
        public string Accent = "#FF7A59";      // highlights
        public int Opacity = 96;               // percent, 30..100
        public bool ShowMenuButton = true;     // peach button -> Peach menu
        public bool ShowWindows = true;        // open windows, click to switch
        public bool ShowClock = true;
        public bool ShowDate = true;
        public List<AppEntry> Pinned = new List<AppEntry>();   // app buttons on the left
    }

    public class CommandEntry
    {
        public string Name;    // typed as /name in the Discord message box
        public string Text;    // action "text": what it turns into ({args}, {date}, {time} are filled in)
                               // action "open": the program, file or URL to open (https URLs get {args} URL-encoded)
        public string Action;  // null / "text" = put the text in the message box, "open" = open Text with Peach
        public string Args;    // action "open" with a program: fixed arguments (never filled from chat)
        public string Hotkey;  // optional, e.g. "ctrl+alt+1": runs the command, only while the Discord window has the focus
    }

    // Settings of the Discord window (themes, commands, scripts). Applied live, stored next to the other settings.
    public class DiscordConfig
    {
        public string Theme = "";             // "" = Discord's own colours | peach | mitternacht | wald | amoled | violett
        public string Accent = "";            // "#RRGGBB" replaces Discord's blue, "" = leave it
        public string Font = "";              // "" = Discord's font
        public int Zoom = 100;                // percent, 60..160
        public bool ReduceMotion = false;
        public bool SendCommandsNow = false;  // false: the expanded text waits in the message box until you press Enter
        public string CustomCss = "";
        public bool Seeded = false;
        public List<CommandEntry> Commands = new List<CommandEntry>();
        public List<string> EnabledScripts = new List<string>();   // script files that may run (opt-in)
        public List<Snippet> Abbreviations = new List<Snippet>();  // "Fast Type" inside the Discord message box only
    }

    // Game boost: more performance while a game runs, everything back to normal afterwards.
    public class GameBoostConfig
    {
        public bool Auto = false;             // start by itself when one of the games below has the focus
        public bool PowerPlan = true;         // switch to the high performance power plan
        public bool Priority = true;          // run the game with high priority
        public List<string> Games = new List<string>();      // exe names, e.g. "game.exe"
        public List<string> CloseApps = new List<string>();  // background programs that are asked to close (never killed)
        public string RestoreScheme;          // power plan to put back (kept until it is restored, also after a crash)
    }

    // One Minecraft profile: own game folder with its own mods, started through the official launcher.
    public class McProfile
    {
        public string Id;          // folder name, e.g. "survival-1-21-1"
        public string Name;
        public string McVersion;   // e.g. "1.21.1"
        public string Loader;      // "fabric" | "vanilla"
        public string VersionId;   // what the official launcher starts, e.g. "fabric-loader-0.16.9-1.21.1"
    }

    // Minecraft area: profiles, mods and the widget overlay (works with any version, no mod needed).
    public class MinecraftConfig
    {
        public List<McProfile> Profiles = new List<McProfile>();
        public string Selected;                         // profile id
        public string ClientId = "";                    // your own Azure app (public client) for the Microsoft login
        public string AccountName = "", AccountUuid = "";
        public string RefreshToken = "";                // protected with Windows DPAPI (only this Windows user can read it)
        public int RamMb = 4096;                        // game memory (-Xmx), 1024..32768
        public bool OverlayEnabled = false;
        public bool OverlayOnlyMinecraft = true;        // only while the Minecraft window has the focus
        public string OverlayCorner = "topright";       // topleft | topright | bottomleft | bottomright
        public int OverlayOpacity = 85;                 // percent, 30..100
        public List<string> Widgets = new List<string> { "spotify", "cpu", "gpu", "ram", "clock", "session" };
    }

    public class Config
    {
        public MinecraftConfig Minecraft = new MinecraftConfig();
        public GameBoostConfig GameBoost = new GameBoostConfig();
        public DiscordConfig Discord = new DiscordConfig();
        public BarConfig Bar = new BarConfig();
        public bool ControllerMode = false;    // launcher UI + controller mouse stay on until switched off
        public bool AppsSeeded = false;
        public bool AddonHostAutostart = false;   // start the script-addon host (Node.js) together with Peach
        public bool GameBarAsked = false;      // the one-time "switch off the Game Bar on the Xbox button?" question was shown
        public List<AppEntry> Apps = new List<AppEntry>();
        public bool MouseEnabled = false;
        public int MouseSpeed = 1200;          // pixels per second at full stick deflection
        public bool FastTypeEnabled = true;
        public bool AutoKeyboard = true;       // show the on-screen keyboard when a text field gets focus (controller mouse on)
        public List<HotkeyEntry> Hotkeys = new List<HotkeyEntry>();
        public List<Snippet> Snippets = new List<Snippet>();

        string path;

        public string ConfigDir() { return Path.GetDirectoryName(path); }

        public static string DefaultPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PeachClient", "config.json");
        }

        public static Config Load(string path)
        {
            if (path == null) path = DefaultPath();
            Config c = null;
            try
            {
                if (File.Exists(path))
                    c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { /* corrupt file: fall back to defaults below */ }
            bool fresh = c == null;
            if (c == null) c = new Config();
            if (c.Hotkeys == null) c.Hotkeys = new List<HotkeyEntry>();
            if (c.Snippets == null) c.Snippets = new List<Snippet>();
            c.path = path;
            if (c.Apps == null) c.Apps = new List<AppEntry>();
            if (c.Bar == null) c.Bar = new BarConfig();
            if (c.Minecraft == null) c.Minecraft = new MinecraftConfig();
            if (c.Minecraft.Profiles == null) c.Minecraft.Profiles = new List<McProfile>();
            if (c.Minecraft.Widgets == null) c.Minecraft.Widgets = new List<string>();
            if (c.Minecraft.ClientId == null) c.Minecraft.ClientId = "";
            if (c.Minecraft.AccountName == null) c.Minecraft.AccountName = "";
            if (c.Minecraft.AccountUuid == null) c.Minecraft.AccountUuid = "";
            if (c.Minecraft.RefreshToken == null) c.Minecraft.RefreshToken = "";
            c.Minecraft.RamMb = Math.Max(1024, Math.Min(32768, c.Minecraft.RamMb));
            c.Minecraft.OverlayOpacity =Math.Max(30, Math.Min(100, c.Minecraft.OverlayOpacity));
            if (Array.IndexOf(new string[] { "topleft", "topright", "bottomleft", "bottomright" }, c.Minecraft.OverlayCorner) < 0) c.Minecraft.OverlayCorner = "topright";
            if (c.GameBoost == null) c.GameBoost = new GameBoostConfig();
            if (c.GameBoost.Games == null) c.GameBoost.Games = new List<string>();
            if (c.GameBoost.CloseApps == null) c.GameBoost.CloseApps = new List<string>();
            if (c.Discord == null) c.Discord = new DiscordConfig();
            if (c.Discord.Commands == null) c.Discord.Commands = new List<CommandEntry>();
            if (c.Discord.EnabledScripts == null) c.Discord.EnabledScripts = new List<string>();
            if (c.Discord.Abbreviations == null) c.Discord.Abbreviations = new List<Snippet>();
            if (c.Discord.CustomCss == null) c.Discord.CustomCss = "";
            c.Discord.Zoom = Math.Max(60, Math.Min(160, c.Discord.Zoom));
            if (!c.Discord.Seeded)
            {
                c.Discord.Seeded = true;
                c.Discord.Commands.Add(new CommandEntry { Name = "zeit", Text = "Es ist {time} Uhr ({date})" });
                c.Discord.Commands.Add(new CommandEntry { Name = "gg", Text = "gg wp {args}" });
                c.Save();
            }
            if (c.Bar.Pinned == null) c.Bar.Pinned = new List<AppEntry>();
            c.Bar.Height = Math.Max(28, Math.Min(96, c.Bar.Height));
            c.Bar.Opacity = Math.Max(30, Math.Min(100, c.Bar.Opacity));
            if (c.Bar.Position != "top") c.Bar.Position = "bottom";
            if (!c.AppsSeeded)
            {
                c.Apps.Add(new AppEntry { Name = "Browser", Path = "https://www.google.com" });
                c.Apps.Add(new AppEntry { Name = "Explorer", Path = "explorer.exe" });
                c.Apps.Add(new AppEntry { Name = "Einstellungen", Path = "ms-settings:" });
                c.Apps.Add(new AppEntry { Name = "Rechner", Path = "calc.exe" });
                c.Apps.Add(new AppEntry { Name = "Editor", Path = "notepad.exe" });
                c.AppsSeeded = true;
                c.Save();
            }
            if (fresh)
            {
                c.Snippets.Add(new Snippet { Abbr = ";mfg", Text = "Mit freundlichen Grüßen" });
                c.Snippets.Add(new Snippet { Abbr = ";lg", Text = "Liebe Grüße" });
                c.Snippets.Add(new Snippet { Abbr = ";datum", Text = "{date}" });
                c.Snippets.Add(new Snippet { Abbr = ";zeit", Text = "{time}" });
                c.Save();
            }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            }
            catch { /* read-only profile: keep running with in-memory settings */ }
        }
    }
}
