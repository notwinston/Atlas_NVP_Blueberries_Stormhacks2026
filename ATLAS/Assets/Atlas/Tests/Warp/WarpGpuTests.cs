using System;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace Atlas.Warp.Tests
{
    public class WarpGpuTests
    {
        const string Path = "Assets/Atlas/Tests/Warp/AtlasWarpProbe.compute";

        // §9.5.6 P1-B03 acceptance: GPU positions match the kernel CPU reference to 1 mm for rho <= 3 (true GPU)
        [Test]
        public void Warp_MatchesKernel_Gpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders)
                Assert.Ignore("P1-B03 GPU half deferred: no graphics device (-nographics)");
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Path);
            Assert.IsNotNull(cs);
            foreach (var m in UnityEditor.ShaderUtil.GetComputeShaderMessages(cs))
                Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, m.severity, m.message);
            Assert.IsTrue(cs.HasKernel("WarpProbe"));
            int k = cs.FindKernel("WarpProbe");
            var inBuf = new ComputeBuffer(64, 16);
            var outBuf = new ComputeBuffer(64, 16);
            try
            {
                cs.SetBuffer(k, "_In", inBuf);
                cs.SetBuffer(k, "_Out", outBuf);
                cs.SetInt("_Count", 64);
                var data = new Vector4[64];
                foreach (double kappa in new[] { -1.0, 1.0 })
                    foreach (double L in new[] { 1.4, 1.9, 3.0 })
                    {
                        var rng = new Random(WarpSamples.Seed);
                        var frames = WarpSamples.Frames(kappa, 60, rng);
                        double maxErr = 0, maxRho = 0; int accepted = 0;
                        foreach (var (u, f, a) in frames)
                        {
                            var pts = WarpSamples.Points(kappa, L, rng);
                            for (int i = 0; i < 64; i++) data[i] = new Vector4(pts[i].x, pts[i].y, pts[i].z, 0);
                            inBuf.SetData(data);
                            cs.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(a));
                            cs.SetFloat(AtlasShaderIDs.WarpMode, kappa < 0 ? 1f : 2f);
                            cs.SetFloat(AtlasShaderIDs.HypL, (float)L);
                            cs.SetMatrix("_ProbeObjectToWorld", Matrix4x4.identity);
                            foreach (var head in WarpSamples.Heads)
                            {
                                cs.SetVector(AtlasShaderIDs.HeadPosWS, new Vector4(head.x, head.y, head.z, 0));
                                cs.Dispatch(k, 1, 1, 1);
                                var o = new Vector4[64];
                                outBuf.GetData(o);
                                for (int i = 0; i < 64; i++)
                                {
                                    var rf = AtlasWarpReference.Warp(pts[i], a, kappa, L, head);
                                    double rho = math.length(rf.xz - (double2)head.xz) / L;
                                    if (rho > 3.0) continue;
                                    double err = WarpTests.Cmax(new double3(o[i].x, o[i].y, o[i].z) - rf);
                                    accepted++; maxRho = Math.Max(maxRho, rho); maxErr = Math.Max(maxErr, err);
                                    Assert.AreEqual((double)pts[i].y, (double)o[i].y);
                                }
                            }
                        }
                        TestContext.WriteLine($"GPU kappa={kappa} L={L}: max err {maxErr * 1000:F5} mm; accepted {accepted}; max rho {maxRho:F3}");
                        Assert.Less(maxErr, 1e-3);
                        Assert.GreaterOrEqual(accepted, 3000);
                        Assert.GreaterOrEqual(maxRho, 2.9);
                    }

                var trs = Matrix4x4.TRS(new Vector3(1.2f, 0.3f, -0.7f), Quaternion.Euler(0, 37, 0), Vector3.one);
                var p0 = WarpSamples.Points(-1, 1.9, new Random(1));
                for (int i = 0; i < 64; i++) data[i] = new Vector4(p0[i].x, p0[i].y, p0[i].z, 0);
                inBuf.SetData(data);
                cs.SetFloat(AtlasShaderIDs.WarpMode, 0f);
                cs.SetMatrix("_ProbeObjectToWorld", trs);
                cs.Dispatch(k, 1, 1, 1);
                var o0 = new Vector4[64];
                outBuf.GetData(o0);
                double mx = 0;
                for (int i = 0; i < 64; i++)
                {
                    var e = trs.MultiplyPoint3x4(new Vector3(p0[i].x, p0[i].y, p0[i].z));
                    mx = Math.Max(mx, Math.Max(Math.Abs(o0[i].x - e.x), Math.Max(Math.Abs(o0[i].y - e.y), Math.Abs(o0[i].z - e.z))));
                }
                TestContext.WriteLine($"GPU mode none max diff {mx}");
                Assert.Less(mx, 1e-5);
            }
            finally { inBuf.Release(); outBuf.Release(); }
        }
    }
}
