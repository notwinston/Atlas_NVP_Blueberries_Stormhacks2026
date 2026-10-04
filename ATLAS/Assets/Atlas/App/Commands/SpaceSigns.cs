using System;
using Atlas.Input;
using Atlas.App.Menu;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App
{
    /// <summary>
    /// Where the four SpaceLab signs stand, in the heading frame of <see cref="MenuFrame"/>: x right, y up,
    /// z ahead, metres, in the lower periphery, clear of the menu's corner chip (lane C contract, section 13.1;
    /// Master Plan section 0.5 rule 5). The gaze ray is the head's forward axis, so a sign must stand in the room
    /// frame and follow the head's position, never turn with the head. Engine-free.
    /// </summary>
    public static class SpaceSignLayout
    {
        public const int Count = 4;
        /// <summary>The signs in order: the home beacon, the previous and next signs, the exhibit sign.</summary>
        public const int Home = 0, Previous = 1, Menu = 2, Next = 3;

        /// <summary>A sign's gaze sphere. At one metre this is four degrees, more than the minimum tolerance.</summary>
        public const float Radius = 0.07f;
        /// <summary>The drawn board of a sign, half width and half height.</summary>
        public const float BoardHalfWidth = 0.075f, BoardHalfHeight = 0.045f;
        /// <summary>The signs are drawn only while the head looks down past this, and always while one is dwelt on.</summary>
        public static readonly float RevealPitch = math.radians(-10f);

        // The beacon stands low and in the middle; the three signs make a row under it, all below the horizon.
        // The menu chip (-0.55, -0.40) is below it too, in its own frame; the chips of the spaces sit level
        // with the horizon or above it, so no heading of their frame puts one on a sign.
        static readonly float3[] s_centres =
        {
            new float3(0f, -0.35f, 1.0f),
            new float3(-0.25f, -0.52f, 1.0f),
            new float3(0f, -0.52f, 1.0f),
            new float3(0.25f, -0.52f, 1.0f),
        };

        public static AtlasAction ActionOf(int sign)
        {
            switch (sign)
            {
                case Home: return AtlasAction.ReturnHome;
                case Previous: return AtlasAction.PrevSpace;
                case Next: return AtlasAction.NextSpace;
                default: return AtlasAction.SpaceMenu;
            }
        }

        public static string LabelOf(int sign)
        {
            switch (sign)
            {
                case Home: return "home beacon";
                case Previous: return "previous sign";
                case Next: return "next sign";
                default: return "exhibit sign";
            }
        }

        /// <summary>The text on the board.</summary>
        public static string TextOf(int sign)
        {
            switch (sign)
            {
                case Home: return "home";
                case Previous: return "prev";
                case Next: return "next";
                default: return "menu";
            }
        }

        /// <summary>Centre of a sign in the heading frame.</summary>
        public static float3 Centre(int sign)
        {
            return s_centres[sign];
        }

        /// <summary>
        /// A sign's place within reach of the hands (documents/ATLAS_Hands_Design.md section 6.1): about the near
        /// anchor as it is about the head a metre away, the beacon in the middle and the three signs in a row
        /// under it.
        /// </summary>
        public static ReachSlot SlotOf(int sign)
        {
            switch (sign)
            {
                case Home: return ReachSlot.SignHome;
                case Previous: return ReachSlot.SignPrevious;
                case Next: return ReachSlot.SignNext;
                default: return ReachSlot.SignMenu;
            }
        }
    }

    /// <summary>What draws the signs. The engine-free logic below drives it; null draws nothing.</summary>
    public interface ISpaceSignView
    {
        /// <summary>Draws the signs, or hides them. Called when it changes.</summary>
        void SetVisible(bool visible);
        /// <summary>A sign that can do nothing now is drawn dim. Called when it changes.</summary>
        void SetAvailable(int sign, bool available);
        /// <summary>
        /// Stands the signs: about the head in the heading frame, or, within reach, at the near anchor in the
        /// anchor's frame. Called every frame while shown.
        /// </summary>
        void Place(float3 origin, quaternion frame);
        /// <summary>
        /// Brings the signs within reach of the hands at this scale, on their slots about the near anchor, drawn
        /// on the world layer and depth-tested; or puts them back about the head on the head-locked layer at
        /// their own size. Called when it changes.
        /// </summary>
        void SetReach(bool near, float scale);
    }

    /// <summary>
    /// The gaze twins of the four SpaceLab actions: a home beacon for ReturnHome, a previous and a next sign for
    /// PrevSpace and NextSpace, and an exhibit sign for SpaceMenu. Each is a <see cref="GazeTarget"/> with the
    /// action it stands for, so a dwell lands in the same <see cref="SpaceActions.Do"/> as the gamepad. A sign is
    /// live exactly when that call would not refuse: not while the phone is in the hand, a switch is under way,
    /// or the demo lock is on, and the neighbour signs only while another space can be entered.
    /// <para>
    /// In a hands session the signs come within reach (documents/ATLAS_Hands_Design.md section 6.1, decision
    /// H14): they place themselves as they always have, then ask <see cref="ReachSlots.TryGetNear"/>, and on
    /// true each stands at its slot in the near anchor's frame with its sphere and its board times the scale.
    /// A metre away a sign that is not drawn can still be dwelt on, because the gaze must look down to reach
    /// it and that reveals it. Within reach a fingertip passing through the anchor would press what the walker
    /// cannot see, so there a sign is live only while it is drawn.
    /// </para>
    /// <para>
    /// Cost: four targets and, at step 6, four moves and one transform; four enabled flags and a neighbour
    /// search at step 8b. No allocation.
    /// </para>
    /// </summary>
    public sealed class SpaceSigns : IUploadStep, IAfterFrameStep
    {
        readonly IAtlasInput _input;
        readonly SpaceActions _actions;
        readonly ISpaceHost _host;
        readonly ISpaceSignView _view;
        readonly GazeTargetId[] _ids = new GazeTargetId[SpaceSignLayout.Count];
        readonly bool[] _enabled = new bool[SpaceSignLayout.Count];

        MenuFrame _frame;
        bool _frameSet, _visible, _shutDown;
        // Within reach of the hands, and the scale in force there (1 when far).
        bool _near;
        float _scale = 1f;

        public SpaceSigns(IAtlasInput input, SpaceActions actions, ISpaceHost host, ISpaceSignView view)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            if (host == null) throw new ArgumentNullException(nameof(host));
            _input = input;
            _actions = actions;
            _host = host;
            _view = view;
            for (int i = 0; i < _ids.Length; i++) Register(i);
        }

        // The room frame's origin until step 6 places it. Live in the Space context. A new target starts
        // enabled, so it takes its own flag at once.
        void Register(int sign)
        {
            float radius = _near ? ReachSlots.TargetHalf(SpaceSignLayout.Radius, _scale) : SpaceSignLayout.Radius;
            GazeTarget target = GazeTarget.World(SpaceSignLayout.ActionOf(sign), Vector3.zero, radius, sign);
            target.Label = SpaceSignLayout.LabelOf(sign);
            _ids[sign] = _input.RegisterGazeTarget(in target);
            _input.SetGazeTargetEnabled(_ids[sign], IsLive(sign));
        }

        /// <summary>The target of a sign, for tests and tools.</summary>
        public GazeTargetId TargetOf(int sign) { return _ids[sign]; }

        /// <summary>True while a sign's target is enabled: its action can act, and within reach it is also drawn.</summary>
        public bool IsLive(int sign) { return _enabled[sign] && (!_near || _visible); }

        /// <summary>True while the signs stand within reach of the hands.</summary>
        public bool Near { get { return _near; } }

        /// <summary>True while the signs are drawn.</summary>
        public bool Visible { get { return _visible; } }

        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            for (int i = 0; i < _ids.Length; i++)
            {
                if (_ids[i].IsValid) _input.UnregisterGazeTarget(_ids[i]);
                _ids[i] = default(GazeTargetId);
                _enabled[i] = false;
            }
            if (_visible && _view != null) _view.SetVisible(false);
            _visible = false;
            var disposable = _view as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        // ---- step 8b: which signs can do something ---------------------------------------------------------

        public void AfterFrame(in FrameContext frame)
        {
            if (_shutDown) return;
            bool refused = _actions.Refused(_input.Context);
            SpaceId neighbour;
            Set(SpaceSignLayout.Home, !refused);
            Set(SpaceSignLayout.Previous, !refused && SpaceActions.TryNeighbour(_host, -1, out neighbour));
            Set(SpaceSignLayout.Next, !refused && SpaceActions.TryNeighbour(_host, 1, out neighbour));
            Set(SpaceSignLayout.Menu, !refused && _actions.OpenSpaceMenu != null);
        }

        void Set(int sign, bool live)
        {
            if (_enabled[sign] == live) return;
            _enabled[sign] = live;
            _input.SetGazeTargetEnabled(_ids[sign], IsLive(sign));
            if (_view != null) _view.SetAvailable(sign, live);
        }

        // ---- step 6: where the signs stand ---------------------------------------------------------------------

        public void Upload(in FrameContext frame)
        {
            if (_shutDown) return;
            InputContext context = _input.Context;
            if (context == InputContext.InHand)
            {
                // Not in the headset: nothing is drawn, and the heading starts again from the walker's.
                _frameSet = false;
                Show(false);
                return;
            }

            float yaw, pitch;
            bool level = MenuFrame.TryYawPitch(frame.RawHeadRotation, out yaw, out pitch);
            bool dwelt = OnASign();
            if (level)
            {
                if (!_frameSet)
                {
                    _frame.Snap(yaw);
                    _frameSet = true;
                }
                else
                {
                    // A sign holds still under the gaze, or it could never be dwelt on.
                    _frame.Follow(yaw, pitch, frame.DeltaTime, dwelt);
                }
            }

            quaternion rotation = _frame.Rotation;
            float3 head = frame.RawHeadPosition;

            // The signs stand as they always have; in a hands session they then come within reach instead.
            float3 anchor;
            quaternion facing;
            float scale;
            bool near = ReachSlots.TryGetNear(out anchor, out facing, out scale);
            SetReach(near, scale);
            if (_near)
            {
                head = anchor;
                rotation = facing;
                for (int i = 0; i < _ids.Length; i++)
                {
                    float3 centre = ReachSlots.World(anchor, facing, _scale, SpaceSignLayout.SlotOf(i));
                    _input.MoveGazeTarget(_ids[i], new Vector3(centre.x, centre.y, centre.z));
                }
            }
            else
            {
                for (int i = 0; i < _ids.Length; i++)
                {
                    float3 centre = head + math.mul(rotation, SpaceSignLayout.Centre(i));
                    _input.MoveGazeTarget(_ids[i], new Vector3(centre.x, centre.y, centre.z));
                }
            }

            // Out of the way of the menu panel, and out of sight until the walker looks down for them.
            Show(context == InputContext.Space && (dwelt || pitch < SpaceSignLayout.RevealPitch));
            if (_visible && _view != null) _view.Place(head, rotation);
        }

        // The targets are spheres, whose radius cannot be changed in place: they are made again when the signs
        // come within reach, go back, or the scale moves, and each takes its own flag again.
        void SetReach(bool near, float scale)
        {
            float size = near ? scale : 1f;
            if (near == _near && !ReachSlots.ScaleMoved(_scale, size)) return;
            _near = near;
            _scale = size;
            for (int i = 0; i < _ids.Length; i++)
            {
                if (_ids[i].IsValid) _input.UnregisterGazeTarget(_ids[i]);
                Register(i);
            }
            if (_view != null) _view.SetReach(near, size);
        }

        bool OnASign()
        {
            GazeTargetId focused = _input.Gaze.Focused;
            if (!focused.IsValid) return false;
            for (int i = 0; i < _ids.Length; i++)
                if (focused.Equals(_ids[i])) return true;
            return false;
        }

        void Show(bool visible)
        {
            if (visible == _visible) return;
            _visible = visible;
            if (_view != null) _view.SetVisible(visible);
            // Within reach a sign is live only while it is drawn.
            if (!_near) return;
            for (int i = 0; i < _ids.Length; i++) _input.SetGazeTargetEnabled(_ids[i], IsLive(i));
        }
    }
}
