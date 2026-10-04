using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>
    /// The gaze dwell (Master Plan section 0.5 rule 5, task P1-C10; lane C contract section 13.2). Engine-free:
    /// it is given the head pose and the frame time and reads no clock (README F2). One target at most is in
    /// focus; looking at it for the dwell time fires it once, and it arms again only after the gaze has been
    /// away for a while. Allocates nothing.
    /// </summary>
    public sealed class GazeDwell
    {
        public const float DefaultDwellSeconds = 0.7f;
        public const float MinDwellSeconds = 0.6f;
        public const float MaxDwellSeconds = 0.8f;
        /// <summary>Leaving the target for less than this does not reset the dwell.</summary>
        public const float GraceSeconds = 0.15f;
        /// <summary>A Once target arms again after the gaze has been off it this long.</summary>
        public const float RearmSeconds = 0.3f;
        /// <summary>Small and far targets are still this wide to the gaze, as an angle about their centre.</summary>
        public const float MinAngleDeg = 2.5f;

        private const float TimeEpsilon = 1e-4f;
        private static readonly float MinTan = math.tan(math.radians(MinAngleDeg));

        private readonly GazeRegistry _registry;
        private readonly float[] _offSeconds = new float[GazeRegistry.Capacity];
        private readonly int[] _disarmedGeneration = new int[GazeRegistry.Capacity];
        private ulong _disarmed;                 // bit per slot: a Once target that fired and has not armed again

        private float _dwellSeconds = DefaultDwellSeconds;
        private int _focus = -1;
        private int _focusGeneration;
        private float _dwell;
        private float _away;
        private float _repeat;
        private bool _reached;
        private bool _holding;
        private float _progress;
        private float3 _point;

        public GazeDwell(GazeRegistry registry)
        {
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        public GazeRegistry Registry => _registry;

        /// <summary>Dwell time in seconds; clamped to 0.6 .. 0.8 (task P1-C10).</summary>
        public float DwellSeconds
        {
            get => _dwellSeconds;
            set => _dwellSeconds = math.isnan(value) ? DefaultDwellSeconds : math.clamp(value, MinDwellSeconds, MaxDwellSeconds);
        }

        /// <summary>The target in focus; default when nothing is under the gaze.</summary>
        public GazeTargetId Focused => _focus >= 0 ? new GazeTargetId(_focus, _focusGeneration) : default;

        /// <summary>0..1 of the dwell on the focused target. Repeat: 0..1 of the current repeat interval.</summary>
        public float Progress => _progress;

        /// <summary>World position of the focused target's centre.</summary>
        public float3 Point => _point;

        /// <summary>True from the dwell time until the gaze leaves, on a Hold target.</summary>
        public bool Holding => _holding && _focus >= 0;

        /// <summary>The Hold target being held, when <see cref="Holding"/>.</summary>
        public bool TryGetHold(out AtlasAction action, out AtlasAxis axis, out float axisValue)
        {
            if (!Holding)
            {
                action = AtlasAction.None;
                axis = default;
                axisValue = 0f;
                return false;
            }
            ref GazeTargetData d = ref _registry.DataAt(_focus);
            action = d.Action;
            axis = d.Axis;
            axisValue = d.AxisValue;
            return true;
        }

        /// <summary>Drops the focus and every pending re-arm: a context change, a new room.</summary>
        public void Reset()
        {
            ClearFocus();
            _disarmed = 0;
        }

        /// <summary>
        /// One frame. The gaze ray starts at the head and runs along the head's forward axis. Returns true when
        /// something fired this frame: the focused target by dwell or by the lever, or Confirm when the lever
        /// was tapped with nothing under the gaze.
        /// </summary>
        public bool Tick(float3 headPosition, quaternion headRotation, float deltaTime, InputContext context,
                         bool leverTap, out GazeFiring firing)
        {
            firing = default;
            float dt = deltaTime > 0f ? deltaTime : 0f;
            float3 forward = math.normalizesafe(math.mul(headRotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));

            int hit = FindHit(headPosition, headRotation, forward, context);
            UpdateRearm(hit, dt);

            // The focused target may have been removed, disabled or left behind by a context change.
            if (_focus >= 0 && !_registry.IsLive(_focus, _focusGeneration, context)) ClearFocus();

            bool justFocused = false;
            if (hit >= 0 && hit == _focus)
            {
                _away = 0f;
            }
            else if (_focus >= 0 && _away + dt < GraceSeconds)
            {
                _away += dt;                     // a blink, a head wobble: the dwell is kept but does not advance
            }
            else
            {
                ClearFocus();
                if (hit >= 0)
                {
                    _focus = hit;
                    _focusGeneration = _registry.GenerationAt(hit);
                    justFocused = true;
                }
            }

            if (_focus < 0)
            {
                _progress = 0f;
                if (!leverTap) return false;
                firing.Action = AtlasAction.Confirm;       // the lever alone confirms
                firing.Source = InputSource.Lever;
                return true;
            }

            ref GazeTargetData d = ref _registry.DataAt(_focus);
            _point = WorldCentre(ref d, headPosition, headRotation, forward);
            ulong bit = 1UL << _focus;
            bool fired = false;

            if (leverTap)
            {
                // The lever fires the target at once, armed or not; the dwell starts over.
                fired = Fire(ref d, InputSource.Lever, ref firing);
                _dwell = 0f;
                _repeat = 0f;
                if (d.Mode == GazeMode.Once) Disarm(bit);
            }
            else if (hit == _focus && !justFocused && (_disarmed & bit) == 0)
            {
                _dwell += dt;
                if (!_reached)
                {
                    if (_dwell >= _dwellSeconds - TimeEpsilon)
                    {
                        _reached = true;
                        _repeat = 0f;
                        fired = Fire(ref d, InputSource.Gaze, ref firing);
                        if (d.Mode == GazeMode.Once) Disarm(bit);
                        else if (d.Mode == GazeMode.Hold) _holding = true;
                    }
                }
                else if (d.Mode == GazeMode.Repeat)
                {
                    _repeat += dt;
                    if (_repeat >= d.RepeatSeconds - TimeEpsilon)
                    {
                        _repeat = math.max(_repeat - d.RepeatSeconds, 0f);
                        fired = Fire(ref d, InputSource.Gaze, ref firing);
                    }
                }
            }

            if ((_disarmed & bit) != 0) _progress = 0f;
            else if (!_reached) _progress = math.saturate(_dwell / _dwellSeconds);
            else if (d.Mode == GazeMode.Repeat) _progress = math.saturate(_repeat / d.RepeatSeconds);
            else _progress = 1f;
            return fired;
        }

        private bool Fire(ref GazeTargetData d, InputSource source, ref GazeFiring firing)
        {
            if (d.Action == AtlasAction.None && d.Callback == null) return false;   // an axis target has nothing to fire
            firing.Id = new GazeTargetId(_focus, _focusGeneration);
            firing.Action = d.Action;
            firing.Source = source;
            firing.Tag = d.Tag;
            firing.Callback = d.Callback;
            return true;
        }

        private void Disarm(ulong bit)
        {
            _disarmed |= bit;
            _offSeconds[_focus] = 0f;
            _disarmedGeneration[_focus] = _focusGeneration;
            _reached = true;
            _holding = false;
        }

        private void ClearFocus()
        {
            _focus = -1;
            _dwell = 0f;
            _away = 0f;
            _repeat = 0f;
            _reached = false;
            _holding = false;
            _progress = 0f;
        }

        // A fired Once target arms again after the gaze has been off it for RearmSeconds.
        private void UpdateRearm(int hit, float dt)
        {
            if (_disarmed == 0) return;
            for (int slot = 0; slot < GazeRegistry.Capacity; slot++)
            {
                ulong bit = 1UL << slot;
                if ((_disarmed & bit) == 0) continue;
                if (_registry.GenerationAt(slot) != _disarmedGeneration[slot])
                {
                    _disarmed &= ~bit;           // the target was removed; the slot may hold a new one
                    continue;
                }
                if (slot == hit)
                {
                    _offSeconds[slot] = 0f;
                    continue;
                }
                _offSeconds[slot] += dt;
                if (_offSeconds[slot] >= RearmSeconds - TimeEpsilon) _disarmed &= ~bit;
            }
        }

        private static float3 WorldCentre(ref GazeTargetData d, float3 headPosition, quaternion headRotation, float3 forward)
        {
            switch (d.Space)
            {
                case GazeSpace.Head: return headPosition + math.mul(headRotation, d.Centre);
                case GazeSpace.LookDown: return headPosition + forward;
                default: return d.Centre;
            }
        }

        // Picks the target under the gaze. A target the ray really passes through beats one that is only within
        // the angular tolerance; among the first the nearest along the ray wins (it hides the others), among the
        // second the smallest angle. LookDown targets come last: they have no place to look at.
        private int FindHit(float3 headPosition, quaternion headRotation, float3 forward, InputContext context)
        {
            int best = -1;
            int bestRank = int.MaxValue;
            float bestScore = float.MaxValue;
            int limit = _registry.SlotLimit;
            for (int slot = 0; slot < limit; slot++)
            {
                if (!_registry.IsLive(slot, context)) continue;
                ref GazeTargetData d = ref _registry.DataAt(slot);
                int rank;
                float score;
                if (d.Space == GazeSpace.LookDown)
                {
                    float pitchDeg = math.degrees(math.asin(math.clamp(forward.y, -1f, 1f)));
                    if (!(pitchDeg < d.PitchBelowDeg)) continue;
                    rank = 2;
                    score = pitchDeg - d.PitchBelowDeg;
                }
                else
                {
                    float3 centre = d.Space == GazeSpace.Head ? headPosition + math.mul(headRotation, d.Centre) : d.Centre;
                    bool exact;
                    bool under;
                    if (d.Shape == GazeShape.Rect)
                    {
                        quaternion rotation = d.Space == GazeSpace.Head ? math.mul(headRotation, d.Rotation) : d.Rotation;
                        under = RayRect(headPosition, forward, centre, rotation, d.HalfSize, d.MaxDistance, out exact, out score);
                    }
                    else
                    {
                        under = RaySphere(headPosition, forward, centre, d.Radius, d.MaxDistance, out exact, out score);
                    }
                    if (!under) continue;
                    rank = exact ? 0 : 1;
                }
                if (rank < bestRank || (rank == bestRank && score < bestScore))
                {
                    best = slot;
                    bestRank = rank;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>
        /// Sphere test of section 13.2: under the gaze when the angle between the ray and the direction to the
        /// centre is below max(atan(radius / distance), 2.5 degrees). exact: the ray passes through the sphere;
        /// score is then the distance along the ray to the entry point, otherwise the tangent of the angle.
        /// </summary>
        public static bool RaySphere(float3 origin, float3 forward, float3 centre, float radius, float maxDistance,
                                     out bool exact, out float score)
        {
            exact = false;
            score = 0f;
            float3 v = centre - origin;
            float distSq = math.lengthsq(v);
            if (distSq < 1e-8f) return false;
            float dist = math.sqrt(distSq);
            if (maxDistance > 0f && dist > maxDistance) return false;
            float along = math.dot(v, forward);
            if (along <= 0f) return false;                       // behind the head
            float perpSq = math.max(distSq - along * along, 0f);
            float perp = math.sqrt(perpSq);
            float tolerance = math.max(radius / dist, MinTan);   // tan of the allowed angle
            if (perp >= tolerance * along) return false;
            if (perpSq <= radius * radius)
            {
                exact = true;
                score = math.max(along - math.sqrt(radius * radius - perpSq), 0f);
            }
            else
            {
                score = perp / along;
            }
            return true;
        }

        /// <summary>
        /// Rectangle test: the ray meets the rectangle's plane inside its half extents, each widened to at least
        /// the 2.5 degree tolerance at that distance. Both faces count. exact and score as for the sphere.
        /// </summary>
        public static bool RayRect(float3 origin, float3 forward, float3 centre, quaternion rotation, float2 halfSize,
                                   float maxDistance, out bool exact, out float score)
        {
            exact = false;
            score = 0f;
            float3 normal = math.mul(rotation, new float3(0f, 0f, 1f));
            float denom = math.dot(forward, normal);
            if (math.abs(denom) < 1e-4f) return false;           // seen edge-on
            float t = math.dot(centre - origin, normal) / denom;
            if (t <= 1e-4f) return false;
            if (maxDistance > 0f && t > maxDistance) return false;
            float3 p = origin + forward * t - centre;
            float2 local = math.abs(new float2(math.dot(p, math.mul(rotation, new float3(1f, 0f, 0f))),
                                               math.dot(p, math.mul(rotation, new float3(0f, 1f, 0f)))));
            float2 excess = math.max(local - halfSize, float2.zero);
            if (excess.x <= 0f && excess.y <= 0f)
            {
                exact = true;
                score = t;
                return true;
            }
            float2 widened = math.max(halfSize, new float2(t * MinTan));
            if (local.x >= widened.x || local.y >= widened.y) return false;
            score = math.length(excess) / t;
            return true;
        }
    }
}
