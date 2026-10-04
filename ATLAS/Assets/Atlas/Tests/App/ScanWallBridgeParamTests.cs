using System;
using Atlas.App;
using Atlas.Link;
using Atlas.Scan;
using NUnit.Framework;

namespace Atlas.App.Tests
{
    /// <summary>scan.wallBridge, the kill switch of route 4: it drives WallBridge.Enabled together with the bridge's own Available.</summary>
    public class ScanWallBridgeParamTests
    {
        sealed class Bridge : IWallBridge
        {
            public bool IsAvailable = true;
            public int AvailableAsked;
            public string Name { get { return "fake"; } }
            public bool Available { get { AvailableAsked++; return IsAvailable; } }
            public bool Start(IntPtr nativeSession) { return true; }
            public string TakeJson() { return ""; }
            public void Stop() { }
        }

        [TearDown]
        public void RestoreTheBridge()
        {
            WallBridge.Register(null);
            WallBridge.Enabled = false;
        }

        [Test]
        public void Parameter_IsABoolDeclaredOnInGroupScan_AfterTheTeamPrescanOne()
        {
            var registry = new ParamRegistry();
            AppParams.DeclareScan(registry, new ScanSettings());
            ParamEntry entry = registry.Find(AppParams.ScanWallBridge);
            Assert.That(AppParams.ScanWallBridge, Is.EqualTo("scan.wallBridge"));
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Bool, Is.True, "on by default");
            Assert.That(entry.Id, Is.EqualTo(registry.Find(AppParams.TeamPrescan).Id + 1));
        }

        [Test]
        public void Parameter_SwitchesTheBridgeOnAndOff_WhenTheBridgeIsAvailable()
        {
            var bridge = new Bridge();
            WallBridge.Register(bridge);
            var registry = new ParamRegistry();
            AppParams.DeclareScan(registry, new ScanSettings());
            registry.PushAll();
            Assert.That(WallBridge.Enabled, Is.True, "default on");

            registry.Set(AppParams.ScanWallBridge, 0f);
            Assert.That(WallBridge.Enabled, Is.False);
            int asked = bridge.AvailableAsked;
            registry.Set(AppParams.ScanWallBridge, 1f);
            Assert.That(WallBridge.Enabled, Is.True);
            Assert.That(bridge.AvailableAsked, Is.GreaterThan(asked));
        }

        [Test]
        public void Parameter_LeavesTheBridgeOff_WhereItIsNotAvailable()
        {
            WallBridge.Register(new Bridge { IsAvailable = false });
            var registry = new ParamRegistry();
            AppParams.DeclareScan(registry, new ScanSettings());
            registry.PushAll();
            Assert.That(WallBridge.Enabled, Is.False);

            WallBridge.Register(null);
            registry.Set(AppParams.ScanWallBridge, 0f);
            registry.Set(AppParams.ScanWallBridge, 1f);
            Assert.That(WallBridge.Enabled, Is.False, "no bridge registered");
        }
    }
}
