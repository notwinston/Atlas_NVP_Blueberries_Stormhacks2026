using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Collections;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// One hand's mesh before any joints are known (documents/ATLAS_Hands_Design.md section 5.1), built in code
    /// from the dimensions of <see cref="HandModel"/>: five tubes of <see cref="HandsConstants.FingerSides"/>
    /// sides that taper with the model's radii and end in a rounded cap, a palm slab with a bevelled rim, and a
    /// short cuff that closes the hand behind the wrist. Built once per side at boot; <see cref="HandsSkin"/>
    /// poses it on the joints every frame.
    ///
    /// Every vertex is stored in the frame of what it hangs from, never in the hand's:
    /// a ring of a tube sits on one bone by the joint at one of its ends and leans on the bone across that joint
    /// (weights <see cref="RingBlend"/> and one minus it), so a bent joint is rounded by two rings a little apart
    /// and not creased by one; a vertex of the palm or of the cuff hangs from one of six anchor joints (the
    /// wrist, the thumb's base, the four knuckles) or halfway between two, so the slab passes through the
    /// knuckles the walker really has and a finger's tube cannot leave it.
    ///
    /// Bone j is the bone that ends at joint j (it starts at HandJoints.Parent(j)); the five bones that hang from
    /// the wrist are the first bones of the five chains. The left hand is the right one with its side axis and
    /// its normals turned over and its triangles wound the other way.
    /// </summary>
    public sealed class HandsMeshTemplate
    {
        // ---- What one hand costs (section 5.7). Hands_Skin_Mesh holds the template to these. ----

        /// <summary>Triangles of one hand: five tubes, then the palm, then the cuff, in that order in the index buffer.</summary>
        internal const int TriangleTotal = 738;
        internal const int FingerTriangles = 120, PalmTriangles = 78, CuffTriangles = 60;
        /// <summary>Vertices of one hand, in the same order.</summary>
        internal const int VertexTotal = 403;
        internal const int FingerVertices = 65, PalmVertices = 41, CuffVertices = 37;
        /// <summary>Rings of one tube: one before and one after each of its three joints, the last full ring, the cap's ring.</summary>
        internal const int FingerRings = 8;
        /// <summary>Sides and rings of the cuff, and corners of the palm's outline.</summary>
        internal const int CuffSides = 12, CuffRings = 3, PalmOutline = 13;
        /// <summary>The most frames a template may ask the skin to build.</summary>
        internal const int MaxFrames = 64;

        // ---- Shape. Starting values, as the model's own dimensions are. ----

        /// <summary>
        /// A ring sits this many joint radii before or after its joint. The two rings of a joint meet on the
        /// inside of a bend when the tangent of a quarter of the bend reaches this number: at 0.8 no joint folds
        /// until it is bent past 150 degrees, where a ring a millimetre from its joint would fold before 30.
        /// </summary>
        internal const float RingOffset = 0.8f;
        /// <summary>Weight of the bone across the joint at a ring: the ring turns by this share of the bend.</summary>
        internal const float RingBlend = 0.25f;
        /// <summary>The first ring of a tube is left open inside the palm; its radius is this share of the joint's, so it stays buried.</summary>
        internal const float BaseRingRadius = 0.5f;
        /// <summary>The cap's apex is this many tip radii beyond the tip joint: the detector's tip lies inside the fingertip.</summary>
        internal const float TipOvershoot = 0.5f;
        /// <summary>The cap's ring sits at this latitude of the cap's half sphere, degrees.</summary>
        internal const float CapLatitudeDegrees = 50f;
        /// <summary>The thumb's up is the palm's normal turned this far about the line from the wrist to the index knuckle, degrees.</summary>
        internal const float ThumbTurnDegrees = 60f;
        /// <summary>The palm's rim lies this far beyond the knuckles, and its two faces stop this far inside the rim, metres.</summary>
        internal const float PalmRim = 0.0075f, PalmBevel = 0.007f;
        /// <summary>The palm's face is this much lower in its hollow than at its rim, metres.</summary>
        internal const float PalmHollow = 0.0015f;
        /// <summary>Half the wrist's width and half its thickness at the cuff, metres.</summary>
        internal const float WristHalfWidth = 0.028f, WristHalfThickness = 0.015f;
        /// <summary>The cuff along the forearm, metres from the wrist: where it starts inside the palm, its two rings, its closed end.</summary>
        internal const float CuffInside = 0.008f, CuffFront = -0.001f, CuffBack = -0.030f, CuffEnd = -0.032f;

        internal enum FrameKind : byte
        {
            /// <summary>A bone's own frame at the joint named, turned towards the bone across that joint.</summary>
            Ring = 0,
            /// <summary>The palm's frame at an anchor joint, or between two.</summary>
            Anchor = 1,
        }

        /// <summary>What a vertex hangs from. The skin builds one posed frame for each, once a frame.</summary>
        internal struct Frame
        {
            public FrameKind Kind;
            /// <summary>Ring: the joint the ring is by, the frame's origin. Anchor: the first anchor joint.</summary>
            public byte Joint;
            /// <summary>Ring: the bone the ring sits on. Anchor: unused (the first anchor).</summary>
            public byte Bone;
            /// <summary>Ring: the bone across the joint, or Bone itself. Anchor: the second anchor joint, or the first.</summary>
            public byte Other;
            /// <summary>Weight of Other; the vertex's own bone or anchor has one minus this. 0 to 0.5.</summary>
            public float Weight;
        }

        /// <summary>
        /// A vertex in the frame it hangs from: x to the side, y up (the palm's side of a finger), z along the
        /// bone, metres at scale 1.
        /// </summary>
        internal struct Vertex
        {
            public float3 Position;
            public float3 Normal;
            public int Frame;
        }

        /// <summary>A ring of a tube or of the cuff: Count vertices from First, around Centre in its frame.</summary>
        internal struct Ring
        {
            public int First, Count, Frame;
            public float3 Centre;
            /// <summary>Metres at scale 1; 0 for the cuff, which is not round.</summary>
            public float Radius;
        }

        internal readonly Vertex[] Vertices;
        internal readonly Frame[] Frames;
        internal readonly Ring[] Rings;
        /// <summary>HandModel.BoneLength for every joint, so that the skin reads no table but this template.</summary>
        internal readonly float[] BoneLengths;
        internal readonly float PalmLength;
        /// <summary>The farthest any vertex is from the joint, or the line between two joints, it hangs from: metres at scale 1.</summary>
        internal readonly float Reach;
        /// <summary>Radians, signed: the right thumb turns one way and the left the other.</summary>
        internal readonly float ThumbTurn;
        readonly int[] m_Indices;

        /// <summary>The hand this mesh is for.</summary>
        public HandSide Side { get; }
        /// <summary>Vertices HandsSkin.Skin writes; at most HandsConstants.MaxVertices.</summary>
        public int VertexCount { get { return Vertices.Length; } }
        /// <summary>Entries <see cref="CopyIndices(NativeArray{int})"/> writes; at most HandsConstants.MaxIndices.</summary>
        public int IndexCount { get { return m_Indices.Length; } }
        /// <summary>Triangles of the mesh.</summary>
        public int TriangleCount { get { return m_Indices.Length / 3; } }

        HandsMeshTemplate(HandSide side, Vertex[] vertices, Frame[] frames, Ring[] rings, int[] indices, float reach)
        {
            Side = side;
            Vertices = vertices;
            Frames = frames;
            Rings = rings;
            m_Indices = indices;
            Reach = reach;
            PalmLength = HandModel.PalmLength;
            BoneLengths = new float[HandJoints.Count];
            for (int joint = 0; joint < HandJoints.Count; joint++) BoneLengths[joint] = HandModel.BoneLength(joint);
            ThumbTurn = math.radians(ThumbTurnDegrees) * (side == HandSide.Left ? -1f : 1f);
        }

        /// <summary>
        /// The mesh of one hand. Allocates: call it once per side at boot. The left hand is the mirror of the right.
        /// </summary>
        public static HandsMeshTemplate Build(HandSide side)
        {
            var vertices = new List<Vertex>(VertexTotal);
            var frames = new List<Frame>(MaxFrames);
            var rings = new List<Ring>(HandJoints.FingerCount * FingerRings + CuffRings);
            var indices = new List<int>(3 * TriangleTotal);

            for (int finger = 0; finger < HandJoints.FingerCount; finger++) AddFinger(finger, vertices, frames, rings, indices);
            AddPalmAndCuff(vertices, frames, rings, indices);
            if (frames.Count > MaxFrames) throw new InvalidOperationException("The hand mesh asks for more frames than the skin builds.");
            if (vertices.Count > HandsConstants.MaxVertices || indices.Count > HandsConstants.MaxIndices)
                throw new InvalidOperationException("The hand mesh does not fit the capacity of HandsConstants.");

            float reach = 0f;
            for (int i = 0; i < vertices.Count; i++) reach = math.max(reach, math.length(vertices[i].Position));

            Vertex[] v = vertices.ToArray();
            Ring[] r = rings.ToArray();
            int[] index = indices.ToArray();
            if (side == HandSide.Left)
            {
                // A reflection: the side axis and the normals turn over, and so does every triangle's winding.
                for (int i = 0; i < v.Length; i++)
                {
                    v[i].Position.x = -v[i].Position.x;
                    v[i].Normal.x = -v[i].Normal.x;
                }
                for (int i = 0; i < r.Length; i++) r[i].Centre.x = -r[i].Centre.x;
                for (int t = 0; t < index.Length; t += 3)
                {
                    int swap = index[t + 1];
                    index[t + 1] = index[t + 2];
                    index[t + 2] = swap;
                }
            }
            return new HandsMeshTemplate(side, v, frames.ToArray(), r, index, reach);
        }

        /// <summary>
        /// Writes the triangles, three indices each, wound clockwise seen from outside. They never change for
        /// the template's life: copy them once into the mesh's index buffer. Needs room for IndexCount entries.
        /// </summary>
        public void CopyIndices(NativeArray<int> indices)
        {
            if (!indices.IsCreated || indices.Length < m_Indices.Length)
                throw new ArgumentException("The index buffer needs room for IndexCount entries.", nameof(indices));
            NativeArray<int>.Copy(m_Indices, indices, m_Indices.Length);
        }

        /// <summary>The same into raw memory with room for capacity entries: what the headless tests read.</summary>
        internal unsafe void CopyIndices(int* indices, int capacity)
        {
            if (indices == null || capacity < m_Indices.Length)
                throw new ArgumentException("The index buffer needs room for IndexCount entries.", nameof(indices));
            for (int i = 0; i < m_Indices.Length; i++) indices[i] = m_Indices[i];
        }

        // ---- Fingers and thumb ----

        // One tube: by each of the chain's first three joints a ring before it and a ring after it, then the last
        // full ring, the cap's ring and the apex. The first ring lies inside the palm and is left open.
        static void AddFinger(int finger, List<Vertex> vertices, List<Frame> frames, List<Ring> rings, List<int> indices)
        {
            const int sides = HandsConstants.FingerSides;
            int first = HandJoints.Base(finger), tip = HandJoints.Tip(finger);
            int start = vertices.Count;

            for (int joint = first; joint < tip; joint++)
            {
                float radius = HandModel.Radius(joint);
                float behind = HandModel.BoneLength(joint), ahead = HandModel.BoneLength(joint + 1);
                // Never more than two fifths of a bone, so two rings on one bone cannot meet.
                float offset = math.min(RingOffset * radius, 0.4f * math.min(behind, ahead));

                float before = joint == first
                    ? BaseRingRadius * radius
                    : math.lerp(HandModel.Radius(joint - 1), radius, 1f - offset / behind);
                AddRing(vertices, rings, AddFrame(frames, FrameKind.Ring, joint, joint, joint + 1, RingBlend), sides, -offset, before, 0f);

                float after = math.lerp(radius, HandModel.Radius(joint + 1), offset / ahead);
                AddRing(vertices, rings, AddFrame(frames, FrameKind.Ring, joint, joint + 1, joint, RingBlend), sides, offset, after, 0f);
            }

            // The cap is half a sphere of the tip's radius whose pole lies beyond the tip joint.
            float tipRadius = HandModel.Radius(tip);
            float centre = -math.min((1f - TipOvershoot) * tipRadius, 0.5f * HandModel.BoneLength(tip));
            float latitude = math.radians(CapLatitudeDegrees);
            int cap = AddFrame(frames, FrameKind.Ring, tip, tip, tip, 0f);
            AddRing(vertices, rings, cap, sides, centre, tipRadius, 0f);
            AddRing(vertices, rings, cap, sides, centre + tipRadius * math.sin(latitude), tipRadius * math.cos(latitude), latitude);
            int apex = vertices.Count;
            vertices.Add(new Vertex { Position = new float3(0f, 0f, centre + tipRadius), Normal = new float3(0f, 0f, 1f), Frame = cap });

            for (int k = 0; k + 1 < FingerRings; k++) AddBand(indices, start + k * sides, start + (k + 1) * sides, sides);
            int last = start + (FingerRings - 1) * sides;
            for (int i = 0; i < sides; i++) AddTriangle(indices, last + i, last + (i + 1) % sides, apex);
        }

        static int AddFrame(List<Frame> frames, FrameKind kind, int joint, int bone, int other, float weight)
        {
            frames.Add(new Frame { Kind = kind, Joint = (byte)joint, Bone = (byte)bone, Other = (byte)other, Weight = weight });
            return frames.Count - 1;
        }

        // A round ring about the bone at height along; its normals lean towards the bone's end by tilt radians.
        static void AddRing(List<Vertex> vertices, List<Ring> rings, int frame, int sides, float along, float radius, float tilt)
        {
            rings.Add(new Ring { First = vertices.Count, Count = sides, Frame = frame, Centre = new float3(0f, 0f, along), Radius = radius });
            math.sincos(tilt, out float rise, out float flat);
            for (int i = 0; i < sides; i++)
            {
                math.sincos(2f * math.PI * i / sides, out float sin, out float cos);
                vertices.Add(new Vertex
                {
                    Position = new float3(radius * cos, radius * sin, along),
                    Normal = new float3(flat * cos, flat * sin, rise),
                    Frame = frame,
                });
            }
        }

        // Two triangles a side between ring a and ring b, b being farther along +z. Seen from outside the corners
        // run clockwise, which is the look's front face (AtlasMeshBuilder): cross(b - a, c - a) points outwards.
        static void AddBand(List<int> indices, int a, int b, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                AddTriangle(indices, a + i, a + next, b + next);
                AddTriangle(indices, a + i, b + next, b + i);
            }
        }

        static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        // ---- Palm and cuff ----

        // A corner of the palm's outline: an offset in the model's palm frame (x to the thumb's side, z towards
        // the fingers) from an anchor joint, or from halfway between two.
        struct Corner
        {
            public int Anchor, Other;
            public float X, Z;

            public Corner(int anchor, int other, float x, float z)
            {
                Anchor = anchor;
                Other = other;
                X = x;
                Z = z;
            }
        }

        // The palm is designed where HandModel's open right hand is, in the model's own palm frame, and each
        // vertex is then written in the palm's rotation at its anchor: that is the frame the skin gives it back in.
        static void AddPalmAndCuff(List<Vertex> vertices, List<Frame> frames, List<Ring> rings, List<int> indices)
        {
            const int wrist = (int)HandJoint.Wrist, thumb = (int)HandJoint.ThumbCmc, index = (int)HandJoint.IndexMcp,
                      middle = (int)HandJoint.MiddleMcp, ring = (int)HandJoint.RingMcp, little = (int)HandJoint.LittleMcp;
            float indexRadius = HandModel.Radius(index), littleRadius = HandModel.Radius(little);

            // Anticlockwise seen from the palm's side with the thumb to the right: up the thumb's side, across
            // the knuckles, down the little finger's side and back along the wrist.
            var outline = new Corner[PalmOutline]
            {
                new Corner(wrist, wrist, WristHalfWidth - 0.0015f, -0.003f),             // the wrist's corner, thumb's side
                new Corner(thumb, thumb, 0.010f, -0.013f),                               // round the thumb's base
                new Corner(thumb, thumb, 0.015f, 0.010f),
                new Corner(thumb, index, 0.012f, 0.004f),                                // the web
                new Corner(index, index, indexRadius + 0.0015f, -0.004f),                // beside the index knuckle
                new Corner(index, index, 0.0025f, PalmRim),                              // beyond the four knuckles
                new Corner(middle, middle, 0f, PalmRim + 0.0005f),
                new Corner(ring, ring, -0.0005f, PalmRim + 0.0005f),
                new Corner(little, little, -0.002f, PalmRim),
                new Corner(little, little, -(littleRadius + 0.0015f), -0.003f),          // beside the little knuckle
                new Corner(wrist, little, -0.0255f, 0.003f),                             // the heel of the hand
                new Corner(wrist, wrist, -(WristHalfWidth - 0.0015f), -0.003f),          // the wrist's corner, little finger's side
                new Corner(wrist, wrist, 0f, -0.005f),
            };

            // The palm's rotation in the open model hand: what HandsSkin calls the palm frame.
            float3 origin = HandModel.Rest(HandSide.Right, wrist);
            float3 forward = math.normalize(HandModel.Rest(HandSide.Right, middle) - origin);
            float3 up = new float3(0f, 1f, 0f);
            up = math.normalize(up - math.dot(up, forward) * forward);
            float3 side = math.cross(up, forward);
            float knuckles = HandModel.Rest(HandSide.Right, middle).z - origin.z;

            const int n = PalmOutline;
            var point = new float3[3 * n + 2];       // rim, palm face, back face, then the two centres
            var anchor = new int[3 * n + 2];
            var plan = new float2[n];
            for (int i = 0; i < n; i++)
            {
                float3 a = Anchor(outline[i].Anchor, outline[i].Other);
                plan[i] = new float2(a.x + outline[i].X, a.z + outline[i].Z);
            }
            for (int i = 0; i < n; i++)
            {
                // The faces stop PalmBevel inside the rim: along the corner's bisector, towards the inside.
                float2 into = Left(math.normalize(plan[i] - plan[(i + n - 1) % n]));
                float2 out2 = Left(math.normalize(plan[(i + 1) % n] - plan[i]));
                float2 inward = math.normalize(into + out2);
                float2 face = plan[i] + inward * (PalmBevel / math.max(math.dot(inward, into), 0.5f));
                float half = math.lerp(HandModel.PalmHalfThicknessWrist, HandModel.PalmHalfThicknessKnuckles,
                                       math.saturate((face.y - origin.z) / knuckles));
                int frame = AnchorFrame(frames, outline[i].Anchor, outline[i].Other);
                point[i] = new float3(plan[i].x, origin.y, plan[i].y);
                point[n + i] = new float3(face.x, origin.y + half, face.y);
                point[2 * n + i] = new float3(face.x, origin.y - half, face.y);
                anchor[i] = anchor[n + i] = anchor[2 * n + i] = frame;
            }

            // The middle of each face hangs halfway between the wrist and the middle knuckle; the palm's is hollow.
            int centreFrame = AnchorFrame(frames, wrist, middle);
            float3 centre = Anchor(wrist, middle) + new float3(-0.0045f, 0f, 0f);
            float centreHalf = math.lerp(HandModel.PalmHalfThicknessWrist, HandModel.PalmHalfThicknessKnuckles,
                                         math.saturate((centre.z - origin.z) / knuckles));
            int palmCentre = 3 * n, backCentre = 3 * n + 1;
            point[palmCentre] = new float3(centre.x, origin.y + centreHalf - PalmHollow, centre.z);
            point[backCentre] = new float3(centre.x, origin.y - centreHalf, centre.z);
            anchor[palmCentre] = anchor[backCentre] = centreFrame;

            // Two fans and the two bands of the bevel, six triangles a corner.
            int first = vertices.Count;
            var triangles = new List<int>(18 * n);
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int rim = i, rimNext = next, top = n + i, topNext = n + next, bottom = 2 * n + i, bottomNext = 2 * n + next;
                AddTriangle(triangles, palmCentre, topNext, top);
                AddTriangle(triangles, rim, top, topNext);
                AddTriangle(triangles, rim, topNext, rimNext);
                AddTriangle(triangles, rim, rimNext, bottomNext);
                AddTriangle(triangles, rim, bottomNext, bottom);
                AddTriangle(triangles, backCentre, bottom, bottomNext);
            }

            // Smooth normals: at each vertex the mean of the faces that meet there.
            var normal = new float3[point.Length];
            for (int t = 0; t < triangles.Count; t += 3)
            {
                float3 a = point[triangles[t]], b = point[triangles[t + 1]], c = point[triangles[t + 2]];
                float3 face = math.normalize(math.cross(b - a, c - a));
                normal[triangles[t]] += face;
                normal[triangles[t + 1]] += face;
                normal[triangles[t + 2]] += face;
            }
            for (int i = 0; i < point.Length; i++)
                vertices.Add(Bound(point[i], math.normalize(normal[i]), anchor[i], frames, side, up, forward));
            for (int t = 0; t < triangles.Count; t++) indices.Add(first + triangles[t]);

            // The cuff: an ellipse about the model's forearm line, starting inside the palm and closed behind
            // the wrist by a nearly flat end. Nothing tracks the elbow, so there is no forearm.
            int wristFrame = AnchorFrame(frames, wrist, wrist);
            int inside = AddCuffRing(vertices, rings, frames, wristFrame, CuffInside, 0.75f * WristHalfWidth, 0.63f * WristHalfThickness,
                                     0f, side, up, forward);
            int front = AddCuffRing(vertices, rings, frames, wristFrame, CuffFront, WristHalfWidth, WristHalfThickness,
                                    0f, side, up, forward);
            int back = AddCuffRing(vertices, rings, frames, wristFrame, CuffBack, WristHalfWidth, WristHalfThickness,
                                   math.radians(45f), side, up, forward);
            int end = vertices.Count;
            vertices.Add(Bound(origin + new float3(0f, 0f, CuffEnd), new float3(0f, 0f, -1f), wristFrame, frames, side, up, forward));
            AddBand(indices, front, inside, CuffSides);
            AddBand(indices, back, front, CuffSides);
            for (int i = 0; i < CuffSides; i++) AddTriangle(indices, back + (i + 1) % CuffSides, back + i, end);
        }

        // The palm's frame at one anchor joint, or halfway between two: one frame for all the vertices that share it.
        static int AnchorFrame(List<Frame> frames, int joint, int other)
        {
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].Kind == FrameKind.Anchor && frames[i].Joint == joint && frames[i].Other == other) return i;
            return AddFrame(frames, FrameKind.Anchor, joint, joint, other, other == joint ? 0f : 0.5f);
        }

        // Where a corner hangs from in the open model hand: an anchor joint, or the middle of two.
        static float3 Anchor(int joint, int other)
        {
            return 0.5f * (HandModel.Rest(HandSide.Right, joint) + HandModel.Rest(HandSide.Right, other));
        }

        // The direction to the left of a direction of travel, in the plan of the palm (x, z): the inside of an
        // outline that runs anticlockwise.
        static float2 Left(float2 travel)
        {
            return new float2(-travel.y, travel.x);
        }

        // A point and a normal of the open model hand written in the palm's rotation at the frame's anchor.
        static Vertex Bound(float3 position, float3 normal, int frame, List<Frame> frames, float3 side, float3 up, float3 forward)
        {
            float3 offset = position - Anchor(frames[frame].Joint, frames[frame].Other);
            return new Vertex
            {
                Position = new float3(math.dot(offset, side), math.dot(offset, up), math.dot(offset, forward)),
                Normal = new float3(math.dot(normal, side), math.dot(normal, up), math.dot(normal, forward)),
                Frame = frame,
            };
        }

        // An ellipse about the model's z axis at height along; its normals lean back by tilt radians.
        static int AddCuffRing(List<Vertex> vertices, List<Ring> rings, List<Frame> frames, int frame, float along,
                               float halfWidth, float halfThickness, float tilt, float3 side, float3 up, float3 forward)
        {
            int first = vertices.Count;
            float3 origin = HandModel.Rest(HandSide.Right, (int)HandJoint.Wrist);
            math.sincos(tilt, out float lean, out float flat);
            for (int i = 0; i < CuffSides; i++)
            {
                math.sincos(2f * math.PI * i / CuffSides, out float sin, out float cos);
                float2 outwards = math.normalize(new float2(cos / halfWidth, sin / halfThickness));
                vertices.Add(Bound(origin + new float3(halfWidth * cos, halfThickness * sin, along),
                                   new float3(flat * outwards.x, flat * outwards.y, -lean), frame, frames, side, up, forward));
            }
            Vertex centre = Bound(origin + new float3(0f, 0f, along), new float3(0f, 0f, 1f), frame, frames, side, up, forward);
            rings.Add(new Ring { First = first, Count = CuffSides, Frame = frame, Centre = centre.Position, Radius = 0f });
            return first;
        }
    }
}
