using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Atlas.EditorTools
{
    /// <summary>
    /// Applies the Project, Player and URP settings of Master Plan Appendix C.4 and section 8.2.
    /// Idempotent: run it from the Atlas menu, or with
    /// -executeMethod Atlas.EditorTools.AtlasProjectSetup.Apply, after any settings merge.
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
            ApplyLayers();
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

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (renderer == null || urp == null)
            {
                Debug.LogError("Atlas: the URP asset or its renderer is missing under " + SettingsDir + ".");
                return;
            }

            ConfigureRenderer(renderer);
            ConfigureUrpAsset(urp, renderer);

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

        // The one renderer both eye cameras, the Screen camera and the hand-mode AR camera share.
        static void ConfigureRenderer(UniversalRendererData renderer)
        {
            renderer.renderingMode = RenderingMode.Forward;
            renderer.depthPrimingMode = DepthPrimingMode.Disabled;
            // "Always" (the default) gives every camera an intermediate colour texture as soon as any feature
            // is active, which costs a full-screen copy per eye and defeats the on-tile MSAA resolve.
            renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
            // No post-process data: URP then creates no post-processing pass at all.
            renderer.postProcessData = null;

            var r = new SerializedObject(renderer);
            // An override here would replace the stencil block of every shader, portals included.
            SetBool(r, "m_DefaultStencilState.overrideStencilState", false);
            r.ApplyModifiedPropertiesWithoutUndo();

            // The AR background feature stays: the hand-mode AR camera draws the camera image through this
            // renderer. It enqueues its pass only for a camera with an enabled ARCameraBackground, so with
            // intermediate textures on Auto the eye cameras and the Screen camera carry nothing from it.

            EditorUtility.SetDirty(renderer);
        }

        static void ConfigureUrpAsset(UniversalRenderPipelineAsset urp, UniversalRendererData renderer)
        {
            urp.supportsHDR = false;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            // Must stay above 1: with MSAA disabled here URP ignores the eye texture's own sample count.
            urp.msaaSampleCount = 4;
            // The eye-texture size carries the render scale. Any other value here shrinks the viewport inside
            // the eye texture of a camera that has a target texture.
            urp.renderScale = 1f;
            urp.upscalingFilter = UpscalingFilterSelection.Auto;   // never STP or FSR
            urp.useSRPBatcher = true;
            urp.supportsDynamicBatching = false;
            urp.useAdaptivePerformance = false;                    // the thermal ladder owns quality
            urp.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
            urp.storeActionsOptimization = StoreActionsOptimization.Auto;

            // These have no public setter. m_AnyShadowsSupported is the Editor-only mirror the internal
            // setters keep in step; it drives shader-keyword filtering, so it is written too.
            var a = new SerializedObject(urp);
            var renderers = a.FindProperty("m_RendererDataList");
            if (renderers.arraySize < 1)
                renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            SetInt(a, "m_DefaultRendererIndex", 0);
            SetInt(a, "m_MainLightRenderingMode", (int)LightRenderingMode.Disabled);
            SetInt(a, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.Disabled);
            SetBool(a, "m_MainLightShadowsSupported", false);
            SetBool(a, "m_AdditionalLightShadowsSupported", false);
            SetBool(a, "m_AnyShadowsSupported", false);
            SetBool(a, "m_SoftShadowsSupported", false);
            a.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(urp);
        }

        static void SetBool(SerializedObject target, string property, bool value)
        {
            SerializedProperty p = target.FindProperty(property);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning("Atlas: serialized property '" + property + "' not found on " + target.targetObject.name + ".");
        }

        static void SetInt(SerializedObject target, string property, int value)
        {
            SerializedProperty p = target.FindProperty(property);
            if (p != null) p.intValue = value;
            else Debug.LogWarning("Atlas: serialized property '" + property + "' not found on " + target.targetObject.name + ".");
        }

        // Layer names of the layer table (lane A contract section 12.1). Code uses the numbers.
        static void ApplyLayers()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            if (layers == null)
                return;
            SetLayer(layers, 6, "AtlasPortalMask");
            SetLayer(layers, 7, "AtlasHud");
            for (int i = 1; i <= 15; i++)
                SetLayer(layers, 7 + i, "AtlasRoom" + i.ToString("00"));
            SetLayer(layers, 23, "AtlasSpectator");
            tags.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayer(SerializedProperty layers, int index, string name)
        {
            if (index < layers.arraySize)
                layers.GetArrayElementAtIndex(index).stringValue = name;
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
