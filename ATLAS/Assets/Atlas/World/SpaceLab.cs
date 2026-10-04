// PLACEHOLDER (lane A, P1-A08): contract surface only; replaced by lane A's implementation
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>
    /// Owns the registered spaces and switches between them (Master Plan §6.2, World; P1-A08).
    /// Placeholder: the switch is synchronous at the next Tick, with no fade or scene load (PH-4);
    /// callers must poll Switching or Active.
    /// </summary>
    public sealed class SpaceLab
    {
        readonly Dictionary<SpaceId, ISpaceModule> _modules = new Dictionary<SpaceId, ISpaceModule>();
        ISpaceModule _pending;

        public SpaceLab(RoomModel room, ParamRegistry registry) { Room = room; Registry = registry; }

        public RoomModel Room { get; }
        public ParamRegistry Registry { get; }
        public ISpaceModule Active { get; private set; }   // null until the first switch
        // The Home rebuild's meshes, set by the app (PH-25, Master Plan §6.2, §9.3): Atlas.Spaces.* may not reference Atlas.Scan.
        public IHomeShell Home { get; set; }                // null until the Home rebuild has run
        public bool Switching => _pending != null;          // true from Request until the switch is applied

        public void Register(ISpaceModule module)           // one per SpaceId; a duplicate throws
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_modules.ContainsKey(module.Id)) throw new InvalidOperationException("Space " + module.Id + " is already registered");
            _modules.Add(module.Id, module);
        }

        public void Request(SpaceId id)
        {
            if (!_modules.TryGetValue(id, out var m)) throw new InvalidOperationException("Space " + id + " is not registered");
            _pending = m;
        }

        // Master Plan §6.3 steps 4, 5, 6 on the active module.
        public void Tick(in FrameContext frame)
        {
            if (_pending != null)
            {
                var next = _pending; _pending = null;
                Active?.Exit();
                next.Configure(Room, Registry);
                next.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
                Active = next;
            }
            if (Active == null) return;
            if (Active is IKernelStage k) k.KernelStep(frame);   // step 4
            Active.Tick(frame);                                  // step 5
            if (Active is IUploadStage u) u.Upload(frame);       // step 6
        }

        public void WriteState(ref StateMessage state) => Active?.WriteState(ref state);
    }
}
