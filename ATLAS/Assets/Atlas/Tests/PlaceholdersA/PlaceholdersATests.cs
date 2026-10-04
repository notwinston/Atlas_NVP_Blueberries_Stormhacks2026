using System;
using System.Collections.Generic;
using System.Reflection;
using Atlas.Blades.Lung;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Atlas.PlaceholdersA.Tests
{
    // Self-tests of the lane A placeholders: each names the Master Plan criterion it checks.
    // They use only the public contract surface.
    public class PlaceholdersATests
    {
        // Test double implementing the contract interface (brief 0.1); not a subclass of a placeholder.
        sealed class Probe : ISpaceModule, IKernelStage, IUploadStage
        {
            readonly List<string> _log;
            public Probe(SpaceId id, List<string> log) { Id = id; _log = log; }
            public SpaceId Id { get; }
            public bool Registered => Id != SpaceId.S2 && Id != SpaceId.S4;   // Master Plan §4.9
            public void Configure(RoomModel room, ParamRegistry registry) => _log.Add(Id + ".Configure");
            public void Enter(in WalkerPose start) => _log.Add(Id + ".Enter");
            public void Tick(in FrameContext frame) => _log.Add(Id + ".Tick");
            public void Exit() => _log.Add(Id + ".Exit");
            public void WriteState(ref StateMessage state) => _log.Add(Id + ".WriteState");
            public void KernelStep(in FrameContext frame) => _log.Add(Id + ".Kernel");
            public void Upload(in FrameContext frame) => _log.Add(Id + ".Upload");
        }

        static FrameContext Frame(long n) =>
            new FrameContext(n, n / 60.0, 1f / 60f, float3.zero, quaternion.identity, true, false);

        [Test] // App. B.1: State.spaceId is u8 0-6
        public void Placeholder_SpaceIdMatchesStateSpaceId()
        {
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(SpaceId)));
            var values = (SpaceId[])Enum.GetValues(typeof(SpaceId));
            Assert.AreEqual(7, values.Length);
            for (int i = 0; i < 7; i++) { Assert.AreEqual((byte)i, (byte)values[i]); Assert.AreEqual("S" + i, values[i].ToString()); }
        }

        [Test] // Master Plan §6.3: step 4 before 5 before 6, once each per Tick
        public void Placeholder_SpaceLab_StepOrder()
        {
            var log = new List<string>();
            var lab = new SpaceLab(null, null);
            lab.Register(new Probe(SpaceId.S0, log));
            Assert.IsNull(lab.Active);
            lab.Request(SpaceId.S0);
            Assert.IsTrue(lab.Switching);
            lab.Tick(Frame(0));
            Assert.IsFalse(lab.Switching);
            Assert.AreEqual(SpaceId.S0, lab.Active.Id);
            Assert.AreEqual(new[] { "S0.Configure", "S0.Enter", "S0.Kernel", "S0.Tick", "S0.Upload" }, log);
            log.Clear();
            lab.Tick(Frame(1));
            Assert.AreEqual(new[] { "S0.Kernel", "S0.Tick", "S0.Upload" }, log);
        }

        [Test] // Master Plan §6.2, §9.3 (PH-25): Spaces reach the Home meshes through SpaceLab.Home, not Atlas.Scan
        public void Placeholder_SpaceLab_HomeProperty()
        {
            var lab = new SpaceLab(null, null);
            Assert.IsNull(lab.Home);
            var shell = new NullShell();
            lab.Home = shell;
            Assert.AreSame(shell, lab.Home);
        }

        sealed class NullShell : IHomeShell
        {
            public Mesh Floor => null; public Mesh Walls => null; public int ObjectCount => 0;
            public RoomObject ObjectRecord(int i) => null; public Mesh ObjectMesh(int i) => null;
            public event Action<int> ObjectMeshChanged { add { } remove { } }
        }

        [Test] // Master Plan §6.2 (World): one module per SpaceId, a duplicate throws; switch exits the old space first
        public void Placeholder_SpaceLab_RegisterAndSwitch()
        {
            var log = new List<string>();
            var lab = new SpaceLab(null, null);
            lab.Register(new Probe(SpaceId.S0, log));
            lab.Register(new Probe(SpaceId.S6, log));
            Assert.Throws<InvalidOperationException>(() => lab.Register(new Probe(SpaceId.S0, log)));
            lab.Request(SpaceId.S0); lab.Tick(Frame(0)); log.Clear();
            lab.Request(SpaceId.S6); lab.Tick(Frame(1));
            Assert.AreEqual(new[] { "S0.Exit", "S6.Configure", "S6.Enter" }, log.GetRange(0, 3));
            Assert.AreEqual(SpaceId.S6, lab.Active.Id);
        }

        [Test] // Master Plan §8.1 palette 128^2; 8 px cells
        public void Placeholder_AtlasPalette_CellUV()
        {
            Assert.AreEqual(128, AtlasPalette.Size);
            Assert.AreEqual(16, AtlasPalette.CellsPerSide);
            Assert.AreEqual(new Vector2(0.5f / 16, 0.5f / 16), AtlasPalette.CellUV(0));
            Assert.AreEqual(new Vector2(15.5f / 16, 15.5f / 16), AtlasPalette.CellUV(255));
            Assert.AreEqual(new Vector2(1.5f / 16, 2.5f / 16), AtlasPalette.CellUV(33));
        }

        [Test] // Master Plan §8.3: property ids exist
        public void Placeholder_AtlasShaderIDs_Resolve()
        {
            Assert.AreEqual(Shader.PropertyToID("_PaletteTex"), AtlasShaderIDs.PaletteTex);
            Assert.AreEqual(Shader.PropertyToID("_FogStart"), AtlasShaderIDs.FogStart);
            Assert.AreEqual(Shader.PropertyToID("_FogEnd"), AtlasShaderIDs.FogEnd);
            Assert.AreEqual(Shader.PropertyToID("_FogColor"), AtlasShaderIDs.FogColor);
            Assert.AreEqual(Shader.PropertyToID("_Tint"), AtlasShaderIDs.Tint);
        }

        [Test] // Master Plan §11.1 Shader_Contract: SRP Batcher compatible, <= 8 variants, compiles clean
        public void Placeholder_AtlasSurface_SrpBatcherCompatible()
        {
            var shader = Shader.Find("Atlas/AtlasSurface");
            Assert.IsNotNull(shader, "shader Atlas/AtlasSurface");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "shader has compile errors");
            // SRP Batcher compatibility: the editor reports "OK" as the issue reason for pass 0
            var m = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(m, "ShaderUtil.GetSRPBatcherCompatibilityCode not found in this Unity version");
            var code = (int)m.Invoke(null, new object[] { shader, 0 });
            var why = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var reason = (string)why.Invoke(null, new object[] { shader, 0, 0 });
            Assert.IsTrue(reason.StartsWith("OK"), "not SRP Batcher compatible (code " + code + "): " + reason);
            var v = typeof(ShaderUtil).GetMethod("GetVariantCount", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(Shader), typeof(bool) }, null);
            if (v != null) Assert.LessOrEqual(Convert.ToInt64(v.Invoke(null, new object[] { shader, false })), 8L, "more than 8 variants");
        }

        [Test] // Master Plan §8.2: +-32 mm eyes, 1118 x 1032 targets; Current set in OnEnable
        public void Placeholder_StereoRig_Eyes()
        {
            var go = new GameObject("rig");
            try
            {
                var rig = go.AddComponent<StereoRig>();
                Assert.AreSame(rig, StereoRig.Current);
                Assert.AreEqual(-0.032f, rig.LeftEye.transform.localPosition.x, 1e-6f);
                Assert.AreEqual(0.032f, rig.RightEye.transform.localPosition.x, 1e-6f);
                Assert.AreEqual(1118, StereoRig.EyeWidth);
                Assert.AreEqual(1032, StereoRig.EyeHeight);
                var planes = GeometryUtility.CalculateFrustumPlanes(rig.LeftEye);
                Assert.AreEqual(6, planes.Length);
                var rt = new RenderTexture(16, 16, 0);
                rig.OverrideEyeTargets(rt, rt, 110f);
                Assert.AreSame(rt, rig.LeftEye.targetTexture);
                Assert.AreEqual(110f, rig.RightEye.fieldOfView, 1e-4f);
                rig.ClearEyeTargetOverride();
                Assert.IsNull(rig.LeftEye.targetTexture);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(rig.LeftEye.gameObject);
                UnityEngine.Object.DestroyImmediate(rig.RightEye.gameObject);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] // Master Plan §10.4.3 / P3-B03: lens off is the ordinary blit (identity)
        public void Placeholder_LensBlit_IdentitySourceUV()
        {
            Assert.IsNull(LensBlit.FinalBlitOverride);
            var uv = new Vector2(0.25f, 0.75f);
            Assert.AreEqual(uv, LensBlit.SourceUV(0, uv));
            Assert.AreEqual(uv, LensBlit.SourceUV(1, uv));
        }

        [Test] // Master Plan §10.2.2 beats a-h
        public void Placeholder_LungBeat_EightBeats()
        {
            Assert.AreEqual(8, Enum.GetValues(typeof(LungBeat)).Length);
            Assert.AreEqual(0, (int)LungBeat.A);
            Assert.AreEqual(7, (int)LungBeat.H);
        }
    }
}
