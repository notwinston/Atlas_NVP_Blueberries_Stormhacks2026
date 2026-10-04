using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>Test-scene controller for the Blind-Spot Finder and river text (Brief §6, D13). Advanced by the confirm tap only.</summary>
    public sealed class BlindSpotTestScene : MonoBehaviour
    {
        enum State { Booting, Intro, Running, Result }

        State _state = State.Booting;
        BlindSpot _blindSpot;
        TextMeshPro _text;
        IBlindSpotRig _rig; IBlindSpotLook _look; IBlindSpotTap _tap; IBlindSpotEvents _events;

        IEnumerator Start()
        {
            if (!SceneManager.GetSceneByName("Atlas_Boot").isLoaded && Application.CanStreamedLevelBeLoaded("Atlas_Boot"))
            {
                var op = SceneManager.LoadSceneAsync("Atlas_Boot", LoadSceneMode.Additive);
                while (!op.isDone) yield return null;
                SceneManager.SetActiveScene(SceneManager.GetSceneByName("Atlas_Boot"));
            }
            string missing = "";
            bool ok = false;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                if (BlindSpotPlaceholderSeams.TryResolve(out _rig, out _look, out _tap, out _events, out missing)) { ok = true; break; }
                yield return null;
            }
            _blindSpot = gameObject.AddComponent<BlindSpot>();
            BuildText();
            if (!ok)
            {
                Debug.LogError("BlindSpotTestScene: missing " + missing);
                SetText(BlindSpotStrings.NoRig);
                yield break;
            }
            _blindSpot.Bind(_rig, _look, _tap, _events);
            _blindSpot.Finished += OnFinished;
            _tap.Tapped += OnTap;
            Enter(State.Intro);
        }

        void BuildText()
        {
            var go = new GameObject("FieldFitInstructions");
            go.transform.SetParent(_rig != null ? _rig.EyeTransform(_blindSpot.Eye) : transform, false);
            var d = BlindSpotGrid.Direction(0, 14.0 * Math.PI / 180.0);
            go.transform.localPosition = d;
            go.transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
            _text = go.AddComponent<TextMeshPro>();
            _text.fontSize = 0.5f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.richText = false;
        }

        void SetText(string s) { _text.text = s; _text.gameObject.SetActive(!string.IsNullOrEmpty(s)); }

        void Enter(State s)
        {
            _state = s;
            switch (s)
            {
                case State.Intro: SetText(BlindSpotStrings.Intro); break;
                case State.Running: SetText(""); break;
                            }
        }

        void OnTap()
        {
            switch (_state)
            {
                case State.Intro:
                    Enter(State.Running);
                    if (!_blindSpot.Begin((uint)DateTime.UtcNow.Ticks)) Enter(State.Intro);
                    break;
                case State.Running: break;   // taps go to BlindSpot only
                case State.Result:
                    _blindSpot.End(); Enter(State.Intro);
                    break;
            }
        }

        void OnFinished(BlindSpotResult r)
        {
            if (r.Aborted) return;
            _state = State.Result;
            SetText(r.Found ? BlindSpotStrings.Result(r) : BlindSpotStrings.NotFound);
        }

        void OnDestroy()
        {
            if (_tap != null) _tap.Tapped -= OnTap;
            if (_blindSpot != null) _blindSpot.Finished -= OnFinished;
        }
    }
}
