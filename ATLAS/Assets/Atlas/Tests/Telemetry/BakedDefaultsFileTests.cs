using System;
using System.IO;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    /// <summary>The baked defaults file of the project, as <c>TelemetryInstaller</c> loads it.</summary>
    public class BakedDefaultsFileTests
    {
        static string DefaultsFile
        {
            get
            {
                string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
                return Path.Combine(project, "Assets", "Atlas", "Telemetry", "Resources",
                    TelemetryInstaller.BakedDefaultsResource.Replace('/', Path.DirectorySeparatorChar) + ".json");
            }
        }

        [Test]
        public void The_file_sits_where_Resources_Load_and_the_Hub_expect_it()
        {
            // The Hub's PresetStore.BakedDefaultsPath is "Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json".
            Assert.That(TelemetryInstaller.BakedDefaultsResource, Is.EqualTo("AtlasParams/defaults"));
            Assert.That(File.Exists(DefaultsFile), Is.True, DefaultsFile);
            Assert.That(File.Exists(DefaultsFile + ".meta"), Is.True, "Unity needs the .meta to import it");
        }

        [Test]
        public void The_file_parses_and_applies_to_a_registry_without_failing()
        {
            JsonValue baked = AtlasJson.Parse(File.ReadAllText(DefaultsFile));
            Assert.That(baked.IsObject, Is.True);
            Assert.That(baked["values"].IsObject, Is.True);

            var registry = new ParamRegistry();
            registry.DeclareBool(TelemetryInstaller.ParamHudVisible, "hud", false);
            Assert.DoesNotThrow(() => BakedDefaults.Apply(registry, baked));
            foreach (string name in new[] { TelemetryInstaller.ParamDemoLock, TelemetryInstaller.ParamLinkEnabled })
            {
                JsonValue ignored;
                Assert.That(baked["values"].TryGet(name, out ignored) && BakedDefaults.IsBakeable(name), Is.False, name);
            }
        }

        [Test]
        public void The_parameters_that_are_never_baked_are_those_the_installer_declares()
        {
            Assert.That(BakedDefaults.IsBakeable(TelemetryInstaller.ParamDemoLock), Is.False);
            Assert.That(BakedDefaults.IsBakeable(TelemetryInstaller.ParamLinkEnabled), Is.False);
            Assert.That(BakedDefaults.IsBakeable(TelemetryInstaller.ParamStateHz), Is.True);
            Assert.That(BakedDefaults.IsBakeable("curved.cullRho"), Is.True);
        }
    }
}
