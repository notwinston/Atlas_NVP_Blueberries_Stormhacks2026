using System.Globalization;
using Atlas.Link;

namespace Atlas.App
{
    /// <summary>
    /// When the phone sends its view to the Station's View page (<c>OperatorCmd view</c>). The Hub repeats
    /// <c>view on</c> every second while anyone watches; with no repeat for <see cref="OperatorCommand.ViewHoldSeconds"/>
    /// the stream stops by itself, and the Hub's answer to a frame stops it at once when nobody is watching.
    /// One frame is in flight at a time (read back, encoded, posted), so a slow link lowers the rate instead of
    /// queueing frames. Engine-free; main thread only.
    /// </summary>
    public sealed class ViewStreamCore
    {
        public const int MinFps = 1, MaxFps = 20, DefaultFps = 10;

        float _left;
        double _next = double.NegativeInfinity;
        int _fps = DefaultFps;

        /// <summary>The Hub asked for the view and has not stopped asking.</summary>
        public bool Active { get { return _left > 0f; } }
        /// <summary>A frame is being read back, encoded or posted.</summary>
        public bool Busy { get; private set; }
        /// <summary>The Hub's HTTP port from its last request.</summary>
        public int Port { get; private set; } = OperatorCommand.DefaultHttpPort;
        /// <summary>Frames finished since start, posted or not.</summary>
        public int Frames { get; private set; }

        /// <summary>Frames per second asked for, clamped to MinFps..MaxFps.</summary>
        public int Fps
        {
            get { return _fps; }
            set { _fps = value < MinFps ? MinFps : (value > MaxFps ? MaxFps : value); }
        }

        /// <summary>The Hub's view command.</summary>
        public void Request(bool on, int port)
        {
            if (!on)
            {
                _left = 0f;
                return;
            }
            _left = OperatorCommand.ViewHoldSeconds;
            Port = port > 0 && port <= 65535 ? port : OperatorCommand.DefaultHttpPort;
        }

        /// <summary>The Hub's answer to a frame: nobody watching stops the stream until the next request.</summary>
        public void Watching(bool watching)
        {
            if (!watching) _left = 0f;
        }

        /// <summary>
        /// Once per frame with the app's clock: counts the hold down and says whether to capture a frame now.
        /// After a true the caller owns the frame until <see cref="Done"/>.
        /// </summary>
        public bool Tick(double time, float deltaTime)
        {
            if (_left > 0f && deltaTime > 0f) _left -= deltaTime;
            if (_left <= 0f || Busy || time < _next) return false;
            _next = time + 1.0 / _fps;
            Busy = true;
            return true;
        }

        /// <summary>The frame taken by the last true <see cref="Tick"/> is finished, sent or failed.</summary>
        public void Done()
        {
            if (!Busy) return;
            Busy = false;
            Frames++;
        }

        /// <summary>Where a frame goes: the Hub's HTTP port on the link's host. mode tells the page what it shows.</summary>
        public static string FrameUrl(string host, int port, bool headset)
        {
            return "http://" + host + ":" + port.ToString(CultureInfo.InvariantCulture) + "/api/view/frame?mode=" +
                   (headset ? "headset" : "hand");
        }

        /// <summary>The height for a frame width that keeps the source's shape, rounded to an even number of pixels.</summary>
        public static int HeightFor(int width, int sourceWidth, int sourceHeight)
        {
            if (width <= 0 || sourceWidth <= 0 || sourceHeight <= 0) return 0;
            int h = (int)System.Math.Round((double)width * sourceHeight / sourceWidth);
            h &= ~1;
            return h < 2 ? 2 : h;
        }

        /// <summary>The <c>watching</c> member of the Hub's answer to a frame; true when the answer cannot be read.</summary>
        public static bool ReadWatching(string json)
        {
            if (string.IsNullOrEmpty(json)) return true;
            JsonValue value;
            string error;
            if (!AtlasJson.TryParse(json, out value, out error) || !value.IsObject) return true;
            return value.GetBool("watching", true);
        }
    }
}
