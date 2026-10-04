using NUnit.Framework;
using Atlas.Rendering.Room;
using Atlas.World;

namespace Atlas.Rendering.Tests.Room
{
    /// <summary>Walls sinking to kerbs, rising and vanishing (Master Plan 4.6).</summary>
    public class RoomWallSinkTests
    {
        const float Ratio = 0.10f / 2.4f;

        static RoomWallSink Fresh()
        {
            var sink = new RoomWallSink();
            sink.Reset(Ratio);
            return sink;
        }

        [Test]
        public void StartsWithFullWalls()
        {
            RoomWallSink sink = Fresh();
            Assert.That(sink.Mode, Is.EqualTo(WallMode.Full));
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            Assert.That(sink.ScaleY, Is.EqualTo(1f));
            Assert.That(sink.Tick(0.1f), Is.False);
        }

        [Test]
        public void SinksToKerbs_ThenTheKerbMeshTakesOver()
        {
            RoomWallSink sink = Fresh();
            sink.Set(WallMode.Kerbs, 0.5f, Ratio);
            Assert.That(sink.Moving, Is.True);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            float last = sink.ScaleY;
            for (int frame = 0; frame < 29; frame++)
            {
                Assert.That(sink.Tick(1f / 60f), Is.True);
                Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
                Assert.That(sink.ScaleY, Is.LessThan(last));
                Assert.That(sink.ScaleY, Is.GreaterThan(Ratio));
                last = sink.ScaleY;
            }
            sink.Tick(1f / 60f);
            sink.Tick(1f / 60f);
            Assert.That(sink.Moving, Is.False);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Kerbs));
            Assert.That(sink.ScaleY, Is.EqualTo(1f));
            Assert.That(sink.Level, Is.EqualTo(Ratio));
        }

        [Test]
        public void RisesFromKerbs_StartingAtKerbHeight()
        {
            RoomWallSink sink = Fresh();
            sink.Set(WallMode.Kerbs, 0f, Ratio);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Kerbs));
            sink.Set(WallMode.Full, 1f, Ratio);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            Assert.That(sink.ScaleY, Is.EqualTo(Ratio));
            sink.Tick(0.5f);
            Assert.That(sink.ScaleY, Is.EqualTo(0.5f * (1f + Ratio)).Within(1e-5f));
            sink.Tick(0.6f);
            Assert.That(sink.Moving, Is.False);
            Assert.That(sink.ScaleY, Is.EqualTo(1f));
        }

        [Test]
        public void Hidden_SinksToNothing_AndComesBack()
        {
            RoomWallSink sink = Fresh();
            sink.Set(WallMode.Hidden, 0.2f, Ratio);
            sink.Tick(0.1f);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            sink.Tick(0.2f);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.None));
            Assert.That(sink.Level, Is.EqualTo(0f));
            sink.Set(WallMode.Kerbs, 0.2f, Ratio);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            sink.Tick(1f);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Kerbs));
        }

        [Test]
        public void ARepeatedOrInstantRequest_ArrivesAtOnce()
        {
            RoomWallSink sink = Fresh();
            sink.Set(WallMode.Full, 0.5f, Ratio);
            Assert.That(sink.Moving, Is.False);
            sink.Set(WallMode.Hidden, 0f, Ratio);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.None));
            sink.Set(WallMode.Full, -1f, Ratio);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            Assert.That(sink.ScaleY, Is.EqualTo(1f));
        }

        [Test]
        public void AChangeOfMind_MidWay_ContinuesFromWhereTheWallsAre()
        {
            RoomWallSink sink = Fresh();
            sink.Set(WallMode.Hidden, 1f, Ratio);
            sink.Tick(0.5f);
            float level = sink.Level;
            Assert.That(level, Is.EqualTo(0.5f).Within(1e-5f));
            sink.Set(WallMode.Full, 1f, Ratio);
            Assert.That(sink.Level, Is.EqualTo(level));
            sink.Tick(1f);
            Assert.That(sink.Shown, Is.EqualTo(WallShown.Walls));
            Assert.That(sink.ScaleY, Is.EqualTo(1f));
        }

        [Test]
        public void KerbHeightChange_IsFollowed()
        {
            RoomWallSink sink = Fresh();
            sink.Set(WallMode.Kerbs, 0f, Ratio);
            sink.SetKerbRatio(0.25f);
            Assert.That(sink.Level, Is.EqualTo(0.25f));
            sink.Set(WallMode.Full, 1f, 0.25f);
            Assert.That(sink.ScaleY, Is.EqualTo(0.25f));
        }
    }
}
