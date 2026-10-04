using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>The tiles of the hexagon reveal: a closed sphere of hexagons and twelve pentagons, a hexagon
    /// straight ahead, and a ripple that spreads from one tile to its neighbours ring by ring.</summary>
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
        public void AHexagonStandsStraightAhead()
        {
            int front = s_Dome.TileAt(new float3(0f, 0f, 1f));
            Assert.That(math.distance(s_Dome.Centres[front], new float3(0f, 0f, 1f)), Is.LessThan(1e-4f));
            Assert.That(s_Dome.Corners[front].Length, Is.EqualTo(6));
        }

        [Test]
        public void TheFrontHexagon_HasAFlatEdgeOnTop_AndTurnsAboutAPoleThroughItsMiddle()
        {
            int front = s_Dome.TileAt(new float3(0f, 0f, 1f));
            int sideCorners = 0, topCorners = 0;
            foreach (float3 c in s_Dome.Corners[front])
            {
                float angle = math.degrees(math.atan2(c.y, c.x));
                // Corners at 0, 60, 120 ... degrees: two level with the centre (left and right), two pairs level
                // with each other above and below, so the top and the bottom are flat edges.
                Assert.That(math.abs(angle / 60f - math.round(angle / 60f)), Is.LessThan(0.02f));
                if (math.abs(c.y) < 1e-3f) sideCorners++;
                if (c.y > 1e-3f) topCorners++;
            }
            Assert.That(sideCorners, Is.EqualTo(2));
            Assert.That(topCorners, Is.EqualTo(2));
            Assert.That(math.distance(s_Dome.Axes[front], new float3(0f, 1f, 0f)), Is.LessThan(1e-4f), "a vertical pole");
        }

        [Test]
        public void Neighbours_AreSixRoundAHexagon_FiveRoundAPentagon_AndMutual()
        {
            for (int i = 0; i < HexDome.TileCount; i++)
            {
                Assert.That(s_Dome.Neighbours[i].Length, Is.EqualTo(s_Dome.Corners[i].Length));
                foreach (int n in s_Dome.Neighbours[i]) Assert.That(s_Dome.Neighbours[n], Has.Member(i));
            }
        }

        [Test]
        public void TheRipple_StartsWithOneTile_AndEachRingTouchesTheOneBefore()
        {
            var rings = new int[HexDome.TileCount];
            int first = s_Dome.TileAt(new float3(0f, 0f, 1f));
            int outer = s_Dome.Rings(first, rings);

            Assert.That(rings[first], Is.EqualTo(0));
            int zeros = 0;
            foreach (int r in rings) if (r == 0) zeros++;
            Assert.That(zeros, Is.EqualTo(1), "one tile turns alone first");
            for (int i = 0; i < HexDome.TileCount; i++)
            {
                Assert.That(rings[i], Is.InRange(0, outer), "every tile is reached");
                if (i == first) continue;
                // Set off by a neighbour one ring in.
                bool setOff = false;
                foreach (int n in s_Dome.Neighbours[i]) setOff |= rings[n] == rings[i] - 1;
                Assert.That(setOff, Is.True);
            }
            // Gradual: several rings, the furthest straight behind.
            Assert.That(outer, Is.GreaterThanOrEqualTo(5));
            Assert.That(rings[s_Dome.TileAt(new float3(0f, 0f, -1f))], Is.GreaterThanOrEqualTo(outer - 1));
            Assert.That(HexDome.RevealSeconds(outer), Is.InRange(2.5f, 3.5f), "slow enough to watch it spread");
        }

        [Test]
        public void EveryTile_TurnsOnItsVerticalPole_LeftAndRightMirrored()
        {
            var axes = new float3[HexDome.TileCount];
            int first = s_Dome.TileAt(new float3(0f, 0f, 1f));
            s_Dome.WaveAxes(first, axes);
            for (int i = 0; i < HexDome.TileCount; i++)
            {
                float3 n = s_Dome.Centres[i];
                Assert.That(math.abs(math.dot(axes[i], s_Dome.Axes[i])), Is.EqualTo(1f).Within(1e-5f), "its own pole");
                if (math.abs(n.x) < 1e-3f || math.abs(n.y) > 0.5f || n.z < 0.2f) continue;
                // In front, near the horizon: the edge on the side of the middle swings in, towards the walker.
                float3 towardsMiddle = math.normalize(math.cross(s_Dome.Axes[i], n)) * (n.x > 0f ? -1f : 1f);
                Assert.That(math.dot(math.cross(axes[i], towardsMiddle), n), Is.LessThan(0f), "tile " + i);
            }
        }

        [Test]
        public void ANeighbour_StartsBeforeItsSetterHasTurned()
        {
            // The next ring starts partway through the turn of the ring before: a wave, not steps.
            Assert.That(HexDome.Delay(1), Is.LessThan(HexDome.FlipSeconds));
            Assert.That(HexDome.Delay(1), Is.GreaterThan(0f));
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
            Assert.That(HexDome.Turn(HexDome.Delay(8), HexDome.RevealSeconds(8)), Is.EqualTo(1f), "the last tile is over when the reveal ends");
        }
    }
}
