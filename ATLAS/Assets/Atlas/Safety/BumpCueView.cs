using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Safety
{
    /// <summary>What BumpCue draws and plays through. The engine side is <see cref="BumpCueView"/>.</summary>
    public interface IBumpCueView
    {
        /// <summary>Shows one indicator (or none) at a place in the head-locked parent's space.</summary>
        void Show(CueIcon icon, float3 position, float rollRadians, float scale);
        /// <summary>Plays one sound now, from a place in the head-locked parent's space or with no side.</summary>
        void Play(CueSound sound, float3 position, bool spatial);
        void Dispose();
    }

    /// <summary>
    /// The indicators and the sounds of the warning cue (Master Plan section 4.10, task P1-C14): three flat
    /// meshes and two AudioClips made in code, parented to the head-locked HUD transform and put on its layer,
    /// so both eye cameras draw them. One indicator is enabled at a time: one draw call. It moves only its own
    /// objects under the HUD parent and never touches the world. Show and Play allocate nothing.
    /// </summary>
    public sealed class BumpCueView : IBumpCueView
    {
        public const string ShaderResource = "AtlasCue";

        const int IconCount = 5;                    // the values of CueIcon

        // Lane A's palette cells 197 to 200 (CueWarn, CueStrong, CueRescan, CueStandStill).
        static readonly Color32 WarnColour = new Color32(0xFF, 0xB0, 0x20, 0xFF);
        static readonly Color32 StrongColour = new Color32(0xFF, 0x3B, 0x30, 0xFF);
        static readonly Color32 RescanColour = new Color32(0x5A, 0xC8, 0xFA, 0xFF);
        static readonly Color32 StandStillColour = new Color32(0xFF, 0xFF, 0xFF, 0xFF);

        readonly GameObject _root;
        readonly Transform[] _transforms = new Transform[IconCount];
        readonly MeshRenderer[] _renderers = new MeshRenderer[IconCount];
        readonly Mesh[] _meshes = new Mesh[IconCount];
        readonly Material _ownMaterial;
        readonly AudioSource _audio;
        readonly Transform _audioTransform;
        readonly AudioClip _tick, _tone;

        CueIcon _icon = CueIcon.None;
        float3 _position;
        float _roll, _scale = 1f;
        float3 _soundPosition;
        bool _soundSpatial;

        /// <summary>
        /// Builds the cue under the head-locked parent. It draws with its own unlit shader from Resources; the
        /// material given is used only when that shader cannot be loaded.
        /// </summary>
        public BumpCueView(Transform hud, Material fallbackMaterial)
        {
            int layer = hud.gameObject.layer;
            _root = new GameObject("Bump Cue") { layer = layer };
            _root.transform.SetParent(hud, false);

            Material material = fallbackMaterial;
            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader != null)
            {
                _ownMaterial = new Material(shader) { name = "Bump Cue", hideFlags = HideFlags.DontSave };
                material = _ownMaterial;
            }

            CueShape chevron = BumpCueShapes.Chevron();
            AddIcon(CueIcon.ChevronWarn, "Chevron Warn", chevron, WarnColour, 197, material, layer);
            AddIcon(CueIcon.ChevronStrong, "Chevron Strong", chevron, StrongColour, 198, material, layer);
            AddIcon(CueIcon.StandStill, "Stand Still", BumpCueShapes.StandStill(), StandStillColour, 200, material, layer);
            AddIcon(CueIcon.Rescan, "Rescan", BumpCueShapes.Rescan(), RescanColour, 199, material, layer);

            _tick = MakeClip("Bump Cue Tick", BumpCueTones.Tick());
            _tone = MakeClip("Bump Cue Tone", BumpCueTones.Tone());

            var audioObject = new GameObject("Bump Cue Sound") { layer = layer };
            _audioTransform = audioObject.transform;
            _audioTransform.SetParent(_root.transform, false);
            _audioTransform.localPosition = new Vector3(0f, 0f, BumpCueScheduler.IndicatorDistance);
            _soundPosition = new float3(0f, 0f, BumpCueScheduler.IndicatorDistance);
            _audio = audioObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;
            _audio.dopplerLevel = 0f;               // head-locked: it never moves against the listener
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 1f;                // full level at the 1 m the source sits at
            _audio.maxDistance = 10f;
            _audio.bypassEffects = true;
            _audio.bypassListenerEffects = true;
            _audio.bypassReverbZones = true;
        }

        public void Show(CueIcon icon, float3 position, float rollRadians, float scale)
        {
            if (_root == null) return;                 // destroyed with the scene
            if (icon != _icon)
            {
                if (_icon != CueIcon.None) _renderers[(int)_icon].enabled = false;
                if (icon != CueIcon.None) _renderers[(int)icon].enabled = true;
                _icon = icon;
                _scale = float.NaN;                    // the new indicator takes its pose below
            }
            if (icon == CueIcon.None) return;
            if (scale == _scale && rollRadians == _roll && math.all(position == _position)) return;

            _position = position;
            _roll = rollRadians;
            _scale = scale;
            Transform t = _transforms[(int)icon];
            math.sincos(0.5f * rollRadians, out float s, out float c);
            t.localPosition = new Vector3(position.x, position.y, position.z);
            t.localRotation = new Quaternion(0f, 0f, s, c);
            t.localScale = new Vector3(scale, scale, 1f);
        }

        public void Play(CueSound sound, float3 position, bool spatial)
        {
            if (_audio == null || sound == CueSound.None) return;
            if (spatial != _soundSpatial)
            {
                _soundSpatial = spatial;
                _audio.spatialBlend = spatial ? 1f : 0f;
            }
            if (spatial && !math.all(position == _soundPosition))
            {
                _soundPosition = position;
                _audioTransform.localPosition = new Vector3(position.x, position.y, position.z);
            }
            _audio.PlayOneShot(sound == CueSound.Tone ? _tone : _tick, 1f);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            for (int i = 0; i < IconCount; i++)
                if (_meshes[i] != null) Object.Destroy(_meshes[i]);
            if (_ownMaterial != null) Object.Destroy(_ownMaterial);
            if (_tick != null) Object.Destroy(_tick);
            if (_tone != null) Object.Destroy(_tone);
        }

        void AddIcon(CueIcon icon, string name, CueShape shape, Color32 colour, int paletteCell, Material material, int layer)
        {
            int n = shape.PointCount;
            var vertices = new Vector3[n];
            var normals = new Vector3[n];
            var colours = new Color32[n];
            var uv = new Vector2[n];
            // The palette coordinate of the colour, for the shared look material when it stands in for the cue shader.
            var cell = new Vector2((paletteCell % 16 + 0.5f) / 16f, (paletteCell / 16 + 0.5f) / 16f);
            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(shape.Points[i].x, shape.Points[i].y, 0f);
                normals[i] = Vector3.back;             // towards the viewer
                colours[i] = colour;
                uv[i] = cell;
            }
            var triangles = new int[shape.IndexCount];
            System.Array.Copy(shape.Triangles, triangles, shape.IndexCount);

            var mesh = new Mesh { name = "Bump Cue " + name, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colours;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            var go = new GameObject(name) { layer = layer };
            Transform t = go.transform;
            t.SetParent(_root.transform, false);
            t.localPosition = new Vector3(0f, 0f, BumpCueScheduler.IndicatorDistance);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = false;

            _meshes[(int)icon] = mesh;
            _transforms[(int)icon] = t;
            _renderers[(int)icon] = renderer;
        }

        static AudioClip MakeClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, BumpCueTones.SampleRate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
