using System;
using System.Diagnostics;
using Atlas.Rendering;
using Unity.Mathematics;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.Endoluminal
{
    // Brief G5 3.4. A true 3D view of one bifurcation from inside the parent airway, rendered once per fork into a 512 x 512 texture
    // (MP 10.2.3 Mode 1, P2-B07). No per-frame hooks: all cost is inside Show, and only when the fork address changes.
    // No Camera and no layer (D-14); a CommandBuffer draws into the render texture.
    public sealed class EndoluminalWindow : MonoBehaviour
    {
        public const int TextureSize = 512;                  // MP 10.2.3
        public const float DisplaySizeM = 0.4f;              // [min] D-17
        public const float LabelRadiusFraction = 0.35f;
        public const float LabelHeightFraction = 0.06f;
        public static readonly Color Background = new Color(0.05f, 0.02f, 0.02f, 1f);

        static readonly ProfilerMarker Marker = new ProfilerMarker("Atlas.Lung.Endoluminal.Render");
        static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
        static readonly int ScreenParamsId = Shader.PropertyToID("_ScreenParams");   // TMP's SDF shader derives its edge softness from it (D-28)

        [SerializeField] Shader surfaceShader;      // "Atlas/AtlasSurface"
        [SerializeField] Shader displayShader;      // "Universal Render Pipeline/Unlit"
        [SerializeField] TMP_FontAsset labelFont;

        RenderTexture rt;
        readonly Mesh[] tubes = new Mesh[3];        // parent, left, right
        Material surfaceMat, displayMat;
        Mesh quad;
        GameObject displayGo;
        readonly GameObject[] labelObjects = new GameObject[2];
        readonly TextMeshPro[] labels = new TextMeshPro[2];
        readonly Matrix4x4[] labelMatrices = new Matrix4x4[2];
        readonly int[] hours = new int[2];
        readonly string[] labelTexts = new string[2];
        CommandBuffer cmd;
        AirwayTubeMeshBuilder tubeBuilder;
        readonly Stopwatch stopwatch = new Stopwatch();

        public bool IsInitialised { get; private set; }
        public RenderTexture Texture => rt;
        public Transform Display => displayGo != null ? displayGo.transform : null;
        public string RenderedAddress { get; private set; }
        public int RenderCount { get; private set; }
        public int LastRenderFrame { get; private set; } = -1;
        public double LastRenderCpuMs { get; private set; }

        public int ClockHour(int child) => hours[child];
        public string LabelText(int child) => labelTexts[child];

        void Awake()
        {
            if (surfaceShader != null && displayShader != null && labelFont != null) Initialise(surfaceShader, displayShader, labelFont);
        }

        // Idempotent. Does all allocation, once. The surface shader reads _WarpMode as a string property until P1-B03's ids merge
        // (switch to AtlasShaderIDs.WarpMode at integration).
        public void Initialise(Shader surface, Shader display, TMP_FontAsset font)
        {
            if (IsInitialised) return;
            if (surface == null || display == null || font == null) throw new ArgumentNullException("surface, display and font are required");

            rt = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "EndoluminalWindow.RT", antiAliasing = 4, useMipMap = false, autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            rt.Create();

            for (int i = 0; i < 3; i++) tubes[i] = new Mesh { name = "EndoluminalWindow.Tube" + i };
            tubeBuilder = new AirwayTubeMeshBuilder();

            surfaceMat = new Material(surface) { name = "EndoluminalWindow.Surface", hideFlags = HideFlags.HideAndDontSave };   // never saved as an asset (MP 9.3)
            surfaceMat.SetFloat("_WarpMode", 0f);
            displayMat = new Material(display) { name = "EndoluminalWindow.Display", hideFlags = HideFlags.HideAndDontSave };
            displayMat.mainTexture = rt;

            quad = new Mesh { name = "EndoluminalWindow.Quad" };
            float h = DisplaySizeM * 0.5f;
            quad.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(-h, h, 0), new Vector3(h, h, 0), new Vector3(h, -h, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            quad.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };      // faces -z
            quad.UploadMeshData(true);
            displayGo = new GameObject("Display");
            displayGo.transform.SetParent(transform, false);
            displayGo.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = displayGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = displayMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            displayGo.SetActive(false);

            for (int i = 0; i < 2; i++)
            {
                // TMP only builds a mesh once its Awake has run, so the object is created active (no frame passes) and then deactivated.
                var go = new GameObject("Label" + i);
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<TextMeshPro>();
                t.font = font; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap; t.fontSize = 36;
                go.SetActive(false);
                labelObjects[i] = go; labels[i] = t;
            }
            cmd = new CommandBuffer { name = "Atlas.Lung.Endoluminal" };
            IsInitialised = true;
        }

        // Returns true when the texture was (re)rendered. A repeat of the rendered address only re-activates the display.
        public bool Show(in ForkView f)
        {
            if (!IsInitialised) throw new InvalidOperationException("EndoluminalWindow.Initialise has not run");
            if (f.Address == RenderedAddress && rt.IsCreated()) { displayGo.SetActive(true); return false; }

            using (Marker.Auto())
            {
                stopwatch.Restart();
                if (!rt.IsCreated()) rt.Create();
                var pose = EndoluminalCamera.Pose(f);
                double s = EndoluminalCamera.SceneScale(f);
                double3 origin = f.Parent.EndMm;
                double rp = f.Parent.RadiusMm;

                tubeBuilder.Build(f.Parent, Math.Max(0, f.Parent.LenMm - EndoluminalCamera.ParentVisibleRadii * rp), f.Parent.LenMm, origin, s, pose.PositionMm, rp, tubes[0]);
                tubeBuilder.Build(f.Left, 0, f.Left.LenMm, origin, s, pose.PositionMm, rp, tubes[1]);
                tubeBuilder.Build(f.Right, 0, f.Right.LenMm, origin, s, pose.PositionMm, rp, tubes[2]);

                // Labels in the camera frame (D-16): depth 0.5 R_p, lateral offset 0.7 z (sin a, cos a), height 0.06 of the view height.
                Vector3 camPos = EndoluminalCamera.ToScene(pose.PositionMm, origin, s);
                Quaternion camRot = Quaternion.LookRotation(EndoluminalCamera.ToSceneDir(pose.Forward), EndoluminalCamera.ToSceneDir(pose.Up));
                float z = (float)(0.5 * EndoluminalCamera.SceneParentRadiusM);
                for (int i = 0; i < 2; i++)
                {
                    var c = f.Child(i);
                    double ang = EndoluminalCamera.ClockAngleDeg(pose, c.Dir);
                    hours[i] = EndoluminalCamera.ClockHour(pose, c.Dir);
                    labelTexts[i] = EndoluminalCamera.ClockLabel(hours[i]);
                    // TMP uploads its mesh only for an active object, so the label is active for this call only (no frame passes; D-28).
                    labelObjects[i].SetActive(true);
                    labels[i].SetText(labelTexts[i]);
                    labels[i].ForceMeshUpdate(true);
                    labelObjects[i].SetActive(false);
                    Bounds b = GlyphBounds(labels[i].mesh);   // TMP leaves mesh.bounds empty
                    float th = (float)(ang * Math.PI / 180.0);
                    float k = LabelHeightFraction * 2f * z / Mathf.Max(b.size.y, 1e-6f);
                    // keep the whole label inside the view (D-16): clamp the camera-space offset to 0.97 z minus the half extent
                    float ox = Mathf.Clamp(0.7f * z * Mathf.Sin(th), -(0.97f * z - 0.5f * b.size.x * k), 0.97f * z - 0.5f * b.size.x * k);
                    float oy = Mathf.Clamp(0.7f * z * Mathf.Cos(th), -(0.97f * z - 0.5f * b.size.y * k), 0.97f * z - 0.5f * b.size.y * k);
                    Vector3 pos = camPos + camRot * new Vector3(ox, oy, z);
                    labelMatrices[i] = Matrix4x4.TRS(pos, camRot, Vector3.one * k) * Matrix4x4.Translate(-b.center);
                }

                // Command buffer: tubes, then depth-only clear, then labels. Fog is off for this draw and restored (D-19).
                float oldFogStart = Shader.GetGlobalFloat(AtlasShaderIDs.FogStart), oldFogEnd = Shader.GetGlobalFloat(AtlasShaderIDs.FogEnd);
                cmd.Clear();
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true, true, Background);
                cmd.SetViewProjectionMatrices(EndoluminalCamera.ViewMatrix(pose, origin, s), GL.GetGPUProjectionMatrix(EndoluminalCamera.ProjectionMatrix(), true));
                cmd.SetGlobalVector(WorldSpaceCameraPosId, new Vector4(camPos.x, camPos.y, camPos.z, 1f));
                Vector4 oldScreen = Shader.GetGlobalVector(ScreenParamsId);
                cmd.SetGlobalVector(ScreenParamsId, new Vector4(TextureSize, TextureSize, 1f + 1f / TextureSize, 1f + 1f / TextureSize));
                cmd.SetGlobalFloat(AtlasShaderIDs.FogStart, 0f);
                cmd.SetGlobalFloat(AtlasShaderIDs.FogEnd, 0f);
                for (int i = 0; i < 3; i++) cmd.DrawMesh(tubes[i], Matrix4x4.identity, surfaceMat, 0, 0);
                cmd.ClearRenderTarget(true, false, Color.clear);
                for (int i = 0; i < 2; i++) cmd.DrawMesh(labels[i].mesh, labelMatrices[i], labels[i].fontSharedMaterial, 0, 0);
                cmd.SetGlobalVector(ScreenParamsId, oldScreen);
                cmd.SetGlobalFloat(AtlasShaderIDs.FogStart, oldFogStart);
                cmd.SetGlobalFloat(AtlasShaderIDs.FogEnd, oldFogEnd);
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Graphics.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                RenderedAddress = f.Address;
                RenderCount++;
                LastRenderFrame = Time.frameCount;
                displayGo.SetActive(true);
                stopwatch.Stop();
                LastRenderCpuMs = stopwatch.Elapsed.TotalMilliseconds;
            }
            return true;
        }

        static Bounds GlyphBounds(Mesh m)
        {
            var v = m.vertices;     // once per fork, 4 vertices per glyph
            if (v.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 1e-6f);
            Vector3 mn = v[0], mx = v[0];
            for (int i = 1; i < v.Length; i++) { mn = Vector3.Min(mn, v[i]); mx = Vector3.Max(mx, v[i]); }
            return new Bounds((mn + mx) * 0.5f, mx - mn);
        }

        public void Hide() { if (displayGo != null) displayGo.SetActive(false); }

        // Idempotent; called by OnDestroy. Public so edit-mode tests can release deterministically.
        public void Release()
        {
            if (!IsInitialised) return;
            IsInitialised = false;
            if (rt != null) { rt.Release(); DestroyObj(rt); rt = null; }
            for (int i = 0; i < tubes.Length; i++) { DestroyObj(tubes[i]); tubes[i] = null; }
            DestroyObj(surfaceMat); DestroyObj(displayMat); DestroyObj(quad);
            cmd?.Release(); cmd = null;
        }

        void OnDestroy() { Release(); }

        static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
