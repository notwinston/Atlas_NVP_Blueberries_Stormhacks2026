using System;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.6; Brief §3, §7.4 (DV6). P3-B02 ships the "absent" stub only: P3-B04 fills it
    // (FromTexture, signed distance transform, the mask terms of the lookup margin).
    internal readonly struct FieldLossMaskData
    {
        public bool Present => false;   // B04 replaces this with the signed-distance array
    }

    public sealed class FieldLossMask : IDisposable
    {
        public const float DefaultHalfWidthDeg = 30f;   // DV6, Brief §7.4
        static readonly FieldLossMask absent = new FieldLossMask();
        public static FieldLossMask Absent => absent;
        internal FieldLossMaskData Data => default;
        public void Dispose() { }
    }
}
