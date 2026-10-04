using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B11: the Home rebuild placed in curved space (MP §3.5, §4.6, §8.3, §8.4, §9.5.6 P1-B11, §11.1 WorldLint_All items)
    public class CurvedRoomViewTests
    {
        GameObject parent; Material shared;
        readonly List<UnityEngine.Object> toKill = new List<UnityEngine.Object>();

        [SetUp] public void SetUp() { parent = new GameObject("test"); shared = DialWalkTests.TestMaterial(); }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(parent); Object.DestroyImmediate(shared);
            foreach (var o in toKill) if (o != null) Object.DestroyImmediate(o);
            toKill.Clear();
        }

        CurvedRoomView NewView(string room, out IHomeShell home, out RoomModel model)
        {
            model = Room(room);
            home = HomeRebuild.Build(model);
            return new CurvedRoomView(parent.transform, shared, home, model.walls);
        }

        static Iso2 Neutral(double kappa) => new Iso2(double3x3.identity, kappa);

        // Room_Kappa0_MatchesHome: "at kappa = 0 the curved-space room matches Home to 1 cm" (MP §9.5.6 P1-B11, §4.6)
        [TestCase("room_3x3.json")]
        [TestCase("budget_room.json")]
        public void Room_Kappa0_MatchesHome(string name)
        {
            using (var v = NewView(name, out var home, out var model))
            {
                // (a) K = 0: mode 0 and the transforms give the Home positions to 1e-5 m
                Iso2 u0 = TileZeroPlacement.Registered(new double2(0.3, -0.2), 0, 1);
                v.Upload(0, 1, u0, Iso2.Identity, true);
                Assert.IsTrue(v.FloorRenderer.enabled); Assert.IsTrue(v.WallsRenderer.enabled); Assert.IsFalse(v.KerbRenderer.enabled);
                foreach (var r in new[] { v.FloorRenderer, v.WallsRenderer })
                {
                    Assert.AreEqual(0f, r.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                    Assert.AreEqual(Matrix4x4.identity, r.transform.localToWorldMatrix);
                }
                for (int i = 0; i < home.ObjectCount; i++)
                {
                    var rec = home.ObjectRecord(i); var r = v.ObjectRenderer(i);
                    Assert.IsTrue(r.enabled);
                    Assert.AreEqual(0f, r.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                    Assert.Less(Vector3.Distance(r.transform.position, rec.position), 1e-5f);
                    var expect = Quaternion.Euler(0, rec.yaw * Mathf.Rad2Deg, 0);
                    Assert.Less(Quaternion.Angle(r.transform.rotation, expect), 1e-3f);
                }

                // (b) near-zero curvature: L = 10,000 m, the float warp of every vertex is within 0.01 m of the Home position
                const double L = 10000;
                var head = new double2(0.3, -0.2);
                Iso2 u = TileZeroPlacement.Registered(head, -1, L);
                Iso2 f0 = Neutral(-1);
                v.Upload(-1, L, u, f0, true);
                var headWS = new float3((float)head.x, 1.6f, (float)head.y);
                float worst = 0;
                var kerb = v.KerbMeshAsset;
                var kv = kerb.vertices;
                float4x4 kA = ToF4(v.KerbRenderer.sharedMaterial.GetMatrix(AtlasShaderIDs.Anchor));
                foreach (var p in kv)
                {
                    float3 w = AtlasWarpCpu.AtlasWarp(p, kA, 1f, (float)L, headWS, float4x4.identity);
                    worst = Mathf.Max(worst, Mathf.Abs(w.x - p.x), Mathf.Abs(w.z - p.z));
                }
                for (int i = 0; i < home.ObjectCount; i++)
                {
                    var rec = home.ObjectRecord(i);
                    float4x4 a = ToF4(v.ObjectRenderer(i).sharedMaterial.GetMatrix(AtlasShaderIDs.Anchor));
                    Quaternion q = Quaternion.Euler(0, rec.yaw * Mathf.Rad2Deg, 0);
                    foreach (var p in home.ObjectMesh(i).vertices)
                    {
                        Vector3 homePos = rec.position + q * p;
                        float3 w = AtlasWarpCpu.AtlasWarp(p, a, 1f, (float)L, headWS, float4x4.identity);
                        worst = Mathf.Max(worst, Mathf.Abs(w.x - homePos.x), Mathf.Abs(w.z - homePos.z));
                        Assert.AreEqual(homePos.y, w.y, 1e-6f);
                    }
                }
                Assert.Less(worst, 0.01f, "float warp at L = 10,000 m vs Home");

                // (c) the flat double reference is exact to 1e-9 (U^-1 F0 Placement applied in double)
                Iso2 uf = TileZeroPlacement.Registered(head, 0, 1);
                double exact = 0;
                for (int i = 0; i < home.ObjectCount; i++)
                {
                    var rec = home.ObjectRecord(i);
                    Iso2 a = uf.Inverse() * Neutral(0) * TileZeroPlacement.Object(new double2(rec.position.x, rec.position.z), rec.yaw, 0, 1);
                    foreach (var p in home.ObjectMesh(i).vertices)
                    {
                        double3 pt = math.mul(a.M, new double3(p.x, p.z, 1));
                        double2 w = head + pt.xy;
                        double2 h = new double2(rec.position.x, rec.position.z) + new double2(p.x * System.Math.Cos(rec.yaw) + p.z * System.Math.Sin(rec.yaw), -p.x * System.Math.Sin(rec.yaw) + p.z * System.Math.Cos(rec.yaw));
                        exact = math.max(exact, math.length(w - h));
                    }
                }
                Assert.Less(exact, 1e-9, "the flat double reference is exact");
            }
        }

        static float4x4 ToF4(Matrix4x4 m) => new float4x4(m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), m.GetColumn(3));

        // Room_ExpAboutCentre: with U = identity the exp map about the room centre is radially exact (MP §4.6)
        [Test]
        public void Room_ExpAboutCentre()
        {
            using (var v = NewView("room_4x5_table.json", out var home, out var model))
            {
                double L = 1.4;
                Iso2 id = Neutral(-1);
                v.Upload(-1, L, id, id, true);
                double3 headWS = new double3(0, 1.6, 0);
                double worst = 0;
                foreach (var p in v.KerbMeshAsset.vertices)
                {
                    var a = ToIso(v.KerbRenderer.sharedMaterial.GetMatrix(AtlasShaderIDs.Anchor));
                    double3 w = AtlasWarpReference.Warp(new double3(p.x, p.y, p.z), a, -1, L, headWS);
                    worst = math.max(worst, math.max(math.abs(w.x - p.x), math.abs(w.z - p.z)));
                }
                Assert.Less(worst, 1e-6, "kerb vertices land on their Home x,z");
                for (int i = 0; i < home.ObjectCount; i++)
                {
                    var rec = home.ObjectRecord(i);
                    Iso2 a = TileZeroPlacement.Object(new double2(rec.position.x, rec.position.z), rec.yaw, -1, L);
                    double3 w = AtlasWarpReference.Warp(double3.zero, a, -1, L, headWS);
                    Assert.AreEqual(rec.position.x, w.x, 1e-6); Assert.AreEqual(rec.position.z, w.z, 1e-6);
                }
            }
        }

        static Iso2 ToIso(Matrix4x4 m)
        {
            // WarpAnchor.ToMatrix wrote (float)M[c][r] at [r, c]
            var d = new double3x3(m[0, 0], m[1, 0], m[2, 0], m[0, 1], m[1, 1], m[2, 1], m[0, 2], m[1, 2], m[2, 2]);
            return new Iso2(d, -1);
        }

        // Furniture_Rigid: object anchors are rigid placements about the base (MP §4.6 "per-object anchors")
        [Test]
        public void Furniture_Rigid()
        {
            var model = Room("room_4x5_table.json");
            var home = HomeRebuild.Build(model);
            double L = 1.4;
            Assert.GreaterOrEqual(home.ObjectCount, 1);
            for (int i = 0; i < home.ObjectCount; i++)
            {
                var rec = home.ObjectRecord(i);
                Iso2 a = TileZeroPlacement.Object(new double2(rec.position.x, rec.position.z), rec.yaw, -1, L);
                double3 baseP = a.M.c2;
                double3 expect = Geometry2.Exp(new double2(rec.position.x, rec.position.z) / L, -1);
                Assert.Less(Geometry2.Distance(baseP, expect, -1) * L, 1e-3, "base centre at Exp(pos / L)");
                double worst = 0;
                foreach (var p in home.ObjectMesh(i).vertices)
                {
                    double3 w = math.mul(a.M, Geometry2.Exp(new double2(p.x, p.z) / L, -1));
                    double d = Geometry2.Distance(w, baseP, -1) * L;
                    worst = math.max(worst, math.abs(d - math.length(new double2(p.x, p.z))));
                }
                Assert.Less(worst, 1e-3, "local distances from the base centre are kept");
            }
        }

        static Wall DoorWall()
        {
            return new Wall
            {
                a = new FloorPoint { x = -1.5f, z = -1.5f }, b = new FloorPoint { x = 1.5f, z = -1.5f }, height = 2.4f,
                openings = new[]
                {
                    new Opening { kind = "door", from = 1.0f, to = 1.9f, bottom = 0f, top = 2.0f },
                    new Opening { kind = "window", from = 2.2f, to = 2.8f, bottom = 1.0f, top = 2.0f },
                },
            };
        }

        // Walls_SinkToKerbs (MP §3.5, §4.6, §8.4): kerb 0.9 m, no edge over 0.25 m, door gap stays open
        [Test]
        public void Walls_SinkToKerbs()
        {
            var w0 = DoorWall();
            var rest = new[]
            {
                new Wall { a = new FloorPoint { x = 1.5f, z = -1.5f }, b = new FloorPoint { x = 1.5f, z = 1.5f }, height = 2.4f },
                new Wall { a = new FloorPoint { x = 1.5f, z = 1.5f }, b = new FloorPoint { x = -1.5f, z = 1.5f }, height = 2.4f },
                new Wall { a = new FloorPoint { x = -1.5f, z = 1.5f }, b = new FloorPoint { x = -1.5f, z = -1.5f }, height = 2.4f },
            };
            var walls = new[] { w0, rest[0], rest[1], rest[2] };
            var mesh = new Mesh(); toKill.Add(mesh);
            int tris = KerbMesh.Build(walls, 0.9f, mesh);
            Assert.Greater(tris, 0);
            var v = mesh.vertices; var t = mesh.triangles;
            float maxY = 0; foreach (var p in v) maxY = Mathf.Max(maxY, p.y);
            Assert.AreEqual(0.9f, maxY, 1e-5f);
            float longest = 0;
            for (int i = 0; i < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                    longest = Mathf.Max(longest, Vector3.Distance(v[t[i + e]], v[t[i + (e + 1) % 3]]));
            Assert.LessOrEqual(longest, 0.25f, "no edge over 0.25 m (MP §8.4)");
            foreach (var p in v)   // door gap along wall 0 (z = -1.5, x from -0.5 to 0.4): no vertex strictly inside
                if (Mathf.Abs(p.z + 1.5f) < 1e-4f) Assert.IsFalse(p.x > -0.5f + 1e-3f && p.x < 0.4f - 1e-3f, "door gap at x = " + p.x);
            bool windowWallPresent = false;   // the window starts at 1.0 m (above the kerb): that stretch stays closed
            foreach (var p in v) if (Mathf.Abs(p.z + 1.5f) < 1e-4f && p.x > 0.75f && p.x < 1.25f) windowWallPresent = true;
            Assert.IsTrue(windowWallPresent);
            var n = mesh.normals; Assert.AreEqual(v.Length, n.Length);
            foreach (var nn in n) Assert.AreEqual(0f, nn.y, 1e-6f);   // horizontal, two-sided

            // the view: K != 0 walls and floor off, kerb on; K = 0 the reverse
            var model = Room("room_3x3.json");
            var home = HomeRebuild.Build(model);
            using (var view = new CurvedRoomView(parent.transform, shared, home, model.walls))
            {
                Iso2 id = Neutral(-1);
                view.Upload(-1, 1.884481, id, id, true);
                Assert.IsFalse(view.FloorRenderer.enabled); Assert.IsFalse(view.WallsRenderer.enabled); Assert.IsTrue(view.KerbRenderer.enabled);
                Assert.AreEqual(1f, view.KerbRenderer.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                view.Upload(0, 1, Iso2.Identity, Iso2.Identity, true);
                Assert.IsTrue(view.FloorRenderer.enabled); Assert.IsTrue(view.WallsRenderer.enabled); Assert.IsFalse(view.KerbRenderer.enabled);
                view.Upload(-1, 1.884481, id, id, false);
                Assert.IsFalse(view.KerbRenderer.enabled);
                for (int i = 0; i < view.ObjectCount; i++) Assert.IsFalse(view.ObjectRenderer(i).enabled);
            }
        }

        // Room_EveryObjectDrawn (MP §11.1 WorldLint_All): one renderer per object with a mesh; WarpBounds: 1000 m local bounds
        [TestCase("budget_room.json")]
        [TestCase("room_4x5_table.json")]
        public void Room_EveryObjectDrawn_And_WarpBounds(string name)
        {
            using (var view = NewView(name, out var home, out var model))
            {
                Assert.AreEqual(model.objects.Length, view.ObjectCount);
                Iso2 id = Neutral(-1);
                foreach (double kappa in new[] { -1.0, 0.0 })
                {
                    view.Upload(kappa, 1.4, kappa == 0 ? Iso2.Identity : id, kappa == 0 ? Iso2.Identity : id, true);
                    for (int i = 0; i < home.ObjectCount; i++)
                    {
                        Assert.IsNotNull(home.ObjectMesh(i));
                        Assert.IsTrue(view.ObjectRenderer(i).enabled, "object " + i + " kappa " + kappa);
                        Assert.IsNotNull(view.ObjectRenderer(i).GetComponent<MeshFilter>().sharedMesh);
                    }
                }
                var all = new List<MeshRenderer> { view.FloorRenderer, view.WallsRenderer, view.KerbRenderer };
                for (int i = 0; i < view.ObjectCount; i++) all.Add(view.ObjectRenderer(i));
                foreach (var r in all)
                {
                    Assert.AreEqual(1000f, r.localBounds.size.x); Assert.AreEqual(1000f, r.localBounds.size.y); Assert.AreEqual(1000f, r.localBounds.size.z);
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode);
                }
                var mats = new HashSet<Material>();
                foreach (var r in all) Assert.IsTrue(mats.Add(r.sharedMaterial), "one material instance each");
            }
        }

        [Test] // MP §3.5, §4.6: near-head fade radius 0.3 m
        public void NearFade_Threshold()
        {
            Assert.IsTrue(CurvedRoomConstants.NearFadeEngaged(0.29)); Assert.IsFalse(CurvedRoomConstants.NearFadeEngaged(0.3));
            Assert.AreEqual(0.3, CurvedRoomConstants.NearFadeRadiusM);
        }
    }
}
