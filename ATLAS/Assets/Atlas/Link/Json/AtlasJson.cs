using System;
using System.Globalization;
using System.Text;

namespace Atlas.Link
{
    /// <summary>
    /// The small engine-free JSON reader and writer of ATLAS (contracts F1). It serves link events, parameter
    /// snapshots, the JSON link messages of Appendix B and RoomModel files. Strict RFC 8259 input, invariant
    /// culture, exact round trip of doubles and of 64-bit integers, and no reflection.
    /// </summary>
    public static class AtlasJson
    {
        /// <summary>Deepest nesting the reader accepts. A deeper document is rejected instead of overflowing the stack.</summary>
        public const int MaxDepth = 128;

        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false, false);

        /// <summary>Parses a JSON document. Throws <see cref="FormatException"/> with the position of the fault.</summary>
        public static JsonValue Parse(string json)
        {
            JsonValue value;
            string error;
            if (!TryParse(json, out value, out error)) throw new FormatException(error);
            return value;
        }

        /// <summary>Parses a JSON document. Returns false, and <see cref="JsonValue.Null"/>, when it is malformed.</summary>
        public static bool TryParse(string json, out JsonValue value)
        {
            string error;
            return TryParse(json, out value, out error);
        }

        /// <summary>Parses a JSON document and reports what was wrong when it is malformed.</summary>
        public static bool TryParse(string json, out JsonValue value, out string error)
        {
            return TryParse(json, false, out value, out error);
        }

        /// <summary>
        /// Parses the first JSON value of the text. With <paramref name="allowTrailing"/> anything after that value
        /// is ignored, which is how link receivers treat trailing bytes (Master Plan section 7.5, rule 5).
        /// </summary>
        public static bool TryParse(string json, bool allowTrailing, out JsonValue value, out string error)
        {
            value = JsonValue.Null;
            if (json == null) { error = "JSON text is null"; return false; }
            var reader = new Reader(json);
            if (!reader.ReadValue(0, out value))
            {
                value = JsonValue.Null;
                error = reader.Error;
                return false;
            }
            reader.SkipWhitespace();
            if (!allowTrailing && !reader.AtEnd)
            {
                value = JsonValue.Null;
                error = "Unexpected text after the JSON value at character " +
                        reader.Position.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>Parses UTF-8 bytes. A byte-order mark is skipped.</summary>
        public static bool TryParseUtf8(byte[] buffer, int offset, int length, bool allowTrailing,
            out JsonValue value, out string error)
        {
            value = JsonValue.Null;
            if (buffer == null || offset < 0 || length < 0 || offset > buffer.Length - length)
            {
                error = "JSON buffer range is invalid";
                return false;
            }
            if (length >= 3 && buffer[offset] == 0xEF && buffer[offset + 1] == 0xBB && buffer[offset + 2] == 0xBF)
            {
                offset += 3;
                length -= 3;
            }
            return TryParse(s_utf8.GetString(buffer, offset, length), allowTrailing, out value, out error);
        }

        /// <summary>Parses a whole UTF-8 file image. Throws <see cref="FormatException"/> when it is malformed.</summary>
        public static JsonValue ParseUtf8(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            JsonValue value;
            string error;
            if (!TryParseUtf8(bytes, 0, bytes.Length, false, out value, out error)) throw new FormatException(error);
            return value;
        }

        /// <summary>Writes a value as JSON text; a null reference is written as <c>null</c>.</summary>
        public static string ToJson(JsonValue value, bool indent = false)
        {
            return (value ?? JsonValue.Null).ToJson(indent);
        }

        /// <summary>Writes a value as UTF-8 JSON with no byte-order mark.</summary>
        public static byte[] ToUtf8(JsonValue value, bool indent = false)
        {
            return s_utf8.GetBytes(ToJson(value, indent));
        }

        /// <summary>The text as a quoted JSON string literal.</summary>
        public static string Quote(string text)
        {
            var sb = new StringBuilder((text != null ? text.Length : 4) + 2);
            AppendQuoted(sb, text);
            return sb.ToString();
        }

        /// <summary>Appends the text as a quoted JSON string literal; a null reference is written as <c>null</c>.</summary>
        public static void AppendQuoted(StringBuilder sb, string text)
        {
            if (text == null) { sb.Append("null"); return; }
            sb.Append('"');
            int runStart = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string escape = null;
                switch (c)
                {
                    case '"': escape = "\\\""; break;
                    case '\\': escape = "\\\\"; break;
                    case '\b': escape = "\\b"; break;
                    case '\f': escape = "\\f"; break;
                    case '\n': escape = "\\n"; break;
                    case '\r': escape = "\\r"; break;
                    case '\t': escape = "\\t"; break;
                    default:
                        // Control characters must be escaped. The two Unicode line separators are escaped as well
                        // so that one event stays on one line for every JSON-lines consumer.
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                        {
                            sb.Append(text, runStart, i - runStart);
                            sb.Append("\\u");
                            AppendHex4(sb, c);
                            runStart = i + 1;
                        }
                        continue;
                }
                sb.Append(text, runStart, i - runStart);
                sb.Append(escape);
                runStart = i + 1;
            }
            sb.Append(text, runStart, text.Length - runStart);
            sb.Append('"');
        }

        static void AppendHex4(StringBuilder sb, int c)
        {
            const string digits = "0123456789abcdef";
            sb.Append(digits[(c >> 12) & 15]);
            sb.Append(digits[(c >> 8) & 15]);
            sb.Append(digits[(c >> 4) & 15]);
            sb.Append(digits[c & 15]);
        }

        /// <summary>Recursive-descent reader over a string.</summary>
        struct Reader
        {
            readonly string _s;
            int _i;
            StringBuilder _scratch;
            public string Error;

            public Reader(string s) { _s = s; _i = 0; _scratch = null; Error = null; }

            public int Position { get { return _i; } }
            public bool AtEnd { get { return _i >= _s.Length; } }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
                    _i++;
                }
            }

            bool Fail(string what)
            {
                Error = what + " at character " + _i.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            public bool ReadValue(int depth, out JsonValue value)
            {
                value = JsonValue.Null;
                if (depth > MaxDepth) return Fail("JSON nesting is deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture));
                SkipWhitespace();
                if (_i >= _s.Length) return Fail("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth, out value);
                    case '[': return ReadArray(depth, out value);
                    case '"':
                    {
                        string text;
                        if (!ReadString(out text)) return false;
                        value = JsonValue.From(text);
                        return true;
                    }
                    case 't': return ReadLiteral("true", JsonValue.From(true), out value);
                    case 'f': return ReadLiteral("false", JsonValue.From(false), out value);
                    case 'n': return ReadLiteral("null", JsonValue.Null, out value);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(out value);
                        return Fail("Unexpected character '" + c + "'");
                }
            }

            bool ReadLiteral(string word, JsonValue result, out JsonValue value)
            {
                value = JsonValue.Null;
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return Fail("Invalid literal");
                _i += word.Length;
                value = result;
                return true;
            }

            bool ReadObject(int depth, out JsonValue value)
            {
                value = JsonValue.Object();
                _i++; // {
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == '}') { _i++; return true; }
                while (true)
                {
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != '"') return Fail("Expected a member name");
                    string key;
                    if (!ReadString(out key)) return false;
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != ':') return Fail("Expected ':'");
                    _i++;
                    JsonValue member;
                    if (!ReadValue(depth + 1, out member)) return false;
                    value.Set(key, member); // a repeated key keeps its last value
                    SkipWhitespace();
                    if (_i >= _s.Length) return Fail("Unterminated object");
                    char c = _s[_i++];
                    if (c == '}') return true;
                    if (c != ',') { _i--; return Fail("Expected ',' or '}'"); }
                }
            }

            bool ReadArray(int depth, out JsonValue value)
            {
                value = JsonValue.Array();
                _i++; // [
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == ']') { _i++; return true; }
                while (true)
                {
                    JsonValue item;
                    if (!ReadValue(depth + 1, out item)) return false;
                    value.Add(item);
                    SkipWhitespace();
                    if (_i >= _s.Length) return Fail("Unterminated array");
                    char c = _s[_i++];
                    if (c == ']') return true;
                    if (c != ',') { _i--; return Fail("Expected ',' or ']'"); }
                }
            }

            bool ReadString(out string text)
            {
                text = null;
                int start = ++_i; // opening quote
                // Fast path: no escape before the closing quote, so the text is one substring.
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '"') { text = _s.Substring(start, _i - start); _i++; return true; }
                    if (c == '\\') break;
                    if (c < 0x20) return Fail("Control character in a string");
                    _i++;
                }
                if (_i >= _s.Length) return Fail("Unterminated string");

                if (_scratch == null) _scratch = new StringBuilder(64);
                StringBuilder sb = _scratch;
                sb.Length = 0;
                sb.Append(_s, start, _i - start);
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"') { text = sb.ToString(); return true; }
                    if (c < 0x20) { _i--; return Fail("Control character in a string"); }
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) break;
                    char e = _s[_i++];
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
                        {
                            // A surrogate pair arrives as two \u escapes and is appended as two UTF-16 units.
                            if (_i + 4 > _s.Length) return Fail("Truncated \\u escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int h = HexValue(_s[_i + k]);
                                if (h < 0) { _i += k; return Fail("Invalid \\u escape"); }
                                code = (code << 4) | h;
                            }
                            _i += 4;
                            sb.Append((char)code);
                            break;
                        }
                        default:
                            _i--;
                            return Fail("Invalid escape");
                    }
                }
                return Fail("Unterminated string");
            }

            static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            bool ReadNumber(out JsonValue value)
            {
                value = JsonValue.Null;
                int start = _i;
                bool integral = true;
                if (_s[_i] == '-') _i++;
                if (_i >= _s.Length || _s[_i] < '0' || _s[_i] > '9') return Fail("Invalid number");
                if (_s[_i] == '0') _i++;
                else while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                if (_i < _s.Length && _s[_i] == '.')
                {
                    integral = false;
                    _i++;
                    if (_i >= _s.Length || _s[_i] < '0' || _s[_i] > '9') return Fail("Invalid number");
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    integral = false;
                    _i++;
                    if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (_i >= _s.Length || _s[_i] < '0' || _s[_i] > '9') return Fail("Invalid number");
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }

                string token = _s.Substring(start, _i - start);
                long whole;
                // "-0" is a double: as a whole number it would lose its sign.
                if (integral && token != "-0" && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole))
                {
                    value = JsonValue.From(whole);
                    return true;
                }
                double number;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    _i = start;
                    return Fail("Invalid number");
                }
                // Mono's parser returns +0 for "-0.0" and for "-1e-400"; the sign is part of the number.
                if (number == 0.0 && token[0] == '-') number = -0.0;
                value = JsonValue.From(number);
                return true;
            }
        }
    }
}
