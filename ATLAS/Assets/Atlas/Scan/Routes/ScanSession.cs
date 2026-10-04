using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// The scan as onboarding drives it (contract section 10; Master Plan section 4.2, tasks P1-C02 to
    /// P1-C06), phone in the hand: sweep, outline review, confirm. The state machine is ScanFlow; this class
    /// connects it to the AR session: it switches the scan features on and off, feeds the sensors' snapshot
    /// to the routes, ray-casts corner taps to the floor and creates the room's anchor.
    /// <para>In the Editor the same calls work with no phone (contract README F9): with XR Simulation the
    /// room comes from the simulated planes and boxes, with no AR provider from corners tapped or walked,
    /// and UseFixture or UseRoom skips the scan altogether.</para>
    /// </summary>
    public sealed class ScanSession : IScanSession
    {
        const string LogPrefix = "Atlas: ";
        // After RoomPlan capture starts the depth image is watched this long; if it stays away the boxes go.
        const float DepthCheckSeconds = 2f;
        const float HintInterval = 0.25f;

        readonly ArSessionRig _rig;
        readonly IPosePipeline _pose;
        readonly ScanFlow _flow;
        readonly ScanSensors _sensors;
        readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>(16);

        bool _featuresOn, _meshOn, _boxesOn, _capturing, _classesAsked;
        bool _depthBefore;
        float _depthMissing, _hintTimer;
        GameObject _anchorObject;

        /// <summary>rig and pose come from the pose systems (ArPoseSystems.Rig and .Pipeline). Neither is searched for.</summary>
        public ScanSession(ArSessionRig rig, IPosePipeline pose)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            _rig = rig;
            _pose = pose;
            _sensors = new ScanSensors(rig);
            _flow = new ScanFlow(_sensors.Capture);
            _flow.StateChanged += OnFlowState;
        }

        /// <summary>The engine-free state machine behind this session: the grid of the floor found so far, the corners, the path.</summary>
        public ScanFlow Flow { get { return _flow; } }
        public ScanSensors Sensors { get { return _sensors; } }

        public ScanState State { get { return _flow.State; } }
        public ScanFailure Failure { get { return _flow.Failure; } }
        public ScanProgress Progress { get { return _flow.Progress; } }
        public event Action<ScanState> StateChanged;

        /// <summary>LiDAR meshing exists here: an ARKit device with a mesh manager. False in XR Simulation, where the planes are used.</summary>
        public bool MeshAvailable
        {
            get { return _rig.HasAr && _rig.Backend == XrBackend.ARKit && _rig.Meshes != null; }
        }

        /// <summary>Furniture boxes exist here: RoomPlan on the phone, simulated boxes in XR Simulation.</summary>
        public bool BoxesAvailable
        {
            get { return _rig.HasAr && _rig.Boxes != null && _rig.Boxes.subsystem != null; }
        }

        public OutlineProposal Proposal { get { return _flow.Proposal; } }
        public RoomModel Result { get { return _flow.Result; } }
        public Transform ResultAnchor { get; private set; }

        /// <summary>The frame scan outlines are given in. Null when it is world space itself (no XR Origin).</summary>
        public Transform SessionSpace { get { return _sensors.SessionSpace; } }

        // ---- driving --------------------------------------------------------------------------------------

        public void Begin(in ScanOptions options)
        {
            _flow.MeshExpected = options.UseMesh && MeshAvailable;
            _flow.Begin(in options, DateTime.UtcNow);
        }

        public void Tick(float deltaTime)
        {
            ScanState state = _flow.State;
            if (state == ScanState.Idle || state == ScanState.Done || state == ScanState.Failed) return;
            if (_featuresOn) WatchProvider(deltaTime);
            HeadPose head = _pose.Raw;
            _flow.Tick(deltaTime, head.Tracking, ToSession(head.Position));
            if (_flow.State == ScanState.Finalising) Finalise();
        }

        public void EndSweep() { _flow.EndSweep(); }
        public void Confirm() { _flow.Confirm(); }
        public void Cancel() { _flow.Cancel(); }

        // ---- route 1 --------------------------------------------------------------------------------------

        public bool TryAddCorner(Vector2 screenPoint)
        {
            float3 hit;
            if (!TryFloorPoint(screenPoint, out hit)) return false;
            return _flow.AddCorner(hit.xz, hit.y);
        }

        public void AddCornerAtFeet()
        {
            _flow.AddCorner(ToSession(_pose.Raw.Position).xz);
        }

        public void RemoveLastCorner() { _flow.RemoveLastCorner(); }
        public int CopyPreviewOutline(float2[] into) { return _flow.CopyPreviewOutline(into); }

        /// <summary>
        /// The floor point (session frame) under a screen point: a ray-cast against the detected level
        /// planes, lowest hit first; failing that, the camera ray cut with the floor height when it is known
        /// (always in the Editor without an AR provider, where the floor is y = 0).
        /// </summary>
        public bool TryFloorPoint(Vector2 screenPoint, out float3 sessionPoint)
        {
            sessionPoint = float3.zero;
            ARRaycastManager raycasts = _rig.Raycasts;
            if (_rig.HasAr && raycasts != null && raycasts.enabled &&
                raycasts.Raycast(screenPoint, _hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                bool found = false;
                for (int k = 0; k < _hits.Count; k++)
                {
                    var plane = _hits[k].trackable as ARPlane;
                    if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp) continue;
                    // The session-relative pose stays right when the XR Origin has been moved (a rescan).
                    float3 p = _hits[k].sessionRelativePose.position;
                    if (found && p.y >= sessionPoint.y) continue;
                    sessionPoint = p;
                    found = true;
                }
                if (found) return true;
            }
            float floorY = FloorHeight;
            Camera camera = _rig.Origin != null ? _rig.Origin.Camera : null;
            if (float.IsNaN(floorY) || camera == null) return false;
            Ray ray = camera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));
            float3 origin = ToSession(ray.origin);
            float3 direction = ToSessionDirection(ray.direction);
            if (direction.y > -1e-3f) return false;                // the ray does not come down to the floor
            float t = (floorY - origin.y) / direction.y;
            if (t <= 0f || t > 20f) return false;
            sessionPoint = origin + t * direction;
            return true;
        }

        /// <summary>y0 in the session frame as far as it is known now: the proposal's, else the last snapshot's; NaN when unknown.</summary>
        public float FloorHeight
        {
            get
            {
                if (_flow.Proposal != null) return _flow.Proposal.FloorY;
                if (_flow.Grid != null && _flow.Grid.Cells != null && _flow.Grid.Cells.Length > 0) return _flow.Grid.FloorHeight;
                return _rig.HasAr ? float.NaN : 0f;
            }
        }

        // ---- confirmation and correction ------------------------------------------------------------------

        public void SetOutline(float2[] outline, int count) { _flow.SetOutline(outline, count); }
        public void MoveCorner(int index, float2 floorPoint) { _flow.MoveCorner(index, floorPoint); }
        public void InsertCorner(int afterIndex, float2 floorPoint) { _flow.InsertCorner(afterIndex, floorPoint); }
        public void RemoveCorner(int index) { _flow.RemoveCorner(index); }

        /// <summary>A floor point of the session frame at height y, in world space: where a view draws it.</summary>
        public Vector3 SessionToWorld(float2 floorPoint, float y)
        {
            var p = new Vector3(floorPoint.x, y, floorPoint.y);
            Transform space = SessionSpace;
            return space != null ? space.TransformPoint(p) : p;
        }

        /// <summary>A world point in the session frame: what MoveCorner and SetOutline take.</summary>
        public float3 ToSession(float3 world)
        {
            Transform space = SessionSpace;
            return space != null ? (float3)space.InverseTransformPoint(world) : world;
        }

        float3 ToSessionDirection(Vector3 world)
        {
            Transform space = SessionSpace;
            return space != null ? (float3)space.InverseTransformDirection(world) : (float3)world;
        }

        // ---- no scan: fixtures and saved rooms (contract README F9) ---------------------------------------

        /// <summary>The fixture names that UseFixture takes.</summary>
        public static string[] FixtureNames { get { return RoomFixtures.All; } }

        /// <summary>
        /// Makes a room the result with no scan: State goes to Done, ResultAnchor is null (the room's own
        /// anchor pose is used). A scan in progress is dropped and its features are switched off.
        /// </summary>
        public void UseRoom(RoomModel room)
        {
            ReleaseAnchor();
            _flow.UseRoom(room);
        }

        /// <summary>Loads a fixture (RoomFixtures) and makes it the result. False when there is no such fixture.</summary>
        public bool UseFixture(string name)
        {
            if (!RoomFixtures.IsFixture(name)) return false;
            RoomModel room = RoomFixtures.Load(name);
            if (room == null) return false;
            UseRoom(room);
            Debug.Log(LogPrefix + "room from fixture " + name + ", no scan");
            return true;
        }

        // ---- the AR side ----------------------------------------------------------------------------------

        void OnFlowState(ScanState state)
        {
            switch (state)
            {
                case ScanState.Starting:
                    StartFeatures();
                    break;
                case ScanState.Finalising:
                    // Meshing and plane detection are a major source of heat: off the moment the walker confirms.
                    StopFeatures("confirmed");
                    break;
                case ScanState.Idle:
                    StopFeatures("cancelled");
                    break;
                case ScanState.Failed:
                    StopFeatures("failed (" + _flow.Failure + ")");
                    break;
                case ScanState.Done:
                    StopFeatures("done");
                    break;
            }
            Action<ScanState> changed = StateChanged;
            if (changed != null) changed(state);
        }

        void StartFeatures()
        {
            ScanOptions options = _flow.Options;
            _meshOn = options.UseMesh && MeshAvailable;
            _boxesOn = options.UseBoxes && _rig.HasAr && _rig.Boxes != null;
            _depthBefore = _pose.Sensors.HasDepth;
            _rig.SetScanFeatures(true, _meshOn, _boxesOn);
            _featuresOn = true;
            _classesAsked = false;
            _capturing = false;
            _depthMissing = 0f;
            _hintTimer = 0f;
            _sensors.Start();
            if (_boxesOn)
            {
                // RoomPlan produces boxes only while a capture runs. XR Simulation needs none.
                _capturing = ArMeshPlatform.Current.StartRoomCapture(_rig.Boxes.subsystem);
                // Starting a capture can replace the session's configuration: ask for depth again.
                _rig.ApplyDepth();
            }
            Debug.Log(LogPrefix + "scan started: mesh " + (_meshOn ? "on" : "off") + ", boxes " + (_boxesOn ? "on" : "off") +
                      (_capturing ? " (RoomPlan capture)" : ""));
        }

        void WatchProvider(float deltaTime)
        {
            IArMeshPlatform platform = ArMeshPlatform.Current;
            if (_meshOn && !_classesAsked && _rig.Meshes != null && _rig.Meshes.subsystem != null)
            {
                // The mesh subsystem appears when the manager starts, some frames after it is enabled.
                platform.SetMeshClassification(_rig.Meshes.subsystem, true);
                _classesAsked = true;
            }
            if (!_capturing) return;
            _hintTimer += deltaTime;
            if (_hintTimer >= HintInterval)
            {
                _hintTimer = 0f;
                _flow.ExternalHint = platform.RoomCaptureHint(_rig.Boxes.subsystem);
            }
            // Bump Guard needs the depth image back after the scan. If the capture took it away and it has
            // not returned in two seconds, this scan goes on without boxes (route 2).
            if (!_depthBefore || _depthMissing < 0f) return;
            if (_pose.Sensors.HasDepth)
            {
                _depthMissing = -1f;      // seen once with the capture running: no need to watch further
                return;
            }
            _depthMissing += deltaTime;
            if (_depthMissing < DepthCheckSeconds) return;
            _depthMissing = -1f;
            StopCapture();
            _boxesOn = false;
            if (_rig.Boxes != null) _rig.Boxes.enabled = false;
            Debug.LogWarning(LogPrefix + "depth did not return with RoomPlan capture running: boxes off for this scan");
        }

        void StopCapture()
        {
            if (!_capturing) return;
            _capturing = false;
            _flow.ExternalHint = ScanHint.None;
            if (_rig.Boxes != null) ArMeshPlatform.Current.StopRoomCapture(_rig.Boxes.subsystem);
            _rig.ApplyDepth();
        }

        void StopFeatures(string why)
        {
            if (!_featuresOn) return;
            _featuresOn = false;
            StopCapture();
            if (_classesAsked && _rig.Meshes != null) ArMeshPlatform.Current.SetMeshClassification(_rig.Meshes.subsystem, false);
            _classesAsked = false;
            _sensors.Stop();
            _rig.SetScanFeatures(false);
            // Gate G5 and task P1-C06 read the log for this: nothing of the scan keeps running while walking.
            Debug.Log(LogPrefix + "scan " + why + ": meshing and plane detection are off");
        }

        void Finalise()
        {
            RoomModel room = _flow.Proposal.Preview;
            ReleaseAnchor();
            string anchorId;
            ResultAnchor = CreateAnchor(room, out anchorId);
            _flow.CompleteFinalise(anchorId, DateTime.UtcNow);
            Debug.Log(LogPrefix + "room " + room.Id + " confirmed: free floor " + room.FreeRect.Wf.ToString("0.00") + " x " +
                      room.FreeRect.Df.ToString("0.00") + " m, lap " + room.LapSide.ToString("0.00") + " m, " +
                      (room.Standing ? "standing" : "walking") + ", " + room.Objects.Length + " objects, route " +
                      (int)room.Quality.Route + ", anchor " + (ResultAnchor != null ? anchorId : "none"));
        }

        // The ARAnchor at the centre of the free rectangle. Its pose is the room frame: the pose pipeline
        // moves the XR Origin so that this transform sits at the world origin (contract README F4).
        Transform CreateAnchor(RoomModel room, out string id)
        {
            id = "";
            ARAnchorManager anchors = _rig.Anchors;
            Transform space = _rig.Origin != null ? _rig.Origin.TrackablesParent : null;
            if (!_rig.HasAr || anchors == null || !anchors.enabled || anchors.subsystem == null || space == null) return null;
            var holder = new GameObject("Atlas Room Anchor");
            holder.SetActive(false);
            holder.transform.SetParent(space, false);
            holder.transform.localPosition = room.Anchor.Position;
            holder.transform.localRotation = Quaternion.AngleAxis(math.degrees(room.Anchor.Yaw), Vector3.up);
            ARAnchor anchor = holder.AddComponent<ARAnchor>();
            // The component registers itself with the anchor subsystem, at its world pose, when it is enabled.
            holder.SetActive(true);
            if (!anchor.enabled || anchor.trackableId == TrackableId.invalidId)
            {
                UnityEngine.Object.Destroy(holder);
                Debug.LogWarning(LogPrefix + "the room anchor could not be created: the room keeps its saved pose");
                return null;
            }
            _anchorObject = holder;
            id = anchor.trackableId.ToString();
            return holder.transform;
        }

        void ReleaseAnchor()
        {
            ResultAnchor = null;
            if (_anchorObject != null) UnityEngine.Object.Destroy(_anchorObject);
            _anchorObject = null;
        }
    }
}
