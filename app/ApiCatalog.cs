using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Peach
{
    internal class ApiItem
    {
        public string Name = "", Signature = "", Doc = "", Group = "", Module = "", Permission = "", Kind = "f";
    }

    internal class Completion
    {
        public string Label = "", Insert = "", Detail = "", Kind = "";
    }

    internal class CompletionResult
    {
        public int Start;
        public List<Completion> Items = new List<Completion>();
    }

    // Everything the editor knows about APIs: the Peach API (built in) plus the modules of the installed runtimes
    // (Node built-ins, the Python standard library, PowerShell commands), read from those runtimes themselves.
    internal class ApiCatalog
    {
        public static readonly ApiCatalog Instance = new ApiCatalog();

        public readonly Dictionary<string, Dictionary<string, string>> JsModules = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, string>> PyModules = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public readonly List<string> PsCommands = new List<string>();
        readonly HashSet<string> ready = new HashSet<string>();
        readonly HashSet<string> loading = new HashSet<string>();
        readonly object gate = new object();

        static string Norm(string lang) { return lang == "ts" ? "js" : lang; }

        public bool Ready(string lang) { lock (gate) return ready.Contains(Norm(lang)); }

        public Dictionary<string, Dictionary<string, string>> Modules(string lang)
        {
            lang = Norm(lang);
            return lang == "js" ? JsModules : (lang == "py" ? PyModules : null);
        }

        // ---- the Peach API ------------------------------------------------------------------------------
        static ApiItem P(string name, string sig, string doc, string perm, string kind)
        {
            return new ApiItem { Name = name, Signature = sig, Doc = doc, Permission = perm, Group = "Peach", Kind = kind };
        }

        public static List<ApiItem> PeachItems(string lang)
        {
            List<ApiItem> l = new List<ApiItem>();
            switch (Norm(lang))
            {
                case "js":
                    l.Add(P("peach.log", "peach.log(...args)", "Schreibt eine Zeile ins Log dieses Addons (Knopf \u201ELog\u201C).", "", "f"));
                    l.Add(P("peach.notify", "peach.notify(title, message)", "Zeigt eine Windows-Benachrichtigung.", "notify", "f"));
                    l.Add(P("peach.systemInfo", "peach.systemInfo()", "Infos zum PC: Windows-Version, CPU, RAM, Benutzer.", "", "f"));
                    l.Add(P("peach.storage.get", "peach.storage.get(key)", "Liest einen gespeicherten Wert (bleibt nach einem Neustart erhalten).", "", "f"));
                    l.Add(P("peach.storage.set", "peach.storage.set(key, value)", "Speichert einen Wert f\u00FCr dieses Addon. null l\u00F6scht ihn.", "", "f"));
                    l.Add(P("peach.clipboard.get", "peach.clipboard.get()", "Liest den Text der Zwischenablage.", "clipboard", "f"));
                    l.Add(P("peach.clipboard.set", "peach.clipboard.set(text)", "Schreibt Text in die Zwischenablage.", "clipboard", "f"));
                    l.Add(P("peach.windows.list", "peach.windows.list()", "Offene Fenster: pid, process, title.", "windows", "f"));
                    l.Add(P("peach.registry.get", "peach.registry.get(path, name)", "Liest einen Registry-Wert (nur lesen), z. B. 'HKCU:\\\\Software\\\\\u2026'.", "registry", "f"));
                    l.Add(P("peach.wallpaper.set", "peach.wallpaper.set(path)", "Setzt das Hintergrundbild.", "wallpaper", "f"));
                    l.Add(P("peach.shell.run", "peach.shell.run(command, args = [], timeoutMs)", "Startet ein Programm, liefert { code, stdout, stderr }.", "shell", "f"));
                    l.Add(P("peach.shell.open", "peach.shell.open(target)", "\u00D6ffnet eine URL oder Datei.", "shell", "f"));
                    l.Add(P("peach.hotkey", "peach.hotkey(combo, handler)", "Globaler Hotkey, z. B. 'ctrl+alt+k' (mindestens ein Modifier).", "hotkeys", "f"));
                    l.Add(P("peach.hotkeys.unregister", "peach.hotkeys.unregister(combo)", "Gibt einen Hotkey wieder frei.", "hotkeys", "f"));
                    l.Add(P("peach.on", "peach.on(type, handler)", "Events: 'focus' (Recht windows), 'clipboard' (Recht clipboard), 'hotkey'.", "", "f"));
                    l.Add(P("peach.events.poll", "peach.events.poll(timeoutMs = 25000)", "Wartet auf Events (Long-Poll), wenn du sie selbst abholen willst.", "", "f"));
                    l.Add(P("peach.events.subscribe", "peach.events.subscribe(events)", "Abonniert 'focus' oder 'clipboard'.", "", "f"));
                    l.Add(P("peach.sleep", "peach.sleep(ms)", "Wartet (Promise).", "", "f"));
                    l.Add(P("peach.call", "peach.call(method, params = {})", "Ruft eine beliebige API-Methode direkt auf, z. B. 'system.info'.", "", "f"));
                    l.Add(P("peach.mod", "peach.mod", "Die ID dieses Addons (Eigenschaft).", "", "v"));
                    break;
                case "py":
                    l.Add(P("peach.log", "peach.log(*args)", "Schreibt eine Zeile ins Log dieses Addons.", "", "f"));
                    l.Add(P("peach.notify", "peach.notify(title, message)", "Zeigt eine Windows-Benachrichtigung.", "notify", "f"));
                    l.Add(P("peach.system_info", "peach.system_info()", "Infos zum PC.", "", "f"));
                    l.Add(P("peach.storage_get", "peach.storage_get(key=None)", "Liest einen gespeicherten Wert (ohne key: alles).", "", "f"));
                    l.Add(P("peach.storage_set", "peach.storage_set(key, value)", "Speichert einen Wert. None l\u00F6scht ihn.", "", "f"));
                    l.Add(P("peach.clipboard_get", "peach.clipboard_get()", "Liest die Zwischenablage.", "clipboard", "f"));
                    l.Add(P("peach.clipboard_set", "peach.clipboard_set(text)", "Schreibt in die Zwischenablage.", "clipboard", "f"));
                    l.Add(P("peach.windows_list", "peach.windows_list()", "Offene Fenster: pid, process, title.", "windows", "f"));
                    l.Add(P("peach.registry_get", "peach.registry_get(path, name)", "Liest einen Registry-Wert (nur lesen).", "registry", "f"));
                    l.Add(P("peach.wallpaper_set", "peach.wallpaper_set(path)", "Setzt das Hintergrundbild.", "wallpaper", "f"));
                    l.Add(P("peach.shell_run", "peach.shell_run(command, args=(), timeout_ms=None)", "Startet ein Programm, liefert code/stdout/stderr.", "shell", "f"));
                    l.Add(P("peach.shell_open", "peach.shell_open(target)", "\u00D6ffnet eine URL oder Datei.", "shell", "f"));
                    l.Add(P("peach.hotkey", "peach.hotkey(combo, fn)", "Globaler Hotkey, z. B. \"ctrl+alt+k\". Danach peach.run() aufrufen.", "hotkeys", "f"));
                    l.Add(P("peach.hotkey_unregister", "peach.hotkey_unregister(combo)", "Gibt einen Hotkey frei.", "hotkeys", "f"));
                    l.Add(P("peach.on", "peach.on(event_type, fn)", "Events: \"focus\" (Recht windows), \"clipboard\" (Recht clipboard), \"hotkey\".", "", "f"));
                    l.Add(P("peach.run", "peach.run()", "Blockiert und verteilt Events an deine Handler (ans Ende des Skripts).", "", "f"));
                    l.Add(P("peach.events_poll", "peach.events_poll(timeout_ms=25000)", "Holt Events selbst ab.", "", "f"));
                    l.Add(P("peach.call", "peach.call(method, **params)", "Ruft eine beliebige API-Methode direkt auf.", "", "f"));
                    l.Add(P("peach.sleep", "peach.sleep(seconds)", "Wartet (time.sleep).", "", "f"));
                    l.Add(P("peach.mod", "peach.mod", "Die ID dieses Addons.", "", "v"));
                    break;
                case "ps1":
                    l.Add(P("Peach-Log", "Peach-Log $Message", "Schreibt eine Zeile ins Log dieses Addons.", "", "f"));
                    l.Add(P("Peach-Notify", "Peach-Notify $Title $Message", "Zeigt eine Windows-Benachrichtigung.", "notify", "f"));
                    l.Add(P("Peach-SystemInfo", "Peach-SystemInfo", "Infos zum PC.", "", "f"));
                    l.Add(P("Peach-StorageGet", "Peach-StorageGet $Key", "Liest einen gespeicherten Wert.", "", "f"));
                    l.Add(P("Peach-StorageSet", "Peach-StorageSet $Key $Value", "Speichert einen Wert.", "", "f"));
                    l.Add(P("Peach-ClipboardGet", "Peach-ClipboardGet", "Liest die Zwischenablage.", "clipboard", "f"));
                    l.Add(P("Peach-ClipboardSet", "Peach-ClipboardSet $Text", "Schreibt in die Zwischenablage.", "clipboard", "f"));
                    l.Add(P("Peach-WindowsList", "Peach-WindowsList", "Offene Fenster: pid, process, title.", "windows", "f"));
                    l.Add(P("Peach-RegistryGet", "Peach-RegistryGet $Path $Name", "Liest einen Registry-Wert (nur lesen).", "registry", "f"));
                    l.Add(P("Peach-WallpaperSet", "Peach-WallpaperSet $Path", "Setzt das Hintergrundbild.", "wallpaper", "f"));
                    l.Add(P("Peach-ShellRun", "Peach-ShellRun $Command [$Args]", "Startet ein Programm, liefert code/stdout/stderr.", "shell", "f"));
                    l.Add(P("Peach-ShellOpen", "Peach-ShellOpen $Target", "\u00D6ffnet eine URL oder Datei.", "shell", "f"));
                    l.Add(P("Peach-Hotkey", "Peach-Hotkey 'ctrl+alt+h' { \u2026 }", "Globaler Hotkey. Danach Peach-Run aufrufen.", "hotkeys", "f"));
                    l.Add(P("Peach-On", "Peach-On 'focus' { param($e) \u2026 }", "Events: 'focus' (Recht windows), 'clipboard' (Recht clipboard).", "", "f"));
                    l.Add(P("Peach-Run", "Peach-Run", "Blockiert und verteilt Events an deine Handler (ans Ende des Skripts).", "", "f"));
                    l.Add(P("Invoke-Peach", "Invoke-Peach 'system.info' @{ }", "Ruft eine beliebige API-Methode direkt auf.", "", "f"));
                    break;
                case "bat":
                case "vbs":
                    foreach (string m in new string[] { "log", "notify", "system.info", "storage.get", "storage.set", "clipboard.get", "clipboard.set", "windows.list", "registry.get", "wallpaper.set", "shell.run", "shell.open", "hotkey.register", "events.poll" })
                    {
                        string perm = m.StartsWith("notify") ? "notify" : m.StartsWith("clipboard") ? "clipboard" : m.StartsWith("windows") ? "windows" : m.StartsWith("registry") ? "registry" : m.StartsWith("wallpaper") ? "wallpaper" : m.StartsWith("shell") ? "shell" : m.StartsWith("hotkey") ? "hotkeys" : "";
                        l.Add(P("%PEACH_API%/" + m, "POST %PEACH_API%/" + m, "HTTP-Aufruf mit Header Authorization: Bearer %PEACH_TOKEN% und JSON-Body.", perm, "f"));
                    }
                    break;
            }
            return l;
        }

        public static string[] Globals(string lang)
        {
            switch (Norm(lang))
            {
                case "js": return new string[] { "peach", "console", "process", "require", "module", "exports", "Buffer", "JSON", "Math", "Promise", "Date", "Array", "Object", "String", "Number", "Boolean", "Map", "Set", "Error", "RegExp", "URL", "fetch", "setTimeout", "setInterval", "clearTimeout", "clearInterval", "parseInt", "parseFloat", "__dirname", "__filename" };
                case "py": return new string[] { "peach", "print", "len", "range", "open", "str", "int", "float", "bool", "list", "dict", "set", "tuple", "sorted", "enumerate", "zip", "map", "filter", "sum", "min", "max", "abs", "any", "all", "isinstance", "input", "format", "repr", "type", "round", "reversed", "bytes", "Exception", "ValueError", "KeyError", "__name__" };
                default: return new string[0];
            }
        }

        // ---- loading the runtime APIs (cached on disk for two weeks) ---------------------------------------
        static string CacheDir() { return Path.GetDirectoryName(Config.DefaultPath()); }

        public void EnsureLoaded(string lang, Action done)
        {
            string key = Norm(lang);
            if (key != "js" && key != "py" && key != "ps1") { if (done != null) done(); return; }
            lock (gate)
            {
                if (ready.Contains(key)) { if (done != null) done(); return; }
                if (loading.Contains(key)) { ThreadPool.QueueUserWorkItem(delegate { WaitFor(key); if (done != null) done(); }); return; }
                loading.Add(key);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Load(key); } catch { /* runtime missing or broken: only the Peach API is offered */ }
                lock (gate) { loading.Remove(key); ready.Add(key); }
                if (done != null) done();
            });
        }

        void WaitFor(string key) { for (int i = 0; i < 400; i++) { lock (gate) if (!loading.Contains(key)) return; Thread.Sleep(100); } }

        void Load(string key)
        {
            string cache = Path.Combine(CacheDir(), "api-" + key + ".json");
            string json = null;
            try { if (File.Exists(cache) && (DateTime.Now - File.GetLastWriteTime(cache)).TotalDays < 14) json = File.ReadAllText(cache, Encoding.UTF8); } catch { }
            if (json == null)
            {
                json = Query(key);
                if (json == null) return;
                try { Directory.CreateDirectory(CacheDir()); File.WriteAllText(cache, json, new UTF8Encoding(false)); } catch { }
            }
            Fill(key, json);
        }

        // Public for tests: puts a runtime's JSON answer into the catalog.
        public void Fill(string key, string json)
        {
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (key == "ps1")
            {
                System.Collections.IEnumerable arr = js.DeserializeObject(json) as System.Collections.IEnumerable;
                if (arr == null) return;
                lock (PsCommands) foreach (object o in arr) if (o is string) PsCommands.Add((string)o);
                return;
            }
            System.Collections.IDictionary root = js.DeserializeObject(json) as System.Collections.IDictionary;
            if (root == null) return;
            Dictionary<string, Dictionary<string, string>> target = key == "js" ? JsModules : PyModules;
            lock (target)
                foreach (System.Collections.DictionaryEntry e in root)
                {
                    Dictionary<string, string> members = new Dictionary<string, string>(StringComparer.Ordinal);
                    System.Collections.IEnumerable list = e.Value as System.Collections.IEnumerable;
                    if (list != null)
                        foreach (object o in list)
                        {
                            string s = o as string;
                            if (s == null) continue;
                            int c = s.LastIndexOf(':');
                            if (c > 0) members[s.Substring(0, c)] = s.Substring(c + 1); else members[s] = "";
                        }
                    target[(string)e.Key] = members;
                }
        }

        // Runs a helper script in the installed runtime and returns its JSON output (or null).
        static string Query(string key)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "peach-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                if (key == "js")
                {
                    string f = Path.Combine(tmp, "q.js");
                    File.WriteAllText(f, "const out = {};\nfor (const m of require('module').builtinModules) {\n  if (m.startsWith('_') || m.indexOf('internal') >= 0 || m.startsWith('node:')) continue;\n  try { const x = require(m);\n    out[m] = Object.keys(x).filter(k => !k.startsWith('_')).map(k => { let t = ''; try { t = typeof x[k]; } catch (e) {} return k + ':' + t; });\n  } catch (e) { out[m] = []; }\n}\nprocess.stdout.write(JSON.stringify(out));\n");
                    return Run(new string[] { "node" }, "\"" + f + "\"", 30000);
                }
                if (key == "py")
                {
                    string f = Path.Combine(tmp, "q.py");
                    File.WriteAllText(f,
                        "import sys, os, json, importlib\nreal = sys.stdout\n" +
                        "skip = {'antigravity','this','idlelib','turtledemo','turtle','tkinter','test','lib2to3','pydoc_data','ensurepip','venv','nturl2path','msilib','distutils','xxlimited','xxlimited_35'}\n" +
                        "names = getattr(sys, 'stdlib_module_names', None)\nif names is None:\n    import pkgutil\n    names = [m.name for m in pkgutil.iter_modules()]\n" +
                        "out = {}\nsys.stdout = open(os.devnull, 'w'); sys.stderr = open(os.devnull, 'w')\n" +
                        "for n in sorted(names):\n    if n.startswith('_') or n in skip:\n        continue\n    try:\n        m = importlib.import_module(n)\n" +
                        "        out[n] = [a + ':' + ('function' if callable(getattr(m, a, None)) else 'value') for a in dir(m) if not a.startswith('_')]\n    except BaseException:\n        out[n] = []\n" +
                        "sys.stdout = real\nreal.write(json.dumps(out))\n");
                    return Run(new string[] { "python", "py" }, "-I \"" + f + "\"", 40000);
                }
                string ps = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $n=@(Get-Command -CommandType Cmdlet,Function,Alias -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Where-Object { $_ -match '^[A-Za-z]+-[A-Za-z0-9]+' } | Sort-Object -Unique); $n | ConvertTo-Json -Compress";
                return Run(new string[] { "powershell.exe" }, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(ps)), 40000);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Starts the first program that exists (PATH aliases like the Windows Store python included); returns stdout or null.
        public static string Run(string[] exes, string args, int timeoutMs)
        {
            foreach (string exe in exes)
            {
                try
                {
                    string a = args;
                    string name = exe;
                    if (exe == "py") a = "-3 " + args;
                    ProcessStartInfo psi = new ProcessStartInfo(name, a);
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                    using (Process p = Process.Start(psi))
                    {
                        StringBuilder err = new StringBuilder();
                        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) err.AppendLine(e.Data); };
                        p.BeginErrorReadLine();
                        System.Threading.Tasks.Task<string> read = System.Threading.Tasks.Task.Factory.StartNew(delegate { return p.StandardOutput.ReadToEnd(); });
                        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                        string output = read.Result;
                        if (p.ExitCode == 0 && output.Length > 0) return output;
                        LastError = err.ToString();
                    }
                }
                catch (System.ComponentModel.Win32Exception) { /* try the next candidate */ }
            }
            return null;
        }

        public static string LastError = "";

        // ---- queries used by the editor --------------------------------------------------------------------
        public List<string> ModuleNames(string lang)
        {
            Dictionary<string, Dictionary<string, string>> m = Modules(lang);
            if (m == null) return new List<string>();
            lock (m) { List<string> l = new List<string>(m.Keys); l.Sort(StringComparer.Ordinal); return l; }
        }

        public Dictionary<string, string> MembersOf(string lang, string module)
        {
            Dictionary<string, Dictionary<string, string>> m = Modules(lang);
            if (m == null) return null;
            lock (m) { Dictionary<string, string> d; return m.TryGetValue(module, out d) ? d : null; }
        }

        // All entries for the API panel, filtered by the search text.
        public List<ApiItem> Search(string lang, string query, int max)
        {
            query = (query ?? "").Trim();
            List<ApiItem> result = new List<ApiItem>();
            Action<ApiItem> add = delegate (ApiItem it)
            {
                if (result.Count >= max) return;
                if (query.Length == 0 || it.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || it.Signature.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || it.Doc.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(it);
            };
            foreach (ApiItem p in PeachItems(lang)) add(p);
            Dictionary<string, Dictionary<string, string>> mods = Modules(lang);
            if (mods != null)
            {
                string label = Norm(lang) == "js" ? "Node-Modul" : "Python-Modul";
                foreach (string name in ModuleNames(lang)) add(new ApiItem { Name = name, Signature = name + "  (" + label + ")", Doc = MembersOf(lang, name).Count + " Eintr\u00E4ge", Group = "Modul", Module = name, Kind = "m" });
                if (query.Length >= 2)
                    foreach (string name in ModuleNames(lang))
                    {
                        Dictionary<string, string> members = MembersOf(lang, name);
                        foreach (KeyValuePair<string, string> kv in members)
                        {
                            if (result.Count >= max) break;
                            if (kv.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && (name + "." + kv.Key).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            result.Add(new ApiItem { Name = name + "." + kv.Key, Signature = name + "." + kv.Key + (kv.Value == "function" ? "(\u2026)" : ""), Doc = label + " " + name, Group = "Funktion", Module = name, Kind = kv.Value == "function" ? "f" : "v" });
                        }
                    }
            }
            if (Norm(lang) == "ps1" && query.Length >= 2)
                lock (PsCommands)
                    foreach (string c in PsCommands)
                    {
                        if (result.Count >= max) break;
                        if (c.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(new ApiItem { Name = c, Signature = c, Doc = "PowerShell-Befehl", Group = "Befehl", Kind = "f" });
                    }
            return result;
        }
    }

    // What to suggest at the caret (pure: text + caret + language in, suggestions out).
    internal static class Completer
    {
        static readonly Regex JsRequire = new Regex(@"(?:const|let|var)\s+(\w+)\s*=\s*require\(\s*['""]([^'""]+)['""]\s*\)", RegexOptions.Compiled);
        static readonly Regex JsImport = new Regex(@"import\s+(?:\*\s+as\s+)?(\w+)\s+from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);
        static readonly Regex PyImport = new Regex(@"^[ \t]*import[ \t]+([^\r\n#]+)$", RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex PyFrom = new Regex(@"^[ \t]*from[ \t]+([\w.]+)[ \t]+import[ \t]+([^\r\n#]+)$", RegexOptions.Compiled | RegexOptions.Multiline);

        public static string NormLang(string l) { return l == "ts" ? "js" : l; }

        // alias -> module (JS: const fs = require('fs'); Python: import os / import json as j)
        public static Dictionary<string, string> Aliases(string text, string lang)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            lang = NormLang(lang);
            if (lang == "js")
            {
                foreach (Match m in JsRequire.Matches(text)) d[m.Groups[1].Value] = StripNode(m.Groups[2].Value);
                foreach (Match m in JsImport.Matches(text)) d[m.Groups[1].Value] = StripNode(m.Groups[2].Value);
            }
            else if (lang == "py")
            {
                foreach (Match m in PyImport.Matches(text))
                    foreach (string part in m.Groups[1].Value.Split(','))
                    {
                        string[] w = part.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (w.Length == 1) d[w[0].Split('.')[0]] = w[0].Split('.')[0];
                        else if (w.Length == 3 && w[1] == "as") d[w[2]] = w[0];
                    }
            }
            return d;
        }

        static string StripNode(string m) { return m.StartsWith("node:") ? m.Substring(5) : m; }

        // names brought in by "from x import a, b"
        static List<string> FromNames(string text)
        {
            List<string> l = new List<string>();
            foreach (Match m in PyFrom.Matches(text))
                foreach (string part in m.Groups[2].Value.Split(','))
                {
                    string[] w = part.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (w.Length == 1) l.Add(w[0]); else if (w.Length == 3 && w[1] == "as") l.Add(w[2]);
                }
            return l;
        }

        static bool IsWordChar(char c, bool ps) { return char.IsLetterOrDigit(c) || c == '_' || c == '$' || (ps && c == '-'); }

        public static CompletionResult Complete(string text, int caret, string lang, ApiCatalog cat, bool manual)
        {
            lang = NormLang(lang);
            CompletionResult r = new CompletionResult();
            if (caret < 0 || caret > text.Length) return r;
            bool ps = lang == "ps1";
            int start = caret;
            while (start > 0 && IsWordChar(text[start - 1], ps)) start--;
            string prefix = text.Substring(start, caret - start);
            int ls = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
            string lineBefore = text.Substring(ls, caret - ls);
            r.Start = start;

            // require('...') in JS
            if (lang == "js")
            {
                Match rq = Regex.Match(lineBefore, @"require\(\s*['""]([^'""]*)$");
                if (rq.Success)
                {
                    string typed = rq.Groups[1].Value;
                    r.Start = caret - typed.Length;
                    foreach (string m in cat.ModuleNames("js")) if (m.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) r.Items.Add(new Completion { Label = m, Insert = m, Detail = "Node-Modul", Kind = "m" });
                    return Sort(r, typed);
                }
            }
            // import x / from x import y in Python
            if (lang == "py")
            {
                Match fm = Regex.Match(lineBefore, @"^[ \t]*from[ \t]+([\w.]+)[ \t]+import[ \t]+(?:[\w, ]*,[ \t]*)?(\w*)$");
                if (fm.Success)
                {
                    Dictionary<string, string> mem = cat.MembersOf("py", fm.Groups[1].Value);
                    r.Start = caret - fm.Groups[2].Value.Length;
                    if (mem != null) foreach (KeyValuePair<string, string> kv in mem) if (kv.Key.StartsWith(fm.Groups[2].Value, StringComparison.OrdinalIgnoreCase)) r.Items.Add(new Completion { Label = kv.Key, Insert = kv.Key, Detail = fm.Groups[1].Value, Kind = kv.Value == "function" ? "f" : "v" });
                    return Sort(r, fm.Groups[2].Value);
                }
                Match im = Regex.Match(lineBefore, @"^[ \t]*(?:import[ \t]+(?:[\w., ]*,[ \t]*)?|from[ \t]+)([\w.]*)$");
                if (im.Success)
                {
                    string typed = im.Groups[1].Value;
                    r.Start = caret - typed.Length;
                    foreach (string m in cat.ModuleNames("py")) if (m.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) r.Items.Add(new Completion { Label = m, Insert = m, Detail = "Python-Modul", Kind = "m" });
                    if ("peach".StartsWith(typed, StringComparison.OrdinalIgnoreCase)) r.Items.Add(new Completion { Label = "peach", Insert = "peach", Detail = "Peach-API", Kind = "m" });
                    return Sort(r, typed);
                }
            }

            // member access:  something.prefix
            if (start > 0 && text[start - 1] == '.' && !ps)
            {
                int e = start - 1, b = e;
                while (b > 0 && (char.IsLetterOrDigit(text[b - 1]) || text[b - 1] == '_' || text[b - 1] == '.' || text[b - 1] == '$')) b--;
                string chain = text.Substring(b, e - b);
                if (chain.Length > 0)
                {
                    AddPeachMembers(r, chain, lang);
                    Dictionary<string, string> al = Aliases(text, lang);
                    string module;
                    if (!chain.Contains(".") && al.TryGetValue(chain, out module))
                    {
                        Dictionary<string, string> mem = cat.MembersOf(lang, module);
                        if (mem != null) foreach (KeyValuePair<string, string> kv in mem) r.Items.Add(new Completion { Label = kv.Key, Insert = kv.Key, Detail = module + (kv.Value == "function" ? "  (Funktion)" : ""), Kind = kv.Value == "function" ? "f" : "v" });
                    }
                    else if (!chain.Contains(".") && cat.MembersOf(lang, chain) != null && lang == "js")
                    {
                        foreach (KeyValuePair<string, string> kv in cat.MembersOf(lang, chain)) r.Items.Add(new Completion { Label = kv.Key, Insert = kv.Key, Detail = chain, Kind = kv.Value == "function" ? "f" : "v" });
                    }
                }
                return Sort(r, prefix);
            }

            // plain word
            if (prefix.Length < (manual ? 0 : 2)) return r;
            foreach (string k in CodeSyntax.Keywords(lang)) r.Items.Add(new Completion { Label = k, Insert = k, Detail = "Schl\u00FCsselwort", Kind = "k" });
            foreach (string g in ApiCatalog.Globals(lang)) r.Items.Add(new Completion { Label = g, Insert = g, Detail = g == "peach" ? "Peach-API" : "global", Kind = g == "peach" ? "m" : "f" });
            foreach (KeyValuePair<string, string> kv in Aliases(text, lang)) r.Items.Add(new Completion { Label = kv.Key, Insert = kv.Key, Detail = "Modul " + kv.Value, Kind = "m" });
            if (lang == "py") foreach (string n in FromNames(text)) r.Items.Add(new Completion { Label = n, Insert = n, Detail = "importiert", Kind = "f" });
            if (ps)
            {
                foreach (ApiItem it in ApiCatalog.PeachItems("ps1")) r.Items.Add(new Completion { Label = it.Name, Insert = it.Name, Detail = it.Doc, Kind = "f" });
                if (prefix.Length >= 2) lock (cat.PsCommands) foreach (string c in cat.PsCommands) if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) r.Items.Add(new Completion { Label = c, Insert = c, Detail = "PowerShell-Befehl", Kind = "f" });
            }
            return Sort(r, prefix);
        }

        // peach.xxx / peach.storage.xxx : members of the chain from the Peach API table
        static void AddPeachMembers(CompletionResult r, string chain, string lang)
        {
            if (!(chain == "peach" || chain.StartsWith("peach."))) return;
            HashSet<string> seen = new HashSet<string>();
            foreach (ApiItem it in ApiCatalog.PeachItems(lang))
            {
                if (!it.Name.StartsWith(chain + ".", StringComparison.Ordinal)) continue;
                string rest = it.Name.Substring(chain.Length + 1);
                int dot = rest.IndexOf('.');
                string name = dot < 0 ? rest : rest.Substring(0, dot);
                if (!seen.Add(name)) continue;
                if (dot < 0) r.Items.Add(new Completion { Label = name, Insert = name, Detail = it.Signature + (it.Permission.Length > 0 ? "   [Recht: " + it.Permission + "]" : ""), Kind = it.Kind });
                else r.Items.Add(new Completion { Label = name, Insert = name, Detail = "Peach-Gruppe", Kind = "m" });
            }
        }

        static CompletionResult Sort(CompletionResult r, string prefix)
        {
            List<Completion> keep = new List<Completion>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Completion c in r.Items)
            {
                if (prefix.Length > 0 && c.Label.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (seen.Add(c.Label)) keep.Add(c);
            }
            keep.Sort(delegate (Completion a, Completion b)
            {
                bool pa = a.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), pb = b.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                if (pa != pb) return pa ? -1 : 1;
                int len = a.Label.Length.CompareTo(b.Label.Length);
                return len != 0 ? len : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            if (keep.Count > 80) keep.RemoveRange(80, keep.Count - 80);
            r.Items = keep;
            return r;
        }
    }

    // Which Peach permissions does the code need? (so mod.json can be checked and fixed)
    internal static class PermissionLint
    {
        static readonly Dictionary<string, string[]> Patterns = new Dictionary<string, string[]>
        {
            { "js", new string[] { @"\bpeach\.notify\b|notify", @"\bpeach\.clipboard\b|on\(\s*['""]clipboard['""]", @"\bpeach\.windows\b|on\(\s*['""]focus['""]", @"\bpeach\.registry\b", @"\bpeach\.wallpaper\b", @"\bpeach\.shell\b", @"\bpeach\.hotkeys?\b" } },
            { "py", new string[] { @"\bpeach\.notify\b", @"\bpeach\.clipboard_(?:get|set)\b|\.on\(\s*['""]clipboard['""]|\bon\(\s*['""]clipboard['""]", @"\bpeach\.windows_list\b|\bon\(\s*['""]focus['""]", @"\bpeach\.registry_get\b", @"\bpeach\.wallpaper_set\b", @"\bpeach\.shell_(?:run|open)\b", @"\bpeach\.hotkey(?:_unregister)?\b" } },
            { "ps1", new string[] { @"\bPeach-Notify\b", @"\bPeach-Clipboard(?:Get|Set)\b|Peach-On\s+['""]clipboard", @"\bPeach-WindowsList\b|Peach-On\s+['""]focus", @"\bPeach-RegistryGet\b", @"\bPeach-WallpaperSet\b", @"\bPeach-Shell(?:Run|Open)\b", @"\bPeach-Hotkey\b" } },
            { "bat", new string[] { @"PEACH_API%?\s*/\s*notify|/notify\b", @"/clipboard\.", @"/windows\.", @"/registry\.", @"/wallpaper\.", @"/shell\.", @"/hotkey\." } },
        };
        static readonly string[] Perms = { "notify", "clipboard", "windows", "registry", "wallpaper", "shell", "hotkeys" };

        public static List<string> Required(string code, string lang)
        {
            lang = Completer.NormLang(lang);
            if (lang == "vbs") lang = "bat";
            List<string> need = new List<string>();
            string[] pats;
            if (!Patterns.TryGetValue(lang, out pats)) return need;
            string clean = StripComments(code, lang == "bat" ? "txt" : lang);
            for (int i = 0; i < pats.Length; i++)
            {
                string pat = pats[i];
                if (lang == "js" && i == 0) pat = @"\bpeach\.notify\b";   // (the generic word "notify" must not count)
                if (Regex.IsMatch(clean, pat)) need.Add(Perms[i]);
            }
            return need;
        }

        static string StripComments(string code, string lang)
        {
            if (lang == "txt") return code;
            char[] chars = code.ToCharArray();
            foreach (Span s in CodeSyntax.Spans(code, lang))
                if (s.Kind == Tok.Comment) for (int i = s.Start; i < s.Start + s.Length && i < chars.Length; i++) if (chars[i] != '\n') chars[i] = ' ';
            return new string(chars);
        }

        public static List<string> Missing(string code, string lang, IList<string> declared)
        {
            List<string> m = new List<string>();
            foreach (string p in Required(code, lang)) if (!declared.Contains(p)) m.Add(p);
            return m;
        }
    }

    // "Is my code even valid?" - asks the real interpreters (node --check, Python ast, PowerShell parser).
    internal static class SyntaxCheck
    {
        // Returns null when fine, a short message when broken. skipped = no checker for this language / runtime missing.
        public static string Check(string lang, string file, out bool skipped)
        {
            skipped = false;
            try
            {
                switch (lang)
                {
                    case "json":
                        try { new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file, Encoding.UTF8)); return null; }
                        catch (Exception e) { return e.Message; }
                    case "js": return NodeError(file, out skipped);
                    case "py":
                        {
                            string tmp = Path.Combine(Path.GetTempPath(), "peach-pycheck-" + Guid.NewGuid().ToString("N") + ".py");
                            File.WriteAllText(tmp, "import ast, sys\ntry:\n    ast.parse(open(sys.argv[1], encoding='utf-8-sig').read(), sys.argv[1])\n    print('OK')\nexcept SyntaxError as e:\n    print('ERR Zeile %s: %s' % (e.lineno, e.msg))\n");
                            try
                            {
                                string o = ApiCatalog.Run(new string[] { "python", "py" }, "-I \"" + tmp + "\" \"" + file + "\"", 15000);
                                if (o == null) { skipped = true; return null; }
                                o = o.Trim();
                                return o.StartsWith("ERR ") ? o.Substring(4) : null;
                            }
                            finally { try { File.Delete(tmp); } catch { } }
                        }
                    case "ps1":
                        {
                            string ps = "$e=$null;$t=$null;[void][System.Management.Automation.Language.Parser]::ParseFile($env:PEACH_CHECK_FILE,[ref]$t,[ref]$e);if($e -and $e.Count){($e|Select-Object -First 1|ForEach-Object{'ERR Zeile '+$_.Extent.StartLineNumber+': '+$_.Message})}else{'OK'}";
                            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(ps)));
                            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.UTF8;
                            psi.EnvironmentVariables["PEACH_CHECK_FILE"] = file;
                            using (Process p = Process.Start(psi))
                            {
                                string o = p.StandardOutput.ReadToEnd();
                                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } skipped = true; return null; }
                                o = o.Trim();
                                return o.StartsWith("ERR ") ? o.Substring(4) : null;
                            }
                        }
                }
            }
            catch { skipped = true; return null; }
            skipped = true;
            return null;
        }

        // node --check prints nothing on success (which Run() reports as null), so ask again with the error output.
        static string NodeError(string file, out bool skipped)
        {
            skipped = false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("node", "--check \"" + file + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } skipped = true; return null; }
                    if (p.ExitCode == 0) return null;
                    string[] lines = err.Replace("\r", "").Split('\n');
                    string line = "", msg = "";
                    foreach (string l in lines)
                    {
                        Match m = Regex.Match(l, @":(\d+)\s*$");
                        if (line.Length == 0 && m.Success) line = m.Groups[1].Value;
                        if (l.StartsWith("SyntaxError:")) { msg = l.Substring(12).Trim(); break; }
                    }
                    return (line.Length > 0 ? "Zeile " + line + ": " : "") + (msg.Length > 0 ? msg : "Syntaxfehler");
                }
            }
            catch { skipped = true; return null; }
        }
    }
}
