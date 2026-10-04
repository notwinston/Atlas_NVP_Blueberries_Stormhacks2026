using System.IO;
using NUnit.Framework;

namespace Atlas.Blades.Network.Tests
{
    public class NetworkCoreRulesTests
    {
        [Test]
        public void Core_UsesNoUnityEngineType()
        {
            string dir = Path.Combine(NetworkTestEnv.ProjectDir, "Assets", "Atlas", "Blades", "Network", "Core");
            string[] files = Directory.GetFiles(dir, "*.cs");
            Assert.GreaterOrEqual(files.Length, 3);
            foreach (string f in files)
            {
                string text = File.ReadAllText(f);
                Assert.IsFalse(text.Contains("UnityEngine"), Path.GetFileName(f) + " mentions UnityEngine");
                Assert.IsFalse(text.Contains("UnityEditor"), Path.GetFileName(f) + " mentions UnityEditor");
            }
        }
    }
}
