using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Atlas.EditorTools
{
    /// <summary>
    /// Batch-mode iOS build: exports the Xcode project to the folder given by -buildOutput.
    /// <c>unity build --target iOS --execute-method Atlas.EditorTools.AtlasBuild.IOS -o Build/iOS</c>
    /// </summary>
    public static class AtlasBuild
    {
        public static void IOS()
        {
            var args = Environment.GetCommandLineArgs();
            var output = args[Array.IndexOf(args, "-buildOutput") + 1];

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
