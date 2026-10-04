using System;
using Atlas.Link;
using Atlas.World;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// What <see cref="TelemetryInstaller"/> made: the link, pairing, the event log, the perf probe and the HUD
    /// of one running app. Published through <c>AtlasServices</c> for onboarding, the menu and the Spectator.
    /// </summary>
    public sealed class AtlasTelemetry
    {
        bool _hooked;
        bool _shutDown;
        internal Application.LogCallback LogTap;
        internal Action<string> DeepLinkTap;
        internal Action<RoomModel> RoomTap;
        internal Action<QualityTier> TierTap;
        internal ISpaceHost SpaceHost;
        internal IQualityState QualityState;

        internal AtlasTelemetry() { }

        public ParamRegistry Params { get; internal set; }
        /// <summary>The link client. Its owner is <see cref="Pump"/>: read it, do not poll it.</summary>
        public LinkClient Link { get; internal set; }
        /// <summary>The link driver (step 8 of the frame).</summary>
        public LinkPump Pump { get; internal set; }
        public PairingController Pairing { get; internal set; }
        public EventLog Events { get; internal set; }
        public AtlasPerfProbe Probe { get; internal set; }
        public AtlasHud Hud { get; internal set; }
        /// <summary>The writer of the phone's log files; null in the Spectator.</summary>
        public LogFileWriter Files { get; internal set; }
        public string SessionId { get; internal set; }

        /// <summary>The link state, for code that needs nothing else.</summary>
        public LinkState LinkState { get { return Pump.State; } }

        /// <summary>
        /// Adds lane D's hooks to the frame loop: the state sink of step 8a and, in this order at step 8b, the
        /// probe (samples counters), the link driver (drains parameters and commands) and the HUD. The installer
        /// calls it when the loop is already published; otherwise the composition root calls it once, before
        /// <c>AtlasServices.MarkBooted</c>.
        /// </summary>
        public void AddHooks(IFrameLoop loop)
        {
            if (loop == null) throw new ArgumentNullException("loop");
            if (_hooked) return;
            _hooked = true;
            loop.SetStateSink(Pump);
            loop.AddAfterFrame(Probe);
            loop.AddAfterFrame(Pump);
            loop.AddAfterFrame(Hud);
        }

        /// <summary>True once <see cref="AddHooks"/> ran.</summary>
        public bool Hooked { get { return _hooked; } }

        /// <summary>
        /// A new session: a new id, new files, row numbers from 0 and the id announced to the Station.
        /// Onboarding calls it when a new walker starts.
        /// </summary>
        public string BeginSession()
        {
            SessionId = Atlas.Telemetry.SessionId.New();
            if (Files != null) Files.Open(SessionId);
            Probe.ResetSession();
            Pump.ResetSession();
            // A Station that is connected is told with a second Hello; the handshake is not repeated. It goes
            // out before the session event, so that the Hub files the event under the new session.
            Link.SetSession(SessionId);
            Events.Log("session", "sessionId", SessionId);
            return SessionId;
        }

        internal void OnPause()
        {
            if (Files != null) Files.Flush();
        }

        internal void OnResume()
        {
            // iOS closes the socket's peer while the app is in the background: do not wait out the back-off.
            if (Pump.IsStarted && Pump.State == LinkState.Offline) Pump.ReconnectNow();
        }

        /// <summary>Says goodbye to the Station, writes what is waiting and closes the files. Safe to call twice.</summary>
        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            if (LogTap != null) Application.logMessageReceived -= LogTap;
            if (DeepLinkTap != null) Application.deepLinkActivated -= DeepLinkTap;
            if (SpaceHost != null && RoomTap != null) SpaceHost.RoomChanged -= RoomTap;
            if (QualityState != null && TierTap != null) QualityState.TierChanged -= TierTap;
            Pump.Stop("quit");
            Probe.Dispose();
            if (Files != null) Files.Dispose();
        }
    }
}
