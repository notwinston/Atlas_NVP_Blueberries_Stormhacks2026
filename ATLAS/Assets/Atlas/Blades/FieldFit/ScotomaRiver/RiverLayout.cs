using System.Numerics;

namespace Atlas.Blades.FieldFit.ScotomaRiver
{
    /// <summary>One placed character: W the flat-text position, Z the position outside the disc, Scale the letter scale.</summary>
    public readonly struct RiverGlyph
    {
        public int CharIndex { get; }
        public Complex W { get; }
        public Complex Z { get; }
        public double Scale { get; }
        public RiverGlyph(int charIndex, Complex w, Complex z, double scale) { CharIndex = charIndex; W = w; Z = z; Scale = scale; }
    }

    public static class RiverLayout
    {
        public static RiverGlyph Place(int charIndex, Complex w, double a)
        {
            var z = RiverMath.Inverse(w, a);
            return new RiverGlyph(charIndex, w, z, RiverMath.LetterScale(z, a));
        }
    }
}
