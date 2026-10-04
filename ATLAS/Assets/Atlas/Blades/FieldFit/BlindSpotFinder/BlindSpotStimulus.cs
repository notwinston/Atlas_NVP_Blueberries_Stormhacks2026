using System;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>Stimulus constants and mesh builders (Brief §3.2, deviations D2, D6). All meshes face the eye (normal -z), clockwise from the eye.</summary>
    public static class BlindSpotStimulus
    {
        public const double StimulusDistanceM = 1.0, BackdropDistanceM = 1.2, BackdropHalfAngleDeg = 61;
        public const double DotDiameterDeg = 0.43;     // Goldmann size III
        public const double CrossArmDeg = 1.0;         // full span
        public const double CrossThicknessDeg = 0.15;
        public const int DotSegments = 16;

        const double Rad = Math.PI / 180.0;
        public static double BackdropHalfM => BackdropDistanceM * Math.Tan(BackdropHalfAngleDeg * Rad);          // 2.16486
        public static double DotRadiusM => StimulusDistanceM * Math.Tan(DotDiameterDeg * 0.5 * Rad);            // 0.0037525
        public static double CrossHalfLengthM => StimulusDistanceM * Math.Tan(CrossArmDeg * 0.5 * Rad);         // 0.0087269
        public static double CrossHalfThicknessM => StimulusDistanceM * Math.Tan(CrossThicknessDeg * 0.5 * Rad); // 0.0013090

        public static Mesh Quad(float half, Vector2 uv)
        {
            var m = new Mesh { name = "BlindSpot.Quad" };
            m.vertices = new[] { new Vector3(-half, -half, 0), new Vector3(-half, half, 0), new Vector3(half, half, 0), new Vector3(half, -half, 0) };
            Finish(m, new[] { 0, 1, 2, 0, 2, 3 }, uv);
            return m;
        }

        public static Mesh Disc(float radius, int segments, Vector2 uv)
        {
            var m = new Mesh { name = "BlindSpot.Disc" };
            var v = new Vector3[segments + 1];
            var t = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                double a = -2.0 * Math.PI * i / segments;   // decreasing angle: clockwise seen from the eye
                v[i + 1] = new Vector3((float)(radius * Math.Cos(a)), (float)(radius * Math.Sin(a)), 0);
                t[3 * i] = 0; t[3 * i + 1] = i + 1; t[3 * i + 2] = (i + 1) % segments + 1;
            }
            m.vertices = v;
            Finish(m, t, uv);
            return m;
        }

        public static Mesh Cross(float halfLength, float halfThickness, Vector2 uv)
        {
            var m = new Mesh { name = "BlindSpot.Cross" };
            float L = halfLength, T = halfThickness;
            m.vertices = new[]
            {
                new Vector3(-L, -T, 0), new Vector3(-L, T, 0), new Vector3(L, T, 0), new Vector3(L, -T, 0),   // horizontal bar
                new Vector3(-T, -L, 0), new Vector3(-T, L, 0), new Vector3(T, L, 0), new Vector3(T, -L, 0),   // vertical bar
            };
            Finish(m, new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, uv);
            return m;
        }

        static void Finish(Mesh m, int[] tris, Vector2 uv)
        {
            int n = m.vertexCount;
            var uvs = new Vector2[n]; var nor = new Vector3[n];
            for (int i = 0; i < n; i++) { uvs[i] = uv; nor[i] = new Vector3(0, 0, -1); }
            m.uv = uvs; m.normals = nor; m.triangles = tris;
            m.RecalculateBounds();
        }
    }
}
