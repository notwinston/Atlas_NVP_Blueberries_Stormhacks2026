using System;
using System.IO;
using Atlas.World;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;

namespace Atlas.Scan
{
    /// <summary>
    /// The AR session set-up, the pose pipeline and the pose-log recorder, built together and wired by
    /// platform (contract README F3 and F9). The scan installer creates one, publishes it, and builds the
    /// scan session on top of its Rig.
    /// <para>On the phone and in XR Simulation the head is the tracked AR camera; in the Editor with no XR
    /// loader it is the keyboard walker. Depth is LiDAR on the phone and a ray-cast of the RoomModel
    /// everywhere else. A pose log can replace the head at any time with StartReplay.</para>
    /// </summary>
    public sealed class ArPoseSystems : IDisposable
    {
        public ArSessionRig Rig { get; private set; }
        public PosePipeline Pipeline { get; private set; }
        public PoseLogRecorder Recorder { get; private set; }
        /// <summary>The tracked-camera source; null with no AR provider.</summary>
        public ArCameraPoseSource ArCamera { get; private set; }
        /// <summary>The Editor walker. Always created, so a developer can switch to it; the default source only with no AR provider.</summary>
        public KeyboardWalkerPoseSource Walker { get; private set; }
        /// <summary>LiDAR depth; null with no AR provider.</summary>
        public ArDepthSource LidarDepth { get; private set; }
        /// <summary>Ray-cast depth; null on the phone.</summary>
        public SyntheticDepthSource SyntheticDepth { get; private set; }
        /// <summary>The source the platform chose: what StopReplay returns to.</summary>
        public IPoseSource DefaultSource { get; private set; }
        /// <summary>The running replay, or null.</summary>
        public PoseLogReplaySource Replay { get; private set; }

        ISpaceHost _host;

        ArPoseSystems() { }

        /// <summary>
        /// Builds everything from what the composition root holds: the boot context (XrOrigin, TrackedCamera,
        /// Head) and the scene's AR Session. Nothing is searched for. logDirectory is where pose logs are
        /// recorded (persistentDataPath/logs on the phone); null leaves recording to an explicit Start.
        /// Publishes nothing and adds no frame hook: call Publish for that.
        /// </summary>
        public static ArPoseSystems Create(AtlasBootContext boot, ARSession session, string logDirectory = null)
        {
            if (boot == null) throw new ArgumentNullException(nameof(boot));
            var systems = new ArPoseSystems();
            XROrigin origin = boot.XrOrigin != null ? boot.XrOrigin.GetComponent<XROrigin>() : null;
            var rig = new ArSessionRig(origin, session);
            var pipeline = new PosePipeline(boot, session) { Rig = rig };
            var recorder = new PoseLogRecorder { Directory = logDirectory ?? "" };
            pipeline.Recorder = recorder;

            systems.Rig = rig;
            systems.Pipeline = pipeline;
            systems.Recorder = recorder;
            systems.Walker = new KeyboardWalkerPoseSource();

            if (rig.HasAr)
            {
                UnityEngine.Transform head = boot.Head != null ? boot.Head : boot.TrackedCamera;
                UnityEngine.Transform sessionSpace = boot.TrackedCamera != null ? boot.TrackedCamera.parent : null;
                systems.ArCamera = new ArCameraPoseSource(head, boot.XrOrigin, session, sessionSpace);
                systems.LidarDepth = new ArDepthSource(rig.Occlusion, rig.Camera, boot.TrackedCamera);
                systems.DefaultSource = systems.ArCamera;
                recorder.Frame = PoseFrameKind.Session;
            }
            else
            {
                systems.DefaultSource = systems.Walker;
                recorder.Frame = PoseFrameKind.Room;
            }

            if (rig.Backend == XrBackend.ARKit && rig.HasAr)
            {
                pipeline.Depth = systems.LidarDepth;
            }
            else
            {
                // XR Simulation has no CPU depth image, and with no provider there is no depth at all.
                systems.SyntheticDepth = new SyntheticDepthSource();
                pipeline.Depth = systems.SyntheticDepth;
            }
            pipeline.Source = systems.DefaultSource;
            return systems;
        }

        /// <summary>
        /// Publishes IPosePipeline, PosePipeline, ArSessionRig, PoseLogRecorder and this object through
        /// AtlasServices, adds the pipeline as the frame loop's step 1 and step 3, and follows
        /// ISpaceHost.RoomChanged once the boot has finished. loop may be null in a test.
        /// </summary>
        public void Publish(IFrameLoop loop)
        {
            AtlasServices.Register<IPosePipeline>(Pipeline);
            AtlasServices.Register(Pipeline);
            AtlasServices.Register(Rig);
            AtlasServices.Register(Recorder);
            AtlasServices.Register(this);
            if (loop != null)
            {
                loop.AddSense(Pipeline);
                loop.SetPoseFilter(Pipeline);
            }
            AtlasServices.Booted += OnBooted;
        }

        /// <summary>Makes a room the room frame: the pipeline, the ray-cast depth and the recorder's header take it. Null clears it.</summary>
        public void SetRoom(RoomModel room)
        {
            if (SyntheticDepth != null) SyntheticDepth.Room = room;
            Recorder.RoomId = room != null ? room.Id ?? "" : "";
            // With a room active the raw stream is in the room frame whatever the source.
            Recorder.Frame = room != null || !Rig.HasAr ? PoseFrameKind.Room : PoseFrameKind.Session;
            Pipeline.Room = room;
        }

        /// <summary>
        /// Replaces the head with a recorded walk from the next frame. False, with the reason, when the file
        /// cannot be read. The log's "room" header names the RoomModel it was recorded in; activating that
        /// room is the caller's step.
        /// </summary>
        public bool StartReplay(string path, bool loop, out string error)
        {
            error = null;
            PoseLogReplaySource replay;
            try
            {
                replay = PoseLogReplaySource.FromFile(path, ReplayMode.RealTime, loop);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException ||
                                      e is ArgumentException || e is NotSupportedException)
            {
                error = e.Message;
                return false;
            }
            StartReplay(replay);
            return true;
        }

        /// <summary>Replaces the head with a replay source from the next frame.</summary>
        public void StartReplay(PoseLogReplaySource replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            replay.Restart();
            Replay = replay;
            Pipeline.Source = replay;
        }

        /// <summary>Back to the platform's own source.</summary>
        public void StopReplay()
        {
            Replay = null;
            Pipeline.Source = DefaultSource;
        }

        /// <summary>Stops the recorder and leaves the room event. The AR objects belong to the scene.</summary>
        public void Dispose()
        {
            AtlasServices.Booted -= OnBooted;
            if (_host != null) _host.RoomChanged -= SetRoom;
            _host = null;
            Recorder.Dispose();
        }

        void OnBooted()
        {
            ISpaceHost host;
            if (!AtlasServices.TryGet(out host)) return;
            _host = host;
            host.RoomChanged += SetRoom;
            if (host.Room != null) SetRoom(host.Room);
        }
    }
}
