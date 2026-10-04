using System;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>The steps of the Slicer toybox (Master Plan section 10.1.3, S6 tasks; task P2-A01).</summary>
    public enum SlicerTask : byte
    {
        /// <summary>Push the corner-first tesseract through the cut: tetrahedron, truncated tetrahedron, octahedron.</summary>
        PushThrough = 0,
        /// <summary>See its shadow: look down (standing still), or the shadow button.</summary>
        Shadow = 1,
        /// <summary>Turn it in a plane that contains w.</summary>
        Rotate = 2,
        /// <summary>The same with the 5-cell.</summary>
        Cell5 = 3,
        /// <summary>The same with the 16-cell.</summary>
        Cell16 = 4,
        Done = 5,
    }

    /// <summary>What the tour reads each frame.</summary>
    public struct SlicerTourInput
    {
        public SlicerObject Body;
        public SectionShape Section;
        /// <summary>The cut offset, model units.</summary>
        public float W;
        /// <summary>The three turns, radians.</summary>
        public float Xw, Yw, Zw;
        /// <summary>The shadow is on screen (the shadow view, or a look-down peek).</summary>
        public bool ShadowShown;
        public float DeltaTime;
    }

    /// <summary>
    /// The guided steps of the Slicer (Master Plan section 10.1.3, "slice the tesseract corner-first: tetrahedron, truncated
    /// tetrahedron, octahedron; look down for the shadow; rotate in a plane containing w; repeat on the 5-cell and 16-cell").
    /// Each step says what to do and with which control, and ends when the walker has done it; a step that is already done when
    /// it starts ends at once. Engine-free; no allocation per step.
    /// </summary>
    public sealed class SlicerTour
    {
        /// <summary>Seconds the shadow must stay on to count as seen.</summary>
        public const float ShadowSeconds = 1f;
        /// <summary>Turn, from where the step started, that counts as a rotation: radians (30 degrees).</summary>
        public const float RotateRadians = math.PI / 6f;
        /// <summary>Span of w that counts as having pushed a body through: model units (most of the range of 2.2).</summary>
        public const float SweepSpan = 1.2f;

        int m_Seen;                 // bit per SectionShape seen on the tesseract during step 0
        float m_ShadowTime;
        float3 m_TurnStart;
        float m_WMin, m_WMax;
        SlicerObject m_SweepBody;

        public SlicerTask Task { get; private set; }
        public bool Completed { get { return Task == SlicerTask.Done; } }
        public string Prompt { get { return PromptOf(Task); } }
        /// <summary>The step changed (also on a restart): the Playground's prompt chip follows it.</summary>
        public event Action<SlicerTask> TaskChanged;

        /// <summary>Starts again from the first step.</summary>
        public void Restart()
        {
            Task = SlicerTask.PushThrough;
            m_Seen = 0;
            m_ShadowTime = 0f;
            m_TurnStart = new float3(float.NaN);
            m_WMin = float.PositiveInfinity;
            m_WMax = float.NegativeInfinity;
            m_SweepBody = (SlicerObject)255;
            Action<SlicerTask> changed = TaskChanged;
            if (changed != null) changed(Task);
        }

        public SlicerTour() { Restart(); }

        /// <summary>One frame. True when the step changed.</summary>
        public bool Step(in SlicerTourInput input)
        {
            SlicerTask before = Task;
            // A step that is already done ends at once, so one frame may pass several.
            for (int guard = 0; guard < 6 && Advance(in input); guard++) { }
            if (Task == before) return false;
            Action<SlicerTask> changed = TaskChanged;
            if (changed != null) changed(Task);
            return true;
        }

        bool Advance(in SlicerTourInput input)
        {
            switch (Task)
            {
                case SlicerTask.PushThrough:
                    if (input.Body != SlicerObject.Tesseract) return false;
                    m_Seen |= 1 << (int)input.Section;
                    const int need = (1 << (int)SectionShape.Tetrahedron) | (1 << (int)SectionShape.TruncatedTetrahedron) |
                                     (1 << (int)SectionShape.Octahedron);
                    return (m_Seen & need) == need && Next(SlicerTask.Shadow);

                case SlicerTask.Shadow:
                    m_ShadowTime = input.ShadowShown ? m_ShadowTime + math.max(0f, input.DeltaTime) : 0f;
                    return m_ShadowTime >= ShadowSeconds && Next(SlicerTask.Rotate);

                case SlicerTask.Rotate:
                    var turns = new float3(input.Xw, input.Yw, input.Zw);
                    if (math.any(math.isnan(m_TurnStart))) m_TurnStart = turns;
                    float3 d = math.abs(new float3(SlicerController.WrapAngle(turns.x - m_TurnStart.x),
                                                   SlicerController.WrapAngle(turns.y - m_TurnStart.y),
                                                   SlicerController.WrapAngle(turns.z - m_TurnStart.z)));
                    return math.cmax(d) >= RotateRadians && Next(SlicerTask.Cell5);

                case SlicerTask.Cell5:
                    return Swept(in input, SlicerObject.Cell5) && Next(SlicerTask.Cell16);

                case SlicerTask.Cell16:
                    return Swept(in input, SlicerObject.Cell16) && Next(SlicerTask.Done);

                default:
                    return false;
            }
        }

        // True once the walker has moved the cut over SweepSpan on the given body; the span starts again on any other body.
        bool Swept(in SlicerTourInput input, SlicerObject body)
        {
            if (input.Body != body)
            {
                m_SweepBody = input.Body;
                m_WMin = float.PositiveInfinity;
                m_WMax = float.NegativeInfinity;
                return false;
            }
            if (m_SweepBody != body)
            {
                m_SweepBody = body;
                m_WMin = m_WMax = input.W;
            }
            m_WMin = math.min(m_WMin, input.W);
            m_WMax = math.max(m_WMax, input.W);
            return m_WMax - m_WMin >= SweepSpan;
        }

        bool Next(SlicerTask task)
        {
            Task = task;
            m_ShadowTime = 0f;
            m_TurnStart = new float3(float.NaN);
            m_SweepBody = (SlicerObject)255;
            return true;
        }

        /// <summary>The name of a step, as logged in <c>slicer.task</c>.</summary>
        public static string TaskName(SlicerTask task)
        {
            switch (task)
            {
                case SlicerTask.PushThrough: return "push";
                case SlicerTask.Shadow: return "shadow";
                case SlicerTask.Rotate: return "rotate";
                case SlicerTask.Cell5: return "cell5";
                case SlicerTask.Cell16: return "cell16";
                default: return "done";
            }
        }

        /// <summary>What the walker is asked to do in a step, with the controls that do it.</summary>
        public static string PromptOf(SlicerTask task)
        {
            switch (task)
            {
                case SlicerTask.PushThrough:
                    return "Walk along the line on the floor to push the tesseract through our space. " +
                           "Watch the slice: tetrahedron, truncated tetrahedron, octahedron. (Or: left stick up/down, or look at w+ / w-.)";
                case SlicerTask.Shadow:
                    return "Stand still and look down: the ghost is the tesseract's shadow, all of it at once. (Or: X, or look at 'shadow'.)";
                case SlicerTask.Rotate:
                    return "Turn it in a plane that contains w: look at a turn handle (XW, YW, ZW). " +
                           "The slice changes shape, the tesseract does not. (Or: sticks and triggers.)";
                case SlicerTask.Cell5:
                    return "Next body: the 5-cell, the 4D tetrahedron. Look at 'next body' (or Y), then walk the line.";
                case SlicerTask.Cell16:
                    return "Now the 16-cell, the 4D octahedron. 'Next body' again (or Y), then walk the line.";
                default:
                    return "Three 4D shapes, one 3D slice at a time. Try the 24-cell and the 4D ball, or open the exhibit menu.";
            }
        }
    }
}
