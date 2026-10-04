using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>
    /// How far ahead the gaze reticle stands (documents/ATLAS_Hands_Design.md, decision R10). It used to stand
    /// 2 m ahead always. Over a panel within reach that is seen double: the eyes converge on the panel 0.45 m
    /// away, and a dot drawn for 2 m then lies 6 degrees apart in the two eyes. So while the focused gaze target
    /// is nearer than 2 m the reticle stands at that target's distance, never nearer than 0.35 m, and otherwise
    /// at 2 m as before. It is scaled with its distance, so its size in the view does not change.
    /// Engine-free (README F2): the reticle's object in the scene only applies what this says.
    /// </summary>
    internal struct ReticleDepth
    {
        /// <summary>Where the reticle stands with nothing near in focus, metres: the distance its mesh is drawn for.</summary>
        public const float FarMetres = 2.0f;
        /// <summary>It never comes nearer than this, metres: closer, the eyes cannot hold it single for long.</summary>
        public const float NearestMetres = 0.35f;
        /// <summary>A change of distance is eased, and is over within this long, seconds.</summary>
        public const float EaseSeconds = 0.15f;

        // What the eyes do between two distances is a change of angle, which is linear in one over the distance:
        // the reticle moves at a steady rate in that measure, fast enough to cross its whole range in EaseSeconds.
        private const float Rate = (1f / NearestMetres - 1f / FarMetres) / EaseSeconds;

        private float _inverse;     // one over the distance; 0 until the first step

        /// <summary>Where the reticle stands now, metres.</summary>
        public float Metres { get { return _inverse > 0f ? 1f / _inverse : FarMetres; } }

        /// <summary>The distance a focus asks for: the target's own while it is nearer than 2 m, never under 0.35 m; otherwise 2 m. focusMetres is 0 with nothing in focus.</summary>
        public static float Wanted(float focusMetres)
        {
            return focusMetres > 0f && focusMetres < FarMetres ? math.max(focusMetres, NearestMetres) : FarMetres;
        }

        /// <summary>The scale of the reticle's mesh, which is drawn for 2 m, at a distance: its angle in the view stays the same.</summary>
        public static float Scale(float metres)
        {
            return metres / FarMetres;
        }

        /// <summary>One frame: eases towards the distance the focus asks for and returns where the reticle stands.</summary>
        public float Step(float focusMetres, float deltaTime)
        {
            float want = 1f / Wanted(focusMetres);
            float have = _inverse > 0f ? _inverse : 1f / FarMetres;
            float step = Rate * (deltaTime > 0f ? deltaTime : 0f);
            _inverse = math.abs(want - have) <= step ? want : have + math.sign(want - have) * step;
            return Metres;
        }

        /// <summary>Back to 2 m at once: the reticle was hidden.</summary>
        public float Reset()
        {
            _inverse = 0f;
            return FarMetres;
        }
    }
}
