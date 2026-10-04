using System.Text;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Menu
{
    /// <summary>
    /// What the menu draws (lane D contract, section 5.7), built in code under the HUD parent (contract README
    /// F3): one mesh of flat quads in the look's shader for the panel, the focused row and the chips, and five
    /// text objects made the way the HUD makes its own. Six draw calls while open, two while closed. Nothing
    /// here decides anything: it shows the model, and only when the model's revision moved.
    /// </summary>
    sealed class MenuView
    {
        const int FontPixels = 64;
        const float TextHeight = MenuLayout.RowPitch;
        const float ChipWidth = 0.14f, ChipHeight = 0.06f;

        // Cells of the palette's neutral ramp (row 0): the panel, a chip, the focused row, a glyph.
        const int PanelCell = 1, ChipCell = 3, FocusCell = 5, GlyphCell = AtlasPalette.Text;

        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);

        readonly MaterialPool _pool;
        readonly Material _material;
        readonly AtlasMeshBuilder _builder = new AtlasMeshBuilder(64, 128);
        readonly StringBuilder _text = new StringBuilder(512);
        readonly GameObject _panel, _chip;
        readonly Mesh _panelMesh, _chipMesh;
        readonly TextMesh _title, _names, _values;
        readonly Quaternion _chipFacing;

        MenuView(Transform hud, MaterialPool pool)
        {
            _pool = pool;
            _material = pool.Rent();
            // Head-locked content: no fog, and the fade of a space switch does not touch it.
            MaterialPool.SetSurface(_material, 1f, 0f, 0f, 0f);
            int layer = hud.gameObject.layer;

            _panel = NewObject("AtlasMenu", hud, layer);
            _builder.Clear();
            AddRect(0f, 0f, MenuLayout.Width * 0.5f, MenuLayout.Height * 0.5f, PanelCell);
            _panelMesh = AtlasMesh.Create(_builder, "Atlas Menu");
            AddMesh(_panel, _panelMesh);

            // The chip is turned to face the head from its corner.
            _chip = NewObject("AtlasMenuChip", hud, layer);
            float3 corner = MenuLayout.OpenChip;
            _chipFacing = Quaternion.LookRotation(new Vector3(corner.x, corner.y, corner.z));
            _builder.Clear();
            AddRect(0f, 0f, ChipWidth * 0.5f, ChipHeight * 0.5f, ChipCell);
            _chipMesh = AtlasMesh.Create(_builder, "Atlas Menu Chip");
            AddMesh(_chip, _chipMesh);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                float top = MenuLayout.FirstRowY + MenuLayout.RowPitch * 0.5f;
                _title = NewText(_panel.transform, "Title", font, new Vector3(MenuLayout.NamesX, MenuLayout.TitleY, 0f), TextAnchor.MiddleLeft);
                _names = NewText(_panel.transform, "Names", font, new Vector3(MenuLayout.NamesX, top, 0f), TextAnchor.UpperLeft);
                _values = NewText(_panel.transform, "Values", font, new Vector3(MenuLayout.ValuesX, top, 0f), TextAnchor.UpperRight);
                _values.alignment = TextAlignment.Right;
                NewText(_panel.transform, "Close", font, new Vector3(MenuLayout.CloseX, MenuLayout.FooterY, 0f), TextAnchor.MiddleCenter).text = "close";
                NewText(_panel.transform, "Apply", font, new Vector3(MenuLayout.ApplyX, MenuLayout.FooterY, 0f), TextAnchor.MiddleCenter).text = "apply";
                NewText(_chip.transform, "Label", font, Vector3.zero, TextAnchor.MiddleCenter).text = "menu";
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
            if (_chip != null) _chip.transform.SetPositionAndRotation(centre, (Quaternion)frame * _chipFacing);
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
            AddRect(0f, 0f, MenuLayout.Width * 0.5f, MenuLayout.Height * 0.5f, PanelCell);
            float y = MenuLayout.RowY(slot);
            AddRect(0f, y, MenuLayout.RowHalfWidth, MenuLayout.RowHalfHeight, FocusCell);
            if (steps)
            {
                AddRect(MenuLayout.MinusX, y, MenuLayout.StepHalfWidth, MenuLayout.StepHalfHeight, ChipCell);
                AddRect(MenuLayout.MinusX, y, 0.010f, 0.002f, GlyphCell);
            }
            // "+" on a value, a plain mark on a row that runs something.
            AddRect(MenuLayout.PlusX, y, MenuLayout.StepHalfWidth, MenuLayout.StepHalfHeight, ChipCell);
            AddRect(MenuLayout.PlusX, y, 0.010f, 0.002f, GlyphCell);
            if (steps) AddRect(MenuLayout.PlusX, y, 0.002f, 0.010f, GlyphCell);
            AddRect(MenuLayout.CloseX, MenuLayout.FooterY, MenuLayout.ChipHalfWidth, MenuLayout.ChipHalfHeight, ChipCell);
            AddRect(MenuLayout.ApplyX, MenuLayout.FooterY, MenuLayout.ChipHalfWidth, MenuLayout.ChipHalfHeight,
                model.HasPending ? FocusCell : ChipCell);
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

        // A rectangle in the panel's plane that faces the walker: the front of a quad is towards cross(v, u).
        void AddRect(float x, float y, float halfWidth, float halfHeight, int cell)
        {
            _builder.AddQuad(new float3(x - halfWidth, y - halfHeight, 0f), new float3(2f * halfWidth, 0f, 0f),
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

        // The HUD's text: legacy 3D text in the built-in font, sized so that one line is one row of the list.
        static TextMesh NewText(Transform parent, string name, Font font, Vector3 position, TextAnchor anchor)
        {
            GameObject go = NewObject(name, parent, parent.gameObject.layer);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
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
