using System;
using System.IO;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Random = System.Random;

namespace Atlas.Warp.Tests
{
    public class WarpTests
    {
        static readonly Matrix4x4 Trs = Matrix4x4.TRS(new Vector3(1.2f, 0.3f, -0.7f), Quaternion.Euler(0, 37, 0), Vector3.one);

        public static float4x4 ToF4(Matrix4x4 m) => new float4x4(
            m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33);

        public static double Cmax(double3 a) => math.cmax(math.abs(a));

        // §9.5.6 P1-B03 acceptance (CPU float half); §4.6
        [TestCase(-1.0, 1.4)] [TestCase(-1.0, 1.9)] [TestCase(-1.0, 3.0)]
        [TestCase(1.0, 1.4)] [TestCase(1.0, 1.9)] [TestCase(1.0, 3.0)]
        public void Warp_MatchesKernel_Cpu(double kappa, double L)
        {
            var rng = new Random(WarpSamples.Seed);
            var frames = WarpSamples.Frames(kappa, 200, rng);
            double maxErr = 0, rhoAtMax = 0, maxRho = 0, maxErrFar = 0;
            int accepted = 0;
            foreach (var (u, f, a) in frames)
            {
                var m = ToF4(WarpAnchor.ToMatrix(a));
                var pts = WarpSamples.Points(kappa, L, rng);
                foreach (var head in WarpSamples.Heads)
                    foreach (var p in pts)
                    {
                        var rf = AtlasWarpReference.Warp(p, a, kappa, L, head);
                        double rho = math.length(rf.xz - (double2)head.xz) / L;
                        var mir = AtlasWarpCpu.AtlasWarp(p, m, kappa < 0 ? 1f : 2f, (float)L, head, float4x4.identity);
                        double err = Cmax((double3)mir - rf);
                        Assert.AreEqual((double)p.y, (double)mir.y, "height passes through");
                        if (rho <= 3.0)
                        {
                            accepted++; maxRho = Math.Max(maxRho, rho);
                            if (err > maxErr) { maxErr = err; rhoAtMax = rho; }
                        }
                        else if (rho <= 4.0) maxErrFar = Math.Max(maxErrFar, err);
                    }
            }
            TestContext.WriteLine($"kappa={kappa} L={L}: max err {maxErr * 1000:F5} mm at rho {rhoAtMax:F3}; accepted {accepted}; max rho {maxRho:F3}; 3<rho<=4 max err {maxErrFar * 1000:F5} mm");
            Assert.Less(maxErr, 1e-3);
            Assert.GreaterOrEqual(accepted, 10000);
            Assert.GreaterOrEqual(maxRho, 2.9);
        }

        // §8.3 "Modes: none"; kernel.md §6.1
        [Test]
        public void Warp_ModeNone_IsObjectTransform()
        {
            var rng = new Random(WarpSamples.Seed);
            var pts = WarpSamples.Points(-1, 1.9, rng);
            var o2w = ToF4(Trs);
            double max = 0;
            for (int i = 0; i < 100; i++)
            {
                var p = pts[i % 64] + i * 0.01f;
                var w = AtlasWarpCpu.AtlasWarp(p, float4x4.identity, 0f, 1.9f, float3.zero, o2w);
                var e = Trs.MultiplyPoint3x4(new Vector3(p.x, p.y, p.z));
                max = Math.Max(max, Math.Max(Math.Abs(w.x - e.x), Math.Max(Math.Abs(w.y - e.y), Math.Abs(w.z - e.z))));
            }
            TestContext.WriteLine($"mode none max diff {max}");
            Assert.Less(max, 1e-5);
        }

        public static string StrippedHlsl()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "Atlas/Rendering/Shaders/AtlasWarp.hlsl"));
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", "");
        }

        static int Count(string s, string pattern) => Regex.Matches(s, pattern).Count;

        // §9.5.6 "exp is evaluated once per vertex"; §8.3 "sinh and cosh from it". By inspection each AtlasWarp call runs exactly one Exp.
        [Test]
        public void Warp_ExpOncePerVertex()
        {
            string s = StrippedHlsl();
            Assert.AreEqual(1, Count(s, @"\bexp\s*\("));
            Assert.AreEqual(1, Count(s, @"\bsincos\s*\("));
            Assert.AreEqual(0, Count(s, @"\b(sinh|cosh|tanh|exp2|pow|sin|cos)\s*\("));
            Assert.AreEqual(2, Count(s, @"\bExpH2\s*\("));
            Assert.AreEqual(2, Count(s, @"\bLogH2\s*\("));
            Assert.AreEqual(2, Count(s, @"\bExpS2\s*\("));
            Assert.AreEqual(2, Count(s, @"\bLogS2\s*\("));
        }

        // kernel.md §6.1, §6.2, §6.6; §8.3 "no new shader keyword"; mechanical half of SRP-Batcher criterion
        [Test]
        public void Warp_IncludeContract()
        {
            string s = StrippedHlsl();
            foreach (var bad in new[] { "CBUFFER_START", "UnityPerMaterial", "#include", "#pragma", @"\bhalf\d?\b", @"\breal\d?\b", "min16float" })
                Assert.AreEqual(0, Count(s, bad), bad);
            foreach (var good in new[] { "float3 AtlasWarp(float3 positionOS)", "ATLAS_WARP_INCLUDED", "_Anchor", "_WarpMode" })
                StringAssert.Contains(good, s);
            StringAssert.IsMatch(@"float\s+_HypL;", s);
            StringAssert.IsMatch(@"float3\s+_HeadPosWS;", s);
            var ms = Regex.Matches(s, @"^\s*(float\d?(x\d)?)\s+_\w+\s*;", RegexOptions.Multiline);
            Assert.AreEqual(2, ms.Count);
            StringAssert.Contains("_HypL", ms[0].Value);
            StringAssert.Contains("_HeadPosWS", ms[1].Value);
        }

        // §4.6 "computed in double, cast to float last"; kernel.md §6.2 upload layout
        [Test]
        public void Warp_AnchorUploadLayout()
        {
            var rng = new Random(WarpSamples.Seed);
            var frames = WarpSamples.Frames(-1, 100, rng);
            foreach (var (u, f, a) in frames)
            {
                var m = WarpAnchor.ToMatrix(a);
                int nonzero = 0;
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                    {
                        if (r < 3 && c < 3) Assert.AreEqual((float)a.M[c][r], m[r, c]);
                        else if (r == 3 && c == 3) Assert.AreEqual(1f, m[3, 3]);
                        else Assert.AreEqual(0f, m[r, c]);
                    }
                Assert.AreEqual((u.Inverse() * f).M, a.M);
                Assert.AreEqual(nonzero, 0);
            }
        }

        // MP §11.1 Float32_Upload (owner B)
        [Test]
        public void Float32_Upload()
        {
            var rng = new Random(WarpSamples.Seed);
            double max = 0;
            for (int i = 0; i < 1000; i++)
            {
                double al = (rng.NextDouble() * 2 - 1) * Math.PI, be = (rng.NextDouble() * 2 - 1) * Math.PI, d = (rng.NextDouble() * 2 - 1) * Math.PI;
                var a = Iso2.Rot(al) * Iso2.Boost(4.35 * new double2(Math.Cos(d), Math.Sin(d)), -1) * Iso2.Rot(be);
                var m = WarpAnchor.ToMatrix(a);
                double x = m[0, 2], y = m[1, 2], z = m[2, 2];
                max = Math.Max(max, Math.Abs(x * x + y * y - z * z + 1));
            }
            TestContext.WriteLine($"Float32_Upload max |<p,p>+1| = {max}");
            Assert.Less(max, 1e-3);
        }

        // §9.5.6 "component that sets huge localBounds"; §8.3
        [Test]
        public void Warp_HugeLocalBounds()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var go2 = new GameObject("bare", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                go.AddComponent<AtlasWarpBounds>();
                var r = go.GetComponent<MeshRenderer>();
                Assert.AreEqual(Vector3.zero, r.localBounds.center);
                Assert.AreEqual(new Vector3(1000, 1000, 1000), r.localBounds.size);
                var r2 = go2.GetComponent<MeshRenderer>();
                AtlasWarpBounds.Apply(r2);
                Assert.AreEqual(Vector3.zero, r2.localBounds.center);
                Assert.AreEqual(new Vector3(1000, 1000, 1000), r2.localBounds.size);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(go2); }
        }

        // §6.4 property ids; kernel.md §6.7
        [Test]
        public void ShaderIDs_MatchContract()
        {
            Assert.AreEqual(Shader.PropertyToID("_HypL"), AtlasShaderIDs.HypL);
            Assert.AreEqual(Shader.PropertyToID("_HeadPosWS"), AtlasShaderIDs.HeadPosWS);
            Assert.AreEqual(Shader.PropertyToID("_Anchor"), AtlasShaderIDs.Anchor);
            Assert.AreEqual(Shader.PropertyToID("_WarpMode"), AtlasShaderIDs.WarpMode);
            Assert.AreEqual(Shader.PropertyToID("_Gates"), AtlasShaderIDs.Gates);
        }
    }
}
