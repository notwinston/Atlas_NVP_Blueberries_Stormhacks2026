using System.IO;
using System.Reflection;
using Atlas.Rendering;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
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
    /// The render set-up (URP asset, renderer features, layers) is also applied by itself: when the Editor
    /// loads a project whose assets are behind this code, and at the start of every player build, so neither
    /// a rehearsal in the Editor nor the one-command build can run on stale settings.
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

        /// <summary>
        /// The part of <see cref="Apply"/> that the stereo rig, the head-locked layer and the doorways depend
        /// on: the URP asset, the renderer with its features, and the layer names. Idempotent, touches no
        /// scene, so it is safe at the start of a build.
        /// </summary>
        public static void ApplyRenderSetup()
        {
            ApplyUrpSettings();
            ApplyLayers();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// True when the assets on disk already carry the render set-up. A cheap check of the values that
        /// were wrong in an untouched template project, not a full comparison.
        /// </summary>
        public static bool IsRenderSetupCurrent()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (renderer == null || urp == null)
                return false;
            if (renderer.intermediateTextureMode != IntermediateTextureMode.Auto || urp.renderScale != 1f)
                return false;
            if ((renderer.opaqueLayerMask.value & PortalStencilTable.FeatureLayerMask) != 0)
                return false;
            if (LayerMask.LayerToName(PortalStencilTable.HudLayer) != PortalStencilTable.HudLayerName)
                return false;

            bool masks = false, lastRoom = false, hud = false, hudText = false;
            string lastRoomName = PortalStencilTable.FeatureName(PortalStencilTable.MaxSlots);
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature == null)
                    continue;
                masks |= feature.name == PortalStencilTable.MaskFeature;
                lastRoom |= feature.name == lastRoomName;
                hud |= feature.name == PortalStencilTable.HudFeature;
                hudText |= feature.name == PortalStencilTable.HudTextFeature;
            }
            return masks && lastRoom && hud && hudText;
        }

        // Opening the project (or recompiling) with assets that are behind this code brings them up to date,
        // so play mode in the Editor shows what the phone will show. Deferred: assets cannot be written
        // while the Editor is still loading.
        [InitializeOnLoadMethod]
        static void ApplyRenderSetupWhenStale()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                    return;
                if (IsRenderSetupCurrent())
                    return;
                ApplyRenderSetup();
                Debug.Log("Atlas: the render set-up on disk was out of date and has been applied; commit the changed settings assets.");
            };
        }

        /// <summary>
        /// Runs the render set-up before anything else in a player build, ahead of the render pipelines' own
        /// build steps (int.MinValue + 50 and + 100), which read the URP asset and its features for stripping.
        /// A batch-mode build runs no deferred Editor callback before its build method, so this is the step
        /// that keeps <c>tools/build_ios.sh</c> from shipping stale settings.
        /// </summary>
        sealed class BeforeBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => int.MinValue;

            public void OnPreprocessBuild(BuildReport report)
            {
                ApplyRenderSetup();
                if (!IsRenderSetupCurrent())
                    throw new BuildFailedException("Atlas: the render set-up could not be applied; see the errors above.");
                Debug.Log("Atlas: render set-up applied for the build.");
            }
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
            // The Forge and the Hub are plain http on the local network: UnityWebRequest refuses them otherwise.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

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

            // A layer drawn by a feature is taken out of the renderer's own passes, or it is drawn twice.
            int ownLayers = ~PortalStencilTable.FeatureLayerMask;
            renderer.opaqueLayerMask = renderer.opaqueLayerMask.value & ownLayers;
            renderer.transparentLayerMask = renderer.transparentLayerMask.value & ownLayers;

            EnsureLayerFeatures(renderer);

            EditorUtility.SetDirty(renderer);
        }

        // The Render Objects features of the layer table (lane A contract section 12.2): the doorway masks,
        // one feature per room layer with its stencil reference, and the head-locked layer on top of the
        // world. Found by name, so a second run changes nothing.
        static void EnsureLayerFeatures(UniversalRendererData renderer)
        {
            // Masks: the mask shader brings its own stencil and depth state. Off until a doorway is in view.
            RenderObjects masks = EnsureFeature(renderer, PortalStencilTable.MaskFeature);
            SetFilter(masks, PortalStencilTable.MaskEvent, PortalStencilTable.MaskLayer, RenderQueueType.Opaque);
            masks.settings.stencilSettings.overrideStencilState = false;
            masks.settings.overrideDepthState = false;
            FinishFeature(masks, false);

            // Rooms behind doorways: drawn only where the mask wrote their reference. A feature is switched
            // on by the portal renderer while its reference is in use.
            for (int reference = 1; reference <= PortalStencilTable.MaxSlots; reference++)
            {
                RenderObjects room = EnsureFeature(renderer, PortalStencilTable.FeatureName(reference));
                SetFilter(room, PortalStencilTable.PassEvent(reference), PortalStencilTable.Layer(reference), RenderQueueType.Opaque);
                StencilStateData stencil = room.settings.stencilSettings;
                stencil.overrideStencilState = true;
                stencil.stencilReference = reference;
                stencil.stencilCompareFunction = CompareFunction.Equal;
                stencil.passOperation = StencilOp.Keep;
                stencil.failOperation = StencilOp.Keep;
                stencil.zFailOperation = StencilOp.Keep;
                room.settings.overrideDepthState = false;
                FinishFeature(room, false);
            }

            // Head-locked content: after everything, never hidden by the world, in two queues.
            RenderObjects hud = EnsureFeature(renderer, PortalStencilTable.HudFeature);
            SetFilter(hud, PortalStencilTable.HudEvent, PortalStencilTable.HudLayer, RenderQueueType.Opaque);
            SetOnTop(hud);
            FinishFeature(hud, true);

            RenderObjects hudText = EnsureFeature(renderer, PortalStencilTable.HudTextFeature);
            SetFilter(hudText, PortalStencilTable.HudTextEvent, PortalStencilTable.HudLayer, RenderQueueType.Transparent);
            SetOnTop(hudText);
            FinishFeature(hudText, true);

            AssetDatabase.SaveAssetIfDirty(renderer);
        }

        // The feature of that name on the renderer, added the way the inspector adds one when it is missing.
        static RenderObjects EnsureFeature(UniversalRendererData renderer, string name)
        {
            foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
            {
                if (existing is RenderObjects found && found.name == name)
                    return found;
            }

            var feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var r = new SerializedObject(renderer);
            SerializedProperty features = r.FindProperty("m_RendererFeatures");
            SerializedProperty map = r.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            r.ApplyModifiedPropertiesWithoutUndo();
            return feature;
        }

        static void SetFilter(RenderObjects feature, int passEvent, int layer, RenderQueueType queue)
        {
            RenderObjects.RenderObjectsSettings s = feature.settings;
            s.passTag = feature.name;
            s.Event = (RenderPassEvent)passEvent;
            s.filterSettings.RenderQueueType = queue;
            s.filterSettings.LayerMask = 1 << layer;
            // The default, Material, would replace the surface shader with the feature's own material.
            s.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
            s.overrideMaterial = null;
            s.overrideShader = null;
            s.cameraSettings.overrideCamera = false;
        }

        static void SetOnTop(RenderObjects feature)
        {
            feature.settings.stencilSettings.overrideStencilState = false;
            feature.settings.overrideDepthState = true;
            feature.settings.depthCompareFunction = CompareFunction.Always;
            feature.settings.enableWrite = false;
        }

        static void FinishFeature(RenderObjects feature, bool active)
        {
            feature.Create();
            feature.SetActive(active);
            EditorUtility.SetDirty(feature);
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
            SetLayer(layers, PortalStencilTable.MaskLayer, PortalStencilTable.MaskLayerName);
            SetLayer(layers, PortalStencilTable.HudLayer, PortalStencilTable.HudLayerName);
            for (int reference = 1; reference <= PortalStencilTable.MaxSlots; reference++)
                SetLayer(layers, PortalStencilTable.Layer(reference), PortalStencilTable.LayerName(reference));
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
