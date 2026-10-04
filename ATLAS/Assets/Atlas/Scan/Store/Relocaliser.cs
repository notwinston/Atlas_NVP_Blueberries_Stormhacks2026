using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>Where a relocalisation stands (lane C section 12).</summary>
    public enum RelocaliseState : byte { Idle = 0, Loading = 1, Relocalising = 2, Aligned = 3, Failed = 4, Unsupported = 5 }

    /// <summary>Why a relocalisation ended as Failed or Unsupported.</summary>
    public enum RelocaliseReason : byte
    {
        None = 0,
        /// <summary>There is no saved room to return to.</summary>
        NoSavedRoom = 1,
        /// <summary>This platform has no world map (the Editor, XR Simulation, Windows).</summary>
        NotSupported = 2,
        /// <summary>The room was saved without a world map.</summary>
        NoWorldMap = 3,
        /// <summary>The world map file is not one ARKit accepts.</summary>
        BadWorldMap = 4,
        /// <summary>The session did not recognise the room in time.</summary>
        TimedOut = 5,
        Cancelled = 6,
    }

    /// <summary>How reading and applying the world map file went.</summary>
    public enum WorldMapLoad : byte { Missing = 0, Bad = 1, Applied = 2 }

    /// <summary>What the caller must do after a Tick.</summary>
    public enum RelocaliseStep : byte
    {
        None = 0,
        /// <summary>Aligned on the anchor the world map restored.</summary>
        UseRestoredAnchor = 1,
        /// <summary>Aligned, but the saved anchor did not come back: create one at the room's saved pose.</summary>
        CreateAnchor = 2,
    }

    /// <summary>
    /// The decisions of the pre-scan path (Master Plan section 4.2, task P1-C13; lane C section 12): load the
    /// map, wait for the session to recognise the room, re-find the room anchor, give up after a time-out so
    /// the walker is offered a new scan. Engine-free; time comes in through Tick.
    /// </summary>
    public sealed class Relocaliser
    {
        /// <summary>Seconds from Begin after which the attempt has failed.</summary>
        public const float Timeout = 15f;
        /// <summary>Seconds of normal tracking without the saved anchor before one is created at the saved pose.</summary>
        public const float AnchorWait = 1f;

        public RelocaliseState State { get; private set; }
        public RelocaliseReason Reason { get; private set; }
        /// <summary>Seconds since Begin; stops when the attempt ends.</summary>
        public float Seconds { get; private set; }
        /// <summary>Seconds of unbroken normal tracking since the session recognised the room.</summary>
        public float SecondsNormal { get; private set; }
        /// <summary>True when the world map brought the saved anchor back; false when a new one stands at the saved pose.</summary>
        public bool AnchorRestored { get; private set; }
        /// <summary>The session has been seen searching for the room since the map was applied.</summary>
        public bool SawSearch { get; private set; }

        /// <summary>Loading or Relocalising: Tick must be called.</summary>
        public bool Busy { get { return State == RelocaliseState.Loading || State == RelocaliseState.Relocalising; } }
        /// <summary>0 at Begin, 1 at the time-out or when the attempt has ended.</summary>
        public float Progress
        {
            get
            {
                if (State == RelocaliseState.Idle) return 0f;
                if (!Busy) return 1f;
                float p = Seconds / Timeout;
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
        }

        /// <summary>
        /// Starts an attempt. With no saved room or no world map support it ends at once as Unsupported;
        /// otherwise the state is Loading and the caller reads the map file.
        /// </summary>
        public void Begin(bool hasRoom, bool worldMapSupported)
        {
            Seconds = 0f;
            SecondsNormal = 0f;
            AnchorRestored = false;
            SawSearch = false;
            Reason = RelocaliseReason.None;
            if (!hasRoom) End(RelocaliseState.Unsupported, RelocaliseReason.NoSavedRoom);
            else if (!worldMapSupported) End(RelocaliseState.Unsupported, RelocaliseReason.NotSupported);
            else State = RelocaliseState.Loading;
        }

        /// <summary>The map file was read and handed to the session, or could not be. Ignored unless Loading.</summary>
        public void MapLoaded(WorldMapLoad load)
        {
            if (State != RelocaliseState.Loading) return;
            switch (load)
            {
                case WorldMapLoad.Applied: State = RelocaliseState.Relocalising; break;
                case WorldMapLoad.Bad: End(RelocaliseState.Failed, RelocaliseReason.BadWorldMap); break;
                default: End(RelocaliseState.Unsupported, RelocaliseReason.NoWorldMap); break;
            }
        }

        /// <summary>
        /// Advances the attempt. tracking is the session's live state; savedAnchorTracked is true when the
        /// anchor with the room's saved id exists and is tracked. Returns what the caller must do now.
        /// </summary>
        public RelocaliseStep Tick(float deltaTime, TrackingState tracking, bool savedAnchorTracked)
        {
            if (!Busy) return RelocaliseStep.None;
            if (deltaTime > 0f) Seconds += deltaTime;      // a NaN or negative step adds nothing

            if (State == RelocaliseState.Relocalising)
            {
                if (tracking != TrackingState.Normal)
                {
                    // An applied map restarts the session: tracking drops while it searches for the room.
                    SawSearch = true;
                    SecondsNormal = 0f;
                }
                else if (SawSearch)
                {
                    // Tracking that was already normal when the map was applied says nothing about the room;
                    // only normal tracking after the search does.
                    if (savedAnchorTracked)
                    {
                        AnchorRestored = true;
                        End(RelocaliseState.Aligned, RelocaliseReason.None);
                        return RelocaliseStep.UseRestoredAnchor;
                    }
                    if (deltaTime > 0f) SecondsNormal += deltaTime;
                    if (SecondsNormal >= AnchorWait)
                    {
                        AnchorRestored = false;
                        End(RelocaliseState.Aligned, RelocaliseReason.None);
                        return RelocaliseStep.CreateAnchor;
                    }
                }
            }

            if (Seconds >= Timeout) End(RelocaliseState.Failed, RelocaliseReason.TimedOut);
            return RelocaliseStep.None;
        }

        /// <summary>Drops an attempt in progress and returns to Idle. A finished attempt is cleared as well.</summary>
        public void Cancel()
        {
            Reason = Busy ? RelocaliseReason.Cancelled : RelocaliseReason.None;
            State = RelocaliseState.Idle;
        }

        void End(RelocaliseState state, RelocaliseReason reason)
        {
            State = state;
            Reason = reason;
        }
    }

    /// <summary>When to ask ARKit for its world map after a scan is confirmed (lane C section 12). Engine-free.</summary>
    public static class WorldMapSaveRule
    {
        /// <summary>Seconds to wait for the mapping status to reach Mapped before asking anyway.</summary>
        public const float MappedWait = 5f;
        /// <summary>Seconds after asking with no answer from ARKit before the save counts as failed.</summary>
        public const float AnswerWait = 10f;

        /// <summary>True when the map should be requested now.</summary>
        public static bool AskNow(WorldMappingQuality quality, float secondsWaited)
        {
            return quality == WorldMappingQuality.Mapped || secondsWaited >= MappedWait;
        }
    }
}
