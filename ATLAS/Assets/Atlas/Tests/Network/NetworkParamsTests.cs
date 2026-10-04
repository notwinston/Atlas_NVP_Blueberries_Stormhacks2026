using System.Reflection;
using Atlas.Blades.Network.Core;
using Atlas.Link;
using Atlas.Telemetry;
using NUnit.Framework;

namespace Atlas.Blades.Network.Tests
{
    // NET-07: the parameters of contract section 6.3.
    public class NetworkParamsTests
    {
        [Test]
        public void Section64Params_DeclaredWithAtlasParam()
        {
            var registry = new ParamRegistry();
            using (var p = new NetworkParams(registry))
            {
                ParamEntry file = registry.Find("net.file"), view = registry.Find("net.view"), rule = registry.Find("net.rule");
                ParamEntry recentre = registry.Find("net.recentreSeconds"), gaze = registry.Find("net.maxGazeNodes");
                Assert.NotNull(file); Assert.NotNull(view); Assert.NotNull(rule); Assert.NotNull(recentre); Assert.NotNull(gaze);
                Assert.AreEqual(ParamKind.Enum, file.Kind); Assert.AreEqual(2, file.EnumCount);
                Assert.AreEqual(ParamKind.Enum, view.Kind); Assert.AreEqual(2, view.EnumCount);
                Assert.AreEqual(ParamKind.Enum, rule.Kind); Assert.AreEqual(2, rule.EnumCount);
                Assert.AreEqual(ParamKind.Float, recentre.Kind);
                Assert.AreEqual(0.3f, recentre.Min); Assert.AreEqual(1.5f, recentre.Max); Assert.AreEqual(0.6f, recentre.Default);
                Assert.AreEqual(ParamKind.Int, gaze.Kind);
                Assert.AreEqual(16f, gaze.Min); Assert.AreEqual(64f, gaze.Max); Assert.AreEqual(64f, gaze.Default);
                Assert.AreEqual(0, file.Int); Assert.AreEqual(0, view.Int); Assert.AreEqual(0, rule.Int);

                // Each is an [AtlasParam] field of NetworkParams, with the id of the contract.
                string[] ids = { "net.file", "net.view", "net.rule", "net.recentreSeconds", "net.maxGazeNodes" };
                int found = 0;
                foreach (FieldInfo f in typeof(NetworkParams).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    var a = (AtlasParamAttribute)System.Attribute.GetCustomAttribute(f, typeof(AtlasParamAttribute));
                    if (a != null) { CollectionAssert.Contains(ids, a.Id); found++; }
                }
                Assert.AreEqual(5, found);

                // The bound fields follow the registry, and Apply writes the view's plain fields.
                gaze.Set(24); recentre.Set(1.2f); rule.Set(1); file.Set(1);
                Assert.AreEqual(24, p.MaxGazeNodes);
                Assert.AreEqual(1.2f, p.RecentreSeconds);
                Assert.AreEqual(RoutingRule.Flat, p.Rule);
                Assert.AreEqual(NetworkFile.Seed83, p.File);
                p.SetRule(RoutingRule.Hyperbolic);
                Assert.AreEqual(0, rule.Int);
                Assert.AreEqual(RoutingRule.Hyperbolic, p.Rule);
            }
        }
    }
}
