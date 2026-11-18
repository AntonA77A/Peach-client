using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Peach
{
    internal delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
        public const uint MOUSE_LEFTDOWN = 0x02, MOUSE_LEFTUP = 0x04, MOUSE_RIGHTDOWN = 0x08, MOUSE_RIGHTUP = 0x10,
                          MOUSE_MIDDLEDOWN = 0x20, MOUSE_MIDDLEUP = 0x40, MOUSE_WHEEL = 0x800, MOUSE_HWHEEL = 0x1000;

        // Marks our own synthetic input so the keyboard hook never reacts to it.
        public static readonly IntPtr Magic = new IntPtr(0x50454348);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION u; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger; public short sThumbLX; public short sThumbLY; public short sThumbRX; public short sThumbRY; }
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", EntryPoint = "GetOEMCP")] static extern uint GetOEMCPNative();
        public static int OemCodePage() { try { return (int)GetOEMCPNative(); } catch { return 437; } }
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buf, int size, uint flags, IntPtr hkl);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

        // The documented XInputGetState hides the Xbox/Home ("Guide") button. The undocumented export #100
        // (XInputGetStateEx, also used by SDL and friends) returns the same struct including button 0x0400.
        [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern uint XInputGetStateEx14(uint index, out XINPUT_STATE s);
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState14(uint index, out XINPUT_STATE s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState910(uint index, out XINPUT_STATE s);
        // 0 = xinput1_4 with Guide button, 1 = xinput1_4 plain, 2 = xinput9_1_0 plain, 3 = unavailable
        static int xinputMode;
        public static int XInputMode { get { return xinputMode; } }

        public static bool PadState(int index, out XINPUT_STATE s)
        {
            s = new XINPUT_STATE();
            try
            {
                if (xinputMode == 0) return XInputGetStateEx14((uint)index, out s) == 0;
                if (xinputMode == 1) return XInputGetState14((uint)index, out s) == 0;
                if (xinputMode == 2) return XInputGetState910((uint)index, out s) == 0;
            }
            catch (DllNotFoundException) { xinputMode = xinputMode < 2 ? 2 : xinputMode + 1; return PadState(index, out s); }
            catch (EntryPointNotFoundException) { xinputMode++; return PadState(index, out s); }
            return false;
        }

        static INPUT KeyInput(ushort vk, ushort scan, uint flags)
        {
            INPUT i = new INPUT();
            i.type = INPUT_KEYBOARD;
            i.u.ki.wVk = vk; i.u.ki.wScan = scan; i.u.ki.dwFlags = flags; i.u.ki.dwExtraInfo = Magic;
            return i;
        }

        public static void AddKey(List<INPUT> list, ushort vk, bool extended)
        {
            uint ext = extended ? KEYEVENTF_EXTENDEDKEY : 0;
            list.Add(KeyInput(vk, 0, ext));
            list.Add(KeyInput(vk, 0, ext | KEYEVENTF_KEYUP));
        }

        static void AddChar(List<INPUT> list, char c)
        {
            list.Add(KeyInput(0, c, KEYEVENTF_UNICODE));
            list.Add(KeyInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        public static void Send(List<INPUT> list)
        {
            if (list.Count == 0) return;
            SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static void Key(ushort vk, bool extended)
        {
            List<INPUT> l = new List<INPUT>();
            AddKey(l, vk, extended);
            Send(l);
        }

        public static void TypeText(string text, int backspaces)
        {
            List<INPUT> l = new List<INPUT>();
            for (int i = 0; i < backspaces; i++) AddKey(l, 0x08, false);
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n') AddKey(l, 0x0D, false);
                else if (c == '\t') AddKey(l, 0x09, false);
                else AddChar(l, c);
            }
            Send(l);
        }

        public static void MouseButton(uint flags)
        {
            INPUT i = new INPUT();
            i.type = INPUT_MOUSE;
            i.u.mi.dwFlags = flags;
            Send(new List<INPUT>(new INPUT[] { i }));
        }

        public static void Wheel(int amount, bool horizontal)
        {
            INPUT i = new INPUT();
            i.type = INPUT_MOUSE;
            i.u.mi.dwFlags = horizontal ? MOUSE_HWHEEL : MOUSE_WHEEL;
            i.u.mi.mouseData = unchecked((uint)amount);
            Send(new List<INPUT>(new INPUT[] { i }));
        }

        // Moves the cursor without pointer acceleration, clamped to the whole virtual desktop.
        public static void MoveCursor(int dx, int dy)
        {
            POINT p;
            if (!GetCursorPos(out p)) return;
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int right = left + GetSystemMetrics(78) - 1, bottom = top + GetSystemMetrics(79) - 1;
            int x = Math.Max(left, Math.Min(right, p.x + dx));
            int y = Math.Max(top, Math.Min(bottom, p.y + dy));
            SetCursorPos(x, y);
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        public static bool ModifiersDown(bool includeShift)
        {
            return Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C) || (includeShift && Down(0x10));
        }

        // Hotkey-triggered typing must wait until the user lets go of Ctrl/Alt, or the
        // target app would see Ctrl+Alt+<letter> instead of plain text.
        public static void WaitModifiersUp(bool includeShift)
        {
            int end = Environment.TickCount + 800;
            while (Environment.TickCount < end && ModifiersDown(includeShift)) Thread.Sleep(10);
        }

        public static void ForceForeground(IntPtr h)
        {
            // A tapped Alt key lifts Windows' foreground lock. (AttachThreadInput is avoided on purpose:
            // it can block forever if the current foreground app is hung.)
            List<INPUT> alt = new List<INPUT>();
            AddKey(alt, 0x12, false);
            Send(alt);
            SetForegroundWindow(h);
            BringWindowToTop(h);
        }
    }
}
