using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Peach
{
    // Theme circles: left half = Discord background tone, right half = the theme's accent.
    internal class ThemePicker : Control
    {
        readonly Func<string> current;
        int hot = -1;
        public event Action<string> Picked;

        public ThemePicker(Func<string> current, int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.current = current; Size = new Size(width, 76); Cursor = Cursors.Hand;
        }

        int Count { get { return DiscordTheme.Presets.Length + 1; } }
        string KeyAt(int i) { return i == 0 ? "" : DiscordTheme.Presets[i - 1].Key; }
        Rectangle Dot(int i) { return new Rectangle(i * 112, 0, 46, 46); }
        int Hit(Point p) { for (int i = 0; i < Count; i++) if (Dot(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0 && Picked != null) Picked(KeyAt(h)); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            string cur = (current() ?? "").ToLowerInvariant();
            for (int i = 0; i < Count; i++)
            {
                Rectangle d = Dot(i);
                ThemePreset p = i == 0 ? null : DiscordTheme.Presets[i - 1];
                Color left = p == null ? Color.FromArgb(49, 51, 56) : ColorTranslator.FromHtml(p.Low);
                Color right = p == null ? Color.FromArgb(88, 101, 242) : ColorTranslator.FromHtml(p.Brand);
                using (Brush b = new SolidBrush(left)) g.FillEllipse(b, d);
                using (Brush b = new SolidBrush(right)) g.FillPie(b, d, -90, 180);
                bool selected = cur == KeyAt(i);
                using (Pen pen = new Pen(selected ? Theme.Accent : (i == hot ? Theme.Muted : Theme.Border), selected ? 3f : 1.5f)) g.DrawEllipse(pen, d.X, d.Y, d.Width - 1, d.Height - 1);
                using (Brush t = new SolidBrush(selected ? Theme.Ink : Theme.Muted))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(p == null ? "Standard" : p.Label, Theme.Font(9f, selected), t, new RectangleF(d.X - 20, d.Bottom + 6, d.Width + 40, 18), sf);
            }
        }
    }

    // Small editor for one Discord script (reuses the addon editor's code box).
    internal class ScriptEditorForm : PeachForm
    {
        readonly string path;
        readonly CodeBox code = new CodeBox();
        string saved;

        public ScriptEditorForm(string path) : base("Skript – " + Path.GetFileName(path), 980, 700)
        {
            this.path = path; EscCloses = false;
            code.Location = new Point(16, TitleH + 12); code.Size = new Size(948, 700 - TitleH - 12 - 76);
            string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
            code.Load(text, "js"); saved = code.Value;
            code.SaveRequested += delegate { Save(); };
            Controls.Add(code);
            AddLabel("Strg + S speichert. Änderungen am Skript gelten nach dem nächsten Neuladen der Discord-Seite (Peach macht das beim Schließen der Einstellungen).", 20, 700 - 62, 700, 40, true, 9.5f, false);
            PeachButton save = new PeachButton("Speichern", BtnStyle.Primary, 130, 40); save.Location = new Point(980 - 24 - 130, 700 - 62);
            save.Click += delegate { Save(); };
            PeachButton close = new PeachButton("Schließen", BtnStyle.Secondary, 120, 40); close.Location = new Point(980 - 24 - 130 - 10 - 120, 700 - 62);
            close.Click += delegate { Close(); };
            Controls.Add(save); Controls.Add(close);
            FormClosing += delegate
            {
                if (code.Value != saved && Dialogs.Ask(this, "Ungespeicherte Änderungen", "Das Skript wurde geändert. Speichern?", "Speichern", "Verwerfen")) Save();
            };
        }

        public bool Saved { get { return code.Value == saved; } }

        void Save()
        {
            try { File.WriteAllText(path, code.Value.Replace("\n", "\r\n"), new UTF8Encoding(false)); saved = code.Value; }
            catch (Exception e) { Dialogs.Info(this, "Speichern", "Das hat nicht geklappt: " + e.Message); }
        }
    }

    // Vencord-style settings for the Discord window: look, commands, CSS and scripts. Everything is applied live.
    internal class DiscordSettingsForm : PeachForm
    {
        readonly Config cfg;
        readonly Action<bool> changed;           // true = scripts changed (page reload needed)
        readonly List<Control>[] pages = { new List<Control>(), new List<Control>(), new List<Control>(), new List<Control>() };
        readonly PeachSegmented tabs;
        readonly PeachList commands = new PeachList(), scripts = new PeachList(), abbrs = new PeachList();
        readonly Timer cssTimer = new Timer();
        PeachTextBox css, accent, font;
        Swatch accentSwatch;
        ThemePicker themes;
        bool loading = true, scriptsDirty;

        T Add<T>(int page, T c) where T : Control { pages[page].Add(c); Controls.Add(c); return c; }
        Label Lbl(int page, string text, int x, int y, int w, int h, bool muted, float size, bool bold) { Label l = AddLabel(text, x, y, w, h, muted, size, bold); pages[page].Add(l); return l; }

        public DiscordSettingsForm(Config cfg, Action<bool> changed) : base("Discord – Einstellungen", 880, 820)
        {
            this.cfg = cfg; this.changed = changed;
            DiscordConfig d = cfg.Discord;

            tabs = new PeachSegmented(new string[] { "Aussehen", "Befehle", "Kürzel", "Skripte & CSS" }, 0, 620);
            tabs.Location = new Point(28, TitleH + 16);
            tabs.SelectedIndexChanged += delegate { ShowPage(); };
            Controls.Add(tabs);
            int y0 = TitleH + 78;

            // ---- page 0: look ----
            Lbl(0, "THEME", 28, y0, 200, 18, true, 8.5f, true);
            themes = Add(0, new ThemePicker(delegate { return cfg.Discord.Theme; }, 800)); themes.Location = new Point(28, y0 + 26);
            themes.Picked += delegate (string key) { cfg.Discord.Theme = key; Apply(false); themes.Invalidate(); };

            Lbl(0, "Akzentfarbe  (ersetzt das Discord-Blau; leer = Theme oder Standard)", 28, y0 + 124, 700, 20, false, 10f, true);
            accent = Add(0, new PeachTextBox(140, 40, false)); accent.Location = new Point(28, y0 + 150); accent.Placeholder = "#RRGGBB"; accent.Text = d.Accent;
            accentSwatch = Add(0, new Swatch(DiscordTheme.IsHex(d.Accent) ? ColorTranslator.FromHtml(DiscordTheme.NormHex(d.Accent)) : Theme.Card)); accentSwatch.Location = new Point(178, y0 + 150);
            accent.TextChanged += delegate
            {
                if (loading) return;
                if (accent.Text.Trim().Length == 0) { cfg.Discord.Accent = ""; accentSwatch.Value = Theme.Card; accentSwatch.Invalidate(); Apply(false); return; }
                if (!DiscordTheme.IsHex(accent.Text)) return;
                cfg.Discord.Accent = DiscordTheme.NormHex(accent.Text);
                accentSwatch.Value = ColorTranslator.FromHtml(cfg.Discord.Accent); accentSwatch.Invalidate(); Apply(false); themes.Invalidate();
            };
            accentSwatch.Click += delegate
            {
                using (ColorDialog cd = new ColorDialog { Color = accentSwatch.Value, FullOpen = true })
                    if (cd.ShowDialog(this) == DialogResult.OK) accent.Text = BarLogic.ToHex(cd.Color).ToLowerInvariant();
            };
            PeachButton reset = Add(0, new PeachButton("Zurücksetzen", BtnStyle.Ghost, 130, 40)); reset.Location = new Point(228, y0 + 150);
            reset.Click += delegate { accent.Text = ""; };

            Lbl(0, "Schrift  (z. B. Segoe UI, Consolas; leer = Discords Schrift)", 28, y0 + 214, 700, 20, false, 10f, true);
            font = Add(0, new PeachTextBox(360, 40, false)); font.Location = new Point(28, y0 + 240); font.Text = d.Font;
            font.TextChanged += delegate { if (loading) return; cfg.Discord.Font = font.Text.Trim(); Apply(false); };

            Lbl(0, "Zoom", 28, y0 + 304, 200, 20, false, 10f, true);
            PeachSlider zoom = Add(0, new PeachSlider(60, 160, d.Zoom, " %", 560)); zoom.Location = new Point(28, y0 + 330);
            zoom.ValueChanged += delegate { if (loading) return; cfg.Discord.Zoom = zoom.Value; Apply(false); };

            PeachToggle motion = Add(0, new PeachToggle(d.ReduceMotion, "Animationen reduzieren (ruhigere Oberfläche)")); motion.Location = new Point(28, y0 + 384);
            motion.CheckedChanged += delegate { if (loading) return; cfg.Discord.ReduceMotion = motion.Checked; Apply(false); };
            PeachToggle sendNow = Add(0, new PeachToggle(d.SendCommandsNow, "Befehle sofort senden (sonst wartet der Text im Chatfeld auf Enter)")); sendNow.Location = new Point(28, y0 + 428);
            sendNow.CheckedChanged += delegate { if (loading) return; cfg.Discord.SendCommandsNow = sendNow.Checked; Apply(false); };
            Lbl(0, "Themes färben Discords Oberfläche um. Discord ändert sein Design öfters – wenn etwas nicht passt, hilft eigenes CSS (Reiter „Skripte & CSS“).\nHinweis: Clientmodifikationen sind laut Discord-Bedingungen nicht vorgesehen. Peach beschränkt sich auf Aussehen und Komfort.", 28, y0 + 488, 810, 64, true, 9.5f, false);

            // ---- page 1: commands ----
            Lbl(1, "Im Chatfeld tippen und Enter drücken – der Befehl wird von Peach abgefangen und nie an Discord geschickt:\n/peach help   ·   /peach settings   ·   /peach theme <name>   ·   /peach commands\nEigene Befehle: „/name“ tippt deinen Text ein oder öffnet ein Programm, eine Datei oder eine Webseite. Optional mit Tastenkürzel – das gilt nur, solange das Discord-Fenster aktiv ist.\n{args} = was hinter dem Befehl steht, {date} = Datum, {time} = Uhrzeit.", 28, y0, 810, 78, true, 10f, false);
            commands.RowHeight = 62; commands.Location = new Point(24, y0 + 90); commands.Size = new Size(826, 440);
            commands.EmptyText = "Noch keine eigenen Befehle.";
            commands.ItemActivated += delegate { EditCommand(commands.SelectedIndex); };
            Add(1, commands);
            PeachButton cAdd = Add(1, new PeachButton("Hinzufügen", BtnStyle.Primary, 136, 40)); cAdd.Location = new Point(28, y0 + 546);
            cAdd.Click += delegate { EditCommand(-1); };
            PeachButton cEdit = Add(1, new PeachButton("Bearbeiten", BtnStyle.Secondary, 124, 40)); cEdit.Location = new Point(174, y0 + 546);
            cEdit.Click += delegate { EditCommand(commands.SelectedIndex); };
            PeachButton cDel = Add(1, new PeachButton("Löschen", BtnStyle.Danger, 110, 40)); cDel.Location = new Point(308, y0 + 546);
            cDel.Click += delegate
            {
                int i = commands.SelectedIndex;
                if (i < 0 || i >= cfg.Discord.Commands.Count) return;
                cfg.Discord.Commands.RemoveAt(i); Apply(false); ReloadCommands();
            };

            // ---- page 2: abbreviations (Fast Type, Discord only) ----
            Lbl(2, "Kürzel werden direkt beim Tippen im Discord-Chatfeld ersetzt – wie Fast Type, aber nur hier. Beispiel: „;gn“ wird zu „Gute Nacht“.\n{date} = Datum, {time} = Uhrzeit. Ein Kürzel muss mindestens 2 Zeichen haben und wirkt nicht mitten in einem Wort (außer es beginnt mit einem Sonderzeichen wie „;“).", 28, y0, 810, 78, true, 10f, false);
            abbrs.RowHeight = 62; abbrs.Location = new Point(24, y0 + 90); abbrs.Size = new Size(826, 440);
            abbrs.EmptyText = "Noch keine Kürzel.";
            abbrs.ItemActivated += delegate { EditAbbr(abbrs.SelectedIndex); };
            Add(2, abbrs);
            PeachButton aAdd = Add(2, new PeachButton("Hinzufügen", BtnStyle.Primary, 136, 40)); aAdd.Location = new Point(28, y0 + 546);
            aAdd.Click += delegate { EditAbbr(-1); };
            PeachButton aEdit = Add(2, new PeachButton("Bearbeiten", BtnStyle.Secondary, 124, 40)); aEdit.Location = new Point(174, y0 + 546);
            aEdit.Click += delegate { EditAbbr(abbrs.SelectedIndex); };
            PeachButton aDel = Add(2, new PeachButton("Löschen", BtnStyle.Danger, 110, 40)); aDel.Location = new Point(308, y0 + 546);
            aDel.Click += delegate
            {
                int i = abbrs.SelectedIndex;
                if (i < 0 || i >= cfg.Discord.Abbreviations.Count) return;
                cfg.Discord.Abbreviations.RemoveAt(i); Apply(false); ReloadAbbrs();
            };

            // ---- page 3: css + scripts ----
            Lbl(3, "Eigenes CSS  (wirkt sofort)", 28, y0, 400, 20, false, 10f, true);
            css = Add(3, new PeachTextBox(826, 190, true)); css.Location = new Point(24, y0 + 26); css.Text = d.CustomCss.Replace("\n", "\r\n");
            css.Inner.Font = new Font("Consolas", 10f); css.Inner.AcceptsTab = true; css.Placeholder = "/* z. B. */\n[class*='sidebar'] { width: 220px; }";
            cssTimer.Interval = 450; cssTimer.Tick += delegate { cssTimer.Stop(); cfg.Discord.CustomCss = css.Text.Replace("\r\n", "\n"); Apply(false); };
            css.TextChanged += delegate { if (loading) return; cssTimer.Stop(); cssTimer.Start(); };

            Lbl(3, "Skripte  (eigene Plugins, laufen nur im Discord-Fenster)", 28, y0 + 232, 600, 20, false, 10f, true);
            Label warn = Lbl(3, "⚠  Skripte können alles im Discord-Fenster – auch auf dein Konto zugreifen. Nur eigene oder vertrauenswürdige Skripte einschalten.", 28, y0 + 256, 810, 24, false, 9.5f, false);
            warn.ForeColor = Theme.Accent;
            scripts.RowHeight = 62; scripts.Location = new Point(24, y0 + 286); scripts.Size = new Size(826, 250);
            scripts.EmptyText = "Noch keine Skripte. „Neues Skript“ legt eins aus einer Vorlage an.";
            scripts.ToggleChanged += delegate (int i)
            {
                string name = scripts.Rows[i].Tag as string;
                if (name == null) return;
                if (scripts.Rows[i].On) { if (!cfg.Discord.EnabledScripts.Contains(name)) cfg.Discord.EnabledScripts.Add(name); }
                else cfg.Discord.EnabledScripts.Remove(name);
                Apply(true); ReloadScripts();
            };
            scripts.ActionClicked += delegate (int i, string act)
            {
                string name = scripts.Rows[i].Tag as string;
                if (name == null) return;
                if (act == "edit") { using (ScriptEditorForm ed = new ScriptEditorForm(Path.Combine(DiscordScripts.Dir(cfg), name))) ed.ShowDialog(this); scriptsDirty = true; }
                else if (act == "del" && Dialogs.Ask(this, "Skript löschen", "„" + name + "“ wirklich löschen?", "Löschen", "Abbrechen", true)) { DiscordScripts.Delete(cfg, name); Apply(true); ReloadScripts(); }
            };
            Add(3, scripts);
            PeachButton sNew = Add(3, new PeachButton("Neues Skript", BtnStyle.Primary, 150, 40)); sNew.Location = new Point(28, y0 + 552);
            sNew.Click += delegate
            {
                string name = DiscordScripts.Create(cfg, "skript");
                Apply(true); ReloadScripts();
                for (int i = 0; i < scripts.Rows.Count; i++) if ((scripts.Rows[i].Tag as string) == name) scripts.SelectRow(i);
            };
            PeachButton sFolder = Add(3, new PeachButton("Ordner öffnen", BtnStyle.Secondary, 150, 40)); sFolder.Location = new Point(188, y0 + 552);
            sFolder.Click += delegate { try { Directory.CreateDirectory(DiscordScripts.Dir(cfg)); Process.Start("explorer.exe", "\"" + DiscordScripts.Dir(cfg) + "\""); } catch { } };
            Lbl(3, "Neue Dateien im Ordner starten ausgeschaltet.", 348, y0 + 561, 500, 22, true, 9.5f, false);

            PeachButton done = new PeachButton("Fertig", BtnStyle.Primary, 130, 42); done.Location = new Point(880 - 24 - 130, 820 - 62);
            done.Click += delegate { Close(); };
            Controls.Add(done);

            ReloadCommands(); ReloadAbbrs(); ReloadScripts();
            loading = false;
            ShowPage();
        }

        public void ShowTab(int index) { tabs.SelectedIndex = index; }

        void ShowPage()
        {
            for (int i = 0; i < pages.Length; i++) foreach (Control c in pages[i]) c.Visible = i == tabs.SelectedIndex;
        }

        void Apply(bool scriptsChanged)
        {
            if (loading) return;
            if (scriptsChanged) scriptsDirty = false;   // this call already re-registers the scripts and reloads the page
            cfg.Save();
            changed(scriptsChanged);
        }

        void ReloadCommands()
        {
            commands.Rows.Clear();
            foreach (CommandEntry c in cfg.Discord.Commands)
            {
                RowItem r = new RowItem(); r.Title = "/" + c.Name + (string.IsNullOrEmpty(c.Hotkey) ? "" : "   ·   " + PeachHotkeyBox.Pretty(c.Hotkey)); r.Letter = c.Action == "open" ? "↗" : "/";
                r.Subtitle = (c.Action == "open" ? "Öffnet: " : "Tippt: ") + (c.Text ?? "").Replace("\n", " ↵ ");
                commands.Rows.Add(r);
            }
            commands.RowsChanged();
        }

        void ReloadAbbrs()
        {
            abbrs.Rows.Clear();
            foreach (Snippet s in cfg.Discord.Abbreviations)
            {
                RowItem r = new RowItem(); r.Title = s.Abbr; r.Letter = "→"; r.Subtitle = (s.Text ?? "").Replace("\n", " ↵ ");
                abbrs.Rows.Add(r);
            }
            abbrs.RowsChanged();
        }

        // null = fine, otherwise the message to show
        public static string CheckAbbr(Config cfg, Snippet existing, string a, string text)
        {
            if (!DiscordBridge.ValidAbbreviation(a)) return "Ein Kürzel braucht mindestens 2 Zeichen ohne Leerzeichen.";
            if ((text ?? "").Trim().Length == 0) return "Der Text darf nicht leer sein.";
            foreach (Snippet s in cfg.Discord.Abbreviations)
            {
                if (s == existing || s.Abbr == null) continue;
                if (string.Equals(s.Abbr, a, StringComparison.OrdinalIgnoreCase)) return "Dieses Kürzel gibt es schon.";
                if (s.Abbr.StartsWith(a, StringComparison.OrdinalIgnoreCase) || a.StartsWith(s.Abbr, StringComparison.OrdinalIgnoreCase))
                    return "Konflikt mit „" + s.Abbr + "“: Ein Kürzel darf nicht am Anfang eines anderen stehen, sonst löst das kürzere zuerst aus.";
            }
            return null;
        }

        // null = fine, otherwise the message to show
        public static string CheckCommand(Config cfg, CommandEntry existing, string name, string text, string action, string hotkey)
        {
            if (!DiscordBridge.ValidCommandName(name)) return "Der Name muss mit einem Buchstaben beginnen (Buchstaben, Zahlen, - und _, höchstens 32 Zeichen). „peach“ ist reserviert.";
            if ((text ?? "").Trim().Length == 0) return action == "open" ? "Bitte ein Programm, eine Datei oder eine URL angeben." : "Der Text darf nicht leer sein.";
            foreach (CommandEntry c in cfg.Discord.Commands) if (c != existing && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return "Diesen Befehl gibt es schon.";
            if (!string.IsNullOrEmpty(hotkey))
            {
                string n = Combo.Normalize(hotkey);
                if (n == null) return "Diese Tastenkombination wird nicht erkannt.";
                if (!(n.Contains("ctrl+") || n.Contains("alt+") || n.Contains("win+"))) return "Das Tastenkürzel braucht Strg oder Alt (nur Umschalt würde das normale Tippen stören).";
                if (n == HotkeyManager.MenuCombo) return "Strg + Alt + P ist für das Peach-Menü reserviert.";
                foreach (HotkeyEntry h in cfg.Hotkeys) if (Combo.Normalize(h.Combo) == n) return "Diese Kombination ist schon ein globaler Peach-Hotkey (der hat Vorrang).";
                foreach (CommandEntry c in cfg.Discord.Commands) if (c != existing && !string.IsNullOrEmpty(c.Hotkey) && Combo.Normalize(c.Hotkey) == n) return "Diese Kombination nutzt schon /" + c.Name + ".";
            }
            return null;
        }

        void EditAbbr(int index)
        {
            Snippet existing = index >= 0 && index < cfg.Discord.Abbreviations.Count ? cfg.Discord.Abbreviations[index] : null;
            if (index >= 0 && existing == null) return;
            using (PeachForm f = new PeachForm(existing == null ? "Kürzel hinzufügen" : "Kürzel bearbeiten", 600, 400))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Kürzel  (z. B. ;gn – mindestens 2 Zeichen)", 30, PeachForm.TitleH + 22, 540, 22, true, 9.5f, true);
                PeachTextBox ab = new PeachTextBox(540, 40, false); ab.Location = new Point(30, PeachForm.TitleH + 48);
                f.AddLabel("Wird zu  ({date}, {time} werden ersetzt)", 30, PeachForm.TitleH + 106, 540, 22, true, 9.5f, true);
                PeachTextBox text = new PeachTextBox(540, 120, true); text.Location = new Point(30, PeachForm.TitleH + 132);
                if (existing != null) { ab.Text = existing.Abbr; text.Text = (existing.Text ?? "").Replace("\n", "\r\n"); }
                f.Controls.Add(ab); f.Controls.Add(text);
                PeachButton save = new PeachButton("Speichern", BtnStyle.Primary, 130, 40); save.Location = new Point(600 - 30 - 130, 400 - 62);
                save.Click += delegate
                {
                    string err = CheckAbbr(cfg, existing, ab.Text.Trim(), text.Text);
                    if (err != null) { Dialogs.Info(f, "Kürzel", err); return; }
                    f.DialogResult = DialogResult.OK;
                };
                PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(600 - 30 - 130 - 10 - 120, 400 - 62); cancel.Result = DialogResult.Cancel;
                f.Controls.Add(save); f.Controls.Add(cancel);
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Snippet s2 = existing ?? new Snippet();
                s2.Abbr = ab.Text.Trim(); s2.Text = text.Text.Replace("\r\n", "\n");
                if (existing == null) cfg.Discord.Abbreviations.Add(s2);
                Apply(false); ReloadAbbrs();
            }
        }

        void ReloadScripts()
        {
            scripts.Rows.Clear();
            foreach (string name in DiscordScripts.Files(cfg))
            {
                bool on = cfg.Discord.EnabledScripts.Contains(name);
                RowItem r = new RowItem(); r.Title = name; r.Letter = "JS"; r.Tint = Color.FromArgb(240, 200, 80); r.Tag = name;
                r.Subtitle = on ? "eingeschaltet – läuft in Discord" : "aus – läuft nicht";
                r.HasToggle = true; r.On = on;
                r.Actions.Add(new RowAction("edit", "Bearbeiten", false)); r.Actions.Add(new RowAction("del", "Löschen", true));
                scripts.Rows.Add(r);
            }
            scripts.RowsChanged();
        }

        void EditCommand(int index)
        {
            CommandEntry existing = index >= 0 && index < cfg.Discord.Commands.Count ? cfg.Discord.Commands[index] : null;
            if (index >= 0 && existing == null) return;
            using (PeachForm f = new PeachForm(existing == null ? "Befehl hinzufügen" : "Befehl bearbeiten", 600, 640))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Name  (im Chatfeld als /name; Buchstaben, Zahlen, - und _)", 30, PeachForm.TitleH + 22, 540, 22, true, 9.5f, true);
                PeachTextBox name = new PeachTextBox(540, 40, false); name.Location = new Point(30, PeachForm.TitleH + 48);
                f.AddLabel("Aktion", 30, PeachForm.TitleH + 102, 540, 22, true, 9.5f, true);
                PeachSegmented act = new PeachSegmented(new string[] { "Text ins Chatfeld", "Programm / Datei / URL öffnen" }, existing != null && existing.Action == "open" ? 1 : 0, 540);
                act.Location = new Point(30, PeachForm.TitleH + 128);
                Label textLbl = f.AddLabel("", 30, PeachForm.TitleH + 176, 540, 22, true, 9.5f, true);
                PeachTextBox text = new PeachTextBox(540, 110, true); text.Location = new Point(30, PeachForm.TitleH + 202);
                Label argsLbl = f.AddLabel("Feste Argumente  (nur für Programme; Chat-Text wird dort nie eingesetzt)", 30, PeachForm.TitleH + 326, 540, 22, true, 9.5f, true);
                PeachTextBox args = new PeachTextBox(540, 40, false); args.Location = new Point(30, PeachForm.TitleH + 352);
                f.AddLabel("Tastenkürzel  (optional – gilt nur, wenn das Discord-Fenster aktiv ist)", 30, PeachForm.TitleH + 408, 540, 22, true, 9.5f, true);
                PeachHotkeyBox hk = new PeachHotkeyBox(existing == null ? "" : existing.Hotkey, 400); hk.Location = new Point(30, PeachForm.TitleH + 434);
                PeachButton hkClear = new PeachButton("Entfernen", BtnStyle.Ghost, 130, 38); hkClear.Location = new Point(440, PeachForm.TitleH + 434);
                hkClear.Click += delegate { hk.Clear(); };
                Action refresh = delegate
                {
                    bool open = act.SelectedIndex == 1;
                    textLbl.Text = open ? "Ziel  (Programm, Datei oder https-URL; bei URLs wird {args} eingesetzt: …/search?q={args})" : "Wird zu  ({args}, {date}, {time} werden ersetzt)";
                    argsLbl.Visible = args.Visible = open;
                };
                act.SelectedIndexChanged += delegate { refresh(); };
                if (existing != null) { name.Text = existing.Name; text.Text = (existing.Text ?? "").Replace("\n", "\r\n"); args.Text = existing.Args ?? ""; }
                f.Controls.Add(name); f.Controls.Add(act); f.Controls.Add(text); f.Controls.Add(args); f.Controls.Add(hk); f.Controls.Add(hkClear);
                refresh();
                int fh = 640;
                PeachButton save = new PeachButton("Speichern", BtnStyle.Primary, 130, 40); save.Location = new Point(600 - 30 - 130, fh - 62);
                save.Click += delegate
                {
                    string n = name.Text.Trim().TrimStart('/');
                    string err = CheckCommand(cfg, existing, n, text.Text, act.SelectedIndex == 1 ? "open" : "text", hk.Combo);
                    if (err != null) { Dialogs.Info(f, "Befehl", err); return; }
                    f.DialogResult = DialogResult.OK;
                };
                PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(600 - 30 - 130 - 10 - 120, fh - 62); cancel.Result = DialogResult.Cancel;
                f.Controls.Add(save); f.Controls.Add(cancel);
                if (f.ShowDialog(this) != DialogResult.OK) return;
                CommandEntry c2 = existing ?? new CommandEntry();
                c2.Name = name.Text.Trim().TrimStart('/'); c2.Text = text.Text.Replace("\r\n", "\n");
                c2.Action = act.SelectedIndex == 1 ? "open" : "text";
                c2.Args = c2.Action == "open" && args.Text.Trim().Length > 0 ? args.Text.Trim() : null;
                c2.Hotkey = string.IsNullOrEmpty(hk.Combo) ? null : Combo.Normalize(hk.Combo);
                if (existing == null) cfg.Discord.Commands.Add(c2);
                Apply(false); ReloadCommands();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cssTimer.Enabled) { cssTimer.Stop(); cfg.Discord.CustomCss = css.Text.Replace("\r\n", "\n"); Apply(false); }
            base.OnFormClosing(e);
            if (!e.Cancel && scriptsDirty) { scriptsDirty = false; changed(true); }   // script edits: re-register and reload once
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) cssTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
