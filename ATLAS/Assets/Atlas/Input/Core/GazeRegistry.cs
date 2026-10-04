using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>
    /// The gaze targets that spaces and menus have registered (lane C contract section 13.2: at most 64).
    /// Fixed storage, so adding, moving and removing allocate nothing and are safe at any time, including from
    /// a callback raised while the dwell is being evaluated: a slot is addressed by index and a stale handle is
    /// told apart by its generation.
    /// </summary>
    public sealed class GazeRegistry
    {
        public const int Capacity = 64;
        public const float DefaultRepeatSeconds = 0.4f;

        private readonly GazeTargetData[] _data = new GazeTargetData[Capacity];
        private readonly int[] _generation = new int[Capacity];
        private readonly bool[] _used = new bool[Capacity];
        private readonly bool[] _enabled = new bool[Capacity];
        private int _count;
        private int _slotLimit;

        /// <summary>Number of registered targets.</summary>
        public int Count => _count;

        /// <summary>One past the highest slot ever used. Iterate slots 0 .. SlotLimit - 1 with <see cref="TryGetSlot"/>.</summary>
        public int SlotLimit => _slotLimit;

        /// <summary>Adds a target, enabled. Returns default when all 64 slots are taken.</summary>
        public GazeTargetId Add(in GazeTargetData target)
        {
            for (int slot = 0; slot < Capacity; slot++)
            {
                if (_used[slot]) continue;
                _data[slot] = target;
                Normalise(ref _data[slot]);
                _used[slot] = true;
                _enabled[slot] = true;
                _generation[slot]++;
                _count++;
                if (slot >= _slotLimit) _slotLimit = slot + 1;
                return new GazeTargetId(slot, _generation[slot]);
            }
            return default;
        }

        /// <summary>Removes a target. A stale or default handle is ignored.</summary>
        public bool Remove(GazeTargetId id)
        {
            if (!Contains(id)) return false;
            int slot = id.Slot;
            _used[slot] = false;
            _enabled[slot] = false;
            _data[slot] = default;      // drops the label and the callback
            _generation[slot]++;
            _count--;
            return true;
        }

        public void Clear()
        {
            for (int slot = 0; slot < _slotLimit; slot++)
            {
                if (!_used[slot]) continue;
                _used[slot] = false;
                _enabled[slot] = false;
                _data[slot] = default;
                _generation[slot]++;
            }
            _count = 0;
        }

        public bool Contains(GazeTargetId id)
        {
            int slot = id.Slot;
            return slot >= 0 && slot < Capacity && _used[slot] && _generation[slot] == id.Generation;
        }

        /// <summary>Moves a target: its centre, in the frame it was registered in.</summary>
        public bool Move(GazeTargetId id, float3 centre)
        {
            if (!Contains(id)) return false;
            _data[id.Slot].Centre = centre;
            return true;
        }

        /// <summary>Rect targets: turns the rectangle.</summary>
        public bool SetRotation(GazeTargetId id, quaternion rotation)
        {
            if (!Contains(id)) return false;
            _data[id.Slot].Rotation = rotation;
            Normalise(ref _data[id.Slot]);
            return true;
        }

        public bool SetEnabled(GazeTargetId id, bool enabled)
        {
            if (!Contains(id)) return false;
            _enabled[id.Slot] = enabled;
            return true;
        }

        public bool IsEnabled(GazeTargetId id) => Contains(id) && _enabled[id.Slot];

        public bool TryGet(GazeTargetId id, out GazeTargetData target)
        {
            if (!Contains(id))
            {
                target = default;
                return false;
            }
            target = _data[id.Slot];
            return true;
        }

        /// <summary>The handle of the target in a slot, for walking the registry. False for an empty slot.</summary>
        public bool TryGetSlot(int slot, out GazeTargetId id)
        {
            if (slot < 0 || slot >= Capacity || !_used[slot])
            {
                id = default;
                return false;
            }
            id = new GazeTargetId(slot, _generation[slot]);
            return true;
        }

        /// <summary>True when an enabled target of the context fires the action.</summary>
        public bool HasEnabled(AtlasAction action, InputContext context)
        {
            if (action == AtlasAction.None) return false;
            for (int slot = 0; slot < _slotLimit; slot++)
                if (_used[slot] && _enabled[slot] && _data[slot].Context == context && _data[slot].Action == action)
                    return true;
            return false;
        }

        /// <summary>True when an enabled Hold target of the context drives the axis.</summary>
        public bool HasEnabled(AtlasAxis axis, InputContext context)
        {
            for (int slot = 0; slot < _slotLimit; slot++)
            {
                if (!_used[slot] || !_enabled[slot]) continue;
                ref GazeTargetData d = ref _data[slot];
                if (d.Context == context && d.Mode == GazeMode.Hold && d.Action == AtlasAction.None && d.Axis == axis)
                    return true;
            }
            return false;
        }

        internal bool IsLive(int slot, InputContext context)
            => _used[slot] && _enabled[slot] && _data[slot].Context == context;

        internal bool IsLive(int slot, int generation, InputContext context)
            => slot >= 0 && _generation[slot] == generation && IsLive(slot, context);

        internal ref GazeTargetData DataAt(int slot) => ref _data[slot];
        internal int GenerationAt(int slot) => _generation[slot];

        private static void Normalise(ref GazeTargetData d)
        {
            if (!(d.RepeatSeconds > 0f)) d.RepeatSeconds = DefaultRepeatSeconds;
            float lenSq = math.lengthsq(d.Rotation.value);
            d.Rotation = lenSq > 1e-12f ? new quaternion(d.Rotation.value / math.sqrt(lenSq)) : quaternion.identity;
            if (!(d.Radius > 0f)) d.Radius = 0f;
            d.HalfSize = math.max(d.HalfSize, float2.zero);
        }
    }
}
