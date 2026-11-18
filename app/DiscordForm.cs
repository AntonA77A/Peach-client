#if WEBVIEW2
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Peach
{
    // The official Discord web app (discord.com/app) in a Peach window, with Vencord-style extras.
    //  - own browser profile in %APPDATA%\PeachClient\webview2\discord: you stay signed in, nothing else shares it
    //  - only Discord pages load here; other links open in your normal browser
    //  - microphone / camera / notifications / paste are granted to Discord only
    //  - themes, accent, font, zoom, custom CSS, chat commands and opt-in scripts (see DiscordBridge / DiscordSettings)
    //  - the X hides the window (a voice call keeps running); "Beenden" ends the session
    internal class DiscordForm : PeachForm
    {
        const int ToolH = 52;
        readonly WebView2 web = new WebView2();
        readonly string profile, startUrl;
        readonly Config cfg;
        readonly Label problem;
        readonly PeachButton problemButton;
        readonly List<string> scriptIds = new List<string>();
        bool reallyClose, injecting, injectAgain;
        public bool PageLoaded;      // a real page (not about:blank) finished loading
        public bool TestMode;        // tests/previews: also allow the data: page they load first, and run the bridge on any page

        // How non-Discord links leave the window (replaceable in tests).
        public static Action<string> OpenExternal = delegate (string url)
        {
            if (!DiscordNav.IsSafeExternal(url)) return;
            try { Process.Start(new Uri(url).AbsoluteUri); } catch { }
        };

        // How an "open" command starts its target (replaceable in tests): (program / file / URL, fixed arguments).
        public static Action<string, string> RunOpen = delegate (string target, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(target);
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        };

        // Messages the page sends to Peach: (type, value). Tests listen here.
        public static Action<string, string> HostCommand;

        public DiscordForm(string profileDir, string startUrl, Config cfg) : base("Discord", 1320, 840)
        {
            this.profile = profileDir; this.startUrl = startUrl; this.cfg = cfg;
            Resizable = true; ShowInTaskbar = true; EscCloses = false;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1320, wa.Width - 60), Math.Min(840, wa.Height - 60));
            MinimumSize = new Size(760, 520);

            int x = 16;
            x = Tool("←", 44, x, BtnStyle.Secondary, delegate { if (web.CoreWebView2 != null && web.CanGoBack) web.GoBack(); });
            x = Tool("Neu laden", 112, x, BtnStyle.Secondary, delegate { if (web.CoreWebView2 != null) web.Reload(); });
            x = Tool("Einstellungen", 138, x, BtnStyle.Primary, delegate { OpenSettings(); });
            x = Tool("Im Browser öffnen", 170, x, BtnStyle.Secondary, delegate { if (web.CoreWebView2 != null) OpenExternal(web.Source == null ? "https://discord.com/app" : web.Source.AbsoluteUri); });

            PeachButton end = new PeachButton("Beenden", BtnStyle.Secondary, 110, 34); end.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            end.Location = new Point(ClientSize.Width - 16 - 110, TitleH + 9);
            end.Click += delegate { EndSession(); };
            PeachButton clear = new PeachButton("Abmelden & Daten löschen", BtnStyle.Danger, 210, 34); clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Location = new Point(ClientSize.Width - 16 - 110 - 10 - 210, TitleH + 9);
            clear.Click += delegate { SignOutAndClear(); };
            Controls.Add(end); Controls.Add(clear);

            web.Location = new Point(0, TitleH + ToolH);
            web.Size = new Size(ClientSize.Width, ClientSize.Height - TitleH - ToolH);
            web.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            web.DefaultBackgroundColor = Theme.Bg;
            Controls.Add(web);

            problem = AddLabel("", 60, TitleH + ToolH + 80, ClientSize.Width - 120, 120, false, 11f, false);
            problem.Visible = false; problem.TextAlign = ContentAlignment.TopCenter; problem.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            problemButton = new PeachButton("WebView2-Runtime laden (Microsoft)", BtnStyle.Primary, 340, 44);
            problemButton.Location = new Point((ClientSize.Width - 340) / 2, TitleH + ToolH + 210); problemButton.Visible = false;
            problemButton.Anchor = AnchorStyles.Top;
            problemButton.Click += delegate { OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/"); };
            Controls.Add(problemButton);
            problem.BringToFront(); problemButton.BringToFront();

            Shown += delegate { StartWeb(); };
        }

        int Tool(string text, int w, int x, BtnStyle style, EventHandler click)
        {
            PeachButton b = new PeachButton(text, style, w, 34);
            b.Location = new Point(x, TitleH + 9); b.Click += click;
            Controls.Add(b);
            return x + w + 8;
        }

        // ---- web view -------------------------------------------------------------------------------------
        public bool WebReady { get { return web.CoreWebView2 != null; } }
        public WebView2 Web { get { return web; } }
        bool started;

        void StartWeb()
        {
            if (started) return;
            started = true;
            if (WebView2Setup.RuntimeVersion() == null) { ShowProblem("Die Microsoft Edge WebView2-Runtime ist nicht installiert.\nSie wird für Discord in Peach gebraucht (bei Windows 11 ist sie normalerweise dabei)."); return; }
            try
            {
                Directory.CreateDirectory(profile);
                web.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
                web.CoreWebView2InitializationCompleted += delegate (object s, CoreWebView2InitializationCompletedEventArgs e)
                {
                    if (!e.IsSuccess) { ShowProblem("Der eingebettete Browser konnte nicht starten:\n" + (e.InitializationException == null ? "" : e.InitializationException.Message)); return; }
                    Wire();
                    FirstNavigation();
                };
                web.EnsureCoreWebView2Async(null);
            }
            catch (Exception ex) { ShowProblem("Der eingebettete Browser konnte nicht starten:\n" + ex.Message); }
        }

        // settings must be registered before the first page is created, so the very first load already has them
        async void FirstNavigation()
        {
            try { web.ZoomFactor = Zoom(); await Inject(); } catch { }
            if (WebReady) web.CoreWebView2.Navigate(startUrl);
        }

        double Zoom() { return Math.Max(0.6, Math.Min(1.6, cfg.Discord.Zoom / 100.0)); }

        void ShowProblem(string text)
        {
            problem.Text = text; problem.Visible = true; problem.BringToFront();
            problemButton.Visible = WebView2Setup.RuntimeVersion() == null; problemButton.BringToFront();
        }

        void Wire()
        {
            CoreWebView2 c = web.CoreWebView2;
            c.Settings.AreDevToolsEnabled = false;
            c.Settings.IsStatusBarEnabled = false;
            c.Settings.IsPasswordAutosaveEnabled = false;
            c.Settings.IsGeneralAutofillEnabled = false;

            c.NavigationStarting += delegate (object s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (DiscordNav.IsAllowed(e.Uri) || (TestMode && e.Uri.StartsWith("data:", StringComparison.Ordinal))) return;
                e.Cancel = true;
                OpenExternal(e.Uri);
            };
            c.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;                                    // never open extra browser windows inside Peach
                if (DiscordNav.IsAllowed(e.Uri)) c.Navigate(e.Uri); else OpenExternal(e.Uri);
            };
            c.PermissionRequested += delegate (object s, CoreWebView2PermissionRequestedEventArgs e)
            {
                e.State = DiscordNav.AllowPermission(e.PermissionKind.ToString(), e.Uri) ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            };
            c.WebMessageReceived += delegate (object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                if (!TestMode && !DiscordNav.IsDiscordPage(e.Source)) return;     // only Discord's own page may talk to Peach
                Dictionary<string, object> m;
                try { m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
                if (m == null) return;
                string type = m.ContainsKey("type") ? m["type"] as string : null, value = m.ContainsKey("value") ? m["value"] as string : null;
                Action<string, string> h = HostCommand; if (h != null) h(type, value);
                if (type == "settings" && !TestMode) BeginInvoke(new Action(OpenSettings));
                else if (type == "theme") SetThemeFromPage(value);
                else if (type == "run")
                {
                    // only commands the user defined can run; the page only names one and may add text for URLs
                    string name = m.ContainsKey("name") ? m["name"] as string : null, args = m.ContainsKey("args") ? m["args"] as string : "";
                    foreach (CommandEntry ce in cfg.Discord.Commands)
                    {
                        string target, arguments;
                        if (ce != null && name != null && string.Equals(ce.Name, name, StringComparison.OrdinalIgnoreCase) && DiscordBridge.ResolveOpen(ce, args, out target, out arguments))
                        { RunOpen(target, arguments); break; }
                    }
                }
            };
            c.NavigationCompleted += delegate { if (web.Source != null && web.Source.AbsoluteUri != "about:blank") PageLoaded = true; };
            c.DocumentTitleChanged += delegate
            {
                Match m = Regex.Match(c.DocumentTitle ?? "", @"^\((\d+)\)");
                SetTitle(m.Success ? "Discord  ·  " + m.Groups[1].Value + " ungelesen" : "Discord");
            };
        }

        // ---- Peach extras: settings, themes, commands, scripts ----------------------------------------------
        // Registers the bridge and the enabled scripts so every new Discord page gets them first thing.
        public async Task Inject()
        {
            if (!WebReady) return;
            if (injecting) { injectAgain = true; return; }
            injecting = true;
            try
            {
                do
                {
                    injectAgain = false;
                    CoreWebView2 c = web.CoreWebView2;
                    foreach (string id in scriptIds) c.RemoveScriptToExecuteOnDocumentCreated(id);
                    scriptIds.Clear();
                    scriptIds.Add(await c.AddScriptToExecuteOnDocumentCreatedAsync(DiscordBridge.Bridge(cfg.Discord, TestMode)));
                    foreach (KeyValuePair<string, string> s in DiscordScripts.Enabled(cfg))
                        scriptIds.Add(await c.AddScriptToExecuteOnDocumentCreatedAsync(DiscordBridge.WrapUserScript(s.Key, s.Value)));
                } while (injectAgain);
            }
            finally { injecting = false; }
        }

        // Settings changed: the open page updates right away; script changes need a reload of the page.
        public async Task ApplySettingsAsync(bool scriptsChanged)
        {
            if (!WebReady) return;
            try
            {
                web.ZoomFactor = Zoom();
                await web.CoreWebView2.ExecuteScriptAsync("window.__peach && window.__peach.update(" + DiscordBridge.SettingsJson(cfg.Discord) + ")");
                await Inject();                       // pages created later (reload, new tab) start with the new settings
                if (scriptsChanged) web.Reload();
            }
            catch { }
        }

        public void ApplySettings(bool scriptsChanged) { Task t = ApplySettingsAsync(scriptsChanged); }

        public void OpenSettings()
        {
            using (DiscordSettingsForm f = new DiscordSettingsForm(cfg, delegate (bool scripts) { ApplySettings(scripts); })) f.ShowDialog(this);
        }

        void SetThemeFromPage(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant();
            if (key == "standard") key = "";
            string say;
            if (key.Length == 0 || DiscordTheme.Find(key) != null)
            {
                cfg.Discord.Theme = key; cfg.Save();
                say = key.Length == 0 ? "Theme: Standard" : "Theme: " + DiscordTheme.Find(key).Label;
                ApplySettings(false);
            }
            else say = "Unbekanntes Theme. Möglich: standard, peach, mitternacht, wald, amoled, violett";
            if (WebReady) { Task t = web.CoreWebView2.ExecuteScriptAsync("window.__peach && window.__peach.toast(" + new JavaScriptSerializer().Serialize(say) + ")"); }
        }

        // ---- actions --------------------------------------------------------------------------------------
        public void ShowAndFocus()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Native.ForceForeground(Handle);
            Activate();
            if (WebReady && (web.Source == null || web.Source.AbsoluteUri == "about:blank")) web.CoreWebView2.Navigate(startUrl);
        }

        // leaves voice, stops the page, hides the window
        public void EndSession()
        {
            if (WebReady) web.CoreWebView2.Navigate("about:blank");
            Hide();
        }

        void SignOutAndClear()
        {
            if (!Dialogs.Ask(this, "Abmelden & Daten löschen", "Damit wirst du bei Discord in Peach abgemeldet und alle Browserdaten dieses Fensters (Cookies, Zwischenspeicher) werden gelöscht.\n\nDein Discord-Konto selbst bleibt unverändert.", "Löschen", "Abbrechen", true)) return;
            if (!WebReady) return;
            try { web.CoreWebView2.Profile.ClearBrowsingDataAsync().ContinueWith(delegate (Task t) { try { BeginInvoke(new Action(delegate { if (WebReady) web.CoreWebView2.Navigate(startUrl); })); } catch { } }); }
            catch { web.CoreWebView2.CookieManager.DeleteAllCookies(); web.CoreWebView2.Navigate(startUrl); }
        }

        public void ForceClose() { reallyClose = true; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }   // calls keep running
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { web.Dispose(); } catch { } }
            base.Dispose(disposing);
        }

        // ---- helpers for tests and previews (they have no message loop of their own) ------------------------
        public static bool Pump(Func<bool> done, int timeoutMs)
        {
            DateTime end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                if (done()) return true;
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
            }
            return done();
        }

        // Screenshot of the page itself (a WebView2 is its own window and is missing from DrawToBitmap).
        public Bitmap CapturePage(int timeoutMs)
        {
            if (!WebReady) return null;
            using (MemoryStream ms = new MemoryStream())
            {
                Task t = web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                if (!Pump(delegate { return t.IsCompleted; }, timeoutMs)) return null;
                ms.Position = 0;
                using (Bitmap raw = new Bitmap(ms)) return new Bitmap(raw);
            }
        }

        // Runs JavaScript in the page and returns its result as text ("" if it did not answer in time).
        public string Eval(string js, int timeoutMs)
        {
            if (!WebReady) return "";
            Task<string> t = web.CoreWebView2.ExecuteScriptAsync(js);
            Pump(delegate { return t.IsCompleted; }, timeoutMs);
            return t.IsCompleted ? t.Result : "";
        }
    }
}
#endif
