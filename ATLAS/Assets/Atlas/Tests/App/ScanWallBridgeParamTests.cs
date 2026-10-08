using System;
using Atlas.App;
using Atlas.Link;
using Atlas.Scan;
using NUnit.Framework;

namespace Atlas.App.Tests
{
    /// <summary>
    /// scan.wallBridge (standalone-phone plan D13): it decides whether the walls of the scan's one RoomPlan session
    /// are merged into the room (WallBridge.Enabled). The RoomPlan session itself does not depend on it.
    /// </summary>
    public class ScanWallBridgeParamTests
    {
        sealed class Plugin : IRoomPlanCapture
        {
            public bool IsAvailable = true;
            public int AvailableAsked;
            public bool Available { get { AvailableAsked++; return IsAvailable; } }
            public bool Start(IntPtr nativeSession) { return true; }
            public string TakeJson() { return ""; }
            public void Stop() { }
        }

        [TearDown]
        public void RestoreThePlugin()
        {
            RoomPlanPlugin.Register(null);
            WallBridge.Enabled = true;
        }

        [Test]
        public void Parameter_IsABoolDeclaredOnInGroupScan_AfterTheTeamPrescanOne()
        {
            var registry = new ParamRegistry();
            AppParams.DeclareScan(registry, new ScanSettings());
            ParamEntry entry = registry.Find(AppParams.ScanWallBridge);
            Assert.That(AppParams.ScanWallBridge, Is.EqualTo("scan.wallBridge"));
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Bool, Is.True, "on by default: the walls come from the scan's one RoomPlan session (D13)");
            Assert.That(entry.Id, Is.EqualTo(registry.Find(AppParams.TeamPrescan).Id + 1));
        }

        [Test]
        public void Parameter_SwitchesTheWallMergeOnAndOff()
        {
            var plugin = new Plugin();
            RoomPlanPlugin.Register(plugin);
            WallBridge.Enabled = false;
            var registry = new ParamRegistry();
            AppParams.DeclareScan(registry, new ScanSettings());
            registry.PushAll();
            Assert.That(WallBridge.Enabled, Is.True, "default on");

            registry.Set(AppParams.ScanWallBridge, 0f);
            Assert.That(WallBridge.Enabled, Is.False);
            registry.Set(AppParams.ScanWallBridge, 1f);
            Assert.That(WallBridge.Enabled, Is.True);
            Assert.That(plugin.AvailableAsked, Is.EqualTo(0), "the switch never asks the plugin: availability decides whether a capture runs");
        }

        [Test]
        public void Parameter_DoesNotDependOnThePlugin()
        {
            RoomPlanPlugin.Register(new Plugin { IsAvailable = false });
            var registry = new ParamRegistry();
            AppParams.DeclareScan(registry, new ScanSettings());
            registry.PushAll();
            Assert.That(WallBridge.Enabled, Is.True);

            RoomPlanPlugin.Register(null);
            registry.Set(AppParams.ScanWallBridge, 0f);
            Assert.That(WallBridge.Enabled, Is.False);
            registry.Set(AppParams.ScanWallBridge, 1f);
            Assert.That(WallBridge.Enabled, Is.True, "no plugin registered: nothing will be captured, so nothing is merged");
        }
    }
}
