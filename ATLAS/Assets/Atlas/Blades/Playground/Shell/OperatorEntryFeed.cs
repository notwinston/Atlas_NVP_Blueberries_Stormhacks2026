using System;

namespace Atlas.Blades.Playground
{
    /// <summary>What the operator typed into the Console.</summary>
    public enum OperatorEntryKind : byte
    {
        /// <summary>Comfort score, 0 to 20, asked once a minute (Master Plan section 10.1.4).</summary>
        Fms = 0,
        /// <summary>Single Ease Question, 1 to 7, asked once per use case (section 11.2).</summary>
        Seq = 1
    }

    /// <summary>One Console entry that reached the phone.</summary>
    public struct OperatorEntry
    {
        public OperatorEntryKind Kind;
        public int Value;
    }

    /// <summary>
    /// Lane C's port for the Console's FMS and SEQ fields. The Hub writes its own <c>fms</c> line when the operator
    /// submits (lane D contract section 3.5) and does not send it to the phone, so something at integration has
    /// to carry the number back: lane D's link glue implements this interface (an <c>OperatorCmd</c> or a new
    /// message is their decision) and the shell binds it. Until then <see cref="Raise"/> lets a test, the Editor
    /// or a keyboard stand-in feed entries by hand.
    /// </summary>
    public interface IOperatorEntryFeed
    {
        /// <summary>Raised on the main thread for each entry.</summary>
        event Action<OperatorEntry> Entered;
    }

    /// <summary>A feed that is raised by hand: the stand-in used until the link carries the entries.</summary>
    public sealed class ManualOperatorEntryFeed : IOperatorEntryFeed
    {
        public event Action<OperatorEntry> Entered;

        public void Raise(OperatorEntryKind kind, int value)
        {
            Action<OperatorEntry> handler = Entered;
            if (handler != null) handler(new OperatorEntry { Kind = kind, Value = value });
        }
    }
}
