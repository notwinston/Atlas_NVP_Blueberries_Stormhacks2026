// P1-B07: one mesh per tile of the generated world: the tile shell with the kerbs the tile owns and its four corner wedges
// (TileMeshGenerator), then the archetype's props (WorldPropMeshBuilder), so a tile stays one draw call (Master Plan §8.5).
// Engine-free: AtlasMesh.Apply uploads Data.
using Atlas.World;

namespace Atlas.Rendering
{
    public struct WorldTileMeshInfo
    {
        public TileMeshLayout Shell;
        /// <summary>First index of the props in Data.Indices; the shell lies before it.</summary>
        public int PropFirstIndex;
        public int ShellTriangles, PropTriangles;
        public int Triangles => ShellTriangles + PropTriangles;
    }

    public sealed class WorldTileMeshBuilder
    {
        public const int PostMask = 0xF;   // MP §3.5: a post at every tile corner, always

        readonly TileMeshGenerator shell = new TileMeshGenerator(TilePalette.Default);
        readonly WorldPropMeshBuilder props = new WorldPropMeshBuilder();

        /// <summary>The vertices and indices of the last Build.</summary>
        public AtlasMeshBuilder Data { get; } = new AtlasMeshBuilder(2048, 8192);

        /// <summary>Hyperbolic {4, q} tile, q = 5 or 6, at length scale L metres. Once Data has grown, building allocates nothing.</summary>
        public WorldTileMeshInfo Build(int q, double lengthScale, TileLod lod, in WorldTileKey key)
        {
            var info = new WorldTileMeshInfo { Shell = shell.Build(TileMeshSpec.Hyperbolic(q, lengthScale, lod, key.KerbMask, PostMask)) };
            Data.Clear();
            Data.Reserve(info.Shell.VertexCount, info.Shell.IndexCount);
            for (int i = 0; i < info.Shell.VertexCount; i++) Data.AddVertex(shell.Vertices[i]);
            for (int i = 0; i < info.Shell.IndexCount; i += 3) Data.AddTriangle(shell.Indices[i], shell.Indices[i + 1], shell.Indices[i + 2]);
            info.ShellTriangles = info.Shell.TriangleCount;
            info.PropFirstIndex = Data.IndexCount;
            info.PropTriangles = props.Build(q, lengthScale, key, Data);
            return info;
        }

        /// <summary>The mesh of a tile from the generator's content.</summary>
        public WorldTileMeshInfo Build(int q, double lengthScale, TileLod lod, in TileContent content) =>
            Build(q, lengthScale, lod, WorldTileKey.From(content));
    }
}
