using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // IChamberView over kit GameObjects (P2-B02); the renderer and kit placement register per tile at integration (hand-over H-1).
    public sealed class ChamberViewRegistry : IChamberView
    {
        sealed class Entry { public GameObject Opaque, Dithered; public GameObject[] Iris; }
        readonly Dictionary<TileId, Entry> _tiles = new Dictionary<TileId, Entry>();
        Transform _arrow; GameObject _wait;

        public void Register(TileId tile, GameObject opaqueShell, GameObject ditheredShell, GameObject[] irisByEdge)
            => _tiles[tile] = new Entry { Opaque = opaqueShell, Dithered = ditheredShell, Iris = irisByEdge };

        public void SetPromptObjects(Transform arrow, GameObject waitIcon) { _arrow = arrow; _wait = waitIcon; }

        public void SetShell(TileId chamber, ShellState s)
        {
            if (!_tiles.TryGetValue(chamber, out var e)) return;
            if (e.Opaque != null) e.Opaque.SetActive(s == ShellState.Opaque);
            if (e.Dithered != null) e.Dithered.SetActive(s == ShellState.Dithered);
        }

        public void SetIris(TileId chamber, int tileEdge)
        {
            if (!_tiles.TryGetValue(chamber, out var e) || e.Iris == null) return;
            for (int k = 0; k < e.Iris.Length; k++) if (e.Iris[k] != null) e.Iris[k].SetActive(k == tileEdge);
        }

        public void SetTurnPrompt(bool visible, double2 direction, bool wait)
        {
            if (_wait != null) _wait.SetActive(visible && wait);
            if (_arrow == null) return;
            _arrow.gameObject.SetActive(visible && !wait);
            // anchor xz -> Unity yaw (+z is yaw 0, +x is yaw 90)
            if (visible && !wait) _arrow.rotation = Quaternion.Euler(0, (float)(math.degrees(math.atan2(direction.x, direction.y))), 0);
        }
    }
}
