using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Atlas.EditorTools
{
    /// <summary>
    /// Batch-mode iOS build: exports the Xcode project to the folder given by -buildOutput.
    /// Called by <c>tools/build_ios.sh</c> with <c>-executeMethod Atlas.EditorTools.AtlasBuild.IOS -buildOutput Build/iOS</c>.
    /// </summary>
    public static class AtlasBuild
    {
        public static void IOS()
        {
            var args = Environment.GetCommandLineArgs();
            var output = args[Array.IndexOf(args, "-buildOutput") + 1];

            // tools/build_ios.sh passes -atlasDevelopment for a development build; the default is release,
            // which is what every published number comes from (Master Plan §11).
            EditorUserBuildSettings.development = Array.IndexOf(args, "-atlasDevelopment") >= 0;

            var options = BuildOptions.None;
            // Append mode keeps the existing Xcode project, so Xcode rebuilds incrementally (Master Plan C.8).
            if (BuildPipeline.BuildCanBeAppended(BuildTarget.iOS, output) == CanAppendBuild.Yes)
                options |= BuildOptions.AcceptExternalModificationsToPlayer;
            if (EditorUserBuildSettings.development)
                options |= BuildOptions.Development;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.iOS,
                options = options,
            });

            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
