using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Peach
{
    // The Peach look: warm dark cards, one accent colour. Everything below is drawn by Peach itself.
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(30, 22, 20), Panel = Color.FromArgb(38, 28, 25), Card = Color.FromArgb(50, 37, 33),
            CardHover = Color.FromArgb(62, 46, 41), Border = Color.FromArgb(78, 58, 52), Ink = Color.FromArgb(246, 233, 228),
            Muted = Color.FromArgb(165, 143, 136), Accent = Color.FromArgb(255, 122, 89), AccentHover = Color.FromArgb(255, 146, 116),
            AccentDown = Color.FromArgb(226, 100, 70), Danger = Color.FromArgb(226, 98, 112), Ok = Color.FromArgb(86, 196, 128);

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font Font(float size, bool bold)
        {
            string key = size + (bold ? "b" : "r");
            lock (fonts)
            {
                Font f;
                if (!fonts.TryGetValue(key, out f)) { f = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size); fonts[key] = f; }
                return f;
            }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color WithAlpha(Color c, int alpha) { return Color.FromArgb(alpha, c.R, c.G, c.B); }
    }

    // Borderless window with Peach title bar, rounded corners on Windows 11, drop shadow, drag by the title.
    // Resizable = true adds edge resizing, minimize/maximize buttons and double-click-to-maximize (used by the Discord window).
    internal class PeachForm : Form
    {
        public const int TitleH = 48;
        const int Grip = 7;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        string title;
        int hotButton;                 // 0 none, 1 close, 2 maximize, 3 minimize
        bool resizable;
        public bool EscCloses = true;

        public PeachForm(string title, int width, int height)
        {
            this.title = title;
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            Font = Theme.Font(10f, false);
            ClientSize = new Size(width, height);
            DoubleBuffered = true;
            ShowInTaskbar = false;
            MinimizeBox = false; MaximizeBox = false;
        }

        public bool Resizable
        {
            get { return resizable; }
            set { resizable = value; MinimizeBox = value; Invalidate(); }
        }

        public void SetTitle(string t) { title = t; Text = t; Invalidate(new Rectangle(0, 0, Width, TitleH)); }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }   // CS_DROPSHADOW
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);                    // rounded corners (Windows 11)
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                DwmSetWindowAttribute(Handle, 34, ref border, 4);                                   // border colour
            }
            catch { /* older Windows: square corners */ }
        }

        Rectangle CloseRect { get { return new Rectangle(Width - 52, 0, 52, TitleH); } }
        Rectangle MaxRect { get { return new Rectangle(Width - 52 - 46, 0, 46, TitleH); } }
        Rectangle MinRect { get { return new Rectangle(Width - 52 - 92, 0, 46, TitleH); } }

        int HitButton(Point p)
        {
            if (p.Y < 0 || p.Y >= TitleH) return 0;
            if (CloseRect.Contains(p)) return 1;
            if (resizable) { if (MaxRect.Contains(p)) return 2; if (MinRect.Contains(p)) return 3; }
            return 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (Brush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, 0, 0, Width, TitleH);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, TitleH - 1, Width, TitleH - 1);
            using (Brush peach = new LinearGradientBrush(new Rectangle(18, 12, 24, 24), Color.FromArgb(255, 180, 130), Theme.Accent, 60f)) g.FillEllipse(peach, 18, 15, 22, 22);
            using (Brush leaf = new SolidBrush(Color.FromArgb(90, 190, 110))) g.FillEllipse(leaf, 29, 10, 11, 7);
            using (Brush ink = new SolidBrush(Theme.Ink)) g.DrawString(title, Theme.Font(12f, true), ink, 50, 12);
            Rectangle c = CloseRect;
            if (hotButton == 1) using (Brush b = new SolidBrush(Theme.Danger)) g.FillRectangle(b, c);
            using (Pen x = new Pen(hotButton == 1 ? Color.White : Theme.Muted, 1.6f))
            {
                g.DrawLine(x, c.X + 20, 18, c.X + 32, 30);
                g.DrawLine(x, c.X + 32, 18, c.X + 20, 30);
            }
            if (resizable)
            {
                Rectangle mx = MaxRect, mn = MinRect;
                if (hotButton == 2) using (Brush b = new SolidBrush(Theme.CardHover)) g.FillRectangle(b, mx);
                if (hotButton == 3) using (Brush b = new SolidBrush(Theme.CardHover)) g.FillRectangle(b, mn);
                using (Pen x = new Pen(Theme.Muted, 1.6f))
                {
                    g.DrawLine(x, mn.X + 17, 30, mn.X + 29, 30);
                    if (WindowState == FormWindowState.Maximized) { g.DrawRectangle(x, mx.X + 18, 20, 8, 8); g.DrawLine(x, mx.X + 21, 17, mx.X + 29, 17); g.DrawLine(x, mx.X + 29, 17, mx.X + 29, 25); }
                    else g.DrawRectangle(x, mx.X + 17, 18, 11, 11);
                }
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(new Rectangle(0, 0, Width, TitleH)); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hot = HitButton(e.Location);
            if (hot != hotButton) { hotButton = hot; Invalidate(new Rectangle(0, 0, Width, TitleH)); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hotButton != 0) { hotButton = 0; Invalidate(new Rectangle(0, 0, Width, TitleH)); }
            base.OnMouseLeave(e);
        }

        public void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else { MaximizedBounds = Screen.FromControl(this).WorkingArea; WindowState = FormWindowState.Maximized; }
            Invalidate(new Rectangle(0, 0, Width, TitleH));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < TitleH)
            {
                int b = HitButton(e.Location);
                if (b == 1) { if (Modal) DialogResult = DialogResult.Cancel; else Close(); return; }
                if (b == 2) { ToggleMaximize(); return; }
                if (b == 3) { WindowState = FormWindowState.Minimized; return; }
                ReleaseCapture();
                SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION: drag the window
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (resizable && e.Button == MouseButtons.Left && e.Y < TitleH && HitButton(e.Location) == 0) { ToggleMaximize(); return; }
            base.OnMouseDoubleClick(e);
        }

        // edge / corner resizing for borderless windows
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && resizable && WindowState == FormWindowState.Normal)    // WM_NCHITTEST
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool l = p.X < Grip, r = p.X >= Width - Grip, t = p.Y < Grip, b = p.Y >= Height - Grip;
                int hit = 0;
                if (t && l) hit = 13; else if (t && r) hit = 14; else if (b && l) hit = 16; else if (b && r) hit = 17;
                else if (l) hit = 10; else if (r) hit = 11; else if (t) hit = 12; else if (b) hit = 15;
                if (hit != 0) { m.Result = new IntPtr(hit); return; }
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && EscCloses) { if (Modal) DialogResult = DialogResult.Cancel; else Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- small layout helpers for the windows built on top ----
        public Label AddLabel(string text, int x, int y, int w, int h, bool muted, float size, bool bold)
        {
            Label l = new Label();
            l.Text = text; l.Location = new Point(x, y); l.Size = new Size(w, h);
            l.ForeColor = muted ? Theme.Muted : Theme.Ink; l.BackColor = Color.Transparent; l.Font = Theme.Font(size, bold);
            Controls.Add(l);
            return l;
        }
    }
    internal enum BtnStyle { Secondary, Primary, Danger, Ghost }

    internal class PeachButton : Control
    {
        public BtnStyle Style;
        public DialogResult Result = DialogResult.None;
        bool hot, down;

        public PeachButton(string text, BtnStyle style, int w, int h)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Text = text; Style = style; Size = new Size(w, h); Cursor = Cursors.Hand; TabStop = true;
            Font = Theme.Font(10f, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            base.OnClick(e);
            if (Result != DialogResult.None) { Form f = FindForm(); if (f != null) f.DialogResult = Result; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, text, border = Color.Empty;
            switch (Style)
            {
                case BtnStyle.Primary:
                    fill = down ? Theme.AccentDown : (hot ? Theme.AccentHover : Theme.Accent); text = Theme.Bg; break;
                case BtnStyle.Danger:
                    fill = hot ? Theme.Danger : Theme.Card; text = hot ? Color.White : Theme.Danger; border = Theme.Mix(Theme.Card, Theme.Danger, 0.5); break;
                case BtnStyle.Ghost:
                    fill = hot ? Theme.Card : Color.Empty; text = hot ? Theme.Ink : Theme.Muted; break;
                default:
                    fill = down ? Theme.Panel : (hot ? Theme.CardHover : Theme.Card); text = Theme.Ink; border = Theme.Border; break;
            }
            if (!Enabled) { text = Theme.Mix(Theme.Card, Theme.Muted, 0.5); if (!fill.IsEmpty) fill = Theme.Mix(Theme.Bg, fill, 0.5); }
            using (GraphicsPath p = Theme.Round(r, 9))
            {
                if (!fill.IsEmpty) using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!border.IsEmpty) using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
                if (Focused && Enabled) using (Pen pen = new Pen(Theme.WithAlpha(Theme.Accent, 160), 2f)) g.DrawPath(pen, p);
            }
            using (Brush tb = new SolidBrush(text))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Text, Font, tb, new RectangleF(6, 0, Width - 12, Height), sf);
        }
    }

    internal class PeachToggle : Control
    {
        bool on;
        bool hot;
        public event EventHandler CheckedChanged;
        public string Caption = "";

        public PeachToggle(bool value, string caption)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            on = value; Caption = caption; Cursor = Cursors.Hand; TabStop = true;
            Size = new Size(caption.Length > 0 ? 60 + TextRenderer.MeasureText(caption, Theme.Font(10f, false)).Width + 8 : 50, 28);
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); EventHandler h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        public void SetSilently(bool value) { on = value; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !on; base.OnClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !on; e.Handled = true; } base.OnKeyDown(e); }

        public static void DrawSwitch(Graphics g, Rectangle r, bool on, bool hot)
        {
            Theme.Smooth(g);
            using (GraphicsPath p = Theme.Round(r, r.Height / 2))
            {
                using (Brush b = new SolidBrush(on ? (hot ? Theme.AccentHover : Theme.Accent) : (hot ? Theme.CardHover : Theme.Card))) g.FillPath(b, p);
                if (!on) using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
            int k = r.Height - 8;
            int x = on ? r.Right - k - 4 : r.X + 4;
            using (Brush kb = new SolidBrush(on ? Color.White : Theme.Muted)) g.FillEllipse(kb, x, r.Y + 4, k, k);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle sw = new Rectangle(0, (Height - 26) / 2, 48, 26);
            DrawSwitch(g, sw, on, hot);
            if (Focused) using (GraphicsPath p = Theme.Round(Rectangle.Inflate(sw, 2, 2), 14)) using (Pen pen = new Pen(Theme.WithAlpha(Theme.Accent, 140), 2f)) g.DrawPath(pen, p);
            if (Caption.Length > 0) using (Brush b = new SolidBrush(Theme.Ink)) g.DrawString(Caption, Theme.Font(10f, false), b, 58, (Height - 20) / 2f - 1);
        }
    }

    internal class PeachSlider : Control
    {
        int min, max, value;
        bool drag;
        public string Suffix = "";
        public event EventHandler ValueChanged;

        public PeachSlider(int min, int max, int value, string suffix, int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.min = min; this.max = max; this.value = Math.Max(min, Math.Min(max, value)); Suffix = suffix;
            Size = new Size(width, 30); Cursor = Cursors.Hand; TabStop = true;
        }

        public int Value
        {
            get { return value; }
            set { int v = Math.Max(min, Math.Min(max, value)); if (v == this.value) return; this.value = v; Invalidate(); EventHandler h = ValueChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        const int LabelW = 64;
        Rectangle Track { get { return new Rectangle(8, Height / 2 - 3, Width - LabelW - 16, 6); } }

        void SetFromX(int x)
        {
            Rectangle t = Track;
            double f = (x - t.X) / (double)t.Width;
            Value = min + (int)Math.Round(Math.Max(0, Math.Min(1, f)) * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; Focus(); SetFromX(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Math.Max(1, (max - min) / 50);
            if (e.KeyCode == Keys.Left) { Value = value - step; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value = value + step; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle t = Track;
            double f = (value - min) / (double)Math.Max(1, max - min);
            using (GraphicsPath p = Theme.Round(t, 3)) using (Brush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
            Rectangle fill = new Rectangle(t.X, t.Y, (int)(t.Width * f), t.Height);
            if (fill.Width > 2) using (GraphicsPath p = Theme.Round(fill, 3)) using (Brush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            int kx = t.X + (int)(t.Width * f) - 9;
            using (Brush kb = new SolidBrush(Color.White)) g.FillEllipse(kb, kx, Height / 2 - 9, 18, 18);
            using (Pen pen = new Pen(Focused ? Theme.Accent : Theme.Border, 2f)) g.DrawEllipse(pen, kx, Height / 2 - 9, 18, 18);
            using (Brush ink = new SolidBrush(Theme.Ink))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                g.DrawString(value + Suffix, Theme.Font(10f, true), ink, new RectangleF(Width - LabelW, 0, LabelW, Height), sf);
        }
    }

    // Segmented choice, e.g. "Unten | Oben".
    internal class PeachSegmented : Control
    {
        readonly string[] options;
        int selected;
        int hot = -1;
        public event EventHandler SelectedIndexChanged;

        public PeachSegmented(string[] options, int selected, int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.options = options; this.selected = selected; Size = new Size(width, 34); Cursor = Cursors.Hand; TabStop = true;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { if (value == selected) return; selected = value; Invalidate(); EventHandler h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        int Hit(int x) { return Math.Max(0, Math.Min(options.Length - 1, x * options.Length / Math.Max(1, Width))); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); SelectedIndex = Hit(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.X); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && selected > 0) { SelectedIndex = selected - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right && selected < options.Length - 1) { SelectedIndex = selected + 1; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle all = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(all, 9)) { using (Brush b = new SolidBrush(Theme.Card)) g.FillPath(b, p); using (Pen pen = new Pen(Focused ? Theme.Accent : Theme.Border)) g.DrawPath(pen, p); }
            float w = (Width - 1) / (float)options.Length;
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < options.Length; i++)
                {
                    Rectangle seg = new Rectangle((int)(i * w) + 3, 3, (int)w - 6, Height - 7);
                    bool sel = i == selected;
                    if (sel) using (GraphicsPath p = Theme.Round(seg, 7)) using (Brush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                    else if (i == hot) using (GraphicsPath p = Theme.Round(seg, 7)) using (Brush b = new SolidBrush(Theme.CardHover)) g.FillPath(b, p);
                    using (Brush t = new SolidBrush(sel ? Theme.Bg : Theme.Ink)) g.DrawString(options[i], Theme.Font(10f, true), t, seg, sf);
                }
            }
        }
    }

    // Text input with a rounded border, focus ring and placeholder.
    internal class PeachTextBox : Control
    {
        readonly TextBox tb = new TextBox();
        bool focused;
        public string Placeholder = "";

        public PeachTextBox(int width, int height, bool multiline)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            tb.BorderStyle = BorderStyle.None; tb.BackColor = Theme.Card; tb.ForeColor = Theme.Ink; tb.Font = Theme.Font(10.5f, false);
            tb.Multiline = multiline; tb.AcceptsReturn = multiline; tb.WordWrap = true;
            if (multiline) tb.ScrollBars = ScrollBars.Vertical;
            tb.GotFocus += delegate { focused = true; Invalidate(); };
            tb.LostFocus += delegate { focused = false; Invalidate(); };
            tb.TextChanged += delegate { Invalidate(); OnTextChanged(EventArgs.Empty); };
            Controls.Add(tb);
            Size = new Size(width, height);
        }

        public override string Text { get { return tb.Text; } set { tb.Text = value ?? ""; } }
        public bool ReadOnly { get { return tb.ReadOnly; } set { tb.ReadOnly = value; } }
        public void FocusInput() { tb.Focus(); }
        public TextBox Inner { get { return tb; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int h = tb.Multiline ? Height - 16 : tb.PreferredHeight;
            tb.SetBounds(12, tb.Multiline ? 8 : (Height - h) / 2, Width - 24, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, 9))
            {
                using (Brush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(focused ? Theme.Accent : Theme.Border, focused ? 2f : 1f)) g.DrawPath(pen, p);
            }
            if (tb.Text.Length == 0 && Placeholder.Length > 0)
                using (Brush b = new SolidBrush(Theme.Muted)) g.DrawString(Placeholder, Theme.Font(10.5f, false), b, 12, tb.Multiline ? 8 : (Height - 20) / 2f);
        }
    }

    // Records a key combination ("ctrl+alt+k") instead of typing characters.
    internal class PeachHotkeyBox : Control
    {
        string combo = "";
        bool focused;
        public event EventHandler ComboChanged;

        public PeachHotkeyBox(string combo, int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.combo = combo ?? ""; Size = new Size(width, 38); TabStop = true; Cursor = Cursors.IBeam;
        }

        public string Combo { get { return combo; } }
        public void Clear() { combo = ""; Invalidate(); }
        public static string Pretty(string c)
        {
            if (string.IsNullOrEmpty(c)) return "";
            string[] parts = c.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i];
                parts[i] = s == "ctrl" ? "Strg" : (s == "alt" ? "Alt" : (s == "shift" ? "Umschalt" : (s == "win" ? "Win" : s.ToUpperInvariant())));
            }
            return string.Join(" + ", parts);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        protected override void OnGotFocus(EventArgs e) { focused = true; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { focused = false; Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return true; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!focused) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu) return true;
            string name = Peach.Combo.KeyName(key);
            bool ctrl = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0, shift = (keyData & Keys.Shift) != 0;
            if (name == null || !(ctrl || alt || shift)) return true;
            combo = (ctrl ? "ctrl+" : "") + (alt ? "alt+" : "") + (shift ? "shift+" : "") + name;
            Invalidate();
            EventHandler h = ComboChanged; if (h != null) h(this, EventArgs.Empty);
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, 9))
            {
                using (Brush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(focused ? Theme.Accent : Theme.Border, focused ? 2f : 1f)) g.DrawPath(pen, p);
            }
            bool empty = combo.Length == 0;
            using (Brush b = new SolidBrush(empty ? Theme.Muted : Theme.Ink))
            using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center })
                g.DrawString(empty ? (focused ? "Jetzt die Tasten drücken…" : "Hier klicken, dann Tastenkombination drücken") : Pretty(combo), Theme.Font(10.5f, !empty), b, new RectangleF(14, 0, Width - 28, Height), sf);
        }
    }

    // Peach-styled replacements for MessageBox.
    internal static class Dialogs
    {
        static PeachForm Build(string title, string text, string yes, string no, bool danger, out bool wantsNo)
        {
            wantsNo = !string.IsNullOrEmpty(no);
            Size textSize = TextRenderer.MeasureText(text, Theme.Font(10.5f, false), new Size(440, 0), TextFormatFlags.WordBreak);
            int h = PeachForm.TitleH + 28 + Math.Max(24, textSize.Height) + 28 + 40 + 22;
            PeachForm f = new PeachForm(title, 500, h);
            f.AddLabel(text, 30, PeachForm.TitleH + 26, 440, textSize.Height + 6, false, 10.5f, false);
            int y = h - 62;
            PeachButton ok = new PeachButton(yes, danger ? BtnStyle.Danger : BtnStyle.Primary, 130, 40); ok.Location = new Point(wantsNo ? 500 - 30 - 130 - 120 - 12 : 500 - 30 - 130, y); ok.Result = DialogResult.OK;
            f.Controls.Add(ok);
            f.AcceptButton = null;
            if (wantsNo) { PeachButton cancel = new PeachButton(no, BtnStyle.Secondary, 120, 40); cancel.Location = new Point(500 - 30 - 120, y); cancel.Result = DialogResult.Cancel; f.Controls.Add(cancel); }
            return f;
        }

        public static bool Ask(IWin32Window owner, string title, string text, string yes, string no) { return Ask(owner, title, text, yes, no, false); }

        public static bool Ask(IWin32Window owner, string title, string text, string yes, string no, bool danger)
        {
            bool wantsNo;
            using (PeachForm f = Build(title, text, yes, no, danger, out wantsNo))
            {
                if (owner == null) { f.TopMost = true; f.ShowInTaskbar = true; }
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static void Info(IWin32Window owner, string title, string text)
        {
            bool wantsNo;
            using (PeachForm f = Build(title, text, "OK", null, false, out wantsNo))
            {
                if (owner == null) { f.TopMost = true; f.ShowInTaskbar = true; }
                f.ShowDialog(owner);
            }
        }
    }
}
