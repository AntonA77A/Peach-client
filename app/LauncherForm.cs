using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace Peach
{
    // Grid navigation for the launcher (pure logic, unit-tested).
    internal class LauncherModel
    {
        public int Count, Cols, Index;

        public LauncherModel(int count, int cols) { Count = Math.Max(1, count); Cols = Math.Max(1, cols); }

        public int Row { get { return Index / Cols; } }

        public void Move(PadKey k)
        {
            switch (k)
            {
                case PadKey.Left: if (Index > 0) Index--; break;
                case PadKey.Right: if (Index < Count - 1) Index++; break;
                case PadKey.Up: if (Index - Cols >= 0) Index -= Cols; break;
                case PadKey.Down:
                    if (Index + Cols < Count) Index += Cols;
                    else if (Index / Cols < (Count - 1) / Cols) Index = Count - 1;   // last, partly filled row
                    break;
            }
        }
    }

    // The "controller mode" home screen: full-screen tiles for the apps you added, plus a tile that
    // switches controller mode off again. Works with the controller, keyboard and mouse.
    internal class LauncherForm : Form
    {
        const int Tile = 210, TileH = 236, Gap = 26, Edge = 90, HeaderH = 140, FooterH = 84;
        static readonly Color Bg1 = Color.FromArgb(30, 22, 20), Bg2 = Color.FromArgb(54, 37, 33),
            TileBg = Color.FromArgb(66, 48, 43), TileSel = Color.FromArgb(96, 68, 60),
            Ink = Color.FromArgb(246, 233, 228), Muted = Color.FromArgb(165, 143, 136), Accent = Color.FromArgb(255, 122, 89);
        static readonly Color[] Palette = {
            Color.FromArgb(255, 122, 89), Color.FromArgb(240, 160, 90), Color.FromArgb(214, 96, 110),
            Color.FromArgb(120, 170, 120), Color.FromArgb(120, 150, 200), Color.FromArgb(170, 130, 190) };

        readonly Config cfg;
        readonly Action<AppEntry> launch;
        readonly Action exitMode;
        readonly Timer clock = new Timer();
        LauncherModel model;
        int firstRow;
        public volatile bool IsOpen;

        public LauncherForm(Config cfg, Action<AppEntry> launch, Action exitMode)
        {
            this.cfg = cfg; this.launch = launch; this.exitMode = exitMode;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = false;   // the launcher is the "desktop" of controller mode: apps open in front of it
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Bg1;
            Text = "Peach Launcher";
            model = new LauncherModel(1, 1);
            clock.Interval = 20000;
            clock.Tick += delegate { if (IsOpen) Invalidate(); };
            hwnd = Handle;   // so Input()/IsFront can be used from the controller thread
        }

        readonly IntPtr hwnd;

        // True while the launcher itself is the window you are looking at. Only then does the controller
        // navigate tiles; with an app in front the controller drives that app (mouse mode) instead.
        public bool IsFront { get { return IsOpen && Native.GetForegroundWindow() == hwnd; } }

        int ItemCount { get { return cfg.Apps.Count + 1; } }   // + the "exit controller mode" tile

        int ColsFor(int width) { return Math.Max(1, Math.Min(7, (width - 2 * Edge + Gap) / (Tile + Gap))); }
        int VisibleRows(int height) { return Math.Max(1, (height - HeaderH - FooterH + Gap) / (TileH + Gap)); }

        // Starts controller mode's home screen (full screen on the screen the cursor is on).
        public void ShowLauncher()
        {
            Bounds = Screen.FromPoint(Cursor.Position).Bounds;
            firstRow = 0;
            model = null;
            ReloadModel();
            IsOpen = true;
            clock.Start();
            BringFront();
        }

        // Home button: put the launcher in front of whatever app is running (the app keeps running behind it).
        public void BringFront()
        {
            if (!IsOpen) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            ReloadModel();
            Native.ForceForeground(Handle);
            Activate();
            Invalidate();
        }

        // The app list can change in the editor while the launcher sits in the background.
        void ReloadModel()
        {
            int keep = model == null ? 0 : model.Index;
            model = new LauncherModel(ItemCount, ColsFor(Width));
            model.Index = Math.Min(keep, ItemCount - 1);
            firstRow = 0;
            KeepSelectionVisible();
        }

        public void HideLauncher()
        {
            IsOpen = false;
            clock.Stop();
            Hide();
        }

        // Safe from any thread (controller thread).
        public void Input(PadKey key)
        {
            BeginInvoke(new Action(delegate
            {
                if (!IsOpen) return;
                if (key == PadKey.Select) { Activate(model.Index); return; }
                if (key == PadKey.Close) return;   // the launcher can only be left with the "Modus beenden" tile
                model.Move(key);
                KeepSelectionVisible();
                Invalidate();
            }));
        }

        void KeepSelectionVisible()
        {
            int rows = VisibleRows(Height);
            if (model.Row < firstRow) firstRow = model.Row;
            if (model.Row >= firstRow + rows) firstRow = model.Row - rows + 1;
        }

        void Activate(int index)
        {
            if (index < 0 || index >= ItemCount) return;
            if (index == cfg.Apps.Count) exitMode();
            else launch(cfg.Apps[index]);
        }

        Rectangle TileRect(int index)
        {
            int cols = model.Cols;
            int gridW = cols * Tile + (cols - 1) * Gap;
            int left = (Width - gridW) / 2;
            int row = index / cols - firstRow, col = index % cols;
            return new Rectangle(left + col * (Tile + Gap), HeaderH + row * (TileH + Gap), Tile, TileH);
        }

        bool TileVisible(int index)
        {
            int row = index / model.Cols;
            return row >= firstRow && row < firstRow + VisibleRows(Height);
        }

        Bitmap IconFor(AppEntry a) { return AppIcons.For(a.Path); }   // shared cache, see StartApps.cs

        static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // `peach.exe --render-launcher out.png`: dump a preview image (no window needed).
        public void RenderTo(string file, int w, int h)
        {
            Size = new Size(w, h);
            model = new LauncherModel(ItemCount, ColsFor(w));
            model.Index = Math.Min(1, ItemCount - 1);
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp)) PaintAll(g);
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        protected override void OnPaint(PaintEventArgs e) { PaintAll(e.Graphics); }

        void PaintAll(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (Brush bg = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Bg1, Bg2, 90f)) g.FillRectangle(bg, 0, 0, Width, Height);

            // header
            using (Brush peach = new LinearGradientBrush(new Rectangle(Edge, 40, 56, 56), Color.FromArgb(255, 170, 120), Accent, 60f))
                g.FillEllipse(peach, Edge, 46, 54, 54);
            using (Brush leaf = new SolidBrush(Color.FromArgb(90, 190, 110))) g.FillEllipse(leaf, Edge + 30, 34, 24, 15);
            using (Font f = new Font("Segoe UI Semibold", 28f)) using (Brush b = new SolidBrush(Ink)) g.DrawString("Peach", f, b, Edge + 70, 38);
            DateTime now = DateTime.Now;
            using (Font f = new Font("Segoe UI Light", 30f)) using (Brush b = new SolidBrush(Ink))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(now.ToString("HH:mm"), f, b, new RectangleF(Width - Edge - 300, 36, 300, 60), right);
            using (Font f = new Font("Segoe UI", 11f)) using (Brush b = new SolidBrush(Muted))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(now.ToString("dddd, dd. MMMM"), f, b, new RectangleF(Width - Edge - 300, 88, 300, 24), right);

            // tiles
            using (Font nameFont = new Font("Segoe UI", 14f))
            using (Font letterFont = new Font("Segoe UI Semibold", 30f))
            using (Brush ink = new SolidBrush(Ink))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            {
                for (int i = 0; i < ItemCount; i++)
                {
                    if (!TileVisible(i)) continue;
                    bool selected = i == model.Index;
                    Rectangle r = TileRect(i);
                    if (selected) r.Inflate(10, 10);
                    using (GraphicsPath p = Round(r, 22))
                    {
                        if (selected)
                            using (Pen glow = new Pen(Color.FromArgb(70, Accent), 14f)) g.DrawPath(glow, p);
                        using (Brush b = new SolidBrush(selected ? TileSel : TileBg)) g.FillPath(b, p);
                        if (selected) using (Pen pen = new Pen(Accent, 4f)) g.DrawPath(pen, p);
                    }

                    Rectangle iconBox = new Rectangle(r.X + (r.Width - 84) / 2, r.Y + 34, 84, 84);
                    bool isExit = i == cfg.Apps.Count;
                    string title;
                    if (isExit)
                    {
                        title = "Modus beenden";
                        DrawPower(g, iconBox, Color.FromArgb(230, 110, 100));
                    }
                    else
                    {
                        AppEntry a = cfg.Apps[i];
                        title = a.Name;
                        Bitmap icon = IconFor(a);
                        if (icon != null) g.DrawImage(icon, iconBox);
                        else
                        {
                            Color c = Palette[Math.Abs((a.Name ?? "?").GetHashCode()) % Palette.Length];
                            using (Brush b = new SolidBrush(c)) g.FillEllipse(b, iconBox);
                            string letter = string.IsNullOrEmpty(a.Name) ? "?" : a.Name.Substring(0, 1).ToUpperInvariant();
                            using (Brush dark = new SolidBrush(Bg1)) g.DrawString(letter, letterFont, dark, iconBox, center);
                        }
                    }
                    g.DrawString(title, nameFont, ink, new Rectangle(r.X + 10, r.Y + 140, r.Width - 20, 70), center);
                }
            }

            // scroll hints
            int rows = (ItemCount + model.Cols - 1) / model.Cols, vis = VisibleRows(Height);
            using (Font f = new Font("Segoe UI", 12f)) using (Brush b = new SolidBrush(Muted))
            using (StringFormat c = new StringFormat { Alignment = StringAlignment.Center })
            {
                if (firstRow > 0) g.DrawString("▲", f, b, new RectangleF(0, HeaderH - 30, Width, 24), c);
                if (firstRow + vis < rows) g.DrawString("▼", f, b, new RectangleF(0, Height - FooterH - 10, Width, 24), c);
            }

            using (Font f = new Font("Segoe UI", 11f)) using (Brush b = new SolidBrush(Muted))
                g.DrawString("Steuerkreuz / Stick  wählen   ·   A  Starten   ·   Home-Taste (Xbox)  Menü aus jeder App   ·   Beenden: letzte Kachel",
                    f, b, Edge, Height - 52);
        }

        static void DrawPower(Graphics g, Rectangle box, Color c)
        {
            using (Pen pen = new Pen(c, 8f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                Rectangle ring = new Rectangle(box.X + 10, box.Y + 14, box.Width - 20, box.Height - 20);
                g.DrawArc(pen, ring, -60, 300);
                g.DrawLine(pen, box.X + box.Width / 2, box.Y + 4, box.X + box.Width / 2, box.Y + box.Height / 2 - 4);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: return true;   // swallowed on purpose: only the exit tile ends controller mode
                case Keys.Left: Input(PadKey.Left); return true;
                case Keys.Right: Input(PadKey.Right); return true;
                case Keys.Up: Input(PadKey.Up); return true;
                case Keys.Down: Input(PadKey.Down); return true;
                case Keys.Enter: case Keys.Space: Input(PadKey.Select); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < ItemCount; i++) if (TileVisible(i) && TileRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i >= 0 && i != model.Index) { model.Index = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i >= 0) Activate(i);
            base.OnMouseClick(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (IsOpen) { ReloadModel(); Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clock.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
