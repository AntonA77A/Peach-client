using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Peach
{
    // `peach.exe --config c.json --render-ui <name> <out.png>`: renders a window to a PNG without showing it.
    // Names: addons, bar-editor, hotkeys, snippets, apps, picker, controller, dialog
    internal static class UiPreview
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        static BuiltinAddon B(string name, string letter, Color tint, string desc, bool on, bool toggle, bool customize)
        {
            BuiltinAddon b = new BuiltinAddon { Name = name, Letter = letter, Tint = tint, Description = desc, IsOn = delegate { return on; }, Chips = new string[] { "Controller", "Beispiel" } };
            if (toggle) b.SetOn = delegate (bool v) { };
            if (customize) b.Customize = delegate { };
            return b;
        }

        public static int Render(string name, string file, Config cfg)
        {
            Form f;
            Action<Form> afterShow = null;
            string tempRoot = null;
            switch (name)
            {
                case "editor":
                    {
                        tempRoot = Path.Combine(Path.GetTempPath(), "peach-preview-" + Guid.NewGuid().ToString("N"));
                        AddonHost host = new AddonHost(tempRoot);
                        string err;
                        string id = AddonFiles.Scaffold(host.ModsDir, "Fenster-Helfer", "Zeigt beim Fensterwechsel eine Benachrichtigung", "js", new List<string>(), out err);
                        File.WriteAllText(Path.Combine(host.ModsDir, id, "main.js"),
                            "const peach = require('peach');\nconst os = require('os');\n\n// Meldet jeden Fensterwechsel\nasync function main() {\n  await peach.log('Start auf', os.hostname(), 'mit', 42, 'Fenstern');\n  await peach.on('focus', ev => peach.notify('Fenster', ev.title));\n  await peach.hotkey('ctrl+alt+j', () => peach.clipboard.get());\n  const name = 'Peach';\n  peach.no");
                        string[] langs = { "js", "py", "ps1" };
                        foreach (string l in langs)
                        {
                            System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
                            ApiCatalog.Instance.EnsureLoaded(l, delegate { done.Set(); });
                            done.WaitOne(90000);
                        }
                        f = new AddonEditorForm(cfg, host, id, "Fenster-Helfer");
                        afterShow = delegate (Form form)
                        {
                            AddonEditorForm ed = (AddonEditorForm)form;
                            ed.PreviewSwitch(0);
                            ed.Code.CaretToEnd();
                            ed.PreviewSearch("peach");
                            ed.PreviewSelectApi(1);
                            ed.Code.TryComplete(true);
                        };
                        break;
                    }
                case "addons":
                case "addons-script":
                    {
                        AddonHost host = new AddonHost(AppDomain.CurrentDomain.BaseDirectory);
                        host.Connected = true; host.HostVersion = "0.1.0";
                        List<AddonInfo> a = new List<AddonInfo>();
                        a.Add(new AddonInfo { Id = "start-counter", Name = "Start-Zähler", Version = "0.2.0", Description = "Zählt, wie oft Peach gestartet wurde, und zeigt es als Benachrichtigung.", Language = "Python", Entry = "main.py", Status = "running", Enabled = true, Permissions = new List<string> { "notify" } });
                        a.Add(new AddonInfo { Id = "fenster-helfer", Name = "Fenster-Helfer", Version = "1.0.0", Description = "Hotkeys zum Anordnen von Fenstern.", Language = "JavaScript", Entry = "main.js", Status = "crashed", Permissions = new List<string> { "hotkeys", "windows" } });
                        a.Add(new AddonInfo { Id = "clip-tools", Name = "Clip-Tools", Version = "0.1.0", Author = "Du", Language = "PowerShell", Entry = "main.ps1", Status = "stopped", Permissions = new List<string> { "clipboard", "notify", "shell" } });
                        host.Addons = a;
                        List<BuiltinAddon> b = new List<BuiltinAddon>();
                        b.Add(B("Controller-Modus", "C", Color.FromArgb(255, 122, 89), "Vollbild-Oberfläche mit deinen Apps. Geht nur mit angeschlossenem Controller, die Home-Taste holt sie nach vorn.", false, true, true));
                        b.Add(B("Controller → Maus", "M", Color.FromArgb(240, 160, 90), "Zeiger, Klicks und Scrollen mit dem Controller.", true, true, true));
                        b.Add(B("Fast Type", "F", Color.FromArgb(120, 150, 200), "Abkürzung tippen, sofort wird der volle Text eingesetzt (z. B. ;mfg).", true, true, true));
                        b.Add(B("Hotkeys", "H", Color.FromArgb(170, 130, 190), "Eigene Tastenkürzel: Programme starten, URLs öffnen oder Text tippen.", true, false, true));
                        b.Add(B("Peach-Leiste", "L", Color.FromArgb(214, 96, 110), "Deine eigene Leiste am Bildschirmrand mit Apps, Fenstern und Uhr.", true, true, true));
                        AddonsForm af = new AddonsForm(cfg, host, b);
                        af.PreviewMode = true;
                        if (name == "addons-script") af.SetFilter(2);
                        f = af;
                        break;
                    }
                case "minecraft":
                case "minecraft-mods":
                case "minecraft-widgets":
                    {
                        string root = Path.Combine(Path.GetTempPath(), "peach-preview-mc-" + Guid.NewGuid().ToString("N"));
                        tempRoot = root;
                        McProfile pa = new McProfile { Id = "survival", Name = "Survival 1.21.1", McVersion = "1.21.1", Loader = "fabric" };
                        McProfile pb = new McProfile { Id = "vanilla", Name = "Vanilla Server", McVersion = "1.20.4", Loader = "vanilla" };
                        cfg.Minecraft.Profiles.Clear(); cfg.Minecraft.Profiles.Add(pa); cfg.Minecraft.Profiles.Add(pb); cfg.Minecraft.Selected = "survival";
                        string md = Minecraft.ModsDir(cfg, pa); Directory.CreateDirectory(md);
                        foreach (string n in new string[] { "sodium-fabric-0.6.0+mc1.21.1.jar", "lithium-fabric-0.13.0+mc1.21.1.jar", "fabric-api-0.102.0+1.21.1.jar", "iris-fabric-1.8.0+mc1.21.1.jar.disabled" }) File.WriteAllText(Path.Combine(md, n), "x");
                        cfg.Minecraft.OverlayEnabled = true;
                        MinecraftForm mf = new MinecraftForm(cfg, delegate { });
                        if (name == "minecraft-mods") mf.ShowTab(1);
                        if (name == "minecraft-widgets") mf.ShowTab(2);
                        f = mf;
                        if (name == "minecraft-widgets")
                        {
                            using (WidgetOverlay wo = new WidgetOverlay(cfg))
                                wo.RenderPreview(Path.ChangeExtension(file, null) + "-overlay.png", OverlayModel.Build(cfg.Minecraft.Widgets, new SystemStats { Cpu = 38, Gpu = 71, RamPct = 62, RamUsedMb = 10200, RamTotalMb = 16384 }, new DateTime(2026, 1, 1, 21, 47, 0), "Daft Punk - Get Lucky", TimeSpan.FromSeconds(4980)));
                        }
                        break;
                    }
                case "gameboost":
                case "gameboost-games":
                    {
                        cfg.GameBoost.Games.Clear(); cfg.GameBoost.Games.Add("eldenring.exe"); cfg.GameBoost.Games.Add("cs2.exe");
                        cfg.GameBoost.CloseApps.Add("chrome.exe");
                        GameBooster gb = new GameBooster(cfg);
                        GameBoostForm gf = new GameBoostForm(cfg, gb);
                        Random rnd = new Random(7);
                        for (int i = 0; i < 60; i++)
                            gf.Feed(new SystemStats { Cpu = 25 + 40 * Math.Abs(Math.Sin(i / 7.0)) + rnd.Next(8), Gpu = 55 + 35 * Math.Abs(Math.Sin(i / 11.0)) + rnd.Next(6), RamPct = 58 + i / 12.0, RamUsedMb = 18600, RamTotalMb = 32000 });
                        if (name == "gameboost-games") gf.ShowTab(1);
                        f = gf;
                        break;
                    }
                case "discord":
                    {
#if WEBVIEW2
                        tempRoot = Path.Combine(Path.GetTempPath(), "peach-preview-dc-" + Guid.NewGuid().ToString("N"));
                        f = new DiscordForm(tempRoot, "https://discord.com/login", cfg);
                        afterShow = delegate (Form form)
                        {
                            DiscordForm d = (DiscordForm)form;
                            DiscordForm.Pump(delegate { return d.WebReady; }, 40000);
                            DiscordForm.Pump(delegate { return d.PageLoaded; }, 40000);
                            DiscordForm.Pump(delegate { return false; }, 9000);   // Discord is a big single-page app: give it time to draw
                            try
                            {
                                var t = d.Web.CoreWebView2.ExecuteScriptAsync("document.title + ' | ' + location.href + ' | ' + document.body.innerText.length");
                                DiscordForm.Pump(delegate { return t.IsCompleted; }, 5000);
                                File.WriteAllText(file + ".txt", t.IsCompleted ? t.Result : "(no answer)");
                            }
                            catch (Exception ex) { File.WriteAllText(file + ".txt", "error: " + ex.Message); }
                        };
                        break;
#else
                        return 3;
#endif
                    }
                case "discord-probe2":
                    {
#if WEBVIEW2
                        tempRoot = Path.Combine(Path.GetTempPath(), "peach-probe2-dc-" + Guid.NewGuid().ToString("N"));
                        f = new DiscordForm(tempRoot, "https://discord.com/login", cfg);
                        afterShow = delegate (Form form)
                        {
                            DiscordForm d = (DiscordForm)form;
                            DiscordForm.Pump(delegate { return d.WebReady; }, 40000);
                            DiscordForm.Pump(delegate { return d.PageLoaded; }, 40000);
                            DiscordForm.Pump(delegate { return false; }, 8000);
                            string js = @"(function(){
var names={};for(var i=0;i<document.styleSheets.length;i++){var rules;try{rules=document.styleSheets[i].cssRules}catch(e){continue}for(var j=0;j<rules.length;j++){var r=rules[j];if(r.style){for(var k=0;k<r.style.length;k++){var p=r.style[k];if(p.indexOf('--')===0)names[p]=1}}}}
var keys=Object.keys(names);var probe=document.createElement('div');probe.style.display='none';document.body.appendChild(probe);
function resolve(n){probe.style.color='';probe.style.color='var('+n+')';return getComputedStyle(probe).color}
var res={};keys.forEach(function(n){res[n]=resolve(n)});
function rgbOf(el){var c=getComputedStyle(el).backgroundColor;return c}
var btn=null;document.querySelectorAll('button').forEach(function(b){if(/Anmelden/.test(b.innerText)&&!btn)btn=b});
var btnColor=btn?getComputedStyle(btn).backgroundColor:'';
var link=null;document.querySelectorAll('a,button').forEach(function(b){if(/Passwort vergessen/.test(b.innerText)&&!link)link=b});
var linkColor=link?getComputedStyle(link).color:'';
var card=null;var h=null;document.querySelectorAll('h1,h2,div').forEach(function(e){if(!h&&e.children.length===0&&/Willkommen zur/.test(e.textContent))h=e});
var cardColor='';var e2=h;while(e2&&e2!==document.body){var bc=getComputedStyle(e2).backgroundColor;if(bc&&bc!=='rgba(0, 0, 0, 0)'&&bc!=='transparent'){cardColor=bc;break}e2=e2.parentElement}
var inputEl=document.querySelector('input');var inputColor=inputEl?getComputedStyle(inputEl).backgroundColor:'';
function named(color){return keys.filter(function(n){return res[n]===color}).sort()}
return JSON.stringify({button:btnColor,link:linkColor,card:cardColor,input:inputColor,
 namesButton:named(btnColor),namesLink:named(linkColor),namesCard:named(cardColor),namesInput:named(inputColor),
 brand:named('rgb(88, 101, 242)')})})()";
                            var t = d.Web.CoreWebView2.ExecuteScriptAsync(js);
                            DiscordForm.Pump(delegate { return t.IsCompleted; }, 20000);
                            File.WriteAllText(file + ".txt", t.IsCompleted ? t.Result : "(no answer)");
                        };
                        break;
#else
                        return 3;
#endif
                    }
                case "discord-probe":
                    {
#if WEBVIEW2
                        tempRoot = Path.Combine(Path.GetTempPath(), "peach-probe-dc-" + Guid.NewGuid().ToString("N"));
                        f = new DiscordForm(tempRoot, "https://discord.com/login", cfg);
                        afterShow = delegate (Form form)
                        {
                            DiscordForm d = (DiscordForm)form;
                            DiscordForm.Pump(delegate { return d.WebReady; }, 40000);
                            DiscordForm.Pump(delegate { return d.PageLoaded; }, 40000);
                            DiscordForm.Pump(delegate { return false; }, 8000);
                            string js = "(function(){var names={};for(var i=0;i<document.styleSheets.length;i++){var rules;try{rules=document.styleSheets[i].cssRules}catch(e){continue}for(var j=0;j<rules.length;j++){var r=rules[j];if(r.style){for(var k=0;k<r.style.length;k++){var p=r.style[k];if(p.indexOf('--')===0){names[p]=(r.selectorText||'').slice(0,60)}}}}}var cs=getComputedStyle(document.documentElement),bs=getComputedStyle(document.body);var keys=Object.keys(names).sort();var ed=document.querySelectorAll('[role=textbox],[contenteditable=true]').length;return JSON.stringify({html:document.documentElement.className,body:document.body.className,count:keys.length,textboxes:ed,vars:keys.map(function(n){return n+' = '+(bs.getPropertyValue(n)||cs.getPropertyValue(n)).trim().slice(0,40)+'   ['+names[n]+']'})})})()";
                            var t = d.Web.CoreWebView2.ExecuteScriptAsync(js);
                            DiscordForm.Pump(delegate { return t.IsCompleted; }, 15000);
                            File.WriteAllText(file + ".txt", t.IsCompleted ? t.Result : "(no answer)");
                        };
                        break;
#else
                        return 3;
#endif
                    }
                case "discord-settings": f = new DiscordSettingsForm(cfg, delegate (bool s) { }); break;
                case "discord-settings-commands": { DiscordSettingsForm sf = new DiscordSettingsForm(cfg, delegate (bool s) { }); sf.ShowTab(1); f = sf; break; }
                case "discord-settings-abbr": { DiscordSettingsForm sf = new DiscordSettingsForm(cfg, delegate (bool s) { }); sf.ShowTab(2); f = sf; break; }
                case "discord-settings-css": { DiscordSettingsForm sf = new DiscordSettingsForm(cfg, delegate (bool s) { }); sf.ShowTab(3); f = sf; break; }
                case "bar-editor": f = new BarEditor(cfg, delegate { }); break;
                case "hotkeys": f = new HotkeyEditor(cfg, delegate { }); break;
                case "snippets": f = new SnippetEditor(cfg, delegate { }); break;
                case "apps": f = new AppEditor(cfg, delegate { }); break;
                case "picker":
                    {
                        AppPicker p = new AppPicker();
                        p.SetApps(StartApps.Load());
                        f = p;
                        break;
                    }
                case "controller": f = new ControllerSettingsForm(cfg, delegate { return true; }, delegate (int v) { }, delegate { }, delegate { }); break;
                case "dialog": f = DialogPreview(); break;
                default: return 2;
            }
            // Child controls only paint into DrawToBitmap once the window has been shown: do that far off screen.
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-6000, -6000);
            f.ShowInTaskbar = false;
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            Application.DoEvents();
            if (afterShow != null) { afterShow(f); System.Threading.Thread.Sleep(300); Application.DoEvents(); }
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                // DrawToBitmap skips native RichEdit boxes: let those paint themselves (PrintWindow) and lay them over
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    OverlayNative(f, f, g);
#if WEBVIEW2
                    DiscordForm dfm = f as DiscordForm;
                    if (dfm != null)
                        using (Bitmap page = dfm.CapturePage(20000))
                            if (page != null) g.DrawImage(page, dfm.Web.Location.X, dfm.Web.Location.Y, dfm.Web.Width, dfm.Web.Height);
#endif
                }
                bmp.Save(file, ImageFormat.Png);
            }
            f.Dispose();
            if (tempRoot != null) { try { Directory.Delete(tempRoot, true); } catch { } }
            return 0;
        }

        static void OverlayNative(Control root, Control c, Graphics g)
        {
            foreach (Control child in c.Controls)
            {
                RichTextBox rtb = child as RichTextBox;
                if (rtb != null && rtb.IsHandleCreated)
                {
                    using (Bitmap part = new Bitmap(rtb.Width, rtb.Height))
                    {
                        using (Graphics pg = Graphics.FromImage(part)) { IntPtr hdc = pg.GetHdc(); try { PrintWindow(rtb.Handle, hdc, 2); } finally { pg.ReleaseHdc(hdc); } }
                        Point at = root.PointToClient(rtb.PointToScreen(Point.Empty));
                        g.DrawImage(part, at);
                    }
                }
                else OverlayNative(root, child, g);
            }
        }

        static Form DialogPreview()
        {
            PeachForm f = new PeachForm("Addon entfernen", 500, 250);
            f.AddLabel("„Start-Zähler“ wirklich entfernen?\n\nDer Ordner mods\\start-counter mit allen Dateien wird gelöscht. Das lässt sich nicht rückgängig machen.", 30, PeachForm.TitleH + 26, 440, 100, false, 10.5f, false);
            PeachButton ok = new PeachButton("Entfernen", BtnStyle.Danger, 130, 40); ok.Location = new Point(500 - 30 - 130 - 120 - 12, 250 - 62);
            PeachButton cancel = new PeachButton("Abbrechen", BtnStyle.Secondary, 120, 40); cancel.Location = new Point(500 - 30 - 120, 250 - 62);
            f.Controls.Add(ok); f.Controls.Add(cancel);
            return f;
        }
    }
}
