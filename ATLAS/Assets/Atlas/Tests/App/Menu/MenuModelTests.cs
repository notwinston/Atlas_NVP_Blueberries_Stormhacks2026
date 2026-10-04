using System.Collections.Generic;
using System.Text;
using Atlas.App.Menu;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>The engine-free core of the on-device menu (Master Plan task P1-D13): rows from a registry,
    /// stepping and clamping, focus, the kill-switch page, the space request and the event log.</summary>
    public class MenuModelTests
    {
        sealed class Actions : IMenuActions
        {
            public int Recentres, Rescans;
            public void Recentre() { Recentres++; }
            public void Rescan() { Rescans++; }
        }

        static ParamRegistry Registry()
        {
            var registry = new ParamRegistry();
            registry.DeclareEnum("space.id", "app", new[] { "S0 Home", "S1 Glued", "S2 Curved Walk" }, 0, null, true);
            registry.DeclareBool("demoLock", "app", false);
            registry.DeclareInt("thermal.pinTier", "render", -1, 4, -1, null, true);
            registry.DeclareFloat("render.scale", "render", 0.6f, 0.8f, 0.8f);
            registry.DeclareFloat("render.ipd", "render", 0.055f, 0.075f, 0.063f);
            registry.DeclareInt("glued.copies", "glued", 3, 7, 5, null, true);
            registry.DeclareFloat("glued.side", "glued", 1f, 4f, 2f);
            registry.DeclareBool("link.enabled", "link", true);
            registry.DeclareBool("hud.visible", "hud", false);
            return registry;
        }

        static int RowOf(MenuModel model, string name)
        {
            for (int i = 0; i < model.RowCount; i++)
            {
                MenuRow row = model.Row(i);
                if (row.Kind == MenuRowKind.Param && row.Entry.Name == name) return i;
            }
            return -1;
        }

        static int RowOf(MenuModel model, MenuCommand command)
        {
            for (int i = 0; i < model.RowCount; i++)
                if (model.Row(i).Kind == MenuRowKind.Command && model.Row(i).Command == command) return i;
            return -1;
        }

        [Test]
        public void EveryPage_StartsWithThePageRow_AndSteppingItChangesThePage()
        {
            var model = new MenuModel(Registry(), new FakeHost());
            Assert.That(model.Page, Is.EqualTo(MenuPage.Spaces));
            Assert.That(model.Row(0).Kind, Is.EqualTo(MenuRowKind.Page));
            Assert.That(model.Selected, Is.EqualTo(0));

            model.Step(-1);
            Assert.That(model.Page, Is.EqualTo(MenuPage.Spaces), "no wrap at the first page");
            model.Step(1);
            Assert.That(model.Page, Is.EqualTo(MenuPage.Quality));
            model.Step(1);
            model.Step(1);
            Assert.That(model.Page, Is.EqualTo(MenuPage.Session));
            model.Step(1);
            Assert.That(model.Page, Is.EqualTo(MenuPage.Session), "no wrap at the last page");
            Assert.That(model.Row(0).Kind, Is.EqualTo(MenuRowKind.Page));
            Assert.That(model.Selected, Is.EqualTo(0));
        }

        [Test]
        public void ParametersPage_ListsEveryGroup_TheActiveSpaceFirst()
        {
            ParamRegistry registry = Registry();
            var host = new FakeHost { HasSpace = true, Current = SpaceId.Glued };
            var model = new MenuModel(registry, host);
            model.SetPage(MenuPage.Parameters);

            var headings = new List<string>();
            int parameters = 0;
            for (int i = 0; i < model.RowCount; i++)
            {
                MenuRow row = model.Row(i);
                if (row.Kind == MenuRowKind.Heading) headings.Add(row.Label);
                if (row.Kind == MenuRowKind.Param) parameters++;
            }
            Assert.That(headings, Is.EqualTo(new[] { "glued", "app", "render", "link", "hud" }));
            Assert.That(parameters, Is.EqualTo(registry.Count), "every parameter of the registry has a row");
            Assert.That(model.Row(2).Entry.Name, Is.EqualTo("glued.copies"));

            // The walker goes Home: the list is rebuilt in declaration order.
            host.Current = SpaceId.Home;
            model.Sync();
            Assert.That(model.Row(1).Label, Is.EqualTo("app"));

            // A parameter declared later appears without reopening the menu.
            registry.DeclareFloat("glued.twist", "glued", 0f, 1f, 0f);
            model.Sync();
            Assert.That(RowOf(model, "glued.twist"), Is.GreaterThan(0));
        }

        [Test]
        public void QualityPage_HoldsTheDeclaredKillSwitches_InContractOrder()
        {
            var model = new MenuModel(Registry(), new FakeHost());
            model.SetPage(MenuPage.Quality);

            var names = new List<string>();
            for (int i = 0; i < model.RowCount; i++)
                if (model.Row(i).Kind == MenuRowKind.Param) names.Add(model.Row(i).Entry.Name);
            Assert.That(names, Is.EqualTo(new[] { "space.id", "demoLock", "thermal.pinTier", "render.scale", "glued.copies", "link.enabled" }));
            Assert.That(model.Row(1).Kind, Is.EqualTo(MenuRowKind.Info));
            Assert.That(model.Row(1).Info, Is.EqualTo(MenuInfo.Tier));
            Assert.That(RowOf(model, MenuCommand.ResetSwitches), Is.EqualTo(model.RowCount - 1));
            Assert.That(MenuModel.KillSwitchIds.Length, Is.EqualTo(22), "the 21 switches of lane D's contract, section 1.5, and hands.enabled");
        }

        /// <summary>The kill switch of the tracked hands (documents/ATLAS_Hands_Design.md section 3.7, E11) is on the
        /// Quality page, so it can be reached in the headset with the Station off.</summary>
        [Test]
        public void QualityPage_HoldsTheHandsSwitch_AfterTheOthers()
        {
            Assert.That(MenuModel.KillSwitchIds[MenuModel.KillSwitchIds.Length - 1], Is.EqualTo("hands.enabled"), "added at the end: no switch changes its place");
            Assert.That(MenuModel.KillSwitchIds, Is.Unique);

            ParamRegistry registry = Registry();
            registry.DeclareBool("hands.enabled", "hands", false);
            var model = new MenuModel(registry, new FakeHost());
            model.SetPage(MenuPage.Quality);
            int row = RowOf(model, "hands.enabled");
            Assert.That(row, Is.EqualTo(RowOf(model, MenuCommand.ResetSwitches) - 1), "the last switch, above the reset row");

            model.Select(row);
            model.Confirm();
            Assert.That(registry.GetBool("hands.enabled"), Is.True, "a bool toggles on Confirm");
            // Reset: back to its default, which is off.
            model.Select(RowOf(model, MenuCommand.ResetSwitches));
            model.Confirm();
            Assert.That(registry.GetBool("hands.enabled"), Is.False);
        }

        [Test]
        public void Stepping_MovesByOneTwentiethOfTheRange_AndClampsAtTheEnds()
        {
            ParamRegistry registry = Registry();
            ParamEntry ipd = registry.Find("render.ipd");
            var model = new MenuModel(registry);
            model.SetPage(MenuPage.Parameters);
            model.Select(RowOf(model, "render.ipd"));

            Assert.That(MenuModel.StepSize(ipd), Is.EqualTo(0.001f).Within(1e-6f));
            model.Step(1);
            Assert.That(ipd.Float, Is.EqualTo(0.064f).Within(1e-5f));
            model.Step(-1);
            model.Step(-1);
            Assert.That(ipd.Float, Is.EqualTo(0.062f).Within(1e-5f));

            for (int i = 0; i < 40; i++) model.Step(1);
            Assert.That(ipd.Float, Is.EqualTo(ipd.Max), "the registry's clamp holds at the top");
            for (int i = 0; i < 40; i++) model.Step(-1);
            Assert.That(ipd.Float, Is.EqualTo(ipd.Min));

            // A bool toggles either way and on Confirm.
            ParamEntry hud = registry.Find("hud.visible");
            model.Select(RowOf(model, "hud.visible"));
            model.Step(1);
            Assert.That(hud.Bool, Is.True);
            model.Step(1);
            Assert.That(hud.Bool, Is.False);
            model.Confirm();
            Assert.That(hud.Bool, Is.True);
        }

        [Test]
        public void IntAndEnum_StepByOne_WithoutWrapping()
        {
            ParamRegistry registry = Registry();
            ParamEntry space = registry.Find("space.id");
            Assert.That(MenuModel.Stepped(space, 2f, 1), Is.EqualTo(2f));
            Assert.That(MenuModel.Stepped(space, 0f, -1), Is.EqualTo(0f));
            Assert.That(MenuModel.Stepped(space, 0f, 1), Is.EqualTo(1f));
            ParamEntry tier = registry.Find("thermal.pinTier");
            Assert.That(MenuModel.Stepped(tier, -1f, -1), Is.EqualTo(-1f));
            Assert.That(MenuModel.Stepped(tier, 3f, 1), Is.EqualTo(4f));
            Assert.That(MenuModel.Stepped(tier, 4f, 5), Is.EqualTo(4f));
        }

        [Test]
        public void AnOnReleaseParameter_WaitsForConfirm()
        {
            ParamRegistry registry = Registry();
            ParamEntry copies = registry.Find("glued.copies");
            var model = new MenuModel(registry);
            model.SetPage(MenuPage.Quality);
            model.Select(RowOf(model, "glued.copies"));

            model.Step(1);
            model.Step(1);
            Assert.That(copies.Int, Is.EqualTo(5), "nothing is applied before Confirm");
            Assert.That(model.HasPending, Is.True);
            Assert.That(model.PendingValue, Is.EqualTo(7f));
            model.Step(1);
            Assert.That(model.PendingValue, Is.EqualTo(7f), "the pending value is clamped too");

            var text = new StringBuilder();
            MenuText.BuildValues(text, model);
            Assert.That(text.ToString(), Does.Contain("7 (apply)"));

            model.Confirm();
            Assert.That(copies.Int, Is.EqualTo(7));
            Assert.That(model.HasPending, Is.False);

            // Moving the focus away drops a value that was not confirmed.
            model.Step(-1);
            Assert.That(model.HasPending, Is.True);
            model.Move(-1);
            Assert.That(model.HasPending, Is.False);
            Assert.That(copies.Int, Is.EqualTo(7));
        }

        [Test]
        public void Focus_SkipsHeadingsAndStatusLines_StopsAtTheEnds_AndScrolls()
        {
            ParamRegistry registry = Registry();
            for (int i = 0; i < 12; i++) registry.DeclareFloat("portal.p" + i, "portal", 0f, 1f, 0f);
            var model = new MenuModel(registry);
            model.SetPage(MenuPage.Parameters);

            Assert.That(model.Row(1).Kind, Is.EqualTo(MenuRowKind.Heading));
            model.Move(1);
            Assert.That(model.Selected, Is.EqualTo(2), "the heading is stepped over");
            model.Select(1);
            Assert.That(model.Selected, Is.EqualTo(2), "a dwell on a heading does nothing");
            model.Move(-1);
            Assert.That(model.Selected, Is.EqualTo(0));
            model.Move(-1);
            Assert.That(model.Selected, Is.EqualTo(0), "clamped at the top");

            model.Move(1000);
            Assert.That(model.Selected, Is.EqualTo(model.RowCount - 1), "clamped at the bottom");
            Assert.That(model.Top, Is.EqualTo(model.RowCount - MenuModel.VisibleRows));
            for (int i = 0; i < model.RowCount; i++)
            {
                model.Move(-1);
                Assert.That(model.Selected, Is.GreaterThanOrEqualTo(model.Top));
                Assert.That(model.Selected, Is.LessThan(model.Top + MenuModel.VisibleRows));
                Assert.That(model.Row(model.Selected).Focusable, Is.True);
            }
            Assert.That(model.Top, Is.EqualTo(0));

            int revision = model.Revision;
            model.Sync();
            Assert.That(model.Revision, Is.EqualTo(revision), "a quiet frame leaves the view alone");
            registry.Set("glued.side", 3f, ParamSource.Remote);
            model.Sync();
            Assert.That(model.Revision, Is.GreaterThan(revision), "a value changed by the Station is redrawn");
        }

        [Test]
        public void SwitchingSpace_RaisesTheRequest_AndAsksTheHost()
        {
            var host = new FakeHost { HasSpace = true, Current = SpaceId.Home };
            var model = new MenuModel(Registry(), host);
            var requested = new List<SpaceId>();
            model.SpaceRequested += requested.Add;

            // Seven available spaces, then Return to Home.
            Assert.That(model.RowCount, Is.EqualTo(1 + SpaceIds.Count + 1));
            Assert.That(model.IsCurrent(SpaceId.Home), Is.True);
            model.Select(2);
            Assert.That(model.Row(2).Space, Is.EqualTo(SpaceId.Glued));
            model.Step(-1);
            Assert.That(host.Calls, Is.Empty, "minus does not run a row");
            model.Step(1);
            Assert.That(host.Calls, Is.Empty, "a space is selected, then entered with Apply");
            Assert.That(model.ApplyReady, Is.True);
            model.Confirm();
            Assert.That(requested, Is.EqualTo(new[] { SpaceId.Glued }));
            Assert.That(host.Calls, Is.EqualTo(new[] { "switch S1" }));
            Assert.That(model.CloseRequested, Is.True, "the menu gets out of the way of the fade");

            model.NoteOpened();
            Assert.That(model.CloseRequested, Is.False);
            model.Select(1);
            Assert.That(model.ApplyReady, Is.False, "Apply has nothing to do on the space the walker is in");
            model.Select(RowOf(model, MenuCommand.ReturnHome));
            model.Step(1);
            Assert.That(host.Calls[host.Calls.Count - 1], Is.EqualTo("home"));

            // A refused switch leaves the menu open.
            model.NoteOpened();
            model.Select(2);
            host.Accept = false;
            model.Confirm();
            Assert.That(host.Calls[host.Calls.Count - 1], Is.EqualTo("switch S1"));
            Assert.That(model.CloseRequested, Is.False);
        }

        [Test]
        public void SessionPage_RunsRecentreAndRescan_AndShowsTheStatus()
        {
            var actions = new Actions();
            var model = new MenuModel(Registry(), new FakeHost(), actions);
            model.SetPage(MenuPage.Session);
            model.Select(RowOf(model, MenuCommand.Recentre));
            model.Confirm();
            Assert.That(actions.Recentres, Is.EqualTo(1));
            Assert.That(model.CloseRequested, Is.False);
            model.Select(RowOf(model, MenuCommand.Rescan));
            model.Confirm();
            Assert.That(actions.Rescans, Is.EqualTo(1));
            Assert.That(model.CloseRequested, Is.True);

            var status = new MenuStatus
            {
                Link = "connected", RttMs = 3, StationHost = "192.168.0.10", StationPort = 7777, Session = "s-1",
                Version = "0.1.0", Room = "sample_3x3", Tier = 1, Thermal = 1
            };
            int revision = model.Revision;
            model.SetStatus(in status);
            Assert.That(model.Revision, Is.GreaterThan(revision));
            revision = model.Revision;
            model.SetStatus(in status);
            Assert.That(model.Revision, Is.EqualTo(revision), "the same figures do not redraw the menu");

            model.Select(0);
            var text = new StringBuilder();
            MenuText.BuildValues(text, model);
            string values = text.ToString();
            Assert.That(values, Does.Contain("connected  3 ms"));
            Assert.That(values, Does.Contain("192.168.0.10:7777"));
            Assert.That(values, Does.Contain("s-1"));
            Assert.That(values, Does.Contain("sample_3x3"));
            MenuText.BuildNames(text, model);
            Assert.That(text.ToString().Split('\n').Length, Is.EqualTo(MenuModel.VisibleRows));
        }

        [Test]
        public void Text_ShowsNamesValuesAndTheDefaultMark()
        {
            ParamRegistry registry = Registry();
            registry.SetGroupActive("glued", false);
            var model = new MenuModel(registry);
            model.SetPage(MenuPage.Parameters);
            registry.Set("render.scale", 0.7f);
            registry.Set("space.id", 1f);
            model.Sync();

            var names = new StringBuilder();
            var values = new StringBuilder();
            var title = new StringBuilder();
            MenuText.BuildNames(names, model);
            MenuText.BuildValues(values, model);
            MenuText.BuildTitle(title, model);
            string[] left = names.ToString().Split('\n');
            string[] right = values.ToString().Split('\n');
            Assert.That(left.Length, Is.EqualTo(MenuModel.VisibleRows));
            Assert.That(right.Length, Is.EqualTo(MenuModel.VisibleRows));
            Assert.That(left[0], Is.EqualTo("Page"));
            Assert.That(right[0], Is.EqualTo("Parameters  3/4"));
            Assert.That(left[1], Is.EqualTo("- app"));
            Assert.That(left[2], Is.EqualTo("space.id *"));
            Assert.That(right[2], Is.EqualTo("S1 Glued"));
            Assert.That(right[3], Is.EqualTo("off"));
            Assert.That(left[4], Is.EqualTo("- render"));
            Assert.That(right[5], Is.EqualTo("-1"));
            Assert.That(left[6], Is.EqualTo("render.scale *"));
            Assert.That(right[6], Is.EqualTo("0.700"));
            Assert.That(title.ToString(), Is.EqualTo("ATLAS  Parameters   1-8 of " + model.RowCount));

            model.Move(1000);
            MenuText.BuildNames(names, model);
            Assert.That(names.ToString(), Does.Contain("- S1 Glued (inactive)"));
        }

        [Test]
        public void AChange_IsLoggedAsAnEvent_WithTheMenuAsItsSource()
        {
            ParamRegistry registry = Registry();
            var events = new FakeEvents();
            var model = new MenuModel(registry) { Events = events };
            model.NoteOpened();
            model.SetPage(MenuPage.Quality);
            model.Select(RowOf(model, "render.scale"));
            model.Step(-1);

            Assert.That(registry.GetFloat("render.scale"), Is.EqualTo(0.79f).Within(1e-5f));
            Assert.That(events.Has("menu state=open"), Is.True);
            Assert.That(events.Has("param.set {\"name\":\"render.scale\",\"value\":0.79"), Is.True);
            Assert.That(events.Lines[events.Lines.Count - 1], Does.EndWith("\"src\":\"menu\"}"));

            // A step that the clamp swallows changes nothing and logs nothing.
            int lines = events.Lines.Count;
            model.Step(1);
            model.Step(1);
            Assert.That(events.Lines.Count, Is.EqualTo(lines + 1));

            // Reset: every switch but space.id goes back to its default, each change logged.
            registry.Set("space.id", 2f);
            registry.Set("demoLock", 1f);
            lines = events.Lines.Count;
            model.Select(RowOf(model, MenuCommand.ResetSwitches));
            model.Confirm();
            Assert.That(registry.GetBool("demoLock"), Is.False);
            Assert.That(registry.GetInt("space.id"), Is.EqualTo(2));
            Assert.That(events.Lines.Count, Is.EqualTo(lines + 1));

            model.NoteClosed();
            Assert.That(events.Has("menu state=close"), Is.True);
        }

        /// <summary>Gate G6: with the Station unreachable, a kill switch changed from the menu is in the phone's log.</summary>
        [Test]
        public void G6_WithNoStation_AKillSwitchChangedFromTheMenu_IsInThePhonesEventLog()
        {
            ParamRegistry registry = Registry();
            var clock = new ManualClock(1000000);
            var client = new LinkClient(new LoopbackNetwork(clock).CreateEndpoint(), clock, LinkRole.Phone, registry);
            var log = new EventLog(clock, null);
            var logged = new List<EventMessage>();
            log.Logged += logged.Add;
            var pump = new LinkPump(client, log);

            var model = new MenuModel(registry) { Events = log };
            model.SetPage(MenuPage.Quality);
            model.Select(RowOf(model, "link.enabled"));
            model.Confirm();
            Assert.That(registry.GetBool("link.enabled"), Is.False);
            Assert.That(client.IsReady, Is.False, "no Station was ever reached");

            EventMessage set = logged.Find(e => e.Ev == MenuModel.EventParamSet);
            Assert.That(set, Is.Not.Null);
            Assert.That(set.Data["name"].AsString(), Is.EqualTo("link.enabled"));
            Assert.That(set.Data["value"].AsFloat(), Is.EqualTo(0f));
            Assert.That(set.Data["src"].AsString(), Is.EqualTo("menu"));
            Assert.That(log.Queued, Is.GreaterThan(0), "the event waits for the Station and is sent when it connects");

            // The link driver writes the applied value as well, within a second.
            clock.AdvanceMs(1100.0);
            pump.Tick();
            Assert.That(logged.Exists(e => e.Ev == LinkEvents.Param && e.Data["name"].AsString() == "link.enabled"), Is.True);
        }

        [Test]
        public void TheMenuFrame_OpensWhereTheChipStood_NotWhereTheChipIs()
        {
            var frame = new MenuFrame();
            frame.Snap(0f);
            // The walker looks down and left at the chip to open the menu: the panel stays ahead.
            frame.Open(math.radians(-30f), true);
            Assert.That(frame.Yaw, Is.EqualTo(0f));
            // Opened again and again from the chip, it never walks off to the left.
            for (int i = 0; i < 5; i++) frame.Open(frame.Yaw + math.radians(-30f), true);
            Assert.That(frame.Yaw, Is.EqualTo(0f));
            // Turned right round, or with no heading yet, it comes up where the walker looks.
            frame.Open(math.radians(120f), true);
            Assert.That(frame.Yaw, Is.EqualTo(math.radians(120f)).Within(1e-5f));
            var fresh = new MenuFrame();
            fresh.Open(math.radians(-30f), false);
            Assert.That(fresh.Yaw, Is.EqualTo(math.radians(-30f)).Within(1e-5f));
        }

        [Test]
        public void TheMenuFrame_TrailsTheWalker_AndHoldsStillWhileTheyReachForTheChip()
        {
            var frame = new MenuFrame();
            frame.Snap(0f);

            // Looking level and turning: the heading catches up.
            float yaw = math.radians(30f);
            for (int i = 0; i < 60; i++) frame.Follow(yaw, 0f, 1f / 60f, false);
            Assert.That(frame.Yaw, Is.EqualTo(yaw).Within(1e-4f));

            // Looking down, or with the gaze on the chip, it holds: the chip can be reached.
            float before = frame.Yaw;
            frame.Follow(0f, math.radians(-20f), 0.5f, false);
            Assert.That(frame.Yaw, Is.EqualTo(before));
            frame.Follow(0f, 0f, 0.5f, true);
            Assert.That(frame.Yaw, Is.EqualTo(before));

            // But never by more than the leash, also across the wrap at 180 degrees.
            frame.Snap(math.radians(170f));
            frame.Follow(math.radians(-100f), math.radians(-30f), 0.016f, false);
            Assert.That(math.abs(MenuFrame.Delta(frame.Yaw, math.radians(-100f))), Is.EqualTo(MenuFrame.ChipLeash).Within(1e-4f));

            // The open panel stays put until the walker has turned away from it.
            frame.Snap(0f);
            Assert.That(frame.Recentre(math.radians(60f)), Is.False);
            Assert.That(frame.Yaw, Is.EqualTo(0f));
            Assert.That(frame.Recentre(math.radians(90f)), Is.True);
            Assert.That(frame.Yaw, Is.EqualTo(math.radians(90f)).Within(1e-6f));

            float headYaw, pitch;
            Assert.That(MenuFrame.TryYawPitch(quaternion.RotateY(math.radians(40f)), out headYaw, out pitch), Is.True);
            Assert.That(headYaw, Is.EqualTo(math.radians(40f)).Within(1e-4f));
            Assert.That(pitch, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(MenuFrame.TryYawPitch(quaternion.RotateX(math.radians(88f)), out headYaw, out pitch), Is.False);
            Assert.That(pitch, Is.EqualTo(math.radians(-88f)).Within(1e-3f), "rotating about +X looks down");
        }

        [Test]
        public void Layout_KeepsEveryTargetOnThePanel_AndTheStepTargetsOnTheFocusedRow()
        {
            for (int target = 0; target < MenuLayout.TargetCount; target++)
            {
                float3 centre = MenuLayout.TargetCentre(target, 7);
                float2 half = MenuLayout.TargetHalfSize(target);
                Assert.That(math.abs(centre.x) + half.x, Is.LessThanOrEqualTo(MenuLayout.Width * 0.5f + 1e-5f), "target " + target);
                Assert.That(math.abs(centre.y) + half.y, Is.LessThanOrEqualTo(MenuLayout.Height * 0.5f + 1e-5f), "target " + target);
            }
            Assert.That(MenuLayout.TargetCentre(MenuLayout.Plus, 3).y, Is.EqualTo(MenuLayout.RowY(3)));
            Assert.That(MenuLayout.TargetCentre(MenuLayout.Minus, 3).z, Is.LessThan(0f), "in front of the row, so the gaze meets it first");
            // The last row clears the chips under the list.
            Assert.That(MenuLayout.RowY(MenuModel.VisibleRows - 1) - MenuLayout.RowHalfHeight,
                Is.GreaterThan(MenuLayout.FooterY + MenuLayout.ChipHalfHeight));
            Assert.That(MenuLayout.TargetCount, Is.LessThanOrEqualTo(16), "a quarter of the 64 gaze targets at most");
        }
    }
}
