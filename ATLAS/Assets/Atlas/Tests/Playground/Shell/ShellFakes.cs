using System;
using System.Collections.Generic;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>A space host that does what the tests tell it: which spaces exist, what a switch does.</summary>
    sealed class FakeHost : ISpaceHost
    {
        public readonly HashSet<SpaceId> Available = new HashSet<SpaceId>
        {
            SpaceId.Home, SpaceId.Glued, SpaceId.CurvedWalk, SpaceId.AlignedCells, SpaceId.SmallPlanet, SpaceId.Portal, SpaceId.Slicer
        };
        public readonly List<SpaceId> Switches = new List<SpaceId>();
        public int ReturnHomeCalls;
        public SpaceHostState StateNow = SpaceHostState.Running;
        public SpaceId CurrentNow = SpaceId.CurvedWalk;
        public RoomModel RoomNow;

        public SpaceHostState State { get { return StateNow; } }
        public bool HasSpace { get { return true; } }
        public SpaceId Current { get { return CurrentNow; } }
        public ISpaceModule Module { get { return null; } }
        public RoomModel Room { get { return RoomNow; } }
        public bool Standing { get { return false; } }
        public ushort WorldVersion { get { return 0; } }
        public bool IsAvailable(SpaceId id) { return Available.Contains(id); }

        public bool RequestSwitch(SpaceId target)
        {
            if (!Available.Contains(target)) return false;
            Switches.Add(target);
            return true;
        }

        public bool ReturnHomeResult = true;

        public bool ReturnHome()
        {
            ReturnHomeCalls++;
            return ReturnHomeResult;
        }

        public void SetRoom(RoomModel room) { RoomNow = room; }
        public void BumpWorldVersion() { }

        public event Action<SpaceId> SpaceEntered;
        public event Action<SpaceId> SpaceExited;
        public event Action<RoomModel> RoomChanged { add { } remove { } }

        public void Enter(SpaceId id)
        {
            CurrentNow = id;
            SpaceEntered?.Invoke(id);
        }

        public void Exit(SpaceId id)
        {
            SpaceExited?.Invoke(id);
        }
    }

    /// <summary>The input service around the real engine-free core, so a dwell is the real dwell.</summary>
    sealed class CoreInput : IAtlasInput
    {
        public readonly AtlasInputCore Core = new AtlasInputCore();
        public RawInput Raw;
        public Vector3 Head = new Vector3(0f, 1.6f, 0f);
        public Quaternion Rotation = Quaternion.identity;

        public CoreInput()
        {
            Core.PushContext(InputContext.Space);
        }

        public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
        {
            Core.Tick(in Raw, new float3(headPosition.x, headPosition.y, headPosition.z),
                      new quaternion(headRotation.x, headRotation.y, headRotation.z, headRotation.w), deltaTime);
        }

        /// <summary>Turns the head to look along a direction (managed maths: the engine's Quaternion calls need the player).</summary>
        public void Aim(Vector3 direction)
        {
            quaternion q = quaternion.LookRotationSafe(new float3(direction.x, direction.y, direction.z), new float3(0f, 1f, 0f));
            Rotation = new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        }

        /// <summary>Turns the head to a yaw in degrees (0 looks along +Z).</summary>
        public void AimYaw(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            Aim(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
        }

        /// <summary>One frame with the stored head pose.</summary>
        public void Frame(float dt)
        {
            Tick(Head, Rotation, dt);
        }

        public InputContext Context { get { return Core.Context; } }
        public void PushContext(InputContext context) { Core.PushContext(context); }
        public void PopContext(InputContext context) { Core.PopContext(context); }
        public bool Pressed(AtlasAction action) { return Core.Pressed(action); }
        public bool Held(AtlasAction action) { return Core.Held(action); }
        public float Axis(AtlasAxis axis) { return Core.Axis(axis); }
        public InputSource LastSource(AtlasAction action) { return Core.LastSource(action); }
        public int LastTag(AtlasAction action) { return Core.LastTag(action); }

        public event Action<AtlasAction, InputSource, int> ActionFired
        {
            add { Core.ActionFired += value; }
            remove { Core.ActionFired -= value; }
        }

        public float DwellSeconds { get { return Core.DwellSeconds; } set { Core.DwellSeconds = value; } }

        public GazeTargetId RegisterGazeTarget(in GazeTarget target)
        {
            GazeTargetData data = target.ToData();
            return Core.Targets.Add(in data);
        }

        public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { Core.Targets.Move(id, new float3(centre.x, centre.y, centre.z)); }
        public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { Core.Targets.SetEnabled(id, enabled); }
        public void UnregisterGazeTarget(GazeTargetId id) { Core.Targets.Remove(id); }

        public GazeState Gaze
        {
            get
            {
                float3 p = Core.GazePoint;
                return new GazeState { Focused = Core.GazeFocused, Progress = Core.GazeProgress, Point = new Vector3(p.x, p.y, p.z) };
            }
        }

        public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { Core.CopyActionsWithoutGazeTwin(into); }
        public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default; return false; }
        public bool GamepadPresent { get { return Core.GamepadPresent; } }
        public void SetRumble(float lowFrequency, float highFrequency) { }
        public WalkerInput Walker { get { return default; } }
    }

    static class Rooms
    {
        /// <summary>A 4 x 5 m empty room: walls on the outline, free rectangle 3.4 x 4.4 m (0.3 m inside the walls), s = 3.0.</summary>
        public static RoomModel Empty(float width = 4f, float depth = 5f)
        {
            var room = new RoomModel();
            room.FreeRect = new FreeRect(width - 0.6f, depth - 0.6f);
            room.Outline = new[]
            {
                new float2(-width / 2, -depth / 2), new float2(width / 2, -depth / 2),
                new float2(width / 2, depth / 2), new float2(-width / 2, depth / 2)
            };
            room.Walls = new RoomWall[4];
            for (int i = 0; i < 4; i++)
                room.Walls[i] = new RoomWall { A = room.Outline[i], B = room.Outline[(i + 1) % 4], Height = 2.4f };
            room.ApplyLoopFit();
            return room;
        }
    }

    sealed class FakeWalk : IWalkReadout
    {
        public bool Armed { get; set; }
        public int Q { get; set; } = 5;
        public int Corners { get; set; }
        public bool Closed { get; set; }
        public double DistanceToFlagM { get; set; } = double.NaN;
        public double WorldTurnDeg { get; set; }
        public double SideM { get; set; } = 2.4;
        public double LengthScaleM { get; set; } = 2.4 / 1.0613;
    }

    sealed class FakeCells : ICellsReadout
    {
        public double CellSideM { get; set; } = 1.1;
        public int Crossings { get; set; }
    }
}
