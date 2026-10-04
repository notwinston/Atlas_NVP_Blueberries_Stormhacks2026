using System;

namespace Atlas.Link
{
    /// <summary>
    /// The engine-free half of the baked parameter defaults (the Console's "Bake", Master Plan section 7.3.2,
    /// contracts lane D 1.7). The phone reads <c>Resources/AtlasParams/defaults.json</c> and hands it to
    /// <see cref="Apply"/>: the values of the names already declared are restored now, and a value is set as its
    /// name is declared later. Baked defaults set the value, never the entry's <c>Default</c>, so "Reset" still
    /// goes to the declared default.
    /// </summary>
    public static class BakedDefaults
    {
        /// <summary>
        /// Parameters a baked file never sets, even when a preset holds them: a baked <c>demoLock</c> would lock a
        /// rehearsal build, a baked <c>link.enabled</c> would silence the link of a build with no Station, and a
        /// baked <c>space.id</c> would pick the space the build starts in behind the app's back.
        /// </summary>
        public static readonly string[] NeverBaked = { "demoLock", "link.enabled", "space.id" };

        public static bool IsBakeable(string name)
        {
            for (int i = 0; i < NeverBaked.Length; i++)
                if (string.Equals(NeverBaked[i], name, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// Applies baked values to a registry: a snapshot <c>{"values":{...}}</c> or the bare name-to-value map. A
        /// number, a boolean, or an enum's name or a numeric string is accepted; an unknown name, a value of
        /// another shape and the names of <see cref="NeverBaked"/> are skipped, and every value is clamped, as
        /// in <see cref="ParamRegistry.Restore"/>. Returns how many parameters changed now. A null or non-object
        /// <paramref name="baked"/> is a no-op. Call once per registry: it stays subscribed to
        /// <see cref="ParamRegistry.Declared"/> for the names declared later.
        /// </summary>
        public static int Apply(ParamRegistry registry, JsonValue baked)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            if (baked == null || !baked.IsObject) return 0;

            JsonValue source = baked["values"].IsObject ? baked["values"] : baked;
            JsonValue values = JsonValue.Object();
            for (int i = 0; i < source.Count; i++)
            {
                string name = source.KeyAt(i);
                JsonValue v = source[i];
                if (IsBakeable(name) && (v.IsNumber || v.IsBool || v.IsString)) values.Set(name, v);
            }
            if (values.Count == 0) return 0;

            int changed = registry.Restore(values, ParamSource.Restore);
            registry.Declared += entry =>
            {
                JsonValue v;
                if (values.TryGet(entry.Name, out v))
                    registry.Restore(JsonValue.Object().Set(entry.Name, v), ParamSource.Restore);
            };
            return changed;
        }

        /// <summary>
        /// <see cref="Apply"/> for the text of the file. Null, empty or unparsable text is "no baked values":
        /// returns false and touches nothing.
        /// </summary>
        public static bool ApplyText(ParamRegistry registry, string json)
        {
            JsonValue baked;
            if (string.IsNullOrEmpty(json) || !AtlasJson.TryParse(json, out baked) || !baked.IsObject) return false;
            Apply(registry, baked);
            return true;
        }
    }
}
