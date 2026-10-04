using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-D05 for S2 (MP §7.3.1): the Station re-renders Curved Walk from the phone's States. The phone's side is the replay
    // rig (the app's kernel stepped at step 4, CurvedWalkCore at step 5); every State goes through the wire's bytes to the
    // real Spectator core, host and loop, whose module is the same CurvedWalkCore read through ReadState. No engine.
    public class StationReplayTests
    {
        /// <summary>CurvedWalkSpace without its views: the same calls on the same core.</summary>
        sealed class Module : ISpaceModule, ISpaceStateReader, ISpaceWorld
        {
            public readonly CurvedWalkCore Core = new CurvedWalkCore();
            public readonly List<SpaceId> Requests = new List<SpaceId>();
            readonly IDialKernel kernel;
            public int WorldBumps, Ticks;

            public Module(IDialKernel kernel) { this.kernel = kernel; }
            public SpaceId Id => SpaceId.CurvedWalk;
            public bool Registered => false;
            public long WorldSeed => CurvedWalkCore.WorldSeed;
            public int GeneratorVersion => CurvedWalkCore.GeneratorVersion;
            public string ContentHash => Core.ContentHash;

            public void Configure(RoomModel room, ParamRegistry registry)
            {
                Core.Configure(room, registry);
                Core.RequestSwitch = id => { Requests.Add(id); return true; };
                Core.WorldChanged = () => WorldBumps++;
            }

            public void Enter(in WalkerPose start) { Core.Enter(kernel); Core.Register(double2.zero); }
            public void ReadState(in StateMessage state) { Core.ReadState(in state); }
            public void Tick(in FrameContext frame) { Core.Tick(in frame, default); Ticks++; }
            public void Exit() { Core.Exit(); }
            public void WriteState(ref StateMessage state) { Core.WriteState(ref state); }
        }

        sealed class OneRoom : ISpectatorRooms
        {
            readonly RoomModel room;
            public OneRoom(RoomModel room) { this.room = room; }
            public void Begin(string roomId, string forgeUrl, RoomOutlineMessage outline) { }
            public bool TryTake(out RoomModel taken) { taken = room; return true; }
        }

        sealed class Station
        {
            public readonly HolonomyWalkerKernel Kernel = new HolonomyWalkerKernel();
            public readonly ParamRegistry Reg = new ParamRegistry();
            public readonly SpectatorHost Host;
            public readonly SpectatorCore Core;
            public readonly SpectatorLoop Loop;
            public Module Entered;

            public Station(RoomModel room)
            {
                CurvedWalkCore.DeclareParams(Reg);   // SpaceRegistry.DeclareAllParams at the Station's boot
                Host = new SpectatorHost(Reg, Kernel) { Create = id => id == SpaceId.CurvedWalk ? Entered = new Module(Kernel) : null };
                Core = new SpectatorCore(Host, Reg, Kernel, new OneRoom(room));
                Loop = new SpectatorLoop(Core, Kernel) { LinkUp = true };
                Loop.AddSense(new SpectatorSense(Core));
            }
        }

        // The State of a frame as the phone sends it: the common fields of FrameLoopCore.FillState for a space that walks the
        // kernel (the tile's six words, Local row-major, p[0] = metres per unit, p[1..2] from rho), then the space's own, then
        // the 202 bytes of the wire and back.
        static StateMessage Sent(Rig phone, in HeadPose head)
        {
            StateMessage s = StateMessage.Identity;
            s.SpaceId = (byte)SpaceId.CurvedWalk;
            s.SceneId = SpaceIds.SceneId(SpaceId.CurvedWalk);
            s.WorldVer = (ushort)(1 + phone.WorldBumps);
            s.TimeUs = (long)(head.Time * 1e6);
            s.HeadPosX = head.Position.x; s.HeadPosY = head.Position.y; s.HeadPosZ = head.Position.z;
            WalkerPose pose = phone.Kernel.Pose;
            TileId t = pose.Tile;
            s.SetTile(t.Xu, t.Xv, t.Yu, t.Yv, t.Tu, t.Tv);
            Span<double> m = stackalloc double[9];
            pose.Local.CopyTo(m);
            for (int i = 0; i < 9; i++) s.SetXform(i, m[i]);
            float rho = (float)phone.Kernel.CullRho;
            s.P0 = (float)phone.Kernel.MetresPerUnit;
            s.P1 = rho - 1f;
            s.P2 = rho;
            phone.S.WriteState(ref s);

            var bytes = new byte[StateMessage.Size];
            Assert.AreEqual(StateMessage.Size, s.WriteTo(bytes, 0));
            StateMessage received = default;
            Assert.IsTrue(received.ReadFrom(bytes, 0, bytes.Length));
            return received;
        }

        // The world event of the link driver (LinkPump.ComputeWorldHash): the kernel's q, the dial's target L, and the
        // generator's seed, version and content hash from the entered module.
        static string SendWorld(Station station, RoomModel room, Rig phone)
        {
            int q = phone.Kernel.Graph.Q, lMm = WorldHash.Millimetres(phone.Kernel.ConfiguredMetresPerUnit);
            string geometry = RoomModelJson.GeometryHash(room);
            string hash = WorldHash.Compute((int)SpaceId.CurvedWalk, q, CurvedWalkCore.WorldSeed, CurvedWalkCore.GeneratorVersion, geometry, lMm);
            JsonValue data = JsonValue.Object().Set("worldVer", 1 + phone.WorldBumps).Set("worldHash", hash)
                .Set("space", (int)SpaceId.CurvedWalk).Set("roomId", room.Id).Set("geomHash", geometry)
                .Set("q", q).Set("seed", (long)CurvedWalkCore.WorldSeed).Set("genVer", CurvedWalkCore.GeneratorVersion).Set("lMm", lMm);
            if (phone.S.ContentHash.Length > 0) data.Set("genHash", phone.S.ContentHash);
            station.Core.OnEvent(new EventMessage(1, "S2", LinkEvents.World, data));
            return hash;
        }

        static void AssertSame(in Iso2 a, in Iso2 b, string what)
        {
            Span<double> x = stackalloc double[9], y = stackalloc double[9];
            a.CopyTo(x); b.CopyTo(y);
            for (int i = 0; i < 9; i++) Assert.AreEqual(y[i], x[i], 1e-9, what + " [" + i + "]");
        }

        // One frame on both sides; the Station must stand where the phone stands and draw from the same tile zero.
        static void Frame(Rig phone, Station station, in HeadPose head, string what)
        {
            phone.Step(head);
            StateMessage s = Sent(phone, head);
            station.Core.OnState(in s);
            station.Loop.Run(head.Time, (float)Dt);
            station.Loop.RunAfterFrame();

            WalkerPose a = station.Kernel.Pose, b = phone.Kernel.Pose;
            Assert.AreEqual(b.Tile, a.Tile, what + ": tile");
            AssertSame(a.Local, b.Local, what + ": U");
            Assert.AreEqual(b.Local.Kappa, a.Local.Kappa, what + ": kappa");
            Assert.AreEqual(phone.Kernel.Q, station.Kernel.Q, what + ": q");
            CurvedWalkCore mine = station.Entered.Core, theirs = phone.S;
            Assert.IsTrue(mine.Remote, what);
            Assert.AreEqual(theirs.SelectedQ, mine.SelectedQ, what + ": dial");
            Assert.AreEqual(theirs.TileZeroFound, mine.TileZeroFound, what + ": tile zero found");
            if (theirs.TileZeroFound) AssertSame(mine.TileZeroInCurrent, theirs.TileZeroInCurrent, what + ": F of tile zero");
            Assert.AreEqual(SpectatorBanner.None, station.Core.Banner, what);
        }

        // "State bytes written by the phone side for a walked lap in S2 decode on the Spectator side to the same tile and pose":
        // the recorded 5-corner lap of the 3 x 3 m room, with the ramp before it.
        [Test]
        public void Station_FollowsTheRecordedLap_q5()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            List<HeadPose> log = Concat(Prefix(new double2(-1, -1)), Load("walk_lap5_3x3.poselog"));
            var station = new Station(room);
            using (var phone = new Rig(room, 5))
            {
                string hash = SendWorld(station, room, phone);
                var tiles = new List<TileId>();
                bool ramp = false;
                for (int i = 0; i < log.Count; i++)
                {
                    Frame(phone, station, log[i], "frame " + i);
                    TileId tile = phone.Kernel.Pose.Tile;
                    if (tiles.Count == 0 || tiles[tiles.Count - 1] != tile) tiles.Add(tile);
                    ramp |= phone.Kernel.Ramping && phone.Kernel.CurvatureNow != 0;
                    Assert.AreEqual(phone.Kernel.MetresPerUnit, station.Kernel.MetresPerUnit, 1e-6 * phone.Kernel.MetresPerUnit, "L, frame " + i);
                }
                Assert.IsTrue(ramp, "the Station followed the ramp");
                Assert.IsTrue(phone.S.Walk.Closed, "the phone closed its lap");
                Assert.Greater(tiles.Count, 1);

                // The same tile sequence on the Station's map.
                FlatTwinTrail trail = station.Core.Trail;
                Assert.AreEqual(tiles.Count, trail.TilesSeen);
                for (int k = 0; k < trail.TileCount; k++) Assert.AreEqual(tiles[tiles.Count - trail.TileCount + k], trail.GetTile(k));

                // The same generated world, by its own generator: the hash agrees and the Station made what the phone made.
                Module module = station.Entered;
                Assert.IsTrue(station.Core.WorldVerified);
                Assert.AreEqual(phone.S.ContentHash, module.Core.ContentHash);
                Assert.AreEqual(16, module.Core.ContentHash.Length);
                Assert.AreEqual(hash, WorldHash.Compute((int)SpaceId.CurvedWalk, station.Kernel.Graph.Q, module.WorldSeed, module.GeneratorVersion,
                    RoomModelJson.GeometryHash(station.Core.Room), WorldHash.Millimetres(module.Core.LengthScale(station.Kernel.Q))));

                // Nothing was driven here: no step, no request, no world change of its own, one module for the whole walk.
                Assert.AreEqual(0, station.Kernel.StepCount);
                Assert.AreEqual(0, module.Requests.Count);
                Assert.AreEqual(0, module.WorldBumps);
                Assert.AreEqual(1, station.Host.EnterCount);
                Assert.AreEqual(log.Count, module.Ticks);
                Assert.IsFalse(module.Core.Walk.Closed, "the Station cannot tell standing from walking: its lap does not advance");
            }
        }

        // The dial moves to 6 on the phone: the ramp runs down, the tiling is swapped at K = 0 and the ramp runs up again.
        // The Station follows from the States and the world event, and drops the generated world that {4,6} does not have.
        [Test]
        public void Station_FollowsTheDialToSix()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            var station = new Station(room);
            using (var phone = new Rig(room, 5))
            {
                SendWorld(station, room, phone);
                List<HeadPose> still = Still(double2.zero, 1500);
                int bumps = 0, swappedAt = -1;
                for (int i = 0; i < still.Count; i++)
                {
                    if (i == 400) phone.Q.Set(6);
                    HeadPose head = still[i];
                    phone.Step(head);
                    if (phone.WorldBumps != bumps)
                    {
                        // LinkPump sees the world version move in the State it is about to send.
                        bumps = phone.WorldBumps;
                        swappedAt = i;
                        SendWorld(station, room, phone);
                    }
                    StateMessage s = Sent(phone, head);
                    station.Core.OnState(in s);
                    station.Loop.Run(head.Time, (float)Dt);
                    station.Loop.RunAfterFrame();
                    Assert.AreEqual(phone.Kernel.Q, station.Kernel.Q, "q, frame " + i);
                    Assert.AreEqual(phone.Kernel.Pose.Tile, station.Kernel.Pose.Tile, "tile, frame " + i);
                    Assert.AreEqual(phone.Kernel.Kappa, station.Kernel.Kappa, "kappa, frame " + i);
                    Assert.AreEqual(phone.S.SelectedQ, station.Entered.Core.SelectedQ, "dial, frame " + i);
                    Assert.AreEqual(SpectatorBanner.None, station.Core.Banner, "frame " + i);
                }
                Assert.Greater(swappedAt, 400, "the phone swapped the tiling");
                Assert.AreEqual(6, phone.Kernel.Q);
                Assert.AreEqual(Curvature.Hyperbolic, station.Kernel.Kappa, "and ramped up again");
                Assert.IsNull(station.Entered.Core.World);
                Assert.AreEqual("", station.Entered.Core.ContentHash);
                Assert.AreEqual(1, station.Host.EnterCount, "the same module throughout");
                Assert.IsTrue(station.Core.WorldVerified);
            }
        }
    }
}
