using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The engine-free parser of a Station address (Master Plan Appendix C.7). Accepted: <c>atlas://&lt;ip&gt;[:port]</c>
    /// (the QR code of the Hub's pairing page), <c>&lt;ip&gt;:&lt;port&gt;</c> and <c>&lt;ip&gt;</c>, where the address is
    /// dotted IPv4. A host name is refused: resolving one would block the calling thread. Written by hand rather
    /// than with <c>System.Uri</c>, so that it behaves the same on every runtime.
    /// </summary>
    public static class PairingAddress
    {
        public const string Scheme = "atlas://";
        public const int DefaultPort = LinkProtocol.DefaultPort;

        /// <summary>True for the three accepted forms. Anything after the port (a path, a query) is ignored.</summary>
        public static bool TryParse(string text, out string host, out int port)
        {
            host = null;
            port = DefaultPort;
            if (text == null) return false;

            int i = 0, end = text.Length;
            while (i < end && char.IsWhiteSpace(text[i])) i++;
            while (end > i && char.IsWhiteSpace(text[end - 1])) end--;
            if (i == end) return false;

            if (StartsWithScheme(text, i, end)) i += Scheme.Length;

            // Four numbers 0..255 of one to three digits, separated by dots.
            int hostStart = i;
            for (int part = 0; part < 4; part++)
            {
                if (part > 0)
                {
                    if (i >= end || text[i] != '.') return false;
                    i++;
                }
                int digits = 0, value = 0;
                while (i < end && text[i] >= '0' && text[i] <= '9' && digits < 4)
                {
                    value = value * 10 + (text[i] - '0');
                    digits++;
                    i++;
                }
                if (digits == 0 || digits > 3 || value > 255) return false;
            }
            int hostEnd = i;

            int parsedPort = DefaultPort;
            if (i < end && text[i] == ':')
            {
                i++;
                int digits = 0, value = 0;
                while (i < end && text[i] >= '0' && text[i] <= '9' && digits < 6)
                {
                    value = value * 10 + (text[i] - '0');
                    digits++;
                    i++;
                }
                if (digits == 0 || digits > 5 || value < 1 || value > 65535) return false;
                parsedPort = value;
            }

            // What follows must be the end or the start of a path, a query or a fragment. A letter or another
            // dot means the text was a host name or a longer number after all.
            if (i < end && text[i] != '/' && text[i] != '?' && text[i] != '#') return false;

            host = text.Substring(hostStart, hostEnd - hostStart);
            port = parsedPort;
            return true;
        }

        static bool StartsWithScheme(string text, int start, int end)
        {
            if (end - start < Scheme.Length) return false;
            for (int k = 0; k < Scheme.Length; k++)
                if (char.ToLowerInvariant(text[start + k]) != Scheme[k]) return false;
            return true;
        }

        /// <summary>The stored form, <c>&lt;ip&gt;:&lt;port&gt;</c>.</summary>
        public static string Format(string host, int port)
        {
            return host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
