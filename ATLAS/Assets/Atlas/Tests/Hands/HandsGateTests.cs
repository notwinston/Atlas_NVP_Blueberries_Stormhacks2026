using System;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// When hands run (documents/ATLAS_Hands_Design.md sections 3.5 and 7.3, with decision R8) and at what
    /// rate. Time is a manual clock at 60 Hz.
    /// </summary>
    public class HandsGateTests
    {
        const double Frame = 1.0 / 60.0;

        sealed class Rig
        {
            public readonly HandsGate Gate = new HandsGate();
            public GateInputs In = Good();
            public double Now = 10.0;
            public GateState Last;
            public int ActiveChanges, RateChanges;

            public GateState Step()
            {
                Now += Frame;
                Last = Gate.Update(Now, (float)Frame, in In);
                if (Last.ActiveChanged) ActiveChanges++;
                if (Last.RateChanged) RateChanges++;
                return Last;
            }

            public GateState Run(double seconds)
            {
                int frames = (int)Math.Round(seconds / Frame);
                for (int i = 0; i < frames; i++) Step();
                return Last;
            }
        }

        /// <summary>Every row holding, in a healthy session: hands run at the full rate.</summary>
        static GateInputs Good()
        {
            return new GateInputs
            {
                Headset = true,
                GamepadPresent = false,
                Enabled = true,
                SourceAvailable = true,
                HeadFromCamera = true,
                Tier = QualityTier.Nominal,
                RateOverride = 0,
                RequestSeconds = 0.022f,
                MainThreadMs = 0.30f,
                AnyHandSeen = true
            };
        }

        // ---- Hands_Gate_Table ----

        [Test]
        public void Hands_Gate_Table()
        {
            EveryRowSwitchesActive();
            TheReasonNamesTheFirstRowThatFailed();
            TheGamepadHoldsBeforeItCounts();
            TheRateFollowsTheTier();
            TheGovernorDropsTheRateAndKeepsItThere();
            IdleDropsTheRateAndAHandBringsItBack();
            TheOverrideWinsEverywhere();
        }

        static void EveryRowSwitchesActive()
        {
            var healthy = new Rig();
            GateState s = healthy.Step();
            Assert.That(s.Active, Is.True, "all rows holding: active");
            Assert.That(s.Reason, Is.EqualTo(HandsOffReason.None));
            Assert.That(s.RateHz, Is.EqualTo(HandsConstants.RateFull));
            Assert.That(s.ActiveChanged, Is.True, "the first active frame is a change");
            Assert.That(s.RateChanged, Is.True);
            Assert.That(healthy.Gate.Current.Active, Is.True);

            var rows = new (string name, Action<Rig> fail, HandsOffReason reason)[]
            {
                ("mode: not the headset", r => r.In.Headset = false, HandsOffReason.Mode),
                ("pad: a gamepad is connected", r => r.In.GamepadPresent = true, HandsOffReason.Pad),
                ("param: hands.enabled is off", r => r.In.Enabled = false, HandsOffReason.Param),
                ("source: none available", r => r.In.SourceAvailable = false, HandsOffReason.Source),
                ("pose: the head is not from the AR camera", r => r.In.HeadFromCamera = false, HandsOffReason.Pose),
                ("heat: Serious", r => r.In.Tier = QualityTier.Serious, HandsOffReason.Heat),
                ("heat: Critical", r => r.In.Tier = QualityTier.Critical, HandsOffReason.Heat),
            };
            foreach (var row in rows)
            {
                // Inactive from the very first frame.
                var rig = new Rig();
                row.fail(rig);
                s = rig.Step();
                Assert.That(s.Active, Is.False, row.name);
                Assert.That(s.Reason, Is.EqualTo(row.reason), row.name);
                Assert.That(s.RateHz, Is.EqualTo(0), row.name + ": no rate when inactive");
                Assert.That(s.ActiveChanged, Is.False, row.name + ": it was never active");

                // Active for a while, then the row fails (a gamepad is counted only after its hold: its own test).
                if (row.reason == HandsOffReason.Pad) continue;
                rig = new Rig();
                rig.Run(0.5);
                Assert.That(rig.Last.Active, Is.True, row.name);
                row.fail(rig);
                s = rig.Step();
                Assert.That(s.Active, Is.False, row.name);
                Assert.That(s.Reason, Is.EqualTo(row.reason), row.name);
                Assert.That(s.RateHz, Is.EqualTo(0), row.name);
                Assert.That(s.ActiveChanged, Is.True, row.name + ": the frame it switches off");
                Assert.That(s.RateChanged, Is.True, row.name + ": the rate goes to 0");
                Assert.That(rig.Step().ActiveChanged, Is.False, row.name + ": a change lasts one frame");
                Assert.That(rig.ActiveChanges, Is.EqualTo(2), row.name + ": on once, off once");
            }

            // The three tiers that allow hands.
            foreach (QualityTier tier in new[] { QualityTier.Nominal, QualityTier.High, QualityTier.Fair })
            {
                var rig = new Rig();
                rig.In.Tier = tier;
                Assert.That(rig.Step().Active, Is.True, "tier " + tier);
            }
            // A value outside the ladder is not a reason to run.
            var odd = new Rig();
            odd.In.Tier = (QualityTier)9;
            Assert.That(odd.Step().Reason, Is.EqualTo(HandsOffReason.Heat));
        }

        static void TheReasonNamesTheFirstRowThatFailed()
        {
            // Everything failing at once; each row is mended in the order of the table and the next one shows.
            var rig = new Rig();
            rig.In.Headset = false;
            rig.In.GamepadPresent = true;
            rig.In.Enabled = false;
            rig.In.SourceAvailable = false;
            rig.In.HeadFromCamera = false;
            rig.In.Tier = QualityTier.Serious;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Mode));

            rig.In.Headset = true;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Pad));
            rig.In.GamepadPresent = false;
            Assert.That(rig.Run(1.2).Reason, Is.EqualTo(HandsOffReason.Param), "the gamepad has gone for long enough");
            rig.In.Enabled = true;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Source));
            rig.In.SourceAvailable = true;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Pose));
            rig.In.HeadFromCamera = true;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Heat));
            rig.In.Tier = QualityTier.Fair;
            GateState s = rig.Step();
            Assert.That(s.Reason, Is.EqualTo(HandsOffReason.None));
            Assert.That(s.Active, Is.True);

            // And the same chain the other way round: the first failing row is the one named.
            rig = new Rig();
            rig.Run(0.2);
            rig.In.Tier = QualityTier.Critical;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Heat));
            rig.In.HeadFromCamera = false;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Pose));
            rig.In.SourceAvailable = false;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Source));
            rig.In.Enabled = false;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Param));
            rig.In.Headset = false;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Mode));
        }

        static void TheGamepadHoldsBeforeItCounts()
        {
            float hold = HandsConstants.GamepadHoldSeconds;

            // A gamepad that stays connected counts after the hold, not before.
            var rig = new Rig();
            rig.Run(0.5);
            rig.In.GamepadPresent = true;
            rig.Run(hold - 0.2);
            Assert.That(rig.Last.Active, Is.True, "still hands before the hold is over");
            rig.Run(0.4);
            Assert.That(rig.Last.Active, Is.False, "a gamepad that stayed counts");
            Assert.That(rig.Last.Reason, Is.EqualTo(HandsOffReason.Pad));
            Assert.That(rig.ActiveChanges, Is.EqualTo(2));

            // And its leaving counts after the hold too.
            rig.In.GamepadPresent = false;
            rig.Run(hold - 0.2);
            Assert.That(rig.Last.Active, Is.False, "the gate does not follow the gamepad out at once either");
            Assert.That(rig.Last.Reason, Is.EqualTo(HandsOffReason.Pad));
            rig.Run(0.4);
            Assert.That(rig.Last.Active, Is.True);
            Assert.That(rig.ActiveChanges, Is.EqualTo(3));

            // A gamepad that flickers is never counted: each change must hold on its own.
            rig = new Rig();
            rig.Run(0.5);
            for (int i = 0; i < 6; i++)
            {
                rig.In.GamepadPresent = true;
                rig.Run(hold - 0.4);
                rig.In.GamepadPresent = false;
                rig.Run(0.2);
            }
            Assert.That(rig.Last.Active, Is.True, "a flicker is not a gamepad");
            Assert.That(rig.ActiveChanges, Is.EqualTo(1));

            // A gamepad already there when the gate first looks is counted at once: there is no change to wait out.
            rig = new Rig();
            rig.In.GamepadPresent = true;
            Assert.That(rig.Step().Reason, Is.EqualTo(HandsOffReason.Pad));
        }

        static void TheRateFollowsTheTier()
        {
            var rig = new Rig();
            rig.In.Tier = QualityTier.Nominal;
            Assert.That(rig.Step().RateHz, Is.EqualTo(HandsConstants.RateFull), "Nominal");
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Tier));
            rig.In.Tier = QualityTier.High;
            Assert.That(rig.Step().RateHz, Is.EqualTo(HandsConstants.RateFull), "High");
            Assert.That(rig.Last.RateChanged, Is.False);

            rig.In.Tier = QualityTier.Fair;
            GateState s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(HandsConstants.RateReduced), "Fair");
            Assert.That(s.RateWhy, Is.EqualTo(HandsRateWhy.Tier));
            Assert.That(s.RateChanged, Is.True, "the frame the rate changes");
            Assert.That(s.ActiveChanged, Is.False, "while the gate stays active");
            Assert.That(rig.Step().RateChanged, Is.False, "a change lasts one frame");

            rig.In.Tier = QualityTier.Serious;
            s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(0), "Serious: stopped");
            Assert.That(s.Active, Is.False);
            rig.In.Tier = QualityTier.Critical;
            Assert.That(rig.Step().RateHz, Is.EqualTo(0), "Critical: stopped");

            rig.In.Tier = QualityTier.Nominal;
            s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(HandsConstants.RateFull), "the heat has gone: back at full rate");
            Assert.That(s.ActiveChanged, Is.True);
        }

        static void TheGovernorDropsTheRateAndKeepsItThere()
        {
            float needed = HandsConstants.GovernorSeconds;

            // Requests too long for the whole time.
            var rig = new Rig();
            rig.Run(1.0);
            rig.In.RequestSeconds = HandsConstants.GovernorRequestSeconds + 0.02f;
            rig.Run(needed - 0.1);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "not yet");
            int changes = rig.RateChanges;
            rig.Run(0.2);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced), "the governor steps in");
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Governor));
            Assert.That(rig.RateChanges, Is.EqualTo(changes + 1), "one change, flagged once");

            // It stays there while the results recover, and across tiers, until the gate next goes inactive.
            rig.In.RequestSeconds = 0.022f;
            rig.Run(30.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced));
            rig.In.Tier = QualityTier.Fair;
            rig.Run(0.2);
            rig.In.Tier = QualityTier.Nominal;
            rig.Run(0.2);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced), "still governed after a visit to Fair");
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Governor));
            rig.In.Headset = false;
            rig.Step();
            rig.In.Headset = true;
            Assert.That(rig.Step().RateHz, Is.EqualTo(HandsConstants.RateFull), "an inactive gate forgets the governor");

            // The hands' own main-thread time is the other cause.
            rig = new Rig();
            rig.Run(1.0);
            rig.In.MainThreadMs = HandsConstants.GovernorMainThreadMs + 0.2f;
            rig.Run(needed - 0.1);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull));
            rig.Run(0.2);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced));
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Governor));

            // It has to be for the whole time: a good frame in between starts the count again, for either cause.
            rig = new Rig();
            for (int i = 0; i < 4; i++)
            {
                rig.In.RequestSeconds = 0.1f;
                rig.Run(needed - 0.3);
                rig.In.RequestSeconds = 0.01f;
                rig.Step();
            }
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "broken runs of slow results do not add up");
            rig = new Rig();
            for (int i = 0; i < 4; i++)
            {
                rig.In.MainThreadMs = 1.0f;
                rig.Run(needed - 0.3);
                rig.In.MainThreadMs = 0.1f;
                rig.Step();
            }
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "broken runs of slow frames do not add up");

            // The two causes keep separate clocks: one second of each is not two seconds of either.
            rig = new Rig();
            rig.In.RequestSeconds = 0.1f;
            rig.Run(needed * 0.6);
            rig.In.RequestSeconds = 0.01f;
            rig.In.MainThreadMs = 1.0f;
            rig.Run(needed * 0.6);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull));

            // At Fair the rate is already the reduced one: the governor has nothing to say and does not engage.
            rig = new Rig();
            rig.In.Tier = QualityTier.Fair;
            rig.In.RequestSeconds = 0.2f;
            rig.Run(needed * 2.5);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced));
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Tier));
            rig.In.Tier = QualityTier.Nominal;
            rig.In.RequestSeconds = 0.01f;
            Assert.That(rig.Step().RateHz, Is.EqualTo(HandsConstants.RateFull), "so the full rate returns with the tier");
        }

        /// <summary>
        /// The governor reacts to an overloaded detector, not to the pipeline's ordinary latency: a healthy one
        /// (requests of 22 ms, the results about 90 ms old by the time they are polled) stays at 30 Hz, and one
        /// whose requests take 40 ms (longer than a full-rate period) drops to 15 Hz after two seconds.
        /// </summary>
        [Test]
        public void Hands_Gate_GovernorIgnoresPipelineLatency()
        {
            var rig = new Rig();
            rig.In.RequestSeconds = 0.022f;
            rig.Run(10.0);
            Assert.That(rig.Last.Active, Is.True);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "a healthy detector stays at 30 Hz");
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Tier));

            rig.In.RequestSeconds = 0.040f;
            rig.Run(HandsConstants.GovernorSeconds - 0.1);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "not yet");
            rig.Run(0.2);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced), "an overloaded detector drops to 15 Hz");
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Governor));
        }

        static void IdleDropsTheRateAndAHandBringsItBack()
        {
            float idle = HandsConstants.IdleAfterSeconds;

            var rig = new Rig();
            rig.In.AnyHandSeen = false;
            rig.Run(idle - 1.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "not idle yet");
            int changes = rig.RateChanges;
            rig.Run(2.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateIdle), "no hand for IdleAfterSeconds");
            Assert.That(rig.Last.RateWhy, Is.EqualTo(HandsRateWhy.Idle));
            Assert.That(rig.RateChanges, Is.EqualTo(changes + 1));
            rig.Run(30.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateIdle), "and it stays idle");

            // The first hand brings the rate back in that very frame.
            rig.In.AnyHandSeen = true;
            GateState s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(HandsConstants.RateFull), "returns at the first hand");
            Assert.That(s.RateChanged, Is.True);
            Assert.That(s.RateWhy, Is.EqualTo(HandsRateWhy.Tier));

            // A hand now and then keeps it from idling.
            rig = new Rig();
            for (int i = 0; i < 5; i++)
            {
                rig.In.AnyHandSeen = false;
                rig.Run(idle - 2.0);
                rig.In.AnyHandSeen = true;
                rig.Step();
            }
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull));

            // The clock runs only while the gate is active: after an inactive spell it starts again.
            rig = new Rig();
            rig.In.AnyHandSeen = false;
            rig.Run(idle - 1.0);
            rig.In.Headset = false;
            rig.Run(0.5);
            rig.In.Headset = true;
            rig.Run(idle - 1.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "the idle time starts again with the gate");
            rig.Run(2.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateIdle));

            // Idle goes below the tier's rate, and the tier's rate is what comes back.
            rig = new Rig();
            rig.In.Tier = QualityTier.Fair;
            rig.In.AnyHandSeen = false;
            rig.Run(idle + 1.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateIdle));
            rig.In.AnyHandSeen = true;
            Assert.That(rig.Step().RateHz, Is.EqualTo(HandsConstants.RateReduced));
        }

        static void TheOverrideWinsEverywhere()
        {
            // 30 or 15 whatever the tier says.
            var rig = new Rig();
            rig.In.RateOverride = 15;
            GateState s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(15));
            Assert.That(s.RateWhy, Is.EqualTo(HandsRateWhy.Override));
            rig.In.Tier = QualityTier.Fair;
            rig.In.RateOverride = 30;
            s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(30), "30 at Fair, for the bench");
            Assert.That(s.RateWhy, Is.EqualTo(HandsRateWhy.Override));
            Assert.That(s.RateChanged, Is.True);

            // Over the governor and over idle.
            rig = new Rig();
            rig.In.RateOverride = 30;
            rig.In.RequestSeconds = 0.3f;
            rig.In.MainThreadMs = 3f;
            rig.In.AnyHandSeen = false;
            rig.Run(HandsConstants.IdleAfterSeconds + 5.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(30), "the override holds against the governor and idle");

            // Back to auto: the rate is the automatic one again.
            rig.In.RateOverride = 0;
            rig.In.AnyHandSeen = true;
            rig.In.RequestSeconds = 0.01f;
            rig.In.MainThreadMs = 0.1f;
            s = rig.Step();
            Assert.That(s.RateHz, Is.EqualTo(HandsConstants.RateFull));
            Assert.That(s.RateWhy, Is.EqualTo(HandsRateWhy.Tier));

            // It does not start a gate that is off.
            rig = new Rig();
            rig.In.RateOverride = 30;
            rig.In.Tier = QualityTier.Serious;
            s = rig.Step();
            Assert.That(s.Active, Is.False);
            Assert.That(s.RateHz, Is.EqualTo(0));

            // A positive value that is neither 30 nor 15 is read as the nearer of the two; zero or less is auto.
            foreach (var pair in new[] { (20, 15), (25, 30), (100, 30), (5, 15), (-4, 30) })
            {
                rig = new Rig();
                rig.In.RateOverride = pair.Item1;
                Assert.That(rig.Step().RateHz, Is.EqualTo(pair.Item2), "override " + pair.Item1);
            }
        }

        // ---- Near ----

        [Test]
        public void Hands_Gate_NearIsEveryRowButTheTier()
        {
            Assert.That(new HandsGate().Current.Near, Is.False, "before the first update");

            // Active, and then hot: the hands stop, the panels stay where they are and work by gaze.
            var rig = new Rig();
            Assert.That(rig.Step().Near, Is.True, "active");
            foreach (QualityTier tier in new[] { QualityTier.Serious, QualityTier.Critical })
            {
                rig.In.Tier = tier;
                GateState s = rig.Step();
                Assert.That(s.Active, Is.False);
                Assert.That(s.Near, Is.True, "tier " + tier + ": no hands, panels still near");
            }
            rig.In.Tier = QualityTier.Fair;
            Assert.That(rig.Step().Near, Is.True, "Fair");

            // Any other row failing brings the panels back to where they were: a hot phone changes nothing about that.
            var rows = new (string name, Action<Rig> fail)[]
            {
                ("mode", r => r.In.Headset = false),
                ("param", r => r.In.Enabled = false),
                ("source", r => r.In.SourceAvailable = false),
                ("pose", r => r.In.HeadFromCamera = false),
                ("pad", r => r.In.GamepadPresent = true),
            };
            foreach (var row in rows)
            {
                rig = new Rig();
                row.fail(rig);
                Assert.That(rig.Step().Near, Is.False, row.name);
                rig.In.Tier = QualityTier.Serious;
                Assert.That(rig.Step().Near, Is.False, row.name + " and hot: the first failed row is not the tier");
            }

            // A gamepad counts after its hold, and so does the end of near.
            rig = new Rig();
            rig.Run(0.5);
            rig.In.GamepadPresent = true;
            Assert.That(rig.Run(0.5).Near, Is.True, "inside the hold");
            Assert.That(rig.Run(0.7).Near, Is.False, "after the hold");
        }

        // ---- The state as a whole ----

        [Test]
        public void Hands_Gate_CurrentIsTheLastAnswer_AndResetStartsOver()
        {
            var rig = new Rig();
            Assert.That(rig.Gate.Current.Active, Is.False, "before the first update");
            Assert.That(rig.Gate.Current.RateHz, Is.EqualTo(0));

            GateState s = rig.Step();
            Assert.That(rig.Gate.Current.Active, Is.EqualTo(s.Active));
            Assert.That(rig.Gate.Current.RateHz, Is.EqualTo(s.RateHz));
            Assert.That(rig.Gate.Current.ActiveChanged, Is.EqualTo(s.ActiveChanged));
            rig.Run(0.5);
            Assert.That(rig.Gate.Current.ActiveChanged, Is.False);

            // After a reset the same healthy inputs are a fresh start, announced again.
            rig.Gate.Reset();
            Assert.That(rig.Gate.Current.Active, Is.False);
            s = rig.Step();
            Assert.That(s.Active, Is.True);
            Assert.That(s.ActiveChanged, Is.True);
            Assert.That(s.RateChanged, Is.True);

            // A reset forgets the governor and the idle time too.
            rig.In.RequestSeconds = 0.3f;
            rig.Run(HandsConstants.GovernorSeconds + 0.5);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateReduced));
            rig.Gate.Reset();
            rig.In.RequestSeconds = 0.01f;
            Assert.That(rig.Step().RateHz, Is.EqualTo(HandsConstants.RateFull));
        }

        [Test]
        public void Hands_Gate_SurvivesGarbageInput()
        {
            var rig = new Rig();
            rig.In.RequestSeconds = float.NaN;
            rig.In.MainThreadMs = float.NaN;
            rig.Run(HandsConstants.GovernorSeconds + 1.0);
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull), "numbers that are not numbers are not an overload");

            rig = new Rig();
            rig.In.RequestSeconds = 1f;
            for (int i = 0; i < 400; i++)
            {
                rig.Now += Frame;
                rig.Last = rig.Gate.Update(rig.Now, -1f, in rig.In);          // a negative step is no step
            }
            Assert.That(rig.Last.RateHz, Is.EqualTo(HandsConstants.RateFull));
        }

        // ---- Allocation ----

        [Test]
        public void Hands_Gate_UpdateAllocatesNothing()
        {
            var gate = new HandsGate();
            GateInputs inputs = Good();
            double now = 10.0;

            Run(gate, ref now, ref inputs, 4000);                      // warm up through every path

            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(gate, ref now, ref inputs, 100);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 100 updates");
        }

        static void Run(HandsGate gate, ref double now, ref GateInputs inputs, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                now += Frame;
                int phase = i % 1000;
                inputs.GamepadPresent = phase > 300 && phase < 500;
                inputs.Tier = phase < 700 ? QualityTier.Nominal : phase < 800 ? QualityTier.Fair : QualityTier.Serious;
                inputs.RequestSeconds = phase < 100 ? 0.1f : 0.022f;
                inputs.MainThreadMs = phase < 50 ? 0.9f : 0.2f;
                inputs.AnyHandSeen = (phase & 64) == 0;
                inputs.RateOverride = phase > 900 ? 15 : 0;
                inputs.Headset = phase != 950;
                GateState s = gate.Update(now, (float)Frame, in inputs);
                if (s.RateHz < 0) throw new InvalidOperationException("negative rate");
            }
        }
    }
}
