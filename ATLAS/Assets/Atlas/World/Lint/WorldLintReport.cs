using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Atlas.World.Lint
{
    public sealed class WorldLintReport
    {
        public bool Passed => Failures.Count == 0;
        public readonly List<string> Failures = new List<string>();
        public uint Seed; public double L; public int TilesLinted;
        public int Unreachable; public int MaxClosedEdges; public double WorstTowerDistance;
        public int TilesMissingWedges; public int StartSpanPairs; public double StartSpanFarCentre;
        public int MaxDrawnTiles, MaxTileDraws, MaxDraws; public long MaxTriangles; public string WorstPose;
        public int CoincidentFaces, StructuralConflicts, RegionMismatches, ContentMismatches, RegionsPresent;
        public int CorridorsMissingKerb; public int Corridors, CorridorsTwoClosed, CorridorsOneClosed, CorridorKerbs;
        public ulong WorldHash1, WorldHash2;

        public override string ToString()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"WorldLint seed={Seed} L={L.ToString("F4", c)} passed={Passed}");
            sb.AppendLine($"  TilesLinted={TilesLinted}");
            sb.AppendLine($"  Unreachable={Unreachable} MaxClosedEdges={MaxClosedEdges}");
            sb.AppendLine($"  WorstTowerDistance={WorstTowerDistance.ToString("F4", c)}");
            sb.AppendLine($"  TilesMissingWedges={TilesMissingWedges}");
            sb.AppendLine($"  StartSpanPairs={StartSpanPairs} StartSpanFarCentre={StartSpanFarCentre.ToString("F4", c)}");
            sb.AppendLine($"  MaxDrawnTiles={MaxDrawnTiles} MaxTileDraws={MaxTileDraws} MaxDraws={MaxDraws} MaxTriangles={MaxTriangles} WorstPose={WorstPose}");
            sb.AppendLine($"  CoincidentFaces={CoincidentFaces} StructuralConflicts={StructuralConflicts}");
            sb.AppendLine($"  Corridors={Corridors} TwoClosedAxisEdges={CorridorsTwoClosed} OneClosedAxisEdge={CorridorsOneClosed} CorridorKerbsOwned={CorridorKerbs} MissingKerbs={CorridorsMissingKerb}");
            sb.AppendLine($"  RegionMismatches={RegionMismatches} ContentMismatches={ContentMismatches} RegionsPresent={RegionsPresent}");
            sb.AppendLine($"  WorldHash1=0x{WorldHash1:X16} WorldHash2=0x{WorldHash2:X16}");
            foreach (var f in Failures) sb.AppendLine("  FAIL: " + f);
            return sb.ToString();
        }
    }
}
