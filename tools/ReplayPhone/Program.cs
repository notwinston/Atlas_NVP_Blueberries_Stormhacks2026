using System;
using System.Threading;

namespace Atlas.Tools
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            ReplayOptions options;
            string error;
            if (!ReplayOptions.TryParse(args, out options, out error))
            {
                Console.Error.WriteLine("ReplayPhone: " + error);
                Console.Error.WriteLine();
                Console.Error.Write(ReplayOptions.Usage);
                return 64;
            }
            if (options.ShowHelp)
            {
                Console.Write(ReplayOptions.Usage);
                return 0;
            }

            using (var stop = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;   // say Bye to the Hub before leaving
                    stop.Cancel();
                };
                return new ReplayPlayer(options).Run(stop.Token);
            }
        }
    }
}
