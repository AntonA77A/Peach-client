using System;
using System.Diagnostics;
using System.Threading;

namespace Peach
{
    // Controller -> mouse (XInput: Xbox controllers and anything Windows maps to XInput).
    //
    // Normal mode                              While the on-screen keyboard is open
    //   Left stick  move pointer                 Left stick / right stick  still move / scroll
    //   Right stick scroll                       D-pad       choose a key
    //   A / B / X   left / right / middle click  A           type the chosen key
    //   RT          left click                   B           backspace      X  space
    //   LB / RB     slow / fast pointer          Y           shift          Start  enter
    //   D-pad       arrow keys                   LB / RB     cursor left / right
    //   Y           open keyboard                RT          left click (to pick another field)
    //   Back + Start toggles controller mouse    Back        close keyboard
    //
    // Home (Xbox button, or Back + Y): bring the controller-mode launcher to the front from any app.
    // When the launcher is the window in front, the pad only navigates its tiles (D-pad/stick, A).
    internal class GamepadMouse : IDisposable
    {
        const ushort DUp = 0x0001, DDown = 0x0002, DLeft = 0x0004, DRight = 0x0008, Start = 0x0010, Back = 0x0020,
                     LB = 0x0100, RB = 0x0200, BtnA = 0x1000, BtnB = 0x2000, BtnX = 0x4000, BtnY = 0x8000,
                     Guide = 0x0400;   // Xbox / Home button (only reported through XInputGetStateEx)
        const double DeadZone = 0.22;
        const byte TriggerThreshold = 40;

        public volatile bool Enabled;
        public volatile int Speed;
        // Last time the user touched the controller. Starts "long ago" so the keyboard never pops up
        // for mouse/keyboard users just because the app started.
        public int LastActivity = Environment.TickCount - 3600000;
        public event Action<bool> Toggled;
        public Func<bool> KeyboardOpen;
        public Action<PadKey> KeyboardInput;
        public Action KeyboardToggle;
        // Controller mode: while Locked, Back+Start cannot switch the pad off (the launcher needs it).
        public volatile bool Locked;
        public Action HomeRequested;               // Back + Y
        public Func<bool> LauncherOpen;
        public Action<PadKey> LauncherInput;

        // Is a controller plugged in right now? Controller mode only works while this is true.
        public volatile bool Connected;
        public event Action<bool> ConnectionChanged;

        public void SetConnected(bool connected)
        {
            if (Connected == connected) return;
            Connected = connected;
            Action<bool> h = ConnectionChanged;
            if (h != null) h(connected);
        }

        Thread thread;
        volatile bool run = true;
        readonly bool[] held = new bool[3];
        static readonly uint[] ClickDown = { Native.MOUSE_LEFTDOWN, Native.MOUSE_RIGHTDOWN, Native.MOUSE_MIDDLEDOWN };
        static readonly uint[] ClickUp = { Native.MOUSE_LEFTUP, Native.MOUSE_RIGHTUP, Native.MOUSE_MIDDLEUP };

        // per-step state
        double fx, fy, sx, sy;
        ushort prev;
        bool comboHeld, backPrev, backSawStart;
        // key-repeat slots: 0-3 d-pad up/down/left/right, 4 B, 5 LB, 6 RB
        readonly double[] nextFire = { -1, -1, -1, -1, -1, -1, -1 };

        public GamepadMouse(bool enabled, int speed) : this(enabled, speed, true) { }

        // start=false lets the self test drive Step() with fake controller states.
        public GamepadMouse(bool enabled, int speed, bool start)
        {
            Enabled = enabled;
            Speed = speed;
            if (start)
            {
                thread = new Thread(Loop);
                thread.IsBackground = true;
                thread.Name = "Peach gamepad";
                thread.Start();
            }
        }

        // Radial dead zone + quadratic response: fine control near the centre, fast at the edge.
        public static void Stick(short x, short y, out double ox, out double oy)
        {
            double fx = x / 32767.0, fy = y / 32767.0;
            double raw = Math.Sqrt(fx * fx + fy * fy);
            if (raw < DeadZone) { ox = 0; oy = 0; return; }
            double m = Math.Min(raw, 1.0);
            double s = (m - DeadZone) / (1.0 - DeadZone);
            s *= s;
            ox = fx / raw * s;
            oy = fy / raw * s;
        }

        void ReleaseAll()
        {
            for (int i = 0; i < held.Length; i++)
                if (held[i]) { held[i] = false; Native.MouseButton(ClickUp[i]); }
        }

        void SendKey(PadKey k) { Action<PadKey> h = KeyboardInput; if (h != null) h(k); }
        void SendLauncher(PadKey k) { Action<PadKey> h = LauncherInput; if (h != null) h(k); }

        bool Repeat(int slot, bool pressed, double now)
        {
            if (!pressed) { nextFire[slot] = -1; return false; }
            if (nextFire[slot] < 0) { nextFire[slot] = now + 0.4; return true; }
            if (now >= nextFire[slot]) { nextFire[slot] = now + 0.06; return true; }
            return false;
        }

        void Click(int i, bool want)
        {
            if (want && !held[i]) { held[i] = true; Native.MouseButton(ClickDown[i]); }
            else if (!want && held[i]) { held[i] = false; Native.MouseButton(ClickUp[i]); }
        }

        // One controller poll. `now` = seconds on a steady clock, `dt` = seconds since the previous step.
        public void Step(Native.XINPUT_STATE st, double now, double dt)
        {
            ushort b = st.Gamepad.wButtons;

            // Back + Start toggles the controller mouse, from any state
            bool combo = (b & Back) != 0 && (b & Start) != 0;
            if (combo && !comboHeld && !Locked)
            {
                Enabled = !Enabled;
                if (!Enabled) ReleaseAll();
                Action<bool> h = Toggled;
                if (h != null) h(Enabled);
            }
            comboHeld = combo;

            // Back on its own closes the keyboard - decided on release so Back+Start never counts as "close"
            bool backNow = (b & Back) != 0;
            if (backNow && !backPrev) backSawStart = false;
            if (backNow && (b & Start) != 0) backSawStart = true;
            bool backReleased = !backNow && backPrev;
            backPrev = backNow;

            // Home = Xbox button, or Back + Y: opens controller mode / brings the launcher to the front
            // (works even while the pad is off)
            bool homeY = backNow && (b & BtnY) != 0 && (prev & BtnY) == 0;
            bool homeGuide = (b & Guide) != 0 && (prev & Guide) == 0;
            if (homeY || homeGuide)
            {
                if (homeY) backSawStart = true;   // not a plain Back press, so it must not close the keyboard
                Action home = HomeRequested;
                if (home != null) home();
            }

            if (!Enabled) { prev = b; return; }

            bool kb = KeyboardOpen != null && KeyboardOpen();
            Func<ushort, bool> pressed = delegate (ushort m) { return (b & m) != 0 && (prev & m) == 0; };

            double ox, oy, rx, ry;
            Stick(st.Gamepad.sThumbLX, st.Gamepad.sThumbLY, out ox, out oy);
            Stick(st.Gamepad.sThumbRX, st.Gamepad.sThumbRY, out rx, out ry);
            bool trigger = st.Gamepad.bRightTrigger > TriggerThreshold;
            if (b != prev || ox != 0 || oy != 0 || rx != 0 || ry != 0 || trigger) LastActivity = Environment.TickCount;

            // launcher (controller mode home screen): the pad only navigates it, the pointer stays put
            if (LauncherOpen != null && LauncherOpen())
            {
                Click(0, false); Click(1, false); Click(2, false);
                if (Repeat(0, (b & DUp) != 0 || oy > 0.3, now)) SendLauncher(PadKey.Up);
                if (Repeat(1, (b & DDown) != 0 || oy < -0.3, now)) SendLauncher(PadKey.Down);
                if (Repeat(2, (b & DLeft) != 0 || ox < -0.3, now)) SendLauncher(PadKey.Left);
                if (Repeat(3, (b & DRight) != 0 || ox > 0.3, now)) SendLauncher(PadKey.Right);
                if (pressed(BtnA)) SendLauncher(PadKey.Select);
                // B does nothing here on purpose: the launcher is only left with the "Modus beenden" tile
                prev = b;
                return;
            }

            // pointer + scroll work in both modes
            double factor = kb ? 1.0 : ((b & LB) != 0 ? 0.35 : ((b & RB) != 0 ? 2.2 : 1.0));
            fx += ox * Speed * factor * dt;
            fy -= oy * Speed * factor * dt;
            int dx = (int)fx, dy = (int)fy;
            if (dx != 0 || dy != 0) { fx -= dx; fy -= dy; Native.MoveCursor(dx, dy); }

            if (ry == 0) sy = 0; else sy += ry * 1500 * dt;
            if (rx == 0) sx = 0; else sx += rx * 1500 * dt;
            if (Math.Abs(sy) >= 30) { int w = (int)sy; sy -= w; Native.Wheel(w, false); }
            if (Math.Abs(sx) >= 30) { int w = (int)sx; sx -= w; Native.Wheel(w, true); }

            // clicks: with the keyboard open A belongs to the keyboard, so RT is the only left click
            Click(0, kb ? trigger : ((b & BtnA) != 0 || trigger));
            Click(1, !kb && (b & BtnB) != 0);
            Click(2, !kb && (b & BtnX) != 0);

            bool up = Repeat(0, (b & DUp) != 0, now), down = Repeat(1, (b & DDown) != 0, now),
                 left = Repeat(2, (b & DLeft) != 0, now), right = Repeat(3, (b & DRight) != 0, now);

            if (kb)
            {
                if (up) SendKey(PadKey.Up);
                if (down) SendKey(PadKey.Down);
                if (left) SendKey(PadKey.Left);
                if (right) SendKey(PadKey.Right);
                if (pressed(BtnA)) SendKey(PadKey.Select);
                if (Repeat(4, (b & BtnB) != 0, now)) SendKey(PadKey.Backspace);
                if (pressed(BtnX)) SendKey(PadKey.Space);
                if (pressed(BtnY) && !backNow) SendKey(PadKey.Shift);
                if (Repeat(5, (b & LB) != 0, now)) SendKey(PadKey.CursorLeft);
                if (Repeat(6, (b & RB) != 0, now)) SendKey(PadKey.CursorRight);
                if (pressed(Start) && !backNow) SendKey(PadKey.Enter);
                if (backReleased && !backSawStart) SendKey(PadKey.Close);
            }
            else
            {
                if (up) Native.Key(0x26, true);
                if (down) Native.Key(0x28, true);
                if (left) Native.Key(0x25, true);
                if (right) Native.Key(0x27, true);
                if (pressed(BtnY) && !backNow)
                {
                    Action t = KeyboardToggle;
                    if (t != null) t();
                }
            }

            prev = b;
        }

        void Loop()
        {
            Native.timeBeginPeriod(1);
            Stopwatch sw = Stopwatch.StartNew();
            double last = sw.Elapsed.TotalSeconds, nextProbe = 0;
            int user = -1;
            Native.XINPUT_STATE st;
            try
            {
                while (run)
                {
                    double now = sw.Elapsed.TotalSeconds;
                    double dt = Math.Min(now - last, 0.05);
                    last = now;

                    if (user < 0)
                    {
                        if (now >= nextProbe)
                        {
                            nextProbe = now + 0.8;
                            for (int i = 0; i < 4; i++) if (Native.PadState(i, out st)) { user = i; break; }
                        }
                        if (user >= 0) SetConnected(true);
                        if (user < 0) { Thread.Sleep(100); continue; }
                    }
                    if (!Native.PadState(user, out st)) { user = -1; ReleaseAll(); SetConnected(false); continue; }

                    Step(st, now, dt);
                    Thread.Sleep(Enabled ? 4 : 30);
                }
            }
            finally { Native.timeEndPeriod(1); }
        }

        public void Dispose()
        {
            run = false;
            if (thread != null) thread.Join(500);
            ReleaseAll();
        }
    }
}
