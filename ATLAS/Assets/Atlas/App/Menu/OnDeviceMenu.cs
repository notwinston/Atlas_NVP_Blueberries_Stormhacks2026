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
    /// In a hands session the menu comes within reach (documents/ATLAS_Hands_Design.md section 6.1, decision
    /// H14): it places itself as it always has, then asks <see cref="ReachSlots.TryGetNear"/>, and on true the
    /// panel and the chip stand at their slots in the near anchor's frame, with the layout multiplied by the
    /// scale, on the world layer, and their gaze targets with them. With no layout, or one that says far,
    /// nothing here differs from a pad session by one number.
    /// </para>
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
        IFrameLoop _loop;
        MenuModel _model;
        IMenuView _view;
        MenuFrame _frame;
        GazeTargetId _chip;
        float3 _panelOffset, _chipCentre;
        // Within reach of the hands: the scale in force (1 when far), and the rotation the open panel's targets
        // were made with, which is the heading frame's when far and the near anchor's within reach.
        quaternion _rotation = quaternion.identity;
        float _scale = 1f;
        bool _near;
        // Turns a rectangle target in place. The real input service can and its interface cannot; with none, a
        // panel whose anchor turned has its targets made again.
        Action<GazeTargetId, Quaternion> _turn;
        bool _open, _frameSet, _shutDown;
        int _shownRevision = -1, _shownSlot = -1;
        float _sinceStatus;
        string _version;

        public bool IsOpen { get { return _open; } }

        /// <summary>The rows, the focus and the values. Null before <see cref="Install"/>.</summary>
        public MenuModel Model { get { return _model; } }

        /// <summary>The target of the chip that opens the menu, for tests and tools.</summary>
        internal GazeTargetId ChipTarget { get { return _chip; } }

        /// <summary>A target of the open panel (the numbering of <see cref="MenuLayout"/>), for tests and tools. Default while closed.</summary>
        internal GazeTargetId TargetOf(int target) { return _targets[target]; }

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
            _version = Application.version;

            IEventLog events;
            AtlasServices.TryGet(out events);
            var model = new MenuModel(app.Registry, app.Spaces, this) { Events = events };
            IMenuView view = null;
            if (app.Boot != null && app.Boot.Hud != null && app.Pool != null) view = MenuView.Create(app.Boot.Hud, app.Pool);
            // The near anchor turns while the walker walks: the service turns the panel's targets with it, so
            // that a dwell or a press on a row is not lost at every frame.
            var service = app.InputService as AtlasInput;
            Action<GazeTargetId, Quaternion> turn = null;
            if (service != null) turn = service.RotateGazeTarget;
            Compose(app.InputService, app.Rig, app.Loop, model, view, turn);

            if (app.Loop != null)
            {
                app.Loop.AddUpload(this);
                app.Loop.AddAfterFrame(this);
            }
            if (app.Rig != null) app.Rig.ModeChanged += OnModeChanged;
        }

        /// <summary>
        /// The menu on its parts alone, with no app behind it: what <see cref="Install"/> ends in, and what a
        /// test of where the menu stands calls. Every part but the model may be null. turn rotates a rectangle
        /// target in place; without it a panel whose anchor turned has its targets made again.
        /// </summary>
        internal void Compose(IAtlasInput input, IStereoRig rig, IFrameLoop loop, MenuModel model, IMenuView view,
                              Action<GazeTargetId, Quaternion> turn = null)
        {
            _input = input;
            _rig = rig;
            _loop = loop;
            _model = model;
            _view = view;
            _turn = turn;
            // The gaze twin of the menu action: a chip down and to the left, live while a space runs.
            RegisterChip();
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

            // The panel comes up level, in the heading the chip stood in: where the walker was looking before
            // they reached for the chip, not the chip's own direction, so it opens in the same place every time.
            float3 head = float3.zero;
            if (_loop != null)
            {
                ref readonly FrameContext frame = ref _loop.Current;
                head = frame.RawHeadPosition;
                float yaw, pitch;
                if (MenuFrame.TryYawPitch(frame.RawHeadRotation, out yaw, out pitch)) _frame.Open(yaw, _frameSet);
            }
            _frameSet = true;
            // In a hands session it comes up within reach instead, at once: its targets never stand a metre away.
            float3 anchor;
            quaternion facing;
            float scale;
            bool near = ReachSlots.TryGetNear(out anchor, out facing, out scale) && InHeadset;
            SetReach(near, scale);
            float3 origin = _near ? ReachSlots.World(anchor, facing, _scale, MenuLayout.PanelSlot) : head;
            quaternion stand = _near ? facing : _frame.Rotation;
            RegisterTargets(origin, stand);
            if (_view != null)
            {
                _view.PlacePanel(origin + _panelOffset, stand);
                _view.SetOpen(true);
            }
            Present();
        }

        /// <summary>
        /// The space menu action: the menu on its Spaces page, opened first when it is closed.
        /// </summary>
        public void OpenOnSpaces()
        {
            Open();
            if (!_open) return;
            _model.SetPage(MenuPage.Spaces);
            if (_model.Revision != _shownRevision) Present();
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
        // A menu within reach is on the world layer, which the camera in the hand does draw, and step 6 does
        // not run for the menu there: it goes back in the frame of the change.
        internal void OnModeChanged(DisplayMode mode)
        {
            if (mode == DisplayMode.Headset) return;
            Close();
            if (!_shutDown) SetReach(false, 1f);
        }

        bool InHeadset { get { return _rig == null || _rig.Mode == DisplayMode.Headset; } }

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
                _offsets[MenuLayout.Minus] = Offset(_rotation, MenuLayout.Minus, slot);
                _offsets[MenuLayout.Plus] = Offset(_rotation, MenuLayout.Plus, slot);
            }
            EnableRows();
            if (_view != null) _view.Show(_model);
        }

        // Only a row that can take the focus is a target; the chips stay live, so every action read here keeps
        // its gaze twin. Also after the targets were made again: a new target starts enabled.
        void EnableRows()
        {
            for (int i = 0; i < MenuModel.VisibleRows; i++)
            {
                int row = _model.Top + i;
                _input.SetGazeTargetEnabled(_targets[i], row < _model.RowCount && _model.Row(row).Focusable);
            }
        }

        void RefreshStatus()
        {
            _sinceStatus = 0f;
            var status = new MenuStatus { Version = _version };
            if (_app != null)
            {
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
            }
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

            // The menu stands as it always has; in a hands session it then comes within reach instead.
            float3 anchor;
            quaternion facing;
            float scale;
            bool near = ReachSlots.TryGetNear(out anchor, out facing, out scale);
            bool reached = SetReach(near, scale);

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
                if (_near)
                {
                    rotation = facing;
                    centre = ReachSlots.World(anchor, facing, _scale, MenuLayout.ChipSlot);
                }
                _chipCentre = centre;
                if (_chip.IsValid) _input.MoveGazeTarget(_chip, centre);
                if (_view != null) _view.PlaceChip(centre, rotation);
                return;
            }

            // The heading frame keeps its own life within reach too, so that a menu that goes back stands where
            // it would have stood.
            bool turned = level && _frame.Recentre(yaw);
            float3 origin = head;
            quaternion stand = _frame.Rotation;
            if (_near)
            {
                origin = ReachSlots.World(anchor, facing, _scale, MenuLayout.PanelSlot);
                stand = facing;
                // The anchor turns while the walker walks and while it eases to a new place. Turned in place, the
                // targets follow it to the frame and keep a dwell or a press that is under way.
                turned = _turn == null && ReachSlots.RotationMoved(_rotation, facing);
                if (_turn != null && !reached) TurnTargets(facing);
            }
            if (turned || reached)
            {
                // Rare a metre away: the walker turned their back on the panel. Within reach: the menu came or
                // went, the scale moved, or the anchor turned under an input service that cannot turn a
                // rectangle. The targets are rectangles with a heading and a size, so they are made again
                // rather than moved.
                UnregisterTargets();
                RegisterTargets(origin, stand);
                // The text is rebuilt only where it always was: a turning anchor costs no string.
                if (_near || reached) EnableRows();
                else Present();
            }
            else
            {
                for (int i = 0; i < _targets.Length; i++) _input.MoveGazeTarget(_targets[i], origin + _offsets[i]);
            }
            if (_view != null) _view.PlacePanel(origin + _panelOffset, stand);
        }

        // Within reach: every target takes the anchor's rotation of this frame, and its place on the turned panel.
        void TurnTargets(quaternion rotation)
        {
            if (rotation.Equals(_rotation)) return;
            _rotation = rotation;
            for (int i = 0; i < _targets.Length; i++)
            {
                _offsets[i] = Offset(rotation, i, _shownSlot);
                _turn(_targets[i], rotation);
            }
        }

        // A target's place from the point the panel is hung from: the head when far, with the panel 1.2 m ahead
        // in the heading frame; the panel's own centre within reach, with the layout times the scale.
        float3 Offset(quaternion rotation, int target, int slot)
        {
            if (_near) return math.mul(rotation, _scale * MenuLayout.TargetCentre(target, slot));
            return math.mul(rotation, MenuLayout.PanelCentre + MenuLayout.TargetCentre(target, slot));
        }

        // Brings the menu within reach of the hands, or puts it back where it has always been: the view's layer,
        // size and text, and the chip's target. True when something changed: an open panel's targets are then
        // made again.
        bool SetReach(bool near, float scale)
        {
            float size = near ? scale : 1f;
            if (near == _near && !ReachSlots.ScaleMoved(_scale, size)) return false;
            _near = near;
            _scale = size;
            if (_view != null) _view.SetReach(near, size);
            RegisterChip();
            return true;
        }

        // A sphere's radius cannot be changed in place, so the chip's target is made again when the menu comes
        // within reach or goes back. It stands where the chip last stood until step 6 moves it; a new target
        // starts enabled, as the chip always is.
        void RegisterChip()
        {
            if (_input == null) return;
            if (_chip.IsValid) _input.UnregisterGazeTarget(_chip);
            float radius = _near ? ReachSlots.TargetHalf(MenuLayout.OpenChipRadius, _scale) : MenuLayout.OpenChipRadius;
            GazeTarget chip = GazeTarget.World(AtlasAction.ParamMenu, _chipCentre, radius);
            chip.Label = "menu chip";
            _chip = _input.RegisterGazeTarget(in chip);
        }

        // Rows select (the gaze twin of up and down), "-" and "+" step and repeat, the two chips confirm and
        // close. All in the room frame, following the head's position, or standing on the near anchor; live
        // while the menu holds the context.
        void RegisterTargets(float3 origin, quaternion rotation)
        {
            _rotation = rotation;
            _panelOffset = _near ? float3.zero : math.mul(rotation, MenuLayout.PanelCentre);
            int slot = _model.Selected - _model.Top;
            _shownSlot = slot;
            for (int i = 0; i < _targets.Length; i++)
            {
                _offsets[i] = Offset(rotation, i, slot);
                float2 half = _near ? MenuLayout.NearTargetHalfSize(i, _scale) : MenuLayout.TargetHalfSize(i);
                AtlasAction action;
                int tag = 0;
                string label;
                if (i < MenuLayout.Minus) { action = AtlasAction.NavDown; tag = i + 1; label = "menu row"; }
                else if (i == MenuLayout.Minus) { action = AtlasAction.NavLeft; label = "menu minus"; }
                else if (i == MenuLayout.Plus) { action = AtlasAction.NavRight; label = "menu plus"; }
                else if (i == MenuLayout.Apply) { action = AtlasAction.Confirm; label = "menu apply"; }
                else { action = AtlasAction.Back; label = "menu close"; }

                GazeTarget target = GazeTarget.WorldRect(action, origin + _offsets[i], rotation, new Vector2(half.x, half.y), tag);
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
