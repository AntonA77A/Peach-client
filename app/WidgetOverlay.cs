using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Peach
{
    internal class WidgetLine
    {
        public string Label = "", Text = "";
        public double Value = -1;       // 0..100 draws a bar, -1 = none
    }

    internal static class SpotifyInfo
    {
        // The Spotify desktop app shows "Artist - Song" as its window title while it plays; otherwise just "Spotify ...".
        public static bool ParseTitle(string title, out string artist, out string song)
        {
            artist = null; song = null;
            if (string.IsNullOrWhiteSpace(title)) return false;
            string t = title.Trim();
            if (t.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) || t.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) || t.Equals("Werbung", StringComparison.OrdinalIgnoreCase)) return false;
            int i = t.IndexOf(" - ", StringComparison.Ordinal);
            if (i <= 0 || i + 3 >= t.Length) return false;
            artist = t.Substring(0, i).Trim(); song = t.Substring(i + 3).Trim();
            return artist.Length > 0 && song.Length > 0;
        }

        public static string ReadTitle()
        {
            foreach (Process p in Process.GetProcessesByName("Spotify"))
            {
                try { string t = p.MainWindowTitle; if (!string.IsNullOrEmpty(t)) return t; } catch { }
                finally { p.Dispose(); }
            }
            return null;
        }
    }

    internal static class MediaKeys
    {
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public static void Send(string what)
        {
            byte vk = what == "next" ? (byte)0xB0 : (what == "prev" ? (byte)0xB1 : (byte)0xB3);
            keybd_event(vk, 0, 1, UIntPtr.Zero);            // KEYEVENTF_EXTENDEDKEY
            keybd_event(vk, 0, 3, UIntPtr.Zero);            // ... | KEYEVENTF_KEYUP
        }
    }

    internal static class OverlayModel
    {
        public static readonly string[] AllWidgets = { "spotify", "cpu", "gpu", "ram", "clock", "session" };

        public static string Title(string widget)
        {
            switch (widget)
            {
                case "spotify": return "Spotify";
                case "cpu": return "CPU";
                case "gpu": return "GPU";
                case "ram": return "RAM";
                case "clock": return "Uhrzeit";
                case "session": return "Spielzeit";
                default: return widget;
            }
        }

        public static string Duration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
        }

        // session = null when Minecraft is not running
        public static List<WidgetLine> Build(List<string> widgets, SystemStats s, DateTime now, string spotifyTitle, TimeSpan? session)
        {
            List<WidgetLine> l = new List<WidgetLine>();
            foreach (string w in widgets)
            {
                switch (w)
                {
                    case "spotify":
                        {
                            string a, song;
                            WidgetLine sp = new WidgetLine { Label = "SPOTIFY" };
                            if (SpotifyInfo.ParseTitle(spotifyTitle, out a, out song)) sp.Text = song + "  ·  " + a;
                            else sp.Text = spotifyTitle == null ? "Spotify läuft nicht" : "Pausiert";
                            l.Add(sp); break;
                        }
                    case "cpu": l.Add(new WidgetLine { Label = "CPU", Text = Math.Round(s.Cpu).ToString("0") + " %", Value = s.Cpu }); break;
                    case "gpu": l.Add(new WidgetLine { Label = "GPU", Text = s.Gpu < 0 ? "–" : Math.Round(s.Gpu).ToString("0") + " %", Value = s.Gpu }); break;
                    case "ram": l.Add(new WidgetLine { Label = "RAM", Text = (s.RamUsedMb / 1024.0).ToString("0.0") + " / " + (s.RamTotalMb / 1024.0).ToString("0.0") + " GB", Value = s.RamPct }); break;
                    case "clock": l.Add(new WidgetLine { Label = "UHRZEIT", Text = now.ToString("HH:mm") }); break;
                    case "session": l.Add(new WidgetLine { Label = "SPIELZEIT", Text = session.HasValue ? Duration(session.Value) : "–" }); break;
                }
            }
            return l;
        }
    }

    // A small click-through window with your widgets, drawn over Minecraft (windowed or borderless; not over exclusive fullscreen).
    internal class WidgetOverlay : Form
    {
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        readonly Config cfg;
        readonly StatsSampler sampler = new StatsSampler();
        readonly Timer timer = new Timer();
        List<WidgetLine> lines = new List<WidgetLine>();
        Process game; DateTime gameChecked = DateTime.MinValue;
        const int W = 270;

        public WidgetOverlay(Config cfg)
        {
            this.cfg = cfg;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel; DoubleBuffered = true; Size = new Size(W, 60);
            timer.Interval = 1000; timer.Tick += delegate { Tick(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00080000;    // NOACTIVATE | TOOLWINDOW | TRANSPARENT (click-through) | LAYERED
                return cp;
            }
        }

        public void Start() { Tick(); timer.Start(); }
        public void Stop() { timer.Stop(); Hide(); }

        Process Game()
        {
            if ((DateTime.Now - gameChecked).TotalSeconds > 3 || (game != null && game.HasExited))
            {
                gameChecked = DateTime.Now;
                if (game != null) game.Dispose();
                game = Minecraft.FindGameProcess();
            }
            return game;
        }

        void Tick()
        {
            try
            {
                MinecraftConfig m = cfg.Minecraft;
                Process g = Game();
                bool gameFront = false; Rectangle area = Screen.PrimaryScreen.WorkingArea;
                if (g != null)
                {
                    try
                    {
                        RECT r; IntPtr h = g.MainWindowHandle;
                        if (h != IntPtr.Zero && GetWindowRect(h, out r)) { area = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); gameFront = Native.GetForegroundWindow() == h; }
                    }
                    catch { }
                }
                bool show = m.OverlayEnabled && (m.OverlayOnlyMinecraft ? gameFront : true);
                if (!show) { if (Visible) Hide(); return; }
                TimeSpan? session = null;
                try { if (g != null) session = DateTime.Now - g.StartTime; } catch { }
                SetLines(OverlayModel.Build(m.Widgets, sampler.Sample(m.Widgets.Contains("gpu")), DateTime.Now, m.Widgets.Contains("spotify") ? SpotifyInfo.ReadTitle() : null, session), area);
                Opacity = Math.Max(0.3, Math.Min(1.0, m.OverlayOpacity / 100.0));
                if (!Visible) Show();
            }
            catch { }
        }

        public void SetLines(List<WidgetLine> newLines, Rectangle area)
        {
            lines = newLines;
            int h = 14 + lines.Count * 26 + 4;
            Size = new Size(W, Math.Max(40, h));
            const int margin = 14;
            string c = cfg.Minecraft.OverlayCorner;
            int x = c.EndsWith("left") ? area.Left + margin : area.Right - W - margin;
            int y = c.StartsWith("top") ? area.Top + margin : area.Bottom - Height - margin;
            Location = new Point(x, y);
            if (IsHandleCreated) SetWindowRgn(Handle, CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 18, 18), true);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowRgn(Handle, CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 18, 18), true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int y = 10;
            foreach (WidgetLine l in lines)
            {
                using (Brush b = new SolidBrush(Theme.Muted)) g.DrawString(l.Label, Theme.Font(8f, true), b, 14, y + 3);
                if (l.Value >= 0)
                {
                    Rectangle bar = new Rectangle(70, y + 9, 70, 6);
                    using (GraphicsPath bp = Theme.Round(bar, 3)) using (Brush b = new SolidBrush(Theme.Bg)) g.FillPath(b, bp);
                    if (l.Value > 0)
                    {
                        Rectangle f = new Rectangle(bar.X, bar.Y, Math.Max(6, (int)(bar.Width * Math.Min(100, l.Value) / 100.0)), bar.Height);
                        using (GraphicsPath bp = Theme.Round(f, 3)) using (Brush b = new SolidBrush(StatTile.LoadColor(l.Value))) g.FillPath(b, bp);
                    }
                    using (Brush b = new SolidBrush(Theme.Ink)) g.DrawString(l.Text, Theme.Font(9f, true), b, 148, y + 2);
                }
                else
                {
                    string t = l.Text;
                    using (Brush b = new SolidBrush(Theme.Ink)) g.DrawString(t, Theme.Font(9f, true), b, new RectangleF(l.Label.Length > 6 ? 84 : 70, y + 2, W - (l.Label.Length > 6 ? 84 : 70) - 10, 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
                y += 26;
            }
        }

        // for previews: renders the overlay with the given lines on a dark "game" background
        public void RenderPreview(string file, List<WidgetLine> l)
        {
            lines = l; Size = new Size(W, 14 + l.Count * 26 + 4);
            using (Bitmap card = new Bitmap(Width, Height))
            {
                DrawToBitmap(card, new Rectangle(0, 0, Width, Height));
                using (Bitmap bg = new Bitmap(520, Height + 60))
                using (Graphics g = Graphics.FromImage(bg))
                {
                    using (Brush b = new LinearGradientBrush(new Rectangle(0, 0, bg.Width, bg.Height), Color.FromArgb(70, 110, 60), Color.FromArgb(40, 70, 120), 90f)) g.FillRectangle(b, 0, 0, bg.Width, bg.Height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Theme.Round(new Rectangle(bg.Width - Width - 24, 30, Width, Height), 12)) { g.SetClip(p); g.DrawImage(card, bg.Width - Width - 24, 30); g.ResetClip(); }
                    bg.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); sampler.Dispose(); if (game != null) game.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
