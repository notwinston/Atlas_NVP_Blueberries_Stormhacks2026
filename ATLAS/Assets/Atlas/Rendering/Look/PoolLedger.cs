using System;

namespace Atlas.Rendering
{
    /// <summary>
    /// The book-keeping of a pool of numbered slots: which are rented, which are free, and growth by doubling.
    /// Engine-free, so the rules of <see cref="MaterialPool"/> are tested without the engine.
    /// </summary>
    public sealed class PoolLedger
    {
        int[] m_Free;        // stack of free slots; valid up to m_FreeCount
        bool[] m_Rented;
        int m_FreeCount;

        public PoolLedger(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Free = new int[capacity];
            m_Rented = new bool[capacity];
            // Slot 0 is handed out first.
            for (int i = 0; i < capacity; i++) m_Free[i] = capacity - 1 - i;
            m_FreeCount = capacity;
        }

        /// <summary>Slots that exist.</summary>
        public int Capacity => m_Rented.Length;
        /// <summary>Slots handed out and not yet returned.</summary>
        public int Rented => m_Rented.Length - m_FreeCount;
        /// <summary>True when the next <see cref="Rent"/> must be preceded by <see cref="Grow"/>.</summary>
        public bool Exhausted => m_FreeCount == 0;

        /// <summary>Doubles the capacity. The new slots are Capacity/2 .. Capacity - 1. Returns the old capacity.</summary>
        public int Grow()
        {
            int old = m_Rented.Length;
            int capacity = old * 2;
            Array.Resize(ref m_Rented, capacity);
            Array.Resize(ref m_Free, capacity);
            for (int slot = capacity - 1; slot >= old; slot--) m_Free[m_FreeCount++] = slot;
            return old;
        }

        /// <summary>Hands out a free slot. Allocates nothing. Throws when the ledger is exhausted.</summary>
        public int Rent()
        {
            if (m_FreeCount == 0) throw new InvalidOperationException("The pool is exhausted; call Grow first.");
            int slot = m_Free[--m_FreeCount];
            m_Rented[slot] = true;
            return slot;
        }

        /// <summary>Takes a slot back. A slot that is not rented, or does not exist, is an ArgumentException.</summary>
        public void Return(int slot)
        {
            if (slot < 0 || slot >= m_Rented.Length)
                throw new ArgumentException("The slot does not belong to this pool.", nameof(slot));
            if (!m_Rented[slot])
                throw new ArgumentException("The slot was returned twice.", nameof(slot));
            m_Rented[slot] = false;
            m_Free[m_FreeCount++] = slot;
        }

        /// <summary>True when the slot is rented.</summary>
        public bool IsRented(int slot)
        {
            return slot >= 0 && slot < m_Rented.Length && m_Rented[slot];
        }
    }
}
