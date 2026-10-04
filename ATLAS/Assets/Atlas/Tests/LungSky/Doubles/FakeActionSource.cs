using Atlas.Input;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public sealed class FakeActionSource : IActionSource
    {
        public bool ConfirmNext; public Vector2 StickValue;
        public bool Triggered(AtlasAction action)
        {
            if (action != AtlasAction.Confirm || !ConfirmNext) return false;
            ConfirmNext = false; return true;
        }
        public Vector2 Stick => StickValue;
    }
}
