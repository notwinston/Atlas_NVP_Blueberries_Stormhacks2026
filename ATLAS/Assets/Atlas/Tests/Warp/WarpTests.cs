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
        // Translation (1.2, 0.3, -0.7) after a turn of 37 degrees about +Y, built with no engine call.
        static readonly float4x4 Trs = float4x4.TRS(new float3(1.2f, 0.3f, -0.7f), quaternion.RotateY(math.radians(37f)), new float3(1f, 1f, 1f));

        public static float4x4 ToF4(Matrix4x4 m) => new float4x4(
            m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33);

        static string ShaderDir
        {
            get
            {
                string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
                if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
                return Path.Combine(project, "Assets", "Atlas", "Rendering", "Shaders", "Resources");
            }
        }

        static string Strip(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", "");
        }

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
            var o2w = Trs;
            double max = 0;
            for (int i = 0; i < 100; i++)
            {
                var p = pts[i % 64] + i * 0.01f;
                var w = AtlasWarpCpu.AtlasWarp(p, float4x4.identity, 0f, 1.9f, float3.zero, o2w);
                var e = math.mul(Trs, new float4(p, 1f)).xyz;
                max = Math.Max(max, Math.Max(Math.Abs(w.x - e.x), Math.Max(Math.Abs(w.y - e.y), Math.Abs(w.z - e.z))));
            }
            TestContext.WriteLine($"mode none max diff {max}");
            Assert.Less(max, 1e-5);
        }

        public static string StrippedHlsl() => Strip(File.ReadAllText(Path.Combine(ShaderDir, "AtlasWarp.hlsl")));

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
            foreach (var good in new[] { "_HypL", "_HeadPosWS", "void AtlasWarpClip(float3 positionWS)" })
                StringAssert.Contains(good, s);
            // The include declares no uniform (lane A contract 10.1): AtlasCore.hlsl declares _Anchor and _WarpMode in the one
            // per-material block and _HypL and _HeadPosWS as globals, and the shader includes it first.
            Assert.AreEqual(0, Regex.Matches(s, @"^\s*(float\d?(x\d)?)\s+_\w+\s*;", RegexOptions.Multiline).Count);
            string core = Strip(File.ReadAllText(Path.Combine(ShaderDir, "AtlasCore.hlsl")));
            int blockStart = core.IndexOf("CBUFFER_START(UnityPerMaterial)", StringComparison.Ordinal), blockEnd = core.IndexOf("CBUFFER_END", StringComparison.Ordinal);
            Assert.Greater(blockStart, -1); Assert.Greater(blockEnd, blockStart);
            string block = core.Substring(blockStart, blockEnd - blockStart);
            StringAssert.IsMatch(@"float4x4\s+_Anchor;", block);
            StringAssert.IsMatch(@"float\s+_WarpMode;", block);
            string globals = core.Substring(blockEnd);
            StringAssert.IsMatch(@"float\s+_HypL;", globals);
            StringAssert.IsMatch(@"float4\s+_HeadPosWS;", globals);
            string surface = File.ReadAllText(Path.Combine(ShaderDir, "AtlasSurface.shader"));
            int coreAt = surface.IndexOf("\"AtlasCore.hlsl\"", StringComparison.Ordinal), warpAt = surface.IndexOf("\"AtlasWarp.hlsl\"", StringComparison.Ordinal);
            Assert.Greater(coreAt, -1); Assert.Greater(warpAt, coreAt);
            StringAssert.Contains("AtlasWarp(input.positionOS)", surface);
            StringAssert.Contains("AtlasWarpClip(input.positionWS)", surface);
        }

        // kernel.md §6.5; lane A contract 10.3: the four gates are _Gate0 .. _Gate3 of the one per-material block.
        [Test]
        public void Gates_IncludeContract()
        {
            string s = Strip(File.ReadAllText(Path.Combine(ShaderDir, "AtlasGates.hlsl")));
            foreach (var bad in new[] { "CBUFFER_START", "UnityPerMaterial", "#include", "#pragma", @"\b_Gates\b" })
                Assert.AreEqual(0, Count(s, bad), bad);
            Assert.AreEqual(0, Regex.Matches(s, @"^\s*(float\d?(x\d)?)\s+_\w+\s*;", RegexOptions.Multiline).Count);
            foreach (var good in new[] { "void AtlasGatesClip(float3 positionWS)", "ATLAS_GATES_INCLUDED", "_Gate0", "_Gate1", "_Gate2", "_Gate3", "_WorldSpaceCameraPos.xz" })
                StringAssert.Contains(good, s);
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

#if !ATLAS_HEADLESS
        // §9.5.6 "component that sets huge localBounds"; §8.3
        [Test, Category("Unity")]
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

        // §6.4 property ids; kernel.md §6.7; the gates are the four vectors of lane A contract 10.3
        [Test, Category("Unity")]
        public void ShaderIDs_MatchContract()
        {
            Assert.AreEqual(Shader.PropertyToID("_HypL"), AtlasShaderIDs.HypL);
            Assert.AreEqual(Shader.PropertyToID("_HeadPosWS"), AtlasShaderIDs.HeadPosWS);
            Assert.AreEqual(Shader.PropertyToID("_Anchor"), AtlasShaderIDs.Anchor);
            Assert.AreEqual(Shader.PropertyToID("_WarpMode"), AtlasShaderIDs.WarpMode);
            Assert.AreEqual(Shader.PropertyToID("_Gate0"), AtlasShaderIDs.Gate0);
            Assert.AreEqual(Shader.PropertyToID("_Gate1"), AtlasShaderIDs.Gate1);
            Assert.AreEqual(Shader.PropertyToID("_Gate2"), AtlasShaderIDs.Gate2);
            Assert.AreEqual(Shader.PropertyToID("_Gate3"), AtlasShaderIDs.Gate3);
        }
#endif
    }
}
