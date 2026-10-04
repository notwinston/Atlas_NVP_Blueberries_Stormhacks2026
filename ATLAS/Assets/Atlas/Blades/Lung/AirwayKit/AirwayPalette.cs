using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayKit
{
    // Palette UVs for the airway kit: texel centres of 16-px blocks in the 128^2 palette of MP 8.1, following P1-B05's pattern.
    // Provisional until P1-A06 (D-8).
    public struct AirwayPalette
    {
        public float2 Kerb, Membrane, Strip, Glyph, Shell, Iris, Tissue, ValveFloor;

        public static readonly AirwayPalette Provisional = new AirwayPalette
        {
            Kerb = Atlas.Rendering.TilePalette.Provisional.Kerb,
            Membrane = new float2(0.4375f, 0.0625f),
            Strip = new float2(0.5625f, 0.0625f),
            Glyph = new float2(0.6875f, 0.0625f),
            Shell = new float2(0.8125f, 0.0625f),
            Iris = new float2(0.9375f, 0.0625f),
            Tissue = new float2(0.0625f, 0.1875f),
            ValveFloor = Atlas.Rendering.TilePalette.Provisional.Floor,
        };
    }
}
