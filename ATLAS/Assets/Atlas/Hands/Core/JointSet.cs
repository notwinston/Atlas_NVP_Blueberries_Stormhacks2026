using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The 21 joints of one hand as plain data: 63 floats, indexed by <see cref="HandJoint"/>. A value type with
    /// no reference inside, so it is copied, kept in arrays and passed with in and ref without allocating.
    /// The frame the positions are in (camera at capture, world) is the owner's business.
    /// </summary>
    public unsafe struct JointSet
    {
        fixed float _v[HandJoints.Count * 3];

        public float3 this[int joint]
        {
            get
            {
                if ((uint)joint >= HandJoints.Count) return float3.zero;
                fixed (float* p = _v) return new float3(p[joint * 3], p[joint * 3 + 1], p[joint * 3 + 2]);
            }
            set
            {
                if ((uint)joint >= HandJoints.Count) return;
                fixed (float* p = _v)
                {
                    p[joint * 3] = value.x;
                    p[joint * 3 + 1] = value.y;
                    p[joint * 3 + 2] = value.z;
                }
            }
        }

        public float3 this[HandJoint joint]
        {
            get { return this[(int)joint]; }
            set { this[(int)joint] = value; }
        }

        /// <summary>Every joint moved by the rigid transform: rotation, then translation.</summary>
        public JointSet Transformed(quaternion rotation, float3 translation)
        {
            JointSet result = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++) result[i] = translation + math.mul(rotation, this[i]);
            return result;
        }

        /// <summary>Every joint moved by the matrix (a point transform).</summary>
        public JointSet Transformed(in float4x4 matrix)
        {
            JointSet result = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++) result[i] = math.transform(matrix, this[i]);
            return result;
        }

        /// <summary>True when every coordinate is finite.</summary>
        public bool IsFinite()
        {
            fixed (float* p = _v)
            {
                for (int i = 0; i < HandJoints.Count * 3; i++)
                    if (float.IsNaN(p[i]) || float.IsInfinity(p[i])) return false;
            }
            return true;
        }
    }
}
