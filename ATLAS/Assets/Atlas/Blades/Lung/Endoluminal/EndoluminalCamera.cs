using System;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.Endoluminal
{
    public readonly struct EndoscopePose
    {
        public readonly double3 PositionMm, Forward, Up, Right;
        public EndoscopePose(double3 positionMm, double3 forward, double3 up, double3 right)
        { PositionMm = positionMm; Forward = forward; Up = up; Right = right; }
    }

    // Pure camera, clock and scene mapping for the endoluminal window (brief G5 3.2; D-12, D-13).
    public static class EndoluminalCamera
    {
        public const double BackOffRadii = 2.0;            // D-13
        public const double SceneParentRadiusM = 0.1;      // scene scale s = 0.1 / Parent.RadiusMm metres per mm
        public const float VerticalFovDeg = 90f, NearClipM = 0.005f, FarClipM = 10f;
        public static readonly double3 AnteriorPatient = new double3(0, -1, 0);   // DICOM LPS anterior (D-12)
        public static readonly double3 SuperiorPatient = new double3(0, 0, 1);    // fallback
        public const double AoNearRadii = 1, AoFarRadii = 8, AoMin = 0.25;        // D-15
        public const double ParentVisibleRadii = 4;

        static readonly string[] labels = MakeLabels();
        static string[] MakeLabels()
        {
            var l = new string[13];
            for (int h = 1; h <= 12; h++) l[h] = h + " o'clock";
            return l;
        }

        public static double SceneScale(in ForkView f) => SceneParentRadiusM / f.Parent.RadiusMm;

        public static EndoscopePose Pose(in ForkView f)
        {
            double3 E = f.Parent.EndMm, d = f.Parent.Dir;
            double b = Math.Min(BackOffRadii * f.Parent.RadiusMm, 0.5 * f.Parent.LenMm);
            double3 A = AnteriorPatient;
            double3 up = A - math.dot(A, d) * d;
            if (math.length(up) < 1e-6) { A = SuperiorPatient; up = A - math.dot(A, d) * d; }
            up = math.normalize(up);
            return new EndoscopePose(E - b * d, d, up, math.cross(d, up));
        }

        // Clockwise from 12 (up), in [0, 360).
        public static double ClockAngleDeg(in EndoscopePose p, double3 dir)
        {
            double a = Math.Atan2(math.dot(dir, p.Right), math.dot(dir, p.Up)) * 180.0 / Math.PI;
            a %= 360.0; if (a < 0) a += 360.0;
            if (a >= 360.0) a = 0;
            return a;
        }

        public static int ClockHour(in EndoscopePose p, double3 dir)
        {
            int h = ((int)Math.Round(ClockAngleDeg(p, dir) / 30.0)) % 12;
            return h == 0 ? 12 : h;
        }

        public static string ClockLabel(int hour)
        {
            if (hour < 1 || hour > 12) throw new ArgumentOutOfRangeException(nameof(hour));
            return labels[hour];
        }

        // z-reflection: Unity's left-handed camera then shows the true, unmirrored view (D-13).
        public static Vector3 ToScene(double3 pMm, double3 originMm, double s)
        {
            double3 d = pMm - originMm;
            return new Vector3((float)(s * d.x), (float)(s * d.y), (float)(-s * d.z));
        }

        public static Vector3 ToSceneDir(double3 d) => new Vector3((float)d.x, (float)d.y, (float)-d.z);

        public static Matrix4x4 ViewMatrix(in EndoscopePose p, double3 originMm, double s)
            => Matrix4x4.Scale(new Vector3(1, 1, -1)) *
               Matrix4x4.TRS(ToScene(p.PositionMm, originMm, s), Quaternion.LookRotation(ToSceneDir(p.Forward), ToSceneDir(p.Up)), Vector3.one).inverse;

        public static Matrix4x4 ProjectionMatrix() => Matrix4x4.Perspective(VerticalFovDeg, 1f, NearClipM, FarClipM);
    }
}
