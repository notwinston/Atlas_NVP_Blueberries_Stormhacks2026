# URP rendering, engine and profiling API facts

Verified for Unity 6000.3.25f1, URP 17.3.0, Core RP Library 17.3.0, Burst 1.8.30, Collections 2.6.8, Mathematics 1.3.3.
No Unity Editor was available: every statement below was read from the installed sources, the engine XML docs, the engine
assemblies (reflection dump) or the project files. C# snippets in this page were compiled against the real assemblies in the
`ios`, `win` and `editor` configurations (no errors, no obsolete warnings). Shader snippets were **not** compiled; they copy
patterns from the shipped URP shaders and say so.

Source shorthand used in "Confirmed at":

| Short | Folder (under `ATLAS/Library/PackageCache/`) |
|---|---|
| `URP/` | `com.unity.render-pipelines.universal@a0dd9d2bd983/Runtime/` |
| `URP-SL/` | `com.unity.render-pipelines.universal@a0dd9d2bd983/ShaderLibrary/` and `.../Shaders/` |
| `CORE/` | `com.unity.render-pipelines.core@3f80e92e91c2/Runtime/` |
| `CORE-SL/` | `com.unity.render-pipelines.core@3f80e92e91c2/ShaderLibrary/` |
| `PS/` | `ATLAS/ProjectSettings/` |
| `XML` | `~/unity-managed/Editor/Data/Managed/UnityEngine/*.xml` |

## 0. Surprises that change the plan

1. **Render Graph is the only URP path.** Compatibility Mode is compiled out unless the scripting define `URP_COMPATIBILITY_MODE` is set. Legacy pass APIs (`Execute`, `OnCameraSetup`, `ConfigureTarget`, `Blit`, `colorAttachmentHandle`) still compile, with obsolete warnings, and do nothing (§2).
2. **The committed `Atlas_URP_iOS.asset` still differs from the plan in four places that matter**: `m_RenderScale` is 0.8 (for a camera with a `targetTexture` that shrinks the viewport inside the eye texture, so it must be 1.0), `m_AnyShadowsSupported` is 1 while both shadow flags are 0, `m_UseAdaptivePerformance` is 1, and its renderer `Atlas_URP_iOS_Renderer.asset` has `IntermediateTextureMode = Always` (any active feature then forces an intermediate colour texture and defeats the on-tile resolve) with the AR background feature still in it (§3, §4).
3. **The eye camera takes its MSAA level from `RenderTexture.antiAliasing` only when the URP asset's MSAA is above 1 and `Camera.allowMSAA` is true.** The asset value also becomes `QualitySettings.antiAliasing`, i.e. an MSAA back buffer for the lens camera unless `Screen.SetMSAASamples(1)` is called (URP only does that when exactly one camera renders) (§6.3).
4. **The on-tile resolve (colour `Resolve`, depth `DontCare`) holds only on a Metal player build without a visible watermark.** The Editor and D3D take the explicit-resolve path (`PlatformRequiresExplicitMsaaResolve`) with an intermediate texture and an extra blit; a visible development watermark makes URP store the MSAA colour, which contradicts a memoryless MSAA surface (what the engine does then is not readable from the sources, §6.4).
5. **A render-graph pass can write the screen only from a camera that targets it**, so the lens pass needs a third camera either way. Write `backBufferColor` and call `SwitchActiveTexturesToBackbuffer()` to stop URP adding its own final blit. URP sorts cameras by `(int)depth` (§7).
6. **ARSession overwrites the frame rate**: `matchFrameRateRequested` defaults to true and every `ARSession.Update` sets `Application.targetFrameRate` and `QualitySettings.vSyncCount = 0` (§12.4). Settings with no public setter (`mainLightRenderingMode`, shadow flags) and the editor-only `m_AnyShadowsSupported` mirror must be written through `SerializedObject` (§3).

## 1. Assemblies an `.asmdef` must reference

Engine types (`UnityEngine.CoreModule` and the other modules) need no asmdef reference. Package types do.

| Need | Namespace | Assembly definition name |
|---|---|---|
| `UniversalRenderPipelineAsset`, `UniversalRendererData`, `UniversalAdditionalCameraData`, `ScriptableRendererFeature`, `ScriptableRenderPass`, `RenderObjects`, `UniversalResourceData`, `StencilStateData`, `RenderPassEvent` | `UnityEngine.Rendering.Universal` | `Unity.RenderPipelines.Universal.Runtime` |
| `RenderGraph`, `TextureHandle`, `AccessFlags`, `IRasterRenderGraphBuilder`, `RasterGraphContext`, `TextureUVOrigin`, `ContextContainer`, `RTHandle`, `RTHandles`, `Blitter`, `RasterCommandBuffer`, `RenderGraphUtils` | `UnityEngine.Rendering`, `UnityEngine.Rendering.RenderGraphModule`, `...RenderGraphModule.Util` | `Unity.RenderPipelines.Core.Runtime` |
| `GPUResidentDrawerMode` (needed by `UniversalRenderPipelineAsset.gpuResidentDrawerMode`) | `UnityEngine.Rendering` | `Unity.RenderPipelines.GPUDriven.Runtime` (compile error CS0012 without it; verified) |
| Editor-only URP helpers (none used in this page) | `UnityEditor.Rendering.Universal` | `Unity.RenderPipelines.Universal.Editor` |
| `Mesh`, `Camera`, `Screen`, `Application`, `QualitySettings`, `SystemInfo`, `RenderTexture`, `Shader`, `Material`, `GL`, `StencilState`, `Watermark`, `ScriptableRenderContext`, `RenderPipelineManager`, `GraphicsFormat`, `FrameTimingManager`, `ProfilerRecorder`, `ProfilerMarker`, `SceneManager`, `NativeArray<T>`, `UnsafeUtility`, `IJob` | various | `UnityEngine.CoreModule` (implicit) |
| `Unity.Mathematics` types | `Unity.Mathematics` | `Unity.Mathematics` |
| `[BurstCompile]` | `Unity.Burst` | `Unity.Burst` |
| `NativeList<T>`, `UnsafeList<T>` (not `NativeArray<T>`) | `Unity.Collections` | `Unity.Collections` |
| `ProfilerCounterValue<T>`, `ProfilerCounter<T>` (not `ProfilerRecorder`, not `ProfilerMarker`) | `Unity.Profiling` | `Unity.Profiling.Core` |
| `XRSettings` (do not use, see §12.1) | `UnityEngine.XR` | `UnityEngine.VRModule` (implicit) |

Project facts: `Atlas.Rendering.asmdef` already references `Unity.RenderPipelines.Core.Runtime`, `Unity.RenderPipelines.Universal.Runtime`, `Unity.Mathematics`. `Atlas.Editor.asmdef` references only `Unity.InputSystem` and is `includePlatforms: ["Editor"]`, so it needs `Unity.RenderPipelines.Universal.Runtime` (and `Unity.RenderPipelines.GPUDriven.Runtime` if it touches `gpuResidentDrawerMode`) before the §3 and §8 editor code compiles. `Atlas.Spectator.asmdef` uses `includePlatforms: ["Editor", "WindowsStandalone64"]`.

## 2. Render Graph is the only path (compatibility mode is gone)

| Fact | Confirmed at |
|---|---|
| `RenderGraphSettings.enableRenderCompatibilityMode` returns `false` and its setter is a compile error (`[Obsolete(..., true)]`) unless `URP_COMPATIBILITY_MODE` is defined. The project asset has `m_EnableRenderCompatibilityMode: 0`. | `URP/Settings/RenderGraphSettings.cs:72`; `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset:376` |
| `UniversalRenderPipelineAsset.enableRenderGraph` is `[Obsolete]` and returns `true` without the define. The `m_EnableRenderGraph: 0` line in the global settings asset is a legacy field. | `URP/Data/UniversalRenderPipelineAsset.cs:1626` |
| The legacy ScriptableRenderPass API exists as obsolete no-ops: `Execute`, `OnCameraSetup`, `Configure`, `ConfigureTarget`, `ConfigureClear`, `ConfigureColorStoreAction`, `ResetTarget`, `Blit(...)`, `colorAttachmentHandle(s)` (return `null`), `k_CameraTarget` (is `null`), `OnFinishCameraStackRendering`, `ScriptableRendererFeature.SetupRenderPasses`. Overriding `Execute` compiles with CS0672 and the override is never called. Verified by compiling `override Execute` and `ConfigureTarget(colorAttachmentHandle)` against the real DLL. | `URP/Deprecated.cs:635-1196` (`CompatibilityScriptingAPIHidden`) |
| `ScriptableRenderPass.RecordRenderGraph(RenderGraph, ContextContainer)` is the only entry point that works. The base implementation logs a warning and the pass has no effect. | `URP/Passes/ScriptableRenderPass.cs:754` |
| `RenderGraph.nativeRenderPassesEnabled` defaults to `true` and URP never sets it. The renderer asset's `m_UseNativeRenderPass` (`ScriptableRendererData.useNativeRenderPass`) only feeds the compatibility path, so it has no effect on the frame. | `CORE/RenderGraph/RenderGraph.cs:493`; `URP/UniversalRendererRenderGraph.cs:597` |
| `ScriptableRendererFeature.useIntermediateTextures` is `[Obsolete]` and unused in 6000.3. | `URP/ScriptableRendererFeature.cs:46` |

```csharp
// Required pass shape (namespace UnityEngine.Rendering.Universal, assembly Unity.RenderPipelines.Universal.Runtime)
public abstract class ScriptableRenderPass {
    public RenderPassEvent renderPassEvent { get; set; }
    public bool requiresIntermediateTexture { get; set; }            // forces ALL passes of the frame through an intermediate texture
    public void ConfigureInput(ScriptableRenderPassInput passInput); // Depth | Normal | Color | Motion; call from AddRenderPasses
    public virtual void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData);
    public virtual void OnCameraCleanup(CommandBuffer cmd);          // still called
}
public abstract class ScriptableRendererFeature : ScriptableObject {
    public abstract void Create();                                   // also called from OnEnable/OnValidate and from the ScriptableRenderer constructor
    public abstract void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData); // once per camera; skipped while isActive == false
    public void SetActive(bool active);
    protected virtual void Dispose(bool disposing);
}
```
`ScriptableRenderer.EnqueuePass(ScriptableRenderPass pass)` (`URP/ScriptableRenderer.cs:1554`). `AddRenderPasses` runs for every camera that uses the renderer, including Scene View and Preview cameras; filter on `renderingData.cameraData.cameraType` (`ref CameraType`) or `.camera`.

## 3. `UniversalRenderPipelineAsset`: settings for forward, no HDR, no depth texture, mobile

Class `UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset`, assembly `Unity.RenderPipelines.Universal.Runtime`. "Now" is read from `Assets/Atlas/Rendering/Settings/Atlas_URP_iOS.asset` (guid `e2fe3d741e3d7436ba7059e44543e3eb`), the asset all three quality levels and `PS/GraphicsSettings.asset:40` reference. The template's `Assets/Settings/URP-Performant*.asset` files are not referenced by any project setting.

| Serialized name (line in `URP/Data/UniversalRenderPipelineAsset.cs`) | C# property | Setter | Plan value | Now |
|---|---|---|---|---|
| `m_RendererType` (470) | n/a | n/a | `UniversalRenderer` = 1 | 1 |
| `m_RendererDataList` (476), `m_DefaultRendererIndex` (477) | `rendererDataList` (`ReadOnlySpan<ScriptableRendererData>`) | none | one eye renderer; a second renderer for the Option R lens camera (§7) | 1 renderer (`Atlas_URP_iOS_Renderer`), index 0 |
| `m_RequireDepthTexture` (480) | `supportsCameraDepthTexture` | public | `false` | 0 |
| `m_RequireOpaqueTexture` (481) | `supportsCameraOpaqueTexture` | public | `false` | 0 |
| `m_SupportsHDR` (486), `m_HDRColorBufferPrecision` (487) | `supportsHDR`, `hdrColorBufferPrecision` | public | `false` | 0 |
| `m_MSAA` (488) | `msaaSampleCount` (int, `MsaaQuality`: Disabled=1, 2, 4, 8) | public | **4** (must be >1 for the eye RT's own count to apply, §6.3) | 4 |
| `m_RenderScale` (489) | `renderScale` | public | `1.0` (the eye texture size carries the 0.8; with a `targetTexture` URP scales the viewport, §6.2) | **0.8** |
| `m_UpscalingFilter` (490) | `upscalingFilter` (`Auto`=0, `Linear`, `Point`, `FSR`, `STP`) | public | `Auto` (no upscale at scale 1.0) | 0 |
| `m_MainLightRenderingMode` (537) | `mainLightRenderingMode` (`Disabled`=0, `PerPixel`=1, `PerVertex`=2) | **get only** | `Disabled` (no lit shader in the base) | 1 (PerPixel) |
| `m_MainLightShadowsSupported` (538) | `supportsMainLightShadows` | **get only** | `false` | 0 |
| `m_AdditionalLightsRenderingMode` (542) | `additionalLightsRenderingMode` | **get only** | `Disabled` | 0 |
| `m_AdditionalLightShadowsSupported` (544) | `supportsAdditionalLightShadows` | **get only** | `false` | 0 |
| `m_AnyShadowsSupported` (567, editor only; derived in the setters at 1312 and 1355) | none | n/a | `false` | **1** (stale: both flags above are 0) |
| `m_SoftShadowsSupported` (575) | `supportsSoftShadows` | **get only** | `false` | 1 |
| `m_UseSRPBatcher` (582) | `useSRPBatcher` (applied in `GraphicsSettings.useScriptableRenderPipelineBatching` at pipeline creation, `URP/UniversalRenderPipeline.cs:255`) | public | `true` | 1 |
| `m_SupportsDynamicBatching` (583) | `supportsDynamicBatching` | public | `false` | 0 |
| `m_StoreActionsOptimization` (602) | `storeActionsOptimization` (`Auto`=0, `Discard`=1, `Store`=2) | public | `Auto` | 0 |
| `m_UseAdaptivePerformance` (605) | `useAdaptivePerformance` (used only with `ENABLE_ADAPTIVE_PERFORMANCE`) | public | `false` | **1** |
| `m_ColorGradingMode` (608) | `colorGradingMode` | public | LDR | 0 |
| `m_GPUResidentDrawerMode` (622) | `gpuResidentDrawerMode` (`Disabled`=0, `InstancedDrawing`=1; type in the GPUDriven assembly) | public | `Disabled` | 0 |
| `m_EnableLODCrossFade` (506), `m_SupportDataDrivenLensFlare` (618), `m_SupportScreenSpaceLensFlare` (619) | `enableLODCrossFade`, `supportScreenSpaceLensFlare`, `supportDataDrivenLensFlare` (all get only) | n/a | off if unused (not required) | 1 / 1 / 1; `m_LODCrossFadeDitheringType: 1` (`BlueNoise`) |

Rules that follow from the source:

- Properties marked **get only** have an `internal` setter. An Editor script writes them with `SerializedObject.FindProperty("m_...")`. Because `supportsMainLightShadows` and `supportsAdditionalLightShadows` update the editor-only `m_AnyShadowsSupported` in their setters, write `m_AnyShadowsSupported` too (it drives shader-keyword filtering through `[ShaderKeywordFilter]`).
- `UniversalRenderPipelineAsset.Create(ScriptableRendererData rendererData = null)` exists in the editor block only (`URP/Data/UniversalRenderPipelineAsset.cs:716`, inside `#if UNITY_EDITOR` from line 701).
- Creating the pipeline sets `QualitySettings.antiAliasing = asset.msaaSampleCount` when they differ (`URP/UniversalRenderPipeline.cs:257-264`). The project's three quality levels (Performant, Balanced, High Fidelity) have `antiAliasing: 0`, `vSyncCount: 1` and the same `customRenderPipeline` (`PS/QualitySettings.asset`).
- `UniversalRenderPipelineAsset.GetRenderer(int)` recreates a renderer when its data is invalidated (`URP/Data/UniversalRenderPipelineAsset.cs:945`); see §8 for what that means at run time.

Editor code (compiled in the `editor` configuration; `gpuResidentDrawerMode` needs the GPUDriven assembly reference, or write `m_GPUResidentDrawerMode` through `SerializedObject` instead):

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Atlas.Editor
{
    static class RendererSetup
    {
        // Adds a Render Objects feature to a renderer asset, the same way the inspector does.
        public static RenderObjects AddPortalFeature(UniversalRendererData data, string name, string layerName, int stencilRef)
        {
            var feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(data);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();

            RenderObjects.RenderObjectsSettings s = feature.settings;
            s.passTag = name;
            s.Event = RenderPassEvent.AfterRenderingOpaques;
            s.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            s.filterSettings.LayerMask = 1 << LayerMask.NameToLayer(layerName);
            s.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
            s.stencilSettings.overrideStencilState = true;
            s.stencilSettings.stencilReference = stencilRef;
            s.stencilSettings.stencilCompareFunction = CompareFunction.Equal;
            s.stencilSettings.passOperation = StencilOp.Keep;
            s.stencilSettings.failOperation = StencilOp.Keep;
            s.stencilSettings.zFailOperation = StencilOp.Keep;
            feature.Create();

            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            return feature;
        }

        // Settings that have no public setter are written through the serialized names.
        public static void ConfigureUrpAsset(UniversalRenderPipelineAsset asset)
        {
            asset.supportsHDR = false;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.msaaSampleCount = 4;
            asset.renderScale = 1f;
            asset.useSRPBatcher = true;
            asset.supportsDynamicBatching = false;
            asset.useAdaptivePerformance = false;
            asset.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
            var so = new SerializedObject(asset);
            so.FindProperty("m_MainLightRenderingMode").intValue = (int)LightRenderingMode.Disabled;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.Disabled;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_AnyShadowsSupported").boolValue = false;        // editor-only mirror the setters keep in sync
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }

        public static void ConfigureRenderer(UniversalRendererData data)
        {
            data.renderingMode = RenderingMode.Forward;
            data.depthPrimingMode = DepthPrimingMode.Disabled;
            data.intermediateTextureMode = IntermediateTextureMode.Auto;
            data.postProcessData = null;
            EditorUtility.SetDirty(data);
        }
    }
}
#endif
```

## 4. `UniversalRendererData` (renderer asset)

Class `UnityEngine.Rendering.Universal.UniversalRendererData : ScriptableRendererData`, assembly `Unity.RenderPipelines.Universal.Runtime`. Setters call `SetDirty()`, which marks the renderer for re-creation.

| Serialized name (line) | C# property | Plan value | Now (`Atlas_URP_iOS_Renderer.asset`) |
|---|---|---|---|
| `m_RenderingMode` (`UniversalRendererData.cs:143`) | `renderingMode` (`Forward`=0, `Deferred`=1, `ForwardPlus`=2, `DeferredPlus`=3) | `Forward` | 0 |
| `m_DepthPrimingMode` (144) | `depthPrimingMode` (`Disabled`, `Auto`, `Forced`) | `Disabled` | 0 |
| `m_CopyDepthMode` (145) | `copyDepthMode` | irrelevant with no depth texture | 0 |
| `m_IntermediateTextureMode` (156, default **`Always`**) | `intermediateTextureMode` (`Auto`=0, `Always`=1) | **`Auto`** | **1** |
| `m_DefaultStencilState` (141) | `defaultStencilState` (`StencilStateData`) | `overrideStencilState = false` | false |
| `m_OpaqueLayerMask` (139), `m_TransparentLayerMask` (140), `m_PrepassLayerMask` (138) | `opaqueLayerMask`, `transparentLayerMask`, `prepassLayerMask` | exclude layers owned by Render Objects features | all layers (`4294967295`) |
| `m_DepthAttachmentFormat` (146), `m_DepthTextureFormat` (147) | `depthAttachmentFormat`, `depthTextureFormat` (`DepthFormat`: Default, Depth_16, Depth_24, Depth_32, Depth_16_Stencil_8, Depth_24_Stencil_8, Depth_32_Stencil_8) | `Default` (the eye camera's depth is the eye RT's, §6) | 0 |
| `postProcessData` (public field, `UniversalRendererData.cs:134`) | `postProcessData` | `null`: "No postProcessData means that post processes are disabled", the post-process passes are not created (`URP/UniversalRenderer.cs:456-464`) | null (already) |
| `m_RendererFeatures`, `m_RendererFeatureMap` (`ScriptableRendererData.cs:40-41`) | `rendererFeatures` (`List<ScriptableRendererFeature>`), map internal | no AR background feature on the eye renderer | `ARBackgroundRendererFeature` active |
| `m_UseNativeRenderPass` (42) | `useNativeRenderPass` | no effect (§2) | 0 |

`m_IntermediateTextureMode: Always` forces an intermediate colour texture for every camera as soon as the renderer has **any** feature with `isActive == true`, whether or not it enqueues a pass (`URP/UniversalRendererRenderGraph.cs:303-310`: `HasActiveRenderFeatures(rendererFeatures) && m_IntermediateTextureMode == Always`; `HasActiveRenderFeatures` is `URP/UniversalRenderer.cs:747-759`; `HasPassesRequiringIntermediateTexture`, which reads `requiresIntermediateTexture`, is `:761-773`). A pass can also force it alone with `requiresIntermediateTexture = true`. With `Auto`, a feature such as Render Objects that sets no input and no flag leaves the camera rendering straight into its target.

`m_DefaultStencilState.overrideStencilState = true` makes the opaque and transparent forward passes apply that stencil state to **every** draw and override a shader's own `Stencil` block (`URP/UniversalRenderer.cs:284-290`, `URP/Passes/DrawObjectsPass.cs:101-105`). Leave it false.

## 5. `UniversalAdditionalCameraData`

Class `UnityEngine.Rendering.Universal.UniversalAdditionalCameraData : MonoBehaviour`, `[RequireComponent(typeof(Camera))]`, `[DisallowMultipleComponent]`, assembly `Unity.RenderPipelines.Universal.Runtime`. A `Camera` with no such component is rendered as a Base camera with pipeline defaults (`URP/UniversalRenderPipelineCore.cs:1553`). Get or create it with the extension `public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera)` (`URP/UniversalAdditionalCameraData.cs:182`, adds the component when missing).

| Member | Serialized name | Value for our rig | Gotcha |
|---|---|---|---|
| `CameraRenderType renderType {get;set;}` (`Base`, `Overlay`) | `m_CameraType` | `Base` for all three cameras (no stacking) | an Overlay camera is forced to `clearFlags = Nothing` in `Start` and `OnValidate` |
| `bool requiresDepthTexture {get;set;}` (and `requiresDepthOption`: `Off`, `On`, `UsePipelineSettings`) | `m_RequiresDepthTextureOption` | `false` | the getter reads `UniversalRenderPipeline.asset`, which is null when no URP asset is active |
| `bool requiresColorTexture {get;set;}` | `m_RequiresOpaqueTextureOption` | `false` | same |
| `bool renderPostProcessing {get;set;}` | `m_RenderPostProcessing` | `false` | default is false |
| `AntialiasingMode antialiasing {get;set;}` (`None`, `FastApproximateAntialiasing`, `SubpixelMorphologicalAntiAliasing`, `TemporalAntiAliasing`) | `m_Antialiasing` | `None` | MSAA is not this field |
| `void SetRenderer(int index)` | `m_RendererIndex` (-1 = default) | eye cameras 0, lens camera the lens renderer index | an invalid index logs a warning and falls back to the default renderer (`URP/UniversalAdditionalCameraData.cs:669-680`) |
| `bool allowXRRendering {get;set;}` | `m_AllowXRRendering` (default true) | `false` | `cameraData.xrRendering = allowXRRendering && XRSystem.displayActive` (`URP/UniversalRenderPipeline.cs:1471`); the ARKit loader creates no `XRDisplaySubsystem`, so XR rendering stays off either way |
| `List<Camera> cameraStack {get;}` | `m_Cameras` | not used | returns `null` with a warning on an Overlay camera or when the renderer does not support stacking (`URP/UniversalAdditionalCameraData.cs:578`) |
| `bool renderShadows {get;set;}` | `m_RenderShadows` | `false` | |
| `bool stopNaN`, `bool dithering` | `m_StopNaN`, `m_Dithering` | `false` | extra work in post-processing only |
| `LayerMask volumeLayerMask` | `m_VolumeLayerMask` | irrelevant | volumes are not used |

Camera fields that matter next to it (`UnityEngine.Camera`): `allowMSAA` (default true), `allowHDR`, `allowDynamicResolution`, `depth` (float), `rect`, `cullingMask`, `clearFlags`, `targetTexture`, `projectionMatrix`, `worldToCameraMatrix`.

## 6. Two cameras, each into its own render texture, 4x MSAA

### 6.1 `RenderTextureDescriptor` fields

`UnityEngine.RenderTextureDescriptor` (struct, `UnityEngine.CoreModule`); constructor `RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat)` exists. `GraphicsFormat`, `GraphicsFormatUsage`, `DefaultFormat` are in namespace `UnityEngine.Experimental.Rendering`.

| Field | Type | XML meaning | For the eye texture |
|---|---|---|---|
| `msaaSamples` | `int` | multisample level, same as `RenderTexture.antiAliasing` | 4 (2 on the ladder rung) |
| `memoryless` | `RenderTextureMemoryless` flags | `Color` = colour memoryless when antiAliasing is 1; `MSAA` = colour memoryless when antiAliasing is 2, 4 or 8; `Depth` = depth memoryless | `MSAA | Depth` only when `SystemInfo.supportsMemorylessTextures` |
| `bindMS` | `bool` | "If true and msaaSamples is greater than 1, the render texture will not be resolved by default. Use this if the render texture needs to be bound as a multisampled texture in a shader." | `false` (we sample the resolved colour) |
| `depthStencilFormat` | `GraphicsFormat` | depth/stencil buffer format | must not be `None`, and must carry stencil for portals (use `SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil)`, the value URP uses for its own targets, `URP/UniversalRenderPipelineCore.cs:1601`) |
| `graphicsFormat` | `GraphicsFormat` | colour format | §11 |
| `useMipMap`, `autoGenerateMips` | `bool` | | `false`, `false` |
| `sRGB` | `bool` | | follows `graphicsFormat` when that is set |

`DefaultFormat.LDR` doc: "If the project uses the linear rendering mode, the actual format is sRGB. If the project uses the gamma rendering mode, the actual format is UNorm." The project is linear (`PS/ProjectSettings.asset:50`, `m_ActiveColorSpace: 1`).

### 6.2 `Camera.targetTexture` with URP: what URP does with it

| Fact | Confirmed at |
|---|---|
| MSAA samples of the camera = `camera.targetTexture.antiAliasing` **only if** `camera.allowMSAA && asset.msaaSampleCount > 1 && renderer supports MSAA`; otherwise 1. With the asset at `Disabled` an MSAA 4x eye texture renders without MSAA. | `URP/UniversalRenderPipeline.cs:1407-1411` |
| The camera descriptor is copied from `camera.targetTexture.descriptor`, `msaaSamples` replaced, `bindMS = false`, width and height replaced by the **scaled viewport**: `scaledWidth = (int)(camera.pixelWidth * cameraData.renderScale)` with `renderScale` taken from the asset, so an asset `renderScale` of 0.8 renders into the lower-left 80 % of each eye texture and sets `imageScalingMode = Upscaling`. Keep the asset at 1.0 (`disableRenderScale` is applied when it is within a threshold of 1). | `URP/UniversalRenderPipelineCore.cs:1612-1631`; `URP/UniversalRenderPipeline.cs:1371-1372`, `:1507-1508`, `:1533-1537` |
| `msaaSamples` is clamped with `SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc)`, and forced to 1 when `!SystemInfo.supportsStoreAndResolveAction`. | `URP/UniversalRenderPipelineCore.cs:1637-1642` |
| The render texture is imported into the graph as `backBufferColor` / `backBufferDepth` with `msaaSamples = targetTexture.antiAliasing`, `format = targetTexture.graphicsFormat`, depth `format = targetTexture.depthStencilFormat`. | `URP/UniversalRendererRenderGraph.cs:1796-1803` |
| If `depthStencilFormat == None`, URP logs "the output Render Texture must have a depth buffer" and substitutes the default depth-stencil format. | `URP/UniversalRendererRenderGraph.cs:1806-1811` |
| Base cameras clear the imported target on first use when no intermediate texture is needed (`clearOnFirstUse`), so the first pass uses a Clear load action, not Load. | `URP/UniversalRendererRenderGraph.cs:1694` |
| Cameras are sorted by `(int)camera1.depth - (int)camera2.depth` with `List<Camera>.Sort` (unstable). Use whole numbers, one per camera. | `URP/UniversalRenderPipelineCore.cs:1528-1534` |
| `Camera.rect` other than the full rect makes the camera need an intermediate texture (`!isDefaultViewport`). | `URP/UniversalRenderer.cs:2042` |

### 6.3 Screen MSAA and the lens camera

- `QualitySettings.antiAliasing` is set to the asset's MSAA at pipeline creation (`URP/UniversalRenderPipeline.cs:257-264`). With the asset at 4x, `Screen.msaaSamples` is 4 and the back buffer is imported with that many samples for a camera that targets the screen (`URP/UniversalRendererRenderGraph.cs:1762`, `URP/ScriptableRenderer.cs:2360-2381`).
- URP lowers it only through `Screen.SetMSAASamples(1)`, and only when `UniversalRenderPipeline.canOptimizeScreenMSAASamples` (true only when the camera list the engine passes to `Render` holds exactly one camera, `cameraCount == 1`, `URP/UniversalRenderPipeline.cs:455`, `:2620`; the AR camera and any other Game camera count), the camera uses an intermediate colour target, native render passes are on and `Screen.msaaSamples > 1` (`URP/ScriptableRenderer.cs:2368-2376`). With three cameras it never fires.
- Call `Screen.SetMSAASamples(int)` (`UnityEngine.CoreModule`, XML: "Sets the given number of MSAA samples for the screen buffer") once from the boot code after the pipeline exists; URP's own comment says that on iOS and macOS the change applies one frame later (`URP/UniversalRenderPipeline.cs:208-213`). Set `Camera.allowMSAA = false` on the lens camera.
- `SystemInfo.supportsMultisampledBackBuffer` is the capability flag; on a platform without it `AdjustAndGetScreenMSAASamples` returns 1.

### 6.4 On-tile resolve and store actions (what the source guarantees)

For the imported eye texture, which is the last writer in its camera's graph:

| Case | Colour | Depth | Confirmed at |
|---|---|---|---|
| `discardOnLastUse` set, colour MSAA > 1, not `bindMS` | `RenderBufferStoreAction.Resolve` (MSAA samples discarded, resolved texture stored) | `DontCare` | `CORE/RenderGraph/Compiler/NativePassCompiler.cs:1556-1560`; `CORE/RenderGraph/RenderGraphResourceRegistry.cs:63-68` ("If MSAA enabled, only the multisampled version is discarded while the MSAA surface is always resolved") |
| `discardOnLastUse` not set | `StoreAndResolve` | stored | `NativePassCompiler.cs:1563-1575` |

URP sets colour `discardOnLastUse = !m_RequiresIntermediateAttachments && !isNativeRenderingAfterURP && msaa > 1` and depth `discardOnLastUse = true` (`URP/UniversalRendererRenderGraph.cs:1704-1722`). So the result matches the plan only when **all** of these hold:

| Condition | Why it breaks | Fix |
|---|---|---|
| No intermediate attachments. Renderer level: `IntermediateTextureMode.Auto` (or no active feature), no pass with `requiresIntermediateTexture`, no depth copy (`requireCopyFromDepth`). Camera level, for a camera with a `targetTexture`: no post-processing, no opaque texture (`requiresColorTexture` or a pass that declares `Color`), no explicit MSAA resolve (next row), full `Camera.rect` | otherwise the camera renders into its own MSAA texture and a final blit copies into the eye texture (`URP/UniversalRendererRenderGraph.cs:303-310`; `URP/UniversalRenderer.cs:2006-2044`, where an offscreen camera returns `requiresBlitForOffscreenCamera` = `applyPostProcessing \|\| requiresOpaqueTexture \|\| requiresExplicitMsaaResolve \|\| !isDefaultViewport`) | §3, §4 |
| `PlatformRequiresExplicitMsaaResolve()` is false | it returns **true in the Editor** and true on every platform that is not (`supportsMultisampleAutoResolve` and mobile) or Metal, so Windows D3D and the Editor always resolve through an intermediate texture and a blit (`URP/UniversalRenderer.cs:1985-1998`) | none: Editor and Station renders do not show the device store actions; check on the phone only |
| `Watermark.IsVisible()` is false | `isNativeRenderingAfterURP` becomes true and URP stores the MSAA colour (`URP/UniversalRendererRenderGraph.cs:1704`); XML: `Watermark.showDeveloperWatermark` "Indicates and controls if the development build watermark should be drawn", `IsVisible()` "depends on your build settings and Unity plan" | set `UnityEngine.Rendering.Watermark.showDeveloperWatermark = false` at boot and log `IsVisible()` once |
| `SystemInfo.supportsStoreAndResolveAction` is true | otherwise URP sets MSAA to 1 for the camera (`URP/UniversalRenderPipelineCore.cs:1641`) | log it once; 1 means the eye textures have no MSAA |
| The target is not an offscreen depth texture | different path | n/a |

Memoryless: the compiler's `memoryLess` flag is for graph-created transient resources. For an imported render texture it does not read `RenderTextureDescriptor.memoryless`; the compiler throws "This pass is storing or resolving a resource marked as memoryless" in the Editor and development builds if a resource it marked memoryless gets a store (`NativePassCompiler.cs:1581-1591`, `CORE/RenderGraph/RenderGraph.ExceptionMessages.cs:135-138`). `TextureDesc(RenderTexture)` copies `memoryless` into the descriptor (`CORE/RenderGraph/RenderGraphResourceTexture.cs:476`). What the engine does with the flag on an MSAA surface that URP stores cannot be read from these sources. The Xcode capture (P1-A05) is the only proof.

Pass merging: the draw passes of one camera attach the same colour and depth (`SetRenderAttachment(activeColorTexture)`, `SetRenderAttachmentDepth(activeDepthTexture)`, see `URP/Passes/RenderObjectsPass.cs:290-292`) and the native pass compiler merges consecutive raster passes unless one of `PassBreakReason` applies: `TargetSizeMismatch`, `NextPassReadsTexture`, `NextPassTargetsTexture`, `NonRasterPass`, `DifferentDepthTextures`, `AttachmentLimitReached`, `SubPassLimitReached`, `FRStateMismatch`, `ExtendedFeatureFlagsIncompatible`, `PassMergingDisabled` (`CORE/RenderGraph/Compiler/PassesData.cs:597-614`). A custom pass between two draw passes must not sample the colour or depth as a texture, must not use an unsafe or compute pass, and must write the same attachments.

The Render Graph Viewer in the Editor therefore shows the explicit-resolve structure, not the device's. The Xcode capture is the check for the on-tile claims.

### 6.5 Compile-checked helpers

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Atlas.Rendering
{
    public sealed class EyeRigExample : MonoBehaviour
    {
        public Camera left, right, lens;
        public Material lensMaterial;

        void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += OnBeginContext;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }

        // Called once per frame before any camera is culled or rendered.
        static void OnBeginContext(ScriptableRenderContext context, List<Camera> cameras) { }
        static void OnBeginCamera(ScriptableRenderContext context, Camera camera) { }
        static void OnEndCamera(ScriptableRenderContext context, Camera camera) { }

        public static void ConfigureEye(Camera cam, RenderTexture target, int order, Matrix4x4 projection)
        {
            cam.targetTexture = target;
            cam.depth = order;                         // URP sorts by (int)depth
            cam.rect = new Rect(0, 0, 1, 1);
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.allowDynamicResolution = false;
            cam.projectionMatrix = projection;
            UniversalAdditionalCameraData d = cam.GetUniversalAdditionalCameraData();
            d.renderType = CameraRenderType.Base;
            d.renderPostProcessing = false;
            d.antialiasing = AntialiasingMode.None;
            d.requiresDepthTexture = false;
            d.requiresColorTexture = false;
            d.renderShadows = false;
            d.allowXRRendering = false;
            d.SetRenderer(0);
        }

        public static void ConfigureLens(Camera cam, int rendererIndex)
        {
            cam.targetTexture = null;
            cam.depth = 100;
            cam.cullingMask = 0;
            cam.clearFlags = CameraClearFlags.Nothing;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            UniversalAdditionalCameraData d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = false;
            d.allowXRRendering = false;
            d.SetRenderer(rendererIndex);
            UnityEngine.Rendering.Watermark.showDeveloperWatermark = false;
            Screen.SetMSAASamples(1);
        }

        public static Matrix4x4 OffAxis(float tanLeft, float tanRight, float tanDown, float tanUp, float near, float far)
            => Matrix4x4.Frustum(-tanLeft * near, tanRight * near, -tanDown * near, tanUp * near, near, far);

        public static AsyncOperation LoadAdditive(string scene)
            => Application.CanStreamedLevelBeLoaded(scene) ? SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive) : null;
    }
}
```

`RenderPipelineManager` (`UnityEngine.Rendering`, `UnityEngine.CoreModule`), signatures from the reflection dump:

| Member | Signature | When |
|---|---|---|
| `beginContextRendering` / `endContextRendering` | `static event Action<ScriptableRenderContext, List<Camera>>` | once per `Render` call, before the camera sort and after the last camera (`URP/UniversalRenderPipeline.cs:415-440`, `:487`) |
| `beginCameraRendering` / `endCameraRendering` | `static event Action<ScriptableRenderContext, Camera>` | per camera, base and overlay, **before** URP reads that camera's matrices and creates its camera data (`URP/UniversalRenderPipeline.cs:1057`, `:1160`); changing the transform or `projectionMatrix` in `beginCameraRendering` is still seen (URP comment at `:1074`) |
| `beginFrameRendering` / `endFrameRendering` | `Action<ScriptableRenderContext, Camera[]>` | `[Obsolete]` since 6000.1, use the context events |
| `RenderPipeline.Render(ScriptableRenderContext, Camera[])` | | `[Obsolete]`, override the `List<Camera>` form |

## 7. One final full-screen pass that samples both eye textures

Both options need a **third camera** (the lens camera, `targetTexture = null`, highest `depth`, `cullingMask = 0` or only the lens layer, `allowMSAA = false`). A render-graph pass only exists inside a camera's graph, and only a camera that targets the screen gets the screen as `backBufferColor`. Eye cameras (`depth` 0 and 1) render first because of the `(int)depth` sort (§6.2). The lens camera is the last base camera (`GetLastBaseCameraIndex`), so URP draws any Screen Space - Overlay canvas into it after the `AfterRendering` passes (`URP/UniversalRendererRenderGraph.cs:1544-1560`).

### 7.1 Option R: `ScriptableRendererFeature` and a raster render pass

Exact API used (all in `Unity.RenderPipelines.Core.Runtime` unless noted):

| Call | Signature | Confirmed at |
|---|---|---|
| add the pass | `IRasterRenderGraphBuilder RenderGraph.AddRasterRenderPass<PassData>(string passName, out PassData passData, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) where PassData : class, new()`; overload with `ProfilingSampler sampler` | `CORE/RenderGraph/RenderGraph.cs:1349`, `:1368` |
| import an eye texture | `TextureHandle RenderGraph.ImportTexture(RTHandle rt)`; `(RTHandle, ImportResourceParams)`; `(RTHandle, RenderTargetInfo, ImportResourceParams = default)` only for handles wrapping a `RenderTargetIdentifier` | `RenderGraph.cs:777`, `:811`, `:834` |
| wrap a render texture | `static RTHandle RTHandles.Alloc(RenderTexture tex, bool transferOwnership = false)`; `static void RTHandles.Release(RTHandle)`; the handle's `.rt` is the texture and it is not owned unless `transferOwnership` | `CORE/Textures/RTHandles.cs:722`, `:780`; `RTHandleSystem.cs:1452-1466` |
| declare reads | `void IBaseRenderGraphBuilder.UseTexture(in TextureHandle input, AccessFlags flags = AccessFlags.Read)` | `CORE/RenderGraph/IRenderGraphBuilder.cs:17` |
| declare the target | `void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Write)`, `void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags = AccessFlags.Write)` (default interface methods on `IRenderAttachmentRenderGraphBuilder`) | `IRenderGraphBuilder.cs:163`, `:209` |
| draw | `void IRasterRenderGraphBuilder.SetRenderFunc<PassData>(BaseRenderFunc<PassData, RasterGraphContext> renderFunc)`; `RasterGraphContext.cmd` is a `RasterCommandBuffer`; `RasterGraphContext.GetTextureUVOrigin(in TextureHandle)` | `IRenderGraphBuilder.cs:389` |
| full-screen triangle | `static void Blitter.BlitTexture(RasterCommandBuffer cmd, Vector4 scaleBias, Material material, int pass)` draws a procedural triangle and sets `_BlitScaleBias`; it binds **no** source. The overload with `RTHandle source` binds `_BlitTexture`. | `CORE/Utilities/Blitter.cs:1020`, `:770` |
| URP frame data | `UniversalResourceData` from `frameData.Get<UniversalResourceData>()`: `activeColorTexture`, `backBufferColor`, `backBufferDepth`, `isActiveTargetBackBuffer`, `SwitchActiveTexturesToBackbuffer()` | `URP/FrameData/UniversalResourceData.cs:20`, `:46`, `:84`, `:102` |
| helper that does **not** fit | `RenderGraphUtils.AddBlitPass(this RenderGraph, in BlitMaterialParameters, string passName, ...)` takes one `source`; the second eye texture would need a property block (banned by F7) or a global texture | sample `Samples~/URPRenderGraphSamples/BlitWithMaterial/BlitAndSwapColorRendererFeature.cs` |

Behaviour that matters:

- `SwitchActiveTexturesToBackbuffer()` doc: "URP will not add the final blit pass if this method is called before that render pass" (`URP/FrameData/UniversalResourceData.cs:36-45`). URP's own final blit is inline logic after `AfterRenderingPostProcessing` and before `AfterRendering` (`URP/UniversalRendererRenderGraph.cs:1517-1539`). Without the call, and with an intermediate camera texture, URP's inline final blit runs after our pass and copies the (unwritten) camera colour over the screen.
- `ImportTexture` doc: a pass that writes an imported texture "will be considered having side effects and can't be automatically culled" (`CORE/RenderGraph/RenderGraph.cs:770`).
- Inside the render function a `TextureHandle` converts implicitly to `Texture`, `RTHandle`, `RenderTexture` (`CORE/RenderGraph/RenderGraphResourceTexture.cs:131-152`). Outside it, the conversion is invalid.
- URP's own final blit sets the material texture inside the render function (`URP/Passes/FinalBlitPass.cs:339-340`: `SetKeyword(LinearToSRGBConversion, ...)` then `material.SetTexture(sourceID, source)`), so `Material.SetTexture` in a render function is the sanctioned way to feed a custom shader.
- Eye textures imported with `ImportTexture(RTHandle)` get `TextureUVOrigin.BottomLeft` (`CORE/RenderGraph/RenderGraphResourceRegistry.cs:456`). URP imports the screen with `TopLeft` when `SystemInfo.graphicsUVStartsAtTop` (`URP/UniversalRendererRenderGraph.cs:1710-1717`). URP's own rule for the blit is `flip = origin(source) != origin(destination)` then `scaleBias = flip ? (sx, -sy, 0, sy) : (sx, sy, 0, 0)` (`URP/RenderingUtils.cs:1149-1157`, an `internal` method, so the snippet repeats it with scale 1).
- Use a second renderer for the lens camera. It keeps the lens feature off the eye cameras (the alternative is an `AddRenderPasses` camera check) and lets the lens camera run `IntermediateTextureMode.Auto` with no other feature. Add a second entry to `m_RendererDataList` of the URP asset through `SerializedObject` (`rendererDataList` has no setter), then call `UniversalAdditionalCameraData.SetRenderer(index)` on the lens camera.

```csharp
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Atlas.Rendering
{
    /// <summary>Eye render textures: one per eye, 4x MSAA, memoryless MSAA colour and depth.</summary>
    public static class EyeTargets
    {
        public static RenderTexture Create(int width, int height, int msaa, string name)
        {
            // LDR default is sRGB in a linear project (DefaultFormat.LDR doc); depth must carry stencil for portals.
            var desc = new RenderTextureDescriptor(width, height,
                SystemInfo.GetGraphicsFormat(DefaultFormat.LDR), SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil))
            {
                msaaSamples = msaa,
                useMipMap = false,
                autoGenerateMips = false,
                bindMS = false,
                memoryless = SystemInfo.supportsMemorylessTextures
                    ? RenderTextureMemoryless.MSAA | RenderTextureMemoryless.Depth
                    : RenderTextureMemoryless.None,
            };
            var rt = new RenderTexture(desc) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }
    }

    /// <summary>Single final pass: samples both eye textures and writes the back buffer.</summary>
    public sealed class LensBlitFeature : ScriptableRendererFeature
    {
        [SerializeField] Material m_Material;
        LensBlitPass m_Pass;

        // Set from code once the eye textures exist (RTHandles are allocated once, not per frame).
        public static RenderTexture LeftEye, RightEye;

        public override void Create()
        {
            m_Pass = new LensBlitPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;   // not Scene View, Preview or Reflection
            if (m_Material == null || LeftEye == null || RightEye == null) return;
            m_Pass.Setup(m_Material, LeftEye, RightEye);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing) { m_Pass?.Release(); }
    }

    sealed class LensBlitPass : ScriptableRenderPass
    {
        static readonly int s_EyeL = Shader.PropertyToID("_EyeL");
        static readonly int s_EyeR = Shader.PropertyToID("_EyeR");

        sealed class PassData
        {
            public Material material;
            public TextureHandle left, right, screen;
        }

        Material m_Material;
        RTHandle m_Left, m_Right;

        public void Setup(Material material, RenderTexture left, RenderTexture right)
        {
            m_Material = material;
            if (m_Left == null || m_Left.rt != left) { RTHandles.Release(m_Left); m_Left = RTHandles.Alloc(left); }
            if (m_Right == null || m_Right.rt != right) { RTHandles.Release(m_Right); m_Right = RTHandles.Alloc(right); }
        }

        public void Release() { RTHandles.Release(m_Left); RTHandles.Release(m_Right); m_Left = m_Right = null; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle left = renderGraph.ImportTexture(m_Left);
            TextureHandle right = renderGraph.ImportTexture(m_Right);
            TextureHandle screen = resources.backBufferColor;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Atlas Lens Blit", out var data))
            {
                data.material = m_Material;
                data.left = left;
                data.right = right;
                data.screen = screen;
                builder.UseTexture(left, AccessFlags.Read);
                builder.UseTexture(right, AccessFlags.Read);
                builder.SetRenderAttachment(screen, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(s_EyeL, d.left);
                    d.material.SetTexture(s_EyeR, d.right);
                    // Eye textures are imported with UV origin BottomLeft; the back buffer reports its own origin.
                    bool flip = ctx.GetTextureUVOrigin(in d.left) != ctx.GetTextureUVOrigin(in d.screen);
                    Vector4 scaleBias = flip ? new Vector4(1f, -1f, 0f, 1f) : new Vector4(1f, 1f, 0f, 0f);
                    Blitter.BlitTexture(ctx.cmd, scaleBias, d.material, 0);
                });
            }
            resources.SwitchActiveTexturesToBackbuffer();
        }
    }
}
```

Shader for Option R (copies `Blit.hlsl` usage; **not compiled**). `Vert` and `Varyings` come from `Blit.hlsl` (`CORE/Utilities/Blit.hlsl:40`), which applies `_BlitScaleBias` to the texcoord (`CORE-SL/DynamicScaling.hlsl:4-14`); `sampler_LinearClamp` is declared in `CORE-SL/GlobalSamplers.hlsl:8`.

```hlsl
Shader "Atlas/LensBlit"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "LensBlit"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_EyeL);
            TEXTURE2D(_EyeR);

            float4 Frag(Varyings i) : SV_Target0
            {
                float2 uv = i.texcoord;                       // V already corrected by _BlitScaleBias
                float2 e = float2(frac(uv.x * 2.0), uv.y);    // left half = left eye
                return uv.x < 0.5 ? SAMPLE_TEXTURE2D_LOD(_EyeL, sampler_LinearClamp, e, 0)
                                  : SAMPLE_TEXTURE2D_LOD(_EyeR, sampler_LinearClamp, e, 0);
            }
            ENDHLSL
        }
    }
}
```

### 7.2 Option M: third camera drawing a full-screen quad with an unlit shader

The lens camera draws one mesh with a normal material through URP's opaque pass. No render-graph code and no second renderer: the lens camera can use the eye renderer, whose Render Objects features draw nothing there because the lens camera's `cullingMask` excludes their layers (with `IntermediateTextureMode.Auto` the features do not force an intermediate texture, §4). The quad's vertex shader writes clip space directly, so the mesh needs huge `bounds` to survive culling. The material samples `_EyeL`, `_EyeR` that were set once on the material (property ids from `Shader.PropertyToID`).

```csharp
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    /// <summary>Option M: a full-screen quad drawn by the lens camera. Built once at boot.</summary>
    public static class LensQuad
    {
        public static GameObject Create(Material material, int layer, RenderTexture left, RenderTexture right)
        {
            var mesh = new Mesh { name = "AtlasLensQuad" };
            mesh.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
            mesh.SetIndices(new[] { 0, 2, 1, 0, 3, 2 }, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // vertex shader ignores the camera, so never cull

            material.SetTexture(Shader.PropertyToID("_EyeL"), left);
            material.SetTexture(Shader.PropertyToID("_EyeR"), right);

            var go = new GameObject("AtlasLensQuad") { layer = layer };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            return go;
        }
    }
}
```

Shader for Option M (pattern from `URP-SL/Shaders/Unlit.shader`; **not compiled**). The pass has no `LightMode` tag, so URP draws it as `SRPDefaultUnlit` (`URP/Passes/DrawObjectsPass.cs:91`). The subshader tag `"RenderPipeline" = "UniversalPipeline"` matches `UniversalRenderPipeline.k_ShaderTagName` (`URP/UniversalRenderPipeline.cs:26`).

```hlsl
Shader "Atlas/LensQuad"
{
    Properties
    {
        [NoScaleOffset] _EyeL("Left eye", 2D) = "black" {}
        [NoScaleOffset] _EyeR("Right eye", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Lens"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_EyeL); SAMPLER(sampler_EyeL);
            TEXTURE2D(_EyeR); SAMPLER(sampler_EyeR);
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = float4(i.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.uv = i.positionOS.xy * 0.5 + 0.5;       // Unity texture V = 0 at the bottom for render textures
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 e = float2(frac(i.uv.x * 2.0), i.uv.y);
                return i.uv.x < 0.5 ? SAMPLE_TEXTURE2D_LOD(_EyeL, sampler_EyeL, e, 0)
                                    : SAMPLE_TEXTURE2D_LOD(_EyeR, sampler_EyeR, e, 0);
            }
            ENDHLSL
        }
    }
}
```
`UNITY_NEAR_CLIP_VALUE` is used by the shipped `GetFullScreenTriangleVertexPosition` (`CORE-SL/Common.hlsl:1613`). The orientation of the clip-space bypass on Metal is derived from the source, not observed: put a test grid through it on the first device run.

### 7.3 Choice

| | Option R (render-graph pass) | Option M (quad) |
|---|---|---|
| Code | one feature, one pass, a second renderer asset | one mesh, one material, no renderer change |
| Passes in the lens camera's graph | the lens pass only; no depth attachment | URP's opaque draw pass with colour and depth attachments, plus the clear |
| Screen write | `backBufferColor`, `WriteAll`; the camera's own clear is on the same attachment | same attachment, `ZTest Always ZWrite Off` |
| Risk | writing `backBufferColor` while URP also adds its final blit if an intermediate texture exists (avoided by `SwitchActiveTexturesToBackbuffer`) | clip-space bypass and huge bounds; `Renderer` settings (probes off) so UnityPerDraw stays small |
| Both | need `Camera.allowMSAA = false` and `Screen.SetMSAASamples(1)` (§6.3) so the screen is not multisampled | |

## 8. Render Objects feature, stencil, features at run time

`UnityEngine.Rendering.Universal.RenderObjects : ScriptableRendererFeature` (`URP/RendererFeatures/RenderObjects.cs:29`), assembly `Unity.RenderPipelines.Universal.Runtime`. Public field `RenderObjects.RenderObjectsSettings settings`.

| Member | Type and default | Confirmed at |
|---|---|---|
| `settings.passTag` | `string` = `"RenderObjectsFeature"` | `RenderObjects.cs:40` |
| `settings.Event` | `RenderPassEvent` = `AfterRenderingOpaques`; `Create()` clamps anything below `BeforeRenderingPrePasses` up to it | `:202` |
| `settings.filterSettings` | `RenderObjects.FilterSettings`: `RenderQueueType RenderQueueType` (`Opaque`, `Transparent`; default Opaque), `LayerMask LayerMask` (default 0, so nothing), `string[] PassNames` | `:128-150` |
| `settings.overrideMode` | `RenderObjectsSettings.OverrideMaterialMode` (`None`, `Material`, `Shader`); **default `Material`** | `:75-96` |
| `settings.overrideMaterial`, `overrideMaterialPassIndex`, `overrideShader`, `overrideShaderPassIndex` | `Material`, `int`, `Shader`, `int` | |
| `settings.overrideDepthState`, `depthCompareFunction` (`LessEqual`), `enableWrite` (true) | applied by `SetDepthState` only when `overrideDepthState` | `:101-111`, `:225-226` |
| `settings.stencilSettings` | `StencilStateData`: `bool overrideStencilState`, `int stencilReference`, `CompareFunction stencilCompareFunction` (`Always`), `StencilOp passOperation` (`Keep`), `failOperation`, `zFailOperation` | `URP/Data/RenderStateData.cs:7-38` |
| `settings.cameraSettings` | `overrideCamera`, `restoreCamera`, `Vector4 offset`, `float cameraFieldOfView` | `RenderObjects.cs:161-176` |

Behaviour:

- `Create()` copies the settings into a `RenderObjectsPass`. After changing `settings` at run time call `feature.Create()` (public) so the pass is rebuilt.
- With empty `PassNames` the pass draws shader tags `SRPDefaultUnlit`, `UniversalForward`, `UniversalForwardOnly` (`URP/Passes/RenderObjectsPass.cs:131-133`).
- When `overrideStencilState` is true the pass calls `SetStencilState(int reference, CompareFunction, StencilOp passOp, StencilOp failOp, StencilOp zFailOp)`: it builds `StencilState.defaultValue`, sets `enabled = true` and the four values, and sets `RenderStateBlock.mask |= RenderStateMask.Stencil`. XML for `RenderStateMask.Stencil`: "the stencil state and reference value is overridden". The shader's own `Stencil` block is therefore ignored for every draw of that pass. The feature exposes no read or write mask.
- The pass draws **in addition to** the renderer's normal opaque pass. Layers owned by a Render Objects feature must be removed from `UniversalRendererData.opaqueLayerMask` (and `transparentLayerMask`) or they are drawn twice.
- `AddRenderPasses` returns for Preview cameras and offscreen-depth cameras (`RenderObjects.cs:237`). The pass has a real `RecordRenderGraph` (it sets the same colour and depth attachments as the opaque pass, so it merges, §6.4). Its `Execute` exists only under `URP_COMPATIBILITY_MODE`.
- Stencil bits URP reserves in forward rendering: `UniversalRendererStencilRef.CrossFadeStencilRef_0 = 1 << 2` and `_1 = 1 << 3` (values 4 and 8), used only when `LODCrossFadeDitheringType.Stencil` is selected (`URP/UniversalRenderer.cs:42-50`, `URP/Data/UniversalRenderPipelineAsset.cs:382-393`). The asset has `m_LODCrossFadeDitheringType: 1` (`BlueNoise`), so references 1 to 15 are free. Keep it that way.
- Values: `CompareFunction` Disabled=0, Never=1, Less=2, Equal=3, LessEqual=4, Greater=5, NotEqual=6, GreaterEqual=7, Always=8. `StencilOp` Keep=0, Zero=1, Replace=2, IncrementSaturate=3, DecrementSaturate=4, Invert=5, IncrementWrap=6, DecrementWrap=7 (reflection dump; matches `stencilCompareFunction: 8`, `passOperation: 2` in the renderer asset).

Adding a feature to a `UniversalRendererData` from an Editor script: see `RendererSetup.AddPortalFeature` in §3 (compiled). It repeats what the inspector does (`Editor/ScriptableRendererDataEditor.cs:258-292`): `CreateInstance`, `AssetDatabase.AddObjectToAsset(feature, rendererData)`, then append to the serialized lists `m_RendererFeatures` and `m_RendererFeatureMap` (the map holds the sub-asset's local file id; `m_RendererFeatureMap` is `internal`, so a script outside the assembly uses `SerializedObject`) and save. `ScriptableRendererData.rendererFeatures` is public, but adding to it directly leaves the map one short; `ValidateRendererFeatures()` repairs the map on the next `OnValidate` that finds a null entry (`URP/ScriptableRendererData.cs:74-82`, `:254-269`).

Features at run time:

| Action | Effect | Confirmed at |
|---|---|---|
| `ScriptableRendererData.rendererFeatures.Add/Remove` | the list is public and mutable, but a built `ScriptableRenderer` copied it in its constructor (and called `Create()` on each) | `URP/ScriptableRenderer.cs:752-761` |
| `ScriptableRendererData.SetDirty()` after editing the list | marks the data invalid; the next `UniversalRenderPipelineAsset.GetRenderer(index)` destroys and re-creates the renderer. Do it between frames, never per frame. | `URP/ScriptableRendererData.cs:60`, `URP/Data/UniversalRenderPipelineAsset.cs:945-948` |
| `ScriptableRendererFeature.SetActive(bool)` | cheap, per frame: `AddRenderPasses` is skipped while inactive | `URP/ScriptableRenderer.cs:1664-1668` |
| change `settings` of a Render Objects feature | takes effect after `feature.Create()` | `RenderObjects.cs:190-230` |
| `UniversalRendererData` property setters (`intermediateTextureMode`, `renderingMode`, ...) | call `SetDirty()` themselves | `URP/UniversalRendererData.cs:200-330` |

## 9. Hand-written URP shaders: SRP Batcher, includes, passes, keywords, stencil

| Rule | Evidence |
|---|---|
| Include `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl`. It pulls in `Common.hlsl`, `Input.hlsl` (which includes `UnityInput.hlsl`) and `ShaderVariablesFunctions.hlsl` (`GetVertexPositionInputs(float3 positionOS)`, `ShaderVariablesFunctions.hlsl:8`). `TransformObjectToHClip(float3)` is in the Core RP `SpaceTransforms.hlsl:108`. | `URP-SL/Core.hlsl:24-28`, `:274` |
| `UnityPerDraw` is declared in `UnityInput.hlsl` ("Block Layout should be respected due to SRP Batcher") and **only when `DOTS_INSTANCING_ON` is not defined**. Never redeclare it. | `URP-SL/UnityInput.hlsl:107-175` |
| Put **every** non-texture material property that the HLSL reads in one `CBUFFER_START(UnityPerMaterial) ... CBUFFER_END`, identical in every pass of the shader; textures and samplers stay outside (`TEXTURE2D`, `SAMPLER`). A texture's `_ST` goes inside the block. Properties used only by ShaderLab state (`_SrcBlend`, `_Cull`, stencil values) are declared in `Properties` and are not in the block. | `URP-SL/Shaders/UnlitInput.hlsl:7-14`, `Unlit.shader:3-30` |
| `GraphicsSettings.useScriptableRenderPipelineBatching = asset.useSRPBatcher` is applied when the pipeline is created. | `URP/UniversalRenderPipeline.cs:255` |
| The core docs' simple unlit example (no `UnityPerMaterial` block, hard-coded colour) is labelled "It is not compatible with SRP Batcher"; the URP shaders use the `UnityPerMaterial` block above. The Shader inspector's SRP Batcher line is the compatibility check (Editor only). | `com.unity.render-pipelines.core@3f80e92e91c2/Documentation~/srp-creating-simple-render-loop.md:37` |
| `LightMode` tags URP draws: forward opaque and transparent use `SRPDefaultUnlit` (a pass **without** a `LightMode` tag), `UniversalForward`, `UniversalForwardOnly`. `LightweightForward` appears only in the deferred forward-only list. Other tags (`DepthOnly`, `DepthNormals`/`DepthNormalsOnly`, `ShadowCaster`, `UniversalGBuffer`, `MotionVectors`, `XRMotionVectors`) are drawn by their own passes. | `URP/Passes/DrawObjectsPass.cs:91`, `URP/UniversalRenderer.cs:63`, `:395-397`, `URP/Passes/DepthOnlyPass.cs:26`, `URP/Passes/DepthNormalOnlyPass.cs:19` |
| Subshader tag `"RenderPipeline" = "UniversalPipeline"`. | `URP/UniversalRenderPipeline.cs:26` |
| `DepthOnly` and `DepthNormals(Only)` passes are drawn only when a prepass is required: depth texture on and the copy not possible, `CopyDepthMode.ForcePrepass`, a pass that calls `ConfigureInput(Depth)` (`requiresDepthPrepass`) or `ConfigureInput(Normal)`, a debug view, or depth priming. With the §3 and §4 settings and no such feature, URP draws no prepass, no shadow pass and no GBuffer pass, and a shader needs only its forward pass. | `URP/UniversalRendererRenderGraph.cs:1603-1611` (`RequirePrepassForTextures`), `:1614-1622` (`RequireDepthTexture`), `:1631` (depth priming) |
| `Unlit.shader` is the model: main pass without `LightMode`, then `GBuffer`, `DepthOnly`, `DepthNormalsOnly`, `Meta`, `XRMotionVectors`, each with its own keyword set. | `URP-SL/Shaders/Unlit.shader` |

Keywords an unlit custom shader can drop (each is a `multi_compile` that multiplies variants):

| Pragma in URP `Unlit` | Where it comes from | Drop when |
|---|---|---|
| `#pragma multi_compile_fog` and `#pragma dynamic_branch _ FOG_LINEAR FOG_EXP FOG_EXP2` | `#include_with_pragmas ".../ShaderLibrary/Fog.hlsl"` (`Fog.hlsl:6-8`) | fog is computed in the shader (plan: shader fog) |
| `#pragma multi_compile_instancing` | explicit in the pass | no GPU instancing wanted. URP forces `enableInstancing = true` in `DrawingSettings` (`URP/RenderingUtils.cs:1064`), a shader without the pragma is simply not instanced |
| `#pragma multi_compile _ DOTS_INSTANCING_ON` | `#include_with_pragmas ".../ShaderLibrary/DOTS.hlsl"` (`DOTS.hlsl:5`) | no GPU Resident Drawer / DOTS (plan: off) |
| `_SCREEN_SPACE_OCCLUSION`, `_DBUFFER_MRT1..3`, `DEBUG_DISPLAY`, `LOD_FADE_CROSSFADE`, `_WRITE_RENDERING_LAYERS` (via `RenderingLayers.hlsl:4`) | explicit pragmas | no SSAO, decals, debug views, LOD cross-fade, rendering layers |
| `_SURFACE_TYPE_TRANSPARENT`, `_ALPHATEST_ON`, `_ALPHAMODULATE_ON` | `shader_feature_local_fragment` (stripped when no material uses them) | opaque only |

With none of these, the shader has one variant per pass. Keyword limits: the XML docs and package docs installed here state no numeric limit for global or local keywords (`GlobalKeyword`, `LocalKeyword`, `LocalKeywordSpace` only). Do not rely on a number; keep the count at zero.

Stencil block (ShaderLab) with material-driven values, as the project's own `TMP_SDF-Mobile.shader:64-71` and URP's `Shaders/Utils/StencilDeferred.shader:49-51` write it:

```hlsl
Properties { _StencilRef("Stencil Ref", Float) = 0  _StencilComp("Stencil Comp", Float) = 8  _StencilPass("Stencil Pass", Float) = 0 }
...
Pass {
    Stencil { Ref [_StencilRef]  Comp [_StencilComp]  Pass [_StencilPass]  ReadMask 255  WriteMask 255 }
```
Numbers are `CompareFunction` and `StencilOp` values (§8). A Render Objects stencil override replaces this block, and `UniversalRendererData.defaultStencilState.overrideStencilState = true` replaces it for every draw (§4).

Per-object data URP asks the engine for: `PerObjectData.Lightmaps | LightProbe | OcclusionProbe | ShadowMask`, plus `ReflectionProbes | LightData` in non-Forward+ (`URP/UniversalRenderPipeline.cs:2096-2104`). A renderer that needs none of it sets `lightProbeUsage = Off` and `reflectionProbeUsage = Off` (both used in the compiled `LensQuad` snippet). `DrawingSettings.enableDynamicBatching` follows `supportsDynamicBatching` (`URP/RenderingUtils.cs:1061`).

`Shader.Find(string)` (XML: "Returns null if the shader is not found") finds a shader only if the player build includes it; the XML does not say how. Reference the shader from an asset, or load the material from `Resources`, instead of relying on `Shader.Find` at run time.

## 10. Shader and material C# API

| API | Signature | Notes |
|---|---|---|
| `Shader.PropertyToID` | `static int PropertyToID(string name)` | cache in `static readonly int` |
| `Shader.SetGlobalVector` / `SetGlobalMatrix` / `SetGlobalFloat` / `SetGlobalTexture` | `(int nameID, Vector4 / Matrix4x4 / float / Texture)` plus `string` overloads; arrays: `SetGlobalVectorArray(int, Vector4[])`, `SetGlobalMatrixArray(int, Matrix4x4[])` (also `List<>`) | `SetGlobalInt` is deprecated, use `SetGlobalInteger` or `SetGlobalFloat` (XML). Globals are visible to every shader, including those using SRP Batcher. |
| in a raster pass | `RasterCommandBuffer.SetGlobalFloat/Vector/Matrix/Color(int nameID, ...)`, `SetGlobalVectorArray(int, Vector4[])`, `EnableKeyword(ref GlobalKeyword)`; a pass that sets global state must call `builder.AllowGlobalStateModification(true)` (as `RenderObjectsPass` does at `:326`) | reflection dump of `RasterCommandBuffer` |
| `Material` constructors | `Material(Shader)`, `Material(Material source)` ("Create a temporary Material", XML: copies all properties from another material) | the XML states no cost; the per-frame rule is to create materials once at boot (contract F3, shared pool) |
| `Renderer.material` / `sharedMaterial` | `material`: "Returns the first instantiated Material assigned to the renderer" (XML) | `material` instantiates; use `sharedMaterial` with the pool |
| `Material.SetFloat(int, float)`, `SetTexture(int, Texture)` | | `SetTexture` has an overload `(int, RenderTexture, RenderTextureSubElement)` |
| `MaterialPropertyBlock` | | banned by contract F7; the SRP Batcher part of that rule is not documented in the installed sources |

## 11. `GraphicsFormat` for iOS Metal eye textures

| Fact | Confirmed at |
|---|---|
| URP's own LDR camera colour is `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)` (HDR off), depth-stencil `SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil)` | `URP/UniversalRenderPipelineCore.cs:1560-1601` |
| Linear project: `DefaultFormat.LDR` is an sRGB format (typically `R8G8B8A8_SRGB`); an sRGB colour target gives hardware linear-to-sRGB on write and sRGB-to-linear on sample | XML `DefaultFormat.LDR` |
| URP's HDR choices when HDR is on: `B10G11R11_UFloatPack32` (32-bit) or `R16G16B16A16_SFloat` (64-bit), selected with `SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Blend)`. Not needed with HDR off. | `UniversalRenderPipelineCore.cs:1561-1573` |
| `UniversalRenderPipeline.MakeUnormRenderTextureGraphicsFormat` prefers `A2B10G10R10_UNormPack32` over `R8G8B8A8_UNorm` (internal helper) | `UniversalRenderPipelineCore.cs:1578-1586` |
| Capability queries: `SystemInfo.IsFormatSupported(GraphicsFormat, GraphicsFormatUsage)`, `SystemInfo.GetCompatibleFormat(GraphicsFormat, GraphicsFormatUsage)`, `SystemInfo.GetRenderTextureSupportedMSAASampleCount(RenderTextureDescriptor)`. `GraphicsFormatUsage` values: `Sample`=1, `Linear`=2, `Render`=16, `Blend`=32, `LoadStore`=1024, `MSAA2x`=2048, `MSAA4x`=4096, `MSAA8x`=8192. | reflection dump, XML |
| Display transfer: `UniversalCameraData.requireSrgbConversion` is `targetTexture == null && Display.main.requiresSrgbBlitToBackbuffer` and URP's blit then toggles the `LinearToSRGBConversion` keyword. A custom lens pass that writes the screen must do that conversion itself when `Display.main.requiresSrgbBlitToBackbuffer` is true (XML: "True when doing a blit to the back buffer requires manual color space conversion"). | `URP/FrameData/UniversalCameraData.cs:287-299`, `URP/Passes/FinalBlitPass.cs:339` |

The recommended eye format is therefore `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)` (guarded with `IsFormatSupported(format, GraphicsFormatUsage.Render | GraphicsFormatUsage.MSAA4x)`), 8 bits per channel. Which sRGB format Metal returns, and whether the back buffer needs the manual conversion, cannot be read from the sources: log `Display.main.requiresSrgbBlitToBackbuffer` and the chosen formats once on the phone.

## 12. Cameras, projection, screen, frame rate

### 12.1 Stereo fields not to use with our own rig

All of these exist for an XR display subsystem. `XRSystem.displayActive` is `s_Display != null && s_Display.running` (`CORE/XR/XRSystem.cs:67`), and the ARKit loader manages session, camera, plane, anchor, raycast, occlusion, input and other AR subsystems but no `XRDisplaySubsystem` (`com.unity.xr.arkit@f7ae96e6506f/Runtime/ARKitLoader.cs:13-28`), so URP's XR path is off. Setting them is a no-op here.

| Do not use | Why |
|---|---|
| `Camera.stereoTargetEye` (`StereoTargetEyeMask`: None=0, Left=1, Right=2, Both=3, default Both), `Camera.stereoEnabled`, `stereoSeparation`, `stereoConvergence`, `stereoActiveEye`, `areVRStereoViewMatricesWithinSingleCullTolerance` | XML: "Defines which eye of a VR display the Camera renders into" |
| `Camera.GetStereoViewMatrix/SetStereoViewMatrix/ResetStereoViewMatrices`, `Get/SetStereoProjectionMatrix`, `ResetStereoProjectionMatrices`, `GetStereoNonJitteredProjectionMatrix`, `CopyStereoDeviceProjectionMatrixToNonJittered`, the `MonoOrStereoscopicEye` overloads of `WorldToScreenPoint`, `ScreenToWorldPoint`, `CalculateFrustumCorners`, `RenderToCubemap(..., stereoEye)` | stereo matrices belong to the XR pass |
| `Camera.SetStereoViewMatrices`, `SetStereoProjectionMatrices`, `GetStereoViewMatrices`, `GetStereoProjectionMatrices` | `[Obsolete]` (warning); `stereoMirrorMode` is `[Obsolete(error)]` |
| `UnityEngine.XR.XRSettings`, `XRDevice` | VR module, not the AR session |
| `UniversalAdditionalCameraData.allowXRRendering` | leave `false` on all three cameras |

### 12.2 `Camera.projectionMatrix` and off-axis helpers

- URP reads `camera.projectionMatrix` and `camera.worldToCameraMatrix` each frame (`URP/UniversalRenderPipeline.cs:1656`, `:1696`) and adjusts for the platform and render target itself (`GL.GetGPUProjectionMatrix(proj, renderIntoTexture)`, XML: "Adjusted projection matrix for the current graphics API"). Assign the API-independent matrix, never a GPU matrix.
- `static Matrix4x4 Matrix4x4.Frustum(float left, float right, float bottom, float top, float zNear, float zFar)` and `Frustum(FrustumPlanes)`: the near-plane rectangle in view space (XML). `Matrix4x4.Perspective(float fov, float aspect, float zNear, float zFar)` (vertical fov in degrees), `Matrix4x4.Ortho(l, r, b, t, zNear, zFar)`. Compiled helper: `EyeRigExample.OffAxis` in §6.5.
- Physical camera path: `Camera.usePhysicalProperties`, `focalLength`, `sensorSize` (`Vector2`), `lensShift` (`Vector2`), `gateFit`, `static CalculateProjectionMatrixFromPhysicalProperties(out Matrix4x4 output, float focalLength, Vector2 sensorSize, Vector2 lensShift, float nearClip, float farClip, GateFitParameters gateFitParameters = null)`, `GetGateFittedFieldOfView()`, `GetGateFittedLensShift()`.
- `Camera.ResetProjectionMatrix()` (XML: "Make the projection reflect normal camera's parameters"), `Camera.CalculateObliqueMatrix(Vector4 clipPlane)`, `Camera.cullingMatrix` (XML: "custom matrix for the camera to use for all culling queries"), `Camera.ResetCullingMatrix()`.
- Frustum tests for the portal swap: there is no `Camera.GetFrustumPlanes`. Use `static void GeometryUtility.CalculateFrustumPlanes(Camera camera, Plane[] planes)` (fills 6 planes, no allocation) or `CalculateFrustumPlanes(Matrix4x4 worldToProjectionMatrix, Plane[] planes)` with `projectionMatrix * worldToCameraMatrix` of the eye, then `static bool GeometryUtility.TestPlanesAABB(Plane[] planes, Bounds bounds)` (also `ref Bounds` and `ReadOnlySpan<Plane>` overloads). All `UnityEngine.CoreModule`.

### 12.3 `Screen` on an iPhone in landscape

| Member | XML text | Project fact |
|---|---|---|
| `Screen.width`, `Screen.height` (`int`) | "The current width / height of the screen window in pixels" | `defaultScreenOrientation: 3` (`UIOrientation.LandscapeLeft`; `UIOrientation`: Portrait=0, PortraitUpsideDown=1, LandscapeRight=2, LandscapeLeft=3, AutoRotation=4) in `PS/ProjectSettings.asset:11`; `resolutionScalingMode: 0`, `targetPixelDensity: 30` |
| `Screen.safeArea` (`Rect`) | "the safe area of the screen in pixels" | |
| `Screen.cutouts` (`Rect[]`), `Screen.dpi` (`float`), `Screen.currentResolution` (`Resolution`) | | |
| `Screen.orientation` (`ScreenOrientation`: Portrait=1, PortraitUpsideDown=2, LandscapeLeft=3 (= `Landscape`), LandscapeRight=4, AutoRotation=5) | "Specifies logical orientation of the screen" | AR Foundation passes `Screen.orientation` into `XRSessionUpdateParams` every `ARSession.Update` (`ARSession.cs:400`) |
| `Screen.SetResolution(int, int, FullScreenMode)` and variants | | |

Whether `Screen.width` is the long side right at start-up, and whether the safe area is already landscape in the first frame, are not stated in the XML; read both in the first frame on the phone and log them. Eye texture size must not be derived from `Screen.width` before that check. `defaultScreenOrientation` here is `LandscapeLeft`; the `allowedAutorotateTo*` flags are all 1 (`PS/ProjectSettings.asset:62-65`) and only matter when the default is `AutoRotation`.

### 12.4 `Application.targetFrameRate` and `QualitySettings.vSyncCount`

| Fact | Confirmed at |
|---|---|
| `Application.targetFrameRate` XML: "Specifies the target frame rate at which Unity tries to render your application." `QualitySettings.vSyncCount`: "the number of vertical syncs that should pass between each frame." The XML says nothing about iOS. | XML |
| `ARSession.matchFrameRateRequested` defaults to `true` (`m_MatchFrameRate = true`). While the session runs, **every `ARSession.Update` sets `Application.targetFrameRate = subsystem.frameRate` and `QualitySettings.vSyncCount = 0`** when `subsystem.matchFrameRateEnabled`. A boot-time `targetFrameRate = 60` is overwritten each frame, and the request blocks the update until the next AR frame. | `com.unity.xr.arfoundation@13d2457b468b/Runtime/ARFoundation/Session/ARSession.cs:59`, `:76-91`, `:391-395` |
| The project: all three quality levels have `vSyncCount: 1`; `adjustIOSFPSUsingThermalState: 0`, `thermalStateSeriousIOSFPS: 30`, `thermalStateCriticalIOSFPS: 15`, `appleEnableProMotion: 0` are set. | `PS/QualitySettings.asset`, `PS/ProjectSettings.asset:69-71`, `:262` |
| `OnDemandRendering.renderFrameInterval` XML: "Get or set the current frame rate interval. To restore rendering back to the value of Application.targetFrameRate or QualitySettings.vSyncCount set this to 0 or 1." | XML |
| `Application.isMobilePlatform` (bool) is what URP uses with `SystemInfo.supportsMultisampleAutoResolve` | `URP/UniversalRenderer.cs:1995` |

## 13. Profiling

### 13.1 `FrameTimingManager`

`UnityEngine.FrameTimingManager` (static class, `UnityEngine.CoreModule`):

| Member | Signature | XML |
|---|---|---|
| enabled? | `static bool IsFeatureEnabled()` | "Check if frame timing statistics are enabled." |
| capture | `static void CaptureFrameTimings()` | "triggers the FrameTimingManager to capture a snapshot of FrameTiming's data" |
| read | `static uint GetLatestTimings(uint numFrames, FrameTiming[] timings)` | returns the number actually filled, "always equal to or less than the requested numFrames"; the array must be large enough |
| other | `static float GetVSyncsPerSecond()`, `static ulong GetGpuTimerFrequency()` ("Currently always 1000000000"), `static ulong GetCpuTimerFrequency()` (0 if unsupported) | |

`UnityEngine.FrameTiming` (struct) fields, all in ms unless noted: `double cpuFrameTime` ("total CPU frame time calculated as the time between ends of two frames, which includes all waiting time and overheads"), `double cpuMainThreadFrameTime` ("start of the frame and when the main thread finished the job"), `double cpuMainThreadPresentWaitTime`, `double cpuRenderThreadFrameTime` ("between start of the work on the render thread and when Present was called"), `double gpuFrameTime`; timestamps `ulong frameStartTimestamp`, `firstSubmitTimestamp`, `cpuTimePresentCalled`, `cpuTimeFrameComplete`; `float heightScale`, `widthScale`; `uint syncInterval`.

Enabling: `UnityEditor.PlayerSettings.enableFrameTimingStats` ("Enable frame timing statistics"); the project already has `enableFrameTimingStats: 1` (`PS/ProjectSettings.asset:160`). Gate on `IsFeatureEnabled()`. The XML gives no latency in frames and no GPU-support statement for Metal: treat `gpuFrameTime` as possibly 0 and check on the phone. Project fact: `mobileMTRendering` is `iPhone: 0` (`PS/ProjectSettings.asset:560-563`, set with `PlayerSettings.SetMobileMTRendering(NamedBuildTarget, bool)`); what that does to `cpuRenderThreadFrameTime` is not stated in the sources, so read it on the phone before using that field as a budget input.

### 13.2 `ProfilerRecorder` and `ProfilerMarker`

`Unity.Profiling.ProfilerRecorder` (struct, `UnityEngine.CoreModule`):
`static ProfilerRecorder StartNew(ProfilerCategory category, string statName, int capacity = 1, ProfilerRecorderOptions options = Default)`; `Valid`, `CurrentValue` (`long`), `LastValue`, `Count`, `Capacity`, `GetSample(int)` (`ProfilerRecorderSample.Value`), `CopyTo(...)`, `Dispose()`. `ProfilerRecorderOptions`: `StartImmediately`=1, `KeepAliveDuringDomainReload`=2, `CollectOnlyOnCurrentThread`=4, `WrapAroundWhenCapacityReached`=8, `SumAllSamplesInFrame`=16, `Default`=24, `GpuRecorder`=64.
`ProfilerCategory.Render`, `.Memory`, `.Scripts`, `.Internal` are static properties. `ProfilerRecorderHandle.GetAvailable(List<ProfilerRecorderHandle>)` lists every stat the player can record. Package docs: "Not all Profiler counters are available in the Release Players. Use `ProfilerRecorder.Valid`" (`com.unity.profiling.core@8a49f7027d06/Documentation~/profilercounter-guide.md:170`).

| Stat (category, name) | Confirmed by | |
|---|---|---|
| `ProfilerCategory.Render`, `"Draw Calls Count"` | `com.coplaydev.unity-mcp@045e809812a8/Editor/Tools/Graphics/RenderingStatsOps.cs:19` (StartNew with the Render category) | confirmed |
| `ProfilerCategory.Render`, `"SetPass Calls Count"` | same file, line 21 | confirmed |
| `ProfilerCategory.Render`, `"Triangles Count"` | same file, line 22 | confirmed |
| `ProfilerCategory.Memory`, `"System Used Memory"` | `profilercounter-guide.md:139` | confirmed |
| `ProfilerCategory.Memory`, `"GC Allocated In Frame"` | **not found in any installed source**; only `"GC Reserved Memory"` (Memory) is confirmed (`profilercounter-guide.md:140`) | enumerate with `ProfilerRecorderHandle.GetAvailable` in a development build before relying on it |

`Unity.Profiling.ProfilerMarker` (struct, `UnityEngine.CoreModule`): constructors `(string name)`, `(ProfilerCategory category, string name)`, with `MarkerFlags` overloads; `Begin()`, `End()`, `Auto()` (returns `ProfilerMarker.AutoScope`, use with `using`). `ProfilerCounterValue<T>` and `ProfilerCounter<T>` live in `Unity.Profiling.Core`. `RasterCommandBuffer.BeginSample(ProfilerMarker)` exists. Compiled example:

```csharp
using System;
using Unity.Profiling;
using UnityEngine;

namespace Atlas.Telemetry
{
    public sealed class PerfProbeExample : MonoBehaviour
    {
        FrameTiming[] m_Timings = new FrameTiming[1];
        ProfilerRecorder m_Draw, m_SetPass, m_Tris, m_SysMem;
        static readonly ProfilerMarker s_Marker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Frame");

        void OnEnable()
        {
            m_Draw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            m_SetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            m_Tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            m_SysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        }

        void OnDisable() { m_Draw.Dispose(); m_SetPass.Dispose(); m_Tris.Dispose(); m_SysMem.Dispose(); }

        void Update()
        {
            using (s_Marker.Auto())
            {
                FrameTimingManager.CaptureFrameTimings();
                uint n = FrameTimingManager.GetLatestTimings(1, m_Timings);
                if (n == 1)
                {
                    double cpu = m_Timings[0].cpuFrameTime;
                    double main = m_Timings[0].cpuMainThreadFrameTime;
                    double render = m_Timings[0].cpuRenderThreadFrameTime;
                    double gpu = m_Timings[0].gpuFrameTime;
                    if (cpu < 0 || main < 0 || render < 0 || gpu < 0) { }
                }
                long draws = m_Draw.Valid ? m_Draw.LastValue : 0;
            }
        }
    }
}
```

## 14. `Mesh` without garbage, and a Burst job

All in namespace `UnityEngine` / `UnityEngine.Rendering`, assembly `UnityEngine.CoreModule` (Burst and collections as in §1). Signatures from the reflection dump of the installed assembly; the XML gives one-line descriptions only, so layout rules such as stride and attribute order are not stated here and are validated by the engine at run time.

| API | Signature |
|---|---|
| layout | `void Mesh.SetVertexBufferParams(int vertexCount, params VertexAttributeDescriptor[] attributes)`; `(int, NativeArray<VertexAttributeDescriptor>)` |
| descriptor | `VertexAttributeDescriptor(VertexAttribute attribute = Position, VertexAttributeFormat format = Float32, int dimension = 3, int stream = 0)` (dimension 1 to 4) |
| `VertexAttribute` | Position=0, Normal=1, Tangent=2, Color=3, TexCoord0=4 ... TexCoord7=11, BlendWeight=12, BlendIndices=13 |
| `VertexAttributeFormat` | Float32=0, Float16=1, UNorm8=2, SNorm8=3, UNorm16=4, SNorm16=5, UInt8=6, SInt8=7, UInt16=8, SInt16=9, UInt32=10, SInt32=11 |
| position + normal + colour32 | `Position Float32 x3`, `Normal Float32 x3`, `Color UNorm8 x4`, stream 0; matching C# struct `{ float3 position; float3 normal; Color32 color; }` (28 bytes, `StructLayout.Sequential`) |
| data | `void SetVertexBufferData<T>(NativeArray<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = Default)` (also `T[]`, `List<T>`); `void SetIndexBufferParams(int indexCount, IndexFormat format)`; `void SetIndexBufferData<T>(NativeArray<T> data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = Default)` |
| `IndexFormat` | `UInt16`=0, `UInt32`=1 (property `Mesh.indexFormat`) |
| sub-mesh | `SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = Triangles)` with `bounds`, `topology`, `indexStart`, `indexCount`, `baseVertex`, `firstVertex`, `vertexCount`; `Mesh.subMeshCount {get;set;}`; `Mesh.SetSubMesh(int index, SubMeshDescriptor desc, MeshUpdateFlags flags = Default)`; `SetSubMeshes(...)` |
| `MeshUpdateFlags` | `Default`=0, `DontValidateIndices`=1, `DontResetBoneBounds`=2, `DontNotifyMeshUsers`=4, `DontRecalculateBounds`=8, `DontValidateLodRanges`=16 (XML: `DontRecalculateBounds` applies to `SetSubMesh`; `DontValidateIndices` to `SetIndexBufferData`) |
| bounds | `Mesh.bounds {get;set;}`, `RecalculateBounds()`, `RecalculateBounds(MeshUpdateFlags)` |
| dynamic | `void Mesh.MarkDynamic()` (XML: "Hints that the Mesh geometry data will be updated frequently, optimizing GPU buffer strategies"), `Mesh.UploadMeshData(bool markNoLongerReadable)`, `Mesh.Clear(bool keepVertexLayout)` |
| writable data | `static Mesh.MeshDataArray Mesh.AllocateWritableMeshData(int meshCount)` (also from a `Mesh`/array/list); `MeshDataArray[int]` gives `Mesh.MeshData`; `MeshData.SetVertexBufferParams`, `SetIndexBufferParams`, `GetVertexData<T>(int stream = 0)`, `GetIndexData<T>()`, `subMeshCount {get;set;}`, `SetSubMesh`; `static void Mesh.ApplyAndDisposeWritableMeshData(Mesh.MeshDataArray data, Mesh mesh, MeshUpdateFlags flags = Default)` (also `Mesh[]`, `List<Mesh>`); `MeshDataArray.Dispose()` |

Compiled example: a Burst kernel on raw pointers (contract F2), an `IJob` wrapper, and a builder that writes the vertex and index data of one mesh through `MeshData` and applies it on the main thread.

```csharp
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SurfaceVertex
    {
        public float3 position;   // VertexAttribute.Position, Float32 x3
        public float3 normal;     // VertexAttribute.Normal,   Float32 x3
        public Color32 color;     // VertexAttribute.Color,    UNorm8 x4
    }

    public static unsafe class SliceKernel
    {
        // Engine-free kernel: raw pointers and lengths only (contract F2).
        [BurstCompile]
        public static void FillQuads(SurfaceVertex* vertices, ushort* indices, int quadCount, float size)
        {
            for (int q = 0; q < quadCount; q++)
            {
                float x = q * size;
                int v = q * 4;
                vertices[v + 0] = new SurfaceVertex { position = new float3(x, 0, 0), normal = new float3(0, 1, 0), color = new Color32(255, 255, 255, 255) };
                vertices[v + 1] = new SurfaceVertex { position = new float3(x, 0, size), normal = new float3(0, 1, 0), color = new Color32(255, 255, 255, 255) };
                vertices[v + 2] = new SurfaceVertex { position = new float3(x + size, 0, size), normal = new float3(0, 1, 0), color = new Color32(255, 255, 255, 255) };
                vertices[v + 3] = new SurfaceVertex { position = new float3(x + size, 0, 0), normal = new float3(0, 1, 0), color = new Color32(255, 255, 255, 255) };
                int i = q * 6;
                indices[i + 0] = (ushort)(v + 0); indices[i + 1] = (ushort)(v + 1); indices[i + 2] = (ushort)(v + 2);
                indices[i + 3] = (ushort)(v + 0); indices[i + 4] = (ushort)(v + 2); indices[i + 5] = (ushort)(v + 3);
            }
        }
    }

    [BurstCompile]
    public unsafe struct FillJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public SurfaceVertex* vertices;
        [NativeDisableUnsafePtrRestriction] public ushort* indices;
        public int quadCount;
        public float size;
        public void Execute() => SliceKernel.FillQuads(vertices, indices, quadCount, size);
    }

    public static unsafe class MeshBuilder
    {
        static readonly VertexAttributeDescriptor[] s_Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0),
        };

        // Writable mesh data: no managed arrays, no GC. Apply on the main thread.
        public static void Build(Mesh mesh, int quadCount, float size)
        {
            Mesh.MeshDataArray array = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData data = array[0];
            data.SetVertexBufferParams(quadCount * 4, s_Layout);
            data.SetIndexBufferParams(quadCount * 6, IndexFormat.UInt16);
            NativeArray<SurfaceVertex> v = data.GetVertexData<SurfaceVertex>();
            NativeArray<ushort> ix = data.GetIndexData<ushort>();
            var job = new FillJob
            {
                vertices = (SurfaceVertex*)NativeArrayUnsafeUtility.GetUnsafePtr(v),
                indices = (ushort*)NativeArrayUnsafeUtility.GetUnsafePtr(ix),
                quadCount = quadCount,
                size = size,
            };
            job.Schedule().Complete();
            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, quadCount * 6, MeshTopology.Triangles)
            {
                bounds = new Bounds(new Vector3(quadCount * size * 0.5f, 0f, size * 0.5f), new Vector3(quadCount * size, 0.01f, size)),
                vertexCount = quadCount * 4,
            }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Mesh.ApplyAndDisposeWritableMeshData(array, mesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            mesh.bounds = new Bounds(new Vector3(quadCount * size * 0.5f, 0f, size * 0.5f), new Vector3(quadCount * size, 0.01f, size));
        }
    }
}
```

Burst notes read from the package docs (`com.unity.burst@2f3d916873ea/Documentation~`): `[BurstCompile]` on a job struct, or on a static class and its static methods (`compilation-burstcompile.md:6-8`); managed objects, reference types and `string` are not allowed (`csharp-type-support.md:3`, `:39`); `Debug.Log` with a literal is supported (`csharp-string-support.md:5`); `Span` cannot cross the managed/Burst boundary (`csharp-type-support.md:150`). `BurstCompileAttribute` properties: `FloatMode`, `FloatPrecision`, `CompileSynchronously`, `Debug`, `DisableSafetyChecks`, `OptimizeFor`. Native containers: `NativeArray<T>` and `NativeArrayUnsafeUtility.GetUnsafePtr<T>(NativeArray<T>)` / `GetUnsafeReadOnlyPtr` are in `UnityEngine.CoreModule`; `[NativeDisableUnsafePtrRestriction]`, `[ReadOnly]`, `[WriteOnly]` too. Jobs: `IJob`, `IJobParallelFor`, `JobHandle` in `UnityEngine.CoreModule`; `Schedule<T>(this T jobData, JobHandle dependsOn = default)`, `Run`, `ScheduleByRef`.

## 15. Additive scene loading

| API | Signature | Notes |
|---|---|---|
| `SceneManager.LoadSceneAsync` | `static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode)`; `(int sceneBuildIndex, LoadSceneMode mode)`; `(string, LoadSceneParameters)`; `(int, LoadSceneParameters)` | `LoadSceneMode.Single`=0, `Additive`=1. XML: Single "closes all current loaded Scenes", Additive "adds the Scene to the current loaded Scenes". `sceneName` is "Name or path". |
| `LoadSceneParameters` | `(LoadSceneMode mode)`, `(LoadSceneMode mode, LocalPhysicsMode physicsMode)` | |
| `Application.CanStreamedLevelBeLoaded` | `static bool CanStreamedLevelBeLoaded(string levelName)`, `(int levelIndex)` | XML: "Checks if the built scene can be loaded." The project's Build Settings list only `Assets/Atlas/Scenes/Atlas_Boot.unity` (`PS/EditorBuildSettings.asset`), so any `Space_*` scene reports false until it is added; the contract F3 fallback (module from `SpaceRegistry`) is the path in that case. |
| `AsyncOperation` | `allowSceneActivation {get;set;}` ("Allows a scene to be activated as soon as it's ready"), `progress` (float, 1.0 = complete), `isDone`, `priority`, `event Action<AsyncOperation> completed` | |
| other | `UnloadSceneAsync(string / int / Scene [, UnloadSceneOptions])`, `SetActiveScene(Scene)`, `GetSceneByName(string)`, `MoveGameObjectToScene`, `MergeScenes`, events `sceneLoaded(Scene, LoadSceneMode)`, `sceneUnloaded`, `activeSceneChanged` | `SceneManager.UnloadScene` is `[Obsolete]` |

Compiled helper: `EyeRigExample.LoadAdditive` in §6.5.

## 16. Windows standalone build of the same project

| Fact | Evidence |
|---|---|
| Platform defines: `UNITY_STANDALONE`, `UNITY_STANDALONE_WIN`; Editor adds `UNITY_EDITOR`, `UNITY_EDITOR_OSX` on the Mac. Version defines used by the headless harness: `UNITY_6000_3`, `UNITY_6000_3_25`, `UNITY_6000_3_OR_NEWER`, `NET_STANDARD_2_1`. Mono is the harness's Windows scripting backend, IL2CPP the iOS one. | `tools/headless/verify.py` `COMMON_DEFINES`, `CONFIGS` |
| `ATLAS_SPECTATOR` is **set only by the harness**. The project's scripting defines are `iPhone: UNITY_XR_ARKIT_LOADER_ENABLED` only, no source or asmdef under `Assets/Atlas` mentions `ATLAS_SPECTATOR` (checked when this page was written), and `Atlas.Spectator` is restricted by `includePlatforms: ["Editor", "WindowsStandalone64"]` instead. Anything that tests `#if ATLAS_SPECTATOR` is compiled out of a real Windows build unless the define is added to Player Settings (`PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget, string)`) for Standalone. | `PS/ProjectSettings.asset:842-843`; `Assets/Atlas/Spectator/Atlas.Spectator.asmdef` |
| Windows scripting backend and graphics API are not set in the project (`scriptingBackend` lists Android and iPhone only; `m_BuildTargetGraphicsAPIs` lists iOS (value `10000000`) and Android); Standalone uses the Unity defaults until set. | `PS/ProjectSettings.asset:547-552`, `:847-849` |
| Burst: `ProjectSettings/BurstAotSettings_StandaloneWindows.json` exists (`EnableBurstCompilation: true`, `EnableSafetyChecks: false`, `CpuTargetsX64: 72`); `BurstAotSettings_Android.json` exists, **no iOS file**. Burst docs: Windows builds put Burst code in `Data/Plugins/lib_burst_generated.dll`; iOS gets a static library. | `PS/`; `building-projects.md:3-6` |
| URP on Windows: `PlatformRequiresExplicitMsaaResolve()` is true (not Metal, not mobile), so each eye camera uses an intermediate colour texture and a blit; `Screen.SetMSAASamples`, `SystemInfo.supportsMemorylessTextures` and `supportsStoreAndResolveAction` decide the rest. The on-tile guarantees of §6.4 do not apply on the Station. | `URP/UniversalRenderer.cs:1985-1998` |
| `Application.targetFrameRate` and `vSyncCount` behave per §12.4; `ARSession` does not run on the Station, so nothing overrides them there. | |
| `Screen.safeArea`, `cutouts`, `orientation`: engine API, values on desktop are not stated in the XML | XML |
| Engine-free cores (contract F2) and Burst kernels compile in both configurations; `verify.py build <Assembly> --config win` compiles the Atlas assemblies with `UNITY_STANDALONE_WIN`. | `docs/contracts/README.md` F2, F9 |

## 17. Not confirmed from the sources (read on the first device or Editor run)

- Which `GraphicsFormat` Metal returns for `DefaultFormat.LDR` and `DepthStencil`; whether `Display.main.requiresSrgbBlitToBackbuffer` is true on the phone (§11).
- Whether the engine accepts a `RenderTextureMemoryless.MSAA` surface when URP stores it (visible watermark, intermediate attachments) (§6.4). Xcode capture.
- `Screen.width`, `Screen.height`, `Screen.safeArea` in the first landscape frame (§12.3).
- `FrameTimingManager` latency in frames, Metal GPU time availability, and `cpuRenderThreadFrameTime` with `mobileMTRendering` off (§13.1).
- The profiler stat name `"GC Allocated In Frame"` (§13.2).
- The cost of `new Material(Material)` and of `Renderer.material`: the XML says only "temporary Material".
- A numeric limit on global and local shader keywords.
- Whether a shader with no non-texture properties (no `UnityPerMaterial` block) shows "SRP Batcher: compatible" in the inspector.
- Orientation of the clip-space-bypass quad (Option M) on Metal.
- Editor Play-mode edits of a `UniversalRendererData` asset through `rendererFeatures.Add` mutate the loaded asset; treat that as true and work on a runtime copy if a run-time list change is ever needed.
