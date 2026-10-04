using System;
using UnityEngine;

namespace Atlas.Hands
{
    /// <summary>
    /// The three short sounds of the hands (documents/ATLAS_Hands_Design.md section 6.6): a click for a press, a
    /// softer tap for a contact with a surface, a two-note tick for a pinch. Made in code at boot, played quietly
    /// from one source at the head. They sit above Bump Guard's tick and tone in pitch and are far shorter, so the
    /// two cannot be confused; the phone's haptics are never used (decision H9).
    /// </summary>
    public sealed class HandsSounds : IDisposable
    {
        const int SampleRate = 44100;
        const float Volume = 0.22f;

        readonly GameObject _object;
        readonly AudioSource _source;
        readonly AudioClip _press, _contact, _pinch;

        /// <summary>The source is a child of parent (the head), so the sounds are heard at the walker.</summary>
        public HandsSounds(Transform parent)
        {
            _object = new GameObject("Atlas Hands Sounds");
            _object.transform.SetParent(parent, false);
            _source = _object.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = Volume;
            _press = Tone("Hands Press", 2100f, 2100f, 0.018f);
            _contact = Tone("Hands Contact", 950f, 950f, 0.028f);
            _pinch = Tone("Hands Pinch", 1500f, 2300f, 0.030f);
        }

        public void Press() { Play(_press); }
        public void Contact() { Play(_contact, 0.6f); }
        public void Pinch() { Play(_pinch); }

        void Play(AudioClip clip, float scale = 1f)
        {
            if (_source != null && clip != null) _source.PlayOneShot(clip, scale);
        }

        // A short sine that glides from one pitch to another under a fast attack and an exponential decay.
        static AudioClip Tone(string name, float fromHz, float toHz, float seconds)
        {
            int count = Mathf.Max(8, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float hz = Mathf.Lerp(fromHz, toHz, t);
                phase += 2f * Mathf.PI * hz / SampleRate;
                float envelope = Mathf.Min(1f, t * 40f) * Mathf.Exp(-5f * t);
                data[i] = Mathf.Sin(phase) * envelope;
            }
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void Dispose()
        {
            Kill(_press);
            Kill(_contact);
            Kill(_pinch);
            Kill(_object);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
