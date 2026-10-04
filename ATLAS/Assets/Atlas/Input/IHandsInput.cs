namespace Atlas.Input
{
    /// <summary>
    /// How tracked hands reach the input service (documents/ATLAS_Hands_Design.md sections 6.2, 6.3 and 6.7). A
    /// fingertip presses the gaze targets that dwell already fires, and a pinch fires the target the gaze is on:
    /// both raise that target's action with <see cref="InputSource.Gaze"/>, its tag and its callback, so a hand is
    /// one more way to fire a target and never the only one (Master Plan section 0.5 rule 5). Implemented by
    /// <see cref="AtlasInput"/>; <see cref="IAtlasInput"/> itself gains no member. The hands module asks for it
    /// with "input as IHandsInput" and does without when the answer is null. Main thread only.
    /// </summary>
    public interface IHandsInput
    {
        /// <summary>
        /// This frame's fingertips and pinches. Call it before <see cref="IAtlasInput.Tick"/>, every frame hands may
        /// act: a frame with no call, or with <see cref="HandsInput.Active"/> false, has no hands, and whatever a
        /// fingertip was hovering or pressing is let go.
        /// </summary>
        void SetHands(in HandsInput hands);

        /// <summary>What touch and pinch did in the last Tick. Valid until the next Tick.</summary>
        ref readonly HandsTouchState HandsTouch { get; }

        /// <summary>
        /// The label of a target, for the event log; null when the target is unknown or has none. A target that a
        /// hand fired in the last Tick is still known here if firing it removed it.
        /// </summary>
        string LabelOf(GazeTargetId id);

        /// <summary>The tag of a target, as for <see cref="LabelOf"/>; 0 when the target is unknown.</summary>
        int TagOf(GazeTargetId id);
    }
}
