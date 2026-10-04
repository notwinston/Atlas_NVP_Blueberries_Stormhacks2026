using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>
    /// What the hands tests share: a walker's head, the input core, a record of what fired, and fingertips placed
    /// against a target's face. A face is given by its centre, its normal towards the head and two axes across
    /// it; a point (u, v, z) is u and v across the face and z in front of it, so a negative z is through it.
    /// </summary>
    internal sealed class HandsRig
    {
        public const float Dt = 1f / 60f;
        /// <summary>Eye height of a standing walker.</summary>
        public static readonly float3 Eye = new float3(0f, 1.6f, 0f);
        /// <summary>Where a near panel stands: 0.45 m ahead and a hand's width below the eyes.</summary>
        public static readonly float3 Panel = Eye + new float3(0f, -0.10f, 0.45f);
        /// <summary>The head turned to the ceiling: no target is under the gaze, so only a hand can fire one.</summary>
        public static readonly quaternion LookingUp = quaternion.RotateX(math.radians(-60f));

        /// <summary>A near menu row: 3 cm tall, half a metre wide.</summary>
        public static readonly float2 RowHalf = new float2(0.2475f, 0.015f);
        public const float RowPitch = 0.0315f;
        /// <summary>A near sign's target.</summary>
        public const float ChipRadius = 0.0525f;

        public struct Face
        {
            public float3 Centre;
            /// <summary>Unit length, towards the head.</summary>
            public float3 Normal;
            public float3 Right;
            public float3 Up;

            public float3 At(float u, float v, float z)
            {
                return Centre + Right * u + Up * v + Normal * z;
            }
        }

        public readonly AtlasInputCore Input = new AtlasInputCore();
        public readonly List<(AtlasAction action, InputSource source, int tag)> Events = new List<(AtlasAction, InputSource, int)>();
        public readonly List<(GazeTargetId id, int tag)> Calls = new List<(GazeTargetId, int)>();
        public float3 Head = Eye;
        public quaternion Looking = LookingUp;
        /// <summary>Seconds ticked so far.</summary>
        public float Time;
        /// <summary>Frames in which the right, and the left, fingertip reported a firing.</summary>
        public int RightFired, LeftFired;

        private readonly System.Action<GazeTargetId, int> _record;

        public HandsRig(InputContext context = InputContext.Space)
        {
            _record = (id, tag) => Calls.Add((id, tag));
            Input.ActionFired += (action, source, tag) => Events.Add((action, source, tag));
            if (context != InputContext.InHand) Input.PushContext(context);
        }

        public ref readonly HandsTouchHand R { get { return ref Input.HandsTouch.Right; } }
        public ref readonly HandsTouchHand L { get { return ref Input.HandsTouch.Left; } }
        public HandsTouchPhase RightPhase { get { return Input.Touch.PhaseOf(1); } }
        public HandsTouchPhase LeftPhase { get { return Input.Touch.PhaseOf(0); } }

        // ---- targets ----

        public static GazeTargetData Row(AtlasAction action, float3 centre, int tag = 0, GazeMode mode = GazeMode.Once)
        {
            return new GazeTargetData
            {
                Action = action, Space = GazeSpace.World, Shape = GazeShape.Rect, Centre = centre,
                Rotation = quaternion.identity, HalfSize = RowHalf, Mode = mode, Context = InputContext.Space, Tag = tag
            };
        }

        public static GazeTargetData Chip(AtlasAction action, float3 centre, int tag = 0, GazeMode mode = GazeMode.Once,
                                          float radius = ChipRadius)
        {
            return new GazeTargetData
            {
                Action = action, Space = GazeSpace.World, Shape = GazeShape.Sphere, Centre = centre, Radius = radius,
                Mode = mode, Context = InputContext.Space, Tag = tag
            };
        }

        /// <summary>Registers a target whose callback is recorded in <see cref="Calls"/>.</summary>
        public GazeTargetId Add(GazeTargetData target)
        {
            if (target.Callback == null) target.Callback = _record;
            return Input.Targets.Add(target);
        }

        /// <summary>
        /// The face of a room-frame target as a fingertip meets it from where the head is now: a rectangle as it
        /// lies, seen from the head's side; a sphere as the disc through its centre that faces the head.
        /// </summary>
        public Face FaceOf(GazeTargetId id)
        {
            Input.Targets.TryGet(id, out GazeTargetData t);
            var face = new Face { Centre = t.Centre };
            if (t.Shape == GazeShape.Rect)
            {
                float3 normal = math.mul(t.Rotation, new float3(0f, 0f, 1f));
                face.Normal = math.dot(Head - t.Centre, normal) < 0f ? -normal : normal;
                face.Right = math.mul(t.Rotation, new float3(1f, 0f, 0f));
                face.Up = math.mul(t.Rotation, new float3(0f, 1f, 0f));
            }
            else
            {
                face.Normal = math.normalize(Head - t.Centre);
                face.Right = math.normalize(math.cross(face.Normal, new float3(0f, 1f, 0f)));
                face.Up = math.cross(face.Right, face.Normal);
            }
            return face;
        }

        // ---- the head ----

        /// <summary>Turns the head so that the gaze ray passes through a point.</summary>
        public void LookAt(float3 point)
        {
            Looking = quaternion.LookRotationSafe(math.normalize(point - Head), new float3(0f, 1f, 0f));
        }

        // ---- frames ----

        public static HandsFingertip Tip(float3 world, float confidence = 1f)
        {
            return new HandsFingertip { Valid = true, World = world, Confidence = confidence };
        }

        public static HandsInput RightHand(float3 world, float confidence = 1f)
        {
            return new HandsInput { Active = true, Right = Tip(world, confidence) };
        }

        public static HandsInput LeftHand(float3 world, float confidence = 1f)
        {
            return new HandsInput { Active = true, Left = Tip(world, confidence) };
        }

        /// <summary>One frame with the hands given.</summary>
        public void Tick(in HandsInput hands)
        {
            var raw = new RawInput { Hands = hands };
            Tick(in raw);
        }

        /// <summary>One frame with the whole raw state given.</summary>
        public void Tick(in RawInput raw)
        {
            Input.Tick(in raw, Head, Looking, Dt);
            Time += Dt;
            if (Input.HandsTouch.Right.Fired) RightFired++;
            if (Input.HandsTouch.Left.Fired) LeftFired++;
        }

        /// <summary>One frame with the right index fingertip at a point of the room.</summary>
        public void Right(float3 world, float confidence = 1f)
        {
            Tick(RightHand(world, confidence));
        }

        /// <summary>Frames with no hands at all: what every session was before hands existed.</summary>
        public void NoHands(float seconds)
        {
            int frames = Frames(seconds);
            for (int i = 0; i < frames; i++) Tick(default(HandsInput));
        }

        /// <summary>The right fingertip moves along a straight line at a steady speed, both ends included.</summary>
        public void Glide(float3 from, float3 to, float seconds, float confidence = 1f)
        {
            int frames = Frames(seconds);
            for (int i = 0; i <= frames; i++) Right(math.lerp(from, to, i / (float)frames), confidence);
        }

        /// <summary>The right fingertip holds still.</summary>
        public void Rest(float3 at, float seconds, float confidence = 1f)
        {
            int frames = Frames(seconds);
            for (int i = 0; i < frames; i++) Right(at, confidence);
        }

        public static int Frames(float seconds)
        {
            return math.max(1, (int)math.round(seconds / Dt));
        }

        /// <summary>The actions fired so far, in order.</summary>
        public List<AtlasAction> Fired()
        {
            var list = new List<AtlasAction>();
            foreach (var e in Events) list.Add(e.action);
            return list;
        }
    }
}
