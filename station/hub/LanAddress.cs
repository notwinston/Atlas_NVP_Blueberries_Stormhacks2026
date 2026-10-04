using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Atlas.Station
{
    /// <summary>
    /// Finds the address a phone on the Station's router should dial (Master Plan section 7.1): unicast to a
    /// known IPv4 address, by default the DHCP-reserved <c>192.168.x.10</c> of Appendix C.7.
    /// </summary>
    public static class LanAddress
    {
        /// <summary>Every IPv4 address of an interface that is up, private ranges first, loopback left out.</summary>
        public static List<string> Candidates()
        {
            var found = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        IPAddress ip = info.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                        string text = ip.ToString();
                        if (text.StartsWith("169.254.", StringComparison.Ordinal)) continue;   // link-local: no router handed it out
                        if (!found.Contains(text)) found.Add(text);
                    }
                }
            }
            catch (NetworkInformationException) { }
            catch (PlatformNotSupportedException) { }
            found.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return found;
        }

        /// <summary>The address to advertise: the one given, else the best candidate, else loopback.</summary>
        public static string Pick(string advertise)
        {
            if (!string.IsNullOrEmpty(advertise)) return advertise;
            List<string> candidates = Candidates();
            return candidates.Count > 0 ? candidates[0] : "127.0.0.1";
        }

        // A home router's 192.168 range first, then the other private ranges, then anything else.
        static int Rank(string ip)
        {
            if (ip.StartsWith("192.168.", StringComparison.Ordinal)) return 0;
            if (ip.StartsWith("10.", StringComparison.Ordinal)) return 1;
            if (ip.StartsWith("172.", StringComparison.Ordinal)) return 2;
            return 3;
        }
    }

    /// <summary>
    /// Asks Windows for a 1 ms timer while the Hub runs. Without it <c>Thread.Sleep(1)</c> lasts up to 15.6 ms
    /// there, and the relay could not stay under its 2 ms budget (task P1-D02). Other systems need nothing.
    /// </summary>
    public static class TimerResolution
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        static extern uint TimeEndPeriod(uint milliseconds);

        sealed class Raised : IDisposable
        {
            bool _done;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                try { TimeEndPeriod(1); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }
        }

        /// <summary>Returns a handle that gives the resolution back, or null where nothing had to be done.</summary>
        public static IDisposable Raise()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                return TimeBeginPeriod(1) == 0 ? new Raised() : null;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }
    }
}
