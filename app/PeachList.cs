using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Peach
{
    internal class RowAction
    {
        public string Id, Label;
        public bool Danger;
        public RowAction(string id, string label, bool danger) { Id = id; Label = label; Danger = danger; }
    }

    internal class RowItem
    {
        public string Title = "", Subtitle = "", Status = "", Letter = "";
        public Color StatusColor = Theme.Muted, Tint = Theme.Accent;
        public Bitmap Icon;                       // shared bitmaps: the list never disposes them
        public bool IconTried;                    // set once PeachList.IconProvider was asked (icons load lazily, only for visible rows)
        public bool IsHeader;                     // small section label, not selectable
        public bool HasToggle, On, Enabled = true;
        public List<string> Chips = new List<string>();
        public List<RowAction> Actions = new List<RowAction>();
        public object Tag;
    }

    // Scrollable list of Peach cards: icon, title, subtitle, chips, status, a switch and small action buttons.
    // Used for editors (compact rows) and the Addons app (tall rows).
    internal class PeachList : Control
    {
        public const int HeaderH = 40, Gap = 8, SbW = 10;
        public readonly List<RowItem> Rows = new List<RowItem>();
        public int RowHeight = 62;
        public bool MultiSelect;
        public string EmptyText = "";
        public Func<RowItem, Bitmap> IconProvider;   // called once per row, when it is first drawn

        public event EventHandler SelectionChanged;
        public event Action<int> ItemActivated;
        public event Action<int> ToggleChanged;
        public event Action<int, string> ActionClicked;

        readonly List<int> sel = new List<int>();
        int hot = -1, anchor = -1, scroll;
        int hotToggle = -1;
        string hotAction;
        bool dragThumb;
        int dragOffset;

        public PeachList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Bg;
        }

        // ---- geometry (pure, unit-tested) -------------------------------------------------------------
        int HeightOf(int i) { return Rows[i].IsHeader ? HeaderH : RowHeight; }

        public int RowTop(int i) { int y = 0; for (int k = 0; k < i; k++) y += HeightOf(k); return y; }
        public int TotalHeight { get { int y = 0; for (int k = 0; k < Rows.Count; k++) y += HeightOf(k); return y; } }
        bool NeedsScroll { get { return TotalHeight > Height; } }
        int ContentWidth { get { return Width - (NeedsScroll ? SbW + 6 : 0); } }
        int MaxScroll { get { return Math.Max(0, TotalHeight - Height); } }

        public Rectangle CardRect(int i)
        {
            int y = RowTop(i) - scroll;
            return new Rectangle(0, y + Gap / 2, ContentWidth, HeightOf(i) - Gap);
        }

        public Rectangle ToggleRect(int i)
        {
            Rectangle c = CardRect(i);
            return new Rectangle(c.Right - 18 - 48, c.Y + (c.Height - 26) / 2, 48, 26);
        }

        public List<Rectangle> ActionRects(int i)
        {
            Rectangle c = CardRect(i);
            RowItem it = Rows[i];
            int right = it.HasToggle ? ToggleRect(i).X - 14 : c.Right - 18;
            Rectangle[] rects = new Rectangle[it.Actions.Count];
            for (int k = it.Actions.Count - 1; k >= 0; k--)
            {
                int w = TextRenderer.MeasureText(it.Actions[k].Label, Theme.Font(9.5f, true)).Width + 30;
                rects[k] = new Rectangle(right - w, c.Y + (c.Height - 30) / 2, w, 30);
                right -= w + 8;
            }
            return new List<Rectangle>(rects);
        }

        // kind: 0 nothing, 1 row, 2 toggle, 3 action button
        public int HitTest(Point p, out int row, out string actionId)
        {
            row = -1; actionId = null;
            for (int i = 0; i < Rows.Count; i++)
            {
                Rectangle c = CardRect(i);
                if (c.Bottom < 0) continue;
                if (c.Y > Height) break;
                if (Rows[i].IsHeader || !c.Contains(p)) continue;
                row = i;
                if (Rows[i].HasToggle && ToggleRect(i).Inflate2(6).Contains(p)) return 2;
                List<Rectangle> acts = ActionRects(i);
                for (int k = 0; k < acts.Count; k++) if (acts[k].Contains(p)) { actionId = Rows[i].Actions[k].Id; return 3; }
                return 1;
            }
            return 0;
        }

        // ---- selection -----------------------------------------------------------------------------------
        public int SelectedIndex { get { return sel.Count > 0 ? sel[0] : -1; } }
        public List<int> SelectedIndices { get { List<int> l = new List<int>(sel); l.Sort(); return l; } }

        public void SelectRow(int i)
        {
            sel.Clear();
            if (i >= 0 && i < Rows.Count && !Rows[i].IsHeader) { sel.Add(i); anchor = i; EnsureVisible(i); }
            Invalidate();
            EventHandler h = SelectionChanged; if (h != null) h(this, EventArgs.Empty);
        }

        public void EnsureVisible(int i)
        {
            if (i < 0 || i >= Rows.Count) return;
            int top = RowTop(i), bottom = top + HeightOf(i);
            if (top < scroll) scroll = top;
            else if (bottom > scroll + Height) scroll = bottom - Height;
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
        }

        // Call after changing Rows.
        public void RowsChanged()
        {
            for (int k = sel.Count - 1; k >= 0; k--) if (sel[k] >= Rows.Count || Rows[sel[k]].IsHeader) sel.RemoveAt(k);
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            hot = -1; hotToggle = -1; hotAction = null;
            Invalidate();
        }

        int Step(int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < Rows.Count; i += dir) if (!Rows[i].IsHeader) return i;
            return from;
        }

        // ---- input ---------------------------------------------------------------------------------------
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                int cur = SelectedIndex;
                int next = cur < 0 ? Step(e.KeyCode == Keys.Down ? -1 : Rows.Count, e.KeyCode == Keys.Down ? 1 : -1) : Step(cur, e.KeyCode == Keys.Down ? 1 : -1);
                if (next >= 0 && next < Rows.Count) SelectRow(next);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && SelectedIndex >= 0) { Action<int> h = ItemActivated; if (h != null) h(SelectedIndex); e.Handled = true; }
            else if (e.KeyCode == Keys.Space && SelectedIndex >= 0 && Rows[SelectedIndex].HasToggle) { Toggle(SelectedIndex); e.Handled = true; }
            base.OnKeyDown(e);
        }

        void Toggle(int i)
        {
            if (!Rows[i].Enabled) return;
            Rows[i].On = !Rows[i].On;
            Invalidate();
            Action<int> h = ToggleChanged; if (h != null) h(i);
        }

        Rectangle ThumbRect()
        {
            int total = Math.Max(1, TotalHeight);
            int th = Math.Max(36, Height * Height / total);
            int ty = MaxScroll == 0 ? 0 : (int)((Height - th) * (scroll / (double)MaxScroll));
            return new Rectangle(Width - SbW, ty, SbW, th);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta / 120 * 56));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            if (NeedsScroll && e.X >= Width - SbW - 4)
            {
                Rectangle t = ThumbRect();
                dragThumb = true; Capture = true;
                dragOffset = t.Contains(e.Location) ? e.Y - t.Y : t.Height / 2;
                MoveThumb(e.Y);
                return;
            }
            int row; string act;
            int kind = HitTest(e.Location, out row, out act);
            if (kind == 2) { Toggle(row); }
            else if (kind == 3) { Action<int, string> h = ActionClicked; if (h != null) h(row, act); }
            else if (kind == 1)
            {
                if (MultiSelect && (ModifierKeys & Keys.Control) != 0)
                {
                    if (!sel.Remove(row)) sel.Add(row);
                    anchor = row;
                    Invalidate(); EventHandler h = SelectionChanged; if (h != null) h(this, EventArgs.Empty);
                }
                else if (MultiSelect && (ModifierKeys & Keys.Shift) != 0 && anchor >= 0)
                {
                    sel.Clear();
                    for (int i = Math.Min(anchor, row); i <= Math.Max(anchor, row); i++) if (!Rows[i].IsHeader) sel.Add(i);
                    Invalidate(); EventHandler h = SelectionChanged; if (h != null) h(this, EventArgs.Empty);
                }
                else SelectRow(row);
            }
            base.OnMouseDown(e);
        }

        void MoveThumb(int y)
        {
            Rectangle t = ThumbRect();
            int range = Math.Max(1, Height - t.Height);
            scroll = Math.Max(0, Math.Min(MaxScroll, (int)((y - dragOffset) / (double)range * MaxScroll)));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragThumb) { MoveThumb(e.Y); return; }
            int row; string act;
            int kind = HitTest(e.Location, out row, out act);
            int newHot = kind > 0 ? row : -1;
            int newToggle = kind == 2 ? row : -1;
            string newAct = kind == 3 ? row + ":" + act : null;
            if (newHot != hot || newToggle != hotToggle || newAct != hotAction)
            {
                hot = newHot; hotToggle = newToggle; hotAction = newAct;
                Cursor = kind >= 2 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragThumb = false; Capture = false; base.OnMouseUp(e); }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hot != -1 || hotToggle != -1 || hotAction != null) { hot = -1; hotToggle = -1; hotAction = null; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int row; string act;
            if (HitTest(e.Location, out row, out act) == 1) { Action<int> h = ItemActivated; if (h != null) h(row); }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnResize(EventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            base.OnResize(e);
        }

        // ---- painting ------------------------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            if (Rows.Count == 0 && EmptyText.Length > 0)
            {
                using (Brush b = new SolidBrush(Theme.Muted))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(EmptyText, Theme.Font(11f, false), b, new RectangleF(20, 0, Width - 40, Height), sf);
                return;
            }

            for (int i = 0; i < Rows.Count; i++)
            {
                Rectangle c = CardRect(i);
                if (c.Bottom < 0) continue;
                if (c.Y > Height) break;
                RowItem it = Rows[i];
                if (it.IsHeader)
                {
                    using (Brush b = new SolidBrush(Theme.Muted))
                    using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center })
                        g.DrawString(it.Title.ToUpperInvariant(), Theme.Font(8.5f, true), b, new RectangleF(6, c.Y - 2, c.Width, c.Height + 4), sf);
                    continue;
                }
                PaintRow(g, i, it, c);
            }

            if (NeedsScroll)
            {
                Rectangle t = ThumbRect();
                using (GraphicsPath p = Theme.Round(new Rectangle(t.X + 2, t.Y, t.Width - 4, t.Height), 3)) using (Brush b = new SolidBrush(dragThumb ? Theme.Muted : Theme.Border)) g.FillPath(b, p);
            }
        }

        void PaintRow(Graphics g, int i, RowItem it, Rectangle c)
        {
            bool selected = sel.Contains(i);
            bool isHot = i == hot;
            bool tall = RowHeight >= 76;
            using (GraphicsPath p = Theme.Round(c, 12))
            {
                using (Brush b = new SolidBrush(selected ? Theme.CardHover : (isHot ? Theme.Mix(Theme.Card, Theme.CardHover, 0.5) : Theme.Card))) g.FillPath(b, p);
                using (Pen pen = new Pen(selected ? Theme.Accent : Theme.Border, selected ? 2f : 1f)) g.DrawPath(pen, p);
            }

            // right side first, so the text knows how much room it has
            int limit = c.Right - 18;
            if (it.HasToggle)
            {
                Rectangle tr = ToggleRect(i);
                PeachToggle.DrawSwitch(g, tr, it.On, i == hotToggle);
                limit = tr.X - 14;
            }
            List<Rectangle> acts = ActionRects(i);
            for (int k = 0; k < acts.Count; k++)
            {
                RowAction a = it.Actions[k];
                bool h = hotAction == i + ":" + a.Id;
                using (GraphicsPath p = Theme.Round(acts[k], 8))
                {
                    using (Brush b = new SolidBrush(h ? (a.Danger ? Theme.Danger : Theme.Accent) : Theme.Panel)) g.FillPath(b, p);
                    using (Pen pen = new Pen(a.Danger ? Theme.Mix(Theme.Card, Theme.Danger, 0.5) : Theme.Border)) g.DrawPath(pen, p);
                }
                using (Brush b = new SolidBrush(h ? (a.Danger ? Color.White : Theme.Bg) : (a.Danger ? Theme.Danger : Theme.Ink)))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(a.Label, Theme.Font(9.5f, true), b, acts[k], sf);
                limit = Math.Min(limit, acts[k].X - 14);
            }

            // icon
            int s = tall ? 46 : 38;
            Rectangle icon = new Rectangle(c.X + 16, tall ? c.Y + 14 : c.Y + (c.Height - s) / 2, s, s);
            if (it.Icon == null && !it.IconTried && IconProvider != null) { it.IconTried = true; it.Icon = IconProvider(it); }
            if (it.Icon != null) g.DrawImage(it.Icon, icon);
            else
            {
                using (Brush b = new SolidBrush(it.Enabled ? it.Tint : Theme.Muted)) g.FillEllipse(b, icon);
                string letter = it.Letter.Length > 0 ? it.Letter : (it.Title.Length > 0 ? it.Title.Substring(0, 1).ToUpperInvariant() : "?");
                using (Brush b = new SolidBrush(Theme.Bg))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(letter, Theme.Font(tall ? 15f : 13f, true), b, icon, sf);
            }

            int tx = icon.Right + 16;
            int ty = tall ? c.Y + 12 : (it.Subtitle.Length > 0 ? c.Y + c.Height / 2 - 21 : c.Y + (c.Height - 22) / 2);
            Rectangle titleBox = new Rectangle(tx, ty, Math.Max(10, limit - tx), 22);
            Brush ink = new SolidBrush(it.Enabled ? Theme.Ink : Theme.Muted);
            using (StringFormat nf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.DrawString(it.Title, Theme.Font(11f, true), ink, titleBox, nf);
                if (it.Status.Length > 0)
                {
                    int tw = TextRenderer.MeasureText(it.Title, Theme.Font(11f, true)).Width;
                    int sx = tx + tw + 6;
                    if (sx + 40 < limit)
                        using (Brush sb = new SolidBrush(it.StatusColor)) g.DrawString("·  " + it.Status, Theme.Font(9.5f, true), sb, new Rectangle(sx, ty + 2, limit - sx, 20), nf);
                }
                using (Brush mb = new SolidBrush(Theme.Muted))
                    g.DrawString(it.Subtitle, Theme.Font(9.5f, false), mb, new Rectangle(tx, ty + 22, Math.Max(10, limit - tx), 20), nf);
            }
            ink.Dispose();

            if (tall && it.Chips.Count > 0)
            {
                int cx = tx, cy = c.Bottom - 32;
                foreach (string chip in it.Chips)
                {
                    int w = TextRenderer.MeasureText(chip, Theme.Font(8.5f, true)).Width + 16;
                    if (cx + w > limit) break;
                    Rectangle cr = new Rectangle(cx, cy, w, 22);
                    using (GraphicsPath p = Theme.Round(cr, 11)) using (Brush b = new SolidBrush(Theme.Mix(Theme.Card, Theme.Ink, 0.1))) g.FillPath(b, p);
                    using (Brush b = new SolidBrush(Theme.Muted))
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(chip, Theme.Font(8.5f, true), b, cr, sf);
                    cx += w + 6;
                }
            }
        }
    }

    internal static class RectExt
    {
        public static Rectangle Inflate2(this Rectangle r, int by) { r.Inflate(by, by); return r; }
    }
}
