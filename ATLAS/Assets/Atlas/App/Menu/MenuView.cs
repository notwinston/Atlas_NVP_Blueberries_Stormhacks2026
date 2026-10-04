using System.Text;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Menu
{
    /// <summary>
    /// What draws the menu. <see cref="OnDeviceMenu"/> drives it and decides everything; null draws nothing,
    /// and a test puts a recorder in its place.
    /// </summary>
    interface IMenuView
    {
        /// <summary>Open: the panel. Closed: the corner chip.</summary>
        void SetOpen(bool open);
        /// <summary>Stands the panel in the room frame. Called every frame while the menu is open.</summary>
        void PlacePanel(float3 centre, quaternion rotation);
        /// <summary>Stands the chip in the room frame, in the frame given. Called every frame while the menu is closed.</summary>
        void PlaceChip(float3 centre, quaternion frame);
        /// <summary>
        /// Brings the panel and the chip within reach of the hands at this scale, drawn on the world layer and
        /// depth-tested, or puts them back on the head-locked layer at their own size. Called when it changes.
        /// </summary>
        void SetReach(bool near, float scale);
        /// <summary>Redraws the panel from the model. Called when the model's revision moved.</summary>
        void Show(MenuModel model);
        /// <summary>Destroys what the view built and gives its material back to the pool.</summary>
        void Dispose();
    }

    /// <summary>
    /// What the menu draws (lane D contract, section 5.7), built in code under the HUD parent (contract README
    /// F3): one flat mesh in the look's shader (squircle panel, capsule row and chips) and five
    /// text objects made the way the HUD makes its own. Six draw calls while open, two while closed. Nothing
    /// here decides anything: it shows the model, and only when the model's revision moved.
    /// Legacy text spaces its lines by the font's line height, not by the row pitch, and hangs from the top of
    /// its line box, not from its letters; both are measured from the font once, so every line sits on the
    /// centre of its row target and every label on the centre of its chip.
    /// <para>
    /// In a hands session the menu comes within reach (documents/ATLAS_Hands_Design.md section 6.1): the same
    /// objects, scaled as a whole, on the world layer, where the look's shader tests them against depth, and
    /// with the text on <see cref="NearText"/>, so that a finger in front of a row covers the row and its text.
    /// </para>
    /// </summary>
    sealed class MenuView : IMenuView
    {
        const int FontPixels = 64;
        /// <summary>One em of the text: the capitals fill about half of a row's height.</summary>
        const float TextHeight = MenuLayout.RowPitch * 0.8f;
        /// <summary>Layers in front of the panel, towards the walker (-z), so no two surfaces share a depth.</summary>
        const float Layer = 0.001f;
        const float MarkZ = -Layer, GlyphZ = -2f * Layer, TextZ = -3f * Layer;
        const float ChipWidth = 0.14f, ChipHeight = 0.06f;

        // Cells of the palette's neutral ramp (row 0): the panel, its glass rim, a chip, the focused row, a glyph.
        const int PanelCell = 1, RimCell = 4, ChipCell = 3, FocusCell = 5, GlyphCell = AtlasPalette.Text;
        /// <summary>The panel's corner radius; rows and chips inside are capsules, so the curves stay concentric.</summary>
        const float PanelRadius = 0.045f;
        /// <summary>How far the rim shows round a squircle, behind it: the lit edge of a pane of glass.</summary>
        const float Rim = 0.003f;

        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);

        readonly MaterialPool _pool;
        readonly Material _material;
        readonly AtlasMeshBuilder _builder = new AtlasMeshBuilder(512, 1536);
        readonly StringBuilder _text = new StringBuilder(512);
        readonly GameObject _panel, _chip;
        readonly Mesh _panelMesh, _chipMesh;
        readonly TextMesh _title, _names, _values;
        readonly Quaternion _chipFacing;
        // Every text of the panel and of the chip: what changes material when the menu comes within reach.
        // Names, Values, Title, Close, Apply, Rescan and the chip's Label.
        readonly MeshRenderer[] _texts = new MeshRenderer[7];
        readonly Font _font;
        readonly int _hudLayer;
        int _textCount;
        bool _near;
        float _scale = 1f;

        MenuView(Transform hud, MaterialPool pool)
        {
            _pool = pool;
            _material = pool.Rent();
            // Head-locked content: no fog, and the fade of a space switch does not touch it.
            MaterialPool.SetSurface(_material, 1f, 0f, 0f, 0f);
            int layer = hud.gameObject.layer;
            _hudLayer = layer;

            _panel = NewObject("AtlasMenu", hud, layer);
            _builder.Clear();
            AddPanel();
            _panelMesh = AtlasMesh.Create(_builder, "Atlas Menu");
            AddMesh(_panel, _panelMesh);

            // The chip is turned to face the head from its corner.
            _chip = NewObject("AtlasMenuChip", hud, layer);
            float3 corner = MenuLayout.OpenChip;
            _chipFacing = Quaternion.LookRotation(new Vector3(corner.x, corner.y, corner.z));
            _builder.Clear();
            AddRimmed(0f, 0f, ChipWidth * 0.5f, ChipHeight * 0.5f, ChipHeight * 0.5f, ChipCell);
            _chipMesh = AtlasMesh.Create(_builder, "Atlas Menu Chip");
            AddMesh(_chip, _chipMesh);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _font = font;
            if (font != null)
            {
                _names = NewText(_panel.transform, "Names", font, Vector3.zero, TextAnchor.UpperLeft);
                float lineSpacing, capCentre;
                Measure(_names, out lineSpacing, out capCentre);
                // Every text hangs from its top: a line's capitals are centred capCentre (negative) from where it stands.
                float rows = MenuLayout.RowY(0) - capCentre;
                _names.transform.localPosition = new Vector3(MenuLayout.NamesX, rows, TextZ);
                _names.lineSpacing = lineSpacing;
                _values = NewText(_panel.transform, "Values", font, new Vector3(MenuLayout.ValuesX, rows, TextZ), TextAnchor.UpperRight);
                _values.alignment = TextAlignment.Right;
                _values.lineSpacing = lineSpacing;
                _title = NewText(_panel.transform, "Title", font,
                    new Vector3(MenuLayout.NamesX, MenuLayout.TitleY - capCentre, TextZ), TextAnchor.UpperLeft);
                float footer = MenuLayout.FooterY - capCentre;
                NewText(_panel.transform, "Close", font, new Vector3(MenuLayout.CloseX, footer, TextZ), TextAnchor.UpperCenter).text = "Close";
                NewText(_panel.transform, "Apply", font, new Vector3(MenuLayout.ApplyX, footer, TextZ), TextAnchor.UpperCenter).text = "Apply";
                NewText(_panel.transform, "Rescan", font, new Vector3(MenuLayout.RescanX, MenuLayout.RescanY - capCentre, TextZ), TextAnchor.UpperCenter).text = "Rescan";
                NewText(_chip.transform, "Label", font, new Vector3(0f, -capCentre, TextZ), TextAnchor.UpperCenter).text = "Menu";
            }

            _panel.SetActive(false);
            _chip.SetActive(true);
        }

        /// <summary>Builds the objects under the HUD parent with one material from the pool.</summary>
        public static MenuView Create(Transform hud, MaterialPool pool)
        {
            return new MenuView(hud, pool);
        }

        /// <summary>Open: the panel. Closed: the corner chip.</summary>
        public void SetOpen(bool open)
        {
            if (_panel == null) return;
            _panel.SetActive(open);
            _chip.SetActive(!open);
        }

        public void PlacePanel(float3 centre, quaternion rotation)
        {
            if (_panel != null) _panel.transform.SetPositionAndRotation(centre, rotation);
        }

        public void PlaceChip(float3 centre, quaternion frame)
        {
            if (_chip == null) return;
            // A metre away the chip is turned to face the head from its corner; within reach it lies in the
            // anchor's plane, which already faces the head.
            _chip.transform.SetPositionAndRotation(centre, _near ? (Quaternion)frame : (Quaternion)frame * _chipFacing);
        }

        public void SetReach(bool near, float scale)
        {
            if (_panel == null) return;
            float size = near ? scale : 1f;
            if (near == _near && size == _scale) return;
            _near = near;
            _scale = size;

            int layer = near ? PortalStencilTable.WorldLayer : _hudLayer;
            SetLayer(_panel.transform, layer);
            SetLayer(_chip.transform, layer);
            // The texts are children: they are scaled with the panel and the chip, and stay on their rows.
            var whole = new Vector3(size, size, size);
            _panel.transform.localScale = whole;
            _chip.transform.localScale = whole;

            if (_font == null) return;
            Material text = near ? NearText.For(_font) : null;
            if (text == null) text = _font.material;
            for (int i = 0; i < _textCount; i++) _texts[i].sharedMaterial = text;
        }

        // A child does not take its parent's layer: the object and each text under it are set one by one.
        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) root.GetChild(i).gameObject.layer = layer;
        }

        /// <summary>
        /// Redraws the panel from the model: three strings and one small mesh. Called when the model's revision
        /// moved, so a still menu costs nothing here.
        /// </summary>
        public void Show(MenuModel model)
        {
            if (_panel == null) return;
            int slot = model.Selected - model.Top;
            MenuRowKind kind = model.Row(model.Selected).Kind;
            bool steps = kind == MenuRowKind.Param || kind == MenuRowKind.Page;

            _builder.Clear();
            AddPanel();
            float y = MenuLayout.RowY(slot);
            AddCapsule(0f, y, MarkZ, MenuLayout.RowHalfWidth, MenuLayout.RowHalfHeight, FocusCell);
            // The step chips stand in front of the focused row, as their gaze targets do.
            if (steps)
            {
                AddCapsule(MenuLayout.MinusX, y, GlyphZ, MenuLayout.StepHalfWidth, MenuLayout.StepHalfHeight, ChipCell);
                AddRect(MenuLayout.MinusX, y, TextZ, 0.010f, 0.002f, GlyphCell);
            }
            // "+" on a value, a plain mark on a row that runs something; none on a space, which Apply enters.
            if (kind != MenuRowKind.Space)
            {
                AddCapsule(MenuLayout.PlusX, y, GlyphZ, MenuLayout.StepHalfWidth, MenuLayout.StepHalfHeight, ChipCell);
                AddRect(MenuLayout.PlusX, y, TextZ, 0.010f, 0.002f, GlyphCell);
                if (steps) AddRect(MenuLayout.PlusX, y, TextZ, 0.002f, 0.010f, GlyphCell);
            }
            AddCapsule(MenuLayout.CloseX, MenuLayout.FooterY, MarkZ, MenuLayout.ChipHalfWidth, MenuLayout.ChipHalfHeight, ChipCell);
            AddCapsule(MenuLayout.RescanX, MenuLayout.RescanY, MarkZ, MenuLayout.RescanHalfWidth, MenuLayout.RescanHalfHeight, ChipCell);
            AddCapsule(MenuLayout.ApplyX, MenuLayout.FooterY, MarkZ, MenuLayout.ChipHalfWidth, MenuLayout.ChipHalfHeight,
                model.ApplyReady ? FocusCell : ChipCell);
            AtlasMesh.Apply(_builder, _panelMesh);

            if (_title == null) return;
            // Legacy 3D text takes a string: three small allocations per change, none while nothing changes.
            MenuText.BuildTitle(_text, model);
            _title.text = _text.ToString();
            MenuText.BuildNames(_text, model);
            _names.text = _text.ToString();
            MenuText.BuildValues(_text, model);
            _values.text = _text.ToString();
        }

        public void Dispose()
        {
            AppObjects.Kill(_panel);
            AppObjects.Kill(_chip);
            AppObjects.Kill(_panelMesh);
            AppObjects.Kill(_chipMesh);
            if (_material != null && _pool.Owns(_material)) _pool.Return(_material);
        }

        void AddPanel()
        {
            AddRimmed(0f, MenuLayout.PanelY, MenuLayout.Width * 0.5f, MenuLayout.Height * 0.5f, PanelRadius, PanelCell);
        }

        // A squircle with its rim showing round it, the rim one layer behind.
        void AddRimmed(float x, float y, float halfWidth, float halfHeight, float radius, int cell)
        {
            _builder.AddSquircle(new float3(x, y, Layer), halfWidth + Rim, halfHeight + Rim, radius + Rim, AtlasPalette.Uv(RimCell));
            _builder.AddSquircle(new float3(x, y, 0f), halfWidth, halfHeight, radius, AtlasPalette.Uv(cell));
        }

        void AddCapsule(float x, float y, float z, float halfWidth, float halfHeight, int cell)
        {
            _builder.AddSquircle(new float3(x, y, z), halfWidth, halfHeight, halfHeight, AtlasPalette.Uv(cell));
        }

        // A rectangle in the panel's plane that faces the walker: the front of a quad is towards cross(v, u).
        void AddRect(float x, float y, float z, float halfWidth, float halfHeight, int cell)
        {
            _builder.AddQuad(new float3(x - halfWidth, y - halfHeight, z), new float3(2f * halfWidth, 0f, 0f),
                new float3(0f, 2f * halfHeight, 0f), AtlasPalette.Uv(cell));
        }

        static GameObject NewObject(string name, Transform parent, int layer)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        void AddMesh(GameObject go, Mesh mesh)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// The line spacing that puts one line on each row, and how far below its top a line's capitals are
        /// centred, from the glyphs the font really draws: the difference between one line and two is one
        /// line's advance. Leaves the text empty.
        /// </summary>
        static void Measure(TextMesh text, out float lineSpacing, out float capCentre)
        {
            var renderer = text.GetComponent<MeshRenderer>();
            text.lineSpacing = 1f;
            text.text = "H";
            Bounds one = renderer.localBounds;
            text.text = "H\nH";
            float advance = renderer.localBounds.size.y - one.size.y;
            text.text = string.Empty;
            // Nothing drawn (no font texture yet): the font's own line height of about 1.15 em.
            if (advance <= 0f || one.size.y <= 0f)
            {
                lineSpacing = MenuLayout.RowPitch / (1.15f * TextHeight);
                capCentre = -0.55f * TextHeight;
                return;
            }
            lineSpacing = MenuLayout.RowPitch / advance;
            capCentre = one.center.y;
        }

        // The HUD's text: legacy 3D text in the built-in font, one em of TextHeight.
        TextMesh NewText(Transform parent, string name, Font font, Vector3 position, TextAnchor anchor)
        {
            GameObject go = NewObject(name, parent, parent.gameObject.layer);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            _texts[_textCount++] = renderer;
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            text.font = font;
            text.fontSize = FontPixels;
            // A line of legacy 3D text is characterSize * fontSize / 10 units tall.
            text.characterSize = TextHeight * 10f / FontPixels;
            text.anchor = anchor;
            text.alignment = TextAlignment.Left;
            text.richText = false;
            text.color = s_text;
            return text;
        }
    }
}
