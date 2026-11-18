using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Peach
{
    internal class ThemePreset
    {
        public string Key, Label, Lowest, Lower, Low, High, Higher, Highest, Text, Strong, Muted, Brand;
    }

    // Themes, accent, font and the CSS that goes into the Discord window. Pure (unit-tested).
    // The variable names come from Discord's own stylesheet (visual-refresh design tokens), read from the live page.
    internal static class DiscordTheme
    {
        public static readonly ThemePreset[] Presets = {
            new ThemePreset { Key = "peach", Label = "Peach", Lowest = "#1a1210", Lower = "#201714", Low = "#261c19", High = "#2d211e", Higher = "#342723", Highest = "#3d2e29", Text = "#f6e9e4", Strong = "#ffffff", Muted = "#a58f88", Brand = "#ff7a59" },
            new ThemePreset { Key = "mitternacht", Label = "Mitternacht", Lowest = "#0a0d14", Lower = "#0e121b", Low = "#121723", High = "#171d2b", Higher = "#1c2333", Highest = "#232b3f", Text = "#e6ebff", Strong = "#ffffff", Muted = "#8892b0", Brand = "#6aa3ff" },
            new ThemePreset { Key = "wald", Label = "Wald", Lowest = "#0b1410", Lower = "#0f1a15", Low = "#13211a", High = "#182a21", Higher = "#1d3328", Highest = "#243d30", Text = "#e3f1e8", Strong = "#ffffff", Muted = "#85a091", Brand = "#5fd08a" },
            new ThemePreset { Key = "amoled", Label = "AMOLED", Lowest = "#000000", Lower = "#000000", Low = "#050505", High = "#0b0b0b", Higher = "#121212", Highest = "#1a1a1a", Text = "#f2f2f2", Strong = "#ffffff", Muted = "#8a8a8a", Brand = "#7c8cff" },
            new ThemePreset { Key = "violett", Label = "Violett", Lowest = "#0f0a18", Lower = "#140e20", Low = "#19122a", High = "#201736", Higher = "#281d42", Highest = "#31244f", Text = "#efe8ff", Strong = "#ffffff", Muted = "#9a8bb8", Brand = "#b68cff" },
        };

        public static ThemePreset Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (ThemePreset p in Presets) if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public static bool IsHex(string s) { return s != null && Regex.IsMatch(s.Trim(), "^#?[0-9a-fA-F]{6}$"); }
        public static string NormHex(string s) { return "#" + s.Trim().TrimStart('#').ToLowerInvariant(); }

        static Color Parse(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb(Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        // lighter (delta > 0) or darker (delta < 0) version of a colour, in HSL
        public static string Shade(string hex, double delta)
        {
            Color c = Parse(hex);
            double h = c.GetHue(), s = c.GetSaturation(), l = Math.Max(0, Math.Min(1, c.GetBrightness() + delta));
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            Func<double, double> channel = delegate (double t)
            {
                t = ((t % 360) + 360) % 360;
                if (t < 60) return p + (q - p) * t / 60;
                if (t < 180) return q;
                if (t < 240) return p + (q - p) * (240 - t) / 60;
                return p;
            };
            return string.Format("#{0:x2}{1:x2}{2:x2}", (int)Math.Round(channel(h + 120) * 255), (int)Math.Round(channel(h) * 255), (int)Math.Round(channel(h - 120) * 255));
        }

        static void V(StringBuilder sb, string name, string value) { sb.Append("  ").Append(name).Append(": ").Append(value).Append(" !important;\n"); }

        static string SafeFont(string f)
        {
            if (string.IsNullOrWhiteSpace(f)) return null;
            string clean = Regex.Replace(f.Trim(), "[^A-Za-z0-9 _-]", "");
            return clean.Length == 0 ? null : clean;
        }

        public static string BuildCss(DiscordConfig d)
        {
            StringBuilder sb = new StringBuilder();
            ThemePreset t = Find(d.Theme);
            string accent = IsHex(d.Accent) ? NormHex(d.Accent) : (t != null ? t.Brand : null);
            string font = SafeFont(d.Font);

            if (t != null || accent != null || font != null)
            {
                sb.Append("[class*='theme-'] {\n");
                if (t != null)
                {
                    V(sb, "--background-base-lowest", t.Lowest); V(sb, "--background-base-lower", t.Lower); V(sb, "--background-base-low", t.Low);
                    V(sb, "--background-surface-high", t.High); V(sb, "--background-surface-higher", t.Higher); V(sb, "--background-surface-highest", t.Highest);
                    V(sb, "--card-background-default", t.High); V(sb, "--embed-background", t.High); V(sb, "--embed-background-alternate", t.Higher);
                    V(sb, "--modal-background", t.High); V(sb, "--modal-footer-background", t.Higher);
                    V(sb, "--chat-background-default", t.Low); V(sb, "--chat-background", t.Low);
                    V(sb, "--channel-background-default", t.Lower); V(sb, "--background-secondary-alt", t.Lower); V(sb, "--channeltextarea-background", t.Higher);
                    V(sb, "--text-default", t.Text); V(sb, "--text-strong", t.Strong); V(sb, "--text-muted", t.Muted);
                    V(sb, "--input-text-default", t.Text); V(sb, "--interactive-text-default", t.Muted); V(sb, "--channels-default", t.Muted);
                    V(sb, "--scrollbar-thin-thumb", t.Highest); V(sb, "--scrollbar-auto-thumb", t.Highest);
                }
                if (accent != null)
                {
                    V(sb, "--background-brand", accent); V(sb, "--brand-500", accent);
                    V(sb, "--brand-360", Shade(accent, 0.14)); V(sb, "--brand-400", Shade(accent, 0.07)); V(sb, "--brand-560", Shade(accent, -0.07)); V(sb, "--brand-600", Shade(accent, -0.14));
                    V(sb, "--interactive-accent-background-default", accent); V(sb, "--interactive-accent-background-hover", Shade(accent, -0.06));
                    V(sb, "--interactive-accent-background-active", Shade(accent, -0.1)); V(sb, "--interactive-accent-background-selected", accent);
                    V(sb, "--input-border-active", accent); V(sb, "--border-focus", accent); V(sb, "--text-brand", Shade(accent, 0.16)); V(sb, "--icon-brand", Shade(accent, 0.16)); V(sb, "--tabs-indicator-default", Shade(accent, 0.16));
                    V(sb, "--control-primary-background-default", accent); V(sb, "--control-primary-background-hover", Shade(accent, -0.06)); V(sb, "--control-primary-background-active", Shade(accent, -0.1));
                    V(sb, "--badge-primary-background", accent); V(sb, "--badge-primary-border", accent); V(sb, "--button-outline-brand-background-hover", accent);
                    V(sb, "--checkbox-background-selected-default", accent); V(sb, "--radio-background-selected-default", accent); V(sb, "--radio-foreground-active", accent);
                    V(sb, "--switch-background-selected-default", accent); V(sb, "--standard-tab-gradient-center", accent);
                }
                if (font != null) { V(sb, "--font-primary", "'" + font + "', 'gg sans', 'Noto Sans', sans-serif"); V(sb, "--font-headline", "'" + font + "', 'gg sans', 'Noto Sans', sans-serif"); }
                sb.Append("}\n");
            }
            if (d.ReduceMotion)
                sb.Append("*, *::before, *::after { animation-duration: 0.01ms !important; animation-iteration-count: 1 !important; transition-duration: 0.01ms !important; scroll-behavior: auto !important; }\n");
            if (!string.IsNullOrEmpty(d.CustomCss) && d.CustomCss.Length <= 200000) sb.Append("\n/* eigenes CSS */\n").Append(d.CustomCss).Append('\n');
            return sb.ToString();
        }
    }

    // User scripts: %APPDATA%\PeachClient\discord\scripts\*.js. Opt-in: a script only runs once it is switched on.
    internal static class DiscordScripts
    {
        public const string Template =
            "// Peach-Skript f\u00FCr Discord. L\u00E4uft nur im Discord-Fenster.\r\n" +
            "// ACHTUNG: Skripte k\u00F6nnen alles im Discord-Fenster, auch auf dein Konto zugreifen.\r\n" +
            "// Schalte nur eigene oder vertrauensw\u00FCrdige Skripte ein.\r\n" +
            "//\r\n" +
            "//   peach.css('...')                      eigenes CSS hinzuf\u00FCgen\r\n" +
            "//   peach.toast('Text')                   kleine Meldung unten rechts\r\n" +
            "//   peach.command('hallo', a => 'Hallo ' + a)   eigener Befehl /hallo; der R\u00FCckgabetext landet im Chatfeld\r\n" +
            "//   peach.ready(() => { ... })            ausf\u00FChren, sobald die Seite geladen ist\r\n" +
            "\r\n" +
            "peach.command('uhr', () => 'Es ist ' + new Date().toLocaleTimeString() + ' Uhr');\r\n";

        public static string Dir(Config cfg) { return Path.Combine(cfg.ConfigDir(), "discord", "scripts"); }

        public static bool ValidName(string name) { return name != null && Regex.IsMatch(name, @"^[\w.-]+\.js$") && name.Length <= 64; }

        public static List<string> Files(Config cfg)
        {
            List<string> l = new List<string>();
            try
            {
                if (Directory.Exists(Dir(cfg)))
                    foreach (string f in Directory.GetFiles(Dir(cfg), "*.js")) if (ValidName(Path.GetFileName(f))) l.Add(Path.GetFileName(f));
            }
            catch { }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        // creates a new script from the template (and switches it on: you just wrote it yourself)
        public static string Create(Config cfg, string baseName)
        {
            string stem = Regex.Replace((baseName ?? "").Trim().ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-');
            if (stem.Length == 0) stem = "skript";
            if (stem.EndsWith("-js")) stem = stem.Substring(0, stem.Length - 3);
            Directory.CreateDirectory(Dir(cfg));
            string name = stem + ".js";
            for (int n = 2; File.Exists(Path.Combine(Dir(cfg), name)); n++) name = stem + "-" + n + ".js";
            File.WriteAllText(Path.Combine(Dir(cfg), name), Template, new UTF8Encoding(false));
            if (!cfg.Discord.EnabledScripts.Contains(name)) cfg.Discord.EnabledScripts.Add(name);
            cfg.Save();
            return name;
        }

        public static bool Delete(Config cfg, string name)
        {
            if (!ValidName(name)) return false;
            try { File.Delete(Path.Combine(Dir(cfg), name)); } catch { return false; }
            cfg.Discord.EnabledScripts.Remove(name);
            cfg.Save();
            return true;
        }

        // the scripts that are switched on and exist (size-limited)
        public static List<KeyValuePair<string, string>> Enabled(Config cfg)
        {
            List<KeyValuePair<string, string>> l = new List<KeyValuePair<string, string>>();
            foreach (string name in Files(cfg))
            {
                if (!cfg.Discord.EnabledScripts.Contains(name)) continue;
                try
                {
                    FileInfo fi = new FileInfo(Path.Combine(Dir(cfg), name));
                    if (fi.Length > 200000) continue;
                    l.Add(new KeyValuePair<string, string>(name, File.ReadAllText(fi.FullName, Encoding.UTF8)));
                }
                catch { }
            }
            return l;
        }
    }

    // The JavaScript that Peach puts into Discord pages, plus its settings.
    internal static class DiscordBridge
    {
        public static bool ValidCommandName(string name)
        {
            return name != null && Regex.IsMatch(name, @"^[A-Za-z][\w-]{0,31}$") && !string.Equals(name, "peach", StringComparison.OrdinalIgnoreCase);
        }

        // abbreviations typed in the message box: 2+ characters, no whitespace
        public static bool ValidAbbreviation(string a) { return a != null && a.Length >= 2 && a.Length <= 32 && !Regex.IsMatch(a, @"\s"); }

        // What an "open" command really does. https URLs get {args} URL-encoded; for programs and files {args} is NOT
        // filled in (so text from a chat can never become a command line). false = nothing to run.
        public static bool ResolveOpen(CommandEntry c, string args, out string target, out string arguments)
        {
            target = null; arguments = null;
            if (c == null || c.Action != "open" || string.IsNullOrWhiteSpace(c.Text)) return false;
            string t = c.Text.Trim();
            Uri u;
            if (Uri.TryCreate(t.Replace("{args}", "x"), UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
            {
                target = t.Replace("{args}", Uri.EscapeDataString(args ?? ""));
                return true;
            }
            target = t;
            arguments = string.IsNullOrWhiteSpace(c.Args) ? null : c.Args.Trim();
            return true;
        }

        public static string SettingsJson(DiscordConfig d)
        {
            List<Dictionary<string, object>> cmds = new List<Dictionary<string, object>>();
            foreach (CommandEntry c in d.Commands)
                if (c != null && ValidCommandName(c.Name) && !string.IsNullOrEmpty(c.Text))
                {
                    string combo = string.IsNullOrEmpty(c.Hotkey) ? null : Combo.Normalize(c.Hotkey);
                    // the page only needs the target of an "open" command to show a message: the real work happens in Peach
                    cmds.Add(new Dictionary<string, object> { { "name", c.Name }, { "text", c.Action == "open" ? "" : c.Text }, { "action", c.Action == "open" ? "open" : "text" }, { "hotkey", combo ?? "" } });
                }
            List<Dictionary<string, object>> abbr = new List<Dictionary<string, object>>();
            foreach (Snippet s in d.Abbreviations)
                if (ValidAbbreviation(s == null ? null : s.Abbr) && !string.IsNullOrEmpty(s.Text))
                    abbr.Add(new Dictionary<string, object> { { "a", s.Abbr }, { "t", s.Text } });
            Dictionary<string, object> o = new Dictionary<string, object>
            {
                { "css", DiscordTheme.BuildCss(d) },
                { "commands", cmds },
                { "abbr", abbr },
                { "sendNow", d.SendCommandsNow },
                { "themes", new List<string>(new string[] { "standard", "peach", "mitternacht", "wald", "amoled", "violett" }) },
            };
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(o).Replace(((char)0x2028).ToString(), "\\u2028").Replace(((char)0x2029).ToString(), "\\u2029");
        }

        // The page script that registers first. `forced`: also run on non-Discord pages (tests).
        public static string Bridge(DiscordConfig d, bool forced)
        {
            return (forced ? "window.__peachForce = true;\n" : "") + "window.__peachInit = " + SettingsJson(d) + ";\n" + BridgeJs;
        }

        // One injected script per user script, so a typo in one cannot break the others.
        public static string WrapUserScript(string name, string code)
        {
            string safeName = Regex.Replace(name, @"[^\w.-]", "_");
            return "(function () { var __p = window.__peach; if (!__p) return;\ntry { (function (peach) {\n" + code + "\n})(__p.api); } catch (e) { try { console.error('Peach script " + safeName + ":', e); __p.toast('Skript-Fehler in " + safeName + ": ' + (e && e.message)); } catch (e2) {} }\n})();\n";
        }

        const string BridgeJs = @"
(function () {
  'use strict';
  if (window.top !== window) return;
  var forced = !!window.__peachForce;
  if (!forced && !/(^|\.)discord\.com$/i.test(location.hostname || '')) return;
  if (window.__peach) return;

  var P = window.__peach = { version: 1, cfg: { css: '', commands: [], sendNow: false, themes: [] }, userCommands: {}, sheets: [] };

  // styles: constructable stylesheets are not blocked by the page's content security policy
  var main = null, fallback = null;
  function addSheet(css) {
    try {
      var s = new CSSStyleSheet(); s.replaceSync(css || '');
      document.adoptedStyleSheets = Array.prototype.concat.call(document.adoptedStyleSheets, [s]);
      return s;
    } catch (e) {
      try { var el = document.createElement('style'); el.textContent = css || ''; (document.head || document.documentElement).appendChild(el); return el; } catch (e2) {}
    }
    return null;
  }
  P.applyCss = function (css) {
    css = css || '';
    if (main && main.replaceSync) { try { main.replaceSync(css); return; } catch (e) {} }
    if (fallback) { fallback.textContent = css; return; }
    var s = addSheet(css);
    if (s && s.replaceSync) main = s; else fallback = s;
  };

  P.toast = function (text) {
    try {
      var d = document.createElement('div');
      d.textContent = String(text);
      d.style.cssText = 'position:fixed;right:20px;bottom:24px;max-width:440px;white-space:pre-wrap;z-index:2147483647;padding:12px 16px;border-radius:10px;background:#2d211e;color:#f6e9e4;border:1px solid #ff7a59;font:14px/1.45 Segoe UI,sans-serif;box-shadow:0 6px 24px rgba(0,0,0,.45)';
      (document.body || document.documentElement).appendChild(d);
      setTimeout(function () { if (d.parentNode) d.parentNode.removeChild(d); }, 6000);
    } catch (e) {}
  };

  function post(msg) { try { window.chrome.webview.postMessage(msg); } catch (e) {} }
  function two(n) { return (n < 10 ? '0' : '') + n; }
  function expand(text, args) {
    var n = new Date();
    return String(text).split('{args}').join(args).split('{date}').join(two(n.getDate()) + '.' + two(n.getMonth() + 1) + '.' + n.getFullYear()).split('{time}').join(two(n.getHours()) + ':' + two(n.getMinutes()));
  }

  function helpText() {
    var t = 'Peach-Befehle (im Chatfeld, mit Enter):\n/peach settings  - Einstellungen\n/peach theme <name>  - ' + (P.cfg.themes || []).join(', ') + '\n/peach commands  - deine Befehle';
    var c = P.cfg.commands || [];
    if (c.length) t += '\n\nEigene: ' + c.map(function (x) { return '/' + x.name; }).join('  ');
    return t;
  }

  // -> { handled: bool, text?: string, send?: bool }
  P.run = function (name, args) {
    if (name === 'peach') {
      var parts = String(args || '').split(/\s+/);
      var sub = (parts[0] || 'help').toLowerCase();
      if (sub === 'help') P.toast(helpText());
      else if (sub === 'settings') post({ type: 'settings' });
      else if (sub === 'theme') post({ type: 'theme', value: parts[1] || '' });
      else if (sub === 'commands') {
        var all = (P.cfg.commands || []).map(function (x) { return '/' + x.name + '  ->  ' + x.text; });
        for (var k in P.userCommands) all.push('/' + k + '  (Skript)');
        P.toast(all.length ? all.join('\n') : 'Noch keine eigenen Befehle (Peach-Einstellungen > Befehle).');
      } else P.toast('Unbekannt: /peach ' + sub + '   (siehe /peach help)');
      return { handled: true };
    }
    var u = P.userCommands[name];
    if (u) {
      var r;
      try { r = u(args); } catch (e) { P.toast('Skript-Fehler in /' + name + ': ' + (e && e.message)); return { handled: true }; }
      return r == null ? { handled: true } : { handled: true, text: String(r), send: !!P.cfg.sendNow };
    }
    var cmds = P.cfg.commands || [];
    for (var i = 0; i < cmds.length; i++)
      if (String(cmds[i].name).toLowerCase() === name) {
        if (cmds[i].action === 'open') { post({ type: 'run', name: cmds[i].name, args: args }); P.toast('Oeffne /' + cmds[i].name + ' ...'); return { handled: true }; }
        return { handled: true, text: expand(cmds[i].text, args), send: !!P.cfg.sendNow };
      }
    return { handled: false };
  };

  function boxText(box) { return String(box.innerText || box.textContent || '').replace(/[\u200b\u200c\u200d\ufeff]/g, '').replace(/\r/g, ''); }
  function selectAll(box) { var s = window.getSelection(), r = document.createRange(); r.selectNodeContents(box); s.removeAllRanges(); s.addRange(r); }
  function fill(box, text, send) {
    box.focus(); selectAll(box);
    if (text == null) { document.execCommand('delete'); return; }
    document.execCommand('insertText', false, text);
    if (send) setTimeout(function () {
      var ev = new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true });
      ev.__peach = true; box.dispatchEvent(ev);
    }, 80);
  }

  // commands typed into the message box are caught before Discord sees them (capture phase)
  window.addEventListener('keydown', function (e) {
    if (e.key !== 'Enter' || e.shiftKey || e.ctrlKey || e.altKey || e.isComposing || e.__peach) return;
    var t = e.target;
    var box = t && t.closest ? t.closest('[role=textbox]') : null;
    if (!box) return;
    var m = /^\/([A-Za-z][\w-]*)(?:\s+([\s\S]*))?$/.exec(boxText(box).trim());
    if (!m) return;
    var res = P.run(m[1].toLowerCase(), (m[2] || '').trim());
    if (!res.handled) return;
    e.preventDefault(); e.stopImmediatePropagation();
    fill(box, res.text == null ? null : res.text, res.send);
  }, true);

  // ---- hotkeys: only while the Discord window has the focus (nothing is registered system-wide) ----
  function comboOf(e) {
    var code = e.code || '', k = null;
    if (/^Key[A-Z]$/.test(code)) k = code.charAt(3).toLowerCase();
    else if (/^Digit[0-9]$/.test(code)) k = code.charAt(5);
    else if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code)) k = code.toLowerCase();
    else {
      var named = { Space: 'space', Enter: 'enter', Escape: 'esc', Tab: 'tab', Backspace: 'backspace', Delete: 'delete', Insert: 'insert', Home: 'home', End: 'end', PageUp: 'pageup', PageDown: 'pagedown', ArrowLeft: 'left', ArrowUp: 'up', ArrowRight: 'right', ArrowDown: 'down' };
      k = named[code] || null;
    }
    if (!k) return null;
    var parts = [];
    if (e.ctrlKey) parts.push('ctrl'); if (e.altKey) parts.push('alt'); if (e.shiftKey) parts.push('shift'); if (e.metaKey) parts.push('win');
    if (!e.ctrlKey && !e.altKey && !e.metaKey) return null;   // Shift alone is just typing capital letters
    parts.push(k);
    return parts.join('+');
  }
  function findBox() {
    var a = document.activeElement, b = a && a.closest ? a.closest('[role=textbox]') : null;
    if (b) return b;
    var all = document.querySelectorAll('[role=textbox][contenteditable=true]');
    return all.length ? all[all.length - 1] : null;
  }
  function insertAtCaret(box, text, send) {
    box.focus();
    document.execCommand('insertText', false, text);
    if (send) setTimeout(function () {
      var ev = new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true });
      ev.__peach = true; box.dispatchEvent(ev);
    }, 80);
  }
  window.addEventListener('keydown', function (e) {
    if (e.__peach || e.repeat || e.isComposing) return;
    var cmds = P.cfg.commands || [];
    if (!cmds.length) return;
    var combo = comboOf(e);
    if (!combo) return;
    for (var i = 0; i < cmds.length; i++) {
      if (!cmds[i].hotkey || cmds[i].hotkey !== combo) continue;
      e.preventDefault(); e.stopImmediatePropagation();
      var res = P.run(String(cmds[i].name).toLowerCase(), '');
      if (res.handled && res.text != null) { var box = findBox(); if (box) insertAtCaret(box, res.text, res.send); else P.toast(res.text); }
      return;
    }
  }, true);

  // ---- abbreviations (Fast Type for the Discord message box only) ----
  function isAl(c) { return /[A-Za-z0-9À-ɏ]/.test(c); }
  function textBeforeCaret(box) {
    var sel = window.getSelection();
    if (!sel.rangeCount) return boxText(box);
    var r = sel.getRangeAt(0), pre = document.createRange();
    pre.selectNodeContents(box); pre.setEnd(r.endContainer, r.endOffset);
    return pre.toString().replace(/[​‌‍﻿]/g, '');
  }
  var expanding = false;
  window.addEventListener('beforeinput', function (e) {
    if (expanding || e.inputType !== 'insertText' || !e.data || e.data.length !== 1) return;
    var list = P.cfg.abbr || [];
    if (!list.length) return;
    var t = e.target, box = t && t.closest ? t.closest('[role=textbox]') : null;
    if (!box) return;
    var before = textBeforeCaret(box) + e.data, best = null;
    for (var i = 0; i < list.length; i++) {
      var ab = String(list[i].a);
      if (ab.length > before.length || before.slice(before.length - ab.length).toLowerCase() !== ab.toLowerCase()) continue;
      var start = before.length - ab.length;
      if (start > 0 && isAl(before.charAt(start - 1)) && isAl(ab.charAt(0))) continue;   // inside a word
      if (!best || ab.length > best.a.length) best = list[i];
    }
    if (!best) return;
    e.preventDefault();
    // editing from inside beforeinput is unreliable: do it right after, before the next key can arrive
    Promise.resolve().then(function () {
      expanding = true;
      try {
        box.focus();
        for (var k = 0; k < String(best.a).length - 1; k++) document.execCommand('delete');   // the last character was never typed
        document.execCommand('insertText', false, expand(best.t, ''));
      } finally { expanding = false; }
    });
  }, true);

  P.update = function (cfg) { if (cfg) P.cfg = cfg; P.applyCss(P.cfg.css); };

  // what user scripts get
  var readyFns = [], isReady = document.readyState !== 'loading';
  document.addEventListener('DOMContentLoaded', function () { isReady = true; readyFns.splice(0).forEach(function (f) { try { f(); } catch (e) { P.toast('Skript-Fehler: ' + (e && e.message)); } }); });
  P.api = {
    css: function (text) { var s = addSheet(String(text)); P.sheets.push(s); return s; },
    toast: P.toast,
    command: function (name, fn) { P.userCommands[String(name).toLowerCase()] = fn; },
    ready: function (fn) { if (isReady) fn(); else readyFns.push(fn); },
    settings: function () { return JSON.parse(JSON.stringify(P.cfg)); }
  };

  P.update(window.__peachInit);
})();
";
    }
}
