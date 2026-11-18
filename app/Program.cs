using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Peach
{
    internal static class Branding
    {
        public static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush b = new LinearGradientBrush(new Rectangle(2, 6, 28, 26), Color.FromArgb(255, 180, 130), Color.FromArgb(255, 110, 80), 60f))
                        g.FillEllipse(b, 2, 7, 27, 24);
                    using (Brush leaf = new SolidBrush(Color.FromArgb(80, 180, 100)))
                        g.FillEllipse(leaf, 15, 1, 13, 8);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    internal static class Program
    {
        const string MutexName = "Peach.Client.Single";
        const string EventName = "Peach.Client.ShowMenu";

        [STAThread]
        static int Main(string[] args)
        {
            WebView2Setup.Init();   // first: lets the later Discord window find lib\webview2
            string cfgPath = null, selftestOut = null, iconOut = null, renderOut = null, renderLauncher = null, renderBar = null, renderUi = null, renderUiFile = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--config") cfgPath = args[i + 1];
                if (args[i] == "--selftest") selftestOut = args[i + 1];
                if (args[i] == "--make-icon") iconOut = args[i + 1];
                if (args[i] == "--render-keyboard") renderOut = args[i + 1];
                if (args[i] == "--render-launcher") renderLauncher = args[i + 1];
                if (args[i] == "--render-bar") renderBar = args[i + 1];
                if (args[i] == "--render-ui" && i + 2 < args.Length) { renderUi = args[i + 1]; renderUiFile = args[i + 2]; }
            }
            if (renderUi != null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Config uc = Config.Load(cfgPath ?? Path.Combine(Path.GetTempPath(), "peach-render-" + Guid.NewGuid().ToString("N") + ".json"));
                return UiPreview.Render(renderUi, renderUiFile, uc);
            }
            if (renderBar != null)
            {
                Config c = Config.Load(cfgPath ?? Path.Combine(Path.GetTempPath(), "peach-render-" + Guid.NewGuid().ToString("N") + ".json"));
                using (BarForm b = new BarForm(c, delegate { }, delegate { }, delegate { }, delegate { }))
                    b.RenderTo(renderBar, 1400, new string[] { "Discord", "Spotify – Chill Mix", "Peach – Visual Studio Code", "Downloads", "Rechner" });
                return 0;
            }
            if (selftestOut != null) return SelfTest.Run(selftestOut);
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--mc-launch-test") return SelfTest.McLaunchTest(args[i + 1]);
            if (renderOut != null)
            {
                using (OnScreenKeyboard k = new OnScreenKeyboard())
                {
                    Size s = OnScreenKeyboard.ComputeSize(1000);
                    using (Bitmap bmp = new Bitmap(s.Width, s.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) k.Draw(g, s.Width);
                        bmp.Save(renderOut, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }
            if (renderLauncher != null)
            {
                Config c = Config.Load(cfgPath ?? Path.Combine(Path.GetTempPath(), "peach-render-" + Guid.NewGuid().ToString("N") + ".json"));
                using (LauncherForm l = new LauncherForm(c, delegate { }, delegate { }))
                {
                    l.RenderTo(renderLauncher, 1600, 900);
                }
                return 0;
            }
            if (iconOut != null)
            {
                using (FileStream fs = new FileStream(iconOut, FileMode.Create)) Branding.MakeIcon().Save(fs);
                return 0;
            }

            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            bool first;
            Mutex mutex = new Mutex(true, MutexName, out first);
            if (!first)
            {
                showEvent.Set();   // already running: just pop up its menu
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool testHooks = Array.IndexOf(args, "--test-hooks") >= 0;
            Application.Run(new PeachContext(cfgPath, showEvent, testHooks));
            GC.KeepAlive(mutex);
            return 0;
        }
    }

    internal class PeachContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        readonly Config cfg;
        readonly NotifyIcon tray;
        readonly Icon icon;
        readonly HotkeyManager hotkeys;
        readonly TextExpander expander;
        readonly GamepadMouse pad;
        readonly MenuForm menu;
        readonly OnScreenKeyboard keyboard;
        readonly BarForm bar;
        readonly LauncherForm launcher;
        readonly Control sync;
        readonly RegisteredWaitHandle wait;
        readonly int ownPid = Process.GetCurrentProcess().Id;
        AutomationFocusChangedEventHandler focusHandler;
        readonly AddonHost addonHost;
        readonly GameBooster booster;
        readonly WidgetOverlay overlay;
        GameBoostForm boostForm;
        MinecraftForm mcForm;
        AddonsForm addons;

        public PeachContext(string cfgPath, EventWaitHandle showEvent, bool testHooks)
        {
            cfg = Config.Load(cfgPath);
            sync = new Control();
            IntPtr forceHandle = sync.Handle;   // creates the handle so BeginInvoke works from other threads

            icon = Branding.MakeIcon();
            tray = new NotifyIcon();
            tray.Icon = icon;
            tray.Text = "Peach Client (Strg+Alt+P)";
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Menü öffnen  (Strg+Alt+P)", null, delegate { ShowMenu(); });
            cm.Items.Add("Addons & Einstellungen\u2026", null, delegate { OpenAddons(); });
            cm.Items.Add("Beenden", null, delegate { Quit(); });
            tray.ContextMenuStrip = cm;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMenu(); };
            tray.Visible = true;

            launcher = new LauncherForm(cfg, LaunchApp, delegate { SetMode(false); });

            addonHost = new AddonHost(AppDomain.CurrentDomain.BaseDirectory);

            booster = new GameBooster(cfg);
            booster.Notify += delegate (string t, string m) { Balloon(t, m); };
            booster.Start();
            overlay = new WidgetOverlay(cfg);
            ApplyOverlay();

            List<MenuEntry> items = new List<MenuEntry>();
            items.Add(new MenuEntry { Label = delegate { return "Addons & Einstellungen\u2026"; }, Run = OpenAddons });
            items.Add(new MenuEntry { Label = delegate { return "Controller-Modus"; }, State = delegate { return cfg.ControllerMode ? (pad.Connected ? "AN" : "PAUSE") : (pad.Connected ? "AUS" : "kein Controller"); }, Run = delegate { SetMode(!cfg.ControllerMode); } });
            items.Add(new MenuEntry { Label = delegate { return "Controller \u2192 Maus"; }, State = delegate { return pad.Enabled ? "AN" : "AUS"; }, KeepOpen = true, Run = ToggleMouse });
            items.Add(new MenuEntry { Label = delegate { return "Tastatur automatisch (Textfeld)"; }, State = delegate { return cfg.AutoKeyboard ? "AN" : "AUS"; }, KeepOpen = true, Run = delegate { cfg.AutoKeyboard = !cfg.AutoKeyboard; cfg.Save(); } });
            items.Add(new MenuEntry { Label = delegate { return "Bildschirmtastatur anzeigen"; }, Run = delegate { keyboard.Toggle(false); } });
            items.Add(new MenuEntry { Label = delegate { return "Fast Type (Abk\u00FCrzungen)"; }, State = delegate { return expander.Enabled ? "AN" : "AUS"; }, KeepOpen = true, Run = ToggleFastType });
            items.Add(new MenuEntry { Label = delegate { return "Peach-Leiste"; }, State = delegate { return cfg.Bar.Enabled ? "AN" : "AUS"; }, KeepOpen = true, Run = delegate { cfg.Bar.Enabled = !cfg.Bar.Enabled; cfg.Save(); ApplyBar(); } });
            items.Add(new MenuEntry { Label = delegate { return "Game-Boost"; }, State = delegate { return booster.Active ? "AN" : "AUS"; }, KeepOpen = true, Run = delegate { if (booster.Active) booster.Restore(); else booster.Boost(null, false); } });
            items.Add(new MenuEntry { Label = delegate { return "Game-Boost öffnen (CPU/GPU)"; }, Run = OpenGameBoost });
            items.Add(new MenuEntry { Label = delegate { return "Minecraft (Profile, Mods, Widgets)"; }, Run = OpenMinecraft });
            items.Add(new MenuEntry { Label = delegate { return "Discord"; }, State = delegate { return DiscordLauncher.IsOpen ? "AN" : null; }, Run = OpenDiscord });
            items.Add(new MenuEntry { Label = delegate { return "Mit Windows starten"; }, State = delegate { return AutostartOn() ? "AN" : "AUS"; }, KeepOpen = true, Run = ToggleAutostart });
            items.Add(new MenuEntry { Label = delegate { return "Beenden"; }, Run = Quit });
            menu = new MenuForm(items);

            keyboard = new OnScreenKeyboard();
            bar = new BarForm(cfg, LaunchApp, ShowMenu, OpenBarEditor, delegate { cfg.Bar.Enabled = false; cfg.Save(); ApplyBar(); });
            expander = new TextExpander(cfg);
            pad = new GamepadMouse(cfg.MouseEnabled, cfg.MouseSpeed);
            // controller mode follows the controller: it pauses when the pad is unplugged and comes back when it returns
            pad.ConnectionChanged += delegate (bool connected) { sync.BeginInvoke(new Action(delegate { OnPadConnection(connected); })); };
            pad.KeyboardOpen = delegate { return keyboard.IsOpen; };
            pad.KeyboardInput = keyboard.Input;
            pad.KeyboardToggle = delegate { sync.BeginInvoke(new Action(delegate { keyboard.Toggle(false); })); };
            pad.LauncherOpen = delegate { return launcher.IsFront; };   // pad navigates tiles only while the launcher is in front
            pad.LauncherInput = launcher.Input;
            pad.HomeRequested = delegate { sync.BeginInvoke(new Action(HomePressed)); };
            focusHandler = OnFocusChanged;
            try { Automation.AddAutomationFocusChangedEventHandler(focusHandler); } catch { /* UI Automation unavailable: manual keyboard still works */ }
            pad.Toggled += delegate (bool on)
            {
                sync.BeginInvoke(new Action(delegate
                {
                    cfg.MouseEnabled = on; cfg.Save();
                    Balloon("Controller-Maus", on ? "an (Back + Start zum Ausschalten)" : "aus");
                }));
            };

            hotkeys = new HotkeyManager();
            hotkeys.MenuRequested += ShowMenu;
            bool menuOk = hotkeys.RegisterMenuHotkey();
            ApplyHotkeys();
            ApplyBar();
            if (cfg.AddonHostAutostart && addonHost.CanRun) addonHost.StartHost();

            wait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { sync.BeginInvoke(new Action(ShowMenu)); }, null, -1, false);

            if (!menuOk) Balloon("Strg+Alt+P ist belegt", "Ein anderes Programm nutzt die Kombination. Nutze das Tray-Symbol.");
            else if (!cfg.ControllerMode) Balloon("Peach läuft", "Strg+Alt+P öffnet das Menü.");
            if (!expander.Active) Balloon("Fast Type nicht verfügbar", "Der Tastatur-Hook konnte nicht installiert werden.");

            // Controller mode left on: the launcher comes up as soon as the controller is detected
            // (OnPadConnection), console-style. Without a controller the normal desktop stays untouched.

            if (testHooks)
            {
                // `--test-hooks` (used by test\exe-test.ps1 only): pretend to plug/unplug a controller
                EventWaitHandle fake = new EventWaitHandle(false, EventResetMode.AutoReset, "Peach.Client.TestPad");
                testWait = ThreadPool.RegisterWaitForSingleObject(fake, delegate { sync.BeginInvoke(new Action(delegate { pad.SetConnected(!pad.Connected); })); }, null, -1, false);
            }
        }

        RegisteredWaitHandle testWait;

        // Controller plugged in / unplugged (UI thread).
        void OnPadConnection(bool connected)
        {
            ApplyPadState();
            ApplyBar();
            if (!cfg.ControllerMode) return;
            if (connected)
            {
                if (keyboard.IsOpen) keyboard.Close();
                launcher.ShowLauncher();
                Balloon("Controller verbunden", "Controller-Modus ist aktiv.");
            }
            else
            {
                launcher.HideLauncher();
                Balloon("Controller getrennt", "Controller-Modus pausiert, bis wieder ein Controller verbunden ist.");
            }
        }

        // Controller mode = launcher UI + controller mouse that cannot be switched off by accident.
        void SetMode(bool on)
        {
            if (on && !pad.Connected)
            {
                Balloon("Kein Controller verbunden", "Der Controller-Modus geht nur mit angeschlossenem Controller.");
                return;
            }
            if (on) AskAboutGameBar();
            cfg.ControllerMode = on;
            cfg.Save();
            ApplyPadState();
            ApplyBar();
            if (on)
            {
                if (keyboard.IsOpen) keyboard.Close();
                launcher.ShowLauncher();
            }
            else
            {
                launcher.HideLauncher();
                Balloon("Controller-Modus aus", "Zum Einschalten: Menü (Strg+Alt+P) oder Home-Taste / Back + Y am Controller.");
            }
        }

        // Windows would open the Game Bar on every Xbox-button press. Ask once; the user decides.
        void AskAboutGameBar()
        {
            if (cfg.GameBarAsked || !GameBar.ControllerButtonOpensGameBar(null)) return;
            cfg.GameBarAsked = true;
            cfg.Save();
            bool yes = Dialogs.Ask(null, "Xbox-Taste",
                "Unter Windows \u00F6ffnet die Xbox-Taste am Controller die Game Bar. Peach nutzt sie als Home-Taste f\u00FCr das Controller-Men\u00FC.\n\nSoll Peach diese Windows-Einstellung ausschalten, damit nur das Peach-Men\u00FC kommt?\n\n(Jederzeit wieder \u00E4nderbar: Addons \u2192 Controller \u2192 Maus \u2192 Anpassen.)",
                "Ja, ausschalten", "Nein, lassen");
            if (yes) GameBar.SetControllerButtonOpensGameBar(false, null);
        }

        void ToggleGameBar()
        {
            bool opens = GameBar.ControllerButtonOpensGameBar(null);
            if (!GameBar.SetControllerButtonOpensGameBar(!opens, null))
                Balloon("Einstellung nicht geändert", "Windows hat den Zugriff verweigert.");
        }

        void ApplyPadState()
        {
            bool active = cfg.ControllerMode && pad.Connected;   // paused mode must not keep the pad locked
            pad.Locked = active;
            pad.Enabled = cfg.MouseEnabled || active;
        }

        // Home (Xbox button / Back + Y): switch controller mode on, or bring the launcher in front of the running app.
        void HomePressed()
        {
            if (!cfg.ControllerMode) { SetMode(true); return; }
            if (keyboard.IsOpen) keyboard.Close();
            launcher.BringFront();
        }

        // The launcher stays open behind the app; the app simply opens in front of it.
        void LaunchApp(AppEntry a)
        {
            if (HandlePeachLink(a.Path)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(a.Path);
                if (!string.IsNullOrEmpty(a.Args)) psi.Arguments = a.Args;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Balloon("App konnte nicht starten", a.Name + ": " + ex.Message);
            }
        }

        // Runs on a UI Automation thread: pop the keyboard up when a (writable) text field gets focus
        // and the controller was used recently; hide it again if it opened itself and focus moves away.
        void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            try
            {
                AutomationElement el = sender as AutomationElement;
                if (el == null || el.Current.ProcessId == ownPid) return;
                bool edit = el.Current.ControlType == ControlType.Edit;
                if (edit)
                {
                    object pattern;
                    if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) && ((ValuePattern)pattern).Current.IsReadOnly) edit = false;
                }
                if (edit)
                {
                    if (cfg.AutoKeyboard && pad.Enabled && Environment.TickCount - pad.LastActivity < 20000 && !keyboard.IsOpen && !launcher.IsFront)
                        sync.BeginInvoke(new Action(delegate { keyboard.Open(true); }));
                }
                else if (keyboard.AutoShown)
                {
                    sync.BeginInvoke(new Action(delegate { if (keyboard.AutoShown) keyboard.Close(); }));
                }
            }
            catch { /* element vanished while reading it */ }
        }

        void Balloon(string title, string text)
        {
            tray.ShowBalloonTip(3500, title, text, ToolTipIcon.Info);
        }

        void ShowMenu() { menu.Toggle(); }

        // The bar is for normal desktop mode: it hides while controller mode is active with a controller connected.
        void ApplyBar()
        {
            if (BarLogic.ShouldShow(cfg.Bar.Enabled, cfg.ControllerMode, pad.Connected)) bar.ShowBar();
            else bar.HideBar();
        }

        void OpenAddons()
        {
            if (addons == null) addons = new AddonsForm(cfg, addonHost, Builtins());
            addons.ShowAndFocus();
        }

        void OpenControllerSettings()
        {
            using (ControllerSettingsForm f = new ControllerSettingsForm(cfg, delegate { return pad.Connected; }, delegate (int v) { pad.Speed = v; }, ApplyPadState,
                delegate { using (AppEditor ed = new AppEditor(cfg, delegate { })) ed.ShowDialog(); }))
                f.ShowDialog();
        }

        // The features built into peach.exe, as cards in the Addons app.
        List<BuiltinAddon> Builtins()
        {
            List<BuiltinAddon> l = new List<BuiltinAddon>();
            l.Add(new BuiltinAddon { Name = "Controller-Modus", Letter = "C", Tint = Color.FromArgb(255, 122, 89),
                Description = "Vollbild-Oberfl\u00E4che f\u00FCr deine Apps \u2013 nur mit angeschlossenem Controller.", Chips = new string[] { "Nur mit Controller", "Home-Taste", "Apps frei w\u00E4hlbar" },
                IsOn = delegate { return cfg.ControllerMode; }, SetOn = delegate (bool v) { SetMode(v); },
                Customize = delegate { using (AppEditor ed = new AppEditor(cfg, delegate { })) ed.ShowDialog(); } });
            l.Add(new BuiltinAddon { Name = "Controller \u2192 Maus", Letter = "M", Tint = Color.FromArgb(240, 160, 90), Chips = new string[] { "Zeiger", "Klicks", "Scrollen", "Xbox-Taste" },
                Description = "Zeiger, Klicks und Scrollen mit dem Controller. Geschwindigkeit, Game Bar und Tastenbelegung unter \u201EAnpassen\u201C.",
                IsOn = delegate { return cfg.MouseEnabled; }, SetOn = delegate (bool v) { cfg.MouseEnabled = v; cfg.Save(); ApplyPadState(); },
                Customize = OpenControllerSettings });
            l.Add(new BuiltinAddon { Name = "Bildschirmtastatur", Letter = "T", Tint = Color.FromArgb(120, 170, 120), Chips = new string[] { "Controller", "Y-Taste", "Automatisch" },
                Description = "Erscheint bei Textfeldern, wenn du mit dem Controller klickst. Mit Y jederzeit von Hand.",
                IsOn = delegate { return cfg.AutoKeyboard; }, SetOn = delegate (bool v) { cfg.AutoKeyboard = v; cfg.Save(); },
                Customize = OpenControllerSettings });
            l.Add(new BuiltinAddon { Name = "Fast Type", Letter = "F", Tint = Color.FromArgb(120, 150, 200), Chips = new string[] { "Textkürzel", "{date} / {time}" },
                Description = "Abk\u00FCrzung tippen, sofort wird der volle Text eingesetzt (z. B. ;mfg).",
                IsOn = delegate { return expander.Enabled; }, SetOn = delegate (bool v) { expander.Enabled = v; cfg.FastTypeEnabled = v; cfg.Save(); },
                Customize = delegate { using (SnippetEditor ed = new SnippetEditor(cfg, delegate { })) ed.ShowDialog(); } });
            l.Add(new BuiltinAddon { Name = "Hotkeys", Letter = "H", Tint = Color.FromArgb(170, 130, 190), Chips = new string[] { "Programme", "URLs", "Text tippen" },
                Description = "Eigene Tastenk\u00FCrzel: Programme starten, URLs \u00F6ffnen oder Text tippen. Strg + Alt + P bleibt das Peach-Men\u00FC.",
                IsOn = delegate { return true; },
                Customize = delegate { using (HotkeyEditor ed = new HotkeyEditor(cfg, ApplyHotkeys)) ed.ShowDialog(); } });
            l.Add(new BuiltinAddon { Name = "Peach-Leiste", Letter = "L", Tint = Color.FromArgb(214, 96, 110), Chips = new string[] { "Oben / Unten", "Farben", "Fenster", "Uhr" },
                Description = "Deine eigene Leiste am Bildschirmrand mit Apps, Fenstern und Uhr. Farben, Gr\u00F6\u00DFe und Inhalt frei w\u00E4hlbar.",
                IsOn = delegate { return cfg.Bar.Enabled; }, SetOn = delegate (bool v) { cfg.Bar.Enabled = v; cfg.Save(); ApplyBar(); },
                Customize = OpenBarEditor });
            l.Add(new BuiltinAddon { Name = "Game-Boost", Letter = "G", Tint = Color.FromArgb(86, 196, 128), CustomizeLabel = "Öffnen", Chips = new string[] { "CPU & GPU live", "Energieplan", "Priorität", "Apps schließen" },
                Description = "Zeigt CPU, GPU und RAM live und holt beim Spielen mehr heraus: Höchstleistung, hohe Priorität, Hintergrund-Apps schließen – danach wird alles zurückgestellt. Automatisch bei deinen Spielen.",
                IsOn = delegate { return cfg.GameBoost.Auto; }, SetOn = delegate (bool v) { cfg.GameBoost.Auto = v; cfg.Save(); },
                Customize = OpenGameBoost });
            l.Add(new BuiltinAddon { Name = "Minecraft", Letter = "M", Tint = Color.FromArgb(110, 180, 90), CustomizeLabel = "Öffnen", Chips = new string[] { "Profile", "Fabric-Mods", "Modrinth", "Widgets", "Spotify" },
                Description = "Minecraft-Profile mit eigenen Mods (Fabric, Modrinth-Suche) und Widgets über dem Spiel: Spotify, CPU/GPU/RAM, Uhr, Spielzeit – mit jeder Version, ohne Mod. Gestartet wird über den offiziellen Launcher.",
                IsOn = delegate { return cfg.Minecraft.OverlayEnabled; }, SetOn = delegate (bool v) { cfg.Minecraft.OverlayEnabled = v; cfg.Save(); ApplyOverlay(); },
                Customize = OpenMinecraft });
            l.Add(new BuiltinAddon { Name = "Discord", Letter = "D", Tint = Color.FromArgb(88, 101, 242), CustomizeLabel = "Einstellungen", Chips = new string[] { "Offizielles Discord", "Themes & CSS", "Befehle", "Skripte" },
                Description = "Das echte Discord in einem Peach-Fenster mit eigenen Themes, CSS, Chat-Befehlen und Skripten. Du meldest dich wie im Browser an, Peach sieht deine Zugangsdaten nie.",
                IsOn = delegate { return DiscordLauncher.IsOpen; },
                SetOn = delegate (bool v) { if (v) OpenDiscord(); else DiscordLauncher.Stop(); },
                Customize = delegate { DiscordLauncher.OpenSettings(cfg); } });
            return l;
        }

        void OpenDiscord() { DiscordLauncher.Open(cfg); }

        void ApplyOverlay()
        {
            if (cfg.Minecraft.OverlayEnabled) overlay.Start(); else overlay.Stop();
        }

        void OpenMinecraft()
        {
            if (mcForm == null || mcForm.IsDisposed)
            {
                mcForm = new MinecraftForm(cfg, ApplyOverlay);
                mcForm.FormClosed += delegate { mcForm = null; };
            }
            if (!mcForm.Visible) mcForm.Show();
            if (mcForm.WindowState == FormWindowState.Minimized) mcForm.WindowState = FormWindowState.Normal;
            mcForm.Activate();
        }

        void OpenGameBoost()
        {
            if (boostForm == null || boostForm.IsDisposed)
            {
                boostForm = new GameBoostForm(cfg, booster);
                boostForm.FormClosed += delegate { boostForm = null; };
            }
            if (!boostForm.Visible) boostForm.Show();
            if (boostForm.WindowState == FormWindowState.Minimized) boostForm.WindowState = FormWindowState.Normal;
            boostForm.Activate();
        }

        // "peach:discord" / "peach:addons" / "peach:menu" in app lists (bar, launcher) open Peach itself.
        bool HandlePeachLink(string path)
        {
            string what = PeachLinks.Parse(path);
            if (what == null) return false;
            if (what == "discord") OpenDiscord();
            else if (what == "addons") OpenAddons();
            else if (what == "menu") ShowMenu();
            else Balloon("Unbekannter Peach-Link", path);
            return true;
        }

        void OpenBarEditor()
        {
            using (BarEditor ed = new BarEditor(cfg, ApplyBar)) ed.ShowDialog();
        }

        void ApplyHotkeys()
        {
            List<string> failed = hotkeys.Reload(cfg.Hotkeys);
            if (failed.Count > 0) Balloon("Hotkey nicht verfügbar", string.Join(", ", failed.ToArray()) + " (schon belegt?)");
        }

        void ToggleMouse()
        {
            cfg.MouseEnabled = !cfg.MouseEnabled;
            cfg.Save();
            ApplyPadState();   // stays on while controller mode is on
        }

        void ToggleFastType()
        {
            expander.Enabled = !expander.Enabled;
            cfg.FastTypeEnabled = expander.Enabled; cfg.Save();
        }

        static string ExePath() { return Process.GetCurrentProcess().MainModule.FileName; }

        static bool AutostartOn()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("PeachClient") != null;
        }

        void ToggleAutostart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (AutostartOn()) k.DeleteValue("PeachClient", false);
                else k.SetValue("PeachClient", "\"" + ExePath() + "\"");
            }
        }

void Quit()
        {
            try { wait.Unregister(null); } catch { }
            try { if (testWait != null) testWait.Unregister(null); } catch { }
            try { Automation.RemoveAllEventHandlers(); } catch { }
            bar.Dispose();          // also hands the reserved screen space back
            DiscordLauncher.Dispose();
            booster.Dispose();       // puts the power plan and priorities back
            if (boostForm != null && !boostForm.IsDisposed) boostForm.Dispose();
            if (mcForm != null && !mcForm.IsDisposed) mcForm.Dispose();
            overlay.Dispose();
            launcher.Dispose();
            keyboard.Dispose();
            pad.Dispose();
            expander.Dispose();
            hotkeys.Dispose();
            if (addons != null) addons.Dispose();
            addonHost.Dispose();      // stops the script addons it started
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            ExitThread();
        }
    }
}
