using System;
using System.Globalization;
using Atlas.Input;
using Atlas.World;

namespace Atlas.Blades.Playground
{
    /// <summary>What one probe measured, degrees. Azimuths are in the room's axes (see <see cref="ProbeGeometry"/>).</summary>
    public struct ProbeResult
    {
        /// <summary>Where the head pointed when the walker pressed A or the dwell finished.</summary>
        public double RayAz;
        /// <summary>Where the kernel says the start is: along the geodesic.</summary>
        public double PredGeodesic;
        /// <summary>Where the start is by the walker's real path: the straight line on the floor.</summary>
        public double PredEuclid;
        /// <summary>Angle between the ray and the geodesic prediction, 0 to 180.</summary>
        public double ErrGeo;
        /// <summary>Angle between the ray and the Euclidean prediction, 0 to 180.</summary>
        public double ErrEuc;
    }

    /// <summary>
    /// The pointing probe (task P2-C17, Master Plan section 10.1.2). After the third leg of the walk at q = 4 (flat,
    /// S0) and again at q = 5 (S2) the director calls <see cref="Arm"/>; the walker looks at where they started
    /// and presses A (<see cref="AtlasAction.Confirm"/>), or dwells on the straight-ahead target, which is the
    /// gaze twin; the head ray at that moment is logged beside the geodesic and the Euclidean prediction as
    /// <c>probe {ray_az, pred_geodesic, pred_euclid, err_geo, err_euc}</c> (Engineering Spec section 13.1).
    /// <para>The Euclidean prediction comes from the floor alone: the bearing from the head's floor point to the
    /// floor point where the walk started. The geodesic one comes from <see cref="IHomeBearing"/>. In S0 the two
    /// agree; in S2 the difference is the holonomy the walk collected. Engine-free; the caller supplies the head.</para>
    /// </summary>
    public sealed class PointingProbe
    {
        public const string ProbeEvent = "probe";
        public const string PromptText = "Look at where you started and press A.";
        /// <summary>The gaze twin: the head ray held within this many degrees for the dwell time counts as a press. Proposed.</summary>
        public const double SteadyDeg = 3.0;
        /// <summary>Gaze dwell of the whole app: 700 ms (Engineering Spec section 4.5).</summary>
        public const double DwellSeconds = 0.7;

        readonly IHomeBearing _home;
        readonly IEventLog _log;
        readonly PromptQueue _prompts;
        double _startX, _startZ;
        bool _started, _armed;
        bool _steadyOn;
        double _steadyYaw, _steadyFor;

        /// <param name="home">The kernel's bearing of the start.</param>
        /// <param name="log">The event log; may be null in a sandbox.</param>
        /// <param name="prompts">Shows the instruction while armed; may be null.</param>
        public PointingProbe(IHomeBearing home, IEventLog log, PromptQueue prompts = null)
        {
            if (home == null) throw new ArgumentNullException("home");
            _home = home;
            _log = log;
            _prompts = prompts;
        }

        /// <summary>True while the walker is asked to point. The director and the action map leave Confirm to the probe meanwhile.</summary>
        public bool IsArmed { get { return _armed; } }

        /// <summary>Probes taken since construction.</summary>
        public int Taken { get; private set; }

        /// <summary>The last result; meaningful when <see cref="Taken"/> is above 0.</summary>
        public ProbeResult Last { get; private set; }

        /// <summary>The walk starts: remembers the floor point of the start flag and asks the kernel to do the same.</summary>
        public void Begin(double startFloorX, double startFloorZ)
        {
            _startX = startFloorX;
            _startZ = startFloorZ;
            _started = true;
            _armed = false;
            _home.MarkStart();
        }

        /// <summary>The third leg is done: asks the walker to look at the start and press A.</summary>
        public bool Arm(double nowS)
        {
            if (!_started) return false;
            _armed = true;
            _steadyOn = false;
            if (_prompts != null) _prompts.Enqueue(PromptKind.Probe, PromptText, nowS);
            return true;
        }

        /// <summary>
        /// The gaze twin of the press, once per frame while armed: when the head ray stays within
        /// <see cref="SteadyDeg"/> for <see cref="DwellSeconds"/> the measurement is taken as if A was pressed. The
        /// reticle is always on the head ray here, so the dwell is on holding still, not on a target.
        /// </summary>
        public bool TickDwell(double headFloorX, double headFloorZ, double forwardX, double forwardZ, double deltaS, double nowS)
        {
            if (!_armed) return false;
            if (forwardX * forwardX + forwardZ * forwardZ < 1e-6)
            {
                _steadyOn = false;
                return false;
            }
            double yaw = ProbeGeometry.YawDeg(forwardX, forwardZ);
            if (!_steadyOn || ProbeGeometry.AngleBetweenDeg(yaw, _steadyYaw) > SteadyDeg)
            {
                _steadyOn = true;
                _steadyYaw = yaw;
                _steadyFor = 0.0;
                return false;
            }
            _steadyFor += deltaS;
            if (_steadyFor < DwellSeconds) return false;
            _steadyFor = 0.0;
            return Capture(headFloorX, headFloorZ, forwardX, forwardZ, nowS);
        }

        /// <summary>Gives up without a measurement: the walker left the walk, or the headset came off.</summary>
        public void Disarm(double nowS)
        {
            if (!_armed) return;
            _armed = false;
            if (_prompts != null) _prompts.Clear(PromptKind.Probe, nowS);
        }

        /// <summary>
        /// The walker pressed A or the dwell finished. <paramref name="headFloorX"/> and <paramref name="headFloorZ"/>
        /// are the floor point under the head and <paramref name="forwardX"/>, <paramref name="forwardZ"/> the head's
        /// forward vector, all in the room frame. Returns false, and stays armed, when the kernel has no bearing yet
        /// or the head looks straight up or down.
        /// </summary>
        public bool Capture(double headFloorX, double headFloorZ, double forwardX, double forwardZ, double nowS)
        {
            if (!_armed) return false;
            if (forwardX * forwardX + forwardZ * forwardZ < 1e-6) return false;
            double geoAz, geoDistance;
            if (!_home.TryGet(out geoAz, out geoDistance)) return false;

            var r = new ProbeResult
            {
                RayAz = Round1(ProbeGeometry.YawDeg(forwardX, forwardZ)),
                PredGeodesic = Round1(geoAz),
                PredEuclid = Round1(ProbeGeometry.AzimuthDeg(_startX - headFloorX, _startZ - headFloorZ))
            };
            r.ErrGeo = Round1(ProbeGeometry.AngleBetweenDeg(r.RayAz, r.PredGeodesic));
            r.ErrEuc = Round1(ProbeGeometry.AngleBetweenDeg(r.RayAz, r.PredEuclid));
            Last = r;
            Taken++;
            _armed = false;
            if (_prompts != null) _prompts.Clear(PromptKind.Probe, nowS);
            if (_log != null) _log.LogJson(ProbeEvent, ToJson(r));
            return true;
        }

        /// <summary>The data object of the <c>probe</c> event.</summary>
        public static string ToJson(ProbeResult r)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return "{\"ray_az\":" + r.RayAz.ToString("0.0", c) +
                   ",\"pred_geodesic\":" + r.PredGeodesic.ToString("0.0", c) +
                   ",\"pred_euclid\":" + r.PredEuclid.ToString("0.0", c) +
                   ",\"err_geo\":" + r.ErrGeo.ToString("0.0", c) +
                   ",\"err_euc\":" + r.ErrEuc.ToString("0.0", c) + "}";
        }

        static double Round1(double v) { return Math.Round(v, 1); }
    }
}
