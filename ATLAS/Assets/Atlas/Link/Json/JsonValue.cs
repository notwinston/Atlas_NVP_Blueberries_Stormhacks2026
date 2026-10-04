using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Atlas.Link
{
    /// <summary>The six JSON value kinds.</summary>
    public enum JsonKind : byte { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// One node of a JSON document: null, bool, number, string, array or object. Objects keep the order in which
    /// their keys were added, so a document written twice is byte-identical. Reading a missing key or index
    /// returns <see cref="Null"/>, which lets a chain such as <c>doc["a"][2]["b"].AsFloat(1f)</c> fall back
    /// without a null check. No reflection, so nothing here is at risk under IL2CPP stripping.
    /// </summary>
    public sealed class JsonValue
    {
        /// <summary>The shared null value. It is immutable: a null has no state to change.</summary>
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);

        static readonly JsonValue s_true = new JsonValue(JsonKind.Bool) { _bool = true };
        static readonly JsonValue s_false = new JsonValue(JsonKind.Bool);

        readonly JsonKind _kind;
        bool _bool;
        double _number;
        long _integer;
        bool _isInteger;            // the number was written without fraction or exponent and fits an Int64
        string _string;
        List<JsonValue> _items;     // array items, or object values in key order
        List<string> _keys;         // object keys in insertion order
        Dictionary<string, int> _index;

        JsonValue(JsonKind kind) { _kind = kind; }

        // ---- construction ------------------------------------------------------------------------------

        /// <summary>A new empty object.</summary>
        public static JsonValue Object()
        {
            return new JsonValue(JsonKind.Object)
            {
                _items = new List<JsonValue>(),
                _keys = new List<string>(),
                _index = new Dictionary<string, int>(StringComparer.Ordinal)
            };
        }

        /// <summary>A new empty array.</summary>
        public static JsonValue Array() { return new JsonValue(JsonKind.Array) { _items = new List<JsonValue>() }; }

        /// <summary>A new array holding the given items.</summary>
        public static JsonValue Array(params JsonValue[] items)
        {
            JsonValue a = Array();
            if (items != null) for (int i = 0; i < items.Length; i++) a.Add(items[i]);
            return a;
        }

        public static JsonValue From(bool value) { return value ? s_true : s_false; }

        public static JsonValue From(double value)
        {
            return new JsonValue(JsonKind.Number) { _number = value };
        }

        /// <summary>
        /// A number from a float. It is stored as the shortest decimal that reads back to the same float, so 0.1f
        /// is written as 0.1 and not as 0.10000000149011612.
        /// </summary>
        public static JsonValue From(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return From((double)value);
            double shortest = double.Parse(value.ToString("R", CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture);
            return new JsonValue(JsonKind.Number) { _number = shortest };
        }

        /// <summary>
        /// A number written with the caller's own text, for files that fix their number format (a RoomModel
        /// writes lengths with a set count of decimals so that its hash is stable). The text must be a JSON
        /// number; reads return what the text parses to.
        /// </summary>
        public static JsonValue Number(string text)
        {
            JsonValue parsed;
            if (text == null || !AtlasJson.TryParse(text, out parsed) || !parsed.IsNumber || text.Trim() != text)
                throw new FormatException("Not a JSON number: " + (text ?? "null"));
            return new JsonValue(JsonKind.Number)
            {
                _number = parsed._number, _integer = parsed._integer, _isInteger = parsed._isInteger, _string = text
            };
        }

        /// <summary>A number written with a .NET numeric format under the invariant culture, for example "F4".</summary>
        public static JsonValue Number(double value, string format)
        {
            return Number(value.ToString(format, CultureInfo.InvariantCulture));
        }

        public static JsonValue From(int value) { return From((long)value); }

        public static JsonValue From(long value)
        {
            return new JsonValue(JsonKind.Number) { _number = value, _integer = value, _isInteger = true };
        }

        public static JsonValue From(string value)
        {
            return value == null ? Null : new JsonValue(JsonKind.String) { _string = value };
        }

        public static implicit operator JsonValue(bool value) { return From(value); }
        public static implicit operator JsonValue(int value) { return From(value); }
        public static implicit operator JsonValue(long value) { return From(value); }
        public static implicit operator JsonValue(float value) { return From(value); }
        public static implicit operator JsonValue(double value) { return From(value); }
        public static implicit operator JsonValue(string value) { return From(value); }

        // ---- inspection --------------------------------------------------------------------------------

        public JsonKind Kind { get { return _kind; } }
        public bool IsNull { get { return _kind == JsonKind.Null; } }
        public bool IsBool { get { return _kind == JsonKind.Bool; } }
        public bool IsNumber { get { return _kind == JsonKind.Number; } }
        public bool IsString { get { return _kind == JsonKind.String; } }
        public bool IsArray { get { return _kind == JsonKind.Array; } }
        public bool IsObject { get { return _kind == JsonKind.Object; } }

        /// <summary>True when the value is a number written as an integer that fits an Int64.</summary>
        public bool IsInteger { get { return _kind == JsonKind.Number && _isInteger; } }

        /// <summary>Number of array items or object members; 0 for every other kind.</summary>
        public int Count { get { return _items != null ? _items.Count : 0; } }

        /// <summary>
        /// Member of an object by key. Reading a missing key, or reading from a value that is not an object,
        /// returns <see cref="Null"/>. Writing adds or replaces the member and throws if this is not an object.
        /// </summary>
        public JsonValue this[string key]
        {
            get
            {
                int i;
                if (_kind == JsonKind.Object && key != null && _index.TryGetValue(key, out i)) return _items[i];
                return Null;
            }
            set { Set(key, value); }
        }

        /// <summary>
        /// Item of an array by index (for an object: the value of the index-th member). Reading out of range
        /// returns <see cref="Null"/>. Writing replaces an existing item and throws if out of range.
        /// </summary>
        public JsonValue this[int index]
        {
            get { return _items != null && index >= 0 && index < _items.Count ? _items[index] : Null; }
            set
            {
                if (_items == null) throw new InvalidOperationException("JSON value is not an array or object");
                _items[index] = value ?? Null;
            }
        }

        /// <summary>Key of the index-th member of an object, in insertion order.</summary>
        public string KeyAt(int index)
        {
            if (_kind != JsonKind.Object) throw new InvalidOperationException("JSON value is not an object");
            return _keys[index];
        }

        public bool Has(string key) { return _kind == JsonKind.Object && key != null && _index.ContainsKey(key); }

        public bool TryGet(string key, out JsonValue value)
        {
            int i;
            if (_kind == JsonKind.Object && key != null && _index.TryGetValue(key, out i))
            {
                value = _items[i];
                return true;
            }
            value = Null;
            return false;
        }

        // ---- building ----------------------------------------------------------------------------------

        /// <summary>Adds or replaces a member of an object and returns this object, so calls can be chained.</summary>
        public JsonValue Set(string key, JsonValue value)
        {
            if (_kind != JsonKind.Object) throw new InvalidOperationException("JSON value is not an object");
            if (key == null) throw new ArgumentNullException("key");
            int i;
            if (_index.TryGetValue(key, out i)) _items[i] = value ?? Null;
            else
            {
                _index.Add(key, _items.Count);
                _keys.Add(key);
                _items.Add(value ?? Null);
            }
            return this;
        }

        /// <summary>Appends an item to an array and returns this array, so calls can be chained.</summary>
        public JsonValue Add(JsonValue item)
        {
            if (_kind != JsonKind.Array) throw new InvalidOperationException("JSON value is not an array");
            _items.Add(item ?? Null);
            return this;
        }

        /// <summary>Removes a member of an object. Returns false when the key is absent.</summary>
        public bool Remove(string key)
        {
            int at;
            if (_kind != JsonKind.Object || key == null || !_index.TryGetValue(key, out at)) return false;
            _items.RemoveAt(at);
            _keys.RemoveAt(at);
            _index.Remove(key);
            for (int i = at; i < _keys.Count; i++) _index[_keys[i]] = i;
            return true;
        }

        /// <summary>Removes every item of an array or member of an object.</summary>
        public void Clear()
        {
            if (_items == null) throw new InvalidOperationException("JSON value is not an array or object");
            _items.Clear();
            if (_keys != null) { _keys.Clear(); _index.Clear(); }
        }

        // ---- typed reads with fallbacks ----------------------------------------------------------------

        public bool AsBool(bool fallback = false) { return _kind == JsonKind.Bool ? _bool : fallback; }

        public double AsDouble(double fallback = 0.0) { return _kind == JsonKind.Number ? _number : fallback; }

        public float AsFloat(float fallback = 0f) { return _kind == JsonKind.Number ? (float)_number : fallback; }

        /// <summary>The number as an Int64. A fractional number is truncated towards zero; out of range clamps.</summary>
        public long AsLong(long fallback = 0L)
        {
            if (_kind != JsonKind.Number) return fallback;
            if (_isInteger) return _integer;
            if (double.IsNaN(_number)) return fallback;
            if (_number >= 9.2233720368547758E+18) return long.MaxValue;
            if (_number <= -9.2233720368547758E+18) return long.MinValue;
            return (long)_number;
        }

        public int AsInt(int fallback = 0)
        {
            if (_kind != JsonKind.Number) return fallback;
            long v = AsLong(fallback);
            return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
        }

        public string AsString(string fallback = null) { return _kind == JsonKind.String ? _string : fallback; }

        public bool GetBool(string key, bool fallback = false) { return this[key].AsBool(fallback); }
        public double GetDouble(string key, double fallback = 0.0) { return this[key].AsDouble(fallback); }
        public float GetFloat(string key, float fallback = 0f) { return this[key].AsFloat(fallback); }
        public long GetLong(string key, long fallback = 0L) { return this[key].AsLong(fallback); }
        public int GetInt(string key, int fallback = 0) { return this[key].AsInt(fallback); }
        public string GetString(string key, string fallback = null) { return this[key].AsString(fallback); }

        // ---- copying and comparing ---------------------------------------------------------------------

        /// <summary>A copy that shares nothing mutable with this value.</summary>
        public JsonValue Clone()
        {
            switch (_kind)
            {
                case JsonKind.Array:
                {
                    JsonValue a = Array();
                    for (int i = 0; i < _items.Count; i++) a._items.Add(_items[i].Clone());
                    return a;
                }
                case JsonKind.Object:
                {
                    JsonValue o = Object();
                    for (int i = 0; i < _items.Count; i++) o.Set(_keys[i], _items[i].Clone());
                    return o;
                }
                default:
                    return this; // scalars are immutable
            }
        }

        /// <summary>Structural equality. Object members are compared by key, whatever their order.</summary>
        public bool DeepEquals(JsonValue other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || other._kind != _kind) return false;
            switch (_kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Bool: return _bool == other._bool;
                case JsonKind.Number:
                    if (_isInteger && other._isInteger) return _integer == other._integer;
                    return _number.Equals(other._number);
                case JsonKind.String: return string.Equals(_string, other._string, StringComparison.Ordinal);
                case JsonKind.Array:
                    if (_items.Count != other._items.Count) return false;
                    for (int i = 0; i < _items.Count; i++)
                        if (!_items[i].DeepEquals(other._items[i])) return false;
                    return true;
                default:
                    if (_items.Count != other._items.Count) return false;
                    for (int i = 0; i < _items.Count; i++)
                    {
                        JsonValue theirs;
                        if (!other.TryGet(_keys[i], out theirs) || !_items[i].DeepEquals(theirs)) return false;
                    }
                    return true;
            }
        }

        // ---- text --------------------------------------------------------------------------------------

        /// <summary>See <see cref="AtlasJson.Parse(string)"/>.</summary>
        public static JsonValue Parse(string json) { return AtlasJson.Parse(json); }

        /// <summary>See <see cref="AtlasJson.TryParse(string, out JsonValue)"/>.</summary>
        public static bool TryParse(string json, out JsonValue value) { return AtlasJson.TryParse(json, out value); }

        /// <summary>
        /// Writes the value as JSON text. Compact output has no whitespace at all (one line, fit for JSON lines);
        /// indented output uses two spaces and "\n".
        /// </summary>
        public string ToJson(bool indent = false)
        {
            var sb = new StringBuilder(256);
            WriteTo(sb, indent);
            return sb.ToString();
        }

        /// <summary>Appends the value as JSON text to a builder the caller owns.</summary>
        public void WriteTo(StringBuilder sb, bool indent = false)
        {
            if (sb == null) throw new ArgumentNullException("sb");
            Write(sb, indent, 0);
        }

        public override string ToString() { return ToJson(false); }

        void Write(StringBuilder sb, bool indent, int depth)
        {
            switch (_kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(_bool ? "true" : "false"); break;
                case JsonKind.Number: WriteNumber(sb); break;
                case JsonKind.String: AtlasJson.AppendQuoted(sb, _string); break;
                case JsonKind.Array:
                    if (_items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (indent) NewLine(sb, depth + 1);
                        _items[i].Write(sb, indent, depth + 1);
                    }
                    if (indent) NewLine(sb, depth);
                    sb.Append(']');
                    break;
                default:
                    if (_items.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (indent) NewLine(sb, depth + 1);
                        AtlasJson.AppendQuoted(sb, _keys[i]);
                        sb.Append(indent ? ": " : ":");
                        _items[i].Write(sb, indent, depth + 1);
                    }
                    if (indent) NewLine(sb, depth);
                    sb.Append('}');
                    break;
            }
        }

        void WriteNumber(StringBuilder sb)
        {
            if (_string != null) { sb.Append(_string); return; } // the caller's own formatting
            if (_isInteger) { sb.Append(_integer.ToString(CultureInfo.InvariantCulture)); return; }
            // JSON has no NaN or infinity; null is the conventional stand-in and reads back as a fallback.
            if (double.IsNaN(_number) || double.IsInfinity(_number)) { sb.Append("null"); return; }
            // "R" is the shortest text that parses back to the same double, on Mono and on .NET alike.
            string text = _number.ToString("R", CultureInfo.InvariantCulture);
            sb.Append(text);
            // Keep a whole-valued double recognisable as a double so that it does not come back as an integer.
            if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0) sb.Append(".0");
        }

        static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }
    }
}
