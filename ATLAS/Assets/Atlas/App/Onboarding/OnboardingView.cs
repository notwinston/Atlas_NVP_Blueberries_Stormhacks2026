using System.Text;
using Atlas.Scan;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Atlas.App.Onboarding
{
    /// <summary>
    /// The onboarding screen: one full-screen panel built in code with UI Toolkit (contract README F3), shown
    /// in the hand over the camera picture. A card on the right carries the words and the buttons; the rest
    /// of the screen shows the camera with the floor outline drawn on it, and takes the taps that place and
    /// drag corners. Every colour, size and font is set here, because a panel made in code has no theme.
    /// The view holds no logic: the words come from OnboardingScreens and every tap goes to OnboardingFlow.
    /// </summary>
    sealed class OnboardingView
    {
        // Steps whose numbers move by themselves are redrawn this often, seconds.
        const float TextInterval = 0.25f;
        // How near a touch must be to a corner to pick it up, as a share of the screen height.
        const float PickRadius = 0.09f;
        const float CardShare = 46f;

        static readonly Color CardColor = new Color(0.03f, 0.04f, 0.06f, 0.93f);
        static readonly Color Ink = Color.white;
        static readonly Color Amber = new Color(1f, 0.80f, 0.20f, 1f);
        static readonly Color ButtonDark = new Color(0.20f, 0.22f, 0.27f, 1f);
        static readonly Color Shade = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color OutlineColor = Color.white;
        static readonly Color FreeColor = new Color(0.25f, 1f, 0.45f, 1f);
        static readonly Color LapColor = new Color(1f, 0.85f, 0.1f, 1f);

        readonly OnboardingFlow _flow;
        readonly ScanSession _session;
        readonly Camera _camera;
        readonly OnboardingScreen _screen = new OnboardingScreen();
        readonly StringBuilder _text = new StringBuilder(512);
        readonly float2[] _points = new float2[RoomModel.MaxOutlinePoints + 1];
        readonly float2[] _rect = new float2[4];

        GameObject _host;
        PanelSettings _panel;
        VisualElement _root, _touch, _card, _buttonRow;
        Label _title, _body, _notice;
        /// <summary>The theme style sheet under Resources: one line that imports Unity's default runtime theme.</summary>
        public const string ThemeResource = "AtlasUi/AtlasRuntimeTheme";

        TextField _address;
        readonly Button[] _buttons = new Button[OnboardingScreen.MaxButtons];

        int _shownVersion = -1;
        float _textTimer;
        bool _hidden;
        int _dragIndex = -1;
        bool _dragValid;
        float2 _dragPoint;
        float _dragY;
        // The tracked camera's layers, put aside while only the camera picture is wanted.
        bool _pictureOnly;
        int _savedMask;

        public OnboardingView(Transform parent, OnboardingFlow flow, ScanSession session, Camera camera)
        {
            _flow = flow;
            _session = session;
            _camera = camera;
            Build(parent);
        }

        /// <summary>Once per frame, after the flow's own tick.</summary>
        public void Refresh(float deltaTime)
        {
            if (_root == null) return;
            OnboardingStep step = _flow.Step;
            ShowPictureOnly(OnboardingFlow.CameraPictureOnly(step));
            if (step == OnboardingStep.Home || step == OnboardingStep.Idle)
            {
                // In the headset nothing of onboarding is drawn and nothing here runs.
                if (!_hidden) Rebuild();
                return;
            }

            _textTimer += deltaTime;
            bool moving = step == OnboardingStep.Sweep || step == OnboardingStep.Station;
            if (_flow.Version != _shownVersion || (moving && _textTimer >= TextInterval)) Rebuild();

            // The camera moves every frame, so the lines on the floor are drawn again every frame.
            if (step == OnboardingStep.Sweep || step == OnboardingStep.Confirm) _touch.MarkDirtyRepaint();

            // The gamepad's A answers the headset prompt as the lever does.
            if (step == OnboardingStep.Headset)
            {
                Gamepad pad = Gamepad.current;
                if (pad != null && pad.buttonSouth.wasPressedThisFrame) _flow.Do(OnboardingAction.HeadsetNow);
            }
        }

        /// <summary>
        /// While the real room is looked at, the tracked camera draws no object, only the camera picture: the
        /// floor and walls of the room in force and the entered space would cover what is being scanned. The
        /// panel and the AR background do not depend on the camera's layers.
        /// </summary>
        void ShowPictureOnly(bool on)
        {
            if (on == _pictureOnly || _camera == null) return;
            if (on)
            {
                _savedMask = _camera.cullingMask;
                _camera.cullingMask = 0;
            }
            else
            {
                _camera.cullingMask = _savedMask;
            }
            _pictureOnly = on;
        }

        public void Dispose()
        {
            ShowPictureOnly(false);
            AppObjects.Kill(_host);
            AppObjects.Kill(_panel);
            _host = null;
            _panel = null;
            _root = null;
        }

        // ---- building ---------------------------------------------------------------------------------------

        void Build(Transform parent)
        {
            _host = new GameObject("Onboarding");
            _host.transform.SetParent(parent, false);
            var document = _host.AddComponent<UIDocument>();
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.hideFlags = HideFlags.DontSave;
            // Landscape only: one layout, scaled to the screen.
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(1920, 1080);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panel.match = 1f;
            _panel.sortingOrder = 10;
            // The default runtime theme gives the text field and the buttons their base styling. Every size
            // and colour of this screen is set inline, so a missing theme costs polish, not the screen.
            var theme = Resources.Load<ThemeStyleSheet>(ThemeResource);
            if (theme != null) _panel.themeStyleSheet = theme;
            else Debug.LogWarning("Atlas: " + ThemeResource + " not in Resources; the onboarding screen has no theme.");
            document.panelSettings = _panel;

            _root = document.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("Atlas: the onboarding panel could not be created.");
                return;
            }
            _root.style.flexGrow = 1f;
            _root.pickingMode = PickingMode.Ignore;
            // No theme style sheet reaches a panel made in code: the font is given here and inherited.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) _root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            _root.style.color = Ink;

            // The whole screen under the card: the lines on the floor, and the taps on the camera picture.
            _touch = new VisualElement();
            Fill(_touch);
            _touch.pickingMode = PickingMode.Position;
            _touch.generateVisualContent += DrawFloor;
            _touch.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _touch.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _touch.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _root.Add(_touch);

            _card = new VisualElement();
            _card.style.position = Position.Absolute;
            _card.style.top = 0f;
            _card.style.bottom = 0f;
            _card.style.right = 0f;
            _card.style.width = new Length(CardShare, LengthUnit.Percent);
            _card.style.backgroundColor = CardColor;
            _card.style.paddingLeft = 40f;
            _card.style.paddingRight = 40f;
            _card.style.paddingTop = 30f;
            _card.style.paddingBottom = 30f;
            _card.style.flexDirection = FlexDirection.Column;
            // Only the buttons take touches: a lever press anywhere else reaches the layer underneath.
            _card.pickingMode = PickingMode.Ignore;
            _root.Add(_card);

            _title = Text(60f, Ink, true);
            _body = Text(38f, Ink, false);
            _body.style.marginTop = 14f;
            _body.style.flexShrink = 1f;
            _body.style.overflow = Overflow.Hidden;
            _notice = Text(36f, Amber, true);
            _notice.style.marginTop = 14f;
            _card.Add(_title);
            _card.Add(_body);
            _card.Add(_notice);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            spacer.pickingMode = PickingMode.Ignore;
            _card.Add(spacer);

            _address = new TextField();
            _address.style.height = 84f;
            _address.style.fontSize = 40f;
            _address.style.color = Color.black;
            _address.style.backgroundColor = Color.white;
            _address.style.marginTop = 12f;
            _address.style.paddingLeft = 16f;
            _address.style.flexShrink = 0f;
            VisualElement input = _address.Q(null, TextField.inputUssClassName);
            if (input != null)
            {
                input.style.flexGrow = 1f;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
            _card.Add(_address);

            _buttonRow = new VisualElement();
            _buttonRow.style.flexDirection = FlexDirection.Row;
            _buttonRow.style.flexWrap = Wrap.Wrap;
            _buttonRow.style.justifyContent = Justify.SpaceBetween;
            _buttonRow.style.flexShrink = 0f;
            _buttonRow.pickingMode = PickingMode.Ignore;
            _card.Add(_buttonRow);
            for (int i = 0; i < _buttons.Length; i++)
            {
                int index = i;
                var button = new Button(() => Click(index));
                button.style.fontSize = 40f;
                button.style.unityTextAlign = TextAnchor.MiddleCenter;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.marginTop = 14f;
                button.style.marginLeft = 0f;
                button.style.marginRight = 0f;
                button.style.marginBottom = 0f;
                Border(button, 3f, Ink, 16f);
                _buttons[i] = button;
                _buttonRow.Add(button);
            }
            Rebuild();
        }

        static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.right = 0f;
            element.style.bottom = 0f;
        }

        static Label Text(float size, Color color, bool bold)
        {
            var label = new Label();
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.flexShrink = 0f;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        static void Border(VisualElement element, float width, Color color, float radius)
        {
            IStyle style = element.style;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftColor = color;
            style.borderRightColor = color;
            style.borderTopColor = color;
            style.borderBottomColor = color;
            style.borderTopLeftRadius = radius;
            style.borderTopRightRadius = radius;
            style.borderBottomLeftRadius = radius;
            style.borderBottomRightRadius = radius;
        }

        // ---- words and buttons --------------------------------------------------------------------------------

        void Rebuild()
        {
            _shownVersion = _flow.Version;
            _textTimer = 0f;
            OnboardingScreens.Fill(_flow, _screen, _text);

            _hidden = _screen.Hidden;
            _root.style.display = _hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (_hidden) return;

            SetText(_title, _screen.Title);
            SetText(_body, _screen.Body);
            SetText(_notice, _screen.Notice);
            _notice.style.display = _screen.Notice.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            bool addressWasShown = _address.style.display == DisplayStyle.Flex;
            _address.style.display = _screen.AddressField ? DisplayStyle.Flex : DisplayStyle.None;
            if (_screen.AddressField && !addressWasShown) _address.value = _flow.Station.Address;

            for (int i = 0; i < _buttons.Length; i++)
            {
                Button button = _buttons[i];
                if (i >= _screen.ButtonCount)
                {
                    button.style.display = DisplayStyle.None;
                    continue;
                }
                OnboardingButton model = _screen.Buttons[i];
                button.style.display = DisplayStyle.Flex;
                if (button.text != model.Label) button.text = model.Label;
                // The way on is one wide, light button; the others sit two to a row.
                button.style.width = new Length(model.Primary ? 100f : 48.5f, LengthUnit.Percent);
                button.style.height = model.Primary ? 112f : 92f;
                button.style.backgroundColor = model.Primary ? Ink : ButtonDark;
                button.style.color = model.Primary ? Color.black : Ink;
            }
        }

        static void SetText(Label label, string text)
        {
            if (label.text != text) label.text = text;
        }

        void Click(int index)
        {
            if (index >= _screen.ButtonCount) return;
            OnboardingAction action = _screen.Buttons[index].Action;
            if (action == OnboardingAction.UseAddress) _flow.SetStationAddress(_address.value);
            else _flow.Do(action);
            Rebuild();
        }

        // ---- taps and drags on the camera picture -------------------------------------------------------------

        // Panel coordinates run from the top left; the scan session takes screen pixels from the bottom left.
        Vector2 ToScreen(Vector3 panelPoint)
        {
            Rect area = _touch.layout;
            float w = math.max(area.width, 1f), h = math.max(area.height, 1f);
            return new Vector2(panelPoint.x / w * Screen.width, (1f - panelPoint.y / h) * Screen.height);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            Vector2 screen = ToScreen(e.position);
            if (_flow.Step == OnboardingStep.Confirm && _flow.Adjusting)
            {
                _dragIndex = PickCorner(screen);
                _dragValid = false;
                if (_dragIndex >= 0) _touch.CapturePointer(e.pointerId);
                return;
            }
            if (_flow.TapScreen(screen.x, screen.y)) Rebuild();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (_dragIndex < 0) return;
            float3 floor;
            if (!_session.TryFloorPoint(ToScreen(e.position), out floor)) return;
            _dragPoint = floor.xz;
            _dragValid = true;
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (_dragIndex < 0) return;
            int index = _dragIndex;
            _dragIndex = -1;
            if (_touch.HasPointerCapture(e.pointerId)) _touch.ReleasePointer(e.pointerId);
            // One correction per drag: the session recomputes the room for it.
            if (_dragValid && _flow.MoveCorner(index, _dragPoint)) Rebuild();
            _dragValid = false;
        }

        int PickCorner(Vector2 screen)
        {
            OutlineProposal proposal = _session.Proposal;
            if (proposal == null || _camera == null) return -1;
            float best = PickRadius * Screen.height;
            int index = -1;
            _dragY = proposal.FloorY;
            for (int i = 0; i < proposal.Count; i++)
            {
                Vector3 world = _session.SessionToWorld(proposal.Outline[i], proposal.FloorY);
                Vector3 p = _camera.WorldToScreenPoint(world);
                if (p.z <= 0f) continue;
                float d = math.distance(new float2(p.x, p.y), new float2(screen.x, screen.y));
                if (d >= best) continue;
                best = d;
                index = i;
            }
            return index;
        }

        // ---- the lines on the floor ---------------------------------------------------------------------------

        void DrawFloor(MeshGenerationContext context)
        {
            if (_camera == null || _session == null) return;
            OnboardingStep step = _flow.Step;
            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            if (step == OnboardingStep.Sweep)
            {
                float y = _session.FloorHeight;
                if (float.IsNaN(y)) return;
                int n = _session.CopyPreviewOutline(_points);
                if (n >= 2) Loop(painter, _points, n, y, OutlineColor, 9f, n >= 3);
                // The corners placed by hand, each as a mark of its own.
                int corners = _session.Flow.CopyCorners(_points);
                for (int i = 0; i < corners; i++) Mark(painter, _points[i], y, LapColor);
            }
            else if (step == OnboardingStep.Confirm)
            {
                OutlineProposal proposal = _session.Proposal;
                if (proposal == null || proposal.Preview == null) return;
                float y = proposal.FloorY;
                Loop(painter, proposal.Outline, proposal.Count, y, OutlineColor, 9f, true);
                proposal.CopyLapSquare(_rect);
                Loop(painter, _rect, 4, y, LapColor, 5f, true);
                proposal.CopyFreeRect(_rect);
                Loop(painter, _rect, 4, y, FreeColor, 8f, true);
                if (!_flow.Adjusting) return;
                for (int i = 0; i < proposal.Count; i++) Mark(painter, proposal.Outline[i], y, i == _dragIndex ? Amber : OutlineColor);
                if (_dragIndex >= 0 && _dragIndex < proposal.Count && _dragValid)
                {
                    // Where the corner would go, with its two new edges.
                    int count = proposal.Count;
                    float2 before = proposal.Outline[(_dragIndex + count - 1) % count];
                    float2 after = proposal.Outline[(_dragIndex + 1) % count];
                    Segment(painter, before, _dragPoint, _dragY, Amber, 6f);
                    Segment(painter, _dragPoint, after, _dragY, Amber, 6f);
                    Mark(painter, _dragPoint, _dragY, Amber);
                }
            }
        }

        void Loop(Painter2D painter, float2[] points, int count, float y, Color color, float width, bool closed)
        {
            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
                Segment(painter, points[i], points[(i + 1) % count], y, color, width);
        }

        // One floor edge, clipped to what is in front of the camera. A dark line under the bright one keeps it
        // readable on a pale floor in daylight.
        void Segment(Painter2D painter, float2 a, float2 b, float y, Color color, float width)
        {
            Vector2 pa, pb;
            if (!Project(_session.SessionToWorld(a, y), _session.SessionToWorld(b, y), out pa, out pb)) return;
            Stroke(painter, pa, pb, Shade, width + 6f);
            Stroke(painter, pa, pb, color, width);
        }

        void Mark(Painter2D painter, float2 point, float y, Color color)
        {
            Vector3 world = _session.SessionToWorld(point, y);
            Vector2 p, unused;
            if (!Project(world, world, out p, out unused)) return;
            const float r = 16f;
            painter.fillColor = Shade;
            Square(painter, p, r + 5f);
            painter.fillColor = color;
            Square(painter, p, r);
        }

        static void Square(Painter2D painter, Vector2 centre, float r)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(centre.x - r, centre.y - r));
            painter.LineTo(new Vector2(centre.x + r, centre.y - r));
            painter.LineTo(new Vector2(centre.x + r, centre.y + r));
            painter.LineTo(new Vector2(centre.x - r, centre.y + r));
            painter.ClosePath();
            painter.Fill();
        }

        static void Stroke(Painter2D painter, Vector2 a, Vector2 b, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.Stroke();
        }

        // World points to panel points, with the part behind the camera cut away.
        bool Project(Vector3 a, Vector3 b, out Vector2 pa, out Vector2 pb)
        {
            pa = pb = Vector2.zero;
            Transform eye = _camera.transform;
            Vector3 origin = eye.position, forward = eye.forward;
            float near = _camera.nearClipPlane + 0.02f;
            float da = Vector3.Dot(a - origin, forward) - near;
            float db = Vector3.Dot(b - origin, forward) - near;
            if (da <= 0f && db <= 0f) return false;
            if (da < 0f) a = Vector3.Lerp(a, b, da / (da - db));
            else if (db < 0f) b = Vector3.Lerp(b, a, db / (db - da));
            pa = ToPanel(_camera.WorldToScreenPoint(a));
            pb = ToPanel(_camera.WorldToScreenPoint(b));
            return true;
        }

        Vector2 ToPanel(Vector3 screen)
        {
            Rect area = _touch.layout;
            float w = math.max(_camera.pixelWidth, 1), h = math.max(_camera.pixelHeight, 1);
            return new Vector2(screen.x / w * area.width, (1f - screen.y / h) * area.height);
        }
    }
}
