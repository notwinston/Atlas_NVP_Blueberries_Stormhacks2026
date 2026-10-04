using System;
using Atlas.Safety;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard_Replay (Master Plan section 11.1): the warning logic of section 4.10 on scripted walks, headless.
    /// </summary>
    [TestFixture]
    public class BumpGuardReplayTests
    {
        const float RoomHalf = 3.5f;          // a 7 x 7 m room for the approaches
        const float FrameSeconds = 1f / BumpScenario.FrameRate;

        public enum Obstacle { Wall, Chair, Stool }

        struct ApproachResult
        {
            public int RisingEdges;
            public bool Cued;
            public double CueTime;
            public double ContactTime;
            public float2 CuePosition;
            public bool Strong;
            public float2 StrongPosition;
            public double LeadSeconds => ContactTime - CueTime;
        }

        static SimBox Chair => SimBox.OnFloor(0f, 2.0f, 0.45f, 0.45f, 0.90f);
        static SimBox Stool => SimBox.OnFloor(0f, 2.0f, 0.35f, 0.35f, 0.45f);

        // Walks at the obstacle until contact: for the wall, where the path reaches clear() = 0 (5 cm before
        // the wall, section 4.10); for a box placed after the scan, where the head reaches its footprint.
        static ApproachResult Approach(Obstacle obstacle, float speed, float angleDegrees)
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                float a = math.radians(angleDegrees);
                float2 dir = new float2(math.sin(a), math.cos(a));
                float2 start;
                if (obstacle == Obstacle.Wall)
                {
                    // Start 3.2 m from the +Z wall, measured along the wall's normal.
                    start = new float2(0f, 0.3f) - new float2(dir.x / dir.y * 1.6f, 0f);
                }
                else
                {
                    sim.Boxes.Add(obstacle == Obstacle.Chair ? Chair : Stool);
                    start = new float2(0f, 2.0f) - dir * 3.5f;
                }

                sim.Walk(start, dir, speed, 30f, (s, p) =>
                    s.MapClearance(p) <= 0f || (s.Boxes.Count > 0 && s.Boxes[0].ContainsFloorPoint(p)));

                var r = new ApproachResult { RisingEdges = sim.RisingEdges(), ContactTime = sim.Time };
                foreach (LevelChange c in sim.Changes)
                {
                    if (!r.Cued && c.From == BumpLevel.Clear)
                    {
                        r.Cued = true;
                        r.CueTime = c.Time;
                        r.CuePosition = c.Position;
                    }
                    if (!r.Strong && c.To == BumpLevel.Strong)
                    {
                        r.Strong = true;
                        r.StrongPosition = c.Position;
                    }
                }
                Assert.That(sim.Core.Level, Is.Not.EqualTo(BumpLevel.TrackingLost));
                return r;
            }
        }

        // 20 approaches: five speeds head-on to each obstacle, and 45 degrees at 0.5 and 1.0 m/s (section 11.4.5).
        static readonly object[] Approaches =
        {
            new object[] { Obstacle.Wall, 0.5f, 0f }, new object[] { Obstacle.Wall, 0.625f, 0f },
            new object[] { Obstacle.Wall, 0.75f, 0f }, new object[] { Obstacle.Wall, 0.875f, 0f },
            new object[] { Obstacle.Wall, 1.0f, 0f },
            new object[] { Obstacle.Chair, 0.5f, 0f }, new object[] { Obstacle.Chair, 0.625f, 0f },
            new object[] { Obstacle.Chair, 0.75f, 0f }, new object[] { Obstacle.Chair, 0.875f, 0f },
            new object[] { Obstacle.Chair, 1.0f, 0f },
            new object[] { Obstacle.Stool, 0.5f, 0f }, new object[] { Obstacle.Stool, 0.625f, 0f },
            new object[] { Obstacle.Stool, 0.75f, 0f }, new object[] { Obstacle.Stool, 0.875f, 0f },
            new object[] { Obstacle.Stool, 1.0f, 0f },
            new object[] { Obstacle.Wall, 0.5f, 45f }, new object[] { Obstacle.Wall, 1.0f, 45f },
            new object[] { Obstacle.Chair, 0.5f, 45f }, new object[] { Obstacle.Chair, 1.0f, 45f },
            new object[] { Obstacle.Stool, 1.0f, 45f },
        };

        [TestCaseSource(nameof(Approaches))]
        public void Approach_CueFiresOnceAtLeast600msBeforeContact(Obstacle obstacle, float speed, float angle)
        {
            ApproachResult r = Approach(obstacle, speed, angle);
            TestContext.WriteLine("{0} {1} m/s {2} deg: lead {3:F3} s, edges {4}", obstacle, speed, angle, r.LeadSeconds, r.RisingEdges);
            Assert.That(r.Cued, Is.True, "no cue before contact");
            Assert.That(r.RisingEdges, Is.EqualTo(1), "exactly one rising edge");
            Assert.That(r.LeadSeconds, Is.GreaterThanOrEqualTo(0.6), "lead time to predicted contact");
        }

        [TestCase(0.7f, 0.66f, 0.38f)]
        [TestCase(1.0f, 0.90f, 0.50f)]
        public void WorkedNumbers_WarnAndStrongDistancesFromWall(float speed, float warnAt, float strongAt)
        {
            ApproachResult r = Approach(Obstacle.Wall, speed, 0f);
            float warnDistance = RoomHalf - r.CuePosition.y;
            float strongDistance = RoomHalf - r.StrongPosition.y;
            TestContext.WriteLine("{0} m/s: WARN at {1:F3} m, STRONG at {2:F3} m", speed, warnDistance, strongDistance);
            // One frame of walking is up to 1.7 cm.
            Assert.That(warnDistance, Is.EqualTo(warnAt).Within(0.02f));
            Assert.That(r.Strong, Is.True);
            Assert.That(strongDistance, Is.EqualTo(strongAt).Within(0.02f));
        }

        [Test]
        public void WorkedNumbers_WarnAtHalfMetreAtHalfMetrePerSecond()
        {
            ApproachResult r = Approach(Obstacle.Wall, 0.5f, 0f);
            float warnDistance = RoomHalf - r.CuePosition.y;
            TestContext.WriteLine("0.5 m/s: WARN at {0:F3} m", warnDistance);
            Assert.That(warnDistance, Is.EqualTo(0.50f).Within(0.02f));
        }

        [Test]
        public void WorkedNumbers_LeadTimeAtLeast850msFrom03To10()
        {
            for (int k = 0; k <= 7; k++)
            {
                float speed = 0.3f + 0.1f * k;
                ApproachResult r = Approach(Obstacle.Wall, speed, 0f);
                TestContext.WriteLine("{0:F1} m/s: lead {1:F3} s", speed, r.LeadSeconds);
                // The cue and the contact are each seen at a frame boundary.
                Assert.That(r.LeadSeconds, Is.GreaterThanOrEqualTo(0.85 - 1.5 * FrameSeconds), "speed " + speed);
                Assert.That(r.RisingEdges, Is.EqualTo(1));
            }
        }

        [Test]
        public void WalkingAlongAWallAtHalfAMetre_RaisesNothing()
        {
            foreach (float speed in new[] { 0.5f, 1.0f })
            {
                using (var sim = new BumpScenario(RoomHalf, RoomHalf))
                {
                    sim.Walk(new float2(RoomHalf - 0.5f, -2.0f), new float2(0f, 1f), speed, 3.5f / speed);
                    Assert.That(sim.Changes.Count, Is.EqualTo(0));
                    Assert.That(sim.Core.Now, Is.EqualTo(0.45f).Within(0.005f));
                    Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0), "a mapped wall is not a dynamic obstacle");
                    Assert.That(sim.Core.RescanAdvised, Is.False);
                }
            }
        }

        // ---- The lap square: 3 x 3 m room, free rectangle 2.4 m, lap side 2.0 m, 0.5 m from the walls (4.4) ----

        static readonly float2[] LapCorners =
        {
            new float2(-1f, -1f), new float2(1f, -1f), new float2(1f, 1f), new float2(-1f, 1f)
        };

        static SimPose LapPose(float arc, bool reverse)
        {
            arc = ((arc % 8f) + 8f) % 8f;
            int side = math.min((int)(arc / 2f), 3);
            float along = arc - 2f * side;
            float2 a = LapCorners[side], b = LapCorners[(side + 1) % 4];
            float2 p = math.lerp(a, b, along / 2f);
            float2 d = math.normalize(b - a);
            if (reverse)
            {
                p = new float2(p.x, -p.y);
                d = new float2(d.x, -d.y);
            }
            return new SimPose { Position = p, Yaw = math.atan2(d.x, d.y) };
        }

        [TestCase(1.0f, false)]
        [TestCase(3.0f, false)]
        [TestCase(4.3f, false)]
        [TestCase(7.0f, true)]
        [TestCase(1.6f, true)]
        public void LapSquare_NoStrongAndNoWarnOnTheStraightSides(float startArc, bool reverse)
        {
            const float speed = 0.5f;
            using (var sim = new BumpScenario(1.5f, 1.5f))
            {
                int frames = (int)((8f / speed + 2f) * BumpScenario.FrameRate);
                for (int k = 0; k < frames; k++)
                    sim.StepFrame(LapPose(startArc + speed * k * FrameSeconds, reverse));

                int cornerCues = 0;
                foreach (LevelChange c in sim.Changes)
                {
                    Assert.That(c.To, Is.Not.EqualTo(BumpLevel.Strong), "STRONG on the lap");
                    Assert.That(c.To, Is.Not.EqualTo(BumpLevel.TrackingLost));
                    if (c.From != BumpLevel.Clear) continue;
                    float nearestCorner = float.MaxValue;
                    foreach (float2 corner in LapCorners) nearestCorner = math.min(nearestCorner, math.distance(c.Position, corner));
                    Assert.That(nearestCorner, Is.LessThanOrEqualTo(0.5f), "WARN on a straight side at " + c.Position);
                    cornerCues++;
                }
                TestContext.WriteLine("corner cues: {0}", cornerCues);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0));
                Assert.That(sim.Core.RescanAdvised, Is.False);
            }
        }

        // ---- Tracking ----

        [Test]
        public void TrackingNotNormal_RaisesTrackingLostAfterHalfASecond()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                var pose = new SimPose { Position = float2.zero, Yaw = 0f };
                sim.Hold(pose, 1f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear));

                sim.TrackingNormal = false;
                sim.Hold(pose, 0.45f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear), "not yet 0.5 s");
                Assert.That(sim.Core.MapTrusted, Is.False, "the static map is dropped from the first frame");
                sim.Hold(pose, 0.1f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.TrackingLost));
                sim.Hold(pose, 2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.TrackingLost));

                sim.TrackingNormal = true;
                sim.Hold(pose, 0.2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.TrackingLost), "relaxes only after 0.3 s");
                sim.Hold(pose, 0.2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear));
            }
        }

        [Test]
        public void TrackingNotNormal_UsesLiveDepthAloneFromTheFirstFrame()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                // 0.15 m from the mapped +X wall, looking along it: STRONG on the map.
                var pose = new SimPose { Position = new float2(RoomHalf - 0.15f, 0f), Yaw = 0f };
                sim.Hold(pose, 0.2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Strong));
                Assert.That(sim.Core.Diagnostics.HeadFrameFallback, Is.False);

                // Tracking goes: the map is dropped at once and the sectors take its place. Depth sees the
                // same wall on the right, but no nearer than the sensor's 0.3 m floor: about 0.34 m.
                sim.TrackingNormal = false;
                sim.Boxes.Add(SimBox.OnFloor(RoomHalf - 1.5f, 1.5f, 0.4f, 0.4f, 1.8f));
                sim.StepFrame(pose);
                Assert.That(sim.Core.MapTrusted, Is.False);
                Assert.That(sim.Core.Diagnostics.HeadFrameFallback, Is.True);
                Assert.That(sim.Core.RawLevel, Is.EqualTo(BumpLevel.Clear));
                Assert.That(sim.Core.Now, Is.InRange(0.30f, 0.40f));
                Assert.That(sim.Core.Now, Is.EqualTo(sim.Core.SectorRange(6)));
                Assert.That(sim.Core.SectorRange(0), Is.GreaterThan(1.0f), "the box, ahead and to the left");
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Strong), "still inside the relax time");
                Assert.That(sim.Core.Direction.x, Is.GreaterThan(0.4f), "to the right");
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0), "no stamping from an untrusted pose");
                sim.Hold(pose, 0.6f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.TrackingLost));
                Assert.That(sim.Core.Diagnostics.NotNormalSeconds, Is.GreaterThanOrEqualTo(0.5f));
            }
        }

        [Test]
        public void TrackingNotNormal_DepthSectorsRaiseStrongWithNoMap()
        {
            // Sector ranges straight into the core: something 0.15 m ahead and to the right while tracking is Limited.
            using (var core = new BumpGuardCore())
            {
                DepthFrameResult depth = DepthFrameResult.Empty;
                depth.Sector5 = 0.15f;
                depth.Time = 1.0;
                core.SubmitDepthResult(depth);
                core.Step(1.0, new float3(0f, 1.6f, 0f), quaternion.identity, false, true);
                Assert.That(core.Level, Is.EqualTo(BumpLevel.Strong), "from the first frame");
                Assert.That(core.SectorRange(5), Is.EqualTo(0.15f));
                Assert.That(core.Direction.x, Is.EqualTo(math.sin(math.radians(20f))).Within(1e-4f));
                Assert.That(core.Direction.y, Is.EqualTo(math.cos(math.radians(20f))).Within(1e-4f));

                // Stale sector ranges are not used.
                core.Step(1.4, new float3(0f, 1.6f, 0f), quaternion.identity, false, true);
                Assert.That(core.RawLevel, Is.EqualTo(BumpLevel.Clear));
            }
        }

        [Test]
        public void TrackingNotNormal_UsesTheLastValidSpeed()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                // Someone standing in the way: tall enough to stay in a level view (a 0.9 m chair leaves it at 1.6 m).
                sim.Boxes.Add(SimBox.OnFloor(0f, 2.0f, 0.45f, 0.45f, 1.8f)); // front face at z = 1.775
                bool lostSeen = false, warnBeforeLost = false;
                sim.Walk(new float2(0f, -1.5f), new float2(0f, 1f), 1.0f, 30f, (s, p) =>
                {
                    if (p.y > 0.6f) s.TrackingNormal = false; // 1.175 m away, still Clear
                    if (!s.TrackingNormal && s.Core.Level == BumpLevel.Warn && !lostSeen) warnBeforeLost = true;
                    if (s.Core.Level == BumpLevel.TrackingLost) lostSeen = true;
                    return lostSeen;
                });
                Assert.That(sim.Core.Speed, Is.EqualTo(1.0f).Within(0.05f), "speed frozen at the last valid value");
                Assert.That(warnBeforeLost, Is.True, "depth-only WARN before the stand-still cue");
            }
        }

        [Test]
        public void AnchorNotTracked_StaticMapIsNotTrustedAndCountsAsNotNormal()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                sim.AnchorTracked = false;
                // 0.15 m from the mapped wall: STRONG on the map, but the map may not be used and depth is off.
                var pose = new SimPose { Position = new float2(RoomHalf - 0.15f, 0f), Yaw = 0f };
                sim.Hold(pose, 0.4f);
                Assert.That(sim.Core.MapTrusted, Is.False);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear));
                sim.Hold(pose, 0.2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.TrackingLost));
                sim.AnchorTracked = true;
                sim.StepFrame(pose);
                Assert.That(sim.Core.MapTrusted, Is.True);
                Assert.That(sim.Core.RawLevel, Is.EqualTo(BumpLevel.Strong));
                sim.Hold(pose, 0.4f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Strong));
            }
        }

        // ---- Pose jump ----

        [Test]
        public void PoseJump_TakesThePositionAndProducesNoStrongFromVelocity()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                var pose = new SimPose { Position = new float2(0f, RoomHalf - 0.8f), Yaw = 0f };
                sim.Hold(pose, 0.5f);
                pose.Position.y += 0.25f; // towards the wall: 15 m/s if it were believed
                sim.StepFrame(pose);
                Assert.That(sim.Core.PoseJumped, Is.True);
                Assert.That(sim.Core.Speed, Is.EqualTo(0f));
                Assert.That(sim.Core.Now, Is.EqualTo(0.50f).Within(0.005f), "the new position is taken at once");
                sim.Hold(pose, 1f);
                Assert.That(sim.Changes.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void StepUnderTheJumpThreshold_IsBelievedAsMotion()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                var pose = new SimPose { Position = float2.zero, Yaw = 0f };
                sim.Hold(pose, 0.5f);
                pose.Position.y += 0.19f;
                sim.StepFrame(pose);
                Assert.That(sim.Core.PoseJumped, Is.False);
                Assert.That(sim.Core.Speed, Is.GreaterThan(0.5f));
            }
        }

        // ---- Dynamic layer ----

        [Test]
        public void DynamicCells_PersistOutOfView_ClearWhenSeenEmpty()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Boxes.Add(Chair);
                var facing = new SimPose { Position = new float2(0f, -1f), Yaw = 0f };
                var away = new SimPose { Position = new float2(0f, -1f), Yaw = math.PI };
                float2 beside = new float2(0f, 1.70f); // 7.5 cm in front of the chair

                sim.Hold(facing, 0.5f);
                int cells = sim.Core.DynamicCellCount;
                Assert.That(cells, Is.GreaterThan(8));
                Assert.That(sim.Core.ClearAt(beside, sim.Time), Is.LessThan(0.13f));
                Assert.That(sim.Changes.Count, Is.EqualTo(0), "2.8 m away: no cue");

                sim.Hold(away, 10f);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(cells), "leaving the image clears nothing");
                Assert.That(sim.Core.ClearAt(beside, sim.Time), Is.LessThan(0.13f));

                sim.Boxes.Clear();
                sim.Hold(away, 1f);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(cells), "taken away unseen: still remembered");
                sim.Hold(facing, 0.5f);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0), "seen empty");
                Assert.That(sim.Core.ClearAt(beside, sim.Time), Is.GreaterThan(1f));
            }
        }

        [Test]
        public void DynamicCells_ExpireAfter30SecondsWithoutAHit()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Boxes.Add(Chair);
                var facing = new SimPose { Position = new float2(0f, -1f), Yaw = 0f };
                float2 beside = new float2(0f, 1.70f);
                sim.Hold(facing, 0.5f);
                sim.DepthEnabled = false;
                sim.Hold(facing, 29f);
                Assert.That(sim.Core.DynamicCellCount, Is.GreaterThan(0));
                Assert.That(sim.Core.ClearAt(beside, sim.Time), Is.LessThan(0.13f));
                sim.Hold(facing, 1.1f);
                Assert.That(sim.Core.ClearAt(beside, sim.Time), Is.GreaterThan(1f), "ignored from 30 s on");
                sim.Hold(facing, 2f);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0), "and swept");
            }
        }

        [Test]
        public void Stool_IsRememberedAfterItDropsOutOfTheLevelView()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Boxes.Add(Stool); // near edge at z = 1.825
                sim.Walk(new float2(0f, -1.4f), new float2(0f, 1f), 0.5f, 30f, (s, p) => p.y > 0.6f);
                // 1.2 m away: a level view from 1.6 m no longer reaches a 0.45 m top (section 4.10).
                Assert.That(sim.Core.LastDepth.Stamped, Is.EqualTo(0), "out of view");
                Assert.That(sim.Core.DynamicCellCount, Is.GreaterThan(0));
                Assert.That(sim.Core.ClearAt(new float2(0f, 1.80f), sim.Time), Is.LessThan(0.20f));
            }
        }

        // ---- Rescan ----

        [Test]
        public void Rescan_AdvisedAfterTwoSecondsOfDisagreement()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                var pose = new SimPose { Position = new float2(0f, 1.5f), Yaw = 0f };
                sim.Hold(pose, 3f);
                Assert.That(sim.Core.RescanAdvised, Is.False, "map and depth agree");
                Assert.That(sim.Core.Diagnostics.RescanMedian, Is.LessThan(0.02f));

                sim.RealHalf = new float2(RoomHalf, RoomHalf + 0.2f); // the wall ahead is 0.2 m off the map
                sim.Hold(pose, 1.9f);
                Assert.That(sim.Core.Diagnostics.RescanMedian, Is.EqualTo(0.2f).Within(0.02f));
                Assert.That(sim.Core.RescanAdvised, Is.False, "not yet 2 s");
                sim.Hold(pose, 0.2f);
                Assert.That(sim.Core.RescanAdvised, Is.True);

                // It is withdrawn only after 2 s of agreement.
                sim.RealHalf = new float2(RoomHalf, RoomHalf);
                sim.Hold(pose, 1.9f);
                Assert.That(sim.Core.RescanAdvised, Is.True);
                sim.Hold(pose, 0.2f);
                Assert.That(sim.Core.RescanAdvised, Is.False);
            }
        }

        [Test]
        public void Rescan_NotAdvisedBelowTheThreshold()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.RealHalf = new float2(RoomHalf, RoomHalf + 0.10f);
                sim.Hold(new SimPose { Position = new float2(0f, 1.5f), Yaw = 0f }, 5f);
                Assert.That(sim.Core.RescanAdvised, Is.False);
            }
        }

        // ---- Hysteresis ----

        [Test]
        public void Hysteresis_RelaxesOnlyAfter5cmAnd300ms()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                // Clearance at the head = distance to the wall - 0.05 m.
                Func<float, SimPose> at = now => new SimPose { Position = new float2(RoomHalf - 0.05f - now, 0f), Yaw = 0f };

                sim.Hold(at(0.29f), 0.5f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
                sim.Hold(at(0.33f), 1.5f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn), "0.03 m of margin is not enough");
                sim.Hold(at(0.37f), 0.25f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn), "0.3 s have not passed");
                sim.Hold(at(0.37f), 0.1f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear));
                Assert.That(sim.RisingEdges(), Is.EqualTo(1));
                Assert.That(sim.Core.Diagnostics.RisingEdges, Is.EqualTo(1));

                // STRONG steps down to WARN the same way.
                sim.Hold(at(0.18f), 0.5f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Strong));
                sim.Hold(at(0.23f), 1f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Strong));
                sim.Hold(at(0.27f), 0.5f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
            }
        }

        // ---- Direction ----

        [Test]
        public void Direction_PointsAtTheHazardInHeadSpace()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                // Wall on the walker's right.
                sim.Hold(new SimPose { Position = new float2(RoomHalf - 0.2f, 0f), Yaw = 0f }, 0.2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Strong));
                Assert.That(sim.Core.Direction.x, Is.EqualTo(1f).Within(0.02f));
                Assert.That(sim.Core.Direction.y, Is.EqualTo(0f).Within(0.02f));
            }
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                // Same wall, walker turned to face -Z: now it is on the left.
                sim.Hold(new SimPose { Position = new float2(RoomHalf - 0.2f, 0f), Yaw = math.PI }, 0.2f);
                Assert.That(sim.Core.Direction.x, Is.EqualTo(-1f).Within(0.02f));
            }
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                // Walking at a chair the map does not know: the hazard is ahead.
                sim.Boxes.Add(Chair);
                sim.Walk(new float2(0f, -1.5f), new float2(0f, 1f), 1.0f, 30f, (s, p) => s.Core.Level != BumpLevel.Clear);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
                Assert.That(sim.Core.Direction.y, Is.GreaterThan(0.95f));
            }
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.DepthEnabled = false;
                sim.Hold(new SimPose { Position = float2.zero, Yaw = 0f }, 0.2f);
                Assert.That(sim.Core.Direction, Is.EqualTo(float2.zero), "Clear has no direction");
            }
        }

        // ---- No allocation on the frame path ----

#if ATLAS_HEADLESS
        [Test]
        public unsafe void StepAndDepth_AllocateNothing()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Boxes.Add(Chair);
                quaternion rot = quaternion.RotateY(0f);
                // Warm up: first calls may JIT.
                sim.Walk(new float2(0f, -1.5f), new float2(0f, 1f), 1.0f, 1f);
                sim.Render(new float3(0f, BumpScenario.HeadHeight, -0.5f), rot);

                BumpGuardCore core = sim.Core;
                double t = sim.Time;
                fixed (float* depth = sim.Depth)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int k = 0; k < 600; k++)
                    {
                        t += FrameSeconds;
                        float3 head = new float3(0f, BumpScenario.HeadHeight, -0.5f + 0.003f * k);
                        bool tracking = k < 300 || k > 400;
                        core.ProcessDepth(depth, BumpScenario.DepthW, BumpScenario.DepthH, sim.Intrinsics, head, rot, t, tracking, true);
                        core.Step(t, head, rot, tracking, true);
                    }
                    long after = GC.GetAllocatedBytesForCurrentThread();
                    Assert.That(after - before, Is.EqualTo(0L));
                }
            }
        }
#endif
    }
}
