using System;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>
    /// Master Plan §10.4.4 beat 1 (P3-B06): one eye lit, a fixation cross, dots on a grid 8-22 deg temporal and +-8 deg vertical,
    /// tap when seen, catch trials re-probing the found spot. Reads no head pose and steps no kernel (MP §6.3).
    /// ExecuteAlways so OnDisable/OnDestroy restore the eye in EditMode tests too (deviation D16).
    /// </summary>
    [ExecuteAlways]
    public sealed class BlindSpot : MonoBehaviour
    {
        public const EyeSide DefaultEye = EyeSide.Right;   // D5

        public EyeSide Eye { get; set; } = DefaultEye;     // settable before Begin only
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;
        public BlindSpotProcedure Procedure { get; private set; }
        public bool HasResult { get; private set; }
        public BlindSpotResult LastResult { get; private set; }
        public Transform LitEye { get; private set; }
        public event Action<BlindSpotResult> Finished;

        IBlindSpotRig _rig; IBlindSpotLook _look; IBlindSpotTap _tap; IBlindSpotEvents _events;
        GameObject _root, _dot;
        Mesh _backdropMesh, _crossMesh, _dotMesh;
        EyeSide _litSide, _blanked;
        bool _isBlanked, _subscribed;

        public void Bind(IBlindSpotRig rig, IBlindSpotLook look, IBlindSpotTap tap, IBlindSpotEvents events)
        { _rig = rig; _look = look; _tap = tap; _events = events; }

        public bool Begin(uint seed)
        {
            if (_rig == null || _look == null || _tap == null)
            {
                if (!BlindSpotServices.TryResolve(out var r, out var l, out var t, out var e, out var missing))
                {
                    Debug.LogError("BlindSpot: seams missing: " + missing);
                    return false;
                }
                Bind(r, l, t, e);
            }
            if (Procedure != null && Procedure.Phase == BlindSpotPhase.Running) Abort();
            End();   // tear down a stimulus left over from a finished run (beat 5)

            _litSide = Eye;
            _blanked = _litSide == EyeSide.Left ? EyeSide.Right : EyeSide.Left;
            _rig.SetEyeBlank(_blanked, true);
            _isBlanked = true;
            LitEye = _rig.EyeTransform(_litSide);
            HasResult = false;

            _root = new GameObject("BlindSpot.Stimulus");
            _root.transform.SetParent(LitEye, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;
            var mat = _look.SurfaceMaterial;
            _backdropMesh = BlindSpotStimulus.Quad((float)BlindSpotStimulus.BackdropHalfM, _look.DarkUv);
            _crossMesh = BlindSpotStimulus.Cross((float)BlindSpotStimulus.CrossHalfLengthM, (float)BlindSpotStimulus.CrossHalfThicknessM, _look.BrightUv);
            _dotMesh = BlindSpotStimulus.Disc((float)BlindSpotStimulus.DotRadiusM, BlindSpotStimulus.DotSegments, _look.BrightUv);
            MakePart("BlindSpot.Backdrop", _backdropMesh, mat, (float)BlindSpotStimulus.BackdropDistanceM);
            MakePart("BlindSpot.Cross", _crossMesh, mat, (float)BlindSpotStimulus.StimulusDistanceM);
            _dot = MakePart("BlindSpot.Dot", _dotMesh, mat, 0f);
            _dot.SetActive(false);

            Procedure = new BlindSpotProcedure(_litSide, seed);
            Procedure.ProbeCompleted += OnProbe;
            Procedure.SpotFound += OnFound;
            Procedure.Finished += OnFinished;
            _tap.Tapped += OnTap; _subscribed = true;
            Procedure.Begin(Clock());
            Emit("blindspot_start", new BlindSpotStartData { eye = EyeName(_litSide), seed = seed });
            return true;
        }

        GameObject MakePart(string name, Mesh mesh, Material mat, float z)
        {
            var go = new GameObject(name);   // layer 0
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;   // shared material used as is, no MaterialPropertyBlock (MP §8.1)
            return go;
        }

        public void Abort()
        {
            if (Procedure != null && Procedure.Phase == BlindSpotPhase.Running) Procedure.Abort(Clock());
        }

        public void End()
        {
            Abort();
            if (_subscribed) { _tap.Tapped -= OnTap; _subscribed = false; }
            if (Procedure != null)
            {
                Procedure.ProbeCompleted -= OnProbe; Procedure.SpotFound -= OnFound; Procedure.Finished -= OnFinished;
            }
            if (_root != null) Kill(_root);
            _root = null; _dot = null;
            if (_backdropMesh != null) Kill(_backdropMesh);
            if (_crossMesh != null) Kill(_crossMesh);
            if (_dotMesh != null) Kill(_dotMesh);
            _backdropMesh = _crossMesh = _dotMesh = null;
            if (_isBlanked) { _isBlanked = false; _rig.SetEyeBlank(_blanked, false); }
            (_look as IDisposable)?.Dispose();   // a look that rented its material gives it back
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        public void Step(double nowS)
        {
            if (Procedure == null) return;
            Procedure.Advance(nowS);
            if (_dot == null) return;
            bool vis = Procedure.DotVisible;
            if (vis)
            {
                var p = Procedure.CurrentPoint;
                var dir = BlindSpotGrid.Direction(p.AzRad, p.ElRad);
                _dot.transform.localPosition = (float)BlindSpotStimulus.StimulusDistanceM * dir;
                _dot.transform.localRotation = Quaternion.LookRotation(dir);
            }
            if (_dot.activeSelf != vis) _dot.SetActive(vis);
        }

        void Update() { Step(Clock()); }
        void OnDisable() { End(); }
        void OnDestroy() { End(); }

        void OnTap() { if (Procedure != null) Procedure.Tap(Clock()); }

        static string EyeName(EyeSide e) => e == EyeSide.Left ? "left" : "right";
        const double Deg = 180.0 / Math.PI;

        void Emit(string ev, object data) { if (_events != null) _events.Emit(ev, data); }

        void OnProbe(ProbeRecord p)
        {
            Emit("blindspot_probe", new BlindSpotProbeData
            {
                index = p.Index, kind = p.Kind == ProbeKind.Grid ? "grid" : p.Kind == ProbeKind.Reprobe ? "reprobe" : "catch",
                az_deg = p.Point.AzRad * Deg, el_deg = p.Point.ElRad * Deg, onset_s = p.OnsetS,
                seen = p.Seen, rt_ms = p.Seen ? p.ResponseS * 1000.0 : -1.0,
            });
        }

        void OnFound(BlindSpotResult r)
        {
            Emit("blindspot_found", new BlindSpotFoundData { az_deg = r.FoundPoint.AzRad * Deg, el_deg = r.FoundPoint.ElRad * Deg, t_find_s = r.TimeToFindS });
        }

        void OnFinished(BlindSpotResult r)
        {
            Emit("blindspot_done", new BlindSpotDoneData
            {
                eye = EyeName(r.Eye), found = r.Found, aborted = r.Aborted,
                centre_az_deg = r.CentreAzRad * Deg, centre_el_deg = r.CentreElRad * Deg, radius_deg = r.RadiusRad * Deg,
                t_find_s = r.TimeToFindS, missed = r.MissedCount, catch_trials = r.CatchTrials, catch_fp = r.CatchFalsePositives,
                trials = r.Trials, duration_s = r.DurationS,
            });
            LastResult = r; HasResult = true;
            Finished?.Invoke(r);
        }
    }
}
