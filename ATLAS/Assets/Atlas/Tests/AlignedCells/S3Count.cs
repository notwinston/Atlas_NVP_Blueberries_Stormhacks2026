using Atlas.Rendering;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // [P1-B12 D7] What S3 draws in one frame, from the real meshes' counts (Master Plan §4.7 "once per eye", §8.5):
    // one draw per cell instance, one for the posts, the room's shell as the room content shows it, one per visible object; x2 eyes.
    static class S3Count
    {
        public static int CellTriangles(CellGrid grid)
            => new TileMeshGenerator(TilePalette.Default).Build(TileMeshSpec.FlatCell(grid.Side, kerbMask: 0, postMask: 0)).TriangleCount;

        public static int PostTriangles(CellGrid grid) => PostMesh.Build(grid, TilePalette.Default, new AtlasMeshBuilder());

        public static void Measure(Rig rig, out int draws, out long tris)
        {
            int instances = rig.Core.Instances.Count;
            int posts = rig.Grid.InteriorCornerCount > 0 ? 1 : 0;
            draws = instances + posts + rig.Content.ShellDrawCalls + rig.RoomView.VisibleObjectCount;
            tris = (long)instances * CellTriangles(rig.Grid) + PostTriangles(rig.Grid) + rig.Content.ShellTriangleCount;
            for (int i = 0; i < rig.RoomView.ObjectCount; i++)
                if (rig.RoomView.ObjectVisible(i)) tris += rig.Content.GetObject(i).TriangleCount;
            draws *= 2; tris *= 2;
        }
    }
}
