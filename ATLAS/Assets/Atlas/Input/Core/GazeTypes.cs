using System;
using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>Frame a gaze target is given in. LookDown has no position: it is under the gaze while the head
    /// pitches below a threshold.</summary>
    public enum GazeSpace : byte { World = 0, Head = 1, LookDown = 2 }

    /// <summary>What a completed dwell does (lane C contract section 13.2).</summary>
    public enum GazeMode : byte { Once = 0, Repeat = 1, Hold = 2 }

    /// <summary>Shape of a gaze target: a sphere about its centre, or a flat oriented rectangle.</summary>
    public enum GazeShape : byte { Sphere = 0, Rect = 1 }

    /// <summary>Handle of a registered target. default is "none".</summary>
    public readonly struct GazeTargetId : IEquatable<GazeTargetId>
    {
        // Slot + 1, so that default(GazeTargetId) is invalid; the generation tells a reused slot from a stale handle.
        internal readonly int SlotPlusOne;
        internal readonly int Generation;

        internal GazeTargetId(int slot, int generation)
        {
            SlotPlusOne = slot + 1;
            Generation = generation;
        }

        internal int Slot => SlotPlusOne - 1;

        public bool IsValid => SlotPlusOne > 0;
        public bool Equals(GazeTargetId other) => SlotPlusOne == other.SlotPlusOne && Generation == other.Generation;
        public override bool Equals(object obj) => obj is GazeTargetId other && Equals(other);
        public override int GetHashCode() => (SlotPlusOne * 397) ^ Generation;
        public static bool operator ==(GazeTargetId a, GazeTargetId b) => a.Equals(b);
        public static bool operator !=(GazeTargetId a, GazeTargetId b) => !a.Equals(b);
        public override string ToString() => IsValid ? "gaze#" + Slot + "." + Generation : "gaze#none";
    }

    /// <summary>A gaze target as the engine-free core keeps it (README F2): the same content as
    /// <see cref="GazeTarget"/>, in Unity.Mathematics types.</summary>
    public struct GazeTargetData
    {
        /// <summary>Fired on dwell. None when the target drives an axis or only calls back.</summary>
        public AtlasAction Action;
        /// <summary>Used when Mode == Hold and Action == None.</summary>
        public AtlasAxis Axis;
        public float AxisValue;
        public GazeSpace Space;
        public GazeShape Shape;
        /// <summary>World: metres in the room frame. Head: metres in the head's frame (x right, y up, z forward).</summary>
        public float3 Centre;
        /// <summary>Rect only: the rectangle lies in the local XY plane of this rotation (same frame as Centre).</summary>
        public quaternion Rotation;
        /// <summary>Rect only: half width and half height, metres.</summary>
        public float2 HalfSize;
        /// <summary>Sphere only: metres.</summary>
        public float Radius;
        /// <summary>Metres; 0 = no limit.</summary>
        public float MaxDistance;
        /// <summary>LookDown only: under the gaze while the head's pitch is below this many degrees.</summary>
        public float PitchBelowDeg;
        public GazeMode Mode;
        /// <summary>Repeat only; 0 = 0.4 s.</summary>
        public float RepeatSeconds;
        /// <summary>The target is live in this context only.</summary>
        public InputContext Context;
        /// <summary>Returned with the event: a row index, a detent.</summary>
        public int Tag;
        /// <summary>Shown by debugging tools and logs; never read on the frame path.</summary>
        public string Label;
        /// <summary>Called from inside the input tick when the target fires: id and tag. May be null.</summary>
        public Action<GazeTargetId, int> Callback;
    }

    /// <summary>One firing of the gaze dwell or of the lever.</summary>
    public struct GazeFiring
    {
        /// <summary>The target that fired; default when the lever was tapped with nothing under the gaze.</summary>
        public GazeTargetId Id;
        public AtlasAction Action;
        public InputSource Source;
        public int Tag;
        public Action<GazeTargetId, int> Callback;
    }
}
