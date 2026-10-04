using System;
using System.Collections.Generic;
using System.Globalization;

namespace Atlas.Link
{
    /// <summary>What a parameter holds. Every kind travels as one f32 in <c>ParamSet</c> (Appendix B.2).</summary>
    public enum ParamKind : byte
    {
        Float = 0,
        Int = 1,
        Bool = 2,
        /// <summary>An integer 0..n-1 with one display name per value.</summary>
        Enum = 3
    }

    /// <summary>Who changed a parameter. The link echoes and the phone logs every change with its source.</summary>
    public enum ParamSource : byte
    {
        /// <summary>Code on the phone or the on-device menu.</summary>
        Local = 0,
        /// <summary>A <c>ParamSet</c> from the Station.</summary>
        Remote = 1,
        /// <summary>A snapshot or preset being restored.</summary>
        Restore = 2
    }

    /// <summary>Called after a parameter took a new value.</summary>
    public delegate void ParamChangedHandler(ParamEntry entry, ParamSource source);

    /// <summary>
    /// A cheap, copyable reference to one parameter of one registry: its wire id. Reading through a handle is an
    /// array index and allocates nothing.
    /// </summary>
    public readonly struct ParamHandle : IEquatable<ParamHandle>
    {
        readonly int _idPlusOne; // default(ParamHandle) is invalid

        internal ParamHandle(ushort id) { _idPlusOne = id + 1; }

        public bool IsValid { get { return _idPlusOne > 0; } }

        /// <summary>The wire id (<c>ParamSet.id</c>). Only meaningful when <see cref="IsValid"/>.</summary>
        public ushort Id { get { return (ushort)(_idPlusOne - 1); } }

        public bool Equals(ParamHandle other) { return _idPlusOne == other._idPlusOne; }
        public override bool Equals(object obj) { return obj is ParamHandle && Equals((ParamHandle)obj); }
        public override int GetHashCode() { return _idPlusOne; }
    }

    /// <summary>
    /// One declared parameter: identity, range, default and current value. Hold on to the entry returned by a
    /// <c>Declare</c> call and read <see cref="Float"/>, <see cref="Int"/> or <see cref="Bool"/> on the frame path;
    /// each is a field read.
    /// </summary>
    public sealed class ParamEntry
    {
        internal float _value;
        internal int _int;
        internal float _min, _max, _default;
        internal Action<float> _onFloat;
        internal Action<int> _onInt;
        internal Action<bool> _onBool;
        internal string[] _names;

        internal ParamEntry(ParamRegistry registry, ushort id, string name, string group, ParamKind kind, bool onRelease)
        {
            Registry = registry;
            Id = id;
            Name = name;
            Group = group;
            Kind = kind;
            OnRelease = onRelease;
        }

        public ParamRegistry Registry { get; private set; }

        /// <summary>Wire id: the order of declaration, starting at 0 (Master Plan section 7.3.2).</summary>
        public ushort Id { get; private set; }

        /// <summary>Dotted name, unique in the registry, for example <c>curved.cullRho</c>.</summary>
        public string Name { get; private set; }

        /// <summary>Group shown as a heading on the Console and in the on-device menu.</summary>
        public string Group { get; private set; }

        public ParamKind Kind { get; private set; }

        /// <summary>The Console sends the value when the slider is released, because applying it regenerates geometry.</summary>
        public bool OnRelease { get; internal set; }

        public float Min { get { return _min; } }
        public float Max { get { return _max; } }
        public float Default { get { return _default; } }

        /// <summary>The current value as it travels on the wire.</summary>
        public float Value { get { return _value; } }

        public float Float { get { return _value; } }
        public int Int { get { return _int; } }
        public bool Bool { get { return _int != 0; } }

        public ParamHandle Handle { get { return new ParamHandle(Id); } }

        /// <summary>
        /// False while the entry's group is marked inactive (the space that owns it is not entered). An inactive
        /// parameter keeps its value and can still be set; the Console and the menu show its group folded.
        /// </summary>
        public bool Active { get; internal set; }

        /// <summary>Number of named values of an enum parameter; 0 for the other kinds.</summary>
        public int EnumCount { get { return _names != null ? _names.Length : 0; } }

        /// <summary>Display name of one value of an enum parameter, or null when out of range.</summary>
        public string EnumName(int value)
        {
            return _names != null && value >= 0 && value < _names.Length ? _names[value] : null;
        }

        /// <summary>Display name of the current value of an enum parameter, or null for the other kinds.</summary>
        public string CurrentName { get { return EnumName(_int); } }

        /// <summary>Sets the value, clamped to the range. Returns true when the value changed. Main thread only.</summary>
        public bool Set(float value, ParamSource source = ParamSource.Local) { return Registry.Set(this, value, source); }

        public bool Set(int value, ParamSource source = ParamSource.Local) { return Registry.Set(this, (float)value, source); }

        public bool Set(bool value, ParamSource source = ParamSource.Local) { return Registry.Set(this, value ? 1f : 0f, source); }

        /// <summary>Puts the value back to its default.</summary>
        public bool Reset(ParamSource source = ParamSource.Local) { return Registry.Set(this, _default, source); }

        /// <summary>The value this entry would take for a requested one: clamped, and rounded for discrete kinds.</summary>
        public float Sanitise(float requested)
        {
            if (float.IsNaN(requested)) return _value;
            float v = requested;
            if (Kind == ParamKind.Bool) return v >= 0.5f ? 1f : 0f;
            if (Kind != ParamKind.Float) v = (float)Math.Round(v, MidpointRounding.AwayFromZero);
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            return v;
        }
    }

    /// <summary>
    /// The engine-free half of the parameter registry (Master Plan sections 7.1 and 7.3.2, contracts F6): ids,
    /// ranges, defaults, current values and the clamp. Every tunable of ATLAS is declared here once; the Console
    /// builds its sliders from the manifest and the on-device menu lists the same entries.
    /// <para>
    /// Thread rule: a registry is created, declared into, set and read on the main thread only. The link queues
    /// what it receives and applies it from <c>Poll()</c> on that thread.
    /// </para>
    /// </summary>
    public sealed class ParamRegistry
    {
        /// <summary>Most entries one <c>ParamSet</c> message can carry (its count is one byte).</summary>
        public const int MaxPerMessage = 255;

        readonly List<ParamEntry> _entries = new List<ParamEntry>(64);
        readonly Dictionary<string, ParamEntry> _byName = new Dictionary<string, ParamEntry>(64, StringComparer.Ordinal);

        readonly HashSet<string> _inactiveGroups = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Raised when a group is marked active or inactive, with (group, active).</summary>
        public event Action<string, bool> GroupActiveChanged;

        /// <summary>Raised after any parameter took a new value, after the entry's own callback.</summary>
        public event ParamChangedHandler Changed;

        /// <summary>Raised when an entry is declared for the first time, so a UI or the link can refresh its list.</summary>
        public event Action<ParamEntry> Declared;

        /// <summary>Goes up by one on every value change. A UI compares it to skip redrawing an unchanged list.</summary>
        public int Version { get; private set; }

        /// <summary>Goes up by one whenever an entry is declared or its range changes: the manifest is stale.</summary>
        public int ManifestVersion { get; private set; }

        // ---- declaring ---------------------------------------------------------------------------------

        /// <summary>
        /// Declares a float parameter. Declaring a name again returns the existing entry with its current value,
        /// takes the new range, default and callback, and re-clamps; so a space may declare in every
        /// <c>Configure</c>. Declaring it with another kind throws.
        /// </summary>
        public ParamEntry DeclareFloat(string name, string group, float min, float max, float defaultValue,
            Action<float> onChanged = null, bool onRelease = false)
        {
            ParamEntry e = Declare(name, group, ParamKind.Float, min, max, defaultValue, null, onRelease);
            e._onFloat = onChanged;
            Reclamp(e);
            return e;
        }

        /// <summary>Declares an integer parameter. See <see cref="DeclareFloat"/> for redeclaration.</summary>
        public ParamEntry DeclareInt(string name, string group, int min, int max, int defaultValue,
            Action<int> onChanged = null, bool onRelease = false)
        {
            ParamEntry e = Declare(name, group, ParamKind.Int, min, max, defaultValue, null, onRelease);
            e._onInt = onChanged;
            Reclamp(e);
            return e;
        }

        /// <summary>Declares an on/off parameter. See <see cref="DeclareFloat"/> for redeclaration.</summary>
        public ParamEntry DeclareBool(string name, string group, bool defaultValue,
            Action<bool> onChanged = null, bool onRelease = false)
        {
            ParamEntry e = Declare(name, group, ParamKind.Bool, 0f, 1f, defaultValue ? 1f : 0f, null, onRelease);
            e._onBool = onChanged;
            Reclamp(e);
            return e;
        }

        /// <summary>
        /// Declares a parameter that takes one of a few named values, stored as the index 0..n-1 into
        /// <paramref name="names"/>. See <see cref="DeclareFloat"/> for redeclaration.
        /// </summary>
        public ParamEntry DeclareEnum(string name, string group, string[] names, int defaultValue,
            Action<int> onChanged = null, bool onRelease = false)
        {
            if (names == null || names.Length == 0) throw new ArgumentException("An enum parameter needs at least one name", "names");
            ParamEntry e = Declare(name, group, ParamKind.Enum, 0f, names.Length - 1, defaultValue,
                (string[])names.Clone(), onRelease);
            e._onInt = onChanged;
            Reclamp(e);
            return e;
        }

        ParamEntry Declare(string name, string group, ParamKind kind, float min, float max, float def,
            string[] names, bool onRelease)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A parameter needs a name", "name");
            if (float.IsNaN(min) || float.IsNaN(max) || float.IsNaN(def)) throw new ArgumentException("Range and default must be numbers", "name");
            if (min > max) throw new ArgumentException("Parameter '" + name + "': min is above max", "min");

            ParamEntry e;
            bool isNew = !_byName.TryGetValue(name, out e);
            if (isNew)
            {
                if (_entries.Count > ushort.MaxValue) throw new InvalidOperationException("The registry is full");
                e = new ParamEntry(this, (ushort)_entries.Count, name, group ?? string.Empty, kind, onRelease);
            }
            else if (e.Kind != kind)
            {
                throw new InvalidOperationException("Parameter '" + name + "' is already declared as " + e.Kind);
            }

            bool rangeChanged = isNew || e._min != min || e._max != max || e._default != def ||
                                e.OnRelease != onRelease || !SameNames(e._names, names);
            e._min = min;
            e._max = max;
            e._names = names;
            e.OnRelease = onRelease;
            e._default = def;          // Sanitise needs the range first
            e._default = SanitiseDefault(e, def);

            if (isNew)
            {
                e._value = e._default;
                e._int = (int)e._default;
                e.Active = !_inactiveGroups.Contains(e.Group);
                _entries.Add(e);
                _byName.Add(name, e);
                ManifestVersion++;
                Action<ParamEntry> declared = Declared;
                if (declared != null) declared(e);
            }
            else
            {
                if (rangeChanged) ManifestVersion++;
                // The previous owner of the entry may be gone (a space that was unloaded): drop its callbacks.
                e._onFloat = null;
                e._onInt = null;
                e._onBool = null;
            }
            return e;
        }

        // The current value survives a redeclaration but must still respect the new range. Runs after the new
        // callback is in place, so the new owner hears about the clamp.
        void Reclamp(ParamEntry e)
        {
            float clamped = e.Sanitise(e._value);
            if (clamped != e._value) Store(e, clamped, ParamSource.Local);
        }

        static float SanitiseDefault(ParamEntry e, float def)
        {
            float v = def;
            if (e.Kind == ParamKind.Bool) return v >= 0.5f ? 1f : 0f;
            if (e.Kind != ParamKind.Float) v = (float)Math.Round(v, MidpointRounding.AwayFromZero);
            return v < e._min ? e._min : v > e._max ? e._max : v;
        }

        static bool SameNames(string[] a, string[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // ---- enumeration -------------------------------------------------------------------------------

        public int Count { get { return _entries.Count; } }

        /// <summary>Entry by position, which is also its wire id.</summary>
        public ParamEntry this[int index] { get { return _entries[index]; } }

        /// <summary>Every entry in declaration order. Enumerating the list with a for loop allocates nothing.</summary>
        public IReadOnlyList<ParamEntry> Entries { get { return _entries; } }

        /// <summary>Entry by name, or null.</summary>
        public ParamEntry Find(string name)
        {
            ParamEntry e;
            return name != null && _byName.TryGetValue(name, out e) ? e : null;
        }

        public bool TryGet(string name, out ParamEntry entry)
        {
            entry = null;
            return name != null && _byName.TryGetValue(name, out entry);
        }

        public bool TryGet(ushort id, out ParamEntry entry)
        {
            entry = id < _entries.Count ? _entries[id] : null;
            return entry != null;
        }

        /// <summary>Entry behind a handle, or null when the handle is invalid for this registry.</summary>
        public ParamEntry Get(ParamHandle handle)
        {
            return handle.IsValid && handle.Id < _entries.Count ? _entries[handle.Id] : null;
        }

        /// <summary>Handle of a declared name; an invalid handle when the name is unknown.</summary>
        public ParamHandle HandleOf(string name)
        {
            ParamEntry e = Find(name);
            return e != null ? e.Handle : default(ParamHandle);
        }

        /// <summary>Fills a list with the distinct group names in order of first declaration.</summary>
        public void GetGroups(List<string> into)
        {
            if (into == null) throw new ArgumentNullException("into");
            into.Clear();
            for (int i = 0; i < _entries.Count; i++)
                if (!into.Contains(_entries[i].Group)) into.Add(_entries[i].Group);
        }

        /// <summary>
        /// Marks a group active or inactive. Values are untouched: SpaceLab marks a space's group inactive on
        /// Exit and active on Enter, and a UI folds an inactive group. Groups are active until told otherwise.
        /// </summary>
        public void SetGroupActive(string group, bool active)
        {
            if (group == null) return;
            bool changed = active ? _inactiveGroups.Remove(group) : _inactiveGroups.Add(group);
            if (!changed) return;
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].Group, group, StringComparison.Ordinal)) _entries[i].Active = active;
            ManifestVersion++;
            Action<string, bool> handler = GroupActiveChanged;
            if (handler != null) handler(group, active);
        }

        public bool IsGroupActive(string group) { return group == null || !_inactiveGroups.Contains(group); }

        // ---- typed reads (allocation-free) -------------------------------------------------------------

        public float GetFloat(ParamHandle handle, float fallback = 0f)
        {
            return handle.IsValid && handle.Id < _entries.Count ? _entries[handle.Id]._value : fallback;
        }

        public int GetInt(ParamHandle handle, int fallback = 0)
        {
            return handle.IsValid && handle.Id < _entries.Count ? _entries[handle.Id]._int : fallback;
        }

        public bool GetBool(ParamHandle handle, bool fallback = false)
        {
            return handle.IsValid && handle.Id < _entries.Count ? _entries[handle.Id]._int != 0 : fallback;
        }

        /// <summary>Read by name: a dictionary lookup, for set-up code and tools. Use a handle or the entry per frame.</summary>
        public float GetFloat(string name, float fallback = 0f)
        {
            ParamEntry e = Find(name);
            return e != null ? e._value : fallback;
        }

        public int GetInt(string name, int fallback = 0)
        {
            ParamEntry e = Find(name);
            return e != null ? e._int : fallback;
        }

        public bool GetBool(string name, bool fallback = false)
        {
            ParamEntry e = Find(name);
            return e != null ? e._int != 0 : fallback;
        }

        // ---- writes ------------------------------------------------------------------------------------

        /// <summary>
        /// Sets a parameter. The value is clamped to the declared range (rounded for int and enum, thresholded at
        /// 0.5 for bool); NaN is ignored. Returns true when the stored value changed, in which case the entry's
        /// callback and then <see cref="Changed"/> have run.
        /// </summary>
        public bool Set(ParamEntry entry, float value, ParamSource source = ParamSource.Local)
        {
            if (entry == null || !ReferenceEquals(entry.Registry, this)) return false;
            float v = entry.Sanitise(value);
            if (v == entry._value) return false;
            Store(entry, v, source);
            return true;
        }

        public bool Set(ParamHandle handle, float value, ParamSource source = ParamSource.Local)
        {
            return Set(Get(handle), value, source);
        }

        public bool Set(ushort id, float value, ParamSource source = ParamSource.Local)
        {
            return id < _entries.Count && Set(_entries[id], value, source);
        }

        public bool Set(string name, float value, ParamSource source = ParamSource.Local)
        {
            return Set(Find(name), value, source);
        }

        void Store(ParamEntry e, float v, ParamSource source)
        {
            e._value = v;
            e._int = (int)v;
            Version++;
            if (e._onFloat != null) e._onFloat(v);
            if (e._onInt != null) e._onInt(e._int);
            if (e._onBool != null) e._onBool(e._int != 0);
            ParamChangedHandler changed = Changed;
            if (changed != null) changed(e, source);
        }

        /// <summary>
        /// Applies a received <c>ParamSet</c>. Unknown ids are skipped; every value is clamped (Master Plan
        /// section 7.5, rule 3). Returns how many parameters changed.
        /// </summary>
        public int Apply(ParamSetMessage set, ParamSource source = ParamSource.Remote)
        {
            if (set == null) return 0;
            int changed = 0;
            for (int i = 0; i < set.Count; i++)
                if (Set(set.Ids[i], set.Values[i], source)) changed++;
            return changed;
        }

        /// <summary>Puts every parameter back to its default. Returns how many changed.</summary>
        public int ResetAll(ParamSource source = ParamSource.Local)
        {
            int changed = 0;
            for (int i = 0; i < _entries.Count; i++)
                if (Set(_entries[i], _entries[i]._default, source)) changed++;
            return changed;
        }

        /// <summary>
        /// Calls every entry's own callback with its current value. The composition root calls this once after
        /// all declarations, so the properties the callbacks drive start from the registry's values.
        /// </summary>
        public void PushAll()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                ParamEntry e = _entries[i];
                if (e._onFloat != null) e._onFloat(e._value);
                if (e._onInt != null) e._onInt(e._int);
                if (e._onBool != null) e._onBool(e._int != 0);
            }
        }

        // ---- link and persistence ----------------------------------------------------------------------

        /// <summary>
        /// Fills a <c>ParamSet</c> with current values, starting at entry <paramref name="firstIndex"/>. Returns
        /// the index to continue from, which equals <see cref="Count"/> when everything fitted.
        /// </summary>
        public int FillParamSet(ParamSetMessage into, int firstIndex = 0)
        {
            if (into == null) throw new ArgumentNullException("into");
            into.Clear();
            int i = firstIndex < 0 ? 0 : firstIndex;
            for (; i < _entries.Count && into.Count < MaxPerMessage; i++) into.Add(_entries[i].Id, _entries[i]._value);
            return i;
        }

        /// <summary>
        /// The <c>ParamManifest</c> body (Appendix B.2): <c>{"params":[{id, name, group, min, max, def, val,
        /// onRelease, kind, names}]}</c>. <c>kind</c> is <c>float</c>, <c>int</c>, <c>bool</c> or <c>enum</c>;
        /// <c>names</c> is present for enums only; <c>active</c> is false while the entry's group is inactive.
        /// </summary>
        public JsonValue ToManifestJson()
        {
            return ParamManifestMessage.FromRegistry(this).ToJson();
        }

        /// <summary>
        /// Current values as a preset: <c>{"values":{"&lt;name&gt;": value}}</c> (Appendix B.3). Floats are numbers,
        /// ints and enums are integers, bools are <c>true</c> or <c>false</c>.
        /// </summary>
        public JsonValue ToSnapshot()
        {
            JsonValue values = JsonValue.Object();
            for (int i = 0; i < _entries.Count; i++)
            {
                ParamEntry e = _entries[i];
                switch (e.Kind)
                {
                    case ParamKind.Float: values.Set(e.Name, e._value); break;
                    case ParamKind.Bool: values.Set(e.Name, e._int != 0); break;
                    default: values.Set(e.Name, e._int); break;
                }
            }
            return JsonValue.Object().Set("values", values);
        }

        /// <summary>
        /// Restores values from a snapshot made by <see cref="ToSnapshot"/> (the bare name-to-value map is also
        /// accepted). Names the registry does not know are skipped, every value is clamped, and an enum may be
        /// given by name. Returns how many parameters changed.
        /// </summary>
        public int Restore(JsonValue snapshot, ParamSource source = ParamSource.Restore)
        {
            if (snapshot == null || !snapshot.IsObject) return 0;
            JsonValue values = snapshot["values"].IsObject ? snapshot["values"] : snapshot;
            int changed = 0;
            for (int i = 0; i < values.Count; i++)
            {
                ParamEntry e;
                if (!_byName.TryGetValue(values.KeyAt(i), out e)) continue;
                JsonValue v = values[i];
                float f;
                if (v.IsNumber) f = v.AsFloat();
                else if (v.IsBool) f = v.AsBool() ? 1f : 0f;
                else if (v.IsString && TryParseText(e, v.AsString(), out f)) { }
                else continue;
                if (Set(e, f, source)) changed++;
            }
            return changed;
        }

        static bool TryParseText(ParamEntry e, string text, out float value)
        {
            if (e._names != null)
            {
                for (int i = 0; i < e._names.Length; i++)
                    if (string.Equals(e._names[i], text, StringComparison.OrdinalIgnoreCase)) { value = i; return true; }
            }
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>The manifest's name for a kind.</summary>
        public static string KindName(ParamKind kind)
        {
            switch (kind)
            {
                case ParamKind.Int: return "int";
                case ParamKind.Bool: return "bool";
                case ParamKind.Enum: return "enum";
                default: return "float";
            }
        }
    }
}
