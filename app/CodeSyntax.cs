using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Peach
{
    internal enum Tok { Plain, Comment, String, Number, Keyword, Builtin, Variable, Property }

    internal struct Span
    {
        public int Start, Length;
        public Tok Kind;
        public Span(int start, int length, Tok kind) { Start = start; Length = length; Kind = kind; }
    }

    // Syntax colouring for the addon editor (pure: text in, spans out). Languages: js, ts, py, ps1, bat, vbs, json.
    internal static class CodeSyntax
    {
        public static string LangOf(string file)
        {
            switch ((Path.GetExtension(file) ?? "").ToLowerInvariant())
            {
                case ".js": case ".mjs": case ".cjs": return "js";
                case ".ts": return "ts";
                case ".py": return "py";
                case ".ps1": case ".psm1": return "ps1";
                case ".bat": case ".cmd": return "bat";
                case ".vbs": return "vbs";
                case ".json": return "json";
                default: return "txt";
            }
        }

        public static Color ColorOf(Tok t)
        {
            switch (t)
            {
                case Tok.Comment: return Color.FromArgb(120, 146, 108);
                case Tok.String: return Color.FromArgb(236, 192, 124);
                case Tok.Number: return Color.FromArgb(142, 204, 176);
                case Tok.Keyword: return Color.FromArgb(255, 132, 100);
                case Tok.Builtin: return Color.FromArgb(118, 190, 255);
                case Tok.Variable: return Color.FromArgb(190, 168, 238);
                case Tok.Property: return Color.FromArgb(150, 204, 232);
                default: return Theme.Ink;
            }
        }

        static HashSet<string> Set(string words, bool ignoreCase)
        {
            return new HashSet<string>(words.Split(' '), ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        static readonly HashSet<string> JsKeywords = Set("break case catch class const continue debugger default delete do else export extends finally for function if import in instanceof let new of return super switch this throw try typeof var void while with yield async await null true false undefined static get set from as type interface enum implements public private protected readonly declare namespace abstract", false);
        static readonly HashSet<string> PyKeywords = Set("and as assert async await break class continue def del elif else except finally for from global if import in is lambda None nonlocal not or pass raise return try while with yield True False match case self", false);
        static readonly HashSet<string> PsKeywords = Set("if elseif else switch foreach for while do until function filter param begin process end return break continue throw try catch finally in trap exit class enum using workflow parallel data dynamicparam default", true);
        static readonly HashSet<string> BatKeywords = Set("echo set setlocal endlocal if else for in do goto call exit pause rem start cd chdir dir copy move del type cls title not exist defined errorlevel equ neq lss leq gtr geq nul shift", true);
        static readonly HashSet<string> VbsKeywords = Set("Dim Set If Then Else ElseIf End Sub Function For Each Next While Wend Do Loop Until Select Case Call Exit Option Explicit True False Nothing Null Empty New And Or Not Xor Mod Is Const ReDim Preserve Private Public Class With On Error Resume Goto To Step In ByVal ByRef Rem", true);

        public static IEnumerable<string> Keywords(string lang)
        {
            switch (lang)
            {
                case "js": case "ts": return JsKeywords;
                case "py": return PyKeywords;
                case "ps1": return PsKeywords;
                case "bat": return BatKeywords;
                case "vbs": return VbsKeywords;
                default: return new string[0];
            }
        }

        // Colours a whole file. Very large files are left plain (the editor stays responsive).
        public static List<Span> Spans(string text, string lang)
        {
            List<Span> spans = new List<Span>();
            if (string.IsNullOrEmpty(text) || text.Length > 300000 || lang == "txt") return spans;
            int n = text.Length, i = 0;
            bool js = lang == "js" || lang == "ts", py = lang == "py", ps = lang == "ps1", bat = lang == "bat", vbs = lang == "vbs", json = lang == "json";
            HashSet<string> keywords = js ? JsKeywords : py ? PyKeywords : ps ? PsKeywords : bat ? BatKeywords : vbs ? VbsKeywords : null;

            while (i < n)
            {
                char c = text[i];

                // ---- comments ----
                if (js && c == '/' && i + 1 < n && text[i + 1] == '/') { int e = LineEnd(text, i); spans.Add(new Span(i, e - i, Tok.Comment)); i = e; continue; }
                if (js && c == '/' && i + 1 < n && text[i + 1] == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2; spans.Add(new Span(i, e - i, Tok.Comment)); i = e; continue; }
                if ((py || ps) && c == '#') { int e = LineEnd(text, i); spans.Add(new Span(i, e - i, Tok.Comment)); i = e; continue; }
                if (ps && c == '<' && i + 1 < n && text[i + 1] == '#') { int e = text.IndexOf("#>", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2; spans.Add(new Span(i, e - i, Tok.Comment)); i = e; continue; }
                if (vbs && c == '\'') { int e = LineEnd(text, i); spans.Add(new Span(i, e - i, Tok.Comment)); i = e; continue; }
                if (bat && c == ':' && i + 1 < n && text[i + 1] == ':' && AtLineStart(text, i)) { int e = LineEnd(text, i); spans.Add(new Span(i, e - i, Tok.Comment)); i = e; continue; }

                // ---- strings ----
                if (py && (c == '"' || c == '\'') && i + 2 < n && text[i + 1] == c && text[i + 2] == c)
                {
                    int e = text.IndexOf(new string(c, 3), i + 3, StringComparison.Ordinal); e = e < 0 ? n : e + 3;
                    spans.Add(new Span(i, e - i, Tok.String)); i = e; continue;
                }
                if (c == '"' || ((js || py || ps || json) && c == '\'') || (js && c == '`'))
                {
                    int e = StringEnd(text, i, c, ps || vbs || bat);
                    Tok kind = Tok.String;
                    if (json && c == '"') { int k = e; while (k < n && (text[k] == ' ' || text[k] == '\t')) k++; if (k < n && text[k] == ':') kind = Tok.Property; }
                    spans.Add(new Span(i, e - i, kind)); i = e; continue;
                }

                // ---- variables ----
                if (ps && c == '$') { int e = i + 1; while (e < n && (char.IsLetterOrDigit(text[e]) || text[e] == '_' || text[e] == ':' || text[e] == '{' || text[e] == '}')) e++; spans.Add(new Span(i, e - i, Tok.Variable)); i = e; continue; }
                if (bat && c == '%') { int e = text.IndexOf('%', i + 1); if (e > i && e - i < 60 && text.IndexOf('\n', i, e - i) < 0) { spans.Add(new Span(i, e - i + 1, Tok.Variable)); i = e + 1; continue; } }

                // ---- numbers ----
                if (char.IsDigit(c) && (i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')))
                {
                    int e = i + 1;
                    while (e < n && (char.IsLetterOrDigit(text[e]) || text[e] == '.' || text[e] == '_')) e++;
                    spans.Add(new Span(i, e - i, Tok.Number)); i = e; continue;
                }

                // ---- words ----
                if (char.IsLetter(c) || c == '_' || (ps && c == '-' && i + 1 < n && char.IsLetter(text[i + 1])))
                {
                    int e = i + 1;
                    while (e < n && (char.IsLetterOrDigit(text[e]) || text[e] == '_' || ((ps || bat) && text[e] == '-'))) e++;
                    string w = text.Substring(i, e - i);
                    if (bat && string.Equals(w, "rem", StringComparison.OrdinalIgnoreCase) && AtLineStart(text, i)) { int le = LineEnd(text, i); spans.Add(new Span(i, le - i, Tok.Comment)); i = le; continue; }
                    if (vbs && string.Equals(w, "rem", StringComparison.OrdinalIgnoreCase) && AtLineStart(text, i)) { int le = LineEnd(text, i); spans.Add(new Span(i, le - i, Tok.Comment)); i = le; continue; }
                    if ((js || py) && w == "peach") spans.Add(new Span(i, e - i, Tok.Builtin));
                    else if (ps && w.StartsWith("Peach-", StringComparison.OrdinalIgnoreCase)) spans.Add(new Span(i, e - i, Tok.Builtin));
                    else if (ps && w == "Invoke-Peach") spans.Add(new Span(i, e - i, Tok.Builtin));
                    else if (ps && w.IndexOf('-') > 0 && char.IsUpper(w[0])) spans.Add(new Span(i, e - i, Tok.Builtin));   // cmdlets: Get-Process ...
                    else if (keywords != null && keywords.Contains(w)) spans.Add(new Span(i, e - i, Tok.Keyword));
                    else if (js && (w == "console" || w == "process" || w == "require" || w == "module" || w == "Buffer" || w == "JSON" || w == "Math" || w == "Promise")) spans.Add(new Span(i, e - i, Tok.Builtin));
                    else if (py && (w == "print" || w == "len" || w == "range" || w == "open" || w == "str" || w == "int" || w == "list" || w == "dict" || w == "set")) spans.Add(new Span(i, e - i, Tok.Builtin));
                    else if (vbs && (w == "WScript" || w == "CreateObject" || w == "MsgBox")) spans.Add(new Span(i, e - i, Tok.Builtin));
                    i = e; continue;
                }
                i++;
            }
            return spans;
        }

        static int LineEnd(string t, int from) { int e = t.IndexOf('\n', from); return e < 0 ? t.Length : e; }

        static bool AtLineStart(string t, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                if (t[k] == '\n') return true;
                if (t[k] != ' ' && t[k] != '\t' && t[k] != '@') return false;
            }
            return true;
        }

        // end index (exclusive) of the string that starts at i. Backslash escapes unless the language uses doubled quotes.
        static int StringEnd(string t, int i, char q, bool noBackslash)
        {
            int n = t.Length, k = i + 1;
            while (k < n)
            {
                char c = t[k];
                if (c == '\n' && q != '`') return k;            // unterminated: stop at the line end
                if (!noBackslash && c == '\\') { k += 2; continue; }
                if (c == q)
                {
                    if (noBackslash && k + 1 < n && t[k + 1] == q) { k += 2; continue; }   // "" inside a VBScript/PowerShell string
                    return k + 1;
                }
                k++;
            }
            return n;
        }
    }
}
