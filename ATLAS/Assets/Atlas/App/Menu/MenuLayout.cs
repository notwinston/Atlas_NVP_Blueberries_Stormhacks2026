using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App.Menu
{
    /// <summary>
    /// Where the menu stands. The gaze ray is the head's forward axis (lane C contract, section 13.2), so a
    /// panel that turned with the head could never be looked at off its centre. The menu therefore follows the
    /// head's position and keeps its own heading: level, and turned about the vertical only. While the menu is
    /// closed the heading trails the walker's, so the corner chip stays down and to the left of where they
    /// look; it holds still while they look down, which is the start of reaching for the chip. While the menu
    /// is open the heading is fixed, and it is brought round only when the walker has turned away from it.
    /// Angles in radians. Engine-free.
    /// </summary>
    public struct MenuFrame
    {
        /// <summary>How fast the closed menu's heading catches up with the walker's.</summary>
        public static readonly float FollowRate = math.radians(90f);
        /// <summary>Below this pitch the heading holds still.</summary>
        public static readonly float HoldPitch = math.radians(-8f);
        /// <summary>The closed menu's heading never trails by more than this.</summary>
        public static readonly float ChipLeash = math.radians(40f);
        /// <summary>The open panel is brought in front of the walker again when they have turned this far from it.</summary>
        public static readonly float PanelLeash = math.radians(75f);

        /// <summary>The heading: 0 looks along +Z, positive turns towards +X.</summary>
        public float Yaw;

        public quaternion Rotation { get { return quaternion.RotateY(Yaw); } }

        /// <summary>
        /// Heading and pitch of a head rotation. False when the head looks almost straight up or down, where
        /// the heading means nothing; the pitch is valid either way.
        /// </summary>
        public static bool TryYawPitch(quaternion head, out float yaw, out float pitch)
        {
            float3 forward = math.mul(head, new float3(0f, 0f, 1f));
            pitch = math.asin(math.clamp(forward.y, -1f, 1f));
            yaw = 0f;
            if (forward.x * forward.x + forward.z * forward.z < 0.01f) return false;
            yaw = math.atan2(forward.x, forward.z);
            return true;
        }

        /// <summary>The signed turn from one heading to another, in -pi to pi.</summary>
        public static float Delta(float from, float to)
        {
            float d = (to - from) % (2f * math.PI);
            if (d > math.PI) d -= 2f * math.PI;
            if (d < -math.PI) d += 2f * math.PI;
            return d;
        }

        public void Snap(float headYaw)
        {
            Yaw = headYaw;
        }

        /// <summary>
        /// The menu opens. A heading the closed menu already trails with is kept: the walker opened it by
        /// looking at the chip, down and to the left, and snapping to that would move the panel left on every
        /// opening. With no heading yet, or one the walker has turned away from, it snaps to theirs.
        /// </summary>
        public void Open(float headYaw, bool hasHeading)
        {
            if (!hasHeading || math.abs(Delta(Yaw, headYaw)) > PanelLeash) Yaw = headYaw;
        }

        /// <summary>
        /// The closed menu: the heading catches up with the walker's unless they look down or
        /// <paramref name="hold"/> is set (the gaze is on the chip), and never trails by more than the leash.
        /// </summary>
        public void Follow(float headYaw, float pitch, float dt, bool hold)
        {
            float d = Delta(Yaw, headYaw);
            if (!hold && pitch > HoldPitch)
            {
                float step = FollowRate * dt;
                Yaw += math.clamp(d, -step, step);
                d = Delta(Yaw, headYaw);
            }
            if (d > ChipLeash) Yaw = headYaw - ChipLeash;
            else if (d < -ChipLeash) Yaw = headYaw + ChipLeash;
        }

        /// <summary>The open menu: true when the walker had turned away and the heading was brought round to theirs.</summary>
        public bool Recentre(float headYaw)
        {
            if (math.abs(Delta(Yaw, headYaw)) <= PanelLeash) return false;
            Yaw = headYaw;
            return true;
        }
    }

    /// <summary>
    /// The measures of the panel (lane D contract, section 5.7: 1.2 m ahead, 0.7 by 0.5 m, eight rows) and of
    /// its gaze targets, in the panel's own frame: x right, y up, the panel in the plane z = 0, metres.
    /// </summary>
    public static class MenuLayout
    {
        /// <summary>Centre of the panel in the menu frame.</summary>
        public static readonly float3 PanelCentre = new float3(0f, 0f, 1.2f);
        public const float Width = 0.70f, Height = 0.50f;

        public const float RowPitch = 0.042f;
        /// <summary>Centre of the first row.</summary>
        public const float FirstRowY = 0.150f;
        public const float RowHalfWidth = 0.33f, RowHalfHeight = 0.020f;
        public const float NamesX = -0.325f, ValuesX = 0.190f;
        public const float TitleY = 0.212f;

        /// <summary>The "-" and "+" of the focused row.</summary>
        public const float MinusX = 0.235f, PlusX = 0.300f;
        public const float StepHalfWidth = 0.028f, StepHalfHeight = 0.018f;
        /// <summary>The step targets stand this far in front of the row, so the gaze meets them first.</summary>
        public const float StepLift = 0.01f;

        /// <summary>The two chips under the list.</summary>
        public const float FooterY = -0.205f;
        public const float CloseX = -0.13f, ApplyX = 0.13f;
        public const float ChipHalfWidth = 0.10f, ChipHalfHeight = 0.024f;

        /// <summary>The chip that opens the menu, in the menu frame (lane D contract, section 5.7).</summary>
        public static readonly float3 OpenChip = new float3(-0.55f, -0.40f, 1.0f);
        public const float OpenChipRadius = 0.06f;

        // Gaze targets while open: one per visible row, then these.
        public const int Minus = MenuModel.VisibleRows, Plus = Minus + 1, Apply = Minus + 2, Close = Minus + 3;
        public const int TargetCount = Minus + 4;

        public static float RowY(int slot)
        {
            return FirstRowY - slot * RowPitch;
        }

        /// <summary>Centre of a target on the panel; the step targets sit on the focused row's slot.</summary>
        public static float3 TargetCentre(int target, int focusedSlot)
        {
            if (target < Minus) return new float3(0f, RowY(target), 0f);
            switch (target)
            {
                case Minus: return new float3(MinusX, RowY(focusedSlot), -StepLift);
                case Plus: return new float3(PlusX, RowY(focusedSlot), -StepLift);
                case Apply: return new float3(ApplyX, FooterY, 0f);
                default: return new float3(CloseX, FooterY, 0f);
            }
        }

        public static float2 TargetHalfSize(int target)
        {
            if (target < Minus) return new float2(RowHalfWidth, RowHalfHeight);
            if (target == Minus || target == Plus) return new float2(StepHalfWidth, StepHalfHeight);
            return new float2(ChipHalfWidth, ChipHalfHeight);
        }

        // ---- within reach of the hands (documents/ATLAS_Hands_Design.md section 6.1) -----------------------
        // In a hands session the panel and the chip stand at these places about the near anchor, in the anchor's
        // frame, and everything above is multiplied by the layout's scale: at 0.75 the panel is 0.525 by 0.375 m
        // and a row 3 cm tall on a 3.15 cm pitch.

        /// <summary>The open panel's place within reach: the anchor itself.</summary>
        public const ReachSlot PanelSlot = ReachSlot.MenuPanel;
        /// <summary>The place within reach of the chip that opens the menu.</summary>
        public const ReachSlot ChipSlot = ReachSlot.MenuChip;

        /// <summary>
        /// Half size of a target within reach: the usual one times the scale, and nothing under 3 cm across, so
        /// the "-" and "+" of the focused row, 2.7 cm tall at the usual scale, are as tall as the row.
        /// </summary>
        public static float2 NearTargetHalfSize(int target, float scale)
        {
            float2 half = TargetHalfSize(target);
            return new float2(ReachSlots.TargetHalf(half.x, scale), ReachSlots.TargetHalf(half.y, scale));
        }
    }
}
