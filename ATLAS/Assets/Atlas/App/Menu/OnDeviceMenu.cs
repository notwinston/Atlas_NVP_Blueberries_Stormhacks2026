using System;
using Atlas.Input;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Menu
{
    /// <summary>
    /// The on-device menu (Master Plan section 12.4, task P1-D13; lane D contract, section 5.7): spaces,
    /// quality and kill switches, every parameter and the session, in the headset, with no Station. This class
    /// is the thin adapter: it reads the input service, feeds the <see cref="MenuModel"/>, places the panel and
    /// its gaze targets, and asks the view to redraw when the model changed. Every row works from the gamepad
    /// alone and from gaze dwell alone (Master Plan section 0.5, rule 5).
    /// <para>
    /// Cost: closed, one action read at step 8b and one target and one transform moved at step 6. Open, a
    /// handful of action reads, a dozen targets moved, and text rebuilt only when something changed.
    /// </para>
    /// </summary>
    public sealed class OnDeviceMenu : IAppMenu, IAfterFrameStep, IUploadStep, IMenuActions
    {
        /// <summary>The status lines are read this often while the menu is open.</summary>
        public const float StatusSeconds = 0.5f;

        readonly GazeTargetId[] _targets = new GazeTargetId[MenuLayout.TargetCount];
        readonly float3[] _offsets = new float3[MenuLayout.TargetCount];

        AtlasApp _app;
        IAtlasInput _input;
        IStereoRig _rig;
        MenuModel _model;
        MenuView _view;
        MenuFrame _frame;
        GazeTargetId _chip;
        float3 _panelOffset;
        bool _open, _frameSet, _shutDown;
        int _shownRevision = -1, _shownSlot = -1;
        float _sinceStatus;
        string _version;

        public bool IsOpen { get { return _open; } }

        /// <summary>The rows, the focus and the values. Null before <see cref="Install"/>.</summary>
        public MenuModel Model { get { return _model; } }

        /// <summary>
        /// Raised by "Rescan the room" after the menu has closed. Whoever owns the scan flow subscribes; with no
        /// subscriber the app's rehearsal flow starts the scan.
        /// </summary>
        public event Action RescanRequested;

        // ---- IAppMenu --------------------------------------------------------------------------------------

        public void Install(AtlasApp app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            _app = app;
            _input = app.InputService;
            _rig = app.Rig;
            _version = Application.version;

            IEventLog events;
            AtlasServices.TryGet(out events);
            _model = new MenuModel(app.Registry, app.Spaces, this) { Events = events };

            if (app.Boot != null && app.Boot.Hud != null && app.Pool != null) _view = MenuView.Create(app.Boot.Hud, app.Pool);

            if (_input != null)
            {
                // The gaze twin of the menu action: a chip down and to the left, live while a space runs.
                GazeTarget chip = GazeTarget.World(AtlasAction.ParamMenu, Vector3.zero, MenuLayout.OpenChipRadius);
                chip.Label = "menu chip";
                _chip = _input.RegisterGazeTarget(in chip);
            }

            if (app.Loop != null)
            {
                app.Loop.AddUpload(this);
                app.Loop.AddAfterFrame(this);
            }
            if (app.Rig != null) app.Rig.ModeChanged += OnModeChanged;
        }

        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            if (_app != null && _app.Rig != null) _app.Rig.ModeChanged -= OnModeChanged;
            if (_input != null)
            {
                if (_open)
                {
                    UnregisterTargets();
                    _input.PopContext(InputContext.Menu);
                }
                if (_chip.IsValid) _input.UnregisterGazeTarget(_chip);
            }
            _open = false;
            _chip = default(GazeTargetId);
            if (_view != null) _view.Dispose();
            _view = null;
        }

        // ---- opening and closing ---------------------------------------------------------------------------

        /// <summary>Shows the panel in front of the walker and takes the input context. The world keeps running.</summary>
        public void Open()
        {
            if (_open || _shutDown || _model == null || _input == null) return;
            _open = true;
            _input.PushContext(InputContext.Menu);
            _model.NoteOpened();
            RefreshStatus();

            // The panel comes up where the walker is looking, level.
            float3 head = float3.zero;
            if (_app.Loop != null)
            {
                ref readonly FrameContext frame = ref _app.Loop.Current;
                head = frame.RawHeadPosition;
                float yaw, pitch;
                if (MenuFrame.TryYawPitch(frame.RawHeadRotation, out yaw, out pitch)) _frame.Snap(yaw);
            }
            _frameSet = true;
            RegisterTargets(head);
            if (_view != null)
            {
                _view.PlacePanel(head + _panelOffset, _frame.Rotation);
                _view.SetOpen(true);
            }
            Present();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            UnregisterTargets();
            _input.PopContext(InputContext.Menu);
            _model.NoteClosed();
            if (_view != null) _view.SetOpen(false);
        }

        // The head-locked layer is not drawn in the hand: an open menu would hold the input context unseen.
        void OnModeChanged(DisplayMode mode)
        {
            if (mode != DisplayMode.Headset) Close();
        }

        // ---- step 8b: input ----------------------------------------------------------------------------------

        public void AfterFrame(in FrameContext frame)
        {
            if (_input == null || _model == null) return;
            if (!_open)
            {
                if (_input.Pressed(AtlasAction.ParamMenu)) Open();
                return;
            }

            if (_input.Pressed(AtlasAction.ParamMenu) || _input.Pressed(AtlasAction.Back))
            {
                Close();
                return;
            }
            if (_input.Pressed(AtlasAction.NavUp)) _model.Move(-1);
            if (_input.Pressed(AtlasAction.NavDown))
            {
                // A dwell on a row carries the row's slot; the d-pad and the stick carry nothing.
                int tag = _input.LastTag(AtlasAction.NavDown);
                if (tag > 0) _model.Select(_model.Top + tag - 1);
                else _model.Move(1);
            }
            if (_input.Pressed(AtlasAction.NavLeft)) _model.Step(-1);
            if (_input.Pressed(AtlasAction.NavRight)) _model.Step(1);
            if (_input.Pressed(AtlasAction.Confirm)) _model.Confirm();

            // A row may have closed the menu itself (rescan), or asked for it (a space, Close).
            if (!_open) return;
            if (_model.CloseRequested)
            {
                Close();
                return;
            }

            _model.Sync();
            _sinceStatus += frame.DeltaTime;
            if (_sinceStatus >= StatusSeconds) RefreshStatus();
            if (_model.Revision != _shownRevision) Present();
        }

        void Present()
        {
            _shownRevision = _model.Revision;
            int slot = _model.Selected - _model.Top;
            if (slot != _shownSlot)
            {
                _shownSlot = slot;
                quaternion rotation = _frame.Rotation;
                _offsets[MenuLayout.Minus] = Offset(rotation, MenuLayout.Minus, slot);
                _offsets[MenuLayout.Plus] = Offset(rotation, MenuLayout.Plus, slot);
            }
            // Only a row that can take the focus is a target; the chips stay live, so every action read here
            // keeps its gaze twin.
            for (int i = 0; i < MenuModel.VisibleRows; i++)
            {
                int row = _model.Top + i;
                _input.SetGazeTargetEnabled(_targets[i], row < _model.RowCount && _model.Row(row).Focusable);
            }
            if (_view != null) _view.Show(_model);
        }

        void RefreshStatus()
        {
            _sinceStatus = 0f;
            var status = new MenuStatus { Version = _version };
            if (_app.Ladder != null)
            {
                status.Tier = (int)_app.Ladder.Tier;
                status.Thermal = _app.Ladder.ThermalState;
            }
            AtlasTelemetry telemetry = _app.TelemetrySystems;
            if (telemetry != null)
            {
                status.Link = LinkPump.StateName(telemetry.Pump.State);
                status.RttMs = telemetry.Link.RttMs;
                status.StationHost = telemetry.Pairing.Host;
                status.StationPort = telemetry.Pairing.Port;
                status.Session = telemetry.SessionId;
            }
            RoomModel room = _app.Spaces != null ? _app.Spaces.Room : null;
            status.Room = room != null ? room.Id : null;
            _model.SetStatus(in status);
        }

        // ---- step 6: where the menu stands -------------------------------------------------------------------

        public void Upload(in FrameContext frame)
        {
            if (_input == null || _shutDown) return;
            if (_rig != null && _rig.Mode != DisplayMode.Headset) return;

            float yaw, pitch;
            bool level = MenuFrame.TryYawPitch(frame.RawHeadRotation, out yaw, out pitch);
            float3 head = frame.RawHeadPosition;

            if (!_open)
            {
                if (level)
                {
                    if (!_frameSet)
                    {
                        _frame.Snap(yaw);
                        _frameSet = true;
                    }
                    else
                    {
                        // The chip holds still under the gaze, or it could never be dwelt on.
                        bool onChip = _chip.IsValid && _input.Gaze.Focused.Equals(_chip);
                        _frame.Follow(yaw, pitch, frame.DeltaTime, onChip);
                    }
                }
                quaternion rotation = _frame.Rotation;
                float3 centre = head + math.mul(rotation, MenuLayout.OpenChip);
                if (_chip.IsValid) _input.MoveGazeTarget(_chip, centre);
                if (_view != null) _view.PlaceChip(centre, rotation);
                return;
            }

            if (level && _frame.Recentre(yaw))
            {
                // Rare: the walker turned their back on the panel. The targets are rectangles with a heading,
                // so they are made again rather than moved.
                UnregisterTargets();
                RegisterTargets(head);
                Present();
            }
            else
            {
                for (int i = 0; i < _targets.Length; i++) _input.MoveGazeTarget(_targets[i], head + _offsets[i]);
            }
            if (_view != null) _view.PlacePanel(head + _panelOffset, _frame.Rotation);
        }

        static float3 Offset(quaternion rotation, int target, int slot)
        {
            return math.mul(rotation, MenuLayout.PanelCentre + MenuLayout.TargetCentre(target, slot));
        }

        // Rows select (the gaze twin of up and down), "-" and "+" step and repeat, the two chips confirm and
        // close. All in the room frame, following the head's position; live while the menu holds the context.
        void RegisterTargets(float3 head)
        {
            quaternion rotation = _frame.Rotation;
            _panelOffset = math.mul(rotation, MenuLayout.PanelCentre);
            int slot = _model.Selected - _model.Top;
            _shownSlot = slot;
            for (int i = 0; i < _targets.Length; i++)
            {
                _offsets[i] = Offset(rotation, i, slot);
                float2 half = MenuLayout.TargetHalfSize(i);
                AtlasAction action;
                int tag = 0;
                string label;
                if (i < MenuLayout.Minus) { action = AtlasAction.NavDown; tag = i + 1; label = "menu row"; }
                else if (i == MenuLayout.Minus) { action = AtlasAction.NavLeft; label = "menu minus"; }
                else if (i == MenuLayout.Plus) { action = AtlasAction.NavRight; label = "menu plus"; }
                else if (i == MenuLayout.Apply) { action = AtlasAction.Confirm; label = "menu apply"; }
                else { action = AtlasAction.Back; label = "menu close"; }

                GazeTarget target = GazeTarget.WorldRect(action, head + _offsets[i], rotation, new Vector2(half.x, half.y), tag);
                target.Context = InputContext.Menu;
                target.Label = label;
                if (i == MenuLayout.Minus || i == MenuLayout.Plus) target.Mode = GazeMode.Repeat;
                _targets[i] = _input.RegisterGazeTarget(in target);
            }
        }

        void UnregisterTargets()
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i].IsValid) _input.UnregisterGazeTarget(_targets[i]);
                _targets[i] = default(GazeTargetId);
            }
        }

        // ---- IMenuActions ------------------------------------------------------------------------------------

        public void Recentre()
        {
            if (_app != null && _app.Commands != null) _app.Commands.Recentre();
        }

        public void Rescan()
        {
            // The scan is done with the phone in the hand: the menu lets go of the input first.
            Close();
            Action requested = RescanRequested;
            if (requested != null) requested();
            else if (_app != null && _app.Flow != null) _app.Flow.BeginScan();
        }
    }
}
