#if !ATLAS_HEADLESS
using System;
using System.Collections.Generic;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// The info card and the gluing sign in the real module (Master Plan section 10.1.3, task P2-C14): the card is built
    /// and says which gluing is on, the sign registers a gaze twin per gluing and one for "seen it", a button of the sign
    /// picks its own gluing, a bumper steps, and A ends the wave step.
    /// </summary>
    [Category("Unity")]
    public class GluedCardUnityTests
    {
        const float Dt = 1f / 60f;

        /// <summary>Records the gaze targets and fires the actions a test asks for, one frame each.</summary>
        sealed class FakeInput : IAtlasInput
        {
            public readonly List<GazeTarget> Targets = new List<GazeTarget>();
            AtlasAction m_Pressed;
            InputSource m_Source;
            int m_Tag;

            public void Press(AtlasAction action, InputSource source, int tag = 0)
            {
                m_Pressed = action;
                m_Source = source;
                m_Tag = tag;
            }

            public void EndFrame() { m_Pressed = AtlasAction.None; }

            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime) { }
            public InputContext Context { get { return InputContext.Space; } }
            public void PushContext(InputContext context) { }
            public void PopContext(InputContext context) { }
            public bool Pressed(AtlasAction action) { return action == m_Pressed; }
            public bool Held(AtlasAction action) { return false; }
            public float Axis(AtlasAxis axis) { return 0f; }
            public InputSource LastSource(AtlasAction action) { return action == m_Pressed ? m_Source : InputSource.None; }
            public int LastTag(AtlasAction action) { return action == m_Pressed ? m_Tag : 0; }
            public event Action<AtlasAction, InputSource, int> ActionFired { add { } remove { } }
            public float DwellSeconds { get; set; }
            public GazeTargetId RegisterGazeTarget(in GazeTarget target) { Targets.Add(target); return default(GazeTargetId); }
            public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { }
            public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { }
            public void UnregisterGazeTarget(GazeTargetId id) { }
            public GazeState Gaze { get { return default(GazeState); } }
            public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { }
            public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
            public bool GamepadPresent { get { return true; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

        MaterialPool m_Pool;

        [TearDown]
        public void TearDown()
        {
            AtlasServices.Clear();
            if (m_Pool != null) m_Pool.Dispose();
            m_Pool = null;
        }

        [Test]
        public void TheModule_ShowsTheCard_AndTheGluingSignPicksAGluing()
        {
            m_Pool = MaterialPool.CreateDefault(64);
            var input = new FakeInput();
            AtlasServices.Register(m_Pool);
            AtlasServices.Register<IAtlasInput>(input);
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var registry = new ParamRegistry();
            var module = (GluedSpace)GluedSpace.Create();
            try
            {
                module.Configure(room, registry);
                module.Enter(default(WalkerPose));
                Assert.That(module.Panel, Is.Not.Null, "a build with the look has a card");
                Assert.That(module.Tour, Is.Not.Null);

                // The sign: T2 on the left bumper's action, Klein and T3 on the right's, each with its gluing's tag; then "seen it".
                Assert.That(input.Targets.Count, Is.EqualTo(GluedPanel.ButtonCount));
                Assert.That(input.Targets[0].Action, Is.EqualTo(AtlasAction.VariantPrev));
                Assert.That(input.Targets[1].Action, Is.EqualTo(AtlasAction.VariantNext));
                Assert.That(input.Targets[2].Action, Is.EqualTo(AtlasAction.VariantNext));
                Assert.That(input.Targets[3].Action, Is.EqualTo(AtlasAction.Confirm));
                for (int i = 0; i < 3; i++) Assert.That(input.Targets[i].Tag, Is.EqualTo(GluedGluingSwitch.SignTag((GluedLayout)i)));

                var frame = new FrameContext { DeltaTime = Dt, HeadPosition = new float3(0f, 1.6f, 0f), HeadRotation = quaternion.identity };
                Tick(module, input, ref frame);
                if (module.PanelText != null)
                {
                    StringAssert.Contains("TORUS", module.PanelText);
                    StringAssert.Contains("Step 1 of 4", module.PanelText);
                    StringAssert.Contains("x mod a", module.PanelText);
                }

                // A dwell on T3 of the sign picks the 3-torus; the left bumper steps back to Klein.
                input.Press(AtlasAction.VariantNext, InputSource.Gaze, GluedGluingSwitch.SignTag(GluedLayout.Torus3));
                Tick(module, input, ref frame);
                Assert.That(registry.GetInt(GluedSpace.ParamMode, -1), Is.EqualTo((int)GluedLayout.Torus3));
                Tick(module, input, ref frame);
                Assert.That(module.Layout, Is.EqualTo(GluedLayout.Torus3));
                if (module.PanelText != null) StringAssert.Contains("3-TORUS", module.PanelText);

                input.Press(AtlasAction.VariantPrev, InputSource.Gamepad);
                Tick(module, input, ref frame);
                Tick(module, input, ref frame);
                Assert.That(module.Layout, Is.EqualTo(GluedLayout.Klein));

                // Walking across a cell edge ends the wrap step; A ends the wave step.
                GluedLattice lattice = module.Tour.Lattice;
                double3 next = lattice.CellCentre(new GluedCell(1, 0));
                frame.HeadPosition = new float3((float)lattice.CellCentre(new GluedCell(0, 0)).x, 1.6f, (float)next.z);
                Tick(module, input, ref frame);
                frame.HeadPosition = new float3((float)next.x, 1.6f, (float)next.z);
                Tick(module, input, ref frame);
                Assert.That(module.Tour.Task, Is.EqualTo(GluedTask.Wave));
                Assert.That(module.Panel.SeenItShown, Is.True, "\"seen it\" stands in for A in the wave step");
                input.Press(AtlasAction.Confirm, InputSource.Gamepad);
                Tick(module, input, ref frame);
                Assert.That(module.Tour.Task, Is.Not.EqualTo(GluedTask.Wave));
                Assert.That(module.Panel.SeenItShown, Is.False);

                module.Exit();
                Assert.That(module.Panel, Is.Null, "Exit frees the card");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(module.gameObject);
            }
        }

        static void Tick(GluedSpace module, FakeInput input, ref FrameContext frame)
        {
            frame.Time += Dt;
            module.Tick(in frame);
            input.EndFrame();
        }
    }
}
#endif
