using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The space host of the Spectator (Master Plan section 7.3.1; lane D contract, section 5.8): it creates the
    /// walker's space with the same module classes as the phone and drives them from the received State. It has
    /// no fade and no pending switch, because nobody is inside the picture: a switch happens at once. The world
    /// version is the phone's, never its own. Engine-free; the Station passes how a module's objects are
    /// destroyed.
    /// </summary>
    public sealed class SpectatorHost : ISpaceHost
    {
        readonly ParamRegistry _registry;
        readonly IWalkerKernel _kernel;
        ISpaceModule _module;
        ISpaceStateReader _reader;
        IUploadStep _upload;
        SpaceId _current;
        bool _hasSpace;
        RoomModel _room;
        int _refused = -1;

        public SpectatorHost(ParamRegistry registry, IWalkerKernel kernel)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            if (kernel == null) throw new ArgumentNullException("kernel");
            _registry = registry;
            _kernel = kernel;
            Create = SpaceRegistry.Create;
        }

        /// <summary>Makes a module; the registered factory by default. Null from it means the space is not in this build.</summary>
        public Func<SpaceId, ISpaceModule> Create { get; set; }
        /// <summary>Destroys what a module's factory made, after Exit. May be null.</summary>
        public Action<ISpaceModule> Destroy { get; set; }
        /// <summary>Called after a module was created, before Configure: the Station parents its object. May be null.</summary>
        public Action<ISpaceModule> Created { get; set; }
        /// <summary>Between two spaces: the look and the room content go back to their defaults. May be null.</summary>
        public Action BetweenSpaces { get; set; }
        public IEventLog Events { get; set; }

        /// <summary>What went wrong last in a module, or null.</summary>
        public string LastError { get; private set; }
        /// <summary>Spaces entered so far.</summary>
        public int EnterCount { get; private set; }

        // ---- ISpaceHost ----------------------------------------------------------------------------------------

        public SpaceHostState State { get { return _hasSpace ? SpaceHostState.Running : SpaceHostState.Empty; } }
        public bool HasSpace { get { return _hasSpace; } }
        public SpaceId Current { get { return _current; } }
        public ISpaceModule Module { get { return _module; } }
        public RoomModel Room { get { return _room; } }
        public bool Standing { get { return _room != null && _room.Standing; } }

        /// <summary>The phone's <c>State.worldVer</c>, as last received.</summary>
        public ushort WorldVersion { get; set; }

        public event Action<SpaceId> SpaceEntered;
        public event Action<SpaceId> SpaceExited;
        public event Action<RoomModel> RoomChanged;

        public bool IsAvailable(SpaceId id)
        {
            return SpaceIds.IsDefined(id) && SpaceRegistry.IsRegistered(id);
        }

        /// <summary>Enters a space at once. False with no room, when already there, or when the space cannot be made.</summary>
        public bool RequestSwitch(SpaceId target)
        {
            if (_room == null || !SpaceIds.IsDefined(target)) return false;
            if (_hasSpace && _current == target) return false;
            // A space that failed is not tried again on every frame; a new room or another space clears it.
            if (_refused == (int)target) return false;
            Leave();

            ISpaceModule module = null;
            try { module = Create != null ? Create(target) : null; }
            catch (Exception e) { Fail("create", e); }
            if (module == null)
            {
                _refused = (int)target;
                return false;
            }
            Action<ISpaceModule> created = Created;
            if (created != null) created(module);

            string group = SpaceIds.ParamGroup(target);
            try
            {
                module.Configure(_room, _registry);
                _registry.SetGroupActive(group, true);
                module.Enter(_kernel.Pose);
            }
            catch (Exception e)
            {
                Fail("enter", e);
                try { module.Exit(); } catch (Exception) { /* it never entered; its objects go next */ }
                _registry.SetGroupActive(group, false);
                Unload(module);
                _refused = (int)target;
                return false;
            }

            _module = module;
            _reader = module as ISpaceStateReader;
            _upload = module as IUploadStep;
            _current = target;
            _hasSpace = true;
            EnterCount++;
            Action<SpaceId> entered = SpaceEntered;
            if (entered != null) entered(target);
            return true;
        }

        public bool ReturnHome() { return RequestSwitch(SpaceId.Home); }

        /// <summary>Makes a room active, or none with null. The entered space is left; the next State enters it again.</summary>
        public void SetRoom(RoomModel room)
        {
            Leave();
            _room = room;
            _refused = -1;
            Action<RoomModel> changed = RoomChanged;
            if (changed != null) changed(room);
        }

        /// <summary>Nothing: the Spectator's world version is the phone's.</summary>
        public void BumpWorldVersion() { }

        // ---- driven by the Spectator ---------------------------------------------------------------------------

        /// <summary>Leaves the entered space, if any.</summary>
        public void Leave()
        {
            if (!_hasSpace) return;
            ISpaceModule module = _module;
            SpaceId id = _current;
            try { module.Exit(); }
            catch (Exception e) { Fail("exit", e); }
            _hasSpace = false;
            _module = null;
            _reader = null;
            _upload = null;
            Action<SpaceId> exited = SpaceExited;
            if (exited != null) exited(id);
            _registry.SetGroupActive(SpaceIds.ParamGroup(id), false);
            Unload(module);
            Action between = BetweenSpaces;
            if (between != null) between();
        }

        /// <summary>
        /// Steps 5 and 6 of the frame for the entered module: the newest State first (lane A contract,
        /// section 2), then Tick and the module's own upload. A module that throws is left.
        /// </summary>
        public void Tick(in StateMessage state, in FrameContext frame)
        {
            if (!_hasSpace) return;
            try
            {
                if (_reader != null) _reader.ReadState(in state);
                _module.Tick(in frame);
                if (_upload != null) _upload.Upload(in frame);
            }
            catch (Exception e)
            {
                Fail("tick", e);
                _refused = (int)_current;
                Leave();
            }
        }

        void Unload(ISpaceModule module)
        {
            Action<ISpaceModule> destroy = Destroy;
            if (destroy == null) return;
            try { destroy(module); }
            catch (Exception e) { Fail("destroy", e); }
        }

        void Fail(string where, Exception e)
        {
            LastError = where + ": " + e.Message;
            IEventLog events = Events;
            if (events != null) events.Log("space.error", where, e.Message);
        }
    }
}
