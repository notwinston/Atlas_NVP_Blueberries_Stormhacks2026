// P1-B07: what a generated prop may cost (Master Plan §3.4, §8.4). WorldLint budgets a world with the same numbers.
using Atlas.World;

namespace Atlas.Rendering
{
    public static class WorldPropBudget
    {
        /// <summary>MP §8.4: nothing placed in a curved space has an edge longer than this, measured in the curved metric.</summary>
        public const float MaxEdge = AtlasMeshBuilder.MaxEdge;
        /// <summary>Side of a prop's grid cell: its diagonal stays under MaxEdge after the placement has stretched it.</summary>
        public const float GridEdge = 0.17f;
        /// <summary>MP §3.4 Garden: "low props only (under 0.5 m)".</summary>
        public const float GardenMaxHeight = 0.5f;
        /// <summary>Nothing generated is taller than the corner posts: the draw rule tests a shell of this height (TileDrawRule.ShellHeight).</summary>
        public const float MaxHeight = TileChart.PostHeight;

        public const int Tower = 1500, Garden = 600, Doorway = 400, Span = 200;   // triangles, MP §8.4 "small 300-1,500"

        /// <summary>Triangle cap of the props of one tile of this archetype; 0 for Home, Plaza and Corridor, which carry none.</summary>
        public static int Triangles(Archetype archetype) => archetype switch
        {
            Archetype.Tower => Tower, Archetype.Garden => Garden, Archetype.Doorway => Doorway,
            Archetype.SpanA => Span, Archetype.SpanB => Span, _ => 0,
        };
    }
}
