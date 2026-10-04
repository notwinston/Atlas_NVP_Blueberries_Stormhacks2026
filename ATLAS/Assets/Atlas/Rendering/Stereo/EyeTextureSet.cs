using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    /// <summary>What the quality ladder needs from the eye textures. Lets the ladder run with no engine.</summary>
    public interface IEyeTextures
    {
        /// <summary>Render scale of the active set: 0.8, 0.7 or 0.6.</summary>
        float RenderScale { get; }
        /// <summary>MSAA sample count of the active set: 4, 2 or 1.</summary>
        int Msaa { get; }
        /// <summary>Makes the set for (renderScale, msaa) the active one.</summary>
        void Select(float renderScale, int msaa);
    }

    /// <summary>
    /// The eye render textures (Master Plan sections 8.2 and 8.6, task P1-A05). One texture per eye; four sets
    /// (0.8 with 4x MSAA, 0.7 with 4x, 0.6 with 2x, 0.6 with none) are allocated once at start and the thermal
    /// ladder swaps between them without allocating. Multisampled colour and depth are memoryless, so only the
    /// resolved colour is kept. Nobody else assigns Camera.targetTexture of the eye cameras, except the rig while
    /// its eye-target override is set (StereoRig.OverrideEyeTargets).
    /// </summary>
    public sealed class EyeTextureSet : IEyeTextures, IDisposable
    {
        const int CustomSet = EyeTextureLayout.SetCount;   // one extra slot for a pair no rung uses

        readonly RenderTexture[] _left = new RenderTexture[EyeTextureLayout.SetCount + 1];
        readonly RenderTexture[] _right = new RenderTexture[EyeTextureLayout.SetCount + 1];
        readonly RTHandle[] _leftHandles = new RTHandle[EyeTextureLayout.SetCount + 1];
        readonly RTHandle[] _rightHandles = new RTHandle[EyeTextureLayout.SetCount + 1];
        readonly int _halfWidth;
        readonly int _halfHeight;

        Camera _leftCamera;
        Camera _rightCamera;
        int _active;
        float _customScale;
        int _customMsaa;

        /// <summary>
        /// Allocates the four sets for a screen of the given pixel size (the longer side is the width).
        /// Pass Screen.width and Screen.height; a size that is not positive gives the 1398 x 1290 half-screen.
        /// </summary>
        public EyeTextureSet(int screenWidth, int screenHeight)
        {
            EyeTextureLayout.HalfScreen(screenWidth, screenHeight, out _halfWidth, out _halfHeight);
            for (int i = 0; i < EyeTextureLayout.SetCount; i++)
                CreateSet(i, EyeTextureLayout.SetScale(i), EyeTextureLayout.SetMsaa(i));
            _active = 0;
        }

        /// <summary>Width of the half-screen an eye covers. Pixels.</summary>
        public int HalfScreenWidth => _halfWidth;
        /// <summary>Height of the half-screen an eye covers. Pixels.</summary>
        public int HalfScreenHeight => _halfHeight;

        public float RenderScale => _active == CustomSet ? _customScale : EyeTextureLayout.SetScale(_active);
        public int Msaa => _active == CustomSet ? _customMsaa : EyeTextureLayout.SetMsaa(_active);

        /// <summary>Left eye texture of the active set.</summary>
        public RenderTexture Left => _left[_active];
        /// <summary>Right eye texture of the active set.</summary>
        public RenderTexture Right => _right[_active];
        /// <summary>The active left texture as the handle the lens pass imports. Allocated with the set.</summary>
        public RTHandle LeftHandle => _leftHandles[_active];
        /// <summary>The active right texture as the handle the lens pass imports. Allocated with the set.</summary>
        public RTHandle RightHandle => _rightHandles[_active];

        /// <summary>Raised after a swap, with the new textures in place.</summary>
        public event Action Changed;

        /// <summary>Gives the eye cameras their targets: now, and again after every swap.</summary>
        public void Bind(Camera leftCamera, Camera rightCamera)
        {
            _leftCamera = leftCamera;
            _rightCamera = rightCamera;
            AssignTargets();
        }

        /// <summary>
        /// Makes the set for (renderScale, msaa) the target of the eye cameras and the source of the lens blit.
        /// renderScale snaps to 0.6, 0.7 or 0.8 and msaa to 1, 2 or 4. A pair that a ladder rung uses is swapped
        /// in with no allocation. Any other pair (an MSAA change outside the ladder) re-creates the one custom
        /// pair of textures, which allocates.
        /// </summary>
        public void Select(float renderScale, int msaa)
        {
            float scale = EyeTextureLayout.SnapScale(renderScale);
            int samples = EyeTextureLayout.SnapMsaa(msaa);
            int set = EyeTextureLayout.FindSet(scale, samples);
            if (set < 0)
            {
                set = CustomSet;
                if (_left[CustomSet] == null || _customScale != scale || _customMsaa != samples)
                {
                    // Point the cameras away before the old custom textures go.
                    if (_active == CustomSet)
                    {
                        _active = 0;
                        AssignTargets();
                    }
                    ReleaseSet(CustomSet);
                    CreateSet(CustomSet, scale, samples);
                    _customScale = scale;
                    _customMsaa = samples;
                    _active = -1;   // force the swap below
                }
            }

            if (set == _active)
                return;
            _active = set;
            AssignTargets();
            Changed?.Invoke();
        }

        /// <summary>Releases every texture. The eye cameras lose their targets.</summary>
        public void Dispose()
        {
            if (_leftCamera != null) _leftCamera.targetTexture = null;
            if (_rightCamera != null) _rightCamera.targetTexture = null;
            for (int i = 0; i <= CustomSet; i++)
                ReleaseSet(i);
        }

        void AssignTargets()
        {
            if (_leftCamera != null) _leftCamera.targetTexture = _left[_active];
            if (_rightCamera != null) _rightCamera.targetTexture = _right[_active];
        }

        void CreateSet(int index, float scale, int msaa)
        {
            EyeTextureLayout.Size(_halfWidth, _halfHeight, scale, out int width, out int height);
            _left[index] = Create(width, height, msaa, "Atlas Eye Left " + scale.ToString("0.0") + " x" + msaa);
            _right[index] = Create(width, height, msaa, "Atlas Eye Right " + scale.ToString("0.0") + " x" + msaa);
            _leftHandles[index] = RTHandles.Alloc(_left[index]);
            _rightHandles[index] = RTHandles.Alloc(_right[index]);
        }

        void ReleaseSet(int index)
        {
            if (_leftHandles[index] != null) RTHandles.Release(_leftHandles[index]);
            if (_rightHandles[index] != null) RTHandles.Release(_rightHandles[index]);
            _leftHandles[index] = null;
            _rightHandles[index] = null;
            Destroy(ref _left[index]);
            Destroy(ref _right[index]);
        }

        static void Destroy(ref RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            CoreUtils.Destroy(texture);
            texture = null;
        }

        static RenderTexture Create(int width, int height, int msaa, string name)
        {
            // The LDR default is an sRGB format in a linear project; the depth format carries the stencil the
            // portals need. Both are what URP uses for its own camera targets.
            var descriptor = new RenderTextureDescriptor(width, height,
                SystemInfo.GetGraphicsFormat(DefaultFormat.LDR), SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil))
            {
                msaaSamples = msaa,
                useMipMap = false,
                autoGenerateMips = false,
                bindMS = false,
                memoryless = Memoryless(msaa),
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.Create();
            return texture;
        }

        // Multisampled colour and depth never leave the tile; only the resolved colour is stored (section 8.2).
        // A set with no MSAA has no multisampled surface, so only its depth is memoryless.
        static RenderTextureMemoryless Memoryless(int msaa)
        {
            if (!SystemInfo.supportsMemorylessTextures)
                return RenderTextureMemoryless.None;
            return msaa > 1
                ? RenderTextureMemoryless.MSAA | RenderTextureMemoryless.Depth
                : RenderTextureMemoryless.Depth;
        }
    }
}
