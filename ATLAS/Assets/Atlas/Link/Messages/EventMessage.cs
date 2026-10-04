// PLACEHOLDER (lane D, P1-D04): contract surface only; replaced by lane D's implementation
namespace Atlas.Link
{
    // Master Plan App. B.2 (Event 0x40). Named EventMessage, not Event, to avoid UnityEngine.Event; mirrors StateMessage.
    public struct EventMessage
    {
        public const byte MsgType = 0x40;
        public long TUs;
        public string Scene;
        public string Ev;
        public string Data;   // JSON object text
    }
}
