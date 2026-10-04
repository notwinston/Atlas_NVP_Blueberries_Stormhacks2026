using System;
using System.IO;
using Atlas.Kernel;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Harness = Atlas.Blades.FieldFit.Tests.FieldFitFeatureTests.Harness;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B05 tests, Master Plan §10.4.5; Brief §9. Seeded System.Random(20261003).
    public class LensGuardTests
    {
        const double Deg = Math.PI / 180.0;

        [Serializable] class OffEv { public string cause; public float d_m; public float dPrev_m; public int frame; }

        static string EvidenceDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P3-B05"));
            Directory.CreateDirectory(d); return d;
        }

        [Test, Description("P3-B05 acceptance: auto-off within 1 frame of 0.5 m displacement, 10 of 10 simulated trials (Master Plan §10.4.5, §10.4.8)")]
        public void AutoOff_Within1Frame_10Of10()
        {
            var rng = new System.Random(20261003);
            float[] speeds = { 0.2f, 0.5f, 0.8f, 1.0f, 1.5f };
            int pass = 0; var log = new System.Text.StringBuilder();
            for (int trial = 0; trial < 10; trial++)
                using (var h = new Harness())
                {
                    // seeded random 3D unit direction
                    double z = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * 2 * Math.PI, r = Math.Sqrt(1 - z * z);
                    var dir = new Vector3((float)(r * Math.Cos(a)), (float)z, (float)(r * Math.Sin(a)));
                    float speed = speeds[trial % speeds.Length];
                    Vector3 Noise() => new Vector3((float)(rng.NextDouble() * 4 - 2), (float)(rng.NextDouble() * 4 - 2), (float)(rng.NextDouble() * 4 - 2)) * 0.001f;
                    Vector3 origin = new Vector3(1f, 1.5f, 2f);
                    Vector3 p0 = origin + Noise(), pk;
                    h.Rig.HeadPosition = p0; h.Step();             // frame 0: lens turns on, guard armed at p0
                    bool ok = h.F.LensActive; int firstOver = -1;
                    for (int k = 1; k < 800 && ok; k++)
                    {
                        pk = origin + dir * (speed * k / 60f) + Noise();
                        bool over = (pk - p0).magnitude > 0.5f;
                        h.Rig.HeadPosition = pk; h.Step();
                        if (!over) { ok = h.F.LensActive; continue; }
                        firstOver = k;
                        ok = !h.F.LensActive && !h.P.On && h.Rig.BlitMaterial == null;
                        var last = h.Ev.Log[h.Ev.Log.Count - 1];
                        ok &= last.Key == "lensOff";
                        var ev = JsonUtility.FromJson<OffEv>(last.Value);
                        ok &= ev.cause == "displacement" && ev.frame == h.Frame;
                        break;
                    }
                    ok &= firstOver > 0;
                    if (ok) pass++;
                    log.AppendLine($"trial {trial}: speed {speed} m/s first frame over 0.5 m = {firstOver}: {(ok ? "pass" : "FAIL")}");
                }
            File.WriteAllText(Path.Combine(EvidenceDir(), "auto_off_sim.txt"), "P3-B05 AutoOff_Within1Frame_10Of10 (Editor simulation, 60 Hz, +-2 mm noise)\n" + log + $"passed {pass} of 10\n");
            Assert.AreEqual(10, pass, "trials passed");
        }

        [Test, Description("P3-B05 acceptance: auto-off is strictly > 0.5 m: 0.5000 keeps the lens on, 0.5001 turns it off (Master Plan §10.4.5)")]
        public void AutoOff_Strict()
        {
            using (var h = new Harness())
            {
                h.Rig.HeadPosition = Vector3.zero; h.Step();
                h.Rig.HeadPosition = new Vector3(0.5f, 0, 0); h.Step();
                Assert.IsTrue(h.F.LensActive, "d = 0.5000 stays on");
                h.Rig.HeadPosition = new Vector3(0.5001f, 0, 0); h.Step();
                Assert.IsFalse(h.F.LensActive, "d = 0.5001 turns off");
            }
        }

        [Test, Description("P3-B05 acceptance: tap-off turns the lens off in that frame with cause tap (Master Plan §10.4.5)")]
        public void TapOff_SameFrame()
        {
            using (var h = new Harness())
            {
                h.Step(); Assert.IsTrue(h.F.LensActive);
                h.F.TapOff(); h.Step();
                Assert.IsFalse(h.F.LensActive); Assert.IsFalse(h.P.On); Assert.IsNull(h.Rig.BlitMaterial);
                StringAssert.Contains("\"cause\":\"tap\"", h.Ev.Log[h.Ev.Log.Count - 1].Value);
            }
        }

        [Test, Description("P3-B05 acceptance: lens.on = 0 turns off in that frame (cause param); after a guard off the lens stays off until lens.on reads 0 then 1 (Brief §8 latch)")]
        public void ParamOff_SameFrame_And_Latch()
        {
            using (var h = new Harness())
            {
                h.Step(); h.P.On = false; h.Step();
                Assert.IsFalse(h.F.LensActive);
                StringAssert.Contains("\"cause\":\"param\"", h.Ev.Log[h.Ev.Log.Count - 1].Value);
                // latch: SetLensOn cannot change the fake
                h.P.On = true; h.P.FailSetOn = true; h.Rig.HeadPosition = Vector3.zero; h.Step();
                Assert.IsTrue(h.F.LensActive);
                h.Rig.HeadPosition = new Vector3(0.7f, 0, 0); h.Step();
                Assert.IsFalse(h.F.LensActive); Assert.IsTrue(h.P.On, "the fake ignored SetLensOn(false)");
                for (int i = 0; i < 30; i++) { h.Step(); Assert.IsFalse(h.F.LensActive, "latched at " + i); }
                h.P.On = false; h.Step(); Assert.IsFalse(h.F.LensActive);
                h.P.On = true; h.Rig.HeadPosition = new Vector3(0.7f, 0, 0); h.Step();
                Assert.IsTrue(h.F.LensActive, "0 then 1 re-activates");
            }
        }

        [Test, Description("P3-B05 acceptance: a second activation measures from the new start point (Master Plan §10.4.5)")]
        public void ReArm_NewStart()
        {
            using (var h = new Harness())
            {
                h.Rig.HeadPosition = Vector3.zero; h.Step();
                h.P.On = false; h.Step();
                h.Rig.HeadPosition = new Vector3(5, 0, 0);                 // far from the first start
                h.P.On = true; h.Step();
                Assert.IsTrue(h.F.LensActive, "armed at the new point, no immediate displacement off");
                h.Rig.HeadPosition = new Vector3(5.4f, 0, 0); h.Step(); Assert.IsTrue(h.F.LensActive);
                h.Rig.HeadPosition = new Vector3(5.6f, 0, 0); h.Step(); Assert.IsFalse(h.F.LensActive);
            }
        }

        [Test, Description("P3-B05 acceptance: with no badge font the lens refuses to start and logs one error (Brief §8)")]
        public void NoFont_RefusesLens()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("badge is not ready"));
            using (var h = new Harness(false, true))
            {
                Assert.IsNull(h.Settings.badgeFont);
                for (int i = 0; i < 20; i++) h.Step();
                Assert.IsFalse(h.F.LensActive); Assert.IsNull(h.Rig.BlitMaterial); Assert.AreEqual(0, h.Ev.Count("lensOn"));
            }
        }

        [Test, Description("P3-B05 acceptance: lensOn / lensOff carry the §8 fields with d_m > 0.5 >= dPrev_m (Master Plan §10.4.7)")]
        public void Events_OnOff()
        {
            using (var h = new Harness())
            {
                h.Rig.HeadPosition = Vector3.zero; h.Step();
                var on = h.Ev.Log[0];
                Assert.AreEqual("lensOn", on.Key);
                foreach (var f in new[] { "\"profile\":1", "\"E\":", "\"rho0\":", "\"e1\":", "\"t\":", "\"kStar\":2.918" }) StringAssert.Contains(f, on.Value);
                h.Rig.HeadPosition = new Vector3(0.45f, 0, 0); h.Step();
                h.Rig.HeadPosition = new Vector3(0.62f, 0, 0); h.Step();
                var off = h.Ev.Log[h.Ev.Log.Count - 1];
                Assert.AreEqual("lensOff", off.Key);
                var e = JsonUtility.FromJson<OffEv>(off.Value);
                Assert.AreEqual("displacement", e.cause); Assert.Greater(e.d_m, 0.5f); Assert.LessOrEqual(e.dPrev_m, 0.5f); Assert.AreEqual(h.Frame, e.frame);
            }
        }

        [Test, Description("P3-B05 acceptance (design choice, Master Plan §10.4.5): each badge rect centre maps to e_out = 15 deg +-0.5 and alpha = -90 deg +-1 under D_test")]
        public void Badge_Position()
        {
            var g = TestGeometry.Dtest;
            using (var lut = new LensLut())
            {
                lut.SetEyeGeometry(0, g); lut.SetEyeGeometry(1, g);
                using (var b = LensBadge.CreateForTest(Texture2D.whiteTexture, lut))
                    foreach (var r in new[] { b.RectLeft, b.RectRight })
                    {
                        Assert.Greater(r.z, 0); Assert.Greater(r.w, 0);
                        var q = new Vector2(r.x + r.z * 0.5f, r.y + r.w * 0.5f);
                        var v = g.ViewTangent(q);
                        double eOut = Math.Atan(Math.Sqrt((double)v.x * v.x + (double)v.y * v.y)) / Deg, alpha = Math.Atan2(v.y, v.x) / Deg;
                        Assert.AreEqual(15.0, eOut, 0.5); Assert.AreEqual(-90.0, alpha, 1.0);
                        // size: about 8 x 2 deg
                        var lo = g.ViewTangent(new Vector2(r.x, r.y)); var hi = g.ViewTangent(new Vector2(r.x + r.z, r.y + r.w));
                        Assert.AreEqual(8.0, (Math.Atan(hi.x) - Math.Atan(lo.x)) / Deg, 1.0);
                    }
            }
        }

        [Test, Description("P3-B05 acceptance: the badge builds from a real TMP font when a graphics device exists; none without a font (Master Plan §10.4.5)")]
        public void Badge_FromFont()
        {
            using (var lut = new LensLut())
            {
                lut.SetEyeGeometry(0, TestGeometry.Dtest); lut.SetEyeGeometry(1, TestGeometry.Dtest);
                using (var none = LensBadge.Create(null, TestGeometry.Dtest, TestGeometry.Dtest, lut)) Assert.IsFalse(none.Ready);
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device (DV-B03-g)");
                var font = TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                Assert.IsNotNull(font, "TMP font (TMP Essentials must be in the project)");
                using (var b = LensBadge.Create(font, TestGeometry.Dtest, TestGeometry.Dtest, lut)) { Assert.IsTrue(b.Ready); Assert.AreEqual(256, b.Texture.width); Assert.AreEqual(64, b.Texture.height); }
            }
        }

        [Test, Category("GPU"), Description("P3-B05 acceptance: with a white badge texture every pixel inside both eyes' rects is white under every profile and the tunnel mask (Master Plan §10.4.5)")]
        public void Badge_VisibleWhileActive()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("GPU test needs a graphics device; run without -nographics (DV-B03-g)");
            var g = TestGeometry.Dtest;
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(FieldFitFeatureTests.ShaderPath);
            var maskTex = MaskFactory.Disc(512, 30, 10);
            foreach (var profile in new[] { LensProfile.None, LensProfile.Morph, LensProfile.Tanh })
                foreach (var useMask in new[] { false, true })
                    using (var lut = new LensLut())
                    using (var mask = useMask ? FieldLossMask.FromTexture(maskTex, 30f) : FieldLossMask.Absent)
                    using (var badge = Prep(lut, g, profile, mask))
                    {
                        var mat = new Material(shader);
                        var d = LensParams.Default; var p = new LensParams(profile, d.E, d.Rho0, d.E1, 1.0);
                        mat.SetTexture(FieldFitShaderIDs.SourceL, Texture2D.blackTexture); mat.SetTexture(FieldFitShaderIDs.SourceR, Texture2D.blackTexture);
                        mat.SetTexture(FieldFitShaderIDs.LutL, lut.Left); mat.SetTexture(FieldFitShaderIDs.LutR, lut.Right);
                        mat.SetFloat(FieldFitShaderIDs.TanE, (float)Math.Tan(p.E));
                        var fr = profile == LensProfile.None ? g.Frustum : new Vector4(-1e4f, 1e4f, -1e4f, 1e4f);
                        mat.SetVector(FieldFitShaderIDs.FrustumL, fr); mat.SetVector(FieldFitShaderIDs.FrustumR, fr);
                        mat.SetTexture(FieldFitShaderIDs.Badge, badge.Texture);
                        mat.SetVector(FieldFitShaderIDs.BadgeRectL, badge.RectLeft); mat.SetVector(FieldFitShaderIDs.BadgeRectR, badge.RectRight);
                        int W = 2796, H = 1290;
                        var rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                        var cmd = new CommandBuffer(); cmd.SetRenderTarget(rt);
                        cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
                        Graphics.ExecuteCommandBuffer(cmd);
                        var req = AsyncGPUReadback.Request(rt); req.WaitForCompletion();
                        var data = req.GetData<Vector4>();
                        for (int eye = 0; eye < 2; eye++)
                        {
                            var r = eye == 0 ? badge.RectLeft : badge.RectRight;
                            int x0 = Mathf.CeilToInt(r.x * (W / 2) + 1), x1 = Mathf.FloorToInt((r.x + r.z) * (W / 2) - 1);
                            int y0 = Mathf.CeilToInt(r.y * H + 1), y1 = Mathf.FloorToInt((r.y + r.w) * H - 1);
                            for (int y = y0; y <= y1; y++)
                                for (int x = x0; x <= x1; x++)
                                {
                                    var c = data[y * W + eye * (W / 2) + x];
                                    Assert.GreaterOrEqual(Mathf.Min(c.x, c.y, c.z), 0.99f, $"{profile} mask={useMask} eye {eye} px {x},{y}");
                                }
                        }
                        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(mat); cmd.Release();
                    }
            UnityEngine.Object.DestroyImmediate(maskTex);
        }

        static LensBadge Prep(LensLut lut, TestGeometry g, LensProfile profile, FieldLossMask mask)
        {
            lut.SetEyeGeometry(0, g); lut.SetEyeGeometry(1, g);
            var d = LensParams.Default;
            Assert.IsTrue(lut.Rebuild(new LensParams(profile, d.E, d.Rho0, d.E1, 1.0), mask));
            return LensBadge.CreateForTest(Texture2D.whiteTexture, lut);
        }
    }
}
