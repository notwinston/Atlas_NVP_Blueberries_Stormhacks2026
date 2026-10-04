using System.Collections;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Phone test-scene host (brief G7 §5.5, D13). Never in the product path. Reads the head pose itself.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class NeedleSkyTestScene : MonoBehaviour
    {
        public NeedleSkySettings settings;

        NeedleSkyBeat _beat;
        NeedleSkyPresenter _presenter;
        SkyMaps _maps;
        Transform _head;
        TextMeshPro _debug;
        long _frame;
        float _t0;
        int _sweepK = -1, _sweepOk, _sweepDone;   // _sweepDone: 0 running, 1 finished
        string _lastEvent = "";
        EventLog _log;

        IEnumerator Start()
        {
            if (!SceneManager.GetSceneByName("Atlas_Boot").isLoaded && Application.CanStreamedLevelBeLoaded("Atlas_Boot"))
            {
                var op = SceneManager.LoadSceneAsync("Atlas_Boot", LoadSceneMode.Additive);
                while (!op.isDone) yield return null;
                SceneManager.SetActiveScene(SceneManager.GetSceneByName("Atlas_Boot"));
            }
            float w0 = Time.realtimeSinceStartup;
            IStereoRig rig = null;
            while (Time.realtimeSinceStartup - w0 < 10f && !AtlasServices.TryGet(out rig)) yield return null;
            _head = rig != null ? rig.Head : null;
            if (_head == null) { Debug.LogError("NeedleSkyTestScene: no IStereoRig published by the composition root"); yield break; }

            var registry = new ParamRegistry();
            _maps = SkyMaps.Load(NeedleSkyServices.CaseRoot("illustrative"));
            _presenter = new NeedleSkyPresenter(_maps, settings, transform);
            var input = NeedleSkyServices.Input();
            if (input == null) Debug.LogWarning("NeedleSkyTestScene: no IAtlasInput published; the beat reads no action and no stick");
            _beat = new NeedleSkyBeat(_maps, registry, NeedleSkyServices.EventLogSink(), input, _presenter);
            _beat.Enter();

            var go = new GameObject("NeedleSkyDebugLine");
            go.transform.SetParent(transform, false);
            _debug = go.AddComponent<TextMeshPro>();
            _debug.fontSize = NeedleSkyConstants.PanelFontSize * 0.7f; _debug.richText = false;
            _debug.alignment = TextAlignmentOptions.Center; _debug.textWrappingMode = TextWrappingModes.NoWrap;
            if (AtlasServices.TryGet<IEventLog>(out var log)) { _log = log as EventLog; if (_log != null) _log.Logged += OnLogged; }
            _t0 = Time.time;
        }

        void OnLogged(EventMessage m) { if (m.Scene == LungEvents.Scene) _lastEvent = m.Ev + " " + m.Data.ToJson(false); }

        void OnDestroy() { if (_log != null) _log.Logged -= OnLogged; _presenter?.Dispose(); }

        void Update()
        {
            if (_beat == null) return;
            quaternion rot = _head.rotation;
            float3 pos = _head.position;
            float el = Time.time - _t0;
            string line = null;
            var checks = _maps.Meta.checks;
            if (_sweepDone == 0)
            {
                int k = Mathf.FloorToInt(el);
                if (k > _sweepK && _sweepK >= 0 && _sweepK < checks.Length)
                    CompareSweep(_sweepK);                                  // 1-s mark of the previous check
                if (k >= checks.Length)
                {
                    _sweepDone = 1; line = "Sweep: " + _sweepOk + "/" + checks.Length + " within 0.1 mm";
                    _beat.Enter();
                    _debug.text = line;
                }
                else
                {
                    _sweepK = k;
                    var c = checks[k];
                    rot = Quaternion.LookRotation((Vector3)(float3)SkyDirection.FromAzEl(c.az_deg, c.el_deg), Vector3.up);
                    line = "Sweep " + (k + 1) + "/" + checks.Length + ": sky.json " + c.clear_mm.ToString("F1") + " mm";
                }
            }
            _beat.Tick(new FrameContext
            {
                FrameIndex = (int)_frame++, Time = Time.timeAsDouble, DeltaTime = Time.deltaTime, HeadPosition = pos, HeadRotation = rot,
                RawHeadPosition = pos, RawHeadRotation = rot, Tracking = TrackingState.Normal,
            });
            // place the debug line under the panel (0.1 m lower, same head-locked frame)
            if (_debug != null)
            {
                var q = (Quaternion)rot;
                _debug.transform.SetPositionAndRotation((Vector3)pos + q * new Vector3(0f, -0.4f, 0.97f), q);
                if (line != null) _debug.text = line;
                else if (_sweepDone == 1 && _lastEvent.Length > 0 && _debug.text != _lastEvent) _debug.text = _lastEvent;
            }
        }

        void CompareSweep(int k)
        {
            var c = _maps.Meta.checks[k];
            float shown = _beat.Readout.ClearanceShownMm;
            if (Mathf.Abs(shown - c.clear_mm) <= 0.1f + 1e-4f) _sweepOk++;
            else Debug.Log("NeedleSkyTestScene sweep " + (k + 1) + ": shown " + shown + " mm, sky.json " + c.clear_mm + " mm");
        }
    }
}
