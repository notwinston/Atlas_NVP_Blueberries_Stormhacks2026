using NUnit.Framework;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// WorldHash_Vector (lane D contract, section 2.5): the test vector gives <c>141060c2fc138145</c>, and every
    /// input of the hash is part of it. The phone and the Spectator compare this value to decide whether they
    /// hold the same world.
    /// </summary>
    public sealed class WorldHash_Vector
    {
        const string Geometry = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [Test]
        public void The_contract_vector_gives_141060c2fc138145()
        {
            Assert.That(WorldHash.Text(1, 4, 0, 1, Geometry, 1000), Is.EqualTo("1|4,4|0|1|" + Geometry + "|1000"));
            Assert.That(WorldHash.Compute(1, 4, 0, 1, Geometry, 1000), Is.EqualTo("141060c2fc138145"));
        }

        [Test]
        public void The_hash_is_16_lower_case_hexadecimal_digits_and_repeats()
        {
            string hash = WorldHash.Compute(3, 5, 4000000000L, 1, Geometry, 1885);
            Assert.That(hash.Length, Is.EqualTo(WorldHash.Length));
            Assert.That(hash, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(WorldHash.Compute(3, 5, 4000000000L, 1, Geometry, 1885), Is.EqualTo(hash), "the same world, the same hash");
            Assert.That(WorldHash.Text(3, 5, 4000000000L, 1, Geometry, 1885), Is.EqualTo("3|4,5|4000000000|1|" + Geometry + "|1885"),
                "a seed above the int range is written in full");
        }

        [Test]
        public void Every_input_changes_the_hash()
        {
            string reference = WorldHash.Compute(1, 4, 0, 1, Geometry, 1000);
            Assert.That(WorldHash.Compute(2, 4, 0, 1, Geometry, 1000), Is.Not.EqualTo(reference), "space");
            Assert.That(WorldHash.Compute(1, 5, 0, 1, Geometry, 1000), Is.Not.EqualTo(reference), "q");
            Assert.That(WorldHash.Compute(1, 4, 1, 1, Geometry, 1000), Is.Not.EqualTo(reference), "seed");
            Assert.That(WorldHash.Compute(1, 4, 0, 2, Geometry, 1000), Is.Not.EqualTo(reference), "generator version");
            Assert.That(WorldHash.Compute(1, 4, 0, 1, Geometry.Replace('9', '8'), 1000), Is.Not.EqualTo(reference), "room geometry");
            Assert.That(WorldHash.Compute(1, 4, 0, 1, Geometry, 1001), Is.Not.EqualTo(reference), "L");
        }

        [Test]
        public void No_room_stands_for_64_zeros()
        {
            Assert.That(WorldHash.NoGeometry.Length, Is.EqualTo(64));
            Assert.That(WorldHash.NoGeometry.Trim('0'), Is.Empty);
            string none = WorldHash.Compute(0, 4, 0, 1, WorldHash.NoGeometry, 1000);
            Assert.That(WorldHash.Compute(0, 4, 0, 1, null, 1000), Is.EqualTo(none));
            Assert.That(WorldHash.Compute(0, 4, 0, 1, string.Empty, 1000), Is.EqualTo(none));
            Assert.That(WorldHash.Text(0, 4, 0, 1, null, 1000), Is.EqualTo("0|4,4|0|1|" + WorldHash.NoGeometry + "|1000"));
            Assert.That(none, Is.Not.EqualTo(WorldHash.Compute(0, 4, 0, 1, Geometry, 1000)));
        }

        [Test]
        public void Millimetres_round_half_away_from_zero()
        {
            Assert.That(WorldHash.Millimetres(1.0), Is.EqualTo(1000));
            Assert.That(WorldHash.Millimetres(1.885), Is.EqualTo(1885));
            Assert.That(WorldHash.Millimetres(1.8854), Is.EqualTo(1885));
            Assert.That(WorldHash.Millimetres(1.8856), Is.EqualTo(1886));
            Assert.That(WorldHash.Millimetres(0.0625), Is.EqualTo(63), "62.5 goes up, not to the even 62");
            Assert.That(WorldHash.Millimetres(0.0), Is.EqualTo(0));
        }
    }
}
