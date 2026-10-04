// Reads a case-contract file with the C# reader and re-writes it with the C# writer.
// Used by tests/test_p2_d01_contract.py to prove Python -> C# -> bytes is byte-identical.
//
//   Atlas.Cases.RoundTrip <tube4d|table|manifest|json> <input> <output>
//   Atlas.Cases.RoundTrip verify <case directory>
using System;
using System.IO;

namespace Atlas.Cases.RoundTrip
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "verify")
                {
                    var man = CaseManifest.Read(File.ReadAllBytes(Path.Combine(args[1], CaseManifest.FileName)));
                    var problems = man.Verify(args[1]);
                    foreach (string p in problems) Console.Error.WriteLine(p);
                    Console.WriteLine(problems.Count == 0 ? "ok " + man.Files.Count + " files" : "FAILED");
                    return problems.Count == 0 ? 0 : 1;
                }
                if (args.Length != 3)
                {
                    Console.Error.WriteLine("usage: Atlas.Cases.RoundTrip <tube4d|table|manifest|json> <input> <output>");
                    Console.Error.WriteLine("       Atlas.Cases.RoundTrip verify <case directory>");
                    return 2;
                }
                byte[] input = File.ReadAllBytes(args[1]);
                byte[] output;
                switch (args[0])
                {
                    case "tube4d": output = Tube4D.Read(input).Write(); break;
                    case "table": output = CaseTable.Read(input).Write(); break;
                    case "manifest": output = CaseManifest.Read(input).Write(); break;
                    case "json": output = CanonicalJson.Write(CanonicalJson.Parse(input)); break;
                    default:
                        Console.Error.WriteLine("unknown kind " + args[0]);
                        return 2;
                }
                File.WriteAllBytes(args[2], output);
                return 0;
            }
            catch (ContractException e)
            {
                Console.Error.WriteLine("contract error: " + e.Message);
                return 1;
            }
        }
    }
}
