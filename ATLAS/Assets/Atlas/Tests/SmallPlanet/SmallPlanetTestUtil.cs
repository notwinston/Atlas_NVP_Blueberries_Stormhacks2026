using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    public static class SmallPlanetTestUtil
    {
        public static readonly double[] LapSides = { 1.0, 1.5, 2.0, 3.0, 3.2 };

        public static string ProjectDir => Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();

        // position in tile-zero coordinates (kernel.md §5.8): Frame(face) * U * o
        public static double3 WorldPoint(int face, in Iso2 u) => (CubeTiling.Frame(face) * new Iso2(u.M, 1)).M.c2;
        public static double3 WorldPoint(in WalkerPose pose) => WorldPoint(CubeTiling.FaceOf(pose.Tile), pose.Local);

        // floor points (x, z), 1 cm apart, the start first
        public static IEnumerable<double2> Feed(double2 start, double2[] legs)
        {
            yield return start;
            double2 a = start;
            foreach (var leg in legs)
            {
                double len = math.length(leg);
                int n = (int)Math.Ceiling(len / 0.01);
                for (int j = 1; j <= n; j++) yield return a + leg * ((double)j / n);
                a += leg;
            }
        }

        public static float4x4 ToF4(Matrix4x4 m) => new float4x4(m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), m.GetColumn(3));

        /// <summary>The app's kernel as S4 sets it: q = 3 at the loop-fit radius of a lap side, the walker over a floor point.</summary>
        public static HolonomyWalkerKernel NewKernel(double s, double2 headFloor)
        {
            var k = new HolonomyWalkerKernel();
            k.Configure(KernelConfig.Curved(3, SmallPlanetConstants.RadiusFromLapSide(s)));
            k.ResetToFloor(headFloor);
            return k;
        }

        /// <summary>A walker placed anywhere on the planet.</summary>
        public static HolonomyWalkerKernel Place(double s, int face, Iso2 u)
        {
            var k = new HolonomyWalkerKernel();
            k.Configure(KernelConfig.Curved(3, SmallPlanetConstants.RadiusFromLapSide(s)));
            k.Reset(new WalkerPose { Tile = CubeTiling.FaceId(face), Local = u });
            return k;
        }

        public static void Anchors(IWalkerKernel k, Iso2[] tiles, out Iso2 beacon)
        {
            WalkerPose p = k.Pose;
            SmallPlanetCore.Anchors(CubeTiling.FaceOf(p.Tile), p.Local, tiles, out beacon);
        }

        public static RoomModel Room(string name) => RoomFixtures.LoadFromProject(name);

        public static HeadPose[] Walk(string name)
        {
            using (var reader = new StreamReader(Path.Combine(ProjectDir, "..", "recordings", name)))
                return PoseLog.Read(reader).Samples;
        }

        public static double MaxAbsDiff(in double3x3 a, in double3x3 b)
            => math.max(math.cmax(math.abs(a.c0 - b.c0)), math.max(math.cmax(math.abs(a.c1 - b.c1)), math.cmax(math.abs(a.c2 - b.c2))));

        public static Iso2 ExactRot(int j)
        {
            var r = new Iso2(new double3x3(0, -1, 0, 1, 0, 0, 0, 0, 1), 1);
            var m = new Iso2(double3x3.identity, 1);
            for (int i = 0; i < ((j % 4) + 4) % 4; i++) m = m * r;
            return m;
        }

        public static bool ExactEq(in Iso2 a, in Iso2 b) => a.M.c0.Equals(b.M.c0) && a.M.c1.Equals(b.M.c1) && a.M.c2.Equals(b.M.c2);

        /// <summary>One mesh as the tests read it: positions and triangles, engine-free.</summary>
        public sealed class TileData
        {
            public float3[] V; public int[] T; public TileMeshLayout Lay; public double R;
        }

        public static TileData BuildTile(double R)
        {
            var gen = new TileMeshGenerator(TilePalette.Default);
            var lay = gen.Build(TileMeshSpec.Spherical(R, TileLod.Near, 0, 0xF));
            var d = new TileData { V = new float3[lay.VertexCount], T = new int[lay.IndexCount], Lay = lay, R = R };
            for (int i = 0; i < d.V.Length; i++) d.V[i] = gen.Vertices[i].Position;
            for (int i = 0; i < d.T.Length; i++) d.T[i] = gen.Indices[i];
            return d;
        }

        public static TileData BuildBeacon(double R)
        {
            var data = new AtlasMeshBuilder(32, 96);
            PoleBeaconMesh.Build(data);
            var d = new TileData { V = new float3[data.VertexCount], T = new int[data.IndexCount], R = R };
            for (int i = 0; i < d.V.Length; i++) d.V[i] = data.Vertices[i].Position;
            Array.Copy(data.Indices, d.T, d.T.Length);
            return d;
        }
    }
}
