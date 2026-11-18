using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Peach
{
    internal class MinecraftForm : PeachForm
    {
        readonly Config cfg;
        readonly Action overlayChanged;
        readonly PeachSegmented tabs;
        readonly List<Control>[] pages = { new List<Control>(), new List<Control>(), new List<Control>(), new List<Control>() };
        readonly PeachList profiles = new PeachList(), mods = new PeachList();
        readonly Label status, modsHeader, accountLabel, codeLabel;
        readonly List<PeachButton> busyButtons = new List<PeachButton>();
        PeachButton signIn;
        bool loading = true, busy, signingIn, cancelSignIn;

        T Add<T>(int page, T c) where T : Control { pages[page].Add(c); Controls.Add(c); return c; }
        Label Lbl(int page, string text, int x, int y, int w, int h, bool muted, float size, bool bold) { Label l = AddLabel(text, x, y, w, h, muted, size, bold); pages[page].Add(l); return l; }

        // runs work on a background thread, then done on the window's thread
        void Async<T>(Func<T> work, Action<T, Exception> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                T r = default(T); Exception ex = null;
                try { r = work(); } catch (Exception e) { ex = e; }
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(delegate { done(r, ex); })); } catch { }
            });
        }

        void SetBusy(bool b, string text)
        {
            busy = b; foreach (PeachButton pb in busyButtons) pb.Enabled = !b;
            status.Text = text ?? ""; status.ForeColor = Theme.Muted;
        }

        public MinecraftForm(Config cfg, Action overlayChanged) : base("Minecraft", 900, 800)
        {
            this.cfg = cfg; this.overlayChanged = overlayChanged;
            tabs = new PeachSegmented(new string[] { "Profile & Start", "Mods", "Widgets", "Konto" }, 0, 620);
            tabs.Location = new Point(28, TitleH + 16);
            tabs.SelectedIndexChanged += delegate { ShowPage(); };
            Controls.Add(tabs);
            int y0 = TitleH + 74;
            status = AddLabel("", 28, 800 - 60, 600, 40, true, 9.5f, false);

            // ---- page 0: profiles ----
            Lbl(0, "Peachs eigener Minecraft-Launcher: Jedes Profil hat seinen Spielordner mit eigenen Mods. Beim ersten Start lädt Peach die Spieldateien von Mojang (mit Prüfsummen) und startet Minecraft direkt – ohne den offiziellen Launcher. Dafür brauchst du Minecraft Java Edition auf deinem Microsoft-Konto (Reiter „Konto“) und Java.", 28, y0, 844, 62, true, 10f, false);
            profiles.RowHeight = 64; profiles.Location = new Point(24, y0 + 74); profiles.Size = new Size(852, 400); profiles.EmptyText = "Noch kein Profil.\nMit „Neues Profil“ legst du eins an (z. B. Fabric 1.21.1).";
            profiles.SelectionChanged += delegate { OnSelect(); };
            profiles.ItemActivated += delegate { Play(false); };
            Add(0, profiles);
            int by = y0 + 488;
            PeachButton nw = Track(Add(0, new PeachButton("Neues Profil", BtnStyle.Primary, 150, 42))); nw.Location = new Point(28, by); nw.Click += delegate { NewProfile(); };
            PeachButton play = Track(Add(0, new PeachButton("Spielen", BtnStyle.Primary, 130, 42))); play.Location = new Point(188, by); play.Click += delegate { Play(false); };
            PeachButton demo = Track(Add(0, new PeachButton("Demo", BtnStyle.Secondary, 100, 42))); demo.Location = new Point(328, by); demo.Click += delegate { Play(true); };
            PeachButton folder = Track(Add(0, new PeachButton("Ordner öffnen", BtnStyle.Secondary, 150, 42))); folder.Location = new Point(438, by); folder.Click += delegate { OpenFolder(false); };
            PeachButton del = Track(Add(0, new PeachButton("Löschen", BtnStyle.Danger, 120, 42))); del.Location = new Point(598, by); del.Click += delegate { DeleteProfile(); };
            Lbl(0, "„Demo“ startet Mojangs offizielle Demo ohne Konto (zeitlich begrenzt).", 28, by + 48, 700, 20, true, 9f, false);

            // ---- page 1: mods ----
            modsHeader = Lbl(1, "", 28, y0, 844, 40, false, 10.5f, true);
            mods.RowHeight = 54; mods.Location = new Point(24, y0 + 50); mods.Size = new Size(852, 424); mods.EmptyText = "Noch keine Mods in diesem Profil.\nMit „Mods suchen“ installierst du welche von Modrinth.";
            mods.ToggleChanged += delegate (int i) { ToggleMod(i); };
            mods.ActionClicked += delegate (int i, string act) { if (act == "del") RemoveMod(i); };
            Add(1, mods);
            PeachButton search = Track(Add(1, new PeachButton("Mods suchen & installieren", BtnStyle.Primary, 260, 42))); search.Location = new Point(28, by); search.Click += delegate { SearchMods(); };
            PeachButton mfolder = Track(Add(1, new PeachButton("Ordner öffnen", BtnStyle.Secondary, 150, 42))); mfolder.Location = new Point(298, by); mfolder.Click += delegate { OpenFolder(true); };

            // ---- page 2: widgets ----
            MinecraftConfig m = cfg.Minecraft;
            PeachToggle on = Add(2, new PeachToggle(m.OverlayEnabled, "Widgets über Minecraft anzeigen")); on.Location = new Point(28, y0);
            on.CheckedChanged += delegate { if (loading) return; m.OverlayEnabled = on.Checked; Changed(); };
            PeachToggle only = Add(2, new PeachToggle(m.OverlayOnlyMinecraft, "Nur zeigen, wenn Minecraft im Vordergrund ist")); only.Location = new Point(28, y0 + 42);
            only.CheckedChanged += delegate { if (loading) return; m.OverlayOnlyMinecraft = only.Checked; Changed(); };
            Lbl(2, "Position", 28, y0 + 92, 200, 20, false, 10f, true);
            string[] corners = { "topleft", "topright", "bottomleft", "bottomright" };
            PeachSegmented pos = Add(2, new PeachSegmented(new string[] { "Oben links", "Oben rechts", "Unten links", "Unten rechts" }, Math.Max(0, Array.IndexOf(corners, m.OverlayCorner)), 560)); pos.Location = new Point(28, y0 + 118);
            pos.SelectedIndexChanged += delegate { if (loading) return; m.OverlayCorner = corners[pos.SelectedIndex]; Changed(); };
            Lbl(2, "Deckkraft", 28, y0 + 168, 200, 20, false, 10f, true);
            PeachSlider op = Add(2, new PeachSlider(30, 100, m.OverlayOpacity, " %", 420)); op.Location = new Point(28, y0 + 194);
            op.ValueChanged += delegate { if (loading) return; m.OverlayOpacity = op.Value; Changed(); };
            Lbl(2, "Welche Widgets", 28, y0 + 244, 300, 20, false, 10f, true);
            int wy = y0 + 272;
            foreach (string w in OverlayModel.AllWidgets)
            {
                string key = w;
                PeachToggle t = Add(2, new PeachToggle(m.Widgets.Contains(key), OverlayModel.Title(key))); t.Location = new Point(28 + (Array.IndexOf(OverlayModel.AllWidgets, key) % 3) * 280, wy + (Array.IndexOf(OverlayModel.AllWidgets, key) / 3) * 40);
                t.CheckedChanged += delegate
                {
                    if (loading) return;
                    if (t.Checked) { if (!m.Widgets.Contains(key)) { m.Widgets.Add(key); SortWidgets(m); } } else m.Widgets.Remove(key);
                    Changed();
                };
            }
            Lbl(2, "Spotify steuern (Mediatasten, wirkt auf die Spotify-App)", 28, y0 + 372, 600, 20, false, 10f, true);
            PeachButton prev = Add(2, new PeachButton("⏮", BtnStyle.Secondary, 70, 40)); prev.Location = new Point(28, y0 + 398); prev.Click += delegate { MediaKeys.Send("prev"); };
            PeachButton pp = Add(2, new PeachButton("⏯", BtnStyle.Secondary, 70, 40)); pp.Location = new Point(106, y0 + 398); pp.Click += delegate { MediaKeys.Send("play"); };
            PeachButton nx = Add(2, new PeachButton("⏭", BtnStyle.Secondary, 70, 40)); nx.Location = new Point(184, y0 + 398); nx.Click += delegate { MediaKeys.Send("next"); };
            Lbl(2, "Die Widgets liegen als durchklickbares Fenster über dem Spiel und funktionieren mit jeder Minecraft-Version – ohne Mod. Dafür muss Minecraft im Fenster oder Randlos-Vollbild laufen (nicht im exklusiven Vollbild, F11). Spotify: die Song-Anzeige liest den Fenstertitel der Spotify-Desktop-App (zeigt „Künstler – Titel“, solange es spielt). FPS und Koordinaten kann nur eine Mod im Spiel anzeigen.", 28, y0 + 452, 844, 110, true, 9.5f, false);

            // ---- page 3: account + memory ----
            accountLabel = Lbl(3, "", 28, y0, 844, 30, false, 12f, true);
            codeLabel = Lbl(3, "", 28, y0 + 34, 844, 60, false, 11f, true); codeLabel.ForeColor = Theme.Accent;
            Lbl(3, "Azure Client-ID  (deine eigene App, siehe unten)", 28, y0 + 104, 600, 20, false, 10f, true);
            PeachTextBox cid = Add(3, new PeachTextBox(520, 40, false)); cid.Location = new Point(28, y0 + 130); cid.Text = m.ClientId; cid.Placeholder = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";
            cid.TextChanged += delegate { if (loading) return; string t = cid.Text.Trim(); if (McAuth.ValidClientId(t) || t.Length == 0) { m.ClientId = t; cfg.Save(); } };
            signIn = Add(3, new PeachButton("Anmelden", BtnStyle.Primary, 150, 40)); signIn.Location = new Point(560, y0 + 130);
            signIn.Click += delegate { SignInClick(); };
            PeachButton signOut = Add(3, new PeachButton("Abmelden", BtnStyle.Secondary, 130, 40)); signOut.Location = new Point(720, y0 + 130);
            signOut.Click += delegate { m.RefreshToken = ""; m.AccountName = ""; m.AccountUuid = ""; cfg.Save(); RefreshAccount(); };
            Lbl(3, "Arbeitsspeicher für Minecraft", 28, y0 + 190, 400, 20, false, 10f, true);
            PeachSlider ram = Add(3, new PeachSlider(1, 16, Math.Max(1, Math.Min(16, m.RamMb / 1024)), " GB", 420)); ram.Location = new Point(28, y0 + 216);
            ram.ValueChanged += delegate { if (loading) return; m.RamMb = ram.Value * 1024; cfg.Save(); };
            Lbl(3, "So bekommst du eine Client-ID (einmalig, kostenlos):\n" +
                   "1.  portal.azure.com → Microsoft Entra ID → App-Registrierungen → Neue Registrierung. Kontotyp: „Nur persönliche Microsoft-Konten“, keine Umleitungs-URI.\n" +
                   "2.  In der App unter „Authentifizierung“ → „Öffentliche Clientflows zulassen“ auf Ja.\n" +
                   "3.  „Anwendungs-(Client-)ID“ kopieren und oben einfügen.\n" +
                   "4.  Mojang gibt den Minecraft-Zugang für neue Apps nur nach Antrag frei (aka.ms/AppRegInfo). Ohne diese Freigabe endet die Anmeldung beim letzten Schritt mit einem Fehler – dann funktioniert nur die Demo.\n" +
                   "Beim Anmelden gibst du einen Code auf microsoft.com ein; Peach bekommt nie dein Passwort. Das Merk-Token wird mit Windows-DPAPI verschlüsselt gespeichert.", 28, y0 + 266, 844, 230, true, 9.5f, false);
            PeachButton portal = Add(3, new PeachButton("Azure öffnen", BtnStyle.Secondary, 150, 40)); portal.Location = new Point(28, y0 + 506); portal.Click += delegate { try { Process.Start("https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade"); } catch { } };
            PeachButton apply = Add(3, new PeachButton("Antrag bei Mojang", BtnStyle.Secondary, 190, 40)); apply.Location = new Point(188, y0 + 506); apply.Click += delegate { try { Process.Start("https://aka.ms/AppRegInfo"); } catch { } };
            RefreshAccount();

            PeachButton done = new PeachButton("Fertig", BtnStyle.Primary, 130, 42); done.Location = new Point(900 - 28 - 130, 800 - 62);
            done.Click += delegate { Close(); };
            Controls.Add(done);

            ReloadProfiles();
            loading = false;
            ShowPage();
        }

        PeachButton Track(PeachButton b) { busyButtons.Add(b); return b; }

        static void SortWidgets(MinecraftConfig m)
        {
            m.Widgets.Sort(delegate (string a, string b) { return Array.IndexOf(OverlayModel.AllWidgets, a).CompareTo(Array.IndexOf(OverlayModel.AllWidgets, b)); });
        }

        void Changed() { cfg.Save(); if (overlayChanged != null) overlayChanged(); }

        void ShowPage() { for (int i = 0; i < pages.Length; i++) foreach (Control c in pages[i]) c.Visible = i == tabs.SelectedIndex; }
        public void ShowTab(int i) { tabs.SelectedIndex = i; }

        McProfile Selected()
        {
            int i = profiles.SelectedIndex;
            return i >= 0 && i < cfg.Minecraft.Profiles.Count ? cfg.Minecraft.Profiles[i] : null;
        }

        void ReloadProfiles()
        {
            profiles.Rows.Clear();
            foreach (McProfile p in cfg.Minecraft.Profiles)
            {
                int n = ModFolder.List(Minecraft.ModsDir(cfg, p)).Count;
                RowItem r = new RowItem(); r.Title = p.Name; r.Letter = "M"; r.Tint = Color.FromArgb(110, 180, 90);
                r.Subtitle = (p.Loader == "fabric" ? "Fabric" : "Vanilla") + "  ·  Minecraft " + p.McVersion + (p.Loader == "fabric" ? "  ·  " + n + " Mods" : "");
                profiles.Rows.Add(r);
            }
            profiles.RowsChanged();
            int sel = cfg.Minecraft.Profiles.FindIndex(delegate (McProfile p) { return p.Id == cfg.Minecraft.Selected; });
            if (sel < 0 && cfg.Minecraft.Profiles.Count > 0) sel = 0;
            if (sel >= 0) profiles.SelectRow(sel);
            ReloadMods();
        }

        void OnSelect()
        {
            if (loading) return;
            McProfile p = Selected();
            if (p != null && cfg.Minecraft.Selected != p.Id) { cfg.Minecraft.Selected = p.Id; cfg.Save(); }
            ReloadMods();
        }

        void ReloadMods()
        {
            McProfile p = Selected();
            mods.Rows.Clear();
            if (p == null) modsHeader.Text = "Lege zuerst ein Profil an (Reiter „Profile & Start“).";
            else if (p.Loader != "fabric") modsHeader.Text = "Das Profil „" + p.Name + "“ ist Vanilla – Mods brauchen ein Fabric-Profil.";
            else
            {
                modsHeader.Text = "Mods für „" + p.Name + "“  ·  Fabric, Minecraft " + p.McVersion;
                foreach (ModEntry e in ModFolder.List(Minecraft.ModsDir(cfg, p)))
                {
                    RowItem r = new RowItem(); r.Title = e.FileName; r.Letter = "J"; r.Tint = Color.FromArgb(240, 200, 80); r.Tag = e.FileName;
                    r.Subtitle = e.Enabled ? "an – wird beim Start geladen" : "aus"; r.HasToggle = true; r.On = e.Enabled;
                    r.Actions.Add(new RowAction("del", "Löschen", true));
                    mods.Rows.Add(r);
                }
            }
            mods.RowsChanged();
        }

        void ToggleMod(int i)
        {
            McProfile p = Selected(); if (p == null || i < 0 || i >= mods.Rows.Count) return;
            string name = mods.Rows[i].Tag as string;
            if (name != null) ModFolder.SetEnabled(Minecraft.ModsDir(cfg, p), name, mods.Rows[i].On);
            ReloadMods();
        }

        void RemoveMod(int i)
        {
            McProfile p = Selected(); if (p == null || i < 0 || i >= mods.Rows.Count) return;
            string name = mods.Rows[i].Tag as string; if (name == null) return;
            if (!Dialogs.Ask(this, "Mod löschen", "„" + name + "“ wirklich löschen?", "Löschen", "Abbrechen", true)) return;
            ModFolder.Delete(Minecraft.ModsDir(cfg, p), name);
            ReloadMods(); ReloadProfiles();
        }

        void OpenFolder(bool mods)
        {
            McProfile p = Selected(); if (p == null) return;
            string dir = mods ? Minecraft.ModsDir(cfg, p) : Minecraft.GameDir(cfg, p);
            try { Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        void RefreshAccount()
        {
            MinecraftConfig m = cfg.Minecraft;
            bool signed = McAuth.Unprotect(m.RefreshToken) != null && m.AccountName.Length > 0;
            accountLabel.Text = signed ? "Angemeldet als " + m.AccountName : "Nicht angemeldet";
            accountLabel.ForeColor = signed ? Theme.Ok : Theme.Muted;
            if (signIn != null) signIn.Text = signingIn ? "Abbrechen" : "Anmelden";
        }

        void SignInClick()
        {
            if (signingIn) { cancelSignIn = true; return; }
            MinecraftConfig m = cfg.Minecraft;
            if (!McAuth.ValidClientId(m.ClientId)) { Dialogs.Info(this, "Anmelden", "Trage zuerst deine Azure Client-ID ein (Anleitung unten auf dieser Seite). Sie sieht so aus: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"); return; }
            signingIn = true; cancelSignIn = false; RefreshAccount(); codeLabel.Text = "Verbinde mit Microsoft …";
            string id = m.ClientId;
            Async<McSession>(delegate
            {
                DeviceCode dc = McAuth.StartDeviceCode(id);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        codeLabel.Text = "Gehe auf " + dc.VerificationUri + " und gib diesen Code ein:   " + dc.UserCode + "   (liegt in der Zwischenablage)";
                        try { Clipboard.SetText(dc.UserCode); } catch { }
                        try { Process.Start(dc.VerificationUri); } catch { }
                    }));
                }
                catch { }
                DateTime end = DateTime.Now.AddSeconds(dc.ExpiresIn);
                MsTokens t = null;
                while (!cancelSignIn && DateTime.Now < end)
                {
                    Thread.Sleep(dc.Interval * 1000);
                    if (cancelSignIn) break;
                    t = McAuth.PollToken(id, dc.Code);
                    if (t != null) break;
                }
                if (t == null) throw new InvalidOperationException(cancelSignIn ? "Anmeldung abgebrochen." : "Der Code ist abgelaufen. Bitte noch einmal anmelden.");
                McSession s = McAuth.ToMinecraft(t.Access);
                if (string.IsNullOrEmpty(t.Refresh)) throw new InvalidOperationException("Microsoft hat kein Merk-Token geliefert.");
                m.RefreshToken = McAuth.Protect(t.Refresh); m.AccountName = s.Name; m.AccountUuid = s.Uuid; cfg.Save();
                return s;
            }, delegate (McSession s, Exception ex)
            {
                signingIn = false;
                if (ex != null) { codeLabel.Text = ex.Message; codeLabel.ForeColor = Theme.Danger; }
                else { codeLabel.Text = "Erfolgreich angemeldet."; codeLabel.ForeColor = Theme.Ok; }
                RefreshAccount();
            });
        }

        void Play(bool demo)
        {
            McProfile p = Selected();
            if (p == null) { Dialogs.Info(this, "Minecraft", "Lege zuerst ein Profil an."); return; }
            if (busy) return;
            MinecraftConfig m = cfg.Minecraft;
            bool signed = McAuth.Unprotect(m.RefreshToken) != null && McAuth.ValidClientId(m.ClientId);
            if (!demo && !signed) { tabs.SelectedIndex = 3; status.Text = "Melde dich zuerst unter „Konto“ an – oder starte die Demo (Reiter „Profile & Start“)."; status.ForeColor = Theme.Muted; return; }
            SetBusy(true, "Starte …");
            Action<string> prog = delegate (string s) { try { BeginInvoke(new Action(delegate { status.Text = s; })); } catch { } };
            Async<Process>(delegate
            {
                McSession s = null;
                if (!demo) { prog("Melde bei Microsoft an …"); s = McAuth.SignInFromStored(cfg); }
                McPlan plan = Minecraft.Prepare(cfg, p, true, prog);
                string problem; string java = Minecraft.FindJava(plan.JavaMajor, out problem);
                if (java == null) throw new InvalidOperationException(problem);
                prog("Starte Minecraft …");
                McContext ctx = new McContext { GameDir = Minecraft.GameDir(cfg, p), RamMb = m.RamMb, Demo = demo };
                if (s != null) { ctx.PlayerName = s.Name; ctx.Uuid = s.Uuid; ctx.AccessToken = s.AccessToken; ctx.UserType = "msa"; }
                else { ctx.PlayerName = "PeachDemo"; ctx.UserType = "legacy"; }
                return Minecraft.Launch(cfg, p, plan, ctx, java);
            }, delegate (Process pr, Exception ex)
            {
                SetBusy(false, null);
                if (ex != null) { status.Text = "Fehler: " + ex.Message; status.ForeColor = Theme.Danger; return; }
                cfg.Save();
                status.Text = "Minecraft startet … (Log: " + Path.Combine(Minecraft.GameDir(cfg, p), "logs", "peach-launch.log") + ")"; status.ForeColor = Theme.Ok;
                pr.Exited += delegate
                {
                    int code = -1; try { code = pr.ExitCode; } catch { }
                    try { BeginInvoke(new Action(delegate { if (!IsDisposed) { status.Text = code == 0 ? "Minecraft wurde beendet." : "Minecraft wurde mit Fehler beendet (Code " + code + "). Das Log steht im Profilordner unter logs\\peach-launch.log."; status.ForeColor = code == 0 ? Theme.Muted : Theme.Danger; } })); } catch { }
                };
            });
        }

        void DeleteProfile()
        {
            McProfile p = Selected(); if (p == null) return;
            if (!Dialogs.Ask(this, "Profil löschen", "Das Profil „" + p.Name + "“ entfernen?\n\nDer Spielordner mit Mods und Welten (" + Minecraft.GameDir(cfg, p) + ") wird dabei mitgelöscht. Das lässt sich nicht rückgängig machen.", "Löschen", "Abbrechen", true)) return;
            Minecraft.Uninstall(cfg, p);
            cfg.Minecraft.Profiles.Remove(p); if (cfg.Minecraft.Selected == p.Id) cfg.Minecraft.Selected = null;
            cfg.Save(); ReloadProfiles();
        }

        void NewProfile()
        {
            if (busy) return;
            McProfile np = null;
            using (PeachForm f = new PeachForm("Neues Minecraft-Profil", 620, 640))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Name", 30, PeachForm.TitleH + 20, 560, 22, true, 9.5f, true);
                PeachTextBox name = new PeachTextBox(560, 40, false); name.Location = new Point(30, PeachForm.TitleH + 46); name.Text = "Mein Profil";
                f.AddLabel("Minecraft-Version  (anklicken oder eintippen, z. B. 1.21.1)", 30, PeachForm.TitleH + 100, 560, 22, true, 9.5f, true);
                PeachTextBox ver = new PeachTextBox(560, 40, false); ver.Location = new Point(30, PeachForm.TitleH + 126);
                PeachList vl = new PeachList(); vl.RowHeight = 40; vl.Location = new Point(26, PeachForm.TitleH + 178); vl.Size = new Size(568, 220); vl.EmptyText = "Lade Versionen …";
                List<string> versions = new List<string>();
                vl.SelectionChanged += delegate { int i = vl.SelectedIndex; if (i >= 0 && i < versions.Count) ver.Text = versions[i]; };
                Async<List<string>>(delegate { return Minecraft.ParseReleases(Web.Get("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", 15000), 40); },
                    delegate (List<string> r, Exception ex)
                    {
                        if (f.IsDisposed) return;
                        if (r != null && r.Count > 0) { versions.AddRange(r); if (ver.Text.Length == 0) ver.Text = r[0]; }
                        else vl.EmptyText = "Versionsliste nicht erreichbar – Version einfach eintippen.";
                        foreach (string v in versions) { RowItem ri = new RowItem(); ri.Title = v; ri.Letter = "▪"; vl.Rows.Add(ri); }
                        vl.RowsChanged();
                    });
                f.AddLabel("Art", 30, PeachForm.TitleH + 416, 560, 22, true, 9.5f, true);
                PeachSegmented kind = new PeachSegmented(new string[] { "Fabric (mit Mods)", "Vanilla" }, 0, 560); kind.Location = new Point(30, PeachForm.TitleH + 442);
                f.Controls.Add(name); f.Controls.Add(ver); f.Controls.Add(vl); f.Controls.Add(kind);
                PeachButton ok = new PeachButton("Anlegen", BtnStyle.Primary, 140, 40); ok.Location = new Point(620 - 30 - 140, 640 - 62);
                ok.Click += delegate
                {
                    string v = ver.Text.Trim();
                    if (name.Text.Trim().Length == 0) { Dialogs.Info(f, "Profil", "Bitte einen Namen eingeben."); return; }
                    if (!Minecraft.ValidVersion(v)) { Dialogs.Info(f, "Profil", "Bitte eine Release-Version wie 1.21.1 angeben."); return; }
                    np = new McProfile { Name = name.Text.Trim(), McVersion = v, Loader = kind.SelectedIndex == 0 ? "fabric" : "vanilla" };
                    f.DialogResult = DialogResult.OK;
                };
                PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(620 - 30 - 140 - 10 - 120, 640 - 62); cancel.Result = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                if (f.ShowDialog(this) != DialogResult.OK || np == null) return;
            }
            np.Id = Minecraft.MakeId(np.Name, cfg.Minecraft.Profiles);
            McProfile created = np;
            SetBusy(true, "Lege „" + created.Name + "“ an …");
            Async<string>(delegate { return Minecraft.Install(cfg, created); },
                delegate (string versionId, Exception ex)
                {
                    SetBusy(false, null);
                    if (ex != null) { status.Text = "Fehler: " + ex.Message; status.ForeColor = Theme.Danger; return; }
                    created.VersionId = versionId;
                    cfg.Minecraft.Profiles.Add(created); cfg.Minecraft.Selected = created.Id; cfg.Save();
                    ReloadProfiles();
                    status.Text = "Profil angelegt. Beim ersten Start lädt Peach die Spieldateien (ein paar hundert MB)."; status.ForeColor = Theme.Ok;
                });
        }

        void SearchMods()
        {
            McProfile p = Selected();
            if (p == null) { Dialogs.Info(this, "Mods", "Lege zuerst ein Profil an."); return; }
            if (p.Loader != "fabric") { Dialogs.Info(this, "Mods", "Mods gibt es nur für Fabric-Profile."); return; }
            using (PeachForm f = new PeachForm("Mods von Modrinth – " + p.Name, 780, 700))
            {
                f.ShowInTaskbar = false;
                PeachTextBox q = new PeachTextBox(520, 40, false); q.Location = new Point(28, PeachForm.TitleH + 20); q.Placeholder = "Suchbegriff, z. B. sodium, minimap, litematica";
                PeachButton go = new PeachButton("Suchen", BtnStyle.Primary, 120, 40); go.Location = new Point(560, PeachForm.TitleH + 20);
                PeachList res = new PeachList(); res.RowHeight = 62; res.Location = new Point(24, PeachForm.TitleH + 76); res.Size = new Size(732, 400); res.EmptyText = "Suche starten, um Mods zu finden (passend zu Fabric " + p.McVersion + ").";
                Label st = f.AddLabel("", 28, PeachForm.TitleH + 488, 724, 46, true, 9.5f, false);
                PeachButton inst = new PeachButton("Installieren", BtnStyle.Primary, 150, 42); inst.Location = new Point(28, 700 - 62);
                PeachButton close = new PeachButton("Schließen", BtnStyle.Secondary, 130, 42); close.Location = new Point(780 - 28 - 130, 700 - 62); close.Click += delegate { f.Close(); };
                List<ModHit> hits = new List<ModHit>();
                Action doSearch = delegate
                {
                    st.Text = "Suche …"; st.ForeColor = Theme.Muted;
                    string url = Modrinth.SearchUrl(q.Text.Trim(), p.McVersion, "fabric", 25);
                    Async<List<ModHit>>(delegate { return Modrinth.ParseSearch(Web.Get(url, 15000)); },
                        delegate (List<ModHit> r, Exception ex)
                        {
                            if (f.IsDisposed) return;
                            res.Rows.Clear(); hits.Clear();
                            if (ex != null) { st.Text = "Modrinth ist nicht erreichbar: " + ex.Message; st.ForeColor = Theme.Danger; res.RowsChanged(); return; }
                            hits.AddRange(r);
                            foreach (ModHit h in hits)
                            {
                                RowItem ri = new RowItem(); ri.Title = h.Title; ri.Letter = h.Title.Substring(0, 1).ToUpperInvariant();
                                ri.Subtitle = (h.Description ?? "").Length > 90 ? h.Description.Substring(0, 90) + "…" : (h.Description ?? "");
                                ri.Status = FormatDownloads(h.Downloads); ri.Chips.Add(h.Author ?? "");
                                res.Rows.Add(ri);
                            }
                            res.RowsChanged();
                            st.Text = hits.Count == 0 ? "Nichts gefunden." : hits.Count + " Treffer. Mod auswählen und „Installieren“ klicken."; st.ForeColor = Theme.Muted;
                        });
                };
                go.Click += delegate { doSearch(); };
                bool installing = false;
                inst.Click += delegate
                {
                    int i = res.SelectedIndex;
                    if (installing || i < 0 || i >= hits.Count) { if (i < 0) { st.Text = "Wähle zuerst eine Mod aus der Liste."; } return; }
                    ModHit h = hits[i]; installing = true; inst.Enabled = false;
                    string dir = Minecraft.ModsDir(cfg, p);
                    Async<List<string>>(delegate { return Modrinth.Install(h.Id, p.McVersion, "fabric", dir, delegate (string s) { try { f.BeginInvoke(new Action(delegate { st.Text = s; st.ForeColor = Theme.Muted; })); } catch { } }); },
                        delegate (List<string> r, Exception ex)
                        {
                            installing = false; if (f.IsDisposed) return; inst.Enabled = true;
                            if (ex != null) { st.Text = "Fehler: " + ex.Message; st.ForeColor = Theme.Danger; return; }
                            st.Text = r.Count == 0 ? "„" + h.Title + "“ ist schon installiert." : "Installiert: " + string.Join(", ", r.ToArray()) + "  (inkl. benötigter Mods)"; st.ForeColor = Theme.Ok;
                        });
                };
                f.Controls.Add(q); f.Controls.Add(go); f.Controls.Add(res); f.Controls.Add(inst); f.Controls.Add(close);
                f.ShowDialog(this);
            }
            ReloadProfiles();
        }

        static string FormatDownloads(long n) { return n >= 1000000 ? (n / 1000000.0).ToString("0.0") + " Mio ⬇" : (n >= 1000 ? (n / 1000) + " Tsd ⬇" : n + " ⬇"); }
    }
}
