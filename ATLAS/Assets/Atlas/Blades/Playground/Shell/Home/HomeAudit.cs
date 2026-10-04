using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>What <see cref="HomeAudit.Count"/> found.</summary>
    public struct HomeAuditResult
    {
        /// <summary>Objects in the RoomModel.</summary>
        public int Objects;
        /// <summary>Objects that have a view showing a placeholder or a mesh.</summary>
        public int Drawn;
        /// <summary>Of those, how many show a placeholder box (state Placeholder or Requested).</summary>
        public int Placeholders;
        /// <summary>Of those, how many show a mesh (state Ready, BuiltIn or StandIn).</summary>
        public int Meshes;

        /// <summary>Drawn over Objects; 1 for a room with no objects.</summary>
        public float Fraction { get { return Objects == 0 ? 1f : (float)Drawn / Objects; } }
        /// <summary>Every object box shows a placeholder or a mesh (plan task P2-C02: 100%).</summary>
        public bool AllDrawn { get { return Drawn == Objects; } }
    }

    /// <summary>
    /// Beat 2, Home appears (Master Plan section 10.1.2): "each object box shows a placeholder tinted by category;
    /// Forge meshes arrive and replace them one by one". The audit counts, from the room content, how many of the
    /// room's boxes are drawn and as what. Engine-free: it reads only the state of each view.
    /// </summary>
    public static class HomeAudit
    {
        /// <summary>Counts the content's views against the room it was built from. A content that is not built
        /// has no views, so nothing is drawn.</summary>
        public static HomeAuditResult Count(IRoomContent content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            HomeAuditResult result = default(HomeAuditResult);
            RoomModel room = content.Room;
            if (room == null) return result;

            RoomObject[] objects = room.Objects;
            result.Objects = objects.Length;
            for (int k = 0; k < objects.Length; k++)
            {
                IRoomObjectView view;
                if (!content.TryGetObject(objects[k].Id, out view) || view == null) continue;
                switch (view.Shown)
                {
                    case MeshState.Placeholder:
                    case MeshState.Requested:
                        result.Drawn++;
                        result.Placeholders++;
                        break;
                    case MeshState.Ready:
                    case MeshState.BuiltIn:
                    case MeshState.StandIn:
                        result.Drawn++;
                        result.Meshes++;
                        break;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Watches the placeholder-to-mesh swap and measures how far it moves each box's centre (plan task P2-C02:
    /// less than 0.05 m). The box is the RoomModel's: its centre is the base point plus half the height, and a
    /// view that keeps its box leaves that centre where it was. Engine-free: a thin adapter feeds it positions.
    /// </summary>
    public sealed class SwapMonitor
    {
        /// <summary>Metres: a swap moves the box centre by less than this.</summary>
        public const float MaxCentreShiftM = 0.05f;

        readonly Dictionary<int, float3> m_Baseline = new Dictionary<int, float3>();

        /// <summary>Swaps seen.</summary>
        public int Swaps { get; private set; }
        /// <summary>Largest centre shift of any swap, metres.</summary>
        public float MaxShiftM { get; private set; }
        /// <summary>Swaps that moved the centre by <see cref="MaxCentreShiftM"/> or more.</summary>
        public int Violations { get; private set; }

        /// <summary>Every swap so far moved its box by less than the limit.</summary>
        public bool AllWithin { get { return Violations == 0; } }

        /// <summary>The centre of the object's box in the room frame: base point plus half the height.</summary>
        public static float3 BoxCentre(RoomObject data, float3 basePosition)
        {
            return basePosition + new float3(0f, 0.5f * data.Size.y, 0f);
        }

        /// <summary>Records every box's centre as the RoomModel has it. Call when the content is built.</summary>
        public void Baseline(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            m_Baseline.Clear();
            Swaps = 0;
            MaxShiftM = 0f;
            Violations = 0;
            for (int k = 0; k < room.Objects.Length; k++)
                m_Baseline[room.Objects[k].Id] = BoxCentre(room.Objects[k], room.Objects[k].Base);
        }

        /// <summary>
        /// One object's view changed to a mesh. viewBase is where the view now stands (its transform's position:
        /// the centre of the box's base). Returns the shift of the box centre in metres, or NaN for an object the
        /// baseline does not know.
        /// </summary>
        public float Observe(RoomObject data, float3 viewBase)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            float3 was;
            if (!m_Baseline.TryGetValue(data.Id, out was)) return float.NaN;

            float shift = math.distance(was, BoxCentre(data, viewBase));
            Swaps++;
            // Written so that a NaN position counts as a violation.
            if (!(shift < MaxCentreShiftM)) Violations++;
            if (shift > MaxShiftM || float.IsNaN(shift)) MaxShiftM = shift;
            return shift;
        }
    }

    /// <summary>Feeds a <see cref="SwapMonitor"/> from a room content's events.</summary>
    public sealed class SwapWatcher : IDisposable
    {
        readonly IRoomContent m_Content;
        readonly SwapMonitor m_Monitor;

        public SwapWatcher(IRoomContent content, SwapMonitor monitor)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (monitor == null) throw new ArgumentNullException(nameof(monitor));
            m_Content = content;
            m_Monitor = monitor;
            content.Built += OnBuilt;
            content.ObjectChanged += OnObjectChanged;
            if (content.Room != null) monitor.Baseline(content.Room);
        }

        void OnBuilt(RoomModel room)
        {
            m_Monitor.Baseline(room);
        }

        void OnObjectChanged(int objectId)
        {
            IRoomObjectView view;
            if (!m_Content.TryGetObject(objectId, out view) || view == null) return;
            // A change of state that draws no mesh moves nothing: only a mesh is a swap.
            if (view.Shown != MeshState.Ready && view.Shown != MeshState.BuiltIn && view.Shown != MeshState.StandIn) return;
            UnityEngine.Vector3 p = view.Transform.position;
            m_Monitor.Observe(view.Data, new float3(p.x, p.y, p.z));
        }

        public void Dispose()
        {
            m_Content.Built -= OnBuilt;
            m_Content.ObjectChanged -= OnObjectChanged;
        }
    }
}
