using UnityEngine;

namespace Atlas.App
{
    /// <summary>Destroys engine objects the same way in play mode and in an edit-mode test.</summary>
    static class AppObjects
    {
        public static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
