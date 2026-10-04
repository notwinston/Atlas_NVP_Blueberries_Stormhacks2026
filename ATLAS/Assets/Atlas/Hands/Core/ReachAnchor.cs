using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The near anchor: where the panels of a hands session stand so that a hand can reach them
    /// (documents/ATLAS_Hands_Design.md section 6.1, with decision R3 and the review). One pose in the world: the
    /// origin is the panel's centre, +Z points away from the walker, +Y is up along the panel. Engine-free; time
    /// is passed in; nothing allocates.
    ///
    /// Where. <see cref="ReachMetres"/> from the rear camera (not the eyes: a pressing palm is nearer than its
    /// fingertip and LiDAR ends at 0.32 m), along a line ReachPitchDegrees below the horizontal in the direction
    /// the walker faces, and tilted to face the camera. The direction is the heading only, as HeadPose.Heading
    /// has it: forward on the floor, or, within 6 degrees of straight up or down, the way the head's up vector
    /// leans, so a walker looking down at the panel keeps it in front of them.
    ///
    /// When. It is set at once when near becomes true, and again whenever it is more than a metre from the camera
    /// (it was left far behind). Then it stays put in the room: small movements of the head do nothing. It
    /// places itself again, easing over ReachEaseSeconds, when the camera has moved ReachFollowMetres from where
    /// it was when the anchor was set, or when the heading has been more than ReachFollowDegrees from the heading
    /// then for ReachFollowSeconds in a row, or when ReachMetres was changed. While the walker walks it follows
    /// every frame: carried along with the camera, so it is never left behind and never walked through, while its
    /// place relative to the walker eases to the one that belongs to the heading now. It never moves while a
    /// fingertip is within ReachFreezeMetres of it or a press is under way: an ease in progress waits, and goes
    /// on when the finger has gone. <see cref="Near"/> does not change while a press is under way either.
    ///
    /// The room. The anchor keeps 5 cm in front of every surface of the room: where its line would end within 5 cm
    /// of one, or behind it, it is pulled back along the line. If that leaves it nearer than 0.30 m, where hands
    /// cannot be tracked, the line is raised towards the horizontal a step at a time, only as far as it takes, and
    /// the placement with the most room is used if none gives 0.30 m. It is never nearer than 0.10 m to the camera.
    /// </summary>
    public sealed class ReachAnchor : IReachLayout
    {
        // ---- What the design leaves to the code (the rest is in HandsConstants) ----

        /// <summary>The anchor is set again, at once, when it is farther than this from the camera, metres.</summary>
        internal const float SetAgainMetres = 1.0f;
        /// <summary>The walker is walking, and the anchor follows every frame, above this head floor speed, metres per second.</summary>
        internal const float WalkEnterSpeed = 0.30f;
        /// <summary>The walker has stopped when the head floor speed has been under this for <see cref="WalkExitSeconds"/>, metres per second.</summary>
        internal const float WalkExitSpeed = 0.15f;
        /// <summary>How long the head floor speed must stay under <see cref="WalkExitSpeed"/> before the walker counts as having stopped, seconds.</summary>
        internal const float WalkExitSeconds = 0.3f;
        /// <summary>The anchor keeps this far in front of a surface of the room, metres.</summary>
        internal const float SurfaceMarginMetres = 0.05f;
        /// <summary>A placement nearer to the camera than this is tried again with the line raised, metres (LiDAR is valid from about 0.30 m).</summary>
        internal const float MinUsefulMetres = 0.30f;
        /// <summary>The anchor is never put nearer to the camera than this, metres: a panel nearer is in the walker's face, whatever is in the way.</summary>
        internal const float MinPlacedMetres = 0.10f;
        /// <summary>The line is raised this much at a time, degrees.</summary>
        internal const float RaiseStepDegrees = 5f;
        /// <summary>The walk along a line towards a surface advances at least this far at a time, metres: a surface cannot slip between two steps.</summary>
        internal const float MarchStepMetres = 0.04f;
        /// <summary>The walk is at most this many steps, which a line of 0.6 m cannot reach.</summary>
        internal const int MarchStepsMax = 24;
        /// <summary>The pull back narrows to the margin by this many halvings: 0.04 m down to under a millimetre.</summary>
        internal const int PullBackHalvings = 6;
        /// <summary>The heading limit as a cosine: the heading is past it when the dot product of the two is under this.</summary>
        static readonly float s_cosFollow = math.cos(math.radians(HandsConstants.ReachFollowDegrees));

        float _reach = HandsConstants.ReachMetres;

        bool _near;
        /// <summary>The anchor has been set since near last became true.</summary>
        bool _placed;
        float3 _position = float3.zero;
        quaternion _rotation = quaternion.identity;

        // As things were when the anchor was last set.
        float3 _setCamera;
        float2 _setHeading;
        float _setReach;
        // The heading has been past the limit since this time.
        bool _turned;
        double _turnedSince;

        // Walking, with the hysteresis of its two speeds.
        bool _walking;
        bool _slow;
        double _slowSince;

        // An ease to a new place, and how far along it is.
        bool _easing;
        float3 _easeFromPosition, _easeToPosition;
        quaternion _easeFromRotation = quaternion.identity, _easeToRotation = quaternion.identity;
        float _easeElapsed;

        // The camera of the last Update, to carry the anchor along with the walker.
        bool _haveCamera;
        float3 _lastCamera;

        /// <summary>True in a hands session: the panels come to the anchor, with <see cref="Scale"/>.</summary>
        public bool Near { get { return _near; } }

        /// <summary>World pose of the anchor. It keeps its last value while <see cref="Near"/> is false.</summary>
        public float3 Position { get { return _position; } }

        /// <summary>World orientation of the anchor: +Z points away from the walker along the line the anchor sits on, +Y is up along the panel, so the panel faces the camera.</summary>
        public quaternion Rotation { get { return _rotation; } }

        /// <summary>HandsConstants.ReachPanelScale while <see cref="Near"/>, 1 otherwise.</summary>
        public float Scale { get { return _near ? HandsConstants.ReachPanelScale : 1f; } }

        /// <summary>
        /// Distance of the anchor from the camera, metres (the parameter hands.reach). Kept between
        /// ReachMinMetres and ReachMaxMetres; NaN is ignored. A new value places the anchor again, eased.
        /// </summary>
        public float ReachMetres
        {
            get { return _reach; }
            set
            {
                if (float.IsNaN(value)) return;
                _reach = math.clamp(value, HandsConstants.ReachMinMetres, HandsConstants.ReachMaxMetres);
            }
        }

        /// <summary>Forgets the placement and every timer: not near, no pose, and the next near sets the anchor afresh. <see cref="ReachMetres"/> is a setting and stays.</summary>
        public void Reset()
        {
            _near = false;
            _placed = false;
            _position = float3.zero;
            _rotation = quaternion.identity;
            _turned = false;
            _walking = false;
            _slow = false;
            _easing = false;
            _haveCamera = false;
        }

        /// <summary>
        /// One frame, in the sense step. now and deltaTime are seconds; near is the gate's answer; the camera
        /// is the tracked rear camera in world space and headRotation its orientation (only the heading is
        /// used); headFloorSpeed is the head's speed over the floor in metres per second; each fingertip is the
        /// index tip in world space, valid or not; pressing says a press is under way; surfaces is the room's,
        /// or null. While near is false the pose is left as it was, and the scale reads 1. <see cref="Near"/>
        /// changes only while no press is under way: the panels register their targets anew when it changes,
        /// which they must not do under a finger, so a press holds it as it is until the press ends.
        /// </summary>
        public void Update(double now, float deltaTime, bool near, float3 cameraPosition, quaternion headRotation,
                           float headFloorSpeed, bool leftTipValid, float3 leftTip, bool rightTipValid, float3 rightTip,
                           bool pressing, RoomSurfaces surfaces)
        {
            if (pressing) near = _near;
            if (!near)
            {
                _near = false;
                _placed = false;
                _easing = false;
                _walking = false;
                _slow = false;
                _turned = false;
                _haveCamera = false;
                return;
            }
            _near = true;
            if (!(deltaTime > 0f)) deltaTime = 0f;
            float2 heading = new HeadPose(now, cameraPosition, headRotation, TrackingState.Normal).Heading;
            UpdateWalking(now, headFloorSpeed);

            if (!_placed)
            {
                // Near has just become true: set where the walker is, whatever a fingertip is doing.
                SetNow(cameraPosition, heading, surfaces);
                _placed = true;
            }
            else
            {
                Advance(now, deltaTime, cameraPosition, heading, leftTipValid, leftTip, rightTipValid, rightTip, pressing, surfaces);
            }
            _lastCamera = cameraPosition;
            _haveCamera = true;
        }

        void Advance(double now, float deltaTime, float3 camera, float2 heading, bool leftTipValid, float3 leftTip,
                     bool rightTipValid, float3 rightTip, bool pressing, RoomSurfaces surfaces)
        {
            bool turnedAway = HeadingPastTheLimit(now, heading);

            // A fingertip on it or a press: nothing moves, and an ease in progress waits.
            if (pressing || TipIsNear(leftTipValid, leftTip) || TipIsNear(rightTipValid, rightTip)) return;

            // Left far behind: set again, at once.
            if (math.distance(_position, camera) > SetAgainMetres)
            {
                SetNow(camera, heading, surfaces);
                return;
            }

            if (_walking)
            {
                Follow(deltaTime, camera, heading, surfaces);
                return;
            }

            float follow = HandsConstants.ReachFollowMetres;
            if (math.distancesq(camera, _setCamera) > follow * follow || turnedAway || math.abs(_reach - _setReach) > 1e-4f)
                StartEase(camera, heading, surfaces);
            if (_easing) AdvanceEase(deltaTime);
        }

        // ---- Walking, and the heading --------------------------------------------------------------------------

        // Walking begins above one speed and ends when the speed has stayed under a lower one, so that a stroll
        // near the limit does not flicker between following and holding still.
        void UpdateWalking(double now, float speed)
        {
            if (!(speed > 0f)) speed = 0f;
            if (!_walking)
            {
                if (speed > WalkEnterSpeed)
                {
                    _walking = true;
                    _slow = false;
                }
                return;
            }
            if (speed >= WalkExitSpeed)
            {
                _slow = false;
                return;
            }
            if (!_slow)
            {
                _slow = true;
                _slowSince = now;
                return;
            }
            if (now < _slowSince) _slowSince = now;
            if (now - _slowSince >= WalkExitSeconds)
            {
                _walking = false;
                _slow = false;
            }
        }

        // True once the heading has been more than the limit from the heading at the last set for long enough.
        bool HeadingPastTheLimit(double now, float2 heading)
        {
            if (math.dot(heading, _setHeading) >= s_cosFollow)
            {
                _turned = false;
                return false;
            }
            if (!_turned)
            {
                _turned = true;
                _turnedSince = now;
            }
            else if (now < _turnedSince)
            {
                _turnedSince = now;
            }
            return now - _turnedSince >= HandsConstants.ReachFollowSeconds;
        }

        bool TipIsNear(bool valid, float3 tip)
        {
            float freeze = HandsConstants.ReachFreezeMetres;
            return valid && math.distancesq(tip, _position) <= freeze * freeze;
        }

        // ---- Setting, easing, following ------------------------------------------------------------------------

        void SetNow(float3 camera, float2 heading, RoomSurfaces surfaces)
        {
            float3 position;
            quaternion rotation;
            Target(camera, heading, surfaces, out position, out rotation);
            _position = position;
            _rotation = rotation;
            _easing = false;
            Remember(camera, heading);
        }

        void StartEase(float3 camera, float2 heading, RoomSurfaces surfaces)
        {
            float3 position;
            quaternion rotation;
            Target(camera, heading, surfaces, out position, out rotation);
            _easeFromPosition = _position;
            _easeFromRotation = _rotation;
            _easeToPosition = position;
            _easeToRotation = rotation;
            _easeElapsed = 0f;
            _easing = true;
            Remember(camera, heading);
        }

        // Smoothstep over ReachEaseSeconds: gentle at both ends, so a panel does not lurch.
        void AdvanceEase(float deltaTime)
        {
            _easeElapsed += deltaTime;
            float u = math.saturate(_easeElapsed / HandsConstants.ReachEaseSeconds);
            if (u >= 1f)
            {
                _position = _easeToPosition;
                _rotation = _easeToRotation;
                _easing = false;
                return;
            }
            float s = u * u * (3f - 2f * u);
            _position = math.lerp(_easeFromPosition, _easeToPosition, s);
            _rotation = math.slerp(_easeFromRotation, _easeToRotation, s);
        }

        // While the walker walks: the anchor moves with the camera, so it can be neither left behind nor walked
        // through, and the place it holds relative to the camera eases to the one the heading asks for now. The
        // easing is exponential, 95 per cent done in ReachEaseSeconds, because the target moves every frame.
        void Follow(float deltaTime, float3 camera, float2 heading, RoomSurfaces surfaces)
        {
            _easing = false;
            if (_haveCamera) _position += camera - _lastCamera;
            float3 position;
            quaternion rotation;
            Target(camera, heading, surfaces, out position, out rotation);
            float k = 1f - math.exp(-3f * deltaTime / HandsConstants.ReachEaseSeconds);
            _position += (position - _position) * k;
            _rotation = math.slerp(_rotation, rotation, k);
            // Whatever the easing had in mind, the anchor is kept out of the walls.
            if (surfaces != null && surfaces.Enabled) _position = KeepClear(surfaces, camera, _position);
            Remember(camera, heading);
        }

        void Remember(float3 camera, float2 heading)
        {
            _setCamera = camera;
            _setHeading = heading;
            _setReach = _reach;
            _turned = false;
        }

        // ---- Where the anchor belongs --------------------------------------------------------------------------

        static float3 Direction(float2 heading, float pitchDegrees)
        {
            float pitch = math.radians(pitchDegrees);
            float c = math.cos(pitch);
            return new float3(heading.x * c, -math.sin(pitch), heading.y * c);
        }

        // The pose for this camera and heading: ReachMetres along the line, pulled back from the room's
        // surfaces, tilted to face the camera.
        void Target(float3 camera, float2 heading, RoomSurfaces surfaces, out float3 position, out quaternion rotation)
        {
            float reach = _reach;
            float3 direction = Direction(heading, HandsConstants.ReachPitchDegrees);
            float t = reach;
            if (surfaces != null && surfaces.Enabled)
            {
                t = PullBack(surfaces, camera, direction, reach);
                if (t < MinUsefulMetres)
                {
                    // Too near the camera for a hand to be tracked there: the line is raised towards the
                    // horizontal, a step at a time, only as far as it takes. If no step gives enough room the one
                    // with the most is kept.
                    float bestT = t;
                    float bestPitch = HandsConstants.ReachPitchDegrees;
                    for (float pitch = HandsConstants.ReachPitchDegrees - RaiseStepDegrees; pitch >= -1e-3f; pitch -= RaiseStepDegrees)
                    {
                        float p = math.max(pitch, 0f);
                        float raised = PullBack(surfaces, camera, Direction(heading, p), reach);
                        if (raised >= bestT)
                        {
                            bestT = raised;
                            bestPitch = p;
                        }
                        if (raised >= MinUsefulMetres) break;
                    }
                    t = bestT;
                    direction = Direction(heading, bestPitch);
                }
                t = math.max(t, math.min(reach, MinPlacedMetres));
            }
            position = camera + direction * t;
            rotation = quaternion.LookRotationSafe(direction, new float3(0f, 1f, 0f));
        }

        // A position that was not put there by Target (the walking anchor's) kept out of the room all the same:
        // pulled back along its line from the camera, never to nearer than MinPlacedMetres.
        static float3 KeepClear(RoomSurfaces surfaces, float3 camera, float3 position)
        {
            float3 offset = position - camera;
            float length = math.length(offset);
            if (length < 1e-4f) return position;
            float3 direction = offset / length;
            float t = PullBack(surfaces, camera, direction, length);
            if (t >= length) return position;
            return camera + direction * math.max(t, math.min(length, MinPlacedMetres));
        }

        // How far along a line from a point the anchor may go: length when nothing is in the way, otherwise the
        // distance at which it is SurfaceMarginMetres from the first surface. The surfaces answer "the nearest one
        // within a distance", so the line is walked: the distance to the nearest surface says how far it is safe to
        // step, and the first step that lands inside the margin is narrowed by halving.
        static float PullBack(RoomSurfaces surfaces, float3 from, float3 direction, float length)
        {
            float3 point, normal;
            // One question covers the whole line, which lies within half its length of its middle.
            if (!surfaces.TryClosest(from + direction * (0.5f * length), 0.5f * length + SurfaceMarginMetres, out point, out normal))
                return length;

            float clear = -1f;      // the farthest distance known to be outside the margin
            float t = 0f;
            for (int i = 0; i < MarchStepsMax; i++)
            {
                float3 p = from + direction * t;
                if (!surfaces.TryClosest(p, length - t + SurfaceMarginMetres, out point, out normal)) return length;
                float gap = math.distance(p, point);
                if (gap <= SurfaceMarginMetres)
                {
                    if (clear < 0f) return 0f;      // the camera itself is that close
                    float lo = clear, hi = t;
                    for (int k = 0; k < PullBackHalvings; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (surfaces.TryClosest(from + direction * mid, SurfaceMarginMetres, out point, out normal)) hi = mid;
                        else lo = mid;
                    }
                    return lo;
                }
                clear = t;
                if (t >= length) return length;
                t = math.min(length, t + math.max(gap - SurfaceMarginMetres, MarchStepMetres));
            }
            return clear < 0f ? 0f : clear;
        }
    }
}
