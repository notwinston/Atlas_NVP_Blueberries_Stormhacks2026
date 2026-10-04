using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Blades.Playground
{
    /// <summary>Draws an <see cref="EquationCard"/> under the HUD parent. The card reads its source at 15 Hz; the
    /// text object is set only when the drawn text changed.</summary>
    public sealed class EquationCardView
    {
        readonly EquationCard _card;
        readonly HudPanel _panel;
        int _shownRevision = -1;
        bool _shown;

        EquationCardView(EquationCard card, Transform hud, MaterialPool pool)
        {
            _card = card;
            _panel = new HudPanel("PlaygroundEquationCard", hud, pool, CardLayout.CardCentre, CardLayout.CardHalfWidth,
                                  CardLayout.CardHalfHeight, CardLayout.CardLineHeight, TextAnchor.UpperLeft,
                                  -CardLayout.CardHalfWidth + 0.015f);
        }

        public static EquationCardView Create(EquationCard card, Transform hud, MaterialPool pool)
        {
            if (card == null) throw new System.ArgumentNullException(nameof(card));
            if (hud == null) throw new System.ArgumentNullException(nameof(hud));
            return new EquationCardView(card, hud, pool);
        }

        /// <summary>Once per frame, from the app's upload step: ticks the card and draws it while bound.</summary>
        public void Update(float deltaTime)
        {
            _card.Tick(deltaTime);
            bool bound = _card.IsBound;
            if (bound != _shown)
            {
                _shown = bound;
                _panel.SetAlpha(bound ? 1f : 0f);
            }
            if (bound && _card.Revision != _shownRevision)
            {
                _shownRevision = _card.Revision;
                // Legacy 3D text takes a string: one small allocation per change, none while the numbers hold still.
                _panel.SetText(_card.Text.ToString());
            }
        }

        public void Dispose()
        {
            _panel.Dispose();
        }
    }
}
