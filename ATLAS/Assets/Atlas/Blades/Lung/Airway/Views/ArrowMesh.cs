using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // The beacon arrow: one floor triangle 0.20 x 0.20 m, every edge <= 0.2237 m < 0.25 m (MP §8.4 A.6). Colour white (COLOR.r = shade = 1,
    // COLOR.g = line weight 0: the arrow carries no lines, as the one vertex layout of the look has it).
    public static class ArrowMesh
    {
        public static Mesh Build(int paletteCell)
        {
            Vector2 uv = AtlasPalette.Uv(paletteCell);
            var m = new Mesh { name = "BeaconArrow" };
            m.vertices = new[] { new Vector3(0, 0, 0.10f), new Vector3(0.10f, 0, -0.10f), new Vector3(-0.10f, 0, -0.10f) };
            m.triangles = new[] { 0, 1, 2 };   // Cross(v1 - v0, v2 - v0).y > 0: the face points up
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            m.uv = new[] { uv, uv, uv };
            m.uv2 = new[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
            var white = new Color32(255, 0, 255, 255);
            m.colors32 = new[] { white, white, white };
            m.RecalculateBounds();
            return m;
        }
    }
}
