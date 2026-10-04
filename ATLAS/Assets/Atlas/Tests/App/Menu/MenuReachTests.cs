using System;
using System.Collections.Generic;
using Atlas.App.Menu;
using Atlas.Input;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Tests
{
    /// <summary>
    /// Where the on-device menu stands (lane D contract, section 5.7; documents/ATLAS_Hands_Design.md section
    /// 6.1): the panel and the chip that opens it, their gaze targets and what the view is asked to draw. The
    /// real adapter and the real input core run, with a recorder in place of the view. The first tests pin
    /// where the menu has always stood; the rest are the hands session, in which it comes within reach.
    /// </summary>
    public class MenuReachTests
    {
        const float Dt = 1f / 60f;
        static readonly float3 Head = new float3(0.3f, 1.6f, -0.4f);

        /// <summary>The engine-free input core behind the service's interface: no devices, gaze and contexts only.</summary>
        sealed class CoreInput : IAtlasInput
        {
            public readonly AtlasInputCore Core = new AtlasInputCore();
            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
            {
                Core.Tick(default(RawInput), new float3(headPosition.x, headPosition.y, headPosition.z),
                    new quaternion(headRotation.x, headRotation.y, headRotation.z, headRotation.w), deltaTime);
            }
            public InputContext Context { get { return Core.Context; } }
            public void PushContext(InputContext context) { Core.PushContext(context); }
            public void PopContext(InputContext context) { Core.PopContext(context); }
            public bool Pressed(AtlasAction action) { return Core.Pressed(action); }
            public bool Held(AtlasAction action) { return Core.Held(action); }
            public float Axis(AtlasAxis axis) { return Core.Axis(axis); }
            public InputSource LastSource(AtlasAction action) { return Core.LastSource(action); }
            public int LastTag(AtlasAction action) { return Core.LastTag(action); }
            public event Action<AtlasAction, InputSource, int> ActionFired
            {
                add { Core.ActionFired += value; }
                remove { Core.ActionFired -= value; }
            }
            public float DwellSeconds { get { return Core.DwellSeconds; } set { Core.DwellSeconds = value; } }
            public GazeTargetId RegisterGazeTarget(in GazeTarget target)
            {
                GazeTargetData data = target.ToData();
                return Core.Targets.Add(in data);
            }
            public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { Core.Targets.Move(id, new float3(centre.x, centre.y, centre.z)); }
            public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { Core.Targets.SetEnabled(id, enabled); }
            public void UnregisterGazeTarget(GazeTargetId id) { Core.Targets.Remove(id); }
            public GazeState Gaze { get { return new GazeState { Focused = Core.GazeFocused, Progress = Core.GazeProgress }; } }
            public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { Core.CopyActionsWithoutGazeTwin(into); }
            public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
            public bool GamepadPresent { get { return false; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

        /// <summary>Keeps what the adapter asked the view to draw.</summary>
        sealed class ViewLog : IMenuView
        {
            public bool Open, Near;
            public float Scale = 1f;
            public float3 Panel, Chip;
            public quaternion PanelRotation, ChipFrame;
            public int Shows, Reaches;
            public void SetOpen(bool open) { Open = open; }
            public void PlacePanel(float3 centre, quaternion rotation) { Panel = centre; PanelRotation = rotation; }
            public void PlaceChip(float3 centre, quaternion frame) { Chip = centre; ChipFrame = frame; }
            public void SetReach(bool near, float scale) { Near = near; Scale = scale; Reaches++; }
            public void Show(MenuModel model) { Shows++; }
            public void Dispose() { }
        }

        /// <summary>The frame loop as the menu reads it: the frame in progress.</summary>
        sealed class HeadLoop : IFrameLoop
        {
            public FrameContext Frame;
            public ref readonly FrameContext Current { get { return ref Frame; } }
            public void AddSense(ISenseStep step) { }
            public void SetGuard(ISafetyStep step) { }
            public void SetPoseFilter(IPoseFilterStep step) { }
            public void AddUpload(IUploadStep step) { }
            public void SetStateSink(IStateSink sink) { }
            public void AddAfterFrame(IAfterFrameStep step) { }
            public void ResetWalkerToFloor() { }
            public void ResetWalker(in Atlas.Kernel.WalkerPose pose) { }
        }

        /// <summary>The near layout as Atlas.Hands publishes it, with every number in the test's hands.</summary>
        sealed class Reach : IReachLayout
        {
            public bool Near { get; set; }
            public float3 Position { get; set; }
            public quaternion Rotation { get; set; }
            public float Scale { get; set; }
        }

        Reach _reach;
        IReachLayout _before;

        [SetUp]
        public void StartWithNoLayout()
        {
            // A layout another test left published would move every panel of this one.
            if (AtlasServices.TryGet(out _before)) AtlasServices.Unregister(_before);
            _reach = null;
        }

        [TearDown]
        public void PutTheLayoutBack()
        {
            if (_reach != null) AtlasServices.Unregister<IReachLayout>(_reach);
            if (_before != null) AtlasServices.Register(_before);
        }

        /// <summary>
        /// Publishes the near anchor of a hands session: 0.48 m from the head on a line 25 degrees below the
        /// horizontal, tilted to face the head (+Z away from the walker, +Y up along the panel).
        /// </summary>
        Reach Publish(bool near = true, float scale = 0.75f)
        {
            if (_reach != null) AtlasServices.Unregister<IReachLayout>(_reach);
            quaternion rotation = math.mul(quaternion.RotateY(math.radians(15f)), quaternion.RotateX(math.radians(25f)));
            _reach = new Reach { Near = near, Scale = scale, Rotation = rotation, Position = Head + math.mul(rotation, new float3(0f, 0f, 0.48f)) };
            AtlasServices.Register<IReachLayout>(_reach);
            return _reach;
        }

        /// <summary>The frame loop's steps that touch the menu, in their order: input at 1, upload at 6, the menu's own step at 8b.</summary>
        sealed class Rig
        {
            public readonly CoreInput Input = new CoreInput();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly MenuModel Model;
            public readonly OnDeviceMenu Menu = new OnDeviceMenu();
            public readonly ViewLog View = new ViewLog();
            public readonly HeadLoop Loop = new HeadLoop();
            public float3 Position = Head;
            public int Frame;

            public Rig()
            {
                Registry.DeclareFloat("render.scale", "render", 0.6f, 0.8f, 0.8f);
                Registry.DeclareBool("hud.visible", "hud", false);
                Model = new MenuModel(Registry, new FakeHost());
                Input.PushContext(InputContext.Space);
                Menu.Compose(Input, null, Loop, Model, View);
            }

            public void Step(quaternion rotation, int frames = 1)
            {
                for (int i = 0; i < frames; i++)
                {
                    Loop.Frame = new FrameContext
                    {
                        FrameIndex = Frame++, DeltaTime = Dt, RawHeadPosition = Position, RawHeadRotation = rotation,
                        HeadPosition = Position, HeadRotation = rotation
                    };
                    Input.Tick((Vector3)Position, (Quaternion)rotation, Dt);
                    Menu.Upload(in Loop.Frame);
                    Menu.AfterFrame(in Loop.Frame);
                }
            }

            public GazeTargetData Data(GazeTargetId id)
            {
                GazeTargetData data;
                Assert.That(Input.Core.Targets.TryGet(id, out data), Is.True, "the target is registered");
                return data;
            }

            public bool Enabled(GazeTargetId id) { return Input.Core.Targets.IsEnabled(id); }
        }

        static quaternion Toward(float3 from, float3 to)
        {
            return quaternion.LookRotation(math.normalize(to - from), new float3(0f, 1f, 0f));
        }

        static int FramesFor(float seconds) { return (int)Math.Round(seconds / Dt); }

        static float Angle(quaternion a, quaternion b)
        {
            return 2f * math.acos(math.min(math.abs(math.dot(a.value, b.value)), 1f));
        }

        /// <summary>Everything a rig holds about where the menu stands, for comparing two runs number by number.</summary>
        static List<string> Snapshot(Rig w)
        {
            var lines = new List<string>
            {
                "open " + w.Menu.IsOpen + " view open " + w.View.Open + " near " + w.View.Near + " scale " + w.View.Scale.ToString("R"),
                "panel " + w.View.Panel + " " + w.View.PanelRotation,
                "chip " + w.View.Chip + " " + w.View.ChipFrame,
                Line(w, "chip target", w.Menu.ChipTarget),
            };
            for (int i = 0; i < MenuLayout.TargetCount; i++) lines.Add(Line(w, "target " + i, w.Menu.TargetOf(i)));
            return lines;
        }

        static string Line(Rig w, string name, GazeTargetId id)
        {
            if (!id.IsValid) return name + " none";
            GazeTargetData d = w.Data(id);
            return name + " " + d.Action + " " + d.Shape + " " + d.Context + " " + d.Mode + " tag " + d.Tag + " at " + d.Centre
                + " r " + d.Radius.ToString("R") + " half " + d.HalfSize + " rot " + d.Rotation + " on " + w.Enabled(id);
        }

        // ---- where the menu has always stood ---------------------------------------------------------------

        [Test]
        public void Closed_TheChipStandsDownAndToTheLeft_InAHeadingFrameAtTheHead()
        {
            var w = new Rig();
            quaternion look = quaternion.RotateY(math.radians(30f));
            w.Step(look);

            float yaw, pitch;
            Assert.That(MenuFrame.TryYawPitch(look, out yaw, out pitch), Is.True);
            quaternion frame = quaternion.RotateY(yaw);
            float3 centre = Head + math.mul(frame, MenuLayout.OpenChip);

            GazeTargetData chip = w.Data(w.Menu.ChipTarget);
            Assert.That(chip.Centre, Is.EqualTo(centre), "to the last number");
            Assert.That(chip.Radius, Is.EqualTo(MenuLayout.OpenChipRadius));
            Assert.That(chip.Shape, Is.EqualTo(GazeShape.Sphere));
            Assert.That(chip.Action, Is.EqualTo(AtlasAction.ParamMenu));
            Assert.That(chip.Context, Is.EqualTo(InputContext.Space));
            Assert.That(w.Enabled(w.Menu.ChipTarget), Is.True);
            Assert.That(w.View.Chip, Is.EqualTo(centre));
            Assert.That(w.View.ChipFrame, Is.EqualTo(frame));
            Assert.That(w.View.Open, Is.False);
            Assert.That(w.View.Near, Is.False, "the view was never asked to come within reach");
            Assert.That(w.View.Reaches, Is.EqualTo(0));

            // The chip follows the head's position.
            w.Position = Head + new float3(0.5f, 0f, 0.25f);
            w.Step(look);
            Assert.That(w.Data(w.Menu.ChipTarget).Centre, Is.EqualTo(w.Position + math.mul(frame, MenuLayout.OpenChip)));
        }

        [Test]
        public void Open_ThePanelStandsAheadOfTheHead_WithItsTwelveTargetsOnIt()
        {
            var w = new Rig();
            quaternion look = quaternion.RotateY(math.radians(-20f));
            w.Step(look, 2);
            w.Menu.Open();
            Assert.That(w.Menu.IsOpen, Is.True);
            Assert.That(w.Input.Context, Is.EqualTo(InputContext.Menu));

            float yaw, pitch;
            MenuFrame.TryYawPitch(look, out yaw, out pitch);
            quaternion frame = quaternion.RotateY(yaw);
            int slot = w.Model.Selected - w.Model.Top;

            for (int pass = 0; pass < 2; pass++)
            {
                Assert.That(w.View.Open, Is.True);
                Assert.That(w.View.Panel, Is.EqualTo(w.Position + math.mul(frame, MenuLayout.PanelCentre)), "pass " + pass);
                Assert.That(w.View.PanelRotation, Is.EqualTo(frame));
                for (int i = 0; i < MenuLayout.TargetCount; i++)
                {
                    GazeTargetData d = w.Data(w.Menu.TargetOf(i));
                    float3 offset = math.mul(frame, MenuLayout.PanelCentre + MenuLayout.TargetCentre(i, slot));
                    Assert.That(d.Centre, Is.EqualTo(w.Position + offset), "target " + i + " to the last number, pass " + pass);
                    Assert.That(d.HalfSize, Is.EqualTo(MenuLayout.TargetHalfSize(i)), "target " + i);
                    Assert.That(d.Shape, Is.EqualTo(GazeShape.Rect));
                    Assert.That(Angle(d.Rotation, frame), Is.LessThan(1e-3f), "target " + i + " lies in the panel's plane");
                    Assert.That(d.Context, Is.EqualTo(InputContext.Menu));
                }
                // The second pass: the panel follows the head's position and keeps its heading.
                w.Position = Head + new float3(-0.2f, 0.05f, 0.3f);
                w.Step(look);
            }

            Assert.That(w.Data(w.Menu.TargetOf(0)).Action, Is.EqualTo(AtlasAction.NavDown));
            Assert.That(w.Data(w.Menu.TargetOf(3)).Tag, Is.EqualTo(4), "a row carries its slot, from one");
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Minus)).Action, Is.EqualTo(AtlasAction.NavLeft));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Minus)).Mode, Is.EqualTo(GazeMode.Repeat));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Plus)).Action, Is.EqualTo(AtlasAction.NavRight));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Plus)).Mode, Is.EqualTo(GazeMode.Repeat));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Apply)).Action, Is.EqualTo(AtlasAction.Confirm));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Close)).Action, Is.EqualTo(AtlasAction.Back));
            // The Spaces page: the page row, seven spaces. Every one can take the focus.
            for (int i = 0; i < MenuModel.VisibleRows; i++) Assert.That(w.Enabled(w.Menu.TargetOf(i)), Is.True, "row " + i);

            w.Menu.Close();
            Assert.That(w.Menu.IsOpen, Is.False);
            Assert.That(w.Input.Context, Is.EqualTo(InputContext.Space));
            for (int i = 0; i < MenuLayout.TargetCount; i++) Assert.That(w.Menu.TargetOf(i).IsValid, Is.False);
            Assert.That(w.Input.Core.Targets.Count, Is.EqualTo(1), "only the chip is left");
        }

        [Test]
        public void Open_TheStepTargetsFollowTheFocus_AndOnlyFocusableRowsAreLive()
        {
            var w = new Rig();
            w.Step(quaternion.identity, 2);
            w.Menu.Open();
            w.Model.SetPage(MenuPage.Parameters);
            // The Parameters page: Page, "- render", render.scale, "- hud", hud.visible.
            w.Model.Select(2);
            w.Step(quaternion.identity, 2);

            quaternion frame = quaternion.RotateY(0f);
            int slot = w.Model.Selected - w.Model.Top;
            Assert.That(slot, Is.EqualTo(2));
            float3 minus = math.mul(frame, MenuLayout.PanelCentre + MenuLayout.TargetCentre(MenuLayout.Minus, slot));
            float3 plus = math.mul(frame, MenuLayout.PanelCentre + MenuLayout.TargetCentre(MenuLayout.Plus, slot));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Minus)).Centre, Is.EqualTo(Head + minus));
            Assert.That(w.Data(w.Menu.TargetOf(MenuLayout.Plus)).Centre, Is.EqualTo(Head + plus));

            Assert.That(w.Enabled(w.Menu.TargetOf(0)), Is.True, "the page row");
            Assert.That(w.Enabled(w.Menu.TargetOf(1)), Is.False, "a heading takes no focus");
            Assert.That(w.Enabled(w.Menu.TargetOf(2)), Is.True);
            Assert.That(w.Enabled(w.Menu.TargetOf(3)), Is.False);
            Assert.That(w.Enabled(w.Menu.TargetOf(4)), Is.True);
            Assert.That(w.Enabled(w.Menu.TargetOf(5)), Is.False, "past the last row");
            Assert.That(w.Enabled(w.Menu.TargetOf(MenuLayout.Close)), Is.True);
        }

        [Test]
        public void Open_WhenTheWalkerTurnsTheirBack_ThePanelComesRoundWithItsTargets()
        {
            var w = new Rig();
            w.Step(quaternion.identity, 2);
            w.Menu.Open();
            GazeTargetId before = w.Menu.TargetOf(0);

            // Sixty degrees: still in front. Ninety: brought round.
            w.Step(quaternion.RotateY(math.radians(60f)));
            Assert.That(w.Menu.TargetOf(0), Is.EqualTo(before), "moved, not made again");
            Assert.That(w.View.PanelRotation, Is.EqualTo(quaternion.RotateY(0f)));

            quaternion look = quaternion.RotateY(math.radians(90f));
            w.Step(look);
            float yaw, pitch;
            MenuFrame.TryYawPitch(look, out yaw, out pitch);
            quaternion frame = quaternion.RotateY(yaw);
            Assert.That(w.Menu.TargetOf(0), Is.Not.EqualTo(before), "rectangles with a heading are made again");
            Assert.That(w.View.Panel, Is.EqualTo(Head + math.mul(frame, MenuLayout.PanelCentre)));
            Assert.That(w.View.PanelRotation, Is.EqualTo(frame));
            for (int i = 0; i < MenuLayout.TargetCount; i++)
            {
                GazeTargetData d = w.Data(w.Menu.TargetOf(i));
                Assert.That(d.Centre, Is.EqualTo(Head + math.mul(frame, MenuLayout.PanelCentre + MenuLayout.TargetCentre(i, 0))), "target " + i);
                Assert.That(Angle(d.Rotation, frame), Is.LessThan(1e-3f));
            }
        }

        [Test]
        public void TheChipOpensTheMenu_ARowTakesTheFocus_AndTheCloseChipClosesIt_ByGazeAlone()
        {
            var w = new Rig();
            w.Step(quaternion.identity, 3);
            float3 chip = w.Data(w.Menu.ChipTarget).Centre;
            w.Step(Toward(Head, chip), FramesFor(0.6f));
            Assert.That(w.Menu.IsOpen, Is.False, "not before the dwell");
            w.Step(Toward(Head, chip), FramesFor(0.25f));
            Assert.That(w.Menu.IsOpen, Is.True, "a dwell on the chip opens the menu");

            float3 row = w.Data(w.Menu.TargetOf(3)).Centre;
            w.Step(Toward(Head, row), FramesFor(0.9f));
            Assert.That(w.Model.Selected, Is.EqualTo(w.Model.Top + 3), "a dwell on a row puts the focus on it");

            float3 close = w.Data(w.Menu.TargetOf(MenuLayout.Close)).Centre;
            w.Step(Toward(Head, close), FramesFor(0.9f));
            Assert.That(w.Menu.IsOpen, Is.False, "a dwell on the close chip closes it");
        }

        // ---- a hands session: the menu comes within reach --------------------------------------------------

        static void Close(float3 actual, float3 expected, string what)
        {
            Assert.That(math.distance(actual, expected), Is.LessThan(1e-6f), what + ": " + actual + " against " + expected);
        }

        /// <summary>A rectangle or a sphere, across its narrow side.</summary>
        static float Across(in GazeTargetData d)
        {
            return d.Shape == GazeShape.Rect ? 2f * math.min(d.HalfSize.x, d.HalfSize.y) : 2f * d.Radius;
        }

        [Test]
        public void Near_TheChipStandsAtItsSlot_InTheAnchorsFrame_TimesTheScale()
        {
            Reach reach = Publish();
            var w = new Rig();
            w.Step(quaternion.identity);

            float3 slot = ReachSlots.Centre(MenuLayout.ChipSlot);
            Assert.That(MenuLayout.ChipSlot, Is.EqualTo(ReachSlot.MenuChip));
            float3 centre = reach.Position + math.mul(reach.Rotation, reach.Scale * new float3(slot.x, slot.y, -slot.z));

            GazeTargetData chip = w.Data(w.Menu.ChipTarget);
            Close(chip.Centre, centre, "the chip's target");
            Assert.That(chip.Centre, Is.EqualTo(ReachSlots.World(reach.Position, reach.Rotation, reach.Scale, ReachSlot.MenuChip)));
            Assert.That(chip.Radius, Is.EqualTo(MenuLayout.OpenChipRadius * 0.75f).Within(1e-7f), "its radius times the scale");
            Assert.That(Across(in chip), Is.GreaterThanOrEqualTo(ReachSlots.MinTargetMetres), "no target under 3 cm");
            Assert.That(chip.Action, Is.EqualTo(AtlasAction.ParamMenu));
            Assert.That(chip.Context, Is.EqualTo(InputContext.Space));
            Assert.That(w.Enabled(w.Menu.ChipTarget), Is.True, "a target made again is live again");

            Assert.That(w.View.Near, Is.True, "drawn on the world layer, depth-tested");
            Assert.That(w.View.Scale, Is.EqualTo(0.75f));
            Close(w.View.Chip, centre, "the drawn chip");
            Assert.That(w.View.ChipFrame, Is.EqualTo(reach.Rotation), "in the anchor's plane");

            // The anchor moves: the chip and its target move with it.
            reach.Position += new float3(0.1f, -0.05f, 0.2f);
            GazeTargetId id = w.Menu.ChipTarget;
            w.Step(quaternion.identity);
            Assert.That(w.Menu.ChipTarget, Is.EqualTo(id), "moved, not made again");
            Close(w.Data(id).Centre, ReachSlots.World(reach.Position, reach.Rotation, 0.75f, ReachSlot.MenuChip), "after the anchor moved");
        }

        [Test]
        public void Near_TheOpenPanelStandsOnTheAnchor_WithEveryTargetAtItsPlaceTimesTheScale()
        {
            Reach reach = Publish();
            var w = new Rig();
            w.Step(quaternion.identity, 2);
            w.Menu.Open();

            // At once: no frame in which the targets stand a metre away.
            for (int pass = 0; pass < 2; pass++)
            {
                float3 panel = reach.Position;
                Assert.That(MenuLayout.PanelSlot, Is.EqualTo(ReachSlot.MenuPanel));
                Assert.That(ReachSlots.Centre(ReachSlot.MenuPanel), Is.EqualTo(float3.zero), "the anchor's origin is the panel's centre");
                Assert.That(w.View.Open, Is.True);
                Assert.That(w.View.Near, Is.True);
                Assert.That(w.View.Scale, Is.EqualTo(0.75f), "the panel is 0.525 by 0.375 m");
                Close(w.View.Panel, panel, "the panel, pass " + pass);
                Assert.That(w.View.PanelRotation, Is.EqualTo(reach.Rotation));

                int slot = w.Model.Selected - w.Model.Top;
                for (int i = 0; i < MenuLayout.TargetCount; i++)
                {
                    GazeTargetData d = w.Data(w.Menu.TargetOf(i));
                    Close(d.Centre, panel + math.mul(reach.Rotation, 0.75f * MenuLayout.TargetCentre(i, slot)), "target " + i + ", pass " + pass);
                    float2 half = MenuLayout.TargetHalfSize(i);
                    Assert.That(d.HalfSize.x, Is.EqualTo(math.max(0.75f * half.x, 0.015f)).Within(1e-7f), "target " + i);
                    Assert.That(d.HalfSize.y, Is.EqualTo(math.max(0.75f * half.y, 0.015f)).Within(1e-7f), "target " + i);
                    Assert.That(d.HalfSize, Is.EqualTo(MenuLayout.NearTargetHalfSize(i, 0.75f)));
                    Assert.That(Across(in d), Is.GreaterThanOrEqualTo(ReachSlots.MinTargetMetres), "target " + i + " is at least 3 cm across");
                    Assert.That(Angle(d.Rotation, reach.Rotation), Is.LessThan(1e-3f), "target " + i + " lies in the anchor's plane");
                    Assert.That(d.Context, Is.EqualTo(InputContext.Menu));
                }
                for (int i = 0; i < MenuModel.VisibleRows; i++) Assert.That(w.Enabled(w.Menu.TargetOf(i)), Is.True, "row " + i);

                // The second pass: the anchor has moved and the head too; only the anchor counts.
                reach.Position += new float3(0.05f, 0.02f, -0.1f);
                w.Position = Head + new float3(0.4f, 0f, 0.4f);
                GazeTargetId first = w.Menu.TargetOf(0);
                w.Step(quaternion.identity);
                Assert.That(w.Menu.TargetOf(0), Is.EqualTo(first), "moved, not made again");
            }
        }

        [Test]
        public void Near_WhenTheAnchorTurnsOrTheScaleMoves_TheTargetsAreMadeAgain_AsTheyWere()
        {
            Reach reach = Publish();
            var w = new Rig();
            w.Step(quaternion.identity, 2);
            w.Menu.Open();
            w.Model.SetPage(MenuPage.Parameters);
            w.Model.Select(2);
            w.Step(quaternion.identity, 2);
            GazeTargetId first = w.Menu.TargetOf(0);
            int shows = w.View.Shows;

            // A turn too small to see: the targets stay.
            reach.Rotation = math.mul(quaternion.RotateY(1e-5f), reach.Rotation);
            w.Step(quaternion.identity);
            Assert.That(w.Menu.TargetOf(0), Is.EqualTo(first));

            reach.Rotation = math.mul(quaternion.RotateY(math.radians(10f)), reach.Rotation);
            w.Step(quaternion.identity);
            Assert.That(w.Menu.TargetOf(0), Is.Not.EqualTo(first), "a rectangle cannot be turned in place");
            Assert.That(w.View.Shows, Is.EqualTo(shows), "the text is not rebuilt for a turn");
            Assert.That(w.View.PanelRotation, Is.EqualTo(reach.Rotation));
            for (int i = 0; i < MenuLayout.TargetCount; i++)
            {
                GazeTargetData d = w.Data(w.Menu.TargetOf(i));
                Close(d.Centre, reach.Position + math.mul(reach.Rotation, 0.75f * MenuLayout.TargetCentre(i, 2)), "target " + i);
                Assert.That(Angle(d.Rotation, reach.Rotation), Is.LessThan(1e-3f));
            }
            // New targets start enabled: each row takes its own flag again.
            Assert.That(w.Enabled(w.Menu.TargetOf(0)), Is.True, "the page row");
            Assert.That(w.Enabled(w.Menu.TargetOf(1)), Is.False, "a heading takes no focus");
            Assert.That(w.Enabled(w.Menu.TargetOf(2)), Is.True);
            Assert.That(w.Enabled(w.Menu.TargetOf(3)), Is.False);
            Assert.That(w.Enabled(w.Menu.TargetOf(5)), Is.False, "past the last row");

            reach.Scale = 0.9f;
            GazeTargetId chip = w.Menu.ChipTarget;
            w.Step(quaternion.identity);
            Assert.That(w.View.Scale, Is.EqualTo(0.9f));
            Assert.That(w.Data(w.Menu.TargetOf(0)).HalfSize, Is.EqualTo(MenuLayout.NearTargetHalfSize(0, 0.9f)));
            Assert.That(w.Data(w.Menu.TargetOf(0)).HalfSize.y, Is.EqualTo(0.9f * MenuLayout.RowHalfHeight).Within(1e-7f));
            Assert.That(w.Enabled(w.Menu.TargetOf(1)), Is.False);
            Assert.That(w.Menu.ChipTarget, Is.Not.EqualTo(chip), "a sphere's radius cannot be changed in place");
            Assert.That(w.Data(w.Menu.ChipTarget).Radius, Is.EqualTo(0.9f * MenuLayout.OpenChipRadius).Within(1e-7f));
        }

        [Test]
        public void Near_GazeDwellWorksOnWhatIsDrawn()
        {
            Reach reach = Publish();
            var w = new Rig();
            w.Step(quaternion.identity, 3);
            float3 chip = ReachSlots.World(reach.Position, reach.Rotation, 0.75f, ReachSlot.MenuChip);
            w.Step(Toward(Head, chip), FramesFor(0.6f));
            Assert.That(w.Menu.IsOpen, Is.False, "not before the dwell");
            w.Step(Toward(Head, chip), FramesFor(0.25f));
            Assert.That(w.Menu.IsOpen, Is.True, "a dwell on the near chip opens the menu");

            float3 row = w.Data(w.Menu.TargetOf(3)).Centre;
            Close(row, reach.Position + math.mul(reach.Rotation, 0.75f * MenuLayout.TargetCentre(3, 0)), "row 3");
            w.Step(Toward(Head, row), FramesFor(0.9f));
            Assert.That(w.Model.Selected, Is.EqualTo(w.Model.Top + 3), "a dwell on a near row puts the focus on it");

            float3 close = w.Data(w.Menu.TargetOf(MenuLayout.Close)).Centre;
            w.Step(Toward(Head, close), FramesFor(0.9f));
            Assert.That(w.Menu.IsOpen, Is.False, "a dwell on the near close chip closes it");
            Assert.That(w.View.Near, Is.True, "still within reach");
        }

        /// <summary>A walk through the menu: closed, opened, a row focused, the walker stepping aside.</summary>
        static void Walk(Rig w, Action between)
        {
            w.Step(quaternion.identity, 3);
            between();
            w.Step(quaternion.identity, 2);
            w.Menu.Open();
            w.Step(quaternion.identity, 2);
            w.Model.SetPage(MenuPage.Parameters);
            w.Model.Select(4);
            w.Step(quaternion.identity, 2);
            between();
            w.Position = Head + new float3(0.25f, 0f, -0.1f);
            w.Step(quaternion.RotateY(math.radians(20f)), 3);
        }

        [Test]
        public void WithNearFalse_EverythingIsExactlyWhereItIsWithNoLayoutAtAll()
        {
            var none = new Rig();
            Walk(none, () => { });
            List<string> expected = Snapshot(none);

            Publish(near: false);
            var far = new Rig();
            Walk(far, () => { });
            Assert.That(Snapshot(far), Is.EqualTo(expected), "Near false: positions, sizes and targets to the last number");
            Assert.That(far.View.Reaches, Is.EqualTo(0), "the view was never asked to change");
        }

        [Test]
        public void SwitchingNearOnAndOff_ReturnsEverythingToItsPlace()
        {
            var control = new Rig();
            Walk(control, () => control.Step(quaternion.identity, 8));
            List<string> expected = Snapshot(control);

            Reach reach = Publish(near: false);
            var w = new Rig();
            bool everNear = false;
            Walk(w, () =>
            {
                reach.Near = true;
                w.Step(quaternion.identity, 5);
                everNear |= w.View.Near && w.Data(w.Menu.ChipTarget).Radius < MenuLayout.OpenChipRadius;
                reach.Near = false;
                w.Step(quaternion.identity, 3);
            });
            Assert.That(everNear, Is.True, "the menu was within reach in between");
            Assert.That(Snapshot(w), Is.EqualTo(expected), "back to the last number: centres, radii, half sizes, rotations, flags");
            Assert.That(w.View.Near, Is.False);
            Assert.That(w.View.Scale, Is.EqualTo(1f));
        }

        [Test]
        public void Near_LeavingTheHeadset_PutsTheMenuBackAtOnce()
        {
            Publish();
            var w = new Rig();
            w.Step(quaternion.identity, 2);
            w.Menu.Open();
            w.Step(quaternion.identity);
            Assert.That(w.View.Near, Is.True);

            // The camera in the hand draws the world layer: a near chip left there would lie over the scan picture.
            w.Menu.OnModeChanged(DisplayMode.Hand);
            Assert.That(w.Menu.IsOpen, Is.False);
            Assert.That(w.View.Near, Is.False, "in the frame of the change, not at the next upload");
            Assert.That(w.View.Scale, Is.EqualTo(1f));
            Assert.That(w.Data(w.Menu.ChipTarget).Radius, Is.EqualTo(MenuLayout.OpenChipRadius));
            Assert.That(w.Enabled(w.Menu.ChipTarget), Is.True);

            w.Menu.OnModeChanged(DisplayMode.Headset);
            w.Step(quaternion.identity);
            Assert.That(w.View.Near, Is.True, "and within reach again in the headset");
        }

        [Test]
        public void Near_ALayoutThatIsNotAPose_LeavesTheMenuWhereItIs()
        {
            var none = new Rig();
            Walk(none, () => { });
            List<string> expected = Snapshot(none);

            foreach (string broken in new[] { "scale 0", "scale NaN", "rotation 0", "position NaN" })
            {
                Reach reach = Publish();
                if (broken == "scale 0") reach.Scale = 0f;
                if (broken == "scale NaN") reach.Scale = float.NaN;
                if (broken == "rotation 0") reach.Rotation = new quaternion(0f, 0f, 0f, 0f);
                if (broken == "position NaN") reach.Position = new float3(0f, float.NaN, 0f);
                var w = new Rig();
                Walk(w, () => { });
                Assert.That(Snapshot(w), Is.EqualTo(expected), broken);
            }
        }

        [Test]
        public void Near_AFrameAllocatesNothing_ClosedOpenOrWhileTheAnchorTurns()
        {
            Reach reach = Publish();
            var w = new Rig();
            w.Step(quaternion.identity, 20);   // warm up, closed

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) w.Menu.Upload(in w.Loop.Frame);
            long closed = GC.GetAllocatedBytesForCurrentThread() - before;   // read before the assertion allocates anything
            Assert.That(closed, Is.EqualTo(0L), "bytes allocated by 100 uploads, closed");

            w.Menu.Open();
            quaternion upright = reach.Rotation;
            for (int i = 0; i < 20; i++)
            {
                reach.Rotation = math.mul(quaternion.RotateY(0.01f * i), upright);
                w.Menu.Upload(in w.Loop.Frame);   // warm up, open and turning
            }

            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) w.Menu.Upload(in w.Loop.Frame);
            long open = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(open, Is.EqualTo(0L), "bytes allocated by 100 uploads, open");

            GazeTargetId first = w.Menu.TargetOf(0);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                // The anchor eases to a new place: the rectangles are made again every frame.
                reach.Rotation = math.mul(quaternion.RotateY(0.01f * (i % 20)), upright);
                w.Menu.Upload(in w.Loop.Frame);
            }
            long turning = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(turning, Is.EqualTo(0L), "bytes allocated by 100 uploads while the anchor turns");
            Assert.That(w.Menu.TargetOf(0), Is.Not.EqualTo(first), "the targets were made again");
        }
    }
}
