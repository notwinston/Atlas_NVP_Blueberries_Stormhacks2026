using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // The beacon arrow: one floor triangle 0.20 x 0.20 m, every edge <= 0.2237 m < 0.25 m (MP §8.4 A.6). Colour white (COLOR.r = AO = 1, PH-8).
    public static class ArrowMesh
    {
        public static Mesh Build(int paletteCell)
        {
            var uv = AtlasPalette.CellUV(paletteCell);
            var m = new Mesh { name = "BeaconArrow" };
            m.vertices = new[] { new Vector3(0, 0, 0.10f), new Vector3(0.10f, 0, -0.10f), new Vector3(-0.10f, 0, -0.10f) };
            m.triangles = new[] { 0, 1, 2 };   // Cross(v1 - v0, v2 - v0).y > 0: the face points up
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            m.uv = new[] { uv, uv, uv };
            m.uv2 = new[] { new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0) };
            m.colors = new[] { Color.white, Color.white, Color.white };
            m.RecalculateBounds();
            return m;
        }
    }
}
