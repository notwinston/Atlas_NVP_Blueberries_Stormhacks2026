using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // S3 duck maze design (2026-10-04) §3, §4: the rubber duck that stands on one tile of the maze. The kit piece "duck"
    // (modelled in Blender, 2,400 triangles, 0.3 m tall, standing on y = 0) when the kit has it; otherwise a procedural duck
    // of the same size on the same palette cells: a yellow sphere body, a smaller head, an orange beak and two black eyes.
    // Engine-free (tests run it headless); AlignedCellsView uploads the result.
    public static class DuckMesh
    {
        public const string KitPiece = "duck";
        public const int Body = AtlasPalette.DuckYellow, Beak = AtlasPalette.DuckOrange, Eye = AtlasPalette.DuckBlack;

        // The procedural duck, metres: the body stands on the floor, the head sits above its front, the beak points along +z.
        public const float BodyRadius = 0.11f, BodyHeight = 0.11f, HeadRadius = 0.065f, EyeRadius = 0.012f;
        public static readonly float3 HeadCentre = new float3(0f, 0.24f, 0.06f);
        public static readonly float3 BeakCentre = new float3(0f, 0.235f, 0.13f), BeakSize = new float3(0.05f, 0.02f, 0.05f);
        public static readonly float3 EyeCentre = new float3(0.03f, 0.26f, 0.115f);   // and its mirror in x

        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with the duck: the kit piece when <paramref name="kit"/> has a
        /// valid one (true), otherwise the procedural duck (false). kitError says why a kit's piece was refused; it is null
        /// when the piece is used or no kit was given.
        /// </summary>
        public static bool Build(KitSet kit, AtlasMeshBuilder into, out string kitError)
        {
            into.Clear();
            kitError = null;
            if (kit != null)
            {
                var piece = new MeshData(1536, 7680);
                if (kit.TryGet(KitPiece, piece, out kitError))
                {
                    into.Append(piece, float3.zero);
                    return true;
                }
            }
            BuildProcedural(into);
            return false;
        }

        /// <summary>A new builder holding the duck (see <see cref="Build(KitSet, AtlasMeshBuilder, out string)"/>).</summary>
        public static AtlasMeshBuilder Build(KitSet kit)
        {
            var data = new AtlasMeshBuilder(1536, 7680);
            Build(kit, data, out _);
            return data;
        }

        /// <summary>The procedural duck, appended: about 560 triangles.</summary>
        public static void BuildProcedural(AtlasMeshBuilder data)
        {
            data.AddSphere(new float3(0f, BodyHeight, 0f), BodyRadius, 16, 10, AtlasPalette.Uv(Body));
            data.AddSphere(HeadCentre, HeadRadius, 12, 8, AtlasPalette.Uv(Body));
            data.AddBox(BeakCentre - new float3(0f, 0.5f * BeakSize.y, 0f), BeakSize, 0f, AtlasPalette.Uv(Beak), true, 0);
            for (int side = -1; side <= 1; side += 2)
                data.AddSphere(new float3(side * EyeCentre.x, EyeCentre.y, EyeCentre.z), EyeRadius, 8, 4, AtlasPalette.Uv(Eye));
        }
    }
}
