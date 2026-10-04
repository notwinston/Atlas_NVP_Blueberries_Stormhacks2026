// PLACEHOLDER (lane D, P1-D03): contract surface only; replaced by lane D's implementation
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.Link;

namespace Atlas.Telemetry
{
    // Master Plan §6.5 (AtlasParam), §7.5 rule 2 (the real binder may apply on the next frame).
    public static class AtlasParamScanner
    {
        static readonly ConditionalWeakTable<object, List<ParamRegistry>> Bound =
            new ConditionalWeakTable<object, List<ParamRegistry>>();

        // Registers each [AtlasParam] field (float, int, bool, enum) of owner and writes the registry value into it,
        // now and on every Changed. Idempotent per (owner, registry).
        public static void Bind(object owner, ParamRegistry registry)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var list = Bound.GetOrCreateValue(owner);
            if (list.Contains(registry)) return;
            list.Add(registry);

            var fields = new Dictionary<ushort, FieldInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = owner.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                {
                    var a = f.GetCustomAttribute<AtlasParamAttribute>();
                    if (a == null) continue;
                    var id = registry.Register(a.Id, a.Group, a.Min, a.Max, a.Default, a.OnRelease);
                    fields[id] = f;
                    Write(owner, f, registry.Get(id));
                }
            }

            registry.Changed += (id, value) =>
            {
                if (fields.TryGetValue(id, out var f)) Write(owner, f, value);
            };
        }

        static void Write(object owner, FieldInfo f, float v)
        {
            var t = f.FieldType;
            object boxed;
            if (t == typeof(float)) boxed = v;
            else if (t == typeof(int)) boxed = (int)Math.Round(v);
            else if (t == typeof(bool)) boxed = v >= 0.5f;
            else if (t.IsEnum) boxed = Enum.ToObject(t, (int)Math.Round(v));
            else throw new NotSupportedException("[AtlasParam] field type " + t + " (float, int, bool, enum only)");
            f.SetValue(f.IsStatic ? null : owner, boxed);
        }
    }
}
