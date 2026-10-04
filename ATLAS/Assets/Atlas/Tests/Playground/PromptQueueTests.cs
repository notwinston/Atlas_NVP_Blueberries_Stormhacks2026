using NUnit.Framework;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C09: one prompt per beat, and the urgent comfort prompts.</summary>
    [TestFixture]
    public class PromptQueueTests
    {
        [Test]
        public void Prompts_OnePerBeat_AllTenBeats()
        {
            var sink = new RecordingSink();
            var queue = new PromptQueue(sink);
            for (int beat = 1; beat <= BeatPrompts.BeatCount; beat++)
            {
                Prompt p = queue.OnBeat(beat, beat);
                Assert.That(p, Is.Not.Null, "beat " + beat);
                Assert.That(queue.Current, Is.SameAs(p));
                Assert.That(queue.Current.Text, Is.EqualTo(BeatPrompts.TextFor(beat)));
            }
            for (int beat = 1; beat <= BeatPrompts.BeatCount; beat++)
                Assert.That(queue.ShownForBeat(beat), Is.EqualTo(1), "beat " + beat);
            Assert.That(sink.Shown.Count, Is.EqualTo(BeatPrompts.BeatCount));
        }

        [Test]
        public void Prompts_EveryBeatHasADistinctLine_OutsideTheTableHasNone()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int beat = 1; beat <= BeatPrompts.BeatCount; beat++)
                Assert.That(seen.Add(BeatPrompts.TextFor(beat)), Is.True);
            Assert.That(BeatPrompts.TextFor(0), Is.Null);
            Assert.That(BeatPrompts.TextFor(11), Is.Null);
            Assert.That(new PromptQueue(null).OnBeat(11, 0), Is.Null);
        }

        [Test]
        public void Prompts_ANewBeatDropsTheOlderBeatPrompt()
        {
            var queue = new PromptQueue(new RecordingSink());
            queue.OnBeat(4, 0);
            queue.OnBeat(5, 1);
            Assert.That(queue.Current.Beat, Is.EqualTo(5));
            Assert.That(queue.WaitingCount, Is.EqualTo(0));
        }

        [Test]
        public void Prompts_ConfirmClearsTheChip()
        {
            var sink = new RecordingSink();
            var queue = new PromptQueue(sink);
            queue.OnBeat(1, 0);
            Assert.That(queue.Confirm(1), Is.True);
            Assert.That(queue.Current, Is.Null);
            Assert.That(sink.Visible, Is.False);
            Assert.That(queue.Confirm(2), Is.False);
        }

        [Test]
        public void Prompts_FmsTakesTheChipAndTheBeatPromptComesBack()
        {
            var queue = new PromptQueue(new RecordingSink());
            queue.OnBeat(7, 0);
            queue.Enqueue(PromptKind.Fms, "fms", 1, 10);
            Assert.That(queue.Current.Kind, Is.EqualTo(PromptKind.Fms));
            Assert.That(queue.WaitingCount, Is.EqualTo(1));
            queue.Confirm(2);
            Assert.That(queue.Current.Kind, Is.EqualTo(PromptKind.Beat));
            Assert.That(queue.Current.Beat, Is.EqualTo(7));
            Assert.That(queue.ShownForBeat(7), Is.EqualTo(2), "shown again after the question; it is the same prompt");
        }

        [Test]
        public void Prompts_FmsExpiresByItself()
        {
            var queue = new PromptQueue(new RecordingSink());
            queue.Enqueue(PromptKind.Fms, "fms", 100, 10);
            queue.Tick(109.9);
            Assert.That(queue.Current, Is.Not.Null);
            queue.Tick(110.0);
            Assert.That(queue.Current, Is.Null);
        }

        [Test]
        public void Prompts_StopOutranksFmsAndConfirmDoesNotClearIt()
        {
            var queue = new PromptQueue(new RecordingSink());
            queue.Enqueue(PromptKind.Fms, "fms", 0, 10);
            queue.Enqueue(PromptKind.Stop, "stop", 1);
            Assert.That(queue.Current.Kind, Is.EqualTo(PromptKind.Stop));
            Assert.That(queue.Confirm(2), Is.False);
            Assert.That(queue.Current.Kind, Is.EqualTo(PromptKind.Stop));
            // A later question waits behind the stop line.
            queue.Enqueue(PromptKind.Fms, "fms2", 3, 10);
            Assert.That(queue.Current.Kind, Is.EqualTo(PromptKind.Stop));
            Assert.That(queue.Clear(PromptKind.Stop, 4), Is.True);
            Assert.That(queue.Current.Kind, Is.EqualTo(PromptKind.Fms));
        }
    }
}
