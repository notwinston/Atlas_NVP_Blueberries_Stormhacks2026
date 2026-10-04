#if !ATLAS_HEADLESS
using System.Collections;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The composition root in the engine: needs the Test Runner. The play-mode test is the Editor-first path
    /// of contract README F9 from start to stop, with no boot scene and no phone.
    /// </summary>
    [Category("Unity")]
    public class BootstrapUnityTests
    {
        // An edit-mode test is resumed by the Editor's own update, which in batch mode runs several times per
        // play-mode frame (the app paces itself to its target frame rate): a count of yields is neither a
        // count of frames nor a time. Every wait below is therefore for a state, an app frame or an engine
        // event, bounded by this real time so that a flow that hangs still fails at its own assertion.
        const double WaitSeconds = 30.0;

        static double Deadline() { return Time.realtimeSinceStartupAsDouble + WaitSeconds; }
        static bool Before(double deadline) { return Time.realtimeSinceStartupAsDouble < deadline; }

        [UnityTest]
        public IEnumerator Boot_InPlayMode_WalksTheWholeFlow_ThenStopsCleanly()
        {
            yield return new EnterPlayMode();

            // Not the boot scene: nothing has booted by itself.
            Assert.That(AtlasBootstrap.App, Is.Null);
            AtlasApp app = AtlasBootstrap.Start(new BootOptions { Room = RoomFixtures.Sample3x3 });
            Assert.That(app, Is.Not.Null, "boot failed: see the console");
            Assert.That(AtlasServices.BootCompleted, Is.True);
            Assert.That(AtlasBootstrap.Start(), Is.SameAs(app), "a second start returns the running app");

            Assert.That(AtlasServices.Has<ParamRegistry>(), Is.True);
            Assert.That(AtlasServices.Has<MaterialPool>(), Is.True);
            Assert.That(AtlasServices.Has<IStereoRig>(), Is.True);
            Assert.That(AtlasServices.Has<IFrameLoop>(), Is.True);
            Assert.That(AtlasServices.Has<IWalkerKernel>(), Is.True);
            Assert.That(AtlasServices.Has<IAtlasInput>(), Is.True);
            Assert.That(AtlasServices.Has<IPosePipeline>(), Is.True);
            Assert.That(AtlasServices.Has<IBumpGuard>(), Is.True);
            Assert.That(AtlasServices.Has<IRoomContent>(), Is.True);
            Assert.That(AtlasServices.Has<IQualityState>(), Is.True);
            Assert.That(AtlasServices.Has<ISpaceHost>(), Is.True);
            Assert.That(AtlasServices.Has<IOperatorCommands>(), Is.True);
            Assert.That(AtlasServices.Has<IEventLog>(), Is.True);
            Assert.That(app.GuardInstalled, Is.True);
            Assert.That(app.LoopCore.HasGuard, Is.True);
            Assert.That(app.Boot.Head, Is.Not.Null);
            Assert.That(app.Boot.Hud.gameObject.layer, Is.EqualTo(7));
            Assert.That(app.Registry[0].Name, Is.EqualTo(AppParams.Space));
            Assert.That(app.Spaces.IsAvailable(SpaceId.Home), Is.True, "Home is available: its own module, or the built-in grid space when Home is not in the build");

            // The fixture room is handed over and Home is entered through a fade.
            double deadline = Deadline();
            while (app.Spaces.State != SpaceHostState.Running && Before(deadline)) yield return null;
            Assert.That(app.Spaces.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(app.Spaces.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(app.Spaces.Room.Id, Is.Not.Empty);
            Assert.That(app.RoomContent.IsBuilt, Is.True);
            Assert.That(app.Guard.Room, Is.Not.Null, "Bump Guard has the room's static map");
            Assert.That(app.Guard.Armed, Is.True);
            Assert.That(app.Rig.Mode, Is.EqualTo(DisplayMode.Headset));
            Assert.That(app.Pose.Pipeline.Sensors.RoomActive, Is.True);
            Assert.That(app.LoopCore.FrameCount, Is.GreaterThan(5));
            Assert.That(app.WalkerKernel.StepCount, Is.EqualTo((long)app.LoopCore.FrameCount), "one kernel step per frame");
            Assert.That(app.LoopCore.LastState.SpaceId, Is.EqualTo(0));
            Assert.That(AtlasLook.State.Fade, Is.EqualTo(1f));

            // Operator commands are local calls.
            int rented = app.Pool.Rented;
            app.Commands.Cue();
            deadline = Deadline();
            while (BumpCue.Shown != BumpLevel.Strong && Before(deadline)) yield return null;
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong));
            app.Commands.SwitchSpace((SpaceId)SpaceIds.Count, null);
            // Two whole app frames: a switch that had wrongly begun would have left Running by then.
            int refusedAt = app.LoopCore.FrameCount;
            deadline = Deadline();
            while (app.LoopCore.FrameCount < refusedAt + 2 && Before(deadline)) yield return null;
            Assert.That(app.LoopCore.FrameCount, Is.GreaterThanOrEqualTo(refusedAt + 2), "the app's frames stopped");
            Assert.That(app.Spaces.Current, Is.EqualTo(SpaceId.Home), "a space no build has is refused");
            Assert.That(app.Spaces.State, Is.EqualTo(SpaceHostState.Running));

            // All seven spaces are in the build. A curved one puts the app's one kernel on its tiling, and
            // Home gets it back flat with every rented material returned.
            for (int i = 0; i < SpaceIds.Count; i++)
                Assert.That(app.Spaces.IsAvailable((SpaceId)i), Is.True, SpaceIds.Code((SpaceId)i) + " is registered");
            app.Commands.SwitchSpace(SpaceId.CurvedWalk, null);
            deadline = Deadline();
            while (!(app.Spaces.State == SpaceHostState.Running && app.Spaces.Current == SpaceId.CurvedWalk) && Before(deadline)) yield return null;
            Assert.That(app.Spaces.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(app.Spaces.Current, Is.EqualTo(SpaceId.CurvedWalk));
            Assert.That(app.WalkerKernel.Graph.Q, Is.EqualTo(5));
            app.Commands.Home();
            deadline = Deadline();
            while (!(app.Spaces.State == SpaceHostState.Running && app.Spaces.Current == SpaceId.Home) && Before(deadline)) yield return null;
            Assert.That(app.Spaces.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(app.Spaces.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(app.WalkerKernel.Graph.Q, Is.EqualTo(4));
            app.Commands.Home();
            app.Commands.Recentre();
            app.Commands.SetQuality(2);
            deadline = Deadline();
            while (app.Ladder.Tier != Atlas.World.QualityTier.Serious && Before(deadline)) yield return null;
            Assert.That(app.Ladder.Tier, Is.EqualTo(Atlas.World.QualityTier.Serious));
            Assert.That(app.Pool.Rented, Is.EqualTo(rented));

            AtlasBootstrap.Stop();
            Assert.That(AtlasBootstrap.App, Is.Null);
            Assert.That(AtlasServices.BootCompleted, Is.False);
            Assert.That(AtlasServices.Has<ISpaceHost>(), Is.False);
            // Destroy takes effect at the end of a play-mode frame.
            deadline = Deadline();
            while (app.Boot.Root != null && Before(deadline)) yield return null;
            Assert.That(app.Boot.Root == null, Is.True, "the Atlas objects are gone");

            yield return new ExitPlayMode();
        }

        [Test]
        public void GridSpace_BuildsItsGround_AndReturnsItsMaterial()
        {
            AtlasServices.Clear();
            MaterialPool pool = MaterialPool.CreateDefault(8);
            AtlasServices.Register(pool);
            ISpaceModule module = GridSpace.Create();
            var component = (GridSpace)module;
            try
            {
                Assert.That(module.Id, Is.EqualTo(SpaceId.Home));
                Assert.That(module.Registered, Is.True);
                Assert.That(component.gameObject.name, Is.EqualTo(SpaceIds.SceneName(SpaceId.Home)));

                module.Configure(new RoomModel { Id = "t", LapSide = 2f }, new ParamRegistry());
                Assert.That(pool.Rented, Is.EqualTo(1));
                var filter = component.GetComponentInChildren<MeshFilter>(true);
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh.triangles.Length / 3, Is.EqualTo(2 * 32 * 32));
                Assert.That(filter.gameObject.activeSelf, Is.False, "hidden until Enter");

                module.Enter(WalkerPose.Start);
                Assert.That(filter.gameObject.activeSelf, Is.True);
                module.Exit();
                Assert.That(pool.Rented, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(component.gameObject);
                pool.Dispose();
                AtlasServices.Clear();
            }
        }

        [Test]
        public void SceneSpaceLoader_UsesTheRegisteredFactory_AndParentsTheModule()
        {
            SpaceRegistry.Clear();
            var root = new GameObject("Space");
            try
            {
                var loader = new SceneSpaceLoader(root.transform);
                Assert.That(loader.IsAvailable(SpaceId.Home), Is.False);
                SpaceRegistry.Register(SpaceId.Home, GridSpace.Create);
                Assert.That(loader.IsAvailable(SpaceId.Home), Is.True);

                loader.Begin(SpaceId.Home);
                ISpaceModule module;
                Assert.That(loader.TryFinish(out module), Is.True);
                Assert.That(module, Is.Not.Null);
                Assert.That(((Component)module).transform.parent, Is.SameAs(root.transform));
                loader.Unload(module);
                Assert.That(root.transform.childCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(root);
                SpaceRegistry.Clear();
            }
        }
    }
}
#endif
