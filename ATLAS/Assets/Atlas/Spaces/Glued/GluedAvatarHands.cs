using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// The walker's hands on the walker's copies (documents/ATLAS_Hands_Design.md section 5.6): per hand a palm, five
    /// fingers of three four-sided segments and a forearm from the avatar's elbow to the wrist. Engine-free.
    /// </summary>
    /// <remarks>
    /// Everything is built in the avatar's own frame (<see cref="GluedAvatarMesh"/>: the origin is the tracked head, +Z
    /// is where the walker faces), from the 21 joints of <see cref="IHands"/> taken into that frame by
    /// <see cref="ToAvatarFrame"/>. Every copy draws the one shared avatar mesh through its own transform, so a copy
    /// in a mirrored row shows the left hand on its other side with no extra work.
    ///
    /// A piece is a closed box, tapered when its two ends differ: six faces of four vertices (the start cap, the end
    /// cap, then the four sides), 24 vertices and 12 triangles whatever the pose, so the figures below are exact and a
    /// hand never costs more than <see cref="TrianglesPerHand"/>. Boxes are appended in a fixed order: the palm, the
    /// forearm, then each finger from the thumb to the little finger with its three segments from base to tip. A
    /// piece with no length (a pose that has no shape yet) is left out. The left hand takes the palette cell
    /// <see cref="AtlasPalette.AvatarLeft"/> and the right <see cref="AtlasPalette.AvatarRight"/>, like the avatar's arms.
    /// </remarks>
    public static class GluedAvatarHands
    {
        /// <summary>The walker's two hands.</summary>
        internal const int MaxHands = 2;
        /// <summary>The segments of a finger: its three bones.</summary>
        internal const int SegmentsPerFinger = HandJoints.PerFinger - 1;
        /// <summary>Boxes of one hand: the palm, the forearm and the fifteen finger segments.</summary>
        internal const int BoxesPerHand = 2 + HandJoints.FingerCount * SegmentsPerFinger;
        /// <summary>Vertices of a box: six faces of four, so that every face has its own normal.</summary>
        internal const int VerticesPerBox = 24;
        /// <summary>Triangles of a box: six faces of two.</summary>
        internal const int TrianglesPerBox = 12;
        /// <summary>Vertices of one hand with its forearm, whatever the pose.</summary>
        internal const int VerticesPerHand = BoxesPerHand * VerticesPerBox;
        /// <summary>Triangles of one hand with its forearm, whatever the pose.</summary>
        internal const int TrianglesPerHand = BoxesPerHand * TrianglesPerBox;
        /// <summary>The most vertices a copy's mesh gains: both hands with their forearms.</summary>
        internal const int MaxVertices = MaxHands * VerticesPerHand;
        /// <summary>The most triangles a copy's mesh gains: both hands with their forearms.</summary>
        internal const int MaxTriangles = MaxHands * TrianglesPerHand;
        /// <summary>The most indices a copy's mesh gains.</summary>
        internal const int MaxIndices = 3 * MaxTriangles;

        /// <summary>Half the palm's width at the wrist, metres at hand scale 1.</summary>
        internal const float PalmWristHalfWidth = 0.028f;
        /// <summary>Half the palm's thickness at the wrist, metres at hand scale 1.</summary>
        internal const float PalmWristHalfThickness = 0.013f;
        /// <summary>Half the palm's thickness at the knuckles, metres at hand scale 1.</summary>
        internal const float PalmKnuckleHalfThickness = 0.011f;
        /// <summary>How far the palm reaches past the outer knuckles on each side, metres at hand scale 1.</summary>
        internal const float PalmKnuckleMargin = 0.010f;
        /// <summary>A finger is as thick at its tip as this share of its base.</summary>
        internal const float FingerTaper = 0.7f;
        /// <summary>The forearm's half width at the elbow, metres. It stays inside the upper arm's 0.09 x 0.10 m end.</summary>
        internal const float ForearmElbowHalfWidth = 0.032f;
        /// <summary>The forearm's half thickness at the elbow, metres.</summary>
        internal const float ForearmElbowHalfThickness = 0.032f;
        /// <summary>The forearm's half width at the wrist, metres at hand scale 1: it takes the hand's scale there.</summary>
        internal const float ForearmWristHalfWidth = 0.027f;
        /// <summary>The forearm's half thickness at the wrist, metres at hand scale 1.</summary>
        internal const float ForearmWristHalfThickness = 0.016f;
        /// <summary>The smallest hand scale drawn: a measurement below it is not believed.</summary>
        internal const float MinScale = 0.5f;
        /// <summary>The largest hand scale drawn: a measurement above it is not believed.</summary>
        internal const float MaxScale = 2f;
        /// <summary>A piece shorter than this has no shape and is left out, metres.</summary>
        internal const float MinLength = 1e-4f;

        // Half the width of each finger at its base, thumb first, metres at hand scale 1. A finger is square.
        static readonly float[] s_fingerHalfWidth = { 0.0110f, 0.0088f, 0.0088f, 0.0083f, 0.0074f };

        /// <summary>Half the width of a finger at its base (0 thumb to 4 little finger), metres at hand scale 1.</summary>
        internal static float FingerHalfWidth(int finger)
        {
            return s_fingerHalfWidth[math.clamp(finger, 0, HandJoints.FingerCount - 1)];
        }

        /// <summary>
        /// A point of the room frame in the avatar's own frame: relative to the head and turned back by the walker's
        /// heading, which is how <see cref="GluedSpace"/> poses every copy (the copy's position is the head's image,
        /// its rotation is the heading about +Y). <paramref name="turnBack"/> is <c>quaternion.RotateY(-heading)</c>.
        /// </summary>
        public static float3 ToAvatarFrame(float3 world, float3 head, quaternion turnBack)
        {
            return math.mul(turnBack, world - head);
        }

        /// <summary>The scale a hand is drawn at: the service's, kept between <see cref="MinScale"/> and <see cref="MaxScale"/>, and 1 when it has none.</summary>
        public static float SaneScale(float scale)
        {
            return scale > 0f && scale < 4f ? math.clamp(scale, MinScale, MaxScale) : 1f;
        }

        /// <summary>
        /// Appends one hand and its forearm. <paramref name="joints"/> holds the 21 joints in the avatar's frame, all
        /// finite. Returns the boxes added: <see cref="BoxesPerHand"/>, or fewer when part of the pose has no length.
        /// </summary>
        public static int Append(AtlasMeshBuilder data, HandSide side, float3[] joints, float scale)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (joints == null || joints.Length < HandJoints.Count) throw new ArgumentException("A hand has 21 joints.", nameof(joints));
            scale = SaneScale(scale);
            float2 uv = AtlasPalette.Uv(side == HandSide.Left ? AtlasPalette.AvatarLeft : AtlasPalette.AvatarRight);
            int boxes = 0;

            // The palm's frame, from the joints alone: along from the wrist to the knuckles, across the knuckle line
            // (towards the index finger), through the palm. Fingers bend about the knuckle line, so it is the one
            // direction a finger bone never lies along, and it gives every segment its sides.
            float3 wrist = joints[(int)HandJoint.Wrist];
            float3 index = joints[(int)HandJoint.IndexMcp], middle = joints[(int)HandJoint.MiddleMcp];
            float3 ring = joints[(int)HandJoint.RingMcp], little = joints[(int)HandJoint.LittleMcp];
            float3 knuckles = 0.25f * (index + middle + ring + little);
            float3 along = math.normalizesafe(knuckles - wrist, new float3(0f, 0f, 1f));
            float3 across = Perpendicular(index - little, along, new float3(1f, 0f, 0f));
            float3 through = math.cross(along, across);

            // The palm: a slab from the wrist to the farthest knuckle, as wide as the knuckles are apart and a margin more.
            float far = math.max(math.max(math.dot(index - wrist, along), math.dot(middle - wrist, along)),
                                 math.max(math.dot(ring - wrist, along), math.dot(little - wrist, along)));
            if (far > MinLength)
            {
                float spread = math.abs(math.dot(index - little, across));
                AddPrism(data, wrist, wrist + along * far, across, through,
                         new float2(PalmWristHalfWidth, PalmWristHalfThickness) * scale,
                         new float2(0.5f * spread + PalmKnuckleMargin * scale, PalmKnuckleHalfThickness * scale), uv);
                boxes++;
            }

            // The forearm: from the avatar's elbow, the lower end of its upper arm, to the wrist.
            float3 elbow = GluedAvatarMesh.Elbow(side);
            float forearm = math.distance(elbow, wrist);
            if (forearm > MinLength)
            {
                float3 axis = (wrist - elbow) / forearm;
                float3 forearmSide = Perpendicular(across, axis, through);
                AddPrism(data, elbow, wrist, forearmSide, math.cross(axis, forearmSide),
                         new float2(ForearmElbowHalfWidth, ForearmElbowHalfThickness),
                         new float2(ForearmWristHalfWidth, ForearmWristHalfThickness) * scale, uv);
                boxes++;
            }

            // The fingers: each segment runs from its joint to the next and is a little thinner than the one before.
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int joint = HandJoints.Base(finger);
                float half = FingerHalfWidth(finger) * scale;
                for (int segment = 0; segment < SegmentsPerFinger; segment++)
                {
                    float3 from = joints[joint + segment], to = joints[joint + segment + 1];
                    float length = math.distance(from, to);
                    if (!(length > MinLength)) continue;
                    float3 axis = (to - from) / length;
                    float3 segmentSide = Perpendicular(across, axis, through);
                    float start = half * math.lerp(1f, FingerTaper, (float)segment / SegmentsPerFinger);
                    float end = half * math.lerp(1f, FingerTaper, (float)(segment + 1) / SegmentsPerFinger);
                    AddPrism(data, from, to, segmentSide, math.cross(axis, segmentSide), new float2(start), new float2(end), uv);
                    boxes++;
                }
            }
            return boxes;
        }

        // The unit vector of `reference` made perpendicular to the unit vector `axis`. When it has no length, the
        // same of `fallback`, and when that has none either, any perpendicular.
        static float3 Perpendicular(float3 reference, float3 axis, float3 fallback)
        {
            float3 v = reference - math.dot(reference, axis) * axis;
            float lengthSquared = math.lengthsq(v);
            if (lengthSquared > 1e-8f) return v * math.rsqrt(lengthSquared);
            v = fallback - math.dot(fallback, axis) * axis;
            lengthSquared = math.lengthsq(v);
            if (lengthSquared > 1e-8f) return v * math.rsqrt(lengthSquared);
            return math.normalize(math.cross(axis, math.abs(axis.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f)));
        }

        // A box from p0 to p1: at each end a rectangle of the half extents (along `side`, along `up`) centred on the
        // end, which are perpendicular to each other and to the axis. Faces in the order the type's summary names.
        static void AddPrism(AtlasMeshBuilder data, float3 p0, float3 p1, float3 side, float3 up, float2 half0, float2 half1, float2 uv)
        {
            float3 sa = side * half0.x, ua = up * half0.y, sb = side * half1.x, ub = up * half1.y;
            float3 a0 = p0 - sa - ua, a1 = p0 + sa - ua, a2 = p0 + sa + ua, a3 = p0 - sa + ua;
            float3 b0 = p1 - sb - ub, b1 = p1 + sb - ub, b2 = p1 + sb + ub, b3 = p1 - sb + ub;
            float3 centre = 0.5f * (p0 + p1);
            AddFace(data, a0, a1, a2, a3, centre, uv);
            AddFace(data, b0, b1, b2, b3, centre, uv);
            AddFace(data, a0, a1, b1, b0, centre, uv);
            AddFace(data, a1, a2, b2, b1, centre, uv);
            AddFace(data, a2, a3, b3, b2, centre, uv);
            AddFace(data, a3, a0, b0, b3, centre, uv);
        }

        // A flat quad a, b, c, d in order round its edge, wound so that its front (clockwise, Unity's rule) faces away
        // from `inside`, with that normal on all four vertices. The winding is chosen from the geometry, so the box is
        // right whichever way round the hand's frame happens to be handed.
        static void AddFace(AtlasMeshBuilder data, float3 a, float3 b, float3 c, float3 d, float3 inside, float2 uv)
        {
            float3 outward = 0.25f * (a + b + c + d) - inside;
            float3 front = math.cross(b - a, c - a);
            bool reversed = math.dot(front, outward) < 0f;
            float3 normal = math.normalizesafe(reversed ? -front : front, math.normalizesafe(outward, new float3(0f, 1f, 0f)));
            // The four vertices share one normal, so they share one baked shade: worked out once, as AddVertex would per vertex.
            AtlasVertex vertex;
            vertex.Normal = normal;
            vertex.Color = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(normal)), 0);
            vertex.Uv0 = uv;
            vertex.Uv1 = float2.zero;
            vertex.Position = a;
            int i0 = data.AddVertex(in vertex);
            vertex.Position = b;
            int i1 = data.AddVertex(in vertex);
            vertex.Position = c;
            int i2 = data.AddVertex(in vertex);
            vertex.Position = d;
            int i3 = data.AddVertex(in vertex);
            if (reversed)
            {
                data.AddTriangle(i0, i2, i1);
                data.AddTriangle(i0, i3, i2);
            }
            else
            {
                data.AddTriangle(i0, i1, i2);
                data.AddTriangle(i0, i2, i3);
            }
        }
    }

    /// <summary>
    /// The mesh every copy of the walker draws, as it stands this frame: the body, and after it the hands when
    /// <see cref="IHands"/> has them (documents/ATLAS_Hands_Design.md section 5.6). Engine-free: it holds the data,
    /// and <see cref="GluedSpace"/> uploads it when <see cref="Update"/> says it changed.
    /// </summary>
    /// <remarks>
    /// With no service, or no tracked hand, the data is exactly <see cref="GluedAvatarMesh.Build"/> and
    /// <see cref="Update"/> never reports a change. When a hand appears or disappears, or any joint, the head's
    /// turn or the hand's scale differs from the frame the data was built from, the hands are appended again after
    /// the body in one call; nothing is allocated, because the builder was given room for both hands at the start.
    /// </remarks>
    public sealed class GluedAvatarRig
    {
        readonly AtlasMeshBuilder _data;
        readonly int _bodyVertices, _bodyIndices;
        readonly float3 _bodyMin, _bodyMax;

        // The joints of each hand in the avatar's frame: this frame's, and those the data was built from.
        readonly float3[][] _joints = { new float3[HandJoints.Count], new float3[HandJoints.Count] };
        readonly float3[][] _built = { new float3[HandJoints.Count], new float3[HandJoints.Count] };
        readonly float[] _scale = new float[GluedAvatarHands.MaxHands];
        readonly float[] _builtScale = new float[GluedAvatarHands.MaxHands];
        // Bit 0: the left hand is in the data. Bit 1: the right hand is.
        int _shown;

        /// <summary>Builds the body and makes room for both hands after it. Holds the body alone until <see cref="Update"/> brings a hand.</summary>
        public GluedAvatarRig()
        {
            _data = new AtlasMeshBuilder(512, 1024);
            GluedAvatarMesh.Build(_data);
            _bodyVertices = _data.VertexCount;
            _bodyIndices = _data.IndexCount;
            _bodyMin = _data.BoundsMin;
            _bodyMax = _data.BoundsMax;
            _data.Reserve(GluedAvatarHands.MaxVertices, GluedAvatarHands.MaxIndices);
        }

        /// <summary>The vertices, indices and bounds to upload. The body first, then the hands. Do not change them.</summary>
        public AtlasMeshBuilder Data { get { return _data; } }
        /// <summary>Vertices of the body alone: where the hands start.</summary>
        public int BodyVertexCount { get { return _bodyVertices; } }
        /// <summary>Indices of the body alone.</summary>
        public int BodyIndexCount { get { return _bodyIndices; } }
        /// <summary>Hands in the data now: 0, 1 or 2.</summary>
        public int HandCount { get { return (_shown & 1) + ((_shown >> 1) & 1); } }
        /// <summary>The data holds at least one hand, so it is not the body alone.</summary>
        public bool ShowsHands { get { return _shown != 0; } }

        /// <summary>The data holds this hand.</summary>
        public bool Shows(HandSide side)
        {
            return (_shown & (1 << (int)side)) != 0;
        }

        /// <summary>
        /// Brings the data up to this frame: each hand the service reports as tracked, from its joints in the avatar's
        /// frame (relative to <paramref name="head"/> and turned back by <paramref name="heading"/>, the walker's
        /// heading about +Y, as <see cref="GluedSpace"/> poses the copies). A hand with a joint that is not a number is
        /// left out. Returns true when the data changed and must be uploaded: a hand came, went or moved. Null
        /// <paramref name="hands"/> is no service. Allocates nothing.
        /// </summary>
        public bool Update(IHands hands, float3 head, float heading)
        {
            int mask = 0;
            if (hands != null)
            {
                quaternion turnBack = quaternion.RotateY(-heading);
                for (int side = 0; side < GluedAvatarHands.MaxHands; side++)
                {
                    var which = (HandSide)side;
                    ref readonly HandPose pose = ref hands.Pose(which);
                    if (!pose.Tracked) continue;
                    if (!ReadJoints(hands, which, head, turnBack, _joints[side])) continue;
                    _scale[side] = GluedAvatarHands.SaneScale(pose.Scale);
                    mask |= 1 << side;
                }
            }
            if (!Differs(mask)) return false;
            Rebuild(mask);
            return true;
        }

        /// <summary>
        /// Takes the hands out of the data again and returns true when there were any. For a fault: the copies then
        /// show the plain body.
        /// </summary>
        public bool ShowBodyOnly()
        {
            if (_shown == 0) return false;
            Truncate();
            _shown = 0;
            return true;
        }

        static bool ReadJoints(IHands hands, HandSide side, float3 head, quaternion turnBack, float3[] into)
        {
            bool finite = true;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float3 p = GluedAvatarHands.ToAvatarFrame(hands.Joint(side, (HandJoint)joint), head, turnBack);
                into[joint] = p;
                finite &= math.all(math.isfinite(p));
            }
            return finite;
        }

        // True when the data is not what these hands would make: another set of hands, or a joint or a scale that differs.
        bool Differs(int mask)
        {
            if (mask != _shown) return true;
            for (int side = 0; side < GluedAvatarHands.MaxHands; side++)
            {
                if ((mask & (1 << side)) == 0) continue;
                if (_scale[side] != _builtScale[side]) return true;
                float3[] now = _joints[side], then = _built[side];
                for (int joint = 0; joint < HandJoints.Count; joint++)
                    if (!math.all(now[joint] == then[joint])) return true;
            }
            return false;
        }

        void Rebuild(int mask)
        {
            Truncate();
            for (int side = 0; side < GluedAvatarHands.MaxHands; side++)
            {
                if ((mask & (1 << side)) == 0) continue;
                GluedAvatarHands.Append(_data, (HandSide)side, _joints[side], _scale[side]);
                Array.Copy(_joints[side], _built[side], HandJoints.Count);
                _builtScale[side] = _scale[side];
            }
            _shown = mask;
        }

        // The builder keeps its arrays: the hands are simply forgotten, and the bounds are the body's again.
        void Truncate()
        {
            _data.VertexCount = _bodyVertices;
            _data.IndexCount = _bodyIndices;
            _data.BoundsMin = _bodyMin;
            _data.BoundsMax = _bodyMax;
        }
    }
}
