using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C09: the FMS question every 60 s ± 5 s, the stop line on an entry, and no event of its own (the Hub writes fms).</summary>
    [TestFixture]
    public class ComfortMonitorTests
    {
        static ComfortMonitor Make(out PromptQueue prompts, int stop = 4)
        {
            prompts = new PromptQueue(new RecordingSink());
            return new ComfortMonitor(prompts, () => stop);
        }

        /// <summary>Runs the monitor at 90 Hz and returns the times of the questions.</summary>
        static System.Collections.Generic.List<double> Run(ComfortMonitor monitor, double seconds, double hitchAt = -1, double hitch = 0)
        {
            var asks = new System.Collections.Generic.List<double>();
            int last = 0;
            double t = 0;
            monitor.Start(0);
            while (t < seconds)
            {
                t += 1.0 / 90.0;
                if (hitchAt >= 0 && t >= hitchAt && t < hitchAt + 1.0 / 90.0) t += hitch;
                monitor.Tick(t);
                if (monitor.AskCount != last)
                {
                    last = monitor.AskCount;
                    asks.Add(monitor.LastAskS);
                }
            }
            return asks;
        }

        [Test]
        public void Fms_AskedEverySixtySecondsPlusMinusFive()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            var asks = Run(monitor, 10 * 60 + 1);
            Assert.That(asks.Count, Is.EqualTo(10));
            double previous = 0;
            foreach (double at in asks)
            {
                Assert.That(at - previous, Is.InRange(55.0, 65.0));
                previous = at;
            }
        }

        [Test]
        public void Fms_AFrameHitchNeverMakesTwoQuestionsInARow()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            var asks = Run(monitor, 5 * 60, hitchAt: 58.0, hitch: 20.0);
            for (int i = 1; i < asks.Count; i++)
                Assert.That(asks[i] - asks[i - 1], Is.InRange(55.0, 65.0), "question " + i);
        }

        [Test]
        public void Fms_PauseStopsTheClock()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            monitor.Start(0);
            monitor.Tick(30);
            monitor.Pause(30);
            monitor.Tick(200);
            Assert.That(monitor.AskCount, Is.EqualTo(0));
            monitor.Resume(200);
            monitor.Tick(229);
            Assert.That(monitor.AskCount, Is.EqualTo(0));
            monitor.Tick(230);
            Assert.That(monitor.AskCount, Is.EqualTo(1));
        }

        [Test]
        public void Fms_TheQuestionShowsOnTheChip()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            monitor.Start(0);
            monitor.Tick(60);
            Assert.That(prompts.Current.Kind, Is.EqualTo(PromptKind.Fms));
            Assert.That(prompts.Current.ExpireSeconds, Is.EqualTo(ComfortMonitor.PromptSeconds));
        }

        [Test]
        public void Fms_EntryAnswersTheQuestionAndWritesNoEvent()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            monitor.Start(0);
            monitor.Tick(60);
            Assert.That(monitor.Submit(2, 61), Is.True);
            Assert.That(monitor.LastScore, Is.EqualTo(2));
            Assert.That(prompts.Current, Is.Null, "the entry answers the question");
            Assert.That(monitor.AlertActive, Is.False);
        }

        [Test]
        public void Fms_RejectsScoresOutsideZeroToTwenty()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            Assert.That(monitor.Submit(-1, 0), Is.False);
            Assert.That(monitor.Submit(21, 0), Is.False);
            Assert.That(monitor.LastScore, Is.EqualTo(-1));
            Assert.That(monitor.Submit(0, 0), Is.True);
            Assert.That(monitor.Submit(20, 0), Is.True);
        }

        [Test]
        public void Fms_AlertRaisedAtOrAboveTheStopValue_TenOfTenTrials()
        {
            for (int trial = 0; trial < 10; trial++)
            {
                PromptQueue prompts;
                int stop = 4 + trial;
                ComfortMonitor monitor = Make(out prompts, stop);
                int alerts = 0, alertScore = -1, alertStop = -1;
                monitor.Alerted += (s, st) => { alerts++; alertScore = s; alertStop = st; };

                monitor.Submit(stop - 1, 1);
                Assert.That(alerts, Is.EqualTo(0), "below the stop value, trial " + trial);
                monitor.Submit(stop, 2);
                Assert.That(alerts, Is.EqualTo(1), "at the stop value, trial " + trial);
                Assert.That(alertScore, Is.EqualTo(stop));
                Assert.That(alertStop, Is.EqualTo(stop));
                Assert.That(monitor.AlertActive, Is.True);
                Assert.That(prompts.Current.Kind, Is.EqualTo(PromptKind.Stop));
            }
        }

        [Test]
        public void Fms_AlertClearsOnALowerEntryOrOnResume()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            monitor.Start(0);
            monitor.Submit(9, 1);
            Assert.That(monitor.AlertActive, Is.True);
            monitor.Submit(1, 2);
            Assert.That(monitor.AlertActive, Is.False);
            Assert.That(prompts.Current, Is.Null);

            monitor.Submit(9, 3);
            monitor.Resume(4);
            Assert.That(monitor.AlertActive, Is.False);
            Assert.That(prompts.Current, Is.Null);
        }

        [Test]
        public void Fms_StopValueIsAParameterTheConsoleCanChange()
        {
            var registry = new ParamRegistry();
            ParamEntry stop = ComfortMonitor.DeclareStopParam(registry);
            Assert.That(stop.Name, Is.EqualTo("pg.fmsStop"));
            Assert.That(stop.Int, Is.EqualTo(ComfortMonitor.DefaultStop));
            Assert.That(ComfortMonitor.DeclareStopParam(registry), Is.SameAs(stop), "declared twice, kept once");

            var prompts = new PromptQueue(new RecordingSink());
            var monitor = new ComfortMonitor(prompts, ComfortMonitor.StopReader(stop));
            int alerts = 0;
            monitor.Alerted += (s, st) => alerts++;
            monitor.Submit(8, 0);
            Assert.That(alerts, Is.EqualTo(1));
            registry.Set("pg.fmsStop", 10f);
            monitor.Submit(8, 1);
            Assert.That(alerts, Is.EqualTo(1), "the stop value moved to 10");
            monitor.Submit(10, 2);
            Assert.That(alerts, Is.EqualTo(2));
        }

        [Test]
        public void Fms_FeedRoutesOnlyFmsEntries()
        {
            PromptQueue prompts;
            ComfortMonitor monitor = Make(out prompts);
            var feed = new ManualOperatorEntryFeed();
            monitor.Bind(feed, () => 5.0);
            feed.Raise(OperatorEntryKind.Seq, 6);
            Assert.That(monitor.LastScore, Is.EqualTo(-1));
            feed.Raise(OperatorEntryKind.Fms, 3);
            Assert.That(monitor.LastScore, Is.EqualTo(3));
        }
    }
}
