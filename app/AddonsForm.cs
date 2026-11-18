using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Peach
{
    // A feature built into peach.exe that shows up as a card in the Addons app.
    internal class BuiltinAddon
    {
        public string Name = "", Description = "", Letter = "";
        public Color Tint = Theme.Accent;
        public Func<bool> IsOn;
        public Action<bool> SetOn;
        public string CustomizeLabel = "Anpassen";
        public Action Customize;          // "Anpassen" button, optional
        public string[] Chips = new string[0];
    }

    // Turns "built-in features + script addons from the host" into list rows (pure, unit-tested).
    internal static class AddonRows
    {
        public const string ActCustomize = "customize", ActLog = "log", ActRemove = "remove", ActStartHost = "starthost", ActEdit = "edit";

        public static Color LanguageColor(string entry)
        {
            string ext = (Path.GetExtension(entry ?? "") ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".js": case ".mjs": return Color.FromArgb(240, 200, 80);
                case ".ts": return Color.FromArgb(91, 155, 224);
                case ".py": return Color.FromArgb(80, 170, 140);
                case ".ps1": return Color.FromArgb(96, 130, 214);
                case ".bat": case ".cmd": return Color.FromArgb(150, 150, 150);
                case ".vbs": return Color.FromArgb(176, 128, 196);
                default: return Theme.Accent;
            }
        }

        public static string LanguageLetters(string entry)
        {
            string ext = (Path.GetExtension(entry ?? "") ?? "").TrimStart('.').ToUpperInvariant();
            return ext.Length == 0 ? "?" : (ext.Length > 3 ? ext.Substring(0, 3) : ext);
        }

        // filter: 0 = all, 1 = built-in, 2 = script addons
        public static List<RowItem> Build(List<BuiltinAddon> builtins, List<AddonInfo> addons, bool hostConnected, string hostMessage, bool canStartHost, int filter)
        {
            List<RowItem> rows = new List<RowItem>();
            if (filter != 2)
            {
                RowItem h = new RowItem(); h.IsHeader = true; h.Title = "Eingebaut";
                rows.Add(h);
                foreach (BuiltinAddon b in builtins)
                {
                    bool on = b.IsOn();
                    RowItem r = new RowItem();
                    r.Title = b.Name; r.Subtitle = b.Description; r.Letter = b.Letter; r.Tint = b.Tint;
                    r.HasToggle = b.SetOn != null; r.On = on;
                    r.Status = on ? "an" : "aus"; r.StatusColor = on ? Theme.Ok : Theme.Muted;
                    foreach (string chip in b.Chips) r.Chips.Add(chip);
                    if (b.Customize != null) r.Actions.Add(new RowAction(ActCustomize, b.CustomizeLabel, false));
                    r.Tag = b;
                    rows.Add(r);
                }
            }
            if (filter != 1)
            {
                RowItem h = new RowItem(); h.IsHeader = true; h.Title = "Script-Addons  (JavaScript, Python, PowerShell …)";
                rows.Add(h);
                if (!hostConnected)
                {
                    RowItem r = new RowItem();
                    r.Title = "Addon-Host nicht verbunden"; r.Subtitle = hostMessage; r.Letter = "!"; r.Tint = Theme.Muted;
                    if (canStartHost) r.Actions.Add(new RowAction(ActStartHost, "Host starten", false));
                    rows.Add(r);
                }
                else if (addons.Count == 0)
                {
                    RowItem r = new RowItem();
                    r.Title = "Noch keine Script-Addons"; r.Subtitle = "Mit „Neues Addon“ legst du eins an, oder „Installieren“ für ein fertiges (ZIP oder Ordner)."; r.Letter = "+"; r.Tint = Theme.Muted;
                    rows.Add(r);
                }
                else
                {
                    foreach (AddonInfo a in addons)
                    {
                        RowItem r = new RowItem();
                        bool running = a.Status == "running";
                        r.Title = a.Name; r.Tint = LanguageColor(a.Entry); r.Letter = LanguageLetters(a.Entry);
                        r.Subtitle = a.Description.Length > 0 ? a.Description : (a.Author.Length > 0 ? "von " + a.Author : a.Entry);
                        r.HasToggle = true; r.On = running;
                        r.Status = running ? "läuft" : (a.Status == "crashed" ? "abgestürzt" : (a.Status == "stopping" ? "stoppt …" : "gestoppt"));
                        r.StatusColor = running ? Theme.Ok : (a.Status == "crashed" ? Theme.Danger : Theme.Muted);
                        if (a.Language.Length > 0) r.Chips.Add(a.Language);
                        if (a.Version.Length > 0) r.Chips.Add("v" + a.Version);
                        if (a.Permissions.Count == 0) r.Chips.Add("keine Rechte");
                        foreach (string p in a.Permissions) r.Chips.Add(p);
                        r.Actions.Add(new RowAction(ActEdit, "Bearbeiten", false));
                        r.Actions.Add(new RowAction(ActLog, "Log", false));
                        r.Actions.Add(new RowAction(ActRemove, "Entfernen", true));
                        r.Tag = a;
                        rows.Add(r);
                    }
                }
            }
            return rows;
        }

        // Cheap fingerprint so the list is only rebuilt when something visible changed.
        public static string Signature(List<RowItem> rows)
        {
            StringBuilder sb = new StringBuilder();
            foreach (RowItem r in rows) sb.Append(r.Title).Append('|').Append(r.Status).Append('|').Append(r.On).Append('|').Append(r.Subtitle).Append('|').Append(r.Chips.Count).Append(';');
            return sb.ToString();
        }
    }

    // The Peach Addons app: built-in features and script addons in one place.
    internal class AddonsForm : PeachForm
    {
        readonly Config cfg;
        readonly AddonHost host;
        readonly List<BuiltinAddon> builtins;
        readonly PeachList list = new PeachList();
        readonly PeachSegmented filter;
        readonly Label hostLabel;
        readonly Timer timer = new Timer();
        readonly PeachToggle hostAuto;
        string signature = "";
        bool refreshing;
        public bool PreviewMode;     // --render-ui: show the sample data as given, never touch the real host

        public AddonsForm(Config cfg, AddonHost host, List<BuiltinAddon> builtins) : base("Peach Addons", 980, 760)
        {
            this.cfg = cfg; this.host = host; this.builtins = builtins;
            ShowInTaskbar = true;
            MinimizeBox = false;
            MinimumSize = Size;

            filter = new PeachSegmented(new string[] { "Alle", "Eingebaut", "Script-Addons" }, 0, 360);
            filter.Location = new Point(28, TitleH + 18);
            filter.SelectedIndexChanged += delegate { Rebuild(true); };
            Controls.Add(filter);

            PeachButton install = new PeachButton("Installieren…", BtnStyle.Secondary, 140, 36); install.Location = new Point(980 - 28 - 140, TitleH + 17);
            install.Click += delegate { Install(); };
            PeachButton create = new PeachButton("Neues Addon", BtnStyle.Primary, 140, 36); create.Location = new Point(980 - 28 - 140 - 10 - 140, TitleH + 17);
            create.Click += delegate { CreateNew(); };
            Controls.Add(install); Controls.Add(create);

            hostLabel = AddLabel("", 28, TitleH + 70, 700, 22, true, 10f, false);

            list.RowHeight = 92;
            list.Location = new Point(24, TitleH + 104);
            list.Size = new Size(932, 760 - TitleH - 104 - 70);
            list.ToggleChanged += OnToggle;
            list.ActionClicked += OnAction;
            Controls.Add(list);

            int by = 760 - 56;
            PeachButton folder = new PeachButton("Addon-Ordner öffnen", BtnStyle.Ghost, 180, 36); folder.Location = new Point(24, by);
            folder.Click += delegate { OpenFolder(); };
            PeachButton web = new PeachButton("Web-Dashboard", BtnStyle.Ghost, 150, 36); web.Location = new Point(210, by);
            web.Click += delegate { try { Process.Start(host.DashboardUrl); } catch { } };
            PeachButton reload = new PeachButton("Neu laden", BtnStyle.Ghost, 110, 36); reload.Location = new Point(366, by);
            reload.Click += delegate { Reload(); };
            Controls.Add(folder); Controls.Add(web); Controls.Add(reload);

            hostAuto = new PeachToggle(cfg.AddonHostAutostart, "Addon-Host mit Peach starten"); hostAuto.Location = new Point(980 - 28 - hostAuto.Width, by + 4);
            hostAuto.CheckedChanged += delegate { cfg.AddonHostAutostart = hostAuto.Checked; cfg.Save(); };
            Controls.Add(hostAuto);

            Rebuild(true);
            timer.Interval = 1200;
            timer.Tick += delegate { Poll(); };
            timer.Start();
            Shown += delegate { if (PreviewMode) return; if (host.CanRun) host.StartHost(); Poll(); };
        }

        string HostMessage()
        {
            if (host.NodePath == null) return "Node.js wurde nicht gefunden. Script-Addons brauchen Node.js (nodejs.org). Die eingebauten Funktionen laufen auch ohne.";
            if (!host.ScriptFound) return "bin\\peach.js fehlt neben peach.exe.";
            if (host.Starting) return "Der Addon-Host startet …";
            return host.Error.Length > 0 ? "Keine Verbindung: " + host.Error : "Der Addon-Host läuft nicht. Mit „Host starten“ geht es los.";
        }

        void UpdateHostLabel()
        {
            if (host.Connected) { hostLabel.Text = "●  Addon-Host läuft" + (host.HostVersion.Length > 0 ? "  ·  Version " + host.HostVersion : "") + "  ·  " + host.Addons.Count + " Script-Addons"; hostLabel.ForeColor = Theme.Ok; }
            else { hostLabel.Text = "○  " + HostMessage(); hostLabel.ForeColor = host.NodePath == null ? Theme.Danger : Theme.Muted; }
        }

        void Poll()
        {
            if (refreshing || IsDisposed || PreviewMode) return;
            refreshing = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                host.Refresh();
                try { BeginInvoke(new Action(delegate { refreshing = false; Rebuild(false); })); } catch { refreshing = false; }
            });
        }

        void Rebuild(bool force)
        {
            UpdateHostLabel();
            List<RowItem> rows = AddonRows.Build(builtins, host.Addons, host.Connected, HostMessage(), host.CanRun, filter.SelectedIndex);
            string sig = AddonRows.Signature(rows);
            if (!force && sig == signature) return;
            signature = sig;
            list.Rows.Clear();
            list.Rows.AddRange(rows);
            list.RowsChanged();
        }

        void OnToggle(int i)
        {
            RowItem r = list.Rows[i];
            BuiltinAddon b = r.Tag as BuiltinAddon;
            AddonInfo a = r.Tag as AddonInfo;
            if (b != null) { b.SetOn(r.On); Rebuild(true); }
            else if (a != null)
            {
                bool want = r.On;
                r.Status = want ? "startet …" : "stoppt …"; r.StatusColor = Theme.Muted; list.Invalidate();
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    if (want) host.Start(a.Id); else host.Stop(a.Id);
                    System.Threading.Thread.Sleep(400);
                    host.Refresh();
                    try { BeginInvoke(new Action(delegate { Rebuild(true); })); } catch { }
                });
            }
        }

        void OnAction(int i, string id)
        {
            RowItem r = list.Rows[i];
            if (id == AddonRows.ActCustomize) { BuiltinAddon b = r.Tag as BuiltinAddon; if (b != null && b.Customize != null) { b.Customize(); Rebuild(true); } }
            else if (id == AddonRows.ActEdit) { AddonInfo a = r.Tag as AddonInfo; if (a != null) OpenEditor(a.Id, a.Name); }
            else if (id == AddonRows.ActLog) { AddonInfo a = r.Tag as AddonInfo; if (a != null) ShowLog(a.Id); }
            else if (id == AddonRows.ActRemove) { AddonInfo a = r.Tag as AddonInfo; if (a != null) Remove(a); }
            else if (id == AddonRows.ActStartHost) { host.StartHost(); Poll(); Rebuild(true); }
        }

        readonly Dictionary<string, AddonEditorForm> editors = new Dictionary<string, AddonEditorForm>();

        // One editor window per addon; no owner, so closing the Addons window does not hide it.
        void OpenEditor(string id, string name)
        {
            AddonEditorForm ed;
            if (editors.TryGetValue(id, out ed) && !ed.IsDisposed) { ed.WindowState = FormWindowState.Normal; Native.ForceForeground(ed.Handle); ed.Activate(); return; }
            if (!Directory.Exists(Path.Combine(host.ModsDir, id))) { Dialogs.Info(this, "Editor", "Der Ordner des Addons wurde nicht gefunden."); return; }
            ed = new AddonEditorForm(cfg, host, id, name);
            editors[id] = ed;
            ed.FormClosed += delegate { editors.Remove(id); };
            ed.Show();
        }

        void Reload()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                host.Rescan();
                host.Refresh();
                try { BeginInvoke(new Action(delegate { Rebuild(true); })); } catch { }
            });
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(host.ModsDir);
                Process.Start("explorer.exe", "\"" + host.ModsDir + "\"");
            }
            catch { }
        }

        void ShowLog(string id)
        {
            using (PeachForm f = new PeachForm("Log – " + id, 760, 560))
            {
                f.ShowInTaskbar = false;
                PeachTextBox box = new PeachTextBox(712, 560 - PeachForm.TitleH - 100, true); box.Location = new Point(24, PeachForm.TitleH + 20);
                box.ReadOnly = true; box.Inner.Font = new Font("Consolas", 9.5f);
                f.Controls.Add(box);
                Action fill = delegate
                {
                    AddonInfo a = host.Addons.Find(delegate (AddonInfo x) { return x.Id == id; });
                    StringBuilder sb = new StringBuilder();
                    if (a == null || a.Log.Count == 0) sb.Append("(noch keine Ausgabe)");
                    else foreach (AddonLog l in a.Log) sb.Append(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(l.T).ToLocalTime().ToString("HH:mm:ss")).Append("  ").Append(l.S == "err" ? "! " : (l.S == "sys" ? "· " : "  ")).Append(l.M).Append("\r\n");
                    box.Text = sb.ToString();
                    box.Inner.SelectionStart = box.Inner.TextLength; box.Inner.ScrollToCaret();
                };
                fill();
                PeachButton refresh = new PeachButton("Aktualisieren", BtnStyle.Secondary, 150, 40); refresh.Location = new Point(24, 560 - 62);
                refresh.Click += delegate
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate { host.Refresh(); try { f.BeginInvoke(new Action(delegate { fill(); })); } catch { } });
                };
                PeachButton close = new PeachButton("Schließen", BtnStyle.Primary, 130, 40); close.Location = new Point(760 - 24 - 130, 560 - 62); close.Result = DialogResult.OK;
                f.Controls.Add(refresh); f.Controls.Add(close);
                f.ShowDialog(this);
            }
        }

        void Remove(AddonInfo a)
        {
            if (!Dialogs.Ask(this, "Addon entfernen", "„" + a.Name + "“ wirklich entfernen?\n\nDer Ordner mods\\" + a.Id + " mit allen Dateien wird gelöscht. Das lässt sich nicht rückgängig machen.", "Entfernen", "Abbrechen", true)) return;
            string id = a.Id;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                host.Stop(id);
                System.Threading.Thread.Sleep(900);       // let the process end so Windows releases its files
                string err;
                bool ok = AddonFiles.Remove(host.ModsDir, id, out err);
                host.Rescan(); host.Refresh();
                try { BeginInvoke(new Action(delegate { Rebuild(true); if (!ok) Dialogs.Info(this, "Addon entfernen", "Das hat nicht geklappt: " + err); })); } catch { }
            });
        }

        void Install()
        {
            string choice = null;
            using (PeachForm f = new PeachForm("Addon installieren", 560, 330))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Ein fertiges Addon hinzufügen. Es muss eine mod.json enthalten – als ZIP-Datei oder als Ordner.\n\nAddons laufen mit deinen Windows-Rechten: installiere nur Addons, denen du vertraust.", 30, PeachForm.TitleH + 22, 500, 110, false, 10.5f, false);
                PeachButton zip = new PeachButton("Aus ZIP-Datei…", BtnStyle.Primary, 240, 46); zip.Location = new Point(30, 330 - 86);
                zip.Click += delegate { choice = "zip"; f.DialogResult = DialogResult.OK; };
                PeachButton dir = new PeachButton("Aus Ordner…", BtnStyle.Secondary, 240, 46); dir.Location = new Point(290, 330 - 86);
                dir.Click += delegate { choice = "dir"; f.DialogResult = DialogResult.OK; };
                f.Controls.Add(zip); f.Controls.Add(dir);
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            string src = null;
            if (choice == "zip")
                using (OpenFileDialog d = new OpenFileDialog { Filter = "Addon (*.zip)|*.zip", Title = "Addon-ZIP wählen" }) { if (d.ShowDialog(this) == DialogResult.OK) src = d.FileName; }
            else
                using (FolderBrowserDialog d = new FolderBrowserDialog { Description = "Ordner mit mod.json wählen" }) { if (d.ShowDialog(this) == DialogResult.OK) src = d.SelectedPath; }
            if (src == null) return;
            string err;
            string id = choice == "zip" ? AddonFiles.InstallZip(src, host.ModsDir, out err) : AddonFiles.InstallFolder(src, host.ModsDir, out err);
            if (id == null) { Dialogs.Info(this, "Addon installieren", err); return; }
            Dialogs.Info(this, "Addon installiert", "„" + id + "“ ist jetzt in der Liste. Mit dem Schalter startest du es.");
            Reload();
        }

        void CreateNew()
        {
            string[] permHelp = {
                "notify – Benachrichtigungen", "clipboard – Zwischenablage", "windows – Fenster ansehen", "hotkeys – globale Hotkeys",
                "registry – Registry lesen", "wallpaper – Hintergrund ändern", "shell – Programme starten" };
            using (PeachForm f = new PeachForm("Neues Addon", 640, 700))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Name", 30, PeachForm.TitleH + 20, 560, 22, true, 9.5f, true);
                PeachTextBox name = new PeachTextBox(580, 40, false); name.Location = new Point(30, PeachForm.TitleH + 46); name.Placeholder = "z. B. Mein Fenster-Helfer";
                f.AddLabel("Beschreibung (optional)", 30, PeachForm.TitleH + 98, 560, 22, true, 9.5f, true);
                PeachTextBox desc = new PeachTextBox(580, 40, false); desc.Location = new Point(30, PeachForm.TitleH + 124);
                f.AddLabel("Sprache", 30, PeachForm.TitleH + 176, 560, 22, true, 9.5f, true);
                PeachSegmented lang = new PeachSegmented(AddonFiles.LanguageNames, 0, 580); lang.Location = new Point(30, PeachForm.TitleH + 202);
                f.AddLabel("Rechte – was darf das Addon?", 30, PeachForm.TitleH + 254, 560, 22, true, 9.5f, true);
                f.Controls.Add(name); f.Controls.Add(desc); f.Controls.Add(lang);
                PeachToggle[] perms = new PeachToggle[permHelp.Length];
                for (int i = 0; i < perms.Length; i++)
                {
                    perms[i] = new PeachToggle(i == 0, permHelp[i]);
                    perms[i].Location = new Point(30 + (i % 2) * 295, PeachForm.TitleH + 284 + (i / 2) * 40);
                    f.Controls.Add(perms[i]);
                }
                f.AddLabel("Daraus entsteht ein Ordner mit mod.json und einer Startdatei zum Loslegen. Zu den Funktionen siehe die README („API“).", 30, PeachForm.TitleH + 450, 580, 44, true, 9.5f, false);

                string createdId = null, createdErr = null;
                PeachButton create = new PeachButton("Anlegen", BtnStyle.Primary, 130, 42); create.Location = new Point(640 - 30 - 130, 700 - 64);
                create.Click += delegate
                {
                    List<string> chosen = new List<string>();
                    for (int i = 0; i < perms.Length; i++) if (perms[i].Checked) chosen.Add(AddonFiles.AllPermissions[i]);
                    createdId = AddonFiles.Scaffold(host.ModsDir, name.Text, desc.Text, AddonFiles.Languages[lang.SelectedIndex], chosen, out createdErr);
                    if (createdId == null) { Dialogs.Info(f, "Neues Addon", createdErr); return; }
                    f.DialogResult = DialogResult.OK;
                };
                PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 42); cancel.Location = new Point(640 - 30 - 130 - 10 - 120, 700 - 64); cancel.Result = DialogResult.Cancel;
                f.Controls.Add(create); f.Controls.Add(cancel);

                if (f.ShowDialog(this) != DialogResult.OK || createdId == null) return;
                Reload();
                if (Dialogs.Ask(this, "Addon angelegt", "\u201E" + createdId + "\u201C liegt jetzt in mods\\" + createdId + ".\n\nJetzt im Editor \u00F6ffnen und loslegen? Gestartet wird es dort mit F5 oder hier mit dem Schalter.", "Im Editor \u00F6ffnen", "Sp\u00E4ter"))
                    OpenEditor(createdId, name.Text.Trim());
            }
        }

        // Closing the Addons window only hides it, so reopening is instant and the host keeps running.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        public void SetFilter(int index) { filter.SelectedIndex = index; }

        public void ShowAndFocus()
        {
            Rebuild(true);
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Native.ForceForeground(Handle);
            Activate();
            Poll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
