using System;
using System.IO;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public static class SkyMapsFactory
    {
        public static string FixtureRoot => Path.Combine(Application.streamingAssetsPath, "cases", "illustrative");
        public static SkyMaps Fixture() => SkyMaps.Load(FixtureRoot);

        public static SkyFile Meta(string schema = "atlas.sky/1", float threshold = 5f) => new SkyFile
        {
            schema = schema, width = 512, height = 256, nside = 64, phases = 10, threshold_mm = threshold, mm_per_unit = 0.2f,
            target_mm = new float[] { 10, 20, 30 }, checks = new SkyCheck[0],
        };

        /// <summary>Synthetic maps: every risk byte = riskByte; robust = robustRow(j) ? 255 : 0 per row.</summary>
        public static SkyMaps Synthetic(Func<int, bool> robustRow, byte riskByte = 50)
        {
            var risk = new byte[10][];
            for (int k = 0; k < 10; k++) { risk[k] = new byte[SkyMaps.MapBytes]; for (int n = 0; n < risk[k].Length; n++) risk[k][n] = riskByte; }
            var rob = new byte[SkyMaps.MapBytes];
            for (int j = 0; j < 256; j++) if (robustRow(j)) for (int i = 0; i < 512; i++) rob[j * 512 + i] = 255;
            return new SkyMaps(Meta(), risk, rob);
        }
    }
}
