using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Atlas.Station
{
    /// <summary>
    /// <c>atlas-hub</c>: <c>dotnet run --project station/hub</c> (Master Plan Appendix C.7). Runs until Ctrl+C
    /// or a terminate signal, then says goodbye to its clients and closes the logs.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            HubOptions options;
            string error;
            if (!HubOptions.TryParse(args, out options, out error))
            {
                Console.Error.WriteLine("atlas-hub: " + error);
                Console.Error.WriteLine();
                Console.Error.Write(HubOptions.Usage);
                return 2;
            }
            if (options.ShowHelp)
            {
                Console.Write(HubOptions.Usage);
                return 0;
            }

            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;   // shut down in order instead of being killed
                done.TrySetResult(true);
            };
            using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                done.TrySetResult(true);
            }))
            {
                var hub = new HubServer(options);
                try
                {
                    await hub.StartAsync();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("atlas-hub: cannot start: " + e.Message);
                    return 1;
                }

                if (!options.Quiet)
                {
                    Console.WriteLine("ATLAS Hub, session " + hub.SessionId);
                    Console.WriteLine("  link     udp " + hub.UdpPort + " (phone, Spectators, Editors)");
                    Console.WriteLine("  console  http://" + hub.StationAddress + ":" + hub.HttpPort + "/");
                    Console.WriteLine("  pairing  http://" + hub.StationAddress + ":" + hub.HttpPort + "/pair   (" + hub.PairUrl(hub.StationAddress) + ")");
                    Console.WriteLine("  logs     " + hub.Log.Folder);
                    Console.WriteLine("Ctrl+C stops the Hub.");
                }

                await done.Task;
                if (!options.Quiet) Console.WriteLine("stopping...");
                await hub.DisposeAsync();
                if (!options.Quiet)
                    Console.WriteLine("stopped: " + hub.StatesReceived + " states, " + hub.StatesLost + " lost, " + hub.TelemetryRows + " telemetry rows");
                return 0;
            }
        }
    }
}
