using NUnit.Framework;
using Atlas.Rendering.Room;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Room
{
    /// <summary>The furniture level: the choice of copy, the light target, the ladder's column and the budget at both levels.</summary>
    public class FurnitureLevelTests
    {
        // A room built in code: 24 generated objects of 5,000 triangles each, 120,000 in all.
        static RoomModel RoomOf24()
        {
            RoomModel room = RoomTestUtil.Rectangle(6f, 5f);
            room.Objects = new RoomObject[24];
            for (int k = 0; k < 24; k++)
            {
                room.Objects[k] = new RoomObject
                {
                    Id = k + 1, Category = ObjectCategory.Storage, Size = new float3(0.5f, 0.5f, 0.5f),
                    Base = new float3(-2.5f + 0.25f * k, 0f, 0f), Mesh = MeshState.Ready, TriTarget = 5000,
                };
            }
            return room;
        }

        [Test]
        public void DrawsLight_OnlyAtLight_AndOnlyWithALightCopy()
        {
            Assert.That(RoomObjectMeshes.DrawsLight(FurnitureLevel.Light, true), Is.True);
            Assert.That(RoomObjectMeshes.DrawsLight(FurnitureLevel.Light, false), Is.False, "light missing falls back to full");
            Assert.That(RoomObjectMeshes.DrawsLight(FurnitureLevel.Full, true), Is.False);
            Assert.That(RoomObjectMeshes.DrawsLight(FurnitureLevel.Full, false), Is.False);
        }

        [Test]
        public void LightTarget_FollowsTheRule()
        {
            Assert.That(RoomObjectMeshes.LightTarget(5), Is.EqualTo(5000));
            Assert.That(RoomObjectMeshes.LightTarget(12), Is.EqualTo(5000));
            Assert.That(RoomObjectMeshes.LightTarget(20), Is.EqualTo(3000));
            Assert.That(RoomObjectMeshes.LightTarget(24), Is.EqualTo(2500));
            Assert.That(RoomObjectMeshes.LightTarget(32), Is.EqualTo(1875));
            Assert.That(RoomObjectMeshes.LightTarget(100), Is.EqualTo(1500));
            Assert.That(RoomObjectMeshes.LightTarget(0), Is.EqualTo(5000));
        }

        [Test]
        public void LightTarget_CountsOnlyObjectsWithATarget()
        {
            RoomModel room = RoomOf24();
            for (int k = 0; k < 4; k++) room.Objects[k].TriTarget = 0;      // 20 objects with meshes
            Assert.That(RoomObjectMeshes.LightTarget(room.Objects), Is.EqualTo(3000));
        }

        [Test]
        public void Ladder_AllowsFullOnNominalAndHigh_AndLightOnTheStepsBetween()
        {
            Assert.That(QualityFurniture.Cap(QualityTier.Nominal), Is.EqualTo(FurnitureLevel.Full));
            Assert.That(QualityFurniture.Cap(QualityTier.Fair), Is.EqualTo(FurnitureLevel.Light));
            Assert.That(QualityFurniture.Cap(QualityTier.Serious), Is.EqualTo(FurnitureLevel.Light));
            Assert.That(QualityFurniture.Cap(QualityTier.Critical), Is.EqualTo(FurnitureLevel.Light));
            Assert.That(QualityFurniture.Cap(QualityTier.High), Is.EqualTo(FurnitureLevel.Full));
        }

        [Test]
        public void Ladder_FirstStepBelowNominal_ForcesLightInEverySpace()
        {
            foreach (FurnitureLevel own in new[] { FurnitureLevel.Full, FurnitureLevel.Light })
            {
                Assert.That(QualityFurniture.Limit(own, QualityTier.Nominal), Is.EqualTo(own));
                Assert.That(QualityFurniture.Limit(own, QualityTier.High), Is.EqualTo(own));
                Assert.That(QualityFurniture.Limit(own, QualityTier.Fair), Is.EqualTo(FurnitureLevel.Light));
                Assert.That(QualityFurniture.Limit(own, QualityTier.Serious), Is.EqualTo(FurnitureLevel.Light));
                Assert.That(QualityFurniture.Limit(own, QualityTier.Critical), Is.EqualTo(FurnitureLevel.Light));
            }
        }

        [Test]
        public void Applier_PublishesTheCapOfItsTier()
        {
            var applier = new QualityApplier(null);
            Assert.That(applier.FurnitureCap, Is.EqualTo(FurnitureLevel.Full));
            applier.Apply(QualityTier.Fair);
            Assert.That(applier.FurnitureCap, Is.EqualTo(FurnitureLevel.Light));
            applier.Apply(QualityTier.Nominal);
            Assert.That(applier.FurnitureCap, Is.EqualTo(FurnitureLevel.Full));
            FramePacing.TargetFrameRate = FramePacing.DefaultFrameRate;
        }

        [Test]
        public void Budget_24ObjectsOf5000_AtFull()
        {
            RoomModel room = RoomOf24();
            RoomDrawCost cost = RoomBudget.Estimate(room);
            Assert.That(cost.ObjectTriangles, Is.EqualTo(120000));
            Assert.That(cost.DrawCallsPerEye, Is.EqualTo(26));              // floor, walls, 24 objects
            Assert.That(cost.DrawCallsPerFrame, Is.EqualTo(52));
            Assert.That(cost.TrianglesPerFrame, Is.EqualTo(2 * (cost.ShellTriangles + 120000)));
            Assert.That(cost.TrianglesPerFrame, Is.EqualTo(246000).Within(5000), "about 246,000: the shell is a few thousand");
            Assert.That(RoomBudget.FitsHome(cost), Is.True);
            Assert.That(RoomBudget.Estimate(room, WallMode.Full, true, true, FurnitureLevel.Full).TrianglesPerFrame,
                        Is.EqualTo(cost.TrianglesPerFrame), "Full is the default level");
        }

        [Test]
        public void Budget_24ObjectsOf5000_AtLight()
        {
            RoomModel room = RoomOf24();
            RoomDrawCost full = RoomBudget.Estimate(room);
            RoomDrawCost light = RoomBudget.Estimate(room, WallMode.Full, true, true, FurnitureLevel.Light);
            Assert.That(light.ObjectTriangles, Is.EqualTo(24 * 2500), "the light target of 24 objects is 2,500");
            Assert.That(light.ShellTriangles, Is.EqualTo(full.ShellTriangles));
            Assert.That(light.DrawCallsPerFrame, Is.EqualTo(full.DrawCallsPerFrame), "the same number of draw calls");
        }

        [Test]
        public void Budget_StandInsAndBuiltIns_AreTheSameAtBothLevels()
        {
            RoomModel room = RoomOf24();
            for (int k = 0; k < 12; k++) room.Objects[k].Mesh = MeshState.StandIn;
            for (int k = 12; k < 24; k++) room.Objects[k].Mesh = MeshState.BuiltIn;
            Assert.That(RoomBudget.Estimate(room, WallMode.Full, true, true, FurnitureLevel.Light).ObjectTriangles,
                        Is.EqualTo(RoomBudget.Estimate(room).ObjectTriangles));
        }

        [Test]
        public void HomeRow_IsEightyDrawCallsAnd250000Triangles()
        {
            Assert.That(RoomBudget.HomeDrawCallsPerFrame, Is.EqualTo(80));
            Assert.That(RoomBudget.HomeTrianglesPerFrame, Is.EqualTo(250000));
        }
    }
}
