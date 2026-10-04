using System.Collections.Generic;
using Atlas.Input;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C07: the twins table of Master Plan section 10.1.3, the lever's gestures and the dwell.</summary>
    public class ActionMapTests
    {
        const float Dt = 1f / 60f;

        // ---- the table ---------------------------------------------------------------------------------------

        [Test]
        public void TheTableIsTheOneOfThePlan()
        {
            PgTwin[] t = ActionMap.Table;
            Assert.That(t.Length, Is.EqualTo(9));
            string[] names = { "Confirm / next beat", "Dial q", "Mode 1 / Mode 2", "Exhibit menu, pick", "Gluing T2 / Klein / T3",
                               "Slice w", "Rotate in a w-plane", "Shadow", "Return Home" };
            string[] pads = { "A", "D-pad left/right", "Y", "Start, stick + A", "bumpers", "bumpers", "triggers (or marker cube pitch)", "X", "B held" };
            string[] gazes = { "prompt chip", "floor dial, four detents", "mode chip", "exhibit sign", "gluing sign",
                               "ends of the w-ruler", "floor dial, five detents", "look down past -35 deg", "home beacon" };
            for (int i = 0; i < t.Length; i++)
            {
                Assert.That(t[i].Name, Is.EqualTo(names[i]));
                Assert.That(t[i].Gamepad, Is.EqualTo(pads[i]), names[i]);
                Assert.That(t[i].GazeDwell, Is.EqualTo(gazes[i]), names[i]);
            }
        }

        [Test]
        public void EveryActionHasAGamepadAndAGazeTwinAndEveryOneButTheShadowAHeadsetLever()
        {
            foreach (PgTwin row in ActionMap.Table)
            {
                Assert.That(row.Gamepad, Is.Not.Empty, row.Name);
                Assert.That(row.GazeDwell, Is.Not.Empty, row.Name);
                if (row.Action == PgAction.Shadow) Assert.That(row.Lever, Is.Null, "the shadow has a gaze twin only");
                else Assert.That(row.Lever, Is.Not.Empty, row.Name);
            }
        }

        [Test]
        public void TheInputServiceCarriesEveryRowOnBothRoutes()
        {
            foreach (PgTwin row in ActionMap.Table)
            {
                if (row.HasAxis)
                {
                    Assert.That(InputTwins.HasGamepadRoute(row.Axis), Is.True, row.Name + " gamepad axis");
                    Assert.That(InputTwins.HasGazeRoute(row.Axis), Is.True, row.Name + " gaze axis");
                    continue;
                }
                foreach (AtlasAction a in new[] { row.Primary, row.Secondary })
                {
                    if (a == AtlasAction.None) continue;
                    Assert.That(InputTwins.HasGamepadRoute(a), Is.True, row.Name + " " + a + " gamepad");
                    Assert.That(InputTwins.HasGazeRoute(a), Is.True, row.Name + " " + a + " gaze");
                }
            }
        }

        [Test]
        public void EverySlotMatchesARowAndFiresWhatTheServiceRoutes()
        {
            var seen = new HashSet<PgAction>();
            for (int i = 1; i < PgSlots.Count; i++)
            {
                PgSlotSpec s = PgSlots.Of((PgSlot)i);
                Assert.That(s.Slot, Is.EqualTo((PgSlot)i));
                seen.Add(s.Twin);
                if (s.IsAxis) Assert.That(InputTwins.HasGazeRoute(s.Axis), Is.True, s.Label);
                else Assert.That(InputTwins.HasGazeRoute(s.Action), Is.True, s.Label);
            }
            foreach (PgTwin row in ActionMap.Table)
            {
                PgAction a = row.Action == PgAction.DialStep ? PgAction.DialTo : row.Action == PgAction.ExhibitOpen ? PgAction.ExhibitOpen : row.Action;
                Assert.That(seen, Does.Contain(a), "a gaze slot for " + row.Name);
            }
            Assert.That(PgSlots.DialDetent(3), Is.EqualTo(PgSlot.DialDetent3));
            Assert.That(PgSlots.DialDetent(6), Is.EqualTo(PgSlot.DialDetent6));
            Assert.That(PgSlots.DialDetent(7), Is.EqualTo(PgSlot.None));
        }

        // ---- the lever ---------------------------------------------------------------------------------------

        static List<LeverGesture> Run(LeverGestures g, params (bool down, float seconds)[] script)
        {
            var found = new List<LeverGesture>();
            foreach (var step in script)
            {
                int frames = Mathf.Max(1, Mathf.RoundToInt(step.seconds / Dt));
                for (int i = 0; i < frames; i++)
                {
                    LeverGesture r = g.Update(step.down, Dt);
                    if (r != LeverGesture.None) found.Add(r);
                }
            }
            return found;
        }

        [Test]
        public void ASingleClickIsImmediateWhenNothingLongerIsBound()
        {
            var g = new LeverGestures { MaxClicks = 1 };
            var r = Run(g, (true, 0.1f), (false, 0.05f));
            Assert.That(r, Is.EqualTo(new[] { LeverGesture.Click }));
        }

        [Test]
        public void ADoubleClickWaitsForItsSecondTouch()
        {
            var g = new LeverGestures { MaxClicks = 2 };
            Assert.That(Run(g, (true, 0.08f), (false, 0.1f), (true, 0.08f), (false, 0.1f)), Is.EqualTo(new[] { LeverGesture.DoubleClick }));
            Assert.That(Run(g, (true, 0.08f), (false, 0.5f)), Is.EqualTo(new[] { LeverGesture.Click }), "one touch, then the window runs out");
        }

        [Test]
        public void ATripleClickIsOneGestureNotThreeClicks()
        {
            var g = new LeverGestures { MaxClicks = 3 };
            var r = Run(g, (true, 0.07f), (false, 0.1f), (true, 0.07f), (false, 0.1f), (true, 0.07f), (false, 0.1f));
            Assert.That(r, Is.EqualTo(new[] { LeverGesture.TripleClick }));
        }

        [Test]
        public void TwoClicksTooFarApartAreTwoClicks()
        {
            var g = new LeverGestures { MaxClicks = 2 };
            var r = Run(g, (true, 0.07f), (false, 0.6f), (true, 0.07f), (false, 0.6f));
            Assert.That(r, Is.EqualTo(new[] { LeverGesture.Click, LeverGesture.Click }));
        }

        [Test]
        public void ALongPressFiresWhileDownAndItsReleaseIsSilent()
        {
            var g = new LeverGestures { MaxClicks = 1, LongPressBound = true };
            var r = Run(g, (true, 0.9f), (false, 0.5f));
            Assert.That(r, Is.EqualTo(new[] { LeverGesture.LongPress }));
        }

        [Test]
        public void AHoldBeginsAndEndsAndReplacesTheLongPress()
        {
            var g = new LeverGestures { MaxClicks = 1, LongPressBound = true, HoldBound = true };
            var r = Run(g, (true, 1.2f), (false, 0.3f));
            Assert.That(r, Is.EqualTo(new[] { LeverGesture.HoldBegin, LeverGesture.HoldEnd }));
        }

        // ---- the map -----------------------------------------------------------------------------------------

        static List<PgActionEvent> Collect(ActionMap map)
        {
            var list = new List<PgActionEvent>();
            map.Fired += e => list.Add(e);
            return list;
        }

        static void Touch(ActionMap map, float seconds)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < frames; i++) map.Tick(true, Dt);
        }

        static void Release(ActionMap map, float seconds)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < frames; i++) map.Tick(false, Dt);
        }

        static void Click(ActionMap map, float settle = 0.4f)
        {
            Touch(map, 0.08f);
            Release(map, settle);
        }

        [Test]
        public void ConfirmComesFromAGamepadADwellAndAClick()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gaze, 0);
            Click(map);
            Assert.That(e.Count, Is.EqualTo(3));
            Assert.That(e.ConvertAll(x => x.Action), Is.All.EqualTo(PgAction.Confirm));
            Assert.That(e.ConvertAll(x => x.Source), Is.EqualTo(new[] { InputSource.Gamepad, InputSource.Gaze, InputSource.Lever }));
        }

        [Test]
        public void TheInputServicesOwnLeverFiringsAreDropped()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Lever, 0);
            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Lever, 0);
            Assert.That(e, Is.Empty);
        }

        [Test]
        public void TheLeverCyclesTheDial3To4To5To6To3()
        {
            var map = new ActionMap { ClickRole = LeverClickRole.Dial, Q = 3 };
            var e = Collect(map);
            var sequence = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                Click(map);
                PgActionEvent last = e[e.Count - 1];
                Assert.That(last.Action, Is.EqualTo(PgAction.DialTo));
                Assert.That(last.Source, Is.EqualTo(InputSource.Lever));
                sequence.Add(last.Value);
                map.Q = last.Value;
            }
            Assert.That(sequence, Is.EqualTo(new[] { 4, 5, 6, 3, 4 }));
        }

        [Test]
        public void TheDpadStepsTheDialAndADwellOnADetentNamesIt()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.DialLeft, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.DialRight, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.DialRight, InputSource.Gaze, 6);
            map.OnActionFired(AtlasAction.DialLeft, InputSource.Gaze, 3);
            Assert.That(e.ConvertAll(x => (x.Action, x.Value)),
                        Is.EqualTo(new[] { (PgAction.DialStep, -1), (PgAction.DialStep, 1), (PgAction.DialTo, 6), (PgAction.DialTo, 3) }));
        }

        [Test]
        public void ModeIsYAModeChipAndADoubleClick()
        {
            var map = new ActionMap { DoubleRole = LeverDoubleRole.Mode };
            var e = Collect(map);
            map.OnActionFired(AtlasAction.Primary, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.Primary, InputSource.Gaze, 0);
            Touch(map, 0.07f); Release(map, 0.1f); Touch(map, 0.07f); Release(map, 0.5f);
            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new[] { PgAction.Mode, PgAction.Mode, PgAction.Mode }));
            Assert.That(e[2].Source, Is.EqualTo(InputSource.Lever));
        }

        [Test]
        public void ReturnHomeIsBHeldTheBeaconAndATripleClick()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.ReturnHome, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.ReturnHome, InputSource.Gaze, 0);
            for (int i = 0; i < 3; i++) { Touch(map, 0.07f); Release(map, 0.1f); }
            Release(map, 0.4f);
            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new[] { PgAction.ReturnHome, PgAction.ReturnHome, PgAction.ReturnHome }));
            Assert.That(e[2].Source, Is.EqualTo(InputSource.Lever));
        }

        [Test]
        public void TheMenuOpensOnStartASignAndALongPress()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gaze, 0);
            Touch(map, 0.9f); Release(map, 0.5f);
            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new[] { PgAction.ExhibitOpen, PgAction.ExhibitOpen, PgAction.ExhibitOpen }));
            Assert.That(e[2].Source, Is.EqualTo(InputSource.Lever));
        }

        [Test]
        public void OneLongTouchMakesOneGestureEvenWhenTheContextChangesUnderIt()
        {
            var host = new FakeHost();
            var map = new ActionMap();
            var menu = new ExhibitMenu(host, null, map);
            map.Fired += menu.OnAction;
            var e = Collect(map);
            Touch(map, 1.5f);
            Assert.That(menu.IsOpen, Is.True, "the long press opened it");
            Assert.That(host.Switches, Is.Empty, "and the same touch picked nothing");
            Release(map, 0.5f);
            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new[] { PgAction.ExhibitOpen }), "the release is silent");
            Assert.That(menu.IsOpen, Is.True);

            // The other way round: a pick closes the menu; holding on must not reopen it.
            Click(map);
            Touch(map, 1.5f);
            Release(map, 0.5f);
            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new[] { PgAction.ExhibitOpen, PgAction.ExhibitStep, PgAction.ExhibitPick }));
            Assert.That(menu.IsOpen, Is.False);
        }

        [Test]
        public void InTheMenuAClickStepsALongPressPicksAStickAndAPickAndADwellPicksASign()
        {
            var map = new ActionMap { MenuOpen = true };
            var e = Collect(map);
            Click(map);
            Touch(map, 0.9f); Release(map, 0.5f);
            map.OnActionFired(AtlasAction.NavDown, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.NavUp, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gaze, 2);
            Assert.That(e.ConvertAll(x => (x.Action, x.Source, x.Value)), Is.EqualTo(new[]
            {
                (PgAction.ExhibitStep, InputSource.Lever, 1),
                (PgAction.ExhibitPick, InputSource.Lever, -1),
                (PgAction.ExhibitStep, InputSource.Gamepad, 1),
                (PgAction.ExhibitStep, InputSource.Gamepad, -1),
                (PgAction.ExhibitPick, InputSource.Gamepad, -1),
                (PgAction.ExhibitPick, InputSource.Gaze, 2),
            }));
        }

        [Test]
        public void GluingIsTheBumpersASignAndAClick()
        {
            var map = new ActionMap { ClickRole = LeverClickRole.Gluing };
            var e = Collect(map);
            map.OnActionFired(AtlasAction.VariantNext, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.VariantPrev, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.VariantNext, InputSource.Gaze, 0);
            Click(map);
            Assert.That(e.ConvertAll(x => (x.Action, x.Source, x.Value)), Is.EqualTo(new[]
            {
                (PgAction.Gluing, InputSource.Gamepad, 1), (PgAction.Gluing, InputSource.Gamepad, -1),
                (PgAction.Gluing, InputSource.Gaze, 0), (PgAction.Gluing, InputSource.Lever, 0),
            }));
        }

        [Test]
        public void RotationStepsFifteenDegreesOnADoubleClickAndTheShadowHasNoLever()
        {
            var map = new ActionMap { DoubleRole = LeverDoubleRole.RotateStep };
            var e = Collect(map);
            Touch(map, 0.07f); Release(map, 0.1f); Touch(map, 0.07f); Release(map, 0.5f);
            Assert.That(e.Count, Is.EqualTo(1));
            Assert.That((e[0].Action, e[0].Value), Is.EqualTo((PgAction.RotateStep, 15)));
            map.OnActionFired(AtlasAction.Secondary, InputSource.Gaze, 0);
            Assert.That(e[1].Action, Is.EqualTo(PgAction.Shadow));
            Assert.That(ActionMap.RotateStepDegrees, Is.EqualTo(15));
        }

        [Test]
        public void AHeldLeverSweepsWAndEachHoldGoesTheOtherWay()
        {
            var map = new ActionMap { HoldSweeps = true };
            Touch(map, 0.5f);
            float first = map.LeverSweep;
            Assert.That(Mathf.Abs(first), Is.EqualTo(1f));
            Release(map, 0.1f);
            Assert.That(map.LeverSweep, Is.EqualTo(0f));
            Touch(map, 0.5f);
            Assert.That(map.LeverSweep, Is.EqualTo(-first));
        }

        [Test]
        public void ADroppedActionFiresFromNoTwin()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.SetLive(PgAction.Gluing, false);
            map.OnActionFired(AtlasAction.VariantNext, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.VariantNext, InputSource.Gaze, 0);
            Assert.That(e, Is.Empty);
            map.Reset();
            map.OnActionFired(AtlasAction.VariantNext, InputSource.Gamepad, 0);
            Assert.That(e.Count, Is.EqualTo(1));
        }

        [Test]
        public void KeyboardIsATwinToo()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Keyboard, 0);
            Assert.That(e.Count, Is.EqualTo(1));
        }

        // ---- the dwell, with the real core ---------------------------------------------------------------------

        [Test]
        public void TheDwellIsSevenHundredMilliseconds()
        {
            var input = new CoreInput();
            DwellReticle.Apply(input);
            Assert.That(input.DwellSeconds, Is.EqualTo(0.7f).Within(1e-6));
            Assert.That(ActionMap.DwellSeconds, Is.EqualTo(0.7f));
        }

        [Test]
        public void AGazeOnTheModeChipFiresModeAtSevenHundredMilliseconds()
        {
            var input = new CoreInput();
            DwellReticle.Apply(input);
            var targets = new PgGazeTargets(input);
            var map = new ActionMap();
            var e = Collect(map);
            input.ActionFired += map.OnActionFired;

            // A chip 1.5 m ahead along +Z, at eye height.
            Assert.That(targets.Bind(PgSlot.ModeChip, GazeTarget.World(AtlasAction.None, new Vector3(0f, 1.6f, 1.5f), 0.1f)), Is.True);
            var reticle = new DwellReticle(targets);

            float t = 0f;
            while (t < 0.65f) { input.Frame(Dt); t += Dt; reticle.Update(input.Gaze); }
            Assert.That(e, Is.Empty, "not yet at 650 ms");
            Assert.That(reticle.Slot, Is.EqualTo(PgSlot.ModeChip));
            Assert.That(reticle.Progress, Is.InRange(0.8f, 1f));
            while (t < 0.75f) { input.Frame(Dt); t += Dt; }
            Assert.That(e.Count, Is.EqualTo(1), "fired by 750 ms");
            Assert.That(e[0].Action, Is.EqualTo(PgAction.Mode));
            Assert.That(e[0].Source, Is.EqualTo(InputSource.Gaze));
        }

        [Test]
        public void ADwellOnADialDetentSetsQ()
        {
            var input = new CoreInput();
            var targets = new PgGazeTargets(input);
            var map = new ActionMap();
            var e = Collect(map);
            input.ActionFired += map.OnActionFired;
            targets.Bind(PgSlot.DialDetent6, GazeTarget.World(AtlasAction.None, new Vector3(0f, 1.6f, 1.5f), 0.1f));
            for (int i = 0; i < 60; i++) input.Frame(Dt);
            Assert.That(e.Count, Is.EqualTo(1));
            Assert.That((e[0].Action, e[0].Value, e[0].Source), Is.EqualTo((PgAction.DialTo, 6, InputSource.Gaze)));
        }

        [Test]
        public void BindingASlotAgainReplacesItsTarget()
        {
            var input = new CoreInput();
            var targets = new PgGazeTargets(input);
            targets.Bind(PgSlot.HomeBeacon, GazeTarget.World(AtlasAction.None, new Vector3(0f, 1.6f, 1.5f), 0.1f));
            int count = input.Core.Targets.Count;
            targets.Bind(PgSlot.HomeBeacon, GazeTarget.World(AtlasAction.None, new Vector3(1f, 1.6f, 1.5f), 0.1f));
            Assert.That(input.Core.Targets.Count, Is.EqualTo(count));
            targets.Unbind(PgSlot.HomeBeacon);
            Assert.That(input.Core.Targets.Count, Is.EqualTo(count - 1));
            Assert.That(targets.IsRegistered(PgSlot.HomeBeacon), Is.False);
        }

        [Test]
        public void EveryPolledActionOfThePlaygroundHasAnEnabledGazeTarget()
        {
            // Rule 5 at run time: with every slot placed, no action the Playground polls lacks a gaze twin.
            var input = new CoreInput();
            var targets = new PgGazeTargets(input);
            var pos = new Vector3(0f, 1.6f, 1.5f);
            for (int i = 1; i < PgSlots.Count; i++)
            {
                var slot = (PgSlot)i;
                if (slot == PgSlot.LookDown) targets.BindLookDown();
                else targets.Bind(slot, GazeTarget.World(AtlasAction.None, pos + new Vector3(0.05f * i, 0f, 0f), 0.05f));
            }
            input.Frame(Dt);
            foreach (AtlasAction a in new[]
            {
                AtlasAction.Confirm, AtlasAction.DialLeft, AtlasAction.DialRight, AtlasAction.Primary, AtlasAction.SpaceMenu,
                AtlasAction.VariantNext, AtlasAction.Secondary,
                AtlasAction.ReturnHome
            })
                input.Pressed(a);
            input.Frame(Dt);
            var missing = new List<AtlasAction>();
            input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.Empty, string.Join(", ", missing));
            Assert.That(input.Core.Targets.HasEnabled(AtlasAxis.Sweep, InputContext.Space), Is.True, "the w-ruler");
            Assert.That(input.Core.Targets.HasEnabled(AtlasAxis.Twist, InputContext.Space), Is.True, "the rotation detents");
        }

        // ---- each input alone ------------------------------------------------------------------------------------

        /// <summary>The run's actions, in the order of the beats, with the input that gives each.</summary>
        static readonly PgAction[] Run3 =
        {
            PgAction.Confirm, PgAction.DialTo, PgAction.Mode, PgAction.ExhibitOpen, PgAction.ExhibitPick, PgAction.ReturnHome
        };

        [Test]
        public void EveryActionOfTheRunIsReachableFromTheGamepadAlone()
        {
            var map = new ActionMap();
            var e = Collect(map);
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.DialRight, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.Primary, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gamepad, 0);
            map.MenuOpen = true;
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gamepad, 0);
            map.MenuOpen = false;
            map.OnActionFired(AtlasAction.ReturnHome, InputSource.Gamepad, 0);
            Assert.That(e.ConvertAll(x => x.Action.ToString().Replace("DialStep", "DialTo")),
                        Is.EqualTo(System.Array.ConvertAll(Run3, a => a.ToString())));
        }

        [Test]
        public void EveryActionOfTheRunIsReachableFromTheLeverAlone()
        {
            var map = new ActionMap { ClickRole = LeverClickRole.Confirm };
            var e = Collect(map);
            Click(map);                                                    // confirm
            map.ClickRole = LeverClickRole.Dial; Click(map);               // dial
            map.ClickRole = LeverClickRole.Confirm; map.DoubleRole = LeverDoubleRole.Mode;
            Touch(map, 0.07f); Release(map, 0.1f); Touch(map, 0.07f); Release(map, 0.5f);   // mode
            Touch(map, 0.9f); Release(map, 0.5f);                          // open
            map.MenuOpen = true;
            Touch(map, 0.9f); Release(map, 0.5f);                          // pick
            map.MenuOpen = false;
            for (int i = 0; i < 3; i++) { Touch(map, 0.07f); Release(map, 0.1f); }   // home
            Release(map, 0.4f);
            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new List<PgAction>(Run3)));
            Assert.That(e.ConvertAll(x => x.Source), Is.All.EqualTo(InputSource.Lever));
        }

        [Test]
        public void EveryActionOfTheRunIsReachableFromGazeDwellAlone()
        {
            var input = new CoreInput();
            DwellReticle.Apply(input);
            var targets = new PgGazeTargets(input);
            var map = new ActionMap();
            var e = Collect(map);
            input.ActionFired += map.OnActionFired;

            // Six chips on a ring at 1.5 m, 40 degrees apart; the head turns to each in turn.
            PgSlot[] slots = { PgSlot.PromptChip, PgSlot.DialDetent5, PgSlot.ModeChip, PgSlot.ExhibitSign, PgSlot.None, PgSlot.HomeBeacon };
            Vector3 CentreOf(int k)
            {
                float a = Mathf.Deg2Rad * (k * 40f);
                return new Vector3(1.5f * Mathf.Sin(a), 1.6f, 1.5f * Mathf.Cos(a));
            }
            for (int k = 0; k < slots.Length; k++)
                if (slots[k] != PgSlot.None)
                    targets.Bind(slots[k], GazeTarget.World(AtlasAction.None, CentreOf(k), 0.1f));

            void LookAndDwell(int k)
            {
                input.AimYaw(k * 40f);
                for (int i = 0; i < 60; i++) input.Frame(Dt);
                // Look away between targets, so the next dwell starts fresh.
                input.AimYaw(200f);
                for (int i = 0; i < 30; i++) input.Frame(Dt);
            }
            LookAndDwell(0);
            LookAndDwell(1);
            LookAndDwell(2);
            LookAndDwell(3);
            // The menu opens: the signs are Menu-context targets (the menu's own); its pick is a dwell on a sign.
            input.PushContext(InputContext.Menu);
            map.MenuOpen = true;
            var sign = GazeTarget.World(AtlasAction.Confirm, CentreOf(4), 0.1f);
            sign.Context = InputContext.Menu;
            sign.Tag = 1;
            input.RegisterGazeTarget(in sign);
            LookAndDwell(4);
            input.PopContext(InputContext.Menu);
            map.MenuOpen = false;
            LookAndDwell(5);

            Assert.That(e.ConvertAll(x => x.Action), Is.EqualTo(new List<PgAction>(Run3)));
            Assert.That(e.ConvertAll(x => x.Source), Is.All.EqualTo(InputSource.Gaze));
            Assert.That(e[4].Value, Is.EqualTo(1), "the sign dwelt on");
            Assert.That(e[1].Value, Is.EqualTo(5), "the detent dwelt on");
        }
    }
}
