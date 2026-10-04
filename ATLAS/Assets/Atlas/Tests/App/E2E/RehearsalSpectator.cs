using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Spaces.AlignedCells;
using Atlas.Spaces.CurvedWalk;
using Atlas.Spaces.SmallPlanet;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>The Station's room source with no Forge: a fixture, read from the Station's own copy of the file.</summary>
    sealed class FixtureRooms : ISpectatorRooms
    {
        string _pending;
        bool _waiting;
        public int Asked { get; private set; }

        public void Begin(string roomId, string forgeUrl, RoomOutlineMessage outline)
        {
            Asked++;
            _pending = roomId;
            _waiting = true;
        }

        public bool TryTake(out RoomModel room)
        {
            room = null;
            if (!_waiting) return false;
            _waiting = false;
            if (RoomFixtures.IsFixture(_pending)) room = RoomFixtures.LoadFromProject(_pending);
            return true;
        }
    }

    /// <summary>
    /// A space as the Station holds it, with no engine: the Station's side of the same cores the phone runs.
    /// Curved Walk generates its world again from the Station's copy of the room; Aligned Cells and Small Planet
    /// take the walker's frame from each State (ISpaceStateReader). It draws nothing.
    /// </summary>
    sealed class StationModule : ISpaceModule, ISpaceStateReader, ISpaceWorld
    {
        public StationModule(SpaceId id) { Id = id; }

        public SpaceId Id { get; private set; }
        public bool Registered { get { return Id != SpaceId.CurvedWalk && Id != SpaceId.SmallPlanet; } }

        public CurvedWalkCore Curved { get; private set; }
        public AlignedCellsCore Cells { get; private set; }
        public SmallPlanetCore Planet { get; private set; }
        /// <summary>The newest State handed over by the host, and how many were.</summary>
        public StateMessage Last;
        public int Reads, Ticks;

        // What the Spectator compares with the phone's world event (lane D contract, section 2.5). A space
        // with no generator names seed 0 and the first version, as the phone's link does for it.
        public long WorldSeed { get { return Curved != null ? CurvedWalkCore.WorldSeed : 0; } }
        public int GeneratorVersion { get { return Curved != null ? CurvedWalkCore.GeneratorVersion : 1; } }
        public string ContentHash { get { return Curved != null ? Curved.ContentHash : string.Empty; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            switch (Id)
            {
                case SpaceId.CurvedWalk:
                    Curved = new CurvedWalkCore();
                    Curved.Configure(room, registry);
                    break;
                case SpaceId.AlignedCells:
                    Cells = new AlignedCellsCore();
                    Cells.Configure(room, registry);
                    break;
                case SpaceId.SmallPlanet:
                    Planet = new SmallPlanetCore();
                    Planet.Configure(room, registry);
                    break;
            }
        }

        public void Enter(in WalkerPose start)
        {
            if (Cells != null) Cells.Enter(WalkerPose.Start, double2.zero);
            if (Planet != null) Planet.Enter(null, double2.zero);     // no dial kernel: nothing is driven here
        }

        public void ReadState(in StateMessage state)
        {
            Last = state;
            Reads++;
            if (Cells != null) Cells.ReadState(in state);
            if (Planet != null) Planet.ReadState(in state);
        }

        public void Tick(in FrameContext frame)
        {
            Ticks++;
            if (Cells != null) Cells.Tick(new double2(double.NaN, double.NaN), false);   // the Station's own head plays no part
            if (Planet != null) Planet.Tick(frame.Walker, false, false, false);
        }

        public void Exit()
        {
            if (Cells != null) Cells.Exit();
            if (Planet != null) Planet.Exit();
        }

        public void WriteState(ref StateMessage state) { }
    }

    /// <summary>
    /// The Spectator behind the rehearsal's Hub, put together as SpectatorApp does it: its own registry, its own
    /// walker kernel (never stepped), the real SpectatorHost, SpectatorCore and SpectatorLoop on the rig's
    /// Spectator link client. Only the modules are the engine-free <see cref="StationModule"/>.
    /// </summary>
    sealed class RehearsalSpectator
    {
        public readonly ParamRegistry Registry = new ParamRegistry();
        public readonly HolonomyWalkerKernel Kernel = new HolonomyWalkerKernel();
        public readonly FixtureRooms Rooms = new FixtureRooms();
        public readonly SpectatorHost Host;
        public readonly SpectatorCore Core;
        public readonly SpectatorLoop Loop;
        /// <summary>Every banner other than None the Spectator showed, with the frame it began in.</summary>
        public readonly List<KeyValuePair<int, SpectatorBanner>> Banners = new List<KeyValuePair<int, SpectatorBanner>>(16);
        SpectatorBanner _last = SpectatorBanner.None;

        /// <summary>Attach before the Station starts, so the first Welcome and world event are heard.</summary>
        public RehearsalSpectator(RehearsalRig rig)
        {
            CurvedWalkCore.DeclareParams(Registry);
            AlignedCellsCore.DeclareParams(Registry);
            SmallPlanetCore.DeclareParams(Registry);
            Host = new SpectatorHost(Registry, Kernel) { Create = id => new StationModule(id) };
            Core = new SpectatorCore(Host, Registry, Kernel, Rooms);
            Loop = new SpectatorLoop(Core, Kernel);
            Loop.AddSense(new SpectatorSense(Core));
            Core.Attach(rig.Spectator);
        }

        public StationModule Module { get { return Host.Module as StationModule; } }

        /// <summary>One frame of the Station, after the Spectator's link was polled: steps 1 to 6, then 8b.</summary>
        public void Frame(RehearsalRig rig)
        {
            Loop.LinkUp = rig.Spectator.IsReady;
            Loop.Run(rig.Time, RehearsalRig.Dt);
            Loop.RunAfterFrame();
            if (Core.Banner != _last)
            {
                _last = Core.Banner;
                if (_last != SpectatorBanner.None) Banners.Add(new KeyValuePair<int, SpectatorBanner>(rig.Frames, _last));
            }
        }
    }
}
