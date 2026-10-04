// ATLAS case contract (docs/case_contract.md, section 3): canonical JSON reader and writer.
// Plain C#, System only, no Unity references.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Atlas.Cases
{
    /// <summary>A file or value violates docs/case_contract.md.</summary>
    public sealed class ContractException : Exception
    {
        public ContractException(string message) : base(message) { }
    }

    /// <summary>
    /// JSON values are represented as: Dictionary&lt;string, object&gt; (object), List&lt;object&gt; (array),
    /// string, long (number token without '.', 'e', 'E'), double (any other number), bool, null.
    /// </summary>
    public static class CanonicalJson
    {
        const double FloatScale = 1e6;
        const double MaxMicro = 9007199254740992.0; // 2^53, exclusive bound on |round(v * 1e6)|

        // ---------------------------------------------------------------- writer

        /// <summary>Canonical bytes: UTF-8, no whitespace, keys sorted by code unit, one trailing LF.</summary>
        public static byte[] Write(object value)
        {
            var sb = new StringBuilder();
            Emit(value, sb);
            sb.Append('\n');
            return new UTF8Encoding(false, true).GetBytes(sb.ToString());
        }

        /// <summary>q = round-half-even(v * 1e6), written as fixed point without trailing zeros, at least one decimal.</summary>
        public static string FormatFloat(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new ContractException("NaN and infinity cannot be written to canonical JSON");
            double r = Math.Round(v * FloatScale, MidpointRounding.ToEven);
            if (Math.Abs(r) >= MaxMicro)
                throw new ContractException("float is outside the canonical range (|v| < 2^53 / 1e6)");
            long q = (long)r;
            bool neg = q < 0;
            ulong a = neg ? (ulong)(-q) : (ulong)q;
            ulong whole = a / 1000000UL;
            ulong frac = a % 1000000UL;
            string digits = frac.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            return (neg ? "-" : "") + whole.ToString(CultureInfo.InvariantCulture) + "." + digits;
        }

        static void Emit(object v, StringBuilder sb)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is string s) { EmitString(s, sb); return; }
            if (v is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
            if (v is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double d) { sb.Append(FormatFloat(d)); return; }
            if (v is float f) { sb.Append(FormatFloat(f)); return; }
            if (v is Dictionary<string, object> o)
            {
                var keys = new List<string>(o.Keys);
                keys.Sort(StringComparer.Ordinal);
                sb.Append('{');
                for (int k = 0; k < keys.Count; k++)
                {
                    CheckKey(keys[k]);
                    if (k > 0) sb.Append(',');
                    EmitString(keys[k], sb);
                    sb.Append(':');
                    Emit(o[keys[k]], sb);
                }
                sb.Append('}');
                return;
            }
            if (v is List<object> a)
            {
                sb.Append('[');
                for (int k = 0; k < a.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    Emit(a[k], sb);
                }
                sb.Append(']');
                return;
            }
            throw new ContractException("cannot write " + v.GetType().Name + " to canonical JSON");
        }

        static void CheckKey(string k)
        {
            if (k.Length == 0) throw new ContractException("object key must be non-empty printable ASCII");
            foreach (char c in k)
                if (c < ' ' || c > '~') throw new ContractException("object key must be non-empty printable ASCII: " + k);
        }

        static void EmitString(string s, StringBuilder sb)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else if (char.IsHighSurrogate(c))
                        {
                            if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                                throw new ContractException("lone surrogate in string");
                            sb.Append(c).Append(s[++i]);
                        }
                        else if (char.IsLowSurrogate(c))
                        {
                            throw new ContractException("lone surrogate in string");
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- reader

        /// <summary>Parse any valid JSON (not only the canonical form). Duplicate keys are rejected.</summary>
        public static object Parse(byte[] utf8)
        {
            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
                throw new ContractException("a byte-order mark is not allowed");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(utf8); }
            catch (ArgumentException) { throw new ContractException("invalid UTF-8"); }
            return Parse(text);
        }

        public static object Parse(string text)
        {
            int pos = 0;
            SkipWs(text, ref pos);
            object v = ParseValue(text, ref pos, 0);
            SkipWs(text, ref pos);
            if (pos != text.Length) throw Err("trailing characters", pos);
            return v;
        }

        static ContractException Err(string what, int pos)
        {
            return new ContractException("invalid JSON: " + what + " at offset " + pos.ToString(CultureInfo.InvariantCulture));
        }

        static void SkipWs(string t, ref int p)
        {
            while (p < t.Length && (t[p] == ' ' || t[p] == '\n' || t[p] == '\r' || t[p] == '\t')) p++;
        }

        static object ParseValue(string t, ref int p, int depth)
        {
            if (depth > 64) throw Err("nesting too deep", p);
            if (p >= t.Length) throw Err("unexpected end", p);
            char c = t[p];
            if (c == '{')
            {
                p++;
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWs(t, ref p);
                if (p < t.Length && t[p] == '}') { p++; return o; }
                while (true)
                {
                    SkipWs(t, ref p);
                    if (p >= t.Length || t[p] != '"') throw Err("expected a string key", p);
                    string key = ParseString(t, ref p);
                    SkipWs(t, ref p);
                    if (p >= t.Length || t[p] != ':') throw Err("expected ':'", p);
                    p++;
                    SkipWs(t, ref p);
                    object val = ParseValue(t, ref p, depth + 1);
                    if (o.ContainsKey(key)) throw new ContractException("duplicate object key " + key);
                    o[key] = val;
                    SkipWs(t, ref p);
                    if (p < t.Length && t[p] == ',') { p++; continue; }
                    if (p < t.Length && t[p] == '}') { p++; return o; }
                    throw Err("expected ',' or '}'", p);
                }
            }
            if (c == '[')
            {
                p++;
                var a = new List<object>();
                SkipWs(t, ref p);
                if (p < t.Length && t[p] == ']') { p++; return a; }
                while (true)
                {
                    SkipWs(t, ref p);
                    a.Add(ParseValue(t, ref p, depth + 1));
                    SkipWs(t, ref p);
                    if (p < t.Length && t[p] == ',') { p++; continue; }
                    if (p < t.Length && t[p] == ']') { p++; return a; }
                    throw Err("expected ',' or ']'", p);
                }
            }
            if (c == '"') return ParseString(t, ref p);
            if (Match(t, p, "true")) { p += 4; return true; }
            if (Match(t, p, "false")) { p += 5; return false; }
            if (Match(t, p, "null")) { p += 4; return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(t, ref p);
            throw Err("unexpected character", p);
        }

        static bool Match(string t, int p, string word)
        {
            return string.CompareOrdinal(t, p, word, 0, word.Length) == 0;
        }

        static object ParseNumber(string t, ref int p)
        {
            int start = p;
            bool isFloat = false;
            if (t[p] == '-') p++;
            if (p >= t.Length) throw Err("bad number", start);
            if (t[p] == '0') p++;
            else if (t[p] >= '1' && t[p] <= '9') { while (p < t.Length && t[p] >= '0' && t[p] <= '9') p++; }
            else throw Err("bad number", start);
            if (p < t.Length && t[p] == '.')
            {
                isFloat = true;
                p++;
                int d = p;
                while (p < t.Length && t[p] >= '0' && t[p] <= '9') p++;
                if (p == d) throw Err("bad number", start);
            }
            if (p < t.Length && (t[p] == 'e' || t[p] == 'E'))
            {
                isFloat = true;
                p++;
                if (p < t.Length && (t[p] == '+' || t[p] == '-')) p++;
                int d = p;
                while (p < t.Length && t[p] >= '0' && t[p] <= '9') p++;
                if (p == d) throw Err("bad number", start);
            }
            string tok = t.Substring(start, p - start);
            if (isFloat)
            {
                double v = double.Parse(tok, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (double.IsNaN(v) || double.IsInfinity(v)) throw Err("number out of range", start);
                return v;
            }
            long l;
            if (!long.TryParse(tok, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l))
                throw Err("integer outside the signed 64-bit range", start);
            return l;
        }

        static string ParseString(string t, ref int p)
        {
            int start = p;
            p++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (p >= t.Length) throw Err("unterminated string", start);
                char c = t[p++];
                if (c == '"') break;
                if (c < ' ') throw Err("control character in string", p - 1);
                if (c != '\\') { sb.Append(c); continue; }
                if (p >= t.Length) throw Err("unterminated string", start);
                char e = t[p++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (p + 4 > t.Length) throw Err("bad \\u escape", p);
                        int code = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            char h = t[p++];
                            int d = h >= '0' && h <= '9' ? h - '0'
                                  : h >= 'a' && h <= 'f' ? h - 'a' + 10
                                  : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                            if (d < 0) throw Err("bad \\u escape", p - 1);
                            code = code * 16 + d;
                        }
                        sb.Append((char)code);
                        break;
                    default: throw Err("bad escape", p - 1);
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- typed access helpers

        public static Dictionary<string, object> AsObject(object v, string what)
        {
            var o = v as Dictionary<string, object>;
            if (o == null) throw new ContractException(what + ": expected an object");
            return o;
        }

        public static List<object> AsArray(object v, string what)
        {
            var a = v as List<object>;
            if (a == null) throw new ContractException(what + ": expected an array");
            return a;
        }

        public static object Need(Dictionary<string, object> o, string key, string what)
        {
            object v;
            if (!o.TryGetValue(key, out v)) throw new ContractException(what + ": missing key " + key);
            return v;
        }

        public static string AsString(object v, string what)
        {
            var s = v as string;
            if (s == null) throw new ContractException(what + ": expected a string");
            return s;
        }

        public static long AsLong(object v, string what)
        {
            if (v is long l) return l;
            throw new ContractException(what + ": expected an integer");
        }

        public static double AsDouble(object v, string what)
        {
            if (v is double d) return d;
            if (v is long l) return l;
            throw new ContractException(what + ": expected a number");
        }

        public static double[] AsDoubles(object v, int n, string what)
        {
            var a = AsArray(v, what);
            if (a.Count != n) throw new ContractException(what + ": expected an array of " + n.ToString(CultureInfo.InvariantCulture) + " numbers");
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = AsDouble(a[i], what);
            return r;
        }

        public static double[][] AsDoubleRows(object v, int rows, int cols, string what)
        {
            var a = AsArray(v, what);
            if (a.Count != rows) throw new ContractException(what + ": expected " + rows.ToString(CultureInfo.InvariantCulture) + " rows");
            var r = new double[rows][];
            for (int i = 0; i < rows; i++) r[i] = AsDoubles(a[i], cols, what);
            return r;
        }

        public static List<object> FromDoubles(double[] v)
        {
            var a = new List<object>(v.Length);
            foreach (double d in v) a.Add(d);
            return a;
        }

        public static List<object> FromDoubleRows(double[][] v)
        {
            var a = new List<object>(v.Length);
            foreach (double[] row in v) a.Add(FromDoubles(row));
            return a;
        }

        public static void CheckKeys(Dictionary<string, object> o, string[] required, string[] optional, string what)
        {
            foreach (string k in required)
                if (!o.ContainsKey(k)) throw new ContractException(what + ": missing key " + k);
            foreach (string k in o.Keys)
                if (Array.IndexOf(required, k) < 0 && Array.IndexOf(optional, k) < 0)
                    throw new ContractException(what + ": unknown key " + k + " (put additions under \"extra\")");
        }
    }
}
