using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// The warning cue's scheduler (Master Plan section 4.10, outputs table; task P1-C14): what plays when.
    /// Engine-free: time is passed in.
    /// </summary>
    public class BumpCueSchedulerTests
    {
        const double Dt = 1.0 / 60.0;

        BumpCueScheduler _cue;
        readonly List<double> _pulses = new List<double>();
        readonly List<float> _strengths = new List<float>();
        readonly List<CueSound> _sounds = new List<CueSound>();
        int _frame;

        [SetUp]
        public void SetUp()
        {
            _cue = new BumpCueScheduler();
            _cue.Reset();
            _pulses.Clear();
            _strengths.Clear();
            _sounds.Clear();
            _frame = 0;
        }

        double Now { get { return _frame * Dt; } }

        void Drain()
        {
            while (_cue.TakePulse(out float strength, out CueSound sound))
            {
                _pulses.Add(Now);
                _strengths.Add(strength);
                _sounds.Add(sound);
            }
        }

        // One frame as Bump Guard runs it: Fire, Rescan, Advance, with the pulses taken after each call.
        void Frame(BumpLevel guard, float2 direction, bool rescan = false)
        {
            _cue.Fire(guard, direction, CueSource.Guard);
            Drain();
            _cue.Rescan(rescan);
            Drain();
            _cue.Advance(Now, (float)Dt);
            Drain();
            _frame++;
        }

        void Run(double seconds, BumpLevel guard, float2 direction, bool rescan = false)
        {
            int frames = (int)math.round(seconds / Dt);
            for (int k = 0; k < frames; k++) Frame(guard, direction, rescan);
        }

        void AssertIntervals(int from, double period)
        {
            for (int i = from + 1; i < _pulses.Count; i++)
                Assert.That(_pulses[i] - _pulses[i - 1], Is.EqualTo(period).Within(Dt + 1e-6), "interval before pulse " + i);
        }

        [Test]
        public void Warn_PulsesTwiceASecond_WithTheTick()
        {
            Run(10.0, BumpLevel.Warn, new float2(1f, 0f));

            Assert.That(_pulses.Count, Is.EqualTo(20));
            Assert.That(_pulses[0], Is.EqualTo(0.0), "the first pulse is in the frame of the call");
            AssertIntervals(0, 0.5);
            Assert.That(_strengths, Is.All.EqualTo(BumpCueScheduler.WarnStrength));
            Assert.That(_sounds, Is.All.EqualTo(CueSound.Tick));
            Assert.That(_cue.Pattern, Is.EqualTo(HapticPattern.Warn));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.ChevronWarn));
        }

        [Test]
        public void Strong_PulsesSixTimesASecond_WithTheTone()
        {
            Run(10.0, BumpLevel.Strong, new float2(0f, 1f));

            Assert.That(_pulses.Count, Is.EqualTo(60));
            Assert.That(_pulses[0], Is.EqualTo(0.0));
            AssertIntervals(0, 1.0 / 6.0);
            Assert.That(_strengths, Is.All.EqualTo(BumpCueScheduler.StrongStrength));
            Assert.That(_sounds, Is.All.EqualTo(CueSound.Tone));
            Assert.That(_cue.Pattern, Is.EqualTo(HapticPattern.Strong));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.ChevronStrong));
        }

        [Test]
        public void TrackingLost_PulsesSixTimesASecond_WithTheStandStillIconAndNoSide()
        {
            Run(5.0, BumpLevel.TrackingLost, new float2(1f, 0f));

            Assert.That(_pulses.Count, Is.EqualTo(30));
            Assert.That(_sounds, Is.All.EqualTo(CueSound.Tone));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.StandStill));
            Assert.That(_cue.SoundSpatial, Is.False);
            Assert.That(_cue.IndicatorPosition.x, Is.EqualTo(0f));
            Assert.That(_cue.IndicatorPosition.y, Is.EqualTo(0f));
            Assert.That(_cue.IndicatorPosition.z, Is.EqualTo(BumpCueScheduler.IndicatorDistance));
        }

        [Test]
        public void ARisingLevel_StartsInTheCallItself()
        {
            Run(1.0, BumpLevel.Clear, float2.zero);
            Assert.That(_pulses, Is.Empty);

            _cue.Fire(BumpLevel.Warn, new float2(-1f, 0f), CueSource.Guard);

            // Before any Advance: the indicator is up and the first pulse is due.
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Warn));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.ChevronWarn));
            Assert.That(_cue.FireCount, Is.EqualTo(1));
            Assert.That(_cue.TakePulse(out float strength, out CueSound sound), Is.True);
            Assert.That(strength, Is.EqualTo(BumpCueScheduler.WarnStrength));
            Assert.That(sound, Is.EqualTo(CueSound.Tick));
            Assert.That(_cue.TakePulse(out _, out _), Is.False, "one pulse, taken once");
        }

        [Test]
        public void Clear_StopsEverythingAtOnce()
        {
            Run(1.1, BumpLevel.Strong, new float2(0f, 1f));
            int before = _pulses.Count;

            Run(3.0, BumpLevel.Clear, float2.zero);

            Assert.That(_pulses.Count, Is.EqualTo(before));
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_cue.Pattern, Is.EqualTo(HapticPattern.Off));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.None));
            Assert.That(_cue.SoundSpatial, Is.False);
            Assert.That(math.lengthsq(_cue.Direction), Is.EqualTo(0f));
        }

        [Test]
        public void Clear_DropsAPulseThatWasNotYetTaken()
        {
            _cue.Fire(BumpLevel.Strong, float2.zero, CueSource.Guard);
            _cue.Fire(BumpLevel.Clear, float2.zero, CueSource.Guard);
            Assert.That(_cue.TakePulse(out _, out _), Is.False);
        }

        [Test]
        public void ARiseInTheMiddleOfAPulseInterval_PulsesAtOnceAndTakesTheFasterRhythm()
        {
            Run(0.2, BumpLevel.Warn, new float2(0f, 1f));
            Assert.That(_pulses.Count, Is.EqualTo(1));
            double rise = Now;

            Run(1.0, BumpLevel.Strong, new float2(0f, 1f));

            Assert.That(_pulses[1], Is.EqualTo(rise).Within(1e-9), "no wait for the WARN interval to end");
            Assert.That(_sounds[1], Is.EqualTo(CueSound.Tone));
            AssertIntervals(1, 1.0 / 6.0);
            Assert.That(_pulses.Count, Is.EqualTo(1 + 6));
            Assert.That(_cue.FireCount, Is.EqualTo(2));
        }

        [Test]
        public void AFallInTheMiddleOfAPulseInterval_KeepsThePhaseAndTakesTheSlowerRhythm()
        {
            Run(1.05, BumpLevel.Strong, new float2(0f, 1f));
            int strongPulses = _pulses.Count;
            double lastStrong = _pulses[strongPulses - 1];

            Run(2.0, BumpLevel.Warn, new float2(0f, 1f));

            Assert.That(_cue.FireCount, Is.EqualTo(1), "a fall is not a new cue");
            Assert.That(_pulses[strongPulses] - lastStrong, Is.EqualTo(0.5).Within(Dt + 1e-6));
            Assert.That(_sounds[strongPulses], Is.EqualTo(CueSound.Tick));
            AssertIntervals(strongPulses, 0.5);
        }

        [Test]
        public void TheOperatorCue_IsHeldForThreeSeconds_ThenDrops()
        {
            Run(0.5, BumpLevel.Clear, float2.zero);
            double fired = Now;
            _cue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);
            Drain();

            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Strong), "shown in the call");
            Assert.That(_pulses.Count, Is.EqualTo(1));
            Assert.That(_cue.FireCount, Is.EqualTo(1));

            double shownUntil = -1.0;
            for (int k = 0; k < 300; k++)
            {
                Frame(BumpLevel.Clear, float2.zero);         // Bump Guard keeps saying Clear
                if (_cue.Shown == BumpLevel.Strong) shownUntil = Now;
            }

            Assert.That(shownUntil - fired, Is.EqualTo(BumpConstants.OperatorHoldSeconds).Within(2 * Dt));
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_pulses.Count, Is.EqualTo(18).Within(1), "six a second for three seconds");
            Assert.That(_sounds, Is.All.EqualTo(CueSound.Tone));
        }

        [Test]
        public void TheOperatorCue_HasNoSide_AndSitsAtTheTop()
        {
            _cue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);
            _cue.Advance(0.0, 0f);

            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.ChevronStrong));
            Assert.That(_cue.IndicatorPosition.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(_cue.IndicatorPosition.y, Is.EqualTo(BumpCueScheduler.ChevronRadius).Within(1e-6f));
            Assert.That(_cue.IndicatorRoll, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(_cue.SoundSpatial, Is.False);
        }

        [Test]
        public void TheHigherSourceIsShown_AndOnATieTheGuardGivesTheDirection()
        {
            _cue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);
            _cue.Fire(BumpLevel.Warn, new float2(1f, 0f), CueSource.Guard);
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Strong));
            Assert.That(math.lengthsq(_cue.Direction), Is.EqualTo(0f), "the operator's cue has no side");

            _cue.Fire(BumpLevel.Strong, new float2(1f, 0f), CueSource.Guard);
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Strong));
            Assert.That(_cue.Direction.x, Is.EqualTo(1f).Within(1e-6f));

            _cue.Fire(BumpLevel.TrackingLost, float2.zero, CueSource.Guard);
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.TrackingLost));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.StandStill));
        }

        [Test]
        public void TheOperatorCue_DoesNotOutliveBumpGuardsOwn()
        {
            _cue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);
            Run(4.0, BumpLevel.Warn, new float2(0f, 1f));
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Warn), "after the hold Bump Guard's own level stands");
            Assert.That(_cue.Pattern, Is.EqualTo(HapticPattern.Warn));
        }

        [Test]
        public void Rescan_ShowsItsIconWithTheWarnRhythm_OnlyWhileNoLevelShows()
        {
            Run(2.0, BumpLevel.Clear, float2.zero, rescan: true);

            Assert.That(_cue.RescanShown, Is.True);
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.Rescan));
            Assert.That(_cue.IndicatorPosition.y, Is.LessThan(0f), "below the view centre");
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_cue.FireCount, Is.EqualTo(0), "the icon is not a level");
            Assert.That(_pulses.Count, Is.EqualTo(4));
            Assert.That(_pulses[0], Is.EqualTo(0.0));
            Assert.That(_sounds, Is.All.EqualTo(CueSound.Tick));

            Frame(BumpLevel.Strong, new float2(0f, 1f), rescan: true);
            Assert.That(_cue.RescanShown, Is.False);
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.ChevronStrong));
        }

        [Test]
        public void TheChevron_SitsAtTheHazardsBearing_AndTheSoundComesFromThatSide()
        {
            // A hazard to the right.
            Frame(BumpLevel.Warn, new float2(2f, 0f));
            Assert.That(_cue.IndicatorPosition.x, Is.EqualTo(BumpCueScheduler.ChevronRadius).Within(1e-5f));
            Assert.That(_cue.IndicatorPosition.y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(_cue.IndicatorPosition.z, Is.EqualTo(BumpCueScheduler.IndicatorDistance));
            Assert.That(_cue.IndicatorRoll, Is.EqualTo(-math.PI / 2f).Within(1e-5f), "it points outwards");
            Assert.That(_cue.SoundSpatial, Is.True);
            Assert.That(_cue.SoundPosition.x, Is.EqualTo(1f).Within(1e-5f), "1 m from the head");
            Assert.That(_cue.SoundPosition.z, Is.EqualTo(0f).Within(1e-5f));

            // Behind: the bottom of the circle.
            Frame(BumpLevel.Warn, new float2(0f, -1f));
            Assert.That(_cue.IndicatorPosition.y, Is.EqualTo(-BumpCueScheduler.ChevronRadius).Within(1e-5f));
            Assert.That(_cue.SoundPosition.z, Is.EqualTo(-1f).Within(1e-5f));

            // To the left.
            Frame(BumpLevel.Warn, new float2(-1f, 0f));
            Assert.That(_cue.IndicatorPosition.x, Is.EqualTo(-BumpCueScheduler.ChevronRadius).Within(1e-5f));
            Assert.That(_cue.IndicatorScale, Is.EqualTo(1f));
        }

        [Test]
        public void TheStrongChevron_IsLargerAndItsSizePulses()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int k = 0; k < 60; k++)
            {
                Frame(BumpLevel.Strong, new float2(0f, 1f));
                min = math.min(min, _cue.IndicatorScale);
                max = math.max(max, _cue.IndicatorScale);
            }
            Assert.That(min, Is.GreaterThan(1.2f), "always larger than the WARN chevron");
            Assert.That(max - min, Is.GreaterThan(0.3f), "it pulses");
            Assert.That(max, Is.LessThanOrEqualTo(BumpCueScheduler.StrongScale * (1f + BumpCueScheduler.StrongPulseDepth) + 1e-4f));
        }

        [Test]
        public void ALongFrame_GivesOnePulse_NotABurst()
        {
            Frame(BumpLevel.Strong, float2.zero);
            Assert.That(_pulses.Count, Is.EqualTo(1));

            _frame += 120;                                   // a two-second hitch
            Frame(BumpLevel.Strong, float2.zero);
            Assert.That(_pulses.Count, Is.EqualTo(2));
            Frame(BumpLevel.Strong, float2.zero);
            Assert.That(_pulses.Count, Is.EqualTo(2));
        }

        [Test]
        public void Reset_ClearsTheStateAndTheCounters()
        {
            Run(1.0, BumpLevel.Strong, new float2(1f, 0f));
            _cue.Reset();
            Assert.That(_cue.Shown, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_cue.FireCount, Is.EqualTo(0));
            Assert.That(_cue.PulseCount, Is.EqualTo(0));
            Assert.That(_cue.Icon, Is.EqualTo(CueIcon.None));
            Assert.That(_cue.TakePulse(out _, out _), Is.False);
        }

        [Test]
        public void TheShapes_AreWoundTowardsTheViewer_AndStayInsideTheirSize()
        {
            CueShape[] shapes = { BumpCueShapes.Chevron(), BumpCueShapes.StandStill(), BumpCueShapes.Rescan() };
            foreach (CueShape shape in shapes)
            {
                Assert.That(shape.IndexCount, Is.GreaterThan(0));
                Assert.That(shape.IndexCount % 3, Is.EqualTo(0));
                Assert.That(shape.PointCount, Is.LessThanOrEqualTo(shape.Points.Length));
                for (int i = 0; i < shape.PointCount; i++)
                    Assert.That(math.length(shape.Points[i]), Is.LessThan(0.12f), "about 7 degrees at 1 m at most");
                for (int t = 0; t < shape.IndexCount; t += 3)
                {
                    float2 a = shape.Points[shape.Triangles[t]];
                    float2 b = shape.Points[shape.Triangles[t + 1]];
                    float2 c = shape.Points[shape.Triangles[t + 2]];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    Assert.That(cross, Is.LessThanOrEqualTo(0f), "clockwise for the viewer");
                }
            }
            // The three must not be mistaken for each other: they differ in outline size and in triangle count.
            Assert.That(shapes[0].IndexCount, Is.Not.EqualTo(shapes[1].IndexCount));
            Assert.That(shapes[1].IndexCount, Is.Not.EqualTo(shapes[2].IndexCount));
        }

        [Test]
        public void TheSounds_HaveTheirLengthAndPitch_AndDoNotClip()
        {
            float[] tick = BumpCueTones.Tick();
            float[] tone = BumpCueTones.Tone();
            Assert.That(tick.Length, Is.EqualTo(480), "10 ms at 48 kHz");
            Assert.That(tone.Length, Is.EqualTo(3840), "80 ms at 48 kHz");
            Assert.That(Peak(tick), Is.LessThanOrEqualTo(0.5f), "soft");
            Assert.That(Peak(tone), Is.GreaterThan(0.8f).And.LessThanOrEqualTo(1f), "clear");
            Assert.That(math.abs(tick[tick.Length - 1]), Is.LessThan(0.01f), "ends in silence: no click");
            Assert.That(math.abs(tone[0]), Is.LessThan(0.01f));
            Assert.That(math.abs(tone[tone.Length - 1]), Is.LessThan(0.01f));
            Assert.That(ZeroCrossings(tone) / 2f / BumpCueTones.ToneSeconds, Is.EqualTo(BumpCueTones.ToneHz).Within(15f));
            Assert.That(ZeroCrossings(tick) / 2f / BumpCueTones.TickSeconds, Is.EqualTo(BumpCueTones.TickHz).Within(150f));
        }

        static float Peak(float[] samples)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) peak = math.max(peak, math.abs(samples[i]));
            return peak;
        }

        static int ZeroCrossings(float[] samples)
        {
            int crossings = 0;
            for (int i = 1; i < samples.Length; i++)
                if ((samples[i - 1] < 0f) != (samples[i] < 0f)) crossings++;
            return crossings;
        }
    }
}
