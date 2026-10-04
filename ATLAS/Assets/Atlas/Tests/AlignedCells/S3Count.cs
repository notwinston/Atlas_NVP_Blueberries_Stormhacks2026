using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // [P1-B12 D7] Editor proxy for the Profiler's draw-call and triangle counts:
    // enabled renderers x submeshes, x2 eyes (Master Plan §4.7 "once per eye", §8.5).
    static class S3Count
    {
        public static void Measure(Transform root, out int draws, out long tris)
        {
            draws = 0; tris = 0;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                for (int s = 0; s < mesh.subMeshCount; s++) { draws++; tris += mesh.GetIndexCount(s) / 3; }
            }
            draws *= 2; tris *= 2;
        }
    }
}
