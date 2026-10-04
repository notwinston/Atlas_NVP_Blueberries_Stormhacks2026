namespace Atlas.Input
{
    /// <summary>Everything a walker can ask for (Master Plan section 0.5 rule 5, task P1-C10). The values are
    /// stable: they are written to the event log.</summary>
    public enum AtlasAction : byte
    {
        None = 0,
        Confirm = 1, Back = 2, ReturnHome = 3,
        NextSpace = 4, PrevSpace = 5,
        DialLeft = 6, DialRight = 7,
        Primary = 8,
        VariantPrev = 9, VariantNext = 10,
        Secondary = 11,
        SpaceMenu = 12, ParamMenu = 13,
        NavUp = 14, NavDown = 15, NavLeft = 16, NavRight = 17
    }

    /// <summary>Held, continuous inputs: -1 to +1.</summary>
    public enum AtlasAxis : byte { Sweep = 0, Twist = 1, LeftX = 2, LeftY = 3, RightX = 4, RightY = 5 }

    /// <summary>Which twin produced an action.</summary>
    public enum InputSource : byte { None = 0, Gamepad = 1, Gaze = 2, Lever = 3, Keyboard = 4 }

    /// <summary>Which bindings are live. InHand: the phone is in the hand (touch UI, no reticle).
    /// Space: in the headset, a space is running. Menu: in the headset, a menu is open.</summary>
    public enum InputContext : byte { InHand = 0, Space = 1, Menu = 2 }
}
