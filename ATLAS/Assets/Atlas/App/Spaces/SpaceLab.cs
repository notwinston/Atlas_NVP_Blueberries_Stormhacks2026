using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>How SpaceLab gets a module: the scene-or-factory rule of contract README F3.</summary>
    public interface ISpaceLoader
    {
        /// <summary>The space has a scene in the build or a registered factory.</summary>
        bool IsAvailable(SpaceId id);
        /// <summary>Starts loading. Exactly one load runs at a time.</summary>
        void Begin(SpaceId id);
        /// <summary>False while a scene is still loading. True when the load has ended: module is the new
        /// module, or null when the space could not be created.</summary>
        bool TryFinish(out ISpaceModule module);
        /// <summary>Destroys a module's root, or unloads its scene.</summary>
        void Unload(ISpaceModule module);
    }

    /// <summary>What SpaceLab changes outside the module: the world fade and the look between two spaces.</summary>
    public interface ISpaceStage
    {
        /// <summary>The world fade: 1 shows the world, 0 is black. The head-locked layer ignores it.</summary>
        void SetFade(float fade);
        /// <summary>Between two spaces: default fog and background, room content back to its default mode, then
        /// the furniture level of the space about to be entered (null when none is).</summary>
        void ResetForSpace(SpaceId? next);
    }

    /// <summary>
    /// Hosts exactly one space at a time (Master Plan task P1-A08): loads it by the scene-or-factory rule,
    /// configures it from the active RoomModel and the registry, enters it, ticks it and leaves it, with a
    /// fade through black between two spaces. Engine-free: the loader and the stage bring the engine.
    /// A switch advances at step 8b of the frame, so Bump Guard runs in every frame of it.
    /// <para>
    /// The walker's kernel is shared. Between two spaces it is the flat kernel of the room; a curved space
    /// configures it in its Enter (IDialKernel), and SpaceLab puts it back to flat after that space's Exit,
    /// with the walker on the room's floor, so the space need not. One switch keeps the walker instead:
    /// Curved Walk to Aligned Cells (Mode 1 to Mode 2, Master Plan sections 4.8 and 4.9) enters with the
    /// tile and frame Curved Walk stood in.
    /// </para>
    /// </summary>
    public sealed class SpaceLab : ISpaceHost, ISpaceFrame, IAfterFrameStep
    {
        public const float FadeOutSeconds = 0.25f;
        public const float FadeInSeconds = 0.25f;

        readonly ParamRegistry _registry;
        readonly IWalkerKernel _kernel;
        readonly IWalkerReset _walker;
        readonly ISpaceLoader _loader;
        readonly ISpaceStage _stage;

        SpaceHostState _state = SpaceHostState.Empty;
        ISpaceModule _module;
        IUploadStep _moduleUpload;
        SpaceId _current;
        bool _hasSpace;
        RoomModel _room;
        ushort _worldVersion;

        // The switch under way.
        SpaceId _target;
        bool _toEmpty;          // leave the space and enter none (the room was cleared, or Home itself failed)
        bool _exited;           // the old space has been left
        bool _loadBegun;
        bool _fallbackTried;
        float _timer;
        bool _keepPose;         // Mode 1 to Mode 2: the new space enters with the pose the old one left
        WalkerPose _keptPose;

        // The newest request made while a switch was under way.
        bool _hasPending;
        bool _pendingReload;
        SpaceId _pending;

        bool _faulted;          // the entered module threw on the frame path; it is not called again
        bool _shutDown;
        int _switchCount;

        public SpaceLab(ParamRegistry registry, IWalkerKernel kernel, IWalkerReset walker, ISpaceLoader loader,
            ISpaceStage stage)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (kernel == null) throw new ArgumentNullException(nameof(kernel));
            if (walker == null) throw new ArgumentNullException(nameof(walker));
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            _registry = registry;
            _kernel = kernel;
            _walker = walker;
            _loader = loader;
            _stage = stage;

            // A space's parameters exist from boot but belong to no entered space yet: shown folded.
            for (int i = 0; i < SpaceIds.Count; i++)
                _registry.SetGroupActive(SpaceIds.ParamGroup((SpaceId)i), false);
        }

        /// <summary>The session's event log. Set by the composition root once telemetry exists. May be null.</summary>
        public IEventLog Events { get; set; }

        /// <summary>Completed switches since boot.</summary>
        public int SwitchCount { get { return _switchCount; } }

        // ---- ISpaceHost -----------------------------------------------------------------------------------

        public SpaceHostState State { get { return _state; } }
        public bool HasSpace { get { return _hasSpace; } }
        public SpaceId Current { get { return _current; } }
        public ISpaceModule Module { get { return _module; } }
        public RoomModel Room { get { return _room; } }
        public bool Standing { get { return _room != null && _room.Standing; } }
        public ushort WorldVersion { get { return _worldVersion; } }

        public event Action<SpaceId> SpaceEntered;
        public event Action<SpaceId> SpaceExited;
        public event Action<RoomModel> RoomChanged;

        public bool IsAvailable(SpaceId id)
        {
            return SpaceIds.IsDefined(id) && _loader.IsAvailable(id);
        }

        public bool RequestSwitch(SpaceId target)
        {
            return Request(target, false);
        }

        public bool ReturnHome()
        {
            if (_state == SpaceHostState.Running && _hasSpace && _current == SpaceId.Home && !_faulted)
            {
                // Already there: only the walker is put back on the room's floor. No fade.
                if (!_walker.RoomResetPending) _walker.ResetWalkerToFloor();
                return true;
            }
            return Request(SpaceId.Home, false);
        }

        public void SetRoom(RoomModel room)
        {
            if (_shutDown) return;
            _room = room;
            _worldVersion++;
            if (room != null)
            {
                // A curved space keeps the kernel it configured until its Exit, which the reload below brings;
                // the flat kernel of the new room follows from RestoreFlatKernel.
                if (!_hasSpace || KernelIsFlat()) _kernel.Configure(KernelConfig.Flat(FlatSide()));
                // The head is still in the old frame: the walker is placed by the frame loop in the first
                // frame of the room frame, and no space is loaded before that.
                _walker.LatchRoomReset();
            }

            Action<RoomModel> changed = RoomChanged;
            if (changed != null) changed(room);

            if (room == null)
            {
                _hasPending = false;
                if (_hasSpace || _state == SpaceHostState.Loading) BeginLeave();
                return;
            }
            if (_state == SpaceHostState.Running && _hasSpace) Request(_current, true);
            else if (_state == SpaceHostState.FadingIn)
            {
                // Entered with the old room a moment ago: load it again once this fade has ended.
                _pending = _current;
                _pendingReload = true;
                _hasPending = true;
            }
            // FadingOut, Loading: the next module has not been configured yet and takes the new room.
        }

        public void BumpWorldVersion() { _worldVersion++; }

        // ---- ISpaceFrame ----------------------------------------------------------------------------------

        public void TickSpace(in FrameContext frame)
        {
            if (!_hasSpace || _faulted) return;
            try { _module.Tick(in frame); }
            catch (Exception e) { Fault("Tick", e); }
        }

        public void UploadSpace(in FrameContext frame)
        {
            if (!_hasSpace || _faulted || _moduleUpload == null) return;
            try { _moduleUpload.Upload(in frame); }
            catch (Exception e) { Fault("Upload", e); }
        }

        public void WriteState(ref StateMessage state)
        {
            if (!_hasSpace || _faulted) return;
            try { _module.WriteState(ref state); }
            catch (Exception e) { Fault("WriteState", e); }
        }

        // ---- step 8b --------------------------------------------------------------------------------------

        public void AfterFrame(in FrameContext frame) { Advance(frame.DeltaTime); }

        /// <summary>Advances a switch by one frame. Called once per frame at step 8b.</summary>
        public void Advance(float deltaTime)
        {
            if (_shutDown) return;

            if (_faulted && _state != SpaceHostState.FadingOut && _state != SpaceHostState.Loading)
            {
                // The module is broken: back to Home, or to no space at all when Home is the broken one.
                _hasPending = false;
                if (_current != SpaceId.Home && _room != null && _loader.IsAvailable(SpaceId.Home)) Begin(SpaceId.Home);
                else BeginLeave();
            }

            if (_state == SpaceHostState.FadingOut)
            {
                _timer += deltaTime;
                SetFade(1f - _timer / FadeOutSeconds);
                if (_timer < FadeOutSeconds) return;
                _state = SpaceHostState.Loading;
            }

            if (_state == SpaceHostState.Loading)
            {
                SetFade(0f);
                if (!AdvanceLoad()) return;
                if (_state != SpaceHostState.FadingIn) return;      // ended in Empty
                _timer = 0f;
                return;                                             // the fade-in starts with the next frame
            }

            if (_state == SpaceHostState.FadingIn)
            {
                _timer += deltaTime;
                SetFade(_timer / FadeInSeconds);
                if (_timer < FadeInSeconds) return;
                SetFade(1f);
                _state = SpaceHostState.Running;
                _switchCount++;
            }

            if ((_state == SpaceHostState.Running || _state == SpaceHostState.Empty) && _hasPending)
            {
                _hasPending = false;
                bool reload = _pendingReload;
                _pendingReload = false;
                Request(_pending, reload);
            }
        }

        /// <summary>Leaves the entered space at once, with no fade. For shutdown.</summary>
        public void Shutdown()
        {
            if (_shutDown) return;
            ExitCurrent();
            _shutDown = true;
            _state = SpaceHostState.Empty;
            _hasPending = false;
        }

        // ---- the switch -----------------------------------------------------------------------------------

        bool Request(SpaceId target, bool reload)
        {
            if (_shutDown) return false;
            if (_room == null) { Refuse(target, "noRoom"); return false; }
            if (!IsAvailable(target)) { Refuse(target, "unavailable"); return false; }

            if (_state == SpaceHostState.Running || _state == SpaceHostState.Empty)
            {
                if (!reload && _state == SpaceHostState.Running && _hasSpace && _current == target && !_faulted) return false;
                Begin(target);
                return true;
            }
            // A switch is under way: the newest request waits for it and replaces any older one.
            _pending = target;
            _pendingReload = reload;
            _hasPending = true;
            return true;
        }

        void Begin(SpaceId target)
        {
            _target = target;
            _toEmpty = false;
            _exited = false;
            _loadBegun = false;
            _fallbackTried = false;
            _timer = 0f;
            // With nothing entered there is nothing to fade out.
            _state = _hasSpace ? SpaceHostState.FadingOut : SpaceHostState.Loading;
        }

        void BeginLeave()
        {
            _toEmpty = true;
            _exited = false;
            _loadBegun = false;
            _timer = 0f;
            _state = _hasSpace ? SpaceHostState.FadingOut : SpaceHostState.Loading;
        }

        // Steps 2 to 6 of the switch. True when the load has ended (in FadingIn or in Empty).
        bool AdvanceLoad()
        {
            if (!_exited)
            {
                // Mode 1 to Mode 2 keeps the tile and the frame (Master Plan section 4.8): read before Exit,
                // while the kernel is still Curved Walk's. Only a walker standing in the {4,5} world has a
                // tile Aligned Cells can take; from the flat stand it enters registered like any other space.
                _keepPose = !_toEmpty && _hasSpace && !_faulted && _current == SpaceId.CurvedWalk
                    && _target == SpaceId.AlignedCells && !_walker.RoomResetPending
                    && _kernel.Graph != null && _kernel.Graph.Q == 5 && _kernel.Kappa == Curvature.Hyperbolic;
                if (_keepPose) _keptPose = _kernel.Pose;
                ExitCurrent();
                _exited = true;
                if (_stage != null)
                {
                    _stage.ResetForSpace(_toEmpty ? (SpaceId?)null : _target);
                    _stage.SetFade(0f);
                }
            }

            if (_toEmpty)
            {
                EndEmpty();
                return true;
            }

            // The room was replaced and the head is not in its frame yet: the walker cannot be placed.
            if (_walker.RoomResetPending) return false;

            while (true)
            {
                if (!_loadBegun)
                {
                    _loader.Begin(_target);
                    _loadBegun = true;
                }
                ISpaceModule module;
                if (!_loader.TryFinish(out module)) return false;     // a scene is still loading
                if (module != null && Start(module))
                {
                    _state = SpaceHostState.FadingIn;
                    return true;
                }

                Log("space.loadFailed", "space", SpaceIds.Code(_target));
                if (!_fallbackTried && _target != SpaceId.Home && _loader.IsAvailable(SpaceId.Home))
                {
                    _fallbackTried = true;
                    _target = SpaceId.Home;
                    _loadBegun = false;
                    continue;
                }
                EndEmpty();
                return true;
            }
        }

        bool Start(ISpaceModule module)
        {
            string group = SpaceIds.ParamGroup(_target);
            try
            {
                module.Configure(_room, _registry);
            }
            catch (Exception e)
            {
                Log("space.error", "configure", e.Message);
                RestoreFlatKernel();
                _loader.Unload(module);
                return false;
            }
            _worldVersion++;
            _registry.SetGroupActive(group, true);

            try
            {
                if (_keepPose && _target == SpaceId.AlignedCells)
                {
                    // The same tile id and frame as in Mode 1; the kernel itself is flat again.
                    _keepPose = false;
                    module.Enter(in _keptPose);
                }
                else
                {
                    // A registered space matches the real room: the walker starts where the head is.
                    if (module.Registered) _walker.ResetWalkerToFloor();
                    module.Enter(_kernel.Pose);
                }
            }
            catch (Exception e)
            {
                Log("space.error", "enter", e.Message);
                try { module.Exit(); } catch (Exception) { /* it never entered; its root goes next */ }
                RestoreFlatKernel();
                _registry.SetGroupActive(group, false);
                _loader.Unload(module);
                return false;
            }

            _module = module;
            _moduleUpload = module as IUploadStep;
            _current = _target;
            _hasSpace = true;
            _faulted = false;
            Action<SpaceId> entered = SpaceEntered;
            if (entered != null) entered(_current);
            return true;
        }

        void ExitCurrent()
        {
            if (!_hasSpace) return;
            ISpaceModule module = _module;
            SpaceId id = _current;
            try { module.Exit(); }
            catch (Exception e) { Log("space.error", "exit", e.Message); }
            _hasSpace = false;
            _module = null;
            _moduleUpload = null;
            _faulted = false;
            Action<SpaceId> exited = SpaceExited;
            if (exited != null) exited(id);
            _registry.SetGroupActive(SpaceIds.ParamGroup(id), false);
            RestoreFlatKernel();
            _loader.Unload(module);
        }

        bool KernelIsFlat()
        {
            ITileGraph graph = _kernel.Graph;
            return graph == null || graph.Q == 4;
        }

        double FlatSide()
        {
            return _room != null && _room.LapSide > 0f ? _room.LapSide : KernelConstants.DefaultFlatTileSideMetres;
        }

        // After a space that configured the kernel curved: the flat kernel of the room again, the walker where
        // the head is. A flat kernel is left as it is, pose and all, as between two flat spaces before.
        void RestoreFlatKernel()
        {
            if (KernelIsFlat()) return;
            _kernel.Configure(KernelConfig.Flat(FlatSide()));
            // With a room reset latched the frame loop places the walker itself, in the room's first frame.
            if (!_walker.RoomResetPending) _walker.ResetWalkerToFloor();
        }

        // No space: the room content stays visible.
        void EndEmpty()
        {
            _toEmpty = false;
            _state = SpaceHostState.Empty;
            SetFade(1f);
        }

        void SetFade(float fade)
        {
            if (_stage != null) _stage.SetFade(fade < 0f ? 0f : (fade > 1f ? 1f : fade));
        }

        void Fault(string where, Exception e)
        {
            _faulted = true;
            Log("space.error", where, e.Message);
        }

        void Refuse(SpaceId target, string reason)
        {
            IEventLog events = Events;
            if (events != null)
                events.LogJson("space.refused", "{\"space\":\"" + SpaceIds.Code(target) + "\",\"reason\":\"" + reason + "\"}");
        }

        void Log(string ev, string key, string value)
        {
            IEventLog events = Events;
            if (events != null) events.Log(ev, key, value);
        }
    }
}
