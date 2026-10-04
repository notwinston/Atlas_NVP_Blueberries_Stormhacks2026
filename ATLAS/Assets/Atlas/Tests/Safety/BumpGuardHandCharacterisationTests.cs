using System.Globalization;
using Atlas.Safety;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard_HandCharacterisation (hands design sections 7.1 and 9.1): what Bump Guard makes of the walker's
    /// own hand when nothing tells it that it is a hand. A hand-sized plate seen only by the synthetic depth is
    /// held 0.3 to 0.7 m ahead of the camera while the walker stands or walks in a scanned 7 x 7 m room, far
    /// from every wall, and the levels it raises are recorded. There is no mask anywhere in this file: it
    /// documents the problem, in the real kernel, and holds before and after Bump Guard learns about hands.
    /// </summary>
    [TestFixture]
    public class BumpGuardHandCharacterisationTests
    {
        const float RoomHalf = 3.5f;
        const float RunSeconds = 3f;
        // The palm's centre is 5 cm below the optical axis, as a hand held up in front of the face is.
        const float PalmBelowAxis = 0.05f;

        static readonly float[] Depths = { 0.31f, 0.35f, 0.40f, 0.45f, 0.50f, 0.60f, 0.70f };
        static readonly float[] Speeds = { 0f, 0.5f, 1.0f };

        struct Outcome
        {
            public BumpLevel Worst;
            /// <summary>Seconds from the start to the first cue and to the first STRONG; -1 when there was none.</summary>
            public double FirstCue, FirstStrong;
            public int Cells;
            public float SmallestNow;
        }

        static Outcome Run(float depth, float speed, bool forearm, float pitchDownDegrees)
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                new SimHand(0f, -PalmBelowAxis, depth, forearm).AddTo(sim.Body);
                var o = new Outcome { Worst = BumpLevel.Clear, FirstCue = -1.0, FirstStrong = -1.0, SmallestNow = float.PositiveInfinity };
                quaternion rotation = quaternion.RotateX(math.radians(pitchDownDegrees));
                int frames = (int)math.round(RunSeconds * BumpScenario.FrameRate);
                for (int k = 0; k < frames; k++)
                {
                    // From 2.5 m behind the middle of the room along +Z: 3 m at 1 m/s ends 3 m short of the wall.
                    var head = new float3(0f, BumpScenario.HeadHeight, -2.5f + speed * k / BumpScenario.FrameRate);
                    sim.StepFrame(head, rotation);
                    if (sim.Core.Level > o.Worst) o.Worst = sim.Core.Level;
                    if (sim.Core.Now < o.SmallestNow) o.SmallestNow = sim.Core.Now;
                }
                foreach (LevelChange c in sim.Changes)
                {
                    if (o.FirstCue < 0.0 && c.From == BumpLevel.Clear) o.FirstCue = c.Time;
                    if (o.FirstStrong < 0.0 && c.To == BumpLevel.Strong) o.FirstStrong = c.Time;
                }
                o.Cells = sim.Core.DynamicCellCount;
                Assert.That(sim.Core.MapTrusted, Is.True);
                return o;
            }
        }

        static string Row(float depth, float speed, in Outcome o)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "hand {0:F2} m ahead, {1:F1} m/s: {2,-6} first cue {3}, first STRONG {4}, smallest clear(head) {5:F2} m, cells left {6}",
                depth, speed, o.Worst.ToString().ToUpperInvariant(), Seconds(o.FirstCue), Seconds(o.FirstStrong), o.SmallestNow, o.Cells);
        }

        static string Seconds(double t)
        {
            return t < 0.0 ? "never" : t.ToString("F2", CultureInfo.InvariantCulture) + " s";
        }

        [Test]
        public void BumpGuard_HandCharacterisation()
        {
            var worst = new BumpLevel[Depths.Length, Speeds.Length];
            for (int d = 0; d < Depths.Length; d++)
                for (int s = 0; s < Speeds.Length; s++)
                {
                    Outcome o = Run(Depths[d], Speeds[s], false, 0f);
                    worst[d, s] = o.Worst;
                    TestContext.WriteLine(Row(Depths[d], Speeds[s], o));
                }

            for (int d = 0; d < Depths.Length; d++)
            {
                float depth = Depths[d];
                // Standing with a level head, a hand is no nearer on the floor than it is deep: nothing is raised
                // once it is clear of the 0.30 m threshold.
                if (depth >= 0.35f) Assert.That(worst[d, 0], Is.EqualTo(BumpLevel.Clear), "standing, " + depth);
                // The numbers section 7.1 quotes: a hand 0.4 to 0.5 m ahead gives WARN at 0.5 m/s and STRONG at 1 m/s.
                if (depth >= 0.40f && depth <= 0.50f)
                {
                    Assert.That(worst[d, 1], Is.EqualTo(BumpLevel.Warn), "0.5 m/s, " + depth);
                    Assert.That(worst[d, 2], Is.EqualTo(BumpLevel.Strong), "1.0 m/s, " + depth);
                }
                // Farther out it is still in the path of a fast walker.
                if (depth >= 0.60f) Assert.That(worst[d, 2], Is.EqualTo(BumpLevel.Warn), "1.0 m/s, " + depth);
            }
        }

        [Test]
        public void BumpGuard_HandCharacterisation_WithAForearm()
        {
            foreach (float depth in new[] { 0.40f, 0.45f, 0.50f, 0.60f })
                foreach (float speed in Speeds)
                {
                    Outcome o = Run(depth, speed, true, 0f);
                    TestContext.WriteLine("with a forearm, " + Row(depth, speed, o));
                    if (speed > 0f) Assert.That(o.Worst, Is.GreaterThanOrEqualTo(BumpLevel.Warn), depth + " m at " + speed + " m/s");
                }
        }

        [Test]
        public void BumpGuard_HandCharacterisation_StandingAndLookingDown()
        {
            // The head pitched down at something within reach: on the floor the hand is nearer than it is deep,
            // so a walker who stands still can be warned about a hand held out to press a panel.
            foreach (float pitch in new[] { 15f, 25f, 35f })
                foreach (float depth in new[] { 0.35f, 0.40f, 0.45f, 0.55f })
                {
                    Outcome o = Run(depth, 0f, false, pitch);
                    TestContext.WriteLine("pitched down " + pitch.ToString(CultureInfo.InvariantCulture) + " degrees, " + Row(depth, 0f, o));
                }
            Assert.That(Run(0.35f, 0f, false, 25f).Worst, Is.GreaterThanOrEqualTo(BumpLevel.Warn));
        }
    }
}
