using System;
using Atlas.Input;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>
    /// The ONLY file naming lane A/C/D types: binds the four seams of BlindSpotSeams.cs to the services in AtlasServices
    /// (IStereoRig, MaterialPool, IAtlasInput, IEventLog). A service that is not published is reported through
    /// <c>missing</c>; this adapter fills none (Brief §2).
    /// </summary>
    public static class BlindSpotServices
    {
        public static bool TryResolve(out IBlindSpotRig rig, out IBlindSpotLook look, out IBlindSpotTap tap,
            out IBlindSpotEvents events, out string missing)
        {
            string m = "";
            rig = null; look = null; tap = null;

            if (AtlasServices.TryGet(out IStereoRig sr) && sr.GetEyeCamera(Eye.Left) != null && sr.GetEyeCamera(Eye.Right) != null) rig = new CameraRig(sr);
            else m += "IStereoRig with both eye cameras (lane A P1-A04); ";

            if (AtlasServices.TryGet(out MaterialPool pool)) look = new PooledLook(pool);
            else m += "MaterialPool (lane A P1-A06); ";

            if (AtlasServices.TryGet(out IAtlasInput input)) tap = new ConfirmTap(input);
            else m += "IAtlasInput (lane C P1-C10); ";

            events = new EventLogSink();
            missing = m.TrimEnd(' ', ';');
            return rig != null && look != null && tap != null;
        }

        sealed class EventLogSink : IBlindSpotEvents
        {
            // The log stamps t_us itself (PH-31); the scene is the blade's own, not the space entered.
            public void Emit(string ev, object data)
            {
                if (AtlasServices.TryGet(out IEventLog log)) log.LogJson("fieldfit", ev, data == null ? "{}" : JsonUtility.ToJson(data));
            }
        }

        // The look's own pooled instance: the lightest and darkest flat swatches of the shared palette, no fog, no world fade.
        // Rented on first use and given back by Dispose (BlindSpot.End), so a finished beat holds no material.
        sealed class PooledLook : IBlindSpotLook, IDisposable
        {
            readonly MaterialPool _pool;
            Material _material;
            public PooledLook(MaterialPool pool) { _pool = pool; }

            public Material SurfaceMaterial
            {
                get
                {
                    if (_material == null)
                    {
                        _material = _pool.Rent();
                        MaterialPool.SetSurface(_material, emissiveGain: 1f, fogWeight: 0f, nearFadeMetres: 0f, fadeWeight: 0f);
                    }
                    return _material;
                }
            }

            public Vector2 BrightUv { get { var uv = AtlasPalette.Uv(AtlasPalette.Reticle); return new Vector2(uv.x, uv.y); } }   // white accent
            public Vector2 DarkUv { get { var uv = AtlasPalette.Uv(AtlasPalette.Void); return new Vector2(uv.x, uv.y); } }      // darkest base

            public void Dispose()
            {
                if (_material == null) return;
                _pool.Return(_material);
                _material = null;
            }
        }

        // Confirm is the tap: the input service raises ActionFired inside its Tick, from any twin.
        sealed class ConfirmTap : IBlindSpotTap
        {
            readonly IAtlasInput _input;
            Action _tapped;
            public ConfirmTap(IAtlasInput input) { _input = input; }

            public event Action Tapped
            {
                add { if (_tapped == null) _input.ActionFired += OnFired; _tapped += value; }
                remove { _tapped -= value; if (_tapped == null) _input.ActionFired -= OnFired; }
            }

            void OnFired(AtlasAction action, InputSource source, int tag) { if (action == AtlasAction.Confirm) _tapped?.Invoke(); }
        }

        // Blanking is done on the eye cameras (the rig has no per-eye blank): nothing drawn, black clear.
        sealed class CameraRig : IBlindSpotRig
        {
            readonly IStereoRig _rig;
            readonly bool[] _blank = new bool[2];
            readonly int[] _mask = new int[2];
            readonly CameraClearFlags[] _flags = new CameraClearFlags[2];
            readonly Color[] _bg = new Color[2];
            public CameraRig(IStereoRig rig) { _rig = rig; }
            Camera Cam(EyeSide e) => _rig.GetEyeCamera(e == EyeSide.Left ? Eye.Left : Eye.Right);
            public Transform EyeTransform(EyeSide eye) => Cam(eye).transform;

            public void SetEyeBlank(EyeSide eye, bool blank)
            {
                int i = (int)eye; var c = Cam(eye);
                if (c == null || _blank[i] == blank) return;
                if (blank)
                {
                    _mask[i] = c.cullingMask; _flags[i] = c.clearFlags; _bg[i] = c.backgroundColor;
                    c.cullingMask = 0; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black;
                }
                else { c.cullingMask = _mask[i]; c.clearFlags = _flags[i]; c.backgroundColor = _bg[i]; }
                _blank[i] = blank;
            }
        }
    }
}
