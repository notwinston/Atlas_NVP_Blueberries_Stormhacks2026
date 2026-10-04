using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Room
{
    /// <summary>Which wall mesh the room shows.</summary>
    public enum WallShown : byte { None = 0, Walls = 1, Kerbs = 2 }

    /// <summary>
    /// The walls sinking to kerbs, rising again or vanishing (Master Plan section 4.6). Engine-free: time comes
    /// in as dt. While the walls move, the full wall mesh is drawn scaled in height; when they arrive at kerb
    /// height the kerb mesh takes its place, and at zero nothing is drawn. No mesh is rebuilt.
    /// </summary>
    public struct RoomWallSink
    {
        /// <summary>The mode asked for last.</summary>
        public WallMode Mode;
        public WallShown Shown;
        /// <summary>Height of the walls as a share of their full height: 1 full, the kerb ratio as kerbs, 0 hidden.</summary>
        public float Level;
        public bool Moving;

        float m_Target, m_Speed, m_KerbRatio;

        /// <summary>The Y scale of the wall renderer: the level while the wall mesh is shown, 1 for the kerbs.</summary>
        public float ScaleY { get { return Shown == WallShown.Walls ? Level : 1f; } }

        /// <summary>Walls in full, at once.</summary>
        public void Reset(float kerbRatio)
        {
            m_KerbRatio = math.saturate(kerbRatio);
            Mode = WallMode.Full;
            Shown = WallShown.Walls;
            Level = 1f;
            Moving = false;
            m_Target = 1f;
            m_Speed = 0f;
        }

        /// <summary>Starts a change that takes `seconds` (0 or less: at once). kerbRatio is the kerb height
        /// over the wall height.</summary>
        public void Set(WallMode mode, float seconds, float kerbRatio)
        {
            m_KerbRatio = math.saturate(kerbRatio);
            Mode = mode;
            m_Target = mode == WallMode.Full ? 1f : mode == WallMode.Kerbs ? m_KerbRatio : 0f;
            float distance = math.abs(m_Target - Level);
            if (!(seconds > 0f) || distance < 1e-5f)
            {
                Arrive();
                return;
            }
            Shown = WallShown.Walls;
            Moving = true;
            m_Speed = distance / seconds;
        }

        /// <summary>The kerb height changed: kerbs follow at once, a move towards kerbs aims at the new height.</summary>
        public void SetKerbRatio(float kerbRatio)
        {
            m_KerbRatio = math.saturate(kerbRatio);
            if (Mode != WallMode.Kerbs) return;
            m_Target = m_KerbRatio;
            if (!Moving) Level = m_KerbRatio;
        }

        /// <summary>Advances the move. True when something to draw changed.</summary>
        public bool Tick(float dt)
        {
            if (!Moving) return false;
            float step = m_Speed * math.max(dt, 0f);
            if (math.abs(m_Target - Level) <= step) Arrive();
            else Level += m_Target > Level ? step : -step;
            return true;
        }

        void Arrive()
        {
            Moving = false;
            Level = m_Target;
            Shown = Mode == WallMode.Full ? WallShown.Walls : Mode == WallMode.Kerbs ? WallShown.Kerbs : WallShown.None;
        }
    }
}
