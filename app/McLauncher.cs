using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Peach
{
    internal class McLib
    {
        public string Name, Path, Url, Sha1;
        public long Size;
        public bool IsNative;
        public List<string> Excludes = new List<string>();
    }

    // Everything needed to start one version.
    internal class McPlan
    {
        public Dictionary<string, object> Merged;
        public string VersionId, ClientJarId, ClientJar, AssetsRoot, AssetIndexId, NativesDir, MainClass;
        public int JavaMajor = 8;
        public List<McLib> Libs = new List<McLib>();
        public List<string> ClassPath = new List<string>();
    }

    internal class McContext
    {
        public string PlayerName = "Player", Uuid = "00000000-0000-0000-0000-000000000000", AccessToken = "0", UserType = "msa";
        public string GameDir, LauncherVersion = "0.1";
        public bool Demo;
        public int RamMb = 4096;
    }

    // Own launcher: version files, libraries, assets, natives, Java, arguments and the start itself.
    internal static class Minecraft
    {
        static readonly string[] hosts = { "piston-meta.mojang.com", "piston-data.mojang.com", "launchermeta.mojang.com", "launcher.mojang.com", "libraries.minecraft.net", "resources.download.minecraft.net", "maven.fabricmc.net", "meta.fabricmc.net" };

        // ---- folders ----
        public static string ProfilesRoot(Config cfg) { return Path.Combine(cfg.ConfigDir(), "minecraft"); }
        public static string SharedDir(Config cfg) { return Path.Combine(ProfilesRoot(cfg), "shared"); }
        public static string GameDir(Config cfg, McProfile p) { return Path.Combine(ProfilesRoot(cfg), p.Id); }
        public static string ModsDir(Config cfg, McProfile p) { return Path.Combine(GameDir(cfg, p), "mods"); }
        public static string VersionsDir(string shared) { return Path.Combine(shared, "versions"); }

        public static bool ValidVersion(string v) { return v != null && Regex.IsMatch(v, @"^[0-9]{1,2}(\.[0-9]{1,2}){1,2}$"); }

        public static string MakeId(string name, IEnumerable<McProfile> existing)
        {
            string b = Regex.Replace((name ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            if (b.Length == 0) b = "profil";
            if (b.Length > 40) b = b.Substring(0, 40).Trim('-');
            string id = b; int n = 2;
            while (new List<McProfile>(existing).Exists(delegate (McProfile p) { return p.Id == id; })) id = b + "-" + n++;
            return id;
        }

        public static McProfile Find(Config cfg, string id)
        {
            foreach (McProfile p in cfg.Minecraft.Profiles) if (p.Id == id) return p;
            return null;
        }

        public static bool IsAllowedHost(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && Array.IndexOf(hosts, u.Host) >= 0;
        }

        // ---- small JSON helpers ----
        static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        static string S(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) && d[k] != null ? d[k].ToString() : null; }
        static object G(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) ? d[k] : null; }
        static Dictionary<string, object> ParseJson(string t) { return Web.Json().Deserialize<Dictionary<string, object>>(t); }
        static long L(Dictionary<string, object> d, string k) { try { return d != null && d.ContainsKey(k) ? Convert.ToInt64(d[k]) : 0; } catch { return 0; } }

        // ---- Fabric / manifest answers (pure, tested) ----
        public static string ParseFabricLoader(string json)
        {
            try
            {
                object[] l = Modrinth.ToArray(Web.Json().DeserializeObject(json));
                if (l == null) return null;
                string first = null;
                foreach (object o in l)
                {
                    Dictionary<string, object> d = D(o); Dictionary<string, object> loader = D(G(d, "loader"));
                    if (loader == null || !loader.ContainsKey("version")) continue;
                    string v = loader["version"].ToString();
                    if (first == null) first = v;
                    if (loader.ContainsKey("stable") && loader["stable"] is bool && (bool)loader["stable"]) return v;
                }
                return first;
            }
            catch { return null; }
        }

        public static List<string> ParseReleases(string manifestJson, int max)
        {
            List<string> l = new List<string>();
            try
            {
                foreach (object o in Modrinth.ToArray(ParseJson(manifestJson)["versions"]))
                {
                    Dictionary<string, object> d = D(o);
                    if (d != null && S(d, "type") == "release") { l.Add(S(d, "id")); if (l.Count >= max) break; }
                }
            }
            catch { }
            return l;
        }

        // finds a version in Mojang's manifest: url + sha1 of its description file
        public static bool FindInManifest(string manifestJson, string id, out string url, out string sha1)
        {
            url = null; sha1 = null;
            try
            {
                foreach (object o in Modrinth.ToArray(ParseJson(manifestJson)["versions"]))
                {
                    Dictionary<string, object> d = D(o);
                    if (S(d, "id") == id) { url = S(d, "url"); sha1 = S(d, "sha1"); return url != null; }
                }
            }
            catch { }
            return false;
        }

        // ---- version description files ----
        // "net.fabricmc:fabric-loader:0.16.9" -> "net/fabricmc/fabric-loader/0.16.9/fabric-loader-0.16.9.jar"
        public static string MavenPath(string name)
        {
            string[] p = (name ?? "").Split(':');
            if (p.Length < 3 || p.Length > 4) return null;
            foreach (string s in p) if (!Regex.IsMatch(s, @"^[A-Za-z0-9_.+\-]+$")) return null;
            string file = p[1] + "-" + p[2] + (p.Length == 4 ? "-" + p[3] : "") + ".jar";
            return p[0].Replace('.', '/') + "/" + p[1] + "/" + p[2] + "/" + file;
        }

        public static Dictionary<string, object> LoadMerged(string versionsDir, string id)
        {
            return LoadMerged(versionsDir, id, 0);
        }

        static Dictionary<string, object> LoadMerged(string versionsDir, string id, int depth)
        {
            if (depth > 3 || !Regex.IsMatch(id ?? "", @"^[A-Za-z0-9_.+\-]+$")) throw new InvalidOperationException("Ungültige Version.");
            string path = Path.Combine(versionsDir, id, id + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException("Versionsdatei fehlt: " + id);
            Dictionary<string, object> d = ParseJson(File.ReadAllText(path, Encoding.UTF8));
            string parent = S(d, "inheritsFrom");
            if (parent == null) return d;
            return Merge(LoadMerged(versionsDir, parent, depth + 1), d);
        }

        public static Dictionary<string, object> Merge(Dictionary<string, object> parent, Dictionary<string, object> child)
        {
            Dictionary<string, object> r = new Dictionary<string, object>(parent);
            foreach (KeyValuePair<string, object> kv in child)
            {
                if (kv.Key == "libraries")
                {
                    List<object> libs = new List<object>();
                    object[] c = Modrinth.ToArray(kv.Value), p = Modrinth.ToArray(G(parent, "libraries"));
                    if (c != null) libs.AddRange(c);
                    if (p != null) libs.AddRange(p);
                    r["libraries"] = libs;
                }
                else if (kv.Key == "arguments")
                {
                    Dictionary<string, object> args = new Dictionary<string, object>();
                    Dictionary<string, object> pa = D(G(parent, "arguments")) ?? new Dictionary<string, object>(), ca = D(kv.Value) ?? new Dictionary<string, object>();
                    foreach (string part in new string[] { "jvm", "game" })
                    {
                        List<object> l = new List<object>();
                        object[] a = Modrinth.ToArray(G(pa, part)), b = Modrinth.ToArray(G(ca, part));
                        if (a != null) l.AddRange(a);
                        if (b != null) l.AddRange(b);
                        args[part] = l;
                    }
                    r["arguments"] = args;
                }
                else r[kv.Key] = kv.Value;
            }
            return r;
        }

        // rules: "allow" / "disallow" by operating system (we are Windows x64) and features
        public static bool RulesAllow(object rules, ICollection<string> features)
        {
            object[] rs = Modrinth.ToArray(rules);
            if (rs == null || rs.Length == 0) return true;
            bool allow = false;
            foreach (object o in rs)
            {
                Dictionary<string, object> r = D(o); if (r == null) continue;
                bool match = true;
                Dictionary<string, object> os = D(G(r, "os"));
                if (os != null)
                {
                    string n = S(os, "name");
                    if (n != null && n != "windows") match = false;
                    if (S(os, "arch") != null) match = false;                     // x86 / arm64 entries are not ours
                    if (S(os, "version") != null && !Regex.IsMatch("10.0", S(os, "version"))) match = false;
                }
                Dictionary<string, object> feat = D(G(r, "features"));
                if (feat != null)
                    foreach (KeyValuePair<string, object> f in feat)
                    {
                        bool wanted = f.Value is bool && (bool)f.Value;
                        if ((features != null && features.Contains(f.Key)) != wanted) match = false;
                    }
                if (match) allow = S(r, "action") == "allow";
            }
            return allow;
        }

        public static List<McLib> ParseLibraries(Dictionary<string, object> merged)
        {
            List<McLib> res = new List<McLib>();
            HashSet<string> seen = new HashSet<string>();
            object[] libs = Modrinth.ToArray(G(merged, "libraries"));
            if (libs == null) return res;
            foreach (object o in libs)
            {
                Dictionary<string, object> lib = D(o); if (lib == null) continue;
                if (!RulesAllow(G(lib, "rules"), null)) continue;
                string name = S(lib, "name");
                Dictionary<string, object> dl = D(G(lib, "downloads"));
                Dictionary<string, object> nativesMap = D(G(lib, "natives"));
                if (nativesMap != null && S(nativesMap, "windows") != null)
                {
                    string key = S(nativesMap, "windows").Replace("${arch}", "64");
                    Dictionary<string, object> art = D(G(D(G(dl, "classifiers")), key));
                    if (art != null)
                    {
                        McLib n = new McLib { Name = name, Path = S(art, "path"), Url = S(art, "url"), Sha1 = S(art, "sha1"), Size = L(art, "size"), IsNative = true };
                        Dictionary<string, object> ex = D(G(lib, "extract"));
                        object[] exl = Modrinth.ToArray(G(ex, "exclude"));
                        if (exl != null) foreach (object e in exl) n.Excludes.Add(e.ToString());
                        res.Add(n);
                    }
                    // the normal artifact of a library with natives (classes) stays on the classpath below
                }
                Dictionary<string, object> artifact = D(G(dl, "artifact"));
                McLib m = null;
                if (artifact != null) m = new McLib { Name = name, Path = S(artifact, "path"), Url = S(artifact, "url"), Sha1 = S(artifact, "sha1"), Size = L(artifact, "size") };
                else if (name != null && S(lib, "url") != null && MavenPath(name) != null)
                {
                    string rel = MavenPath(name), baseUrl = S(lib, "url");
                    m = new McLib { Name = name, Path = rel, Url = baseUrl.TrimEnd('/') + "/" + rel, Sha1 = S(lib, "sha1"), Size = L(lib, "size") };
                }
                if (m == null || m.Path == null) continue;
                string[] parts = (name ?? m.Path).Split(':');
                string dedupe = parts.Length >= 2 ? parts[0] + ":" + parts[1] + (parts.Length > 3 ? ":" + parts[3] : "") : m.Path;
                if (seen.Add(dedupe)) res.Add(m);
            }
            return res;
        }

        // ---- downloads ----
        static void Fetch(string url, string target, string sha1, long size)
        {
            if (File.Exists(target) && (size <= 0 || new FileInfo(target).Length == size)) return;
            if (!IsAllowedHost(url)) throw new InvalidOperationException("Download von einer nicht erlaubten Adresse abgelehnt.");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            byte[] data = Web.Download(url, 60000, 600L * 1024 * 1024);
            if (!string.IsNullOrEmpty(sha1) && !Sha1Hex(data).Equals(sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Prüfsumme falsch: " + Path.GetFileName(target));
            string part = target + ".part"; File.WriteAllBytes(part, data);
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
        }

        public static string Sha1Hex(byte[] data)
        {
            using (SHA1 s = SHA1.Create()) { StringBuilder sb = new StringBuilder(); foreach (byte b in s.ComputeHash(data)) sb.Append(b.ToString("x2")); return sb.ToString(); }
        }

        static void SafeRelative(string rel)
        {
            if (rel == null || rel.Contains("..") || Path.IsPathRooted(rel) || rel.Contains(":")) throw new InvalidOperationException("Ungültiger Dateipfad in der Versionsdatei.");
        }

        static void ForAll<T>(List<T> items, Action<T> work, Action<int, int> progress)
        {
            int done = 0, total = items.Count;
            ServicePointManagerLimit();
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = 8 };
            try
            {
                Parallel.ForEach(items, po, delegate (T it)
                {
                    work(it);
                    int d = Interlocked.Increment(ref done);
                    if (progress != null && (d % 20 == 0 || d == total)) progress(d, total);
                });
            }
            catch (AggregateException ae) { throw ae.InnerExceptions[0]; }
        }

        static void ServicePointManagerLimit() { System.Net.ServicePointManager.DefaultConnectionLimit = Math.Max(System.Net.ServicePointManager.DefaultConnectionLimit, 32); }

        // makes sure the description file of a vanilla version is there
        public static void EnsureVanillaJson(string shared, string mcVersion)
        {
            string dir = Path.Combine(VersionsDir(shared), mcVersion), file = Path.Combine(dir, mcVersion + ".json");
            if (File.Exists(file)) return;
            string url, sha1;
            if (!FindInManifest(Web.Get("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", 20000), mcVersion, out url, out sha1)) throw new InvalidOperationException("Minecraft " + mcVersion + " gibt es nicht.");
            Fetch(url, file, sha1, 0);
        }

        // Fabric: description file of loader + version (from Fabric's meta server)
        public static string EnsureFabric(string shared, string mcVersion, string knownVersionId)
        {
            string id = knownVersionId;
            if (id == null || !Regex.IsMatch(id, @"^fabric-loader-[0-9A-Za-z.+\-]+-" + Regex.Escape(mcVersion) + "$"))
            {
                string loader = ParseFabricLoader(Web.Get("https://meta.fabricmc.net/v2/versions/loader/" + mcVersion, 20000));
                if (loader == null || !Regex.IsMatch(loader, @"^[0-9A-Za-z.\-+]+$")) throw new InvalidOperationException("Für Minecraft " + mcVersion + " gibt es keinen Fabric-Loader.");
                id = "fabric-loader-" + loader + "-" + mcVersion;
            }
            string file = Path.Combine(VersionsDir(shared), id, id + ".json");
            if (!File.Exists(file))
            {
                string loaderVer = id.Substring("fabric-loader-".Length, id.Length - "fabric-loader-".Length - mcVersion.Length - 1);
                string prof = Web.Get("https://meta.fabricmc.net/v2/versions/loader/" + mcVersion + "/" + loaderVer + "/profile/json", 30000);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, prof, new UTF8Encoding(false));
            }
            return id;
        }

        // Creates the profile's folders and fetches what describes its version (no game files yet: they come at the first start).
        public static string Install(Config cfg, McProfile p)
        {
            if (!ValidVersion(p.McVersion)) throw new InvalidOperationException("Ungültige Minecraft-Version.");
            string shared = SharedDir(cfg);
            EnsureVanillaJson(shared, p.McVersion);
            string id = p.McVersion;
            if (p.Loader == "fabric") id = EnsureFabric(shared, p.McVersion, null);
            Directory.CreateDirectory(ModsDir(cfg, p));
            return id;
        }

        public static void Uninstall(Config cfg, McProfile p) { try { Directory.Delete(GameDir(cfg, p), true); } catch { } }

        // Downloads and checks everything for the profile's version. withAssets=false skips sounds/languages (tests).
        public static McPlan Prepare(Config cfg, McProfile p, bool withAssets, Action<string> progress)
        {
            string shared = SharedDir(cfg), vdir = VersionsDir(shared);
            if (progress != null) progress("Prüfe Versionsdateien …");
            EnsureVanillaJson(shared, p.McVersion);
            string versionId = p.Loader == "fabric" ? EnsureFabric(shared, p.McVersion, p.VersionId) : p.McVersion;
            p.VersionId = versionId;
            Dictionary<string, object> merged = LoadMerged(vdir, versionId);
            McPlan plan = new McPlan { Merged = merged, VersionId = versionId, MainClass = S(merged, "mainClass") };
            plan.ClientJarId = S(merged, "inheritsFrom") ?? versionId;
            Dictionary<string, object> jv = D(G(merged, "javaVersion"));
            if (jv != null) plan.JavaMajor = (int)L(jv, "majorVersion");

            // client jar
            Dictionary<string, object> client = D(G(D(G(merged, "downloads")), "client"));
            plan.ClientJar = Path.Combine(vdir, plan.ClientJarId, plan.ClientJarId + ".jar");
            if (client == null) throw new InvalidOperationException("Die Versionsdatei enthält keinen Client.");
            if (progress != null) progress("Lade Minecraft " + plan.ClientJarId + " …");
            Fetch(S(client, "url"), plan.ClientJar, S(client, "sha1"), L(client, "size"));

            // libraries
            plan.Libs = ParseLibraries(merged);
            string libRoot = Path.Combine(shared, "libraries");
            foreach (McLib l in plan.Libs) SafeRelative(l.Path);
            ForAll(plan.Libs, delegate (McLib l) { Fetch(l.Url, Path.Combine(libRoot, l.Path.Replace('/', '\\')), l.Sha1, l.Size); },
                delegate (int d, int t) { if (progress != null) progress("Lade Bibliotheken " + d + " / " + t + " …"); });
            foreach (McLib l in plan.Libs) if (!l.IsNative) plan.ClassPath.Add(Path.Combine(libRoot, l.Path.Replace('/', '\\')));
            plan.ClassPath.Add(plan.ClientJar);

            // natives (older versions ship them as separate zips)
            plan.NativesDir = Path.Combine(vdir, versionId, "natives");
            foreach (McLib l in plan.Libs) if (l.IsNative) ExtractNatives(Path.Combine(libRoot, l.Path.Replace('/', '\\')), plan.NativesDir, l.Excludes);

            // assets
            plan.AssetsRoot = Path.Combine(shared, "assets");
            Dictionary<string, object> ai = D(G(merged, "assetIndex"));
            plan.AssetIndexId = S(ai, "id") ?? S(merged, "assets") ?? "legacy";
            if (ai != null) PrepareAssets(plan, ai, withAssets, progress);
            return plan;
        }

        static void ExtractNatives(string zipPath, string dir, List<string> excludes)
        {
            Directory.CreateDirectory(dir);
            using (ZipArchive z = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (e.Name.Length == 0 || e.FullName.EndsWith("/")) continue;
                    bool skip = false;
                    foreach (string x in excludes) if (e.FullName.StartsWith(x, StringComparison.OrdinalIgnoreCase)) skip = true;
                    if (skip || e.FullName.Contains("..")) continue;
                    string t = Path.Combine(dir, e.Name);
                    if (File.Exists(t) && new FileInfo(t).Length == e.Length) continue;
                    try { e.ExtractToFile(t, true); } catch (IOException) { /* in use by a running game: it is the same file */ }
                }
        }

        static void PrepareAssets(McPlan plan, Dictionary<string, object> ai, bool withAssets, Action<string> progress)
        {
            string indexFile = Path.Combine(plan.AssetsRoot, "indexes", plan.AssetIndexId + ".json");
            Fetch(S(ai, "url"), indexFile, S(ai, "sha1"), 0);
            if (!withAssets) return;
            Dictionary<string, object> idx = ParseJson(File.ReadAllText(indexFile, Encoding.UTF8));
            Dictionary<string, object> objs = D(G(idx, "objects")) ?? new Dictionary<string, object>();
            bool legacy = G(idx, "virtual") is bool && (bool)G(idx, "virtual");
            List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, object> kv in objs)
            {
                string hash = S(D(kv.Value), "hash");
                if (hash != null && Regex.IsMatch(hash, "^[0-9a-f]{40}$")) items.Add(new KeyValuePair<string, string>(kv.Key, hash));
            }
            ForAll(items, delegate (KeyValuePair<string, string> it)
            {
                string h = it.Value, rel = h.Substring(0, 2) + "/" + h;
                Fetch("https://resources.download.minecraft.net/" + rel, Path.Combine(plan.AssetsRoot, "objects", h.Substring(0, 2), h), h, 0);
            }, delegate (int d, int t) { if (progress != null) progress("Lade Sounds & Sprachen " + d + " / " + t + " …"); });
            if (legacy)
            {
                string virt = Path.Combine(plan.AssetsRoot, "virtual", "legacy");
                foreach (KeyValuePair<string, string> it in items)
                {
                    if (it.Key.Contains("..")) continue;
                    string t = Path.Combine(virt, it.Key.Replace('/', '\\'));
                    if (File.Exists(t)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(t));
                    File.Copy(Path.Combine(plan.AssetsRoot, "objects", it.Value.Substring(0, 2), it.Value), t);
                }
                plan.AssetsRoot = virt;
            }
        }

        // ---- Java ----
        public static int JavaMajorFromVersion(string v)
        {
            Match m = Regex.Match(v ?? "", @"(?:version\s+)?""?(\d+)(?:\.(\d+))?");
            if (!m.Success) return 0;
            int a = int.Parse(m.Groups[1].Value);
            return a == 1 && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : a;      // 1.8.0_x = Java 8
        }

        public static string FindJava(int required, out string problem)
        {
            problem = null;
            List<string> candidates = new List<string>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] roots = {
                Path.Combine(appData, ".minecraft", "runtime"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BellSoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto") };
            string jh = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(jh)) candidates.Add(Path.Combine(jh, "bin", "javaw.exe"));
            foreach (string r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try { foreach (string f in Directory.GetFiles(r, "javaw.exe", SearchOption.AllDirectories)) candidates.Add(f); } catch { }
            }
            string bestExact = null; string bestHigher = null; int higher = int.MaxValue;
            foreach (string c in candidates)
            {
                if (!File.Exists(c)) continue;
                int major = JavaMajorOf(c);
                if (major == required) { bestExact = c; break; }
                if (major > required && major < higher) { higher = major; bestHigher = c; }
            }
            string pick = bestExact ?? bestHigher;
            if (pick == null) problem = "Für diese Minecraft-Version wird Java " + required + " gebraucht, gefunden wurde keins. Installiere z. B. „Eclipse Temurin " + required + "“ (adoptium.net) und starte es noch einmal.";
            return pick;
        }

        static int JavaMajorOf(string javaw)
        {
            try
            {
                string releaseFile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(javaw)), "release");
                if (File.Exists(releaseFile))
                {
                    Match m = Regex.Match(File.ReadAllText(releaseFile), "JAVA_VERSION=\"([^\"]+)\"");
                    if (m.Success) return JavaMajorFromVersion(m.Groups[1].Value);
                }
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(javaw), "java.exe"), "-version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                using (Process pr = Process.Start(psi)) { string err = pr.StandardError.ReadToEnd(); pr.WaitForExit(8000); return JavaMajorFromVersion(err); }
            }
            catch { return 0; }
        }

        // ---- arguments ----
        static string Subst(string s, Dictionary<string, string> vars)
        {
            return Regex.Replace(s, @"\$\{([a-z_]+)\}", delegate (Match m) { string v; return vars.TryGetValue(m.Groups[1].Value, out v) ? v : m.Value; });
        }

        static void AddArgs(object list, Dictionary<string, string> vars, ICollection<string> features, List<string> into)
        {
            object[] items = Modrinth.ToArray(list);
            if (items == null) return;
            foreach (object o in items)
            {
                if (o is string) { into.Add(Subst((string)o, vars)); continue; }
                Dictionary<string, object> d = D(o);
                if (d == null || !RulesAllow(G(d, "rules"), features)) continue;
                object v = G(d, "value");
                if (v is string) into.Add(Subst((string)v, vars));
                else { object[] vs = Modrinth.ToArray(v); if (vs != null) foreach (object x in vs) into.Add(Subst(x.ToString(), vars)); }
            }
        }

        // returns the full argument list for java (without the java path)
        public static List<string> BuildArguments(McPlan plan, McContext ctx)
        {
            List<string> features = new List<string>();
            if (ctx.Demo) features.Add("is_demo_user");
            Dictionary<string, string> vars = new Dictionary<string, string>
            {
                { "auth_player_name", ctx.PlayerName }, { "version_name", plan.VersionId }, { "game_directory", ctx.GameDir },
                { "assets_root", plan.AssetsRoot }, { "assets_index_name", plan.AssetIndexId }, { "auth_uuid", ctx.Uuid },
                { "auth_access_token", ctx.AccessToken }, { "clientid", "0" }, { "auth_xuid", "0" }, { "user_type", ctx.UserType },
                { "version_type", S(plan.Merged, "type") ?? "release" }, { "natives_directory", plan.NativesDir },
                { "launcher_name", "Peach" }, { "launcher_version", ctx.LauncherVersion },
                { "classpath", string.Join(";", plan.ClassPath.ToArray()) }, { "user_properties", "{}" }, { "game_assets", plan.AssetsRoot }
            };
            List<string> jvm = new List<string>(), game = new List<string>();
            jvm.Add("-Xmx" + ctx.RamMb + "M"); jvm.Add("-Xms256M");
            jvm.Add("-Dlog4j2.formatMsgNoLookups=true");
            Dictionary<string, object> args = D(G(plan.Merged, "arguments"));
            if (args != null)
            {
                AddArgs(G(args, "jvm"), vars, features, jvm);
                AddArgs(G(args, "game"), vars, features, game);
            }
            else
            {
                jvm.Add("-Djava.library.path=" + plan.NativesDir); jvm.Add("-cp"); jvm.Add(string.Join(";", plan.ClassPath.ToArray()));
                string legacy = S(plan.Merged, "minecraftArguments") ?? "";
                foreach (string part in legacy.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) game.Add(Subst(part, vars));
                if (ctx.Demo) game.Add("--demo");
            }
            if (!jvm.Contains("-cp") && !jvm.Exists(delegate (string s) { return s == "-classpath"; })) { jvm.Add("-cp"); jvm.Add(string.Join(";", plan.ClassPath.ToArray())); }
            List<string> all = new List<string>(jvm); all.Add(plan.MainClass); all.AddRange(game);
            return all;
        }

        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return a;
            StringBuilder sb = new StringBuilder("\""); int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2); sb.Append('"');
            return sb.ToString();
        }

        // starts the game; output goes to <gamedir>\logs\peach-launch.log
        public static Process Launch(Config cfg, McProfile p, McPlan plan, McContext ctx, string javaw)
        {
            Directory.CreateDirectory(ctx.GameDir); Directory.CreateDirectory(Path.Combine(ctx.GameDir, "logs"));
            List<string> a = BuildArguments(plan, ctx);
            StringBuilder cmd = new StringBuilder();
            foreach (string s in a) { if (cmd.Length > 0) cmd.Append(' '); cmd.Append(Quote(s)); }
            ProcessStartInfo psi = new ProcessStartInfo(javaw, cmd.ToString()) { WorkingDirectory = ctx.GameDir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            Process pr = Process.Start(psi);
            string logPath = Path.Combine(ctx.GameDir, "logs", "peach-launch.log");
            StreamWriter log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            DataReceivedEventHandler h = delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) { lock (log) { try { log.WriteLine(ctx.AccessToken != null && ctx.AccessToken.Length > 12 ? e.Data.Replace(ctx.AccessToken, "***") : e.Data); } catch { } } } };
            pr.OutputDataReceived += h; pr.ErrorDataReceived += h;
            pr.BeginOutputReadLine(); pr.BeginErrorReadLine();
            pr.EnableRaisingEvents = true;
            pr.Exited += delegate { lock (log) { try { log.Dispose(); } catch { } } };
            return pr;
        }

        public static Process FindGameProcess()
        {
            foreach (string n in new string[] { "javaw", "java" })
                foreach (Process p in Process.GetProcessesByName(n))
                {
                    try { if (p.MainWindowTitle != null && p.MainWindowTitle.StartsWith("Minecraft", StringComparison.Ordinal)) return p; } catch { }
                    p.Dispose();
                }
            return null;
        }
    }
}
