using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Atlas.EditorTools
{
    /// <summary>
    /// Applies the Project, Player and URP settings of Master Plan Appendix C.4.
    /// Idempotent: run it from the Atlas menu, or with -executeMethod, after any settings merge.
    /// </summary>
    public static class AtlasProjectSetup
    {
        // One fixed bundle id for the whole project (C.1). A Personal Team allows 10 new ids per 7 days.
        const string BundleId = "com.winstonthov.atlas";

        const string SettingsDir = "Assets/Atlas/Rendering/Settings";
        const string UrpAssetPath = SettingsDir + "/Atlas_URP_iOS.asset";
        const string UrpRendererPath = SettingsDir + "/Atlas_URP_iOS_Renderer.asset";
        const string BootScenePath = "Assets/Atlas/Scenes/Atlas_Boot.unity";

        [MenuItem("Atlas/Apply Project Settings")]
        public static void Apply()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";

            ApplyPlayerSettings();
            ApplyUrpSettings();
            EnsureBootScene();

            AssetDatabase.SaveAssets();
            Debug.Log("Atlas: project settings applied (Master Plan Appendix C.4).");
        }

        static void ApplyPlayerSettings()
        {
            var ios = NamedBuildTarget.iOS;
            PlayerSettings.productName = "ATLAS";
            PlayerSettings.SetApplicationIdentifier(ios, BundleId);
            PlayerSettings.SetScriptingBackend(ios, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(ios, 1); // ARM64
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetManagedStrippingLevel(ios, ManagedStrippingLevel.Low);
            PlayerSettings.iOS.targetOSVersionString = "17.0";
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.cameraUsageDescription = AtlasUsageText.Camera;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.enableFrameTimingStats = true;

            // Static and dynamic batching off (C.4); the toggle has no public API.
            typeof(PlayerSettings)
                .GetMethod("SetBatchingForPlatform", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { BuildTarget.iOS, 0, 0 });

            // "Adjust iOS FPS based on thermal state" off: the thermal ladder owns quality (R5 C1).
            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            player.FindProperty("adjustIOSFPSUsingThermalState").boolValue = false;
            player.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ApplyUrpSettings()
        {
            if (!File.Exists(UrpAssetPath))
            {
                // Start from the template's assets so shader and renderer-feature references stay valid.
                Directory.CreateDirectory(SettingsDir);
                AssetDatabase.Refresh();
                AssetDatabase.CopyAsset("Assets/Settings/URP-Performant-Renderer.asset", UrpRendererPath);
                AssetDatabase.CopyAsset("Assets/Settings/URP-Performant.asset", UrpAssetPath);
            }

            var renderer = AssetDatabase.LoadMainAssetAtPath(UrpRendererPath);
            var r = new SerializedObject(renderer);
            r.FindProperty("m_RenderingMode").intValue = 0; // Forward
            r.FindProperty("m_DepthPrimingMode").intValue = 0; // Disabled
            r.FindProperty("postProcessData").objectReferenceValue = null;
            r.ApplyModifiedPropertiesWithoutUndo();

            var urp = (RenderPipelineAsset)AssetDatabase.LoadMainAssetAtPath(UrpAssetPath);
            var a = new SerializedObject(urp);
            a.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            a.FindProperty("m_SupportsHDR").boolValue = false;
            a.FindProperty("m_RequireDepthTexture").boolValue = false;
            a.FindProperty("m_RequireOpaqueTexture").boolValue = false;
            a.FindProperty("m_MSAA").intValue = 4;
            a.FindProperty("m_RenderScale").floatValue = 0.8f;
            a.FindProperty("m_UpscalingFilter").intValue = 0; // Auto, never STP
            a.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            a.FindProperty("m_AdditionalLightsRenderingMode").intValue = 0; // Disabled
            a.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            a.FindProperty("m_UseSRPBatcher").boolValue = true;
            a.FindProperty("m_GPUResidentDrawerMode").intValue = 0; // Disabled
            a.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = urp;

            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = quality.FindProperty("m_QualitySettings");
            for (var i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("vSyncCount").intValue = 1;
                level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = urp;
            }
            quality.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureBootScene()
        {
            if (File.Exists(BootScenePath))
                return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");

            // The pose is read once per frame, in Update (Master Plan §6.3); "before render" makes holonomy deltas jitter.
            foreach (var driver in Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsSortMode.None))
                driver.updateType = TrackedPoseDriver.UpdateType.Update;

            Directory.CreateDirectory(Path.GetDirectoryName(BootScenePath));
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }
    }
}
