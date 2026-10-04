using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>What the rest of the app tells the visibility of a hand, once per frame and the same for both hands.</summary>
    public struct VisibilityInputs
    {
        /// <summary>The session gate is active (<see cref="HandsGate"/>). False fades the hands out over <see cref="HandsConstants.GateOutSeconds"/>, before the detector stops.</summary>
        public bool GateActive;
        /// <summary>Bump Guard's level is not Clear (decision H11): the hands fade out fast and return slowly.</summary>
        public bool Warning;
        /// <summary>A space asked for the hands to be hidden in this frame (IHands.HideThisFrame): zero at once, and back in the next frame.</summary>
        public bool Hidden;
    }

    /// <summary>
    /// How a hand appears and disappears (documents/ATLAS_Hands_Design.md section 5.4, with decision R2 for the
    /// loss). A hand's opacity is the smallest of five factors, each a ramp so that nothing pops:
    ///
    /// presence rises over <see cref="HandsConstants.FadeInSeconds"/> once the hand has been detected
    /// <see cref="HandsConstants.EnterDetections"/> times running; on a loss it holds 1 until the hand has been
    /// missing for <see cref="HandsConstants.LossFadeStartSeconds"/> and then falls over
    /// <see cref="HandsConstants.FadeOutSeconds"/>, so that it is gone when tracking ends; and it falls the same
    /// way when confidence is under <see cref="HandsConstants.ExitConfidence"/>. The edge factor is 0 at the
    /// picture's border and 1 at <see cref="HandsConstants.EdgeFadeBand"/> inside it. The warning factor falls
    /// over <see cref="HandsConstants.WarningOutSeconds"/> and returns over
    /// <see cref="HandsConstants.WarningBackSeconds"/>. The gate factor falls over
    /// <see cref="HandsConstants.GateOutSeconds"/> and is back at once. Hidden is 0 at once and 1 next frame.
    ///
    /// Engine-free, allocation-free, and called once per hand per frame.
    /// </summary>
    public sealed class HandsVisibility
    {
        struct Channel
        {
            public float Presence, Warning, Gate, Opacity;
            /// <summary>The hand has met the enter conditions and has not left since.</summary>
            public bool Entered;
        }

        readonly Channel[] _channels = new Channel[2];

        /// <summary>Both hands invisible, no warning, the gate not yet active.</summary>
        public HandsVisibility()
        {
            Reset();
        }

        /// <summary>Nothing is remembered: both hands are invisible, as before the gate first becomes active.</summary>
        public void Reset()
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i] = new Channel { Presence = 0f, Warning = 1f, Gate = 0f, Opacity = 0f, Entered = false };
        }

        /// <summary>
        /// One frame of one hand: advances its ramps by deltaTime and returns its opacity, 0 to 1. A negative
        /// or unreadable deltaTime is no time at all.
        /// </summary>
        public float Update(HandSide side, float deltaTime, in TrackedHand hand, in VisibilityInputs inputs)
        {
            ref Channel c = ref _channels[side == HandSide.Right ? 1 : 0];
            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (!hand.Tracked)
            {
                // No pose, nothing to draw. By now a lost hand has faded out: its fall ends with the filter's grace.
                c.Entered = false;
                c.Presence = 0f;
            }
            else
            {
                // The hand's own confidence is read from detections. A held pose carries whatever the filter left
                // in it, and the hold of decision R2 is a matter of time alone.
                bool lost = hand.LostSeconds > 0f;
                if (!lost && !(hand.Confidence >= HandsConstants.ExitConfidence)) c.Entered = false;
                else if (hand.GoodDetections >= HandsConstants.EnterDetections) c.Entered = true;

                bool show = c.Entered && !(hand.LostSeconds >= HandsConstants.LossFadeStartSeconds);
                c.Presence = show
                    ? math.min(1f, c.Presence + dt / HandsConstants.FadeInSeconds)
                    : math.max(0f, c.Presence - dt / HandsConstants.FadeOutSeconds);
            }

            c.Warning = inputs.Warning
                ? math.max(0f, c.Warning - dt / HandsConstants.WarningOutSeconds)
                : math.min(1f, c.Warning + dt / HandsConstants.WarningBackSeconds);
            c.Gate = inputs.GateActive ? 1f : math.max(0f, c.Gate - dt / HandsConstants.GateOutSeconds);

            // The edge is where the hand is, not a ramp in time; a margin that is not a number hides the hand.
            float edge = hand.EdgeMargin > 0f ? math.min(1f, hand.EdgeMargin / HandsConstants.EdgeFadeBand) : 0f;
            float hidden = inputs.Hidden ? 0f : 1f;

            c.Opacity = math.min(math.min(c.Presence, edge), math.min(math.min(c.Warning, c.Gate), hidden));
            return c.Opacity;
        }

        /// <summary>A hand's opacity as the last <see cref="Update"/> left it.</summary>
        public float Opacity(HandSide side)
        {
            return _channels[side == HandSide.Right ? 1 : 0].Opacity;
        }
    }
}
