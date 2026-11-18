using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;

namespace Peach
{
    // Controller-friendly on-screen keyboard. It never takes focus from the app you are typing
    // into (WS_EX_NOACTIVATE), so the keys land in the text field that was clicked.
    internal class OnScreenKeyboard : Form
    {
        // Geometry (also used by test\exe-test.ps1 to click keys): padding 12, gap 6, key height 54, hint strip 26.
        public const int Pad = 12, Gap = 6, KeyH = 54, HintH = 26;
        const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WM_MOUSEACTIVATE = 0x21;

        static readonly Color Bg = Color.FromArgb(38, 28, 25), KeyBg = Color.FromArgb(62, 46, 41), KeySpecial = Color.FromArgb(50, 37, 33),
            Ink = Color.FromArgb(246, 233, 228), Muted = Color.FromArgb(165, 143, 136), Accent = Color.FromArgb(255, 122, 89);

        public readonly KeyboardModel Model = new KeyboardModel();
        public volatile bool IsOpen;
        public bool AutoShown;

        // Typing happens on one worker thread so quickly pressed keys can never be reordered, and so
        // SendInput never runs on the UI thread that owns the keyboard hook.
        static readonly BlockingCollection<Action> Outbox = new BlockingCollection<Action>();
        static OnScreenKeyboard()
        {
            Thread t = new Thread(delegate () { foreach (Action a in Outbox.GetConsumingEnumerable()) { try { a(); } catch { } } });
            t.IsBackground = true;
            t.Name = "Peach keyboard output";
            t.Start();
        }

        public OnScreenKeyboard()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Bg;
            Text = "Peach Tastatur";
            IntPtr handle = Handle;   // create the handle now so Input() works from the controller thread
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(3); return; }   // MA_NOACTIVATE
            base.WndProc(ref m);
        }

        public static Size ComputeSize(int width) { return new Size(width, Pad + KeyboardModel.Rows * KeyH + (KeyboardModel.Rows - 1) * Gap + Pad + HintH); }

        public void Toggle(bool auto)
        {
            if (IsOpen) Close(); else Open(auto);
        }

        public void Open(bool auto)
        {
            if (IsOpen) return;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = ComputeSize(Math.Min(1000, area.Width - 40));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 12);
            Model.Reset();
            AutoShown = auto;
            IsOpen = true;
            Show();
            Invalidate();
        }

        public new void Close()
        {
            IsOpen = false;
            AutoShown = false;
            Hide();
        }

        // Safe to call from any thread (the controller thread).
        public void Input(PadKey key)
        {
            BeginInvoke(new Action(delegate
            {
                if (!IsOpen) return;
                Emit(Model.Handle(key));
                Invalidate();
            }));
        }

        void Emit(KeyOutput o)
        {
            if (o.Close) { Close(); return; }
            if (o.Text != null) { string t = o.Text; Outbox.Add(delegate { Native.TypeText(t, 0); }); }
            else if (o.Vk != 0) { ushort vk = o.Vk; bool ext = o.Extended; Outbox.Add(delegate { Native.Key(vk, ext); }); }
        }

        public Rectangle RectOf(int row, int col, int width)
        {
            KeyDef[] keys = Model.RowKeys(row);
            double sum = 0;
            foreach (KeyDef k in keys) sum += k.W;
            double avail = width - 2 * Pad - Gap * (keys.Length - 1);
            double x = Pad;
            for (int i = 0; i < keys.Length; i++)
            {
                double w = keys[i].W / sum * avail;
                if (i == col) return new Rectangle((int)Math.Round(x), Pad + row * (KeyH + Gap), (int)Math.Round(w), KeyH);
                x += w + Gap;
            }
            return Rectangle.Empty;
        }

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

        // Public so `peach.exe --render-keyboard` can dump a PNG for visual checks.
        public void Draw(Graphics g, int width)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Bg);
            StringFormat center = new StringFormat();
            center.Alignment = StringAlignment.Center;
            center.LineAlignment = StringAlignment.Center;
            using (Font f = new Font("Segoe UI", 15f))
            using (Font hint = new Font("Segoe UI", 9f))
            using (Brush ink = new SolidBrush(Ink))
            using (Brush dark = new SolidBrush(Bg))
            using (Brush muted = new SolidBrush(Muted))
            {
                for (int r = 0; r < KeyboardModel.Rows; r++)
                {
                    KeyDef[] keys = Model.RowKeys(r);
                    for (int c = 0; c < keys.Length; c++)
                    {
                        Rectangle rect = RectOf(r, c, width);
                        bool selected = r == Model.Row && c == Model.Col;
                        bool special = keys[c].Kind != KeyKind.Char;
                        bool shiftOn = keys[c].Kind == KeyKind.Shift && Model.Shift;
                        using (GraphicsPath p = Round(rect, 8))
                        {
                            using (Brush b = new SolidBrush(selected ? Accent : (shiftOn ? Color.FromArgb(120, 70, 55) : (special ? KeySpecial : KeyBg)))) g.FillPath(b, p);
                            if (shiftOn && !selected) using (Pen pen = new Pen(Accent, 2f)) g.DrawPath(pen, p);
                        }
                        g.DrawString(Model.Label(keys[c]), f, selected ? dark : ink, rect, center);
                    }
                }
                g.DrawString("Steuerkreuz wählen  ·  A Taste  ·  B Löschen  ·  X Leerzeichen  ·  Y Shift  ·  LB/RB Cursor  ·  Start Enter  ·  RT Klick  ·  Back schließen",
                    hint, muted, Pad + 4, ComputeSize(width).Height - HintH + 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics, Width); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int r = 0; r < KeyboardModel.Rows; r++)
            {
                int n = Model.RowKeys(r).Length;
                for (int c = 0; c < n; c++)
                {
                    if (RectOf(r, c, Width).Contains(e.Location))
                    {
                        Emit(Model.PressAt(r, c));
                        Invalidate();
                        return;
                    }
                }
            }
            base.OnMouseDown(e);
        }
    }
}
