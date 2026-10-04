using System;
using System.Globalization;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Session ids: <c>yyyyMMddTHHmmssZ-xxxx</c>, UTC time and four random hexadecimal digits, for example
    /// <c>20261004T153012Z-a3f9</c>. The id names the phone's log files.
    /// </summary>
    public static class SessionId
    {
        public static string Make(DateTime utc, int random)
        {
            return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" +
                   (random & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
        }

        public static string New()
        {
            return Make(DateTime.UtcNow, Guid.NewGuid().GetHashCode());
        }
    }
}
