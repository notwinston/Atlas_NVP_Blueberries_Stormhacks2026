using System;
using Atlas.Input;
using Atlas.Link;
using Atlas.Spaces.Glued;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>
    /// The Glued exhibit of the Geometry Playground (Master Plan tasks P2-C12 to P2-C14): the wrap counter, the
    /// <c>glued.*</c> events, the tasks, the chiral comparison and the gluing switch over
    /// <see cref="GluedSpace"/> (S1). It adds no geometry of its own, so S1 costs what P1-A09 and P1-A15 measured
    /// (<see cref="GluedBudget"/>); the gluing sign is a gaze target, and its look belongs to the look polish
    /// (P2-A04). The logic is <see cref="GluedExhibitCore"/>; this is the adapter.
    /// </summary>
    public sealed class GluedExhibit : MonoBehaviour
    {
        /// <summary>The gluing sign hangs this high and this far inside the free rectangle's far wall, metres.</summary>
        const float SignHeight = 1.5f, SignInset = 0.3f;
        static readonly Vector2 s_SignHalfSize = new Vector2(0.3f, 0.12f);

        GluedExhibitCore m_Core;
        GluedSpace m_Space;
        ParamEntry m_Mode;
        IAtlasInput m_Input;
        Action<AtlasAction, InputSource, int> m_OnAction;
        GazeTargetId m_Sign;
        bool m_HasSign;

        /// <summary>The logic behind this component. Null until Bind.</summary>
        public GluedExhibitCore Core { get { return m_Core; } }
        /// <summary>The caption chip of the exhibit.</summary>
        public string Caption { get { return GluedText.Caption; } }
        /// <summary>What the walker is asked to do now; empty before Bind. Raises <see cref="PromptChanged"/>.</summary>
        public string Prompt { get { return m_Core != null ? m_Core.Prompt : string.Empty; } }
        public event Action<string> PromptChanged;
        /// <summary>The exhibit's wrap counter and the space's own crossing count agree (a check for the Editor and the logs).</summary>
        public bool CountersAgree { get { return m_Core != null && m_Space != null && m_Core.Wraps == m_Space.Crossings; } }

        /// <summary>
        /// Wires the exhibit to a configured, entered Glued space. <paramref name="registry"/> is the registry the
        /// space declared glued.mode in; the exhibit follows that parameter, so the operator's SceneCmd and the
        /// gluing sign move the same value.
        /// </summary>
        public void Bind(GluedSpace space, RoomModel room, ParamRegistry registry)
        {
            if (space == null) throw new ArgumentNullException(nameof(space));
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            Unbind();
            m_Space = space;
            m_Mode = registry.Find(GluedSpace.ParamMode);
            AtlasServices.TryGet(out IEventLog log);
            m_Core = new GluedExhibitCore(room, log);
            m_Core.PromptChanged += OnPromptChanged;

            if (AtlasServices.TryGet(out m_Input))
            {
                m_OnAction = OnAction;
                m_Input.ActionFired += m_OnAction;
                RegisterSign(room);
            }
        }

        public void Unbind()
        {
            if (m_Input != null)
            {
                if (m_OnAction != null) m_Input.ActionFired -= m_OnAction;
                if (m_HasSign) m_Input.UnregisterGazeTarget(m_Sign);
            }
            m_HasSign = false;
            m_Input = null;
            m_OnAction = null;
            if (m_Core != null) m_Core.PromptChanged -= OnPromptChanged;
            m_Core = null;
            m_Space = null;
            m_Mode = null;
        }

        /// <summary>Starts the exhibit again, in the torus.</summary>
        public void Restart()
        {
            if (m_Core == null) return;
            m_Core.Begin();
            if (m_Mode != null && m_Mode.Int != (int)GluedLayout.Torus) m_Mode.Set((int)GluedLayout.Torus);
        }

        /// <summary>Once a frame, after the space's own Tick.</summary>
        public void Tick(in FrameContext frame)
        {
            if (m_Core == null) return;
            GluedLayout layout = GluedRules.LayoutOf(m_Mode != null ? m_Mode.Int : 0);
            m_Core.Step(frame.HeadPosition, frame.HeadRotation, layout);
        }

        void OnDestroy()
        {
            Unbind();
        }

        // The gluing sign: a rectangle on the far wall of the free floor, facing the room. Dwelling on it steps the
        // gluing, the same as the right bumper.
        void RegisterSign(RoomModel room)
        {
            Vector3 centre = new Vector3(0f, SignHeight, 0.5f * room.FreeRect.Df - SignInset);
            GazeTarget sign = GazeTarget.WorldRect(AtlasAction.VariantNext, centre, Quaternion.Euler(0f, 180f, 0f), s_SignHalfSize);
            sign.Label = GluedGluingSwitch.SignLabel;
            m_Sign = m_Input.RegisterGazeTarget(in sign);
            m_HasSign = m_Sign.IsValid;
        }

        void OnAction(AtlasAction action, InputSource source, int tag)
        {
            if (m_Core == null || m_Mode == null) return;
            // Until the wave is done, a lever click confirms the prompt and is not the gluing switch.
            bool leverSteps = m_Core.Task >= GluedTask.Klein;
            if (action == AtlasAction.Confirm && m_Core.Task == GluedTask.Wave) m_Core.Acknowledge();
            int next = GluedGluingSwitch.Next(action, source, m_Mode.Int, leverSteps);
            if (next >= 0) m_Mode.Set(next);
        }

        void OnPromptChanged(string text)
        {
            Action<string> changed = PromptChanged;
            if (changed != null) changed(text);
        }
    }
}
