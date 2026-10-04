extern alias replay;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using NUnit.Framework;
using ReplayOptions = replay::Atlas.Tools.ReplayOptions;
using ReplayPlayer = replay::Atlas.Tools.ReplayPlayer;

namespace Atlas.Station.Tests
{
    /// <summary>ReplayPhone against a real Hub over UDP (Master Plan Appendix C.8).</summary>
    [TestFixture]
    public class ReplayPhoneTests
    {
        [Test]
        public async Task SyntheticWalk_DrivesTheHub_AndItsRecordingReplaysIntoAnother()
        {
            // A copy of the first Hub's session folder: state.bin with its manifest.json, outline.bin and events.jsonl.
            string recording = Path.Combine(Path.GetTempPath(), "atlas-hub-tests", "replay-" + System.Guid.NewGuid().ToString("N"));
            long sent;

            await using (TestHub first = await TestHub.StartAsync())
            using (var spectator = new TestClient(LinkRole.Spectator, first.Hub.UdpPort))
            {
                Wait.Ready(spectator);
                var player = new ReplayPlayer(new ReplayOptions { Port = first.Hub.UdpPort, Seconds = 2.5, Quiet = true });
                int exit = player.Run(CancellationToken.None);
                Assert.That(exit, Is.EqualTo(ReplayPlayer.ExitOk));
                sent = player.StatesSent;
                Assert.That(sent, Is.InRange(140, 152), "2.5 s at 60 Hz");

                Wait.Until(() => first.Hub.StatesReceived == sent, "the Hub to count every State", 2000);
                Assert.That(first.Hub.StatesLost, Is.EqualTo(0));
                Assert.That(first.Hub.TelemetryRows, Is.GreaterThanOrEqualTo(2), "one Telemetry row a second");
                Assert.That(spectator.ReceivedSeqs.Count, Is.GreaterThanOrEqualTo(sent * 99 / 100), "relayed to the Spectator");

                // The replayed phone declares parameters, so the Console has sliders with no phone in the room.
                JsonValue list = await first.GetJson("/api/params");
                Assert.That(list["params"].Count, Is.GreaterThanOrEqualTo(5));
                Assert.That((await first.GetJson("/api/params/curved.cullRho")).GetFloat("val"), Is.EqualTo(3f));
                Wait.Until(() => !first.Hub.HasPhone, "the replay's Bye to free the seat", 4000);

                await first.Hub.StopAsync();
                Directory.CreateDirectory(recording);
                foreach (string name in SessionLog.Files)
                {
                    string source = Path.Combine(first.Hub.Log.Folder, name);
                    Assert.That(File.Exists(source), Is.True, name + " is part of every session with a phone that sent a manifest and an outline");
                    File.Copy(source, Path.Combine(recording, name), true);
                }
            }

            try
            {
                List<StateLog.Entry> original = StateLog.ReadAll(Path.Combine(recording, SessionLog.StateFile));
                Assert.That(original.Count, Is.EqualTo(sent));

                await using (TestHub second = await TestHub.StartAsync())
                {
                    var player = new ReplayPlayer(new ReplayOptions { Port = second.Hub.UdpPort, File = recording, Speed = 2.0, Quiet = true });
                    Assert.That(player.Run(CancellationToken.None), Is.EqualTo(ReplayPlayer.ExitOk));
                    Assert.That(player.StatesSent, Is.EqualTo(sent));
                    Wait.Until(() => second.Hub.StatesReceived == sent, "the second Hub to count the replayed States", 2000);
                    Assert.That(second.Hub.StatesLost, Is.EqualTo(0));

                    // The recording's manifest and room came along, so the Console and the Spectator have them too.
                    Assert.That((await second.GetJson("/api/params"))["params"].Count, Is.GreaterThanOrEqualTo(5));
                    Assert.That(File.Exists(Path.Combine(second.Hub.Log.Folder, SessionLog.OutlineFile)), Is.True);
                    Assert.That(File.ReadAllBytes(Path.Combine(second.Hub.Log.Folder, SessionLog.OutlineFile)),
                        Is.EqualTo(File.ReadAllBytes(Path.Combine(recording, SessionLog.OutlineFile))));

                    second.Hub.Log.Flush();
                    List<StateLog.Entry> replayed = StateLog.ReadAll(Path.Combine(second.Hub.Log.Folder, SessionLog.StateFile));
                    Assert.That(replayed.Count, Is.EqualTo(original.Count));
                    for (int i = 0; i < original.Count; i++)
                    {
                        Assert.That(replayed[i].State.HeadPosX, Is.EqualTo(original[i].State.HeadPosX));
                        Assert.That(replayed[i].State.HeadPosZ, Is.EqualTo(original[i].State.HeadPosZ));
                        Assert.That(replayed[i].State.GetXform(2), Is.EqualTo(original[i].State.GetXform(2)));
                    }
                }
            }
            finally
            {
                Directory.Delete(recording, true);
            }
        }

        [Test]
        public async Task ReplayPhone_AppliesConsoleParameters_AndAnswersCommands()
        {
            await using (TestHub t = await TestHub.StartAsync())
            {
                var player = new ReplayPlayer(new ReplayOptions { Port = t.Hub.UdpPort, Seconds = 30, Quiet = true });
                using (var stop = new CancellationTokenSource())
                {
                    Task<int> running = Task.Run(() => player.Run(stop.Token));
                    Wait.Until(() => t.Hub.PhoneState == LinkState.Connected && t.Hub.GetParams()["params"].Count > 0, "ReplayPhone to connect", 8000);
                    Wait.Until(() => t.Hub.GetState() != null, "the first State");

                    await t.PostJson("/api/params/curved.cullRho", "{\"value\": 2.5}");
                    await t.PostJson("/api/scene", "{\"space\": \"S4\"}");
                    await t.PostJson("/api/op", "{\"cmd\": \"cue\"}");
                    Wait.Until(() => player.CommandsReceived == 2, "the commands at ReplayPhone");

                    // The applied values come back in State: p[2] is cullRho, the space id follows the SceneCmd.
                    Wait.Until(() =>
                    {
                        JsonValue s = t.Hub.GetState();
                        return s != null && s["p"][StateMessage.ParamCullRho].AsFloat() == 2.5f && s.GetInt("space") == 4 && s.GetBool("cue");
                    }, "State to show the parameter, the space and the cue");

                    stop.Cancel();
                    Assert.That(await running, Is.EqualTo(ReplayPlayer.ExitOk));
                }
            }
        }

        [Test]
        public void ReplayPhone_WithNoHub_GivesUp()
        {
            var options = new ReplayOptions { Port = 9, ConnectTimeoutSeconds = 1.0, Quiet = true };
            var error = new StringWriter();
            TextWriter previous = System.Console.Error;
            System.Console.SetError(error);
            try
            {
                Assert.That(new ReplayPlayer(options).Run(CancellationToken.None), Is.EqualTo(ReplayPlayer.ExitNoHub));
            }
            finally
            {
                System.Console.SetError(previous);
            }
            Assert.That(error.ToString(), Does.Contain("no Hub answered"));
        }

        [Test]
        public void ReplayOptions_Parse()
        {
            ReplayOptions o;
            string error;
            Assert.That(ReplayOptions.TryParse(new[] { "--hub", "192.168.0.10:9060" }, out o, out error), Is.True, error);
            Assert.That(o.Address, Is.EqualTo("192.168.0.10"));
            Assert.That(o.Port, Is.EqualTo(9060));
            Assert.That(ReplayOptions.TryParse(new[] { "--hub", "192.168.0.10:99999" }, out o, out error), Is.False);
            Assert.That(ReplayOptions.TryParse(new[] { "walk.bin", "--hub", "192.168.1.10", "--port", "9051", "--seconds", "5", "--loop", "--speed", "0.5" }, out o, out error), Is.True, error);
            Assert.That(o.File, Is.EqualTo("walk.bin"));
            Assert.That(o.Address, Is.EqualTo("192.168.1.10"));
            Assert.That(o.Port, Is.EqualTo(9051));
            Assert.That(o.Seconds, Is.EqualTo(5.0));
            Assert.That(o.Loop, Is.True);
            Assert.That(o.Speed, Is.EqualTo(0.5));
            Assert.That(ReplayOptions.TryParse(new[] { "--hub", "station.local" }, out o, out error), Is.False);
            Assert.That(ReplayOptions.TryParse(new[] { "--hz", "0" }, out o, out error), Is.False);
            Assert.That(ReplayOptions.TryParse(new[] { "--port" }, out o, out error), Is.False);
        }
    }
}
