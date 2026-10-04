using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Blades.Playground
{
    /// <summary>Draws a <see cref="CaptionChip"/> under the HUD parent. Nothing here decides anything: it copies the
    /// text when the chip's revision moved and sets the fade each frame.</summary>
    public sealed class CaptionChipView
    {
        readonly CaptionChip _chip;
        readonly HudPanel _panel;
        int _shownRevision = -1;

        CaptionChipView(CaptionChip chip, Transform hud, MaterialPool pool)
        {
            _chip = chip;
            _panel = new HudPanel("PlaygroundCaptionChip", hud, pool, CardLayout.ChipCentre, CardLayout.ChipHalfWidth,
                                  CardLayout.ChipHalfHeight, CardLayout.ChipLineHeight, TextAnchor.MiddleCenter, 0f);
        }

        public static CaptionChipView Create(CaptionChip chip, Transform hud, MaterialPool pool)
        {
            if (chip == null) throw new System.ArgumentNullException(nameof(chip));
            if (hud == null) throw new System.ArgumentNullException(nameof(hud));
            return new CaptionChipView(chip, hud, pool);
        }

        /// <summary>Once per frame, from the app's upload step: ticks the chip and draws it.</summary>
        public void Update(float deltaTime)
        {
            _chip.Tick(deltaTime);
            if (_chip.Revision != _shownRevision)
            {
                _shownRevision = _chip.Revision;
                _panel.SetText(_chip.Text);
            }
            _panel.SetAlpha(_chip.Alpha);
        }

        public void Dispose()
        {
            _panel.Dispose();
        }
    }
}
