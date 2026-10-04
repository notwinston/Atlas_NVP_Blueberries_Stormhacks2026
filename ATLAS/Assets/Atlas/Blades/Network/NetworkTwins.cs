using System;
using Atlas.Blades.Network.Core;
using Atlas.Input;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// The twins of the floor map (contract section 6.5, Master Plan rule 5): every action by gamepad and by gaze.
    /// Re-centre: stick cursor and Confirm, or a dwell on a node. Send packet: Primary, or a dwell on the packet chip.
    /// Switch rule: Secondary, or a dwell on the rule chip. Both twins end in the same action, so one code path serves both.
    /// Engine-free apart from the input interface; nothing allocates per frame (the targets are registered when the
    /// count changes and moved when the map moved).
    /// </summary>
    public sealed class NetworkTwins : IDisposable
    {
        /// <summary>The two chips always fit: they are registered before the nodes (section 6.5).</summary>
        public const int ChipSlots = 2;
        public const float NodeTargetRadius = 0.06f;
        public const float NodeTargetHeight = 0.1f;
        public const float StickPress = 0.6f, StickRelease = 0.3f;
        /// <summary>The cursor moves to a node within this cosine of the stick direction (60 degrees).</summary>
        public const float CursorCone = 0.5f;
        // Chip layout: the signs of the exhibit menu (ExhibitSignLayout: Height, HalfWidth/HalfHeight, StepDegrees, arc radius
        // 0.8 of the free rectangle's half short side, clamped 0.5 to 1.5 m) and the caption chip of the Playground
        // (CardLayout.ChipHalfWidth/ChipHalfHeight/ChipLineHeight), mirrored here because this assembly cannot reference
        // Playground. The chips stand on an arc round the disc centre, centred on +Z, and face it, so a visitor standing
        // at the centre looks at them (a gaze ray never lies in a chip's plane).
        public const int PacketChipIndex = 0, RuleChipIndex = 1, CaptionChipIndex = 2;
        public const float ChipHeight = 1.5f, ChipStepDegrees = 28f;
        public const float ChipRadiusFactor = 0.8f, ChipRadiusMin = 0.5f, ChipRadiusMax = 1.5f;
        public static readonly float2 ChipHalfSize = new float2(0.22f, 0.07f);
        public static readonly float2 CaptionHalfSize = new float2(0.42f, 0.035f);
        public const float ChipLineHeight = 0.034f;

        /// <summary>Radius of the chip arc for a disc scale (1 = the nominal 1.2 m disc).</summary>
        public static float ChipArcRadius(float discScale)
        {
            return math.clamp(ChipRadiusFactor * NetworkFloorMesher.DiscRadiusMetres * discScale, ChipRadiusMin, ChipRadiusMax);
        }

        /// <summary>Centre of a chip in the room frame: packet and rule at the two middle slots of the arc, caption above the middle.</summary>
        public static float3 ChipCentre(int chip, float discScale)
        {
            float r = ChipArcRadius(discScale);
            float step = chip == PacketChipIndex ? -0.5f : chip == RuleChipIndex ? 0.5f : 0f;
            float angle = math.radians(step * ChipStepDegrees);
            float y = chip == CaptionChipIndex ? ChipHeight + 2.5f * ChipHalfSize.y : ChipHeight;
            return new float3(r * math.sin(angle), y, r * math.cos(angle));
        }

        /// <summary>The chip's front (-Z of its own frame) looks at the disc centre.</summary>
        public static Quaternion ChipRotation(int chip)
        {
            float angle = math.radians((chip == PacketChipIndex ? -0.5f : chip == RuleChipIndex ? 0.5f : 0f) * ChipStepDegrees);
            quaternion q = quaternion.LookRotationSafe(new float3(math.sin(angle), 0f, math.cos(angle)), new float3(0f, 1f, 0f));
            return new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        }

        readonly IAtlasInput m_Input;
        readonly NetworkModel m_Model;
        readonly NetworkFocus m_Focus;
        readonly NetworkFloorMesher m_Mesher;
        readonly GreedyRouter m_Router;
        readonly NetworkPacket m_Packet;
        readonly NetworkParams m_Params;

        readonly GazeTargetId[] m_NodeIds = new GazeTargetId[GazeRegistry.Capacity];
        readonly int[] m_SlotNode = new int[GazeRegistry.Capacity];
        readonly float[] m_SlotDist = new float[GazeRegistry.Capacity];
        GazeTargetId m_PacketChip, m_RuleChip;
        int m_NodeCount, m_Wanted = -1;
        double2 m_SyncedCentre;
        bool m_Dirty = true, m_Registered, m_ChipsMoved, m_StickArmed = true, m_Disposed;
        uint m_Rng = 0x9E3779B9u;

        public NetworkTwins(IAtlasInput input, NetworkModel model, NetworkFocus focus, NetworkFloorMesher mesher, NetworkParams parameters = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (focus == null) throw new ArgumentNullException(nameof(focus));
            if (mesher == null) throw new ArgumentNullException(nameof(mesher));
            m_Input = input; m_Model = model; m_Focus = focus; m_Mesher = mesher; m_Params = parameters;
            m_Router = new GreedyRouter(model);
            m_Packet = new NetworkPacket(m_Router, new NetworkCaption());
            // The cursor starts on the node nearest the file's centre: the lowest radius.
            int best = 0;
            for (int i = 1; i < model.NodeCount; i++) if (model.Radius(i) < model.Radius(best)) best = i;
            Cursor = best;
        }

        /// <summary>The plain fields the view carries (net.rule, net.maxGazeNodes) and the room scale of the disc.</summary>
        public RoutingRule Rule = RoutingRule.Hyperbolic;
        public int MaxGazeNodes = 64;
        public bool Visible = true;
        public float ScaleX = 1f, ScaleZ = 1f;

        /// <summary>The node the gamepad cursor is on; A re-centres on it and a packet is sent to it.</summary>
        public int Cursor { get; private set; }
        public NetworkPacket Packet { get { return m_Packet; } }
        public NetworkCaption Caption { get { return m_Packet.Caption; } }
        public int NodeTargetCount { get { return m_NodeCount; } }
        /// <summary>The node a node target stands on.</summary>
        public int TargetNode(int slot) { return m_SlotNode[slot]; }
        public GazeTargetId PacketChip { get { return m_PacketChip; } }
        public GazeTargetId RuleChip { get { return m_RuleChip; } }
        public int Recentres { get; private set; }

        /// <summary>One frame with the view's fields: reads them in, runs <see cref="Update(float)"/>, writes the rule back.</summary>
        public void Update(float dt, NetworkFloorView view)
        {
            if (view == null) { Update(dt); return; }
            if (m_Params != null) m_Params.Apply(view);
            Rule = view.Rule;
            MaxGazeNodes = view.MaxGazeNodes;
            Visible = view.Visible;
            Vector3 s = view.transform.localScale;
            if (s.x != ScaleX || s.z != ScaleZ) { ScaleX = s.x; ScaleZ = s.z; m_Dirty = true; m_ChipsMoved = true; }
            Update(dt);
            view.Rule = Rule;
        }

        /// <summary>One frame, after the input step: acts on the frame's actions, keeps the targets on the map, runs the packet.</summary>
        public void Update(float dt)
        {
            if (m_Disposed) return;
            // Hidden: every slot goes back to the shared registry (capacity 64 for the whole app), not just disabled.
            if (!Visible) { if (m_Registered) Unregister(); return; }
            m_Registered = true;
            RegisterChips();

            int want = math.clamp(MaxGazeNodes, 0, GazeRegistry.Capacity);
            if (want != m_Wanted) Arrange(want);

            // The same action by either twin.
            if (m_Input.Pressed(AtlasAction.Confirm))
            {
                int node = Cursor;
                if (m_Input.LastSource(AtlasAction.Confirm) == InputSource.Gaze)
                {
                    int slot = m_Input.LastTag(AtlasAction.Confirm);
                    if (slot >= 0 && slot < m_NodeCount) { node = m_SlotNode[slot]; Cursor = node; }
                }
                Recentre(node);
            }
            if (m_Input.Pressed(AtlasAction.Primary)) SendPacket();
            if (m_Input.Pressed(AtlasAction.Secondary)) SwitchRule();
            MoveCursor();

            if (m_Focus.Centre.x != m_SyncedCentre.x || m_Focus.Centre.y != m_SyncedCentre.y) m_Dirty = true;
            if (m_ChipsMoved) PlaceChips();
            if (m_Dirty) PlaceTargets();

            if (m_Packet.Advance(dt)) m_Mesher.SetHighlight(m_Packet.Path, m_Packet.ShownCount);
        }

        void RegisterChips()
        {
            if (!m_PacketChip.IsValid)
                m_PacketChip = m_Input.RegisterGazeTarget(Chip(AtlasAction.Primary, PacketChipIndex, ScaleX, "packet chip"));
            if (!m_RuleChip.IsValid)
                m_RuleChip = m_Input.RegisterGazeTarget(Chip(AtlasAction.Secondary, RuleChipIndex, ScaleX, "rule chip"));
        }

        static GazeTarget Chip(AtlasAction action, int chip, float scale, string label)
        {
            float3 centre = ChipCentre(chip, scale);
            GazeTarget t = GazeTarget.WorldRect(action, new Vector3(centre.x, centre.y, centre.z), ChipRotation(chip),
                new Vector2(ChipHalfSize.x, ChipHalfSize.y));
            t.Label = label;
            return t;
        }

        /// <summary>Starts the glide to a node and logs net.recentre. False when it is out of range or already the centre.</summary>
        public bool Recentre(int node)
        {
            if (!m_Focus.Begin(node)) return false;
            Recentres++;
            NetworkEvents.Recentre(node, m_Model.Radius(node), m_Model.Degree(node));
            return true;
        }

        /// <summary>
        /// Sends a packet from the centred node (or the node the map started on) to the cursor; when they are the same node,
        /// to a pseudo-random node of the giant component. The route lights hop by hop and net.packet is logged.
        /// </summary>
        public bool SendPacket()
        {
            int src = m_Focus.FocusNode >= 0 ? m_Focus.FocusNode : StartNode();
            int dst = Cursor;
            if (dst == src && m_Router.GiantCount > 1)
            {
                do
                {
                    m_Rng ^= m_Rng << 13; m_Rng ^= m_Rng >> 17; m_Rng ^= m_Rng << 5;
                    dst = m_Router.GiantNode((int)(m_Rng % (uint)m_Router.GiantCount));
                } while (dst == src);
            }
            if (!m_Packet.Start(src, dst, Rule)) return false;
            m_Mesher.SetHighlight(m_Packet.Path, 1);
            return true;
        }

        /// <summary>Flips net.rule between the hyperbolic and the flat-picture rule.</summary>
        public void SwitchRule()
        {
            Rule = Rule == RoutingRule.Hyperbolic ? RoutingRule.Flat : RoutingRule.Hyperbolic;
            if (m_Params != null) m_Params.SetRule(Rule);
        }

        int StartNode()
        {
            int best = 0;
            for (int i = 1; i < m_Model.NodeCount; i++) if (m_Model.Radius(i) < m_Model.Radius(best)) best = i;
            return best;
        }

        // Stick: one step per push, then it must come back to rest.
        void MoveCursor()
        {
            float x = m_Input.Axis(AtlasAxis.LeftX), y = m_Input.Axis(AtlasAxis.LeftY);
            float len = math.sqrt(x * x + y * y);
            if (len < StickRelease) { m_StickArmed = true; return; }
            if (!m_StickArmed || len < StickPress) return;
            m_StickArmed = false;
            // The disc picture: x east, y along +Z.
            float2 dir = new float2(x, y) / len;
            float3 from = NodeRoomPosition(Cursor);
            int best = -1;
            float bestD = float.MaxValue;
            for (int k = 0; k < m_NodeCount; k++)
            {
                int n = m_SlotNode[k];
                if (n == Cursor) continue;
                float3 p = NodeRoomPosition(n);
                float2 d = new float2(p.x - from.x, p.z - from.z);
                float dl = math.length(d);
                if (dl < 1e-6f || math.dot(d / dl, dir) < CursorCone) continue;
                if (dl < bestD) { bestD = dl; best = n; }
            }
            if (best >= 0) Cursor = best;
        }

        float3 NodeRoomPosition(int node)
        {
            float3 p = m_Mesher.NodePosition(node);
            return new float3(p.x * ScaleX, 0f, p.z * ScaleZ);
        }

        void PlaceChips()
        {
            m_ChipsMoved = false;
            float3 p = ChipCentre(PacketChipIndex, ScaleX), r = ChipCentre(RuleChipIndex, ScaleX);
            if (m_PacketChip.IsValid) m_Input.MoveGazeTarget(m_PacketChip, new Vector3(p.x, p.y, p.z));
            if (m_RuleChip.IsValid) m_Input.MoveGazeTarget(m_RuleChip, new Vector3(r.x, r.y, r.z));
        }

        // Targets are the nodes nearest the centre of the map after projection, nearest first.
        void PlaceTargets()
        {
            m_Dirty = false;
            m_SyncedCentre = m_Focus.Centre;
            int k = m_NodeCount;
            if (k == 0) return;
            int filled = 0;
            for (int i = 0; i < m_Model.NodeCount; i++)
            {
                float3 p = m_Mesher.NodePosition(i);
                float d = p.x * p.x + p.z * p.z;
                if (filled == k && d >= m_SlotDist[k - 1]) continue;
                int at = filled < k ? filled++ : k - 1;
                while (at > 0 && m_SlotDist[at - 1] > d) { m_SlotDist[at] = m_SlotDist[at - 1]; m_SlotNode[at] = m_SlotNode[at - 1]; at--; }
                m_SlotDist[at] = d; m_SlotNode[at] = i;
            }
            for (int s = 0; s < k; s++)
            {
                float3 p = NodeRoomPosition(m_SlotNode[s]);
                m_Input.MoveGazeTarget(m_NodeIds[s], new Vector3(p.x, NodeTargetHeight, p.z));
            }
        }

        // The node targets: at most net.maxGazeNodes, and at most what is free with the two chips already holding theirs.
        void Arrange(int want)
        {
            for (int i = 0; i < m_NodeCount; i++) { m_Input.UnregisterGazeTarget(m_NodeIds[i]); m_NodeIds[i] = default; }
            m_NodeCount = 0;
            m_Wanted = want;
            int cap = math.min(want, m_Model.NodeCount);
            for (int s = 0; s < cap; s++)
            {
                GazeTarget t = GazeTarget.World(AtlasAction.Confirm, Vector3.zero, NodeTargetRadius, s);
                t.Label = "network node";
                GazeTargetId id = m_Input.RegisterGazeTarget(t);
                if (!id.IsValid) break;
                m_NodeIds[m_NodeCount++] = id;
            }
            m_Dirty = true;
        }

        // Gives every slot back: the nodes and the two chips.
        void Unregister()
        {
            for (int i = 0; i < m_NodeCount; i++) { m_Input.UnregisterGazeTarget(m_NodeIds[i]); m_NodeIds[i] = default; }
            m_NodeCount = 0;
            m_Wanted = -1;
            if (m_PacketChip.IsValid) m_Input.UnregisterGazeTarget(m_PacketChip);
            if (m_RuleChip.IsValid) m_Input.UnregisterGazeTarget(m_RuleChip);
            m_PacketChip = default; m_RuleChip = default;
            m_Registered = false;
            m_Dirty = true;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Unregister();
        }
    }

    /// <summary>
    /// Joins the twins to the installed floor view and the frame loop. Like <see cref="NetworkInstaller"/> it waits, with a
    /// light poller, for the services the composition root publishes; no file of NET-06 is edited.
    /// </summary>
    public static class NetworkTwinsInstaller
    {
        static NetworkTwins s_Twins;
        static NetworkParams s_Params;

        public static NetworkTwins Twins { get { return s_Twins; } }

        public static NetworkTwins Install(NetworkFloorView view, IAtlasInput input, IFrameLoop loop, ParamRegistry registry, Atlas.Rendering.MaterialPool pool = null)
        {
            if (s_Twins != null) return s_Twins;
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            s_Params = registry != null ? new NetworkParams(registry) : null;
            s_Twins = new NetworkTwins(input, view.Mesher.Model, view.Focus, view.Mesher, s_Params);
            NetworkChipView chips = pool != null ? new NetworkChipView(s_Twins, view.Mesher, pool) : null;
            loop.AddUpload(new Step(s_Twins, view, chips));
            return s_Twins;
        }

        sealed class Step : IUploadStep
        {
            readonly NetworkTwins m_Twins;
            readonly NetworkFloorView m_View;
            readonly NetworkChipView m_Chips;
            public Step(NetworkTwins twins, NetworkFloorView view, NetworkChipView chips) { m_Twins = twins; m_View = view; m_Chips = chips; }
            public void Upload(in FrameContext frame)
            {
                m_Twins.Update(frame.DeltaTime, m_View);
                if (m_Chips != null) m_Chips.Update();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (s_Twins != null) return;
            var go = new GameObject("Network Twins Installer") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Poller>();
        }

        sealed class Poller : MonoBehaviour
        {
            void Update()
            {
                if (s_Twins != null) { Destroy(gameObject); return; }
                if (!AtlasServices.BootCompleted) return;
                NetworkFloorView view = NetworkInstaller.View;
                if (view == null) return;
                if (!AtlasServices.TryGet(out IAtlasInput input) || !AtlasServices.TryGet(out IFrameLoop loop)) return;
                AtlasServices.TryGet(out ParamRegistry registry);
                AtlasServices.TryGet(out Atlas.Rendering.MaterialPool pool);
                Install(view, input, loop, registry, pool);
                Destroy(gameObject);
            }
        }
    }
}
