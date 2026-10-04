using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// What the tests of the hand mesh share: poses the template hand can take, hands whose bones are not the
    /// template's, and a vertex buffer with guard entries behind its end.
    /// </summary>
    internal static unsafe class HandsSkinRig
    {
        /// <summary>Entries after the last vertex that the skin must leave alone.</summary>
        public const int Guard = 8;
        const uint GuardColor = 0xDEADBEEFu;

        /// <summary>The colour the hands are drawn in.</summary>
        public static readonly float2 Uv = AtlasPalette.Uv(AtlasPalette.AvatarBody);
        /// <summary>The palm's normal in the palm frame of either hand.</summary>
        public static readonly float3 PalmUp = new float3(0f, 1f, 0f);

        /// <summary>Skins into a fresh buffer and checks that nothing was written behind the template's vertices.</summary>
        public static AtlasVertex[] Skin(HandsMeshTemplate template, in JointSet joints, float3 palmNormal,
                                         out float3 boundsMin, out float3 boundsMax)
        {
            return Skin(template, in joints, palmNormal, AtlasShading.LightDirection, out boundsMin, out boundsMax);
        }

        public static AtlasVertex[] Skin(HandsMeshTemplate template, in JointSet joints, float3 palmNormal, float3 light,
                                         out float3 boundsMin, out float3 boundsMax)
        {
            var vertices = new AtlasVertex[template.VertexCount + Guard];
            for (int i = template.VertexCount; i < vertices.Length; i++) vertices[i].Color = GuardColor;
            fixed (AtlasVertex* p = vertices)
                HandsSkin.Skin(template, in joints, palmNormal, Uv, light, p, out boundsMin, out boundsMax);
            for (int i = template.VertexCount; i < vertices.Length; i++)
                Assert.That(vertices[i].Color, Is.EqualTo(GuardColor), "wrote past the vertex buffer");
            return vertices;
        }

        /// <summary>The template's triangles as a managed array.</summary>
        public static int[] Indices(HandsMeshTemplate template)
        {
            var indices = new int[template.IndexCount + Guard];
            for (int i = template.IndexCount; i < indices.Length; i++) indices[i] = -77;
            fixed (int* p = indices) template.CopyIndices(p, template.IndexCount);
            for (int i = template.IndexCount; i < indices.Length; i++)
                Assert.That(indices[i], Is.EqualTo(-77), "wrote past the index buffer");
            return indices;
        }

        /// <summary>
        /// The template hand in its palm frame with every joint of the four fingers bent towards the palm by its
        /// own angle in degrees (knuckle, middle joint, end joint), and the three joints of the thumb by thumb
        /// degrees each. Bone lengths are the template's times scale. HandModel.Pose stops at 80 degrees at the
        /// knuckle; a fist seen through a noisy lift does not.
        /// </summary>
        public static JointSet Curled(HandSide side, float knuckle, float middle, float end, float thumb = 0f, float scale = 1f)
        {
            JointSet set = default(JointSet);
            set[0] = float3.zero;
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int joint = HandJoints.Base(finger);
                float3 position = HandModel.Rest(HandSide.Right, joint);
                float3 direction = math.normalize(HandModel.Rest(HandSide.Right, joint + 3) - position);
                float3 axis = math.normalize(math.cross(direction, PalmUp));
                float3 bends = math.radians(finger == 0 ? new float3(thumb) : new float3(knuckle, middle, end));
                set[joint] = Sided(side, position) * scale;
                float angle = 0f;
                for (int bone = 0; bone < 3; bone++)
                {
                    angle += bends[bone];
                    position += math.mul(quaternion.AxisAngle(axis, angle), direction) * HandModel.BoneLength(joint + 1 + bone);
                    set[joint + 1 + bone] = Sided(side, position) * scale;
                }
            }
            return set;
        }

        /// <summary>A fist as HandModel curls it: 80, 100 and 70 degrees, the thumb folded over.</summary>
        public static JointSet Fist(HandSide side, float scale = 1f)
        {
            return HandModel.Pose(side, new float4(1f, 1f, 1f, 1f), 1f, scale);
        }

        /// <summary>One finger out, the others curled: the pose a press is made with.</summary>
        public static JointSet Pointing(HandSide side, float scale = 1f)
        {
            return HandModel.Pose(side, new float4(0f, 1f, 1f, 1f), 0.8f, scale);
        }

        /// <summary>The thumb's tip brought onto the index finger's: the curls that bring the two tips closest, then the tips joined.</summary>
        public static JointSet Pinch(HandSide side, float scale = 1f)
        {
            float best = float.MaxValue;
            JointSet pose = default(JointSet);
            for (int i = 0; i <= 20; i++)
            {
                for (int t = 0; t <= 20; t++)
                {
                    JointSet candidate = HandModel.Pose(side, new float4(i / 20f, 0.25f, 0.2f, 0.15f), t / 20f, scale);
                    float gap = math.distance(candidate[HandJoint.ThumbTip], candidate[HandJoint.IndexTip]);
                    if (gap < best)
                    {
                        best = gap;
                        pose = candidate;
                    }
                }
            }
            float3 meet = 0.5f * (pose[HandJoint.ThumbTip] + pose[HandJoint.IndexTip]);
            pose[HandJoint.ThumbTip] = meet;
            pose[HandJoint.IndexTip] = meet;
            return pose;
        }

        /// <summary>
        /// The same pose with every bone made longer or shorter by its own factor between 0.8 and 1.25, as a lift
        /// that has not learnt the walker's bones yet leaves them. The same seed gives the same hand.
        /// </summary>
        public static JointSet Stretched(in JointSet pose, uint seed)
        {
            var random = new Random(seed);
            JointSet set = default(JointSet);
            set[0] = pose[0];
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                int parent = HandJoints.Parent(joint);
                set[joint] = set[parent] + (pose[joint] - pose[parent]) * random.NextFloat(0.8f, 1.25f);
            }
            return set;
        }

        /// <summary>A pose as the room sees it: turned and moved.</summary>
        public static JointSet Placed(in JointSet pose, quaternion rotation, float3 translation)
        {
            return pose.Transformed(rotation, translation);
        }

        /// <summary>A rotation that lines up with no axis, and a place in the room.</summary>
        public static readonly quaternion Turn = math.normalize(quaternion.EulerXYZ(new float3(0.7f, -1.9f, 0.35f)));
        public static readonly float3 Place = new float3(0.6f, 1.25f, -1.4f);

        static float3 Sided(HandSide side, float3 p)
        {
            return side == HandSide.Left ? new float3(-p.x, p.y, p.z) : p;
        }

        public static bool IsFinite(float3 v)
        {
            return math.all(math.isfinite(v));
        }

        /// <summary>Six times the volume the triangles enclose, measured from a point: positive when they are wound outwards.</summary>
        public static double SignedVolume(AtlasVertex[] vertices, int[] indices, int indexCount, float3 about)
        {
            double six = 0.0;
            for (int t = 0; t < indexCount; t += 3)
            {
                double3 a = vertices[indices[t]].Position - about;
                double3 b = vertices[indices[t + 1]].Position - about;
                double3 c = vertices[indices[t + 2]].Position - about;
                six += math.dot(a, math.cross(b, c));
            }
            return six / 6.0;
        }
    }
}
