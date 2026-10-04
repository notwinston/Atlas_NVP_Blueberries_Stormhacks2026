using System;
using System.Collections.Generic;
using System.Reflection;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Binds <see cref="AtlasParamAttribute"/> fields to the registry (Master Plan sections 6.2 and 7.1).
    /// Reflection runs once in <c>Bind</c> and again only when a bound value changes, which the link and the menu
    /// do at step 8b; reading a bound field on the frame path is a plain field read.
    /// </summary>
    public static class ParamBinder
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Declares every attributed instance field of <paramref name="target"/> (its own and its base types')
        /// and keeps each field equal to the registry value until the binding is disposed.
        /// </summary>
        public static ParamBinding Bind(object target, ParamRegistry registry)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (registry == null) throw new ArgumentNullException("registry");
            var binding = new ParamBinding(registry, target);
            for (Type t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                Collect(binding, t.GetFields(InstanceFields));
            binding.Activate();
            return binding;
        }

        /// <summary>As <see cref="Bind"/>, for the attributed static fields of a type (base types are not searched).</summary>
        public static ParamBinding BindStatic(Type type, ParamRegistry registry)
        {
            if (type == null) throw new ArgumentNullException("type");
            if (registry == null) throw new ArgumentNullException("registry");
            var binding = new ParamBinding(registry, null);
            Collect(binding, type.GetFields(StaticFields));
            binding.Activate();
            return binding;
        }

        static void Collect(ParamBinding binding, FieldInfo[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                var attribute = (AtlasParamAttribute)Attribute.GetCustomAttribute(field, typeof(AtlasParamAttribute), false);
                if (attribute == null) continue;
                binding.Add(field, Declare(binding.Registry, field, attribute));
            }
        }

        static ParamEntry Declare(ParamRegistry registry, FieldInfo field, AtlasParamAttribute a)
        {
            if (string.IsNullOrEmpty(a.Id)) throw Bad(field, "has an empty parameter id");
            if (field.IsInitOnly || field.IsLiteral) throw Bad(field, "is read-only");

            Type type = field.FieldType;
            ParamKind kind;
            string[] names = null;
            if (type == typeof(float)) { kind = ParamKind.Float; Require(field, a, AtlasParamAttribute.Form.Range); }
            else if (type == typeof(int)) { kind = ParamKind.Int; Require(field, a, AtlasParamAttribute.Form.Range); }
            else if (type == typeof(bool)) { kind = ParamKind.Bool; Require(field, a, AtlasParamAttribute.Form.Bool); }
            else if (type.IsEnum)
            {
                kind = ParamKind.Enum;
                Require(field, a, AtlasParamAttribute.Form.Option);
                names = OptionNames(field, type);
            }
            else throw Bad(field, "is a " + type.Name + "; only float, int, bool and enum fields can be parameters");

            if (kind != ParamKind.Bool && kind != ParamKind.Enum && a.Min > a.Max) throw Bad(field, "has min above max");

            // A name that is already declared keeps its owner, range and value: declaring it again would drop
            // the owner's callback. The field simply follows it.
            ParamEntry existing = registry.Find(a.Id);
            if (existing != null)
            {
                if (existing.Kind != kind)
                    throw Bad(field, "binds '" + a.Id + "', which is declared as " + existing.Kind + ", not " + kind);
                return existing;
            }

            string group = a.ResolveGroup();
            switch (kind)
            {
                case ParamKind.Float: return registry.DeclareFloat(a.Id, group, a.Min, a.Max, a.Default, null, a.OnRelease);
                case ParamKind.Int:
                    return registry.DeclareInt(a.Id, group, (int)Math.Round(a.Min), (int)Math.Round(a.Max), (int)Math.Round(a.Default), null, a.OnRelease);
                case ParamKind.Bool: return registry.DeclareBool(a.Id, group, a.Default >= 0.5f, null, a.OnRelease);
                default: return registry.DeclareEnum(a.Id, group, names, (int)a.Default, null, a.OnRelease);
            }
        }

        static void Require(FieldInfo field, AtlasParamAttribute a, AtlasParamAttribute.Form form)
        {
            if (a.Shape == form) return;
            string wanted = form == AtlasParamAttribute.Form.Range ? "(id, min, max, default)"
                : form == AtlasParamAttribute.Form.Bool ? "(id, bool default)" : "(id, int defaultIndex)";
            throw Bad(field, "needs the attribute form " + wanted);
        }

        // An enum can be a parameter only when its values are exactly 0..n-1, because the wire carries the index.
        static string[] OptionNames(FieldInfo field, Type type)
        {
            Array values = Enum.GetValues(type);
            var names = new string[values.Length];
            if (names.Length == 0) throw Bad(field, "is an enum with no values");
            for (int i = 0; i < values.Length; i++)
            {
                int v = Convert.ToInt32(values.GetValue(i));
                if (v < 0 || v >= names.Length || names[v] != null) throw Bad(field, "is an enum whose values are not exactly 0 to n-1");
                names[v] = Enum.GetName(type, values.GetValue(i));
            }
            return names;
        }

        static InvalidOperationException Bad(FieldInfo field, string what)
        {
            return new InvalidOperationException("[AtlasParam] field " + field.DeclaringType.Name + "." + field.Name + " " + what);
        }
    }

    /// <summary>
    /// The live link between a set of fields and the registry. Dispose stops the field updates; the parameters
    /// stay declared and keep their values.
    /// </summary>
    public sealed class ParamBinding : IDisposable
    {
        readonly object _target;
        readonly List<FieldInfo> _fields = new List<FieldInfo>(8);
        readonly List<ParamEntry> _entries = new List<ParamEntry>(8);
        bool _active;

        internal ParamBinding(ParamRegistry registry, object target)
        {
            Registry = registry;
            _target = target;
        }

        public ParamRegistry Registry { get; private set; }

        /// <summary>How many fields are bound.</summary>
        public int Count { get { return _entries.Count; } }

        /// <summary>The handle of a parameter bound here, for <c>registry.Set</c>.</summary>
        public ParamHandle HandleOf(string id) { return EntryOf(id).Handle; }

        /// <summary>The registry entry of a parameter bound here.</summary>
        public ParamEntry EntryOf(string id)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].Name, id, StringComparison.Ordinal)) return _entries[i];
            throw new KeyNotFoundException("Parameter '" + id + "' is not bound here");
        }

        internal void Add(FieldInfo field, ParamEntry entry)
        {
            _fields.Add(field);
            _entries.Add(entry);
        }

        internal void Activate()
        {
            for (int i = 0; i < _entries.Count; i++) Write(i);
            if (_entries.Count == 0) return;
            Registry.Changed += OnChanged;
            _active = true;
        }

        void OnChanged(ParamEntry entry, ParamSource source)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (ReferenceEquals(_entries[i], entry)) Write(i);
        }

        // Boxes the value: this runs once at Bind and then only when the parameter changes.
        void Write(int i)
        {
            FieldInfo field = _fields[i];
            ParamEntry entry = _entries[i];
            Type type = field.FieldType;
            object value;
            if (type == typeof(float)) value = entry.Float;
            else if (type == typeof(int)) value = entry.Int;
            else if (type == typeof(bool)) value = entry.Bool;
            else value = Enum.ToObject(type, entry.Int);
            field.SetValue(_target, value);
        }

        public void Dispose()
        {
            if (!_active) return;
            _active = false;
            Registry.Changed -= OnChanged;
        }
    }
}
