using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>The app as the tour's core sees it: spaces switch after a delay, tiers after a delay, lines go to a list.</summary>
    sealed class FakeTourDriver : IPerfTourDriver
    {
        public readonly StringBuilder File = new StringBuilder();
        public readonly List<string> Events = new List<string>();
        public readonly List<string> Calls = new List<string>();
        public readonly HashSet<SpaceId> Refused = new HashSet<SpaceId>();
        public int RoomReadyAfterCalls = 2;
        public bool RoomNever;
        public bool HasSpace;
        public SpaceId Current;
        public int Tier;
        public int ForcedTier = -1;
        public bool TierStuck;
        int _roomCalls;

        public bool PrepareRoom()
        {
            if (RoomNever) return false;
            return ++_roomCalls >= RoomReadyAfterCalls;
        }

        public void ApplyVariant(PerfVariant variant) { Calls.Add("variant " + variant); }

        public bool InSpace(SpaceId space) { return HasSpace && Current == space; }

        public bool RequestSpace(SpaceId space)
        {
            Calls.Add("request " + SpaceIds.Code(space));
            if (Refused.Contains(space)) return false;
            HasSpace = true;
            Current = space;
            return true;
        }

        public void ForceTier(int tier)
        {
            Calls.Add("tier " + tier);
            ForcedTier = tier;
        }

        /// <summary>What the ladder does at the end of a frame: a forced tier is in force from the next one.</summary>
        public void EndFrame()
        {
            if (ForcedTier >= 0 && !TierStuck) Tier = ForcedTier;
        }

        public void WriteLines(string text) { File.Append(text); }
        public void LogEvent(string ev, string dataJson) { Events.Add(ev + " " + dataJson); }

        public string[] Lines()
        {
            return File.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public List<string> DataRows()
        {
            var rows = new List<string>();
            foreach (string line in Lines())
                if (!line.StartsWith("#") && line != PerfTourCsv.Header) rows.Add(line);
            return rows;
        }
    }

    [TestFixture]
    public sealed class PerfTourScheduleTests
    {
        static bool Built(SpaceId id)
        {
            return id == SpaceId.Home || id == SpaceId.Glued || id == SpaceId.Portal || id == SpaceId.Slicer;
        }

        [Test]
        public void PerfTour_Schedule_VisitsEveryBuiltSpaceAndBothGluedLayouts()
        {
            PerfSchedule tour = PerfSchedule.Tour(30f, Built);
            Assert.That(tour.Kind, Is.EqualTo(PerfRunKind.Tour));
            Assert.That(tour.Aggregate, Is.False);
            var labels = new List<string>();
            foreach (PerfStep step in tour.Steps)
            {
                labels.Add(step.Label);
                Assert.That(step.Kind, Is.EqualTo(PerfStepKind.Visit));
                Assert.That(step.Seconds, Is.EqualTo(30f));
            }
            Assert.That(labels, Is.EqualTo(new[]
            {
                "S0 Home", "S1 Glued 5x5", "S1 Glued 3x3x3", "S5 Impossible Rooms", "S6 Slicer"
            }));
            Assert.That(tour.Steps[1].Variant, Is.EqualTo(PerfVariant.Glued5x5));
            Assert.That(tour.Steps[2].Variant, Is.EqualTo(PerfVariant.GluedT3));
            Assert.That(tour.TotalSeconds, Is.EqualTo(5 * (30f + PerfSchedule.DefaultSettleSeconds)).Within(1e-3));

            string notes = string.Join("\n", tour.Notes);
            Assert.That(notes, Does.Contain("S2 Curved Walk is not in this build"));
            Assert.That(notes, Does.Contain("S3 Aligned Cells is not in this build"));
            Assert.That(notes, Does.Contain("S4 Small Planet is not in this build"));
        }

        [Test]
        public void PerfTour_Schedule_WithEverySpaceBuiltHasEightRows()
        {
            PerfSchedule tour = PerfSchedule.Tour(1f, null);
            Assert.That(tour.Steps.Length, Is.EqualTo(8), "S0 to S6, S1 twice");
            Assert.That(tour.Steps[0].Seconds, Is.EqualTo(PerfSchedule.MinVisitSeconds), "a visit is never shorter than two seconds");
        }

        [Test]
        public void PerfTour_Schedule_SoakAlternatesGluedAndHomeForFifteenMinutes()
        {
            PerfSchedule soak = PerfSchedule.Soak(PerfRunKind.Soak, 900f, 60f, Built);
            Assert.That(soak.Aggregate, Is.True);
            Assert.That(soak.Steps.Length, Is.EqualTo(15));
            Assert.That(soak.IntervalSeconds, Is.EqualTo(60f));
            Assert.That(soak.TotalSeconds, Is.EqualTo(900f).Within(1e-2));
            for (int i = 0; i < soak.Steps.Length; i++)
            {
                PerfStep step = soak.Steps[i];
                Assert.That(step.Space, Is.EqualTo((i & 1) == 0 ? SpaceId.Glued : SpaceId.Home), "step " + i);
                Assert.That(step.Variant, Is.EqualTo((i & 1) == 0 ? PerfVariant.Glued5x5 : PerfVariant.None));
                Assert.That(step.Seconds, Is.EqualTo(60f - PerfSchedule.DefaultSettleSeconds));
            }
            string notes = string.Join("\n", soak.Notes);
            Assert.That(notes, Does.Contain("S2 Curved Walk is not in this build"));
            Assert.That(notes, Does.Contain("every 60 s"));
            Assert.That(notes, Does.Contain("LiDAR depth on"));
            Assert.That(notes, Does.Not.Contain("rehearsal"));
        }

        [Test]
        public void PerfTour_Schedule_SoakUsesCurvedWalkOnceItIsBuilt()
        {
            PerfSchedule soak = PerfSchedule.Soak(PerfRunKind.Soak, 900f, 60f, null);
            Assert.That(soak.Steps[0].Space, Is.EqualTo(SpaceId.Glued));
            Assert.That(soak.Steps[1].Space, Is.EqualTo(SpaceId.CurvedWalk));
            Assert.That(string.Join("\n", soak.Notes), Does.Not.Contain("not in this build"));
        }

        [Test]
        public void PerfTour_Schedule_ShortSoakIsCutToItsLengthAndCalledARehearsal()
        {
            PerfSchedule soak = PerfSchedule.Soak(PerfRunKind.SoakDepthOff, 100f, 60f, Built);
            Assert.That(soak.Kind, Is.EqualTo(PerfRunKind.SoakDepthOff));
            Assert.That(soak.Steps.Length, Is.EqualTo(2));
            Assert.That(soak.Steps[1].Seconds, Is.EqualTo(40f - PerfSchedule.DefaultSettleSeconds).Within(1e-3));
            Assert.That(soak.TotalSeconds, Is.EqualTo(100f).Within(1e-3));
            string notes = string.Join("\n", soak.Notes);
            Assert.That(notes, Does.Contain("rehearsal"));
            Assert.That(notes, Does.Contain("LiDAR depth off"));
        }

        [Test]
        public void PerfTour_Schedule_LadderForcesEveryTierAsAChange()
        {
            PerfSchedule ladder = PerfSchedule.Ladder();
            Assert.That(ladder.Steps[0].Kind, Is.EqualTo(PerfStepKind.Enter));
            Assert.That(ladder.Steps[0].Space, Is.EqualTo(SpaceId.Home));
            var tiers = new List<int>();
            for (int i = 1; i < ladder.Steps.Length; i++)
            {
                Assert.That(ladder.Steps[i].Kind, Is.EqualTo(PerfStepKind.ForceTier));
                tiers.Add(ladder.Steps[i].Tier);
            }
            Assert.That(tiers, Is.EqualTo(new[] { 1, 2, 3, 4, 0 }));
            Assert.That(ladder.Steps[3].Label, Is.EqualTo("Critical"));
        }

        [Test]
        public void PerfTour_Options_ParseTheCommandLine()
        {
            PerfTourOptions o = PerfTourOptions.Parse(new[]
            {
                "atlas", "-atlasPerf", "SOAK", "-atlasPerfVisit", "20", "-atlasPerfSoakMinutes", "5", "-atlasPerfInterval", "45",
                "-atlasPerfPose", "replay", "-atlasPerfQuit"
            });
            Assert.That(o.Run, Is.EqualTo(PerfRunKind.Soak));
            Assert.That(o.VisitSeconds, Is.EqualTo(20));
            Assert.That(o.SoakMinutes, Is.EqualTo(5));
            Assert.That(o.IntervalSeconds, Is.EqualTo(45));
            Assert.That(o.Pose, Is.EqualTo((int)PerfPoseChoice.Replay));
            Assert.That(o.Quit, Is.True);

            PerfTourOptions none = PerfTourOptions.Parse(new[] { "-atlasPerf", "nonsense", "-atlasPerfVisit", "x" });
            Assert.That(none.Run, Is.EqualTo(PerfRunKind.None));
            Assert.That(none.VisitSeconds, Is.EqualTo(0));
            Assert.That(PerfTourOptions.Parse(null).Run, Is.EqualTo(PerfRunKind.None));
            Assert.That(PerfTourOptions.Parse(new[] { "-atlasPerf", "soakNoDepth" }).Run, Is.EqualTo(PerfRunKind.SoakDepthOff));
            for (int i = 0; i < PerfTourOptions.RunNames.Length; i++)
                Assert.That(PerfSchedule.RunName((PerfRunKind)i), Is.EqualTo(PerfTourOptions.RunNames[i]));
        }

        [Test]
        public void PerfTour_Options_DepthOffNeverFollowsATrackedHead()
        {
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.Tour, PerfPoseChoice.Auto, true, true), Is.EqualTo(PerfPoseChoice.Synthetic));
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.Soak, PerfPoseChoice.Auto, true, true), Is.EqualTo(PerfPoseChoice.Tracked));
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.Soak, PerfPoseChoice.Auto, false, true), Is.EqualTo(PerfPoseChoice.Synthetic));
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.SoakDepthOff, PerfPoseChoice.Auto, true, true), Is.EqualTo(PerfPoseChoice.Replay));
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.SoakDepthOff, PerfPoseChoice.Tracked, true, true), Is.EqualTo(PerfPoseChoice.Replay));
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.SoakDepthOff, PerfPoseChoice.Tracked, true, false), Is.EqualTo(PerfPoseChoice.Synthetic));
            Assert.That(PerfTourOptions.ResolvePose(PerfRunKind.Tour, PerfPoseChoice.Tracked, true, true), Is.EqualTo(PerfPoseChoice.Tracked));
        }
    }

    [TestFixture]
    public sealed class PerfTourStatsTests
    {
        static PerfSummary Good()
        {
            PerfSummary s = PerfSummary.Empty;
            s.Frames = 1800;
            s.Seconds = 30f;
            s.P50 = 16.67f; s.P95 = 17.1f; s.P99 = 18.4f; s.Max = 21f;
            s.Over17_2 = 1.2f; s.Over33_8 = 0f;
            s.FpsMean = 59.8f; s.FpsMin = 58f;
            s.DrawCalls = 126; s.SetPass = 14; s.Triangles = 148000;
            s.CpuMainP95 = 5.2f; s.CpuRenderP95 = 3.1f; s.GpuP95 = 6.4f;
            s.MemMB = 640; s.GcAllocB = 0; s.GcAllocMaxB = 0;
            return s;
        }

        [Test]
        public void PerfTour_Stats_HistogramGivesNearestRankPercentiles()
        {
            var h = new PerfHistogram();
            Assert.That(h.Percentile(0.95f), Is.EqualTo(0f), "empty");
            for (int i = 1; i <= 100; i++) h.Add(i);
            Assert.That(h.Count, Is.EqualTo(100));
            Assert.That(h.Percentile(0.50f), Is.EqualTo(50f).Within(1e-3));
            Assert.That(h.Percentile(0.95f), Is.EqualTo(95f).Within(1e-3));
            Assert.That(h.Percentile(0.99f), Is.EqualTo(99f).Within(1e-3));
            Assert.That(h.Percentile(1f), Is.EqualTo(100f).Within(1e-3));
            Assert.That(h.Max, Is.EqualTo(100f));
        }

        [Test]
        public void PerfTour_Stats_HistogramNeverReportsATimeLowerThanItWas()
        {
            var h = new PerfHistogram();
            h.Add(16.667f);
            Assert.That(h.Percentile(0.5f), Is.EqualTo(16.67f).Within(1e-4), "rounded up to the hundredth");
            h.Clear();
            h.Add(16.7f);
            Assert.That(h.Percentile(0.5f), Is.EqualTo(16.70f).Within(1e-4), "a whole hundredth stays in its bin");
            h.Clear();
            h.Add(20.004f);
            Assert.That(h.Percentile(0.5f), Is.GreaterThan(20f));
            h.Clear();
            h.Add(250f);
            h.Add(float.NaN);
            h.Add(-1f);
            Assert.That(h.Count, Is.EqualTo(3));
            Assert.That(h.Percentile(0.5f), Is.EqualTo(0f), "no time reported counts as zero");
            Assert.That(h.Percentile(1f), Is.EqualTo(250f), "over 100 ms the exact maximum is given");
            Assert.That(h.Measured, Is.True);
            h.Clear();
            h.Add(0f);
            Assert.That(h.Measured, Is.False);
        }

        [Test]
        public void PerfTour_Stats_SummaryOfSyntheticSamples()
        {
            var stats = new PerfStats();
            // 1,000 frames: 940 on time, 50 one interval late, 10 two intervals late.
            for (int i = 0; i < 940; i++) stats.AddFrame(16.6f, 4f, 3f, 5f, 0.0166f);
            for (int i = 0; i < 50; i++) stats.AddFrame(18f, 9f, 3f, 5f, 0.018f);
            for (int i = 0; i < 10; i++) stats.AddFrame(40f, 9f, 7f, 12f, 0.04f);
            stats.AddSecond(60f, 120, 14, 140000, 600, 0);
            stats.AddSecond(55f, 126, 15, 148000, 612, 2048);
            stats.AddSecond(59f, 110, 12, 100000, 605, 0);
            stats.AddThermal(0);
            stats.AddThermal(1);
            stats.AddThermal(0);

            PerfSummary s = PerfSummary.Empty;
            stats.Fill(ref s);
            Assert.That(s.Frames, Is.EqualTo(1000));
            Assert.That(s.Seconds, Is.EqualTo(940 * 0.0166 + 50 * 0.018 + 10 * 0.04).Within(1e-2));
            Assert.That(s.P50, Is.EqualTo(16.6f).Within(1e-3));
            Assert.That(s.P95, Is.EqualTo(18f).Within(1e-3));
            Assert.That(s.P99, Is.EqualTo(18f).Within(1e-3), "rank 990 is the last frame of 18 ms");
            Assert.That(s.Max, Is.EqualTo(40f));
            Assert.That(s.Over17_2, Is.EqualTo(6f).Within(1e-4));
            Assert.That(s.Over33_8, Is.EqualTo(1f).Within(1e-4));
            Assert.That(s.FpsMean, Is.EqualTo(58f).Within(1e-3));
            Assert.That(s.FpsMin, Is.EqualTo(55f));
            Assert.That(s.DrawCalls, Is.EqualTo(126));
            Assert.That(s.SetPass, Is.EqualTo(15));
            Assert.That(s.Triangles, Is.EqualTo(148000));
            Assert.That(s.MemMB, Is.EqualTo(612));
            Assert.That(s.GcAllocB, Is.EqualTo(683), "the mean of the seconds' worst frames");
            Assert.That(s.GcAllocMaxB, Is.EqualTo(2048));
            Assert.That(s.CpuMainP95, Is.EqualTo(9f).Within(1e-3));
            Assert.That(s.CpuRenderP95, Is.EqualTo(3f).Within(1e-3));
            Assert.That(s.GpuP95, Is.EqualTo(5f).Within(1e-3));
            Assert.That(s.ThermalMax, Is.EqualTo(1));

            stats.Clear();
            PerfSummary empty = PerfSummary.Empty;
            stats.Fill(ref empty);
            Assert.That(empty.Frames, Is.EqualTo(0));
            Assert.That(empty.P95, Is.EqualTo(0f));
            Assert.That(empty.FpsMean, Is.EqualTo(0f));
        }

        [Test]
        public void PerfTour_Stats_FrameRateFallsBackToFramesOverTime()
        {
            var stats = new PerfStats();
            for (int i = 0; i < 120; i++) stats.AddFrame(16.6f, 1f, 1f, 1f, 1f / 60f);
            PerfSummary s = PerfSummary.Empty;
            stats.Fill(ref s);
            Assert.That(s.FpsMean, Is.EqualTo(60f).Within(0.1f));
            Assert.That(s.FpsMin, Is.EqualTo(s.FpsMean));
        }

        [Test]
        public void PerfTour_Must_AGoodSpacePasses()
        {
            PerfSummary s = Good();
            PerfMust.JudgeSpace(ref s, true, new StringBuilder());
            Assert.That(s.Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(s.Failed, Is.Empty);
            Assert.That(s.Unmeasured, Is.Empty);
        }

        [Test]
        public void PerfTour_Must_ValuesOnTheLimitPass()
        {
            PerfSummary s = Good();
            s.P50 = 16.7f; s.P95 = 20f; s.P99 = 33.3f; s.Over17_2 = 5f; s.Over33_8 = 0.5f;
            s.DrawCalls = 300; s.SetPass = 60; s.Triangles = 500000;
            s.CpuMainP95 = 8f; s.CpuRenderP95 = 6f; s.GpuP95 = 9f; s.MemMB = 1536; s.GcAllocB = 1024;
            PerfMust.JudgeSpace(ref s, true, new StringBuilder());
            Assert.That(s.Result, Is.EqualTo(PerfMust.Pass), s.Failed);
        }

        [TestCase("p50")]
        [TestCase("p95")]
        [TestCase("p99")]
        [TestCase("over17_2")]
        [TestCase("over33_8")]
        [TestCase("drawCalls")]
        [TestCase("setPass")]
        [TestCase("tris")]
        [TestCase("cpuMain")]
        [TestCase("cpuRender")]
        [TestCase("gpu")]
        [TestCase("mem")]
        [TestCase("gcAlloc")]
        [TestCase("notCold")]
        public void PerfTour_Must_EachRowOverItsLimitFails(string row)
        {
            PerfSummary s = Good();
            switch (row)
            {
                case "p50": s.P50 = 16.71f; break;
                case "p95": s.P95 = 20.01f; break;
                case "p99": s.P99 = 33.31f; break;
                case "over17_2": s.Over17_2 = 5.1f; break;
                case "over33_8": s.Over33_8 = 0.6f; break;
                case "drawCalls": s.DrawCalls = 301; break;
                case "setPass": s.SetPass = 61; break;
                case "tris": s.Triangles = 500001; break;
                case "cpuMain": s.CpuMainP95 = 8.01f; break;
                case "cpuRender": s.CpuRenderP95 = 6.01f; break;
                case "gpu": s.GpuP95 = 9.01f; break;
                case "mem": s.MemMB = 1537; break;
                case "gcAlloc": s.GcAllocB = 1025; break;
                case "notCold": s.ThermalMax = 1; break;
            }
            PerfMust.JudgeSpace(ref s, true, new StringBuilder());
            Assert.That(s.Result, Is.EqualTo(PerfMust.Fail));
            Assert.That(s.Failed, Is.EqualTo(row));
        }

        [Test]
        public void PerfTour_Must_SeveralFailuresAreJoined()
        {
            PerfSummary s = Good();
            s.P95 = 25f;
            s.DrawCalls = 400;
            s.GpuP95 = 11f;
            PerfMust.JudgeSpace(ref s, true, new StringBuilder());
            Assert.That(s.Failed, Is.EqualTo("p95|drawCalls|gpu"));
        }

        [Test]
        public void PerfTour_Must_WhatThePlatformDoesNotReportCannotPass()
        {
            // The Editor: frame times from the frame's delta, no thread times.
            PerfSummary s = Good();
            s.CpuMainP95 = 0f; s.CpuRenderP95 = 0f; s.GpuP95 = 0f;
            PerfMust.JudgeSpace(ref s, true, new StringBuilder());
            Assert.That(s.Result, Is.EqualTo(PerfMust.Incomplete));
            Assert.That(s.Failed, Is.Empty);
            Assert.That(s.Unmeasured, Is.EqualTo("cpuMain|cpuRender|gpu"));

            s.P95 = 30f;
            PerfMust.JudgeSpace(ref s, true, new StringBuilder());
            Assert.That(s.Result, Is.EqualTo(PerfMust.Fail), "a failure outranks a missing value");

            PerfSummary none = PerfSummary.Empty;
            PerfMust.JudgeSpace(ref none, true, new StringBuilder());
            Assert.That(none.Result, Is.EqualTo(PerfMust.Fail));
            Assert.That(none.Failed, Is.EqualTo("noFrames"));
        }

        [Test]
        public void PerfTour_Must_AWarmSoakRowGetsTheHotGpuLimit()
        {
            PerfSummary s = Good();
            s.GpuP95 = 12f;
            s.ThermalMax = 1;
            PerfMust.JudgeSpace(ref s, false, new StringBuilder());
            Assert.That(s.Result, Is.EqualTo(PerfMust.Pass), "14 ms once the phone has left Nominal");
            s.GpuP95 = 14.5f;
            PerfMust.JudgeSpace(ref s, false, new StringBuilder());
            Assert.That(s.Failed, Is.EqualTo("gpu"));
            s.GpuP95 = 12f;
            s.ThermalMax = 0;
            PerfMust.JudgeSpace(ref s, false, new StringBuilder());
            Assert.That(s.Failed, Is.EqualTo("gpu"), "a soak row that stayed Nominal keeps the cold limit");
        }

        [Test]
        public void PerfTour_Must_SoakRule()
        {
            var scratch = new StringBuilder();
            PerfSummary s = Good();
            s.TimeToFair = 240f;
            s.TimeToSerious = 700f;
            s.ThermalMax = 2;
            PerfMust.JudgeSoak(ref s, 900f, scratch);
            Assert.That(s.Result, Is.EqualTo(PerfMust.Pass), "Serious after ten minutes is allowed");

            s.TimeToSerious = 599f;
            PerfMust.JudgeSoak(ref s, 900f, scratch);
            Assert.That(s.Failed, Is.EqualTo("seriousEarly"));

            s.TimeToSerious = -1f;
            s.TimeToCritical = 800f;
            PerfMust.JudgeSoak(ref s, 900f, scratch);
            Assert.That(s.Failed, Is.EqualTo("critical"));

            s = Good();
            s.FpsMean = 44.9f;
            s.FpsMin = 29f;
            PerfMust.JudgeSoak(ref s, 900f, scratch);
            Assert.That(s.Failed, Is.EqualTo("fpsMean|fpsMin"));

            s = Good();
            PerfMust.JudgeSoak(ref s, 120f, scratch);
            Assert.That(s.Result, Is.EqualTo(PerfMust.Incomplete), "a short soak is a rehearsal");
            Assert.That(s.Unmeasured, Is.EqualTo("short"));
        }

        [Test]
        public void PerfTour_Must_AForcedTierHasOneSecond()
        {
            PerfSummary s = PerfSummary.Empty;
            s.EffectMs = 16.7f;
            PerfMust.JudgeTier(ref s);
            Assert.That(s.Result, Is.EqualTo(PerfMust.Pass));
            s.EffectMs = 1000.5f;
            PerfMust.JudgeTier(ref s);
            Assert.That(s.Failed, Is.EqualTo("slow"));
            s.EffectMs = -1f;
            PerfMust.JudgeTier(ref s);
            Assert.That(s.Failed, Is.EqualTo("noEffect"));
            Assert.That(s.Result, Is.EqualTo(PerfMust.Fail));
        }

        [Test]
        public void PerfTour_Csv_RowAndHeaderHaveTheSameColumns()
        {
            Assert.That(PerfTourCsv.Header.Split(',').Length, Is.EqualTo(PerfTourCsv.ColumnCount));

            PerfSummary s = Good();
            s.Run = "tour"; s.Kind = "space"; s.Label = "S1 Glued 5x5"; s.Space = "S1"; s.Variant = "5x5";
            s.Room = "budget_room"; s.Pose = "synthetic"; s.Depth = "on";
            s.Result = "pass";
            var sb = new StringBuilder();
            PerfTourCsv.AppendRow(sb, in s);
            Assert.That(sb.ToString(), Is.EqualTo(
                "tour,space,S1 Glued 5x5,S1,5x5,budget_room,synthetic,on,30.0,1800,16.670,17.100,18.400,21.000,1.200,0.000," +
                "59.800,58.000,126,14,148000,5.200,3.100,6.400,640,0,0,0,-1.0,-1.0,-1.0,-1,-1.0,pass,,\n"));
            Assert.That(sb.ToString().TrimEnd('\n').Split(',').Length, Is.EqualTo(PerfTourCsv.ColumnCount));
        }

        [Test]
        public void PerfTour_Csv_TextNeverBreaksTheLine()
        {
            PerfSummary s = PerfSummary.Empty;
            s.Label = "a,b\"c\nd";
            var sb = new StringBuilder();
            PerfTourCsv.AppendRow(sb, in s);
            string line = sb.ToString();
            Assert.That(line.Split(',').Length, Is.EqualTo(PerfTourCsv.ColumnCount));
            Assert.That(line.IndexOf('\n'), Is.EqualTo(line.Length - 1));
            sb.Length = 0;
            PerfTourCsv.AppendFact(sb, "note", "one\ntwo");
            Assert.That(sb.ToString(), Is.EqualTo("# note=one two\n"));
        }

        [Test]
        public void PerfTour_Csv_TheEventCarriesTheSameMembers()
        {
            PerfSummary s = Good();
            s.Run = "soak"; s.Kind = "total"; s.Label = "soak"; s.Result = "fail"; s.Failed = "fpsMin|critical";
            s.TimeToFair = 312.5f;
            var sb = new StringBuilder();
            PerfTourCsv.AppendJson(sb, in s);
            JsonValue json = AtlasJson.Parse(sb.ToString());
            Assert.That(json.IsObject, Is.True);
            string[] columns = PerfTourCsv.Header.Split(',');
            Assert.That(json.Count, Is.EqualTo(columns.Length));
            for (int i = 0; i < columns.Length; i++) Assert.That(json.KeyAt(i), Is.EqualTo(columns[i]));
            Assert.That(json["failed"].AsString(), Is.EqualTo("fpsMin|critical"));
            Assert.That(json["tFairS"].AsDouble(), Is.EqualTo(312.5).Within(1e-6));
            Assert.That(json["drawCalls"].AsLong(), Is.EqualTo(126));
            Assert.That(sb.Length, Is.LessThan(2000), "an event's data is at most 2 kB");
        }

        [Test]
        public void PerfTour_Csv_TheSampleFileOfTheSummaryToolHasThisHeader()
        {
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            string sample = Path.Combine(project, "..", "tools", "perf", "sample", "tour_sample.csv");
            Assert.That(File.Exists(sample), Is.True, sample);
            string[] lines = File.ReadAllLines(sample);
            Assert.That(lines[0], Is.EqualTo(PerfTourCsv.Magic));
            Assert.That(Array.IndexOf(lines, PerfTourCsv.Header), Is.GreaterThan(0), "tools/perf/summarise.py reads these columns");
        }
    }

    [TestFixture]
    public sealed class PerfTourCoreTests
    {
        const float Dt = 1f / 60f;

        static PerfRunInfo Info()
        {
            return new PerfRunInfo
            {
                Session = "20261004T153012Z-a3f9", Room = "budget_room", Pose = "synthetic", Depth = "on",
                Device = "test", Build = "0.1",
            };
        }

        // Plays a run at 60 frames per second with one row of the probe per second, as the adapter feeds it.
        static double Play(PerfTourCore core, FakeTourDriver driver, double time, double seconds,
            Func<double, int> thermal = null, float frameMs = 16.6f)
        {
            int frames = (int)Math.Round(seconds * 60.0);
            for (int i = 0; i < frames && core.Running; i++)
            {
                time += Dt;
                if (i % 60 == 59) core.AddSecond(60f, 120, 14, 148000, 640, 0);
                core.AddFrame(frameMs, 4f, 3f, 5f, Dt);
                core.Tick(time, thermal != null ? thermal(time) : 0, driver.Tier);
                driver.EndFrame();
            }
            return time;
        }

        [Test]
        public void PerfTour_Core_TourWritesOneRowPerSpace()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            bool finished = false, aborted = true;
            core.Finished += a => { finished = true; aborted = a; };
            PerfSchedule tour = PerfSchedule.Tour(5f, id => id == SpaceId.Home || id == SpaceId.Glued || id == SpaceId.Slicer, 1f);

            core.Begin(tour, Info(), 100.0);
            Assert.That(core.Running, Is.True);
            Assert.That(core.Phase, Is.EqualTo(PerfTourPhase.Preparing));
            Play(core, driver, 100.0, 60.0);

            Assert.That(finished, Is.True);
            Assert.That(aborted, Is.False);
            Assert.That(core.Running, Is.False);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(core.Rows.Count, Is.EqualTo(4));
            Assert.That(core.Rows[0].Label, Is.EqualTo("S0 Home"));
            Assert.That(core.Rows[1].Label, Is.EqualTo("S1 Glued 5x5"));
            Assert.That(core.Rows[2].Label, Is.EqualTo("S1 Glued 3x3x3"));
            Assert.That(core.Rows[3].Label, Is.EqualTo("S6 Slicer"));
            foreach (PerfSummary row in core.Rows)
            {
                Assert.That(row.Kind, Is.EqualTo("space"));
                Assert.That(row.Frames, Is.InRange(298, 302), "five seconds at 60 fps, the settle time left out");
                Assert.That(row.P95, Is.EqualTo(16.6f).Within(1e-3));
                Assert.That(row.DrawCalls, Is.EqualTo(120));
                Assert.That(row.Result, Is.EqualTo(PerfMust.Pass));
                Assert.That(row.Room, Is.EqualTo("budget_room"));
            }

            string[] lines = driver.Lines();
            Assert.That(lines[0], Is.EqualTo(PerfTourCsv.Magic));
            Assert.That(driver.File.ToString(), Does.Contain("# session=20261004T153012Z-a3f9\n"));
            Assert.That(driver.File.ToString(), Does.Contain("# note=S2 Curved Walk is not in this build: not measured\n"));
            Assert.That(driver.File.ToString(), Does.Contain("# thermalAtStart=0\n"));
            Assert.That(driver.File.ToString(), Does.EndWith("# result=pass\n"));
            Assert.That(Array.FindAll(lines, l => l == PerfTourCsv.Header).Length, Is.EqualTo(1));
            List<string> rows = driver.DataRows();
            Assert.That(rows.Count, Is.EqualTo(4));
            foreach (string row in rows) Assert.That(row.Split(',').Length, Is.EqualTo(PerfTourCsv.ColumnCount));

            // Both Glued layouts are one visit to SpaceLab: the space is asked for once, the layout set twice.
            Assert.That(driver.Calls, Is.EqualTo(new[]
            {
                "variant None", "request S0", "variant Glued5x5", "request S1", "variant GluedT3", "variant None", "request S6"
            }));
            Assert.That(driver.Events.FindAll(e => e.StartsWith(PerfTourCsv.SummaryEvent + " ")).Count, Is.EqualTo(4));
            Assert.That(driver.Events[0], Does.StartWith(PerfTourCsv.BeginEvent + " {\"run\":\"tour\""));
            Assert.That(driver.Events[driver.Events.Count - 1], Does.StartWith(PerfTourCsv.EndEvent + " {\"run\":\"tour\",\"result\":\"pass\""));
            foreach (string e in driver.Events)
            {
                JsonValue data;
                Assert.That(AtlasJson.TryParse(e.Substring(e.IndexOf(' ') + 1), out data) && data.IsObject, Is.True, e);
            }
        }

        [Test]
        public void PerfTour_Core_FramesOfASwitchNeverReachARow()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Tour(4f, id => id == SpaceId.Home, 2f), Info(), 0.0);

            double time = 0.0;
            // A terrible frame in every phase but the measured one.
            while (core.Running && core.Phase != PerfTourPhase.Measuring)
            {
                time += Dt;
                core.AddFrame(500f, 400f, 300f, 200f, 0.5f);
                core.AddSecond(2f, 9999, 999, 9000000, 4000, 100000);
                core.Tick(time, 0, 0);
            }
            // The probe's row that ends in the first measured second still holds frames of the settle time.
            core.AddSecond(2f, 9999, 999, 9000000, 4000, 100000);
            Play(core, driver, time, 10.0);

            Assert.That(core.Rows.Count, Is.EqualTo(1));
            Assert.That(core.Rows[0].Max, Is.EqualTo(16.6f).Within(1e-3));
            Assert.That(core.Rows[0].DrawCalls, Is.EqualTo(120));
            Assert.That(core.Rows[0].Result, Is.EqualTo(PerfMust.Pass));
        }

        [Test]
        public void PerfTour_Core_ASlowSpaceFailsAndTheRunSaysSo()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Tour(3f, id => id == SpaceId.Home, 1f), Info(), 0.0);
            Play(core, driver, 0.0, 20.0, null, 24f);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Fail));
            Assert.That(core.Rows[0].Failed, Is.EqualTo("p50|p95|over17_2"));
            Assert.That(driver.File.ToString(), Does.EndWith("# result=fail\n"));
        }

        [Test]
        public void PerfTour_Core_ASpaceThatCannotBeEnteredFailsItsRowAndTheTourGoesOn()
        {
            var driver = new FakeTourDriver();
            driver.Refused.Add(SpaceId.Glued);
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Tour(2f, id => id == SpaceId.Home || id == SpaceId.Glued || id == SpaceId.Slicer, 1f), Info(), 0.0);
            Play(core, driver, 0.0, 2.0 * PerfTourCore.SwitchTimeoutSeconds + 30.0);

            Assert.That(core.Running, Is.False);
            Assert.That(core.Rows.Count, Is.EqualTo(4));
            Assert.That(core.Rows[0].Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(core.Rows[1].Result, Is.EqualTo(PerfMust.Fail));
            Assert.That(core.Rows[1].Failed, Is.EqualTo("noFrames|notEntered"));
            Assert.That(core.Rows[3].Label, Is.EqualTo("S6 Slicer"));
            Assert.That(core.Rows[3].Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(core.Result, Is.EqualTo(PerfMust.Fail));
        }

        [Test]
        public void PerfTour_Core_LeavingTheSpaceWhileMeasuringFailsTheRow()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Tour(4f, id => id == SpaceId.Home, 1f), Info(), 0.0);
            double time = Play(core, driver, 0.0, 3.0);
            Assert.That(core.Phase, Is.EqualTo(PerfTourPhase.Measuring));
            driver.Current = SpaceId.Slicer;        // a fault sent the walker elsewhere
            time = Play(core, driver, time, 0.5);
            driver.Current = SpaceId.Home;
            Play(core, driver, time, 10.0);
            Assert.That(core.Rows[0].Result, Is.EqualTo(PerfMust.Fail));
            Assert.That(core.Rows[0].Failed, Is.EqualTo("leftSpace"));
        }

        [Test]
        public void PerfTour_Core_NoRoomEndsTheRun()
        {
            var driver = new FakeTourDriver { RoomNever = true };
            var core = new PerfTourCore(driver);
            bool aborted = false;
            core.Finished += a => aborted = a;
            core.Begin(PerfSchedule.Tour(5f, null), Info(), 0.0);
            Play(core, driver, 0.0, PerfTourCore.PrepareTimeoutSeconds + 1.0);
            Assert.That(core.Running, Is.False);
            Assert.That(aborted, Is.True);
            Assert.That(core.Result, Is.EqualTo("aborted"));
            Assert.That(core.Rows.Count, Is.EqualTo(0));
            Assert.That(driver.File.ToString(), Does.Contain("# aborted=the budget room did not become active\n"));
            Assert.That(driver.Calls, Is.Empty, "nothing was asked of SpaceLab");
        }

        [Test]
        public void PerfTour_Core_LadderMeasuresTheTimeUntilATierIsInForce()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Ladder(1f), Info(), 0.0);
            Play(core, driver, 0.0, 30.0);

            Assert.That(core.Running, Is.False);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(core.Rows.Count, Is.EqualTo(5));
            int[] order = { 1, 2, 3, 4, 0 };
            for (int i = 0; i < 5; i++)
            {
                PerfSummary row = core.Rows[i];
                Assert.That(row.Kind, Is.EqualTo("ladder"));
                Assert.That(row.Tier, Is.EqualTo(order[i]));
                Assert.That(row.EffectMs, Is.EqualTo(1000f / 60f).Within(0.5f), "in force on the next frame");
                Assert.That(row.Result, Is.EqualTo(PerfMust.Pass));
            }
            Assert.That(driver.ForcedTier, Is.EqualTo(-1), "the ladder is automatic again");
            Assert.That(driver.Calls[driver.Calls.Count - 1], Is.EqualTo("tier -1"));
            Assert.That(driver.DataRows()[2], Does.StartWith("ladder,ladder,Critical,S0,"));
        }

        [Test]
        public void PerfTour_Core_ATierThatNeverComesFails()
        {
            var driver = new FakeTourDriver { TierStuck = true };
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Ladder(1f), Info(), 0.0);
            Play(core, driver, 0.0, 5 * PerfTourCore.TierTimeoutSeconds + 10.0);
            Assert.That(core.Running, Is.False);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Fail));
            Assert.That(core.Rows[0].Failed, Is.EqualTo("noEffect"));
            // Nominal was in force all along, so the last step sees its tier at once.
            Assert.That(core.Rows[4].Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(driver.ForcedTier, Is.EqualTo(-1));
        }

        [Test]
        public void PerfTour_Core_SoakWritesOneRowPerSpaceATotalAndTheThermalTimeline()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            PerfSchedule soak = PerfSchedule.Soak(PerfRunKind.Soak, 900f, 60f, id => id == SpaceId.Home || id == SpaceId.Glued);
            core.Begin(soak, Info(), 50.0);
            // Fair after five minutes, Serious after eleven.
            Play(core, driver, 50.0, 1000.0, t => t - 50.0 > 660.0 ? 2 : (t - 50.0 > 300.0 ? 1 : 0));

            Assert.That(core.Running, Is.False);
            Assert.That(core.Rows.Count, Is.EqualTo(3));
            Assert.That(core.Rows[0].Label, Is.EqualTo("S1 Glued 5x5"));
            Assert.That(core.Rows[1].Label, Is.EqualTo("S0 Home"));
            PerfSummary total = core.Rows[2];
            Assert.That(total.Kind, Is.EqualTo("total"));
            Assert.That(total.Space, Is.Empty);
            Assert.That(total.Frames, Is.EqualTo(core.Rows[0].Frames + core.Rows[1].Frames));
            Assert.That(core.Rows[0].Frames, Is.InRange(8 * 57 * 60 - 20, 8 * 57 * 60 + 20), "eight visits of 57 measured seconds");
            Assert.That(total.TimeToFair, Is.EqualTo(300f).Within(0.5f));
            Assert.That(total.TimeToSerious, Is.EqualTo(660f).Within(0.5f));
            Assert.That(total.TimeToCritical, Is.EqualTo(-1f));
            Assert.That(total.ThermalMax, Is.EqualTo(2));
            Assert.That(total.Result, Is.EqualTo(PerfMust.Pass), total.Failed);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Pass));

            string file = driver.File.ToString();
            Assert.That(file, Does.Contain("# intervalSeconds=60\n"));
            Assert.That(file, Does.Contain("# note=S2 Curved Walk is not in this build: the soak alternates S1 Glued 5x5 and S0 Home\n"));
            Assert.That(file, Does.Contain("# thermal=300 1\n").Or.Contain("# thermal=300.1 1\n"));
            Assert.That(file, Does.Contain("# thermal=660 2\n").Or.Contain("# thermal=660.1 2\n"));
        }

        [Test]
        public void PerfTour_Core_AnEarlySeriousFailsTheSoak()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Soak(PerfRunKind.Soak, 900f, 60f, null), Info(), 0.0);
            Play(core, driver, 0.0, 1000.0, t => t > 400.0 ? 2 : 0);
            Assert.That(core.Rows[core.Rows.Count - 1].Failed, Is.EqualTo("seriousEarly"));
            Assert.That(core.Result, Is.EqualTo(PerfMust.Fail));
        }

        [Test]
        public void PerfTour_Core_AStoppedSoakKeepsWhatItMeasured()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Soak(PerfRunKind.SoakDepthOff, 900f, 60f, id => id != SpaceId.CurvedWalk), Info(), 0.0);
            Play(core, driver, 0.0, 130.0);
            core.Abort("stopped by perf.run");
            core.Abort("twice");

            Assert.That(core.Running, Is.False);
            Assert.That(core.Result, Is.EqualTo("aborted"));
            Assert.That(core.Rows.Count, Is.EqualTo(3));
            Assert.That(core.Rows[2].Run, Is.EqualTo("soakNoDepth"));
            Assert.That(core.Rows[2].Result, Is.EqualTo(PerfMust.Incomplete));
            Assert.That(core.Rows[2].Frames, Is.GreaterThan(100 * 60));
            Assert.That(driver.File.ToString(), Does.EndWith("# aborted=stopped by perf.run\n"));
        }

        [Test]
        public void PerfTour_Core_ASecondRunStartsClean()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Tour(2f, id => id == SpaceId.Home, 1f), Info(), 0.0);
            Assert.Throws<InvalidOperationException>(() => core.Begin(PerfSchedule.Ladder(), Info(), 0.0));
            double time = Play(core, driver, 0.0, 10.0, null, 30f);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Fail));

            core.Begin(PerfSchedule.Tour(2f, id => id == SpaceId.Home, 1f), Info(), time);
            Play(core, driver, time, 10.0);
            Assert.That(core.Rows.Count, Is.EqualTo(1));
            Assert.That(core.Rows[0].Result, Is.EqualTo(PerfMust.Pass), "the first run's frames are gone");
            Assert.That(core.Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(core.Status, Is.EqualTo("perf: pass"));
        }

        [Test]
        public void PerfTour_Core_MeasuringAllocatesNothing()
        {
            var driver = new FakeTourDriver();
            var core = new PerfTourCore(driver);
            core.Begin(PerfSchedule.Tour(60f, id => id == SpaceId.Home, 1f), Info(), 0.0);
            double time = Play(core, driver, 0.0, 3.0);
            Assert.That(core.Phase, Is.EqualTo(PerfTourPhase.Measuring));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++)
            {
                time += Dt;
                core.AddFrame(16.6f, 4f, 3f, 5f, Dt);
                if (i % 60 == 0) core.AddSecond(60f, 120, 14, 148000, 640, 0);
                core.Tick(time, 0, 0);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public sealed class PerfTourPoseTrackTests
    {
        [Test]
        public void PerfTour_Track_WalksInsideTheFreeRectangleWithoutJumps()
        {
            var track = new SyntheticPoseTrack();
            track.SetFreeRect(2.4f, 3.4f, false);
            Assert.That(track.Name, Is.EqualTo("synthetic"));
            Assert.That(track.Frame, Is.EqualTo(PoseFrameKind.Room));
            Assert.That(track.DrivesRig, Is.False);

            HeadPose previous;
            Assert.That(track.TryRead(0.0, 1f / 60f, out previous), Is.True);
            float walked = 0f, minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
            for (int i = 1; i < 60 * 60; i++)
            {
                HeadPose pose;
                Assert.That(track.TryRead(i / 60.0, 1f / 60f, out pose), Is.True);
                float step = math.distance(pose.Floor, previous.Floor);
                Assert.That(step, Is.LessThan(0.03f), "0.8 m/s is 13 mm per frame; the pose filter drops 0.2 m");
                walked += step;
                Assert.That(math.abs(pose.Position.x), Is.LessThanOrEqualTo(1.2f - SyntheticPoseTrack.Inset + 1e-4f));
                Assert.That(math.abs(pose.Position.z), Is.LessThanOrEqualTo(1.7f - SyntheticPoseTrack.Inset + 1e-4f));
                Assert.That(pose.Position.y, Is.EqualTo(SyntheticPoseTrack.EyeHeight));
                Assert.That(pose.Tracking, Is.EqualTo(TrackingState.Normal));
                Assert.That(math.abs(math.length(pose.Rotation.value) - 1f), Is.LessThan(1e-4f));
                Assert.That(math.abs(pose.Forward.y), Is.LessThan(0.45f), "the gaze stays near the horizon");
                minX = math.min(minX, pose.Position.x); maxX = math.max(maxX, pose.Position.x);
                minZ = math.min(minZ, pose.Position.z); maxZ = math.max(maxZ, pose.Position.z);
                previous = pose;
            }
            Assert.That(walked, Is.EqualTo(SyntheticPoseTrack.Speed * 60f).Within(1.5f), "walking speed along the lap");
            Assert.That(maxX - minX, Is.GreaterThan(1.7f), "the lap spans the rectangle");
            Assert.That(maxZ - minZ, Is.GreaterThan(2.7f));
        }

        [Test]
        public void PerfTour_Track_IsTheSameEveryRun()
        {
            var a = new SyntheticPoseTrack();
            var b = new SyntheticPoseTrack();
            a.SetFreeRect(3f, 4f, false);
            b.SetFreeRect(3f, 4f, false);
            HeadPose pa = HeadPose.Identity, pb = HeadPose.Identity;
            for (int i = 0; i < 500; i++)
            {
                a.TryRead(i / 60.0, 1f / 60f, out pa);
                b.TryRead(i / 60.0, 1f / 60f, out pb);
            }
            Assert.That(pa.Position, Is.EqualTo(pb.Position));
            Assert.That(pa.Rotation.value, Is.EqualTo(pb.Rotation.value));
            b.SetFreeRect(3f, 4f, false);
            Assert.That(b.Time, Is.EqualTo(0f), "a new room starts the lap again");
        }

        [Test]
        public void PerfTour_Track_TurnsOnTheSpotInStandingMode()
        {
            var track = new SyntheticPoseTrack();
            track.SetFreeRect(1.0f, 1.0f, true);
            Assert.That(track.HalfAxes, Is.EqualTo(float2.zero));
            HeadPose first, pose = HeadPose.Identity;
            track.TryRead(0.0, 1f / 60f, out first);
            for (int i = 1; i < 240; i++) track.TryRead(i / 60.0, 1f / 60f, out pose);
            Assert.That(pose.Floor, Is.EqualTo(float2.zero));
            Assert.That(math.abs(pose.Yaw - first.Yaw), Is.GreaterThan(0.5f));

            track.SetRoom(null);
            Assert.That(track.HalfAxes, Is.EqualTo(float2.zero));
            track.SetFreeRect(0.7f, 0.8f, false);
            Assert.That(track.HalfAxes, Is.EqualTo(float2.zero), "too small to lap");
        }

        [Test]
        public void PerfTour_Track_FitsTheBudgetRoom()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            var track = new SyntheticPoseTrack();
            track.SetRoom(room);
            Assert.That(track.HalfAxes.x, Is.EqualTo(0.5f * room.FreeRect.Wf - SyntheticPoseTrack.Inset).Within(1e-5));
            Assert.That(track.HalfAxes.y, Is.EqualTo(0.5f * room.FreeRect.Df - SyntheticPoseTrack.Inset).Within(1e-5));
            Assert.That(track.HalfAxes.x, Is.GreaterThan(SyntheticPoseTrack.MinHalfAxis));
        }
    }

    [TestFixture]
    public sealed class PerfTourRoomKeeperTests
    {
        static RoomModel Room(string id) { return new RoomModel { Id = id }; }

        [Test]
        public void PerfTour_Room_ScannedRoomAndItsAnchorComeBackAfterARun([Values(PerfRunKind.Tour, PerfRunKind.Soak, PerfRunKind.SoakDepthOff)] PerfRunKind kind)
        {
            var keeper = new PerfRoomKeeper(RoomFixtures.BudgetRoom);
            RoomModel scanned = Room("room-20261004");
            object anchorOfScan = new object();

            keeper.Begin(kind, scanned, anchorOfScan);
            Assert.That(keeper.Holding, Is.True);
            Assert.That(keeper.NeedsRunRoom(scanned), Is.True, "the run is measured on the fixture");
            Assert.That(keeper.RoomIdOfRun(scanned), Is.EqualTo(RoomFixtures.BudgetRoom));
            RoomModel fixture = Room(RoomFixtures.BudgetRoom);
            Assert.That(keeper.NeedsRunRoom(fixture), Is.False);

            RoomModel room;
            object anchor;
            Assert.That(keeper.TakeBack(fixture, out room, out anchor), Is.True);
            Assert.That(room, Is.SameAs(scanned));
            Assert.That(anchor, Is.SameAs(anchorOfScan));
            Assert.That(keeper.Holding, Is.False);
            Assert.That(keeper.TakeBack(fixture, out room, out anchor), Is.False, "handed back once");
            Assert.That(room, Is.Null);
        }

        [Test]
        public void PerfTour_Room_LadderCheckStaysInTheActiveRoom()
        {
            var keeper = new PerfRoomKeeper(RoomFixtures.BudgetRoom);
            RoomModel scanned = Room("room-20261004");

            keeper.Begin(PerfRunKind.Ladder, scanned, new object());
            Assert.That(PerfRoomKeeper.UsesRunRoom(PerfRunKind.Ladder), Is.False);
            Assert.That(keeper.NeedsRunRoom(scanned), Is.False);
            Assert.That(keeper.RoomIdOfRun(scanned), Is.EqualTo("room-20261004"));
            Assert.That(keeper.Holding, Is.False);
            RoomModel room;
            object anchor;
            Assert.That(keeper.TakeBack(scanned, out room, out anchor), Is.False);

            // With no room at all Home cannot be entered: the fixture stands in, and there is nothing to give back.
            keeper.Begin(PerfRunKind.Ladder, null, null);
            Assert.That(keeper.NeedsRunRoom(null), Is.True);
            Assert.That(keeper.RoomIdOfRun(null), Is.EqualTo(RoomFixtures.BudgetRoom));
            Assert.That(keeper.Holding, Is.False);
        }

        [Test]
        public void PerfTour_Room_NothingIsHandedBackWhenTheFixtureWasTheRoomOrNeverReplacedIt()
        {
            var keeper = new PerfRoomKeeper(RoomFixtures.BudgetRoom);
            RoomModel room;
            object anchor;

            // The Editor rehearsal: the fixture was the room before the run.
            RoomModel fixture = Room(RoomFixtures.BudgetRoom);
            keeper.Begin(PerfRunKind.Tour, fixture, null);
            Assert.That(keeper.Holding, Is.False);
            Assert.That(keeper.NeedsRunRoom(fixture), Is.False);
            Assert.That(keeper.TakeBack(fixture, out room, out anchor), Is.False);

            // Stopped before the fixture was made active: the scanned room never left.
            RoomModel scanned = Room("room-20261004");
            keeper.Begin(PerfRunKind.Soak, scanned, new object());
            Assert.That(keeper.TakeBack(scanned, out room, out anchor), Is.False);
            Assert.That(room, Is.Null);
            Assert.That(anchor, Is.Null);
            Assert.That(keeper.Holding, Is.False);

            // A newer room scanned or loaded during the run stays.
            keeper.Begin(PerfRunKind.Soak, scanned, new object());
            Assert.That(keeper.TakeBack(Room("room-newer"), out room, out anchor), Is.False);
            Assert.That(keeper.Holding, Is.False);
        }

        [Test]
        public void PerfTour_Room_ARoomFromAFileComesBackWithNoAnchor()
        {
            var keeper = new PerfRoomKeeper(RoomFixtures.BudgetRoom);
            RoomModel loaded = Room("sample_3x3");
            keeper.Begin(PerfRunKind.Tour, loaded, null);
            RoomModel room;
            object anchor;
            Assert.That(keeper.TakeBack(Room(RoomFixtures.BudgetRoom), out room, out anchor), Is.True);
            Assert.That(room, Is.SameAs(loaded));
            Assert.That(anchor, Is.Null);
        }
    }
}
