// PLACEHOLDER (lane D, P1-D03): contract surface only; replaced by lane D's implementation
using System;
using System.Collections.Generic;

namespace Atlas.Link
{
    // Master Plan §6.5, §7.1, §7.3.2, App. B.2 (ParamManifest, ParamSet). Plain C#: no engine references.
    public sealed class ParamRegistry
    {
        sealed class Entry
        {
            public string Name, Group;
            public float Min, Max, Value;
            public bool OnRelease;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, ushort> _byName = new Dictionary<string, ushort>();

        // Master Plan §7.3.2: the phone clamps; values are f32 (ParamSet). Enumerations are stored as indices.
        public bool DemoLock { get; set; }

        public event Action<ushort, float> Changed;

        // id u16 assigned in registration order; registering an existing name returns its id unchanged.
        public ushort Register(string name, string group, float min, float max, float def, bool onRelease)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (_byName.TryGetValue(name, out var existing)) return existing;
            if (min > max) throw new ArgumentException("min > max for " + name);
            if (_entries.Count >= ushort.MaxValue) throw new InvalidOperationException("parameter id space (u16) exhausted");
            var id = (ushort)_entries.Count;
            _entries.Add(new Entry { Name = name, Group = group, Min = min, Max = max, Value = Clamp(def, min, max), OnRelease = onRelease });
            _byName.Add(name, id);
            return id;
        }

        public bool TryGetId(string name, out ushort id) => _byName.TryGetValue(name, out id);

        public float Get(ushort id) => _entries[id].Value;

        // Master Plan §7.3.2: clamps to [min, max]; returns the applied value. Synchronous Changed (placeholder).
        public float Set(ushort id, float value)
        {
            var e = _entries[id];
            var applied = Clamp(value, e.Min, e.Max);
            if (applied != e.Value)
            {
                e.Value = applied;
                Changed?.Invoke(id, applied);
            }
            return applied;
        }

        // ParamSet path (Master Plan §7.3.2): ignored, returning false, while DemoLock is on.
        public bool ApplyRemote(ushort id, float value)
        {
            if (DemoLock) return false;
            Set(id, value);
            return true;
        }

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
