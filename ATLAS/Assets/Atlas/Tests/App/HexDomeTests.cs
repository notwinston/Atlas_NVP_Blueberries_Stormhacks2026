using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>The tiles of the hexagon reveal: a closed sphere of hexagons and twelve pentagons, a hexagon
    /// straight ahead that turns first, and the rest in order of their angle from it.</summary>
    public class HexDomeTests
    {
        static readonly HexDome s_Dome = new HexDome();

        [Test]
        public void EightyHexagons_AndTwelvePentagons_OnTheUnitSphere()
        {
            int hexagons = 0, pentagons = 0;
            for (int i = 0; i < HexDome.TileCount; i++)
            {
                Assert.That(math.length(s_Dome.Centres[i]), Is.EqualTo(1f).Within(1e-4f));
                int n = s_Dome.Corners[i].Length;
                if (n == 6) hexagons++;
                else if (n == 5) pentagons++;
                else Assert.Fail("tile " + i + " has " + n + " corners");
            }
            Assert.That(hexagons, Is.EqualTo(80));
            Assert.That(pentagons, Is.EqualTo(12));
        }

        [Test]
        public void EveryCorner_IsSharedByThreeTiles_SoTheClosedDomeHasNoGap()
        {
            var uses = new Dictionary<int3, int>();
            for (int i = 0; i < HexDome.TileCount; i++)
            foreach (float3 c in s_Dome.Corners[i])
            {
                var key = (int3)math.round(c * 1000f);
                int n;
                uses.TryGetValue(key, out n);
                uses[key] = n + 1;
            }
            // The dual of the 180 triangles of the subdivided icosahedron: one corner for each.
            Assert.That(uses.Count, Is.EqualTo(180));
            foreach (int n in uses.Values) Assert.That(n, Is.EqualTo(3));
        }

        [Test]
        public void AHexagonStandsStraightAhead_AndTurnsFirst()
        {
            int front = -1;
            for (int i = 0; i < HexDome.TileCount; i++)
                if (math.distance(s_Dome.Centres[i], new float3(0f, 0f, 1f)) < 1e-4f) front = i;
            Assert.That(front, Is.Not.EqualTo(-1));
            Assert.That(s_Dome.Corners[front].Length, Is.EqualTo(6));
            Assert.That(s_Dome.Delays[front], Is.EqualTo(0f).Within(1e-4f));
            for (int i = 0; i < HexDome.TileCount; i++)
            {
                Assert.That(s_Dome.Delays[i], Is.InRange(0f, HexDome.SpreadSeconds));
                if (i != front) Assert.That(s_Dome.Delays[i], Is.GreaterThan(s_Dome.Delays[front]));
            }
        }

        [Test]
        public void Delays_GrowWithTheAngleFromTheGaze()
        {
            float3 forward = math.normalize(new float3(1f, 0.2f, 0f));
            Assert.That(HexDome.Delay(forward, forward), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(HexDome.Delay(-forward, forward), Is.EqualTo(HexDome.SpreadSeconds).Within(1e-3f));
            Assert.That(HexDome.Delay(new float3(0f, 0f, 1f), forward), Is.EqualTo(HexDome.SpreadSeconds * 0.5f).Within(1e-3f));
        }

        [Test]
        public void Axes_LieInTheirTiles_AndAreUnit()
        {
            for (int i = 0; i < HexDome.TileCount; i++)
            {
                Assert.That(math.length(s_Dome.Axes[i]), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(s_Dome.Axes[i], s_Dome.Centres[i]), Is.EqualTo(0f).Within(1e-4f));
            }
        }

        [Test]
        public void TheMesh_IsOneFanPerTile_AtTheRadius()
        {
            int corners = 0;
            for (int i = 0; i < HexDome.TileCount; i++) corners += s_Dome.Corners[i].Length;
            Assert.That(s_Dome.Positions.Count, Is.EqualTo(HexDome.TileCount + corners));
            Assert.That(s_Dome.Triangles.Count, Is.EqualTo(3 * corners));
            Assert.That(s_Dome.VertexAxes.Count, Is.EqualTo(s_Dome.Positions.Count));
            Assert.That(s_Dome.VertexCentres.Count, Is.EqualTo(s_Dome.Positions.Count));
            Assert.That(s_Dome.Rims.Count, Is.EqualTo(s_Dome.Positions.Count));
            foreach (float3 p in s_Dome.Positions) Assert.That(math.length(p), Is.EqualTo(HexDome.Radius).Within(1e-3f));
            foreach (int t in s_Dome.Triangles) Assert.That(t, Is.InRange(0, s_Dome.Positions.Count - 1));
        }

        [Test]
        public void Turn_IsZeroBeforeTheDelay_OneAfterTheFlip_AndEased()
        {
            Assert.That(HexDome.Turn(0.3f, -1f), Is.EqualTo(0f));
            Assert.That(HexDome.Turn(0.3f, 0.3f), Is.EqualTo(0f));
            Assert.That(HexDome.Turn(0.3f, 0.3f + HexDome.FlipSeconds * 0.5f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(HexDome.Turn(0.3f, 0.3f + HexDome.FlipSeconds), Is.EqualTo(1f));
            Assert.That(HexDome.Turn(HexDome.SpreadSeconds, HexDome.RevealSeconds), Is.EqualTo(1f), "the last tile is over when the reveal ends");
        }
    }
}
