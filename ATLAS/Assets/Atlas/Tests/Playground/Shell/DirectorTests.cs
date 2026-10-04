using System.Collections.Generic;
using Atlas.Blades.Playground.Shell;
using Atlas.Link;
using NUnit.Framework;
#if !ATLAS_HEADLESS
using UnityEditor;
#endif

namespace Atlas.Blades.Playground.Tests.ShellFlow
{
    /// <summary>P2-C01: the director's state machine, by condition and by SceneCmd, and the ten pg.beat events.</summary>
    public class DirectorTests
    {
        sealed class FlagStep : IBeatStep
        {
            readonly PlaygroundBeat m_Beat;
            public bool Done;
            public int Enters;
            public FlagStep(PlaygroundBeat beat) { m_Beat = beat; }
            public PlaygroundBeat Beat { get { return m_Beat; } }
            public void Enter() { Enters++; Done = false; }
            public bool Complete { get { return Done; } }
        }

        static FlagStep[] Steps(PlaygroundDirectorCore director)
        {
            var steps = new FlagStep[PlaygroundBeats.Count + 1];
            for (int i = 1; i <= PlaygroundBeats.Count; i++)
            {
                steps[i] = new FlagStep((PlaygroundBeat)i);
                director.SetStep(steps[i]);
            }
            return steps;
        }

        [Test]
        public void AllTenBeatsAdvanceByCondition_AndLogTenEvents()
        {
            var log = new RecordingEvents();
            var director = new PlaygroundDirectorCore(log, null);
            FlagStep[] steps = Steps(director);
            bool finished = false;
            director.RunFinished += () => finished = true;

            director.Begin(0.0);
            for (int beat = 1; beat <= 10; beat++)
            {
                Assert.That((int)director.Beat, Is.EqualTo(beat));
                director.Update(beat);
                Assert.That((int)director.Beat, Is.EqualTo(beat), "no condition yet: the beat stays");
                steps[beat].Done = true;
                director.Update(beat + 0.5);
            }
            Assert.That(finished, Is.True);
            Assert.That(director.Finished, Is.True);
            Assert.That(director.Running, Is.False);
            Assert.That(log.Count("pg.beat"), Is.EqualTo(10));
            for (int beat = 1; beat <= 10; beat++)
                Assert.That(log.Events[beat - 1].Json,
                    Is.EqualTo("{\"beat\":" + beat + ",\"name\":\"" + PlaygroundBeats.Name((PlaygroundBeat)beat) + "\"}"));
        }

        [Test]
        public void AConditionThatAlreadyHoldsAdvancesOneBeatPerUpdate()
        {
            var director = new PlaygroundDirectorCore(null, null);
            FlagStep[] steps = Steps(director);
            steps[1].Done = true;
            director.Begin(0.0);
            // Enter() on a FlagStep clears Done; set it again as the pre-scan fast path does.
            steps[1].Done = true;
            director.Update(0.0);
            Assert.That(director.Beat, Is.EqualTo(PlaygroundBeat.HomeAppears));
        }

        [Test]
        public void SceneCmdAdvancesAndJumps()
        {
            var log = new RecordingEvents();
            var director = new PlaygroundDirectorCore(log, null);
            Steps(director);
            director.Begin(0.0);

            var next = OperatorCommand.SwitchSpace(0, "next");
            Assert.That(director.OnOperatorCommand(in next, 1.0), Is.True);
            Assert.That(director.Beat, Is.EqualTo(PlaygroundBeat.HomeAppears));

            var jump = OperatorCommand.SwitchSpace(0, null, JsonValue.Object().Set("beat", 7));
            Assert.That(director.OnOperatorCommand(in jump, 2.0), Is.True);
            Assert.That(director.Beat, Is.EqualTo(PlaygroundBeat.FreeCurvedWalk));

            var byVariant = OperatorCommand.SwitchSpace(0, "beat:3");
            Assert.That(director.OnOperatorCommand(in byVariant, 3.0), Is.True);
            Assert.That(director.Beat, Is.EqualTo(PlaygroundBeat.HeadsetOn));
            Assert.That(log.Count("pg.beat"), Is.EqualTo(4));
        }

        [Test]
        public void SceneCmdWalksAllTenBeatsAndTheRunEnds()
        {
            var log = new RecordingEvents();
            var director = new PlaygroundDirectorCore(log, null);
            Steps(director);
            director.Begin(0.0);
            var next = OperatorCommand.SwitchSpace(0, "next");
            for (int i = 0; i < 9; i++) director.OnOperatorCommand(in next, i);
            Assert.That(director.Beat, Is.EqualTo(PlaygroundBeat.ReturnHome));
            director.OnOperatorCommand(in next, 9.0);
            Assert.That(director.Finished, Is.True);
            Assert.That(log.Count("pg.beat"), Is.EqualTo(10));
        }

        [Test]
        public void OtherCommandsAreNotTheDirectors()
        {
            var director = new PlaygroundDirectorCore(null, null);
            Steps(director);
            director.Begin(0.0);
            var toS2 = OperatorCommand.SwitchSpace(2, "next");
            var noVariant = OperatorCommand.SwitchSpace(0);
            var home = OperatorCommand.Home();
            var badBeat = OperatorCommand.SwitchSpace(0, "beat:11");
            var badArg = OperatorCommand.SwitchSpace(0, null, JsonValue.Object().Set("beat", 0));
            Assert.That(director.OnOperatorCommand(in toS2, 1.0), Is.False);
            Assert.That(director.OnOperatorCommand(in noVariant, 1.0), Is.False);
            Assert.That(director.OnOperatorCommand(in home, 1.0), Is.False);
            Assert.That(director.OnOperatorCommand(in badBeat, 1.0), Is.False);
            Assert.That(director.OnOperatorCommand(in badArg, 1.0), Is.False);
            Assert.That(director.Beat, Is.EqualTo(PlaygroundBeat.Scan));
        }

        [Test]
        public void EachEntryShowsOneBeatPrompt_AndTheNamesCanComeFromTheAsset()
        {
            var sink = new RecordingSink();
            var prompts = new PromptQueue(sink);
            var names = new string[PlaygroundBeats.Count + 1];
            names[4] = "disc";
            var log = new RecordingEvents();
            var director = new PlaygroundDirectorCore(log, prompts, names);
            Steps(director);
            director.Begin(0.0);
            var next = OperatorCommand.SwitchSpace(0, "next");
            for (int i = 1; i <= 9; i++) director.OnOperatorCommand(in next, i);
            for (int beat = 1; beat <= 10; beat++) Assert.That(prompts.ShownForBeat(beat), Is.EqualTo(1), "beat " + beat);
            Assert.That(sink.Shown[3].Text, Is.EqualTo(BeatPrompts.TextFor(4)));
            Assert.That(log.Events[3].Json, Is.EqualTo("{\"beat\":4,\"name\":\"disc\"}"));
            Assert.That(log.Events[4].Json, Does.Contain("\"name\":\"dial\""), "a missing name takes the default");
        }

        [Test]
        public void LeavingFiresForEveryBeatWithTheNextOne()
        {
            var director = new PlaygroundDirectorCore(null, null);
            FlagStep[] steps = Steps(director);
            var left = new List<string>();
            director.BeatLeft += (from, to) => left.Add((int)from + ">" + (int)to);
            director.Begin(0.0);
            for (int beat = 1; beat <= 10; beat++) { steps[beat].Done = true; director.Update(beat); }
            Assert.That(left, Is.EqualTo(new[] { "1>2", "2>3", "3>4", "4>5", "5>6", "6>7", "7>8", "8>9", "9>10", "10>0" }));
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void BeatsAssetHoldsTheTenBeatsOfThePlan()
        {
            var table = AssetDatabase.LoadAssetAtPath<PlaygroundBeatTable>("Assets/Atlas/Blades/Playground/Shell/Director/Beats.asset");
            Assert.That(table, Is.Not.Null);
            Assert.That(table.IsComplete(), Is.True);
            string[] names = table.Names();
            for (int beat = 1; beat <= PlaygroundBeats.Count; beat++)
                Assert.That(names[beat], Is.EqualTo(PlaygroundBeats.Name((PlaygroundBeat)beat)));
        }
#endif
    }
}
