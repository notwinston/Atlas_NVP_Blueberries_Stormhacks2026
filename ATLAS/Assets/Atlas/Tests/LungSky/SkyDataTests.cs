using System;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public class SkyDataTests
    {
        [Test] // MP §10.2.4 step 8; §10.2.5 "11 R8 textures (1.4 MB)"
        public void B10_Load_Fixture_ShapesAndBudget()
        {
            var m = SkyMapsFactory.Fixture();
            for (int k = 0; k < 10; k++) Assert.AreEqual(131072, m.RiskBytes(k).Length);
            Assert.AreEqual(131072, m.RobustBytes.Length);
            Assert.AreEqual(512, m.Meta.width); Assert.AreEqual(256, m.Meta.height); Assert.AreEqual(10, m.Meta.phases);
            Assert.AreEqual(64, m.Meta.nside); Assert.AreEqual(5f, m.Meta.threshold_mm); Assert.AreEqual(0.2f, m.Meta.mm_per_unit, 1e-6f);
            Assert.AreEqual(1441792, SkyMaps.TextureBytes); Assert.LessOrEqual(SkyMaps.TextureBytes, 1.5e6);
            Assert.AreEqual(20, m.Meta.checks.Length);
        }

        [Test] // MP §10.2.4 step 8 (loader validation)
        public void B10_Load_RejectsBadInput()
        {
            var risk = new byte[10][];
            for (int k = 0; k < 10; k++) risk[k] = new byte[131072];
            risk[3] = new byte[131];
            var ex = Assert.Throws<InvalidDataException>(() => new SkyMaps(SkyMapsFactory.Meta(), risk, new byte[131072]));
            StringAssert.Contains("131072", ex.Message);
            for (int k = 0; k < 10; k++) risk[k] = new byte[131072];
            Assert.Throws<InvalidDataException>(() => new SkyMaps(SkyMapsFactory.Meta(schema: "atlas.sky/2"), risk, new byte[131072]));
            Assert.Throws<InvalidDataException>(() => new SkyMaps(SkyMapsFactory.Meta(threshold: 4f), risk, new byte[131072]));
        }

        [Test] // MP §10.2.4 step 8 "robust = AND over 10 phases >= 5 mm"
        public void B10_Robust_IsAndOfPhases()
        {
            var m = SkyMapsFactory.Fixture();
            int bad = 0;
            for (int j = 0; j < 256; j++)
                for (int i = 0; i < 512; i++)
                    if (m.IsRobust(i, j) != (m.MinClearanceMm(i, j) >= 5f - 1e-4f)) bad++;
            Assert.AreEqual(0, bad);
        }

        [Test] // D3 convention
        public void B10_Direction_RoundTripAndAxes()
        {
            var rng = new System.Random(1);
            for (int n = 0; n < 1000; n++)
            {
                double3 d = new double3(Gauss(rng), Gauss(rng), Gauss(rng)); d = math.normalize(d);
                SkyDirection.ToAzEl(d, out double az, out double el);
                Assert.Less(math.length(SkyDirection.FromAzEl(az, el) - d), 1e-12);
            }
            Assert.Less(math.length(SkyDirection.FromAzEl(0, 0) - new double3(0, 0, 1)), 1e-12);
            Assert.Less(math.length(SkyDirection.FromAzEl(90, 0) - new double3(1, 0, 0)), 1e-12);
            Assert.Less(math.length(SkyDirection.FromAzEl(-90, 0) - new double3(-1, 0, 0)), 1e-12);
            Assert.Less(math.length(SkyDirection.FromAzEl(37, 90) - new double3(0, 1, 0)), 1e-12);
        }

        static double Gauss(System.Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

        [Test] // PH-37 layout
        public void B10_Pixel_Convention()
        {
            SkyDirection.Pixel(-180, 90, out int i, out int j); Assert.AreEqual((0, 0), (i, j));
            SkyDirection.Pixel(179.9999, -89.9999, out i, out j); Assert.AreEqual((511, 255), (i, j));
            for (int jj = 0; jj < 256; jj++)
                for (int ii = 0; ii < 512; ii++)
                {
                    SkyDirection.Pixel((ii + 0.5) / 512 * 360 - 180, 90 - (jj + 0.5) / 256 * 180, out int a, out int b);
                    if (a != ii || b != jj) Assert.Fail("pixel " + ii + "," + jj + " -> " + a + "," + b);
                }
        }

        [Test] // beat g "robust-window share of sky in %"
        public void B10_RobustShare_EqualArea()
        {
            Assert.AreEqual(100.0, SkyMapsFactory.Synthetic(j => true).RobustSharePercent, 1e-9);
            Assert.AreEqual(50.0, SkyMapsFactory.Synthetic(j => j < 128).RobustSharePercent, 1e-9);
            double cap = 100.0 * (1 - Math.Cos(0.703125 * Math.PI / 180.0)) / 2;
            Assert.AreEqual(cap, SkyMapsFactory.Synthetic(j => j == 0).RobustSharePercent, 1e-12);
            var m = SkyMapsFactory.Fixture();
            double s = 0;
            for (int j = 0; j < 256; j++)
            {
                int n = 0; for (int i = 0; i < 512; i++) if (m.IsRobust(i, j)) n++;
                double elMid = (90 - (j + 0.5) / 256 * 180) * Math.PI / 180;
                s += Math.Cos(elMid) * (Math.PI / 256) * (2 * Math.PI / 512) * n / (4 * Math.PI) * 100;
            }
            Assert.AreEqual(s, m.RobustSharePercent, 0.01);
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")] // MP §8.1 data textures; §10.2.5
        public void B10_Textures_R8_Linear_PointClamp_Once()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "no graphics device (-nographics)");
            var m = SkyMapsFactory.Fixture();
            using (var t = new SkyTextures(m))
            {
                Assert.AreEqual(11, t.Uploads);
                for (int k = 0; k < 11; k++)
                {
                    var tex = k < 10 ? t.Phase(k) : t.Robust;
                    Assert.AreEqual(TextureFormat.R8, tex.format); Assert.AreEqual(512, tex.width); Assert.AreEqual(256, tex.height);
                    Assert.AreEqual(1, tex.mipmapCount); Assert.AreEqual(FilterMode.Point, tex.filterMode);
                    Assert.AreEqual(TextureWrapMode.Clamp, tex.wrapMode); Assert.IsFalse(tex.isDataSRGB);
                }
                Assert.AreEqual(11, t.Uploads);
            }
        }

        [Test, Category("Unity")] // MP §10.2.5 "one inverted sphere"
        public void B10_Mesh_Inverted()
        {
            var mesh = NeedleSkyMesh.Build();
            try
            {
                var v = mesh.vertices; var t = mesh.triangles;
                Assert.LessOrEqual(t.Length / 3, 1100);
                foreach (var p in v) Assert.AreEqual(2.0f, p.magnitude, 1e-5f);
                for (int n = 0; n < t.Length; n += 3)
                {
                    Vector3 a = v[t[n]], b = v[t[n + 1]], c = v[t[n + 2]];
                    Assert.Less(Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f), 0f, "triangle " + n / 3);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
#endif
    }
}
