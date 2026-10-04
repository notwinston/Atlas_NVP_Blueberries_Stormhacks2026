using System;
using Atlas.Link;
using Atlas.Safety;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App
{
    /// <summary>
    /// The six operator commands as local calls (Master Plan section 7.3.2). The link glue hands a received
    /// command to this class at step 8b; it never passes a link type on, so Bump Guard's cue is fired by a
    /// plain call and Atlas.Safety never sees the network.
    /// </summary>
    public sealed class CommandRouter : IOperatorCommands
    {
        public const string ParamPinTier = "thermal.pinTier";
        public const string ParamDemoLock = "demoLock";

        readonly ISpaceHost _host;
        readonly IFrameLoop _loop;
        readonly ParamRegistry _registry;
        readonly IPosePipeline _pipeline;
        readonly Action _cue;

        /// <param name="pipeline">May be null: recentre then resets the walker only.</param>
        /// <param name="cue">The STOP cue; null gives the real one, <see cref="FireOperatorCue"/>.</param>
        public CommandRouter(ISpaceHost host, IFrameLoop loop, ParamRegistry registry, IPosePipeline pipeline = null,
            Action cue = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _host = host;
            _loop = loop;
            _registry = registry;
            _pipeline = pipeline;
            _cue = cue ?? FireOperatorCue;
        }

        /// <summary>
        /// The Console's STOP: the warning cue at STRONG with no direction, from the operator, which Bump
        /// Guard's own level cannot clear before its hold has run out.
        /// </summary>
        public static void FireOperatorCue()
        {
            BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);
        }

        public void SwitchSpace(SpaceId space, string variant)
        {
            if (!string.IsNullOrEmpty(variant)) ApplyVariant(space, variant);
            _host.RequestSwitch(space);
        }

        public void Home()
        {
            _host.ReturnHome();
        }

        public void Recentre()
        {
            _loop.ResetWalkerToFloor();
            if (_pipeline != null) _pipeline.ResetGeometry();
        }

        public void SetQuality(int tier)
        {
            int clamped = tier < OperatorCommand.TierAuto ? OperatorCommand.TierAuto
                : (tier > OperatorCommand.TierHigh ? OperatorCommand.TierHigh : tier);
            _registry.Set(ParamPinTier, clamped, ParamSource.Remote);
        }

        public void Cue()
        {
            _cue();
        }

        public void SetLock(bool on)
        {
            _registry.Set(ParamDemoLock, on ? 1f : 0f, ParamSource.Remote);
        }

        /// <summary>
        /// The parameter a <c>SceneCmd</c> variant sets for a space (lane A contract section 14): the gluing of
        /// S1, the layout of S5, the body of S6. Null for a space that has no variants.
        /// </summary>
        public static string VariantParam(SpaceId space)
        {
            switch (space)
            {
                case SpaceId.Glued: return "glued.mode";
                case SpaceId.Portal: return "rooms.layout";
                case SpaceId.Slicer: return "slicer.object";
                default: return null;
            }
        }

        // A variant names one value of the space's variant parameter, by name or by index. A space with no
        // such parameter, or one that has not declared it, ignores the variant.
        void ApplyVariant(SpaceId space, string variant)
        {
            string name = VariantParam(space);
            ParamEntry mode;
            if (name == null || !_registry.TryGet(name, out mode)) return;
            for (int i = 0; i < mode.EnumCount; i++)
            {
                if (string.Equals(mode.EnumName(i), variant, StringComparison.OrdinalIgnoreCase))
                {
                    _registry.Set(mode, i, ParamSource.Remote);
                    return;
                }
            }
            int index;
            if (int.TryParse(variant, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out index))
                _registry.Set(mode, index, ParamSource.Remote);
        }
    }
}
