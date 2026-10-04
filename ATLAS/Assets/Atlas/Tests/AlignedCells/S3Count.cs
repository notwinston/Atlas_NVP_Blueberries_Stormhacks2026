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

#if !ATLAS_HEADLESS
        // The same frame counted from what is really switched on under the given roots (the space's and the room's):
        // every enabled renderer of an active object, one draw per submesh, its index count; x2 eyes.
        public static void Measure(out int draws, out long tris, params UnityEngine.Transform[] roots)
        {
            draws = 0; tris = 0;
            foreach (UnityEngine.Transform root in roots)
                foreach (var mr in root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
                {
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                    var mf = mr.GetComponent<UnityEngine.MeshFilter>();
                    UnityEngine.Mesh mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    for (int s = 0; s < mesh.subMeshCount; s++) { draws++; tris += mesh.GetIndexCount(s) / 3; }
                }
            draws *= 2; tris *= 2;
        }
#endif
    }
}
