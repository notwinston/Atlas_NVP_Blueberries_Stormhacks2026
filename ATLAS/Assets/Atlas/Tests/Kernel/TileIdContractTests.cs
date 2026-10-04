using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Kernel.Tests
{
    /// <summary>Identity, wire form and content seed of tile ids (Master Plan section 6.5, Appendix A.5).</summary>
    [TestFixture]
    public class TileIdContractTests
    {
        [Test]
        public void TileId_Basics()
        {
            TileId origin = TileId.Origin;
            var words = new long[6];
            origin.CopyTo(words);
            Assert.That(words, Is.EqualTo(new long[] { 0, 0, 0, 0, 1, 0 }));
            Assert.That(origin.IsNone, Is.False);
            Assert.That(default(TileId).IsNone, Is.True);
            Assert.That(origin.ToString(), Is.EqualTo("0,0,0,0,1,0"));

            var a = new TileId(1, 2, 3, 4, 5, 6);
            var same = new TileId(1, 2, 3, 4, 5, 6);
            Assert.That(a == same, Is.True);
            Assert.That(a != same, Is.False);
            Assert.That(a.Equals(same), Is.True);
            Assert.That(a.Equals((object)same), Is.True);
            Assert.That(a.Equals("1,2,3,4,5,6"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(same.GetHashCode()));

            // Every one of the six words takes part in the identity.
            for (int word = 0; word < 6; word++)
            {
                a.CopyTo(words);
                Assert.That(a[word], Is.EqualTo(words[word]));
                words[word] += 1;
                TileId other = TileId.FromWords(words);
                Assert.That(a == other, Is.False, "word " + word);
                Assert.That(a != other, Is.True, "word " + word);
                Assert.That(a.Equals(other), Is.False, "word " + word);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => { long unused = a[6]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { long unused = a[-1]; });
            Assert.Throws<ArgumentException>(() => a.CopyTo(new long[5]));
            Assert.Throws<ArgumentException>(() => TileId.FromWords(new long[5]));

            var map = new Dictionary<TileId, int>();
            map[a] = 1;
            map[same] = 2;
            map[origin] = 3;
            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map[new TileId(1, 2, 3, 4, 5, 6)], Is.EqualTo(2));
            Assert.That(map.ContainsKey(new TileId(1, 2, 3, 4, 5, 7)), Is.False);

            var extreme = new TileId(long.MinValue, long.MaxValue, -1, 0, 1, -42);
            foreach (TileId id in new[] { origin, a, extreme, default(TileId) })
            {
                id.CopyTo(words);
                Assert.That(TileId.FromWords(words), Is.EqualTo(id));
                Assert.That(TileId.TryParse(id.ToString(), out TileId parsed), Is.True, id.ToString());
                Assert.That(parsed, Is.EqualTo(id));
            }
            Assert.That(extreme.ToString(), Is.EqualTo("-9223372036854775808,9223372036854775807,-1,0,1,-42"));

            foreach (string bad in new[] { null, "", "0,0,0,0,1", "0,0,0,0,1,0,0", "0,0,x,0,1,0", "0,0,0.5,0,1,0",
                                           "0,0,,0,1,0", "9223372036854775808,0,0,0,1,0" })
            {
                Assert.That(TileId.TryParse(bad, out TileId none), Is.False, bad ?? "null");
                Assert.That(none.IsNone, Is.True);
            }
        }

        // The golden rows of the frame-loop contract (lane-b-kernel.md section 3.4); the {4,5} golden file is TileIdTests.TileId_Seed.
        [TestCase(0L, 0L, 0L, 0L, 1L, 0L, 0u, 0x4194AB7A26ABAF68UL, 1732183058)]
        [TestCase(0L, 0L, 0L, 0L, 1L, 0L, 1u, 0x2D1912F0CC4D7056UL, 1732183058)]
        [TestCase(0L, 0L, 0L, 0L, 1L, 0L, 0x000A71A5u, 0x32B1E53DB78ED428UL, 1732183058)]
        [TestCase(1L, 0L, 0L, 0L, 1L, 0L, 0u, 0x96D0F30FDF01A6F3UL, 1238455804)]
        [TestCase(0L, 0L, 1L, 0L, 1L, 0L, 0u, 0x88576DCBC0B24818UL, 1222976979)]
        [TestCase(-1L, 0L, 2L, 0L, 1L, 0L, 0u, 0x778545E01C883E4DUL, 1796045741)]
        [TestCase(-1L, 0L, 2L, 0L, 1L, 0L, 0x000A71A5u, 0x6C423E6694739896UL, 1796045741)]
        [TestCase(3L, 0L, -4L, 0L, 1L, 0L, 0xFFFFFFFFu, 0x54C68B49A291F26AUL, -485598869)]
        [TestCase(1L, 0L, 0L, 0L, 0L, 1L, 0u, 0xB27898E099B829B2UL, 734048594)]
        [TestCase(0L, 0L, 0L, 0L, 0L, 0L, 0u, 0xB3A523F080E922ECUL, 860619036)]
        [TestCase(9223372036854775807L, -9223372036854775808L, 5L, -7L, 11L, -13L, 12345u, 0x9218621C55847DC5UL, 1192343521)]
        public void TileId_Seed(long xu, long xv, long yu, long yv, long tu, long tv,
                                uint worldSeed, ulong seed, int hash)
        {
            var id = new TileId(xu, xv, yu, yv, tu, tv);
            Assert.That(id.ContentSeed(worldSeed), Is.EqualTo(seed));
            Assert.That(id.GetHashCode(), Is.EqualTo(hash));
            // Deterministic: the same on every call and for an equal id built another way.
            Assert.That(TileId.FromWords(new[] { xu, xv, yu, yv, tu, tv }).ContentSeed(worldSeed), Is.EqualTo(seed));
        }

        [Test]
        public void TileId_SeedSpreads()
        {
            // Within one world no two tiles share a seed, and a tile's seed changes with the world seed.
            // Seeds are compared within a world only: the world seed and the first word enter the fold
            // together, so pairs such as (world 1, X = 1) and (world 0, X = 0) are equal by construction.
            for (uint world = 0; world < 3; world++)
            {
                var seen = new HashSet<ulong>();
                for (long i = -8; i <= 8; i++)
                {
                    for (long j = -8; j <= 8; j++)
                    {
                        TileId cell = FlatTileGraph.Cell(i, j);
                        Assert.That(seen.Add(cell.ContentSeed(world)), Is.True);
                        Assert.That(cell.ContentSeed(world + 1), Is.Not.EqualTo(cell.ContentSeed(world)));
                    }
                }
            }
        }
    }
}
