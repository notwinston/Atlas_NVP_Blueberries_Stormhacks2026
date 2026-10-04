using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Safety
{
    /// <summary>
    /// The warning cue: indicator, sound and haptic (Master Plan sections 4.10 and 7.3.2, task P1-C14).
    /// One local entry point, <see cref="Fire"/>, used by Bump Guard every frame and by the operator's STOP
    /// command. Main thread only. The decisions are made by <see cref="BumpCueScheduler"/>; this class carries
    /// them to the view and to the haptic channels in the same call, so a cue starts in the frame it is fired.
    /// It never changes the world, reads no link and no parameter, and allocates nothing after Initialise.
    /// </summary>
    public static class BumpCue
    {
        const int MaxChannels = 4;

        static readonly BumpCueScheduler s_core = new BumpCueScheduler();
        static readonly IHaptics[] s_channels = new IHaptics[MaxChannels];
        static int s_channelCount;
        static IBumpCueView s_view;

        /// <summary>
        /// Builds the cue under hud, the head-locked parent, on hud's layer. The cue draws with its own shader;
        /// material is used only if that shader is missing, and may be null. haptics may be null. Called once,
        /// by SafetyInstaller. A null hud gives a cue with haptics only.
        /// </summary>
        public static void Initialise(Transform hud, Material material, IHaptics haptics)
        {
            Initialise(hud != null ? new BumpCueView(hud, material) : null, haptics);
        }

        /// <summary>The same with a view made elsewhere: tests pass a recording view, or none.</summary>
        public static void Initialise(IBumpCueView view, IHaptics haptics)
        {
            Shutdown();
            s_view = view;
            AddHaptics(haptics);
        }

        /// <summary>A further haptic channel that follows the same pulses and pattern (the gamepad's rumble).</summary>
        public static void AddHaptics(IHaptics haptics)
        {
            if (haptics == null || s_channelCount == MaxChannels) return;
            for (int i = 0; i < s_channelCount; i++)
                if (ReferenceEquals(s_channels[i], haptics)) return;
            s_channels[s_channelCount++] = haptics;
        }

        /// <summary>
        /// Sets the level one source asks for. Guard: Bump Guard calls this every frame with its level, Clear
        /// included. Operator: the level is held for <see cref="BumpConstants.OperatorHoldSeconds"/>, then drops
        /// to Clear. The cue shows the higher of the two; on a tie the Guard's direction is used. direction is in
        /// head space (x right, y ahead); zero means "no side". Indicator, sound and haptic start in this call.
        /// </summary>
        public static void Fire(BumpLevel level, float2 direction, CueSource source = CueSource.Guard)
        {
            s_core.Fire(level, direction, source);
            Apply();
        }

        /// <summary>Shows or hides the "rescan" icon with the WARN haptic. Bump Guard calls it every frame.</summary>
        public static void Rescan(bool advised)
        {
            s_core.Rescan(advised);
            Apply();
        }

        /// <summary>Advances pulses and the indicator. Bump Guard calls it once per frame after Fire.</summary>
        public static void Advance(double time, float deltaTime)
        {
            s_core.Advance(time, deltaTime);
            Apply();
            // Every frame, so that a pad picked up in the middle of a cue starts to rumble.
            HapticPattern pattern = s_core.Pattern;
            for (int i = 0; i < s_channelCount; i++) s_channels[i].SetPattern(pattern);
        }

        /// <summary>What the walker gets now.</summary>
        public static BumpLevel Shown { get { return s_core.Shown; } }
        /// <summary>The rescan icon is up.</summary>
        public static bool RescanShown { get { return s_core.RescanShown; } }
        /// <summary>Rises with every rise of <see cref="Shown"/>: the telemetry cue count.</summary>
        public static int FireCount { get { return s_core.FireCount; } }
        /// <summary>The cue's state, for tests and the HUD's diagnostics.</summary>
        public static BumpCueScheduler Scheduler { get { return s_core; } }

        /// <summary>
        /// Stops every haptic channel's own pattern without changing what is shown: for a pause of the app, where
        /// no frame would otherwise switch a rumble motor off.
        /// </summary>
        public static void Silence()
        {
            for (int i = 0; i < s_channelCount; i++) s_channels[i].SetPattern(HapticPattern.Off);
        }

        /// <summary>Removes the view and the channels and clears the state. Called when Bump Guard is destroyed.</summary>
        public static void Shutdown()
        {
            Silence();
            for (int i = 0; i < s_channelCount; i++) s_channels[i] = null;
            s_channelCount = 0;
            if (s_view != null) s_view.Dispose();
            s_view = null;
            s_core.Reset();
        }

        // Play mode can start without a domain reload: the statics of the last run must not survive.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < MaxChannels; i++) s_channels[i] = null;
            s_channelCount = 0;
            s_view = null;
            s_core.Reset();
        }

        static void Apply()
        {
            if (s_core.TakePulse(out float strength, out CueSound sound))
            {
                for (int i = 0; i < s_channelCount; i++) s_channels[i].Pulse(strength);
                if (s_view != null) s_view.Play(sound, s_core.SoundPosition, s_core.SoundSpatial);
            }
            if (s_view != null)
                s_view.Show(s_core.Icon, s_core.IndicatorPosition, s_core.IndicatorRoll, s_core.IndicatorScale);
        }
    }
}
