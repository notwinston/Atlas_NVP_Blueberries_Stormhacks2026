using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using WorldTier = Atlas.World.QualityTier;

namespace Atlas.App.Tests
{
    /// <summary>The furniture level of each space, the ladder's hold on it and render.furniture (mesh design section 5.4).</summary>
    public class FurnitureLevelPolicyTests
    {
        // Only the level and the presentation reset matter here.
        sealed class LevelContent : IRoomContent
        {
            RoomModel m_Room;
            public int Sets;
            FurnitureLevel m_Level;
            public FurnitureLevel FurnitureLevel { get { return m_Level; } set { Log.Add("level:" + value); m_Level = value; Sets++; } }
            public readonly System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();
            public void ResetPresentation() { Log.Add("reset"); m_Level = FurnitureLevel.Full; }
            public RoomModel Room { get { return m_Room; } }
            public bool IsBuilt { get { return m_Room != null; } }
            public void Build(RoomModel room) { m_Room = room; ResetPresentation(); if (Built != null) Built(room); }
            public void Clear() { m_Room = null; }
            public UnityEngine.Transform Root { get { return null; } }
            public WallMode Walls { get { return WallMode.Full; } }
            public void SetWallMode(WallMode mode, float seconds = 0.5f) { }
            public float KerbHeight { get; set; }
            public bool FloorVisible { get; set; }
            public bool ObjectsVisible { get; set; }
            public UnityEngine.Renderer FloorRenderer { get { return null; } }
            public UnityEngine.Renderer WallRenderer { get { return null; } }
            public UnityEngine.Material ShellMaterial { get { return null; } }
            public int ShellTriangleCount { get { return 0; } }
            public int ObjectCount { get { return 0; } }
            public IRoomObjectView GetObject(int index) { return null; }
            public bool TryGetObject(int objectId, out IRoomObjectView view) { view = null; return false; }
            public bool SetObjectState(int objectId, MeshState state) { return false; }
            public bool SetGeneratedMesh(int objectId, UnityEngine.Mesh mesh, string assetHash, float fadeSeconds = 0.3f) { return false; }
            public bool SetGeneratedLightMesh(int objectId, UnityEngine.Mesh mesh, string assetHashLight) { return false; }
            public bool IsMeshDrawn(UnityEngine.Mesh mesh) { return false; }
            public bool SetBuiltInMesh(int objectId, UnityEngine.Mesh sharedMesh, UnityEngine.Vector3 scale, float fadeSeconds = 0.3f) { return false; }
            public event Action<RoomModel> Built;
            public event Action<int> ObjectChanged { add { } remove { } }
        }

        sealed class Rig
        {
            public readonly LevelContent Content = new LevelContent();
            public readonly QualityLadder Ladder = new QualityLadder(new QualityApplier(null));
            public readonly FurnitureLevelPolicy Policy;

            public Rig()
            {
                Policy = new FurnitureLevelPolicy(Content, Ladder);
                Content.Build(new RoomModel { Id = "r" });
            }

            // What SpaceStage does between two spaces.
            public void Enter(SpaceId id)
            {
                Content.ResetPresentation();
                Policy.EnterSpace(id);
            }
        }

        [TearDown]
        public void RestoreFrameRate() { FramePacing.TargetFrameRate = FramePacing.DefaultFrameRate; }

        [Test]
        public void SpaceTable_FullInHomeAndCurvedWalk_LightEverywhereElse()
        {
            Assert.That(SpaceFurniture.Level(SpaceId.Home), Is.EqualTo(FurnitureLevel.Full));
            Assert.That(SpaceFurniture.Level(SpaceId.CurvedWalk), Is.EqualTo(FurnitureLevel.Full));
            foreach (SpaceId id in new[] { SpaceId.Glued, SpaceId.AlignedCells, SpaceId.SmallPlanet, SpaceId.Portal, SpaceId.Slicer })
                Assert.That(SpaceFurniture.Level(id), Is.EqualTo(FurnitureLevel.Light), id.ToString());
            for (int i = 0; i < SpaceIds.Count; i++)
                Assert.That(Enum.IsDefined(typeof(SpaceId), (SpaceId)i), Is.True);
        }

        [Test]
        public void EnteringASpace_SetsItsLevel()
        {
            var rig = new Rig();
            rig.Enter(SpaceId.Glued);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light));
            rig.Enter(SpaceId.Home);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
            rig.Enter(SpaceId.CurvedWalk);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
            rig.Enter(SpaceId.Slicer);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light));
            rig.Policy.EnterSpace(null);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full), "no space: Full");
        }

        [Test]
        public void LadderFirstStep_ForcesLightInHome_AndNominalGivesItBack()
        {
            var rig = new Rig();
            rig.Enter(SpaceId.Home);
            rig.Ladder.Applier.Apply(WorldTier.Fair);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light));
            rig.Ladder.Applier.Apply(WorldTier.Critical);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light));
            rig.Ladder.Applier.Apply(WorldTier.Nominal);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
            rig.Ladder.Applier.Apply(WorldTier.High);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
        }

        [Test]
        public void LadderAtFair_EnteringAnySpace_StaysLight()
        {
            var rig = new Rig();
            rig.Ladder.Applier.Apply(WorldTier.Fair);
            rig.Enter(SpaceId.CurvedWalk);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light));
        }

        [Test]
        public void Resolve_AutoLightFull()
        {
            // auto: the space's level, held down by the ladder
            Assert.That(FurnitureLevelPolicy.Resolve(FurnitureMode.Auto, FurnitureLevel.Full, FurnitureLevel.Full), Is.EqualTo(FurnitureLevel.Full));
            Assert.That(FurnitureLevelPolicy.Resolve(FurnitureMode.Auto, FurnitureLevel.Light, FurnitureLevel.Full), Is.EqualTo(FurnitureLevel.Light));
            Assert.That(FurnitureLevelPolicy.Resolve(FurnitureMode.Auto, FurnitureLevel.Full, FurnitureLevel.Light), Is.EqualTo(FurnitureLevel.Light));
            // light: always
            Assert.That(FurnitureLevelPolicy.Resolve(FurnitureMode.Light, FurnitureLevel.Full, FurnitureLevel.Full), Is.EqualTo(FurnitureLevel.Light));
            // full: overrides the space and the ladder
            Assert.That(FurnitureLevelPolicy.Resolve(FurnitureMode.Full, FurnitureLevel.Light, FurnitureLevel.Light), Is.EqualTo(FurnitureLevel.Full));
        }

        [Test]
        public void Parameter_HasThreeValues_AndDefaultsToAuto()
        {
            var registry = new ParamRegistry();
            var rig = new Rig();
            AppParams.DeclareRender(registry, rig.Ladder, new FlatWalkerKernel(), null, null, null, false, null, null, rig.Policy);
            registry.PushAll();
            ParamEntry entry = registry.Find(AppParams.Furniture);
            Assert.That(AppParams.Furniture, Is.EqualTo("render.furniture"));
            Assert.That(entry.Kind, Is.EqualTo(ParamKind.Enum));
            Assert.That(entry.Group, Is.EqualTo("render"));
            Assert.That(entry.Int, Is.EqualTo(0));
            Assert.That(entry.EnumCount, Is.EqualTo(3));
            Assert.That(entry.EnumName(0), Is.EqualTo("auto"));
            Assert.That(entry.EnumName(1), Is.EqualTo("light"));
            Assert.That(entry.EnumName(2), Is.EqualTo("full"));
            Assert.That(rig.Policy.Mode, Is.EqualTo(FurnitureMode.Auto));

            rig.Enter(SpaceId.Home);
            registry.Set(AppParams.Furniture, 1f);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light), "light");
            rig.Enter(SpaceId.Home);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light), "light holds in Home");

            registry.Set(AppParams.Furniture, 2f);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full), "full");
            rig.Enter(SpaceId.Glued);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full), "full in a light space");
            rig.Ladder.Applier.Apply(WorldTier.Serious);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full), "full overrides the ladder");

            registry.Set(AppParams.Furniture, 0f);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light), "auto: Glued is light, and Serious holds it there");
        }

        // The real stage, with the look given: the room is reset first, then the level of the next space is set.
        [Test]
        public void SpaceStage_ResetsThePresentation_ThenSetsTheLevel()
        {
            var rig = new Rig();
            var stage = new SpaceStage(rig.Content, rig.Policy, () => { }, fade => { });
            rig.Content.Log.Clear();
            stage.ResetForSpace(SpaceId.Glued);
            Assert.That(rig.Content.Log, Is.EqualTo(new[] { "reset", "level:Light" }));
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light), "a reset after the set would leave Full");

            rig.Content.Log.Clear();
            stage.ResetForSpace(SpaceId.Home);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
            stage.ResetForSpace(SpaceId.Portal);
            stage.ResetForSpace(null);
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full), "no space: Full");
        }

        [Test]
        public void SpaceStage_WithNoRoomBuilt_OrNoPolicy_DoesNotThrow()
        {
            var content = new LevelContent();
            var stage = new SpaceStage(content, null, () => { }, fade => { });
            Assert.DoesNotThrow(() => stage.ResetForSpace(SpaceId.Home));
            Assert.DoesNotThrow(() => new SpaceStage(null, null, () => { }, fade => { }).ResetForSpace(null));
        }

        [Test]
        public void RebuildingTheRoom_PutsTheLevelBack()
        {
            var rig = new Rig();
            rig.Enter(SpaceId.Glued);
            rig.Content.Build(new RoomModel { Id = "again" });       // ends with ResetPresentation: Full
            Assert.That(rig.Content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Light), "the policy reapplies it on Built");
        }

        [Test]
        public void ParameterWithoutAPolicy_StillExists()
        {
            var registry = new ParamRegistry();
            var ladder = new QualityLadder(new QualityApplier(null));
            AppParams.DeclareRender(registry, ladder, new FlatWalkerKernel(), null, null, null, false, null, null);
            registry.PushAll();
            Assert.That(registry.Find(AppParams.Furniture), Is.Not.Null);
        }
    }
}
