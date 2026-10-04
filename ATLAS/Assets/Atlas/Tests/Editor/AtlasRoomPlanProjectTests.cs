#if UNITY_IOS
using Atlas.EditorTools;
using NUnit.Framework;
using UnityEditor.iOS.Xcode;

namespace Atlas.EditorTools.Tests
{
    /// <summary>
    /// The post-build edit of the Xcode project for AtlasRoomPlan.swift, applied to a small project text with
    /// the Unity Xcode API (it is plain managed code and needs no Editor). Not proved here: that Xcode builds
    /// the result (that needs an export of the real project and Xcode).
    /// </summary>
    public class AtlasRoomPlanProjectTests
    {
        const string Main = "00000000000000000000A001", Framework = "00000000000000000000A002";

        // Two native targets as Unity names them, one configuration each, empty source and framework phases.
        static string Skeleton()
        {
            const string phases = "isa = PBXSourcesBuildPhase;buildActionMask = 2147483647;files = ();runOnlyForDeploymentPostprocessing = 0;";
            string frameworks = phases.Replace("PBXSourcesBuildPhase", "PBXFrameworksBuildPhase");
            string text = @"// !$*UTF8*$!
{
archiveVersion = 1;
classes = {};
objectVersion = 54;
objects = {
00000000000000000000A000 = {isa = PBXProject;attributes = {};buildConfigurationList = 00000000000000000000A010;compatibilityVersion = ""Xcode 9.3"";developmentRegion = en;hasScannedForEncodings = 0;mainGroup = 00000000000000000000A003;projectDirPath = """";projectRoot = """";targets = (MAIN,FRAMEWORK,);};
00000000000000000000A003 = {isa = PBXGroup;children = (00000000000000000000A004,);sourceTree = ""<group>"";};
00000000000000000000A004 = {isa = PBXGroup;children = ();path = Libraries;sourceTree = ""<group>"";};
MAIN = {isa = PBXNativeTarget;buildConfigurationList = 00000000000000000000A011;buildPhases = (00000000000000000000A005,00000000000000000000A006,);buildRules = ();dependencies = ();name = ""Unity-iPhone"";productName = ""Unity-iPhone"";productType = ""com.apple.product-type.application"";};
FRAMEWORK = {isa = PBXNativeTarget;buildConfigurationList = 00000000000000000000A012;buildPhases = (00000000000000000000A007,00000000000000000000A008,);buildRules = ();dependencies = ();name = UnityFramework;productName = UnityFramework;productType = ""com.apple.product-type.framework"";};
00000000000000000000A005 = {PHASES};
00000000000000000000A006 = {FRAMEWORKS};
00000000000000000000A007 = {PHASES};
00000000000000000000A008 = {FRAMEWORKS};
00000000000000000000A020 = {isa = XCBuildConfiguration;buildSettings = {};name = Release;};
00000000000000000000A021 = {isa = XCBuildConfiguration;buildSettings = {};name = Release;};
00000000000000000000A022 = {isa = XCBuildConfiguration;buildSettings = {};name = Release;};
00000000000000000000A010 = {isa = XCConfigurationList;buildConfigurations = (00000000000000000000A020,);defaultConfigurationIsVisible = 0;defaultConfigurationName = Release;};
00000000000000000000A011 = {isa = XCConfigurationList;buildConfigurations = (00000000000000000000A021,);defaultConfigurationIsVisible = 0;defaultConfigurationName = Release;};
00000000000000000000A012 = {isa = XCConfigurationList;buildConfigurations = (00000000000000000000A022,);defaultConfigurationIsVisible = 0;defaultConfigurationName = Release;};
};
rootObject = 00000000000000000000A000;
}
";
            return text.Replace("{PHASES}", "{" + phases + "}").Replace("{FRAMEWORKS}", "{" + frameworks + "}")
                .Replace("MAIN", Main).Replace("FRAMEWORK,", Framework + ",").Replace("FRAMEWORK =", Framework + " =");
        }

        static PBXProject Read(string text)
        {
            var project = new PBXProject();
            project.ReadFromString(text);
            return project;
        }

        [Test]
        public void Skeleton_HasTheTwoTargetsUnityNames()
        {
            PBXProject project = Read(Skeleton());
            Assert.That(project.GetUnityMainTargetGuid(), Is.EqualTo(Main));
            Assert.That(project.GetUnityFrameworkTargetGuid(), Is.EqualTo(Framework));
        }

        [Test]
        public void Apply_AddsTheSwiftFileAndLinksRoomPlanWeakly()
        {
            PBXProject project = Read(Skeleton());
            Assert.That(AtlasRoomPlanProject.Apply(project, Framework, Main), Is.True);
            string text = project.WriteToString();

            Assert.That(project.FindFileGuidByProjectPath(AtlasRoomPlanProject.ProjectPath), Is.Not.Null);
            StringAssert.Contains("AtlasRoomPlan.swift in Sources", text);
            StringAssert.Contains("RoomPlan.framework in Frameworks", text);
            StringAssert.Contains("Weak", text);
            StringAssert.Contains("SWIFT_VERSION = 5.0", text);
            StringAssert.Contains("-disable-autolink-framework", text);
            StringAssert.Contains("ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES = YES", text);
            Assert.That(Count(text, "AtlasRoomPlan.swift in Sources */ = {isa"), Is.EqualTo(1));
        }

        [Test]
        public void Apply_TwiceGivesTheSameProjectAsOnce()
        {
            PBXProject once = Read(Skeleton());
            AtlasRoomPlanProject.Apply(once, Framework, Main);
            string first = once.WriteToString();

            // Applied to the project that was written out and read back, as a second build into the same folder does.
            PBXProject again = Read(first);
            Assert.That(AtlasRoomPlanProject.Apply(again, Framework, Main), Is.False);
            Assert.That(again.WriteToString(), Is.EqualTo(first));

            // And applied twice to the one project in memory.
            Assert.That(AtlasRoomPlanProject.Apply(once, Framework, Main), Is.False);
            Assert.That(once.WriteToString(), Is.EqualTo(first));
        }

        [Test]
        public void Apply_LeavesAFileUnityAlreadyAddedWhereItIs()
        {
            PBXProject project = Read(Skeleton());
            string guid = project.AddFile(AtlasRoomPlanProject.ProjectPath, AtlasRoomPlanProject.ProjectPath, PBXSourceTree.Source);
            project.AddFileToBuild(Framework, guid);
            AtlasRoomPlanProject.Apply(project, Framework, Main);
            Assert.That(Count(project.WriteToString(), "AtlasRoomPlan.swift in Sources */ = {isa"), Is.EqualTo(1));
        }

        static int Count(string text, string part)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(part, at, System.StringComparison.Ordinal)) >= 0) { n++; at += part.Length; }
            return n;
        }
    }
}
#endif
