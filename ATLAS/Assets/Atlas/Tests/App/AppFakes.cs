using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>Stand-ins shared by the app's headless tests. Every one writes what it was asked into a log.</summary>
    static class Rooms
    {
        public static RoomModel Make(string id, float lapSide = 2f, bool standing = false)
        {
            return new RoomModel { Id = id, LapSide = lapSide, Standing = standing, FreeRect = new FreeRect(3f, 3f) };
        }
    }

    sealed class FakeSense : ISenseStep
    {
        readonly List<string> _log;
        readonly string _name;
        public float3 Position = new float3(0f, 1.6f, 0f);
        public quaternion Rotation = quaternion.identity;
        public TrackingState Tracking = TrackingState.Normal;
        public bool WritesPose = true;

        public FakeSense(List<string> log, string name) { _log = log; _name = name; }

        public void Sense(ref FrameContext frame)
        {
            if (_log != null) _log.Add(_name);
            if (!WritesPose) return;
            frame.RawHeadPosition = Position;
            frame.RawHeadRotation = Rotation;
            frame.Tracking = Tracking;
        }
    }

    sealed class FakeGuard : ISafetyStep, IStateWriter
    {
        readonly List<string> _log;
        public float3 SeenRaw;
        public float3 SeenGeometry;
        public int Calls;
        public byte Level;

        public FakeGuard(List<string> log) { _log = log; }

        public void Guard(in FrameContext frame)
        {
            if (_log != null) _log.Add("guard");
            SeenRaw = frame.RawHeadPosition;
            SeenGeometry = frame.HeadPosition;
            Calls++;
        }

        public void WriteState(ref StateMessage state)
        {
            state.BumpLevel = Level;
            state.CueActive = Level != 0;
        }
    }

    /// <summary>The pose filter's rule as the pose pipeline applies it: a floor step over 0.2 m is dropped.</summary>
    sealed class FakeFilter : IPoseFilterStep
    {
        readonly List<string> _log;
        float2 _previous;
        bool _have;

        public FakeFilter(List<string> log) { _log = log; }

        public void Filter(ref FrameContext frame)
        {
            if (_log != null) _log.Add("filter");
            frame.HeadPosition = frame.RawHeadPosition;
            frame.HeadRotation = frame.RawHeadRotation;
            float2 floor = frame.RawHeadPosition.xz;
            float2 step = _have ? floor - _previous : float2.zero;
            bool jump = math.length(step) > 0.2f;
            frame.PoseJumpRejected = jump;
            frame.FloorDelta = jump ? float2.zero : step;
            _previous = floor;
            _have = true;
        }
    }

    sealed class FakeUpload : IUploadStep
    {
        readonly List<string> _log;
        readonly string _name;
        public FakeUpload(List<string> log, string name) { _log = log; _name = name; }
        public void Upload(in FrameContext frame) { if (_log != null) _log.Add(_name); }
    }

    sealed class FakeAfter : IAfterFrameStep
    {
        readonly List<string> _log;
        readonly string _name;
        public int Calls;
        public FakeAfter(List<string> log, string name) { _log = log; _name = name; }
        public void AfterFrame(in FrameContext frame) { Calls++; if (_log != null) _log.Add(_name); }
    }

    sealed class FakeSink : IStateSink
    {
        readonly List<string> _log;
        public StateMessage Last;
        public int Calls;
        public FakeSink(List<string> log) { _log = log; }
        public void Send(in StateMessage state) { Last = state; Calls++; if (_log != null) _log.Add("sink"); }
    }

    sealed class FakeSpaceFrame : ISpaceFrame
    {
        readonly List<string> _log;
        public FakeSpaceFrame(List<string> log) { _log = log; }
        public void TickSpace(in FrameContext frame) { if (_log != null) _log.Add("space.tick"); }
        public void UploadSpace(in FrameContext frame) { if (_log != null) _log.Add("space.upload"); }
        public void WriteState(ref StateMessage state) { if (_log != null) _log.Add("space.state"); }
    }

    /// <summary>A pose pipeline that only says whether the room frame is in force.</summary>
    sealed class FakePipeline : IPosePipeline
    {
        SensorFrame _sensors;
        public int Resets;
        public List<string> Log;

        public bool RoomActive { get { return _sensors.RoomActive; } set { _sensors.RoomActive = value; } }

        public IPoseSource Source { get; set; }
        public IDepthSource Depth { get; set; }
        RoomModel _room;
        public RoomModel Room
        {
            get { return _room; }
            set { _room = value; _sensors.RoomActive = value != null; }
        }
        public ref readonly SensorFrame Sensors { get { return ref _sensors; } }
        public HeadPose Raw { get { return _sensors.Head; } }
        public HeadPose Geometry { get { return _sensors.Head; } }
        public float2 Step { get { return float2.zero; } }
        public float2 PathFloor { get { return float2.zero; } }
        public bool JumpRejected { get { return false; } }
        public int JumpCount { get { return 0; } }
        public bool TrackingLost { get { return false; } }
        public void ResetGeometry() { Resets++; if (Log != null) Log.Add("pipeline.reset"); }
        public float GyroPredictMs { get; set; }
        public quaternion PredictionRotation { get { return quaternion.identity; } }
#pragma warning disable 67
        public event Action<TrackingState> TrackingChanged;
        public event Action TrackingLostRaised;
        public event Action TrackingRegained;
        public event Action<float> JumpRejectedRaised;
#pragma warning restore 67
    }

    sealed class FakeWalker : IWalkerReset
    {
        public List<string> Log;
        public int Resets;
        public int Latches;
        public bool RoomResetPending { get; set; }
        public void ResetWalkerToFloor() { Resets++; if (Log != null) Log.Add("walker.reset"); }
        public void LatchRoomReset() { Latches++; RoomResetPending = true; if (Log != null) Log.Add("walker.latch"); }
    }

    sealed class FakeStage : ISpaceStage
    {
        public readonly List<float> Fades = new List<float>();
        public int Resets;
        public float Fade = 1f;
        public void SetFade(float fade) { Fade = fade; Fades.Add(fade); }
        public readonly List<SpaceId?> Nexts = new List<SpaceId?>();
        public void ResetForSpace(SpaceId? next) { Resets++; Nexts.Add(next); }
    }

    sealed class FakeModule : ISpaceModule, IUploadStep
    {
        public readonly List<string> Log;
        public int Configures, Enters, Exits, Ticks, Uploads, States;
        public RoomModel Room;
        public bool ThrowInConfigure, ThrowInEnter, ThrowInTick;
        public ParamEntry Param;
        /// <summary>The pose SpaceLab entered the module with.</summary>
        public WalkerPose Start;
        /// <summary>Runs inside Enter: what a curved space does to the shared kernel there.</summary>
        public Action OnEnter;

        public FakeModule(SpaceId id, List<string> log, bool registered = true)
        {
            Id = id;
            Registered = registered;
            Log = log;
        }

        public SpaceId Id { get; private set; }
        public bool Registered { get; private set; }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            if (ThrowInConfigure) throw new InvalidOperationException("configure failed");
            Configures++;
            Room = room;
            Param = registry.DeclareFloat(SpaceIds.ParamGroup(Id) + ".test", SpaceIds.ParamGroup(Id), 0f, 1f, 0.5f);
            Log.Add("configure " + SpaceIds.Code(Id));
        }

        public void Enter(in WalkerPose start)
        {
            Start = start;
            if (OnEnter != null) OnEnter();
            if (ThrowInEnter) throw new InvalidOperationException("enter failed");
            Enters++;
            Log.Add("enter " + SpaceIds.Code(Id));
        }

        public void Tick(in FrameContext frame)
        {
            if (ThrowInTick) throw new InvalidOperationException("tick failed");
            Ticks++;
        }

        public void Upload(in FrameContext frame) { Uploads++; }
        public void Exit() { Exits++; Log.Add("exit " + SpaceIds.Code(Id)); }
        public void WriteState(ref StateMessage state) { States++; state.Sheet = (short)(10 + (int)Id); }
    }

    sealed class FakeLoader : ISpaceLoader
    {
        public readonly List<string> Log;
        public readonly HashSet<SpaceId> Available = new HashSet<SpaceId>();
        public readonly HashSet<SpaceId> Broken = new HashSet<SpaceId>();
        public readonly List<FakeModule> Created = new List<FakeModule>();
        public Action<FakeModule> OnCreate;
        /// <summary>Frames a load takes; 0 finishes in the frame it began (a factory).</summary>
        public int LoadFrames;
        public int Unloads;
        SpaceId _target;
        int _wait;

        public FakeLoader(List<string> log, params SpaceId[] available)
        {
            Log = log;
            for (int i = 0; i < available.Length; i++) Available.Add(available[i]);
        }

        public bool IsAvailable(SpaceId id) { return Available.Contains(id); }

        public void Begin(SpaceId id)
        {
            _target = id;
            _wait = LoadFrames;
        }

        public bool TryFinish(out ISpaceModule module)
        {
            module = null;
            if (_wait > 0) { _wait--; return false; }
            if (Broken.Contains(_target)) return true;
            var made = new FakeModule(_target, Log);
            if (OnCreate != null) OnCreate(made);
            Created.Add(made);
            module = made;
            return true;
        }

        public void Unload(ISpaceModule module) { Unloads++; }
    }

    sealed class FakeEvents : IEventLog
    {
        public readonly List<string> Lines = new List<string>();
        public void Log(string ev) { Lines.Add(ev); }
        public void Log(string ev, string key, double value) { Lines.Add(ev + " " + key + "=" + value); }
        public void Log(string ev, string key, string value) { Lines.Add(ev + " " + key + "=" + value); }
        public void Log(string ev, string key1, double value1, string key2, double value2) { Lines.Add(ev); }
        public void LogJson(string ev, string dataJson) { Lines.Add(ev + " " + dataJson); }

        public bool Has(string start)
        {
            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].StartsWith(start, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    sealed class FakeHost : ISpaceHost
    {
        public readonly List<string> Calls = new List<string>();
        public bool Accept = true;
        public SpaceHostState State { get; set; }
        public bool HasSpace { get; set; }
        public SpaceId Current { get; set; }
        public ISpaceModule Module { get { return null; } }
        public RoomModel Room { get; set; }
        public bool Standing { get { return false; } }
        public ushort WorldVersion { get; set; }
        public bool IsAvailable(SpaceId id) { return Accept; }
        public bool RequestSwitch(SpaceId target) { Calls.Add("switch " + SpaceIds.Code(target)); return Accept; }
        public bool ReturnHome() { Calls.Add("home"); return true; }
        public void SetRoom(RoomModel room) { Room = room; Calls.Add("room"); if (RoomChanged != null) RoomChanged(room); }
        public void BumpWorldVersion() { WorldVersion++; }
        public void RaiseEntered(SpaceId id) { Current = id; HasSpace = true; if (SpaceEntered != null) SpaceEntered(id); }
        public void RaiseExited(SpaceId id) { if (SpaceExited != null) SpaceExited(id); }
        public event Action<SpaceId> SpaceEntered;
        public event Action<SpaceId> SpaceExited;
        public event Action<RoomModel> RoomChanged;
    }

    sealed class FakeLoop : IFrameLoop
    {
        public readonly List<string> Calls = new List<string>();
        FrameContext _frame;
        public ref readonly FrameContext Current { get { return ref _frame; } }
        public void AddSense(ISenseStep step) { }
        public void SetGuard(ISafetyStep step) { }
        public void SetPoseFilter(IPoseFilterStep step) { }
        public void AddUpload(IUploadStep step) { }
        public void SetStateSink(IStateSink sink) { }
        public void AddAfterFrame(IAfterFrameStep step) { }
        public void ResetWalkerToFloor() { Calls.Add("reset"); }
        public void ResetWalker(in WalkerPose pose) { Calls.Add("resetPose"); }
    }
}
