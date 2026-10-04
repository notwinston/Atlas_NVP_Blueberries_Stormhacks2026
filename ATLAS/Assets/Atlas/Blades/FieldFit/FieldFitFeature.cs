using System;
using System.Globalization;
using Atlas.Kernel;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Atlas.Blades.FieldFit
{
    /// <summary>FieldFit's own shader property ids (DV12): never added to AtlasShaderIDs.cs (the §6.4 look-shader contract).</summary>
    internal static class FieldFitShaderIDs
    {
        public static readonly int SourceL = Shader.PropertyToID("_FFSourceL");
        public static readonly int SourceR = Shader.PropertyToID("_FFSourceR");
        public static readonly int LutL = Shader.PropertyToID("_FFLutL");
        public static readonly int LutR = Shader.PropertyToID("_FFLutR");
        public static readonly int TanE = Shader.PropertyToID("_FFTanE");
        public static readonly int FrustumL = Shader.PropertyToID("_FFFrustumL");
        public static readonly int FrustumR = Shader.PropertyToID("_FFFrustumR");
        public static readonly int Badge = Shader.PropertyToID("_FFBadge");
        public static readonly int BadgeRectL = Shader.PropertyToID("_FFBadgeRectL");
        public static readonly int BadgeRectR = Shader.PropertyToID("_FFBadgeRectR");
    }

    /// <summary>
    /// FieldFit: while lens.on = 1 the eye cameras render into wide square sources and lane A's single final blit draws
    /// FieldFitBlit (lookup instead of the barrel step). Master Plan §10.4.3; Brief §1, §8. DV2: a MonoBehaviour that
    /// substitutes the material of the one final pass (R2), not a URP renderer feature.
    /// </summary>
    public sealed class FieldFitFeature : MonoBehaviour
    {
        [SerializeField] FieldFitSettings settings;

        // Master Plan §10.4.3 / §8.6: ladder 1,536 -> 1,280 -> 1,024; 2,048^2 on High only.
        public static readonly int[] SourceSizeByRung = { 1536, 1280, 1024, 1024 };
        public const int HighSourceSize = 2048;
        public static readonly int[] MsaaByRung = { 4, 4, 2, 1 };
        public const int HighMsaa = 4;

        const float Deg2Rad = (float)(Math.PI / 180.0), Rad2Deg = (float)(180.0 / Math.PI);
        static readonly ProfilerMarker FrameMarker = new ProfilerMarker("Atlas.FieldFit.Frame");   // Brief §8
        static readonly Vector4 Unbounded = new Vector4(-1e4f, 1e4f, -1e4f, 1e4f);                 // Brief §7.3

        enum Cause { Displacement, Tap, Param }
        static readonly string[] CauseName = { "displacement", "tap", "param" };

        IFieldFitRig rig; ILensParamSource src; ILensEventSink sink;
        readonly RenderTexture[][] sets = new RenderTexture[5][];   // [rung][eye]; every rung allocated once (DV8)
        LensLut lut; FieldLossMask mask; LensBadge badge; Material material; LensGuard guard;
        bool setupDone, subscribed, active, latched, prevOn, built, errLogged, uniformsDirty;
        LensParams current, lastBuilt, lastInvalid; bool haveInvalid;
        int lastFrame = int.MinValue, appliedSet = -1; double appliedTan = double.NaN;
        LensProfile appliedProfile = (LensProfile)255;
        float lastD;
        Func<int> frameSource = DefaultFrame;
        static int DefaultFrame() { return Time.frameCount; }

        public bool LensActive => active;
        public LensParams CurrentParams => current;
        internal int RenderTexturesCreated { get; private set; }
        internal int FrameWorkCount { get; private set; }
        internal LensLut Lut => lut;
        internal Material BlitMaterial => material;
        internal RenderTexture SourceTexture(int rung, int eye) { return sets[rung][eye]; }
        internal Func<int> FrameSource { set { frameSource = value ?? DefaultFrame; } }
        internal FieldFitSettings Settings { get { return settings; } set { settings = value; } }
        internal LensGuard Guard => guard;

        /// <summary>Master Plan §10.4.5: tap-off. B05 binds the input action here.</summary>
        public void TapOff() { guard?.RequestTapOff(); }

        // ---- lifecycle ------------------------------------------------------------------------------------------

        void OnEnable()
        {
            if (setupDone) return;
            var s = LensParamBinding.CreateSource();
            if (s == null) { Debug.LogError("FieldFit: lens.* parameters are not registered (lane D P3-D02) or FieldFitFeature.BindRegistry was not called; the lens stays off."); enabled = false; return; }
            if (!PlaceholderRigBinding.Available) { Debug.LogError("FieldFit: no StereoRig; the lens stays off."); enabled = false; return; }
            Init(new PlaceholderRigBinding(), s, LensParamBinding.CreateSink());
        }

        void OnDisable() { Shutdown(); }

        /// <summary>Tests and runtime adapters (Brief §5). Builds everything of §8 "Enable".</summary>
        internal void Init(IFieldFitRig rig, ILensParamSource src, ILensEventSink ev)
        {
            if (setupDone) Shutdown();
            this.rig = rig; this.src = src; sink = ev;
            if (settings == null || settings.blitShader == null) { Debug.LogError("FieldFit: settings or blit shader missing; the lens stays off."); return; }

            lut = new LensLut();
            lut.SetEyeGeometry(0, rig.EyeGeometry(0));           // stage A, once (Brief §7.1)
            lut.SetEyeGeometry(1, rig.EyeGeometry(1));
            for (int r = 0; r < 5; r++)
            {
                int size = r < 4 ? SourceSizeByRung[r] : HighSourceSize, msaa = r < 4 ? MsaaByRung[r] : HighMsaa;
                sets[r] = new[] { MakeSource(size, msaa, r, 0), MakeSource(size, msaa, r, 1) };
            }
            material = new Material(settings.blitShader) { hideFlags = HideFlags.DontSave };   // never Shader.Find
            // P3-B04 (Master Plan §10.4.6, DV6): the tunnel mask from settings when assigned; none otherwise.
            mask = settings.fieldLossMask != null ? FieldLossMask.FromTexture(settings.fieldLossMask, settings.maskHalfWidthDeg) : FieldLossMask.Absent;
            guard = new LensGuard();
            badge = LensBadge.Create(rig.EyeGeometry(0), rig.EyeGeometry(1), lut);
            material.SetTexture(FieldFitShaderIDs.LutL, lut.Left);
            material.SetTexture(FieldFitShaderIDs.LutR, lut.Right);
            if (badge.Texture != null) material.SetTexture(FieldFitShaderIDs.Badge, badge.Texture);
            material.SetVector(FieldFitShaderIDs.BadgeRectL, badge.RectLeft);
            material.SetVector(FieldFitShaderIDs.BadgeRectR, badge.RectRight);

            Application.onBeforeRender += FrameUpdate;   // DV9: after every Update incl. AtlasFrameLoop, before the eye cameras
            subscribed = true; setupDone = true;
        }

        RenderTexture MakeSource(int size, int msaa, int rung, int eye)
        {
            var depthFmt = SystemInfo.IsFormatSupported(GraphicsFormat.D24_UNorm_S8_UInt, GraphicsFormatUsage.Render)
                ? GraphicsFormat.D24_UNorm_S8_UInt : GraphicsFormat.D32_SFloat_S8_UInt;
            var d = new RenderTextureDescriptor(size, size, GraphicsFormat.R8G8B8A8_SRGB, depthFmt)
            {
                msaaSamples = msaa,
                bindMS = false, useMipMap = false, autoGenerateMips = false, dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            };
            var rt = new RenderTexture(d) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
                                            name = "FieldFit_Src" + rung + (eye == 0 ? "L" : "R") };
            // Master Plan §8.6: multisampled colour and depth stay in tile memory (ignored in the Editor).
            rt.memorylessMode = RenderTextureMemoryless.MSAA | RenderTextureMemoryless.Depth;
            rt.Create();
            RenderTexturesCreated++;
            return rt;
        }

        internal void Shutdown()
        {
            if (subscribed) { Application.onBeforeRender -= FrameUpdate; subscribed = false; }
            if (active) Deactivate();
            for (int r = 0; r < 5; r++)
                if (sets[r] != null) { foreach (var rt in sets[r]) if (rt != null) { rt.Release(); Kill(rt); } sets[r] = null; }
            lut?.Dispose(); lut = null;
            mask?.Dispose(); mask = null;     // Absent is a shared stub: Dispose is a no-op for it
            badge?.Dispose(); badge = null;
            if (material != null) Kill(material);
            material = null; setupDone = false; built = false; latched = false; prevOn = false; appliedSet = -1;
        }

        static void Kill(UnityEngine.Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        // ---- per frame ------------------------------------------------------------------------------------------

        /// <summary>Once per Time.frameCount (Brief §8). Neither steps the kernel nor waits on the network (Master Plan §6.3).</summary>
        internal void FrameUpdate()
        {
            if (!setupDone) return;
            int fc = frameSource();
            if (fc == lastFrame) return;
            lastFrame = fc; FrameWorkCount++;
            using (FrameMarker.Auto()) Work(fc);
        }

        void Work(int fc)
        {
            if (!src.TryRead(out float eDeg, out float rho0Deg, out float e1Deg, out float tt, out int profile, out bool on)) return;
            var p = new LensParams((LensProfile)profile, eDeg * Deg2Rad, rho0Deg * Deg2Rad, e1Deg * Deg2Rad, tt);
            current = p;

            if (!on)
            {
                latched = false;                       // the latch clears when lens.on reads 0 (Brief §8)
                if (active) TurnOff(Cause.Param, fc, false);
                prevOn = false; return;
            }

            bool justActivated = false;
            if (!active && !latched && !prevOn) justActivated = TryActivate(p);
            prevOn = true;
            if (!active) return;

            if (!justActivated)
            {
                bool exceeded = guard.Exceeded(rig.HeadPosition, out float d);
                if (guard.ConsumeTapOff()) { TurnOff(Cause.Tap, fc, true, d); return; }
                if (exceeded) { TurnOff(Cause.Displacement, fc, true, d); return; }
                lastD = d;
            }

            if (!built || !p.Equals(lastBuilt))
            {
                if (lut.Rebuild(p, mask)) { lastBuilt = p; built = true; uniformsDirty = true; }
                else if (!haveInvalid || !p.Equals(lastInvalid))
                {
                    lastInvalid = p; haveInvalid = true;
                    Debug.LogWarning("FieldFit: invalid lens parameters; keeping the previous lookup.");
                }
            }
            if (built) ApplyRig(lastBuilt);
        }

        bool TryActivate(in LensParams p)
        {
            if (!badge.Ready)
            {
                if (!errLogged) { errLogged = true; Debug.LogError("FieldFit: the badge is not ready; the lens may not run without its badge (Brief §8)."); }
                return false;
            }
            guard.Arm(rig.HeadPosition);
            lastD = 0f; active = true; uniformsDirty = true; appliedSet = -1; appliedTan = double.NaN; appliedProfile = (LensProfile)255;
            Emit("lensOn", LensOnJson(p));
            return true;
        }

        void ApplyRig(in LensParams p)
        {
            int set = Mathf.Clamp((int)rig.Rung, 0, 4);
            double tanE = math.tan(p.E);
            bool setChanged = set != appliedSet, tanChanged = tanE != appliedTan;
            if (setChanged || tanChanged)
            {
                var cs = settings.centreEyeSource;
                for (int e = 0; e < 2; e++)
                    rig.SetEyeOverride(e, sets[set][e], (float)tanE, cs ? 0f : rig.NormalLateralOffset(e));
            }
            if (setChanged)
            {
                material.SetTexture(FieldFitShaderIDs.SourceL, sets[set][0]);
                material.SetTexture(FieldFitShaderIDs.SourceR, sets[set][1]);
                appliedSet = set;
            }
            if (tanChanged) { material.SetFloat(FieldFitShaderIDs.TanE, (float)tanE); appliedTan = tanE; }
            if (p.Profile != appliedProfile)
            {
                // Profile None keeps the eye's ordinary frustum edge; the lens profiles clip by lookup (Brief §7.3).
                material.SetVector(FieldFitShaderIDs.FrustumL, p.Profile == LensProfile.None ? rig.EyeGeometry(0).Frustum : Unbounded);
                material.SetVector(FieldFitShaderIDs.FrustumR, p.Profile == LensProfile.None ? rig.EyeGeometry(1).Frustum : Unbounded);
                appliedProfile = p.Profile;
            }
            if (rigBlitUnset) { rig.SetFinalBlitMaterial(material); rigBlitUnset = false; }
            uniformsDirty = false;
        }
        bool rigBlitUnset = true;

        void TurnOff(Cause cause, int fc, bool setParam, float d = 0f)
        {
            if (setParam) { src.SetLensOn(false); latched = true; }
            if (cause == Cause.Param) guard.Exceeded(rig.HeadPosition, out d);
            float dPrev = lastD;
            Deactivate();
            Emit("lensOff", string.Format(CultureInfo.InvariantCulture,
                "{{\"cause\":\"{0}\",\"d_m\":{1:R},\"dPrev_m\":{2:R},\"frame\":{3}}}", CauseName[(int)cause], d, dPrev, fc));
        }

        void Deactivate()
        {
            active = false; rigBlitUnset = true;
            rig.ClearFinalBlitMaterial();
            rig.ClearEyeOverrides();
        }

        void Emit(string ev, string json) { sink?.Emit(ev, json); }

        static string LensOnJson(in LensParams p)
        {
            double k = p.IsValid ? LensMath.KStar(p) : 0.0;
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"profile\":{0},\"E\":{1:R},\"rho0\":{2:R},\"e1\":{3:R},\"t\":{4:R},\"kStar\":{5:R}",
                (int)p.Profile, p.E * 180.0 / Math.PI, p.Rho0 * 180.0 / Math.PI, p.E1 * 180.0 / Math.PI, p.T, k) + "}";
        }
    }
}
