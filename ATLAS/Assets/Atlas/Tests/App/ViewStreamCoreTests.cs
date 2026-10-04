using Atlas.Link;
using NUnit.Framework;

namespace Atlas.App.Tests
{
    /// <summary>When the phone sends its view to the Station's View page, and when it stops.</summary>
    public class ViewStreamCoreTests
    {
        const float Dt = 1f / 60f;

        // Runs frames at 60 Hz from time t, finishing each frame at once. Returns the frames taken.
        static int Run(ViewStreamCore core, ref double t, int frames)
        {
            int taken = 0;
            for (int i = 0; i < frames; i++)
            {
                t += Dt;
                if (core.Tick(t, Dt))
                {
                    taken++;
                    core.Done();
                }
            }
            return taken;
        }

        [Test]
        public void Nothing_IsCaptured_UntilTheHubAsks()
        {
            var core = new ViewStreamCore();
            double t = 0;
            Assert.That(Run(core, ref t, 120), Is.EqualTo(0));
            Assert.That(core.Active, Is.False);
        }

        [Test]
        public void An_Asked_Stream_Runs_AtItsRate()
        {
            var core = new ViewStreamCore { Fps = 10 };
            core.Request(true, 8080);
            double t = 0;
            Assert.That(Run(core, ref t, 60), Is.InRange(9, 11), "one second at 10 frames a second");
            Assert.That(core.Port, Is.EqualTo(8080));
        }

        [Test]
        public void One_Frame_IsInFlight_AtATime()
        {
            var core = new ViewStreamCore { Fps = 20 };
            core.Request(true, 8080);
            double t = 0;
            t += Dt;
            Assert.That(core.Tick(t, Dt), Is.True);
            int more = 0;
            for (int i = 0; i < 60; i++)
            {
                t += Dt;
                if (core.Tick(t, Dt)) more++;
            }
            Assert.That(more, Is.EqualTo(0), "a frame still being posted holds the next back");
            core.Done();
            t += Dt;
            Assert.That(core.Tick(t, Dt), Is.True);
            Assert.That(core.Frames, Is.EqualTo(1));
        }

        [Test]
        public void Without_A_Repeat_TheStream_StopsAfterItsHold()
        {
            var core = new ViewStreamCore();
            core.Request(true, 8080);
            double t = 0;
            int frames = (int)(OperatorCommand.ViewHoldSeconds * 60f) + 30;
            Run(core, ref t, frames);
            Assert.That(core.Active, Is.False);
            Assert.That(Run(core, ref t, 60), Is.EqualTo(0));

            core.Request(true, 8080);     // the Hub's next repeat starts it again
            Assert.That(Run(core, ref t, 60), Is.GreaterThan(0));
        }

        [Test]
        public void Off_OrNobodyWatching_StopsItAtOnce()
        {
            var core = new ViewStreamCore();
            core.Request(true, 8080);
            core.Request(false, 8080);
            double t = 0;
            Assert.That(Run(core, ref t, 60), Is.EqualTo(0));

            core.Request(true, 9000);
            core.Watching(false);
            Assert.That(core.Active, Is.False);
            core.Watching(true);
            Assert.That(core.Active, Is.False, "an answer never starts the stream; only the Hub's request does");
        }

        [Test]
        public void Settings_AreClamped()
        {
            var core = new ViewStreamCore { Fps = 500 };
            Assert.That(core.Fps, Is.EqualTo(ViewStreamCore.MaxFps));
            core.Fps = 0;
            Assert.That(core.Fps, Is.EqualTo(ViewStreamCore.MinFps));
            core.Request(true, 0);
            Assert.That(core.Port, Is.EqualTo(OperatorCommand.DefaultHttpPort));
        }

        [Test]
        public void Url_Height_AndTheHubsAnswer()
        {
            Assert.That(ViewStreamCore.FrameUrl("10.0.0.104", 8080, true), Is.EqualTo("http://10.0.0.104:8080/api/view/frame?mode=headset"));
            Assert.That(ViewStreamCore.FrameUrl("10.0.0.104", 8081, false), Is.EqualTo("http://10.0.0.104:8081/api/view/frame?mode=hand"));
            Assert.That(ViewStreamCore.HeightFor(640, 1118, 1032), Is.EqualTo(590));
            Assert.That(ViewStreamCore.HeightFor(640, 2796, 1290), Is.EqualTo(294));
            Assert.That(ViewStreamCore.HeightFor(0, 10, 10), Is.EqualTo(0));
            Assert.That(ViewStreamCore.ReadWatching("{\"watching\":false}"), Is.False);
            Assert.That(ViewStreamCore.ReadWatching("{\"watching\":true}"), Is.True);
            Assert.That(ViewStreamCore.ReadWatching("not json"), Is.True, "an unreadable answer does not stop the stream");
        }
    }
}
