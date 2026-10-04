using Atlas.Input;
using Atlas.Rendering;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// The state of the prompt chip (task P2-C09): what <see cref="PromptQueue"/> shows. Engine-free, so a test can read
    /// it; <see cref="PromptChipView"/> draws it. Main thread only.
    /// </summary>
    public sealed class PromptChip : IPromptSink
    {
        public bool Visible { get; private set; }
        public string Text { get; private set; } = "";
        public PromptKind Kind { get; private set; }
        /// <summary>Moves on every Show and Hide.</summary>
        public int Revision { get; private set; }

        public void Show(Prompt prompt)
        {
            Visible = true;
            Text = prompt.Text;
            Kind = prompt.Kind;
            Revision++;
        }

        public void Hide()
        {
            Visible = false;
            Revision++;
        }
    }

    /// <summary>Draws a <see cref="PromptChip"/> under the HUD parent, like <see cref="CaptionChipView"/>.</summary>
    public sealed class PromptChipView
    {
        readonly PromptChip m_Chip;
        readonly HudPanel m_Panel;
        int m_ShownRevision = -1;

        PromptChipView(PromptChip chip, Transform hud, MaterialPool pool, IAtlasInput input = null)
        {
            m_Chip = chip;
            m_Panel = new HudPanel("PlaygroundPromptChip", hud, pool, CardLayout.PromptCentre, CardLayout.PromptHalfWidth,
                                   CardLayout.PromptHalfHeight, CardLayout.PromptLineHeight, TextAnchor.MiddleCenter, 0f, true, input);
        }

        public static PromptChipView Create(PromptChip chip, Transform hud, MaterialPool pool, IAtlasInput input = null)
        {
            if (chip == null) throw new System.ArgumentNullException(nameof(chip));
            if (hud == null) throw new System.ArgumentNullException(nameof(hud));
            return new PromptChipView(chip, hud, pool, input);
        }

        /// <summary>Once per frame: copies the text when the chip changed.</summary>
        public void Update()
        {
            m_Panel.PlaceWithin(ReachSlot.PromptChip, CardLayout.PromptCentre);
            if (m_Chip.Revision == m_ShownRevision) return;
            m_ShownRevision = m_Chip.Revision;
            m_Panel.SetText(m_Chip.Text);
            m_Panel.SetAlpha(m_Chip.Visible ? 1f : 0f);
        }

        public void Dispose()
        {
            m_Panel.Dispose();
        }
    }
}
