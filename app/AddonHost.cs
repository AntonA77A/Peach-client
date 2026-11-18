using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Peach
{
    internal class AddonLog { public long T; public string S = "", M = ""; }

    internal class AddonInfo
    {
        public string Id = "", Name = "", Version = "", Description = "", Author = "", Language = "", Entry = "", Status = "stopped";
        public bool Enabled;
        public int Pid;
        public List<string> Permissions = new List<string>(), Unknown = new List<string>();
        public List<AddonLog> Log = new List<AddonLog>();
    }

    // Talks to the script-addon host (Node.js, bin\peach.js) over its local HTTP API and, if needed, starts it.
    internal class AddonHost : IDisposable
    {
        public readonly string Root;
        public readonly string NodePath;
        public readonly bool ScriptFound;
        Process proc;
        volatile int port;
        string token;
        public volatile bool Connected;
        public string Error = "";
        public volatile List<AddonInfo> Addons = new List<AddonInfo>();
        public string HostVersion = "";

        public AddonHost(string root)
        {
            Root = root;
            NodePath = FindOnPath("node.exe");
            ScriptFound = File.Exists(Path.Combine(root, "bin", "peach.js"));
        }

        public string ModsDir { get { return Path.Combine(Root, "mods"); } }
        public bool CanRun { get { return NodePath != null && ScriptFound; } }
        public bool Starting { get { return proc != null && !proc.HasExited && !Connected; } }
        public string DashboardUrl { get { return port > 0 ? "http://127.0.0.1:" + port + "/" : "http://127.0.0.1:47800/"; } }

        static string FindOnPath(string exe)
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { string f = Path.Combine(dir.Trim('"'), exe); if (File.Exists(f)) return f; } catch { }
            }
            return null;
        }

        // Starts the host hidden (no browser). Safe to call repeatedly.
        public void StartHost()
        {
            if (!CanRun || (proc != null && !proc.HasExited)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(NodePath, "\"" + Path.Combine(Root, "bin", "peach.js") + "\" start");
                psi.WorkingDirectory = Root; psi.UseShellExecute = false; psi.CreateNoWindow = true;
                proc = Process.Start(psi);
            }
            catch (Exception e) { Error = e.Message; }
        }

        string Http(string method, string url, int timeoutMs, bool auth)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method; req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs; req.Proxy = null;
            if (auth) req.Headers["x-peach-ui"] = token;
            if (method == "POST") req.ContentLength = 0;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // Finds a running host (47800 and the next ports it may fall back to) and reads its UI token.
        bool Probe()
        {
            for (int p = 47800; p < 47820; p++)
            {
                try
                {
                    string html = Http("GET", "http://127.0.0.1:" + p + "/", 350, false);
                    Match m = Regex.Match(html, "const TOKEN = \"([0-9a-f]+)\"");
                    if (m.Success && html.Contains("Peach Client")) { port = p; token = m.Groups[1].Value; return true; }
                }
                catch { /* nothing on this port */ }
            }
            return false;
        }

        // Blocking: call from a worker thread. Returns true when the snapshot is fresh.
        public bool Refresh() { lock (refreshGate) return RefreshCore(); }

        readonly object refreshGate = new object();

        bool RefreshCore()
        {
            try
            {
                if (port == 0 && !Probe()) { Connected = false; return false; }
                string json;
                try { json = Http("GET", "http://127.0.0.1:" + port + "/ui/state", 8000, true); }
                catch (WebException) { port = 0; token = null; if (!Probe()) { Connected = false; return false; } json = Http("GET", "http://127.0.0.1:" + port + "/ui/state", 8000, true); }
                string version;
                Addons = ParseState(json, out version);
                HostVersion = version;
                Connected = true;
                Error = "";
                return true;
            }
            catch (Exception e) { Connected = false; port = 0; token = null; Error = e.Message; return false; }
        }

        public static List<AddonInfo> ParseState(string json, out string version)
        {
            version = "";
            List<AddonInfo> list = new List<AddonInfo>();
            IDictionary root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as IDictionary;
            if (root == null) return list;
            if (root["version"] != null) version = root["version"].ToString();
            IEnumerable mods = root["mods"] as IEnumerable;
            if (mods == null) return list;
            foreach (object o in mods)
            {
                IDictionary d = o as IDictionary;
                if (d == null) continue;
                AddonInfo a = new AddonInfo();
                a.Id = S(d, "id"); a.Name = S(d, "name"); a.Version = S(d, "version"); a.Description = S(d, "description"); a.Author = S(d, "author");
                a.Language = S(d, "language"); a.Entry = S(d, "entry"); a.Status = S(d, "status");
                a.Enabled = d["enabled"] is bool && (bool)d["enabled"];
                if (d["pid"] is int) a.Pid = (int)d["pid"];
                foreach (string p in Strings(d["permissions"])) a.Permissions.Add(p);
                foreach (string p in Strings(d["unknownPermissions"])) a.Unknown.Add(p);
                IEnumerable log = d["log"] as IEnumerable;
                if (log != null)
                    foreach (object lo in log)
                    {
                        IDictionary ld = lo as IDictionary;
                        if (ld == null) continue;
                        AddonLog l = new AddonLog { S = S(ld, "s"), M = S(ld, "m") };
                        if (ld["t"] is long) l.T = (long)ld["t"]; else if (ld["t"] is int) l.T = (int)ld["t"]; else if (ld["t"] is double) l.T = (long)(double)ld["t"];
                        a.Log.Add(l);
                    }
                list.Add(a);
            }
            return list;
        }

        static string S(IDictionary d, string k) { object v = d[k]; return v == null ? "" : v.ToString(); }
        static IEnumerable<string> Strings(object o)
        {
            IEnumerable e = o as IEnumerable;
            if (e == null || o is string) yield break;
            foreach (object x in e) if (x != null) yield return x.ToString();
        }

        bool Post(string path)
        {
            try
            {
                if (port == 0 && !Probe()) return false;
                Http("POST", "http://127.0.0.1:" + port + path, 4000, true);
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        public bool Start(string id) { return Post("/ui/start/" + id); }
        public bool Stop(string id) { return Post("/ui/stop/" + id); }
        public bool Rescan() { return Post("/ui/rescan"); }

        public void Dispose()
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited)
                {
                    ProcessStartInfo kill = new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F");
                    kill.CreateNoWindow = true; kill.UseShellExecute = false;
                    Process.Start(kill).WaitForExit(3000);   // also ends the addons the host started
                }
            }
            catch { }
            proc = null;
        }
    }

    // Creating, installing and removing addon folders (mods\<id>\mod.json + entry file).
    internal static class AddonFiles
    {
        public static readonly string[] Languages = { "js", "ts", "py", "ps1", "bat", "vbs" };
        public static readonly string[] LanguageNames = { "JavaScript", "TypeScript", "Python", "PowerShell", "Batch", "VBScript" };
        public static readonly string[] AllPermissions = { "notify", "clipboard", "windows", "hotkeys", "registry", "wallpaper", "shell" };

        public static string Slug(string name)
        {
            string s = Regex.Replace((name ?? "").Trim().ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss"), "[^a-z0-9]+", "-").Trim('-');
            return s.Length == 0 ? "addon" : (s.Length > 40 ? s.Substring(0, 40).Trim('-') : s);
        }

        static string UniqueDir(string modsDir, string id)
        {
            string dir = Path.Combine(modsDir, id);
            for (int n = 2; Directory.Exists(dir) || File.Exists(dir); n++) dir = Path.Combine(modsDir, id + "-" + n);
            return dir;
        }

        static string JsonString(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        public static string EntryFile(string lang) { return "main." + lang; }

        public static string TemplateBody(string lang, string name, bool notify)
        {
            string n = (name ?? "").Replace("'", "").Replace("\"", "");
            switch (lang)
            {
                case "js":
                    return "const peach = require('peach');\r\n\r\n(async () => {\r\n  await peach.log('Hello from JavaScript!');\r\n" + (notify ? "  await peach.notify('" + n + "', 'JavaScript addon is running');\r\n" : "") + "})();\r\n";
                case "ts":
                    return "// Needs Node >= 23.6 (runs TypeScript natively)\r\nconst peach = require('peach') as any;\r\n\r\n(async (): Promise<void> => {\r\n  await peach.log('Hello from TypeScript!');\r\n" + (notify ? "  await peach.notify('" + n + "', 'TypeScript addon is running');\r\n" : "") + "})();\r\n";
                case "py":
                    return "import peach\r\n\r\npeach.log(\"Hello from Python!\")\r\n" + (notify ? "peach.notify(\"" + n + "\", \"Python addon is running\")\r\n" : "");
                case "ps1":
                    return ". \"$env:PEACH_SDK\\peach.ps1\"\r\n\r\nPeach-Log \"Hello from PowerShell!\"\r\n" + (notify ? "Peach-Notify \"" + n + "\" \"PowerShell addon is running\"\r\n" : "");
                case "bat":
                    return "@echo off\r\nrem Any language can talk to Peach over plain HTTP. curl.exe ships with Windows 10+.\r\ncurl.exe -s -X POST \"%PEACH_API%/log\" -H \"Authorization: Bearer %PEACH_TOKEN%\" -d \"{\\\"message\\\":\\\"Hello from Batch!\\\"}\"\r\necho.\r\n";
                case "vbs":
                    return "Set env = CreateObject(\"WScript.Shell\").Environment(\"PROCESS\")\r\nSet http = CreateObject(\"MSXML2.ServerXMLHTTP.6.0\")\r\nhttp.Open \"POST\", env(\"PEACH_API\") & \"/log\", False\r\nhttp.SetRequestHeader \"Authorization\", \"Bearer \" & env(\"PEACH_TOKEN\")\r\nhttp.Send \"{\"\"message\"\":\"\"Hello from VBScript!\"\"}\"\r\nWScript.Echo http.ResponseText\r\n";
            }
            return "";
        }

        public static string ModJson(string name, string description, string entry, IList<string> perms)
        {
            StringBuilder p = new StringBuilder();
            for (int i = 0; i < perms.Count; i++) p.Append(i > 0 ? ", " : "").Append(JsonString(perms[i]));
            return "{\r\n  \"name\": " + JsonString(name) + ",\r\n  \"version\": \"0.1.0\",\r\n  \"description\": " + JsonString(description) +
                   ",\r\n  \"entry\": " + JsonString(entry) + ",\r\n  \"permissions\": [" + p + "],\r\n  \"autostart\": true\r\n}\r\n";
        }

        // Creates mods\<slug>\ with mod.json and a starter script. Returns the new id, or null + error.
        public static string Scaffold(string modsDir, string name, string description, string lang, IList<string> perms, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) { error = "Bitte einen Namen eingeben."; return null; }
            if (Array.IndexOf(Languages, lang) < 0) { error = "Unbekannte Sprache."; return null; }
            try
            {
                Directory.CreateDirectory(modsDir);
                string dir = UniqueDir(modsDir, Slug(name));
                Directory.CreateDirectory(dir);
                string entry = EntryFile(lang);
                File.WriteAllText(Path.Combine(dir, "mod.json"), ModJson(name.Trim(), description ?? "", entry, perms), new UTF8Encoding(false));
                // Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI, which garbles umlauts: write PowerShell with BOM
                File.WriteAllText(Path.Combine(dir, entry), TemplateBody(lang, name.Trim(), perms.Contains("notify")), new UTF8Encoding(lang == "ps1"));
                return Path.GetFileName(dir);
            }
            catch (Exception e) { error = e.Message; return null; }
        }

        // null = fine, otherwise a German explanation
        public static string ValidateModFolder(string dir)
        {
            string file = Path.Combine(dir, "mod.json");
            if (!File.Exists(file)) return "In diesem Ordner fehlt die Datei mod.json.";
            try
            {
                IDictionary d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as IDictionary;
                string entry = d == null ? null : d["entry"] as string;
                if (string.IsNullOrEmpty(entry)) return "In mod.json fehlt „entry“ (die Startdatei).";
                string full = Path.GetFullPath(Path.Combine(dir, entry));
                string root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "„entry“ muss im Addon-Ordner liegen.";
                if (!File.Exists(full)) return "Die Startdatei „" + entry + "“ wurde nicht gefunden.";
            }
            catch (Exception e) { return "mod.json ist ungültig: " + e.Message; }
            return null;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), false);
            foreach (string d in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(d);
                if (name == ".git" || name == "node_modules") continue;
                CopyDir(d, Path.Combine(dst, name));
            }
        }

        public static string InstallFolder(string src, string modsDir, out string error)
        {
            error = ValidateModFolder(src);
            if (error != null) return null;
            try
            {
                Directory.CreateDirectory(modsDir);
                string full = Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar);
                string target = UniqueDir(modsDir, Slug(Path.GetFileName(full)));
                CopyDir(full, target);
                return Path.GetFileName(target);
            }
            catch (Exception e) { error = e.Message; return null; }
        }

        // .zip with mod.json at the top or inside one top-level folder. Entries that try to leave the folder are refused.
        public static string InstallZip(string zip, string modsDir, out string error)
        {
            error = null;
            string temp = Path.Combine(Path.GetTempPath(), "peach-addon-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                string tempRoot = Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                using (ZipArchive za = ZipFile.OpenRead(zip))
                {
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        string dest = Path.GetFullPath(Path.Combine(temp, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!dest.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) { error = "Die ZIP-Datei enthält unsichere Pfade und wurde nicht installiert."; return null; }
                        if (e.FullName.EndsWith("/") || e.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                }
                string modRoot = null;
                if (File.Exists(Path.Combine(temp, "mod.json"))) modRoot = temp;
                else
                {
                    string[] dirs = Directory.GetDirectories(temp);
                    if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], "mod.json"))) modRoot = dirs[0];
                }
                if (modRoot == null) { error = "In der ZIP-Datei wurde keine mod.json gefunden."; return null; }
                string name = modRoot == temp ? Path.GetFileNameWithoutExtension(zip) : Path.GetFileName(modRoot);
                string verr = ValidateModFolder(modRoot);
                if (verr != null) { error = verr; return null; }
                Directory.CreateDirectory(modsDir);
                string target = UniqueDir(modsDir, Slug(name));
                CopyDir(modRoot, target);
                return Path.GetFileName(target);
            }
            catch (Exception e) { error = e.Message; return null; }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        // Only folders directly inside mods\ can be removed.
        public static bool Remove(string modsDir, string id, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, @"^[\w.-]+$") || id == "." || id == "..") { error = "Ungültige Addon-ID."; return false; }
            string root = Path.GetFullPath(modsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string dir = Path.GetFullPath(Path.Combine(modsDir, id));
            if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar != root)
            { error = "Der Ordner liegt nicht direkt im Addon-Ordner."; return false; }
            if (!Directory.Exists(dir)) { error = "Der Ordner existiert nicht mehr."; return false; }
            try { Directory.Delete(dir, true); return true; }
            catch (Exception e) { error = e.Message; return false; }
        }
    }
}
