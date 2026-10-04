#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Atlas.EditorTools
{
    /// <summary>
    /// Writes every Info.plist key and Xcode build setting the app needs (Master Plan Appendix C.5).
    /// Nobody edits the generated Xcode project by hand.
    /// </summary>
    static class AtlasPostBuild
    {
        [PostProcessBuild]
        static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
                return;

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;

            root.SetString("NSCameraUsageDescription", AtlasUsageText.Camera);
            root.SetString("NSLocalNetworkUsageDescription", AtlasUsageText.LocalNetwork);
            root.CreateDict("NSAppTransportSecurity").SetBoolean("NSAllowsLocalNetworking", true);
            root.SetBoolean("UIFileSharingEnabled", true);
            root.SetBoolean("UIRequiresFullScreen", true);

            // QR deep link atlas://<ip>:9050
            var urlType = root.CreateArray("CFBundleURLTypes").AddDict();
            urlType.SetString("CFBundleURLName", PlayerSettings.applicationIdentifier);
            urlType.CreateArray("CFBundleURLSchemes").AddString("atlas");

            if (EditorUserBuildSettings.development)
                root.SetBoolean("MetalHudEnabled", true);

            plist.WriteToFile(plistPath);

            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            foreach (var guid in new[] { project.ProjectGuid(), project.GetUnityMainTargetGuid(), project.GetUnityFrameworkTargetGuid() })
                project.SetBuildProperty(guid, "ENABLE_BITCODE", "NO");
            // Scan route 4: the RoomPlan wall bridge, in Swift, built into UnityFramework.
            AtlasRoomPlanProject.CopySource(Application.dataPath, path);
            AtlasRoomPlanProject.Apply(project, project.GetUnityFrameworkTargetGuid(), project.GetUnityMainTargetGuid());
            project.WriteToFile(projectPath);
        }
    }
}
#endif
