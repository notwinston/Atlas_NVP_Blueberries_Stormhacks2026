using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// The AR session's configuration (Master Plan section 8.2, Appendix C.4, decision D19; task P1-C01).
    /// It is given the AR Session and the XR Origin by the composition root and never searches the scene.
    /// Always: the lowest-resolution 60 Hz video format, LiDAR depth for Bump Guard, no light estimation, a
    /// frame rate the frame loop owns, one pose sample per frame. Only during a scan: meshing, planes and
    /// ray-casts. Furniture boxes do not come through AR Foundation: the scan's one RoomPlan session is the
    /// Swift plugin (RoomPlanSession, standalone-phone plan D2), so no ARBoundingBoxManager is created or
    /// enabled, and one the scene brings is kept off. With no AR provider running (the Editor without XR Simulation, Windows) every call is
    /// accepted and does nothing.
    /// </summary>
    public sealed class ArSessionRig
    {
        const string LogPrefix = "Atlas: ";

        readonly XROrigin _origin;
        readonly ARSession _session;
        readonly XrBackend _backend;
        readonly bool _hasAr;

        string _videoFormat = "none";
        bool _noFormatLogged;
        bool _trackingLogged;
        ARBoundingBoxManager _sceneBoxes;

        /// <summary>Either argument may be null (the Station): the rig is then neutral.</summary>
        public ArSessionRig(XROrigin origin, ARSession session)
        {
            _origin = origin;
            _session = session;
            _backend = ArBackend.Detect();
            Camera trackedCamera = origin != null ? origin.Camera : null;
            _hasAr = _backend != XrBackend.None && origin != null && session != null && trackedCamera != null;

            // The frame loop owns the frame rate. Left on, ARSession sets Application.targetFrameRate and
            // vSyncCount every frame and blocks Update until the next camera frame.
            if (session != null) session.matchFrameRateRequested = false;

            if (origin != null)
            {
                // A handheld session reports the device pose itself; a camera height offset would lift the
                // whole room off the floor.
                origin.CameraYOffset = 0f;
                GameObject originObject = origin.gameObject;
                Planes = originObject.GetComponent<ARPlaneManager>();
                Raycasts = originObject.GetComponent<ARRaycastManager>();
                Anchors = originObject.GetComponent<ARAnchorManager>();
                // Only to keep it off: a running RoomPlanBoundingBoxSubsystem would be a second RoomPlan session.
                _sceneBoxes = originObject.GetComponent<ARBoundingBoxManager>();
                if (_hasAr)
                {
                    if (Planes == null) Planes = originObject.AddComponent<ARPlaneManager>();
                    if (Raycasts == null) Raycasts = originObject.AddComponent<ARRaycastManager>();
                    if (Anchors == null) Anchors = originObject.AddComponent<ARAnchorManager>();
                    Anchors.enabled = true;
                    Meshes = CreateMeshManager(origin.transform);
                }
            }

            if (trackedCamera != null)
            {
                GameObject cameraObject = trackedCamera.gameObject;
                Camera = cameraObject.GetComponent<ARCameraManager>();
                Occlusion = cameraObject.GetComponent<AROcclusionManager>();
                Driver = cameraObject.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (_hasAr)
                {
                    if (Camera == null) Camera = cameraObject.AddComponent<ARCameraManager>();
                    if (Occlusion == null) Occlusion = cameraObject.AddComponent<AROcclusionManager>();
                    if (Driver == null) Driver = AddPoseDriver(cameraObject);
                }
                if (Camera != null) ArBackgroundOrientation.Attach(Camera, trackedCamera);
                if (Driver != null)
                {
                    // One pose sample per frame feeds the kernel and both eyes (decision D8).
                    Driver.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.Update;
                    Driver.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
                }
            }

            // The scan features start off; the boot scene's template has planes and ray-casts switched on.
            DisableScanManagers();
            ApplyBase();
            Debug.Log(LogPrefix + "AR backend " + ArBackend.Name(_backend) + (_hasAr ? "" : " (no AR session: neutral)"));
        }

        public XROrigin Origin { get { return _origin; } }
        public ARSession Session { get { return _session; } }
        public XrBackend Backend { get { return _backend; } }
        /// <summary>An AR provider is running and the rig configures it. False in the Editor without XR Simulation and on Windows.</summary>
        public bool HasAr { get { return _hasAr; } }

        /// <summary>Null when there is no tracked camera.</summary>
        public ARCameraManager Camera { get; private set; }
        public AROcclusionManager Occlusion { get; private set; }
        public ARAnchorManager Anchors { get; private set; }
        public ARPlaneManager Planes { get; private set; }
        /// <summary>On a child of the XR Origin. Null with no AR provider.</summary>
        public ARMeshManager Meshes { get; private set; }
        public ARRaycastManager Raycasts { get; private set; }
        /// <summary>The tracked camera's pose driver, on Update only.</summary>
        public UnityEngine.InputSystem.XR.TrackedPoseDriver Driver { get; private set; }

        /// <summary>The video format has been chosen, or there is none to choose (no provider, no 60 Hz entry after the camera started).</summary>
        public bool VideoFormatApplied { get; private set; }
        /// <summary>"1280x720@60", or "default" when the provider lists no 60 Hz format, or "none".</summary>
        public string VideoFormatText { get { return _videoFormat; } }
        public bool ScanFeaturesOn { get; private set; }

        /// <summary>
        /// The settings that always hold. Call every frame until VideoFormatApplied (the camera subsystem
        /// appears some frames after start); the pose pipeline does. Cheap afterwards.
        /// </summary>
        public void ApplyBase()
        {
            if (_session != null && _session.matchFrameRateRequested) _session.matchFrameRateRequested = false;
            if (!_hasAr)
            {
                VideoFormatApplied = true;
                return;
            }
            ApplyTracking();
            ApplyDepth();
            if (Camera != null && Camera.requestedLightEstimation != LightEstimation.None)
                Camera.requestedLightEstimation = LightEstimation.None;
            if (!VideoFormatApplied) TryApplyVideoFormat();
        }

        /// <summary>
        /// The tracking request: position and rotation. With no mode requested ARKit picks its configuration
        /// from the features that are on, so when the scan features go off it drops to rotation only and the
        /// head position freezes. The boot scene's request never reaches a session that is already running:
        /// ARSession reads the subsystem's mode back over it. Call every frame; a comparison when it holds.
        /// </summary>
        public void ApplyTracking()
        {
            if (!_hasAr) return;
            XRSessionSubsystem subsystem = _session.subsystem;
            if (subsystem == null || subsystem.requestedTrackingMode == Feature.PositionAndRotation) return;
            _session.requestedTrackingMode = TrackingMode.PositionAndRotation;
            subsystem.requestedTrackingMode = Feature.PositionAndRotation;
            if (!_trackingLogged)
            {
                _trackingLogged = true;
                Debug.Log(LogPrefix + "AR tracking mode requested: PositionAndRotation");
            }
        }

        /// <summary>
        /// The depth request: environment depth at mode Fastest, no temporal smoothing, no human stencil or
        /// depth. Depth stays on while walking (D19). Call again after a RoomPlan capture starts or stops,
        /// which can replace the session's configuration (Appendix C.10).
        /// </summary>
        public void ApplyDepth()
        {
            AROcclusionManager occlusion = Occlusion;
            if (occlusion == null) return;
            if (!occlusion.enabled) occlusion.enabled = true;
            if (occlusion.requestedEnvironmentDepthMode != EnvironmentDepthMode.Fastest)
                occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            if (occlusion.environmentDepthTemporalSmoothingRequested)
                occlusion.environmentDepthTemporalSmoothingRequested = false;
            if (occlusion.requestedHumanStencilMode != HumanSegmentationStencilMode.Disabled)
                occlusion.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            if (occlusion.requestedHumanDepthMode != HumanSegmentationDepthMode.Disabled)
                occlusion.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
        }

        /// <summary>
        /// Meshing, planes and ray-casts on for a scan, or off when it ends (confirm, cancel, failure). mesh lets
        /// a scan leave out route 2. Face classification and the RoomPlan capture are started by the scan
        /// session once the managers are on.
        /// </summary>
        public void SetScanFeatures(bool on, bool mesh = true)
        {
            if (!_hasAr)
            {
                ScanFeaturesOn = on;
                return;
            }
            if (on)
            {
                if (Planes != null)
                {
                    Planes.enabled = true;
                    Planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                }
                if (Raycasts != null) Raycasts.enabled = true;
                if (Meshes != null) Meshes.enabled = mesh;
                ScanFeaturesOn = true;
                Debug.Log(LogPrefix + "scan features on: " + (mesh ? "meshing, " : "") + "planes");
                return;
            }

            bool wasOn = ScanFeaturesOn;
            DisableScanManagers();
            ScanFeaturesOn = false;
            // Meshing and plane detection are a major source of heat: this line is what gate G5 looks for (its
            // text is unchanged; "boxes" now means the RoomPlan capture, which the scan session stops first).
            if (wasOn) Debug.Log(LogPrefix + "scan features off: meshing, planes, boxes");
        }

        /// <summary>One line for the start-up log: backend, video format, depth, scan features.</summary>
        public string Describe()
        {
            string depth = "none";
            if (Occlusion != null)
            {
                XROcclusionSubsystemDescriptor descriptor = Occlusion.descriptor;
                bool supported = descriptor != null && descriptor.environmentDepthImageSupported == Supported.Supported;
                depth = supported ? "environment depth " + Occlusion.currentEnvironmentDepthMode : "no depth image";
            }
            string extras = _session != null ? ArSessionPlatform.Current.Describe(_session.subsystem) : "";
            return "AR " + ArBackend.Name(_backend) + ", video " + _videoFormat + ", " + depth +
                   ", scan features " + (ScanFeaturesOn ? "on" : "off") +
                   (string.IsNullOrEmpty(extras) ? "" : ", " + extras);
        }

        void DisableScanManagers()
        {
            if (Planes != null)
            {
                if (Planes.enabled) Planes.requestedDetectionMode = PlaneDetectionMode.None;
                Planes.enabled = false;
            }
            if (Raycasts != null) Raycasts.enabled = false;
            if (_sceneBoxes != null) _sceneBoxes.enabled = false;
            if (Meshes != null)
            {
                Meshes.enabled = false;
                Meshes.DestroyAllMeshes();
            }
        }

        void TryApplyVideoFormat()
        {
            ARCameraManager camera = Camera;
            if (camera == null || camera.subsystem == null) return;      // retried next frame
            using (NativeArray<XRCameraConfiguration> configurations = camera.GetConfigurations(Allocator.Temp))
            {
                if (configurations.Length == 0) return;                   // the provider has not listed them yet
                bool found = false;
                XRCameraConfiguration best = default(XRCameraConfiguration);
                long bestPixels = long.MaxValue;
                for (int i = 0; i < configurations.Length; i++)
                {
                    XRCameraConfiguration c = configurations[i];
                    if (c.framerate != VideoFormat.RequiredFrameRate) continue;
                    long pixels = (long)c.width * c.height;
                    if (pixels <= 0 || pixels >= bestPixels) continue;
                    best = c;
                    bestPixels = pixels;
                    found = true;
                }
                if (!found)
                {
                    // XR Simulation lists one format with no frame rate: nothing to choose.
                    _videoFormat = "default";
                    VideoFormatApplied = true;
                    if (!_noFormatLogged)
                    {
                        _noFormatLogged = true;
                        Debug.Log(LogPrefix + ArBackend.Name(_backend) + " video: no 60 Hz format listed, the default is kept");
                    }
                    return;
                }
                try
                {
                    camera.currentConfiguration = best;
                }
                catch (System.InvalidOperationException)
                {
                    return;      // the native session is not ready for it yet: retried next frame
                }
                catch (System.ArgumentException e)
                {
                    _videoFormat = "default";
                    VideoFormatApplied = true;
                    Debug.LogWarning(LogPrefix + "ARKit video format refused: " + e.Message);
                    return;
                }
                _videoFormat = VideoFormat.Describe(best.width, best.height, VideoFormat.RequiredFrameRate);
                VideoFormatApplied = true;
                Debug.Log(LogPrefix + "ARKit video " + _videoFormat);
                Debug.Log(LogPrefix + Describe());
            }
        }

        static ARMeshManager CreateMeshManager(Transform origin)
        {
            ARMeshManager existing = origin.GetComponentInChildren<ARMeshManager>(true);
            if (existing != null)
            {
                existing.enabled = false;
                return existing;
            }
            // Built inactive so the manager never starts: it must sit below the XR Origin and stays off until a scan.
            var holder = new GameObject("Atlas Meshing");
            holder.SetActive(false);
            holder.transform.SetParent(origin, false);
            holder.transform.localScale = new Vector3(10f, 10f, 10f);    // the meshing volume, as the package's own default
            ARMeshManager manager = holder.AddComponent<ARMeshManager>();
            manager.enabled = false;

            // The manager generates nothing without a template to copy. Ours has a filter and nothing else:
            // the scan reads the mesh data, nobody draws it.
            var template = new GameObject("Atlas Mesh Template");
            template.SetActive(false);
            template.transform.SetParent(holder.transform, false);
            manager.meshPrefab = template.AddComponent<MeshFilter>();
            manager.normals = false;

            holder.SetActive(true);
            return manager;
        }

        static UnityEngine.InputSystem.XR.TrackedPoseDriver AddPoseDriver(GameObject cameraObject)
        {
            // The bindings the XR Origin menu item gives a mobile AR camera.
            var driver = cameraObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            var position = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            return driver;
        }
    }
}
