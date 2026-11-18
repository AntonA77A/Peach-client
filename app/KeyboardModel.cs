using System;
using System.Collections.Generic;

namespace Peach
{
    // What the controller can do on the on-screen keyboard.
    internal enum PadKey { Left, Right, Up, Down, Select, Backspace, Space, Shift, CursorLeft, CursorRight, Enter, Close }

    internal enum KeyKind { Char, Backspace, Enter, Shift, Layer, Space, CursorLeft, CursorRight, Close }

    internal class KeyDef
    {
        public string Label, Lower, Upper;
        public double W = 1;
        public KeyKind Kind;
    }

    // What a key press should produce. Exactly one of Text / Vk / Close is set (or nothing for navigation).
    internal struct KeyOutput
    {
        public string Text;
        public ushort Vk;
        public bool Extended;
        public bool Close;
        public bool IsNone { get { return Text == null && Vk == 0 && !Close; } }
    }

    // Pure keyboard logic (no UI): German QWERTZ layout, two layers, a selection cursor.
    internal class KeyboardModel
    {
        public const int Rows = 5;
        public int Layer;       // 0 = letters, 1 = symbols
        public bool Shift;      // one-shot: applies to the next character only
        public int Row, Col;

        static KeyDef C(string lower, string upper) { return new KeyDef { Label = lower, Lower = lower, Upper = upper, Kind = KeyKind.Char }; }
        static KeyDef L(char c) { return C(c.ToString(), c.ToString().ToUpperInvariant()); }
        static KeyDef K(string label, KeyKind kind, double w) { return new KeyDef { Label = label, Kind = kind, W = w }; }
        static KeyDef[] Letters(string s) { List<KeyDef> l = new List<KeyDef>(); foreach (char c in s) l.Add(L(c)); return l.ToArray(); }
        static KeyDef[] Chars(string s) { List<KeyDef> l = new List<KeyDef>(); foreach (char c in s) l.Add(C(c.ToString(), c.ToString())); return l.ToArray(); }
        static KeyDef[] Cat(params KeyDef[][] parts) { List<KeyDef> l = new List<KeyDef>(); foreach (KeyDef[] p in parts) l.AddRange(p); return l.ToArray(); }
        static KeyDef[] One(KeyDef k) { return new KeyDef[] { k }; }

        static KeyDef[] BottomRow()
        {
            return new KeyDef[] {
                K("?123", KeyKind.Layer, 1.6), C("@", "@"), K("Leer", KeyKind.Space, 5),
                K("←", KeyKind.CursorLeft, 1), K("→", KeyKind.CursorRight, 1), K("×", KeyKind.Close, 1.2) };
        }

        static readonly KeyDef[][][] Layers = new KeyDef[][][]
        {
            new KeyDef[][] {
                Cat(new KeyDef[] { C("1", "!"), C("2", "\""), C("3", "§"), C("4", "$"), C("5", "%"), C("6", "&"), C("7", "/"), C("8", "("), C("9", ")"), C("0", "="), C("ß", "?") },
                    One(K("⌫", KeyKind.Backspace, 1.6))),
                Cat(Letters("qwertzuiopü"), new KeyDef[] { C("+", "*") }),
                Cat(Letters("asdfghjklöä"), One(K("⏎", KeyKind.Enter, 1.6))),
                Cat(One(K("Shift", KeyKind.Shift, 1.6)), Letters("yxcvbnm"), new KeyDef[] { C(",", ";"), C(".", ":"), C("-", "_") }),
                BottomRow(),
            },
            new KeyDef[][] {
                Cat(Chars("!\"§$%&/()=?"), One(K("⌫", KeyKind.Backspace, 1.6))),
                Chars("@#*+~'_:;<>"),
                Cat(Chars("{}[]\\|^`€°´"), One(K("⏎", KeyKind.Enter, 1.6))),
                Cat(One(K("ABC", KeyKind.Layer, 1.6)), Chars(",.-!?:;")),
                BottomRow(),
            },
        };

        public KeyboardModel() { Reset(); }

        public void Reset() { Layer = 0; Shift = false; Row = 2; Col = 5; }

        public KeyDef[] RowKeys(int row) { return Layers[Layer][row]; }

        public string Label(KeyDef k)
        {
            if (k.Kind == KeyKind.Char) return Shift ? k.Upper : k.Lower;
            if (k.Kind == KeyKind.Layer) return Layer == 0 ? "?123" : "ABC";
            return k.Label;
        }

        double Center(int row, int col)
        {
            KeyDef[] keys = RowKeys(row);
            double sum = 0, acc = 0;
            for (int i = 0; i < keys.Length; i++) { if (i < col) acc += keys[i].W; sum += keys[i].W; }
            return (acc + keys[col].W / 2) / sum;
        }

        int Nearest(int row, double center)
        {
            KeyDef[] keys = RowKeys(row);
            int best = 0; double bestDist = double.MaxValue;
            for (int i = 0; i < keys.Length; i++)
            {
                double d = Math.Abs(Center(row, i) - center);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        void Move(PadKey k)
        {
            int n = RowKeys(Row).Length;
            switch (k)
            {
                case PadKey.Left: Col = (Col + n - 1) % n; break;
                case PadKey.Right: Col = (Col + 1) % n; break;
                case PadKey.Up:
                case PadKey.Down:
                    double c = Center(Row, Col);
                    Row = (Row + (k == PadKey.Up ? Rows - 1 : 1)) % Rows;
                    Col = Nearest(Row, c);
                    break;
            }
        }

        public KeyOutput PressAt(int row, int col) { Row = row; Col = col; return Press(); }

        KeyOutput Press()
        {
            KeyDef key = RowKeys(Row)[Col];
            KeyOutput o = new KeyOutput();
            switch (key.Kind)
            {
                case KeyKind.Char: o.Text = Shift ? key.Upper : key.Lower; Shift = false; break;
                case KeyKind.Space: o.Text = " "; Shift = false; break;
                case KeyKind.Backspace: o.Vk = 0x08; break;
                case KeyKind.Enter: o.Vk = 0x0D; break;
                case KeyKind.CursorLeft: o.Vk = 0x25; o.Extended = true; break;
                case KeyKind.CursorRight: o.Vk = 0x27; o.Extended = true; break;
                case KeyKind.Shift: Shift = !Shift; break;
                case KeyKind.Layer:
                    Layer = 1 - Layer; Shift = false;
                    if (Col >= RowKeys(Row).Length) Col = RowKeys(Row).Length - 1;
                    break;
                case KeyKind.Close: o.Close = true; break;
            }
            return o;
        }

        public KeyOutput Handle(PadKey k)
        {
            KeyOutput o = new KeyOutput();
            switch (k)
            {
                case PadKey.Left: case PadKey.Right: case PadKey.Up: case PadKey.Down: Move(k); break;
                case PadKey.Select: return Press();
                case PadKey.Backspace: o.Vk = 0x08; break;
                case PadKey.Space: o.Text = " "; Shift = false; break;
                case PadKey.Shift: Shift = !Shift; break;
                case PadKey.CursorLeft: o.Vk = 0x25; o.Extended = true; break;
                case PadKey.CursorRight: o.Vk = 0x27; o.Extended = true; break;
                case PadKey.Enter: o.Vk = 0x0D; break;
                case PadKey.Close: o.Close = true; break;
            }
            return o;
        }
    }
}
