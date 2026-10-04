using System.Collections.Generic;

namespace Atlas.Input
{
    /// <summary>How an action is reached by gaze alone.</summary>
    public enum GazeRoute : byte
    {
        None = 0,
        /// <summary>A registered target that carries the action: a chip, a sign, a beacon.</summary>
        Target = 1,
        /// <summary>A registered target, or looking down past a pitch (GazeTarget.LookDown).</summary>
        TargetOrLookDown = 2,
        /// <summary>Menu rows: dwelling on a row selects it, which is what stepping to it does.</summary>
        RowTarget = 3,
        /// <summary>Two Hold targets, one for each end of the axis.</summary>
        HoldTargets = 4
    }

    /// <summary>One row of the twin table: the gamepad binding and the gaze-dwell route of an action.</summary>
    public readonly struct ActionTwin
    {
        public readonly AtlasAction Action;
        /// <summary>Input System binding path of the gamepad control, for example "&lt;Gamepad&gt;/buttonSouth".</summary>
        public readonly string GamepadPath;
        /// <summary>A second gamepad control for the same action (menu navigation on the left stick), or null.</summary>
        public readonly string GamepadAltPath;
        /// <summary>What turns the control into this action when it is shared, or null.</summary>
        public readonly string GamepadRule;
        /// <summary>Editor keys (README F9): binding paths, the second may be null.</summary>
        public readonly string KeyPath;
        public readonly string KeyAltPath;
        public readonly GazeRoute Gaze;
        /// <summary>An action whose targets also serve as this action's gaze twin (NavUp and NavDown share the
        /// menu rows), or None.</summary>
        public readonly AtlasAction GazeAlias;
        /// <summary>Who registers the gaze target, and what it is.</summary>
        public readonly string GazeTarget;
        /// <summary>The headset lever fires it with nothing under the gaze.</summary>
        public readonly bool Lever;
        public readonly bool InSpace;
        public readonly bool InMenu;

        public ActionTwin(AtlasAction action, string gamepadPath, string gamepadAltPath, string gamepadRule,
                          string keyPath, string keyAltPath, GazeRoute gaze, AtlasAction gazeAlias, string gazeTarget,
                          bool lever, bool inSpace, bool inMenu)
        {
            Action = action;
            GamepadPath = gamepadPath;
            GamepadAltPath = gamepadAltPath;
            GamepadRule = gamepadRule;
            KeyPath = keyPath;
            KeyAltPath = keyAltPath;
            Gaze = gaze;
            GazeAlias = gazeAlias;
            GazeTarget = gazeTarget;
            Lever = lever;
            InSpace = inSpace;
            InMenu = inMenu;
        }

        public bool HasGamepadRoute => !string.IsNullOrEmpty(GamepadPath);
        public bool HasGazeRoute => Gaze != GazeRoute.None && !string.IsNullOrEmpty(GazeTarget);
    }

    /// <summary>One row of the twin table for a held axis.</summary>
    public readonly struct AxisTwin
    {
        public readonly AtlasAxis Axis;
        /// <summary>Gamepad control that pushes the axis towards +1, and the one towards -1. For a stick both
        /// name the stick's two directions.</summary>
        public readonly string GamepadPositivePath;
        public readonly string GamepadNegativePath;
        /// <summary>Editor keys, or null.</summary>
        public readonly string KeyPositivePath;
        public readonly string KeyNegativePath;
        public readonly GazeRoute Gaze;
        public readonly string GazeTarget;

        public AxisTwin(AtlasAxis axis, string gamepadPositivePath, string gamepadNegativePath,
                        string keyPositivePath, string keyNegativePath, GazeRoute gaze, string gazeTarget)
        {
            Axis = axis;
            GamepadPositivePath = gamepadPositivePath;
            GamepadNegativePath = gamepadNegativePath;
            KeyPositivePath = keyPositivePath;
            KeyNegativePath = keyNegativePath;
            Gaze = gaze;
            GazeTarget = gazeTarget;
        }

        public bool HasGamepadRoute => !string.IsNullOrEmpty(GamepadPositivePath) && !string.IsNullOrEmpty(GamepadNegativePath);
        public bool HasGazeRoute => Gaze != GazeRoute.None && !string.IsNullOrEmpty(GazeTarget);
    }

    /// <summary>
    /// The twin rule made checkable (Master Plan section 0.5 rule 5): every action and every axis has a gamepad
    /// binding and a gaze-dwell route. The action map is built from this table, so the table is what runs.
    /// Bindings follow lane C contract section 13.1 and Master Plan section 10.1.3.
    /// </summary>
    public static class InputTwins
    {
        /// <summary>Number of actions, None excluded. Their values run 1 .. ActionCount.</summary>
        public const int ActionCount = 17;
        public const int AxisCount = 6;

        /// <summary>The gamepad control held to turn "start" into ParamMenu (Master Plan section 7.3.2).</summary>
        public const string ChordModifierPath = "<Gamepad>/select";
        /// <summary>The headset lever: a touch that begins on the screen. A mouse click stands in for it in the Editor.</summary>
        public const string LeverPath = "<Touchscreen>/primaryTouch/press";
        public const string LeverEditorPath = "<Mouse>/leftButton";

        /// <summary>buttonEast released within this time is Back.</summary>
        public const float BackTapSeconds = 0.5f;
        /// <summary>buttonEast held this long is ReturnHome.</summary>
        public const float ReturnHomeHoldSeconds = 1.0f;
        /// <summary>Menu navigation repeats after this delay, then at the interval below.</summary>
        public const float NavRepeatDelaySeconds = 0.4f;
        public const float NavRepeatIntervalSeconds = 0.15f;

        private static readonly ActionTwin[] s_actions = BuildActions();
        private static readonly AxisTwin[] s_axes = BuildAxes();
        private static readonly uint s_spaceMask = BuildMask(InputContext.Space);
        private static readonly uint s_menuMask = BuildMask(InputContext.Menu);

        /// <summary>The row of an action. For None, or a value out of range, the row is empty (no routes).</summary>
        public static ref readonly ActionTwin Of(AtlasAction action)
        {
            int i = (int)action;
            return ref s_actions[i >= 1 && i <= ActionCount ? i : 0];
        }

        public static ref readonly AxisTwin Of(AtlasAxis axis)
        {
            int i = (int)axis;
            return ref s_axes[i >= 0 && i < AxisCount ? i : 0];
        }

        public static bool HasGamepadRoute(AtlasAction action) => Of(action).HasGamepadRoute;
        public static bool HasGazeRoute(AtlasAction action) => Of(action).HasGazeRoute;
        public static bool HasGamepadRoute(AtlasAxis axis) => Of(axis).HasGamepadRoute;
        public static bool HasGazeRoute(AtlasAxis axis) => Of(axis).HasGazeRoute;

        /// <summary>Bit of an action in a mask of actions.</summary>
        public static uint Bit(AtlasAction action) => 1u << (int)action;

        /// <summary>True when the action's bindings are live in the context. Nothing is live in the hand.</summary>
        public static bool LiveIn(AtlasAction action, InputContext context) => (ContextMask(context) & Bit(action)) != 0;

        /// <summary>The actions that are live in a context, as a mask of <see cref="Bit"/>.</summary>
        public static uint ContextMask(InputContext context)
        {
            switch (context)
            {
                case InputContext.Space: return s_spaceMask;
                case InputContext.Menu: return s_menuMask;
                default: return 0u;
            }
        }

        /// <summary>True when a gaze target carrying targetAction is a gaze twin for action.</summary>
        public static bool GazeTargetServes(AtlasAction targetAction, AtlasAction action)
        {
            if (action == AtlasAction.None || targetAction == AtlasAction.None) return false;
            return targetAction == action || Of(action).GazeAlias == targetAction;
        }

        /// <summary>
        /// The twin rule: true when every action and every axis has both a gamepad binding and a gaze-dwell
        /// route. The offenders are appended to the lists when given.
        /// </summary>
        public static bool IsComplete(List<AtlasAction> actionsMissing = null, List<AtlasAxis> axesMissing = null)
        {
            bool complete = true;
            for (int i = 1; i <= ActionCount; i++)
            {
                ref readonly ActionTwin t = ref s_actions[i];
                if ((int)t.Action == i && t.HasGamepadRoute && t.HasGazeRoute && (t.InSpace || t.InMenu)) continue;
                complete = false;
                actionsMissing?.Add((AtlasAction)i);
            }
            for (int i = 0; i < AxisCount; i++)
            {
                ref readonly AxisTwin t = ref s_axes[i];
                if ((int)t.Axis == i && t.HasGamepadRoute && t.HasGazeRoute) continue;
                complete = false;
                axesMissing?.Add((AtlasAxis)i);
            }
            return complete;
        }

        private static uint BuildMask(InputContext context)
        {
            uint mask = 0u;
            for (int i = 1; i <= ActionCount; i++)
            {
                bool live = context == InputContext.Space ? s_actions[i].InSpace
                          : context == InputContext.Menu && s_actions[i].InMenu;
                if (live) mask |= 1u << i;
            }
            return mask;
        }

        private static ActionTwin[] BuildActions()
        {
            const GazeRoute target = GazeRoute.Target;
            const AtlasAction none = AtlasAction.None;
            var t = new ActionTwin[ActionCount + 1];
            t[0] = default;

            Set(t, AtlasAction.Confirm, "<Gamepad>/buttonSouth", null, null, "<Keyboard>/enter", "<Keyboard>/space",
                target, none, "Any target registered with Confirm: a prompt chip, a menu row. Its owner", true, true, true);
            Set(t, AtlasAction.Back, "<Gamepad>/buttonEast", null, "released within 0.5 s", "<Keyboard>/backspace", null,
                target, none, "A back chip. The owner of the prompt or menu", false, true, true);
            Set(t, AtlasAction.ReturnHome, "<Gamepad>/buttonEast", null, "held 1.0 s", "<Keyboard>/h", null,
                target, none, "The home beacon. SpaceLab", false, true, false);
            Set(t, AtlasAction.NextSpace, "<Gamepad>/dpad/up", null, null, "<Keyboard>/pageUp", null,
                target, none, "The next sign. SpaceLab", false, true, false);
            Set(t, AtlasAction.PrevSpace, "<Gamepad>/dpad/down", null, null, "<Keyboard>/pageDown", null,
                target, none, "The previous sign. SpaceLab", false, true, false);
            Set(t, AtlasAction.DialLeft, "<Gamepad>/dpad/left", null, null, "<Keyboard>/leftArrow", null,
                target, none, "Floor-dial detent to the left. The dial's owner", false, true, false);
            Set(t, AtlasAction.DialRight, "<Gamepad>/dpad/right", null, null, "<Keyboard>/rightArrow", null,
                target, none, "Floor-dial detent to the right. The dial's owner", false, true, false);
            Set(t, AtlasAction.Primary, "<Gamepad>/buttonNorth", null, null, "<Keyboard>/m", null,
                target, none, "The space's first chip. The space", false, true, false);
            Set(t, AtlasAction.VariantPrev, "<Gamepad>/leftShoulder", null, null, "<Keyboard>/comma", null,
                target, none, "Left end of the space's sign. The space", false, true, false);
            Set(t, AtlasAction.VariantNext, "<Gamepad>/rightShoulder", null, null, "<Keyboard>/period", null,
                target, none, "Right end of the space's sign. The space", false, true, false);
            Set(t, AtlasAction.Secondary, "<Gamepad>/buttonWest", null, null, "<Keyboard>/x", null,
                GazeRoute.TargetOrLookDown, none, "The space's second chip, or looking down past -35 degrees. The space",
                false, true, false);
            Set(t, AtlasAction.SpaceMenu, "<Gamepad>/start", null, "while select is up", "<Keyboard>/tab", null,
                target, none, "The exhibit sign. SpaceLab", false, true, true);
            Set(t, AtlasAction.ParamMenu, "<Gamepad>/start", null, "while select is held", "<Keyboard>/f1", null,
                target, none, "A chip the on-device menu registers. The menu", false, true, true);
            Set(t, AtlasAction.NavUp, "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up", "repeats after 0.4 s, then every 0.15 s",
                "<Keyboard>/upArrow", null, GazeRoute.RowTarget, AtlasAction.NavDown,
                "Each menu row is a target: dwelling on it selects it. The menu", false, false, true);
            Set(t, AtlasAction.NavDown, "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down", "repeats after 0.4 s, then every 0.15 s",
                "<Keyboard>/downArrow", null, GazeRoute.RowTarget, AtlasAction.NavUp,
                "Each menu row is a target: dwelling on it selects it. The menu", false, false, true);
            Set(t, AtlasAction.NavLeft, "<Gamepad>/dpad/left", "<Gamepad>/leftStick/left", "repeats after 0.4 s, then every 0.15 s",
                "<Keyboard>/leftArrow", null, target, none,
                "The minus target of the selected row (GazeMode.Repeat). The menu", false, false, true);
            Set(t, AtlasAction.NavRight, "<Gamepad>/dpad/right", "<Gamepad>/leftStick/right", "repeats after 0.4 s, then every 0.15 s",
                "<Keyboard>/rightArrow", null, target, none,
                "The plus target of the selected row (GazeMode.Repeat). The menu", false, false, true);
            return t;
        }

        private static void Set(ActionTwin[] table, AtlasAction action, string pad, string padAlt, string padRule,
                                string key, string keyAlt, GazeRoute gaze, AtlasAction alias, string gazeTarget,
                                bool lever, bool inSpace, bool inMenu)
        {
            table[(int)action] = new ActionTwin(action, pad, padAlt, padRule, key, keyAlt, gaze, alias, gazeTarget,
                                                lever, inSpace, inMenu);
        }

        private static AxisTwin[] BuildAxes()
        {
            const GazeRoute hold = GazeRoute.HoldTargets;
            var t = new AxisTwin[AxisCount];
            t[(int)AtlasAxis.Sweep] = new AxisTwin(AtlasAxis.Sweep, "<Gamepad>/rightShoulder", "<Gamepad>/leftShoulder",
                "<Keyboard>/period", "<Keyboard>/comma", hold, "Two Hold targets with axis values -1 and +1: the ends of a ruler");
            t[(int)AtlasAxis.Twist] = new AxisTwin(AtlasAxis.Twist, "<Gamepad>/rightTrigger", "<Gamepad>/leftTrigger",
                "<Keyboard>/equals", "<Keyboard>/minus", hold, "Two Hold targets");
            t[(int)AtlasAxis.LeftX] = new AxisTwin(AtlasAxis.LeftX, "<Gamepad>/leftStick/right", "<Gamepad>/leftStick/left",
                "<Keyboard>/l", "<Keyboard>/j", hold, "Two Hold targets: the detents of a floor dial");
            t[(int)AtlasAxis.LeftY] = new AxisTwin(AtlasAxis.LeftY, "<Gamepad>/leftStick/up", "<Gamepad>/leftStick/down",
                "<Keyboard>/i", "<Keyboard>/k", hold, "Two Hold targets: the detents of a floor dial");
            t[(int)AtlasAxis.RightX] = new AxisTwin(AtlasAxis.RightX, "<Gamepad>/rightStick/right", "<Gamepad>/rightStick/left",
                null, null, hold, "Two Hold targets");
            t[(int)AtlasAxis.RightY] = new AxisTwin(AtlasAxis.RightY, "<Gamepad>/rightStick/up", "<Gamepad>/rightStick/down",
                null, null, hold, "Two Hold targets");
            return t;
        }
    }
}
