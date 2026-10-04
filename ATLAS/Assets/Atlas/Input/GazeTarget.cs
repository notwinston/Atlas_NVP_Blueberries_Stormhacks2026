using System;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Input
{
    /// <summary>
    /// A gaze target as spaces and menus describe it (lane C contract section 13.3). Register it with
    /// <see cref="IAtlasInput.RegisterGazeTarget"/> while the action it fires is available: that is how every
    /// action keeps its gaze-dwell twin (Master Plan section 0.5 rule 5).
    /// </summary>
    public struct GazeTarget
    {
        /// <summary>Fired on dwell; None when the target drives an axis or only calls back.</summary>
        public AtlasAction Action;
        /// <summary>Used when Mode == Hold and Action == None.</summary>
        public AtlasAxis Axis;
        /// <summary>-1..1.</summary>
        public float AxisValue;
        public GazeSpace Space;
        /// <summary>World: metres in world space (the room frame). Head: metres in the head's frame
        /// (x right, y up, z forward). LookDown: unused.</summary>
        public Vector3 Centre;
        /// <summary>Sphere: metres.</summary>
        public float Radius;
        /// <summary>Metres; 0 = no limit.</summary>
        public float MaxDistance;
        /// <summary>LookDown only: under the gaze while the head's pitch is below this (for example -35).</summary>
        public float PitchBelowDeg;
        public GazeMode Mode;
        /// <summary>Repeat only; 0 = 0.4.</summary>
        public float RepeatSeconds;
        /// <summary>Live in this context only.</summary>
        public InputContext Context;
        /// <summary>Returned with the event: a row index, a detent.</summary>
        public int Tag;

        /// <summary>Sphere (default) or an oriented rectangle: a sign, a menu row.</summary>
        public GazeShape Shape;
        /// <summary>Rect only: the rectangle lies in the local XY plane of this rotation, in the frame of Centre.
        /// The zero quaternion counts as identity.</summary>
        public Quaternion Rotation;
        /// <summary>Rect only: half width and half height in metres.</summary>
        public Vector2 HalfSize;
        /// <summary>A name for logs and debugging tools.</summary>
        public string Label;
        /// <summary>Called from inside the input tick when the target fires, before ActionFired: id and tag.</summary>
        public Action<GazeTargetId, int> Callback;

        /// <summary>A sphere in the room frame, live in the Space context.</summary>
        public static GazeTarget World(AtlasAction action, Vector3 centre, float radius, int tag = 0)
        {
            return new GazeTarget
            {
                Action = action, Space = GazeSpace.World, Centre = centre, Radius = radius, Tag = tag,
                Context = InputContext.Space, Rotation = Quaternion.identity
            };
        }

        /// <summary>A sphere fixed to the head (x right, y up, z forward), live in the Space context.</summary>
        public static GazeTarget Head(AtlasAction action, Vector3 centre, float radius, int tag = 0)
        {
            GazeTarget t = World(action, centre, radius, tag);
            t.Space = GazeSpace.Head;
            return t;
        }

        /// <summary>Under the gaze while the head looks down past the pitch given.</summary>
        public static GazeTarget LookDown(AtlasAction action, float pitchBelowDeg = -35f)
        {
            return new GazeTarget
            {
                Action = action, Space = GazeSpace.LookDown, PitchBelowDeg = pitchBelowDeg,
                Context = InputContext.Space, Rotation = Quaternion.identity
            };
        }

        /// <summary>A sphere in the room frame that pushes an axis to a value while it is dwelt on.</summary>
        public static GazeTarget HoldAxis(AtlasAxis axis, float value, Vector3 worldCentre, float radius)
        {
            GazeTarget t = World(AtlasAction.None, worldCentre, radius);
            t.Axis = axis;
            t.AxisValue = value;
            t.Mode = GazeMode.Hold;
            return t;
        }

        /// <summary>A rectangle in the room frame: it lies in the XY plane of rotation, about centre.</summary>
        public static GazeTarget WorldRect(AtlasAction action, Vector3 centre, Quaternion rotation, Vector2 halfSize, int tag = 0)
        {
            GazeTarget t = World(action, centre, 0f, tag);
            t.Shape = GazeShape.Rect;
            t.Rotation = rotation;
            t.HalfSize = halfSize;
            return t;
        }

        /// <summary>A rectangle fixed to the head: a menu row, a chip.</summary>
        public static GazeTarget HeadRect(AtlasAction action, Vector3 centre, Quaternion rotation, Vector2 halfSize, int tag = 0)
        {
            GazeTarget t = WorldRect(action, centre, rotation, halfSize, tag);
            t.Space = GazeSpace.Head;
            return t;
        }

        /// <summary>The same target in the engine-free core's types.</summary>
        public GazeTargetData ToData()
        {
            return new GazeTargetData
            {
                Action = Action, Axis = Axis, AxisValue = AxisValue, Space = Space, Shape = Shape,
                Centre = new float3(Centre.x, Centre.y, Centre.z),
                Rotation = new quaternion(Rotation.x, Rotation.y, Rotation.z, Rotation.w),
                HalfSize = new float2(HalfSize.x, HalfSize.y),
                Radius = Radius, MaxDistance = MaxDistance, PitchBelowDeg = PitchBelowDeg, Mode = Mode,
                RepeatSeconds = RepeatSeconds, Context = Context, Tag = Tag, Label = Label, Callback = Callback
            };
        }
    }

    /// <summary>What the gaze is on this frame.</summary>
    public struct GazeState
    {
        /// <summary>default when nothing is under the gaze.</summary>
        public GazeTargetId Focused;
        /// <summary>0..1 of the dwell.</summary>
        public float Progress;
        /// <summary>World position of the focused target's centre.</summary>
        public Vector3 Point;
    }
}
