using System;
using Atlas.Blades.Lung.Endoluminal;
using Atlas.Blades.Lung.Endoluminal.Testing;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class EndoluminalMathTests
    {
        // Criterion: MP 10.2.3 endoluminal "clock positions"; Spec 11.3.1.
        [Test]
        public void Endo_ClockHour_Sweep()
        {
            var pose = EndoluminalCamera.Pose(SyntheticForks.Route(1)[0]);
            Assert.AreEqual(0, math.length(pose.Forward - new double3(0, 0, -1)), 1e-12);
            Assert.AreEqual(0, math.length(pose.Up - new double3(0, -1, 0)), 1e-12);
            double c = Math.Cos(37 * Math.PI / 180), s = Math.Sin(37 * Math.PI / 180);
            int n = 0;
            for (double phi = 0; phi < 360; phi += 7)
            {
                double dist = Math.Abs(((phi - 15) % 30 + 30) % 30);
                if (dist < 0.5 || 30 - dist < 0.5) continue;
                double r = phi * Math.PI / 180;
                double3 dir = c * pose.Forward + s * (Math.Cos(r) * pose.Up + Math.Sin(r) * pose.Right);
                int expect = ((int)Math.Round(phi / 30)) % 12; if (expect == 0) expect = 12;
                Assert.AreEqual(expect, EndoluminalCamera.ClockHour(pose, dir), "phi " + phi);
                Assert.AreEqual(phi, EndoluminalCamera.ClockAngleDeg(pose, dir), 1e-9);
                n++;
            }
            Assert.Greater(n, 45);
        }

        // Criterion: same; root fork of the synthetic route reads 9 and 3 (plane axis x), 6 and 12 (plane axis y).
        [Test]
        public void Endo_ClockHour_SyntheticRoot()
        {
            var f = SyntheticForks.Route(1)[0];
            var pose = EndoluminalCamera.Pose(f);
            Assert.AreEqual(9, EndoluminalCamera.ClockHour(pose, f.Left.Dir));
            Assert.AreEqual(3, EndoluminalCamera.ClockHour(pose, f.Right.Dir));
            var g = SyntheticForks.Route(1, new double3(0, 1, 0))[0];
            var pg = EndoluminalCamera.Pose(g);
            Assert.AreEqual(6, EndoluminalCamera.ClockHour(pg, g.Left.Dir));
            Assert.AreEqual(12, EndoluminalCamera.ClockHour(pg, g.Right.Dir));
            Assert.AreEqual("9 o'clock", EndoluminalCamera.ClockLabel(9));
            Assert.Throws<ArgumentOutOfRangeException>(() => EndoluminalCamera.ClockLabel(0));
        }

        // Criterion: MP 10.2.3 "true 3D view": the projected direction of each child equals its clock angle (a mirrored image would fail).
        [Test]
        public void Endo_ClockMatchesProjection()
        {
            foreach (var f in SyntheticForks.Route(4))
            {
                var pose = EndoluminalCamera.Pose(f);
                double s = EndoluminalCamera.SceneScale(f);
                Matrix4x4 vp = EndoluminalCamera.ProjectionMatrix() * EndoluminalCamera.ViewMatrix(pose, f.Parent.EndMm, s);
                for (int k = 0; k < 2; k++)
                {
                    var c = f.Child(k);
                    Vector4 clip = vp * new Vector4(0, 0, 0, 1);   // placeholder to keep the type explicit
                    Vector3 p = EndoluminalCamera.ToScene(c.EndMm, f.Parent.EndMm, s);
                    clip = vp * new Vector4(p.x, p.y, p.z, 1);
                    Assert.Greater(clip.w, 0, "child end behind the camera");
                    double ang = Math.Atan2(clip.x / clip.w, clip.y / clip.w) * 180 / Math.PI;
                    ang = (ang % 360 + 360) % 360;
                    double want = EndoluminalCamera.ClockAngleDeg(pose, c.Dir);
                    double diff = Math.Abs(ang - want); diff = Math.Min(diff, 360 - diff);
                    Assert.Less(diff, 0.5, $"fork '{f.Address}' child {k}: projected {ang:F2} vs clock {want:F2}");
                }
            }
        }

        // Criterion: D-12 (up fallback when the parent runs along anterior).
        [Test]
        public void Endo_UpFallback()
        {
            var pose = EndoluminalCamera.Pose(EndoTestUtil.Fork(new double3(0, -1, 0)));
            Assert.AreEqual(1, math.length(pose.Up), 1e-12);
            Assert.AreEqual(0, math.dot(pose.Up, pose.Forward), 1e-12);
            Assert.AreEqual(0, math.length(pose.Up - new double3(0, 0, 1)), 1e-12);
            Assert.IsFalse(double.IsNaN(pose.Right.x + pose.Right.y + pose.Right.z));
        }

        // Criterion: D-13 (camera inside the parent, on its axis).
        [Test]
        public void Endo_PoseInsideParent()
        {
            var f = SyntheticForks.Route(1)[0];
            double rp = f.Parent.RadiusMm;
            var pose = EndoluminalCamera.Pose(f);
            double3 rel = pose.PositionMm - f.Parent.PosMm;
            double3 radial = rel - math.dot(rel, f.Parent.Dir) * f.Parent.Dir;
            Assert.LessOrEqual(math.length(radial), 1e-9);
            Assert.AreEqual(0, math.length(pose.PositionMm - (f.Parent.EndMm - 2 * rp * f.Parent.Dir)), 1e-9);
            var shortParent = new AirwaySegmentMm("", double3.zero, new double3(0, 0, -1), 3 * rp, rp);
            var k = new ForkView(shortParent, new AirwaySegmentMm("L", shortParent.EndMm, new double3(0, 1, -1), 10, 5), new AirwaySegmentMm("R", shortParent.EndMm, new double3(0, -1, -1), 10, 5));
            Assert.AreEqual(0, math.length(EndoluminalCamera.Pose(k).PositionMm - (shortParent.EndMm - 1.5 * rp * shortParent.Dir)), 1e-9);
        }

        // Criterion: D-15 (tube resolution, depth AO, parent visible length 4 r_p).
        [Test]
        public void Endo_TubeMesh()
        {
            var f = SyntheticForks.Route(1)[0];
            var pose = EndoluminalCamera.Pose(f);
            double s = EndoluminalCamera.SceneScale(f), rp = f.Parent.RadiusMm;
            var b = new AirwayTubeMeshBuilder(); var m = new Mesh();
            try
            {
                var c = f.Left;
                b.Build(c, 0, c.LenMm, f.Parent.EndMm, s, pose.PositionMm, rp, m);
                var v = KitTestUtil.Verts(m); var idx = KitTestUtil.Idx(m);
                Vector3 a = EndoluminalCamera.ToScene(c.PosMm, f.Parent.EndMm, s), d = EndoluminalCamera.ToSceneDir(c.Dir);
                foreach (var vv in v)
                {
                    Vector3 rel = (Vector3)vv.Position - a;
                    float dist = (rel - Vector3.Dot(rel, d) * d).magnitude;
                    Assert.AreEqual(c.RadiusMm * s, dist, 1e-5);
                }
                int rings = v.Length / AirwayTubeMeshBuilder.Sides;
                Assert.AreEqual(2 * (rings - 1) * AirwayTubeMeshBuilder.Sides * 6, idx.Length);
                Assert.GreaterOrEqual(rings, 2);
                // AO range and rank check against camera distance
                var cam = EndoluminalCamera.ToScene(pose.PositionMm, f.Parent.EndMm, s);
                var order = new int[v.Length]; for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (x, y) => ((Vector3)v[x].Position - cam).sqrMagnitude.CompareTo(((Vector3)v[y].Position - cam).sqrMagnitude));
                int prev = 255;
                foreach (int i in order)
                {
                    int ao = v[i].Color.r;
                    Assert.That(ao / 255.0, Is.InRange(0.25 - 1.0 / 255, 1.0));
                    Assert.LessOrEqual(ao, prev + 1, "AO must not increase with camera distance");
                    prev = ao;
                }
                // parent drawn over 4 r_p only
                var p = f.Parent;
                b.Build(p, p.LenMm - 4 * rp, p.LenMm, f.Parent.EndMm, s, pose.PositionMm, rp, m);
                v = KitTestUtil.Verts(m);
                Vector3 pa = EndoluminalCamera.ToScene(p.PosMm, f.Parent.EndMm, s), pd = EndoluminalCamera.ToSceneDir(p.Dir);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var vv in v) { float t = Vector3.Dot((Vector3)vv.Position - pa, pd); lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); }
                Assert.LessOrEqual(hi - lo, 4 * rp * s + 1e-6);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }

        // Criterion: tree.json addresses (MP 10.2.4).
        [Test]
        public void Endo_ForkView_Validation()
        {
            var p = new AirwaySegmentMm("LR", double3.zero, new double3(0, 0, -1), 10, 2);
            var l = new AirwaySegmentMm("LRL", double3.zero, new double3(0, 1, -1), 5, 1.5);
            var r = new AirwaySegmentMm("LRR", double3.zero, new double3(0, -1, -1), 5, 1.5);
            Assert.DoesNotThrow(() => new ForkView(p, l, r));
            Assert.Throws<ArgumentException>(() => new ForkView(p, r, l));
            Assert.Throws<ArgumentException>(() => new ForkView(p, l, l));
            Assert.Throws<ArgumentException>(() => new ForkView(p, new AirwaySegmentMm("X", double3.zero, new double3(0, 0, 1), 1, 1), r));
            var root = EndoTestUtil.Fork(new double3(0, 0, -1), "");
            Assert.AreEqual("", root.Address);
            Assert.Throws<ArgumentException>(() => new AirwaySegmentMm(null, double3.zero, new double3(0, 0, 1), 1, 1));
            Assert.Throws<ArgumentException>(() => new AirwaySegmentMm("", double3.zero, double3.zero, 1, 1));
            Assert.Throws<ArgumentException>(() => new AirwaySegmentMm("", double3.zero, new double3(0, 0, 1), 0, 1));
            Assert.Throws<ArgumentException>(() => new AirwaySegmentMm("", double3.zero, new double3(0, 0, 1), 1, 0));
            Assert.AreEqual(1, math.length(new AirwaySegmentMm("", double3.zero, new double3(0, 0, 5), 1, 1).Dir), 1e-12);
        }
    }
}
