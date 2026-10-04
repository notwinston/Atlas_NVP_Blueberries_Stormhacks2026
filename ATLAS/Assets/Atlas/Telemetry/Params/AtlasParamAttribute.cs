using System;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Marks a field as a parameter of the registry (Master Plan section 6.5, contracts F6). The field is the
    /// fast read: reading it costs nothing. Code never assigns the field; it sets the parameter in the registry
    /// and <see cref="ParamBinder"/> writes the applied value back.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class AtlasParamAttribute : Attribute
    {
        internal enum Form : byte { Range, Bool, Option }

        /// <summary>A float or int field with a range and a default.</summary>
        public AtlasParamAttribute(string id, float min, float max, float def)
        {
            Id = id;
            Min = min;
            Max = max;
            Default = def;
            Shape = Form.Range;
        }

        /// <summary>A bool field.</summary>
        public AtlasParamAttribute(string id, bool def)
        {
            Id = id;
            Min = 0f;
            Max = 1f;
            Default = def ? 1f : 0f;
            Shape = Form.Bool;
        }

        /// <summary>An enum field; <paramref name="def"/> is the index of the default option.</summary>
        public AtlasParamAttribute(string id, int def)
        {
            Id = id;
            Min = 0f;
            Max = 0f;
            Default = def;
            Shape = Form.Option;
        }

        /// <summary>Dotted name, unique in the registry, for example <c>glued.postWidth</c>.</summary>
        public string Id { get; private set; }
        public float Min { get; private set; }
        public float Max { get; private set; }
        public float Default { get; private set; }

        /// <summary>Heading on the Console and in the menu. Null means the first segment of the id.</summary>
        public string Group { get; set; }

        /// <summary>Slider step for a float field. Carried for the Console; the registry does not quantise.</summary>
        public float Step { get; set; }

        /// <summary>The Console sends the value when the slider is released, because applying it rebuilds geometry.</summary>
        public bool OnRelease { get; set; }

        /// <summary>The parameter is one of the kill switches of Master Plan section 12.4.</summary>
        public bool KillSwitch { get; set; }

        internal Form Shape { get; private set; }

        /// <summary>The group this parameter is declared under.</summary>
        public string ResolveGroup()
        {
            if (!string.IsNullOrEmpty(Group)) return Group;
            int dot = Id != null ? Id.IndexOf('.') : -1;
            return dot > 0 ? Id.Substring(0, dot) : "app";
        }
    }
}
