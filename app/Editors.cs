using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Peach
{
    // Base of the list editors (apps, hotkeys, Fast Type): a Peach list plus Add / Edit / Delete.
    internal abstract class ListEditorForm : PeachForm
    {
        protected readonly PeachList List = new PeachList();
        protected readonly Config Cfg;
        protected readonly Label HintLabel;
        readonly Action changed;
        int nextX = 24;
        readonly int bottomY;

        protected ListEditorForm(Config cfg, Action changed, string title, string hint, int rowHeight)
            : base(title, 780, 640)
        {
            Cfg = cfg; this.changed = changed;
            HintLabel = AddLabel(hint, 28, TitleH + 18, 724, 46, true, 10f, false);
            List.RowHeight = rowHeight;
            List.Location = new Point(24, TitleH + 76);
            List.Size = new Size(732, 640 - TitleH - 76 - 84);
            List.ItemActivated += delegate { Edit(); };
            Controls.Add(List);

            bottomY = 640 - 64;
            AddButton("Hinzufügen", BtnStyle.Primary, 136, delegate { Add(); });
            AddButton("Bearbeiten", BtnStyle.Secondary, 124, delegate { Edit(); });
            AddButton("Löschen", BtnStyle.Danger, 110, delegate { Delete(); });
            PeachButton close = new PeachButton("Schließen", BtnStyle.Secondary, 124, 40);
            close.Location = new Point(780 - 24 - 124, bottomY);
            close.Click += delegate { Close(); };
            Controls.Add(close);
        }

        protected PeachButton AddButton(string text, BtnStyle style, int w, EventHandler click)
        {
            PeachButton b = new PeachButton(text, style, w, 40);
            b.Location = new Point(nextX, bottomY);
            b.Click += click;
            Controls.Add(b);
            nextX += w + 10;
            return b;
        }

        // A wide action in the header row (right of the hint text).
        protected PeachButton AddHeaderButton(string text, int w, EventHandler click)
        {
            PeachButton b = new PeachButton(text, BtnStyle.Secondary, w, 36);
            b.Location = new Point(780 - 24 - w, TitleH + 16);
            b.Click += click;
            Controls.Add(b);
            HintLabel.Width = 724 - w - 12;
            return b;
        }

        protected abstract void Reload();
        protected abstract bool AddItem();
        protected abstract bool EditItem(int index);
        protected abstract void DeleteItem(int index);

        protected void Init() { Reload(); }

        protected void Done(int select)
        {
            Cfg.Save(); changed(); Reload();
            if (select >= 0 && select < List.Rows.Count) List.SelectRow(select);
        }

        void Add() { if (AddItem()) Done(List.Rows.Count); }
        void Edit() { int i = List.SelectedIndex; if (i >= 0 && EditItem(i)) Done(i); }
        void Delete()
        {
            int i = List.SelectedIndex;
            if (i < 0) return;
            DeleteItem(i);
            Done(Math.Min(i, List.Rows.Count - 2));
        }

        // ---- helpers for the small edit dialogs ----
        protected static PeachForm Dialog(string title, int w, int h)
        {
            PeachForm f = new PeachForm(title, w, h);
            f.ShowInTaskbar = false;
            return f;
        }

        protected static void FieldLabel(PeachForm f, string text, int x, int y) { f.AddLabel(text, x, y, 540, 22, true, 9.5f, true); }

        protected static void SaveCancel(PeachForm f, EventHandler onSave, out PeachButton save)
        {
            int y = f.ClientSize.Height - 62;
            save = new PeachButton("Speichern", BtnStyle.Primary, 130, 40);
            save.Location = new Point(f.ClientSize.Width - 28 - 130, y);
            save.Click += onSave;
            PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40);
            cancel.Location = new Point(f.ClientSize.Width - 28 - 130 - 10 - 120, y);
            cancel.Result = DialogResult.Cancel;
            f.Controls.Add(save); f.Controls.Add(cancel);
        }
    }

    internal class HotkeyEditor : ListEditorForm
    {
        public HotkeyEditor(Config cfg, Action changed)
            : base(cfg, changed, "Hotkeys",
                   "Eigene Tastenkürzel: starten ein Programm, öffnen eine Datei oder URL oder tippen einen Text. Strg + Alt + P bleibt fest für das Peach-Menü reserviert.", 62)
        { List.EmptyText = "Noch keine Hotkeys.\nMit „Hinzufügen“ legst du das erste an."; Init(); }

        protected override void Reload()
        {
            List.Rows.Clear();
            foreach (HotkeyEntry h in Cfg.Hotkeys)
            {
                RowItem r = new RowItem();
                r.Title = PeachHotkeyBox.Pretty(h.Combo);
                r.Letter = "⌨";
                r.Subtitle = (h.Action == "type" ? "Tippt: " : "Öffnet: ") + (h.Target ?? "").Replace("\n", " ↵ ");
                List.Rows.Add(r);
            }
            List.RowsChanged();
        }

        protected override bool AddItem() { return Edit(null); }
        protected override bool EditItem(int i) { return Edit(Cfg.Hotkeys[i]); }
        protected override void DeleteItem(int i) { Cfg.Hotkeys.RemoveAt(i); }

        bool Edit(HotkeyEntry existing)
        {
            using (PeachForm f = Dialog(existing == null ? "Hotkey hinzufügen" : "Hotkey bearbeiten", 600, 500))
            {
                FieldLabel(f, "Tastenkombination", 30, PeachForm.TitleH + 22);
                PeachHotkeyBox hk = new PeachHotkeyBox(existing == null ? "" : existing.Combo, 540); hk.Location = new Point(30, PeachForm.TitleH + 48);
                f.Controls.Add(hk);

                FieldLabel(f, "Aktion", 30, PeachForm.TitleH + 106);
                PeachSegmented act = new PeachSegmented(new string[] { "Programm / Datei / URL öffnen", "Text tippen" }, existing != null && existing.Action == "type" ? 1 : 0, 540);
                act.Location = new Point(30, PeachForm.TitleH + 132);
                f.Controls.Add(act);

                FieldLabel(f, "Ziel bzw. Text  ({date} und {time} werden ersetzt)", 30, PeachForm.TitleH + 188);
                PeachTextBox target = new PeachTextBox(540, 130, true); target.Location = new Point(30, PeachForm.TitleH + 214);
                if (existing != null) target.Text = (existing.Target ?? "").Replace("\n", "\r\n");
                f.Controls.Add(target);

                PeachButton save;
                SaveCancel(f, delegate
                {
                    string norm = Peach.Combo.Normalize(hk.Combo);
                    string err = null;
                    if (norm == null) err = "Bitte eine Tastenkombination mit Strg, Alt oder Umschalt drücken.";
                    else if (norm == HotkeyManager.MenuCombo) err = "Strg + Alt + P ist für das Peach-Menü reserviert.";
                    else if (target.Text.Trim().Length == 0) err = "Ziel bzw. Text darf nicht leer sein.";
                    else foreach (HotkeyEntry h in Cfg.Hotkeys)
                        if (h != existing && Peach.Combo.Normalize(h.Combo) == norm) err = "Diese Kombination ist schon vergeben.";
                    if (err != null) { Dialogs.Info(f, "Hotkey", err); return; }
                    f.DialogResult = DialogResult.OK;
                }, out save);

                if (f.ShowDialog(this) != DialogResult.OK) return false;
                HotkeyEntry e = existing ?? new HotkeyEntry();
                e.Combo = Peach.Combo.Normalize(hk.Combo);
                e.Action = act.SelectedIndex == 1 ? "type" : "open";
                e.Target = target.Text.Replace("\r\n", "\n");
                if (existing == null) Cfg.Hotkeys.Add(e);
                return true;
            }
        }
    }

    internal class SnippetEditor : ListEditorForm
    {
        public SnippetEditor(Config cfg, Action changed)
            : base(cfg, changed, "Fast Type",
                   "Abkürzung tippen → wird sofort durch den Text ersetzt. Tipp: mit ; beginnen (z. B. ;mfg), dann löst es nie aus Versehen aus. {date} und {time} werden eingesetzt.", 62)
        { List.EmptyText = "Noch keine Abkürzungen."; Init(); }

        protected override void Reload()
        {
            List.Rows.Clear();
            foreach (Snippet s in Cfg.Snippets)
            {
                RowItem r = new RowItem();
                r.Title = s.Abbr; r.Letter = "→";
                r.Subtitle = (s.Text ?? "").Replace("\n", " ↵ ");
                List.Rows.Add(r);
            }
            List.RowsChanged();
        }

        protected override bool AddItem() { return Edit(null); }
        protected override bool EditItem(int i) { return Edit(Cfg.Snippets[i]); }
        protected override void DeleteItem(int i) { Cfg.Snippets.RemoveAt(i); }

        bool Edit(Snippet existing)
        {
            using (PeachForm f = Dialog(existing == null ? "Abkürzung hinzufügen" : "Abkürzung bearbeiten", 600, 460))
            {
                FieldLabel(f, "Abkürzung (mindestens 2 Zeichen, keine Leerzeichen)", 30, PeachForm.TitleH + 22);
                PeachTextBox abbr = new PeachTextBox(540, 40, false); abbr.Location = new Point(30, PeachForm.TitleH + 48);
                abbr.Placeholder = ";mfg";
                if (existing != null) abbr.Text = existing.Abbr;
                f.Controls.Add(abbr);

                FieldLabel(f, "Ersetzt durch", 30, PeachForm.TitleH + 106);
                PeachTextBox text = new PeachTextBox(540, 140, true); text.Location = new Point(30, PeachForm.TitleH + 132);
                if (existing != null) text.Text = (existing.Text ?? "").Replace("\n", "\r\n");
                f.Controls.Add(text);

                PeachButton save;
                SaveCancel(f, delegate
                {
                    string a = abbr.Text, err = null;
                    if (a.Length < 2) err = "Die Abkürzung braucht mindestens 2 Zeichen.";
                    else if (a.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' }) >= 0) err = "Die Abkürzung darf keine Leerzeichen enthalten.";
                    else if (text.Text.Length == 0) err = "Der Ersetzungstext darf nicht leer sein.";
                    else foreach (Snippet s in Cfg.Snippets)
                    {
                        if (s == existing) continue;
                        if (string.Equals(s.Abbr, a, StringComparison.OrdinalIgnoreCase)) err = "Diese Abkürzung gibt es schon.";
                        else if (s.Abbr.StartsWith(a, StringComparison.OrdinalIgnoreCase) || a.StartsWith(s.Abbr, StringComparison.OrdinalIgnoreCase))
                            err = "Konflikt mit „" + s.Abbr + "“: Eine Abkürzung darf nicht am Anfang einer anderen stehen, sonst löst die kürzere zuerst aus.";
                    }
                    if (err != null) { Dialogs.Info(f, "Fast Type", err); return; }
                    f.DialogResult = DialogResult.OK;
                }, out save);

                if (f.ShowDialog(this) != DialogResult.OK) return false;
                Snippet r = existing ?? new Snippet();
                r.Abbr = abbr.Text;
                r.Text = text.Text.Replace("\r\n", "\n");
                if (existing == null) Cfg.Snippets.Add(r);
                return true;
            }
        }
    }
}
