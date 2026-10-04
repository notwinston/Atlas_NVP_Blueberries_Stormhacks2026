using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Walk
{
    // V3 towers in tiles: one post per node standing at its offset in its tile's chart, taller for a higher degree.
    // Built into a caller-owned AtlasMeshBuilder (AtlasMeshBuilder only; the caller anchors the mesh to the tile).
    public static class NetworkTowerView
    {
        public const float BaseHeight = 0.6f, HeightPerDegree = 0.05f, MaxHeight = 2.4f, Radius = 0.12f;

        // Appends the towers of every node placed in `tile`; offsets are scaled to metres by unitMetres (L). Returns the count.
        public static int AppendTowers(AtlasMeshBuilder builder, NetworkModel model, NetworkTilePlacement placement,
                                       TileId tile, float unitMetres)
        {
            int count = 0;
            for (int i = 0, n = placement.NodeCount; i < n; i++)
            {
                if (placement.Tile(i) != tile) continue;
                double2 o = placement.Offset(i);
                float h = math.min(MaxHeight, BaseHeight + HeightPerDegree * model.Degree(i));
                builder.AddPost(new float3((float)o.x * unitMetres, 0f, (float)o.y * unitMetres), Radius, h, float2.zero);
                count++;
            }
            return count;
        }
    }
}
