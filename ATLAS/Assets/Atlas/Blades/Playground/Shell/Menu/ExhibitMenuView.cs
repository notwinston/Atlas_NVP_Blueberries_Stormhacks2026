using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// Draws the signs of an <see cref="ExhibitMenu"/> in Home: one board while the menu is closed, five signs while
    /// it is open, the selected one lit. Built in code under the room's root so the signs stand where the
    /// layout puts them in the room frame. Redraws only when the menu's revision moved.
    /// </summary>
    public sealed class ExhibitMenuView
    {
        const float LineHeight = 0.05f;
        static readonly float3 s_lit = new float3(1.0f, 0.85f, 0.35f);
        static readonly float3 s_dim = new float3(0.55f, 0.6f, 0.65f);
        static readonly float3 s_plain = new float3(1f, 1f, 1f);

        readonly ExhibitMenu _menu;
        readonly HudPanel _board;
        readonly HudPanel[] _signs = new HudPanel[ExhibitSignLayout.Count];
        int _shownRevision = -1;

        ExhibitMenuView(ExhibitMenu menu, RoomModel room, Transform roomRoot, MaterialPool pool)
        {
            _menu = menu;
            float hw = ExhibitSignLayout.HalfWidth, hh = ExhibitSignLayout.HalfHeight;
            _board = new HudPanel("PlaygroundExhibitBoard", roomRoot, pool, ExhibitSignLayout.BoardCentre(room), hw, hh,
                                  LineHeight, TextAnchor.MiddleCenter, 0f, false);
            Face(_board, ExhibitSignLayout.BoardCentre(room));
            _board.SetText("Exhibits");
            for (int k = 0; k < _signs.Length; k++)
            {
                float3 c = ExhibitSignLayout.Centre(room, k);
                _signs[k] = new HudPanel("PlaygroundExhibitSign" + k, roomRoot, pool, c, hw, hh, LineHeight,
                                         TextAnchor.MiddleCenter, 0f, false);
                Face(_signs[k], c);
            }
        }

        public static ExhibitMenuView Create(ExhibitMenu menu, RoomModel room, Transform roomRoot, MaterialPool pool)
        {
            if (menu == null) throw new System.ArgumentNullException(nameof(menu));
            if (room == null) throw new System.ArgumentNullException(nameof(room));
            if (roomRoot == null) throw new System.ArgumentNullException(nameof(roomRoot));
            return new ExhibitMenuView(menu, room, roomRoot, pool);
        }

        /// <summary>Once per frame; draws only when the menu changed.</summary>
        public void Update()
        {
            if (_menu.Revision == _shownRevision) return;
            _shownRevision = _menu.Revision;
            bool open = _menu.IsOpen;
            _board.SetAlpha(open ? 0f : 1f);
            for (int k = 0; k < _signs.Length; k++)
            {
                bool ready = _menu.IsReady((ExhibitId)k);
                bool on = k == (int)ExhibitId.Network && AtlasServices.TryGet(out IHomeExhibit net) && net.Active;
                _signs[k].SetText(!ready ? ExhibitMenu.Label(k) + "\n(not ready)" : on ? ExhibitMenu.Label(k) + "\n(on)" : ExhibitMenu.Label(k));
                _signs[k].SetTint(k == _menu.Selected ? s_lit : ready ? s_plain : s_dim);
                _signs[k].SetAlpha(open ? 1f : 0f);
            }
        }

        public void Dispose()
        {
            _board.Dispose();
            for (int k = 0; k < _signs.Length; k++) _signs[k].Dispose();
        }

        // The sign's front (-Z) looks at the middle of the arc, where the walker stands.
        static void Face(HudPanel panel, float3 centre)
        {
            Transform t = panel.Transform;
            if (t == null) return;
            var outward = new Vector3(centre.x, 0f, centre.z);
            if (outward.sqrMagnitude > 1e-6f) t.localRotation = Quaternion.LookRotation(outward, Vector3.up);
        }
    }
}
