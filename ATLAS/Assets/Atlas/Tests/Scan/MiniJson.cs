using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Atlas.Scan.Tests
{
    /// <summary>
    /// A few lines of JSON reading for the RoomModel fixtures: objects become dictionaries, arrays become lists,
    /// numbers become doubles. Test-only; the app reads RoomModels through the link's JSON reader.
    /// </summary>
    static class MiniJson
    {
        public static object Parse(string text)
        {
            int at = 0;
            object value = Value(text, ref at);
            Skip(text, ref at);
            if (at != text.Length) throw new FormatException("Trailing text at " + at);
            return value;
        }

        public static Dictionary<string, object> Obj(object node, string key) => (Dictionary<string, object>)((Dictionary<string, object>)node)[key];
        public static List<object> List(object node, string key) => (List<object>)((Dictionary<string, object>)node)[key];
        public static double Num(object node, string key) => (double)((Dictionary<string, object>)node)[key];
        public static double Num(object list, int index) => (double)((List<object>)list)[index];

        static void Skip(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
        }

        static object Value(string s, ref int at)
        {
            Skip(s, ref at);
            char c = s[at];
            if (c == '{')
            {
                var map = new Dictionary<string, object>();
                at++;
                Skip(s, ref at);
                if (s[at] == '}') { at++; return map; }
                while (true)
                {
                    Skip(s, ref at);
                    string key = Text(s, ref at);
                    Skip(s, ref at);
                    if (s[at++] != ':') throw new FormatException("':' expected at " + at);
                    map[key] = Value(s, ref at);
                    Skip(s, ref at);
                    char sep = s[at++];
                    if (sep == '}') return map;
                    if (sep != ',') throw new FormatException("',' expected at " + at);
                }
            }
            if (c == '[')
            {
                var list = new List<object>();
                at++;
                Skip(s, ref at);
                if (s[at] == ']') { at++; return list; }
                while (true)
                {
                    list.Add(Value(s, ref at));
                    Skip(s, ref at);
                    char sep = s[at++];
                    if (sep == ']') return list;
                    if (sep != ',') throw new FormatException("',' expected at " + at);
                }
            }
            if (c == '"') return Text(s, ref at);
            if (string.CompareOrdinal(s, at, "true", 0, 4) == 0) { at += 4; return true; }
            if (string.CompareOrdinal(s, at, "false", 0, 5) == 0) { at += 5; return false; }
            if (string.CompareOrdinal(s, at, "null", 0, 4) == 0) { at += 4; return null; }
            int start = at;
            while (at < s.Length && "+-0123456789.eE".IndexOf(s[at]) >= 0) at++;
            return double.Parse(s.Substring(start, at - start), CultureInfo.InvariantCulture);
        }

        static string Text(string s, ref int at)
        {
            if (s[at++] != '"') throw new FormatException("string expected at " + at);
            var sb = new StringBuilder();
            while (s[at] != '"')
            {
                char c = s[at++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[at++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); at += 4; break;
                    default: sb.Append(e); break;
                }
            }
            at++;
            return sb.ToString();
        }
    }
}
