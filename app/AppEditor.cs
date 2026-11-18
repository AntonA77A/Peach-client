using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Peach
{
    // Searchable list of everything installed (Start menu), multi-select.
    internal class AppPicker : PeachForm
    {
        readonly PeachList list = new PeachList();
        readonly PeachTextBox filter;
        readonly Label status;
        readonly PeachButton add;
        List<StartApp> all = new List<StartApp>();
        public readonly List<StartApp> Selected = new List<StartApp>();

        public AppPicker() : base("Installierte Apps", 680, 700)
        {
            filter = new PeachTextBox(632, 40, false);
            filter.Location = new Point(24, TitleH + 18); filter.Placeholder = "Suchen … (z. B. Discord)";
            filter.TextChanged += delegate { Fill(); };
            Controls.Add(filter);

            list.MultiSelect = true; list.RowHeight = 56;
            list.Location = new Point(24, TitleH + 70); list.Size = new Size(632, 700 - TitleH - 70 - 92);
            list.EmptyText = "Lade installierte Apps …";
            list.IconProvider = delegate (RowItem r) { StartApp a = r.Tag as StartApp; return a == null ? null : AppIcons.For(a.Path); };
            list.SelectionChanged += delegate { UpdateButtons(); };
            list.ItemActivated += delegate { Accept(); };
            Controls.Add(list);

            status = AddLabel("", 28, 700 - 76, 330, 24, true, 9.5f, false);
            add = new PeachButton("Hinzufügen", BtnStyle.Primary, 130, 40); add.Location = new Point(680 - 24 - 130, 700 - 66); add.Enabled = false;
            add.Click += delegate { Accept(); };
            PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(680 - 24 - 130 - 10 - 120, 700 - 66); cancel.Result = DialogResult.Cancel;
            Controls.Add(add); Controls.Add(cancel);

            IntPtr h = Handle;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartApp> apps = StartApps.Load();
                try { BeginInvoke(new Action(delegate { SetApps(apps); })); } catch { }
            });
        }

        public void SetApps(List<StartApp> apps)
        {
            List<StartApp> merged = new List<StartApp>();
            merged.Add(new StartApp { Name = "Discord (in Peach)", Id = "peach:discord", Direct = "peach:discord" });
            merged.Add(new StartApp { Name = "Peach Addons", Id = "peach:addons", Direct = "peach:addons" });
            merged.AddRange(apps);
            all = merged; list.EmptyText = "Keine Apps gefunden."; Fill();
            status.Text = (apps.Count + 2) + " Apps – mehrere mit Strg oder Umschalt wählen";
        }

        void Fill()
        {
            string q = filter.Text.Trim();
            list.Rows.Clear();
            foreach (StartApp a in all)
            {
                if (q.Length > 0 && a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                RowItem r = new RowItem(); r.Title = a.Name; r.Tag = a;
                list.Rows.Add(r);
            }
            list.RowsChanged();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            int n = list.SelectedIndices.Count;
            add.Enabled = n > 0;
            add.Text = n > 1 ? n + " hinzufügen" : "Hinzufügen";
        }

        void Accept()
        {
            foreach (int i in list.SelectedIndices) Selected.Add((StartApp)list.Rows[i].Tag);
            if (Selected.Count == 0) return;
            DialogResult = DialogResult.OK;
        }
    }

    // Manages an app list: the controller-mode launcher tiles, or the apps pinned to the Peach bar.
    internal class AppEditor : ListEditorForm
    {
        readonly Func<List<AppEntry>> getList;
        List<AppEntry> Apps { get { return getList(); } }

        public AppEditor(Config cfg, Action changed)
            : this(cfg, changed, delegate { return cfg.Apps; }, "Apps für den Controller-Modus",
                   "Diese Apps erscheinen als Kacheln im Controller-Modus. Programme, Verknüpfungen oder Ordner einfach ins Fenster ziehen.") { }

        public AppEditor(Config cfg, Action changed, Func<List<AppEntry>> getList, string title, string hint)
            : base(cfg, changed, title, hint, 66)
        {
            this.getList = getList;
            List.EmptyText = "Noch keine Apps.\nZiehe ein Programm hierher oder nutze „Installierte Apps durchsuchen“.";
            List.IconProvider = delegate (RowItem r) { return AppIcons.For(r.Tag as string); };
            AllowDrop = true;
            DragEnter += delegate (object s, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += delegate (object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                foreach (string f in files) Apps.Add(new AppEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f });
                Done(Apps.Count - 1);
            };
            List.AllowDrop = true;
            List.DragEnter += delegate (object s, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            List.DragDrop += delegate (object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                foreach (string f in files) Apps.Add(new AppEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f });
                Done(Apps.Count - 1);
            };

            AddButton("Hoch", BtnStyle.Secondary, 78, delegate { MoveSelected(-1); });
            AddButton("Runter", BtnStyle.Secondary, 88, delegate { MoveSelected(1); });
            AddHeaderButton("Installierte Apps durchsuchen…", 270, delegate { PickInstalled(); });
            Init();
        }

        void PickInstalled()
        {
            using (AppPicker picker = new AppPicker())
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                int added = 0;
                foreach (StartApp s in picker.Selected)
                {
                    bool exists = false;
                    foreach (AppEntry e in Apps) if (string.Equals(e.Path, s.Path, StringComparison.OrdinalIgnoreCase)) exists = true;
                    if (exists) continue;
                    Apps.Add(new AppEntry { Name = s.Name, Path = s.Path });
                    added++;
                }
                if (added > 0) Done(Apps.Count - 1);
            }
        }

        void MoveSelected(int delta)
        {
            int i = List.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= Apps.Count) return;
            AppEntry t = Apps[i]; Apps[i] = Apps[j]; Apps[j] = t;
            Done(j);
        }

        protected override void Reload()
        {
            List.Rows.Clear();
            foreach (AppEntry a in Apps)
            {
                RowItem r = new RowItem();
                r.Title = a.Name; r.Tag = a.Path;
                r.Subtitle = (a.Path ?? "") + (string.IsNullOrEmpty(a.Args) ? "" : "  " + a.Args);
                List.Rows.Add(r);
            }
            List.RowsChanged();
        }

        protected override bool AddItem() { return Edit(null); }
        protected override bool EditItem(int i) { return Edit(Apps[i]); }
        protected override void DeleteItem(int i) { Apps.RemoveAt(i); }

        bool Edit(AppEntry existing)
        {
            using (PeachForm f = Dialog(existing == null ? "App hinzufügen" : "App bearbeiten", 600, 470))
            {
                FieldLabel(f, "Name", 30, PeachForm.TitleH + 22);
                PeachTextBox name = new PeachTextBox(540, 40, false); name.Location = new Point(30, PeachForm.TitleH + 48);
                f.Controls.Add(name);

                FieldLabel(f, "Programm, Datei, Ordner oder URL", 30, PeachForm.TitleH + 106);
                PeachTextBox path = new PeachTextBox(430, 40, false); path.Location = new Point(30, PeachForm.TitleH + 132);
                f.Controls.Add(path);
                PeachButton browse = new PeachButton("Suchen…", BtnStyle.Secondary, 100, 40); browse.Location = new Point(470, PeachForm.TitleH + 132);
                browse.Click += delegate
                {
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.bat;*.cmd|Alle Dateien|*.*";
                        if (d.ShowDialog(f) != DialogResult.OK) return;
                        path.Text = d.FileName;
                        if (name.Text.Trim().Length == 0) name.Text = Path.GetFileNameWithoutExtension(d.FileName);
                    }
                };
                f.Controls.Add(browse);

                FieldLabel(f, "Argumente (optional)", 30, PeachForm.TitleH + 190);
                PeachTextBox args = new PeachTextBox(540, 40, false); args.Location = new Point(30, PeachForm.TitleH + 216);
                f.Controls.Add(args);

                if (existing != null) { name.Text = existing.Name; path.Text = existing.Path; args.Text = existing.Args ?? ""; }

                PeachButton save;
                SaveCancel(f, delegate
                {
                    string err = null;
                    if (name.Text.Trim().Length == 0) err = "Bitte einen Namen eingeben.";
                    else if (path.Text.Trim().Length == 0) err = "Bitte ein Programm, eine Datei oder URL angeben.";
                    if (err != null) { Dialogs.Info(f, "App", err); return; }
                    f.DialogResult = DialogResult.OK;
                }, out save);

                if (f.ShowDialog(this) != DialogResult.OK) return false;
                AppEntry a = existing ?? new AppEntry();
                a.Name = name.Text.Trim(); a.Path = path.Text.Trim(); a.Args = args.Text.Trim();
                if (existing == null) Apps.Add(a);
                return true;
            }
        }
    }
}
