using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Peach
{
    // `peach.exe --selftest <outfile>` checks the pure logic and writes a report. Exit code 0 = all passed.
    internal static class SelfTest
    {
        static int failed;
        static StringBuilder log = new StringBuilder();

        static void Check(bool ok, string name)
        {
            if (!ok) failed++;
            log.AppendLine((ok ? "PASS " : "FAIL ") + name);
        }

        static string T(KeyOutput o) { return o.Text ?? (o.Vk != 0 ? "vk" + o.Vk : (o.Close ? "close" : "-")); }

        static void KeyboardTests()
        {
            KeyboardModel m = new KeyboardModel();
            Check(m.RowKeys(m.Row)[m.Col].Lower == "h", "Keyboard: starts on 'h'");
            m.Handle(PadKey.Right);
            Check(T(m.Handle(PadKey.Select)) == "j", "Keyboard: D-pad right then A types 'j'");
            m.Handle(PadKey.Shift);
            Check(T(m.Handle(PadKey.Select)) == "J" && !m.Shift, "Keyboard: Shift is one-shot (types 'J' once)");
            Check(T(m.Handle(PadKey.Select)) == "j", "Keyboard: back to lower case afterwards");

            m.Reset();
            m.Handle(PadKey.Up);
            Check(m.Row == 1 && m.RowKeys(1)[m.Col].Lower == "z", "Keyboard: up from 'h' lands on a key above ('z')");
            m.Handle(PadKey.Down); m.Handle(PadKey.Down);
            Check(m.Row == 3, "Keyboard: down twice reaches row 4");
            m.Reset(); m.Row = 0; m.Handle(PadKey.Up);
            Check(m.Row == KeyboardModel.Rows - 1, "Keyboard: up from the top wraps to the bottom row");
            m.Reset(); m.Col = 0; m.Handle(PadKey.Left);
            Check(m.Col == m.RowKeys(m.Row).Length - 1, "Keyboard: left wraps inside the row");

            m.Reset();
            Check(T(m.Handle(PadKey.Space)) == " ", "Keyboard: X = space");
            Check(T(m.Handle(PadKey.Backspace)) == "vk8", "Keyboard: B = backspace");
            Check(T(m.Handle(PadKey.Enter)) == "vk13", "Keyboard: Start = enter");
            KeyOutput cl = m.Handle(PadKey.CursorLeft);
            Check(cl.Vk == 0x25 && cl.Extended, "Keyboard: LB = cursor left (extended key)");
            Check(m.Handle(PadKey.Close).Close, "Keyboard: Back closes");

            m.Reset(); m.Row = 4; m.Col = 0;
            m.Handle(PadKey.Select);
            Check(m.Layer == 1, "Keyboard: ?123 switches to the symbol layer");
            m.Row = 1; m.Col = 0;
            Check(T(m.Handle(PadKey.Select)) == "@", "Keyboard: symbol layer types '@'");
            m.Row = 4; m.Col = 0; m.Handle(PadKey.Select);
            Check(m.Layer == 0, "Keyboard: ABC switches back to letters");

            m.Reset(); m.Row = 0; m.Col = 1; m.Shift = true;
            Check(T(m.Handle(PadKey.Select)) == "\"", "Keyboard: Shift+2 = \"");
            m.Reset();
            Check(T(m.PressAt(3, 1)) == "y", "Keyboard: click on row 4 key 2 types 'y'");
            bool shape = true;
            for (int l = 0; l < 2; l++) { m.Layer = l; for (int r = 0; r < KeyboardModel.Rows; r++) if (m.RowKeys(r).Length < 5) shape = false; }
            Check(shape, "Keyboard: both layers have 5 filled rows");
        }

        static Native.XINPUT_STATE Pad(ushort buttons, byte rt)
        {
            Native.XINPUT_STATE s = new Native.XINPUT_STATE();
            s.Gamepad.wButtons = buttons;
            s.Gamepad.bRightTrigger = rt;
            return s;
        }

        static Native.XINPUT_STATE PadStick(short lx, short ly)
        {
            Native.XINPUT_STATE s = new Native.XINPUT_STATE();
            s.Gamepad.sThumbLX = lx;
            s.Gamepad.sThumbLY = ly;
            return s;
        }

        static void PadTests()
        {
            const ushort DRight = 0x8, Start = 0x10, Back = 0x20, BtnA = 0x1000, BtnB = 0x2000, BtnX = 0x4000, BtnY = 0x8000, LB = 0x100, Guide = 0x400;
            List<PadKey> got = new List<PadKey>();
            int toggles = 0;
            GamepadMouse g = new GamepadMouse(true, 1200, false);
            bool open = true;
            g.KeyboardOpen = delegate { return open; };
            g.KeyboardInput = delegate (PadKey k) { got.Add(k); };
            g.KeyboardToggle = delegate { toggles++; };
            double t = 0;
            Action<ushort> step = delegate (ushort b) { g.Step(Pad(b, 0), t, 0.016); t += 0.016; };
            Func<string> dump = delegate { string s = ""; foreach (PadKey k in got) s += k + ","; got.Clear(); return s; };

            step(0); step(DRight); step(0);
            Check(dump() == "Right,", "Pad: D-pad right press sends one Right");
            step(DRight); t += 0.5; step(DRight); t += 0.07; step(DRight); step(0);
            Check(dump() == "Right,Right,Right,", "Pad: holding D-pad repeats after a delay");
            step(BtnA); step(0); step(BtnX); step(0); step(BtnY); step(0);
            Check(dump() == "Select,Space,Shift,", "Pad: A=Select, X=Space, Y=Shift while keyboard open");
            step(BtnB); step(0);
            Check(dump() == "Backspace,", "Pad: B = Backspace");
            step(LB); step(0);
            Check(dump() == "CursorLeft,", "Pad: LB = cursor left");
            step(Start); step(0);
            Check(dump() == "Enter,", "Pad: Start = Enter");
            step(Back); step(0);
            Check(dump() == "Close,", "Pad: Back (alone) closes the keyboard");

            step(Back); step(Back | Start); step(0);
            Check(dump() == "" && g.Enabled == false, "Pad: Back+Start toggles the mouse off without Close/Enter");
            step(Back); step(Back | Start); step(0);
            Check(g.Enabled == true && dump() == "", "Pad: Back+Start toggles it on again");

            open = false;
            step(BtnY); step(0);
            Check(toggles == 1 && dump() == "", "Pad: Y opens the keyboard when it is closed");
            g.Enabled = false;
            step(BtnA); step(0); step(Back); step(0);
            Check(dump() == "", "Pad: nothing is sent while the controller mouse is off");

            // ---- controller mode: Home combo, lock, launcher navigation ----
            int homes = 0;
            g.HomeRequested = delegate { homes++; };
            g.Enabled = true; got.Clear(); toggles = 0;
            step(Back); step(Back | BtnY); step(0);
            Check(homes == 1 && toggles == 0 && dump() == "", "Pad: Back+Y fires Home (no keyboard toggle, no Close)");
            step(BtnY); step(0);
            Check(homes == 1 && toggles == 1, "Pad: Y alone still opens the keyboard");
            g.Enabled = false;
            step(Back); step(Back | BtnY); step(0);
            Check(homes == 2, "Pad: Back+Y works even while the pad is off");
            g.Enabled = true;
            step(Guide); step(0);
            Check(homes == 3 && dump() == "", "Pad: the Xbox/Home button fires Home");
            step(Guide); step(Guide); step(0);
            Check(homes == 4, "Pad: holding Home fires once, not repeatedly");
            g.Enabled = false;
            step(Guide); step(0);
            Check(homes == 5, "Pad: the Xbox/Home button works even while the pad is off");
            g.Enabled = true;

            g.Locked = true;
            step(Back); step(Back | Start); step(0);
            Check(g.Enabled == true, "Pad: Back+Start is ignored while controller mode is locked");
            g.Locked = false;

            List<PadKey> nav = new List<PadKey>();
            bool launcherOpen = true;
            g.LauncherOpen = delegate { return launcherOpen; };
            g.LauncherInput = delegate (PadKey k) { nav.Add(k); };
            Func<string> navDump = delegate { string s = ""; foreach (PadKey k in nav) s += k + ","; nav.Clear(); return s; };
            open = true; got.Clear();
            step(DRight); step(0); step(BtnA); step(0); step(BtnB); step(0); step(BtnB); step(0);
            Check(navDump() == "Right,Select," && dump() == "", "Pad: launcher gets D-pad and A (B does nothing), keyboard stays silent");
            g.Step(PadStick(30000, 0), t, 0.016); t += 0.016;
            g.Step(PadStick(0, 0), t, 0.016); t += 0.016;
            Check(navDump() == "Right,", "Pad: left stick right navigates the launcher");
            g.Step(PadStick(0, -30000), t, 0.016); t += 0.016;
            g.Step(PadStick(0, 0), t, 0.016); t += 0.016;
            Check(navDump() == "Down,", "Pad: left stick down navigates the launcher");
            launcherOpen = false; open = false;
            step(DRight); step(0);
            Check(navDump() == "", "Pad: with an app in front (launcher not in front) the pad no longer navigates tiles");

            // XInput must expose the Guide button export on this Windows (otherwise only Back + Y works)
            Native.XINPUT_STATE probe;
            Native.PadState(0, out probe);
            Check(Native.XInputMode == 0, "XInput: Guide-button export (#100) is available (mode " + Native.XInputMode + ")");
        }

        static void LauncherTests()
        {
            LauncherModel m = new LauncherModel(7, 3);   // rows: 3,3,1
            m.Move(PadKey.Right);
            Check(m.Index == 1, "Launcher: right moves to the next tile");
            m.Move(PadKey.Down);
            Check(m.Index == 4, "Launcher: down moves one row");
            m.Move(PadKey.Down);
            Check(m.Index == 6, "Launcher: down into a short last row lands on its last tile");
            m.Move(PadKey.Right);
            Check(m.Index == 6, "Launcher: right stops at the end");
            m.Move(PadKey.Up);
            Check(m.Index == 3, "Launcher: up goes back one row");
            m.Index = 0; m.Move(PadKey.Left); m.Move(PadKey.Up);
            Check(m.Index == 0, "Launcher: left/up stop at the start");
            LauncherModel one = new LauncherModel(1, 5);
            one.Move(PadKey.Down); one.Move(PadKey.Right);
            Check(one.Index == 0, "Launcher: a single tile stays selected");
            Check(new LauncherModel(10, 4).Row == 0 && new LauncherModel(3, 0).Cols == 1, "Launcher: sane defaults");
        }

        static bool Overlap(List<BarItem> l)
        {
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                    if (l[i].Rect.IntersectsWith(l[j].Rect)) return true;
            return false;
        }

        static int Count(List<BarItem> l, int kind) { int n = 0; foreach (BarItem b in l) if (b.Kind == kind) n++; return n; }

        static void BarTests()
        {
            // when the bar is shown: only for normal desktop mode
            Check(BarLogic.ShouldShow(true, false, false), "Bar: shown on the normal desktop");
            Check(BarLogic.ShouldShow(true, false, true), "Bar: shown when controller mode is off even with a controller plugged in");
            Check(!BarLogic.ShouldShow(true, true, true), "Bar: hidden while controller mode is active (mode on + controller connected)");
            Check(BarLogic.ShouldShow(true, true, false), "Bar: back again while controller mode is paused (controller unplugged)");
            Check(!BarLogic.ShouldShow(false, false, false), "Bar: stays hidden when switched off");

            // colours
            Check(BarLogic.ToHex(BarLogic.ParseColor("#FF7A59", System.Drawing.Color.Black)) == "#FF7A59", "Bar: colour round trip #FF7A59");
            Check(BarLogic.ParseColor("26 1C 19", System.Drawing.Color.Red) == System.Drawing.Color.Red && BarLogic.ParseColor(null, System.Drawing.Color.Blue) == System.Drawing.Color.Blue, "Bar: bad colour text falls back");
            Check(BarLogic.IsLight(System.Drawing.Color.White) && !BarLogic.IsLight(System.Drawing.Color.FromArgb(38, 28, 25)), "Bar: text colour contrast (light vs dark bar)");

            // layout
            BarConfig c = new BarConfig();
            for (int i = 0; i < 3; i++) c.Pinned.Add(new AppEntry { Name = "A" + i, Path = "x" + i });
            List<BarItem> l = BarLogic.Layout(1200, 44, c, 5);
            bool inside = true;
            foreach (BarItem b in l) if (b.Rect.Left < 0 || b.Rect.Top < 0 || b.Rect.Right > 1200 || b.Rect.Bottom > 44) inside = false;
            Check(Count(l, BarItem.Menu) == 1 && Count(l, BarItem.Pinned) == 3 && Count(l, BarItem.Window) == 5 && Count(l, BarItem.Clock) == 1, "Bar layout: menu button, 3 pinned apps, 5 windows, clock");
            Check(inside && !Overlap(l), "Bar layout: everything inside the bar and nothing overlaps");
            List<BarItem> many = BarLogic.Layout(800, 44, c, 40);
            bool wide = true;
            foreach (BarItem b in many) if (b.Kind == BarItem.Window && b.Rect.Width < 40) wide = false;
            Check(Count(many, BarItem.Window) > 0 && Count(many, BarItem.Window) < 40 && wide && !Overlap(many), "Bar layout: too many windows - shows what fits, buttons stay readable, no overlap");
            c.ShowWindows = false; c.ShowClock = false; c.ShowDate = false; c.ShowMenuButton = false;
            List<BarItem> bare = BarLogic.Layout(1200, 44, c, 5);
            Check(Count(bare, BarItem.Window) == 0 && Count(bare, BarItem.Clock) == 0 && Count(bare, BarItem.Menu) == 0 && Count(bare, BarItem.Pinned) == 3, "Bar layout: switched-off parts disappear, pinned apps stay");
            c = new BarConfig();
            Check(Count(BarLogic.Layout(1000, 44, c, 0), BarItem.Window) == 0, "Bar layout: no windows, no window buttons");

            // open windows as a taskbar would list them
            List<WinInfo> wins = WindowList.Snapshot(0);
            bool titles = true;
            foreach (WinInfo w in wins) if (string.IsNullOrEmpty(w.Title)) titles = false;
            Check(wins.Count > 0 && titles, "WindowList: lists this PC's open windows (" + wins.Count + " found)");

            // config limits survive a reload
            string tmp = Path.Combine(Path.GetTempPath(), "peach-bar-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Config cfg = Config.Load(tmp);
                Check(!cfg.Bar.Enabled && cfg.Bar.Position == "bottom" && cfg.Bar.Height == 44, "Bar config: off by default, bottom, 44 px");
                cfg.Bar.Enabled = true; cfg.Bar.Position = "top"; cfg.Bar.Height = 500; cfg.Bar.Opacity = 5; cfg.Bar.Color = "#102030";
                cfg.Bar.Pinned.Add(new AppEntry { Name = "Discord", Path = "shell:AppsFolder\\com.squirrel.Discord.Discord" });
                cfg.Save();
                Config again = Config.Load(tmp);
                Check(again.Bar.Enabled && again.Bar.Position == "top" && again.Bar.Height == 96 && again.Bar.Opacity == 30 && again.Bar.Color == "#102030" && again.Bar.Pinned.Count == 1 && again.Bar.Pinned[0].Name == "Discord",
                      "Bar config: settings persist, height/opacity are clamped (96 px / 30 %)");
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        static string TempDir() { string d = Path.Combine(Path.GetTempPath(), "peach-selftest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        static void AddonRowTests()
        {
            List<BuiltinAddon> b = new List<BuiltinAddon>();
            b.Add(new BuiltinAddon { Name = "Fast Type", IsOn = delegate { return true; }, SetOn = delegate (bool v) { }, Customize = delegate { }, Chips = new string[] { "a", "b" } });
            b.Add(new BuiltinAddon { Name = "Hotkeys", IsOn = delegate { return true; } });
            List<AddonInfo> a = new List<AddonInfo>();
            a.Add(new AddonInfo { Id = "x", Name = "Eins", Entry = "main.py", Language = "Python", Version = "1.0.0", Status = "running", Permissions = new List<string> { "notify" } });
            a.Add(new AddonInfo { Id = "y", Name = "Zwei", Entry = "main.js", Status = "crashed" });
            a.Add(new AddonInfo { Id = "z", Name = "Drei", Entry = "main.ps1", Status = "stopped", Description = "beschrieben" });

            List<RowItem> all = AddonRows.Build(b, a, true, "", true, 0);
            Check(all.Count == 7 && all[0].IsHeader && all[3].IsHeader, "AddonRows: two sections - header + 2 built-in + header + 3 script addons");
            Check(all[1].HasToggle && all[1].Actions.Count == 1 && all[1].Chips.Count == 2 && !all[2].HasToggle && all[2].Actions.Count == 0, "AddonRows: built-in cards - switch/customize only where available");
            Check(all[4].On && all[4].Status == "läuft" && all[5].Status == "abgestürzt" && !all[6].On && all[6].Subtitle == "beschrieben", "AddonRows: script addon status (running / crashed / stopped) and subtitle");
            Check(all[4].Actions.Count == 3 && all[4].Actions[0].Id == AddonRows.ActEdit && all[4].Actions[2].Danger && all[4].Chips.Contains("Python") && all[4].Chips.Contains("notify") && all[5].Chips.Contains("keine Rechte"), "AddonRows: Log + Entfernen buttons, language/permission chips");
            Check(AddonRows.Build(b, a, true, "", true, 1).Count == 3 && AddonRows.Build(b, a, true, "", true, 2).Count == 4, "AddonRows: filters (built-in only / script addons only)");
            List<RowItem> off = AddonRows.Build(b, a, false, "Node fehlt", true, 2);
            Check(off.Count == 2 && off[1].Title == "Addon-Host nicht verbunden" && off[1].Subtitle == "Node fehlt" && off[1].Actions.Count == 1, "AddonRows: host not connected shows why, with a start button");
            Check(AddonRows.Build(b, a, false, "x", false, 2)[1].Actions.Count == 0, "AddonRows: no start button when Node.js is missing");
            Check(AddonRows.Build(b, new List<AddonInfo>(), true, "", true, 2)[1].Title == "Noch keine Script-Addons", "AddonRows: friendly empty state");
            string s1 = AddonRows.Signature(all);
            a[1].Status = "running";
            Check(AddonRows.Signature(AddonRows.Build(b, a, true, "", true, 0)) != s1, "AddonRows: signature changes when a status changes");
            Check(AddonRows.LanguageLetters("main.ps1") == "PS1" && AddonRows.LanguageLetters("x.mjs") == "MJS" && AddonRows.LanguageColor("a.py") != AddonRows.LanguageColor("a.js"), "AddonRows: language badge letters and colours");
        }

        static void AddonHostParseTests()
        {
            string json = "{\"mods\":[{\"id\":\"hello-js\",\"name\":\"hello-js\",\"version\":\"0.1.0\",\"description\":\"\",\"author\":\"\",\"language\":\"JavaScript\",\"entry\":\"main.js\"," +
                          "\"permissions\":[\"notify\",\"clipboard\"],\"unknownPermissions\":[\"bogus\"],\"enabled\":true,\"status\":\"running\",\"pid\":1234,\"startedAt\":1700000000000," +
                          "\"log\":[{\"t\":1700000000000,\"s\":\"out\",\"m\":\"hi\"},{\"t\":1700000001000,\"s\":\"err\",\"m\":\"oops\"}]}],\"runners\":[],\"version\":\"0.1.0\"}";
            string version;
            List<AddonInfo> l = AddonHost.ParseState(json, out version);
            Check(version == "0.1.0" && l.Count == 1 && l[0].Id == "hello-js" && l[0].Status == "running" && l[0].Pid == 1234 && l[0].Enabled, "AddonHost.ParseState: reads the host's /ui/state (id, status, pid, enabled)");
            Check(l[0].Permissions.Count == 2 && l[0].Unknown.Count == 1 && l[0].Log.Count == 2 && l[0].Log[0].T == 1700000000000L && l[0].Log[1].S == "err" && l[0].Log[1].M == "oops", "AddonHost.ParseState: permissions, unknown permissions and log lines with timestamps");
            Check(AddonHost.ParseState("{\"mods\":[],\"version\":\"9\"}", out version).Count == 0 && version == "9", "AddonHost.ParseState: empty list");
        }

        static void AddonFileTests()
        {
            Check(AddonFiles.Slug("Mein Fenster-Helfer!") == "mein-fenster-helfer" && AddonFiles.Slug("Übergrößen") == "uebergroessen" && AddonFiles.Slug("   ") == "addon" && AddonFiles.Slug("../..\\x") == "x", "AddonFiles.Slug: safe folder names (umlauts, symbols, path tricks)");
            string root = TempDir();
            try
            {
                string mods = Path.Combine(root, "mods"), err;
                bool allGood = true; string firstBad = "";
                for (int i = 0; i < AddonFiles.Languages.Length; i++)
                {
                    string lang = AddonFiles.Languages[i];
                    string id = AddonFiles.Scaffold(mods, "Test " + lang, "Beschreibung mit \"Anführungszeichen\"", lang, new List<string> { "notify", "clipboard" }, out err);
                    string dir = id == null ? null : Path.Combine(mods, id);
                    if (id == null || AddonFiles.ValidateModFolder(dir) != null || !File.Exists(Path.Combine(dir, "main." + lang))) { allGood = false; firstBad = lang + ": " + err; }
                }
                Check(allGood, "AddonFiles.Scaffold: valid addon folders for all 6 languages (mod.json + entry file) " + firstBad);

                string a = AddonFiles.Scaffold(mods, "Doppelt", "", "js", new List<string>(), out err);
                string b = AddonFiles.Scaffold(mods, "Doppelt", "", "js", new List<string>(), out err);
                Check(a == "doppelt" && b == "doppelt-2", "AddonFiles.Scaffold: same name twice gets a free folder (doppelt, doppelt-2)");
                string modJson = File.ReadAllText(Path.Combine(mods, "doppelt", "mod.json"));
                string noNotify = File.ReadAllText(Path.Combine(mods, "doppelt", "main.js"));
                Check(!noNotify.Contains("notify") && modJson.Contains("\"permissions\": []"), "AddonFiles.Scaffold: no notify call and no permission unless chosen");
                string withNotify = AddonFiles.Scaffold(mods, "Mit Notify", "", "py", new List<string> { "notify" }, out err);
                Check(File.ReadAllText(Path.Combine(mods, withNotify, "main.py")).Contains("peach.notify") && File.ReadAllText(Path.Combine(mods, withNotify, "mod.json")).Contains("\"notify\""), "AddonFiles.Scaffold: notify chosen -> permission in mod.json and call in the script");
                string ps = AddonFiles.Scaffold(mods, "Powershell Test", "", "ps1", new List<string>(), out err);
                byte[] head = new byte[3]; using (FileStream fs = File.OpenRead(Path.Combine(mods, ps, "main.ps1"))) fs.Read(head, 0, 3);
                Check(head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF, "AddonFiles.Scaffold: PowerShell script is written with BOM (umlauts survive Windows PowerShell 5.1)");
                Check(AddonFiles.Scaffold(mods, "", "", "js", new List<string>(), out err) == null && err != null && AddonFiles.Scaffold(mods, "X", "", "cobol", new List<string>(), out err) == null, "AddonFiles.Scaffold: empty name / unknown language are refused");

                // install from a folder
                string target = Path.Combine(root, "mods2");
                string idF = AddonFiles.InstallFolder(Path.Combine(mods, "doppelt"), target, out err);
                Check(idF == "doppelt" && File.Exists(Path.Combine(target, "doppelt", "main.js")), "AddonFiles.InstallFolder: copies a valid addon folder");
                string empty = Path.Combine(root, "leer"); Directory.CreateDirectory(empty);
                Check(AddonFiles.InstallFolder(empty, target, out err) == null && err.Contains("mod.json"), "AddonFiles.InstallFolder: folder without mod.json is refused");
                string evil = Path.Combine(root, "evil"); Directory.CreateDirectory(evil);
                File.WriteAllText(Path.Combine(evil, "mod.json"), "{\"name\":\"e\",\"entry\":\"..\\\\x.js\"}");
                Check(AddonFiles.ValidateModFolder(evil) != null, "AddonFiles.ValidateModFolder: entry that leaves the folder is refused");

                // install from zips
                string zipRoot = Path.Combine(root, "z1.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(mods, "doppelt"), zipRoot);
                string idZ = AddonFiles.InstallZip(zipRoot, target, out err);
                Check(idZ != null && File.Exists(Path.Combine(target, idZ, "mod.json")), "AddonFiles.InstallZip: zip with mod.json at the top");
                string zipDir = Path.Combine(root, "z2.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(mods, zipDir);   // many folders at the top -> not a single addon
                Check(AddonFiles.InstallZip(zipDir, target, out err) == null && err != null, "AddonFiles.InstallZip: zip with several top-level folders and no mod.json is refused");
                string wrapped = Path.Combine(root, "wrapper"); Directory.CreateDirectory(wrapped);
                string inner = Path.Combine(wrapped, "mein-addon"); Directory.CreateDirectory(inner);
                File.Copy(Path.Combine(mods, "doppelt", "mod.json"), Path.Combine(inner, "mod.json")); File.Copy(Path.Combine(mods, "doppelt", "main.js"), Path.Combine(inner, "main.js"));
                string zipWrapped = Path.Combine(root, "z3.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(wrapped, zipWrapped);
                string idW = AddonFiles.InstallZip(zipWrapped, target, out err);
                Check(idW == "mein-addon" && File.Exists(Path.Combine(target, "mein-addon", "main.js")), "AddonFiles.InstallZip: zip with one wrapping folder");

                // zip-slip: an entry that tries to write outside
                string zipEvil = Path.Combine(root, "z4.zip");
                using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.Open(zipEvil, System.IO.Compression.ZipArchiveMode.Create))
                {
                    using (StreamWriter w = new StreamWriter(za.CreateEntry("mod.json").Open())) w.Write("{\"name\":\"s\",\"entry\":\"main.js\"}");
                    using (StreamWriter w = new StreamWriter(za.CreateEntry("main.js").Open())) w.Write("//");
                    using (StreamWriter w = new StreamWriter(za.CreateEntry("../peach-zipslip-proof.txt").Open())) w.Write("pwned");
                }
                string slipTarget = Path.Combine(Path.GetTempPath(), "peach-zipslip-proof.txt");
                try { File.Delete(slipTarget); } catch { }
                string idS = AddonFiles.InstallZip(zipEvil, target, out err);
                Check(idS == null && !File.Exists(slipTarget) && !File.Exists(Path.Combine(root, "peach-zipslip-proof.txt")), "AddonFiles.InstallZip: zip-slip entry (../) is refused and nothing is written outside");

                // remove: only direct children of mods\
                Check(AddonFiles.Remove(mods, "doppelt-2", out err) && !Directory.Exists(Path.Combine(mods, "doppelt-2")), "AddonFiles.Remove: deletes the addon folder");
                Check(!AddonFiles.Remove(mods, "..", out err) && !AddonFiles.Remove(mods, "..\\x", out err) && !AddonFiles.Remove(mods, "a/b", out err) && !AddonFiles.Remove(mods, "", out err) && Directory.Exists(mods),
                      "AddonFiles.Remove: refuses '..', paths and empty ids");
                Check(!AddonFiles.Remove(mods, "gibts-nicht", out err) && err != null, "AddonFiles.Remove: unknown folder is reported, not ignored");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        static void ListGeometryTests()
        {
            PeachList l = new PeachList();
            l.Size = new System.Drawing.Size(600, 300); l.RowHeight = 92;
            RowItem h = new RowItem { IsHeader = true, Title = "Kopf" };
            RowItem r = new RowItem { Title = "A", HasToggle = true };
            r.Actions.Add(new RowAction("log", "Log", false)); r.Actions.Add(new RowAction("rm", "Entfernen", true));
            l.Rows.Add(h); l.Rows.Add(r); l.Rows.Add(new RowItem { Title = "B" }); l.Rows.Add(new RowItem { Title = "C" }); l.Rows.Add(new RowItem { Title = "D" });
            Check(l.RowTop(1) == PeachList.HeaderH && l.TotalHeight == PeachList.HeaderH + 4 * 92, "PeachList: header and row heights add up");
            System.Drawing.Rectangle tr = l.ToggleRect(1);
            System.Drawing.Rectangle c = l.CardRect(1);
            List<System.Drawing.Rectangle> acts = l.ActionRects(1);
            Check(acts.Count == 2 && acts[1].Right < tr.Left && acts[0].Right < acts[1].Left && tr.Right <= c.Right && acts[0].Left > c.Left + 150, "PeachList: toggle sits at the right edge, action buttons left of it without overlap");
            int row; string act;
            Check(l.HitTest(new System.Drawing.Point(tr.X + 10, tr.Y + 10), out row, out act) == 2 && row == 1, "PeachList: click on the switch is a toggle hit");
            Check(l.HitTest(new System.Drawing.Point(acts[1].X + 5, acts[1].Y + 5), out row, out act) == 3 && act == "rm", "PeachList: click on a button reports its id");
            Check(l.HitTest(new System.Drawing.Point(c.X + 30, c.Y + 8), out row, out act) == 1 && row == 1, "PeachList: click on the card body selects the row");
            Check(l.HitTest(new System.Drawing.Point(30, 5), out row, out act) == 0, "PeachList: section headers are not clickable");
            l.SelectRow(3);
            Check(l.SelectedIndex == 3 && l.SelectedIndices.Count == 1, "PeachList: SelectRow");
            l.SelectRow(0);
            Check(l.SelectedIndex == 3 || l.SelectedIndex == -1, "PeachList: headers cannot be selected");
        }

        // The real script-addon host (Node.js): start it, create an addon, start/stop it, remove it. Skipped without Node.js.
        static void AddonHostLiveTests()
        {
            AddonHost h = new AddonHost(AppDomain.CurrentDomain.BaseDirectory);
            if (!h.CanRun) { log.AppendLine("SKIP AddonHost live test (Node.js or bin\\peach.js not found)"); return; }
            string id = null;
            try
            {
                bool up = h.Refresh();
                if (!up) { h.StartHost(); for (int i = 0; i < 40 && !up; i++) { System.Threading.Thread.Sleep(300); up = h.Refresh(); } }
                Check(up && h.Connected, "AddonHost live: finds/starts the host and connects");
                if (!up) return;
                Check(h.Addons.Exists(delegate (AddonInfo x) { return x.Id == "hello-js"; }), "AddonHost live: lists the addons from mods\\ (" + h.Addons.Count + " found)");

                string err;
                id = AddonFiles.Scaffold(h.ModsDir, "ZZ Selftest " + Guid.NewGuid().ToString("N").Substring(0, 6), "wird vom Selbsttest gleich wieder gelöscht", "js", new List<string>(), out err);
                File.WriteAllText(Path.Combine(h.ModsDir, id, "main.js"), "const peach = require('peach'); peach.log('selftest-ok'); setInterval(function () {}, 1000);");
                h.Rescan(); h.Refresh();
                AddonInfo mine = h.Addons.Find(delegate (AddonInfo x) { return x.Id == id; });
                Check(mine != null && mine.Status == "stopped", "AddonHost live: a freshly created addon appears after Rescan (stopped)");

                h.Start(id);
                bool running = false, logged = false;
                for (int i = 0; i < 25 && !(running && logged); i++)
                {
                    System.Threading.Thread.Sleep(300); h.Refresh();
                    mine = h.Addons.Find(delegate (AddonInfo x) { return x.Id == id; });
                    running = mine != null && mine.Status == "running";
                    logged = mine != null && mine.Log.Exists(delegate (AddonLog l) { return l.M == "selftest-ok"; });
                }
                Check(running && logged, "AddonHost live: Start runs the addon and its peach.log line arrives in the log");

                h.Stop(id);
                bool stopped = false;
                for (int i = 0; i < 20 && !stopped; i++)
                {
                    System.Threading.Thread.Sleep(300); h.Refresh();
                    mine = h.Addons.Find(delegate (AddonInfo x) { return x.Id == id; });
                    stopped = mine != null && mine.Status != "running";
                }
                Check(stopped, "AddonHost live: Stop ends the addon");

                System.Threading.Thread.Sleep(600);
                bool removed = AddonFiles.Remove(h.ModsDir, id, out err);
                h.Rescan(); h.Refresh();
                Check(removed && !h.Addons.Exists(delegate (AddonInfo x) { return x.Id == id; }) && !Directory.Exists(Path.Combine(h.ModsDir, id)), "AddonHost live: removing deletes the folder and the addon disappears from the list");
                id = null;
            }
            finally
            {
                if (id != null) { string e; try { AddonFiles.Remove(h.ModsDir, id, out e); h.Rescan(); } catch { } }
                h.Dispose();
            }
        }

        static bool Has(List<Span> spans, string text, string word, Tok kind)
        {
            foreach (Span s in spans) if (s.Kind == kind && text.Substring(s.Start, s.Length) == word) return true;
            return false;
        }

        static bool HasLabel(CompletionResult r, string label) { foreach (Completion c in r.Items) if (c.Label == label) return true; return false; }

        // A small catalog so completion tests do not depend on what is installed on this PC.
        static ApiCatalog FakeCatalog()
        {
            ApiCatalog c = new ApiCatalog();
            c.Fill("js", "{\"fs\":[\"readFileSync:function\",\"writeFileSync:function\",\"constants:object\"],\"path\":[\"join:function\",\"sep:string\"],\"child_process\":[\"spawn:function\"]}");
            c.Fill("py", "{\"os\":[\"getcwd:function\",\"environ:value\"],\"json\":[\"dumps:function\",\"loads:function\"],\"time\":[\"sleep:function\"]}");
            c.Fill("ps1", "[\"Get-Process\",\"Get-ChildItem\",\"Set-Content\"]");
            return c;
        }

        static void CodeEditorTests()
        {
            // ---- colouring ----
            string js = "// hi\nconst a = 'x'; /* c */ let n = 42;\nawait peach.notify(\"t\", `m`);";
            List<Span> s = CodeSyntax.Spans(js, "js");
            Check(Has(s, js, "// hi", Tok.Comment) && Has(s, js, "/* c */", Tok.Comment) && Has(s, js, "'x'", Tok.String) && Has(s, js, "`m`", Tok.String) && Has(s, js, "42", Tok.Number) && Has(s, js, "const", Tok.Keyword) && Has(s, js, "await", Tok.Keyword) && Has(s, js, "peach", Tok.Builtin),
                  "CodeSyntax js: comments, strings, numbers, keywords and the peach object");
            string tricky = "const s = \"// not a comment\"; // real";
            List<Span> t = CodeSyntax.Spans(tricky, "js");
            Check(Has(t, tricky, "\"// not a comment\"", Tok.String) && Has(t, tricky, "// real", Tok.Comment) && !Has(t, tricky, "// not a comment\"; // real", Tok.Comment), "CodeSyntax js: a // inside a string is not a comment");
            string py = "# c\nimport peach\ndef f(x):\n    \"\"\"doc\n    more\"\"\"\n    return 'a' + str(3.5)\n";
            List<Span> p = CodeSyntax.Spans(py, "py");
            Check(Has(p, py, "# c", Tok.Comment) && Has(p, py, "import", Tok.Keyword) && Has(p, py, "def", Tok.Keyword) && Has(p, py, "\"\"\"doc\n    more\"\"\"", Tok.String) && Has(p, py, "3.5", Tok.Number) && Has(p, py, "peach", Tok.Builtin), "CodeSyntax py: comments, keywords, triple-quoted strings, numbers");
            string ps = "<# block #>\n$x = Get-Process | Where-Object { $_.Name -eq 'a' } # tail\nPeach-Notify \"a\" \"b\"";
            List<Span> q = CodeSyntax.Spans(ps, "ps1");
            Check(Has(q, ps, "<# block #>", Tok.Comment) && Has(q, ps, "$x", Tok.Variable) && Has(q, ps, "Get-Process", Tok.Builtin) && Has(q, ps, "Peach-Notify", Tok.Builtin) && Has(q, ps, "# tail", Tok.Comment) && Has(q, ps, "'a'", Tok.String), "CodeSyntax ps1: block comment, variables, cmdlets, Peach-* commands");
            string bat = "@echo off\nrem hallo\n:: auch\nset X=%TEMP%\n";
            List<Span> b = CodeSyntax.Spans(bat, "bat");
            Check(Has(b, bat, "rem hallo", Tok.Comment) && Has(b, bat, ":: auch", Tok.Comment) && Has(b, bat, "%TEMP%", Tok.Variable) && Has(b, bat, "echo", Tok.Keyword), "CodeSyntax bat: rem/:: comments, %VARS%, keywords");
            string json = "{ \"name\": \"x\", \"n\": 3, \"ok\": true }";
            List<Span> j = CodeSyntax.Spans(json, "json");
            Check(Has(j, json, "\"name\"", Tok.Property) && Has(j, json, "\"x\"", Tok.String) && Has(j, json, "3", Tok.Number), "CodeSyntax json: keys vs values");
            Check(CodeSyntax.Spans(new string('x', 400000), "js").Count == 0 && CodeSyntax.LangOf("a.PS1") == "ps1" && CodeSyntax.LangOf("mod.json") == "json" && CodeSyntax.LangOf("x.bin") == "txt", "CodeSyntax: huge files stay plain, language by extension");

            // ---- the Peach API table matches the real SDK files ----
            string sdk = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sdk");
            if (Directory.Exists(sdk))
            {
                string pyText = File.ReadAllText(Path.Combine(sdk, "peach.py"));
                List<string> missingPy = new List<string>();
                foreach (Match m in Regex.Matches(pyText, @"^def ([a-z]\w+)\(", RegexOptions.Multiline))
                {
                    string name = m.Groups[1].Value;
                    if (!ApiCatalog.PeachItems("py").Exists(delegate (ApiItem i) { return i.Name == "peach." + name; })) missingPy.Add(name);
                }
                Check(missingPy.Count == 0, "Peach API table (Python) covers every function of sdk\\peach.py" + (missingPy.Count > 0 ? "  missing: " + string.Join(", ", missingPy.ToArray()) : ""));
                string psText = File.ReadAllText(Path.Combine(sdk, "peach.ps1"));
                List<string> missingPs = new List<string>();
                foreach (Match m in Regex.Matches(psText, @"^function ((?:Peach-|Invoke-Peach)\w*)", RegexOptions.Multiline))
                    if (!ApiCatalog.PeachItems("ps1").Exists(delegate (ApiItem i) { return i.Name == m.Groups[1].Value; })) missingPs.Add(m.Groups[1].Value);
                Check(missingPs.Count == 0, "Peach API table (PowerShell) covers every function of sdk\\peach.ps1" + (missingPs.Count > 0 ? "  missing: " + string.Join(", ", missingPs.ToArray()) : ""));
                string jsText = File.ReadAllText(Path.Combine(sdk, "peach.js"));
                List<string> badJs = new List<string>();
                foreach (ApiItem it in ApiCatalog.PeachItems("js"))
                    foreach (string part in it.Name.Split('.')) if (part != "peach" && !Regex.IsMatch(jsText, @"\b" + part + @"\b")) badJs.Add(it.Name);
                Check(badJs.Count == 0, "Peach API table (JavaScript): every entry exists in sdk\\peach.js" + (badJs.Count > 0 ? "  unknown: " + string.Join(", ", badJs.ToArray()) : ""));
            }

            // ---- suggestions ----
            ApiCatalog cat = FakeCatalog();
            string src = "const peach = require('peach');\nconst fs = require('fs');\npeach.";
            CompletionResult r = Completer.Complete(src, src.Length, "js", cat, false);
            Check(HasLabel(r, "notify") && HasLabel(r, "storage") && HasLabel(r, "hotkey") && r.Start == src.Length, "Completer js: peach. offers the Peach API (notify, storage, hotkey …)");
            string src2 = "const peach = require('peach');\npeach.storage.";
            r = Completer.Complete(src2, src2.Length, "js", cat, false);
            Check(HasLabel(r, "get") && HasLabel(r, "set") && !HasLabel(r, "notify"), "Completer js: peach.storage. offers only get/set");
            string src3 = "const fs = require('fs');\nfs.read";
            r = Completer.Complete(src3, src3.Length, "js", cat, false);
            Check(HasLabel(r, "readFileSync") && !HasLabel(r, "writeFileSync") && r.Start == src3.Length - 4, "Completer js: fs.read -> readFileSync (alias from require, prefix filter)");
            string src4 = "const cp = require('child_process');\ncp.";
            Check(HasLabel(Completer.Complete(src4, src4.Length, "js", cat, false), "spawn"), "Completer js: aliases work under any variable name (cp -> child_process)");
            string src5 = "const x = require('ch";
            r = Completer.Complete(src5, src5.Length, "js", cat, false);
            Check(HasLabel(r, "child_process") && !HasLabel(r, "fs") && r.Start == src5.Length - 2, "Completer js: require('ch -> module names");
            string src6 = "pe";
            r = Completer.Complete(src6, src6.Length, "js", cat, false);
            Check(HasLabel(r, "peach") && HasLabel(Completer.Complete("con", 3, "js", cat, false), "const") && HasLabel(Completer.Complete("con", 3, "js", cat, false), "console"), "Completer js: plain words offer keywords and globals");
            Check(Completer.Complete("c", 1, "js", cat, false).Items.Count == 0 && Completer.Complete("c", 1, "js", cat, true).Items.Count > 0, "Completer: one letter only on Ctrl+Space");
            string pyS = "import peach\nimport os\nos.get";
            r = Completer.Complete(pyS, pyS.Length, "py", cat, false);
            Check(HasLabel(r, "getcwd") && !HasLabel(r, "environ"), "Completer py: os.get -> getcwd");
            string pyP = "import peach\npeach.no";
            Check(HasLabel(Completer.Complete(pyP, pyP.Length, "py", cat, false), "notify"), "Completer py: peach.no -> notify");
            string pyI = "import js";
            r = Completer.Complete(pyI, pyI.Length, "py", cat, false);
            Check(HasLabel(r, "json") && !HasLabel(r, "os"), "Completer py: import js -> json");
            string pyF = "from json import du";
            Check(HasLabel(Completer.Complete(pyF, pyF.Length, "py", cat, false), "dumps"), "Completer py: from json import du -> dumps");
            string pyA = "import json as j\nj.lo";
            Check(HasLabel(Completer.Complete(pyA, pyA.Length, "py", cat, false), "loads"), "Completer py: import json as j -> j.lo -> loads");
            r = Completer.Complete("Peach-N", 7, "ps1", cat, false);
            Check(HasLabel(r, "Peach-Notify") && !HasLabel(r, "Peach-Log"), "Completer ps1: Peach-N -> Peach-Notify");
            r = Completer.Complete("Get-Ch", 6, "ps1", cat, false);
            Check(HasLabel(r, "Get-ChildItem") && !HasLabel(r, "Get-Process"), "Completer ps1: PowerShell commands from the installed PowerShell");
            Check(Completer.Complete("// peach.", 9, "js", cat, false).Items.Count > 0 && Completer.Complete("x", 5, "js", cat, true).Items.Count == 0, "Completer: odd caret positions do not crash");

            // ---- permission check ----
            List<string> need = PermissionLint.Required("peach.notify('a','b'); const t = await peach.clipboard.get(); peach.hotkey('ctrl+alt+k', f); peach.on('focus', g);", "js");
            Check(need.Contains("notify") && need.Contains("clipboard") && need.Contains("hotkeys") && need.Contains("windows") && !need.Contains("shell"), "PermissionLint js: notify, clipboard, hotkeys and the 'focus' event (windows)");
            Check(PermissionLint.Required("// peach.notify('x')\n/* peach.shell.run('x') */\npeach.log('hi')", "js").Count == 0, "PermissionLint: calls in comments do not count");
            Check(PermissionLint.Required("import peach\npeach.shell_run('cmd')\npeach.registry_get('a','b')\npeach.on('clipboard', f)", "py").Count == 3, "PermissionLint py: shell, registry and the 'clipboard' event");
            List<string> psNeed = PermissionLint.Required("Peach-WallpaperSet 'a'\nPeach-Hotkey 'ctrl+alt+h' { }\nPeach-Log 'x'", "ps1");
            Check(psNeed.Contains("wallpaper") && psNeed.Contains("hotkeys") && psNeed.Count == 2, "PermissionLint ps1: wallpaper and hotkeys, Peach-Log needs nothing");
            Check(PermissionLint.Required("curl.exe -X POST \"%PEACH_API%/notify\" -d x", "bat").Contains("notify"), "PermissionLint bat: reads the endpoint from the HTTP call");
            Check(PermissionLint.Missing("peach.notify('x'); peach.shell.open('u')", "js", new List<string> { "notify" }).Count == 1 && PermissionLint.Missing("peach.notify('x')", "js", new List<string> { "notify" }).Count == 0, "PermissionLint.Missing: only what is not declared yet");

            // ---- syntax check with the real interpreters ----
            string tmp = TempDir();
            try
            {
                bool skipped;
                string good = Path.Combine(tmp, "good.js"), bad = Path.Combine(tmp, "bad.js");
                File.WriteAllText(good, "const a = 1;\nconsole.log(a);\n"); File.WriteAllText(bad, "const a = ;\nconsole.log(a\n");
                string r1 = SyntaxCheck.Check("js", good, out skipped);
                if (skipped) log.AppendLine("SKIP SyntaxCheck js (Node.js not found)");
                else
                {
                    Check(r1 == null, "SyntaxCheck js: valid file passes");
                    string r2 = SyntaxCheck.Check("js", bad, out skipped);
                    Check(r2 != null && r2.StartsWith("Zeile 1"), "SyntaxCheck js: broken file reports the line (" + r2 + ")");
                }
                string pgood = Path.Combine(tmp, "good.py"), pbad = Path.Combine(tmp, "bad.py");
                File.WriteAllText(pgood, "x = 1\nprint(x)\n"); File.WriteAllText(pbad, "def f(:\n    pass\n");
                string p1 = SyntaxCheck.Check("py", pgood, out skipped);
                if (skipped) log.AppendLine("SKIP SyntaxCheck py (Python not found)");
                else
                {
                    Check(p1 == null, "SyntaxCheck py: valid file passes");
                    string p2 = SyntaxCheck.Check("py", pbad, out skipped);
                    Check(p2 != null && p2.StartsWith("Zeile 1"), "SyntaxCheck py: broken file reports the line (" + p2 + ")");
                }
                string sgood = Path.Combine(tmp, "good.ps1"), sbad = Path.Combine(tmp, "bad.ps1");
                File.WriteAllText(sgood, "$a = 1\nWrite-Output $a\n"); File.WriteAllText(sbad, "if ($true {\n  1\n");
                string s1 = SyntaxCheck.Check("ps1", sgood, out skipped);
                Check(!skipped && s1 == null, "SyntaxCheck ps1: valid file passes");
                string s2 = SyntaxCheck.Check("ps1", sbad, out skipped);
                Check(s2 != null && s2.StartsWith("Zeile"), "SyntaxCheck ps1: broken file reports the line (" + s2 + ")");
                string jgood = Path.Combine(tmp, "g.json"), jbad = Path.Combine(tmp, "b.json");
                File.WriteAllText(jgood, "{\"a\":1}"); File.WriteAllText(jbad, "{\"a\":");
                Check(SyntaxCheck.Check("json", jgood, out skipped) == null && SyntaxCheck.Check("json", jbad, out skipped) != null, "SyntaxCheck json: valid / broken");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // The editing functions of the code box, driven without a keyboard (the box lives in a window that is never shown).
        static void CodeBoxTests()
        {
            using (Form host = new Form())
            {
                CodeBox cb = new CodeBox();
                cb.Catalog = FakeCatalog();
                cb.Size = new System.Drawing.Size(600, 300);
                host.Controls.Add(cb);
                IntPtr handle = host.Handle;

                cb.Load("const a = 1;\r\nconsole.log(a);", "js");
                Check(cb.Value == "const a = 1;\nconsole.log(a);", "CodeBox: Load keeps the text (line breaks normalised)");
                cb.Highlight();
                Check(cb.Value == "const a = 1;\nconsole.log(a);", "CodeBox: colouring never changes the text");

                // insert with import
                cb.Load("", "js");
                cb.InsertWithImport(@"require\(\s*['""]peach['""]\s*\)", "const peach = require('peach');", "peach.notify()", 1);
                Check(cb.Value == "const peach = require('peach');\npeach.notify()" && cb.Caret == cb.Value.Length - 1, "CodeBox: inserting an API adds the import once and puts the caret inside ()");
                cb.InsertWithImport(@"require\(\s*['""]peach['""]\s*\)", "const peach = require('peach');", "\npeach.log()", 1);
                Check(cb.Value.Split(new string[] { "require('peach')" }, StringSplitOptions.None).Length == 2, "CodeBox: a second insert does not add the import again");

                // undo / redo
                cb.Load("eins", "js");
                cb.Caret = 4; cb.TypeText(" zwei"); cb.Commit();
                cb.TypeText(" drei"); cb.Commit();
                Check(cb.Value == "eins zwei drei", "CodeBox: typing");
                cb.Undo();
                Check(cb.Value == "eins zwei", "CodeBox: undo takes back the last change");
                cb.Undo();
                Check(cb.Value == "eins", "CodeBox: undo again");
                cb.Redo();
                Check(cb.Value == "eins zwei", "CodeBox: redo brings it back");
                cb.Caret = 0; cb.TypeText("neu "); cb.Commit(); cb.Redo();
                Check(cb.Value == "neu eins zwei", "CodeBox: a new edit clears the redo history");

                // indent / outdent
                cb.Load("a\nb\nc", "js");
                cb.Select(0, 3); cb.Indent(false);
                Check(cb.Value == "    a\n    b\nc", "CodeBox: Tab on a selection indents the selected lines");
                cb.Select(0, 11); cb.Indent(true);
                Check(cb.Value == "a\nb\nc", "CodeBox: Shift+Tab takes it back");
                cb.Load("x", "js"); cb.Caret = 1; cb.Indent(false);
                Check(cb.Value == "x    ", "CodeBox: Tab without a selection inserts 4 spaces");

                // auto-indent
                cb.Load("    if (x) {", "js"); cb.Caret = cb.Value.Length; cb.NewLine();
                Check(cb.Value == "    if (x) {\n        ", "CodeBox: Enter after { indents one level deeper");
                cb.Load("def f():", "py"); cb.Caret = cb.Value.Length; cb.NewLine();
                Check(cb.Value == "def f():\n    ", "CodeBox: Enter after : in Python indents");
                cb.Load("  plain", "js"); cb.Caret = cb.Value.Length; cb.NewLine();
                Check(cb.Value == "  plain\n  ", "CodeBox: Enter keeps the current indent");

                // comment toggle per language
                cb.Load("a();\n  b();", "js"); cb.Select(0, cb.Value.Length); cb.ToggleComment();
                Check(cb.Value == "// a();\n  // b();", "CodeBox: Ctrl+/ comments the selected lines (js)");
                cb.Select(0, cb.Value.Length); cb.ToggleComment();
                Check(cb.Value == "a();\n  b();", "CodeBox: Ctrl+/ again removes the comment");
                cb.Load("x = 1", "py"); cb.Select(0, 0); cb.ToggleComment();
                Check(cb.Value == "# x = 1", "CodeBox: comment marker per language (# for Python)");

                // accepting a suggestion replaces the typed prefix
                cb.Load("const peach = require('peach');\npeach.no", "js");
                cb.CaretToEnd(); cb.TryComplete(true);
                Check(cb.PopupVisible, "CodeBox: the suggestion popup opens for peach.no");
                cb.AcceptCompletion();
                Check(cb.Value.EndsWith("\npeach.notify") && !cb.PopupVisible, "CodeBox: accepting 'notify' completes peach.no -> peach.notify");
                cb.Load("const fs = require('fs');\nfs.", "js");
                cb.CaretToEnd(); cb.TryComplete(false);
                Check(cb.PopupVisible, "CodeBox: fs. opens the list of fs functions (alias from require)");
                cb.Load("xyz", "js"); cb.CaretToEnd(); cb.TryComplete(false);
                Check(!cb.PopupVisible, "CodeBox: no suggestions, no popup");
            }
        }

        // The real runtimes: ask node / python / PowerShell what they offer (skipped when a runtime is missing).
        static void ApiCatalogLiveTests()
        {
            ApiCatalog c = new ApiCatalog();
            foreach (string lang in new string[] { "js", "py", "ps1" })
            {
                System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
                c.EnsureLoaded(lang, delegate { done.Set(); });
                done.WaitOne(90000);
            }
            Dictionary<string, string> fs = c.MembersOf("js", "fs");
            if (fs == null) log.AppendLine("SKIP ApiCatalog live js (Node.js not found)");
            else Check(c.ModuleNames("js").Count > 30 && fs.ContainsKey("readFileSync") && fs["readFileSync"] == "function" && c.MembersOf("js", "path") != null && c.MembersOf("js", "child_process") != null,
                       "ApiCatalog live js: all Node built-in modules with their functions (" + c.ModuleNames("js").Count + " modules, fs has " + fs.Count + " members)");
            Dictionary<string, string> js = c.MembersOf("py", "json");
            if (js == null) log.AppendLine("SKIP ApiCatalog live py (Python not found)");
            else Check(c.ModuleNames("py").Count > 80 && js.ContainsKey("dumps") && c.MembersOf("py", "os") != null && c.MembersOf("py", "os").ContainsKey("getcwd") && c.MembersOf("py", "antigravity") == null,
                       "ApiCatalog live py: the Python standard library (" + c.ModuleNames("py").Count + " modules, json has " + js.Count + " members, antigravity left out)");
            Check(c.PsCommands.Count > 100 && c.PsCommands.Contains("Get-Process") && c.PsCommands.Contains("Set-Content"), "ApiCatalog live ps1: PowerShell commands from this PC (" + c.PsCommands.Count + " found)");
            CompletionResult r = Completer.Complete("const fs = require('fs');\nfs.readF", 31, "js", c, false);
            Check(c.MembersOf("js", "fs") == null || HasLabel(r, "readFileSync"), "Completer with the live Node catalog: fs.readF -> readFileSync");
            List<ApiItem> found = c.Search("js", "readFile", 50);
            Check(c.MembersOf("js", "fs") == null || found.Exists(delegate (ApiItem i) { return i.Name == "fs.readFileSync"; }), "ApiCatalog.Search: finds module functions by name");
            Check(c.Search("py", "notify", 50).Exists(delegate (ApiItem i) { return i.Name == "peach.notify"; }) && c.Search("py", "", 10).Count > 0, "ApiCatalog.Search: Peach API is always searchable");
        }

        static void DiscordCoreTests()
        {
            Check(DiscordNav.IsAllowed("https://discord.com/app") && DiscordNav.IsAllowed("https://discord.com/channels/@me") && DiscordNav.IsAllowed("https://cdn.discordapp.com/a.png") && DiscordNav.IsAllowed("https://discord.gg/abc") && DiscordNav.IsAllowed("https://newassets.hcaptcha.com/captcha") && DiscordNav.IsAllowed("about:blank"),
                  "Discord nav: discord.com, discordapp.com, discord.gg, the hCaptcha login and about:blank are allowed");
            Check(!DiscordNav.IsAllowed("https://example.com/") && !DiscordNav.IsAllowed("https://discord.com.evil.com/") && !DiscordNav.IsAllowed("https://evildiscord.com/") && !DiscordNav.IsAllowed("https://discord.comx.org/"), "Discord nav: look-alike hosts (discord.com.evil.com, evildiscord.com) are blocked");
            Check(!DiscordNav.IsAllowed("http://discord.com/app") && !DiscordNav.IsAllowed("javascript:alert(1)") && !DiscordNav.IsAllowed("file:///C:/Windows/win.ini") && !DiscordNav.IsAllowed("") && !DiscordNav.IsAllowed(null) && !DiscordNav.IsAllowed("data:text/html,x"), "Discord nav: only https - http, javascript:, file:, data: and empty are blocked");
            Check(DiscordNav.AllowPermission("Microphone", "https://discord.com/channels/1/2") && DiscordNav.AllowPermission("Camera", "https://discord.com/app") && DiscordNav.AllowPermission("Notifications", "https://discord.com/app") && DiscordNav.AllowPermission("ClipboardRead", "https://discord.com/app"), "Discord permissions: microphone, camera, notifications and paste are granted to discord.com");
            Check(!DiscordNav.AllowPermission("Geolocation", "https://discord.com/app") && !DiscordNav.AllowPermission("Microphone", "https://example.com/") && !DiscordNav.AllowPermission("Microphone", "https://hcaptcha.com/") && !DiscordNav.AllowPermission("Camera", "https://discord.com.evil.com/"), "Discord permissions: location, and anything from other sites (even the captcha), is denied");
            Check(DiscordNav.IsSafeExternal("https://example.com/x") && DiscordNav.IsSafeExternal("http://example.com") && DiscordNav.IsSafeExternal("mailto:a@b.de") && !DiscordNav.IsSafeExternal("file:///C:/x.exe") && !DiscordNav.IsSafeExternal("ms-msdt:/id x") && !DiscordNav.IsSafeExternal("javascript:1") && !DiscordNav.IsSafeExternal("calc.exe") && !DiscordNav.IsSafeExternal(null), "Discord links: only http(s) and mailto may be opened outside; file:, ms-msdt: and the like never");
            Check(PeachLinks.Parse("peach:discord") == "discord" && PeachLinks.Parse("PEACH:Addons/") == "addons" && PeachLinks.Parse("C:\\x\\peach.exe") == null && PeachLinks.Parse(null) == null && PeachLinks.Parse("https://peach:discord") == null, "PeachLinks: peach:discord / peach:addons are recognised, normal paths are not");
            System.Drawing.Bitmap icon = AppIcons.For("peach:discord");
            Check(icon != null && icon.Width == 64 && AppIcons.For("peach:discord") == icon, "AppIcons: peach:discord gets a drawn icon (cached)");
            List<RowItem> rows = AddonRows.Build(new List<BuiltinAddon> { new BuiltinAddon { Name = "Discord", IsOn = delegate { return false; }, CustomizeLabel = "\u00D6ffnen", Customize = delegate { } } }, new List<AddonInfo>(), false, "", false, 1);
            Check(rows.Count == 2 && rows[1].Actions.Count == 1 && rows[1].Actions[0].Label == "\u00D6ffnen" && !rows[1].HasToggle, "Addons card: the Discord card says \u201E\u00D6ffnen\u201C instead of \u201EAnpassen\u201C");
            string tmp = Path.Combine(Path.GetTempPath(), "peach-selftest-dir-" + Guid.NewGuid().ToString("N"));
            try { Config c = Config.Load(Path.Combine(tmp, "config.json")); Check(c.ConfigDir() == tmp, "Config.ConfigDir: the Discord profile lives next to the config file in use"); }
            finally { try { Directory.Delete(tmp, true); } catch { } }
#if WEBVIEW2
            Check(WebView2Setup.Available && File.Exists(WebView2Setup.LoaderPath), "WebView2 libraries and the native loader for this process are in lib\\webview2");
#endif
        }

        static void MinecraftTests()
        {
            // ---- Modrinth answers (sample data) ----
            string searchJson = "{\"hits\":[{\"project_id\":\"AANobbMI\",\"slug\":\"sodium\",\"title\":\"Sodium\",\"description\":\"Fast renderer\",\"author\":\"jellysquid3\",\"downloads\":61000000},{\"project_id\":\"x\",\"title\":null}],\"total_hits\":2}";
            List<ModHit> hits = Modrinth.ParseSearch(searchJson);
            Check(hits.Count == 1 && hits[0].Id == "AANobbMI" && hits[0].Title == "Sodium" && hits[0].Downloads == 61000000 && Modrinth.ParseSearch("kaputt").Count == 0, "Minecraft: Modrinth search results are read (broken entries skipped)");
            string verJson = "[{\"id\":\"v3\",\"version_number\":\"0.7.0-beta\",\"version_type\":\"beta\",\"files\":[{\"url\":\"https://cdn.modrinth.com/data/x/versions/v3/beta.jar\",\"filename\":\"beta.jar\",\"primary\":true,\"hashes\":{\"sha512\":\"aa\"}}],\"dependencies\":[]}," +
                             "{\"id\":\"v2\",\"version_number\":\"0.6.0\",\"version_type\":\"release\",\"files\":[{\"url\":\"https://cdn.modrinth.com/data/x/versions/v2/extra.jar\",\"filename\":\"extra.jar\",\"primary\":false,\"hashes\":{\"sha512\":\"bb\"}},{\"url\":\"https://cdn.modrinth.com/data/x/versions/v2/mod-0.6.0.jar\",\"filename\":\"mod-0.6.0.jar\",\"primary\":true,\"hashes\":{\"sha512\":\"cc\"}}],\"dependencies\":[{\"project_id\":\"P1\",\"dependency_type\":\"required\"},{\"project_id\":\"P2\",\"dependency_type\":\"optional\"}]}]";
            ModFile best = Modrinth.ParseBestVersion(verJson, "X");
            Check(best != null && best.FileName == "mod-0.6.0.jar" && best.Sha512 == "cc" && best.RequiredProjects.Count == 1 && best.RequiredProjects[0] == "P1", "Minecraft: the newest release and its primary file are chosen, only required dependencies are followed");
            Check(Modrinth.ParseBestVersion("[]", "X") == null && Modrinth.ParseBestVersion("kaputt", "X") == null && Modrinth.ParseBestVersion(verJson.Replace("\"release\"", "\"beta\""), "X").FileName == "beta.jar", "Minecraft: no version = null, only betas = the first beta");
            Check(Modrinth.SearchUrl("sodium", "1.21.1", "fabric", 20).Contains("versions%3A1.21.1") && Modrinth.SearchUrl("a b&c", "1.21.1", "fabric", 5).Contains("a%20b%26c") && Modrinth.VersionsUrl("AANobbMI", "1.21.1", "fabric").Contains("game_versions="), "Minecraft: search and version URLs are encoded");

            // ---- safety of downloads ----
            Check(Modrinth.SafeFileName("sodium-fabric-0.6.0+mc1.21.1.jar") != null && Modrinth.SafeFileName("..\\evil.jar") == null && Modrinth.SafeFileName("a/b.jar") == null && Modrinth.SafeFileName("x.exe") == null && Modrinth.SafeFileName("a;b.jar") == null && Modrinth.SafeFileName(null) == null && Modrinth.SafeFileName("..jar.jar") == null, "Minecraft: only plain *.jar names are accepted");
            Check(Modrinth.IsSafeSource("https://cdn.modrinth.com/data/a/b.jar") && !Modrinth.IsSafeSource("http://cdn.modrinth.com/a.jar") && !Modrinth.IsSafeSource("https://evil.example/cdn.modrinth.com/a.jar") && !Modrinth.IsSafeSource("https://cdn.modrinth.com.evil.com/a.jar") && !Modrinth.IsSafeSource("file:///c:/x.jar"), "Minecraft: downloads only come from Modrinth's own CDN over https");
            Check(Modrinth.HashMatches(Encoding.ASCII.GetBytes("abc"), "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f") && !Modrinth.HashMatches(Encoding.ASCII.GetBytes("abd"), "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f") && !Modrinth.HashMatches(new byte[1], null), "Minecraft: a downloaded file must match its SHA-512 checksum");

            // ---- ids, versions ----
            List<McProfile> ex = new List<McProfile> { new McProfile { Id = "survival" } };
            Check(Minecraft.MakeId("Survival", ex) == "survival-2" && Minecraft.MakeId("Mein Profil!", ex) == "mein-profil" && Minecraft.MakeId("???", ex) == "profil" && Minecraft.ValidVersion("1.21.1") && Minecraft.ValidVersion("26.3") && !Minecraft.ValidVersion("1.21.1; calc") && !Minecraft.ValidVersion("..\\x") && !Minecraft.ValidVersion(null), "Minecraft: profile ids are safe folder names, versions are validated");

            // ---- mods folder ----
            string tmp = Path.Combine(Path.GetTempPath(), "peach-selftest-mc-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                File.WriteAllText(Path.Combine(tmp, "a.jar"), "x"); File.WriteAllText(Path.Combine(tmp, "b.jar.disabled"), "x"); File.WriteAllText(Path.Combine(tmp, "readme.txt"), "x");
                List<ModEntry> l = ModFolder.List(tmp);
                Check(l.Count == 2 && l[0].FileName == "a.jar" && l[0].Enabled && l[1].FileName == "b.jar" && !l[1].Enabled, "Minecraft: the mods folder lists enabled and disabled mods (other files ignored)");
                Check(ModFolder.SetEnabled(tmp, "a.jar", false) && File.Exists(Path.Combine(tmp, "a.jar.disabled")) && !File.Exists(Path.Combine(tmp, "a.jar")) && ModFolder.SetEnabled(tmp, "a.jar", true) && File.Exists(Path.Combine(tmp, "a.jar")), "Minecraft: a mod is switched off by renaming and on again");
                Check(!ModFolder.SetEnabled(tmp, "..\\a.jar", false) && !ModFolder.Delete(tmp, "..\\..\\x.jar") && ModFolder.Delete(tmp, "b.jar") && !File.Exists(Path.Combine(tmp, "b.jar.disabled")), "Minecraft: paths are refused, delete removes the mod");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }

            // ---- own launcher: rules, libraries, arguments ----
            Check(Minecraft.MavenPath("net.fabricmc:fabric-loader:0.16.9") == "net/fabricmc/fabric-loader/0.16.9/fabric-loader-0.16.9.jar" && Minecraft.MavenPath("a:b:1:natives") == "a/b/1/b-1-natives.jar" && Minecraft.MavenPath("../x:y:1") == null && Minecraft.MavenPath("x") == null, "Minecraft launcher: maven names become library paths (and paths with .. are refused)");
            Check(Minecraft.IsAllowedHost("https://libraries.minecraft.net/a.jar") && Minecraft.IsAllowedHost("https://maven.fabricmc.net/a.jar") && !Minecraft.IsAllowedHost("http://libraries.minecraft.net/a.jar") && !Minecraft.IsAllowedHost("https://evil.example/libraries.minecraft.net/a.jar") && !Minecraft.IsAllowedHost("https://libraries.minecraft.net.evil.com/a.jar"), "Minecraft launcher: game files only come from Mojang's and Fabric's servers over https");
            System.Web.Script.Serialization.JavaScriptSerializer jj = Web.Json();
            object rWin = jj.DeserializeObject("[{\"action\":\"allow\"},{\"action\":\"disallow\",\"os\":{\"name\":\"osx\"}}]");
            object rOsx = jj.DeserializeObject("[{\"action\":\"allow\",\"os\":{\"name\":\"osx\"}}]");
            object rArch = jj.DeserializeObject("[{\"action\":\"allow\",\"os\":{\"name\":\"windows\",\"arch\":\"x86\"}}]");
            object rDemo = jj.DeserializeObject("[{\"action\":\"allow\",\"features\":{\"is_demo_user\":true}}]");
            Check(Minecraft.RulesAllow(rWin, null) && !Minecraft.RulesAllow(rOsx, null) && !Minecraft.RulesAllow(rArch, null) && !Minecraft.RulesAllow(rDemo, null) && Minecraft.RulesAllow(rDemo, new List<string> { "is_demo_user" }) && Minecraft.RulesAllow(null, null), "Minecraft launcher: rules for Windows, other systems, 32-bit/ARM and features (demo)");
            string libJson = "{\"libraries\":[" +
                "{\"name\":\"org.lwjgl:lwjgl:3.3.3\",\"downloads\":{\"artifact\":{\"path\":\"org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar\",\"url\":\"https://libraries.minecraft.net/a.jar\",\"sha1\":\"aa\",\"size\":5}}}," +
                "{\"name\":\"org.lwjgl:lwjgl:3.3.3:natives-windows\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"windows\"}}],\"downloads\":{\"artifact\":{\"path\":\"org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar\",\"url\":\"https://libraries.minecraft.net/b.jar\",\"sha1\":\"bb\",\"size\":6}}}," +
                "{\"name\":\"org.lwjgl:lwjgl:3.3.3:natives-macos\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"osx\"}}],\"downloads\":{\"artifact\":{\"path\":\"mac.jar\",\"url\":\"https://libraries.minecraft.net/c.jar\",\"sha1\":\"cc\",\"size\":7}}}," +
                "{\"name\":\"org.lwjgl:lwjgl:3.3.3:natives-windows-arm64\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"windows\",\"arch\":\"arm64\"}}],\"downloads\":{\"artifact\":{\"path\":\"arm.jar\",\"url\":\"https://libraries.minecraft.net/d.jar\",\"sha1\":\"dd\",\"size\":8}}}," +
                "{\"name\":\"net.fabricmc:fabric-loader:0.16.9\",\"url\":\"https://maven.fabricmc.net/\",\"sha1\":\"ee\",\"size\":9}," +
                "{\"name\":\"net.fabricmc:fabric-loader:0.1.0\",\"url\":\"https://maven.fabricmc.net/\"}," +
                "{\"name\":\"old:native:1\",\"natives\":{\"windows\":\"natives-windows\"},\"extract\":{\"exclude\":[\"META-INF/\"]},\"downloads\":{\"artifact\":{\"path\":\"old/native/1/native-1.jar\",\"url\":\"https://libraries.minecraft.net/e.jar\",\"sha1\":\"ff\",\"size\":1},\"classifiers\":{\"natives-windows\":{\"path\":\"old/native/1/native-1-natives-windows.jar\",\"url\":\"https://libraries.minecraft.net/f.jar\",\"sha1\":\"gg\",\"size\":2}}}}]}";
            List<McLib> libs = Minecraft.ParseLibraries(jj.Deserialize<Dictionary<string, object>>(libJson));
            McLib nat = libs.Find(delegate (McLib l) { return l.IsNative; });
            Check(libs.Count == 5 && libs.TrueForAll(delegate (McLib l) { return !l.Path.Contains("mac.jar") && l.Path != "arm.jar"; }) && libs.Exists(delegate (McLib l) { return l.Path == "net/fabricmc/fabric-loader/0.16.9/fabric-loader-0.16.9.jar" && l.Sha1 == "ee" && l.Url == "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.16.9/fabric-loader-0.16.9.jar"; }) && !libs.Exists(delegate (McLib l) { return l.Path.Contains("0.1.0"); }) && nat != null && nat.Path.EndsWith("natives-windows.jar") && nat.Excludes.Contains("META-INF/"),
                  "Minecraft launcher: libraries for Windows x64 only, Fabric's maven libraries, duplicates dropped, old-style natives with excludes");
            Dictionary<string, object> mp = jj.Deserialize<Dictionary<string, object>>("{\"mainClass\":\"P\",\"libraries\":[{\"name\":\"a:a:1\"}],\"arguments\":{\"game\":[\"--a\"],\"jvm\":[\"-Dp\"]},\"id\":\"1.21.1\"}");
            Dictionary<string, object> mc2 = jj.Deserialize<Dictionary<string, object>>("{\"id\":\"fab\",\"inheritsFrom\":\"1.21.1\",\"mainClass\":\"C\",\"libraries\":[{\"name\":\"b:b:1\"}],\"arguments\":{\"game\":[\"--b\"],\"jvm\":[\"-Dc\"]}}");
            Dictionary<string, object> mm = Minecraft.Merge(mp, mc2);
            object[] mlibs = Modrinth.ToArray(mm["libraries"]);
            object[] mgame = Modrinth.ToArray(((Dictionary<string, object>)mm["arguments"])["game"]), mjvm = Modrinth.ToArray(((Dictionary<string, object>)mm["arguments"])["jvm"]);
            Check(mm["mainClass"].ToString() == "C" && mm["id"].ToString() == "fab" && mlibs.Length == 2 && ((Dictionary<string, object>)mlibs[0])["name"].ToString() == "b:b:1" && mgame.Length == 2 && mgame[0].ToString() == "--a" && mgame[1].ToString() == "--b" && mjvm.Length == 2, "Minecraft launcher: a Fabric profile is merged onto its Minecraft version (main class, libraries, arguments)");

            McPlan plan0 = new McPlan { VersionId = "1.21.1", MainClass = "net.minecraft.client.main.Main", AssetsRoot = "C:\\a b\\assets", AssetIndexId = "17", NativesDir = "C:\\n x", ClassPath = new List<string> { "C:\\l\\a.jar", "C:\\l\\b.jar" } };
            plan0.Merged = jj.Deserialize<Dictionary<string, object>>("{\"type\":\"release\",\"arguments\":{\"jvm\":[\"-Djava.library.path=${natives_directory}\",\"-cp\",\"${classpath}\",{\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"osx\"}}],\"value\":\"-XstartOnFirstThread\"}],\"game\":[\"--username\",\"${auth_player_name}\",\"--version\",\"${version_name}\",\"--accessToken\",\"${auth_access_token}\",{\"rules\":[{\"action\":\"allow\",\"features\":{\"is_demo_user\":true}}],\"value\":\"--demo\"}]}}");
            List<string> ar2 = Minecraft.BuildArguments(plan0, new McContext { PlayerName = "Anton", GameDir = "C:\\g", RamMb = 2048, AccessToken = "TOK" });
            int mainAt = ar2.IndexOf("net.minecraft.client.main.Main");
            Check(ar2.Contains("-Xmx2048M") && ar2.Contains("-Djava.library.path=C:\\n x") && ar2.IndexOf("-cp") >= 0 && ar2[ar2.IndexOf("-cp") + 1] == "C:\\l\\a.jar;C:\\l\\b.jar" && !ar2.Contains("-XstartOnFirstThread") && !ar2.Contains("--demo") && mainAt > ar2.IndexOf("-cp") && ar2[mainAt + 1] == "--username" && ar2[mainAt + 2] == "Anton" && ar2.Contains("TOK"), "Minecraft launcher: JVM and game arguments are filled in (other systems' arguments skipped)");
            Check(Minecraft.BuildArguments(plan0, new McContext { PlayerName = "Anton", GameDir = "C:\\g", Demo = true }).Contains("--demo"), "Minecraft launcher: demo mode adds --demo");
            McPlan plan1 = new McPlan { VersionId = "1.8.9", MainClass = "net.minecraft.client.main.Main", AssetsRoot = "x", AssetIndexId = "1.8", NativesDir = "n", ClassPath = new List<string> { "a.jar" }, Merged = jj.Deserialize<Dictionary<string, object>>("{\"minecraftArguments\":\"--username ${auth_player_name} --gameDir ${game_directory}\"}") };
            List<string> ar3 = Minecraft.BuildArguments(plan1, new McContext { PlayerName = "Anton", GameDir = "C:\\g" });
            Check(ar3.Contains("-cp") && ar3.Contains("-Djava.library.path=n") && ar3[ar3.IndexOf("--username") + 1] == "Anton" && ar3[ar3.IndexOf("--gameDir") + 1] == "C:\\g", "Minecraft launcher: old versions (minecraftArguments) work too");
            Check(Minecraft.Quote("plain") == "plain" && Minecraft.Quote("a b") == "\"a b\"" && Minecraft.Quote("C:\\x y\\") == "\"C:\\x y\\\\\"" && Minecraft.Quote("a\"b") == "\"a\\\"b\"" && Minecraft.Quote("") == "\"\"", "Minecraft launcher: arguments with spaces and quotes are quoted correctly");
            Check(Minecraft.JavaMajorFromVersion("21.0.9") == 21 && Minecraft.JavaMajorFromVersion("1.8.0_392") == 8 && Minecraft.JavaMajorFromVersion("17.0.2") == 17 && Minecraft.JavaMajorFromVersion("openjdk version \"25.0.2\" 2026-01-20") == 25 && Minecraft.JavaMajorFromVersion("kaputt") == 0, "Minecraft launcher: Java versions are read (1.8 = Java 8)");
            string mu, ms;
            Check(Minecraft.FindInManifest("{\"versions\":[{\"id\":\"1.21.1\",\"url\":\"https://piston-meta.mojang.com/x.json\",\"sha1\":\"ab\"}]}", "1.21.1", out mu, out ms) && mu.EndsWith("x.json") && ms == "ab" && !Minecraft.FindInManifest("{\"versions\":[]}", "1.21.1", out mu, out ms) && !Minecraft.FindInManifest("kaputt", "1", out mu, out ms), "Minecraft launcher: a version is found in Mojang's manifest");
            Check(Minecraft.ParseFabricLoader("[{\"loader\":{\"version\":\"0.17.0\",\"stable\":false}},{\"loader\":{\"version\":\"0.16.9\",\"stable\":true}}]") == "0.16.9" && Minecraft.ParseFabricLoader("[{\"loader\":{\"version\":\"0.17.0\",\"stable\":false}}]") == "0.17.0" && Minecraft.ParseFabricLoader("[]") == null, "Minecraft: the newest stable Fabric loader is chosen");

            // ---- Microsoft login: answers (sample data) and token storage ----
            DeviceCode dcx = McAuth.ParseDeviceCode("{\"device_code\":\"DC\",\"user_code\":\"ABCD1234\",\"verification_uri\":\"https://www.microsoft.com/link\",\"expires_in\":900,\"interval\":5}");
            Check(dcx != null && dcx.UserCode == "ABCD1234" && dcx.Interval == 5 && McAuth.ParseDeviceCode("{\"verification_uri\":\"http://evil\"}") == null && McAuth.ParseDeviceCode("{\"device_code\":\"a\",\"user_code\":\"b\",\"verification_uri\":\"http://evil.example\"}").VerificationUri.StartsWith("https://www.microsoft.com"), "Minecraft login: the device code answer is read, only https addresses are opened");
            bool declined = false, expired = false;
            try { McAuth.ParseToken("{\"error\":\"authorization_declined\"}"); } catch (InvalidOperationException) { declined = true; }
            try { McAuth.ParseToken("{\"error\":\"expired_token\"}"); } catch (InvalidOperationException) { expired = true; }
            MsTokens tk = McAuth.ParseToken("{\"access_token\":\"AT\",\"refresh_token\":\"RT\"}");
            Check(McAuth.ParseToken("{\"error\":\"authorization_pending\"}") == null && McAuth.ParseToken("{\"error\":\"slow_down\"}") == null && tk != null && tk.Access == "AT" && tk.Refresh == "RT" && declined && expired, "Minecraft login: waiting / success / declined / expired are told apart");
            string xt, xu;
            Check(McAuth.ParseXbox("{\"Token\":\"T\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"U\"}]}}", out xt, out xu) && xt == "T" && xu == "U" && !McAuth.ParseXbox("{\"Token\":\"T\"}", out xt, out xu) && McAuth.XstsError("{\"XErr\":2148916233}").Contains("Xbox-Profil") && McAuth.XstsError("{\"XErr\":2148916238}").Contains("Kind") && McAuth.XstsError("kaputt").Length > 0, "Minecraft login: Xbox answers and the common error messages");
            McSession ps = McAuth.ParseProfile("{\"id\":\"069A79F444E94726A5BEFCA90E38AAF5\",\"name\":\"Notch\"}");
            Check(ps != null && ps.Name == "Notch" && ps.Uuid == "069a79f4-44e9-4726-a5be-fca90e38aaf5" && McAuth.ParseProfile("{\"id\":\"x\",\"name\":\"a b\"}") == null && McAuth.ParseProfile("kaputt") == null, "Minecraft login: profile name and UUID are read and checked");
            string prot = McAuth.Protect("geheim-123");
            Check(prot.Length > 20 && !prot.Contains("geheim") && McAuth.Unprotect(prot) == "geheim-123" && McAuth.Unprotect("kein-base64!") == null && McAuth.Unprotect("") == null && McAuth.ValidClientId("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0") && !McAuth.ValidClientId("abc") && !McAuth.ValidClientId(null), "Minecraft login: the refresh token is stored encrypted (Windows DPAPI), client IDs are validated");
            List<string> rel = Minecraft.ParseReleases("{\"versions\":[{\"id\":\"26.4-snapshot-1\",\"type\":\"snapshot\"},{\"id\":\"1.21.1\",\"type\":\"release\"},{\"id\":\"1.21\",\"type\":\"release\"}]}", 10);
            Check(rel.Count == 2 && rel[0] == "1.21.1", "Minecraft: the version list only has releases");

            // ---- widgets ----
            string ar, so;
            Check(SpotifyInfo.ParseTitle("Daft Punk - Get Lucky", out ar, out so) && ar == "Daft Punk" && so == "Get Lucky" && SpotifyInfo.ParseTitle("AC/DC - Back In Black - Remastered", out ar, out so) && ar == "AC/DC" && so == "Back In Black - Remastered"
                  && !SpotifyInfo.ParseTitle("Spotify Premium", out ar, out so) && !SpotifyInfo.ParseTitle("Spotify", out ar, out so) && !SpotifyInfo.ParseTitle("Advertisement", out ar, out so) && !SpotifyInfo.ParseTitle("", out ar, out so) && !SpotifyInfo.ParseTitle(null, out ar, out so), "Minecraft widgets: Spotify's window title is read as artist and song");
            SystemStats st = new SystemStats { Cpu = 42.4, Gpu = -1, RamPct = 60, RamUsedMb = 8192, RamTotalMb = 16384 };
            List<WidgetLine> wl = OverlayModel.Build(new List<string> { "spotify", "cpu", "gpu", "ram", "clock", "session", "unbekannt" }, st, new DateTime(2026, 1, 1, 9, 5, 0), "Daft Punk - Get Lucky", TimeSpan.FromSeconds(3725));
            Check(wl.Count == 6 && wl[0].Text.Contains("Get Lucky") && wl[1].Text == "42 %" && wl[1].Value > 42 && wl[2].Text == "–" && wl[3].Text == (8.0).ToString("0.0") + " / " + (16.0).ToString("0.0") + " GB" && wl[4].Text == "09:05" && wl[5].Text == "1:02:05", "Minecraft widgets: the overlay lines (unknown widgets ignored, GPU n/a shown as a dash)");
            Check(OverlayModel.Build(new List<string> { "spotify" }, st, DateTime.Now, null, null)[0].Text == "Spotify läuft nicht" && OverlayModel.Build(new List<string> { "spotify" }, st, DateTime.Now, "Spotify Free", null)[0].Text == "Pausiert" && OverlayModel.Build(new List<string> { "session" }, st, DateTime.Now, null, null)[0].Text == "–" && OverlayModel.Duration(TimeSpan.FromSeconds(65)) == "1:05", "Minecraft widgets: not running / paused / no game are shown honestly");

            // ---- real network: Modrinth download + Fabric profile (skipped without internet) ----
            string net = Path.Combine(Path.GetTempPath(), "peach-selftest-mcnet-" + Guid.NewGuid().ToString("N"));
            try
            {
                List<ModHit> live = null;
                try { live = Modrinth.ParseSearch(Web.Get(Modrinth.SearchUrl("sodium", "1.21.1", "fabric", 5), 15000)); } catch { }
                if (live == null) { log.AppendLine("SKIP Minecraft live tests (no internet)"); return; }
                Check(live.Count > 0 && live.Exists(delegate (ModHit h) { return h.Title == "Sodium"; }), "Minecraft live: Modrinth finds Sodium for Fabric 1.21.1");
                string modsDir = Path.Combine(net, "mods");
                List<string> done = Modrinth.Install("AANobbMI", "1.21.1", "fabric", modsDir, null);
                Check(done.Count >= 1 && done.Exists(delegate (string n) { return n.ToLowerInvariant().Contains("sodium"); }) && File.Exists(Path.Combine(modsDir, done[0])) && new FileInfo(Path.Combine(modsDir, done[0])).Length > 100000, "Minecraft live: a real mod is downloaded, verified and saved (" + string.Join(",", done.ToArray()) + ")");
                Check(Modrinth.Install("AANobbMI", "1.21.1", "fabric", modsDir, null).Count == 0, "Minecraft live: installing the same mod again changes nothing");

                // own launcher against the real servers (files are cached between test runs)
                string cache = Path.Combine(Path.GetTempPath(), "peach-selftest-mc-cache"); Directory.CreateDirectory(cache);
                Config c = Config.Load(Path.Combine(cache, "config.json"));
                McProfile vp = new McProfile { Id = "van", Name = "Van", McVersion = "1.21.1", Loader = "vanilla" };
                Check(Minecraft.Install(c, vp) == "1.21.1" && Directory.Exists(Minecraft.ModsDir(c, vp)), "Minecraft launcher live: a vanilla profile fetches its version file from Mojang");
                McPlan vplan = Minecraft.Prepare(c, vp, false, null);
                bool allThere = vplan.ClassPath.Count > 20;
                foreach (string cp in vplan.ClassPath) if (!File.Exists(cp)) allThere = false;
                Check(allThere && vplan.MainClass == "net.minecraft.client.main.Main" && vplan.JavaMajor == 21 && new FileInfo(vplan.ClientJar).Length > 10000000 && File.Exists(Path.Combine(vplan.AssetsRoot, "indexes", vplan.AssetIndexId + ".json")), "Minecraft launcher live: client, libraries and asset index are downloaded and verified (" + vplan.ClassPath.Count + " files on the class path, Java " + vplan.JavaMajor + ")");
                List<string> va = Minecraft.BuildArguments(vplan, new McContext { PlayerName = "PeachDemo", UserType = "legacy", GameDir = Path.Combine(cache, "game"), Demo = true });
                Check(va.Contains("--demo") && va.Contains("--username") && va.Contains("net.minecraft.client.main.Main") && va.Exists(delegate (string a) { return a.StartsWith("-Djava.library.path="); }) && !va.Exists(delegate (string a) { return a.Contains("${"); }), "Minecraft launcher live: the real argument list is complete (no unfilled ${...})");
                McProfile fp = new McProfile { Id = "fab", Name = "Fab", McVersion = "1.21.1", Loader = "fabric" };
                string fid = Minecraft.Install(c, fp);
                McPlan fplan = Minecraft.Prepare(c, fp, false, null);
                Check(fid.StartsWith("fabric-loader-") && fid.EndsWith("-1.21.1") && fplan.MainClass.Contains("fabricmc") && fplan.ClientJarId == "1.21.1" && fplan.ClassPath.Exists(delegate (string x) { return x.Contains("fabric-loader"); }) && fplan.ClassPath.TrueForAll(delegate (string x) { return File.Exists(x); }), "Minecraft launcher live: Fabric 1.21.1 is merged onto vanilla (" + fid + ")");
                string jproblem; string java = Minecraft.FindJava(21, out jproblem);
                if (java == null) log.AppendLine("SKIP Minecraft launcher live: no Java 21 installed (" + jproblem + ")");
                else Check(File.Exists(java), "Minecraft launcher live: Java 21 found (" + java + ")");
            }
            finally { try { Directory.Delete(net, true); } catch { } }
        }

        // Manual check (--mc-launch-test <file>): really starts Minecraft in demo mode for a few seconds, then closes it.
        public static int McLaunchTest(string outFile)
        {
            StringBuilder o = new StringBuilder();
            int code = 1;
            try
            {
                string cache = Path.Combine(Path.GetTempPath(), "peach-selftest-mc-cache"); Directory.CreateDirectory(cache);
                Config c = Config.Load(Path.Combine(cache, "config.json"));
                McProfile p = new McProfile { Id = "launchtest", Name = "Launchtest", McVersion = "1.21.1", Loader = "fabric" };
                p.VersionId = Minecraft.Install(c, p);
                McPlan plan = Minecraft.Prepare(c, p, true, delegate (string s) { o.AppendLine("  " + s); });
                string problem; string java = Minecraft.FindJava(plan.JavaMajor, out problem);
                if (java == null) throw new InvalidOperationException(problem);
                o.AppendLine("Java: " + java);
                McContext ctx = new McContext { PlayerName = "PeachDemo", UserType = "legacy", GameDir = Minecraft.GameDir(c, p), Demo = true, RamMb = 2048 };
                Process pr = Minecraft.Launch(c, p, plan, ctx, java);
                DateTime end = DateTime.Now.AddSeconds(60); string title = "";
                while (DateTime.Now < end && !pr.HasExited)
                {
                    System.Threading.Thread.Sleep(1000);
                    try { pr.Refresh(); title = pr.MainWindowTitle; } catch { }
                    if (title != null && title.StartsWith("Minecraft")) break;
                }
                o.AppendLine("Fenstertitel: " + title + (pr.HasExited ? "  (Prozess beendet, Code " + pr.ExitCode + ")" : "  (läuft)"));
                bool up = !pr.HasExited && title != null && title.StartsWith("Minecraft");
                System.Threading.Thread.Sleep(4000);
                if (!pr.HasExited) { pr.Kill(); pr.WaitForExit(5000); }
                System.Threading.Thread.Sleep(500);
                string log = Path.Combine(ctx.GameDir, "logs", "peach-launch.log");
                if (File.Exists(log)) { string[] lines = File.ReadAllLines(log); o.AppendLine("Log (" + lines.Length + " Zeilen), Anfang/Ende:"); for (int i = 0; i < Math.Min(8, lines.Length); i++) o.AppendLine("  " + lines[i]); o.AppendLine("  ..."); for (int i = Math.Max(8, lines.Length - 8); i < lines.Length; i++) o.AppendLine("  " + lines[i]); }
                o.AppendLine(up ? "ERGEBNIS: Minecraft ist gestartet (Fenster da)." : "ERGEBNIS: Fenster nicht gesehen.");
                code = up ? 0 : 2;
            }
            catch (Exception ex) { o.AppendLine("FEHLER: " + ex.Message); }
            File.WriteAllText(outFile, o.ToString());
            return code;
        }

        static void GameBoostTests()
        {
            // ---- names and protection ----
            Check(BoostLogic.NormalizeExe("Game.EXE") == "game.exe" && BoostLogic.NormalizeExe("C:\\Spiele\\Mein Spiel.exe") == "mein spiel.exe" && BoostLogic.NormalizeExe(" \"cs2\" ") == "cs2.exe" && BoostLogic.NormalizeExe("") == null && BoostLogic.NormalizeExe("a;b&c.exe") == null && BoostLogic.NormalizeExe(null) == null && BoostLogic.NormalizeExe(".exe") == null, "Game boost: program names are cleaned up, odd names are refused");
            Check(BoostLogic.IsProtected("explorer.exe") && BoostLogic.IsProtected("SVCHOST") && BoostLogic.IsProtected("peach.exe") && BoostLogic.IsProtected("MsMpEng.exe") && BoostLogic.IsProtected("nonsense;;") && !BoostLogic.IsProtected("chrome.exe") && !BoostLogic.IsProtected("eldenring.exe"), "Game boost: Windows, security software and Peach are protected");
            List<string> gl = new List<string> { "eldenring.exe", "CS2" };
            Check(BoostLogic.Matches("EldenRing", gl) && BoostLogic.Matches("cs2.exe", gl) && !BoostLogic.Matches("notepad", gl) && !BoostLogic.Matches(null, gl), "Game boost: a running program is matched against the list");

            // ---- power plan parsing (English + German) ----
            string act = "Power Scheme GUID: 381B4222-F694-41F0-9685-FF5BB260DF2E  (Balanced)";
            string actDe = "GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert)";
            Check(BoostLogic.ParseActiveScheme(act) == "381b4222-f694-41f0-9685-ff5bb260df2e" && BoostLogic.ParseActiveScheme(actDe) == "381b4222-f694-41f0-9685-ff5bb260df2e" && BoostLogic.ParseActiveScheme("kaputt") == null, "Game boost: the active power plan is read in any language");
            string listEn = "Existing Power Schemes (* Active)\n-----\nPower Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\nPower Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\nPower Scheme GUID: e9a42b02-d5df-448d-aa00-03f14749eb61  (Ultimate Performance)\n";
            string listDe = "Vorhandene Energieschemas\nGUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert) *\nGUID des Energieschemas: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Höchstleistung)\n";
            Check(BoostLogic.PickScheme(listEn) == "e9a42b02-d5df-448d-aa00-03f14749eb61" && BoostLogic.PickScheme(listDe) == "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" && BoostLogic.PickScheme("nichts") == null, "Game boost: picks Ultimate / High performance (English and German names)");

            // ---- the booster with a fake powercfg ----
            string tmp = Path.Combine(Path.GetTempPath(), "peach-selftest-gb-" + Guid.NewGuid().ToString("N"));
            try
            {
                Config c = Config.Load(Path.Combine(tmp, "config.json"));
                string current = "381b4222-f694-41f0-9685-ff5bb260df2e";
                List<string> cmds = new List<string>();
                Func<string, string> fake = delegate (string args)
                {
                    cmds.Add(args);
                    if (args == "/getactivescheme") return "Power Scheme GUID: " + current + "  (x)";
                    if (args == "/list") return listEn;
                    if (args.StartsWith("/setactive ")) current = args.Substring(11);
                    return "";
                };
                List<string> said = new List<string>();
                using (GameBooster b = new GameBooster(c))
                {
                    b.RunPowerCfg = fake;
                    b.Notify += delegate (string t, string m) { said.Add(t); };
                    Process child = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                    try
                    {
                        c.GameBoost.CloseApps.Add("explorer.exe"); c.GameBoost.CloseApps.Add("gibt-es-nicht-xyz.exe"); c.GameBoost.CloseApps.Add("bad;name");
                        Check(b.Boost(child, false) && b.Active && b.Game == "ping.exe", "Game boost: boost starts for the game");
                        Check(current == "e9a42b02-d5df-448d-aa00-03f14749eb61" && c.GameBoost.RestoreScheme == "381b4222-f694-41f0-9685-ff5bb260df2e", "Game boost: power plan switched to Ultimate, the old one is remembered in the config");
                        child.Refresh();
                        Check(child.PriorityClass == ProcessPriorityClass.High, "Game boost: the game runs with high priority");
                        Check(b.Closed == 0 && Process.GetProcessesByName("explorer").Length > 0, "Game boost: protected and unknown programs are left alone (the desktop is still there)");
                        Check(!b.Boost(child, false), "Game boost: starting twice does nothing");
                        b.Restore();
                        child.Refresh();
                        Check(!b.Active && current == "381b4222-f694-41f0-9685-ff5bb260df2e" && c.GameBoost.RestoreScheme == null && child.PriorityClass == ProcessPriorityClass.Normal, "Game boost: restoring puts power plan and priority back");
                        Check(said.Count == 2 && said[0] == "Game-Boost an" && said[1] == "Game-Boost aus", "Game boost: you get a message at start and end");

                        // switched off: no power plan, no priority
                        c.GameBoost.PowerPlan = false; c.GameBoost.Priority = false; cmds.Clear();
                        b.Boost(child, false);
                        child.Refresh();
                        Check(cmds.Count == 0 && child.PriorityClass == ProcessPriorityClass.Normal, "Game boost: with the options off nothing is changed");
                        b.Restore();
                        c.GameBoost.PowerPlan = true; c.GameBoost.Priority = true;

                        // automatic: list + focus
                        b.Foreground = delegate { return Process.GetProcessById(child.Id); };
                        c.GameBoost.Games.Clear(); c.GameBoost.Auto = true;
                        b.Tick();
                        Check(!b.Active, "Game boost: automatic does nothing for a program that is not on the list");
                        c.GameBoost.Games.Add("ping.exe");
                        b.Tick();
                        Check(b.Active && b.Game == "ping.exe", "Game boost: automatic starts when a listed game has the focus");
                        child.Kill(); child.WaitForExit(3000);
                        b.Tick();
                        Check(!b.Active && current == "381b4222-f694-41f0-9685-ff5bb260df2e", "Game boost: automatic ends and restores when the game exits");
                        c.GameBoost.Auto = false;
                    }
                    finally { try { if (!child.HasExited) child.Kill(); } catch { } child.Dispose(); }

                    // crash recovery: a remembered plan is put back at the next start
                    current = "e9a42b02-d5df-448d-aa00-03f14749eb61"; c.GameBoost.RestoreScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";
                    b.RecoverIfNeeded();
                    Check(current == "381b4222-f694-41f0-9685-ff5bb260df2e" && c.GameBoost.RestoreScheme == null, "Game boost: after a crash the old power plan comes back at the next start");
                    c.GameBoost.RestoreScheme = "x; calc.exe"; cmds.Clear();
                    b.RecoverIfNeeded();
                    Check(cmds.Count == 0 && c.GameBoost.RestoreScheme == null, "Game boost: a damaged config value is never passed to powercfg");
                }
                Config again = Config.Load(Path.Combine(tmp, "config.json"));
                Check(!again.GameBoost.Auto && again.GameBoost.PowerPlan && again.GameBoost.Games.Count >= 0, "Game boost: settings load with safe defaults (automatic is off)");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }

            // ---- live numbers (CPU, RAM, GPU) ----
            using (StatsSampler s = new StatsSampler())
            {
                s.Sample(true);
                System.Threading.Thread.Sleep(600);
                SystemStats st = s.Sample(true);
                Check(st.RamTotalMb > 512 && st.RamPct > 0 && st.RamPct <= 100 && st.RamUsedMb > 0, "Game boost: RAM numbers are real (" + st.RamUsedMb + " / " + st.RamTotalMb + " MB)");
                Check(st.Cpu >= 0 && st.Cpu <= 100, "Game boost: CPU load is a percentage (" + Math.Round(st.Cpu) + " %)");
                Check(st.Gpu == -1 || (st.Gpu >= 0 && st.Gpu <= 100), "Game boost: GPU load is a percentage or 'not available' (" + (st.Gpu < 0 ? "n/a" : Math.Round(st.Gpu) + " %") + ")");
            }
        }

        static void DiscordExtrasTests()
        {
            // ---- themes ----
            DiscordConfig none = new DiscordConfig();
            Check(DiscordTheme.BuildCss(none).Trim().Length == 0, "Discord CSS: nothing set = nothing injected (Discord stays untouched)");
            DiscordConfig peach = new DiscordConfig { Theme = "peach" };
            string css = DiscordTheme.BuildCss(peach);
            Check(css.Contains("[class*='theme-']") && css.Contains("--card-background-default: #2d211e") && css.Contains("--modal-background: #2d211e") && css.Contains("--background-base-low: #261c19 !important") && css.Contains("--background-surface-high: #2d211e") && css.Contains("--chat-background-default: #261c19") && css.Contains("--text-default: #f6e9e4") && css.Contains("--background-brand: #ff7a59"),
                  "Discord CSS: the Peach theme sets Discord's own design variables (base/surface/chat/text/brand)");
            Check(DiscordTheme.Presets.Length == 5 && DiscordTheme.Find("WALD") != null && DiscordTheme.Find("gibts-nicht") == null && DiscordTheme.Find("") == null, "Discord themes: 5 presets, lookup ignores case");
            bool allSet = true;
            foreach (ThemePreset p in DiscordTheme.Presets) { string c = DiscordTheme.BuildCss(new DiscordConfig { Theme = p.Key }); if (!c.Contains(p.Low) || !c.Contains(p.Brand) || !c.Contains(p.Text)) allSet = false; }
            Check(allSet, "Discord themes: every preset produces its own colours");
            string acc = DiscordTheme.BuildCss(new DiscordConfig { Accent = "#33AAFF" });
            Check(acc.Contains("--background-brand: #33aaff") && acc.Contains("--control-primary-background-default: #33aaff") && acc.Contains("--switch-background-selected-default: #33aaff") && acc.Contains("--text-brand:") && acc.Contains("--brand-500: #33aaff") && acc.Contains("--brand-360:") && acc.Contains("--interactive-accent-background-default: #33aaff") && !acc.Contains("--background-base-low"), "Discord CSS: an accent colour alone only changes the accent");
            Check(DiscordTheme.Shade("#808080", 0.2) != "#808080" && DiscordTheme.Shade("#808080", 0.2).CompareTo("#808080") > 0 && DiscordTheme.Shade("#ff0000", 0) == "#ff0000", "Discord CSS: lighter / darker shades of the accent");
            Check(DiscordTheme.IsHex("#A1b2C3") && DiscordTheme.IsHex("a1b2c3") && !DiscordTheme.IsHex("#12345") && !DiscordTheme.IsHex("red") && !DiscordTheme.IsHex(null) && DiscordTheme.NormHex(" #A1B2C3 ") == "#a1b2c3", "Discord accent: hex validation");
            string fontCss = DiscordTheme.BuildCss(new DiscordConfig { Font = "Segoe UI'; } body { display:none } /*" });
            Check(fontCss.Contains("--font-primary: 'Segoe UI") && !fontCss.Contains("display:none") && !fontCss.Contains("}  body"), "Discord CSS: the font name cannot smuggle extra CSS in");
            string mixed = DiscordTheme.BuildCss(new DiscordConfig { ReduceMotion = true, CustomCss = "body { outline: 1px solid red; }" });
            Check(mixed.Contains("animation-duration: 0.01ms") && mixed.TrimEnd().EndsWith("body { outline: 1px solid red; }"), "Discord CSS: reduce-motion and the custom CSS (last, so it wins)");
            Check(DiscordTheme.BuildCss(new DiscordConfig { CustomCss = new string('x', 300000) }).Length < 1000, "Discord CSS: absurdly large custom CSS is ignored");

            // ---- commands ----
            Check(DiscordBridge.ValidCommandName("zeit") && DiscordBridge.ValidCommandName("Gg-2") && !DiscordBridge.ValidCommandName("peach") && !DiscordBridge.ValidCommandName("PEACH") && !DiscordBridge.ValidCommandName("1abc") && !DiscordBridge.ValidCommandName("a b") && !DiscordBridge.ValidCommandName("") && !DiscordBridge.ValidCommandName(new string('a', 40)), "Discord commands: valid names ('peach' is reserved)");
            DiscordConfig cc = new DiscordConfig();
            cc.Commands.Add(new CommandEntry { Name = "gruss", Text = "Hallo {args}!" });
            cc.Commands.Add(new CommandEntry { Name = "peach", Text = "reserved" });
            cc.Commands.Add(new CommandEntry { Name = "bad name", Text = "x" });
            cc.Commands.Add(new CommandEntry { Name = "leer", Text = "" });
            string json = DiscordBridge.SettingsJson(cc);
            Check(json.Contains("\"gruss\"") && !json.Contains("reserved") && !json.Contains("bad name") && !json.Contains("\"leer\"") && json.Contains("\"sendNow\":false"), "Discord commands: only valid ones reach the page (not 'peach', bad names, empty texts)");
            // ---- command actions, hotkeys, abbreviations ----
            DiscordConfig ac = new DiscordConfig();
            ac.Commands.Add(new CommandEntry { Name = "alt", Text = "nur Text" });
            ac.Commands.Add(new CommandEntry { Name = "web", Action = "open", Text = "https://example.com/?q={args}", Hotkey = "Ctrl+Alt+W" });
            ac.Abbreviations.Add(new Snippet { Abbr = "btw", Text = "by the way" });
            ac.Abbreviations.Add(new Snippet { Abbr = "x", Text = "zu kurz" });
            ac.Abbreviations.Add(new Snippet { Abbr = "a b", Text = "Leerzeichen" });
            string aj = DiscordBridge.SettingsJson(ac);
            Check(aj.Contains("\"action\":\"text\"") && aj.Contains("\"action\":\"open\"") && aj.Contains("\"hotkey\":\"ctrl+alt+w\"") && aj.Contains("\"a\":\"btw\"") && !aj.Contains("zu kurz") && !aj.Contains("Leerzeichen") && !aj.Contains("example.com"),
                  "Discord extras: the page gets action, hotkey and valid abbreviations (but never the target of an open command)");
            string tg, ar;
            CommandEntry web = ac.Commands[1];
            Check(DiscordBridge.ResolveOpen(web, "a b&c", out tg, out ar) && tg == "https://example.com/?q=a%20b%26c" && ar == null, "Discord open: URL commands fill {args} URL-encoded");
            CommandEntry prog = new CommandEntry { Name = "p", Action = "open", Text = "C:\\Tools\\x.exe", Args = " --fest {args} " };
            Check(DiscordBridge.ResolveOpen(prog, "; evil", out tg, out ar) && tg == "C:\\Tools\\x.exe" && ar == "--fest {args}", "Discord open: a program gets only its fixed arguments, {args} is not replaced");
            Check(!DiscordBridge.ResolveOpen(ac.Commands[0], "", out tg, out ar) && !DiscordBridge.ResolveOpen(new CommandEntry { Action = "open", Text = "  " }, "", out tg, out ar) && !DiscordBridge.ResolveOpen(null, "", out tg, out ar), "Discord open: text commands and empty targets are never opened");
            Check(DiscordBridge.ValidAbbreviation("btw") && DiscordBridge.ValidAbbreviation(";x") && !DiscordBridge.ValidAbbreviation("x") && !DiscordBridge.ValidAbbreviation("a b") && !DiscordBridge.ValidAbbreviation(null), "Discord abbreviations: 2+ characters, no spaces");
            Config cfgA = Config.Load(Path.Combine(Path.GetTempPath(), "peach-selftest-dca-" + Guid.NewGuid().ToString("N"), "config.json"));
            try
            {
                CommandEntry ex = cfgA.Discord.Commands[0];
                Check(ex.Action == null && DiscordSettingsForm.CheckCommand(cfgA, null, "neu", "x", "text", "") == null, "Discord settings: old commands without an action count as text commands");
                Check(DiscordSettingsForm.CheckCommand(cfgA, null, "peach", "x", "text", "") != null && DiscordSettingsForm.CheckCommand(cfgA, null, "ZEIT", "x", "text", "") != null && DiscordSettingsForm.CheckCommand(cfgA, null, "ok", "  ", "text", "") != null, "Discord settings: reserved, duplicate and empty commands are refused");
                Check(DiscordSettingsForm.CheckCommand(cfgA, null, "ok", "x", "text", "shift+a") != null && DiscordSettingsForm.CheckCommand(cfgA, null, "ok", "x", "text", "ctrl+alt+p") != null && DiscordSettingsForm.CheckCommand(cfgA, null, "ok", "x", "text", "ctrl+alt+1") == null, "Discord settings: a hotkey needs Strg/Alt and cannot take Strg+Alt+P");
                cfgA.Hotkeys.Add(new HotkeyEntry { Combo = "ctrl+alt+k", Action = "open", Target = "calc.exe" });
                cfgA.Discord.Commands.Add(new CommandEntry { Name = "k1", Text = "x", Hotkey = "ctrl+alt+7" });
                Check(DiscordSettingsForm.CheckCommand(cfgA, null, "k2", "x", "text", "ctrl+alt+k") != null && DiscordSettingsForm.CheckCommand(cfgA, null, "k2", "x", "text", "ctrl+alt+7") != null && DiscordSettingsForm.CheckCommand(cfgA, cfgA.Discord.Commands[2], "k1", "y", "text", "ctrl+alt+7") == null, "Discord settings: a hotkey cannot be a global Peach hotkey or used twice (editing yourself is fine)");
                cfgA.Discord.Abbreviations.Add(new Snippet { Abbr = "btw", Text = "x" });
                Check(DiscordSettingsForm.CheckAbbr(cfgA, null, "BTW", "x") != null && DiscordSettingsForm.CheckAbbr(cfgA, null, "btwx", "x") != null && DiscordSettingsForm.CheckAbbr(cfgA, null, "q", "x") != null && DiscordSettingsForm.CheckAbbr(cfgA, null, "zz", "") != null && DiscordSettingsForm.CheckAbbr(cfgA, null, "zz", "ok") == null, "Discord settings: duplicate, conflicting, too short and empty abbreviations are refused");
            }
            finally { try { Directory.Delete(cfgA.ConfigDir(), true); } catch { } }

            Check(DiscordBridge.Bridge(cc, true).StartsWith("window.__peachForce = true;") && !DiscordBridge.Bridge(cc, false).Contains("__peachForce = true"), "Discord bridge: the test switch is only in test builds of the script");
            Check(DiscordBridge.WrapUserScript("my script!.js", "peach.toast('x');").Contains("my_script_.js") && DiscordBridge.WrapUserScript("a.js", "x").Contains("catch (e)"), "Discord scripts: each script is wrapped so an error shows up as a message and cannot break the others");

            // ---- scripts folder (opt-in) ----
            string tmp = Path.Combine(Path.GetTempPath(), "peach-selftest-dc-" + Guid.NewGuid().ToString("N"));
            try
            {
                Config c = Config.Load(Path.Combine(tmp, "config.json"));
                Check(c.Discord.Commands.Count == 2 && c.Discord.Commands[0].Name == "zeit" && c.Discord.Seeded, "Discord config: a new config starts with two example commands (/zeit, /gg)");
                Check(DiscordScripts.Files(c).Count == 0, "Discord scripts: no folder, no scripts");
                string n1 = DiscordScripts.Create(c, "Mein Skript!");
                string n2 = DiscordScripts.Create(c, "Mein Skript!");
                Check(n1 == "mein-skript.js" && n2 == "mein-skript-2.js" && File.ReadAllText(Path.Combine(DiscordScripts.Dir(c), n1)).Contains("peach.command"), "Discord scripts: created from the template, names stay unique");
                Check(DiscordScripts.Enabled(c).Count == 2, "Discord scripts: a script you create yourself starts switched on");
                File.WriteAllText(Path.Combine(DiscordScripts.Dir(c), "fremd.js"), "peach.toast('hi');");
                Check(DiscordScripts.Files(c).Contains("fremd.js") && DiscordScripts.Enabled(c).Count == 2, "Discord scripts: a file dropped into the folder does NOT run until you switch it on");
                c.Discord.EnabledScripts.Add("fremd.js");
                Check(DiscordScripts.Enabled(c).Count == 3, "Discord scripts: switching it on makes it run");
                File.WriteAllText(Path.Combine(DiscordScripts.Dir(c), "readme.txt"), "x"); File.WriteAllText(Path.Combine(DiscordScripts.Dir(c), "evil name.js"), "x");
                Check(!DiscordScripts.Files(c).Contains("readme.txt") && !DiscordScripts.Files(c).Contains("evil name.js") && !DiscordScripts.ValidName("..\\x.js") && !DiscordScripts.ValidName("a/b.js") && DiscordScripts.ValidName("ok-1.js"), "Discord scripts: only plain *.js names are accepted");
                Check(DiscordScripts.Delete(c, n2) && !File.Exists(Path.Combine(DiscordScripts.Dir(c), n2)) && !c.Discord.EnabledScripts.Contains(n2) && !DiscordScripts.Delete(c, "..\\..\\config.json") && File.Exists(Path.Combine(tmp, "config.json")), "Discord scripts: delete removes the file and the switch, and refuses paths");
                c.Discord.Theme = "wald"; c.Discord.Accent = "#112233"; c.Discord.Zoom = 500; c.Discord.CustomCss = "a{b:c}"; c.Save();
                Config again = Config.Load(Path.Combine(tmp, "config.json"));
                Check(again.Discord.Theme == "wald" && again.Discord.Accent == "#112233" && again.Discord.Zoom == 160 && again.Discord.CustomCss == "a{b:c}" && again.Discord.EnabledScripts.Contains("fremd.js") && again.Discord.Commands.Count == 2, "Discord config: settings survive a reload (zoom is limited to 160 %)");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

#if WEBVIEW2
        // The bridge in a real browser, on a local page that imitates Discord's message box and theme variables.
        static void DiscordBridgeLiveTests()
        {
            if (WebView2Setup.RuntimeVersion() == null) { log.AppendLine("SKIP Discord bridge live test (WebView2 runtime not installed)"); return; }
            string profile = TempDir();
            string scriptRoot = Path.Combine(profile + "-cfg");
            DiscordForm f = null;
            List<string> messages = new List<string>();
            Action<string, string> oldHost = DiscordForm.HostCommand;
            Action<string, string> oldRun = null;
            DiscordForm.HostCommand = delegate (string type, string value) { messages.Add(type + ":" + value); };
            try
            {
                Config cfg = Config.Load(Path.Combine(scriptRoot, "config.json"));
                cfg.Discord.Theme = "peach"; cfg.Discord.Accent = "#33aaff";
                cfg.Discord.Commands.Add(new CommandEntry { Name = "gruss", Text = "Hallo {args}!" });
                cfg.Discord.Commands.Add(new CommandEntry { Name = "suche", Action = "open", Text = "https://example.com/s?q={args}", Hotkey = "ctrl+alt+2" });
                cfg.Discord.Commands.Add(new CommandEntry { Name = "prog", Action = "open", Text = "notepad.exe", Args = "fest.txt" });
                cfg.Discord.Commands.Add(new CommandEntry { Name = "hk", Text = "per-Hotkey", Hotkey = "ctrl+alt+1" });
                cfg.Discord.Abbreviations.Add(new Snippet { Abbr = "btw", Text = "by the way" });
                cfg.Discord.Abbreviations.Add(new Snippet { Abbr = ";gn", Text = "Gute Nacht" });
                List<string> opened = new List<string>();
                oldRun = DiscordForm.RunOpen;
                DiscordForm.RunOpen = delegate (string t, string a) { opened.Add(t + "|" + a); };
                // a script that is on, one that is off
                Directory.CreateDirectory(DiscordScripts.Dir(cfg));
                File.WriteAllText(Path.Combine(DiscordScripts.Dir(cfg), "an.js"), "peach.command('zz', a => 'ZZ-' + a); peach.css('#marker { outline: 3px solid rgb(1, 2, 3); }');");
                File.WriteAllText(Path.Combine(DiscordScripts.Dir(cfg), "aus.js"), "peach.command('nein', () => 'sollte nie laufen');");
                File.WriteAllText(Path.Combine(DiscordScripts.Dir(cfg), "kaputt.js"), "peach.command('kaputt', ;;; syntax error here");
                cfg.Discord.EnabledScripts.Add("an.js"); cfg.Discord.EnabledScripts.Add("kaputt.js");

                f = new DiscordForm(profile, "about:blank", cfg);
                f.TestMode = true;
                f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(-6000, -6000); f.ShowInTaskbar = false;
                f.Show();
                if (!DiscordForm.Pump(delegate { return f.WebReady; }, 45000)) { Check(false, "Discord bridge: browser did not start"); return; }
                DiscordForm.Pump(delegate { return false; }, 600);   // let the first injection finish

                string page = "<html class='theme-dark theme-darker'><head><style>.theme-darker{--background-base-low:rgb(255,0,0);--brand-500:rgb(0,255,0);} #t{background:var(--background-base-low);color:var(--brand-500)}</style></head>" +
                              "<body><div id='t'>x</div><div id='marker'>m</div><div id='b' role='textbox' contenteditable='true'></div></body></html>";
                f.Web.CoreWebView2.NavigateToString(page);
                // wait for THIS page (the earlier about:blank also reports "navigation completed")
                DateTime until = DateTime.Now.AddSeconds(20);
                while (DateTime.Now < until && f.Eval("!!document.getElementById('b')", 2000) != "true") DiscordForm.Pump(delegate { return false; }, 150);
                string injected = f.Eval("typeof window.__peach + '/' + (window.__peach && typeof window.__peach.run)", 3000);
                Check(injected == "\"object/function\"", "Discord bridge: the script is injected into the page before anything else (" + injected + ")");

                // theme overrides beat the page's own theme variables
                string bg = f.Eval("getComputedStyle(document.getElementById('t')).backgroundColor", 5000);
                string fg = f.Eval("getComputedStyle(document.getElementById('t')).color", 5000);
                Check(bg == "\"rgb(38, 28, 25)\"", "Discord bridge: the Peach theme overrides the page's theme variable (background " + bg + ")");
                Check(fg == "\"rgb(51, 170, 255)\"", "Discord bridge: the accent colour overrides the brand variable (" + fg + ")");

                // typing a command + Enter
                Func<string, string> typeAndEnter = delegate (string text)
                {
                    return f.Eval("(function(){var b=document.getElementById('b');b.focus();b.textContent=" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(text) +
                                  ";var ev=new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true});b.dispatchEvent(ev);return JSON.stringify({prevented:ev.defaultPrevented,text:b.textContent})})()", 5000);
                };
                string r1 = typeAndEnter("/gruss Welt");
                Check(r1.Contains("prevented\\\":true") && r1.Contains("Hallo Welt!"), "Discord commands: /gruss Welt turns into the text „Hallo Welt!“ in the message box and Enter is not passed on (" + r1 + ")");
                string r2 = typeAndEnter("/unbekannt etwas");
                Check(r2.Contains("prevented\\\":false") && r2.Contains("/unbekannt etwas"), "Discord commands: unknown /commands are left alone for Discord itself");
                string r3 = typeAndEnter("hallo normal");
                Check(r3.Contains("prevented\\\":false") && r3.Contains("hallo normal"), "Discord commands: normal messages are untouched");
                string r4 = typeAndEnter("/peach settings");
                Check(r4.Contains("prevented\\\":true") && messages.Contains("settings:"), "Discord commands: /peach settings is caught and sent to Peach, not to Discord (" + string.Join(",", messages.ToArray()) + ")");
                string r5 = typeAndEnter("/peach theme wald");
                Check(r5.Contains("prevented\\\":true") && messages.Contains("theme:wald"), "Discord commands: /peach theme wald reaches Peach");
                DiscordForm.Pump(delegate { return false; }, 400);
                Check(cfg.Discord.Theme == "wald", "Discord commands: ... and Peach switched the theme (now " + cfg.Discord.Theme + ")");
                string help = typeAndEnter("/peach help");
                Check(help.Contains("prevented\\\":true") && f.Eval("document.body.innerText.indexOf('/peach settings') >= 0 && document.body.innerText.indexOf('/gruss') >= 0", 3000) == "true", "Discord commands: /peach help shows a message with the built-in and your own commands");
                f.Eval("(function(){var b=document.getElementById('b');b.textContent='/gruss';b.focus();})()", 2000);
                string r6 = typeAndEnter("/GRUSS  Anton ");
                Check(r6.Contains("Hallo Anton!"), "Discord commands: case-insensitive, extra spaces are trimmed");

                // scripts: the one that is on works, the one that is off does not, the broken one breaks nothing
                string r7 = typeAndEnter("/zz 42");
                Check(r7.Contains("prevented\\\":true") && r7.Contains("ZZ-42"), "Discord scripts: a switched-on script can add a command (/zz 42 -> ZZ-42)");
                string r8 = typeAndEnter("/nein");
                Check(r8.Contains("prevented\\\":false"), "Discord scripts: a script that is switched off does not run");
                Check(f.Eval("getComputedStyle(document.getElementById('marker')).outlineColor", 3000) == "\"rgb(1, 2, 3)\"", "Discord scripts: peach.css() from a script works");
                string r9 = typeAndEnter("/kaputt");
                Check(r9.Contains("prevented\\\":false"), "Discord scripts: a script with a syntax error does not break the others (the bridge and /zz above still worked)");

                // open commands: the page only names the command, Peach does the opening
                string o1 = typeAndEnter("/suche a b&c");
                DiscordForm.Pump(delegate { return opened.Count > 0; }, 4000);
                Check(o1.Contains("prevented\\\":true") && opened.Count == 1 && opened[0] == "https://example.com/s?q=a%20b%26c|", "Discord commands: an open command with a URL opens it with {args} URL-encoded (" + string.Join(";", opened.ToArray()) + ")");
                opened.Clear();
                typeAndEnter("/prog ; calc.exe & del x");
                DiscordForm.Pump(delegate { return opened.Count > 0; }, 4000);
                Check(opened.Count == 1 && opened[0] == "notepad.exe|fest.txt", "Discord commands: a program command only gets its fixed arguments, never the chat text (" + string.Join(";", opened.ToArray()) + ")");
                opened.Clear();
                f.Eval("window.chrome.webview.postMessage({type:'run',name:'zeit',args:''}); window.chrome.webview.postMessage({type:'run',name:'calc',args:''});", 2000);
                DiscordForm.Pump(delegate { return false; }, 500);
                Check(opened.Count == 0, "Discord commands: the page cannot start anything that is not one of your open commands");

                // hotkeys: only inside the Discord window
                f.Eval("(function(){var b=document.getElementById('b');b.textContent='';b.focus();})()", 2000);
                f.Eval("document.dispatchEvent(new KeyboardEvent('keydown',{code:'Digit1',key:'1',ctrlKey:true,altKey:true,bubbles:true,cancelable:true}))", 2000);
                DiscordForm.Pump(delegate { return false; }, 200);
                Check(f.Eval("document.getElementById('b').textContent", 2000) == "\"per-Hotkey\"", "Discord hotkeys: Strg+Alt+1 puts the command's text into the message box");
                opened.Clear();
                f.Eval("document.getElementById('b').focus(); document.dispatchEvent(new KeyboardEvent('keydown',{code:'Digit2',key:'2',ctrlKey:true,altKey:true,bubbles:true,cancelable:true}))", 2000);
                DiscordForm.Pump(delegate { return opened.Count > 0; }, 4000);
                Check(opened.Count == 1 && opened[0] == "https://example.com/s?q=|", "Discord hotkeys: a hotkey can run an open command");
                f.Eval("(function(){var b=document.getElementById('b');b.textContent='';b.focus();})()", 2000);
                f.Eval("document.dispatchEvent(new KeyboardEvent('keydown',{code:'Digit1',key:'1',shiftKey:true,bubbles:true,cancelable:true}))", 2000);
                Check(f.Eval("document.getElementById('b').textContent", 2000) == "\"\"", "Discord hotkeys: without Strg/Alt nothing happens (normal typing is never taken)");

                // abbreviations while typing
                Func<string, string> typeKeys = delegate (string s)
                {
                    // real key events sent straight into the page (DevTools protocol): no keys go to the user's PC
                    f.Eval("(function(){var b=document.getElementById('b');b.textContent='';b.focus();})()", 2000);
                    foreach (char ch in s)
                    {
                        string t = ch.ToString(), jt = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(t);
                        System.Threading.Tasks.Task<string> k = f.Web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyDown\",\"text\":" + jt + ",\"unmodifiedText\":" + jt + ",\"key\":" + jt + "}");
                        DiscordForm.Pump(delegate { return k.IsCompleted; }, 3000);
                        System.Threading.Tasks.Task<string> u = f.Web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\",\"key\":" + jt + "}");
                        DiscordForm.Pump(delegate { return u.IsCompleted; }, 3000);
                        DiscordForm.Pump(delegate { return false; }, 25);
                    }
                    DiscordForm.Pump(delegate { return false; }, 100);
                    return f.Eval("document.getElementById('b').textContent", 2000);
                };
                string tk = typeKeys("btw");
                Check(tk == "\"by the way\"", "Discord abbreviations: typing btw becomes „by the way“ (got " + tk + ")");
                Check(typeKeys("ok btw") == "\"ok by the way\"", "Discord abbreviations: works after other words");
                Check(typeKeys("abtw") == "\"abtw\"", "Discord abbreviations: not in the middle of a word");
                Check(typeKeys("x;gn") == "\"xGute Nacht\"", "Discord abbreviations: an abbreviation starting with ';' works right after a letter");
                Check(typeKeys("BTW") == "\"by the way\"", "Discord abbreviations: ignore upper/lower case");
                Check(typeKeys("bt") == "\"bt\"", "Discord abbreviations: unfinished abbreviation is left alone");

                // live update without reload
                cfg.Discord.Theme = "amoled"; cfg.Discord.Accent = "";
                System.Threading.Tasks.Task apply = f.ApplySettingsAsync(false);
                DiscordForm.Pump(delegate { return apply.IsCompleted; }, 10000);
                string bg2 = f.Eval("getComputedStyle(document.getElementById('t')).backgroundColor", 5000);
                Check(bg2 == "\"rgb(5, 5, 5)\"", "Discord settings: a theme change shows up in the open page right away (" + bg2 + ")");
            }
            finally
            {
                DiscordForm.HostCommand = oldHost;
                if (oldRun != null) DiscordForm.RunOpen = oldRun;
                if (f != null) { try { f.ForceClose(); } catch { } }
                System.Threading.Thread.Sleep(800);
                try { Directory.Delete(profile, true); } catch { }
                try { Directory.Delete(scriptRoot, true); } catch { }
            }
        }

        // The real embedded browser, offline: starts, runs JavaScript, and Peach's rules stop a page from leaving Discord.
        static void DiscordLiveTests()
        {
            if (WebView2Setup.RuntimeVersion() == null) { log.AppendLine("SKIP Discord live test (WebView2 runtime not installed)"); return; }
            string profile = TempDir();
            string captured = null;
            Action<string> oldOpen = DiscordForm.OpenExternal;
            DiscordForm.OpenExternal = delegate (string u) { captured = u; };
            DiscordForm f = null;
            try
            {
                f = new DiscordForm(profile, "about:blank", Config.Load(Path.Combine(profile + "-cfg", "config.json")));
                f.TestMode = true;
                f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(-6000, -6000); f.ShowInTaskbar = false;
                f.Show();
                bool ready = DiscordForm.Pump(delegate { return f.WebReady; }, 45000);
                Check(ready, "WebView2: the embedded browser starts (runtime " + WebView2Setup.RuntimeVersion() + ")");
                if (!ready) return;

                System.Threading.Tasks.Task<string> js = f.Web.CoreWebView2.ExecuteScriptAsync("6*7");
                DiscordForm.Pump(delegate { return js.IsCompleted; }, 10000);
                Check(js.IsCompleted && js.Result == "42", "WebView2: runs JavaScript (6*7 = " + (js.IsCompleted ? js.Result : "?") + ")");

                f.Web.CoreWebView2.NavigateToString("<html><title>t</title><script>location.href='https://example.com/blocked?x=1';</script></html>");
                DiscordForm.Pump(delegate { return captured != null; }, 20000);
                Check(captured == "https://example.com/blocked?x=1", "Discord window: a page that tries to leave for a foreign site is stopped and the link goes to the normal browser (" + captured + ")");
                string src = f.Web.CoreWebView2.Source;
                Check(src != null && !src.Contains("example.com"), "Discord window: ... the window itself stays where it was");

                captured = null;
                f.Web.CoreWebView2.NavigateToString("<html><body><a id='l' href='https://example.org/new' target='_blank'>x</a><script>document.getElementById('l').click();</script></body></html>");
                DiscordForm.Pump(delegate { return captured != null; }, 15000);
                Check(captured == "https://example.org/new", "Discord window: a link that wants a new window goes to the normal browser (no extra window inside Peach)");

                f.Close();
                Check(!f.IsDisposed && !f.Visible, "Discord window: the X only hides it (so a running voice call is not cut)");
            }
            finally
            {
                DiscordForm.OpenExternal = oldOpen;
                if (f != null) { try { f.ForceClose(); } catch { } }
                System.Threading.Thread.Sleep(800);
                try { Directory.Delete(profile, true); } catch { }
            }
        }
#endif

        static void ConnectionTests()
        {
            GamepadMouse c = new GamepadMouse(false, 1200, false);
            int changes = 0; bool last = false;
            c.ConnectionChanged += delegate (bool v) { changes++; last = v; };
            Check(!c.Connected, "Connection: starts as not connected");
            c.SetConnected(false);
            Check(changes == 0, "Connection: no event when nothing changed");
            c.SetConnected(true); c.SetConnected(true);
            Check(changes == 1 && last && c.Connected, "Connection: plugging in fires once");
            c.SetConnected(false);
            Check(changes == 2 && !last && !c.Connected, "Connection: unplugging fires once");
        }

        static void GameBarTests()
        {
            // a throw-away key: the real Windows Game Bar setting is never touched by tests
            string key = @"Software\Peach\SelfTest-" + Guid.NewGuid().ToString("N");
            try
            {
                Check(GameBar.ControllerButtonOpensGameBar(key), "GameBar: a missing value means Windows default (Xbox button opens the Game Bar)");
                Check(GameBar.SetControllerButtonOpensGameBar(false, key) && !GameBar.ControllerButtonOpensGameBar(key), "GameBar: switching it off is read back as off");
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
                    Check(k != null && k.GetValue("UseNexusForGameBarEnabled") is int && (int)k.GetValue("UseNexusForGameBarEnabled") == 0 && k.GetValueKind("UseNexusForGameBarEnabled") == Microsoft.Win32.RegistryValueKind.DWord,
                          "GameBar: stored as DWORD UseNexusForGameBarEnabled = 0 (what Windows Settings writes)");
                Check(GameBar.SetControllerButtonOpensGameBar(true, key) && GameBar.ControllerButtonOpensGameBar(key), "GameBar: switching it back on is read back as on");
            }
            finally
            {
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\Peach", false); } catch { }
            }
        }

        static void StartAppsTests()
        {
            string sample = "[{\"Name\":\"Discord\",\"AppID\":\"com.squirrel.Discord.Discord\"},{\"Name\":\"Rechner\",\"AppID\":\"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App\"}," +
                            "{\"Name\":\"Support\",\"AppID\":\"http://support.example.com/\"},{\"Name\":\"Doppelt\",\"AppID\":\"com.squirrel.Discord.Discord\"},{\"Name\":\"\",\"AppID\":\"x\"}]";
            List<StartApp> p = StartApps.Parse(sample);
            Check(p.Count == 2 && p[0].Name == "Discord", "StartApps.Parse: keeps apps, drops web links, duplicates and nameless entries");
            Check(p[0].Path == "shell:AppsFolder\\com.squirrel.Discord.Discord", "StartApps: path is a shell:AppsFolder target");
            List<StartApp> one = StartApps.Parse("{\"Name\":\"Solo\",\"AppID\":\"solo.app\"}");
            Check(one.Count == 1 && one[0].Name == "Solo", "StartApps.Parse: a single object (PowerShell unwraps one-item arrays)");
            Check(StartApps.Parse("").Count == 0 && StartApps.Parse("  ").Count == 0, "StartApps.Parse: empty output");

            List<StartApp> live = StartApps.Load();
            bool discord = false;
            foreach (StartApp a in live) if (a.Name.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0) discord = true;
            Check(live.Count > 10, "StartApps.Load: Get-StartApps lists this PC's apps (" + live.Count + " found, Discord " + (discord ? "present" : "not installed") + ")");

            int withIcon = 0, tried = 0;
            foreach (StartApp a in live)
            {
                if (tried++ >= 15) break;
                using (System.Drawing.Bitmap b = ShellIcon.FromShell(a.Path)) if (b != null && b.Width >= 16) withIcon++;
            }
            Check(withIcon >= 8, "ShellIcon: real icons load for shell:AppsFolder targets (" + withIcon + " of " + Math.Min(15, live.Count) + ")");
        }

        public static int Run(string outFile)
        {
            uint mods, vk;
            Check(Combo.Parse("ctrl+alt+k", out mods, out vk) && mods == (Native.MOD_CONTROL | Native.MOD_ALT) && vk == (uint)Keys.K, "Combo.Parse ctrl+alt+k");
            Check(Combo.Normalize("Shift+Ctrl+F5") == "ctrl+shift+f5", "Combo.Normalize orders modifiers");
            Check(Combo.Parse("k", out mods, out vk) == false, "Combo.Parse rejects a key without modifier");
            Check(Combo.Parse("ctrl+banana", out mods, out vk) == false, "Combo.Parse rejects unknown key");
            Check(Combo.KeyName(Keys.D7) == "7" && Combo.KeyName(Keys.PageDown) == "pagedown", "Combo.KeyName digits and named keys");

            double ox, oy;
            GamepadMouse.Stick(2000, -1500, out ox, out oy);
            Check(ox == 0 && oy == 0, "Stick: small deflection is inside the dead zone");
            GamepadMouse.Stick(32767, 0, out ox, out oy);
            Check(Math.Abs(ox - 1.0) < 0.001 && oy == 0, "Stick: full deflection gives 1.0");
            GamepadMouse.Stick(0, short.MinValue, out ox, out oy);
            Check(oy < -0.99 && Math.Abs(ox) < 0.001, "Stick: full down is -1.0 on Y");
            double sx, sy, hx, hy;
            GamepadMouse.Stick(14000, 0, out sx, out sy);
            GamepadMouse.Stick(28000, 0, out hx, out hy);
            Check(hx > sx * 2, "Stick: response is progressive");

            List<Snippet> sn = new List<Snippet>();
            sn.Add(new Snippet { Abbr = ";mfg", Text = "x" });
            sn.Add(new Snippet { Abbr = "btw", Text = "y" });
            Check(TextExpander.Find("hallo ;mfg", sn) != null, "Find: ;mfg after a space");
            Check(TextExpander.Find("abc;mfg", sn) != null, "Find: ;mfg directly after letters (prefix char is a boundary)");
            Check(TextExpander.Find("btw", sn) != null, "Find: abbreviation at buffer start");
            Check(TextExpander.Find("obtw", sn) == null, "Find: no match inside a word");
            Check(TextExpander.Find("BTW", sn) != null, "Find: case-insensitive");
            Check(TextExpander.Find("mfg", sn) == null, "Find: needs the ; prefix");
            Check(TextExpander.Expand("{date}").Length == 10 && TextExpander.Expand("a{time}b").Length == 7, "Expand: {date} and {time}");

            KeyboardTests();
            PadTests();
            LauncherTests();
            StartAppsTests();
            ConnectionTests();
            AddonRowTests();
            AddonHostParseTests();
            AddonFileTests();
            ListGeometryTests();
            AddonHostLiveTests();
            DiscordCoreTests();
            DiscordExtrasTests();
            GameBoostTests();
            MinecraftTests();
#if WEBVIEW2
            DiscordLiveTests();
            DiscordBridgeLiveTests();
#endif
            CodeEditorTests();
            CodeBoxTests();
            ApiCatalogLiveTests();
            BarTests();
            GameBarTests();

            string tmp = Path.Combine(Path.GetTempPath(), "peach-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Config c = Config.Load(tmp);
                Check(c.Snippets.Count >= 4, "Config: fresh config seeds default snippets");
                Check(c.Apps.Count >= 5 && c.AppsSeeded, "Config: fresh config seeds default apps for the launcher");
                c.Hotkeys.Add(new HotkeyEntry { Combo = "ctrl+alt+k", Action = "type", Target = "gr\u00FC\u00DFe\nzeile2" });
                c.MouseSpeed = 777;
                c.ControllerMode = true;
                c.Apps.Clear();
                c.Apps.Add(new AppEntry { Name = "Sp\u00E4ter", Path = "C:\\x\\y.exe", Args = "--a \"b\"" });
                c.Save();
                Config d = Config.Load(tmp);
                Check(d.MouseSpeed == 777 && d.Hotkeys.Count == 1 && d.Hotkeys[0].Target == "gr\u00FC\u00DFe\nzeile2", "Config: save/load round trip incl. umlauts and newline");
                Check(d.ControllerMode && d.Apps.Count == 1 && d.Apps[0].Name == "Sp\u00E4ter" && d.Apps[0].Args == "--a \"b\"", "Config: controller mode and a custom app list survive a reload (no re-seeding)");
            }
            finally { try { File.Delete(tmp); } catch { } }

            log.AppendLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            File.WriteAllText(outFile, log.ToString(), new UTF8Encoding(false));
            return failed == 0 ? 0 : 1;
        }
    }
}
