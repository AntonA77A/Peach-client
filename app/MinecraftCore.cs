using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Peach
{
    internal class ModHit { public string Id, Slug, Title, Description, Author; public long Downloads; }
    internal class ModFile { public string ProjectId, VersionId, VersionName, Url, FileName, Sha512; public List<string> RequiredProjects = new List<string>(); }
    internal class ModEntry { public string FileName; public bool Enabled; }

    // Network access with a polite User-Agent. All results are parsed by pure functions (tested offline).
    internal static class Web
    {
        public static string Get(string url, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;     // TLS 1.2
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "PeachClient/0.1 (Minecraft profile + mod manager)";
            r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs;
            using (WebResponse resp = r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }

        public static byte[] Download(string url, int timeoutMs, long maxBytes)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "PeachClient/0.1 (Minecraft profile + mod manager)";
            r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs;
            using (WebResponse resp = r.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[81920]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > maxBytes) throw new IOException("Die Datei ist größer als erlaubt.");
                }
                return ms.ToArray();
            }
        }

        // POST / GET with an optional body; also returns the body of HTTP error answers (the login services explain themselves there)
        public static string Send(string method, string url, string body, string contentType, Dictionary<string, string> headers, int timeoutMs, out int status)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = method; r.UserAgent = "PeachClient/0.1"; r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs; r.Accept = "application/json";
            if (headers != null) foreach (KeyValuePair<string, string> h in headers) r.Headers[h.Key] = h.Value;
            if (body != null)
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                r.ContentType = contentType; r.ContentLength = b.Length;
                using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) { status = (int)resp.StatusCode; return sr.ReadToEnd(); }
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp == null) throw;
                using (resp) using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) { status = (int)resp.StatusCode; return sr.ReadToEnd(); }
            }
        }

        public static JavaScriptSerializer Json() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }
    }

    internal static class Modrinth
    {
        const string Api = "https://api.modrinth.com/v2";

        public static string SearchUrl(string query, string mcVersion, string loader, int limit)
        {
            List<string> facets = new List<string> { "[\"project_type:mod\"]" };
            if (!string.IsNullOrEmpty(mcVersion)) facets.Add("[\"versions:" + mcVersion + "\"]");
            if (!string.IsNullOrEmpty(loader) && loader != "vanilla") facets.Add("[\"categories:" + loader + "\"]");
            return Api + "/search?query=" + Uri.EscapeDataString(query ?? "") + "&limit=" + limit + "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets.ToArray()) + "]");
        }

        public static string VersionsUrl(string projectId, string mcVersion, string loader)
        {
            string u = Api + "/project/" + Uri.EscapeDataString(projectId) + "/version?game_versions=" + Uri.EscapeDataString("[\"" + mcVersion + "\"]");
            if (!string.IsNullOrEmpty(loader) && loader != "vanilla") u += "&loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]");
            return u;
        }

        public static List<ModHit> ParseSearch(string json)
        {
            List<ModHit> l = new List<ModHit>();
            try
            {
                Dictionary<string, object> root = Web.Json().Deserialize<Dictionary<string, object>>(json);
                object[] hits = root != null && root.ContainsKey("hits") ? ToArray(root["hits"]) : null;
                if (hits == null) return l;
                foreach (object o in hits)
                {
                    Dictionary<string, object> h = o as Dictionary<string, object>;
                    if (h == null) continue;
                    ModHit m = new ModHit { Id = Str(h, "project_id"), Slug = Str(h, "slug"), Title = Str(h, "title"), Description = Str(h, "description"), Author = Str(h, "author") };
                    try { if (h.ContainsKey("downloads")) m.Downloads = Convert.ToInt64(h["downloads"]); } catch { }
                    if (m.Id != null && m.Title != null) l.Add(m);
                }
            }
            catch { }
            return l;
        }

        // newest release first (Modrinth already sorts by date); returns the primary file of the first usable version
        public static ModFile ParseBestVersion(string json, string projectId)
        {
            try
            {
                object[] versions = ToArray(Web.Json().DeserializeObject(json));
                if (versions == null) return null;
                ModFile fallback = null;
                foreach (object o in versions)
                {
                    Dictionary<string, object> v = o as Dictionary<string, object>;
                    if (v == null || !v.ContainsKey("files")) continue;
                    ModFile mf = ToModFile(v, projectId);
                    if (mf == null) continue;
                    if (Str(v, "version_type") == "release") return mf;
                    if (fallback == null) fallback = mf;
                }
                return fallback;
            }
            catch { return null; }
        }

        static ModFile ToModFile(Dictionary<string, object> v, string projectId)
        {
            object[] files = ToArray(v["files"]);
            if (files == null || files.Length == 0) return null;
            Dictionary<string, object> pick = null;
            foreach (object f in files) { Dictionary<string, object> d = f as Dictionary<string, object>; if (d == null) continue; if (pick == null) pick = d; if (d.ContainsKey("primary") && d["primary"] is bool && (bool)d["primary"]) { pick = d; break; } }
            if (pick == null) return null;
            ModFile m = new ModFile { ProjectId = projectId, VersionId = Str(v, "id"), VersionName = Str(v, "version_number"), Url = Str(pick, "url"), FileName = Str(pick, "filename") };
            Dictionary<string, object> hashes = pick.ContainsKey("hashes") ? pick["hashes"] as Dictionary<string, object> : null;
            if (hashes != null) m.Sha512 = Str(hashes, "sha512");
            if (v.ContainsKey("dependencies"))
            {
                object[] deps = ToArray(v["dependencies"]);
                if (deps != null) foreach (object o in deps)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d != null && Str(d, "dependency_type") == "required" && Str(d, "project_id") != null) m.RequiredProjects.Add(Str(d, "project_id"));
                    }
            }
            return m;
        }

        internal static object[] ToArray(object o)
        {
            if (o is object[]) return (object[])o;
            if (o == null || o is string || o is Dictionary<string, object>) return null;
            System.Collections.IEnumerable en = o as System.Collections.IEnumerable;
            if (en == null) return null;
            List<object> l = new List<object>();
            foreach (object x in en) l.Add(x);
            return l.ToArray();
        }

        static string Str(Dictionary<string, object> d, string key) { return d.ContainsKey(key) && d[key] != null ? d[key].ToString() : null; }

        // Only files from Modrinth's own CDN are downloaded; the name must be a plain *.jar
        public static bool IsSafeSource(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && (u.Host == "cdn.modrinth.com");
        }

        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = Path.GetFileName(name.Replace('/', '\\'));
            if (n != name || !n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || n.Length > 120) return null;
            if (!Regex.IsMatch(n, @"^[A-Za-z0-9_.+\-() ]+$") || n.Contains("..")) return null;
            return n;
        }

        public static bool HashMatches(byte[] data, string sha512Hex)
        {
            if (string.IsNullOrEmpty(sha512Hex)) return false;
            using (SHA512 s = SHA512.Create())
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in s.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return string.Equals(sb.ToString(), sha512Hex, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Installs the mod and everything it requires into modsDir. Returns the file names (new ones only). Throws a readable error.
        public static List<string> Install(string projectId, string mcVersion, string loader, string modsDir, Action<string> progress)
        {
            List<string> installed = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            Directory.CreateDirectory(modsDir);
            InstallOne(projectId, mcVersion, loader, modsDir, progress, installed, seen, 0);
            return installed;
        }

        static void InstallOne(string projectId, string mc, string loader, string dir, Action<string> progress, List<string> installed, HashSet<string> seen, int depth)
        {
            if (depth > 4 || !seen.Add(projectId)) return;
            if (progress != null) progress("Suche passende Version …");
            ModFile f = ParseBestVersion(Web.Get(VersionsUrl(projectId, mc, loader), 20000), projectId);
            if (f == null) { if (depth == 0) throw new InvalidOperationException("Für Minecraft " + mc + " (" + loader + ") gibt es keine passende Version dieser Mod."); return; }
            string name = SafeFileName(f.FileName);
            if (name == null || !IsSafeSource(f.Url) || string.IsNullOrEmpty(f.Sha512)) throw new InvalidOperationException("Die Mod-Datei sieht nicht vertrauenswürdig aus und wurde nicht geladen.");
            string target = Path.Combine(dir, name);
            if (!File.Exists(target) && !File.Exists(target + ".disabled"))
            {
                if (progress != null) progress("Lade " + name + " …");
                byte[] data = Web.Download(f.Url, 60000, 200L * 1024 * 1024);
                if (!HashMatches(data, f.Sha512)) throw new InvalidOperationException("Die heruntergeladene Datei stimmt nicht mit der Prüfsumme überein und wurde verworfen.");
                File.WriteAllBytes(target, data);
                installed.Add(name);
            }
            foreach (string dep in f.RequiredProjects) InstallOne(dep, mc, loader, dir, progress, installed, seen, depth + 1);
        }
    }

    internal static class ModFolder
    {
        public static List<ModEntry> List(string dir)
        {
            List<ModEntry> l = new List<ModEntry>();
            if (!Directory.Exists(dir)) return l;
            foreach (string f in Directory.GetFiles(dir))
            {
                string n = Path.GetFileName(f);
                if (n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) l.Add(new ModEntry { FileName = n, Enabled = true });
                else if (n.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) l.Add(new ModEntry { FileName = n.Substring(0, n.Length - 9), Enabled = false });
            }
            l.Sort(delegate (ModEntry a, ModEntry b) { return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase); });
            return l;
        }

        static string Resolve(string dir, string name, bool enabled)
        {
            string safe = Modrinth.SafeFileName(name);
            if (safe == null) return null;
            return Path.Combine(dir, enabled ? safe : safe + ".disabled");
        }

        public static bool SetEnabled(string dir, string name, bool on)
        {
            string from = Resolve(dir, name, !on), to = Resolve(dir, name, on);
            if (from == null || to == null || !File.Exists(from) || File.Exists(to)) return false;
            File.Move(from, to);
            return true;
        }

        public static bool Delete(string dir, string name)
        {
            foreach (bool e in new bool[] { true, false })
            {
                string p = Resolve(dir, name, e);
                if (p != null && File.Exists(p)) { File.Delete(p); return true; }
            }
            return false;
        }
    }
}
