using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayKit
{
    // Palette UVs for the airway kit: the cells of the one palette (MP 8.1; AtlasPalette), by what each part of the kit is.
    public struct AirwayPalette
    {
        public float2 Kerb, Membrane, Strip, Glyph, Shell, Iris, Tissue, ValveFloor;

        public static readonly AirwayPalette Default = new AirwayPalette
        {
            Kerb = TilePalette.Default.Kerb,
            Membrane = AtlasPalette.Uv(AtlasPalette.Wall),
            Strip = AtlasPalette.Uv(AtlasPalette.Opening),
            Glyph = AtlasPalette.Uv(AtlasPalette.LapMarker),
            Shell = AtlasPalette.Uv(AtlasPalette.WallAlt),
            Iris = AtlasPalette.Uv(AtlasPalette.DoorFrame),
            Tissue = AtlasPalette.Uv(AtlasPalette.Region(2, 2)),
            ValveFloor = TilePalette.Default.Floor,
        };
    }
}
