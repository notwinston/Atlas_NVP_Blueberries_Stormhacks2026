#if UNITY_IOS
using System.IO;
using UnityEditor.iOS.Xcode;

namespace Atlas.EditorTools
{
    /// <summary>
    /// What the generated Xcode project needs to compile Assets/Plugins/iOS/AtlasRoomPlan.swift (scan route 4,
    /// the wall bridge): the file in the sources of UnityFramework, the target that holds Unity's native
    /// plugins and the IL2CPP code that calls the four exported functions; Swift settings for that target; the
    /// RoomPlan framework linked weakly. Every step looks before it writes, so applying it to a project twice
    /// gives the project of applying it once. It also keeps whatever Unity itself did with the .swift file:
    /// a file Unity has already added to the project is not added again.
    /// </summary>
    public static class AtlasRoomPlanProject
    {
        /// <summary>Where Unity puts a native plugin in the Xcode project, and where the file is added if Unity did not.</summary>
        public const string ProjectPath = "Libraries/Plugins/iOS/AtlasRoomPlan.swift";
        public const string AssetPath = "Plugins/iOS/AtlasRoomPlan.swift";
        public const string Framework = "RoomPlan.framework";
        public const string FrameworkName = "RoomPlan";

        /// <summary>
        /// Edits project in memory. frameworkTarget is the UnityFramework target, mainTarget the app.
        /// Returns true when the project text is now different from what it was.
        /// </summary>
        public static bool Apply(PBXProject project, string frameworkTarget, string mainTarget)
        {
            string before = project.WriteToString();

            // The source file. Unity adds plugin sources of the kinds it knows (.swift among them) to the sources of
            // UnityFramework; add it only when it is not in the project yet.
            // The name is looked for in the whole project text too, so a file Unity put under another path is not added twice.
            string file = project.FindFileGuidByProjectPath(ProjectPath) ?? project.FindFileGuidByRealPath(ProjectPath);
            if (file == null && !before.Contains("AtlasRoomPlan.swift"))
            {
                string added = project.AddFile(ProjectPath, ProjectPath, PBXSourceTree.Source);
                project.AddFileToBuild(frameworkTarget, added);
            }

            // RoomPlan weakly: the app starts where the framework is missing, and the Swift autolink entry that
            // would link it strongly is switched off for this one framework (the module import stays).
            if (!project.ContainsFramework(frameworkTarget, Framework))
                project.AddFrameworkToProject(frameworkTarget, Framework, true);
            // Added once to whatever the target already hands the Swift compiler, never in its place.
            string autolinkOff = "-disable-autolink-framework -Xfrontend " + FrameworkName;
            string swiftFlags = project.GetBuildPropertyForAnyConfig(frameworkTarget, "OTHER_SWIFT_FLAGS");
            if (string.IsNullOrEmpty(swiftFlags))
                project.SetBuildProperty(frameworkTarget, "OTHER_SWIFT_FLAGS", "-Xfrontend " + autolinkOff);
            else if (!swiftFlags.Contains(autolinkOff))
                project.SetBuildProperty(frameworkTarget, "OTHER_SWIFT_FLAGS", swiftFlags + " -Xfrontend " + autolinkOff);

            // Swift settings. Unity sets the first two itself when it finds Swift sources; a version that is
            // already chosen is left alone.
            if (string.IsNullOrEmpty(project.GetBuildPropertyForAnyConfig(frameworkTarget, "SWIFT_VERSION")))
                project.SetBuildProperty(frameworkTarget, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(frameworkTarget, "CLANG_ENABLE_MODULES", "YES");
            project.SetBuildProperty(mainTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
            project.SetBuildProperty(frameworkTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");

            return project.WriteToString() != before;
        }

        /// <summary>
        /// Puts the Swift file into the exported project folder when Unity has not (it is not overwritten when it
        /// is there). assetsFolder is the project's Assets folder.
        /// </summary>
        public static void CopySource(string assetsFolder, string buildPath)
        {
            string source = Path.Combine(assetsFolder, AssetPath);
            string target = Path.Combine(buildPath, ProjectPath);
            if (File.Exists(target) || !File.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target);
        }
    }
}
#endif
