using System;
using Atlas.Input;
using Atlas.Link;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// The four SpaceLab actions of the action map as local calls (Master Plan section 10.1.3; gate G5 of
    /// section 11.3): return home, next space, previous space and the space menu. The rules live here, with no
    /// engine type, so they run in headless tests; <see cref="SpaceActionStep"/> feeds it from the input
    /// service once per frame.
    /// <para>Nothing happens while the phone is in the hand, while a switch is under way, or while the demo
    /// lock is on: a visitor's gamepad then changes nothing, and the operator's commands still work.</para>
    /// </summary>
    public sealed class SpaceActions
    {
        readonly ISpaceHost _host;
        readonly ParamRegistry _registry;
        ParamHandle _lock;

        public SpaceActions(ISpaceHost host, ParamRegistry registry)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _host = host;
            _registry = registry;
        }

        /// <summary>True while the phone is in the hand whatever the input context says (onboarding, a scan).
        /// Null counts as false.</summary>
        public Func<bool> InHand { get; set; }

        /// <summary>Opens the on-device menu on its Spaces page. Null when no menu is installed: the space
        /// menu action then does nothing.</summary>
        public Action OpenSpaceMenu { get; set; }

        /// <summary>True when no SpaceLab action may run now.</summary>
        public bool Refused(InputContext context)
        {
            if (context == InputContext.InHand) return true;
            Func<bool> inHand = InHand;
            if (inHand != null && inHand()) return true;
            SpaceHostState state = _host.State;
            if (state != SpaceHostState.Running && state != SpaceHostState.Empty) return true;
            // The lock is declared by the link glue after this object may have been made: looked up once.
            if (!_lock.IsValid) _lock = _registry.HandleOf(CommandRouter.ParamDemoLock);
            return _registry.GetBool(_lock);
        }

        /// <summary>
        /// Runs one action that fired this frame. True when it did something. Return home and the two space
        /// steps belong to a running space; the space menu also answers while the menu is open, where it goes
        /// to the Spaces page.
        /// </summary>
        public bool Do(AtlasAction action, InputContext context)
        {
            if (Refused(context)) return false;
            switch (action)
            {
                case AtlasAction.ReturnHome:
                    return context == InputContext.Space && _host.ReturnHome();
                case AtlasAction.NextSpace:
                case AtlasAction.PrevSpace:
                {
                    SpaceId target;
                    if (context != InputContext.Space) return false;
                    if (!TryNeighbour(_host, action == AtlasAction.NextSpace ? 1 : -1, out target)) return false;
                    return _host.RequestSwitch(target);
                }
                case AtlasAction.SpaceMenu:
                {
                    Action open = OpenSpaceMenu;
                    if (open == null) return false;
                    open();
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// The next (direction +1) or previous (-1) space of this build in the order S0 .. S6, from the
        /// entered space, or from Home when none is entered. Spaces with no scene and no factory are skipped
        /// and the ends wrap. False when no other space is available.
        /// </summary>
        public static bool TryNeighbour(ISpaceHost host, int direction, out SpaceId target)
        {
            int count = SpaceIds.Count;
            int from = host.HasSpace && SpaceIds.IsDefined(host.Current) ? (int)host.Current : (int)SpaceId.Home;
            int step = direction < 0 ? count - 1 : 1;
            for (int i = 1; i < count; i++)
            {
                var id = (SpaceId)((from + i * step) % count);
                if (!host.IsAvailable(id)) continue;
                target = id;
                return true;
            }
            target = SpaceId.Home;
            return false;
        }
    }

    /// <summary>
    /// Step 8b: hands the SpaceLab actions that fired this frame to <see cref="SpaceActions"/>. It runs before
    /// SpaceLab's own step, so a switch starts in the frame that asked for it. Four action reads when nothing
    /// fired.
    /// </summary>
    public sealed class SpaceActionStep : IAfterFrameStep
    {
        readonly IAtlasInput _input;
        readonly SpaceActions _actions;

        public SpaceActionStep(IAtlasInput input, SpaceActions actions)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            _input = input;
            _actions = actions;
        }

        public void AfterFrame(in FrameContext frame)
        {
            // Every action is read every frame, so the input service can tell which ones are in use.
            bool home = _input.Pressed(AtlasAction.ReturnHome);
            bool next = _input.Pressed(AtlasAction.NextSpace);
            bool previous = _input.Pressed(AtlasAction.PrevSpace);
            bool menu = _input.Pressed(AtlasAction.SpaceMenu);
            if (!(home || next || previous || menu)) return;

            // One per frame: the first starts a switch, or opens the menu, and that refuses the rest.
            InputContext context = _input.Context;
            if (home) _actions.Do(AtlasAction.ReturnHome, context);
            else if (next) _actions.Do(AtlasAction.NextSpace, context);
            else if (previous) _actions.Do(AtlasAction.PrevSpace, context);
            else _actions.Do(AtlasAction.SpaceMenu, context);
        }
    }
}
