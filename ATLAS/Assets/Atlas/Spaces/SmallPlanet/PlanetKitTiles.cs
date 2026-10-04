using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    /// <summary>
    /// The six Small Planet tiles modelled in Blender (PlanetKit.md): kit GLBs in Resources/PlanetKit, read once
    /// through <see cref="KitSet"/> and kept as <see cref="MeshData"/>. Vertex xz is the tile chart of CubeTiling
    /// face i in units of the authored radius R0, y is metres; <see cref="Scale"/> turns a tile into metres for a
    /// planet of radius R. Engine-free: the caller says how a file is read (Resources on the phone, the disk in tests).
    /// </summary>
    public sealed class PlanetKitTiles
    {
        /// <summary>The Resources folder of the tiles. Not KitSet.ResourceFolder: that one is Glued's.</summary>
        public const string ResourceFolder = "PlanetKit";
        /// <summary>The exporter's ceiling on one tile (tools/kit/export_planet.py).</summary>
        public const int MaxTileTriangles = 3000;
        /// <summary>Half-width of a tile along a chart axis, in units of R (pi/4).</summary>
        public const double HalfWidthR = Math.PI / 4;
        /// <summary>The exporter keeps every vertex within this many R of the tile centre on both chart axes.</summary>
        public const double MaxChartR = 1.0;

        readonly MeshData[] tiles;

        PlanetKitTiles(MeshData[] tiles, double authoredRadius)
        {
            this.tiles = tiles;
            AuthoredRadius = authoredRadius;
            for (int f = 0; f < tiles.Length; f++)
            {
                TotalTriangles += tiles[f].TriangleCount;
                MaxVertices = math.max(MaxVertices, tiles[f].VertexCount);
                MaxIndices = math.max(MaxIndices, tiles[f].IndexCount);
            }
        }

        /// <summary>R0 of the manifest's "planet" block: the radius, metres, the tiles were authored at.</summary>
        public double AuthoredRadius { get; }
        public int TotalTriangles { get; }
        /// <summary>The largest tile's vertex and index counts: one scratch of this size scales any tile.</summary>
        public int MaxVertices { get; }
        public int MaxIndices { get; }

        public static string TileName(int face) => "tile" + face;

        /// <summary>Tile of CubeTiling face <paramref name="face"/> as read: xz in units of R0, y in metres. Do not modify.</summary>
        public MeshData Tile(int face) => tiles[face];

        public int Triangles(int face) => tiles[face].TriangleCount;

        /// <summary>
        /// Reads the manifest and the six tiles, each checked against its hash and triangle count. Null with a
        /// reason when anything is missing, altered, unreadable or out of the convention; never a partial set.
        /// Never throws.
        /// </summary>
        /// <param name="read">Gives the bytes of "manifest" or "tile&lt;i&gt;.glb", or null when there is no such file.</param>
        public static PlanetKitTiles Load(Func<string, byte[]> read, out string error)
        {
            try
            {
                return TryLoad(read, out error);
            }
            catch (Exception e)
            {
                error = "the planet kit cannot be read: " + e.Message;
                return null;
            }
        }

        static PlanetKitTiles TryLoad(Func<string, byte[]> read, out string error)
        {
            if (read == null) { error = "no reader for the planet kit"; return null; }
            byte[] manifestBytes = null;
            KitSet set = KitSet.Load(name =>
            {
                byte[] bytes = read(name);
                if (name == KitSet.ManifestName) manifestBytes = bytes;
                return bytes;
            });
            if (set.LastError != null) { error = set.LastError; return null; }
            if (set.Count != CubeTiling.FaceCount)
            {
                error = "the planet kit has " + set.Count + " tiles; it needs " + CubeTiling.FaceCount;
                return null;
            }

            JsonValue manifest;
            string parseError;
            if (manifestBytes == null || !AtlasJson.TryParseUtf8(manifestBytes, 0, manifestBytes.Length, true, out manifest, out parseError))
            {
                error = "the planet kit manifest is unreadable";
                return null;
            }
            double r0 = manifest["planet"].GetDouble("R0", double.NaN);
            if (!(r0 > 0) || double.IsInfinity(r0))
            {
                error = "the planet kit manifest has no planet.R0";
                return null;
            }

            var tiles = new MeshData[CubeTiling.FaceCount];
            for (int f = 0; f < tiles.Length; f++)
            {
                var mesh = new MeshData(1024, 4096);
                string name = TileName(f);
                if (!set.TryGet(name, mesh, out error)) return null;
                if (mesh.TriangleCount == 0 || mesh.TriangleCount > MaxTileTriangles)
                {
                    error = "planet tile '" + name + "' has " + mesh.TriangleCount + " triangles; 1.." + MaxTileTriangles + " allowed";
                    return null;
                }
                float limit = (float)MaxChartR + 1e-4f;
                if (math.cmax(math.abs(mesh.BoundsMin.xz)) > limit || math.cmax(math.abs(mesh.BoundsMax.xz)) > limit)
                {
                    error = "planet tile '" + name + "' reaches beyond " + MaxChartR + " R of its centre";
                    return null;
                }
                tiles[f] = mesh;
            }
            error = null;
            return new PlanetKitTiles(tiles, r0);
        }

        /// <summary>
        /// Replaces <paramref name="into"/> with tile <paramref name="face"/> on a planet of radius R: x and z times R,
        /// y as authored, normals turned to match (the inverse transpose of the stretch R/R0 in xz). Palette, shade
        /// and line coordinates are copied unchanged. Allocates nothing when <paramref name="into"/> holds
        /// <see cref="MaxVertices"/> and <see cref="MaxIndices"/>.
        /// </summary>
        public void Scale(int face, double radius, AtlasMeshBuilder into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (!(radius > 0) || double.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            MeshData source = tiles[face];
            into.Clear();
            into.Reserve(source.VertexCount, source.IndexCount);
            float r = (float)radius;
            float inverseStretch = (float)(AuthoredRadius / radius);
            AtlasVertex[] from = source.Vertices, to = into.Vertices;
            for (int v = 0; v < source.VertexCount; v++)
            {
                AtlasVertex vertex = from[v];
                vertex.Position = new float3(vertex.Position.x * r, vertex.Position.y, vertex.Position.z * r);
                float3 n = new float3(vertex.Normal.x * inverseStretch, vertex.Normal.y, vertex.Normal.z * inverseStretch);
                float length = math.length(n);
                if (length > 1e-12f) vertex.Normal = n / length;
                to[v] = vertex;
            }
            into.VertexCount = source.VertexCount;
            Array.Copy(source.Indices, 0, into.Indices, 0, source.IndexCount);
            into.IndexCount = source.IndexCount;
            into.BoundsMin = new float3(source.BoundsMin.x * r, source.BoundsMin.y, source.BoundsMin.z * r);
            into.BoundsMax = new float3(source.BoundsMax.x * r, source.BoundsMax.y, source.BoundsMax.z * r);
        }
    }
}
