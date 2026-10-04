using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// Poses a <see cref="HandsMeshTemplate"/> on 21 joints and writes vertices of the look, shade baked
    /// (documents/ATLAS_Hands_Design.md section 5.2). The detector gives joint positions and no rotations, so
    /// every frame is built from positions alone.
    ///
    /// The kernel is static, pointer-based and allocation-free (README F2), as the Slicer's is: the headless
    /// tests run the same code the phone does, and a Burst job could call it with the template's arrays pinned.
    /// The overload that takes a native array is what the hands' view calls in the upload step.
    /// </summary>
    public static unsafe class HandsSkin
    {
        /// <summary>
        /// A joint farther than this from the origin on any axis, or not finite, cannot be drawn: metres. Every
        /// vertex then goes to one point, so nothing is drawn and no bound is ever NaN.
        /// </summary>
        internal const float MaxCoordinate = 1e4f;
        /// <summary>
        /// A bone shorter than this times the hand's scale has no direction of its own: it keeps the axis of the
        /// bone before it. Metres.
        /// </summary>
        internal const float MinBoneMetres = 0.002f;
        /// <summary>
        /// Two bones whose axes are more opposed than this cosine are folded back on each other: the frame turns
        /// half round about its side axis.
        /// </summary>
        internal const float OpposedCosine = -0.99f;

        // One posed frame: unit axes, where it is, and how far a metre of the template reaches sideways and along.
        struct Posed
        {
            public float3 Origin, X, Y, Z;
            public float Lateral, Axial;
        }

        /// <summary>
        /// Writes template.VertexCount vertices in the frame the joints are in (the room's, for a drawn hand).
        /// The hand's size is the joints' own: radii follow the measured length from the wrist to the middle
        /// knuckle. palmNormal is unit length and points out of the palm; light is the unit direction towards
        /// the look's light in the same frame. Every vertex gets the two-sided baked shade, no line, the palette
        /// coordinate given. Bounds are the joints grown by the template's largest radius. Allocates nothing;
        /// any finite joints give finite vertices, and joints that are not finite collapse the mesh to a point.
        /// </summary>
        public static void Skin(HandsMeshTemplate template, in JointSet joints, float3 palmNormal, float2 paletteUv, float3 light,
                                NativeArray<AtlasVertex> vertices, out float3 boundsMin, out float3 boundsMax)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!vertices.IsCreated || vertices.Length < template.VertexCount)
                throw new ArgumentException("The vertex buffer needs room for the template's VertexCount vertices.", nameof(vertices));
            Skin(template, in joints, palmNormal, paletteUv, light, (AtlasVertex*)NativeArrayUnsafeUtility.GetUnsafePtr(vertices),
                 out boundsMin, out boundsMax);
        }

        /// <summary>The same into raw memory with room for template.VertexCount vertices.</summary>
        internal static void Skin(HandsMeshTemplate template, in JointSet joints, float3 palmNormal, float2 paletteUv, float3 light,
                                  AtlasVertex* vertices, out float3 boundsMin, out float3 boundsMax)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));

            JointSet copy = joints;
            float3* joint = stackalloc float3[HandJoints.Count];
            for (int i = 0; i < HandJoints.Count; i++) joint[i] = copy[i];

            fixed (HandsMeshTemplate.Vertex* templateVertices = template.Vertices)
            fixed (HandsMeshTemplate.Frame* frames = template.Frames)
            fixed (float* boneLength = template.BoneLengths)
            {
                Kernel(templateVertices, template.Vertices.Length, frames, template.Frames.Length,
                       boneLength, template.PalmLength, template.Reach, template.ThumbTurn,
                       joint, palmNormal, paletteUv, light, vertices, out boundsMin, out boundsMax);
            }
        }

        /// <summary>
        /// The skin itself, on plain memory: vertexCount template vertices and frameCount frames (at most
        /// HandsMeshTemplate.MaxFrames), the template's bone length for each of the 21 joints, 21 joints.
        ///
        /// 1. The palm's frame: z from the wrist to the middle knuckle, y the palm's normal made perpendicular.
        /// 2. A frame per bone. The first bone of each chain runs from the wrist to the chain's base: its up is
        ///    the palm's normal made perpendicular to it, which is always well defined because the bases lie in
        ///    the palm (the thumb's up is that normal turned by thumbTurn about the line from the wrist to the
        ///    index knuckle). Every later bone takes the frame before it turned by the smallest rotation that
        ///    carries the old axis onto the new: fingers bend and do not twist. The normal is never made
        ///    perpendicular to a finger's own bone, which runs along it when a fist is closed.
        /// 3. A frame per ring: its bone's frame at the joint, turned towards the bone across the joint by the
        ///    ring's weight. A ring keeps its radius; its distance from the joint shrinks with a bone that
        ///    measures shorter than the template's, so rings land on the joints and never pass each other.
        /// 4. Vertices.
        /// </summary>
        internal static void Kernel(
            HandsMeshTemplate.Vertex* template, int vertexCount,
            HandsMeshTemplate.Frame* frames, int frameCount,
            float* boneLength, float palmLength, float reach, float thumbTurn,
            float3* joint, float3 palmNormal, float2 paletteUv, float3 light,
            AtlasVertex* vertices, out float3 boundsMin, out float3 boundsMax)
        {
            if (!math.all(math.isfinite(light))) light = float3.zero;

            bool drawable = frameCount <= HandsMeshTemplate.MaxFrames;
            float3 lo = joint[0], hi = joint[0];
            for (int i = 0; i < HandJoints.Count; i++)
            {
                float3 p = joint[i];
                drawable &= math.all(math.abs(p) <= MaxCoordinate);     // false for NaN and for infinity
                lo = math.min(lo, p);
                hi = math.max(hi, p);
            }
            float3 wrist = joint[(int)HandJoint.Wrist];
            if (!drawable)
            {
                Collapse(vertices, vertexCount, math.all(math.abs(wrist) <= MaxCoordinate) ? wrist : float3.zero,
                         paletteUv, light, out boundsMin, out boundsMax);
                return;
            }

            // The hand's size is its own: the joints are already at the walker's scale.
            float3 palm = joint[(int)HandJoint.MiddleMcp] - wrist;
            float palmMetres = math.length(palm);
            float scale = palmMetres / palmLength;
            float shortest = math.max(MinBoneMetres * scale, 1e-9f);

            // 1. The palm.
            float3 indexLine = joint[(int)HandJoint.IndexMcp] - wrist;
            float sign = thumbTurn < 0f ? -1f : 1f;
            float3 normal = Unit(palmNormal, sign * math.cross(joint[(int)HandJoint.LittleMcp] - wrist, indexLine));
            float3 forward = palmMetres > shortest ? palm / palmMetres : AnyPerpendicular(normal);
            float3 palmY = Perpendicular(normal, forward);
            float3 palmX = math.cross(palmY, forward);

            // 2. The bones. half[j] is the direction halfway round the bend at joint j.
            float3* boneZ = stackalloc float3[HandJoints.Count];
            float3* boneY = stackalloc float3[HandJoints.Count];
            float3* half = stackalloc float3[HandJoints.Count];
            float* boneScale = stackalloc float[HandJoints.Count];

            float indexMetres = math.length(indexLine);
            float3 turnAxis = indexMetres > shortest ? indexLine / indexMetres : forward;
            math.sincos(thumbTurn, out float sin, out float cos);
            float3 thumbUp = normal * cos + math.cross(turnAxis, normal) * sin + turnAxis * (math.dot(turnAxis, normal) * (1f - cos));

            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int j = HandJoints.Base(finger);
                float3 bone = joint[j] - wrist;
                float metres = math.length(bone);
                bool real = metres > shortest;
                float3 z = real ? bone / metres : forward;
                float3 y = Perpendicular(finger == 0 ? thumbUp : normal, z);
                boneZ[j] = z;
                boneY[j] = y;
                boneScale[j] = real ? metres / boneLength[j] : 0f;

                for (int last = j + HandJoints.PerFinger - 1; j < last; j++)
                {
                    bone = joint[j + 1] - joint[j];
                    metres = math.length(bone);
                    real = metres > shortest;
                    float3 next = real ? bone / metres : z;
                    float cosine = math.dot(z, next);
                    if (cosine < OpposedCosine)
                    {
                        // Folded back: no smallest rotation exists. Half a turn about the side axis, the way a
                        // finger curls; halfway round is the old up.
                        half[j] = y;
                        y = Perpendicular(-y, next);
                    }
                    else
                    {
                        half[j] = math.normalize(z + next);
                        y = Carry(y, z, next, cosine);
                    }
                    z = next;
                    boneZ[j + 1] = z;
                    boneY[j + 1] = y;
                    boneScale[j + 1] = real ? metres / boneLength[j + 1] : 0f;
                }
            }

            // 3. The frames the template's vertices hang from.
            Posed* posed = stackalloc Posed[HandsMeshTemplate.MaxFrames];
            for (int f = 0; f < frameCount; f++)
            {
                HandsMeshTemplate.Frame frame = frames[f];
                Posed p;
                if (frame.Kind == HandsMeshTemplate.FrameKind.Ring)
                {
                    float3 z = boneZ[frame.Bone], y = boneY[frame.Bone];
                    if (frame.Weight > 0f)
                    {
                        // Towards the halfway direction of the bend by twice the weight: a weight of a quarter
                        // turns the ring a quarter of the bend, exactly.
                        float3 turned = math.normalize(math.lerp(z, half[frame.Joint], 2f * frame.Weight));
                        y = Carry(y, z, turned, math.dot(z, turned));
                        z = turned;
                    }
                    p.Origin = joint[frame.Joint];
                    p.X = math.cross(y, z);
                    p.Y = y;
                    p.Z = z;
                    p.Lateral = scale;
                    p.Axial = math.min(scale, boneScale[frame.Bone]);
                }
                else
                {
                    p.Origin = math.lerp(joint[frame.Joint], joint[frame.Other], frame.Weight);
                    p.X = palmX;
                    p.Y = palmY;
                    p.Z = forward;
                    p.Lateral = scale;
                    p.Axial = scale;
                }
                posed[f] = p;
            }

            // 4. The vertices.
            for (int i = 0; i < vertexCount; i++)
            {
                HandsMeshTemplate.Vertex* source = template + i;
                Posed* p = posed + source->Frame;
                float3 local = source->Position, localNormal = source->Normal;
                float3 n = localNormal.x * p->X + localNormal.y * p->Y + localNormal.z * p->Z;

                AtlasVertex* vertex = vertices + i;
                vertex->Position = p->Origin + p->Lateral * (local.x * p->X + local.y * p->Y) + (p->Axial * local.z) * p->Z;
                vertex->Normal = n;
                vertex->Color = AtlasVertex.PackColor(ShadeTwoSided(n, light), 0);
                vertex->Uv0 = paletteUv;
                vertex->Uv1 = float2.zero;
            }

            // No vertex is farther than the scaled reach from the joint, or the line between two joints, it hangs
            // from. The second term is the rounding of a position far from the origin.
            float grow = scale * reach * 1.00001f + 1e-6f * (1f + math.cmax(math.max(math.abs(lo), math.abs(hi))));
            boundsMin = lo - grow;
            boundsMax = hi + grow;
        }

        /// <summary>The byte of AtlasShading.FaceShadeTwoSided with the light passed in: what the Slicer bakes into its moving mesh.</summary>
        internal static byte ShadeTwoSided(float3 normal, float3 light)
        {
            float shade = AtlasShading.ShadeFloor
                        + (1f - AtlasShading.ShadeFloor) * (0.5f + 0.5f * math.abs(math.dot(normal, light)));
            return (byte)math.round(255f * math.saturate(shade));
        }

        // Every vertex at one point: triangles with no area, bounds with no size.
        static void Collapse(AtlasVertex* vertices, int vertexCount, float3 at, float2 paletteUv, float3 light,
                             out float3 boundsMin, out float3 boundsMax)
        {
            AtlasVertex vertex;
            vertex.Position = at;
            vertex.Normal = new float3(0f, 1f, 0f);
            vertex.Color = AtlasVertex.PackColor(ShadeTwoSided(vertex.Normal, light), 0);
            vertex.Uv0 = paletteUv;
            vertex.Uv1 = float2.zero;
            for (int i = 0; i < vertexCount; i++) vertices[i] = vertex;
            boundsMin = at;
            boundsMax = at;
        }

        // up carried from a frame whose axis is from to the frame whose axis is to, by the smallest rotation that
        // takes one axis onto the other (Rodrigues' formula with no angle). cosine = dot(from, to) > -1.
        static float3 Carry(float3 up, float3 from, float3 to, float cosine)
        {
            float3 v = math.cross(from, to);
            float3 carried = up + math.cross(v, up) + math.cross(v, math.cross(v, up)) / (1f + cosine);
            // Square it up against the new axis, so the error of one bone is not handed to the next.
            carried -= math.dot(carried, to) * to;
            return math.normalize(carried);
        }

        // up made perpendicular to a unit axis; any perpendicular when up runs along the axis.
        static float3 Perpendicular(float3 up, float3 axis)
        {
            float3 across = up - math.dot(up, axis) * axis;
            float lengthSq = math.lengthsq(across);
            return lengthSq > 1e-6f ? across * math.rsqrt(lengthSq) : AnyPerpendicular(axis);
        }

        // A unit vector perpendicular to a unit axis: the room axis the axis leans on least, squared up.
        static float3 AnyPerpendicular(float3 axis)
        {
            float3 a = math.abs(axis);
            float3 other = a.x <= a.y && a.x <= a.z ? new float3(1f, 0f, 0f)
                         : a.y <= a.z ? new float3(0f, 1f, 0f) : new float3(0f, 0f, 1f);
            return math.normalize(other - math.dot(other, axis) * axis);
        }

        // v at unit length; the fallback at unit length when v has no usable length; straight up when neither has.
        static float3 Unit(float3 v, float3 fallback)
        {
            float lengthSq = math.lengthsq(v);
            if (lengthSq > 1e-12f && lengthSq < 1e12f) return v * math.rsqrt(lengthSq);
            // The fallback is a product of two differences of joints: tiny for a tiny hand, so it is sized first.
            float largest = math.cmax(math.abs(fallback));
            if (largest > 0f && largest < float.PositiveInfinity)
            {
                fallback /= largest;
                return fallback * math.rsqrt(math.lengthsq(fallback));
            }
            return new float3(0f, 1f, 0f);
        }
    }
}
