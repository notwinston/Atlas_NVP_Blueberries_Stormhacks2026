using System;

namespace Atlas.Rendering
{
    /// <summary>
    /// The one allocation table of the stencil portals (Master Plan D16, lane A contract section 12.2):
    /// which layer, renderer feature and render pass event belong to each stencil reference. The project
    /// set-up creates the features from it and the portal renderer finds them by it. Engine-free.
    /// </summary>
    public static class PortalStencilTable
    {
        /// <summary>Stencil references a frame may use: 1 .. 15. Reference 0 is the room the walker is in.</summary>
        public const int MaxSlots = 15;

        /// <summary>Layer "Default": the room the walker is in, and every space without portals.</summary>
        public const int WorldLayer = 0;
        /// <summary>Layer "AtlasPortalMask": the doorway masks.</summary>
        public const int MaskLayer = 6;
        /// <summary>Layer "AtlasHud": head-locked content.</summary>
        public const int HudLayer = 7;
        /// <summary>Layer "AtlasRoom01"; reference n is on layer FirstRoomLayer + n - 1.</summary>
        public const int FirstRoomLayer = 8;

        public const string MaskLayerName = "AtlasPortalMask";
        public const string HudLayerName = "AtlasHud";

        public const string MaskFeature = "Atlas Portal Masks";
        public const string HudFeature = "Atlas HUD";
        public const string HudTextFeature = "Atlas HUD Text";

        /// <summary>RenderPassEvent.AfterRenderingOpaques: the masks follow the walker's own room.</summary>
        public const int MaskEvent = 300;
        /// <summary>RenderPassEvent.AfterRenderingTransparents: the head-locked layer, opaque queue.</summary>
        public const int HudEvent = 500;
        /// <summary>The head-locked layer, transparent queue (text).</summary>
        public const int HudTextEvent = 501;

        /// <summary>Render queue of a mask that marks a doorway, and of the one that then resets its depth.</summary>
        public const int MarkQueue = 2000, ResetQueue = 2001;

        static readonly string[] s_LayerNames = BuildNames("AtlasRoom", "00");
        static readonly string[] s_FeatureNames = BuildNames("Atlas Room ", "00");

        /// <summary>True for 1 .. 15.</summary>
        public static bool IsReference(int reference)
        {
            return reference >= 1 && reference <= MaxSlots;
        }

        /// <summary>Layer of the room seen through the doorway that holds this reference: 8 .. 22.</summary>
        public static int Layer(int reference)
        {
            Check(reference);
            return FirstRoomLayer + reference - 1;
        }

        /// <summary>"AtlasRoom01" .. "AtlasRoom15".</summary>
        public static string LayerName(int reference)
        {
            Check(reference);
            return s_LayerNames[reference - 1];
        }

        /// <summary>"Atlas Room 01" .. "Atlas Room 15": the Render Objects feature that draws the layer.</summary>
        public static string FeatureName(int reference)
        {
            Check(reference);
            return s_FeatureNames[reference - 1];
        }

        /// <summary>Render pass event of the room feature: 301 .. 315, so rooms follow the masks in order.</summary>
        public static int PassEvent(int reference)
        {
            Check(reference);
            return MaskEvent + reference;
        }

        /// <summary>The stencil reference a layer is drawn with, or 0 for a layer that is no room layer.</summary>
        public static int ReferenceOfLayer(int layer)
        {
            int reference = layer - FirstRoomLayer + 1;
            return IsReference(reference) ? reference : 0;
        }

        /// <summary>Bit mask of the layers that a renderer feature draws: they must be out of the renderer's own
        /// opaque and transparent masks, or they are drawn twice.</summary>
        public static int FeatureLayerMask
        {
            get
            {
                int mask = (1 << MaskLayer) | (1 << HudLayer);
                for (int reference = 1; reference <= MaxSlots; reference++) mask |= 1 << Layer(reference);
                return mask;
            }
        }

        static void Check(int reference)
        {
            if (!IsReference(reference))
                throw new ArgumentOutOfRangeException(nameof(reference), "Stencil references of rooms are 1 to 15.");
        }

        static string[] BuildNames(string prefix, string format)
        {
            var names = new string[MaxSlots];
            for (int i = 0; i < MaxSlots; i++)
                names[i] = prefix + (i + 1).ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            return names;
        }
    }

    /// <summary>
    /// Gives out the stencil references of one frame: rooms behind visible doorways first, the largest
    /// doorway first, then rooms that are kept on although no doorway shows them. Never more than 15.
    /// Engine-free, fixed storage: a frame allocates nothing.
    /// </summary>
    public sealed class PortalSlotAllocator
    {
        readonly int[] m_RoomOfReference = new int[PortalStencilTable.MaxSlots + 1];
        int m_Count;

        /// <summary>References given out this frame: 0 .. 15.</summary>
        public int Count { get { return m_Count; } }

        /// <summary>Starts a frame: no reference is in use.</summary>
        public void Begin()
        {
            m_Count = 0;
        }

        /// <summary>
        /// The reference of a room this frame. A room asked for twice keeps its reference, so two doorways
        /// into one room share it. Returns 0 when all 15 are taken.
        /// </summary>
        public int Assign(int room)
        {
            for (int reference = 1; reference <= m_Count; reference++)
                if (m_RoomOfReference[reference] == room) return reference;
            if (m_Count >= PortalStencilTable.MaxSlots) return 0;
            m_Count++;
            m_RoomOfReference[m_Count] = room;
            return m_Count;
        }

        /// <summary>The reference a room holds this frame, or 0.</summary>
        public int ReferenceOf(int room)
        {
            for (int reference = 1; reference <= m_Count; reference++)
                if (m_RoomOfReference[reference] == room) return reference;
            return 0;
        }

        /// <summary>The room that holds a reference this frame, or -1.</summary>
        public int RoomOf(int reference)
        {
            return reference >= 1 && reference <= m_Count ? m_RoomOfReference[reference] : -1;
        }
    }
}
