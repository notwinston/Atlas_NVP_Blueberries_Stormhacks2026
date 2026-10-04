using System;
using Atlas.World;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// The seam that carries the operator's <c>SceneCmd</c> to the director (task P2-C01) without a change to the app:
    /// the app hands its <see cref="IOperatorCommands"/> to this wrapper, and the link glue calls the wrapper. A
    /// <c>SceneCmd</c> to S0 with the variant <c>next</c> or <c>beat:N</c> is the director's and is not forwarded (it
    /// would otherwise re-enter Home); every other command goes on to the app unchanged.
    /// </summary>
    public sealed class OperatorCommandTap : IOperatorCommands
    {
        readonly IOperatorCommands m_Inner;
        readonly PlaygroundShell m_Shell;

        public OperatorCommandTap(IOperatorCommands inner, PlaygroundShell shell)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            m_Inner = inner;
            m_Shell = shell;
        }

        public void SwitchSpace(SpaceId space, string variant)
        {
            if (space == SpaceId.Home && !string.IsNullOrEmpty(variant))
            {
                var command = Atlas.Link.OperatorCommand.SwitchSpace((int)space, variant);
                if (m_Shell.OnOperatorCommand(in command)) return;
            }
            m_Inner.SwitchSpace(space, variant);
        }

        public void Home() { m_Inner.Home(); }
        public void Recentre() { m_Inner.Recentre(); }
        public void SetQuality(int tier) { m_Inner.SetQuality(tier); }
        public void Cue() { m_Inner.Cue(); }
        public void SetLock(bool on) { m_Inner.SetLock(on); }
        public void Walk(float moveX, float moveY, float turnDegreesPerSecond, float speed) { m_Inner.Walk(moveX, moveY, turnDegreesPerSecond, speed); }
    }
}
