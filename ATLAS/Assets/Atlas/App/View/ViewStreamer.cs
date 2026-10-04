using System;
using System.Collections;
using System.Threading;
using Atlas.Link;
using Atlas.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace Atlas.App
{
    /// <summary>
    /// Sends what the phone shows to the Station's View page while the Hub asks for it (<see cref="ViewStreamCore"/>):
    /// in the headset the left eye before the lens pass, HUD included; in the hand the screen. At the end of a
    /// frame the source is scaled down on the GPU, read back without a stall, encoded to JPEG on a worker thread
    /// and posted to the Hub's HTTP port. Off and allocation-free while nobody watches; the textures are let go
    /// when the stream stops.
    /// </summary>
    public sealed class ViewStreamer : MonoBehaviour
    {
        public const string ParamFps = "view.fps";
        public const string ParamWidth = "view.width";
        public const string ParamQuality = "view.quality";
        public const string ParamFlipHeadset = "view.flipHeadset";
        public const string ParamFlipHand = "view.flipHand";

        const int PostTimeoutSeconds = 2;

        readonly ViewStreamCore _core = new ViewStreamCore();
        StereoRig _rig;
        Func<string> _host;
        int _width = 640;
        int _quality = 60;
        bool _flipHeadset, _flipHand;

        RenderTexture _screen, _small;
        byte[] _pixels;
        int _frameW, _frameH;
        bool _frameHeadset;
        byte[] _encoded;               // written by the encoder thread, taken on the main thread
        UnityWebRequest _post;
        Coroutine _loop;
        Action<AsyncGPUReadbackRequest> _onReadback;

        /// <summary>The engine-free state: requests, rate, frame in flight.</summary>
        public ViewStreamCore Core { get { return _core; } }

        /// <summary>A disabled-until-asked streamer under parent. host gives the Hub's address, or null while there is no link.</summary>
        public static ViewStreamer Create(Transform parent, StereoRig rig, Func<string> host)
        {
            var go = new GameObject("Atlas View Streamer");
            go.transform.SetParent(parent, false);
            ViewStreamer streamer = go.AddComponent<ViewStreamer>();
            streamer._rig = rig;
            streamer._host = host;
            return streamer;
        }

        /// <summary>view.fps, view.width, view.quality and the two flips: settings, not switches. The Hub turns the stream on.</summary>
        public void DeclareParams(ParamRegistry registry)
        {
            registry.DeclareInt(ParamFps, "view", ViewStreamCore.MinFps, ViewStreamCore.MaxFps, ViewStreamCore.DefaultFps, v => _core.Fps = v);
            registry.DeclareInt(ParamWidth, "view", 320, 1280, 640, v => _width = v & ~1);
            registry.DeclareInt(ParamQuality, "view", 30, 90, 60, v => _quality = v);
            // Which way up a read-back image comes depends on the graphics API: these turn a picture that
            // arrives upside down the right way up without a new build.
            registry.DeclareBool(ParamFlipHeadset, "view", false, v => _flipHeadset = v);
            registry.DeclareBool(ParamFlipHand, "view", false, v => _flipHand = v);
        }

        /// <summary>The Hub's view command.</summary>
        public void Request(bool on, int port)
        {
            _core.Request(on, port);
            if (on && _loop == null && isActiveAndEnabled) _loop = StartCoroutine(EndOfFrameLoop());
        }

        IEnumerator EndOfFrameLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (_core.Tick(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime))
                {
                    if (!Capture()) _core.Done();
                }
                if (!_core.Active && !_core.Busy)
                {
                    Release();
                    _loop = null;
                    yield break;
                }
            }
        }

        void Update()
        {
            // The encoded frame goes out from the main thread; the answer says whether anyone still watches.
            if (_post == null)
            {
                byte[] jpeg = Interlocked.Exchange(ref _encoded, null);
                if (jpeg != null) Post(jpeg);
                return;
            }
            if (!_post.isDone) return;
            if (_post.result == UnityWebRequest.Result.Success)
                _core.Watching(ViewStreamCore.ReadWatching(_post.downloadHandler != null ? _post.downloadHandler.text : null));
            _post.Dispose();
            _post = null;
            _core.Done();
        }

        bool Capture()
        {
            if (_rig == null) return false;
            bool headset = _rig.Mode == DisplayMode.Headset;
            Texture source;
            bool flip;
            if (headset)
            {
                Camera eye = _rig.GetEyeCamera(Eye.Left);
                source = eye != null ? eye.targetTexture : null;
                flip = _flipHeadset;
            }
            else
            {
                if (_screen == null || _screen.width != Screen.width || _screen.height != Screen.height)
                {
                    ReleaseTexture(ref _screen);
                    _screen = new RenderTexture(Screen.width, Screen.height, 0, GraphicsFormat.R8G8B8A8_SRGB) { name = "Atlas View Screen" };
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_screen);
                source = _screen;
                // The screen arrives upside down where texture coordinates start at the top (Metal).
                flip = SystemInfo.graphicsUVStartsAtTop != _flipHand;
            }
            if (source == null || source.width <= 0 || source.height <= 0) return false;

            int w = Math.Min(_width, source.width) & ~1;
            int h = ViewStreamCore.HeightFor(w, source.width, source.height);
            if (w < 2 || h < 2) return false;
            if (_small == null || _small.width != w || _small.height != h)
            {
                ReleaseTexture(ref _small);
                _small = new RenderTexture(w, h, 0, GraphicsFormat.R8G8B8A8_SRGB) { name = "Atlas View Frame" };
            }
            if (flip) Graphics.Blit(source, _small, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(source, _small);

            _frameW = w;
            _frameH = h;
            _frameHeadset = headset;
            if (_onReadback == null) _onReadback = OnReadback;
            AsyncGPUReadback.Request(_small, 0, TextureFormat.RGBA32, _onReadback);
            return true;
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            if (request.hasError || _frameW <= 0)
            {
                _core.Done();
                return;
            }
            int length = _frameW * _frameH * 4;
            if (_pixels == null || _pixels.Length != length) _pixels = new byte[length];
            request.GetData<byte>().CopyTo(_pixels);
            byte[] pixels = _pixels;
            int w = _frameW, h = _frameH, quality = _quality;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] jpeg = null;
                try
                {
                    jpeg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h, (uint)(w * 4), quality);
                }
                catch (Exception)
                {
                    jpeg = null;
                }
                // A failed encode still ends the frame: an empty array is dropped by Post.
                Interlocked.Exchange(ref _encoded, jpeg ?? new byte[0]);
            });
        }

        void Post(byte[] jpeg)
        {
            string host = _host != null ? _host() : null;
            if (jpeg.Length == 0 || string.IsNullOrEmpty(host))
            {
                _core.Done();
                return;
            }
            var post = new UnityWebRequest(ViewStreamCore.FrameUrl(host, _core.Port, _frameHeadset), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(jpeg) { contentType = "image/jpeg" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = PostTimeoutSeconds
            };
            post.SendWebRequest();
            _post = post;
        }

        void Release()
        {
            ReleaseTexture(ref _screen);
            ReleaseTexture(ref _small);
            _pixels = null;
        }

        static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        void OnDisable()
        {
            _core.Request(false, _core.Port);
            if (_post != null)
            {
                _post.Abort();
                _post.Dispose();
                _post = null;
            }
            _loop = null;
            Release();
        }
    }
}
