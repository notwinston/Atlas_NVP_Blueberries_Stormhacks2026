using System;
using Atlas.Input;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Playground
{
    /// <summary>The rows of the exhibit menu (Master Plan beat 9): the three exhibits that start from Home, and the
    /// way out of the menu to the end of the run.</summary>
    public enum ExhibitId : byte
    {
        Glued = 0,
        ImpossibleRooms = 1,
        Slicer = 2,
        /// <summary>The network exhibit, shown in Home through <see cref="IHomeExhibit"/> (contract network.md NET-08).</summary>
        Network = 3,
        /// <summary>"Exhibit chosen, or done": done ends beat 9 and the director goes on to the return Home.</summary>
        Done = 4
    }

    /// <summary>
    /// A slot for an exhibit that lane A builds (P2-A01, the Slicer toybox). When one is set, the menu uses it to
    /// decide whether the row is ready and to start it; with none the row starts the plain Slicer space (S6) when
    /// SpaceLab has it.
    /// </summary>
    public interface IExhibitSlot
    {
        bool Ready { get; }
        /// <summary>Starts the exhibit. False when it could not.</summary>
        bool Launch();
    }

    /// <summary>Where the signs of the menu stand in Home. A pure function of the RoomModel, so it runs without the
    /// engine and the test checks that every sign is inside the free rectangle.</summary>
    public static class ExhibitSignLayout
    {
        /// <summary>Signs per menu: four exhibits and Done.</summary>
        public const int Count = 5;
        /// <summary>Height of a sign's centre above the floor, metres.</summary>
        public const float Height = 1.5f;
        public const float HalfWidth = 0.22f, HalfHeight = 0.07f;
        /// <summary>Angular step between neighbouring signs on the arc, degrees. The arc is centred on +Z.</summary>
        public const float StepDegrees = 28f;

        /// <summary>Radius of the arc: 0.8 of the free rectangle's half short side, within 0.5 to 1.5 m, so a sign
        /// stands inside the rectangle the walker can reach.</summary>
        public static float Radius(RoomModel room)
        {
            float half = 0.5f * math.min(room.FreeRect.Wf, room.FreeRect.Df);
            return math.clamp(0.8f * half, 0.5f, 1.5f);
        }

        /// <summary>Centre of sign k (0 to 4) in the room frame; the arc is round the centre of the free rectangle.</summary>
        public static float3 Centre(RoomModel room, int k)
        {
            float angle = math.radians((k - 0.5f * (Count - 1)) * StepDegrees);
            float r = Radius(room);
            return new float3(r * math.sin(angle), Height, r * math.cos(angle));
        }

        /// <summary>Faces the centre of the arc: the sign's front (-Z of its own frame) looks at the walker.</summary>
        public static quaternion Rotation(RoomModel room, int k)
        {
            float3 c = Centre(room, k);
            float3 toCentre = new float3(-c.x, 0f, -c.z);
            // A rect target lies in the XY plane of its rotation, facing -Z: the rotation that turns +Z to the walker's back.
            return quaternion.LookRotationSafe(-toCentre, new float3(0f, 1f, 0f));
        }

        /// <summary>The single board that opens the menu while it is closed, at the middle of the arc.</summary>
        public static float3 BoardCentre(RoomModel room)
        {
            float r = Radius(room);
            return new float3(0f, Height, r);
        }
    }

    /// <summary>
    /// The exhibit menu and the way back Home (Master Plan section 10.1, beat 9, task P2-C08). Four rows, three
    /// exhibits and Done, shown as signs in Home. Open it with Start, the long press or a dwell on the board; step
    /// with the stick or a click; pick with A, a long press or a dwell on a sign (the twins table). A pick of an
    /// exhibit asks SpaceLab for its space (or starts the slot lane A put in); Done only closes the menu.
    /// <para>The menu holds the input context Menu while it is open, so the d-pad and the stick navigate it
    /// and not the dial. Engine-free but for the types of the input service. Main thread only.</para>
    /// </summary>
    public sealed class ExhibitMenu : IDisposable
    {
        static readonly string[] s_labels = { "Glued", "Impossible Rooms", "Slicer", "Network", "Done" };

        readonly ISpaceHost _host;
        readonly IAtlasInput _input;
        readonly ActionMap _map;
        readonly GazeTargetId[] _signs = new GazeTargetId[ExhibitSignLayout.Count];
        IExhibitSlot _slicerSlot;
        bool _open;
        int _selected;
        bool _pushed;
        bool _attached;

        /// <summary>input and map may be null (a bare test rig); then nothing is registered and the lever is not told.</summary>
        public ExhibitMenu(ISpaceHost host, IAtlasInput input, ActionMap map)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _input = input;
            _map = map;
            _host.SpaceEntered += OnSpaceEntered;
            _attached = true;
        }

        public bool IsOpen { get { return _open; } }
        /// <summary>The highlighted row, 0 to 4.</summary>
        public int Selected { get { return _selected; } }
        /// <summary>Moves on every change of the open state or the selection.</summary>
        public int Revision { get; private set; }
        public static int RowCount { get { return ExhibitSignLayout.Count; } }

        /// <summary>The slot of lane A's Slicer toybox. Null removes it.</summary>
        public IExhibitSlot SlicerSlot
        {
            get { return _slicerSlot; }
            set { _slicerSlot = value; Revision++; }
        }

        /// <summary>Raised when an exhibit was chosen and its start was accepted.</summary>
        public event Action<ExhibitId> Chosen;
        /// <summary>Raised when Done was picked.</summary>
        public event Action DonePicked;

        public static string Label(int row)
        {
            return row >= 0 && row < s_labels.Length ? s_labels[row] : "";
        }

        public static string Label(ExhibitId id)
        {
            return Label((int)id);
        }

        /// <summary>The space an exhibit starts, or Home for Done.</summary>
        public static SpaceId SpaceOf(ExhibitId id)
        {
            switch (id)
            {
                case ExhibitId.Glued: return SpaceId.Glued;
                case ExhibitId.ImpossibleRooms: return SpaceId.Portal;
                case ExhibitId.Slicer: return SpaceId.Slicer;
                // Network is no space: it is shown in Home through IHomeExhibit.
                default: return SpaceId.Home;
            }
        }

        /// <summary>True when the row can be picked now: Done always; the Slicer by its slot; the others by SpaceLab.</summary>
        public bool IsReady(ExhibitId id)
        {
            if (id == ExhibitId.Done) return true;
            if (id == ExhibitId.Slicer && _slicerSlot != null) return _slicerSlot.Ready;
            if (id == ExhibitId.Network) return AtlasServices.TryGet(out IHomeExhibit net) && net.Ready;
            return _host.IsAvailable(SpaceOf(id));
        }

        /// <summary>Opens the menu with the selection on the first ready row. False when it is open already.</summary>
        public bool Open()
        {
            if (_open) return false;
            _open = true;
            _selected = FirstReady();
            if (_map != null) _map.MenuOpen = true;
            if (_input != null && !_pushed)
            {
                _input.PushContext(InputContext.Menu);
                _pushed = true;
            }
            EnableSigns(true);
            Revision++;
            return true;
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (_map != null) _map.MenuOpen = false;
            if (_input != null && _pushed)
            {
                _input.PopContext(InputContext.Menu);
                _pushed = false;
            }
            EnableSigns(false);
            Revision++;
        }

        /// <summary>Moves the selection by one row, skipping rows that are not ready; wraps at the ends.</summary>
        public void Step(int direction)
        {
            if (!_open) return;
            int step = direction < 0 ? -1 : 1;
            for (int i = 1; i <= ExhibitSignLayout.Count; i++)
            {
                int row = ((_selected + i * step) % ExhibitSignLayout.Count + ExhibitSignLayout.Count) % ExhibitSignLayout.Count;
                if (!IsReady((ExhibitId)row)) continue;
                _selected = row;
                Revision++;
                return;
            }
        }

        /// <summary>Picks a row (-1 = the selected one). False when the row is not ready or the menu is closed.</summary>
        public bool Pick(int row = -1)
        {
            if (!_open) return false;
            if (row < 0) row = _selected;
            if (row >= ExhibitSignLayout.Count) return false;
            var id = (ExhibitId)row;
            if (!IsReady(id)) return false;

            if (id == ExhibitId.Network)
            {
                // The exhibit stays in Home: a second pick hides it again.
                if (!AtlasServices.TryGet(out IHomeExhibit net)) return false;
                if (net.Active) net.End();
                else if (!net.Begin()) return false;
                Close();
                Action<ExhibitId> shown = Chosen;
                if (shown != null && net.Active) shown(id);
                return true;
            }

            if (id == ExhibitId.Done)
            {
                EndHomeExhibit();
                Close();
                Action done = DonePicked;
                if (done != null) done();
                return true;
            }

            bool started = id == ExhibitId.Slicer && _slicerSlot != null ? _slicerSlot.Launch() : _host.RequestSwitch(SpaceOf(id));
            if (!started) return false;
            Close();
            Action<ExhibitId> chosen = Chosen;
            if (chosen != null) chosen(id);
            return true;
        }

        /// <summary>The twins of the table: open, step, pick. Subscribe this to <see cref="ActionMap.Fired"/>.</summary>
        public void OnAction(PgActionEvent e)
        {
            switch (e.Action)
            {
                case PgAction.ExhibitOpen:
                    // Start again closes it: a pick is A, a long press or a dwell on a sign.
                    if (_open) Close(); else Open();
                    break;
                case PgAction.ExhibitStep: Step(e.Value); break;
                case PgAction.ExhibitPick: Pick(e.Value); break;
            }
        }

        /// <summary>
        /// Registers the gaze targets of the four signs (live in Menu only) and of the board that opens the menu.
        /// Call once the room is known, and again if the room changes. Slot ExhibitSign is the board.
        /// </summary>
        public void PlaceSigns(RoomModel room, PgGazeTargets board)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (_input == null) return;
            for (int k = 0; k < ExhibitSignLayout.Count; k++)
            {
                if (_signs[k].IsValid) _input.UnregisterGazeTarget(_signs[k]);
                float3 c = ExhibitSignLayout.Centre(room, k);
                quaternion r = ExhibitSignLayout.Rotation(room, k);
                GazeTarget t = GazeTarget.WorldRect(AtlasAction.Confirm, new Vector3(c.x, c.y, c.z),
                    new Quaternion(r.value.x, r.value.y, r.value.z, r.value.w),
                    new Vector2(ExhibitSignLayout.HalfWidth, ExhibitSignLayout.HalfHeight), k);
                t.Context = InputContext.Menu;
                t.Label = "exhibit sign " + s_labels[k];
                _signs[k] = _input.RegisterGazeTarget(in t);
                _input.SetGazeTargetEnabled(_signs[k], _open);
            }
            if (board != null)
            {
                float3 c = ExhibitSignLayout.BoardCentre(room);
                quaternion r = quaternion.LookRotationSafe(new float3(0f, 0f, 1f), new float3(0f, 1f, 0f));
                board.Bind(PgSlot.ExhibitSign, GazeTarget.WorldRect(AtlasAction.SpaceMenu, new Vector3(c.x, c.y, c.z),
                    new Quaternion(r.value.x, r.value.y, r.value.z, r.value.w),
                    new Vector2(ExhibitSignLayout.HalfWidth, ExhibitSignLayout.HalfHeight)));
            }
        }

        public void Dispose()
        {
            Close();
            if (_attached)
            {
                _host.SpaceEntered -= OnSpaceEntered;
                _attached = false;
            }
            if (_input == null) return;
            for (int k = 0; k < _signs.Length; k++)
            {
                if (_signs[k].IsValid) _input.UnregisterGazeTarget(_signs[k]);
                _signs[k] = default;
            }
        }

        // A space was entered (a pick, or an operator command): the menu has done its job.
        void OnSpaceEntered(SpaceId id)
        {
            EndHomeExhibit();
            Close();
        }

        static void EndHomeExhibit()
        {
            if (AtlasServices.TryGet(out IHomeExhibit net) && net.Active) net.End();
        }

        int FirstReady()
        {
            for (int row = 0; row < ExhibitSignLayout.Count; row++)
                if (IsReady((ExhibitId)row)) return row;
            return (int)ExhibitId.Done;
        }

        void EnableSigns(bool on)
        {
            if (_input == null) return;
            for (int k = 0; k < _signs.Length; k++)
                if (_signs[k].IsValid) _input.SetGazeTargetEnabled(_signs[k], on);
        }
    }
}
