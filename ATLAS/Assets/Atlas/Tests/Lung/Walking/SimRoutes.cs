using System;

namespace Atlas.Blades.Lung.Walking.Tests
{
    static class SimRoutes
    {
        // G6 §4: 100 four-letter addresses from System.Random(2101); same list for B03, B04 and B05.
        public static string[] Hundred()
        {
            var r = new Random(2101);
            var list = new string[100];
            for (int i = 0; i < 100; i++)
            {
                var c = new char[4];
                for (int k = 0; k < 4; k++) c[k] = r.Next(2) == 0 ? 'L' : 'R';
                list[i] = new string(c);
            }
            return list;
        }
    }
}
