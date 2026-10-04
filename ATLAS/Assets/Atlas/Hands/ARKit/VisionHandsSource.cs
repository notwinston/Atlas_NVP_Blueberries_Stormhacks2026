using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Atlas.Hands.ARKit
{
    /// <summary>
    /// The phone's hand detector (documents/ATLAS_Hands_Design.md sections 4.2 and 4.5, Appendix C): Apple
    /// Vision's hand-pose request on the frames of the running ARSession, through
    /// Assets/Plugins/iOS/AtlasHands.swift. Registers its factory before the first scene loads; nothing
    /// references this assembly (contract README F1). In the Editor every call does nothing and
    /// <see cref="Available"/> is false (contracts F9).
    /// <para>The plugin is one detector for the whole app, so one source runs at a time: make a new one with
    /// HandsPlatform.Create when the AR session is made anew, and stop the old one first.</para>
    /// </summary>
    public sealed class VisionHandsSource : IHandsSource
    {
        /// <summary>
        /// <see cref="Fault"/> when the plugin in the build does not write the bytes of
        /// <see cref="HandsDetection"/> (another record size or version): the source refuses to run. The
        /// plugin's own codes are -1 not started, -2 no session, -3 no camera frame for 2 s, -4 the last 30
        /// requests failed (Appendix C).
        /// </summary>
        internal const int FaultLayout = -5;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStartUp()
        {
            HandsPlatform.Register(nativeSession => new VisionHandsSource(nativeSession));
        }

        /// <summary>"vision".</summary>
        public string Name { get { return "vision"; } }

#if UNITY_IOS && !UNITY_EDITOR
        // nativeSession is passed as it is: the plugin reads the struct behind it (an int version, then the ARSession*).
        [DllImport("__Internal")] static extern int atlas_hands_start(IntPtr nativeSession, int hz);
        [DllImport("__Internal")] static extern void atlas_hands_set_rate(int hz);
        [DllImport("__Internal")] static extern int atlas_hands_tick();
        [DllImport("__Internal")] static extern unsafe int atlas_hands_poll(HandsDetection* result);
        [DllImport("__Internal")] static extern int atlas_hands_result_size();
        [DllImport("__Internal")] static extern void atlas_hands_stop();

        static bool s_layoutLogged;

        readonly IntPtr _session;
        bool _started;
        int _rate;
        int _fault;

        /// <summary>nativeSession is XRSessionSubsystem.nativePtr of the running AR session, or IntPtr.Zero when there is none.</summary>
        public VisionHandsSource(IntPtr nativeSession)
        {
            _session = nativeSession;
        }

        /// <summary>True on the phone when there is an AR session to read frames from.</summary>
        public bool Available { get { return _session != IntPtr.Zero; } }

        /// <summary>
        /// 0 while healthy; otherwise the last negative code of the plugin, or <see cref="FaultLayout"/>. The
        /// plugin's errors hold only while their cause does, so this returns to 0 by itself when frames or
        /// results come back. It keeps its value through <see cref="Stop"/> and is cleared by the next start
        /// that succeeds.
        /// </summary>
        public int Fault { get { return _fault; } }

        /// <summary>
        /// Detections per second: 30, 15 or 5; 0 idles the detector. Starts the plugin when it is stopped
        /// and hz is above 0; a start that fails leaves its code in <see cref="Fault"/> and is tried again
        /// by the next call. Costs nothing when the rate is the one already set.
        /// </summary>
        public unsafe void SetRate(int hz)
        {
            if (hz < 0) hz = 0;
            if (_started)
            {
                if (hz == _rate) return;
                _rate = hz;
                atlas_hands_set_rate(hz);
                return;
            }
            _rate = hz;
            if (hz == 0) return;

            // The plugin writes its record straight into the caller's HandsDetection: both sides must agree on
            // every byte of it. A plugin left over from an older build in an appended Xcode project would not.
            int pluginSize = atlas_hands_result_size();
            if (pluginSize != HandsDetection.NativeSize || sizeof(HandsDetection) != HandsDetection.NativeSize)
            {
                RefuseLayout(pluginSize);
                return;
            }
            int code = atlas_hands_start(_session, hz);
            _started = code == 0;
            _fault = code == 0 ? 0 : (code < 0 ? code : -1);
        }

        /// <summary>
        /// Once per frame. Lets the plugin start a detection on the newest camera frame when one is due, then
        /// takes the newest finished result straight into detection: true when one newer than the last was
        /// copied, with <see cref="HandsDetection.CameraToSession"/> turned from ARKit's convention into
        /// Unity's. Never waits and allocates nothing. time is not used: the plugin keeps its rate on the
        /// device's own clock, the one the camera frames are stamped with. When it returns false, detection
        /// holds nothing to read.
        /// </summary>
        public unsafe bool TryRead(double time, ref HandsDetection detection)
        {
            if (!_started) return false;
            int ticked = atlas_hands_tick();
            int polled;
            fixed (HandsDetection* result = &detection) polled = atlas_hands_poll(result);
            if (polled == 1 && !HandsNativeMath.IsRecord(in detection))
            {
                RefuseLayout(-1);
                return false;
            }
            _fault = HandsNativeMath.Fault(ticked, polled);
            if (polled != 1) return false;
            detection.CameraToSession = HandsNativeMath.ArKitToUnity(detection.CameraToSession);
            return true;
        }

        /// <summary>Stops the plugin at once: a request still running finishes by itself and its result is dropped.</summary>
        public void Stop()
        {
            if (!_started) return;
            _started = false;
            _rate = 0;
            atlas_hands_stop();
        }

        // pluginSize is the plugin's record size, or -1 when a result it handed over is not a record of this version.
        // Said once: the caller may ask again on every frame.
        unsafe void RefuseLayout(int pluginSize)
        {
            if (_started)
            {
                _started = false;
                atlas_hands_stop();
            }
            _fault = FaultLayout;
            if (s_layoutLogged) return;
            s_layoutLogged = true;
            Debug.LogError("Atlas: hands detector off: AtlasHands.swift in this build is not the one this code was written against (" +
                           (pluginSize < 0
                               ? "a result is not version " + HandsDetection.CurrentVersion + " of the record"
                               : "its record is " + pluginSize + " bytes, HandsDetection is " + sizeof(HandsDetection)) + ")");
        }
#else
        /// <summary>nativeSession is XRSessionSubsystem.nativePtr of the running AR session, or IntPtr.Zero when there is none.</summary>
        public VisionHandsSource(IntPtr nativeSession)
        {
        }

        /// <summary>False: the plugin exists on the phone only.</summary>
        public bool Available { get { return false; } }

        /// <summary>0: nothing runs here.</summary>
        public int Fault { get { return 0; } }

        /// <summary>Does nothing off the phone.</summary>
        public void SetRate(int hz)
        {
        }

        /// <summary>False: there is never a result off the phone.</summary>
        public bool TryRead(double time, ref HandsDetection detection)
        {
            return false;
        }

        /// <summary>Does nothing off the phone.</summary>
        public void Stop()
        {
        }
#endif
    }
}
