using System;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// Reads the head once per frame and offers it as two streams (Master Plan sections 6.3 and 8.7): the raw
    /// stream for safety and the cameras, the geometry stream for the kernel and the spaces.
    /// Published through AtlasServices. Its per-frame work is ISenseStep and IPoseFilterStep.
    /// </summary>
    public interface IPosePipeline
    {
        /// <summary>May be replaced between frames.</summary>
        IPoseSource Source { get; set; }
        IDepthSource Depth { get; set; }
        /// <summary>The active room, or null. Setting it puts positions in the room frame from the next frame
        /// and calls ResetGeometry.</summary>
        RoomModel Room { get; set; }

        /// <summary>Valid after step 1.</summary>
        ref readonly SensorFrame Sensors { get; }
        /// <summary>Sensors.Head.</summary>
        HeadPose Raw { get; }
        /// <summary>Valid after step 3.</summary>
        HeadPose Geometry { get; }
        /// <summary>Metres (x, z); zero when rejected.</summary>
        float2 Step { get; }
        /// <summary>Metres (x, z): Raw.Floor at the last reset plus every Step since.</summary>
        float2 PathFloor { get; }
        /// <summary>A jump was dropped this frame.</summary>
        bool JumpRejected { get; }
        /// <summary>Jumps dropped since start.</summary>
        int JumpCount { get; }
        /// <summary>Not Normal for 0.5 s or more.</summary>
        bool TrackingLost { get; }
        /// <summary>The recentre entry point: PathFloor becomes Raw.Floor and the next displacement is zero.</summary>
        void ResetGeometry();

        /// <summary>Rotation prediction, milliseconds: 0 (off, the default) or 30 to 60 (section 8.7).</summary>
        float GyroPredictMs { get; set; }
        /// <summary>The rotation to add to the eyes this frame, in head space; identity when off.</summary>
        quaternion PredictionRotation { get; }

        /// <summary>Raised inside step 1.</summary>
        event Action<TrackingState> TrackingChanged;
        /// <summary>Raised inside step 3, once per loss.</summary>
        event Action TrackingLostRaised;
        event Action TrackingRegained;
        /// <summary>Metres of the dropped jump.</summary>
        event Action<float> JumpRejectedRaised;
    }
}
