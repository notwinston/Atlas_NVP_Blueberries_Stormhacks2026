// PLACEHOLDER (lane D, P1-D03): contract surface only; replaced by lane D's implementation
using System;

namespace Atlas.Telemetry
{
    // Master Plan §6.5: name, usage and the five fields are fixed by the plan.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AtlasParamAttribute : Attribute
    {
        public AtlasParamAttribute(string id, string group, float min, float max, float @default)
        {
            Id = id; Group = group; Min = min; Max = max; Default = @default;
        }

        public string Id { get; }
        public string Group { get; }
        public float Min { get; }
        public float Max { get; }
        public float Default { get; }
        public bool OnRelease { get; set; }   // Master Plan §7.3.2 onRelease ("bumps worldVer")
    }
}
