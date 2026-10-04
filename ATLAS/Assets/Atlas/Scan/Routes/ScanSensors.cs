using System;
using System.Collections.Generic;
using Atlas.Scan.Grid;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// What the AR managers hold during a scan, copied into plain arrays in the AR session frame (Master Plan
    /// section 4.2): the LiDAR mesh with a class per face (route 2), the planes for the floor height, the
    /// wall directions and the wall height, and the furniture boxes (route 3). A thin adapter: every rule
    /// lives in the engine-free cores it feeds.
    /// <para>Each mesh is read once when it changes and kept; a snapshot joins the kept copies. Where a mesh
    /// has no usable face classes, its faces are classed by their geometry. With no scanned mesh at all
    /// (XR Simulation, a phone without LiDAR) the detected planes are laid out as triangles instead, so the
    /// same grid pipeline runs on them.</para>
    /// </summary>
    public sealed class ScanSensors
    {
        sealed class MeshCopy
        {
            public MeshFilter Filter;
            public bool Dirty = true;
            public float3[] Vertices = Array.Empty<float3>();
            public int[] Indices = Array.Empty<int>();
            public byte[] Classes = Array.Empty<byte>();
            public int VertexCount, IndexCount;
            public bool HasClasses;
        }

        readonly ArSessionRig _rig;
        readonly Dictionary<TrackableId, MeshCopy> _meshes = new Dictionary<TrackableId, MeshCopy>();
        readonly List<Vector3> _vertexBuffer = new List<Vector3>(4096);
        readonly List<int> _indexBuffer = new List<int>(8192);
        readonly List<HorizontalPlane> _planes = new List<HorizontalPlane>(32);
        readonly List<WallPlane> _walls = new List<WallPlane>(32);
        readonly List<float3> _planeVertices = new List<float3>(256);
        readonly List<int> _planeIndices = new List<int>(512);
        readonly List<SurfaceClass> _planeClasses = new List<SurfaceClass>(256);
        ScannedBox[] _boxes = new ScannedBox[32];
        bool _listening;

        public ScanSensors(ArSessionRig rig)
        {
            _rig = rig;
        }

        /// <summary>Meshes with faces that had no class array matching them at the last snapshot.</summary>
        public int NeutralMeshes { get; private set; }
        /// <summary>Meshes that could not be read at the last snapshot (not readable on the CPU).</summary>
        public int UnreadableMeshes { get; private set; }

        /// <summary>The frame AR trackables are reported in: the XR Origin's trackables parent. Null with no XR Origin.</summary>
        public Transform SessionSpace
        {
            get
            {
                if (_rig == null || _rig.Origin == null) return null;
                Transform space = _rig.Origin.TrackablesParent;
                if (space != null) return space;
                Camera camera = _rig.Origin.Camera;
                return camera != null ? camera.transform.parent : null;
            }
        }

        /// <summary>Starts following the mesh manager. Call when the scan features are switched on.</summary>
        public void Start()
        {
            _meshes.Clear();
            if (_listening || _rig == null || _rig.Meshes == null) return;
            _rig.Meshes.meshInfosChanged.AddListener(OnMeshes);
            _listening = true;
        }

        /// <summary>Stops following and forgets the meshes. Call before the scan features are switched off.</summary>
        public void Stop()
        {
            if (_listening && _rig != null && _rig.Meshes != null) _rig.Meshes.meshInfosChanged.RemoveListener(OnMeshes);
            _listening = false;
            _meshes.Clear();
        }

        void OnMeshes(ARMeshInfosChangedEventArgs args)
        {
            foreach (MeshUpdateInfo info in args.added) Touch(info);
            foreach (MeshUpdateInfo info in args.updated) Touch(info);
            foreach (MeshUpdateInfo info in args.removed) _meshes.Remove(info.id);
        }

        void Touch(MeshUpdateInfo info)
        {
            MeshCopy copy;
            if (!_meshes.TryGetValue(info.id, out copy))
            {
                copy = new MeshCopy();
                _meshes.Add(info.id, copy);
            }
            copy.Filter = info.meshFilter;
            copy.Dirty = true;
        }

        /// <summary>
        /// Everything sensed so far as one snapshot in the session frame. The arrays are new, so a worker
        /// thread may keep them. The path, the walker and the outline are added by the scan flow.
        /// </summary>
        public ScanSnapshot Capture()
        {
            var snapshot = new ScanSnapshot();
            if (_rig == null || !_rig.HasAr)
            {
                // No AR provider (the Editor walker): the floor is the world's y = 0 and nothing else is sensed.
                snapshot.FloorHeight = 0f;
                return snapshot;
            }
            Transform space = SessionSpace;
            ReadPlanes(snapshot);
            ReadMeshes(snapshot, space);
            if (snapshot.TriangleCount == 0) LayPlanes(snapshot);
            ReadBoxes(snapshot);
            return snapshot;
        }

        // ---- planes ---------------------------------------------------------------------------------------

        void ReadPlanes(ScanSnapshot snapshot)
        {
            _planes.Clear();
            _walls.Clear();
            ARPlaneManager manager = _rig.Planes;
            if (manager == null || !manager.enabled) return;
            float wallTop = float.NaN, ceiling = float.NaN, ceilingArea = 0f;
            foreach (ARPlane plane in manager.trackables)
            {
                if (plane == null || plane.subsumedBy != null) continue;
                if (plane.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.None) continue;
                Pose pose = plane.pose;      // session frame
                NativeArray<Vector2> boundary = plane.boundary;
                if (boundary.Length < 3) continue;
                PlaneAlignment alignment = plane.alignment;
                if (alignment == PlaneAlignment.HorizontalUp || alignment == PlaneAlignment.HorizontalDown)
                {
                    float area = Area(boundary);
                    if (alignment == PlaneAlignment.HorizontalUp)
                    {
                        bool floor = (plane.classifications & PlaneClassifications.Floor) != 0;
                        _planes.Add(new HorizontalPlane { Height = pose.position.y, Area = area, IsFloor = floor });
                    }
                    else if (area > ceilingArea)
                    {
                        ceilingArea = area;
                        ceiling = pose.position.y;
                    }
                }
                else if (alignment == PlaneAlignment.Vertical)
                {
                    Vector3 normal = pose.rotation * Vector3.up;
                    var flat = new float2(normal.x, normal.z);
                    if (math.lengthsq(flat) < 0.25f) continue;
                    var along = math.normalize(new float2(-flat.y, flat.x));
                    float lo = float.MaxValue, hi = float.MinValue, top = float.MinValue;
                    for (int k = 0; k < boundary.Length; k++)
                    {
                        Vector3 p = pose.position + pose.rotation * new Vector3(boundary[k].x, 0f, boundary[k].y);
                        float t = p.x * along.x + p.z * along.y;
                        lo = math.min(lo, t);
                        hi = math.max(hi, t);
                        top = math.max(top, p.y);
                    }
                    float length = hi - lo;
                    if (length < 0.3f) continue;
                    _walls.Add(new WallPlane { Normal = flat, Length = length });
                    if (length >= 0.5f && (float.IsNaN(wallTop) || top > wallTop)) wallTop = top;
                }
            }
            snapshot.Planes = _planes.ToArray();
            snapshot.Walls = _walls.Count > 0 ? _walls.ToArray() : null;
            // A ceiling plane is the better witness of the wall height: wall planes stop where the sweep did.
            snapshot.WallTop = ceilingArea >= 1f ? ceiling : wallTop;
            if (_planes.Count > 0)
            {
                double y0 = FloorScanCore.FindFloorHeight(new FloorGridInput { Planes = snapshot.Planes },
                    Array.Empty<float3>(), Array.Empty<int>(), 0);
                if (!double.IsNaN(y0)) snapshot.FloorHeight = (float)y0;
            }
        }

        // With no scanned mesh the level planes stand in for it: the planes at floor height are floor, the
        // higher ones are furniture tops. A plane's boundary is laid out as a fan round its centre.
        void LayPlanes(ScanSnapshot snapshot)
        {
            ARPlaneManager manager = _rig.Planes;
            if (manager == null || !manager.enabled || float.IsNaN(snapshot.FloorHeight)) return;
            _planeVertices.Clear();
            _planeIndices.Clear();
            _planeClasses.Clear();
            float y0 = snapshot.FloorHeight;
            foreach (ARPlane plane in manager.trackables)
            {
                if (plane == null || plane.subsumedBy != null || plane.alignment != PlaneAlignment.HorizontalUp) continue;
                if (plane.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.None) continue;
                NativeArray<Vector2> boundary = plane.boundary;
                if (boundary.Length < 3) continue;
                Pose pose = plane.pose;
                float height = pose.position.y - y0;
                SurfaceClass cls;
                if (math.abs(height) <= (float)FloorScanCore.FloorBand) cls = SurfaceClass.Floor;
                else if (height > (float)FloorScanCore.ObstacleMinHeight && height < (float)FloorScanCore.ObstacleMaxHeight) cls = SurfaceClass.Table;
                else continue;
                Vector2 centre = plane.centerInPlaneSpace;
                int first = _planeVertices.Count;
                _planeVertices.Add(pose.position + pose.rotation * new Vector3(centre.x, 0f, centre.y));
                for (int k = 0; k < boundary.Length; k++)
                    _planeVertices.Add(pose.position + pose.rotation * new Vector3(boundary[k].x, 0f, boundary[k].y));
                for (int k = 0; k < boundary.Length; k++)
                {
                    _planeIndices.Add(first);
                    _planeIndices.Add(first + 1 + k);
                    _planeIndices.Add(first + 1 + (k + 1) % boundary.Length);
                    _planeClasses.Add(cls);
                }
            }
            if (_planeClasses.Count == 0) return;
            snapshot.Vertices = _planeVertices.ToArray();
            snapshot.Indices = _planeIndices.ToArray();
            snapshot.Classes = _planeClasses.ToArray();
            snapshot.MeshFromPlanes = true;
        }

        static float Area(NativeArray<Vector2> ring)
        {
            float total = 0f;
            for (int k = 0; k < ring.Length; k++)
            {
                Vector2 p = ring[k], q = ring[(k + 1) % ring.Length];
                total += p.x * q.y - q.x * p.y;
            }
            return math.abs(0.5f * total);
        }

        // ---- the mesh -------------------------------------------------------------------------------------

        void ReadMeshes(ScanSnapshot snapshot, Transform space)
        {
            NeutralMeshes = 0;
            UnreadableMeshes = 0;
            ARMeshManager manager = _rig.Meshes;
            if (manager == null || !manager.enabled || _meshes.Count == 0) return;
            IArMeshPlatform platform = ArMeshPlatform.Current;
            Matrix4x4 toSession = space != null ? space.worldToLocalMatrix : Matrix4x4.identity;

            int vertexTotal = 0, indexTotal = 0;
            foreach (KeyValuePair<TrackableId, MeshCopy> entry in _meshes)
            {
                MeshCopy copy = entry.Value;
                if (copy.Dirty) Read(entry.Key, copy, manager, platform, toSession);
                vertexTotal += copy.VertexCount;
                indexTotal += copy.IndexCount;
            }
            if (indexTotal == 0) return;

            var vertices = new float3[vertexTotal];
            var indices = new int[indexTotal];
            var classes = new SurfaceClass[indexTotal / 3];
            int v = 0, i = 0;
            bool anyNeutral = false;
            foreach (KeyValuePair<TrackableId, MeshCopy> entry in _meshes)
            {
                MeshCopy copy = entry.Value;
                if (copy.IndexCount == 0) continue;
                Array.Copy(copy.Vertices, 0, vertices, v, copy.VertexCount);
                for (int k = 0; k < copy.IndexCount; k++) indices[i + k] = copy.Indices[k] + v;
                int triangles = copy.IndexCount / 3, firstTriangle = i / 3;
                if (copy.HasClasses)
                {
                    for (int t = 0; t < triangles; t++)
                    {
                        byte cls = copy.Classes[t];
                        classes[firstTriangle + t] = cls <= (byte)SurfaceClass.Door ? (SurfaceClass)cls : SurfaceClass.None;
                    }
                }
                else
                {
                    anyNeutral = true;
                    NeutralMeshes++;
                    // Marked for the geometric classes below, which need the floor height of the whole scan.
                    for (int t = 0; t < triangles; t++) classes[firstTriangle + t] = (SurfaceClass)255;
                }
                v += copy.VertexCount;
                i += copy.IndexCount;
            }
            if (anyNeutral)
            {
                float y0 = snapshot.FloorHeight;
                if (float.IsNaN(y0)) y0 = NeutralSurfaceClasses.EstimateFloor(vertices, indices, indexTotal);
                for (int t = 0; t < classes.Length; t++)
                {
                    if (classes[t] != (SurfaceClass)255) continue;
                    classes[t] = float.IsNaN(y0) ? SurfaceClass.None
                        : NeutralSurfaceClasses.Classify(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], y0);
                }
                if (float.IsNaN(snapshot.FloorHeight) && !float.IsNaN(y0)) snapshot.FloorHeight = y0;
            }
            snapshot.Vertices = vertices;
            snapshot.Indices = indices;
            snapshot.Classes = classes;
        }

        void Read(TrackableId id, MeshCopy copy, ARMeshManager manager, IArMeshPlatform platform, Matrix4x4 toSession)
        {
            copy.Dirty = false;
            copy.VertexCount = 0;
            copy.IndexCount = 0;
            copy.HasClasses = false;
            MeshFilter filter = copy.Filter;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            if (!mesh.isReadable)
            {
                UnreadableMeshes++;
                return;
            }
            mesh.GetVertices(_vertexBuffer);
            mesh.GetIndices(_indexBuffer, 0);
            int vertexCount = _vertexBuffer.Count, indexCount = _indexBuffer.Count - _indexBuffer.Count % 3;
            if (vertexCount == 0 || indexCount == 0) return;
            if (copy.Vertices.Length < vertexCount) copy.Vertices = new float3[Mathf.NextPowerOfTwo(vertexCount)];
            if (copy.Indices.Length < indexCount) copy.Indices = new int[Mathf.NextPowerOfTwo(indexCount)];
            // Mesh objects sit under the trackables parent: local to world, then world to session.
            Matrix4x4 matrix = toSession * filter.transform.localToWorldMatrix;
            for (int k = 0; k < vertexCount; k++) copy.Vertices[k] = matrix.MultiplyPoint3x4(_vertexBuffer[k]);
            for (int k = 0; k < indexCount; k++) copy.Indices[k] = _indexBuffer[k];
            copy.VertexCount = vertexCount;
            copy.IndexCount = indexCount;

            int triangles = indexCount / 3;
            if (!platform.HasFaceClasses) return;
            if (copy.Classes.Length < triangles) copy.Classes = new byte[Mathf.NextPowerOfTwo(triangles)];
            // The classes are read at another moment than the mesh was generated: they count only when
            // there is exactly one per face.
            copy.HasClasses = platform.CopyFaceClasses(manager.subsystem, id, copy.Classes) == triangles;
        }

        // ---- boxes ----------------------------------------------------------------------------------------

        void ReadBoxes(ScanSnapshot snapshot)
        {
            ARBoundingBoxManager manager = _rig.Boxes;
            if (manager == null || !manager.enabled) return;
            int count = 0;
            foreach (ARBoundingBox box in manager.trackables)
            {
                if (box == null || box.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.None) continue;
                if (count == _boxes.Length) Array.Resize(ref _boxes, 2 * count);
                Pose pose = box.pose;        // session frame, at the centre of the box
                _boxes[count] = new ScannedBox
                {
                    Centre = pose.position, Rotation = pose.rotation, Size = box.size,
                    ClassBits = (uint)box.classifications, SourceId = count + 1,
                };
                count++;
            }
            if (count > 0) snapshot.Boxes = ObjectBoxMerge.Merge(_boxes, count);
        }
    }
}
