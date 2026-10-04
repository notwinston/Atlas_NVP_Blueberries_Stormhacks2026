using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using QRCoder;

namespace Atlas.Station
{
    /// <summary>
    /// The pairing page (Master Plan section 7.1 and Appendix C.7): a QR code of <c>atlas://&lt;ip&gt;:9050</c>
    /// drawn on the server as inline SVG, read by the iOS Camera app, with the address in plain text below it
    /// for the typed-IP fallback. Nothing is loaded from outside the Station: the router has no internet.
    /// </summary>
    public static class PairPage
    {
        /// <summary>The QR code of <paramref name="text"/> as its module matrix, quiet zone included.</summary>
        public static bool[,] Modules(string text)
        {
            using (var generator = new QRCodeGenerator())
            using (QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
            {
                List<BitArray> rows = data.ModuleMatrix;
                int n = rows.Count;
                var modules = new bool[n, n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++) modules[y, x] = rows[y][x];
                return modules;
            }
        }

        /// <summary>The QR code as a self-contained SVG: one path of dark modules on a white square.</summary>
        public static string Svg(string text, int pixelsPerModule = 8)
        {
            bool[,] m = Modules(text);
            int n = m.GetLength(0);
            int size = n * pixelsPerModule;
            var sb = new StringBuilder(4096);
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"QR code\" width=\"").Append(size)
              .Append("\" height=\"").Append(size).Append("\" viewBox=\"0 0 ").Append(n).Append(' ').Append(n)
              .Append("\" shape-rendering=\"crispEdges\"><rect width=\"").Append(n).Append("\" height=\"").Append(n)
              .Append("\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
            for (int y = 0; y < n; y++)
            {
                int x = 0;
                while (x < n)
                {
                    if (!m[y, x]) { x++; continue; }
                    int start = x;
                    while (x < n && m[y, x]) x++;
                    // One horizontal run of dark modules.
                    sb.Append('M').Append(start.ToString(CultureInfo.InvariantCulture)).Append(' ')
                      .Append(y.ToString(CultureInfo.InvariantCulture)).Append('h')
                      .Append((x - start).ToString(CultureInfo.InvariantCulture)).Append("v1h-")
                      .Append((x - start).ToString(CultureInfo.InvariantCulture)).Append('z');
                }
            }
            sb.Append("\"/></svg>");
            return sb.ToString();
        }

        public static string Html(string pairUrl, string address, int udpPort, int httpPort, List<string> otherAddresses, bool phoneConnected = false)
        {
            var sb = new StringBuilder(8192);
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
              .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>ATLAS pairing</title><style>")
              .Append("body{font:16px/1.5 system-ui,sans-serif;margin:0;background:#101418;color:#e8edf2;display:flex;min-height:100vh;align-items:center;justify-content:center}")
              .Append("main{max-width:30rem;padding:2rem;text-align:center}h1{font-size:1.4rem;margin:0 0 1rem}")
              .Append(".qr{display:inline-block;background:#fff;padding:12px;border-radius:8px}.qr svg{display:block;width:min(70vw,320px);height:auto}")
              .Append("code{font:1.1rem ui-monospace,Consolas,monospace;background:#1c232b;padding:.2rem .5rem;border-radius:4px}")
              .Append("p{margin:1rem 0}small{color:#93a1b0}a{color:#7cc4ff}</style></head><body><main>")
              .Append("<h1>Pair a phone with this Station</h1><div class=\"qr\">").Append(Svg(pairUrl)).Append("</div>")
              .Append("<p><code id=\"pair-url\">").Append(WebUtility.HtmlEncode(pairUrl)).Append("</code></p>")
              .Append("<p>Point the iPhone Camera at the code and open the link. ATLAS stores the address and connects.</p>")
              .Append("<p id=\"phone\">").Append(phoneConnected ? "A phone is connected." : "No phone is connected yet.").Append("</p>")
              .Append("<p><small>No camera? Type <code>").Append(WebUtility.HtmlEncode(address)).Append("</code> into the app's link screen (\"Change IP\"). ")
              .Append("Hub UDP port ").Append(udpPort).Append(", Console on port ").Append(httpPort).Append(".");
            if (otherAddresses != null && otherAddresses.Count > 0)
            {
                sb.Append(" Other addresses of this Station: ");
                for (int i = 0; i < otherAddresses.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("<a href=\"/pair?ip=").Append(WebUtility.UrlEncode(otherAddresses[i])).Append("\">")
                      .Append(WebUtility.HtmlEncode(otherAddresses[i])).Append("</a>");
                }
                sb.Append('.');
            }
            sb.Append("</small></p><p><a href=\"/\">Console</a></p></main></body></html>");
            return sb.ToString();
        }
    }
}
