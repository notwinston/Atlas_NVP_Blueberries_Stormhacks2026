using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>A depth source that needs to be told where the camera is, because it has no sensor of its own.</summary>
    public interface IPosedDepthSource
    {
        /// <summary>The tracked camera's world pose of this frame. Called by the pose pipeline before TryRead.</summary>
        void SetCameraPose(float3 position, quaternion rotation);
    }

    /// <summary>
    /// Depth with no phone (contract README F9): rays cast from the camera against the active RoomModel's
    /// walls and object boxes, plus boxes added in code, into a small image at 15 Hz. It lets Bump Guard's
    /// live layer be exercised in the Editor. Engine-free.
    /// </summary>
    public sealed class SyntheticDepthSource : IDepthSource, IPosedDepthSource
    {
        public const int ImageWidth = 64;
        public const int ImageHeight = 48;
        /// <summary>Images per second.</summary>
        public const float Rate = 15f;
        /// <summary>Radians: horizontal field of view, close to the phone's wide camera.</summary>
        public const float HorizontalFov = 1.2f;
        public const int MaxExtraBoxes = 16;

        struct Box
        {
            public float3 Base;
            public float3 Size;
            public float Sin, Cos;
        }

        readonly float[] _depth = new float[ImageWidth * ImageHeight];
        readonly Box[] _extra = new Box[MaxExtraBoxes];
        int _extraCount;
        Box[] _roomBoxes = new Box[0];
        RoomModel _room;
        float3 _position;
        quaternion _rotation = quaternion.identity;
        bool _havePose;
        double _lastTime = double.NegativeInfinity;
        int _sequence;

        public bool Available { get { return true; } }

        /// <summary>The room whose walls and objects are seen; null for none.</summary>
        public RoomModel Room
        {
            get { return _room; }
            set
            {
                _room = value;
                RoomObject[] objects = value != null && value.Objects != null ? value.Objects : new RoomObject[0];
                _roomBoxes = new Box[objects.Length];
                for (int i = 0; i < objects.Length; i++)
                    _roomBoxes[i] = MakeBox(objects[i].Base, objects[i].Size, objects[i].Yaw);
            }
        }

        public int ExtraBoxCount { get { return _extraCount; } }

        /// <summary>An obstacle that is not in the RoomModel: a stool put down after the scan. baseCentre is the
        /// centre of its footprint on the floor; size is (x, height, z). False when the list is full.</summary>
        public bool AddBox(float3 baseCentre, float3 size, float yaw = 0f)
        {
            if (_extraCount >= MaxExtraBoxes) return false;
            _extra[_extraCount++] = MakeBox(baseCentre, size, yaw);
            return true;
        }

        public void ClearBoxes()
        {
            _extraCount = 0;
        }

        public void SetCameraPose(float3 position, quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
            _havePose = true;
        }

        public bool TryRead(double time, out DepthFrame frame)
        {
            frame = default(DepthFrame);
            if (!_havePose) return false;
            if (time - _lastTime < 1.0 / Rate - 1e-4 && time >= _lastTime) return false;
            _lastTime = time;

            float fx = 0.5f * ImageWidth / math.tan(0.5f * HorizontalFov);
            float cx = 0.5f * ImageWidth, cy = 0.5f * ImageHeight;
            float3 right = math.mul(_rotation, new float3(1f, 0f, 0f));
            float3 up = math.mul(_rotation, new float3(0f, 1f, 0f));
            float3 forward = math.mul(_rotation, new float3(0f, 0f, 1f));
            RoomWall[] walls = _room != null && _room.Walls != null ? _room.Walls : null;

            for (int v = 0; v < ImageHeight; v++)
            {
                float cv = -(v + 0.5f - cy) / fx;
                for (int u = 0; u < ImageWidth; u++)
                {
                    float cu = (u + 0.5f - cx) / fx;
                    // A direction with unit length along the camera axis: the ray parameter is the depth.
                    float3 dir = forward + right * cu + up * cv;
                    float best = float.PositiveInfinity;
                    if (walls != null)
                        for (int w = 0; w < walls.Length; w++) HitWall(walls[w], dir, ref best);
                    for (int b = 0; b < _roomBoxes.Length; b++) HitBox(in _roomBoxes[b], dir, ref best);
                    for (int b = 0; b < _extraCount; b++) HitBox(in _extra[b], dir, ref best);
                    _depth[v * ImageWidth + u] = DepthFrame.IsReturn(best) ? best : 0f;
                }
            }

            _sequence++;
            frame.Depth = _depth;
            frame.Width = ImageWidth;
            frame.Height = ImageHeight;
            frame.Fx = fx;
            frame.Fy = fx;
            frame.Cx = cx;
            frame.Cy = cy;
            frame.Position = _position;
            frame.Rotation = _rotation;
            frame.Time = time;
            frame.Sequence = _sequence;
            return true;
        }

        static Box MakeBox(float3 baseCentre, float3 size, float yaw)
        {
            return new Box { Base = baseCentre, Size = size, Sin = math.sin(yaw), Cos = math.cos(yaw) };
        }

        void HitWall(RoomWall wall, float3 dir, ref float best)
        {
            if (wall == null) return;
            // The wall is the segment A-B on the floor, raised to its height.
            float2 a = wall.A, e = wall.B - wall.A;
            float2 o = _position.xz, d = dir.xz;
            float det = d.x * e.y - d.y * e.x;
            if (math.abs(det) < 1e-9f) return;
            float2 ao = a - o;
            float t = (ao.x * e.y - ao.y * e.x) / det;
            float s = (ao.x * d.y - ao.y * d.x) / det;
            if (t <= 0f || t >= best || s < 0f || s > 1f) return;
            float y = _position.y + dir.y * t;
            if (y < 0f || y > wall.Height) return;
            best = t;
        }

        void HitBox(in Box box, float3 dir, ref float best)
        {
            // Into the box's own frame: yaw carries local +X to (cos, -sin) and local +Z to (sin, cos) in (x, z).
            float3 rel = _position - box.Base;
            float3 o = new float3(rel.x * box.Cos - rel.z * box.Sin, rel.y, rel.x * box.Sin + rel.z * box.Cos);
            float3 d = new float3(dir.x * box.Cos - dir.z * box.Sin, dir.y, dir.x * box.Sin + dir.z * box.Cos);
            float3 lo = new float3(-0.5f * box.Size.x, 0f, -0.5f * box.Size.z);
            float3 hi = new float3(0.5f * box.Size.x, box.Size.y, 0.5f * box.Size.z);

            float tMin = 0f, tMax = best;
            for (int axis = 0; axis < 3; axis++)
            {
                float oa = o[axis], da = d[axis];
                if (math.abs(da) < 1e-9f)
                {
                    if (oa < lo[axis] || oa > hi[axis]) return;
                    continue;
                }
                float t0 = (lo[axis] - oa) / da, t1 = (hi[axis] - oa) / da;
                if (t0 > t1) { float swap = t0; t0 = t1; t1 = swap; }
                if (t0 > tMin) tMin = t0;
                if (t1 < tMax) tMax = t1;
                if (tMin > tMax) return;
            }
            if (tMin > 0f && tMin < best) best = tMin;
        }
    }
}
