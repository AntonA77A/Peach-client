using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Peach
{
    internal class MenuEntry
    {
        public Func<string> Label;
        public Func<string> State;     // null = no pill, otherwise "AN" / "AUS" / any text
        public Action Run;
        public bool KeepOpen;          // toggles keep the menu open and just repaint
    }

    // The Ctrl+Alt+P overlay. Keyboard (arrows, Enter, 1-9, Esc), mouse and the controller
    // mouse all work on it.
    internal class MenuForm : Form
    {
        const int W = 480, Head = 84, Row = 40, Foot = 44;
        static readonly Color Bg = Color.FromArgb(38, 28, 25), Ink = Color.FromArgb(246, 233, 228),
            Muted = Color.FromArgb(165, 143, 136), Accent = Color.FromArgb(255, 122, 89),
            Hover = Color.FromArgb(62, 45, 40), Ok = Color.FromArgb(76, 190, 120);

        readonly List<MenuEntry> items;
        int hot;

        public MenuForm(List<MenuEntry> items)
        {
            this.items = items;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Bg;
            Text = "Peach Menü";
            ClientSize = new Size(W, Head + Row * items.Count + Foot);
            using (GraphicsPath p = Rounded(new Rectangle(0, 0, Width, Height), 18)) Region = new Region(p);
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            hot = 0;
            Show();
            Native.ForceForeground(Handle);
            Activate();
            Invalidate();
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
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

        Rectangle RowRect(int i) { return new Rectangle(14, Head + i * Row, W - 28, Row - 6); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // logo + title
            using (Brush peach = new LinearGradientBrush(new Rectangle(26, 24, 40, 40), Color.FromArgb(255, 170, 120), Accent, 60f))
                g.FillEllipse(peach, 26, 26, 38, 38);
            using (Brush leaf = new SolidBrush(Color.FromArgb(90, 190, 110)))
                g.FillEllipse(leaf, 46, 18, 16, 10);
            using (Font f = new Font("Segoe UI Semibold", 18f)) using (Brush b = new SolidBrush(Ink))
                g.DrawString("Peach", f, b, 76, 22);
            using (Font f = new Font("Segoe UI", 9.5f)) using (Brush b = new SolidBrush(Muted))
                g.DrawString("Strg+Alt+P schließt das Menü wieder", f, b, 79, 56);

            using (Font f = new Font("Segoe UI", 12f))
            using (Font small = new Font("Segoe UI", 9.5f))
            using (Font pill = new Font("Segoe UI Semibold", 9f))
            using (Brush ink = new SolidBrush(Ink))
            using (Brush muted = new SolidBrush(Muted))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Rectangle r = RowRect(i);
                    if (i == hot)
                        using (GraphicsPath p = Rounded(r, 10)) using (Brush b = new SolidBrush(Hover)) g.FillPath(b, p);
                    if (i == hot)
                        using (Brush b = new SolidBrush(Accent)) g.FillRectangle(b, r.X, r.Y + 8, 3, r.Height - 16);

                    g.DrawString(i < 9 ? (i + 1).ToString() : (i == 9 ? "0" : ""), small, muted, r.X + 14, r.Y + 9);
                    g.DrawString(items[i].Label(), f, ink, r.X + 40, r.Y + 6);

                    string state = items[i].State == null ? null : items[i].State();
                    if (state != null)
                    {
                        SizeF sz = g.MeasureString(state, pill);
                        Rectangle pr = new Rectangle((int)(r.Right - sz.Width - 30), r.Y + 5, (int)sz.Width + 18, 24);
                        Color c = state == "AN" ? Ok : Muted;
                        using (GraphicsPath p = Rounded(pr, 11)) using (Pen pen = new Pen(c, 1.5f)) g.DrawPath(pen, p);
                        using (Brush b = new SolidBrush(c)) g.DrawString(state, pill, b, pr.X + 9, pr.Y + 3);
                    }
                }
                g.DrawString("↑ ↓ wählen   ·   Enter ausführen   ·   Esc schließen", small, muted, 28, Head + Row * items.Count + 8);
            }
        }

        void Activate(int i)
        {
            if (i < 0 || i >= items.Count) return;
            MenuEntry it = items[i];
            if (it.KeepOpen) { it.Run(); Invalidate(); return; }
            Hide();
            BeginInvoke(it.Run);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Hide(); return true;
                case Keys.Up: hot = (hot + items.Count - 1) % items.Count; Invalidate(); return true;
                case Keys.Down: hot = (hot + 1) % items.Count; Invalidate(); return true;
                case Keys.Enter: case Keys.Space: Activate(hot); return true;
            }
            if (keyData >= Keys.D1 && keyData <= Keys.D9) { Activate(keyData - Keys.D1); return true; }
            if (keyData == Keys.D0) { Activate(9); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            for (int i = 0; i < items.Count; i++)
                if (RowRect(i).Contains(e.Location) && hot != i) { hot = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < items.Count; i++)
                if (RowRect(i).Contains(e.Location)) { Activate(i); break; }
            base.OnMouseClick(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible) Hide();
        }
    }
}
