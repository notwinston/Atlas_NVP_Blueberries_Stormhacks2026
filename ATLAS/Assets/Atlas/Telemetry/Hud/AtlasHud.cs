using System.Text;
using Atlas.Link;
using Atlas.World;
using TMPro;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The in-headset HUD and the link dot (Master Plan sections 7.2 and 8.8, task P1-D04). Two small
    /// TextMeshPro objects built in code under the head-locked HUD root, so both eyes draw them at their real
    /// depth. The text is rebuilt twice per second from the last telemetry row into one cached builder and
    /// handed over without making a string; between refreshes the HUD does no work.
    /// </summary>
    public sealed class AtlasHud : IAfterFrameStep
    {
        public const float RefreshSeconds = 0.5f;

        /// <summary>Distance of the panel and of the dot from the eyes, metres.</summary>
        public const float Distance = 1.0f;
        /// <summary>The panel sits this far below the view axis, degrees.</summary>
        public const float PanelBelowDegrees = 14f;
        public const float DotBelowDegrees = 22f;
        /// <summary>Height of one line of the panel, metres: five lines stay inside a 0.45 m wide panel.</summary>
        public const float LineHeight = 0.02f;
        /// <summary>Width of the panel, metres.</summary>
        public const float PanelWidth = 0.45f;

        // One point of a TextMeshPro world text is a tenth of a unit, and a line of the default font is about
        // 1.15 times its point size.
        const float PointsPerLineMetre = 10f / 1.15f;

        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);
        static readonly Color s_linkOk = new Color(0.25f, 0.85f, 0.40f, 1f);
        static readonly Color s_linkDegraded = new Color(0.95f, 0.70f, 0.20f, 1f);
        static readonly Color s_linkOff = new Color(0.55f, 0.55f, 0.55f, 1f);

        readonly AtlasPerfProbe _probe;
        readonly LinkPump _pump;
        readonly StringBuilder _text = new StringBuilder(256);
        readonly GameObject _panelObject, _dotObject;
        readonly TextMeshPro _panel, _dot;
        float _sinceRefresh = RefreshSeconds;
        bool _panelShown, _dotShown;
        LinkState _dotState = (LinkState)255;

        /// <param name="parent">The head-locked HUD root (<c>AtlasBootContext.Hud</c>); null builds no objects.</param>
        /// <param name="probe">Source of the telemetry row; may be null.</param>
        /// <param name="pump">Source of the link state and the Bump Guard level; may be null.</param>
        public AtlasHud(Transform parent, AtlasPerfProbe probe, LinkPump pump)
        {
            _probe = probe;
            _pump = pump;
            LinkDotEnabled = true;
            if (parent == null) return;

            // The default font asset of the project's TextMeshPro resources. A build without them has no HUD
            // and runs on.
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null) return;

            _panel = MakeText(parent, "AtlasHudPanel", font, PanelBelowDegrees, LineHeight,
                new Vector2(PanelWidth, LineHeight * 6f), TextAlignmentOptions.Left, out _panelObject);
            _panel.color = s_text;
            _dot = MakeText(parent, "AtlasLinkDot", font, DotBelowDegrees, LineHeight * 1.5f,
                new Vector2(LineHeight * 3f, LineHeight * 3f), TextAlignmentOptions.Center, out _dotObject);
            _dot.text = "\u2022";
            _dot.color = s_linkOff;
            _panelObject.SetActive(false);
            _dotObject.SetActive(false);
        }

        /// <summary><c>hud.visible</c>.</summary>
        public bool Visible { get; set; }

        /// <summary>True while <c>telemetry.mode</c> is <c>hud</c>: the panel is shown whatever <see cref="Visible"/> says.</summary>
        public bool ForceVisible { get; set; }

        /// <summary>False while <c>link.enabled</c> is off: no dot.</summary>
        public bool LinkDotEnabled { get; set; }

        /// <summary>The panel's text as last built. For tests and the Editor.</summary>
        public StringBuilder Text { get { return _text; } }

        static TextMeshPro MakeText(Transform parent, string name, TMP_FontAsset font, float belowDegrees, float lineHeight,
            Vector2 size, TextAlignmentOptions alignment, out GameObject go)
        {
            go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = lineHeight * PointsPerLineMetre;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;

            // The component turns the transform into a RectTransform: place it after the component exists.
            RectTransform rect = text.rectTransform;
            rect.sizeDelta = size;
            float a = belowDegrees * Mathf.Deg2Rad;
            rect.localPosition = new Vector3(0f, -Mathf.Sin(a) * Distance, Mathf.Cos(a) * Distance);
            // Tilted to face the eyes.
            rect.localRotation = Quaternion.Euler(belowDegrees, 0f, 0f);

            Renderer renderer = text.renderer;
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return text;
        }

        /// <summary>Step 8b: shows or hides the two objects and, every half second, rebuilds their content.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            if (_panel == null) return;
            bool sessionOver = frame.Tier == QualityTier.Critical;
            LinkState link = _pump != null ? _pump.State : LinkState.Offline;
            bool demoLock = _pump != null && _pump.Client.DemoLock;

            bool showPanel = Visible || ForceVisible || sessionOver;
            // The dot is hidden while everything is fine in a locked demo: the audience needs no indicator.
            bool showDot = LinkDotEnabled && _pump != null && !(demoLock && link == LinkState.Connected);
            if (showPanel != _panelShown)
            {
                _panelShown = showPanel;
                _panelObject.SetActive(showPanel);
                _sinceRefresh = RefreshSeconds;
            }
            if (showDot != _dotShown)
            {
                _dotShown = showDot;
                _dotObject.SetActive(showDot);
            }
            if (!showPanel && !showDot) return;

            _sinceRefresh += frame.DeltaTime;
            if (_sinceRefresh < RefreshSeconds) return;
            _sinceRefresh = 0f;

            if (showDot && link != _dotState)
            {
                _dotState = link;
                _dot.color = link == LinkState.Connected ? s_linkOk : link == LinkState.Offline ? s_linkOff : s_linkDegraded;
            }
            if (showPanel)
            {
                TelemetryMessage row;
                if (_probe == null || !_probe.TryGetLastRow(out row)) row = new TelemetryMessage { Battery = 255 };
                Refresh(in row, link, _pump != null ? _pump.BumpLevel : 0, sessionOver);
            }
        }

        /// <summary>Rebuilds the panel's text from plain values.</summary>
        public void Refresh(in TelemetryMessage row, LinkState link, int bumpLevel, bool sessionOver)
        {
            HudText.Build(_text, in row, link, bumpLevel, sessionOver);
            // The builder's characters are copied into the text object's own array: no string is made.
            if (_panel != null) _panel.SetText(_text);
        }
    }
}
