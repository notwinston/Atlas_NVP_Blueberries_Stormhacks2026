using System;
using Atlas.World;

namespace Atlas.Blades.Playground
{
    /// <summary>The rule of each exhibit, in the words of Master Plan section 10.1.3, and the two mode captions of
    /// beat 8. A flat space is never called curved (section 10.1.4): the texts of S0 and S1 carry no "curv".</summary>
    public static class CaptionTexts
    {
        /// <summary>S0. The plan gives no chip for Home; this is the flat counterpart of the others.</summary>
        public const string Home = "Your room, rebuilt 1:1. Flat.";
        public const string Glued = "Every wall is a door to its opposite wall. Flat, but it wraps.";
        public const string CurvedWalk = "Five rooms meet at every corner. Constant negative curvature.";
        public const string AlignedCells = "Each cell is flat. All the curvature sits at the corners.";
        public const string SmallPlanet = "Three rooms meet at every corner. Six tiles, then it repeats.";
        public const string Portal = "Flat rooms joined so that they overlap.";
        public const string Slicer = "A 3D slice of a flat 4D shape.";

        /// <summary>Beat 8: Mode 1 (S2) against Mode 2 (S3).</summary>
        public const string ModeSmooth = "smooth curvature";
        public const string ModeCorners = "curvature at the corners";

        /// <summary>The chip of a space at its entrance.</summary>
        public static string For(SpaceId id)
        {
            switch (id)
            {
                case SpaceId.Home: return Home;
                case SpaceId.Glued: return Glued;
                case SpaceId.CurvedWalk: return CurvedWalk;
                case SpaceId.AlignedCells: return AlignedCells;
                case SpaceId.SmallPlanet: return SmallPlanet;
                case SpaceId.Portal: return Portal;
                case SpaceId.Slicer: return Slicer;
                default: return "";
            }
        }

        /// <summary>
        /// The chip of S2 for the dial position: the dial is "rooms per corner", so the caption follows it and a flat
        /// position never says curved. q = 5 is the chip of the plan; q = 3 is Small Planet's rule; q = 4 and q = 6
        /// are the same sentence with their own count (derived).
        /// </summary>
        public static string CurvedWalkForDial(int q)
        {
            switch (q)
            {
                case 3: return SmallPlanet;
                case 4: return "Four rooms meet at every corner. Flat: four turns bring you home.";
                case 6: return "Six rooms meet at every corner. Constant negative curvature.";
                default: return CurvedWalk;
            }
        }

        /// <summary>Mode 1 or 2 of beat 8; anything else is empty.</summary>
        public static string ForMode(int mode)
        {
            return mode == 1 ? ModeSmooth : mode == 2 ? ModeCorners : "";
        }

        /// <summary>True when the text names curvature although the space is flat (S0, S1, S2 at q = 4).</summary>
        public static bool CallsFlatSpaceCurved(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToLowerInvariant();
            // "Flat: four turns bring you home" is flat and says so; the word "curv" is what must not appear.
            return t.Contains("curv");
        }
    }

    /// <summary>
    /// The caption chip (Master Plan section 10.1.3, task P2-C06): a one-line card with the rule of the exhibit,
    /// shown at every entrance. Engine-free: it listens to the space host, holds the text, the fade and the
    /// count of entrances shown, and <see cref="CaptionChipView"/> draws it. Main thread only.
    /// </summary>
    public sealed class CaptionChip : IDisposable
    {
        /// <summary>How long the chip stays after an entrance before it fades (derived: long enough to read
        /// the longest rule twice, and the walker can also dismiss it with a dwell).</summary>
        public const float ShowSeconds = 12f;
        /// <summary>Fade in and out, seconds.</summary>
        public const float FadeSeconds = 0.5f;

        readonly int[] _shown = new int[SpaceIds.Count];
        ISpaceHost _host;
        string _text = "";
        float _age;
        bool _visible;
        int _q = 5;

        public bool Visible { get { return _visible; } }
        public string Text { get { return _text; } }
        /// <summary>0 hidden, 1 fully shown.</summary>
        public float Alpha { get; private set; }
        /// <summary>Moves on every change of the text or of the visibility.</summary>
        public int Revision { get; private set; }
        /// <summary>Entrances at which a chip was shown, over all spaces.</summary>
        public int EntrancesShown { get; private set; }
        /// <summary>The space of the chip now or last shown.</summary>
        public SpaceId Space { get; private set; }

        /// <summary>How many times the chip of a space was shown.</summary>
        public int ShownCount(SpaceId id)
        {
            return SpaceIds.IsDefined(id) ? _shown[(int)id] : 0;
        }

        /// <summary>Follows the host: a chip at every <see cref="ISpaceHost.SpaceEntered"/>, hidden at every exit.</summary>
        public void Attach(ISpaceHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            Detach();
            _host = host;
            host.SpaceEntered += OnEntered;
            host.SpaceExited += OnExited;
        }

        public void Detach()
        {
            if (_host == null) return;
            _host.SpaceEntered -= OnEntered;
            _host.SpaceExited -= OnExited;
            _host = null;
        }

        public void Dispose()
        {
            Detach();
        }

        /// <summary>The dial moved inside S2: the caption follows it (a chip already shown is replaced).</summary>
        public void SetDial(int q)
        {
            _q = q;
            if (_visible && Space == SpaceId.CurvedWalk) Show(SpaceId.CurvedWalk, CaptionTexts.CurvedWalkForDial(q), false);
        }

        /// <summary>Shows the chip of a space. Called by the host event, and by tests.</summary>
        public void OnSpaceEntered(SpaceId id)
        {
            string text = id == SpaceId.CurvedWalk ? CaptionTexts.CurvedWalkForDial(_q) : CaptionTexts.For(id);
            Show(id, text, true);
        }

        /// <summary>Shows any one-line text as the chip, for example the mode caption of beat 8.</summary>
        public void ShowText(string text)
        {
            Show(Space, text, false);
        }

        public void Dismiss()
        {
            if (!_visible) return;
            _visible = false;
            Revision++;
        }

        /// <summary>Advances the age and the fade.</summary>
        public void Tick(float deltaTime)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;
            if (_visible)
            {
                _age += dt;
                if (_age >= ShowSeconds)
                {
                    _visible = false;
                    Revision++;
                }
            }
            float step = dt / FadeSeconds;
            Alpha = _visible ? Math.Min(1f, Alpha + step) : Math.Max(0f, Alpha - step);
        }

        void Show(SpaceId id, string text, bool entrance)
        {
            if (string.IsNullOrEmpty(text)) return;
            Space = id;
            _text = text;
            _age = 0f;
            _visible = true;
            Revision++;
            if (entrance && SpaceIds.IsDefined(id))
            {
                _shown[(int)id]++;
                EntrancesShown++;
            }
        }

        void OnEntered(SpaceId id) { OnSpaceEntered(id); }

        void OnExited(SpaceId id)
        {
            Dismiss();
            Alpha = 0f;
        }
    }
}
