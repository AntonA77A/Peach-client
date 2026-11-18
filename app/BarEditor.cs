using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Peach
{
    // Mock desktop with the bar on it, redrawn on every change: what you set is what you see.
    internal class BarPreview : Control
    {
        readonly Config cfg;
        readonly List<WinInfo> demo = new List<WinInfo>();

        public BarPreview(Config cfg, int width, int height)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.cfg = cfg; Size = new Size(width, height);
            try
            {
                foreach (WinInfo w in WindowList.Snapshot((uint)System.Diagnostics.Process.GetCurrentProcess().Id))
                {
                    demo.Add(w);
                    if (demo.Count == 4) break;
                }
            }
            catch { }
            if (demo.Count == 0)
            {
                string[] names = { "Discord", "Spotify – Chill Mix", "Downloads" };
                for (int i = 0; i < names.Length; i++) demo.Add(new WinInfo { Hwnd = new IntPtr(2000 + i), Title = names[i] });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath clip = Theme.Round(r, 12))
            {
                g.SetClip(clip);
                using (Brush b = new LinearGradientBrush(r, Color.FromArgb(70, 52, 74), Color.FromArgb(120, 70, 66), 35f)) g.FillRectangle(b, r);
                using (Brush w = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                {
                    using (GraphicsPath p = Theme.Round(new Rectangle(40, 30, 300, 76), 8)) g.FillPath(w, p);
                    using (GraphicsPath p = Theme.Round(new Rectangle(Width - 330, 56, 270, 70), 8)) g.FillPath(w, p);
                }
                BarConfig c = cfg.Bar;
                int bh = c.Height;
                using (Bitmap bar = new Bitmap(Width, bh))
                {
                    using (Graphics bg = Graphics.FromImage(bar))
                        BarForm.Draw(bg, Width, bh, c, demo, demo.Count > 0 ? demo[0].Hwnd : IntPtr.Zero, -1);
                    ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = Math.Max(30, Math.Min(100, c.Opacity)) / 100f;
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        int y = c.Position == "top" ? 0 : Height - bh;
                        g.DrawImage(bar, new Rectangle(0, y, Width, bh), 0, 0, Width, bh, GraphicsUnit.Pixel, ia);
                    }
                }
                g.ResetClip();
            }
            using (Pen pen = new Pen(Theme.Border)) using (GraphicsPath p = Theme.Round(r, 12)) g.DrawPath(pen, p);
        }
    }

    internal class Swatch : Control
    {
        public Color Value = Color.Black;
        public Swatch(Color c) { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Value = c; Size = new Size(40, 40); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 9))
            {
                using (Brush b = new SolidBrush(Value)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border, 1.5f)) g.DrawPath(pen, p);
            }
        }
    }

    // Colour schemes: left half = background, right half = accent.
    internal class PresetPicker : Control
    {
        public static readonly string[] Names = { "Peach", "Mitternacht", "Wald", "Violett", "Hell" };
        public static readonly string[] Backgrounds = { "#261C19", "#10131C", "#14201A", "#1F1630", "#F4E6DD" };
        public static readonly string[] Accents = { "#FF7A59", "#6AA3FF", "#5FD08A", "#B68CFF", "#E8643C" };
        readonly Func<string> bg, accent;
        int hot = -1;
        public event Action<int> Picked;

        public PresetPicker(Func<string> bg, Func<string> accent, int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.bg = bg; this.accent = accent; Size = new Size(width, 56); Cursor = Cursors.Hand;
        }

        Rectangle Dot(int i) { return new Rectangle(i * 66, 0, 40, 40); }
        int Hit(Point p) { for (int i = 0; i < Names.Length; i++) if (Dot(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0 && Picked != null) Picked(h); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            for (int i = 0; i < Names.Length; i++)
            {
                Rectangle d = Dot(i);
                Color b = BarLogic.ParseColor(Backgrounds[i], Color.Black), a = BarLogic.ParseColor(Accents[i], Color.White);
                using (GraphicsPath p = new GraphicsPath()) { p.AddEllipse(d); g.SetClip(p); }
                using (Brush bb = new SolidBrush(b)) g.FillEllipse(bb, d);
                using (Brush ab = new SolidBrush(a)) g.FillPie(ab, d, -90, 180);
                g.ResetClip();
                bool selected = string.Equals(bg(), Backgrounds[i], StringComparison.OrdinalIgnoreCase) && string.Equals(accent(), Accents[i], StringComparison.OrdinalIgnoreCase);
                using (Pen pen = new Pen(selected ? Theme.Accent : (i == hot ? Theme.Muted : Theme.Border), selected ? 3f : 1.5f)) g.DrawEllipse(pen, d.X, d.Y, d.Width - 1, d.Height - 1);
                using (Brush t = new SolidBrush(selected ? Theme.Ink : Theme.Muted))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(Names[i], Theme.Font(8.5f, selected), t, new RectangleF(d.X - 12, d.Bottom + 4, d.Width + 24, 16), sf);
            }
        }
    }

    // "Leiste anpassen": every change is saved and applied to the real bar immediately.
    internal class BarEditor : PeachForm
    {
        readonly Config cfg;
        readonly Action changed;
        readonly BarPreview preview;
        readonly PeachTextBox hexBg, hexAccent;
        readonly Swatch swBg, swAccent;
        readonly PeachButton pinned;
        bool loading = true;

        public BarEditor(Config cfg, Action changed) : base("Leiste anpassen", 780, 770)
        {
            this.cfg = cfg; this.changed = changed;
            BarConfig b = cfg.Bar;

            PeachToggle on = new PeachToggle(b.Enabled, "Peach-Leiste anzeigen"); on.Location = new Point(28, TitleH + 16);
            on.CheckedChanged += delegate { cfg.Bar.Enabled = on.Checked; Apply(); };
            Controls.Add(on);
            AddLabel("Wirkt sofort · im Controller-Modus ausgeblendet", 300, TitleH + 20, 452, 22, true, 9.5f, false).TextAlign = ContentAlignment.MiddleRight;

            AddLabel("VORSCHAU", 28, TitleH + 66, 200, 18, true, 8.5f, true);
            preview = new BarPreview(cfg, 724, 140); preview.Location = new Point(28, TitleH + 88);
            Controls.Add(preview);

            int y0 = TitleH + 252;
            AddLabel("AUSSEHEN", 28, y0, 200, 18, true, 8.5f, true);
            AddLabel("Position", 28, y0 + 28, 200, 20, false, 10f, true);
            PeachSegmented pos = new PeachSegmented(new string[] { "Unten", "Oben" }, b.Position == "top" ? 1 : 0, 350); pos.Location = new Point(28, y0 + 52);
            pos.SelectedIndexChanged += delegate { cfg.Bar.Position = pos.SelectedIndex == 1 ? "top" : "bottom"; Apply(); };
            Controls.Add(pos);

            AddLabel("Höhe", 28, y0 + 100, 200, 20, false, 10f, true);
            PeachSlider height = new PeachSlider(28, 96, b.Height, " px", 350); height.Location = new Point(28, y0 + 124);
            height.ValueChanged += delegate { cfg.Bar.Height = height.Value; Apply(); };
            Controls.Add(height);

            AddLabel("Deckkraft", 28, y0 + 164, 200, 20, false, 10f, true);
            PeachSlider opacity = new PeachSlider(30, 100, b.Opacity, " %", 350); opacity.Location = new Point(28, y0 + 188);
            opacity.ValueChanged += delegate { cfg.Bar.Opacity = opacity.Value; Apply(); };
            Controls.Add(opacity);

            AddLabel("Farbschema", 28, y0 + 228, 200, 20, false, 10f, true);
            PresetPicker presets = new PresetPicker(delegate { return cfg.Bar.Color; }, delegate { return cfg.Bar.Accent; }, 350); presets.Location = new Point(28, y0 + 254);
            presets.Picked += delegate (int i) { SetColors(PresetPicker.Backgrounds[i], PresetPicker.Accents[i]); };
            Controls.Add(presets);

            AddLabel("Hintergrund", 28, y0 + 322, 160, 20, false, 10f, true);
            hexBg = new PeachTextBox(120, 40, false); hexBg.Location = new Point(28, y0 + 346); hexBg.Text = b.Color;
            swBg = new Swatch(BarLogic.ParseColor(b.Color, Color.Black)); swBg.Location = new Point(156, y0 + 346);
            AddLabel("Akzent", 214, y0 + 322, 160, 20, false, 10f, true);
            hexAccent = new PeachTextBox(120, 40, false); hexAccent.Location = new Point(214, y0 + 346); hexAccent.Text = b.Accent;
            swAccent = new Swatch(BarLogic.ParseColor(b.Accent, Color.Black)); swAccent.Location = new Point(342, y0 + 346);
            Controls.Add(hexBg); Controls.Add(swBg); Controls.Add(hexAccent); Controls.Add(swAccent);
            hexBg.TextChanged += delegate { HexChanged(hexBg, swBg, true); };
            hexAccent.TextChanged += delegate { HexChanged(hexAccent, swAccent, false); };
            swBg.Click += delegate { PickColor(swBg, hexBg); };
            swAccent.Click += delegate { PickColor(swAccent, hexAccent); };

            AddLabel("INHALT", 410, y0, 200, 18, true, 8.5f, true);
            int ty = y0 + 30;
            AddToggle("Peach-Knopf (öffnet das Menü)", 410, ty, b.ShowMenuButton, delegate (bool v) { cfg.Bar.ShowMenuButton = v; });
            AddToggle("Offene Fenster (zum Wechseln)", 410, ty + 44, b.ShowWindows, delegate (bool v) { cfg.Bar.ShowWindows = v; });
            AddToggle("Uhrzeit", 410, ty + 88, b.ShowClock, delegate (bool v) { cfg.Bar.ShowClock = v; });
            AddToggle("Datum", 410, ty + 132, b.ShowDate, delegate (bool v) { cfg.Bar.ShowDate = v; });

            pinned = new PeachButton("Angeheftete Apps bearbeiten…", BtnStyle.Secondary, 342, 44); pinned.Location = new Point(410, ty + 190);
            pinned.Click += delegate
            {
                using (AppEditor ed = new AppEditor(cfg, Apply, delegate { return cfg.Bar.Pinned; }, "Angeheftete Apps der Leiste",
                    "Diese Apps stehen als Knöpfe links in der Leiste. Programme, Verknüpfungen oder Ordner einfach ins Fenster ziehen – oder „Installierte Apps durchsuchen“ (Discord, Steam, Spotify …)."))
                    ed.ShowDialog(this);
                UpdatePinnedText();
            };
            Controls.Add(pinned);
            UpdatePinnedText();
            AddLabel("Tipp: Rechtsklick auf die Leiste → anpassen, ausblenden, Fenster schließen.", 410, ty + 250, 342, 40, true, 9.5f, false);

            PeachButton done = new PeachButton("Fertig", BtnStyle.Primary, 130, 42); done.Location = new Point(780 - 24 - 130, 770 - 64);
            done.Click += delegate { Close(); };
            Controls.Add(done);
            loading = false;
        }

        void UpdatePinnedText() { pinned.Text = "Angeheftete Apps bearbeiten…  (" + cfg.Bar.Pinned.Count + ")"; }

        void Apply() { if (loading) return; cfg.Save(); changed(); preview.Invalidate(); }

        void AddToggle(string text, int x, int y, bool value, Action<bool> set)
        {
            PeachToggle t = new PeachToggle(value, text); t.Location = new Point(x, y);
            t.CheckedChanged += delegate { set(t.Checked); Apply(); };
            Controls.Add(t);
        }

        void SetColors(string bg, string accent)
        {
            cfg.Bar.Color = bg; cfg.Bar.Accent = accent;
            loading = true;
            hexBg.Text = bg; hexAccent.Text = accent;
            swBg.Value = BarLogic.ParseColor(bg, Color.Black); swAccent.Value = BarLogic.ParseColor(accent, Color.Black);
            swBg.Invalidate(); swAccent.Invalidate();
            loading = false;
            Apply();
            Invalidate(true);
        }

        void HexChanged(PeachTextBox box, Swatch sw, bool isBg)
        {
            if (loading) return;
            string t = box.Text.Trim().TrimStart('#');
            if (t.Length != 6) return;
            Color c = BarLogic.ParseColor("#" + t, Color.Empty);
            if (c.IsEmpty) return;
            sw.Value = c; sw.Invalidate();
            if (isBg) cfg.Bar.Color = BarLogic.ToHex(c); else cfg.Bar.Accent = BarLogic.ToHex(c);
            Apply();
            Invalidate(true);
        }

        void PickColor(Swatch sw, PeachTextBox box)
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.Color = sw.Value; d.FullOpen = true;
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = BarLogic.ToHex(d.Color);
            }
        }
    }
}
