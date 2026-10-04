using System;
using System.Collections.Generic;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Meshes a client owns that the room content has been given a replacement for. Each is destroyed once, and
    /// only when nothing draws it any more: the room content starts one swap per frame and then fades the old
    /// mesh out, so a fixed wait can destroy a mesh that is still on a renderer. Engine-free.
    /// </summary>
    public sealed class RetiredMeshes<T> where T : class
    {
        readonly List<T> _items = new List<T>();

        public int Count { get { return _items.Count; } }

        public void Add(T mesh)
        {
            if (mesh != null) _items.Add(mesh);
        }

        /// <summary>Destroys, once, every mesh that is not drawn now. The others wait for the next sweep.</summary>
        public void Sweep(Func<T, bool> isDrawn, Action<T> destroy)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (isDrawn(_items[i])) continue;
                T mesh = _items[i];
                _items.RemoveAt(i);
                destroy(mesh);
            }
        }

        /// <summary>Destroys every mesh at once, drawn or not (shutdown).</summary>
        public void DestroyAll(Action<T> destroy)
        {
            for (int i = 0; i < _items.Count; i++) destroy(_items[i]);
            _items.Clear();
        }
    }
}
