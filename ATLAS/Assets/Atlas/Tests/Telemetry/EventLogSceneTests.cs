using System.Collections.Generic;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>The scene-tagged LogJson: a use case names its own scene (contract section 2.6).</summary>
    public class EventLogSceneTests
    {
        static EventLog NewLog(List<EventMessage> logged)
        {
            var log = new EventLog(new ManualClock(5000), null) { Scene = "S6" };
            log.Logged += logged.Add;
            return log;
        }

        [Test]
        public void LogJson_with_a_scene_logs_under_that_scene_and_leaves_the_current_one()
        {
            var logged = new List<EventMessage>();
            EventLog log = NewLog(logged);

            log.LogJson("lung", "fork", "{\"correct\":true}");

            Assert.That(logged.Count, Is.EqualTo(1));
            Assert.That(logged[0].Scene, Is.EqualTo("lung"));
            Assert.That(logged[0].Ev, Is.EqualTo("fork"));
            Assert.That(logged[0].TimeUs, Is.EqualTo(5000));
            Assert.That(logged[0].Data.GetBool("correct"), Is.True);
            Assert.That(log.Scene, Is.EqualTo("S6"));
        }

        [Test]
        public void LogJson_with_an_empty_scene_keeps_the_current_scene()
        {
            var logged = new List<EventMessage>();
            EventLog log = NewLog(logged);

            log.LogJson(null, "walk.lap", "{\"q\":5}");

            Assert.That(logged[0].Scene, Is.EqualTo("S6"));
            Assert.That(logged[0].Data.GetLong("q"), Is.EqualTo(5));
        }

        [Test]
        public void The_extension_reaches_the_session_log_through_the_published_interface()
        {
            var logged = new List<EventMessage>();
            EventLog log = NewLog(logged);
            IEventLog service = log;

            service.LogJson("fieldfit", "ff.lens", "{}");

            Assert.That(logged[0].Scene, Is.EqualTo("fieldfit"));
        }

        sealed class OtherLog : IEventLog
        {
            public readonly List<string> Calls = new List<string>();
            public void Log(string ev) { Calls.Add(ev); }
            public void Log(string ev, string key, double value) { Calls.Add(ev); }
            public void Log(string ev, string key, string value) { Calls.Add(ev); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Calls.Add(ev); }
            public void LogJson(string ev, string dataJson) { Calls.Add(ev + " " + dataJson); }
        }

        [Test]
        public void The_extension_on_another_log_falls_back_to_the_scene_less_call()
        {
            var other = new OtherLog();

            ((IEventLog)other).LogJson("lung", "fork", "{}");

            Assert.That(other.Calls, Is.EqualTo(new[] { "fork {}" }));
        }
    }
}
