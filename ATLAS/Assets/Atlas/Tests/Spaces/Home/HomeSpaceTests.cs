using System.Collections.Generic;
using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering.Room;
using Atlas.Rendering;
using Atlas.Spaces.Home;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests.Home
{
    /// <summary>Home (S0): registration, the lifecycle against the room content, the floor marks, State and the budget.</summary>
    public class HomeSpaceTests
    {
        /// <summary>Records what a space asks of the room content, in order. Touches no engine object.</summary>
        sealed class FakeRoomContent : IRoomContent
        {
            public readonly List<string> Calls = new List<string>();
            RoomModel m_Room;
            WallMode m_Walls = WallMode.Kerbs;
            bool m_Floor, m_Objects;

            public RoomModel Room { get { return m_Room; } }
            public bool IsBuilt { get { return m_Room != null; } }
            public void Build(RoomModel room) { m_Room = room; Calls.Add("Build"); }
            public void Clear() { m_Room = null; Calls.Add("Clear"); }
            public UnityEngine.Transform Root { get { return null; } }
            public WallMode Walls { get { return m_Walls; } }
            public float LastSeconds = -1f;
            public void SetWallMode(WallMode mode, float seconds = 0.5f)
            {
                m_Walls = mode;
                LastSeconds = seconds;
                Calls.Add("SetWallMode:" + mode);
            }
            public float KerbHeight { get; set; }
            public bool FloorVisible { get { return m_Floor; } set { m_Floor = value; Calls.Add("FloorVisible:" + value); } }
            public bool ObjectsVisible { get { return m_Objects; } set { m_Objects = value; Calls.Add("ObjectsVisible:" + value); } }
            public void ResetPresentation() { Calls.Add("ResetPresentation"); }
            public UnityEngine.Renderer FloorRenderer { get { return null; } }
            public UnityEngine.Renderer WallRenderer { get { return null; } }
            public UnityEngine.Material ShellMaterial { get { return null; } }
            public int ShellTriangleCount { get { return 0; } }
            public int ObjectCount { get { return m_Room != null ? m_Room.Objects.Length : 0; } }
            public IRoomObjectView GetObject(int index) { return null; }
            public bool TryGetObject(int objectId, out IRoomObjectView view) { view = null; return false; }
            public bool SetObjectState(int objectId, MeshState state) { Calls.Add("SetObjectState"); return false; }
            public bool SetGeneratedMesh(int objectId, UnityEngine.Mesh mesh, string assetHash, float fadeSeconds = 0.3f)
            {
                Calls.Add("SetGeneratedMesh");
                return false;
            }
            public FurnitureLevel FurnitureLevel { get; set; }
            public bool SetGeneratedLightMesh(int objectId, UnityEngine.Mesh mesh, string assetHashLight) { return false; }
            public bool SetBuiltInMesh(int objectId, UnityEngine.Mesh sharedMesh, UnityEngine.Vector3 scale, float fadeSeconds = 0.3f)
            {
                Calls.Add("SetBuiltInMesh");
                return false;
            }
#pragma warning disable 67
            public event Action<RoomModel> Built;
            public event Action<int> ObjectChanged;
#pragma warning restore 67
        }

        static RoomModel Load(string name) { return RoomFixtures.LoadFromProject(name); }

        // ---- Registration ----------------------------------------------------------------------------------

        [Test]
        public void Registration_PutsHomeInTheRegistry()
        {
            HomeRegistration.Register();
            Assert.That(SpaceRegistry.IsRegistered(SpaceId.Home), Is.True);
            // Safe to run twice: no domain reload in the Editor.
            HomeRegistration.Register();
            Assert.That(SpaceRegistry.IsRegistered(SpaceId.Home), Is.True);
        }

        [Test]
        public void Registration_DeclaresTheHomeGroup()
        {
            HomeRegistration.Register();
            var registry = new ParamRegistry();
            SpaceRegistry.DeclareAllParams(registry);

            Assert.That(registry.TryGet(HomeSpaceCore.MarksParam, out ParamEntry marks), Is.True);
            Assert.That(marks.Group, Is.EqualTo("home"));
            Assert.That(marks.Group, Is.EqualTo(SpaceIds.ParamGroup(SpaceId.Home)));
            Assert.That(marks.Kind, Is.EqualTo(ParamKind.Bool));
            Assert.That(marks.Bool, Is.True);
        }

        [Test]
        public void Configure_DeclaresAgainAndKeepsTheValue()
        {
            var registry = new ParamRegistry();
            HomeSpaceCore.DeclareParams(registry);
            int declared = registry.Count;
            registry.Set(HomeSpaceCore.MarksParam, 0f);

            var core = new HomeSpaceCore();
            core.Configure(Load(RoomFixtures.Sample3x3), registry, new FakeRoomContent());

            Assert.That(registry.Count, Is.EqualTo(declared));
            Assert.That(core.MarksWanted, Is.False);
        }

        // ---- Lifecycle -------------------------------------------------------------------------------------

        [Test]
        public void Lifecycle_DrivesTheRoomContentInOrder()
        {
            RoomModel room = Load(RoomFixtures.Sample4x5Table);
            var content = new FakeRoomContent();
            content.Build(room);            // what the installer does on RoomChanged
            content.Calls.Clear();
            var core = new HomeSpaceCore();

            core.Configure(room, new ParamRegistry(), content);
            Assert.That(content.Calls, Is.Empty, "Configure only keeps the room");
            Assert.That(core.Room, Is.SameAs(room));
            Assert.That(core.Registered, Is.True);
            Assert.That(core.Entered, Is.False);

            core.Enter();
            Assert.That(content.Calls, Is.EqualTo(new[] { "SetWallMode:Full", "FloorVisible:True", "ObjectsVisible:True" }));
            Assert.That(content.Walls, Is.EqualTo(WallMode.Full));
            Assert.That(content.LastSeconds, Is.EqualTo(0f), "the world is black: the walls stand at once");
            Assert.That(content.FloorVisible, Is.True);
            Assert.That(content.ObjectsVisible, Is.True);
            Assert.That(core.Entered, Is.True);
            Assert.That(core.Registered, Is.True);

            content.Calls.Clear();
            core.Exit();
            Assert.That(content.Calls, Is.Empty, "Exit leaves the room content for the next space");
            Assert.That(content.IsBuilt, Is.True);
            Assert.That(content.Room, Is.SameAs(room));
            Assert.That(core.Entered, Is.False);
        }

        [Test]
        public void Enter_BuildsTheRoomWhenNothingElseDid()
        {
            RoomModel room = Load(RoomFixtures.Sample3x3);
            var content = new FakeRoomContent();
            var core = new HomeSpaceCore();
            core.Configure(room, null, content);
            core.Enter();
            Assert.That(content.Calls[0], Is.EqualTo("Build"));
            Assert.That(content.Calls[1], Is.EqualTo("SetWallMode:Full"));
            Assert.That(content.Room, Is.SameAs(room));
            Assert.That(content.ObjectCount, Is.EqualTo(room.Objects.Length));
        }

        [Test]
        public void Enter_RebuildsForAnotherRoom()
        {
            var content = new FakeRoomContent();
            content.Build(Load(RoomFixtures.Sample3x3));
            content.Calls.Clear();
            RoomModel room = Load(RoomFixtures.SampleL3x4);
            var core = new HomeSpaceCore();
            core.Configure(room, null, content);
            core.Enter();
            Assert.That(content.Calls[0], Is.EqualTo("Build"));
            Assert.That(content.Room, Is.SameAs(room));
        }

        [Test]
        public void Lifecycle_RunsWithNoRoomContent()
        {
            var core = new HomeSpaceCore();
            core.Configure(Load(RoomFixtures.Sample3x3), null, null);
            Assert.DoesNotThrow(() => core.Enter());
            Assert.That(core.CentreMarkShown, Is.True);
            Assert.DoesNotThrow(() => core.Exit());
        }

        [Test]
        public void Enter_BeforeConfigure_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => new HomeSpaceCore().Enter());
            Assert.Throws<ArgumentNullException>(() => new HomeSpaceCore().Configure(null, null, null));
        }

        // ---- Marks -----------------------------------------------------------------------------------------

        [Test]
        public void Marks_ShownWhileEntered_AndFollowTheSwitch()
        {
            var registry = new ParamRegistry();
            var core = new HomeSpaceCore();
            core.Configure(Load(RoomFixtures.Sample3x3), registry, new FakeRoomContent());
            Assert.That(core.CentreMarkShown, Is.False, "nothing is shown before Enter");

            core.Enter();
            Assert.That(core.CentreMarkShown, Is.True);
            Assert.That(core.LapMarksShown, Is.True);

            registry.Set(HomeSpaceCore.MarksParam, 0f);
            Assert.That(core.CentreMarkShown, Is.False);
            Assert.That(core.LapMarksShown, Is.False);
            registry.Set(HomeSpaceCore.MarksParam, 1f);
            Assert.That(core.CentreMarkShown, Is.True);

            core.Exit();
            Assert.That(core.CentreMarkShown, Is.False);
            Assert.That(core.LapMarksShown, Is.False);
        }

        [Test]
        public void StandingMode_HidesTheLapMarks()
        {
            RoomModel room = Load(RoomFixtures.TinyStanding);
            Assert.That(room.Standing, Is.True);
            var core = new HomeSpaceCore();
            core.Configure(room, new ParamRegistry(), new FakeRoomContent());
            core.Enter();

            Assert.That(core.Standing, Is.True);
            Assert.That(core.CentreMarkShown, Is.True, "the centre mark stays");
            Assert.That(core.LapMarksShown, Is.False);
            Assert.That(HomeMarks.HasLapOutline(room), Is.False);

            var data = new AtlasMeshBuilder();
            HomeMarks.Build(room, data);
            Assert.That(data.TriangleCount, Is.EqualTo(HomeMarks.CentreTriangles));
            for (int k = 0; k < data.VertexCount; k++)
            {
                float3 p = data.Vertices[k].Position;
                Assert.That(math.length(p.xz), Is.LessThanOrEqualTo(HomeMarks.RingOuterRadius + 1e-5f));
            }
        }

        [Test]
        public void Marks_LapOutlineFollowsTheRoom()
        {
            foreach (string name in new[] { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table, RoomFixtures.BudgetRoom })
            {
                RoomModel room = Load(name);
                Assert.That(room.Standing, Is.False, name);
                var data = new AtlasMeshBuilder();
                HomeMarks.Build(room, data);

                Assert.That(data.TriangleCount, Is.EqualTo(HomeMarks.Triangles(room)), name);
                Assert.That(data.TriangleCount, Is.GreaterThan(HomeMarks.CentreTriangles), name);
                Assert.That(data.LongestEdge(), Is.LessThanOrEqualTo(AtlasMeshBuilder.MaxEdge * 1.001f + HomeMarks.LapLineWidth), name);

                // The outline is the lap square of the RoomModel: its outer rim is half a side plus half a line out.
                float reach = 0.5f * room.LapSide + 0.5f * HomeMarks.LapLineWidth;
                float farthest = 0f;
                for (int k = 0; k < data.VertexCount; k++)
                {
                    float3 p = data.Vertices[k].Position;
                    Assert.That(p.y, Is.EqualTo(HomeMarks.Height).Within(1e-6f), name);
                    farthest = math.max(farthest, math.max(math.abs(p.x), math.abs(p.z)));
                    Assert.That(room.FreeRect.Contains(p.xz), Is.True, name + ": the marks lie inside the free rectangle");
                }
                Assert.That(farthest, Is.EqualTo(reach).Within(1e-4f), name);
            }
        }

        [Test]
        public void Marks_FaceUp_AndUseTheAccentCells()
        {
            RoomModel room = Load(RoomFixtures.Sample3x3);
            var data = new AtlasMeshBuilder();
            HomeMarks.Build(room, data);
            for (int k = 0; k < data.IndexCount; k += 3)
            {
                float3 a = data.Vertices[data.Indices[k]].Position;
                float3 b = data.Vertices[data.Indices[k + 1]].Position;
                float3 c = data.Vertices[data.Indices[k + 2]].Position;
                // Unity's clockwise winding: the front face looks along cross(b - a, c - a).
                Assert.That(math.cross(b - a, c - a).y, Is.GreaterThan(0f), "triangle " + (k / 3));
            }
            for (int k = 0; k < data.VertexCount; k++)
            {
                AtlasVertex v = data.Vertices[k];
                Assert.That(v.Normal.y, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(v.Uv0.y, Is.GreaterThanOrEqualTo(AtlasPalette.EmissiveV), "marks glow: an accent cell");
                Assert.That(v.LineWeight, Is.EqualTo(0), "no grid lines on a mark");
            }
        }

        // ---- State -----------------------------------------------------------------------------------------

        [Test]
        public void WriteState_WritesHomesRow_AndNothingElse()
        {
            StateMessage state = StateMessage.Identity;
            state.SceneId = 7;
            state.SpaceId = (byte)SpaceId.Home;
            state.BumpLevel = 2;
            state.WorldVer = 41;
            state.HeadPosX = 1f; state.HeadPosY = 1.6f; state.HeadPosZ = -2f;
            state.P0 = 1f; state.P1 = 2f; state.P2 = 3f;
            // Left over from another space: Home must clear what it owns.
            state.SetTile(5L, 4L, 3L, 2L, 1L, 9L);
            state.Sheet = 3;
            state.SetXform(1, 0.5);
            state.P3 = 9f; state.P4 = 9f; state.P5 = 9f; state.P6 = 9f; state.P7 = 9f;

            HomeSpaceCore.WriteState(ref state);

            for (int i = 0; i < StateMessage.TileLength; i++) Assert.That(state.GetTile(i), Is.EqualTo(0L));
            Assert.That(state.Sheet, Is.EqualTo(0));
            for (int i = 0; i < StateMessage.XformLength; i++)
                Assert.That(state.GetXform(i), Is.EqualTo(i % 4 == 0 ? 1.0 : 0.0), "xform " + i);
            for (int i = 3; i < StateMessage.ParamLength; i++) Assert.That(state.GetParam(i), Is.EqualTo(0f));

            Assert.That(state.SceneId, Is.EqualTo(7));
            Assert.That(state.SpaceId, Is.EqualTo(0));
            Assert.That(state.BumpLevel, Is.EqualTo(2));
            Assert.That(state.WorldVer, Is.EqualTo(41));
            Assert.That(state.HeadPosY, Is.EqualTo(1.6f));
            Assert.That(state.HeadRotW, Is.EqualTo(1f));
            Assert.That(state.P0, Is.EqualTo(1f));
            Assert.That(state.P1, Is.EqualTo(2f));
            Assert.That(state.P2, Is.EqualTo(3f));
        }

        // ---- Budget ----------------------------------------------------------------------------------------

        [Test]
        public void Budget_HomeOnBudgetRoom_IsInsideItsRow()
        {
            RoomModel room = Load(RoomFixtures.BudgetRoom);
            HomeDrawCost cost = HomeBudget.Estimate(room);

            Assert.That(cost.MarkDrawCalls, Is.EqualTo(1));
            Assert.That(cost.MarkTriangles, Is.EqualTo(HomeMarks.Triangles(room)));
            Assert.That(cost.DrawCallsPerEye, Is.EqualTo(cost.Room.DrawCallsPerEye + 1));
            Assert.That(cost.TrianglesPerEye, Is.EqualTo(cost.Room.TrianglesPerEye + cost.MarkTriangles));
            Assert.That(cost.DrawCallsPerFrame, Is.LessThanOrEqualTo(80));
            Assert.That(cost.TrianglesPerFrame, Is.LessThanOrEqualTo(250000));
            Assert.That(HomeBudget.Fits(cost), Is.True);
            TestContext.WriteLine("Home on budget_room: " + cost.DrawCallsPerFrame + " draw calls, " +
                                  cost.TrianglesPerFrame + " triangles per frame");
        }

        [Test]
        public void Budget_EveryFixtureFits_AndTheMarksStaySmall()
        {
            foreach (string name in RoomFixtures.All)
            {
                RoomModel room = Load(name);
                HomeDrawCost cost = HomeBudget.Estimate(room);
                Assert.That(HomeBudget.Fits(cost), Is.True, name);
                Assert.That(cost.MarkTriangles, Is.LessThanOrEqualTo(200), name);
                HomeDrawCost bare = HomeBudget.Estimate(room, false);
                Assert.That(bare.MarkDrawCalls, Is.EqualTo(0), name);
                Assert.That(bare.TrianglesPerEye, Is.EqualTo(cost.Room.TrianglesPerEye), name);
            }
        }

        [Test]
        public void Budget_LargestLapSquare_CountsAsBuilt()
        {
            // 3.2 m is the cap of the loop-fit rule (Master Plan section 4.4).
            var data = new AtlasMeshBuilder();
            HomeMarks.AddLapOutline(3.2f, data);
            Assert.That(data.TriangleCount, Is.EqualTo(HomeMarks.LapTriangles(3.2f)));
            Assert.That(HomeMarks.CentreTriangles + data.TriangleCount, Is.LessThanOrEqualTo(200));
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void Module_FromTheFactory_IsNamedLikeTheScene_AndRunsBare()
        {
            ISpaceModule module = HomeSpace.Create();
            var home = (HomeSpace)module;
            try
            {
                Assert.That(home.gameObject.name, Is.EqualTo("Space_Home"));
                Assert.That(module.Id, Is.EqualTo(SpaceId.Home));
                Assert.That(module.Registered, Is.True);

                var registry = new ParamRegistry();
                module.Configure(Load(RoomFixtures.Sample3x3), registry);
                module.Enter(default);
                Assert.That(home.Core.Entered, Is.True);
                Assert.That(home.transform.position, Is.EqualTo(UnityEngine.Vector3.zero));

                var frame = new FrameContext();
                module.Tick(in frame);
                StateMessage state = StateMessage.Identity;
                module.WriteState(ref state);
                Assert.That(state.GetTile(0), Is.EqualTo(0L));

                module.Exit();
                Assert.That(home.Core.Entered, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(home.gameObject);
            }
        }
#endif
    }
}
