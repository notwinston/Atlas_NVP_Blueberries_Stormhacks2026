using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B11: the room content placed in curved space (MP §3.5, §4.6, §8.3, §8.4, §9.5.6 P1-B11). The mathematics runs on
    // the real RoomModel and on the mesh data the room content draws (RoomShellBuilder, RoomObjectMeshes), with the anchors
    // CurvedRoomView uploads: U^-1 F0 for the shell and U^-1 F0 Object(base, yaw) per object. The engine half, on the real
    // RoomContent, is at the end of the file.
    public class CurvedRoomViewTests
    {
        static Iso2 Neutral(double kappa) => new Iso2(double3x3.identity, kappa);

        // The layout WarpAnchor.ToMatrix uploads (kernel.md §6.2): the isometry's 3x3 in the upper left, cast to float last.
        static float4x4 Upload(in Iso2 a) =>
            new float4x4(new float4((float3)a.M.c0, 0f), new float4((float3)a.M.c1, 0f), new float4((float3)a.M.c2, 0f), new float4(0f, 0f, 0f, 1f));

        static AtlasMeshBuilder Kerbs(RoomModel room)
        {
            var data = new AtlasMeshBuilder();
            RoomShellBuilder.Kerbs(room, CurvedRoomConstants.KerbHeightM, data);
            return data;
        }

        static AtlasMeshBuilder ObjectMesh(RoomObject o)
        {
            var data = new AtlasMeshBuilder();
            RoomObjectMeshes.Initial(o, data);
            return data;
        }

        // Room_Kappa0_MatchesHome: "at kappa = 0 the curved-space room matches Home to 1 cm" (MP §9.5.6 P1-B11, §4.6)
        [TestCase(RoomFixtures.Sample3x3)]
        [TestCase(RoomFixtures.Sample4x5Table)]
        [TestCase(RoomFixtures.BudgetRoom)]
        public void Room_Kappa0_MatchesHome(string name)
        {
            RoomModel room = Room(name);
            // near-zero curvature: L = 10,000 m, the float warp of every vertex is within 0.01 m of the Home position
            const double L = 10000;
            var head = new double2(0.3, -0.2);
            Iso2 a0 = TileZeroPlacement.Registered(head, -1, L).Inverse() * Neutral(-1);
            var headWS = new float3((float)head.x, 1.6f, (float)head.y);
            float worst = 0;
            AtlasMeshBuilder kerb = Kerbs(room);
            Assert.Greater(kerb.VertexCount, 0);
            float4x4 kA = Upload(a0);
            for (int i = 0; i < kerb.VertexCount; i++)
            {
                float3 p = kerb.Vertices[i].Position;
                float3 w = AtlasWarpCpu.AtlasWarp(p, kA, AtlasWarpCpu.ModeHyperbolic, (float)L, headWS, float4x4.identity);
                worst = math.max(worst, math.max(math.abs(w.x - p.x), math.abs(w.z - p.z)));
            }
            foreach (RoomObject o in room.Objects)
            {
                float4x4 a = Upload(a0 * TileZeroPlacement.Object(new double2(o.Base.x, o.Base.z), o.Yaw, -1, L));
                AtlasMeshBuilder mesh = ObjectMesh(o);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    float3 p = mesh.Vertices[i].Position;
                    float3 home = RoomObjectMeshes.ToRoom(o, p);
                    float3 w = AtlasWarpCpu.AtlasWarp(p, a, AtlasWarpCpu.ModeHyperbolic, (float)L, headWS, float4x4.identity);
                    worst = math.max(worst, math.max(math.abs(w.x - home.x), math.abs(w.z - home.z)));
                    Assert.AreEqual(p.y, w.y, 1e-6f, "the warp leaves the height alone");
                }
            }
            Assert.Less(worst, 0.01f, "float warp at L = 10,000 m vs Home");

            // the flat double reference is exact to 1e-9 (U^-1 F0 Placement applied in double)
            Iso2 uf = TileZeroPlacement.Registered(head, 0, 1);
            double exact = 0;
            foreach (RoomObject o in room.Objects)
            {
                Iso2 a = uf.Inverse() * Neutral(0) * TileZeroPlacement.Object(new double2(o.Base.x, o.Base.z), o.Yaw, 0, 1);
                AtlasMeshBuilder mesh = ObjectMesh(o);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    float3 p = mesh.Vertices[i].Position;
                    double3 pt = math.mul(a.M, new double3(p.x, p.z, 1));
                    double2 w = head + pt.xy;
                    double2 h = new double2(o.Base.x, o.Base.z) + new double2(p.x * Math.Cos(o.Yaw) + p.z * Math.Sin(o.Yaw), -p.x * Math.Sin(o.Yaw) + p.z * Math.Cos(o.Yaw));
                    exact = math.max(exact, math.length(w - h));
                }
            }
            Assert.Less(exact, 1e-9, "the flat double reference is exact");
        }

        // Room_ExpAboutCentre: with U = identity the exp map about the room centre is radially exact (MP §4.6)
        [Test]
        public void Room_ExpAboutCentre()
        {
            RoomModel room = Room(RoomFixtures.Sample4x5Table);
            double L = room.Curvature.ForDial(5);
            Iso2 id = Neutral(-1);
            double3 headWS = new double3(0, 1.6, 0);
            double worst = 0;
            AtlasMeshBuilder kerb = Kerbs(room);
            for (int i = 0; i < kerb.VertexCount; i++)
            {
                float3 p = kerb.Vertices[i].Position;
                double3 w = AtlasWarpReference.Warp(new double3(p.x, p.y, p.z), id, -1, L, headWS);
                worst = math.max(worst, math.max(math.abs(w.x - p.x), math.abs(w.z - p.z)));
            }
            Assert.Less(worst, 1e-6, "kerb vertices land on their Home x,z");
            Assert.GreaterOrEqual(room.Objects.Length, 1);
            foreach (RoomObject o in room.Objects)
            {
                Iso2 a = TileZeroPlacement.Object(new double2(o.Base.x, o.Base.z), o.Yaw, -1, L);
                double3 w = AtlasWarpReference.Warp(double3.zero, a, -1, L, headWS);
                Assert.AreEqual(o.Base.x, w.x, 1e-6); Assert.AreEqual(o.Base.z, w.z, 1e-6);
            }
        }

        // Furniture_Rigid: object anchors are rigid placements about the base (MP §4.6 "per-object anchors")
        [TestCase(RoomFixtures.Sample4x5Table)]
        [TestCase(RoomFixtures.BudgetRoom)]
        public void Furniture_Rigid(string name)
        {
            RoomModel room = Room(name);
            double L = room.Curvature.ForDial(5);
            Assert.GreaterOrEqual(room.Objects.Length, 1);
            foreach (RoomObject o in room.Objects)
            {
                Iso2 a = TileZeroPlacement.Object(new double2(o.Base.x, o.Base.z), o.Yaw, -1, L);
                double3 baseP = a.M.c2;
                double3 expect = Geometry2.Exp(new double2(o.Base.x, o.Base.z) / L, -1);
                Assert.Less(Geometry2.Distance(baseP, expect, -1) * L, 1e-3, "base centre at Exp(pos / L)");
                double worst = 0;
                AtlasMeshBuilder mesh = ObjectMesh(o);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    float3 p = mesh.Vertices[i].Position;
                    double3 w = math.mul(a.M, Geometry2.Exp(new double2(p.x, p.z) / L, -1));
                    double d = Geometry2.Distance(w, baseP, -1) * L;
                    worst = math.max(worst, math.abs(d - math.length(new double2(p.x, p.z))));
                }
                Assert.Less(worst, 1e-3, "local distances from the base centre are kept");
            }
        }

        // Walls_SinkToKerbs (MP §3.5, §4.6, §8.4): the kerb the room content draws in curved space is 0.9 m high and has no
        // edge over 0.25 m, so the warp bends it correctly. The openings are the room content's own tests (RoomShellTests).
        [TestCase(RoomFixtures.Sample3x3)]
        [TestCase(RoomFixtures.SampleL3x4)]
        [TestCase(RoomFixtures.Sample4x5Table)]
        [TestCase(RoomFixtures.BudgetRoom)]
        public void Walls_SinkToKerbs(string name)
        {
            Assert.AreEqual(0.9f, CurvedRoomConstants.KerbHeightM);
            Assert.LessOrEqual(CurvedRoomConstants.KerbHeightM, RoomShellBuilder.MaxKerbHeight);
            AtlasMeshBuilder kerb = Kerbs(Room(name));
            Assert.Greater(kerb.TriangleCount, 0);
            Assert.AreEqual(0.9f, kerb.BoundsMax.y, 1e-5f);
            // The warp leaves the height alone, so what counts is an edge's length along the floor (room contract §9.3).
            Assert.LessOrEqual(RoomShellBuilder.LongestFloorPlaneEdge(kerb), CurvedRoomConstants.EdgeRuleM * 1.0002f, "no edge over 0.25 m along the floor (MP §8.4)");
        }

        // The warp reads the mesh in metres and ignores Transforms: every room object mesh obeys the edge rule too. A stand-in
        // has no edge over 0.25 m; a placeholder box is gridded at 0.25 m, so its sides obey it and a cell's diagonal is 0.354 m.
        [TestCase(RoomFixtures.Sample4x5Table)]
        [TestCase(RoomFixtures.BudgetRoom)]
        public void Furniture_ObeysTheEdgeRule(string name)
        {
            foreach (RoomObject o in Room(name).Objects)
            {
                AtlasMeshBuilder mesh = ObjectMesh(o);
                float limit = CurvedRoomConstants.EdgeRuleM * 1.0002f;
                if (o.Mesh == MeshState.StandIn) Assert.LessOrEqual(mesh.LongestEdge(), limit, "object " + o.Id);
                else Assert.LessOrEqual(mesh.LongestSide(), limit, "object " + o.Id);
            }
        }

        [Test] // MP §3.5, §4.6: near-head fade radius 0.3 m
        public void NearFade_Threshold()
        {
            Assert.IsTrue(CurvedRoomConstants.NearFadeEngaged(0.29)); Assert.IsFalse(CurvedRoomConstants.NearFadeEngaged(0.3));
            Assert.AreEqual(0.3, CurvedRoomConstants.NearFadeRadiusM);
        }

        [Test]
        public void Registration_PutsCurvedWalkInTheRegistry()
        {
            CurvedWalkRegistration.Register();
            Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.CurvedWalk));
            // Safe to run twice: no domain reload in the Editor.
            CurvedWalkRegistration.Register();
            Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.CurvedWalk));
            var registry = new ParamRegistry();
            SpaceRegistry.DeclareAllParams(registry);
            Assert.IsNotNull(registry.Find(CurvedWalkCore.ParamQ), "the dial is in the manifest at boot");
            Assert.IsNotNull(registry.Find(CurvedWalkCore.ParamStrong));
        }

        // The curvature of the ramp's first frame: linear in K over 4 s from 0 to -1 / L^2 (MP §10.1.4), one frame in.
        static double FirstRampFrame(List<HeadPose> log, double L) => -(log[1].Time - log[0].Time) / 4.0 / (L * L);

        // The room is exactly Home on the frame the walker is registered; standing, the ramp starts on the next one.
        [Test]
        public void Ramp_BeginsTheFrameAfterRegistration()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample4x5Table)))
            {
                var log = Load("walk_still_60s.poselog");
                h.Step(log[0]);
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow);
                Assert.Less(h.Kernel.CurvatureTarget, 0.0);
                Assert.AreEqual(Iso2.Identity.M, h.S.TileZeroInCurrent.M);
                h.Step(log[1]);
                Assert.AreEqual(FirstRampFrame(log, h.S.LengthScale(5)), h.Kernel.CurvatureNow, 1e-12);
            }
        }

#if !ATLAS_HEADLESS
        // ---- the engine half: the real RoomContent, driven by CurvedRoomView and by the module ----

        UnityEngine.GameObject root;
        MaterialPool pool;
        RoomContent content;

        void Build(string name)
        {
            root = new UnityEngine.GameObject("room");
            pool = MaterialPool.CreateDefault();
            content = new RoomContent(root.transform, pool) { Instant = true };
            content.Build(Room(name));
        }

        [TearDown]
        public void TearDown()
        {
            AtlasServices.Clear();
            if (content != null) content.Dispose();
            content = null;
            if (pool != null) pool.Dispose();
            pool = null;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            root = null;
        }

        // K = 0: exactly the room (warp off, floor, full walls); K != 0: floor off, 0.9 m kerbs, the shell and every object
        // warped on its own material with 1000 m bounds; the reverse on the way back.
        [Test, Category("Unity")]
        public void View_DrivesTheRealRoomContent()
        {
            Build(RoomFixtures.Sample4x5Table);
            using (var view = new CurvedRoomView(content))
            {
                Assert.AreEqual(content.Room.Objects.Length, view.ObjectCount);
                view.Upload(0, 1, TileZeroPlacement.Registered(new double2(0.3, -0.2), 0, 1), Iso2.Identity, true);
                Assert.IsTrue(content.FloorVisible); Assert.AreEqual(WallMode.Full, content.Walls);
                Assert.AreEqual(0f, content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode));

                double L = content.Room.Curvature.ForDial(5);
                Iso2 id = Neutral(-1);
                view.Upload(-1, L, id, id, true);
                Assert.IsFalse(content.FloorVisible); Assert.AreEqual(WallMode.Kerbs, content.Walls);
                Assert.AreEqual(CurvedRoomConstants.KerbHeightM, content.KerbHeight);
                Assert.AreEqual(1f, content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                Assert.AreEqual(AtlasWarpBounds.Size, view.WallRenderer.localBounds.size.x);
                var mats = new HashSet<UnityEngine.Material> { content.ShellMaterial };
                for (int i = 0; i < view.ObjectCount; i++)
                {
                    IRoomObjectView o = content.GetObject(i);
                    Assert.IsTrue(o.Renderer.enabled);
                    Assert.IsNotNull(o.Renderer.GetComponent<UnityEngine.MeshFilter>().sharedMesh, "every object is drawn");
                    Assert.AreEqual(1f, o.Material.GetFloat(AtlasShaderIDs.WarpMode));
                    Assert.AreEqual(AtlasWarpBounds.Size, o.Renderer.localBounds.size.x);
                    Assert.AreEqual(UnityEngine.Vector3.one, o.Renderer.transform.localScale, "the warp ignores Transforms: no fitting scale");
                    Assert.IsTrue(mats.Add(o.Material), "one material instance each");
                    Iso2 a = TileZeroPlacement.Object(new double2(o.Data.Base.x, o.Data.Base.z), o.Data.Yaw, -1, L);
                    Assert.AreEqual(WarpAnchor.ToMatrix(a), o.Material.GetMatrix(AtlasShaderIDs.Anchor));
                }

                view.Upload(-1, L, id, id, false);
                Assert.AreEqual(WallMode.Hidden, content.Walls); Assert.IsFalse(content.ObjectsVisible);

                view.Upload(0, 1, Iso2.Identity, Iso2.Identity, true);
                Assert.IsTrue(content.FloorVisible); Assert.AreEqual(WallMode.Full, content.Walls); Assert.IsTrue(content.ObjectsVisible);
                Assert.AreEqual(0f, content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                for (int i = 0; i < view.ObjectCount; i++) Assert.AreEqual(0f, content.GetObject(i).Material.GetFloat(AtlasShaderIDs.WarpMode));
            }
        }

        // The module as SpaceLab drives it, on the app's services: created by the factory, configured, entered on the shared
        // kernel, ticked and uploaded; the room is the room content's (no copy), the world is drawn once curved, and Exit
        // gives every material back.
        [Test, Category("Unity")]
        public void Module_RunsOnTheAppsServices_AndGivesEverythingBack()
        {
            Build(RoomFixtures.Sample4x5Table);
            var kernel = new HolonomyWalkerKernel();
            RoomModel room = content.Room;
            kernel.Configure(KernelConfig.Flat(room.LapSide));
            AtlasServices.Register(pool);
            AtlasServices.Register<IRoomContent>(content);
            AtlasServices.Register<IDialKernel>(kernel);
            var registry = new ParamRegistry();
            CurvedWalkSpace.DeclareParams(registry);

            var module = (CurvedWalkSpace)CurvedWalkSpace.Create();
            try
            {
                Assert.AreEqual(SpaceIds.SceneName(SpaceId.CurvedWalk), module.gameObject.name);
                Assert.AreEqual(SpaceId.CurvedWalk, module.Id);
                Assert.IsFalse(module.Registered);
                int rentedBefore = pool.Rented;
                module.Configure(room, registry);
                Assert.AreEqual(16, module.ContentHash.Length);
                Assert.AreEqual(CurvedWalkCore.WorldSeed, module.WorldSeed);
                module.Enter(kernel.Pose);
                Assert.AreEqual(5, kernel.Q);

                var log = Load("walk_still_60s.poselog");
                for (int i = 0; i < 330; i++)
                {
                    FrameContext f = StepKernel(kernel, i, log[i]);
                    module.Tick(in f);
                    module.Upload(in f);
                    if (i == 0)
                    {
                        // Frame 0 registers the walker under the head at K = 0 and only asks for the ramp.
                        Assert.AreEqual(0.0, kernel.CurvatureNow, "the room is Home until the ramp begins");
                        Assert.Less(kernel.CurvatureTarget, 0.0, "the ramp is asked for");
                        Assert.AreEqual(WallMode.Full, content.Walls, "the room is Home until the ramp begins");
                        Assert.IsTrue(content.FloorVisible);
                        Assert.AreEqual(0f, content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                    }
                    if (i == 1)
                    {
                        // The walker stands, so frame 1 is the ramp's first: K = -(dt / 4 s) / L^2 (MP §10.1.4).
                        Assert.AreEqual(FirstRampFrame(log, module.Core.LengthScale(5)), kernel.CurvatureNow, 1e-12);
                        Assert.AreEqual(WallMode.Kerbs, content.Walls, "curved from the ramp's first frame");
                    }
                }
                Assert.Less(kernel.CurvatureNow, 0.0);
                Assert.IsTrue(module.Tiles.gameObject.activeSelf);
                Assert.IsFalse(module.Tiles.DrawShells, "the generated world draws the tiles");
                Assert.Greater(module.Content.TilesDrawn, 0);
                Assert.AreEqual(module.Tiles.Selection.DrawnCount, module.Content.TilesDrawn);
                Assert.AreEqual(WallMode.Kerbs, content.Walls);
                Assert.IsFalse(content.FloorVisible);
                Assert.AreEqual(1f, content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                Assert.AreEqual(2 + content.ObjectCount, root.transform.GetComponentsInChildren<UnityEngine.MeshRenderer>(true).Length,
                    "the room content holds the floor, the walls and one renderer per object: the space made no copy of the room");

                var st = StateMessage.Identity;
                module.WriteState(ref st);
                Assert.AreEqual(5f, st.GetParam(CurvedWalkCore.StateQ));

                module.Exit();
                Assert.AreEqual(rentedBefore, pool.Rented, "every material is back in the pool");
                Assert.AreEqual(0f, content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode), "the warp is off the room");
                Assert.AreEqual(5, kernel.Q, "the kernel is SpaceLab's to restore");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(module.gameObject);
            }
        }
#endif
    }
}
