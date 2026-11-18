using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Peach
{
    internal struct BarItem
    {
        public const int Menu = 0, Pinned = 1, Window = 2, Clock = 3;
        public int Kind, Index;
        public Rectangle Rect;
    }

    // Pure bar logic (unit-tested): when to show it, colours, and where every item goes.
    internal static class BarLogic
    {
        // The bar belongs to normal desktop mode. While controller mode is really active (controller connected)
        // the launcher is the "desktop", so the bar steps aside.
        public static bool ShouldShow(bool enabled, bool controllerMode, bool controllerConnected)
        {
            return enabled && !(controllerMode && controllerConnected);
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                hex = hex.Trim().TrimStart('#');
                if (hex.Length != 6) return fallback;
                return Color.FromArgb(Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
            }
            catch { return fallback; }
        }

        public static string ToHex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

        public static bool IsLight(Color c) { return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150; }

        // Left: peach button, pinned apps. Right: clock/date. Middle: open windows, sharing the rest of the width.
        public static List<BarItem> Layout(int width, int height, BarConfig c, int windowCount)
        {
            List<BarItem> list = new List<BarItem>();
            int pad = 6, gap = 4;
            int btn = Math.Max(24, height - 2 * pad);
            int y = (height - btn) / 2;
            int x = pad;
            if (c.ShowMenuButton) { list.Add(new BarItem { Kind = BarItem.Menu, Rect = new Rectangle(x, y, btn, btn) }); x += btn + gap; }
            for (int i = 0; i < c.Pinned.Count; i++)
            {
                list.Add(new BarItem { Kind = BarItem.Pinned, Index = i, Rect = new Rectangle(x, y, btn, btn) });
                x += btn + gap;
            }
            int rightW = (c.ShowClock || c.ShowDate) ? ((c.ShowClock && c.ShowDate) ? 84 : 70) : 0;
            int clockX = width - pad - rightW;
            if (rightW > 0) list.Add(new BarItem { Kind = BarItem.Clock, Rect = new Rectangle(clockX, y, rightW, btn) });

            if (c.ShowWindows && windowCount > 0)
            {
                int x0 = x + (list.Count > 0 ? 8 : 0);
                int end = rightW > 0 ? clockX - 8 : width - pad;
                int avail = end - x0;
                int n = windowCount;
                int w = n > 0 ? Math.Min(200, (avail + gap) / n - gap) : 0;
                if (w < 40) { n = Math.Max(0, (avail + gap) / (40 + gap)); w = 40; }   // too many windows: show what fits
                for (int k = 0; k < n; k++)
                    list.Add(new BarItem { Kind = BarItem.Window, Index = k, Rect = new Rectangle(x0 + k * (w + gap), y, w, btn) });
            }
            return list;
        }
    }

    internal class WinInfo
    {
        public IntPtr Hwnd;
        public string Title;
        public uint Pid;
        public bool Minimized;
    }

    // The windows a taskbar would list, plus switching to them.
    internal static class WindowList
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr h, int index);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr proc, uint flags, StringBuilder name, ref int size);

        static readonly Dictionary<IntPtr, Bitmap> iconCache = new Dictionary<IntPtr, Bitmap>();

        public static List<WinInfo> Snapshot(uint ownPid)
        {
            List<WinInfo> list = new List<WinInfo>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;        // hidden, or owned popup
                int ex = GetWindowLong(h, -20);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;                       // tool window
                int cloaked;
                if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;   // suspended Store apps
                int len = GetWindowTextLength(h);
                if (len == 0) return true;
                StringBuilder cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == ownPid) return true;
                StringBuilder sb = new StringBuilder(len + 2);
                GetWindowText(h, sb, sb.Capacity);
                list.Add(new WinInfo { Hwnd = h, Title = sb.ToString(), Pid = pid, Minimized = IsIconic(h) });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void Close(IntPtr h) { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }   // WM_CLOSE

        // Like the Windows taskbar: click = bring to front (restore if minimized); click on the active one = minimize.
        public static void ActivateOrMinimize(IntPtr h)
        {
            if (IsIconic(h)) { ShowWindow(h, 9); Native.ForceForeground(h); return; }
            if (Native.GetForegroundWindow() == h) { ShowWindow(h, 6); return; }
            Native.ForceForeground(h);
        }

        static string ExePath(uint pid)
        {
            IntPtr p = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (p == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(520);
                int size = sb.Capacity;
                return QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(p); }
        }

        // Window's own small icon first, then the program's icon. Shared bitmap: never dispose.
        public static Bitmap IconOf(WinInfo w)
        {
            lock (iconCache)
            {
                Bitmap bmp;
                if (iconCache.TryGetValue(w.Hwnd, out bmp)) return bmp;
                bmp = null;
                try
                {
                    IntPtr ic;
                    SendMessageTimeout(w.Hwnd, 0x7F, new IntPtr(2), IntPtr.Zero, 2, 100, out ic);           // WM_GETICON, ICON_SMALL2
                    if (ic == IntPtr.Zero) SendMessageTimeout(w.Hwnd, 0x7F, new IntPtr(1), IntPtr.Zero, 2, 100, out ic);   // ICON_BIG
                    if (ic == IntPtr.Zero) ic = GetClassLongPtr(w.Hwnd, -34);                                // GCL_HICONSM
                    if (ic != IntPtr.Zero) using (Icon i = Icon.FromHandle(ic)) bmp = i.ToBitmap();
                }
                catch { bmp = null; }
                if (bmp == null)
                {
                    string exe = ExePath(w.Pid);
                    if (exe != null) bmp = AppIcons.For(exe);
                }
                iconCache[w.Hwnd] = bmp;
                return bmp;
            }
        }

        public static void ForgetMissing(List<WinInfo> alive)
        {
            lock (iconCache)
            {
                List<IntPtr> gone = new List<IntPtr>();
                foreach (IntPtr h in iconCache.Keys)
                {
                    bool found = false;
                    foreach (WinInfo w in alive) if (w.Hwnd == h) { found = true; break; }
                    if (!found) gone.Add(h);
                }
                foreach (IntPtr h in gone) iconCache.Remove(h);   // bitmaps from AppIcons are shared, so nothing is disposed here
            }
        }
    }

    // The Peach bar. A real "app bar": Windows keeps maximized windows out of its space.
    internal class BarForm : Form
    {
        const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
        const uint ABE_TOP = 1, ABE_BOTTOM = 3;
        const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WM_MOUSEACTIVATE = 0x21;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);

        readonly Config cfg;
        readonly Action openMenu, customize, hideBar;
        readonly Action<AppEntry> launch;
        readonly Timer timer = new Timer();
        readonly ToolTip tip = new ToolTip();
        readonly uint callbackMsg = RegisterWindowMessage("PeachBarCallback");
        readonly uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        List<WinInfo> wins = new List<WinInfo>();
        List<BarItem> items = new List<BarItem>();
        bool registered;
        uint edge;
        int hover = -1;
        IntPtr active;
        string lastClock = "";

        public BarForm(Config cfg, Action<AppEntry> launch, Action openMenu, Action customize, Action hideBar)
        {
            this.cfg = cfg; this.launch = launch; this.openMenu = openMenu; this.customize = customize; this.hideBar = hideBar;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Text = "Peach Leiste";
            timer.Interval = 500;
            timer.Tick += delegate { Tick(); };
            IntPtr handle = Handle;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        public bool IsShown { get { return registered && Visible; } }

        // ---- show / hide / reserve screen space -----------------------------------------------------------

        public void ShowBar()
        {
            BarConfig b = cfg.Bar;
            BackColor = BarLogic.ParseColor(b.Color, Color.FromArgb(38, 28, 25));
            Opacity = Math.Max(30, Math.Min(100, b.Opacity)) / 100.0;
            uint wantedEdge = b.Position == "top" ? ABE_TOP : ABE_BOTTOM;
            if (registered && edge != wantedEdge) Unregister();      // moved to the other edge: re-register there
            edge = wantedEdge;
            if (!registered) Register();
            Reposition();
            if (!Visible) Show();
            timer.Start();
            Tick(true);
            Invalidate();
        }

        public void HideBar()
        {
            timer.Stop();
            Unregister();
            if (Visible) Hide();
        }

        void Register()
        {
            APPBARDATA d = NewData();
            SHAppBarMessage(ABM_NEW, ref d);
            registered = true;
        }

        void Unregister()
        {
            if (!registered) return;
            APPBARDATA d = NewData();
            SHAppBarMessage(ABM_REMOVE, ref d);    // gives the screen space back to the other windows
            registered = false;
        }

        APPBARDATA NewData()
        {
            APPBARDATA d = new APPBARDATA();
            d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            d.hWnd = Handle;
            d.uCallbackMessage = callbackMsg;
            d.uEdge = edge;
            return d;
        }

        void Reposition()
        {
            if (!registered) return;
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            int h = cfg.Bar.Height;
            APPBARDATA d = NewData();
            d.rc.left = scr.Left; d.rc.top = scr.Top; d.rc.right = scr.Right; d.rc.bottom = scr.Bottom;
            SHAppBarMessage(ABM_QUERYPOS, ref d);                   // Windows trims the rect around other bars (e.g. the taskbar)
            if (edge == ABE_TOP) d.rc.bottom = d.rc.top + h; else d.rc.top = d.rc.bottom - h;
            SHAppBarMessage(ABM_SETPOS, ref d);
            SetBounds(d.rc.left, d.rc.top, d.rc.right - d.rc.left, d.rc.bottom - d.rc.top);
            Relayout();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(3); return; }   // clicks never steal focus
            if (m.Msg == callbackMsg && callbackMsg != 0)
            {
                if ((int)m.WParam == 1) Reposition();                 // ABN_POSCHANGED: another bar / the work area changed
                else if ((int)m.WParam == 2) TopMost = m.LParam == IntPtr.Zero;   // ABN_FULLSCREENAPP: get out of the way
                return;
            }
            base.WndProc(ref m);
        }

        // ---- content -----------------------------------------------------------------------------------

        void Relayout()
        {
            items = BarLogic.Layout(Width, Height, cfg.Bar, wins.Count);
            Invalidate();
        }

        void Tick() { Tick(false); }

        void Tick(bool force)
        {
            List<WinInfo> now = cfg.Bar.ShowWindows ? WindowList.Snapshot(ownPid) : new List<WinInfo>();
            IntPtr fg = Native.GetForegroundWindow();
            string clock = DateTime.Now.ToString("HH:mm dd.MM");
            bool changed = force || fg != active || clock != lastClock || now.Count != wins.Count;
            if (!changed)
                for (int i = 0; i < now.Count; i++)
                    if (now[i].Hwnd != wins[i].Hwnd || now[i].Title != wins[i].Title || now[i].Minimized != wins[i].Minimized) { changed = true; break; }
            if (!changed) return;
            wins = now; active = fg; lastClock = clock;
            WindowList.ForgetMissing(now);
            Relayout();
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Public so `peach.exe --render-bar` can render a preview without showing a window.
        public static void Draw(Graphics g, int width, int height, BarConfig c, List<WinInfo> windows, IntPtr activeWindow, int hover)
        {
            Color bg = BarLogic.ParseColor(c.Color, Color.FromArgb(38, 28, 25));
            Color accent = BarLogic.ParseColor(c.Accent, Color.FromArgb(255, 122, 89));
            bool light = BarLogic.IsLight(bg);
            Color ink = light ? Color.FromArgb(30, 24, 22) : Color.FromArgb(246, 233, 228);
            Color hoverBg = Mix(bg, light ? Color.Black : Color.White, 0.12);
            Color activeBg = Mix(bg, light ? Color.Black : Color.White, 0.2);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(bg);

            List<BarItem> layout = BarLogic.Layout(width, height, c, windows.Count);
            using (Font small = new Font("Segoe UI", 9f))
            using (Font big = new Font("Segoe UI Semibold", Math.Max(10f, height / 4.2f)))
            using (Brush inkBrush = new SolidBrush(ink))
            using (Brush dimBrush = new SolidBrush(Mix(bg, ink, 0.55)))
            using (StringFormat left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int idx = 0; idx < layout.Count; idx++)
                {
                    BarItem it = layout[idx];
                    Rectangle r = it.Rect;
                    bool isHover = idx == hover;
                    bool isActive = it.Kind == BarItem.Window && windows[it.Index].Hwnd == activeWindow && !windows[it.Index].Minimized;
                    if (it.Kind != BarItem.Clock && (isHover || isActive))
                        using (GraphicsPath p = Round(r, 6)) using (Brush b = new SolidBrush(isActive ? activeBg : hoverBg)) g.FillPath(b, p);

                    switch (it.Kind)
                    {
                        case BarItem.Menu:
                            {
                                int s = Math.Min(r.Width, r.Height) - 10;
                                Rectangle circle = new Rectangle(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2 + 2, s, s - 2);
                                using (Brush peach = new LinearGradientBrush(circle, Color.FromArgb(255, 180, 130), accent, 60f)) g.FillEllipse(peach, circle);
                                using (Brush leaf = new SolidBrush(Color.FromArgb(90, 190, 110))) g.FillEllipse(leaf, circle.X + s / 2 - 1, circle.Y - 4, s / 2, s / 3);
                                break;
                            }
                        case BarItem.Pinned:
                            {
                                AppEntry a = c.Pinned[it.Index];
                                int s = Math.Min(r.Width, r.Height) - 12;
                                Rectangle box = new Rectangle(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s);
                                Bitmap bmp = AppIcons.For(a.Path);
                                if (bmp != null) g.DrawImage(bmp, box);
                                else DrawLetter(g, box, a.Name, ink, bg);
                                break;
                            }
                        case BarItem.Window:
                            {
                                WinInfo w = windows[it.Index];
                                int s = Math.Min(20, r.Height - 12);
                                Rectangle box = new Rectangle(r.X + 8, r.Y + (r.Height - s) / 2, s, s);
                                Bitmap bmp = WindowList.IconOf(w);
                                if (bmp != null) g.DrawImage(bmp, box); else DrawLetter(g, box, w.Title, ink, bg);
                                Rectangle text = new Rectangle(box.Right + 6, r.Y, r.Right - box.Right - 10, r.Height);
                                if (text.Width > 12) g.DrawString(w.Title, small, w.Minimized ? dimBrush : inkBrush, text, left);
                                if (isActive || !w.Minimized)
                                    using (Pen pen = new Pen(isActive ? accent : Mix(bg, accent, 0.35), 3f)) g.DrawLine(pen, r.X + 8, r.Bottom - 2, r.Right - 8, r.Bottom - 2);
                                break;
                            }
                        case BarItem.Clock:
                            {
                                DateTime now = DateTime.Now;
                                if (c.ShowClock && c.ShowDate)
                                {
                                    g.DrawString(now.ToString("HH:mm"), small, inkBrush, new Rectangle(r.X, r.Y, r.Width, r.Height / 2 + 2), new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far });
                                    g.DrawString(now.ToString("dd.MM.yyyy"), small, dimBrush, new Rectangle(r.X, r.Y + r.Height / 2 + 1, r.Width, r.Height / 2), new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near });
                                }
                                else if (c.ShowClock) g.DrawString(now.ToString("HH:mm"), big, inkBrush, r, new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
                                else g.DrawString(now.ToString("dd.MM.yyyy"), small, inkBrush, r, new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
                                break;
                            }
                    }
                }
            }
        }

        static void DrawLetter(Graphics g, Rectangle box, string name, Color ink, Color bg)
        {
            using (Brush b = new SolidBrush(Mix(bg, ink, 0.25))) g.FillEllipse(b, box);
            string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            using (Font f = new Font("Segoe UI Semibold", Math.Max(7f, box.Height / 2.2f)))
            using (Brush t = new SolidBrush(ink))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(letter, f, t, box, sf);
        }

        // `peach.exe --render-bar out.png`: preview with made-up windows.
        public void RenderTo(string file, int width, string[] titles)
        {
            List<WinInfo> demo = new List<WinInfo>();
            for (int i = 0; i < titles.Length; i++) demo.Add(new WinInfo { Hwnd = new IntPtr(1000 + i), Title = titles[i], Minimized = i == titles.Length - 1 });
            using (Bitmap bmp = new Bitmap(width, cfg.Bar.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp)) Draw(g, width, cfg.Bar.Height, cfg.Bar, demo, new IntPtr(1000 + Math.Min(1, titles.Length - 1)), -1);
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics, Width, Height, cfg.Bar, wins, active, hover); }

        // ---- mouse -------------------------------------------------------------------------------------

        int HitTest(Point p)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].Kind != BarItem.Clock && items[i].Rect.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i != hover)
            {
                hover = i;
                string text = "";
                if (i >= 0)
                {
                    BarItem it = items[i];
                    if (it.Kind == BarItem.Menu) text = "Peach-Menü";
                    else if (it.Kind == BarItem.Pinned && it.Index < cfg.Bar.Pinned.Count) text = cfg.Bar.Pinned[it.Index].Name;
                    else if (it.Kind == BarItem.Window && it.Index < wins.Count) text = wins[it.Index].Title;
                }
                tip.SetToolTip(this, text);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            BarItem it = i >= 0 ? items[i] : new BarItem { Kind = -1 };
            if (e.Button == MouseButtons.Left)
            {
                if (it.Kind == BarItem.Menu) openMenu();
                else if (it.Kind == BarItem.Pinned && it.Index < cfg.Bar.Pinned.Count) launch(cfg.Bar.Pinned[it.Index]);
                else if (it.Kind == BarItem.Window && it.Index < wins.Count) { WindowList.ActivateOrMinimize(wins[it.Index].Hwnd); BeginInvoke(new Action(delegate { Tick(true); })); }
            }
            else if (e.Button == MouseButtons.Right)
            {
                ContextMenuStrip cm = new ContextMenuStrip();
                if (it.Kind == BarItem.Window && it.Index < wins.Count)
                {
                    IntPtr h = wins[it.Index].Hwnd;
                    cm.Items.Add("Fenster schließen", null, delegate { WindowList.Close(h); });
                    cm.Items.Add(new ToolStripSeparator());
                }
                cm.Items.Add("Leiste anpassen…", null, delegate { customize(); });
                cm.Items.Add("Leiste ausblenden", null, delegate { hideBar(); });
                cm.Show(this, e.Location);
            }
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Stop(); Unregister(); timer.Dispose(); tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
