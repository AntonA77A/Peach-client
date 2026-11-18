using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Peach
{
    // One live meter: big percentage, a bar and the last minute as a line.
    internal class StatTile : Control
    {
        public string Title = "", Detail = "";
        public double Value = -1;                       // -1 = not available
        readonly List<double> history = new List<double>();

        public StatTile(string title, int w, int h)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Title = title; Size = new Size(w, h);
        }

        public void Push(double v, string detail)
        {
            Value = v; Detail = detail;
            history.Add(Math.Max(0, v)); if (history.Count > 60) history.RemoveAt(0);
            Invalidate();
        }

        public static Color LoadColor(double v) { return v < 0 ? Theme.Muted : (v < 65 ? Theme.Ok : (v < 88 ? Theme.Accent : Theme.Danger)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color c = LoadColor(Value);
            using (GraphicsPath p = Theme.Round(r, 12))
            {
                using (Brush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
                Region old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                if (history.Count > 1)
                {
                    List<PointF> pts = new List<PointF>();
                    float step = (Width - 2) / 59f, baseY = Height - 8, span = Height * 0.5f;
                    for (int i = 0; i < history.Count; i++) pts.Add(new PointF(Width - 1 - (history.Count - 1 - i) * step, baseY - (float)(history[i] / 100.0) * span));
                    List<PointF> fill = new List<PointF>(pts); fill.Add(new PointF(pts[pts.Count - 1].X, Height)); fill.Insert(0, new PointF(pts[0].X, Height));
                    using (Brush b = new SolidBrush(Theme.WithAlpha(c, 38))) g.FillPolygon(b, fill.ToArray());
                    using (Pen pen = new Pen(Theme.WithAlpha(c, 190), 1.6f)) g.DrawLines(pen, pts.ToArray());
                }
                g.Clip = old;
            }
            using (Brush b = new SolidBrush(Theme.Muted)) g.DrawString(Title, Theme.Font(9f, true), b, 16, 12);
            string big = Value < 0 ? "–" : Math.Round(Value).ToString("0") + " %";
            using (Brush b = new SolidBrush(Value < 0 ? Theme.Muted : Theme.Ink)) g.DrawString(big, Theme.Font(24f, true), b, 14, 30);
            using (Brush b = new SolidBrush(Theme.Muted)) g.DrawString(Value < 0 && Detail.Length == 0 ? "nicht verfügbar" : Detail, Theme.Font(8.5f, false), b, 16, 76);
            Rectangle bar = new Rectangle(16, Height - 22, Width - 33, 6);
            using (GraphicsPath bp = Theme.Round(bar, 3)) using (Brush b = new SolidBrush(Theme.Bg)) g.FillPath(b, bp);
            if (Value > 0)
            {
                Rectangle fillR = new Rectangle(bar.X, bar.Y, Math.Max(6, (int)(bar.Width * Math.Min(100, Value) / 100.0)), bar.Height);
                using (GraphicsPath bp = Theme.Round(fillR, 3)) using (Brush b = new SolidBrush(c)) g.FillPath(b, bp);
            }
        }
    }

    internal class GameBoostForm : PeachForm
    {
        readonly Config cfg;
        readonly GameBooster booster;
        readonly StatsSampler sampler = new StatsSampler();
        readonly Timer timer = new Timer();
        readonly StatTile cpu, gpu, ram;
        readonly Label status;
        readonly PeachButton boostBtn;
        readonly PeachSegmented tabs;
        readonly List<Control>[] pages = { new List<Control>(), new List<Control>(), new List<Control>() };
        readonly PeachList games = new PeachList(), apps = new PeachList();
        readonly Action changedHandler;
        bool loading = true;

        T Add<T>(int page, T c) where T : Control { pages[page].Add(c); Controls.Add(c); return c; }
        Label Lbl(int page, string text, int x, int y, int w, int h, bool muted, float size, bool bold) { Label l = AddLabel(text, x, y, w, h, muted, size, bold); pages[page].Add(l); return l; }

        public GameBoostForm(Config cfg, GameBooster booster) : base("Game-Boost", 880, 760)
        {
            this.cfg = cfg; this.booster = booster;
            int y0 = TitleH + 20;

            cpu = new StatTile("PROZESSOR (CPU)", 262, 118); cpu.Location = new Point(28, y0);
            gpu = new StatTile("GRAFIKKARTE (GPU)", 262, 118); gpu.Location = new Point(308, y0);
            ram = new StatTile("ARBEITSSPEICHER (RAM)", 262, 118); ram.Location = new Point(588, y0);
            Controls.Add(cpu); Controls.Add(gpu); Controls.Add(ram);

            status = AddLabel("", 28, y0 + 140, 600, 50, false, 10.5f, false);
            boostBtn = new PeachButton("Boost starten", BtnStyle.Primary, 210, 46); boostBtn.Location = new Point(880 - 28 - 210, y0 + 138);
            boostBtn.Click += delegate
            {
                if (this.booster.Active) this.booster.Restore();
                else this.booster.Boost(null, false);
            };
            Controls.Add(boostBtn);

            tabs = new PeachSegmented(new string[] { "Einstellungen", "Spiele", "Apps schließen" }, 0, 520);
            tabs.Location = new Point(28, y0 + 204);
            tabs.SelectedIndexChanged += delegate { ShowPage(); };
            Controls.Add(tabs);
            int py = y0 + 258;

            // ---- page 0 ----
            PeachToggle auto = Add(0, new PeachToggle(cfg.GameBoost.Auto, "Automatisch boosten, wenn ein Spiel aus der Liste läuft")); auto.Location = new Point(28, py);
            auto.CheckedChanged += delegate { if (loading) return; cfg.GameBoost.Auto = auto.Checked; cfg.Save(); };
            PeachToggle plan = Add(0, new PeachToggle(cfg.GameBoost.PowerPlan, "Energiesparplan auf „Höchstleistung“ stellen")); plan.Location = new Point(28, py + 44);
            plan.CheckedChanged += delegate { if (loading) return; cfg.GameBoost.PowerPlan = plan.Checked; cfg.Save(); };
            PeachToggle prio = Add(0, new PeachToggle(cfg.GameBoost.Priority, "Spiel mit hoher Priorität laufen lassen")); prio.Location = new Point(28, py + 88);
            prio.CheckedChanged += delegate { if (loading) return; cfg.GameBoost.Priority = prio.Checked; cfg.Save(); };
            Lbl(0, "So funktioniert es – ehrlich gesagt:\n" +
                   "•  Peach holt nichts aus der Hardware, was nicht da ist. Der Boost sorgt dafür, dass Windows nichts ausbremst: Höchstleistung statt Energiesparen, das Spiel bekommt vor anderen Programmen Rechenzeit, und Programme, die du unter „Apps schließen“ einträgst, werden höflich zum Schließen aufgefordert.\n" +
                   "•  Nach dem Spiel (oder mit „Boost beenden“) wird alles zurückgestellt: Energieplan und Priorität. Falls Peach dazwischen beendet wird, stellt es den alten Energieplan beim nächsten Start wieder her.\n" +
                   "•  Peach beendet nie Windows-Prozesse oder Sicherheitsprogramme und erzwingt kein Beenden – ungespeicherte Arbeit bleibt geschützt (das Programm fragt selbst nach).", 28, py + 142, 826, 230, true, 10f, false);

            // ---- page 1: games ----
            Lbl(1, "Spiele, bei denen Peach automatisch boostet (wenn „Automatisch“ an ist und das Spiel im Vordergrund läuft).", 28, py, 826, 22, true, 10f, false);
            SetupList(games, 1, py + 30, "Noch keine Spiele.\nMit „Hinzufügen“ ein laufendes Spiel auswählen.", cfg.GameBoost.Games, "Spiel hinzufügen");
            // ---- page 2: apps to close ----
            Lbl(2, "Programme, die beim Boost höflich zum Schließen aufgefordert werden (z. B. Browser, Launcher). Sie werden nie zwangsweise beendet.", 28, py, 826, 22, true, 10f, false);
            SetupList(apps, 2, py + 30, "Noch keine Programme.\nMit „Hinzufügen“ ein laufendes Programm auswählen.", cfg.GameBoost.CloseApps, "Programm hinzufügen");

            PeachButton done = new PeachButton("Fertig", BtnStyle.Primary, 130, 42); done.Location = new Point(880 - 28 - 130, 760 - 62);
            done.Click += delegate { Close(); };
            Controls.Add(done);

            changedHandler = delegate { if (IsHandleCreated) BeginInvoke(new Action(RefreshState)); };
            booster.Changed += changedHandler;
            ReloadLists();
            RefreshState();
            loading = false;
            ShowPage();

            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
            Shown += delegate { Tick(); timer.Start(); };
        }

        void SetupList(PeachList list, int page, int y, string empty, List<string> data, string dialogTitle)
        {
            list.RowHeight = 54; list.Location = new Point(24, y); list.Size = new Size(826, 270); list.EmptyText = empty;
            Add(page, list);
            PeachButton add = Add(page, new PeachButton("Hinzufügen", BtnStyle.Primary, 136, 40)); add.Location = new Point(28, y + 282);
            add.Click += delegate
            {
                string exe = PickProcess(this, dialogTitle);
                if (exe == null) return;
                foreach (string d in data) if (BoostLogic.NormalizeExe(d) == exe) return;
                data.Add(exe); cfg.Save(); ReloadLists();
            };
            PeachButton del = Add(page, new PeachButton("Entfernen", BtnStyle.Danger, 120, 40)); del.Location = new Point(174, y + 282);
            del.Click += delegate
            {
                int i = list.SelectedIndex;
                if (i < 0 || i >= data.Count) return;
                data.RemoveAt(i); cfg.Save(); ReloadLists();
            };
        }

        void ReloadLists()
        {
            Fill(games, cfg.GameBoost.Games, "▶");
            Fill(apps, cfg.GameBoost.CloseApps, "✕");
        }

        static void Fill(PeachList list, List<string> data, string letter)
        {
            list.Rows.Clear();
            foreach (string s in data)
            {
                RowItem r = new RowItem(); r.Title = s; r.Letter = letter;
                r.Subtitle = Process.GetProcessesByName(BoostLogic.ProcName(BoostLogic.NormalizeExe(s) ?? s)).Length > 0 ? "läuft gerade" : "läuft nicht";
                list.Rows.Add(r);
            }
            list.RowsChanged();
        }

        void ShowPage() { for (int i = 0; i < pages.Length; i++) foreach (Control c in pages[i]) c.Visible = i == tabs.SelectedIndex; }
        public void ShowTab(int i) { tabs.SelectedIndex = i; }

        void RefreshState()
        {
            if (IsDisposed) return;
            boostBtn.Text = booster.Active ? "Boost beenden" : "Boost starten";
            if (booster.Active)
            {
                status.Text = "BOOST AN" + (booster.Game != null ? "  ·  " + booster.Game : "  ·  von Hand gestartet") + "\n" +
                              (booster.PlanName != null ? "Energieplan: " + booster.PlanName + "   " : "") + (booster.Closed > 0 ? booster.Closed + " Programm(e) gebeten sich zu schließen" : "");
                status.ForeColor = Theme.Ok;
            }
            else
            {
                status.Text = "Boost aus\nMit „Boost starten“ jetzt von Hand, oder automatisch bei deinen Spielen (Reiter „Spiele“).";
                status.ForeColor = Theme.Muted;
            }
            boostBtn.Invalidate();
        }

        void Tick()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            SystemStats s = sampler.Sample(true);
            Feed(s);
        }

        public void Feed(SystemStats s)
        {
            cpu.Push(s.Cpu, "Auslastung gesamt");
            gpu.Push(s.Gpu, s.Gpu < 0 ? "nicht verfügbar" : "3D-Auslastung");
            ram.Push(s.RamPct, s.RamUsedMb > 0 ? (s.RamUsedMb / 1024.0).ToString("0.0") + " von " + (s.RamTotalMb / 1024.0).ToString("0.0") + " GB belegt" : "");
        }

        // A small dialog: pick one of the running programs (with a window) or type a program name. Returns "name.exe" or null.
        public static string PickProcess(IWin32Window owner, string title)
        {
            using (PeachForm f = new PeachForm(title, 620, 620))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Laufendes Programm anklicken oder den Namen eintippen (z. B. spiel.exe)", 30, PeachForm.TitleH + 20, 560, 22, true, 9.5f, true);
                PeachList list = new PeachList(); list.RowHeight = 46; list.Location = new Point(26, PeachForm.TitleH + 50); list.Size = new Size(568, 330);
                list.EmptyText = "Keine Programme mit Fenster gefunden.";
                SortedDictionary<string, string> found = new SortedDictionary<string, string>();
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0) continue;
                        string exe = BoostLogic.NormalizeExe(p.ProcessName);
                        if (exe == null || BoostLogic.IsProtected(exe) || found.ContainsKey(exe)) continue;
                        found[exe] = p.MainWindowTitle;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                PeachTextBox name = new PeachTextBox(568, 40, false); name.Location = new Point(26, PeachForm.TitleH + 400);
                List<string> keys = new List<string>(found.Keys);
                foreach (string k in keys)
                {
                    RowItem r = new RowItem(); r.Title = k; r.Letter = k.Substring(0, 1).ToUpperInvariant();
                    r.Subtitle = found[k].Length > 70 ? found[k].Substring(0, 70) + "…" : found[k];
                    list.Rows.Add(r);
                }
                list.RowsChanged();
                list.SelectionChanged += delegate { int i = list.SelectedIndex; if (i >= 0 && i < keys.Count) name.Text = keys[i]; };
                f.Controls.Add(list); f.Controls.Add(name);
                string result = null;
                PeachButton ok = new PeachButton("Hinzufügen", BtnStyle.Primary, 140, 40); ok.Location = new Point(620 - 30 - 140, 620 - 62);
                ok.Click += delegate
                {
                    string exe = BoostLogic.NormalizeExe(name.Text);
                    if (exe == null) { Dialogs.Info(f, title, "Bitte einen gültigen Programmnamen angeben (nur der Name, z. B. spiel.exe)."); return; }
                    if (BoostLogic.IsProtected(exe)) { Dialogs.Info(f, title, "Windows-Prozesse, Sicherheitsprogramme und Peach selbst bleiben unberührt und können nicht eingetragen werden."); return; }
                    result = exe; f.DialogResult = DialogResult.OK;
                };
                PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(620 - 30 - 140 - 10 - 120, 620 - 62); cancel.Result = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                return f.ShowDialog(owner) == DialogResult.OK ? result : null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { booster.Changed -= changedHandler; timer.Dispose(); sampler.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
