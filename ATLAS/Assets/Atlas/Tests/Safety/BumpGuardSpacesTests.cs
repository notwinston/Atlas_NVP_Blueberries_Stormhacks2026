using System;
using System.Collections.Generic;
using System.IO;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// P2-C10, headless part: the scripted approach test of Master Plan section 11.4.5 repeated for S0 to S6 with
    /// the same fixed thresholds, driven by scripted walks and by the recordings in /recordings.
    /// <para>Bump Guard reads the physical head pose and the physical scan only (section 4.10): no space feeds it
    /// anything, so what these tests prove for a space is that the shared logic meets the rows with the walk the
    /// space asks of the walker. What a space can change is on the phone (frame time while the cue plays, the cue
    /// drawn over the space's look, the room registration); that is the device check in
    /// docs/evidence/bumpguard_spaces.md. S2, S3 and S4 wait for lane B and are reported as ignored (pending).</para>
    /// </summary>
    [TestFixture]
    public class BumpGuardSpacesTests
    {
        const float RoomHalf = 3.5f;          // a 7 x 7 m room for the approaches, as in BumpGuardReplayTests
        const double RequiredLeadSeconds = 0.6;
        const float WallZ = 1.5f;             // sample_3x3: the wall the recorded approach walks at
        const float LapCornerRadius = 0.5f;   // a WARN within 0.5 m of a lap corner is a corner cue (11.4.5)

        /// <summary>One row of the table in docs/evidence/bumpguard_spaces.md.</summary>
        public sealed class SpaceRow
        {
            public readonly string Id, Name;
            /// <summary>Why the row waits, or null when the space is built on this branch.</summary>
            public readonly string Pending;
            public SpaceRow(string id, string name, string pending)
            {
                Id = id;
                Name = name;
                Pending = pending;
            }
            public override string ToString() => Id + " " + Name;
        }

        const string LaneB = "lane B: this space is not on this branch (P1-B09, P1-B11 and the kernel, S2 to S4)";

        public static readonly SpaceRow[] Spaces =
        {
            new SpaceRow("S0", "Home", null),
            new SpaceRow("S1", "Glued", null),
            new SpaceRow("S2", "Curved Walk", LaneB),
            new SpaceRow("S3", "Aligned Cells", LaneB),
            new SpaceRow("S4", "Small Planet", LaneB),
            new SpaceRow("S5", "Impossible Rooms", null),
            new SpaceRow("S6", "Slicer", null),
        };

        static void RequireBuilt(SpaceRow space)
        {
            if (space.Pending != null) Assert.Ignore("Pending, " + space.Pending + ". Steps: docs/evidence/bumpguard_spaces.md.");
        }

        // ---- The ten wall approaches of a space ----

        struct Approach
        {
            public float Speed, AngleDegrees;
            public Approach(float speed, float angle)
            {
                Speed = speed;
                AngleDegrees = angle;
            }
        }

        // Head-on at 0.5 to 1.0 m/s in six steps, 45 degrees at 0.5, 0.75 and 1.0 m/s, and a second head-on at 1.0 m/s.
        static readonly Approach[] WallApproaches =
        {
            new Approach(0.5f, 0f), new Approach(0.6f, 0f), new Approach(0.7f, 0f), new Approach(0.8f, 0f),
            new Approach(0.9f, 0f), new Approach(1.0f, 0f),
            new Approach(0.5f, 45f), new Approach(0.75f, 45f), new Approach(1.0f, 45f),
            new Approach(1.0f, 0f),
        };

        struct Outcome
        {
            public bool Cued;
            public int RisingEdges;
            public double LeadSeconds;
            public bool Strong;
        }

        static Outcome RunWallApproach(Approach a)
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                float r = math.radians(a.AngleDegrees);
                float2 dir = new float2(math.sin(r), math.cos(r));
                // Start 3.2 m from the +Z wall, measured along the wall's normal.
                float2 start = new float2(0f, 0.3f) - new float2(dir.x / dir.y * 1.6f, 0f);
                sim.Walk(start, dir, a.Speed, 30f, (s, p) => s.MapClearance(p) <= 0f);

                var o = new Outcome { RisingEdges = sim.RisingEdges() };
                foreach (LevelChange c in sim.Changes)
                {
                    if (!o.Cued && c.From == BumpLevel.Clear)
                    {
                        o.Cued = true;
                        o.LeadSeconds = sim.Time - c.Time;
                    }
                    if (c.To == BumpLevel.Strong) o.Strong = true;
                }
                return o;
            }
        }

        [TestCaseSource(nameof(Spaces))]
        public void WallApproaches_TenOfTen_WarnAtLeast600msBeforeContact(SpaceRow space)
        {
            RequireBuilt(space);
            int ok = 0;
            foreach (Approach a in WallApproaches)
            {
                Outcome o = RunWallApproach(a);
                bool pass = o.Cued && o.RisingEdges == 1 && o.LeadSeconds >= RequiredLeadSeconds;
                TestContext.WriteLine("{0} {1:F3} m/s {2} deg: lead {3:F3} s, edges {4}, {5}",
                    space.Id, a.Speed, a.AngleDegrees, o.LeadSeconds, o.RisingEdges, pass ? "ok" : "FAIL");
                if (pass) ok++;
            }
            TestContext.WriteLine("{0}: {1} of {2} approaches warned in time", space.Id, ok, WallApproaches.Length);
            Assert.That(ok, Is.EqualTo(WallApproaches.Length));
        }

        // ---- The recorded approach and the recorded lap ----

        static PoseLog LoadRecording(string name)
        {
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            string path = Path.GetFullPath(Path.Combine(project, "..", "recordings", name + ".poselog"));
            Assert.That(File.Exists(path), "missing recording " + path);
            using (var reader = new StreamReader(path)) return PoseLog.Read(reader);
        }

        // Replays a log through the scenario at its own timing; one pose per frame (the logs are 60 Hz).
        static void Replay(BumpScenario sim, PoseLog log)
        {
            foreach (HeadPose s in log.Samples)
            {
                sim.TrackingNormal = s.Tracking == TrackingState.Normal;
                sim.StepFrame(s.Position, s.Rotation);
            }
        }

        [TestCaseSource(nameof(Spaces))]
        public void RecordedWallApproach_OneCueAtLeast600msBeforePredictedContact(SpaceRow space)
        {
            RequireBuilt(space);
            PoseLog log = LoadRecording("walk_wall_approach");
            using (var sim = new BumpScenario(1.5f, 1.5f))
            {
                Replay(sim, log);
                Assert.That(sim.RisingEdges(), Is.EqualTo(1), "exactly one cue");
                Assert.That(sim.FirstChangeTo(BumpLevel.Warn, out LevelChange warn), Is.True, "a WARN");
                // The walk stops 0.3 m short. Predicted contact is where the straight path reaches clear() = 0,
                // 5 cm before the wall (section 4.10), at the walking speed of the log (1.0 m/s).
                const float speed = 1.0f;
                double lead = (WallZ - BumpConstants.StaticInset - warn.Position.y) / speed;
                TestContext.WriteLine("{0}: WARN at z = {1:F3} m, lead to predicted contact {2:F3} s", space.Id, warn.Position.y, lead);
                Assert.That(lead, Is.GreaterThanOrEqualTo(RequiredLeadSeconds));
            }
        }

        [TestCaseSource(nameof(Spaces))]
        public void RecordedLap_NoStrongAndNoWarnOnTheStraightSides_CornerCuesCounted(SpaceRow space)
        {
            RequireBuilt(space);
            PoseLog log = LoadRecording("walk_lap_sample_3x3");
            float2[] corners = { new float2(-1f, -1f), new float2(1f, -1f), new float2(1f, 1f), new float2(-1f, 1f) };
            using (var sim = new BumpScenario(1.5f, 1.5f))
            {
                Replay(sim, log);
                int strong = 0, straight = 0, corner = 0;
                foreach (LevelChange c in sim.Changes)
                {
                    if (c.To == BumpLevel.Strong) strong++;
                    if (c.From != BumpLevel.Clear) continue;
                    float nearest = float.MaxValue;
                    foreach (float2 k in corners) nearest = math.min(nearest, math.distance(c.Position, k));
                    if (nearest <= LapCornerRadius) corner++; else straight++;
                }
                TestContext.WriteLine("{0}: STRONG {1}, WARN on straight sides {2}, corner WARNs {3}", space.Id, strong, straight, corner);
                Assert.That(strong, Is.EqualTo(0), "STRONG on the lap");
                Assert.That(straight, Is.EqualTo(0), "WARN on a straight side");
            }
        }

        [Test]
        public void TheTableCoversS0ToS6_ThreeOfThemPendingOnLaneB()
        {
            var ids = new List<string>();
            int pending = 0;
            foreach (SpaceRow s in Spaces)
            {
                ids.Add(s.Id);
                if (s.Pending != null) pending++;
            }
            Assert.That(ids, Is.EqualTo(new[] { "S0", "S1", "S2", "S3", "S4", "S5", "S6" }));
            Assert.That(pending, Is.EqualTo(3));
        }
    }
}
