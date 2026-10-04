using System.Collections.Generic;
using Atlas.Link;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public sealed class FakeSkyEvents : ISkyEvents
    {
        public readonly List<string> Names = new List<string>();
        public readonly List<string> Data = new List<string>();
        public void Emit(string ev, string dataJson) { Names.Add(ev); Data.Add(dataJson); }
        public int Count(string ev) { int n = 0; foreach (var s in Names) if (s == ev) n++; return n; }
        public SkyPickData Pick(int index)
        {
            int n = 0;
            for (int i = 0; i < Names.Count; i++)
                if (Names[i] == "sky_pick") { if (n == index) return SkyPickData.FromJson(AtlasJson.Parse(Data[i])); n++; }
            throw new System.IndexOutOfRangeException("sky_pick " + index);
        }
    }
}
