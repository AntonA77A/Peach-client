using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Peach
{
    // The suggestion popup under the caret. It never takes focus, so typing continues in the editor.
    internal class CompletionList : Control
    {
        public const int RowH = 28, MaxRows = 9;
        List<Completion> items = new List<Completion>();
        int selected, top;
        public event EventHandler Accepted;

        public CompletionList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            Visible = false; Width = 520; TabStop = false;
        }

        public Completion Current { get { return selected >= 0 && selected < items.Count ? items[selected] : null; } }
        public int Count { get { return items.Count; } }

        public void SetItems(List<Completion> l)
        {
            items = l; selected = 0; top = 0;
            Height = Math.Min(items.Count, MaxRows) * RowH + 8;
            Invalidate();
        }

        public void Step(int d)
        {
            if (items.Count == 0) return;
            selected = (selected + d + items.Count) % items.Count;
            if (selected < top) top = selected;
            if (selected >= top + MaxRows) top = selected - MaxRows + 1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = top + (e.Y - 4) / RowH;
            if (i >= 0 && i < items.Count) { selected = i; Invalidate(); EventHandler h = Accepted; if (h != null) h(this, EventArgs.Empty); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e) { Step(e.Delta > 0 ? -1 : 1); base.OnMouseWheel(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Panel);
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8)) { using (Brush b = new SolidBrush(Theme.Card)) g.FillPath(b, p); using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p); }
            using (Font mono = new Font("Consolas", 10f, FontStyle.Bold))
            using (StringFormat nf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
            {
                for (int k = 0; k < MaxRows && top + k < items.Count; k++)
                {
                    Completion c = items[top + k];
                    Rectangle r = new Rectangle(4, 4 + k * RowH, Width - 8, RowH);
                    bool sel = top + k == selected;
                    if (sel) using (GraphicsPath p = Theme.Round(r, 6)) using (Brush b = new SolidBrush(Theme.CardHover)) g.FillPath(b, p);
                    Color kc = c.Kind == "k" ? CodeSyntax.ColorOf(Tok.Keyword) : c.Kind == "m" ? CodeSyntax.ColorOf(Tok.Builtin) : c.Kind == "f" ? CodeSyntax.ColorOf(Tok.String) : CodeSyntax.ColorOf(Tok.Variable);
                    Rectangle badge = new Rectangle(r.X + 6, r.Y + 5, 18, 18);
                    using (Brush b = new SolidBrush(Theme.WithAlpha(kc, 60))) g.FillEllipse(b, badge);
                    using (Brush b = new SolidBrush(kc)) using (StringFormat cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(c.Kind.Length > 0 ? c.Kind.ToUpperInvariant() : "·", Theme.Font(8f, true), b, badge, cf);
                    using (Brush b = new SolidBrush(Theme.Ink)) g.DrawString(c.Label, mono, b, new Rectangle(r.X + 32, r.Y, 190, r.Height), nf);
                    using (Brush b = new SolidBrush(Theme.Muted)) g.DrawString(c.Detail, Theme.Font(9f, false), b, new Rectangle(r.X + 226, r.Y, r.Width - 232, r.Height), nf);
                }
            }
        }
    }

    // Code field: RichTextBox with Peach colours, line numbers, undo that survives re-colouring, indent, auto-indent, suggestions.
    internal class CodeBox : Control
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Point l);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        const int WM_SETREDRAW = 0x0B, EM_GETEVENTMASK = 0x43B, EM_SETEVENTMASK = 0x431, EM_GETSCROLLPOS = 0x4DD, EM_SETSCROLLPOS = 0x4DE;

        class Snap { public string Text = ""; public int Caret; }

        readonly RichTextBox rtb = new RichTextBox();
        readonly Gutter gutter;
        readonly CompletionList popup = new CompletionList();
        readonly Timer hlTimer = new Timer(), commitTimer = new Timer();
        readonly List<Snap> undo = new List<Snap>(), redo = new List<Snap>();
        Snap committed = new Snap();
        CompletionResult pending;
        bool loading;
        bool popupOpen;   // our own flag: Control.Visible is false while the whole window is not shown (tests, previews)
        void HidePopup() { popupOpen = false; popup.Visible = false; }
        public string Lang = "txt";
        public ApiCatalog Catalog = ApiCatalog.Instance;
        public event EventHandler Edited, CaretChanged, SaveRequested;

        class Gutter : Control
        {
            readonly RichTextBox box;
            public Gutter(RichTextBox box) { this.box = box; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(34, 25, 22));
                int first = box.GetCharIndexFromPosition(new Point(0, 0));
                int line = box.GetLineFromCharIndex(first);
                int cur = box.GetLineFromCharIndex(box.SelectionStart);
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Far })
                    for (; ; line++)
                    {
                        int ci = box.GetFirstCharIndexFromLine(line);
                        if (ci < 0) break;
                        Point p = box.GetPositionFromCharIndex(ci);
                        if (p.Y > Height) break;
                        using (Brush b = new SolidBrush(line == cur ? Theme.Ink : Theme.Muted))
                            g.DrawString((line + 1).ToString(), box.Font, b, new RectangleF(0, p.Y, Width - 12, box.Font.Height + 2), sf);
                    }
                using (Pen pen = new Pen(Theme.Border)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            }
        }

        public CodeBox()
        {
            SetStyle(ControlStyles.ContainerControl, true);
            rtb.BorderStyle = BorderStyle.None; rtb.BackColor = Theme.Panel; rtb.ForeColor = Theme.Ink;
            rtb.Font = new Font("Consolas", 11.5f); rtb.WordWrap = false; rtb.AcceptsTab = true; rtb.DetectUrls = false;
            rtb.ScrollBars = RichTextBoxScrollBars.ForcedBoth; rtb.HideSelection = false; rtb.EnableAutoDragDrop = false; rtb.Multiline = true;
            gutter = new Gutter(rtb);
            Controls.Add(rtb); Controls.Add(gutter); Controls.Add(popup);
            popup.Accepted += delegate { AcceptCompletion(); };
            hlTimer.Interval = 110; hlTimer.Tick += delegate { hlTimer.Stop(); Highlight(); };
            commitTimer.Interval = 500; commitTimer.Tick += delegate { Commit(); };

            rtb.TextChanged += delegate
            {
                if (loading) return;
                hlTimer.Stop(); hlTimer.Start(); commitTimer.Stop(); commitTimer.Start();
                gutter.Invalidate();
                EventHandler h = Edited; if (h != null) h(this, EventArgs.Empty);
            };
            rtb.SelectionChanged += delegate { gutter.Invalidate(); EventHandler h = CaretChanged; if (h != null) h(this, EventArgs.Empty); };
            rtb.VScroll += delegate { gutter.Invalidate(); HidePopup(); };
            rtb.MouseDown += delegate { HidePopup(); };
            rtb.LostFocus += delegate { if (!popup.ClientRectangle.Contains(popup.PointToClient(Cursor.Position))) HidePopup(); };
            rtb.KeyDown += OnKeyDown;
            rtb.KeyPress += OnKeyPress;
            rtb.HandleCreated += delegate { try { SetWindowTheme(rtb.Handle, "DarkMode_Explorer", null); } catch { } };
            Layout += delegate { DoLayout(); };
            DoLayout();
        }

        void DoLayout()
        {
            const int gw = 58;
            gutter.SetBounds(0, 0, gw, Height);
            rtb.SetBounds(gw + 6, 4, Math.Max(10, Width - gw - 6), Math.Max(10, Height - 4));
        }

        public string Value { get { return rtb.Text; } }
        public int Line { get { return rtb.GetLineFromCharIndex(rtb.SelectionStart) + 1; } }
        public int Column { get { return rtb.SelectionStart - rtb.GetFirstCharIndexOfCurrentLine() + 1; } }
        public void FocusEditor() { rtb.Focus(); }
        internal bool PopupVisible { get { return popupOpen; } }
        internal int Caret { get { return rtb.SelectionStart; } set { rtb.SelectionStart = value; rtb.SelectionLength = 0; } }
        internal void Select(int start, int length) { rtb.Select(start, length); }
        // test hook: behaves like typing text at the caret
        internal void TypeText(string s) { rtb.SelectedText = s; }
        public void CaretToEnd() { rtb.SelectionStart = rtb.TextLength; rtb.SelectionLength = 0; }
        public Font CodeFont { get { return rtb.Font; } }

        public void Load(string text, string lang)
        {
            loading = true;
            Lang = lang;
            rtb.Text = (text ?? "").Replace("\r\n", "\n");
            undo.Clear(); redo.Clear();
            committed = new Snap { Text = rtb.Text, Caret = 0 };
            rtb.SelectionStart = 0; rtb.SelectionLength = 0;
            loading = false;
            Highlight();
            gutter.Invalidate();
        }

        // ---- colouring -------------------------------------------------------------------------------------
        public void Highlight()
        {
            string text = rtb.Text;
            if (text.Length > 300000) return;
            List<Span> spans = CodeSyntax.Spans(text, Lang);
            int selStart = rtb.SelectionStart, selLen = rtb.SelectionLength;
            Point scroll = new Point();
            IntPtr h = rtb.Handle;
            SendMessage(h, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);
            IntPtr mask = SendMessage(h, EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            SendMessage(h, EM_SETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            SendMessage(h, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                rtb.SelectAll(); rtb.SelectionColor = Theme.Ink;
                foreach (Span s in spans)
                {
                    if (s.Kind == Tok.Plain) continue;
                    rtb.Select(s.Start, s.Length);
                    rtb.SelectionColor = CodeSyntax.ColorOf(s.Kind);
                }
                rtb.Select(selStart, selLen);
                rtb.SelectionColor = Theme.Ink;
            }
            finally
            {
                SendMessage(h, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);
                SendMessage(h, EM_SETEVENTMASK, IntPtr.Zero, mask);
                SendMessage(h, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                rtb.Invalidate();
                gutter.Invalidate();
            }
        }

        // ---- undo / redo (the RichEdit one would also undo the colouring) -----------------------------------
        internal void Commit()
        {
            commitTimer.Stop();
            string now = rtb.Text;
            if (now == committed.Text) return;
            undo.Add(committed);
            if (undo.Count > 300) undo.RemoveAt(0);
            redo.Clear();
            committed = new Snap { Text = now, Caret = rtb.SelectionStart };
        }

        void Apply(Snap s)
        {
            loading = true;
            rtb.Text = s.Text;
            rtb.SelectionStart = Math.Min(s.Caret, s.Text.Length);
            loading = false;
            Highlight();
            EventHandler h = Edited; if (h != null) h(this, EventArgs.Empty);
        }

        public void Undo()
        {
            Commit();
            if (undo.Count == 0) return;
            redo.Add(committed);
            committed = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            Apply(committed);
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(committed);
            committed = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
            Apply(committed);
        }

        // ---- keyboard --------------------------------------------------------------------------------------
        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (popupOpen)
            {
                if (e.KeyCode == Keys.Down) { popup.Step(1); Eat(e); return; }
                if (e.KeyCode == Keys.Up) { popup.Step(-1); Eat(e); return; }
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab) { AcceptCompletion(); Eat(e); return; }
                if (e.KeyCode == Keys.Escape) { HidePopup(); Eat(e); return; }
            }
            if (e.Control && e.KeyCode == Keys.Space) { TryComplete(true); Eat(e); return; }
            if (e.Control && e.KeyCode == Keys.S) { EventHandler h = SaveRequested; if (h != null) h(this, EventArgs.Empty); Eat(e); return; }
            if (e.Control && e.KeyCode == Keys.Z && !e.Shift) { Undo(); Eat(e); return; }
            if (e.Control && (e.KeyCode == Keys.Y || (e.Shift && e.KeyCode == Keys.Z))) { Redo(); Eat(e); return; }
            if (e.Control && e.KeyCode == Keys.V) { string t = Clipboard.ContainsText() ? Clipboard.GetText() : null; if (t != null) rtb.SelectedText = t.Replace("\r\n", "\n"); Eat(e); return; }
            if (e.Control && (e.KeyCode == Keys.OemQuestion || e.KeyCode == Keys.Oem2 || e.KeyCode == Keys.D7 || e.KeyCode == Keys.Divide)) { ToggleComment(); Eat(e); return; }
            if (e.KeyCode == Keys.Tab && !e.Control) { Indent(e.Shift); Eat(e); return; }
            if (e.KeyCode == Keys.Enter && !e.Control && !e.Shift) { NewLine(); Eat(e); return; }
            if (e.KeyCode == Keys.Back && !popupOpen) { BeginInvoke(new Action(delegate { if (rtb.SelectionStart > 0) TryComplete(false); })); }
        }

        static void Eat(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

        void OnKeyPress(object sender, KeyPressEventArgs e)
        {
            char c = e.KeyChar;
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '\'' || c == '"' || c == '$')
                BeginInvoke(new Action(delegate { TryComplete(false); }));
            else if (!char.IsControl(c)) HidePopup();
        }

        // ---- suggestions -----------------------------------------------------------------------------------
        public void TryComplete(bool manual)
        {
            if (loading || rtb.SelectionLength > 0) { HidePopup(); return; }
            CompletionResult r = Completer.Complete(rtb.Text, rtb.SelectionStart, Lang, Catalog, manual);
            if (r.Items.Count == 0) { HidePopup(); return; }
            pending = r;
            popup.SetItems(r.Items);
            Point p = rtb.GetPositionFromCharIndex(rtb.SelectionStart);
            int x = rtb.Left + p.X, y = rtb.Top + p.Y + rtb.Font.Height + 4;
            if (x + popup.Width > Width - 4) x = Math.Max(4, Width - popup.Width - 4);
            if (y + popup.Height > Height - 4) y = Math.Max(4, rtb.Top + p.Y - popup.Height - 4);
            popup.Location = new Point(x, y);
            popupOpen = true; popup.Visible = true;
            popup.BringToFront();
        }

        internal void AcceptCompletion()
        {
            Completion c = popup.Current;
            if (c == null || pending == null) { HidePopup(); return; }
            int caret = rtb.SelectionStart;
            int start = Math.Max(0, Math.Min(pending.Start, caret));
            rtb.Select(start, caret - start);
            rtb.SelectedText = c.Insert;
            HidePopup();
        }

        // ---- editing helpers -------------------------------------------------------------------------------
        internal void NewLine()
        {
            int caret = rtb.SelectionStart;
            string text = rtb.Text;
            int ls = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
            string before = text.Substring(ls, caret - ls);
            int ws = 0; while (ws < before.Length && (before[ws] == ' ' || before[ws] == '\t')) ws++;
            string indent = before.Substring(0, ws);
            string trimmed = before.TrimEnd();
            bool opens = trimmed.Length > 0 && (trimmed.EndsWith("{") || trimmed.EndsWith("(") || trimmed.EndsWith("[") || (Lang == "py" && trimmed.EndsWith(":")));
            rtb.SelectedText = "\n" + indent + (opens ? "    " : "");
        }

        internal void Indent(bool outdent)
        {
            int s = rtb.SelectionStart, len = rtb.SelectionLength;
            if (len == 0 && !outdent) { rtb.SelectedText = "    "; return; }
            string text = rtb.Text;
            int firstLine = rtb.GetLineFromCharIndex(s), lastLine = rtb.GetLineFromCharIndex(s + len);
            int from = rtb.GetFirstCharIndexFromLine(firstLine);
            int to = lastLine + 1 < rtb.Lines.Length ? rtb.GetFirstCharIndexFromLine(lastLine + 1) - 1 : text.Length;
            string block = text.Substring(from, to - from);
            string[] lines = block.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!outdent) lines[i] = "    " + lines[i];
                else { int k = 0; while (k < 4 && k < lines[i].Length && lines[i][k] == ' ') k++; lines[i] = lines[i].Substring(k); }
            }
            string result = string.Join("\n", lines);
            rtb.Select(from, to - from);
            rtb.SelectedText = result;
            rtb.Select(from, result.Length);
        }

        string LineComment()
        {
            switch (Lang) { case "js": case "ts": return "// "; case "py": case "ps1": return "# "; case "bat": return "rem "; case "vbs": return "' "; default: return null; }
        }

        internal void ToggleComment()
        {
            string marker = LineComment();
            if (marker == null) return;
            string text = rtb.Text;
            int s = rtb.SelectionStart, len = rtb.SelectionLength;
            int firstLine = rtb.GetLineFromCharIndex(s), lastLine = rtb.GetLineFromCharIndex(s + len);
            int from = rtb.GetFirstCharIndexFromLine(firstLine);
            int to = lastLine + 1 < rtb.Lines.Length ? rtb.GetFirstCharIndexFromLine(lastLine + 1) - 1 : text.Length;
            string[] lines = text.Substring(from, to - from).Split('\n');
            bool allCommented = true;
            foreach (string l in lines) if (l.Trim().Length > 0 && !l.TrimStart().StartsWith(marker.Trim())) allCommented = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                int ws = 0; while (ws < lines[i].Length && (lines[i][ws] == ' ' || lines[i][ws] == '\t')) ws++;
                if (allCommented) { string rest = lines[i].Substring(ws); lines[i] = lines[i].Substring(0, ws) + (rest.StartsWith(marker) ? rest.Substring(marker.Length) : rest.Substring(marker.Trim().Length)); }
                else lines[i] = lines[i].Substring(0, ws) + marker + lines[i].Substring(ws);
            }
            string result = string.Join("\n", lines);
            rtb.Select(from, to - from);
            rtb.SelectedText = result;
            rtb.Select(from, result.Length);
        }

        // Inserts text at the caret; adds an import/include line at the top first when it is not there yet.
        public void InsertWithImport(string importPattern, string importLine, string insertText, int caretBack)
        {
            int caret = rtb.SelectionStart, selLen = rtb.SelectionLength;
            if (importLine != null && importPattern != null && !Regex.IsMatch(rtb.Text, importPattern))
            {
                rtb.Select(0, 0);
                rtb.SelectedText = importLine + "\n";
                caret += importLine.Length + 1;
            }
            rtb.Select(caret, selLen);
            rtb.SelectedText = insertText;
            if (caretBack > 0) rtb.SelectionStart = Math.Max(0, rtb.SelectionStart - caretBack);
            rtb.Focus();
        }
    }

    // Open files as tabs, with a dirty dot and a close button, plus a "+" button.
    internal class PeachTabs : Control
    {
        public readonly List<string> Titles = new List<string>();
        public readonly List<bool> Dirty = new List<bool>();
        public int Selected;
        int hot = -1;
        public event Action<int> TabSelected, TabClosed;
        public event Action<Point> PlusClicked;

        public PeachTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 38; Cursor = Cursors.Hand;
        }

        int TabW(int i) { return Math.Max(110, Math.Min(230, TextRenderer.MeasureText(Titles[i], Theme.Font(10f, true)).Width + 64)); }

        Rectangle TabRect(int i) { int x = 0; for (int k = 0; k < i; k++) x += TabW(k) + 6; return new Rectangle(x, 0, TabW(i), Height - 4); }
        Rectangle PlusRect() { int x = 0; for (int k = 0; k < Titles.Count; k++) x += TabW(k) + 6; return new Rectangle(x, 0, 38, Height - 4); }
        Rectangle CloseRect(int i) { Rectangle r = TabRect(i); return new Rectangle(r.Right - 30, r.Y + 6, 24, r.Height - 12); }

        int Hit(Point p, out bool close, out bool plus)
        {
            close = false; plus = false;
            if (PlusRect().Contains(p)) { plus = true; return -1; }
            for (int i = 0; i < Titles.Count; i++) if (TabRect(i).Contains(p)) { close = CloseRect(i).Contains(p); return i; }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { bool c, p; int h = Hit(e.Location, out c, out p); if (p) h = -2; if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            bool close, plus; int i = Hit(e.Location, out close, out plus);
            if (plus) { Action<Point> h = PlusClicked; if (h != null) h(new Point(PlusRect().X, Height)); }
            else if (i >= 0 && (close || e.Button == MouseButtons.Middle)) { Action<int> h = TabClosed; if (h != null) h(i); }
            else if (i >= 0) { Action<int> h = TabSelected; if (h != null) h(i); }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            for (int i = 0; i < Titles.Count; i++)
            {
                Rectangle r = TabRect(i);
                bool sel = i == Selected;
                using (GraphicsPath p = Theme.Round(r, 9))
                {
                    using (Brush b = new SolidBrush(sel ? Theme.Panel : (i == hot ? Theme.Card : Theme.Mix(Theme.Bg, Theme.Card, 0.5)))) g.FillPath(b, p);
                    using (Pen pen = new Pen(sel ? Theme.Accent : Theme.Border, sel ? 1.8f : 1f)) g.DrawPath(pen, p);
                }
                using (Brush b = new SolidBrush(sel ? Theme.Ink : Theme.Muted))
                using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(Titles[i], Theme.Font(10f, true), b, new Rectangle(r.X + 14, r.Y, r.Width - 48, r.Height), sf);
                Rectangle cr = CloseRect(i);
                if (Dirty[i]) using (Brush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cr.X + 7, cr.Y + cr.Height / 2 - 4, 9, 9);
                else using (Pen x = new Pen(sel || i == hot ? Theme.Muted : Theme.Border, 1.5f)) { g.DrawLine(x, cr.X + 7, cr.Y + 9, cr.X + 16, cr.Y + 18); g.DrawLine(x, cr.X + 16, cr.Y + 9, cr.X + 7, cr.Y + 18); }
            }
            Rectangle pr = PlusRect();
            using (GraphicsPath p = Theme.Round(pr, 9)) { using (Brush b = new SolidBrush(hot == -2 ? Theme.Card : Theme.Mix(Theme.Bg, Theme.Card, 0.5))) g.FillPath(b, p); using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p); }
            using (Pen x = new Pen(Theme.Muted, 1.8f)) { g.DrawLine(x, pr.X + 13, pr.Y + pr.Height / 2, pr.X + 25, pr.Y + pr.Height / 2); g.DrawLine(x, pr.X + 19, pr.Y + pr.Height / 2 - 6, pr.X + 19, pr.Y + pr.Height / 2 + 6); }
        }
    }

    // The addon code editor: tabs for the addon's files, code, API panel (Peach + the language's modules), live log.
    internal class AddonEditorForm : PeachForm
    {
        class FileBuf { public string Path, Text = "", Saved = ""; public int Caret; public string Lang = "txt"; public bool Dirty { get { return Text != Saved; } } }

        readonly Config cfg;
        readonly AddonHost host;
        readonly string addonId, addonDir;
        readonly List<FileBuf> files = new List<FileBuf>();
        readonly PeachTabs tabs = new PeachTabs();
        readonly CodeBox code = new CodeBox();
        readonly PeachList apiList = new PeachList();
        readonly PeachTextBox search;
        readonly Label doc, hint, status;
        readonly PeachButton fixPerms;
        readonly PeachTextBox log;
        readonly Timer lintTimer = new Timer(), logTimer = new Timer();
        int current = -1;
        List<ApiItem> shown = new List<ApiItem>();
        string checkMessage = "";
        string lastLog = "";
        bool refreshing;

        public AddonEditorForm(Config cfg, AddonHost host, string addonId, string addonName)
            : base("Editor – " + addonName, 1320, 840)
        {
            this.cfg = cfg; this.host = host; this.addonId = addonId; addonDir = Path.Combine(host.ModsDir, addonId);
            ShowInTaskbar = true; EscCloses = false;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1320, wa.Width - 40), Math.Min(840, wa.Height - 40));
            int W = ClientSize.Width, H = ClientSize.Height;
            const int M = 16, side = 372;

            tabs.Location = new Point(M, TitleH + 10); tabs.Size = new Size(W - M * 2 - 330, 38);
            tabs.TabSelected += delegate (int i) { Switch(i); };
            tabs.TabClosed += delegate (int i) { CloseTab(i); };
            tabs.PlusClicked += delegate (Point p) { PlusMenu(p); };
            Controls.Add(tabs);

            PeachButton run = new PeachButton("Speichern & Starten  (F5)", BtnStyle.Primary, 210, 38); run.Location = new Point(W - M - 210, TitleH + 10);
            run.Click += delegate { SaveAndRun(); };
            PeachButton save = new PeachButton("Speichern", BtnStyle.Secondary, 110, 38); save.Location = new Point(W - M - 210 - 8 - 110, TitleH + 10);
            save.Click += delegate { SaveAll(); };
            Controls.Add(run); Controls.Add(save);

            hint = AddLabel("", M + 4, TitleH + 56, W - M * 2 - 220, 28, true, 10f, false);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            fixPerms = new PeachButton("Rechte ergänzen", BtnStyle.Secondary, 180, 30); fixPerms.Location = new Point(W - M - 180, TitleH + 56); fixPerms.Visible = false;
            fixPerms.Click += delegate { AddMissingPermissions(); };
            Controls.Add(fixPerms);

            int top = TitleH + 92, logH = 150, statusH = 30;
            int codeBottom = H - logH - statusH - 24;
            code.Location = new Point(M, top); code.Size = new Size(W - M * 2 - side - 12, codeBottom - top);
            code.Edited += delegate { OnEdited(); };
            code.CaretChanged += delegate { UpdateStatus(); };
            code.SaveRequested += delegate { SaveAll(); };
            Controls.Add(code);

            int sx = W - M - side;
            search = new PeachTextBox(side, 38, false); search.Location = new Point(sx, top); search.Placeholder = "API durchsuchen … (z. B. notify, readFile, json)";
            search.TextChanged += delegate { RebuildApi(); };
            Controls.Add(search);
            int docH = 128;
            apiList.RowHeight = 54; apiList.Location = new Point(sx, top + 46); apiList.Size = new Size(side, codeBottom - top - 46 - docH - 8);
            apiList.SelectionChanged += delegate { ShowDoc(); };
            apiList.ItemActivated += delegate (int i) { InsertApi(i); };
            Controls.Add(apiList);
            doc = AddLabel("Doppelklick auf einen Eintrag fügt ihn samt Import ein.", sx + 2, codeBottom - docH, side - 4, docH, true, 9.5f, false);

            AddLabel("LOG  – live", M + 4, codeBottom + 8, 300, 18, true, 8.5f, true);
            log = new PeachTextBox(W - M * 2, logH - 28, true); log.Location = new Point(M, codeBottom + 28);
            log.ReadOnly = true; log.Inner.Font = new Font("Consolas", 9.5f);
            Controls.Add(log);
            status = AddLabel("", M + 4, H - statusH - 4, W - M * 2, 24, true, 9.5f, false);

            lintTimer.Interval = 600; lintTimer.Tick += delegate { lintTimer.Stop(); UpdateLint(); };
            logTimer.Interval = 1100; logTimer.Tick += delegate { PollLog(); };

            OpenInitialFiles();
            Shown += delegate
            {
                code.FocusEditor();
                if (host.CanRun) host.StartHost();
                logTimer.Start(); PollLog();
                ApiCatalog.Instance.EnsureLoaded(CurrentLang(), delegate { try { BeginInvoke(new Action(RebuildApi)); } catch { } });
            };
            FormClosing += OnClosing;
        }

        // F5 / Ctrl+S even when the code box does not have the focus
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { SaveAndRun(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        string CurrentLang() { return current >= 0 ? files[current].Lang : "js"; }

        // ---- files ---------------------------------------------------------------------------------------
        static readonly string[] TextExtensions = { ".js", ".mjs", ".ts", ".py", ".ps1", ".bat", ".cmd", ".vbs", ".json", ".txt", ".md", ".html", ".css" };

        void OpenInitialFiles()
        {
            string entry = null;
            try
            {
                System.Collections.IDictionary d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(addonDir, "mod.json"), Encoding.UTF8)) as System.Collections.IDictionary;
                entry = d == null ? null : d["entry"] as string;
            }
            catch { }
            if (entry != null) OpenFile(Path.GetFullPath(Path.Combine(addonDir, entry)));
            OpenFile(Path.Combine(addonDir, "mod.json"));
            if (files.Count > 0) Switch(0);
        }

        int OpenFile(string path)
        {
            for (int i = 0; i < files.Count; i++) if (string.Equals(files[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
            if (!File.Exists(path)) return -1;
            FileBuf b = new FileBuf { Path = path, Lang = CodeSyntax.LangOf(path) };
            try { b.Text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n"); } catch (Exception e) { Dialogs.Info(this, "Datei öffnen", e.Message); return -1; }
            b.Saved = b.Text;
            files.Add(b);
            tabs.Titles.Add(Path.GetFileName(path)); tabs.Dirty.Add(false);
            tabs.Invalidate();
            return files.Count - 1;
        }

        void Switch(int i)
        {
            if (i < 0 || i >= files.Count) return;
            if (current >= 0) { files[current].Text = code.Value; files[current].Caret = 0; }
            current = i; tabs.Selected = i;
            code.Load(files[i].Text, files[i].Lang);
            tabs.Invalidate();
            RebuildApi(); UpdateStatus(); UpdateLint();
            code.FocusEditor();
            ApiCatalog.Instance.EnsureLoaded(files[i].Lang, delegate { try { BeginInvoke(new Action(RebuildApi)); } catch { } });
        }

        void CloseTab(int i)
        {
            if (files.Count <= 1) return;
            if (i == current) files[i].Text = code.Value;
            if (files[i].Dirty && Dialogs.Ask(this, "Ungespeicherte Änderungen", "„" + Path.GetFileName(files[i].Path) + "“ wurde geändert. Speichern?", "Speichern", "Verwerfen")) SaveFile(files[i]);
            bool wasCurrent = i == current;
            files.RemoveAt(i); tabs.Titles.RemoveAt(i); tabs.Dirty.RemoveAt(i);
            if (wasCurrent) { current = -1; Switch(Math.Max(0, i - 1)); }
            else { if (current > i) current--; tabs.Selected = current; tabs.Invalidate(); }
        }

        void PlusMenu(Point at)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            List<string> others = new List<string>();
            try { foreach (string f in Directory.GetFiles(addonDir)) if (Array.IndexOf(TextExtensions, Path.GetExtension(f).ToLowerInvariant()) >= 0) others.Add(f); } catch { }
            foreach (string f in others)
            {
                bool open = false;
                foreach (FileBuf b in files) if (string.Equals(b.Path, f, StringComparison.OrdinalIgnoreCase)) open = true;
                if (open) continue;
                string path = f;
                m.Items.Add(Path.GetFileName(f), null, delegate { int i = OpenFile(path); if (i >= 0) Switch(i); });
            }
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Neue Datei…", null, delegate { NewFile(); });
            m.Show(tabs, at);
        }

        void NewFile()
        {
            string name = null;
            using (PeachForm f = new PeachForm("Neue Datei", 520, 250))
            {
                f.ShowInTaskbar = false;
                f.AddLabel("Dateiname im Addon-Ordner (z. B. helfer.js)", 30, PeachForm.TitleH + 22, 460, 22, true, 9.5f, true);
                PeachTextBox box = new PeachTextBox(460, 40, false); box.Location = new Point(30, PeachForm.TitleH + 48);
                f.Controls.Add(box);
                PeachButton ok = new PeachButton("Anlegen", BtnStyle.Primary, 130, 40); ok.Location = new Point(520 - 30 - 130, 250 - 62);
                ok.Click += delegate
                {
                    string n = box.Text.Trim();
                    if (!Regex.IsMatch(n, @"^[\w.-]+$") || Array.IndexOf(TextExtensions, Path.GetExtension(n).ToLowerInvariant()) < 0) { Dialogs.Info(f, "Neue Datei", "Bitte einen einfachen Namen mit Endung angeben (.js, .py, .ps1, .json …)."); return; }
                    if (File.Exists(Path.Combine(addonDir, n))) { Dialogs.Info(f, "Neue Datei", "Diese Datei gibt es schon."); return; }
                    name = n; f.DialogResult = DialogResult.OK;
                };
                PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(520 - 30 - 130 - 10 - 120, 250 - 62); cancel.Result = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            try
            {
                string path = Path.Combine(addonDir, name);
                File.WriteAllText(path, "", new UTF8Encoding(Path.GetExtension(name).ToLowerInvariant() == ".ps1"));
                int i = OpenFile(path);
                if (i >= 0) Switch(i);
            }
            catch (Exception e) { Dialogs.Info(this, "Neue Datei", e.Message); }
        }

        // ---- saving & running ----------------------------------------------------------------------------
        void SyncCurrent() { if (current >= 0) files[current].Text = code.Value; }

        void SaveFile(FileBuf b)
        {
            try
            {
                string text = b.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                File.WriteAllText(b.Path, text, new UTF8Encoding(b.Lang == "ps1"));   // PowerShell 5.1 needs the BOM for umlauts
                b.Saved = b.Text;
            }
            catch (Exception e) { Dialogs.Info(this, "Speichern", "Das hat nicht geklappt: " + e.Message); }
        }

        void SaveAll()
        {
            SyncCurrent();
            foreach (FileBuf b in files) if (b.Dirty) SaveFile(b);
            RefreshTabs();
            if (current >= 0) CheckSyntaxAsync(files[current]);
            UpdateStatus();
        }

        void RefreshTabs() { for (int i = 0; i < files.Count; i++) tabs.Dirty[i] = files[i].Dirty; tabs.Invalidate(); }

        void SaveAndRun()
        {
            SaveAll();
            string id = addonId;
            status.Text = "Starte „" + id + "“ …";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                host.Rescan();
                host.Stop(id);
                System.Threading.Thread.Sleep(500);
                bool ok = host.Start(id);
                System.Threading.Thread.Sleep(500);
                host.Refresh();
                try { BeginInvoke(new Action(delegate { status.Text = ok ? "Gestartet – siehe Log unten." : "Konnte nicht gestartet werden (läuft der Addon-Host?)."; UpdateLog(); })); } catch { }
            });
        }

        void CheckSyntaxAsync(FileBuf b)
        {
            string path = b.Path, lang = b.Lang;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool skipped;
                string err = SyntaxCheck.Check(lang, path, out skipped);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        checkMessage = skipped ? "" : (err == null ? "✓  Syntax in Ordnung" : "✗  " + err);
                        checkIsError = err != null && !skipped;
                        UpdateLint();
                    }));
                }
                catch { }
            });
        }

        bool checkIsError;

        // ---- hints: missing permissions, syntax result -----------------------------------------------------
        List<string> DeclaredPermissions()
        {
            List<string> l = new List<string>();
            foreach (FileBuf b in files)
                if (string.Equals(Path.GetFileName(b.Path), "mod.json", StringComparison.OrdinalIgnoreCase))
                    try
                    {
                        System.Collections.IDictionary d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(b == (current >= 0 ? files[current] : null) ? code.Value : b.Text) as System.Collections.IDictionary;
                        System.Collections.IEnumerable p = d == null ? null : d["permissions"] as System.Collections.IEnumerable;
                        if (p != null) foreach (object o in p) if (o != null) l.Add(o.ToString());
                    }
                    catch { }
            return l;
        }

        List<string> MissingPermissions()
        {
            if (current < 0) return new List<string>();
            string lang = files[current].Lang;
            if (lang == "json" || lang == "txt") return new List<string>();
            return PermissionLint.Missing(code.Value, lang, DeclaredPermissions());
        }

        void UpdateLint()
        {
            List<string> missing = MissingPermissions();
            if (missing.Count > 0)
            {
                hint.Text = "⚠  Dein Code braucht in mod.json noch das Recht: " + string.Join(", ", missing.ToArray());
                hint.ForeColor = Theme.Accent; fixPerms.Visible = true;
            }
            else
            {
                fixPerms.Visible = false;
                if (checkMessage.Length > 0) { hint.Text = checkMessage; hint.ForeColor = checkIsError ? Theme.Danger : Theme.Ok; }
                else { hint.Text = "Strg + Leertaste: Vorschläge  ·  Strg + S: speichern  ·  F5: speichern & starten  ·  Strg + /: Kommentar"; hint.ForeColor = Theme.Muted; }
            }
        }

        void AddMissingPermissions()
        {
            List<string> missing = MissingPermissions();
            if (missing.Count == 0) return;
            int modIndex = -1;
            for (int i = 0; i < files.Count; i++) if (string.Equals(Path.GetFileName(files[i].Path), "mod.json", StringComparison.OrdinalIgnoreCase)) modIndex = i;
            if (modIndex < 0) { Dialogs.Info(this, "Rechte", "mod.json wurde nicht gefunden."); return; }
            SyncCurrent();
            string json = files[modIndex].Text;
            List<string> all = DeclaredPermissions();
            foreach (string m in missing) if (!all.Contains(m)) all.Add(m);
            StringBuilder arr = new StringBuilder();
            for (int i = 0; i < all.Count; i++) arr.Append(i > 0 ? ", " : "").Append('"').Append(all[i]).Append('"');
            string replaced = Regex.IsMatch(json, "\"permissions\"\\s*:\\s*\\[[^\\]]*\\]")
                ? Regex.Replace(json, "\"permissions\"\\s*:\\s*\\[[^\\]]*\\]", "\"permissions\": [" + arr + "]")
                : new Regex("\\{").Replace(json, "{\n  \"permissions\": [" + arr + "],", 1);
            files[modIndex].Text = replaced;
            if (current == modIndex) code.Load(replaced, "json");
            SaveFile(files[modIndex]);
            RefreshTabs(); UpdateLint(); UpdateStatus();
            status.Text = "Rechte in mod.json ergänzt und gespeichert. Mit F5 neu starten, damit sie gelten.";
        }

        // ---- editing events, status ------------------------------------------------------------------------
        void OnEdited()
        {
            if (current < 0) return;
            files[current].Text = code.Value;
            tabs.Dirty[current] = files[current].Dirty; tabs.Invalidate();
            checkMessage = ""; checkIsError = false;
            lintTimer.Stop(); lintTimer.Start();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (current < 0) return;
            FileBuf b = files[current];
            string langName = b.Lang == "js" ? "JavaScript" : b.Lang == "ts" ? "TypeScript" : b.Lang == "py" ? "Python" : b.Lang == "ps1" ? "PowerShell" : b.Lang == "bat" ? "Batch" : b.Lang == "vbs" ? "VBScript" : b.Lang == "json" ? "JSON" : "Text";
            status.Text = "Zeile " + code.Line + ", Spalte " + code.Column + "   ·   " + langName + "   ·   " + (b.Text != b.Saved ? "● ungespeichert" : "gespeichert") + "   ·   mods\\" + addonId;
        }

        // ---- API panel -------------------------------------------------------------------------------------
        static string GroupLetter(ApiItem it) { return it.Group == "Peach" ? "P" : it.Group == "Modul" ? "M" : it.Group == "Befehl" ? "B" : "f"; }
        static Color GroupColor(ApiItem it) { return it.Group == "Peach" ? Theme.Accent : it.Group == "Modul" ? Color.FromArgb(118, 190, 255) : it.Group == "Befehl" ? Color.FromArgb(150, 130, 220) : Color.FromArgb(120, 200, 150); }

        void RebuildApi()
        {
            shown = ApiCatalog.Instance.Search(CurrentLang(), search.Text, 300);
            apiList.Rows.Clear();
            foreach (ApiItem it in shown)
            {
                RowItem r = new RowItem();
                r.Title = it.Name; r.Subtitle = it.Signature.StartsWith(it.Name) && it.Signature.Length > it.Name.Length ? it.Signature.Substring(it.Name.Length) : it.Doc;
                r.Letter = GroupLetter(it); r.Tint = GroupColor(it); r.Tag = it;
                apiList.Rows.Add(r);
            }
            apiList.EmptyText = ApiCatalog.Instance.Ready(CurrentLang()) ? "Nichts gefunden." : "Lade die APIs aus dem installierten " + (CurrentLang() == "py" ? "Python" : CurrentLang() == "ps1" ? "PowerShell" : "Node.js") + " …";
            apiList.RowsChanged();
            ShowDoc();
        }

        void ShowDoc()
        {
            int i = apiList.SelectedIndex;
            if (i < 0 || i >= shown.Count) { doc.Text = "Doppelklick auf einen Eintrag fügt ihn samt Import ein."; return; }
            ApiItem it = shown[i];
            doc.Text = it.Signature + "\n" + it.Doc + (it.Permission.Length > 0 ? "\nBraucht das Recht „" + it.Permission + "“ in mod.json." : "");
        }

        static string VarName(string module) { return Regex.Replace(module, @"[^A-Za-z0-9_]+(.)?", delegate (Match m) { return m.Groups[1].Success ? m.Groups[1].Value.ToUpperInvariant() : ""; }); }

        void InsertApi(int i)
        {
            if (i < 0 || i >= shown.Count || current < 0) return;
            ApiItem it = shown[i];
            string lang = CodeboxLang();
            string importPattern = null, importLine = null, insert = it.Name;
            int back = 0;
            if (lang == "js")
            {
                if (it.Group == "Peach") { importPattern = @"require\(\s*['""]peach['""]\s*\)"; importLine = "const peach = require('peach');"; }
                else if (it.Module.Length > 0) { string v = VarName(it.Module); importPattern = @"require\(\s*['""](?:node:)?" + Regex.Escape(it.Module) + @"['""]\s*\)"; importLine = "const " + v + " = require('" + it.Module + "');"; insert = it.Group == "Modul" ? v : v + it.Name.Substring(it.Module.Length); }
                if (it.Kind == "f") { insert += "()"; back = 1; }
            }
            else if (lang == "py")
            {
                if (it.Group == "Peach") { importPattern = @"^\s*import\s+peach\b"; importLine = "import peach"; }
                else if (it.Module.Length > 0) { importPattern = @"^\s*import\s+" + Regex.Escape(it.Module) + @"\b"; importLine = "import " + it.Module; }
                if (it.Kind == "f") { insert += "()"; back = 1; }
            }
            else if (lang == "ps1")
            {
                if (it.Group == "Peach") { importPattern = @"peach\.ps1"; importLine = ". \"$env:PEACH_SDK\\peach.ps1\""; }
                insert += " ";
            }
            else if (lang == "bat" || lang == "vbs") insert = it.Name;
            code.InsertWithImport(importPattern, importLine, insert, back);
        }

        string CodeboxLang() { return Completer.NormLang(CurrentLang()); }

        // ---- live log --------------------------------------------------------------------------------------
        void PollLog()
        {
            if (refreshing || IsDisposed) return;
            refreshing = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                host.Refresh();
                try { BeginInvoke(new Action(delegate { refreshing = false; UpdateLog(); })); } catch { refreshing = false; }
            });
        }

        void UpdateLog()
        {
            AddonInfo a = host.Addons.Find(delegate (AddonInfo x) { return x.Id == addonId; });
            StringBuilder sb = new StringBuilder();
            if (!host.Connected) sb.Append(host.NodePath == null ? "Node.js nicht gefunden – Script-Addons können ohne Node.js nicht laufen." : "Addon-Host startet …");
            else if (a == null) sb.Append("(Addon ist dem Host noch nicht bekannt – speichern & starten)");
            else
            {
                sb.Append("Status: ").Append(a.Status == "running" ? "läuft" : a.Status == "crashed" ? "abgestürzt" : "gestoppt").Append("\r\n");
                foreach (AddonLog l in a.Log)
                    sb.Append(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(l.T).ToLocalTime().ToString("HH:mm:ss")).Append("  ").Append(l.S == "err" ? "! " : (l.S == "sys" ? "· " : "  ")).Append(l.M).Append("\r\n");
            }
            string text = sb.ToString();
            if (text == lastLog) return;
            lastLog = text;
            log.Text = text;
            log.Inner.SelectionStart = log.Inner.TextLength; log.Inner.ScrollToCaret();
        }

        // ---- closing ---------------------------------------------------------------------------------------
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            SyncCurrent();
            bool dirty = false;
            foreach (FileBuf b in files) if (b.Dirty) dirty = true;
            if (dirty)
            {
                if (Dialogs.Ask(this, "Ungespeicherte Änderungen", "Es gibt ungespeicherte Änderungen im Editor. Speichern?", "Speichern", "Verwerfen"))
                    foreach (FileBuf b in files) if (b.Dirty) SaveFile(b);
            }
            logTimer.Stop(); lintTimer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { logTimer.Dispose(); lintTimer.Dispose(); }
            base.Dispose(disposing);
        }

        // Preview helpers (UiPreview): fill the search box / pick a row without a user.
        public void PreviewSearch(string text) { search.Text = text; RebuildApi(); }
        public void PreviewSelectApi(int i) { apiList.SelectRow(i); }
        public CodeBox Code { get { return code; } }
        public void PreviewSwitch(int i) { Switch(i); }
    }
}
