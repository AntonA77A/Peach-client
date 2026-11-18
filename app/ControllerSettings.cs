using System;
using System.Drawing;
using System.Windows.Forms;

namespace Peach
{
    // Controller settings + a cheat sheet of what every button does.
    internal class ControllerSettingsForm : PeachForm
    {
        readonly Func<bool> isConnected;
        readonly Label connection;
        readonly Timer timer = new Timer();

        public ControllerSettingsForm(Config cfg, Func<bool> isConnected, Action<int> speedChanged, Action padChanged, Action openApps)
            : base("Controller-Einstellungen", 700, 800)
        {
            this.isConnected = isConnected;
            connection = AddLabel("", 28, TitleH + 20, 640, 24, false, 11f, true);
            UpdateConnection();
            timer.Interval = 800; timer.Tick += delegate { UpdateConnection(); }; timer.Start();

            AddLabel("ZEIGER", 28, TitleH + 66, 200, 18, true, 8.5f, true);
            AddLabel("Geschwindigkeit des Zeigers (Pixel pro Sekunde bei vollem Ausschlag)", 28, TitleH + 90, 640, 20, false, 10f, false);
            PeachSlider speed = new PeachSlider(400, 3000, cfg.MouseSpeed, "", 644); speed.Location = new Point(28, TitleH + 116);
            speed.ValueChanged += delegate { cfg.MouseSpeed = speed.Value; cfg.Save(); speedChanged(speed.Value); };
            Controls.Add(speed);

            AddLabel("VERHALTEN", 28, TitleH + 172, 200, 18, true, 8.5f, true);
            Toggle("Controller steuert die Maus", 28, TitleH + 198, cfg.MouseEnabled, delegate (bool v) { cfg.MouseEnabled = v; cfg.Save(); padChanged(); });
            Toggle("Bildschirmtastatur automatisch bei Textfeldern", 28, TitleH + 240, cfg.AutoKeyboard, delegate (bool v) { cfg.AutoKeyboard = v; cfg.Save(); });
            Toggle("Game Bar bei der Xbox-Taste sperren", 28, TitleH + 282, !GameBar.ControllerButtonOpensGameBar(null), delegate (bool v) { GameBar.SetControllerButtonOpensGameBar(!v, null); });

            PeachButton apps = new PeachButton("Apps für den Controller-Modus…", BtnStyle.Secondary, 330, 42); apps.Location = new Point(28, TitleH + 336);
            apps.Click += delegate { openApps(); };
            Controls.Add(apps);

            AddLabel("BELEGUNG", 28, TitleH + 398, 200, 18, true, 8.5f, true);
            string[,] map = {
                { "Linker Stick / Steuerkreuz", "Zeiger bewegen · im Launcher: Kachel wählen" },
                { "A  /  B  /  X", "Linksklick  /  Rechtsklick  /  Mittelklick" },
                { "RT", "Linksklick (auch mit Tastatur oder Launcher)" },
                { "LB  /  RB", "Zeiger langsam  /  schnell" },
                { "Rechter Stick", "Scrollen" },
                { "Y", "Bildschirmtastatur ein/aus" },
                { "Back + Start", "Controller-Maus ein/aus" },
                { "Home (Xbox) oder Back + Y", "Controller-Modus / Launcher nach vorn" },
            };
            for (int i = 0; i < map.GetLength(0); i++)
            {
                AddLabel(map[i, 0], 28, TitleH + 424 + i * 28, 270, 22, false, 10f, true);
                AddLabel(map[i, 1], 300, TitleH + 424 + i * 28, 372, 22, true, 10f, false);
            }

            PeachButton done = new PeachButton("Fertig", BtnStyle.Primary, 130, 42); done.Location = new Point(700 - 24 - 130, 800 - 62);
            done.Click += delegate { Close(); };
            Controls.Add(done);
        }

        void Toggle(string text, int x, int y, bool value, Action<bool> set)
        {
            PeachToggle t = new PeachToggle(value, text); t.Location = new Point(x, y);
            t.CheckedChanged += delegate { set(t.Checked); };
            Controls.Add(t);
        }

        void UpdateConnection()
        {
            bool c = isConnected();
            connection.Text = c ? "●  Controller verbunden" : "○  Kein Controller verbunden";
            connection.ForeColor = c ? Theme.Ok : Theme.Muted;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
