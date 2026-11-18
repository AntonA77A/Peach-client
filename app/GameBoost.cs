using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Peach
{
    internal class SystemStats
    {
        public double Cpu;            // percent
        public double Gpu = -1;       // percent, -1 = not available on this PC
        public double RamPct;
        public long RamUsedMb, RamTotalMb;
    }

    // Pure helpers (unit-tested): names, protected processes, power plan parsing.
    internal static class BoostLogic
    {
        static readonly string[] protectedNames = {
            "system", "idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "sihost", "fontdrvhost",
            "ctfmon", "conhost", "audiodg", "runtimebroker", "searchhost", "searchapp", "startmenuexperiencehost", "shellexperiencehost",
            "textinputhost", "taskmgr", "peach", "msmpeng", "securityhealthservice", "registry", "memory compression", "wudfhost", "dllhost" };

        // "Game.EXE", "C:\Games\game.exe", "\"game\"" -> "game.exe"; null when it is not a plain program name
        public static string NormalizeExe(string s)
        {
            if (s == null) return null;
            s = s.Trim().Trim('"').Trim();
            if (s.Length == 0) return null;
            int cut = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
            if (cut >= 0) s = s.Substring(cut + 1);
            s = s.ToLowerInvariant();
            if (!s.EndsWith(".exe")) s += ".exe";
            if (!Regex.IsMatch(s, @"^[a-z0-9_ .()+\-]{1,80}$") || s == ".exe") return null;
            return s;
        }

        public static string ProcName(string exe) { return exe.EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe; }

        // Windows itself, the shell, security software and Peach are never closed or touched.
        public static bool IsProtected(string exe)
        {
            string n = exe == null ? null : NormalizeExe(exe);
            if (n == null) return true;
            return Array.IndexOf(protectedNames, ProcName(n)) >= 0;
        }

        public static bool Matches(string procName, List<string> exes)
        {
            if (procName == null || exes == null) return false;
            string n = NormalizeExe(procName);
            if (n == null) return false;
            foreach (string e in exes) if (NormalizeExe(e) == n) return true;
            return false;
        }

        static readonly Regex guidRe = new Regex(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        public static bool IsGuid(string s) { return s != null && Regex.IsMatch(s, @"^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$"); }

        // output of "powercfg /getactivescheme" in any language
        public static string ParseActiveScheme(string output)
        {
            if (output == null) return null;
            Match m = guidRe.Match(output);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        // output of "powercfg /list": picks "Ultimate Performance" first, then "High performance" (English / German)
        public static string PickScheme(string output)
        {
            if (output == null) return null;
            string high = null, ultimate = null;
            foreach (string line in output.Split('\n'))
            {
                Match m = guidRe.Match(line);
                if (!m.Success) continue;
                string l = line.ToLowerInvariant();
                if (l.Contains("ultimate") || l.Contains("ultimativ")) { if (ultimate == null) ultimate = m.Value.ToLowerInvariant(); }
                else if (l.Contains("high performance") || l.Contains("höchstleistung") || l.Contains("hochleistung") || l.Contains("hohe leistung")) { if (high == null) high = m.Value.ToLowerInvariant(); }
            }
            return ultimate ?? high;
        }
    }

    // CPU, RAM and GPU load, the same numbers as the Task Manager (performance counters).
    internal class StatsSampler : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        PerformanceCounter cpu;
        readonly Dictionary<string, List<PerformanceCounter>> gpu = new Dictionary<string, List<PerformanceCounter>>();
        DateTime gpuListed = DateTime.MinValue;
        bool cpuFailed, gpuFailed;

        public SystemStats Sample(bool withGpu)
        {
            SystemStats s = new SystemStats();
            s.Cpu = SampleCpu();
            MEMORYSTATUSEX ms = new MEMORYSTATUSEX(); ms.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref ms))
            {
                s.RamTotalMb = (long)(ms.ullTotalPhys / (1024 * 1024));
                s.RamUsedMb = (long)((ms.ullTotalPhys - ms.ullAvailPhys) / (1024 * 1024));
                s.RamPct = ms.dwMemoryLoad;
            }
            if (withGpu) s.Gpu = SampleGpu();
            return s;
        }

        double SampleCpu()
        {
            if (cpuFailed) return 0;
            try
            {
                if (cpu == null)
                {
                    try { cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpu.NextValue(); }
                    catch { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); }
                    return 0;     // the first reading of a counter is always 0
                }
                return Math.Max(0, Math.Min(100, cpu.NextValue()));
            }
            catch { cpuFailed = true; return 0; }
        }

        // the busiest 3D engine of any GPU, all programs added up (like the Task Manager's GPU column)
        double SampleGpu()
        {
            if (gpuFailed) return -1;
            try
            {
                if ((DateTime.Now - gpuListed).TotalSeconds > 15)
                {
                    gpuListed = DateTime.Now;
                    if (!PerformanceCounterCategory.Exists("GPU Engine")) { gpuFailed = true; return -1; }
                    string[] names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
                    foreach (string n in names)
                    {
                        if (n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Match m = Regex.Match(n, @"luid_\S+?_eng_\d+");   // one 3D engine of one adapter; all programs using it are added up
                        string adapter = m.Success ? m.Value : "gpu";
                        List<PerformanceCounter> l;
                        if (!gpu.TryGetValue(adapter, out l)) { l = new List<PerformanceCounter>(); gpu[adapter] = l; }
                        bool known = false;
                        foreach (PerformanceCounter pc in l) if (pc.InstanceName == n) { known = true; break; }
                        if (!known) { try { PerformanceCounter c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true); c.NextValue(); l.Add(c); } catch { } }
                    }
                }
                double best = -1;
                foreach (KeyValuePair<string, List<PerformanceCounter>> kv in gpu)
                {
                    double sum = 0;
                    for (int i = kv.Value.Count - 1; i >= 0; i--)
                    {
                        try { sum += kv.Value[i].NextValue(); }
                        catch { kv.Value[i].Dispose(); kv.Value.RemoveAt(i); }      // that process ended
                    }
                    best = Math.Max(best, Math.Min(100, sum));
                }
                return best;
            }
            catch { gpuFailed = true; return -1; }
        }

        public void Dispose()
        {
            if (cpu != null) cpu.Dispose();
            foreach (List<PerformanceCounter> l in gpu.Values) foreach (PerformanceCounter c in l) c.Dispose();
            gpu.Clear();
        }
    }

    // The booster: switches to the high performance power plan, raises the game's priority and (only on your list)
    // asks background programs to close. Undoes everything when the game ends or you switch it off.
    internal class GameBooster : IDisposable
    {
        const string FallbackHighGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        readonly Config cfg;
        readonly Timer timer = new Timer();
        Process game;
        ProcessPriorityClass oldPriority = ProcessPriorityClass.Normal;
        bool priorityChanged, planChanged, autoStarted;

        public bool Active { get; private set; }
        public string Game { get; private set; }          // exe name, null = manual boost
        public string PlanName { get; private set; }
        public int Closed { get; private set; }           // programs asked to close
        public event Action Changed;
        public event Action<string, string> Notify;       // title, text

        // replaceable in tests: runs powercfg with the given arguments and returns its output
        public Func<string, string> RunPowerCfg = delegate (string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.GetEncoding(Native.OemCodePage());
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    return o;
                }
            }
            catch { return null; }
        };

        // replaceable in tests: the program that has the focus right now
        public Func<Process> Foreground = delegate
        {
            try
            {
                uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return null;
                return Process.GetProcessById((int)pid);
            }
            catch { return null; }
        };

        public GameBooster(Config cfg)
        {
            this.cfg = cfg;
            timer.Interval = 3000;
            timer.Tick += delegate { Tick(); };
        }

        public void Start() { RecoverIfNeeded(); timer.Start(); }

        void Say(string title, string text) { Action<string, string> n = Notify; if (n != null) n(title, text); }
        void Fire() { Action a = Changed; if (a != null) a(); }

        // Peach was closed or crashed while the power plan was switched: put the old one back.
        public void RecoverIfNeeded()
        {
            string prev = cfg.GameBoost.RestoreScheme;
            if (Active || !BoostLogic.IsGuid(prev)) { if (!Active && prev != null && !BoostLogic.IsGuid(prev)) { cfg.GameBoost.RestoreScheme = null; cfg.Save(); } return; }
            RunPowerCfg("/setactive " + prev);
            cfg.GameBoost.RestoreScheme = null; cfg.Save();
        }

        // game = the program to give more priority (null for a manual boost without a game)
        public bool Boost(Process gameProcess, bool automatic)
        {
            if (Active) return false;
            GameBoostConfig g = cfg.GameBoost;
            string note = "";
            planChanged = false; priorityChanged = false; Closed = 0; PlanName = null;
            if (g.PowerPlan)
            {
                string prev = BoostLogic.ParseActiveScheme(RunPowerCfg("/getactivescheme"));
                string target = BoostLogic.PickScheme(RunPowerCfg("/list")) ?? FallbackHighGuid;
                if (prev != null && prev != target)
                {
                    RunPowerCfg("/setactive " + target);
                    string now = BoostLogic.ParseActiveScheme(RunPowerCfg("/getactivescheme"));
                    if (now == target) { planChanged = true; g.RestoreScheme = prev; cfg.Save(); PlanName = "Höchstleistung"; }
                    else note += "Der Energieplan ließ sich nicht umschalten. ";
                }
                else if (prev == target) PlanName = "Höchstleistung (schon aktiv)";
            }
            game = gameProcess; Game = null;
            if (gameProcess != null)
            {
                try
                {
                    Game = BoostLogic.NormalizeExe(gameProcess.ProcessName);
                    if (g.Priority && !BoostLogic.IsProtected(Game))
                    {
                        oldPriority = gameProcess.PriorityClass;
                        if (oldPriority != ProcessPriorityClass.High) { gameProcess.PriorityClass = ProcessPriorityClass.High; priorityChanged = true; }
                    }
                }
                catch { /* the game ended or is not ours to change */ }
            }
            foreach (string app in new List<string>(g.CloseApps))
            {
                string exe = BoostLogic.NormalizeExe(app);
                if (exe == null || BoostLogic.IsProtected(exe) || exe == Game) continue;
                foreach (Process p in Process.GetProcessesByName(BoostLogic.ProcName(exe)))
                {
                    try { if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) Closed++; } catch { }
                    finally { p.Dispose(); }
                }
            }
            Active = true; autoStarted = automatic;
            Fire();
            Say("Game-Boost an", (Game != null ? Game + " läuft mit mehr Priorität. " : "") + (planChanged ? "Energieplan: Höchstleistung. " : "") + (Closed > 0 ? Closed + " Programm(e) gebeten sich zu schließen. " : "") + note);
            return true;
        }

        public void Restore()
        {
            if (!Active) return;
            if (priorityChanged && game != null)
                try { if (!game.HasExited) game.PriorityClass = oldPriority; } catch { }
            string prev = cfg.GameBoost.RestoreScheme;
            if (planChanged && BoostLogic.IsGuid(prev)) RunPowerCfg("/setactive " + prev);
            cfg.GameBoost.RestoreScheme = null; cfg.Save();
            string was = Game;
            game = null; Game = null; Active = false; planChanged = false; priorityChanged = false; autoStarted = false;
            Fire();
            Say("Game-Boost aus", was != null ? was + " wurde beendet, alles ist wieder normal." : "Alles ist wieder normal.");
        }

        // every 3 seconds: start when a game from the list has the focus, end when it has exited
        public void Tick()
        {
            try
            {
                if (Active)
                {
                    if (autoStarted && (game == null || game.HasExited)) Restore();
                    return;
                }
                if (!cfg.GameBoost.Auto || cfg.GameBoost.Games.Count == 0) return;
                Process fg = Foreground();
                if (fg == null) return;
                string name = null;
                try { name = fg.ProcessName; } catch { }
                if (name != null && BoostLogic.Matches(name, cfg.GameBoost.Games)) Boost(fg, true);
                else fg.Dispose();
            }
            catch { }
        }

        public void Dispose()
        {
            timer.Stop();
            if (Active) Restore();
            timer.Dispose();
        }
    }
}
